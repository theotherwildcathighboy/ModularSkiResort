"""
welcome_sign.py - the resort Welcome Sign (timber-and-stone portal) -> SkiWelcomeSignA
(assets/import/signs/A), the mesh for SkiResortGate's m_Meshes (engine base, R14).

Contract: docs/research/art/assets-2026-10/design/civic-sign.md section 4 (+ 1, 5) with
civic-sign-skeptic.md applied (skeptic wins) and ROUND-SPEC R14, R18, R24, R25:
    plank   the name plank front `plankfront_sign` is EXACTLY ONE quad, 7.50 x 1.50 m,
            facing authoring +X = game +Z (the road), centre / width / height exported as
            constants (PLANK_CENTRE_GAME, PLANK_W, PLANK_H) and as the manifest "we" block
            (S6); plain walnut boards on its own island, density 1.8.
    header  "SKI AREA" (--header) from a Blender FONT curve (builtin Bfont: NOT Inter,
            skeptic claim 4; DejaVu-derived, unverified) rasterised 4 x 4 supersampled
            into the reserved BAND, cream on the slot-1 field, ControlMask 0 under the
            letters (S5), routed 3 mm (normal from height); two snowflake ornaments.
    emblem  disc D 1.50 on the gable, ski_launcher_glyph.cand_c (the shipped launcher
            glyph, DECISIONS 148) white at 0.70 D + a white rim ring on the slot-2 field.
    M3      every painted face is a front-only part on its own island: `header_sign`,
            `emblemfront_sign` (band) and `plankfront_sign`; rims / bodies are other parts.
    M5      the front rake stops 0.09 m past the emblem face (slab front x -0.30) with a
            flush 0.03 m barge (outer face x -0.27, no hang below the slab): S7 asserts the
            whole disc is visible along 45-deg-elevation head-on rays (BVH).
    M6      SPECS["band"] is a placeholder MaterialSpec for material_sources; the band
            SourceSet replaces the source at index sorted(keys).index("band") of the keys
            the parts USE (cs2_bake.material_sources indexes used keys).
    M8      S2 samples a grid inset >= 0.10 m (the WE box |y| <= 3.30, z 1.90-3.20).
    N3      cs2_bake multiplies AO over the restored band: the post-compose hook re-copies
            the band rects from the band PNGs exactly as _restore_reserved does.
    should-fix: band rects in px multiples of 8 at 1024 (fractions of A, integral at 512);
            non-band UVTile offset +2 u before the bake (no kit face pinned by accident);
            density weights within 0.55-1.8; wall plate y +-3.30 (not into the roof);
            G3 slack >= 0.02 m on the flags (flag 1.10 m, |y| <= 7.67).
Kit API (R25): cs2_bldg builders (box_part, prism_gable, roof_slab, lantern), materials
(BLDG_MATERIALS + one local walnut plank BldgMat, prefiltered by cs2_bldg.prefilter_set,
R24), retile, check_base_mode, plan_lod_targets / lod_chain, shipped_density; recipe-local
parts from recipes/_civic.py.

CLI (headless only):
    blender --background --factory-startup --python-exit-code 1 --python welcome_sign.py --
        [--out DIR | --export] [--name SkiWelcomeSignA] [--masts {0,2,3}] [--no-lanterns]
        [--header "SKI AREA"] [--finish] [--lods] [--blockout] [--preview [DIR]]
        [--we-mock [TEXT]] [--review [DIR]] [--tex 1024] [--ao-samples 48] [--no-validate]
    Without --finish: blockout bake at A = 512, AO off (fast look). --blockout: geometry,
    band, contracts and the manifest only (no bake, no FBX). Exit 1 on any failure; last line
        PASS welcome_sign: <Name> LOD0 n, chain [n1, n2], engine base OK, tex A, ...

Fine-detail step 3d (2026-10-12; build/fine-detail-step3d-patrol-sign.md; the engine of
build/fine-detail-step2-engine.md). The DEFAULT build is the fine-detail one; `--no-fine` rebuilds the
pre-pass sign vertex for vertex (the shipped 2026-10-11 export):
    header  "SKI AREA" was vector Bfont at 186 px/m in the host band (< 300): the header face is split
            where it is plain field (y +-GATE_SPLIT_Y 1.30, between the letters' +-1.21 and the snowflakes'
            +-1.47) into the lettering panel `sign_gate_face` (2.60 x 0.60) and two snowflake panels
            `sign_gateflake{L,R}_face` (0.70 x 0.60); all three leave the host for the sign set
            SkiSignSetGateB (a sharer of the school's building sheet: kinds gate.header, lit, and
            gate.flake, used twice; 300 px/m vector), placed by an identity row on SkiResortGate. The
            band keeps the emblem only (the header rect is no longer reserved: its texels go to the pack).
    lamps   the two post lanterns -> WallLightShortRangeRandom01 rows on the post fronts (H4): no lamp_
            part is left, so no _Emissive map (a stale one is removed, lodge.remove_stale_maps).
    masts   12-seg masts and finials (were 8 / 6); the flags unchanged. FlagPole01 / 02 have no offline
            GUID (Editor harvest, H5): the masts stay modelled (PLACEHOLDER).
    rows    the manifest gains a sub_objects block (target SkiResortGate, the hosts' (y, z, x) convention).
"""

import argparse
import hashlib
import importlib.util
import json
import math
import os
import sys
import tempfile
import time
import traceback
import types
import zlib
from dataclasses import replace

import bmesh
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

_HERE = os.path.dirname(os.path.abspath(__file__))
if _HERE not in sys.path:
    sys.path.insert(0, _HERE)
import cs2_asset            # noqa: E402
import cs2_bake             # noqa: E402
import cs2_bldg as K        # noqa: E402
import cs2_compose          # noqa: E402
import cs2_uv               # noqa: E402
import cs2_validate         # noqa: E402
import ski_launcher_glyph as G   # noqa: E402
from bldg_recipes import Budget   # noqa: E402
import fine_detail as FD          # noqa: E402  (bpy-free; fine-detail step 3d)


def _load_civic():
    name = "bldg_recipe_civic_helpers"
    if name in sys.modules:
        return sys.modules[name]
    spec = importlib.util.spec_from_file_location(name, os.path.join(_HERE, "recipes", "_civic.py"))
    mod = importlib.util.module_from_spec(spec)
    sys.modules[name] = mod
    spec.loader.exec_module(mod)
    return mod


C = _load_civic()

REPO = os.path.normpath(os.path.join(_HERE, os.pardir, os.pardir))
IMPORT_DIR = os.path.join(REPO, "assets", "import", "signs", "A")
PREVIEW_DIR = os.path.join(REPO, "assets", "preview", "signs")
NAME = "SkiWelcomeSignA"
PREFAB = "SkiResortGate"
SPEC_TAG = "civic-sign-2026-10"

# ======================================================================================
# Constants (authoring metres; contract 4.1; lot 2 x 1 -> X +-4, Y +-8)
# ======================================================================================

LOT = (2, 1)                          # (W, D) cells: game X = authored Y = 16 m, Z = 8 m
HALF_X, HALF_Y = 4.0 * LOT[1], 4.0 * LOT[0]
LOT_MARGIN = 0.25                     # contract G3 (self-imposed, engine warns past +0.5)
PORTAL_X = -0.50
PIER_Y = 4.70
PIER = (1.20, 0.35, 2.50)             # width, shaft z0, z1
PLINTH = (1.36, 0.35)                 # width, height (ground part, open bottom)
CAP = (1.40, 2.50, 2.62)              # width, z0, z1
POST = (0.35, 2.62, 4.45)             # size, z0, z1
BEAM_D = 0.30                         # beam depth (x), centred on PORTAL_X
BEAM_Y = 5.45
LOWER_BEAM = (3.45, 3.75)
HEADER_BEAM = (3.85, 4.45)
HEADER_X = PORTAL_X + BEAM_D / 2      # -0.35: header / beam front face
HEADER_Y = 2.00                       # header face y +-2.00 (4.00 x 0.60)
BRACE = 0.15
BRACE_Z0 = 2.95
PLANK_W, PLANK_H, PLANK_T = 7.50, 1.50, 0.08
PLANK_Z = (1.80, 3.30)
PLANK_FRONT_X = PORTAL_X + PLANK_T / 2          # -0.46
FRAME_W, FRAME_T = 0.05, 0.03
STRAP_Y, STRAP_Z0 = 3.45, 3.15
PLATE = (0.40, 3.30, 4.45, 4.55)      # depth (x), half length (y), z0, z1
GABLE_HALF, GABLE_PITCH = 3.30, 30.0
GABLE_BASE = PLATE[3]                 # 4.55
GABLE_T = 0.12                        # x -0.56 .. -0.44
GABLE_FRONT_X = PORTAL_X + GABLE_T / 2          # -0.44
TAN30 = math.tan(math.radians(GABLE_PITCH))
GABLE_APEX = GABLE_BASE + GABLE_HALF * TAN30    # 6.4553
ROOF_T = 0.14
EAVE_Y = 3.45
RAKE_FRONT_X, RAKE_BACK_X = -0.30, -0.95        # M5: front rake <= 0.15 past the emblem
BARGE_T = 0.03                        # flush barge (no hang below the slab)
RIDGE_EXT = 0.05
RIDGE_CAP = (0.10, 6.55, 6.76)        # half width, z0, z1 (top 6.76 = contract)
EMBLEM_D, EMBLEM_T, EMBLEM_SEGS = 1.50, 0.05, 24
EMBLEM_Z = GABLE_BASE + GABLE_HALF * math.tan(math.radians(GABLE_PITCH / 2.0))   # incircle 5.4342
EMBLEM_FACE_X = GABLE_FRONT_X + EMBLEM_T        # -0.39
MAST_H, MAST_R0, MAST_R1, MAST_FINIAL = 8.0, 0.07, 0.045, 8.16
FOOTING = (0.50, 0.12)
MASTS = {0: (), 2: ((PORTAL_X, 6.50), (PORTAL_X, -6.50)),
         3: ((PORTAL_X, 6.50), (PORTAL_X, -6.50), (-3.00, 0.00))}
FLAG = (1.10, 0.75, 7.85, 0.06)       # length (flying -Y), height, top z, wave amplitude
LANTERN_AT = (PORTAL_X + POST[0] / 2, 3.15)     # (post front x, lens centre z)
HEADER_TEXT = "SKI AREA"
HEADER_CAP = 0.42                     # letter cap height, m
ORNAMENT_Y, ORNAMENT_D = 1.65, 0.36
CREAM_SRGB = (0.93, 0.89, 0.78)
FIELD_SRGB = 0.80                     # cs2_asset.RECOLOR_ALBEDO under the recolour masks
EMBLEM_GLYPH_FRAC = 0.70
EMBLEM_RING = (0.06, 0.01)            # ring width, inset from the disc edge
ROUTE_DEPTH = 0.003
BAND_SMOOTH = 0.45

# WE constants (game frame: lot-centre origin, +Z road; manifest "we" block, S6)
WE_MARGIN = 0.45
WE_STANDOFF = 0.004
PLANK_CENTRE_AUTH = (PLANK_FRONT_X, 0.0, 0.5 * (PLANK_Z[0] + PLANK_Z[1]))
PLANK_CENTRE_GAME = (-PLANK_CENTRE_AUTH[1] + 0.0, PLANK_CENTRE_AUTH[2], PLANK_CENTRE_AUTH[0])   # gx = -y (x-sign gate N)
WE_MAX_WIDTH = round(PLANK_W - 2 * WE_MARGIN, 3)
WE_OFFSET = (PLANK_CENTRE_GAME[0], PLANK_CENTRE_GAME[1], round(PLANK_CENTRE_GAME[2] + WE_STANDOFF, 4))
WE_LAYOUT = "assets/we/K45_WE/layouts/SkiResortGate.wedefault.xml"
WE_BOX = (3.30, 1.90, 3.20)           # M8 S2 grid: |y| <= 3.30, z 1.90 .. 3.20

# Band (contract 4.3): px at A = 1024, multiples of 8 so every A >= 128 is integral
BAND_PX_1024 = {"emblem": (8, 8, 120, 120), "header": (136, 8, 880, 120)}
RESERVED_KEYS = ("emblem", "header")

BUDGET = Budget(2000, 2500, 900, 250, 250)
TEX = 1024
AO_SAMPLES = 48
WELD_M = 0.0005
SHADE_ANGLE = 66.0
GROUND = ("pierplinth_", "footing_")
FRONT_PROBE = "plankfront_"
BAND_PARTS = ("header_", "emblemfront_")
# LOD lists (contract 4.2: kit-style names; emissive is LOD0-only, DECISIONS 77)
DELETE1 = ("strap_", "finial_", "lamp_")
DELETE2 = DELETE1 + ("brace_", "plankframe_", "plate_")
BEVEL = ("pierplinth_", "pier_", "piercap_", "tpost_", "beamlow_", "brace_", "ridge_")
# density weights (longest prefix; skeptic: 0.55 .. 1.8, the report errors past 2x)
DENSITY = {"plankfront_": 1.8, "roof_": 0.6, "soffit_": 0.55, "mast_": 0.55, "finial_": 0.55,
           "footing_": 0.8, "flag_": 1.0, "lamp_": 0.55, "strap_": 0.55}
# VariationSet defaults (target sRGB / 0.80): green trim + header field, blue emblem / flags
SLOTS = {1: (0.15, 0.31, 0.225), 2: (0.15, 0.375, 0.725)}

# Local material (kit has no 0.5 m walnut plank): WoodSiding009 boards run along u
# (16 per image height, 2:1 image) -> tile (16, 8) m = 0.50 m boards, prefiltered (R24).
PLANK_MAT = K.BldgMat("WoodSiding009", (16.0, 8.0), (0.45, 0.30, 0.18), smooth=0.35,
                      density_w=1.8)
LOCAL_MATS = {"plank": PLANK_MAT}
BAND_KEY = "band"

# ---- fine-detail step 3d (module docstring) ----------------------------------------------------------
GATE_SPLIT_Y = 1.30                   # header split: plain field between the letters and the flakes
FINE_KEYS = ("emblem",)               # band keys of the fine build (the header left for the sign set)
MAST_SEGS_FINE = 12
SIGN_SET = FD.SignSetSpec(prop="SkiSignSetGate", letter="B", host="gate", owner=False,
                          faces=(("sign_gateflakeL_face", "gate.flake"), ("sign_gate_face", "gate.header"),
                                 ("sign_gateflakeR_face", "gate.flake")))
WALL_LIGHT = ("WallLightShortRangeRandom01", "cfc12fde01d9da24b86ddedd657eaae1")
SUB_OBJECT_NOTE = ("ObjectSubObjects on SkiResortGate (the deployed prefab: keep its name / CID; drop the "
                   "ParkSign placeholder row ec48568...); m_GroupIndex 0, m_Probability 100; m_ParentMesh 0 "
                   "(on the sign mesh)")


def fine_rows():
    """The fine build's rows (DESIGN frame (gx, gy, gz) = authoring (y, z, x); lodge.sub_objects_block writes
    them through rule T into the game frame, gx negated): the two post
    lanterns as short-range wall lights on the post fronts (the old lens height), facing the road (+Z)."""
    lx, lz = LANTERN_AT
    return tuple(WALL_LIGHT + ((sy * PIER_Y, lz, lx), (0.0, 0.0, 0.0, 1.0), 0) for sy in (-1.0, 1.0))


# ======================================================================================
# CLI
# ======================================================================================

def parse_args(argv=None):
    if argv is None:
        argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser(prog="welcome_sign.py")
    ap.add_argument("--out", default=None)
    ap.add_argument("--export", action="store_true", help="write to assets/import/signs/A")
    ap.add_argument("--name", default=NAME)
    ap.add_argument("--masts", type=int, default=2, choices=(0, 2, 3))
    ap.add_argument("--no-lanterns", action="store_true")
    ap.add_argument("--header", default=HEADER_TEXT)
    ap.add_argument("--finish", action="store_true")
    ap.add_argument("--lods", action="store_true")
    ap.add_argument("--blockout", action="store_true")
    ap.add_argument("--preview", nargs="?", const="", default=None, metavar="DIR")
    ap.add_argument("--we-mock", nargs="?", const="Ski area 1", default=None, metavar="TEXT")
    ap.add_argument("--review", nargs="?", const="", default=None, metavar="DIR")
    ap.add_argument("--tex", type=int, default=TEX, choices=(512, 1024, 2048))
    ap.add_argument("--ao-samples", type=int, default=AO_SAMPLES)
    ap.add_argument("--no-validate", action="store_true")
    # fine-detail step 3d (module docstring)
    ap.add_argument("--no-fine", action="store_true",
                    help="the pre-pass sign (header in the band, modelled lanterns, 8-seg masts)")
    ap.add_argument("--signset-out", default=None, help="sign-set letter folder (default: beside --out)")
    return ap.parse_args(argv)


def clear_scene():
    if bpy.context.object is not None and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.cameras, bpy.data.lights,
                  bpy.data.images, bpy.data.curves):
        for item in list(block):
            if item.users == 0:
                block.remove(item)


# ======================================================================================
# Parts
# ======================================================================================

def band_rects(A, keys=RESERVED_KEYS):
    """{key: (u0, v0, u1, v1)} fractions; px = BAND_PX_1024 scaled, integral (asserted)."""
    out = {}
    for k in keys:
        px = [p * A / 1024.0 for p in BAND_PX_1024[k]]
        if any(abs(p - round(p)) > 1e-9 for p in px):
            raise ValueError(f"band rect {k} not integral at A = {A}: {px}")
        out[k] = tuple(round(p) / float(A) for p in px)
    return out


def ground_prefixes(args):
    """GROUND minus the mast footings when --masts 0 (every listed prefix must give OK
    engine-base edges, cs2_bldg.check_base_mode)."""
    return tuple(g for g in GROUND if g != "footing_" or MASTS[args.masts])


def build_parts(args, fine=False):
    P = []
    px = PORTAL_X
    # ---- piers: plinth (ground), shaft, cap; posts; beams; braces --------------------
    for tag, sy in (("l", -1.0), ("r", 1.0)):
        y = sy * PIER_Y
        w, h = PLINTH
        P += K.box_part(f"pierplinth_{tag}", (px - w / 2, y - w / 2, 0.0), (px + w / 2, y + w / 2, h),
                        "stone_rubble", ground=True)
        w, z0, z1 = PIER
        P += K.box_part(f"pier_{tag}", (px - w / 2, y - w / 2, z0), (px + w / 2, y + w / 2, z1),
                        "stone_rubble", drop=("-Z", "+Z"))
        w, z0, z1 = CAP
        P += K.box_part(f"piercap_{tag}", (px - w / 2, y - w / 2, z0), (px + w / 2, y + w / 2, z1),
                        "rock")
        s, z0, z1 = POST
        P += K.box_part(f"tpost_{tag}", (px - s / 2, y - s / 2, z0), (px + s / 2, y + s / 2, z1),
                        "timber_heavy", drop=("-Z", "+Z"))
        yi, yo = y - sy * s / 2, y + sy * s / 2          # post inner / outer face
        for k, (ya, yb) in enumerate(((yi, sy * (PIER_Y - 0.675)), (yo, sy * (BEAM_Y - 0.075)))):
            P += C.beam_part(f"brace_{tag}{k}", (px, ya, BRACE_Z0), (px, yb, LOWER_BEAM[0]),
                             BRACE, BRACE, "timber_heavy", extend=0.08)
    bx0, bx1 = px - BEAM_D / 2, px + BEAM_D / 2
    P += K.box_part("beamlow_sign", (bx0, -BEAM_Y, LOWER_BEAM[0]), (bx1, BEAM_Y, LOWER_BEAM[1]),
                    "timber_heavy")
    # header beam: body quads cut at y +-2.00 (no T-junction with the front quads)
    z0, z1 = HEADER_BEAM
    ys = (-BEAM_Y, -HEADER_Y, HEADER_Y, BEAM_Y)
    body = [([(bx0, -BEAM_Y, z0), (bx0, BEAM_Y, z0), (bx0, BEAM_Y, z1), (bx0, -BEAM_Y, z1)], (-1, 0, 0)),
            ([(bx0, -BEAM_Y, z0), (bx1, -BEAM_Y, z0), (bx1, -BEAM_Y, z1), (bx0, -BEAM_Y, z1)], (0, -1, 0)),
            ([(bx0, BEAM_Y, z0), (bx1, BEAM_Y, z0), (bx1, BEAM_Y, z1), (bx0, BEAM_Y, z1)], (0, 1, 0))]
    for a, b in zip(ys, ys[1:]):
        body.append(([(bx0, a, z1), (bx1, a, z1), (bx1, b, z1), (bx0, b, z1)], (0, 0, 1)))
        body.append(([(bx0, a, z0), (bx1, a, z0), (bx1, b, z0), (bx0, b, z0)], (0, 0, -1)))
    P += C.quads_part("beamhead_sign", body, "trim")
    P += C.quads_part("beamheadfront_sign", [
        ([(bx1, -BEAM_Y, z0), (bx1, -HEADER_Y, z0), (bx1, -HEADER_Y, z1), (bx1, -BEAM_Y, z1)], (1, 0, 0)),
        ([(bx1, HEADER_Y, z0), (bx1, BEAM_Y, z0), (bx1, BEAM_Y, z1), (bx1, HEADER_Y, z1)], (1, 0, 0))],
        "trim")
    if fine:          # fine-detail step 3d: three single-quad faces for the sign set (routed in run())
        g = GATE_SPLIT_Y
        for nm, ya, yb in (("sign_gateflakeL_face", -HEADER_Y, -g), ("sign_gate_face", -g, g),
                           ("sign_gateflakeR_face", g, HEADER_Y)):
            P += C.quads_part(nm, [([(bx1, ya, z0), (bx1, yb, z0), (bx1, yb, z1), (bx1, ya, z1)], (1, 0, 0))],
                              "trim")
    else:
        P += C.quads_part("header_sign", [([(bx1, -HEADER_Y, z0), (bx1, HEADER_Y, z0),
                                            (bx1, HEADER_Y, z1), (bx1, -HEADER_Y, z1)], (1, 0, 0))],
                          BAND_KEY)
    # ---- plank: body (+X dropped), ONE front quad, frame strips, straps ----------------
    pz0, pz1 = PLANK_Z
    hw = PLANK_W / 2
    fx = PLANK_FRONT_X
    P += K.box_part("plankbody_sign", (fx - PLANK_T, -hw, pz0), (fx, hw, pz1), "plank", drop=("+X",))
    P += C.quads_part("plankfront_sign", [([(fx, -hw, pz0), (fx, hw, pz0), (fx, hw, pz1),
                                            (fx, -hw, pz1)], (1, 0, 0))], "plank")
    fw, ft = FRAME_W, FRAME_T
    frame = [((fx, -hw, pz1 - fw), (fx + ft, hw, pz1), ("-X",)),
             ((fx, -hw, pz0), (fx + ft, hw, pz0 + fw), ("-X",)),
             ((fx, -hw, pz0 + fw), (fx + ft, -hw + fw, pz1 - fw), ("-X", "-Z", "+Z")),
             ((fx, hw - fw, pz0 + fw), (fx + ft, hw, pz1 - fw), ("-X", "-Z", "+Z"))]
    P += C.new_part("plankframe_sign", lambda bm: [C.box_into(bm, lo, hi, d) for lo, hi, d in frame],
                    "trim")
    straps = [((fx, sy * STRAP_Y - 0.03, STRAP_Z0), (fx + 0.012, sy * STRAP_Y + 0.03, LOWER_BEAM[0] + 0.02),
               ("-X",)) for sy in (-1.0, 1.0)]
    P += C.new_part("strap_sign", lambda bm: [C.box_into(bm, lo, hi, d) for lo, hi, d in straps], "galv")
    # ---- wall plate, gable, roof, barges, ridge cap ------------------------------------
    d, hl, z0, z1 = PLATE
    P += K.box_part("plate_sign", (px - d / 2, -hl, z0), (px + d / 2, hl, z1), "timber_heavy")
    P += K.prism_gable("sign", "X", px - GABLE_T / 2, px + GABLE_T / 2, -GABLE_HALF, GABLE_HALF,
                       GABLE_BASE, GABLE_APEX, ridge_at=0.0, mat="timber_vboard")
    half_c = 0.5 * ROOF_T / math.cos(math.radians(GABLE_PITCH))

    def centre_z(y_signed, side):
        # centre plane of the `side` slab (side -1 rises toward +y) at across-coordinate y
        return GABLE_APEX + half_c + TAN30 * (-side * y_signed)
    for tag, side in (("L", -1.0), ("R", 1.0)):
        e = side * EAVE_Y
        r = -side * RIDGE_EXT
        ez, rz = centre_z(e, side), centre_z(r, side)
        P += K.roof_slab(f"sign{tag}", "X", RAKE_BACK_X, RAKE_FRONT_X, e, ez, r, rz, thick=ROOF_T,
                         wall_line=side * GABLE_HALF, mat="timber_shake")
        for end, (a0, a1) in (("f", (RAKE_FRONT_X, RAKE_FRONT_X + BARGE_T)),
                              ("b", (RAKE_BACK_X - BARGE_T, RAKE_BACK_X))):
            P += K.roof_slab(f"sign{tag}{end}", "X", a0, a1, e, ez, r, rz, thick=ROOF_T,
                             mat="trim", prefix="barge_")
    rw, rz0, rz1 = RIDGE_CAP
    P += K.box_part("ridge_sign", (RAKE_BACK_X - BARGE_T, -rw, rz0), (RAKE_FRONT_X + BARGE_T, rw, rz1),
                    "trim")
    # ---- emblem disc: front fan (band) + rim ------------------------------------------
    R = EMBLEM_D / 2
    ring_f = [(EMBLEM_FACE_X, R * math.cos(2 * math.pi * k / EMBLEM_SEGS),
               EMBLEM_Z + R * math.sin(2 * math.pi * k / EMBLEM_SEGS)) for k in range(EMBLEM_SEGS)]
    centre = (EMBLEM_FACE_X, 0.0, EMBLEM_Z)
    P += C.quads_part("emblemfront_sign", [([centre, ring_f[k], ring_f[(k + 1) % EMBLEM_SEGS]],
                                            (1, 0, 0)) for k in range(EMBLEM_SEGS)], BAND_KEY)
    rim = []
    for k in range(EMBLEM_SEGS):
        a, b = ring_f[k], ring_f[(k + 1) % EMBLEM_SEGS]
        a0, b0 = (GABLE_FRONT_X, a[1], a[2]), (GABLE_FRONT_X, b[1], b[2])
        out = (0.0, 0.5 * (a[1] + b[1]), 0.5 * (a[2] + b[2]) - EMBLEM_Z)
        rim.append(([a0, b0, b, a], out))
    P += C.quads_part("emblemrim_sign", rim, "trim")
    # ---- masts, footings, finials, waved two-sided flags -----------------------------
    for k, (mx, my) in enumerate(MASTS[args.masts]):
        fw_, fh_ = FOOTING
        P += K.box_part(f"footing_{k}", (mx - fw_ / 2, my - fw_ / 2, 0.0), (mx + fw_ / 2, my + fw_ / 2, fh_),
                        "concrete", ground=True)
        ms, fs = (MAST_SEGS_FINE, MAST_SEGS_FINE) if fine else (8, 6)
        P += C.cyl_part(f"mast_{k}", (mx, my, fh_), (mx, my, MAST_H), MAST_R0, "trim_white", segs=ms,
                        cap0=False, r1=MAST_R1)

        def finial(bm, mx=mx, my=my, fs=fs):
            C.cyl_into(bm, (mx, my, MAST_H), (mx, my, MAST_H + 0.08), MAST_R1, fs, False, False, 0.08)
            C.cyl_into(bm, (mx, my, MAST_H + 0.08), (mx, my, MAST_FINIAL), 0.08, fs, False, False, 0.0)
        P += C.new_part(f"finial_{k}", finial, "trim_white")
        L, H, ztop, amp = FLAG
        cols = 3
        y0 = my - (MAST_R0 + 0.01)

        def flag(bm, mx=mx, y0=y0):
            for c_ in range(cols):
                s0, s1 = L * c_ / cols, L * (c_ + 1) / cols
                x0_, x1_ = (mx + amp * math.sin(math.pi * 2 * s0 / L), mx + amp * math.sin(math.pi * 2 * s1 / L))
                for side, off in ((1.0, 0.0), (-1.0, -0.004)):
                    pts = [(x0_ + off, y0 - s0, ztop - H), (x1_ + off, y0 - s1, ztop - H),
                           (x1_ + off, y0 - s1, ztop), (x0_ + off, y0 - s0, ztop)]
                    C.face(bm, pts, (side, 0, 0))
        P += C.new_part(f"flag_{k}", flag, "accent")
    # ---- lanterns on the post fronts (kit lantern: lamp_ lens + arm) -------------------
    if not args.no_lanterns and not fine:      # fine: WallLightShortRange rows (fine_rows)
        lx, lz = LANTERN_AT
        for tag, sy in (("l", -1.0), ("r", 1.0)):
            lp, _pt = K.lantern(f"sign{tag}", (lx, sy * PIER_Y, lz), "+X", kind="wall", h=0.40, w=0.25)
            P += lp
    P = C.remat(P, (("soffit_", "timber_heavy"), ("lamp_signlarm", "galv"), ("lamp_signrarm", "galv")))
    return P


# ======================================================================================
# Materials (projection, bake sources) - kit table + the local plank + the band
# ======================================================================================

def projection_specs(keys):
    kit = [k for k in keys if k in K.BLDG_MATERIALS]
    out = dict(K.projection_specs(kit))
    for k in keys:
        if k in LOCAL_MATS:
            m = LOCAL_MATS[k]
            out[k] = cs2_bake.MaterialSpec(texture_set=m.set, metres_per_tile=K.square_tile_m(m),
                                           metal=float(m.metal), slot=m.slot, projection="box")
        elif k == BAND_KEY:
            out[k] = cs2_bake.MaterialSpec(albedo=(0.8, 0.8, 0.8), metres_per_tile=1.0)   # M6
    missing = [k for k in keys if k not in out]
    if missing:
        raise KeyError(f"no projection spec for {missing}")
    return out


def native_ppm(mat):
    """Source px per metre of a set material's Color map over its image tile (PNG header);
    None when unknown. cs2_bldg.prefilter_set has no cap and UPsamples past it (kit need)."""
    p = cs2_bake.find_set_map(cs2_bake._set_dir(mat.set), "Color")
    if p is None or not p.lower().endswith(".png"):
        return None
    w, h, _d, _c = cs2_validate.read_png_header(p)
    tu, tv = K.image_tile_m(mat)
    return min(w / tu, h / tv)


def pack_margin(A):
    """16 px at 1024 (cs2_uv default, mip-4 safe), 8 at 512: the library FRACTION packer
    leaves islands outside [0, 1] with 16 px on ~260 islands at 512 (measured 2026-10-11)."""
    return max(8, int(A) // 64)


def bake_sources(keys, base_ppm, band_paths):
    """{mat_index: SourceSet}: index = sorted(USED keys).index(key) (M6); kit materials via
    cs2_bldg.material_specs (prefiltered, R24), the plank via cs2_bldg.prefilter_set, the
    band from the painted A x A band PNGs (uv None = the packed source layer, for the 1:1
    restore, cs2_bake.py:862-869)."""
    keys = sorted(keys)
    kit = [k for k in keys if k in K.BLDG_MATERIALS]
    specs, info = K.material_specs(kit, base_ppm)
    for k in keys:
        if k in LOCAL_MATS:
            m = LOCAL_MATS[k]
            ppm = max(4.0, K.PREFILTER_FACTOR * float(base_ppm) * float(m.density_w))
            native = native_ppm(m)
            if native is not None and ppm > native:
                ppm = native            # never UPsample (the 16 m plank tile: 3,292 px, 8 min)
            pf = K.prefilter_set(m.set, K.image_tile_m(m), ppm)
            tl = K.srgb_to_linear(tuple(m.target_srgb))
            tint = tuple(a / max(b, 1e-6) for a, b in zip(tl, pf["mean_lin"]))
            specs[k] = cs2_bake.MaterialSpec(texture_set=pf["dir"], metres_per_tile=pf["tile_sq"],
                                             tint=tint, metal=float(m.metal),
                                             rough=1.0 - float(m.smooth), projection="box")
            info[k] = dict(set=m.set, filtered_dir=pf["dir"], N=pf["N"], ppm=round(ppm, 2),
                           tile_sq=pf["tile_sq"], tint_lin=[round(t, 4) for t in tint],
                           target_srgb=list(m.target_srgb), local=True)
    out = {}
    for i, k in enumerate(keys):
        if k == BAND_KEY:
            out[i] = cs2_bake.SourceSet(base=band_paths["BaseColor"], normal=band_paths["Normal"],
                                        normal_convention="GL", mask=band_paths["MaskMap"],
                                        control=band_paths["ControlMask"], uv=None, slot=0,
                                        recolor_albedo=False)
            info[k] = dict(band=True, files={m: os.path.basename(p) for m, p in band_paths.items()})
            continue
        ss = cs2_bake._resolve_spec(cs2_bake._as_material_spec(specs[k], k), k)
        out[i] = replace(ss, uv=cs2_bake.TILE_LAYER)
    return out, info, keys.index(BAND_KEY)


# ======================================================================================
# Band (N6 text raster, painted PNGs)
# ======================================================================================

def _tri_coverage(tris, w, h, ppu, ppv, ss=4):
    """Coverage (h, w) bottom-up of 2D triangles (metres, origin = rect centre)."""
    W, H = w * ss, h * ss
    cov = np.zeros((H, W), np.float32)
    xs = ((np.arange(W) + 0.5) / ss - w / 2.0) / ppu
    ys = ((np.arange(H) + 0.5) / ss - h / 2.0) / ppv
    for (a, b, c) in tris:
        x0, x1 = min(a[0], b[0], c[0]), max(a[0], b[0], c[0])
        y0, y1 = min(a[1], b[1], c[1]), max(a[1], b[1], c[1])
        j0, j1 = np.searchsorted(xs, x0), np.searchsorted(xs, x1, side="right")
        i0, i1 = np.searchsorted(ys, y0), np.searchsorted(ys, y1, side="right")
        if j1 <= j0 or i1 <= i0:
            continue
        X, Y = np.meshgrid(xs[j0:j1], ys[i0:i1])
        d = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
        if abs(d) < 1e-14:
            continue
        l1 = ((b[1] - c[1]) * (X - c[0]) + (c[0] - b[0]) * (Y - c[1])) / d
        l2 = ((c[1] - a[1]) * (X - c[0]) + (a[0] - c[0]) * (Y - c[1])) / d
        inside = (l1 >= 0) & (l2 >= 0) & (l1 + l2 <= 1)
        cov[i0:i1, j0:j1][inside] = 1.0
    return cov.reshape(h, ss, w, ss).mean(axis=(1, 3))


def text_triangles(body, cap_m):
    """FONT curve (builtin Bfont) -> triangulated mesh -> 2D triangles in metres, scaled so
    the cap height (bbox height of an all-caps line) is `cap_m`, centred on the origin.
    Returns (tris, width_m)."""
    cu = bpy.data.curves.new("CivicSignTextCurve", type="FONT")
    cu.body = body
    cu.size = 1.0
    cu.align_x = "CENTER"
    ob = bpy.data.objects.new("CivicSignText", cu)
    bpy.context.scene.collection.objects.link(ob)
    try:
        dg = bpy.context.evaluated_depsgraph_get()
        me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
        bm = bmesh.new()
        bm.from_mesh(me)
        bmesh.ops.triangulate(bm, faces=bm.faces[:])
        tris = [tuple((v.co.x, v.co.y) for v in f.verts) for f in bm.faces]
        bm.free()
        bpy.data.meshes.remove(me)
    finally:
        bpy.data.objects.remove(ob, do_unlink=True)
        bpy.data.curves.remove(cu)
    pts = np.array([p for t in tris for p in t], np.float64)
    lo, hi = pts.min(0), pts.max(0)
    s = cap_m / (hi[1] - lo[1])
    c = 0.5 * (lo + hi)
    out = [tuple(((p[0] - c[0]) * s, (p[1] - c[1]) * s) for p in t) for t in tris]
    return out, float((hi[0] - lo[0]) * s)


def _place(dst, cov_topdown, cx_px, cy_px):
    """Max-blend a top-down coverage square into the bottom-up array `dst`, centred at
    (cx_px, cy_px) in dst's (column, row-from-bottom) pixels."""
    s = cov_topdown.shape[0]
    src = cov_topdown[::-1]
    r0 = int(round(cy_px - s / 2.0))
    c0 = int(round(cx_px - s / 2.0))
    if r0 < 0 or c0 < 0 or r0 + s > dst.shape[0] or c0 + s > dst.shape[1]:
        raise ValueError("band glyph does not fit its rect")
    dst[r0:r0 + s, c0:c0 + s] = np.maximum(dst[r0:r0 + s, c0:c0 + s], src)


def paint_band(A, header_text, out_dir, name, keys=RESERVED_KEYS):
    """Paint the band (contract 4.3 steps 1-6) into A x A arrays (bottom-up; BaseColor
    display-encoded as the importer reads it) and write <name>_band_<Map>.png into
    `out_dir` (never the asset folder: a stray PNG is a validator ERROR). Returns
    (paths, arrays, coverage {key: (rect slices, cov)}, report)."""
    rects = band_rects(A)
    base = np.zeros((A, A, 4), np.float32)
    nrm = np.zeros((A, A, 4), np.float32)
    msk = np.zeros((A, A, 4), np.float32)
    ctl = np.zeros((A, A, 4), np.float32)
    base[..., 3] = 1.0
    nrm[...] = (0.5, 0.5, 1.0, 1.0)
    cream = np.array(CREAM_SRGB, np.float32)
    cov_out, rep = {}, {}
    # -- header -----------------------------------------------------------------------
    if "header" in keys:
        _paint_header(A, header_text, rects, base, nrm, msk, ctl, cream, cov_out, rep)
    # -- emblem ---------------------------------------------------------------------------
    _paint_emblem(A, rects, base, msk, ctl, cov_out, rep)
    # -- write ------------------------------------------------------------------------------
    os.makedirs(out_dir, exist_ok=True)
    paths = {}
    for arr, m, is_data in ((base, "BaseColor", False), (nrm, "Normal", True), (msk, "MaskMap", True),
                            (ctl, "ControlMask", True)):
        p = os.path.join(out_dir, f"{name}_band_{m}.png")
        cs2_asset._write_png_raw(p, f"{name}Band{m}", arr, A, is_data)
        paths[m] = p
    return paths, {"BaseColor": base, "Normal": nrm, "MaskMap": msk, "ControlMask": ctl}, cov_out, rep


def _paint_header(A, header_text, rects, base, nrm, msk, ctl, cream, cov_out, rep):
    """The band's header rect (paint_band's original header block, unchanged)."""
    sl = cs2_compose._rect_slices(rects["header"], A)
    h, w = sl[0].stop - sl[0].start, sl[1].stop - sl[1].start
    ppu, ppv = w / (2 * HEADER_Y), h / (HEADER_BEAM[1] - HEADER_BEAM[0])
    tris, text_w = text_triangles(header_text, HEADER_CAP)
    max_w = 2 * (ORNAMENT_Y - ORNAMENT_D / 2 - 0.05)
    if text_w > max_w:
        raise ValueError(f"header text {header_text!r} is {text_w:.2f} m wide > {max_w:.2f} m "
                         f"(between the ornaments)")
    cov = _tri_coverage(tris, w, h, ppu, ppv)
    s = int(round(ORNAMENT_D * ppu))
    flake = G.render_alpha(G.snowflake(128.0, 128.0, 112.0, 24.0, bw=18.0), s, ss=8)
    for sy in (-1.0, 1.0):
        _place(cov, flake, w / 2.0 + sy * ORNAMENT_Y * ppu, h / 2.0)
    fld = FIELD_SRGB
    base[sl][..., 0:3] = fld * (1.0 - cov[..., None]) + cream * cov[..., None]
    ctl[sl][..., 0] = np.clip(1.0 - 2.0 * cov, 0.0, 1.0)
    msk[sl][..., 3] = BAND_SMOOTH
    nrm[sl][..., 0:3] = cs2_compose.height_to_normal(-ROUTE_DEPTH * cov, 0.5 * (ppu + ppv))[..., 0:3]
    cov_out["header"] = (sl, cov)
    rep["header"] = dict(px=(w, h), ppm=(round(ppu, 2), round(ppv, 2)), text=header_text,
                         text_width_m=round(text_w, 3), cap_m=HEADER_CAP, font="Bfont (builtin)",
                         ornament_px=s, coverage_mean=round(float(cov.mean()), 4))


def _paint_emblem(A, rects, base, msk, ctl, cov_out, rep):
    """The band's emblem rect (paint_band's original emblem block, unchanged)."""
    fld = FIELD_SRGB
    sl = cs2_compose._rect_slices(rects["emblem"], A)
    h, w = sl[0].stop - sl[0].start, sl[1].stop - sl[1].start
    ppm = w / EMBLEM_D
    gs = int(round(EMBLEM_GLYPH_FRAC * EMBLEM_D * ppm))
    covE = np.zeros((h, w), np.float32)
    _place(covE, G.render_alpha(G.cand_c(), gs, ss=8), w / 2.0, h / 2.0)
    ss = 4
    yy, xx = np.meshgrid((np.arange(h * ss) + 0.5) / ss, (np.arange(w * ss) + 0.5) / ss, indexing="ij")
    rr = np.hypot(xx - w / 2.0, yy - h / 2.0) / ppm
    rw, inset = EMBLEM_RING
    ring = (np.abs(rr - (EMBLEM_D / 2 - inset - rw / 2)) <= rw / 2).astype(np.float32)
    ring = ring.reshape(h, ss, w, ss).mean(axis=(1, 3))
    covE = np.maximum(covE, ring)
    white = 0.92
    base[sl][..., 0:3] = fld * (1.0 - covE[..., None]) + white * covE[..., None]
    ctl[sl][..., 1] = np.clip(1.0 - 2.0 * covE, 0.0, 1.0)
    msk[sl][..., 3] = BAND_SMOOTH
    cov_out["emblem"] = (sl, covE)
    rep["emblem"] = dict(px=(w, h), ppm=round(ppm, 2), glyph="ski_launcher_glyph.cand_c",
                         glyph_px=gs, ring=EMBLEM_RING)


def set_band_uvs(obj, table, A, layer=cs2_bake.TILE_LAYER, keys=RESERVED_KEYS):
    """Band faces -> their rect (u along authoring +y, v along +z, read from +X); every
    other loop shifted +2 u (integer: same tiling) so no kit face can be pinned (skeptic
    determinism (a)). Runs on the joined mesh in the AUTHORING frame."""
    rects = band_rects(A)
    me = obj.data
    lay = me.uv_layers[layer]
    uv = cs2_uv.loop_uvs(lay)
    pv = np.zeros(len(me.polygons), np.int32)
    me.attributes["part"].data.foreach_get("value", pv)
    names = {i + 1: n for i, n in enumerate(table)}
    z0, z1 = HEADER_BEAM
    R = EMBLEM_D / 2
    for p in me.polygons:
        n = names.get(int(pv[p.index]), "")
        sl = range(p.loop_start, p.loop_start + p.loop_total)
        if n.startswith("header_"):
            u0, v0, u1, v1 = rects["header"]
            for li in sl:
                co = me.vertices[me.loops[li].vertex_index].co
                uv[li] = (u0 + (co.y + HEADER_Y) / (2 * HEADER_Y) * (u1 - u0),
                          v0 + (co.z - z0) / (z1 - z0) * (v1 - v0))
        elif n.startswith("emblemfront_"):
            u0, v0, u1, v1 = rects["emblem"]
            for li in sl:
                co = me.vertices[me.loops[li].vertex_index].co
                uv[li] = (u0 + (co.y + R) / EMBLEM_D * (u1 - u0),
                          v0 + (co.z - (EMBLEM_Z - R)) / EMBLEM_D * (v1 - v0))
    cs2_uv.set_loop_uvs(lay, uv)
    # every non-band UV island moved by a whole number of tiles (same texture phase) so its
    # min u >= 2: no kit face can lie inside a reserved rect and be pinned by accident
    band_faces = sorted(p.index for p in me.polygons
                        if names.get(int(pv[p.index]), "").startswith(BAND_PARTS))
    others = sorted(set(range(len(me.polygons))) - set(band_faces))
    start, total = cs2_uv.face_loop_table(me)
    for isl in cs2_uv.uv_islands(me, lay, faces=others):
        idx = np.concatenate([np.arange(start[f], start[f] + total[f]) for f in isl])
        k = math.ceil(2.0 - float(uv[idx, 0].min()))
        uv[idx, 0] += k
    cs2_uv.set_loop_uvs(lay, uv)
    pinned = sorted(cs2_uv.pinned_faces(me, lay, [rects[k] for k in keys]))
    if pinned != band_faces:
        raise RuntimeError(f"S3: pinned faces {len(pinned)} != band faces {len(band_faces)}")
    return rects


# ======================================================================================
# Contracts
# ======================================================================================

def _faces_of(obj, table, prefix):
    pv = np.zeros(len(obj.data.polygons), np.int32)
    obj.data.attributes["part"].data.foreach_get("value", pv)
    ids = {i + 1 for i, n in enumerate(table) if n.startswith(prefix)}
    return [p for p in obj.data.polygons if int(pv[p.index]) in ids]


def _bvh_without(obj, table, exclude_prefixes):
    pv = np.zeros(len(obj.data.polygons), np.int32)
    obj.data.attributes["part"].data.foreach_get("value", pv)
    ex = {i + 1 for i, n in enumerate(table) if n.startswith(tuple(exclude_prefixes))}
    M = obj.matrix_world
    verts = [tuple(M @ v.co) for v in obj.data.vertices]
    polys = [tuple(p.vertices) for p in obj.data.polygons if int(pv[p.index]) not in ex]
    return BVHTree.FromPolygons(verts, polys)


def contracts_authoring(obj, table, args):
    """S1, S2 (M8), S4, S7 (M5), G3, G4 on the joined mesh BEFORE orient_for_net."""
    rep = {}
    # S1: plankfront = ONE quad, +X, 7.50 x 1.50, centre (-0.46, 0, 2.55)
    fs = _faces_of(obj, table, FRONT_PROBE)
    if len(fs) != 1 or len(fs[0].vertices) != 4:
        raise RuntimeError(f"S1: {FRONT_PROBE} has {len(fs)} polygons "
                           f"({[len(f.vertices) for f in fs]} verts), want ONE quad")
    f = fs[0]
    n = obj.matrix_world.to_3x3() @ f.normal
    co = np.array([tuple(obj.matrix_world @ obj.data.vertices[v].co) for v in f.vertices])
    ext = co.max(0) - co.min(0)
    cen = co.mean(0)
    if (n - Vector((1, 0, 0))).length > 1e-4:
        raise RuntimeError(f"S1/G4: plank normal {tuple(n)} != +X")
    if abs(ext[1] - PLANK_W) > 1e-3 or abs(ext[2] - PLANK_H) > 1e-3 or ext[0] > 1e-5:
        raise RuntimeError(f"S1: plank extents {ext} != (0, {PLANK_W}, {PLANK_H})")
    if np.abs(cen - np.array(PLANK_CENTRE_AUTH)).max() > 1e-4:
        raise RuntimeError(f"S1: plank centre {cen} != {PLANK_CENTRE_AUTH}")
    rep["S1"] = dict(extents=[round(float(x), 4) for x in ext], centre=[round(float(x), 4) for x in cen])
    # S2 (M8): 21 x 5 rays along +X from the WE box, 5 m, hit nothing
    bvh = _bvh_without(obj, table, (FRONT_PROBE,))
    hy, z0, z1 = WE_BOX
    hits = []
    for i in range(21):
        for j in range(5):
            p = Vector((PLANK_FRONT_X + 1e-3, -hy + 2 * hy * i / 20, z0 + (z1 - z0) * j / 4))
            h = bvh.ray_cast(p, Vector((1, 0, 0)), 5.0)
            if h[0] is not None:
                hits.append((round(p.y, 2), round(p.z, 2)))
    if hits:
        raise RuntimeError(f"S2: {len(hits)} WE-box rays hit geometry: {hits[:6]}")
    rep["S2"] = "105 rays clear"
    # S4: eave underside >= header top + 0.005; emblem inside the gable triangle, margin
    roof_z = min(float((obj.matrix_world @ obj.data.vertices[v].co).z)
                 for pre in ("roof_", "soffit_", "barge_") for p in _faces_of(obj, table, pre)
                 for v in p.vertices)
    if roof_z < HEADER_BEAM[1] + 0.005 - 1e-6:
        raise RuntimeError(f"S4: roof underside {roof_z:.4f} < header top + 0.005")
    # the slabs really pitch DOWN from the ridge: eave underside on the gable line and
    # nothing of the roof above the ridge cap (an inverted slab passes the line above)
    # lowest slab corner: the eave end face is perpendicular to the slope, so the bottom
    # corner sits h sin(pitch) inboard of the eave line, on the gable top line
    eave_under = GABLE_APEX - (EAVE_Y - 0.5 * ROOF_T * math.sin(math.radians(GABLE_PITCH))) * TAN30
    roof_top = max(float((obj.matrix_world @ obj.data.vertices[v].co).z)
                   for pre in ("roof_", "barge_") for p in _faces_of(obj, table, pre)
                   for v in p.vertices)
    if abs(roof_z - eave_under) > 0.01 or roof_top > RIDGE_CAP[2] + 1e-6:
        raise RuntimeError(f"S4: roof pitch wrong: eave underside {roof_z:.4f} (want "
                           f"{eave_under:.4f}), slab top {roof_top:.4f} (cap {RIDGE_CAP[2]})")
    margin = 1e9
    for p in _faces_of(obj, table, "emblemfront_"):
        for v in p.vertices:
            c = obj.matrix_world @ obj.data.vertices[v].co
            d_base = c.z - GABLE_BASE
            d_rake = ((GABLE_APEX - abs(c.y) * TAN30) - c.z) * math.cos(math.radians(GABLE_PITCH))
            margin = min(margin, d_base, d_rake)
    if margin < 0.10 - 1e-6:
        raise RuntimeError(f"S4: emblem margin to the gable triangle {margin:.3f} < 0.10")
    rep["S4"] = dict(roof_underside_min=round(roof_z, 4), roof_top=round(roof_top, 4),
                     emblem_margin=round(margin, 4))
    # S7 (M5): the whole disc visible along 45-deg-elevation head-on rays
    bvh = _bvh_without(obj, table, ("emblemfront_",))
    d = Vector((math.cos(math.radians(45)), 0.0, math.sin(math.radians(45))))
    blocked = []
    pts = [(0.0, 0.0)] + [(r * math.cos(2 * math.pi * k / 24), r * math.sin(2 * math.pi * k / 24))
                          for r in (0.25, 0.50, EMBLEM_D / 2 - 0.01) for k in range(24)]
    for (py, pz) in pts:
        p = Vector((EMBLEM_FACE_X + 1e-3, py, EMBLEM_Z + pz))
        if bvh.ray_cast(p, d, 30.0)[0] is not None:
            blocked.append((round(py, 2), round(pz, 2)))
    if blocked:
        raise RuntimeError(f"S7: {len(blocked)} of {len(pts)} emblem points hidden at 45 deg: "
                           f"{blocked[:6]}")
    rep["S7"] = f"{len(pts)} emblem points visible at 45 deg"
    # G3 (+ G1 below grade)
    W = K._world_coords(obj)
    lo, hi = W.min(0), W.max(0)
    mx, my = max(-lo[0], hi[0]), max(-lo[1], hi[1])
    if mx > HALF_X - LOT_MARGIN + 1e-6 or my > HALF_Y - LOT_MARGIN + 1e-6 or lo[2] < -1e-5:
        raise RuntimeError(f"G3: |x| {mx:.3f} |y| {my:.3f} min z {lo[2]:.4f} (limits "
                           f"{HALF_X - LOT_MARGIN}, {HALF_Y - LOT_MARGIN}, 0)")
    rep["G3"] = dict(x=(round(float(lo[0]), 3), round(float(hi[0]), 3)),
                     y=(round(float(lo[1]), 3), round(float(hi[1]), 3)),
                     z=(round(float(lo[2]), 3), round(float(hi[2]), 3)),
                     margin_x=round(HALF_X - mx, 3), margin_y=round(HALF_Y - my, 3))
    return rep


def contracts_oriented(obj, table):
    """G4 after orient_for_net (plank normal Blender -Y) and S6 (game-frame plank centre
    = PLANK_CENTRE_GAME via the export map (x, y, z) -> (-x, z, -y): the game negates FBX x)."""
    f = _faces_of(obj, table, FRONT_PROBE)[0]
    n = obj.matrix_world.to_3x3() @ f.normal
    if (n - Vector((0, -1, 0))).length > 1e-4:
        raise RuntimeError(f"G4: oriented plank normal {tuple(n)} != (0, -1, 0)")
    co = np.array([tuple(obj.matrix_world @ obj.data.vertices[v].co) for v in f.vertices]).mean(0)
    game = (float(-co[0]) + 0.0, float(co[2]), float(-co[1]))      # FBX-raw (x, z, -y), then the game negates x
    if max(abs(a - b) for a, b in zip(game, PLANK_CENTRE_GAME)) > 1e-4:
        raise RuntimeError(f"S6: game plank centre {game} != {PLANK_CENTRE_GAME}")
    return dict(plank_centre_game=[round(x, 4) for x in game], normal_game=(0, 0, 1))


def check_band(arrays, cov, rects, A, uv_obj=None, table=None):
    """S3 (texel aspect) + S5 (ControlMask zero under coverage >= 0.5, field slot, R+G+B
    <= 1, BaseColor alpha 1); S3 UV containment is checked by set_band_uvs' pinning."""
    rep = {}
    ctl, base = arrays["ControlMask"], arrays["BaseColor"]
    if (ctl[..., 0:3].sum(-1) > 1.0 + 1e-3).any():
        raise RuntimeError("S5: ControlMask R + G + B > 1")
    if (base[..., 3] < 1.0).any():
        raise RuntimeError("S5: BaseColor alpha < 1")
    for key, ch in (("header", 0), ("emblem", 1)):
        if key not in cov:                  # fine-detail step 3d: the header left for the sign set
            continue
        sl, c = cov[key]
        sub = ctl[sl]
        if (sub[..., 0:3][c >= 0.5] > 0.0).any():
            raise RuntimeError(f"S5: {key} ControlMask non-zero under coverage >= 0.5")
        if not (sub[..., ch][c <= 0.0] == 1.0).all():
            raise RuntimeError(f"S5: {key} field slot {ch + 1} not 1 off the glyphs")
        rep[key] = dict(glyph_texels=int((c >= 0.5).sum()), field_texels=int((c <= 0.0).sum()))
    sl = cs2_compose._rect_slices(rects["header"], A)
    h, w = sl[0].stop - sl[0].start, sl[1].stop - sl[1].start
    exp_h = w / (2 * HEADER_Y) * (HEADER_BEAM[1] - HEADER_BEAM[0])
    if abs(exp_h - h) > 1.0:
        raise RuntimeError(f"S3: header texel aspect off by {exp_h - h:.2f} px")
    sl = cs2_compose._rect_slices(rects["emblem"], A)
    if (sl[0].stop - sl[0].start) != (sl[1].stop - sl[1].start):
        raise RuntimeError("S3: emblem rect not square")
    if max(r[3] for r in rects.values()) > 0.125 + 1e-9:
        raise RuntimeError("band top above v 0.125")
    rep["S3"] = dict(header_px=(w, h), header_aspect_err_px=round(exp_h - h, 3))
    return rep


# ======================================================================================
# Bake hook
# ======================================================================================

def make_post(obj, band_paths, A, dspecs, keys=RESERVED_KEYS):
    rects = [band_rects(A)[k] for k in keys]

    def hook(base, nrm, msk, ctl, ao, mesh, A_):
        # N3: AO was multiplied over the restored band - copy it again, exactly as
        # cs2_bake._restore_reserved does (sRGB -> linear, tint 1, normal GL raw, mask R/A,
        # control RGB)
        px = {m: cs2_bake._load_rgba(p, A, f"band {m}") for m, p in band_paths.items()}
        for r in rects:
            sl = cs2_compose._rect_slices(r, A)
            base[sl][..., 0:3] = K.srgb_to_linear(px["BaseColor"][sl][..., 0:3])
            nrm[sl][..., 0:3] = px["Normal"][sl][..., 0:3]
            msk[sl][..., 0] = px["MaskMap"][sl][..., 0]
            msk[sl][..., 3] = px["MaskMap"][sl][..., 3]
            ctl[sl][..., 0:3] = px["ControlMask"][sl][..., 0:3]
        part_map, ppm = cs2_compose.rasterise_face_fields(mesh, A)
        h = cs2_compose.detail_height(mesh, A, dspecs) if dspecs else None
        uncovered = nrm[..., 0:3].max(axis=-1) < 1e-3
        opts = {"reserved": rects, "part_map": part_map, "noise_amp": 0.03, "gloss_var": 0.03,
                "contact": K.contact_map(mesh, A)}
        base, nrm, msk, ctl = cs2_compose.compose_maps(base, nrm, msk, ctl, None, h, ppm, opts)
        keep = np.zeros((A, A), bool)
        for r in rects:
            keep[cs2_compose._rect_slices(r, A)] = True
        n = nrm[..., 0:3] * 2.0 - 1.0
        n[..., 2] = np.maximum(n[..., 2], 0.05)
        n /= np.maximum(np.linalg.norm(n, axis=-1, keepdims=True), 1e-6)
        fix = ~keep
        nrm[fix, 0:3] = (n * 0.5 + 0.5)[fix]
        nrm[uncovered & fix, 0:3] = (0.5, 0.5, 1.0)
        nrm[..., 3] = 1.0
        hook.density = K.shipped_density(obj, A)
        hook.band_copied = True
        print(f"[sign] compose: band re-copied over AO (N3), relief "
              f"{(float(h.min()), float(h.max())) if h is not None else None}, w=1 median "
              f"{hook.density['median_w1']} px/m @ {A}")
        return base, nrm, msk, ctl
    hook.density = None
    hook.band_copied = False
    return hook


# ======================================================================================
# Run
# ======================================================================================

def mesh_hash(obj):
    import lodge
    return lodge.mesh_hash(obj)


def _sha(path):
    with open(path, "rb") as fh:
        return hashlib.sha1(fh.read()).hexdigest()[:12]


def we_block():
    return {"PLANK_CENTRE_GAME": list(PLANK_CENTRE_GAME), "normal_game": [0.0, 0.0, 1.0],
            "PLANK_W": PLANK_W, "PLANK_H": PLANK_H, "WE_MARGIN": WE_MARGIN,
            "WE_MAX_WIDTH": WE_MAX_WIDTH, "WE_STANDOFF": WE_STANDOFF, "WE_OFFSET": list(WE_OFFSET),
            "layout": WE_LAYOUT, "frame": "game: lot-centre origin at grade, +Z road, y up"}


def _we_mock_object(obj, text, A):
    """Preview-only copy of `obj` + the WE mock text on the plank (post-orient frame), its
    loops mapped to a fully-covered cream texel of the header. Never exported."""
    rects = band_rects(A)
    u0, v0, u1, v1 = rects["header"]
    ctl = None
    tris, w = text_triangles(text, 0.55)
    scale = min(1.0, WE_MAX_WIDTH / max(w, 1e-6))
    mock = obj.copy()
    mock.data = obj.data.copy()
    mock.name = obj.name + "WeMock"
    bpy.context.scene.collection.objects.link(mock)
    bm = bmesh.new()
    bm.from_mesh(mock.data)
    lay = bm.loops.layers.uv.get(cs2_uv.UNIQUE_LAYER)
    # authoring (x, y, z) -> post-orient Blender (y, -x, z); the plank faces Blender -Y
    yb = -(PLANK_FRONT_X + WE_STANDOFF)
    zc = PLANK_CENTRE_AUTH[2]
    cream_uv = getattr(_we_mock_object, "cream_uv", (0.5 * (u0 + u1), 0.5 * (v0 + v1)))
    for t in tris:
        vs = [bm.verts.new((p[0] * scale, yb, zc + p[1] * scale)) for p in t]
        try:
            f = bm.faces.new(vs)
        except ValueError:
            continue
        f.normal_update()
        if f.normal.y > 0:
            f.normal_flip()
        for lp in f.loops:
            lp[lay].uv = cream_uv
    bm.to_mesh(mock.data)
    bm.free()
    return mock


def run(args):
    if not bpy.app.background:
        raise RuntimeError("welcome_sign.py runs headless only (ROUND-SPEC R10)")
    t0 = time.perf_counter()
    name = cs2_asset.validate_asset_name(args.name)
    rep = cs2_validate._Report(name)
    cs2_validate.lint_asset_name(name, rep)
    if any(f[0] == "ERROR" for f in rep):
        raise RuntimeError(f"{name}: name lint {list(rep)}")
    out = os.path.abspath(args.out) if args.out else (IMPORT_DIR if args.export else
                                                      os.path.join(tempfile.gettempdir(), "welcome_sign", "A"))
    asset_dir = os.path.join(out, name)
    A = int(args.tex) if args.finish else 512
    ao = int(args.ao_samples) if args.finish else 0
    clear_scene()
    fine = not getattr(args, "no_fine", False)          # fine-detail step 3d (module docstring)
    bkeys = FINE_KEYS if fine else RESERVED_KEYS
    # 1-2 parts
    parts = build_parts(args, fine)
    names = [o.name for o, _ in parts]
    if len(set(names)) != len(names) or any("." in n for n in names):
        raise RuntimeError(f"part names not unique: {names}")
    signset = None
    if fine:                       # the header faces leave the host for the sign set (cs2_signset)
        import cs2_signset
        # an earlier build in this process left its (unlinked) sign-set object: drop it, so this one is named
        # exactly SIGN_SET.mesh (the FBX model node the importer pairs the textures by)
        for o in [o for o in bpy.data.objects if o.name == SIGN_SET.mesh or o.name.startswith(SIGN_SET.mesh + ".")]:
            me = o.data
            bpy.data.objects.remove(o, do_unlink=True)
            if me is not None and me.users == 0:
                bpy.data.meshes.remove(me)
        parts, routed = cs2_signset.route(parts, SIGN_SET)
        cid = cs2_signset.mint_cid(SIGN_SET) if args.export else (cs2_signset.staged_cid(SIGN_SET)
                                                                   or cs2_signset.PLACEHOLDER_CID)
        sobj = cs2_signset.build_sign_set(routed, SIGN_SET, yaw_deg=0.0)
        signset = {"obj": sobj, "cid": cid, "faces": [o.name for o, _k in routed],
                   "row": FD.sign_set_row(SIGN_SET, cid, bounds=cs2_signset.bounds_game(sobj))}
        for o, _k in routed:
            me = o.data
            bpy.data.objects.remove(o, do_unlink=True)
            bpy.data.meshes.remove(me)
    keys = sorted({k for _o, k in parts})
    # 3-5 bevel, density, tags
    n_bev = K.bevel_parts(parts, prefixes=BEVEL)
    cs2_uv.set_density(parts, DENSITY)
    table = cs2_asset.tag_parts(parts)
    dspecs = K.detail_specs([(o, k) for o, k in parts if k in K.BLDG_MATERIALS])
    # 6 materials: UVTile projection (band placeholder, M6) + kit retile
    K.strip_uv_layers(parts)
    proj = projection_specs(keys)
    cs2_bake.material_sources(proj, parts)
    K.retile([(o, k) for o, k in parts if k in K.BLDG_MATERIALS], proj)
    # 7 join + weld + table + shade
    bpy.ops.object.select_all(action="DESELECT")
    for o, _m in parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0][0]
    bpy.ops.object.join()
    obj = bpy.context.active_object
    obj.name = name
    obj.data.name = name
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=WELD_M)
    bpy.ops.object.mode_set(mode="OBJECT")
    cs2_asset.store_part_table(obj, table)
    cs2_asset.shade_by_angle(obj, SHADE_ANGLE, weighted=True)
    # band UVs (authoring frame), band paint, contracts before orient
    rects = set_band_uvs(obj, table, A, keys=bkeys)
    # per run (name, A, asset folder): two builds at once never share band PNGs
    band_dir = os.path.join(tempfile.gettempdir(), "welcome_sign",
                            f"{name}_{A}_{zlib.crc32(asset_dir.encode('utf-8')):08x}")
    band_paths, band_arrays, cov, band_rep = paint_band(A, args.header, band_dir, name, keys=bkeys)
    band_check = check_band(band_arrays, cov, rects, A)
    pre = contracts_authoring(obj, table, args)
    # 8 orient, G4 / S6, engine base G1 / G2
    cs2_asset.orient_for_net(obj)
    post = contracts_oriented(obj, table)
    ground = ground_prefixes(args)
    base0 = K.check_base_mode(obj, table, "engine", ground)
    n0 = K.assert_budget(obj, BUDGET, 0)
    area = K.area_report(obj)
    result = dict(name=name, asset_dir=asset_dir, tris={0: n0}, contracts=dict(pre, **post),
                  band=dict(band_rep, check=band_check, dir=band_dir,
                            rects={k: [round(x, 6) for x in r]
                                                              for k, r in rects.items() if k in bkeys},
                            png_sha1={m: _sha(p) for m, p in band_paths.items()}),
                  base={0: base0}, area=area, bevelled=n_bev, keys=keys, parts=len(table),
                  hashes={"LOD0_prebake": mesh_hash(obj)}, masts=args.masts,
                  lanterns=not args.no_lanterns, header=args.header)
    if fine:
        result["fine"] = True
        result["sub_objects"] = sub_objects_block(fine_rows() + ((signset["row"],) if signset else ()))
        result["sign_set"] = {"cid": signset["cid"], "faces": signset["faces"], "mesh": SIGN_SET.mesh,
                              "obj": signset["obj"]}
    print(f"[sign] {name}: LOD0 {n0} tris, kept {area['kept_m2']} m2, predicted w=1 px/m "
          f"{area['predicted_ppm_w1']}, contracts {sorted(result['contracts'])}")
    os.makedirs(asset_dir, exist_ok=True)
    if args.blockout:
        _manifest(asset_dir, name, args, result, A, t0)
        print(f"PASS welcome_sign: {name} BLOCKOUT LOD0 {n0}, engine base {base0['verdict']}, "
              f"band {list(bkeys)}")
        return result, obj
    # 9 bake (reserved band, post hook)
    base_ppm = area["predicted_ppm_w1"].get(A) or 0.48 * A / math.sqrt(max(area["sum_w2a"], 1.0))
    sources, mat_info, band_index = bake_sources(keys, base_ppm, band_paths)
    hook = make_post(obj, band_paths, A, dspecs, keys=bkeys)
    world = bpy.context.scene.world or bpy.data.worlds.new("World")
    bpy.context.scene.world = world
    world.light_settings.distance = 2.0
    emissive = not args.no_lanterns and not fine       # fine: the lanterns are vanilla rows
    brep = cs2_bake.bake_unique(obj, asset_dir, name, A, sources,
                                reserved=[rects[k] for k in bkeys],
                                margin_px=pack_margin(A),
                                ao_samples=ao, post_compose=hook, emissive=emissive,
                                src_uv=cs2_bake.TILE_LAYER)
    if not hook.band_copied:
        raise RuntimeError("post hook did not run")
    result["bake"] = {k: brep[k] for k in ("A", "margin_px", "uncovered_texels", "seconds", "files", "ao")}
    result["bake"]["band_index"] = band_index
    if fine:                       # a map this build no longer writes (the old _Emissive) goes
        import lodge
        stale = lodge.remove_stale_maps(asset_dir, name, brep["files"])
        if stale:
            result["bake"]["stale_maps_removed"] = stale
    result["density"] = hook.density
    result["materials"] = mat_info
    result["tex"] = A
    result["hashes"]["LOD0"] = mesh_hash(obj)
    cs2_asset.export_fbx(obj, os.path.join(asset_dir, f"{name}.fbx"))
    # 10 LODs
    lods = []
    if args.lods:
        recipe_like = types.SimpleNamespace(budget=BUDGET, ground_prefixes=ground)
        (t1, t2), plan = K.plan_lod_targets(obj, table, recipe_like, delete_lists=(DELETE1, DELETE2),
                                            protect_prefixes=ground)
        lods, infos = K.lod_chain(obj, name, (t1, t2), (DELETE1, DELETE2), ground)
        result["lod_plan"], result["lod_info"] = plan, infos
        for lvl, lod in enumerate(lods, start=1):
            result["base"][lvl] = K.check_base_mode(lod, table, "engine", ground)
            result["tris"][lvl] = cs2_asset.tri_count(lod)
            if lvl == 2:
                K.assert_budget(lod, BUDGET, 2)
            result["hashes"][f"LOD{lvl}"] = mesh_hash(lod)
            cs2_asset.export_fbx(lod, os.path.join(asset_dir, f"{name}_LOD{lvl}.fbx"))
    cs2_asset.write_settings(asset_dir, name, supplied_lods=tuple(range(1, len(lods) + 1)))
    batches = 2 * (1 + len(lods))
    if batches > 16:
        raise RuntimeError(f"G5: {batches} batches > 16")
    result["batches"] = batches
    # 11 previews / review
    tint = {"RGB"[k - 1]: tuple(v) for k, v in SLOTS.items()}
    W = K._world_coords(obj)
    size = tuple(float(v) for v in (W.max(0) - W.min(0)))
    if lods:
        import cs2_preview
        result["lod_switch_m"] = cs2_preview.lod_switch_table(
            size, [result["tris"][i] for i in sorted(result["tris"])], area=area["kept_m2"])
    if signset is not None:
        result["sign_set"].update(_export_sign_set(signset, args, name, result, size, area))
    if args.preview is not None:
        import cs2_preview
        prev = args.preview or PREVIEW_DIR
        views = [("front", (0.0, -1.0, 0.25)), ("three_quarter", (1.0, -1.2, 0.45)),
                 ("three_quarter_left", (-1.0, -1.2, 0.45)), ("back", (0.3, 1.0, 0.5)),
                 ("game_high", (0.6, -1.0, 1.4)), ("emblem_45", (0.0, -1.0, 1.0))]
        modes = ("pbr", "dusk", "normals", "density") + (("lod",) if lods else ())
        files = cs2_preview.preview(obj, asset_dir, name, prev, modes=modes, views=views,
                                    lods=lods or None, tint=tint)
        if args.we_mock:
            sl, c = cov["header"] if "header" in cov else cov["emblem"]     # fine: the emblem's white
            ii, jj = np.argwhere(c >= 0.999)[len(np.argwhere(c >= 0.999)) // 2]
            _we_mock_object.cream_uv = ((sl[1].start + jj + 0.5) / A, (sl[0].start + ii + 0.5) / A)
            mock = _we_mock_object(obj, args.we_mock, A)
            files += cs2_preview.preview(mock, asset_dir, name, prev, modes=("pbr",),
                                         views=[("plank_we_mock", (0.0, -1.0, 0.15))], tint=tint)
            me = mock.data
            bpy.data.objects.remove(mock, do_unlink=True)
            bpy.data.meshes.remove(me)
        obj.data.materials.clear()
        result["previews"] = [os.path.relpath(f, REPO) for f in files]
    if args.review is not None:
        import cs2_review
        result["review_pack"] = cs2_review.write_pack(
            name, asset_dir, dict(enumerate([obj] + lods)), {},
            meta={"generator": "welcome_sign.py", "args": vars(args), "kind": "building",
                  "caps": {0: BUDGET.lod0_cap, 1: result["tris"].get(1), 2: BUDGET.lod2_cap},
                  "letter": "A", "tint": tint, "lot_cells": list(LOT)},
            out_dir=args.review or None,
            **({"manifest": {"sub_objects": result["sub_objects"]}} if fine else {}),
            **({"extra_meshes": {"SignSet": (signset["obj"], *result["sign_set"]["texture"])}}
               if signset is not None and result["sign_set"].get("texture") else {}))
    result["extent_game_m"] = {"x": round(size[0], 2), "y": round(size[2], 2), "z": round(size[1], 2)}
    for lod in lods:
        me = lod.data
        bpy.data.objects.remove(lod, do_unlink=True)
        bpy.data.meshes.remove(me)
    _manifest(asset_dir, name, args, result, A, t0)
    if not args.no_validate:
        obj.name, obj.data.name = name + "Src", name + "Src"
        findings = cs2_validate.validate_asset_folder(asset_dir, name, {
            "kind": "building", "lod0_cap": BUDGET.lod0_cap, "lod2_cap": BUDGET.lod2_cap,
            "hero": True, "expected_lods": tuple(range(1, len(lods) + 1)),
            "expected_submeshes": (), "max_texture": max(A, 1024), "base_mode": "engine",
            "lot_m": (8 * LOT[0], 8 * LOT[1])})
        errs = [f for f in findings if f[0] == "ERROR"]
        warns = [f for f in findings if f[0] == "WARN"]
        result["validate"] = {"errors": len(errs), "warnings": len(warns),
                              "warn": [f[1:] for f in warns][:12]}
        print(f"[sign] validate_asset_folder: {len(errs)} errors, {len(warns)} warnings")
        if emissive:
            w_, h_, _d, _c = cs2_validate.read_png_header(os.path.join(asset_dir, f"{name}_Emissive.png"))
            if (w_, h_) != (A, A):
                raise RuntimeError(f"_Emissive {w_}x{h_} != {A}")
    chain = [result["tris"][i] for i in sorted(result["tris"]) if i]
    print(f"[sign] asset folder: {asset_dir}")
    print(f"PASS welcome_sign: {name} LOD0 {n0}, chain {chain}, engine base {base0['verdict']}, "
          f"tex {A}, px/m med {(result.get('density') or {}).get('median_w1')}, masts {args.masts}")
    return result, obj


def _manifest(asset_dir, name, args, result, A, t0):
    data = {
        "generator": "welcome_sign.py", "spec": SPEC_TAG, "prefab": PREFAB, "args": vars(args),
        "kind": "building", "lot": list(LOT), "tris": result.get("tris"),
        "budget": vars(BUDGET), "parts": result.get("parts"), "materials_used": result.get("keys"),
        "materials": result.get("materials"), "tex": result.get("tex", A),
        "bake": result.get("bake"), "shipped_density": result.get("density"),
        "area_report": result.get("area"), "engine_base": result.get("base"),
        "contracts": result.get("contracts"), "band": result.get("band"),
        "lod_plan": result.get("lod_plan"), "lod_info": result.get("lod_info"),
        "lod_switch_m": result.get("lod_switch_m"), "batches_per_mesh_group": result.get("batches"),
        "extent_game_m": result.get("extent_game_m"), "mesh_hash": result.get("hashes"),
        "slots_variation_set": {str(k): list(v) for k, v in SLOTS.items()},
        "emissive": ({"purpose": "BuildingActive 72", "intensity": 0.6}
                     if not args.no_lanterns and not result.get("fine") else None),
        "constants": {"PORTAL_X": PORTAL_X, "PIER_Y": PIER_Y, "PLANK": [PLANK_W, PLANK_H, PLANK_T],
                      "PLANK_Z": list(PLANK_Z), "HEADER": [2 * HEADER_Y, HEADER_BEAM[1] - HEADER_BEAM[0]],
                      "GABLE": [GABLE_HALF, GABLE_PITCH, round(GABLE_APEX, 4)],
                      "EMBLEM": [EMBLEM_D, round(EMBLEM_Z, 4), EMBLEM_FACE_X],
                      "RAKE_X": [RAKE_BACK_X, RAKE_FRONT_X], "BARGE_T": BARGE_T,
                      "MASTS": [list(m) for m in MASTS[args.masts]], "FLAG": list(FLAG)},
        "we": we_block(),
        "review_pack": result.get("review_pack"), "previews": result.get("previews"),
        "total_seconds": round(time.perf_counter() - t0, 1),
    }
    if result.get("fine"):         # fine-detail step 3d: keys only on the fine build (--no-fine unchanged)
        data["sub_objects"] = result.get("sub_objects")
        ss = {k: v for k, v in (result.get("sign_set") or {}).items() if k not in ("obj", "texture")}
        data["fine_detail"] = {"fine_detail": True, "x_convention": FD.X_CONVENTION, "sign_set": ss or None,
                               "rows": (result.get("sub_objects") or {}).get("kinds"),
                               "header": {"split_y": GATE_SPLIT_Y, "band_keys": list(FINE_KEYS),
                                          "was": "vector Bfont at 186 px/m in the host band"},
                               "masts_segs": MAST_SEGS_FINE,
                               "placeholders": ["masts + flags (FlagPole01 / 02: Editor harvest, H5)"]}
    cs2_validate.write_manifest(asset_dir, name, data)


def sub_objects_block(rows):
    """The manifest's sub_objects block for SkiResortGate (lodge.sub_objects_block's shape and checks:
    verified GUIDs, kit / sign-set CIDs) + the fine-detail x convention and row kinds."""
    import types
    import lodge
    rec = types.SimpleNamespace(key="sign", prefab=PREFAB, sub_objects=tuple(rows))
    block = lodge.sub_objects_block(rec)
    block["notes"] = SUB_OBJECT_NOTE
    block["x_convention"] = FD.X_CONVENTION
    block["kinds"] = {"vanilla": sum(1 for o in block["objects"] if o.get("kind") != "mod"),
                      "mod": sum(1 for o in block["objects"] if o.get("kind") == "mod")}
    return block


def _export_sign_set(signset, args, name, result, size, area):
    """Export SkiSignSetGateB (cs2_signset) once the LOD chain exists: its cull targets the sign's LOD2
    switch (design C.4). Returns the numbers merged into result["sign_set"] (+ "texture" for the pack)."""
    import cs2_preview
    import cs2_signset
    sw = (result.get("lod_switch_m") or {}).get("switch") or {}
    target = sw.get(2) or sw.get("2") or (max(sw.values()) if sw else None) or \
        cs2_preview.lod_switch_table(size, [result["tris"][0]], area=area["kept_m2"])["cull"]
    letter_dir = os.path.abspath(args.signset_out) if args.signset_out else \
        cs2_signset.letter_dir_for(SIGN_SET, args.export, args.out)
    host = {"name": name, "recipe": "welcome_sign", "prefab": PREFAB,
            "lot_half_game": [4.0 * LOT[0], 4.0 * LOT[1]], "switch_lod2_m": round(float(target), 2),
            "host_tris": result["tris"]}
    rep = cs2_signset.export_sign_set(signset["obj"], SIGN_SET, letter_dir, host=host, cid=signset["cid"],
                                      mint=bool(args.export))
    owner_dir = os.path.join(os.path.dirname(letter_dir), cs2_signset.SHEET_LETTER, cs2_signset.SHEET_OWNER)
    if not os.path.isdir(owner_dir):
        owner_dir = cs2_signset.owner_dir_shipped()
    val = cs2_signset.validate_sign_set(rep["asset_dir"], rep["name"], owner_dir)
    print(f"[sign] sign set {rep['name']}: {rep['tris']} tris, cull m_Bias {rep['cull']['m_Bias']} (target "
          f"{rep['cull']['target_m']} m), bounds {rep['bounds_game']}, validate {len(val['errors'])} errors / "
          f"{len(val['warnings'])} warnings -> {rep['asset_dir']}")
    if val["errors"]:
        raise RuntimeError(f"sign set {rep['name']}: validator errors {val['errors'][:4]}")
    return {"asset_dir": os.path.relpath(rep["asset_dir"], REPO).replace("\\", "/")
            if rep["asset_dir"].startswith(REPO) else rep["asset_dir"],
            "tris": rep["tris"], "cull": rep["cull"], "bounds_game": rep["bounds_game"],
            "row": {k: (list(v) if isinstance(v, tuple) else v) for k, v in rep["row"].items()},
            "owner": rep["owner"], "validate": val,
            "staged": [os.path.relpath(p, REPO).replace("\\", "/") for p in rep["staged"]],
            "texture": (cs2_signset.SHEET_OWNER, owner_dir)}


def main():
    args = parse_args()
    run(args)


if __name__ == "__main__":
    try:
        main()
    except Exception:  # noqa: BLE001 - unambiguous exit status for the harness
        traceback.print_exc()
        sys.exit(1)
