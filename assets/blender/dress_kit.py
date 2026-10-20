"""
dress_kit.py - the shared DRESSING KIT prop set, letter B (fine-detail pass, design section D as
corrected by its skeptic FS1-FS16). Design: docs/research/art/assets-2026-10/design/fine-detail-pass.md
section D + design/fine-detail-pass-skeptic.md (FS1 weather mast stays in the stations: 8 parts;
FS3 one shared 1024^2 set >= 250 px/m; FS6 loadout variants; FS7 Parent color on the racks;
FS10 installed cull formula; FS12 nothing existing changes). Build report:
docs/research/art/assets-2026-10/build/dress-kit.md.

    cd assets/blender
    blender --background --factory-startup --python-exit-code 1 --python dress_kit.py -- \
        --out ../import/props/B --export --preview ../preview/props/B --review
    blender ... --python dress_kit.py -- --out <tmp> --export --no-staging --no-subobj   (tests)
    blender ... --python dress_kit.py -- --settings-proof     (FS12: write_settings byte identity)
    blender ... --python dress_kit.py -- --variants           (kit color variants: stage / table / kit manifests)
    blender ... --python dress_kit.py -- --variant-surgery <ImportedData>   (S1, game closed)

THE KIT (8 parts, 11 meshes; prefab-local meters x lateral, y up, z out / front)
    SkiDressSkiRackB / V2B / V3B   A-frame rack, 3 loadouts     cs2_bldg.ski_rack_frame + ski_gear
    SkiDressSkiRackWallB / V2B     station wall rack, 2 loadouts cs2_liftdress.ski_rack + ski_gear
    SkiDressHoseReelB              wall hose reel                 dims of cs2_liftdress.hose_reel
    SkiDressExtinguisherB          wall extinguisher              dims of cs2_liftdress.extinguisher
    SkiDressToolsB                 scraper + shovel, leaning      cs2_liftdress.tools (+ grips)
    SkiDressEStopB                 emergency-stop post            dims of cs2_liftdress.estop_post
    SkiDressAkjaB                  rescue toboggan, leaning       dims of cs2_bldg.akja
    SkiDressSpineBoardB            spine board, leaning           dims of cs2_bldg.backboard
    The existing builders are imported and CALLED, never edited. The racks and the tools are the
    builders' own geometry; for the five round / hero parts the builders' 8-sided output is what
    the fine-detail pass replaces, so the kit builds a 12-16-sided low mesh to the builders'
    dimensions and `parity()` proves the footprint against the builder's own output.

ONE SHARED SET
    All 11 low meshes are laid out on one line (2 m gaps, AO distance 0.5 m: no cross-talk) and
    joined. layout_unique() builds "UVUnique" itself: every island of every texel-owning part at
    D x w px/m (w = the part's density weight: 1 on every visible face, lower only on hidden /
    plain faces, listed in the report), skyline-packed with 90 deg rotation and 8 px gutters, D the
    densest scale that fits 1024^2. cs2_bake.bake_high_to_low then bakes the joined hi-poly twin's
    tangent normal + AO straight into that layer (bevels, bolt heads, ribs, hose turns, handholds),
    and compose_kit() writes the four maps from the kit's constant materials + cs2_compose
    .compose_maps. Why not bake_unique: its cs2_uv.pack_unique packs AABBs without rotation and
    reached ~50 % fill on these thin parts (158-175 px/m measured); rotation is safe here because
    no source normal map is re-projected. The first prop (SkiDressSkiRackB) owns the four PNGs;
    the ten others ship an FBX and a settings.json whose sharedAssets name the owner's maps
    (`../SkiDressSkiRackB/..._<Map>.png`).
    Rigid copies share texels (their loops take the canonical part's): every ski / board / pole
    item of every rack is a rigid copy of one library item (built by cs2_bldg.ski_gear at its real placement, then proven
    equal to the library item in its own frame), repeated A-frame bars are translation copies, the
    three A-frame frames and the two wall frames are copies. The hi-poly of a shared part is
    invariant under its copy transform (bolts on both faces of a shared rail), so a stacked texel
    is right for every copy.
    Bake rule: every hi part lies inside its low part's envelope or <= 2.5 mm outside it
    (round lows are circumscribed: vertex radius r / cos(pi / n)), cage extrusion 3 mm, max ray
    40 mm; a ray never starts inside hi geometry.

COLOR (kit color variants, 2026-10-12, build/kit-colour-variants.md; FS7 Parent DROPPED by Richard ~18:50:
    "I think they should stay a color that makes sense for that building")
    Ski-rack gear `accent` texels and the akja shell are ControlMask G (slot 2, channel 1), albedo 0.80
    under the mask. ColorProperties lives on the MESH prefab (RenderPrefab; decompile: ColorProperties.cs
    ComponentMenu, MeshColorSystem reads the SubMesh's mesh prefab), so each color SET of a colored prop
    is a VARIANT = its own object prefab + its own mesh prefab (a RenderPrefab clone of the imported base
    mesh prefab: same geometry + surface assets, a fixed ColorProperties with ONE VariationSet, no external
    channel, ranges 0). The registry below (stage_variant / sync_variants / variant_surgery) mints them
    ON DEMAND (only the (prop, set) pairs a shipped host manifest names), CIDs seeded from the name
    (fine_detail.variant_cids). The six colored base object prefabs and their Parent blocks do not ship.
    Every other prop has no ControlMask texel and no ColorProperties (beacon rule).

PREFABS (surgery, beacon pattern)
    assets/staging/prefabs/props/B/<Object>/<Object>.Prefab + .Prefab.cid (hand-minted ONCE, never
    re-minted: an existing .cid is kept) for the five UNCOLORED props; for every used color variant
    <Variant>/<Variant>.Prefab (complete: names the variant mesh CID) + .Prefab.cid + "<Variant> Mesh.Prefab.cid"
    + "<Variant> Mesh.components.json", and the table kit-variants.json.

No LOD chain (41 / 50 vanilla props ship none); the cull estimate uses the installed 1.6.2 formula
(MeshSystem.cs:497-503: LOD-less, max dim s <= 5 m -> m_LodBias = 1.2 (s / 5)^3) [U until SkiProbeLodA].
Determinism: no RNG, no clock in any output but the FBX header / manifest stamp; tag_parts canonical
order; Cycles CPU seed 0. No top-level side effects; the __main__ block runs only as a Blender script.
"""

import argparse
import hashlib
import json
import math
import os
import re
import shutil
import sys
import tempfile
import time
import uuid

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import cs2_asset      # noqa: E402
import cs2_bake       # noqa: E402
import cs2_compose    # noqa: E402
import cs2_uv         # noqa: E402
import cs2_validate   # noqa: E402

REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
GENERATOR = "assets/blender/dress_kit.py"

# --------------------------------------------------------------------------------------
# Kit-wide numbers
# --------------------------------------------------------------------------------------

LETTER = "B"
A = 1024                    # FS3: one 1024^2 four-map set
MARGIN_PX = 8               # island gutter (cs2_uv default 16 @ 1024): mip-3 safe; 6 gained 1 px/m
CAGE = 0.003                # cage extrusion (m): hi details stay <= 2.5 mm proud of the low
MAX_RAY = 0.04              # deepest hi recess / sagitta below a low face (m)
AO_SAMPLES = 48
AO_DISTANCE = 0.5           # world AO distance during the bake (m); props stand >= 2 m apart
LAYOUT_GAP = 2.0
DK6_MIN_PPM = 250.0         # FS3 (design DK6 >= 400 at 2048^2 -> >= 250 at 1024^2)
DK3_MIN_HF_DEG = 5.0        # design DK3: fine-detail rms >= 5 deg on each part's islands
BENCH_HF_DEG = 10.7         # vanilla GardenBench01, vanilla-detail-benchmark.md section 2.2
LOD_FACTOR = 652.0          # [U] until SkiProbeLodA
OWNER = "SkiDressSkiRackB"
MAPS = ("BaseColor", "ControlMask", "MaskMap", "Normal")
STAGING = os.path.join(REPO, "assets", "staging", "prefabs", "props", LETTER)
SUBOBJ_JSON = os.path.join(HERE, "subobj_bounds.json")

#: the 11 meshes. cap = design D.1 / DK1 cap of the PART; kind: ground | wall | lean;
#: color "variants" = colored (ControlMask G accent texels): ships ONLY as fixed color-set VARIANTS (kit
#: color variants, 2026-10-12; the FS7 Parent block and the base object prefab are retired); rows = design D.1
#: approved / pending instances.
PROPS = (
    dict(code="R1", name="SkiDressSkiRackB", obj="SkiDressSkiRack", part="SkiDressSkiRack",
         cap=900, kind="ground", colour="variants", variant=1,
         builder="cs2_bldg.ski_rack_frame(slots=8, along='X') + cs2_bldg.ski_gear (bindings off)",
         rows="approved L1 6, L2 2, school 2; pending tram base 1, L5 4 (all rack variants)"),
    dict(code="R2", name="SkiDressSkiRackV2B", obj="SkiDressSkiRackV2", part="SkiDressSkiRack",
         cap=900, kind="ground", colour="variants", variant=2,
         builder="as R1, loadout 2", rows="alternate with R1 / R3 along a row"),
    dict(code="R3", name="SkiDressSkiRackV3B", obj="SkiDressSkiRackV3", part="SkiDressSkiRack",
         cap=900, kind="ground", colour="variants", variant=3,
         builder="as R1, loadout 3", rows="alternate with R1 / R2 along a row"),
    dict(code="W1", name="SkiDressSkiRackWallB", obj="SkiDressSkiRackWall", part="SkiDressSkiRackWall",
         cap=450, kind="ground", colour="variants", variant=1,
         builder="cs2_liftdress.ski_rack(x0 -1.5, x1 1.5, side -1, pairs 0) frame + cs2_bldg.ski_gear",
         rows="approved DQ base 1, gondola 2; pending FG M base 1"),
    dict(code="W2", name="SkiDressSkiRackWallV2B", obj="SkiDressSkiRackWallV2",
         part="SkiDressSkiRackWall", cap=450, kind="ground", colour="variants", variant=2,
         builder="as W1, loadout 2", rows="the second rack of a host"),
    dict(code="HR", name="SkiDressHoseReelB", obj="SkiDressHoseReel", part="SkiDressHoseReel",
         cap=260, kind="wall", colour=None, variant=1,
         builder="dims of cs2_liftdress.hose_reel (r 0.30, w 0.18, plate 0.70); 16-seg kit build",
         rows="approved DQ 2, gondola 2; pending tram 2, P2 1"),
    dict(code="EX", name="SkiDressExtinguisherB", obj="SkiDressExtinguisher",
         part="SkiDressExtinguisher", cap=180, kind="wall", colour=None, variant=1,
         builder="dims of cs2_liftdress.extinguisher (r 0.08, h 0.45, 0.02 off the wall); 16-seg",
         rows="approved DQ 2, gondola 4; pending FG M 3, tram 3"),
    dict(code="TL", name="SkiDressToolsB", obj="SkiDressTools", part="SkiDressTools",
         cap=110, kind="lean", colour=None, variant=1,
         builder="cs2_liftdress.tools(lean 75, gap 0.35) + T / D grips",
         rows="approved DQ 2, gondola 2; pending FG M 2, tram top 1"),
    dict(code="ES", name="SkiDressEStopB", obj="SkiDressEStop", part="SkiDressEStop",
         cap=110, kind="ground", colour=None, variant=1,
         builder="dims of cs2_liftdress.estop_post (post r 0.04 h 1.00, box 0.16 x 0.12 x 0.16)",
         rows="approved DQ 2, gondola 4; pending FG M 2"),
    dict(code="AK", name="SkiDressAkjaB", obj="SkiDressAkja", part="SkiDressAkja",
         cap=260, kind="lean", colour="variants", variant=1,
         builder="dims of cs2_bldg.akja (2.0 x 0.6 m) at lean 75 deg",
         rows="pending P1 2, P2 3"),
    dict(code="SB", name="SkiDressSpineBoardB", obj="SkiDressSpineBoard", part="SkiDressSpineBoard",
         cap=60, kind="lean", colour=None, variant=1,
         builder="dims of cs2_bldg.backboard (1.85 x 0.45 x 0.05) at lean 85 deg",
         rows="pending P1 1, P2 1"),
)
BY_CODE = {p["code"]: p for p in PROPS}

# --------------------------------------------------------------------------------------
# Gear library + loadouts (FS6). Color pairs are cs2_liftdress.GEAR_SKI_COLOURS /
# GEAR_BOARD_COLOURS entries; one length per colorway so the tips of a row do not line up.
# --------------------------------------------------------------------------------------

GEAR = {
    "S1": ("ski", ("trim_white", "ski_blue"), 1.70),
    "S2": ("ski", ("accent", "ski_black"), 1.64),
    "S3": ("ski", ("ski_red", "ski_black"), 1.76),
    "S4": ("ski", ("ski_black", "accent"), 1.72),
    "B1": ("board", ("ski_black", "accent"), 1.52),
    "B2": ("board", ("ski_blue", "trim_white"), 1.48),
    "P1": ("poles", "accent", 1.38),
}
GEAR_BACK_W = 0.5           # density weight of gear faces that face the rack (back ski, board base)
FRAME_W = 1.0               # galvanised rack bars: visible, full weight
#: A-frame (8 slots per side; side +1 = front = prefab +Z). None = empty slot. Boards never sit
#: next to a board on one side (cs2_bldg.ski_rack_frame occupancy rule, GEAR_W).
AFRAME_LOADOUTS = {
    "R1": {+1: ("S1", "S2", "B1", "S3", "P1", "S4", "B2", "S1"),          # design: 10 + 3 + 2
           -1: ("S3", "B2", "S1", "S2", "S4", "P1", "S3", None)},
    "R2": {+1: ("S4", "S2", None, "S1", "B2", "S3", "S4", "S1"),          # 12 pairs + 2 boards
           -1: ("S2", "S1", "B1", "S4", "S3", None, "S2", "S3")},
    "R3": {+1: ("B1", "S2", None, "S4", "P1", None, "S3", "S1"),          # 8 + 2 + 2, gaps
           -1: ("S1", None, "B2", "S2", None, "P1", "S4", "S3")},
}
#: wall rack (3.0 m, one side, items toward prefab +Z), cs2_liftdress.ski_rack slot math
WALL_LOADOUTS = {
    "W1": ("S2", "S4", "B2", "S1", "S3", "S1", "B1", "S2", "S4"),       # design: 7 pairs + 2 boards
    "W2": ("S1", "S3", "P1", "S2", "B1", "S4", "S2", "S3"),             # 6 + 1 board + 1 poles
}

# A-frame numbers = cs2_bldg.ski_rack_frame defaults (slots 8, h 1.20, spread 0.40, bar 0.035,
# pitch 0.20, along "X"); parity() rebuilds every item with the builder itself.
AF = dict(slots=8, h=1.20, spread=0.40, bar=0.035, pitch=0.20, zb=0.35)
# wall rack = cs2_liftdress.ski_rack defaults on a 3.0 m run (x0 -1.5, x1 1.5, y 0, z0 0)
WR = dict(x0=-1.5, x1=1.5, y=0.0, z0=0.0, h=1.30, stand_off=0.40, side=-1.0)

# hero parts (prop-local Blender meters; wall plane y = 0, prop toward -y = prefab +Z)
HOSE = dict(r=0.30, w=0.18, plate=0.70, plate_t=0.02, coil_r=0.25, hose_r=0.022, cz=0.35)
EXT = dict(r=0.08, h=0.45, off=0.02)
ESTOP = dict(r=0.04, h=1.00, box=(0.16, 0.12, 0.14), lid=(0.17, 0.13, 0.02), btn_r=0.035)
AKJA = dict(length=2.0, width=0.60, depth=0.22, shell=0.008, lean=75.0)
SPINE = dict(length=1.85, width=0.45, t=0.05, lean=85.0, chamfer=0.06)
TOOLS = dict(lean=75.0, gap=0.35, wall=0.32)

# --------------------------------------------------------------------------------------
# Materials (linear albedo, cs2_bake.MaterialSpec fields). The gear / safety keys are
# cs2_liftdress.DRESS_MATERIALS' (read at build time); three kit keys are new.
# --------------------------------------------------------------------------------------


def _lin(c):
    c = float(c)
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def _lin3(rgb):
    return tuple(round(_lin(v), 6) for v in rgb)


KIT_MATERIALS = {
    "hose_canvas": dict(albedo=_lin3((0.80, 0.76, 0.66)), metal=0.0, rough=0.88),
    "brass": dict(albedo=_lin3((0.78, 0.60, 0.30)), metal=1.0, rough=0.38),
    "label_white": dict(albedo=_lin3((0.88, 0.88, 0.86)), metal=0.0, rough=0.55),
}
REUSED_KEYS = ("accent", "trim_white", "ski_black", "ski_blue", "ski_red", "galv", "fire_red",
               "safety_yellow", "safety_black", "darksteel", "rubber")

#: post_compose painters (sRGB targets)
LABEL_SRGB = (0.90, 0.90, 0.88)
INK_SRGB = (0.06, 0.06, 0.06)
STRAP_SRGB = (0.05, 0.05, 0.05)
SIGN_RED_SRGB = (0.70, 0.08, 0.06)

#: ColorProperties fallback sets (slot-2 targets / RECOLOR_ALBEDO, the hydrant compensation:
#: SkiHydrantBoxA Mesh 1.0 / 0.3125 / 0.125 = PAD_RGB / 0.80, clamped to 1)
FALLBACK_SLOT2 = ((0.85, 0.25, 0.10), (0.10, 0.30, 0.75), (0.30, 0.60, 0.15), (0.90, 0.72, 0.08))


def material_table():
    """{key: MaterialSpec dict} for every key the kit uses (DRESS_MATERIALS read-only + KIT)."""
    import cs2_liftdress as LD
    out = {k: dict(LD.DRESS_MATERIALS[k]) for k in REUSED_KEYS}
    out.update({k: dict(v) for k, v in KIT_MATERIALS.items()})
    return out


# ======================================================================================
# Scene helpers
# ======================================================================================

def clear_scene():
    """Delete every object and orphaned data (headless builds only)."""
    if bpy.context.object is not None and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.images, bpy.data.cameras,
                 bpy.data.lights, bpy.data.curves):
        for item in list(coll):
            if item.users == 0:
                coll.remove(item)


def _link(obj):
    bpy.context.scene.collection.objects.link(obj)
    return obj


def _remove(obj):
    me = obj.data
    bpy.data.objects.remove(obj, do_unlink=True)
    if me is not None and me.users == 0:
        bpy.data.meshes.remove(me)


def _world_co(obj):
    me = obj.data
    co = np.empty(len(me.vertices) * 3, dtype=np.float64)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    M = np.array(obj.matrix_world, dtype=np.float64)
    return co @ M[:3, :3].T + M[:3, 3]


def _local_co(me):
    co = np.empty(len(me.vertices) * 3, dtype=np.float64)
    me.vertices.foreach_get("co", co)
    return co.reshape(-1, 3)


def _bake_world(obj):
    """Apply obj.matrix_world into the mesh (no operator); identity afterwards."""
    M = obj.matrix_world.copy()
    if M == Matrix.Identity(4):
        return obj
    if M.determinant() < 0.0:
        raise RuntimeError(f"{obj.name}: negative-determinant transform (mirror) refused")
    obj.data.transform(M)
    obj.matrix_world = Matrix.Identity(4)
    obj.data.update()
    return obj


def _move(obj, offset):
    obj.matrix_world = Matrix.Translation(Vector(offset)) @ obj.matrix_world


# ======================================================================================
# Low-poly mesh builder with explicit UVs (meters, positive winding, isotropic)
# ======================================================================================

class LowMesh:
    """Vertices, faces and per-face-loop UVs (meters). Faces are given in the order whose
    right-hand normal is the outward normal; every helper below assigns UVs whose winding is
    positive with respect to that normal (no mirrored islands, DK5)."""

    def __init__(self):
        self.V, self.F, self.UV = [], [], []

    def v(self, p):
        self.V.append((float(p[0]), float(p[1]), float(p[2])))
        return len(self.V) - 1

    def face(self, idx, uv):
        if len(idx) != len(uv):
            raise ValueError("face / uv length mismatch")
        self.F.append(tuple(int(i) for i in idx))
        self.UV.append([(float(a), float(b)) for a, b in uv])

    # -- primitives --------------------------------------------------------------------
    def loft(self, rings, closed=True, u0=0.0, v0=0.0):
        """Quads between consecutive rings (lists of points, equal counts). Face k,i =
        (r_k[i], r_k[i+1], r_k+1[i+1], r_k+1[i]); normal = t_u x t_v with u along the ring
        order and v along the ring sequence: order the rings / ring points for the side you
        want. UV u = arc length along each ring, v = cumulative mean ring spacing (meters).
        Returns the vertex index rings."""
        idx = [[self.v(p) for p in ring] for ring in rings]
        n = len(rings[0])
        arcs = []
        for ring in rings:
            P = [Vector(p) for p in ring]
            seq = P + ([P[0]] if closed else [])
            acc = [0.0]
            for a, b in zip(seq, seq[1:]):
                acc.append(acc[-1] + (b - a).length)
            arcs.append(acc)
        vs = [v0]
        for ra, rb in zip(rings, rings[1:]):
            d = sum((Vector(b) - Vector(a)).length for a, b in zip(ra, rb)) / len(ra)
            vs.append(vs[-1] + d)
        m = n if closed else n - 1
        for k in range(len(rings) - 1):
            for i in range(m):
                j = (i + 1) % n
                self.face((idx[k][i], idx[k][j], idx[k + 1][j], idx[k + 1][i]),
                          ((u0 + arcs[k][i], vs[k]), (u0 + arcs[k][i + 1], vs[k]),
                           (u0 + arcs[k + 1][i + 1], vs[k + 1]), (u0 + arcs[k + 1][i], vs[k + 1])))
        return idx

    def planar(self, idx, normal, fan=False):
        """A planar polygon (vertex indices) facing `normal`: reordered so its winding normal is
        `normal`, UV = projection on (e1, e2) with e1 x e2 = normal. fan=True triangulates
        from the first vertex (no n-gons in the shipped mesh)."""
        N = Vector(normal).normalized()
        P = [Vector(self.V[i]) for i in idx]
        c = sum(P, Vector()) / len(P)
        wn = Vector()
        for a, b in zip(P, P[1:] + P[:1]):
            wn += (a - c).cross(b - c)
        if wn.dot(N) < 0.0:
            idx, P = list(reversed(idx)), list(reversed(P))
        e1 = (P[1] - P[0])
        e1 = (e1 - N * e1.dot(N)).normalized()
        e2 = N.cross(e1)
        uv = [((p - P[0]).dot(e1), (p - P[0]).dot(e2)) for p in P]
        if fan and len(idx) > 4:
            for k in range(1, len(idx) - 1):
                self.face((idx[0], idx[k], idx[k + 1]), (uv[0], uv[k], uv[k + 1]))
        else:
            self.face(idx, uv)

    def quad(self, pts, normal):
        self.planar([self.v(p) for p in pts], normal)

    def box(self, c, axes, half, drop=()):
        """Oriented box as ONE UV island (a net): the four faces around the strip axis (the axis
        of a dropped face, else the longest) unrolled as a strip, the two caps folded out of the
        +c face's ends; every face keeps a positive winding. drop = local keys "+X" .. "-Z".
        One island keeps the edges inside it, where the baked bevels are (and fewer margins)."""
        C = Vector(c)
        Ax = [Vector(a).normalized() for a in axes]
        h = [float(x) for x in half]
        if Ax[0].cross(Ax[1]).dot(Ax[2]) < 0.0:            # left-handed axes: flip the third
            Ax[2] = -Ax[2]
            drop = tuple({"+Z": "-Z", "-Z": "+Z"}.get(d_, d_) for d_ in drop)
        names = ("X", "Y", "Z")
        both = [i for i in range(3) if ("+" + names[i]) in drop and ("-" + names[i]) in drop]
        k = both[0] if both else max(range(3), key=lambda i: (h[i], -i))
        i_, j_ = (k + 1) % 3, (k + 2) % 3                  # (k, i, j) cyclic = right-handed
        ha, hb, hc = h[k], h[i_], h[j_]
        Hb, Hc = 2.0 * hb, 2.0 * hc
        V = {}

        def vert(sk, si, sj):
            key = (sk, si, sj)
            if key not in V:
                V[key] = self.v(C + Ax[k] * (sk * ha) + Ax[i_] * (si * hb) + Ax[j_] * (sj * hc))
            return V[key]

        def keyof(ax, sg):
            return ("+" if sg > 0 else "-") + names[ax]
        # (a, b, c) = (k, i, j) local coordinates in meters from the low corner
        faces = []
        if keyof(j_, 1) not in drop:                       # +c: u = a, v = b
            faces.append(([(-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1)],
                          lambda a, b, cc: (a, b)))
        if keyof(i_, 1) not in drop:                       # +b: u = a, v = Hb + (Hc - c)
            faces.append(([(-1, 1, 1), (1, 1, 1), (1, 1, -1), (-1, 1, -1)],
                          lambda a, b, cc: (a, Hb + (Hc - cc))))
        if keyof(j_, -1) not in drop:                      # -c: u = a, v = Hb + Hc + (Hb - b)
            faces.append(([(-1, 1, -1), (1, 1, -1), (1, -1, -1), (-1, -1, -1)],
                          lambda a, b, cc: (a, Hb + Hc + (Hb - b))))
        if keyof(i_, -1) not in drop:                      # -b: u = a, v = 2 Hb + Hc + c
            faces.append(([(-1, -1, -1), (1, -1, -1), (1, -1, 1), (-1, -1, 1)],
                          lambda a, b, cc: (a, 2.0 * Hb + Hc + cc)))
        attach = Hc <= 0.5 * ha and keyof(j_, 1) not in drop
        if keyof(k, 1) not in drop:                        # +a cap off the +c face's end
            faces.append(([(1, -1, 1), (1, -1, -1), (1, 1, -1), (1, 1, 1)],
                          (lambda a, b, cc: (2.0 * ha + (Hc - cc), b)) if attach else
                          (lambda a, b, cc: (10.0 + (Hc - cc), b))))
        if keyof(k, -1) not in drop:                       # -a cap
            faces.append(([(-1, -1, -1), (-1, -1, 1), (-1, 1, 1), (-1, 1, -1)],
                          (lambda a, b, cc: (-(Hc - cc), b)) if attach else
                          (lambda a, b, cc: (-10.0 - (Hc - cc), b))))
        for corners, fn in faces:
            idx = [vert(*cn) for cn in corners]
            uv = [fn((cn[0] + 1) * ha, (cn[1] + 1) * hb, (cn[2] + 1) * hc) for cn in corners]
            P = [Vector(self.V[q]) for q in idx]
            nrm = (P[1] - P[0]).cross(P[2] - P[1])
            cen = sum(P, Vector()) / 4.0
            if nrm.dot(cen - C) < 0.0:
                raise RuntimeError("LowMesh.box: inward face")
            self.face(idx, uv)

    def obj(self, name):
        me = bpy.data.meshes.new(name)
        me.from_pydata(self.V, [], self.F)
        me.update()
        lay = me.uv_layers.new(name="UVMap")
        flat = [uv for f in self.UV for uv in f]
        if len(flat) != len(me.loops):
            raise RuntimeError(f"{name}: {len(flat)} UVs for {len(me.loops)} loops")
        # from_pydata keeps face and loop order
        lay.data.foreach_set("uv", np.array(flat, dtype=np.float32).ravel())
        return _link(bpy.data.objects.new(name, me))


def ring_pts(c, axis, r, n, phase=0.0, ref=None, ccw=True):
    """n points on a circle of radius r around `axis` through c; CCW seen from +axis when
    ccw (a loft along +axis then faces OUTWARD)."""
    ax = Vector(axis).normalized()
    ref = Vector(ref) if ref is not None else (Vector((0, 0, 1)) if abs(ax.z) < 0.9
                                                else Vector((1, 0, 0)))
    t = (ref - ax * ref.dot(ax)).normalized()
    b = ax.cross(t)
    out = []
    for k in range(n):
        a = phase + 2.0 * math.pi * k / n * (1.0 if ccw else -1.0)
        out.append(Vector(c) + (t * math.cos(a) + b * math.sin(a)) * r)
    return out


def circ(r, n):
    """Vertex radius of an n-gon whose flats are tangent to a circle of radius r."""
    return r / math.cos(math.pi / n)


# ======================================================================================
# Hi-poly builder (bmesh; no UVs needed)
# ======================================================================================

class HiGeo:
    def __init__(self):
        self.bm = bmesh.new()

    def merge(self, bm):
        me = bpy.data.meshes.new("_dk_tmp")
        bm.to_mesh(me)
        bm.free()
        self.bm.from_mesh(me)
        bpy.data.meshes.remove(me)

    def loft(self, rings, closed=True, cap0=False, cap1=False):
        vs = [[self.bm.verts.new(tuple(p)) for p in ring] for ring in rings]
        n = len(rings[0])
        m = n if closed else n - 1
        for k in range(len(vs) - 1):
            for i in range(m):
                j = (i + 1) % n
                self.bm.faces.new((vs[k][i], vs[k][j], vs[k + 1][j], vs[k + 1][i]))
        if cap0 and closed:
            self.bm.faces.new(list(reversed(vs[0])))
        if cap1 and closed:
            self.bm.faces.new(vs[-1])
        return vs

    def tube(self, p0, p1, r, segs=16, cap0=True, cap1=True, r1=None):
        ax = Vector(p1) - Vector(p0)
        a = ring_pts(p0, ax, r, segs)
        b = ring_pts(p1, ax, r if r1 is None else r1, segs)
        self.loft([a, b], True, cap0, cap1)

    def sweep(self, pts, r, segs=12, closed_path=False, cap0=True, cap1=True):
        """Tube along a polyline with parallel-transported rings."""
        P = [Vector(p) for p in pts]
        rings = []
        ref = None
        for i, p in enumerate(P):
            if closed_path:
                t = (P[(i + 1) % len(P)] - P[i - 1]).normalized()
            elif i == 0:
                t = (P[1] - P[0]).normalized()
            elif i == len(P) - 1:
                t = (P[-1] - P[-2]).normalized()
            else:
                t = ((P[i] - P[i - 1]).normalized() + (P[i + 1] - P[i]).normalized()).normalized()
            if ref is None:
                ref = Vector((0, 0, 1)) if abs(t.z) < 0.9 else Vector((1, 0, 0))
            ref = (ref - t * ref.dot(t)).normalized()
            rings.append(ring_pts(p, t, r, segs, ref=ref))
        if closed_path:
            rings.append(rings[0])
            vs = self.loft(rings, True)
            # weld the duplicated closing ring
            bmesh.ops.remove_doubles(self.bm, verts=vs[0] + vs[-1], dist=1e-7)
        else:
            self.loft(rings, True, cap0, cap1)

    def box(self, c, axes, half, bevel=0.0, segs=2, drop=(), bevel_open=False):
        """Oriented box; bevel = rounded edges (inside the box); drop removes faces (open bars:
        only the non-boundary edges are bevelled)."""
        bm = bmesh.new()
        C = Vector(c)
        Ax = [Vector(a).normalized() for a in axes]
        h = [float(x) for x in half]
        V = {}
        for sx in (-1, 1):
            for sy in (-1, 1):
                for sz in (-1, 1):
                    V[(sx, sy, sz)] = bm.verts.new(C + Ax[0] * (sx * h[0]) + Ax[1] * (sy * h[1])
                                                   + Ax[2] * (sz * h[2]))
        names = ("X", "Y", "Z")
        for i in range(3):
            j, k = [a for a in range(3) if a != i]
            for s in (1, -1):
                if (("+" if s > 0 else "-") + names[i]) in drop:
                    continue
                cyc = []
                for (a, b) in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
                    idx = [0, 0, 0]
                    idx[i], idx[j], idx[k] = s, a, b
                    cyc.append(V[tuple(idx)])
                f = bm.faces.new(cyc)
                f.normal_update()
                if f.normal.dot(Ax[i] * s) < 0:
                    f.normal_flip()
        if bevel > 0.0:
            edges = [e for e in bm.edges if not e.is_boundary and len(e.link_faces) == 2]
            if edges:
                bmesh.ops.bevel(bm, geom=edges, offset=float(bevel), segments=int(segs),
                                profile=0.5, affect="EDGES", clamp_overlap=True)
        self.merge(bm)

    def band(self, c, axis, z0, z1, r0, r1, ch=0.0015, segs=32):
        """A raised band around an axis: radius r0 (the surface) -> r1 over a chamfer `ch` at both
        ends, open at r0 (it sits on the surface): the step edges carry the baked detail."""
        A_ = Vector(axis).normalized()
        C_ = Vector(c)
        rings = [ring_pts(C_ + A_ * z, A_, r, segs) for z, r in
                 ((z0, r0), (z0 + ch, r1), (z1 - ch, r1), (z1, r0))]
        self.loft(rings, True)

    def bolt(self, c, n, r=0.008, h=0.002, segs=6):
        """Bolt head on a surface, from 1 mm under it to h above (h <= 2.5 mm): a short collar,
        then a chamfered / domed crown to a 0.45 r flat, so the whole head tilts in the normal
        map (a flat-topped cylinder bakes as one flat texel disc with a 1-texel rim)."""
        N = Vector(n).normalized()
        C_ = Vector(c)
        n_ = max(8, int(segs))
        rings = [ring_pts(C_ - N * 0.001, N, r, n_), ring_pts(C_ + N * (0.3 * h), N, r, n_),
                 ring_pts(C_ + N * (0.75 * h), N, 0.78 * r, n_), ring_pts(C_ + N * h, N, 0.45 * r, n_)]
        self.loft(rings, True, cap0=False, cap1=True)

    def obj(self, name):
        me = bpy.data.meshes.new(name)
        self.bm.normal_update()
        self.bm.to_mesh(me)
        self.bm.free()
        me.update()
        return _link(bpy.data.objects.new(name, me))


# ======================================================================================
# Part records
# ======================================================================================

class Part:
    """One low part: obj, matkey, prop code, role, share key (None = unique), density weight,
    and its hi twin(s)."""

    def __init__(self, obj, mat, code, role, key=None, w=1.0, hi=None):
        self.obj, self.mat, self.code, self.role, self.key, self.w = obj, mat, code, role, key, w
        self.hi = list(hi or [])


def _rename(obj, name):
    if name in bpy.data.objects and bpy.data.objects[name] is not obj:
        raise ValueError(f"duplicate part name {name}")
    obj.name = name
    obj.data.name = name
    return obj


def _smart_uv(obj):
    """Deterministic smart projection into a fresh "UVMap" (66 deg: a 16-seg ring is one
    island, box faces are islands of their own); every other UV / float loop layer removed."""
    me = obj.data
    for lay in [l.name for l in me.uv_layers]:
        me.uv_layers.remove(me.uv_layers[lay])
    for key in ("netu", "netv", "netf"):
        a = me.attributes.get(key)
        while a is not None:
            me.attributes.remove(a)
            a = me.attributes.get(key)
    me.uv_layers.new(name="UVMap")
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66.0), island_margin=0.0,
                             area_weight=0.0, correct_aspect=True, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    return obj


def _hull(pts):
    """Convex hull (Andrew's monotone chain), CCW, of (N, 2) points."""
    P = sorted(set((round(float(x), 9), round(float(y), 9)) for x, y in pts))
    if len(P) <= 2:
        return np.array(P, dtype=np.float64)

    def cross(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])
    lower, upper = [], []
    for p in P:
        while len(lower) >= 2 and cross(lower[-2], lower[-1], p) <= 0:
            lower.pop()
        lower.append(p)
    for p in reversed(P):
        while len(upper) >= 2 and cross(upper[-2], upper[-1], p) <= 0:
            upper.pop()
        upper.append(p)
    return np.array(lower[:-1] + upper[:-1], dtype=np.float64)


def _orient_islands(obj, layer="UVMap"):
    """Rotate every island of the SOURCE layer to its minimum-area rectangle, long side along v
    (cs2_uv.pack_unique packs AABBs without rotation: a diagonal strip wastes its whole box).
    Rotations keep the winding; the source layer only feeds the pack (sources sample UVTile or
    constants), so this changes the layout, never a texel's meaning."""
    me = obj.data
    lay = me.uv_layers[layer]
    uv = cs2_uv.loop_uvs(lay)
    start, total = cs2_uv.face_loop_table(me)
    for isl in cs2_uv.uv_islands(me, lay):
        idx = np.concatenate([np.arange(start[f], start[f] + total[f]) for f in isl])
        pts = uv[idx]
        hull = _hull(pts)
        if len(hull) < 3:
            continue
        best = None
        for i in range(len(hull)):
            e = hull[(i + 1) % len(hull)] - hull[i]
            if math.hypot(e[0], e[1]) < 1e-12:
                continue
            ang = math.atan2(e[1], e[0])
            c, s = math.cos(-ang), math.sin(-ang)
            q = hull @ np.array([[c, s], [-s, c]])
            ext = q.max(axis=0) - q.min(axis=0)
            area = float(ext[0] * ext[1])
            if best is None or area < best[0] - 1e-12:
                best = (area, ang, ext)
        _area, ang, ext = best
        theta = -ang + (math.pi / 2.0 if ext[0] > ext[1] else 0.0)
        c, s = math.cos(theta), math.sin(theta)
        ctr = pts.mean(axis=0)
        uv[idx] = (pts - ctr) @ np.array([[c, s], [-s, c]]) + ctr
    cs2_uv.set_loop_uvs(lay, uv)
    return obj


def _bar_net_uv(obj):
    """One-island net for a box bar along LOCAL x (cs2_kit._box_bm via cs2_liftdress._beam): the four
    long faces unrolled around x (u = x, v = perimeter), end caps as their own planar islands;
    every face keeps a positive winding (e1 x e2 = its normal)."""
    me = obj.data
    co = _local_co(me)
    lo, hi = co.min(axis=0), co.max(axis=0)
    hy, hz = float(hi[1] - lo[1]), float(hi[2] - lo[2])
    for lay in [l.name for l in me.uv_layers]:
        me.uv_layers.remove(me.uv_layers[lay])
    lay = me.uv_layers.new(name="UVMap")
    lv = np.empty(len(me.loops), dtype=np.int64)
    me.loops.foreach_get("vertex_index", lv)
    uv = np.zeros((len(me.loops), 2), dtype=np.float64)
    for p in me.polygons:
        n = p.normal
        for li in p.loop_indices:
            x, y, z = co[lv[li]]
            if n.z > 0.9:
                uv[li] = (x, y - lo[1])
            elif n.y > 0.9:
                uv[li] = (x, hy + (hi[2] - z))
            elif n.z < -0.9:
                uv[li] = (x, hy + hz + (hi[1] - y))
            elif n.y < -0.9:
                uv[li] = (x, 2 * hy + hz + (z - lo[2]))
            elif n.x > 0.9:                      # +X cap: e1 = y, e2 = x cross y = z
                uv[li] = (hi[0] + 0.1 + (y - lo[1]), z - lo[2])
            else:                                # -X cap: e1 = -y, e2 = -x cross -y = z
                uv[li] = (lo[0] - 0.1 - (y - lo[1]), z - lo[2])
    cs2_uv.set_loop_uvs(lay, uv)
    return obj


def _net_uv(obj):
    """cs2_bldg cross-net loop layers (netu / netv, meters) -> "UVMap" (one island per box)."""
    me = obj.data
    nu, nv, nf = me.attributes.get("netu"), me.attributes.get("netv"), me.attributes.get("netf")
    if nu is None or nv is None or nf is None:
        raise RuntimeError(f"{obj.name}: no net UV layers")
    flags = np.zeros(len(me.polygons), dtype=np.int32)
    nf.data.foreach_get("value", flags)
    if not flags.all():
        raise RuntimeError(f"{obj.name}: {int((flags == 0).sum())} faces without a net")
    u = np.empty(len(me.loops), dtype=np.float32)
    v = np.empty(len(me.loops), dtype=np.float32)
    nu.data.foreach_get("value", u)
    nv.data.foreach_get("value", v)
    for key in ("netu", "netv", "netf"):
        a = me.attributes.get(key)
        while a is not None:
            me.attributes.remove(a)
            a = me.attributes.get(key)
    for lay in [l.name for l in me.uv_layers]:
        me.uv_layers.remove(me.uv_layers[lay])
    lay = me.uv_layers.new(name="UVMap")
    lay.data.foreach_set("uv", np.stack([u, v], axis=1).ravel())
    return obj


def _to_local(obj, M):
    """Rewrite obj's (identity-matrix, world-coordinate) mesh in the frame M (proper rigid) and
    set obj.matrix_world = M."""
    if abs(M.determinant() - 1.0) > 1e-6:
        raise RuntimeError(f"{obj.name}: frame is not a proper rotation (det {M.determinant()})")
    _bake_world(obj)
    obj.data.transform(M.inverted())
    obj.data.update()
    obj.matrix_world = M
    return obj


def _same_local(a, b, tol=1e-5):
    """Max abs difference of two meshes' local vertex arrays (inf when topology differs)."""
    if len(a.vertices) != len(b.vertices) or len(a.polygons) != len(b.polygons):
        return float("inf")
    for pa, pb in zip(a.polygons, b.polygons):
        if tuple(pa.vertices) != tuple(pb.vertices):
            return float("inf")
    return float(np.abs(_local_co(a) - _local_co(b)).max())


class Library:
    """Rigid-copy library: the first part under a key is canonical; later parts must equal it
    in their own frame (<= 1e-5 m) and take an exact copy of its (UV-unwrapped) mesh."""

    def __init__(self):
        self.canon = {}
        self.max_dev = 0.0

    def adopt(self, key, obj, unwrap):
        if key not in self.canon:
            unwrap(obj)
            _orient_islands(obj)
            self.canon[key] = obj
            return obj
        c = self.canon[key]
        dev = _same_local(c.data, obj.data)
        if dev > 1e-5:
            raise RuntimeError(f"library {key}: {obj.name} differs from {c.name} by {dev}")
        self.max_dev = max(self.max_dev, dev)
        old = obj.data
        obj.data = c.data.copy()
        obj.data.name = obj.name
        bpy.data.meshes.remove(old)
        return obj


# ======================================================================================
# Gear items: cs2_bldg.ski_gear at the real placement, rigid copies of a library item
# ======================================================================================

def item_frame(kind, F, T):
    """(M, O, d, a, n) of a ski_gear item standing at foot F leaning on T: the builder's own
    vectors (cs2_bldg.ski_gear: d foot->lean, a = z x horizontal, n = d x a flipped toward the
    foot side, O = F + n * GEAR_BACK). M is the PROPER frame (d, a, d x a, O): ski_gear's
    (d, a, n) is left-handed (n = -(d x a) always), a mirror would flip the UV winding."""
    import cs2_bldg as K
    F, T = Vector(F), Vector(T)
    d = (T - F).normalized()
    hz = Vector((F.x - T.x, F.y - T.y, 0.0))
    a = Vector((0.0, 0.0, 1.0)).cross(hz).normalized()
    n = d.cross(a).normalized()
    if n.dot(hz) < 0.0:
        n = -n
    O = F + n * K.GEAR_BACK[kind]
    z = d.cross(a)
    M = Matrix(((d.x, a.x, z.x, O.x), (d.y, a.y, z.y, O.y), (d.z, a.z, z.z, O.z),
                (0.0, 0.0, 0.0, 1.0)))
    return M, O, d, a, n


def _interp(stations, s):
    """(half width, curl) of a cs2_bldg station table at s / L (cs2_bldg._hw_at, linear)."""
    for (s0, h0, c0), (s1, h1, c1) in zip(stations, stations[1:]):
        if s0 - 1e-9 <= s <= s1 + 1e-9:
            f = (s - s0) / max(s1 - s0, 1e-9)
            return h0 + (h1 - h0) * f, c0 + (c1 - c0) * f
    return stations[-1][1], stations[-1][2]


def _fine_stations(stations, step):
    s = {round(st[0], 6) for st in stations}
    k = 0
    while k * step < 1.0:
        s.add(round(k * step, 6))
        k += 1
    s.add(1.0)
    return sorted(s)


def hi_ski_pair(g, O, d, a, n, L):
    """Hi twin of cs2_bldg._ski_pair_into (no bindings): per ski a closed solid between the base
    plane (0.003 off the pair plane) and the top sheet (SKI_TH), crowned 1.5 mm, 3 mm edge
    chamfers, the tip curl of SKI_STATIONS; dense stations along the ski."""
    import cs2_bldg as K
    st = K.SKI_STATIONS
    for sg in (1.0, -1.0):
        rings = []
        for s in _fine_stations(st, 0.025):
            hw, curl = _interp(st, s)
            c = O + d * (s * L)
            hb, ht = 0.003 + curl, K.SKI_TH + curl
            ch = min(0.003, 0.3 * hw)
            sec = ((-hw, hb), (-hw * 0.5, hb), (hw * 0.5, hb), (hw, hb), (hw, ht - ch),
                   (hw - ch, ht), (hw * 0.5, ht + 0.0011), (0.0, ht + 0.0015),
                   (-hw * 0.5, ht + 0.0011), (-hw + ch, ht), (-hw, ht - ch))
            rings.append([c + a * u + n * (sg * h) for u, h in sec])
        g.loft(rings, True, cap0=True, cap1=True)
    bmesh.ops.recalc_face_normals(g.bm, faces=g.bm.faces[:])     # two closed solids only


def hi_board(g, O, d, a, n, L, colour):
    """Hi twin of cs2_bldg._board_into: a 12 mm closed solid with rounded edges and the nose /
    tail kick, the two graphic bands raised 3 mm (= the low stripe quads), 8 insert screws."""
    import cs2_bldg as K
    st = K.BOARD_STATIONS
    th = K.BOARD_TH
    rings = []
    for s in _fine_stations(st, 0.02):
        hw, kick = _interp(st, s)
        c = O + d * (s * L) + n * kick
        r = min(0.002, 0.3 * hw)
        sec = ((-hw + r, -th), (hw - r, -th), (hw, -th + r), (hw, th - r), (hw - r, th),
               (hw * 0.5, th + 0.0008), (0.0, th + 0.001), (-hw * 0.5, th + 0.0008),
               (-hw + r, th), (-hw, th - r), (-hw, -th + r))
        rings.append([c + a * u + n * h for u, h in sec])
    g.loft(rings, True, cap0=True, cap1=True)
    bmesh.ops.recalc_face_normals(g.bm, faces=g.bm.faces[:])     # the closed solid only
    stripe = None if isinstance(colour, str) else colour[1]
    if stripe:
        for b0, b1 in K.BOARD_STRIPES:
            h0, _ = _interp(st, b0)
            h1, _ = _interp(st, b1)
            hwm = 0.97 * min(h0, h1)
            c = O + d * (0.5 * (b0 + b1) * L) + n * (th + 0.0015)
            g.box(c, (d, a, n), (0.5 * (b1 - b0) * L, hwm, 0.0015), bevel=0.0008, segs=1)
    for sb in K.BOARD_BINDINGS:
        for ds in (-0.02, 0.02):
            for da in (-0.04, 0.04):
                g.bolt(O + d * (sb * L + ds) + a * da + n * th, n, r=0.005, h=0.0012, segs=8)


def hi_poles(g, O, d, a, n, L, colour):
    """Hi twin of cs2_bldg._poles_into: 10-sided shafts INSCRIBED in the 4-sided low (r cos 45),
    16-sided grips with three finger grooves inside the 36 mm grip box, a 3 mm basket plate under
    the low diamond with a raised ring and spokes."""
    import cs2_bldg as K
    for sa in (-0.022, 0.022):
        p0 = O + a * sa
        p1 = p0 + d * L
        g.tube(p0, p1 - d * 0.16, K.POLE_R * math.cos(math.pi / 4.0), 10)
        rings = []
        for t, r in ((0.0, 0.0150), (0.02, 0.0165), (0.045, 0.0165), (0.05, 0.0150),
                     (0.055, 0.0165), (0.08, 0.0165), (0.085, 0.0150), (0.09, 0.0165),
                     (0.15, 0.0165), (0.16, 0.0140)):
            rings.append(ring_pts(p1 - d * 0.16 + d * t, d, r, 16))
        g.loft(rings, True, cap0=True, cap1=True)
        bc = p0 + d * 0.10
        corners = [bc + a * 0.039, bc + n * 0.039, bc - a * 0.039, bc - n * 0.039]
        bm = bmesh.new()
        top = [bm.verts.new(c) for c in corners]
        bot = [bm.verts.new(c - d * 0.003) for c in corners]
        bm.faces.new(top)
        bm.faces.new(list(reversed(bot)))
        for i in range(4):
            j = (i + 1) % 4
            bm.faces.new((top[i], bot[i], bot[j], top[j]))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        g.merge(bm)
        for k in range(4):
            ang = math.pi / 4.0 + k * math.pi / 2.0
            u = a * math.cos(ang) + n * math.sin(ang)
            g.box(bc + u * 0.014 + d * 0.0008, (u, u.cross(d).normalized(), d), (0.011, 0.0015, 0.0008))
        g.tube(bc, bc + d * 0.0015, 0.008, 12)


HI_GEAR = {"ski": hi_ski_pair, "board": hi_board, "poles": hi_poles}


class GearBuilder:
    """Places library items: low parts via cs2_bldg.ski_gear at the real placement (then moved into
    their proper frame and checked against the library item), hi via the hi twin once per item."""

    def __init__(self, lib):
        self.lib = lib
        self.hi_local = {}          # gear key -> hi Mesh in the item frame
        self.count = 0
        self.tris = {}

    def place(self, code, tag, gkey, F, T, parts_out):
        import cs2_bldg as K
        kind, colour, L = GEAR[gkey]
        self.count += 1
        built = K.ski_gear(f"dk{code}{tag}", [(kind, tuple(F), tuple(T), colour, L)])
        M, O, d, a, n = item_frame(kind, F, T)
        tris = 0
        first = len(parts_out)
        for o, mat in built:
            gc = K.GEAR_CODE.get(mat, mat)
            for ci, faces in enumerate(_components(o.data)):
                role = f"g{tag}{gc}{ci}"
                sub = _subset(o, faces, f"{code}_{role}")
                nsum = Vector()
                for p in sub.data.polygons:
                    nsum += p.normal * p.area
                back = kind != "poles" and nsum.dot(n) < -0.3 * max(nsum.length, 1e-12)
                _to_local(sub, M)
                # the local-geometry hash is part of the key: cs2_bldg._Geo.cyl phases its rings
                # from WORLD x when the axis is steep, so the A-frame's back-side pole shafts are
                # not rotation copies of the front ones (they get their own island)
                key = ("gear", gkey, mat, ci, _geom_key(sub.data, "g")[1])
                self.lib.adopt(key, sub, _smart_uv)
                tris += cs2_asset.tri_count(sub)
                parts_out.append(Part(sub, mat, code, role, key=key, w=GEAR_BACK_W if back else 1.0))
            _remove(o)
        self.tris[gkey] = tris
        if gkey not in self.hi_local:
            g = HiGeo()
            HI_GEAR[kind](g, *((O, d, a, n, L) if kind == "ski" else (O, d, a, n, L, colour)))
            ho = g.obj(f"_dkhi_{gkey}")
            _to_local(ho, M)
            self.hi_local[gkey] = ho.data
            bpy.data.objects.remove(ho, do_unlink=True)
        hi = _link(bpy.data.objects.new(f"{code}_g{tag}_hi", self.hi_local[gkey].copy()))
        hi.matrix_world = M
        parts_out[first].hi.append(hi)
        return built


# ======================================================================================
# Racks
# ======================================================================================

def aframe_pose(j, sg):
    """(F, T) of A-frame slot j on side sg = cs2_bldg.ski_rack_frame's foot / lean point
    (x 0, y 0, along "X", z0 0: P(s, c, z) = (s, -c, z))."""
    import cs2_bldg as K
    w = AF["slots"] * AF["pitch"] + 0.10
    pitch = (w - 0.10) / AF["slots"]
    s_end = w / 2.0 - 0.05
    k = AF["spread"] / (AF["h"] - AF["zb"])
    z_leg = K.GROUND_LIFT + AF["bar"] / 2.0
    c_leg = AF["spread"] + (AF["zb"] - z_leg) * k
    sc = -s_end + (j + 0.5) * pitch
    F = Vector((sc, -sg * (c_leg + 0.075), 0.035))
    T = Vector((sc, -sg * 0.06, AF["h"] + 0.04))
    return F, T


def wall_pose(i, n_items):
    """(F, T) of wall-rack item i = cs2_liftdress.ski_rack's foot / lean point."""
    import cs2_bldg as K
    import cs2_liftdress as LD
    x0, x1, y, z0 = WR["x0"], WR["x1"], WR["y"], WR["z0"]
    pitch = (x1 - x0 - 0.30) / max(1, n_items)
    sg = -1.0 if WR["side"] < 0.0 else 1.0
    zt = z0 + WR["h"]
    zf = max(float(z0), K.GROUND_LIFT) + 0.015
    x = x0 + 0.15 + (i + 0.5) * pitch
    F = Vector((x, y + sg * WR["stand_off"], zf))
    T = Vector((x, y + sg * LD.GEAR_LEAN_PAD, zt + LD.GEAR_LEAN_PAD))
    return F, T


def _components(me):
    """Connected face components (lists of polygon indices, ascending), ordered by first face."""
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    seen, comps = set(), []
    for f in bm.faces:
        if f.index in seen:
            continue
        stack, comp = [f], []
        seen.add(f.index)
        while stack:
            x = stack.pop()
            comp.append(x.index)
            for e in x.edges:
                for y in e.link_faces:
                    if y.index not in seen:
                        seen.add(y.index)
                        stack.append(y)
        comps.append(sorted(comp))
    bm.free()
    return comps


def _subset(obj, faces, name):
    """New object with only `faces` of obj (world coordinates, identity matrix)."""
    o = obj.copy()
    o.data = obj.data.copy()
    _link(o)
    _bake_world(o)
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bm.faces.ensure_lookup_table()
    keep = set(faces)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.index not in keep], context="FACES")
    loose = [v for v in bm.verts if not v.link_faces]
    if loose:
        bmesh.ops.delete(bm, geom=loose, context="VERTS")
    bm.to_mesh(o.data)
    bm.free()
    o.data.update()
    return _rename(o, name)


def _geom_key(me, tag, nd=4):
    co = np.round(_local_co(me), nd)
    h = hashlib.sha1(co.tobytes())
    for p in me.polygons:
        h.update(bytes(str(tuple(p.vertices)), "ascii"))
    return (tag, h.hexdigest()[:16])


def hi_bar(low_me, bolts=()):
    """Hi twin of a frame bar: the low box (local) with every non-boundary edge bevelled 4 mm,
    2 segments (inside the low), plus bolt heads (local point, normal)."""
    g = HiGeo()
    bm = bmesh.new()
    bm.from_mesh(low_me)
    for lay in list(bm.loops.layers.uv):
        bm.loops.layers.uv.remove(lay)
    edges = [e for e in bm.edges if not e.is_boundary and len(e.link_faces) == 2]
    bmesh.ops.bevel(bm, geom=edges, offset=0.004, segments=2, profile=0.5, affect="EDGES",
                    clamp_overlap=True)
    g.merge(bm)
    for c, nrm in bolts:
        g.bolt(c, nrm, r=0.008, h=0.002, segs=6)
    return g


def build_aframe(code, lib, gear):
    """A-frame rack variant `code`: the builder's frame split into bars (translation copies share)
    + the loadout's gear; the whole rack drops by GROUND_LIFT so its feet stand on z 0."""
    import cs2_bldg as K
    parts = []
    built = K.ski_rack_frame(f"dk{code}fr", 0.0, 0.0, slots=AF["slots"], along="X", skis=(),
                             gear=True)
    if len(built) != 1:
        raise RuntimeError(f"{code}: ski_rack_frame returned {len(built)} parts")
    frame = built[0][0]
    divider_x = [-(AF["slots"] * AF["pitch"]) / 2.0 + AF["pitch"] * i for i in range(AF["slots"] + 1)]
    for ci, faces in enumerate(_components(frame.data)):
        bar = _subset(frame, faces, f"{code}_fr{ci:02d}")
        co = _local_co(bar.data)
        lo = co.min(axis=0)
        M = Matrix.Translation(Vector(lo))
        bar.data.transform(M.inverted())
        bar.data.update()
        bar.matrix_world = M
        key = _geom_key(bar.data, "afbar")
        lib.adopt(key, bar, _net_uv)
        p = Part(bar, "galv", code, f"fr{ci:02d}", key=key, w=FRAME_W)
        if key not in lib.__dict__.setdefault("hi", {}):
            ext = co.max(axis=0) - lo
            bolts = []
            if ext[0] > 1.5 and ext[1] < 0.08:                  # rails along x: both faces
                yc, zc = 0.5 * ext[1], 0.5 * ext[2]
                for x in divider_x:
                    lx = x - lo[0]
                    if 0.02 < lx < ext[0] - 0.02:
                        for sy in (0.0, ext[1]):
                            nrm = (0.0, 1.0 if sy > 0 else -1.0, 0.0)
                            bolts.append(((lx, sy, zc), nrm))
            elif ext[1] > 0.9 and ext[2] < 0.06:                # foot bars along y: top face
                for y in (0.25, 0.75):
                    bolts.append(((0.5 * ext[0], y * ext[1], ext[2]), (0.0, 0.0, 1.0)))
            lib.hi[key] = hi_bar(lib.canon[key].data, bolts).obj(f"_dkhi_{key[1]}").data
        hi = _link(bpy.data.objects.new(f"{code}_fr{ci:02d}_hi", lib.hi[key].copy()))
        hi.matrix_world = M
        p.hi.append(hi)
        parts.append(p)
    _remove(frame)
    load = AFRAME_LOADOUTS[code]
    for sg in (+1, -1):
        for j, gk in enumerate(load[sg]):
            if gk is None:
                continue
            F, T = aframe_pose(j, sg)
            tag = f"{'f' if sg > 0 else 'b'}{j}"
            gear.place(code, tag, gk, F, T, parts)
    for p in parts:                                   # feet on z 0 (builder: GROUND_LIFT)
        _move(p.obj, (0.0, 0.0, -K.GROUND_LIFT))
        for h in p.hi:
            _move(h, (0.0, 0.0, -K.GROUND_LIFT))
    return parts


def build_wall_rack(code, lib, gear):
    """Wall rack variant `code`: cs2_liftdress.ski_rack's legs + rails (pairs 0: frame only) and
    the loadout placed with the builder's slot math; feet on z 0."""
    import cs2_liftdress as LD
    parts = []
    built = LD.ski_rack(f"dk{code}w", WR["x0"], WR["x1"], WR["y"], WR["z0"], pairs=0, boards=0,
                        side=WR["side"], h=WR["h"], stand_off=WR["stand_off"])
    bpy.context.view_layer.update()
    for o, mat in built:
        role = "leg" + o.name.split("_leg")[-1] if "_leg" in o.name else "rail" + o.name.split("_rail")[-1]
        _rename(o, f"{code}_{role}")
        M = o.matrix_world.copy()
        key = _geom_key(o.data, "wrbar")
        lib.adopt(key, o, _bar_net_uv)
        p = Part(o, mat, code, role, key=key, w=FRAME_W)
        hikey = ("wrhi",) + key[1:]
        hidict = lib.__dict__.setdefault("hi", {})
        if hikey not in hidict:
            ext = _local_co(o.data).max(axis=0) - _local_co(o.data).min(axis=0)
            bolts = []
            if role.startswith("rail"):
                half = 0.5 * ext[0]
                for lx in (WR["x0"] + 0.05, WR["x1"] - 0.05):
                    for sy in (-1.0, 1.0):
                        bolts.append(((lx, sy * 0.5 * ext[1], 0.0), (0.0, sy, 0.0)))
            hidict[hikey] = hi_bar(lib.canon[key].data, bolts).obj(f"_dkhi_{hikey[1]}").data
        hi = _link(bpy.data.objects.new(f"{code}_{role}_hi", hidict[hikey].copy()))
        hi.matrix_world = M
        p.hi.append(hi)
        parts.append(p)
    load = WALL_LOADOUTS[code]
    for i, gk in enumerate(load):
        F, T = wall_pose(i, len(load))
        gear.place(code, f"w{i}", gk, F, T, parts)
    bpy.context.view_layer.update()
    zmin = min(float(_world_co(p.obj)[:, 2].min()) for p in parts)
    for p in parts:
        _move(p.obj, (0.0, 0.0, -zmin))
        for h in p.hi:
            _move(h, (0.0, 0.0, -zmin))
    return parts


# ======================================================================================
# Hero parts (own low meshes to the builders' dimensions)
# ======================================================================================

AXIS_OUT = Vector((0.0, -1.0, 0.0))          # out of the wall = prefab +Z


def miter_ring(c, t0, t1, r, n, ref=(1.0, 0.0, 0.0)):
    """Ring of an n-sided tube at a bend t0 -> t1, in the bisecting plane: radius r across the
    bend, r / cos(theta / 2) in the bend plane (the exact miter of the two legs). Vertex phase =
    ring_pts(..., ref=ref) on both legs, so the loft does not twist."""
    t = (Vector(t0) + Vector(t1)).normalized()
    bn = Vector(t0).cross(Vector(t1)).normalized()
    inplane = bn.cross(t).normalized()
    half = 0.5 * math.acos(max(-1.0, min(1.0, Vector(t0).dot(Vector(t1)))))
    R = Vector(ref)
    e1 = (R - t * R.dot(t)).normalized()
    e2 = t.cross(e1)
    out = []
    for k in range(n):
        a = 2.0 * math.pi * k / n
        q = e1 * math.cos(a) + e2 * math.sin(a)
        out.append(Vector(c) + (bn * q.dot(bn) + inplane * (q.dot(inplane) / math.cos(half))) * r)
    return out


def _unique(parts, lm, name, mat, code, role, w=1.0, hi=()):
    o = _orient_islands(lm.obj(f"{code}_{role}"))
    parts.append(Part(o, mat, code, role, key=None, w=w, hi=list(hi)))
    return o


def build_hose_reel(code):
    """Wall hose reel (cs2_liftdress.hose_reel: r 0.30, drum 0.6 r, w 0.18, plate 0.70 x 0.02):
    plate bottom on z 0, back on the wall plane y 0, reel axis out of the wall (-y) at z 0.35;
    a wound-hose coil band, a hose tail with a brass nozzle, a brass inlet valve wheel."""
    H = HOSE
    parts = []
    cz = H["cz"]
    pl = H["plate"] / 2.0
    # plate
    lm = LowMesh()
    lm.box((0.0, -H["plate_t"] / 2.0, cz), ((1, 0, 0), (0, 1, 0), (0, 0, 1)),
           (pl, H["plate_t"] / 2.0, pl), drop=("+Y",))
    g = HiGeo()
    g.box((0.0, -H["plate_t"] / 2.0, cz), ((1, 0, 0), (0, 1, 0), (0, 0, 1)),
          (pl, H["plate_t"] / 2.0, pl), bevel=0.003, drop=("+Y",))
    for x, z in ((-0.31, 0.04), (0.31, 0.04), (-0.31, 0.66), (0.31, 0.66), (-0.325, cz), (0.325, cz)):
        g.bolt((x, -H["plate_t"], z), AXIS_OUT, r=0.009, h=0.002)
    _unique(parts, lm, "", "fire_red", code, "plate", w=0.6, hi=[g.obj(f"{code}_plate_hi")])
    # wall-side flange: rim + the visible front ANNULUS between the coil and the rim (the disc
    # under the coil is never seen: no texels for it)
    c0 = Vector((0.0, -0.02, cz))
    r16 = circ(H["r"], 16)
    lm = LowMesh()
    rings = [ring_pts(c0, AXIS_OUT, r16, 16), ring_pts(c0 + AXIS_OUT * 0.01, AXIS_OUT, r16, 16)]
    lm.loft(rings, True)
    fr = c0 + AXIS_OUT * 0.01
    # CCW rings, outer -> inner: normal = axis x e_r ... = +AXIS_OUT (faces out of the wall)
    lm.loft([ring_pts(fr, AXIS_OUT, r16, 16),
             ring_pts(fr, AXIS_OUT, circ(H["coil_r"], 16), 16)], True)
    g = HiGeo()
    g.tube(c0, c0 + AXIS_OUT * 0.01, H["r"] - 0.001, 48, cap0=False, cap1=True)
    _unique(parts, lm, "", "fire_red", code, "fl0", hi=[g.obj(f"{code}_fl0_hi")])
    # front flange (both faces + rim)
    c1 = Vector((0.0, -0.185, cz))
    lm = LowMesh()
    rings = [ring_pts(c1, AXIS_OUT, r16, 16), ring_pts(c1 + AXIS_OUT * 0.015, AXIS_OUT, r16, 16)]
    idx = lm.loft(rings, True)
    lm.planar(idx[1], AXIS_OUT, fan=True)
    lmb = LowMesh()
    lmb.planar([lmb.v(p) for p in ring_pts(c1, AXIS_OUT, r16, 16)], -AXIS_OUT, fan=True)
    _unique(parts, lmb, "", "fire_red", code, "fl1back", w=0.5, hi=[])
    g = HiGeo()
    g.tube(c1, c1 + AXIS_OUT * 0.015, H["r"] - 0.001, 48)
    front = c1 + AXIS_OUT * 0.015
    g.loft([ring_pts(front - AXIS_OUT * 0.001, AXIS_OUT, 0.06, 32),               # hub boss,
            ring_pts(front + AXIS_OUT * 0.0003, AXIS_OUT, 0.06, 32),               # chamfered
            ring_pts(front + AXIS_OUT * 0.0012, AXIS_OUT, 0.052, 32)], True, cap1=True)
    g.bolt(front + AXIS_OUT * 0.0011, AXIS_OUT, r=0.022, h=0.0013, segs=8)         # axle nut
    for k in range(6):
        ang = k * math.pi / 3.0
        g.bolt(front + Vector((0.17 * math.cos(ang), 0.0, 0.17 * math.sin(ang))), AXIS_OUT,
               r=0.009, h=0.0018)
    _unique(parts, lm, "", "fire_red", code, "fl1", hi=[g.obj(f"{code}_fl1_hi")])
    # wound hose coil (outer surface of the windings)
    lm = LowMesh()
    rc = circ(H["coil_r"], 16)
    lm.loft([ring_pts(c0 + AXIS_OUT * 0.01, AXIS_OUT, rc, 16),
             ring_pts(c1, AXIS_OUT, rc, 16)], True)
    g = HiGeo()
    rh = H["hose_r"]
    Rc = H["coil_r"] - rh
    ya, yb = -0.03 - rh, -0.185 + rh
    turns = (ya - yb) / (2.0 * rh)
    steps = int(math.ceil(turns * 24))
    pts = []
    for k in range(steps + 1):
        t = k / steps
        ang = 2.0 * math.pi * turns * t
        pts.append((Rc * math.cos(ang), ya - (ya - yb) * t, cz + Rc * math.sin(ang)))
    g.sweep(pts, rh * 0.98, 10)
    g.tube(c0 + AXIS_OUT * 0.01, c1, Rc - 0.002, 32, cap0=False, cap1=False)
    _unique(parts, lm, "", "hose_canvas", code, "coil", hi=[g.obj(f"{code}_coil_hi")])
    # hose tail + nozzle
    P0, P1, P2 = Vector((0.0, -0.11, 0.13)), Vector((0.0, -0.11, 0.04)), Vector((0.0, -0.26, 0.04))
    lm = LowMesh()
    rt = circ(rh, 6)
    t0, t1 = (P1 - P0).normalized(), (P2 - P1).normalized()
    rings = [ring_pts(P0, t0, rt, 6, ref=Vector((1, 0, 0))),
             miter_ring(P1, t0, t1, rt, 6),
             ring_pts(P2, t1, rt, 6, ref=Vector((1, 0, 0)))]
    lm.loft(rings, True)
    g = HiGeo()
    g.sweep([P0, P1 + (P0 - P1).normalized() * 0.02, P1 + (P2 - P1).normalized() * 0.02, P2], rh * 0.97, 12)
    _unique(parts, lm, "", "hose_canvas", code, "tail", hi=[g.obj(f"{code}_tail_hi")])
    N0, N1 = P2, P2 + Vector((0.0, -0.06, 0.0))
    lm = LowMesh()
    idx = lm.loft([ring_pts(N0, N1 - N0, circ(0.020, 6), 6, ref=Vector((1, 0, 0))),
                   ring_pts(N1, N1 - N0, circ(0.011, 6), 6, ref=Vector((1, 0, 0)))], True)
    lm.planar(idx[1], N1 - N0, fan=True)
    g = HiGeo()
    rings = []
    for t, r in ((0.0, 0.0215), (0.006, 0.0215), (0.008, 0.0192), (0.02, 0.0172), (0.022, 0.0180),
                 (0.026, 0.0160), (0.028, 0.0166), (0.032, 0.0150), (0.056, 0.0112), (0.06, 0.0104)):
        rings.append(ring_pts(N0 + (N1 - N0).normalized() * t, N1 - N0, r, 16, ref=Vector((1, 0, 0))))
    g.loft(rings, True, cap0=True, cap1=True)
    _unique(parts, lm, "", "brass", code, "nozzle", hi=[g.obj(f"{code}_nozzle_hi")])
    # inlet valve: stem + hand wheel on the plate, upper right
    vc = Vector((0.25, -0.02, 0.62))
    lm = LowMesh()
    lm.loft([ring_pts(vc, AXIS_OUT, circ(0.008, 8), 8), ring_pts(vc + AXIS_OUT * 0.03, AXIS_OUT,
                                                                  circ(0.008, 8), 8)], True)
    w0 = vc + AXIS_OUT * 0.03
    idx = lm.loft([ring_pts(w0, AXIS_OUT, circ(0.045, 12), 12),
                   ring_pts(w0 + AXIS_OUT * 0.01, AXIS_OUT, circ(0.045, 12), 12)], True)
    lm.planar(idx[1], AXIS_OUT, fan=True)
    g = HiGeo()
    g.tube(vc, w0, 0.0075, 12, cap0=False, cap1=False)
    g.tube(w0, w0 + AXIS_OUT * 0.0085, 0.044, 32)
    for k in range(4):
        ang = k * math.pi / 2.0 + math.pi / 4.0
        u = Vector((math.cos(ang), 0.0, math.sin(ang)))
        g.box(w0 + AXIS_OUT * 0.0095 + u * 0.022, (u, AXIS_OUT.cross(u), AXIS_OUT),
              (0.018, 0.003, 0.0012))
    g.tube(w0 + AXIS_OUT * 0.008, w0 + AXIS_OUT * 0.0105, 0.04, 32, cap0=False)
    g.tube(w0 + AXIS_OUT * 0.008, w0 + AXIS_OUT * 0.0108, 0.01, 12, cap0=False)
    _unique(parts, lm, "", "brass", code, "valve", hi=[g.obj(f"{code}_valve_hi")])
    return parts


def build_extinguisher(code):
    """Wall extinguisher (cs2_liftdress.extinguisher: r 0.08, h 0.45, 0.02 off the wall, bracket,
    valve head): bottom on z 0, back of the bracket on the wall plane y 0; 16-sided body
    circumscribed on the 32-sided hi, two dome bands, valve, lever, hose, bracket."""
    E = EXT
    parts = []
    cy = -(E["off"] + E["r"])
    C = lambda z: Vector((0.0, cy, z))   # noqa: E731
    Z = Vector((0, 0, 1))
    r16 = circ(E["r"], 16)
    lm = LowMesh()
    lm.loft([ring_pts(C(0.0), Z, r16, 16), ring_pts(C(0.40), Z, r16, 16)], True)
    g = HiGeo()
    g.loft([ring_pts(C(0.0), Z, E["r"] - 0.004, 32), ring_pts(C(0.004), Z, E["r"], 32),
            ring_pts(C(0.40), Z, E["r"], 32)], True)
    ax0 = C(0.0)
    g.band(ax0, Z, 0.14, 0.30, E["r"] - 0.0002, E["r"] + 0.0010, ch=0.001)        # label sleeve
    g.band(ax0, Z, 0.25, 0.285, E["r"] - 0.0002, E["r"] + 0.0020, ch=0.0015)      # bracket strap
    for z0_, z1_ in ((0.004, 0.022), (0.375, 0.398), (0.09, 0.098), (0.33, 0.338), (0.20, 0.205)):
        g.band(ax0, Z, z0_, z1_, E["r"] - 0.0002, E["r"] + 0.0016, ch=0.0012)    # chimes / beads
    _unique(parts, lm, "", "fire_red", code, "body", hi=[g.obj(f"{code}_body_hi")])
    lm = LowMesh()
    idx = lm.loft([ring_pts(C(0.40), Z, r16, 16), ring_pts(C(0.43), Z, 0.070, 16),
                   ring_pts(C(0.452), Z, 0.036, 16)], True)
    lm.planar(idx[2], Z, fan=True)
    g = HiGeo()
    rings = []
    for k in range(9):
        t = k / 8.0
        z = 0.40 + 0.045 * t
        r = max(0.018, E["r"] * math.sqrt(max(0.0, 1.0 - (t * 0.95) ** 2)))
        rings.append(ring_pts(C(z), Z, r, 32))
    rings.append(ring_pts(C(0.451), Z, 0.018, 32))
    g.loft(rings, True, cap0=False, cap1=True)
    _unique(parts, lm, "", "fire_red", code, "dome", hi=[g.obj(f"{code}_dome_hi")])
    X, Y = Vector((1, 0, 0)), Vector((0, 1, 0))
    lm = LowMesh()
    lm.box(C(0.48), (X, Y, Z), (0.025, 0.025, 0.03), drop=("-Z",))
    g = HiGeo()
    g.box(C(0.48), (X, Y, Z), (0.025, 0.025, 0.03), bevel=0.003, drop=("-Z",))
    g.tube(C(0.485) + Vector((0, -0.025, 0)), C(0.485) + Vector((0, -0.0265, 0)), 0.013, 24)
    g.tube(C(0.485) + Vector((0, -0.0265, 0)), C(0.485) + Vector((0, -0.0272, 0)), 0.003, 8, cap0=False)
    _unique(parts, lm, "", "safety_black", code, "valve", hi=[g.obj(f"{code}_valve_hi")])
    lm = LowMesh()
    lm.box(C(0.521), (X, Y, Z), (0.05, 0.008, 0.006))
    g = HiGeo()
    g.box(C(0.521), (X, Y, Z), (0.05, 0.008, 0.006), bevel=0.002)
    _unique(parts, lm, "", "darksteel", code, "lever", hi=[g.obj(f"{code}_lever_hi")])
    hose = [Vector((0.042, cy, 0.475)), Vector((0.078, cy - 0.02, 0.445)),
            Vector((0.1, cy - 0.02, 0.33)), Vector((0.1, cy - 0.012, 0.18))]
    lm = LowMesh()
    rings = []
    for i, p in enumerate(hose):
        t = (hose[min(i + 1, 3)] - hose[max(i - 1, 0)]).normalized()
        rings.append(ring_pts(p, t, circ(0.008, 6) * 1.03, 6, ref=Vector((0, 1, 0))))
    lm.loft(rings, True)
    g = HiGeo()
    g.sweep(hose, 0.0077, 12)
    g.tube(hose[-1], hose[-1] + (hose[-1] - hose[-2]).normalized() * 0.002, 0.0079, 10)
    _unique(parts, lm, "", "safety_black", code, "hose", hi=[g.obj(f"{code}_hose_hi")])
    lm = LowMesh()
    lm.box((0.0, -0.007, 0.28), (X, Y, Z), (0.03, 0.007, 0.03), drop=("+Y",))
    g = HiGeo()
    g.box((0.0, -0.007, 0.28), (X, Y, Z), (0.03, 0.007, 0.03), bevel=0.003, drop=("+Y",))
    for x in (-0.018, 0.018):
        g.bolt((x, -0.014, 0.292), AXIS_OUT, r=0.005, h=0.0015)
    _unique(parts, lm, "", "darksteel", code, "bracket", hi=[g.obj(f"{code}_bracket_hi")])
    return parts


def build_estop(code):
    """Emergency-stop post (cs2_liftdress.estop_post: post r 0.04 h 1.00, yellow box, red mushroom):
    a base plate on z 0, 12-sided post, box + overhanging lid, 8 / 12-sided mushroom."""
    S = ESTOP
    parts = []
    X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))
    lm = LowMesh()
    lm.box((0, 0, 0.005), (X, Y, Z), (0.07, 0.07, 0.005), drop=("-Z",))
    g = HiGeo()
    g.box((0, 0, 0.005), (X, Y, Z), (0.07, 0.07, 0.005), bevel=0.002, drop=("-Z",))
    for x in (-0.05, 0.05):
        for y in (-0.05, 0.05):
            g.bolt((x, y, 0.01), Z, r=0.007, h=0.002)
    _unique(parts, lm, "", "galv", code, "plate", w=0.6, hi=[g.obj(f"{code}_plate_hi")])
    lm = LowMesh()
    lm.loft([ring_pts((0, 0, 0.01), Z, circ(S["r"], 12), 12),
             ring_pts((0, 0, 1.00), Z, circ(S["r"], 12), 12)], True)
    g = HiGeo()
    g.tube((0, 0, 0.0), (0, 0, 1.005), S["r"], 24, cap0=False, cap1=False)
    for zc in (0.25, 0.55, 0.85):                   # cable clamp bands, 1.2 mm proud of the flats
        g.band((0, 0, 0), Z, zc - 0.0125, zc + 0.0125, S["r"] - 0.0002, S["r"] + 0.0012, segs=24)
        g.bolt((0.0, -(S["r"] + 0.0012), zc), (0, -1, 0), r=0.005, h=0.0011)
    g.box((0.0, S["r"] + 0.0004, 0.505), (X, Y, Z), (0.002, 0.0008, 0.49))      # seam weld
    _unique(parts, lm, "", "galv", code, "post", w=0.5, hi=[g.obj(f"{code}_post_hi")])
    bx, by, bz = S["box"]
    lm = LowMesh()
    lm.box((0, 0, 1.0 + bz / 2.0), (X, Y, Z), (bx / 2, by / 2, bz / 2), drop=("-Z", "+Z"))
    g = HiGeo()
    g.box((0, 0, 1.0 + bz / 2.0), (X, Y, Z), (bx / 2, by / 2, bz / 2), bevel=0.007, drop=("-Z", "+Z"))
    for x in (-0.062, 0.062):
        for z in (1.018, 1.122):
            g.bolt((x, -by / 2.0, z), (0, -1, 0), r=0.0065, h=0.0016)
    for sx in (-1.0, 1.0):                          # cast stiffening ribs on both side faces
        for yy in (-0.03, 0.03):
            g.box((sx * (bx / 2.0 + 0.0007), yy, 1.0 + bz / 2.0), (X, Y, Z),
                  (0.0009, 0.006, bz / 2.0 - 0.012), bevel=0.0005, segs=1)
    for k in range(3):                              # cable gland bosses on the back
        zc = 1.03 + 0.04 * k
        g.tube((0.0, by / 2.0 - 0.001, zc), (0.0, by / 2.0 + 0.0016, zc), 0.009, 12)
    _unique(parts, lm, "", "safety_yellow", code, "box", hi=[g.obj(f"{code}_box_hi")])
    lx, ly, lz = S["lid"]
    lm = LowMesh()
    lm.box((0, 0, 1.0 + bz + lz / 2.0), (X, Y, Z), (lx / 2, ly / 2, lz / 2), drop=("-Z",))
    g = HiGeo()
    g.box((0, 0, 1.0 + bz + lz / 2.0), (X, Y, Z), (lx / 2, ly / 2, lz / 2), bevel=0.006, drop=("-Z",))
    g.tube((0, 0, 1.0 + bz + lz - 0.001), (0, 0, 1.0 + bz + lz + 0.0016), 0.05, 32)   # button guard
    g.tube((0, 0, 1.0 + bz + lz - 0.001), (0, 0, 1.0 + bz + lz + 0.0024), 0.037, 32)
    _unique(parts, lm, "", "safety_black", code, "lid", hi=[g.obj(f"{code}_lid_hi")])
    z0 = 1.0 + bz + lz
    lm = LowMesh()
    lm.loft([ring_pts((0, 0, z0), Z, circ(0.02, 8), 8), ring_pts((0, 0, z0 + 0.015), Z,
                                                                   circ(0.02, 8), 8)], True)
    g = HiGeo()
    g.tube((0, 0, z0 - 0.001), (0, 0, z0 + 0.016), 0.0195, 16, cap0=False, cap1=False)
    _unique(parts, lm, "", "safety_black", code, "stem", hi=[g.obj(f"{code}_stem_hi")])
    h0 = z0 + 0.015
    lm = LowMesh()
    idx = lm.loft([ring_pts((0, 0, h0), Z, circ(S["btn_r"], 12), 12),
                   ring_pts((0, 0, h0 + 0.015), Z, circ(S["btn_r"], 12), 12)], True)
    lm.planar(idx[1], Z, fan=True)
    g = HiGeo()
    knurl = [ring_pts((0, 0, h0 + t), Z, S["btn_r"] - 0.0005, 48) for t in (0.0, 0.010)]
    for ring in knurl:
        for k in range(0, 48, 2):
            ring[k] = Vector((0, 0, ring[k].z)) + (ring[k] - Vector((0, 0, ring[k].z))) * 0.96
    rings = knurl + [ring_pts((0, 0, h0 + 0.013), Z, 0.033, 48), ring_pts((0, 0, h0 + 0.016), Z, 0.027, 48),
                     ring_pts((0, 0, h0 + 0.0175), Z, 0.016, 48)]
    g.loft(rings, True, cap0=True, cap1=True)
    _unique(parts, lm, "", "fire_red", code, "btn", hi=[g.obj(f"{code}_btn_hi")])
    return parts


def build_tools(code):
    """Ice scraper + snow shovel leaning on a wall: cs2_liftdress.tools(foot, facing -Y, lean 75,
    gap 0.35) as built, plus a T-grip on the scraper and a D-grip on the shovel; foot line on the
    prefab origin (y 0, z 0), the tops touch the wall plane y = TOOLS["wall"] (prefab z -0.32)."""
    import cs2_liftdress as LD
    T_ = TOOLS
    parts = []
    gap = T_["gap"]
    foot = (-gap / 2.0, -T_["wall"])
    built = LD.tools(f"dk{code}t", foot, "-Y", 0.0, lean_deg=T_["lean"], gap=gap)
    bpy.context.view_layer.update()
    ca, sa = math.cos(math.radians(T_["lean"])), math.sin(math.radians(T_["lean"]))
    n, right = Vector((0.0, -1.0, 0.0)), Vector((1.0, 0.0, 0.0))
    axis = (-n * ca + Vector((0, 0, sa))).normalized()
    side = axis.cross(right).normalized()
    L = 1.25
    for k, (o, mat) in enumerate(built):
        tag = "scraper" if k == 0 else "shovel"
        _rename(o, f"{code}_{tag}")
        _bake_world(o)
        _drop_small_caps(o, axis, 0.002)            # handle end caps: inside the blade / grip
        _orient_islands(_smart_uv(o))
        f = Vector((foot[0], foot[1], 0.01)) + right * (k * gap)
        bw, bl = (0.30, 0.10) if k == 0 else (0.25, 0.30)
        g = HiGeo()
        bc = f + axis * (bl / 2.0) + side * 0.006
        g.box(bc, (right, axis, side), (bw / 2.0, bl / 2.0, 0.006), bevel=0.002)
        g.box(bc + side * 0.0062 + axis * 0.0, (right, axis, side), (0.012, bl / 2.0 - 0.01, 0.0011),
              bevel=0.0005, segs=1)
        hc0 = f + side * 0.0175 + axis * bl
        g.tube(hc0 - axis * 0.002, f + side * 0.0175 + axis * L, 0.0172, 12)
        for dx in (-0.04, 0.04):
            g.bolt(f + axis * (bl - 0.03) + right * dx + side * 0.012, side, r=0.005, h=0.0014, segs=8)
        parts.append(Part(o, mat, code, tag, key=None, w=1.0, hi=[g.obj(f"{code}_{tag}_hi")]))
        top = f + side * 0.0175 + axis * L
        lm = LowMesh()
        g = HiGeo()
        if k == 0:
            c = top + axis * 0.015
            lm.box(c, (right, axis, side), (0.06, 0.015, 0.0175))
            g.box(c, (right, axis, side), (0.06, 0.015, 0.0175), bevel=0.004)
            grip = "galv"
        else:
            boxes = ((top + axis * 0.008, (0.05, 0.008, 0.0175), ()),
                     (top + axis * 0.068 + right * 0.042, (0.008, 0.052, 0.0175), ("-Y", "+Y")),
                     (top + axis * 0.068 - right * 0.042, (0.008, 0.052, 0.0175), ("-Y", "+Y")),
                     (top + axis * 0.128, (0.05, 0.008, 0.0175), ()))
            for c, half, drop in boxes:
                lm.box(c, (right, axis, side), half, drop=drop)
                g.box(c, (right, axis, side), half, bevel=0.003, drop=drop)
            grip = "darksteel"
        _unique(parts, lm, "", grip, code, f"{tag}grip", hi=[g.obj(f"{code}_{tag}grip_hi")])
    shift = Vector((0.0, T_["wall"], 0.0))
    bpy.context.view_layer.update()
    zmin = min(float(_world_co(p.obj)[:, 2].min()) for p in parts)
    for p in parts:
        _move(p.obj, shift - Vector((0, 0, zmin)))
        for h in p.hi:
            _move(h, shift - Vector((0, 0, zmin)))
    return parts


def _drop_small_caps(obj, axis, max_area):
    """Delete faces facing +-axis smaller than max_area (the builder's hidden handle end caps)."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.normal_update()
    ax = Vector(axis).normalized()
    dead = [f for f in bm.faces if abs(f.normal.dot(ax)) > 0.99 and f.calc_area() < max_area]
    bmesh.ops.delete(bm, geom=dead, context="FACES")
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    return len(dead)


def _lean_frame(lean_deg):
    """(side, up, out) of a part leaning back toward +y (the wall behind, prefab -Z)."""
    lam = math.radians(lean_deg)
    side = Vector((1.0, 0.0, 0.0))
    up = Vector((0.0, math.cos(lam), math.sin(lam)))
    out = side.cross(up)
    return side, up, out


def akja_profile(u_scale, w_scale, s, n=6, inset=0.0, bump=False, m=None):
    """Half-ellipse cross-section points (u, w) at station s, theta 0 (u +) -> pi (u -),
    with the nose curl: w -= 0.20 ((s - 1.5) / 0.5)^2 for s > 1.5."""
    K_ = AKJA
    a_ = K_["width"] / 2.0 * u_scale - inset
    b_ = K_["depth"] * w_scale - inset
    curl = 0.20 * ((s - 1.5) / 0.5) ** 2 if s > 1.5 else 0.0
    pts = []
    cnt = (m or n)
    for k in range(cnt + 1):
        th = math.pi * k / cnt
        u, w = a_ * math.cos(th), b_ * math.sin(th)
        if bump and 0.1 < s < 1.48:
            rise = 0.0
            arc = math.radians(1.0) * 0.26                 # ~ meters per degree on the hull
            for c in (45.0, 60.0, 75.0, 90.0, 105.0, 120.0, 135.0):   # molded ribs, 2 cm tops
                dm = abs(math.degrees(th) - c) * arc        # meters from the rib center
                if dm < 0.016:
                    rise = max(rise, 0.0024 * min(1.0, (0.016 - dm) / 0.006))
            for sc in (0.25, 0.55, 0.85, 1.15, 1.45):      # transverse strakes, 3 cm, steep flanks
                ds = abs(s - sc)
                if ds < 0.021 and 35.0 < math.degrees(th) < 145.0:
                    rise = max(rise, 0.0018 * min(1.0, (0.021 - ds) / 0.006))
            u += rise * math.cos(th)
            w += rise * math.sin(th)
        pts.append((u, w - curl))
    return pts


def build_akja(code):
    """Rescue toboggan (cs2_bldg.akja: 2.0 x 0.6, slot 2) leaning back at 75 deg, open side to the
    wall, foot (tail rim) on the origin: outer + inner shell (6 x 4 quads each), rims, 4 side
    handles, 2 runners. Low outer = circumscribed polygon of the hi ellipse."""
    K_ = AKJA
    parts = []
    side, up, out = _lean_frame(K_["lean"])
    P = lambda u, s, w: side * u + up * s + out * w   # noqa: E731
    # the curl is quadratic: low stations every 0.1 m along it keep the chord error <= 2 mm
    # (0.2 x 0.2^2 / 4), inside the 3 mm cage both ways (outer bottom and rim face opposite ways)
    S = (0.0, 1.5, 1.6, 1.7, 1.8, 1.9, 2.0)
    f = 1.0 / math.cos(math.pi / 12.0)
    t = K_["shell"]
    outer = [[P(u, s, w) for u, w in reversed(akja_profile(f, f, s))] for s in S]
    inner = [[P(u, s, w) for u, w in akja_profile(1.0, 1.0, s, inset=t)] for s in S]
    lm = LowMesh()
    lm.loft(outer, closed=False)
    _unique(parts, lm, "", "accent", code, "outer", hi=[])
    lm = LowMesh()
    lm.loft(inner, closed=False)
    _unique(parts, lm, "", "accent", code, "inner", w=0.3, hi=[])
    lm = LowMesh()
    a_o = [r[-1] for r in outer]          # outer theta 0 (u +): last of the reversed ring
    a_i = [r[0] for r in inner]
    b_o = [r[0] for r in outer]           # u - side
    b_i = [r[-1] for r in inner]
    lm.loft([a_i, a_o], closed=False)     # rim strips along s, facing the opening (-w)
    lm.loft([b_o, b_i], closed=False)
    # tail and nose end rims (quads between the outer and inner sections)
    for k, nrm in ((0, -up), (len(S) - 1, up)):
        ro, ri = outer[k], list(reversed(inner[k]))
        for i in range(len(ro) - 1):
            lm.planar([lm.v(ro[i]), lm.v(ro[i + 1]), lm.v(ri[i + 1]), lm.v(ri[i])], nrm)
    _unique(parts, lm, "", "accent", code, "rim", hi=[])
    # hi shell (every surface built in its outward winding; open surfaces, no recalc):
    # dense outer with three bottom ribs, inner, a rounded rim bead that covers the low rim out
    # to the circumscribed outer edge, end lips from the circumscribed outer section
    g = HiGeo()
    # hi stations every 5 mm through rib / strake flanks would be heavy: 2.5 mm along the
    # strakes, 2.5 cm elsewhere; the hull and the inner run 1 cm past both ends (edge rays)
    Sh = sorted({round(i * 0.025, 4) for i in range(81)} | {-0.004, 2.004}
                | {round(sc + k * 0.0025, 4) for sc in (0.25, 0.55, 0.85, 1.15, 1.45)
                   for k in range(-10, 11)})
    ho = [[P(u, s, w) for u, w in reversed(akja_profile(1.0, 1.0, s, m=144, bump=True))] for s in Sh]
    hi_in = [[P(u, s, w) for u, w in akja_profile(1.0, 1.0, s, inset=t, m=48)] for s in Sh]
    # the curl formula holds below 0 / above 2 too (clamped below 1.5): extended ends
    g.loft(ho, closed=False)
    g.loft(hi_in, closed=False)
    lip = f * 1.004
    for side_sign in (1.0, -1.0):
        rings = []
        for s in Sh:
            prof_o = akja_profile(lip, 1.0, s, m=48)
            prof_i = akja_profile(1.0, 1.0, s, inset=t, m=48)
            uo, wo = prof_o[0] if side_sign > 0 else prof_o[-1]
            ui, wi = prof_i[0] if side_sign > 0 else prof_i[-1]
            uh = (AKJA["width"] / 2.0) * side_sign           # the hull's outer edge (scale 1)
            um = 0.5 * (uo + ui)
            # lip: out from the hull edge under the rim (faces +w), its outer edge (faces out),
            # over the top (faces the opening), down into the inner shell
            rings.append([P(uh, s, wo + 0.003), P(uo, s, wo + 0.0015), P(uo, s, wo - 0.0008),
                          P(uo - side_sign * 0.003, s, wo - 0.0016), P(um, s, wo - 0.002),
                          P(ui + side_sign * 0.001, s, wi - 0.0012), P(ui, s, wi)])
        if side_sign < 0:
            rings = [list(reversed(r)) for r in rings]
        g.loft(rings, closed=False)
    for s in (0.0, AKJA["length"]):
        po = [P(u, s, w) for u, w in reversed(akja_profile(lip, lip, s, m=48))]
        pi = [P(u, s, w) for u, w in reversed(akja_profile(1.0, 1.0, s, inset=t, m=48))]
        g.loft([pi, po] if s == 0.0 else [po, pi], closed=False)
    hi_shell = g.obj(f"{code}_shell_hi")
    parts[0].hi.append(hi_shell)
    # handles (darksteel) and runners (rubber)
    lm = LowMesh()
    g = HiGeo()
    for su in (1.0, -1.0):
        for sc in (0.40, 1.20):
            c = P(su * (K_["width"] / 2.0 * f + 0.022), sc, 0.03)
            lm.box(c, (side, up, out), (0.012, 0.10, 0.012), drop=("-X",) if su > 0 else ("+X",))
            g.tube(c - up * 0.10, c + up * 0.10, 0.0115, 12)
    _unique(parts, lm, "", "darksteel", code, "handles", hi=[g.obj(f"{code}_handles_hi")])
    lm = LowMesh()
    g = HiGeo()
    for su in (1.0, -1.0):
        u = su * 0.12
        prof = akja_profile(f, f, 0.8)
        # w of the circumscribed outer polygon at u (linear between the vertices around it)
        best = None
        for (u0, w0), (u1, w1) in zip(prof, prof[1:]):
            if min(u0, u1) - 1e-9 <= u <= max(u0, u1) + 1e-9 and w0 > 0.05:
                best = w0 + (w1 - w0) * (u - u0) / (u1 - u0)
        c = P(u, 0.85, best + 0.008)
        lm.box(c, (side, up, out), (0.012, 0.70, 0.008), drop=("-Z",))
        g.box(c, (side, up, out), (0.012, 0.70, 0.008), bevel=0.003, drop=("-Z",))
    _unique(parts, lm, "", "rubber", code, "runners", hi=[g.obj(f"{code}_runners_hi")])
    return parts


def build_spine_board(code):
    """Rescue spine board (cs2_bldg.backboard: 1.85 x 0.45 x 0.05, safety yellow) leaning back at
    85 deg, foot line on the origin: chamfered-octagon slab (one side band island); straps and the
    handhold slots are paint + normal (hi: 1.5 mm straps, slot rims)."""
    B = SPINE
    parts = []
    side, up, out = _lean_frame(B["lean"])
    P = lambda u, s, w: side * u + up * s + out * w   # noqa: E731
    hw, Ln, c = B["width"] / 2.0, B["length"], B["chamfer"]
    oct_ = ((-hw + c, 0.0), (hw - c, 0.0), (hw, c), (hw, Ln - c), (hw - c, Ln), (-hw + c, Ln),
            (-hw, Ln - c), (-hw, c))
    lm = LowMesh()
    back = [P(u, s, 0.0) for u, s in oct_]
    frontp = [P(u, s, B["t"]) for u, s in oct_]
    for i in range(len(oct_)):                    # one island per edge (a 4.6 m band caps the pack)
        j = (i + 1) % len(oct_)
        e = Vector((oct_[j][0] - oct_[i][0], oct_[j][1] - oct_[i][1]))
        nrm = (side * e.y - up * e.x).normalized()
        lm.quad([back[i], back[j], frontp[j], frontp[i]], nrm)
    _unique(parts, lm, "", "safety_yellow", code, "side", hi=[])
    lm = LowMesh()
    lm.planar([lm.v(p) for p in frontp], out, fan=True)
    _unique(parts, lm, "", "safety_yellow", code, "front", hi=[])
    lm = LowMesh()
    lm.planar([lm.v(p) for p in back], -out, fan=True)
    _unique(parts, lm, "", "safety_yellow", code, "back", w=0.35, hi=[])
    g = HiGeo()

    def inset_ring(d_, w_):
        cc = c + d_ * (math.sqrt(2.0) - 1.0)
        o2 = ((-hw + cc, d_), (hw - cc, d_), (hw - d_, cc), (hw - d_, Ln - cc), (hw - cc, Ln - d_),
              (-hw + cc, Ln - d_), (-hw + d_, Ln - cc), (-hw + d_, cc))
        return [P(u, s, w_) for u, s in o2]
    g.loft([inset_ring(0.006, 0.0), inset_ring(0.0, 0.006), inset_ring(0.0, B["t"] - 0.006),
            inset_ring(0.006, B["t"])], True, cap0=True, cap1=True)
    for s0 in STRAPS:
        g.box(P(0.0, s0, B["t"] / 2.0), (side, up, out), (hw + 0.0015, 0.025, B["t"] / 2.0 + 0.0015),
              bevel=0.0008, segs=1)
        g.box(P(0.12, s0, B["t"] / 2.0), (side, up, out), (0.022, 0.03, B["t"] / 2.0 + 0.0024),
              bevel=0.001, segs=1)
    # molded front: a raised border lip just inside the edge and two long stiffening ribs
    wl = B["t"] + 0.0006
    ring = [(u, s) for u, s in oct_]
    for i in range(len(ring)):
        a_, b_ = Vector(ring[i]), Vector(ring[(i + 1) % len(ring)])
        ctr = (a_ + b_) * 0.5
        dvec = (b_ - a_)
        ln = dvec.length
        dvec.normalize()
        inward = Vector((-dvec.y, dvec.x))
        cc = ctr + inward * 0.022
        ax_e = (side * dvec.x + up * dvec.y).normalized()
        ax_n = (side * inward.x + up * inward.y).normalized()
        g.box(P(cc.x, cc.y, wl), (ax_e, ax_n, out), (max(0.01, ln / 2.0 - 0.03), 0.006, 0.0009),
              bevel=0.0005, segs=1)
    for s0 in (1.58, 1.63, 1.68, 1.73):             # head-block pads across the top
        g.box(P(0.0, s0, wl), (side, up, out), (0.10, 0.012, 0.0009), bevel=0.0005, segs=1)
    for s0 in (0.06, 0.10):                         # foot-end grip ridges
        g.box(P(0.0, s0, wl), (side, up, out), (0.12, 0.01, 0.0009), bevel=0.0005, segs=1)
    for u0 in (-0.15, -0.10, -0.055, -0.018, 0.018, 0.055, 0.10, 0.15):
        for s_a, s_b in ((0.24, 0.37), (0.45, 0.92), (1.0, 1.47)):
            g.box(P(u0, 0.5 * (s_a + s_b), wl), (side, up, out), (0.006, 0.5 * (s_b - s_a), 0.0009),
                  bevel=0.0005, segs=1)
    for u0 in (-0.13, 0.13):                        # molded runners on the back
        g.box(P(u0, Ln / 2.0, -0.0008), (side, up, out), (0.02, Ln / 2.0 - 0.12, 0.0009),
              bevel=0.0005, segs=1)
    for su in (1.0, -1.0):
        for s0 in HANDHOLDS:
            uc = su * (hw - 0.045)
            for face_w in (B["t"], 0.0):
                nrm = 1.0 if face_w > 0 else -1.0
                wc = face_w + nrm * 0.0008
                for du, ds, hu, hs in ((0.0, -0.053, 0.021, 0.003), (0.0, 0.053, 0.021, 0.003),
                                       (-0.018, 0.0, 0.003, 0.05), (0.018, 0.0, 0.003, 0.05)):
                    g.box(P(uc + du, s0 + ds, wc), (side, up, out), (hu, hs, 0.0009), bevel=0.0005,
                          segs=1)
    hi = g.obj(f"{code}_slab_hi")
    parts[0].hi.append(hi)
    return parts


HANDHOLDS = (0.16, 0.60, 0.82, 1.22, 1.70)  # spine-board slot centers along the board (m)
STRAPS = (0.40, 0.95, 1.50)                 # strap centers (m), 0.05 wide

BUILDERS = {"R": build_aframe, "W": build_wall_rack, "HR": build_hose_reel,
            "EX": build_extinguisher, "ES": build_estop, "TL": build_tools,
            "AK": build_akja, "SB": build_spine_board}


def build_prop(spec, lib, gear):
    code = spec["code"]
    if code[0] == "R":
        return build_aframe(code, lib, gear)
    if code[0] == "W":
        return build_wall_rack(code, lib, gear)
    return BUILDERS[code](code)


# ======================================================================================
# Parity with the builders (read-only calls; recorded in the manifest, asserted by the test)
# ======================================================================================

def _sorted_rows(co, nd=6):
    co = np.round(np.asarray(co, dtype=np.float64), nd)
    return co[np.lexsort((co[:, 2], co[:, 1], co[:, 0]))]


def parity_aframe(code):
    """Every gear item of an A-frame loadout: cs2_bldg.ski_rack_frame(skis=[that item],
    gear=True) vs cs2_bldg.ski_gear at aframe_pose (what the kit places). The builder mirrors
    side -1 (a = A); ski_gear rotates it (a = -A); the items are symmetric across a, so the
    VERTEX SETS must agree. Returns {"items", "max_dev_m"}."""
    import cs2_bldg as K
    worst, n = 0.0, 0
    for sg in (+1, -1):
        for j, gk in enumerate(AFRAME_LOADOUTS[code][sg]):
            if gk is None:
                continue
            kind, colour, L = GEAR[gk]
            ref = K.ski_rack_frame(f"pq{code}{sg > 0}{j}", 0.0, 0.0, slots=AF["slots"], along="X",
                                   skis=[(j, sg, kind, colour, L)], gear=True)
            F, T = aframe_pose(j, sg)
            mine = K.ski_gear(f"pk{code}{sg > 0}{j}", [(kind, tuple(F), tuple(T), colour, L)])
            a = _sorted_rows(np.concatenate([_world_co(o) for o, _m in ref[1:]]))
            b = _sorted_rows(np.concatenate([_world_co(o) for o, _m in mine]))
            dev = float(np.abs(a - b).max()) if a.shape == b.shape else float("inf")
            worst = max(worst, dev)
            n += 1
            for o, _m in ref + mine:
                _remove(o)
    return {"items": n, "max_dev_m": worst}


def parity_wall():
    """cs2_liftdress.ski_rack(pairs 7, boards 2) vs ski_gear at wall_pose for the same kinds:
    per slot the foot (lowest vertex z and the x center of the lowest 5 mm) must agree."""
    import cs2_bldg as K
    import cs2_liftdress as LD
    kinds = ["ski"] * 7 + ["board"] * 2
    ref = LD.ski_rack("pqwall", WR["x0"], WR["x1"], WR["y"], WR["z0"], pairs=7, boards=2,
                      side=WR["side"], h=WR["h"], stand_off=WR["stand_off"])
    bpy.context.view_layer.update()
    gear_co = np.concatenate([_world_co(o) for o, _m in ref if o.name.startswith("ski_")])
    worst = 0.0
    for i, kind in enumerate(kinds):
        F, T = wall_pose(i, len(kinds))
        L = 1.70 if kind == "ski" else 1.52
        colour = ("trim_white", "ski_blue") if kind == "ski" else ("ski_black", "accent")
        mine = K.ski_gear(f"pkw{i}", [(kind, tuple(F), tuple(T), colour, L)])
        b = np.concatenate([_world_co(o) for o, _m in mine])
        sel = gear_co[np.abs(gear_co[:, 0] - F.x) < 0.11]
        foot_a = sel[sel[:, 2] <= sel[:, 2].min() + 0.005]
        foot_b = b[b[:, 2] <= b[:, 2].min() + 0.005]
        dev = max(abs(float(sel[:, 2].min() - b[:, 2].min())),
                  abs(float(foot_a[:, 0].mean() - foot_b[:, 0].mean())),
                  abs(float(foot_a[:, 1].mean() - foot_b[:, 1].mean())))
        worst = max(worst, dev)
        for o, _m in mine:
            _remove(o)
    for o, _m in ref:
        _remove(o)
    return {"items": len(kinds), "max_dev_m": worst}


def parity_bounds():
    """Builder footprints (their own calls, in the kit's pivot frame) vs the kit low bounds:
    reported, not asserted (the kit adds hose / nozzle / grips / rims)."""
    import cs2_bldg as K
    import cs2_liftdress as LD
    out = {}
    calls = {
        "HR": lambda: LD.hose_reel("pqh", (0.0, 0.0), "-Y", HOSE["cz"]),
        "EX": lambda: LD.extinguisher("pqe", (0.0, 0.0), "-Y", 0.0),
        "ES": lambda: LD.estop_post("pqs", (0.0, 0.0), 0.0),
        "AK": lambda: K.akja("pqa", (0.0, 0.0, K.GROUND_LIFT), "-Y", lean_deg=AKJA["lean"],
                             length=AKJA["length"], width=AKJA["width"]),
        "SB": lambda: K.backboard("pqb", (0.0, 0.0, K.GROUND_LIFT), "-Y", lean_deg=SPINE["lean"],
                                  length=SPINE["length"], width=SPINE["width"], t=SPINE["t"]),
    }
    for code, fn in calls.items():
        parts = fn()
        bpy.context.view_layer.update()
        co = np.concatenate([_world_co(o) for o, _m in parts])
        if code in ("AK", "SB"):
            co[:, 2] -= K.GROUND_LIFT
        out[code] = {"builder_blender_min": [round(float(v), 3) for v in co.min(0)],
                     "builder_blender_max": [round(float(v), 3) for v in co.max(0)]}
        for o, _m in parts:
            _remove(o)
    return out


# ======================================================================================
# Assembly + one joint bake
# ======================================================================================

def _join(objs, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    o.name = name
    o.data.name = name
    bpy.context.view_layer.update()
    return _bake_world(o)


def assemble(props=PROPS):
    """Build every prop's low parts + hi twins, lay them out on one line. Returns
    (groups [(spec, parts)], offsets {code: (x, 0, 0)}, lib, gear, stats)."""
    lib = Library()
    gear = GearBuilder(lib)
    groups = []
    for spec in props:
        parts = build_prop(spec, lib, gear)
        groups.append((spec, parts))
    bpy.context.view_layer.update()
    cursor, offsets, stats = 0.0, {}, {}
    for spec, parts in groups:
        co = np.concatenate([_world_co(p.obj) for p in parts])
        lo, hi = co.min(axis=0), co.max(axis=0)
        off = Vector((cursor - float(lo[0]), 0.0, 0.0))
        offsets[spec["code"]] = (float(off.x), 0.0, 0.0)
        low_t = sum(cs2_asset.tri_count(p.obj) for p in parts)
        hi_t = sum(cs2_asset.tri_count(h) for p in parts for h in p.hi)
        stats[spec["code"]] = {"tris_low": low_t, "tris_hi": hi_t,
                               "parts": len(parts), "shared_parts": sum(1 for p in parts if p.key)}
        for p in parts:
            _move(p.obj, off)
            for h in p.hi:
                _move(h, off)
        cursor += float(hi[0] - lo[0]) + LAYOUT_GAP
    bpy.context.view_layer.update()
    return groups, offsets, lib, gear, stats


def _set_density(obj, w):
    me = obj.data
    a = me.attributes.get(cs2_uv.DENSITY_ATTR) or me.attributes.new(cs2_uv.DENSITY_ATTR, "FLOAT", "FACE")
    a.data.foreach_set("value", [float(w)] * len(me.polygons))


def _texel_points(q, P, X, Y):
    """Per-texel 3D points of a face raster (least-squares affine UV -> 3D, exact for planar
    affine faces)."""
    qm, Pm = q.mean(axis=0), P.mean(axis=0)
    Mt, *_ = np.linalg.lstsq(q - qm, P - Pm, rcond=None)
    return Pm + np.stack([X - qm[0], Y - qm[1]], axis=-1) @ Mt


def _rule_ext_label(p):
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    front = y < -(EXT["off"] + EXT["r"])
    lab = front & (z >= 0.15) & (z <= 0.30)
    red = lab & (z >= 0.283)
    ink = lab & (np.abs(x) < 0.045) & (((z >= 0.255) & (z <= 0.266)) | ((z >= 0.228) & (z <= 0.236))
                                         | ((z >= 0.20) & (z <= 0.206)))
    return [(lab, LABEL_SRGB), (red, SIGN_RED_SRGB), (ink, INK_SRGB)]


def _rule_estop_label(p):
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    front = y < -ESTOP["box"][1] / 2.0 + 0.001
    lab = front & (np.abs(x) <= 0.055) & (z >= 1.035) & (z <= 1.105)
    ink = lab & (((z >= 1.078) & (z <= 1.091) & (np.abs(x) < 0.045))
                 | ((z >= 1.052) & (z <= 1.062) & (np.abs(x) < 0.030)))
    return [(lab, LABEL_SRGB), (ink, INK_SRGB)]


def _rule_hose_sign(p):
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    front = y < -HOSE["plate_t"] + 0.001
    lab = front & (x >= -0.335) & (x <= -0.205) & (z >= 0.585) & (z <= 0.675)
    ink = lab & (x >= -0.325) & (x <= -0.215) & (((z >= 0.645) & (z <= 0.657))
                                                  | ((z >= 0.618) & (z <= 0.627))
                                                  | ((z >= 0.598) & (z <= 0.604)))
    return [(lab, LABEL_SRGB), (ink, SIGN_RED_SRGB)]


def _rule_spine(p):
    side, up, out = _lean_frame(SPINE["lean"])
    u = p[..., 0]
    s = p[..., 1] * up.y + p[..., 2] * up.z
    w = p[..., 1] * out.y + p[..., 2] * out.z
    strap = np.zeros(u.shape, dtype=bool)
    for s0 in STRAPS:
        strap |= np.abs(s - s0) <= 0.025
    slot = np.zeros(u.shape, dtype=bool)
    hw = SPINE["width"] / 2.0
    for s0 in HANDHOLDS:
        for su in (1.0, -1.0):
            slot |= (np.abs(u - su * (hw - 0.045)) <= 0.015) & (np.abs(s - s0) <= 0.05)
    face = (w > SPINE["t"] - 0.001) | (w < 0.001)
    return [(strap, STRAP_SRGB), (slot & face & ~strap, (0.16, 0.15, 0.13))]


PAINT_RULES = {"EX_body": _rule_ext_label, "ES_box": _rule_estop_label,
               "HR_plate": _rule_hose_sign, "SB_front": _rule_spine, "SB_back": _rule_spine,
               "SB_side": _rule_spine}


def paint_details(base, mesh, size, offsets, table):
    """World-position painters on the LINEAR base before AO (labels, the spine board's straps and
    handhold slots). Returns {part: painted texels}."""
    part = cs2_compose.part_indices(mesh)
    painted = {}
    for poly, sl, mask, X, Y, q, P, _ppm in cs2_compose.iter_face_rasters(mesh, size, "UVUnique"):
        name = table[part[poly.index] - 1]
        rule = PAINT_RULES.get(name)
        if rule is None:
            continue
        code = name.split("_", 1)[0]
        pts = _texel_points(q, np.asarray(P, np.float64), X, Y) - np.asarray(offsets[code])
        for sel, srgb in rule(pts):
            m = mask & sel
            if not m.any():
                continue
            lin = np.array([_lin(c) for c in srgb], dtype=np.float32)
            base[sl][m, 0:3] = lin[None, :]
            painted[name] = painted.get(name, 0) + int(m.sum())
    return painted


# --------------------------------------------------------------------------------------
# Own UVUnique layout: skyline packer, 90 deg rotation, the densest scale that fits
# --------------------------------------------------------------------------------------

def skyline_pack(rects, W, H):
    """rects = [(w, h, key)] in texels (margins included). Skyline bottom-left with 90 deg
    rotation, largest side first (ties by key): {key: (x, y, rotated)} or None when one does
    not fit. Deterministic."""
    order = sorted(rects, key=lambda r: (-max(r[0], r[1]), -min(r[0], r[1]), r[2]))
    sky = [[0, 0, W]]                          # [x, y, width], x ascending, contiguous
    out = {}
    for w, h, key in order:
        best = None
        for rot in (False, True):
            rw, rh = (h, w) if rot else (w, h)
            if rw > W or rh > H:
                continue
            for i in range(len(sky)):
                x = sky[i][0]
                if x + rw > W:
                    break
                y, left, j = 0, rw, i
                while left > 0:
                    y = max(y, sky[j][1])
                    left -= sky[j][2]
                    j += 1
                if y + rh > H:
                    continue
                waste, left, j = 0, rw, i
                while left > 0:
                    seg = min(left, sky[j][2])
                    waste += seg * (y - sky[j][1])
                    left -= seg
                    j += 1
                score = (y + rh, waste, x, rot)
                if best is None or score < best[0]:
                    best = (score, x, y, rw, rh, rot)
        if best is None:
            return None
        _s, x, y, rw, rh, rot = best
        out[key] = (x, y, rot)
        new = [x, y + rh, rw]
        nxt = []
        for seg in sky:
            sx, sy, sw = seg
            if sx + sw <= x or sx >= x + rw:
                nxt.append(seg)
                continue
            if sx < x:
                nxt.append([sx, sy, x - sx])
            if sx + sw > x + rw:
                nxt.append([x + rw, sy, sx + sw - (x + rw)])
        nxt.append(new)
        nxt.sort(key=lambda s: s[0])
        merged = []
        for seg in nxt:
            if merged and merged[-1][1] == seg[1] and merged[-1][0] + merged[-1][2] == seg[0]:
                merged[-1][2] += seg[2]
            else:
                merged.append(seg)
        sky = merged
    return out


def _part_polys(me, n_parts):
    part = np.zeros(len(me.polygons), dtype=np.int32)
    me.attributes["part"].data.foreach_get("value", part)
    order = np.argsort(part, kind="stable")
    bounds = np.searchsorted(part[order], np.arange(1, n_parts + 2))
    return {i: np.sort(order[bounds[i - 1]:bounds[i]]) for i in range(1, n_parts + 1)}


def layout_unique(bake, table, allparts, size=A, margin=MARGIN_PX):
    """Build "UVUnique" from "UVMap": every island of every canonical / unique part, in meters
    (island scale = sqrt(3D area / UV area)), at D x w texels per meter (w = the part's density
    weight), skyline-packed with `margin` texels between islands; D = the largest density that
    fits (binary search). Rigid copies take their canonical part's loops (share keys). The
    rotation is free here: no source normal map is re-projected (bake_high_to_low bakes the
    tangent normal straight into this layer). Returns the report."""
    me = bake.data
    src = me.uv_layers["UVMap"]
    uv_src = cs2_uv.loop_uvs(src)
    start, total = cs2_uv.face_loop_table(me)
    wa = cs2_uv.face_world_areas(me)
    fua = cs2_uv.face_uv_areas(me, uv_src, start, total)
    polys = _part_polys(me, len(table))
    canon_of, groups = {}, {}
    for i, p in enumerate(allparts, start=1):
        if p.key is not None:
            groups.setdefault(p.key, []).append(i)
    for key, idxs in groups.items():
        for i in idxs[1:]:
            canon_of[i] = idxs[0]
    islands = []                                   # (key, loops, pts_m (n,2), w)
    for i, p in enumerate(allparts, start=1):
        if i in canon_of:
            continue
        for isl in cs2_uv.uv_islands(me, src, faces=[int(f) for f in polys[i]]):
            loops = np.concatenate([np.arange(start[f], start[f] + total[f]) for f in isl])
            pts = uv_src[loops]
            ua = fua[isl].sum()
            s = math.sqrt(float(wa[isl].sum()) / max(float(ua), 1e-15))
            pm = (pts - pts.min(axis=0)) * s
            islands.append((len(islands), loops, pm, float(p.w)))
    half = margin / 2.0

    def rects_at(D):
        out = []
        for k, _loops, pm, w in islands:
            ext = pm.max(axis=0) * D * w
            out.append((int(math.ceil(ext[0] + margin)), int(math.ceil(ext[1] + margin)), k))
        return out
    lo_d, hi_d, best = 50.0, 900.0, None
    for _it in range(18):
        mid = 0.5 * (lo_d + hi_d)
        got = skyline_pack(rects_at(mid), size, size)
        if got is None:
            hi_d = mid
        else:
            lo_d, best = mid, got
    if best is None:
        raise RuntimeError("layout_unique: nothing fits at 50 px/m")
    D = lo_d
    uv = np.zeros((len(me.loops), 2), dtype=np.float64)
    for k, loops, pm, w in islands:
        x, y, rot = best[k]
        q = pm * D * w
        if rot:
            h = float(q[:, 1].max())
            q = np.stack([h - q[:, 1], q[:, 0]], axis=1)
        uv[loops] = (q + np.array([x + half, y + half])) / float(size)
    n_dup = 0
    for i, c in canon_of.items():
        ci, di = polys[c], polys[i]
        if len(ci) != len(di):
            raise RuntimeError(f"layout: {table[i - 1]} vs {table[c - 1]} face count")
        for fc, fd in zip(ci, di):
            if total[fc] != total[fd]:
                raise RuntimeError(f"layout: loop count {table[i - 1]}")
            uv[start[fd]:start[fd] + total[fd]] = uv[start[fc]:start[fc] + total[fc]]
        n_dup += 1
    if UNIQUE_NAME in me.uv_layers:
        me.uv_layers.remove(me.uv_layers[UNIQUE_NAME])
    lay = me.uv_layers.new(name=UNIQUE_NAME, do_init=False)
    cs2_uv.set_loop_uvs(lay, uv)
    me.uv_layers.active = lay
    lay.active_render = True
    if uv.min() < -1e-9 or uv.max() > 1.0 + 1e-9:
        raise RuntimeError(f"layout: UVs outside [0, 1] ({uv.min()}, {uv.max()})")
    used = sum((r[0]) * (r[1]) for r in rects_at(D))
    # DK6 is a property of the atlas: area-weighted px/m over the faces that OWN texels (every
    # canonical / unique face once; rigid copies would count the racks' gear 60 times)
    own = np.ones(len(me.polygons), dtype=bool)
    for i in canon_of:
        own[polys[i]] = False
    dens = cs2_uv.face_px_per_m(me, lay, size)
    wts = cs2_uv.face_density_weights(me)
    ok = own & np.isfinite(dens) & (wa > 0)
    full = ok & (wts >= 0.999)
    uniq = {"median": round(_wmedian(dens[ok], wa[ok]), 1),
            "p10": round(_wmedian(dens[ok], wa[ok], 0.1), 1),
            "min": round(float(dens[ok].min()), 1),
            "median_w1": round(_wmedian(dens[full], wa[full]), 1),
            "area_m2": round(float(wa[ok].sum()), 3),
            "downweighted_area_share": round(float(wa[ok & ~full].sum() / wa[ok].sum()), 3)}
    return {"base_px_per_m": round(D, 2), "islands": len(islands), "dup_parts": n_dup,
            "margin_px": margin, "rect_fill": round(used / float(size * size), 4),
            "unique_px_per_m": uniq}


UNIQUE_NAME = cs2_uv.UNIQUE_LAYER


def compose_kit(bake, table, matkey_of, size, n_path, a_path, owner_dir, offsets, margin):
    """The four maps from the kit's constant materials (linear albedo, metal, rough, slot) on
    "UVUnique", the painters, the hi normal / AO of bake_high_to_low, cs2_compose.compose_maps
    (per-part +-3 % value noise, AO with the 0.08 floor, MaskMap B 0, ControlMask A 0), a
    `margin`-texel gutter dilation, sRGB encode; writes <OWNER>_<map>.png. Returns the report."""
    mats = compose_materials()
    me = bake.data
    pmap, ppm = cs2_compose.rasterise_face_fields(me, size, UNIQUE_NAME)
    cov = pmap > 0
    base = np.zeros((size, size, 4), np.float32)
    msk = np.zeros((size, size, 4), np.float32)
    ctl = np.zeros((size, size, 4), np.float32)
    base[..., 3] = 1.0
    keys_used = {}
    for p in sorted(int(v) for v in np.unique(pmap) if v > 0):
        key = matkey_of[table[p - 1]]
        m = mats[key]
        sel = pmap == p
        base[sel, 0:3] = m["albedo"]
        msk[sel, 0] = m["metal"]
        msk[sel, 3] = 1.0 - m["rough"]
        if m["slot"]:
            ctl[sel, m["slot"] - 1] = 1.0
        keys_used[key] = keys_used.get(key, 0) + int(sel.sum())
    galv = np.zeros((size, size), bool)
    for p in sorted(int(v) for v in np.unique(pmap) if v > 0):
        if matkey_of[table[p - 1]] == "galv":
            galv |= pmap == p
    rng = np.random.default_rng(20261012)
    spangle = cs2_compose.value_noise(size, 96, rng) * 0.05 + cs2_compose.value_noise(size, 24, rng) * 0.04
    base[galv, 0:3] *= (1.0 + spangle[galv])[:, None]
    painted = paint_details(base, me, size, offsets, table)
    nrm_px = cs2_validate.load_png_pixels(n_path)
    ao = cs2_validate.load_png_pixels(a_path)[..., 0] if a_path else None
    nrm = np.array(nrm_px, dtype=np.float32)
    base, nrm, msk, ctl = cs2_compose.compose_maps(
        base, nrm, msk, ctl, ao, None, 0.0,
        {"reserved": [], "part_map": pmap, "noise_amp": 0.03, "seed": 0, "gloss_var": 0.02})
    # gutter: every map extends its covered texels `margin` texels out (deterministic 4-neighbor
    # propagation, left / right / down / up priority); the rest is neutral
    filled = cov.copy()
    for _k in range(int(margin)):
        grow = np.zeros_like(filled)
        src_idx = np.zeros(filled.shape + (2,), np.int32)
        for dy, dx in ((0, -1), (0, 1), (-1, 0), (1, 0)):
            nb = np.roll(filled, (dy, dx), axis=(0, 1))
            if dy == -1:
                nb[-1, :] = False
            if dy == 1:
                nb[0, :] = False
            if dx == -1:
                nb[:, -1] = False
            if dx == 1:
                nb[:, 0] = False
            take = nb & ~filled & ~grow
            for arr in (base, msk, ctl):
                arr[take] = np.roll(arr, (dy, dx), axis=(0, 1))[take]
            grow |= take
        filled |= grow
    flat = ~filled
    base[flat, 0:3] = 0.18
    msk[flat] = 0.0
    ctl[flat] = 0.0
    nrm[~filled, 0:3] = (0.5, 0.5, 1.0)
    base = np.clip(base, 0.0, 1.0)
    nrm = np.clip(nrm, 0.0, 1.0)
    msk = np.clip(msk, 0.0, 1.0)
    ctl = np.clip(ctl, 0.0, 1.0)
    base[..., 3] = 1.0
    nrm[..., 3] = 1.0
    msk[..., 1] = 0.0
    msk[..., 2] = 0.0
    ctl[..., 3] = 0.0
    import cs2_preview
    base[..., 0:3] = cs2_preview._linear_to_srgb(base[..., 0:3])
    files = []
    for arr, suffix, is_data in ((base, "BaseColor", False), (nrm, "Normal", True),
                                 (msk, "MaskMap", True), (ctl, "ControlMask", True)):
        path = os.path.join(owner_dir, f"{OWNER}_{suffix}.png")
        cs2_asset._write_png_raw(path, f"{OWNER}_{suffix}", arr, size, is_data)
        files.append(os.path.basename(path))
    return {"files": files, "covered_texels": int(cov.sum()), "coverage": round(float(cov.mean()), 4),
            "painted_texels": painted, "texels_by_material": keys_used}


def compose_materials():
    """{key: albedo (linear rgb), metal, rough, slot} for compose_kit: DRESS_MATERIALS' flat paints
    (read-only), the kit keys, and a FLAT galvanised steel (Metal032's mean color, the beacon's
    values) instead of the CC0 tiling set (no source texture is sampled on this route)."""
    tab = material_table()
    out = {}
    for k, v in tab.items():
        if k == "galv":
            out[k] = {"albedo": np.array(_lin3((0.50, 0.53, 0.565)), np.float32), "metal": 1.0,
                      "rough": 0.43, "slot": 0}
            continue
        out[k] = {"albedo": np.array(v["albedo"], np.float32), "metal": float(v.get("metal", 0.0)),
                  "rough": float(v.get("rough", 0.6) if v.get("rough") is not None else 0.6),
                  "slot": int(v.get("slot", 0) or 0)}
    return out


def bake_kit(groups, offsets, owner_dir, size=A, margin=MARGIN_PX, ao_samples=AO_SAMPLES,
             work_dir=None):
    """Join the low parts, lay out UVUnique (layout_unique), bake the hi twin's tangent normal + AO
    into it with cs2_bake.bake_high_to_low, compose and write the owner's four maps. Returns
    (bake obj, table, report)."""
    allparts = [p for _s, ps in groups for p in ps]
    for p in allparts:
        _set_density(p.obj, p.w)
    table = cs2_asset.tag_parts([(p.obj, p.mat) for p in allparts])
    matkey_of = {p.obj.name: p.mat for p in allparts}
    his = [h for p in allparts for h in p.hi]
    hi_tris = sum(cs2_asset.tri_count(h) for h in his)
    bake = _join([p.obj for p in allparts], "DKBake")
    cs2_asset.store_part_table(bake, table)
    cs2_asset.shade_by_angle(bake, 66.0)
    lay = layout_unique(bake, table, allparts, size, margin)
    high = _join(his, "DKBakeHi")
    bpy.ops.object.select_all(action="DESELECT")
    high.select_set(True)
    bpy.context.view_layer.objects.active = high
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(35.0), keep_sharp_edges=True)
    world = bpy.context.scene.world
    saved = world.light_settings.distance if world is not None else None
    if world is not None:
        world.light_settings.distance = AO_DISTANCE
    work = work_dir or cs2_bake.default_work_dir(OWNER)
    t0 = time.perf_counter()
    try:
        n_path, a_path = cs2_bake.bake_high_to_low(high, bake, owner_dir, OWNER, size,
                                                   cage_extrusion=CAGE, max_ray=MAX_RAY,
                                                   ao_samples=ao_samples, margin_px=margin,
                                                   out_dir=work, uv_layer=UNIQUE_NAME)
    finally:
        if world is not None:
            world.light_settings.distance = saved
    t_bake = time.perf_counter() - t0
    comp = compose_kit(bake, table, matkey_of, size, n_path, a_path, owner_dir, offsets, margin)
    me = bake.data
    for name in [l.name for l in me.uv_layers if l.name != UNIQUE_NAME]:
        me.uv_layers.remove(me.uv_layers[name])
    me.uv_layers.active = me.uv_layers[UNIQUE_NAME]
    me.materials.clear()
    rep = {"layout": lay, "compose": comp, "hi_tris": hi_tris, "low_tris": cs2_asset.tri_count(bake),
           "seconds_high_to_low": round(t_bake, 2), "share": {"dup_parts": lay["dup_parts"]},
           "route": "cs2_bake.bake_high_to_low into an own UVUnique (layout_unique) + compose_kit"}
    _remove(high)
    return bake, table, rep


def extract(bake, table, code, offset, name):
    """One prop: the bake object's faces of `code`, moved back to its pivot (custom normals and
    UVUnique kept: bmesh keeps generic corner attributes)."""
    o = bake.copy()
    o.data = bake.data.copy()
    _link(o)
    keep = {i for i, n in enumerate(table, start=1) if n.startswith(code + "_")}
    bm = bmesh.new()
    bm.from_mesh(o.data)
    lay = bm.faces.layers.int["part"]
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f[lay] not in keep], context="FACES")
    loose = [v for v in bm.verts if not v.link_faces]
    if loose:
        bmesh.ops.delete(bm, geom=loose, context="VERTS")
    bmesh.ops.translate(bm, verts=bm.verts[:], vec=-Vector(offset))
    bm.to_mesh(o.data)
    bm.free()
    o.data.update()
    o.name = name
    o.data.name = name
    if o.name != name:
        raise RuntimeError(f"extract: {name} renamed {o.name}")
    return o


# ======================================================================================
# Measurements / contracts (DK1-DK6)
# ======================================================================================

def bounds_prefab(obj):
    """m_Bounds the import writes: prefab (x, y, z) = Blender (x, z, -y)."""
    co = _local_co(obj.data)
    x, y, z = co[:, 0], co[:, 2], -co[:, 1]
    return {"min": [round(float(x.min()), 4), round(float(y.min()), 4), round(float(z.min()), 4)],
            "max": [round(float(x.max()), 4), round(float(y.max()), 4), round(float(z.max()), 4)]}


def uv_faults(obj, layer="UVUnique"):
    """(mirrored, zero-area) faces of a UV layer, winding vs the face's loop order (DK5)."""
    me = obj.data
    uv = cs2_uv.loop_uvs(me.uv_layers[layer])
    start, total = cs2_uv.face_loop_table(me)
    mir = zero = 0
    for i in range(len(start)):
        s, t = int(start[i]), int(total[i])
        x, y = uv[s:s + t, 0], uv[s:s + t, 1]
        sa = 0.5 * float(np.dot(x, np.roll(y, -1)) - np.dot(y, np.roll(x, -1)))
        if abs(sa) < 1e-12:
            zero += 1
        elif sa < 0.0:
            mir += 1
    return mir, zero


def _wmedian(d, a, q=0.5):
    o = np.argsort(d, kind="stable")
    cum = np.cumsum(a[o]) / a.sum()
    return float(d[o][min(np.searchsorted(cum, q), len(d) - 1)])


def density_stats(obj, size=A, layer="UVUnique"):
    """Area-weighted shipped px/m over the prop's faces (DK6): median / p10 / min of all faces,
    the median of the full-weight faces (w = 1: the visible surfaces) and the area share of the
    deliberately down-weighted faces (hidden sides, plain bars / posts)."""
    me = obj.data
    d = cs2_uv.face_px_per_m(me, me.uv_layers[layer], size)
    a = cs2_uv.face_world_areas(me)
    w = cs2_uv.face_density_weights(me)
    ok = np.isfinite(d) & (a > 0)
    d, a, w = d[ok], a[ok], w[ok]
    full = w >= 0.999
    return {"median": round(_wmedian(d, a), 1), "p10": round(_wmedian(d, a, 0.1), 1),
            "min": round(float(d.min()), 1), "area_m2": round(float(a.sum()), 3),
            "median_w1": round(_wmedian(d[full], a[full]), 1) if full.any() else None,
            "downweighted_area_share": round(float(a[~full].sum() / a.sum()), 3)}


def box_blur(a, r):
    """vanilla-detail-benchmark.data/scripts/nmstats.py box_blur (verbatim math)."""
    k = 2 * r + 1
    p = np.pad(a, ((r, r), (r, r), (0, 0)), mode="edge")
    c = np.cumsum(p, axis=0, dtype=np.float64)
    c = np.concatenate([np.zeros((1,) + c.shape[1:]), c], 0)
    a1 = (c[k:] - c[:-k]) / k
    c = np.cumsum(a1, axis=1)
    c = np.concatenate([np.zeros((a1.shape[0], 1, a1.shape[2])), c], 1)
    return ((c[:, k:] - c[:, :-k]) / k).astype(np.float32)


def normal_detail(nx, ny, mask, r=3):
    """Fine-detail (hf) rms of a tangent normal map on `mask`: deviation of (nx, ny) from its
    (2r+1)^2 box blur, arcsin, rms over texels whose whole window is inside the mask - the
    benchmark's metric (nmstats.normal_stats_xy; GardenBench01 10.7 deg)."""
    n = np.stack([nx, ny], -1)
    bl = box_blur(n, r)
    dev = np.hypot(*(n - bl).transpose(2, 0, 1))
    devd = np.degrees(np.arcsin(np.minimum(dev, 1.0)))
    mk = mask & (box_blur(mask[..., None].astype(np.float32), r)[..., 0] > 0.999)
    if mk.sum() < 50:
        mk = mask
    if mk.sum() < 1:
        return {"texels": 0, "hf_rms_deg": 0.0}
    t = np.hypot(nx, ny)
    tilt = np.degrees(np.arcsin(np.minimum(t, 1.0)))
    return {"texels": int(mask.sum()), "core_texels": int(mk.sum()),
            "hf_rms_deg": round(float(np.sqrt((devd[mk] ** 2).mean())), 2),
            "hf_p95_deg": round(float(np.percentile(devd[mk], 95)), 2),
            "tilt_rms_deg": round(float(np.sqrt((tilt[mk] ** 2).mean())), 2)}


def prop_mask(obj, size=A):
    _parts, ppm = cs2_compose.rasterise_face_fields(obj.data, size, "UVUnique")
    return ppm > 0, ppm


def part_breakdown(obj, nx, ny, nb, family, size=A):
    """Per part family (frame / gear item:matkey / hero part): texels, normals with B <= 0.55
    (bake misses), fine-detail rms - the tuning table of the build report."""
    table = json.loads(obj.data[cs2_asset.PART_TABLE_PROP])
    pmap, _ppm = cs2_compose.rasterise_face_fields(obj.data, size, "UVUnique")
    fam_mask = {}
    for p in sorted(int(v) for v in np.unique(pmap) if v > 0):
        fam = family.get(table[p - 1], table[p - 1])
        fam_mask[fam] = fam_mask.get(fam, np.zeros(pmap.shape, dtype=bool)) | (pmap == p)
    out = {}
    for fam in sorted(fam_mask):
        m = fam_mask[fam]
        nd = normal_detail(nx, ny, m)
        out[fam] = {"texels": int(m.sum()), "bad_b": int(((nb <= 0.55) & m).sum()),
                    "hf_rms_deg": nd["hf_rms_deg"]}
    return out


def read_normal(path):
    px = cs2_validate.load_png_pixels(path)
    return px[..., 0] * 2.0 - 1.0, px[..., 1] * 2.0 - 1.0, px[..., 2]


def cull_estimate(obj, tris):
    """FS10: cs2_preview.lod_switch_table cull (lodFactor 652, thin-mesh term) x 2^-m_LodBias with
    the installed small-mesh bias m_LodBias = 1.2 (s / 5)^3 (s = max dim <= 5 m; MeshSystem.cs:
    497-503, 1.6.2f1 only). [U] until SkiProbeLodA."""
    import cs2_preview
    co = _local_co(obj.data)
    size = (co.max(axis=0) - co.min(axis=0)).tolist()
    area = float(cs2_uv.face_world_areas(obj.data).sum())
    t = cs2_preview.lod_switch_table(size, [tris], lod_factor=LOD_FACTOR, area=area)
    s = max(size)
    small = 1.2 * (s / 5.0) ** 3 if 0.0 < s <= 5.0 else 0.0
    return {"max_dim_m": round(s, 3), "surface_m2": round(area, 3),
            "thin_bias": round(t["bias"], 3), "small_mesh_bias": round(small, 3),
            "cull_m_1_6_0": round(t["cull"], 1), "cull_m": round(t["cull"] * 2.0 ** (-small), 1),
            "status": "[U] formula until SkiProbeLodA"}


def pivot_check(obj, spec):
    """DK4: ground z 0 everywhere; wall props: back on the wall plane (Blender y max 0); ground
    props centered on the origin; leaning props: foot line on the origin."""
    co = _local_co(obj.data)
    lo, hi = co.min(axis=0), co.max(axis=0)
    fails = []
    if abs(lo[2]) > 1e-4:
        fails.append(f"min z {lo[2]:.5f} != 0")
    kind = spec["kind"]
    if kind == "wall" and abs(hi[1]) > 1e-4:
        fails.append(f"wall plane: max y {hi[1]:.5f} != 0")
    if kind == "ground":
        cx = 0.5 * (lo[0] + hi[0])
        if abs(cx) > 0.02:
            fails.append(f"x centre {cx:.4f}")
        if spec["code"][0] in ("R", "E") and abs(0.5 * (lo[1] + hi[1])) > 0.02:
            fails.append(f"y centre {0.5 * (lo[1] + hi[1]):.4f}")
    if kind == "lean":
        foot = co[co[:, 2] <= lo[2] + 0.005]
        if abs(float(foot[:, 1].mean())) > 0.03 or abs(float(foot[:, 0].mean())) > 0.03:
            fails.append(f"foot line centre ({foot[:, 0].mean():.3f}, {foot[:, 1].mean():.3f})")
    return fails


def rest_plane(obj, spec):
    """Leaning props: the plane the top rests on = max Blender y -> prefab z (negative)."""
    if spec["kind"] != "lean":
        return None
    co = _local_co(obj.data)
    return round(-float(co[:, 1].max()), 4)


def check_sharers(out_root):
    """DK2: exactly one folder owns PNGs; every other folder's settings.json maps all four maps
    to the owner's existing files by a relative path. Returns failures."""
    fails = []
    for spec in PROPS:
        d = os.path.join(out_root, spec["name"])
        pngs = sorted(f for f in os.listdir(d) if f.lower().endswith(".png")) if os.path.isdir(d) else []
        with open(os.path.join(d, "settings.json"), encoding="utf-8") as fh:
            st = json.load(fh)
        if spec["name"] == OWNER:
            if pngs != sorted(f"{OWNER}_{m}.png" for m in MAPS):
                fails.append(f"owner PNGs {pngs}")
            if st != {}:
                fails.append(f"owner settings {st}")
            continue
        if pngs:
            fails.append(f"{spec['name']}: own PNGs {pngs}")
        sh = st.get("sharedAssets", {})
        want = {f"{spec['name']}_{m}.png" for m in MAPS}
        if set(sh) != want:
            fails.append(f"{spec['name']}: sharedAssets keys {sorted(sh)}")
        for k, v in sh.items():
            target = os.path.normpath(os.path.join(d, v))
            m = k[len(spec["name"]) + 1:]
            if os.path.basename(target) != f"{OWNER}_{m}" or not os.path.isfile(target):
                fails.append(f"{spec['name']}: {k} -> {v} (missing or not the owner's {m})")
    return fails


# ======================================================================================
# Settings, prefab staging, sub-object bounds
# ======================================================================================

def write_sharer_settings(asset_dir, name):
    """settings.json of a sharer: sharedAssets -> the owner's four maps (same json.dump shape as
    cs2_asset.write_settings: indent 2)."""
    shared = {f"{name}_{m}.png": f"../{OWNER}/{OWNER}_{m}.png" for m in MAPS}
    path = os.path.join(asset_dir, "settings.json")
    with open(path, "w", encoding="utf-8") as fh:
        json.dump({"sharedAssets": shared}, fh, indent=2)
    return path


def mint_cid(path):
    """Hand-minted .cid (beacon rule): 32 lowercase hex, no newline, no BOM, minted ONCE - an
    existing file is kept (never re-mint: rows and saves hang on it)."""
    if os.path.isfile(path):
        with open(path, "rb") as fh:
            raw = fh.read()
        cid = raw.decode("ascii").strip()
        if len(cid) != 32 or any(c not in "0123456789abcdef" for c in cid) or raw != cid.encode():
            raise RuntimeError(f"{path}: not a clean 32-hex cid ({raw!r})")
        return cid, False
    cid = uuid.uuid4().hex
    with open(path, "wb") as fh:
        fh.write(cid.encode("ascii"))
    return cid, True


def object_prefab_json(obj_name, mesh_name):
    """StaticObjectPrefab, no components, one ObjectMeshInfo (towers-transfer.md B2 skeleton)."""
    return (
        '{\n'
        '    "$id": 0,\n'
        '    "$type": "0|Game.Prefabs.StaticObjectPrefab, Game",\n'
        f'    "name": "{obj_name}",\n'
        '    "active": true,\n'
        '    "components": {\n'
        '        "$id": 1,\n'
        '        "$type": "1|System.Collections.Generic.List`1[[Game.Prefabs.ComponentBase, Game]], mscorlib",\n'
        '        "$rlength": 0,\n'
        '        "$rcontent": [\n'
        '        ]\n'
        '    },\n'
        '    "m_Meshes": {\n'
        '        "$id": 2,\n'
        '        "$type": "2|Game.Prefabs.ObjectMeshInfo[], Game",\n'
        '        "$rlength": 1,\n'
        '        "$rcontent": [\n'
        '            {\n'
        '                "$id": 3,\n'
        '                "$type": "3|Game.Prefabs.ObjectMeshInfo, Game",\n'
        f'                "m_Mesh": $fstrref:"CID:REPLACE_{mesh_name}_MESH_CID",\n'
        '                "m_Position": {\n'
        '                    "$type": "4|Unity.Mathematics.float3, Unity.Mathematics",\n'
        '                    "x": 0,\n'
        '                    "y": 0,\n'
        '                    "z": 0\n'
        '                },\n'
        '                "m_Rotation": {\n'
        '                    "$type": "5|Unity.Mathematics.quaternion, Unity.Mathematics",\n'
        '                    "value": {\n'
        '                        "$type": "6|Unity.Mathematics.float4, Unity.Mathematics",\n'
        '                        "x": 0,\n'
        '                        "y": 0,\n'
        '                        "z": 0,\n'
        '                        "w": 1\n'
        '                    }\n'
        '                },\n'
        '                "m_RequireState": 0\n'
        '            }\n'
        '        ]\n'
        '    },\n'
        '    "m_Circular": false\n'
        '}\n')


def _num(v):
    s = f"{v:.6f}".rstrip("0").rstrip(".")
    return s if s not in ("-0", "") else "0"


def colour_components_json():
    """RETIRED 2026-10-12 (FS7 Parent dropped by Richard; kit color variants): never written any more, kept so
    the archived staging (assets/staging/archive/superseded-parent-2026-10-12) stays reproducible.
    The mesh prefab's `components` block (FS7): ColorProperties, ExternalColorSource Parent (1),
    channel 1 external, 0 / 2 not; 4 fallback VariationSets (slot-2 color / RECOLOR_ALBEDO,
    clamped; channels 0 / 2 white: no R / B texels). Type ids 1-9; the importer tail renumbers to
    10 (AssetReference[]), 11 (Bounds3), 12 (float3) exactly as SkiHydrantBoxA Mesh."""
    sets = []
    rid = 102
    for k, rgb in enumerate(FALLBACK_SLOT2):
        c = [min(1.0, v / cs2_asset.RECOLOR_ALBEDO) for v in rgb]
        t_set = '"4|Game.Prefabs.ColorProperties+VariationSet, Game"' if k == 0 else "4"
        t_arr = '"5|UnityEngine.Color[], UnityEngine.CoreModule"' if k == 0 else "5"
        t_col = '"6|UnityEngine.Color, UnityEngine.CoreModule"' if k == 0 else "6"
        sets.append(
            '                        {\n'
            f'                            "$id": {rid},\n'
            f'                            "$type": {t_set},\n'
            '                            "m_Colors": {\n'
            f'                                "$id": {rid + 1},\n'
            f'                                "$type": {t_arr},\n'
            '                                "$rlength": 3,\n'
            '                                "$rcontent": [\n'
            f'                                    {{ "$type": {t_col}, 1, 1, 1, 1 }},\n'
            f'                                    {{ "$type": 6, {_num(c[0])}, {_num(c[1])}, {_num(c[2])}, 1 }},\n'
            '                                    { "$type": 6, 1, 1, 1, 1 }\n'
            '                                ]\n'
            '                            },\n'
            '                            "m_VariationGroup": ""\n'
            '                        }')
        rid += 2
    return (
        '    "components": {\n'
        '        "$id": 1,\n'
        '        "$type": "1|System.Collections.Generic.List`1[[Game.Prefabs.ComponentBase, Game]], mscorlib",\n'
        '        "$rlength": 1,\n'
        '        "$rcontent": [\n'
        '            {\n'
        '                "$id": 100,\n'
        '                "$type": "2|Game.Prefabs.ColorProperties, Game",\n'
        '                "name": "ColorProperties",\n'
        '                "active": true,\n'
        '                "m_ColorVariations": {\n'
        '                    "$id": 101,\n'
        '                    "$type": "3|System.Collections.Generic.List`1[[Game.Prefabs.ColorProperties+VariationSet, Game]], mscorlib",\n'
        f'                    "$rlength": {len(FALLBACK_SLOT2)},\n'
        '                    "$rcontent": [\n'
        + ",\n".join(sets) + '\n'
        '                    ]\n'
        '                },\n'
        '                "m_ChannelsBinding": {\n'
        '                    "$id": 120,\n'
        '                    "$type": "7|System.Collections.Generic.List`1[[Game.Prefabs.ColorProperties+ColorChannelBinding, Game]], mscorlib",\n'
        '                    "$rlength": 3,\n'
        '                    "$rcontent": [\n'
        '                        { "$id": 121, "$type": "8|Game.Prefabs.ColorProperties+ColorChannelBinding, Game", "m_ChannelId": 0, "m_CanBeModifiedByExternal": false },\n'
        '                        { "$id": 122, "$type": 8, "m_ChannelId": 1, "m_CanBeModifiedByExternal": true },\n'
        '                        { "$id": 123, "$type": 8, "m_ChannelId": 2, "m_CanBeModifiedByExternal": false }\n'
        '                    ]\n'
        '                },\n'
        '                "m_VariationRanges": { "$type": "9|Unity.Mathematics.int3, Unity.Mathematics", "x": 0, "y": 0, "z": 0 },\n'
        '                "m_AlphaRanges": { "$type": 9, "x": 0, "y": 0, "z": 0 },\n'
        '                "m_ExternalColorSource": 1\n'
        '            }\n'
        '        ]\n'
        '    },\n')


def write_staging(spec, bounds):
    """assets/staging/prefabs/props/B/<Object>/: <Object>.Prefab (template), <Object>.Prefab.cid
    (minted once) - UNCOLORED props only. A colored prop ships as color-set variants (stage_variant), its
    base object prefab is retired: the record says so and nothing is written. Returns the record."""
    if spec["colour"] == "variants":
        return retired_base_record(spec)
    d = os.path.join(STAGING, spec["obj"])
    os.makedirs(d, exist_ok=True)
    pf = os.path.join(d, f"{spec['obj']}.Prefab")
    with open(pf, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(object_prefab_json(spec["obj"], spec["name"]))
    cid, minted = mint_cid(pf + ".cid")
    rec = {"object_prefab": spec["obj"], "prefab": os.path.relpath(pf, REPO).replace("\\", "/"),
           "cid": cid, "cid_minted_this_run": minted,
           "mesh_cid_placeholder": f"CID:REPLACE_{spec['name']}_MESH_CID"}
    return rec


def update_subobj_bounds(records):
    """ADDITIVE: one entry per kit object prefab (keyed by its name, guid null) so cs2_subobj
    draws real bounds for CID rows by prefab name; every existing entry is left byte-for-byte
    (same json.dump shape as cs2_subobj.build_bounds_cache; build_bounds_cache keeps them)."""
    with open(SUBOBJ_JSON, "r", encoding="utf-8") as fh:
        before = fh.read()
    data = json.loads(before)
    props = data["props"]
    old_keys = set(props)
    for r in records:
        props[r["prefab"]] = {
            "bounds": [[float(v) for v in r["bounds"]["min"]], [float(v) for v in r["bounds"]["max"]]],
            "guid": None, "mesh_offset_variants": [], "meshes": [f"{r['mesh']} Mesh"],
            "prefab": r["prefab"], "prefabs": sorted({r["prefab"], r["mesh"]}),
            "source": "dress_kit (game local, prefab frame)", "tris": int(r["tris"]),
            "variants": []}
    data["props"] = {k: props[k] for k in sorted(props)}
    text = json.dumps(data, indent=1, sort_keys=True) + "\n"
    old = json.loads(before)["props"]
    for k in old_keys:
        if json.dumps(old[k], sort_keys=True) != json.dumps(data["props"][k], sort_keys=True) \
                and k not in {r["prefab"] for r in records}:
            raise RuntimeError(f"subobj_bounds: existing entry {k} would change")
    with open(SUBOBJ_JSON, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(text)
    return {"added_or_updated": sorted(r["prefab"] for r in records),
            "existing_unchanged": len(old_keys - {r["prefab"] for r in records})}


def settings_proof():
    """FS12: cs2_asset.write_settings is not edited; prove its default output is byte-identical
    on shipped settings.json files by regenerating them into a temp folder."""
    cases = [("props/A/SkiBeaconRedA", "SkiBeaconRedA", ()),
             ("towers/O/SkiLiftTowerGD16O", "SkiLiftTowerGD16O", (1, 2))]
    out = []
    tmp = tempfile.mkdtemp(prefix="dk_settings_")
    try:
        for rel, name, lods in cases:
            shipped = os.path.join(REPO, "assets", "import", rel, "settings.json")
            if not os.path.isfile(shipped):
                out.append({"asset": name, "skipped": "no shipped settings.json"})
                continue
            d = os.path.join(tmp, name)
            os.makedirs(d)
            cs2_asset.write_settings(d, name, supplied_lods=lods)
            with open(shipped, "rb") as fh:
                a = hashlib.sha1(fh.read()).hexdigest()
            with open(os.path.join(d, "settings.json"), "rb") as fh:
                b = hashlib.sha1(fh.read()).hexdigest()
            out.append({"asset": name, "supplied_lods": list(lods), "shipped_sha1": a,
                        "regenerated_sha1": b, "identical": a == b})
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    for r in out:
        print(f"[dress_kit] settings proof {r}")
    return out


# ======================================================================================
# Kit color variants: the registry (2026-10-12, build/kit-colour-variants.md)
# ======================================================================================
#
# Richard (verdicts 2026-10-11, ~18:50): "Sorry, I don't think we want the accents to follow the parent
# color. I think they should stay a color that makes sense for that building." -> FS7 Parent DROPPED.
# ENGINE (decompile 1.6.0f1, unchanged in 1.6.2f1): ColorProperties is a RenderPrefab component
# (ColorProperties.cs:12-16 ComponentMenu RenderPrefab / CharacterOverlay; :137-140 puts the ColorVariation
# buffer on the MESH prefab entity); MeshColorSystem.cs:410-471 walks the object prefab's SubMesh buffer and
# SetColor(.., subMesh.m_SubMesh, ..) reads m_ColorVariations of that MESH prefab (:541). An object prefab
# cannot carry its own color: a color variant needs its own mesh prefab. Hence per (prop, set):
#   <Variant>.Prefab              StaticObjectPrefab, one ObjectMeshInfo -> CID of the variant mesh (staged, complete)
#   <Variant> Mesh.Prefab         RenderPrefab built AT SURGERY from the imported base mesh prefab
#                                 (<SEED>\<Mesh>\<Mesh> Mesh_<hash>.Prefab): same m_GeometryAsset, m_SurfaceAssets,
#                                 bounds and counts; name "<Variant> Mesh"; components = the fixed ColorProperties
#                                 (clone_mesh_prefab; variant_surgery writes both into <ID>\<Variant>\)
#   .Prefab.cid of both           seeded md5 of the names (fine_detail.variant_cids): stable across runs
# ONE VariationSet at probability 100 with ranges 0 and no external channel = one deterministic color
# (MeshColorSystem.cs:560-702, 742-857). [U] a hand-made RenderPrefab sharing an imported geometry asset: no
# precedent of ours; the funicular pack ships two NetPiecePrefabs (RenderPrefab subclasses) on ONE geometry +
# surface (docs/research/funicular-network-vehicle.md 3.4); S1 is the first proof.

VARIANT_TABLE = os.path.join(STAGING, "kit-variants.json")
STAGING_ARCHIVE = os.path.join(REPO, "assets", "staging", "archive")
RETIRED_DIR = os.path.join(STAGING_ARCHIVE, "superseded-parent-2026-10-12", "prefabs", "props", LETTER)
UNUSED_DIR = os.path.join(STAGING_ARCHIVE, "unused-variants", "prefabs", "props", LETTER)
IMPORT_ROOT = os.path.join(REPO, "assets", "import")
EDITOR_SEED = "c030ee3ff256df91"
#: the six colored props (ControlMask G texels): they ship only as variants
COLOURED = tuple(s["obj"] for s in PROPS if s["colour"] == "variants")
BY_OBJ = {s["obj"]: s for s in PROPS}


def _fd():
    import fine_detail as FD
    return FD


def colour_record(spec):
    """The kit manifest's `colour` value of a prop (export and patch_kit_manifests write the same)."""
    if spec["colour"] != "variants":
        return None
    return {"ColorProperties": "per colour-set VARIANT (kit colour variants 2026-10-12): the variant's own mesh "
                               "prefab (a RenderPrefab clone of this mesh's imported prefab) carries ONE fixed "
                               "VariationSet (white / set colour / white), no external channel, ranges 0; the "
                               "imported mesh prefab itself gets NO ColorProperties; the FS7 Parent block and the "
                               "base object prefab are retired",
            "slot": 2, "variants": "assets/staging/prefabs/props/B/kit-variants.json",
            "colour_sets": sorted(_fd().COLOUR_SETS)}


def retired_base_record(spec):
    """kit["staging"] entry of a colored prop: no base object prefab ships."""
    return {"object_prefab": None, "retired_base_object_prefab": spec["obj"],
            "retired_base_cid": _fd().KIT[spec["obj"]].cid,
            "retired_to": os.path.relpath(os.path.join(RETIRED_DIR, spec["obj"]), REPO).replace("\\", "/"),
            "ships_as": "colour-set variants <Object>_<Set> (assets/staging/prefabs/props/B/kit-variants.json)"}


def variant_components_json(rgb):
    """The variant MESH prefab's `components` block: ColorProperties with ONE VariationSet (channel 0 white,
    channel 1 = the set's slot-2 color, channel 2 white: the kit has no R / B ControlMask texel), every
    m_CanBeModifiedByExternal false (ColorVariation.hasExternalChannels false: FindExternalSource never runs),
    m_VariationRanges / m_AlphaRanges 0 (no RandomizeColor), m_ExternalColorSource 0 (= Brand; ColorSourceType
    has no None - with no external channel the field is never read; the deployed SkiHydrantBoxA Mesh shape).
    Type ids 1-9 and $ids 1 / 100-107; clone_mesh_prefab renumbers the importer tail after them (10-12)."""
    r, g, b = (_num(v) for v in rgb)
    return (
        '    "components": {\n'
        '        "$id": 1,\n'
        '        "$type": "1|System.Collections.Generic.List`1[[Game.Prefabs.ComponentBase, Game]], mscorlib",\n'
        '        "$rlength": 1,\n'
        '        "$rcontent": [\n'
        '            {\n'
        '                "$id": 100,\n'
        '                "$type": "2|Game.Prefabs.ColorProperties, Game",\n'
        '                "name": "ColorProperties",\n'
        '                "active": true,\n'
        '                "m_ColorVariations": {\n'
        '                    "$id": 101,\n'
        '                    "$type": "3|System.Collections.Generic.List`1[[Game.Prefabs.ColorProperties+VariationSet, Game]], mscorlib",\n'
        '                    "$rlength": 1,\n'
        '                    "$rcontent": [\n'
        '                        {\n'
        '                            "$id": 102,\n'
        '                            "$type": "4|Game.Prefabs.ColorProperties+VariationSet, Game",\n'
        '                            "m_Colors": {\n'
        '                                "$id": 103,\n'
        '                                "$type": "5|UnityEngine.Color[], UnityEngine.CoreModule",\n'
        '                                "$rlength": 3,\n'
        '                                "$rcontent": [\n'
        '                                    { "$type": "6|UnityEngine.Color, UnityEngine.CoreModule", 1, 1, 1, 1 },\n'
        f'                                    {{ "$type": 6, {r}, {g}, {b}, 1 }},\n'
        '                                    { "$type": 6, 1, 1, 1, 1 }\n'
        '                                ]\n'
        '                            },\n'
        '                            "m_VariationGroup": ""\n'
        '                        }\n'
        '                    ]\n'
        '                },\n'
        '                "m_ChannelsBinding": {\n'
        '                    "$id": 104,\n'
        '                    "$type": "7|System.Collections.Generic.List`1[[Game.Prefabs.ColorProperties+ColorChannelBinding, Game]], mscorlib",\n'
        '                    "$rlength": 3,\n'
        '                    "$rcontent": [\n'
        '                        { "$id": 105, "$type": "8|Game.Prefabs.ColorProperties+ColorChannelBinding, Game", "m_ChannelId": 0, "m_CanBeModifiedByExternal": false },\n'
        '                        { "$id": 106, "$type": 8, "m_ChannelId": 1, "m_CanBeModifiedByExternal": false },\n'
        '                        { "$id": 107, "$type": 8, "m_ChannelId": 2, "m_CanBeModifiedByExternal": false }\n'
        '                    ]\n'
        '                },\n'
        '                "m_VariationRanges": { "$type": "9|Unity.Mathematics.int3, Unity.Mathematics", "x": 0, "y": 0, "z": 0 },\n'
        '                "m_AlphaRanges": { "$type": 9, "x": 0, "y": 0, "z": 0 },\n'
        '                "m_ExternalColorSource": 0\n'
        '            }\n'
        '        ]\n'
        '    },\n')


def variant_object_prefab_json(name, mesh_cid):
    """The variant's object prefab: the B2 skeleton of object_prefab_json with m_Mesh = the variant MESH
    prefab's own (seeded) CID - complete, nothing to replace at surgery."""
    t = object_prefab_json(name, "VARIANTMESH")
    old = 'CID:REPLACE_VARIANTMESH_MESH_CID'
    if t.count(old) != 1:
        raise RuntimeError("variant_object_prefab_json: placeholder not found once")
    return t.replace(old, f"CID:{mesh_cid}")


def variant_record(prop, colour_set):
    FD = _fd()
    name = FD.variant_name(prop, colour_set)
    ocid, mcid = FD.variant_cids(name)
    cs = FD.COLOUR_SETS[colour_set]
    return {"name": name, "object_prefab_cid": ocid, "mesh_prefab": FD.variant_mesh_name(name),
            "mesh_prefab_cid": mcid, "base_prop": prop, "base_mesh": FD.KIT[prop].mesh,
            "base_mesh_prefab": f"{FD.KIT[prop].mesh} Mesh", "colour_set": colour_set,
            "rgb_slot2": [float(v) for v in cs.rgb], "retired_base_cid": FD.KIT[prop].cid}


def stage_variant(prop, colour_set, staging=STAGING):
    """Mint ONE variant into staging (idempotent): <Variant>/<Variant>.Prefab (+ .cid), "<Variant> Mesh.Prefab.cid"
    and "<Variant> Mesh.components.json". UTF-8 without BOM, LF; a .cid is 32 lowercase hex without newline and
    is never re-minted (an existing different .cid raises). Returns the variant record + "written" files."""
    FD = _fd()
    rec = variant_record(prop, colour_set)
    name, mesh = rec["name"], rec["mesh_prefab"]
    d = os.path.join(staging, name)
    os.makedirs(d, exist_ok=True)
    files = ((f"{name}.Prefab", variant_object_prefab_json(name, rec["mesh_prefab_cid"])),
             (f"{name}.Prefab.cid", rec["object_prefab_cid"]),
             (f"{mesh}.Prefab.cid", rec["mesh_prefab_cid"]),
             (f"{mesh}.components.json", variant_components_json(FD.COLOUR_SETS[colour_set].rgb)))
    written = []
    for fn, text in files:
        path = os.path.join(d, fn)
        data = text.encode("ascii" if fn.endswith(".cid") else "utf-8")
        if os.path.isfile(path):
            with open(path, "rb") as fh:
                old = fh.read()
            if old == data:
                continue
            if fn.endswith(".cid"):
                raise RuntimeError(f"{path}: staged cid {old!r} != the seeded {data!r} (never re-mint)")
        with open(path, "wb") as fh:
            fh.write(data)
        written.append(fn)
    rec["staged"] = [fn for fn, _t in files]
    rec["written"] = written
    return rec


def _iter_manifests(import_root=IMPORT_ROOT):
    for dp, dns, fns in os.walk(import_root):
        dns[:] = sorted(n for n in dns if n != "archive")
        for fn in sorted(fns):
            if fn.endswith(".manifest.json"):
                yield os.path.join(dp, fn), fn[:-len(".manifest.json")]


def _mod_rows(o, out):
    if isinstance(o, dict):
        if o.get("kind") == "mod" and o.get("colour_set"):
            out.append(o)
        for v in o.values():
            _mod_rows(v, out)
    elif isinstance(o, list):
        for v in o:
            _mod_rows(v, out)


def used_variants(import_root=IMPORT_ROOT):
    """{variant name: {"prop", "colour_set", "hosts": [..], "rows": n}} from every shipped host manifest's kit
    "mod" rows that name a color set (the ON-DEMAND set: only what a host uses)."""
    FD = _fd()
    used = {}
    for path, name in _iter_manifests(import_root):
        with open(path, "r", encoding="utf-8") as fh:
            m = json.load(fh)
        rows = []
        _mod_rows(m, rows)
        for r in rows:
            vn = FD.variant_name(r["prefab"], r["colour_set"])
            if r.get("variant") not in (None, vn):
                raise RuntimeError(f"{name}: row variant {r.get('variant')!r} != {vn!r}")
            cid = str(r.get("m_Object") or r.get("guid") or "").replace("CID:", "")
            if cid != FD.variant_cids(vn)[0]:
                raise RuntimeError(f"{name}: {vn} row CID {cid} != the seeded {FD.variant_cids(vn)[0]}")
            u = used.setdefault(vn, {"prop": r["prefab"], "colour_set": r["colour_set"], "hosts": [], "rows": 0})
            if name not in u["hosts"]:
                u["hosts"].append(name)
            u["rows"] += 1
    return {k: used[k] for k in sorted(used)}


def stage_variants_for_rows(rows):
    """ON DEMAND from a host build: stage every (colored prop, color set) pair its kit rows name."""
    out = []
    for r in rows or ():
        if isinstance(r, dict) and r.get("kind") == "mod" and r.get("colour_set"):
            out.append(stage_variant(r["prefab"], r["colour_set"]))
    return out


def stage_from_manifest(asset_dir, name):
    """The host generators' hook after their manifest write: a SHIPPING export (inside assets/import) stages the
    variants its kit rows name and refreshes kit-variants.json; scratch / test exports touch nothing."""
    ad = os.path.normcase(os.path.abspath(asset_dir))
    if not ad.startswith(os.path.normcase(os.path.abspath(IMPORT_ROOT)) + os.sep):
        return None
    with open(os.path.join(asset_dir, f"{name}.manifest.json"), "r", encoding="utf-8") as fh:
        m = json.load(fh)
    rows = []
    _mod_rows(m, rows)
    if not rows:
        return None
    recs = stage_variants_for_rows(rows)
    write_variant_table()
    print(f"[dress_kit] {name}: staged {sorted({r['name'] for r in recs})} (kit-variants.json refreshed)")
    return recs


def _move_keep(src, dst):
    """Move a staging folder into the archive; never delete: an existing different destination gets a suffix."""
    if not os.path.isdir(src):
        return None
    if os.path.exists(dst):
        k = 2
        while os.path.exists(f"{dst}.{k}"):
            k += 1
        dst = f"{dst}.{k}"
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    shutil.move(src, dst)
    return os.path.relpath(dst, REPO).replace("\\", "/")


def retire_base_staging():
    """Move the six colored BASE object-prefab folders (Parent ColorProperties) out of the staging set."""
    moved = {}
    for obj in COLOURED:
        r = _move_keep(os.path.join(STAGING, obj), os.path.join(RETIRED_DIR, obj))
        if r:
            moved[obj] = r
    return moved


def staged_variant_names(staging=STAGING):
    FD = _fd()
    names = set()
    for p in COLOURED:
        for k in FD.COLOUR_SETS:
            n = FD.variant_name(p, k)
            if os.path.isdir(os.path.join(staging, n)):
                names.add(n)
    return sorted(names)


def write_variant_table(path=VARIANT_TABLE, import_root=IMPORT_ROOT):
    """kit-variants.json: the color sets, every STAGED variant (name, CIDs, base mesh, set, rgb, the shipped
    hosts that use it), the retired base CIDs and the S1 surgery steps. Deterministic (no clock)."""
    FD = _fd()
    used = used_variants(import_root)
    variants = []
    for n in staged_variant_names():
        prop, cs = next((p, k) for p in COLOURED for k in FD.COLOUR_SETS if FD.variant_name(p, k) == n)
        rec = variant_record(prop, cs)
        u = used.get(n)
        rec["hosts"] = u["hosts"] if u else []
        rec["rows"] = u["rows"] if u else 0
        rec["staged"] = sorted(os.listdir(os.path.join(STAGING, n)))
        variants.append(rec)
    data = {
        "generator": GENERATOR + " (kit colour variants: stage_variant / sync_variants / variant_surgery)",
        "report": "docs/research/art/assets-2026-10/build/kit-colour-variants.md",
        "ruling": "Richard 2026-10-11 ~18:50: accents do NOT follow the Parent colour; \"a colour that makes "
                  "sense for that building\" -> one fixed colour set per building (fine_detail.COLOUR_SETS)",
        "engine": "ColorProperties is a RenderPrefab component; MeshColorSystem reads the SubMesh's MESH prefab "
                  "(decompile 1.6.0f1 ColorProperties.cs:12-16 / 137-140, MeshColorSystem.cs:470-471 / 541): "
                  "a variant = its own object prefab + its own mesh prefab (RenderPrefab clone of the imported "
                  "base mesh prefab, same geometry + surface)",
        "colour_sets": {k: {"label": c.label, "rgb_slot2": [float(v) for v in c.rgb], "source": c.source}
                        for k, c in FD.COLOUR_SETS.items()},
        "cid_seeds": {"object_prefab": f"md5('{FD.VARIANT_CID_SEED}' + name)",
                      "mesh_prefab": f"md5('{FD.VARIANT_MESH_CID_SEED}' + name + ' Mesh')"},
        "variants": variants,
        "unstaged_but_used": sorted(set(used) - {v["name"] for v in variants}),
        "retired_base_object_prefabs": {p: FD.KIT[p].cid for p in COLOURED},
        "uncoloured_base_object_prefabs": {s["obj"]: FD.KIT[s["obj"]].cid for s in PROPS if s["colour"] is None},
        "surgery": [
            "Import all 11 props/B folders in ONE Editor action (unchanged; F1 decides share / copy).",
            "The 5 uncoloured object prefabs as before (dress-kit.md 3.1 step 4: REPLACE_<Mesh>_MESH_CID).",
            "The 6 imported coloured base mesh prefabs stay EXACTLY as imported (no ColorProperties: the retired "
            "*B Mesh.components.json Parent blocks are not pasted); no base object prefab for them.",
            "Per variant (game closed): blender ... --python dress_kit.py -- --variant-surgery \"<ImportedData>\" "
            "writes <ID>\\<Variant>\\<Variant>.Prefab + .cid (copied from staging) and <Variant> Mesh.Prefab + .cid "
            "(clone of <SEED>\\<BaseMesh>\\<BaseMesh> Mesh_<hash>.Prefab: name, components = the staged block, "
            "importer tail renumbered 10-12); it refuses a missing / ambiguous base or a different existing file.",
            "validate_prefab.ps1 on each written file; preflight 0 FAIL / 0 WARN (+2 prefabs and +2 cid files per "
            "variant); then the host rows (their m_Object = the variant object-prefab CIDs) may be pasted.",
        ],
    }
    text = json.dumps(data, indent=2) + "\n"
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(text)
    return data


def sync_variants(import_root=IMPORT_ROOT):
    """`--variants`: stage every variant a shipped host uses, archive staged variants no host uses any more,
    retire the six base object prefabs, rewrite kit-variants.json and patch the six colored kit manifests'
    `colour` / `row_spec` (+ the owner's kit_report.staging). Returns the summary."""
    used = used_variants(import_root)
    staged = [stage_variant(u["prop"], u["colour_set"]) for u in used.values()]
    unused = {}
    for n in staged_variant_names():
        if n not in used:
            unused[n] = _move_keep(os.path.join(STAGING, n), os.path.join(UNUSED_DIR, n))
    retired = retire_base_staging()
    table = write_variant_table(import_root=import_root)
    patched = patch_kit_manifests(os.path.join(import_root, "props", LETTER))
    out = {"used": used, "staged": [s["name"] for s in staged],
           "written": {s["name"]: s["written"] for s in staged if s["written"]},
           "archived_unused": unused, "retired_base": retired, "table": os.path.relpath(VARIANT_TABLE, REPO),
           "variants": len(table["variants"]), "kit_manifests_patched": patched}
    print(f"[dress_kit] variants: {len(table['variants'])} staged ({sorted(used)}), archived unused {sorted(unused)}, "
          f"retired base {sorted(retired)}, kit manifests patched {patched}")
    return out


def _dump_like(path, data):
    """Rewrite a manifest exactly as cs2_validate.write_manifest dumps it (indent 2, the file's own newline)."""
    with open(path, "rb") as fh:
        raw = fh.read()
    nl = "\r\n" if b"\r\n" in raw else "\n"
    text = json.dumps(data, indent=2, sort_keys=False, default=str).replace("\n", nl)
    with open(path, "wb") as fh:
        fh.write(text.encode("utf-8"))


def patch_kit_manifests(out_root):
    """In place, only for the six colored props: `colour` (colour_record) and `row_spec` (row_spec), and the
    owner's kit_report.staging entries of the six (retired_base_record). Every other key / byte unchanged (the
    step-3a precedent: only row_spec strings changed). A manifest whose dump does not round-trip is refused."""
    patched = []
    for spec in PROPS:
        path = os.path.join(out_root, spec["name"], f"{spec['name']}.manifest.json")
        if not os.path.isfile(path):
            continue
        with open(path, "rb") as fh:
            raw = fh.read()
        data = json.loads(raw)
        nl = "\r\n" if b"\r\n" in raw else "\n"
        if json.dumps(data, indent=2, sort_keys=False, default=str).replace("\n", nl).encode("utf-8") != raw:
            raise RuntimeError(f"{path}: the dump does not round-trip; refusing an in-place patch")
        before = json.dumps(data, sort_keys=True)
        if spec["colour"] == "variants":
            data["colour"] = colour_record(spec)
            data["row_spec"] = row_spec(spec, {"rest_plane_prefab_z": (data.get("row_spec") or {}).get(
                "rest_plane_prefab_z")}, retired_base_record(spec))
        if spec["name"] == OWNER and isinstance(data.get("kit_report", {}).get("staging"), dict):
            for obj in COLOURED:
                if obj in data["kit_report"]["staging"]:
                    data["kit_report"]["staging"][obj] = retired_base_record(BY_OBJ[obj])
        if json.dumps(data, sort_keys=True) != before:
            _dump_like(path, data)
            patched.append(spec["name"])
    return patched


# ---- surgery: the variant mesh prefab, cloned from the imported base mesh prefab ----------------------------

_DECL = re.compile(r'"\$type":\s*"(\d+)\|([^"]+)"')
_TYPE = re.compile(r'"\$type":\s*(?:"(\d+)\|([^"]+)"|(\d+)\b)')
_IDS = re.compile(r'"\$id":\s*(\d+)\b')


def _decls(text):
    return {int(m.group(1)): m.group(2) for m in _DECL.finditer(text)}


def clone_mesh_prefab(base_text, variant, components_text):
    """The variant MESH prefab from the imported base mesh prefab's text: root "name" -> "<variant> Mesh", the
    whole `components` block (from '    "components": {' to the line before '    "m_GeometryAsset"') ->
    `components_text` (types 1-9), and the importer tail's type table renumbered after the block: a tail
    declaration takes the next free id (10, 11, 12 for the importer's AssetReference[] / Bounds3 / float3 - the
    deployed SkiHydrantBoxA Mesh shape), a bare reference follows its declaration, a reference to a type the old
    components block declared is re-declared at its first use (or points at the same name in the new block).
    m_GeometryAsset, m_SurfaceAssets, m_Bounds and the counts are kept byte for byte. Raises on any shape it
    does not know (validate_prefab rules are re-checked on the result)."""
    if '"$type": "0|Game.Prefabs.RenderPrefab, Game"' not in base_text:
        raise ValueError("clone_mesh_prefab: the base is not a RenderPrefab")
    names = re.findall(r'(?m)^    "name": "([^"]+)",$', base_text)
    if len(names) != 1 or not names[0].endswith(" Mesh"):
        raise ValueError(f"clone_mesh_prefab: root name lines {names}")
    a = base_text.find('    "components": {')
    b = base_text.find('    "m_GeometryAsset"')
    if a < 0 or b < 0 or b < a or base_text.count('    "components": {') != 1:
        raise ValueError(f"clone_mesh_prefab: components / m_GeometryAsset anchors ({a}, {b})")
    head, old_comp, tail = base_text[:a], base_text[a:b], base_text[b:]
    head = head.replace(f'    "name": "{names[0]}",', f'    "name": "{variant} Mesh",', 1)
    new_decl = dict(_decls(head))
    for k, v in _decls(components_text).items():
        if k in new_decl and new_decl[k] != v:
            raise ValueError(f"clone_mesh_prefab: type {k} clash between head and block")
        new_decl[k] = v
    by_name = {v: k for k, v in new_decl.items()}
    old_decl = dict(_decls(head))
    old_decl.update(_decls(old_comp))
    nxt = max(new_decl) + 1
    remap = {}
    out, pos = [], 0
    for m in _TYPE.finditer(tail):
        out.append(tail[pos:m.start()])
        pos = m.end()
        if m.group(1) is not None:                     # a declaration "N|Name"
            old, nm = int(m.group(1)), m.group(2)
            if nm in by_name:
                remap[old] = by_name[nm]
                out.append(f'"$type": {by_name[nm]}')
            else:
                remap[old] = nxt
                by_name[nm] = nxt
                out.append(f'"$type": "{nxt}|{nm}"')
                nxt += 1
        else:                                          # a bare reference N
            old = int(m.group(3))
            if old in remap:
                out.append(f'"$type": {remap[old]}')
            elif old in old_decl and old_decl[old] in by_name:
                remap[old] = by_name[old_decl[old]]
                out.append(f'"$type": {remap[old]}')
            elif old in old_decl:
                remap[old] = nxt
                by_name[old_decl[old]] = nxt
                out.append(f'"$type": "{nxt}|{old_decl[old]}"')
                nxt += 1
            else:
                raise ValueError(f"clone_mesh_prefab: tail references undeclared type {old}")
    out.append(tail[pos:])
    new_tail = "".join(out)
    ids_block = {int(x) for x in _IDS.findall(components_text)} | {int(x) for x in _IDS.findall(head)}
    clash = ids_block & {int(x) for x in _IDS.findall(new_tail)}
    if clash:
        raise ValueError(f"clone_mesh_prefab: $id clash between the new block and the tail {sorted(clash)}")
    text = head + components_text + new_tail
    errs = prefab_text_errors(text)
    if errs:
        raise ValueError(f"clone_mesh_prefab: result fails the validate_prefab rules: {errs[:4]}")
    return text


def prefab_text_errors(text):
    """tools/validate_prefab.ps1 + preflight VALIDATE rules in Python: a bare "$type": N only after "N|Name",
    no id redeclared with another name, balanced braces, no shell-mangle line (> 400 chars, '&n ', '\\n"')."""
    errs, declared, braces = [], {}, 0
    for i, line in enumerate(text.split("\n")):
        braces += line.count("{") - line.count("}")
        if len(line) > 400 or "&n " in line or '\\n"' in line:
            errs.append(f"line {i + 1}: suspicious line")
        for m in _TYPE.finditer(line):
            if m.group(1) is not None:
                k, nm = int(m.group(1)), m.group(2)
                if k in declared and declared[k] != nm:
                    errs.append(f"line {i + 1}: type {k} redeclared as {nm}")
                declared[k] = nm
            elif int(m.group(3)) not in declared:
                errs.append(f"line {i + 1}: forward / undeclared type {m.group(3)}")
    if braces:
        errs.append(f"unbalanced braces ({braces})")
    return errs


def variant_surgery(imported_data, table_path=VARIANT_TABLE, seed=EDITOR_SEED, staging=STAGING):
    """S1 (game closed): for every variant of kit-variants.json write <ID>\\<Variant>\\ = <Variant>.Prefab +
    .Prefab.cid (staged, verbatim) and "<Variant> Mesh.Prefab" + .Prefab.cid (clone_mesh_prefab of the ONE
    imported "<BaseMesh> Mesh_<hash>.Prefab" in <ID>\\<seed>\\<BaseMesh>\\). UTF-8 no BOM, LF kept as the base
    has them. Refuses a missing / ambiguous base and an existing different file. Returns the written paths."""
    with open(table_path, "r", encoding="utf-8") as fh:
        table = json.load(fh)
    written = []
    for v in table["variants"]:
        bdir = os.path.join(imported_data, seed, v["base_mesh"])
        cands = sorted(f for f in (os.listdir(bdir) if os.path.isdir(bdir) else ())
                       if f.startswith(f"{v['base_mesh']} Mesh_") and f.endswith(".Prefab"))
        if len(cands) != 1:
            raise RuntimeError(f"{v['name']}: expected ONE '{v['base_mesh']} Mesh_<hash>.Prefab' in {bdir}, found {cands}")
        with open(os.path.join(bdir, cands[0]), "rb") as fh:
            base = fh.read().decode("utf-8")
        sdir = os.path.join(staging, v["name"])
        with open(os.path.join(sdir, f"{v['mesh_prefab']}.components.json"), "rb") as fh:
            comp = fh.read().decode("utf-8")
        crlf = "\r\n" in base
        mesh_text = clone_mesh_prefab(base.replace("\r\n", "\n"), v["name"], comp)
        if crlf:
            mesh_text = mesh_text.replace("\n", "\r\n")
        files = {f"{v['mesh_prefab']}.Prefab": mesh_text.encode("utf-8"),
                 f"{v['mesh_prefab']}.Prefab.cid": v["mesh_prefab_cid"].encode("ascii")}
        for fn in (f"{v['name']}.Prefab", f"{v['name']}.Prefab.cid"):
            with open(os.path.join(sdir, fn), "rb") as fh:
                files[fn] = fh.read()
        if files[f"{v['name']}.Prefab.cid"].decode() != v["object_prefab_cid"]:
            raise RuntimeError(f"{v['name']}: staged object .cid != the table's")
        odir = os.path.join(imported_data, v["name"])
        os.makedirs(odir, exist_ok=True)
        for fn, data in files.items():
            p = os.path.join(odir, fn)
            if os.path.isfile(p):
                with open(p, "rb") as fh:
                    if fh.read() == data:
                        continue
                raise RuntimeError(f"{p} exists with different content (remove it first)")
            with open(p, "wb") as fh:
                fh.write(data)
            written.append(p)
    print(f"[dress_kit] variant surgery: {len(table['variants'])} variant(s), {len(written)} file(s) written "
          f"under {imported_data}")
    return written


# ======================================================================================
# Previews (2 m and 10 m PBR, two-tone normal view) and review packs
# ======================================================================================

VIEW_DIR = (0.55, -1.0, 0.45)
VIEW_DIR_10 = (0.55, -1.0, 0.75)
FOV_10M_DEG = 2.0 * math.degrees(math.atan(math.tan(math.radians(22.5)) * 1000.0 / 2160.0))


def _bbox_centre(obj):
    co = _local_co(obj.data)
    return Vector(((co.min(0) + co.max(0)) * 0.5).tolist())


def _set_fov(cam, deg):
    cam.data.sensor_fit = "VERTICAL"
    cam.data.sensor_height = 24.0
    cam.data.lens = 12.0 / math.tan(math.radians(deg) / 2.0)


def _twotone_material(rig, owner_dir):
    """Diffuse white + the baked normal map: under a warm key and a cool fill the relief reads in
    two tones (no albedo, no AO)."""
    mat = rig.track_data(bpy.data.materials, bpy.data.materials.new("DK_TwoTone"))
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = (0.8, 0.8, 0.8, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.9
    img = rig.track_data(bpy.data.images, bpy.data.images.load(os.path.join(owner_dir, f"{OWNER}_Normal.png")))
    img.colorspace_settings.name = "Non-Color"
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    nm = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(tex.outputs["Color"], nm.inputs["Color"])
    nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    return mat


def render_previews(objs, owner_dir, out_dir, samples=None):
    """Per prop: <Name>_pbr_2m, <Name>_pbr_10m (the 4K / 45 deg angular density: 261 px/m at 10 m,
    a 1000 px crop), <Name>_normal2tone_2m; plus SkiDressKitB_lineup_pbr. Returns paths."""
    import cs2_preview as P
    os.makedirs(out_dir, exist_ok=True)
    written = []
    for name, obj in objs:
        rig = P._Rig(obj, samples)
        try:
            cam = P._make_camera(rig)
            sc = rig.scene
            sc.render.resolution_x, sc.render.resolution_y = 1000, 1000
            c = _bbox_centre(obj)
            world, sun = P._make_world(rig, **P.NOON)
            gnd = P._make_ground(rig)
            P._assign(obj, P._pbr_material(rig, owner_dir, OWNER, None, mat_name=f"{name}_DKPBR"))
            _set_fov(cam, 45.0)
            P._aim(cam, c, VIEW_DIR, 2.0)
            written.append(P._render(rig, os.path.join(out_dir, f"{name}_pbr_2m.png"), engine="EEVEE"))
            _set_fov(cam, FOV_10M_DEG)
            P._aim(cam, c, VIEW_DIR_10, 10.0)
            written.append(P._render(rig, os.path.join(out_dir, f"{name}_pbr_10m.png"), engine="EEVEE"))
            for o in (gnd, sun):
                rig.objects.remove(o)
                bpy.data.objects.remove(o, do_unlink=True)
            sc.world = None
            P._assign(obj, _twotone_material(rig, owner_dir))
            wd = rig.track_data(bpy.data.worlds, bpy.data.worlds.new("DK2toneWorld"))
            wd.use_nodes = True
            bg = wd.node_tree.nodes.get("Background")
            if bg is not None:
                bg.inputs[0].default_value = (0.10, 0.10, 0.11, 1.0)
                bg.inputs[1].default_value = 1.0
            sc.world = wd
            # warm key from the camera's right, cool key from its left, both at ~80 deg to the view
            # direction: grazing on every surface the camera looks at, so the relief reads
            v = Vector(VIEW_DIR).normalized()
            right = (-v).cross(Vector((0.0, 0.0, 1.0))).normalized()
            for dvec, col, en in (((right + v * 0.18 + Vector((0, 0, 0.12))), (1.0, 0.62, 0.32), 3.5),
                                  ((-right + v * 0.18 + Vector((0, 0, 0.12))), (0.30, 0.55, 1.0), 2.5)):
                ld = bpy.data.lights.new("DK2tone", "SUN")
                ld.energy = en
                ld.color = col
                lo = rig.track(_link(bpy.data.objects.new("DK2tone", ld)))
                lo.rotation_euler = (-Vector(dvec).normalized()).to_track_quat("-Z", "Y").to_euler()
            _set_fov(cam, 45.0)
            P._aim(cam, c, VIEW_DIR, 2.0)
            written.append(P._render(rig, os.path.join(out_dir, f"{name}_normal2tone_2m.png"),
                                     engine="EEVEE", view_transform="Standard"))
        finally:
            rig.restore()
        print(f"[dress_kit] previews {name} -> {out_dir}")
    return written


def render_lineup(objs, owner_dir, out_dir, samples=None):
    import cs2_preview as P
    tmp = []
    x = 0.0
    for name, obj in objs:
        o = obj.copy()
        o.data = obj.data.copy()
        _link(o)
        co = _local_co(o.data)
        o.location = (x - float(co[:, 0].min()), 0.0, 0.0)
        x += float(co[:, 0].max() - co[:, 0].min()) + 0.4
        tmp.append(o)
    bpy.context.view_layer.update()
    joined = _join(tmp, "DKLineup")
    rig = P._Rig(joined, samples)
    path = os.path.join(out_dir, "SkiDressKitB_lineup_pbr.png")
    try:
        cam = P._make_camera(rig)
        world, sun = P._make_world(rig, **P.NOON)
        P._make_ground(rig)
        P._assign(joined, P._pbr_material(rig, owner_dir, OWNER, None, mat_name="DKLineupPBR"))
        co = _local_co(joined.data)
        lo_, hi_ = co.min(axis=0), co.max(axis=0)
        ctr = Vector(((lo_ + hi_) * 0.5).tolist())
        _set_fov(cam, 20.0)
        half_h = math.atan(math.tan(math.radians(10.0)) * 2400.0 / 900.0)
        dist = 0.5 * float(hi_[0] - lo_[0]) / math.tan(half_h) * 1.12 + 1.0
        P._aim(cam, ctr, (0.12, -1.0, 0.30), dist)
        rig.scene.render.resolution_x, rig.scene.render.resolution_y = 2400, 900
        P._render(rig, path, engine="EEVEE")
    finally:
        rig.restore()
        _remove(joined)
    return path


def review_tex_dir(name, owner_dir):
    """A sharer's review pack needs <name>_<map>.png beside it: hard links (copies as a fallback)
    of the owner's maps under assets/preview/review/dresskit_B/<name>/ (preview only)."""
    if name == OWNER:
        return owner_dir
    d = os.path.join(REPO, "assets", "preview", "review", f"dresskit_{LETTER}", name)
    os.makedirs(d, exist_ok=True)
    for m in MAPS:
        src = os.path.join(owner_dir, f"{OWNER}_{m}.png")
        dst = os.path.join(d, f"{name}_{m}.png")
        if os.path.exists(dst):
            os.remove(dst)
        try:
            os.link(src, dst)
        except OSError:
            shutil.copy2(src, dst)
    return d


# ======================================================================================
# Export
# ======================================================================================

VALIDATE_SPEC = {"kind": "prop", "min_texture": 1024, "max_texture": 1024, "expected_lods": (0,)}
SHARER_STOCK_CODES = ("folder", "settings")      # what the stock validator reports on a sharer


def _clean_dir(d, name):
    os.makedirs(d, exist_ok=True)
    for f in os.listdir(d):
        p = os.path.join(d, f)
        if os.path.isfile(p) and (f.startswith(name) or f == "settings.json"):
            os.remove(p)


def resolved_validation(asset_dir, name, owner_dir, cap):
    """The sharer as the importer resolves it: FBX + the owner's maps under the sharer's names in
    a temp folder (settings {}), then cs2_validate.validate_asset_folder_ex."""
    tmp = tempfile.mkdtemp(prefix=f"dk_{name}_")
    try:
        d = os.path.join(tmp, name)
        os.makedirs(d)
        shutil.copy2(os.path.join(asset_dir, f"{name}.fbx"), d)
        for m in MAPS:
            shutil.copy2(os.path.join(owner_dir, f"{OWNER}_{m}.png"), os.path.join(d, f"{name}_{m}.png"))
        cs2_asset.write_settings(d, name)
        spec = dict(VALIDATE_SPEC, lod0_cap=cap)
        return cs2_validate.validate_asset_folder_ex(d, name, spec)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


def export(out_root, preview_dir=None, review=False, staging=True, subobj=True, args=None,
           margin=MARGIN_PX):
    """Build, bake, split, validate and write the 11 asset folders (+ staging, bounds, previews,
    review packs). Returns the kit report dict."""
    t0 = time.perf_counter()
    out_root = os.path.abspath(out_root)
    os.makedirs(out_root, exist_ok=True)
    owner_dir = os.path.join(out_root, OWNER)
    for spec in PROPS:
        _clean_dir(os.path.join(out_root, spec["name"]), spec["name"])
    par = {"aframe": {c: parity_aframe(c) for c in AFRAME_LOADOUTS}, "wall": parity_wall(),
           "bounds": parity_bounds()}
    groups, offsets, lib, gear, stats = assemble()
    family = {}                         # part name -> report family (names die at the join)
    for _s, ps in groups:
        for p in ps:
            if p.key is not None and p.key[0] == "gear":
                family[p.obj.name] = f"gear {p.key[1]}:{p.key[2]}"
            elif p.key is not None:
                family[p.obj.name] = "frame"
            else:
                family[p.obj.name] = p.role
    bake, table, rep = bake_kit(groups, offsets, owner_dir, margin=margin)
    t_bake = time.perf_counter() - t0
    nx, ny, nb = read_normal(os.path.join(owner_dir, f"{OWNER}_Normal.png"))
    objs, records, kit = [], [], {"props": {}}
    all_d, all_a = [], []
    for spec, _parts in groups:
        name, code = spec["name"], spec["code"]
        obj = extract(bake, table, code, offsets[code], name)
        d = os.path.join(out_root, name)
        tris = cs2_asset.tri_count(obj)
        mir, zero = uv_faults(obj)
        dens = density_stats(obj)
        mask, _ppm = prop_mask(obj)
        nd = normal_detail(nx, ny, mask)
        bad_b = int(((nb <= 0.55) & mask).sum())
        piv = pivot_check(obj, spec)
        cull = cull_estimate(obj, tris)
        bnd = bounds_prefab(obj)
        cs2_asset.export_fbx(obj, os.path.join(d, f"{name}.fbx"))
        if name == OWNER:
            cs2_asset.write_settings(d, name)
        else:
            write_sharer_settings(d, name)
        mf = cs2_validate.validate_mesh(obj, kind="prop")
        if name == OWNER:
            vf, vsum = cs2_validate.validate_asset_folder_ex(d, name, dict(VALIDATE_SPEC, lod0_cap=spec["cap"]))
            stock = None
        else:
            vf, vsum = resolved_validation(d, name, owner_dir, spec["cap"])
            sf, _ss = cs2_validate.validate_asset_folder_ex(d, name, dict(VALIDATE_SPEC, lod0_cap=spec["cap"]))
            stock = {"errors": sorted({c for l, c, _m in sf if l == "ERROR"}),
                     "n_errors": sum(1 for f in sf if f[0] == "ERROR"),
                     "n_warnings": sum(1 for f in sf if f[0] == "WARN")}
        errs = [f for f in vf + mf if f[0] == "ERROR"]
        warns = [f for f in vf + mf if f[0] == "WARN"]
        d_all = cs2_uv.face_px_per_m(obj.data, obj.data.uv_layers["UVUnique"], A)
        a_all = cs2_uv.face_world_areas(obj.data)
        ok_ = np.isfinite(d_all)
        all_d.append(d_all[ok_])
        all_a.append(a_all[ok_])
        dk = {"DK1_tris_le_cap": tris <= spec["cap"], "DK3_hf_ge_5deg": nd["hf_rms_deg"] >= DK3_MIN_HF_DEG,
              "DK4_pivot": not piv, "DK5_no_mirrored": mir == 0 and zero == 0,
              "DK6_w1_median_ge_250": (dens["median_w1"] or 0.0) >= DK6_MIN_PPM}
        pr = {"code": code, "name": name, "object_prefab": spec["obj"], "part": spec["part"],
              "variant": spec["variant"], "tris": tris, "cap": spec["cap"],
              "tris_hi": stats[code]["tris_hi"], "px_per_m": dens, "normal_detail": nd,
              "normal_b_le_055_texels": bad_b,
              "parts": part_breakdown(obj, nx, ny, nb, family),
              "bounds_m": bnd, "cull": cull, "pivot_fails": piv,
              "rest_plane_prefab_z": rest_plane(obj, spec), "uv_mirrored": mir, "uv_zero": zero,
              "validation": {"errors": len(errs), "warnings": len(warns),
                             "warn_list": [f"{c}: {m}" for _l, c, m in warns][:12],
                             "error_list": [f"{c}: {m}" for _l, c, m in errs][:12]},
              "stock_validator_on_sharer": stock, "dk": dk}
        kit["props"][name] = pr
        objs.append((name, obj))
        records.append({"prefab": spec["obj"], "mesh": name, "bounds": bnd, "tris": tris})
        print(f"[dress_kit] {name}: {tris} tris (cap {spec['cap']}), hi {stats[code]['tris_hi']}, "
              f"px/m med {dens['median']} p10 {dens['p10']}, hf {nd['hf_rms_deg']} deg, "
              f"pivot {'OK' if not piv else piv}, mirrored {mir}, errors {len(errs)}, "
              f"cull {cull['cull_m']} m [U]")
    D, Aw = np.concatenate(all_d), np.concatenate(all_a)
    o = np.argsort(D)
    cum = np.cumsum(Aw[o]) / Aw.sum()
    kit_median = float(D[o][np.searchsorted(cum, 0.5)])
    sharers = check_sharers(out_root)
    kit.update({
        "generator": GENERATOR, "args": list(args or []), "letter": LETTER, "A": A,
        "owner": OWNER, "margin_px": margin, "cage_extrusion_m": CAGE, "max_ray_m": MAX_RAY,
        "ao_samples": AO_SAMPLES, "ao_distance_m": AO_DISTANCE,
        "bake": rep,
        "kit_px_per_m_area_median": rep["layout"]["unique_px_per_m"]["median"],
        "all_instances_px_per_m_area_median": round(kit_median, 1),
        "DK2_sharers": sharers,
        "DK6_kit_median_ge_250": rep["layout"]["unique_px_per_m"]["median_w1"] >= DK6_MIN_PPM,
        "parity": par, "library_max_dev_m": lib.max_dev, "gear_items_placed": gear.count,
        "gear_item_tris": gear.tris, "seconds_to_bake": round(t_bake, 1),
        "vram_mb": {"raw_bc7_with_mips": round(4 * A * A * 4 / 3 / 2 ** 20, 2)},
    })
    if staging:
        kit["staging"] = {spec["obj"]: write_staging(spec, kit["props"][spec["name"]]["bounds_m"])
                          for spec in PROPS}
    if subobj:
        kit["subobj_bounds"] = update_subobj_bounds(records)
    if preview_dir:
        kit["previews"] = [os.path.relpath(p, REPO).replace("\\", "/")
                           for p in render_previews(objs, owner_dir, os.path.abspath(preview_dir))]
        kit["previews"].append(os.path.relpath(render_lineup(objs, owner_dir, os.path.abspath(preview_dir)),
                                               REPO).replace("\\", "/"))
    if review:
        import cs2_review
        kit["review_packs"] = {}
        for (name, obj), spec in zip(objs, PROPS):
            p = cs2_review.write_pack(name, review_tex_dir(name, owner_dir), {0: obj},
                                      meta={"generator": "dress_kit.py", "args": list(args or []),
                                            "kind": "prop", "caps": {0: spec["cap"]}, "letter": LETTER},
                                      manifest={})
            kit["review_packs"][name] = os.path.relpath(p, REPO).replace("\\", "/")
    for spec in PROPS:
        name = spec["name"]
        pr = kit["props"][name]
        data = {
            "generator": GENERATOR, "args": list(args or []), "letter": LETTER,
            "kind": f"prop (hosted sub-object, {spec['kind']}; no engine base rule)",
            "object_prefab": spec["obj"], "mesh_prefab": f"{name} Mesh",
            "kit_part": spec["part"], "variant": spec["variant"], "builder": spec["builder"],
            "design_rows": spec["rows"],
            "tris": {"LOD0": pr["tris"]}, "lod0_cap": spec["cap"], "tris_hi": pr["tris_hi"],
            "lods": "LOD0 only (41 / 50 vanilla props ship none); no LodProperties",
            "texture": ({"owner": True, "px": A, "maps": list(MAPS)} if name == OWNER else
                        {"owner": False, "shared_from": OWNER, "px": A,
                         "settings_sharedAssets": f"../{OWNER}/{OWNER}_<Map>.png"}),
            "px_per_m": pr["px_per_m"], "normal_detail": pr["normal_detail"],
            "normal_detail_benchmark": {"GardenBench01_hf_rms_deg": BENCH_HF_DEG,
                                        "DK3_min_deg": DK3_MIN_HF_DEG},
            "bounds_m": pr["bounds_m"], "cull": pr["cull"],
            "colour": colour_record(spec),
            "row_spec": row_spec(spec, pr, kit.get("staging", {}).get(spec["obj"])),
            "validation": pr["validation"], "stock_validator_on_sharer": pr["stock_validator_on_sharer"],
            "dk": pr["dk"], "kit": {k: kit[k] for k in ("A", "owner", "margin_px", "kit_px_per_m_area_median",
                                                        "DK6_kit_median_ge_250", "bake", "DK2_sharers")},
        }
        if name == OWNER:
            data["kit_report"] = {k: v for k, v in kit.items() if k != "props"}
        if kit.get("review_packs"):
            data["review_pack"] = kit["review_packs"].get(name)
        cs2_validate.write_manifest(os.path.join(out_root, name), name, data)
    print(f"[dress_kit] kit: {len(PROPS)} meshes, area-weighted median {kit_median:.0f} px/m, "
          f"sharers {'OK' if not sharers else sharers}, bake {t_bake:.0f} s")
    return kit


def row_spec(spec, pr, staging):
    """How a host writes the ObjectSubObjects row for this prop (host integration is a later step). A colored
    prop (kit color variants, 2026-10-12) is written by color-set VARIANT: its row names the variant."""
    if spec["colour"] == "variants":
        m_obj = ('per colour set: $fstrref:"CID:<the variant object-prefab CID>" (fine_detail.kit_row(..., '
                 'colour_set=<COLOUR_SETS key>) = fine_detail.kit_cid(prop, set); assets/staging/prefabs/props/B/'
                 'kit-variants.json); the base object prefab is retired')
        obj = f"{spec['obj']}_<Set> (colour-set variant, e.g. {spec['obj']}_Red)"
    else:
        m_obj = (f'$fstrref:"CID:{staging["cid"]}"' if staging else f'$fstrref:"CID:<{spec["obj"]}.Prefab.cid>"')
        obj = spec["obj"]
    rs = {"m_Object": m_obj,
          "object_prefab": obj, "m_GroupIndex": 0, "m_Probability": 100,
          "m_ParentMesh": "0 on a deck / wall / roof of the host mesh; -1 on the lot (snaps to terrain)",
          "rotation": "yaw quaternion turning the prop's +Z (front / out of the wall) to the host facing in "
                      "the hosts' row frame (fine_detail.yaw_quat((dx, dz)) / kit_row(facing=)); identity = "
                      "+Z to game +Z = authoring +X; leaning props may add a pitch about the prop's x axis "
                      "(the foot line) to change the lean",
          # fine-detail step 3a (2026-10-12): the frame the rotation is read in (step-2 report section 4)
          "frame": "hosts' row frame (fine_detail.X_CONVENTION, every building manifest): game "
                   "(gx, gy, gz) = (-y, z, x) of authoring, gx NEGATED (x-sign gate settled N, 2026-10-15 "
                   "playtest; rows are designed in the old +y frame and written through rule T, "
                   "fine_detail.design_row_to_game). IDENTITY rotation = the prop's +Z (front / out of the "
                   "wall) on game +Z = authoring +X; its long axis (prefab x) lies on authoring y, "
                   "mirrored by the game's x negation (fine-detail step 2 D11: the kit frame = the removed "
                   "rack vertex for vertex at identity IN THE DESIGN FRAME)"}
    k = spec["kind"]
    if k == "wall":
        rs["pivot"] = "wall plane (prop back at prefab z 0), bottom of the prop at y 0, centred on x"
        if spec["code"] == "HR":
            rs["from_builder"] = ("position = the old hose_reel mount point on the wall face with "
                                  f"y = old centre height - {HOSE['cz']:.2f}")
        else:
            rs["from_builder"] = ("position = the old extinguisher mount point on the wall face with y = "
                                  "the old bottom z (the body stands 0.02 off the wall as before)")
    elif k == "lean":
        rs["pivot"] = "foot line centre on the ground; the part leans back toward -Z"
        rs["rest_plane_prefab_z"] = pr["rest_plane_prefab_z"]
        rs["from_builder"] = ("position = the foot line; the support face (wall / bar) must lie at "
                              f"prefab z {pr['rest_plane_prefab_z']} +0..0.02 behind it (LEAN contract, "
                              "recipes/_lean.py)")
    else:
        rs["pivot"] = "foot centre on the ground"
        if spec["code"][0] == "R":
            rs["from_builder"] = ("A-frame: position = the old ski_rack_frame centre (x, y) on its surface "
                                  "(z0); long axis = prefab x = game x = authoring y: IDENTITY for a rack "
                                  "that ran along='Y' (the school), yaw +-90 (fine_detail.yaw_quat((+-1, 0))) "
                                  "for one that ran along='X'; front (+Z) = the builder's side +1; alternate "
                                  "SkiDressSkiRack / V2 / V3 along a row. (Corrected 2026-10-12, fine-detail "
                                  "step 3a: the earlier 'yaw 90 deg for along=Y hosts' assumed game x = "
                                  "authoring x, which no host uses.)")
        elif spec["code"][0] == "W":
            rs["from_builder"] = ("wall rack: position = centre of the old ski_rack run ((x0 + x1) / 2, y), "
                                  "items toward prefab +Z (the old `side`); 3.0 m long along prefab x: "
                                  "identity for a run along authoring Y facing +X, fine_detail.yaw_quat((dx, "
                                  "dz)) of the run's outward normal otherwise")
        else:
            rs["from_builder"] = "position = the old estop_post `at` point (x, y) on z0"
    return rs


# ======================================================================================
# Command line
# ======================================================================================

def parse_args(argv):
    ap = argparse.ArgumentParser(prog="dress_kit.py", description="dressing kit props, letter B")
    ap.add_argument("--out", default=None, help="letter folder, e.g. ../import/props/B")
    ap.add_argument("--export", action="store_true")
    ap.add_argument("--preview", default=None, help="preview folder, e.g. ../preview/props/B")
    ap.add_argument("--review", action="store_true", help="assets/preview/review/<Name>.blend")
    ap.add_argument("--no-staging", action="store_true", help="no prefab templates / cid minting")
    ap.add_argument("--no-subobj", action="store_true", help="leave subobj_bounds.json alone")
    ap.add_argument("--margin", type=int, default=MARGIN_PX)
    ap.add_argument("--json", default=None, help="write the kit report here")
    ap.add_argument("--settings-proof", action="store_true")
    ap.add_argument("--variants", action="store_true",
                    help="kit colour variants: stage every (prop, set) a shipped host manifest names, archive the "
                         "unused ones and the six Parent base prefabs, rewrite kit-variants.json, patch the kit "
                         "manifests' colour / row_spec (no geometry, no texture)")
    ap.add_argument("--variant-surgery", default=None, metavar="IMPORTED_DATA",
                    help="S1 (game closed): write every staged variant's object + mesh prefab (+ .cid) under this "
                         "ImportedData, the mesh prefab cloned from the imported base mesh prefab")
    return ap.parse_args(argv)


def main(argv=None):
    if argv is None:
        argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    args = parse_args(argv)
    if args.settings_proof:
        res = settings_proof()
        if not all(r.get("identical", True) for r in res):
            sys.exit(1)
        return
    if args.variants:
        res = sync_variants()
        if args.json:
            with open(args.json, "w", encoding="utf-8") as fh:
                json.dump(res, fh, indent=1, default=str)
        return
    if args.variant_surgery:
        variant_surgery(os.path.abspath(args.variant_surgery))
        return
    clear_scene()
    if not args.export:
        groups, offsets, _lib, _gear, stats = assemble()
        for spec, _p in groups:
            s = stats[spec["code"]]
            print(f"[dress_kit] {spec['name']}: low {s['tris_low']} (cap {spec['cap']}), hi {s['tris_hi']}")
        return
    if not args.out:
        raise SystemExit("--export needs --out")
    kit = export(args.out, preview_dir=args.preview, review=args.review, staging=not args.no_staging,
                 subobj=not args.no_subobj, args=argv, margin=args.margin)
    if args.json:
        with open(args.json, "w", encoding="utf-8") as fh:
            json.dump(kit, fh, indent=1, default=str)
    bad = [n for n, p in kit["props"].items() if p["validation"]["errors"] or not all(p["dk"].values())]
    if bad or kit["DK2_sharers"] or not kit["DK6_kit_median_ge_250"]:
        print(f"[dress_kit] CONTRACT FAILURES: {bad} sharers {kit['DK2_sharers']} "
              f"kit median {kit['kit_px_per_m_area_median']}")
        sys.exit(1)


if __name__ == "__main__":
    main()
