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

L (art pipeline v2, DECISIONS 103/104): the baked static chairs are GONE - the live chairs
now dive from the entry tower, round the bullwheel and climb back out on the modelled rope
(ChairPinSystem), so a static chair would duplicate a moving one. Every part is smooth-shaded
by angle with weighted normals (cs2_asset.shade_by_angle - the flat primitive default reached
the game as lit facets), parts are tagged before the join, and --lods ships an authored
LOD1/LOD2 chain decimated from the finished mesh (cs2_asset.make_lod_chain).

L full v2 (roundL-spec, 2026-09-22):
  * ENGINE BASE instead of the modelled 3 m skirts: pad / hut base / knoll ramps + crest are
    OPEN-BOTTOM parts with their side-face bottom edges exactly at z = 0 (cs2_kit.add_box /
    add_wedge open_bottom=True). BatchMeshHelpers.AddBaseLines builds the foundation skirt
    from those edges and DROPS any edge that touches a face lying at/below y = 0, which is
    why the closed E pads floated and K needed skirts. cs2_validate.ground_ring_report is
    the acceptance check (main() refuses to export when the pad / hut base are not "OK").
  * Parts kit v1 (cs2_kit): leg flanges with real studs, portal gussets, rail stiffener
    rings, groove-profile bank sheaves and bullwheel rim (rope torus 1.82 and the wheel
    centre unchanged), cable clamps, catwalk grating + handrail, hut doors, louvred
    hatches, roof drip edges; return station: mast base/collar flanges, knoll handrails.
  * `_Win` sub-meshes: the hut and cabin glass bands leave the main mesh (cs2_submesh,
    room cells (2,1) hut / (3,2) cabin) and ship as <Name>_Win.fbx + <Name>_LOD1_Win.fbx.
  * Textures 2048 (quad_size 1024): unique repack with density control (cs2_uv.finish_packer,
    corrugated walls x2), geometry-keyed relief layer (cs2_compose: corrugation 0.076 m on
    machinebox/cabin/hut, seams 1.2 m + bolt rows on legs/beams/rails, 0.10 m helix on
    ropes, grate on the catwalk) blended into the Normal, per-part value noise, AO.
  * Validators + manifest (cs2_validate) and the shared preview rig (cs2_preview:
    pbr / dusk / normals / lod / ab vs K).

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

    L: the rim is the cs2_kit.sheave groove profile spun 32 times (r 1.80, width 0.40,
    groove 0.06 deep, inner radius 1.55 so the spokes bury in it) instead of the K torus;
    outer edge still 1.80, centre untouched. Name: sheave_<prefix>bull_rim.
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
            # Tiny NON-DEGENERATE patch on quiet paint texels (was a single point:
            # zero-area islands upset tangent export and, since the finish pass,
            # pack_islands during the unique repack). L: projected along the face's
            # own dominant axis - the x/z projection left the +-X end faces and the
            # top/bottom faces with zero UV area (cs2_validate uv-zero).
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


# Boxy parts that take the finishing-pass bevel. Glass bands and the rope torus are
# excluded. (L: the baked static chairs are gone - live chairs ride through the terminal;
# the skirts are gone too - the ENGINE builds the foundation from the open-bottom ground
# parts' z = 0 edges, which are boundary edges and therefore never bevelled.)
BEVEL_PREFIXES = ("pad", "beam_", "saddle_", "endmember_", "carr_",
                  "machfloor", "machinebox", "boxroof", "cabfloor", "cabin", "cabroof",
                  "hutbase", "hut", "hutroof", "cabinet", "bankmount_",
                  "plinth", "crest", "rampup", "rampdown")
BEVEL_EXCLUDE = ("hutwin", "cabwin")

# Smooth-by-angle threshold (art-pipeline-v2.md 3.1): 66 keeps the 6-segment tubes
# (60 deg between faces) smooth, every box edge (90) and the knoll ramp breaks (~68) sharp.
SHADE_ANGLE = 66.0

# Authored LOD chain (art-pipeline-v2.md 3.7): parts deleted outright per level, the rest
# collapse-decimated to an absolute triangle target (LOD1 = 45 % of LOD0, LOD2 = 500 drive /
# 400 return). Level 1 drops sub-pixel hardware (L: + every kit stud, clamp, drip edge,
# hatch, handrail post and kick plate); level 2 keeps only the masses that make a terminal
# recognisable at 300 m: pad, legs, beams, rails, housing, cabin, hut, wheel rim.
# NOTE the kit's rail prefix: the catwalk / knoll handrails are "rail_catwalk_*" /
# "rail_knoll_*" - never a bare "rail_", which would also match the main tube rails.
LOD1_DELETE = ("raillug_", "cabmull_", "stovepipe", "wx", "lrung_", "post_",
               "tcable_", "ropeguard_", "sheave_bank", "bankmount_", "cwplate",
               "hatch_", "bolt_", "clamp_", "drip_", "kick_")
LOD2_DELETE = LOD1_DELETE + ("spoke", "tspoke", "wheelhub", "twheelhub", "rail_catwalk",
                             "rail_knoll", "rail_bay", "lrail_", "grate_", "scale_", "railcap_",
                             "flange_", "gusset_", "stiff_", "door_",
                             "saddle_", "endmember_", "carr_", "cabinet", "hutroof",
                             "boxroof", "cabroof", "machfloor", "cabfloor", "mastcollar",
                             "plinth", "wheelshaft", "cweight", "divrope", "wheelrope")

# Texel-density weights for the unique repack (cs2_uv.set_density, longest prefix wins):
# corrugated walls x2 (the 0.076 m corrugation needs >= ~53 px/m to clear the 2-texel
# floor at 2048), ropes x1.5 for the helix, legs/beams/rails x1.5 for the bolt rows.
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
# Island margin for the unique repack, in texels. cs2_uv's default max(16, A // 64) is
# 32 px at 2048 (64 px between neighbours): with ~300-960 smart-projected islands that
# packed the stations at a median 36 px/m (12 px: 35 px/m - the gap, not the margin
# formula, dominates at this island count). 6 px = 12 px gap = 1.5 texels at mip 3,
# the same gap-per-mip K shipped at 4096 (margin 0.004); the bake margin (8 px EXTEND)
# fills the gutter with the island's own tone either way.
PACK_MARGIN_PX = 6


def finish_packer(obj, mesh, A, reserved):
    """
    lift_tower.apply_finish_textures `packer` hook: cs2_uv.pack_unique with the station
    margin (PACK_MARGIN_PX) instead of cs2_uv.finish_packer's default. The finish pass
    has already created "UVUnique" from "UVAtlas"; pack_unique rebuilds it from the
    active source layer, so the source is re-activated first (same result, tighter pack).
    """
    mesh.uv_layers.active = mesh.uv_layers["UVAtlas"]
    cs2_uv.pack_unique(obj, A, reserved, margin_px=PACK_MARGIN_PX)
LOD0_CAP = {"drive": 7000, "return": 3600}
LOD2_TARGET = {"drive": 500, "return": 400}


def detail_specs():
    """
    Relief layer keyed by part-name prefix (cs2_compose.detail_height, longest prefix
    wins; an empty DetailSpec() EXCLUDES a longer name from a shorter prefix's feature):
    corrugation 0.076 m on machinebox / cabin / hut, seams 1.2 m + bolt rows on legs,
    beams, rails and the return mast, 0.10 m helix on every rope, painted grate on the
    catwalk, pressed seams on the door leaves.
    """
    D = cs2_compose.DetailSpec
    corr = dict(corrugation_m=0.076, corr_depth_m=0.002)
    # 36 mm heads (M24 structural bolts): 2 texels at >= 56 px/m, which the legs / rails /
    # beams reach at their 1.5x density weight on the 2048 atlas (28 mm heads did not).
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


def make_post_compose(obj, reserved):
    """
    lift_tower.apply_finish_textures `post_compose` hook (linear space, after the AO
    multiply): rasterise the relief layer on the unique layout, composite it through
    cs2_compose.compose_maps (per-part value noise, RNM detail normal, gloss variation,
    channel rules; ao=None because the finish pass already multiplied its AO in), print
    the density report while both UV layers still exist, then give the uncovered gutter
    a flat normal and renormalise the covered texels (photo normal sets are not unit
    length; nz clamped >= 0.05 keeps B > 0.5 everywhere).
    """
    def hook(base, nrm, msk, ctl, ao, mesh, A):
        hook.density = cs2_uv.density_report(obj, A, reserved_rects=reserved)
        part_map, ppm_map = cs2_compose.rasterise_face_fields(mesh, A)
        h = cs2_compose.detail_height(mesh, A, detail_specs())
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
    mound_w = 6.0                     # spans both rope lines plus chair overhang (L: 5.4 ->
                                      # 6.0 so the knoll handrails at +-2.9 clear a chair
                                      # riding at y 1.8 +- 0.9)

    # Unload knoll: up-ramp facing the line, crest deck under the wheel, run-out down back.
    # E: shortened both ramps (footprint was sprawling on slopes). L: the K foundation
    # skirt is GONE - the knoll is OPEN-BOTTOM with its side-face bottom edges exactly at
    # z = 0 (ramp toes at 0.0, not -0.02), so the ENGINE extrudes the foundation from those
    # edges down to the terrain (BatchMeshHelpers.AddBaseLines; see the module docstring).
    # The crest box drops its +-X faces: they would coincide with the ramps' end faces.
    parts.append((cs2_kit.add_wedge("rampup", -4.6, -1.1, mound_w, 0.0, crest_z,
                                    open_bottom=True), QUAD_CONCRETE))
    parts.append((cs2_kit.add_box("crest", (2.4, mound_w, crest_z), (0.1, 0, crest_z / 2.0),
                                  open_bottom=True, drop=("+X", "-X", "+Y")), QUAD_CONCRETE))
    parts.append((cs2_kit.add_wedge("rampdown", 1.3, 2.6, mound_w, crest_z, 0.0,
                                    open_bottom=True), QUAD_CONCRETE))
    # L (user verdict): the liftie box stands LEVEL WITH THE CREST, not on the ground beside
    # the knoll ("a liftie looking out would just be staring at a wall"). A concrete bay on
    # the +Y side of the knoll carries it: open-bottom, edges at z 0 (engine base), top at
    # crest_z, 3.6 m along the line so it also retains the up-ramp and run-out beside it
    # (its -X/+X faces stand above the ramps as retaining walls). The crest drops its +Y
    # face - buried inside the bay.
    bay = (3.6, 2.6, crest_z)
    bay_x, bay_y = 0.1, mound_w / 2.0 + bay[1] / 2.0
    parts.append((cs2_kit.add_box("liftiebay", bay, (bay_x, bay_y, crest_z / 2.0),
                                  open_bottom=True), QUAD_CONCRETE))

    def z_rampup(x):
        return crest_z * (x + 4.6) / 3.5

    def z_rampdown(x):
        return crest_z * (2.6 - x) / 1.3

    # Stout centre mast on a plinth. Liftblog img_6900 (the real Pali summit): one FAT
    # clean tube, no gussets, wheel right on top - so the old thin mast+fin cluster is
    # replaced by a thicker tube with a collar flange under the wheel bearing.
    # Everything stays inside r=0.6 of centre - the chairs' swing circle.
    parts.append((cs2_kit.add_box("plinth", (0.9, 0.9, 0.35), (0, 0, crest_z + 0.175),
                                  open_bottom=True), QUAD_CONCRETE))
    parts.append((add_cyl("mast", 0.42, wheel_z - 0.17 - crest_z,
                          (0, 0, crest_z + (wheel_z - 0.17 - crest_z) / 2.0),
                          segments=14), QUAD_PAINT))
    parts.append((add_cyl("mastcollar", 0.52, 0.18,
                          (0, 0, wheel_z - 0.38), segments=14), QUAD_PAINT))
    # L kit: bolted base flange on the plinth (real studs - ground level) and a plain
    # collar flange (painted bolt circle) under the wheel bearing; both inside r 0.6.
    parts.extend(cs2_kit.flange("mastbase", 0.58, 0.04, bolts=8, segs=16,
                                loc=(0, 0, crest_z + 0.35 + 0.02), axis="Z",
                                bolt_geo=True, caps="top"))
    parts.extend(cs2_kit.flange("mastcollar", 0.58, 0.03, bolts=12, segs=16,
                                loc=(0, 0, wheel_z - 0.38 + 0.09 + 0.015), axis="Z",
                                bolt_geo=False))

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

    # L: NO baked chairs. The live chairs (ChairPinSystem, DECISIONS 103) dive from the
    # entry tower, round this wheel and climb back out on exactly the rope modelled above;
    # a static chair here duplicated a moving one and faced against travel on its side.

    # L kit: guard rails along both knoll edges (h 1.10), from the up-ramp to the run-out,
    # 0.1 m inside the mound edge so posts stand on concrete. Names post_knoll_*,
    # rail_knoll_*_top/_knee, kick_knoll_*.
    ry_ = -(mound_w / 2.0 - 0.1)
    path = [(-4.0, ry_, z_rampup(-4.0)), (-1.1, ry_, crest_z),
            (1.3, ry_, crest_z), (2.3, ry_, z_rampdown(2.3))]
    parts.extend(cs2_kit.handrail("knoll_-1", path, h=1.10, knee=0.55, kick=0.10,
                                  post_every=1.5, segs=6, radius=0.028))
    # +Y side: the knoll rail runs up the ramp to the bay's -X wall, then a second rail
    # wraps the bay's three outer edges (0.1 m inside them) at crest height.
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

    # Liftie box ON the bay, level with the crest: glazing toward the unload (-Y), the
    # arriving line (-X) and the back (+Y); the door is on the downhill end wall (+X) so
    # no pane faces a wall. The slab drops its bottom face (buried in the bay top).
    hut = (1.8, 1.6, 2.3)
    hx, hy = bay_x, mound_w / 2.0 + 0.3 + hut[1] / 2.0
    hz0 = crest_z + 0.15                     # hut floor = bay top + slab
    parts.append((cs2_kit.add_box("hutbase", (hut[0] + 0.3, hut[1] + 0.3, 0.15),
                                  (hx, hy, crest_z + 0.075), drop=("-Z",)), QUAD_CONCRETE))
    parts.append((add_box("hut", hut, (hx, hy, hz0 + hut[2] / 2.0)), QUAD_PAINT))
    # Glass band leaves the main mesh as the `_Win` sub-mesh (main() splits it off); the
    # +X (door) pane is dropped.
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
    Shared tail of both builders (L): bevels -> density weights -> part tags -> quadrant
    unwrap -> join -> remove_doubles -> part table -> shade -> base pivot -> orient_for_net.
    Parts with quad None (the tension-scale plates) keep their hand-set UVs.
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

    # L: OPEN-BOTTOM pad with its side-face bottom edges exactly at z = 0 - the ENGINE
    # builds the foundation skirt from them (module docstring); the K skirt box is gone.
    parts.append((cs2_kit.add_box("pad", pad, (0, 0, pad[2] / 2.0), open_bottom=True),
                  QUAD_CONCRETE))

    # FOUR fat tube legs (img_6870: ~0.6 m round columns, not lattice), one pair per
    # portal plane, standing outside the corridor, each pair tied by a transverse box
    # beam whose top carries the rails. Saddle blocks make the rail/beam joints read
    # heavy instead of tangent. L kit: bolted base flanges (8 real studs each, ground
    # level) replace the legfoot boxes; twin gusset webs tie every leg to its beam.
    beam_z = 4.55                    # beam centre; beam spans z 4.35..4.75
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

    # Twin FAT tube rails - the low long chassis that IS the Pali silhouette - with
    # proud round end caps (img_6870: open tube ends with cap flanges) and box end
    # members tying the pair together at both ends. Small lifting lugs on top at the
    # ends, straight off img_6828.
    for ry in (-rail_y, rail_y):
        parts.append((add_cyl(f"rail_{ry:.0f}", rail_r, rail_len, (0, ry, rail_z),
                              segments=16, rotation=(0, math.radians(90), 0)), QUAD_PAINT))
        # L kit: welded stiffener rings. The painted tension scale occupies x -0.3..3.7
        # and the back saddle / end member 3.7..4.8, so the rings sit on the front span
        # and in the gap before the end member (3 per rail).
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
        a0, a1 = (wheel_x + 0.35, s * 0.5, 4.55), (4.42, s * 0.5, 4.80)
        b0, b1 = (4.60, s * 0.45, 4.80), (cw_x + 0.05, s * 0.45, cw_top)
        parts.append((add_strut(f"tcable_a_{s}", a0, a1, 0.030), QUAD_PAINT))
        parts.append((add_strut(f"tcable_b_{s}", b0, b1, 0.030), QUAD_PAINT))
        # L kit: two-piece cable clamps at the yoke end and at the drum end (local X
        # along the cable), 0.10 m in from the cable ends.
        for tag, p0, p1, at_start in (("a", a0, a1, True), ("b", b0, b1, False)):
            d = mathutils.Vector(p1) - mathutils.Vector(p0)
            u = d.normalized()
            c = mathutils.Vector(p0) + u * 0.10 if at_start else mathutils.Vector(p1) - u * 0.10
            parts.extend(cs2_kit.cable_clamp(f"tc{tag}_{s}", 0.030, loc=tuple(c),
                                             rot=u.to_track_quat("X", "Z").to_euler()))

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
        for k, sxo in enumerate((-0.2, 0.2)):
            # L kit: groove-profile sheaves (sheave_bank_<ry>_<k>_rim / _hub), axle on Y
            parts.extend(cs2_kit.sheave(f"bank_{ry:.0f}_{k}", 0.17, 0.13,
                                        (front_x + sxo, ry, bank_z), "Y", segs=12))
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

    # L: NO baked chairs at the load point or under the frame. The live chairs
    # (ChairPinSystem, DECISIONS 103) ride segment A, the bank, segment B and the wheel
    # wrap; the K-generation static pair duplicated them and faced against travel.

    # -------- topside: machinery box over the wheel, operator cabin at the back -------
    # Machinery box directly above the carriage/wheel (drive machinery drives the wheel
    # shaft - vents + hatch, no glazing; locked design decision). Floor plates close the
    # gap down to the rails.
    box = (3.2, 2.4, 1.9)
    bx = 0.3
    parts.append((add_box("machfloor", (box[0] + 0.1, box[1] + 0.1, 0.12),
                          (bx, 0, deck_z - 0.06)), QUAD_PAINT))
    parts.append((add_box("machinebox", box, (bx, 0, deck_z + box[2] / 2.0)), QUAD_PAINT))
    # L kit: louvred access hatch on the line-side (-X) wall replaces the hatch slab, a
    # wide louvred vent hatch on the +X wall replaces the three vent slats; drip edge
    # flashing around the roof box.
    parts.extend(cs2_kit.hatch("mach", 0.8, 1.3, reveal=0.03,
                               loc=(bx - box[0] / 2, 0.3, deck_z + 0.75), facing="-X"))
    parts.extend(cs2_kit.hatch("vent", 1.2, 0.6, reveal=0.03,
                               loc=(bx + box[0] / 2, 0, deck_z + 0.9), facing="+X"))
    boxroof = add_box("boxroof", (box[0] + 0.25, box[1] + 0.25, 0.09),
                      (bx, 0, deck_z + box[2] + 0.045))
    parts.append((boxroof, QUAD_GALV))
    parts.extend(cs2_kit.drip_edge(boxroof, drop=0.06, t=0.02))

    # Operator cabin ON the frame toward the back (img_6828/6870: corrugated black box,
    # wrap-around glass band, roof overhang, stovepipe) - in ADDITION to the ground hut.
    cab = (2.2, 2.5, 2.05)
    cx = 3.4
    parts.append((add_box("cabfloor", (cab[0] + 0.1, cab[1] + 0.1, 0.12),
                          (cx, 0, deck_z - 0.06)), QUAD_PAINT))
    parts.append((add_box("cabin", cab, (cx, 0, deck_z + cab[2] / 2.0)), QUAD_PAINT))
    cab_band_z = deck_z + cab[2] * 0.62
    # Wrap-around glass band: leaves the main mesh as the `_Win` sub-mesh (main() splits
    # it off, room cell (3, 2)); the corner mullions stay in the main mesh.
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

    # Catwalk along the +Y side of the deck (img_6828: the operator walks the frame
    # between ladder and cabin). L kit: opaque painted grating (holes in the normal map -
    # a see-through cutout would flip the whole surface to DefaultCutout) and a proper
    # guard rail (h 1.10, knee rail, kick plate) on the outboard edge.
    parts.extend(cs2_kit.grating("catwalk", 6.8, 0.55, t=0.06, loc=(1.6, 1.73, deck_z - 0.03)))
    parts.extend(cs2_kit.handrail("catwalk", [(-1.8, 1.95, deck_z), (5.0, 1.95, deck_z)],
                                  h=1.10, knee=0.55, kick=0.10, post_every=1.5, segs=6,
                                  radius=0.028))

    # Ladder up the back end to the deck (img_6828: straight ladder to the cabin)
    lad_x, lad_top = rail_len / 2 + 0.25, deck_z + 0.25
    for ly in (-0.25, 0.25):
        # L: the rails stand ON the pad (z 0.3), bottom cap buried - a rail starting at
        # z = 0 put a face into the engine's ground band and BLOCKED the base there.
        parts.append((cs2_kit.add_cyl(f"lrail_{ly}", 0.035, lad_top - pad[2],
                                      (lad_x, ly, (lad_top + pad[2]) / 2.0), segments=6,
                                      caps="top"), QUAD_GALV))
    for i in range(1, 10):
        parts.append((add_cyl(f"lrung_{i}", 0.024, 0.5,
                              (lad_x, 0, i * lad_top / 10.0),
                              rotation=(math.radians(90), 0, 0), segments=6), QUAD_GALV))

    # Ground-level glazed liftie hut off to the side (stays - locked decision), plus the
    # electrical cabinet on the pad.
    hut = (1.8, 1.6, 2.3)
    hx, hy = -1.2, pad[1] / 2.0 + hut[1] / 2.0 + 0.55
    # L: OPEN-BOTTOM base with its edges at z = 0 (engine base); the K hut skirt is gone.
    parts.append((cs2_kit.add_box("hutbase", (hut[0] + 0.3, hut[1] + 0.3, 0.15),
                                  (hx, hy, 0.075), open_bottom=True), QUAD_CONCRETE))
    parts.append((add_box("hut", hut, (hx, hy, 0.15 + hut[2] / 2.0)), QUAD_PAINT))
    # Glass band leaves the main mesh as the `_Win` sub-mesh (room cell (2, 1)); the -Y
    # (pad-facing) pane is dropped to make room for the door.
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

    # The painted 1..16 tension scale on the outboard face of each rail (img_6828).
    # UVs are hand-set onto the stamped atlas strip, so these two plates skip the
    # normal quadrant unwrap (quad None).
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
    return ap.parse_args(argv)


# Ground parts that must feed the engine base (cs2_validate.ground_ring_report ok_parts).
ENGINE_BASE_PARTS = {"drive": ("pad", "hutbase"),
                     "return": ("rampup", "crest", "rampdown", "liftiebay")}
# `_Win` room cells per pane group (int(v*5)*5 + int(u*5) of the first vertex: hut 7, cabin 13)
WIN_CELLS = {"hutwin": (2, 1), "cabwin": (3, 2)}


def split_windows(t, name, table):
    """
    Move the hut (and, on the drive station, the cabin) glass bands off the main mesh
    into ONE `<name>_Win` sub-object: cs2_submesh.split_submesh per pane group (each with
    its own room cell), the second sub joined into the first. Returns the sub or None
    when the mesh carries no pane parts. Must run after orient_for_net and BEFORE the
    finish pass, so the bake never sees pane texels and the LOD chain never sees panes.
    """
    groups = [(pfx, cell) for pfx, cell in WIN_CELLS.items()
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


def check_engine_base(t, kind):
    """
    The skirt-removal acceptance gate: cs2_validate.validate_mesh(kind="building") must
    report the engine base OK, and every ground part of this station must contribute
    OK edges. Raises RuntimeError otherwise (nothing is exported).
    """
    findings = cs2_validate.validate_mesh(t, kind="building")
    errors = [f for f in findings if f[0] == "ERROR"]
    base_ok = any(f[0] == "INFO" and f[1] == "engine-base" for f in findings)
    r = cs2_validate.ground_ring_report(t)
    missing = [p for p in ENGINE_BASE_PARTS[kind] if p not in r["ok_parts"]]
    print(f"[terminal] engine base: verdict {r['verdict']}, {r['ok_edges']} OK edges / "
          f"{r['blocked_edges']} blocked / {r['below_verts']} verts below -1 cm; "
          f"ok parts {list(r['ok_parts'].keys())}")
    if errors or not base_ok or missing:
        raise RuntimeError(f"engine-base gate failed: errors {errors}, base_ok {base_ok}, "
                           f"missing ground parts {missing}, offending {r['offending_parts']}")
    return r


def main():
    args = parse_args()
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

    # post hook stamps the white 1..16 tension scale into THIS asset's atlas copy - the
    # shared lift atlas recipe itself stays untouched for towers and chairs.
    # quad_size=1024 (L): 2048 atlas (was 4096 in J/K) - the density report at 4096
    # showed ~55-65 px/m packed, which 2048 reaches with the unique repack.
    # paint_pad_strip is composed in (J follow-up) so every lift atlas carries the same
    # crash-pad strip - stations have no pads, but the palette stays identical across
    # the system and quad 2's reserved headroom is painted, not undefined.
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
        # Texture half of the finishing pass (shared pipeline in lift_tower). BOTH
        # stamped strips are density-protected through the repack. L: the unique
        # repack is cs2_uv.finish_packer (density weights) and the relief / compositing
        # layer runs through make_post_compose before the sRGB encode.
        compose = make_post_compose(t, reserved)
        finished = lift_tower.apply_finish_textures(
            t, asset_dir, name, atlas_size=FINISH_ATLAS, reserved_strips=reserved,
            ao_samples=AO_SAMPLES, packer=finish_packer, post_compose=compose)
        if not finished:
            raise RuntimeError("finish pass failed (see [tower] finish log above)")

    cs2_asset.export_fbx(t, os.path.join(asset_dir, f"{name}.fbx"))
    if win is not None:
        # _Win at LOD1 AND LOD2 (review F12): vanilla ships _LOD2_Win so rooms stay lit
        # beyond the LOD2 switch (~300 m); the pane mesh is 14 tris, the cost is nil.
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
        # the three legacy views plus "back" (-X post-orient): the hut door and the
        # machinery hatch face that way and never showed in the K frames.
        views = list(cs2_preview.DEFAULT_VIEWS) + [("back", (-1.0, -0.8, 0.35))]
        for f in cs2_preview.preview(t, asset_dir, name, prev, modes=tuple(modes),
                                     views=views, lods=lods or None, prev_generation=ab):
            print(f"[terminal] preview -> {f}")
        t.data.materials.clear()      # export stays material-free per the import contract

    switch = None
    if lods:
        _c, size = lift_tower.world_bounds(t)
        switch = cs2_preview.lod_switch_table(size, [tris[i] for i in sorted(tris)])
    for lod in lods:
        me = lod.data
        bpy.data.objects.remove(lod, do_unlink=True)
        bpy.data.meshes.remove(me)

    # -- manifest + folder validation (L) -----------------------------------------------
    density = compose.density if compose is not None else None
    cs2_validate.write_manifest(asset_dir, name, {
        "generator": "lift_terminal.py", "kind": args.kind, "spec": "roundL",
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
        findings = cs2_validate.validate_asset_folder(asset_dir, name, {
            "kind": "building", "lod0_cap": cap, "lod2_cap": LOD2_TARGET[args.kind],
            "hero": True, "expected_lods": tuple(range(1, len(lods) + 1)),
            "expected_submeshes": ("Win",) if win is not None else (),
        })
        n_err = sum(1 for f in findings if f[0] == "ERROR")
        n_warn = sum(1 for f in findings if f[0] == "WARN")
        print(f"[terminal] validate_asset_folder: {n_err} errors, {n_warn} warnings")
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
