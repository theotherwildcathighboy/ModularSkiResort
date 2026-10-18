"""
cs2_sheaves - the towers' sheave-train builder, shared by the towers and the lift stations.

Extracted 2026-10-13 from lift_tower.py (Richard, twenty-fifth look S-2: "Let's all apply the same PULLEY
WHEELS / ROLLERS we use on the lift tower onto the lift stations"; build report
docs/research/art/assets-2026-10/build/nitpick-a-stations.md). The code below is lift_tower.py's, moved
VERBATIM (expression for expression, the same part order and names): lift_tower imports every function
back and passes its OWN module globals into the train builders at call time, so every tower regenerates
byte-identical and the tests that patch lift_tower constants (DQ_SUB_Y, DQ_LINK, GD_CAR_DY, DP1_INSET ...)
still steer the tower builds. The constants here are the towers' numbers: lift_tower aliases them (one
source), the stations read them as the defaults.

Parts:
  lite_sheave / lite_sheave_parts  the D4 "lite" sheave: an axis-closed V-groove (or flat-rim) wheel about a
                                   Y axle, 6 x segs tris; the _parts form splits the side discs ("galv") from
                                   the rope liner (LINER_KEY "rubber") - the detail-pass liners
  fg_train_side                    the Yan (FG / ET) rocker tree of ONE lane: hanger -> main rocker -> pivots
                                   -> sub rockers -> sheave arms -> sheaves + liners + axle caps, hanging
                                   below its carrier (the crossarm on a tower)
  dq_train                         the gantry (DQ / ETDQ / GD / ETGD) train of both lanes: sheaves + liners,
                                   sub rockers + pins, main rockers + pins, the yoke, links for a lone middle
                                   sub, the hanger to the girder - every carrier INBOARD of the sheaves
  dq_centres                       the gantry train's sheave centres (sag parabola)
  bevel_parts / chamfer_long_edges the tower finish of these parts (BEVEL_W bevel by prefix table; the FG
                                   1-segment long-edge chamfer of rocker_sub_ / sheave_arm_)
  TRAIN_BEVEL / TRAIN_CHAMFER      the towers' bevel / chamfer tables restricted to the train's parts

Every builder takes `prefix` (default ""): the stations put their trains in their own name space
("sheave_guide_", "sheave_hold_" ...) so their LOD / C8 / density tables apply; the towers pass "".
Materials returned are the TOWER keys ("paint" = the tower's slot-1 steel, "galv", LINER_KEY): a station
maps them onto its own table (lift_terminal_detach: paint -> darksteel; lift_terminal M: paint -> paint1).
"""

import math

import bmesh
import bpy
import mathutils

import cs2_asset
import cs2_kit as K

# -- the towers' train numbers (lift_tower aliases these; one source) ---------------------------------
BEVEL_W = 0.02
LINER_KEY = "rubber"
DP1_CHAMFER = 0.012                    # FG / ET rocker_sub_ + sheave_arm_ long edges
# FG (legacy head numbers, verbatim: lift_tower.py TowerParams / build_sheave_train)
FG_ARM_T = 0.22                        # crossarm depth (the hanger reaches into it)
FG_HANGER, FG_MAIN_TO_SUB, FG_SUB_TO_AXLE = 0.40, 0.30, 0.26     # axle = mast - 0.96
FG_HANGER_INSET = 0.005                # M4: hanger top 5 mm inside the crossarm (DP1_INSET fg_hanger_top)
FG_SHEAVE = dict(r=0.20, w=0.13, pitch=0.52)                     # lift_tower._fg_spec
# DQ gantry train (towers-v2 section 4.2, F9, Revision 1c)
DQ_SUB_Y = 2.512                       # F9: 2.517 moved 5 mm inboard (outer face 2.545)
DQ_MAIN_Y = 2.44
DQ_LINK = dict(w=0.15, t=0.072, below=0.10, bury=0.02)   # None = the pre-Revision-1c build (test)
DQ_HANGER = dict(y0=2.30, y1=2.41, z0=0.25, z1=0.42, x=0.15)
DQ_HANGER_Z_INSET = 0.005              # M4: top / bottom 5 mm inside the yoke's faces (DP1_INSET dq_hanger_z)
DQ_SHEAVE = dict(r=0.223, w=0.10, pitch=0.59)                    # lift_tower._dq_spec
DQ_PIN_S = 0.17                        # sub-rocker pivot pin length (head_kit pin_s, DQ)
# GD: the gondola head (lift_tower GD_* constants; head_kit for family "GD")
GD_SHEAVE = dict(r=0.25, w=0.10, pitch=0.62, segs=12, groove=0.0)
GD_CAR_DY = 0.39
GD_PIN_S = 0.14
# the towers' finish of the train parts (lift_tower FG_BEVEL / DQ_BEVEL / detail_post, train keys only)
TRAIN_BEVEL = {"FG": {"hanger_": 1, "rocker_main_": 2}, "DQ": {"rocker_main_": 1, "rocker_link_": 1}}
TRAIN_CHAMFER = {"FG": (("rocker_sub_", (1.0, 0.0, 0.0)), ("sheave_arm_", (0.0, 0.0, 1.0))), "DQ": ()}

# -- T62 (2026-10-18): ONE sheave WHEEL on every tower and station ----------------------------------------
# Richard 2026-10-17: "the gondola station bottom used the simpler roller wheel; let's be consistent. The ones on
# CS2R_SkiLiftTowerDQ20O_LOD0 seem to look the best" + "it's more just the pulley wheels themselves should be
# consistent on every tower. Different pulley housings make sense." Build note:
# docs/research/field/2026-10-15/build/T62-sheave-wheels.md.
# The DQ20O wheel (census, section 1 there) = lite_sheave_parts with the default V-groove (depth max(0.02, 0.18 r),
# a 5-point axis-closed profile), 12 segments, side discs (galv) closed by a centre vertex, the groove flanks as a rubber
# liner, NO axle cap, no hub bolts (its hub is the towers' normal-map ring, lift_tower.detail_marks). Round 1 built
# exactly that (round 2 below replaces it); a family keeps its radius / width (WHEEL_FAMILY). WHEELS "v2" (default) routes every tower head, every
# station train and the tram haul rollers through `wheel`; "v1" rebuilds the pre-T62 wheels byte for byte (FG axle
# caps, the GD / lattice flat 12-gon rims, the tram's 8-gon sheave_lite rollers). Set with set_wheels(); the
# generators' CLI is --wheels v1|v2.
WHEELS = "v2"
WHEEL_ID = "cs2_sheaves.wheel"         # the stamp every wheel part carries (obj["cs2_wheel"]); tests/test_sheaves.py
# -- T62 round 2 (2026-10-18). Richard on round 1: "VERY confused about the T62 wheels. They all look different still and
# of varying quality. FG tower ones look probably best, maybe a bit shiny, but still a tiny bit blocky. They'd be
# slightly smaller than a bicycle or car wheel... So, probably raise the cap." He judged the FG wheel WITH its raised
# axle cap the best. Decisions: every wheel = that FG wheel made round; raise the caps. Build note section "Round 2" in
# docs/research/field/2026-10-15/build/T62-sheave-wheels.md; vanilla numbers docs/research/art/vanilla-wheels-2026-10-18.md
# (car / bus tires 16-gon, tread smooth-radial, caps flat with a HARD split, LOD1 coarser, LOD2 no wheels).
# The v2 wheel (one shape, scaled by the family radius; width per family), all numbers x r unless noted:
#   segments 16 (vanilla car tire density; a multiple of 4 keeps a vertex on the rim top / bottom: ST1, C4);
#   V groove root r - max(0.02, 0.18 r) at mid-width (the DQ20O groove); crest rings at radius r, |y| = w/2 - chamfer;
#   rim chamfer 0.05 r at 45 deg on both corners (crest -> disc edge (r - c, +-w/2));
#   flat n-gon side discs at +-w/2; a raised hub boss on BOTH faces: a 12-gon frustum base 0.45 r -> top 0.35 r,
#   height 0.10 r (FG: 0.09 -> 0.07, 0.02 = the T27 kit axle cap Richard picked, made round) + a flat n-gon top;
#   every ring loop (disc edge, crest, groove root, boss base, boss top) is marked SHARP and every edge between two
#   segments stays smooth (shade_by_angle 66 keeps sharp marks): the treads (chamfer, flanks, boss side) shade smooth
#   around and flat across, the caps stay flat - vanilla's tire split (the round-1 disc/flank edge was smoothed: rim
#   normals tilted 31 deg, the disc read as a dome; vanilla-wheels section 1.6).
#   Tris: liner 4 x 16 + chamfers 4 x 16 + discs 2 x 14 + boss sides 2 x 24 + boss tops 2 x 10 = 224 (WHEEL_TRIS).
# Material: every wheel's discs / chamfers / bosses take WHEEL_KEY (WHEEL_MATERIAL, one spec for every generator) instead
# of the generator's "galv" (Metal032 at metal 1.0: the dark sky-tinted mirror Richard called shiny; the snow-props
# galvanised lesson, snow-props-weather-2026-10-17 section 2: light albedo, smoothness inside vanilla's band, metal low
# for paint); liners stay rubber.
# LODs (WHEEL_LOD, one rule for every family): a wheel is never collapse-decimated. LOD1 = 8 segments, a FLAT band on
# the crest rings + two n-gon discs, no chamfer / boss (28 tris; vanilla LOD1 keeps a coarser wheel); LOD2 = no wheel
# (vanilla car LOD2). Which assets still draw wheels at LOD1 is each generator's LOD table (unchanged: the towers and
# the FG M counterweight sheaves; the lattice / tram rollers / station trains leave at LOD1 with their trains).
# Implemented as the make_lod_chain `fixed` hook (lod_fixed / LodWheels below).
# -- T62 round 3 (2026-10-18). Richard on round 2's 224 tris: "Ouch, 224 tris sounds like a lot compared to vanilla
# wheels, which is our closest analogy... We need to reduce the tris on the wheels." Budget <= 120 tris per wheel at LOD0
# (WHEEL_TRIS_BUDGET); keep, in order, the V groove, 16 segments, a hub boss. Round 3 cut (build note "Round 3"):
#   * the 45 deg rim chamfer is gone (the sharp tread / cap split is the hard edge): the discs sit on the crest rings
#     again (r, +-w/2), so every rope seat is round 1's (FG 0.2783 / DQ 0.3096 / GD 0.3366);
#   * the groove ROOT ring has 8 vertices under the 16-vertex crests (3 tris per root segment and flank, 48 for the
#     liner instead of 64): the rim silhouette and the rope-tangent vertices (every 4th crest vertex sits over a root
#     vertex: the rim top / bottom included) keep 16 segments; the root is inside the groove;
#   * the hub boss is an 8-gon frustum (0.45 r -> 0.35 r, 0.10 r proud) with a flat 8-gon top: 22 tris a face (a cone
#     read flat at 1 m in the round-3 prototype, scratch t62r3/proto);
#   * a boss is built only on a face that is SEEN: cull_hidden_bosses measures every boss after the asset is built (rays
#     from the boss top along the axle: a face whose rays hit another part within BOSS_HIDDEN["dist"] on
#     >= BOSS_HIDDEN["share"] of the samples is behind a housing / rocker) and deletes the hidden ones.
#   Tris: liner 48 + discs 2 x 14 = 76 (WHEEL_TRIS_BASE) + 22 per seen boss (WHEEL_BOSS_TRIS): 76 / 98 / 120.
WHEEL_SEGS = 16
WHEEL_PROFILE = dict(groove=0.18, groove_min=0.02, chamfer=0.0, boss_r=0.45, boss_top=0.35, boss_h=0.10,
                     boss_segs=8, root_segs=8)
WHEEL_TRIS_BASE = 2 * 3 * WHEEL_PROFILE["root_segs"] + 2 * (WHEEL_SEGS - 2)                  # 76
WHEEL_BOSS_TRIS = 2 * WHEEL_PROFILE["boss_segs"] + (WHEEL_PROFILE["boss_segs"] - 2)         # 22
WHEEL_TRIS = WHEEL_TRIS_BASE + 2 * WHEEL_BOSS_TRIS                                           # 120 (both bosses)
WHEEL_TRIS_BUDGET = 120                # Richard 2026-10-18 (round 3): <= 120 per wheel at LOD0; tests/test_sheaves
BOSS_HIDDEN = dict(dist=0.10, share=0.75)
WHEEL_LOD = {1: dict(segs=8, form="flat band on the crest rings + 2 n-gon discs (no boss)",
                     tris=2 * 8 + 2 * (8 - 2)),                                          # 28
             2: dict(segs=0, form="no wheel", tris=0)}
WHEEL_KEY = "wheelsteel"
# satin painted / galvanised steel, plain colour (no photo set: the generators' Metal032 roots differ, prefiltered or not,
# and a wheel must read the same on every asset): sRGB ~0.58 neutral grey (linear 0.29; vanilla galvanised luma 0.51-0.84,
# painted utility 0.18-0.73), metal 0.30, smoothness 0.40 (inside vanilla's painted band, under its 0.47-0.62 bare-metal one)
WHEEL_MATERIAL = dict(albedo=(0.285, 0.290, 0.297), metal=0.30, rough=0.60, projection="box")
# Per family (LOD0): r / w kept from the family's v1 wheel; every family the same 16-segment wheel (segs / ngon kept as
# keys for the callers and the gate; the discs are always n-gons).
WHEEL_FAMILY = {
    "FG": dict(r=FG_SHEAVE["r"], w=FG_SHEAVE["w"], segs=WHEEL_SEGS, ngon=True),    # FG8-20, ET62 / ET68, FG M hold-down
    "DQ": dict(r=DQ_SHEAVE["r"], w=DQ_SHEAVE["w"], segs=WHEEL_SEGS, ngon=True),    # DQ10-20, ETDQ, DQ top nose
    "GD": dict(r=GD_SHEAVE["r"], w=GD_SHEAVE["w"], segs=WHEEL_SEGS, ngon=True),    # GD12-20, ETGD, lattice, gondola top
    "TRAM": dict(r=0.16, w=0.08, segs=WHEEL_SEGS, ngon=True),                      # tram_tower.ROLLER haul rollers
    "CWHEAD": dict(r=0.35, w=0.10, segs=WHEEL_SEGS, ngon=True),                    # FG M counterweight head sheave
    "CWDEFL": dict(r=0.20, w=0.10, segs=WHEEL_SEGS, ngon=True),                    # FG M counterweight deflection sheave
}
WHEEL_LOG = []                         # [(discs name, liner name, r, w, segs, ngon)] built this process (tests, LOD hook)


def set_wheels(mode):
    """Select the wheel generation for this process ("v1" | "v2"); returns the previous mode."""
    global WHEELS
    if mode not in ("v1", "v2"):
        raise ValueError(f"wheels {mode!r}: want v1 | v2")
    old, WHEELS = WHEELS, mode
    return old


def wheels_v2():
    return WHEELS == "v2"


def wheel_spec(family):
    """The T62 wheel of `family` (WHEEL_FAMILY row, a copy): r, w, segs, ngon."""
    return dict(WHEEL_FAMILY[family])


def wheel_profile(r, w):
    """The v2 wheel's profile numbers (metres) for radius r, width w: groove root depth gd, rim chamfer c (0 since round
    3), half width hw, crest |y| hc = hw - c, boss base / top radius rb / rt, boss height h, boss segments bs, groove
    root segments rs."""
    P = WHEEL_PROFILE
    hw = w / 2.0
    c = P["chamfer"] * r
    return dict(r=r, w=w, hw=hw, gd=max(P["groove_min"], P["groove"] * r), c=c, hc=hw - c,
                rb=P["boss_r"] * r, rt=P["boss_top"] * r, h=P["boss_h"] * r, bs=P["boss_segs"], rs=P["root_segs"])


def wheel_tris(n_bosses):
    """LOD0 tris of one v2 wheel with `n_bosses` (0..2) seen hub bosses."""
    return WHEEL_TRIS_BASE + n_bosses * WHEEL_BOSS_TRIS


def wheel_materials(factory=dict):
    """{WHEEL_KEY: factory(**WHEEL_MATERIAL)} under v2, {} under v1 (a v1 material table stays byte-identical).
    `factory` = the generator's MaterialSpec class (or dict for lift_tower's dict table)."""
    return {WHEEL_KEY: factory(**WHEEL_MATERIAL)} if wheels_v2() else {}


def wheel_roles(role="none"):
    """{WHEEL_KEY: role} for a weather role table under v2 ({} under v1): the wheel is clean steel on every asset (the
    families' weather intensities differ; one wheel must look the same everywhere)."""
    return {WHEEL_KEY: role} if wheels_v2() else {}


def wheel_key(v1_key="galv"):
    """The material key a wheel's discs take: WHEEL_KEY (v2) or the generator's own v1 key."""
    return WHEEL_KEY if wheels_v2() else v1_key


# --------------------------------------------------------------------------------------
# Part helpers (bmesh-built, deterministic; every part carries exactly one "UVMap")
# --------------------------------------------------------------------------------------

def _box_uv_bm(bm, layer):
    """Per-face dominant-axis projection in metres with u x v = +normal axis (never mirrored)."""
    bm.normal_update()
    for f in bm.faces:
        n = f.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        s = 1.0 if n[ax] >= 0.0 else -1.0
        for lp in f.loops:
            c = lp.vert.co
            if ax == 0:
                uv = (s * c.y, c.z)
            elif ax == 1:
                uv = (-s * c.x, c.z)
            else:
                uv = (s * c.x, c.y)
            lp[layer].uv = uv


def _new_part(name, bm, relief="geo"):
    """bmesh (world coordinates) -> linked mesh object with identity transform."""
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.update()
    obj = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(obj)
    obj["relief"] = relief
    return obj


def lite_sheave(name, r, w, loc, segs=12, groove=None):
    """
    The towers-v2 D4 "lite" sheave, generator-local (cs2_kit.sheave is untouched so the
    stations stay byte-stable): an axis-closed 5-point V-groove profile (axle -> flange ->
    groove root -> flange -> axle) around a Y axle = 6 * segs tris (72 at 12). Built
    vertex by vertex, then canonical_order_bmesh (skeptic: determinism) and an unmirrored
    box-projected UVMap. Name used verbatim (pass "sheave_..._rim").
    """
    gd = max(0.02, 0.18 * r) if groove is None else groove
    hw = w / 2.0
    cx, cy, cz = loc
    bm = bmesh.new()
    layer = bm.loops.layers.uv.new("UVMap")
    ang = [2.0 * math.pi * k / segs for k in range(segs)]
    if gd <= 0.0:
        # Revision 1b (GD): FLAT rim = tram_tower.sheave_lite(groove=0): one band of `segs` quads
        # + two n-gon side discs = 2 segs + 2 (segs - 2) tris (44 at 12). Windings explicit:
        # band (L k, R k, R k+1, L k+1) faces out, the -Y disc in ring order faces -Y.
        L = [bm.verts.new((cx + r * math.cos(a), cy - hw, cz + r * math.sin(a))) for a in ang]
        R = [bm.verts.new((cx + r * math.cos(a), cy + hw, cz + r * math.sin(a))) for a in ang]
        for k in range(segs):
            k1 = (k + 1) % segs
            bm.faces.new((L[k], R[k], R[k1], L[k1]))
        bm.faces.new(L)
        bm.faces.new(list(reversed(R)))
        cs2_asset.canonical_order_bmesh(bm)
        _box_uv_bm(bm, layer)
        return _new_part(name, bm)
    ca = bm.verts.new((cx, cy - hw, cz))
    cb = bm.verts.new((cx, cy + hw, cz))
    rings = [[bm.verts.new((cx + rad * math.cos(a), cy + yy, cz + rad * math.sin(a)))
              for a in ang] for rad, yy in ((r, -hw), (r - gd, 0.0), (r, hw))]
    for k in range(segs):
        k1 = (k + 1) % segs
        bm.faces.new((ca, rings[0][k], rings[0][k1]))                         # -Y disc
        bm.faces.new((rings[0][k], rings[1][k], rings[1][k1], rings[0][k1]))  # groove flank
        bm.faces.new((rings[1][k], rings[2][k], rings[2][k1], rings[1][k1]))  # groove flank
        bm.faces.new((cb, rings[2][k1], rings[2][k]))                         # +Y disc
    cs2_asset.canonical_order_bmesh(bm)
    _box_uv_bm(bm, layer)
    return _new_part(name, bm)


def lite_sheave_parts(name, liner, r, w, loc, segs=12, groove=None):
    """
    lite_sheave split in two parts (design 3.1 `liner_`): `name` keeps the two side discs (the
    C1 / C2 / C9 measurements read its bbox centre: unchanged), `liner` the rope faces - the
    V-groove flanks (4 x segs tris) or the flat band (2 x segs) - in material rubber. Same
    vertices, windings and tri count as lite_sheave; remove_doubles welds the two at the join.
    Returns [(discs, "galv"), (liner, LINER_KEY)].
    """
    gd = max(0.02, 0.18 * r) if groove is None else groove
    hw = w / 2.0
    cx, cy, cz = loc
    ang = [2.0 * math.pi * k / segs for k in range(segs)]

    def ring(bm, rad, yy):
        return [bm.verts.new((cx + rad * math.cos(a), cy + yy, cz + rad * math.sin(a))) for a in ang]

    bm = bmesh.new()
    layer = bm.loops.layers.uv.new("UVMap")
    if gd <= 0.0:
        L, R = ring(bm, r, -hw), ring(bm, r, hw)
        bm.faces.new(L)
        bm.faces.new(list(reversed(R)))
    else:
        ca = bm.verts.new((cx, cy - hw, cz))
        cb = bm.verts.new((cx, cy + hw, cz))
        r0, r2 = ring(bm, r, -hw), ring(bm, r, hw)
        for k in range(segs):
            k1 = (k + 1) % segs
            bm.faces.new((ca, r0[k], r0[k1]))
            bm.faces.new((cb, r2[k1], r2[k]))
    cs2_asset.canonical_order_bmesh(bm)
    _box_uv_bm(bm, layer)
    discs = _new_part(name, bm)
    bm = bmesh.new()
    layer = bm.loops.layers.uv.new("UVMap")
    if gd <= 0.0:
        L, R = ring(bm, r, -hw), ring(bm, r, hw)
        for k in range(segs):
            k1 = (k + 1) % segs
            bm.faces.new((L[k], R[k], R[k1], L[k1]))
    else:
        rg = [ring(bm, r, -hw), ring(bm, r - gd, 0.0), ring(bm, r, hw)]
        for k in range(segs):
            k1 = (k + 1) % segs
            bm.faces.new((rg[0][k], rg[1][k], rg[1][k1], rg[0][k1]))
            bm.faces.new((rg[1][k], rg[2][k], rg[2][k1], rg[1][k1]))
    cs2_asset.canonical_order_bmesh(bm)
    _box_uv_bm(bm, layer)
    lin = _new_part(liner, bm)
    lin["dp_root_r"] = float(r - max(gd, 0.0))
    return [(discs, "galv"), (lin, LINER_KEY)]


def wheel(name, liner, r, w, loc, segs=None, ngon=None, uv="developed"):
    """
    T62 (round 3): THE sheave wheel (every tower head, every station train, the counterweight sheaves, the tram haul
    rollers): the WHEEL_PROFILE wheel about a Y axle through `loc`, split into `name` (the two n-gon side discs + both
    hub bosses, material WHEEL_KEY) and `liner` (the V-groove flanks, 16 crest vertices a side over an 8-vertex root,
    LINER_KEY rubber, prop dp_root_r = the groove root radius). Both bosses are built here; the generators drop the
    hidden ones after the asset is assembled (cull_hidden_bosses). `segs` / `ngon` are accepted for the callers' family
    rows and must be WHEEL_SEGS / True (one wheel everywhere; anything else raises). uv "developed" (the default since
    round 3: the groove triangles and the boss frustum box-project at up to 1.41 anisotropy, lift_tower DT-C5) = an
    isometric map of every face (the groove
    triangles unfolded as one strip per flank, the boss frustum developed like tram_tower.sheave_lite's flanks, planar
    discs / boss tops; islands 2 units apart, / circumference), kept by every generator (lift_tower's uv_rule leaves v2
    wheel parts alone); "box" = the per-face box projection (tests). Ring loops are marked sharp (sharp_edge survives bmesh, join and remove_doubles; shade_by_angle keeps it).
    Every part is stamped obj["cs2_wheel"] = WHEEL_ID, cs2_wheel_r / _w / _segs, the discs cs2_wheel_bosses "-+", and
    logged in WHEEL_LOG. Returns [(discs, WHEEL_KEY), (liner, LINER_KEY)].
    """
    if segs is not None and int(segs) != WHEEL_SEGS:
        raise ValueError(f"wheel {name}: segs {segs} - every v2 wheel has {WHEEL_SEGS} segments (one wheel)")
    if ngon is not None and not ngon:
        raise ValueError(f"wheel {name}: v2 discs are n-gons (ngon False asked)")
    if uv not in ("box", "developed"):
        raise ValueError(f"wheel {name}: uv {uv!r}")
    parts = _wheel_parts(name, liner, r, w, loc, WHEEL_SEGS, uv)
    for o, _k in parts:
        o["cs2_wheel"] = WHEEL_ID
        o["cs2_wheel_r"] = float(r)
        o["cs2_wheel_w"] = float(w)
        o["cs2_wheel_segs"] = int(WHEEL_SEGS)
    parts[0][0]["cs2_wheel_bosses"] = "-+"
    WHEEL_LOG.append((parts[0][0].name, parts[1][0].name, float(r), float(w), int(WHEEL_SEGS), True))
    return parts


def _develop_band(faces, ring_of, rad_y, segs, circ, off, layer):
    """Isometric UVs of a cone-frustum band, written into `layer`: `faces` [(face, k)] between ring "A" and ring "B"
    (ring_of[vert] = ("A"|"B", k)), rad_y = {"A": (radius, y), "B": (radius, y)}. The polygonal frustum is developed
    exactly (corners at apex distance D and D rB / rA, alpha per segment: tram_tower.sheave_lite, DT-C5), in units of
    the circumference, shifted by `off`; then un-mirrored (_unmirror)."""
    (rA, yA), (rB, yB) = rad_y["A"], rad_y["B"]
    side = math.hypot(rA - rB, yA - yB)
    D = side * rA / abs(rA - rB)
    alpha = 2.0 * math.asin(rA * math.sin(math.pi / segs) / D)
    if rA < rB:
        alpha = -alpha
    for f, k in faces:
        for lp in f.loops:
            which, kv = ring_of[lp.vert]
            j = k if kv == k else k + 1          # the far corners (k + 1, wrapping to 0) unwrap to j = k + 1: one open strip
            dist = D if which == "A" else D * rB / rA
            lp[layer].uv = (dist * math.cos(j * alpha) / circ + off[0], dist * math.sin(j * alpha) / circ + off[1])
    _unmirror([f for f, _k in faces], layer, off[0])


def _uv_area(f, layer):
    uv = [lp[layer].uv for lp in f.loops]
    return 0.5 * sum(uv[i].x * uv[i - 1].y - uv[i - 1].x * uv[i].y for i in range(len(uv))) * -1.0


def _unmirror(faces, layer, u0):
    """One island = one handedness: if its UVs run clockwise against the face winding (a mirrored tangent frame),
    reflect u about u0 (the island's column). Decided on the summed signed area, applied to the whole island."""
    if sum(_uv_area(f, layer) for f in faces) < 0.0:
        for f in faces:
            for lp in f.loops:
                lp[layer].uv = (2.0 * u0 - lp[layer].uv.x, lp[layer].uv.y)


def _unfold_strip(tris, layer, circ, off):
    """Isometric UVs of a triangle strip (each triangle shares an edge with the one before): the first laid flat, each
    next one's free vertex placed by its two edge lengths on the far side of the shared edge (exact for a developable
    strip; a V flank's strip spans 2 pi gd / side < 2 pi, so it never overlaps itself), / circ + off."""
    def put(f, xy):
        for lp in f.loops:
            x, y = xy[lp.vert]
            lp[layer].uv = (x / circ + off[0], y / circ + off[1])
    a, b, c = list(tris[0].verts)
    ab = (b.co - a.co).length
    ca, cb = (c.co - a.co).length, (c.co - b.co).length
    cx = (ca * ca - cb * cb + ab * ab) / (2.0 * ab)
    prev = {a: (0.0, 0.0), b: (ab, 0.0), c: (cx, math.sqrt(max(ca * ca - cx * cx, 0.0)))}
    put(tris[0], prev)
    for f in tris[1:]:
        vs = list(f.verts)
        shared = [v for v in vs if v in prev]
        if len(shared) != 2:
            raise RuntimeError("unfold: the strip broke (no shared edge)")
        p, q = shared
        n = [v for v in vs if v not in shared][0]
        o = [v for v in prev if v not in shared][0]
        P, Q, O = mathutils.Vector(prev[p]), mathutils.Vector(prev[q]), mathutils.Vector(prev[o])
        dpq = (Q - P).length
        dp, dq = (n.co - p.co).length, (n.co - q.co).length
        t = (dp * dp - dq * dq + dpq * dpq) / (2.0 * dpq)
        hgt = math.sqrt(max(dp * dp - t * t, 0.0))
        u = (Q - P) / dpq
        nrm = mathutils.Vector((-u.y, u.x))
        if (O - P).dot(nrm) > 0.0:                       # the new vertex goes on the other side of the shared edge
            nrm = -nrm
        N = P + u * t + nrm * hgt
        cur = {p: prev[p], q: prev[q], n: (N.x, N.y)}
        put(f, cur)
        prev = cur


def _wheel_parts(name, liner, r, w, loc, segs, uv):
    """wheel()'s geometry: (discs + bosses, liner) bmesh parts, sharp ring loops, outward normals."""
    p = wheel_profile(r, w)
    hw, hc, gd, rb, rt, h, bs, rs = p["hw"], p["hc"], p["gd"], p["rb"], p["rt"], p["h"], p["bs"], p["rs"]
    if p["c"] != 0.0 or segs != 2 * rs:
        raise ValueError(f"wheel {name}: the round-3 profile wants no chamfer and a root ring of segs / 2")
    cx, cy, cz = loc
    C = mathutils.Vector(loc)
    circ = 2.0 * math.pi * r

    def ring(bm, rad, yy, n):
        return [bm.verts.new((cx + rad * math.cos(2.0 * math.pi * k / n), cy + yy,
                              cz + rad * math.sin(2.0 * math.pi * k / n))) for k in range(n)]

    def quads(bm, A, B, n):
        return [(bm.faces.new((A[k], B[k], B[(k + 1) % n], A[(k + 1) % n])), k) for k in range(n)]

    def orient(bm):
        bm.normal_update()
        for f in bm.faces:
            if f.normal.dot(f.calc_center_median() - C) < 0.0:
                f.normal_flip()
        bm.normal_update()

    def sharp_rings(bm, rings):
        for R in rings:
            n = len(R)
            for k in range(n):
                e = bm.edges.get((R[k], R[(k + 1) % n]))
                if e is not None:
                    e.smooth = False

    # ---- discs part: two n-gon side discs on the crest rings + a hub boss on each face (8-gon frustum, flat top)
    bm = bmesh.new()
    layer = bm.loops.layers.uv.new("UVMap")
    ngons, bands, rings = [], [], []
    for s in (-1.0, 1.0):
        row = 0.0 if s < 0 else 2.0
        K = ring(bm, r, s * hc, segs)                    # crest (shared with the liner after the weld)
        Bb = ring(bm, rb, s * hw, bs)                    # boss base (on the disc plane, a separate shell)
        Bt = ring(bm, rt, s * (hw + h), bs)              # boss top
        ngons += [(bm.faces.new(K), s, 0.0, row), (bm.faces.new(Bt), s, 2.0, row)]
        bands.append((quads(bm, Bb, Bt, bs), Bb, Bt, (rb, s * hw), (rt, s * (hw + h)), bs, 4.0, row))
        rings += [K, Bb, Bt]
    orient(bm)
    sharp_rings(bm, rings)
    if uv == "developed":
        for f, s, u0, v0 in ngons:                       # planar: (x, z) on the -Y face, (z, x) on +Y (u x v = normal)
            for lp in f.loops:
                d = lp.vert.co - C
                lp[layer].uv = ((d.x if s < 0 else d.z) / circ + u0, (d.z if s < 0 else d.x) / circ + v0)
            _unmirror([f], layer, u0)
        for fq, A, B, ra, rbb, n, u0, v0 in bands:
            ring_of = {v: ("A", k) for k, v in enumerate(A)}
            ring_of.update({v: ("B", k) for k, v in enumerate(B)})
            for q in range(0, len(fq), 2):          # the boss frustum in quarters (2 quads each): compact islands
                _develop_band(fq[q:q + 2], ring_of, {"A": ra, "B": rbb}, n, circ, (u0 + 2.0 * (q // 2), v0 + 8.0), layer)
        cs2_asset.canonical_order_bmesh(bm)
    else:
        cs2_asset.canonical_order_bmesh(bm)
        _box_uv_bm(bm, layer)
    discs = _new_part(name, bm)

    # ---- liner part: the two V flanks, 16 crest vertices a side over the 8-vertex root (3 triangles per root segment)
    bm = bmesh.new()
    layer = bm.loops.layers.uv.new("UVMap")
    K0, G, K1 = ring(bm, r, -hc, segs), ring(bm, r - gd, 0.0, rs), ring(bm, r, hc, segs)
    strips = []
    for K in (K0, K1):
        st = []
        for j in range(rs):
            c0, c1, c2 = K[2 * j], K[2 * j + 1], K[(2 * j + 2) % segs]
            g0, g1 = G[j], G[(j + 1) % rs]
            st += [bm.faces.new((c0, c1, g0)), bm.faces.new((c1, g1, g0)), bm.faces.new((c1, c2, g1))]
        strips.append(st)
    orient(bm)
    sharp_rings(bm, (K0, G, K1))
    if uv == "developed":
        for band, st in enumerate(strips):
            # one island per 2 root segments (6 triangles, 45 deg of arc): a whole flank unfolds to a 200+ deg annular
            # sector that packs badly (FG8 lost 9 % of its median px/m to it); the short pieces pack like quads
            for q in range(0, len(st), 6):
                _unfold_strip(st[q:q + 6], layer, circ, (2.0 * (q // 6), 2.0 * band))
                _unmirror(st[q:q + 6], layer, 2.0 * (q // 6))
        cs2_asset.canonical_order_bmesh(bm)
    else:
        cs2_asset.canonical_order_bmesh(bm)
        _box_uv_bm(bm, layer)
    lin = _new_part(liner, bm)
    lin["dp_root_r"] = float(r - gd)
    return [(discs, WHEEL_KEY), (lin, LINER_KEY)]


def _world_co(o):
    M = o.matrix_world
    return [M @ v.co for v in o.data.vertices]


def boss_visibility(parts):
    """
    T62 round 3: how hidden each hub boss of the v2 wheels in `parts` is - {discs name: {-1: share, +1: share}}, the
    share of 17 rays (the boss top's centre, 8 at rt / 2, 8 at rt) that hit another (non-wheel) part within
    BOSS_HIDDEN["dist"] of the boss top, cast along the outward axle (-1 = the wheel's -Y face, +1 = +Y). A ray that
    starts inside a part (a boss reaching into a rocker plate) hits that part's far side. The frame comes from the liner
    (two crest rings + the root ring, symmetric), so a boss already removed does not move it.
    """
    from mathutils.bvhtree import BVHTree
    import numpy as np
    bpy.context.view_layer.update()
    by_name = {o.name: o for o, _k in parts}
    pairs = {}
    for dn, ln, *_r in WHEEL_LOG:
        if dn in by_name and ln in by_name and by_name[dn].get("cs2_wheel") == WHEEL_ID:
            pairs[dn] = ln
    wheel_names = set(pairs) | set(pairs.values()) | {o.name for o, _k in parts if o.get("cs2_wheel_hub")}
    verts, polys = [], []
    for o, _k in parts:
        if o.name in wheel_names or o.type != "MESH":
            continue
        base = len(verts)
        verts += _world_co(o)
        polys += [[base + i for i in p.vertices] for p in o.data.polygons]
    bvh = BVHTree.FromPolygons(verts, polys) if polys else None
    out = {}
    for dn, ln in sorted(pairs.items()):
        d = by_name[dn]
        r, w = float(d["cs2_wheel_r"]), float(d["cs2_wheel_w"])
        P = wheel_profile(r, w)
        co = np.array([tuple(v) for v in _world_co(by_name[ln])])
        c = mathutils.Vector(tuple((co.min(0) + co.max(0)) / 2.0))
        _u, _s, vt = np.linalg.svd(co - co.mean(0), full_matrices=False)
        ax = mathutils.Vector(tuple(vt[-1])).normalized()
        if ax.y < 0.0:
            ax = -ax
        wa = min(range(3), key=lambda i: abs(ax[i]))
        e1 = mathutils.Vector(tuple(1.0 if i == wa else 0.0 for i in range(3)))
        e1 = (e1 - ax * e1.dot(ax)).normalized()
        e2 = ax.cross(e1)
        samples = [(0.0, 0.0)] + [(rr * math.cos(a), rr * math.sin(a)) for rr in (P["rt"] / 2.0, P["rt"])
                                  for a in (math.pi * k / 4.0 for k in range(8))]
        res = {}
        for s in (-1, 1):
            hits = 0
            for x, y in samples:
                o = c + ax * (s * (P["hw"] + P["h"] + 1e-4)) + e1 * x + e2 * y
                if bvh is not None and bvh.ray_cast(o, ax * s, BOSS_HIDDEN["dist"])[0] is not None:
                    hits += 1
            res[s] = hits / len(samples)
        out[dn] = res
    return out


def hub_name(dn):
    """The seen-boss part of wheel `dn`: "hub" glued to its side tag and no "_rim" (sheave_n_0_m_rim -> sheave_hubn_0_m,
    sheave_guide_sheave_n_1_rim -> sheave_guide_sheave_hubn_1, sheave_cwhead_n -> sheave_cwhead_hubn, haulroll_p_3 ->
    haulroll_hubp_3): it keeps the family prefix (LOD / density / material tables) but never matches a rim's own name, a
    prefix of it, "sheave_[np]_" / "haulroll_[np]_" or "*_rim" - the contracts that select rims by name never see it."""
    import re
    base = dn[:-len("_rim")] if dn.endswith("_rim") else dn
    m = list(re.finditer(r"_([np])(_|$)", base))
    if not m:
        raise ValueError(f"hub_name: no side tag in {dn!r}")
    k = m[-1]
    return base[:k.start()] + "_hub" + k.group(1) + k.group(2) + base[k.end():]




def cull_hidden_bosses(parts, label=""):
    """
    T62 round 3: on an asset's full part list (every housing built), drop every hub boss that boss_visibility finds
    hidden (>= BOSS_HIDDEN["share"] of its rays blocked within BOSS_HIDDEN["dist"]; 22 tris a face saved where a rocker
    plate / bar covers it) and move the SEEN bosses out of the discs part into their own part hub_name(discs) (same
    material key), appended to `parts`: the discs part stays symmetric about the wheel's mid-plane, so every contract
    that reads a rim's bbox centre (C1 / C2, lattice C3, tram C4, the stations' ST1 / M21) reads the axle as before.
    Stamps: discs and hub obj["cs2_wheel_bosses"] = the faces kept ("-+", "-", "+" or ""); hub obj["cs2_wheel_hub"] =
    the discs name. v1: no-op. Returns {discs name: kept}.
    """
    if not wheels_v2():
        return {}
    import numpy as np
    vis = boss_visibility(parts)
    by_name = {o.name: o for o, _k in parts}
    key_of = {o.name: k for o, k in parts}
    kept_all = {}
    for dn, res in sorted(vis.items()):
        d = by_name[dn]
        if d.get("cs2_wheel_bosses") is None:
            continue
        r, w = float(d["cs2_wheel_r"]), float(d["cs2_wheel_w"])
        P = wheel_profile(r, w)
        kept = "".join(t for t, s in (("-", -1), ("+", 1)) if t in d["cs2_wheel_bosses"] and res[s] < BOSS_HIDDEN["share"])
        # the wheel frame from its liner (symmetric)
        ln = [l for dn_, l, *_r in WHEEL_LOG if dn_ == dn][-1]
        lco = np.array([tuple(v) for v in _world_co(by_name[ln])])
        c = mathutils.Vector(tuple((lco.min(0) + lco.max(0)) / 2.0))
        _u, _s, vt = np.linalg.svd(lco - lco.mean(0), full_matrices=False)
        ax = mathutils.Vector(tuple(vt[-1])).normalized()
        if ax.y < 0.0:
            ax = -ax
        M = d.matrix_world

        def side(v):
            # a boss vertex: inside 0.7 r (the boss base ring sits ON the disc plane at 0.45 r, the disc's own ring is at r)
            # and on or proud of a face plane
            d_ = M @ v.co - c
            a = d_.dot(ax)
            if (d_ - ax * a).length > 0.7 * r or abs(a) < P["hw"] - 1e-5:
                return 0
            return -1 if a < 0.0 else 1
        # the hub part: a copy of the discs with only the kept bosses' vertices
        if kept:
            hub = d.copy()
            hub.data = d.data.copy()
            hub.name = hub_name(dn)
            hub.data.name = hub.name
            for k in [k for k in hub.keys() if k.startswith("cs2_wheel")]:
                del hub[k]
            bpy.context.collection.objects.link(hub)
            bm = bmesh.new()
            bm.from_mesh(hub.data)
            want = {-1 if t == "-" else 1 for t in kept}
            bmesh.ops.delete(bm, geom=[v for v in bm.verts if side(v) not in want], context="VERTS")
            bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
            cs2_asset.canonical_order_bmesh(bm)
            bm.to_mesh(hub.data)
            bm.free()
            hub.data.update()
            hub["cs2_wheel_hub"] = dn
            hub["cs2_wheel_bosses"] = kept
            if hub.name != hub_name(dn):
                raise RuntimeError(f"{label}: hub part name collided ({hub.name})")
            parts.append((hub, key_of[dn]))
        # the discs part: the discs only
        bm = bmesh.new()
        bm.from_mesh(d.data)
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if side(v) != 0], context="VERTS")
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
        bm.to_mesh(d.data)
        bm.free()
        d.data.update()
        d["cs2_wheel_bosses"] = kept
        kept_all[dn] = kept
    n = {k: sum(1 for v in kept_all.values() if v == k) for k in ("-+", "-", "+", "")}
    print(f"[sheaves] {label} hub bosses kept (seen): {n['-+']} wheels both faces, {n['-'] + n['+']} one face, "
          f"{n['']} none; {sum(len(v) for v in kept_all.values())} bosses x {WHEEL_BOSS_TRIS} tris in the hub "
          f"parts (hidden = >= {BOSS_HIDDEN['share']:.0%} of the rays blocked within {BOSS_HIDDEN['dist']} m)")
    return kept_all


def train_wheel(name, liner, r, w, loc, segs=12, groove=None, ngon=False):
    """A train's wheel by the active generation: v2 = wheel(r, w, loc) (the one 16-segment wheel; `segs` /
    `groove` / `ngon` are the v1 family numbers and are ignored), v1 = lite_sheave_parts(segs, groove) (pre-T62)."""
    if wheels_v2():
        return wheel(name, liner, r, w, loc)
    return lite_sheave_parts(name, liner, r, w, loc, segs=segs, groove=groove)


def _unit(v):
    v = mathutils.Vector(v)
    return v / max(v.length, 1e-12)


def _kit(parts_q, key_of):
    """cs2_kit (obj, quad) list -> (obj, material key) by a name -> key rule."""
    return [(o, key_of(o.name)) for o, _q in parts_q]


# --------------------------------------------------------------------------------------
# The trains
# --------------------------------------------------------------------------------------

def fg_train_side(tag, s, y, main_z, sub_z, axle_z, n_sub, sub_span, main_span, sheave_r, sheave_w,
                  arm_t=FG_ARM_T, hanger=FG_HANGER, ins=FG_HANGER_INSET, prefix="", inboard=0.0):
    """
    The Yan (FG / ET) rocker tree of ONE lane (`tag` "n" / "p", `s` -1 / +1, the lane at `y`), hanging
    below its carrier (lift_tower: the crossarm; its centre = main_z + hanger): the hanger (its top `ins`
    inside a carrier `arm_t` deep), the main rocker at main_z, per sub rocker a pivot pin, the sub rocker at
    sub_z and two sheave arms down to the sheaves on axle_z (lite_sheave_parts: discs + liner) with an
    outboard axle cap each. lift_tower.fg_parts' loop body verbatim (the same expressions, the same part
    order). Returns [(obj, tower material key)].
    T62 (2026-10-18): the sheaves come from train_wheel (v2 = wheel(), round 2's capped wheel - its own hub bosses, so NO
    kit axle cap;
    v1 = lite_sheave_parts + the kit axle cap, the pre-T62 tree).
    T27 (2026-10-17, build/T27-rope-path.md; lift_tower.ROPE_ON_CROWN): `inboard` > 0 moves the whole carrier tree
    (hanger, rockers, pins, sheave arms) that far INBOARD of the lane (to y - s * inboard) and hangs each sheave on an
    axle stub `<prefix>axlecap_stub_<tag>_<si>_<m|p>` (the axlecap_ LOD class) from its arm to the sheave's inboard face, so a rope riding the sheave
    crowns clears the tree (in-plane, the rope on the crown runs through the sub rockers and the arms). 0 = the shipped
    tree, expression for expression.
    """
    P = []
    yl = y
    y = y - s * inboard if inboard else y
    P.append((K.add_box(f"{prefix}hanger_{tag}", (0.16, 0.15, hanger + arm_t - ins),
                        (0.0, y, main_z + hanger / 2.0 - ins / 2.0)), "paint"))
    P.append((K.add_box(f"{prefix}rocker_main_{tag}", (main_span + 0.40, arm_t * 0.7, 0.15),
                        (0.0, y, main_z)), "paint"))
    for si in range(n_sub):
        sx = (-main_span / 2.0 + si * (main_span / max(1, n_sub - 1))) if n_sub > 1 else 0.0
        P.append((K.add_cyl(f"{prefix}pivot_{tag}_{si}", 0.045, 0.22,
                            (sx, y, (main_z + sub_z) / 2.0), (math.radians(90.0), 0.0, 0.0),
                            segments=6, caps="both"), "galv"))
        P.append((K.add_box(f"{prefix}rocker_sub_{tag}_{si}", (sub_span + 0.30, arm_t * 0.55, 0.12),
                            (sx, y, sub_z)), "paint"))
        for wtag, wv in (("m", -1.0), ("p", 1.0)):
            wx = sx + wv * sub_span / 2.0
            P.append((K.add_box(f"{prefix}sheave_arm_{tag}_{si}_{wtag}", (0.09, 0.10, 0.26),
                                (wx, y, (sub_z + axle_z) / 2.0)), "paint"))
            # T62: the wheel (v2 = cs2_sheaves.wheel at the FG radius / width; v1 = lite_sheave_parts)
            P += train_wheel(f"{prefix}sheave_{tag}_{si}_{wtag}_rim", f"{prefix}liner_{tag}_{si}_{wtag}",
                             sheave_r, sheave_w, (wx, yl, axle_z))
            if inboard:
                y0, y1 = y, yl - s * sheave_w / 2.0
                P.append((K.add_cyl(f"{prefix}axlecap_stub_{tag}_{si}_{wtag}", 0.035, abs(y1 - y0),
                                    (wx, (y0 + y1) / 2.0, axle_z), (math.radians(90.0), 0.0, 0.0),
                                    segments=6, caps="both"), "galv"))
            if wheels_v2():
                continue        # T62: the v2 wheel carries its own hub bosses (v1 below: the FG kit cap)
            caps = _kit(K.axle_cap(f"{tag}_{si}_{wtag}", 0.09,
                                   (wx, yl + s * sheave_w / 2.0, axle_z),
                                   "+Y" if s > 0 else "-Y", studs_geo=False, segs=8),
                        lambda n: "galv")
            if prefix:      # a station's train: the cap joins the prefix's name space (cs2_kit names it axlecap_<n>)
                for o, _k in caps:
                    o.name = f"{prefix}axlecap_{tag}_{si}_{wtag}"
                    o.data.name = o.name
            P += caps
    return P


def fg_spans(sheaves, pitch, sheave_r):
    """(n_sub, sub_span, main_span) of an FG train with `sheaves` per lane (lift_tower.fg_parts)."""
    n_sub = max(1, sheaves // 2)
    sub_span = pitch
    main_span = (n_sub - 1) * (sub_span + 2.0 * sheave_r + 0.10)
    return n_sub, sub_span, main_span


def dq_centres(n, pitch, axle, sag, gauge, side, x0=0.0):
    """[(X_k, Y, Z_k)] for one side: X_k = (k - (n-1)/2) pitch, Z_k = A - sag (2 X_k / L_c)^2
    (lift_tower.dq_sheave_centres); `x0` shifts the train along travel (stations; the towers pass 0 and
    take the unshifted expression)."""
    lc = (n - 1) * pitch
    out = []
    for k in range(n):
        xk = (k - (n - 1) / 2.0) * pitch
        zk = axle - sag * (2.0 * xk / lc) ** 2 if lc > 0 else axle
        out.append(((xk + x0) if x0 else xk, side * gauge / 2.0, zk))
    return out


def _sub_pairs(n_subs):
    """Sub-rocker index pairs under the main rockers + the subs no main carries. Even counts
    pair (0,1), (2,3), ... (every DQ / ETDQ build, unchanged). Odd counts (GD 10 sheaves = 5
    subs) pair from both ends inward, so the lone sub sits in the MIDDLE and the train stays
    symmetric about the portal: (0,1), (3,4) + [2]. Revision 1c: the lone sub hangs from the
    yoke on a link (DQ_LINK, _dq_train)."""
    if n_subs % 2 == 0:
        return [(2 * m, 2 * m + 1) for m in range(n_subs // 2)], []
    mid = n_subs // 2
    left = [(2 * m, 2 * m + 1) for m in range(mid // 2)]
    right = [(n_subs - 2 - 2 * m, n_subs - 1 - 2 * m) for m in range(mid // 2)][::-1]
    single = [i for i in range(n_subs) if not any(i in p for p in left + right)]
    return left + right, single


def dq_train(centres_of, n, axle, sheave_r, sheave_w, segs, groove, sub_y, main_y, pin_s, hanger_box,
             hz, link=DQ_LINK, prefix="", ngon=False):
    """
    The gantry sheave trains of both lanes with every carrier INBOARD of the sheaves (towers-v2 4.2, F9):
    lift_tower._dq_train's body verbatim. `centres_of(s)` = the [(x, y, z)] sheave centres of the side s
    (-1 / +1; dq_centres), n sheaves per side (even), `axle` the train's reference height (the main rockers,
    pins, yoke and hanger sit at axle + their tower offsets), `sub_y` / `main_y` the |y| of the sub / main
    rocker plates and the yoke, `pin_s` the sub pins' length, `hanger_box` {x, y0, y1, z0, z1} (z about the
    axle), `hz` the hanger's top / bottom inset, `link` the lone-middle-sub link (DQ_LINK; None = none).
    Returns [(obj, tower material key)].
    """
    P = []
    for tag, s in (("n", -1.0), ("p", 1.0)):
        cents = centres_of(s)
        for k, (xk, yk, zk) in enumerate(cents):
            # T62: v2 = cs2_sheaves.wheel (`groove` ignored, `ngon` the cap-bound GD discs); v1 = lite_sheave_parts
            P += train_wheel(f"{prefix}sheave_{tag}_{k}_rim", f"{prefix}liner_{tag}_{k}", sheave_r,
                             sheave_w, (xk, yk, zk), segs=segs, groove=groove, ngon=ngon)
        subs = [((cents[2 * j][0] + cents[2 * j + 1][0]) / 2.0,
                 (cents[2 * j][2] + cents[2 * j + 1][2]) / 2.0) for j in range(n // 2)]
        for j, (xs, zs) in enumerate(subs):
            P.append((K.add_box(f"{prefix}rocker_sub_{tag}_{j}", (0.81, 0.066, 0.25), (xs, s * sub_y, zs)),
                      "paint"))
            P.append((K.add_cyl(f"{prefix}pivot_{tag}_s{j}", 0.03, pin_s,
                                (xs, s * (sub_y + main_y) / 2.0, zs),
                                (math.radians(90.0), 0.0, 0.0), segments=6, caps="none"), "galv"))
        pairs, single = _sub_pairs(len(subs))
        mains = [((subs[a][0] + subs[b][0]) / 2.0, (subs[a][1] + subs[b][1]) / 2.0 - axle)
                 for a, b in pairs]
        hang = mains + [(subs[i][0], subs[i][1] - axle) for i in single]
        for m, (xm, dzm) in enumerate(mains):
            P.append((K.add_box(f"{prefix}rocker_main_{tag}_{m}", (1.78, 0.072, 0.30),
                                (xm, s * main_y, axle + 0.05 + dzm)), "paint"))
            P.append((K.add_cyl(f"{prefix}pivot_{tag}_m{m}", 0.03, 0.14,
                                (xm, s * main_y, axle + 0.225 + dzm),
                                (math.radians(90.0), 0.0, 0.0), segments=6, caps="none"), "galv"))
        xs_all = [h[0] for h in hang]
        dz_y = sum(h[1] for h in hang) / len(hang)
        span = (max(xs_all) - min(xs_all)) + 0.30
        P.append((K.add_box(f"{prefix}yoke_{tag}", (span, 0.072, 0.17),
                            ((max(xs_all) + min(xs_all)) / 2.0, s * main_y,
                             axle + 0.335 + dz_y)), "paint"))
        if link is not None:
            yoke_z0 = axle + 0.335 + dz_y - 0.17 / 2.0
            for i in single:
                xs, zs = subs[i]
                z0, z1 = zs - link["below"], yoke_z0 + link["bury"]
                P.append((K.add_box(f"{prefix}rocker_link_{tag}_{i}", (link["w"], link["t"], z1 - z0),
                                    (xs, s * main_y, (z0 + z1) / 2.0)), "paint"))
        h = hanger_box
        # detail pass 1 reclaim: the hanger's outboard end runs 6 mm into the yoke; when the
        # yoke's z band encloses the (inset) hanger the end face is never seen - dropped
        yz0, yz1 = axle + 0.335 + dz_y - 0.085, axle + 0.335 + dz_y + 0.085
        hz0, hz1 = axle + h["z0"] + hz, axle + h["z1"] - hz
        enclosed = yz0 <= hz0 + 1e-9 and hz1 <= yz1 + 1e-9 and h["y1"] > main_y - 0.036 + 1e-6
        drop = (("+Y",) if s > 0 else ("-Y",)) if enclosed else ()
        P.append((K.add_box(f"{prefix}hanger_{tag}", (h["x"], h["y1"] - h["y0"], h["z1"] - h["z0"] - 2.0 * hz),
                            (0.0, s * (h["y0"] + h["y1"]) / 2.0, axle + (h["z0"] + h["z1"]) / 2.0),
                            drop=drop), "paint"))
    return P


# --------------------------------------------------------------------------------------
# The towers' finish of the train parts
# --------------------------------------------------------------------------------------

def chamfer_long_edges(obj, axis, width=None):
    """Design 3.1: chamfer the 4 long edges of a box part (bmesh bevel, 1 segment, `width`
    0.012) - the edges parallel to `axis`. +16 tris per box (octagon section)."""
    width = DP1_CHAMFER if width is None else width
    a = _unit(axis)
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    edges = [e for e in bm.edges
             if abs((e.verts[1].co - e.verts[0].co).normalized().dot(a)) > 0.999]
    bmesh.ops.bevel(bm, geom=edges, offset=width, offset_type="OFFSET", segments=1,
                    profile=0.5, affect="EDGES", clamp_overlap=True)
    cs2_asset.canonical_order_bmesh(bm)
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    return len(edges)


def bevel_parts(parts, table, width=None, strip=""):
    """lift_tower._bevel: a BEVEL_W (or `width`) bevel modifier, `segs` segments by the longest matching
    prefix of `table`, angle limit 45 deg, clamped, hardened normals, applied. `strip`: a station prefix
    removed from the name before the table lookup (the towers pass "")."""
    w = BEVEL_W if width is None else width
    for o, _k in parts:
        segs = None
        nm = o.name[len(strip):] if (strip and o.name.startswith(strip)) else o.name
        for prefix, n in sorted(table.items(), key=lambda kv: -len(kv[0])):
            if nm.startswith(prefix):
                segs = n
                break
        if not segs:
            continue
        mod = o.modifiers.new("Bevel", type="BEVEL")
        mod.width = w
        mod.segments = segs
        mod.limit_method = "ANGLE"
        mod.angle_limit = math.radians(45.0)
        mod.use_clamp_overlap = True
        mod.harden_normals = True
        bpy.ops.object.select_all(action="DESELECT")
        o.select_set(True)
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.modifier_apply(modifier=mod.name)


def apply_transforms(parts):
    """lift_tower._apply_transforms: bake every part's object transform into its mesh."""
    for o, _k in parts:
        mb = o.matrix_basis.copy()
        if mb != mathutils.Matrix.Identity(4):
            o.data.transform(mb)
            o.matrix_basis = mathutils.Matrix.Identity(4)
            o.data.update()
    bpy.context.view_layer.update()


def finish_train(parts, family, prefix=""):
    """A STATION train finished as the towers finish theirs (lift_tower.build_tower_v2 order): the train's
    bevels (TRAIN_BEVEL[family]), the transforms baked, the FG long-edge chamfers (TRAIN_CHAMFER) - so the
    station's rockers / hangers / arms have the towers' edges. Returns the chamfered part names."""
    bevel_parts(parts, TRAIN_BEVEL[family], strip=prefix)
    apply_transforms(parts)
    done = []
    for o, _k in parts:
        nm = o.name[len(prefix):] if (prefix and o.name.startswith(prefix)) else o.name
        for pre, axis in TRAIN_CHAMFER[family]:
            if nm.startswith(pre):
                chamfer_long_edges(o, axis)
                done.append(o.name)
    return done


def train_tris(parts):
    """Triangle count of a part list (n-gons fanned)."""
    return sum(sum(len(pl.vertices) - 2 for pl in o.data.polygons) for o, _k in parts)



# --------------------------------------------------------------------------------------
# T62 round 2: the wheels' LOD forms (cs2_asset.make_lod_chain `fixed` hook)
# --------------------------------------------------------------------------------------

def lod_fixed():
    """The make_lod_chain `fixed` hook for the v2 wheels built in this process (None under v1: the chain is unchanged)."""
    return LodWheels() if wheels_v2() else None


class LodWheels:
    """
    WHEEL_LOD on a LOD copy of a FINISHED mesh: every wheel that survives the level's prefix deletes is rebuilt in the
    level's form from its own LOD0 vertices and corner UVs (never collapse-decimated; make_lod_chain keeps these faces
    out of the decimation): LOD1 = WHEEL_LOD[1]["segs"] segments (every second crest vertex): a flat band crest ring ->
    crest ring + one n-gon disc per side; LOD2 = deleted. UVs come from ONE source face per new face (the disc n-gon's
    own corners; the band from the -side flank face of its segment, squashed 2x along the rim) so no new face spans two
    UV islands of the repacked atlas. Wheels are found by name (WHEEL_LOG pairs present in the part table); the frame
    (centre, axle, phase) from the geometry, so it works in any frame (net, station-rotated, chord-tilted).
    """

    def __init__(self):
        self.pairs = []
        self.hubs = []
        self.report = {}

    def parts(self, table):
        idx = {n: i for i, n in enumerate(table, start=1)}
        lin = {}
        for dn, ln, *_rest in WHEEL_LOG:
            lin[dn] = ln
        self.pairs = sorted((idx[d], idx[l]) for d, l in lin.items() if d in idx and l in idx)
        # round 3: the seen hub bosses live in hub_name(discs) parts: they never reach a LOD (LOD1 wheels carry no boss)
        self.hubs = sorted(idx[hub_name(d)] for d in lin if d in idx and hub_name(d) in idx)
        return {i for p in self.pairs for i in p} | set(self.hubs)

    def tris(self, level, n_wheels):
        return int(n_wheels * WHEEL_LOD[level]["tris"])

    def apply(self, obj, table, level):
        """Rebuild / delete the surviving wheels of `obj` (a LOD copy) for `level`; returns (wheels, tris)."""
        import numpy as np
        form = WHEEL_LOD[level]
        bm = bmesh.new()
        bm.from_mesh(obj.data)
        lay = bm.faces.layers.int.get("part")
        uvls = list(bm.loops.layers.uv.values())
        cols = list(bm.loops.layers.color.values()) + list(bm.loops.layers.float_color.values())
        by = {}
        for f in bm.faces:
            by.setdefault(f[lay], []).append(f)
        n_w, made, dead = 0, [], []
        for hid in getattr(self, "hubs", []):
            dead += by.get(hid, [])
        for did, lid in self.pairs:
            DF, LF = by.get(did, []), by.get(lid, [])
            if not DF and not LF:
                continue
            if not DF or not LF:
                # a delete list that takes one half (e.g. an older table without "liner_") takes the wheel: it leaves
                # as a unit, never as a disc without its liner or the reverse
                print(f"[lod] {obj.name} LOD{level}: wheel {table[did - 1]} half deleted by the prefixes "
                      f"({len(DF)} disc / {len(LF)} liner faces): the rest of it goes too")
                dead += DF + LF
                continue
            n_w += 1
            dead += DF + LF
            if form["segs"] == 0:
                continue
            made += self._rebuild(bm, DF, LF, form["segs"], uvls, cols, np, f"{obj.name}:{table[did - 1]}")
        wverts = {v for f in dead for v in f.verts}
        bmesh.ops.delete(bm, geom=dead, context="FACES_ONLY")
        loose_e = [e for e in {e for v in wverts if v.is_valid for e in v.link_edges} if not e.link_faces]
        bmesh.ops.delete(bm, geom=loose_e, context="EDGES")
        loose_v = [v for v in wverts if v.is_valid and not v.link_edges]
        bmesh.ops.delete(bm, geom=loose_v, context="VERTS")
        made = [f for f in made if f.is_valid]
        if made:            # the LOD levels are tri-only (C12): triangulate the rebuilt faces (6 per 8-gon, 2 per quad)
            made = bmesh.ops.triangulate(bm, faces=made)["faces"]
        tris = len(made)
        bm.to_mesh(obj.data)
        bm.free()
        obj.data.update()
        self.report[level] = {"wheels": n_w, "tris": tris, "form": form["form"]}
        return n_w, tris

    @staticmethod
    def _rebuild(bm, DF, LF, segs1, uvls, cols, np, label):
        V = mathutils.Vector
        # the frame from the LINER (two crest rings + the root ring: symmetric even when a hub boss was culled)
        dverts = list({v for f in LF for v in f.verts})
        co = np.array([tuple(v.co) for v in dverts])
        c = V(tuple((co.min(0) + co.max(0)) / 2.0))
        _u, _s, vt = np.linalg.svd(co - co.mean(0), full_matrices=False)
        ax = V(tuple(vt[-1])).normalized()
        if ax[max(range(3), key=lambda i: abs(ax[i]))] < 0.0:
            ax = -ax
        wa = min(range(3), key=lambda i: abs(ax[i]))
        e1 = V(tuple(1.0 if i == wa else 0.0 for i in range(3)))
        e1 = (e1 - ax * e1.dot(ax)).normalized()
        e2 = ax.cross(e1)

        def frm(v):
            d = v.co - c
            a = d.dot(ax)
            q = d - ax * a
            return a, q.length, math.atan2(q.dot(e2), q.dot(e1))
        lverts = list({v for f in LF for v in f.verts})
        F = {v: frm(v) for v in lverts}
        rho = [F[v][1] for v in lverts]
        r, rmin = max(rho), min(rho)
        rcut = (r + rmin) / 2.0
        crest = [v for v in lverts if F[v][1] > rcut]
        neg = [v for v in crest if F[v][0] < 0.0]
        n = len(neg)
        if n == 0 or n % segs1 or len(crest) != 2 * n:
            raise RuntimeError(f"{label}: {len(crest)} crest vertices ({n} per side) do not reduce to {segs1}")
        step = 2.0 * math.pi / n
        # the phase: a crest vertex that sits over a groove-root vertex (round 3: the root ring has n / 2 vertices, the
        # kept LOD1 crest vertices must be those), the top-most of them (deterministic in any frame)
        roots = [F[v][2] for v in lverts if F[v][1] <= rcut]

        def over_root(v):
            return min(abs(math.remainder(F[v][2] - a, 2.0 * math.pi)) for a in roots) < step / 4.0 if roots else True
        cand = [v for v in neg if over_root(v)] or neg
        top = max(cand, key=lambda v: (round(v.co.z, 6), -round(v.co.x, 6), -round(v.co.y, 6)))
        th0 = F[top][2]

        def kof(fv):
            return int(round((fv[2] - th0) / step)) % n
        Cr = {-1: {}, 1: {}}
        for v in crest:
            Cr[-1 if F[v][0] < 0.0 else 1][kof(F[v])] = v
        if any(len(Cr[s]) != n for s in Cr):
            raise RuntimeError(f"{label}: crest rings not {n} distinct segments")

        def corner(lp):
            return ([tuple(lp[l].uv) for l in uvls], [tuple(lp[l]) for l in cols])
        # source corners: the -side flank face of each segment, and each disc n-gon (corners on its rim)
        flank = {}
        for f in LF:
            if sum(F[v][0] for v in f.verts) >= 0.0:
                continue
            ks = sorted(kof(F[v]) for v in f.verts if F[v][1] > rcut)
            k = n - 1 if (0 in ks and n - 1 in ks) else min(ks)
            flank[k] = (f, {lp.vert: corner(lp) for lp in f.loops})
        disc = {-1: {}, 1: {}}
        disc_face = {}
        for f in DF:
            f.normal_update()
            if abs(f.normal.dot(ax)) < 0.99:
                continue
            fr = [frm(lp.vert) for lp in f.loops]
            if max(x[1] for x in fr) < 0.8 * r:
                continue                       # a boss top
            s = -1 if sum(x[0] for x in fr) < 0.0 else 1
            disc_face[s] = f
            for lp, x in zip(f.loops, fr):
                if x[1] > 0.8 * r:
                    disc[s][kof(x)] = corner(lp)
        st = n // segs1
        ks = list(range(0, n, st))
        if any(k not in flank for k in ks) or any(k not in disc[s] for s in (-1, 1) for k in ks) or len(disc_face) != 2:
            raise RuntimeError(f"{label}: LOD0 wheel faces not found for every kept segment "
                               f"(flank {sorted(flank)}, discs {len(disc_face)})")
        out = []

        def put(verts, corners, example):
            f = bm.faces.new(verts, example)
            for lp, (uv, cl) in zip(f.loops, corners):
                for l, x in zip(uvls, uv):
                    lp[l].uv = x
                for l, x in zip(cols, cl):
                    lp[l] = x
            f.normal_update()
            if f.normal.dot(f.calc_center_median() - c) < 0.0:
                f.normal_flip()
            out.append(f)
        for s in (-1, 1):
            put([Cr[s][k] for k in ks], [disc[s][k] for k in ks], disc_face[s])
        for k in ks:
            k1, k2 = (k + 1) % n, (k + st) % n
            f0, src = flank[k]
            root = sorted((v for v in f0.verts if v not in (Cr[-1][k], Cr[-1][k1])),
                          key=lambda v: (Cr[-1][k].co - v.co).length)
            if len(root) == 1:
                # round 3: the source is the flank TRIANGLE (crest k, crest k + 1, root); the band's 4th corner takes the
                # midpoint of two of its corners - every corner stays inside that one triangle's UV island
                g, b_ = src[root[0]], src[Cr[-1][k1]]
                mid = ([tuple((x + y) / 2.0 for x, y in zip(u, v)) for u, v in zip(g[0], b_[0])],
                       [tuple((x + y) / 2.0 for x, y in zip(u, v)) for u, v in zip(g[1], b_[1])])
                corners = [src[Cr[-1][k]], g, mid, b_]
            else:
                corners = [src[Cr[-1][k]], src[root[0]], src[root[1]], src[Cr[-1][k1]]]
            put([Cr[-1][k], Cr[1][k], Cr[1][k2], Cr[-1][k2]], corners, f0)
        return out
