"""
Chairlift terminal generator v2: drive station (bottom) and return station (top).

v2 adds these items:
- Real CC0 PBR textures through the 2x2 atlas (cs2_asset.make_texture_set_atlas), with per-part UV quadrants.
- A lowered machinery housing (v1 read too tall in game).
- Chunkier columns.
- A bullwheel that is visibly exposed under the housing, with a shaft gap.

J: The built-in entry towers (and their droop-anchor ropes) left both builders. Each station's tower is now a HOSTED
SUB-OBJECT prefab-side: SkiLiftTowerET68 at the drive and SkiLiftTowerET62 at the return. They stand at x -6.9 / -4.9 with
the anchor at axle height 5.84 / 5.24. The interior static ropes keep their exact start points. This is the whole contract
between the two halves. The stations also moved to a 4096 atlas (quad_size=2048).

L (art pipeline v2, DECISIONS 103/104): The baked static chairs are GONE. The live chairs now dive from the entry tower, go
around the bullwheel and climb back out on the modeled rope (ChairPinSystem). A static chair would duplicate a moving one.
Every part is smooth-shaded by angle with weighted normals (cs2_asset.shade_by_angle). The flat primitive default reached the
game as lit facets. Parts are tagged before the join. The --lods flag ships an authored LOD1/LOD2 chain, decimated from the
finished mesh (cs2_asset.make_lod_chain).

L full v2 (roundL-spec, 2026-09-22):
- ENGINE BASE instead of the modeled 3 m skirts. The pad, hut base, knoll ramps and crest are OPEN-BOTTOM parts with their
  side-face bottom edges exactly at z = 0 (cs2_kit.add_box / add_wedge open_bottom=True). BatchMeshHelpers.AddBaseLines
  builds the foundation skirt from those edges. It DROPS any edge that touches a face lying at/below y = 0. That is why the
  closed E pads floated and K needed skirts. cs2_validate.ground_ring_report is the acceptance check. main() refuses to
  export when the pad and hut base are not "OK".
- Parts kit v1 (cs2_kit): leg flanges with real studs, portal gussets, rail stiffener rings, groove-profile bank sheaves and
  bullwheel rim (rope torus 1.82 and the wheel center unchanged), cable clamps, catwalk grating + handrail, hut doors,
  louvered hatches, roof drip edges. The return station adds mast base/collar flanges and knoll handrails.
- `_Win` sub-meshes: the hut and cabin glass bands leave the main mesh (cs2_submesh, room cells (2,1) hut / (3,2) cabin) and
  ship as <Name>_Win.fbx + <Name>_LOD1_Win.fbx.
- Textures 2048 (quad_size 1024). The texture set has a unique repack with density control (cs2_uv.finish_packer, corrugated
  walls x2). It has a geometry-keyed relief layer blended into the Normal (cs2_compose: corrugation 0.076 m on
  machinebox/cabin/hut, seams 1.2 m + bolt rows on legs/beams/rails, 0.10 m helix on ropes, grate on the catwalk). It also
  has per-part value noise and AO.
- Validators + manifest (cs2_validate) and the shared preview rig (cs2_preview: pbr / dusk / normals / lod / ab vs K).

Atlas quadrants:
    0 concrete   (pad)                          not recolorable
    1 galvanized (columns, shaft, cabinet)      not recolorable
    2 plates     (bullwheel)                    not recolorable
    3 painted    (housing, hut)                 RECOLORABLE (ControlMask R = 1)

    blender --background --factory-startup --python lift_terminal.py -- --out ../import --kind drive --preview ../preview

Import contract as ever (SPEC 11.0): base-pivoted, +X travel pre-conversion, no underscores.
"""

import bpy
import bmesh
import json
import math
import os
import sys
import argparse
from dataclasses import dataclass

import mathutils
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cs2_asset  # noqa: E402
import cs2_kit  # noqa: E402
import cs2_uv  # noqa: E402
import cs2_compose  # noqa: E402
import cs2_submesh  # noqa: E402
import cs2_validate  # noqa: E402
import cs2_preview  # noqa: E402
import lift_tower  # noqa: E402  (reuses its preview camera helpers + finish pass)

TEX = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "textures")

QUAD_CONCRETE, QUAD_GALV, QUAD_GLASS, QUAD_PAINT = 0, 1, 2, 3
# The atlas definition moved to cs2_asset.lift_atlas_quads, so towers and chairs share the exact same palette and recolor
# slot. Concrete is tinted down there (it rendered near-white in game). Painted steel stays the dark Pali look.
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
    """Cylinder between two arbitrary 3D points, for cables that angle in both axes."""
    a, b = mathutils.Vector(p0), mathutils.Vector(p1)
    d = b - a
    o = add_cyl(name, radius, d.length, tuple((a + b) / 2.0), segments=segments)
    o.rotation_euler = d.to_track_quat("Z", "Y").to_euler()
    return o


def add_droop_rope(name, p0, p1, radius, sag, segs=5):
    """
    Rope between two points with parabolic droop (user direction, I-generation). Our static haul-rope pieces sag like the
    game's own cables, so the free-air anchor junction reads as one continuous catenary instead of a kink. Returns a list of
    strut objects that approximate the curve.
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
    This bullwheel matches the Pali photos (liftblog img_6900: the summit wheel seen from below). It has a deep dark RIM ring
    with a light galvanized SPOKE web and hub, and you can see straight through it. The old flat cylinder read as a dinner
    plate. The rim outer edge sits at r=1.80, so the rope torus (r 1.82) lies proud in the groove and chair grips at radial
    1.84 stay clear, exactly as before.

    L: the rim is the cs2_kit.sheave groove profile spun 32 times (r 1.80, width 0.40, groove 0.06 deep, inner radius 1.55 so
    the spokes bury in it) instead of the K torus. The outer edge is still 1.80 and the center is untouched. Name:
    sheave_<prefix>bull_rim.
    """
    parts = []
    parts.extend(cs2_kit.sheave(f"{prefix}bull", 1.80, 0.40, (cx, cy, cz), "Z", segs=32,
                                quad_rim=QUAD_PAINT, hub=False, groove=0.06, r_hub=1.55))
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
# The painted tension scale (liftblog img_6828/6870: "1 2 3 ... 16" in white along the rail tube). It is the carriage
# position indicator that every Yan drive-tension terminal carries. The digits are stamped into a thin strip at the top edge
# of the PAINT quadrant of this terminal's atlas copy. Two thin plates on the rails UV-map exactly onto the strip.
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
    make_texture_set_atlas `post` hook: paint 1..16 in white into the scale strip. The pixel buffer is bottom-up, so higher
    rows = higher V. ControlMask R is zeroed under every painted texel, so the digits stay white when the player recolors the
    black steel (they are painted markings, not steel).

    Every pixel measure scales with `sc`. J: the stations moved to a 4096 atlas, which doubles the pixel height of the strip.
    Without this scaling the digits shrank to half the strip and sat at its bottom edge.
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
    Map the outboard face of the plate onto the tension-scale strip. Every other face lands on a quiet texel of the paint
    quadrant. `outboard` is the sign of the Y of the face normal. `ascending` makes the numbers run with +X or -X, so each
    side of the terminal reads left-to-right from its own viewpoint (the real machine is painted per side).
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
            # Tiny NON-DEGENERATE patch on quiet paint texels. It was a single point: zero-area islands upset tangent export
            # and, since the finish pass, pack_islands during the unique repack. L: the patch is projected along the dominant
            # axis of the face itself. The x/z projection left the +-X end faces and the top/bottom faces with zero UV area
            # (cs2_validate uv-zero).
            n = poly.normal
            ax = max(range(3), key=lambda i: abs(n[i]))
            others = [i for i in range(3) if i != ax]
            ext = [(min(v.co[i] for v in mesh.vertices), max(v.co[i] for v in mesh.vertices))
                   for i in range(3)]
            for li in poly.loop_indices:
                co = mesh.vertices[mesh.loops[li].vertex_index].co
                a, b = others
                uv.data[li].uv = (0.555 + 0.008 * (co[a] - ext[a][0]) / max(ext[a][1] - ext[a][0], 1e-6),
                                  0.555 + 0.008 * (co[b] - ext[b][0]) / max(ext[b][1] - ext[b][0], 1e-6))


# Boxy parts that take the finishing-pass bevel. Glass bands and the rope torus are excluded. (L: the baked static chairs are
# gone, so live chairs ride through the terminal. The skirts are gone too. The ENGINE builds the foundation from the z = 0
# edges of the open-bottom ground parts. These edges are boundary edges, so they never get beveled.)
BEVEL_PREFIXES = ("pad", "beam_", "saddle_", "endmember_", "carr_",
                  "machfloor", "machinebox", "boxroof", "cabfloor", "cabin", "cabroof",
                  "hutbase", "hut", "hutroof", "cabinet", "bankmount_",
                  "plinth", "crest", "rampup", "rampdown")
BEVEL_EXCLUDE = ("hutwin", "cabwin")

# Smooth-by-angle threshold (art-pipeline-v2.md 3.1). 66 keeps the 6-segment tubes (60 deg between faces) smooth. Every box
# edge (90) and the knoll ramp (~68) break sharp.
SHADE_ANGLE = 66.0

# Authored LOD chain (art-pipeline-v2.md 3.7). The build deletes parts outright per level and collapse-decimates the rest to
# an absolute triangle target (LOD1 = 45 % of LOD0, LOD2 = 500 drive / 400 return). Level 1 drops sub-pixel hardware (L: +
# every kit stud, clamp, drip edge, hatch, handrail post and kick plate). Level 2 keeps only the masses that make a terminal
# recognizable at 300 m: pad, legs, beams, rails, housing, cabin, hut, wheel rim.
#
# NOTE the rail prefix of the kit: the catwalk / knoll handrails are "rail_catwalk_*" / "rail_knoll_*". Never use a bare
# "rail_", which would also match the main tube rails.
LOD1_DELETE = ("raillug_", "cabmull_", "stovepipe", "wx", "lrung_", "post_",
               "tcable_", "ropeguard_", "sheave_bank", "bankmount_", "cwplate",
               "hatch_", "bolt_", "clamp_", "drip_", "kick_")
LOD2_DELETE = LOD1_DELETE + ("spoke", "tspoke", "wheelhub", "twheelhub", "rail_catwalk",
                             "rail_knoll", "rail_bay", "lrail_", "grate_", "scale_", "railcap_",
                             "flange_", "gusset_", "stiff_", "door_",
                             "saddle_", "endmember_", "carr_", "cabinet", "hutroof",
                             "boxroof", "cabroof", "machfloor", "cabfloor", "mastcollar",
                             "plinth", "wheelshaft", "cweight", "divrope", "wheelrope")

# Texel-density weights for the unique repack (cs2_uv.set_density, longest prefix wins). Corrugated walls get x2, because the
# 0.076 m corrugation needs >= ~53 px/m to clear the 2-texel floor at 2048. Ropes get x1.5 for the helix. Legs/beams/rails
# get x1.5 for the bolt rows.
DENSITY_WEIGHTS = {
    "machinebox": 2.0, "cabin": 2.0, "cabinet": 1.0, "cabmull_": 1.0,
    "hut": 2.0, "hutbase": 1.0, "hutroof": 1.0, "hutwin": 1.0, "cabwin": 1.0,
    "divrope": 1.5, "wheelrope": 1.5, "tcable_": 1.5,
    "leg_": 1.5, "beam_": 1.5, "rail_": 1.5, "rail_catwalk": 1.0, "rail_knoll": 1.0,
    "mast": 1.5, "mastcollar": 1.0,
}

# Shipping texture recipe (L): 2048 atlas = quad_size 1024, finish at the same size.
ATLAS_QUAD_PX = 1024
FINISH_ATLAS = 2048
AO_SAMPLES = 48
# Island margin for the unique repack, in texels. The default of cs2_uv, max(16, A // 64), is 32 px at 2048 (64 px between
# neighbors). With ~300-960 smart-projected islands, that packed the stations at a median 36 px/m (12 px: 35 px/m). At this
# island count the gap dominates, not the margin formula. 6 px = 12 px gap = 1.5 texels at mip 3, the same gap-per-mip K
# shipped at 4096 (margin 0.004). The bake margin (8 px EXTEND) fills the gutter with the island's own tone either way.
PACK_MARGIN_PX = 6


def finish_packer(obj, mesh, A, reserved):
    """
    lift_tower.apply_finish_textures `packer` hook: cs2_uv.pack_unique with the station margin (PACK_MARGIN_PX) instead of
    the default of cs2_uv.finish_packer. The finish pass already created "UVUnique" from "UVAtlas". pack_unique rebuilds it
    from the active source layer, so the code re-activates the source first (same result, tighter pack).
    """
    mesh.uv_layers.active = mesh.uv_layers["UVAtlas"]
    cs2_uv.pack_unique(obj, A, reserved, margin_px=PACK_MARGIN_PX)
LOD0_CAP = {"drive": 7000, "return": 3600}
LOD2_TARGET = {"drive": 500, "return": 400}


def detail_specs():
    """
    Relief layer keyed by part-name prefix (cs2_compose.detail_height, longest prefix wins). An empty DetailSpec() EXCLUDES a
    longer name from the feature of a shorter prefix. Features: corrugation 0.076 m on machinebox / cabin / hut, seams 1.2 m
    + bolt rows on legs, beams, rails and the return mast, 0.10 m helix on every rope, painted grate on the catwalk, pressed
    seams on the door leaves.
    """
    D = cs2_compose.DetailSpec
    corr = dict(corrugation_m=0.076, corr_depth_m=0.002)
    # 36 mm heads (M24 structural bolts) cover 2 texels at >= 56 px/m. The legs / rails / beams reach that at their 1.5x
    # density weight on the 2048 atlas. 28 mm heads did not.
    steel_rings = dict(seams_m=1.2, bolt_pitch_m=0.15, bolt_r_m=0.018, seam_axes="rings")
    return {
        "machinebox": D(**corr), "cabin": D(**corr), "cabinet": D(), "cabmull_": D(),
        "hut": D(**corr), "hutbase": D(), "hutroof": D(),
        "leg_": D(**steel_rings),
        "beam_": D(seams_m=1.2, bolt_pitch_m=0.15, bolt_r_m=0.018),
        "rail_": D(**steel_rings), "rail_catwalk": D(), "rail_knoll": D(),
        "mast": D(**steel_rings), "mastcollar": D(),
        "divrope": D(helix_m=0.10), "wheelrope": D(helix_m=0.10), "tcable_": D(helix_m=0.10),
        "grate_catwalk": D(grate_m=0.03), "grate_catwalk_lip": D(),
        "door_hut_leaf": D(seams_m=0.30, seam_depth_m=0.003, seam_axes="rings"),
    }


def make_post_compose(obj, reserved, specs=None):
    """
    lift_tower.apply_finish_textures `post_compose` hook (linear space, after the AO multiply). The hook does these steps:
    1. Rasterize the relief layer on the unique layout.
    2. Composite it through cs2_compose.compose_maps (per-part value noise, RNM detail normal, gloss variation, channel
       rules). ao=None because the finish pass already multiplied its AO in.
    3. Print the density report while both UV layers still exist.
    4. Give the uncovered gutter a flat normal and renormalize the covered texels. Photo normal sets are not unit length. nz
       clamped >= 0.05 keeps B > 0.5 everywhere.

    `specs` (generation M, 2026-10-11): a zero-argument function that returns the relief table. None = this module's
    detail_specs, looked up at CALL time (lift_terminal_detach still overrides the module global through _lt_override,
    unchanged).
    """
    def hook(base, nrm, msk, ctl, ao, mesh, A):
        hook.density = cs2_uv.density_report(obj, A, reserved_rects=reserved)
        part_map, ppm_map = cs2_compose.rasterise_face_fields(mesh, A)
        h = cs2_compose.detail_height(mesh, A, (specs or detail_specs)())
        hook.height_range = (float(h.min()), float(h.max()))
        uncovered = nrm[..., 0:3].max(axis=-1) < 1e-3       # EMIT bake left them black
        base, nrm, msk, ctl = cs2_compose.compose_maps(
            base, nrm, msk, ctl, None, h, ppm_map,
            {"reserved": reserved, "part_map": part_map, "seed": 0})
        n = nrm[..., 0:3] * 2.0 - 1.0
        n[..., 2] = np.maximum(n[..., 2], 0.05)
        n /= np.maximum(np.linalg.norm(n, axis=-1, keepdims=True), 1e-6)
        nrm[..., 0:3] = n * 0.5 + 0.5
        nrm[uncovered, 0:3] = (0.5, 0.5, 1.0)
        nrm[..., 3] = 1.0
        print(f"[terminal] compose: relief height {h.min():+.4f} .. {h.max():+.4f} m, "
              f"{int((~uncovered).sum())} covered texels @ {A}")
        return base, nrm, msk, ctl
    hook.density = None
    hook.height_range = None
    return hook


def apply_finish_bevels(parts, prefixes=None, exclude=None):
    """0.02 m single-segment bevel on the boxy steel/concrete, applied BEFORE unwrap. `prefixes` / `exclude` (generation M): the
    bevel tables. None = this module's BEVEL_PREFIXES / BEVEL_EXCLUDE at call time (the override of the detach builder still
    works)."""
    pre = BEVEL_PREFIXES if prefixes is None else tuple(prefixes)
    exc = BEVEL_EXCLUDE if exclude is None else tuple(exclude)
    for obj, _ in parts:
        if obj.name.startswith(exc) or not obj.name.startswith(pre):
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
    Top station at ski-off height. The rope lane crosses the whole line at 7.04 m, so the mesh models the BREAKOVER
    (liftblog: 'a fixed bullwheel at the breakover point'). The rope dives from line height at the mesh edge down to a low
    fixed bullwheel. The ground rises to meet the chair: a built-up unload knoll like every real top station. The chair seat
    arrives ~0.6 m above the knoll crest. Riders stand up and ski off down the run-out.

    The old angled strut is gone. At this height its only footing would be in the middle of the unload run-out. So the wheel
    sits on a stout center mast on the crest instead (inside the chairs' swing circle, radius < 0.6 m).
    """
    parts = []
    rope_y = 1.8
    wheel_z = 3.7                     # rope over the wheel: seat ~2.05 m, crest 1.42 m
    crest_z = 1.42
    mound_w = 6.0                     # spans both rope lines plus chair overhang (L: 5.4 ->
                                      # 6.0 so the knoll handrails at +-2.9 clear a chair
                                      # riding at y 1.8 +- 0.9)

    # Unload knoll: up-ramp facing the line, crest deck under the wheel, run-out down back. E: both ramps are shorter,
    # because the footprint sprawled on slopes. L: the K foundation skirt is GONE. The knoll is OPEN-BOTTOM with its
    # side-face bottom edges exactly at z = 0 (ramp toes at 0.0, not -0.02). So the ENGINE extrudes the foundation from those
    # edges down to the terrain (BatchMeshHelpers.AddBaseLines, see the module docstring). The crest box drops its +-X faces,
    # because they would coincide with the end faces of the ramps.
    parts.append((cs2_kit.add_wedge("rampup", -4.6, -1.1, mound_w, 0.0, crest_z,
                                    open_bottom=True), QUAD_CONCRETE))
    parts.append((cs2_kit.add_box("crest", (2.4, mound_w, crest_z), (0.1, 0, crest_z / 2.0),
                                  open_bottom=True, drop=("+X", "-X", "+Y")), QUAD_CONCRETE))
    parts.append((cs2_kit.add_wedge("rampdown", 1.3, 2.6, mound_w, crest_z, 0.0,
                                    open_bottom=True), QUAD_CONCRETE))
    # L (user verdict): the liftie box stands LEVEL WITH THE CREST, not on the ground beside the knoll ("a liftie looking out
    # would just be staring at a wall"). A concrete bay on the +Y side of the knoll carries it. The bay is open-bottom, with
    # edges at z 0 (engine base) and its top at crest_z. It is 3.6 m along the line, so it also retains the up-ramp and
    # run-out beside it (its -X/+X faces stand above the ramps as retaining walls). The crest drops its +Y face, which is
    # buried inside the bay.
    bay = (3.6, 2.6, crest_z)
    bay_x, bay_y = 0.1, mound_w / 2.0 + bay[1] / 2.0
    parts.append((cs2_kit.add_box("liftiebay", bay, (bay_x, bay_y, crest_z / 2.0),
                                  open_bottom=True), QUAD_CONCRETE))

    def z_rampup(x):
        return crest_z * (x + 4.6) / 3.5

    def z_rampdown(x):
        return crest_z * (2.6 - x) / 1.3

    # Stout center mast on a plinth. Liftblog img_6900 (the real Pali summit) shows one FAT clean tube, no gussets, with the
    # wheel right on top. So a thicker tube with a collar flange under the wheel bearing replaces the old thin mast+fin
    # cluster. Everything stays inside r=0.6 of center, the chairs' swing circle.
    parts.append((cs2_kit.add_box("plinth", (0.9, 0.9, 0.35), (0, 0, crest_z + 0.175),
                                  open_bottom=True), QUAD_CONCRETE))
    parts.append((add_cyl("mast", 0.42, wheel_z - 0.17 - crest_z,
                          (0, 0, crest_z + (wheel_z - 0.17 - crest_z) / 2.0),
                          segments=14), QUAD_PAINT))
    parts.append((add_cyl("mastcollar", 0.52, 0.18,
                          (0, 0, wheel_z - 0.38), segments=14), QUAD_PAINT))
    # L kit: a bolted base flange on the plinth (real studs, ground level) and a plain collar flange (painted bolt circle)
    # under the wheel bearing. Both are inside r 0.6.
    parts.extend(cs2_kit.flange("mastbase", 0.58, 0.04, bolts=8, segs=16,
                                loc=(0, 0, crest_z + 0.35 + 0.02), axis="Z",
                                bolt_geo=True, caps="top"))
    parts.extend(cs2_kit.flange("mastcollar", 0.58, 0.03, bolts=12, segs=16,
                                loc=(0, 0, wheel_z - 0.38 + 0.09 + 0.015), axis="Z",
                                bolt_geo=False))

    # The fixed bullwheel at the breakover is spoked, because from the unload you look straight up through it (img_6900 shows
    # the light spoke web against the dark rim).
    parts.extend(build_spoked_wheel(0, 0, wheel_z, prefix="t"))

    # J-generation: the built-in entry tower and its droop-anchor ropes are GONE. A hosted sub-object tower
    # (SkiLiftTowerET62, same train geometry) stands at px = -4.9 prefab-side and carries the cable anchor. Only the interior
    # static rope remains ours. It starts exactly where the trailing sheaves of that tower meet the line (px - cant + 0.75 at
    # axle_z + 0.12). See the drive-side comment.
    px = -4.9                               # hosted tower base (prefab-side contract)
    tp = lift_tower.TowerParams(mast_height=6.2)
    axle_z = tp.sheave_axle_height          # 5.24. HARD INVARIANT, the anchor height
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

    # L: NO baked chairs. The live chairs (ChairPinSystem, DECISIONS 103) dive from the entry tower, go around this wheel and
    # climb back out on exactly the rope modeled above. A static chair here duplicated a moving one and faced against travel
    # on its side.

    # L kit: guard rails along both knoll edges (h 1.10), from the up-ramp to the run-out, 0.1 m inside the mound edge so
    # posts stand on concrete. Names: post_knoll_*, rail_knoll_*_top/_knee, kick_knoll_*.
    ry_ = -(mound_w / 2.0 - 0.1)
    path = [(-4.0, ry_, z_rampup(-4.0)), (-1.1, ry_, crest_z),
            (1.3, ry_, crest_z), (2.3, ry_, z_rampdown(2.3))]
    parts.extend(cs2_kit.handrail("knoll_-1", path, h=1.10, knee=0.55, kick=0.10,
                                  post_every=1.5, segs=6, radius=0.028))
    # +Y side: the knoll rail runs up the ramp to the -X wall of the bay. A second rail then wraps the three outer edges of
    # the bay (0.1 m inside them) at crest height.
    ry_ = mound_w / 2.0 - 0.1
    bx0, bx1 = bay_x - bay[0] / 2.0, bay_x + bay[0] / 2.0
    parts.extend(cs2_kit.handrail("knoll_1", [(-4.0, ry_, z_rampup(-4.0)),
                                              (bx0 - 0.05, ry_, z_rampup(bx0 - 0.05))],
                                  h=1.10, knee=0.55, kick=0.10, post_every=1.5, segs=6,
                                  radius=0.028))
    by1 = bay_y + bay[1] / 2.0
    parts.extend(cs2_kit.handrail("bay", [(bx0 + 0.1, mound_w / 2.0 + 0.05, crest_z),
                                          (bx0 + 0.1, by1 - 0.1, crest_z),
                                          (bx1 - 0.1, by1 - 0.1, crest_z),
                                          (bx1 - 0.1, mound_w / 2.0 + 0.05, crest_z)],
                                  h=1.10, knee=0.55, kick=0.10, post_every=1.5, segs=6,
                                  radius=0.028))

    # Liftie box ON the bay, level with the crest. Glazing faces the unload (-Y), the arriving line (-X) and the back (+Y).
    # The door is on the downhill end wall (+X), so no pane faces a wall. The slab drops its bottom face (buried in the bay
    # top).
    hut = (1.8, 1.6, 2.3)
    hx, hy = bay_x, mound_w / 2.0 + 0.3 + hut[1] / 2.0
    hz0 = crest_z + 0.15                     # hut floor = bay top + slab
    parts.append((cs2_kit.add_box("hutbase", (hut[0] + 0.3, hut[1] + 0.3, 0.15),
                                  (hx, hy, crest_z + 0.075), drop=("-Z",)), QUAD_CONCRETE))
    parts.append((add_box("hut", hut, (hx, hy, hz0 + hut[2] / 2.0)), QUAD_PAINT))
    # Glass band leaves the main mesh as the `_Win` sub-mesh (main() splits it off). The +X (door) pane is dropped.
    parts.append((cs2_kit.add_box("hutwin", (hut[0] + 0.05, hut[1] + 0.05, 0.7),
                                  (hx, hy, hz0 + hut[2] * 0.62), drop=("+X",)), QUAD_GLASS))
    parts.extend(cs2_kit.door("hut", w=0.85, h=1.95, reveal=0.04,
                              loc=(hx + hut[0] / 2.0, hy - 0.2, hz0), facing="+X",
                              quad=QUAD_PAINT, quad_frame=QUAD_GALV))
    hutroof = add_box("hutroof", (hut[0] + 0.2, hut[1] + 0.2, 0.08),
                      (hx, hy, hz0 + hut[2] + 0.04))
    parts.append((hutroof, QUAD_GALV))
    parts.extend(cs2_kit.drip_edge(hutroof, drop=0.06, t=0.02))
    # Weather mast on the hut roof (img_6900: anemometer pole over the summit hut)
    wm_z = hz0 + hut[2] + 0.08
    parts.append((add_cyl("wxmast", 0.025, 1.3,
                          (hx - 0.55, hy - 0.45, wm_z + 0.65), segments=6), QUAD_GALV))
    parts.append((add_cyl("wxarm", 0.018, 0.36,
                          (hx - 0.55, hy - 0.45, wm_z + 1.24),
                          rotation=(math.radians(90), 0, 0), segments=6), QUAD_GALV))
    parts.append((add_box("wxbox", (0.09, 0.09, 0.14),
                          (hx - 0.55, hy - 0.45, wm_z + 1.36)), QUAD_GALV))

    return _assemble(parts, finish)


def _assemble(parts, finish):
    """
    Shared tail of both builders (L): bevels -> density weights -> part tags -> quadrant unwrap -> join -> remove_doubles ->
    part table -> shade -> base pivot -> orient_for_net. Parts with quad None (the tension-scale plates) keep their hand-set
    UVs.
    """
    if finish:
        apply_finish_bevels(parts)
    cs2_uv.set_density(parts, DENSITY_WEIGHTS)   # float FACE attr, survives the join
    table = cs2_asset.tag_parts(parts)           # after bevels, before the join (L)
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
    cs2_asset.store_part_table(t, table)
    cs2_asset.shade_by_angle(t, SHADE_ANGLE)    # L: smooth tubes, crisp boxes
    bpy.context.scene.cursor.location = (0.0, 0.0, 0.0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    cs2_asset.orient_for_net(t)
    n_kit = sum(1 for n in table if n.startswith(cs2_kit.PREFIXES))
    print(f"[terminal] assembled {len(table)} parts ({n_kit} kit parts), "
          f"{cs2_asset.tri_count(t)} tris")
    return t


def build_terminal_pali(p: TerminalParams, kind: str, finish=False):
    """
    Drive terminal after the Pallavicini reference photos (liftblog img_6828/6866/6870): a LOW, LONG, HEAVY machine.

    - Twin fat tube rails (round end caps, box end members) ride four fat column legs.
    - The bullwheel hangs from a tension CARRIAGE that rides those rails.
    - The machinery box sits directly over the wheel. The windowed operator cabin sits on top toward the back (img_6828:
      corrugated box, wrap-around glass, catwalk with railing, stovepipe, ladder up the back).
    - The haul rope dives from the hosted entry tower (a prefab-side sub-object since J, see the comment at px below). It
      passes UNDER depression-sheave banks hung from the front cross beam, with rope contact at the sheave BOTTOM, exactly as
      img_6866 shows. Then it runs to the wheel tangent and wraps.
    - The counterweight (img_6867: fat black cylinder drum) hangs under the rails behind the carriage, cabled over the back
      end member. This is the "tension" in Yan drive-tension.
    - A white 1..16 tension scale is painted along both rails (img_6828), via stamp_tension_scale + set_plate_uvs.

    Line side is -X (maps to CS2 -Z, where the connection stub of the building sits).
    """
    drive = (kind == "drive")
    if not drive:
        return build_return_simple(p, finish=finish)
    parts = []

    rope_y = 1.8                     # lane offset, as everywhere
    wheel_x, wheel_z = 0.2, 3.5      # bullwheel center - loading height (INVARIANT)
    rail_len = 10.0                  # frame front at -5.0, back at +5.0
    rail_y, rail_r, rail_z = 1.25, 0.30, 4.95
    leg_y = 3.35                     # legs fully outside the chair corridor (y 0.6..3.0)
    front_x, back_x = -4.3, 4.0      # portal (leg pair) planes
    deck_z = rail_z + 0.35           # top-of-rail deck the boxes stand on
    pad = (11.0, 7.6, 0.3)

    # L: OPEN-BOTTOM pad with its side-face bottom edges exactly at z = 0. The ENGINE builds the foundation skirt from them
    # (module docstring). The K skirt box is gone.
    parts.append((cs2_kit.add_box("pad", pad, (0, 0, pad[2] / 2.0), open_bottom=True),
                  QUAD_CONCRETE))

    # FOUR fat tube legs (img_6870: ~0.6 m round columns, not lattice), one pair per portal plane, standing outside the
    # corridor. A transverse box beam ties each pair, and its top carries the rails. Saddle blocks make the rail/beam joints
    # read heavy instead of tangent. L kit: bolted base flanges (8 real studs each, ground level) replace the legfoot boxes.
    # Twin gusset webs tie every leg to its beam.
    beam_z = 4.55                    # beam center, beam spans z 4.35..4.75
    for lx in (front_x, back_x):
        for ly in (-leg_y, leg_y):
            parts.append((add_cyl(f"leg_{lx:.0f}_{ly:.0f}", 0.30, beam_z - 0.2 - pad[2],
                                  (lx, ly, pad[2] + (beam_z - 0.2 - pad[2]) / 2.0),
                                  segments=14), QUAD_PAINT))
            parts.extend(cs2_kit.flange(f"leg_{lx:.0f}_{ly:.0f}", 0.46, 0.04, bolts=8,
                                        segs=16, loc=(lx, ly, pad[2] + 0.02), axis="Z",
                                        bolt_geo=True, caps="top"))
            sgn = 1.0 if ly > 0 else -1.0
            for k, gx in enumerate((-0.15, 0.15)):
                # web in the YZ plane: local +X -> inboard (-sgn Y), local +Z -> down
                parts.extend(cs2_kit.gusset(f"leg_{lx:.0f}_{ly:.0f}_{k}", 0.55, 0.55, 0.02,
                                            loc=(lx + gx, ly - sgn * 0.25, beam_z - 0.2),
                                            rot=(math.pi, 0.0, -sgn * math.pi / 2.0)))
        parts.append((add_box(f"beam_{lx:.0f}", (0.5, leg_y * 2 + 0.7, 0.4),
                              (lx, 0, beam_z)), QUAD_PAINT))
        for ry in (-rail_y, rail_y):
            parts.append((add_box(f"saddle_{lx:.0f}_{ry:.0f}", (0.6, 0.44, 0.3),
                                  (lx, ry, beam_z + 0.25)), QUAD_PAINT))

    # Twin FAT tube rails, the low long chassis that IS the Pali silhouette. They have proud round end caps (img_6870: open
    # tube ends with cap flanges). Box end members tie the pair together at both ends. Small lifting lugs on top at the ends
    # come straight off img_6828.
    for ry in (-rail_y, rail_y):
        parts.append((add_cyl(f"rail_{ry:.0f}", rail_r, rail_len, (0, ry, rail_z),
                              segments=16, rotation=(0, math.radians(90), 0)), QUAD_PAINT))
        # L kit: welded stiffener rings. The painted tension scale occupies x -0.3..3.7 and the back saddle / end member
        # occupy 3.7..4.8. So the rings sit on the front span and in the gap before the end member (3 per rail).
        for i, sx in enumerate((-3.6, -2.0, 4.36)):
            parts.extend(cs2_kit.stiffener_ring(f"rail_{ry:.0f}_{i}", rail_r, w=0.06, t=0.02,
                                                loc=(sx, ry, rail_z), axis="X", segs=16))
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

    # Tension carriage: two saddles ride the rails, a yoke hangs between them, and the fat wheel shaft drops to the
    # bullwheel. On the real machine this whole trolley slides along the rails as rope tension changes. The painted scale
    # (below) measures that.
    parts.append((add_box("carr_yoke", (0.6, rail_y * 2, 0.35),
                          (wheel_x, 0, 4.45)), QUAD_PAINT))
    for ry in (-rail_y, rail_y):
        parts.append((add_box(f"carr_saddle_{ry:.0f}", (1.7, 0.42, 0.55),
                              (wheel_x, ry, rail_z - 0.05)), QUAD_PAINT))
    parts.append((add_cyl("wheelshaft", 0.17, 4.45 - wheel_z + 0.2,
                          (wheel_x, 0, (4.45 + wheel_z) / 2.0 + 0.1), segments=10), QUAD_PAINT))

    # Spoked bullwheel (see build_spoked_wheel, liftblog img_6900) + haul rope torus seated in its groove. Rim outer edge
    # r=1.80, rope torus 1.82: INVARIANT.
    parts.extend(build_spoked_wheel(wheel_x, 0, wheel_z))
    bpy.ops.mesh.primitive_torus_add(major_radius=1.82, minor_radius=0.055,
                                     major_segments=28, minor_segments=6,
                                     location=(wheel_x, 0, wheel_z))
    _torus = bpy.context.active_object
    _torus.name = "wheelrope"
    parts.append((_torus, QUAD_PAINT))

    # Counterweight drum under the rails behind the carriage (img_6867: a fat black cylinder on cables). Cables run from the
    # carriage yoke back along the frame, over the back end member and down to the drum. The nearest point of the drum sits
    # 3.3 m from the wheel center, outside the chairs' swing annulus around the wrap (max 2.9 m).
    cw_x, cw_top = 4.2, 2.85
    parts.append((add_cyl("cweight", 0.72, 1.5, (cw_x, 0, cw_top - 0.75),
                          segments=16), QUAD_PAINT))
    parts.append((add_box("cwplate", (0.9, 0.9, 0.10), (cw_x, 0, cw_top + 0.05)), QUAD_PAINT))
    for s in (-1, 1):
        a0, a1 = (wheel_x + 0.35, s * 0.5, 4.55), (4.42, s * 0.5, 4.80)
        b0, b1 = (4.60, s * 0.45, 4.80), (cw_x + 0.05, s * 0.45, cw_top)
        parts.append((add_strut(f"tcable_a_{s}", a0, a1, 0.030), QUAD_PAINT))
        parts.append((add_strut(f"tcable_b_{s}", b0, b1, 0.030), QUAD_PAINT))
        # L kit: two-piece cable clamps at the yoke end and at the drum end (local X along the cable), 0.10 m in from the
        # cable ends.
        for tag, p0, p1, at_start in (("a", a0, a1, True), ("b", b0, b1, False)):
            d = mathutils.Vector(p1) - mathutils.Vector(p0)
            u = d.normalized()
            c = mathutils.Vector(p0) + u * 0.10 if at_start else mathutils.Vector(p1) - u * 0.10
            parts.extend(cs2_kit.cable_clamp(f"tc{tag}_{s}", 0.030, loc=tuple(c),
                                             rot=u.to_track_quat("X", "Z").to_euler()))

    # J-generation: the built-in ENTRY TOWER is GONE. A hosted sub-object tower (the standalone SkiLiftTowerET68 asset, same
    # sheave-train geometry) now stands at px = -6.9 prefab-side. It also carries the cable anchor of the game. So all of the
    # F/G/I etpad/etmast/etarm/cantilever/droop-anchor mesh left this builder.
    #
    # What REMAINS ours is the interior static rope. Its start point is untouched: it still begins exactly where the trailing
    # sheaves of that tower meet the line (d_x0 = px - cant + 0.75 at axle_z + 0.12). So the rope lands in the wheels of the
    # hosted tower, and neither side knows about the other.
    px = -6.9                               # hosted tower base (prefab-side contract)
    tp = lift_tower.TowerParams(mast_height=6.8)
    axle_z = tp.sheave_axle_height          # 5.84. HARD INVARIANT, the anchor height
    cant = 1.0                              # its trains hang 1.0 m line-ward (G, slope fix)

    # Two-angle rope, the way img_6866 actually shows it. From the entry tower the rope DIVES steeply to a depression-sheave
    # bank hung from the UNDERSIDE of the front cross beam (rope contact at sheave BOTTOM, because the sheaves press the rope
    # down). Then it runs nearly level to the bullwheel tangent. Angled guard plates flank each bank.
    bank_z = 4.28                    # sheave axle height, rope center below at 4.06
    rope_at_bank = bank_z - 0.17 - 0.05
    tan_x, tan_z = wheel_x - 0.1, wheel_z + 0.05
    d_x0, d_z0 = px - cant + 0.75, axle_z + 0.12
    for ry in (-rope_y, rope_y):
        parts.append((add_box(f"bankmount_{ry:.0f}", (0.55, 0.14, 0.35),
                              (front_x, ry, bank_z + 0.28)), QUAD_PAINT))
        for k, sxo in enumerate((-0.2, 0.2)):
            # L kit: groove-profile sheaves (sheave_bank_<ry>_<k>_rim / _hub), axle on Y
            parts.extend(cs2_kit.sheave(f"bank_{ry:.0f}_{k}", 0.17, 0.13,
                                        (front_x + sxo, ry, bank_z), "Y", segs=12))
        guard = add_box(f"ropeguard_{ry:.0f}", (0.75, 0.04, 0.26),
                        (front_x, ry + math.copysign(0.13, ry), bank_z + 0.02))
        guard.rotation_euler = (0, math.radians(25), 0)
        parts.append((guard, QUAD_PAINT))
        # segment A: entry-tower axle down to the front of the bank (steep, the Pali dive really is ~50 degrees, img_6866)
        parts.append((add_strut(f"divropea_{ry:.0f}", (d_x0, ry, d_z0),
                                (front_x - 0.22, ry, rope_at_bank + 0.01), 0.055), QUAD_PAINT))
        # segment B: under the bank, nearly level, out to the wheel tangent
        parts.append((add_strut(f"divropeb_{ry:.0f}", (front_x - 0.24, ry, rope_at_bank),
                                (tan_x, ry, tan_z), 0.055), QUAD_PAINT))

    # L: NO baked chairs at the load point or under the frame. The live chairs (ChairPinSystem, DECISIONS 103) ride segment
    # A, the bank, segment B and the wheel wrap. The K-generation static pair duplicated them and faced against travel.

    # -------- topside: machinery box over the wheel, operator cabin at the back -------
    # Machinery box directly above the carriage/wheel. The drive machinery drives the wheel shaft: vents + hatch, no glazing,
    # locked design decision. Floor plates close the gap down to the rails.
    box = (3.2, 2.4, 1.9)
    bx = 0.3
    parts.append((add_box("machfloor", (box[0] + 0.1, box[1] + 0.1, 0.12),
                          (bx, 0, deck_z - 0.06)), QUAD_PAINT))
    parts.append((add_box("machinebox", box, (bx, 0, deck_z + box[2] / 2.0)), QUAD_PAINT))
    # L kit: a louvered access hatch on the line-side (-X) wall replaces the hatch slab. A wide louvered vent hatch on the +X
    # wall replaces the three vent slats. Drip edge flashing runs around the roof box.
    parts.extend(cs2_kit.hatch("mach", 0.8, 1.3, reveal=0.03,
                               loc=(bx - box[0] / 2, 0.3, deck_z + 0.75), facing="-X"))
    parts.extend(cs2_kit.hatch("vent", 1.2, 0.6, reveal=0.03,
                               loc=(bx + box[0] / 2, 0, deck_z + 0.9), facing="+X"))
    boxroof = add_box("boxroof", (box[0] + 0.25, box[1] + 0.25, 0.09),
                      (bx, 0, deck_z + box[2] + 0.045))
    parts.append((boxroof, QUAD_GALV))
    parts.extend(cs2_kit.drip_edge(boxroof, drop=0.06, t=0.02))

    # Operator cabin ON the frame toward the back (img_6828/6870: corrugated black box, wrap-around glass band, roof
    # overhang, stovepipe), in ADDITION to the ground hut.
    cab = (2.2, 2.5, 2.05)
    cx = 3.4
    parts.append((add_box("cabfloor", (cab[0] + 0.1, cab[1] + 0.1, 0.12),
                          (cx, 0, deck_z - 0.06)), QUAD_PAINT))
    parts.append((add_box("cabin", cab, (cx, 0, deck_z + cab[2] / 2.0)), QUAD_PAINT))
    cab_band_z = deck_z + cab[2] * 0.62
    # Wrap-around glass band: leaves the main mesh as the `_Win` sub-mesh (main() splits it off, room cell (3, 2)). The
    # corner mullions stay in the main mesh.
    parts.append((cs2_kit.add_box("cabwin", (cab[0] + 0.05, cab[1] + 0.05, 0.72),
                                  (cx, 0, cab_band_z)), QUAD_GLASS))
    for mx, my in ((-1, -1), (-1, 1), (1, -1), (1, 1)):
        parts.append((add_box(f"cabmull_{mx}{my}", (0.07, 0.07, 0.72),
                              (cx + mx * cab[0] / 2.0, my * cab[1] / 2.0, cab_band_z)),
                      QUAD_GALV))
    cabroof = add_box("cabroof", (cab[0] + 0.24, cab[1] + 0.24, 0.09),
                      (cx, 0, deck_z + cab[2] + 0.045))
    parts.append((cabroof, QUAD_GALV))
    parts.extend(cs2_kit.drip_edge(cabroof, drop=0.06, t=0.02))
    parts.append((add_cyl("stovepipe", 0.055, 0.85,
                          (cx - 0.85, -0.85, deck_z + cab[2] + 0.5), segments=8), QUAD_GALV))

    # Catwalk along the +Y side of the deck (img_6828: the operator walks the frame between ladder and cabin). L kit: opaque
    # painted grating (holes in the normal map, because a see-through cutout would flip the whole surface to DefaultCutout)
    # and a proper guard rail (h 1.10, knee rail, kick plate) on the outboard edge.
    parts.extend(cs2_kit.grating("catwalk", 6.8, 0.55, t=0.06, loc=(1.6, 1.73, deck_z - 0.03)))
    parts.extend(cs2_kit.handrail("catwalk", [(-1.8, 1.95, deck_z), (5.0, 1.95, deck_z)],
                                  h=1.10, knee=0.55, kick=0.10, post_every=1.5, segs=6,
                                  radius=0.028))

    # Ladder up the back end to the deck (img_6828: straight ladder to the cabin)
    lad_x, lad_top = rail_len / 2 + 0.25, deck_z + 0.25
    for ly in (-0.25, 0.25):
        # L: the rails stand ON the pad (z 0.3), bottom cap buried. A rail that started at z = 0 put a face into the ground
        # band of the engine and BLOCKED the base there.
        parts.append((cs2_kit.add_cyl(f"lrail_{ly}", 0.035, lad_top - pad[2],
                                      (lad_x, ly, (lad_top + pad[2]) / 2.0), segments=6,
                                      caps="top"), QUAD_GALV))
    for i in range(1, 10):
        parts.append((add_cyl(f"lrung_{i}", 0.024, 0.5,
                              (lad_x, 0, i * lad_top / 10.0),
                              rotation=(math.radians(90), 0, 0), segments=6), QUAD_GALV))

    # Ground-level glazed liftie hut off to the side (stays, locked decision), plus the electrical cabinet on the pad.
    hut = (1.8, 1.6, 2.3)
    hx, hy = -1.2, pad[1] / 2.0 + hut[1] / 2.0 + 0.55
    # L: OPEN-BOTTOM base with its edges at z = 0 (engine base). The K hut skirt is gone.
    parts.append((cs2_kit.add_box("hutbase", (hut[0] + 0.3, hut[1] + 0.3, 0.15),
                                  (hx, hy, 0.075), open_bottom=True), QUAD_CONCRETE))
    parts.append((add_box("hut", hut, (hx, hy, 0.15 + hut[2] / 2.0)), QUAD_PAINT))
    # Glass band leaves the main mesh as the `_Win` sub-mesh (room cell (2, 1)). The -Y (pad-facing) pane is dropped to make
    # room for the door.
    parts.append((cs2_kit.add_box("hutwin", (hut[0] + 0.05, hut[1] + 0.05, 0.7),
                                  (hx, hy, 0.15 + hut[2] * 0.62), drop=("-Y",)), QUAD_GLASS))
    parts.extend(cs2_kit.door("hut", w=0.85, h=1.95, reveal=0.04,
                              loc=(hx + 0.3, hy - hut[1] / 2.0, 0.15), facing="-Y",
                              quad=QUAD_PAINT, quad_frame=QUAD_GALV))
    hutroof = add_box("hutroof", (hut[0] + 0.2, hut[1] + 0.2, 0.08),
                      (hx, hy, 0.15 + hut[2] + 0.04))
    parts.append((hutroof, QUAD_GALV))
    parts.extend(cs2_kit.drip_edge(hutroof, drop=0.06, t=0.02))
    parts.append((add_box("cabinet", (0.9, 0.5, 1.4),
                          (-3.2, -pad[1] / 2.0 + 0.55, pad[2] + 0.7)), QUAD_GALV))

    # The painted 1..16 tension scale on the outboard face of each rail (img_6828). The UVs are hand-set onto the stamped
    # atlas strip, so these two plates skip the normal quadrant unwrap (quad None).
    for s, asc in ((-1, True), (1, False)):
        plate = add_box(f"scale_{s}", (4.0, 0.024, 0.34),
                        (1.7, s * (rail_y + rail_r + 0.012), rail_z))
        set_plate_uvs(plate, outboard=s, ascending=asc)
        parts.append((plate, None))

    return _assemble(parts, finish)


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
        # haunches: short diagonal braces where columns meet the housing
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
    # A thin roof cap with a slight overhang sheds the boxy look
    parts.append((add_box("roofcap", (housing[0] + 0.3, housing[1] + 0.3, 0.1),
                          (0, 0, housing_z0 + housing[2] + 0.05)), QUAD_GALV))

    # Liftie box: every lift has one at each end. Walls, wrap-around window band, roof.
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
    Three framed views, using the camera helpers of lift_tower, with the atlas as material.

    `hut_target` (post-orient x, y, z, optional) adds a hut_closeup view aimed at the liftie control box: the K acceptance
    view for its new below-grade skirt.
    """
    scene = bpy.context.scene
    scene.render.engine = lift_tower.pick_engine()
    scene.render.image_settings.file_format = "PNG"

    world = bpy.data.worlds.new("PreviewWorld")
    world.use_nodes = True
    # Dark world like the tower previews. A bright sky here acts as shadowless ambient flood and washes the charcoal steel to
    # pale gray, which misrepresents the atlas.
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.11, 0.13, 0.16, 1.0)
    scene.world = world

    bpy.ops.object.camera_add(location=(0, 0, 0))
    cam = bpy.context.active_object
    cam.data.lens = 50
    scene.camera = cam

    # Key/fill a notch above the tower previews (4.0/1.4). This is ALL-BLACK steel, and at the tower energies the whole
    # machine collapsed into one silhouette.
    bpy.ops.object.light_add(type="SUN", location=(10, -12, 20))
    key = bpy.context.active_object
    key.data.energy = 4.5
    key.rotation_euler = (math.radians(50), 0, math.radians(35))
    bpy.ops.object.light_add(type="SUN", location=(-12, 8, 12))
    fill = bpy.context.active_object
    fill.data.energy = 1.8
    fill.rotation_euler = (math.radians(65), 0, math.radians(215))

    # The object is already net-oriented when previews render (orient_for_net ran at the end of the build). So pre-rotation
    # -X (the line side) is +Y here, and the catwalk side (+Y pre-rotation) is +X.
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
            # The liftie control box with its whole foundation column in frame: hut (z to ~2.6) down through the 3 m
            # below-grade skirt (z -3). So the K skirt fix is verifiable (no ground plane in previews).
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


# ======================================================================================
# GENERATION M (2026-10-11): the Pali stations on the v2 kit. Contract:
# docs/research/art/assets-2026-10/design/fg-stations-m.md with every MUST-CHANGE of design/fg-stations-m-skeptic.md applied.
# Build report: build/fg-stations-m.md.
# ======================================================================================
# FRAMES (design claim S1, skeptic-confirmed from the K importer bounds, unverified in game). Authoring: x travel (-x = line
# side), y lateral, z up, origin = station origin on the ground. The FBX holds (y, z, x) of authoring (orient_for_net, then
# the export bake). The game importer negates X. So prefab-local GAME = (gx, gy, gz) = (-y, z, x) and authoring = (gz, -gx,
# gy). Every game-frame number this section writes (prefab contract, vanilla rows, bounds) goes through m_to_game /
# m_from_game. Never use cs2_liftdress.blender_to_game (skeptic MC3).
#
# Library reuse, never copied:
# - clear_scene / apply_finish_bevels / split_windows / make_post_compose of this module (keyword tables, defaults unchanged
#   for the detach builder).
# - lift_terminal_detach (lazy): sign_layout, paint_sign, sign_relief, digit_triangles, check_sign_uv, check_label_uv,
#   SteppedWeather, density_classes, ctl_8bit_exclusive, emissive_stats.
# - cs2_liftdress: dressing, labels, weathering spec, vanilla GUIDs.
# - tram_station (lazy): the C14 clash test and the C15 house numbers / parsers, through a table override.
#
# Fine-detail pass, step 3c (2026-10-12, build/fine-detail-step3c-tram-pali.md): FINE_DETAIL_M (CLI --no-fine = the approved
# stations byte for byte). The e-stops, extinguishers, scraper + shovel and the base ski rack are dressing-kit "mod" rows in
# the own -y frame of M (fd_kit_specs_m / fd_kit_rows_m, M_X_CONVENTION). F1 (check_kit_rows_m -> cs2_kitrows) runs on the
# real kit meshes. M3 / M7 / M18 count them (cs2_kitrows.augment / probe_object). The pass also adds a fine maze, gate and
# post boards, and a lid lip on the base cabinet. The review pack draws the rows through review_rows_m (display only).
#
# Lift-board redraw (2026-10-12, build/lift-boards-redraw.md): the boards repaint in the real signage style
# (cs2_liftdress.paint_board, same rects / UVs). The TOP carries no STAY SEATED board any more (post, panel, label, atlas
# rect gone). That follows the nineteenth-look ruling on the DQ top, "We don't need the STAY SEATED at the top ... doesn't
# quite make sense". The ruling applies to the Pali top, whose STAY stood 2.55 m PAST the unload line. M19 = the DQ top's D11
# `forbid`: any board_stay* / label_stay* part on the top raises, whatever the mount (M_FORBID). Both FINE_DETAIL_M modes
# drop the board (the DQ precedent). --no-fine still reproduces the approved BASE geometry (FBX) byte for byte. Its textures
# differ by the repainted labels only.

import contextlib as _ctx
import dataclasses as _dc
import hashlib as _hashlib
import heapq as _heapq
import re as _re

from mathutils import Vector as _V

REPO_M = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
CARRIERS_JSON = os.path.join(REPO_M, "catalog", "carriers.json")
SKICATALOG_CS = os.path.join(REPO_M, "mod", "SkiResortMod", "Systems", "SkiCatalog.cs")
GAMEDATA_M = os.path.join(REPO_M, "gamedata-mirror", "ImportedData")
IMPORT_M = os.path.join(REPO_M, "assets", "import")
CHAIR_M_DIR = os.path.join(IMPORT_M, "chairs", "M", "SkiLiftChair6M")
FG_ROPE_LANE = "SkiFixedGripHaulRope"   # the FG stations' own haul-rope lane prefab (lift alignment F3, 2026-11-02)
GEN_M = "lift_terminal.py (generation M)"
FG_SEATS_DIGITS = "3"            # carriers.json tr seats (the plate number, as the DQ's "4")


@dataclass(frozen=True)
class MSpec:
    kind: str                # "drive" (base) | "return" (top)
    wheel: tuple             # authoring (x, y, z) = SkiCatalog WheelLocal (gz, -gx, gy)
    anchor_in: tuple         # (x, z) of the HV sub-lane inboard end (the chain end, DECISIONS 4)
    anchor_out: tuple        # (x, z) of the sub-lane's outboard end
    tower_x: float           # hosted end tower base, authoring x (= game z), rotation 180
    tower: str               # the hosted tower's mesh folder (towers/O)
    tower_prefab: str        # the hosted tower's gameplay prefab (repointed M6 / M7)
    building: str            # the deployed gameplay prefab whose m_Mesh is repointed
    k_mesh: str              # the deployed K mesh
    comm_y: tuple            # comm sub-lane y (a, d) game


M_SPECS = {
    "drive": MSpec("drive", (0.20, 0.0, 3.55), (-6.775, 5.87), (-7.025, 5.84), -6.9,
                   "SkiLiftTowerET68O", "SkiLiftTerminalTowerBase", "SkiBaseStationBuilding",
                   "SkiLiftBaseStationK", (6.50, 6.53)),
    "return": MSpec("return", (0.0, 0.0, 3.75), (-4.775, 5.27), (-5.025, 5.24), -4.9,
                    "SkiLiftTowerET62O", "SkiLiftTerminalTowerTop", "SkiTopStationBuilding",
                    "SkiLiftTopStationK", (5.90, 5.93)),
}
# T27 ROPE ON THE CROWN (2026-10-17, docs/research/field/2026-10-15/build/T27-rope-path.md, lift_tower.ROPE_ON_CROWN).
#
# False (default) = the deployed K anchors: the HV sub-lane on the hosted ET68 / ET62 sheave AXLE (5.84 / 5.24), so the line
# rope runs through the wheels of the end tower. True (set_m_rope_on_crown, CLI --rope-on-crown) = both anchor ends on the ET
# train's CROWN (axle + lift_tower.crown_seat = + 0.278: the vanilla HV cable resting in the FG V-groove). The code chord
# (chord_z), the modeled ropechord_, the hold-down train and every sweep follow spec.anchor_in. The bullwheel (WheelLocal)
# does not move. M1 then checks the deployed prefab against M_SPECS_SHIPPED and reports the surgery rows.
M_ROPE_ON_CROWN = False
M_SPECS_SHIPPED = dict(M_SPECS)
M_HOSTED_VARIANT = {"drive": "ET68", "return": "ET62"}


def m_rope_seat(kind):
    """The anchor lift (m) of station `kind` under T27: lift_tower.crown_seat of its hosted end tower (support head)."""
    return lift_tower.crown_seat(lift_tower.VARIANTS[M_HOSTED_VARIANT[kind]])


def set_m_rope_on_crown(on):
    """Swap M_SPECS between the shipped anchors and the crown anchors (T27). Idempotent."""
    global M_ROPE_ON_CROWN
    M_ROPE_ON_CROWN = bool(on)
    for k, s in M_SPECS_SHIPPED.items():
        if on:
            d = m_rope_seat(k)
            M_SPECS[k] = _dc.replace(s, anchor_in=(s.anchor_in[0], round(s.anchor_in[1] + d, 4)),
                                     anchor_out=(s.anchor_out[0], round(s.anchor_out[1] + d, 4)))
        else:
            M_SPECS[k] = s


def m_rope_surgery(kind):
    """T27 surgery rows of station `kind` (game frame, the deployed prefab's HV ObjectSubLanes)."""
    s0, d = M_SPECS_SHIPPED[kind], m_rope_seat(kind)
    rows = []
    for side in (-1, 1):
        for pt, (x, z) in (("a", s0.anchor_out), ("d", s0.anchor_in)):
            rows.append({"field": f"{s0.building} ObjectSubLanes HV x {side * FG_LANE:+.1f} m_BezierCurve.{pt}.y",
                         "old": z, "new": round(z + d, 4)})
        rows.append({"field": f"{s0.building} ObjectSubLanes HV x {side * FG_LANE:+.1f} m_BezierCurve.b.y / .c.y",
                     "old": "a + 0.01 / a + 0.02", "new": "a + 0.01 / a + 0.02 (shift by the same +%.4f)" % d})
    return {"seat": d, "hosted": M_HOSTED_VARIANT[kind], "rows": rows,
            "unchanged": "WheelLocal, StubTipLocalZ (ObjectSubNets), the LV comm sub-lane, the hosted tower pose"}


# up lane = chain A = right looking uphill (StationAlign.UpLaneIsSecond, skeptic claim 6, conditional on S1). The base loads
# on authoring +y and the top unloads on authoring -y. ONE constant per station, so a field read can flip it (design section
# 7, S2).
UP_LANE_Y = {"drive": +1.0, "return": -1.0}
FG_LANE = 1.8                    # carriers tr lane_x_m
FG_HANG = 2.400                  # carriers tr hang_m (grip top = the code lane / path)
FG_GRIP_ABOVE = 0.080            # carriers tr grip_top_above_rope_m (only the rope-point pin uses it)
M_ROPE_R_SHIPPED = 0.055         # the shipped stations' modeled rope
# art-601 #10 (owner hand-test note 10, "really jarring"): the modeled station rope (ropechord_ / wheelrope, 0.11 m across)
# read 2-3x the line's vanilla cable beside it. --thin-rope builds with THIN_ROPE_R, 0.048 m across. The shipped letter O uses
# it (SHIPPED_FLAGS). The line cable's MESH is r 0.060 (rope-width-check.md), but the game draws it about 0.040 m across
# (hexagon r 0.020 = SkiRopeStillLVA Mesh m_MinSize 0.04, a dilated curve mesh; P1 2026-11-20 measured 3.03x for a 0.060 station
# rope and 1.24x for O's 0.024, critic-oc3; the shader is not decompiled). A station rope draws at its mesh width. The rig
# (RIG_ROPE_R) uses THIN_ROPE_R too. No chair or train contract depends on 0.048: every consumer of the radius (the axle of
# the hold trains on the rope's top, the rope parts, the checks) follows the one global. The manifest records the radius of
# the build (rope_r_m), and the seams tests read it per asset. LOD caveat: vanilla cables keep a minimum screen width at
# distance, but a modeled 0.048 m rope does not (judge the far preview).
THIN_ROPE_R = 0.024
M_ROPE_R = M_ROPE_R_SHIPPED       # this build's radius (main_m: --thin-rope)
M_TORUS_R = 1.80                 # rope torus = the code arc radius (LiftLoopCache r = lane)
M_RIM_OUT = 1.74                 # bullwheel rim outer (A4: r 1.80 rope proud in the groove)
CHAIR_BODY_Z = 2.10              # chair-local z below which the chair is BODY (the grip rides the rope)
CHAIR_CUSHION = 0.15             # cushion top above the chair's lowest point (6M probe)
M_CLEAR_STRUCT = 0.20            # M3 body -> structure
M_CLEAR_DRESS = 0.10             # M3 body -> dressing / rails / stakes / maze / boards
M_SEAT_WIN = (0.45, 0.55)        # M4 cushion top over the load / unload surface
M_PLANK_MIN = 0.30               # M4 plank over every walkable surface along the sweep
M_RAMP_MAX_DEG = 20.0            # FG rule (not in tram C15): a walking ramp is <= 20 deg
M_LOT_HALF = (12.0, 8.0)         # lot 2 x 3 = 16 x 24 m: authoring x half 12, y half 8
M_LOT_MARGIN = 0.25
M_SEAM_CLEAR = 0.10              # M5 hosted tower -> station face
M_SEAM_ROPE_EXEMPT = 1.0         # ropechord_ within this of the sub-lane end (rope in sheave)
LOD0_CAP_M = {"drive": 9000, "return": 3600}    # design 9,000 / skeptic: the top keeps 3,600
# T62: drive 9,000 -> 9,800 (round 2, 224-tri wheels) -> 9,000 again (round 3: 7,950 with the <= 120-tri wheels fits)
LOD1_RATIO_M = 0.45
LOD2_TARGET_M = {"drive": 500, "return": 400}
M_ATLAS = 2048
M_PACK_MARGIN_PX = 6
M_SHADE = 66.0
M_SMOKE_ATLAS = 512
# Reserved strip (top of the atlas, texels at 2048, x0 y0 x1 y1). It holds the sign panel at 448 px/m (detach SIGN_PX_PER_M,
# 1.750 x 0.875 m -> 784 x 392 px), the label sheet beside it (400 px/m, cs2_liftdress.pack_label_sheet), and one full-width
# band below for the tension scale (4.0 x 0.20 m at 400 px/m = 1,600 x 80 px). Both rail plates share the band, and each
# reads it from its side.
M_SIGN_PX = (16, 1648)                         # sign rect lower-left at 2048 (W x H from the layout)
M_LABEL_REGION_PX = (808, 1648, 2040, 2040)
M_LABEL_PPM = 400.0
M_LABEL_GUTTER_PX = 8
M_SCALE_PX = (16, 1560, 1616, 1640)            # 1,600 x 80
M_SCALE_LEN = 4.0
M_SCALE_H = 0.20
M_SCALE_PPM = 400.0
M_BADGE_EMB = 0.36                             # the badge's emblem square (X11: <= 0.40, square)
# Fine-detail pass, step 3c (2026-10-12, build/fine-detail-step3c-tram-pali.md, design/fine-detail-pass.md A.2 / B / D as cut
# by its skeptic FS1 / FS5 / FS6 / FS7).
#
# FINE_DETAIL_M False (CLI --no-fine, main_m sets it from its args every run) = the approved generation-M stations byte for
# byte.
#
# FINE_DETAIL_M True:
# - The e-stops, extinguishers, the scraper + shovel and the ski rack of the base leave the mesh as dressing-kit "mod" rows
#   (fd_kit_specs_m) in the OWN frame of M (game x = -authoring y, M_X_CONVENTION). The x-sign gate settled N on 2026-10-15:
#   the A family now matches.
# - The maze, swing gate and post boards take the fine geometry of the kit builders.
# - The base cabinet loses its finish bevel and gains a lid lip.
#
# Contract F1 (cs2_kitrows.check_kit_rows) tests every kit row on the REAL kit mesh. M3 (the chair sweep), M7 (crossings) and
# M18 (bay routes) count the kit meshes as dressing / obstacles.
#
# Stays modeled: the LIFT boards, the painted 1-16 tension scale, the badge and the sign (FS5: the host atlas), the weather
# mast of the top (FS1), the tension gauge, the maze / gate / stakes, the propane tank, the stop arm. Every lamp is already a
# vanilla row.
FINE_DETAIL_M = True
M_X_CONVENTION = ("game (gx, gy, gz) = authoring (-y, z, x) (lift_terminal.m_to_game, S1): gx = -authoring y; gx "
                  "NEGATED (x-sign gate settled N, 2026-10-15 playtest; the DQ / gondola / tram / building rows "
                  "follow it since S1b-N, 2026-10-16: cs2_liftdress.blender_to_game is the same map)")
#: Kit color variants (2026-10-12 ~18:50, build/kit-colour-variants.md).
#: The color set (fine_detail.COLOUR_SETS) of the colored kit rows per kind is yellow, the Yan black / yellow
#: (the stations' safety yellow, the yellow / black placards). The return station has no colored row.
FD_M_KIT_COLOUR = {"drive": "yellow", "return": "yellow"}
FD_M_WALL_RACK = "SkiDressSkiRackWallV2"       # the base's 3.0 m wall rack: loadout W2 (6 pairs + 1 board + 1 pole
#                                                pair) - the wall racks alternate by station (DQ base W1, gondola W2 / W1)
FD_M_LID_T, FD_M_LID_LIP = 0.02, 0.015         # the base cabinet's lid (the step-3b detach cabinets' lip)
# The base scraper + shovel: the foot-line rule of the kit (0.3925 m) puts the pair off the -y face of the cabinet, past the
# -y edge of the pad (y -3.85: the feet over the grade, F1 floor check). So they lean on the +x face of the cabinet instead
# (between the cabinet and the ski rack). "+X" = the outward normal of that face.
FD_M_BASE_TOOLS = "+X"

# ---------------------------------------------------------------- frames ------------------

def m_to_game(p):
    """Authoring (x, y, z) -> prefab-local game (gx, gy, gz) = (-y, z, x)  (S1)."""
    return (-float(p[1]), float(p[2]), float(p[0]))


def m_from_game(g):
    """Prefab-local game (gx, gy, gz) -> authoring (x, y, z) = (gz, -gx, gy)  (S1)."""
    return (float(g[2]), -float(g[0]), float(g[1]))


M_FACING_TO_GAME = {"+X": "+Z", "-X": "-Z", "+Y": "-X", "-Y": "+X"}   # S1: the lateral is mirrored


def m_facing_quat(facing):
    """Yaw quat (x, y, z, w) that turns the local +Z of a vanilla prop to an AUTHORING facing, through the S1 map (+Y authoring
    = -X game). cs2_liftdress.FACING_QUAT holds the game quats."""
    import cs2_liftdress as LD
    return LD.FACING_QUAT[M_FACING_TO_GAME[facing]]


def m_subobj_box(prefab, pos_auth, facing, pad=0.0):
    """The variant-UNION box of a vanilla prop (cs2_liftdress.SUBOBJ bounds), placed in the AUTHORING frame through the own map
    of M (skeptic MC3: not subobj_box(frame="blender"))."""
    import cs2_liftdress as LD
    rot = m_facing_quat(facing) if facing else (0.0, 0.0, 0.0, 1.0)
    glo, ghi = LD.subobj_box(prefab, m_to_game(pos_auth), rot, frame="game", pad=pad)
    corners = [m_from_game((x, y, z)) for x in (glo[0], ghi[0]) for y in (glo[1], ghi[1])
               for z in (glo[2], ghi[2])]
    c = np.array(corners)
    return c.min(axis=0), c.max(axis=0)


# T17 lamp part (DECISIONS 247, 2026-10-16, build/T17-T23-T25-dressing.md): both FG station prefabs carry OUR
# SkiPisteFloodlightLow (8 m, 3 heads, 25-degree down-tilt baked in) where the generator used to emit the vanilla
# LightpoleParkRandom01. Same position, yaw 190 (beam toward the line, 10 degrees across the platform), parent 0
# (gamedata-mirror SkiBaseStationBuilding (3.4, 0.3, 5.6) / SkiTopStationBuilding (4.77, 0, 2.64: nudged 2026-10-17), quat
# below). It is a "mod" row (CID, no SUBOBJ placeholder: the DQ precedent lift_terminal_detach.d1_flood_rows). Its footprint
# for M12 / F1 is three prefab-local boxes (x, y up, z), measured on the shipped props/A FBX (T17 loading-room round,
# 2026-10-17): the 0.90 m square base plate (0.20 thick), the pole (r 0.094, + the 0.124 junction box) and the head bar
# (+-0.775).
M_FLOOD = dict(prefab="SkiPisteFloodlightLow", cid="6827e281427c087a3b7ef19c824ca28b",
               mesh="SkiPisteFloodlightLowA Mesh", rot=(0.0, 0.9961947, 0.0, -0.0871557),
               boxes=(((-0.45, 0.0, -0.45), (0.45, 0.20, 0.45)),
                      ((-0.094, 0.20, -0.094), (0.094, 8.00, 0.124)),
                      ((-0.775, 7.947, -0.312), (0.775, 8.373, 0.333))))


def m_row_boxes(prefab, pos_auth, facing):
    """[(lo, hi)] AUTHORING boxes of one subobjects_m row: the union box of a vanilla prop (m_subobj_box), or the three boxes of
    the M_FLOOD floodlight at its deployed yaw (the rotation of cs2_liftdress.subobj_box about game +Y)."""
    if prefab != M_FLOOD["prefab"]:
        return [m_subobj_box(prefab, pos_auth, facing)]
    x, y, z, w = M_FLOOD["rot"]
    a = 2.0 * math.atan2(y, w)
    c, s = math.cos(a), math.sin(a)
    g = np.array(m_to_game(pos_auth), dtype=np.float64)
    out = []
    for lo, hi in M_FLOOD["boxes"]:
        P = np.array([[px, py, pz] for px in (lo[0], hi[0]) for py in (lo[1], hi[1]) for pz in (lo[2], hi[2])])
        G = np.stack([P[:, 0] * c + P[:, 2] * s, P[:, 1], -P[:, 0] * s + P[:, 2] * c], axis=1) + g
        A = np.array([m_from_game(v) for v in G])
        out.append((A.min(axis=0), A.max(axis=0)))
    return out


# ---------------------------------------------------------------- materials ---------------

def _lin_m(rgb):
    return tuple(float(cs2_preview._srgb_to_linear(np.float32(c))) for c in rgb)


def materials_m():
    """The M material table (cs2_bake.MaterialSpec, rough = 1 - smooth). paint1 = the slot-1 recolor (slot 1 of ColorProperties
    on the K mesh prefab (0.25, 0.26, 0.275) = Pali black). signband = the same slot (the sign panel, the badge emblem and
    the tension-scale plates sit in reserved rects, one material per rect). The Pali logo / numbers are WHITE on the black
    steel, so the FG panel is slot 1, not slot 3 of the DQ (white by default here). galv / darksteel / rubber = the DQ
    definitions exactly (cs2_liftdress.merge_materials checks)."""
    import cs2_bake
    MS = cs2_bake.MaterialSpec
    grey = _lin_m((0.80, 0.80, 0.80))
    if TEXPASS_M:
        return texpass_materials_m(MS, grey)
    return {
        "paint1": MS(albedo=grey, metal=0.10, rough=0.55, slot=1, recolor_albedo=True,
                     projection="planar"),
        "signband": MS(albedo=grey, metal=0.10, rough=0.50, slot=1, recolor_albedo=True),
        "concrete": MS(texture_set="Concrete034", metres_per_tile=3.0, tint=0.58, metal=0.0,
                       rough=0.82),
        "darksteel": MS(albedo=cs2_asset.STEEL_DEFAULT_RGB, metal=0.2, rough=0.60),
        "galv": MS(texture_set="Metal032", metres_per_tile=2.0, metal=1.0, rough=0.65),
        "glass": MS(albedo=(0.07, 0.09, 0.11), metal=0.0, rough=0.08),
        # T27: the vanilla line cable (cs2_asset.ROPE_MATERIAL, was albedo (0.30, 0.30, 0.32) metal 0.9 rough 0.65)
        "rope": MS(**cs2_asset.ROPE_MATERIAL),
        "rubber": MS(albedo=(0.04, 0.04, 0.045), metal=0.0, rough=0.70),
        # weathered pine: the load-deck crib, the knoll stakes (PAL-03 / PAL-10)
        "timber": MS(albedo=_lin_m((0.50, 0.40, 0.29)), metal=0.0, rough=0.78),
    }


def materials_for_m(parts):
    import cs2_liftdress as LD
    import cs2_sheaves as S
    import cs2_bake
    keys = LD.part_keys(parts)
    # T62 round 2: adds the one wheel material under v2 ({} under v1)
    return {**LD.merge_materials(materials_m(), [k for k in keys if k in LD.DRESS_MATERIALS]),
            **S.wheel_materials(cs2_bake.MaterialSpec)}


def mat_roles_m():
    import cs2_liftdress as LD
    import cs2_sheaves as S
    if TEXPASS_M:
        return {**LD.DQ_MAT_ROLES, "timber": "timber", **TP1_ROLES, **S.wheel_roles()}
    return {**LD.DQ_MAT_ROLES, "timber": "timber", **S.wheel_roles()}


# ---------------------------------------------------------------- tables ------------------

M_BEVEL_PREFIXES = ("pad", "beam_", "saddle_", "endmember_", "carr_", "house", "hutbase", "hut",
                    "cabinet", "crest", "rampup", "rampdown", "liftiebay", "column", "colplate",
                    "bearingdeck_", "cwfoot", "loaddeck", "holdboom_", "cwmast")
M_BEVEL_EXCLUDE = ("hutwin", "cabwin", "signband", "scale_", "badge", "hutroofcap")
M_WIN_CELLS = {"drive": {"hutwin": (2, 1), "cabwin": (3, 2)}, "return": {"hutwin": (2, 1)}}
M_ENGINE_BASE = {"drive": ("pad", "hutbase", "cwfoot"),
                 "return": ("rampup", "crest", "rampdown", "liftiebay")}
# density weights (cs2_uv.set_density, longest prefix wins), merged with cs2_liftdress.DRESS_DENSITY
M_DENSITY = {
    "house": 1.6, "hut": 1.6, "hutbase": 0.6, "hutroof": 0.8, "houseroof": 0.8,
    "leg_": 1.4, "beam_": 1.4, "beamdrum_": 1.0, "rail_n": 1.4, "rail_p": 1.4, "column": 1.4,
    "cwmast": 1.2, "cwpot": 1.0, "endmember_": 1.0, "carr_": 1.0, "saddle_": 1.0,
    "pad": 0.7, "crest": 0.7, "rampup": 0.7, "rampdown": 0.7, "liftiebay": 0.8, "cwfoot": 0.5,
    "sheave_bull": 1.0, "spoke_": 0.6, "wheelhub": 0.6, "wheelrope": 0.8, "ropechord_": 0.8,
    "cwrope_": 0.6, "cwcable_": 0.6, "loaddeck": 0.9, "loadramp": 0.9, "stake_": 1.0,
    "stakewrap_": 0.8, "grate_": 0.6, "rail_catwalk": 0.6, "rail_bay": 0.6, "post_": 0.6,
    "kick_": 0.6, "ladder_": 0.6, "lrung_": 0.6, "wrung_": 0.5, "flange_": 0.8, "gusset_": 0.8,
    "stiff_": 0.8, "bolt_": 0.6, "railcap_": 0.8, "raillug_": 0.6, "wheeldisc_": 0.8,
    "sheave_hold": 0.8, "holdboom_": 0.8, "holdhang_": 0.6, "holdweb_": 0.8, "holdplate_": 0.6, "holdclamp_": 0.6, "sheave_cw": 0.8, "cwbrk_": 0.6,
    "exhaust": 0.6, "antenna": 0.5, "antwhip": 0.4, "winframe_": 0.6, "door_": 0.8,
    "doorlite_": 0.6, "hatch_": 0.8, "drip_": 0.5, "cabinet": 0.8, "bearingdeck_": 0.8,
    "bearing": 0.6, "colplate": 0.8, "wxmast": 0.5, "wxarm": 0.5, "wxcup": 0.5, "guy_": 0.4,
    "propane": 0.8, "stoparm": 0.6, "catbrk_": 0.5, "plate_cap": 1.0, "label_": 1.0,
    "signband_": 1.0, "scale_": 1.0, "badge": 1.0,
}


def detail_specs_m():
    """Relief per prefix (cs2_compose.detail_height, longest prefix wins, an empty DetailSpec excludes a longer name). The
    relief covers corrugated house / hut walls (0.076 m from L), bolted seams on the legs, beams, main rails, column and
    counterweight mast (bolt r 0.025, drawn at >= 40 px/m), the rope helix, the catwalk grating, pressed door leaves, and
    deck planks. There is no relief on the pinned / painted faces, the dressing or the glass."""
    D = cs2_compose.DetailSpec
    corr = dict(corrugation_m=0.076, corr_depth_m=0.002)
    steel = dict(seams_m=1.2, bolt_pitch_m=0.15, bolt_r_m=0.025)
    leaf = D(seams_m=0.30, seam_depth_m=0.003, seam_axes="rings")
    return {
        "house": D(**corr), "houseroof": D(), "hut": D(**corr), "hutbase": D(), "hutroof": D(),
        "hutwin": D(), "cabwin": D(),
        "leg_": D(seam_axes="rings", **steel), "beam_": D(**steel), "beamdrum_": D(),
        "rail_n": D(seam_axes="rings", **steel), "rail_p": D(seam_axes="rings", **steel),
        "column": D(seam_axes="rings", **steel), "cwmast": D(**steel),
        "ropechord_": D(helix_m=0.10), "wheelrope": D(helix_m=0.10), "cwrope_": D(helix_m=0.10),
        "cwcable_": D(helix_m=0.10),
        "grate_": D(grate_m=0.12), "grate_catwalk_lip": D(),
        "door_house_leaf": leaf, "door_hut_leaf": leaf,
        "loaddeck": D(seams_m=0.20, seam_axes="ribs", seam_depth_m=0.004),
        "loadramp": D(seams_m=0.20, seam_axes="ribs", seam_depth_m=0.004),
        "signband_": D(), "scale_": D(), "badge": D(), "label_": D(), "board_": D(),
        "stripe_": D(), "chev_": D(), "carpet_": D(), "doorlite_": D(), "winframe_": D(),
        "plate_cap": D(),
        **(texpass_detail_specs_m(D) if TEXPASS_M else {}),
    }


# ---------------------------------------------------------------- texture pass v1 ---------
# r4/tex (2026-11-19): `--texpass v1`, default off = every FBX and map byte for byte as before (the tables below are read
# only under the flag). The pass changes pixels, UV density weights and material response. It moves no vertex, renames no
# part and adds no triangle. What it does, per audit finding (docs: r4-fleet/tex/fg/NOTE.md):
# - TP1_TONE: the towers' detail-pass-1 tone layer (cs2_compose.detail_tone: paint mottle + chalk on up-facing paint, galv
#   bloom) reaches the stations, plus a damp-patch mottle on the concrete and a board tone on the timber. The stations
#   shipped flat paint, a featureless galv (Metal032 is one blue-gray tone) and a clean scan on 121 m2 of pad.
# - TP1_CAVITY: relief deeper than 2.5 mm darkens and dulls (grating holes, plank gaps, slab joints). Flag-off relief is
#   normal-map only, which does not read at play distance.
# - texpass_detail_specs_m: slab joints on the pad (3.85 m panels), standing seams on the two sheet roofs.
# - texpass_materials_m: satin structural paint (rough 0.55 -> 0.42); the galvanized steel leaves Metal032's blue-gray for
#   the light neutral zinc of the pass's towers (lift_tower.TEXPASS_MATERIALS, one galv in the family); the concrete takes
#   the towers' warm tint and roughness (the hosted end tower's pier stands on this pad).
# - TP1_DENSITY: texels move from the fat painted tubes to the surfaces the game camera sees from above (pad, roofs,
#   grating, load deck).
# IDENTITY_M ("shack", `--identity shack`, needs the pass): the station-identity proposal. The operator hut is clad in the
# lodges' stained plank siding (key hutclad, the lodge timber_lap scan at the lodges' 2.4 m tile = 17 cm boards; the scan
# holds 14 boards, so the 1.2 m tile of the first draft drew 8.6 cm boards, cs2_tilesheet.TEXPASS r4/tex a2f5552d) and the
# machine house leaves the structure's recolor slot for slot 2 (key hood, satin), so the machine carries the lift's own
# color. Material keys only.
TEXPASS_M = None            # None | "v1" (main_m sets it from the args before a part is built)
IDENTITY_M = None           # None | "shack"
TEXPASS_CHOICES = ("off", "v1")
IDENTITY_CHOICES = ("off", "shack")
TP1_ROUGH_PAINT = 0.42
TP1_ROUGH_HOOD = 0.32
TP1_GALV_TINT = (2.09, 1.93, 1.76)          # = lift_tower.TEXPASS_MATERIALS v1 FG galv: sRGB 0.70 / 0.715 / 0.73
TP1_CONCRETE = dict(tint=(0.661, 0.625, 0.547), rough=0.76)     # = lift_tower.TOWER_MATERIALS concrete
TP1_HUTCLAD = dict(texture_set="PHWeatheredPlankSiding", metres_per_tile=2.4, metal=0.0)
TP1_ROLES = {"hood": "paint_metal", "hutclad": "timber"}
TP1_REKEY = {"shack": {"hut": "hutclad", "house": "hood", "door_house_leaf": "hood"}}
# density weights (longest prefix wins) laid over M_DENSITY
TP1_DENSITY = {
    "pad": 1.0, "houseroof": 1.2, "hutroof": 1.2, "grate_": 0.9, "loaddeck": 1.2, "loadramp": 1.2,
    "hutbase": 0.8, "cwfoot": 0.7, "crest": 0.8, "rampup": 0.8, "rampdown": 0.8,
    "leg_": 1.15, "beam_": 1.15, "rail_n": 1.15, "rail_p": 1.15, "column": 1.2,
}
# detail_tone specs per MATERIAL key (cs2_compose.DetailSpec tone fields). paint / galv = lift_tower.DP1_TONE within 20 %
TP1_TONE = {
    "paint1": dict(mottle_m=0.60, mottle_amp=0.04, mottle_smooth=0.08, chalk_amp=0.05, chalk_smooth=0.10),
    "hood": dict(mottle_m=0.90, mottle_amp=0.03, mottle_smooth=0.06, chalk_amp=0.04, chalk_smooth=0.08),
    "galv": dict(mottle_m=0.15, mottle_stretch=2.5, mottle_amp=0.06, mottle_smooth=0.10),
    "concrete": dict(mottle_m=1.50, mottle_amp=0.07, mottle_smooth=0.04),
    "timber": dict(mottle_m=0.35, mottle_stretch=4.0, mottle_amp=0.07, mottle_smooth=0.04),
}
TP1_TONE_FLOOR_TEXELS = 4.4     # mottle cell >= this many texels (lift_tower.DP1_TONE_FLOOR_TEXELS)
TP1_CAVITY = dict(from_m=0.0025, span_m=0.0075, dark=0.40, dull=0.15)
TP1_JOINT_M = 3.85              # slab panel = the pad's half width: joints on the center line and at the edges
# weather overrides laid BEFORE FG_WEATHER (first match wins on an equal prefix), and two lower ladder steps: the pass moves
# texels to the walked pad, so the texel-weighted vanilla budget (VL1 / VL2) can WARN at the shipped first step 0.40
TP1_WEATHER = ()
TP1_WEATHER_STEPS = (0.30, 0.35)
TP1_SEAM_M = 0.45               # standing-seam pitch of the sheet roofs


def texpass_materials_m(MS, grey):
    """materials_m under --texpass v1: the flag-off table with satin structural paint, and under IDENTITY_M the two keys of
    the proposal (hood = slot 2, hutclad = the lodges' plank siding). galv keeps its set and tile
    (cs2_liftdress.merge_materials: one galv on every station)."""
    table = {
        "paint1": MS(albedo=grey, metal=0.10, rough=TP1_ROUGH_PAINT, slot=1, recolor_albedo=True,
                     projection="planar"),
        "signband": MS(albedo=grey, metal=0.10, rough=0.50, slot=1, recolor_albedo=True),
        "concrete": MS(texture_set="Concrete034", metres_per_tile=3.0, metal=0.0, **TP1_CONCRETE),
        "darksteel": MS(albedo=cs2_asset.STEEL_DEFAULT_RGB, metal=0.2, rough=0.60),
        "galv": MS(texture_set="Metal032", metres_per_tile=2.0, metal=1.0, rough=0.65, tint=TP1_GALV_TINT),
        "glass": MS(albedo=(0.07, 0.09, 0.11), metal=0.0, rough=0.08),
        "rope": MS(**cs2_asset.ROPE_MATERIAL),
        "rubber": MS(albedo=(0.04, 0.04, 0.045), metal=0.0, rough=0.70),
        "timber": MS(albedo=_lin_m((0.50, 0.40, 0.29)), metal=0.0, rough=0.78),
    }
    if IDENTITY_M == "shack":
        table["hood"] = MS(albedo=grey, metal=0.0, rough=TP1_ROUGH_HOOD, slot=2, recolor_albedo=True,
                           projection="planar")
        table["hutclad"] = MS(**TP1_HUTCLAD)
    return table


def identity_rekey_m(parts):
    """IDENTITY_M: the proposal's material keys on the named parts (TP1_REKEY). Objects, names and order are unchanged."""
    table = TP1_REKEY[IDENTITY_M]
    hit = sorted(o.name for o, _m in parts if o.name in table)
    if not hit:
        raise RuntimeError(f"identity {IDENTITY_M}: none of {sorted(table)} in this station")
    return [(o, table.get(o.name, m)) for o, m in parts]


def set_texpass_m(texpass="off", identity="off"):
    """main_m: the pass and the identity proposal of this run ("off" / None = the shipped build). Tests call it too."""
    global TEXPASS_M, IDENTITY_M
    tp = None if texpass in (None, "off") else texpass
    idn = None if identity in (None, "off") else identity
    if tp not in (None,) + TEXPASS_CHOICES[1:] or idn not in (None,) + IDENTITY_CHOICES[1:]:
        raise ValueError(f"texpass {texpass!r} / identity {identity!r}: choices {TEXPASS_CHOICES} / {IDENTITY_CHOICES}")
    if idn and not tp:
        raise SystemExit("--identity needs --texpass v1")
    TEXPASS_M, IDENTITY_M = tp, idn


def texpass_block_m(hook, part_material):
    """The manifest's `texpass` block (written only under the flag)."""
    keys = sorted(set(part_material.values()))
    return {"version": TEXPASS_M, "identity": IDENTITY_M or "off",
            "rough": {"paint1": TP1_ROUGH_PAINT, **({"hood": TP1_ROUGH_HOOD} if IDENTITY_M else {})},
            "galv_tint": list(TP1_GALV_TINT), "concrete": {k: list(v) if isinstance(v, tuple) else v
                                                           for k, v in TP1_CONCRETE.items()},
            "density": dict(TP1_DENSITY), "tone": {k: dict(v) for k, v in TP1_TONE.items() if k in keys},
            "tone_stats": hook.inner.tone, "cavity": hook.inner.cavity,
            "relief": {"pad_joint_m": TP1_JOINT_M, "roof_seam_m": TP1_SEAM_M},
            "rekey": dict(TP1_REKEY[IDENTITY_M]) if IDENTITY_M else {},
            "weather": [list(map(str, r)) for r in TP1_WEATHER],
            "tris_added": 0, "textures_added_mb": 0.0}


def density_m():
    """The density weights of this build: M_DENSITY, with TP1_DENSITY over it under --texpass v1."""
    return {**M_DENSITY, **TP1_DENSITY} if TEXPASS_M else M_DENSITY


def texpass_detail_specs_m(D):
    """Relief the pass adds to detail_specs_m: slab joints on the pad, raised standing seams across the two sheet roofs
    ("rings" on a horizontal face = lines at constant travel x, across the short side). Under IDENTITY_M the hut loses the
    corrugation: its boards come from the scan's own normal map."""
    out = {
        "pad": D(seams_m=TP1_JOINT_M, seam_width_m=0.03, seam_depth_m=0.008),
        "houseroof": D(seams_m=TP1_SEAM_M, seam_axes="rings", seam_width_m=0.03, seam_depth_m=-0.012),
        "hutroof": D(seams_m=TP1_SEAM_M, seam_axes="rings", seam_width_m=0.03, seam_depth_m=-0.012),
    }
    if IDENTITY_M == "shack":
        out["hut"] = D()
    return out


def tone_specs_tp1(table, part_material, ppm_of):
    """detail_tone specs keyed by FULL part name (every part gets a key, so a short name never lends its tone to a longer one):
    TP1_TONE by material key, a galv bloom cell never below TP1_TONE_FLOOR_TEXELS texels."""
    D = cs2_compose.DetailSpec
    out = {}
    for i, nm in enumerate(table, start=1):
        key = part_material.get(nm)
        tone = TP1_TONE.get(key)
        if tone is None or nm.startswith(("signband", "signboard", "scale_", "badge", "label_", "plate_cap")):
            out[nm] = D()
            continue
        g = dict(tone)
        ppm = ppm_of.get(i, 0.0)
        if key == "galv" and ppm > 0:
            g["mottle_m"] = max(g["mottle_m"], TP1_TONE_FLOOR_TEXELS / ppm)
        out[nm] = D(**g)
    return out


def make_post_compose_tp1(obj, reserved, part_material):
    """--texpass v1 twin of make_post_compose (same steps, same order): the relief of detail_specs_m, then the tone layer
    (tone_specs_tp1) and the cavity term (TP1_CAVITY) go through compose_maps' tone_l / tone_s. Records hook.tone and
    hook.cavity for the manifest."""
    def hook(base, nrm, msk, ctl, ao, mesh, A):
        hook.density = cs2_uv.density_report(obj, A, reserved_rects=reserved)
        part_map, ppm_map = cs2_compose.rasterise_face_fields(mesh, A)
        h = cs2_compose.detail_height(mesh, A, detail_specs_m())
        hook.height_range = (float(h.min()), float(h.max()))
        table = cs2_compose.read_part_table(mesh)
        flat_p, flat_d = part_map.ravel(), ppm_map.ravel()
        order = np.argsort(flat_p, kind="stable")
        cuts = np.flatnonzero(np.diff(flat_p[order])) + 1
        ppm_of = {}
        for seg in np.split(order, cuts):
            p = int(flat_p[seg[0]])
            if p > 0:
                ppm_of[p] = float(np.median(flat_d[seg]))
        dl, ds = cs2_compose.detail_tone(mesh, A, tone_specs_tp1(table, part_material, ppm_of))
        hook.tone = {k: dict(v) for k, v in cs2_compose.LAST_TONE_STATS.items()}
        c = TP1_CAVITY
        cav = np.clip((-h - c["from_m"]) / c["span_m"], 0.0, 1.0).astype(np.float32)
        hook.cavity = {"texels": int((cav > 0).sum()), "mean": round(float(cav[cav > 0].mean()), 4) if (cav > 0).any()
                       else 0.0, **c}
        dl = dl - c["dark"] * cav
        ds = ds - c["dull"] * cav
        uncovered = nrm[..., 0:3].max(axis=-1) < 1e-3       # EMIT bake left them black
        base, nrm, msk, ctl = cs2_compose.compose_maps(
            base, nrm, msk, ctl, None, h, ppm_map,
            {"reserved": reserved, "part_map": part_map, "seed": 0, "tone_l": dl, "tone_s": ds})
        n = nrm[..., 0:3] * 2.0 - 1.0
        n[..., 2] = np.maximum(n[..., 2], 0.05)
        n /= np.maximum(np.linalg.norm(n, axis=-1, keepdims=True), 1e-6)
        nrm[..., 0:3] = n * 0.5 + 0.5
        nrm[uncovered, 0:3] = (0.5, 0.5, 1.0)
        nrm[..., 3] = 1.0
        print(f"[terminal] compose tp1: relief height {h.min():+.4f} .. {h.max():+.4f} m, tone {hook.tone}, cavity "
              f"{hook.cavity['texels']} texels, {int((~uncovered).sum())} covered texels @ {A}")
        return base, nrm, msk, ctl
    hook.density = None
    hook.height_range = None
    hook.tone = hook.cavity = None
    return hook


# not walkable by design (C15-FG / tram C15 N2 rule: every standable top is walkable or listed)
M_NOT_WALKABLE = (
    ("beam_", "the portal cross beams: steel members, not a walkway (no railing, no access)"),
    ("beamdrum_", "the portal beam's end drums: steel"),
    ("endmember_", "the rail end members: steel"),
    ("rail_n", "the main tube rails: round steel members"),
    ("rail_p", "the main tube rails: round steel members"),
    ("carr_", "the tension carriage under the house: machinery"),
    ("hutroof", "the operator hut / booth roof: no roof access"),
    ("hutbase", "the booth slab: an interior floor behind the booth walls"),
    ("cwpot", "the counterweight pot lid: machinery, hangs free"),
    ("cwfoot", "the counterweight footing: a 0.25 m concrete pad, not a platform"),
    ("column", "the top wheel column: under the hub (machinery)"),
    ("colplate", "the column base plate on the crest (4 cm, machinery)"),
    ("bearingdeck_", "the bearing deck under the top wheel: machinery, < 0.5 m under the spokes"),
    ("cabinet", "the electrical cabinet top"),
    ("sheave_bull", "the bullwheel rim: machinery"),
    ("wheelhub", "the bullwheel hub: machinery"),
    ("bearing", "the top bearing housing: machinery"),
    # nitpick A (2026-10-13): the base's hold-down trains (the towers' Yan rocker tree) and their box boom
    ("sheave_hold_", "the hold-down sheave trains (rocker tree): machinery over the rope"),
    ("holdboom_", "the hold-down boom: a steel member over the rope, no access"),
    ("holdhang_", "the hold-down boom's saddle on the portal beam: steel"),
    # art-601 (M_NOSE_TRAIN): the boom's haunch web and its root plate on the front beam
    ("holdweb_", "the hold-down boom's root gusset tab: steel, no access"),
    ("holdplate_", "the hold-down boom's root cheek plates on the front beam: steel"),
    ("holdclamp_", "the hold-down boom's hanger clamp and lifting tab: steel, no access"),
)
M_WALK = ("pad", "loaddeck", "carpet_", "grate_catwalk", "houseroof", "crest", "liftiebay")
# FG walking ramps: (ramp part, low surface or GRADE, high surface)
M_RAMPS = {"drive": (("loadramp", "pad", "loaddeck"),), "return": (("rampup", "GRADE", "crest"),)}

# declared part-against-part contacts (C14-FG, tram C14 kinds = leading letters). Each line: reason -> kind pairs. The pairs
# of the dressing kit come from cs2_liftdress.DRESS_CONTACTS.
M_CONTACTS = {
    "legs stand on the pad on bolted flanges, gussets weld leg to beam":
        "leg:pad flange:leg flange:pad bolt:flange gusset:leg beam:gusset beam:leg",
    "the main rails ride the portal beams on saddle clamps, caps / rings / lugs welded on":
        "beam:saddle rail:saddle beam:rail rail:railcap rail:stiff rail:raillug",
    "portal beam end drums and the badge plaque on the front beam": "beam:beamdrum badge:beam",
    "rail end members tie the two rails": "endmember:rail",
    "the tension carriage rides the rails, the wheel shaft hangs from its yoke":
        "carr:rail carr:carr carr:wheelshaft",
    "bullwheel: rim / spokes / hub / shaft, the rope in the rim groove":
        "sheave:spoke spoke:wheelhub spoke:spoke sheave:wheelrope wheelhub:wheelshaft "
        "ropechord:wheelrope",
    "hold-down sheaves press the rope from above, hung from their boom, the boom hung from "
    "the front beam": "ropechord:sheave holdboom:sheave holdboom:holdhang beam:holdhang",
    "art-601 nose train: the slim tube boom, its hanger clamp round the train's hanger, the gusset tab on its top, the "
    "cheek plates clamping it onto the saddle on the front beam":
        "holdboom:holdclamp holdclamp:sheave holdboom:holdweb holdboom:holdplate holdplate:holdweb holdhang:holdplate "
        "beam:holdplate",
    "nitpick A (2026-10-13): a sheave train is ONE assembly (cs2_sheaves: hanger in its boom, main rocker, pins, sub "
    "rockers, sheave arms, wheel discs + rubber liners, axle caps), the boom's front end in its saddle on the portal "
    "beam; the counterweight wheels' discs + liners":
        "sheave:sheave beam:holdboom",
    "the house rides the rails on its wheel discs; door, hatch, sign, windows on its walls; "
    "the roof slab and its drips on top; exhaust and antenna on the roof":
        "house:rail house:wheeldisc rail:wheeldisc door:house hatch:house house:signband "
        "house:winframe house:houseroof drip:houseroof exhaust:houseroof antenna:houseroof "
        "antenna:antwhip exhaust:exhaust doorlite:door house:scale",
    "catwalk grating on its brackets from the rail, railing on the grating; T17: the platform deck on the rail tops "
    "and the rear rail caps, edge to edge with the catwalk":
        "catbrk:rail catbrk:grate grate:post grate:kick grate:rail house:grate grate:railcap grate:grate",
    "ladders: rails on the pad / catwalk, standoffs to the rail cap and the house":
        "ladder:pad ladder:grate ladder:railcap ladder:house ladder:houseroof ladder:rail "
        "lrung:ladder",
    "counterweight: mast on its footing, sheaves on the mast head and the end-member brackets, "
    "ropes over the sheaves, cables to the pot":
        "cwfoot:cwmast cwmast:sheave cwbrk:endmember cwbrk:sheave cwrope:sheave "
        "cwrope:cwrope cwcable:sheave cwcable:cwpot cwpot:cwpot carr:cwrope wrung:cwmast",
    "load deck crib + ramp on the pad, carpet / stripe / seat marks on the deck":
        "loaddeck:pad loadramp:pad loaddeck:loadramp carpet:loaddeck stripe:carpet "
        "label:carpet",
    "operator hut: base, walls, door, roof, drips": "hut:hutbase door:hut drip:hutroof "
                                                    "hut:hutroof doorlite:door",
    "tension-scale plates on the rails' outboard faces": "rail:scale",
    "top knoll: crest between the ramps, the bay beside them, column on its plate on the crest, "
    "bearing deck on the column": "crest:rampup crest:rampdown crest:liftiebay "
                                  "liftiebay:rampup liftiebay:rampdown column:colplate "
                                  "colplate:crest bolt:colplate bearingdeck:column "
                                  "bearingdeck:bearingdeck bearing:bearingdeck bearing:wheelhub "
                                  "plate:column wrung:column",
    "booth on its slab on the bay; weather mast and guys on its roof; bay railing; signboard on "
    "the bay face; propane on the bay; stowed stop arm in its bracket":
        "hutbase:liftiebay liftiebay:post liftiebay:kick liftiebay:rail signboard:liftiebay "
        "propane:liftiebay propane:propane hutroof:wxmast guy:hutroof guy:wxmast wxarm:wxmast "
        "wxarm:wxcup stoparm:stoparm liftiebay:stoparm",
    "stakes on the crest edge with their black wraps": "crest:stake rampup:stake "
                                                       "rampdown:stake stake:stakewrap",
    "kit internals: a door's leaf in its frame, drip corners meeting, the antenna whips from one "
    "boss, the guys meeting at the mast, the maintenance rungs' U legs, the counterweight mast's "
    "cap / pedestal / axle": "door:door drip:drip antwhip:antwhip guy:guy wrung:wrung cwmast:cwmast",
    "glazing: each pane sits inside its frame ring, the frame on the wall":
        "cabwin:winframe hutwin:winframe hut:winframe",
    "door jambs stand on the hut base / booth slab": "door:hutbase",
    "top: the bearing housing sits in the column top, hazard plates on the bearing-deck beam ends, "
    "the unload stripe painted on the crest, the e-stop post on the bay":
        "bearing:column bearingdeck:chev crest:stripe estop:liftiebay",
    "the scraper + shovel lean on the cabinet": "cabinet:tool",
    "fine-detail step 3c: the cabinet's lid lip sits on its body": "cabinet:cabinet",
    "dressing on its host (boards / gauges / extinguishers / tools on walls and posts, posts and "
    "the rack on the floor)":
        "board:pad board:crest board:liftiebay estop:pad estop:crest gate:pad maze:pad "
        "maze:crest skirack:pad ski:pad ext:hut ext:house ext:grate gauge:house tool:hut "
        "tool:hutbase tool:liftiebay tool:rail board:hut board:gate ext:liftiebay tool:post "
        "board:hutbase",
}

# LOD policy: level 1 drops the sub-pixel hardware and every dressing family (cs2_liftdress lod_split adds the exact names of
# the dressing). Level 2 keeps the masses (pad / knoll, legs, beams, rails, house, hut / booth, rim, column, counterweight
# mast + pot).
M_LOD1_DELETE = ("bolt_", "clamp_", "drip_", "kick_", "post_", "rail_catwalk", "rail_bay",
                 "lrung_", "hatch_", "wrung_", "antwhip", "guy_", "winframe_", "doorlite_",
                 "raillug_", "holdhang_", "cwbrk_", "stoparm", "gusset_", "catbrk_", "wxarm",
                 "wxcup", "propane", "plate_cap", "sheave_hold")
M_LOD2_DELETE = M_LOD1_DELETE + ("spoke_", "wheelhub", "wheelrope", "grate_", "ladder_",
                                 "flange_", "stiff_", "saddle_", "endmember_", "carr_",
                                 "wheelshaft", "railcap_", "hutroof", "cabinet", "beamdrum_",
                                 "badge", "scale_", "cwrope_", "cwcable_", "sheave_cw",
                                 "wheeldisc_", "exhaust", "antenna", "holdboom_", "holdweb_", "holdplate_", "holdclamp_", "door_",
                                 "stake", "bearing", "colplate", "wxmast", "ropechord_",
                                 "houseroof", "signband_", "loadramp")

# ---------------------------------------------------------------- mesh helpers ------------


def _mbox(name, x0, x1, y0, y1, z0, z1, open_bottom=False, drop=()):
    return cs2_kit.add_box(name, (x1 - x0, y1 - y0, z1 - z0),
                           ((x0 + x1) / 2.0, (y0 + y1) / 2.0, (z0 + z1) / 2.0),
                           open_bottom=open_bottom, drop=drop)


def _mcyl(name, p0, p1, r, segs=8, caps="both"):
    a, b = _V(p0), _V(p1)
    d = b - a
    return cs2_kit.add_cyl(name, r, d.length, tuple((a + b) / 2.0),
                           rot=tuple(d.to_track_quat("Z", "Y").to_euler()), segments=segs, caps=caps)


def _mquad(name, corners, hint):
    """One quad wound to face `hint` (a pinned sign / label face)."""
    bm = bmesh.new()
    vs = [bm.verts.new(tuple(float(c) for c in p)) for p in corners]
    f = bm.faces.new(vs)
    f.normal_update()
    if f.normal.dot(_V(hint)) < 0.0:
        f.normal_flip()
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(o)
    return o


def _mbm_obj(name, bm):
    bm.normal_update()
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(o)
    return o


def _kit_m(kit_parts, key, **over):
    """Kit assembly [(obj, quad int)] -> [(obj, material key)], longest-prefix overrides."""
    rules = sorted(over.items(), key=lambda kv: -len(kv[0]))
    out = []
    for o, _q in kit_parts:
        k = key
        for pfx, kk in rules:
            if o.name.startswith(pfx):
                k = kk
                break
        out.append((o, k))
    return out


def _half_torus(name, cx, cz, R, r, majors=12, minors=6):
    """The rope around the wheel: a half torus about (cx, 0, cz) bulging +x (the code arc, LiftLoopCache AppendTerminalTurn),
    open at both tangent points (0, +-R) where the chords land. 2 * majors * minors quads."""
    bm = bmesh.new()
    rings = []
    for i in range(majors + 1):
        a = -math.pi / 2.0 + math.pi * i / majors
        c = _V((cx + R * math.cos(a), R * math.sin(a), cz))
        radial = _V((math.cos(a), math.sin(a), 0.0))
        up = _V((0.0, 0.0, 1.0))
        rings.append([bm.verts.new(c + radial * (r * math.cos(2 * math.pi * k / minors))
                                   + up * (r * math.sin(2 * math.pi * k / minors)))
                      for k in range(minors)])
    for i in range(majors):
        for k in range(minors):
            k1 = (k + 1) % minors
            bm.faces.new((rings[i][k], rings[i + 1][k], rings[i + 1][k1], rings[i][k1]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    # outward: the face center minus its ring center must point along the normal
    for f in bm.faces:
        cen = f.calc_center_median()
        a = math.atan2(cen.y, cen.x - cx)
        rc = _V((cx + R * math.cos(a), R * math.sin(a), cz))
        if f.normal.dot(cen - rc) < 0.0:
            f.normal_flip()
    return _mbm_obj(name, bm)


def _oval_plate(name, centre, facing_x, w, h, t, segs=16):
    """An elliptical plaque w (along y) x h (z), t thick toward -facing (its back on the face plane), front at center. facing_x
    = -1 / +1 (the plaque faces -x / +x)."""
    bm = bmesh.new()
    cx, cy, cz = centre
    front, back = [], []
    for k in range(segs):
        a = 2.0 * math.pi * k / segs
        y, z = cy + 0.5 * w * math.cos(a), cz + 0.5 * h * math.sin(a)
        front.append(bm.verts.new((cx, y, z)))
        back.append(bm.verts.new((cx - facing_x * t, y, z)))
    bm.faces.new(front)
    for k in range(segs):
        k1 = (k + 1) % segs
        bm.faces.new((front[k], front[k1], back[k1], back[k]))
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    for f in bm.faces:
        cen = f.calc_center_median()
        out = _V((facing_x, 0.0, 0.0)) if abs(cen.x - cx) < 1e-6 and len(f.verts) == 3 else \
            _V((0.0, cen.y - cy, cen.z - cz))
        if f.normal.dot(out) < 0.0:
            f.normal_flip()
    return _mbm_obj(name, bm)


def _frame_ring(name, plane, c, n_sign, u0, u1, v0, v1, w=0.04, d=0.025):
    """Window frame ring around a pane rect on a wall plane: plane "x" (wall at x = c, the rect in (y, z)) or "y" (wall at y =
    c, the rect in (x, z)). n_sign = the wall's outward normal sign. Front face + outer and inner side faces (back open on
    the wall), 24 tris."""
    bm = bmesh.new()

    def P(a, b, off):
        return (c + n_sign * off, a, b) if plane == "x" else (a, c + n_sign * off, b)
    oc = [(u0 - w, v0 - w), (u1 + w, v0 - w), (u1 + w, v1 + w), (u0 - w, v1 + w)]
    ic = [(u0, v0), (u1, v0), (u1, v1), (u0, v1)]
    of = [bm.verts.new(P(a, b, d)) for a, b in oc]
    ifr = [bm.verts.new(P(a, b, d)) for a, b in ic]
    ob = [bm.verts.new(P(a, b, 0.0)) for a, b in oc]
    ib = [bm.verts.new(P(a, b, 0.0)) for a, b in ic]
    for k in range(4):
        k1 = (k + 1) % 4
        bm.faces.new((of[k], of[k1], ifr[k1], ifr[k]))          # front
        bm.faces.new((ob[k], ob[k1], of[k1], of[k]))            # outer side
        bm.faces.new((ifr[k], ifr[k1], ib[k1], ib[k]))          # inner side (reveal)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    nrm = _V((n_sign, 0, 0)) if plane == "x" else _V((0, n_sign, 0))
    cu, cv = (u0 + u1) / 2.0, (v0 + v1) / 2.0
    for f in bm.faces:
        cen = f.calc_center_median()
        if f.normal.dot(nrm) < -0.5:
            f.normal_flip()
        elif abs(f.normal.dot(nrm)) < 0.5:
            a, b = (cen.y, cen.z) if plane == "x" else (cen.x, cen.z)
            # outer sides point away from the pane center, the reveal sides toward it
            outer = (a < u0 - w / 2 or a > u1 + w / 2 or b < v0 - w / 2 or b > v1 + w / 2)
            radial = _V((0, a - cu, b - cv)) if plane == "x" else _V((a - cu, 0, b - cv))
            want = radial if outer else -radial
            if f.normal.dot(want) < 0.0:
                f.normal_flip()
    return _mbm_obj(name, bm)


def _pane(name, plane, c, n_sign, u0, u1, v0, v1, proud=0.006):
    if plane == "x":
        x = c + n_sign * proud
        pts = [(x, u0, v0), (x, u1, v0), (x, u1, v1), (x, u0, v1)]
        hint = (n_sign, 0, 0)
    else:
        y = c + n_sign * proud
        pts = [(u0, y, v0), (u1, y, v0), (u1, y, v1), (u0, y, v1)]
        hint = (0, n_sign, 0)
    return _mquad(name, pts, hint)


def _tag_label_m(parts, right, up, kind):
    """Record a label quad's reading frame (u = right, v = up) for the seed (detach's rule)."""
    for o, _m in parts:
        if o.name.startswith("label_"):
            o["d1_right"] = [float(v) for v in right]
            o["d1_up"] = [float(v) for v in up]
            o["d1_kind"] = kind
    return parts


def chord_z(spec, x, anchor="in"):
    """The height of the code chord (grip top = lane) at authoring x on the straight from the HV sub-lane end to the wheel
    tangent (LiftLoopCache AppendTerminalTurn)."""
    ax, az = spec.anchor_in if anchor == "in" else spec.anchor_out
    wx, _wy, wz = spec.wheel
    t = (x - ax) / (wx - ax)
    return az + (wz - az) * t


# ---------------------------------------------------------------- labels / signs ----------

M_LABELS = {
    "drive": {"label_tipsup": "tipsup", "label_ops": "ops", "label_slow": "slow",
              "label_waithere": "waithere", "label_seat1": "seat1", "label_seat2": "seat2",
              "label_seat3": "seat3", "label_tension": "tension"},
    # 2026-10-12: no label_stay (the top's STAY SEATED board is gone, M19). Its sheet rect goes with it
    "return": {"label_unload": "unload", "label_ops": "ops", "label_slow": "slow"},
}
# M19 (2026-10-12, the DQ top's D11 `forbid`): board kinds that a station must not carry in ANY mount. A part named
# board_<kind>* / label_<kind>* raises. The top: STAY SEATED (it stood past the unload line, "doesn't quite make sense" at a
# top station).
M_FORBID = {"drive": (), "return": ("stay",)}
# floor marks: (authoring right, authoring up) of the reading frame (riders face -x at the load)
M_FLOOR_FRAME = {"seat": ((0.0, 1.0, 0.0), (-1.0, 0.0, 0.0))}


def _LTD():
    import lift_terminal_detach as LTD     # lazy: it imports this module at its top
    return LTD


def sign_layout_m():
    return _LTD().sign_layout(FG_SEATS_DIGITS)


def sign_rect_m(A=M_ATLAS):
    """The ONE sign rect (u0, v0, u1, v1): 448 px/m on both axes at 2048 (the DQ rule)."""
    LTD = _LTD()
    lay = sign_layout_m()
    k = LTD.SIGN_PX_PER_M / float(M_ATLAS)
    u0, v0 = M_SIGN_PX[0] / float(M_ATLAS), M_SIGN_PX[1] / float(M_ATLAS)
    return (u0, v0, u0 + lay["W"] * k, v0 + lay["H"] * k)


def emblem_rect_m():
    """The emblem square inside the sign rect (the badge's emblem quad reads it)."""
    u0, v0, u1, v1 = sign_rect_m()
    lay = sign_layout_m()
    e0, e1, f0, f1 = lay["emblem"]
    su, sv = (u1 - u0) / lay["W"], (v1 - v0) / lay["H"]
    return (u0 + e0 * su, v0 + f0 * sv, u0 + e1 * su, v0 + f1 * sv)


def scale_rect_m():
    x0, y0, x1, y1 = M_SCALE_PX
    return (x0 / float(M_ATLAS), y0 / float(M_ATLAS), x1 / float(M_ATLAS), y1 / float(M_ATLAS))


def label_sheet_m(kind):
    import cs2_liftdress as LD
    items = {nm: LD.board_px(k, M_LABEL_PPM) for nm, k in sorted(M_LABELS[kind].items())}
    return LD.pack_label_sheet(items, M_LABEL_REGION_PX, gutter_px=M_LABEL_GUTTER_PX)


def reserved_rects_m(kind):
    """{name: (u0, v0, u1, v1)}: the sign rect, the scale band (base) and the label sheet."""
    out = {"sign": sign_rect_m()}
    if kind == "drive":
        out["scale"] = scale_rect_m()
    for nm, r in label_sheet_m(kind).items():
        out[nm] = tuple(v / float(M_ATLAS) for v in r)
    return out


# ---------------------------------------------------------------- base station (drive) ----

M_BASE = dict(
    pad=(-5.50, 6.00, 3.85), pad_h=0.30,
    leg_r=0.30, leg_y=3.35, front_x=-3.20, back_x=4.00,
    beam_front=(-3.45, -2.95, 5.35, 5.85), beam_back=(3.75, 4.25, 4.15, 4.55), beam_y=3.70,
    rail_y=1.10, rail_r=0.40, rail_z=4.95, rail_x=(-5.00, 5.00),
    ring_x=(-2.35,),     # MC2: no ring in the chord zone (x < -2.4). Under the house / catwalk none fits
    house=(-1.30, 4.50, 1.25, 5.35, 7.55), roof_t=0.12, roof_over=0.12,
    catwalk=(-1.80, 5.30, 1.36, 2.20), rail_line=2.16,
    hut=(-2.10, -0.30, 4.35, 5.95), hut_h=2.50, hutbase=(-2.50, 0.40, 4.20, 6.20, 0.15),
    deck=(-1.00, 1.40, 0.60, 3.00, 0.80), ramp_x=2.90,
    cwfoot=(6.40, 9.60, 1.20, 0.25), mast_foot=(6.80, 0.25), mast_head=(8.30, 5.60),
    pot_x=8.66, pot_r=0.70, pot_z=(0.75, 1.95),
    # the rear-end numbers the builder used to hard-code (T17 made them keys, these are the pre-T17 values)
    endmember_x=4.60, ladder_x=5.48, catbrk_x=(-0.80, 1.60, 4.10), defl_x=5.00, cwbrk_x=(4.62, 5.06),
    slow=(5.60, -2.20, 2.20), rear_lugs=True, platform=None,
)

# -- T17 loading room (Ash Flat 2026-10-15 item 16, shot 17.44.53.17, build/T17-fg-loadroom.md) -----------------
# "The entire rear support posts need to be moved back (and thus the weight and such; we can have a bigger empty platform up
# top) so that there is more room for the skiers to get into the lift load spot."
#
# The rear portal (legs, beam, saddles), the rail ends + end member, the counterweight (deflection sheave, brackets, mast,
# pot, footing) and the main ladder move back 2.80 m inside the SAME 2 x 3 lot. The house does NOT move (its door, wall light
# and extinguisher are frozen sub-object rows). The catwalk runs on to the new rail end. A grating deck (the empty platform)
# fills the rail top behind the house, railed around. 6.80 is the nearest leg x whose 0.46 flange clears the frozen
# SkiPisteFloodlightLow row at (5.60, -3.40) (its 0.90 m base plate) by >= 0.2 m. The counterweight leans 1.00 m (was 1.50),
# so pot + footing end inside the lot (11.75). False = the pre-T17 base byte for byte.
M_BASE_LOADROOM = True
M_LOADROOM = dict(
    pad=(-5.50, 8.60, 3.85), back_x=6.80, beam_back=(6.55, 7.05, 4.15, 4.55), rail_x=(-5.00, 7.80),
    endmember_x=7.40, catwalk=(-1.80, 8.10, 1.36, 2.20), ladder_x=8.28, catbrk_x=(-0.80, 1.60, 4.10, 5.90, 7.70),
    defl_x=7.80, cwbrk_x=(7.42, 7.86), cwfoot=(8.95, 11.55, 1.20, 0.25), mast_foot=(9.30, 0.25),
    mast_head=(10.30, 5.60), pot_x=10.66, slow=(8.40, -2.40, 2.20), rear_lugs=False,
    # the empty platform: grating x0, x1, y0, y1, top z, thickness (on the rail tops 5.35, 0.04 over the catwalk: one walking
    # level, C15 level 0.05). y0 flush with the house's -y wall
    platform=(4.50, 8.10, -1.25, 1.36, 5.39, 0.04),
)


def m_base():
    """The base station's numbers: M_BASE, with the T17 loading-room rear end (M_LOADROOM) when M_BASE_LOADROOM."""
    return dict(M_BASE, **M_LOADROOM) if M_BASE_LOADROOM else M_BASE


# -- Nitpick A (2026-10-13, twenty-fifth look, build/nitpick-a-stations.md) ---------------------------
# S-2 ("Let's all apply the same PULLEY WHEELS / ROLLERS we use on the lift tower onto the lift stations"): the four
# hold-down sheaves per lane of the base become the Yan rocker tree of the towers (cs2_sheaves.fg_train_side). It is the code
# that lift_tower builds every FG / ET head with: hanger, main rocker, pins, two sub rockers, sheave arms, 4 sheaves with
# rubber liners and axle caps, the FG8 numbers r 0.20 / w 0.13 / pitch 0.52. It is finished as the towers are
# (cs2_sheaves.finish_train) and built in the frame of the CHORD (x along the rope, z its normal), so all four sheave bottoms
# sit on the top of the rope (the code chord, A1). It hangs from a box boom (the 0.22 depth of the tower crossarm) whose
# front end rests in a saddle on the front portal beam. The head + deflection sheaves of the counterweight are the wheels of
# the towers (lite_sheave_parts).
#
# S-3 ("The ladder and the corral fencing would get in the way of each other. I wouldn't worry about corral fencing; the
# skiers could enter through the side."): the rope maze + swing gate of the base are gone. WAIT HERE stands on a post. Both
# FINE_DETAIL_M modes (the DQ STAY SEATED precedent): --no-fine is the approved station minus these changes.
M_HOLD_TRAIN = True            # False = the pre-2026-10-13 plain hold-down / counterweight cylinders (A-B, negatives)
M_BASE_CORRAL = False          # True = the pre-2026-10-13 rope maze + swing gate + WAIT HERE on the gate panel
M_WAITHERE_POST = (1.55, 3.30, 1.75)   # WAIT HERE board center: off the deck's +y edge beside the ramp top, facing +X
M_HOLD = dict(xc=-4.60, n=4, prefix="sheave_hold_", boom_w=0.15, boom_rear=-0.30, boom_front_x=-3.30,
              saddle_x=(-3.44, -3.24), saddle_w=0.18)
# -- art-601 (owner hand-test notes 5 / 11, 2026-11-05): the base without its hosted end tower -------------------------
# "FG base station: first sheave train not attached to anything; drop the station's first tower since the station sheaves do
# its job?"
#
# M_NOSE_TRAIN True (CLI --no-hosted-tower, drive only): the hosted SkiLiftTerminalTowerBase row (ET68 at x -6.9) leaves the
# prefab at the import round. The own hold-down train of the station becomes the first train the rope meets. It moves out
# along the code chord until the rim of its outer sheave reaches the inboard end of the HV sub-lane (anchor_in). The
# sub-lanes and the chord are unchanged, so the line rope hands over to the modeled ropechord_ right there. Its boom runs
# back to the same saddle on the front portal beam.
#
# Round 2 (the reference photo of a real FG base from the owner, "as long as they don't look too chunky or blocky"): the boom
# is a slim round tube (the real arm). The hanger of the train sits in a small clamp with a lifting tab on top. A thin gusset
# tab stiffens the root. Two thin cheek plates clamp the tube onto the saddle. Round 1 read chunky with its 0.15 x 0.22 box,
# 0.42 m haunch web and full root plate.
#
# Everything sits above the rope in the plane of the lane: the chairs pass under it as under the train. False = the pre-art-601
# station; the shipped letter O base is built with it on (--no-hosted-tower, SHIPPED_FLAGS).
M_NOSE_TRAIN = False
M_NOSE = dict(rim_gap=0.02,          # m along x: the outer rim's line-side edge inboard of anchor_in
              tube_r=0.075, tube_segs=12,   # the boom tube (round 1: a 0.15 x 0.22 box)
              clamp=(0.10, 0.085, 0.10, 0.115),   # hanger clamp: half length, half width, below / above the tube axis
              tab=(0.06, 0.015, 0.09),      # its lifting tab on top: half length, half thickness, height
              web_d=0.16, web_len=0.50, web_t=0.03,   # root gusset tab on the tube's top: root depth, length, thickness
              cheek_t=0.02, cheek_over=0.02, cheek_up=0.03)   # root cheeks: thickness, past the saddle, over the tube
# -- T256 (r4 lift-motion, 2026-11-19): the RIGGED fixed-grip stations (CLI --rig, letter P) ----------------------------
# M_RIG True (set_m_rig, main_m --rig): the station mesh is skinned (cs2_skin rig, every LOD):
# - bone `Root` (static), `Bullwheel` (rim, spokes, hub, hub cap, the painted mark; head on the wheel center, the game turns
#   it about its local Y = the vertical axle), and on the base `NoseTrain` (the OUTER half of an articulated hold-down train
#   per lane, head on the line through both lane ends, game (0, 5.84, -7.025); the game pitches it about its local X).
# - the modeled rope starts at the LANE END (the HV sub-lane's outboard end, anchor_out: the carriers' chain ends there,
#   LiftLoopCache.AppendTerminalTurn) instead of the sub-lane's inboard end: M_SPECS anchor_in := anchor_out, so the chord,
#   the train frame, M2 / M3 / M21 and the previews all follow one datum. The prefab's sub-lanes do not move (M1 reads
#   M_SPECS_SHIPPED).
# - the rope is a hexagon (one vertex on the horizontal) of RIG_ROPE_R: as drawn in game by the line's cable (SkiRopeStillLVA
#   = vanilla LV cable; its mesh is r 0.060, rope-width-check 2026-11-19, but P1 measured about 0.04 m on screen), so r 0.024,
#   the chair grip's rope. The half torus has RIG_TORUS_MAJORS segments (ring centers on the code arc, chord
#   sagitta reported). LOD1 keeps the rope faces un-decimated (RigLodFixed).
# - the 8-spoke bullwheel (box spokes, hub cap, one safety_yellow spoke: the turn reads at play distance).
# - an entry (hold-down) sheave at each of the wheel's two tangent points, its rim on the rope where the chord meets the
#   level wheel plane (plan option W3), on a static arm (base: from the tension carriage, top: from an outboard post on the
#   crest), the cheek braced to the arm by a gusset.
# - base only: the nose train (fg_train_side_halves as the skinned towers' FG halves, mirrored to a hold-down: sheaves over
#   the rope, the main pin ON the rope line at the lane end, the tree RIG_NOSE["inboard"] inboard of the wheels) on the slim
#   tube boom of art-601, now out to the pin. The inner half is static, the outer half rides NoseTrain.
# - round 2 (critic pass 1, S4): the nose train's bogie pins hang in straps (cs2_sheaves.fg_train_side_halves pin_links,
#   FG_PIN_LINK) instead of floating between beam and bar; the shipped towers keep the floating pin (kit-wide fix = new letters).
# - round 2 (critic pass 1, S2 / C6): the top's entry arms come in from two outboard posts on the crest (RIG_TOP_POST,
#   outside the chairs' swept body), not from a post on the hub with a T-bar over the turning wheel; a gusset hangs each
#   cheek from its arm on both stations. S3: spoke_00 is safety_yellow (round 1: a galv plate on the rim top, too faint).
#   C1: a 6-gon knuckle collar on each lane end of the station rope (RIG_KNUCKLE). C2: a knee brace under each nose boom
#   (RIG_NOSE_BRACE). S1 (support wheels under the outer half): not built, lead decision 2026-11-20 (the chair's in-plane
#   stem would pass through a support roller's axle; art-fg-build-2.md "S1 finding").
# - round 2b (critic pass 2): the top entry posts r 0.09 with the crest stakes' black wrap (P2-S1); the nose brace's top
#   moved toward the boom root, 30 deg to the boom (P2-S4).
# False = every shipped station byte for byte (no expression below runs).
M_RIG = False
RIG_ROPE_R = THIN_ROPE_R      # m (0.024). P1 field test 2026-11-20 (lift-motion REPORT "P1 results" F2, OC-3): at r 0.060 the
                              # station rope read 3.03x the line cable at the base nose joint (same lane, same depth, 10 m:
                              # 33.4 px vs 11.0 px), so the cable draws about 0.04 m (m_MinSize), not its mesh width. 0.024 =
                              # every shipped FG station and tower seat (O measured 1.24x the cable); one prefab float
                              # (the cable's m_MinSize) can later make it exact kit-wide
RIG_ROPE_SIDES = 6            # hexagon, one vertex on the horizontal
RIG_TORUS_MAJORS = 24         # half torus segments (7.5 deg each): chord sagitta R (1 - cos 3.75 deg) = 3.9 mm at R 1.80
RIG_BONES = {"drive": ("Root", "Bullwheel", "NoseTrain"), "return": ("Root", "Bullwheel")}
RIG_BULL_PARTS = ("sheave_bull", "spoke_", "wheelhub")      # Bullwheel (wheelhub_cap included; spoke_00 the yellow mark)
RIG_NOSE = dict(inboard=0.20,     # the tree plane this far inboard of the wheels (lift_tower.FG_CROWN_INBOARD: S9-proven)
                shift=0.12,       # bogies out along the train (towers 0.07): the sub rockers clear at a 40 deg fold
                lap=-0.06,        # each half beam stops this short of the main pin (a folded bogie pin misses the beam)
                boom_tip=-0.12,   # the boom tube's line-side end, chord-frame x (just past the hanger clamp)
                tmax=40.0,        # deg, NoseTrain pitch range the art clears (the code's limit; most cases within 20)
                clear=0.005,      # m, NoseTrain vs every static part at every pose (G6 rule)
                chair_tmax=20.0)  # deg, the chair (M3 rules on the posed line) must clear within +-this; wider = reported
RIG_NOSE_CHAIR_THETAS = (-40.0, -30.0, -20.0, -10.0, 0.0, 10.0, 20.0, 30.0, 40.0)
RIG_ENTRY = dict(r=0.20, w=0.10, arm=(0.10, 0.10), cheek_t=0.03, stub_r=0.035, clear=0.005,
                 gusset=(0.14, 0.02))     # round 2 (C6): cheek 0.02 -> 0.03 and an arm / cheek gusset (leg, thickness)
RIG_LOD0_CAP = {"drive": 9450, "return": 3600}   # M15 under --rig: the base at the 5 % rule's ceiling over the 9,000
                                                 # design cap (lead, critic round 2). Round 1 finished 9,124 (rope 0.060 +
                                                 # wheel + entry sheaves + nose train); round 2 S4 pin straps +80 (est.)
RIG_HOSTED_X = -5.025         # top: the skinned hosted end tower's pivot on the lane end (shipped row: -4.9)
RIG_HOSTED_DIR = None         # top: folder holding the skinned hosted tower's FBXs (M5 / previews), CLI --rig-hosted
RIG_WHEEL = dict(spokes=8, spoke=(0.10, 0.16), spoke_r=(0.30, 1.52), cap_r=0.24, cap_h=0.06,
                 mark_spoke=0)      # round 2 (S3): this spoke is safety_yellow (the turn's mark; round 1: a galv rim plate)
RIG_KNUCKLE = dict(proud=0.006, length=0.08)   # round 2 (C1): the rope collar on each lane end (hides the V of a kinked
# line). r = RIG_ROPE_R + proud (rig_knuckle_r_m): 0.030 at the r 0.024 rope (OC-3), 0.066 at round 2's 0.060.
# First try r 0.068 x 0.12: its line-side half sat 9 mm from the chair stem at NoseTrain +20 deg (RIG-nose, M3 machine >= 0.01).
# The drawn line cable (hexagon r 0.020, SkiRopeStillLVA m_MinSize 0.04) leaves through the cap face up to a kink of about
# 8 deg; past that its silhouette crosses the collar's side (the V stays hidden; critic-oc3, the P1 joint showed 13.3 deg)
RIG_NOSE_BRACE = dict(r=0.045, segs=6, a_top=2.75, foot_z=0.15, foot_in=0.03)   # round 2 (C2): the knee brace under
# each nose boom: from the boom's axis at chord-frame a = a_top down to the front portal beam's line-side face, foot_z over its
# bottom (clear of the rail clamp saddle_f_<side>, top 0.05 over the beam bottom) and foot_in inside it. 6-gon, open ends
# (both buried): 12 tris a lane. Round 2b (critic pass 2, P2-S4): a_top 1.55 -> 2.75 opens the brace / boom angle from
# 12.9 to 30.0 deg (it read as a second boom). With the foot fixed on the beam face, a top point nearer the boom tip makes
# the brace MORE parallel to the boom; only a top nearer the root opens the triangle (about 0.77 m out from the saddle)
RIG_TOP_POST = dict(x=0.10, y=3.05, r=0.09, segs=8, plate=(0.24, 0.03), gusset=0.22,   # round 2 (S2): top entry posts
                    wrap=(0.02, 0.10, 1.60))
# Round 2b (critic pass 2, P2-S1: the load-bearing post was the slimmest on the crest, bare steel among wrapped stakes):
# r 0.065 -> 0.09, and the crest stakes' black wrap (rubber, 0.02 off the post, crest + 0.10 .. crest + 1.60, open ends:
# 16 tris a post). The chair body comes to 0.316 m (+y) / 0.321 m (-y) of the post axes at crest + 0.35, so the wrap keeps
# 0.206 / 0.211 >= M_CLEAR_STRUCT
RIG_LOD2_DELETE = ("holdbrace_", "entrypost")   # round 2: the C2 brace goes with the boom (holdboom_, M_LOD2_DELETE) at
                                                # LOD2; P2-C1: the top's entry posts (kept at LOD1) go at LOD2
RIG_GRIP_OK = ("sheave_entry",)       # the chair grip passes under the entry sheaves (as under the nose train)
RIG_LOD1_DELETE = ("sheave_entry", "entryarm", "entrypost_n_gusset", "entrypost_p_gusset", "entrypost_n_plate",
                   "entrypost_p_plate")   # with the nose train (sheave_hold): gone at LOD1. P2-C1 (critic pass 2, bundled
# with the OC-3 rope re-export): the top's two 2.6 m entry posts and their wrap stay at LOD1, so they do not pop at the LOD0 /
# LOD1 switch; the arm gussets go with the arms, the base plates (20 tris, unseen at that range) go too. About 76 tris before
# decimation; the LOD1 target is a ratio, so the decimation takes them back elsewhere
RIG_ROPE_PARTS = ("wheelrope", "ropechord_")                 # kept out of the LOD1 decimation (RigLodFixed)
RIG_CONTACTS = {   # declared contacts of the rig's own parts (contact_kinds_m adds them under M_RIG only)
    "T256 entry sheaves: the axle stub through the wheel hub and its cheek plate, the cheek and its gusset on its arm, the "
    "arm out of the tension carriage's saddle (base) or the outboard post (top)":
        "entryarm:entryarm entryarm:sheave carr:entryarm entryarm:entrypost",
    "T256 round 2 (S2) top: each outboard entry post on its base plate on the crest, with its arm gusset and its black "
    "wrap (round 2b); the crest "
    "maze's ropes tie into the +y post (it stands in place of the maze's middle post)":
        "crest:entrypost entrypost:entrypost entrypost:maze",
    "T256 bullwheel: the hub cap on the hub (the turning boss)": "wheelhub:wheelhub",
    "T256 round 2 (C1): the knuckle collar on the lane end of the station rope": "ropechord:ropechord",
    "T256 round 2 (C2): the nose boom's knee brace, into the boom and the front portal beam": "beam:holdbrace holdboom:holdbrace",
}
_RIG_SAVED = {}


def set_m_rig(on, kind=None):
    """T256: switch the rigged build on (main_m --rig) or back to the shipped tables. On: M_SPECS anchor_in := anchor_out
    (the chord and everything that reads it start at the lane end), M_ROPE_R = RIG_ROPE_R, M_NOSE_TRAIN on the drive (the
    base has no hosted end tower), the entry sheaves join the grip-OK and machine classes of M3. Off restores every table.
    Idempotent."""
    global M_RIG, M_ROPE_R, M_NOSE_TRAIN, M_GRIP_OK, M_MACHINE
    if not _RIG_SAVED:
        _RIG_SAVED.update(rope_r=M_ROPE_R, nose=M_NOSE_TRAIN, grip=M_GRIP_OK, machine=M_MACHINE)
    M_RIG = bool(on)
    if M_RIG:
        if M_ROPE_ON_CROWN:
            raise SystemExit("--rig and --rope-on-crown do not combine (the rig's datum is the shipped lane end)")
        for k, s in M_SPECS_SHIPPED.items():
            M_SPECS[k] = _dc.replace(s, anchor_in=s.anchor_out)
        M_ROPE_R = RIG_ROPE_R
        M_NOSE_TRAIN = kind == "drive"
        M_GRIP_OK = tuple(_RIG_SAVED["grip"]) + RIG_GRIP_OK
        M_MACHINE = tuple(_RIG_SAVED["machine"]) + RIG_GRIP_OK
    else:
        for k, s in M_SPECS_SHIPPED.items():
            M_SPECS[k] = s
        M_ROPE_R, M_NOSE_TRAIN = _RIG_SAVED["rope_r"], _RIG_SAVED["nose"]
        M_GRIP_OK, M_MACHINE = _RIG_SAVED["grip"], _RIG_SAVED["machine"]


M_HOLD_MAT ={"paint": "paint1", "galv": "galv", "rubber": "rubber",   # tower key -> M key (Pali black steel)
              "wheelsteel": "wheelsteel"}                                # T62 round 2: cs2_sheaves.WHEEL_KEY
M_CW_SHEAVE_W = 0.10


def hold_xc_m(spec):
    """The center x of the hold-down train on the chord: M_HOLD["xc"], or with M_NOSE_TRAIN the x that puts the rim of the outer
    (line-side) sheave M_NOSE["rim_gap"] inboard of anchor_in (wheel center = O + u a + n axle_z, a = the outer wheel of the
    outer sub). T256 (M_RIG): the main pin on the lane end, anchor_out x."""
    if M_RIG:
        return spec.anchor_out[0]
    if not M_NOSE_TRAIN:
        return M_HOLD["xc"]
    T = hold_train_layout_m(spec)
    _O, u, nv, _M = hold_train_frame_m(spec, 0.0)
    a_out = -(T["main_span"] / 2.0 + T["sub_span"] / 2.0)
    want = spec.anchor_in[0] + M_NOSE["rim_gap"] + T["r"]          # the outer wheel's center x
    return round(want - u.x * a_out - nv.x * T["axle_z"], 4)


def hold_train_frame_m(spec, xc):
    """The frame of the hold-down train: origin O on the code chord at authoring x = xc, u = the unit direction of the chord
    (toward the wheel, +x, downhill), n = its upward normal (u x n = -y... a proper rotation about y: local x -> u, z -> n).
    Returns (O, u, n, M) with M the 4x4 local -> authoring matrix."""
    import mathutils
    ax, az = spec.anchor_in
    wx, _wy, wz = spec.wheel
    u = mathutils.Vector((wx - ax, 0.0, wz - az)).normalized()
    nv = mathutils.Vector((-u.z, 0.0, u.x))
    O = mathutils.Vector((xc, 0.0, chord_z(spec, xc)))
    M = mathutils.Matrix(((u.x, 0.0, nv.x, O.x), (0.0, 1.0, 0.0, 0.0), (u.z, 0.0, nv.z, O.z), (0.0, 0.0, 0.0, 1.0)))
    return O, u, nv, M


def hold_train_layout_m(spec):
    """The hold-down train numbers (local chord frame: x along the rope, z off the rope axis)."""
    import cs2_sheaves as S
    H = M_HOLD
    sh = S.FG_SHEAVE
    r, w, pitch = sh["r"], sh["w"], sh["pitch"]
    n_sub, sub_span, main_span = S.fg_spans(H["n"], pitch, r)
    axle_z = r + M_ROPE_R                         # the sheave bottoms on the rope's top (hold-down: rope underneath)
    sub_z = axle_z + S.FG_SUB_TO_AXLE
    main_z = sub_z + S.FG_MAIN_TO_SUB
    arm_c = main_z + S.FG_HANGER                  # the boom's center (the tower crossarm's)
    return dict(n=H["n"], r=r, w=w, pitch=pitch, n_sub=n_sub, sub_span=sub_span, main_span=main_span,
                axle_z=axle_z, sub_z=sub_z, main_z=main_z, arm_c=arm_c, arm_t=S.FG_ARM_T,
                half_len=round(main_span / 2.0 + sub_span / 2.0 + r, 6))


def build_hold_train_m(spec):
    """The hold-down trains of the base (M_HOLD_TRAIN): per lane, cs2_sheaves.fg_train_side in the chord frame (finished as the
    towers are), its box boom along the rope and the saddle of the boom on the front portal beam. [(obj, M key)]."""
    import cs2_sheaves as S
    B, H = M_BASE, M_HOLD
    T = hold_train_layout_m(spec)
    _O, u, nv, M = hold_train_frame_m(spec, hold_xc_m(spec))
    pre = H["prefix"]
    bf = B["beam_front"]
    out = []
    for tag, s in (("n", -1.0), ("p", 1.0)):
        y = s * FG_LANE
        raw = S.fg_train_side(tag, s, y, T["main_z"], T["sub_z"], T["axle_z"], T["n_sub"], T["sub_span"],
                              T["main_span"], T["r"], T["w"], prefix=pre)
        S.finish_train(raw, "FG", prefix=pre)
        for o, k in raw:
            o.data.transform(M)
            o.data.update()
            out.append((o, M_HOLD_MAT[k]))
        # the boom: the 0.22 depth of the crossarm, boom_w wide, along the rope from boom_rear (behind the hanger) to
        # boom_front_x (its center line), in the chord frame
        a1 = (H["boom_front_x"] - M[0][3] - nv.x * T["arm_c"]) / u.x
        if M_NOSE_TRAIN:           # art-601 round 2: a slim tube (authoring endpoints through M: no frame offset)
            boom = _mcyl(f"holdboom_{tag}", M @ _V((H["boom_rear"], y, T["arm_c"])), M @ _V((a1, y, T["arm_c"])),
                         M_NOSE["tube_r"], M_NOSE["tube_segs"], "both")
        else:
            boom = _mbox(f"holdboom_{tag}", H["boom_rear"], a1, y - H["boom_w"] / 2.0, y + H["boom_w"] / 2.0,
                         T["arm_c"] - T["arm_t"] / 2.0, T["arm_c"] + T["arm_t"] / 2.0)
            boom.data.transform(M)
        boom.data.update()
        out.append((boom, "paint1"))
        # its saddle: from 1 cm inside the top of the front beam up 2 cm into the underside of the boom (the higher, rear
        # end)
        sx0, sx1 = H["saddle_x"]
        zb = T["arm_c"] - T["arm_t"] / 2.0          # the boom underside in the chord frame
        if M_NOSE_TRAIN:
            zb = T["arm_c"] - M_NOSE["tube_r"]      # the tube's underside

        def under(x):
            a = (x - M[0][3] - nv.x * zb) / u.x
            return M[2][3] + u.z * a + nv.z * zb
        top = max(under(sx0), under(sx1)) + 0.02
        out.append((_mbox(f"holdhang_{tag}", sx0, sx1, y - H["saddle_w"] / 2.0, y + H["saddle_w"] / 2.0,
                          bf[3] - 0.01, top), "paint1"))
        if M_NOSE_TRAIN:
            out += _nose_stay_m(tag, y, M, u, nv, T, a1, bf)
    return out


def _bake_loc(obj):
    """Move the location of an object into its mesh (object at the origin). A box from _mbox keeps its center on the object, so
    data.transform(M) alone maps only the center-relative mesh. The part then lands (I - R) c off the chord frame (about 0.2
    m on the shipped hold boom, kept byte for byte when M_NOSE_TRAIN is off)."""
    from mathutils import Matrix
    obj.data.transform(Matrix.Translation(obj.location))
    obj.location = (0.0, 0.0, 0.0)


def _cuboids_bm(boxes):
    """One bmesh of axis-aligned cuboids [(x0, x1, y0, y1, z0, z1)], outward normals."""
    from mathutils import Matrix
    bm = bmesh.new()
    for x0, x1, y0, y1, z0, z1 in boxes:
        bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Translation(((x0 + x1) / 2.0, (y0 + y1) / 2.0,
                                                                       (z0 + z1) / 2.0))
                              @ Matrix.Diagonal((x1 - x0, y1 - y0, z1 - z0, 1.0)))
    bm.normal_update()
    return bm


def _nose_stay_m(tag, y, M, u, nv, T, a1, bf):
    """M_NOSE_TRAIN round 2: the fittings of the slim arm, after the owner's reference photo (the hold-down arm of a real FG
    base: a slim tube cantilever with small brackets). Chord frame through M:
    - the hanger clamp at a 0 (around the top of the train's hanger) with a lifting tab on top (holdclamp_)
    - a thin gusset tab on the top of the tube at the root, deep at the root and running out toward the hanger (holdweb_)

    Authoring, axis-aligned: two thin cheek plates either side of the tube on the saddle, from the top of the front beam to
    just over the tube (holdplate_). [(obj, M key)]."""
    H, N = M_HOLD, M_NOSE
    c, r = T["arm_c"], N["tube_r"]
    cl, cw, cdn, cup = N["clamp"]
    tl, tt, th = N["tab"]
    clamp = cs2_kit._new_obj(f"holdclamp_{tag}", _cuboids_bm([
        (-cl, cl, y - cw, y + cw, c - cdn, c + cup),
        (-tl, tl, y - tt, y + tt, c + cup - 0.01, c + cup + th)]))
    clamp.data.transform(M)
    clamp.data.update()
    zt = c + r * math.cos(math.pi / N["tube_segs"]) - 0.005     # just inside the tube's top flat
    ar, at, d, wt = a1, a1 - N["web_len"], N["web_d"], N["web_t"] / 2.0
    bm = bmesh.new()
    vs = [bm.verts.new(p) for p in ((ar, y - wt, zt), (ar, y + wt, zt), (ar, y + wt, zt + d),
                                    (ar, y - wt, zt + d), (at, y - wt, zt), (at, y + wt, zt))]
    for face in ((0, 3, 2, 1), (0, 4, 3), (1, 2, 5), (3, 4, 5, 2), (0, 1, 5, 4)):   # root, sides, slope, bottom (closed)
        bm.faces.new([vs[i] for i in face])
    bm.normal_update()
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    web = cs2_kit._new_obj(f"holdweb_{tag}", bm)
    web.data.transform(M)
    web.data.update()
    sx0, sx1 = H["saddle_x"]
    xm = 0.5 * (sx0 + sx1)
    am = (xm - M[0][3] - nv.x * c) / u.x                        # the tube axis over the saddle's center
    ztop = (M @ _V((am, y, c))).z + r + N["cheek_up"]
    yi = r * math.cos(math.pi / N["tube_segs"]) - 0.002          # 2 mm into the tube's side flats: they clamp it
    plates = []
    for side, k in (("a", -1.0), ("b", 1.0)):
        y0, y1 = sorted((y + k * yi, y + k * (yi + N["cheek_t"])))
        plates.append((_mbox(f"holdplate_{tag}{side}", sx0 - N["cheek_over"], sx1 + N["cheek_over"], y0, y1,
                             bf[3] - 0.01, ztop), "paint1"))
    return [(clamp, "paint1"), (web, "paint1")] + plates


def hold_train_numbers_m(spec):
    """Manifest block `sheave_trains` (base): the tower head copied, counts, gauge, the chord frame, the sheave
    centers (authoring) and the rope tangent."""
    T = hold_train_layout_m(spec)
    O, u, nv, M = hold_train_frame_m(spec, hold_xc_m(spec))
    if M_RIG:       # T256: the half trains' bogies at +-(main_half + shift), the pin at local x 0 (the lane end)
        sc = T["main_span"] / 2.0 + RIG_NOSE["shift"]
        xs = [h * sc + d for h in (-1.0, 1.0) for d in (-T["sub_span"] / 2.0, T["sub_span"] / 2.0)]
        cents = [tuple(round(v, 4) for v in (M @ _V((a, 0.0, T["axle_z"]))).xz) for a in xs]
        return {"builder": "cs2_sheaves.fg_train_side_halves (the skinned towers' FG halves, beam + yoke straps) + "
                           "finish_train", "head": "FG (FG8: r 0.20, w 0.13, pitch 0.52)", "per_lane": T["n"],
                "lanes_y": [-FG_LANE, FG_LANE], "gauge_m": round(2.0 * FG_LANE, 3),
                "frame": "the code chord from the lane end (x along the rope, z its normal)",
                "chord_slope_deg": round(math.degrees(math.atan2(-u.z, u.x)), 3), "pin_x": hold_xc_m(spec),
                "pin": "on the rope axis at the lane end", "bones": {"NoseTrain": "outer half (line side)",
                                                                     "Root": "inner half, hanger, main pin"},
                "rig_nose": dict(RIG_NOSE), "sheave_centres_xz": cents,
                "contact": "sheave bottom on the rope's top (hold-down)"}
    xs = [(-T["main_span"] / 2.0 + i * (T["main_span"] / max(1, T["n_sub"] - 1)) if T["n_sub"] > 1 else 0.0) + d
          for i in range(T["n_sub"]) for d in (-T["sub_span"] / 2.0, T["sub_span"] / 2.0)]
    cents = [tuple(round(v, 4) for v in (M @ _V((a, 0.0, T["axle_z"]))).xz) for a in xs]
    import cs2_sheaves as S
    out = {"builder": "cs2_sheaves.fg_train_side (lift_tower's Yan rocker tree) + finish_train",
           "head": "FG (FG8: r 0.20, w 0.13, pitch 0.52)", "per_lane": T["n"], "lanes_y": [-FG_LANE, FG_LANE],
           "gauge_m": round(2.0 * FG_LANE, 3), "frame": "the code chord (x along the rope, z its normal)",
           "chord_slope_deg": round(math.degrees(math.atan2(-u.z, u.x)), 3), "centre_x": hold_xc_m(spec),
           "nose_train": ({"hosted_tower": "dropped (art-601): the import round removes the prefab's hosted row",
                           "outer_rim_inboard_of_anchor_in_m": M_NOSE["rim_gap"], "king_post_and_tie": True}
                          if M_NOSE_TRAIN else None),
           "sheave_centres_xz": cents, "contact": "sheave bottom on the rope's top (hold-down)",
           "counterweight": "head r 0.35 + deflection r 0.20 per rope: lite_sheave_parts (w 0.10, 12 segments)"}
    if S.wheels_v2():   # T62: the manifest of a v1 build stays as it was
        out["wheels"] = {"generation": "v2 (T62 round 3: cs2_sheaves.wheel, 16 segments, groove, seen hub bosses)",
                         "builder": S.WHEEL_ID, "tris_per_wheel_max": S.WHEEL_TRIS, "material": S.WHEEL_KEY,
                         "hold_down": "r 0.20 / w 0.13", "counterweight": "head r 0.35 + deflection r 0.20, w 0.10",
                         "lod": {str(k): v["form"] for k, v in S.WHEEL_LOD.items()}}
    return out


def _cw_sheave_m(name, r, loc):
    """A counterweight sheave as the wheel of the towers (cs2_sheaves.lite_sheave_parts: V-groove discs `name` + rubber liner
    `name`_liner, 12 segments, about the y axle). It has the same center / radius as the plain cylinder it replaces. T62 v2:
    cs2_sheaves.wheel (the same geometry: the 12-segment V wheel at r / M_CW_SHEAVE_W, stamped)."""
    import cs2_sheaves as S
    if S.wheels_v2():
        return [(o, M_HOLD_MAT[k]) for o, k in S.wheel(name, f"{name}_liner", r, M_CW_SHEAVE_W, loc)]
    return [(o, M_HOLD_MAT[k]) for o, k in S.lite_sheave_parts(name, f"{name}_liner", r, M_CW_SHEAVE_W, loc)]


def _sign_quads_m(x0, x1, y_wall, z0, z1):
    """signband_n / signband_p: ONE quad per house side, 0.012 proud (the DQ panel)."""
    out = []
    for s, tag in ((1.0, "p"), (-1.0, "n")):
        y = s * (y_wall + 0.012)
        out.append((_mquad(f"signband_{tag}", [(x0, y, z0), (x1, y, z0), (x1, y, z1), (x0, y, z1)],
                           (0.0, s, 0.0)), "signband"))
    return out


def build_base_m():
    """Generation M base (drive) station: the Yan Pali machine (PAL-01/03/04/05) on the K silhouette. Its parts:
    - four fat legs
    - a raised front portal over the code chord
    - twin 0.80 m tube rails
    - ONE corrugated house riding them on wheel discs
    - the open spoked bullwheel under the frame on the code datum
    - hold-down sheaves on the chord
    - the leaning counterweight mast and pot behind
    - a timber load deck with the carpet
    - the glazed operator hut at grade

    Returns [(obj, material key)] in a fixed order."""
    import cs2_liftdress as LD
    B = m_base()
    spec = M_SPECS["drive"]
    wx, _wy, wz = spec.wheel
    ph = B["pad_h"]
    parts = []
    x0, x1, py = B["pad"]
    parts.append((_mbox("pad", x0, x1, -py, py, 0.0, ph, open_bottom=True), "concrete"))

    # -- legs, flanges, gussets, portal beams ------------------------------------------------
    bf, bb = B["beam_front"], B["beam_back"]
    for tag, lx, ztop in (("f", B["front_x"], bf[2]), ("b", B["back_x"], bb[2])):
        for side, ly in (("n", -B["leg_y"]), ("p", B["leg_y"])):
            nm = f"leg_{tag}_{side}"
            parts.append((_mcyl(nm, (lx, ly, ph), (lx, ly, ztop), B["leg_r"], 16, "none"), "paint1"))
            parts += _kit_m(cs2_kit.flange(nm, 0.46, 0.04, bolts=8, segs=16, loc=(lx, ly, ph + 0.02),
                                           axis="Z", bolt_geo=True, caps="top"), "paint1", bolt_="galv")
            sgn = 1.0 if ly > 0 else -1.0
            for k, gx in enumerate((-0.15, 0.15)):
                parts += _kit_m(cs2_kit.gusset(f"{nm}_{k}", 0.50, 0.50, 0.02,
                                               loc=(lx + gx, ly - sgn * 0.27, ztop),
                                               rot=(math.pi, 0.0, -sgn * math.pi / 2.0)), "paint1")
    parts.append((_mbox("beam_front", bf[0], bf[1], -B["beam_y"], B["beam_y"], bf[2], bf[3]), "paint1"))
    parts.append((_mbox("beam_back", bb[0], bb[1], -B["beam_y"], B["beam_y"], bb[2], bb[3]), "paint1"))
    zc = (bf[2] + bf[3]) / 2.0
    for side, s in (("n", -1.0), ("p", 1.0)):
        # PAL-05: cylinder end caps on the portal box beam
        parts.append((_mcyl(f"beamdrum_{side}", (B["front_x"], s * B["beam_y"], zc),
                            (B["front_x"], s * (B["beam_y"] + 0.35), zc), 0.30, 16, "top"), "paint1"))
    # the raised emblem badge on the front beam's line face (PAL-05 oval, X11 square emblem)
    xf = bf[0]
    parts.append((_oval_plate("badge", (xf - 0.03, 0.0, zc), -1.0, 0.70, 0.42, 0.03), "darksteel"))
    e = M_BADGE_EMB / 2.0
    parts.append((_mquad("badge_emblem", [(xf - 0.033, e, zc - e), (xf - 0.033, -e, zc - e),
                                          (xf - 0.033, -e, zc + e), (xf - 0.033, e, zc + e)],
                         (-1.0, 0.0, 0.0)), "signband"))

    # -- main rails (Ø0.80) + flush caps + rings + lugs, end members, saddles ------------------
    ry, rr, rz = B["rail_y"], B["rail_r"], B["rail_z"]
    rxa, rxb = B["rail_x"]
    for side, s in (("n", -1.0), ("p", 1.0)):
        y = s * ry
        parts.append((_mcyl(f"rail_{side}", (rxa, y, rz), (rxb, y, rz), rr, 16, "none"), "paint1"))
        for end, ex, d in (("f", rxa, -1.0), ("b", rxb, 1.0)):
            # MC2: flush end caps (r <= the tube's 0.40)
            parts.append((_mcyl(f"railcap_{side}_{end}", (ex - d * 0.01, y, rz), (ex + d * 0.06, y, rz),
                                rr, 16, "both"), "paint1"))
            if end == "b" and not B["rear_lugs"]:
                continue        # T17: the rear lifting lugs would pierce the platform deck
            parts.append((_mbox(f"raillug_{side}_{end}", ex - d * 0.40 - 0.05, ex - d * 0.40 + 0.05,
                                y - 0.025, y + 0.025, rz + rr - 0.02, rz + rr + 0.14), "paint1"))
        for k, sx in enumerate(B["ring_x"]):
            parts += _kit_m(cs2_kit.stiffener_ring(f"rail_{side}_{k}", rr, w=0.06, t=0.02,
                                                   loc=(sx, y, rz), axis="X", segs=16), "paint1")
        # saddles: front - clamps from the rail top up into the beam. Back - blocks on the beam
        parts.append((_mbox(f"saddle_f_{side}", bf[0] - 0.05, bf[1] + 0.05, y - 0.30, y + 0.30,
                            rz + 0.22, bf[2] + 0.05), "paint1"))
        parts.append((_mbox(f"saddle_b_{side}", bb[0] - 0.05, bb[1] + 0.05, y - 0.30, y + 0.30,
                            bb[3] - 0.05, rz - 0.22), "paint1"))
    for tag, ex in (("f", -4.60), ("b", B["endmember_x"])):
        parts.append((_mbox(f"endmember_{tag}", ex - 0.175, ex + 0.175, -(ry + 0.15), ry + 0.15,
                            rz - 0.175, rz + 0.175), "paint1"))

    # -- tension carriage (rides the rails) + wheel shaft --------------------------------------
    parts.append((_mbox("carr_yoke", wx - 0.30, wx + 0.30, -ry, ry, 4.20, 4.62), "paint1"))
    for side, s in (("n", -1.0), ("p", 1.0)):
        parts.append((_mbox(f"carr_saddle_{side}", wx - 0.85, wx + 0.85, s * ry - 0.30, s * ry + 0.30,
                            4.35, rz - 0.20), "paint1"))
    parts.append((_mcyl("wheelshaft", (wx, 0.0, wz + 0.25), (wx, 0.0, 4.25), 0.17, 10, "none"), "paint1"))

    # -- the open spoked bullwheel on the code datum + the rope -------------------------------
    if M_RIG:       # T256: the 8-spoke wheel, the cable-width hexagon rope from the lane end, the entry sheaves
        parts += _rope_and_wheel_rig_m(spec) + entry_sheaves_rig_m("drive")
    else:
        parts += _wheel_m(wx, wz)
        parts.append((_half_torus("wheelrope", wx, wz, M_TORUS_R, M_ROPE_R), "rope"))
        for side, s in (("n", -1.0), ("p", 1.0)):
            ax, az = spec.anchor_in
            parts.append((_mcyl(f"ropechord_{side}", (ax, s * FG_LANE, az), (wx, s * FG_LANE, wz),
                                M_ROPE_R, 8, "none"), "rope"))

    # -- hold-down sheaves on the chord (PAL-05 boom), hung from the front beam (X12) ---------
    # nitpick A (2026-10-13, S-2): the towers' Yan rocker tree (cs2_sheaves.fg_train_side, M_HOLD_TRAIN)
    # replaces the four plain cylinders: build_hold_train_m
    if M_RIG:       # T256: the articulated nose train, its pin on the lane end
        parts += build_nose_rig_m(spec)
    elif M_HOLD_TRAIN:
        parts += build_hold_train_m(spec)
    else:
        for side, s in (("n", -1.0), ("p", 1.0)):
            y = s * FG_LANE
            for k, sx in enumerate((-4.85, -4.45, -4.05, -3.65)):
                c = (sx, y, chord_z(spec, sx) + M_ROPE_R + 0.20)
                parts.append((_mcyl(f"sheave_hold_{side}_{k}", (c[0], y - 0.05, c[2]), (c[0], y + 0.05, c[2]),
                                    0.20, 10, "both"), "galv"))
            za, zb = chord_z(spec, -5.00) + 0.42, chord_z(spec, B["front_x"]) + 0.42
            boom = _mcyl(f"holdboom_{side}", (-5.00, y, za), (B["front_x"], y, zb), 0.06, 6, "both")
            parts.append((boom, "paint1"))
            parts.append((_mbox(f"holdhang_{side}", B["front_x"] - 0.04, B["front_x"] + 0.04, y - 0.05,
                                y + 0.05, zb - 0.02, bf[2] + 0.01), "paint1"))

    # -- the house (one box, A6 / Q2): machine end solid, operator end glazed -----------------
    hx0, hx1, hw, hz0, hz1 = B["house"]
    parts.append((_mbox("house", hx0, hx1, -hw, hw, hz0, hz1), "paint1"))
    ro = B["roof_over"]
    roof = _mbox("houseroof", hx0 - ro, hx1 + ro, -(hw + ro), hw + ro, hz1, hz1 + B["roof_t"])
    parts.append((roof, "galv"))
    parts += _kit_m(cs2_kit.drip_edge(roof, drop=0.06, t=0.02), "galv")
    # windows on all four sides (M10): +-y operator end, the -x machine end, the +x end
    panes = []
    for side, s in (("p", 1.0), ("n", -1.0)):
        for k, (a, b) in enumerate(((2.20, 3.00), (3.20, 3.80))):
            panes.append((f"{side}{k}", "y", s * hw, s, a, b, 6.25, 7.10))
    panes.append(("mx", "x", hx0, -1.0, -0.60, 0.60, 6.45, 7.05))
    for k, (a, b) in enumerate(((-1.00, -0.10), (0.10, 1.00))):
        panes.append((f"px{k}", "x", hx1, 1.0, a, b, 6.25, 7.10))
    for tag, plane, c, n, a, b, v0, v1 in panes:
        parts.append((_pane(f"cabwin_{tag}", plane, c, n, a, b, v0, v1), "glass"))
        parts.append((_frame_ring(f"winframe_{tag}", plane, c, n, a, b, v0, v1), "darksteel"))
    parts += _kit_m(cs2_kit.door("house", w=0.85, h=1.90, reveal=0.04, loc=(1.175, hw, hz0),
                                 facing="+Y"), "paint1", door_house_jamb="galv", door_house_head="galv",
                    door_house_handle="galv", door_house_kick="galv")
    parts.append((_mquad("doorlite_house", [(0.95, hw + 0.025, 6.55), (1.40, hw + 0.025, 6.55),
                                           (1.40, hw + 0.025, 7.00), (0.95, hw + 0.025, 7.00)],
                         (0, 1, 0)), "glass"))
    parts += _kit_m(cs2_kit.hatch("vent", 1.10, 0.60, reveal=0.03, loc=(1.35, -hw, 6.30),
                                  facing="-Y"), "galv")
    parts += _sign_quads_m(-1.20, 0.55, hw, 6.35, 6.35 + sign_layout_m()["H"])
    for k, (dx, side) in enumerate(((-0.90, "n"), (-0.90, "p"), (4.10, "n"), (4.10, "p"))):
        s = 1.0 if side == "p" else -1.0
        parts.append((_mcyl(f"wheeldisc_{k}", (dx, s * (hw + 0.0), hz0 + 0.25),
                            (dx, s * (hw + 0.08), hz0 + 0.25), 0.30, 16, "both"), "darksteel"))
    zr = hz1 + B["roof_t"]
    # Pali J exhaust + antenna on the roof (PAL-01 / PAL-05)
    parts.append((cs2_kit.tube_along("exhaust", [(3.60, -0.90, zr - 0.02), (3.60, -0.90, zr + 0.60),
                                                 (3.15, -0.90, zr + 0.60)], 0.06, segments=6,
                                     caps="both", bend_rings=4, fillet=0.15), "galv"))
    parts.append((_mcyl("exhausthat", (3.10, -0.90, zr + 0.60), (3.02, -0.90, zr + 0.60), 0.09, 6), "galv"))
    parts.append((_mcyl("antenna", (-0.60, 0.60, zr - 0.02), (-0.60, 0.60, zr + 1.00), 0.03, 6, "top"), "galv"))
    for k, dy in enumerate((-0.12, 0.12)):
        parts.append((_mcyl(f"antwhip_{k}", (-0.60, 0.60, zr + 0.80), (-0.60, 0.60 + dy, zr + 1.30),
                            0.012, 4, "top"), "galv"))

    # -- catwalk (0.84 wide) on brackets from the +y rail, railing, ladders -------------------
    cx0, cx1, cy0, cy1 = B["catwalk"]
    parts += _kit_m(cs2_kit.grating("catwalk", cx1 - cx0, cy1 - cy0, t=0.06,
                                    loc=((cx0 + cx1) / 2.0, (cy0 + cy1) / 2.0, hz0 - 0.03)), "galv")
    rl = B["rail_line"]
    parts += _kit_m(cs2_kit.handrail("catwalk", [(cx1, rl, hz0), (cx0 + 0.04, rl, hz0),
                                                 (cx0 + 0.04, cy0 + 0.06, hz0)],
                                     h=1.10, knee=0.55, kick=0.10, post_every=1.5, segs=6,
                                     radius=0.028), "galv")
    for k, bx in enumerate(B["catbrk_x"]):
        parts.append((_mbox(f"catbrk_{k}", bx - 0.04, bx + 0.04, ry, cy1 - 0.05, hz0 - 0.20,
                            hz0 - 0.06), "galv"))
    # main ladder: pad -> catwalk at the back end (rung line x ladder_x: 5.48, T17 8.28, climber on +x)
    rungs = [round(0.60 + 0.30 * k, 2) for k in range(16)]
    parts += LD.fixed_ladder("main", line=(B["ladder_x"], 1.75), climb=(1.0, 0.0), rung_z=rungs,
                             rail_z=(ph, hz0 + 1.07), width=0.48, style="tram",
                             standoffs=[(rz, 0.40)])
    # roof ladder: catwalk -> house roof on the +y wall (climber over the catwalk, +y)
    roof_rungs = [round(hz0 + 0.30 + 0.30 * k, 2) for k in range(7)]
    parts += LD.fixed_ladder("roof", line=(4.15, 1.43), climb=(0.0, 1.0), rung_z=roof_rungs,
                             rail_z=(hz0, zr + 1.04), width=0.48, style="tram",
                             standoffs=[(6.00, 0.15), (7.20, 0.15)])
    if B["platform"]:
        # T17 (M_BASE_LOADROOM): the empty platform on the rail tops behind the house, one level with the catwalk (0.04 over
        # it), railed on its rear end (open at the head of the ladder, y > 1.20) and its -y side to the house
        px0, px1, py0, py1, ptop, pt = B["platform"]
        parts += _kit_m(cs2_kit.grating("catwalkdeck", px1 - px0, py1 - py0, t=pt,
                                        loc=((px0 + px1) / 2.0, (py0 + py1) / 2.0, ptop - pt / 2.0)), "galv")
        parts += _kit_m(cs2_kit.handrail("catwalkdeck", [(px1 - 0.04, 1.20, ptop), (px1 - 0.04, py0 + 0.04, ptop),
                                                         (px0 + 0.04, py0 + 0.04, ptop)],
                                         h=1.10, knee=0.55, kick=0.10, post_every=1.5, segs=6,
                                         radius=0.028), "galv")

    # -- tension scale plates on the rails' outboard faces (vector 1..16, one shared rect) ----
    for side, s in (("n", -1.0), ("p", 1.0)):
        y = s * (ry + rr + 0.012)
        xa, xb = -0.30, -0.30 + M_SCALE_LEN
        za, zb = rz - M_SCALE_H / 2.0, rz + M_SCALE_H / 2.0
        parts.append((_mquad(f"scale_{side}", [(xa, y, za), (xb, y, za), (xb, y, zb), (xa, y, zb)],
                             (0.0, s, 0.0)), "signband"))

    # -- counterweight: leaning box mast + pot behind the pad (A7 / Q3, PAL-04) ---------------
    fx0, fx1, fy, fh = B["cwfoot"]
    parts.append((_mbox("cwfoot", fx0, fx1, -fy, fy, 0.0, fh, open_bottom=True), "concrete"))
    (mfx, mfz), (mhx, mhz) = B["mast_foot"], B["mast_head"]
    d = _V((mhx - mfx, 0.0, mhz - mfz))
    mast = cs2_kit.add_box("cwmast", (0.45, 0.35, d.length), ((mfx + mhx) / 2.0, 0.0, (mfz + mhz) / 2.0),
                           rot=(0.0, math.atan2(d.x, d.z), 0.0))
    parts.append((mast, "paint1"))
    hz = mhz + 0.35
    # mast head: cap plate, a pedestal and the head-sheave axle across both ropes
    parts.append((_mbox("cwmast_cap", mhx - 0.28, mhx + 0.28, -0.24, 0.24, mhz - 0.06, mhz + 0.10),
                  "paint1"))
    parts.append((_mbox("cwmast_ped", mhx - 0.10, mhx + 0.10, -0.12, 0.12, mhz + 0.10, hz), "paint1"))
    parts.append((_mcyl("cwmast_axle", (mhx, -0.40, hz), (mhx, 0.40, hz), 0.06, 8), "galv"))
    for side, s in (("n", -1.0), ("p", 1.0)):
        y = s * 0.30
        if M_HOLD_TRAIN:
            # nitpick A (2026-10-13, S-2): the head + deflection sheaves of the counterweight are the wheels of the towers
            # (cs2_sheaves.lite_sheave_parts: V-groove discs + rubber liner, 12 segments), same centers / radii (the
            # counterweight ropes keep their tangent points), w 0.10
            parts += _cw_sheave_m(f"sheave_cwhead_{side}", 0.35, (mhx, y, hz))
            parts += _cw_sheave_m(f"sheave_cwdefl_{side}", 0.20, (B["defl_x"], y, 4.45))
        else:
            parts.append((_mcyl(f"sheave_cwhead_{side}", (mhx, y - 0.05, hz), (mhx, y + 0.05, hz), 0.35, 12),
                          "galv"))
            parts.append((_mcyl(f"sheave_cwdefl_{side}", (B["defl_x"], y - 0.05, 4.45),
                                (B["defl_x"], y + 0.05, 4.45), 0.20, 10), "galv"))
        yb = y + 0.05 if s > 0 else y - 0.08
        parts.append((_mbox(f"cwbrk_{side}", B["cwbrk_x"][0], B["cwbrk_x"][1], yb, yb + 0.03, 4.40, 4.80),
                      "paint1"))
        # rope: carriage yoke -> over the deflection sheave -> up over the head sheave -> pot
        pts = [(wx + 0.30, y, 4.65), (B["defl_x"], y, 4.65)]
        parts.append((_mcyl(f"cwrope_{side}_0", pts[0], pts[1], 0.025, 6, "none"), "rope"))
        a1 = (B["defl_x"] + 0.20 * math.sin(math.radians(30)), y, 4.45 + 0.20 * math.cos(math.radians(30)))
        b1 = (mhx - 0.35 * 0.40, y, hz + 0.35 * 0.92)
        parts.append((_mcyl(f"cwrope_{side}_1", a1, b1, 0.025, 6, "none"), "rope"))
        parts.append((_mcyl(f"cwcable_{side}", (mhx + 0.35, y, hz), (B["pot_x"] - 0.35 * 0.0 + 0.0, y,
                                                                      B["pot_z"][1] + 0.30), 0.025, 6,
                            "none"), "rope"))
    pz0, pz1 = B["pot_z"]
    parts.append((_mcyl("cwpot", (B["pot_x"], 0.0, pz0), (B["pot_x"], 0.0, pz1), B["pot_r"], 16, "both"),
                  "paint1"))
    parts.append((_mbox("cwpot_bail", B["pot_x"] - 0.04, B["pot_x"] + 0.04, -0.40, 0.40, pz1,
                        pz1 + 0.32), "paint1"))
    for k in range(6):
        z = 0.70 + 0.30 * k
        t_ = (z - mfz) / (mhz - mfz)
        xm = mfx + (mhx - mfx) * t_ - 0.225 - 0.06
        parts.append((_mcyl(f"wrung_cw_{k}", (xm, -0.20, z), (xm, 0.20, z), 0.016, 4, "both"), "galv"))

    # -- timber load deck + ramp, carpet, load line, seat marks (A2: cushion 0.47 over the carpet)
    dx0, dx1, dy0, dy1, dz = B["deck"]
    parts.append((_mbox("loaddeck", dx0, dx1, dy0, dy1, ph, dz, drop=("-Z",)), "timber"))
    ramp = cs2_kit.add_wedge("loadramp", dx1, B["ramp_x"], dy1 - dy0, dz - ph, 0.0)   # on the pad
    ramp.location = (0.0, (dy0 + dy1) / 2.0, ph)
    _drop_bottom(ramp)
    parts.append((ramp, "timber"))
    parts += LD.carpet("ld", -0.90, 1.30, 0.90, 2.70, z0=dz)
    zc_ = dz + 0.03
    parts += LD.stripe("load", (wx, 0.95), (wx, 2.65), 0.10, z=zc_)
    for k, yc in enumerate((1.20, 1.80, 2.40), start=1):
        parts += _tag_label_m(LD.flat_label(f"seat{k}", 0.30, 0.55, yc - 0.125, yc + 0.125, zc_,
                                            read_up=(-1.0, 0.0)), (0.0, 1.0, 0.0), (-1.0, 0.0, 0.0),
                              f"seat{k}")

    # -- operator hut at grade beside the load (glazed 3 sides, door +x, door lite) -----------
    hx0_, hx1_, hy0_, hy1_ = B["hut"]
    bx0, bx1, by0, by1, bh = B["hutbase"]
    parts.append((_mbox("hutbase", bx0, bx1, by0, by1, 0.0, bh, open_bottom=True), "concrete"))
    ht = bh + B["hut_h"]
    parts.append((_mbox("hut", hx0_, hx1_, hy0_, hy1_, bh, ht, drop=("-Z",)), "paint1"))
    for tag, plane, c, n, a, b in (("my", "y", hy0_, -1.0, -1.95, -0.45),
                                   ("mx", "x", hx0_, -1.0, 4.55, 5.75),
                                   ("py", "y", hy1_, 1.0, -1.90, -0.50)):
        parts.append((_pane(f"hutwin_{tag}", plane, c, n, a, b, 1.15, 2.05), "glass"))
        parts.append((_frame_ring(f"winframe_hut{tag}", plane, c, n, a, b, 1.15, 2.05), "darksteel"))
    parts += _kit_m(cs2_kit.door("hut", w=0.80, h=1.95, reveal=0.04, loc=(hx1_, 5.15, bh),
                                 facing="+X"), "paint1", door_hut_jamb="galv", door_hut_head="galv",
                    door_hut_handle="galv", door_hut_kick="galv")
    parts.append((_mquad("doorlite_hut", [(hx1_ + 0.025, 5.32, 1.40), (hx1_ + 0.025, 4.98, 1.40),
                                          (hx1_ + 0.025, 4.98, 1.85), (hx1_ + 0.025, 5.32, 1.85)],
                         (1, 0, 0)), "glass"))
    hroof = _mbox("hutroof", hx0_ - 0.10, hx1_ + 0.10, hy0_ - 0.10, hy1_ + 0.10, ht, ht + 0.08)
    parts.append((hroof, "galv"))
    parts += _kit_m(cs2_kit.drip_edge(hroof, drop=0.06, t=0.02), "galv")
    if FINE_DETAIL_M:
        # fine-detail step 3c (design B, benchmark 2.2: no bevel strips): the hidden top is dropped, no finish bevel
        # (assemble_m excludes `cabinet`), a 0.02 m lid overhangs 0.015 on every side (the lip of the detach cabinets)
        parts.append((_mbox("cabinet", -2.40, -1.50, -3.50, -3.00, ph, ph + 1.40, drop=("+Z",)), "galv"))
        L_ = FD_M_LID_LIP
        parts.append((_mbox("cabinet_lid", -2.40 - L_, -1.50 + L_, -3.50 - L_, -3.00 + L_, ph + 1.40,
                            ph + 1.40 + FD_M_LID_T), "galv"))
    else:
        parts.append((_mbox("cabinet", -2.40, -1.50, -3.50, -3.00, ph, ph + 1.40), "galv"))

    parts += _dress_base_m()
    return parts


def _drop_bottom(obj):
    """Delete the faces pointing -Z (a part standing ON another one: no buried bottom)."""
    me = obj.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.normal_update()
    kill = [f for f in bm.faces if f.normal.z < -0.9]
    bmesh.ops.delete(bm, geom=kill, context="FACES")
    bm.to_mesh(me)
    bm.free()
    return obj


def _wheel_m(cx, cz):
    """The open spoked bullwheel (PAL-10 / img_6900): groove-profile rim outer 1.74 (A4), 12 flat-bar spokes (A12), hub. Center
    = the code WheelLocal."""
    parts = _kit_m(cs2_kit.sheave("bull", M_RIM_OUT, 0.40, (cx, 0.0, cz), "Z", segs=32, hub=False,
                                  groove=0.04, r_hub=1.50), "paint1")
    for k in range(12):
        a = k * math.pi / 6.0
        sp = cs2_kit.add_box(f"spoke_{k:02d}", (1.32, 0.09, 0.055),
                             (cx + 0.96 * math.cos(a), 0.96 * math.sin(a), cz), rot=(0.0, 0.0, a))
        parts.append((sp, "galv"))
    parts.append((cs2_kit.add_cyl("wheelhub", 0.34, 0.62, (cx, 0.0, cz), segments=12), "galv"))
    return parts


# ---------------------------------------------------------------- T256 rig geometry (M_RIG) ---

def _hex_tube_m(name, p0, p1, r, sides=RIG_ROPE_SIDES):
    """An open straight tube of `sides` sides from p0 to p1 (no caps): the cable section, one vertex on the HORIZONTAL
    lateral (authoring y) and the flats on top / bottom (the vanilla LV cable's hexagon). The chord runs in an x-z plane, so
    e1 = +y is perpendicular to it. 2 * sides tris."""
    a, b = _V(p0), _V(p1)
    d = (b - a).normalized()
    e1 = _V((0.0, 1.0, 0.0))
    e1 = (e1 - d * e1.dot(d)).normalized()
    e2 = d.cross(e1)
    if e2.z < 0.0:
        e2 = -e2
    bm = bmesh.new()
    rings = []
    for c in (a, b):
        rings.append([bm.verts.new(c + e1 * (r * math.cos(2.0 * math.pi * k / sides))
                                   + e2 * (r * math.sin(2.0 * math.pi * k / sides))) for k in range(sides)])
    for k in range(sides):
        k1 = (k + 1) % sides
        f = bm.faces.new((rings[0][k], rings[0][k1], rings[1][k1], rings[1][k]))
        f.normal_update()
        cen = f.calc_center_median()
        axis = a + d * (cen - a).dot(d)
        if f.normal.dot(cen - axis) < 0.0:
            f.normal_flip()
    return _mbm_obj(name, bm)


def _hex_collar_m(name, p_cap, p_open, r, sides=RIG_ROPE_SIDES):
    """T256 round 2 (C1): a short tube of `sides` sides from p_cap to p_open, the section of _hex_tube_m (one vertex on the
    horizontal lateral), closed by one flat cap at p_cap only (the open end sits inside the station rope). 2 * sides + (sides
    - 2) tris."""
    a, b = _V(p_cap), _V(p_open)
    d = (b - a).normalized()
    e1 = _V((0.0, 1.0, 0.0))
    e1 = (e1 - d * e1.dot(d)).normalized()
    e2 = d.cross(e1)
    if e2.z < 0.0:
        e2 = -e2
    bm = bmesh.new()
    rings = [[bm.verts.new(c + e1 * (r * math.cos(2.0 * math.pi * k / sides))
                           + e2 * (r * math.sin(2.0 * math.pi * k / sides))) for k in range(sides)] for c in (a, b)]
    for k in range(sides):
        k1 = (k + 1) % sides
        f = bm.faces.new((rings[0][k], rings[0][k1], rings[1][k1], rings[1][k]))
        f.normal_update()
        cen = f.calc_center_median()
        axis = a + d * (cen - a).dot(d)
        if f.normal.dot(cen - axis) < 0.0:
            f.normal_flip()
    R0 = rings[0]
    for k in range(1, sides - 1):                  # the cap as explicit triangles (no n-gon)
        cap = bm.faces.new((R0[0], R0[k], R0[k + 1]))
        cap.normal_update()
        if cap.normal.dot(-d) < 0.0:
            cap.normal_flip()
    return _mbm_obj(name, bm)


def rig_knuckle_r_m():
    """OC-3 (critic-oc3 change 5): the knuckle collar's radius follows the station rope (RIG_KNUCKLE proud over M_ROPE_R)."""
    return M_ROPE_R + RIG_KNUCKLE["proud"]


def _wheel_rig_m(cx, cz):
    """T256: the bullwheel of the rigged stations. The O rim (the groove profile, outer 1.74, paint1) on RIG_WHEEL["spokes"]
    box spokes (0.10 wide x 0.16 deep, galv, from inside the hub to inside the rim), the O hub (galv) with a hub cap
    (wheelhub_cap, the boss on its top face). Round 2 (critic S3): ONE spoke (RIG_WHEEL["mark_spoke"]) in the pedestal's
    hazard yellow (safety_yellow) is the symmetry breaker that lets the turn read at play distance; it replaces round 1's
    galv plate on the rim top (spoke_mark, the same tone as the rim: too faint). Every part rides the Bullwheel bone
    (RIG_BULL_PARTS)."""
    W = RIG_WHEEL
    # OC-3 (critic-oc3 change 3, accepted as on O): at r 0.024 the rope ring's inner edge is 1.776, 36 mm outside this 1.74 rim
    # lip (P at r 0.060 sat on it). A rim at 1.776 failed the rig test: the entry sheaves cut the turning wheel's envelope by
    # 21-26 mm and the chair body cleared the rim by 1-6 mm (M3 wants 10). Shipped O has the same gap on a still wheel; a seated
    # rope needs the entry sheaves and the chair path moved (an art item, not this re-export)
    parts = _kit_m(cs2_kit.sheave("bull", M_RIM_OUT, 0.40, (cx, 0.0, cz), "Z", segs=32, hub=False,
                                  groove=0.04, r_hub=1.50), "paint1")
    r0, r1 = W["spoke_r"]
    sw, sd = W["spoke"]
    rm, L = (r0 + r1) / 2.0, r1 - r0
    for k in range(W["spokes"]):
        a = k * 2.0 * math.pi / W["spokes"]
        sp = cs2_kit.add_box(f"spoke_{k:02d}", (L, sw, sd), (cx + rm * math.cos(a), rm * math.sin(a), cz),
                             rot=(0.0, 0.0, a))
        parts.append((sp, "safety_yellow" if k == W["mark_spoke"] else "galv"))
    parts.append((cs2_kit.add_cyl("wheelhub", 0.34, 0.62, (cx, 0.0, cz), segments=12), "galv"))
    ch = W["cap_h"]
    parts.append((cs2_kit.add_cyl("wheelhub_cap", W["cap_r"], ch + 0.01, (cx, 0.0, cz + 0.31 + ch / 2.0 - 0.005),
                                  segments=12, caps="top"), "galv"))
    return parts


def _rope_and_wheel_rig_m(spec):
    """T256: the rigged wheel + the station rope on the carrier path: per lane the straight ropechord_<side> from the LANE END
    (anchor_in = anchor_out under M_RIG) to the wheel tangent (wx, +-1.8, wz), and the level half torus `wheelrope` of
    RIG_TORUS_MAJORS segments (ring centers on the code arc r M_TORUS_R about the wheel center). Both at M_ROPE_R (=
    RIG_ROPE_R), RIG_ROPE_SIDES sides, one vertex on the horizontal, material "rope" (near-black, as O)."""
    wx, _wy, wz = spec.wheel
    parts = _wheel_rig_m(wx, wz)
    parts.append((_half_torus("wheelrope", wx, wz, M_TORUS_R, M_ROPE_R, majors=RIG_TORUS_MAJORS,
                              minors=RIG_ROPE_SIDES), "rope"))
    ax, az = spec.anchor_in
    u = _V((wx - ax, 0.0, wz - az)).normalized()
    h = RIG_KNUCKLE["length"] / 2.0
    for side, s in (("n", -1.0), ("p", 1.0)):
        parts.append((_hex_tube_m(f"ropechord_{side}", (ax, s * FG_LANE, az), (wx, s * FG_LANE, wz), M_ROPE_R),
                      "rope"))
        # round 2 (C1): the knuckle, a rope collar centered on the lane end on the chord axis, capped on the line side
        # (it hides the V and the open tube end where the line rope kinks at the lane end); RIG_ROPE_PARTS by its prefix
        e = _V((ax, s * FG_LANE, az))
        parts.append((_hex_collar_m(f"ropechord_knuckle_{side}", e - u * h, e + u * h, rig_knuckle_r_m()), "rope"))
    return parts


def entry_center_m(spec):
    """The entry (hold-down) sheave center (x, z) over the tangent point (wx, wz) of either lane: on the bisector of the chord
    normal and the vertical, (r + rope) / cos(half the chord angle) out, so its rim touches BOTH the chord (lane end ->
    tangent) and the level arc's start (plan option W3)."""
    E = RIG_ENTRY
    ax, az = spec.anchor_in
    wx, _wy, wz = spec.wheel
    beta = math.atan2(az - wz, wx - ax)          # the chord's fall toward the wheel
    nv = (math.sin(beta), math.cos(beta))         # the chord's upward normal (x, z)
    bx, bz = nv[0], nv[1] + 1.0
    L = math.hypot(bx, bz)
    d = (E["r"] + M_ROPE_R) / math.cos(beta / 2.0)
    return (round(wx + d * bx / L, 6), round(wz + d * bz / L, 6))


def _tri_plate_m(name, pts, x0, x1):
    """T256 round 2: a triangular gusset plate, the triangle `pts` [(y, z) x 3] extruded from x0 to x1 (a closed prism, 8
    tris)."""
    bm = bmesh.new()
    vs = [[bm.verts.new((x, y, z)) for y, z in pts] for x in (x0, x1)]
    bm.faces.new(vs[0])
    bm.faces.new(vs[1][::-1])
    for k in range(3):
        k1 = (k + 1) % 3
        bm.faces.new((vs[0][k], vs[1][k], vs[1][k1], vs[0][k1]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return _mbm_obj(name, bm)


def entry_sheaves_rig_m(kind):
    """T256: an entry sheave per lane at the wheel's tangent point (cs2_sheaves.wheel r RIG_ENTRY["r"], w, named
    sheave_entry_<side>_rim / sheave_entry_liner_<side>), its rim on the rope where the chord meets the level wheel plane.
    It is held from OUTBOARD (the bullwheel rim is 1 cm inboard of its inner face, so only the outboard hub boss is built):
    an axle stub (sheave_entry_axle_<side>) through a cheek plate (entryarm_<side>_cheek) hung from a horizontal arm
    (entryarm_<side>), the cheek braced to the arm by a gusset (entryarm_<side>_gusset, round 2 C6: a load-bearing hanger).
    Base: the arm comes out of the tension carriage's saddle (carr_saddle_<side>) above the rope; the gusset sits in the
    arm / cheek corner on the inboard side. Top (round 2, critic S2; round 1 hung both arms from a post on the hub with a
    T-bar over the wheel): each arm runs in from an outboard post (entrypost_<side>, its base plate, its arm gusset and,
    round 2b, the crest stakes' black wrap entrypost_<side>_wrap) that
    stands on the crest at RIG_TOP_POST (x, +-y): outside the chairs' swept body (r <= 2.76 about the wheel axis + 0.20) and
    nowhere over the wheel or across a lane below the rope. The +y post takes the place of the crest maze's middle post
    (_dress_top_m). The arm, 1 cm above the sheave crest by ~0.3 m, is a few degrees off square (the post stands on the maze
    line, x 0.10; the sheave plane is x cx). Every part is static (Root)."""
    import cs2_sheaves as S
    spec = M_SPECS[kind]
    E = RIG_ENTRY
    wx, _wy, wz = spec.wheel
    cx, cz = entry_center_m(spec)
    hw = E["w"] / 2.0
    p = S.wheel_profile(E["r"], E["w"])
    boss_out = FG_LANE + hw + p["h"]                         # the outboard boss top |y|
    y_ch0 = boss_out + E["clear"]                           # the cheek's inner face
    y_ch1 = y_ch0 + E["cheek_t"]
    aw, ad = E["arm"]
    g, gt = E["gusset"]
    if kind == "drive":
        B = m_base()
        za0 = 4.36                                          # 1 cm above the saddle bottom (4.35)
        y_root = B["rail_y"] + 0.30 - 0.01                  # 1 cm inside the saddle's outboard face (|y| 1.40)
    else:
        za0 = 4.50
    za1 = za0 + ad
    out = []
    for tag, s in (("n", -1.0), ("p", 1.0)):
        y = s * FG_LANE
        wp = S.wheel(f"sheave_entry_{tag}_rim", f"sheave_entry_liner_{tag}", E["r"], E["w"], (cx, y, cz))
        wp[0][0]["cs2_wheel_bosses"] = "+" if s > 0 else "-"     # the outboard face only
        out += [(o, M_HOLD_MAT[k]) for o, k in wp]
        out.append((_mcyl(f"sheave_entry_axle_{tag}", (cx, s * (FG_LANE + hw - 0.01), cz),
                          (cx, s * (y_ch1 + 0.005), cz), E["stub_r"], 6, "both"), "galv"))
        if kind == "drive":
            ya, yb = sorted((s * y_root, s * y_ch1))
            out.append((_mbox(f"entryarm_{tag}", cx - aw / 2.0, cx + aw / 2.0, ya, yb, za0, za1), "paint1"))
            # the gusset in the arm / cheek corner, inboard of the cheek (above the sheave), 5 mm into both
            gy = (s * (y_ch0 + 0.005), s * (y_ch0 - g), s * (y_ch0 + 0.005))
        else:
            P = RIG_TOP_POST
            px, py, pr = P["x"], s * P["y"], P["r"]
            ztop = M_TOP["crest_z"]
            out.append((_mcyl(f"entrypost_{tag}", (px, py, ztop - 0.01), (px, py, za1 + 0.02), pr, P["segs"], "top"),
                        "paint1"))
            # round 2b (P2-S1): the crest stakes' black wrap on the post's lower part (stakewrap_: rubber, 0.02 off, open ends)
            wo, wz0, wz1 = P["wrap"]
            out.append((_mcyl(f"entrypost_{tag}_wrap", (px, py, ztop + wz0), (px, py, ztop + wz1), pr + wo, P["segs"],
                              "none"), "rubber"))
            pw, pt = P["plate"]
            out.append((_mbox(f"entrypost_{tag}_plate", px - pw / 2.0, px + pw / 2.0, py - pw / 2.0, py + pw / 2.0,
                              ztop - 0.005, ztop + pt, drop=("-Z",)), "paint1"))
            # the arm: from the post's axis to 5 mm inside the cheek's inner face, in plan a few degrees off square
            a = _V((px, py, 0.0))
            b = _V((cx, s * (y_ch0 + 0.005), 0.0))
            d = b - a
            mid = (a + b) / 2.0
            out.append((cs2_kit.add_box(f"entryarm_{tag}", (d.length, aw, ad), (mid.x, mid.y, (za0 + za1) / 2.0),
                                        rot=(0.0, 0.0, math.atan2(d.y, d.x))), "paint1"))
            # the post / arm gusset in the post's plane (x px): along the arm's underside and down the post's inboard face
            g2 = P["gusset"]
            yi = abs(py) - pr * 0.8
            out.append((_tri_plate_m(f"entrypost_{tag}_gusset",
                                     [(s * yi, za0 + 0.005), (s * (yi - g2), za0 + 0.005), (s * yi, za0 - g2)],
                                     px - gt / 2.0, px + gt / 2.0), "paint1"))
            # the arm / cheek gusset, outboard of the cheek (the arm comes from outboard)
            gy = (s * (y_ch1 - 0.005), s * (y_ch1 + g), s * (y_ch1 - 0.005))
        ya, yb = sorted((s * y_ch0, s * y_ch1))
        out.append((_mbox(f"entryarm_{tag}_cheek", cx - aw / 2.0, cx + aw / 2.0, ya, yb,
                          cz - E["stub_r"] - 0.035, za1), "paint1"))
        out.append((_tri_plate_m(f"entryarm_{tag}_gusset", [(gy[0], za0 + 0.005), (gy[1], za0 + 0.005),
                                                             (gy[2], za0 - g)], cx - gt / 2.0, cx + gt / 2.0),
                    "paint1"))
    return out


def rig_nose_parts_m(names):
    """The NoseTrain parts among `names`: the OUTER (line-side, local x < 0 = fg_train_side_halves half "b", bogie index 0)
    half of each lane's nose train. The hanger and the main pin stay on Root with the inner half."""
    pre = M_HOLD["prefix"]
    pat = _re.compile(rf"^{pre}(rocker_main_[np]_b|yoke_[np]_b|pivot_[np]_0$|rocker_sub_[np]_0$|sheave_arm_[np]_0_|"
                      rf"sheave_[np]_0_|sheave_hub[np]_0_|liner_[np]_0_|axlecap_stub_[np]_0_)")
    return sorted({n for n in names if pat.match(n)})


def build_nose_rig_m(spec):
    """T256: the base's articulated nose train. Per lane cs2_sheaves.fg_train_side_halves (the skinned towers' FG halves: two
    2-wheel bogies on half beams with yoke straps, lapping on the main pin) in the chord frame with the main pin ON the rope
    axis at the LANE END (local z 0 at x = anchor_out), the sheaves ABOVE the rope (bottoms on its top: a hold-down), the tree
    RIG_NOSE["inboard"] inboard of the wheels. The outer half (line side) rides NoseTrain, the inner half, the hanger and the
    pin are static. The hanger hangs from the art-601 slim tube boom (now out to the pin, at the hanger's |y|) with its clamp,
    gusset and cheek plates on the saddle on the front portal beam. [(obj, M key)]."""
    import cs2_sheaves as S
    B, H, N, R = M_BASE, M_HOLD, M_NOSE, RIG_NOSE
    T = hold_train_layout_m(spec)
    piv_x = spec.anchor_out[0]
    _O, u, nv, M = hold_train_frame_m(spec, piv_x)
    pre = H["prefix"]
    bf = B["beam_front"]
    out = []
    for tag, s in (("n", -1.0), ("p", 1.0)):
        y = s * FG_LANE
        raw = S.fg_train_side_halves(tag, s, y, T["main_z"], T["sub_z"], T["axle_z"], T["sub_span"],
                                     T["main_span"] / 2.0, T["r"], T["w"], pin_z=0.0, inboard=R["inboard"],
                                     shift=R["shift"], arm_t=S.FG_ARM_T, hanger=S.FG_HANGER, ins=S.FG_HANGER_INSET,
                                     gap=0.015, poke=0.006, pin_r=0.05, lap_gap=0.010, beam=True, prefix=pre,
                                     lap=R["lap"], pin_links=True)     # T256 S4: the bogie pins in their straps
        S.finish_train(raw, "FG", prefix=pre)
        hang = [o for o, _k in raw if o.name == f"{pre}hanger_{tag}"][0]
        hv = [hang.matrix_world @ v.co for v in hang.data.vertices]
        yh = 0.5 * (min(v.y for v in hv) + max(v.y for v in hv))
        for o, k in raw:
            o.data.transform(M)
            o.data.update()
            out.append((o, M_HOLD_MAT[k]))
        a1 = (H["boom_front_x"] - M[0][3] - nv.x * T["arm_c"]) / u.x
        boom = _mcyl(f"holdboom_{tag}", M @ _V((R["boom_tip"], yh, T["arm_c"])), M @ _V((a1, yh, T["arm_c"])),
                     N["tube_r"], N["tube_segs"], "both")
        boom.data.update()
        out.append((boom, "paint1"))
        sx0, sx1 = H["saddle_x"]
        zb = T["arm_c"] - N["tube_r"]

        def under(x):
            a = (x - M[0][3] - nv.x * zb) / u.x
            return M[2][3] + u.z * a + nv.z * zb
        top = max(under(sx0), under(sx1)) + 0.02
        if top < bf[3] + 0.01:
            raise RuntimeError(f"build_nose_rig_m: the boom's underside {top - 0.02:.3f} is not over the front beam "
                               f"{bf[3]} (the saddle would be inverted)")
        out.append((_mbox(f"holdhang_{tag}", sx0, sx1, yh - H["saddle_w"] / 2.0, yh + H["saddle_w"] / 2.0,
                          bf[3] - 0.01, top), "paint1"))
        out += _nose_stay_m(tag, yh, M, u, nv, T, a1, bf)
        # round 2 (critic C2: the slim boom read as an insect leg at play distance): a knee brace in the boom's plane, from
        # the boom's axis down to the front portal beam's line-side face (a triangle with the boom and the beam). Static
        NB = RIG_NOSE_BRACE
        out.append((_mcyl(f"holdbrace_{tag}", M @ _V((NB["a_top"], yh, T["arm_c"])),
                          (bf[0] + NB["foot_in"], yh, bf[2] + NB["foot_z"]), NB["r"], NB["segs"], "none"), "paint1"))
    return out


def _dress_base_m():
    """Base dressing (cs2_liftdress). The positions are the starting points of the design, moved per the skeptic: no flood masts
    (MC1, vanilla lamp post rows instead), SLOW on its own post."""
    import cs2_liftdress as LD
    B = m_base()
    ph = B["pad_h"]
    out = []
    up = (0.0, 0.0, 1.0)

    def wall_right(f):
        _n, r = LD._plan(f)
        return (r.x, r.y, 0.0)

    def board(nm, c, f, w, h, **kw):
        return _tag_label_m(LD.board(nm, c, f, w, h, **kw), wall_right(f), up, M_LABELS["drive"]["label_" + nm])

    # fine-detail step 3c (FINE_DETAIL_M): the maze / gate / post boards take the fine geometry of the kit builders (8-seg
    # capped posts, 6-sided ropes, 12-seg capped board posts). The e-stop, both extinguishers, the scraper + shovel and the
    # ski rack are dressing-kit rows (fd_kit_specs_m), not mesh
    fine = FINE_DETAIL_M
    if M_BASE_CORRAL:
        # queue maze (rope + flags) from the back toward the gate, the gate + WAIT HERE
        out += LD.maze("ld", [[(5.20, 0.90), (3.20, 0.90)], [(3.20, 1.60), (5.20, 1.60)]], z0=ph, fine=fine)
        out += LD.swing_gate("ld", hinge=(3.13, 0.90), latch=(3.13, 1.60), z0=ph, panel=True, fine=fine)
        out += board("waithere", (3.13 + 0.005 + 0.023, 1.25, ph + 0.535), "+X", 0.60, 0.35, mount="wall",
                     fine=fine)
    else:
        # nitpick A (2026-10-13, S-3: "The ladder and the corral fencing would get in the way of each other. I wouldn't worry
        # about corral fencing; the skiers could enter through the side."): no maze, no gate. The skiers walk straight onto
        # the load ramp. WAIT HERE stands on its own post beside the top of the ramp, off the +y (loading-lane) edge of the
        # deck, facing the skiers who come up the ramp (+X)
        wx_, wy_, wz_ = M_WAITHERE_POST
        out += board("waithere", (wx_, wy_, wz_), "+X", 0.60, 0.35, mount="post", post_z0=ph, fine=fine)
    # SKI TIPS UP on a post ahead of the departing chairs (riders face -x)
    out += board("tipsup", (-2.50, 3.25, 2.60), "+X", 0.70, 0.55, mount="post", post_z0=ph, fine=fine)
    # e-stop beside the load line, LIFT OPS on the hut's load-side wall
    if not fine:
        out += LD.estop_post("ld", (-1.25, 3.30), z0=ph)
    hx0, hx1, hy0, hy1 = B["hut"]
    out += board("ops", (-1.20, hy0 - 0.023, 0.15 + 2.18), "-Y", 1.20, 0.35, mount="wall", fine=fine)
    # SLOW on its own post for skiers coming in from the back (MC1: not on a flood mast)
    out += board("slow", B["slow"], "+X", 1.20, 0.35, mount="post", post_z0=ph, fine=fine)
    # tension gauge + extinguisher on the house's catwalk wall, extinguisher + tools at the hut
    hw, hz0 = B["house"][2], B["house"][3]
    out += _tag_label_m(LD.gauge("tension", (1.95, hw), "+Y", z=6.55), wall_right("+Y"), up, "tension")
    if not fine:
        out += LD.extinguisher("house", (2.60, hw), "+Y", z=hz0 + 0.06)
        out += LD.extinguisher("hut", (hx1, 5.80), "+X", z=0.95)
        # scraper + shovel against the cabinet's -y face (the hut walls are glazed)
        out += LD.tools("cab", foot=(-2.30, -3.82), facing="-Y", z0=ph)
        # ski rack on the -y pad edge (the A-frame gear, no bindings)
        out += LD.ski_rack("b", -1.00, 2.00, y=-3.40, z0=ph, pairs=5, boards=2, side=-1.0)
    return out


# ---------------------------------------------------------------- top station (return) ----

M_TOP = dict(
    crest_z=1.00, knoll_y=3.20, toe_x=-3.85, crest_x=(-1.10, 1.30), runout_x=3.30,
    # M18 (2026-10-12): the bay walk routes (crossing -> booth door, the W / back / E strips, the ledge) are >= 0.70 m wide
    # at hip height. The first build (bay -1.70..1.90 x -6.00..-3.20, booth -5.45..-3.85) left 0.39 m to the door and 0.17 m
    # in the W / back strips. The booth moved 0.12 m toward the back. The bay grew to a ~0.97 m clear strip on every side of
    # the booth (the rail stands 0.10 inside the edge). The dressing that stood on the routes left them (keys below).
    bay=(-1.90, 2.10, -6.65, -3.20),
    column=(1.00, 0.80), col_z=(1.00, 3.30), deck_z=(3.05, 3.30), deck_r=1.30,
    hut=(-0.80, 1.00, -5.57, -3.97), hut_h=2.30, slab=0.15,
    stakes=((-1.10, -3.05), (1.30, -3.05), (-1.10, 3.05), (1.30, 3.05)),
    # The dressing that M18 moved (first build in brackets):
    # - UNLOAD (-0.60, -3.30) and STAY (1.45, -3.35) stood on the bay boundary, and the e-stop post (1.00, -3.35) stood
    #   beside them. Together they formed a fence across the crest <-> ledge gateway. The boards now stand on grade beside
    #   the -y edge of the knoll on their own posts (like SLOW). board_z0 = the post foot (1.00 before). The e-stop post
    #   stands in the ledge in front of the crest-side wall of the booth, where the crest side stays open.
    # - The scraper + shovel (foot x, foot y, wall facing: (1.48, -5.25, "-X"), on the E rail beside the extinguisher) lean
    #   on the E rail in the SE corner pocket.
    # - The propane tank (dx, dy from the SW corner of the bay: (0.40, 0.45)) is tucked into the SW corner pocket.
    # - The stop arm bracket stands (dx, dy) from (bay x0, booth y1) ((0.30, -0.15)).
    # - The extinguisher on the door wall of the booth stands `ext` from the +y end of the booth (-1.45, near the back wall,
    #   before).
    #
    # 2026-10-12: the STAY board (it stood at (2.55, -3.50), past the unload line x 0) is gone with its key (M19 forbids it
    # on the top).
    unload=(-2.40, -3.50), board_z0=0.0,
    estop=(0.00, -3.42), tools=(1.68, -5.95, "-X"), propane=(0.32, 0.32), stoparm=(0.21, -0.15), ext=-0.15,
)


def build_top_m():
    """Generation M top (return) station: the Pali summit (PAL-09 / PAL-10). Its parts:
    - the knoll with the crest at 1.00 (A3: cushion 0.50 over it at the unload tangent) and a 20 deg up-ramp whose toe clears
      the hosted ET62O pad (M5)
    - the open spoked wheel on a stout rectangular column with a bearing deck
    - the liftie booth on a bay level with the crest on the UNLOAD side (A14 / S2)
    - timber stakes with black wraps + rope and flags (Q4 / Q5)
    - the sign on the bay face toward the arriving line"""
    import cs2_liftdress as LD
    T = M_TOP
    spec = M_SPECS["return"]
    wx, _wy, wz = spec.wheel
    cz, ky = T["crest_z"], T["knoll_y"]
    c0, c1 = T["crest_x"]
    parts = []
    parts.append((cs2_kit.add_wedge("rampup", T["toe_x"], c0, 2.0 * ky, 0.0, cz, open_bottom=True),
                  "concrete"))
    parts.append((cs2_kit.add_box("crest", (c1 - c0, 2.0 * ky, cz), ((c0 + c1) / 2.0, 0.0, cz / 2.0),
                                  open_bottom=True, drop=("+X", "-X")), "concrete"))
    parts.append((cs2_kit.add_wedge("rampdown", c1, T["runout_x"], 2.0 * ky, cz, 0.0, open_bottom=True),
                  "concrete"))
    bx0, bx1, by0, by1 = T["bay"]
    parts.append((_mbox("liftiebay", bx0, bx1, by0, by1, 0.0, cz, open_bottom=True), "concrete"))

    # -- column + base plate + bolts + capacity placard + welded maintenance rungs -------------
    cw, cd = T["column"]
    za, zb = T["col_z"]
    parts.append((_mbox("column", -cw / 2.0, cw / 2.0, -cd / 2.0, cd / 2.0, za, zb, drop=("-Z",)), "paint1"))
    parts.append((_mbox("colplate", -0.65, 0.65, -0.55, 0.55, cz, cz + 0.04, drop=("-Z",)), "paint1"))
    for k, (bx, by) in enumerate(((-0.58, -0.48), (0.0, -0.48), (0.58, -0.48), (0.58, 0.0),
                                  (0.58, 0.48), (0.0, 0.48), (-0.58, 0.48), (-0.58, 0.0))):
        parts.append((_mcyl(f"bolt_col_{k}", (bx, by, cz + 0.035), (bx, by, cz + 0.085), 0.025, 6, "top"),
                      "galv"))
    parts.append((_mquad("plate_cap", [(cw / 2.0 + 0.004, -0.175, 1.90), (cw / 2.0 + 0.004, 0.175, 1.90),
                                       (cw / 2.0 + 0.004, 0.175, 2.40), (cw / 2.0 + 0.004, -0.175, 2.40)],
                         (1, 0, 0)), "label"))
    for k in range(6):          # 1.30 .. 2.80: the bearing deck starts at 3.05
        z = 1.30 + 0.30 * k
        x = -cw / 2.0 - 0.12
        parts.append((_mcyl(f"wrung_col_{k}_bar", (x, -0.18, z), (x, 0.18, z), 0.016, 4, "both"), "galv"))
        for j, yy in enumerate((-0.16, 0.16)):
            parts.append((_mcyl(f"wrung_col_{k}_l{j}", (-cw / 2.0 - 0.005, yy, z), (x, yy, z), 0.012, 4,
                                "none"), "galv"))
    # -- bearing deck (crossed box beams, NOT walkable) + bearing housing ----------------------
    d0, d1 = T["deck_z"]
    R = T["deck_r"]
    parts.append((_mbox("bearingdeck_x", -R, R, -0.125, 0.125, d0, d1), "paint1"))
    parts.append((_mbox("bearingdeck_y", -0.125, 0.125, -R, R, d0, d1), "paint1"))
    parts.append((_mcyl("bearing", (wx, 0.0, d1 - 0.01), (wx, 0.0, wz - 0.30), 0.30, 12, "both"), "galv"))
    # hazard bands on the bearing-deck beam ends (painted chevrons as geometry)
    for k, (fac, c_, a_rng) in enumerate((("+X", R, (-0.125, 0.125)), ("-X", -R, (-0.125, 0.125)))):
        parts += LD.chevrons(f"deck{k}", fac, c=c_, a_rng=a_rng, b_rng=(d0 + 0.02, d1 - 0.02),
                             period=0.12, band=0.06)

    # -- wheel + rope ----------------------------------------------------------------------------
    if M_RIG:       # T256: the 8-spoke wheel, the hexagon rope from the lane end, the entry sheaves on the axle post
        parts += _rope_and_wheel_rig_m(spec) + entry_sheaves_rig_m("return")
    else:
        parts += _wheel_m(wx, wz)
        parts.append((_half_torus("wheelrope", wx, wz, M_TORUS_R, M_ROPE_R), "rope"))
        ax, az = spec.anchor_in
        for side, s in (("n", -1.0), ("p", 1.0)):
            parts.append((_mcyl(f"ropechord_{side}", (ax, s * FG_LANE, az), (wx, s * FG_LANE, wz),
                                M_ROPE_R, 8, "none"), "rope"))

    # -- the liftie booth on the bay (glazed toward the unload, the line and the back, door +x) -
    hx0, hx1, hy0, hy1 = T["hut"]
    sl = T["slab"]
    parts.append((_mbox("hutbase", hx0 - 0.15, hx1 + 0.15, hy0 - 0.15, hy1 + 0.15, cz, cz + sl,
                        drop=("-Z",)), "concrete"))
    h0, h1 = cz + sl, cz + sl + T["hut_h"]
    parts.append((_mbox("hut", hx0, hx1, hy0, hy1, h0, h1, drop=("-Z",)), "paint1"))
    for tag, plane, c, n, a, b in (("py", "y", hy1, 1.0, hx0 + 0.20, hx1 - 0.20),
                                   ("mx", "x", hx0, -1.0, hy0 + 0.20, hy1 - 0.20),
                                   ("my", "y", hy0, -1.0, hx0 + 0.20, hx1 - 0.20)):
        parts.append((_pane(f"hutwin_{tag}", plane, c, n, a, b, h0 + 0.90, h0 + 1.75), "glass"))
        parts.append((_frame_ring(f"winframe_hut{tag}", plane, c, n, a, b, h0 + 0.90, h0 + 1.75),
                      "darksteel"))
    yd = (hy0 + hy1) / 2.0
    parts += _kit_m(cs2_kit.door("hut", w=0.80, h=1.95, reveal=0.04, loc=(hx1, yd, h0), facing="+X"),
                    "paint1", door_hut_jamb="galv", door_hut_head="galv", door_hut_handle="galv",
                    door_hut_kick="galv")
    parts.append((_mquad("doorlite_hut", [(hx1 + 0.025, yd + 0.17, h0 + 1.25), (hx1 + 0.025, yd - 0.17, h0 + 1.25),
                                          (hx1 + 0.025, yd - 0.17, h0 + 1.70), (hx1 + 0.025, yd + 0.17, h0 + 1.70)],
                         (1, 0, 0)), "glass"))
    hroof = _mbox("hutroof", hx0 - 0.10, hx1 + 0.10, hy0 - 0.10, hy1 + 0.10, h1, h1 + 0.08)
    parts.append((hroof, "galv"))
    parts += _kit_m(cs2_kit.drip_edge(hroof, drop=0.06, t=0.02), "galv")
    # bay railing on its three outer sides (the crest side stays open: the crossing, X9)
    parts += _kit_m(cs2_kit.handrail("bay", [(bx0 + 0.10, by1 - 0.10, cz), (bx0 + 0.10, by0 + 0.10, cz),
                                             (bx1 - 0.10, by0 + 0.10, cz), (bx1 - 0.10, by1 - 0.10, cz)],
                                     h=1.10, knee=0.55, kick=0.10, post_every=1.5, segs=6,
                                     radius=0.028), "galv")
    # the guyed anemometer mast on the booth roof (A17, PAL-10), the propane cylinder on the bay
    xm, ym = (hx0 + hx1) / 2.0, yd
    zr = h1 + 0.08
    parts.append((_mcyl("wxmast", (xm, ym, zr - 0.02), (xm, ym, zr + 3.00), 0.03, 6, "top"), "galv"))
    parts.append((_mcyl("wxarm", (xm - 0.25, ym, zr + 2.85), (xm + 0.25, ym, zr + 2.85), 0.012, 4), "galv"))
    for k, dx in enumerate((-0.25, 0.25)):
        parts.append((_mcyl(f"wxcup_{k}", (xm + dx, ym, zr + 2.80), (xm + dx, ym, zr + 2.90), 0.035, 6),
                      "galv"))
    for k, (gx, gy) in enumerate(((hx0 - 0.05, hy0 - 0.05), (hx1 + 0.05, hy0 - 0.05),
                                  (hx1 + 0.05, hy1 + 0.05), (hx0 - 0.05, hy1 + 0.05))):
        parts.append((_mcyl(f"guy_{k}", (xm, ym, zr + 2.40), (gx, gy, zr), 0.006, 3, "none"), "galv"))
    parts.append((_mcyl("propane", (bx0 + T["propane"][0], by0 + T["propane"][1], cz), (bx0 + T["propane"][0], by0 + T["propane"][1], cz + 0.90),
                        0.18, 10, "top"), "galv"))
    parts.append((_mcyl("propane_cap", (bx0 + T["propane"][0], by0 + T["propane"][1], cz + 0.90),
                        (bx0 + T["propane"][0], by0 + T["propane"][1], cz + 1.02), 0.06, 6, "top"), "galv"))
    # the red / white stop-gate arm, STOWED upright in its bracket (X6: every in-game chair rides
    # through, so the arm never crosses the chair path)
    sx, sy = bx0 + T["stoparm"][0], hy1 + T["stoparm"][1]
    parts.append((_mbox("stoparm_brk", sx - 0.06, sx + 0.06, sy - 0.06, sy + 0.06, cz, cz + 0.30), "darksteel"))
    for k in range(6):
        parts.append((_mcyl(f"stoparm_{k}", (sx, sy, cz + 0.30 + 0.35 * k), (sx, sy, cz + 0.30 + 0.35 * (k + 1)),
                            0.03, 6, "top" if k == 5 else "none"),
                      "fire_red" if k % 2 == 0 else "trim_white"))
    # the sign on the bay's -x retaining face, toward the arriving line (one 1.750 m panel)
    lay = sign_layout_m()
    ys0 = (by0 + by1) / 2.0 + lay["W"] / 2.0 - 0.125     # centered on the W face, 0.125 m toward the back (the first build's offset)
    ys1 = ys0 - lay["W"]
    xs = bx0 - 0.012
    parts.append((_mquad("signboard", [(xs, ys0, 0.06), (xs, ys1, 0.06), (xs, ys1, 0.06 + lay["H"]),
                                       (xs, ys0, 0.06 + lay["H"])], (-1, 0, 0)), "signband"))

    # -- timber stakes with black wraps on the crest corners (A13 / Q4 / Q5) --------------------
    for k, (sx_, sy_) in enumerate(T["stakes"]):
        parts.append((_mbox(f"stake_{k}", sx_ - 0.09, sx_ + 0.09, sy_ - 0.09, sy_ + 0.09, cz - 0.05, cz + 3.00),
                      "timber"))
        parts.append((_mbox(f"stakewrap_{k}", sx_ - 0.11, sx_ + 0.11, sy_ - 0.11, sy_ + 0.11, cz + 0.10,
                            cz + 1.60, drop=("+Z", "-Z")), "rubber"))
    parts += _dress_top_m()
    return parts


def _dress_top_m():
    """Top dressing:
    - the unload line
    - UNLOAD on its own post on grade beside the -y edge of the knoll (M18: it stood in the crest <-> bay gateway and pinched
      the booth route to 0.39 m)
    - the e-stop post in the ledge in front of the booth
    - LIFT OPS on the booth
    - SLOW on its own post at the run-out (MC1)
    - the +y knoll edge maze
    - the extinguisher on the door wall of the booth
    - the scraper + shovel on the crest-side wall of the booth

    Every position that M18 cares about is an M_TOP key (the re-pinched negative of test_terminal_fg_m patches them back).
    2026-10-12: no STAY SEATED board (it stood past the unload line, M19 / M_FORBID)."""
    import cs2_liftdress as LD
    T = M_TOP
    cz = T["crest_z"]
    out = []
    up = (0.0, 0.0, 1.0)

    def wall_right(f):
        _n, r = LD._plan(f)
        return (r.x, r.y, 0.0)

    def board(nm, c, f, w, h, **kw):
        return _tag_label_m(LD.board(nm, c, f, w, h, **kw), wall_right(f), up, M_LABELS["return"]["label_" + nm])

    # fine-detail step 3c (FINE_DETAIL_M): fine maze / board posts. The e-stop, the extinguisher and the scraper + shovel are
    # dressing-kit rows (fd_kit_specs_m), not mesh
    fine = FINE_DETAIL_M
    out += LD.stripe("unload", (0.0, -2.70), (0.0, -0.90), 0.10, z=cz)
    out += board("unload", (T["unload"][0], T["unload"][1], 2.30), "-X", 0.70, 0.55, mount="post",
                 post_z0=T["board_z0"], fine=fine)
    if not fine:
        out += LD.estop_post("ul", T["estop"], z0=cz)
    hx0, hx1, hy0, hy1 = T["hut"]
    out += board("ops", ((hx0 + hx1) / 2.0, hy1 + 0.023, cz + T["slab"] + 2.03), "+Y", 1.20, 0.35,
                 mount="wall", fine=fine)
    out += board("slow", (3.60, -3.80, 1.80), "-X", 1.20, 0.35, mount="post", post_z0=0.0, fine=fine)
    run = [(-0.95, 3.05), (1.15, 3.05)]
    maze = LD.maze("crest", [run], z0=cz, fine=fine)
    if M_RIG:
        # T256 round 2 (S2): the +y entry post stands on the maze line in place of the maze's middle post (the ropes tie into
        # it). The run is 2.10 m with posts at most 1.40 apart: maze_post_crest_1 stands at its midpoint
        P = RIG_TOP_POST
        if abs((run[0][0] + run[1][0]) / 2.0 - P["x"]) > 1e-6 or abs(run[0][1] - P["y"]) > 1e-6:
            raise RuntimeError("_dress_top_m: RIG_TOP_POST is off the crest maze's middle post")
        gone = [o for o, _k in maze if o.name == "maze_post_crest_1"]
        if len(gone) != 1:
            raise RuntimeError("_dress_top_m: no maze_post_crest_1 to replace")
        maze = [(o, k) for o, k in maze if o is not gone[0]]
        bpy.data.objects.remove(gone[0], do_unlink=True)
    out += maze
    if not fine:
        out += LD.extinguisher("booth", (hx1, hy1 + T["ext"]), "+X", z=cz + T["slab"] + 0.60)
        tx, ty, tf = T["tools"]
        out += LD.tools("bay", foot=(tx, ty), facing=tf, z0=cz)
    return out


def subobjects_m(kind):
    """Vanilla sub-objects (cs2_liftdress.SUBOBJ, GUIDs verified against the dump): (prefab, authoring position, authoring
    facing or None, mount). MC1: the lamp posts replace the modeled flood masts. X14: wall lights instead of modeled lamps
    (no modeled lamp left). T17 lamp part (DECISIONS 247): the lamp post row is our SkiPisteFloodlightLow (M_FLOOD, a "mod"
    row at yaw 190, facing None)."""
    if kind == "drive":
        B = m_base()
        hw, hz1 = B["house"][2], B["house"][4]
        return [(M_FLOOD["prefab"], (5.60, -3.40, B["pad_h"]), None, "pad"),      # T17 lamp part: was LightpolePark -X
                ("TrashbinRandom01", (5.60, 2.70, B["pad_h"]), None, "pad"),
                ("WallLightShortRangeRandom01", (1.175, hw, 7.42), "+Y", "wall"),
                ("WallLightShortRangeRandom01", (B["hut"][1], 5.15, 2.40), "+X", "wall")]
    T = M_TOP
    yd = (T["hut"][2] + T["hut"][3]) / 2.0          # the booth door's y (the door sits on the +x wall)
    # T17 lamp part: was LightpolePark -Y at bay x1 + 0.50. The deployed floodlight row was nudged to game z 2.64 (review,
    # 2026-10-17: its 0.90 m base plate corner sat 0.021 m inside the +x wall of liftiebay at 2.60)
    return [(M_FLOOD["prefab"], (T["bay"][1] + 0.54, yd, 0.0), None, "grade"),
            ("WallLightShortRangeRandom01", (T["hut"][1], yd, 3.30), "+X", "wall")]


def subobject_rows_m(kind):
    """The surgery / manifest block (cs2_liftdress.sub_object_rows) in the GAME frame through
    m_to_game (S1). OPTIONAL and append-only at surgery (design section 6 step 4)."""
    import cs2_liftdress as LD
    rows = []
    for pf, pos, facing, _mount in subobjects_m(kind):
        if pf == M_FLOOD["prefab"]:
            # T17 lamp part (DECISIONS 247): the deployed floodlight row, a "mod" row in the slot of the vanilla rows
            rows.append({"kind": "mod", "ref": "CID", "m_Object": M_FLOOD["cid"], "prefab": pf,
                         "mesh": M_FLOOD["mesh"],
                         "position": m_to_game(pos), "rotation": M_FLOOD["rot"], "m_ParentMesh": 0,
                         "note": "replaces LightpoleParkRandom01 (DECISIONS 247); yaw 190, beam to the line"})
            continue
        rot = m_facing_quat(facing) if facing else (0.0, 0.0, 0.0, 1.0)
        rows.append((pf, m_to_game(pos), rot, 0))
    if FINE_DETAIL_M:
        # fine-detail step 3c: adds the "mod" rows of the dressing kit, in the own frame of M (M_X_CONVENTION, not the
        # station default string, which claims +y)
        return LD.sub_object_rows(M_SPECS[kind].building, rows + fd_kit_rows_m(kind), fine=True,
                                  x_convention=M_X_CONVENTION)
    # the floodlight is a mod row, so --no-fine writes the fine block format too (the DQ precedent, fine=bool(flood))
    return LD.sub_object_rows(M_SPECS[kind].building, rows, fine=True, x_convention=M_X_CONVENTION)


# ---------------------------------------------------------------- fine-detail step 3c: kit rows

_M_NORMAL = {"+X": (1.0, 0.0), "-X": (-1.0, 0.0), "+Y": (0.0, 1.0), "-Y": (0.0, -1.0)}


def m_dir_game(facing):
    """An AUTHORING facing ("+X" .. "-Y") -> the game-frame horizontal direction (dx, dz) through m_to_game (authoring (nx, ny)
    -> game (-ny, nx)). fine_detail.yaw_quat of it = m_facing_quat(facing)."""
    nx, ny = _M_NORMAL[facing]
    return (-ny + 0.0, nx + 0.0)


def m_host(G):
    """Game-local (N, 3) -> the AUTHORING frame (cs2_kitrows host): m_from_game = (gz, -gx, gy)."""
    G = np.asarray(G, dtype=np.float64)
    return np.stack([G[..., 2], -G[..., 0], G[..., 1]], axis=-1)


def fd_kit_specs_m(kind):
    """The dressing-kit rows of this station in the AUTHORING frame, one per removed builder call (design A.2 / D,
    build/dress-kit.md 3 pivots): dict(prop, pos, facing (authoring), mount, removed (old part prefixes), was [, ledge]). For
    ground: pos = the foot center on its floor. For wall: pos = the old mount point ON the wall face at the bottom of the
    prop (extinguisher: the old bottom z). For lean: pos = the wall (or ledge) face point at the floor, facing = its outward
    normal (fine_detail.kit_lean_row: the 0.3925 m foot-line rule). [] with FINE_DETAIL_M off."""
    if not FINE_DETAIL_M:
        return []
    if kind == "drive":
        B = m_base()
        ph = B["pad_h"]
        hw, hz0 = B["house"][2], B["house"][3]
        hx1 = B["hut"][1]
        cab = (-2.40, -1.50, -3.50, -3.00)          # the cabinet x0, x1, y0, y1 (build_base_m)
        rx0, rx1, ry = -1.00, 2.00, -3.40           # the old ski_rack("b", ...) run
        tools = {"+X": ((cab[1], (cab[2] + cab[3]) / 2.0, ph), "the cabinet's +x face (between it and the rack)"),
                 "-Y": (((cab[0] + cab[1]) / 2.0, cab[2], ph), "the cabinet's -y face")}[FD_M_BASE_TOOLS]
        return [dict(prop="SkiDressEStop", pos=(-1.25, 3.30, ph), facing="-Y", mount="ground",
                     removed=("estop_ld_",), was="estop_post('ld', (-1.25, 3.30)), z0 pad"),
                dict(prop="SkiDressExtinguisher", pos=(2.60, hw, round(hz0 + 0.06, 4)), facing="+Y", mount="wall",
                     removed=("ext_house",), was="extinguisher('house', (2.60, house +y wall), +Y, z hz0 + 0.06)"),
                dict(prop="SkiDressExtinguisher", pos=(hx1, 5.80, 0.95), facing="+X", mount="wall",
                     removed=("ext_hut",), was="extinguisher('hut', (hut +x wall, 5.80), +X, z 0.95)"),
                dict(prop="SkiDressTools", pos=tools[0], facing=FD_M_BASE_TOOLS, mount="lean", removed=("tool_cab_",),
                     was=f"tools('cab', foot (-2.30, -3.82), -Y): MOVED to {tools[1]} (the kit's 0.3925 m foot line "
                         f"off the -y face leaves the pad)" if FD_M_BASE_TOOLS != "-Y"
                     else "tools('cab', foot (-2.30, -3.82), -Y)"),
                dict(prop=FD_M_WALL_RACK, pos=(round((rx0 + rx1) / 2.0, 4), ry, ph), facing="-Y", mount="ground",
                     removed=("skirack_b_", "ski_b_"),
                     was="ski_rack('b', x -1.00 .. 2.00, y -3.40, side -1): frame + 5 pairs + 2 boards; the kit W2 "
                         "loadout holds 6 pairs + 1 board + 1 pole pair on the same 3.0 m frame")]
    T = M_TOP
    cz = T["crest_z"]
    hx0, hx1, hy0, hy1 = T["hut"]
    bx0, bx1, by0, by1 = T["bay"]
    tx, ty, tf = T["tools"]
    # the center of the builder pair (cs2_liftdress.tools lays the 2nd tool gap 0.35 to the right of `foot`): the kit pair
    # stands there (the M18 negatives that move M_TOP tools move it too)
    nx_, ny_ = _M_NORMAL[tf]
    tx, ty = round(tx - ny_ * 0.175, 4), round(ty + nx_ * 0.175, 4)
    rail = bx1 - 0.10 - 0.028                       # the E rail's inner face (handrail line bx1 - 0.10, tube r 0.028)
    return [dict(prop="SkiDressEStop", pos=(T["estop"][0], T["estop"][1], cz), facing="+Y", mount="ground",
                 removed=("estop_ul_",), was="estop_post('ul', M_TOP estop), z0 crest"),
            dict(prop="SkiDressExtinguisher", pos=(hx1, hy1 + T["ext"], round(cz + T["slab"] + 0.60, 4)), facing="+X",
                 mount="wall", removed=("ext_booth",), was="extinguisher('booth', booth +x (door) wall), +X"),
            dict(prop="SkiDressTools", pos=(rail, ty, cz), facing=tf, mount="lean", removed=("tool_bay_",),
                 tube=(1.10, 0.028),
                 was="tools('bay', foot M_TOP tools, -X): the builder pair's center, leaning on the E rail's top "
                     "tube (the rest plane behind the tube's inner face so the pair just touches the tube, "
                     "cs2_kitrows.kit_tube_offset)")]


def fd_kit_rows_m(kind, colour_set=None):
    """fine_detail.kit_row / kit_lean_row dicts in the GAME frame through m_to_game (game x = -authoring y), m_ParentMesh 0 like
    the vanilla rows of M. yaw = fine_detail.yaw_quat(m_dir_game(facing)). Colored props (the wall rack) name the color set
    of the kind (`colour_set` or FD_M_KIT_COLOUR[kind])."""
    import fine_detail as FD
    import cs2_kitrows as KR
    rows = []
    cset = colour_set or FD_M_KIT_COLOUR[kind]
    for s in fd_kit_specs_m(kind):
        note = f"replaces {s['was']}"
        d = m_dir_game(s["facing"])
        cs = cset if FD.is_coloured(s["prop"]) else None
        if s["mount"] == "lean":
            pos = s["pos"]
            if s.get("ledge") or s.get("tube"):
                n = _M_NORMAL[s["facing"]]
                off = KR.kit_ledge_offset(FD.KIT[s["prop"]].mesh, *s["ledge"]) if s.get("ledge") else \
                    KR.kit_tube_offset(FD.KIT[s["prop"]].mesh, *s["tube"])
                pos = (pos[0] - n[0] * off, pos[1] - n[1] * off, pos[2])
                note += f"; rest plane {off} m behind the {'ledge' if s.get('ledge') else 'rail tube'} face"
            wp = m_to_game(pos)
            rows.append(FD.kit_lean_row(s["prop"], wp, d, wp[1], parent=0, note=note, colour_set=cs))
        else:
            rows.append(FD.kit_row(s["prop"], m_to_game(s["pos"]), facing=d, parent=0, note=note, colour_set=cs))
    return rows


def fd_vanilla_boxes_m(kind):
    """[(label, lo, hi)] authoring: the variant-union boxes of the vanilla rows of M (m_subobj_box), with the mount face pulled
    0.01 off."""
    out = []
    for pf, pos, facing, mount in subobjects_m(kind):
        for k, (lo, hi) in enumerate(m_row_boxes(pf, pos, facing)):
            lo, hi = np.array(lo, dtype=np.float64), np.array(hi, dtype=np.float64)
            if mount in ("pad", "grade", "deck") and lo[2] <= pos[2] + 1e-6:
                lo[2] += 0.01
            elif mount == "wall" and facing:
                nx, ny = _M_NORMAL[facing]
                ax = 0 if nx else 1
                if (nx or ny) > 0:
                    lo[ax] += 0.01
                else:
                    hi[ax] -= 0.01
            out.append((f"{pf}@{pos[0]:.2f},{pos[1]:.2f}" + (f"#{k}" if k else ""), lo, hi))
    return out


def check_kit_rows_m(co, tris, names, table, kind, cloud=None):
    """F1 (fine-detail step 3c) on the AUTHORING-frame LOD0: cs2_kitrows.check_kit_rows with these inputs:
    - the hosted end tower as a keep-out (the shipped ET68O / ET62O LOD0 at its pose, + M_SEAM_CLEAR)
    - the swept 6M chair body cloud (M3) at >= M_CLEAR_DRESS
    - the vanilla row boxes of M and the lot (M_LOT_HALF - M_LOT_MARGIN)

    Returns ({}, None) when off."""
    import cs2_kitrows as KR
    specs = fd_kit_specs_m(kind)
    if not specs:
        return {}, None
    rows = fd_kit_rows_m(kind)
    hspecs = [dict(s, normal=_M_NORMAL[s["facing"]]) for s in specs]
    if M_NOSE_TRAIN and kind == "drive":
        keep = []                          # art-601: no hosted end tower on the nose-train base
    else:
        tw = load_tower_m(kind, 0)
        keep = [(f"hosted {os.path.splitext(os.path.basename(tw['path']))[0]} (+{M_SEAM_CLEAR})", tw["co"].min(axis=0) - M_SEAM_CLEAR,
                 tw["co"].max(axis=0) + M_SEAM_CLEAR)]
    hx, hy = M_LOT_HALF[0] - M_LOT_MARGIN, M_LOT_HALF[1] - M_LOT_MARGIN
    clouds = [("the swept 6M chair body (M3)", cloud, M_CLEAR_DRESS)] if cloud is not None and len(cloud) else []
    try:
        return KR.check_kit_rows(hspecs, rows, co, tris, names, m_host, all_names=table, keepouts=keep,
                                 clouds=clouds, vboxes=fd_vanilla_boxes_m(kind), lot=(-hx, hx, -hy, hy))
    except KR.KitRowError as e:
        raise MContractError(f"F1 {e}") from None


def review_rows_m(block):
    """The review pack's copy of the row block of M (fine-detail step 3c). Until 2026-10-16 this negated gx and mirrored the
    yaw, because cs2_subobj.game_to_pack assumed game x = +authoring y. The x-sign gate settled N (2026-10-15 playtest, S1b-N
    / T14). Since then game_to_pack is (-gx, -gz, gy) for every family, and the rows of M (m_to_game, gx = -authoring y) draw
    straight. No display copy is needed any more. It stays as the identity wrapper, so the manifest-shaped dict that the pack
    reader takes is unchanged."""
    return {"sub_objects": dict(block, objects=[dict(o) for o in block["objects"]])}


def fine_detail_block_m(kind, contracts):
    """The manifest's `fine_detail` block (None with FINE_DETAIL_M off)."""
    if not FINE_DETAIL_M:
        return None
    f1 = contracts.get("F1") or {}
    msg, nums = {"F1": f1.get("message")}, {"rows": f1.get("rows")}
    blk = subobject_rows_m(kind)
    kept = {"labels / LIFT boards": "FS5: the host atlas (" + ("WAIT HERE, SKI TIPS UP, LIFT OPS, SLOW, the seat marks, "
                                                               "the painted 1-16 tension scale, the badge, the sign"
                                                               if kind == "drive" else
                                                               "UNLOAD (no STAY SEATED: M19), LIFT OPS, SLOW, the capacity placard, "
                                                               "the sign") + ")",
            "maze / gate / board posts": "fine geometry (8-seg capped posts, 6-sided ropes, 12-seg capped posts)",
            "tension gauge": "modeled (no vanilla gauge)" if kind == "drive" else None,
            "cabinet": "PLACEHOLDER: ElectricalBox01 / 02 need the S1 Editor GUID harvest; fine: lid lip, no bevel"
                       if kind == "drive" else None,
            "weather mast": None if kind == "drive" else "FS1: the guyed anemometer mast stays",
            "propane / stop arm / stakes": None if kind == "drive" else "modeled (no vanilla match / M18 layout)"}
    return {"flag": True, "step": "3c", "report": "build/fine-detail-step3c-tram-pali.md",
            "x_convention": blk.get("x_convention"), "rows": blk.get("kinds"), "F1": msg.get("F1"),
            "kit_rows": (nums or {}).get("rows"),
            "removed": [{"prop": s["prop"], "removed": list(s["removed"]), "was": s["was"]} for s in fd_kit_specs_m(kind)],
            "lamps_to_vanilla": "none left: the wall lights are vanilla rows (X14); the lamp post row is our "
                                "SkiPisteFloodlightLow mod row (T17 lamp part, DECISIONS 247)",
            "kept_modelled": {k: v for k, v in kept.items() if v}}


# ---------------------------------------------------------------- assembly ----------------

M_SEED = "UVSeed"


def build_station_m(kind):
    parts = build_base_m() if kind == "drive" else build_top_m()
    names = [o.name for o, _ in parts]
    bad = sorted(n for n in names if "." in n)
    if bad or len(set(names)) != len(names):
        dup = sorted({n for n in names if names.count(n) > 1})
        raise RuntimeError(f"M part names collided: renamed {bad[:8]}, duplicates {dup[:8]}")
    if TEXPASS_M and IDENTITY_M:
        parts = identity_rekey_m(parts)       # r4/tex identity proposal: material keys only
    missing = sorted({m for _o, m in parts} - set(materials_for_m(parts)))
    if missing:
        raise RuntimeError(f"M parts use undefined materials {missing}")
    if any("binding" in n for n in names):
        raise RuntimeError("M12: a part named *binding* (no bindings on rack gear)")
    import cs2_sheaves as S
    S.cull_hidden_bosses(parts, label=f"FG M {kind}")    # T62 round 3: hidden hub bosses dropped (measured, v1 no-op)
    return parts


def seed_parts_m(parts, kind):
    """The pack SOURCE layer UVSeed of the bake (the rule of lift_terminal_detach.seed_parts). The pinned faces sit AT their
    reserved rect (sign panels, the badge emblem, the tension-scale plates, the labels). Every other part is smart-projected
    and moved out of [0, 1], so no island can fall inside a reserved rect and be pinned by accident."""
    LTD = _LTD()
    sign = sign_rect_m()
    sheet = label_sheet_m(kind)
    pinned = {"signband_n": sign, "signband_p": sign, "signboard": sign,
              "badge_emblem": emblem_rect_m(), "scale_n": scale_rect_m(), "scale_p": scale_rect_m()}
    for obj, _m in parts:
        obj.data.uv_layers.new(name=M_SEED, do_init=False)
        if obj.name in pinned:
            LTD._seed_sign(obj, pinned[obj.name])
            continue
        if obj.name in sheet:
            LTD._seed_label(obj, tuple(v / float(M_ATLAS) for v in sheet[obj.name]))
            continue
        LTD._seed_smart(obj)
        lay = obj.data.uv_layers[M_SEED].data
        uv = np.empty(len(lay) * 2, dtype=np.float32)
        lay.foreach_get("uv", uv)
        uv = uv.reshape(-1, 2)
        uv[:, 0] += LTD.SEED_OFFSET_U - np.floor(uv[:, 0].min())
        lay.foreach_set("uv", uv.ravel())


def assemble_m(parts, finish, kind):
    """bevels -> strip UVs -> density -> tag_parts -> seed -> material_sources -> join ->
    remove_doubles -> part table -> shade. The joined object stays in the AUTHORING frame
    (orient_for_net runs after the geometry contracts). Returns (t, table, sources)."""
    import cs2_bake
    import cs2_liftdress as LD
    if finish:
        # fine-detail step 3c: the cabinet carries a lid lip instead of the finish bevel
        apply_finish_bevels(parts, prefixes=M_BEVEL_PREFIXES,
                            exclude=M_BEVEL_EXCLUDE + (("cabinet",) if FINE_DETAIL_M else ()))
    for obj, _m in parts:
        me = obj.data
        while me.uv_layers:
            me.uv_layers.remove(me.uv_layers[0])
    cs2_uv.set_density(parts, {**LD.DRESS_DENSITY, **density_m()})
    table = cs2_asset.tag_parts(parts)
    bpy.context.view_layer.update()
    seed_parts_m(parts, kind)
    sources = cs2_bake.material_sources(materials_for_m(parts), parts)
    keys = sorted({str(m) for _o, m in parts})
    for pinned in ("signband", "label"):
        if pinned in keys:
            i = keys.index(pinned)
            sources[i] = _dc.replace(sources[i], uv=None)
    bpy.ops.object.select_all(action="DESELECT")
    for obj, _m in parts:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = parts[0][0]
    bpy.ops.object.join()
    t = bpy.context.active_object
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=0.0005)
    bpy.ops.object.mode_set(mode="OBJECT")
    cs2_asset.store_part_table(t, table)
    cs2_asset.shade_by_angle(t, M_SHADE)
    layers = sorted(l.name for l in t.data.uv_layers)
    if layers != sorted([cs2_bake.TILE_LAYER, M_SEED]):
        raise RuntimeError(f"M joined mesh UV layers {layers}, expected UVTile + {M_SEED}")
    t.data.uv_layers.active = t.data.uv_layers[cs2_bake.TILE_LAYER]
    print(f"[terminal M] assembled {len(table)} parts, {cs2_asset.tri_count(t)} tris, "
          f"{len(sources)} materials {keys}")
    return t, table, sources


# ---------------------------------------------------------------- contract helpers --------

class MContractError(RuntimeError):
    pass


def _m_fail(code, msg):
    raise MContractError(f"{code}: {msg}")


M_ORIENT = {   # M20: (part prefix, what) that stand on the UP lane's side, per kind. Boards: (prefix, rider facing)
    "drive": dict(parts=(("hut", "operator hut"), ("carpet_ld", "load carpet"), ("stripe_load", "load line"),
                         ("board_tipsup", "KEEP TIPS UP"), ("board_waithere", "WAIT HERE")),
                  facing={"label_tipsup": (1.0, 0.0), "label_waithere": (1.0, 0.0)}),
    "return": dict(parts=(("hut", "liftie booth"), ("liftiebay", "booth bay"), ("stripe_unload", "unload line"),
                          ("board_unload", "UNLOAD")),
                   facing={"label_unload": (-1.0, 0.0)}),
}


def _part_centre_m(co, tris, names, prefix):
    idx = [i for i, n in enumerate(names) if n.startswith(prefix)]
    if not idx:
        _m_fail("M20", f"no part {prefix}*")
    v = co[np.unique(tris[idx].ravel())]
    return (v.min(axis=0) + v.max(axis=0)) / 2.0, idx


def orientation_m(co, tris, names, kind):
    """M20 (nitpick A, 2026-10-13, twenty-fifth look S-1): the station is laid out around ONE up lane (UP_LANE_Y: the base LOADS
    the departing chairs on +y, the top UNLOADS the arriving chairs on -y. This is the same physical lane, the right one
    looking uphill in this authoring frame). The liftie's hut / booth, the load (unload) surface and line, and the rider
    boards stand on THAT side of the lane (center y sign). The lane boards face the riders (label normal = the opposite of
    the chairs' travel: the departing chairs of the base travel -x, the arriving ones of the top +x). Measured on the built
    faces. Returns the report."""
    L = UP_LANE_Y[kind]
    spec = M_ORIENT[kind]
    got, bad = {}, {}
    for prefix, what in spec["parts"]:
        c, _idx = _part_centre_m(co, tris, names, prefix)
        got[prefix] = round(float(c[1]), 3)
        if c[1] * L <= 0.0:
            bad[f"{prefix} ({what})"] = round(float(c[1]), 3)
    if bad:
        _m_fail("M20", f"not on the up lane's side (y sign {L:+.0f}): {bad}")
    face = {}
    for prefix, (nx, ny) in spec["facing"].items():
        _c, idx = _part_centre_m(co, tris, names, prefix)
        t0 = tris[idx[0]]
        n = np.cross(co[t0[1]] - co[t0[0]], co[t0[2]] - co[t0[0]])
        n = n / max(np.linalg.norm(n), 1e-12)
        face[prefix] = [round(float(n[0]), 3), round(float(n[1]), 3)]
        if n[0] * nx + n[1] * ny < 0.9:
            _m_fail("M20", f"{prefix} faces {face[prefix]}, the riders need ({nx:+.0f}, {ny:+.0f})")
    return {"up_lane_y_authoring": L, "measured_centre_y": got, "label_normals_xy": face, "ok": True,
            "frame": M_X_CONVENTION,
            "spin": "the up lane is the RIGHT one looking uphill in this frame (base +y loads, top -y unloads); "
                    "StationAlign.UpLaneIsSecond puts chain A on the right looking uphill IN GAME: the same lane "
                    "under probe X2 branch N (game x = -authoring y, M's own rows), the mirror under branch P"}


def sheave_trains_m(co, tris, names, kind):
    """M21 (nitpick A, 2026-10-13, S-2): the hold-down trains of the base are the rocker tree of the towers. The check has these
    rules:
    - Per lane, M_HOLD n rims. The lowest point of each rim is on the top of the code chord (rope tangent, 2 mm: the sheaves
      press the rope from above), centered on its lane (1 mm).
    - Every train part is above the rope and in front of the front face of the portal beam (the beam / house is never touched
      but by the saddle).
    - The counterweight wheels are on their old centers.

    The top station has no rope-guide wheel group besides its bullwheel (the hosted ET62O carries the entry train): reported."""
    if kind != "drive":
        return {"note": "no rope-guiding wheel group besides the bullwheel (the hosted ET62O carries the entry "
                        "train, a tower built by lift_tower itself)", "ok": True}
    if not M_HOLD_TRAIN:
        return {"note": "M_HOLD_TRAIN off (the pre-2026-10-13 cylinders)", "ok": True}
    spec = M_SPECS["drive"]
    T = hold_train_layout_m(spec)
    pre = M_HOLD["prefix"]
    rims = sorted({n for n in names if n.startswith(pre + "sheave_") and n.endswith("_rim")})
    per = {s: [n for n in rims if f"sheave_{s}_" in n] for s in ("n", "p")}
    if len(per["n"]) != T["n"] or len(per["p"]) != T["n"]:
        _m_fail("M21", f"rims per lane {[len(v) for v in per.values()]}, want {T['n']}")
    worst_t, worst_y = 0.0, 0.0
    _O, _u, nv, _M = hold_train_frame_m(spec, hold_xc_m(spec))
    for s, lst in per.items():
        ys = -FG_LANE if s == "n" else FG_LANE
        for nm in lst:
            c, _idx = _part_centre_m(co, tris, names, nm)
            # the distance of the rim center from the rope axis (the straight code chord) along the chord normal: the
            # vertical offset to the chord under it times n.z (the chord is a line: P0 = (x_c, chord_z(x_c)))
            d = float((c[2] - chord_z(spec, float(c[0]))) * nv.z)
            dt = abs(d - (T["r"] + M_ROPE_R))
            worst_t = max(worst_t, dt)
            worst_y = max(worst_y, abs(float(c[1]) - ys))
            if dt > 0.002:
                _m_fail("M21", f"{nm}: center {d:.4f} off the rope axis along its normal, want r + rope "
                               f"{T['r'] + M_ROPE_R:.4f} (the rope not tangent under the sheave)")
            if abs(float(c[1]) - ys) > 1e-3:
                _m_fail("M21", f"{nm} centered at y {c[1]:.4f}, lane {ys}")
    tp = [i for i, n in enumerate(names) if n.startswith(pre)]
    front = float(co[np.unique(tris[tp].ravel())][:, 0].max())
    bf = M_BASE["beam_front"]
    if front > bf[0] - 0.01:
        _m_fail("M21", f"the hold-down train reaches x {front:.3f}, past the portal beam's front face {bf[0]} - 0.01")
    for nm, r, c in (("sheave_cwhead_", 0.35, None), ("sheave_cwdefl_", 0.20, (m_base()["defl_x"], 4.45))):
        for s in ("n", "p"):
            cc, _ = _part_centre_m(co, tris, names, f"{nm}{s}")
            if c is not None and (abs(cc[0] - c[0]) > 1e-3 or abs(cc[2] - c[1]) > 1e-3):
                _m_fail("M21", f"{nm}{s} centered at {cc.round(3).tolist()}, want x {c[0]} z {c[1]}")
    tri_n = sum(1 for n in names if n.startswith((pre, "holdboom_", "holdhang_", "sheave_cw")))
    return dict(hold_train_numbers_m(spec), tangent_err_m=round(worst_t, 5), lateral_err_m=round(worst_y, 5),
                rims=len(rims), tris_lod0=tri_n, ok=True)


def board_forbid_m(names, kind):
    """M19 (2026-10-12, the DQ top's D11 `forbid`, lift_terminal_detach.assert_board_order): no part of a forbidden board kind
    (M_FORBID[kind]) in ANY mount. A part named board_<kind>* / label_<kind>* raises 'M19 the <kind> station carries a
    <WORDS> board (...)'. `names` = the part names (per triangle or a set). Returns {"forbidden": [{item, words}], "ok":
    True}."""
    import cs2_liftdress as LD
    forbid = tuple(M_FORBID.get(kind, ()))
    seen = sorted(set(names))
    for k in forbid:
        hit = [n for n in seen if n.startswith((f"board_{k}", f"label_{k}"))]
        if hit:
            _m_fail("M19", f"the {kind} station carries a {' '.join(LD.BOARDS[k]['words'])} board ({hit[:6]}); "
                           f"forbidden on this station in any mount")
    return {"forbidden": [{"item": k, "words": " ".join(LD.BOARDS[k]["words"])} for k in forbid], "ok": True}


def m_arrays(t, matrix=None):
    """(co (N, 3) float64, tris (T, 3) int, tri part names list, table) of a joined part-tagged mesh. co in world (= authoring
    before orient_for_net)."""
    me = t.data
    co = np.empty(len(me.vertices) * 3, dtype=np.float64)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    M = np.array(t.matrix_world if matrix is None else matrix, dtype=np.float64)
    co = co @ M[:3, :3].T + M[:3, 3]
    table = json.loads(me[cs2_asset.PART_TABLE_PROP])
    part = np.zeros(len(me.polygons), dtype=np.int32)
    me.attributes["part"].data.foreach_get("value", part)
    tris, names = [], []
    for pl, q in zip(me.polygons, part):
        v = list(pl.vertices)
        nm = table[q - 1] if 0 < q <= len(table) else "?"
        for j in range(1, len(v) - 1):
            tris.append((v[0], v[j], v[j + 1]))
            names.append(nm)
    return co, np.array(tris, dtype=np.int64), names, table


def _edge_points(co, tris, step):
    """Vertices + points every `step` along every edge of `tris`."""
    if not len(tris):
        return np.zeros((0, 3))
    e = np.sort(np.concatenate([tris[:, [0, 1]], tris[:, [1, 2]], tris[:, [2, 0]]]), axis=1)
    e = np.unique(e, axis=0)
    a, b = co[e[:, 0]], co[e[:, 1]]
    L = np.linalg.norm(b - a, axis=1)
    out = [co[np.unique(tris)]]
    for k in np.nonzero(L > step)[0]:
        n = int(math.ceil(L[k] / step))
        t_ = np.linspace(0.0, 1.0, n + 1)[1:-1, None]
        out.append(a[k] + (b[k] - a[k]) * t_)
    return np.concatenate(out)


def _bvh(co, tris):
    from mathutils.bvhtree import BVHTree
    if not len(tris):
        return None
    ids = np.unique(tris)
    remap = np.full(int(ids.max()) + 1, -1, dtype=np.int64)
    remap[ids] = np.arange(len(ids))
    return BVHTree.FromPolygons(co[ids].tolist(), remap[tris].tolist(), all_triangles=True)


def _min_dist(bvh, pts, limit):
    """Smallest distance from `pts` to `bvh` (capped at `limit`), and the nearest triangle."""
    best, best_i = limit, None
    if bvh is None:
        return best, None
    for p in pts:
        hit = bvh.find_nearest(_V(p), best)
        if hit[0] is not None and hit[3] < best:
            best, best_i = hit[3], hit[2]
    return best, best_i


# ---------------------------------------------------------------- the chair and the towers -

_CHAIR_CACHE = {}


def load_chair_m(level=0):
    """The shipped SkiLiftChair6M (LOD0 / LOD1) in the own AUTHORING frame of the chair (x travel = facing of the riders, y
    lateral, z up, pivot = lowest point): FBX raw (fx, fy, fz) cm -> (fz, fx, fy) m (the same S1 map: the X flip of the
    importer cancels between chair and station). Returns {"co", "tris", "body" (tri mask, z < CHAIR_BODY_Z), "lowest",
    "cushion"}."""
    if level in _CHAIR_CACHE:
        return _CHAIR_CACHE[level]
    import cs2_validate
    suf = "" if level == 0 else f"_LOD{level}"
    path = os.path.join(CHAIR_M_DIR, f"SkiLiftChair6M{suf}.fbx")
    objs = cs2_validate.import_fbx_temp(path)
    try:
        o = [x for x in objs if x.type == "MESH"][0]
        bm = bmesh.new()
        bm.from_mesh(o.data)
        bm.transform(o.matrix_world)
        bmesh.ops.triangulate(bm, faces=bm.faces[:])
        raw = np.array([v.co[:] for v in bm.verts], dtype=np.float64) / 100.0
        tris = np.array([[v.index for v in f.verts] for f in bm.faces], dtype=np.int64)
        bm.free()
    finally:
        cs2_validate.remove_objects(objs)
    co = np.stack([raw[:, 2], raw[:, 0], raw[:, 1]], axis=1)
    body = (co[tris][:, :, 2] < CHAIR_BODY_Z).all(axis=1)
    seat = co[(np.abs(co[:, 1]) < 0.70) & (co[:, 2] < 0.25)]
    out = {"co": co, "tris": tris, "body": body, "lowest": float(co[:, 2].min()),
           "cushion": float(seat[:, 2].max()), "top": float(co[:, 2].max()), "path": path}
    _CHAIR_CACHE[level] = out
    return out


_TOWER_CACHE = {}


def load_tower_m(kind, level=0):
    """The hosted end tower (ET68O base / ET62O top) at its hosted pose in the station's
    AUTHORING frame: FBX raw -> tower authoring (fz, fx, fy) m -> rotation 180 about up ->
    + (tower_x, 0, 0) (game (0, 0, -6.9 | -4.9), quat (0, 1, 0, 0))."""
    key = (kind, level)
    if key in _TOWER_CACHE:
        return _TOWER_CACHE[key]
    import cs2_validate
    spec = M_SPECS[kind]
    suf = "" if level == 0 else f"_LOD{level}"
    path = os.path.join(IMPORT_M, "towers", "O", spec.tower, f"{spec.tower}{suf}.fbx")
    tower_x = spec.tower_x
    if M_RIG and kind == "return":      # T256: the skinned hosted end tower (lift_tower --skinned --hosted-lane), TrainBack posed
        tower_x = RIG_HOSTED_X          # without --rig-hosted (F1's keep-out only): the shipped ET62O at the rig's row
        if RIG_HOSTED_DIR:
            nm = os.path.basename(os.path.normpath(RIG_HOSTED_DIR))
            path = os.path.join(RIG_HOSTED_DIR, f"{nm}{suf}.fbx")
    objs = cs2_validate.import_fbx_temp(path)
    back = None
    try:
        o = [x for x in objs if x.type == "MESH"][0]
        if M_RIG and kind == "return" and RIG_HOSTED_DIR:
            gi = {g.index: g.name for g in o.vertex_groups}
            back = np.array([any(gi.get(g.group) == "TrainBack" and g.weight > 0.5 for g in v.groups)
                             for v in o.data.vertices], dtype=bool)
        bm = bmesh.new()
        bm.from_mesh(o.data)
        bm.transform(o.matrix_world)
        bmesh.ops.triangulate(bm, faces=bm.faces[:])
        raw = np.array([v.co[:] for v in bm.verts], dtype=np.float64) / 100.0
        tris = np.array([[v.index for v in f.verts] for f in bm.faces], dtype=np.int64)
        bm.free()
    finally:
        cs2_validate.remove_objects(objs)
    loc = np.stack([raw[:, 2], raw[:, 0], raw[:, 1]], axis=1)
    co = np.stack([-loc[:, 0] + tower_x, -loc[:, 1], loc[:, 2]], axis=1)
    out = {"co": co, "tris": tris, "path": path}
    if back is not None:
        # T256: TrainBack (the station-side half, inboard +x here) pitched onto the station chord, as the code holds it
        if not back.any() or float(co[back, 0].mean()) <= tower_x:
            raise MContractError(f"RIG-hosted: no inboard TrainBack group in {path}")
        a = rig_hosted_pitch_m(spec)
        pz = spec.anchor_out[1]
        dx, dz = co[back, 0] - tower_x, co[back, 2] - pz
        c, s_ = math.cos(math.radians(a)), math.sin(math.radians(a))
        co[back, 0] = tower_x + c * dx + s_ * dz
        co[back, 2] = pz - s_ * dx + c * dz
        out["train_back_pitch_deg"] = round(a, 3)
        out["train_back_x_max"] = float(co[back, 0].max())
    _TOWER_CACHE[key] = out
    return out


def rig_hosted_pitch_m(spec):
    """T256 top: the station chord's descent (deg) from the lane end to the wheel tangent, in the xz plane: the constant pitch
    the code holds the hosted tower's TrainBack at (its inboard end down)."""
    (lx, lz), (wx, _wy, wz) = spec.anchor_out, spec.wheel
    return math.degrees(math.atan2(lz - wz, wx - lx))


def sweep_poses_m(spec, step=0.10, dang=2.0, anchors=None):
    """Chair poses along the code path (LiftLoopCache.AppendTerminalTurn): for each lane (y +-1.8) and each chain end in
    `anchors` [(x, z)], the straight from the end to the wheel tangent (wx, +-1.8, wz), then the half circle r 1.8 about the
    wheel bulging +x. BOTH facings. Returns [(label, path point (3,), travel unit (3,) horizontal, facing sign)]."""
    wx, _wy, wz = spec.wheel
    anchors = anchors or [spec.anchor_in, spec.anchor_out]
    out = []
    for ai, (ax, az) in enumerate(anchors):
        if any(tuple(anchors[j]) == (ax, az) for j in range(ai)):
            continue        # T256 rig: anchor_in == anchor_out; the label index stays the list index (X7: a2+ = line-first)
        for s in (-1.0, 1.0):
            a = np.array([ax, s * FG_LANE, az])
            b = np.array([wx, s * FG_LANE, wz])
            n = max(2, int(np.linalg.norm(b - a) / step) + 1)
            d = np.array([1.0, 0.0, 0.0])
            for i in range(n):
                q = a + (b - a) * (i / (n - 1))
                for f in (1.0, -1.0):
                    out.append((f"a{ai}_lane{int(s):+d}", q, d * f, f))
    for adeg in np.arange(-90.0, 90.0 + 1e-6, dang):
        r = math.radians(adeg)
        q = np.array([wx + FG_LANE * math.cos(r), FG_LANE * math.sin(r), wz])
        tan = np.array([-math.sin(r), math.cos(r), 0.0])
        for f in (1.0, -1.0):
            out.append(("arc", q, tan * f, f))
    return out


def place_chair(chair, q, d):
    """Chair vertices at path point q (grip top = path, pivot = q - hang) facing travel d."""
    d = np.asarray(d, dtype=np.float64)
    d = d / np.linalg.norm(d)
    up = np.array([0.0, 0.0, 1.0])
    left = np.cross(up, d)
    R = np.stack([d, left, up], axis=1)            # local x -> d, y -> left, z -> up
    return chair["co"] @ R.T + (np.asarray(q, dtype=np.float64) - np.array([0.0, 0.0, FG_HANG]))


# ---------------------------------------------------------------- M3 / M4 ------------------

M_GRIP_OK = ("wheelrope", "ropechord_", "sheave_bull", "sheave_hold_", "spoke_", "wheelhub")
M_MACHINE = ("wheelrope", "ropechord_", "sheave_bull", "sheave_hold_", "spoke_", "wheelhub",
             "wheelshaft")
M_DRESS_FAMS = ("maze_", "gate_", "board_", "label_", "stripe_", "chev_", "carpet_", "ext_",
                "tool_", "skirack_", "ski_", "estop_", "gauge_", "stake", "rail_catwalk",
                "rail_bay", "post_", "kick_", "stoparm", "propane", "wx", "guy_",
                "kitrow_")       # fine-detail step 3c: the meshes of the kit rows (cs2_kitrows.augment) are dressing


def _m_class(name):
    if name.startswith(M_MACHINE):
        return "machine"
    if name.startswith(M_DRESS_FAMS):
        return "dress"
    return "struct"


def chair_sweep_m(co, tris, names, spec, levels=(0, 1), step=0.10, dang=2.0, extra_anchors=(),
                  collect=False, poses=None):
    """M3 (C8-FG): the real 6M chair (LOD0 and LOD1) swept along the code path. The BODY (chair local z < 2.10) keeps these
    distances: to structure >= M_CLEAR_STRUCT, to dressing >= M_CLEAR_DRESS, and no overlap with anything but the rope
    machine (body <-> machine distance reported). The GRIP zone may touch only the rope machine (M_GRIP_OK). Distances =
    point-to-triangle both ways (vertices + edge samples every 0.03 m), so an edge-to-edge pass is resolved to ~1.5 cm.
    Returns the report.

    T256: `poses` (default None = the code path, sweep_poses_m) replaces the pose list (the nose train's posed line rope).
    Under M_RIG anchor_in == anchor_out: sweep_poses_m sweeps a repeated anchor once (labels keep their list index)."""
    cls = np.array([_m_class(n) for n in names])
    groups = {c: tris[cls == c] for c in ("struct", "dress", "machine")}
    bvhs = {c: _bvh(co, t_) for c, t_ in groups.items()}
    spts = {c: _edge_points(co, t_, 0.03) for c, t_ in groups.items()}
    nongrip = tris[[not n.startswith(M_GRIP_OK) for n in names]]
    bvh_nongrip = _bvh(co, nongrip)
    anchors = [spec.anchor_in, spec.anchor_out] + list(extra_anchors)
    rep = {"poses": 0, "min": {}, "where": {}, "grip_min": 9.9, "grip_where": None, "levels": list(levels),
           "anchors": [list(a) for a in anchors]}
    need = {"struct": M_CLEAR_STRUCT, "dress": M_CLEAR_DRESS, "machine": 0.0}
    cloud = []
    for lvl in levels:
        ch = load_chair_m(lvl)
        btris = ch["tris"][ch["body"]]
        gtris = ch["tris"][~ch["body"]]
        bloc = _edge_points(ch["co"], btris, 0.03)
        gloc = _edge_points(ch["co"], gtris, 0.03)
        for label, q, d, f in (sweep_poses_m(spec, step, dang, anchors) if poses is None else poses):
            P = place_chair(ch, q, d)
            up = np.array([0.0, 0.0, 1.0])
            dd = d / np.linalg.norm(d)
            R = np.stack([dd, np.cross(up, dd), up], axis=1)
            off = np.asarray(q) - np.array([0.0, 0.0, FG_HANG])
            BP = bloc @ R.T + off
            GP = gloc @ R.T + off
            if collect and lvl == 0:
                cloud.append(BP[::4])
            rep["poses"] += 1
            lo, hi = BP.min(axis=0) - 0.25, BP.max(axis=0) + 0.25
            cb = None
            # the line-first chain ends (X7) test STRUCTURE only: their chord is not the drawn rope (a line-first top ends at
            # the end-node tower), so rope-vs-chair is moot there
            primary = label == "arc" or int(label[1:label.index("_")]) < 2
            for c in (("struct", "dress", "machine") if primary else ("struct", "dress")):
                lim = max(need[c], 0.05) + 0.15     # the reported minimum is capped here
                d1, i1 = _min_dist(bvhs[c], BP, lim)
                S = spts[c]
                if len(S):
                    m = np.all((S >= lo) & (S <= hi), axis=1)
                    if m.any():
                        if cb is None:
                            cb = _bvh(P, btris)
                        d2, _i2 = _min_dist(cb, S[m], lim)
                        d1 = min(d1, d2)
                if bvhs[c] is not None:
                    if cb is None:
                        cb = _bvh(P, btris)
                    if bvhs[c].overlap(cb):
                        d1 = 0.0
                if d1 < rep["min"].get(c, 9.9):
                    rep["min"][c] = d1
                    rep["where"][c] = {"lod": lvl, "pose": label, "path": [round(float(v), 3) for v in q],
                                       "facing": f}
            gd, _gi = _min_dist(bvh_nongrip, GP, 0.06)
            if gd < rep["grip_min"]:
                rep["grip_min"] = gd
                rep["grip_where"] = {"lod": lvl, "pose": label, "path": [round(float(v), 3) for v in q],
                                     "facing": f}
    # name the parts at the minima (one nearest-triangle lookup per class)
    rep["min"] = {k: round(float(v), 4) for k, v in rep["min"].items()}
    rep["grip_min"] = round(float(rep["grip_min"]), 4)
    if collect:
        rep["_cloud"] = np.concatenate(cloud) if cloud else np.zeros((0, 3))
    return rep


def nearest_part_m(co, tris, names, pts, classes=("struct", "dress")):
    """Name of the station part nearest to `pts` among the classes (for messages)."""
    keep = [i for i, n in enumerate(names) if _m_class(n) in classes]
    if not keep:
        return None
    b = _bvh(co, tris[keep])
    d, i = _min_dist(b, pts, 2.0)
    return (names[keep[i]], round(d, 3)) if i is not None else None


def assert_sweep_m(co, tris, names, spec, rep):
    bad = []
    if rep["min"].get("struct", 9.9) < M_CLEAR_STRUCT - 1e-6:
        bad.append(f"body -> structure {rep['min']['struct']:.3f} < {M_CLEAR_STRUCT} at {rep['where']['struct']}")
    if rep["min"].get("dress", 9.9) < M_CLEAR_DRESS - 1e-6:
        bad.append(f"body -> dressing {rep['min']['dress']:.3f} < {M_CLEAR_DRESS} at {rep['where']['dress']}")
    if rep["min"].get("machine", 9.9) < 0.01 - 1e-6:
        bad.append(f"body -> rope machine {rep['min']['machine']:.3f} < 0.01 at {rep['where']['machine']}")
    if rep["grip_min"] <= 0.0:
        bad.append(f"grip zone touches a non-rope part at {rep['grip_where']}")
    if bad:
        for c, w in rep["where"].items():
            if w and rep["min"].get(c, 9.9) < 0.5:
                ch = load_chair_m(w["lod"])
                P = place_chair(ch, w["path"], [w["facing"] * v for v in _pose_dir(spec, w)])
                w["part"] = nearest_part_m(co, tris, names, P[np.unique(ch["tris"][ch["body"]])],
                                           classes=(c,))
        _m_fail("M3", "; ".join(bad) + f" (parts {[(c, w.get('part')) for c, w in rep['where'].items()]})")


def _pose_dir(spec, w):
    """Travel direction of a recorded pose (straights run +x, the arc's tangent from its point)."""
    q = w["path"]
    if w["pose"] == "arc":
        a = math.atan2(q[1], q[0] - spec.wheel[0])
        return (-math.sin(a), math.cos(a), 0.0)
    return (1.0, 0.0, 0.0)


def load_window_m(co, tris, names, spec, kind):
    """M4: the cushion top at the UP-lane tangent over the load (carpet_ld) / unload (crest) surface is in [0.45, 0.55]. The
    plank (the chair's lowest point) is >= M_PLANK_MIN over every walkable or ramp surface along the whole sweep (LOD0, both
    facings)."""
    ch = load_chair_m(0)
    wx, _wy, wz = spec.wheel
    surf_pref = ("carpet_ld",) if kind == "drive" else ("crest",)
    walk = tris[[n.startswith(M_WALK + ("loadramp", "rampup", "rampdown")) and not n.endswith("_lip")
                 for n in names]]
    target = tris[[n.startswith(surf_pref) for n in names]]
    bw, bt = _bvh(co, walk), _bvh(co, target)
    y_up = UP_LANE_Y[kind] * FG_LANE
    low = wz - FG_HANG + ch["lowest"]
    cushion = wz - FG_HANG + ch["cushion"]
    hit = bt.ray_cast(_V((wx, y_up, cushion)), _V((0.0, 0.0, -1.0)), 5.0)
    if hit[0] is None:
        _m_fail("M4", f"no {surf_pref[0]} under the up-lane tangent ({wx}, {y_up})")
    surf_z = float(hit[0].z)
    over = cushion - surf_z
    # the plank along the sweep: every chair vertex within 0.02 of its lowest z, rays down
    plank_local = ch["co"][ch["co"][:, 2] <= ch["lowest"] + 0.02]
    worst, worst_at = 9.9, None
    for label, q, d, f in sweep_poses_m(spec, 0.10, 2.0):
        dd = d / np.linalg.norm(d)
        up = np.array([0.0, 0.0, 1.0])
        R = np.stack([dd, np.cross(up, dd), up], axis=1)
        P = plank_local @ R.T + (q - np.array([0.0, 0.0, FG_HANG]))
        for p in P[::2]:
            h = bw.ray_cast(_V(p.tolist()), _V((0.0, 0.0, -1.0)), 3.0)
            if h[0] is not None and h[3] < worst:
                worst, worst_at = h[3], {"pose": label, "path": [round(float(v), 3) for v in q],
                                        "part": names[[i for i, n in enumerate(names) if n.startswith(
                                            M_WALK + ("loadramp", "rampup", "rampdown"))
                                            and not n.endswith("_lip")][h[2]]]}
    rep = {"surface": surf_pref[0], "surface_z": round(surf_z, 4), "cushion_z": round(cushion, 4),
           "cushion_over": round(over, 4), "plank_z": round(low, 4), "plank_over_surface": round(low - surf_z, 4),
           "plank_min_sweep": round(float(worst), 4), "plank_min_at": worst_at,
           "window": list(M_SEAT_WIN), "pin": "grip top = lane (ChairPinSystem.cs:1401); a rope-point "
                                               "pin would raise every FG chair 0.080 (skeptic claim 4)"}
    if not (M_SEAT_WIN[0] - 1e-6 <= over <= M_SEAT_WIN[1] + 1e-6):
        _m_fail("M4", f"cushion {over:.3f} over {surf_pref[0]} outside {M_SEAT_WIN}")
    if worst < M_PLANK_MIN - 1e-6:
        _m_fail("M4", f"plank {worst:.3f} over a walkable surface < {M_PLANK_MIN} at {worst_at}")
    return rep


# ---------------------------------------------------------------- M1 / M2 ------------------

def _prefab_json(path):
    """A CS2 .Prefab (Colossal JSON) -> dict: $fstrref:"x" tokens become plain strings."""
    with open(path, "r", encoding="utf-8-sig") as fh:
        txt = fh.read()
    txt = _re.sub(r'\$fstrref:"([^"]*)"', lambda m: json.dumps("$fstrref:" + m.group(1)), txt)
    return json.loads(txt)


def frozen_block_m(kind):
    """The frozen prefab fields of the DEPLOYED K building prefab (gamedata-mirror), game frame: everything the M repoint must
    not move. These are name, lot, components (in order, with every value), sub-nets, sub-lanes, sub-objects (the hosted
    tower pose), terraform, and the mesh POSITION / rotation. m_Meshes[0].m_Mesh is reported apart (the ONE line that
    changes). Returns (frozen dict, the old m_Mesh, the file's sha256)."""
    spec = M_SPECS[kind]
    path = os.path.join(GAMEDATA_M, spec.building, f"{spec.building}.Prefab")
    with open(path, "rb") as fh:
        sha = _hashlib.sha256(fh.read()).hexdigest()
    d = _prefab_json(path)
    mesh = d["m_Meshes"]["$rcontent"][0]
    old = mesh["m_Mesh"]

    def strip_ids(x):
        if isinstance(x, dict):
            return {k: strip_ids(v) for k, v in x.items() if k not in ("$id",)}
        if isinstance(x, list):
            return [strip_ids(v) for v in x]
        return x
    frozen = strip_ids({k: v for k, v in d.items() if k != "m_Meshes"})
    frozen["m_Meshes"] = [strip_ids({k: v for k, v in m.items() if k != "m_Mesh"})
                          for m in d["m_Meshes"]["$rcontent"]]
    return frozen, old, sha, path


def _comp(frozen, type_tail):
    for c in frozen["components"]["$rcontent"]:
        if c["$type"].split("|")[-1].startswith(type_tail) or c.get("name") == type_tail:
            return c
    return None


def hosted_row_m(kind, frozen=None):
    """The deployed building prefab's ObjectSubObjects row naming the hosted terminal tower (matched by the tower
    prefab's CID in gamedata-mirror), or None: the art-601 drive base has none."""
    spec = M_SPECS[kind]
    if frozen is None:
        frozen = frozen_block_m(kind)[0]
    with open(os.path.join(GAMEDATA_M, spec.tower_prefab, spec.tower_prefab + ".Prefab.cid"), "r",
              encoding="utf-8") as fh:
        cid = fh.read().strip()
    subs = _comp(frozen, "ObjectSubObjects")["m_SubObjects"]["$rcontent"]
    rows = [s for s in subs if cid in str(s.get("m_Object"))]
    if len(rows) > 1:
        _m_fail("M1", f"{len(rows)} hosted {spec.tower_prefab} rows in {spec.building}, expected at most 1")
    return rows[0] if rows else None


def _mesh_folder_by_cid_m(cid_ref, prefixes):
    """The gamedata-mirror mesh folder (under c030ee3ff256df91) whose LOD0 '<name> Mesh_*.Prefab.cid' holds the CID in
    cid_ref (a '$fstrref:"CID:<32 hex>"' value), among the folders whose name starts with one of prefixes. None when absent."""
    m = _re.search(r"[0-9a-f]{32}", str(cid_ref))
    root = os.path.join(GAMEDATA_M, "c030ee3ff256df91")
    if not m or not os.path.isdir(root):
        return None
    for name in sorted(os.listdir(root)):
        # the folder also holds loose asset cids ('<name>_<hash>.Geometry.cid', '.Texture.cid'): folders only
        if not name.startswith(prefixes) or not os.path.isdir(os.path.join(root, name)):
            continue
        for f in os.listdir(os.path.join(root, name)):
            if f.startswith(name + " Mesh_") and f.endswith(".Prefab.cid"):
                with open(os.path.join(root, name, f), "r", encoding="utf-8") as fh:
                    if fh.read().strip() == m.group(0):
                        return name
    return None


def hosted_expected_m(kind, old_mesh=None):
    """T256 (r4-tex, 2026-11-20): the hosted tower row the deployed building should carry. The rig generation (the mesh the
    building draws has a manifest with a "rig" block: P on) moves the top's row to RIG_HOSTED_X and its tower prefab to the
    rig's hosted tower (ET62Q); every earlier generation keeps spec.tower_x / spec.tower. Returns (x, tower mesh name,
    the deployed station mesh name or None, rig bool). The M1 / MANIFESTSHA contract follows the DEPLOYED generation, so a
    re-freeze after the rig repoint passes, and the M manifest records which generation it was frozen against."""
    spec = M_SPECS[kind]
    if old_mesh is None:
        old_mesh = frozen_block_m(kind)[1]
    st = _mesh_folder_by_cid_m(old_mesh, ("SkiLiftBaseStation", "SkiLiftTopStation"))
    rig = False
    if st:
        for prefix in ("SkiLiftBaseStation", "SkiLiftTopStation"):
            if st.startswith(prefix):
                letter = st[len(prefix):]
                mp = os.path.join(IMPORT_M, "stations", "fixedgrip", letter, st, st + ".manifest.json")
                if os.path.isfile(mp):
                    with open(mp, "r", encoding="utf-8") as fh:
                        rig = bool(json.load(fh).get("rig"))
    if not rig:
        return spec.tower_x, spec.tower, st, False
    tower = spec.tower
    tp = os.path.join(GAMEDATA_M, spec.tower_prefab, spec.tower_prefab + ".Prefab")
    if os.path.isfile(tp):
        tm = _re.search(r'"m_Mesh"\s*:\s*([^,\n]+)', open(tp, "r", encoding="utf-8").read())
        tower = (_mesh_folder_by_cid_m(tm.group(1), ("SkiLiftTowerET",)) if tm else None) or spec.tower
    return RIG_HOSTED_X, tower, st, True


def m1_frozen(kind):
    """M1: the datums of the generator agree with SkiCatalog.cs, carriers.json and the deployed K prefab. Returns the
    prefab_contract block of the manifest. The hosted tower row is checked against the DEPLOYED generation
    (hosted_expected_m: RIG_HOSTED_X once the building draws the rig mesh)."""
    spec = M_SPECS[kind]
    frozen, old_mesh, sha, path = frozen_block_m(kind)
    hx, htower, hstation, hrig = hosted_expected_m(kind, old_mesh)
    bad = []
    # SkiCatalog.cs: the FG station rows (WheelLocal, stub tip)
    with open(SKICATALOG_CS, "r", encoding="utf-8") as fh:
        cs = fh.read()
    nm = "StationFixedGripBase" if kind == "drive" else "StationFixedGripTop"
    m = _re.search(r"Names\." + nm + r",[^\n]*?new float3\(([-0-9.f]+), ([-0-9.f]+), ([-0-9.f]+)\), "
                   r"(StubTipLocalZ|[-0-9.f]+)", cs)
    stub = _re.search(r"public const float StubTipLocalZ = ([-0-9.f]+);", cs)
    if not m or not stub:
        _m_fail("M1", f"SkiCatalog.cs: no {nm} row / StubTipLocalZ")
    wl = tuple(float(v.rstrip("f")) for v in m.groups()[:3])
    tip = float(stub.group(1).rstrip("f")) if m.group(4) == "StubTipLocalZ" else float(m.group(4).rstrip("f"))
    if any(abs(a - b) > 1e-6 for a, b in zip(m_from_game(wl)[::2], spec.wheel[::2])) or abs(wl[0]) > 1e-9:
        bad.append(f"WheelLocal {wl} (game) != generator {m_to_game(spec.wheel)}")
    with open(CARRIERS_JSON, "r", encoding="utf-8") as fh:
        tr = json.load(fh)["carriers"]["tr"]
    for k, v in (("hang_m", FG_HANG), ("lane_x_m", FG_LANE), ("grip_top_above_rope_m", FG_GRIP_ABOVE),
                 ("seats", int(FG_SEATS_DIGITS))):
        if abs(float(tr[k]) - float(v)) > 1e-9:
            bad.append(f"carriers tr {k} {tr[k]} != generator {v}")
    # the deployed prefab: HV sub-lane ends, stub tip, hosted tower pose
    lanes = _comp(frozen, "ObjectSubLanes")["m_SubLanes"]["$rcontent"]
    # the HV sub-lanes name the OWN rope lane of the FG station (SkiFixedGripHaulRope, lift alignment F3, 2026-11-02, the
    # deployed prefab carries its CID). The filter used to be the vanilla HV GUID "5d88ac45". After F3 it matched no lane and
    # the anchor check below passed vacuously. So it keys on the CID of the lane (read from the mirror), and an empty or
    # short match is a failure, never a pass.
    with open(os.path.join(GAMEDATA_M, FG_ROPE_LANE, FG_ROPE_LANE + ".Prefab.cid"), "r", encoding="utf-8") as fh:
        fg_lane_cid = fh.read().strip()
    hv = [l for l in lanes if fg_lane_cid in l["m_LanePrefab"]]
    if len(hv) != 2:
        bad.append(f"{len(hv)} HV sub-lanes name {FG_ROPE_LANE} (CID {fg_lane_cid}), expected 2 (one per lane x)")
    s0 = M_SPECS_SHIPPED[kind]          # T27: the deployed anchors (= spec unless M_ROPE_ON_CROWN)
    for l in hv:
        a, d_ = l["m_BezierCurve"]["a"], l["m_BezierCurve"]["d"]
        if abs(abs(a["x"]) - FG_LANE) > 1e-6 or abs(a["y"] - s0.anchor_out[1]) > 1e-6 or \
                abs(a["z"] - s0.anchor_out[0]) > 1e-6 or abs(d_["y"] - s0.anchor_in[1]) > 1e-6 or \
                abs(d_["z"] - s0.anchor_in[0]) > 1e-6:
            bad.append(f"HV sub-lane {a} -> {d_} != generator anchors {s0.anchor_out} / {s0.anchor_in}")
    nets = _comp(frozen, "ObjectSubNets")["m_SubNets"]["$rcontent"]
    hv_stub = [n for n in nets if n["m_BezierCurve"]["a"]["x"] == 0]
    if not hv_stub or abs(hv_stub[0]["m_BezierCurve"]["d"]["z"] - tip) > 1e-6:
        bad.append(f"HV stub tip {hv_stub[0]['m_BezierCurve']['d'] if hv_stub else None} != StubTipLocalZ {tip}")
    # the hosted tower row by the CID of its prefab (it was entry 0). art-601: the drive base ships without it (the nose
    # train, --no-hosted-tower, owner notes 5 / 11), so an absent row is the host-less base. The return (ET62) keeps its row
    tw = hosted_row_m(kind, frozen)
    if tw is None:
        if kind != "drive":
            bad.append(f"no hosted {spec.tower_prefab} row in {spec.building} (the top keeps its ET62)")
    else:
        pos, rot = tw["m_Position"], tw["m_Rotation"]["value"]
        if (pos["x"], pos["y"], pos["z"]) != (0, 0, hx) or \
                (rot["x"], rot["y"], rot["z"], rot["w"]) != (0, 1, 0, 0):
            bad.append(f"hosted tower pose {pos} / {rot} != generator (0, 0, {hx}) rot 180"
                       f"{' (rig generation ' + str(hstation) + ')' if hrig else ''}")
    if bad:
        _m_fail("M1", "; ".join(bad))
    out = {
        "source": os.path.relpath(path, REPO_M).replace("\\", "/"),
        "source_sha256": sha,
        "repoint": {"m_Meshes[0].m_Mesh_from": old_mesh, "m_Meshes[0].m_Mesh_to": "<the M mesh's CID, "
                    "written by the importer>", "k_mesh": spec.k_mesh,
                    "rule": "only this line changes (surgery/towers-transfer.md; never rename, never a "
                            "new .Prefab.cid)"},
        "frozen": frozen,
        "frozen_sha256": _hashlib.sha256(json.dumps(frozen, sort_keys=True).encode("utf-8")).hexdigest(),
        "code": {"WheelLocal_game": list(wl), "StubTipLocalZ": tip, "SkiCatalog": "SkiCatalog.cs " + nm},
        "carrier_tr": {k: tr[k] for k in ("hang_m", "grip_top_above_rope_m", "lane_x_m", "seats", "axle_m")},
        "hosted_tower": None if tw is None else dict({
            "prefab": spec.tower_prefab, "mesh_after_M6_M7": spec.tower,
            "position": [0.0, 0.0, hx], "rotation_quat": [0.0, 1.0, 0.0, 0.0]},
            **({"deployed_generation": hstation, "deployed_mesh": htower,
                "why": "T256: the building draws the rig generation; its row sits at RIG_HOSTED_X"} if hrig else {})),
        "utility_object": None,
        "colour_slots_lod0": [[0.25, 0.26, 0.275, 1.0], [0.85, 0.25, 0.10, 1.0], [1.0, 1.0, 1.0, 1.0]],
    }
    if M_ROPE_ON_CROWN:
        out["rope_on_crown_surgery"] = m_rope_surgery(kind)
    return out


def m2_rope(co, tris, names, spec):
    """M2: the rope torus center = WheelLocal +- 0.002. Its radius is 1.80 +- 0.005. The rim outer is <= 1.745 (also under M_RIG: OC-3
    keeps the O rim, _wheel_rig_m). Every ropechord_
    vertex is within 0.06 of the analytic chord."""
    wx, _wy, wz = spec.wheel

    def verts(pfx):
        sel = np.unique(tris[[n.startswith(pfx) for n in names]])
        return co[sel]
    T = verts("wheelrope")
    rad = np.hypot(T[:, 0] - wx, T[:, 1])
    ring = np.hypot(rad - M_TORUS_R, T[:, 2] - wz)
    # the ring center circle: radial mean of (rad), the height mean
    R_fit = float(0.5 * (rad.max() + rad.min()))
    z_fit = float(0.5 * (T[:, 2].max() + T[:, 2].min()))
    rim = verts("sheave_bull")
    rim_out = float(np.hypot(rim[:, 0] - wx, rim[:, 1]).max())
    worst = 0.0
    ax, az = spec.anchor_in
    for side, s in (("n", -1.0), ("p", 1.0)):
        C = verts(f"ropechord_{side}")
        a = np.array([ax, s * FG_LANE, az])
        b = np.array([wx, s * FG_LANE, wz])
        u = (b - a) / np.linalg.norm(b - a)
        v = C - a
        dist = np.linalg.norm(v - np.outer(v @ u, u), axis=1)
        worst = max(worst, float(dist.max()))
    rep = {"torus_R": round(R_fit, 4), "torus_z": round(z_fit, 4), "ring_dev_max": round(float(np.abs(ring - M_ROPE_R).max()), 4),
           "rim_outer": round(rim_out, 4), "chord_dev_max": round(worst, 4)}
    bad = []
    if abs(R_fit - M_TORUS_R) > 0.005 or abs(z_fit - wz) > 0.002:
        bad.append(f"torus R {R_fit:.4f} / z {z_fit:.4f} vs {M_TORUS_R} / {wz}")
    if rim_out > 1.745:
        bad.append(f"rim outer {rim_out:.4f} > 1.745")
    if worst > (M_ROPE_R + 0.001 if M_RIG else 0.06):     # T256: the rig's rope hexagon (r M_ROPE_R) sits on the code chord;
                                                          # flag-off keeps the shipped 0.06 tolerance
        bad.append(f"chord vertex {worst:.3f} off the code chord")
    if bad:
        _m_fail("M2", "; ".join(bad))
    return rep


# ---------------------------------------------------------------- M5 / M9 / M12 -----------

def seam_m(co, tris, names, kind, levels=((0, 0), (0, 1), (1, 0), (1, 1)), lod1_drop=None):
    """M5 (S8-FG): the hosted ET68O / ET62O (LOD0 + LOD1, the shipped FBXs) at its hosted pose against station LOD0 / LOD1 (LOD1
    = LOD0 minus the LOD1 deletions). No station face is within M_SEAM_CLEAR of the tower, except a ropechord_ within
    M_SEAM_ROPE_EXEMPT of the sub-lane end (the rope runs into the sheaves of the tower). Returns the report."""
    spec = M_SPECS[kind]
    if M_NOSE_TRAIN and kind == "drive":
        return {"skipped": "art-601 nose train: no hosted end tower (the import round drops the prefab's hosted row)"}
    if M_RIG and kind == "return" and not RIG_HOSTED_DIR:
        return {"skipped": "T256: no --rig-hosted folder (the skinned hosted end tower's FBXs) given"}
    ax = spec.anchor_in[0]
    drop1 = lod1_drop or ()
    rep = {}
    for sl, tl in levels:
        keep = []
        for i, n in enumerate(names):
            if sl == 1 and n.startswith(drop1):
                continue
            keep.append(i)
        st = tris[keep]
        snames = [names[i] for i in keep]
        tw = load_tower_m(kind, tl)
        tb = _bvh(tw["co"], tw["tris"])
        tlo, thi = tw["co"].min(axis=0) - 0.2, tw["co"].max(axis=0) + 0.2
        worst, wpart = 9.9, None
        pts_by_part = {}
        for i, n in enumerate(snames):
            pts_by_part.setdefault(n, []).append(i)
        for n, idx in pts_by_part.items():
            T = st[idx]
            P = _edge_points(co, T, 0.03)
            m = np.all((P >= tlo) & (P <= thi), axis=1)
            if not m.any():
                continue
            P = P[m]
            if n.startswith("ropechord_"):
                ex = M_SEAM_ROPE_EXEMPT
                if "train_back_x_max" in tw:       # T256: the rope runs in the pitched TrainBack's sheaves to its inner end
                    ex = max(ex, tw["train_back_x_max"] - ax + 0.15)
                P = P[np.abs(P[:, 0] - ax) > ex]
                if not len(P):
                    continue
            d, _i = _min_dist(tb, P, M_SEAM_CLEAR + 0.05)
            sb = _bvh(co, T)
            Q = _edge_points(tw["co"], tw["tris"], 0.03)
            plo, phi = co[np.unique(T)].min(axis=0) - 0.2, co[np.unique(T)].max(axis=0) + 0.2
            mq = np.all((Q >= plo) & (Q <= phi), axis=1)
            if mq.any() and not n.startswith("ropechord_"):
                d2, _ = _min_dist(sb, Q[mq], M_SEAM_CLEAR + 0.05)
                d = min(d, d2)
            if d < worst:
                worst, wpart = d, n
        rep[f"st{sl}_et{tl}"] = {"min_m": round(float(worst), 4), "part": wpart,
                                 **({"train_back_pitch_deg": tw["train_back_pitch_deg"]} if "train_back_pitch_deg" in tw
                                    else {})}
        if worst < M_SEAM_CLEAR - 1e-6:
            _m_fail("M5", f"station LOD{sl} part {wpart} {worst:.3f} m from the hosted {spec.tower} "
                          f"LOD{tl} (< {M_SEAM_CLEAR})")
    return rep


def lot_m(co, kind):
    """M9: every LOD0 vertex inside the 2 x 3 lot with 0.25 margin. Nothing within 1.0 m of the LV stub (base: x -12..-8.5 at y
    -6, the vanilla pole at (-12, -6))."""
    hx, hy = M_LOT_HALF[0] - M_LOT_MARGIN, M_LOT_HALF[1] - M_LOT_MARGIN
    lo, hi = co.min(axis=0), co.max(axis=0)
    if lo[0] < -hx or hi[0] > hx or lo[1] < -hy or hi[1] > hy:
        _m_fail("M9", f"mesh x {lo[0]:.2f}..{hi[0]:.2f}, y {lo[1]:.2f}..{hi[1]:.2f} outside the lot "
                      f"(+-{hx}, +-{hy})")
    stub = None
    if kind == "drive":
        d = np.hypot(np.clip(co[:, 0], -12.0, -8.5) - co[:, 0], co[:, 1] - (-6.0))
        stub = float(d.min())
        if stub < 1.0:
            _m_fail("M9", f"a vertex {stub:.2f} m from the LV stub / pole")
    return {"x": [round(float(lo[0]), 3), round(float(hi[0]), 3)],
            "y": [round(float(lo[1]), 3), round(float(hi[1]), 3)],
            "z": [round(float(lo[2]), 3), round(float(hi[2]), 3)], "lv_stub_min_m": stub}


def subobj_check_m(co, tris, names, kind, cloud=None):
    """M12: no *binding* part. Every vanilla row's variant-UNION box (own frame of M, MC3), shrunk 0.01, holds no station
    triangle, stays inside the lot and stays >= M_CLEAR_DRESS from the swept chair body cloud (when given)."""
    TS = __import__("tram_station")
    if any("binding" in n for n in names):
        _m_fail("M12", "a part named *binding*")
    T = co[tris]
    out = []
    for pf, pos, facing, mount in subobjects_m(kind):
        # T17 lamp part: the floodlight row is three boxes (plate, pole, heads, m_row_boxes), a vanilla row one
        bx = m_row_boxes(pf, pos, facing)
        lo, hi = np.min([b[0] for b in bx], axis=0), np.max([b[1] for b in bx], axis=0)
        hit = np.zeros(len(T), dtype=bool)
        for blo, bhi in bx:
            hit |= TS._tri_box_overlap(T, blo + 0.01, bhi - 0.01)
        hits = sorted({names[i] for i in np.nonzero(hit)[0]})
        row = {"prefab": pf, "pos_authoring": [round(v, 3) for v in pos], "facing": facing,
               "box_lo": [round(float(v), 3) for v in lo], "box_hi": [round(float(v), 3) for v in hi],
               "hits": hits}
        if hits:
            _m_fail("M12", f"{pf} at {pos} (box {row['box_lo']}..{row['box_hi']}) holds {hits[:6]}")
        if cloud is not None and len(cloud):
            dd = min(np.linalg.norm(np.maximum(np.maximum(blo - cloud, 0.0), cloud - bhi), axis=1).min()
                     for blo, bhi in bx)
            row["chair_m"] = round(float(dd), 3)
            if dd < M_CLEAR_DRESS:
                _m_fail("M12", f"{pf} at {pos}: {dd:.3f} m from the swept chair")
        if lo[0] < -M_LOT_HALF[0] or hi[0] > M_LOT_HALF[0] or lo[1] < -M_LOT_HALF[1] or hi[1] > M_LOT_HALF[1]:
            _m_fail("M12", f"{pf} at {pos} leaves the lot")
        out.append(row)
    return out


# ---------------------------------------------------------------- M7 / M8 (tram C14 / C15) -

@_ctx.contextmanager
def _ts_override(**names):
    """Point the tables of tram_station at those of M for the duration (reuse, never copy: the lift_terminal_detach._lt_override
    pattern)."""
    import tram_station as TS
    saved = {k: getattr(TS, k) for k in names}
    for k, v in names.items():
        setattr(TS, k, v)
    try:
        yield TS
    finally:
        for k, v in saved.items():
            setattr(TS, k, v)


def contact_kinds_m():
    import cs2_liftdress as LD
    out = dict(LD.DRESS_CONTACT_KINDS)
    for why, toks in M_CONTACTS.items():
        for tok in toks.split():
            out[tuple(sorted(tok.split(":")))] = why
    if M_RIG:       # T256: the rig's own contacts (none of these parts exist without --rig)
        for why, toks in RIG_CONTACTS.items():
            for tok in toks.split():
                out[tuple(sorted(tok.split(":")))] = why
    return out


def clash_m(t):
    """M8 (C14-FG): tram_station.check_part_clashes on the joined mesh with the declared kind pairs of M (M_CONTACTS +
    cs2_liftdress.DRESS_CONTACTS), KNOWN_OPEN empty, no hut-clearance rule (the FG huts carry their dressing by declared
    contact)."""
    with _ts_override(CONTACT_KINDS=contact_kinds_m(), KNOWN_OPEN={}, HUT_GROUP=(), HUT_NEIGHBOURS=()) as TS:
        try:
            return TS.check_part_clashes(t, "m")
        except TS.ContractError as e:
            raise MContractError(f"M8 {e}") from None


def reach_m(t, kind):
    """M7 (C15-FG): every walkable surface (M_WALK) reaches grade. The edges of the graph are:
    - Surfaces at <= C15 grade (a 0.30 step) join GRADE.
    - Two surfaces at one level within C15 gap join when a C15 clear crossing exists (tram join_crossing).
    - A fixed ladder (tram walk_ladders: rails ladder_<tag>_*, rungs lrung_<tag>_<k>) joins its foot and head surfaces when
      pitch <= 0.36, first rung <= 0.40, its rails reach >= 1.0 over the arrival and its climbing corridor (0.61 wide x 0.61
      deep, foot + 0.10 .. top rung + 1.0) holds no foreign triangle.
    - An FG walking RAMP (M_RAMPS) joins its two ends when its slope <= M_RAMP_MAX_DEG and it is >= C15 clear wide.

    Every standable top (tram C15_STAND) must be walkable or declared in M_NOT_WALKABLE. Every railing post stands on a
    walkable surface. Returns the report, raises MContractError("M7 ...")."""
    with _ts_override(WALK_PREFIXES=M_WALK, NOT_WALKABLE=M_NOT_WALKABLE) as TS:
        C = TS.C15
        pf, parts = TS._part_tris(t)
        names = sorted(parts)
        surf = TS.walk_surfaces(pf, parts)
        ladders = TS.walk_ladders(pf, parts)
        T_all = np.concatenate([pf[parts[n]] for n in names])
        N_all = np.repeat(np.arange(len(names)), [len(parts[n]) for n in names])
        T_lo, T_hi = T_all.min(axis=1), T_all.max(axis=1)
        msgs = []

        def blocked(lo, hi, own):
            cand = np.nonzero(np.all(T_lo <= hi, axis=1) & np.all(T_hi >= lo, axis=1))[0]
            cand = np.array([i for i in cand if names[N_all[i]] not in own], dtype=np.int64)
            if not len(cand):
                return []
            ov = TS._tri_box_overlap(T_all[cand], np.asarray(lo, float), np.asarray(hi, float))
            return sorted({names[N_all[i]] for i in cand[ov]})

        def lands(y, px, pz):
            out = [s for s, d in surf.items() if abs(d["y"] - y) <= C["land"] and
                   TS._plan_gap(px, pz, d) <= C["land"]]
            if y <= C["grade"]:
                out.append("GRADE")
            return out

        adj = {n: set() for n in list(surf) + ["GRADE"]}
        for n, d in surf.items():
            if d["y"] <= C["grade"] + 1e-6:
                adj[n].add("GRADE")
                adj["GRADE"].add(n)
        sl = sorted(surf)
        joins = {}
        for i, a in enumerate(sl):
            for b in sl[i + 1:]:
                if abs(surf[a]["y"] - surf[b]["y"]) <= C["level"] and TS._surf_gap(surf[a], surf[b]) <= C["gap"]:
                    j = TS.join_crossing(a, b, surf, T_all, N_all, T_lo, T_hi, names)
                    joins[f"{a} | {b}"] = j
                    if j["ok"]:
                        adj[a].add(b)
                        adj[b].add(a)
        rep_l = {}
        for tag, L in ladders.items():
            bad = []
            if L["foot_y"] is None:
                msgs.append(f"ladder {tag}: no rails")
                continue
            pt = [0.0, 0.0, 0.0]
            pt[L["rax"]], pt[L["nax"]] = L["mid"], L["plane"]
            heads = [s for s in lands(L["head_y"], pt[0], pt[2]) if s != "GRADE"]
            heads = [s for s in heads if surf[s]["y"] >= L["head_y"] - 1e-6] or heads
            feet = lands(L["foot_y"], pt[0], pt[2])
            rails = [n_ for n_ in names if n_.startswith(f"ladder_{tag}_") and not _re.fullmatch(
                rf"ladder_{tag}_s\d+", n_)]
            rail_top = max(float(pf[parts[n_]][:, :, 1].max()) for n_ in rails) if rails else 0.0
            if L["pitch"] > C["pitch"] + 1e-6:
                bad.append(f"rung pitch {L['pitch']} > {C['pitch']}")
            if L["first"] > C["first"] + 1e-6:
                bad.append(f"first rung {L['first']} > {C['first']}")
            if not heads:
                bad.append(f"top rung {L['head_y']} arrives on no walkable surface")
            if not feet:
                bad.append(f"foot {L['foot_y']} on no walkable surface or the ground")
            corridor = None
            if heads:
                s = surf[heads[0]]
                if rail_top < s["y"] + 1.0 - 1e-6:
                    bad.append(f"rails end {rail_top - s['y']:.2f} m over the arrival (< 1.0)")
                c = (s["x"][0] + s["x"][1]) / 2.0 if L["nax"] == 0 else (s["z"][0] + s["z"][1]) / 2.0
                side = -1.0 if c > L["plane"] else 1.0
                lo, hi = np.empty(3), np.empty(3)
                lo[L["rax"]], hi[L["rax"]] = L["mid"] - C["clear"] / 2.0, L["mid"] + C["clear"] / 2.0
                a_, b_ = L["plane"] + side * 0.05, L["plane"] + side * C["clear"]
                lo[L["nax"]], hi[L["nax"]] = min(a_, b_), max(a_, b_)
                lo[1], hi[1] = L["foot_y"] + 0.10, L["head_y"] + 1.0
                own = {n_ for n_ in names if n_.startswith(L["own_prefixes"])}
                hit = blocked(lo, hi, own)
                corridor = {"lo": [round(float(v), 3) for v in lo], "hi": [round(float(v), 3) for v in hi]}
                if hit:
                    bad.append(f"climbing corridor blocked by {hit[:6]}")
            rep_l[tag] = {"rungs": L["rungs"], "pitch": L["pitch"], "first": L["first"], "from": feet,
                          "to": heads, "rails_over_arrival": round(rail_top - (surf[heads[0]]["y"] if heads else 0.0), 3),
                          "corridor_pf": corridor}
            if bad:
                msgs.append(f"ladder {tag}: " + "; ".join(bad))
                continue
            for a in heads:
                for b in feet:
                    if a != b:
                        adj[a].add(b)
                        adj[b].add(a)
        rep_r = {}
        for ramp, low, high in M_RAMPS[kind]:
            if ramp not in parts:
                msgs.append(f"ramp {ramp}: missing")
                continue
            T = pf[parts[ramp]]
            n = np.cross(T[:, 1] - T[:, 0], T[:, 2] - T[:, 0])
            L_ = np.linalg.norm(n, axis=1)
            # the walking face(s): up-facing triangles of >= 0.25 m2 (the 0.02 m chamfer strips of the bevel between the deck
            # and the cheeks are not walked on)
            top = (L_ > 1e-12) & (n[:, 1] > 0.5 * L_) & (0.5 * L_ >= 0.25)
            ang = float(np.degrees(np.arccos(np.clip(n[top][:, 1] / L_[top], -1, 1))).max())
            Q = T[top].reshape(-1, 3)
            ylo, yhi = float(Q[:, 1].min()), float(Q[:, 1].max())
            ext = Q.max(axis=0) - Q.min(axis=0)
            width = float(min(ext[0], ext[2]))
            ok_low = (ylo <= C["grade"]) if low == "GRADE" else (low in surf and abs(surf[low]["y"] - ylo) <= C["land"])
            ok_high = high in surf and abs(surf[high]["y"] - yhi) <= C["land"]
            bad = []
            if ang > M_RAMP_MAX_DEG + 1e-6:
                bad.append(f"slope {ang:.2f} deg > {M_RAMP_MAX_DEG}")
            if width < C["clear"]:
                bad.append(f"width {width:.2f} < {C['clear']}")
            if not ok_low or not ok_high:
                bad.append(f"ends {ylo:.2f} / {yhi:.2f} do not land on {low} / {high}")
            rep_r[ramp] = {"slope_deg": round(ang, 2), "width": round(width, 3), "from": low, "to": high,
                           "y": [round(ylo, 3), round(yhi, 3)]}
            if bad:
                msgs.append(f"ramp {ramp}: " + "; ".join(bad))
                continue
            adj.setdefault(low, set()).add(high)
            adj[high].add(low)
        seen, todo = {"GRADE"}, ["GRADE"]
        while todo:
            for b in sorted(adj[todo.pop()]):
                if b not in seen:
                    seen.add(b)
                    todo.append(b)
        orphans = [n for n in sl if n not in seen]
        if orphans:
            msgs.append("orphan surfaces (no ramp / ladder path to grade): " + ", ".join(
                f"{n} (y {surf[n]['y']:.2f})" for n in orphans) + "; joins " + json.dumps(
                {k: v for k, v in joins.items() if not v["ok"]}))
        stand = TS.standable_parts(pf, parts, names, T_all, N_all, T_lo, T_hi)
        undeclared = {n: d for n, d in stand.items() if TS._not_walkable_reason(n) is None}
        if undeclared:
            msgs.append("undeclared standable surfaces: " + ", ".join(
                f"{n} (y {d['y']:.2f}, {d['area']:.1f} m2, clear at {d['at']})" for n, d in sorted(undeclared.items())[:8]))
        loose = []
        for n in names:
            if not _re.fullmatch(r"post_.+_\d+", n):
                continue
            P = pf[parts[n]].reshape(-1, 3)
            fy = float(P[:, 1].min())
            F = P[P[:, 1] <= fy + 1e-4]
            fx, fz = float(F[:, 0].mean()), float(F[:, 2].mean())
            if not any(abs(d["y"] - fy) <= C["level"] and TS._plan_gap(fx, fz, d) <= C["post_on"]
                       for d in surf.values()):
                loose.append(f"{n} ({fx:.2f}, {fy:.2f}, {fz:.2f})")
        if loose:
            msgs.append("railing posts on no walkable surface: " + ", ".join(loose[:8]))
        rep = {"surfaces": {n: [surf[n]["y"], surf[n]["x"], surf[n]["z"]] for n in sl},
               "ladders": rep_l, "ramps": rep_r, "joins": joins, "orphans": orphans,
               "not_walkable": {n: {"why": TS._not_walkable_reason(n)[0], **d} for n, d in sorted(stand.items())
                                if TS._not_walkable_reason(n) is not None},
               "undeclared": sorted(undeclared), "rule_ramp_deg": M_RAMP_MAX_DEG,
               "rule_grade_step": C["grade"]}
        if msgs:
            raise MContractError("M7 " + "; ".join(msgs))
        return rep


# ---------------------------------------------------------------- M18 bay walk routes -------
#
# M18 (2026-10-12): every walk route on the bay of the top station is >= 0.70 m wide at hip height. The checker found 0.41 m
# to the booth door (the e-stop post and the STAY board stood in the gateway between the crest and the ledge) and 0.17-0.37 m
# in the W / back strips. M7 only measures the free length of the crest <-> bay edge. Model:
# - The walk surface is the level top of `crest` + `liftiebay`. A plan cell stands when the highest up-facing surface below
#   the hip band lies within M_ROUTE_FLOOR_TOL of the bay top. Paint stripes and the 4 cm column plate are floor. The 15 cm
#   booth slab is a step, and the ramps and the grade are other levels. This contract does not walk on any of them.
# - An obstacle is any mesh triangle clipped to the hip band (M_ROUTE_BAND over the surface), seen in plan (a wall is a
#   segment). clearance(p) = the plan distance from p to the nearest obstacle (0 inside one).
# - The width of a route = 2 x its bottleneck clearance. This is the best, over the 8-connected paths of standing cells
#   between its two nodes, of the smallest clearance on the path (capped at M_ROUTE_CAP). Nodes and strip boxes come from
#   part bounding boxes (booth, bay, crest, rails, door leaf), not from M_TOP.
# - Routes: crest -> door, crest -> the NW / NE ledge corners, the W / back / E strip each inside its own strip box, and the
#   front ledge NW -> NE (ledge + 0.8 m of the open crest).
#
# Raises "M18 ..." naming the narrowest point and the two parts that pinch it.
M_ROUTE_MIN = 0.70               # clear width of every bay route (hip height)
M_ROUTE_BAND = (0.30, 1.80)      # m over the walking surface
M_ROUTE_FLOOR_TOL = 0.05         # a feature this low is floor, not a step
M_ROUTE_STEP = 0.05              # plan grid of the clearance field
M_ROUTE_CAP = 0.80               # clearance cap (route width cap 1.6 m)


def _band_footprints_m(co, tris, names, z_lo, z_hi, box):
    """Plan triangles of every mesh triangle clipped to z_lo..z_hi whose plan bbox meets `box`
    (x0, x1, y0, y1): (A, B, C (m, 2) arrays, part name per plan triangle). A wall gives a
    degenerate plan triangle (a segment)."""
    T = co[tris]
    keep = (T[:, :, 2].max(axis=1) > z_lo + 1e-9) & (T[:, :, 2].min(axis=1) < z_hi - 1e-9)
    keep &= (T[:, :, 0].max(axis=1) >= box[0]) & (T[:, :, 0].min(axis=1) <= box[1])
    keep &= (T[:, :, 1].max(axis=1) >= box[2]) & (T[:, :, 1].min(axis=1) <= box[3])
    A, B, C, who = [], [], [], []
    for i in np.nonzero(keep)[0]:
        poly = [tuple(float(v) for v in p) for p in T[i]]
        for level, upper in ((z_lo, False), (z_hi, True)):
            out = []
            for k in range(len(poly)):
                p, q = poly[k], poly[(k + 1) % len(poly)]
                ip = (p[2] <= level) if upper else (p[2] >= level)
                iq = (q[2] <= level) if upper else (q[2] >= level)
                if ip:
                    out.append(p)
                if ip != iq:
                    s = (level - p[2]) / (q[2] - p[2])
                    out.append((p[0] + s * (q[0] - p[0]), p[1] + s * (q[1] - p[1]), level))
            poly = out
            if len(poly) < 3:
                break
        for k in range(1, len(poly) - 1):
            A.append(poly[0][:2])
            B.append(poly[k][:2])
            C.append(poly[k + 1][:2])
            who.append(names[i])
    z2 = np.zeros((0, 2))
    return (np.array(A) if A else z2, np.array(B) if B else z2, np.array(C) if C else z2, who)


def _seg_dist_m(P, S0, S1):
    """Distance (n, m) from points P (n, 2) to the segments S0[k] - S1[k]."""
    d = S1 - S0
    L2 = (d * d).sum(axis=1)
    w = P[:, None, :] - S0[None, :, :]
    t = np.einsum("nmk,mk->nm", w, d) / np.where(L2 > 1e-18, L2, 1.0)[None, :]
    t = np.where(L2[None, :] > 1e-18, np.clip(t, 0.0, 1.0), 0.0)
    q = w - t[:, :, None] * d[None, :, :]
    return np.sqrt((q * q).sum(axis=2))


def _plan_clearance_m(P, A, B, C, chunk=96):
    """Distance from every point of P (n, 2) to the nearest plan triangle (0 inside one)."""
    best = np.full(len(P), np.inf)
    for s in range(0, len(A), chunk):
        a, b, c = A[s:s + chunk], B[s:s + chunk], C[s:s + chunk]
        d = np.minimum(np.minimum(_seg_dist_m(P, a, b), _seg_dist_m(P, b, c)), _seg_dist_m(P, c, a))
        area = (b[:, 0] - a[:, 0]) * (c[:, 1] - a[:, 1]) - (b[:, 1] - a[:, 1]) * (c[:, 0] - a[:, 0])
        ok = np.abs(area) > 1e-10
        if ok.any():
            a2, b2, c2 = a[ok], b[ok], c[ok]
            px, py = P[:, None, 0], P[:, None, 1]
            s1 = (b2[:, 0] - a2[:, 0]) * (py - a2[:, 1]) - (b2[:, 1] - a2[:, 1]) * (px - a2[:, 0])
            s2 = (c2[:, 0] - b2[:, 0]) * (py - b2[:, 1]) - (c2[:, 1] - b2[:, 1]) * (px - b2[:, 0])
            s3 = (a2[:, 0] - c2[:, 0]) * (py - c2[:, 1]) - (a2[:, 1] - c2[:, 1]) * (px - c2[:, 0])
            inside = ((s1 >= 0) & (s2 >= 0) & (s3 >= 0)) | ((s1 <= 0) & (s2 <= 0) & (s3 <= 0))
            dd = d[:, ok]
            dd[inside] = 0.0
            d[:, ok] = dd
        best = np.minimum(best, d.min(axis=1))
    return best


def _pinch_parts_m(p, A, B, C, who, reach=1.0):
    """The two parts that pinch the plan point p: the nearest part and the nearest part on the far side of the point (their
    directions from p at >= 90 deg), each [name, distance]. If nothing lies on the far side, the second nearest part. [] when
    nothing is within `reach`."""
    p = np.asarray(p, dtype=np.float64)
    near = {}
    for k in range(len(A)):
        if min(A[k][0], B[k][0], C[k][0]) > p[0] + reach or max(A[k][0], B[k][0], C[k][0]) < p[0] - reach \
                or min(A[k][1], B[k][1], C[k][1]) > p[1] + reach or max(A[k][1], B[k][1], C[k][1]) < p[1] - reach:
            continue
        best = None
        for s0, s1 in ((A[k], B[k]), (B[k], C[k]), (C[k], A[k])):
            d = s1 - s0
            L2 = float(d @ d)
            t = 0.0 if L2 < 1e-18 else min(1.0, max(0.0, float((p - s0) @ d) / L2))
            q = s0 + t * d
            r = float(np.linalg.norm(q - p))
            if best is None or r < best[0]:
                best = (r, q)
        ar = (B[k][0] - A[k][0]) * (C[k][1] - A[k][1]) - (B[k][1] - A[k][1]) * (C[k][0] - A[k][0])
        if abs(ar) > 1e-10:
            c1 = (B[k][0] - A[k][0]) * (p[1] - A[k][1]) - (B[k][1] - A[k][1]) * (p[0] - A[k][0])
            c2 = (C[k][0] - B[k][0]) * (p[1] - B[k][1]) - (C[k][1] - B[k][1]) * (p[0] - B[k][0])
            c3 = (A[k][0] - C[k][0]) * (p[1] - C[k][1]) - (A[k][1] - C[k][1]) * (p[0] - C[k][0])
            if (c1 >= 0 and c2 >= 0 and c3 >= 0) or (c1 <= 0 and c2 <= 0 and c3 <= 0):
                best = (0.0, p)
        if best[0] <= reach and (who[k] not in near or best[0] < near[who[k]][0]):
            near[who[k]] = best
    order = sorted(near.items(), key=lambda kv: (kv[1][0], kv[0]))
    if not order:
        return []
    first = order[0]
    da = first[1][1] - p
    other = None
    for nm, (r, q) in order[1:]:
        if float((q - p) @ da) <= 0.0:
            other = (nm, r)
            break
    if other is None and len(order) > 1:
        other = (order[1][0], order[1][1][0])
    out = [[first[0], round(first[1][0], 3)]]
    if other is not None:
        out.append([other[0], round(other[1], 3)])
    return out


def route_width_m(co, tris, names, kind):
    """M18: every walk route on the bay of the top station is >= M_ROUTE_MIN wide at hip height (model and routes in the block
    comment above). Returns the report (min width, the narrowest route / point / pinching parts, every route, the nodes).
    Raises MContractError("M18 ...") that lists each route under the minimum with its narrowest point and the two parts that
    pinch it. The drive station has no bay: {"applies": False}."""
    if kind != "return":
        return {"applies": False, "why": "the drive station has no bay (its walkways are M7's ladders)"}
    nm = np.array(names)

    def bbox(pred):
        sel = np.nonzero(pred(nm))[0]
        if not len(sel):
            _m_fail("M18", "a part this contract measures from is missing")
        pts = co[np.unique(tris[sel])]
        return pts.min(axis=0), pts.max(axis=0)

    bay_lo, bay_hi = bbox(lambda a: a == "liftiebay")
    hut_lo, hut_hi = bbox(lambda a: a == "hut")
    crest_lo, crest_hi = bbox(lambda a: a == "crest")
    door_lo, door_hi = bbox(lambda a: a == "door_hut_leaf")
    zs = float(bay_hi[2])
    bx0, bx1, by0, by1 = float(bay_lo[0]), float(bay_hi[0]), float(bay_lo[1]), float(bay_hi[1])
    hx0, hx1, hy0, hy1 = float(hut_lo[0]), float(hut_hi[0]), float(hut_lo[1]), float(hut_hi[1])
    door_y = 0.5 * float(door_lo[1] + door_hi[1])
    # a node is the standing cell of best clearance in its box: the crest 0.6-1.6 m beyond the crossing, the 0.8 m wide
    # ground in front of the door, and the four corner pockets around the booth (a walker has to be able to stand and turn in
    # each)
    node_box = {"crest": (float(crest_lo[0]), float(crest_hi[0]), by1 + 0.6, by1 + 1.6),
                "door": (hx1, bx1, door_y - 0.40, door_y + 0.40),
                "NW": (bx0, hx0, hy1, by1), "NE": (hx1, bx1, hy1, by1),
                "SW": (bx0, hx0, by0, hy0), "SE": (hx1, bx1, by0, hy0)}
    routes = [("crest>door", "crest", "door", None), ("crest>NW", "crest", "NW", None),
              ("crest>NE", "crest", "NE", None),
              ("W strip", "NW", "SW", (bx0, hx0, by0, by1)), ("back strip", "SW", "SE", (bx0, bx1, by0, hy0)),
              ("E strip", "SE", "NE", (hx1, bx1, by0, by1)),
              ("front ledge", "NW", "NE", (bx0, bx1, hy1, by1 + 0.8))]
    roi = (bx0 - 0.30, bx1 + 0.30, by0 - 0.30, by1 + 2.30)
    z_lo, z_hi = zs + M_ROUTE_BAND[0], zs + M_ROUTE_BAND[1]
    st = M_ROUTE_STEP
    xs = np.arange(roi[0], roi[1] + 1e-9, st) + 0.0131
    ys_ = np.arange(roi[2], roi[3] + 1e-9, st) + 0.0173
    nx, ny = len(xs), len(ys_)
    bvh = _bvh(co, tris)
    stand = np.zeros((nx, ny), dtype=bool)
    for i in range(nx):
        for j in range(ny):
            z = z_lo - 1e-3
            for _ in range(8):
                h = bvh.ray_cast(_V((float(xs[i]), float(ys_[j]), z)), _V((0.0, 0.0, -1.0)), 10.0)
                if h[0] is None:
                    break
                if h[1].z >= 0.5:
                    stand[i, j] = abs(h[0].z - zs) <= M_ROUTE_FLOOR_TOL
                    break
                z = h[0].z - 1e-4
    A, B, C, who = _band_footprints_m(co, tris, names, z_lo, z_hi,
                                      (roi[0] - M_ROUTE_CAP, roi[1] + M_ROUTE_CAP,
                                       roi[2] - M_ROUTE_CAP, roi[3] + M_ROUTE_CAP))
    clr = np.zeros((nx, ny))
    ii, jj = np.nonzero(stand)
    P = np.stack([xs[ii], ys_[jj]], axis=1)
    clr[ii, jj] = np.minimum(_plan_clearance_m(P, A, B, C) if len(A) else M_ROUTE_CAP, M_ROUTE_CAP)
    X, Y = np.meshgrid(xs, ys_, indexing="ij")

    def best_in(box):
        m = stand & (X >= box[0]) & (X <= box[1]) & (Y >= box[2]) & (Y <= box[3])
        if not m.any():
            return None
        return np.unravel_index(np.argmax(np.where(m, clr, -1.0)), clr.shape)

    def widest(src, dst, box):
        ok = stand.copy()
        if box is not None:
            ok &= (X >= box[0] - 1e-9) & (X <= box[1] + 1e-9) & (Y >= box[2] - 1e-9) & (Y <= box[3] + 1e-9)
        best = np.full((nx, ny), -1.0)
        prev = {}
        best[src] = clr[src]
        heap = [(-float(clr[src]), src)]
        while heap:
            b, (i, j) = _heapq.heappop(heap)
            b = -b
            if b < best[i, j] - 1e-12:
                continue
            if (i, j) == dst:
                break
            for di in (-1, 0, 1):
                for dj in (-1, 0, 1):
                    a, c = i + di, j + dj
                    if (di or dj) and 0 <= a < nx and 0 <= c < ny and ok[a, c]:
                        nb = min(b, float(clr[a, c]))
                        if nb > best[a, c] + 1e-12:
                            best[a, c] = nb
                            prev[(a, c)] = (i, j)
                            _heapq.heappush(heap, (-nb, (a, c)))
        if best[dst] < 0.0:
            return 0.0, []
        path = [dst]
        while path[-1] != src:
            path.append(prev[path[-1]])
        return float(best[dst]), path[::-1]

    snapped = {k: best_in(v) for k, v in node_box.items()}
    nodes = {k: ((float(xs[v[0]]), float(ys_[v[1]])) if v is not None else None) for k, v in snapped.items()}
    rep_routes, bad = {}, []
    worst = None
    for rname, a, b, box in routes:
        if snapped[a] is None or snapped[b] is None:
            rep_routes[rname] = {"from": a, "to": b, "box": None, "width": 0.0, "at": None, "parts": [],
                                 "why": "a node has no standing cell in its box"}
            bad.append(f"route '{rname}': node {a if snapped[a] is None else b} "
                       f"has no standing cell in its box")
            continue
        bc, path = widest(snapped[a], snapped[b], box)
        w = 2.0 * min(bc, M_ROUTE_CAP)
        if path:
            tight = min(path, key=lambda q: (float(clr[q]), q))
            at = (round(float(xs[tight[0]]), 2), round(float(ys_[tight[1]]), 2))
            parts = _pinch_parts_m((xs[tight[0]], ys_[tight[1]]), A, B, C, who)
        else:
            at, parts = None, []
        rep_routes[rname] = {"from": a, "to": b, "box": [round(v, 3) for v in box] if box else None,
                             "width": round(w, 3), "at": list(at) if at else None, "parts": parts}
        if worst is None or w < worst[0]:
            worst = (w, rname)
        if w < M_ROUTE_MIN - 1e-9:
            pn = " and ".join(f"{p[0]} ({p[1]:.2f} m)" for p in parts) or "no route"
            bad.append(f"route '{rname}' is {w:.3f} m wide at {at}: pinched by {pn}")
    if bad:
        raise MContractError(f"M18 bay route narrower than {M_ROUTE_MIN:.2f} m at hip height "
                             f"({M_ROUTE_BAND[0]}-{M_ROUTE_BAND[1]} m over the surface): " + "; ".join(bad))
    w, rname = worst
    r = rep_routes[rname]
    return {"min_width": round(w, 3), "narrowest_route": rname, "narrowest_at": r["at"],
            "narrowest_parts": r["parts"], "routes": rep_routes,
            "nodes": {k: [round(v[0], 3), round(v[1], 3)] for k, v in nodes.items() if v is not None},
            "rule": {"min": M_ROUTE_MIN, "band": list(M_ROUTE_BAND), "floor_tol": M_ROUTE_FLOOR_TOL,
                     "grid": M_ROUTE_STEP, "cap": M_ROUTE_CAP, "surface_z": round(zs, 3),
                     "standing_cells": int(stand.sum()), "plan_triangles": int(len(A))}}


def windows_m(co, tris, names, kind):
    """M10 (geometry half, before the split): >= 1 pane on each of the house's four sides
    (base), the hut / booth glazed on 3 sides + a door lite, and no pane looks into a wall
    within 0.5 m (a ray from each pane center along its normal hits no other part)."""
    pane_pfx = tuple(M_WIN_CELLS[kind])
    keep = [i for i, n in enumerate(names) if not n.startswith(pane_pfx)]
    b = _bvh(co, tris[keep])
    panes = {}
    for i, n in enumerate(names):
        if n.startswith(pane_pfx):
            panes.setdefault(n, []).append(i)
    out, bad = {}, []
    for n, idx in sorted(panes.items()):
        T = co[tris[idx]]
        c = T.reshape(-1, 3).mean(axis=0)
        nv = np.cross(T[0, 1] - T[0, 0], T[0, 2] - T[0, 0])
        nv /= np.linalg.norm(nv)
        hit = b.ray_cast(_V((c + nv * 0.03).tolist()), _V(nv.tolist()), 0.5)
        side = "+x" if nv[0] > 0.9 else "-x" if nv[0] < -0.9 else "+y" if nv[1] > 0.9 else "-y"
        out[n] = {"side": side, "facing_wall_m": None if hit[0] is None else round(hit[3] + 0.03, 3)}
        if hit[0] is not None:
            bad.append(f"{n} faces {names[keep[hit[2]]]} {hit[3] + 0.03:.2f} m away")
    house = {v["side"] for k, v in out.items() if k.startswith("cabwin")}
    hut = {v["side"] for k, v in out.items() if k.startswith("hutwin")}
    if kind == "drive" and house != {"+x", "-x", "+y", "-y"}:
        bad.append(f"house panes on sides {sorted(house)} (want all four)")
    if len(hut) < 3 or not any(n.startswith("doorlite_hut") for n in names):
        bad.append(f"hut / booth panes on {sorted(hut)} (want 3 sides + a door lite)")
    if bad:
        _m_fail("M10", "; ".join(bad))
    return {"panes": out, "house_sides": sorted(house), "hut_sides": sorted(hut)}


# ---------------------------------------------------------------- bake hook ---------------

M_SCALE_CAP = 0.12              # tension-scale digit cap height (m): 48 px at 400 px/m
M_SCALE_WHITE = (0.85, 0.86, 0.88)


def stamp_sign_m(base, ctl, nrm, me, table, kind):
    """M11 sign: the emblem + vector "3" panel (lift_terminal_detach.paint_sign, the own painter of the DQ, unchanged) into the
    ONE sign rect. The code asserts that every face that shares it is pinned and reads unmirrored (base: signband_p and the
    emblem quad of the badge. The owner of the rect is signband_n / the signboard of the top). The plate-edge relief (detach
    sign_relief) is blended into the Normal. Returns the metrics."""
    LTD = _LTD()
    A = base.shape[0]
    lay = sign_layout_m()
    u0, v0, u1, v1 = sign_rect_m()
    want = (int(round(v0 * A)), int(round(v1 * A)), int(round(u0 * A)), int(round(u1 * A)))
    owner = "signband_n" if kind == "drive" else "signboard"
    info = {}

    def painter(rect, base_sl, ctl_sl):
        info["rect"] = [int(v) for v in rect]
        info.update(LTD.paint_sign(base_sl, ctl_sl, lay))
    LTD.check_sign_uv(me, table, owner)
    cs2_compose.stamp_on_face(base, ctl, me, table, owner, painter)
    bad = []
    if tuple(info.get("rect", ())) != want:
        bad.append(f"rect {info.get('rect')} != reserved {list(want)} (not pinned)")
    floor = LTD.SIGN_PX_PER_M * A / float(M_ATLAS) - 1.0
    if info.get("px_per_m", 0.0) < floor:
        bad.append(f"{info.get('px_per_m')} px/m < {floor:.0f}")
    if info.get("digit_texels", 0.0) < 0.8 * info.get("digit_texels_expected", 1.0) or \
            info.get("digit_texels", 0.0) > 1.2 * info.get("digit_texels_expected", 1.0):
        bad.append(f"digit coverage {info.get('digit_coverage_ratio')} outside 0.8..1.2")
    if info.get("digit_aa_levels", 0) < LTD.DIGIT_AA_LEVELS_MIN:
        bad.append(f"{info.get('digit_aa_levels')} anti-aliasing levels (not a vector edge)")
    shared = ["signband_p"] if kind == "drive" else []
    for q in shared:
        LTD.check_sign_uv(me, table, q)
        r_q = cs2_compose.uv_bbox_px(me, table, q, A)
        if tuple(int(v) for v in r_q) != want:
            bad.append(f"{q} rect {r_q} != {list(want)} (not sharing)")
    badge = None
    if kind == "drive":
        LTD.check_sign_uv(me, table, "badge_emblem")
        e = emblem_rect_m()
        r_b = cs2_compose.uv_bbox_px(me, table, "badge_emblem", A)
        want_b = (int(round(e[1] * A)), int(round(e[3] * A)), int(round(e[0] * A)), int(round(e[2] * A)))
        if any(abs(int(a) - b) > 1 for a, b in zip(r_b, want_b)):
            bad.append(f"badge_emblem rect {r_b} != the emblem square {list(want_b)}")
        badge = {"rect": [int(v) for v in r_b], "px_per_m": round((r_b[1] - r_b[0]) / M_BADGE_EMB, 1),
                 "quad_m": M_BADGE_EMB}
        if badge["px_per_m"] < 400.0 * A / float(M_ATLAS):
            bad.append(f"badge emblem {badge['px_per_m']} px/m < 400")
    if bad:
        _m_fail("M11", f"sign {owner}: {bad}")
    # plate-edge relief (Revision 1b of the DQ) on the one rect
    r0, r1, c0, c1 = info["rect"]
    h, w = r1 - r0, c1 - c0
    pu, pv = w / lay["W"], h / lay["H"]
    height, _occ = LTD.sign_relief(lay, h, w, pu, pv)
    sl = nrm[r0:r1, c0:c1]
    det = cs2_compose.height_to_normal(height, 0.5 * (pu + pv))
    n = cs2_compose.blend_rnm(sl[..., 0:3], det) * 2.0 - 1.0
    n /= np.maximum(np.linalg.norm(n, axis=-1, keepdims=True), 1e-6)
    sl[..., 0:3] = n * 0.5 + 0.5
    tilt = np.degrees(np.arccos(np.clip(n[..., 2], -1.0, 1.0)))
    info["relief_tilt_max_deg"] = round(float(tilt.max()), 2)
    if tilt.max() < 10.0:
        _m_fail("M11", f"sign plate-edge relief tilt {tilt.max():.2f} deg < 10")
    info["owner"] = owner
    info["shared_with"] = shared + (["badge_emblem"] if kind == "drive" else [])
    info["badge"] = badge
    info["reserved_uv"] = [round(v, 8) for v in sign_rect_m()]
    return info


def stamp_labels_m(base, ctl, me, table, kind):
    """M11 labels: cs2_liftdress.paint_board into each pinned sheet rect. The labels are upright / unmirrored (detach
    check_label_uv, the seat marks read toward -x). They have >= 400 px/m (380 per axis) scaled with the atlas, T2 at the
    shipping atlas (the dial excepted)."""
    import cs2_liftdress as LD
    LTD = _LTD()
    A = base.shape[0]
    k = A / float(M_ATLAS)
    sheet = label_sheet_m(kind)
    out, bad = {}, []
    for nm, kd in sorted(M_LABELS[kind].items()):
        x0, y0, x1, y1 = sheet[nm]
        want = (int(round(y0 * k)), int(round(y1 * k)), int(round(x0 * k)), int(round(x1 * k)))
        frame = next((f for pre, f in M_FLOOR_FRAME.items() if kd.startswith(pre)), None)
        if frame is None:
            LTD.check_label_uv(me, table, nm)
        else:
            LTD.check_label_uv(me, table, nm, LTD._oriented(frame[0]), LTD._oriented(frame[1]))
        painter = LD.board_painter(kd)
        rect = cs2_compose.uv_bbox_px(me, table, nm, A)
        cs2_compose.stamp_on_face(base, ctl, me, table, nm, painter)
        m = dict(painter.last or {})
        if tuple(int(v) for v in (rect or ())) != want:
            bad.append(f"{nm} rect {rect} != sheet {want}")
        pu, pv = m.get("px_per_m", [0.0, 0.0])
        if math.sqrt(pu * pv) < M_LABEL_PPM * k - 0.5 or min(pu, pv) < 380.0 * k - 0.5:
            bad.append(f"{nm} {pu:.0f} x {pv:.0f} px/m < {M_LABEL_PPM * k:.0f}")
        if A >= M_ATLAS and kd != "tension" and not m.get("t2_ok"):
            bad.append(f"{nm} fails T2")
        m["rect"] = list(want)
        out[nm] = m
    if bad:
        _m_fail("M11", f"labels: {bad}")
    return out


def scale_aa_levels_m(cov):
    """The M11 vector rule of the tension scale: distinct partial-coverage levels (of 64) in the coverage of the painted marks.
    Vector outlines at 8 x 8 samples show dozens. The K / L 3 x 5 bitmap (whole-texel cells) shows none. >= 8 passes (the DQ
    digit rule, DIGIT_AA_LEVELS_MIN)."""
    c = np.asarray(cov, dtype=np.float64)
    part = c[(c > 0.0) & (c < 1.0)]
    return int(np.unique(np.round(part * 64)).size)


_SCALE_TRIS = {}


def scale_digit_tris_m(s):
    """Cached Bfont outline triangles of one scale number (built BEFORE the bake: no FONT object is ever created inside the hook
    of bake_unique, the detach digit rule)."""
    if s not in _SCALE_TRIS:
        import welcome_sign as WS
        _SCALE_TRIS[s] = WS.text_triangles(s, M_SCALE_CAP)[0]
    return _SCALE_TRIS[s]


def stamp_scale_m(base, ctl, me, table):
    """M11 tension scale (A9): white VECTOR digits 1..16 (Bfont outlines, welcome_sign .text_triangles / _tri_coverage at 8 x 8
    samples, the DQ digit path) and a tick per cell into the scale rect at 400 px/m. ControlMask R is zeroed by coverage (the
    digits stay white when the steel is recolored). scale_n owns the rect, scale_p shares it. Each reads left to right from
    its own side (u = viewer right). Replaces the K / L 3 x 5 bitmap."""
    import welcome_sign as WS
    LTD = _LTD()
    A = base.shape[0]
    u0, v0, u1, v1 = scale_rect_m()
    want = (int(round(v0 * A)), int(round(v1 * A)), int(round(u0 * A)), int(round(u1 * A)))
    for q in ("scale_n", "scale_p"):
        LTD.check_sign_uv(me, table, q)
        r_q = cs2_compose.uv_bbox_px(me, table, q, A)
        if tuple(int(v) for v in r_q) != want:
            _m_fail("M11", f"{q} rect {r_q} != the scale band {list(want)} (not pinned / shared)")
    r0, r1, c0, c1 = want
    h, w = r1 - r0, c1 - c0
    pu, pv = w / M_SCALE_LEN, h / M_SCALE_H
    cell = M_SCALE_LEN / 16.0
    area = 0.0
    allt = []
    for i in range(1, 17):
        tris = scale_digit_tris_m(str(i))
        cx, cy = (i - 0.5) * cell, 0.5 * M_SCALE_H + 0.012
        # strip-centered meters (the _tri_coverage frame: origin = the rect center)
        allt += [tuple((x + cx - 0.5 * M_SCALE_LEN, y + cy - 0.5 * M_SCALE_H) for x, y in tri)
                 for tri in tris]
        area += sum(0.5 * abs((b[0] - a[0]) * (c[1] - a[1]) - (c[0] - a[0]) * (b[1] - a[1]))
                    for a, b, c in tris)
    cov = np.asarray(WS._tri_coverage(allt, w, h, pu, pv, ss=8), dtype=np.float32)
    # ticks: one at each cell boundary (0.012 m wide, 0.045 m tall at the bottom edge)
    for i in range(17):
        xa = i * cell
        ja, jb = int(round((xa - 0.006) * pu)), int(round((xa + 0.006) * pu))
        cov[0:int(round(0.045 * pv)), max(ja, 0):min(jb, w)] = 1.0
    white = np.array(_lin_m(M_SCALE_WHITE), np.float32)
    sl = base[r0:r1, c0:c1]
    sl[..., 0:3] = sl[..., 0:3] * (1.0 - cov[..., None]) + white * cov[..., None]
    sl[..., 3] = 1.0
    ctl[r0:r1, c0:c1, 0] *= (1.0 - cov)
    # (the AA rule: scale_aa_levels_m)
    info = {"rect": list(want), "px_per_m": [round(pu, 2), round(pv, 2)], "digits": "1..16",
            "cap_m": M_SCALE_CAP, "digit_px_h": round(M_SCALE_CAP * pv, 1),
            "aa_levels": scale_aa_levels_m(cov), "font": "Bfont (welcome_sign)",
            "shared_with": "scale_p", "glyph_area_m2": round(area, 4)}
    if min(pu, pv) < 400.0 * A / float(M_ATLAS) - 1.0:
        _m_fail("M11", f"tension scale {pu:.0f} x {pv:.0f} px/m < 400")
    if A >= M_ATLAS and info["aa_levels"] < 8:
        _m_fail("M11", f"tension scale digits show {info['aa_levels']} AA levels (not vector)")
    return info


def make_hook_m(t, table, kind, part_material=None):
    """post_compose = make_post_compose (density report, relief with detail_specs_m, compose_maps, normal renormalize, the
    reserved rects keep their baked texels), then the sign + badge, the labels, the tension scale and the 8-bit ControlMask
    exclusivity guard."""
    import cs2_liftdress as LD
    LTD = _LTD()
    rects = list(reserved_rects_m(kind).values())
    if TEXPASS_M:
        if part_material is None:
            raise ValueError("make_hook_m: --texpass needs part_material ({part name: material key})")
        inner = make_post_compose_tp1(t, rects, part_material)
    else:
        inner = make_post_compose(t, rects, specs=detail_specs_m)
    LTD.digit_triangles(sign_layout_m())
    LD.stem_ratio()
    if kind == "drive":
        for i in range(1, 17):
            scale_digit_tris_m(str(i))

    def hook(base, nrm, msk, ctl, ao, mesh, A):
        base, nrm, msk, ctl = inner(base, nrm, msk, ctl, ao, mesh, A)
        hook.detail_stats = json.loads(json.dumps(cs2_compose.LAST_DETAIL_STATS))
        hook.stamps = stamp_sign_m(base, ctl, nrm, mesh, table, kind)
        hook.labels = stamp_labels_m(base, ctl, mesh, table, kind)
        hook.scale = stamp_scale_m(base, ctl, mesh, table) if kind == "drive" else None
        hook.ctl_scaled = LTD.ctl_8bit_exclusive(ctl)
        return base, nrm, msk, ctl

    hook.inner = inner
    hook.stamps = hook.labels = hook.scale = hook.detail_stats = hook.ctl_scaled = None
    return hook


FG_WEATHER = (
    ("ropechord_", {"role": "none"}), ("wheelrope", {"role": "none"}), ("cwrope_", {"role": "none"}),
    ("cwcable_", {"role": "none"}), ("signband_", {"role": "none"}), ("signboard", {"role": "none"}),
    ("scale_", {"role": "none"}), ("badge", {"role": "none"}), ("plate_cap", {"role": "none"}),
    ("loaddeck", {"scuff": 1.3}), ("loadramp", {"scuff": 1.3}), ("crest", {"scuff": 1.2}),
    ("door_", {"rust": 0.0}), ("ladder_", {"role": "galv", "edge": 1.5}),
    ("lrung_", {"role": "galv", "scuff": 1.0}), ("stake_", {"scale": 0.8}),
)
FG_TRAFFIC = ("pad", "loaddeck", "loadramp", "carpet_", "grate_", "crest", "liftiebay", "rampup",
              "rampdown")


# the top is mostly knoll concrete + the column in full view. At 0.40 with the rules of the base it read VL1 0.052 / VL2
# 0.086 (budget 0.05 / 0.08, 2026-10-11 export). The knoll is not walked like a deck (riders ski off it), so it leaves the
# traffic list. Both it and the column weather at 0.75 of the station level (first match wins on an equal prefix: these
# precede FG_WEATHER)
FG_WEATHER_TOP = (("crest", {"scale": 0.75}), ("rampup", {"scale": 0.75}), ("rampdown", {"scale": 0.75}),
                  ("liftiebay", {"scale": 0.75}), ("column", {"scale": 0.75}))


def weather_spec_m(name, kind, intensity):
    """The station preset (DECISIONS 171 via cs2_liftdress.station_weather_spec: slot_wear free, snow 0, edge band 0.08):
    FG_WEATHER (+ FG_WEATHER_TOP on the top) before DRESS_WEATHER_OVERRIDES of the dressing. ground = the pad top (base) /
    grade (top)."""
    import cs2_liftdress as LD
    tp = tuple(TP1_WEATHER) if TEXPASS_M else ()
    if kind == "drive":
        return LD.station_weather_spec(name, ground="grade", ground_z=M_BASE["pad_h"],
                                       overrides=tp + FG_WEATHER + tuple(LD.DRESS_WEATHER_OVERRIDES),
                                       traffic_extra=FG_TRAFFIC, intensity=intensity)
    return LD.station_weather_spec(name, ground="grade", ground_z=0.0,
                                   overrides=tp + FG_WEATHER_TOP + FG_WEATHER + tuple(LD.DRESS_WEATHER_OVERRIDES),
                                   traffic_extra=tuple(p for p in FG_TRAFFIC if p not in
                                                       ("crest", "liftiebay", "rampup", "rampdown")),
                                   intensity=intensity)


# ---------------------------------------------------------------- previews ----------------

M_TINT = {"R": (0.25, 0.26, 0.275), "G": (0.85, 0.25, 0.10), "B": (1.0, 1.0, 1.0)}   # K ColorProperties
M_CLOSE_RES = (2600, 2000)


def _post_m(v):
    """Authoring -> the post-orient Blender frame of the previews ((x, y, z) -> (y, -x, z))."""
    return (v[1], -v[0], v[2])


def chair_preview_poses_m(kind, spacing=2.2):
    """Chairs for the close-ups along the real path: arriving on -y (travel +x), around the wheel, leaving on +y (travel -x).
    Riders face travel. [(path point, travel unit)]."""
    spec = M_SPECS[kind]
    wx, _wy, wz = spec.wheel
    ax, az = spec.anchor_in
    out = []
    L = wx - ax
    n = int(L / spacing)
    for k in range(n + 1):
        x = ax + k * spacing
        if x > wx - 0.5:
            break
        out.append(((x, -FG_LANE, chord_z(spec, x)), (1.0, 0.0, 0.0)))
    for adeg in (-45.0, 0.0, 45.0, 90.0):
        a = math.radians(adeg)
        out.append(((wx + FG_LANE * math.cos(a), FG_LANE * math.sin(a), wz), (-math.sin(a), math.cos(a), 0.0)))
    for k in range(1, n + 1):
        x = wx - k * spacing + 0.6
        if x < ax + 0.3:
            break
        out.append(((x, FG_LANE, chord_z(spec, x)), (-1.0, 0.0, 0.0)))
    return out


def _pose_hosted_raw_m(src, a_deg, pivot_y_cm):
    """T256 previews: the skinned hosted tower's TrainBack (raw z < 0) pitched by a_deg, its outer (station-side) end down,
    about its head (raw z 0, y pivot_y_cm), in the FBX raw (cm) mesh. Same pose as load_tower_m."""
    gi = {g.index: g.name for g in src.vertex_groups}
    c, s_ = math.cos(math.radians(a_deg)), math.sin(math.radians(a_deg))
    n = 0
    for v in src.data.vertices:
        if any(gi.get(g.group) == "TrainBack" and g.weight > 0.5 for g in v.groups):
            dx, dz = v.co.z, v.co.y - pivot_y_cm
            v.co.z, v.co.y = c * dx - s_ * dz, pivot_y_cm + s_ * dx + c * dz
            n += 1
    if not n:
        raise MContractError("RIG-hosted: no TrainBack vertices in the hosted tower FBX")
    src.data.update()


def _standin_from_fbx(rig, path, name_prefix, mats, poses, frame, prep=None):
    """Import an FBX once (cs2_validate.import_fbx_temp) and link render-only instances: each
    pose -> a 4x4 from the FBX raw (cm) frame to the post-orient preview frame. `frame` builds
    the authoring 3x3 + offset from a pose."""
    import cs2_validate
    objs = cs2_validate.import_fbx_temp(path)
    src = [o for o in objs if o.type == "MESH"][0]
    for o in objs:
        o.hide_render = True
        rig.track(o)
    if prep is not None:
        prep(src)
    src.data.materials.clear()
    for m in mats:
        src.data.materials.append(m)
    A = np.array([[0.0, 0.0, 1.0], [1.0, 0.0, 0.0], [0.0, 1.0, 0.0]]) / 100.0     # raw -> authoring
    P = np.array([[0.0, 1.0, 0.0], [-1.0, 0.0, 0.0], [0.0, 0.0, 1.0]])            # authoring -> post
    out = []
    for i, pose in enumerate(poses):
        R, off = frame(pose)
        M3 = P @ R @ A
        Mw = np.eye(4)
        Mw[:3, :3] = M3
        Mw[:3, 3] = P @ np.asarray(off, dtype=np.float64)
        o = rig.track(bpy.data.objects.new(f"{name_prefix}{i:02d}", src.data))
        bpy.context.collection.objects.link(o)
        o.matrix_world = mathutils.Matrix([[float(v) for v in r] for r in Mw])   # rows (a nested list is read column-major)
        o.hide_render = False
        out.append(o)
    return out


def render_closeups_m(t, asset_dir, name, out_dir, kind, win=None, chairs=True, shots=None, extras=None):
    """2600 x 2000 close-ups at noon with the REAL SkiLiftChair6M (its own textures) along the
    code path and the hosted end tower (ET68O / ET62O, its own textures) at its pose, the `_Win`
    panes as dark glass (render-only stand-ins, removed by the rig)."""
    rig = cs2_preview._Rig(t, None)
    written = []
    os.makedirs(out_dir, exist_ok=True)
    spec = M_SPECS[kind]
    try:
        cam = cs2_preview._make_camera(rig)
        cs2_preview._make_world(rig, **cs2_preview.NOON)
        cs2_preview._make_ground(rig)
        cs2_preview._assign(t, cs2_preview._pbr_material(rig, asset_dir, name, M_TINT))
        for eo, rgba in (extras or ()):        # T256: render-only helpers (the posed line rope), the caller removes them
            em = rig.track_data(bpy.data.materials, bpy.data.materials.new(f"{eo.name}_PreviewM"))
            em.use_nodes = True
            eb = next(n for n in em.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
            eb.inputs["Base Color"].default_value = rgba
            eb.inputs["Roughness"].default_value = 0.7
            eo.data.materials.clear()
            eo.data.materials.append(em)
            eo.hide_render = False
        if win is not None:
            gm = rig.track_data(bpy.data.materials, bpy.data.materials.new("StandInGlassM"))
            gm.use_nodes = True
            gb = next(n for n in gm.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
            gb.inputs["Base Color"].default_value = (0.03, 0.05, 0.07, 1.0)
            gb.inputs["Roughness"].default_value = 0.08
            wo = rig.track(bpy.data.objects.new("standin_win_m", win.data.copy()))
            bpy.context.collection.objects.link(wo)
            wo.matrix_world = win.matrix_world.copy()
            wo.data.materials.clear()
            wo.data.materials.append(gm)
            wo.hide_render = False
        # the hosted end tower (none on the art-601 nose-train base)
        tdir = os.path.join(IMPORT_M, "towers", "O", spec.tower)
        tname, tx = spec.tower, spec.tower_x
        nose = M_NOSE_TRAIN and kind == "drive"
        if M_RIG and kind == "return":      # T256: the skinned hosted end tower at rest, when its folder is given
            nose = not RIG_HOSTED_DIR
            if RIG_HOSTED_DIR:
                tdir = os.path.abspath(RIG_HOSTED_DIR)
                tname, tx = os.path.basename(os.path.normpath(tdir)), RIG_HOSTED_X
        tm = cs2_preview._pbr_material(rig, tdir, tname, M_TINT, mat_name=f"{tname}_PreviewM")

        def tower_frame(_pose):
            R = np.array([[-1.0, 0.0, 0.0], [0.0, -1.0, 0.0], [0.0, 0.0, 1.0]])
            return R, (tx, 0.0, 0.0)
        prep = None
        if M_RIG and kind == "return" and RIG_HOSTED_DIR:
            def prep(src):
                _pose_hosted_raw_m(src, rig_hosted_pitch_m(spec), spec.anchor_out[1] * 100.0)
        if not nose:
            _standin_from_fbx(rig, os.path.join(tdir, f"{tname}.fbx"), "standin_tower_", [tm], [None],
                              tower_frame, prep=prep)
        if chairs:
            cm = cs2_preview._pbr_material(rig, CHAIR_M_DIR, "SkiLiftChair6M", M_TINT,
                                           mat_name="SkiLiftChair6M_PreviewM")

            def chair_frame(pose):
                q, d = pose
                d = np.asarray(d, dtype=np.float64) / np.linalg.norm(d)
                up = np.array([0.0, 0.0, 1.0])
                R = np.stack([d, np.cross(up, d), up], axis=1)
                return R, np.asarray(q, dtype=np.float64) - np.array([0.0, 0.0, FG_HANG])
            _standin_from_fbx(rig, os.path.join(CHAIR_M_DIR, "SkiLiftChair6M.fbx"), "standin_chair_",
                              [cm], chair_preview_poses_m(kind), chair_frame)
        sc = rig.scene
        sc.render.resolution_x, sc.render.resolution_y = M_CLOSE_RES
        for vname, tgt, eye in (shots or closeup_shots_m(kind)):
            tg, ey_ = _V(_post_m(tgt)), _V(_post_m(eye))
            cs2_preview._aim(cam, tg, ey_ - tg, (ey_ - tg).length)
            path = os.path.join(out_dir, f"{name}_closeup_{vname}.png")
            written.append(cs2_preview._render(rig, path, engine="EEVEE"))
            print(f"[preview M] closeup -> {path}")
    finally:
        rig.restore()
    return written


def closeup_shots_m(kind):
    """(name, target, eye) in the authoring frame.

    Base: the loading area (the seat height over the carpet), the chair sweep through the front portal / hold-down booms /
    chord over the beam, the badge, the catwalk + both ladders, the house door / gauge, the counterweight, the hut, the sign.

    Top: the unload crest, the column + wheel, the booth bay, the stakes, the chair sweep, the sign."""
    if kind == "drive":
        return [("loading_area", (0.0, 1.8, 1.0), (2.6, 5.6, 2.7)),
                ("chair_sweep", (-2.2, 0.0, 3.4), (-5.5, -11.5, 5.0)),
                ("front_portal", (-3.6, 0.0, 5.1), (-11.0, 3.5, 5.2)),
                ("badge", (-3.5, 0.0, 5.6), (-6.5, -0.6, 5.4)),
                ("catwalk_ladders", (4.6, 1.8, 6.2), (9.8, 6.8, 7.4)),
                ("house_door", (1.4, 1.3, 6.4), (2.2, 6.6, 6.9)),
                ("counterweight", (7.2, 0.0, 3.2), (12.5, -6.8, 3.8)),
                ("hut", (-1.2, 5.15, 1.3), (3.0, 8.4, 2.2)),
                ("sign_scale", (0.6, -1.3, 5.9), (0.3, -7.5, 6.2))]
    return [("unload_crest", (0.0, -1.8, 1.4), (-5.6, -6.0, 2.8)),
            ("chair_sweep", (-1.6, 0.0, 3.2), (-5.0, -10.5, 4.4)),
            ("column_wheel", (0.0, 0.0, 2.6), (4.6, 4.2, 2.3)),
            ("booth_bay", (0.1, -4.8, 2.0), (5.0, -10.2, 3.6)),
            ("bay_routes", (0.1, -4.6, 1.2), (1.6, -8.4, 6.8)),
            ("stakes", (0.1, 3.05, 1.8), (-3.4, 7.8, 2.6)),
            ("sign", (-1.86, -5.05, 0.5), (-6.8, -4.25, 1.5))]


def lod1_drop_m(table):
    """The LOD1 deletions as exact prefixes: M_LOD1_DELETE + the dressing's own names
    (cs2_liftdress.lod_split names, applied to whatever this table holds)."""
    import cs2_liftdress as LD
    dress = [n for n in table if n.startswith(tuple(LD.DRESS_LOD1_DELETE) + tuple(LD.DRESS_LOD1_KEEP))
             and n.startswith(("maze_", "gate_", "board_", "label_", "stripe_", "chev_", "carpet_", "ext_",
                               "tool_", "skirack_", "ski_", "estop_", "gauge_", "hosereel_"))]
    return tuple(M_LOD1_DELETE) + tuple(sorted(dress)) + (RIG_LOD1_DELETE if M_RIG else ())


def lod2_drop_m(table):
    return lod1_drop_m(table) + tuple(M_LOD2_DELETE) + (RIG_LOD2_DELETE if M_RIG else ())


def contracts_m(t, table, kind, sweep=True):
    """Every geometry contract on the joined AUTHORING-frame LOD0 (before orient_for_net): M1, M2, M3, M4, M5, M7, M8, M9, M12,
    M19 (forbidden boards) and, on the top station, M18 (bay route widths). Raises MContractError on the first failure.
    Returns the reports for the manifest."""
    spec = M_SPECS[kind]
    co, tris, names, _tb = m_arrays(t)
    # fine-detail step 3c: the dressing that left the mesh as kit rows still stands there. The chair sweep (M3, class dress),
    # the crossings (M7) and the bay routes (M18) run on the station + the placed REAL kit meshes (cs2_kitrows.augment /
    # probe_object, parts kitrow_*). Everything else runs on the station mesh alone
    kit = fd_kit_rows_m(kind)
    if kit:
        import cs2_kitrows as KR
        aco, atris, anames = KR.augment(co, tris, names, kit, m_host)
    else:
        aco, atris, anames = co, tris, names
    out = {}
    out["M1"] = m1_frozen(kind)
    out["M2"] = m2_rope(co, tris, names, spec)
    if sweep:
        extra = [(-9.0, 7.04), (-12.0, 7.04)] if kind == "return" else []
        rep = chair_sweep_m(aco, atris, anames, spec, extra_anchors=extra, collect=True)
        cloud = rep.pop("_cloud")
        assert_sweep_m(aco, atris, anames, spec, rep)
        out["M3"] = rep
    else:
        cloud = None
    out["M4"] = load_window_m(co, tris, names, spec, kind)
    out["M5"] = seam_m(co, tris, names, kind, lod1_drop=lod1_drop_m(table))
    probe = KR.probe_object(t, kit, m_host, cs2_asset.PART_TABLE_PROP, name=f"{t.name}KitProbe") if kit else None
    try:
        out["M7"] = reach_m(probe if probe is not None else t, kind)
    finally:
        if probe is not None:
            KR.remove_probe(probe)
    out["M8"] = clash_m(t)
    out["M9"] = lot_m(co, kind)
    out["M12"] = subobj_check_m(co, tris, names, kind, cloud=cloud)
    out["M19"] = board_forbid_m(names, kind)
    # nitpick A (2026-10-13): M20 the layout around the up lane, M21 the sheave trains of the towers
    out["M20"] = orientation_m(co, tris, names, kind)
    out["M21"] = sheave_trains_m(co, tris, names, kind)
    if kit:
        msg, nums = check_kit_rows_m(co, tris, names, table, kind, cloud=cloud)
        out["F1"] = {"message": msg.get("F1"), "rows": (nums or {}).get("rows")}
    if kind == "return":                      # M18: the top station's bay (the drive station has none)
        out["M18"] = route_width_m(aco, atris, anames, kind)
    for k in sorted(out):
        v = out[k]
        brief = {kk: v[kk] for kk in list(v)[:4]} if isinstance(v, dict) else f"{len(v)} rows"
        print(f"[contract M] {k} ok: {json.dumps(brief, default=str)[:300]}")
    return out

# ---------------------------------------------------------------- bounds policy (MC4) -----

def k_mesh_bounds_m(kind):
    """The deployed K mesh prefab's importer-written m_Bounds (game frame)."""
    spec = M_SPECS[kind]
    d = os.path.join(GAMEDATA_M, "c030ee3ff256df91", spec.k_mesh)
    f = [x for x in sorted(os.listdir(d)) if x.endswith(".Prefab")][0]
    with open(os.path.join(d, f), "r", encoding="utf-8-sig") as fh:
        txt = fh.read()
    m = _re.search(r'"m_Bounds".*?"min".*?"x":\s*([-0-9.Ee]+),\s*"y":\s*([-0-9.Ee]+),\s*"z":\s*([-0-9.Ee]+)'
                   r'.*?"max".*?"x":\s*([-0-9.Ee]+),\s*"y":\s*([-0-9.Ee]+),\s*"z":\s*([-0-9.Ee]+)', txt, _re.S)
    v = [float(x) for x in m.groups()]
    return {"min": v[:3], "max": v[3:], "file": f"c030ee3ff256df91/{spec.k_mesh}/{f}"}


def bounds_block_m(kind, lo, hi):
    """MC4 bounds policy: the bounds of the M mesh in the game frame vs those of the deployed K mesh prefab. The BUILDING prefab
    (the frozen file) carries no bounds. The M mesh prefab gets its own, importer-written, at import (y min clamped to -0.01
    at surgery, as K). This is a recorded deviation pending the orchestrator's ack. Nothing in the mod reads station bounds
    (the only ObjectGeometryData reader is SkiPrefabCatalogSystem.cs:849, carriers). The footprint is the lot (M9 keeps every
    vertex inside it). The use of the mesh bounds of a building by vanilla itself (culling, selection) is unverified."""
    corners = np.array([m_to_game((x, y, z)) for x in (lo[0], hi[0]) for y in (lo[1], hi[1])
                        for z in (lo[2], hi[2])])
    gmin, gmax = corners.min(axis=0), corners.max(axis=0)
    k = k_mesh_bounds_m(kind)
    grow = {ax: [round(float(min(0.0, gmin[i] - k["min"][i])), 3), round(float(max(0.0, gmax[i] - k["max"][i])), 3)]
            for i, ax in enumerate("xyz")}
    return {"m_game": {"min": [round(float(v), 3) for v in gmin], "max": [round(float(v), 3) for v in gmax]},
            "k_mesh_prefab": k, "grows_beyond_k": grow,
            "policy": "recorded deviation (skeptic MC4): the building prefab has no bounds field; the "
                      "repoint changes m_Mesh only, the M mesh prefab's bounds are the importer's; "
                      "mod code reads no station bounds (SkiPrefabCatalogSystem.cs:849 = carriers only); "
                      "the footprint is the lot. Orchestrator ack PENDING; vanilla use unverified."}


def engine_ring_m(t, kind, label="LOD0"):
    """M6 ground ring (cs2_validate.ground_ring_report on the ORIENTED mesh): verdict OK, every
    M_ENGINE_BASE part among the OK parts, 0 blocked edges, 0 verts below -1 cm."""
    r = cs2_validate.ground_ring_report(t)
    miss = [p for p in M_ENGINE_BASE[kind] if p not in r["ok_parts"]]
    if r["verdict"] != "OK" or miss or r["blocked_edges"] or r["below_verts"]:
        _m_fail("M6", f"engine base on {label}: {r['verdict']}, missing {miss}, {r['blocked_edges']} "
                      f"blocked, {r['below_verts']} below, offending {r['offending_parts']}")
    return r


# ---------------------------------------------------------------- T256 rig: bones, contracts, LOD, skin -

def rig_net_m(a):
    """Authoring -> net frame (cs2_asset.orient_for_net: (x, y, z) -> (y, -x, z))."""
    return (float(a[1]), -float(a[0]), float(a[2]))


def rig_heads_m(kind):
    """{bone: net-frame head} in bone order (Root first): Bullwheel on the wheel center, NoseTrain (base) on the lane end
    (the main pin's line, x 0). Game = (-x, z, -y) of net: base Bullwheel (0, 3.55, 0.20), NoseTrain (0, 5.84, -7.025), top
    Bullwheel (0, 3.75, 0)."""
    spec = M_SPECS[kind]
    out = {"Root": (0.0, 0.0, 0.0), "Bullwheel": rig_net_m(spec.wheel)}
    if kind == "drive":
        px, pz = spec.anchor_out
        out["NoseTrain"] = rig_net_m((px, 0.0, pz))
    return {b: out[b] for b in RIG_BONES[kind]}


def rig_part_bones_m(table, kind):
    """{part: bone} of the rig (parts not listed ride Root). Raises when a bone gets no part or the bullwheel misses a part."""
    out = {n: "Bullwheel" for n in table if n.startswith(RIG_BULL_PARTS)}
    if kind == "drive":
        out.update({n: "NoseTrain" for n in rig_nose_parts_m(table)})
    want = {"sheave_bull_rim", "wheelhub", "wheelhub_cap"} | {f"spoke_{k:02d}" for k in
                                                                           range(RIG_WHEEL["spokes"])}
    miss = sorted(want - set(out))
    if miss:
        raise MContractError(f"RIG: bullwheel parts missing {miss}")
    for b in RIG_BONES[kind][1:]:
        if b not in out.values():
            raise MContractError(f"RIG: bone {b} rides no part")
    return out


def _rig_bone_masks_m(t, table, kind):
    """(names per face, face bone index, vertex bone index, bones) on a tagged mesh: cs2_skin.face_bones (raises when a
    vertex is shared by two bones)."""
    import cs2_skin
    bones = list(RIG_BONES[kind])
    pb = rig_part_bones_m(table, kind)
    try:
        names, fb, vb = cs2_skin.face_bones(t, table, pb, bones)
    except cs2_skin.SkinError as e:
        raise MContractError(f"RIG one bone per vertex: {e}") from None
    return names, fb, vb, bones, pb


def rig_pose_np_m(co, vb, bones, angles, heads, frame="net"):
    """Coordinates with each bone's vertices turned: Bullwheel `angles["Bullwheel"]` deg about the vertical through its head
    (the bone's local Y), NoseTrain `angles["NoseTrain"]` deg about the lateral through its head (the bone's local X = net X:
    cs2_skin.rotate_x; + raises the line-side end). frame "net" (heads as given) or "authoring" (co authoring: the same turn
    expressed there)."""
    import cs2_skin
    out = np.array(co, dtype=np.float64, copy=True)
    for i, b in enumerate(bones):
        a = float(angles.get(b, 0.0))
        m = vb == i
        if i == 0 or not a or not m.any():
            continue
        h = heads[b]
        if frame == "authoring":             # net (y, -x, z): the head back in authoring
            h = (-h[1], h[0], h[2])
        if b == "NoseTrain":
            if frame == "net":
                out[m] = cs2_skin.rotate_x(co[m], a, (h[1], h[2]))
            else:
                c, s = math.cos(math.radians(a)), math.sin(math.radians(a))
                x, z = co[m, 0] - h[0], co[m, 2] - h[2]
                out[m, 0] = c * x + s * z + h[0]
                out[m, 2] = -s * x + c * z + h[2]
        else:
            # net: +a about +Z (x -> y). Authoring (net y = ax, net x = -ay... ): the same physical turn
            c, s = math.cos(math.radians(a)), math.sin(math.radians(a))
            x, y = co[m, 0] - h[0], co[m, 1] - h[1]
            out[m, 0] = c * x - s * y + h[0]
            out[m, 1] = s * x + c * y + h[1]
    return out


def rig_rope_path_m(co, tris, names, spec):
    """T256 contract (b): the modeled rope on the carrier path within 1 mm. Each ropechord_ vertex lies M_ROPE_R (+-1 mm) off
    the straight lane end -> tangent (wx, +-1.8, wz), between its ends; each wheelrope ring (RIG_ROPE_SIDES vertices of one
    major angle) is centered on the level arc r M_TORUS_R about the wheel center (1 mm), its vertices M_ROPE_R (1 mm) off
    the ring center. Reports the hexagon's flats and the torus chord sagitta."""
    wx, _wy, wz = spec.wheel
    ax, az = spec.anchor_in
    r = M_ROPE_R
    rep = {"rope_r_m": r, "sides": RIG_ROPE_SIDES, "chord_from": "lane end (anchor_out)", "lane_end": [ax, az]}
    worst_r, worst_end = 0.0, 0.0
    for side, s in (("n", -1.0), ("p", 1.0)):
        sel = np.unique(tris[[n == f"ropechord_{side}" for n in names]])
        C = co[sel]
        a = np.array([ax, s * FG_LANE, az])
        b = np.array([wx, s * FG_LANE, wz])
        L = np.linalg.norm(b - a)
        u = (b - a) / L
        v = C - a
        t_ = v @ u
        dist = np.linalg.norm(v - np.outer(t_, u), axis=1)
        worst_r = max(worst_r, float(np.abs(dist - r).max()))
        ends = [C[t_ < L / 2.0].mean(axis=0), C[t_ >= L / 2.0].mean(axis=0)]
        worst_end = max(worst_end, float(np.linalg.norm(ends[0] - a)), float(np.linalg.norm(ends[1] - b)))
        if len(C) != 2 * RIG_ROPE_SIDES:
            _m_fail("RIG-rope", f"ropechord_{side}: {len(C)} vertices, want {2 * RIG_ROPE_SIDES}")
    T = co[np.unique(tris[[n == "wheelrope" for n in names]])]
    ang = np.degrees(np.arctan2(T[:, 1], T[:, 0] - wx))
    step = 180.0 / RIG_TORUS_MAJORS
    ring = np.round((ang + 90.0) / step).astype(int)
    worst_c, worst_v = 0.0, 0.0
    for i in range(RIG_TORUS_MAJORS + 1):
        R_ = T[ring == i]
        if len(R_) != RIG_ROPE_SIDES:
            _m_fail("RIG-rope", f"wheelrope ring {i}: {len(R_)} vertices, want {RIG_ROPE_SIDES}")
        c = R_.mean(axis=0)
        a = math.radians(-90.0 + i * step)
        want = np.array([wx + M_TORUS_R * math.cos(a), M_TORUS_R * math.sin(a), wz])
        worst_c = max(worst_c, float(np.linalg.norm(c - want)))
        worst_v = max(worst_v, float(np.abs(np.linalg.norm(R_ - c, axis=1) - r).max()))
    rep.update(chord_radius_dev_m=round(worst_r, 5), chord_end_dev_m=round(worst_end, 5),
               ring_center_dev_m=round(worst_c, 5), ring_radius_dev_m=round(worst_v, 5),
               torus_segments=RIG_TORUS_MAJORS,
               torus_sagitta_m=round(M_TORUS_R * (1.0 - math.cos(math.pi / (2.0 * RIG_TORUS_MAJORS))), 5),
               hexagon_across_corners_m=round(2.0 * r, 4), hexagon_across_flats_m=round(2.0 * r * math.cos(math.pi / 6), 4))
    bad = [k for k in ("chord_radius_dev_m", "chord_end_dev_m", "ring_center_dev_m", "ring_radius_dev_m")
           if rep[k] > 0.001]
    if bad:
        _m_fail("RIG-rope", f"rope off the carrier path by more than 1 mm: {[(k, rep[k]) for k in bad]}")
    rep["ok"] = True
    return rep


def rig_entry_m(co, tris, names, spec, vb, kind):
    """T256 contract (d): each entry sheave's rim touches the rope at the tangent point: its center on entry_center_m (1 mm),
    r + rope off BOTH the chord axis and the level arc's axis (1 mm), its lowest crest point on the rope's top (1 mm). And
    the turning bullwheel (its revolved envelope: every angle at once) clears every static part around it by
    RIG_ENTRY["clear"] (the coaxial shafts and the rope in the rim groove excepted, reported)."""
    E = RIG_ENTRY
    wx, _wy, wz = spec.wheel
    ax, az = spec.anchor_in
    cx, cz = entry_center_m(spec)
    rep = {"center_xz": [cx, cz], "r": E["r"], "w": E["w"], "lanes": {}}
    bad = []
    u = np.array([wx - ax, 0.0, wz - az])
    u /= np.linalg.norm(u)
    for side, s in (("n", -1.0), ("p", 1.0)):
        sel = np.unique(tris[[n == f"sheave_entry_liner_{side}" for n in names]])
        L_ = co[sel]
        c = (L_.min(axis=0) + L_.max(axis=0)) / 2.0
        crest = co[np.unique(tris[[n == f"sheave_entry_{side}_rim" for n in names]])]
        low = float(crest[:, 2].min())
        T0 = np.array([wx, s * FG_LANE, wz])
        v = c - T0
        d_chord = float(np.linalg.norm(v - (v @ u) * u))
        d_level = float(c[2] - wz)
        row = {"center": [round(float(x), 4) for x in c], "to_chord_axis_m": round(d_chord, 4),
               "to_level_axis_m": round(d_level, 4), "rim_low_over_rope_top_m": round(low - (wz + M_ROPE_R), 4)}
        rep["lanes"][side] = row
        want = E["r"] + M_ROPE_R
        if (abs(c[0] - cx) > 1e-3 or abs(c[2] - cz) > 1e-3 or abs(c[1] - s * FG_LANE) > 1e-3
                or abs(d_chord - want) > 1e-3 or abs(d_level - want) > 1e-3 or abs(low - (wz + M_ROPE_R)) > 1e-3):
            bad.append(f"{side}: {row} (want r + rope {want:.3f})")
    # the bullwheel's revolved envelope (z bins of 5 mm, the max radius of its vertices there and in both neighbors)
    bw = co[vb == 1]
    rb, zb = np.hypot(bw[:, 0] - wx, bw[:, 1]), bw[:, 2]
    z0 = float(zb.min())
    nb = int(math.ceil((float(zb.max()) - z0) / 0.005)) + 1
    rmax = np.zeros(nb)
    np.maximum.at(rmax, np.clip(((zb - z0) / 0.005).astype(int), 0, nb - 1), rb)
    rmax = np.maximum(rmax, np.maximum(np.r_[0.0, rmax[:-1]], np.r_[rmax[1:], 0.0]))
    coax = ("wheelshaft", "bearing")     # round 2 (S2): the top's hub post (entrypost) is gone
    stat_f = vb[tris[:, 0]] == 0
    tv = co[tris]
    rt = np.hypot(tv[:, :, 0] - wx, tv[:, :, 1]).min(axis=1)
    cand = stat_f & (rt < 2.0) & (tv[:, :, 2].max(axis=1) >= z0 - 0.05) & (tv[:, :, 2].min(axis=1) <= z0 + nb * 0.005 + 0.05)
    by = {}
    for i in np.nonzero(cand)[0]:
        by.setdefault(names[i], []).append(i)
    near = {}
    for nm, idx in sorted(by.items()):
        P = _edge_points(co, tris[idx], 0.02)
        rp, zp = np.hypot(P[:, 0] - wx, P[:, 1]), P[:, 2]
        inb = (zp >= z0) & (zp <= z0 + nb * 0.005)
        if not inb.any():
            continue
        k = np.clip(((zp[inb] - z0) / 0.005).astype(int), 0, nb - 1)
        gap = rp[inb] - rmax[k]
        g = float(gap.min())
        if g < 0.05:
            near[nm] = round(g, 4)
    rep["bullwheel_envelope_min_m"] = near
    for nm, g in near.items():
        if nm.startswith(coax) or nm.startswith(RIG_ROPE_PARTS):
            continue
        if g < E["clear"] - 1e-6:
            bad.append(f"{nm} {g:.4f} m from the turning bullwheel's envelope (< {E['clear']})")
    rep["exempt"] = "coaxial shafts (inside the hub, turn-invariant) and the rope in the rim groove"
    if bad:
        _m_fail("RIG-entry", "; ".join(bad))
    rep["ok"] = True
    return rep


RIG_NOSE_GRID = (-40.0, -30.0, -20.0, -10.0, 10.0, 20.0, 30.0, 40.0)


def rig_nose_sweep_m(co, tris, names, spec, vb, heads):
    """T256 contract (c): the NoseTrain joint sweep (G6 rule): at every pitch of RIG_NOSE_GRID the outer half (its posed
    vertices + edge samples every 2 cm, both ways) keeps RIG_NOSE["clear"] from every static part near the pin and overlaps
    none (BVH). Exempt: the outer half's yoke straps vs the main pin they hang on (a lug round its pin). Then the chair
    (LOD0) swept along the POSED line rope (lane end out 2.5 m at the pitch) and back along the in-station chord 1.5 m, at
    RIG_NOSE_CHAIR_THETAS, both lanes and facings, holds M3's rules (body >= M_CLEAR_STRUCT / M_CLEAR_DRESS / 0.01 machine,
    grip only on the rope machine) within +-RIG_NOSE["chair_tmax"]; wider pitches are reported (envelope_deg), not gated."""
    px, pz = spec.anchor_out
    mov_f = np.array([vb[t_[0]] == 2 for t_ in tris])
    mov = tris[mov_f]
    mov_names = [names[i] for i in np.nonzero(mov_f)[0]]
    stat_idx = [i for i in np.nonzero(~mov_f)[0]]
    box_lo, box_hi = np.array([px - 1.6, -2.1, pz - 0.8]), np.array([px + 1.6, 2.1, pz + 1.8])
    tv = co[tris[stat_idx]]
    inside = np.all((tv >= box_lo) & (tv <= box_hi), axis=2).any(axis=1)
    keep = [stat_idx[k] for k in np.nonzero(inside)[0]]
    st_all = tris[keep]
    st_names = [names[i] for i in keep]
    yoke = np.array([n.startswith(M_HOLD["prefix"] + "yoke_") for n in mov_names])
    pin = np.array([n.startswith(M_HOLD["prefix"] + "pivot_main_") for n in st_names])
    S_free = st_all[~pin]
    S_pin = st_all[pin]
    sb_free, sb_all = _bvh(co, S_free), _bvh(co, st_all)
    sp_free, sp_all = _edge_points(co, S_free, 0.02), _edge_points(co, st_all, 0.02)
    rows = []
    worst = (9.9, None)
    for th in RIG_NOSE_GRID:
        pc = rig_pose_np_m(co, vb, ["Root", "Bullwheel", "NoseTrain"], {"NoseTrain": th}, heads, frame="authoring")
        row = {"theta_deg": th}
        for lab, M_, sb, sp in (("plates_vs_all_but_pin", mov[yoke], sb_free, sp_free),
                                ("rest_vs_all", mov[~yoke], sb_all, sp_all)):
            if not len(M_):
                continue
            mb = _bvh(pc, M_)
            if mb.overlap(sb):
                _m_fail("RIG-nose", f"NoseTrain {lab} overlaps a static part at {th:+.0f} deg")
            d1, _i = _min_dist(sb, _edge_points(pc, M_, 0.02), 0.10)
            lo, hi = pc[np.unique(M_)].min(axis=0) - 0.12, pc[np.unique(M_)].max(axis=0) + 0.12
            m = np.all((sp >= lo) & (sp <= hi), axis=1)
            d2 = _min_dist(mb, sp[m], 0.10)[0] if m.any() else 0.10
            d = min(d1, d2)
            row[lab] = round(float(d), 4)
            if d < worst[0]:
                worst = (d, f"{lab} at {th:+.0f} deg")
        rows.append(row)
    if worst[0] < RIG_NOSE["clear"] - 1e-6:
        _m_fail("RIG-nose", f"NoseTrain {worst[0]:.4f} m from a static part ({worst[1]}) < {RIG_NOSE['clear']}")
    # the chair along the posed line rope
    wx, _wy, wz = spec.wheel
    u = np.array([wx - px, wz - pz])
    u /= np.linalg.norm(u)
    chair = {}
    for th in RIG_NOSE_CHAIR_THETAS:
        pc = rig_pose_np_m(co, vb, ["Root", "Bullwheel", "NoseTrain"], {"NoseTrain": th}, heads, frame="authoring")
        c, s_ = math.cos(math.radians(th)), math.sin(math.radians(th))
        dx, dz = -u[0], -u[1]
        lx, lz = c * dx + s_ * dz, -s_ * dx + c * dz          # the line rope's outward direction at this pitch
        poses = []
        for side in (-1.0, 1.0):
            for k in range(-15, 26):
                d = k * 0.10
                if d < 0.0:
                    q = np.array([px - d * u[0], side * FG_LANE, pz - d * u[1]])
                else:
                    q = np.array([px + d * lx, side * FG_LANE, pz + d * lz])
                for f in (1.0, -1.0):
                    poses.append((f"a0_nose_lane{int(side):+d}", q, np.array([f, 0.0, 0.0]), f))
        rep = chair_sweep_m(pc, tris, names, spec, levels=(0,), poses=poses)
        err = None
        try:
            assert_sweep_m(pc, tris, names, spec, rep)
        except MContractError as e:
            err = str(e)
            if abs(th) <= RIG_NOSE["chair_tmax"] + 1e-9:
                raise MContractError(f"RIG-nose chair at NoseTrain {th:+.0f} deg: {e}") from None
        chair[f"{th:+.0f}"] = {"poses": rep["poses"], "min": rep["min"], "grip_min": rep["grip_min"],
                               "ok": err is None, **({"fail": err[:240]} if err else {})}
    env = [th for th in RIG_NOSE_CHAIR_THETAS if all(chair[f"{x:+.0f}"]["ok"]
                                                     for x in RIG_NOSE_CHAIR_THETAS if min(0, th) <= x <= max(0, th))]
    chair["envelope_deg"] = [min(env), max(env)] if env else None
    chair["gated_deg"] = RIG_NOSE["chair_tmax"]
    return {"grid_deg": list(RIG_NOSE_GRID), "clear_min_m": round(float(worst[0]), 4), "where": worst[1],
            "rule": f">= {RIG_NOSE['clear']} m, no overlap; exempt: the outer yoke straps vs the main pin",
            "rows": rows, "chair_on_posed_line": chair, "pitch_sign": "+ raises the line-side (outer) end",
            "ok": True}


def rig_contracts_m(t, table, kind):
    """T256: the rig contracts on the joined AUTHORING-frame LOD0 (before orient_for_net). Returns the manifest block."""
    spec = M_SPECS[kind]
    co, tris, names, _tb = m_arrays(t)
    fnames, fb, vb, bones, pb = _rig_bone_masks_m(t, table, kind)
    heads = rig_heads_m(kind)
    out = {"bones": bones, "heads_net": {b: [round(v, 4) for v in h] for b, h in heads.items()},
           "heads_game": {b: [round(-h[0], 4), round(h[2], 4), round(-h[1], 4)] for b, h in heads.items()},
           "parts": {b: sorted(p for p, bb in pb.items() if bb == b) for b in bones[1:]},
           "verts_per_bone_lod0": {b: int((vb == i).sum()) for i, b in enumerate(bones)}}
    # centroid sanity: NoseTrain parts on the line side of the pin, the other train parts on the station side
    if kind == "drive":
        px = spec.anchor_out[0]
        wrong = []
        for n in sorted({n for n in names if n.startswith(M_HOLD["prefix"])}):
            c, _i = _part_centre_m(co, tris, names, n)
            if n.startswith((M_HOLD["prefix"] + "hanger_", M_HOLD["prefix"] + "pivot_main_")):
                continue
            if (pb.get(n) == "NoseTrain") != (c[0] < px):
                wrong.append((n, round(float(c[0]), 3), pb.get(n, "Root")))
        if wrong:
            _m_fail("RIG", f"nose train parts on the wrong side of the pin x {px}: {wrong[:6]}")
    out["rope"] = rig_rope_path_m(co, tris, names, spec)
    out["entry"] = rig_entry_m(co, tris, names, spec, vb, kind)
    if kind == "drive":
        out["nose_sweep"] = rig_nose_sweep_m(co, tris, names, spec, vb, heads)
    out["bullwheel_turn"] = {"axis": "the bone's local Y = game up (the wheel axle)", "code_rate_rev_s": 0.22,
                             "clearance": "revolved envelope (every angle), contract rig_entry_m"}
    for k in ("rope", "entry", "nose_sweep"):
        if k in out:
            print(f"[contract M] RIG {k} ok: {json.dumps({kk: out[k][kk] for kk in list(out[k])[:6]}, default=str)[:300]}")
    return out


class RigLodFixed:
    """make_lod_chain `fixed` hook under M_RIG: the wheels (cs2_sheaves.LodWheels, when v2) plus the station rope
    (RIG_ROPE_PARTS), which never enters the decimation (LOD1 keeps its 6-sided width; LOD2 deletes it by prefix). apply
    triangulates the surviving rope faces (the levels are tri-only)."""

    def __init__(self, inner):
        self.inner = inner
        self.rope = set()
        self.report = {}

    def parts(self, table):
        ids = set(self.inner.parts(table)) if self.inner is not None else set()
        self.rope = {i for i, n in enumerate(table, start=1) if n.startswith(RIG_ROPE_PARTS)}
        return ids | self.rope

    def apply(self, obj, table, level):
        n, tris = self.inner.apply(obj, table, level) if self.inner is not None else (0, 0)
        bm = bmesh.new()
        bm.from_mesh(obj.data)
        lay = bm.faces.layers.int.get("part")
        rf = [f for f in bm.faces if f[lay] in self.rope]
        made = bmesh.ops.triangulate(bm, faces=rf)["faces"] if rf else []
        bm.to_mesh(obj.data)
        bm.free()
        obj.data.update()
        self.report[level] = {"rope_tris": len(made)}
        return n + (1 if made else 0), tris + len(made)


def rig_rope_tris_m(t, table):
    """LOD0 tris of the rope parts (the LOD1 rest target gives them back: the level ratio stays O's)."""
    me = t.data
    part = np.zeros(len(me.polygons), dtype=np.int32)
    me.attributes["part"].data.foreach_get("value", part)
    rope = {i for i, n in enumerate(table, start=1) if n.startswith(RIG_ROPE_PARTS)}
    return int(sum(len(p.vertices) - 2 for p, q in zip(me.polygons, part) if q in rope))


def rig_skin_m(t, lods, table, kind, name):
    """The rig in the net frame + the weights of every level (each vertex 1.0 on its part's bone). Returns (arm, weights)."""
    import cs2_skin
    if any(o.matrix_world != mathutils.Matrix.Identity(4) for o in [t] + list(lods)):
        _m_fail("RIG", "a level carries an object transform; the rig assumes none")
    try:
        arm = cs2_skin.build_armature(name + "Rig", rig_heads_m(kind))
    except cs2_skin.SkinError as e:
        raise MContractError(f"RIG armature: {e}") from None
    pb = rig_part_bones_m(table, kind)
    weights = {}
    for i, o in enumerate([t] + list(lods)):
        try:
            weights[str(i)] = cs2_skin.assign(o, table, pb, list(RIG_BONES[kind]))
        except cs2_skin.SkinError as e:
            raise MContractError(f"RIG weights LOD{i}: {e}") from None
        cs2_asset.check_skin_weights(o, arm)
    print(f"[terminal M] RIG skin: heads {rig_heads_m(kind)}, weights per level {weights}")
    return arm, weights


def rig_roundtrip_m(asset_dir, name, levels, kind):
    """T256 contract (a): cs2_skin.roundtrip of every exported level (bones exactly, heads at the wheel center / lane end,
    identity rests, one bone per vertex)."""
    import cs2_skin

    def check(ok, label, detail=""):
        if not ok:
            _m_fail("RIG-roundtrip", f"{label} {detail}")
        print(f"[contract M] {label}")
    return cs2_skin.roundtrip(asset_dir, name, levels, rig_heads_m(kind), check, f"FG M {kind}")


def rig_posed_copy_m(t, arm, angles, kind, name):
    """A posed copy of the skinned net-frame `t` (an Armature modifier, evaluated): Bullwheel `angles["Bullwheel"]` deg about
    its local Y, NoseTrain `angles["NoseTrain"]` deg about its local X. Raises unless it equals rig_pose_np_m of the weights
    to 0.1 mm (the pose the contracts measured)."""
    import cs2_skin
    for pbn in arm.pose.bones:
        pbn.rotation_mode = "XYZ"
        a = math.radians(float(angles.get(pbn.name, 0.0)))
        pbn.rotation_euler = (a, 0.0, 0.0) if pbn.name == "NoseTrain" else (0.0, a, 0.0) if pbn.name == "Bullwheel" \
            else (0.0, 0.0, 0.0)
    bpy.context.view_layer.update()
    tmp = t.copy()
    bpy.context.collection.objects.link(tmp)
    mod = tmp.modifiers.new("Armature", type="ARMATURE")
    mod.object = arm
    me = bpy.data.meshes.new_from_object(tmp.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    bpy.data.objects.remove(tmp, do_unlink=True)
    for pbn in arm.pose.bones:
        pbn.rotation_euler = (0.0, 0.0, 0.0)
    bpy.context.view_layer.update()
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    table = json.loads(t.data[cs2_asset.PART_TABLE_PROP])
    _n, _fb, vb, bones, _pb = _rig_bone_masks_m(t, table, kind)
    co, got = cs2_skin.coords(t), cs2_skin.coords(ob)
    want = rig_pose_np_m(co, vb, bones, angles, rig_heads_m(kind))
    err = float(np.abs(got - want).max()) if len(got) == len(co) else 9.0
    if err > 1e-4:
        bpy.data.objects.remove(ob, do_unlink=True)
        raise MContractError(f"RIG posed copy {angles}: the armature is not the contracts' pose (max {err:.5f} m)")
    return ob


def rig_line_rope_m(kind, theta, length=6.0, name="riglinerope"):
    """A render-only tube (net frame): the line rope from each lane end outward at NoseTrain pitch `theta` (the in-station
    chord's continuation turned theta about the pin), r M_ROPE_R."""
    spec = M_SPECS[kind]
    px, pz = spec.anchor_out
    wx, _wy, wz = spec.wheel
    u = np.array([wx - px, wz - pz])
    u /= np.linalg.norm(u)
    c, s_ = math.cos(math.radians(theta)), math.sin(math.radians(theta))
    lx, lz = c * -u[0] + s_ * -u[1], -s_ * -u[0] + c * -u[1]
    objs = []
    for side in (-1.0, 1.0):
        a = (px, side * FG_LANE, pz)
        b = (px + length * lx, side * FG_LANE, pz + length * lz)
        o = _hex_tube_m(f"{name}_{'n' if side < 0 else 'p'}", rig_net_m(a), rig_net_m(b), M_ROPE_R)
        objs.append(o)
    return objs


def rig_shots_m(kind):
    """(name, target, eye) authoring: the wheel + entry sheaves close-ups, the nose train (base), play distance (70 m at 35 deg
    down) from the line side and the load side."""
    spec = M_SPECS[kind]
    wx, _wy, wz = spec.wheel
    cx, cz = entry_center_m(spec)
    d = 70.0
    e = math.radians(35.0)
    shots = [("rig_wheel_entry", (wx + 0.3, 1.2, wz + 0.1), (wx + 4.8, 5.6, wz + 3.4)),
             ("rig_entry_close", (cx, FG_LANE, cz), (cx + 2.2, FG_LANE + 2.6, cz + 0.9)),
             ("rig_wheel_above", (wx, 0.0, wz), (wx + 1.5, 0.8, wz + 7.5)),
             ("rig_play_line", (wx - 3.0, 0.0, 3.0), (wx - 3.0 - d * math.cos(e) * 0.8, -d * math.cos(e) * 0.6,
                                                       3.0 + d * math.sin(e))),
             ("rig_play_side", (wx - 2.0, 0.0, 3.0), (wx - 2.0 + d * math.cos(e) * 0.3, d * math.cos(e) * 0.95,
                                                       3.0 + d * math.sin(e)))]
    if kind == "drive":
        px, pz = spec.anchor_out
        shots += [("rig_nose_side", (px + 0.2, FG_LANE, pz + 0.5), (px - 0.8, FG_LANE + 4.6, pz + 1.2)),
                  ("rig_nose_front", (px + 0.4, 0.0, pz + 0.4), (px - 5.5, -2.5, pz + 1.8)),
                  ("rig_nose_close", (px, FG_LANE, pz + 0.4), (px - 0.6, FG_LANE + 2.2, pz + 0.9))]
    return shots


def rig_manifest_m(kind, block):
    """The manifest's `rig` block: what the code (BullwheelSpinSystem, TrainPitchSystem) and the import round need."""
    spec = M_SPECS[kind]
    out = {"bones": block["bones"], "heads_game": block["heads_game"],
           "bone_rules": {"Root": "static (parentId -1: rotation (0,0,0,1), scale (1,1,1) after every import, gate BONEROOT)",
                          "Bullwheel": "turn about its local Y (game up, the wheel axle); RIG_BULL_PARTS",
                          **({"NoseTrain": "pitch about its local X through the lane end; + raises the line-side end; "
                                           f"the art clears +-{RIG_NOSE['tmax']:.0f} deg (joint sweep)"}
                             if kind == "drive" else {})},
           "rest": "identity (every bone up, roll 0)", "weights": "every vertex 1.0 on one bone on every LOD",
           "settings": "procedural_animation true", "rope_r_m": M_ROPE_R, "rope_sides": RIG_ROPE_SIDES,
           "rope_from": {"lane_end_authoring_xz": list(spec.anchor_out),
                         "lane_end_game": [round(s * FG_LANE, 3) for s in (-1, 1)] + [spec.anchor_out[1],
                                                                                    spec.anchor_out[0]]},
           "entry_sheave_center_xz": list(entry_center_m(spec)),
           "submesh_win": "the _Win glass sub-mesh stays an unskinned sub-object of a skinned building (UNVERIFIED in "
                          "game, PLAN P0 c)"}
    if kind == "return":
        hf = RIG_HOSTED_DIR and os.path.abspath(RIG_HOSTED_DIR)      # repo-relative in the manifest (no home paths)
        if hf and os.path.normcase(hf).startswith(os.path.normcase(REPO_M + os.sep)):
            hf = os.path.relpath(hf, REPO_M).replace(os.sep, "/")
        out["hosted_tower"] = {"folder": hf, "row_position_game": [0.0, 0.0, RIG_HOSTED_X],
                               "shipped_row_z": spec.tower_x, "rotation_quat": [0, 1, 0, 0],
                               "note": "the skinned ET62's pivot (crown at 5.24 = the lane end) on the lane end"}
    return out


def render_rig_views_m(t, arm, asset_dir, name, out_dir, kind, win=None):
    """T256 posed previews (EEVEE, the baked maps, the real chair): the rig shots at rest, the bullwheel at 0 / 15 / 30 deg
    (wheel + entry sheaves from above and the side), and on the base the NoseTrain at -20 / 0 / +20 / +40 / -40 deg with the
    line rope drawn from the lane end at the matching pitch (side + close-up). Each posed frame is a posed copy checked
    against the contracts' pose (rig_posed_copy_m)."""
    written = []
    written += render_closeups_m(t, asset_dir, name, out_dir, kind, win=win, shots=rig_shots_m(kind))
    shots = {s[0]: s for s in rig_shots_m(kind)}
    for a in (0.0, 15.0, 30.0):
        ob = rig_posed_copy_m(t, arm, {"Bullwheel": a}, kind, f"{name}PoseW{int(a)}")
        try:
            for key in ("rig_wheel_above", "rig_wheel_entry"):
                nm, tg, ey = shots[key]
                written += render_closeups_m(ob, asset_dir, name, out_dir, kind, win=None, chairs=False,
                                             shots=[(f"{nm}_bullwheel{int(a):02d}deg", tg, ey)])
        finally:
            me = ob.data
            bpy.data.objects.remove(ob, do_unlink=True)
            bpy.data.meshes.remove(me)
    if kind == "drive":
        for th in (-20.0, 0.0, 20.0, 40.0, -40.0):
            ob = rig_posed_copy_m(t, arm, {"NoseTrain": th}, kind, f"{name}PoseN{int(th)}")
            ropes = rig_line_rope_m(kind, th)
            try:
                for key in ("rig_nose_side", "rig_nose_close"):
                    nm, tg, ey = shots[key]
                    tag = f"{'p' if th >= 0 else 'm'}{abs(int(th)):02d}"
                    written += render_closeups_m(ob, asset_dir, name, out_dir, kind, win=None, chairs=False,
                                                 shots=[(f"{nm}_nose{tag}deg", tg, ey)],
                                                 extras=[(r_, (0.02, 0.02, 0.02, 1.0)) for r_ in ropes])
            finally:
                for o in [ob] + ropes:
                    me = o.data
                    bpy.data.objects.remove(o, do_unlink=True)
                    bpy.data.meshes.remove(me)
    return written


# ---------------------------------------------------------------- main (generation M) ------

def main_m(args):
    """Generation M shipping path (design/fg-stations-m.md section 6):
    1. Build, assemble.
    2. M1-M5, M7-M10, M12 on the authoring mesh.
    3. Orient.
    4. `_Win` split (M10 cells).
    5. Engine base (M6).
    6. Budget (M15).
    7. Bake (v2 kit: material_sources -> bake_unique, the sign / labels / tension scale stamped in the hook (M11), weathering
       stepped to the highest budget-clean intensity (M13)).
    8. Export LOD0 / `_Win` / LOD1-2 (M6 on every LOD).
    9. Settings.
    10. Previews (+ the chair / tower close-ups).
    11. Validation.
    12. Manifest.
    13. Review pack."""
    global FINE_DETAIL_M, M_NOSE_TRAIN, M_ROPE_R, RIG_HOSTED_DIR
    import cs2_bake
    import cs2_liftdress as LD
    LTD = _LTD()
    if not bpy.app.background:
        raise RuntimeError("lift_terminal generation M runs headless only (ROUND-SPEC R10)")
    import cs2_sheaves as S
    S.set_wheels(getattr(args, "wheels", "v2"))   # T62: before any part is built
    if getattr(args, "rope_on_crown", False):
        set_m_rope_on_crown(True)          # T27 preview flag: the anchors on the ET crown (surgery in M1)
    FINE_DETAIL_M = not bool(getattr(args, "no_fine", False))     # fine-detail step 3c: every run follows its args
    set_texpass_m(getattr(args, "texpass", "off"), getattr(args, "identity", "off"))   # every run follows its args
    kind = args.kind
    M_NOSE_TRAIN = bool(getattr(args, "no_hosted_tower", False))
    M_ROPE_R = THIN_ROPE_R if getattr(args, "thin_rope", False) else M_ROPE_R_SHIPPED   # every run follows its args
    if M_NOSE_TRAIN and kind != "drive":
        raise SystemExit("--no-hosted-tower is drive only (the return has no hold-down train: ET62 keeps the rope)")
    rig = bool(getattr(args, "rig", False))
    if rig:             # T256: the rigged station (letter P): every M_RIG table, after the flags above
        if getattr(args, "wheels", "v2") != "v2":
            raise SystemExit("--rig builds on the v2 wheels")
        set_m_rig(True, kind)
        RIG_HOSTED_DIR = getattr(args, "rig_hosted", None) if kind == "return" else None
    elif getattr(args, "rig_hosted", None):
        raise SystemExit("--rig-hosted needs --rig")
    spec = M_SPECS[kind]
    default = "SkiLiftBaseStation" if kind == "drive" else "SkiLiftTopStation"
    name = cs2_asset.validate_asset_name(args.name or default)
    asset_dir = os.path.join(os.path.abspath(args.out), name)
    os.makedirs(asset_dir, exist_ok=True)
    print(f"[terminal M] {name} ({kind}): wheel {spec.wheel}, hang {FG_HANG}, lane +-{FG_LANE}, "
          f"hosted {spec.tower} at x {spec.tower_x} -> {asset_dir}")
    clear_scene()
    parts = build_station_m(kind)
    part_material = {o.name: str(m) for o, m in parts}
    dress = LD.lod_split([(o, m) for o, m in parts if o.name.startswith(
        ("maze_", "gate_", "board_", "label_", "stripe_", "chev_", "carpet_", "ext_", "tool_", "skirack_",
         "ski_", "estop_", "gauge_"))])
    t, table, sources = assemble_m(parts, args.finish, kind)
    t.name = name
    t.data.name = name
    contracts = contracts_m(t, table, kind, sweep=not getattr(args, "skip_sweep", False))
    co, tris, names, _tb = m_arrays(t)
    contracts["M10"] = windows_m(co, tris, names, kind)
    if rig:
        contracts["RIG"] = rig_contracts_m(t, table, kind)
        rope_tris = rig_rope_tris_m(t, table)
    bb_lo, bb_hi = co.min(axis=0), co.max(axis=0)
    cs2_asset.orient_for_net(t)

    win = split_windows(t, name, table, cells=M_WIN_CELLS[kind])
    if win is None:
        _m_fail("M10", "no _Win sub-mesh")
    cells = cs2_validate._win_cells(win.data)
    want_cells = sorted(row * 5 + col for col, row in M_WIN_CELLS[kind].values())
    fparts = np.zeros(len(t.data.polygons), dtype=np.int32)
    t.data.attributes["part"].data.foreach_get("value", fparts)
    still = sum(1 for q in fparts if table[q - 1].startswith(tuple(M_WIN_CELLS[kind])))
    if sorted(cells) != want_cells or still:
        _m_fail("M10", f"_Win cells {cells} (want {want_cells}), {still} pane faces left in main")
    contracts["M10"]["cells"] = {str(k): v for k, v in sorted(cells.items())}
    zmin = float(np.array([v.co.z for v in t.data.vertices]).min())
    if zmin < -1e-5:
        _m_fail("M6", f"min z {zmin:.5f} below grade")
    # pre-bake: the ground ring only (two UV layers still exist). The full check_engine_base (validate_mesh + ring) runs
    # after the bake on the shipped layout
    base_report = engine_ring_m(t, kind)
    n0 = cs2_asset.tri_count(t)
    cap = (RIG_LOD0_CAP if M_RIG else LOD0_CAP_M)[kind]
    if n0 > cap:
        _m_fail("M15", f"LOD0 {n0} tris > cap {cap}")
    print(f"[contract M] M6 pre-bake ok: {base_report['verdict']}, {base_report['ok_edges']} OK edges; "
          f"M10 cells {sorted(cells.items())}; M15 LOD0 {n0} <= {cap}")

    # -- bake ----------------------------------------------------------------------------------
    A = args.atlas if args.finish else M_SMOKE_ATLAS
    ao = args.ao_samples if args.finish else 0
    rects = list(reserved_rects_m(kind).values())
    hook0 = make_hook_m(t, table, kind, part_material=part_material)
    hook = hook0
    if args.weather == "on":
        roles = LD.lift_mat_roles(t.data, mat_roles_m())
        steps = (TP1_WEATHER_STEPS + tuple(LD.INTENSITY_STEPS)) if TEXPASS_M else LD.INTENSITY_STEPS
        hook = LTD.SteppedWeather(hook0, lambda s: weather_spec_m(name, kind, s), steps,
                                  mat_roles=roles, detail_specs=detail_specs_m(), reserved=rects,
                                  compose_contact=False)
    margin = max(2, int(round(M_PACK_MARGIN_PX * A / float(M_ATLAS))))
    report = cs2_bake.bake_unique(t, asset_dir, name, A, sources, reserved=rects, margin_px=margin,
                                  ao_samples=ao, post_compose=hook, emissive=False, src_uv=M_SEED)
    # M6 on the baked mesh: lift_terminal_detach.engine_base_gate (validate_mesh + the ring, with the float64 uv-zero
    # recount: the float32 shoelace of validate_mesh reads sub-texel faces as zero)
    import types as _types
    base_report = LTD.engine_base_gate(t, _types.SimpleNamespace(engine_base=M_ENGINE_BASE[kind]))
    if not rig:         # T256: the skinned LOD0 is written with the chain (rig_skin_m)
        cs2_asset.export_fbx(t, os.path.join(asset_dir, f"{name}.fbx"))
    cs2_submesh.export_submeshes(win, name, "Win", asset_dir, lods=(1, 2) if args.lods else ())

    lods = []
    lod_base = {}
    if args.lods:
        import cs2_sheaves as S
        if rig:         # T256: the rope rides the fixed hook at LOD1 (its 6 sides kept); the rest target gives its tris back
            fx = RigLodFixed(S.lod_fixed())
            t1 = int(round(LOD1_RATIO_M * n0)) - rope_tris
        else:
            fx, t1 = S.lod_fixed(), int(round(LOD1_RATIO_M * n0))
        lods = cs2_asset.make_lod_chain(t, name, targets=(t1, LOD2_TARGET_M[kind]),
                                        delete_prefixes=(lod1_drop_m(table), lod2_drop_m(table)),
                                        shade_angle=M_SHADE, fixed=fx)   # T62 round 2: WHEEL_LOD
        for i, lod in enumerate(lods, start=1):
            if not rig:
                cs2_asset.export_fbx(lod, os.path.join(asset_dir, f"{name}_LOD{i}.fbx"))
            rl = cs2_validate.ground_ring_report(lod)
            miss = [p for p in M_ENGINE_BASE[kind] if p not in rl["ok_parts"]]
            if rl["verdict"] != "OK" or miss or rl["blocked_edges"] or rl["below_verts"]:
                _m_fail("M6", f"engine base on LOD{i}: {rl['verdict']}, missing {miss}, "
                              f"{rl['blocked_edges']} blocked, offending {rl['offending_parts']}")
            lod_base[i] = {k: rl[k] for k in ("verdict", "ok_edges", "blocked_edges")}
    arm = None
    if rig:             # T256: one rig for every level, each vertex 1.0 on its part's bone; skinned FBXs; round trip
        arm, rig_w = rig_skin_m(t, lods, table, kind, name)
        cs2_asset.export_fbx_skinned(t, arm, os.path.join(asset_dir, f"{name}.fbx"))
        for i, lod in enumerate(lods, start=1):
            cs2_asset.export_fbx_skinned(lod, arm, os.path.join(asset_dir, f"{name}_LOD{i}.fbx"))
        cs2_asset.write_settings(asset_dir, name, supplied_lods=tuple(range(1, len(lods) + 1)),
                                 procedural_animation=True)
        contracts["RIG"]["weights"] = rig_w
        contracts["RIG"]["roundtrip"] = rig_roundtrip_m(asset_dir, name, len(lods), kind)
        if lods:
            contracts["RIG"]["lod_fixed"] = fx.report
    else:
        cs2_asset.write_settings(asset_dir, name, supplied_lods=tuple(range(1, len(lods) + 1)))
    tris = {0: cs2_asset.tri_count(t)}
    tris.update({i: cs2_asset.tri_count(l) for i, l in enumerate(lods, start=1)})
    tris_win = cs2_asset.tri_count(win)
    if lods and (tris[1] > LOD1_RATIO_M * n0 * 1.10 or tris[2] > LOD2_TARGET_M[kind] * 1.10):
        _m_fail("M15", f"LOD chain {tris} over LOD1 {LOD1_RATIO_M} x LOD0 / LOD2 {LOD2_TARGET_M[kind]} (+10 %)")
    print(f"[terminal M] tris LOD0 {tris[0]}, chain {[tris[i] for i in sorted(tris) if i]}, _Win {tris_win}")
    density = LTD.density_classes(t, table, A, reserved=rects)
    weather = None
    if isinstance(hook, LTD.SteppedWeather):
        import cs2_weather as CW
        rep = json.loads(CW.report_json(hook.report))
        weather = {"intensity": hook.chosen, "ladder": hook.ladder, "preset": "station",
                   "slot_wear": LD.STATION_SLOT_WEAR, "snow": 0.0,
                   "vanilla": {k: rep["vanilla"].get(k) for k in ("VL1_mean_rel_dL", "VL2_share_rel_dL_gt_0.15",
                                                                  "VL4_max", "visible_texels", "ok", "failed")}}
        contracts["M13"] = {"intensity": hook.chosen, "ok": True,
                            "ladder": [(r["intensity"], r["ok"]) for r in hook.ladder]}
    contracts["M11"] = {"sign": {k: hook0.stamps[k] for k in ("px_per_m", "digit_px_h", "digit_coverage_ratio",
                                                              "digit_aa_levels", "owner", "shared_with", "badge")},
                        "labels": {k: v.get("px_per_m") for k, v in (hook0.labels or {}).items()},
                        "scale": hook0.scale}
    contracts["M14"] = {"w1_px_per_m": density["w1_px_per_m"], "median_px_per_m": density["median_px_per_m"],
                        "starving_lt14": density["starving_lt14"]}
    contracts["M15"] = {"lod0": tris[0], "cap": cap, "lod1": tris.get(1), "lod2": tris.get(2),
                        "lod2_target": LOD2_TARGET_M[kind], "batches": 9 if lods else 3}

    # -- previews ------------------------------------------------------------------------------
    written = []
    if args.preview:
        prev = os.path.abspath(args.preview)
        ab = args.ab
        if ab is None:
            cand = os.path.join(IMPORT_M, "stations", "fixedgrip", "K", spec.k_mesh)
            ab = cand if os.path.isdir(cand) else None
        modes = ["pbr", "dusk", "normals", "density"] + (["lod"] if lods else []) + (["ab"] if ab else [])
        views = list(cs2_preview.DEFAULT_VIEWS) + [("back", (-1.0, -0.8, 0.35))]
        for f in cs2_preview.preview(t, asset_dir, name, prev, modes=tuple(modes), views=views,
                                     lods=lods or None, prev_generation=ab, tint=M_TINT):
            written.append(f)
        written += render_closeups_m(t, asset_dir, name, os.path.join(prev, "closeups"), kind, win=win)
        if rig:
            written += render_rig_views_m(t, arm, asset_dir, name, os.path.join(prev, "rig"), kind, win=win)
        t.data.materials.clear()

    switch = None
    if lods:
        _c, size = lift_tower.world_bounds(t)
        switch = cs2_preview.lod_switch_table(size, [tris[i] for i in sorted(tris)])
    validation = None
    if not args.no_validate:
        t.name, t.data.name = name + "Src", name + "Src"
        win.name, win.data.name = name + "WinSrc", name + "WinSrc"
        for i, lod in enumerate(lods, start=1):
            lod.name, lod.data.name = f"{name}LOD{i}Src", f"{name}LOD{i}Src"
        findings = cs2_validate.validate_asset_folder(asset_dir, name, {
            "kind": "building", "lod0_cap": cap, "lod2_cap": LOD2_TARGET_M[kind], "hero": True,
            "expected_lods": tuple(range(1, len(lods) + 1)), "expected_submeshes": ("Win",)})
        n_err = sum(1 for f in findings if f[0] == "ERROR")
        n_warn = sum(1 for f in findings if f[0] == "WARN")
        validation = {"errors": n_err, "warnings": n_warn,
                      "warn_checks": sorted({f[1] for f in findings if f[0] == "WARN"})}
        print(f"[terminal M] validate_asset_folder: {n_err} errors, {n_warn} warnings")
        if n_err and A >= M_ATLAS:
            raise RuntimeError(f"validate_asset_folder: {n_err} errors")
    pc = contracts.pop("M1")
    fine_block = fine_detail_block_m(kind, contracts)          # fine-detail step 3c (None when off)
    from carrier_split import manifest_args
    cs2_validate.write_manifest(asset_dir, name, {
        # T256: every parsed flag (defaults too): a letter's build is reproducible. r4/tex fg (2026-11-20): paths
        # repo-relative / %USERPROFILE% (manifest_args, as lift_chair), the first export into assets/import leaked them
        "args": manifest_args(args, REPO_M),
        "generator": GEN_M, "kind": kind, "generation": "M", "rope_r_m": M_ROPE_R,
        "spec": "design/fg-stations-m.md + fg-stations-m-skeptic.md MUST-CHANGE 1-6 (build/fg-stations-m.md)",
        "finish": "shipping" if args.finish else "blockout", "atlas_px": A, "ao_samples": ao,
        "pack_margin_px": margin, "tris": tris, "tris_win": tris_win, "parts": len(table),
        "caps": {"lod0": cap, "lod1_ratio": LOD1_RATIO_M, "lod2": LOD2_TARGET_M[kind]},
        "materials": sorted(materials_for_m(parts)),
        "reserved_uv": {k: [round(v, 8) for v in r] for k, r in sorted(reserved_rects_m(kind).items())},
        "win_cells": want_cells, "submeshes": ["Win"],
        "engine_base": {k: base_report[k] for k in ("verdict", "ok_edges", "blocked_edges")},
        "engine_base_lods": lod_base, "lod_switch_m": switch, "validation": validation,
        "density": density, "weather": weather, "emissive": "none (MC1 / X14: every light is a vanilla sub-object)",
        "sign_band": hook0.stamps, "labels": hook0.labels, "tension_scale": hook0.scale,
        "detail_stats": hook0.detail_stats, "ctl_seam_texels_scaled": hook0.ctl_scaled,
        "dressing_lod": {k: list(v) for k, v in dress.items()},
        **({"texpass": texpass_block_m(hook0, part_material)} if TEXPASS_M else {}),
        "sub_objects": subobject_rows_m(kind),
        **({"fine_detail": fine_block} if fine_block else {}),
        "prefab_contract": pc,
        "bounds": bounds_block_m(kind, bb_lo, bb_hi),
        "bounds_authoring": {"min": [round(float(v), 4) for v in bb_lo], "max": [round(float(v), 4) for v in bb_hi]},
        "up_lane_y": UP_LANE_Y[kind], "frame": "game = (-y, z, x) of authoring (S1, unverified in game)",
        # nitpick A (2026-10-13, build/nitpick-a-stations.md): the layout around the up lane (M20) and the sheave trains of
        # the towers (M21), also under "contracts"
        "orientation": contracts.get("M20"), "sheave_trains": contracts.get("M21"),
        **({"rig": rig_manifest_m(kind, contracts["RIG"])} if rig else {}),
        "contracts": contracts,
    })
    # kit color variants (2026-10-12): a shipping export stages the variants that its kit rows name
    import dress_kit
    dress_kit.stage_from_manifest(asset_dir, name)

    try:
        if args.review is not None:
            import cs2_review
            ch = load_chair_m(0)
            y_up = UP_LANE_Y[kind] * FG_LANE
            chair_rows = [{"role": role, "pos_game": [round(v, 4) for v in m_to_game(
                (spec.wheel[0], s * FG_LANE, spec.wheel[2] - FG_HANG))], "yaw_travel": travel,
                "note": "SkiLiftChair6M pivot (lowest point) at the tangent; meta only, not exported"}
                for role, s, travel in (("load" if kind == "drive" else "unload", UP_LANE_Y[kind],
                                         "-x" if kind == "drive" else "+x"),)]
            cs2_review.write_pack(name, asset_dir, dict(enumerate([t] + lods)), {"Win": win},
                                  meta={"generator": GEN_M, "kind": "building",
                                        "caps": {0: cap, 1: int(LOD1_RATIO_M * n0), 2: LOD2_TARGET_M[kind]},
                                        "letter": "M", "tint": M_TINT, "chairs_meta_only": chair_rows,
                                        "cushion_over_surface_m": contracts["M4"]["cushion_over"],
                                        "lane_y_up": y_up, "chair_lowest_m": ch["lowest"]},
                                  out_dir=args.review or None,
                                  **({"manifest": review_rows_m(subobject_rows_m(kind))} if FINE_DETAIL_M else {}))
    finally:
        for lod in lods:
            me = lod.data
            bpy.data.objects.remove(lod, do_unlink=True)
            bpy.data.meshes.remove(me)
    print(f"[terminal M] asset folder: {asset_dir}")
    print(f"PASS lift_terminal M: {name} ({kind}) LOD0 {tris[0]} tris, chain "
          f"{[tris[i] for i in sorted(tris) if i]}, _Win {tris_win}, engine base {base_report['verdict']}, "
          f"weathering {weather['intensity'] if weather else 'off'}, w1 {density['w1_px_per_m']} px/m"
          + (f", fine-detail on ({fine_block['rows']})" if fine_block else ", fine-detail off"))
    return {"name": name, "asset_dir": asset_dir, "tris": tris, "contracts": contracts, "fine_detail": fine_block}

#@@M-SECTION-END@@


# T256 (orchestrator finding, 2026-11-19): the shipped letters' build flags. The O stations were built with --thin-rope (both)
# and --no-hosted-tower (base), and no manifest recorded it (HEAD without them builds base O at 7,950 tris, shipped 8,166).
# An entry lists the flags that differ from the parser defaults, checked against the shipped manifests in
# assets/import/stations/fixedgrip/O/<name>/<name>.manifest.json: rope_r_m 0.024 (= THIN_ROPE_R, --thin-rope), the base's
# nose_train block (--no-hosted-tower), finish "shipping" (--finish), tris "1" / "2" (--lods), kind. Every build's manifest now
# records "args" (vars(args)). A build under a listed --name stops when a SHAPE_FLAGS value differs, unless
# --allow-flag-drift (experiments).
SHIPPED_FLAGS = {
    "SkiLiftBaseStationO": dict(kind="drive", thin_rope=True, no_hosted_tower=True, finish=True, lods=True),
    "SkiLiftTopStationO": dict(kind="return", thin_rope=True, finish=True, lods=True),
}
# the flags that change what a build exports (meshes, maps, settings). Output / preview / review / debugging flags (--out,
# --preview, --review, --ab, --no-validate, --skip-sweep, --name) never count
SHAPE_FLAGS = ("gen", "weather", "atlas", "ao_samples", "no_fine", "rope_on_crown", "thin_rope", "no_hosted_tower",
               "wheels", "rig", "rig_hosted", "kind", "finish", "lods")


def flag_drift(args, ap=None):
    """{flag: (shipped, given)} of the SHAPE_FLAGS that differ from SHIPPED_FLAGS[args.name] (parser defaults for the flags an
    entry leaves out). {} when the name is not a shipped letter."""
    entry = SHIPPED_FLAGS.get(getattr(args, "name", None))
    if entry is None:
        return {}
    ap = ap or build_parser()
    want = {k: ap.get_default(k) for k in SHAPE_FLAGS}
    want.update(entry)
    return {k: (want[k], getattr(args, k, None)) for k in SHAPE_FLAGS if getattr(args, k, None) != want[k]}


def check_shipped_flags(args, ap=None):
    """SystemExit naming the differing flags when --name is a shipped letter and a shape flag differs (flag_drift), unless
    --allow-flag-drift."""
    drift = flag_drift(args, ap)
    if drift and not getattr(args, "allow_flag_drift", False):
        raise SystemExit(f"{args.name} is a shipped letter (SHIPPED_FLAGS) and these flags differ from its shipped build "
                         f"(shipped, given): {drift}. Use the shipped flags, another --name, or --allow-flag-drift.")
    if drift:
        print(f"[terminal] --allow-flag-drift: {args.name} built with flags that differ from the shipped letter: {drift}")
    return drift


def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    return build_parser().parse_args(argv)


def build_parser():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument(
        "--gen", choices=["M", "L"], default="M",
        help="generation: M (default, the Pali stations on the v2 kit, design/fg-stations-m.md) "
             "or L (the round-L builders, kept for A/B and reproducibility).",
    )
    ap.add_argument("--weather", choices=["on", "off"], default="on",
                    help="M: the cs2_weather station layer (DECISIONS 171), default on.")
    ap.add_argument("--atlas", type=int, default=2048, help="M: shipping atlas (2048).")
    ap.add_argument("--ao-samples", type=int, default=48, dest="ao_samples",
                    help="M: AO samples of the bake (48).")
    ap.add_argument("--skip-sweep", action="store_true", dest="skip_sweep",
                    help="M, debugging only: skip the M3 chair sweep (the manifest then has no M3).")
    ap.add_argument("--no-fine", action="store_true", dest="no_fine",
                    help="M: fine-detail step 3c off (FINE_DETAIL_M) = the approved generation-M station byte for byte.")
    ap.add_argument("--rope-on-crown", action="store_true", dest="rope_on_crown",
                    help="M, T27 preview flag (M_ROPE_ON_CROWN): the HV anchors on the hosted end tower's sheave "
                         "crown; M1 reports the prefab surgery. Off = the shipped station byte for byte.")
    ap.add_argument("--thin-rope", action="store_true", dest="thin_rope",
                    help="art-601 #10: the station rope at THIN_ROPE_R (r 0.024, the chair grip's rope); the shipped letter O uses this flag.")
    ap.add_argument("--no-hosted-tower", action="store_true", dest="no_hosted_tower",
                    help="M drive, art-601 (M_NOSE_TRAIN): the base without its hosted end tower; the shipped letter O uses this flag.")
    ap.add_argument("--wheels", choices=["v1", "v2"], default="v2",
                    help="M, T62: sheave wheel generation (v2 default = cs2_sheaves.wheel on the hold-down train and the "
                         "counterweight sheaves: the round-3 wheel (16 segments, groove, seen hub bosses); v1 = the pre-T62 wheels byte for byte).")
    ap.add_argument("--texpass", choices=list(TEXPASS_CHOICES), default="off",
                    help="M, r4/tex: texture pass (v1 = TP1_* tables: tone layer, cavity term, slab joints, roof seams, "
                         "satin paint, density toward the pad / roofs). off = the shipped maps byte for byte.")
    ap.add_argument("--identity", choices=list(IDENTITY_CHOICES), default="off",
                    help="M, r4/tex proposal, needs --texpass v1: shack = plank-siding operator hut + the machine house "
                         "on recolor slot 2. off = the pass alone.")
    ap.add_argument("--rig", action="store_true",
                    help="M, T256 (M_RIG): the rigged fixed-grip station (letter P): skinned (Root, Bullwheel, base "
                         "NoseTrain), the 8-spoke wheel, entry sheaves, the cable-width rope from the lane end; drive implies "
                         "--no-hosted-tower. Off = every shipped station byte for byte.")
    ap.add_argument("--rig-hosted", default=None, dest="rig_hosted",
                    help="M return + --rig: the folder of the skinned hosted end tower (lift_tower --skinned "
                         "--hosted-lane 5.24); M5 and the previews use it at x RIG_HOSTED_X. Absent = M5 skipped.")
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
    ap.add_argument(
        "--lods", action="store_true",
        help="L: also export an authored LOD1/LOD2 chain decimated from the finished mesh "
             "(cs2_asset.make_lod_chain; LOD1 = 45%% of LOD0, LOD2 = 500 drive / 400 "
             "return). The importer generates no LODs on its own (DECISIONS 77/104).",
    )
    ap.add_argument(
        "--ab", default=None,
        help="previous generation asset folder for the ab preview (default: the K "
             "station under ../import/stations/fixedgrip/K when present).",
    )
    ap.add_argument(
        "--no-validate", action="store_true",
        help="skip the end-of-build cs2_validate.validate_asset_folder gate (debugging).",
    )
    # live review (ROUND-SPEC R10): optional DIR, default assets/preview/review
    ap.add_argument(
        "--review", nargs="?", const="", default=None, metavar="DIR",
        help="also write the live-review pack <DIR>/<name>.blend (cs2_review.write_pack, "
             "duplicates only; exports, manifest and validation unchanged).",
    )
    ap.add_argument("--allow-flag-drift", action="store_true", dest="allow_flag_drift",
                    help="build a shipped letter (SHIPPED_FLAGS) with other shape flags anyway (experiments only).")
    return ap


# Ground parts that must feed the engine base (cs2_validate.ground_ring_report ok_parts).
ENGINE_BASE_PARTS = {"drive": ("pad", "hutbase"),
                     "return": ("rampup", "crest", "rampdown", "liftiebay")}
# `_Win` room cells per pane group (int(v*5)*5 + int(u*5) of the first vertex: hut 7, cabin 13)
WIN_CELLS = {"hutwin": (2, 1), "cabwin": (3, 2)}


def split_windows(t, name, table, cells=None):
    """
    Move the hut glass bands (and, on the drive station, the cabin glass bands) off the main mesh into ONE `<name>_Win`
    sub-object: cs2_submesh.split_submesh per pane group (each with its own room cell), the second sub joined into the first.
    Returns the sub, or None when the mesh carries no pane parts. Must run after orient_for_net and BEFORE the finish pass,
    so the bake never sees pane texels and the LOD chain never sees panes.

    `cells` (generation M): {pane prefix: (col, row)}. None = WIN_CELLS at call time.
    """
    groups = [(pfx, cell) for pfx, cell in (WIN_CELLS if cells is None else cells).items()
              if any(n.startswith(pfx) for n in table)]
    if not groups:
        return None
    subs = []
    for pfx, cell in groups:
        sub = cs2_submesh.split_submesh(t, table, (pfx,), "Win", win_cell=cell)
        sub.name = f"{name}Win{len(subs)}"           # free the `_Win` name for the next split
        sub.data.name = sub.name
        subs.append(sub)
    main_sub = subs[0]
    if len(subs) > 1:
        bpy.ops.object.select_all(action="DESELECT")
        for s in subs:
            s.select_set(True)
        bpy.context.view_layer.objects.active = main_sub
        bpy.ops.object.join()
        main_sub = bpy.context.active_object
    main_sub.name = cs2_submesh.submesh_name(name, "Win")
    main_sub.data.name = main_sub.name
    if len(main_sub.data.uv_layers) != 1:
        raise RuntimeError(f"{main_sub.name}: {len(main_sub.data.uv_layers)} UV layers")
    print(f"[terminal] _Win: {len(groups)} pane groups {[g[0] for g in groups]} -> "
          f"{main_sub.name}: {len(main_sub.data.polygons)} panes / "
          f"{cs2_asset.tri_count(main_sub)} tris")
    return main_sub


def check_engine_base(t, kind, parts=None):
    """
    The skirt-removal acceptance gate: cs2_validate.validate_mesh(kind="building") must report the engine base OK, and every
    ground part of this station must contribute OK edges. Raises RuntimeError otherwise (nothing is exported). `parts`
    (generation M): the ground parts. None = ENGINE_BASE_PARTS[kind].
    """
    findings = cs2_validate.validate_mesh(t, kind="building")
    errors = [f for f in findings if f[0] == "ERROR"]
    base_ok = any(f[0] == "INFO" and f[1] == "engine-base" for f in findings)
    r = cs2_validate.ground_ring_report(t)
    missing = [p for p in (ENGINE_BASE_PARTS[kind] if parts is None else parts) if p not in r["ok_parts"]]
    print(f"[terminal] engine base: verdict {r['verdict']}, {r['ok_edges']} OK edges / "
          f"{r['blocked_edges']} blocked / {r['below_verts']} verts below -1 cm; "
          f"ok parts {list(r['ok_parts'].keys())}")
    if errors or not base_ok or missing:
        raise RuntimeError(f"engine-base gate failed: errors {errors}, base_ok {base_ok}, "
                           f"missing ground parts {missing}, offending {r['offending_parts']}")
    return r


def main():
    """CLI entry (tests/test_review_side_effects.py 1b drives it through sys.argv): generation M
    by default, L with --gen L."""
    args = parse_args()
    check_shipped_flags(args)          # T256: a shipped letter builds with its shipped flags (SHIPPED_FLAGS)
    if args.gen == "M":
        return main_m(args)
    return main_l(args)


def main_l(args):
    """The round-L shipping path (unchanged, --gen L)."""
    clear_scene()
    p = TerminalParams()
    here = os.path.dirname(os.path.abspath(__file__))

    default = "SkiLiftBaseStation" if args.kind == "drive" else "SkiLiftTopStation"
    t = build_terminal_pali(p, args.kind, finish=args.finish)
    t.name = args.name or default
    t.data.name = t.name
    name = cs2_asset.validate_asset_name(t.name)
    table = json.loads(t.data[cs2_asset.PART_TABLE_PROP])

    asset_dir = os.path.join(os.path.abspath(args.out), name)
    os.makedirs(asset_dir, exist_ok=True)

    d = t.dimensions
    print(f"[terminal] kind: {args.kind}   tris: {tri_count(t)}")
    print(f"[terminal] size (m): {d.x:.2f} x {d.y:.2f} x {d.z:.2f}   (v1 was 9.14 tall)")

    # -- `_Win` sub-mesh out of the main mesh, then the engine-base gate (L) ----------
    win = split_windows(t, name, table)
    base_report = check_engine_base(t, args.kind)
    n0 = tri_count(t)
    cap = LOD0_CAP[args.kind]
    if n0 > cap:
        raise RuntimeError(f"LOD0 {n0} tris exceeds the {cap} cap")
    print(f"[terminal] LOD0 after _Win split: {n0} tris (cap {cap})")

    # post hook stamps the white 1..16 tension scale into the atlas copy of THIS asset. The shared lift atlas recipe itself
    # stays untouched for towers and chairs.
    #
    # quad_size=1024 (L): 2048 atlas (was 4096 in J/K). The density report at 4096 showed ~55-65 px/m packed, which 2048
    # reaches with the unique repack. paint_pad_strip is composed in (J follow-up), so every lift atlas carries the same
    # crash-pad strip. Stations have no pads, but the palette stays identical across the system, and the reserved headroom of
    # quad 2 is painted, not undefined.
    def _post(base, nrm, msk, ctl, A):
        cs2_asset.paint_pad_strip(base, nrm, msk, ctl, A)
        stamp_tension_scale(base, nrm, msk, ctl, A)

    cs2_asset.make_texture_set_atlas(asset_dir, name, QUAD_SETS, quad_size=ATLAS_QUAD_PX,
                                     control_quads=CONTROL_QUADS, post=_post)

    reserved = [(cs2_asset.PAD_U0, cs2_asset.PAD_V0, cs2_asset.PAD_U1, cs2_asset.PAD_V1),
                (SCALE_U0, SCALE_V0, SCALE_U1, SCALE_V1)]
    finished = False
    compose = None
    if args.finish:
        # Texture half of the finishing pass (shared pipeline in lift_tower). BOTH stamped strips are density-protected
        # through the repack. L: the unique repack is cs2_uv.finish_packer (density weights), and the relief / compositing
        # layer runs through make_post_compose before the sRGB encode.
        compose = make_post_compose(t, reserved)
        finished = lift_tower.apply_finish_textures(
            t, asset_dir, name, atlas_size=FINISH_ATLAS, reserved_strips=reserved,
            ao_samples=AO_SAMPLES, packer=finish_packer, post_compose=compose)
        if not finished:
            raise RuntimeError("finish pass failed (see [tower] finish log above)")

    cs2_asset.export_fbx(t, os.path.join(asset_dir, f"{name}.fbx"))
    if win is not None:
        # _Win at LOD1 AND LOD2 (review F12): vanilla ships _LOD2_Win so rooms stay lit beyond the LOD2 switch (~300 m). The
        # pane mesh is 14 tris, so the cost is nil.
        cs2_submesh.export_submeshes(win, name, "Win", asset_dir, lods=(1, 2) if args.lods else ())

    # Authored LOD chain AFTER the finish pass (it repacked the UVs) and after the LOD0
    # export. The levels stay linked until the previews rendered the lod frames.
    lods = []
    if args.lods:
        lods = cs2_asset.make_lod_chain(t, name, targets=(int(round(0.45 * n0)),
                                                          LOD2_TARGET[args.kind]),
                                        delete_prefixes=(LOD1_DELETE, LOD2_DELETE),
                                        shade_angle=SHADE_ANGLE)
        for i, lod in enumerate(lods, start=1):
            cs2_asset.export_fbx(lod, os.path.join(asset_dir, f"{name}_LOD{i}.fbx"))
    cs2_asset.write_settings(asset_dir, name, supplied_lods=tuple(range(1, len(lods) + 1)))
    tris = {0: tri_count(t)}
    tris.update({i: tri_count(l) for i, l in enumerate(lods, start=1)})
    print(f"[terminal] LOD0 tris (shipped): {tris[0]}   LOD chain: {len(lods)} levels "
          f"{[tris[i] for i in range(1, len(lods) + 1)]}")

    # -- previews (cs2_preview rig: pbr, dusk, normals, lod, ab vs K) ------------------
    if args.preview:
        prev = os.path.abspath(args.preview)
        ab = args.ab
        if ab is None:
            k_name = "SkiLiftBaseStationK" if args.kind == "drive" else "SkiLiftTopStationK"
            cand = os.path.join(here, "..", "import", "stations", "fixedgrip", "K", k_name)
            ab = os.path.abspath(cand) if os.path.isdir(cand) else None
        modes = ["pbr", "dusk", "normals"]
        if lods:
            modes.append("lod")
        if ab:
            modes.append("ab")
        # the three legacy views plus "back" (-X post-orient). The hut door and the machinery hatch face that way and never
        # showed in the K frames.
        views = list(cs2_preview.DEFAULT_VIEWS) + [("back", (-1.0, -0.8, 0.35))]
        for f in cs2_preview.preview(t, asset_dir, name, prev, modes=tuple(modes),
                                     views=views, lods=lods or None, prev_generation=ab):
            print(f"[terminal] preview -> {f}")
        t.data.materials.clear()      # export stays material-free per the import contract

    switch = None
    if lods:
        _c, size = lift_tower.world_bounds(t)
        switch = cs2_preview.lod_switch_table(size, [tris[i] for i in sorted(tris)])
    # -- manifest + folder validation (L) -----------------------------------------------
    density = compose.density if compose is not None else None
    cs2_validate.write_manifest(asset_dir, name, {
        "generator": "lift_terminal.py", "kind": args.kind, "spec": "roundL", "rope_r_m": M_ROPE_R,
        "tris": tris, "parts": len(table), "atlas_px": FINISH_ATLAS if finished else ATLAS_QUAD_PX * 2,
        "finish": {"packer": f"cs2_uv.pack_unique(margin_px={PACK_MARGIN_PX})", "ao_samples": AO_SAMPLES,
                   "detail": sorted(detail_specs().keys()),
                   "height_range_m": compose.height_range if compose else None} if finished else None,
        "density_worst": [{"part": table[p - 1] if 0 < p <= len(table) else p,
                           "source_px_per_m": round(s, 1), "shipped_px_per_m": round(dd, 1)}
                          for p, s, dd in (density or [])[:8]],
        "submeshes": ["Win"] if win is not None else [],
        "engine_base": {k: base_report[k] for k in ("verdict", "ok_edges", "blocked_edges")},
        "lod_switch_m": switch,
    })
    if not args.no_validate:
        # free the names so the validator's re-imports do not land as `<stem>.001`
        t.name, t.data.name = name + "Src", name + "Src"
        if win is not None:
            win.name, win.data.name = name + "WinSrc", name + "WinSrc"
        for i, lod in enumerate(lods, start=1):      # alive for the review pack below
            lod.name, lod.data.name = f"{name}LOD{i}Src", f"{name}LOD{i}Src"
        findings = cs2_validate.validate_asset_folder(asset_dir, name, {
            "kind": "building", "lod0_cap": cap, "lod2_cap": LOD2_TARGET[args.kind],
            "hero": True, "expected_lods": tuple(range(1, len(lods) + 1)),
            "expected_submeshes": ("Win",) if win is not None else (),
        })
        n_err = sum(1 for f in findings if f[0] == "ERROR")
        n_warn = sum(1 for f in findings if f[0] == "WARN")
        print(f"[terminal] validate_asset_folder: {n_err} errors, {n_warn} warnings")

    # -- live-review pack (ROUND-SPEC R10, --review): DUPLICATES of the built LOD0..2 and `_Win` go to <DIR>/<name>.blend,
    # AFTER export, previews, manifest and validation (pipeline-gap.md section 6), so a pack failure never leaves a
    # half-written folder. write_pack names its copies from `name`, not from the (renamed) build objects (gate:
    # tests/test_review_side_effects.py, station with and without --review).
    if args.review is not None:
        import cs2_review
        cs2_review.write_pack(name, asset_dir, dict(enumerate([t] + lods)),
                              {"Win": win} if win is not None else {},
                              meta={"generator": "lift_terminal.py", "args": vars(args),
                                    "kind": "building",
                                    "caps": {0: cap, 2: LOD2_TARGET[args.kind]}},
                              out_dir=args.review or None)
    for lod in lods:
        me = lod.data
        bpy.data.objects.remove(lod, do_unlink=True)
        bpy.data.meshes.remove(me)
    print(f"[terminal] asset folder: {asset_dir}")
    print(f"PASS lift_terminal: {name} LOD0 {tris[0]} tris, chain "
          f"{[tris[i] for i in range(1, len(lods) + 1)]}, _Win {'yes' if win else 'no'}, "
          f"engine base {base_report['verdict']}, finish {'on' if finished else 'off'}")


if __name__ == "__main__":
    try:
        main()
    except Exception:  # noqa: BLE001 - unambiguous exit status for the harness
        import traceback
        traceback.print_exc()
        sys.exit(1)
