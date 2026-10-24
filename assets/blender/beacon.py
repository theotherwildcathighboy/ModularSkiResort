"""
beacon.py - SkiBeaconRedA: ONE shared blinking red obstruction beacon, hosted as a sub-object on
the tall gondola and tram towers (asset round 2026-10; ninth look on GD12: "emissive red
blinking lights on the tops of the towers", ruled for the GD12 / 16 / 20 tube towers, the gondola
lattice 25 / 32 / 40 and the 60 m tram tower only, never the 8-20 m fixed-grip / quad towers).
Design: docs/research/art/assets-2026-10/design/blinking-lights.md, option B (section 5).
Build report: docs/research/art/assets-2026-10/build/beacon.md. Surgery rows, the curve JSON and the
step order: docs/research/art/assets-2026-10/surgery/towers-transfer.md, addendum "Beacon
sub-objects (2026-10-11)".

    cd assets/blender
    blender --background --factory-startup --python-exit-code 1 --python beacon.py -- \
        --out ../import/props/A --export --preview ../preview/props --review
    blender ... --python beacon.py -- --measure [--json <path>]
        the anchor table, RE-MEASURED on the shipped tower FBX: ray-cast down at the tower center,
        the plate footprint, and a clearance probe of the beacon envelope. Run it again after any
        tower re-export; the surgery rows take its numbers.
    blender ... --python beacon.py -- --scale-preview ../preview/props
        the beacon on a scratch copy of GD16's FBX at the measured anchor (nothing is exported).

THE ASSET (prefab-local meters: x lateral, y up, z travel; origin = base-plate bottom center)
    plate    galvanised square base plate 0.14 x 0.14 x 0.016, open bottom (it sits on a beam)
    mast     galvanised 50 x 50 square stub mast, plate -> 0.27 (its top hides inside the housing)
    housing  dark cast funnel r 0.024 @ 0.240 -> r 0.130 @ 0.285: closes the shell underneath
    lens     red Fresnel lens, band r 0.130 @ 0.285 -> r 0.114 @ 0.365, cone cap to 0.440
             (dome 0.26 m across): the ONLY emissive surface
    cage     four galvanised guard posts (12 mm triangular bars) leaning in from the lens rim to
             r 0.118 @ 0.450, >= 2 mm clear of the lens above the rim and >= 5 mm above the shoulder
    86 tris (cap 100), one material, one UV layer "UVMap", 128 px maps, bounds x / z +-0.130,
    y 0 .. 0.451. The 0.25 m stub lifts the lens clear of the head-beam hardware.

ONE LOD, ON PURPOSE (no LOD1 / LOD2)
    Emissive exists on LOD0 only: the importer writes EmissiveProperties under `lod.level == 0`
    (AssetImportPipeline.cs:2232-2243) and 0 of 45 vanilla emissive props with a chain carry it on
    LOD1/2 (prop-emissive-lights.md 1.2). A 12-tri LOD1 would switch at ~40 m (reach_table) and the
    blink would stop there. 86 tris is below every vanilla prop band's median LOD0, so a chain saves
    nothing. The reach knob is `LodProperties.m_Bias` on the mesh prefab with an EMPTY
    m_LodMeshes (vanilla shape: "High-voltage Cable Lane Mesh", m_Bias -2, rlength 0; 20 vanilla
    RenderPrefabs ship LodProperties without LOD meshes). MeshSystem.cs:455-460 reads m_Bias
    whenever the LodMesh buffer exists, and LodProperties.GetPrefabComponents adds that buffer
    unconditionally. reach_table() prints the cull distance per bias (formula, lodFactor 652
    unverified until the SkiProbeLodA verdict).

THE EMISSIVE COLOR LIVES IN THE _Emissive MAP, NOT IN BaseColor
    The pipeline's `_Emissive` is sRGB EMITTED COLOR (cs2_asset.make_snow_prop_emissive docstring;
    cs2_bake SourceSet `emissive=` path | rgb; prop-emissive-lights.md 1.3): emission ~ texel.rgb x
    EmissiveProperties.color.linear x intensity*100 x LightState.m_Intensity. BaseColor is not in
    that product, so white texels with color (1,1,1,1) would flash WHITE. The lens texels are red
    in _Emissive (EMISSIVE_SRGB) and red glass in BaseColor (the unlit look between flashes); every
    other surface's _Emissive texels are black. Equivalent at surgery: white texels + `color`
    (0.95, 0.12, 0.08, 1). Not used: vanilla single lights keep `color` white and put the hue in the
    PNG (StreetlightSingle01, WarningLight01).

DETERMINISM
    Explicit vertex lists, hand-laid UVs (no smart_project, no unique repack, no Cycles bake: the
    maps are painted; the AO of an 86-tri 0.45 m prop at 128 px would be a two-texel gradient),
    cs2_asset.tag_parts canonical order, numpy maps from a fixed seed. Two builds hash identically
    and two fresh processes write byte-identical PNGs and settings.json (tests/test_beacon.py). The
    FBX carries the export clock, as everywhere in this pipeline.

No top-level side effects; the __main__ block runs only as a Blender --python script.
"""

import argparse
import json
import math
import os
import shutil
import sys
import tempfile

import bmesh
import bpy
import mathutils
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import cs2_asset     # noqa: E402
import cs2_validate  # noqa: E402

REPO = os.path.normpath(os.path.join(HERE, "..", ".."))

# --------------------------------------------------------------------------------------
# Names, budgets
# --------------------------------------------------------------------------------------

NAME = "SkiBeaconRedA"            # mesh asset; the Editor import makes "SkiBeaconRedA Mesh"
OBJECT_PREFAB = "SkiBeaconRed"    # hand-made StaticObjectPrefab (surgery, hand-minted .cid)
A = 128                           # atlas px (all five maps)
TRIS = 86                         # exact LOD0 triangle count (contract)
LOD0_CAP = 100                    # design spec section 5: <= 100 tris
SEG = 8                           # lens / housing segments
VALIDATE_SPEC = {"kind": "prop", "lod0_cap": LOD0_CAP, "min_texture": 128,
                 "expected_lods": (0,)}

# --------------------------------------------------------------------------------------
# Geometry (meters, Blender Z up; Blender x = prefab x lateral, Blender -y = prefab z travel)
# --------------------------------------------------------------------------------------

PLATE_HALF, PLATE_H = 0.070, 0.016
MAST_HALF, MAST_Z0, MAST_Z1 = 0.025, 0.014, 0.270     # 2 mm into the plate, top inside the housing
HOUSING = (0.024, 0.240, 0.130, 0.285)                 # r0, z0, r1, z1 (r0 inside the mast flats)
LENS = (0.130, 0.285, 0.114, 0.365, 0.440)             # r rim, z rim, r shoulder, z shoulder, z apex
CAGE = dict(n=4, az0_deg=45.0, r0=0.136, z0=0.284, r1=0.118, z1=0.450, rad=0.007)
CAGE_CLEAR_RIM = 0.002            # post inner face vs lens, z >= rim + 5 mm
CAGE_CLEAR_SHOULDER = 0.005       # post inner face vs lens, z >= shoulder
LENS_DIAMETER = 2.0 * LENS[0]     # 0.26 m

# --------------------------------------------------------------------------------------
# Atlas (texel rects x0, y0, x1, y1; y up = UV v / Image.pixels row order)
# --------------------------------------------------------------------------------------

R_LENS_BLOCK = (0, 72, 128, 128)  # lens material + emissive (band, cap and spare texels: mip-safe)
R_LENS_BAND = (0, 72, 128, 96)
R_LENS_CAP = (0, 96, 32, 128)
R_HOUSING = (0, 48, 128, 72)
R_GALV_BLOCK = (0, 0, 128, 48)    # galvanised: mast, plate, cage
R_MAST = (0, 0, 32, 48)
R_POST_SIDES = (32, 0, 80, 40)    # 12 strips 4 px wide (4 posts x 3 faces)
R_POST_CAPS = (80, 32, 96, 36)    # 4 caps 4 x 4 px
R_PLATE_SIDES = (32, 40, 96, 48)
R_PLATE_TOP = (96, 16, 128, 48)
INSET = 0.5                       # texels: UVs land on edge-texel centers (bilinear-safe at mip 0)

# --------------------------------------------------------------------------------------
# Materials (BaseColor / _Emissive sRGB as written; MaskMap R metal, A smoothness, linear)
# --------------------------------------------------------------------------------------

GALV_SRGB = (0.50, 0.53, 0.565)   # Metal032 color mean (0.491, 0.535, 0.578): the towers' galv set
GALV_METAL, GALV_SMOOTH = 1.0, 0.57   # Metal032 metalness 1.0, roughness 0.41
HOUSING_SRGB = (0.165, 0.170, 0.175)
HOUSING_METAL, HOUSING_SMOOTH = 0.0, 0.40
LENS_SRGB = (0.60, 0.065, 0.05)   # red glass, unlit
LENS_METAL, LENS_SMOOTH = 0.0, 0.90
EMISSIVE_SRGB = (0.95, 0.12, 0.08)    # design spec section 5 (aviation red)
RIB_PITCH_PX = 3.0                # Fresnel rings: band rows / cap radius
SEED = 20261011

# --------------------------------------------------------------------------------------
# Surgery values (recorded in the manifest; the JSON lives in surgery/towers-transfer.md)
# --------------------------------------------------------------------------------------

EMISSIVE_PROPERTIES = dict(purpose=34, color=(1, 1, 1, 1), colorOff=(0, 0, 0, 1), intensity=4.0,
                           luminance=0, responseTime=0.05, animationIndex=0, materialId=0)
CURVE = dict(m_Duration=2.0,      # 120 frames; 0.30 s on per 2.0 s = 30 flashes / min
             keys=((0.0, 0.0, 0.0, 0.0), (0.4995, 0.0, 0.0, 1000.0), (0.5, 1.0, 1000.0, 0.0),
                   (0.6495, 1.0, 0.0, -1000.0), (0.65, 0.0, -1000.0, 0.0), (1.0, 0.0, 0.0, 1.0)))
LOD_BIAS = -1                     # LodProperties.m_Bias on the mesh prefab (empty m_LodMeshes)
LOD_FACTOR = 652.0

# The seven hosts. Design anchors from blinking-lights.md section 5; the
# surgery uses measure_anchors() on the CURRENT FBX instead.
TOWERS = (
    # gameplay prefab,      mesh asset,             folder (assets/import), design y, host
    ("SkiLiftTowerGD12",    "SkiLiftTowerGD12O",    "towers/O",     11.976, "top gantry beam"),
    ("SkiLiftTowerGD16",    "SkiLiftTowerGD16O",    "towers/O",     15.976, "top gantry beam"),
    ("SkiLiftTowerGD20",    "SkiLiftTowerGD20O",    "towers/O",     19.976, "top gantry beam"),
    ("SkiGondolaLattice25", "SkiGondolaLattice25O", "towers/O",     25.000, "head beam top"),
    ("SkiGondolaLattice32", "SkiGondolaLattice32O", "towers/O",     32.000, "head beam top"),
    ("SkiGondolaLattice40", "SkiGondolaLattice40O", "towers/O",     40.000, "head beam top"),
    ("SkiTramTower60",      "SkiTramTower60A",      "tramtowers/A", 59.960, "gantryx_1"),
)
FOOTPRINT_TOL = 0.005             # plate corners must hit within 5 mm of the center hit
ENVELOPE = (0.135, 0.455)         # clearance probe: half-width (x, z) and height above the anchor


# --------------------------------------------------------------------------------------
# Scene + part helpers
# --------------------------------------------------------------------------------------

def clear_scene():
    """Delete every object and orphaned mesh / material / image (headless builds only)."""
    if bpy.context.object is not None and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.images, bpy.data.cameras,
                 bpy.data.lights):
        for item in list(coll):
            if item.users == 0:
                coll.remove(item)


def _uv(rect, fu, fv):
    """Texel coordinates of (fu, fv) in [0, 1]^2 inside `rect`, inset INSET texels."""
    x0, y0, x1, y1 = rect
    return (x0 + INSET + fu * (x1 - x0 - 2.0 * INSET), y0 + INSET + fv * (y1 - y0 - 2.0 * INSET))


def _ring(r, z, n, phase=0.0):
    return [(r * math.cos(phase + 2.0 * math.pi * i / n),
             r * math.sin(phase + 2.0 * math.pi * i / n), z) for i in range(n)]


def _band(ring_a, ring_b, rect, base=0):
    """Quads between two rings (a below b), outward-facing; u = azimuth, v = a -> b."""
    n = len(ring_a)
    faces, uvs = [], []
    for i in range(n):
        j = (i + 1) % n
        faces.append((base + i, base + j, base + n + j, base + n + i))
        uvs.append([_uv(rect, i / n, 0.0), _uv(rect, (i + 1) / n, 0.0),
                    _uv(rect, (i + 1) / n, 1.0), _uv(rect, i / n, 1.0)])
    return faces, uvs


def _part(name, verts, faces, uvs):
    """Mesh object from explicit verts / faces and per-face texel UVs (layer "UVMap")."""
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in verts], [], [tuple(f) for f in faces])
    me.update()
    layer = me.uv_layers.new(name="UVMap")
    for poly, fuv in zip(me.polygons, uvs):
        if len(fuv) != poly.loop_total:
            raise RuntimeError(f"{name}: face {poly.index} has {poly.loop_total} loops, "
                               f"{len(fuv)} UVs")
        for k, li in enumerate(poly.loop_indices):
            layer.data[li].uv = (fuv[k][0] / A, fuv[k][1] / A)
    obj = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(obj)
    return obj


# --------------------------------------------------------------------------------------
# Parts
# --------------------------------------------------------------------------------------

def _plate():
    r = PLATE_HALF * math.sqrt(2.0)
    a = _ring(r, 0.0, 4, math.pi / 4.0)
    b = _ring(r, PLATE_H, 4, math.pi / 4.0)
    faces, uvs = _band(a, b, R_PLATE_SIDES)
    faces.append((4, 5, 6, 7))                       # top, CCW from above; no bottom face
    uvs.append([_uv(R_PLATE_TOP, (x + PLATE_HALF) / (2 * PLATE_HALF),
                    (y + PLATE_HALF) / (2 * PLATE_HALF)) for x, y, _z in b])
    return _part("plate", a + b, faces, uvs)


def _mast():
    r = MAST_HALF * math.sqrt(2.0)
    a = _ring(r, MAST_Z0, 4, math.pi / 4.0)
    b = _ring(r, MAST_Z1, 4, math.pi / 4.0)
    faces, uvs = _band(a, b, R_MAST)                 # no caps: plate below, housing above
    return _part("mast", a + b, faces, uvs)


def _housing():
    r0, z0, r1, z1 = HOUSING
    a, b = _ring(r0, z0, SEG), _ring(r1, z1, SEG)
    faces, uvs = _band(a, b, R_HOUSING)
    return _part("housing", a + b, faces, uvs)


def _lens():
    r_rim, z_rim, r_sh, z_sh, z_apex = LENS
    a, b = _ring(r_rim, z_rim, SEG), _ring(r_sh, z_sh, SEG)
    faces, uvs = _band(a, b, R_LENS_BAND)
    apex = 2 * SEG
    cx, cy = (R_LENS_CAP[0] + R_LENS_CAP[2]) / 2.0, (R_LENS_CAP[1] + R_LENS_CAP[3]) / 2.0
    rad_px = (R_LENS_CAP[2] - R_LENS_CAP[0]) / 2.0 - INSET
    for i in range(SEG):
        j = (i + 1) % SEG
        faces.append((SEG + i, SEG + j, apex))       # CCW from above / outside
        uvs.append([(cx + rad_px * b[i][0] / r_sh, cy + rad_px * b[i][1] / r_sh),
                    (cx + rad_px * b[j][0] / r_sh, cy + rad_px * b[j][1] / r_sh),
                    (cx, cy)])
    return _part("lens", a + b + [(0.0, 0.0, z_apex)], faces, uvs)


def _post_frame(k):
    """(P0, P1, d, e_r, e_t) of cage post k: axis ends, axis, outward radial and tangent units."""
    phi = math.radians(CAGE["az0_deg"] + 360.0 * k / CAGE["n"])
    c, s = math.cos(phi), math.sin(phi)
    p0 = mathutils.Vector((CAGE["r0"] * c, CAGE["r0"] * s, CAGE["z0"]))
    p1 = mathutils.Vector((CAGE["r1"] * c, CAGE["r1"] * s, CAGE["z1"]))
    d = (p1 - p0).normalized()
    radial = mathutils.Vector((c, s, 0.0))
    e_r = (radial - d * d.dot(radial)).normalized()
    e_t = d.cross(e_r)
    return p0, p1, d, e_r, e_t


def _post(k):
    p0, p1, _d, e_r, e_t = _post_frame(k)
    rad = CAGE["rad"]
    offs = [rad * (math.cos(t) * e_r + math.sin(t) * e_t)
            for t in (0.0, 2.0 * math.pi / 3.0, 4.0 * math.pi / 3.0)]
    verts = [p0 + o for o in offs] + [p1 + o for o in offs]
    faces, uvs = [], []
    for m in range(3):
        mm = (m + 1) % 3
        faces.append((m, mm, 3 + mm, 3 + m))
        s = 3 * k + m
        rect = (R_POST_SIDES[0] + 4 * s, R_POST_SIDES[1], R_POST_SIDES[0] + 4 * s + 4, R_POST_SIDES[3])
        uvs.append([_uv(rect, 0, 0), _uv(rect, 1, 0), _uv(rect, 1, 1), _uv(rect, 0, 1)])
    faces.append((3, 4, 5))                          # top cap, CCW seen from +d
    rect = (R_POST_CAPS[0] + 4 * k, R_POST_CAPS[1], R_POST_CAPS[0] + 4 * k + 4, R_POST_CAPS[3])
    uvs.append([_uv(rect, 0.5 + 0.5 * math.cos(t), 0.5 + 0.5 * math.sin(t))
                for t in (0.0, 2.0 * math.pi / 3.0, 4.0 * math.pi / 3.0)])
    return _part(f"cage_{k}", [tuple(v) for v in verts], faces, uvs)


def build_beacon(name=NAME):
    """Build, tag, join, weld, shade. Returns the finished LOD0 object (meters, Z up)."""
    if bpy.context.object is not None and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    parts = [_plate(), _mast(), _housing(), _lens()] + [_post(k) for k in range(CAGE["n"])]
    table = cs2_asset.tag_parts([(p, None) for p in parts])
    bpy.ops.object.select_all(action="DESELECT")
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    obj = bpy.context.active_object
    obj.name = name
    obj.data.name = name
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=0.0005)   # welds housing top = lens rim
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    cs2_asset.store_part_table(obj, table)
    cs2_asset.shade_by_angle(obj, 66.0)
    return obj


# --------------------------------------------------------------------------------------
# Contracts (raise on failure; return the measured numbers)
# --------------------------------------------------------------------------------------

def _lens_radius(z):
    """Lens surface radius at height z along a vertex azimuth (0, 45, 90 ... deg), else None."""
    r_rim, z_rim, r_sh, z_sh, z_apex = LENS
    if z < z_rim or z > z_apex:
        return None
    if z <= z_sh:
        return r_rim + (r_sh - r_rim) * (z - z_rim) / (z_sh - z_rim)
    return r_sh * (z_apex - z) / (z_apex - z_sh)


def part_faces(obj, prefix):
    """Polygon indices whose part name starts with `prefix`."""
    table = json.loads(obj.data[cs2_asset.PART_TABLE_PROP])
    vals = np.zeros(len(obj.data.polygons), dtype=np.int64)
    obj.data.attributes["part"].data.foreach_get("value", vals)
    return [i for i, v in enumerate(vals) if table[v - 1].startswith(prefix)]


def contracts(obj):
    """Geometry / UV contracts of the asset. Raises RuntimeError listing every failure."""
    fails = []
    me = obj.data
    co = np.array([v.co[:] for v in me.vertices])
    lo, hi = co.min(axis=0), co.max(axis=0)
    tris = cs2_asset.tri_count(obj)
    if tris != TRIS or tris > LOD0_CAP:
        fails.append(f"tris {tris} (expected {TRIS}, cap {LOD0_CAP})")
    if abs(lo[2]) > 1e-6:
        fails.append(f"min z {lo[2]:.6f} != 0 (prop base pivot)")
    if np.abs(co[:, :2]).max() > LENS[0] + 1e-6:
        fails.append(f"lateral extent {np.abs(co[:, :2]).max():.4f} > lens rim {LENS[0]}")
    ring = co[np.isclose(co[:, 2], LENS[1], atol=1e-6)]
    rim = 2.0 * np.hypot(ring[:, 0], ring[:, 1]).max() if len(ring) else 0.0
    if abs(rim - LENS_DIAMETER) > 1e-3:
        fails.append(f"lens rim diameter {rim:.4f} != {LENS_DIAMETER}")
    if len(me.uv_layers) != 1:
        fails.append(f"{len(me.uv_layers)} UV layers")
    # cage clearance along each post's inner face (offset -rad/2 along e_r)
    worst_rim, worst_sh = 1.0, 1.0
    for k in range(CAGE["n"]):
        p0, p1, _d, e_r, _e_t = _post_frame(k)
        for t in np.linspace(0.0, 1.0, 201):
            p = p0.lerp(p1, float(t)) - e_r * (CAGE["rad"] * 0.5)
            rl = _lens_radius(p.z)
            if rl is None:
                continue
            gap = math.hypot(p.x, p.y) - rl
            if p.z >= LENS[1] + 0.005:
                worst_rim = min(worst_rim, gap)
            if p.z >= LENS[3]:
                worst_sh = min(worst_sh, gap)
    if worst_rim < CAGE_CLEAR_RIM:
        fails.append(f"cage clearance above the rim {worst_rim * 1000:.1f} mm < "
                     f"{CAGE_CLEAR_RIM * 1000:.0f}")
    if worst_sh < CAGE_CLEAR_SHOULDER:
        fails.append(f"cage clearance above the shoulder {worst_sh * 1000:.1f} mm < "
                     f"{CAGE_CLEAR_SHOULDER * 1000:.0f}")
    # every face points away from the beacon axis (posts: from their own axis; tops: up)
    table = json.loads(me[cs2_asset.PART_TABLE_PROP])
    part = np.zeros(len(me.polygons), dtype=np.int64)
    me.attributes["part"].data.foreach_get("value", part)
    inward = 0
    for p in me.polygons:
        name = table[part[p.index] - 1]
        c = mathutils.Vector(p.center)
        if name.startswith("cage_"):
            k = int(name.split("_")[1])
            p0, p1, d, _er, _et = _post_frame(k)
            ref = c - (p0 + d * d.dot(c - p0))
            if (c - p0).dot(d) > (p1 - p0).length - 1e-4:       # the top cap
                ref = d
        elif name == "plate" and p.normal.z > 0.9:
            ref = mathutils.Vector((0.0, 0.0, 1.0))
        else:
            ref = mathutils.Vector((c.x, c.y, 0.0))
            if ref.length < 1e-6:
                ref = mathutils.Vector((0.0, 0.0, 1.0))
        if p.normal.dot(ref) <= 0.0:
            inward += 1
    if inward:
        fails.append(f"{inward} faces point inward")
    # UV range, zero area, mirror, lens-block membership
    layer = me.uv_layers[0]
    lens_faces = set(part_faces(obj, "lens"))
    bad_block = zero = mirrored = out_range = 0
    for p in me.polygons:
        pts = np.array([layer.data[li].uv[:] for li in p.loop_indices]) * A
        x, y = pts[:, 0], pts[:, 1]
        sa = 0.5 * float(np.dot(x, np.roll(y, -1)) - np.dot(y, np.roll(x, -1)))
        if abs(sa) < 1e-9:
            zero += 1
        elif sa < 0.0:
            mirrored += 1
        inside = bool(((x >= R_LENS_BLOCK[0]) & (x <= R_LENS_BLOCK[2]) &
                       (y >= R_LENS_BLOCK[1]) & (y <= R_LENS_BLOCK[3])).all())
        if (p.index in lens_faces) != inside:
            bad_block += 1
        if (pts < -1e-6).any() or (pts > A + 1e-6).any():
            out_range += 1
    if zero or mirrored or out_range:
        fails.append(f"UV faces: {zero} zero-area, {mirrored} mirrored, {out_range} out of [0, 1]")
    if bad_block:
        fails.append(f"{bad_block} faces on the wrong side of the lens block "
                     f"(lens faces must map inside {R_LENS_BLOCK}, others outside)")
    if fails:
        raise RuntimeError(f"{obj.name}: contracts FAILED: " + "; ".join(fails))
    out = {"tris": tris, "verts": len(me.vertices), "faces": len(me.polygons),
           "bounds_blender_m": [[round(float(v), 4) for v in lo], [round(float(v), 4) for v in hi]],
           "lens_rim_diameter_m": round(float(rim), 4),
           "cage_clearance_mm": {"above_rim": round(worst_rim * 1000.0, 1),
                                 "above_shoulder": round(worst_sh * 1000.0, 1)},
           "lens_faces": len(lens_faces)}
    print(f"[beacon] contracts hold: {tris} tris, bounds {out['bounds_blender_m']}, rim "
          f"{rim:.3f} m, cage clearance {out['cage_clearance_mm']} mm")
    return out


def bounds_prefab(obj):
    """Bounds in prefab-local meters (x lateral, y up, z travel) = the m_Bounds the import writes."""
    co = np.array([v.co[:] for v in obj.data.vertices])
    x, y, z = co[:, 0], co[:, 2], -co[:, 1]
    return {"min": [round(float(x.min()), 4), round(float(y.min()), 4), round(float(z.min()), 4)],
            "max": [round(float(x.max()), 4), round(float(y.max()), 4), round(float(z.max()), 4)]}


def reach_table(obj):
    """Cull distance (single LOD) per LodProperties.m_Bias, and the LOD1 switch a 12-tri chain
    would have: cs2_preview.lod_switch_table with the thin-mesh area term (MeshSystem.cs:496-503)."""
    import cs2_preview
    co = np.array([v.co[:] for v in obj.data.vertices])
    size = (co.max(axis=0) - co.min(axis=0)).tolist()
    area = float(sum(p.area for p in obj.data.polygons))
    tris = cs2_asset.tri_count(obj)
    t = cs2_preview.lod_switch_table(size, [tris, 12], lod_factor=LOD_FACTOR, area=area)
    cull0 = t["cull"]
    out = {"bbox_size_m": [round(v, 4) for v in size], "surface_area_m2": round(area, 4),
           "area_bias": round(t["bias"], 3), "lod_factor": LOD_FACTOR,
           "cull_m_by_bias": {str(b): round(cull0 * 2.0 ** (-b), 1) for b in (0, -1, -2)},
           "lod1_switch_m_if_12tri_chain": round(t["switch"][1], 1),
           "surgery_bias": LOD_BIAS, "cull_m_at_surgery_bias": round(cull0 * 2.0 ** (-LOD_BIAS), 1)}
    print(f"[beacon] reach: cull {out['cull_m_by_bias']} m by m_Bias (area bias {t['bias']:.3f}); "
          f"a 12-tri LOD1 would switch at {out['lod1_switch_m_if_12tri_chain']} m")
    return out


# --------------------------------------------------------------------------------------
# Maps
# --------------------------------------------------------------------------------------

def _sl(rect):
    x0, y0, x1, y1 = rect
    return slice(y0, y1), slice(x0, x1)


def paint_maps():
    """{suffix: (rgba float32 (A, A, 4) bottom-up rows, is_data)} for the five maps."""
    rng = np.random.default_rng(SEED)
    noise = rng.standard_normal((A, A)).astype(np.float32)
    noise = (noise + np.roll(noise, 1, 0) + np.roll(noise, -1, 0)
             + np.roll(noise, 1, 1) + np.roll(noise, -1, 1)) / 5.0
    base = np.zeros((A, A, 4), np.float32)
    nrm = np.zeros((A, A, 4), np.float32)
    msk = np.zeros((A, A, 4), np.float32)
    ctl = np.zeros((A, A, 4), np.float32)            # no recolor slot anywhere, alpha 0
    emi = np.zeros((A, A, 4), np.float32)
    base[..., 3] = 1.0
    emi[..., 3] = 1.0
    nrm[..., 0] = 0.5
    nrm[..., 1] = 0.5
    nrm[..., 2] = 1.0
    nrm[..., 3] = 1.0

    def fill(rect, srgb, metal, smooth, mottle):
        ys, xs = _sl(rect)
        for c in range(3):
            base[ys, xs, c] = srgb[c] + mottle * noise[ys, xs]
        msk[ys, xs, 0] = metal
        msk[ys, xs, 3] = smooth + 0.5 * mottle * noise[ys, xs]

    fill(R_GALV_BLOCK, GALV_SRGB, GALV_METAL, GALV_SMOOTH, 0.02)
    fill(R_HOUSING, HOUSING_SRGB, HOUSING_METAL, HOUSING_SMOOTH, 0.01)
    fill(R_LENS_BLOCK, LENS_SRGB, LENS_METAL, LENS_SMOOTH, 0.0)
    ys, xs = _sl(R_LENS_BLOCK)
    for c in range(3):
        emi[ys, xs, c] = EMISSIVE_SRGB[c]

    # Fresnel rings on the band: horizontal ridges (v), brightness + normal tilt + glow ripple
    ys, xs = _sl(R_LENS_BAND)
    rows = np.arange(ys.start, ys.stop, dtype=np.float32)
    ph = 2.0 * math.pi * (rows - ys.start + 0.5) / RIB_PITCH_PX
    rib, tilt = np.cos(ph), 0.30 * np.sin(ph)
    inv = 1.0 / np.sqrt(1.0 + tilt * tilt)
    for c in range(3):
        base[ys, xs, c] = LENS_SRGB[c] * (1.0 + 0.12 * rib)[:, None]
        emi[ys, xs, c] = EMISSIVE_SRGB[c] * (0.92 + 0.08 * rib)[:, None]
    nrm[ys, xs, 0] = 0.5
    nrm[ys, xs, 1] = (0.5 + 0.5 * tilt * inv)[:, None]
    nrm[ys, xs, 2] = (0.5 + 0.5 * inv)[:, None]

    # concentric rings on the cap (planar top projection: radial tilt in UV = radial in world)
    ys, xs = _sl(R_LENS_CAP)
    cx, cy = (R_LENS_CAP[0] + R_LENS_CAP[2]) / 2.0, (R_LENS_CAP[1] + R_LENS_CAP[3]) / 2.0
    gy, gx = np.mgrid[ys.start:ys.stop, xs.start:xs.stop].astype(np.float32)
    dx, dy = gx + 0.5 - cx, gy + 0.5 - cy
    rho = np.sqrt(dx * dx + dy * dy)
    ph = 2.0 * math.pi * rho / RIB_PITCH_PX
    rib, tilt = np.cos(ph), 0.30 * np.sin(ph)
    ux, uy = dx / np.maximum(rho, 1e-6), dy / np.maximum(rho, 1e-6)
    inv = 1.0 / np.sqrt(1.0 + tilt * tilt)
    for c in range(3):
        base[ys, xs, c] = LENS_SRGB[c] * (1.0 + 0.10 * rib)
        emi[ys, xs, c] = EMISSIVE_SRGB[c] * (0.92 + 0.08 * rib)
    nrm[ys, xs, 0] = 0.5 + 0.5 * tilt * ux * inv
    nrm[ys, xs, 1] = 0.5 + 0.5 * tilt * uy * inv
    nrm[ys, xs, 2] = 0.5 + 0.5 * inv

    for arr in (base, nrm, msk, emi):
        np.clip(arr, 0.0, 1.0, out=arr)
    msk[..., 1] = 0.0
    msk[..., 2] = 0.0
    return {"BaseColor": (base, False), "Normal": (nrm, True), "MaskMap": (msk, True),
            "ControlMask": (ctl, True), "Emissive": (emi, False)}


def write_maps(asset_dir, maps=None):
    maps = maps or paint_maps()
    out = []
    for suffix in ("BaseColor", "Normal", "MaskMap", "ControlMask", "Emissive"):
        arr, is_data = maps[suffix]
        path = os.path.join(asset_dir, f"{NAME}_{suffix}.png")
        cs2_asset._write_png_raw(path, f"{NAME}_{suffix}", arr, A, is_data)
        out.append(path)
    return out


# --------------------------------------------------------------------------------------
# Anchors on the shipped towers
# --------------------------------------------------------------------------------------

def _tower_fbx(row):
    return os.path.join(REPO, "assets", "import", row[2], row[1], row[1] + ".fbx")


def _raw_metres(path):
    """Vertices (prefab-local meters: x lateral, y up, z travel) and polygons of an exported FBX.
    The FBX holds raw cm Y-up vertices (cs2_asset.export_fbx); mesh data is read as stored."""
    objs = cs2_validate.import_fbx_temp(path)
    try:
        meshes = [o for o in objs if o.type == "MESH"]
        verts, polys, off = [], [], 0
        for o in meshes:
            me = o.data
            co = np.empty(len(me.vertices) * 3, dtype=np.float64)
            me.vertices.foreach_get("co", co)
            co = co.reshape(-1, 3) / cs2_asset.CS2_UNIT_SCALE
            verts.append(co)
            polys.extend(tuple(off + i for i in p.vertices) for p in me.polygons)
            off += len(co)
        return np.concatenate(verts, axis=0), polys
    finally:
        cs2_validate.remove_objects(objs)


def measure_one(row):
    """Ray-cast the shipped FBX of one host tower. Returns the anchor row (prefab-local meters)."""
    from mathutils.bvhtree import BVHTree
    V = mathutils.Vector
    path = _tower_fbx(row)
    if not os.path.isfile(path):
        return {"prefab": row[0], "mesh": row[1], "error": f"missing {path}", "ok": False}
    co, polys = _raw_metres(path)
    tree = BVHTree.FromPolygons([V(c) for c in co], polys)
    top = float(co[:, 1].max())

    def down(x, z):
        hit = tree.ray_cast(V((x, top + 1.0, z)), V((0.0, -1.0, 0.0)), top + 50.0)
        return (None, None) if hit[0] is None else (float(hit[0].y), tuple(hit[1]))

    y0, n0 = down(0.0, 0.0)
    if y0 is None:
        return {"prefab": row[0], "mesh": row[1], "error": "no hit at the tower centre", "ok": False}
    h = PLATE_HALF
    foot = [down(dx, dz)[0] for dx, dz in ((h, h), (-h, h), (-h, -h), (h, -h),
                                            (h, 0.0), (-h, 0.0), (0.0, h), (0.0, -h))]
    dy = [abs(v - y0) for v in foot if v is not None]
    misses = sum(1 for v in foot if v is None)
    flat = max(dy) if dy else None
    # clearance of the beacon envelope above the anchor
    ex, eh = ENVELOPE
    inside = int(np.count_nonzero((np.abs(co[:, 0]) <= ex) & (np.abs(co[:, 2]) <= ex)
                                  & (co[:, 1] > y0 + 0.005) & (co[:, 1] < y0 + eh)))
    up_hits = 0
    for dx, dz in ((0.0, 0.0), (0.1, 0.0), (-0.1, 0.0), (0.0, 0.1), (0.0, -0.1)):
        hit = tree.ray_cast(V((dx, y0 + 0.002, dz)), V((0.0, 1.0, 0.0)), eh)
        up_hits += hit[0] is not None
    side_hits = 0
    for hh in (0.03, 0.15, 0.30, 0.44):
        for o, d in (((-ex, y0 + hh, 0.0), (1.0, 0.0, 0.0)), ((0.0, y0 + hh, -ex), (0.0, 0.0, 1.0))):
            hit = tree.ray_cast(V(o), V(d), 2.0 * ex)
            side_hits += hit[0] is not None
    # nearest tower geometry above the anchor inside a 1 m column (what stands near the lamp)
    col = co[(np.abs(co[:, 0]) <= 0.5) & (np.abs(co[:, 2]) <= 0.5) & (co[:, 1] > y0 + 0.005)]
    above = round(float(col[:, 1].min() - y0), 3) if len(col) else None
    ok = (n0[1] > 0.99 and misses == 0 and flat is not None and flat <= FOOTPRINT_TOL
          and inside == 0 and up_hits == 0 and side_hits == 0)
    out = {"prefab": row[0], "mesh": row[1], "host": row[4],
           "anchor": [0.0, round(y0, 3), 0.0], "design_y": row[3],
           "delta_vs_design_m": round(y0 - row[3], 3), "hit_normal_y": round(n0[1], 4),
           "bounds_top_m": round(top, 3),
           "footprint_flat_m": None if flat is None else round(flat, 4),
           "footprint_misses": misses, "envelope_verts": inside, "up_hits": up_hits,
           "side_hits": side_hits, "nearest_geometry_above_m": above,
           "lamp_top_m": round(y0 + CAGE["z1"], 3), "fbx_mtime": os.path.getmtime(path),
           "ok": bool(ok)}
    print(f"[beacon] anchor {row[0]:20s} y {y0:8.3f} (design {row[3]:7.3f}, d {y0 - row[3]:+.3f}) "
          f"top {top:7.3f} flat {out['footprint_flat_m']} miss {misses} env {inside} up {up_hits} "
          f"side {side_hits} above {above} -> {'OK' if ok else 'CHECK'}")
    return out


def measure_anchors(rows=TOWERS):
    return [measure_one(r) for r in rows]


# --------------------------------------------------------------------------------------
# Export
# --------------------------------------------------------------------------------------

def _clean_asset_dir(asset_dir):
    for f in os.listdir(asset_dir):
        p = os.path.join(asset_dir, f)
        if os.path.isfile(p) and (f.startswith(NAME) or f == "settings.json"):
            os.remove(p)


def export(out_root, preview_dir=None, review=False, measure=True, args=None):
    """Build, check, write the asset folder (+ previews / review pack). Returns (asset_dir, obj,
    manifest data)."""
    asset_dir = os.path.join(os.path.abspath(out_root), NAME)
    os.makedirs(asset_dir, exist_ok=True)
    _clean_asset_dir(asset_dir)
    obj = build_beacon()
    held = contracts(obj)
    write_maps(asset_dir)
    cs2_asset.write_settings(asset_dir, NAME)        # {} = LOD0 only (no chain, by design)
    cs2_asset.export_fbx(obj, os.path.join(asset_dir, f"{NAME}.fbx"))
    findings, summary = cs2_validate.validate_asset_folder_ex(asset_dir, NAME, VALIDATE_SPEC)
    errors = [f for f in findings if f[0] == "ERROR"]
    warns = [f for f in findings if f[0] == "WARN"]
    mesh_findings = cs2_validate.validate_mesh(obj, kind="prop")
    errors += [f for f in mesh_findings if f[0] == "ERROR"]
    warns += [f for f in mesh_findings if f[0] == "WARN"]
    if errors:
        raise RuntimeError(f"{NAME}: validation errors: {errors}")
    reach = reach_table(obj)
    anchors = measure_anchors() if measure else None
    data = {
        "generator": "assets/blender/beacon.py",
        "args": list(args or []),
        "kind": "prop (hosted sub-object; no engine base rule)",
        "object_prefab": OBJECT_PREFAB,
        "mesh_prefab": f"{NAME} Mesh",
        "tris": {"LOD0": held["tris"]}, "lod0_cap": LOD0_CAP,
        "lods": "LOD0 only: emissive is LOD0-only engine-side; reach via LodProperties.m_Bias",
        "texture_px": A, "maps": ["BaseColor", "Normal", "MaskMap", "ControlMask", "Emissive"],
        "vram_mb": {"uncompressed_with_mips": round(5 * A * A * 4 * 4 / 3 / 2 ** 20, 3),
                    "bc_1byte_per_texel_with_mips": round(5 * A * A * 4 / 3 / 2 ** 20, 3)},
        "bounds_m": bounds_prefab(obj), "contracts": held, "reach": reach,
        "validation": {"errors": 0, "warnings": len(warns), "tris": summary.get("tris"),
                       "warn_list": [f"{c}: {m}" for _l, c, m in warns]},
        "atlas_px": {"lens_block": R_LENS_BLOCK, "lens_band": R_LENS_BAND, "lens_cap": R_LENS_CAP,
                     "housing": R_HOUSING, "galv_block": R_GALV_BLOCK},
        "materials_srgb": {"galv": GALV_SRGB, "housing": HOUSING_SRGB, "lens": LENS_SRGB,
                           "emissive_lens": EMISSIVE_SRGB},
        "emissive_properties_surgery": EMISSIVE_PROPERTIES,
        "animation_curve_surgery": {"m_Duration": CURVE["m_Duration"],
                                    "keys_t_v_in_out": CURVE["keys"],
                                    "on_s": round((0.65 - 0.5) * CURVE["m_Duration"], 3),
                                    "flashes_per_min": round(60.0 / CURVE["m_Duration"], 1)},
        "lod_properties_surgery": {"m_Bias": LOD_BIAS, "m_ShadowBias": 0, "m_LodMeshes": []},
        "hosting": {"rows": anchors, "rotation_xyzw": [0, 0, 0, 1], "m_ParentMesh": 0,
                    "m_GroupIndex": 0, "m_Probability": 100,
                    "note": "one ObjectSubObjects row per host tower; no Night condition "
                            "(DECISIONS 83/84); anchor = beacon origin = plate bottom"},
    }
    if preview_dir:
        import cs2_preview
        views = (("three_quarter", (1.0, -1.2, 0.45)), ("top", (0.35, -0.45, 1.0)),
                 ("side", (1.0, 0.0, 0.10)))
        data["previews"] = [os.path.relpath(p, REPO).replace("\\", "/") for p in
                            cs2_preview.preview(obj, asset_dir, NAME, os.path.abspath(preview_dir),
                                                modes=("pbr", "dusk", "normals", "density"),
                                                views=views, resolution=(900, 900))]
    if review:
        import cs2_review
        data["review_pack"] = os.path.relpath(
            cs2_review.write_pack(NAME, asset_dir, {0: obj},
                                  meta={"generator": "beacon.py", "args": list(args or []),
                                        "kind": "prop", "caps": {0: LOD0_CAP}, "letter": "A"}),
            REPO).replace("\\", "/")
    cs2_validate.write_manifest(asset_dir, NAME, data)
    print(f"[beacon] exported {asset_dir}: {held['tris']} tris, {A} px x 5 maps, "
          f"validation 0 errors / {len(warns)} warnings")
    return asset_dir, obj, data


# --------------------------------------------------------------------------------------
# Scale preview: the beacon on a scratch copy of GD16 (nothing exported)
# --------------------------------------------------------------------------------------

def _import_tower_metres(fbx):
    """Import an exported tower FBX and bake it to meters, Z up (the inverse of export_fbx). Not
    cs2_preview.import_generation: its y-up test misreads a below-grade pier (cs2_review.py:43)."""
    objs = cs2_validate.import_fbx_temp(fbx)
    meshes = [o for o in objs if o.type == "MESH"]
    for o in objs:
        if o.type != "MESH":
            bpy.data.objects.remove(o, do_unlink=True)
    t = meshes[0]
    t.data.transform(cs2_validate._import_matrix())
    t.matrix_world = mathutils.Matrix.Identity(4)
    t.data.update()
    return t


def scale_preview(out_dir, tower="SkiLiftTowerGD16", samples=None):
    """Render the beacon on a scratch copy of the tower at its measured anchor. Returns
    (paths, anchor row)."""
    import cs2_preview as P
    row = next(r for r in TOWERS if r[0] == tower)
    anchor = measure_one(row)
    if "error" in anchor:
        raise RuntimeError(anchor["error"])
    y = anchor["anchor"][1]
    src = os.path.join(REPO, "assets", "import", row[2], row[1])
    scratch = tempfile.mkdtemp(prefix="beacon_scale_")
    tdir = os.path.join(scratch, row[1])
    bdir = os.path.join(scratch, NAME)
    os.makedirs(tdir)
    os.makedirs(bdir)
    for f in os.listdir(src):
        if f.startswith(row[1]) and "_LOD" not in f and f.endswith((".fbx", ".png")):
            shutil.copy2(os.path.join(src, f), tdir)
    write_maps(bdir)
    out_dir = os.path.abspath(out_dir)
    os.makedirs(out_dir, exist_ok=True)
    written = []
    beacon = build_beacon(name="BeaconScale")
    tower_obj = _import_tower_metres(os.path.join(tdir, row[1] + ".fbx"))
    beacon.location = (0.0, 0.0, y)                 # prefab (0, y, 0) -> Blender (0, 0, y)
    bpy.context.view_layer.update()
    rig = P._Rig(beacon, samples)
    try:
        rig.track(tower_obj)
        tower_obj.hide_render = False
        cam = P._make_camera(rig)
        P._assign(tower_obj, P._pbr_material(rig, tdir, row[1], None))
        bmat = P._pbr_material(rig, bdir, NAME, None, mat_name="BeaconScalePBR")
        P._assign(beacon, bmat)
        bsdf = next(n for n in bmat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
        sc = rig.scene
        head = (0.0, 0.0, y + 0.22)
        stem = f"{NAME}_on{row[1]}"

        def shot(label, world, lit, aim=None, frame=None, res=(1000, 800)):
            _w, sun = P._make_world(rig, **world)
            g = P._make_ground(rig)
            bsdf.inputs["Emission Strength"].default_value = P.EMISSIVE_STRENGTH if lit else 0.0
            sc.render.resolution_x, sc.render.resolution_y = res
            if frame is not None:
                P._frame(cam, tower_obj, mathutils.Vector(frame), res)
            else:
                P._aim(cam, head, aim[0], aim[1])
            path = os.path.join(out_dir, f"{stem}_{label}.png")
            written.append(P._render(rig, path, engine="EEVEE"))
            print(f"[beacon] scale preview -> {path}")
            for o in (g, sun):
                rig.objects.remove(o)
                bpy.data.objects.remove(o, do_unlink=True)
            sc.world = None
            return path

        close = ((1.0, -1.35, 0.55), 3.2)
        shot("tower_noon", P.NOON, True, frame=(1.0, -1.25, 0.30), res=(900, 1200))
        a = shot("head_noon_off", P.NOON, False, aim=close)
        b = shot("head_noon_on", P.NOON, True, aim=close)
        c = shot("head_dusk_on", P.DUSK, True, aim=close)
        shot("tower_dusk_on", P.DUSK, True, frame=(1.0, -1.25, 0.30), res=(900, 1200))
        shot("far150m_dusk_on", P.DUSK, True, aim=((1.0, -1.25, 0.20), 150.0), res=(1000, 800))
        sheet = os.path.join(out_dir, f"{stem}_head_sheet.png")
        P._write_rgba(sheet, P._hstack([P._load_rgba(p) for p in (a, b, c)]))
        written.append(sheet)
        print(f"[beacon] scale preview sheet [noon off | noon on | dusk on] -> {sheet}")
    finally:
        rig.restore()                               # removes the tracked tower, camera, lights
        for o in (beacon, tower_obj):
            try:
                me = o.data
                bpy.data.objects.remove(o, do_unlink=True)
            except ReferenceError:                  # already removed by the rig
                continue
            if me.users == 0:
                bpy.data.meshes.remove(me)
        shutil.rmtree(scratch, ignore_errors=True)
    return written, anchor


# --------------------------------------------------------------------------------------
# Command line
# --------------------------------------------------------------------------------------

def parse_args(argv):
    ap = argparse.ArgumentParser(prog="beacon.py", description="SkiBeaconRedA generator")
    ap.add_argument("--out", default=None, help="letter folder, e.g. ../import/props/A")
    ap.add_argument("--export", action="store_true", help="write the asset folder (needs --out)")
    ap.add_argument("--preview", default=None, help="preview folder (pbr / dusk / normals / density)")
    ap.add_argument("--review", action="store_true", help="write assets/preview/review/<Name>.blend")
    ap.add_argument("--no-measure", action="store_true",
                    help="export without the tower anchor table (tests, fresh-process check)")
    ap.add_argument("--measure", action="store_true", help="print the anchor table and exit")
    ap.add_argument("--scale-preview", default=None, help="render the beacon on a scratch GD16")
    ap.add_argument("--json", default=None, help="--measure: write the rows here")
    return ap.parse_args(argv)


def main(argv=None):
    if argv is None:
        argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    args = parse_args(argv)
    clear_scene()
    if args.measure:
        rows = measure_anchors()
        if args.json:
            with open(args.json, "w", encoding="utf-8") as fh:
                json.dump(rows, fh, indent=2)
        bad = [r["prefab"] for r in rows if not r.get("ok")]
        print(f"[beacon] measure: {len(rows) - len(bad)} / {len(rows)} OK"
              + (f"; CHECK {bad}" if bad else ""))
        if bad:
            sys.exit(1)
        return
    if args.scale_preview:
        scale_preview(args.scale_preview)
        return
    if args.export:
        if not args.out:
            raise SystemExit("--export needs --out")
        export(args.out, preview_dir=args.preview, review=args.review,
               measure=not args.no_measure, args=argv)
        return
    obj = build_beacon()
    contracts(obj)
    reach_table(obj)
    print(f"[beacon] built {obj.name} (no export; pass --out <letter dir> --export)")


if __name__ == "__main__":
    main()
