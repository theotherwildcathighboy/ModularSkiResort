"""
recipes/patrol.py - the two ski-patrol huts as lodge.py recipes:
    P1 summit hut (Lady Morgan)  `lodge.py --patrol p1` -> SkiPatrolHutCommercialLowA
    P2 patrol HQ (Song Mountain) `lodge.py --patrol p2` -> SkiPatrolHQCommercialLowA
both in assets/import/buildings/A.

Contract: docs/research/art/assets-2026-10/design/civic-sign.md section 3 with
civic-sign-skeptic.md applied (skeptic wins), ROUND-SPEC R21 (complexity 1), R22 (P2 lot
2 x 2), R25 (kit API wins):
    M1  P1 fence rolls moved inside the lot: centres x <= 3.20 (max x 3.42 <= 3.75);
        G3 (|x|, |y| <= half lot - 0.25) asserted on the parts.
    M2  P1 glazing is `_Win` (one cell (2, 2) = 12, _LOD1_Win + _LOD2_Win), the interior
        keyword CommercialLow is in the FILE name (no Richard ruling for frosted panes).
    M3  the white-on-red crosses are painted on FRONT-ONLY faces: kit emblem_disc gives
        `emblem_<n>_face` (one 16-fan, its own island) + `emblem_<n>_rim`; the P1 info
        board is kit sign_board `sign_p1info_face` (one quad) + frame.
    M7  P2 LOD1 <= 650 / LOD2 <= 200 (RS section 4 has one patrol row).
P2 depth: the 2 x 2 lot (R22) takes the contract's Q11 alternative "2 x 2 with 6 m": walls
X [-3.70, +2.30] (6.0 deep), deck X [2.30, 4.10].
Kit adaptations (measured on the first blockouts, build/civic-sign.md): every ground part
is LOD-protected whole by the kit (lod_level protect = ground prefixes), so the P2 piers are
square 0.40 m footings (`footing_`, 10 tris each: round 8-seg piers alone would eat the 200
tri LOD2) and the P1 log posts are 6-seg; the rolls go at LOD2 for the same budget; the P1
door lamp sits beside the door (the wall is 2.35 m tall and the door head meets the log
plate: no room above it).

Detail pass 1 (2026-10-11): design/detail-pass-1.md sections 3.3 / 3.4 / 5.2 with ALL ten
MUST-CHANGE items of design/detail-pass-1-skeptic.md (Richard: "all make sense") on the kit
of build/detail-pass-1-kit.md; report build/detail-pass-1-patrol.md.
    P1  "SKI" / "PATROL" board on the -Y end (M4 fix: 2.60 x 1.30 at w 1.75, T1), bench
        under it (bench_ in lod1_extra, M1), roof cross on the +X slope (kept to LOD2; the
        square starts 0.07 m below the kit ridge cap's REAL edge: hw = max(w / 2,
        RIDGE_EXT + 0.05) = 0.40 m plan), radio mast + red beacon on the +Y end, toboggan
        rack + akja + spine board on the back wall (S2: both lean ON the upper bar, the
        foot is derived from the bar), snow guard over the door, step stripe; vanilla wall
        light beside the rack (y +0.3, S2c) and an AC vent on the +Y end.
    P2  "SKI PATROL" / "FIRST AID" board on the +Y wall (M4 fix b: 4.60 x 1.40 at w 2.25),
        roof cross on the +Y slope (lod2_keep; the front disc leaves lod2_keep, M7), radio
        mast on the back gable, patrol flag on the deck (cloth panels in lod1_extra, M8),
        toboggan rack + akja + spine board on the front strips (facing "+X", M9), fire-hose
        reel, firewood stack (top below the sill BOARD at 1.80, not the sill 1.96), snow
        guards, lean-to gutter + leader downspout, 4 stair stripes; the repack of design 5.2
        (inner gable faces dropped, floor + deck undersides to soffit_ parts, frames and
        ridge at w 0.75); vanilla bench on the deck, two wall lights, an AC vent.
    Both: recipe.weather (DECISIONS 171: free slots, snow 0; intensity = the highest
    budget-clean step after the dressing, lodge --weather-sweep), recipe.weights (labels per
    design 2.0 / kit report 5), recipe.sub_objects (game local, design 1.6 GUIDs).
    Arrival prefixes name the DISC emblems exactly ("emblem_<n>_"): the roof crosses and
    flags are emblems too and would move the arrival centroid.

Lean fix (Richard, 2026-10-11 fifteenth look: "on the ski patrol shacks, a few of the
toboggans lean against nothing"; report build/detail-pass-1-patrol.md "Lean fix"). Generator
CONTRACT LEAN (recipes/_lean.py, run at the end of both builders, fails the build): every
leaning part (akja_, gear_ spine boards, pole_) has a SOLID face <= 0.02 m behind the top of
its back face over >= 80 % of its width, and its foot on grade / a deck / a tread.
    P1  the back-wall rack's upper bar moved up to the TOPS of the akja (1.9 m) and the spine
        board (a top rest bar; the rack ends at y +0.05, clear of the wall light); the bamboo
        poles' tops touch the front wall (they stood 0.15 m off it) and their feet sit at
        GROUND_LIFT (0.035 before). The +Y gable akja already rested (gap 0.002).
    P2  the two toboggans that leaned on the stair's open side (no stringer within 2 m of
        their tops) lean on the lean-to's -Y wall; the front rack's bar moved up to the akja
        top. The spine board already rested on the front gable (gap 0.006).

Fine-detail step 3d (2026-10-12; build/fine-detail-step3d-patrol-sign.md; the engine of
build/fine-detail-step2-engine.md, the school / L2 as the worked examples; Richard approved the shacks
in his eighteenth look and called their signs pixelated: "I think we know our plan for that"). Both
recipes are fine_detail.FineRecipe (fine_detail=True, sign_set=..., legacy = the approved rows), so
`lodge.py --no-fine` rebuilds the approved shacks vertex for vertex:
    signs   P1 "SKI / PATROL" + the info board, P2 "SKI PATROL / FIRST AID" leave the host atlas for
            SkiSignSetPatrolHutB / SkiSignSetPatrolHQB (sharers of the school's sheet, the reserved
            P1 / P2 rects painted in vector Bfont at 300 px/m; name boards lit 0.2, H2). The board
            frames, discs, roof crosses, flag cloths and the log-ends decal stay in the host.
    kit     every leaning akja / spine board is a SkiDressAkja / SkiDressSpineBoard row resting on the
            SAME support as the modelled part did (CONTRACT LEAN on the real kit mesh,
            recipes/_lean.check_rows); the P1 rack's top bar sits at the spine board's rest line and the
            rack akja is pitched 2.8 deg further back to share it (the kit props are built at 75 / 85 deg);
            P2's fire-hose reel is a SkiDressHoseReel row. The toboggan racks stay modelled (no vanilla /
            kit equivalent) as 8-seg round bars.
    vanilla the door lanterns -> WallLightShortRangeRandom01 rows (H4).
    geom    radio masts 12-seg, P2's flag mast 12-seg + ball finial, log posts / plates / frames 8-seg,
            the bamboo poles 6-sided (phase 90 deg: the lean_pole vertex rule holds), the P1 bench planked.

Authoring frame: +X = road / arrival (game +Z), origin = lot centre at grade. No bpy at
import: geometry is built in the builders, which load recipes/_civic.py lazily.
"""

import importlib.util
import math
import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
_BLENDER = os.path.dirname(_HERE)
if _BLENDER not in sys.path:
    sys.path.insert(0, _BLENDER)
from bldg_recipes import Budget, Recipe, PAD_RGB   # noqa: E402
from fine_detail import KIT, FineRecipe, SignSetSpec, kit_row, yaw_quat   # noqa: E402  (bpy-free)


def load_civic():
    name = "bldg_recipe_civic_helpers"
    if name in sys.modules:
        return sys.modules[name]
    spec = importlib.util.spec_from_file_location(name, os.path.join(_HERE, "_civic.py"))
    mod = importlib.util.module_from_spec(spec)
    sys.modules[name] = mod
    spec.loader.exec_module(mod)
    return mod


def load_lean():
    """recipes/_lean.py (CONTRACT LEAN; imports bpy, so loaded lazily like _civic)."""
    name = "bldg_recipe_lean_contract"
    if name in sys.modules:
        return sys.modules[name]
    spec = importlib.util.spec_from_file_location(name, os.path.join(_HERE, "_lean.py"))
    mod = importlib.util.module_from_spec(spec)
    sys.modules[name] = mod
    spec.loader.exec_module(mod)
    return mod


# CONTRACT LEAN: the leaning parts of both huts (prefixes) and the exact names each declares
LEAN_PREFIXES = ("akja_", "gear_", "pole_")


# Kit-internal coplanar overlaps (not recipe geometry; reported in build/civic-sign.md):
# window_bay jambs start / end inside the sill / head boxes; cs2_kit.handrail tube ends
# overhang the posts; cs2_bldg.gable_roof slabs (ridge extension) and rake boards cross at
# the ridge, so their gable-end side faces overlap under the ridge cap.
KIT_INTERNAL = (("jamb_", "sill_"), ("jamb_", "head_"), ("post_", "rail_"), ("roof_", "roof_"),
                ("roof_", "ridge_"), ("rake_", "rake_"))
GREEN = (0.16, 0.26, 0.275)          # patrol livery slot 1 = fascia sRGB (0.13, 0.21, 0.22) / 0.8
SLOTS = {1: GREEN, 2: PAD_RGB}

# detail pass 1: label colours (encoded sRGB literals: recipe modules import no bpy; equal to
# cs2_bldg.PATROL_RED_SRGB / WHITE_SRGB)
PATROL_RED = (0.72, 0.09, 0.08)
WHITE = (0.92, 0.92, 0.92)
# vanilla prop placeholders (design/detail-pass-1.md 1.6) and the facing quaternions
# (game local, local +Z = out of the wall; unverified until the placement pass)
WALL_LIGHT = ("WallLightShortRangeRandom01", "cfc12fde01d9da24b86ddedd657eaae1")
AC_VENT = ("ACVentWallRandom01", "b99325ff4ff4dfd459734d8f765c2597")
BENCH = ("GardenBenchRandom01", "8df0703be09426948b5bc3262d86bc04")
R2 = 0.7071068
Q_FACE = {"+X": (0.0, 0.0, 0.0, 1.0), "-X": (0.0, 1.0, 0.0, 0.0),
          "+Y": (0.0, R2, 0.0, R2), "-Y": (0.0, -R2, 0.0, R2)}
RIDGE_EXT = 0.35                     # = cs2_bldg.RIDGE_EXT (bpy-free literal here)


def ridge_cap_half(ridge_w):
    """Plan half-width of the kit ridge cap (cs2_bldg._ridge_cap_parts: max(w / 2,
    RIDGE_EXT + 0.05))."""
    return max(0.5 * ridge_w, RIDGE_EXT + 0.05)


def rest_foot(lean_deg, z_foot, bar_face, bar_top, gap, outward):
    """Foot coordinate (along the wall normal) of an akja / board leaning at lean_deg whose
    back line passes `gap` in front of a bar face at the bar's top: the back line moves
    toward the wall by cot(lean) per metre of rise. outward = the wall normal's sign."""
    run = (bar_top - z_foot) / math.tan(math.radians(lean_deg))
    return bar_face + outward * (gap + run)


def top_rest_foot(lean_deg, length, face, gap, outward):
    """Lean fix: foot coordinate (along the wall normal) of an akja / board of `length` at
    lean_deg whose TOP back edge rests `gap` in front of `face` (a wall, or the front face
    of a rest bar at the top's height): top = foot - outward x length x cos(lean)."""
    return face + outward * (gap + length * math.cos(math.radians(lean_deg)))


def lean_pole(C, name, foot_xy, wall_x, top_z, r, mat, gap=0.005):
    """Lean fix: a 4-seg pole (_civic.cyl_part) leaning on a +X-facing wall at x = wall_x:
    its back-most top vertex `gap` off the wall, its lowest vertex at GROUND_LIFT. For an
    axis a = (ax, 0, az) the cylinder ring (cyl_into, ref X) puts one vertex at r(-az, 0, ax)
    from each end centre: the back-most and the lowest one (ax < 0: the top leans to -X)."""
    fx, fy = foot_xy
    tx, fz = wall_x + gap + r, 0.02 + r
    for _ in range(4):
        L = math.hypot(tx - fx, top_z - fz)
        ax, az = (tx - fx) / L, (top_z - fz) / L
        tx = wall_x + gap + r * az
        fz = 0.02 + r * abs(ax) + 5e-5
    return C.cyl_part(name, (fx, fy, fz), (tx, fy, top_z), r, mat, segs=4)


def lean_pole_fine(C, name, foot_xy, wall_x, top_z, r, mat, gap=0.005):
    """Fine-detail step 3d: lean_pole as a 6-sided pole. cyl_into's ring puts a vertex at angle
    phase + 2 pi k / n from t; phase pi / 2 puts one exactly at b = r(-az, 0, ax) (and at -b), the
    two vertices lean_pole's rule derives the wall gap and the foot from, so the derivation holds."""
    fx, fy = foot_xy
    tx, fz = wall_x + gap + r, 0.02 + r
    for _ in range(4):
        L = math.hypot(tx - fx, top_z - fz)
        ax, az = (tx - fx) / L, (top_z - fz) / L
        tx = wall_x + gap + r * az
        fz = 0.02 + r * abs(ax) + 5e-5
    return C.cyl_part(name, (fx, fy, fz), (tx, fy, top_z), r, mat, segs=6, phase=math.pi / 2)


# ---- fine-detail step 3d: dressing-kit LEAN rows (module docstring) ----------------------------------
#: (prefab y, prefab z) of each lean kit prop's REST LINE (the vertices nearest the support: the akja's
#: nose rim, the spine board's top back edge), measured on the shipped props/B FBX (z = fine_detail.KIT
#: rest_z). recipes/_lean.check_rows measures the REAL kit mesh placed by the row on every build, so a
#: drifted constant fails the build instead of shipping a floating prop.
KIT_REST = {"SkiDressAkja": (1.8801, -0.7108), "SkiDressSpineBoard": (1.843, -0.1612)}
KIT_GAP = 0.004              # rest line this far in front of its support face (LEAN reads ~+0.005..0.007 m)
LEAN_SAMPLE = 0.005          # _lean.measure_part samples the contact 5 mm down the part's axis
KIT_LEAN_DEG = {"SkiDressAkja": 75.0, "SkiDressSpineBoard": 85.0}   # dress_kit AKJA / SPINE "lean"
#: kit colour variants (Richard 2026-10-12 ~18:50; build/kit-colour-variants.md): the akja shells in rescue red
#: (the red cross, cs2_bldg.PATROL_RED_SRGB); P1 and P2 alike
KIT_COLOUR_SET = "red"


def rest_after_pitch(prop, pitch_deg):
    """(y, z) of the prop's rest line after an extra lean `pitch_deg` (top toward the support, about the
    foot line = the prefab x axis through the origin)."""
    y, z = KIT_REST[prop]
    a = -math.radians(float(pitch_deg))
    return (y * math.cos(a) - z * math.sin(a), y * math.sin(a) + z * math.cos(a))


def pitch_for_rest_height(prop, y_target, tol=1e-6):
    """The extra lean (deg, 0..15) that brings the rest line down to prefab height y_target."""
    lo, hi = 0.0, 15.0
    if rest_after_pitch(prop, 0.0)[0] < y_target - tol:
        raise ValueError(f"{prop}: rest line {KIT_REST[prop][0]} is below {y_target}")
    for _ in range(80):
        mid = 0.5 * (lo + hi)
        if rest_after_pitch(prop, mid)[0] > y_target:
            lo = mid
        else:
            hi = mid
    return round(0.5 * (lo + hi), 4)


def contact_z(prop, pitch_deg=0.0):
    """Height (above the foot) _lean samples the contact at: the rest line, 5 mm down the axis."""
    lean = KIT_LEAN_DEG[prop] - float(pitch_deg)
    return rest_after_pitch(prop, pitch_deg)[0] - LEAN_SAMPLE * math.sin(math.radians(lean))


def _qmul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (aw * bx + ax * bw + ay * bz - az * by, aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw, aw * bw - ax * bx - ay * by - az * bz)


def lean_row(prop, support, normal, plane, along, floor_y, pitch_deg=0.0, gap=KIT_GAP, replaces=None,
             note=None):
    """A dressing-kit LEAN row (game local, the hosts' (y, z, x) convention, m_ParentMesh 0: the prop
    stands in the building's own frame like the modelled part it replaces). normal = the support face's
    outward game axis ("+X" / "-X" / "+Z" / "-Z"), plane = that face's game coordinate along it, along = the
    prop centre's game coordinate along the face, floor_y = the foot height. The rest line lands `gap` in
    front of the face; the prop's +Z turns to the normal (its top leans back onto the support), pitched
    `pitch_deg` further back about its foot line. `support` = the host part it must rest on (recorded;
    recipes/_lean.check_rows asserts it); `replaces` = the modelled part the row stands in for."""
    sgn = 1.0 if normal[0] == "+" else -1.0
    axis = normal[1].upper()
    n = (sgn, 0.0) if axis == "X" else (0.0, sgn)
    _yr, zr = rest_after_pitch(prop, pitch_deg)
    off = plane + sgn * (-zr + gap)
    pos = (off, float(floor_y), float(along)) if axis == "X" else (float(along), float(floor_y), off)
    a = -math.radians(float(pitch_deg))
    qp = (math.sin(a / 2.0), 0.0, 0.0, math.cos(a / 2.0))
    q = tuple(round(v, 7) + 0.0 for v in _qmul(yaw_quat(n), qp))
    if note is None and replaces:
        note = f"replaces {replaces} (resting on {support})"
    r = kit_row(prop, tuple(round(v, 4) for v in pos), rot=q, parent=0, note=note,
                colour_set=KIT_COLOUR_SET if KIT[prop].coloured else None)
    r["lean"] = {"support": support, "replaces": replaces, "wall_normal": [n[0], n[1]],
                 "wall_point": [round(plane if axis == "X" else along, 4),
                                round(along if axis == "X" else plane, 4)],
                 "rest_z": round(_rest_z_eff(prop, pitch_deg), 4), "pitch_deg": float(pitch_deg),
                 "gap": gap, "contact_z": round(float(floor_y) + contact_z(prop, pitch_deg), 4)}
    return r


def _rest_z_eff(prop, pitch_deg):
    return rest_after_pitch(prop, pitch_deg)[1]


def wall_row(prop_or_vanilla, pos, normal, parent=0, note=None):
    """A wall-mounted row facing `normal` ("+X" / "-X" / "+Z" / "-Z", game): a kit prop by name (a mod row)
    or a vanilla (prefab, guid) pair (the recipe's tuple row form)."""
    sgn = 1.0 if normal[0] == "+" else -1.0
    n = (sgn, 0.0) if normal[1].upper() == "X" else (0.0, sgn)
    if isinstance(prop_or_vanilla, str):
        return kit_row(prop_or_vanilla, pos, facing=n, parent=parent, note=note,
                       colour_set=KIT_COLOUR_SET if KIT[prop_or_vanilla].coloured else None)
    prefab, guid = prop_or_vanilla
    return (prefab, guid, tuple(pos), yaw_quat(n), parent)


# ======================================================================================
# P1 - summit hut (contract 3.2; lot 1 x 1 -> X +-4, Y +-4)
# ======================================================================================

P1_LOT = (1, 1)
P1_X = (-2.0, 2.0)
P1_Y = (-2.725, 2.725)
P1_EAVE = 2.35                       # wall top = roof centre plane at the wall line
P1_PITCH = 14.0
P1_OVER, P1_RAKE_OVER, P1_SLAB, P1_FASCIA = 0.35, 0.34, 0.10, 0.17
P1_RIDGE_CAP = (0.30, 0.08)
P1_POST_R, P1_POST_TOP = 0.10, 2.40
P1_PLATE_R, P1_PLATE_Z = 0.09, 2.21
P1_DOOR = (-1.30, 0.85, 1.97)        # y centre, w, h (head meets the log plate)
P1_STEP = (2.0, 2.6, -1.8, -0.8, 0.15)
P1_WIN = (1.60, 0.86, 1.43, 0.65)    # y centre, w, h, sill (head 2.08 under the plate)
P1_DISC = (-0.25, 1.70, 0.70)        # y, z centre, D (front-only face, M3)
P1_BOARD = (0.50, 1.125, 0.55, 0.85)  # y centre, z bottom, w, h
P1_RAIL = ((2.9, -2.4, 0.0), (2.9, -0.4, 0.0))
P1_ROLLS = tuple((x, y) for x in (2.70, 3.20) for y in (0.75, 1.30, 1.85))   # M1
P1_ROLL_R, P1_ROLL_H = 0.22, 0.32
P1_POLES_Y = (2.27, 2.33, 2.39, 2.45, 2.51, 2.57)
P1_LAMP = (-2.20, 1.85)              # beside the door (y, z)
# ---- detail pass 1 (design 3.3; skeptic M4 / M8 / S2 / S3 / S6; kit report section 5) ----
P1_NAME = (0.0, 0.62, 2.60, 1.30)    # -Y end board: u (= x), z bottom, w, h (M4: "SKI" /
#                                      "PATROL" T1 at w 1.75; clear z 0.2-2.0, logs at x +-1.91)
P1_NAME_T = 0.08
P1_CROSS = 1.50                      # +X slope roof cross (square side, m)
P1_CROSS_GAP = 0.07                  # its upper edge this far (plan) below the ridge cap edge
P1_GUARD_OFF = 0.35                  # snow guard over the door: brackets at plan x 1.99-2.03,
#                                      0.065 m below the cross's lower edge (plan 1.925)
P1_MAST = (-1.60, 3.30, 0.60, 5.30)  # x, y, z0, z1: +Y end, 0.075 m outside the rake board
P1_MAST_BR = (1.2, 2.0)              # brackets to the +Y end wall (y 2.725)
P1_RACK = (-0.05, 2.6, (0.55, 1.86))  # back wall (-X, u = -y): y 0.05 .. -2.6, bar heights.
#                                      Lean fix: the upper bar is the TOP rest of both items
#                                      (akja top z 1.855, board 1.863: on the 1.84-1.88 bar
#                                      face); it ends at y +0.05, 0.06 m clear of the wall
#                                      light's D10 box (light at y +0.3, z 1.95). Length 2.65
#                                      > cs2_bldg.NET_MAX_M 2.5: the bars keep their box UVs (6
#                                      islands each; a net bar is 1), P1 stays above the 300-
#                                      island margin line (304 -> 8 px gutters, M6)
P1_RACK_DEPTH, P1_RACK_BAR = 0.12, 0.04
P1_AKJA = (-0.6, 75.0, 1.9)          # y, lean, length (lean fix: 2.0 -> 1.9, top on the bar)
P1_BACKBOARD = (-1.6, 85.0, 1.85)    # y, lean, length (kit default; top on the bar)
REST_GAP = 0.01                      # akja / board back line 1 cm in front of the bar face
P1_POLE_TOP = 2.0                    # lean fix: the poles' tops touch the front wall (x 2.0)
P1_LEANING = ("akja_p1", "akja_p1b", "gear_p1board") + tuple(f"pole_{k}" for k in range(6))
P1_BENCH = (0.0, 1.6)                # x, length: back line on the -Y wall, no backrest
P1_WALL_LIGHT = (0.3, 1.95)          # back wall (y, z): beside the akja top (S2c)
P1_VENT = (-1.0, 1.90)               # +Y end (x, z)
# ---- fine-detail step 3d (module docstring) ------------------------------------------------------------
LOG_SEGS_FINE = 8                    # log posts / plates / door-frame logs (were 6)
# the rack's TOP bar at the spine board's contact height; the rack akja is pitched to share it (the kit
# akja's rest line is 37 mm higher at its native 75 deg: one 40 mm round bar cannot carry both)
P1_RACK_BAR_TOP_FINE = round(contact_z("SkiDressSpineBoard"), 4)                       # 1.838
P1_RACK_AKJA_PITCH = pitch_for_rest_height("SkiDressAkja", KIT_REST["SkiDressSpineBoard"][0])
P1_RACK_FACE = P1_X[0] - P1_RACK_DEPTH - P1_RACK_BAR / 2      # authoring x of the bar's front (-2.14)
P1_LEANING_FINE = tuple(f"pole_{k}" for k in range(6))         # the modelled leaning parts left
P1_SIGN_SET = SignSetSpec(prop="SkiSignSetPatrolHut", letter="B", host="p1", owner=False,
                          faces=(("sign_p1name_face", "p1.name"), ("sign_p1info_face", "p1.info")))


def _p1_rows():
    """(legacy rows, fine rows) of P1 in game local (gx, gy, gz) = authoring (y, z, x)."""
    legacy = (WALL_LIGHT + ((P1_WALL_LIGHT[0], P1_WALL_LIGHT[1], P1_X[0]), Q_FACE["-X"], 0),
              AC_VENT + ((P1_Y[1], P1_VENT[1], P1_VENT[0]), Q_FACE["+Y"], 0))
    fine = (
        # the door lantern lamp_p1door (+ arm) -> a short-range wall light at its lens height (H4)
        WALL_LIGHT + ((P1_LAMP[0], P1_LAMP[1], P1_X[1]), Q_FACE["+X"], 0),
        # akja_p1 (+Y gable end, 2.2 m at 75 deg) -> the kit akja on the same wall (authoring +Y = game +X)
        lean_row("SkiDressAkja", "wall_p1", "+X", P1_Y[1], 0.5, 0.0, replaces="akja_p1"),
        # akja_p1b / gear_p1board (back-wall rack, tops on its upper bar) -> kit rows on the same bar
        # (authoring -X = game -Z); the akja pitched P1_RACK_AKJA_PITCH so both rest lines meet the bar
        lean_row("SkiDressAkja", "tobrack_p1", "-Z", P1_RACK_FACE, P1_AKJA[0], 0.0,
                 pitch_deg=P1_RACK_AKJA_PITCH, replaces="akja_p1b"),
        lean_row("SkiDressSpineBoard", "tobrack_p1", "-Z", P1_RACK_FACE, P1_BACKBOARD[0], 0.0,
                 replaces="gear_p1board"),
    )
    return legacy, fine


P1_ROWS_LEGACY, P1_ROWS_FINE = _p1_rows()
P1_LEAN_NAMES = tuple(sorted(r["lean"]["replaces"] for r in P1_ROWS_FINE if isinstance(r, dict) and "lean" in r))


def lean_rows(recipe):
    """The recipe's dressing-kit LEAN rows (mod rows of a KIT prop with mount "lean")."""
    return tuple(r for r in recipe.sub_objects if isinstance(r, dict) and r.get("kind") == "mod"
                 and KIT.get(r.get("prefab")) is not None and KIT[r["prefab"]].mount == "lean")


def build_p1(recipe, opts):
    import cs2_bldg as K
    import cs2_kit
    C = load_civic()
    from cs2_bldg import RoofSpec, WallRef, WinSpec
    fine = bool(getattr(recipe, "fine_detail", False))       # fine-detail step 3d (docstring)
    lsegs = LOG_SEGS_FINE if fine else 6
    x0, x1 = P1_X
    y0, y1 = P1_Y
    P = []
    P += K.storey("p1", x0, x1, y0, y1, 0.0, P1_EAVE, "timber_vboard", ground=True, drop=("+Z",))
    spec = RoofSpec("gable", x0, x1, y0, y1, P1_EAVE, P1_PITCH, axis="Y", overhang=P1_OVER,
                    rake_over=P1_RAKE_OVER, thick=P1_SLAB, mat="metal_roof_s1", trim="trim")
    P += K.gable_roof("p1", spec, fascia=P1_FASCIA, rake=P1_FASCIA, ridge_cap=P1_RIDGE_CAP,
                      walls=True, wall_mat="timber_vboard", ext=0.12)
    # log corner posts (ground, open bottom) and the front / back log plates
    for k, (px, py) in enumerate((x, y) for x in (x0, x1) for y in (y0, y1)):
        P += C.cyl_part(f"logpost_{k}", (px, py, 0.0), (px, py, P1_POST_TOP), P1_POST_R,
                        "timber_heavy", segs=lsegs, cap0=False)
    for tag, px in (("front", x1 + 0.05), ("back", x0 - 0.05)):
        P += C.cyl_part(f"logplate_{tag}", (px, y0 + P1_POST_R, P1_PLATE_Z),
                        (px, y1 - P1_POST_R, P1_PLATE_Z), P1_PLATE_R, "timber_heavy", segs=lsegs)
    front = WallRef("+X", x1)
    # door on the step (kit door; its head piece goes: the log plate is the header)
    dy, dw, dh = P1_DOOR
    sx0, sx1, sy0, sy1, sz = P1_STEP
    P += K.box_part("step_p1", (sx0, sy0, 0.0), (sx1, sy1, sz), "concrete", ground=True,
                    drop=("-X",))
    for o, _q in cs2_kit.door("p1", w=dw, h=dh, loc=(x1, dy, sz), facing="+X"):
        if o.name.endswith("_head"):
            bpy_remove(o)
            continue
        P.append((o, "trim"))          # slot-1 green door: reads as a door on the boards
    for k, sg in enumerate((-1, 1)):
        yy = dy + sg * (dw / 2 + 0.06 + 0.08 + 0.04)
        P += C.cyl_part(f"logframe_{k}", (x1 + 0.06, yy, 0.02), (x1 + 0.06, yy, P1_PLATE_Z),
                        0.08, "timber_heavy", segs=lsegs)
    # window -> _Win (M2), frame members varnished log colour, no head board (the plate)
    wy, ww, wh, ws = P1_WIN
    P += K.window_bay("p1", front, wy - ww / 2, wy + ww / 2, WinSpec(ww, wh, ws, lights=2, rows=3),
                      room="hut", head_board=False)
    # cross disc (front-only face, M3) and the info board
    cy_, cz, cd = P1_DISC
    P += K.emblem_disc("p1", front, cy_, cz, cd, t=0.04, segs=16)
    by, bz, bw, bh = P1_BOARD
    P += K.sign_board("p1info", front, by, bz, bw, bh, t=0.05, mat="timber_heavy", frame=0.05)
    # entry rail (kit handrail, slot-1 green), door lamp, stovepipe
    P += K.rail("entry", list(P1_RAIL), h=1.0, kind="tube")
    if not fine:                         # fine: a WallLightShortRangeRandom01 row (P1_ROWS_FINE)
        ly, lz = P1_LAMP
        lp, _pt = K.lantern("p1door", (x1, ly, lz), "+X", kind="wall", h=0.35, w=0.20)
        P += lp
    P += K.stovepipe("p1", spec, -1.2, 1.5, r=0.08, rise=0.95)
    # gear: standing fence rolls (closed, z 0.02: kit ground-band rule), bamboo poles
    # leaning on the front wall, a toboggan against the +Y gable end
    for k, (rx, ry) in enumerate(P1_ROLLS):
        P += C.cyl_part(f"roll_{k}", (rx, ry, 0.02), (rx, ry, 0.02 + P1_ROLL_H), P1_ROLL_R,
                        "accent", segs=8)
    for k, py in enumerate(P1_POLES_Y):          # lean fix: tops on the wall, feet on grade
        P += (lean_pole_fine if fine else lean_pole)(C, f"pole_{k}", (2.45 + 0.02 * (k % 2), py), x1,
                                                     P1_POLE_TOP, 0.015, "deck")
    if not fine:                         # fine: the kit akja row on the same wall (P1_ROWS_FINE)
        P += K.akja("p1", (0.5, y1 + 0.57, 0.02), "+Y", lean_deg=75.0, length=2.2, width=0.65)
    P += p1_detail(K, spec, fine)
    P = C.remat(P, (("sill_", "timber_heavy"), ("jamb_", "timber_heavy"), ("mull_", "timber_heavy"),
                    ("emblem_p1_rim", "trim_white")))
    C.assert_lot_margin(P, 4.0 * P1_LOT[1], 4.0 * P1_LOT[0], 0.25, "P1")    # M1
    C.check_coplanar(P, exempt=KIT_INTERNAL)
    # corner posts / plates are half-embedded in the wall corners by design (photo)
    C.check_inside(P, [((x0, y0, 0.0), (x1, y1, P1_EAVE))], exempt=("logpost_", "logplate_"))
    L = load_lean()
    L.check(P, LEAN_PREFIXES, "P1", expect=P1_LEANING_FINE if fine else P1_LEANING)   # CONTRACT LEAN
    if fine:                             # ... and on the kit rows' REAL meshes (same supports)
        L.check_rows(P, lean_rows(recipe), "P1", LEAN_PREFIXES, expect=P1_LEAN_NAMES)
    return P


def p1_detail(K, spec, fine=False):
    """Detail pass 1 dressing of P1 (design 3.3 as corrected; see the module docstring). fine
    (fine-detail step 3d): the rack's round top bar at the kit props' rest line, no modelled akja /
    spine board (kit rows), 12-seg radio mast, planked bench."""
    from cs2_bldg import WallRef
    x0, _x1 = P1_X
    y0, y1 = P1_Y
    fk = {"fine": True} if fine else {}
    P = []
    # P1-1 "SKI" / "PATROL" board on the -Y end (M4 fix) and P1-5 the bench under it
    u, z, w, h = P1_NAME
    P += K.sign_board("p1name", WallRef("-Y", y0), u, z, w, h, t=P1_NAME_T, mat="timber_heavy")
    bx, bl = P1_BENCH
    P += K.bench("p1", bx, y0, "-Y", length=bl, back=False, **fk)
    # P1-2 roof cross on the +X slope: the square's upper edge P1_CROSS_GAP (plan) below the
    # ridge cap's real edge (skeptic S3 lift 0.03 is the kit default)
    t = math.tan(math.radians(P1_PITCH))
    s0 = (ridge_cap_half(P1_RIDGE_CAP[0]) + P1_CROSS_GAP) * math.sqrt(1.0 + t * t)
    P += K.roof_emblem("p1roof", spec, "+X", P1_CROSS, s0=s0)
    # P1-6 snow guard over the door, below the cross
    P += K.snow_guard("p1E", spec, "+X", offset=P1_GUARD_OFF)
    # P1-3 radio mast + red beacon on the +Y end
    mx, my, mz0, mz1 = P1_MAST
    br = tuple((bz, "-Y", round(my - y1, 4)) for bz in P1_MAST_BR)
    P += K.radio_mast("p1", mx, my, mz0, mz1, brackets=br, **fk)
    # P1-4 toboggan rack on the back wall; the akja and the spine board rest their TOPS on
    # its upper bar (lean fix; was S2a / S2b mid-length: the tops stood 0.10 / 0.14 m off the
    # wall above the bar). The foot is derived so the top back edge clears the bar face by 1 cm
    ru0, ru1, bars = P1_RACK
    if fine:                 # the kit rows' rest line (P1_ROWS_FINE); the bar a round 8-seg tube
        bars = (bars[0], P1_RACK_BAR_TOP_FINE)
    P += K.toboggan_rack("p1", ("-X", x0), ru0, ru1, bars, depth=P1_RACK_DEPTH, bar=P1_RACK_BAR,
                         **fk)
    if fine:
        _sx0, sx1, sy0, sy1, sz = P1_STEP
        P += K.safety_stripe("p1step", (sx1 - 0.025, sy0 + 0.005), (sx1 - 0.025, sy1 - 0.005), sz)
        return P
    face = x0 - P1_RACK_DEPTH - P1_RACK_BAR / 2
    ay, alean, alen = P1_AKJA
    ax = top_rest_foot(alean, alen, face, REST_GAP, -1.0)
    P += K.akja("p1b", (ax, ay, K.GROUND_LIFT), "-X", lean_deg=alean, length=alen)
    gy, glean, glen = P1_BACKBOARD
    gx = top_rest_foot(glean, glen, face, REST_GAP, -1.0)
    P += K.backboard("p1", (gx, gy, K.GROUND_LIFT), "-X", lean_deg=glean, length=glen)
    # P1-7 step nosing stripe
    _sx0, sx1, sy0, sy1, sz = P1_STEP
    P += K.safety_stripe("p1step", (sx1 - 0.025, sy0 + 0.005), (sx1 - 0.025, sy1 - 0.005), sz)
    return P


def bpy_remove(o):
    import bpy
    me = o.data
    bpy.data.objects.remove(o, do_unlink=True)
    if me is not None and me.users == 0:
        bpy.data.meshes.remove(me)


class _Lazy:
    """Defers loading _civic (bpy) until the bake calls the painter."""

    def __init__(self, cls, *args):
        self.cls, self.args = cls, args
        self.__name__ = f"{cls}_{args[0]}"

    def __call__(self, rect, base, ctl):
        return getattr(load_civic(), self.cls)(*self.args)(rect, base, ctl)


ROOF_CROSS = {"draws": (("cross_square", {}),), "symmetric": True}

P1 = FineRecipe(
    key="P1", aliases=("patrol-summit", "p1"), letter="A",
    asset="SkiPatrolHutCommercialLow", prefab="SkiPatrolHut", category="buildings",
    lot=P1_LOT, base_mode="engine", plinth=None, yaw_deg=0.0, tex=1024, allow_4096=False,
    budget=Budget(1500, 2000, 650, 200, 200),
    massing="custom", builder=build_p1,
    rooms={"hut": (2, 2)}, interior="CommercialLow", slots=SLOTS,
    ground_prefixes=("wall_p1", "logpost_", "step_p1"),
    arrival_prefixes=("door_p1", "emblem_p1_", "step_p1"),
    lod1_extra=("pole_", "stovepipe_", "bench_"),
    lod2_extra=("pole_", "stovepipe_", "logplate_", "logframe_", "roll_", "soffit_", "bench_"),
    lod2_keep=("emblem_p1_face", "emblem_p1_rim", "emblem_p1roof_face"),
    labels=(("emblem_p1_face", "paint_cross_disc", {}),
            ("sign_p1info_face", _Lazy("BarsPainter", "sign_p1info_face"), {}),
            ("sign_p1name_face", "display",
             {"draws": (("text", {"lines": ("SKI", "PATROL"), "bg": PATROL_RED}),)}),
            ("emblem_p1roof_face", "display", ROOF_CROSS)),
    cull_ok=("soffit_",),   # eave undersides at ~2.3-3 m: the kit's height-aware cull may take them
    views=(("front_closeup", (0.25, -1.0, 0.25)),),
    weights=(("sign_p1name_face", 1.75), ("emblem_p1roof_face", 1.0), ("lamp_p1beacon", 1.5),
             ("bench_", 0.5)),
    weather={"intensity": 0.40, "overrides": (    # sweep after the dressing: 0.45 VL1 0.0554
        ("akja_", {"role": "pad"}), ("roll_", {"role": "pad", "scale": 0.7}),
        ("pole_", {"role": "timber"}), ("tobrack_", {"role": "galv"}), ("gear_", {"role": "pad"}),
        ("mast_", {"role": "galv"}), ("logpost_", {"role": "timber", "edge": 1.2}))},
    sub_objects=P1_ROWS_LEGACY + P1_ROWS_FINE,
    kit_colour_set=KIT_COLOUR_SET,
    notes="civic-sign contract 3 (P1) + skeptic M1/M2/M3; detail pass 1 (design 3.3 + skeptic); "
          "fine-detail step 3d (sign set, kit lean rows, vanilla door light, fine geometry)",
    # fine-detail step 3d (opt-in; lodge.py --no-fine restores `legacy` = the approved hut)
    fine_detail=True,
    sign_set=P1_SIGN_SET,
    legacy=(("sub_objects", P1_ROWS_LEGACY),),
)


# ======================================================================================
# P2 - patrol HQ on piers (contract 3.2, lot 2 x 2 per R22 -> X +-8, Y +-8)
# ======================================================================================

P2_LOT = (2, 2)
P2_X = (-3.70, 2.30)                 # 6.0 m deep (Q11 alternative with the 2 x 2 lot)
P2_Y = (-3.90, 4.20)                 # lean-to -3.9..-2.0, main volume -2.0..4.2
P2_FLOOR = (1.10, 1.30)
P2_RIDGE_Y, P2_APEX_TOP, P2_PITCH, P2_SLAB = 1.30, 6.20, 28.0, 0.15
P2_OVER, P2_RAKE_OVER = 0.85, 0.40
P2_RIDGE_CAP = (0.30, 0.10)
P2_GABLE_T, P2_WALL_T = 0.30, 0.15
P2_DECK = (2.30, 4.10, -3.90, 5.00)
P2_POST_X = 4.00
P2_POSTS_Y = (-3.85, -2.0, 0.67, 3.15, 4.90)
P2_BAR_Z = (0.46, 0.76, 1.06)
P2_FOOTINGS = tuple((x, y) for x in (-3.45, 2.05) for y in (-3.65, 0.25, 3.95)) + \
    ((3.90, -1.0), (3.90, 4.70))
P2_STAIR_X = 3.15
P2_STAIR = (P2_STAIR_X, -5.031, -3.911, 1.2, 0.33)   # x, foot y, top y, width, rise_max
P2_WIN = (-0.67, 3.05, 1.96, 1.85)    # window wall u0, u1, sill, h (4 x 2)
P2_UPPER = (0.32, 1.99, 4.01, 0.70)   # upper window (2 lights), sits on the wall's head board
P2_DOOR = (-2.57, 0.92, 2.03)
P2_DISC = (1.30, 5.30, 0.90, 0.13)    # y, z, D, proud (in front of the split collar)
P2_COLLAR_Z = 5.04
# ---- detail pass 1 (design 3.4 / 5.2; skeptic M4 / M6 / M7 / M8 / M9 / S6; kit report 5) ----
P2_NAME = (0.7, 2.20, 4.60, 1.40)    # +Y wall board: u (x -0.7), z, w, h (M4 fix b: T1 at w
#                                      2.25; x -3.00..1.60, clear of the knee brackets)
P2_CROSS = (2.20, (-0.70, 2.97))     # +Y slope roof cross: side, plan centre (y 2.00..3.94)
P2_MAST = (-4.40, 1.30, 3.6, 8.6)    # back gable: 0.14 m outside the rake board (x -4.22)
P2_MAST_BR = (4.2, 5.0)
P2_FLAG = (3.55, 4.75, 7.3, (1.2, 0.8))   # deck NE corner: x, y, h (top on grade), cloth
P2_RACK = (-3.85, -3.15, (3.18,))    # +X wall strip south of the door (u = y). Lean fix: the
#                                      bar (was z 2.4, mid-length) is the akja's TOP rest
#                                      (top z 3.18 on the 3.16-3.20 bar face; the lean-to
#                                      underside is >= 3.29 over the rack)
P2_RACK_DEPTH, P2_RACK_BAR = 0.12, 0.04
P2_AKJA = (-3.5, 82.0, 1.9)          # y, lean, length (M9 "+X"); 82 deg (design 80), 1.9 m:
#                                      top z 3.18, 0.11 under the lean-to slab at y -3.85
P2_TOBOGGANS = (("a", 0.55), ("b", 1.35))   # lean fix: x of the two toboggans that leaned on
#                                      the stair's open side; they lean on the -Y wall
P2_TOBOGGAN = (72.0, 2.2, 0.65)      # lean, length, width (unchanged): top z 2.11 on wall_hqs
P2_LEANING = ("akja_hqa", "akja_hqb", "akja_hqc", "gear_hqboard")
P2_BACKBOARD = (2.37, 3.52, 88.0)    # foot x, y, lean (M9 "+X"; top 5 mm off the wall)
P2_REEL = (-1.95, 1.75)              # +Y wall: u (x 1.95), centre z (the design's u -1.80 / z
#                                      1.90 flange cut the 4.6 m board frame's corner)
P2_LOGS = (2.33, 2.73, -0.6, 2.4, 1.75)   # x0, x1, y0, y1, top: 0.05 under the sill BOARD
#                                      (sill_hqw z 1.80..1.96 reaches x 2.45)
P2_GUARDS = (("hqN", "+Y", 0.6), ("hqS", "-Y", 0.4))
P2_GUTTER = (-4.95, 2.95)            # lean-to eave: y (on the fascia face y -4.88), top z
P2_SPOUT = (-3.6, -4.0)              # downspout pipe line (x, y) on the -Y wall; a leader
#                                      from the gutter outlet passes under the fascia
P2_WALL_LIGHTS = ((3.55, 3.35), (-0.70, 3.85))   # +X wall (y, z) deck end; +Y wall (x, z)
P2_VENT = (-2.5, 2.4)                # -Y wall (x, z)
P2_BENCH = (3.30, 1.0)               # deck (x, y): 0.07 clear of the log face, 0.10 of logpost_2
# ---- fine-detail step 3d (module docstring) ------------------------------------------------------------
# the front rack's bar at the kit akja's contact height on the deck (native 75 deg; was 3.18 for the
# modelled 1.9 m / 82 deg akja); the kit akja's top 3.239 stays 0.05 under the lean-to slab (>= 3.29)
P2_RACK_BAR_FINE = round(P2_FLOOR[1] + contact_z("SkiDressAkja"), 4)                   # 3.1753
P2_RACK_FACE = P2_X[1] + P2_RACK_DEPTH + P2_RACK_BAR / 2      # authoring x of the bar's front (2.44)
# the kit hose reel's plate (0.70 m square, pivot = plate bottom on the wall) under the name board's frame
# (bottom z 2.14): the kit rule "old centre height - 0.35" (1.40) leaves 0.04 m; 1.35 leaves 0.09 (D10 0.05)
P2_REEL_FINE_Y0 = 1.35
P2_LEANING_FINE = ()                 # every P2 leaning part is a kit row
P2_SIGN_SET = SignSetSpec(prop="SkiSignSetPatrolHQ", letter="B", host="p2", owner=False,
                          faces=(("sign_hqname_face", "p2.name"),))


def _p2_rows():
    """(legacy rows, fine rows) of P2 in game local (gx, gy, gz) = authoring (y, z, x)."""
    legacy = (
        BENCH + ((P2_BENCH[1], P2_FLOOR[1], P2_BENCH[0]), Q_FACE["+X"], 0),
        WALL_LIGHT + ((P2_WALL_LIGHTS[0][0], P2_WALL_LIGHTS[0][1], P2_X[1]), Q_FACE["+X"], 0),
        WALL_LIGHT + ((P2_Y[1], P2_WALL_LIGHTS[1][1], P2_WALL_LIGHTS[1][0]), Q_FACE["+Y"], 0),
        AC_VENT + ((P2_Y[0], P2_VENT[1], P2_VENT[0]), Q_FACE["-Y"], 0))
    f1 = P2_FLOOR[1]
    fine = (
        # the door lantern lamp_hqdoor (+ arm, lens under the lean-to eave) -> a short-range wall light
        WALL_LIGHT + ((P2_DOOR[0], 3.70, P2_X[1]), Q_FACE["+X"], 0),
        # the two toboggans on the lean-to's -Y wall (authoring -Y = game -X), feet on grade
        lean_row("SkiDressAkja", "wall_hqs", "-X", P2_Y[0], P2_TOBOGGANS[0][1], 0.0, replaces="akja_hqa"),
        lean_row("SkiDressAkja", "wall_hqs", "-X", P2_Y[0], P2_TOBOGGANS[1][1], 0.0, replaces="akja_hqb"),
        # the front rack's akja on the deck, its top on the rack bar (authoring +X = game +Z)
        lean_row("SkiDressAkja", "tobrack_hq", "+Z", P2_RACK_FACE, P2_AKJA[0], f1, replaces="akja_hqc"),
        # the spine board on the front gable, on the deck (the kit board leans 85 deg; the modelled 88)
        lean_row("SkiDressSpineBoard", "gable_hqfront", "+Z", P2_X[1], P2_BACKBOARD[1], f1,
                 replaces="gear_hqboard"),
        # the fire-hose reel on the +Y wall (authoring +Y = game +X), x 1.95 as the modelled reel
        wall_row("SkiDressHoseReel", (P2_Y[1], P2_REEL_FINE_Y0, -P2_REEL[0]), "+X",
                 note="replaces hosereel_hq (+Y wall; plate bottom 1.35, under the name board frame)"),
    )
    return legacy, fine


P2_ROWS_LEGACY, P2_ROWS_FINE = _p2_rows()
P2_LEAN_NAMES = tuple(sorted(r["lean"]["replaces"] for r in P2_ROWS_FINE if isinstance(r, dict) and "lean" in r))


def p2_roof_numbers():
    t = math.tan(math.radians(P2_PITCH))
    half = 0.5 * P2_SLAB / math.cos(math.radians(P2_PITCH))
    y0, y1 = P2_Y
    eave0 = P2_APEX_TOP - (P2_RIDGE_Y - y0) * t - half          # centre plane at y0
    eave1 = P2_APEX_TOP - (y1 - P2_RIDGE_Y) * t - half          # centre plane at y1
    return dict(t=t, half=half, eave0=eave0, eave1=eave1,
                under0=eave0 - half, under1=eave1 - half, under_apex=P2_APEX_TOP - 2 * half)


def p2_under(y):
    """Roof underside z at across-coordinate y (the gable top line)."""
    r = p2_roof_numbers()
    return r["under_apex"] - abs(y - P2_RIDGE_Y) * r["t"]


def p2_treads():
    """(y of the front edge, top z) of each stair tread as cs2_bldg.stair builds it (n =
    ceil(rise / rise_max) risers; the treads overrun their run by 0.01 m at both ends)."""
    _x, fy, ty, _w, rmax = P2_STAIR
    f1 = P2_FLOOR[1]
    n = max(1, int(math.ceil(f1 / rmax - 1e-9)))
    tread = (ty - fy) / n
    return [(fy + k * tread - 0.01, (k + 1) * f1 / n) for k in range(n)]


def build_p2(recipe, opts):
    import cs2_bldg as K
    import cs2_kit
    C = load_civic()
    from cs2_bldg import RoofSpec, WallRef, WinSpec
    fine = bool(getattr(recipe, "fine_detail", False))       # fine-detail step 3d (docstring)
    x0, x1 = P2_X
    y0, y1 = P2_Y
    f0, f1 = P2_FLOOR
    R = p2_roof_numbers()
    P = []
    # ---- floor platform, gable walls (pentagons to the floor), eave walls between ------
    # detail pass 1 (design 5.2): the floor underside is its own soffit_ part (w 0.25 by the
    # kit rule instead of the deck 0.75) and the gable prisms lose their inner end faces
    P += K.box_part("floor_hq", (x0, y0, f0), (x1, y1, f1), "deck", drop=("+Z", "-Z"))
    P += C.quads_part("soffit_hqfloor", [([(x0, y0, f0), (x1, y0, f0), (x1, y1, f0), (x0, y1, f0)],
                                          (0.0, 0.0, -1.0))], "deck")
    for tag, a0, a1, inner in (("front", x1 - P2_GABLE_T, x1, "a0"),
                               ("back", x0, x0 + P2_GABLE_T, "a1")):
        P += K.prism_gable(f"hq{tag}", "X", a0, a1, y0, y1, R["under0"], R["under_apex"],
                           ridge_at=P2_RIDGE_Y, eave_z1=R["under1"], mat="timber_vboard",
                           base_z=f1, inner=inner)
    gx0, gx1 = x0 + P2_GABLE_T, x1 - P2_GABLE_T
    P += K.box_part("wall_hqs", (gx0, y0, f1), (gx1, y0 + P2_WALL_T, R["under0"]),
                    "timber_vboard", drop=("-Z", "+Z", "+Y", "-X", "+X"))
    P += K.box_part("wall_hqn", (gx0, y1 - P2_WALL_T, f1), (gx1, y1, R["under1"]),
                    "timber_vboard", drop=("-Z", "+Z", "-Y", "-X", "+X"))
    # ---- roof (kit gable_roof, offset ridge, same pitch both sides) -------------------
    spec = RoofSpec("gable", x0, x1, y0, y1, R["eave0"], P2_PITCH, axis="X", overhang=P2_OVER,
                    rake_over=P2_RAKE_OVER, ridge_at=P2_RIDGE_Y, eave_z1=R["eave1"],
                    thick=P2_SLAB, mat="slate", trim="trim")
    P += K.gable_roof("hq", spec, fascia=0.25, rake=0.20, ridge_cap=P2_RIDGE_CAP, walls=False,
                      ext=0.15)
    # ---- front gable: window wall, upper window, door + glazed lite, split collar, struts
    front = WallRef("+X", x1)
    wu0, wu1, ws, wh = P2_WIN
    P += K.window_bay("hqw", front, wu0, wu1, WinSpec(wu1 - wu0, wh, ws, lights=4, rows=2),
                      room="hq")
    uu0, uu1, us, uh = P2_UPPER
    P += K.window_bay("hqu", front, uu0, uu1, WinSpec(uu1 - uu0, uh, us, lights=2), room="hq",
                      sill_board=False)
    dy, dw, dh = P2_DOOR
    for o, _q in cs2_kit.door("hq", w=dw, h=dh, loc=(x1, dy, f1), facing="+X"):
        P.append((o, "trim_white"))
    P += K.pane_quad("win_hq_doorlite", front, dy - 0.28, dy + 0.28, f1 + 1.00, f1 + 1.85,
                     d=0.035)
    cy_, cz, cd, ct = P2_DISC
    P += K.emblem_disc("hq", front, cy_, cz, cd, t=ct, segs=16)
    xb = x1 + 0.05
    lo_y, hi_y = P2_RIDGE_Y - (P2_COLLAR_Z - R["under_apex"]) / -R["t"], \
        P2_RIDGE_Y + (P2_COLLAR_Z - R["under_apex"]) / -R["t"]
    P += K.beam("collarL", (xb, lo_y, P2_COLLAR_Z), (xb, cy_ - 0.35, P2_COLLAR_Z), 0.10, 0.20,
                mat="trim")
    P += K.beam("collarR", (xb, cy_ + 0.35, P2_COLLAR_Z), (xb, hi_y, P2_COLLAR_Z), 0.10, 0.20,
                mat="trim")
    for tag, ya, yb in (("L", -0.20, 0.55), ("R", 2.80, 2.05)):
        zb = p2_under(yb) - 0.09
        P += K.beam(f"strut{tag}", (xb, ya, P2_COLLAR_Z + 0.10), (xb, yb, zb), 0.08, 0.12,
                    mat="trim")       # 0.08 deep: no face coplanar with the 0.10 collar
    # knee brackets under the long +Y eave
    for tag, bx in (("f", x1 - 0.15), ("b", x0 + 0.15)):
        P += K.bracket(f"hq{tag}", (bx, y1, 3.95), "+Y", span=0.70, drop=0.80, t=0.12)
    # door lamp (lens under the lean-to eave), stovepipe through the long slope
    if not fine:                         # fine: a WallLightShortRangeRandom01 row (P2_ROWS_FINE)
        lp, _pt = K.lantern("hqdoor", (x1, dy, 3.70), "+X", kind="wall", h=0.35, w=0.20)
        P += lp
    P += K.stovepipe("hq", spec, -1.5, -2.5, r=0.08, rise=1.2)
    # ---- deck, joists, front beam, stair (ground stringers), log posts, steel bars ------
    dx0, dx1, dy0, dy1 = P2_DECK
    P += K.deck("hq", dx0, dx1, dy0, dy1, f1, support="none", fascia=0.0, board_dir="Y",
                thick=0.05, soffit=True)          # design 5.2: underside -> soffit_deckhq
    jz0, jz1 = f1 - 0.25, f1 - 0.05
    P += C.new_part("joist_hq", lambda bm: [
        C.box_into(bm, (dx0, dy0, jz0), (dx1, dy0 + 0.05, jz1)),
        C.box_into(bm, (dx0, dy1 - 0.05, jz0), (dx1, dy1, jz1)),
        C.box_into(bm, (3.82, dy0 + 0.05, jz0), (3.98, dy1 - 0.05, jz1), ("-Y", "+Y"))], "deck")
    # kit treads overrun their run by 0.01 m: stop 0.011 short of the deck (no coplanar
    # overlap of the top tread with the deck boards)
    sx, sfy, sty, sw, srm = P2_STAIR
    P += K.stair("hq", (sx, sfy, 0.0), (sx, sty, f1), w=sw, rise_max=srm,
                 ground=True, rail="none", tread_mat="deck", stringer_mat="deck")
    for k, py in enumerate(P2_POSTS_Y):
        P += C.cyl_part(f"logpost_{k}", (P2_POST_X, py, f1), (P2_POST_X, py, f1 + 1.30), 0.11,
                        "timber_shake", segs=LOG_SEGS_FINE if fine else 6, cap0=False)
    bars = []
    for zb in P2_BAR_Z:
        z = f1 + zb
        bars.append(((P2_POST_X - 0.01, -2.0, z - 0.01), (P2_POST_X + 0.01, 4.90, z + 0.01), ()))
        bars.append(((dx0, 4.89, z - 0.01), (P2_POST_X - 0.012, 4.91, z + 0.01), ()))
    P += C.new_part("bar_hq", lambda bm: [C.box_into(bm, lo, hi, d) for lo, hi, d in bars],
                    "timber_heavy")
    # ---- footings (ground) + timber stilts --------------------------------------------
    for k, (px, py) in enumerate(P2_FOOTINGS):
        P += K.box_part(f"footing_{k}", (px - 0.20, py - 0.20, 0.0), (px + 0.20, py + 0.20, 0.45),
                        "concrete", ground=True)
        top = f0 if px < x1 else jz0
        P += K.box_part(f"stilt_{k}", (px - 0.075, py - 0.075, 0.45), (px + 0.075, py + 0.075, top),
                        "timber_heavy", drop=("-Z", "+Z"))
    # ---- toboggans leaning on the lean-to's -Y wall (lean fix: they leaned on the stair's
    # open side, no stringer near their tops); the top back edge REST_GAP off wall_hqs ----
    tlean, tlen, tw = P2_TOBOGGAN
    ty = top_rest_foot(tlean, tlen, y0, REST_GAP, -1.0)
    for tag, tx in (() if fine else P2_TOBOGGANS):      # fine: kit akja rows on the same wall
        P += K.akja(f"hq{tag}", (tx, ty, K.GROUND_LIFT), "-Y", lean_deg=tlean, length=tlen,
                    width=tw)
    P += p2_detail(K, spec, fine)
    P = C.remat(P, (("emblem_hq_rim", "trim_white"), ("lamp_hqdoorarm", "trim")))
    C.assert_lot_margin(P, 4.0 * P2_LOT[1], 4.0 * P2_LOT[0], 0.25, "P2")
    C.check_coplanar(P, exempt=KIT_INTERNAL)
    C.check_inside(P, [((x0, y0, f1), (x1, y1, R["under0"])), ((x0, y0, f0), (x1, y1, f1))],
                   exempt=("floor_hq", "gable_hq"))
    L = load_lean()
    L.check(P, LEAN_PREFIXES, "P2", expect=P2_LEANING_FINE if fine else P2_LEANING)   # CONTRACT LEAN
    if fine:                             # ... and on the kit rows' REAL meshes (same supports)
        L.check_rows(P, lean_rows(recipe), "P2", LEAN_PREFIXES, expect=P2_LEAN_NAMES)
    return P


def p2_detail(K, spec, fine=False):
    """Detail pass 1 dressing of P2 (design 3.4 as corrected; see the module docstring). fine
    (fine-detail step 3d): 12-seg radio mast, 12-seg flag mast + finial, the rack's round bar at the kit
    akja's rest line, no modelled akja / spine board / hose reel (kit rows)."""
    fk = {"fine": True} if fine else {}
    from cs2_bldg import WallRef
    x0, x1 = P2_X
    y0, y1 = P2_Y
    f1 = P2_FLOOR[1]
    P = []
    # P2-1 "SKI PATROL" / "FIRST AID" board on the +Y wall (M4 fix b)
    u, z, w, h = P2_NAME
    P += K.sign_board("hqname", WallRef("+Y", y1), u, z, w, h, mat="timber_heavy")
    # P2-2 roof cross on the +Y slope (lod2_keep; M7: the disc leaves lod2_keep)
    side, centre = P2_CROSS
    P += K.roof_emblem("hqroof", spec, "+Y", side, centre=centre)
    # P2-3 radio mast + beacon on the back gable (kept to LOD1)
    mx, my, mz0, mz1 = P2_MAST
    br = tuple((bz, "+X", round(x0 - mx, 4)) for bz in P2_MAST_BR)
    P += K.radio_mast("hq", mx, my, mz0, mz1, brackets=br, **fk)
    # P2-4 patrol flag on the deck NE corner (cloth = two label quads, M8 lod1_extra)
    fx, fy, fh, cloth = P2_FLAG
    P += K.flag_mast("hq", fx, fy, h=fh, flag=cloth, z0=f1, along="Y", face=True, **fk)
    # P2-5 toboggan rack on the front strip south of the door; akja + spine board face "+X"
    # (M9: the outward normal of the wall they lean on); the akja's TOP rests on the bar
    # (lean fix: it rested mid-length with its top 0.044 m off the wall)
    ru0, ru1, bars = P2_RACK
    if fine:
        bars = (P2_RACK_BAR_FINE,)
    P += K.toboggan_rack("hq", ("+X", x1), ru0, ru1, bars, depth=P2_RACK_DEPTH, bar=P2_RACK_BAR,
                         **fk)
    if not fine:                         # fine: kit rows (P2_ROWS_FINE)
        face = x1 + P2_RACK_DEPTH + P2_RACK_BAR / 2
        ay, alean, alen = P2_AKJA
        ax = top_rest_foot(alean, alen, face, REST_GAP, 1.0)
        P += K.akja("hqc", (ax, ay, f1), "+X", lean_deg=alean, length=alen)
        bx, by, blean = P2_BACKBOARD
        P += K.backboard("hq", (bx, by, f1), "+X", lean_deg=blean)
        # P2-6 fire-hose reel on the +Y wall (fine: a SkiDressHoseReel row)
        ru, rz = P2_REEL
        P += K.hose_reel("hq", ("+Y", y1), ru, rz)
    # P2-7 firewood under the window wall
    lx0, lx1, ly0, ly1, lz1 = P2_LOGS
    P += K.log_stack("hq", lx0, lx1, ly0, ly1, f1, lz1, "+X")
    # P2-8 snow guards (N slope; S over the door)
    for tag, sd, off in P2_GUARDS:
        P += K.snow_guard(tag, spec, sd, offset=off)
    # P2-9 lean-to gutter on the fascia + a leader downspout (outlet under the gutter, offset
    # leg under the fascia to the -Y wall: the kit's plain elbow would cut the 0.25 m fascia)
    gy, gz = P2_GUTTER
    P += K.gutter("hqS", (x0 - P2_RAKE_OVER, gy), (x1 + P2_RAKE_OVER, gy), gz)
    sxp, syp = P2_SPOUT
    P += K.downspout("hqS", sxp, gy, gz, "-Y", leader=(sxp, syp))
    # P2-10 stair stripes: the four tread nosings (the top tread is level with the deck)
    sx, _fy, _ty, sw, _rm = P2_STAIR
    for k, (yf, zt) in enumerate(p2_treads()):
        P += K.safety_stripe(f"hq{k}", (sx - sw / 2 + 0.005, yf + 0.025),
                             (sx + sw / 2 - 0.005, yf + 0.025), zt)
    return P


FLAG_CROSS = {"draws": (("shapes", {"items": (("cross", 0.5, 0.5, 0.70, WHITE),),
                                    "bg": PATROL_RED}),)}
P2_WEATHER_OVERRIDES = (
    ("akja_", {"role": "pad"}), ("bar_hq", {"role": "galv", "rust": 1.0}),
    ("footing_", {"role": "masonry"}), ("stilt_", {"role": "timber"}),
    ("hosereel_", {"role": "paint"}), ("wood_", {"role": "timber", "scale": 0.5}),
    ("mast_", {"role": "galv"}), ("gear_", {"role": "pad"}))

P2 = FineRecipe(
    key="P2", aliases=("patrol-hq", "p2"), letter="A",
    asset="SkiPatrolHQCommercialLow", prefab="SkiPatrolHQ", category="buildings",
    lot=P2_LOT, base_mode="engine", plinth=None, yaw_deg=0.0, tex=1024, allow_4096=False,
    budget=Budget(2000, 2500, 650, 200, 200),
    # tiled rollout (texture-parity-rollout-2026-10-16.md section 2; --tiled only)
    tile_scale=(("timber_heavy", 2), ("galv", 2), ("deck", 2)),
    massing="custom", builder=build_p2,
    rooms={"hq": (3, 3)}, interior="CommercialLow", slots=SLOTS,
    ground_prefixes=("footing_", "stair_hq_str"),
    arrival_prefixes=("stair_hq", "deck_hq", "door_hq", "emblem_hq_"),
    # detail pass 1b (check-patrol F1): the name board keeps its frame at LOD1 (it was deleted
    # while the face stayed, a 0.08 m float); the 67 tris come from the two decorative gable
    # struts (beam_strutL / R, braces above the collar ties: LOD1 867 before the decimation
    # instead of 894, LOD1 649 <= the 650 cap). beam_ is already on the kit LOD2 delete list.
    lod1_extra=("joist_", "stovepipe_", "emblem_hqflag", "beam_strut", "sign_hqlogs_"),
    lod2_extra=("joist_", "stovepipe_", "stilt_", "logpost_", "stair_hq_treads", "soffit_"),
    lod2_keep=("emblem_hqroof_face",),
    labels=(("emblem_hq_face", "paint_cross_disc", {}),
            ("sign_hqname_face", "display",
             {"draws": (("text", {"lines": ("SKI PATROL", "FIRST AID"), "bg": PATROL_RED}),)}),
            ("emblem_hqroof_face", "display", ROOF_CROSS),
            ("emblem_hqflagA_face", "display", FLAG_CROSS),
            ("emblem_hqflagB_face", "display", FLAG_CROSS),
            ("sign_hqlogs_face", "display", {"draws": (("logs", {"seed": 2}),)})),
    cull_ok=("soffit_",),   # eave undersides at ~2.3-3 m: the kit's height-aware cull may take them
    views=(("deck_closeup", (0.45, -1.0, 0.35)), ("gable_closeup", (0.1, -1.0, 0.55))),
    weights=(("sign_hqname_face", 2.25), ("emblem_hqroof_face", 1.0), ("emblem_hqflag", 1.5),
             ("sign_hqlogs_face", 1.0), ("mull_hq", 0.75), ("sill_hq", 0.75), ("jamb_hq", 0.75),
             ("ridge_hq", 0.75), ("lamp_hqbeacon", 1.5)),
    # lean fix sweep (moved toboggans + rack bar): 0.50 VL1 0.05017 WARN (was 0.04972), so the
    # highest budget-clean step is 0.45 (VL1 0.0451); dressing sweep: 0.55 VL1 0.0547.
    # Fine-detail step 3d sweep (kit rows, sign set, fine geometry): 0.50 clean (VL1 0.047, VL2 0.065),
    # 0.55 VL1 0.052 -> ships 0.50; the legacy path keeps the approved 0.45
    weather={"intensity": 0.50, "overrides": P2_WEATHER_OVERRIDES},
    sub_objects=P2_ROWS_LEGACY + P2_ROWS_FINE,
    notes="civic-sign contract 3 (P2) + skeptic M3/M7; lot 2 x 2 (R22); detail pass 1 (design "
          "3.4 / 5.2 + skeptic); fine-detail step 3d (sign set, kit lean rows + hose reel, vanilla "
          "door light, fine geometry)",
    # fine-detail step 3d (opt-in; lodge.py --no-fine restores `legacy` = the approved HQ)
    fine_detail=True,
    sign_set=P2_SIGN_SET,
    kit_colour_set=KIT_COLOUR_SET,
    legacy=(("sub_objects", P2_ROWS_LEGACY),
            ("weather", {"intensity": 0.45, "overrides": P2_WEATHER_OVERRIDES})),
)

RECIPES = (P1, P2)
