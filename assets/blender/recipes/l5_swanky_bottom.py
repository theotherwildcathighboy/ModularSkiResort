"""
recipes/l5_swanky_bottom.py - L5 "swanky bottom" lodge (lux-base) + the shared swanky helpers
and the reusable clock-tower part.

Contract: docs/research/art/assets-2026-10/design/lodges-swanky.md section 3 (L5) and 5 (clock
tower), with design/lodges-swanky-skeptic.md applied (MUST-CHANGE 4 by-construction contract
fixes, 6 determinism, 7 LOD table, 9 API alignment) and the ROUND-SPEC rulings R21 (complexity
1, no "educated staff": prefab side, see build/lodges-swanky.md), R24 (prefiltered CC0 sources:
the kit does it) and R25 (the kit API as built wins: cs2_bldg / lodge.py). Build report:
docs/research/art/assets-2026-10/build/lodges-swanky.md.

REVISION 1 (2026-10-11, verdicts-2026-10-11.md B1: "the Swanky lodge should be bigger",
reference = the Breckenridge Grand Lodge on Peak 7, "pools are a good addition"): a U-shaped
5-storey grand lodge on a 10 x 8 lot (main bar + two wings, cross gables over balcony stacks,
stone chimneys, the revision-0 great hall / porte-cochere / clock tower, a pool terrace in the
courtyard). Build report section "Revision 1" in build/lodges-swanky.md.

REVISION 2 (2026-10-11, verdicts-2026-10-11.md "Seventh look" B1': "looks really good"; facade
colour stays): (a) an END-TO-END PLAZA on the road side (+X) - paved across the whole lot
width, ski racks with real gear (cs2_bldg.ski_rack_frame gear=True), cafe tables with
chairs and umbrellas, benches, bins, planters with conifers, lamp posts, a bollard-lined
drive to the kept porte-cochere - so the lot grows 10 x 8 -> 10 x 9 (one cell deeper toward
the road) and the whole build moves X_SHIFT = -4 m after assembly (shift_x) so the back
margin stays 0.6 m and the plaza gets the new 8 m; (b) the CLOCK moves DOWN from the stage
(24.9, its top under the cap's eave) onto the shaft (CT_CLOCK_Z_L5 20.3, a 2.9 m dial on a
glulam dial board), the stage gets louvres; the sightline is a contract
(check_clock_sightline); (c) ASYMMETRIC front: an ELEVATED DECK one storey up (top Z_F[1]
4.5) on the clock-tower side, wrapping the tower base (glulam posts on stone piers, beams,
honey board balustrade, a 3 m stair down to the plaza, bistro tables, lanterns, a deck-level
tower door). The -Y side of the front stays as built. Build report section "Revision 2
(plaza, clock, tower deck)" in build/lodges-swanky.md.

REVISION 3 (2026-10-11, verdicts-2026-10-11.md "Twelfth look" B1'': "looks good, but we didn't
need to place / create our own trees and benches - use the in-game ones. Only put placeholders
if they are cheap, and let me know if they are placeholders"; the raised deck "is great, but
the STAIRS should be to the RIGHT, so that cars can pull through the valet area"; "I wanted
the PLAZA ON THE POOL SIDE"): (1) the plaza moves to the slope / pool side (`deck_s*`, end to
end along the slope face, joined to the pool terrace by a paved strip and a flight of steps
on the podium's -X face); the lot's extra cell moves with it (X_SHIFT = +4: the build moves
4 m toward the road); the road side returns to a forecourt with a valet loop (`deck_e0`
drive, `deck_e1` walks, `deck_e2` raised island); (2) every tree, bench, bin, table, chair,
parasol, lounger, lamp post, planter and bollard is a VANILLA sub-object by reference
(SUB_OBJECTS -> manifest `sub_objects`, GUIDs in VANILLA, verified against the dump by
tests/test_lodges_swanky.py); the modelled ones are gone; modelled stand-ins that remain are
listed in PLACEHOLDERS; (3) the deck stair runs from the deck's right-hand (+Y) edge
(DK_STAIR_*), its foot on the walk, clear of the drive (contract: the drive's free corridor
past the deck >= CORRIDOR_MIN). Build report section "Revision 3 (pool-side plaza, valet drive,
stair right, vanilla props)" in build/lodges-swanky.md.

REVISION 4 (2026-10-12, verdicts-2026-10-11.md "Eighteenth look": "the front triangle above the
valet area needs... something. Right now it's empty space you can see into"; "there are no doors
on the pool side of the building, we will probably need some"; "does it look real" detail):
(a) the porte-cochere gable is closed: a tongue-and-groove board infill (`gable_pcgab1`, new
material `lux_tg`) just behind the front king-post truss and one behind the back truss
(`gable_pcgab0`), the ceiling between them, frieze boards over the tie beams (`trim_pcfrieze`);
two vanilla WallLightShortRangeRandom01 rows light the gable (mount "wall"); contract G1 (a ray
grid through the gable from every arrival camera and a close walk-around meets a front face
within GABLE_DEPTH_MAX of the truss face; PC_GABLE_INFILL False = the revision-3 gable fails it);
(b) three French doors onto the pool terrace: one in each wing's courtyard face under its cross
gable and one in M's courtyard face on the plaza axis (terrace_door: a REAL opening cut into the
host wall and recessed DOOR_SET, glazed leaves with lites, kick plates and pulls, a 1.80 x 2.30
clear opening, the threshold flush with the walk), each aligned with a flight up to the podium
(the +-Y flights re-centred on the wing doors, a new +X flight `stairblk_e2` on the plaza axis;
the pool loungers / parasols moved off it); contracts D1-D5 (check_door: real opening, clear
opening, threshold on the walk, aligned + unobstructed walk to the steps, no gutter / downspout
in the clear box). Build report section "Revision 4 (gable infill, pool-side doors)".

FINE-DETAIL PASS, step 3e (2026-10-12, verdicts-2026-10-11.md "Twenty-third look": "L5 and L4 look
good, run the engine pass on them too!"; design/fine-detail-pass.md A.2 / B / C as cut by -skeptic.md
FS6 / FS14 / FS15; build report docs/research/art/assets-2026-10/build/fine-detail-step3e-swanky.md):
RECIPE is a fine_detail.FineRecipe (fine_detail=True, sign_set=SIGN_SET); `lodge.py --no-fine`
rebuilds the approved revision 4 from `legacy` (byte-identical, proven in the build report):
  - the 4 modelled plaza racks (PZ_RACKS) -> dressing-kit rows SkiDressSkiRack / V2 / V3 / V1
    (KIT_ROWS: identity, on the plaza slab, m_ParentMesh 0) = the removed frames vertex for vertex;
    contract F1 (cs2_kitrows.check_kit_rows: the REAL kit mesh on the plaza, clear of every modelled
    triangle, every vanilla row box and the plaza walks; rack access aisles free; inside the lot);
  - the 4 wall lanterns on M's road face (lamp_pm0-3 + arms) -> WallLightShortRangeRandom01 rows at
    their mount points (FINE_ROWS; pivot on the pier / wall face at the lens height, facing the road;
    S1-S4 like every vanilla row);
  - a porte-cochere NAME BOARD ("GRAND LODGE", 5.00 x 0.80 m on the front truss chord; the lodge carried
    no lettered board): its face goes to the sign set SkiSignSetGrandB (the shared sheet's reserved
    `l5.name` rect painted in place, vector Bfont at 300 px/m, lit 0.2 at night); the frame and a
    backing slab stay in the host (FS1);
  - fine geometry (design B.1): the clock-tower finial 12-seg (was 6), the ridge flue pots 12-seg (8),
    the hanging lanterns' rods 8-seg round (0.04 m box);
  - PLACEHOLDERS left: the 2 fire pits and the 2 hanging porte-cochere lanterns (vanilla has no pendant).

Authoring frame (bldg-kit.md section 2): +X = arrival / road (game +Z), -X = slope side, z 0 =
grade at the lot centre; lot 10 x 9 cells -> |Y| <= 40, |X| <= 36 (revisions 2 / 3: the
builder works in the revision-1 frame, then shifts by X_SHIFT; revision 3 X_SHIFT = +4, so in
that frame the lot spans x -40..32). Seen from the road, +Y is on the RIGHT.
Engine base ON (vanilla flatten): ground parts are open-bottom boxes from z 0
(GROUND_PREFIXES); every other part starts at z >= LIFT = 0.05 (cs2_bldg.GROUND_LIFT 0.02 +
LOD2 collapse headroom).

Kit adaptations (R25; each one listed in the build report):
  - glazing = cs2_bldg window_bay / curtain_wall panes 0.05 PROUD of the wall (no wall holes);
    full-glass walls get a `glass_dark` backing box (density w 0.5) so the room is closed
    and cull_hidden removes its interior faces;
  - LOD2 = cs2_bldg.lod_chain (delete + protected collapse) sampling the LOD0 atlas: no
    proxy, no reserved rect (skeptic MUST-CHANGE 5 is moot: the rect costs nothing);
  - no bay sharing (the kit has no instance()/share_signature; contract 7.3 "on for L5" is a
    kit need);
  - pool water is a painted glossy plane (`lux_water`, MaskMap smooth 0.95): cs2_submesh
    whitelists `_Wat` but lodge.py routes only `_Win` (a `_Wat` probe is an import-session
    item, not a recipe change);
  - the clock tower is IN the L5 mesh behind lodge.py's --tower/--no-tower switch (default
    on) and exposed as clock_tower() for reuse (tram base station), not a separate
    SkiClockTowerA asset;
  - swanky palette = eight recipe-local BldgMat entries registered at build time
    (register_materials; additive keys "lux_*" only - a kit need: a material registry);
  - revision 1 coplanar hygiene on kit-built parts (split_double_doors, inset_transom_ends,
    drop_faces_above, flip_dormer_cheeks, drop_loose_verts; kit needs K11-K14) and an LOD2
    list that repeats lod1_extra (lodge.delete_lists does not carry it; K15).

Every geometry contract of section 9 that can be checked on the parts is asserted in
build_l5() (a wrong build raises); C10-C15 on the joined / exported meshes are asserted by
tests/test_lodges_swanky.py.
"""

import math
import os
import sys

_HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if _HERE not in sys.path:
    sys.path.insert(0, _HERE)

from bldg_recipes import Budget, Recipe   # noqa: E402
from fine_detail import AFRAME_RACKS, FineRecipe, SignSetSpec, kit_row   # noqa: E402  (bpy-free)

# ======================================================================================
# Shared swanky helpers (used by recipes/l4_swanky_top.py too). bpy-free at import: the
# kit (cs2_bldg -> bpy) is imported lazily inside the functions.
# ======================================================================================


def _kit():
    import cs2_bldg
    return cs2_bldg


def _lin2srgb(c):
    out = []
    for a in c:
        a = max(0.0, float(a))
        out.append(round(a * 12.92 if a <= 0.0031308 else 1.055 * a ** (1.0 / 2.4) - 0.055, 4))
    return tuple(out)


# Contract section 6: linear targets = CC0 set mean x tint (lin), encoded here once.
#   siding L5  WoodSiding009 mean (0.592, 0.536, 0.352) x (0.338, 0.187, 0.193)
#   siding L4                                           x (0.236, 0.149, 0.142)
#   stone L5   Bricks096 mean (0.165, 0.146, 0.081) x (1.82, 1.16, 1.23)  (L4 grey = kit stone_rubble)
#   glulam     Planks021 mean (0.552, 0.409, 0.184) x (0.906, 0.587, 0.543)
SIDING_RED_LIN = (0.200, 0.100, 0.068)
SIDING_WALNUT_LIN = (0.140, 0.080, 0.050)
STONE_TAN_LIN = (0.300, 0.169, 0.0996)
GLULAM_LIN = (0.500, 0.240, 0.100)


def swanky_materials():
    """{key: BldgMat} of the recipe-local swanky materials (additive keys, `lux_` prefix)."""
    K = _kit()
    BM = K.BldgMat
    return {
        # 8 courses per tile at 0.175 m exposure -> 2.8 x 1.4 (contract 6; 2:1 set)
        "lux_siding_red": BM("WoodSiding009", (2.8, 1.4), _lin2srgb(SIDING_RED_LIN)),
        "lux_siding_walnut": BM("WoodSiding009", (2.8, 1.4), _lin2srgb(SIDING_WALNUT_LIN)),
        # vertical boards (clock-tower shaft): same set rotated 90 deg
        "lux_vboard_red": BM("WoodSiding009", (1.4, 2.8), _lin2srgb(SIDING_RED_LIN),
                             detail=K.BATTEN, uv_rot=90),
        "lux_stone_tan": BM("Bricks096", (2.4, 1.2), _lin2srgb(STONE_TAN_LIN), smooth=0.22),
        # honey glulam, grain along the member (kit "along" projection)
        "lux_glulam": BM("Planks021", (1.54, 1.54), _lin2srgb(GLULAM_LIN), proj="along"),
        # L4 rock band below the design terrain: Rock030 x 2.0 (lin), density w 0.55
        "lux_rock": BM("Rock030", (3.0, 3.0), None, tint=(2.0, 2.0, 2.0), density_w=0.55),
        # fire-pit ember disc (emissive, LOD0 only)
        "lux_ember": BM(None, None, (0.32, 0.14, 0.06), smooth=0.3,
                        emissive_srgb=(1.0, 0.55, 0.25)),
        # slate at the contract's roof weight 0.60 (kit slate 0.8): roofs dominate the view
        # but carry little detail (contract 7.2 weights)
        "lux_slate": BM("RoofingTiles003", (3.2, 3.2), None, density_w=0.6, proj="inplane"),
        # --- L5 revision 1 (2026-10-11, Peak 7 reference) ------------------------------
        # balcony balustrades: honey glulam colour as vertical boards with batten grooves
        # (Peak 7's orange baluster rails read as a slatted band at game distance)
        "lux_balrail": BM("WoodSiding009", (1.2, 2.4), _lin2srgb(GLULAM_LIN), detail=K.BATTEN,
                          uv_rot=90, density_w=0.6),
        # pool deck / walkway pavers (the photo's red-brown pavers): Bricks096 laid flat
        "lux_paver": BM("Bricks096", (1.6, 0.8), (0.52, 0.34, 0.28), smooth=0.20,
                        density_w=0.5),
        # pool water: painted glossy plane (MaskMap smooth 0.95); no `_Wat` route in lodge.py
        "lux_water": BM(None, None, (0.07, 0.36, 0.45), smooth=0.95, density_w=0.25),
        # pool / tub inner walls (light aqua tile)
        "lux_pooltile": BM(None, None, (0.55, 0.78, 0.82), smooth=0.60, density_w=0.4),
        # the kit "trim" (slot-1 recolour, flat) at density weight 0.5: fascia, rake, belt
        # and corner boards are flat colour whose texels carry AO only (~1,100 m2 on L5 r1)
        "lux_trim": BM(None, None, (0.80, 0.80, 0.80), smooth=0.45, slot=1, recolor=True,
                       density_w=0.5),
        # --- L5 revision 2 (2026-10-11, plaza): planter shrubs and the small conifers (flat
        # dark spruce green; no CC0 foliage set in assets/textures). Unused since revision 3
        # (vanilla trees / pots); kept as an additive key (L4 / L5 hashes do not read it)
        "lux_hedge": BM(None, None, (0.13, 0.22, 0.11), smooth=0.10, density_w=0.3),
        # --- L5 revision 3 (2026-10-11, valet drive): the drive surface, grey concrete a
        # shade darker than the kit's (0.58 tint) so the loop reads against the paver walks
        "lux_drive": BM("Concrete034", (3.0, 1.5), (0.36, 0.36, 0.37), smooth=0.18,
                        density_w=0.4),
        # --- revision 4 (2026-10-12, eighteenth look "the front triangle ... empty space you can
        # see into"): tongue-and-groove gable boarding behind the porte-cochere trusses (L5) and
        # the L4 entry porch: narrow vertical boards (0.15 m joints, the kit BATTEN is 0.30) in a
        # cedar tone a step darker than the glulam so the truss reads in front of it
        "lux_tg": BM("WoodSiding009", (1.2, 2.4), _lin2srgb((0.40, 0.19, 0.085)), uv_rot=90,
                     detail=_tg_detail(), density_w=1.0),
    }


def _tg_detail():
    """The tongue-and-groove joint grid (cs2_compose "ribs": vertical joins, 0.15 m pitch)."""
    import cs2_compose
    return cs2_compose.DetailSpec(seams_m=0.15, seam_axes="ribs", seam_depth_m=0.006,
                                  seam_width_m=0.02)


def register_materials():
    """Add the swanky materials to cs2_bldg.BLDG_MATERIALS (idempotent; raises if a key
    already exists with a different definition). Called by the builders, never at import."""
    K = _kit()
    for k, m in sorted(swanky_materials().items()):
        cur = K.BLDG_MATERIALS.get(k)
        if cur is not None and cur != m:
            raise RuntimeError(f"material {k!r} already registered differently")
        K.BLDG_MATERIALS[k] = m


def world_coords(obj):
    """World vertex coords of an unparented part (matrix_basis: matrix_world is stale until
    a depsgraph update, and the cs2_kit door / handrail parts carry loc + rot)."""
    import numpy as np
    me = obj.data
    co = np.empty(len(me.vertices) * 3, np.float32)
    me.vertices.foreach_get("co", co)
    M = np.array(obj.matrix_basis, dtype=np.float64)
    return co.reshape(-1, 3).astype(np.float64) @ M[:3, :3].T + M[:3, 3]


def parts_bounds(parts, prefixes=None, exclude=()):
    """(lo, hi) xyz over the parts whose name starts with `prefixes` (all when None)."""
    import numpy as np
    pts = []
    for o, _m in parts:
        if prefixes is not None and not o.name.startswith(tuple(prefixes)):
            continue
        if exclude and o.name.startswith(tuple(exclude)):
            continue
        W = world_coords(o)
        if len(W):
            pts.append(W)
    if not pts:
        raise RuntimeError(f"contract: no part matches {prefixes}")
    P = np.concatenate(pts)
    return P.min(0), P.max(0)


def area_centroid_x(parts, prefixes):
    """Face-area-weighted centroid x of the parts matching `prefixes`."""
    import numpy as np
    sx = sa = 0.0
    hit = False
    for o, _m in parts:
        if not o.name.startswith(tuple(prefixes)):
            continue
        hit = True
        M = np.array(o.matrix_basis)
        for p in o.data.polygons:
            c = M[:3, :3] @ np.array(p.center) + M[:3, 3]
            sx += c[0] * p.area
            sa += p.area
    if not hit:
        raise RuntimeError(f"contract: no part matches {prefixes}")
    return sx / max(sa, 1e-9)


def check(cond, msg):
    if not cond:
        raise RuntimeError(f"contract: {msg}")


def check_panes(parts, max_h, lines_of, tag):
    """C8 (kit form, R25): every `win_` part is ONE quad (cs2_bldg panes sit 0.05 proud of
    the wall); no pane taller than max_h; no pane crosses a storey line of its own mass
    (`lines_of(xc, yc)` -> storey-line z values at the pane centre: a transom at every
    storey line). Returns the pane count."""
    n = 0
    for o, _m in parts:
        if not o.name.startswith("win_"):
            continue
        me = o.data
        check(len(me.polygons) == 1 and len(me.polygons[0].vertices) == 4,
              f"{tag}: pane {o.name} is not a single quad")
        W = world_coords(o)
        z0, z1 = float(W[:, 2].min()), float(W[:, 2].max())
        check(z1 - z0 <= max_h + 1e-6, f"{tag}: pane {o.name} {z1 - z0:.2f} m > {max_h} m")
        c = W.mean(0)
        for zl in lines_of(float(c[0]), float(c[1])):
            check(not (z0 + 1e-3 < zl < z1 - 1e-3),
                  f"{tag}: pane {o.name} ({z0:.2f}..{z1:.2f}) crosses the storey line {zl}")
        n += 1
    return n


def roof_face_counts(parts):
    """C7: {roof part: face count} straight from the builders (dormers / cross gables are
    additive parts; nothing ever cuts a roof_ part)."""
    return {o.name: len(o.data.polygons) for o, _m in parts if o.name.startswith("roof_")}


# solid parts that must never stand in front of glass (buildings-review-geometry F1-F3: the
# L5 main eave ran across 12 cross-gable windows, stone piers stood in front of 3 L4 cellar
# windows and 6 L5 curtain panes). Timber trusses / brackets in front of glass are a design
# idiom (hall + xg king-post trusses) and are not listed.
PANE_BLOCKERS = ("roof_", "gutter_", "fascia_", "rake_", "pier_", "plinthcap_")
PANE_CLEAR_M = 0.6


def check_pane_clearance(parts, tag, blockers=PANE_BLOCKERS, dist=PANE_CLEAR_M,
                         grid=(0.25, 0.5, 0.75)):
    """Pane-ray contract: 9 rays per `win_` pane (inner grid points of the quad, along its
    outward normal) must not hit a `blockers` part within `dist` m. Raises on any hit and
    names the pane and the part; returns the number of panes checked. Panes are one quad
    each (check_panes), so the polygon's loop order is cyclic (bilinear corners)."""
    import numpy as np
    from mathutils import Vector
    from mathutils.bvhtree import BVHTree
    verts, polys, owner = [], [], []
    for o, _m in parts:
        if not o.name.startswith(tuple(blockers)):
            continue
        W = world_coords(o)
        base = len(verts)
        verts += [Vector(v) for v in W]
        for p in o.data.polygons:
            polys.append([base + i for i in p.vertices])
            owner.append(o.name)
    bvh = BVHTree.FromPolygons(verts, polys) if polys else None
    bad, n = [], 0
    for o, _m in parts:
        if not o.name.startswith("win_"):
            continue
        n += 1
        if bvh is None:
            continue
        W = world_coords(o)
        poly = o.data.polygons[0]
        nrm = np.array(o.matrix_basis, dtype=np.float64)[:3, :3] @ np.array(poly.normal, np.float64)
        nrm /= np.linalg.norm(nrm)
        c = [W[i] for i in poly.vertices]
        for s in grid:
            for t in grid:
                q = (1 - s) * (1 - t) * c[0] + s * (1 - t) * c[1] + s * t * c[2] + (1 - s) * t * c[3]
                loc, _nn, idx, d = bvh.ray_cast(Vector(q + nrm * 1e-3), Vector(nrm), dist)
                if loc is not None:
                    bad.append((o.name, owner[idx], round(float(d), 2)))
    check(not bad, f"{tag}: {len(bad)} pane ray(s) hit a solid part within {dist} m "
                   f"(pane, part, m): {sorted(set(bad))[:8]}")
    return n


def ring_part(name, cx, cy, z0, z1, r_out, r_in, mat, segs=16):
    """Open annulus wall (fire-pit ring): outer + inner walls + top, no bottom."""
    K = _kit()
    from mathutils import Vector

    def build(g):
        o0 = [g.v((cx + r_out * math.cos(2 * math.pi * k / segs),
                   cy + r_out * math.sin(2 * math.pi * k / segs), z0)) for k in range(segs)]
        o1 = [g.v((v.co.x, v.co.y, z1)) for v in o0]
        i0 = [g.v((cx + r_in * math.cos(2 * math.pi * k / segs),
                   cy + r_in * math.sin(2 * math.pi * k / segs), z0)) for k in range(segs)]
        i1 = [g.v((v.co.x, v.co.y, z1)) for v in i0]
        c = Vector((cx, cy, 0.5 * (z0 + z1)))
        for k in range(segs):
            j = (k + 1) % segs
            mo = (o0[k].co + o0[j].co) * 0.5
            g.face([o0[k], o0[j], o1[j], o1[k]], Vector((mo.x - c.x, mo.y - c.y, 0)))
            g.face([i0[k], i0[j], i1[j], i1[k]], Vector((c.x - mo.x, c.y - mo.y, 0)))
            g.face([o1[k], o1[j], i1[j], i1[k]], (0, 0, 1))
    return K._single(name, build, mat)


def disc_part(name, cx, cy, z, r, mat, segs=12):
    """Flat upward disc (ember bed)."""
    K = _kit()

    def build(g):
        pts = [(cx + r * math.cos(2 * math.pi * k / segs), cy + r * math.sin(2 * math.pi * k / segs), z)
               for k in range(segs)]
        g.poly(pts, (0, 0, 1))
    return K._single(name, build, mat)


def member(name, p0, p1, w, h, mat="lux_glulam", up=(0.0, 0.0, 1.0)):
    """One glulam member as its OWN part (grain per member: the kit "along" projection runs
    along a part's principal axis, so a truss built as one part would get one grain)."""
    K = _kit()
    from mathutils import Vector
    g = K._Geo()
    K._beam_into(g, Vector(p0), Vector(p1), w, h, up=Vector(up))
    return [(g.finish(name), mat)]


def curved_member(name, pts, w, h, mat="lux_glulam", up=(0.0, 0.0, 1.0)):
    """A curved brace as one part: OBB segments along a polyline (4-6 points)."""
    K = _kit()
    from mathutils import Vector
    g = K._Geo()
    for a, b in zip(pts, pts[1:]):
        K._beam_into(g, Vector(a), Vector(b), w, h, up=Vector(up))
    return [(g.finish(name), mat)]


def arc_pts(a, b, sag, n=4):
    """Points of a circular-ish arc from a to b bowed by `sag` (vector) at mid-span."""
    out = []
    for k in range(n + 1):
        t = k / n
        s = 4.0 * t * (1.0 - t)
        out.append(tuple(a[i] + (b[i] - a[i]) * t + sag[i] * s for i in range(3)))
    return out


def renamed(parts, mapping):
    """Rename kit parts (exact old -> new) raising on duplicates (no .001)."""
    K = _kit()
    for o, _m in parts:
        if o.name in mapping:
            K._rename(o, mapping[o.name])
    return parts


# --- below-grade variants (plinth mode). cs2_bldg.curtain_wall clamps z0 >= 0.08, rail()
# lifts every path point to >= 0.02, col() / umbrella() / entry() lift their base to 0.02:
# all engine-mode assumptions that would float L4's terrace (deck -2.55) furniture at grade.
# These copies keep the kit's names, prefixes and proportions and take z as given (a kit
# need, build report). ------------------------------------------------------------------

def curtain_rows(name, wall, u0, u1, zs, ncol=None, grid_u=1.6, mullion=0.08, room=None,
                 per_row_rooms=False):
    """cs2_bldg.curtain_wall without the ground clamp and with explicit row heights `zs`
    (z0, z_transom..., z1): panes `win_<room>_<name>r<r>c<c>`, `cmull_<name>`, `ctran_<name>`."""
    K = _kit()
    room = room or name
    ncol = ncol or max(1, int(round((u1 - u0) / grid_u)))
    zs = [float(z) for z in zs]
    P = []
    for r in range(len(zs) - 1):
        tag = f"{room}{r}" if per_row_rooms else room
        for c in range(ncol):
            ua = u0 + (u1 - u0) * c / ncol
            ub = u0 + (u1 - u0) * (c + 1) / ncol
            P += K.pane_quad(f"win_{tag}_{name}r{r}c{c}", wall, ua, ub, zs[r], zs[r + 1])
    face = K.PANE_OFFSET + 0.10
    mg = K._Geo()
    for c in range(ncol + 1):
        um = u0 + (u1 - u0) * c / ncol
        lo, hi = K._wall_aabb(wall, um - mullion / 2, um + mullion / 2, zs[0], zs[-1], 0.0, face)
        mg.aabb(lo, hi, K._wall_keys(wall, ("back",)))
    P.append((mg.finish(f"cmull_{name}"), "trim"))
    tg = K._Geo()
    for zt in zs:
        lo, hi = K._wall_aabb(wall, u0 - mullion / 2, u1 + mullion / 2, zt - 0.06, zt + 0.06,
                              0.0, face - 0.01)
        tg.aabb(lo, hi, K._wall_keys(wall, ("back",)))
    P.append((tg.finish(f"ctran_{name}"), "trim"))
    return P


def umbrella_at(name, x, y, z, r=1.5):
    """cs2_bldg.umbrella without the grade lift (z = deck top)."""
    K = _kit()
    P = K._single(f"umbstand_{name}", lambda g: g.cyl((x, y, z), (x, y, z + 0.08), 0.24, 8,
                                                       cap0=False), "galv")
    P += K._single(f"umbpole_{name}", lambda g: g.cyl((x, y, z + 0.08), (x, y, z + 2.5), 0.04, 6,
                                                       cap0=False), "galv")
    P += K._single(f"umbcanopy_{name}", lambda g: g.cyl((x, y, z + 2.18), (x, y, z + 2.80), r, 8,
                                                         cap0=True, r1=0.06), "accent")
    return P


def post_lantern(name, x, y, z0, h=3.2, size=0.12):
    """Lantern post from z0 (no grade lift): `col_<name>` + cs2_bldg.lantern(kind post)."""
    K = _kit()
    P = K.box_part(f"col_{name}", (x - size / 2, y - size / 2, z0), (x + size / 2, y + size / 2, z0 + h),
                   "trim", drop=("-Z",))
    lp, _pt = K.lantern(name, (x, y, z0 + h), "+X", kind="post")
    return P + lp


def glass_balustrade(name, path, h=1.10, post_every=1.5, panel=(0.10, 1.05)):
    """Glass balustrade along a floor-level polyline (no grade lift): slot-1 `post_<name>`
    posts every <= post_every, `rail_<name>` top rail, opaque `glassrail_<name>` panels
    (glass_dark, 0.02 thick, z + panel[0] .. z + panel[1]; opaque per the kit's glass rail,
    no alpha cutout) - contract 4.2 T balustrade with the kit's glass rule."""
    K = _kit()
    from mathutils import Vector
    pts = [Vector(p) for p in path]
    posts, rails, glass = K._Geo(), K._Geo(), K._Geo()
    up = Vector((0, 0, 1))
    for i, (a, b) in enumerate(zip(pts, pts[1:])):
        dv = b - a
        L = dv.length
        d = dv / L
        side = Vector((d.y, -d.x, 0.0))
        n = max(1, int(math.ceil(L / post_every - 1e-9)))
        for j in range(n + (1 if i == len(pts) - 2 else 0)):
            q = a + dv * (j / n)
            posts.aabb((q.x - 0.035, q.y - 0.035, q.z), (q.x + 0.035, q.y + 0.035, q.z + h))
        c = (a + b) * 0.5 + up * (h - 0.025)
        rails.obb(c, (d, side, up), (L / 2 + 0.04, 0.035, 0.025))
        for j in range(n):
            qa = a + dv * (j / n) + d * 0.05
            qb = a + dv * ((j + 1) / n) - d * 0.05
            cc = (qa + qb) * 0.5 + up * (0.5 * (panel[0] + panel[1]))
            glass.obb(cc, (d, side, up), (0.5 * (qb - qa).length, 0.01, 0.5 * (panel[1] - panel[0])))
    return [(posts.finish(f"post_{name}"), "trim"), (rails.finish(f"rail_{name}"), "trim"),
            (glass.finish(f"glassrail_{name}"), "glass_dark")]


def handrail_at(name, path, h=1.10):
    """cs2_kit.handrail straight (no grade lift): post_ / rail_ / kick_ parts in slot-1 trim."""
    import cs2_kit
    return [(o, "trim") for o, _q in cs2_kit.handrail(name, [tuple(p) for p in path], h=h)]


def prism_xz(name, profile, y0, y1, mat, caps=(True, True)):
    """Prism: an XZ polygon `profile` [(x, z), ...] extruded from y0 to y1; caps = (face at
    y0, face at y1) - drop the cap that faces a closed hollow (walls are only contact-culled)."""
    K = _kit()

    def build(g):
        g.prism([(x, y0, z) for x, z in profile], (0.0, y1 - y0, 0.0), caps=caps)
    return K._single(name, build, mat)


def prism_yz(name, profile, x0, x1, mat, caps=(True, True)):
    """Prism: a YZ polygon `profile` [(y, z), ...] extruded from x0 to x1; caps = (face at
    x0, face at x1)."""
    K = _kit()

    def build(g):
        g.prism([(x0, y, z) for y, z in profile], (x1 - x0, 0.0, 0.0), caps=caps)
    return K._single(name, build, mat)


def pier_box(name, lo, hi, mat, drop=(), cap=0.15):
    """Stone pier (`pier_<name>`) with chosen buried faces dropped + `plinthcap_<name>`."""
    K = _kit()
    P = K.box_part(f"pier_{name}", lo, hi, mat, drop=tuple(drop) + (("+Z",) if cap else ()))
    if cap:
        P += K.box_part(f"plinthcap_{name}", (lo[0] - 0.05, lo[1] - 0.05, hi[2]),
                        (hi[0] + 0.05, hi[1] + 0.05, hi[2] + cap), "concrete")
    return P


def unbevelled(parts, old, new):
    """Rename `old`-prefixed parts to `new` + the rest of the name. Used for MULTI-member
    glulam parts (kit trusses, post rows): cs2_bldg.bevel_parts bevels truss_/col_/beam_ and
    retile then maps every chamfer / corner face of a multi-axis part as its own UV island
    (L5: 1,043 truss islands of 3,845; bevel corner triangles 0.0008 m2 sat 1.3x above
    cs2_uv's zero-island limit after the pack). `trim_` keeps the LOD semantics (LOD1 keep,
    LOD2 delete) and the material weight; single members keep their bevel."""
    K = _kit()
    for o, _m in parts:
        if o.name.startswith(old):
            K._rename(o, new + o.name[len(old):])
    return parts


def strip_tiny_faces(parts, names, max_area=2e-3):
    """Delete faces smaller than max_area from the matching parts (snow-guard bar end caps:
    0.0003 m2 6-gon fans, invisible, but below cs2_uv's zero-area island limit after a
    ~3,800-island pack - the L5 dry pack raised on them)."""
    import bmesh
    n = 0
    for o, _m in parts:
        if o.name not in names:
            continue
        bm = bmesh.new()
        bm.from_mesh(o.data)
        dead = [f for f in bm.faces if f.calc_area() < max_area]
        if dead and len(dead) < len(bm.faces):
            bmesh.ops.delete(bm, geom=dead, context="FACES")
            n += len(dead)
            bm.to_mesh(o.data)
            o.data.update()
        bm.free()
    return n


def retag_rooms(parts, fn):
    """Rename every pane `win_<room>_<rest>` to `win_<fn(room)>_<rest>` and return the sorted
    room tags. cs2_bldg.split_rooms tiles each ROOM into its id's cell separately, so two
    rooms that share an id (assign_rooms reuses ids beyond 3 m once there are more rooms
    than the 23-id pool) land on the same cell texels: the L5 export with 61 rooms failed
    cs2_validate's `_Win` overlap check (60.6 % of the pane texels). <= 23 tags -> every tag
    gets its own id and the panes of a tag are tiled without overlap (C13)."""
    K = _kit()
    tags = set()
    for o, _m in parts:
        if not o.name.startswith("win_"):
            continue
        room, rest = o.name[4:].split("_", 1)
        new = fn(room)
        if not new.isalnum():
            raise ValueError(f"room tag {new!r} must be alphanumeric")
        tags.add(new)
        if new != room:
            K._rename(o, f"win_{new}_{rest}")
    tags = sorted(tags)
    check(len(tags) <= len(K.WIN_POOL), f"C13 {len(tags)} room tags > {len(K.WIN_POOL)} ids "
                                          f"(ids would be reused and the _Win cells overlap)")
    return tags


def corner_boards(name, corners, z0, z1, mat="trim", s=0.18):
    """`trim_<name>` corner boards: [(x, y), ...] building corners, boards s x s."""
    K = _kit()
    return K.boxes_part(f"trim_{name}", [((x - s / 2, y - s / 2, z0), (x + s / 2, y + s / 2, z1))
                                         for x, y in corners], mat)


# --- coplanar hygiene (L5 revision 1, 2026-10-11). test_lodges_swanky's coplanar contract
# (no two parts with same-facing overlapping faces on the shipped LOD0) found 37 pairs on the
# first revision-1 build, all made by kit builders, none removable by cull_hidden (they are
# partial overlaps, not duplicates). These helpers fix them recipe-side; each one is a kit
# need in build/lodges-swanky.md "Revision 1". New helpers only: the shared ones L4 calls
# are unchanged (its shipped mesh hash must not move). ----------------------------------

def _drop_parts(parts, names):
    """Remove the parts named in `names` from the list `parts` (in place) and delete their
    objects and meshes. Returns the number removed."""
    import bpy
    keep, n = [], 0
    for o, m in parts:
        if o.name in names:
            me = o.data
            bpy.data.objects.remove(o, do_unlink=True)
            bpy.data.meshes.remove(me)
            n += 1
        else:
            keep.append((o, m))
    parts[:] = keep
    return n


def _identity(o):
    M = o.matrix_basis
    return all(abs(M[i][j] - (1.0 if i == j else 0.0)) < 1e-9 for i in range(4) for j in range(4))


def split_double_doors(parts):
    """cs2_bldg.entry builds a two-leaf door as two cs2_kit.door leaves whose meeting jambs
    (`door_<n>0_jamb1`, `door_<n>1_jamb0`) overlap by 2 cm and run up through the other
    leaf's head: same-facing coplanar faces. Drop both meeting jambs, so the leaves meet on
    a 2 cm reveal. Returns the number of parts removed."""
    names = {o.name for o, _m in parts}
    dead = set()
    for nm in sorted(names):
        if nm.startswith("door_") and nm.endswith("0_jamb1"):
            twin = nm[:-len("0_jamb1")] + "1_jamb0"
            if twin in names:
                dead |= {nm, twin}
    return _drop_parts(parts, dead)


def inset_transom_ends(parts, inset=0.005):
    """cs2_bldg.curtain_wall (and the hall-gable grid) ends every transom box exactly on the
    outer face of the end mullion: same-facing coplanar end caps. Pull both ends of every
    `ctran_` part in along its wall by `inset`, so the caps sit inside the end mullions.
    The wall's along-axis is the part's longer horizontal extent. Returns the count."""
    import numpy as np
    n = 0
    for o, _m in parts:
        if not o.name.startswith("ctran_"):
            continue
        check(_identity(o), f"{o.name}: transom part has a transform")
        me = o.data
        co = np.empty(len(me.vertices) * 3, np.float32)
        me.vertices.foreach_get("co", co)
        co = co.reshape(-1, 3).astype(np.float64)
        ext = co.max(0) - co.min(0)
        ax = 0 if ext[0] >= ext[1] else 1
        lo, hi = float(co[:, ax].min()), float(co[:, ax].max())
        check(hi - lo > 4 * inset, f"{o.name}: transom shorter than the inset")
        co[np.abs(co[:, ax] - lo) < 1e-4, ax] += inset
        co[np.abs(co[:, ax] - hi) < 1e-4, ax] -= inset
        me.vertices.foreach_set("co", co.astype(np.float32).ravel())
        me.update()
        n += 1
    return n


def drop_faces_above(parts, name, z):
    """Delete the faces of part `name` whose vertices all lie at z >= `z` (one transom box
    of a kit curtain_wall: the hall's lower curtains and the upper one each build a transom
    at 4.5, the same box twice). Returns the number of faces deleted."""
    import bmesh
    for o, _m in parts:
        if o.name != name:
            continue
        check(_identity(o), f"{name}: part has a transform")
        bm = bmesh.new()
        bm.from_mesh(o.data)
        dead = [f for f in bm.faces if all(v.co.z >= z - 1e-4 for v in f.verts)]
        check(dead and len(dead) < len(bm.faces), f"{name}: no faces above {z} to drop")
        bmesh.ops.delete(bm, geom=dead, context="FACES")
        bm.to_mesh(o.data)
        bm.free()
        o.data.update()
        return len(dead)
    raise RuntimeError(f"contract: part {name} missing")


def turn_posts(parts, prefix, deg=30.0):
    """cs2_kit.handrail builds its posts as 6-gons with fixed flats facing +-X (and +-60 deg)
    and its rails as 6-gon tubes framed on the run; a rail running along Y (the revision-3
    deck stair) then has side faces facing +-X that lie on the posts' +-X flats inside every
    post (14 same-facing coplanar pairs on the first r3 build; the revision-2 stair ran along
    X and had none). Turn each `prefix` post's mesh by `deg` about its own axis (the object
    origin; the kit posts carry loc on the object), so no flat faces +-X. Returns the count.
    Kit need K21 (handrail posts oriented to the run)."""
    from mathutils import Matrix
    R = Matrix.Rotation(math.radians(deg), 4, "Z")
    n = 0
    for o, _m in parts:
        if o.name.startswith(prefix):
            check(abs(o.rotation_euler.z) < 1e-9, f"{o.name}: post already turned")
            o.data.transform(R)
            o.data.update()
            n += 1
    return n


def drop_loose_verts(parts):
    """Delete vertices no face uses from every part (a box_part with all faces at a corner
    dropped keeps that corner; cs2_bldg.split_rooms then copies such verts into every room
    of `_Win`: the first r1 export had 42 at the glass backing's z-0 back corners, so the
    `_Win` bounds reached grade). Returns the number of vertices deleted."""
    import bmesh
    n = 0
    for o, _m in parts:
        me = o.data
        if not me.vertices:
            continue
        bm = bmesh.new()
        bm.from_mesh(me)
        dead = [v for v in bm.verts if not v.link_faces]
        if dead:
            bmesh.ops.delete(bm, geom=dead, context="VERTS")
            bm.to_mesh(me)
            me.update()
            n += len(dead)
        bm.free()
    return n


def flip_dormer_cheeks(parts, name, side, t=0.12):
    """cs2_bldg.dormer extrudes the two cheek prisms INTO the dormer on the -X and +Y roof
    sides (its thickness sign is flipped twice there), so each cheek's outer face lies on
    the front box's side face: same-facing coplanar. Move both cheeks of `dormer_<name>`
    out by their thickness t, as the kit builds them on the +X and -Y sides."""
    if side not in ("-X", "+Y"):
        return 0
    import numpy as np
    ax = 1 if side[1] == "X" else 0               # the along-wall axis
    front = [o for o, _m in parts if o.name == f"dormer_{name}_front"]
    check(len(front) == 1, f"dormer {name}: front part missing")
    centre = 0.5 * (world_coords(front[0])[:, ax].min() + world_coords(front[0])[:, ax].max())
    n = 0
    for o, _m in parts:
        if o.name not in (f"dormer_{name}_cheek0", f"dormer_{name}_cheek1"):
            continue
        check(_identity(o), f"{o.name}: cheek part has a transform")
        me = o.data
        co = np.empty(len(me.vertices) * 3, np.float32)
        me.vertices.foreach_get("co", co)
        co = co.reshape(-1, 3).astype(np.float64)
        sg = 1.0 if co[:, ax].mean() > centre else -1.0
        co[:, ax] += sg * t
        me.vertices.foreach_set("co", co.astype(np.float32).ravel())
        me.update()
        n += 1
    check(n == 2, f"dormer {name}: {n} cheeks moved, want 2")
    return n


# ======================================================================================
# Revision 4 shared helpers (2026-10-12, verdicts-2026-10-11.md "Eighteenth look"): REAL door
# openings (cut_recess + terrace_door + check_door), the clear-box and see-through ray
# contracts, the preview cameras and true close-up previews. recipes/l4_swanky_top.py (its
# revision 1) uses them too. bpy-free at import (Blender modules are imported in the bodies).
# ======================================================================================

DOOR_W, DOOR_H = 1.80, 2.30        # clear opening w x h (the gondola follow-up's door standard)
DOOR_SET = 0.20                    # the recess: the door plane this far behind the wall face
DOOR_FRAME = 0.10                  # fixed jambs / head, outside the clear opening
DOOR_FRAME_D = 0.14                # frame depth, from the glass backing toward the wall face
DOOR_STILE, DOOR_TOPRAIL, DOOR_BOTRAIL = 0.10, 0.12, 0.26
DOOR_LEAF_T = 0.05                 # leaf stiles / rails proud of the glass backing
DOOR_LITE_D = 0.03                 # the lite (a `win_` pane) proud of the glass backing
DOOR_KICK = (0.03, 0.23)           # kick plate ("door plate") band above the threshold
DOOR_PULL = (0.80, 1.40)           # vertical pull bar band
DOOR_SILL_T = 0.012                # metal threshold strip on the recess floor
DOOR_REAL_MIN = DOOR_SET - 0.08    # D1: the door's plane lies at least this far behind the face
DOOR_CLEAR_OUT = 1.50              # D5: the clear box runs this far out in front of the face
DOOR_PARTS = ("door_", "doorsill_", "glassback_")


def _uniq(seq):
    """Order-preserving de-duplication (determinism: a set is only used for membership)."""
    seen, out = set(), []
    for x in seq:
        if x not in seen:
            seen.add(x)
            out.append(x)
    return out


def _side_axes(side):
    """(normal axis 0 = X / 1 = Y, outward sign, along-wall axis) of a wall side."""
    ax = 0 if side[1] == "X" else 1
    return ax, (1.0 if side[0] == "+" else -1.0), 1 - ax


def cut_recess(obj, side, plane, a0, a1, z0, z1, depth):
    """Cut a REAL opening into the host part `obj` (identity transform): its faces on the wall
    (side, plane) are bisected at the world along-coordinates a0 / a1 and the heights z0 / z1,
    the rectangle is extruded `depth` m INTO the building (bmesh extrude_face_region; the
    flipped original left on the wall plane is deleted), so its four side faces become the
    reveal - jambs facing the opening, the head facing down, the sill (the threshold) facing up
    at z0 - and the moved rectangle the recess back. The wall stays closed and welded (no hole
    into the hollow storey box; the LOD collapse cannot open a crack). Raises unless the
    rectangle lies wholly on that wall (area check). Returns the number of faces added."""
    import bmesh
    from mathutils import Vector
    check(_identity(obj), f"{obj.name}: host part has a transform")
    check(a1 > a0 and z1 > z0 and depth > 0.0, f"{obj.name}: empty recess")
    ax, sg, al = _side_axes(side)
    n = Vector((0.0, 0.0, 0.0))
    n[ax] = sg
    bm = bmesh.new()
    bm.from_mesh(obj.data)

    def on_wall():
        out = []
        for f in bm.faces:
            f.normal_update()
            if f.normal.dot(n) > 0.999 and all(abs(v.co[ax] - plane) < 1e-4 for v in f.verts):
                out.append(f)
        return out

    def inside(f):
        c = f.calc_center_median()
        return a0 + 1e-5 < c[al] < a1 - 1e-5 and z0 + 1e-5 < c.z < z1 - 1e-5
    def spans(f, axis, lo, hi):
        vs = [v.co[axis] for v in f.verts]
        return min(vs) < hi - 1e-5 and max(vs) > lo + 1e-5
    nf0 = len(bm.faces)
    # the cuts stay local (the LOD chain protects ground parts from collapse, so every split of a
    # ground wall ships at LOD2): the two vertical cuts only through the wall faces the opening
    # overlaps, the two horizontal ones only through the faces between those vertical cuts
    for axis, val in ((al, a0), (al, a1), (2, z0), (2, z1)):
        if axis == al:
            fs = [f for f in on_wall() if spans(f, al, a0, a1) and spans(f, 2, z0, z1)]
        else:
            fs = [f for f in on_wall() if spans(f, 2, z0, z1)
                  and a0 - 1e-5 <= min(v.co[al] for v in f.verts)
                  and max(v.co[al] for v in f.verts) <= a1 + 1e-5]
        check(fs, f"{obj.name}: no face of its {side} wall at {plane} under the opening")
        co, no = Vector((0.0, 0.0, 0.0)), Vector((0.0, 0.0, 0.0))
        co[axis], no[axis] = val, 1.0
        geom = fs + _uniq([e for f in fs for e in f.edges]) + _uniq([v for f in fs for v in f.verts])
        bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-6, plane_co=co, plane_no=no)
    region = [f for f in on_wall() if inside(f)]
    area = sum(f.calc_area() for f in region)
    want = (a1 - a0) * (z1 - z0)
    check(region and abs(area - want) < 1e-3 * max(1.0, want),
          f"{obj.name}: the opening {a0:.2f}..{a1:.2f} x {z0:.2f}..{z1:.2f} is not wholly on its "
          f"{side} wall ({area:.3f} of {want:.3f} m2)")
    res = bmesh.ops.extrude_face_region(bm, geom=region)
    newv = [e for e in res["geom"] if isinstance(e, bmesh.types.BMVert)]
    bmesh.ops.translate(bm, verts=newv, vec=-n * depth)
    left = []
    for f in bm.faces:
        c = f.calc_center_median()
        if abs(c[ax] - plane) < 1e-4 and inside(f):
            left.append(f)
    bmesh.ops.delete(bm, geom=left, context="FACES_ONLY")
    # the reveal faces face the opening; the back faces out (asserted, flipped if not)
    ac, zc = 0.5 * (a0 + a1), 0.5 * (z0 + z1)
    lo_d, hi_d = sorted((plane, plane - sg * depth))
    for f in bm.faces:
        f.normal_update()
        c = f.calc_center_median()
        if not (lo_d - 1e-4 <= c[ax] <= hi_d + 1e-4 and a0 - 1e-4 <= c[al] <= a1 + 1e-4
                and z0 - 1e-4 <= c.z <= z1 + 1e-4):
            continue
        if abs(f.normal[ax]) > 0.9:
            want_n = n
        elif abs(f.normal[al]) > 0.9:
            want_n = Vector((0.0, 0.0, 0.0))
            want_n[al] = 1.0 if c[al] < ac else -1.0
        else:
            want_n = Vector((0.0, 0.0, 1.0 if c.z < zc else -1.0))
        if f.normal.dot(want_n) < 0.0:
            f.normal_flip()
    bad = [e for e in bm.edges if len(e.link_faces) > 2]
    check(not bad, f"{obj.name}: the recess left {len(bad)} non-manifold edge(s)")
    added = len(bm.faces) - nf0
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    return added


def terrace_door(tag, side, plane, s, z0, room, *, w=DOOR_W, h=DOOR_H, set_back=DOOR_SET,
                 frame=DOOR_FRAME, frame_sides=True, frame_mat="lux_glulam", leaf_mat="trim",
                 metal="galv"):
    """French terrace door for a REAL opening (the caller cuts it into the host with
    cut_recess(host, side, plane, *door["rough_a"], *door["rough_z"], set_back)): centred on
    the world along-coordinate `s` of the wall (side, plane), threshold at z0 (the outside
    surface), clear opening w x h. Parts: `glassback_<tag>` the dark glass backing 0.01 in
    front of the recess back (kept at every LOD: the recess never shows the stone / hollow);
    `doorsill_<tag>` a metal threshold strip on the recess floor; `door_<tag>_frame` the fixed
    jambs (frame_sides; L4 uses its glulam posts instead) and head outside the clear opening;
    per leaf k (two leaves meeting on the centre line) `door_<tag>_leaf<k>` stiles + rails, a
    lite `win_<room>_<tag>l<k>` (one pane: it glows with the room at night), the kick plate
    `door_<tag>_kick<k>` and a pull bar on stand-offs `door_<tag>_pull<k>`. Returns (parts,
    door) - `door` holds what cut_recess and check_door need."""
    K = _kit()
    from cs2_bldg import WallRef
    wall = WallRef(side, plane)
    uc = s if side in ("+X", "-Y") else -s
    db = -set_back + 0.01                          # the glass backing plane (outward d)
    fw = frame if frame_sides else 0.0
    u0, u1 = uc - w / 2, uc + w / 2
    zt = z0 + h
    P = K.wall_boxes(f"glassback_{tag}", wall, [(u0 - fw, u1 + fw, z0 + DOOR_SILL_T, zt + frame,
                                                  db - 0.002, db)], "glass_dark",
                     drop=("back", "left", "right", "top", "bottom"))
    P += K.wall_boxes(f"doorsill_{tag}", wall, [(u0 - fw, u1 + fw, z0, z0 + DOOR_SILL_T,
                                                  -set_back, 0.0)], metal, drop=("bottom", "back"))
    fr = [(u0, u1, zt, zt + frame, db, db + DOOR_FRAME_D)]
    if frame_sides:
        fr += [(u0 - frame, u0, z0 + DOOR_SILL_T, zt + frame, db, db + DOOR_FRAME_D),
               (u1, u1 + frame, z0 + DOOR_SILL_T, zt + frame, db, db + DOOR_FRAME_D)]
    P += K.wall_boxes(f"door_{tag}_frame", wall, fr, frame_mat)
    lz0 = z0 + DOOR_SILL_T + 0.005
    for k, (la, lb) in enumerate(((u0, uc), (uc, u1))):
        bx = [(la, la + DOOR_STILE, lz0, zt, db, db + DOOR_LEAF_T),
              (lb - DOOR_STILE, lb, lz0, zt, db, db + DOOR_LEAF_T),
              (la + DOOR_STILE, lb - DOOR_STILE, zt - DOOR_TOPRAIL, zt, db, db + DOOR_LEAF_T),
              (la + DOOR_STILE, lb - DOOR_STILE, lz0, lz0 + DOOR_BOTRAIL, db, db + DOOR_LEAF_T)]
        P += K.wall_boxes(f"door_{tag}_leaf{k}", wall, bx, leaf_mat)
        P += K.pane_quad(f"win_{room}_{tag}l{k}", wall, la + DOOR_STILE, lb - DOOR_STILE,
                         lz0 + DOOR_BOTRAIL, zt - DOOR_TOPRAIL, d=db + DOOR_LITE_D)
        P += K.wall_boxes(f"door_{tag}_kick{k}", wall,
                          [(la + DOOR_STILE * 0.5, lb - DOOR_STILE * 0.5, z0 + DOOR_KICK[0],
                            z0 + DOOR_KICK[1], db + DOOR_LEAF_T, db + DOOR_LEAF_T + 0.004)], metal)
        um = lb - 0.06 if k == 0 else la + 0.06    # beside the meeting stiles
        pull = [(um - 0.015, um + 0.015, z0 + DOOR_PULL[0], z0 + DOOR_PULL[1],
                 db + DOOR_LEAF_T + 0.035, db + DOOR_LEAF_T + 0.065)]
        for zs in (z0 + DOOR_PULL[0] + 0.06, z0 + DOOR_PULL[1] - 0.08):
            pull.append((um - 0.01, um + 0.01, zs, zs + 0.02, db + DOOR_LEAF_T, db + DOOR_LEAF_T + 0.035))
        P += K.wall_boxes(f"door_{tag}_pull{k}", wall, pull, metal)
    ra = sorted(((u0 - fw) if side in ("+X", "-Y") else -(u0 - fw),
                 (u1 + fw) if side in ("+X", "-Y") else -(u1 + fw)))
    door = dict(tag=tag, side=side, plane=plane, s=s, z0=z0, w=w, h=h, set_back=set_back,
                room=room, rough_a=tuple(ra), rough_z=(z0, zt + frame), frame_sides=frame_sides)
    return P, door


def _bvh_of(parts, skip=()):
    """(BVHTree, owner per polygon) of the parts' world faces (parts named in `skip` left out)."""
    from mathutils import Vector
    from mathutils.bvhtree import BVHTree
    verts, polys, owner = [], [], []
    for o, _m in parts:
        if o.name in skip:
            continue
        W = world_coords(o)
        base = len(verts)
        verts += [Vector(v) for v in W]
        for p in o.data.polygons:
            polys.append([base + i for i in p.vertices])
            owner.append(o.name)
    return BVHTree.FromPolygons(verts, polys), owner


def check_door(parts, door, host, grid=(9, 7)):
    """Revision-4 door contract (raises naming the door and the part):
      D1 REAL opening: rays from 0.5 m outside, along the inward normal, over a grid of the clear
         opening (inset 0.02 m) meet no part of the host wall in front of the recess back and
         reach the door (glass backing, leaves, lites) no nearer than DOOR_REAL_MIN behind the
         face (pulls / kick plates may stand proud of the leaves);
      D2 clear opening w x h: the first hit of every such ray is a movable or glazing part of the
         door (leaf, lite, kick plate, pull, backing) - never a fixed frame, transom, mullion,
         post or rail (the frame stands outside the clear opening);
      D3 threshold on the terrace surface: the recess floor (the metal strip) at z0 +- 0.02 and the
         surface 0.3 m outside the door at z0 +- 0.02, both facing up.
    Returns a dict for the report."""
    from mathutils import Vector
    K = _kit()
    from cs2_bldg import WallRef
    side, plane, s, z0, w, h = (door[k] for k in ("side", "plane", "s", "z0", "w", "h"))
    tag, sb = door["tag"], door["set_back"]
    wall = WallRef(side, plane)
    uc = s if side in ("+X", "-Y") else -s
    n = Vector(K._SIDES[side][0])
    bvh, owner = _bvh_of(parts)
    own = (f"door_{tag}_leaf", f"door_{tag}_kick", f"door_{tag}_pull", f"glassback_{tag}")
    lite = f"_{tag}l"
    nu, nz = grid
    bad, depths, hits = [], [], 0
    for i in range(nu):
        u = uc - w / 2 + 0.02 + (w - 0.04) * i / (nu - 1)
        for j in range(nz):
            z = z0 + 0.02 + (h - 0.04) * j / (nz - 1)
            o = K.wall_point(wall, u, z, 0.5)
            loc, _nn, idx, d = bvh.ray_cast(o, -n, 0.5 + sb + 0.3)
            if loc is None:
                bad.append(("D1 nothing behind the opening (see-through)", round(u, 2), round(z, 2)))
                continue
            depth = d - 0.5
            nm = owner[idx]
            hits += 1
            if nm == host and depth < sb - 1e-3:
                bad.append(("D1 the host wall stands in the opening", nm, round(u, 2), round(z, 2),
                            round(depth, 3)))
            elif not (nm.startswith(own) or (nm.startswith("win_") and lite in nm)):
                bad.append(("D2 a fixed part in the clear opening", nm, round(u, 2), round(z, 2),
                            round(depth, 3)))
            elif not nm.startswith((f"door_{tag}_kick", f"door_{tag}_pull")) and depth < DOOR_REAL_MIN:
                bad.append(("D1 the door is not recessed (a plate on the wall?)", nm, round(depth, 3)))
            else:
                depths.append(depth)
    down = Vector((0.0, 0.0, -1.0))
    for d_out, tol, what in ((-sb * 0.5, 0.02, "recess floor"), (0.30, 0.02, "outside surface")):
        o = K.wall_point(wall, uc, z0 + 0.6, d_out)
        loc, nn, idx, _d = bvh.ray_cast(o, down, 1.2)
        if loc is None or abs(loc.z - z0) > tol or nn.z < 0.9:
            bad.append(("D3 threshold", what, None if loc is None else (round(loc.z, 3), owner[idx])))
    check(not bad, f"door {tag}: {len(bad)} violation(s): {bad[:6]}")
    return dict(rays=nu * nz, door_depth_min=round(min(depths), 3) if depths else None,
                clear=(w, h), threshold=z0)


def clear_box(door, out=DOOR_CLEAR_OUT):
    """The door's clear box (world AABB): the clear opening from its recess back out to `out` m
    in front of the wall face."""
    K = _kit()
    from cs2_bldg import WallRef
    side, plane, s, z0 = door["side"], door["plane"], door["s"], door["z0"]
    uc = s if side in ("+X", "-Y") else -s
    lo, hi = K._wall_aabb(WallRef(side, plane), uc - door["w"] / 2, uc + door["w"] / 2, z0,
                          z0 + door["h"], -door["set_back"], out)
    return tuple(lo), tuple(hi)


def check_clear_boxes(parts, boxes, prefixes=("gutter_", "downspout_")):
    """D5 / E1: no `prefixes` part (gutters, downspouts) meets any clear box [(label, (lo, hi))]
    (triangle overlap or a vertex inside). Raises naming the box and the part."""
    import numpy as np
    from mathutils import Vector
    from mathutils.bvhtree import BVHTree
    sel = [(o, m) for o, m in parts if o.name.startswith(prefixes)]
    bad = []
    if sel:
        bvh, owner = _bvh_of(sel)
        for label, (lo, hi) in boxes:
            V, F = _box_poly(lo, hi)
            bb = BVHTree.FromPolygons([Vector(v) for v in V], [list(f) for f in F])
            names = {owner[a] for a, _b in bvh.overlap(bb)}
            for o, _m in sel:
                W = world_coords(o)
                if ((W > np.array(lo)) & (W < np.array(hi))).all(axis=1).any():
                    names.add(o.name)
            if names:
                bad.append((label, sorted(names)[:4]))
    check(not bad, f"E1 gutter / downspout in a door's clear box: {bad}")
    return len(boxes)


def preview_cameras(parts, views, shift=0.0):
    """{view: camera position (authoring frame of `parts`)} of cs2_preview's framing
    (lift_tower.frame_camera: bounding sphere of the whole build, 45 deg lens, 1300 x 1000,
    margin 1.12) for preview-frame directions (bx, by, bz) -> authoring (-by, bx, bz) (the
    joined mesh is orient_for_net'ed (x, y) -> (y, -x)). Pre-shift parts give pre-shift
    cameras (the build only translates)."""
    import numpy as np
    lo, hi = parts_bounds(parts)
    centre, radius = (lo + hi) / 2.0, float(np.linalg.norm(hi - lo)) / 2.0
    fy = math.radians(45.0)
    fx = 2.0 * math.atan(math.tan(fy / 2.0) * 1300 / 1000)
    dist = radius / math.tan(min(fx, fy) / 2.0) * 1.12
    out = {}
    for name, (bx, by, bz) in views:
        d = np.array([-by, bx, bz], np.float64)
        out[name] = tuple(centre + d / np.linalg.norm(d) * dist)
    return out


def check_gable_closed(parts, cams, x_face, ys, zs_of, depth_max, tag="G1"):
    """See-through contract on a gable opening facing +X: for every camera in `cams` ({name:
    point}) in front of the plane x = x_face and every sample (x_face, y, z) for y in `ys`, z in
    zs_of(y), the first face the camera ray meets is FRONT-facing (a back face is invisible in
    game: the eye would see into the void) and lies no deeper than `depth_max` behind the plane.
    Raises naming the camera, the sample and the part; returns {camera: rays}."""
    from mathutils import Vector
    bvh, owner = _bvh_of(parts)
    out, bad = {}, []
    for cam_name in sorted(cams):
        c = Vector(cams[cam_name])
        if c.x <= x_face + 0.05:
            continue
        k = 0
        for y in ys:
            for z in zs_of(y):
                p = Vector((x_face, y, z))
                d = (p - c).normalized()
                loc, nn, idx, _dd = bvh.ray_cast(c, d, (p - c).length + 50.0)
                k += 1
                if loc is None:
                    bad.append((cam_name, (round(y, 2), round(z, 2)), "sky"))
                elif nn.dot(d) > 0.0:
                    bad.append((cam_name, (round(y, 2), round(z, 2)), owner[idx], "back face"))
                elif loc.x < x_face - depth_max:
                    bad.append((cam_name, (round(y, 2), round(z, 2)), owner[idx],
                                f"{x_face - loc.x:.2f} m deep"))
        out[cam_name] = k
    check(not bad, f"{tag} the gable is open: {len(bad)} ray(s) see into it, e.g. {bad[:5]}")
    return out


def _vanilla_mount(vanilla, pf):
    return vanilla[pf].get("mount", "ground")


def closeup_previews(recipe, shots, out_dir=None, ground=True, modes=("pbr", "dusk"),
                     name=None):
    """True close-ups of the EXPORTED asset (cs2_preview's rig, lens, sky, PBR material and tint;
    the `_Win` panes as dark-glass stand-ins): `<name>_closeup_<shot>_<mode>.png`. shots =
    ((shot, target, eye), ...) in the shipped authoring frame (+X = road, z up). The import is
    re-oriented to cs2_preview's frame (authoring (x, y, z) -> (y, -x, z)) whichever way the
    FBX importer left it."""
    import bpy
    import mathutils
    import numpy as np
    import cs2_preview as CP
    import lodge
    name = name or lodge.asset_name(recipe, recipe.letter)
    asset_dir = os.path.join(lodge.IMPORT_ROOT, recipe.category, recipe.letter, name)
    out_dir = out_dir or os.path.join(lodge.PREVIEW_ROOT, recipe.category)
    os.makedirs(out_dir, exist_ok=True)
    with open(os.path.join(asset_dir, f"{name}.manifest.json"), encoding="utf-8") as fh:
        ext = json_load(fh)["extent_game_m"]
    lodge.clear_scene()

    def load(path):
        o = CP.import_generation(path)
        W = np.array([v.co[:] for v in o.data.vertices])
        # cs2_preview frame: z up with z_min = the game min_y; else the importer left it y up
        if abs(float(W[:, 2].min()) - ext["min_y"]) > 0.05 or \
                abs(float(W[:, 2].max() - W[:, 2].min()) - ext["y"]) > 0.05:
            o.data.transform(mathutils.Matrix.Rotation(math.radians(90.0), 4, "X"))
            o.data.update()
        return o
    obj = load(os.path.join(asset_dir, f"{name}.fbx"))
    obj.name = name + "Closeup"
    win = None
    if os.path.exists(os.path.join(asset_dir, f"{name}_Win.fbx")):
        win = load(os.path.join(asset_dir, f"{name}_Win.fbx"))
    tint = {"RGB"[k - 1]: tuple(v) for k, v in recipe.slots.items()}
    rig = CP._Rig(obj, None)
    written = []
    try:
        cam = CP._make_camera(rig)
        sc = rig.scene
        if win is not None:
            rig.track(win)
            gm = rig.track_data(bpy.data.materials, bpy.data.materials.new("CloseupGlass"))
            gm.use_nodes = True
            gb = next(nd for nd in gm.node_tree.nodes if nd.type == "BSDF_PRINCIPLED")
            gb.inputs["Base Color"].default_value = (0.03, 0.05, 0.07, 1.0)
            gb.inputs["Roughness"].default_value = 0.08
            win.data.materials.clear()
            win.data.materials.append(gm)
            win.hide_render = False
        CP._assign(obj, CP._pbr_material(rig, asset_dir, name, tint))
        for mode in modes:
            world, sun = CP._make_world(rig, **(CP.NOON if mode == "pbr" else CP.DUSK))
            gnd = CP._make_ground(rig) if ground else None
            for shot, tgt, eye in shots:
                sc.render.resolution_x, sc.render.resolution_y = 1300, 1000
                tg = mathutils.Vector((tgt[1], -tgt[0], tgt[2]))
                ey = mathutils.Vector((eye[1], -eye[0], eye[2]))
                CP._aim(cam, tg, ey - tg, (ey - tg).length)
                path = os.path.join(out_dir, f"{name}_closeup_{shot}_{mode}.png")
                written.append(CP._render(rig, path, engine="EEVEE"))
                print(f"[closeup] {mode} {shot} -> {path}")
            for o in [x for x in (gnd, sun) if x is not None]:
                rig.objects.remove(o)
                bpy.data.objects.remove(o, do_unlink=True)
            sc.world = None
    finally:
        rig.restore()
    return written


def json_load(fh):
    import json
    return json.load(fh)


# ======================================================================================
# Clock-tower module (contract section 5) - recipe-local, exposed for reuse
# ======================================================================================

CT_BASE_W, CT_BASE_TOP = 5.2, 7.3
CT_SHAFT_W, CT_SHAFT_TOP = 4.0, 19.5
CT_STAGE_W, CT_STAGE_TOP = 4.6, 22.3
CT_CLOCK_D, CT_CLOCK_Z = 2.5, 20.9
CT_CAP_OVER, CT_CAP_APEX, CT_FINIAL_TOP = 0.9, 25.0, 25.8


def clock_tower(name="ct", cx=0.0, cy=0.0, z0=0.0, *, ground=True, stone="lux_stone_tan",
                boards="lux_vboard_red", trim="trim", roof="lux_slate", door_faces=("+X", "-Y"),
                rise=0.0, clock_z=None, clock_d=None, dial=None, louvres=False, fine=False):
    """
    The swanky clock tower (contract section 5; Jackson Hole Teton Village proportions):
    stone base 5.2 x 5.2 from z0 to 7.3 with recessed doors 1.6 x 2.6 on `door_faces`,
    vertical-board shaft 4.0 x 4.0 to 19.5 with corner boards, clock stage 4.6 x 4.6 to 22.3
    with four clock faces (d 2.5, `clock_<name><k>_face`, stamped with cs2_bldg.paint_clock:
    list them in the host recipe's labels, one entry per face), pyramid cap (eave 22.3,
    overhang 0.9, apex 25.0, slate) and a finial to 25.8.

    z0 = 0 with ground=True: the base is an open-bottom ground part (`wall_g<name>`, an
    engine-base host such as L5 lists the prefix "wall_g"); a hosted static object passes
    z0 = -1.5, ground=False (footing for uneven hosts, closed bottom). Heights are absolute
    (z0 only moves the base bottom). Part names: wall_g<name>, tower_<name>shaft, trim_<name>corners,
    tower_<name>stage, clock_<name><k>_face, clockring_<name><k>, roof_<name>cap...,
    tower_<name>fin, entry_/door_ for the doors. Reused by the tram base station.
    `rise` (L5 revision 1) lengthens the shaft: everything above the stone base moves up by
    `rise` m (shaft top, stage, clock, cap apex, finial); 0 = the section-5 tower above.
    L5 revision 2 (verdict B1' "the clock is a bit hidden by the roof so it needs to move
    down"; all four default to the section-5 tower, byte-identical): `clock_z` = the dial
    centre (absolute); a dial whose top is below the shaft top sits ON THE SHAFT (plane
    +-2.0) instead of the stage (+-2.3), and must clear the stage by 0.3 m; `clock_d` = the
    dial diameter; `dial` = the side of a square glulam dial board `trim_<name>dial<k>` (0.06
    proud) the dial sits on; `louvres` = a louvred belfry vent on each stage face
    (`trim_<name>louvre<k>` dark backing + `trim_<name>slat<k>` glulam slats) where the clock was.
    Fine-detail step 3e (design B.1, opt-in; the default = the geometry above, vertex for vertex):
    `fine` = the finial spire is 12-seg (was 6), same base / tip radii and height.
    Returns the parts list; asserts its own section-5 contract (apex, finial, footprint).
    """
    K = _kit()
    from cs2_bldg import RoofSpec, WallRef
    register_materials()
    P = []
    hb, hs, hg = CT_BASE_W / 2, CT_SHAFT_W / 2, CT_STAGE_W / 2
    if rise < 0.0:
        raise ValueError("clock_tower: rise must be >= 0")
    shaft_top, stage_top = CT_SHAFT_TOP + rise, CT_STAGE_TOP + rise
    clock_z = CT_CLOCK_Z + rise if clock_z is None else float(clock_z)
    cap_apex, fin_top = CT_CAP_APEX + rise, CT_FINIAL_TOP + rise
    if ground:
        if abs(z0) > 1e-9:
            raise ValueError("clock_tower: a ground base starts at z 0")
        P += K.box_part(f"wall_g{name}", (cx - hb, cy - hb, 0.0),
                        (cx + hb, cy + hb, CT_BASE_TOP), stone, ground=True)
    else:
        P += K.box_part(f"wall_g{name}", (cx - hb, cy - hb, z0),
                        (cx + hb, cy + hb, CT_BASE_TOP), stone)
    P += K.box_part(f"plinthcap_{name}base", (cx - hb - 0.08, cy - hb - 0.08, CT_BASE_TOP),
                    (cx + hb + 0.08, cy + hb + 0.08, CT_BASE_TOP + 0.18), "concrete")
    P += K.box_part(f"tower_{name}shaft", (cx - hs, cy - hs, CT_BASE_TOP + 0.18),
                    (cx + hs, cy + hs, shaft_top), boards, drop=("-Z", "+Z"))
    corners = []
    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = cx + sx * (hs - 0.04), cy + sy * (hs - 0.04)
            corners.append(((x - 0.12, y - 0.12, CT_BASE_TOP + 0.18), (x + 0.12, y + 0.12, shaft_top)))
    P += K.boxes_part(f"trim_{name}corners", corners, trim)
    P += K.box_part(f"tower_{name}stage", (cx - hg, cy - hg, shaft_top),
                    (cx + hg, cy + hg, stage_top), boards, drop=("+Z",))
    cdia = CT_CLOCK_D if clock_d is None else float(clock_d)
    reach = 0.5 * max(cdia, dial or 0.0)
    on_shaft = clock_z + reach < shaft_top - 1e-9
    if on_shaft:
        check(clock_z + reach <= shaft_top - 0.3, f"clock tower {name}: dial top "
              f"{clock_z + reach:.2f} within 0.3 m of the stage {shaft_top:.2f}")
        check(reach <= hs - 0.04 - 0.12 - 0.15, f"clock tower {name}: dial {2 * reach:.2f} m "
              f"reaches the shaft corner boards")
        check(clock_z - reach >= CT_BASE_TOP + 1.0, f"clock tower {name}: dial below the shaft")
    face_half = hs if on_shaft else hg
    for k, side in enumerate(("+X", "+Y", "-X", "-Y")):
        sg = 1.0 if side[0] == "+" else -1.0
        plane = {"+X": cx + face_half, "-X": cx - face_half, "+Y": cy + face_half,
                 "-Y": cy - face_half}[side]
        uc = {"+X": cy, "-X": -cy, "+Y": -cx, "-Y": cx}[side]
        if dial:
            P += K.wall_box(f"trim_{name}dial{k}", WallRef(side, plane), uc - dial / 2,
                            uc + dial / 2, clock_z - dial / 2, clock_z + dial / 2, 0.0, 0.06,
                            "lux_glulam" if "lux_glulam" in K.BLDG_MATERIALS else trim)
            plane += sg * 0.06
        ps = K.emblem_disc(f"clk{name}{k}", WallRef(side, plane), uc, clock_z, cdia,
                           t=0.08, segs=24)
        renamed(ps, {f"emblem_clk{name}{k}_face": f"clock_{name}{k}_face",
                     f"emblem_clk{name}{k}_rim": f"clockring_{name}{k}"})
        P += ps
        if louvres:                     # belfry vent on the stage face the clock left
            sw = WallRef(side, {"+X": cx + hg, "-X": cx - hg, "+Y": cy + hg, "-Y": cy - hg}[side])
            zl0, zl1 = shaft_top + 0.45, stage_top - 0.55
            P += K.wall_box(f"trim_{name}louvre{k}", sw, uc - 0.75, uc + 0.75, zl0, zl1, 0.0,
                            0.03, "ski_black" if "ski_black" in K.BLDG_MATERIALS else trim)
            n = 4
            slats = [(uc - 0.75, uc + 0.75, zl0 + (zl1 - zl0) * (j + 0.5) / n - 0.06,
                      zl0 + (zl1 - zl0) * (j + 0.5) / n + 0.06, 0.03, 0.10) for j in range(n)]
            P += K.wall_boxes(f"trim_{name}slat{k}", sw, slats,
                              "lux_glulam" if "lux_glulam" in K.BLDG_MATERIALS else trim)
    # pyramid cap: hip_roof over the stage square; pitch solved for the apex
    ov = CT_CAP_OVER
    thick = 0.22
    hw = hg + ov
    t = (cap_apex - stage_top - 0.5 * thick * 1.5) / (hw - ov)
    pitch = math.degrees(math.atan(t))
    for _ in range(3):        # refine: hz depends on the pitch
        hz = 0.5 * thick * math.sqrt(1 + t * t)
        t = (cap_apex - stage_top - hz) / (hw - ov)
        pitch = math.degrees(math.atan(t))
    spec = RoofSpec("hip", cx - hg, cx + hg, cy - hg, cy + hg, stage_top, pitch, axis="Y",
                    overhang=ov, thick=thick, mat=roof)
    P += K.hip_roof(f"{name}cap", spec, fascia=0.30, ridge_cap=(0.20, 0.08))
    apex = K.roof_z(spec, cx, cy)
    P += K._single(f"tower_{name}fin", lambda g: g.cyl((cx, cy, apex - 0.15), (cx, cy, fin_top),
                                                       0.10, 12 if fine else 6, r1=0.03), trim)
    for side in door_faces:
        plane = {"+X": cx + hb, "-X": cx - hb, "+Y": cy + hb, "-Y": cy - hb}[side]
        uc = {"+X": cy, "-X": -cy, "+Y": -cx, "-Y": cx}[side]
        P += K.entry(f"{name}{side[1].lower()}{'p' if side[0] == '+' else 'n'}",
                     WallRef(side, plane), uc, w=1.6, h=2.6, sidelights=0, lamps=0,
                     z=max(z0, 0.0) + 0.14, surround_mat="lux_glulam" if "lux_glulam" in
                     K.BLDG_MATERIALS else "timber_heavy")
    split_double_doors(P)               # L5 r1 coplanar hygiene (the leaves' meeting jambs)
    lo, hi = parts_bounds(P)
    check(abs(apex - cap_apex) < 0.05, f"clock tower apex {apex:.2f} != {cap_apex}")
    check(abs(hi[2] - fin_top) < 1e-3, f"clock tower top {hi[2]:.3f} != {fin_top}")
    half = hg + ov + 0.36       # cap + fascia + rake reach (skeptic: +-3.2, not 2.6)
    check(max(abs(lo[0] - cx), abs(hi[0] - cx), abs(lo[1] - cy), abs(hi[1] - cy)) <= half,
          f"clock tower footprint exceeds +-{half:.2f} m: {lo} {hi}")
    check(abs(lo[2] - z0) < 1e-3, f"clock tower bottom {lo[2]:.3f} != {z0}")
    return P


def clock_tower_labels(name="ct"):
    """Recipe label entries for the four clock faces (one stamp per face: a prefix that
    matched all four would paint across unrelated islands, cs2_compose.uv_bbox_px)."""
    return tuple((f"clock_{name}{k}_face", "paint_clock", {}) for k in range(4))


# ======================================================================================
# L5 constants - REVISION 1 (2026-10-11, verdicts-2026-10-11.md B1: Richard "the Swanky
# lodge should be bigger", reference = the Breckenridge Grand Lodge on Peak 7, "pools are a
# good addition"). Authoring metres; lot 10 x 8 cells = 80 x 64 m (was 8 x 6: the
# ROUND-SPEC lot table is amended by that verdict).
#
# Plan (top view, +X = road / arrival, -X = slope):
#   M  main bar      X -8..6,  Y -36..36: stone GF + 3 timber floors + knee wall, gable
#                    roof (ridge X -1, 22.0) with 4 cross gables and 7 dormers
#   WS / WN wings    X -30..-8, Y -34..-22 / 22..34: same storeys, gable roofs (ridge 21.0)
#                    running into M's roof, king-post gable ends facing the slope
#   H  great hall    X 6..18, Y -9..9 (the revision-0 hall, moved with M's road face)
#   P  porte-cochere X 20..27.4 on the road side; E forecourt to X 31
#   CT clock tower   (12.5, 16.5) in front of M, shaft raised 4 m (finial 29.8)
#   T  pool terrace  in the courtyard between the wings: raised paver deck (0.72) on stone
#                    retaining walls, a lagoon pool + 2 hot tubs with coping, loungers,
#                    umbrellas, lamp posts, a fire pit; grade walkways around it
# Revision 3 (this frame, before the +4 shift): S slope plaza X -39.4..-30 end to end + the
#   strip to the podium; E valet forecourt (drive X 21.75..31.4, Y -16..31, island X
#   25.75..31.4, Y -12..7, walks along the hall, under the deck and to the stair foot); the
#   deck stair on the deck's +Y edge at X 17.5..20.5 running to Y 28.6; loungers, umbrellas
#   and lamp posts are vanilla sub-objects (SUB_OBJECT_ROWS)
# ======================================================================================

LOT_W, LOT_D = 10, 9                               # revision 2: 10 x 8 -> 10 x 9 (plaza)
HX, HY = 4.0 * LOT_D, 4.0 * LOT_W                 # 36, 40
# revision 2: every constant below is in the revision-1 frame; build_l5 assembles there and
# then moves the whole build by X_SHIFT (shift_x), so in that frame the lot spans x
# -HX - X_SHIFT .. HX - X_SHIFT. Revision 2 was -4 (lot -32..40: the new cell on the road
# side for the road plaza). Revision 3 (verdict B1'' "I wanted the PLAZA ON THE POOL SIDE"):
# +4, lot -40..32 - the road margin is revision 1's again (forecourt to 31.4) and the new
# cell is the slope-side plaza
X_SHIFT = 4.0

Z_F = (0.0, 4.5, 7.7, 10.9)                        # floor levels GF, F2, F3, F4
# roof wall line: F4 3.2 m + a 0.9 m knee wall, so the 1.0 m eave (tip 14.0) clears the F4
# window heads (13.5) instead of hanging across them (the revision-0 F1 lesson)
Z_WALL = 15.0
STOREY_LINES = (Z_F[1], Z_F[2], Z_F[3], Z_WALL)
UPPER = ((2, Z_F[1]), (3, Z_F[2]), (4, Z_F[3]))   # (floor tag, floor z) of F2..F4
M_X0, M_X1, M_Y0, M_Y1 = -8.0, 6.0, -36.0, 36.0
M_RIDGE_X, M_PITCH, M_OVER, ROOF_T = -1.0, 45.0, 1.0, 0.30
M_RIDGE = Z_WALL + math.tan(math.radians(M_PITCH)) * (M_RIDGE_X - M_X0)    # 22.0
BAY = 8.0
# cross-gable bay projection beyond the host walls: past the host eave stack (overhang 1.0
# + fascia + gutter, ~1.26 m) so the eave, fascia and gutter die into the bay sides
# (buildings-review-geometry F1; asserted for every bay)
XG_PROJ = 1.6
XG_HALF = 3.0
XG_RIDGE = Z_WALL + XG_HALF                        # 18.0 at 45 deg
XG_ROAD = (-24.0, 24.0)                            # M road face (+X), stone GF from grade
XG_COURT = (-16.0, 16.0)                           # M courtyard face (-X), from F2 on brackets
XG_WING = -18.0                                    # wing courtyard faces, from F2 on brackets

W_X0, W_X1 = -30.0, M_X0
WINGS = (("s", -34.0, -22.0), ("n", 22.0, 34.0))  # (tag, y0, y1); inset 2 m from M's ends
W_RIDGE = Z_WALL + 6.0                             # 21.0 (12 m deep, 45 deg)

H_X0, H_X1, H_Y = M_X1, 18.0, 9.0
H_EAVE = 9.0
H_APEX = H_EAVE + H_Y                              # 18.0 at 45 deg
H_TRUSS_X = H_X1 + 0.75
H_PIER_Y = 8.1                                     # pier Y 7.1..9.1: 0.1 proud of the flank

# where a roof whose centre-plane ridge is z meets M's top plane (same pitch / thickness)
X_MEET_ROAD = M_RIDGE_X + (M_RIDGE - XG_RIDGE)     # 3.0 (road xg)
X_MEET_COURT = M_RIDGE_X - (M_RIDGE - XG_RIDGE)    # -5.0 (courtyard xg)
H_MEET = M_RIDGE_X + (M_RIDGE - H_APEX)            # 3.0 (hall)
W_MEET = M_RIDGE_X - (M_RIDGE - W_RIDGE)           # -2.0 (wings)

P_X0, P_X1, P_Y = 20.0, 27.4, 6.0                  # rake edge 27.8 + board 0.12 < 32
P_TIE = 5.0                                        # truss chords (centre) on the posts
P_POST_X = (20.7, 26.8)
E_X1 = 31.0                                        # forecourt front edge

CT_C = (12.5, 16.5)
CT_RISE = 4.0                                      # finial 29.8: the landmark over the 22 m ridge
CHIM_TOP = 24.8                                    # M ridge stacks (ridge top 22.21 + 2.6)
FLUE_TOP = CHIM_TOP + 0.55                         # flue pots: max z with --no-tower
W_CHIM_TOP = 23.3                                  # wing exterior stacks (wing ridge top 21.21 + 2.1)

# pool terrace (contract: water below the deck top, no coplanar faces)
POOL_X0, POOL_X1, POOL_Y0, POOL_Y1 = -29.0, -13.0, -17.0, 17.0
PD = 0.72                                          # deck top (stone retaining walls 0..0.72)
POOL_CAP_W, POOL_CAP_H = 0.40, 0.10                # podium coping ring (0.05 proud)
COPE_W = 0.35                                      # main-pool coping width
MAIN_TOP, MAIN_WATER = PD + 0.06, PD - 0.25        # coping top 0.78, water 0.47
TUB_TOP, TUB_WATER = PD + 0.40, PD - 0.10          # raised stone tub rim 1.12, water 0.62
TUB_R_IN, TUB_R_OUT = 1.45, 1.95
TUBS = ((-16.0, 10.5), (-25.5, 11.0))
FIREPIT = (-15.6, -14.6)
# lagoon outline (CCW control points; one Chaikin pass), waist on the east side
MAIN_POOL_CTRL = ((-18.0, -11.5), (-17.6, -6.0), (-18.8, -2.5), (-17.6, 1.5), (-18.3, 5.8),
                  (-21.5, 6.3), (-24.3, 4.0), (-24.8, -1.0), (-24.4, -6.5), (-22.5, -11.8))

SLAB_T = 0.14                                      # walkway / forecourt slab top (lifted)
# closed parts at grade start here: cs2_bldg.GROUND_LIFT (0.02) is the floor, but the LOD2
# collapse moved lifted pier bottoms 0.02 -> 0.013 (inside the forbidden 0.011..0.02 band,
# check_base_mode on LOD2, 2026-10-11); 0.05 leaves decimation headroom
LIFT = 0.05

# ground parts all start "wall_g": gm (M GF), gws / gwn (wing GFs), gxr0 / gxr1 (road
# cross-gable GFs), gh (hall), ghgl (hall glass), gpool (pool retaining walls), gct
# (clock-tower base; absent with --no-tower, so one shared prefix)
GROUND_PREFIXES = ("wall_g",)
GROUND_PARTS = ("wall_gm", "wall_gws", "wall_gwn", "wall_gxr0", "wall_gxr1", "wall_gh",
                "wall_ghgl", "wall_gpool")
ARRIVAL_PREFIXES = ("entry_h", "pier_pc", "col_pc", "roof_pc", "deck_e")
CHIM_CAPS = ("chimcap_m0", "chimcap_m1", "chimcap_hx", "chimcap_ws", "chimcap_wn")

# ---------------- revision 2 (2026-10-11, verdict B1' "Seventh look") -------------------------
# (b) the clock: dial centre on the SHAFT (was CT_CLOCK_Z + CT_RISE = 24.9 on the stage, where
# the cap's 0.9 m overhang at ~48 deg drops its eave to ~25.0, in front of the dial's top
# third even from straight ahead; measured from the preview cameras: 41-59 % of the dial
# samples visible at 10-20 deg, 0 % above 57 deg). On the shaft the limit is the cap fascia
# seen past the stage corner from the 3/4 azimuths (sweep, every dial facing the camera, clear
# up to: z 21.2 -> 48-66 deg, 20.9 -> 51-68, 20.6 -> 54-70, 20.3 -> 56-72): 20.3 keeps every
# facing dial whole up to CLOCK_CLEAR_DEG from every arrival azimuth.
CT_CLOCK_Z_L5, CT_CLOCK_D_L5, CT_DIAL_L5 = 20.3, 2.9, 3.3
CLOCK_CLEAR_DEG = 55.0                             # contract: full dial visible 0..55 deg
# arrival azimuths in the authoring frame (+X = road): the preview views arrival_front,
# arrival_3q, road34left and arrival (lodge.VIEWS / RECIPE.views, (bx, by) -> (-by, bx))
CLOCK_AZIMUTHS = (("front", (1.0, 0.05)), ("arrival_3q", (1.0, 0.8)), ("road34left", (1.0, -0.7)),
                  ("arrival", (1.0, 0.55)))
# (c) the tower deck: one storey up (F2 floor), between M's road face, the hall's +Y flank and
# the road cross gable r1; wraps the tower base (CT_C +- CT_BASE_W / 2) on all four sides
DK_Z, DK_T = Z_F[1], 0.25                          # deck top 4.5, slab 0.25
DK_X0, DK_X1 = M_X1, 21.0                          # M's road face .. 3 m past the hall glass
DK_Y0, DK_Y1 = H_Y, XG_ROAD[1] - XG_HALF           # the hall's +Y wall (9.0) .. the r1 bay (21.0)
DK_BEAM = (0.35, 0.45)                             # glulam beam w x h under the slab
DK_POSTS = ((10.35, 20.45), (15.45, 20.45), (20.55, 9.45), (20.55, 15.15), (20.55, 20.45))
DK_RAIL_H = 1.0
# (3) revision 3 (verdict B1'' "the STAIRS should be to the RIGHT, so that cars can pull through
# the valet area"): the stair leaves the deck from its right-hand edge (+Y, seen from the road:
# DK_Y1) and runs on +Y down to the walk (25 risers of 0.174, treads 0.30). Its band x
# DK_STAIR_X +- DK_STAIR_W / 2 lies inside the deck's front post line (x 20.55), so the foot
# lands on the walk 1.25 m short of the drive (LANE_X0). Revision 2 ran it from the front edge
# (x 21) out to x 28.6 at y 14.9, across the valet lane.
DK_STAIR_X, DK_STAIR_W = 19.0, 3.0                 # stair centre / width (+Y edge, run to +Y)
DK_STAIR_FOOT_Y = 28.6                             # 25 risers of 0.174, treads 0.30
DK_STAIR_LAND = 1.5                                # free landing beyond the foot (contract)
# (1) revision 3: the plaza on the POOL / SLOPE side (lux_paver, the pool deck's pavers, as the
# Peak 7 pool plaza) from 0.6 m inside the back lot edge to the wing gable ends (W_X0), end to
# end (HY - 1.5 each side), plus the strip from the wing line to the podium (|y| <= POOL_Y1):
# it runs into the courtyard walkways (deck_w1 / w2, same SLAB_T) and up the podium steps on
# its -X face (pool_steps "-X"). Plaza SLAB_T 0.14, podium deck PD 0.72, coping PD + 0.10.
PS_X0 = -HX - X_SHIFT + 0.6                        # -39.4 (revision-1 frame) = -35.4 shipped
PS_Y = HY - 1.5                                    # 38.5
PS_STEPS = (-2.0, 2.0)                             # the podium's -X flight (y): the plaza axis
# four freestanding kit racks with real gear along the slope edge where skiers come in (no
# vanilla ski rack exists); kit gear WITHOUT bindings (cs2_bldg default since Richard's ninth
# look; this recipe never passes bindings=); pole pairs at the kit's 1.38 m so they reach the
# rack's 1.33 m lean (revision 2's 1.20-1.28 m poles stopped in mid-air)
PS_RACK_X = -37.6
PZ_RACKS = (                                       # (name, x, y, along, gear entries)
    ("pzS", PS_RACK_X, -24.5, "Y", (
        (0, 1, "ski", ("trim_white", "ski_blue"), 1.72), (1, 1, "ski", ("ski_black", "accent"), 1.64),
        (2, 1, "board", ("ski_black", "accent"), 1.54), (4, 1, "ski", ("ski_red", "ski_black"), 1.60),
        (6, 1, "poles", "ski_black", 1.38), (1, -1, "ski", ("accent", "ski_black"), 1.70),
        (3, -1, "board", ("trim_white", "ski_red"), 1.50), (5, -1, "ski", "trim_white", 1.58),
        (7, -1, "ski", ("ski_blue", "trim_white"), 1.76))),
    ("pzW", PS_RACK_X, -20.0, "Y", (
        (0, 1, "ski", ("ski_black", "accent"), 1.76), (1, 1, "ski", "trim_white", 1.62),
        (3, 1, "board", ("accent", "ski_black"), 1.52), (5, 1, "ski", ("ski_red", "ski_black"), 1.58),
        (6, 1, "poles", "ski_blue", 1.38), (0, -1, "ski", ("trim_white", "ski_red"), 1.72),
        (2, -1, "ski", ("ski_blue", "trim_white"), 1.70), (4, -1, "board", ("ski_black", "accent"), 1.55),
        (7, -1, "ski", ("accent", "ski_black"), 1.64))),
    ("pzE", PS_RACK_X, 20.0, "Y", (
        (1, 1, "ski", ("ski_blue", "trim_white"), 1.80), (2, 1, "ski", ("ski_black", "trim_white"), 1.60),
        (4, 1, "board", ("trim_white", "ski_red"), 1.46), (6, 1, "ski", ("accent", "ski_black"), 1.68),
        (7, 1, "ski", "ski_red", 1.56), (0, -1, "poles", "ski_black", 1.38),
        (1, -1, "ski", ("trim_white", "ski_blue"), 1.62), (3, -1, "ski", ("ski_red", "ski_black"), 1.74),
        (5, -1, "board", ("ski_blue", "trim_white"), 1.58))),
    ("pzN", PS_RACK_X, 24.5, "Y", (
        (0, 1, "board", ("ski_blue", "trim_white"), 1.56), (2, 1, "ski", ("trim_white", "ski_red"), 1.68),
        (3, 1, "ski", "ski_black", 1.62), (5, 1, "ski", ("accent", "ski_black"), 1.74),
        (7, 1, "poles", "accent", 1.38), (0, -1, "ski", ("ski_blue", "trim_white"), 1.66),
        (2, -1, "ski", ("ski_red", "ski_black"), 1.58), (4, -1, "board", ("ski_black", "accent"), 1.52),
        (6, -1, "ski", ("trim_white", "accent"), 1.80))),
)
# the plaza fire pit (modelled: a PLACEHOLDER, see PLACEHOLDERS) at the -Y end, the asymmetric
# counterweight to the cafe terrace; vanilla benches face it (SUB_OBJECT_ROWS)
PZ_FIREPIT = (-34.0, -31.0)
# (2) revision 3: the road side is a forecourt with a VALET LOOP (verdict B1''): cars come in
# on the left (-Y) leg, run on +Y under the porte-cochere between its pier rows and leave over
# the open apron on the right (+Y) to the road. A raised island in front of the canopy holds
# pots, garden lights and the flag proposals. Drive DRIVE_T, walks SLAB_T (a 4 cm curb),
# island ISLAND_T. The walk runs along the hall front, under the tower deck (the arcade with
# the tower's grade doors) and on to the stair foot.
DRIVE_T, ISLAND_T = 0.10, 0.28
RD_X1 = HX - X_SHIFT - 0.6                         # 31.4: 0.6 m inside the road edge
LANE_X0, LANE_X1 = 21.75, 25.75                    # the lane under the canopy (pier faces 21.4 / 26.1)
DRIVE_Y0, DRIVE_Y1 = -16.0, 31.0                   # the loop's outer edges
ENTRY_Y1 = -12.0                                   # the entry leg: y DRIVE_Y0..ENTRY_Y1
ISLAND_Y0, ISLAND_Y1 = ENTRY_Y1, 7.0               # the island; the exit apron y ISLAND_Y1..DRIVE_Y1
WALK_X0 = H_X1 + 0.3                               # 18.3: the hall's plinth-ring front
WALK_XB = 17.4                                     # the walk past the hall (under the deck front)
CORRIDOR_MIN = 6.0                                 # contract: free drive width past the deck

# ---------------- revision 4 (2026-10-12, eighteenth look) -------------------------------------
# (a) the porte-cochere gable: T&G infills just inside the two king-post trusses (members 0.40 on
# the post lines P_POST_X), the ceiling between them, frieze boards on the tie beams. The front
# truss face is the G1 plane; whatever a ray sees inside the gable lies within GABLE_DEPTH_MAX of it
PC_GABLE_INFILL = True                             # False = the revision-3 open gable (G1 negative)
PC_GAB_T = 0.12
PC_GAB_FRONT = (P_POST_X[1] - 0.22 - PC_GAB_T, P_POST_X[1] - 0.22)    # x 26.46 .. 26.58
PC_GAB_BACK = (P_POST_X[0] + 0.22, P_POST_X[0] + 0.22 + PC_GAB_T)     # x 20.92 .. 21.04
PC_GAB_E, PC_GAB_BASE, PC_GAB_EAVE = 5.55, P_TIE, P_TIE + 0.30   # half width; base 5.0 = the tie-beam centre (behind
#   the truss chord 4.8..5.2: no slot under the boards), eave point 5.30 (on the ceiling top)
PC_CEIL_Z = (P_TIE + 0.24, P_TIE + 0.30)          # the ceiling slab between the infills
PC_GAB_LIGHT_Y, PC_GAB_LIGHT_Z = 1.0, 6.0          # the two gable lights (under the braces)
GABLE_DEPTH_MAX = 0.80
GABLE_FACE_X = P_POST_X[1] + 0.20                  # the front truss face (27.0)
# close walk-around cameras for G1 beside the preview ones: (distance from the truss face,
# azimuth deg from +X, height) - the drive at car height, the forecourt, the island's far end
GABLE_NEAR_CAMS = tuple((r, a, zc) for r in (12.0, 25.0) for a in (-40.0, 0.0, 40.0)
                        for zc in (1.6, 5.0, 9.0))
# (b) the terrace doors: WS / WN courtyard faces under their cross gables (x = XG_WING) and M's
# courtyard face on the plaza axis (y 0); threshold = the walk (SLAB_T). The podium flights that
# meet them: the +-Y flights re-centred on XG_WING (were x -23..-19), a new +X flight on y 0
POOL_DOORS = (("dws", "+Y", WINGS[0][2], XG_WING), ("dwn", "-Y", WINGS[1][1], XG_WING),
              ("dm", "-X", M_X0, 0.0))
NS_STEPS_HALF = 1.8                                # the +-Y flights: x XG_WING +- 1.8
E2_HALF = 1.5                                      # the new +X flight: y +- 1.5
DOOR_SIDE_GAP = 0.15                               # wall between a door's frame and its sidelight
DOOR_TRANSOM_Z = 2.70                              # the transom light over the door (to 4.0)
DOOR_BAYS = {"dws": (-20.9, -15.1), "dwn": (-20.9, -15.1), "dm": (-2.9, 2.9)}   # the old curtains
DOOR_HOSTS = {"dws": "wall_gws", "dwn": "wall_gwn", "dm": "wall_gm"}
DOOR_STEPS = {"dws": "stairblk_s", "dwn": "stairblk_n", "dm": "stairblk_e2"}
# the arrival-side preview views (lodge.VIEWS + RECIPE.views; tests assert they match): the G1
# gable contract's far cameras
ARRIVAL_VIEWS = (("arrival_3q", (0.8, -1.0, 0.45)), ("arrival_front", (0.05, -1.0, 0.20)),
                 ("arrival", (0.55, -1.0, 0.22)), ("road34left", (-0.7, -1.0, 0.45)),
                 ("valet", (-0.35, -1.0, 0.65)), ("tower_deck", (0.95, -0.7, 0.42)),
                 ("gable_closeup", (-0.15, -1.0, 0.25)))

# ---------------- revision 3: vanilla props by reference (verdict B1'' + the standing rule of the
# twelfth look: trees, benches, bins, tables, chairs, lamp posts, planters, bollards = VANILLA
# sub-objects; modelled only where no vanilla prop exists, each stand-in flagged) -------------
# Placeholder GUIDs (the SpawnableObject.m_Placeholders GUID every variant names; the
# placeholder prefab carries no GUID of its own, so the name is "inferred" in the preflight
# whitelist sense, as cs2_liftdress.SUBOBJ). bounds = the union of the placeholder's own mesh
# and every variant mesh (RenderPrefab m_Bounds, game local, y up; a variant without a "<V>
# Mesh" uses its "<stem><Kind><nn> Mesh" set: the trees' growth stages, Trashbin01-04's
# TrashbinEmpty / TrashbinFull meshes - which is why the bin here is +-0.36, wider than
# cs2_liftdress.SUBOBJ's +-0.25 placeholder-only envelope), tris = the largest mesh, snow =
# WeatherObject ("forbid": hidden while the owner's
# Surface.m_AccumulatedSnow >= 15, "require": shown only then; SubObjectSystem.cs:3231-3233,
# WeatherObject.cs:30-42, 1.6.0f1). tests/test_lodges_swanky.verify_vanilla re-derives every
# field from %TEMP%\pltar\dump.zip and cross-checks the four cs2_liftdress.SUBOBJ entries.
VANILLA = {
    "TableSetCommercialRandom01": dict(
        guid="fc058a3d03b337e4794c432a84170316",
        variants=tuple(f"TableSetCommercial0{k}" for k in range(1, 7)),
        bounds=((-1.099, -0.013, -1.259), (1.099, 1.088, 1.259)), tris=1636, snow=None),
    "ParasolRandom01": dict(
        guid="b7e2343fd7ff9f642b77084a04738e43", variants=("Parasol01", "Parasol02", "Parasol03"),
        bounds=((-1.75, 0.0, -0.726), (1.75, 2.641, 3.056)), tris=1130, snow="forbid"),
    "DeckChairRandom01": dict(
        guid="63c46490ab59afd4dae01ee442f1e462", variants=("DeckChair01", "DeckChair02", "DeckChair03"),
        bounds=((-0.398, 0.0, -1.195), (0.398, 1.226, 1.195)), tris=388, snow="forbid"),
    "GardenBenchRandom01": dict(
        guid="8df0703be09426948b5bc3262d86bc04",
        variants=("GardenBench01", "GardenBench02", "GardenBench03", "GardenBench04"),
        bounds=((-1.059, 0.0, -0.496), (1.059, 1.054, 0.496)), tris=906, snow=None),
    "TrashbinRandom01": dict(
        guid="41e3c9ba6e1d6b948a6b1bc3499a5397",
        variants=("Trashbin01", "Trashbin02", "Trashbin03", "Trashbin04"),
        bounds=((-0.355, 0.0, -0.364), (0.355, 1.036, 0.364)), tris=658, snow=None),
    "LightpoleParkRandom01": dict(
        guid="c987b53e24a01e744b4a00ee5b1f8a08",
        variants=("LightpolePark01", "LightpolePark02", "LightpolePark03", "LightpolePark04"),
        bounds=((-0.267, 0.0, -0.235), (0.267, 3.268, 0.833)), tris=1788, snow=None),
    "GardenLightRandom01": dict(
        guid="ba47f549eb430ac46a7c1b4cca509594", variants=("GardenLight01", "GardenLight02", "GardenLight03"),
        bounds=((-0.144, 0.0, -0.144), (0.144, 0.62, 0.144)), tris=714, snow=None),
    "PotRandom01": dict(
        guid="8d8722db083c3564291e3778cceeb1ae",
        variants=("FlowerPot01", "FlowerPot02", "PlantPot01", "PlantPot02"),
        bounds=((-0.549, 0.0, -0.597), (0.56, 1.103, 0.543)), tris=690, snow=None),
    "GasHeaterRandom01": dict(
        guid="0bbcb0620e01694448cb78385580351b", variants=("GasHeater01", "GasHeater02"),
        bounds=((-0.37, 0.0, -0.367), (0.365, 2.241, 0.367)), tris=2050, snow="require"),
    "TreeWildRandom01": dict(
        guid="f7955fd41c1617847b4a4138acabcbcb",
        variants=("BirchTree01", "EU_AlderTree01", "NA_HickoryTree01", "OakTree01", "PineTree01",
                  "SpruceTree01"),
        bounds=((-9.8, -0.035, -10.128), (9.637, 21.57, 10.128)), tris=30630, snow=None),
    # revision 4 (2026-10-12): the porte-cochere gable lights (and L4's entry lights, its
    # revision 1): the fine-detail default for a wall lantern (Richard H4, seventeenth look),
    # 138 tris; mount "wall" = the pivot on the wall face, local +Z out of it (0..0.115), y
    # vertical about the pivot (cs2_liftdress.SUBOBJ, verify_subobj)
    "WallLightShortRangeRandom01": dict(
        guid="cfc12fde01d9da24b86ddedd657eaae1",
        variants=("WallLightShortRange01", "WallLightShortRange02", "WallLightShortRange03"),
        bounds=((-0.142, -0.058, 0.0), (0.142, 0.058, 0.115)), tris=138, snow=None, mount="wall"),
}
# collision envelopes the contracts use beside the full bounds (unverified shapes, conservative
# sizes): a parasol is a pole at its pivot (local +-0.30, to 1.80) under a canopy tier (the full
# footprint from 1.80 up); a tree is its trunk (StandingObject m_LegSize 1 x 3, circular) for
# clearances and the full union envelope for the clock sightline
PARASOL_POLE, PARASOL_TIER = 0.30, 1.80
TREE_TRUNK = (0.50, 3.0)
SUB_PAD = 0.10                                     # clearance of every prop from every modelled part
# the vanilla parasol convention (measured on the dump by the L1 / L3 lane, bldg_recipes
# PARASOL_OFFSET: 442 base-game parasol / table-set pairs, pivot median 1.37 m from the table,
# quartiles 1.30 / 1.54, local +Z pointing at it, 0 of 442 on the table's pivot): the parasol's
# stand sits PARASOL_OFFSET behind its table and the offset canopy (local z -0.73..3.06) hangs over
# it. _parasol() places one from its table and the yaw (the direction from the pole to the table)
PARASOL_OFFSET = 1.37


def _parasol(tx, ty, z, yaw, zone):
    """SUB_OBJECT_ROWS entry of a parasol serving the table at (tx, ty): pivot PARASOL_OFFSET
    behind it, local +Z (yaw, authoring degrees) pointing at the table."""
    a = math.radians(yaw)
    return ("ParasolRandom01", round(tx - PARASOL_OFFSET * math.cos(a), 4),
            round(ty - PARASOL_OFFSET * math.sin(a), 4), z, yaw, 0, zone)


# (prefab, x, y, z, yaw deg, m_ParentMesh, zone) in the revision-1 frame (pre-shift). yaw turns
# the prop's local +Z (its front: unverified until placement, design/detail-pass-1.md 1.6) to
# the authoring direction: 0 = +X (road), 90 = +Y, 180 = -X (slope), -90 = -Y. m_ParentMesh 0 =
# on our paving / deck (the surface z is asserted under the pivot), -1 = on the flattened lot.
_T, _P, _C, _B = "TableSetCommercialRandom01", "ParasolRandom01", "DeckChairRandom01", "GardenBenchRandom01"
_BIN, _L, _G, _POT = "TrashbinRandom01", "LightpoleParkRandom01", "GardenLightRandom01", "PotRandom01"
_H, _TREE = "GasHeaterRandom01", "TreeWildRandom01"
_ST, _PD, _DK, _IS = SLAB_T, PD, DK_Z, ISLAND_T
SUB_OBJECT_ROWS = (
    # --- the slope plaza: cafe terrace (two rows of four), parasols over four tables (the
    # vanilla convention: stand 1.37 m off the table, poles in the aisle between the rows),
    # winter gas heaters, the fire-pit benches, two benches
    # on the wing gable ends, bins, pots at the podium steps and the slope edge, lamp posts
    # along the slope edge, a tree at each outer corner
    *((_T, x, y, _ST, 90.0, 0, "slope plaza") for x in (-36.8, -33.2) for y in (-12.0, -6.5, 6.5, 12.0)),
    _parasol(-36.8, 6.5, _ST, 180.0, "slope plaza"), _parasol(-36.8, -6.5, _ST, 180.0, "slope plaza"),
    _parasol(-33.2, 12.0, _ST, 0.0, "slope plaza"), _parasol(-33.2, -12.0, _ST, 0.0, "slope plaza"),
    (_H, -31.4, -9.25, _ST, 0.0, 0, "slope plaza"), (_H, -31.4, 9.25, _ST, 0.0, 0, "slope plaza"),
    (_B, PZ_FIREPIT[0] - 2.4, PZ_FIREPIT[1], _ST, 0.0, 0, "slope plaza"),
    (_B, PZ_FIREPIT[0], PZ_FIREPIT[1] - 2.4, _ST, 90.0, 0, "slope plaza"),
    (_B, PZ_FIREPIT[0] + 2.4, PZ_FIREPIT[1], _ST, 180.0, 0, "slope plaza"),
    (_B, -30.85, -28.0, _ST, 180.0, 0, "slope plaza"), (_B, -30.85, 28.0, _ST, 180.0, 0, "slope plaza"),
    (_BIN, -38.8, -2.4, _ST, 0.0, 0, "slope plaza"), (_BIN, -38.8, 2.4, _ST, 0.0, 0, "slope plaza"),
    (_BIN, -36.6, -34.4, _ST, 0.0, 0, "slope plaza"),
    (_POT, -30.7, -3.0, _ST, 0.0, 0, "slope plaza"), (_POT, -30.7, 3.0, _ST, 0.0, 0, "slope plaza"),
    (_POT, -38.7, -14.5, _ST, 0.0, 0, "slope plaza"), (_POT, -38.7, 14.5, _ST, 0.0, 0, "slope plaza"),
    *((_L, -38.9, y, _ST, 0.0, 0, "slope plaza") for y in (-33.0, -16.5, -3.4, 3.4, 16.5, 33.0)),
    (_TREE, -37.0, -37.0, _ST, 0.0, 0, "slope plaza"), (_TREE, -37.0, 37.0, _ST, 0.0, 0, "slope plaza"),
    # --- the pool terrace (podium deck PD): loungers in the revision-1 rows (the west row
    # leaves the -X steps free), parasols at the revision-1 umbrella spots, six lamp posts
    # revision 4: the east row opens on the plaza axis for M's terrace door and the new +X
    # flight (`stairblk_e2`, y +-E2_HALF): loungers at |y| >= 2.4, the parasols between them
    *((_C, -14.85, y, _PD, 0.0, 0, "pool terrace") for y in (-6.0, -4.2, -2.4, 2.4, 4.2, 6.0)),
    *((_C, -27.25, y, _PD, 180.0, 0, "pool terrace") for y in (-8.5, -6.5, -4.5, 4.5)),
    (_P, -14.5, -3.3, _PD, 180.0, 0, "pool terrace"), (_P, -14.5, 3.3, _PD, 180.0, 0, "pool terrace"),
    (_P, -27.6, -10.5, _PD, 0.0, 0, "pool terrace"), (_P, -27.6, 5.5, _PD, 0.0, 0, "pool terrace"),
    (_L, -28.25, -16.25, _PD, 0.0, 0, "pool terrace"), (_L, -28.25, 16.25, _PD, 0.0, 0, "pool terrace"),
    (_L, -28.25, 8.0, _PD, 0.0, 0, "pool terrace"), (_L, -13.75, -16.25, _PD, 180.0, 0, "pool terrace"),
    (_L, -13.75, 16.25, _PD, 180.0, 0, "pool terrace"), (_L, -13.75, 7.0, _PD, 180.0, 0, "pool terrace"),
    # --- the tower deck (DK_Z): three table sets at the revision-2 bistro spots, a parasol, a
    # heater, three lamp posts (two on the front edge, one at the stair head)
    (_T, 17.6, 11.45, _DK, 90.0, 0, "tower deck"), (_T, 17.6, 18.3, _DK, 90.0, 0, "tower deck"),
    (_T, 9.4, 11.45, _DK, 90.0, 0, "tower deck"), _parasol(17.6, 11.45, _DK, 0.0, "tower deck"),
    (_H, 13.4, 11.2, _DK, 0.0, 0, "tower deck"),
    (_L, 20.45, 10.9, _DK, 180.0, 0, "tower deck"), (_L, 20.45, 15.2, _DK, 180.0, 0, "tower deck"),
    (_L, 17.0, 20.45, _DK, 180.0, 0, "tower deck"),
    # --- the forecourt: benches either side of the hall door, a bin, lamp posts (walk end and
    # the island ends), garden lights as lit bollards on the walk curb and the island's lane
    # edge, pots on the island, three trees on the lawn off the sightlines to the clock
    (_B, 19.0, -4.2, _ST, 0.0, 0, "forecourt"), (_B, 19.0, 4.2, _ST, 0.0, 0, "forecourt"),
    (_BIN, 18.85, -10.3, _ST, 0.0, 0, "forecourt"),
    (_L, 21.3, -14.8, _ST, 180.0, 0, "forecourt"),
    (_L, 30.8, -11.2, _IS, -90.0, 0, "forecourt"), (_L, 30.8, 6.2, _IS, 90.0, 0, "forecourt"),
    *((_G, 21.55, y, _ST, 0.0, 0, "forecourt") for y in (-15.5, -10.8, 12.3, 17.8, 24.0, 30.4)),
    *((_G, 26.0, y, _IS, 0.0, 0, "forecourt") for y in (-11.6, -8.0, -2.6, 2.6, 6.6)),
    *((_POT, 29.2, y, _IS, 0.0, 0, "forecourt") for y in (-7.6, -2.4, 2.4)),
    (_TREE, 24.0, -25.0, 0.0, 0.0, -1, "forecourt"), (_TREE, 14.0, -31.0, 0.0, 0.0, -1, "forecourt"),
    (_TREE, 12.0, 38.5, 0.0, 0.0, -1, "forecourt"),
    # --- revision 4 (a): two short-range wall lights on the porte-cochere's T&G gable either
    # side of the king post (pivot on the board face, facing the road), under the curved braces
    *(("WallLightShortRangeRandom01", PC_GAB_FRONT[1], y, PC_GAB_LIGHT_Z, 0.0, 0,
       "porte-cochere gable") for y in (-PC_GAB_LIGHT_Y, PC_GAB_LIGHT_Y)),
)
# FlagPole01_Clt (no harvestable GUID: no placeholder, buildings-integration O3) stays a
# proposal for the surgery round's Editor picker: two on the island ends, one on the pool deck
FLAG_PROPOSALS = ((28.6, -10.6, ISLAND_T), (28.6, 5.6, ISLAND_T), (-28.0, -14.0, PD))


def _yaw_q(deg):
    """Unity yaw about game +Y as (x, y, z, w): local +Z -> game (sin, 0, cos) = authoring
    (cos, sin) (recipes/lodge_hut._yaw_q): 0 = authoring +X, 90 = +Y, 180 = -X, -90 = -Y."""
    h = math.radians(deg) / 2.0
    return (0.0, round(math.sin(h), 7), 0.0, round(math.cos(h), 7))


def to_game(x, y, z):
    """Pre-shift authoring point -> the shipped game local frame (gx, gy, gz) = authoring (y, z,
    x + X_SHIFT) (lodge.sub_objects_block's frame)."""
    return (round(float(y), 4), round(float(z), 4), round(float(x) + X_SHIFT, 4))


# the manifest rows (lodge.sub_objects_block): (prefab, guid, game local position, yaw, parent)
SUB_OBJECTS = tuple((pf, VANILLA[pf]["guid"], to_game(x, y, z), _yaw_q(yaw), int(parent))
                    for pf, x, y, z, yaw, parent, _zone in SUB_OBJECT_ROWS)

# modelled parts that still stand in for a vanilla prop (verdict B1'': "let me know if they are
# placeholders"); the builder prints their tris. (part-name prefixes, what, why)
PLACEHOLDERS = (
    (("fp_ring", "lamp_ember"), "pool-terrace fire pit (stone ring + ember disc)",
     "vanilla CampgroundFireplace01-03 exist but sit behind no placeholder, so the dump yields no "
     "GUID (the Editor's sub-object picker can place CampgroundFireplace02 at surgery); ours glows "
     "at night (emissive ember)"),
    (("fp_pz", "lamp_pzember"), "slope-plaza fire pit (stone ring + ember disc)",
     "as the pool fire pit"),
    (("lamp_pm", "lamp_pc"), "wall lanterns on M's road face (4) and the porte-cochere's hanging "
     "lanterns (2, with rods)",
     "architectural light fixtures kept from revision 1 (the facade read, the emissive map); "
     "vanilla WallLightShortRangeRandom01 / RoofLightRandom01 could replace them at surgery"),
)


# ======================================================================================
# FINE-DETAIL PASS, step 3e (2026-10-12; module docstring; build/fine-detail-step3e-swanky.md).
# Everything below is used ONLY when the recipe is fine (build_l5 reads recipe.fine_detail);
# `lodge.py --no-fine` (fine_detail.effective: `legacy` restored) builds revision 4 unchanged.
# ======================================================================================

# M's road-face wall lanterns (revision 1): y, lens height; on the pier fronts (0.6 m proud of M_X1)
# except the one under the tower deck (revision 2: no pier there, on M's wall)
PM_YS = (-35.0, -12.0, 12.0, 35.0)
PM_LAMP_Z = 2.6


def pm_mount(y):
    """The wall point of M's road-face lantern at y (cs2_bldg.lantern kind "wall" `at`: on the pier
    / wall face at the lens height; the lens sat 0.22 m out). Revisions 1-4 used these numbers
    inline; the legacy build passes the same floats."""
    return ((M_X1 if DK_Y0 < y < DK_Y1 else M_X1 + 0.6), y, PM_LAMP_Z)


# the 4 lanterns -> WallLightShortRangeRandom01 (Richard H4): pivot ON the pier / wall face at the
# lens height, local +Z out of it = the road (yaw 0); same tuple form / frame as SUB_OBJECT_ROWS
FINE_ROWS = tuple(("WallLightShortRangeRandom01", *pm_mount(y), 0.0, 0, "M road face") for y in PM_YS)
FINE_SUB_OBJECTS = tuple((pf, VANILLA[pf]["guid"], to_game(x, y, z), _yaw_q(yaw), int(parent))
                         for pf, x, y, z, yaw, parent, _zone in FINE_ROWS)

# the 4 plaza racks -> dressing-kit rows, loadouts alternating along the slope edge (FS6: -Y pair
# V1 / V2, +Y pair V3 / V1). Identity rotation: the kit's long axis (prefab x) = game x = authoring
# y = these racks' `along` "Y"; its +Z front (the builder's side +1) = game +Z = authoring +X; the
# A-frame is symmetric under the 180 deg yaw, both sides carry gear. On the plaza slab (m_ParentMesh
# 0, gy = SLAB_T: the old racks stood on it through ski_rack_frame z0 = SLAB_T, so the kit frame =
# the removed frame vertex for vertex, no 2 cm drop). Accent colour: build report (the kit colour-set
# variants re-point these rows later; FS7 Parent colour dropped by Richard).
RACK_KIT = tuple((nm, AFRAME_RACKS[i % len(AFRAME_RACKS)]) for i, (nm, *_r) in enumerate(PZ_RACKS))
#: kit colour variants (build/kit-colour-variants.md): red, the Grand Lodge's deep-red slot 2 family (bronze /
#: deep red / copper VariationSet A)
KIT_COLOUR_SET = "red"
KIT_ROWS = tuple(
    kit_row(prop, to_game(x, y, SLAB_T), parent=0,
            note=f"replaces skirack_{nm} (ski_rack_frame 8 slots along {along} with gear at authoring ({x}, "
                 f"{y}) on the plaza slab {SLAB_T}); identity: kit +Z = authoring +X", colour_set=KIT_COLOUR_SET)
    for (nm, prop), (_n, x, y, along, _g) in zip(RACK_KIT, PZ_RACKS))
# F1 keep-outs (pre-shift authoring = the host frame): the plaza axis walk (the -X podium flight to
# the plaza's back edge: revision 4's door -> walk -> flight -> pool deck -> -X flight -> plaza), and
# each rack's ACCESS AISLES: RACK_AISLE m in front of and behind its footprint over its length (both
# faces carry gear), free of modelled parts, vanilla rows and other kit rows, over the plaza
PLAZA_WALKS = (("plaza axis walk", (PS_X0, PS_STEPS[0], SLAB_T + 0.02), (POOL_X0, PS_STEPS[1], SLAB_T + 2.2)),)
RACK_AISLE = 0.9

# the porte-cochere NAME BOARD (the lodge carried no lettered board; the shared sheet held one name
# slot for it, cs2_signset `l5.name` 5.00 x 0.80, FS14): "GRAND LODGE" (the in-game name,
# LocaleEN SkiLodgeGrand) on the front truss, its face centred on the tie-beam chord (4.80..5.20)
# facing the road, a glulam frame ring (cs2_bldg.sign_board, the school's / L2's board) on a
# glulam backing slab 5 mm off the chord face (closes the board from behind); the face leaves the
# host for the sign set (FS1: frame + backing stay). Under it 4.44 m clear over the drive.
NB_TEXT = "GRAND LODGE"
NB_W, NB_H = 5.00, 0.80
NB_Z0 = P_TIE - NB_H / 2                           # 4.60: face bottom
NB_FRAME, NB_T = 0.06, 0.08                        # sign_board ring width / face offset
NB_BACK = (GABLE_FACE_X + 0.005, GABLE_FACE_X + 0.065)   # backing slab x (the ring sits on its front)
NB_FACE = "sign_l5name_face"
NAME_LABEL = (NB_FACE, "display", {"draws": (("text", {"lines": (NB_TEXT,), "bg": (0.20, 0.14, 0.10)}),)})
SIGN_SET = SignSetSpec(prop="SkiSignSetGrand", letter="B", host="l5", owner=False,
                       faces=((NB_FACE, "l5.name"),))

# modelled stand-ins left after the pass (the lanterns on M's road face are vanilla rows now)
FINE_PLACEHOLDERS = (
    PLACEHOLDERS[0], PLACEHOLDERS[1],
    (("lamp_pc",), "the porte-cochere's hanging lanterns (2, with rods)",
     "vanilla has no pendant (RoofLightRandom01 is a ceiling dome, WallLightShortRange a wall mount); "
     "kept modelled, emissive; the rods are 8-seg round now"),
)


def name_board(K):
    """The porte-cochere name board parts (fine only): `sign_l5name_back` (a CLOSED backing slab: its
    front face is the board field the HOST shows inside the ring, because the face itself leaves the host
    for the sign set; with that face dropped the routed host showed the slab's inside through the ring,
    caught by G1 on the shipped mesh) + cs2_bldg.sign_board `sign_l5name_face` / `sign_l5name_frame`
    (ring, back faces dropped) on the slab's front plane."""
    from cs2_bldg import WallRef
    x0, x1 = NB_BACK
    P = K.box_part("sign_l5name_back", (x0, -NB_W / 2 - NB_FRAME, NB_Z0 - NB_FRAME),
                   (x1, NB_W / 2 + NB_FRAME, NB_Z0 + NB_H + NB_FRAME), "lux_glulam")
    P += K.sign_board("l5name", WallRef("+X", x1), 0.0, NB_Z0, NB_W, NB_H, t=NB_T, mat="lux_glulam",
                      frame=NB_FRAME)
    return P


def check_name_board(parts):
    """NB contract (fine): the board stands in front of the front truss (every board vertex at x >=
    the truss face), inside the porte-cochere's width (|y| <= the post lines), its bottom >= 4.4 m
    over the drive; the face is exactly the sheet kind's size, upright, facing the road; nothing
    modelled stands in front of it: rays from 1.5 m out at a 9 x 3 grid of the face meet the face
    first; and the HOST WITHOUT the face (what ships once the sign set takes it) is closed there: the same
    rays meet the backing slab's front face (front-facing, within NB_T + 0.01 behind the face). Returns a
    dict for the report."""
    from mathutils import Vector
    lo, hi = parts_bounds(parts, ("sign_l5name",))
    check(lo[0] >= GABLE_FACE_X - 1e-6 and max(-lo[1], hi[1]) <= 5.3 and lo[2] - DRIVE_T >= 4.4 - 1e-6,
          f"NB name board {lo} .. {hi}: not in front of the truss / inside the posts / 4.4 m over the drive")
    flo, fhi = parts_bounds(parts, (NB_FACE,))
    check(abs((fhi[1] - flo[1]) - NB_W) < 1e-6 and abs((fhi[2] - flo[2]) - NB_H) < 1e-6
          and abs(fhi[0] - flo[0]) < 1e-6, f"NB face {flo} .. {fhi} != {NB_W} x {NB_H} upright")
    bvh, owner = _bvh_of(parts)
    bad = []
    for i in range(9):
        y = flo[1] + 0.05 + (NB_W - 0.1) * i / 8
        for j in range(3):
            z = flo[2] + 0.05 + (NB_H - 0.1) * j / 2
            o = Vector((fhi[0] + 1.5, y, z))
            loc, _n, idx, _d = bvh.ray_cast(o, Vector((-1.0, 0.0, 0.0)), 2.0)
            if loc is None or owner[idx] != NB_FACE:
                bad.append((round(y, 2), round(z, 2), None if loc is None else owner[idx]))
    check(not bad, f"NB the name board's face is covered: {bad[:4]}")
    bvh2, owner2 = _bvh_of(parts, skip=(NB_FACE,))
    for i in range(9):
        y = flo[1] + 0.05 + (NB_W - 0.1) * i / 8
        for j in range(3):
            z = flo[2] + 0.05 + (NB_H - 0.1) * j / 2
            o = Vector((fhi[0] + 1.5, y, z))
            d = Vector((-1.0, 0.0, 0.0))
            loc, n_, idx, dist = bvh2.ray_cast(o, d, 2.0)
            if loc is None or owner2[idx] != "sign_l5name_back" or n_.dot(d) >= 0.0 \
                    or dist > 1.5 + NB_T + 0.01:
                bad.append(("host without the face", round(y, 2), round(z, 2),
                            None if loc is None else (owner2[idx], round(dist - 1.5, 3))))
    check(not bad, f"NB the routed host is open behind the name board: {bad[:4]}")
    return dict(face_x=round(float(flo[0]), 3), bottom=round(float(lo[2]), 3),
                clear_over_drive=round(float(lo[2] - DRIVE_T), 3))


def _host_arrays(parts):
    """(co (N, 3), tris (T, 3), names per tri) of the parts in world (pre-shift authoring) coords."""
    import numpy as np
    co, tris, names, n = [], [], [], 0
    for o, _m in parts:
        W = world_coords(o)
        co.append(W)
        for p in o.data.polygons:
            vs = list(p.vertices)
            for j in range(1, len(vs) - 1):
                tris.append((n + vs[0], n + vs[j], n + vs[j + 1]))
                names.append(o.name)
        n += len(W)
    return np.concatenate(co), np.asarray(tris, dtype=np.int64), names


def check_kit_rows_l5(parts, boxes, rows):
    """F1 on KIT_ROWS (cs2_kitrows.check_kit_rows, the step-3b / 3c contract, in this recipe's pre-shift
    authoring frame): CIDs staged, the replaced racks gone, each REAL kit rack (the shipped props/B FBX)
    standing on the plaza slab (floor face <= 1 cm under its foot and every footprint corner), the mesh
    lifted 1 cm cutting no modelled triangle, its AABB clear of every vanilla row box (`boxes` from
    check_sub_objects over `rows`), the other kit rows and the plaza walks, inside the lot; plus the
    ACCESS AISLES (RACK_AISLE m on both long sides, over the rack's length, 0.02..1.8 m above the slab:
    over the plaza, no modelled triangle, vanilla box or other kit row in them). Raises; returns a dict."""
    import numpy as np
    from mathutils import Vector
    from mathutils.bvhtree import BVHTree
    import cs2_kitrows as KR
    co, tris, names = _host_arrays(parts)
    specs = [{"prop": r["prefab"], "pos": (x, y, SLAB_T), "normal": (1.0, 0.0), "mount": "ground",
              "grade": False, "removed": (f"skirack_{nm}",), "was": f"skirack_{nm}"}
             for (nm, x, y, _a, _g), r in zip(PZ_RACKS, KIT_ROWS)]

    def to_host(G):
        G = np.asarray(G, dtype=np.float64)
        return np.stack([G[:, 2] - X_SHIFT, G[:, 0], G[:, 1]], axis=1)
    vboxes = [(f"{rows[i][0]}#{i}:{k}", lo, hi) for i, k, lo, hi in boxes]
    lot = (-HX - X_SHIFT, HX - X_SHIFT, -HY, HY)
    msg, rep = KR.check_kit_rows(specs, list(KIT_ROWS), co, tris, names, to_host,
                                 all_names=[o.name for o, _m in parts], keepouts=PLAZA_WALKS, vboxes=vboxes,
                                 lot=lot, code="F1")
    tree = BVHTree.FromPolygons(co.tolist(), tris.tolist(), all_triangles=True)
    kab = [(np.array(r_["aabb_host"][0]), np.array(r_["aabb_host"][1])) for r_ in rep["rows"]]
    aisles, bad = [], []
    for i, (lo, hi) in enumerate(kab):
        for side, (xa, xb) in (("front", (hi[0], hi[0] + RACK_AISLE)), ("back", (lo[0] - RACK_AISLE, lo[0]))):
            alo = np.array([xa, lo[1], SLAB_T + 0.02])
            ahi = np.array([xb, hi[1], SLAB_T + 1.8])
            V, F = _box_poly(tuple(alo), tuple(ahi))
            bb = BVHTree.FromPolygons([Vector(v) for v in V], [list(f) for f in F])
            hit = sorted({names[a] for a, _b in tree.overlap(bb)})
            hit += [lab for lab, vlo, vhi in vboxes
                    if (np.asarray(vlo) < ahi).all() and (np.asarray(vhi) > alo).all()]
            hit += [f"kit #{j}" for j, (olo, ohi) in enumerate(kab)
                    if j != i and (olo < ahi).all() and (ohi > alo).all()]
            for x_, y_ in ((alo[0] + 0.02, alo[1] + 0.02), (ahi[0] - 0.02, alo[1] + 0.02),
                           (alo[0] + 0.02, ahi[1] - 0.02), (ahi[0] - 0.02, ahi[1] - 0.02)):
                h = tree.ray_cast(Vector((float(x_), float(y_), SLAB_T + 0.02)), Vector((0.0, 0.0, -1.0)), 0.05)
                if h[0] is None or abs(h[3] - 0.02) > 1e-4 or not names[h[2]].startswith("deck_s"):
                    hit.append(f"no plaza under ({x_:.2f}, {y_:.2f})")
            if hit:
                bad.append((KIT_ROWS[i]["prefab"], side, hit[:3]))
            aisles.append(side)
    check(not bad, f"F1 kit rack access aisles blocked: {bad[:4]}")
    return dict(F1=msg.get("F1"), aisles=len(aisles), rows=rep["rows"])


# ======================================================================================
# L5 revision-1 helpers (recipe-local; each one is listed as a kit need in the report)
# ======================================================================================

def _u(side, a, b):
    """World along-wall interval [a, b] (y on an X wall, x on a Y wall) -> (u0, u1) on a
    WallRef of `side` (+X u = y, -X u = -y, +Y u = -x, -Y u = x)."""
    a, b = min(a, b), max(a, b)
    return (a, b) if side in ("+X", "-Y") else (-b, -a)


def window_at(name, side, plane, s, win, room=None):
    """cs2_bldg.window_bay centred on the world along-coordinate `s` of the wall (side,
    plane); the room defaults to the window name (L5 regroups rooms by zone afterwards)."""
    K = _kit()
    from cs2_bldg import WallRef
    u0, u1 = _u(side, s - win.w / 2, s + win.w / 2)
    return K.window_bay(name, WallRef(side, plane), u0, u1, win, room=room or name)


def curtain_at(name, side, plane, s0, s1, z0=0.6, z1=4.0, grid=1.5, transom=1.7):
    """cs2_bldg.curtain_wall over the world along-interval [s0, s1] (GF glazing)."""
    K = _kit()
    from cs2_bldg import WallRef
    u0, u1 = _u(side, s0, s1)
    return K.curtain_wall(name, WallRef(side, plane), u0, u1, z0, z1, grid_u=grid,
                          transom_at=transom, room=name, per_storey_rooms=False)


def wall_pier(name, side, plane, s, z0, z1, mat, w=2.0, proud=0.6):
    """Stone pier `pier_<name>` (+ `plinthcap_<name>`) standing on the wall face, w along
    the wall, `proud` out, its buried back face dropped (no part inside the wall)."""
    sg = 1.0 if side[0] == "+" else -1.0
    a, b = sorted((plane, plane + sg * proud))
    back = ("-" if sg > 0 else "+") + side[1]
    if side[1] == "X":
        lo, hi = (a, s - w / 2, z0), (b, s + w / 2, z1)
    else:
        lo, hi = (s - w / 2, a, z0), (s + w / 2, b, z1)
    return pier_box(name, lo, hi, mat, drop=(back,))


def balcony_lite(name, wall, u0, u1, z, depth=1.4, h=1.05, t=0.06, struts=True):
    """A Peak 7 balcony at ~60 tris (the kit balcony's posts, double rails and knee braces
    cost ~200, and L5 carries ~30 of them): `balc_<name>` deck slab 0.18 (top at z, back
    face dropped), `balcwall_<name>` = front + two side baluster panels (t thick, h tall,
    `lux_balrail` vertical boards; bottoms on the slab and the side-panel ends buried in
    the front panel dropped), `bracket_<name>` = two diagonal glulam struts under the slab.
    LOD: struts go at LOD1 (bracket_), slab and panels at LOD2 (balc_ + recipe lod2_extra)."""
    K = _kit()
    P = K.wall_box(f"balc_{name}", wall, u0, u1, z - 0.18, z, 0.0, depth, "deck")
    g = K._Geo()
    lo, hi = K._wall_aabb(wall, u0, u1, z, z + h, depth - t, depth)
    g.aabb(lo, hi, ("-Z",))
    keys = K._wall_keys(wall, ("back", "front")) + ("-Z",)
    for ua, ub in ((u0, u0 + t), (u1 - t, u1)):
        lo, hi = K._wall_aabb(wall, ua, ub, z, z + h, 0.0, depth - t)
        g.aabb(lo, hi, keys)
    P.append((g.finish(f"balcwall_{name}"), "lux_balrail"))
    if struts:
        sg = K._Geo()
        for uu in (u0 + 0.35, u1 - 0.35):
            a = K.wall_point(wall, uu, z - 1.05, 0.02)
            b = K.wall_point(wall, uu, z - 0.22, depth - 0.30)
            K._beam_into(sg, a, b, 0.14, 0.14)
        P.append((sg.finish(f"bracket_{name}"), "lux_glulam"))
    return P


def balcony_stack(name, side, plane, s, floors, win_off=2.6):
    """Peak 7 balcony column on a flat wall: per floor a 2-light balcony door 1.6 x 2.3 +
    a 3.4 x 1.4 balcony on the bay centre `s` and a 2-light window either side at
    s +- win_off."""
    from cs2_bldg import WallRef, WinSpec
    wall = WallRef(side, plane)
    P = []
    for fl, zf in floors:
        P += window_at(f"{name}d{fl}", side, plane, s, WinSpec(1.6, 2.3, zf + 0.12, lights=2))
        u0, u1 = _u(side, s - 1.7, s + 1.7)
        P += balcony_lite(f"{name}{fl}", wall, u0, u1, zf + 0.10)
        for j, d in enumerate((-win_off, win_off)):
            P += window_at(f"{name}w{fl}{j}", side, plane, s + d,
                           WinSpec(1.4, 1.8, zf + 0.8, lights=2))
    return P


def window_column(name, side, plane, s, floors, gf=True):
    """One window per floor at the along-coordinate `s`: GF 2.0 x 2.4 (2 lights, sill 1.0)
    when `gf`, F2..F4 1.4 x 1.8 (2 lights, sill floor + 0.8)."""
    from cs2_bldg import WinSpec
    P = []
    if gf:
        P += window_at(f"{name}g", side, plane, s, WinSpec(2.0, 2.4, 1.0, lights=2))
    for fl, zf in floors:
        P += window_at(f"{name}u{fl}", side, plane, s, WinSpec(1.4, 1.8, zf + 0.8, lights=2))
    return P


def gable_pair(name, side, plane, s, z=Z_WALL + 0.45):
    """Two small gable windows either side of a king-post truss's post (the post would
    otherwise stand across a single centred window)."""
    from cs2_bldg import WinSpec
    P = []
    for j, d in enumerate((-0.62, 0.62)):
        P += window_at(f"{name}{j}", side, plane, s + d, WinSpec(0.5, 1.1, z, lights=1))
    return P


def cross_gable(name, side, plane, c, z0, meet, *, wall_mat, glulam, gf_mat=None,
                floors=UPPER, brackets=False):
    """
    Peak 7 projecting bay under a cross gable, over a balcony stack. The bay box stands
    XG_PROJ proud of the host wall (side, plane), XG_HALF either side of the along-coordinate
    c, from z0 to the roof wall line Z_WALL; its back face is KEPT so cull_hidden removes it
    and the host wall strip behind it by contact (the host is bisected at c +- XG_HALF).
    gf_mat: a stone GF box from grade as a ground part `wall_gx<name>` (z0 must be 0); else
    the bay starts at z0 (F2) on two curved glulam brackets when `brackets`. Gable roof
    (45 deg, ridge XG_RIDGE, overhang 0.5, rake 0.8) from the front back to `meet` (where the
    host roof's top plane reaches the xg ridge), timber-shake gable front, the kit king-post
    truss 0.22 proud, per floor a balcony door + 3.4 x 1.4 balcony + a 1-light window either
    side, two gable windows. Returns (parts, front plane).
    """
    K = _kit()
    from cs2_bldg import RoofSpec, WallRef, WinSpec
    sg = 1.0 if side[0] == "+" else -1.0
    front = plane + sg * XG_PROJ
    axn = side[1]                               # wall-normal axis = the xg ridge axis
    a, b = sorted((plane, front))

    def rect(za, zb):
        if axn == "X":
            return (a, c - XG_HALF, za), (b, c + XG_HALF, zb)
        return (c - XG_HALF, a, za), (c + XG_HALF, b, zb)
    P = []
    if gf_mat is not None:
        if abs(z0) > 1e-9:
            raise ValueError(f"cross_gable {name}: a stone GF starts at grade")
        lo, hi = rect(0.0, Z_F[1])
        P += K.box_part(f"wall_gx{name}", lo, hi, gf_mat, drop=("+Z",), ground=True)
        lo, hi = rect(Z_F[1], Z_WALL)
        P += K.box_part(f"wall_xg{name}", lo, hi, wall_mat, drop=("+Z", "-Z"))
    else:
        lo, hi = rect(z0, Z_WALL)
        P += K.box_part(f"wall_xg{name}", lo, hi, wall_mat, drop=("+Z",))
    r0, r1 = (meet + 0.8, front) if sg > 0 else (front, meet - 0.8)
    if axn == "X":
        spec = RoofSpec("gable", r0, r1, c - XG_HALF, c + XG_HALF, Z_WALL, 45.0, axis="X",
                        overhang=0.5, rake_over=0.8, thick=ROOF_T, mat="lux_slate",
                        trim="lux_trim")
    else:
        spec = RoofSpec("gable", c - XG_HALF, c + XG_HALF, r0, r1, Z_WALL, 45.0, axis="Y",
                        overhang=0.5, rake_over=0.8, thick=ROOF_T, mat="lux_slate",
                        trim="lux_trim")
    P += K.gable_roof(f"xg{name}", spec, walls=False, ridge_cap=(0.40, 0.12))
    g0, g1 = (front - 0.3, front) if sg > 0 else (front, front + 0.3)
    P += K.prism_gable(f"xg{name}f", axn, g0, g1, c - XG_HALF, c + XG_HALF, Z_WALL,
                       XG_RIDGE - 0.02, mat="timber_shake")
    uc = c if side in ("+X", "-Y") else -c
    wall = WallRef(side, front, uc - XG_HALF, uc + XG_HALF)
    P += K.truss_kingpost(f"xg{name}", wall, 2 * XG_HALF - 0.4, Z_WALL, 45.0, member=0.30,
                          braces="curved", d=0.22, mat=glulam)
    if gf_mat is not None:
        P += window_at(f"xg{name}gf", side, front, c, WinSpec(2.0, 2.4, 1.0, lights=2))
    for fl, zf in floors:
        P += window_at(f"xg{name}d{fl}", side, front, c, WinSpec(1.6, 2.3, zf + 0.12, lights=2))
        u0, u1 = _u(side, c - 1.7, c + 1.7)
        P += balcony_lite(f"xg{name}{fl}", wall, u0, u1, zf + 0.10)
        for j, d in enumerate((-2.35, 2.35)):
            P += window_at(f"xg{name}w{fl}{j}", side, front, c + d,
                           WinSpec(0.8, 1.8, zf + 0.8, lights=1))
    P += gable_pair(f"xg{name}g", side, front, c)
    if brackets:
        for j, d in enumerate((-2.4, 2.4)):
            at = (plane, c + d, z0 - 0.05) if axn == "X" else (c + d, plane, z0 - 0.05)
            P += K.bracket(f"xg{name}k{j}", at, side, span=XG_PROJ - 0.1, drop=1.2, t=0.22,
                           curved=True, mat=glulam)
    return P, front


def chaikin(pts, n=1):
    """Closed-polyline corner cutting (deterministic smoothing of the pool outline)."""
    for _ in range(n):
        out = []
        for i in range(len(pts)):
            a, b = pts[i], pts[(i + 1) % len(pts)]
            out.append((0.75 * a[0] + 0.25 * b[0], 0.75 * a[1] + 0.25 * b[1]))
            out.append((0.25 * a[0] + 0.75 * b[0], 0.25 * a[1] + 0.75 * b[1]))
        pts = out
    return pts


def offset_loop(pts, d):
    """Outward offset of a CCW closed polyline by d (mitred vertex normals, mitre <= 2 d)."""
    n = len(pts)
    out = []
    for i in range(n):
        (x0, y0), (x1, y1), (x2, y2) = pts[i - 1], pts[i], pts[(i + 1) % n]
        e0 = (x1 - x0, y1 - y0)
        e1 = (x2 - x1, y2 - y1)
        l0, l1 = math.hypot(*e0), math.hypot(*e1)
        n0 = (e0[1] / l0, -e0[0] / l0)
        n1 = (e1[1] / l1, -e1[0] / l1)
        mx, my = n0[0] + n1[0], n0[1] + n1[1]
        lm = math.hypot(mx, my)
        mx, my = mx / lm, my / lm
        s = d / max(mx * n0[0] + my * n0[1], 0.5)
        out.append((x1 + mx * s, y1 + my * s))
    return out


def signed_area(pts):
    return 0.5 * sum(pts[i][0] * pts[(i + 1) % len(pts)][1] - pts[(i + 1) % len(pts)][0] * pts[i][1]
                     for i in range(len(pts)))


def circle_loop(cx, cy, r, segs=16):
    return [(cx + r * math.cos(2 * math.pi * k / segs), cy + r * math.sin(2 * math.pi * k / segs))
            for k in range(segs)]


def deck_with_holes(name, rect, z, holes, mat):
    """`<name>` = ONE upward surface: the rectangle (x0, x1, y0, y1) at z minus the hole
    loops (bmesh triangle_fill treats nested loops as holes; measured 2026-10-11: a 20 x 20
    plane with two 16-gon holes filled to the exact area). Raises on a loose edge."""
    import bmesh
    K = _kit()
    x0, x1, y0, y1 = rect
    g = K._Geo()
    edges = []
    for loop in [[(x0, y0), (x1, y0), (x1, y1), (x0, y1)]] + [list(h) for h in holes]:
        vs = [g.v((x, y, z)) for x, y in loop]
        edges += [g.bm.edges.new((vs[i], vs[(i + 1) % len(vs)])) for i in range(len(vs))]
    bmesh.ops.triangle_fill(g.bm, use_beauty=True, use_dissolve=False, edges=edges,
                            normal=(0.0, 0.0, 1.0))
    for f in g.bm.faces:
        f.normal_update()
        if f.normal.z < 0.0:
            f.normal_flip()
    loose = [e for e in g.bm.edges if not e.link_faces]
    if loose or not g.bm.faces:
        g.bm.free()
        raise RuntimeError(f"{name}: triangle_fill left {len(loose)} loose edges")
    return [(g.finish(name), mat)]


def pool_basin(name, inner, outer, z_deck, z_top, z_water, cope_mat):
    """A basin set into the deck hole `outer`: `poolcope_<name>` coping (top ring at z_top
    between the CCW loops `inner` and `outer` + the outer edge from z_deck), `poolwall_<name>`
    tile wall from z_top down to the water, `water_<name>` the water surface at z_water
    (`lux_water`, polyfill-triangulated: the lagoon outline is concave)."""
    import bmesh
    K = _kit()
    n = len(inner)
    if len(outer) != n:
        raise ValueError(f"pool {name}: inner / outer loops differ in length")

    def cope(g):
        it = [g.v((x, y, z_top)) for x, y in inner]
        ot = [g.v((x, y, z_top)) for x, y in outer]
        ob = [g.v((x, y, z_deck)) for x, y in outer]
        for i in range(n):
            j = (i + 1) % n
            g.face([it[i], it[j], ot[j], ot[i]], (0.0, 0.0, 1.0))
            ex, ey = outer[j][0] - outer[i][0], outer[j][1] - outer[i][1]
            g.face([ob[i], ob[j], ot[j], ot[i]], (ey, -ex, 0.0))

    def wall(g):
        top = [g.v((x, y, z_top)) for x, y in inner]
        bot = [g.v((x, y, z_water)) for x, y in inner]
        for i in range(n):
            j = (i + 1) % n
            ex, ey = inner[j][0] - inner[i][0], inner[j][1] - inner[i][1]
            g.face([bot[i], bot[j], top[j], top[i]], (-ey, ex, 0.0))
    P = K._single(f"poolcope_{name}", cope, cope_mat)
    P += K._single(f"poolwall_{name}", wall, "lux_pooltile")
    g = K._Geo()
    g.poly([(x, y, z_water) for x, y in inner], (0.0, 0.0, 1.0))
    bmesh.ops.triangulate(g.bm, faces=g.bm.faces[:], quad_method="BEAUTY", ngon_method="BEAUTY")
    P.append((g.finish(f"water_{name}"), "lux_water"))
    return P


def pool_steps(name, side, s0, s1):
    """Three stone risers from the walkway (SLAB_T) up to just under the podium coping
    (tops 0.33 / 0.52 / 0.70, treads 0.35) against the podium edge `side` (+X, +Y, -Y or,
    revision 3, -X: the flight down to the slope plaza), across the along-interval [s0, s1].
    One part `stairblk_<name>`; the faces on the walkway and against the next riser / the
    podium wall are dropped."""
    K = _kit()
    tops = (0.33, 0.52, 0.70)
    boxes = []
    for k, top in enumerate(tops):              # k 0 = lowest, farthest out
        d0, d1 = (2 - k) * 0.35, (3 - k) * 0.35
        if side == "+X":
            boxes.append(((POOL_X1 + d0, s0, SLAB_T), (POOL_X1 + d1, s1, top), ("-Z", "-X")))
        elif side == "-X":
            boxes.append(((POOL_X0 - d1, s0, SLAB_T), (POOL_X0 - d0, s1, top), ("-Z", "+X")))
        elif side == "+Y":
            boxes.append(((s0, POOL_Y1 + d0, SLAB_T), (s1, POOL_Y1 + d1, top), ("-Z", "-Y")))
        elif side == "-Y":
            boxes.append(((s0, POOL_Y0 - d1, SLAB_T), (s1, POOL_Y0 - d0, top), ("-Z", "+Y")))
        else:
            raise ValueError(side)
    return K.boxes_part(f"stairblk_{name}", boxes, "concrete")


def pool_terrace(stone):
    """The Peak 7 pool deck (verdict B1 "pools are a good addition"; photos `breckenridge
    pools.jpg`, `Grand-Lodge-on-Peak-7-*.jpg`): a raised paver deck (top PD) on stone
    retaining walls (ground part `wall_gpool`, outer faces only: the coping ring and the
    deck close it) with a coping ring `poolcap_rim`, a lagoon pool and two raised stone hot
    tubs with coping (water BELOW the deck top), a fire pit, 5 flights of steps (revision 3:
    the fifth, `stairblk_w`, on the -X face down to the slope plaza) and grade walkways to M
    and the wings. Revision 3 (verdict B1''): the 10 loungers, 4 umbrellas and 6 lamp posts
    are vanilla sub-objects now (DeckChairRandom01, ParasolRandom01, LightpoleParkRandom01 in
    SUB_OBJECT_ROWS, at the revision-1 spots). Returns (parts, info) with the contract numbers
    the builder asserts."""
    K = _kit()
    P = []
    x0, x1, y0, y1 = POOL_X0, POOL_X1, POOL_Y0, POOL_Y1

    def walls(g):
        c = [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]
        bot = [g.v((x, y, 0.0)) for x, y in c]
        top = [g.v((x, y, PD)) for x, y in c]
        cx, cy = 0.5 * (x0 + x1), 0.5 * (y0 + y1)
        for i in range(4):
            j = (i + 1) % 4
            mx = 0.5 * (c[i][0] + c[j][0]) - cx
            my = 0.5 * (c[i][1] + c[j][1]) - cy
            g.face([bot[i], bot[j], top[j], top[i]], (mx, my, 0.0))
    P += K._single("wall_gpool", walls, stone)
    o, w, hcap = 0.05, POOL_CAP_W, POOL_CAP_H
    xa, xb, ya, yb = x0 - o, x1 + o, y0 - o, y1 + o
    ring = [((xa, ya, PD), (xb, ya + w, PD + hcap), ()),
            ((xa, yb - w, PD), (xb, yb, PD + hcap), ()),
            ((xa, ya + w, PD), (xa + w, yb - w, PD + hcap), ("-Y", "+Y")),
            ((xb - w, ya + w, PD), (xb, yb - w, PD + hcap), ("-Y", "+Y"))]
    P += K.boxes_part("poolcap_rim", ring, "concrete")
    deck_rect = (xa + w, xb - w, ya + w, yb - w)
    main_in = chaikin(list(MAIN_POOL_CTRL), 1)
    if signed_area(main_in) <= 0.0:
        raise RuntimeError("pool outline must be counter-clockwise")
    main_out = offset_loop(main_in, COPE_W)
    holes = [main_out]
    tubs = []
    for cx, cy in TUBS:
        tubs.append((circle_loop(cx, cy, TUB_R_IN), circle_loop(cx, cy, TUB_R_OUT)))
        holes.append(tubs[-1][1])
    P += deck_with_holes("deck_pool", deck_rect, PD, holes, "lux_paver")
    P += pool_basin("main", main_in, main_out, PD, MAIN_TOP, MAIN_WATER, "concrete")
    for k, (t_in, t_out) in enumerate(tubs):
        P += pool_basin(f"t{k}", t_in, t_out, PD, TUB_TOP, TUB_WATER, stone)
    # revision 3: loungers, umbrellas and lamp posts are vanilla sub-objects (SUB_OBJECT_ROWS);
    # the fire pit stays modelled (a PLACEHOLDER)
    fx, fy = FIREPIT
    P += ring_part("fp_ring", fx, fy, PD, PD + 0.45, 1.4, 1.0, stone)
    P += disc_part("lamp_ember", fx, fy, PD + 0.25, 1.0, "lux_ember")
    P += pool_steps("e0", "+X", -11.0, -7.0)
    P += pool_steps("e1", "+X", 9.0, 13.0)
    # revision 4: the +-Y flights face the wing terrace doors (were x -23..-19), and a +X flight
    # on the plaza axis faces M's terrace door (door -> walk -> podium -> -X flight -> plaza)
    P += pool_steps("n", "+Y", XG_WING - NS_STEPS_HALF, XG_WING + NS_STEPS_HALF)
    P += pool_steps("s", "-Y", XG_WING - NS_STEPS_HALF, XG_WING + NS_STEPS_HALF)
    P += pool_steps("e2", "+X", -E2_HALF, E2_HALF)
    P += pool_steps("w", "-X", PS_STEPS[0], PS_STEPS[1])      # revision 3: down to the plaza
    # grade walkways (paver slabs, lifted) between the podium, M and the wings
    P += K.box_part("deck_w0", (x1, -22.0, LIFT), (M_X0, 22.0, SLAB_T), "lux_paver")
    P += K.box_part("deck_w1", (W_X0, y1, LIFT), (x1, 22.0, SLAB_T), "lux_paver")
    P += K.box_part("deck_w2", (W_X0, -22.0, LIFT), (x1, y0, SLAB_T), "lux_paver")
    info = dict(deck_rect=deck_rect, holes=holes, tubs=tubs, main_in=main_in)
    return P, info


def l5_zone(x, y, z, axis):
    """Room tag of a pane group at centre (x, y, z) on a wall whose normal is along `axis`.
    Hall: hlo / hup / hgab by height. Elsewhere the floor (GF f0 .. roof storey f4, by the
    storey lines) and a column class from the 8 m bay grid along the wall (4 classes on
    F0-F3, 2 in the roof): neighbouring bays and stacked floors never share a tag, the same
    tag recurs only >= 24 m apart (or across the building), so every floor carries several
    independently lit rooms at night. 3 + 4 x 4 + 2 = 21 tags <= the 23-id pool."""
    if x > H_X0 + 0.3 and abs(y) < H_Y + 0.6:
        return "hgab" if z >= H_EAVE else ("hup" if z >= Z_F[1] else "hlo")
    fl = sum(1 for zl in STOREY_LINES if z >= zl)
    s = y if axis == "X" else x
    n = 4 if fl < 4 else 2
    return f"f{fl}{'abcd'[int(math.floor(s / BAY)) % n]}"


def retag_rooms_by_zone(parts):
    """Group the panes by their builder room (one window / curtain / dormer), place each
    group by its pane centroid and wall axis with l5_zone, rename through retag_rooms (C13
    <= 23 tags, one id each). Returns ({tag: pane count}, sorted tags)."""
    import numpy as np
    groups = {}
    for o, _m in parts:
        if o.name.startswith("win_"):
            groups.setdefault(o.name[4:].split("_", 1)[0], []).append(o)
    tag_of = {}
    for room in sorted(groups):
        objs = groups[room]
        W = np.concatenate([world_coords(o) for o in objs])
        c = W.mean(0)
        o0 = objs[0]
        nrm = np.array(o0.matrix_basis, dtype=np.float64)[:3, :3] @ np.array(o0.data.polygons[0].normal)
        tag_of[room] = l5_zone(float(c[0]), float(c[1]), float(c[2]),
                               "X" if abs(nrm[0]) >= abs(nrm[1]) else "Y")
    tags = retag_rooms(parts, lambda r: tag_of[r])
    count = {}
    for o, _m in parts:
        if o.name.startswith("win_"):
            t = o.name[4:].split("_", 1)[0]
            count[t] = count.get(t, 0) + 1
    return dict(sorted(count.items())), tags


def l5_storey_lines(xc, yc):
    """Storey lines of the mass a pane belongs to (C8)."""
    if xc > H_X0 + 0.5 and abs(yc) <= H_Y + 0.6:
        return (Z_F[1], H_EAVE)                    # great hall (transom 4.5, eave 9.0)
    return STOREY_LINES


# ======================================================================================
# L5 revision-2 / 3 helpers (tower deck, slope plaza, forecourt, vanilla sub-object contracts,
# clock sightline, the X shift). Recipe-local; the kit parts they would want are listed as kit
# needs in build/lodges-swanky.md. Revision 3 removed the modelled dressing builders
# (raised, cafe_set, planter_tree, bollards; lounger, lamp_post): vanilla props replace them.
# ======================================================================================

def deck_rail(name, segs, z, h=DK_RAIL_H, t=0.06, post=0.16, every=2.6):
    """Peak 7 honey board balustrade on a deck edge. segs = ((a, b), ...) axis-aligned plan
    segments (the rail line, 0.03 inside the deck edge). `balcwall_<name>` panels
    (lux_balrail, t thick, z..z+h, bottoms dropped: they stand on the deck), `trim_<name>post`
    glulam posts post x post to z+h+0.10 at both ends and every <= `every`, `trim_<name>cap`
    a 0.14 x 0.06 cap rail on the panels (bottom dropped: it lies on the panel tops)."""
    K = _kit()
    panels, posts, caps = K._Geo(), K._Geo(), K._Geo()
    seen = set()
    for (xa, ya), (xb, yb) in segs:
        along_x = abs(yb - ya) < 1e-9
        check(along_x or abs(xb - xa) < 1e-9, f"deck_rail {name}: segment not axis-aligned")
        if along_x:
            a, b = sorted((xa, xb))
            panels.aabb((a, ya - t / 2, z), (b, ya + t / 2, z + h), ("-Z",))
            caps.aabb((a - post / 2, ya - 0.07, z + h), (b + post / 2, ya + 0.07, z + h + 0.06),
                      ("-Z",))
        else:
            a, b = sorted((ya, yb))
            panels.aabb((xa - t / 2, a, z), (xa + t / 2, b, z + h), ("-Z",))
            caps.aabb((xa - 0.07, a - post / 2, z + h), (xa + 0.07, b + post / 2, z + h + 0.06),
                      ("-Z",))
        n = max(1, int(math.ceil((b - a) / every - 1e-9)))
        for j in range(n + 1):
            s = a + (b - a) * j / n
            px, py = (s, ya) if along_x else (xa, s)
            key = (round(px, 3), round(py, 3))
            if key in seen:                     # a corner post shared by two segments
                continue
            seen.add(key)
            posts.aabb((px - post / 2, py - post / 2, z), (px + post / 2, py + post / 2,
                                                           z + h + 0.10), ("-Z",))
    return [(panels.finish(f"balcwall_{name}"), "lux_balrail"),
            (posts.finish(f"trim_{name}post"), "lux_glulam"),
            (caps.finish(f"trim_{name}cap"), "lux_glulam")]


def tower_deck(tower=True):
    """(c) "on the clock tower side, build an elevated deck and the clock tower is sort of the
    focal point" (verdict B1'): the deck one storey up (top DK_Z = F2 4.5, slab DK_T) between
    M's road face (x 6), the hall's +Y wall (y 9) and the road cross gable r1 (y 21), out to
    DK_X1 = 21 (3 m past the hall glass). `deck_tw` = four boxes around the tower base (one
    box with --no-tower), faces in contact with M, the hall and the tower dropped;
    `deckfascia_tw` on the open edges; five glulam posts (`col_tw*`) on stone piers
    (`pier_tw*` / `plinthcap_tw*`, from LIFT) under a front and a side beam (`beam_tw*`; the
    hall wall and M carry the other edges); four curved knee braces on the front posts; the
    honey board balustrade (deck_rail); a deck-level door into the tower (`entry_ctdk`).
    Revision 3 (verdict B1'' "the STAIRS should be to the RIGHT, so that cars can pull
    through the valet area"): the 3 m stair (`stair_tw*`, kit stair, tube rails) leaves from
    the right-hand edge (DK_Y1, +Y seen from the road) at x DK_STAIR_X and runs on +Y down to
    the walk at DK_STAIR_FOOT_Y; the front balustrade is closed; the bistro sets and lanterns
    are vanilla sub-objects (SUB_OBJECT_ROWS). Returns (parts, info)."""
    K = _kit()
    from cs2_bldg import WallRef
    STN, GLU = "lux_stone_tan", "lux_glulam"
    P = []
    x0, x1, y0, y1 = DK_X0, DK_X1, DK_Y0, DK_Y1
    zt, zb = DK_Z, DK_Z - DK_T
    hb = CT_BASE_W / 2
    tx0, tx1, ty0, ty1 = CT_C[0] - hb, CT_C[0] + hb, CT_C[1] - hb, CT_C[1] + hb
    if tower:
        # faces against M (-X), the hall wall (A1 -Y), the tower base and the neighbouring
        # boxes are dropped; B keeps +Y (the open edge past the r1 bay, behind the fascia)
        boxes = [((x0, y0, zb), (H_X1, ty0, zt), ("-X", "-Y", "+X", "+Y")),   # A1 along the hall
                 ((H_X1, y0, zb), (x1, ty0, zt), ("-X", "+Y")),           # A2 past its glass
                 ((x0, ty1, zb), (x1, y1, zt), ("-X", "-Y")),             # B along the r1 bay
                 ((x0, ty0, zb), (tx0, ty1, zt), ("-X", "-Y", "+Y", "+X")),   # C behind
                 ((tx1, ty0, zb), (x1, ty1, zt), ("-X", "-Y", "+Y"))]     # D in front
    else:
        boxes = [((x0, y0, zb), (x1, y1, zt), ("-X",))]
    P += K.boxes_part("deck_tw", boxes, "deck")
    P[-1][0]["tile_rot"] = 0                    # boards run along X (to the road)
    fz0, fz1 = zb - 0.12, zt - 0.02
    P += K.boxes_part("deckfascia_tw", [
        ((x1, y0 - 0.04, fz0), (x1 + 0.04, y1 + 0.04, fz1), ("-X",)),
        ((M_X1 + XG_PROJ, y1, fz0), (x1, y1 + 0.04, fz1), ("-Y",)),
        ((H_X1 + 1.1, y0 - 0.04, fz0), (x1, y0, fz1), ("+Y",))], "lux_trim")
    # supports: stone pier 1.0 (to 1.2 + cap 0.15), glulam post 0.40 to the beam bottom
    bw, bh = DK_BEAM
    bz = zb - bh / 2
    for k, (px, py) in enumerate(DK_POSTS):
        P += K.stone_pier(f"tw{k}", px, py, 1.0, 1.0, LIFT, 1.2, cap=True, ground=False, mat=STN)
        P += K.col(f"tw{k}", px, py, 1.35, zb - bh, size=0.40, mat=GLU)
    fx = DK_POSTS[2][0]
    P += K.beam("tw0", (fx, y0 + 0.2, bz), (fx, y1 - 0.2, bz), bw, bh, mat=GLU)        # front
    P += K.beam("tw1", (x0 + 0.05, DK_POSTS[0][1], bz), (fx - bw / 2, DK_POSTS[0][1], bz),
                bw, bh, mat=GLU)                                                     # side
    for k, (py, sy) in enumerate(((DK_POSTS[2][1], 1.0), (DK_POSTS[3][1], -1.0),
                                  (DK_POSTS[3][1], 1.0), (DK_POSTS[4][1], -1.0))):
        P += K.bracket(f"tw{k}", (fx, py + sy * 0.2, zb - bh), (0.0, sy, 0.0), span=1.0,
                       drop=1.0, t=0.22, curved=True, mat=GLU)
    # balustrade (revision 3): the whole front edge (no stair gap there any more), the r1
    # side with the stair gap, the open stretch past the hall pier
    s0, s1 = DK_STAIR_X - DK_STAIR_W / 2, DK_STAIR_X + DK_STAIR_W / 2
    check(x0 + XG_PROJ + 0.5 < s0 and s1 < x1 - 0.3,
          f"R3c stair band {s0:.2f}..{s1:.2f} leaves the deck's +Y edge")
    rx, ry1, ry0 = x1 - 0.03, y1 - 0.03, y0 + 0.03
    P += deck_rail("tw", [((rx, ry0), (rx, ry1)),
                          ((M_X1 + XG_PROJ + 0.08, ry1), (s0, ry1)), ((s1, ry1), (rx, ry1)),
                          ((H_X1 + 1.15, ry0), (rx, ry0))], zt)
    # the stair to the walk (kit stair; foot on the walk slab, closed stringer feet), run +Y
    P += K.stair("tw", (DK_STAIR_X, DK_STAIR_FOOT_Y, SLAB_T), (DK_STAIR_X, y1 + 0.02, zt),
                 w=DK_STAIR_W, rise_max=0.18, rail="tube", ground=False, tread_mat="deck",
                 stringer_mat=GLU)
    if tower:                                   # a door from the deck into the tower base
        P += K.entry("ctdk", WallRef("+X", tx1), CT_C[1], w=1.6, h=2.4, sidelights=0, lamps=0,
                     z=zt + 0.01, surround_mat=GLU)
    info = dict(top=zt, slab=(zb, zt), x=(x0, x1), y=(y0, y1), tower=(tx0, tx1, ty0, ty1),
                stair=(DK_STAIR_FOOT_Y, s0, s1))
    return P, info


def slope_plaza(fine=False):
    """(1) revision 3, "I wanted the PLAZA ON THE POOL SIDE" (verdict B1''): `deck_s0` the
    paver plaza along the slope face from PS_X0 (0.6 m inside the back lot edge) to the wing
    gable ends (W_X0), end to end (+-PS_Y); `deck_s1` the strip from the wing line to the
    podium's -X wall (|y| <= POOL_Y1), top only (its sides meet the plaza, the walkways and
    the podium), so the plaza, the courtyard walkways (deck_w*) and the podium steps
    (`stairblk_w`, pool_terrace) read as one paved terrace like Peak 7's pool plaza; four kit
    ski racks with real gear on the slope edge (z0 = the slab top, the kit's K17 keyword);
    the plaza fire pit (`fp_pz` + `lamp_pzember`, a PLACEHOLDER). Tables, chairs, parasols,
    heaters, benches, bins, pots, lamp posts and trees are vanilla sub-objects
    (SUB_OBJECT_ROWS). Fine-detail step 3e (`fine`): the racks are dressing-kit rows (KIT_ROWS),
    none is modelled. Returns (parts, info)."""
    K = _kit()
    P = []
    P += K.box_part("deck_s0", (PS_X0, -PS_Y, LIFT), (W_X0, PS_Y, SLAB_T), "lux_paver",
                    drop=("-Z",))
    P += K.box_part("deck_s1", (W_X0, POOL_Y0, LIFT), (POOL_X0, POOL_Y1, SLAB_T), "lux_paver",
                    drop=("-Z", "-X", "+X", "-Y", "+Y"))
    for nm, x, y, along, gear in (() if fine else PZ_RACKS):
        P += K.ski_rack_frame(nm, x, y, slots=8, mat="galv", along=along, skis=gear,
                              ski_mat="accent", gear=True, z0=SLAB_T)
    fx, fy = PZ_FIREPIT
    P += ring_part("fp_pz", fx, fy, SLAB_T, SLAB_T + 0.45, 1.2, 0.85, "lux_stone_tan")
    P += disc_part("lamp_pzember", fx, fy, SLAB_T + 0.25, 0.85, "lux_ember")
    info = dict(x=(PS_X0, POOL_X0), y=(-PS_Y, PS_Y), top=SLAB_T, racks=0 if fine else len(PZ_RACKS))
    return P, info


def forecourt():
    """(2) revision 3: the road side returns to a forecourt with a VALET LOOP (verdict B1''
    "so that cars can pull through the valet area"). `deck_e0` the drive (lux_drive, top
    DRIVE_T): the lane under the porte-cochere x LANE_X0..LANE_X1 from DRIVE_Y0 to DRIVE_Y1,
    the entry leg on the left (y DRIVE_Y0..ENTRY_Y1) and the open exit apron on the right
    (y ISLAND_Y1..DRIVE_Y1) out to RD_X1; `deck_e1` the walks (lux_paver, SLAB_T, a 4 cm
    curb over the drive): along the hall front, past it under the deck front to the stair
    foot, and the arcade under the tower deck around the tower base (the tower's two grade
    doors stand on it); `deck_e2` the raised island (ISLAND_T) in front of the canopy (the
    porte-cochere's front piers stand in it). All `deck_e*` (arrival parts, kept at LOD2).
    Faces against the hall's plinth ring, M, the tower base, the r1 bay and each other are
    dropped. Bollards, benches, a bin, lamp posts, pots and trees are vanilla sub-objects.
    Returns (parts, info)."""
    K = _kit()
    P = []
    check(ISLAND_Y1 <= DK_Y0 and DK_STAIR_FOOT_Y + DK_STAIR_LAND <= DRIVE_Y1,
          "R3b the exit apron must run past the whole deck and the stair landing")
    P += K.boxes_part("deck_e0", [
        ((LANE_X0, DRIVE_Y0, LIFT), (LANE_X1, DRIVE_Y1, DRIVE_T), ("-Z", "-X", "+X")),  # lane
        ((LANE_X1, DRIVE_Y0, LIFT), (RD_X1, ENTRY_Y1, DRIVE_T), ("-Z", "-X", "+Y")),    # entry
        ((LANE_X1, ISLAND_Y1, LIFT), (RD_X1, DRIVE_Y1, DRIVE_T), ("-Z", "-X", "-Y"))],  # apron
        "lux_drive")
    hy = H_Y + 0.3                              # the hall's plinth ring
    tx0, tx1 = CT_C[0] - CT_BASE_W / 2, CT_C[0] + CT_BASE_W / 2
    ty0, ty1 = CT_C[1] - CT_BASE_W / 2, CT_C[1] + CT_BASE_W / 2
    walk = [((WALK_X0, DRIVE_Y0, LIFT), (LANE_X0, -hy, SLAB_T), ("-Z", "+Y")),          # A0
            ((WALK_X0, -hy, LIFT), (LANE_X0, hy, SLAB_T), ("-Z", "-X", "-Y", "+Y")),    # A1
            ((WALK_XB, hy, LIFT), (LANE_X0, DK_Y1, SLAB_T), ("-Z", "-X", "-Y", "+Y")),  # B1
            ((WALK_XB, DK_Y1, LIFT), (LANE_X0, DRIVE_Y1, SLAB_T), ("-Z", "-Y"))]        # B2
    # the arcade under the deck (between M, the hall's ring, the tower base and B1)
    walk += [((M_X1, hy, LIFT), (WALK_XB, ty0, SLAB_T), ("-Z", "-X", "-Y", "+X", "+Y")),  # C1
             ((M_X1, ty0, LIFT), (tx0, ty1, SLAB_T), ("-Z", "-X", "-Y", "+X", "+Y")),     # C2
             ((tx1, ty0, LIFT), (WALK_XB, ty1, SLAB_T), ("-Z", "-X", "-Y", "+X", "+Y")),  # C3
             ((M_X1, ty1, LIFT), (WALK_XB, DK_Y1, SLAB_T), ("-Z", "-X", "-Y", "+X"))]      # C4
    P += K.boxes_part("deck_e1", walk, "lux_paver")
    P += K.box_part("deck_e2", (LANE_X1, ISLAND_Y0, LIFT), (RD_X1, ISLAND_Y1, ISLAND_T),
                    "lux_paver", drop=("-Z",))
    info = dict(drive=(LANE_X0, RD_X1, DRIVE_Y0, DRIVE_Y1), lane=(LANE_X0, LANE_X1),
                island=(LANE_X1, RD_X1, ISLAND_Y0, ISLAND_Y1))
    return P, info


# --- revision 3: the vanilla sub-objects' geometry contracts (pre-shift frame) ----------------

def vanilla_boxes(row, envelope=False):
    """Authoring-frame AABBs [(kind, (lo, hi)), ...] of one SUB_OBJECT_ROWS entry from its
    VANILLA bounds turned by the row's yaw (Unity yaw: local (x, z) -> game (x c + z s,
    -x s + z c) -> authoring (X, Y) = (gz, gx); cs2_liftdress.subobj_box). Kinds: "solid"
    (the full bounds), "pole" + "canopy" (a parasol), "trunk" (a tree) and, with `envelope`,
    the tree's full union envelope."""
    pf, x, y, z, yaw = row[:5]
    lo, hi = VANILLA[pf]["bounds"]
    a = math.radians(yaw)
    c, s = math.cos(a), math.sin(a)

    def box(bl, bh):
        xs, ys = [], []
        for lx in (bl[0], bh[0]):
            for lz in (bl[2], bh[2]):
                xs.append(-lx * s + lz * c)
                ys.append(lx * c + lz * s)
        return ((x + min(xs), y + min(ys), z + bl[1]), (x + max(xs), y + max(ys), z + bh[1]))
    if pf == "ParasolRandom01":
        r = PARASOL_POLE
        return [("pole", box((-r, 0.0, -r), (r, PARASOL_TIER, r))),
                ("canopy", box((lo[0], PARASOL_TIER, lo[2]), hi))]
    if pf == "TreeWildRandom01":
        r, h = TREE_TRUNK
        out = [("trunk", box((-r, 0.0, -r), (r, h, r)))]
        if envelope:
            out.append(("envelope", box(lo, hi)))
        return out
    return [("solid", box(lo, hi))]


def _box_poly(lo, hi):
    """(verts, tris) of an axis-aligned box (12 triangles)."""
    (x0, y0, z0), (x1, y1, z1) = lo, hi
    V = [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0),
         (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]
    F = [(0, 2, 1), (0, 3, 2), (4, 5, 6), (4, 6, 7), (0, 1, 5), (0, 5, 4), (1, 2, 6), (1, 6, 5),
         (2, 3, 7), (2, 7, 6), (3, 0, 4), (3, 4, 7)]
    return V, F


def _parts_bvh(parts, boxes=()):
    """(BVHTree, owner per polygon, vertex array, owner per vertex) of the parts (world
    coordinates) plus labelled boxes [(label, (lo, hi)), ...] as 12-triangle solids."""
    import numpy as np
    from mathutils import Vector
    from mathutils.bvhtree import BVHTree
    verts, polys, owner, vown = [], [], [], []
    for o, _m in parts:
        W = world_coords(o)
        base = len(verts)
        verts += [Vector(v) for v in W]
        vown += [o.name] * len(W)
        for p in o.data.polygons:
            polys.append([base + i for i in p.vertices])
            owner.append(o.name)
    for label, (lo, hi) in boxes:
        V, F = _box_poly(lo, hi)
        base = len(verts)
        verts += [Vector(v) for v in V]
        vown += [label] * 8
        for f in F:
            polys.append([base + i for i in f])
            owner.append(label)
    VA = np.array([tuple(v) for v in verts], dtype=np.float64)
    return BVHTree.FromPolygons(verts, polys), owner, VA, vown


def mount_ray(row):
    """(origin, direction, expected surface normal sign) of a row's S1 surface probe: ground
    rows look down from 0.3 m above the pivot; "wall" rows (revision 4) look back at the wall
    from 0.3 m out along their facing (yaw), and the face they meet must face that way."""
    from mathutils import Vector
    pf, x, y, z, yaw = row[:5]
    if VANILLA[pf].get("mount", "ground") == "wall":
        a = math.radians(yaw)
        d = Vector((math.cos(a), math.sin(a), 0.0))
        return Vector((x, y, z)) + d * 0.3, -d, d
    return Vector((x, y, z + 0.3)), Vector((0.0, 0.0, -1.0)), Vector((0.0, 0.0, 1.0))


def check_sub_objects(parts, rows=SUB_OBJECT_ROWS, pad=SUB_PAD, lot=None):
    """Revision 3 contract on the vanilla sub-objects (pre-shift frame; raises naming the
    prop and the part):
      S1 surface: m_ParentMesh 0 -> the first face under the pivot is at the row's z (+-2 mm,
         facing up: our paving / deck); revision 4: a "wall" mount (VANILLA mount) meets its
         wall face at the pivot (+-2 mm, facing the row's yaw); -1 -> no part under the pivot
         above grade + 0.02;
      S2 clearance: every box (bottom 0.02 above its surface - a wall mount: 0.02 off its wall -
         grown by `pad` otherwise) meets no modelled part (no triangle overlap, no part vertex
         inside);
      S3 no two props intersect (a parasol's stand in its own table set's envelope excepted);
      S4 every box inside the lot (`lot` = (x0, x1, y0, y1) in the rows' frame; default L5's
         lot before the shift) (trees: the trunk).
    Returns {zone: count} and the box list for the corridor / sightline contracts."""
    import numpy as np
    from mathutils import Vector
    from mathutils.bvhtree import BVHTree
    if lot is None:
        lot = (-HX - X_SHIFT, HX - X_SHIFT, -HY, HY)
    bvh, owner, VA, vown = _parts_bvh(parts)
    bad, boxes, zones = [], [], {}
    down = Vector((0.0, 0.0, -1.0))
    for i, row in enumerate(rows):
        pf, x, y, z, yaw, parent, zone = row
        check(pf in VANILLA, f"S0 {pf} has no VANILLA entry")
        wall = VANILLA[pf].get("mount", "ground") == "wall"
        zones[zone] = zones.get(zone, 0) + 1
        if parent == 0:
            o, dvec, nwant = mount_ray(row)
            loc, nrm, idx, dist = bvh.ray_cast(o, dvec, 0.6)
            if loc is None or abs(dist - 0.3) > 2e-3 or nrm.dot(nwant) < 0.9:
                bad.append(("S1 surface", pf, (x, y, z),
                            None if loc is None else (round(dist, 3), owner[idx])))
        else:
            check(abs(z) < 1e-9 and parent == -1 and not wall,
                  f"S1 {pf} at {(x, y)}: parent {parent}, z {z}")
            loc, _n, idx, _d = bvh.ray_cast(Vector((x, y, 25.0)), down, 25.0 - 0.02)
            if loc is not None:
                bad.append(("S1 grade", pf, (x, y), owner[idx]))
        for kind, (lo, hi) in vanilla_boxes(row):
            boxes.append((i, kind, lo, hi))
            if wall:                            # off the wall by 0.02, pad on the other sides
                a = math.radians(yaw)
                dv = (math.cos(a), math.sin(a))
                lo2 = [lo[0] - pad, lo[1] - pad, lo[2] - pad]
                hi2 = [hi[0] + pad, hi[1] + pad, hi[2] + pad]
                for k in (0, 1):
                    if dv[k] > 0.5:
                        lo2[k] = lo[k] + 0.02
                    elif dv[k] < -0.5:
                        hi2[k] = hi[k] - 0.02
                lo2, hi2 = tuple(lo2), tuple(hi2)
            else:
                lo2 = (lo[0] - pad, lo[1] - pad, max(lo[2], z) + 0.02)
                hi2 = (hi[0] + pad, hi[1] + pad, hi[2])
            V, F = _box_poly(lo2, hi2)
            bb = BVHTree.FromPolygons([Vector(v) for v in V], [list(f) for f in F])
            names = {owner[a] for a, _b in bvh.overlap(bb)}
            inside = np.nonzero((VA[:, 0] > lo2[0]) & (VA[:, 0] < hi2[0]) & (VA[:, 1] > lo2[1])
                                & (VA[:, 1] < hi2[1]) & (VA[:, 2] > lo2[2])
                                & (VA[:, 2] < hi2[2]))[0]
            names |= {vown[j] for j in inside[:50]}
            if names:
                bad.append(("S2 clearance", pf, (x, y, z), kind, sorted(names)[:4]))
            if kind in ("solid", "pole", "trunk", "canopy"):
                ok = (lo[0] >= lot[0] and hi[0] <= lot[1] and lo[1] >= lot[2] and hi[1] <= lot[3])
                if not ok:
                    bad.append(("S4 lot", pf, (x, y), kind))
    for a in range(len(boxes)):
        ia, ka, la, ha = boxes[a]
        for b in range(a + 1, len(boxes)):
            ib, kb, lb, hb = boxes[b]
            if ia == ib:
                continue
            if all(la[k] < hb[k] - 1e-6 and lb[k] < ha[k] - 1e-6 for k in range(3)):
                ra, rb = rows[ia], rows[ib]
                near = math.hypot(ra[1] - rb[1], ra[2] - rb[2]) <= PARASOL_OFFSET + 0.05
                tables = {ra[0], rb[0]} == {"ParasolRandom01", "TableSetCommercialRandom01"}
                if near and tables and "pole" in (ka, kb):
                    continue                    # a parasol's stand beside its own table set
                bad.append(("S3 overlap", ra[0], ra[1:3], ka, rb[0], rb[1:3], kb))
    check(not bad, f"R3 vanilla sub-objects: {len(bad)} violation(s): {bad[:6]}")
    return zones, boxes


def check_drive_corridor(parts, boxes, min_w=CORRIDOR_MIN):
    """(3) contract, verdict B1'' "so that cars can pull through the valet area": past the
    tower deck and the stair landing (y DK_Y0 .. DK_STAIR_FOOT_Y + DK_STAIR_LAND), rays from
    the walk curb (LANE_X0) toward the road at 0.2 / 1.0 / 2.0 / 3.0 / 3.5 m (a vehicle
    envelope over the drive) meet no part and no vanilla prop box for >= min_w m (to RD_X1 at
    most); the stair (every part) stays >= 1.0 m short of the curb. Also measures the lane
    under the porte-cochere (pier face to the island curb, info only: as built since
    revision 0). Returns (min width, y, z, blocker), lane width."""
    import numpy as np
    from mathutils import Vector
    bvh, owner, _VA, _vo = _parts_bvh(parts, [(f"sub:{rows_i}:{kind}", (lo, hi))
                                               for rows_i, kind, lo, hi in boxes])
    span = RD_X1 - LANE_X0
    worst = (1e9, None, None, None)
    for y in np.arange(DK_Y0, DK_STAIR_FOOT_Y + DK_STAIR_LAND + 1e-9, 0.25):
        for zz in (0.2, 1.0, 2.0, 3.0, 3.5):
            o = Vector((LANE_X0 + 0.01, float(y), zz))
            loc, _n, idx, d = bvh.ray_cast(o, Vector((1.0, 0.0, 0.0)), span - 0.01)
            w = (d + 0.01) if loc is not None else span
            if w < worst[0]:
                worst = (round(float(w), 3), round(float(y), 2), zz,
                         owner[idx] if loc is not None else "road edge")
    check(worst[0] >= min_w, f"R3b valet corridor past the deck only {worst[0]:.2f} m (< "
                             f"{min_w}) at y {worst[1]}, z {worst[2]}: {worst[3]}")
    slo, shi = parts_bounds(parts, ("stair_tw", "post_stairtw", "rail_stairtw", "kick_stairtw"))
    check(shi[0] <= LANE_X0 - 1.0 and slo[1] >= DK_Y1 - 0.15,       # top rail posts at the edge
          f"R3c the stair ({slo[0]:.2f}..{shi[0]:.2f}, y {slo[1]:.2f}..{shi[1]:.2f}) is not on the "
          f"deck's right-hand edge clear of the drive (x <= {LANE_X0 - 1.0})")
    # the lane under the porte-cochere: narrowest pier-face-to-island gap at 0.2..3.5 m
    lane = 1e9
    xm = 0.5 * (LANE_X0 + LANE_X1)
    for y in np.arange(-P_Y, P_Y + 1e-9, 0.25):
        for zz in (0.3, 1.0, 2.0, 3.0, 3.5):
            o = Vector((xm, float(y), zz))
            la, _n, _i, da = bvh.ray_cast(o, Vector((-1.0, 0.0, 0.0)), 10.0)
            lb, _n, _i, db = bvh.ray_cast(o, Vector((1.0, 0.0, 0.0)), 10.0)
            lane = min(lane, (da if la is not None else 10.0) + (db if lb is not None else 10.0))
    return worst, round(float(lane), 3), (round(float(slo[0]), 3), round(float(shi[0]), 3),
                                          round(float(slo[1]), 3), round(float(shi[1]), 3))


def check_door_walks(parts, doors, boxes, rows=SUB_OBJECT_ROWS):
    """Revision 4 D4 ("aligned with the plaza walks"): every terrace door faces its flight up to
    the podium (DOOR_STEPS: the flight's centre on the door's along-coordinate +- 0.05 m) and the
    walk between them is free: rays from the door's face across its width (5) at 0.3 / 1.0 /
    1.8 m above the walk run to the flight's first riser without meeting a part or a vanilla
    prop box (`boxes` from check_sub_objects over `rows`). Returns {door: walk length m}."""
    from mathutils import Vector
    bvh, owner, _VA, _vo = _parts_bvh(parts, [(f"sub:{rows[i][0]}:{k}", (lo, hi))
                                              for i, k, lo, hi in boxes])
    out = {}
    for d in doors:
        ax, sg, al = _side_axes(d["side"])
        slo, shi = parts_bounds(parts, (DOOR_STEPS[d["tag"]],))
        sc = 0.5 * (float(slo[al]) + float(shi[al]))
        check(abs(sc - d["s"]) <= 0.05, f"D4 door {d['tag']}: its flight is centred at {sc:.2f}, "
                                        f"the door at {d['s']:.2f}")
        near = float(slo[ax]) if sg > 0 else float(shi[ax])
        length = (near - d["plane"]) * sg
        check(1.5 <= length <= 6.0, f"D4 door {d['tag']}: the flight is {length:.2f} m away")
        n = Vector((0.0, 0.0, 0.0))
        n[ax] = sg
        hits = []
        for i in range(5):
            a = d["s"] - d["w"] / 2 + 0.1 + (d["w"] - 0.2) * i / 4
            for dz in (0.3, 1.0, 1.8):
                o = Vector((0.0, 0.0, d["z0"] + dz))
                o[ax], o[al] = d["plane"] + sg * 0.02, a
                loc, _nn, idx, dist = bvh.ray_cast(o, n, length - 0.06)
                if loc is not None:
                    hits.append((round(a, 2), dz, owner[idx], round(dist, 2)))
        check(not hits, f"D4 door {d['tag']}: the walk to the flight is blocked: {hits[:4]}")
        out[d["tag"]] = round(length, 2)
    return out


def placeholder_tris(parts, placeholders=PLACEHOLDERS):
    """{what: (tris, part count)} of PLACEHOLDERS (the modelled stand-ins for vanilla props;
    fine-detail step 3e: FINE_PLACEHOLDERS)."""
    out = {}
    for prefixes, what, _why in placeholders:
        n, k = 0, 0
        for o, _m in parts:
            if o.name.startswith(tuple(prefixes)):
                k += 1
                n += sum(len(p.vertices) - 2 for p in o.data.polygons)
        out[what] = (n, k)
    return out


def clock_samples(parts, name="ct"):
    """{face part: (sample points, outward normal, centre)} of the four dials: the 24 fan
    triangles' centroids, the centre and 24 points at 92 % of the radius."""
    import numpy as np
    from mathutils import Vector
    out = {}
    for o, _m in parts:
        if not (o.name.startswith(f"clock_{name}") and o.name.endswith("_face")):
            continue
        W = world_coords(o)
        pts, n, area = [], Vector((0.0, 0.0, 0.0)), 0.0
        M3 = np.array(o.matrix_basis, dtype=np.float64)[:3, :3]
        cc = Vector(W.mean(0))
        for p in o.data.polygons:
            vs = [Vector(W[i]) for i in p.vertices]
            pts.append(sum(vs, Vector()) / len(vs))
            n += Vector(M3 @ np.array(p.normal)) * p.area
            far = sorted(vs, key=lambda v: (v - cc).length)[-2:]
            pts.append((far[0] + far[1]) * 0.5 * 0.92 + cc * 0.08)
        pts.append(cc)
        out[o.name] = (pts, n.normalized(), cc)
    return out


def check_clock_sightline(parts, name="ct", max_el=CLOCK_CLEAR_DEG, azimuths=CLOCK_AZIMUTHS,
                          boxes=()):
    """(b) contract: from every arrival azimuth (the preview views arrival_front / arrival_3q
    / road34left / arrival, authoring frame) and every camera elevation 0..max_el deg (1 deg
    steps), every sample of every dial that faces the camera (n . d > 0.05) sees the sky: no
    part (the cap, the stage, the main roofs) and (revision 3) no vanilla prop box `boxes`
    [(label, (lo, hi)), ...] (parasols, lamp posts, the trees' full growth envelopes) between
    it and a camera 2 km out. Raises naming the azimuth, elevation, dial and the blocking part.
    Returns {azimuth: highest elevation (deg, <= 80) up to which every facing dial is fully
    clear}."""
    from mathutils import Vector
    bvh, owner, _VA, _vo = _parts_bvh(parts, boxes)
    dials = clock_samples(parts, name)
    check(len(dials) == 4, f"clock sightline: {len(dials)} dials, want 4")
    clear = {}
    for az, (ax, ay) in azimuths:
        h = Vector((ax, ay, 0.0)).normalized()
        best = -1
        for el in range(0, 81):
            e = math.radians(el)
            d = h * math.cos(e) + Vector((0.0, 0.0, math.sin(e)))
            hits = []
            for nm, (pts, n, _c) in sorted(dials.items()):
                if n.dot(d) <= 0.05:
                    continue
                for p in pts:
                    loc, _nn, idx, _dd = bvh.ray_cast(p + n * 0.005, d, 2000.0)
                    if loc is not None:
                        hits.append((nm, owner[idx]))
                        break
            if hits:
                check(el > max_el, f"clock sightline: {az} at {el} deg, {hits[0][0]} is blocked "
                                   f"by {hits[0][1]} ({len(hits)} dial(s))")
                break
            best = el
        clear[az] = best
    return clear


def shift_x(parts, dx):
    """Move the whole build by dx along X (revision 2: the lot grew one cell toward the road
    and the build moves back so the plaza takes the new cell): identity parts get their mesh
    translated (the recipe's hygiene helpers and lodge.py's join expect identity parts to
    stay identity), the kit's transformed parts (cs2_kit doors, handrails) get their
    location moved; the view layer is updated so matrix_world is current."""
    import bpy
    from mathutils import Matrix
    T = Matrix.Translation((dx, 0.0, 0.0))
    for o, _m in parts:
        check(o.data.users == 1, f"{o.name}: shared mesh data, cannot shift in place")
        if _identity(o):
            o.data.transform(T)
            o.data.update()
        else:
            o.location.x += dx
    bpy.context.view_layer.update()
    return len(parts)


def build_l5(recipe, opts):
    """Parts of L5 revision 1 (custom massing). Order is fixed (determinism); every name
    unique; no set is ever iterated unsorted. Fine-detail step 3e: `recipe.fine_detail` (module
    docstring) swaps the racks / road-face lanterns for rows, adds the name board, fine finial /
    flues / rods; False = revision 4 vertex for vertex."""
    K = _kit()
    from cs2_bldg import RoofSpec, WallRef, WinSpec
    register_materials()
    fine = bool(getattr(recipe, "fine_detail", False))
    rows = SUB_OBJECT_ROWS + (FINE_ROWS if fine else ())
    STN, GLU = "lux_stone_tan", "lux_glulam"
    SID_LO, SID_HI = "lux_siding_red", "lux_siding_walnut"   # Peak 7 two-tone siding
    P = []

    # ---------------- M main bar ----------------------------------------------------------
    # cut planes on every storey: the hall (+-H_Y), the wing contacts (wing y0 / y1) and
    # the cross-gable bays, so the covered strips are culled by contact with the abutting
    # masses' kept faces
    cuts = sorted(set([-H_Y, H_Y] + [v for _t, y0, y1 in WINGS for v in (y0, y1)]
                      + [c + d for c in XG_ROAD + XG_COURT for d in (-XG_HALF, XG_HALF)]))
    P += K.storey("gm", M_X0, M_X1, M_Y0, M_Y1, 0.0, Z_F[1], STN, ground=True, drop=("+Z",))
    P += K.storey("mf2", M_X0, M_X1, M_Y0, M_Y1, Z_F[1], Z_F[2], SID_LO, drop=("-Z", "+Z"))
    P += K.storey("mf3", M_X0, M_X1, M_Y0, M_Y1, Z_F[2], Z_F[3], SID_HI, drop=("-Z", "+Z"),
                  cut_z=(H_EAVE,))
    P += K.storey("mf4", M_X0, M_X1, M_Y0, M_Y1, Z_F[3], Z_WALL, SID_HI, drop=("-Z", "+Z"))
    for o, _m in P[-4:]:
        K.bisect_part(o, [((0.0, y, 0.0), (0.0, 1.0, 0.0)) for y in cuts])
    mspec = RoofSpec("gable", M_X0, M_X1, M_Y0, M_Y1, Z_WALL, M_PITCH, axis="Y", overhang=M_OVER,
                     rake_over=M_OVER, ridge_at=M_RIDGE_X, thick=ROOF_T, mat="lux_slate",
                     trim="lux_trim")
    P += K.gable_roof("m", mspec, gutters=True, walls=True, wall_mat="timber_shake")
    # M gable ends: king-post trusses 0.25 proud (Peak 7's log / timber gables)
    for k, (side, plane) in enumerate((("-Y", M_Y0), ("+Y", M_Y1))):
        u0, u1 = _u(side, M_X0 + 0.3, M_X1 - 0.3)
        P += K.truss_kingpost(f"xgm{k}", WallRef(side, plane, u0, u1), (M_X1 - M_X0) - 0.6,
                              Z_WALL, M_PITCH, member=0.36, braces="curved", d=0.25, mat=GLU)

    # ---------------- WS / WN wings ---------------------------------------------------------
    wspecs = {}
    for tag, y0, y1 in WINGS:
        P += K.storey(f"gw{tag}", W_X0, W_X1, y0, y1, 0.0, Z_F[1], STN, ground=True,
                      drop=("+Z",))
        P += K.storey(f"w{tag}f2", W_X0, W_X1, y0, y1, Z_F[1], Z_F[2], SID_LO, drop=("-Z", "+Z"))
        P += K.storey(f"w{tag}f3", W_X0, W_X1, y0, y1, Z_F[2], Z_F[3], SID_HI, drop=("-Z", "+Z"))
        P += K.storey(f"w{tag}f4", W_X0, W_X1, y0, y1, Z_F[3], Z_WALL, SID_HI, drop=("-Z", "+Z"))
        for o, _m in P[-4:]:
            K.bisect_part(o, [((x, 0.0, 0.0), (1.0, 0.0, 0.0))
                              for x in (XG_WING - XG_HALF, XG_WING + XG_HALF)])
        # the roof runs into M's: its +X rake end sits where the wing ridge meets M's top
        # plane (W_MEET); the far gable end is the only gable wall (the near one would be
        # buried in M's attic)
        wspec = RoofSpec("gable", W_X0, W_MEET - M_OVER, y0, y1, Z_WALL, 45.0, axis="X",
                         overhang=M_OVER, rake_over=M_OVER, thick=ROOF_T, mat="lux_slate",
                         trim="lux_trim")
        P += K.gable_roof(f"w{tag}", wspec, gutters=True, walls=False)
        P += K.prism_gable(f"w{tag}end", "X", W_X0, W_X0 + 0.3, y0, y1, Z_WALL, W_RIDGE - 0.02,
                           mat="timber_shake")
        u0, u1 = _u("-X", y0 + 0.3, y1 - 0.3)
        P += K.truss_kingpost(f"xgend{tag}", WallRef("-X", W_X0, u0, u1), (y1 - y0) - 0.6, Z_WALL,
                              45.0, member=0.36, braces="curved", d=0.25, mat=GLU)
        wspecs[tag] = wspec

    # belt course between the stone GF and the timber floors, corner boards
    belt = []
    zb0, zb1 = Z_F[1] - 0.05, Z_F[1] + 0.20

    def belt_x(x, ya, yb, out):                 # on an X wall, `out` = +1 / -1
        a, b = sorted((x, x + 0.1 * out))
        belt.append(((a, ya, zb0), (b, yb, zb1), ("-X" if out > 0 else "+X",)))

    def belt_y(y, xa, xb, out):
        a, b = sorted((y, y + 0.1 * out))
        belt.append(((xa, a, zb0), (xb, b, zb1), ("-Y" if out > 0 else "+Y",)))
    for ya, yb in ((M_Y0, XG_ROAD[0] - XG_HALF), (XG_ROAD[0] + XG_HALF, -H_Y),
                   (H_Y, XG_ROAD[1] - XG_HALF), (XG_ROAD[1] + XG_HALF, M_Y1)):
        belt_x(M_X1, ya, yb, +1)
    belt_x(M_X0, WINGS[0][2], WINGS[1][1], -1)
    belt_y(M_Y0, M_X0, M_X1, -1)
    belt_y(M_Y1, M_X0, M_X1, +1)
    for tag, y0, y1 in WINGS:
        belt_x(W_X0, y0, y1, -1)
        belt_y(y0, W_X0, W_X1, -1)
        belt_y(y1, W_X0, W_X1, +1)
    P += K.boxes_part("trim_belt", belt, "lux_trim")
    P += corner_boards("corners", [(M_X1, M_Y0), (M_X1, M_Y1), (M_X0, M_Y0), (M_X0, M_Y1),
                                   (W_X0, -34.0), (W_X0, -22.0), (W_X0, 22.0), (W_X0, 34.0)],
                       Z_F[1] + 0.20, Z_WALL, mat="lux_trim")

    # ---------------- M road face (+X, x = 6) ----------------------------------------------
    for k, y in enumerate(PM_YS):
        if not (DK_Y0 < y < DK_Y1):     # revision 2: under the tower deck - no pier (its cap
            P += wall_pier(f"m{k}", "+X", M_X1, y, LIFT, Z_F[1], STN)          # met the deck)
        if not fine:                    # fine-detail step 3e: a WallLightShortRange row (FINE_ROWS)
            lp, _pt = K.lantern(f"pm{k}", pm_mount(y), "+X", kind="wall")
            P += lp
    for k, (s0, s1) in enumerate(((-33.9, -28.2), (-19.9, -13.1), (13.1, 19.9), (28.2, 33.9))):
        P += curtain_at(f"mgr{k}", "+X", M_X1, s0, s1)
    for k, yc in enumerate((-32.0, -16.0, 16.0, 32.0)):
        for j, d in enumerate((-2.0, 2.0)):
            P += window_column(f"mr{k}{j}", "+X", M_X1, yc + d, UPPER, gf=False)
    xg_fronts = []
    for k, yc in enumerate(XG_ROAD):
        ps, front = cross_gable(f"r{k}", "+X", M_X1, yc, 0.0, X_MEET_ROAD, wall_mat=SID_HI,
                                glulam=GLU, gf_mat=STN)
        P += ps
        xg_fronts.append(("+X", front, "m", "B"))

    # ---------------- M courtyard face (-X, x = -8): glazed GF between piers ------------
    for k, y in enumerate((-20.0, -12.0, -4.0, 4.0, 12.0, 20.0)):
        P += wall_pier(f"mc{k}", "-X", M_X0, y, SLAB_T, Z_F[1], STN)
    for k, (s0, s1) in enumerate(((-18.9, -13.1), (-10.9, -5.1), (-2.9, 2.9), (5.1, 10.9),
                                  (13.1, 18.9))):
        if k == 2:                      # revision 4: the terrace door bay (POOL_DOORS, below)
            continue
        P += curtain_at(f"mgc{k}", "-X", M_X0, s0, s1)
    P += balcony_stack("mcb0", "-X", M_X0, -8.0, UPPER)
    P += balcony_stack("mcb1", "-X", M_X0, 8.0, UPPER)
    for j, d in enumerate((-2.0, 2.0)):
        P += window_column(f"mc{j}", "-X", M_X0, d, UPPER, gf=False)
    for k, yc in enumerate(XG_COURT):
        ps, front = cross_gable(f"c{k}", "-X", M_X0, yc, Z_F[1], X_MEET_COURT, wall_mat=SID_HI,
                                glulam=GLU, brackets=True)
        P += ps
        xg_fronts.append(("-X", front, "m", "A"))

    # ---------------- M ends (y = -+36): window columns + a balcony stack, gable windows -
    for k, (side, plane) in enumerate((("-Y", M_Y0), ("+Y", M_Y1))):
        for j, x in enumerate((-5.5, 3.5)):
            P += window_column(f"me{k}{j}", side, plane, x, (), gf=True)
        P += balcony_stack(f"meb{k}", side, plane, M_RIDGE_X, UPPER, win_off=3.4)
        P += gable_pair(f"meg{k}", side, plane, M_RIDGE_X)

    # ---------------- wings: courtyard faces, outer faces, slope gable ends ---------------
    for tag, y0, y1 in WINGS:
        cside, cplane = ("+Y", y1) if y1 < 0 else ("-Y", y0)       # facing the courtyard
        oside, oplane = ("-Y", y0) if y1 < 0 else ("+Y", y1)       # facing out
        for k, x in enumerate((-29.0, -22.0, -14.0)):
            P += wall_pier(f"w{tag}{k}", cside, cplane, x, SLAB_T, Z_F[1], STN)
        for k, (s0, s1) in enumerate(((-27.9, -23.1), (-20.9, -15.1), (-12.9, -9.0))):
            if k == 1:                  # revision 4: the terrace door bay (POOL_DOORS, below)
                continue
            P += curtain_at(f"wgc{tag}{k}", cside, cplane, s0, s1)
        for j, x in enumerate((-27.8, -24.2, -12.6, -10.2)):
            P += window_column(f"wc{tag}{j}", cside, cplane, x, UPPER, gf=False)
        meet = (0.5 * (y0 + y1)) + (W_RIDGE - XG_RIDGE) * (1.0 if y1 < 0 else -1.0)
        ps, front = cross_gable(f"w{tag}", cside, cplane, XG_WING, Z_F[1], meet, wall_mat=SID_HI,
                                glulam=GLU, brackets=True)
        P += ps
        xg_fronts.append((cside, front, f"w{tag}", "B" if y1 < 0 else "A"))
        for j, x in enumerate((-27.0, -23.0, -15.0, -11.0)):
            P += window_column(f"wo{tag}{j}", oside, oplane, x, UPPER, gf=True)
        sy = -1.0 if y1 < 0 else 1.0
        P += K.chimney(f"w{tag}", -19.0, oplane + sy * 0.8, 2.4, 1.6, W_CHIM_TOP, base_z=LIFT,
                       shoulder=(3.4, 2.0, Z_F[1]), mat=STN)
        yc = 0.5 * (y0 + y1)
        for j, d in enumerate((-3.6, 3.6)):
            P += window_column(f"we{tag}{j}", "-X", W_X0, yc + d, (), gf=True)
        P += balcony_stack(f"web{tag}", "-X", W_X0, yc, UPPER, win_off=3.6)
        P += gable_pair(f"weg{tag}", "-X", W_X0, yc)

    # ---------------- revision 4 (b): French terrace doors onto the pool terrace -----------------
    # eighteenth look "there are no doors on the pool side of the building": in each wing's
    # courtyard face under its cross gable and in M's on the plaza axis, the curtain bay becomes a
    # glazed door wall: two sidelights, a transom light and the door in a REAL recessed opening
    # cut into the stone ground storey (threshold = the walk, SLAB_T)
    doors = []
    for tag, side, plane, s in POOL_DOORS:
        ps, door = terrace_door(tag, side, plane, s, SLAB_T, tag, frame_mat=GLU)
        P += ps
        half = DOOR_W / 2 + DOOR_FRAME
        b0, b1 = DOOR_BAYS[tag]
        P += curtain_at(f"{tag}sl0", side, plane, b0, s - half - DOOR_SIDE_GAP)
        P += curtain_at(f"{tag}sl1", side, plane, s + half + DOOR_SIDE_GAP, b1)
        P += curtain_at(f"{tag}tr", side, plane, s - half, s + half, z0=DOOR_TRANSOM_Z, z1=4.0,
                        grid=2.0)
        door["host"] = DOOR_HOSTS[tag]
        host = [o for o, _m in P if o.name == door["host"]]
        check(len(host) == 1, f"door {tag}: host part {door['host']} missing")
        cut_recess(host[0], side, plane, *door["rough_a"], *door["rough_z"], DOOR_SET)
        doors.append(door)

    # ---------------- dormers (additive), ridge chimneys ---------------------------------------
    dorms = [(mspec, "+X", y) for y in (-32.0, -16.0, 14.0, 32.0)] + \
            [(mspec, "-X", y) for y in (-8.0, 0.0, 8.0)]
    for tag, y0, y1 in WINGS:
        inner, outer = ("+Y", "-Y") if y1 < 0 else ("-Y", "+Y")
        dorms += [(wspecs[tag], inner, -26.5), (wspecs[tag], inner, -11.5),
                  (wspecs[tag], outer, -25.0)]
    for k, (spec, side, u) in enumerate(dorms):
        ps = K.dormer(f"d{k}", spec, u, w=2.4, h=1.7, room=f"dorm{k}",
                      win=WinSpec(1.3, 1.2, 0.25, lights=2), side=side, setback=1.0,
                      pitch_deg=45.0, wall_mat=SID_HI)
        flip_dormer_cheeks(ps, f"d{k}", side)   # coplanar hygiene (-X / +Y sides)
        P += ps
    for k, y in enumerate((-14.0, 14.0)):       # stack from just under the slab top
        P += K.chimney(f"m{k}", M_RIDGE_X, y, 2.0, 3.0, CHIM_TOP,
                       base_z=K.roof_z(mspec, M_RIDGE_X - 1.0, y) - 0.25, mat=STN)
        for j, dy in enumerate((-0.6, 0.6)):
            P += K._single(f"flue_m{k}{j}", lambda g, y=y, dy=dy: g.cyl(
                (M_RIDGE_X, y + dy, CHIM_TOP - 0.02), (M_RIDGE_X, y + dy, FLUE_TOP), 0.17,
                12 if fine else 8, cap0=False), "plaster")      # step 3e: 12-seg flue pots

    # ---------------- H great hall (revision 0, moved with M's road face) --------------
    P += K.storey("gh", H_X0, H_X1, -H_Y, H_Y, 0.0, H_EAVE, SID_LO, ground=True,
                  drop=("+Z", "+X"), cut_z=(1.2,))
    # glass backing: its +-Y ends lay on the hall's side walls (same-facing coplanar strips,
    # L5 r1 coplanar contract) - dropped, the side walls close the box
    P += K.box_part("wall_ghgl", (H_X1 - 0.12, -H_Y, 0.0), (H_X1, H_Y, H_EAVE), "glass_dark",
                    drop=("-X", "-Y", "+Y"), ground=True)
    ring = [((H_X1, -H_Y - 0.3, LIFT), (H_X1 + 0.3, -1.55, 1.2), ("-X",)),
            ((H_X1, 1.55, LIFT), (H_X1 + 0.3, H_Y + 0.3, 1.2), ("-X",)),
            ((H_X0, -H_Y - 0.3, LIFT), (H_X1, -H_Y, 1.2), ("+Y", "-X")),
            ((H_X0, H_Y, LIFT), (H_X1, H_Y + 0.3, 1.2), ("-Y", "-X"))]
    P += K.boxes_part("plinth_hring", ring, STN)
    hspec = RoofSpec("gable", H_MEET + 1.5, H_X1, -H_Y, H_Y, H_EAVE, 45.0, axis="X",
                     overhang=1.0, rake_over=1.5, thick=ROOF_T, mat="lux_slate",
                     trim="lux_trim")
    P += K.gable_roof("h", hspec, walls=False, gutters=True)
    P += K.prism_gable("hfront", "X", H_X1 - 0.3, H_X1, -H_Y, H_Y, H_EAVE, H_APEX - 0.02,
                       mat="timber_shake")
    hf = WallRef("+X", H_X1)
    P += K.curtain_wall("hlo0", hf, -7.0, -1.55, 1.2, 4.5, grid_u=1.75, transom_at=3.3,
                        room="hlo", per_storey_rooms=False)
    P += K.curtain_wall("hlo1", hf, 1.55, 7.0, 1.2, 4.5, grid_u=1.75, transom_at=3.3,
                        room="hlo", per_storey_rooms=False)
    P += K.curtain_wall("hup", hf, -7.0, 7.0, 4.5, 8.6, grid_u=1.75, transom_at=4.1,
                        room="hup", per_storey_rooms=False)
    P += K.entry("h", hf, 0.0, w=2.4, h=2.7, sidelights=0, lamps=0, z=SLAB_T + 0.01,
                 room="hdoor", surround_mat=GLU)
    P += K.pane_quad("win_hlo_hdoortr", hf, -1.4, 1.4, 3.32, 4.44)
    steps = ((-4.4, -2.2), (-2.2, 0.0), (0.0, 2.2), (2.2, 4.4))
    tops = [H_APEX - max(abs(ya), abs(yb)) - 0.95 for ya, yb in steps]
    for j, (ya, yb) in enumerate(steps):
        for r, (za, zb) in enumerate(((9.25, 12.4), (12.4, tops[j]))):
            if zb - za > 0.3:
                P += K.pane_quad(f"win_hgab{r}_hg{j}{r}", hf, ya, yb, za, zb)
    gm = []
    edges = [steps[0][0]] + [yb for _ya, yb in steps]
    for i, ye in enumerate(edges):
        t_left = tops[i - 1] if i > 0 else tops[0]
        t_right = tops[i] if i < len(tops) else tops[-1]
        # mullions start inside the 9.15..9.27 sill transom (r1: a 9.15 start put their
        # bottoms on the transom's, same-facing coplanar)
        gm.append((ye - 0.06, ye + 0.06, 9.21, max(t_left, t_right), 0.0, 0.15))
    P += K.wall_boxes("cmull_hgab", hf, gm, "trim")
    P += K.wall_boxes("ctran_hgab", hf, [(-4.46, 4.46, 9.15, 9.27, 0.0, 0.14),
                                         (-4.46, 4.46, 12.34, 12.46, 0.0, 0.14)], "trim")
    for side, plane, tag in (("-Y", -H_Y, "s"), ("+Y", H_Y, "n")):
        wall = WallRef(side, plane)
        sg = 1.0 if side == "-Y" else -1.0
        for r, sill in enumerate((1.5, 5.3)):
            # revision 2: the tower deck (slab 4.25-4.5) runs along the +Y wall, so the +Y
            # ground-floor windows end at 3.9 (head board to 4.1) instead of 4.5 (4.7)
            wh = 2.4 if (tag == "n" and r == 0) else 3.0
            for j, xc in enumerate((H_X0 + 2.25, H_X0 + 9.45)):
                u = sg * xc
                P += K.window_bay(f"hf{tag}{r}{j}", wall, u - 1.5, u + 1.5,
                                  WinSpec(2.6, wh, sill, lights=2), room=f"hf{tag}{r}")
    for k, y in enumerate((-H_PIER_Y, H_PIER_Y)):
        P += K.stone_pier(f"h{k}", H_X1, y, 2.0, 2.0, LIFT, 4.5, cap=True, ground=False,
                          mat=STN)
    z_ch = 8.65                         # chord centre: the truss sits UNDER the 18.0 roof
    ch_h, mw = 0.6, 0.5

    def rz(y):                          # rafter centre line just under the slab
        return (H_APEX - 0.212 - 0.354) - abs(y)
    yh = 8.75
    P += member("truss_hchord", (H_TRUSS_X, -yh, z_ch), (H_TRUSS_X, yh, z_ch), mw, ch_h)
    P += member("truss_hraft0", (H_TRUSS_X, -yh, rz(yh)), (H_TRUSS_X, 0.0, rz(0.0)), mw, mw,
                up=(1, 0, 0))
    P += member("truss_hraft1", (H_TRUSS_X, yh, rz(yh)), (H_TRUSS_X, 0.0, rz(0.0)), mw, mw,
                up=(1, 0, 0))
    P += member("truss_hking", (H_TRUSS_X, 0.0, z_ch + 0.3), (H_TRUSS_X, 0.0, rz(0.0)), mw, mw,
                up=(1, 0, 0))
    for j, sg in enumerate((-1, 1)):
        P += member(f"truss_hstrut{j}", (H_TRUSS_X, 0.0, z_ch + 1.6),
                    (H_TRUSS_X, sg * 4.2, rz(4.2) - 0.1), 0.40, 0.40, up=(1, 0, 0))
        P += member(f"truss_hpost{j}", (H_TRUSS_X, sg * 8.0, 4.65), (H_TRUSS_X, sg * 8.0, z_ch - 0.3),
                    mw, mw, up=(1, 0, 0))
        a, b = (H_TRUSS_X, sg * 7.75, 6.5), (H_TRUSS_X, sg * 5.5, z_ch - 0.3)
        P += curved_member(f"truss_hbrace{j}", arc_pts(a, b, (0.0, sg * 0.45, -0.45)), 0.40, 0.40,
                           up=(1, 0, 0))
    plates = [(0.0, z_ch), (0.0, rz(0.0) - 0.25), (-8.0, z_ch), (8.0, z_ch), (0.0, z_ch + 1.6)]
    P += K.boxes_part("trsplate_h", [((H_TRUSS_X + 0.25, y - 0.36, z - 0.36),
                                      (H_TRUSS_X + 0.28, y + 0.36, z + 0.36)) for y, z in plates],
                      "trim")
    for k, (y, side) in enumerate(((-H_PIER_Y, "+X"), (H_PIER_Y, "+X"), (-H_PIER_Y, "-Y"),
                                   (H_PIER_Y, "+Y"))):
        if side == "+X":
            lo, hi = (H_X1 + 1.0, y - 0.5, 0.9), (H_X1 + 1.04, y + 0.5, 3.9)
        elif side == "-Y":
            lo, hi = (H_X1 - 0.5, y - 1.04, 0.9), (H_X1 + 0.5, y - 1.0, 3.9)
        else:
            lo, hi = (H_X1 - 0.5, y + 1.0, 0.9), (H_X1 + 0.5, y + 1.04, 3.9)
        P += K.box_part(f"banner_h{k}", lo, hi, "accent")
    P += K.chimney("hx", H_X0 + 6.2, -H_Y - 0.8, 2.4, 1.6, 21.5, base_z=LIFT,
                   shoulder=(3.4, 2.0, 4.5), mat=STN)

    # ---------------- P porte-cochere (+X, in front of the hall) ---------------------------
    ped = [(x, y) for x in P_POST_X for y in (-5.3, 5.3)]
    for k, (x, y) in enumerate(ped):
        P += K.stone_pier(f"pc{k}", x, y, 1.4, 1.4, LIFT, 1.6, cap=True, ground=False, mat=STN)
        P += K.col(f"pc{k}", x, y, 1.75, P_TIE - 0.2, size=0.5, mat=GLU)
    for k, y in enumerate((-5.3, 5.3)):
        P += K.beam(f"pcl{k}", (P_POST_X[0] - 0.25, y, P_TIE), (P_POST_X[1] + 0.25, y, P_TIE),
                    0.40, 0.45, mat=GLU)
    pspec = RoofSpec("gable", P_X0, P_X1, -P_Y, P_Y, P_TIE, 45.0, axis="X", overhang=0.6,
                     rake_over=0.4, thick=0.24, mat="metal_roof")
    P += K.gable_roof("pc", pspec, walls=False)
    for k, x in enumerate(P_POST_X):    # the trusses sit on the post lines (chord = tie beam)
        P += K.truss_kingpost(f"pc{k}", WallRef("+X", x, -5.3, 5.3), 10.6, P_TIE, 45.0,
                              member=0.40, braces="curved", d=0.0, mat=GLU)
    pmid = 0.5 * (P_POST_X[0] + P_POST_X[1])
    for k, (x, y) in enumerate(ped):    # 8 curved knee braces
        sx = 1.0 if x < pmid else -1.0
        sy = 1.0 if y < 0 else -1.0
        P += K.bracket(f"pcx{k}", (x + sx * 0.25, y, P_TIE - 0.25), (sx, 0.0, 0.0), span=1.1,
                       drop=1.1, t=0.25, curved=True, mat=GLU)
        P += K.bracket(f"pcy{k}", (x, y + sy * 0.25, P_TIE - 0.2), (0.0, sy, 0.0), span=1.1,
                       drop=1.1, t=0.25, curved=True, mat=GLU)
    if PC_GABLE_INFILL:
        # revision 4 (a), eighteenth look "the front triangle above the valet area ... empty space
        # you can see into": tongue-and-groove infills (pentagons from the ceiling bottom to 0.02
        # inside the roof slab) just inside both trusses, the ceiling between them (its ends meet
        # the infills: no exposed ceiling top), frieze boards on the tie beams closing the long
        # slots between the ceiling edge and the roof underside
        P += K.box_part("soffit_pcceil", (PC_GAB_BACK[1], -5.3, PC_CEIL_Z[0]),
                        (PC_GAB_FRONT[0], 5.3, PC_CEIL_Z[1]), "deck", drop=("+Z", "-X", "+X"))
        # the board profile (y, z): down to the tie-beam centre between the beams' inner faces,
        # notched onto the beam tops, its sloped edges 0.02 inside the roof slab's underside
        bt = P_TIE + 0.225                      # tie-beam top
        apex = PC_GAB_EAVE + PC_GAB_E           # 10.85 (underside 10.83 at y 0)
        prof = [(-5.1, PC_GAB_BASE), (5.1, PC_GAB_BASE), (5.1, bt), (apex - bt, bt), (0.0, apex),
                (-(apex - bt), bt), (-5.1, bt)]
        for k, (xa, xb) in enumerate((PC_GAB_BACK, PC_GAB_FRONT)):
            P += prism_yz(f"gable_pcgab{k}", prof, xa, xb, "lux_tg")
        P += K.boxes_part("trim_pcfrieze", [
            ((PC_GAB_BACK[1], sg * 5.30 - (0.20 if sg < 0 else 0.0), P_TIE + 0.225),
             (PC_GAB_FRONT[0], sg * 5.30 + (0.20 if sg > 0 else 0.0), P_TIE + 0.45),
             ("-Y",) if sg > 0 else ("+Y",)) for sg in (-1.0, 1.0)], "lux_tg")
    else:                               # the revision-3 open gable (tests: the G1 negative)
        P += K.box_part("soffit_pcceil", (P_POST_X[0], -5.3, P_TIE + 0.24),
                        (P_POST_X[1], 5.3, P_TIE + 0.30), "deck", drop=("+Z",))
    for k, y in enumerate((-2.5, 2.5)):
        lp, _pt = K.lantern(f"pc{k}", (pmid, y, 3.9), "+X", kind="post")
        P += lp
        if fine:                        # step 3e: an 8-seg round rod (lantern top to the ceiling)
            P += K._single(f"lamp_pc{k}rod", lambda g, y=y: g.cyl(
                (pmid, y, 4.39), (pmid, y, P_TIE + 0.24), 0.02, 8, cap0=False, cap1=False), "trim")
        else:
            P += K.box_part(f"lamp_pc{k}rod", (pmid - 0.02, y - 0.02, 4.39),
                            (pmid + 0.02, y + 0.02, P_TIE + 0.24), "trim")
    if fine:                            # step 3e: the porte-cochere name board (sign set face)
        P += name_board(K)

    # ---------------- E (revision 3): the valet forecourt (drive loop, walks, island) ---------
    # replaces revision 2's road plaza; its lamp posts, bollards and planters are vanilla
    ps, fc = forecourt()
    P += ps

    # ---------------- T pool terrace (courtyard, slope side) -----------------------------
    ps, pool = pool_terrace(STN)
    P += ps

    # ---------------- S (revision 3): the plaza on the pool / slope side ----------------------
    ps, pz = slope_plaza(fine)
    P += ps

    # ---------------- clock tower (in-mesh, lodge.py --tower / --no-tower) -----------------
    tower = opts.get("tower", True)
    if tower:                           # revision 2: the dial moves down onto the shaft
        P += clock_tower("ct", CT_C[0], CT_C[1], 0.0, ground=True, rise=CT_RISE,
                         clock_z=CT_CLOCK_Z_L5, clock_d=CT_CLOCK_D_L5, dial=CT_DIAL_L5,
                         louvres=True, fine=fine)

    # ---------------- revision 2: the tower deck (asymmetric front) ----------------------------
    ps, deck = tower_deck(tower)
    P += ps

    # ---------------- UV-island hygiene (see unbevelled) ----------------------------------
    unbevelled(P, "truss_xg", "trim_trussxg")
    unbevelled(P, "truss_pc", "trim_trusspc")
    # ---------------- coplanar hygiene (revision 1; see split_double_doors & co) ------------
    split_double_doors(P)                       # hall door (the tower's did it already)
    turn_posts(P, "post_stairtw")               # revision 3: the stair runs along Y (K21)
    inset_transom_ends(P)
    for nm in ("ctran_hlo0", "ctran_hlo1"):     # hup's full-width 4.5 transom stays
        drop_faces_above(P, nm, Z_F[1] - 0.06)
    drop_loose_verts(P)
    room_count, rooms = retag_rooms_by_zone(P)

    # ---------------- contracts on the parts (section 9 + revision 1) ---------------------
    lo, hi = parts_bounds(P)            # the revision-1 frame: the lot is x -32..40
    check(lo[0] >= -HX - X_SHIFT and hi[0] <= HX - X_SHIFT and max(-lo[1], hi[1]) <= HY,
          f"C1 lot fit (pre-shift): x {lo[0]:.2f}..{hi[0]:.2f}, y {lo[1]:.2f}..{hi[1]:.2f} vs "
          f"x {-HX - X_SHIFT}..{HX - X_SHIFT}, y +-{HY}")
    check(lo[2] >= -1e-4, f"C2 min z {lo[2]:.4f} < 0")
    top = CT_FINIAL_TOP + CT_RISE if opts.get("tower", True) else FLUE_TOP
    check(abs(hi[2] - top) <= 0.05, f"C2 max z {hi[2]:.3f} != {top} (tower finial / flues)")
    for gp in GROUND_PARTS:
        check(any(o.name == gp for o, _m in P), f"C3 ground part {gp} missing")
    for pre in (("deck_e",), ("pier_pc", "col_pc", "roof_pc"), ("entry_h",)):
        cx = area_centroid_x(P, pre)
        check(cx > H_X1, f"C4 facing: {pre} centroid x {cx:.2f} <= {H_X1}")
    for pre in (("water_", "poolcope_", "deck_pool"), ("wall_gws", "wall_gwn")):
        cx = area_centroid_x(P, pre)
        check(cx < M_X0 - 4.0, f"C4 facing: {pre} centroid x {cx:.2f} >= {M_X0 - 4.0}")
    check(abs(M_RIDGE - 22.0) < 1e-6, f"C6 M ridge {M_RIDGE}")
    h_ridge_c = K.roof_z(hspec, 12.0, 0.0) - 0.5 * ROOF_T * math.sqrt(2)
    check(abs(h_ridge_c - H_APEX) < 1e-6, f"C6 H apex {h_ridge_c:.3f} != {H_APEX}")
    _lo, _hi = parts_bounds(P, ("trim_trusspc",))
    check(_lo[2] >= 4.5, f"C6 P tie beam (truss chord) bottom {_lo[2]:.2f} < 4.5")
    _lo, _hi = parts_bounds(P, ("truss_h",))
    check(_hi[2] <= K.roof_z(hspec, H_TRUSS_X, 0.0) - 0.3,
          f"C6 hall truss top {_hi[2]:.2f} pokes through the roof")
    m_ridge_top = K.roof_z(mspec, M_RIDGE_X, 0.0)
    w_ridge_top = K.roof_z(wspecs["s"], -20.0, -28.0)
    for pre, ridge in (("trim_trussxgm", m_ridge_top), ("trim_trussxgend", w_ridge_top)):
        _lo, _hi = parts_bounds(P, (pre,))
        check(_hi[2] <= ridge - 0.2, f"C6 {pre} top {_hi[2]:.2f} pokes through the ridge "
                                     f"{ridge:.2f} - 0.2")
    for cap, ridge in (("chimcap_m0", m_ridge_top), ("chimcap_m1", m_ridge_top),
                       ("chimcap_hx", K.roof_z(hspec, 12.0, 0.0)),
                       ("chimcap_ws", w_ridge_top), ("chimcap_wn", w_ridge_top)):
        _lo, _hi = parts_bounds(P, (cap,))
        check(_hi[2] >= ridge + 2.0, f"C6 {cap} top {_hi[2]:.2f} < ridge {ridge:.2f} + 2")
    rf = roof_face_counts(P)
    bad = {nm: n for nm, n in rf.items()
           if nm.startswith(("roof_m", "roof_h", "roof_xg", "roof_pc", "roof_w")) and n not in (6, 8)}
    check(not bad, f"C7 roof slabs must be the builder's closed slabs: {bad}")
    npanes = check_panes(P, 4.6, l5_storey_lines, "L5")
    # F1: every cross-gable bay front stands >= 0.1 m beyond its host's eave stack (roof
    # slab, fascia, gutter on that side), so the eave dies into the bay sides
    reach = {}
    for side, front, host, tag in xg_fronts:
        key = (host, tag)
        if key not in reach:
            elo, ehi = parts_bounds(P, (f"roof_{host}{tag}", f"fascia_{host}{tag}",
                                        f"gutter_{host}{tag}"))
            ax = 0 if side[1] == "X" else 1
            plane = {"+X": M_X1, "-X": M_X0}.get(side) if host == "m" else \
                (WINGS[0][2] if host == "ws" else WINGS[1][1])
            reach[key] = float(ehi[ax] - plane) if side[0] == "+" else float(plane - elo[ax])
            reach[key + ("plane",)] = plane
        proj = abs(front - reach[key + ("plane",)])
        check(proj >= reach[key] + 0.1 - 1e-9,
              f"F1 cross gable on {host}{tag} ({side}) projects {proj:.2f} m, inside the eave "
              f"reach {reach[key]:.2f} m + 0.1")
    check_pane_clearance(P, "L5")
    # pools: water below the deck top; coping / tub rims above it; the holes inside the deck
    # and apart; distinct horizontal levels (no coplanar deck / coping / water faces)
    for o, _m in P:
        if o.name.startswith("water_"):
            W = world_coords(o)
            check(float(W[:, 2].max()) <= PD - 0.05,
                  f"pool {o.name}: water {W[:, 2].max():.2f} not below the deck top {PD}")
    levels = sorted((PD, PD + POOL_CAP_H, MAIN_TOP, MAIN_WATER, TUB_TOP, TUB_WATER, PD + 0.25,
                     PD + 0.45))
    check(min(b - a for a, b in zip(levels, levels[1:])) >= 0.02,
          f"pool horizontal levels within 2 cm of each other: {levels}")
    rx0, rx1, ry0, ry1 = pool["deck_rect"]
    for h in pool["holes"]:
        hx = [p[0] for p in h]
        hy = [p[1] for p in h]
        check(min(hx) >= rx0 + 0.3 and max(hx) <= rx1 - 0.3 and min(hy) >= ry0 + 0.3
              and max(hy) <= ry1 - 0.3, f"pool hole {min(hx):.2f}..{max(hx):.2f} x "
              f"{min(hy):.2f}..{max(hy):.2f} leaves the deck {pool['deck_rect']}")
    boxes = [(min(p[0] for p in h), max(p[0] for p in h), min(p[1] for p in h),
              max(p[1] for p in h)) for h in pool["holes"]]
    boxes.append((FIREPIT[0] - 1.4, FIREPIT[0] + 1.4, FIREPIT[1] - 1.4, FIREPIT[1] + 1.4))
    for i, a in enumerate(boxes):
        for b in boxes[i + 1:]:
            gap = max(b[0] - a[1], a[0] - b[1], b[2] - a[3], a[2] - b[3])
            check(gap >= 0.5, f"pool features {a} and {b} only {gap:.2f} m apart")
    # night read: every storey (GF, F2, F3, F4, roof) has panes in >= 2 room tags
    per_floor = {}
    for t in rooms:
        fl = int(t[1]) if t[0] == "f" else (0 if t == "hlo" else 1 if t == "hup" else 2)
        per_floor.setdefault(fl, []).append(t)
    check(all(len(per_floor.get(fl, [])) >= 2 for fl in range(5)),
          f"night read: room tags per storey {per_floor}")
    # ---------------- revision 4 contracts (eighteenth look) ------------------------------------
    # (a) G1: no ray from an arrival-side preview camera or the close walk-around through the
    # porte-cochere's front gable triangle sees into it (front faces only, within GABLE_DEPTH_MAX
    # of the truss face); a grid every 0.4 m across x 0.3 m up the triangle above the ceiling
    gcams = preview_cameras(P, ARRIVAL_VIEWS)
    for r, a, zc in GABLE_NEAR_CAMS:
        gcams[f"near{r:g}_{a:+g}_{zc:g}"] = (GABLE_FACE_X + r * math.cos(math.radians(a)),
                                              r * math.sin(math.radians(a)), zc)
    g_ys = [-5.2 + 0.4 * i for i in range(27)]

    def g_zs(y):
        top = P_TIE + P_Y - abs(y) - 0.17 - 0.06    # just under the roof slab's underside
        return [PC_GAB_EAVE + 0.06 + 0.3 * j for j in range(40) if PC_GAB_EAVE + 0.06 + 0.3 * j <= top]
    gable = check_gable_closed(P, gcams, GABLE_FACE_X, g_ys, g_zs, GABLE_DEPTH_MAX)
    # (b) D1-D3 every terrace door: a real recessed opening, the clear 1.80 x 2.30, the threshold
    # on the walk; D5 no gutter / downspout in a door's clear box (D4 after the vanilla rows)
    door_rep = {d["tag"]: check_door(P, d, d["host"]) for d in doors}
    check_clear_boxes(P, [(d["tag"], clear_box(d)) for d in doors])
    # ---------------- revision 3 contracts (pool-side plaza, valet forecourt, stair right, vanilla
    # props; the revision-2 deck and clock contracts stand) --------------------------------------
    # (1) the plaza on the slope side: end to end, to 0.6 m inside the back lot edge, at SLAB_T,
    # running into the courtyard (the strip reaches the podium wall) with a flight up to the
    # podium on its -X face (tops 0.33 / 0.52 / 0.70 under the 0.72-0.82 deck / coping)
    plo, phi = parts_bounds(P, ("deck_s",))
    check(plo[0] <= -HX - X_SHIFT + 1.0 and plo[1] <= -(HY - 2.0) and phi[1] >= HY - 2.0,
          f"R3a slope plaza not end to end / short of the back edge: x {plo[0]:.2f}.., y "
          f"{plo[1]:.2f}..{phi[1]:.2f}")
    check(abs(phi[2] - SLAB_T) < 1e-6 and abs(phi[0] - POOL_X0) < 1e-6,
          f"R3a slope plaza top {phi[2]:.3f} / reach x {phi[0]:.2f} (want {SLAB_T} / the podium "
          f"wall {POOL_X0})")
    wlo, whi = parts_bounds(P, ("stairblk_w",))
    check(abs(whi[0] - POOL_X0) < 1e-6 and abs(wlo[2] - SLAB_T) < 1e-6 and whi[2] < PD,
          f"R3a podium steps to the plaza {wlo} {whi}")
    check(not any(o.name.startswith("deck_e") and parts_bounds(P, (o.name,))[0][0] < M_X1 - 1e-6
                  for o, _m in P), "R3a a forecourt deck_e part reaches behind M's road face")
    # (2) the vanilla rule: no modelled tree, bench, bin, table, chair, umbrella, lamp post,
    # planter or bollard is left; the racks carry kit gear WITHOUT bindings
    gone = ("cafe", "bench_", "bin_", "planter_", "shrub_", "conifer_", "bollard_", "lounger_",
            "umbstand_", "umbpole_", "umbcanopy_", "post_pl", "post_fc", "post_pz", "post_tw",
            "lamp_pl", "lamp_fc", "lamp_tw", "lamp_pz0", "lamp_pz1", "lamp_pz2", "lamp_pz3")
    left = sorted(o.name for o, _m in P if o.name.startswith(gone))
    check(not left, f"R3d modelled dressing left (vanilla rule): {left[:8]}")
    import inspect
    check(inspect.signature(K.ski_rack_frame).parameters["bindings"].default is False,
          "R3d cs2_bldg.ski_rack_frame draws bindings by default (the no-bindings kit rule)")
    gear = [o.name for o, _m in P if o.name.startswith("skirack_pz") and
            o.name.rstrip("AWKBRG").endswith("g")]
    if fine:                            # step 3e: no modelled rack / road-face lantern is left
        left = sorted(o.name for o, _m in P if o.name.startswith(("skirack_", "lamp_pm")))
        check(not left, f"R3d fine: modelled racks / road-face lanterns left: {left[:6]}")
    else:
        check(len({g[len("skirack_"):].split("g")[0] for g in gear}) == len(PZ_RACKS),
              f"R3d gear on {len(gear)} parts, want all {len(PZ_RACKS)} plaza racks")
    for o, _m in P:                     # racks stand ON the plaza, never in it
        if o.name.startswith("skirack_pz"):
            z0 = float(world_coords(o)[:, 2].min())
            check(z0 >= SLAB_T - 1e-4, f"R3d {o.name} starts at z {z0:.3f} (inside the plaza)")
    zones, sboxes = check_sub_objects(P, rows=rows)
    walks = check_door_walks(P, doors, sboxes, rows=rows)   # revision 4 D4
    if fine:                            # step 3e: F1 on the kit rack rows, the name board
        kit = check_kit_rows_l5(P, sboxes, rows)
        nb = check_name_board(P)
    else:
        kit, nb = None, None
    # (3) the deck (revision 2 contracts) and the stair at its right-hand end, clear of the drive
    dlo, dhi = parts_bounds(P, ("deck_tw",))
    check(abs(dhi[2] - DK_Z) < 1e-6 and abs(dlo[2] - (DK_Z - DK_T)) < 1e-6,
          f"R2c deck slab {dlo[2]:.2f}..{dhi[2]:.2f} != {DK_Z - DK_T}..{DK_Z}")
    if tower:
        tx0, tx1, ty0, ty1 = deck["tower"]
        check(dlo[0] <= tx0 - 3.0 and dhi[0] >= tx1 + 5.0 and dlo[1] <= ty0 - 4.0
              and dhi[1] >= ty1 + 1.5, f"R2c the deck does not wrap the tower base: deck x "
              f"{dlo[0]:.2f}..{dhi[0]:.2f} y {dlo[1]:.2f}..{dhi[1]:.2f}, tower x {tx0}..{tx1} "
              f"y {ty0}..{ty1}")
    slo, shi = parts_bounds(P, ("stair_tw_treads",))
    check(abs(shi[2] - DK_Z) < 1e-6 and slo[2] >= SLAB_T - 1e-6 and slo[1] >= DK_Y1
          and shi[1] <= DK_STAIR_FOOT_Y + 0.02 and slo[0] >= DK_X0 and shi[0] <= DK_X1,
          f"R3c stair treads {slo} {shi}: not from the walk up to the deck's right-hand edge")
    flo, fhi = parts_bounds(P, ("deck_e1",))
    check(fhi[1] >= DK_STAIR_FOOT_Y + DK_STAIR_LAND and abs(fhi[2] - SLAB_T) < 1e-6,
          f"R3c the stair foot / landing is not on the walk (walk y ..{fhi[1]:.2f})")
    corridor, lane, sx = check_drive_corridor(P, sboxes)
    # (b) the clock, with the vanilla props in the way (parasols, lamps, the trees' envelopes)
    if tower:
        sight = [(f"sub:{rows[i][0]}:{k}", (lo_, hi_))
                 for i, row in enumerate(rows)
                 for k, (lo_, hi_) in vanilla_boxes(row, envelope=True) if k != "trunk"]
        clear = check_clock_sightline(P, boxes=sight)
        clo, chi = parts_bounds(P, ("clock_ct",))
        check(chi[2] <= CT_SHAFT_TOP + CT_RISE - 0.3, f"R2b dial top {chi[2]:.2f} not below "
                                                      f"the stage")
    else:
        clear = {}
    ph = placeholder_tris(P, FINE_PLACEHOLDERS if fine else PLACEHOLDERS)
    # ---------------- the shift (lot 10 x 9: revision 3, the new cell is the slope side) -------
    shift_x(P, X_SHIFT)
    lo, hi = parts_bounds(P)
    check(max(-lo[0], hi[0]) <= HX and max(-lo[1], hi[1]) <= HY,
          f"C1 lot fit: x {lo[0]:.2f}..{hi[0]:.2f}, y {lo[1]:.2f}..{hi[1]:.2f} vs +-{HX}/{HY}")
    print(f"[L5] contracts OK: bounds x {lo[0]:.2f}..{hi[0]:.2f} y {lo[1]:.2f}..{hi[1]:.2f} "
          f"z {lo[2]:.2f}..{hi[2]:.2f}; {npanes} panes in {len(rooms)} rooms {room_count}; "
          f"{len(P)} parts; xg eave reach {sorted((k[0] + k[1], round(v, 2)) for k, v in reach.items() if len(k) == 2)}; "
          f"pane clearance {PANE_CLEAR_M} m OK; pools: deck {PD}, water {MAIN_WATER} / {TUB_WATER}; "
          f"r3: slope plaza x {plo[0] + X_SHIFT:.2f}..{phi[0] + X_SHIFT:.2f} (lot -{HX}) y "
          f"+-{phi[1]:.2f}, {pz['racks']} racks, {len(gear)} gear parts (no bindings); drive "
          f"corridor past the deck {corridor[0]:.2f} m (min at y {corridor[1]}, z {corridor[2]}: "
          f"{corridor[3]}), canopy lane {lane:.2f} m, stair x {sx[0] + X_SHIFT:.2f}.."
          f"{sx[1] + X_SHIFT:.2f} y {sx[2]:.2f}..{sx[3]:.2f}; {len(rows)} vanilla "
          f"sub-objects {zones}; placeholders {ph}; deck {DK_Z} wraps the tower; clock z "
          f"{CT_CLOCK_Z_L5}, clear to {clear} deg; shift {X_SHIFT}; r4: gable closed from "
          f"{len(gable)} cameras ({sum(gable.values())} rays, depth <= {GABLE_DEPTH_MAX} m), doors "
          f"{door_rep}, walks to the steps {walks}"
          + (f"; fine-detail 3e: {len(KIT_ROWS)} kit rack rows {[r['prefab'] for r in KIT_ROWS]} ({kit['F1']}; "
             f"{kit['aisles']} access aisles free), {len(FINE_ROWS)} road-face wall lights, name board {nb}"
             if fine else ""))
    return P


NOTES_R4 = ("Revision 4 (the porte-cochere gable closed with tongue-and-groove boards and two vanilla "
            "wall lights; three French terrace doors in real recessed openings onto the pool terrace, "
            "each facing a flight up to the podium) on "
            "revision 3 (pool-side plaza, valet drive, stair right, vanilla props) on revision 2 "
            "(clock, tower deck) and revision 1 (Peak 7): U-shaped 5-storey grand lodge on a 10 x 9 "
            "lot - main bar + two wings, 6 cross gables over balcony stacks, 13 dormers, 5 stone "
            "chimneys, great hall with king-post glass gable, porte-cochere on +X over a valet loop "
            "with a raised island, clock tower (raised 4 m, dial on the shaft), an elevated deck "
            "wrapping the tower base with its stair at the right-hand end, a pool terrace (lagoon "
            "pool, 2 hot tubs, fire pit) joined to an end-to-end slope-side plaza with ski racks of "
            "real gear and a fire pit; tables, chairs, parasols, loungers, heaters, benches, bins, "
            "pots, lamp posts, garden lights and trees are vanilla sub-objects.")
FINE_NOTES = ("Fine-detail step 3e (the plaza racks are dressing-kit rows, the road-face lanterns vanilla "
              "short-range wall lights, a GRAND LODGE name board on the porte-cochere truss drawn by the sign "
              "set SkiSignSetGrandB, 12-seg finial and flue pots, round lantern rods) on " + NOTES_R4)

RECIPE = FineRecipe(
    key="L5", aliases=("lux-base",), letter="B",
    # prefab = the code catalog's name (SkiCatalog.Names.LodgeGrand, LocaleEN, hub; R25 /
    # bldg-kit 11). The mesh keeps its built name (no consumer reads it). buildings-integration 1
    asset="SkiLodgeSwankyBottomCommercialLow", prefab="SkiLodgeGrand", category="lodges",
    lot=(LOT_W, LOT_D), base_mode="engine", plinth=None, yaw_deg=0.0,
    # R18/R23: finish at 2048, lodge.py rebakes at 4096 when the shipped w=1 median < 25 px/m
    tex=2048, allow_4096=True,
    # revision 1: LOD0 cap 26,000 (verdict B1 / this round's task). LOD2 target 480 keeps the
    # generic assets-check (-Kind building, lod2_cap 500) green; the recipe cap stays 800
    # (validator spec via lodge.validate; K8: the CLI cannot take a per-asset cap).
    # revision 2: cap 30,000 - the plaza (4 racks with 36 pieces of real gear, 9 bistro sets,
    # planters, benches, lamps, bollards) and the tower deck (posts, beams, balustrade, a
    # 25-riser stair) add ~6,800 tris; all of it is LOD0 dressing (deleted at LOD1).
    # revision 3: the cap and target stand (verdict brief); the modelled dressing is vanilla
    # sub-objects now (their tris are vanilla's own, not ours), so LOD0 drops (build report)
    budget=Budget(26000, 30000, 11000, 480, 800),
    massing="custom", builder=build_l5,
    rooms="auto", interior="CommercialLow",
    # VariationSet A (contract 6, STEEL_DEFAULT_RGB convention): bronze / deep red / copper
    slots={1: (0.17, 0.12, 0.08), 2: (0.50, 0.07, 0.05), 3: (0.45, 0.22, 0.12)},
    ground_prefixes=GROUND_PREFIXES,
    arrival_prefixes=ARRIVAL_PREFIXES,
    # revision 3: the modelled dressing prefixes are gone with the dressing (vanilla now); the
    # racks are kit LOD1_DELETE `skirack_`, the fire pits `fp_` + `lamp_` embers
    lod1_extra=("trsplate_", "flue_", "fp_", "banner_"),
    # plinth_hring: see revision 0 (the hall's stone base strips; the LOD2 collapse kept a
    # 0.87 m eave drift with it). Pool walls / steps / podium coping and the balcony
    # balustrades are sub-pixel at the LOD2 switch.
    # Revision 1 (the first r1 export: LOD2 lost the forecourt slab, the wing rakes and the
    # whole clock-tower cap, 3.5 m, because 480 tris had to stand for 2,026 free tris after
    # the deletes): lodge.delete_lists does not carry lod1_extra into LOD2, so those come
    # again; piers, posts, soffits, grade walkways, pool coping, clock faces and the 0.2 m
    # finial are sub-pixel at the 1.6 km switch. Measured on the shipped LOD0 (review pack):
    # 907 free tris -> 480 keeps every kept part's extent (roof rakes, deck_e, the tower cap
    # apex) at 470 / 480 / 490 targets, with or without the chimney caps
    lod2_extra=("seatwall_", "soffit_pcceil", "plinth_hring", "balcwall_", "stairblk_",
                "poolwall_", "poolcap_",
                "trsplate_", "flue_", "fp_", "banner_",
                "poolcope_", "clock_", "pier_", "col_", "soffit_", "deck_w", "tower_ctfin",
                # revision 2 / 3: the deck stair; the deck slab (deck_tw), the forecourt
                # (deck_e) and the slope plaza (deck_s) stay
                "stair_tw",
                # revision 4: the door recesses add 60 tris to the collapse-PROTECTED ground walls
                # (146 -> 206), and with the free part squeezed (revision 3 kept 40 % of it) the
                # 480-tri collapse ate the tower cap (2.7 m) and the plaza's -Y end (1.9 m). The new
                # door / gable pieces and three flat slabs that are 5-20 px at the 1.76 km switch go
                # instead (the tower deck slab, the pool deck and its water: the podium reads as its
                # walls; the gable truss was already LOD2-deleted): 39 % kept, every extent held
                # (variants measured: build report "Revision 4", LOD2)
                "gable_pcgab", "doorsill_", "glassback_", "deck_tw", "deck_pool", "water_"),
    lod2_keep=CHIM_CAPS,
    # fine-detail step 3e: + the name board's face (painted in the atlas only with --no-signset; the
    # sign set routes it out and drops this entry, cs2_signset.host_recipe)
    labels=clock_tower_labels("ct") + (NAME_LABEL,),
    # soffits under the 45 deg M eaves face down AND inward (normal (+-0.707, 0, -0.707)):
    # only a camera below the eave within the cull's 25 m horizon sees them (revision 0
    # had soffit_mB here for the same reason; the courtyard one is now enclosed by the
    # wings too). deck_w0 keeps its top only: all four sides abut the podium, M and the
    # wings (contact), the bottom is on grade. Revision 3: deck_w1 / deck_w2 likewise - their
    # slope ends now abut the plaza (deck_s0), their long sides a wing and the podium / strip
    cull_ok=("soffit_mB", "soffit_mA", "deck_w0", "deck_w1", "deck_w2"),
    views=(("road34left", (-0.7, -1.0, 0.45)), ("slope34right", (0.8, 1.0, 0.5)),
           ("aerial", (0.4, -0.6, 1.2)), ("pools", (0.35, 1.0, 0.85)),
           ("arrival", (0.55, -1.0, 0.22)),
           # revision 2: the tower deck from its side. Revision 3: the plaza from the slope
           # (with the pools), the valet forecourt from the road (left, raised: the loop, the
           # island and the stair at the deck's right-hand end)
           ("plaza_pool", (-0.15, 1.0, 0.55)), ("tower_deck", (0.95, -0.7, 0.42)),
           ("valet", (-0.35, -1.0, 0.65)),
           # revision 4: the porte-cochere gable from the drive (front, low) and the pool-side doors
           # from the courtyard / slope side (low); whole-building frames like every view - the
           # true close-ups: `--closeups` (CLOSEUP_SHOTS) after the export
           ("gable_closeup", (-0.15, -1.0, 0.25)), ("pool_doors", (0.45, 1.0, 0.30))),
    # revision 4: the T&G gable infills are the entrance's hero boards: 1.5 x the wall density
    # (their 0.15 m joints get ~4 px at the shipped density instead of ~2.7)
    weights=(("gable_pcgab", 1.5),),
    # revision 3 (verdict B1''): every tree, bench, bin, table, chair, parasol, lounger, lamp
    # post, planter and bollard is a vanilla prop by reference (manifest `sub_objects`)
    # fine-detail step 3e: + the 4 road-face wall lights (FINE_SUB_OBJECTS) and the 4 kit rack rows
    # (KIT_ROWS); lodge.py appends the sign set's identity row
    sub_objects=SUB_OBJECTS + FINE_SUB_OBJECTS + KIT_ROWS,
    notes=FINE_NOTES,
    # fine-detail pass step 3e (opt-in; lodge.py --no-fine restores `legacy` = the approved revision 4)
    fine_detail=True,
    sign_set=SIGN_SET,
    kit_colour_set=KIT_COLOUR_SET,
    legacy=(("sub_objects", SUB_OBJECTS), ("labels", clock_tower_labels("ct")), ("notes", NOTES_R4)),
)


# ======================================================================================
# Revision 4 close-ups (CLI). lodge.py's previews frame the whole 77 m lodge from a direction;
# the gable and the doors need a camera at a point:
#   blender --background --factory-startup --python recipes/l5_swanky_bottom.py -- --closeups [OUT]
# re-imports the exported LOD0 (+ `_Win` as dark glass) and renders pbr + dusk
# `<name>_closeup_<shot>_<mode>.png` (closeup_previews). (shot, target, eye) in the SHIPPED
# authoring frame (after X_SHIFT): +X = road, +Y = right seen from the road, z up.
# ======================================================================================

_XS = X_SHIFT
CLOSEUP_SHOTS = (
    ("gable", (P_POST_X[1] + _XS, 0.0, 7.6), (P_POST_X[1] + _XS + 15.0, -5.0, 4.2)),
    ("gable_valet", (P_POST_X[1] + _XS, 0.0, 6.6), (P_POST_X[1] + _XS + 8.0, 6.5, 1.7)),
    ("pool_door_ws", (XG_WING + _XS, WINGS[0][2], 1.6), (XG_WING + _XS + 2.6, WINGS[0][2] + 7.5, 2.6)),
    ("pool_door_wn", (XG_WING + _XS, WINGS[1][1], 1.6), (XG_WING + _XS + 2.6, WINGS[1][1] - 7.5, 2.6)),
    ("pool_door_m", (M_X0 + _XS, 0.0, 1.6), (M_X0 + _XS - 8.5, 4.5, 2.8)),
    ("pool_doors", (-12.0 + _XS, 0.0, 2.2), (-37.0 + _XS, 9.0, 10.0)),
)


if __name__ == "__main__":
    _argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if "--closeups" in _argv:
        _i = _argv.index("--closeups")
        _out = _argv[_i + 1] if len(_argv) > _i + 1 and not _argv[_i + 1].startswith("--") else None
        for _f in closeup_previews(RECIPE, CLOSEUP_SHOTS, _out, ground=True):
            print("[closeups]", _f)
