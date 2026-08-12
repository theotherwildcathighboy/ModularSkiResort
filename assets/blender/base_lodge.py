"""
A-Frame Day Lodge generator - the ski area's anchor building (Phase 2 flagship).

DESIGN ("A-Basin day lodge, Pali era") - ROUND C: "SkiAFrameDayLodge". The round-B
massing passed review ("looks awesome"); round C commits the asset to a TRUE A-FRAME
and settles its place in the lodge ladder:

  C1  A-FRAME PROPORTIONS. A-Basin's actual base lodge is a steep A-frame where the
      roof IS the building. Main + prow pitches now 53 degrees; eaves sweep down to
      1.1 m above the wings' window heads (EAVE_Z 5.3, barely above the deck); the
      timber story shrinks to a 2.4 m band of ribbon windows tucked under the eaves;
      ridge height FOLLOWS from the pitch (16.7 m main / 15.9 prow). The wall plates
      are DERIVED (S2_Z = EAVE_Z + pitch * overhang), so pitch changes can never
      strand a wall above its roof again. Silhouette-reads as an A-frame at range.
  C2  TIER 1 of a future lodge ladder - cheapest, first unlock, least tourism.
      Modest is correct: ribbon bands, one loft pair per gable end, a small tucked
      entry gable. No ornament creep.
  C3  Deck furniture: the modeled pedestal TABLES are gone - deck dressing comes from
      reused vanilla props at prefab level (docs/research/vanilla-reuse.md). The
      UMBRELLAS stay (slot-2 recolorable canopies, a deliberate feature), respaced
      into wing groups clear of the prow roof, each on a small weighted stand so the
      pole doesn't grow out of bare decking.

Held over from round B: solid stone-coursed masonry base (no glazing in concrete;
painted STONE BAND, see below), framed window bands only, true-gable roofs built from
open slabs + rake boards, the prow's mullioned glass wall filling the gable (the
A-Basin signature), full-width sundeck, through-roof stone chimney (now 17.6 to clear
the taller ridge), timber-framed grade entry.

Reference set (web round, user green-lit): A-Basin's base lodge is the steep A-FRAME
(ex missile-test building) - now driving the whole massing, not just the prow;
6th Alley's two-story deck / wood facade - the full-width sundeck over the masonry
base; Colorado lodge grammar (stone bases carrying expansive decks, deep eaves,
banded openings) - stone course, overhangs, window rhythm.

Recolour language matches the lift family: slot 1 (quad 3, light 0.80 albedo) = trim,
fascia, barge/rake boards, corner boards, doors, window frames + mullions, deck
railing - the accent kit; slot 2 (pad-strip texels, ControlMask G) = the deck
UMBRELLAS' canopies, default pad-orange via the prefab VariationSet. Timber cladding
and stone are deliberately NOT recolorable.

WOOD BAND (round A, retuned): the lift atlas has no timber, so the lodge paints lap
siding into quad 0's reserved top (cs2_asset.QUAD_RESERVED_TOP keeps smart-projected
islands out of v 0.44..0.50). Mip-bleed contract as the tower pier band: painted to
0.4995, geometry maps only to 0.4900 - quad 2's near-black glass sits directly above
v 0.5 (measured the hard way in the tower M round).

STONE BAND (round B; ROUND D density rework): same mechanism, second band - the top
of quad 1 (galv) is also reserved (this generator sets cs2_asset.QUAD_RESERVED_TOP[1]
for ITS OWN process only; tower/terminal builds run their own Blender and never see
it). Two zones by column: zone A = base walls + skirt; zone B = the chimney.

ROUND D (stone-density pass): round C mapped the whole 43 m plinth run across zone A
once - ~26 px/m, every stone ~10 blurry pixels (field-confirmed blur). Now the plinth
walls carry 8 m segmentation loops (STONE_SEG), each bay maps across the FULL zone-A
width, and the ashlar is painted WRAP-CONTINUOUS horizontally: stones crossing the
right edge continue on the left, painted wrap padding sits outside both zone edges
(bilinear/mip insurance), and each course's wrap point falls on an ordinary mortar
joint - so bay seams are invisible. The band also grew vertically by claiming more of
quad 1 (reserved top 0.12 -> 0.33; the galv islands - roof slabs, ridge caps - pack a
little flatter, tolerable on the uniform Metal032 photo). Wall density: ~26 -> ~195
px/m horizontal, ~25 -> ~82 px/m vertical. Chimney: same taller window (~10 -> ~33
px/m vertical) and a per-face mapping fix (the old run%3 wrapped mid-face on negative
coords); its faces cannot be tiled without cutting the chimney, which round D leaves
untouched by design.

Hand-mapped parts (wood band, stone band, canopies) skip the quadrant unwrap AND the
finish bevel (hand-set UVs don't bevel - crash-pad rule).

Authoring frame: +X = slope side (deck, glass prow), -X = parking/entry side; after
orient_for_net the entry faces CS2 -Z, the road side, like the stations.

Headless:
    blender --background --factory-startup --python base_lodge.py -- --out <dir>
        [--preview <dir>] [--finish] [--export]

Design round C: previews only - run WITHOUT --export (no FBX/settings are written;
the atlas + previews are the deliverable for the user verdict). Preview set includes
prow_closeup and scale_reference (an 8 m lift-tower stand-in built from lift_tower
beside the lodge).
"""

import bpy
import bmesh
import math
import os
import sys
import argparse

import mathutils

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cs2_asset   # noqa: E402
import lift_tower  # noqa: E402  (pick_engine, frame_camera, apply_finish_textures)

TEX = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "textures")

QUAD_CONCRETE, QUAD_GALV, QUAD_GLASS, QUAD_PAINT = 0, 1, 2, 3

# B1: reserve the top of the GALV quadrant for the stone band, process-locally.
# Every generator runs in its own headless Blender, so mutating the shared dict here
# cannot leak into tower/terminal/chair builds - they import a fresh cs2_asset.
# D: 0.12 -> 0.33 - the stone band claimed a taller window (see STONE_V0/MV0); galv
# islands (roofs, ridge caps, umbrella hardware) now pack into the bottom 67% of
# quad 1. That's ~24% extra vertical squeeze on the Metal032 photo - uniform mottle,
# no directional pattern, so the anisotropy doesn't read.
cs2_asset.QUAD_RESERVED_TOP[1] = 0.33

# ---------------------------------------------------------------------------------
# The painted wood band (quad 0 reserved top; per-asset atlas copy, towers unaffected)
# ---------------------------------------------------------------------------------
WOOD_U0, WOOD_U1 = 0.010, 0.490
WOOD_V0, WOOD_V1 = 0.4425, 0.4995      # painted rows
WOOD_MV0, WOOD_MV1 = 0.4450, 0.4900    # window geometry maps into (mip insulation)
WOOD_TONE = (0.295, 0.235, 0.185)      # dark-stained rough-sawn cladding
WOOD_SMOOTH = 0.30

# World extents the band mapping normalizes against (fixed texel density, no tiling -
# the band cannot wrap, so the longest wall must fit exactly once). C: the vertical
# range now reaches the A-frame gable apex.
WOOD_SPAN_U = 43.0                     # longest horizontal run it must cover
WOOD_Z0, WOOD_Z1 = 3.8, 16.9           # vertical range timber appears in
WOOD_X0, WOOD_X1 = -14.0, 14.0         # for horizontal faces (deck floor plan coords)

# ---------------------------------------------------------------------------------
# The painted stone band (quad 1 reserved top - the masonry base + chimney)
# ---------------------------------------------------------------------------------
STONE_U0, STONE_U1 = 0.510, 0.990      # painted columns (quad 1 spans u 0.5..1.0)
STONE_V0, STONE_V1 = 0.3395, 0.4995    # painted rows (D: grew down into quad 1)
STONE_MV0, STONE_MV1 = 0.3420, 0.4900  # window geometry maps into (mip insulation)
STONE_UA0, STONE_UA1 = 0.520, 0.900    # zone A: base walls + skirt (wrap-tiled, D)
STONE_UB0, STONE_UB1 = 0.916, 0.982    # zone B: the chimney
STONE_PAD_UV = 0.0078                  # painted wrap padding outside each zone edge
                                       # (~32 texels at the 4096 atlas)
STONE_TONE = (0.455, 0.435, 0.400)     # warm-gray Colorado fieldstone
MORTAR_TONE = (0.580, 0.565, 0.545)    # light mortar - what makes coursing read
STONE_SMOOTH = 0.22

STONE_SEG = 8.0                        # D: plinth walls cut + mapped in 8 m bays;
                                       # each bay spans the FULL zone-A width
STONE_Z0, STONE_Z1 = -3.0, 4.4         # skirt bottom .. base story top
CHIM_SPAN_U = 3.6                      # widest chimney face (shoulder 3.4 m + margin)
CHIM_Z0, CHIM_Z1 = -1.0, 17.6          # chimney z range (C: taller ridge to clear)
# D: per-axis run origins for the chimney mapping (shoulder/stack faces stay course-
# aligned). Mirror build_lodge's chimney placement: centre (RIDGE_AT_X = -4.5, 21.55),
# shoulder 3.4 x 2.4 - so x0 = -4.5 - 1.7, y0 = 21.55 - 1.2.
CHIM_X0, CHIM_Y0 = -6.2, 20.35

# Umbrella canopies map into the crash-pad strip's quiet left margin (slot 2 texels,
# clear of the warning triangle stamped at the strip's centre).
UMB_U0, UMB_U1 = 0.012, 0.150
UMB_V0, UMB_V1 = 0.948, 0.996


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
    # Location AND scale into the verts: band mapping reads world coords (the tower
    # M round shipped a black pier off local coords - never again).
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=True)
    return o


def add_cyl(name, radius, depth, center, segments=8, rotation=(0, 0, 0)):
    bpy.ops.mesh.primitive_cylinder_add(vertices=segments, radius=radius, depth=depth,
                                        location=center, rotation=rotation)
    o = bpy.context.active_object
    o.name = name
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=False)
    return o


def add_gable_prism(name, axis, a0, a1, e0, e1, eave_z, ridge_z, ridge_at=None):
    """
    Closed triangular prism, used ONLY for thin vertical GABLE-END WALL triangles
    (timber) - never for roof volumes (a roof prism's end face is what made the
    round-A prow read as a pyramid). `axis` is the RIDGE direction ('x' or 'y');
    a0..a1 span the ridge axis, e0..e1 the eave axis.
    """
    r = ridge_at if ridge_at is not None else (e0 + e1) / 2.0
    if axis == "y":
        v = [(e0, a0, eave_z), (r, a0, ridge_z), (e1, a0, eave_z),
             (e0, a1, eave_z), (r, a1, ridge_z), (e1, a1, eave_z)]
    else:
        v = [(a0, e0, eave_z), (a0, r, ridge_z), (a0, e1, eave_z),
             (a1, e0, eave_z), (a1, r, ridge_z), (a1, e1, eave_z)]
    faces = [(0, 1, 2), (5, 4, 3), (0, 3, 4, 1), (1, 4, 5, 2), (0, 2, 5, 3)]
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(v, [], faces)
    mesh.validate()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    return obj


def add_roof_slab(name, axis, a0, a1, e_edge, e_z, r_edge, r_z, thick=0.24):
    """
    One sloped roof PLANE as a thin tilted box: spans a0..a1 along the ridge axis,
    slopes from the eave line (e_edge, e_z) up to the ridge line (r_edge, r_z). Two
    of these meeting at a ridge make an OPEN gable - the gable end shows whatever
    wall stands there, not a roof-textured triangle. Also reused (narrow span) for
    the rake/barge boards dressing the open slab ends.
    """
    length = math.hypot(r_edge - e_edge, r_z - e_z)
    cz = (e_z + r_z) / 2.0
    ce = (e_edge + r_edge) / 2.0
    if axis == "y":
        # slope runs in x-z; rotate about Y by -phi so local +X follows eave->ridge
        phi = math.atan2(r_z - e_z, r_edge - e_edge)
        rot, size, center = (0.0, -phi, 0.0), (length, a1 - a0, thick), \
            (ce, (a0 + a1) / 2.0, cz)
    else:
        # slope runs in y-z; rotate about X by +psi so local +Y follows eave->ridge
        psi = math.atan2(r_z - e_z, r_edge - e_edge)
        rot, size, center = (psi, 0.0, 0.0), (a1 - a0, length, thick), \
            ((a0 + a1) / 2.0, ce, cz)
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=center, rotation=rot)
    o = bpy.context.active_object
    o.name = name
    o.scale = size
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return o


def segment_stone_walls(obj):
    """
    D: cut the plinth/skirt box into STONE_SEG m bays with bisect planes so each
    vertical wall face can map its own bay across the full stone zone (map_to_stone).
    Pure topology - the silhouette is untouched, and remove_doubles at join welds the
    coincident cut verts where basewalls meets skirt (their shared 8 m grid means the
    welded corners carry identical UVs, keeping coursing unbroken across the joint).
    Cut planes anchor at the wall corners (BASE_X0/BASE_Y0), matching map_to_stone's
    bay origins.
    """
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    cuts = [((BASE_X0 + STONE_SEG * k, 0.0, 0.0), (1.0, 0.0, 0.0))
            for k in range(1, int(math.ceil((BASE_X1 - BASE_X0) / STONE_SEG)))]
    cuts += [((0.0, BASE_Y0 + STONE_SEG * k, 0.0), (0.0, 1.0, 0.0))
             for k in range(1, int(math.ceil((BASE_Y1 - BASE_Y0) / STONE_SEG)))]
    for plane_co, plane_no in cuts:
        bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:],
                               plane_co=plane_co, plane_no=plane_no)
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    return obj


# ---------------------------------------------------------------------------------
# Hand mapping: wood band + stone band + umbrella canopies
# ---------------------------------------------------------------------------------

def map_to_wood(obj):
    """
    Planar-map a timber part into the wood band at fixed world density. Vertical
    faces: u = the wall's horizontal run, v = height (lap siding reads horizontal).
    Horizontal faces (deck floor, soffits): u = y, v = x - the painted lap lines then
    run lengthwise, which is exactly how deck planks lie. World coords keep adjacent
    walls continuous; everything clamps inside the mapped window.
    """
    mesh = obj.data
    if not mesh.uv_layers:
        mesh.uv_layers.new()
    uv = mesh.uv_layers.active.data

    def band(ufrac, vfrac):
        ufrac = min(max(ufrac, 0.0), 1.0)
        vfrac = min(max(vfrac, 0.0), 1.0)
        return (WOOD_U0 + (0.01 + 0.98 * ufrac) * (WOOD_U1 - WOOD_U0),
                WOOD_MV0 + vfrac * (WOOD_MV1 - WOOD_MV0))

    for poly in mesh.polygons:
        n = poly.normal
        for li in poly.loop_indices:
            co = mesh.vertices[mesh.loops[li].vertex_index].co
            if abs(n.z) < 0.7:
                run = co.y if abs(n.x) >= abs(n.y) else co.x
                uvco = band((run + WOOD_SPAN_U / 2.0) / WOOD_SPAN_U,
                            (co.z - WOOD_Z0) / (WOOD_Z1 - WOOD_Z0))
            else:
                uvco = band((co.y + WOOD_SPAN_U / 2.0) / WOOD_SPAN_U,
                            (co.x - WOOD_X0) / (WOOD_X1 - WOOD_X0))
            uv[li].uv = uvco


def _stone_uv(zone_u0, zone_u1, ufrac, vfrac):
    # D: NO horizontal inset - a bay maps edge-to-edge across its zone. Safe because
    # the paint is wrap-continuous and paint_stone_band lays STONE_PAD_UV of painted
    # wrap padding OUTSIDE both zone edges, so bilinear/mip taps at u = 0/1 blend
    # with the correct wrapped masonry, never with raw galv.
    ufrac = min(max(ufrac, 0.0), 1.0)
    vfrac = min(max(vfrac, 0.0), 1.0)
    return (zone_u0 + ufrac * (zone_u1 - zone_u0),
            STONE_MV0 + vfrac * (STONE_MV1 - STONE_MV0))


def map_to_stone(obj):
    """
    Base walls / skirt -> stone band zone A. D: the walls are pre-cut into STONE_SEG
    (8 m) bays by segment_stone_walls; each vertical face maps ITS OWN bay across the
    full zone-A width, which is what buys the ~8x horizontal density. The bay index
    comes from the FACE CENTRE, its corners map 0..1 relative to that bay - so a
    corner shared by two bays gets u=1 on the left face and u=0 on the right face,
    and the wrap-continuous paint makes that seam invisible. v spans the whole
    masonry range (skirt bottom -3.0 .. base top 4.4) so coursing runs unbroken
    across the skirt/wall joint. Horizontal faces (the exposed cap ring, skirt
    underside) take a right-hand sliver of the zone: coursing across a flat top is
    wrong but those faces are grazing-angle or buried.
    """
    mesh = obj.data
    if not mesh.uv_layers:
        mesh.uv_layers.new()
    uv = mesh.uv_layers.active.data
    for poly in mesh.polygons:
        n = poly.normal
        along_y = abs(n.x) >= abs(n.y)      # +/-X wall -> run is y, else run is x
        if abs(n.z) < 0.7:
            run0 = BASE_Y0 if along_y else BASE_X0   # bay origins = wall corners
            cen = poly.center.y if along_y else poly.center.x
            seg = math.floor((cen - run0) / STONE_SEG)
            for li in poly.loop_indices:
                co = mesh.vertices[mesh.loops[li].vertex_index].co
                run = co.y if along_y else co.x
                uv[li].uv = _stone_uv(STONE_UA0, STONE_UA1,
                                      (run - run0 - seg * STONE_SEG) / STONE_SEG,
                                      (co.z - STONE_Z0) / (STONE_Z1 - STONE_Z0))
        else:
            for li in poly.loop_indices:
                co = mesh.vertices[mesh.loops[li].vertex_index].co
                uv[li].uv = _stone_uv(STONE_UA1 - 0.014, STONE_UA1 - 0.002,
                                      (co.x + 15.0) / 30.0, (co.y + 22.0) / 44.0)
    return obj


def map_to_chimstone(obj):
    """
    Chimney -> stone band zone B. The chimney's z range (18.6 m) dwarfs the walls'
    (7.4 m); zone B is painted with proportionally shorter course rows so world
    course height comes out similar. u = the face's run from the chimney footprint's
    own corner (CHIM_X0/CHIM_Y0) over CHIM_SPAN_U - all faces on one axis share
    texels AND stay course-aligned between shoulder and stack, which photo-real
    masonry tolerates fine. (D fix: the old `run % 3` wrapped mid-face for these
    negative/straddling coords, smearing a reversed gradient across the face.)
    D leaves the chimney untiled: tiling would need extra cuts in the chimney box,
    which the round-D geometry contract does not allow - it inherits the taller
    band window instead (~3.3x vertical density).
    """
    mesh = obj.data
    if not mesh.uv_layers:
        mesh.uv_layers.new()
    uv = mesh.uv_layers.active.data
    for poly in mesh.polygons:
        n = poly.normal
        along_y = abs(n.x) >= abs(n.y)
        for li in poly.loop_indices:
            co = mesh.vertices[mesh.loops[li].vertex_index].co
            if abs(n.z) < 0.7:
                run, run0 = (co.y, CHIM_Y0) if along_y else (co.x, CHIM_X0)
                uvco = _stone_uv(STONE_UB0, STONE_UB1,
                                 (run - run0) / CHIM_SPAN_U,
                                 (co.z - CHIM_Z0) / (CHIM_Z1 - CHIM_Z0))
            else:
                uvco = _stone_uv(STONE_UB1 - 0.014, STONE_UB1 - 0.002,
                                 (co.x + 8.0) / 16.0, (co.y - 18.0) / 8.0)
            uv[li].uv = uvco
    return obj


def map_canopy(obj):
    """Umbrella canopy -> the pad strip's quiet margin (slot 2): angle -> u, radius -> v."""
    mesh = obj.data
    if not mesh.uv_layers:
        mesh.uv_layers.new()
    uv = mesh.uv_layers.active.data
    for poly in mesh.polygons:
        ca = math.atan2(poly.center.y - obj.get("cy", 0), poly.center.x - obj.get("cx", 0))
        for li in poly.loop_indices:
            co = mesh.vertices[mesh.loops[li].vertex_index].co
            dx, dy = co.x - obj.get("cx", 0), co.y - obj.get("cy", 0)
            a = math.atan2(dy, dx)
            d = a - ca
            if d > math.pi:
                d -= 2.0 * math.pi
            elif d < -math.pi:
                d += 2.0 * math.pi
            afrac = (ca + d + math.pi) / (2.0 * math.pi)
            rfrac = min(math.hypot(dx, dy) / 1.55, 1.0)
            uv[li].uv = (UMB_U0 + (0.06 + 0.88 * min(max(afrac, 0.0), 1.0)) * (UMB_U1 - UMB_U0),
                         UMB_V0 + (0.05 + 0.90 * rfrac) * (UMB_V1 - UMB_V0))


def paint_wood_band(base, nrm, msk, ctl, A):
    """
    Author the timber cladding into the band: dark-stained lap siding. Horizontal
    plank courses (~0.42 m at wall density) with per-course tone variation, fine
    grain noise, dark seam rows, and a matching normal groove per seam so the laps
    catch light. Not recolourable. Course height derives from the band's texel
    density - the vertical world range grows with the building.
    """
    import numpy as np
    rng = np.random.default_rng(41)
    c0, c1 = int(WOOD_U0 * A), int(WOOD_U1 * A)
    r0, r1 = int(WOOD_V0 * A), int(WOOD_V1 * A)
    h, w = r1 - r0, c1 - c0

    rows_per_m = ((WOOD_MV1 - WOOD_MV0) * A) / (WOOD_Z1 - WOOD_Z0)
    P = max(3, int(round(0.42 * rows_per_m)))       # plank course height in rows

    tone = np.array(WOOD_TONE, dtype=np.float32).reshape(1, 1, 3)
    grain = rng.normal(0.0, 0.020, (h, w, 1)).astype(np.float32)
    streak = np.clip(1.0 + rng.normal(0.0, 0.05, (h, 1, 1)), 0.82, 1.18).astype(np.float32)

    rows = np.arange(h)
    course = (rows // P).astype(np.int64)
    course_mul = (1.0 + rng.normal(0.0, 0.06, (course.max() + 1,))
                  .clip(-0.15, 0.15)).astype(np.float32)[course].reshape(h, 1, 1)

    px = np.clip(tone * course_mul * streak + grain, 0.0, 1.0)
    seam = (rows % P) == 0
    px[seam, :, :] *= 0.55                  # shadow line under each lap

    base[r0:r1, c0:c1, 0:3] = px
    base[r0:r1, c0:c1, 3] = 1.0
    nrm[r0:r1, c0:c1, 0] = 0.5
    nrm[r0:r1, c0:c1, 1] = 0.5
    nrm[r0:r1, c0:c1, 2] = 1.0
    nrm[r0:r1, c0:c1, 3] = 1.0
    seam_rows = np.where(seam)[0]
    nrm[r0 + seam_rows, c0:c1, 1] = 0.28    # tilt away from the light under the lap
    msk[r0:r1, c0:c1, 0] = 0.0
    msk[r0:r1, c0:c1, 3] = WOOD_SMOOTH
    ctl[r0:r1, c0:c1, 0:3] = 0.0


def paint_stone_band(base, nrm, msk, ctl, A):
    """
    Author the masonry into the stone band: random-length ashlar in a running bond
    over light mortar - the light joint grid is what makes 'stone coursing' read at
    game distance. Per-stone tone jitter (slightly warm/cool), per-texel noise, and
    a height-field normal so joints sit in grooves. Two zones (see STONE_U*): zone A
    at 8 m bay density, zone B with shorter course rows for the chimney's taller
    z range. Not recolourable.

    D: each zone is painted WRAP-CONTINUOUS horizontally. Stones tile the zone width
    circularly (a stone crossing the right edge continues on the left), every course
    ends on a real mortar joint at its own random wrap position, and the joint-groove
    normals are computed with wrapped finite differences - so u=0 and u=1 are the
    same masonry column and bay seams cannot show. STONE_PAD_UV of wrapped padding is
    painted OUTSIDE both zone edges so edge-texel filtering blends with wrapped
    masonry, not raw galv (the pier-band mip lesson, applied sideways).
    """
    import numpy as np
    rng = np.random.default_rng(53)
    r0, r1 = int(STONE_V0 * A), int(STONE_V1 * A)
    h = r1 - r0
    win_rows = (STONE_MV1 - STONE_MV0) * A
    pad = max(8, int(round(STONE_PAD_UV * A)))

    zones = (
        # (u0, u1, px/m vertical over its mapped z range, world span the u range covers)
        (STONE_UA0, STONE_UA1, win_rows / (STONE_Z1 - STONE_Z0), STONE_SEG),
        (STONE_UB0, STONE_UB1, win_rows / (CHIM_Z1 - CHIM_Z0), CHIM_SPAN_U),
    )
    for u0, u1, ppm_v, span_u in zones:
        c0, c1 = int(u0 * A), int(u1 * A)
        w = c1 - c0
        ppm_h = w / span_u
        course_px = max(3, int(round(0.55 * ppm_v)))     # ~0.55 m courses
        stone_px = max(8, int(round(1.25 * ppm_h)))      # ~1.25 m stones
        bed_px = max(1, int(round(0.05 * ppm_v)))        # horizontal mortar bed
        joint_px = max(1, int(round(0.05 * ppm_h)))      # vertical mortar joint

        px = np.empty((h, w, 3), dtype=np.float32)
        px[:] = MORTAR_TONE
        height = np.zeros((h, w), dtype=np.float32)

        y = 0
        while y < h:
            ch = max(3, int(rng.normal(course_px, course_px * 0.12)))
            y1 = min(y + ch, h)
            # Circular course: start at a random phase (running-bond offset), lay
            # stones until one course-circumference is covered, clip the last stone
            # one joint short of the start so the wrap lands on ordinary mortar.
            start = rng.uniform(0.0, stone_px)
            limit = start + w - joint_px
            x = start
            while x < limit - 2:
                sw = max(6, int(rng.normal(stone_px, stone_px * 0.25)))
                x1 = min(x + sw, limit)
                if x1 - x > 2 and y1 - y > 1:
                    t = np.array(STONE_TONE, dtype=np.float32) \
                        * (1.0 + rng.normal(0.0, 0.07))
                    t[0] *= 1.0 + rng.normal(0.0, 0.03)  # warm/cool jitter
                    t[2] *= 1.0 + rng.normal(0.0, 0.03)
                    cols = np.arange(int(x), int(x1)) % w
                    px[y:y1, cols] = np.clip(t, 0.0, 1.0)
                    height[y:y1, cols] = 1.0
                x = x1 + joint_px
            y = y1 + bed_px

        px += rng.normal(0.0, 0.012, (h, w, 1)).astype(np.float32)

        # Normal grooves at the joints (paint_panel_detail's finite-difference
        # recipe); horizontal gradient wraps so the groove shading is continuous
        # across the zone edge too.
        dhdx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5
        dhdy = np.zeros_like(height)
        dhdy[1:-1, :] = (height[2:, :] - height[:-2, :]) * 0.5
        nx, ny, nz = -dhdx * 2.5, -dhdy * 2.5, np.ones_like(height)
        ln = np.sqrt(nx * nx + ny * ny + nz * nz)

        # Write content + wrapped padding columns in one strip.
        cp0, cp1 = c0 - pad, c1 + pad

        def wrapped(a):
            return np.concatenate((a[:, -pad:], a, a[:, :pad]), axis=1)

        base[r0:r1, cp0:cp1, 0:3] = np.clip(wrapped(px), 0.0, 1.0)
        base[r0:r1, cp0:cp1, 3] = 1.0
        nrm[r0:r1, cp0:cp1, 0] = wrapped(nx / ln * 0.5 + 0.5)
        nrm[r0:r1, cp0:cp1, 1] = wrapped(ny / ln * 0.5 + 0.5)
        nrm[r0:r1, cp0:cp1, 2] = wrapped(nz / ln * 0.5 + 0.5)
        nrm[r0:r1, cp0:cp1, 3] = 1.0

        msk[r0:r1, cp0:cp1, 0] = 0.0
        msk[r0:r1, cp0:cp1, 3] = STONE_SMOOTH
        ctl[r0:r1, cp0:cp1, 0:3] = 0.0


# ---------------------------------------------------------------------------------
# The building (round C massing - the true A-frame)
# ---------------------------------------------------------------------------------

# Massing constants (metres, authoring frame). The A-frame discipline: pick the eave
# height and the pitch, DERIVE everything else. Wall plates fall out of the roof
# planes, ridge heights fall out of the pitch - nothing is free to disagree.
S1_Z = 4.4                          # top of the stone story
EAVE_Z = 5.30                       # eave tips - down at deck level (C1)
BASE_X0, BASE_X1 = -13.0, 14.0      # footprint depth  27 m (narrowed with the mass)
BASE_Y0, BASE_Y1 = -21.0, 21.0      # footprint length 42 m
S2_X0, S2_X1 = -12.0, 3.0           # A-frame mass depth (deck takes +X of this)
RIDGE_AT_X = -4.5                   # main ridge x (centred between the eave tips)
OVER = 1.1                          # horizontal eave overhang
RIDGE_Z = 16.7                      # main ridge; with these eaves = 53.0 degrees
MAIN_PITCH = (RIDGE_Z - EAVE_Z) / (RIDGE_AT_X - (S2_X0 - OVER))     # 1.326
S2_Z = EAVE_Z + MAIN_PITCH * OVER   # wing wall plate 6.76 - DERIVED from the roof
PROW_X1 = 10.0                      # gable prow pushes over the deck
PROW_Y0, PROW_Y1 = -5.0, 9.0
PROW_RIDGE_Y = 2.0
PROW_RIDGE = 15.9                   # 52.9 degrees - matches the main pitch
PROW_PITCH = (PROW_RIDGE - EAVE_Z) / (PROW_RIDGE_Y - (PROW_Y0 - 1.0))
S2P_Z = EAVE_Z + PROW_PITCH * 1.0   # prow wall plate 6.63 - derived likewise
DECK_X1 = 13.6
DECK_Y = 20.5                       # full-width sundeck
DECK_TOP = S1_Z + 0.26              # decking rides above the capbase ring (top 4.57)

# Ribbon window band shared by every story wall (C2: one modest rhythm everywhere)
SILL_Z, HEAD_Z = 4.95, 6.30

BEVEL_PREFIXES = ("roof", "ridge", "steps", "porchslab", "door", "capchimney",
                  "capbase")
BEVEL_EXCLUDE = ()


def build_lodge(finish=False):
    # (obj, quad): int = atlas quadrant; None = wood band; "stone"/"chim" = stone
    # band zones; "canopy" = pad-strip margin. Strings/None skip quadrant unwrap.
    parts = []

    def band_x(tag, wx, sgn, y0, y1, sill, head, lights):
        """
        Framed window band on a +/-X-facing wall (grouped lights sharing head and
        sill boards, jamb casings, and mullions - never a bare punched hole). Frames
        are slot-1 trim; glass rides proud of the wall so nothing is cut into it.
        """
        run, yc, zc = y1 - y0, (y0 + y1) / 2.0, (sill + head) / 2.0
        parts.append((add_box(f"glass{tag}", (0.14, run, head - sill),
                              (wx + sgn * 0.05, yc, zc)), QUAD_GLASS))
        tx = wx + sgn * 0.10
        parts.append((add_box(f"sill{tag}", (0.30, run + 0.36, 0.16),
                              (tx, yc, sill - 0.07)), QUAD_PAINT))
        parts.append((add_box(f"head{tag}", (0.26, run + 0.36, 0.20),
                              (tx, yc, head + 0.09)), QUAD_PAINT))
        for e in (y0 - 0.09, y1 + 0.09):
            parts.append((add_box(f"jamb{tag}_{e:.1f}", (0.22, 0.16, head - sill + 0.30),
                                  (tx, e, zc)), QUAD_PAINT))
        for i in range(1, lights):
            parts.append((add_box(f"mull{tag}_{i}", (0.20, 0.12, head - sill),
                                  (tx, y0 + run * i / lights, zc)), QUAD_PAINT))

    def band_y(tag, wy, sgn, x0, x1, sill, head, lights):
        """band_x rotated onto a +/-Y-facing wall (gable ends, prow flanks)."""
        run, xc, zc = x1 - x0, (x0 + x1) / 2.0, (sill + head) / 2.0
        parts.append((add_box(f"glass{tag}", (run, 0.14, head - sill),
                              (xc, wy + sgn * 0.05, zc)), QUAD_GLASS))
        ty = wy + sgn * 0.10
        parts.append((add_box(f"sill{tag}", (run + 0.36, 0.30, 0.16),
                              (xc, ty, sill - 0.07)), QUAD_PAINT))
        parts.append((add_box(f"head{tag}", (run + 0.36, 0.26, 0.20),
                              (xc, ty, head + 0.09)), QUAD_PAINT))
        for e in (x0 - 0.09, x1 + 0.09):
            parts.append((add_box(f"jamb{tag}_{e:.1f}", (0.16, 0.22, head - sill + 0.30),
                                  (e, ty, zc)), QUAD_PAINT))
        for i in range(1, lights):
            parts.append((add_box(f"mull{tag}_{i}", (0.12, 0.20, head - sill),
                                  (x0 + run * i / lights, ty, zc)), QUAD_PAINT))

    # ---- solid masonry base + below-grade skirt (stone, zero glazing) ------------
    # D: both boxes get 8 m segmentation loops so map_to_stone can tile each bay
    # across the full stone zone (the round-D density fix - a pure mesh cut).
    parts.append((segment_stone_walls(
        add_box("basewalls", (BASE_X1 - BASE_X0, BASE_Y1 - BASE_Y0, S1_Z),
                ((BASE_X0 + BASE_X1) / 2, 0, S1_Z / 2))), "stone"))
    parts.append((segment_stone_walls(
        add_box("skirt", (BASE_X1 - BASE_X0, BASE_Y1 - BASE_Y0, 3.0),
                ((BASE_X0 + BASE_X1) / 2, 0, -1.5))), "stone"))
    # water-table cap ring where masonry meets timber (concrete, reads as cut cap)
    parts.append((add_box("capbase", (BASE_X1 - BASE_X0 + 0.4, BASE_Y1 - BASE_Y0 + 0.4,
                                      0.30), ((BASE_X0 + BASE_X1) / 2, 0, S1_Z + 0.02)),
                  QUAD_CONCRETE))

    # ---- timber story: a short ribbon-window band under the sweeping roof (C1) ---
    parts.append((add_box("cladmain",
                          (S2_X1 - S2_X0, BASE_Y1 - BASE_Y0, S2_Z - S1_Z),
                          ((S2_X0 + S2_X1) / 2, 0, (S1_Z + S2_Z) / 2)), None))
    # main gable end walls: TIMBER triangles - the A-frame face at each end
    for gy in (-20.85, 20.85):
        t = add_gable_prism(f"cladgable_{gy:.0f}", "y", gy - 0.15, gy + 0.15,
                            S2_X0, S2_X1, S2_Z, RIDGE_Z + 0.05, ridge_at=RIDGE_AT_X)
        parts.append((t, None))

    # ---- gable prow over the deck (true gable, wall first, roof above) -----------
    parts.append((add_box("cladprow",
                          (PROW_X1 - 0.15 - S2_X0, PROW_Y1 - PROW_Y0, S2P_Z - S1_Z),
                          ((S2_X0 + PROW_X1 - 0.15) / 2, (PROW_Y0 + PROW_Y1) / 2,
                           (S1_Z + S2P_Z) / 2)), None))
    # Wall triangle matches the WALL width (the rake overhang belongs to the roof);
    # raking edges run parallel to and slightly inside the slab undersides.
    t = add_gable_prism("cladprowgable", "x", PROW_X1 - 0.3, PROW_X1,
                        PROW_Y0, PROW_Y1, S2P_Z, PROW_RIDGE - 0.02,
                        ridge_at=PROW_RIDGE_Y)
    parts.append((t, None))

    # ---- roofs: open A-frame SLABS with ridge caps + rake boards -----------------
    # Slabs run 0.35 PAST the ridge line so the pair overlaps under the cap; the
    # ridge-end z is extended along the same pitch so the centre planes still cross
    # exactly at the ridge - extending the edge without the z flattens the pitch
    # and opens a gap under the cap (learned in round B).
    parts.append((add_roof_slab("roofmainW", "y", -22.0, 22.0,
                                S2_X0 - OVER, EAVE_Z, RIDGE_AT_X + 0.35,
                                RIDGE_Z + 0.35 * MAIN_PITCH), QUAD_GALV))
    parts.append((add_roof_slab("roofmainE", "y", -22.0, 22.0,
                                S2_X1 + OVER, EAVE_Z, RIDGE_AT_X - 0.35,
                                RIDGE_Z + 0.35 * MAIN_PITCH), QUAD_GALV))
    # ridge cap stops short of the chimney (the cap would spear straight through it)
    parts.append((add_box("ridgemain", (0.45, 42.7, 0.14),
                          (RIDGE_AT_X, -0.75, RIDGE_Z + 0.08)), QUAD_GALV))
    # prow slabs: back ends past the ridge/plane convergence (prow ridge 15.9 meets
    # the main east plane at x -3.9), so every buried texel is inside the attic or
    # the story box - nothing pokes through the big roof
    parts.append((add_roof_slab("roofprowS", "x", -4.1, PROW_X1 + 1.1,
                                PROW_Y0 - 1.0, EAVE_Z, PROW_RIDGE_Y + 0.35,
                                PROW_RIDGE + 0.35 * PROW_PITCH), QUAD_GALV))
    parts.append((add_roof_slab("roofprowN", "x", -4.1, PROW_X1 + 1.1,
                                PROW_Y1 + 1.0, EAVE_Z, PROW_RIDGE_Y - 0.35,
                                PROW_RIDGE + 0.35 * PROW_PITCH), QUAD_GALV))
    # cap runs from the ridge's emergence out of the main east plane (x -3.9)
    parts.append((add_box("ridgeprow", (15.1, 0.45, 0.14),
                          (3.63, PROW_RIDGE_Y, PROW_RIDGE + 0.08)), QUAD_GALV))

    # rake/barge boards dress the open slab ends (slot-1 trim, like the fascia)
    for gy in (-22.06, 22.06):
        parts.append((add_roof_slab(f"rakeW_{gy:.0f}", "y", gy - 0.06, gy + 0.06,
                                    S2_X0 - OVER, EAVE_Z - 0.05, RIDGE_AT_X,
                                    RIDGE_Z - 0.02, thick=0.40), QUAD_PAINT))
        parts.append((add_roof_slab(f"rakeE_{gy:.0f}", "y", gy - 0.06, gy + 0.06,
                                    S2_X1 + OVER, EAVE_Z - 0.05, RIDGE_AT_X,
                                    RIDGE_Z - 0.02, thick=0.40), QUAD_PAINT))
    px1 = PROW_X1 + 1.16
    parts.append((add_roof_slab("rakeprowS", "x", px1 - 0.06, px1 + 0.06,
                                PROW_Y0 - 1.0, EAVE_Z - 0.05, PROW_RIDGE_Y,
                                PROW_RIDGE - 0.02, thick=0.40), QUAD_PAINT))
    parts.append((add_roof_slab("rakeprowN", "x", px1 - 0.06, px1 + 0.06,
                                PROW_Y1 + 1.0, EAVE_Z - 0.05, PROW_RIDGE_Y,
                                PROW_RIDGE - 0.02, thick=0.40), QUAD_PAINT))

    # eave fascia boards
    parts.append((add_box("fasciaslope", (0.12, 44.1, 0.42),
                          (S2_X1 + OVER + 0.07, 0, EAVE_Z - 0.06)), QUAD_PAINT))
    parts.append((add_box("fasciapark", (0.12, 44.1, 0.42),
                          (S2_X0 - OVER - 0.07, 0, EAVE_Z - 0.06)), QUAD_PAINT))
    parts.append((add_box("fasciaprowS", (6.9, 0.12, 0.42),
                          (7.65, PROW_Y0 - 1.07, EAVE_Z - 0.06)), QUAD_PAINT))
    parts.append((add_box("fasciaprowN", (6.9, 0.12, 0.42),
                          (7.65, PROW_Y1 + 1.07, EAVE_Z - 0.06)), QUAD_PAINT))

    # rafter tails under the low eaves (timber)
    def tails(name, xs, ys, sizes, z):
        for i, (tx, ty) in enumerate(zip(xs, ys)):
            parts.append((add_box(f"{name}{i}", sizes, (tx, ty, z)), None))

    ny = 17
    yy = [(-20.8 + i * 41.6 / (ny - 1)) for i in range(ny)]
    tails("tailE", [S2_X1 + 0.55] * ny, yy, (0.62, 0.12, 0.16), EAVE_Z + 0.50)
    tails("tailW", [S2_X0 - 0.55] * ny, yy, (0.62, 0.12, 0.16), EAVE_Z + 0.50)
    xx = [(5.8 + i * 1.5) for i in range(4)]
    tails("tailPS", xx, [PROW_Y0 - 0.55] * 4, (0.12, 0.62, 0.16), EAVE_Z + 0.36)
    tails("tailPN", xx, [PROW_Y1 + 0.55] * 4, (0.12, 0.62, 0.16), EAVE_Z + 0.36)

    # ---- full-width sundeck (the beach) ------------------------------------------
    parts.append((add_box("deckfloor", (DECK_X1 - S2_X1 + 0.4, 2 * DECK_Y, 0.16),
                          ((S2_X1 + DECK_X1) / 2, 0, S1_Z + 0.18)), None))
    rail_z0, rail_z1 = S1_Z + 0.82, S1_Z + 1.28

    def rail_run(name, p0, p1, count):
        for i in range(count + 1):
            t = i / count
            px_, py_ = p0[0] + (p1[0] - p0[0]) * t, p0[1] + (p1[1] - p0[1]) * t
            parts.append((add_box(f"{name}post{i}", (0.07, 0.07, 1.10),
                                  (px_, py_, S1_Z + 0.81)), QUAD_PAINT))
        mid = ((p0[0] + p1[0]) / 2, (p0[1] + p1[1]) / 2)
        length = math.hypot(p1[0] - p0[0], p1[1] - p0[1])
        along_x = abs(p1[0] - p0[0]) > abs(p1[1] - p0[1])
        for rz, tag in ((rail_z1, "top"), (rail_z0, "mid")):
            size = (length, 0.09, 0.06) if along_x else (0.09, length, 0.06)
            parts.append((add_box(f"rail{tag}{name}", size, (mid[0], mid[1], rz)),
                          QUAD_PAINT))
    rail_run("F", (DECK_X1, -DECK_Y), (DECK_X1, DECK_Y), 20)              # slope edge
    # side rails start clear of the swept-down eave tip (x 4.1) - a post through
    # the roof plane would be the A-frame's version of the punched-window sin
    rail_run("S", (4.6, -DECK_Y), (DECK_X1, -DECK_Y), 4)
    rail_run("N", (4.6, DECK_Y), (DECK_X1, DECK_Y), 4)

    # deck umbrellas (slot-2 canopies - the pop of colour). C3: NO modeled tables -
    # deck furniture comes from reused vanilla props at prefab level; the umbrellas
    # stand on small weighted bases, grouped into the two wings clear of the prow
    # roof plan (canopy r 1.55 vs prow eave fascia at y -7.06 / +11.06).
    for k, (ux, uy) in enumerate(((8.6, -17.0), (11.9, -12.7), (8.8, -9.9),
                                  (9.0, 13.2), (11.9, 17.2))):
        parts.append((add_cyl(f"umbstand{k}", 0.24, 0.08, (ux, uy, DECK_TOP + 0.04),
                              segments=8), QUAD_GALV))
        parts.append((add_cyl(f"umbpole{k}", 0.04, 2.5, (ux, uy, DECK_TOP + 1.25),
                              segments=6), QUAD_GALV))
        bpy.ops.mesh.primitive_cone_add(vertices=8, radius1=1.55, radius2=0.06,
                                        depth=0.62, location=(ux, uy, DECK_TOP + 2.49))
        can = bpy.context.active_object
        can.name = f"umbcanopy{k}"
        bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)
        can["cx"], can["cy"] = ux, uy
        map_canopy(can)
        parts.append((can, "canopy"))

    # ---- the prow's mullioned glass wall filling the A-frame gable ---------------
    # Storefront grid under a header beam, stepped clerestory panels tracking the
    # 53-degree gable above - bays continue through both levels so the wall reads
    # as ONE composition reaching for the ridge (the A-Basin signature).
    gx = PROW_X1 + 0.05
    bay = [-4.3, -1.78, 0.74, 3.26, 5.78, 8.3]
    parts.append((add_box("glassprowmain", (0.14, bay[-1] - bay[0], 2.20),
                          (gx, (bay[0] + bay[-1]) / 2, 5.95)), QUAD_GLASS))
    parts.append((add_box("sillprow", (0.32, 13.2, 0.18),
                          (PROW_X1 + 0.08, 2.0, 4.78)), QUAD_PAINT))
    parts.append((add_box("headprow", (0.30, 13.2, 0.30),
                          (PROW_X1 + 0.08, 2.0, 7.20)), QUAD_PAINT))
    parts.append((add_box("tranprow", (0.22, bay[-1] - bay[0], 0.12),
                          (PROW_X1 + 0.10, 2.0, 5.95)), QUAD_PAINT))
    for my in bay:
        parts.append((add_box(f"mullprow_{my:.1f}", (0.24, 0.14, 2.64),
                              (PROW_X1 + 0.10, my, 6.09)), QUAD_PAINT))
    for i in range(len(bay) - 1):
        mid = (bay[i] + bay[i + 1]) / 2
        parts.append((add_box(f"mullprowlt_{i}", (0.16, 0.09, 2.20),
                              (PROW_X1 + 0.08, mid, 5.95)), QUAD_PAINT))
    # clerestory: centre bay reaches toward the ridge, flanks step down with the
    # gable; outermost bays stay timber (the roof is already at head height there)
    clere = ((bay[1], bay[2], 10.4), (bay[2], bay[3], 13.7), (bay[3], bay[4], 10.4))
    for i, (cy0, cy1, top) in enumerate(clere):
        parts.append((add_box(f"glassprowcl_{i}", (0.14, cy1 - cy0, top - 7.65),
                              (gx, (cy0 + cy1) / 2, (7.65 + top) / 2)), QUAD_GLASS))
    for my, top in ((bay[1], 10.5), (bay[2], 13.8), (bay[3], 13.8), (bay[4], 10.5)):
        parts.append((add_box(f"mullprowcl_{my:.1f}", (0.20, 0.14, top - 7.65),
                              (PROW_X1 + 0.10, my, (7.65 + top) / 2)), QUAD_PAINT))

    # ---- ribbon window bands under the eaves (C2: one modest rhythm) -------------
    band_x("SlopeS", S2_X1, +1, -18.0, -8.0, SILL_Z, HEAD_Z, 5)    # slope wings
    band_x("SlopeN", S2_X1, +1, 11.0, 19.0, SILL_Z, HEAD_Z, 4)
    band_x("ParkS", S2_X0, -1, -18.0, -10.0, SILL_Z, HEAD_Z, 4)    # parking side
    band_x("ParkC", S2_X0, -1, -4.0, 4.0, SILL_Z, HEAD_Z, 4)
    band_x("ParkN", S2_X0, -1, 10.0, 18.0, SILL_Z, HEAD_Z, 4)
    band_y("EndS", -21.0, -1, -11.5, -4.0, SILL_Z, HEAD_Z, 4)      # south gable end
    band_y("LoftS", -21.0, -1, -6.5, -2.5, 8.0, 9.4, 2)
    band_y("EndN", 21.0, +1, -11.5, -6.2, SILL_Z, HEAD_Z, 3)       # north end: bands
    band_y("LoftN", 21.0, +1, -9.2, -6.2, 8.0, 9.4, 2)             # sit off-chimney
    band_y("ProwS", PROW_Y0, -1, 5.4, 9.6, SILL_Z, HEAD_Z, 3)      # prow flanks
    band_y("ProwN", PROW_Y1, +1, 5.4, 9.6, SILL_Z, HEAD_Z, 3)

    # ---- entry porch, parking side (C2: a small gable TUCKED UNDER the swept
    # eave - ridge 5.35 stays below the main slab underside everywhere, so the
    # porch nests into the A-frame instead of punching through it) ----------------
    parts.append((add_box("steps", (1.3, 6.4, 0.18), (-17.75, 0, 0.09)), QUAD_CONCRETE))
    parts.append((add_box("steps2", (0.9, 6.0, 0.18), (-17.35, 0, 0.27)), QUAD_CONCRETE))
    parts.append((add_box("porchslab", (4.0, 5.6, 0.20), (-15.4, 0, 0.42)),
                  QUAD_CONCRETE))
    for py in (-1.9, 1.9):
        parts.append((add_box(f"porchpost_{py:.0f}", (0.22, 0.22, 2.70),
                              (-16.5, py, 0.52 + 1.35)), None))
    # beam length/height sized so its ends stay UNDER the porch roof planes (the
    # first cut poked through both slabs near the eaves and read as boxes on the
    # roof); slab underside at the beam ends (y +/-2.0) is 3.30
    parts.append((add_box("porchbeam", (0.24, 4.0, 0.26), (-16.5, 0, 3.16)), None))
    # ridge 5.25 with a minimal 0.05 overlap extension: the slab tips (top 5.40)
    # stay inside the ridge cap AND under the main slab, so the little gable truly
    # nests beneath the swept eave
    parts.append((add_roof_slab("roofporchS", "x", -17.4, -12.95, -2.6, 2.85,
                                0.05, 5.295, thick=0.20), QUAD_GALV))
    parts.append((add_roof_slab("roofporchN", "x", -17.4, -12.95, 2.6, 2.85,
                                -0.05, 5.295, thick=0.20), QUAD_GALV))
    parts.append((add_box("ridgeporch", (4.45, 0.40, 0.12), (-15.18, 0, 5.35)),
                  QUAD_GALV))
    t = add_gable_prism("cladporchgable", "x", -17.15, -16.85, -2.2, 2.2, 3.20, 5.22)
    parts.append((t, None))
    for sgn in (-1, 1):
        parts.append((add_roof_slab(f"rakeporch_{sgn}", "x", -17.52, -17.40,
                                    sgn * 2.6, 2.78, 0.0, 5.18, thick=0.30),
                      QUAD_PAINT))
    # timber-framed entry surround IN the masonry story - door and sidelights are
    # framed openings in wood, never holes in raw concrete
    parts.append((add_box("entrysurround", (0.26, 5.0, 3.30), (-13.05, 0, 1.65)), None))
    parts.append((add_box("doorentry", (0.14, 2.6, 2.50), (-13.22, 0, 1.30)),
                  QUAD_PAINT))
    for sy in (-1.85, 1.85):
        parts.append((add_box(f"glassentry_{sy:.1f}", (0.12, 0.90, 2.10),
                              (-13.20, sy, 1.20)), QUAD_GLASS))
    parts.append((add_box("trimentryhead", (0.10, 5.2, 0.28), (-13.26, 0, 3.30)),
                  QUAD_PAINT))
    parts.append((add_box("trimentrydoor", (0.10, 2.9, 2.70), (-13.24, 0, 1.37)),
                  QUAD_PAINT))

    # ---- corner boards (slot-1 trim kit) -----------------------------------------
    for cx, cy, cz1 in ((S2_X0, -21.0, S2_Z), (S2_X0, 21.0, S2_Z),
                        (S2_X1, -21.0, S2_Z), (S2_X1, 21.0, S2_Z),
                        (PROW_X1 - 0.08, PROW_Y0, S2P_Z),
                        (PROW_X1 - 0.08, PROW_Y1, S2P_Z)):
        parts.append((add_box(f"trimcorner_{cx:.0f}_{cy:.0f}",
                              (0.18, 0.18, cz1 - S1_Z),
                              (cx, cy, (S1_Z + cz1) / 2)), QUAD_PAINT))

    # ---- stone chimney, north gable end (rides through the taller roof) ----------
    parts.append((add_box("chimshoulder", (3.4, 2.4, 5.4), (RIDGE_AT_X, 21.55, 1.7)),
                  "chim"))
    parts.append((add_box("chimney", (2.6, 1.8, 18.6), (RIDGE_AT_X, 21.55, 8.3)),
                  "chim"))
    parts.append((add_box("capchimney", (3.0, 2.2, 0.28), (RIDGE_AT_X, 21.55, 17.72)),
                  QUAD_GALV))

    # ---- finish bevel (skip hand-mapped parts - their UVs would smear) ------------
    if finish:
        for obj, quad in parts:
            if not isinstance(quad, int):
                continue
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

    # ---- UVs, join, pivot, orient -------------------------------------------------
    for obj, quad in parts:
        if quad is None:
            map_to_wood(obj)
        elif quad == "stone":
            map_to_stone(obj)
        elif quad == "chim":
            map_to_chimstone(obj)
        elif quad == "canopy":
            pass                        # mapped at build time
        else:
            cs2_asset.unwrap_into_quadrant(obj, quad)

    bpy.ops.object.select_all(action="DESELECT")
    for obj, _ in parts:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = parts[0][0]
    bpy.ops.object.join()
    lodge = bpy.context.active_object
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=0.0005)
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.context.scene.cursor.location = (0.0, 0.0, 0.0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    cs2_asset.orient_for_net(lodge)
    return lodge


def tri_count(obj):
    bm = bmesh.new(); bm.from_mesh(obj.data)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    n = len(bm.faces); bm.free(); return n


def render_previews(obj, out_dir, stem):
    scene = bpy.context.scene
    scene.render.engine = lift_tower.pick_engine()
    scene.render.image_settings.file_format = "PNG"
    world = bpy.data.worlds.new("PreviewWorld")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.13, 0.15, 0.18, 1.0)
    scene.world = world

    bpy.ops.object.camera_add(location=(0, 0, 0))
    cam = bpy.context.active_object
    cam.data.lens = 50
    scene.camera = cam
    bpy.ops.object.light_add(type="SUN", location=(10, -12, 20))
    key = bpy.context.active_object
    key.data.energy = 4.5
    key.rotation_euler = (math.radians(50), 0, math.radians(35))
    bpy.ops.object.light_add(type="SUN", location=(-12, 8, 12))
    fill = bpy.context.active_object
    fill.data.energy = 1.8
    fill.rotation_euler = (math.radians(65), 0, math.radians(215))

    # Post-orient: authoring +X (slope/deck/prow) -> -Y; -X (parking/entry) -> +Y;
    # authoring +Y (north gable, chimney) -> +X.
    views = [
        ("three_quarter_slope",   mathutils.Vector((1.05, -1.0, 0.42)), (1400, 1000), None),
        ("three_quarter_parking", mathutils.Vector((-1.05, 1.0, 0.42)), (1400, 1000), None),
        ("slope_front",           mathutils.Vector((0.05, -1.0, 0.20)), (1400, 1000), None),
        ("prow_closeup",          mathutils.Vector((0.50, -1.0, 0.28)), (1300, 1000),
         (mathutils.Vector((2.0, -10.5, 8.0)), 28.0, 45)),
        ("deck_closeup",          mathutils.Vector((0.9, -1.0, 0.35)), (1300, 1000),
         (mathutils.Vector((-10.0, -9.5, 5.4)), 17.0, 42)),
        ("entry_closeup",         mathutils.Vector((-0.8, 1.0, 0.25)), (1300, 1000),
         (mathutils.Vector((0.0, 14.5, 2.2)), 13.0, 42)),
        # D: the stone-density verdict view - parking-side plinth south of the entry,
        # ~16 m out at game-camera closeness; spans two 8 m bays so the wrap seam
        # (or its absence) is ON CAMERA. Post-orient: authoring (x -13, y -10) ->
        # (-10, 13).
        ("stone_closeup",         mathutils.Vector((-0.35, 1.0, 0.22)), (1400, 1000),
         (mathutils.Vector((-10.0, 13.0, 1.9)), 16.0, 45)),
    ]
    written = []
    for vname, direction, res, tight in views:
        scene.render.resolution_x, scene.render.resolution_y = res
        bounce = None
        if tight:
            tgt, dist, lens = tight
            cam.data.lens = lens
            cam.location = tgt + direction.normalized() * dist
            cam.rotation_euler = (tgt - cam.location).to_track_quat("-Z", "Y").to_euler()
        else:
            cam.data.lens = 50
            lift_tower.frame_camera(cam, obj, direction, res)
        if vname == "entry_closeup":
            # The porch sits in the building's own key-light shadow with no sky
            # bounce in the studio world; a temporary camera-side fill stands in
            # for in-game GI (same convention as the tower pier_view).
            bpy.ops.object.light_add(type="SUN", location=(0, 14, 4))
            bounce = bpy.context.active_object
            bounce.data.energy = 2.2
            bounce.rotation_euler = (math.radians(75.0), 0.0, math.radians(190.0))
        path = os.path.join(out_dir, f"{stem}_{vname}.png")
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        written.append(path)
        if bounce is not None:
            bpy.data.objects.remove(bounce, do_unlink=True)

    # ---- scale reference: an 8 m fixed-grip line tower stand-in beside the lodge -
    # Preview-only theatre: built, framed, rendered, deleted. The tower's quadrant
    # UVs read the lodge's atlas copy correctly (same shared layout), so it borrows
    # the lodge's preview material.
    tower = lift_tower.build_tower(lift_tower.TowerParams())      # mast 8.0
    tower.location = (27.5, -7.0, 0.0)
    if obj.data.materials:
        tower.data.materials.clear()
        tower.data.materials.append(obj.data.materials[0])
    c1, s1 = lift_tower.world_bounds(obj)
    c2, s2 = lift_tower.world_bounds(tower)
    lo = mathutils.Vector((min(c1[i] - s1[i] / 2, c2[i] - s2[i] / 2) for i in range(3)))
    hi = mathutils.Vector((max(c1[i] + s1[i] / 2, c2[i] + s2[i] / 2) for i in range(3)))
    helper = add_box("framehelper", tuple(hi - lo), tuple((hi + lo) / 2))
    scene.render.resolution_x, scene.render.resolution_y = 1600, 1000
    cam.data.lens = 50
    lift_tower.frame_camera(cam, helper, mathutils.Vector((0.75, -1.0, 0.32)),
                            (1600, 1000))
    bpy.data.objects.remove(helper, do_unlink=True)
    path = os.path.join(out_dir, f"{stem}_scale_reference.png")
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    written.append(path)
    tower_mesh = tower.data
    bpy.data.objects.remove(tower, do_unlink=True)
    bpy.data.meshes.remove(tower_mesh)
    return written


def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True, help="atlas/asset staging dir")
    ap.add_argument("--name", default="SkiAFrameDayLodge")
    ap.add_argument("--preview", default=None)
    ap.add_argument("--finish", action="store_true",
                    help="shipping pass: bevels + unique-UV AO rebake (4096 atlas)")
    ap.add_argument("--export", action="store_true",
                    help="write FBX + settings.json (OFF for design rounds - previews "
                         "go to the user verdict first; PIPELINE step 2)")
    return ap.parse_args(argv)


def main():
    args = parse_args()
    clear_scene()

    lodge = build_lodge(finish=args.finish)
    lodge.name = args.name
    name = cs2_asset.validate_asset_name(lodge.name)

    asset_dir = os.path.join(os.path.abspath(args.out), name)
    os.makedirs(asset_dir, exist_ok=True)

    d = lodge.dimensions
    lo, hi = lift_tower.world_bounds(lodge)[0] - lift_tower.world_bounds(lodge)[1] / 2, \
        lift_tower.world_bounds(lodge)[0] + lift_tower.world_bounds(lodge)[1] / 2
    print(f"[lodge] tris: {tri_count(lodge)}  (ceiling 9000)")
    print(f"[lodge] size (m): {d.x:.2f} x {d.y:.2f} x {d.z:.2f}  "
          f"(walls 42 x 27, A-frame)")
    print(f"[lodge] pitches: main {math.degrees(math.atan(MAIN_PITCH)):.1f} deg, "
          f"prow {math.degrees(math.atan(PROW_PITCH)):.1f} deg")
    print(f"[lodge] z range:  {lo.z:.3f} .. {hi.z:.3f}  "
          f"(skirt -3.0, main ridge {RIDGE_Z}, chimney cap ~17.86)")

    quad_sets, control_quads = cs2_asset.lift_atlas_quads(TEX)

    def _post(base, nrm, msk, ctl, A):
        cs2_asset.paint_pad_strip(base, nrm, msk, ctl, A)
        paint_wood_band(base, nrm, msk, ctl, A)
        paint_stone_band(base, nrm, msk, ctl, A)

    cs2_asset.make_texture_set_atlas(asset_dir, name, quad_sets, quad_size=2048,
                                     control_quads=control_quads, post=_post)
    if args.finish:
        # D: the wood and stone bands ride the same density-protected path as the
        # pad strip - islands pinned, texels restored 1:1 after the rebake. Without
        # these rects the repack would resample the hand-painted bands (smear) and
        # packed islands could land under them and get stomped by the restore.
        lift_tower.apply_finish_textures(
            lodge, asset_dir, name, atlas_size=4096,
            reserved_strips=[
                (cs2_asset.PAD_U0, cs2_asset.PAD_V0,
                 cs2_asset.PAD_U1, cs2_asset.PAD_V1),
                (WOOD_U0, WOOD_V0, WOOD_U1, WOOD_V1),
                (STONE_U0, STONE_V0, STONE_U1, STONE_V1),
            ],
            ao_samples=32,
        )

    if args.preview:
        prev = os.path.abspath(args.preview)
        os.makedirs(prev, exist_ok=True)
        cs2_asset.preview_material(lodge, asset_dir, name)
        for f in render_previews(lodge, prev, name):
            print(f"[lodge] preview -> {f}")
        lodge.data.materials.clear()

    if args.export:
        cs2_asset.export_fbx(lodge, os.path.join(asset_dir, f"{name}.fbx"))
        cs2_asset.write_settings(asset_dir, name)
        print(f"[lodge] EXPORTED asset folder: {asset_dir}")
    else:
        print(f"[lodge] design round: previews only, no FBX (atlas staged in {asset_dir})")


if __name__ == "__main__":
    main()
