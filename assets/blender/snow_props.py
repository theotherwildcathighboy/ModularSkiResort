"""
snow_props.py — piste-edge snowmaking props (generation A).

    blender --background --factory-startup --python-exit-code 1 \
            --python snow_props.py -- --out ../import/props/A --variants
    blender --background --factory-startup --python snow_props.py -- --lineup ../preview
    blender --background --factory-startup --python snow_props.py -- --row ../preview
    blender --background --factory-startup --python-exit-code 1 --python snow_props.py -- \
            --detail ../preview/snow-props-2026-10-16 --review     # T20 preview gate, no FBX

GENERATION B (T20 detail pass, 2026-10-16) — docs/research/art/snow-props-detail-2026-10-16.md.
The four snowmaking assets are B (lathe fan head with an open nozzle ring, yoke + trunnions,
work basket the ladder actually reaches, U-cage of flat-bar hoops, hinged lance, coiled
hydrant hose, part-tagged LOD chains, smooth shading), then the de-CAD pass (chamfered boxes,
SOFT_PREFIXES shading, rolled bell lip, cast feet, folded-sheet cabinets, wheel profiles;
measured with cad_metric). The folded lance is dropped. The floodlight is still A.

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
     normalized (area-prop-variation.md C2), so which side the piste sits on flips with
     the direction the player drew the run. A perpendicular barrel fires into the trees on
     half of all runs. Guns get GunParams.yaw_deg of 15-30 off the border tangent, which
     reads plausibly either way.
  3. BASE-PIVOTED, origin at ground level on the machine's own footprint center. Geometry
     below z=0 is treated as underground and clipped (the lesson lift_chair paid for).

TEXTURE BUDGET — the top risk in the pipeline plan. quad_size is picked per asset from its
physical size, targeting 60-150 px/m; copying the towers' 1024 default would cost ~112 MB
for this family instead of ~9 MB, on a project already overspending an order of magnitude.
atlas_size is ALWAYS derived as 2 x quad_size, never written as a literal — a mismatch
silently corrupts the AO rebake and only shows up in game.
REDO (wiki style, 2026-10-17): the 60-150 px/m band above is SUPERSEDED for the snowmaking four.
With --repack the sizes are TEX_REDO (towers / fans / lances 1024, hydrants 512: the wiki's 512
floor; `--tex B` restores the B sizes) and density_packer unfolds, weights and shelf-packs the
islands with mip-safe margins (docs/research/art/snow-props-weather-2026-10-17.md "Redo").
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
# plastic, quad 3 into livery paint (recolor slot 1) and accent paint (slot 3).
#
# Two material bugs the materials plan caught in the first draft, fixed here:
#   * tires, castor, hoses and the fan intake were on the GLASS quad at 0.92 smoothness —
#     mirror-finish rubber. They are now on `rubber` at 0.18.
#   * the orange hydrant lid and handwheel were on recolor slot 1, so the safety cap
#     would have recolored with the manufacturer livery. They are now on `plastic`,
#     which is slot 2 (safety orange) — a separate picker.
Z_CONCRETE = "concrete"   # pads, plinths
Z_GALV     = "galv"       # masts, frames, ladders, hardware
Z_LIVERY   = "livery"     # housing paint, recolor slot 1
Z_ACCENT   = "accent"     # chassis/trim paint, recolor slot 3
Z_RUBBER   = "rubber"     # tires, hoses, conduit
Z_GRATE    = "grate"      # intake mesh, platform grating, louvres
Z_PLASTIC  = "plastic"    # safety plastic, recolor slot 2 (orange)
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
    "tower":   (0.50, 0.20),
    "fan":     (0.50, 0.20),
    "lance":   (),
    "hydrant": (),
    "flood":   (0.42, 0.12),
}

# T20: delete-then-decimate for the guns (art-pipeline-v2 section 5, "Snow props B" row).
# Collapse decimation alone turns rungs, cage bars and nozzle studs into the chunky shards
# A shipped at LOD1/LOD2; deleting whole parts by name first keeps the silhouette parts
# clean and spends the decimation on the big shells. Prefixes are cumulative per level.
LOD_DELETE = {
    "tower": (("HeadStud", "HeadBolt", "Rung", "FootNut", "Clamp", "Standoff", "Knee", "Kick",
               "Hose", "CageBar", "Gland", "Elbow", "Hinge", "Handle", "Louvre", "CabDoor"),
              ("HeadStud", "HeadBolt", "Rung", "FootNut", "Clamp", "Standoff", "Knee", "Kick",
               "Hose", "CageBar", "HeadSpinner", "Hinge", "Handle", "Louvre", "CabDoor",
               "CageHoop", "Stile", "Rail", "Brace", "Flange", "Conduit", "Riser", "HeadTrunnion",
               "HeadBore", "HeadFan", "HeadGuard", "Beacon", "BasePlate", "MastCollar",
               "MastFoot", "CabLid", "HeadBlades", "Gland", "Elbow")),
    "fan":   (("GunStud", "GunNuc", "GunBolt", "GunValve", "Feed", "Coil", "Hose", "HubCap",
               "HubIn", "JackFoot", "Elbow", "Axle", "Hinge", "Handle", "Louvre", "CabDoor"),
              ("GunStud", "GunNuc", "GunBolt", "GunValve", "Feed", "Coil", "Hose", "HubCap",
               "HubIn", "JackFoot", "GunSpinner", "Axle", "Hinge", "Handle", "Louvre", "CabDoor",
               "GunManifold", "Jack", "Castor", "GunTrunnion", "GunBore", "GunFan", "GunGuard",
               "Turntable", "Tank", "Hub", "PedFoot", "CabLid", "GunBlades", "Elbow")),
}


def make_lods(key, obj, name):
    """
    LOD1..n objects for `obj` (caller exports and removes them). Guns with a LOD_DELETE
    recipe go through cs2_asset.make_lod_chain; each level's target is the ratio x LOD0,
    or what is left after the deletion when that is already lower (then no decimation).
    Assets without the recipe keep the A path (plain ratio decimate).
    """
    ratios = LOD_CHAIN.get(key, ())
    if not ratios:
        return []
    if key not in LOD_DELETE or cs2_asset.PART_TABLE_PROP not in obj.data:
        return [lift_tower.make_lod(obj, r, f"{name}_LOD{i}") for i, r in enumerate(ratios, 1)]
    import json
    table = json.loads(obj.data[cs2_asset.PART_TABLE_PROP])
    lod0 = tri_count(obj)
    attr = obj.data.attributes["part"].data
    targets = []
    for ratio, prefixes in zip(ratios, LOD_DELETE[key]):
        left = sum(len(poly.vertices) - 2 for poly in obj.data.polygons
                   if not table[attr[poly.index].value - 1].startswith(prefixes))
        targets.append(min(int(round(ratio * lod0)), left))
    return cs2_asset.make_lod_chain(obj, name, targets, LOD_DELETE[key])

# Parts that take the finishing-pass bevel — the BOXY ones only, same rule as
# lift_tower.BEVEL_PREFIXES. Cylinders already read round; thin bars (rails, rungs, ribs,
# clamps, jack feet) are too small to catch a specular line and the bevel would cost more
# tris than it returns. Razor-sharp box edges are the single biggest procedural tell
# against vanilla assets, so the parts a player can actually see a corner on get 0.02 m.
BEVEL_PREFIXES = ("Pad", "BasePlate", "Cabinet", "Platform", "Body", "Lid",
                  "Compressor", "DriveHousing", "PushPlate",
                  # T20 de-CAD (generation B): every chunky box gets a real chamfer
                  "HeadCompressor")

# T20 de-CAD: thin bars, plates, pins and small capped cylinders are SOFT-shaded instead
# of bevelled: no sharp edges, face-area weighted normals, so a 2.5 cm rung reads as the
# round bar it is and a pin cap reads domed, for zero triangles. (A geometric bevel on
# the ~60 bars of a tower would cost ~1,000 tris against an 1,800 cap.)
SOFT_PREFIXES = ("Rung", "Rail", "Knee", "Kick", "Brace", "Stile", "Standoff", "CageBar",
                 "Clamp", "HeadFork", "GunFork", "HeadTrunnion", "GunTrunnion", "Chassis",
                 "Cross", "AxleArm", "Axle", "CastorFork", "Castor", "Cheek", "Rest", "Cradle",
                 "Louvre", "Hinge", "Handle", "JackFoot", "Jack", "Floor", "CabDoor",
                 "NumberPlate", "Pin", "Valve", "Conduit", "Marker", "Beacon", "Tank",
                 "Outlet", "Stub", "NucHead", "HeadCap", "Wheel", "SlewDrive", "Turntable",
                 "HeadStud", "GunStud", "GunNuc", "HeadBolt", "GunBolt", "FootNut", "HubCap",
                 "HubIn", "ColumnTop", "CabLid", "Deck", "SensorBox", "Skid", "HeadFan", "GunFan",
                 "HeadGuard", "GunGuard", "HeadBore", "GunBore", "Pedestal", "GunValve",
                 "GunManifold", "Lance", "Coil", "HoseCoil", "Hose", "HoseLead", "Feed", "Riser",
                 "Gland", "HeadSpinner", "GunSpinner")

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


def add_tube(name, radius, depth, location, rotation=(0, 0, 0), segments=8, caps=True):
    bpy.ops.mesh.primitive_cylinder_add(vertices=segments, radius=radius, depth=depth,
                                        location=location, rotation=rotation,
                                        end_fill_type="NGON" if caps else "NOTHING")
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


# --------------------------------------------------------------------------------------
# Detail-pass primitives (T20, 2026-10-16). Built from raw verts so every face is one we
# asked for: no hidden end caps. The A-generation cage hoops were primitive_cylinder rings
# whose filled caps rendered as SOLID DISCS around the mast, which is what read in game as
# "the ladder goes through the platforms" (docs/research/art/snow-props-detail-2026-10-16.md).
# Each face carries a wanted direction and is flipped to match it, so open shapes (bands,
# ribbons, bores) are wound correctly without relying on recalc_face_normals.
# --------------------------------------------------------------------------------------

def _mk(name, verts, faces):
    """faces = [(vertex index list, wanted direction Vector)]; returns a linked object."""
    out = []
    for idx, want in faces:
        n = mathutils.geometry.normal([mathutils.Vector(verts[i]) for i in idx])
        if want is not None and n.dot(want) < 0.0:
            idx = list(reversed(idx))
        out.append(tuple(idx))
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in verts], [], out)
    me.update()
    o = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(o)
    return o


def add_lathe(name, profile, bands, matrix, segments=16):
    """
    Revolve (x, r) profile points about local +X, then place with `matrix`. `bands[i]`
    says which way band i (points i -> i+1) faces: 'out' / 'in' (away from / toward the
    axis) or '+x' / '-x' (along the axis). No caps: discs are separate parts.
    """
    verts = []
    for x, r in profile:
        for j in range(segments):
            a = 2.0 * math.pi * j / segments
            verts.append(mathutils.Vector((x, r * math.cos(a), r * math.sin(a))))
    faces = []
    for i, mode in enumerate(bands):
        for j in range(segments):
            j2 = (j + 1) % segments
            idx = [i * segments + j, (i + 1) * segments + j,
                   (i + 1) * segments + j2, i * segments + j2]
            c = sum((verts[k] for k in idx), mathutils.Vector()) / 4.0
            radial = mathutils.Vector((0.0, c.y, c.z)).normalized()
            want = {"out": radial, "in": -radial,
                    "+x": mathutils.Vector((1, 0, 0)), "-x": mathutils.Vector((-1, 0, 0))}[mode]
            faces.append((idx, want))
    o = _mk(name, verts, faces)
    o.data.transform(matrix)
    o.data.update()
    return o


def add_disc(name, x, r, facing, matrix, segments=16):
    """One n-gon disc in the local YZ plane at `x`, facing +X (facing=1) or -X (-1)."""
    verts = [(x, r * math.cos(2 * math.pi * j / segments), r * math.sin(2 * math.pi * j / segments))
             for j in range(segments)]
    o = _mk(name, verts, [(list(range(segments)), mathutils.Vector((facing, 0, 0)))])
    o.data.transform(matrix)
    o.data.update()
    return o


def add_spinner(name, x, r, h, matrix, segments=8):
    """Open cone (no base) from radius r at local x to an apex at x + h: the fan hub."""
    verts = [(x, r * math.cos(2 * math.pi * j / segments), r * math.sin(2 * math.pi * j / segments))
             for j in range(segments)] + [(x + h, 0.0, 0.0)]
    faces = []
    for j in range(segments):
        a, b = verts[j], verts[(j + 1) % segments]
        want = mathutils.Vector((0.3, (a[1] + b[1]) * 0.5, (a[2] + b[2]) * 0.5))
        faces.append(([j, (j + 1) % segments, segments], want))
    o = _mk(name, verts, faces)
    o.data.transform(matrix)
    o.data.update()
    return o


def add_bar(name, p0, p1, w, h, up=None):
    """
    Box-section bar from p0 to p1 WITHOUT end caps (8 tris): rungs, rails, posts, braces,
    stiles. `w` is measured along the side axis (d x up), `h` along the bar's own up.
    Ends always butt into something (a stile, a post, the mast), so caps would be hidden.
    """
    a, b = mathutils.Vector(p0), mathutils.Vector(p1)
    d = (b - a).normalized()
    ref = mathutils.Vector(up) if up is not None else mathutils.Vector((0, 0, 1))
    if abs(d.dot(ref.normalized())) > 0.95:
        ref = mathutils.Vector((1, 0, 0))
    side = d.cross(ref).normalized()
    upv = side.cross(d).normalized()
    corners = [(+1, +1), (-1, +1), (-1, -1), (+1, -1)]
    verts = [p + side * (sx * w * 0.5) + upv * (sy * h * 0.5)
             for p in (a, b) for sx, sy in corners]
    faces = []
    for k in range(4):
        k2 = (k + 1) % 4
        want = side * (corners[k][0] + corners[k2][0]) + upv * (corners[k][1] + corners[k2][1])
        faces.append(([k, k2, 4 + k2, 4 + k], want))
    return _mk(name, verts, faces)


def add_ribbon(name, pts, z, height, thick):
    """
    Vertical flat bar following a horizontal polyline (cage hoops): an inner and an outer
    face per segment, 4 tris, no top/bottom (10 mm of edge never reads).
    """
    P = [mathutils.Vector((x, y, 0.0)) for x, y in pts]
    offs = []
    for i in range(len(P)):
        t = (P[min(i + 1, len(P) - 1)] - P[max(i - 1, 0)]).normalized()
        offs.append(mathutils.Vector((t.y, -t.x, 0.0)) * (thick * 0.5))
    verts = []
    for p, o in zip(P, offs):
        for s in (+1, -1):
            for dz in (-height * 0.5, height * 0.5):
                verts.append(p + o * s + mathutils.Vector((0, 0, z + dz)))
    faces = []
    for i in range(len(P) - 1):
        a, b = 4 * i, 4 * (i + 1)
        n = offs[i] + offs[i + 1]
        faces.append(([a, b, b + 1, a + 1], n))
        faces.append(([a + 2, b + 2, b + 3, a + 3], -n))
    return _mk(name, verts, faces)


def add_stud(name, base, axis, size, height, roll=0.0):
    """Triangular nozzle stud standing on a surface along `axis` (7 tris, no base)."""
    ax = mathutils.Vector(axis).normalized()
    u = ax.orthogonal().normalized()
    v = ax.cross(u)
    base = mathutils.Vector(base)
    ring = []
    for k in range(3):
        a = roll + 2.0 * math.pi * k / 3.0
        ring.append(u * (math.cos(a) * size * 0.5) + v * (math.sin(a) * size * 0.5))
    verts = [base + r for r in ring] + [base + r * 0.7 + ax * height for r in ring]
    faces = [([3, 4, 5], ax)]
    for k in range(3):
        k2 = (k + 1) % 3
        faces.append(([k, k2, 3 + k2, 3 + k], ring[k] + ring[k2]))
    return _mk(name, verts, faces)


def add_blades(name, matrix, x, r0, r1, n, R, pitch_deg=35.0, thick=0.004):
    """
    n flat fan blades in the local YZ plane at local x, root radius r0, tip r1, pitched
    pitch_deg about their own radial axis; chord 0.12 R at the root widening to 0.20 R at the
    tip. Two faces per blade, `thick` apart, no edge faces (4 tris a blade).
    """
    p = math.radians(pitch_deg)
    verts, faces = [], []
    for i in range(n):
        a = 2.0 * math.pi * i / n
        rad = mathutils.Vector((0.0, math.cos(a), math.sin(a)))
        tan = mathutils.Vector((0.0, -math.sin(a), math.cos(a)))
        chord = tan * math.cos(p) + mathutils.Vector((1.0, 0.0, 0.0)) * math.sin(p)
        nrm = rad.cross(chord).normalized()
        base = mathutils.Vector((x, 0.0, 0.0))
        quad = [base + rad * r0 - chord * (0.06 * R), base + rad * r1 - chord * (0.10 * R),
                base + rad * r1 + chord * (0.10 * R), base + rad * r0 + chord * (0.06 * R)]
        for s in (+1.0, -1.0):
            k = len(verts)
            verts += [q + nrm * (s * thick / 2.0) for q in quad]
            faces.append(([k, k + 1, k + 2, k + 3], nrm * s))
    o = _mk(name, verts, faces)
    o.data.transform(matrix)
    o.data.update()
    return o


def add_torus(name, centre, R, r, segments=10, sides=3):
    """Low torus lying flat (hose coils): segments x sides quads."""
    c = mathutils.Vector(centre)
    verts = []
    for i in range(segments):
        a = 2.0 * math.pi * i / segments
        rad = mathutils.Vector((math.cos(a), math.sin(a), 0.0))
        for k in range(sides):
            b = 2.0 * math.pi * k / sides + math.pi / 2.0
            verts.append(c + rad * (R + r * math.cos(b)) + mathutils.Vector((0, 0, r * math.sin(b))))
    faces = []
    for i in range(segments):
        i2 = (i + 1) % segments
        for k in range(sides):
            k2 = (k + 1) % sides
            idx = [i * sides + k, i2 * sides + k, i2 * sides + k2, i * sides + k2]
            ctr = sum((verts[q] for q in idx), mathutils.Vector()) / 4.0
            a = 2.0 * math.pi * (i + 0.5) / segments
            ring_c = c + mathutils.Vector((math.cos(a), math.sin(a), 0.0)) * R
            faces.append((idx, ctr - ring_c))
    return _mk(name, verts, faces)


def add_hose(name, pts, radius, segments=5):
    """Open tube (no caps) through a polyline: returns one part per segment."""
    out = []
    for i in range(len(pts) - 1):
        o = add_tube(f"{name}{i}", radius, (mathutils.Vector(pts[i + 1]) -
                                            mathutils.Vector(pts[i])).length + 0.03,
                     tuple((mathutils.Vector(pts[i]) + mathutils.Vector(pts[i + 1])) / 2.0),
                     segments=segments, caps=False)
        o.rotation_euler = (mathutils.Vector(pts[i + 1]) -
                            mathutils.Vector(pts[i])).to_track_quat("Z", "Y").to_euler()
        out.append(o)
    return out


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
    # T20: 1.20 pad per snow-gun-anatomy.md A1 #1 (was 0.80, which left the cabinet
    # hanging off the pad edge and no room for the ladder foot).
    pad: tuple = (1.20, 1.20, 0.25)  # concrete pad L,W,H
    base_plate: tuple = (0.60, 0.60, 0.05)
    # Head proportions, T20: TR10 / TF10 photographs (close-up + summer-slope refs) read a
    # housing ~1.5x as long as it is wide, a flared rear intake bell, and a ring collar a
    # step wider than the body. Was 1.25 long on a 0.90 cone = a bucket.
    head_len: float = 1.45
    head_r_muzzle: float = 0.44      # body front radius; collar = 1.10x -> 0.97 dia
    head_r_throat: float = 0.39      # body rear radius; bell lip = 1.12x
    pitch_deg: float = 22.0          # nose-up
    yaw_deg: float = 22.0            # off the border tangent — see convention 2
    # Work basket (SMI Denali pattern, snow-gun-anatomy.md C1/C3): the DECK sits 1.5 m
    # under the mast top so a standing worker's hands reach the yoke, not 72 % up the mast
    # where the A generation put it (2.6 m below the head on the Tall: out of reach).
    deck_below_top: float = 1.50
    deck: tuple = (0.80, 0.90)       # x (fore-aft) x y; x starts just past the ladder
    deck_x0: float = -0.24           # deck edge on the ladder side (ladder plane -0.30)
    rail_h: float = 1.10             # EN ISO 14122-3 top rail
    ladder_x: float = -0.30          # rung centerline: 0.19 m clear of the mast skin
    ladder_w: float = 0.40           # stile spacing (anatomy C3)
    cabinet: tuple = (0.60, 0.26, 0.95)   # W(lateral) x D x H — compressor/control cabinet
    cabinet_off: float = 0.45
    nozzles: int = 12                # 24 real (TF10); 12 studs + the open annulus read as a ring


@dataclass
class LanceParams:
    """TechnoAlpin TL8: 4.50 / 7.17 / 10.06 m working heights. Default 7.0."""
    height: float = 7.0
    pipe_r: float = 0.030            # 0.06 dia aluminum tube
    # Anatomy study corrects the lean: real working lances sit at 25-40 degrees off
    # vertical, not the 15 the first spec assumed.
    lean_deg: float = 30.0
    pipe_len: float = 0.0            # 0 = reach `height`; >0 = fixed length (folded pose)
    head_len: float = 0.26
    head_r: float = 0.050
    column_r: float = 0.10           # 0.20 dia fixed column (anatomy #40)
    column_h: float = 0.80
    base_plate: tuple = (0.40, 0.40, 0.06)
    pad: tuple = (0.70, 0.70, 0.18)
    jacket: tuple = (0.24, 0.16, 0.50)    # orange base jacket: r bottom, r top, height
    stub_len: float = 0.35           # pipe tail behind the pivot = the water inlet
    compressor_r: float = 0.26       # TechnoAlpin-style drum compressor at the base
    compressor_l: float = 0.48
    with_compressor: bool = True
    with_rest: bool = False          # (B1 folded pose, superseded by `laid`)
    laid: bool = False               # summer pose: swung flat onto a low cradle (T20 verdict 2)
    cradle_h: float = 0.22           # cradle saddle height under the tube


@dataclass
class FanParams:
    """TechnoAlpin TF10 Manual: 2.70 L (incl. drawbar) x 1.55 W x 2.55 H, turbine 0.90."""
    barrel_len: float = 1.60         # T20: TR10 close-up reads ~1.5x the body diameter
    barrel_r_muzzle: float = 0.50    # collar 1.10x -> 1.10 dia
    barrel_r_throat: float = 0.45
    barrel_z: float = 1.65           # centerline height
    pitch_deg: float = 30.0
    yaw_deg: float = 20.0            # see convention 2
    wheel_r: float = 0.30
    wheel_w: float = 0.20
    track_w: float = 1.20            # between rail centers; wheels sit outboard
    frame_w: float = 0.06
    drawbar_len: float = 0.90
    castor_r: float = 0.10
    jack_h: float = 0.35
    cabinet: tuple = (0.40, 0.25, 0.55)
    nozzles: int = 12                # T20 de-CAD budget: 12 studs + the open annulus
    nucleators: int = 0              # (6 in B1; the inner row did not read and cost 42 tris)
    tank_r: float = 0.15             # the blue accent water module


@dataclass
class FloodlightParams:
    """
    Piste floodlight for night skiing (docs/research/art/piste-floodlight-spec.md).
    Dimensions from dimensioned German catalog drawings (megamast.de 12 m mast).

    THE DESIGN GOAL IS TELLING IT APART FROM THE SNOW-GUN TOWER AT A GLANCE, and the
    spec's answer is INVERTED MASS DISTRIBUTION: this mast's widest point is its CROWN
    (a 1.5 m cross-arm carrying three heads at 12 m) over a completely BARE tapered
    shaft; the snow gun's widest point is its BASE (cabinet, standpipe, hoses, orange
    box) under a busy shaft (platform + caged ladder). The bare shaft is authentic, not
    a shortcut — real piste masts have no ladder and no platform, only an 85 x 400 mm
    base access door.
    """
    height: float = 12.0             # light point height; variants 8 / 12 / 16
    # Taper rule that holds at any height: ~10 mm of diameter per meter of mast.
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
    coil_r: float = 0.30             # T20: the locale says "with a coiled hose"; A laid
    with_hose: bool = True           # a straight stub instead


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


def add_vlathe(name, centre, profile, bands, segments=12):
    """Vertical lathe: profile = [(z offset, r)] about a vertical axis through `centre`
    (bands as add_lathe: 'out' / 'in' radial, '+x' = up, '-x' = down)."""
    M = (mathutils.Matrix.Translation(centre) @
         mathutils.Matrix.Rotation(math.radians(-90.0), 4, "Y"))
    return add_lathe(name, profile, bands, M, segments=segments)


def add_foot(name, centre, r_flange, r_top, height, segments=12):
    """Cast foot (T20 de-CAD): a flange rim, a sloped shoulder and a fillet into the tube
    it carries; open at both ends (the plate below and the tube above close it)."""
    return add_vlathe(name, centre, [(0.0, r_flange), (0.03, r_flange), (0.07, r_flange * 0.62),
                                     (height, r_top)], ["out", "out", "out"], segments)


def add_nuts(prefix, centre, r, z, n, size=0.034, phase=0.0):
    """Hex-nut stand-ins on a flange: n three-sided studs (7 tris each)."""
    out = []
    for i in range(n):
        a = phase + 2.0 * math.pi * i / n
        out.append(add_stud(f"{prefix}{i}", (centre[0] + r * math.cos(a),
                                             centre[1] + r * math.sin(a), z),
                            (0, 0, 1), size, size * 0.8, roll=a))
    return out


def add_cabinet(name, size, centre, z0, facing, zone, door=True, louvres=True,
                lid_zone=Z_GALV):
    """
    Folded-sheet cabinet (T20 de-CAD; "a folded-sheet look, door seam, hinges,
    louvre band, slight overhang"): a chamfered body, a lid that overhangs on all sides,
    a door leaf standing 5 mm proud (its outline is the seam) with two hinge barrels on one
    edge and a handle off-center on the other, and a band of louvre slats low on the side.
    size = (depth along `facing`, width, height); facing = +-1 along X (door side).
    Returns [(obj, zone)].
    """
    d, w, h = size
    cx, cy = centre
    parts = [(add_box(name, (d, w, h), (cx, cy, z0 + h / 2.0)), zone),
             (add_box(f"CabLid{name}", (d + 0.05, w + 0.05, 0.03),
                      (cx + facing * 0.012, cy, z0 + h + 0.012)), lid_zone)]
    fx = cx + facing * (d / 2.0)
    if door:
        parts.append((add_box(f"CabDoor{name}", (0.012, w - 0.08, h - 0.10),
                              (fx + facing * 0.002, cy, z0 + h / 2.0 + 0.01)), zone))
        hy = cy - (w / 2.0 - 0.045)
        for k, zf in enumerate((0.25, 0.75)):
            hz = z0 + 0.06 + zf * (h - 0.12)
            parts.append((add_tube(f"Hinge{name}{k}", 0.012, 0.07,
                                   (fx + facing * 0.010, hy, hz), segments=6, caps=False),
                          Z_GALV))
        # handle off-center, on the opening edge (asymmetry is the point)
        ry = cy + (w / 2.0 - 0.07)
        parts.append((add_bar(f"Handle{name}", (fx + facing * 0.022, ry, z0 + h * 0.52),
                              (fx + facing * 0.022, ry, z0 + h * 0.52 + 0.11), 0.016, 0.016),
                      Z_GALV))
    if louvres:
        sy = cy + w / 2.0                                # louvres on the +y side face
        for k in range(3):
            z = z0 + 0.10 + 0.05 * k
            parts.append((add_bar(f"Louvre{name}{k}", (cx - d * 0.32, sy + 0.004, z),
                                  (cx + d * 0.32, sy + 0.004, z), 0.012, 0.025), Z_GRATE))
    return parts


def body_radius_at(x, length, r_muzzle, r_throat):
    """Housing radius at local x (the straight body between the bell waist and the collar)."""
    xa, xb = -length / 2.0 + 0.16 * length, length / 2.0 - 0.13 * length
    ra, rb = r_throat * 0.98, r_muzzle
    t = min(max((x - xa) / (xb - xa), 0.0), 1.0)
    return ra + (rb - ra) * t


def build_gun_head(prefix, origin, r_muzzle, r_throat, length, pitch_deg, yaw_deg,
                   nozzles, nucleators=0, scale=1.0, manifold=False, compressor=False,
                   segments=14, ring_bolts=4, bell_segments=8, blades=7):
    """
    Barrel along +X before aiming, then pitched nose-up and yawed off the border.
    Returns (parts, anchors); origin is the barrel's center (the trunnion axis).

    T20 detail pass (snow-props-detail-2026-10-16.md). The A head was a 16-sided cone with
    FILLED caps: from the front it read as a flat lid with studs on it, never as a gun.
    One lathe profile now carries what the TR10 / TF10 photographs show: a flared rear
    INTAKE BELL closed by a dark guard disc, a straight BODY ~1.5x as long as it is wide,
    the NOZZLE-RING COLLAR a step wider than the body (open annulus with the nozzle studs),
    a dark BORE with the FAN disc and a hub SPINNER.
    De-CAD (second verdict, "straight outta CAD"): no 90-degree profile corners left. The
    bell ends in a ROLLED LIP, the collar step is a chamfer and its front edge is rounded
    into the annulus, which itself turns into the bore over a small radius; bolt heads
    ring the annulus between the nozzles (fasteners where the eye lands).
    """
    parts = []
    L, R, Rt = length, r_muzzle, r_throat
    xr, xf = -L / 2.0, L / 2.0
    xw, xc = xr + 0.16 * L, xf - 0.13 * L          # bell waist, collar step
    xbore = xf - 0.35 * L                          # fan plane
    centre = mathutils.Vector(origin)
    fwd, side, perp = gun_axes(pitch_deg, yaw_deg)
    M = (mathutils.Matrix.Translation(centre) @
         mathutils.Euler((0.0, -math.radians(pitch_deg), math.radians(yaw_deg)),
                         "XYZ").to_matrix().to_4x4())
    seg = segments

    # Rear intake bell on its own, coarser lathe (verdict 3: "find the tris on something the
    # eye does not land on - the rear bell, not the ring"); the body starts 12 mm inside the
    # bell waist so the two segment counts never meet edge to edge.
    parts.append((add_lathe(f"{prefix}Bell",
                            [(xr + 0.04, Rt * 0.88), (xr, Rt * 1.00), (xr + 0.01, Rt * 1.10),
                             (xr + 0.03, Rt * 1.125), (xr + 0.07, Rt * 1.09), (xw, Rt * 0.98)],
                            ["-x", "out", "out", "out", "out"], M, segments=bell_segments), Z_LIVERY))
    parts.append((add_lathe(f"{prefix}Housing", [(xw - 0.012, Rt * 0.965), (xc, R)], ["out"], M,
                            segments=seg), Z_LIVERY))
    parts.append((add_lathe(f"{prefix}Collar",
                            [(xc, R), (xc + 0.012, R * 1.09), (xf - 0.016, R * 1.10),
                             (xf, R * 1.065), (xf, R * 0.78)],
                            ["-x", "out", "out", "+x"], M, segments=seg), Z_GALV))
    parts.append((add_lathe(f"{prefix}Bore", [(xf, R * 0.78), (xbore, R * 0.78)], ["in"], M,
                            segments=seg), Z_GRATE))
    parts.append((add_disc(f"{prefix}Fan", xbore, R * 0.78, +1, M, segments=seg), Z_GRATE))
    parts.append((add_spinner(f"{prefix}Spinner", xbore, R * 0.24, 0.22 * scale, M), Z_GALV))
    parts.append((add_disc(f"{prefix}Guard", xr + 0.04, Rt * 0.88, -1, M, segments=bell_segments),
                  Z_GRATE))
    # Fan blades (verdict 3; "close up of fan gun ... fan blades": a hub and a ring of flat,
    # pitched blades seen through the nozzle ring). One part, each blade a two-sided pitched
    # plate 4 mm thick, root on the spinner, tip 4 % short of the bore. 4 tris a blade.
    parts.append((add_blades(f"{prefix}Blades", M, xbore + 0.05 * scale, R * 0.17, R * 0.74,
                             blades, R), Z_GALV))

    # Nozzle studs on the collar face (the TR10 close-up: a ring of nozzle bodies on the
    # aluminum annulus), the inner nucleator row, and bolt heads between the nozzles.
    for i in range(nozzles):
        a = 2.0 * math.pi * i / nozzles
        base = M @ mathutils.Vector((xf, R * 0.93 * math.cos(a), R * 0.93 * math.sin(a)))
        parts.append((add_stud(f"{prefix}Stud{i}", base, fwd, 0.036 * scale, 0.05 * scale,
                               roll=a), Z_GALV))
    for i in range(nucleators):
        a = 2.0 * math.pi * (i + 0.5) / nucleators
        base = M @ mathutils.Vector((xf, R * 0.84 * math.cos(a), R * 0.84 * math.sin(a)))
        parts.append((add_stud(f"{prefix}Nuc{i}", base, fwd, 0.026 * scale, 0.035 * scale,
                               roll=a), Z_GALV))
    for i in range(ring_bolts):
        a = 2.0 * math.pi * (i + 0.37) / ring_bolts
        base = M @ mathutils.Vector((xf, R * 1.025 * math.cos(a), R * 1.025 * math.sin(a)))
        parts.append((add_stud(f"{prefix}Bolt{i}", base, fwd, 0.022 * scale, 0.014 * scale,
                               roll=a), Z_GALV))

    anchors = {"centre": centre, "rc": body_radius_at(0.0, L, R, Rt),
               "fwd": fwd, "side": side, "perp": perp, "hose": None}

    if compressor:
        # Compressor / water module slung under the housing, a second color — the yellow
        # box under the white TF10 in the "fan gun on tower arm" photograph.
        rcm = body_radius_at(0.16 * L, L, R, Rt)
        cpos = centre + fwd * (0.16 * L) - perp * (rcm + 0.13)
        box = add_box(f"{prefix}Compressor", (0.40, 0.34, 0.24), (0, 0, 0))
        box.rotation_euler = mathutils.Matrix((fwd, perp.cross(fwd), perp)).transposed().to_euler()
        box.location = cpos
        parts.append((box, Z_ACCENT))
        anchors["hose"] = cpos - perp * 0.12 - fwd * 0.18

    if manifold:
        # Valve bank on the lower flank of the barrel (reference "fan gun blowing snow on
        # ground blue"). On the flank, not dead under: dead under it ran into the yoke.
        mdir = (-perp * 0.75 - side * 0.66).normalized()
        base = centre + mdir * (anchors["rc"] + 0.06)
        parts.append((add_link(f"{prefix}ManifoldPipe",
                               tuple(base - fwd * (L * 0.16)), tuple(base + fwd * (L * 0.28)),
                               0.032 * scale, segments=6), Z_GALV))
        for i, t in enumerate((-0.05,)):
            vp = base + fwd * (L * t) + mdir * 0.05
            v = add_tube(f"{prefix}Valve{i}", 0.040 * scale, 0.10 * scale, tuple(vp), segments=6)
            v.rotation_euler = mdir.to_track_quat("Z", "Y").to_euler()
            parts.append((v, Z_GALV))
        anchors["hose"] = base + fwd * (L * 0.28)
    return parts, anchors


def build_fork(prefix, centre, rc, yaw_deg, z_base):
    """
    Yoke holding the barrel by TRUNNIONS at its sides, on a cross-head over the slew drive.
    The A generation ran one fat tube from the mast top straight into the barrel's middle,
    so the head looked skewered rather than carried. De-CAD: the arms and cross-head are
    soft-shaded castings, and three bolt heads ring each trunnion boss.
    """
    yaw = math.radians(yaw_deg)
    s = mathutils.Vector((math.sin(yaw), -math.cos(yaw), 0.0))   # == gun_axes side
    up = mathutils.Vector((0.0, 0.0, 1.0))
    fh = mathutils.Vector((-s.y, s.x, 0.0))
    aw = rc + 0.09
    b = mathutils.Vector((centre.x, centre.y, z_base))
    parts = [(add_bar(f"{prefix}ForkBase", tuple(b - s * (aw + 0.05)), tuple(b + s * (aw + 0.05)),
                      0.12, 0.08), Z_GALV)]
    for k, sg in enumerate((-1.0, 1.0)):
        parts.append((add_bar(f"{prefix}ForkArm{k}", tuple(b + s * (aw * sg) - up * 0.035),
                              tuple(centre + s * (aw * sg) + up * 0.07), 0.11, 0.06,
                              up=tuple(s)), Z_GALV))
        parts.append((add_link(f"{prefix}Trunnion{k}", tuple(centre + s * (sg * (rc - 0.04))),
                               tuple(centre + s * (sg * (aw + 0.06))), 0.055, segments=6),
                      Z_GALV))
        for j in range(2):
            a = math.pi * j + 0.9
            p = centre + s * (sg * (aw + 0.03)) + (fh * math.cos(a) + up * math.sin(a)) * 0.085
            parts.append((add_stud(f"{prefix}Bolt{k}{j}", tuple(p), s * sg, 0.022, 0.016), Z_GALV))
    return parts


# --------------------------------------------------------------------------------------
# Assets
# --------------------------------------------------------------------------------------

def build_tower(p: TowerParams):
    """
    Fan gun on a mast with a caged ladder and a work basket (SMI Denali pattern; the TT10
    has no ladder, DECISIONS 75). T20 rebuilt the access, after the A mesh shipped with:
      * cage hoops made of CAPPED cylinders = solid 0.72 m discs threaded on the mast, which
        read in game as platforms the ladder passes through (Ash Flat item 30);
      * the cage centered 0.30 m off the ladder so it wrapped the mast, and its top hoop and
        two straps cutting through the deck, a rail post and the kick plate;
      * a rail closed on all four sides (no way onto the deck) and stiles stopping at deck
        level (EN ISO 14122-4 wants them 1.1 m above it as grab rails);
      * the deck 2.6 m under the head on the Tall: nobody standing on it reaches the gun.
    Now: the ladder plane is 0.19 m off the mast skin, the deck starts 0.06 m past it with a
    0.50 m opening in the rail, and the cage is a U of flat-bar hoops ON THE CLIMBER'S SIDE
    ONLY (open toward the mast), ending 1 m above the deck beside the opening.
    De-CAD: the mast is one open lathe standing in a CAST FOOT with nuts (no capped cones
    meeting face to face), a weld collar under the slew drive, rail posts a size up from
    the rails and the rails ending on them (A/B1 had 10 coplanar post/rail overlaps), and a
    folded-sheet cabinet.
    """
    parts = []
    pad_top = p.pad[2]
    parts.append((add_box("Pad", p.pad, (0, 0, pad_top / 2.0)), Z_CONCRETE))
    parts.append((add_box("BasePlate", p.base_plate,
                          (0, 0, pad_top + p.base_plate[2] / 2.0 - 0.003)), Z_GALV))

    mast_bottom = pad_top + p.base_plate[2]
    mid_z = mast_bottom + (p.mast_h - mast_bottom) * 0.5
    r_mid = (p.mast_r_base + p.mast_r_top) * 0.5
    parts.append((add_foot("MastFoot", (0, 0, mast_bottom - 0.003), 0.26, p.mast_r_base * 1.04, 0.22,
                           segments=8),
                  Z_GALV))
    parts += [(o, Z_GALV) for o in add_nuts("FootNut", (0, 0), 0.215, mast_bottom + 0.035, 4,
                                             phase=math.pi / 4.0)]
    parts.append((add_vlathe("Mast", (0, 0, mast_bottom + 0.10),
                             [(0.0, p.mast_r_base), (mid_z - mast_bottom - 0.10, r_mid),
                              (p.mast_h - mast_bottom - 0.10, p.mast_r_top)],
                             ["out", "out"], 10), Z_GALV))

    def mast_r(z):
        t = min(max((z - mast_bottom) / (p.mast_h - mast_bottom), 0.0), 1.0)
        return p.mast_r_base + (p.mast_r_top - p.mast_r_base) * t

    deck_z = p.mast_h - p.deck_below_top          # deck TOP surface

    # Flange joints every ~2.5 m (sections ship that way): open bands, none at the deck.
    n_flange = max(1, int((p.mast_h - pad_top) / 2.5))
    for i in range(1, n_flange + 1):
        z = pad_top + i * (p.mast_h - pad_top) / (n_flange + 1)
        if abs(z - deck_z) < 0.35:
            continue
        parts.append((add_tube(f"Flange{i}", mast_r(z) * 1.25, 0.035, (0, 0, z), segments=8,
                               caps=False), Z_GALV))
    parts.append((add_tube("MastCollar", p.mast_r_top * 1.22, 0.06, (0, 0, p.mast_h - 0.03),
                           segments=12, caps=False), Z_GALV))

    # ---- Work basket -------------------------------------------------------------------
    x0 = p.deck_x0
    x1 = x0 + p.deck[0]
    hy = p.deck[1] / 2.0
    # Deck: a grating frame round a 0.34 x 0.37 m opening at the mast, so the mast, the riser
    # and the conduit pass THROUGH THE OPENING rather than through the grating (verdict 3:
    # "pipes that go through other objects"). Four soft-shaded plates.
    hx0, hx1, hy0, hy1 = -0.13, 0.21, -0.25, 0.12
    for k, (xa, xb, ya, yb) in enumerate(((x0, hx0, -hy, hy), (hx1, x1, -hy, hy),
                                          (hx0 + 0.002, hx1 - 0.002, -hy, hy0), (hx0 + 0.002, hx1 - 0.002, hy1, hy))):
        parts.append((add_box(f"Deck{k}", (xb - xa, yb - ya, 0.05),
                              ((xa + xb) / 2.0, (ya + yb) / 2.0, deck_z - 0.025)), Z_GRATE))
    gap = p.ladder_w / 2.0 + 0.05                  # rail opening half-width at the ladder
    pw = 0.05                                      # posts a size up from the 0.04 rails
    edges = [((x1, -hy), (x1, hy)), ((x0, hy), (x1, hy)), ((x0, -hy), (x1, -hy)),
             ((x0, gap), (x0, hy)), ((x0, -hy), (x0, -gap))]
    for k, (a, b) in enumerate(edges):
        va, vb = mathutils.Vector((*a, 0.0)), mathutils.Vector((*b, 0.0))
        t = (vb - va).normalized() * (pw / 2.0)
        a2, b2 = va + t, vb - t                    # end ON the posts, not through them
        # top rail 1.10, knee rail at half height, 0.10 kick plate (EN ISO 14122-3)
        for tag, z, w, h in (("Rail", deck_z + p.rail_h, 0.04, 0.04),
                             ("Knee", deck_z + p.rail_h * 0.5, 0.03, 0.03),
                             ("Kick", deck_z + 0.06, 0.012, 0.10)):
            parts.append((add_bar(f"{tag}{k}", (a2.x, a2.y, z), (b2.x, b2.y, z), w, h), Z_GALV))
    for k, (x, y) in enumerate(((x0, -hy), (x0, hy), (x1, -hy), (x1, hy), (x0, gap), (x0, -gap))):
        parts.append((add_bar(f"RailPost{k}", (x, y, deck_z - 0.02),
                              (x, y, deck_z + p.rail_h + 0.03), pw, pw), Z_GALV))
    for k, sy in enumerate((-1.0, 1.0)):
        parts.append((add_bar(f"Brace{k}", (0.06, sy * 0.05, deck_z - 0.80),
                              (x1 - 0.06, sy * (hy - 0.06), deck_z - 0.06), 0.04, 0.04), Z_GALV))

    # ---- Ladder ------------------------------------------------------------------------
    lx, lw = p.ladder_x, p.ladder_w / 2.0
    for k, sy in enumerate((-lw, lw)):
        parts.append((add_bar(f"Stile{k}", (lx, sy, pad_top - 0.01), (lx, sy, deck_z + p.rail_h),
                              0.06, 0.02, up=(0, 1, 0)), Z_GALV))
    z, i = deck_z, 0
    while z > pad_top + 0.20:                      # 0.28 pitch down from the deck
        parts.append((add_bar(f"Rung{i}", (lx, -lw + 0.01, z), (lx, lw - 0.01, z), 0.025, 0.025),
                      Z_GALV))
        z -= 0.28
        i += 1
    for i, z in enumerate((pad_top + 1.0, deck_z - 0.35)):
        for k, sy in enumerate((-lw, lw)):
            parts.append((add_bar(f"Standoff{i}{k}", (lx + 0.02, sy, z),
                                  (-mast_r(z) * 0.6, sy * 0.3, z), 0.012, 0.05), Z_GALV))

    cage_z0 = pad_top + 2.4
    cage_top = deck_z + 1.0
    if cage_top - cage_z0 > 0.8:
        ey, depth = 0.36, 0.32
        cx = lx - depth
        rho = math.hypot(depth, ey)
        th0 = math.atan2(ey, depth)
        arc = [(cx + rho * math.cos(th0 + (2 * math.pi - 2 * th0) * s / 5.0),
                rho * math.sin(th0 + (2 * math.pi - 2 * th0) * s / 5.0)) for s in range(6)]
        hoop = [(lx, lw)] + arc + [(lx, -lw)]
        n = max(1, math.ceil((cage_top - cage_z0) / 1.5))
        for i in range(n + 1):
            z = cage_z0 + i * (cage_top - cage_z0) / n
            parts.append((add_ribbon(f"CageHoop{i}", hoop, z, 0.05, 0.012), Z_GALV))
        for k, frac in enumerate((0.17, 0.50, 0.83)):
            th = th0 + (2 * math.pi - 2 * th0) * frac
            x, y = cx + rho * math.cos(th), rho * math.sin(th)
            parts.append((add_bar(f"CageBar{k}", (x, y, cage_z0 - 0.03), (x, y, cage_top + 0.03),
                                  0.045, 0.012, up=(math.cos(th), math.sin(th), 0.0)), Z_GALV))

    # ---- Head --------------------------------------------------------------------------
    drive_h = 0.16
    parts.append((add_tube("SlewDrive", 0.17, drive_h, (0, 0, p.mast_h + drive_h / 2.0),
                           segments=8), Z_ACCENT))
    z_base = p.mast_h + drive_h + 0.035           # cross-head sunk 5 mm into the drive
    rc = body_radius_at(0.0, p.head_len, p.head_r_muzzle, p.head_r_throat)
    head_c = (0.0, 0.0, z_base + 0.045 + rc * 1.08 + 0.08)
    head, anc = build_gun_head("Head", head_c, p.head_r_muzzle, p.head_r_throat, p.head_len,
                               p.pitch_deg, p.yaw_deg, p.nozzles, scale=0.85, compressor=True)
    parts += head
    parts += build_fork("Head", anc["centre"], anc["rc"], p.yaw_deg, z_base)

    # ---- Services (routing audited, verdict 3) -------------------------------------------
    # Water riser: comes up out of the pad OUTSIDE the cast foot on a flange, dog-legs in to
    # the mast above the foot (the bends are the elbows), runs up through the deck opening
    # and ends in a horizontal stub; B2 started it inside the foot and base plate.
    rx, ry = -0.02, -0.18
    r_top = p.mast_h - 0.35
    riser = [(0.02, -0.40, pad_top - 0.01), (0.02, -0.40, pad_top + 0.40),
             (rx, ry, pad_top + 0.62), (rx, ry, r_top)]
    parts += [(o, Z_GALV) for o in add_hose("Riser", riser, 0.0375, segments=5)]
    parts.append((add_tube("RiserFlange", 0.07, 0.03, (0.02, -0.40, pad_top + 0.012),
                           segments=6), Z_GALV))
    stub_end = (0.05, -0.31, r_top)
    parts.append((add_link("RiserStub", (rx, ry, r_top), stub_end, 0.0375, segments=6), Z_GALV))
    # Hose: leaves the stub end through a clamp, hangs in a lopsided loop with slack inside
    # the basket (above the deck, inside the rail), and enters the head's compressor box
    # square to its face through a second clamp.
    a_in = mathutils.Vector(anc["hose"])
    a_pre = a_in - anc["perp"] * 0.16
    loop = [stub_end, (0.13, -0.38, p.mast_h - 0.71),
            (0.33, -0.30, p.mast_h - 0.36), tuple(a_pre), tuple(a_in)]
    parts += [(o, Z_RUBBER) for o in add_hose("Hose", loop, 0.03)]
    for k, (pa, pb) in enumerate(((stub_end, loop[1]), (tuple(a_pre), tuple(a_in)))):
        va, vb = mathutils.Vector(pa), mathutils.Vector(pb)
        c = add_tube(f"HoseClamp{k}", 0.042, 0.05, tuple(va + (vb - va).normalized() * 0.03),
                     segments=5, caps=False)
        c.rotation_euler = (vb - va).to_track_quat("Z", "Y").to_euler()
        parts.append((c, Z_GALV))
    # Cable conduit: out of the cabinet's back through a gland, across to the mast, up the
    # mast through the deck opening, into the underside of the slew drive. B2 stood it on the
    # pad through the cast foot.
    cab_w, cab_d, cab_h = p.cabinet
    cbz = pad_top + 0.70
    back = p.cabinet_off - cab_d / 2.0
    cond = [(back + 0.01, 0.05, cbz), (0.16, 0.05, cbz), (0.16, 0.05, p.mast_h + 0.01)]
    parts += [(o, Z_RUBBER) for o in add_hose("Conduit", cond, 0.026, segments=5)]
    g = add_tube("Gland", 0.04, 0.04, (back - 0.012, 0.05, cbz), segments=5, caps=False)
    g.rotation_euler = (0.0, math.radians(90.0), 0.0)
    parts.append((g, Z_GALV))
    for i, z in enumerate((pad_top + 1.6, deck_z - 0.5)):
        parts.append((add_bar(f"Clamp{i}", (0.105, 0.05, z), (0.205, 0.05, z), 0.07, 0.04), Z_GALV))

    # Base compressor / control cabinet: folded sheet, door to +x, beacon on the lid.
    parts += add_cabinet("Cabinet", (cab_d, cab_w, cab_h), (p.cabinet_off, 0.0), pad_top - 0.004,
                         +1, Z_ACCENT)
    parts.append((add_tube("Beacon", 0.06, 0.12,
                           (p.cabinet_off - 0.04, 0.18, pad_top - 0.004 + cab_h + 0.024 + 0.057),
                           segments=6), Z_AMBER))
    return parts


def build_lance(p: LanceParams):
    """
    T20: a lance is a light aluminum tube HINGED on a short galvanised column (the
    "lance snow gun" photograph: a bracket and pin, the tube's tail running past the pivot
    to the water inlet). The jacket covers only the lower column so the pivot reads; the
    tube is galvanised aluminum ("aluminum tubes"); the machine-colored parts
    are the nucleator head and the sensor box.
    De-CAD: a cast foot with nuts replaces the square plate, the column is one open tube
    into it with a weld collar under the cheeks, the jacket is a lathe with a rolled top
    lip, the drum compressor has rounded ends, the sensor box is chamfered.
    `laid` (second verdict): the summer pose. The tube is swung down through its hinge and
    rests nearly flat on a low cradle, head out along the border; the hose is coiled at the
    base and the compressor has gone to the shed.
    """
    parts = []
    pad_top = p.pad[2]
    parts.append((add_box("Pad", p.pad, (0, 0, pad_top / 2.0)), Z_CONCRETE))
    col_bottom = pad_top + 0.06
    col_top = col_bottom + p.column_h
    parts.append((add_foot("ColFoot", (0, 0, pad_top - 0.003), 0.29, p.column_r * 1.05, 0.16,
                           segments=8), Z_GALV))
    parts += [(o, Z_GALV) for o in add_nuts("FootNut", (0, 0), 0.268, pad_top + 0.031, 4,
                                             phase=math.pi / 4.0)]
    parts.append((add_vlathe("Column", (0, 0, pad_top + 0.05),
                             [(0.0, p.column_r), (col_top - pad_top - 0.05, p.column_r),
                              (col_top - pad_top - 0.05 + 0.012, p.column_r * 0.86)],
                             ["out", "out"], 10), Z_GALV))
    parts.append((add_disc("ColumnTop", 0.0, p.column_r * 0.86, +1,
                           mathutils.Matrix.Translation((0, 0, col_top + 0.012)) @
                           mathutils.Matrix.Rotation(math.radians(-90.0), 4, "Y"), segments=10),
                  Z_GALV))
    parts.append((add_tube("WeldCollar", p.column_r * 1.18, 0.05, (0, 0, col_top - 0.07),
                           segments=10, caps=False), Z_GALV))
    jr0, jr1, jh = p.jacket
    parts.append((add_vlathe("BaseJacket", (0, 0, pad_top + 0.05),
                             [(0.0, jr0), (jh, jr1), (jh + 0.018, jr1 * 0.93),
                              (jh + 0.022, p.column_r + 0.004)],
                             ["out", "out", "+x"], 10), Z_PLASTIC))

    piv = mathutils.Vector((0.0, 0.0, col_top + 0.27))
    for k, sy in enumerate((-0.07, 0.07)):
        parts.append((add_bar(f"Cheek{k}", (0, sy, col_top - 0.10), (0, sy, piv.z + 0.07),
                              0.16, 0.02, up=(0, 1, 0)), Z_GALV))
    parts.append((add_link("Pin", (0, -0.10, piv.z), (0, 0.10, piv.z), 0.025, segments=6),
                  Z_GALV))

    if p.laid:
        Lp = p.pipe_len
        drop = piv.z - (p.cradle_h + p.pipe_r + 0.02)
        lean = math.pi / 2.0 + math.asin(min(0.99, drop / Lp))
    else:
        lean = math.radians(p.lean_deg)
        Lp = p.pipe_len if p.pipe_len > 0 else (p.height - piv.z) / math.cos(lean)
    d = mathutils.Vector((math.sin(lean), 0.0, math.cos(lean)))
    nrm = mathutils.Vector((-math.cos(lean), 0.0, math.sin(lean)))
    tip = piv + d * Lp
    tail = piv - d * p.stub_len
    along = d.to_track_quat("Z", "Y").to_euler()
    parts.append((add_link("Lance", tuple(tail), tuple(tip), p.pipe_r, segments=8, extend=0.0),
                  Z_GALV))
    for k, f in enumerate((0.36, 0.68)):           # section couplings
        t = add_tube(f"Coupling{k}", p.pipe_r * 1.45, 0.10, tuple(piv + d * (Lp * f)), segments=8,
                     caps=False)
        t.rotation_euler = along
        parts.append((t, Z_GALV))
    mb = piv + d * (Lp * 0.42) + nrm * (p.pipe_r + 0.05)
    box = add_box("SensorBox", (0.10, 0.10, 0.16), (0, 0, 0))
    box.rotation_euler = along
    box.location = mb
    parts.append((box, Z_LIVERY))
    mk = add_tube("Marker", 0.025, 0.06, tuple(mb + nrm * 0.075), segments=6)
    mk.rotation_euler = nrm.to_track_quat("Z", "Y").to_euler()
    parts.append((mk, Z_AMBER))

    hc = tip - d * (p.head_len * 0.35)
    head = add_tube("NucHead", p.head_r, p.head_len, tuple(hc), segments=8)
    head.rotation_euler = along
    parts.append((head, Z_LIVERY))
    cap = add_tube("HeadCap", p.head_r * 1.3, 0.05, tuple(tip + d * 0.02), segments=6)
    cap.rotation_euler = along
    parts.append((cap, Z_LIVERY))
    u = d.orthogonal().normalized()
    for i in range(3):
        a = 2.0 * math.pi * i / 3.0
        r = (u * math.cos(a) + d.cross(u) * math.sin(a))
        s0 = hc + d * 0.06 + r * (p.head_r * 0.9)
        parts.append((add_link(f"Stub{i}", tuple(s0), tuple(s0 + r * 0.09 + d * 0.06), 0.012,
                               segments=4), Z_GALV))

    if p.laid:
        # Low cradle under the head end: two splayed legs and a saddle bar.
        cpt = piv + d * (Lp * 0.86)
        zc = cpt.z - p.pipe_r - 0.012
        for k, sy in enumerate((-0.22, 0.22)):
            parts.append((add_bar(f"Cradle{k}", (cpt.x - 0.05, sy, 0.0), (cpt.x, sy * 0.45, zc),
                                  0.035, 0.035), Z_GALV))
        parts.append((add_bar("CradleSaddle", (cpt.x, -0.12, zc), (cpt.x, 0.12, zc), 0.04, 0.025),
                      Z_GALV))
        # Hose run from the inlet to a coil on the snow beside the pad.
        parts += [(o, Z_RUBBER) for o in add_hose("Hose", [tuple(tail), (-0.40, 0.22, 0.60),
                                                           (-0.50, 0.42, 0.05)], 0.03)]
        parts.append((add_torus("HoseCoil0", (-0.62, 0.62, 0.03), 0.26, 0.03, segments=14),
                      Z_RUBBER))
    else:
        # Hose off the tail inlet through a clamp, continuing straight off the tube end before
        # it drops to the snow (B2 bent it back along the tube; verdict-3 routing audit).
        h0 = tail - d * 0.07
        parts += [(o, Z_RUBBER) for o in add_hose("Hose", [tuple(tail - d * 0.01), tuple(h0),
                                                           (-0.34, 0.22, 0.56), (-0.46, 0.40, 0.08),
                                                           (-0.80, 0.44, 0.04)], 0.03)]
        hc = add_tube("HoseClamp", 0.042, 0.05, tuple(tail - d * 0.03), segments=5, caps=False)
        hc.rotation_euler = along
        parts.append((hc, Z_GALV))

    if p.with_compressor:
        # Drum compressor beside the pad (white dome with a dark intake in the "lance snow
        # gun" photograph): rounded drum ends, dark intake disc, chamfered skid.
        cr, cl = p.compressor_r, p.compressor_l
        cx, cy = -0.66, -0.12
        parts.append((add_box("Skid", (0.50, cl + 0.06, 0.08), (cx, cy, 0.04)), Z_GALV))
        DM = (mathutils.Matrix.Translation((cx, cy, 0.08 + cr - 0.004)) @
              mathutils.Matrix.Rotation(math.radians(90.0), 4, "Z"))
        parts.append((add_lathe("CompDrum", [(-cl / 2.0, cr * 0.80), (-cl / 2.0 + 0.035, cr),
                                             (cl / 2.0 - 0.035, cr), (cl / 2.0, cr * 0.80)],
                                ["out", "out", "out"], DM, segments=10), Z_LIVERY))
        parts.append((add_disc("CompIntake", cl / 2.0, cr * 0.80, +1, DM, segments=10), Z_GRATE))
        parts.append((add_disc("CompEnd", -cl / 2.0, cr * 0.80, -1, DM, segments=10), Z_LIVERY))
    if p.with_rest:
        rp = piv + d * (Lp * 0.82)
        for k, sy in enumerate((-0.25, 0.25)):
            parts.append((add_bar(f"Rest{k}", (rp.x, sy, 0.0), (rp.x, 0.0, rp.z - p.pipe_r),
                                  0.04, 0.04), Z_GALV))
    return parts


def build_fan(p: FanParams):
    """
    Carriage fan gun. T20: the TR10/TF10 head from build_gun_head on a YOKE (fork +
    trunnions) over a turntable, a bent-tube A-frame drawbar to the groomer push plate
    (anatomy #26/#27; the TR10 close-up shows the bent galvanised tubes), and the services
    moved off the barrel's path.
    De-CAD: real wheel profiles (rounded tire shoulders, dished hub with a cap) instead of
    capped 12-gon discs, chassis and cross members of different sections so their faces
    no longer coincide (A/B1: 4 x 108 cm2 coplanar overlaps), round axle stubs, a cast foot
    under the pedestal, folded-sheet cabinet and compressor, everything that stands on the
    floor sunk 3 mm into it.
    """
    parts = []
    fw = p.frame_w
    zc = 0.42
    ty = p.track_w / 2.0
    for k, sy in enumerate((-ty, ty)):
        parts.append((add_box(f"Chassis{k}", (1.60, fw, fw), (0.0, sy, zc)), Z_GALV))
    for k, sx in enumerate((-0.62, 0.62)):
        parts.append((add_box(f"Cross{k}", (fw - 0.012, p.track_w - fw + 0.01, fw - 0.012),
                              (sx, 0.0, zc - 0.004)), Z_GALV))

    wy = ty + p.wheel_w / 2.0 + 0.05
    wr, ww = p.wheel_r, p.wheel_w
    for k, sg in enumerate((-1.0, 1.0)):
        y = sg * wy
        WM = (mathutils.Matrix.Translation((-0.10, y, wr)) @
              mathutils.Matrix.Rotation(math.radians(90.0 * sg), 4, "Z"))   # local +x = outboard
        parts.append((add_lathe(f"Wheel{k}", [(-ww / 2, wr * 0.62), (-ww / 2, wr * 0.88),
                                              (-ww / 2 + 0.045, wr), (ww / 2 - 0.045, wr),
                                              (ww / 2, wr * 0.88), (ww / 2, wr * 0.62)],
                                  ["-x", "out", "out", "out", "+x"], WM, segments=8), Z_RUBBER))
        parts.append((add_lathe(f"Hub{k}", [(ww / 2 - 0.004, wr * 0.62), (ww / 2 - 0.035, wr * 0.30)],
                                ["+x"], WM, segments=8), Z_GALV))
        parts.append((add_disc(f"HubCap{k}", ww / 2 - 0.035, wr * 0.30, +1, WM, segments=6), Z_GALV))
        parts.append((add_disc(f"HubIn{k}", -ww / 2 + 0.004, wr * 0.62, -1, WM, segments=8), Z_GALV))
        parts.append((add_bar(f"AxleArm{k}", (-0.10, sg * ty, zc), (-0.10, sg * ty, wr - 0.02),
                              0.045, 0.035), Z_GALV))
        parts.append((add_link(f"Axle{k}", (-0.10, sg * (ty - 0.01), wr), (-0.10, y, wr), 0.025,
                               segments=6), Z_GALV))

    apex = mathutils.Vector((0.62 + p.drawbar_len, 0.0, 0.30))
    for k, sg in enumerate((-1.0, 1.0)):
        parts += [(o, Z_GALV) for o in add_hose(f"Drawbar{'LR'[k]}",
                                                [(0.70, sg * ty, zc),
                                                 (0.98, sg * 0.26, 0.40), tuple(apex)],
                                                0.032, segments=6)]
    parts.append((add_box("PushPlate", (0.06, 0.34, 0.26), (apex.x + 0.05, 0, 0.34)), Z_GALV))
    parts.append((add_bar("CastorFork", (apex.x - 0.03, 0.0, apex.z), (apex.x - 0.03, 0.0, p.castor_r),
                          0.05, 0.05), Z_GALV))
    parts.append((add_tube("Castor", p.castor_r, 0.07, (apex.x - 0.03, 0, p.castor_r), segments=6,
                           rotation=(math.radians(90), 0, 0)), Z_RUBBER))

    for k, sy in enumerate((-0.44, 0.44)):
        parts.append((add_link(f"Jack{k}", (-0.62, sy, zc - 0.03), (-0.62, sy, 0.02), 0.035,
                               segments=5), Z_GALV))
        parts.append((add_box(f"JackFoot{k}", (0.18, 0.18, 0.03), (-0.62, sy, 0.03)), Z_GALV))

    # The gun, then the yoke under it. The cross-head height is MEASURED from the built
    # housing (lowest vertex over the cross-head / turntable footprint) over the working
    # pitch AND the 45 deg tilt limit (a probe head, deleted again), minus a clearance, so
    # one yoke height serves every pose of the same machine.
    head, anc = build_gun_head("Gun", (0.0, 0.0, p.barrel_z), p.barrel_r_muzzle,
                               p.barrel_r_throat, p.barrel_len, p.pitch_deg, p.yaw_deg,
                               p.nozzles, nucleators=p.nucleators, scale=1.0, manifold=True,
                               ring_bolts=4, segments=12, bell_segments=7)
    probe, _ = build_gun_head("Probe", (0.0, 0.0, p.barrel_z), p.barrel_r_muzzle,
                              p.barrel_r_throat, p.barrel_len, max(p.pitch_deg, 45.0),
                              p.yaw_deg, 0, ring_bolts=0, segments=12)
    bpy.context.view_layer.update()
    low = p.barrel_z
    s = mathutils.Vector((math.sin(math.radians(p.yaw_deg)), -math.cos(math.radians(p.yaw_deg)), 0))
    fh = mathutils.Vector((-s.y, s.x, 0.0))
    for parts_set in (head, probe):
        for o, _z in parts_set:
            if not o.name.endswith(("Housing", "Guard", "Collar", "Bell")):
                continue
            mw = o.matrix_world
            for v in o.data.vertices:
                w = mw @ v.co
                h = mathutils.Vector((w.x, w.y, 0.0))
                if h.length < 0.30 or (abs(h.dot(fh)) < 0.12 and abs(h.dot(s)) < anc["rc"] + 0.20):
                    low = min(low, w.z)
    for o, _z in probe:
        me = o.data
        bpy.data.objects.remove(o, do_unlink=True)
        bpy.data.meshes.remove(me)
    z_base = low - 0.10
    print(f"[fan]   yoke cross-head at z {z_base:.3f} (housing low point {low:.3f})")
    parts += head
    parts += build_fork("Gun", anc["centre"], anc["rc"], p.yaw_deg, z_base)
    parts.append((add_tube("Turntable", 0.22, 0.08, (0, 0, z_base - 0.075), segments=8), Z_GALV))
    zf = zc + fw / 2.0 + 0.02
    parts.append((add_link("Pedestal", (0.0, 0, zf - 0.01), (0.0, 0, z_base - 0.10), 0.085,
                           segments=8), Z_GALV))

    parts.append((add_box("Floor", (1.24 - 0.02, p.track_w - fw - 0.01, 0.02),
                          (0.0, 0.0, zf - 0.01)), Z_GRATE))
    parts.append((add_tube("Tank", p.tank_r, 0.45, (0.40, 0.40, zf + p.tank_r - 0.003),
                           rotation=(0, math.radians(90), 0), segments=8), Z_ACCENT))
    # Feed hose: straight off the valve-bank pipe end through a clamp, a slack drop in front
    # of the water module, and square into its front cap through a second clamp (B2 cut
    # diagonally down through the tank body; verdict-3 routing audit).
    feed = mathutils.Vector(anc["hose"])
    f1 = feed + anc["fwd"] * 0.07
    tank_in = mathutils.Vector((0.40 + 0.225 - 0.01, 0.40, zf + p.tank_r))
    route = [tuple(feed - anc["fwd"] * 0.01), tuple(f1), (0.86, 0.47, zf + 0.62),
             (0.80, 0.40, zf + p.tank_r), tuple(tank_in)]
    parts += [(o, Z_RUBBER) for o in add_hose("Feed", route, 0.028)]
    for k, (pa, pb) in enumerate(((feed, f1), (tank_in, mathutils.Vector(route[3])))):
        c = add_tube(f"HoseClamp{k}", 0.040, 0.045, tuple(pa + (pb - pa).normalized() * 0.03),
                     segments=5, caps=False)
        c.rotation_euler = (pb - pa).to_track_quat("Z", "Y").to_euler()
        parts.append((c, Z_GALV))
    t = add_torus("Coil", (0, 0, 0), 0.20, 0.026, segments=7)
    t.rotation_euler = (math.radians(90), 0, 0)
    t.location = (-0.50, ty - 0.08, zf + 0.223)
    parts.append((t, Z_RUBBER))
    cw, cd, ch = p.cabinet
    parts += add_cabinet("Cabinet", (cd, cw, ch), (0.42, -0.34), zf - 0.003, +1, Z_GALV,
                         louvres=False)
    parts += add_cabinet("Compressor", (0.42, 0.34, 0.24), (-0.40, -0.32), zf - 0.003, -1, Z_GALV,
                         door=False)
    parts += [(o, Z_RUBBER) for o in add_hose("Hose", [(-0.55, 0.20, 0.06), (-1.00, 0.48, 0.05),
                                                       (-1.32, 0.30, 0.05)], 0.032)]
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
        # Spread across the arm; odd counts put one on center.
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
    """
    Snowmaking hydrant cover + standpipe. T20: two Camlock outlets and a number plate on
    the standpipe (the "50" plate on the hydrant in the "fan gun in foreground, snow lance
    in background" photograph), and the hose now lies COILED as the asset description
    says; A laid a straight stub.
    De-CAD: the lid sits 3 mm into the body (A: face on face), two hinge barrels on its back
    edge and a pull handle off-center on the front; the standpipe stands in a cast foot and
    is an open tube under the handwheel; the outlets start inside the pipe wall (B1: their
    end caps overlapped at the pipe center).
    """
    parts = []
    bw, bd, bh = p.box
    body_h = bh - p.lid_h
    parts.append((add_box("Body", (bd, bw, body_h), (0, 0, body_h / 2.0)), Z_GALV))
    # Lid and handwheel are SAFETY plastic (recolor slot 2, orange by default) — not the
    # manufacturer livery slot.
    parts.append((add_box("Lid", (bd * 1.06, bw * 1.06, p.lid_h),
                          (0, 0, body_h + p.lid_h / 2.0 - 0.003)), Z_PLASTIC))
    for k, sy in enumerate((-0.14, 0.14)):
        parts.append((add_tube(f"Hinge{k}", 0.014, 0.08, (-bd * 0.53, sy, body_h + 0.004),
                               rotation=(math.radians(90), 0, 0), segments=6, caps=False), Z_GALV))
    parts.append((add_bar("Handle", (bd * 0.53 + 0.012, 0.06, body_h + p.lid_h * 0.4),
                          (bd * 0.53 + 0.012, 0.17, body_h + p.lid_h * 0.4), 0.016, 0.016), Z_GALV))
    px = 0.34
    parts.append((add_foot("PipeFoot", (px, 0.0, -0.003), 0.08, p.pipe_r * 1.08, 0.12,
                           segments=8), Z_GALV))
    parts.append((add_tube("Pipe", p.pipe_r, p.pipe_h - 0.05, (px, 0.0, 0.05 + (p.pipe_h - 0.05) / 2.0),
                           segments=8, caps=False), Z_GALV))
    parts.append((add_tube("Wheel", p.wheel_r, 0.035, (px, 0.0, p.pipe_h), segments=10),
                  Z_PLASTIC))
    for k, sg in enumerate((-1.0, 1.0)):
        parts.append((add_link(f"Outlet{k}", (px, sg * p.pipe_r * 0.6, 0.55), (px, sg * 0.13, 0.55),
                               0.028, segments=6), Z_GALV))
    parts.append((add_box("NumberPlate", (0.012, 0.12, 0.09),
                          (px + p.pipe_r + 0.006, 0.0, 0.76)), "plate"))
    if p.with_hose:
        cx, cy = 0.80, 0.42
        # Lead off the +y outlet through a clamp, straight on for 6 cm, then down to the coil
        # it is part of (verdict-3 routing audit).
        parts += [(o, Z_RUBBER) for o in add_hose("HoseLead", [(px, 0.12, 0.55), (px, 0.19, 0.55),
                                                               (0.44, 0.33, 0.22),
                                                               (cx - p.coil_r + 0.02, cy, p.hose_r)],
                                                  p.hose_r)]
        parts.append((add_tube("HoseClamp", p.hose_r + 0.012, 0.045, (px, 0.155, 0.55),
                               rotation=(math.radians(90), 0, 0), segments=5, caps=False), Z_GALV))
        parts.append((add_torus("Coil0", (cx, cy, p.hose_r), p.coil_r, p.hose_r, segments=11),
                      Z_RUBBER))
        parts.append((add_torus("Coil1", (cx + 0.015, cy - 0.01, p.hose_r * 2.8), p.coil_r - 0.05,
                                p.hose_r, segments=11), Z_RUBBER))
    return parts


# --------------------------------------------------------------------------------------
# Assembly
# --------------------------------------------------------------------------------------

# Long thin tubes whose smart-project island is a sliver: unwrap_into_rect keeps the
# island's 7 m : 0.19 m aspect, so the lance tube got ~4 px of atlas around its
# circumference, the finish repack kept that, and mip filtering bled the neighboring
# LIVERY islands onto it (half the B tube rendered yellow in the first preview). Stretching
# the island across its zone rect costs nothing visible (the zone swatch is plain metal)
# and gives the repack a full-width island.
UV_WIDEN = ("Lance", "Stile", "CageBar", "Riser", "Brace")   # name prefixes (de-CAD round:
                                                             # a stile bled yellow the same way)


def _widen_uv(obj, rect, A, inset_px=3):
    """Stretch the part's islands along their SHORT UV axis to fill the zone rect."""
    u0, v0, u1, v1 = rect
    ins = inset_px / float(A)
    data = obj.data.uv_layers.active.data
    us = [d.uv[0] for d in data]
    vs = [d.uv[1] for d in data]
    if (max(us) - min(us)) / (u1 - u0) <= (max(vs) - min(vs)) / (v1 - v0):
        lo, hi = min(us), max(us)
        for d in data:
            t = (d.uv[0] - lo) / max(hi - lo, 1e-6)
            d.uv = (u0 + ins + t * (u1 - u0 - 2 * ins), d.uv[1])
    else:
        lo, hi = min(vs), max(vs)
        for d in data:
            t = (d.uv[1] - lo) / max(hi - lo, 1e-6)
            d.uv = (d.uv[0], v0 + ins + t * (v1 - v0 - 2 * ins))


def _shade(obj, table, angle_deg=66.0):
    """
    cs2_asset.shade_by_angle's recipe (attribute route + face-area weighted normals), with
    one T20 change: edges whose faces all belong to SOFT_PREFIXES parts are never sharp.
    """
    import json as _json
    names = _json.loads(obj.data[cs2_asset.PART_TABLE_PROP])
    soft = [n.startswith(SOFT_PREFIXES) for n in names]
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    me = obj.data
    for p in me.polygons:
        p.use_smooth = True
    bm = bmesh.new()
    bm.from_mesh(me)
    layer = bm.faces.layers.int.get("part")
    lim = math.radians(angle_deg)
    flags = []
    for e in bm.edges:
        fs = e.link_faces
        if len(fs) != 2:
            flags.append(False)
            continue
        if any(soft[f[layer] - 1] for f in fs):          # a soft part softens its rim too
            flags.append(False)
            continue
        flags.append(e.calc_face_angle(0.0) > lim)
    bm.free()
    attr = me.attributes.get("sharp_edge") or me.attributes.new("sharp_edge", "BOOLEAN", "EDGE")
    attr.data.foreach_set("value", flags)
    me.update()
    mod = obj.modifiers.new("WeightedNormal", type="WEIGHTED_NORMAL")
    mod.mode = "FACE_AREA"
    mod.keep_sharp = True
    bpy.ops.object.modifier_apply(modifier=mod.name)
    print(f"[shade] {obj.name}: {sum(flags)} sharp edges (> {angle_deg:.0f} deg), "
          f"{sum(soft)} soft parts of {len(names)}")


def finish(parts, name, atlas_size, base_pivot=True, bevel=False, smooth=False):
    """
    Per-part atlas UVs, join, weld, base-pivot, and rotate into the CS2 net frame.

    `smooth` (T20; the four snowmaking assets pass True, the floodlight keeps the A path):
    tag every part with the "part" face attribute so the LOD chain can delete parts by name
    (cs2_asset.make_lod_chain), and shade by angle (66 deg) so 8-16-gon tubes and the
    housing read round instead of faceted (art-pipeline-v2 item 1). UVs and zones unchanged.
    """
    # Bevel BEFORE unwrap so the new bevel faces get atlas UVs like everything else —
    # beveling after would leave them unmapped.
    if bevel:
        for obj, _zone in parts:
            if not obj.name.startswith(BEVEL_PREFIXES):
                continue
            mod = obj.modifiers.new("Bevel", type="BEVEL")
            # T20 de-CAD: 0.5-2 cm by part size (15 % of the thinnest side) on the B assets;
            # the floodlight (smooth=False) keeps the flat 0.02.
            mod.width = (min(0.02, max(0.005, 0.15 * min(obj.dimensions))) if smooth else 0.02)
            mod.segments = 1
            mod.limit_method = "ANGLE"
            mod.angle_limit = math.radians(45.0)
            mod.use_clamp_overlap = True
            bpy.ops.object.select_all(action="DESELECT")
            obj.select_set(True)
            bpy.context.view_layer.objects.active = obj
            bpy.ops.object.modifier_apply(modifier=mod.name)

    # After bevels (chamfer faces belong to their part), before join (names die there).
    table = cs2_asset.tag_parts(parts) if smooth else None

    zones = cs2_asset.snow_prop_zones(atlas_size)
    for obj, zone in parts:
        cs2_asset.unwrap_into_rect(obj, zones[zone], atlas_size)
        if smooth and obj.name.startswith(UV_WIDEN):
            _widen_uv(obj, zones[zone], atlas_size)

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
    if smooth:
        cs2_asset.store_part_table(obj, table)
        _shade(obj, table)
    return obj


# Generation B (T20 detail pass, 2026-10-16) for the four snowmaking assets; the floodlight
# keeps its A geometry and names. B is free in assets/import/props/B and in ImportedData.
ASSETS = {
    "tower":   ("SkiSnowGunTowerB", lambda a: build_tower(TowerParams(mast_h=a.mast)), 256),
    "lance":   ("SkiSnowGunLanceB", lambda a: build_lance(LanceParams(height=a.lance)), 128),
    "fan":     ("SkiSnowGunFanB",   lambda a: build_fan(FanParams()), 256),
    "hydrant": ("SkiHydrantBoxB",   lambda a: build_hydrant(HydrantParams()), 128),
    "flood":   ("SkiPisteFloodlightA", lambda a: build_floodlight(FloodlightParams()), 256),
}

# finish(smooth=True): part tags for the LOD chain + shade by angle (T20).
SMOOTH_ASSETS = {"tower", "lance", "fan", "hydrant"}


# ── Variants ────────────────────────────────────────────────────────────────────────
# The placeholder spawn group picks ONE of these per vertex, weighted
# (area-prop-variation.md). Mast/lance height is the variety axis the design spec ranks
# 5th but it is nearly free here — same builder, one parameter — and it is the axis that
# breaks the SKYLINE rhythm, which is what the eye actually reads along a run.
# Naming keeps the generation letter last (PIPELINE.md): never reuse a name.
VARIANTS = {
    "tower":   [("SkiSnowGunTowerB",     dict(mast_h=6.5), 256),
                ("SkiSnowGunTowerLowB",  dict(mast_h=4.5), 256),
                ("SkiSnowGunTowerTallB", dict(mast_h=8.0), 256)],
    # Lean varies per variant inside the documented 25-40 deg band (anatomy, Machine 3).
    "lance":   [("SkiSnowGunLanceB",     dict(height=7.0, lean_deg=30.0), 128),
                ("SkiSnowGunLanceTallB", dict(height=10.0, pipe_r=0.035, lean_deg=35.0), 128),
                # Folded / summer variant DROPPED (verdict 2, 2026-10-16: "if it still
                # does not read in a 150 m shot, drop the variant"). The laid pose (tube swung
                # flat onto a low cradle, hose coiled, compressor away) reads close up but at
                # 150 m it is an orange dot with a hairline that vanishes into the snow
                # (assets/preview/snow-props-2026-10-16/SkiSnowGunLanceDownB_far150m.png).
                # build_lance(LanceParams(laid=True, pipe_len=6.0, with_compressor=False))
                # still builds it; the in-game SkiSnowGunLanceDown prefab stays on A until the
                # surgery round removes it from the spawn group.
                ],
    "fan":     [("SkiSnowGunFanB",       dict(), 256),
                # Parked. CORRECTED by the anatomy study: an idle fan gun sits barrel
                # NOSE-UP at 30-45 degrees (water drains, snow does not sit in the
                # muzzle), not dropped level as the first pass assumed. Distinguished
                # from the working variant by yaw swung back along the frame.
                ("SkiSnowGunFanIdleB",   dict(pitch_deg=42.0, yaw_deg=-6.0), 256)],
    "hydrant": [("SkiHydrantBoxB",       dict(), 128),
                ("SkiHydrantBoxBareB",   dict(with_hose=False), 128)],
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
    obj = finish(parts, name, quad_size * 2, smooth=key in SMOOTH_ASSETS)
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


# Weathering pass (2026-10-17, docs/research/art/snow-props-weather-2026-10-17.md): the last
# WeatherHook run, so a driver can read its report / fields. Texture-only: see snow_weather.py.
LAST_WEATHER = None

# -- Texture size (REDO, wiki style, 2026-10-17; snow-props-weather-2026-10-17.md "Redo") ---------
# The wiki floor is 512^2 ("anything smaller is unsupported", Asset Creation Guide; the B lance /
# hydrant sets were 256^2) and B's props were under-textured 3-20x against vanilla props
# (wiki-best-practices-gap-2026-10-17.md section 2a). With --repack these sizes are the DEFAULTS
# (coordinator decision, reversible: `--tex B` restores the exported B sizes). quad_size stays
# DERIVED as tex // 2 everywhere (finish(), build_maps(), the AO rebake).
# Lances 512 -> 1024 (2026-10-17, after the street-light comparison, weather doc section 12: the 6.9 / 11 m
# single-quad tube is capped along its length by the atlas width; vanilla poles sit on 4096 maps).
TEX_REDO = {"tower": 1024, "fan": 1024, "lance": 1024, "hydrant": 512}
TEX_FLOOR = 512                # wiki minimum; cs2_validate texture-size ERROR below it
TEX_CEIL_UNDER_10M = 1024      # snow-prop-materials section 5: never 2048 for anything under 10 m


def texture_size(key, quad_size, repack=False, override=None):
    """Texture / atlas size of one variant. `override`: None = TEX_REDO with --repack, the B size
    (2 x the VARIANTS quad_size) without; "B" = the B size; an int = every class;
    "tower=1024,lance=512,..." = per class (unlisted classes fall back to the default)."""
    b = int(quad_size) * 2
    default = TEX_REDO.get(key, b) if repack else b
    if override is None or str(override).strip() == "":
        return default
    s = str(override).strip()
    if s.upper() == "B":
        return b
    if "=" in s:
        table = dict(kv.strip().split("=") for kv in s.split(",") if kv.strip())
        return int(table[key]) if key in table else default
    return int(s)


# -- Density repack -------------------------------------------------------------------------------
# rev 3 (2026-10-17, snow-props-weather-2026-10-17.md section 4): the B finish pass kept each
# part's atlas-time UV scale, so the painted shells shipped at 12-40 px/m while nuts got
# 200-1,450 px/m. `--repack` replaces the finish bake's pack step (lift_tower.apply_finish_textures
# packer=): islands equalized to 3D area, translation-identical copies stacked
# (cs2_liftdress.share_identical), weighted per TIER, packed concave. LODs are decimated from the
# finished mesh, so LOD1/LOD2 keep sharing this layout.
# REDO (wiki style, 2026-10-17, the "Redo" section of the same doc), four changes:
#   1. fixed RELATIVE tier weights (DENSITY_REL) instead of rev 3's absolute floors and 105 px/m
#      cap, so the density follows the texture size (wiki: "similar texel density" across islands,
#      priority to the most visible parts; the hardware and masts ride below the shells);
#   2. MIP-SAFE island margins (repack_margin_px; rev 3 kept B's 2 px, below the wiki's "spacing
#      large enough to avoid issues with mipmapping"), held EXACTLY by our own shelf packer
#      (_shelf_layout: Blender's pack_islands could not hold them, see there);
#   3. DARK islands (the rubber and grate zones) fill the shelves from the bottom of the band and
#      the light ones start an extra empty gap above them (wiki: "cluster similarly colored
#      materials ... to prevent bleeding between very dark and very light materials"), at ONE
#      shared scale;
#   4. the atlas-time one-island-per-face slivers are UNFOLDED into strips / nets first
#      (_unfold_islands), and islands may rotate (the reprojected normals are re-oriented per face).
DENSITY_SMALL = ("FootNut", "HeadBolt", "GunBolt", "HeadStud", "GunStud", "Hinge", "Handle", "Clamp",
                 "HoseClamp", "Gland", "Coupling", "Pin", "HubCap", "HubIn", "Stub", "RiserStub",
                 "Outlet", "Valve", "GunValve", "Louvre", "Elbow", "WeldCollar", "MastCollar",
                 "Flange", "RiserFlange", "Standoff", "Knee", "Cheek", "NumberPlate", "Marker",
                 "Beacon", "SensorBox", "HeadCap", "NucHead", "CompEnd", "CompIntake", "Axle",
                 "CastorFork", "JackFoot", "HeadTrunnion", "GunTrunnion", "HeadSpinner",
                 "GunSpinner", "HeadBlades", "GunBlades", "Kick", "HeadCollar", "GunCollar")
DENSITY_SHELL = ("HeadHousing", "HeadBell", "GunHousing", "GunBell", "Cabinet", "CabDoor", "CabLid",
                 "HeadCompressor", "SlewDrive", "Tank", "BaseJacket", "Lid", "CompDrum",
                 "Compressor", "Body")
DENSITY_MAST = ("Mast", "Column", "ColFoot", "Lance", "Pipe", "Riser", "Pedestal", "Chassis", "Cross",
                "DrawbarL", "DrawbarR", "Turntable", "HeadFork", "GunFork")
# shells 1 : hardware 0.8 : masts 0.65 : rest 0.6 - the gap doc's 1024 tower estimate
# (~150 / ~120 / ~95 px/m) as ratios; scale-free, so a bigger texture lifts every tier.
DENSITY_REL = {"shell": 1.0, "small": 0.8, "mast": 0.65, "rest": 0.6}
REPACK_MARGIN_PX = None         # None = repack_margin_px(A); a driver may pin an int to compare
DARK_ZONES = ("rubber", "grate")
DARK_GAP_MARGINS = 2.0          # extra empty band between the dark and the light shelves, in margins
                                # (each side also keeps margin / 2: dark <-> light >= 3 margins)
# Long single faces cap the scale: the lance tube is ONE 6 m quad per side, so at 512 its strip
# cannot carry the shell density along its length and, uncapped, it pulls every island of the
# asset down with it (one scale for all). An island longer than REPACK_MAX_LEN of the width at the
# tested scale is compressed ALONG its long axis only, never below REPACK_MIN_SQUASH (full density
# across, less along a uniform tube; the reprojected normals follow through reorient_normals).
REPACK_MAX_LEN = 0.85
REPACK_MIN_SQUASH = 0.35        # the 10 m LanceTall tube: ~2.9 : 1 across : along (B shipped it 28 : 1)
LAST_REPACK = None


def repack_margin_px(A):
    """Island margin in texels: A // 64 = 8 @ 512, 16 @ 1024 - clean down to the 64^2 mip at both
    sizes (mip 3 / mip 4); equals cs2_uv.default_margin_px (the buildings' rule) at 1024 and is
    twice the A // 128 floor at 512 (write-up "Redo", margin study)."""
    if REPACK_MARGIN_PX is not None:
        return int(REPACK_MARGIN_PX)
    return max(8, int(A) // 64)


def _face_zones(mesh, A):
    """Atlas zone name per face (UVAtlas centroid, the rule snow_weather uses)."""
    import numpy as np
    import cs2_uv
    import snow_weather
    zones = cs2_asset.snow_prop_zones(A)
    uva = cs2_uv.loop_uvs(mesh.uv_layers["UVAtlas"])
    start, total = cs2_uv.face_loop_table(mesh)
    return [snow_weather._zone_of(tuple(uva[s:s + t].mean(0)), zones) for s, t in zip(start, total)]


# -- Unfold (REDO): one island per developable strip instead of one per face -------------------
# The atlas-time smart project gives most parts ONE ISLAND PER FACE (tower 577 islands: every
# stile, rail, cage bar, conduit and riser facet is its own 1-6 cm x 0.3-6 m sliver). With a
# mip-safe margin each sliver pays 2 x margin across its whole length: at 16 px the tower's fill
# fell to 0.07 (margin study in the write-up). `_unfold_islands` rebuilds the UVUnique start layout
# by UNFOLDING each part's faces across shared edges whose dihedral is <= UNFOLD_MAX_DEG: every
# face is placed exactly (rotated about the shared edge into its neighbor's plane: isometric, no
# stretch), a face that would overlap the island already laid out starts a new island. Prism
# sides become one strip, boxes become nets, cones / cylinder bands unroll; a face is never
# mirrored (the 2-D frame keeps the face's own winding). Pinned (stamp strip) faces are untouched.
# The tangent-space normals the bake reprojects from the atlas are re-oriented per face to the new
# layout (snow_weather.reorient_normals), so nothing in the Normal map depends on the island angle.
UNFOLD = True
UNFOLD_MAX_DEG = 92.0           # merge across edges up to a box corner (90 deg) plus bevel slack
UNFOLD_EPS = 1e-7               # SAT shrink: faces touching along an edge are not "overlapping"
UNFOLD_MIN_FILL = 0.6           # a face joins only while island area / its PCA bounding box >= this:
                                # a hinged or bent tube would otherwise unroll into an L / V whose
                                # bounding box is mostly empty (the lance tube did: 0.53 x 0.75 UV)


UNFOLD_MAX_LEN_M = 2.0          # a chain of faces never unrolls longer than this (or than its longest
                                # single face): the lance tube unrolled side-cap-side into a 13.9 m
                                # strip and capped the whole asset's scale


def _obb(points, area):
    """(face area / PCA-oriented bounding box area, long side) of the 2-D `points`."""
    import numpy as np
    c = points - points.mean(0)
    w, v = np.linalg.eigh(c.T @ c)
    q = c @ v
    ext = q.max(0) - q.min(0)
    box = float(np.prod(ext))
    return (area / box if box > 1e-15 else 1.0), float(ext.max())


def _sat_overlap(p, q, eps=UNFOLD_EPS):
    """Convex 2-D polygons (k, 2) overlap with positive area (separating-axis test)."""
    import numpy as np
    for poly in (p, q):
        e = np.roll(poly, -1, axis=0) - poly
        axes = np.stack([-e[:, 1], e[:, 0]], 1)
        for ax in axes:
            n = np.hypot(ax[0], ax[1])
            if n < 1e-15:
                continue
            ax = ax / n
            a, b = p @ ax, q @ ax
            if a.max() <= b.min() + eps or b.max() <= a.min() + eps:
                return False
    return True


def _unfold_islands(mesh, pinned):
    """Write an unfolded start layout (meters) into the active UVUnique layer (see the block
    comment above). Returns {"islands": n, "faces": n_placed}. Deterministic: faces are visited
    in index order, roots are each part's largest unplaced face (ties -> lowest index)."""
    import math
    import numpy as np
    import cs2_uv
    n = len(mesh.polygons)
    co = np.empty(len(mesh.vertices) * 3, np.float64)
    mesh.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    lv = np.empty(len(mesh.loops), np.int64)
    mesh.loops.foreach_get("vertex_index", lv)
    le = np.empty(len(mesh.loops), np.int64)
    mesh.loops.foreach_get("edge_index", le)
    start, total = cs2_uv.face_loop_table(mesh)
    part = np.empty(n, np.int32)
    mesh.attributes["part"].data.foreach_get("value", part)
    layer = mesh.uv_layers[cs2_uv.UNIQUE_LAYER]
    uv = cs2_uv.loop_uvs(layer)
    atl = cs2_uv.loop_uvs(mesh.uv_layers["UVAtlas"])
    fv = [lv[start[f]:start[f] + total[f]] for f in range(n)]
    fn = np.zeros((n, 3))
    farea = np.zeros(n)
    for f in range(n):
        P = co[fv[f]]
        c = np.zeros(3)
        for i in range(len(P)):
            c += np.cross(P[i], P[(i + 1) % len(P)])
        a = np.linalg.norm(c)
        farea[f] = 0.5 * a
        fn[f] = c / a if a > 1e-15 else (0.0, 0.0, 1.0)
    edge_faces = {}
    for f in range(n):
        if f in pinned:
            continue
        for li in range(start[f], start[f] + total[f]):
            edge_faces.setdefault(int(le[li]), []).append(f)
    cos_max = math.cos(math.radians(UNFOLD_MAX_DEG))
    nbr = [[] for _ in range(n)]
    for e, fs in edge_faces.items():
        if len(fs) != 2:
            continue
        a, b = fs
        if part[a] != part[b] or float(fn[a] @ fn[b]) < cos_max:
            continue
        nbr[a].append(b)
        nbr[b].append(a)
    pos = {}                                   # face -> {vertex: (x, y)}
    placed = np.zeros(n, bool)
    for f in pinned:
        placed[f] = True
    island_of = np.full(n, -1, np.int64)
    n_isl = 0
    # roots: per part, largest unplaced face first
    order = sorted((f for f in range(n) if not placed[f]), key=lambda f: (int(part[f]), -farea[f], f))
    cursor_x = 0.0
    for root in order:
        if placed[root]:
            continue
        # 2-D frame of the root: e1 along the root's atlas-u direction projected into the face
        # plane (keeps the unique island roughly in the atlas orientation), e2 = n x e1 (no mirror)
        P = co[fv[root]]
        A2 = atl[start[root]:start[root] + total[root]]
        d3, d2 = P[1] - P[0], A2[1] - A2[0]
        d3b, d2b = P[2] - P[0], A2[2] - A2[0]
        J = np.array([d2, d2b]).T              # atlas uv deltas (2 x 2)
        if abs(np.linalg.det(J)) > 1e-14:
            Tu = (np.array([d3, d3b]).T @ np.linalg.inv(J))[:, 0]   # dP/du_atlas
        else:
            Tu = d3
        nr = fn[root]
        e1 = Tu - (Tu @ nr) * nr
        if np.linalg.norm(e1) < 1e-12:
            e1 = d3 - (d3 @ nr) * nr
        e1 /= np.linalg.norm(e1)
        e2 = np.cross(nr, e1)
        pos[root] = {int(v): (float((co[v] - P[0]) @ e1), float((co[v] - P[0]) @ e2)) for v in fv[root]}
        placed[root] = True
        island_of[root] = n_isl
        members = [root]
        isl_area = farea[root]
        polys = {root: np.array([pos[root][int(v)] for v in fv[root]])}
        isl_len = _obb(polys[root], farea[root])[1]
        queue = [root]
        while queue:
            f = queue.pop(0)
            for g in sorted(nbr[f]):
                if placed[g]:
                    continue
                shared = [int(v) for v in fv[g] if int(v) in pos[f]]
                if len(shared) != 2:
                    continue
                va, vb = shared
                a3, b3 = co[va], co[vb]
                a2, b2 = np.array(pos[f][va]), np.array(pos[f][vb])
                ed3 = b3 - a3
                L3 = np.linalg.norm(ed3)
                if L3 < 1e-12:
                    continue
                eh3 = ed3 / L3
                ed2 = b2 - a2
                eh2 = ed2 / max(np.linalg.norm(ed2), 1e-15)
                perp = np.array((-eh2[1], eh2[0]))
                fc = polys[f].mean(0)
                if (fc - a2) @ perp > 0:           # f lies on +perp: g goes to -perp
                    perp = -perp
                pg = {}
                for v in fv[g]:
                    v = int(v)
                    if v == va or v == vb:
                        pg[v] = tuple(pos[f][v])
                        continue
                    r = co[v] - a3
                    t = r @ eh3
                    h = np.linalg.norm(r - t * eh3)
                    pg[v] = tuple(a2 + t * eh2 + h * perp)
                poly = np.array([pg[int(v)] for v in fv[g]])
                lo, hi = poly.min(0), poly.max(0)
                clash = False
                for m in members:
                    q = polys[m]
                    if (q.min(0) > hi).any() or (q.max(0) < lo).any():
                        continue
                    if _sat_overlap(poly, q):
                        clash = True
                        break
                if clash:
                    continue
                fill_new, len_new = _obb(np.concatenate([polys[m] for m in members] + [poly]),
                                         isl_area + farea[g])
                if len(members) >= 2 and fill_new < UNFOLD_MIN_FILL:
                    continue
                if len_new > max(isl_len, _obb(poly, farea[g])[1], UNFOLD_MAX_LEN_M) + 1e-9:
                    continue
                pos[g] = pg
                isl_area += farea[g]
                isl_len = len_new
                polys[g] = poly
                placed[g] = True
                island_of[g] = n_isl
                members.append(g)
                queue.append(g)
        # lay islands side by side (the packer moves them anyway; no overlap at the start)
        allp = np.concatenate([polys[m] for m in members])
        lo = allp.min(0)
        for m in members:
            for li, v in zip(range(start[m], start[m] + total[m]), fv[m]):
                x, y = pos[m][int(v)]
                uv[li] = (x - lo[0] + cursor_x, y - lo[1])
        cursor_x += float(allp[:, 0].max() - lo[0]) + 0.05
        n_isl += 1
    cs2_uv.set_loop_uvs(layer, uv)
    return {"islands": n_isl, "faces": int((island_of >= 0).sum())}


# -- The pack itself (REDO): our own shelf packer with EXACT margins -------------------------------
# Blender's pack_islands could not hold a mip-safe margin on these layouts: FRACTION / ADD left
# 2-4 x the asked gap and collapsed the tower's fill to 0.07-0.22 at 16 px (the margin is applied
# at the islands' pre-pack scale), SCALED ignored it (2 px gaps); margin study in the write-up.
# `_shelf_layout` packs each island's minimum-area bounding rectangle (rotating calipers on its
# hull, long side horizontal) first-fit-decreasing-height into full-width shelves: every island
# keeps exactly `margin` texels to its neighbors and margin / 2 to the band edges, the DARK
# islands fill the shelves from the bottom of the band, the light ones start DARK_GAP_MARGINS
# margins above the last dark shelf, and ONE scale (binary search, the largest that fits) is
# shared by both, so the tier weights hold exactly. Rotation is safe because
# snow_weather.reorient_normals turns every reprojected tangent-space normal by its face's
# atlas -> unique rotation. An island longer than REPACK_MAX_LEN of the width at the tested scale
# is compressed along its long axis down to REPACK_MIN_SQUASH (block comment at REPACK_MAX_LEN).


def _hull(pts):
    """Convex hull (monotone chain), counter-clockwise, no repeated end point."""
    import numpy as np
    p = np.unique(np.round(pts, 12), axis=0)
    if len(p) < 3:
        return p
    p = p[np.lexsort((p[:, 1], p[:, 0]))]

    def half(seq):
        out = []
        for q in seq:
            while len(out) >= 2 and ((out[-1][0] - out[-2][0]) * (q[1] - out[-2][1])
                                     - (out[-1][1] - out[-2][1]) * (q[0] - out[-2][0])) <= 0:
                out.pop()
            out.append(tuple(q))
        return out
    lower, upper = half(p), half(p[::-1])
    return np.array(lower[:-1] + upper[:-1])


def _min_rect_angle(pts):
    """Rotation (radians) that puts the minimum-area bounding rectangle of `pts` axis-aligned with
    its LONG side along +u (rotating calipers; ties -> the smallest angle)."""
    import math
    import numpy as np
    h = _hull(pts)
    if len(h) < 3:
        d = h[-1] - h[0] if len(h) == 2 else np.array((1.0, 0.0))
        return -math.atan2(d[1], d[0])
    best = None
    e = np.roll(h, -1, axis=0) - h
    for a in sorted(set(round(math.atan2(y, x) % (math.pi / 2), 12) for x, y in e)):
        c, s = math.cos(-a), math.sin(-a)
        q = h @ np.array(((c, s), (-s, c)))
        w, ht = np.ptp(q[:, 0]), np.ptp(q[:, 1])
        if best is None or w * ht < best[0] - 1e-15:
            best = (w * ht, -a, w, ht)
    _area, ang, w, ht = best
    if ht > w:
        ang += math.pi / 2
    return ang


def _ffdh(sizes, width):
    """First-fit decreasing height shelves. sizes = [(w, h)] incl. margin. -> ([(x, y)], height)
    or None when an item is wider than the shelf."""
    order = sorted(range(len(sizes)), key=lambda i: (-sizes[i][1], -sizes[i][0], i))
    shelves = []
    pos = [None] * len(sizes)
    top = 0.0
    for i in order:
        w, h = sizes[i]
        if w > width + 1e-9:
            return None
        for sh in shelves:
            if sh[2] + w <= width + 1e-9 and h <= sh[1] + 1e-9:
                pos[i] = (sh[2], sh[0])
                sh[2] += w
                break
        else:
            shelves.append([top, h, w])
            pos[i] = (0.0, top)
            top += h
    return pos, top


def _shelf_layout(rects, dark, A, band_px, margin_px, gap_px):
    """Largest scale s (texels per UV-unit) at which every rect (w, h), padded by margin_px, fits:
    dark shelves from 0, light shelves from (dark height + gap_px). Long rects squash along u.
    -> dict(s, pos [(x, y) px of the padded footprint], squash [factor per rect], heights)."""
    width = float(A)

    def attempt(s):
        squash = []
        sizes = []
        for (w, h) in rects:
            k = 1.0
            if w * s > REPACK_MAX_LEN * width:
                k = REPACK_MAX_LEN * width / (w * s)
                if k < REPACK_MIN_SQUASH:
                    return None
            squash.append(k)
            sizes.append((w * s * k + margin_px, h * s + margin_px))
        di = [i for i in range(len(rects)) if dark[i]]
        li = [i for i in range(len(rects)) if not dark[i]]
        pos = [None] * len(rects)
        hd = 0.0
        if di:
            r = _ffdh([sizes[i] for i in di], width)
            if r is None:
                return None
            for j, i in enumerate(di):
                pos[i] = r[0][j]
            hd = r[1]
        y0 = hd + (gap_px if di and li else 0.0)
        hl = 0.0
        if li:
            r = _ffdh([sizes[i] for i in li], width)
            if r is None:
                return None
            for j, i in enumerate(li):
                pos[i] = (r[0][j][0], r[0][j][1] + y0)
            hl = r[1]
        if y0 + hl > band_px + 1e-9:
            return None
        return {"s": s, "pos": pos, "squash": squash, "dark_px": hd, "light_px": hl}

    lo, hi = 0.0, None
    s = 64.0
    while attempt(s) is not None:            # bracket
        lo, s = s, s * 2.0
        if s > 1e7:
            break
    hi = s
    for _ in range(40):
        mid = 0.5 * (lo + hi)
        if attempt(mid) is not None:
            lo = mid
        else:
            hi = mid
    res = attempt(lo)
    if res is None:
        raise RuntimeError("shelf pack: nothing fits (an island wider than the atlas at any scale?)")
    return res


def _pack_once(obj, mesh, A, reserved, density_layer, share=None, dark_face=None):
    """The REDO pack (block comment above): weights, rotate, shelf-pack, place, share copies.
    `share` = cs2_liftdress.share_identical list: translation-identical copies (rungs, nuts,
    studs, cage hoops...) are held back and take their canonical island's UVs afterwards.
    Returns the layout facts."""
    import math
    import numpy as np
    import cs2_uv
    layer = mesh.uv_layers[cs2_uv.UNIQUE_LAYER]
    mesh.uv_layers.active = layer
    pinned = cs2_uv.pinned_faces(mesh, layer, reserved)
    hold = set(pinned)
    src = dst = None
    if share:
        dup, src, dst = cs2_uv._share_loops(mesh, share, pinned)
        hold |= dup
    if not UNFOLD:                           # unfolded islands are already true meters
        cs2_uv._select_unpinned(obj, mesh, hold)
        bpy.ops.uv.average_islands_scale()
        bpy.ops.object.mode_set(mode="OBJECT")
    layer = mesh.uv_layers[cs2_uv.UNIQUE_LAYER]
    weights = cs2_uv.face_density_weights(mesh, density_layer)
    cs2_uv._scale_islands_by_weight(mesh, layer, hold, weights)
    free = [i for i in range(len(mesh.polygons)) if i not in hold]
    islands = cs2_uv.uv_islands(mesh, layer, faces=free)
    start, total = cs2_uv.face_loop_table(mesh)
    uv = cs2_uv.loop_uvs(layer)
    loops, rects, angles, dark = [], [], [], []
    for isl in islands:
        li = np.concatenate([np.arange(start[f], start[f] + total[f]) for f in isl])
        q = uv[li]
        a = _min_rect_angle(q)
        c, s = math.cos(a), math.sin(a)
        r = q @ np.array(((c, s), (-s, c)))
        r -= r.min(0)
        uv[li] = r
        loops.append(li)
        rects.append((float(r[:, 0].max()), float(r[:, 1].max())))
        angles.append(a)
        dark.append(bool(dark_face is not None and np.mean(dark_face[isl]) >= 0.5))
    lo, hi = cs2_uv.free_v_band(reserved)
    mpx = float(repack_margin_px(A))
    band_px = (hi - lo) * A
    res = _shelf_layout(rects, dark, A, band_px, mpx, DARK_GAP_MARGINS * mpx)
    s = res["s"]
    for k, li in enumerate(loops):
        x, y = res["pos"][k]
        q = uv[li] * s
        q[:, 0] *= res["squash"][k]
        uv[li] = (q + np.array((x + 0.5 * mpx, y + 0.5 * mpx))) / A + np.array((0.0, lo))
    cs2_uv.set_loop_uvs(layer, uv)
    if share and len(dst):
        uv = cs2_uv.loop_uvs(layer)
        uv[dst] = uv[src]
        cs2_uv.set_loop_uvs(layer, uv)
    cs2_uv.check_layout(mesh, layer, reserved, pinned)
    sq = [round(k, 3) for k in res["squash"] if k < 1.0]
    return {"band": (round(lo, 4), round(hi, 4)), "scale_px_per_unit": round(s, 3),
            "islands": len(islands), "dark_islands": int(sum(dark)), "light_islands": len(dark) - int(sum(dark)),
            "dark_px": round(res["dark_px"], 1), "light_px": round(res["light_px"], 1),
            "squashed": sq}


REPACK_SHARE = True             # stack translation-identical copies on one island
REPACK_SHARE_CAP_M2 = 0.5       # only small parts stack (rungs, nuts, studs, hoops, clamps)
REPACK_GROUND_BAND = (0.0, 0.45)  # grade copies stack only together (a grade-soiled rung never
                                  # lends its UVs to a clean one)


def _share_list(obj, mesh, names, reserved):
    import cs2_liftdress
    return cs2_liftdress.share_identical(obj, names, (), cap_m2=REPACK_SHARE_CAP_M2,
                                         reserved=reserved, uv_layer="UVUnique",
                                         ground_band=REPACK_GROUND_BAND)


def density_tier(name):
    """'small' | 'shell' | 'mast' | 'rest' for a part name (small first: CompEnd before Comp*)."""
    if name.startswith(DENSITY_SMALL):
        return "small"
    if name.startswith(DENSITY_SHELL):
        return "shell"
    if name.startswith(DENSITY_MAST):
        return "mast"
    return "rest"


def density_packer(obj, mesh, A, reserved):
    """
    Packer hook for lift_tower.apply_finish_textures(packer=): "UVUnique" exists (a copy of
    UVAtlas) and is active. Unfolds the islands (UNFOLD), weights them per tier (DENSITY_REL),
    shelf-packs dark and light islands at ONE scale with exact mip-safe margins (_pack_once) and
    records LAST_REPACK: per-tier px/m, pack fill, margin, squashed islands. Leaves no attribute
    behind (the weights live in a temporary FACE attribute).
    """
    import json
    import numpy as np
    import cs2_uv
    global LAST_REPACK
    names = json.loads(mesh[cs2_asset.PART_TABLE_PROP])
    n = len(mesh.polygons)
    part = np.empty(n, np.int32)
    mesh.attributes["part"].data.foreach_get("value", part)
    tiers = np.array([density_tier(names[i - 1]) for i in part])
    rel_face = np.array([DENSITY_REL[t] for t in tiers])
    zone = _face_zones(mesh, A)
    dark_face = np.array([z in DARK_ZONES for z in zone])
    lay = mesh.uv_layers[cs2_uv.UNIQUE_LAYER]
    pinned = cs2_uv.pinned_faces(mesh, lay, reserved)
    free = np.ones(n, bool)
    free[list(pinned)] = False
    wa = cs2_uv.face_world_areas(mesh)
    tmp = "snow_density_w"
    share = _share_list(obj, mesh, names, reserved) if REPACK_SHARE else None
    unfold = _unfold_islands(mesh, pinned) if UNFOLD else None
    canon = free.copy()             # area that costs texels: stacked copies are free
    if share:
        for _c, dps in share:
            for dp in dps:
                canon[dp] = False
    area = {t: float(wa[(tiers == t) & canon].sum()) for t in ("shell", "small", "mast", "rest")}
    attr = mesh.attributes.get(tmp) or mesh.attributes.new(tmp, "FLOAT", "FACE")
    attr.data.foreach_set("value", rel_face.tolist())
    info = _pack_once(obj, mesh, A, reserved, tmp, share=share, dark_face=dark_face)
    mesh.attributes.remove(mesh.attributes[tmp])
    lay = mesh.uv_layers[cs2_uv.UNIQUE_LAYER]
    d = cs2_uv.face_px_per_m(mesh, lay, A)
    ok = free & np.isfinite(d)

    def rms(sel, x=d):
        return float(np.sqrt((x[sel] ** 2 * wa[sel]).sum() / max(wa[sel].sum(), 1e-12)))             if sel.any() else None
    got = {t: rms(ok & (tiers == t)) for t in area}
    k = d / rel_face                                   # density per unit weight
    uva = cs2_uv.face_uv_areas(mesh, cs2_uv.loop_uvs(lay))
    used = float(uva[ok & canon].sum())
    lo, hi = info["band"]
    final = {"weights": dict(DENSITY_REL),
             "px_per_m": {t: (round(v, 1) if v is not None else None) for t, v in got.items()},
             "k_dark": (round(rms(ok & dark_face, k), 1) if (ok & dark_face).any() else None),
             "k_light": round(rms(ok & ~dark_face, k), 1),
             "fill_band": round(used / max(hi - lo, 1e-9), 4), "fill_atlas": round(used, 4),
             "texels_used": round(used * A * A), "layout": info}
    LAST_REPACK = {"A": int(A), "margin_px": repack_margin_px(A), "dark_gap_margins": DARK_GAP_MARGINS,
                   "unfold": unfold, "area_m2": {t: round(v, 2) for t, v in area.items()},
                   "dark_area_m2": round(float(wa[dark_face & canon].sum()), 2),
                   "shared": (share.report if share else None),
                   "passes": [final], "final": final}
    print(f"[repack] {obj.name} @ {A}: margin {repack_margin_px(A)} px, islands {info['islands']} "
          f"({info['dark_islands']} dark), fill {final['fill_band']:.3f}, px/m {final['px_per_m']}, "
          f"squashed {info['squashed']}")
    return None


def build_maps(key, obj, name, asset_dir, quad_size, do_finish=False, weather=False,
               repack=False, post_hook=None, weather_step=2):
    """
    The texture half of export_one: atlas maps (+ emissive) into `asset_dir`, then the
    finish rebake. Split out (T20) so the preview build can make the same maps WITHOUT
    writing an FBX or settings.json.

    `weather` (2026-10-17, opt-in until the verdict): run snow_weather's clean-style
    texture pass inside the finish bake (post_compose). Pixels only: the mesh, UVs, LODs and
    every file name are unchanged. Needs do_finish. `weather_step` (REDO, wiki style): 1 =
    smoothness / metal parity + flat clean base + normal-map detail, 2 = step 1 + the
    low-frequency soiling (snow_weather module docstring).

    `repack` (2026-10-17, opt-in): density_packer replaces the finish pass's UV pack (new
    UVUnique layout, same geometry / tris / file set). `post_hook`: a caller's own
    post_compose (measurement drivers); ignored when `weather` is set.
    """
    global LAST_WEATHER
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
    if (weather or repack) and not do_finish:
        raise RuntimeError("--weather / --repack run inside the finish bake: pass --finish")
    if do_finish:
        zones = cs2_asset.snow_prop_zones(A)
        hook = post_hook
        if weather:
            import snow_weather
            hook = snow_weather.make_hook(obj, name, step=weather_step, key=key)
            LAST_WEATHER = hook
        elif repack:
            # the REDO packer rotates islands: re-orient the reprojected normals (N0) even
            # without the texture pass
            import snow_weather
            hook = snow_weather.ReorientHook(obj, inner=post_hook)
        ok = lift_tower.apply_finish_textures(obj, asset_dir, name, atlas_size=A,
                                              reserved_strips=[zones["strip"]],
                                              ao_samples=24, post_compose=hook,
                                              packer=density_packer if repack else None)
        if (weather or repack) and not ok:
            raise RuntimeError(f"{name}: finish bake failed, the weathering / repack pass did not run")
        print(f"[{key}]   finish: AO rebake {'ok' if ok else 'FAILED (shipping bevels only)'}")

    # TEXTURE BUDGET GUARD. The first version divided quad_size by the LONGEST DIMENSION,
    # which is not the metric the budget was set with and passed everything trivially
    # (the tower scored 69 and sailed through while actually running 250). The budget is
    # atlas_px / sqrt(surface_area) — texels per meter of SKIN, not of bounding box.
    area = mesh_area(obj)
    px_per_m = (quad_size * 2) / math.sqrt(max(area, 1e-3))
    print(f"[{key}]   atlas {quad_size * 2}px over {area:.1f} m^2 -> {px_per_m:.0f} px/m")
    if repack:
        # REDO (wiki style, 2026-10-17): the 2026-08 60-150 px/m band and its 200 ceiling were a
        # mesh-area metric + memory budget, not vanilla (vanilla props 158-2,520 px/m, median
        # ~910; wiki-best-practices-gap-2026-10-17.md section 2a). With --repack the size rules
        # are the wiki floor and the materials doc's no-2048-under-10-m ceiling; the per-PART
        # density is gated by cs2_validate.
        A = quad_size * 2
        if A < TEX_FLOOR:
            raise RuntimeError(f"{name}: {A} px texture is below the wiki floor {TEX_FLOOR} "
                               f"(Asset Creation Guide: anything smaller is unsupported)")
        if A > TEX_CEIL_UNDER_10M and max(obj.dimensions) < 10.0:
            raise RuntimeError(f"{name}: {A} px on a {max(obj.dimensions):.1f} m prop - never "
                               f"2048 for anything under 10 m (snow-prop-materials.md section 5)")
    elif px_per_m > 200:
        raise RuntimeError(f"{name}: {px_per_m:.0f} px/m exceeds the 200 px/m ceiling — "
                           f"halve quad_size (snow-prop-materials.md section 5)")
    return px_per_m


def export_one(key, obj, name, out_root, quad_size, do_finish=False, weather=False,
               repack=False, weather_step=2):
    asset_dir = os.path.join(os.path.abspath(out_root), name)
    build_maps(key, obj, name, asset_dir, quad_size, do_finish=do_finish, weather=weather,
               repack=repack, weather_step=weather_step)

    cs2_asset.export_fbx(obj, os.path.join(asset_dir, f"{name}.fbx"))

    # Authored LOD chain, decimated from the finished mesh. Supplying an LOD FBX makes the
    # importer skip generation for that level (they are mutually exclusive), so an asset
    # either ships a full chain or relies on the default — which, with stock settings,
    # generates NOTHING. See the write_settings docstring correction.
    lods = make_lods(key, obj, name)
    for i, lod in enumerate(lods, start=1):
        print(f"[{key}]   LOD{i}: {tri_count(lod)} tris")
        cs2_asset.export_fbx(lod, os.path.join(asset_dir, f"{name}_LOD{i}.fbx"))
        bpy.data.objects.remove(lod, do_unlink=True)
    cs2_asset.write_settings(asset_dir, name, supplied_lods=tuple(range(1, len(lods) + 1)))
    print(f"[{key}]   asset folder: {asset_dir}")
    return asset_dir


# --------------------------------------------------------------------------------------
# Preview: one lineup image beats four separate renders when judging VARIETY
# --------------------------------------------------------------------------------------

# Manufacturer liveries as TARGET colors (slot 1 housing, slot 2 safety, slot 3 accent).
# Art direction read off product photography, not certified brand fact — see
# snowmaking-props-spec.md section 6.1. Every channel caps at RECOLOR_ALBEDO (0.80):
# recolor tints MULTIPLY a light-neutral albedo, so 0.80 is the brightest reachable value.
LIVERIES = [
    ("technoalpin", (0.80, 0.62, 0.04), (0.80, 0.24, 0.09), (0.22, 0.23, 0.25)),
    ("legacyblue",  (0.10, 0.30, 0.70), (0.80, 0.24, 0.09), (0.72, 0.74, 0.76)),
    ("demaclenko",  (0.10, 0.10, 0.12), (0.80, 0.24, 0.09), (0.62, 0.14, 0.10)),
    ("smiblue",     (0.14, 0.38, 0.66), (0.80, 0.24, 0.09), (0.74, 0.75, 0.77)),
]


def make_tinted_preview(asset_dir, name, out_dir, livery):
    """
    Bake the recolor tints into a PREVIEW-ONLY BaseColor.

    In game the albedo under every recolor mask is light-neutral RECOLOR_ALBEDO gray and
    the picker tint multiplies it, so a straight preview render shows gray machines and
    tells you nothing about livery. This reproduces what CS2 will actually display:
    read the ControlMask, build a per-texel tint from the three slot colors, multiply.
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
    """Node lookup by TYPE, not by name — the shader node's name is localization- and
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
    # does, so this preview is a faithful rehearsal of the shipped behavior.
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


# --------------------------------------------------------------------------------------
# Detail-pass previews (T20, 2026-10-16): the preview gate for generation B
# --------------------------------------------------------------------------------------

# Preview tint = the TechnoAlpin livery row of LIVERIES as picker multipliers
# (target / RECOLOR_ALBEDO, the make_tinted_preview math): yellow machine, orange safety,
# dark accent.
DETAIL_TINT = {"R": (1.0, 0.775, 0.05), "G": (1.0, 0.30, 0.11), "B": (0.275, 0.29, 0.31)}
# Camera directions in the net frame (machine forward = -Y after orient_for_net). The
# tower's ladder and cage are on its BACK (+Y), so towers get a fourth view of them.
DETAIL_VIEWS = (("front", (0.0, -1.0, 0.15)), ("side", (1.0, 0.0, 0.12)),
                ("three_quarter", (1.0, -1.2, 0.45)))
DETAIL_LADDER_VIEW = ("ladder", (-0.75, 1.0, 0.30))
FAR_M = 150.0
FAR_DIR = (1.0, -1.25, 0.20)


def _muzzle_shot(obj, asset_dir, name, out_path, tint, pitch_deg, yaw_deg, dist):
    """Close-up into the nozzle ring (verdict 3: the fan blades must read through it). The
    camera sits on the barrel axis in front of the muzzle, slightly off-axis, aimed at the
    blades centroid (part "*Blades"). Net frame: author (x, y, z) -> (y, -x, z)."""
    import json as _json
    import cs2_preview as P
    table = _json.loads(obj.data[cs2_asset.PART_TABLE_PROP])
    attr = obj.data.attributes["part"].data
    ids = {i + 1 for i, n in enumerate(table) if n.endswith("Blades")}
    pts = [obj.matrix_world @ obj.data.vertices[v].co for p in obj.data.polygons
           if attr[p.index].value in ids for v in p.vertices]
    if not pts:
        return None
    ctr = sum(pts, mathutils.Vector()) / len(pts)
    p, y = math.radians(pitch_deg), math.radians(yaw_deg)
    f = mathutils.Vector((math.cos(p) * math.sin(y), -math.cos(p) * math.cos(y), math.sin(p)))
    side = f.cross(mathutils.Vector((0, 0, 1))).normalized()
    rig = P._Rig(obj, None)
    try:
        cam = P._make_camera(rig)
        P._make_world(rig, **P.NOON)
        P._make_ground(rig)
        P._assign(obj, P._pbr_material(rig, asset_dir, name, tint, mat_name=f"{obj.name}_Muz"))
        rig.scene.render.resolution_x, rig.scene.render.resolution_y = 1000, 1000
        P._aim(cam, ctr, f + side * 0.25 + mathutils.Vector((0, 0, 0.10)), dist)
        P._render(rig, out_path, engine="EEVEE")
    finally:
        rig.restore()
    print(f"[detail] muzzle close-up -> {out_path}")
    return out_path


def _far_shot(obj, asset_dir, name, out_path, tint, label):
    """Render `obj` alone at FAR_M through the cs2_preview rig, noon. Writes
    [full frame | 4x nearest crop of the center] so the pixels the object really gets at
    150 m are visible next to the frame they live in."""
    import numpy as np
    import cs2_preview as P
    rig = P._Rig(obj, None)
    try:
        cam = P._make_camera(rig)
        P._make_world(rig, **P.NOON)
        P._make_ground(rig)
        P._assign(obj, P._pbr_material(rig, asset_dir, name, tint, mat_name=f"{obj.name}_Far"))
        sc = rig.scene
        sc.render.resolution_x, sc.render.resolution_y = 1000, 700
        bb = [obj.matrix_world @ mathutils.Vector(c) for c in obj.bound_box]
        ctr = sum(bb, mathutils.Vector()) / 8.0
        P._aim(cam, ctr, FAR_DIR, FAR_M)
        tmp = out_path[:-4] + "_tmp.png"
        P._render(rig, tmp, engine="EEVEE")
        img = P._load_rgba(tmp)
        h, w = img.shape[:2]
        c = img[h // 2 - 87:h // 2 + 88, w // 2 - 125:w // 2 + 125]
        crop = np.repeat(np.repeat(c, 4, axis=0), 4, axis=1)[:h]
        P._write_rgba(out_path, P._hstack([img, crop]))
        os.remove(tmp)
    finally:
        rig.restore()
    print(f"[detail] far {FAR_M:.0f} m ({label}) -> {out_path}")
    return out_path


def cad_metric(obj):
    """
    The cabin round's "straight outta CAD" metric (lift_cabin.md R1.5 / revision1b):
      coplanar = same-facing coplanar overlapping triangle pairs and their area (cm2), via
                 lift_cabin.coplanar_overlaps (1 deg / 0.5 mm / > 0.1 cm2);
      hard90_m = length of edges with an 80-100 deg dihedral whose corner normals SPLIT
                 (they render as a razor line); hard90_geo_m = the same edges regardless of
                 shading (soft-shaded square bars still count here).
    """
    import numpy as np
    import lift_cabin
    me = obj.data
    cn = np.zeros(len(me.loops) * 3, np.float32)
    me.corner_normals.foreach_get("vector", cn)
    cn = cn.reshape(-1, 3)
    ek = {}
    for p in me.polygons:
        ls = list(p.loop_indices)
        vs = [me.loops[i].vertex_index for i in ls]
        for i in range(len(vs)):
            a, b = vs[i], vs[(i + 1) % len(vs)]
            ek.setdefault((min(a, b), max(a, b)), []).append(
                (p.index, {a: ls[i], b: ls[(i + 1) % len(vs)]}))
    geo = hard = 0.0
    cos1 = math.cos(math.radians(1.0))
    for (a, b), fl in ek.items():
        if len(fl) != 2:
            continue
        ang = math.degrees(me.polygons[fl[0][0]].normal.angle(me.polygons[fl[1][0]].normal, 0.0))
        if 80.0 <= ang <= 100.0:
            L = (me.vertices[a].co - me.vertices[b].co).length
            geo += L
            if any(float(cn[fl[0][1][v]] @ cn[fl[1][1][v]]) < cos1 for v in (a, b)):
                hard += L
    tot, _ = lift_cabin.coplanar_summary(lift_cabin.coplanar_overlaps(obj))
    return {"hard90_m": round(hard, 2), "hard90_geo_m": round(geo, 2),
            "coplanar_same": [tot["same"]["pairs"], tot["same"]["area_cm2"]],
            "coplanar_opposed": [tot["opposed"]["pairs"], tot["opposed"]["area_cm2"]]}


def _vstack(rows, pad=6):
    import numpy as np
    w = max(r.shape[1] for r in rows)
    out = []
    for i, r in enumerate(rows):
        if r.shape[1] < w:
            r = np.concatenate([r, np.full((r.shape[0], w - r.shape[1], 4), (0.5, 0.5, 0.5, 1.0),
                                           np.float32)], axis=1)
        out.append(r)
        if i < len(rows) - 1:
            out.append(np.full((pad, w, 4), (0.5, 0.5, 0.5, 1.0), np.float32))
    return np.concatenate(out[::-1], axis=0)   # Blender pixel rows run bottom-up


def detail_previews(out_dir, keys=("tower", "lance", "fan", "hydrant"), review=False,
                    prev_root=None):
    """
    Build every generation-B variant exactly as the export would (bevel + part tags +
    shading, atlas maps + AO finish, LOD chain) but write NO FBX and no settings.json: the
    maps land in <out_dir>/_build/<Name>/ only because the renders (and the review packs)
    need them. Renders, per variant: pbr front / side / three-quarter (+ a ladder view on
    towers), matcap normals for the same views, and the 150 m shot with the LOD the game
    would draw there. Plus the A-vs-B pair for the Tall tower and a contact sheet.
    Returns {name: stats} (also written to <out_dir>/stats.json).
    """
    import json as _json
    import cs2_preview as P
    out_dir = os.path.abspath(out_dir)
    build_root = os.path.join(out_dir, "_build")
    prev_root = prev_root or os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                          "..", "import", "props", "A")
    stats = {}
    for key in keys:
        for name, kwargs, qs in VARIANTS[key]:
            clear_scene()
            parts = BUILDER[key](PARAM_CLASS[key](**kwargs))
            obj = finish(parts, name, qs * 2, bevel=True, smooth=key in SMOOTH_ASSETS)
            cs2_asset.validate_asset_name(name)
            t0 = report(key, obj)
            asset_dir = os.path.join(build_root, name)
            px = build_maps(key, obj, name, asset_dir, qs, do_finish=True)
            lods = make_lods(key, obj, name)
            tris = [t0] + [tri_count(lo) for lo in lods]
            area = mesh_area(obj)
            table = P.lod_switch_table(tuple(obj.dimensions), tris, area=area)
            views = list(DETAIL_VIEWS) + ([DETAIL_LADDER_VIEW] if key == "tower" else [])
            P.preview(obj, asset_dir, name, out_dir, modes=("pbr", "normals"), views=views,
                      tint=DETAIL_TINT, resolution=(1000, 1100))
            # Which level the game draws at FAR_M (LODk from switch[k] outward).
            level = max([0] + [k for k, d in table["switch"].items() if FAR_M >= d])
            far_obj = obj if level == 0 else lods[level - 1]
            culled = FAR_M > table["cull"]
            _far_shot(far_obj, asset_dir, name, os.path.join(out_dir, f"{name}_far150m.png"),
                      DETAIL_TINT, f"LOD{level}, {tris[level]} tris" + (", CULLED" if culled else ""))
            if key in ("tower", "fan"):
                prm = PARAM_CLASS[key](**kwargs)
                _muzzle_shot(obj, asset_dir, name, os.path.join(out_dir, f"{name}_pbr_muzzle.png"),
                             DETAIL_TINT, prm.pitch_deg, prm.yaw_deg, 2.4 if key == "tower" else 2.8)
            if name == "SkiSnowGunTowerTallB":
                P.preview(obj, asset_dir, name, out_dir, modes=("ab",), views=views,
                          tint=DETAIL_TINT, resolution=(1000, 1100),
                          prev_generation=os.path.join(prev_root, "SkiSnowGunTowerTallA"))
            if review:
                import cs2_review
                cs2_review.write_pack(name, asset_dir,
                                      {0: obj, **{i + 1: lo for i, lo in enumerate(lods)}},
                                      meta={"generator": "snow_props.py", "args": ["--detail"],
                                            "kind": "prop", "letter": "B",
                                            "caps": {0: BUDGETS[key][1]}})
            stats[name] = {"tris": tris, "dims_m": [round(v, 2) for v in obj.dimensions],
                           "px_per_m": round(px, 1), "area_m2": round(area, 1),
                           "switch_m": {k: round(v, 1) for k, v in table["switch"].items()},
                           "cull_m": round(table["cull"], 1), "lod_at_150m": level,
                           "culled_at_150m": culled, "cad": cad_metric(obj)}
            for lo in lods:
                bpy.data.objects.remove(lo, do_unlink=True)

    # The A side of the 150 m pair: the shipped Tall tower with ITS LOD at that range.
    a_dir = os.path.join(prev_root, "SkiSnowGunTowerTallA")
    if os.path.isdir(a_dir) and "tower" in keys:
        clear_scene()
        a_objs = [P.import_generation(os.path.join(a_dir, f"SkiSnowGunTowerTallA{s}.fbx"))
                  for s in ("", "_LOD1", "_LOD2")]
        a_tris = [tri_count(o) for o in a_objs]
        t = P.lod_switch_table(tuple(a_objs[0].dimensions), a_tris, area=mesh_area(a_objs[0]))
        lv = max([0] + [k for k, d in t["switch"].items() if FAR_M >= d])
        _far_shot(a_objs[lv], a_dir, "SkiSnowGunTowerTallA",
                  os.path.join(out_dir, "SkiSnowGunTowerTallA_far150m.png"), DETAIL_TINT,
                  f"A LOD{lv}, {a_tris[lv]} tris")
        stats["SkiSnowGunTowerTallA (shipped)"] = {
            "tris": a_tris, "switch_m": {k: round(v, 1) for k, v in t["switch"].items()},
            "cull_m": round(t["cull"], 1), "lod_at_150m": lv, "cad": cad_metric(a_objs[0])}

    # Contact sheet: three-quarter pbr of every variant (half size, VARIANTS order, 5 per
    # row), the muzzle close-ups (Tall, wheeled), the Tall A|B pairs (side, ladder), then the
    # Tall 150 m pair A | B.
    def tile(n, view="three_quarter"):
        pth = os.path.join(out_dir, f"{n}_pbr_{view}.png")
        return P._load_rgba(pth)[::2, ::2] if os.path.exists(pth) else None
    order = [n for k in keys for n, _kw, _q in VARIANTS[k]]
    tiles = [t for t in (tile(n) for n in order) if t is not None]
    rows = [P._hstack(tiles[i:i + 5]) for i in range(0, len(tiles), 5)]
    muz = [os.path.join(out_dir, f"{n}_pbr_muzzle.png") for n in ("SkiSnowGunTowerTallB", "SkiSnowGunFanB")]
    muz = [P._load_rgba(m)[::2, ::2] for m in muz if os.path.exists(m)]
    if muz:
        rows.append(P._hstack(muz))
    pairs = [os.path.join(out_dir, f"SkiSnowGunTowerTallB_ab_{v}.png") for v in ("side", "ladder")]
    pairs = [P._load_rgba(pth)[::2, ::2] for pth in pairs if os.path.exists(pth)]
    if pairs:
        rows.append(P._hstack(pairs))
    far = [os.path.join(out_dir, f) for f in ("SkiSnowGunTowerTallA_far150m.png",
                                              "SkiSnowGunTowerTallB_far150m.png")]
    if all(os.path.exists(f) for f in far):
        rows.append(P._hstack([P._load_rgba(f)[::2, ::2] for f in far]))
    if rows:
        sheet = os.path.join(out_dir, "contact_sheet.png")
        P._write_rgba(sheet, _vstack(rows))
        print(f"[detail] contact sheet ({', '.join(order)}) -> {sheet}")
    with open(os.path.join(out_dir, "stats.json"), "w", encoding="utf-8") as fh:
        _json.dump(stats, fh, indent=1)
    return stats


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
    ap.add_argument("--detail", default=None,
                    help="T20 preview gate: build every variant of --assets (maps, LODs, no FBX) "
                         "and render the 3-view + 150 m set, A/B pair and contact sheet here")
    ap.add_argument("--weather", action="store_true",
                    help="with --finish: clean-style texture pass (snow_weather.py, REDO wiki "
                         "style 2026-10-17); pixels only, geometry unchanged")
    ap.add_argument("--weather-step", type=int, default=2, choices=(1, 2),
                    help="with --weather: 1 = parity + flat clean base + normal-map detail, "
                         "2 = step 1 + low-frequency soiling (default)")
    ap.add_argument("--repack", action="store_true",
                    help="with --finish: per-tier texel-density repack of the unique UVs "
                         "(density_packer; REDO: mip-safe margins, dark band) at the REDO texture "
                         "sizes (TEX_REDO: tower / fan / lance 1024, hydrant 512); new UV layout, "
                         "same geometry")
    ap.add_argument("--tex", default=None,
                    help="texture size override: B (the exported B sizes), an int, or "
                         "tower=1024,fan=1024,lance=512,hydrant=512 (default: TEX_REDO with "
                         "--repack, B without)")
    ap.add_argument("--review", action="store_true",
                    help="with --detail: also write assets/preview/review/<Name>.blend packs")
    return ap.parse_args(argv)


def main():
    args = parse_args()
    keys = [k.strip() for k in args.assets.split(",") if k.strip()]
    for k in keys:
        if k not in ASSETS:
            raise SystemExit(f"unknown asset '{k}' (have: {', '.join(ASSETS)})")

    if args.detail:
        detail_previews(args.detail, keys=tuple(keys), review=args.review)
        return

    if args.lineup or args.row:
        clear_scene()
        objs = []
        by_key = {}
        total = 0
        for k in keys:
            jobs = VARIANTS[k] if args.variants else [(ASSETS[k][0], {}, ASSETS[k][2])]
            for idx, (name, kwargs, qs) in enumerate(jobs):
                parts = BUILDER[k](PARAM_CLASS[k](**kwargs))
                obj = finish(parts, name, qs * 2, smooth=k in SMOOTH_ASSETS)
                total += report(k, obj)
                cs2_asset.validate_asset_name(name)
                objs.append(obj)
                by_key[f"{k}:{idx}"] = obj
        print(f"[lineup] family total: {total} tris across {len(objs)} assets")
        # Preview materials, ONE PER LIVERY. The shipped albedo is light-neutral because
        # recolor tints multiply, so a raw preview shows gray machines and says nothing
        # about color. make_tinted_preview bakes each livery's tint through the
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
            tex = texture_size(k, quad_size, repack=args.repack, override=args.tex)
            parts = BUILDER[k](PARAM_CLASS[k](**kwargs))
            obj = finish(parts, name, tex, bevel=args.finish,
                         smooth=k in SMOOTH_ASSETS)
            cs2_asset.validate_asset_name(name)
            total += report(k, obj)
            export_one(k, obj, name, args.out, tex // 2, do_finish=args.finish,
                       weather=args.weather, repack=args.repack,
                       weather_step=args.weather_step)
            count += 1
    print(f"[snow_props] family total: {total} tris across {count} asset(s)")


if __name__ == "__main__":
    main()
