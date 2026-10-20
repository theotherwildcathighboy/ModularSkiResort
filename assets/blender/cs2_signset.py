"""
cs2_signset.py - the shared BUILDING SIGN SHEET and the per-host SIGN-SET props (fine-detail
pass, design/fine-detail-pass.md C as cut by -skeptic.md FS5 / FS12 / FS14 / FS15 / FS16;
Richard's seventeenth look H2: name signs glow faintly at night when powered, intensity 0.2).
Build report: docs/research/art/assets-2026-10/build/fine-detail-step2-engine.md.

WHY. The building signs were stamped into each host's atlas with a 5x7 bitmap font at
38-122 px/m (4-6 texels per letter cell): "some of the TEXT SIGNS are PIXELATED". Here every
label face of an opted-in host leaves the host and is drawn by ONE small sign-set prop per host
(one quad per label face at its exact as-built position, placed by an identity sub-object row)
from ONE shared 2048^2 five-map sheet at ~300 px/m in the VECTOR font (Blender's Bfont through
cs2_liftdress._text_cov, the lift boards' path). Lift boards stay as they are (FS5: the game
camera cannot get inside 10 m, where the 400 px/m boards are already oversampled).

THE SHEET (SHEET, below; FROZEN and APPEND-ONLY, SS4)
    2048^2, maps BaseColor / ControlMask / MaskMap / Normal / Emissive, SHEET_PPM 300 px/m,
    GUTTER 8 px between rects and from the edges, each painted rect's border dilated DILATE px
    into its gutter (mip-safe). Owned by the FIRST sign-set prop, SkiSignSetSchoolB: its folder
    holds the only five PNGs. Every later host's sign set is a SHARER (settings.json
    sharedAssets -> the owner's PNGs, cs2_asset.write_settings(shared_from=...), probe F1).
    Kinds of APPROVED hosts are painted (school, L2); kinds of PENDING hosts are BLANK reserved
    rects (FS14: P1, P2, L5, tram), painted when Richard approves that host's design. Once the
    sheet is imported its rects never move (a sharer's UVs point into them): a new kind goes into
    free space (append a Kind with a rect inside the free area), a CHANGED painted rect after
    import means a new owner letter (both sheets coexist until the sharers migrate, design C.2).
    Lit kinds (Emissive): the NAME signs only (SKI SCHOOL, RESTAURANT, SKI PATROL x2 when
    painted): emissive = the letters' coverage x EMISSIVE_SRGB; surgery sets the importer's
    EmissiveProperties intensity 5 -> EMISSIVE_INTENSITY 0.2 (vanilla shop sign, design C.2).
    ControlMask 0 everywhere (signs are never tinted), MaskMap metal 0 / smoothness SMOOTH,
    Normal flat (printed boards; the frames stay in the host atlas and weather with it).

THE SIGN-SET PROP (per host; recipe opt-in fine_detail.SignSetSpec)
    route(parts, spec)              the spec's label faces leave the host part list
    host_recipe(recipe, spec)       the host recipe without those faces' label entries
    build_sign_set(faces, spec, yaw_deg)   one mesh: each face's own world quad (the host's
                                    authoring frame), UVs = its sheet rect laid upright as seen
                                    from outside (right = -n x up), then the host's yaw and
                                    cs2_asset.orient_for_net's -90 deg about Z, so the FBX lands
                                    in the host's own frame and the identity row puts every board
                                    exactly where the host had it (frame-free: SG2)
    export_sign_set(obj, spec, letter_dir, host=...)   FBX + (owner) the five sheet PNGs +
                                    settings.json + manifest; stage_prefabs: the object prefab
                                    template + hand-minted .cid + the mesh-prefab surgery
                                    fragments (EmissiveProperties intensity, LodProperties m_Bias)
    The row: fine_detail.sign_set_row (kind "mod", role "signset", identity, m_ParentMesh 0).
    Cull (design C.4 recomputed with the 1.6.2f1 formula, FS10, [U] until SkiProbeLodA): a
    LodProperties component with an EMPTY m_LodMeshes and m_Bias = log2(cull at bias 0 / the
    host's LOD2 switch) (MeshSystem.cs:458 m_LodBias = m_Bias; the <= 5 m small-mesh rule of
    :497-503 applies only WITHOUT LodProperties; :505-511 thin-mesh term; positive bias has no
    clamp, LodProperties.cs:10).

FOLDER LAYOUT (decided here; build report section 3)
    assets/import/signs/B/SkiSignSetSchoolB/   the OWNER: FBX + the five sheet PNGs + settings {}
    assets/import/signs/B/SkiSignSet<Host>B/   a sharer imported in the SAME round as the owner:
                                               sharedAssets "../SkiSignSetSchoolB/SkiSignSetSchoolB_<Map>.png"
    assets/import/signs/<L>/SkiSignSet<Host><L>/  a sharer of a LATER round (new letter, FS15):
                                               sharedAssets "../../B/SkiSignSetSchoolB/SkiSignSetSchoolB_<Map>.png"
                                               [U] whether the importer accepts a target outside the
                                               imported folder's parent; fallback = repoint the
                                               sharer mesh's m_SurfaceAssets to the owner Surface CID
                                               (FS8, the funicular pattern) - no PNG path at all
    assets/staging/prefabs/signs/<L>/SkiSignSet<Host>/   object prefab + .cid + surgery fragments

NITPICK C (2026-10-13, build/nitpick-c-lodges.md; Richard, twenty-fifth look: the Bfont lettering "just looks too
AI"): the lettered boards are drawn in INSTALLED Windows typefaces through the same vector path (draw kind "type":
a FONT curve with the TTF loaded; TYPEFACES pins each file's sha1, the file is never copied or shipped): Bookman
Old Style Bold for the resort-identity boards (GRAND LODGE with a rule and lozenges, SKI AREA), Franklin Gothic
Demi Cond for every name / utility board; hut.picto is a blank board (draw kind "blank", fine_detail.blank_board).
In place, inside the rects (layout hashes unchanged); the Bfont "text" path is unchanged for the bib numbers.

CONTRACTS (tests/test_signset.py, each with a negative): SS1 rects inside the sheet, gutter >= 6
px, no overlap; SS2 vector only (cs2_bldg._font5x7 never called while painting); SS3 stroke >= 4
px on every word; SS4 the frozen layout hash; SS5 emissive only inside lit rects; SS6
ControlMask 0; SS7 determinism. SG1 each face's UV inside its rect +-0.5 texel; SG2 each board's
position = the pre-change host board +-1 mm; SG3 no routed face left in the host (LOD0-2) or its
labels block; SG4 exactly one identity row in the host manifest; SG5 sign-set bounds inside the
lot; SG6 tris <= SG6_CAP; SG7 cs2_asset.write_settings default output byte-identical on every
shipped settings.json.

bpy is imported lazily (the sheet table, layout checks and row data work without Blender).
"""

import hashlib
import json
import math
import os
import sys
import uuid
import zlib
from dataclasses import dataclass, replace

import numpy as np

_HERE = os.path.dirname(os.path.abspath(__file__))
if _HERE not in sys.path:
    sys.path.insert(0, _HERE)
import fine_detail as FD    # noqa: E402

REPO = os.path.normpath(os.path.join(_HERE, os.pardir, os.pardir))
IMPORT_ROOT = os.path.join(REPO, "assets", "import")
STAGING_ROOT = os.path.join(REPO, "assets", "staging", "prefabs", "signs")

SHEET_OWNER_PROP = "SkiSignSetSchool"
SHEET_LETTER = "B"
SHEET_OWNER = SHEET_OWNER_PROP + SHEET_LETTER        # SkiSignSetSchoolB: holds the PNGs
SHEET_A = 2048
SHEET_PPM = 300.0
GUTTER = 8
DILATE = 4                  # px of each painted rect's border extended into its gutter
MAPS = ("BaseColor", "ControlMask", "MaskMap", "Normal", "Emissive")
SS = 4                      # supersamples per texel axis (shapes; text uses cs2_liftdress TEXT_SS 8)
SHEET_BG_SRGB = (0.30, 0.30, 0.30)
SMOOTH = 0.30
EMISSIVE_SRGB = (1.00, 0.90, 0.70)    # warm white letters (probe card A)
EMISSIVE_INTENSITY = 0.2              # surgery value (vanilla shop sign; H2)
SG6_CAP = 400
LOD_FACTOR = 652.0                    # [U] until SkiProbeLodA
TEXT_FILL = 0.85                      # a line's width <= TEXT_FILL x the face width
TEXT_CAP_FRAC = 0.55                  # single line cap <= 0.55 x face height
TIER_T1 = (0.40, 4.0)                 # cap >= m, stroke >= px (design 2.0)

# encoded sRGB colors (cs2_bldg constants, recipes/school.py, recipes/lodge_hut.py)
WHITE = 0.92
BOARD_BROWN = (0.20, 0.14, 0.10)
SCHOOL_RED = (0.62, 0.10, 0.08)
HUT_RED = (0.62, 0.10, 0.08)
CHALK = (0.10, 0.22, 0.16)
TRAIL_GREEN, TRAIL_BLUE, TRAIL_BLACK = (0.08, 0.50, 0.20), (0.08, 0.28, 0.72), (0.05, 0.05, 0.05)


def _seed(face):
    return zlib.crc32(face.encode("utf-8")) & 0xFFFF


# ======================================================================================
# The sheet: FROZEN, APPEND-ONLY (rects in texels, x right, y = UV v x A, bottom-up rows)
# ======================================================================================

@dataclass(frozen=True)
class Kind:
    key: str            # "<host>.<board>"
    host: str           # school | hut | p1 | p2 | l5 | tram
    size: tuple         # (w, h) meters of the face
    rect: tuple         # (x0, y0, x1, y1) texels
    state: str          # "painted" | "reserved"
    lit: bool = False   # name sign: Emissive letters
    draws: tuple = ()   # ((draw kind, kwargs), ...) - see DRAW_KINDS
    note: str = ""


_LESSONS = (("circle", 0.80, TRAIL_GREEN), ("square", 0.70, TRAIL_BLUE),
            ("diamond", 0.86, TRAIL_BLACK), ("glyph:cand_d", 0.86, SCHOOL_RED))
_LESSON_RECTS = ((1524, 436, 1704, 616), (1712, 436, 1892, 616), (1516, 1112, 1696, 1292),
                 (1704, 1112, 1884, 1292))
_DOOR_RECTS = ((1900, 436, 1990, 526), (1884, 834, 1974, 924), (1892, 1112, 1982, 1202),
               (1884, 1360, 1974, 1450))

#: the layout (packed once 2026-10-12 by shelf_pack in height order, then frozen; 68.1 % of the
#: texels, rows 1796-2040 and x 736-2040 of the last shelf free for appends). Approved hosts
#: painted, pending hosts reserved blank (FS14).
SHEET = (
    Kind("school.name", "school", (4.20, 0.90), (8, 834, 1268, 1104), "painted", True,
         (("text", {"lines": ("SKI SCHOOL",), "fg": WHITE, "bg": BOARD_BROWN}),)),
    Kind("school.meet", "school", (2.40, 1.20), (796, 436, 1516, 796), "painted", False,
         (("shapes", {"items": (("square", 0.21, 0.5, 0.80, SCHOOL_RED),
                                ("glyph:cand_d", 0.21, 0.5, 0.68, WHITE),
                                ("circle", 0.525, 0.5, 0.333, TRAIL_GREEN),
                                ("square", 0.7083, 0.5, 0.283, TRAIL_BLUE),
                                ("diamond", 0.8917, 0.5, 0.383, TRAIL_BLACK)), "bg": WHITE}),)),
    Kind("school.bibs", "school", (2.00, 0.90), (1276, 834, 1876, 1104), "painted", False,
         (("bibs", {"n": 8, "seed": _seed("sign_bibs_face"), "bg": BOARD_BROWN}),)),
    Kind("school.notice", "school", (0.70, 1.00), (1396, 8, 1606, 308), "painted", False,
         (("notices", {"seed": _seed("sign_notice_face")}),)),
) + tuple(
    Kind(f"school.lesson{k}", "school", (0.60, 0.60), _LESSON_RECTS[k], "painted", False,
         (("shapes", {"items": ((kind, 0.5, 0.5, size, rgb),), "bg": WHITE}),))
    for k, (kind, size, rgb) in enumerate(_LESSONS)
) + tuple(
    # school revision 2 (2026-10-12, build/school-rev2.md): the door numbering is gone; the four
    # frozen rects stay allocated and are repainted plain school red (no digits, no face uses them)
    Kind(f"school.door{n}", "school", (0.30, 0.30), _DOOR_RECTS[n - 1], "painted", False,
         (("shapes", {"items": (), "bg": SCHOOL_RED}),),
         note="revision 2: plain red plate, no digit (unused by the school)")
    for n in (1, 2, 3, 4)
) + (
    # L2 (approved; its sign set is a later fleet's, a SHARER of this sheet): painted now
    Kind("hut.name", "hut", (5.00, 0.80), (8, 1112, 1508, 1352), "painted", True,
         (("text", {"lines": ("RESTAURANT",), "fg": WHITE, "bg": HUT_RED}),)),
    Kind("hut.picto", "hut", (2.40, 0.60), (8, 1608, 728, 1788), "painted", False,
         (("pictogram", {"kind": "restaurant", "square": True, "bg": BOARD_BROWN}),)),
    Kind("hut.menu", "hut", (0.55, 0.80), (1787, 8, 1952, 248), "painted", False,
         (("menu", {"bg": CHALK}),)),
    # PENDING hosts (FS14): blank, sized from their shipped faces; painted at approval
    Kind("p1.name", "p1", (2.60, 1.30), (8, 436, 788, 826), "reserved", True,
         note="SKI / PATROL (recipes/patrol.py P1_NAME)"),
    Kind("p1.info", "p1", (0.55, 0.85), (1614, 8, 1779, 263), "reserved", False,
         note="info board (P1_BOARD)"),
    Kind("p2.name", "p2", (4.60, 1.40), (8, 8, 1388, 428), "reserved", True,
         note="SKI PATROL / FIRST AID (P2_NAME)"),
    Kind("l5.name", "l5", (5.00, 0.80), (8, 1360, 1508, 1600), "reserved", True,
         note="L5 rev 3 carries no word board today: one name-board slot held for its approval"),
    Kind("tram.stn", "tram", (1.20, 0.50), (1516, 1360, 1876, 1510), "reserved", False,
         note="tram top TRAM board (tram_station.py label_stn) if rev 2 moves it off the host"),
)
# ---- APPENDED 2026-10-12, school revision 2 (build/school-rev2.md): the photos' RED MARKINGS, in
# free space of the last shelf (x 744-1112, rows 1616-1724; 8 px gutters). The legends are sized for
# SS3 (>= 4 px strokes at 300 px/m): the front-door placard reads "STAFF / ONLY" (the photo's own
# two-line legend is illegible: a stand-in), the back sign "NO / SLEDDING" (legible in 120856)
SHEET = SHEET + (
    Kind("school.markstaff", "school", (0.44, 0.36), (744, 1616, 876, 1724), "painted", False,
         (("text", {"lines": ("STAFF", "ONLY"), "fg": WHITE, "bg": SCHOOL_RED, "cap_frac": 0.66,
                    "pitch": 1.35}),),
         note="red placard on the white doors (original photo; legend a stand-in)"),
    Kind("school.marknosled", "school", (0.76, 0.36), (884, 1616, 1112, 1724), "painted", False,
         (("text", {"lines": ("NO", "SLEDDING"), "fg": WHITE, "bg": SCHOOL_RED, "cap_frac": 0.62,
                    "pitch": 1.30}),
          ("frame", {"width": 0.018, "inset": 0.014, "fg": WHITE})),
         note="red NO SLEDDING sign with a white rim (Street View 120856)"),
)
# ---- PAINTED 2026-10-12, fine-detail step 3d (build/fine-detail-step3d-patrol-sign.md): Richard approved the
# patrol shacks (eighteenth look: "Ski patrol shacks look good"; "some of the signs ... are pixelated but I think
# we know our plan for that"), so their three RESERVED rects are painted in place (FS14). Only `state`, `draws`
# and `note` change: key, size, rect and lit are untouched, so every layout_hash prefix is unchanged. Vector
# Bfont only (the 5x7 bitmap font is retired for them); the two name boards are lit (H2, 0.2 at surgery).
PATROL_RED = (0.72, 0.09, 0.08)       # recipes/patrol.PATROL_RED = cs2_bldg.PATROL_RED_SRGB
PATROL_WHITE = 0.92                   # recipes/patrol.WHITE
#: the P1 info board (recipes/_civic.BarsPainter, contract 3.5): white field, three LINEAR-rgb bars
#: (center height 0..1 from the bottom, length share), left-aligned as the viewer sees the board
INFO_BARS_LIN = ((0.78, 1.00, (0.05, 0.20, 0.45)), (0.52, 0.80, (0.05, 0.30, 0.12)),
                 (0.26, 0.55, (0.05, 0.20, 0.45)))
_PATROL_PAINT = {
    "p1.name": ((("text", {"lines": ("SKI", "PATROL"), "fg": PATROL_WHITE, "bg": PATROL_RED,
                           "cap_frac": 0.75, "pitch": 1.35}),),
                "SKI / PATROL white on patrol red (recipes/patrol.py P1_NAME); painted at approval"),
    "p1.info": ((("bars", {"bars_lin": INFO_BARS_LIN, "white": 0.90}),),
                "info board, three colour bars (P1_BOARD, _civic.BarsPainter); painted at approval"),
    "p2.name": ((("text", {"lines": ("SKI PATROL", "FIRST AID"), "fg": PATROL_WHITE, "bg": PATROL_RED,
                           "cap_frac": 0.75, "pitch": 1.35}),),
                "SKI PATROL / FIRST AID white on patrol red (P2_NAME); painted at approval"),
}
SHEET = tuple(replace(k, state="painted", draws=_PATROL_PAINT[k.key][0], note=_PATROL_PAINT[k.key][1])
              if k.key in _PATROL_PAINT else k for k in SHEET)
# ---- APPENDED 2026-10-12, fine-detail step 3d: the WELCOME SIGN header (welcome_sign.py, SkiResortGate). Its
# "SKI AREA" header was vector Bfont at 186 px/m in the host's band (< 300): it moves to this sheet. A whole
# 4.00 x 0.60 m header (1,200 px) fits only in the top rows and would leave 72 free rows (SS1 keeps >= 200 for
# appends), so the header face is split where it is plain field (y +-1.30, between the letters' +-1.21 and the
# snowflakes' +-1.47): the 2.60 m lettering panel (780 px, the last shelf's free run x 1260-2040) and two
# 0.70 m snowflake end panels that share ONE rect (the flake is centered in each; free space above the lessons).
# Field = the color the host band's slot-1 field shows: welcome_sign.SLOTS[1] (0.15, 0.31, 0.225) multiplied into
# the band's 0.80 albedo in LINEAR space (cs2_preview._tinted_base_image's model, the look Richard reviews), so the
# panel meets the beam trim without a step; ControlMask 0 here (the gate has one VariationSet). [U] how the game
# applies a VariationSet color (as linear like the preview, or decoded from sRGB, which would read darker on the
# host) decides the in-game match; the field is one constant, repaintable before the first import.
GATE_FIELD = (0.333, 0.47, 0.404)     # lin_to_srgb(srgb_to_lin(0.80) x (0.15, 0.31, 0.225))
GATE_CREAM = (0.93, 0.89, 0.78)       # welcome_sign.CREAM_SRGB
SHEET = SHEET + (
    Kind("gate.header", "gate", (2.60, 0.60), (1260, 1608, 2040, 1788), "painted", True,
         (("text", {"lines": ("SKI AREA",), "fg": GATE_CREAM, "bg": GATE_FIELD, "cap_frac": 0.70,
                    "fill": 0.95}),),
         note="SKI AREA lettering panel of the welcome sign header (welcome_sign HEADER_TEXT, cap 0.42 m = "
              "HEADER_CAP); lit: the resort name board (H2)"),
    Kind("gate.flake", "gate", (0.70, 0.60), (1524, 624, 1734, 804), "painted", False,
         (("flake", {"d": 0.36, "fg": GATE_CREAM, "bg": GATE_FIELD}),),
         note="snowflake end panel of the welcome sign header (ORNAMENT_D 0.36 m, centred); used twice (L / R)"),
)
# ---- PAINTED 2026-10-12, fine-detail step 3e (build/fine-detail-step3e-swanky.md): Richard approved L5 revision 4
# (twenty-third look: "L5 and L4 look good, run the engine pass on them too!"), so its RESERVED name slot is painted in
# place (FS14): "GRAND LODGE" (the building's in-game name, LocaleEN SkiLodgeGrand) in the welcome sign's cream on the
# school's board brown, the porte-cochere name board on the front truss (recipes/l5_swanky_bottom.py NB_*); lit (H2,
# 0.2 at surgery). Only state / draws / note change: key, size, rect and lit are untouched, so every layout_hash prefix
# (20 / 22 / 24) is unchanged. Vector Bfont only.
_GRAND_PAINT = {
    "l5.name": ((("text", {"lines": ("GRAND LODGE",), "fg": GATE_CREAM, "bg": BOARD_BROWN}),),
                "GRAND LODGE cream on board brown, the L5 porte-cochere name board (recipes/l5_swanky_bottom NB_TEXT); "
                "painted at approval (step 3e)"),
}
SHEET = tuple(replace(k, state="painted", draws=_GRAND_PAINT[k.key][0], note=_GRAND_PAINT[k.key][1])
              if k.key in _GRAND_PAINT else k for k in SHEET)
# ---- REPAINTED 2026-10-13, nitpick C (build/nitpick-c-lodges.md; Richard, twenty-fifth look: L5 "use a DIFFERENT
# FONT. Hard to explain but it just looks too AI", ski school "Same comment as the swanky lodge on the fonts", L3 / L4
# "not loving the SIGN of the FORK AND KNIFE ... leave it blank or keep it simple"). In place, within the rects: only
# `draws` and `note` change (key, size, rect and lit untouched), so every layout_hash prefix (20 / 22 / 24) is
# unchanged (SS4). Nothing is imported yet (S1 imports the fine versions), so no new owner letter is needed (section 3
# of the step-2 report: a changed painted rect AFTER an import needs one). Faces (font table in the report; the TTFs
# are rendered here, never shipped): the resort-identity boards in Bookman Old Style Bold, letter-spaced caps (GRAND
# LODGE with the lodge-sign rule and diamonds; the welcome sign's SKI AREA), every utility / name board in ONE
# condensed sans, Franklin Gothic Demi Cond. hut.picto (the same fork-and-knife glyph) becomes a blank board. Kinds
# not listed keep their draws byte for byte (Bfont bib numbers, pictograms, notices, menu, flakes, bars).
NC_LODGE_FACE = "bookman_bold"
NC_UTILITY_FACE = "franklin_demi_cond"
NC_RULE = ("frame", {"width": 0.015, "inset": 0.05, "fg": GATE_CREAM})
L2_TRIM_SRGB = (0.214, 0.472, 0.425)  # the hut's slot-1 trim (0.0625, 0.3125, 0.25) as shown on a 0.80 paint
#                                       albedo: lin_to_srgb(srgb_to_lin(0.80) x slot), the GATE_FIELD model
_NC_PAINT = {
    "l5.name": ((("type", {"lines": ("GRAND LODGE",), "face": NC_LODGE_FACE, "fg": GATE_CREAM, "bg": BOARD_BROWN,
                           "tracking": 0.08, "fill": 0.90, "flank": "diamond", "flank_frac": 0.36,
                           "flank_gap": 0.45}), NC_RULE),
                "GRAND LODGE in Bookman Old Style Bold, letter-spaced caps between two lozenges, a thin cream rule "
                "inside the face; cream on board brown (nitpick C; was Bfont, step 3e)"),
    "gate.header": ((("type", {"lines": ("SKI AREA",), "face": NC_LODGE_FACE, "fg": GATE_CREAM, "bg": GATE_FIELD,
                               "cap_frac": 0.70, "fill": 0.95, "tracking": 0.06}),),
                    "SKI AREA lettering panel of the welcome sign header in Bookman Old Style Bold (the resort-identity "
                    "face; nitpick C, was Bfont); lit: the resort name board (H2)"),
    "school.name": ((("type", {"lines": ("SKI SCHOOL",), "face": NC_UTILITY_FACE, "fg": WHITE, "bg": BOARD_BROWN,
                               "tracking": 0.06}),),
                    "SKI SCHOOL in Franklin Gothic Demi Cond, white on board brown (nitpick C, was Bfont)"),
    "hut.name": ((("type", {"lines": ("RESTAURANT",), "face": NC_UTILITY_FACE, "fg": WHITE, "bg": HUT_RED,
                            "tracking": 0.06}),),
                 "RESTAURANT in Franklin Gothic Demi Cond, white on hut red (nitpick C, was Bfont)"),
    "hut.picto": ((("blank", {"bg": BOARD_BROWN, "rim": L2_TRIM_SRGB, "rim_m": 0.02, "inset_m": 0.05}),),
                  "BLANK board (nitpick C: the fork-and-knife pictogram Richard called 'quite bad' on L3 / L4 is the same "
                  "glyph): board brown with a thin rim in the hut's green trim; a label surface for the player"),
    "p1.name": ((("type", {"lines": ("SKI", "PATROL"), "face": NC_UTILITY_FACE, "fg": PATROL_WHITE, "bg": PATROL_RED,
                           "cap_frac": 0.75, "pitch": 1.35, "tracking": 0.04}),),
                "SKI / PATROL in Franklin Gothic Demi Cond, white on patrol red (nitpick C, was Bfont)"),
    "p2.name": ((("type", {"lines": ("SKI PATROL", "FIRST AID"), "face": NC_UTILITY_FACE, "fg": PATROL_WHITE,
                           "bg": PATROL_RED, "cap_frac": 0.75, "pitch": 1.35, "tracking": 0.04}),),
                "SKI PATROL / FIRST AID in Franklin Gothic Demi Cond, white on patrol red (nitpick C, was Bfont)"),
    "school.markstaff": ((("type", {"lines": ("STAFF", "ONLY"), "face": NC_UTILITY_FACE, "fg": WHITE,
                                    "bg": SCHOOL_RED, "cap_frac": 0.66, "pitch": 1.35, "tracking": 0.04}),),
                         "red placard on the white doors, Franklin Gothic Demi Cond (nitpick C; legend a stand-in)"),
    "school.marknosled": ((("type", {"lines": ("NO", "SLEDDING"), "face": NC_UTILITY_FACE, "fg": WHITE,
                                     "bg": SCHOOL_RED, "cap_frac": 0.62, "pitch": 1.30, "tracking": 0.04}),
                           ("frame", {"width": 0.018, "inset": 0.014, "fg": WHITE})),
                          "red NO SLEDDING sign with a white rim, Franklin Gothic Demi Cond (nitpick C; Street View "
                          "120856)"),
}
SHEET = tuple(replace(k, draws=_NC_PAINT[k.key][0], note=_NC_PAINT[k.key][1]) if k.key in _NC_PAINT else k
              for k in SHEET)
#: SS4: sha1 of the frozen kinds (key, size, rect, lit) in table order; extend the table, then
#: append the new hash here (the old prefix must still hash to every earlier entry)
LAYOUT_SHA = {len(SHEET): None}          # filled below (module constant computed once, pinned in the test)
DRAW_KINDS = ("text", "bibs", "menu", "shapes", "glyph", "notices", "pictogram", "cross_square", "logs",
              "frame",
              "bars", "flake",      # fine-detail step 3d (P1 info board, welcome-sign snowflake)
              "type", "blank")      # nitpick C (2026-10-13): installed typefaces, blank boards


def kind(key):
    for k in SHEET:
        if k.key == key:
            return k
    raise KeyError(f"sheet kind {key!r} not in cs2_signset.SHEET")


def layout_hash(kinds=SHEET, n=None):
    """SS4: sha1 (16 hex) of the first n kinds' (key, size, rect, lit) - the frozen prefix."""
    h = hashlib.sha1()
    for k in tuple(kinds)[: (len(kinds) if n is None else n)]:
        h.update(json.dumps([k.key, [round(float(v), 4) for v in k.size], list(k.rect), bool(k.lit)])
                 .encode())
    return h.hexdigest()[:16]


LAYOUT_SHA[len(SHEET)] = layout_hash()


def check_layout(kinds=SHEET, A=SHEET_A, gutter_min=6, ppm=SHEET_PPM, ppm_tol=0.02):
    """SS1: every rect inside the sheet with >= gutter_min px to the edge, pairwise gaps >=
    gutter_min (no overlap), unique keys, rect size = size x ppm (+-ppm_tol relative). Returns
    a list of problems ([] = OK)."""
    bad = []
    keys = [k.key for k in kinds]
    if len(set(keys)) != len(keys):
        bad.append("duplicate keys")
    for k in kinds:
        x0, y0, x1, y1 = k.rect
        if not (gutter_min <= x0 < x1 <= A - gutter_min and gutter_min <= y0 < y1 <= A - gutter_min):
            bad.append(f"{k.key}: rect {k.rect} outside the sheet / gutter")
        pu, pv = (x1 - x0) / k.size[0], (y1 - y0) / k.size[1]
        if abs(pu / ppm - 1.0) > ppm_tol or abs(pv / ppm - 1.0) > ppm_tol:
            bad.append(f"{k.key}: {pu:.1f} / {pv:.1f} px/m, not {ppm}")
        if k.state not in ("painted", "reserved"):
            bad.append(f"{k.key}: state {k.state}")
        if k.state == "painted" and not k.draws:
            bad.append(f"{k.key}: painted without draws")
        for d, _kw in k.draws:
            if d not in DRAW_KINDS:
                bad.append(f"{k.key}: draw {d!r}")
    ks = list(kinds)
    for i, a in enumerate(ks):
        for b in ks[i + 1:]:
            ax0, ay0, ax1, ay1 = a.rect
            bx0, by0, bx1, by1 = b.rect
            gx = max(bx0 - ax1, ax0 - bx1)
            gy = max(by0 - ay1, ay0 - by1)
            if max(gx, gy) < gutter_min:
                bad.append(f"{a.key} / {b.key}: gap {max(gx, gy)} px < {gutter_min}")
    return bad


def shelf_pack(items, A=SHEET_A, gutter=GUTTER, ppm=SHEET_PPM):
    """The packer that produced SHEET (kept for the record and for a next owner letter):
    items = ((key, w_m, h_m), ...) placed in descending height, first-fit shelves from the
    bottom. Returns {key: rect}."""
    order = sorted(items, key=lambda t: (-round(t[2] * ppm), -round(t[1] * ppm)))
    shelves, out, ytop = [], {}, gutter
    for key, w, h in order:
        W, H = int(round(w * ppm)), int(round(h * ppm))
        for s in shelves:
            if H <= s[1] and s[2] + W + gutter <= A:
                out[key] = (s[2], s[0], s[2] + W, s[0] + H)
                s[2] += W + gutter
                break
        else:
            if ytop + H + gutter > A:
                raise ValueError(f"shelf_pack: {key} does not fit")
            shelves.append([ytop, H, gutter + W + gutter])
            out[key] = (gutter, ytop, gutter + W, ytop + H)
            ytop += H + gutter
    return out


def free_rows(kinds=SHEET, A=SHEET_A):
    return A - max(k.rect[3] for k in kinds)


# ======================================================================================
# Painting (numpy; vector text needs Blender's Bfont -> bpy at call time)
# ======================================================================================

def _K():
    import cs2_bldg
    return cs2_bldg


def _LD():
    import cs2_liftdress
    return cs2_liftdress


def _canvas(base_l, rect, size, last):
    """A cs2_bldg.DisplayCanvas over the texel rect of the LINEAR (A, A, 4) array (rows =
    UV v, bottom-up): x right / y down from the face's top-left, in face meters."""
    K = _K()
    x0, y0, x1, y1 = rect
    w, h = x1 - x0, y1 - y0
    W, H = (float(v) for v in size)
    off = (np.arange(SS, dtype=np.float64) + 0.5) / SS
    jj = np.arange(w, dtype=np.float64)[None, :, None, None] + off[None, None, None, :]
    ii = np.arange(h, dtype=np.float64)[:, None, None, None] + off[None, None, :, None]
    jj, ii = np.broadcast_arrays(jj, ii)
    x = (jj * (W / w)).reshape(h, w, SS * SS)
    y = ((h - ii) * (H / h)).reshape(h, w, SS * SS)
    ppm = 0.5 * (w / W + h / H)
    cv = K.DisplayCanvas(base_l[y0:y1, x0:x1, :], x, y, W, H, ppm, last)
    cv.w, cv.h = w, h
    cv.emit = np.zeros((h, w), np.float32)
    return cv


def _tier(cap, stroke_px):
    if cap >= TIER_T1[0] - 1e-9 and stroke_px >= TIER_T1[1] - 1e-9:
        return "T1"
    return "T2" if stroke_px >= 2.0 - 1e-9 else "none"


def word_cov(body, cap, cx, cy, W, H, w, h):
    """(h, w) coverage (rows bottom-up) of one word at cap height `cap` centered on (cx, cy) face
    meters (y up from the bottom edge). Bfont's "1" is a bare stem that reads as I / l, so a lone
    "1" is the lift boards' flagged one (cs2_liftdress.ONE_PRIMS, the seat-plate digit)."""
    LD = _LD()
    if body == "1":
        S = cap * 256.0 / 176.0
        return LD._sdf_cov(LD.ONE_PRIMS, cx - S / 2.0 - cap * 0.07, cy - S / 2.0, S, w, h, w / W, h / H)
    return LD._text_cov(body, cap, cx, cy, 1.0, W, H, w, h)


def draw_text_vec(lines, fg=WHITE, bg=None, fill=TEXT_FILL, cap_frac=TEXT_CAP_FRAC, pitch=1.45):
    """Words in the VECTOR font (Bfont, cs2_liftdress._text_cov, 8 x 8 supersampled): each line
    centered, the block centered; cap = cap_frac x the face height / the line stack, shrunk so the
    widest line is <= fill x the face width. Records cap / stroke (Bfont stem ratio x cap) /
    tier; the coverage also feeds the Emissive of a lit kind."""
    lines = (lines,) if isinstance(lines, str) else tuple(lines)

    def draw(cv):
        LD = _LD()
        if bg is not None:
            cv.fill(bg)
        L = len(lines)
        cap = cap_frac * cv.H / (1.0 + (L - 1) * pitch)
        widest = max(LD.text_width(s, cap) for s in lines)
        if widest > fill * cv.W:
            cap *= fill * cv.W / widest
        cov = np.zeros((cv.h, cv.w), np.float32)
        for i, s in enumerate(lines):
            cy = 0.5 * cv.H + ((L - 1) / 2.0 - i) * pitch * cap
            cov = np.maximum(cov, word_cov(s, cap, 0.5 * cv.W, cy, cv.W, cv.H, cv.w, cv.h))
        cv.paint(cov, fg)
        cv.emit = np.maximum(cv.emit, cov)
        stroke = LD.stem_ratio() * cap
        cv.record("text", {"lines": list(lines), "font": "Bfont vector", "cap_m": round(cap, 4),
                           "stroke_m": round(stroke, 5), "stroke_px": round(stroke * cv.ppm, 3),
                           "tier": _tier(cap, stroke * cv.ppm),
                           "width_m": round(max(LD.text_width(s, cap) for s in lines), 4)})
    return draw


def draw_bibs_vec(n=8, seed=0, colours=None, num_fg=WHITE, bg=BOARD_BROWN):
    """cs2_bldg.draw_bibs (the school bib board) with VECTOR numbers: same vests, same seeded
    color order, same cap (min(0.55 x bib width, 0.30 x bib height))."""
    def draw(cv):
        K, LD = _K(), _LD()
        cols = colours or K.BIB_SRGB
        if bg is not None:
            cv.fill(bg)
        slot = cv.W / n
        bw = 0.88 * slot
        bh = min(0.70 * cv.H, 2.6 * bw)
        top = 0.5 * (cv.H - bh)
        order = np.random.default_rng(int(seed) & 0xFFFFFFFF).permutation(n)
        cap = min(0.55 * bw, 0.30 * bh)
        num = np.zeros((cv.h, cv.w), np.float32)
        for i in range(n):
            xc = (i + 0.5) * slot
            X, Y = cv.x - xc, cv.y - top
            vest = (np.abs(X) <= bw / 2) & (Y >= 0) & (Y <= bh)
            neck = X * X + Y * Y <= (0.22 * bw) ** 2
            cv.paint(cv.cover(vest & ~neck), cols[int(order[i]) % len(cols)])
            num = np.maximum(num, word_cov(str(i + 1), cap, xc, cv.H - (top + 0.45 * bh),
                                           cv.W, cv.H, cv.w, cv.h))
        cv.paint(num, num_fg)
        stroke = LD.stem_ratio() * cap
        cv.record("bibs", {"n": n, "font": "Bfont vector", "cap_m": round(cap, 4),
                           "stroke_m": round(stroke, 5), "stroke_px": round(stroke * cv.ppm, 3)})
    return draw


def draw_menu(bg=CHALK, ink=None):
    """The L2 chalkboard menu (cs2_bldg.paint_glyph kind "menu" layout: title bar + six item /
    price rows) on the canvas."""
    def draw(cv):
        K = _K()
        if bg is not None:
            cv.fill(bg)
        X, Y = cv.x / cv.W, cv.y / cv.H
        cv.paint(cv.cover(K._menu_sdf(X, Y) < 0.0), ink or K.MENU_INK_SRGB)
        cv.record("menu", {"rows": 6})
    return draw


def draw_frame(width=0.02, inset=0.0, fg=WHITE):
    """A rectangular rim `width` m wide, `inset` m in from the face edge (school revision 2: the
    white rim of the NO SLEDDING sign). Vector (supersampled coverage), no text."""
    def draw(cv):
        a, b = float(inset), float(inset) + float(width)
        X, Y = cv.x, cv.y
        outer = (X >= a) & (X <= cv.W - a) & (Y >= a) & (Y <= cv.H - a)
        inner = (X > b) & (X < cv.W - b) & (Y > b) & (Y < cv.H - b)
        cv.paint(cv.cover(outer & ~inner), fg)
        cv.record("frame", {"width_m": round(float(width), 4), "inset_m": round(float(inset), 4),
                            "width_px": round(float(width) * cv.ppm, 2)})
    return draw


def draw_bars(bars_lin=INFO_BARS_LIN, white=0.90):
    """Fine-detail step 3d: the P1 info board (recipes/_civic.BarsPainter's design, contract 3.5): a white
    field and horizontal color bars (center height 0..1 from the bottom, length share of 0.72 W from 0.14 W,
    half height 0.065 H), LINEAR rgb converted to the canvas' sRGB. No text."""
    def draw(cv):
        cv.fill(white)
        X, Y = cv.x / cv.W, cv.y / cv.H                 # x right, y DOWN (0..1)
        for cy, ln, rgb in bars_lin:
            m = (np.abs((1.0 - Y) - cy) < 0.065) & (X > 0.14) & (X < 0.14 + 0.72 * ln)
            cv.paint(cv.cover(m), tuple(float(v) for v in _lin_to_srgb(rgb)))
        cv.record("bars", {"n": len(bars_lin), "font": None})
    return draw


def draw_flake(d=0.36, fg=(0.93, 0.89, 0.78), bg=None):
    """Fine-detail step 3d: the welcome sign's snowflake ornament (welcome_sign.paint_band: ski_launcher_glyph
    snowflake(128, 128, 112, 24, bw=18) rendered 8 x 8 supersampled) at diameter `d` m, centered on the face."""
    def draw(cv):
        import ski_launcher_glyph as G
        if bg is not None:
            cv.fill(bg)
        s = int(round(d * cv.ppm))
        flake = G.render_alpha(G.snowflake(128.0, 128.0, 112.0, 24.0, bw=18.0), s, ss=8)[::-1]
        cov = np.zeros((cv.h, cv.w), np.float32)
        r0, c0 = int(round(cv.h / 2.0 - s / 2.0)), int(round(cv.w / 2.0 - s / 2.0))
        if r0 < 0 or c0 < 0 or r0 + s > cv.h or c0 + s > cv.w:
            raise ValueError(f"draw_flake: {s} px flake does not fit {cv.w} x {cv.h}")
        cov[r0:r0 + s, c0:c0 + s] = flake
        cv.paint(cov, fg)
        cv.record("flake", {"d_m": float(d), "px": s, "font": None})
    return draw


# ======================================================================================
# Nitpick C (2026-10-13, build/nitpick-c-lodges.md): REAL SIGNAGE TYPOGRAPHY. Richard, twenty-fifth
# look, L5: "use a DIFFERENT FONT. Hard to explain but it just looks too AI"; ski school: "Same comment
# as the swanky lodge on the fonts". The sheet's lettered boards are drawn in INSTALLED Windows
# typefaces through the SAME vector path as Bfont (a Blender FONT curve with the TTF loaded ->
# triangulated outline -> 8 x 8 supersampled coverage, welcome_sign._tri_coverage): only the rendered
# texels ship. The TTF files are NEVER copied into the repo or shipped (Windows font licenses allow
# rendering into our bitmaps, not redistribution of the file); TYPEFACES pins each file's sha1 so a
# changed / missing system font stops the paint loudly instead of silently repainting the sheet.
# ======================================================================================

FONT_DIR = os.path.join(os.environ.get("SystemRoot") or os.environ.get("WINDIR") or r"C:\Windows", "Fonts")
TTF_RES = 16                # FONT curve resolution_u (bezier steps per segment; Bfont path keeps the default)
TTF_SS = 8                  # samples per texel axis (= cs2_liftdress.TEXT_SS, the Bfont words)


@dataclass(frozen=True)
class Typeface:
    key: str                # TYPEFACES key
    name: str               # the family + style as Blender reports it (VectorFont.name)
    file: str               # file name under FONT_DIR (never copied)
    sha1: str               # first 16 hex of the file's sha1 (pinned: a changed font stops the paint)
    weight: float = 0.0     # FONT curve outline offset (font units at size 1): 0 = the face as drawn
    role: str = ""          # "lodge" | "utility" (what it was chosen for; build report font table)


#: candidates rendered for the font table (assets/preview/boards-2026-10-13/fonts/); the CHOSEN ones are named
#: by FACE_FOR below. sha1 = the file on Richard's machine 2026-10-13 (C:\Windows\Fonts).
TYPEFACES = {t.key: t for t in (
    Typeface("bookman_bold", "Bookman Old Style Bold", "BOOKOSB.TTF", "275aab405883ea72", role="lodge"),
    Typeface("georgia_bold", "Georgia Bold", "georgiab.ttf", "e27ea4537381876f", role="lodge"),
    Typeface("copperplate_bold", "Copperplate Gothic Bold", "COPRGTB.TTF", "1a4a190d2e949d5e", role="lodge"),
    Typeface("rockwell_bold", "Rockwell Bold", "ROCKB.TTF", "db22c0265eb8d5d4", role="lodge"),
    Typeface("schoolbook_bold", "Century Schoolbook Bold", "SCHLBKB.TTF", "7d12b3c112239109", role="lodge"),
    Typeface("goudy_bold", "Goudy Old Style Bold", "GOUDOSB.TTF", "fff8b94c51eb2dcf", role="lodge"),
    Typeface("arial_narrow_bold", "Arial Narrow Bold", "ARIALNB.TTF", "2bb165b1c58956eb", role="utility"),
    Typeface("bahnschrift", "Bahnschrift (default instance)", "bahnschrift.ttf", "34d4becf017fa35d",
             role="utility"),
    Typeface("bahnschrift_semibold", "Bahnschrift (default instance, outline +0.012 ~ SemiBold)",
             "bahnschrift.ttf", "34d4becf017fa35d", weight=0.012, role="utility"),
    Typeface("franklin_demi_cond", "Franklin Gothic Demi Cond", "FRADMCN.TTF", "8f6c5b31ee254ce5", role="utility"),
    Typeface("franklin_med_cond", "Franklin Gothic Medium Cond", "FRAMDCN.TTF", "b7ca37f01d8c8028", role="utility"),
    Typeface("twcen_cond_bold", "Tw Cen MT Condensed Bold", "TCCB____.TTF", "c8f19c15d2d6e750", role="utility"),
)}

_TTF = {}
_TTF_REF = {}
_TTF_CHECKED = {}


def typeface(key):
    if key not in TYPEFACES:
        raise KeyError(f"cs2_signset: typeface {key!r} not in TYPEFACES {sorted(TYPEFACES)}")
    return TYPEFACES[key]


def typeface_path(tf, font_dir=None):
    """The TTF's path under FONT_DIR after the pinned-sha1 check (FileNotFoundError / RuntimeError: the
    sheet must not repaint silently on another machine or after a font update)."""
    p = os.path.join(font_dir or FONT_DIR, tf.file)
    got = _TTF_CHECKED.get(p)                  # the file's hash, read once per process
    if got is None:
        if not os.path.isfile(p):
            raise FileNotFoundError(f"cs2_signset: typeface {tf.name!r} needs {p} (an installed Windows font; it is "
                                    f"rendered into the sheet, never shipped). Install it or repaint with another "
                                    f"face.")
        with open(p, "rb") as fh:
            got = hashlib.sha1(fh.read()).hexdigest()[:16]
    if got != tf.sha1:
        raise RuntimeError(f"cs2_signset: {p} sha1 {got} != pinned {tf.sha1} ({tf.name}): the system font "
                           f"changed, so the sheet would repaint differently; re-render the font table and re-pin")
    _TTF_CHECKED[p] = got
    return p


def _ttf_font_tris(body, tf, track=1.0):
    """Triangles (font units, size 1, align LEFT) of `body` in the typeface `tf` (a temporary FONT curve with
    the TTF loaded; removed again). track = FONT curve space_character (1 = the font's own spacing; each step
    of +0.1 adds 0.05 font units per character advance, measured 2026-10-13 on Blender 5.2.2)."""
    import bmesh
    import bpy
    key = (body, tf.key, round(float(track), 6))
    if key in _TTF:
        return _TTF[key]
    vf = bpy.data.fonts.load(typeface_path(tf), check_existing=True)
    cu = bpy.data.curves.new("SignSetTTF", type="FONT")
    cu.font = vf
    cu.body = body
    cu.size = 1.0
    cu.align_x = "LEFT"
    cu.space_character = float(track)
    cu.offset = float(tf.weight)
    cu.resolution_u = TTF_RES
    ob = bpy.data.objects.new("SignSetTTF", cu)
    bpy.context.scene.collection.objects.link(ob)
    try:
        dg = bpy.context.evaluated_depsgraph_get()
        me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
        bm = bmesh.new()
        bm.from_mesh(me)
        bmesh.ops.triangulate(bm, faces=bm.faces[:])
        tris = [tuple((float(v.co.x), float(v.co.y)) for v in f.verts) for f in bm.faces]
        bm.free()
        bpy.data.meshes.remove(me)
    finally:
        bpy.data.objects.remove(ob, do_unlink=True)
        bpy.data.curves.remove(cu)
    if not tris:
        raise RuntimeError(f"cs2_signset: {tf.name} drew no triangles for {body!r}")
    _TTF[key] = tris
    return tris


def _xsect(tris, axis, at):
    """Total length of the union of the triangles' cuts by the line {coord[axis] == at} (axis 1: a horizontal
    line, lengths along x; axis 0: a vertical line, lengths along y) and the intervals themselves."""
    o = 1 - axis
    iv = []
    for t in tris:
        pts = []
        for i in range(3):
            a, b = t[i], t[(i + 1) % 3]
            da, db = a[axis] - at, b[axis] - at
            if da == 0.0:
                pts.append(a[o])
            if da * db < 0.0:
                pts.append(a[o] + (b[o] - a[o]) * da / (da - db))
        if len(pts) >= 2:
            iv.append((min(pts), max(pts)))
    iv.sort()
    merged = []
    for lo, hi in iv:
        if merged and lo <= merged[-1][1] + 1e-9:
            merged[-1][1] = max(merged[-1][1], hi)
        else:
            merged.append([lo, hi])
    return sum(hi - lo for lo, hi in merged), merged


def ttf_ref(tf):
    """(cap bottom, cap top, stem, thin) in font units: the cap box of 'H' (every line is scaled and centered
    on it, so a Q tail or a comma never moves the baseline); stem = the 'I' cut at mid cap height (the
    vertical stroke, serifs excluded); thin = the thinnest of the 'H' crossbar and the 'O' bowl at its top
    and bottom (the horizontal hairlines of a serif face) - SS3 checks the THINNER of stem and thin."""
    if tf.key in _TTF_REF:
        return _TTF_REF[tf.key]
    th = _ttf_font_tris("H", tf)
    ph = np.array([p for t in th for p in t])
    lo, hi = float(ph[:, 1].min()), float(ph[:, 1].max())
    ti = _ttf_font_tris("I", tf)
    stem, _iv = _xsect(ti, 1, 0.5 * (lo + hi))
    _l, ivh = _xsect(th, 0, 0.5 * float(ph[:, 0].min() + ph[:, 0].max()))
    bar = min((b - a for a, b in ivh), default=0.0)
    to = _ttf_font_tris("O", tf)
    po = np.array([p for t in to for p in t])
    _l, ivo = _xsect(to, 0, 0.5 * float(po[:, 0].min() + po[:, 0].max()))
    bowl = min((b - a for a, b in ivo), default=bar)
    _TTF_REF[tf.key] = (lo, hi, float(stem), float(min(bar, bowl)))
    return _TTF_REF[tf.key]


def ttf_line(body, cap, tf, track=1.0):
    """One line in `tf` at cap height `cap` (m): (triangles in meters centered on the ink's width and on the
    cap box, width_m, stem_m, thin_m)."""
    lo, hi, stem, thin = ttf_ref(tf)
    s = float(cap) / (hi - lo)
    tris = _ttf_font_tris(body, tf, track)
    pts = np.array([p for t in tris for p in t], np.float64)
    xc, yc = 0.5 * (pts[:, 0].min() + pts[:, 0].max()), 0.5 * (lo + hi)
    out = [tuple(((p[0] - xc) * s, (p[1] - yc) * s) for p in t) for t in tris]
    return out, float((pts[:, 0].max() - pts[:, 0].min()) * s), stem * s, thin * s


def track_for(tf, tracking_cap):
    """FONT curve space_character that adds `tracking_cap` x the cap height between letters (0 = as drawn)."""
    lo, hi, _s, _t = ttf_ref(tf)
    return 1.0 + 2.0 * float(tracking_cap) * (hi - lo)


def ttf_cov(tris, cx, cy, W, H, w, h):
    """(h, w) coverage (rows bottom-up) of meter triangles centered on (cx, cy) face meters (y up)."""
    import welcome_sign
    ox, oy = cx - W / 2.0, cy - H / 2.0
    tt = [tuple((x + ox, y + oy) for x, y in t) for t in tris]
    return welcome_sign._tri_coverage(tt, w, h, w / W, h / H, ss=TTF_SS)


def draw_type(lines, face, fg=WHITE, bg=None, fill=TEXT_FILL, cap_frac=TEXT_CAP_FRAC, pitch=1.45,
              tracking=0.0, flank=None, flank_frac=0.42, flank_gap=0.55, cap_max=None):
    """Nitpick C: words in an installed TYPEFACE (TYPEFACES[face]) through the vector path. Layout as
    draw_text_vec (each line centered, the block centered; cap = cap_frac x the face height / the line stack,
    shrunk so the widest line + its ornaments is <= fill x the face width; cap_max caps it) plus letter
    spacing `tracking` (x the cap) and `flank` = "diamond": a small lozenge (flank_frac x the cap) each side
    of a single line, flank_gap x the cap from the ink - the lodge-sign ornament. Records the face, file,
    cap, stem / thin strokes and the tier; lit kinds glow with the letters AND the ornaments."""
    lines = (lines,) if isinstance(lines, str) else tuple(lines)
    tf = typeface(face)

    def draw(cv):
        if bg is not None:
            cv.fill(bg)
        tr = track_for(tf, tracking)
        L = len(lines)
        cap = cap_frac * cv.H / (1.0 + (L - 1) * pitch)
        if cap_max is not None:
            cap = min(cap, float(cap_max))
        orn = (2.0 * (flank_gap + flank_frac)) if (flank and L == 1) else 0.0

        def need(c):
            return max(ttf_line(s, c, tf, tr)[1] for s in lines) + orn * c

        for _ in range(3):                      # widths scale with the cap: converge in 1-2 steps
            wneed = need(cap)
            if wneed <= fill * cv.W * (1.0 + 1e-6):
                break
            cap *= fill * cv.W / wneed
        cov = np.zeros((cv.h, cv.w), np.float32)
        widths = []
        stem = thin = None
        for i, s in enumerate(lines):
            tris, wl, st, tn = ttf_line(s, cap, tf, tr)
            widths.append(wl)
            stem, thin = st, tn
            cy = 0.5 * cv.H + ((L - 1) / 2.0 - i) * pitch * cap
            cov = np.maximum(cov, ttf_cov(tris, 0.5 * cv.W, cy, cv.W, cv.H, cv.w, cv.h))
        orn_rec = None
        if flank and L == 1:
            if flank != "diamond":
                raise ValueError(f"draw_type: flank {flank!r}")
            r = 0.5 * flank_frac * cap
            cyd = 0.5 * cv.H                           # one line: centered (canvas y is DOWN from the top)
            xo = 0.5 * widths[0] + flank_gap * cap + r
            m = np.zeros(cv.x.shape, bool)
            for sx in (-1.0, 1.0):
                m |= (np.abs(cv.x - (0.5 * cv.W + sx * xo)) + np.abs(cv.y - cyd)) <= r
            cov = np.maximum(cov, cv.cover(m).astype(np.float32))
            orn_rec = {"kind": "diamond", "d_m": round(2 * r, 4), "gap_m": round(flank_gap * cap, 4)}
        cv.paint(cov, fg)
        cv.emit = np.maximum(cv.emit, cov)
        st_px, tn_px = stem * cv.ppm, thin * cv.ppm
        cv.record("text", {"lines": list(lines), "font": tf.name, "font_file": tf.file, "font_sha1": tf.sha1,
                           "font_weight_offset": tf.weight, "tracking_cap": float(tracking),
                           "cap_m": round(cap, 4), "stem_px": round(st_px, 3), "thin_px": round(tn_px, 3),
                           "stroke_m": round(min(stem, thin), 5), "stroke_px": round(min(st_px, tn_px), 3),
                           "tier": _tier(cap, min(st_px, tn_px)), "width_m": round(max(widths), 4),
                           "ornament": orn_rec})
    return draw


def draw_blank(bg=BOARD_BROWN, rim=None, rim_m=0.02, inset_m=0.05):
    """Nitpick C: a BLANK board (fine_detail.blank_board on the sheet): the plain panel + an optional thin
    rim, no pictogram - a label surface people can paste their own text on."""
    return FD.blank_board(bg=bg, rim=rim, rim_m=rim_m, inset_m=inset_m)


def _draw_fn(kind_name, kw):
    K = _K()
    if kind_name == "text":
        return draw_text_vec(**kw)
    if kind_name == "type":
        return draw_type(**kw)
    if kind_name == "blank":
        return draw_blank(**kw)
    if kind_name == "bibs":
        return draw_bibs_vec(**kw)
    if kind_name == "menu":
        return draw_menu(**kw)
    if kind_name == "frame":
        return draw_frame(**kw)
    if kind_name == "bars":
        return draw_bars(**kw)
    if kind_name == "flake":
        return draw_flake(**kw)
    fn = {"shapes": K.draw_shapes, "glyph": K.draw_glyph, "notices": K.draw_notices,
          "pictogram": K.draw_pictogram, "cross_square": K.draw_cross_square,
          "logs": K.draw_logs}.get(kind_name)
    if fn is None:
        raise KeyError(f"sheet draw {kind_name!r}")
    return fn(**kw)


def _srgb_to_lin(c):
    c = np.asarray(c, np.float32)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4).astype(np.float32)


def _lin_to_srgb(c):
    c = np.clip(np.asarray(c, np.float32), 0.0, 1.0)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(c, 1.0 / 2.4) - 0.055).astype(np.float32)


def _dilate(arr, rect, d=DILATE):
    """Extend a rect's border texels d px outward (edge mode), clamped to the sheet."""
    x0, y0, x1, y1 = rect
    A = arr.shape[0]
    sub = arr[y0:y1, x0:x1].copy()
    pad = np.pad(sub, ((d, d), (d, d), (0, 0)), mode="edge")
    ya, xa = max(0, y0 - d), max(0, x0 - d)
    yb, xb = min(A, y1 + d), min(A, x1 + d)
    arr[ya:yb, xa:xb] = pad[ya - (y0 - d):yb - (y0 - d), xa - (x0 - d):xb - (x0 - d)]


def paint_sheet(kinds=SHEET, A=SHEET_A):
    """{map: (array, is_data)} of the whole sheet + {kind key: metrics}. BaseColor painted
    LINEAR then encoded sRGB (the importer reads the PNG as color), Emissive = lit letters x
    EMISSIVE_SRGB (encoded), ControlMask 0, MaskMap (0, 0, 0, SMOOTH), Normal flat. Reserved
    rects stay the background (blank)."""
    base_l = np.zeros((A, A, 4), np.float32)
    base_l[..., 0:3] = _srgb_to_lin(SHEET_BG_SRGB)
    base_l[..., 3] = 1.0
    emi = np.zeros((A, A, 4), np.float32)
    emi[..., 3] = 1.0
    metrics = {}
    for k in kinds:
        if k.state != "painted":
            metrics[k.key] = {"state": k.state, "rect": list(k.rect), "size_m": list(k.size),
                              "lit": k.lit}
            continue
        last = {"key": k.key, "rect": list(k.rect), "size_m": list(k.size), "lit": k.lit}
        cv = _canvas(base_l, k.rect, k.size, last)
        last["ppm"] = round(cv.ppm, 3)
        last["ppm_uv"] = [round(cv.w / k.size[0], 3), round(cv.h / k.size[1], 3)]
        for d, kw in k.draws:
            _draw_fn(d, dict(kw))(cv)
        if k.lit:
            x0, y0, x1, y1 = k.rect
            for c in range(3):
                emi[y0:y1, x0:x1, c] = EMISSIVE_SRGB[c] * cv.emit
            last["lit_texels"] = int(np.count_nonzero(cv.emit > 0.5))
        metrics[k.key] = last
        _dilate(base_l, k.rect)
    base = np.empty_like(base_l)
    base[..., 0:3] = _lin_to_srgb(base_l[..., 0:3])
    base[..., 3] = 1.0
    nrm = np.zeros((A, A, 4), np.float32)
    nrm[..., 0:2] = 0.5
    nrm[..., 2:4] = 1.0
    msk = np.zeros((A, A, 4), np.float32)
    msk[..., 3] = SMOOTH
    ctl = np.zeros((A, A, 4), np.float32)
    return {"BaseColor": (base, False), "ControlMask": (ctl, True), "MaskMap": (msk, True),
            "Normal": (nrm, True), "Emissive": (emi, False)}, metrics


def write_sheet(maps, asset_dir, name, A=SHEET_A):
    import cs2_asset
    out = []
    for m in MAPS:
        arr, is_data = maps[m]
        p = os.path.join(asset_dir, f"{name}_{m}.png")
        cs2_asset._write_png_raw(p, f"{name}_{m}", arr, A, is_data)
        out.append(p)
    return out


# ======================================================================================
# The host side: route the label faces out, the recipe without them
# ======================================================================================

def route(parts, spec):
    """(host parts, routed face parts) - every face of spec.faces must exist exactly once."""
    want = spec.face_names()
    names = [o.name for o, _k in parts]
    missing = [f for f in want if names.count(f) != 1]
    if missing:
        raise RuntimeError(f"sign set {spec.prop}: faces {missing} not found exactly once in the host")
    routed = [(o, k) for o, k in parts if o.name in want]
    host = [(o, k) for o, k in parts if o.name not in want]
    return host, [routed[[o.name for o, _k in routed].index(f)] for f in want]


def host_recipe(recipe, spec):
    """The host recipe without the routed faces' `labels` entries (their label quads are gone)."""
    want = set(spec.face_names())
    return replace(recipe, labels=tuple(e for e in recipe.labels if e[0] not in want))


def _face_frame(obj):
    """(corners world (4, 3), n, right, up, s, t) of a single-quad label part."""
    import mathutils
    me = obj.data
    if len(me.polygons) != 1 or me.polygons[0].loop_total != 4:
        raise RuntimeError(f"sign set: {obj.name} is not one quad")
    mw = obj.matrix_world
    poly = me.polygons[0]
    P = np.array([list(mw @ me.vertices[me.loops[li].vertex_index].co) for li in poly.loop_indices],
                 np.float64)
    n = (mw.to_3x3() @ poly.normal).normalized()
    up = mathutils.Vector((0.0, 0.0, 1.0))
    up = (up - n * up.dot(n))
    if up.length < 0.5:
        raise RuntimeError(f"sign set: {obj.name} is horizontal (no viewer up)")
    up.normalize()
    right = (-n).cross(up)
    R, U = np.array(list(right)), np.array(list(up))
    C = P.mean(axis=0)
    return P, np.array(list(n)), R, U, (P - C) @ R, (P - C) @ U


def build_sign_set(faces, spec, yaw_deg=0.0, kinds=SHEET, A=SHEET_A, size_tol=0.002):
    """The sign-set object (meters, Z up, the host's NET-ORIENTED frame, not linked to the
    scene): one quad per routed face at its own world position, UV layer "UVMap" = its sheet
    rect upright as seen from outside; face attribute "part" + the part table (face names).
    Raises when a face's size differs from its sheet kind by more than size_tol."""
    import bpy
    import mathutils
    import cs2_asset
    by_face = dict(spec.faces)
    verts, polys, uvs, part, table, info = [], [], [], [], [], {}
    for o, _k in faces:
        k = kind(by_face[o.name])
        if k.state != "painted":
            raise RuntimeError(f"sign set {spec.prop}: {o.name} -> {k.key} is reserved (not painted)")
        P, n, R, U, s, t = _face_frame(o)
        s0, s1, t0, t1 = s.min(), s.max(), t.min(), t.max()
        dw, dh = (s1 - s0) - k.size[0], (t1 - t0) - k.size[1]
        if abs(dw) > size_tol or abs(dh) > size_tol:
            raise RuntimeError(f"sign set {spec.prop}: {o.name} is {s1 - s0:.4f} x {t1 - t0:.4f} m, "
                               f"sheet kind {k.key} {k.size}")
        x0, y0, x1, y1 = k.rect
        base = len(verts)
        for i in range(4):
            verts.append(tuple(P[i]))
            uvs.append(((x0 + (s[i] - s0) / (s1 - s0) * (x1 - x0)) / A,
                        (y0 + (t[i] - t0) / (t1 - t0) * (y1 - y0)) / A))
        polys.append((base, base + 1, base + 2, base + 3))
        table.append(o.name)
        part.append(len(table))
        info[o.name] = {"kind": k.key, "rect": list(k.rect), "size_m": [round(s1 - s0, 4),
                                                                          round(t1 - t0, 4)],
                        "normal_authoring": [round(float(v), 4) for v in n]}
    me = bpy.data.meshes.new(spec.mesh)
    me.from_pydata(verts, [], polys)
    me.update()                                # loop order = the source quads': same normals
    lay = me.uv_layers.new(name="UVMap")
    for p_ in me.polygons:
        for li in p_.loop_indices:
            lay.data[li].uv = uvs[me.loops[li].vertex_index]
    attr = me.attributes.new("part", "INT", "FACE")
    attr.data.foreach_set("value", part)
    me[cs2_asset.PART_TABLE_PROP] = json.dumps(table)
    # the host's chain after the join: apply_yaw (about Z), cs2_asset.orient_for_net (-90 about Z)
    M = mathutils.Matrix.Rotation(math.radians(-90.0), 4, "Z") @ \
        mathutils.Matrix.Rotation(math.radians(float(yaw_deg)), 4, "Z")
    me.transform(M)
    me.update()
    ob = bpy.data.objects.new(spec.mesh, me)
    ob["cs2_signset"] = json.dumps(info, sort_keys=True)
    return ob


def face_record(obj, yaw_deg=0.0):
    """Nitpick C: a single-quad label face as a Write Everywhere target (another fleet lays the WE layout out):
    center, outward normal, right / up (as seen from outside: right = -n x up, the DisplayPainter frame) in the
    prefab-local GAME frame through the SAME chain as build_sign_set (the host's yaw, orient_for_net's -90 deg
    about Z, export_fbx (x, y, z) -> (x, z, -y), then the game's x negation): game x = -authoring y (fine_detail.
    X_CONVENTION; gx NEGATED, x-sign gate settled N 2026-10-15; the x of every vector is negated too), and the
    face size (m)."""
    P, n, R, U, s, t = _face_frame(obj)
    a, b = math.radians(-90.0), math.radians(float(yaw_deg))
    Rz = lambda th: np.array([[math.cos(th), -math.sin(th), 0.0], [math.sin(th), math.cos(th), 0.0],   # noqa: E731
                              [0.0, 0.0, 1.0]])
    M = Rz(a) @ Rz(b)

    def g(v):
        w = M @ np.asarray(v, np.float64)
        return [round(float(-w[0]) + 0.0, 4), round(float(w[2]), 4), round(float(-w[1]), 4)]

    C = P.mean(axis=0)
    return {"centre": g(C), "normal": g(n), "right": g(R), "up": g(U),
            "size_m": [round(float(s.max() - s.min()), 4), round(float(t.max() - t.min()), 4)]}


def game_coords(obj):
    """(N, 3) vertex coordinates in the prefab-local game frame (export_fbx: (x, y, z) -> (x, z, -y), then the
    game negates x on import: x-sign gate N)."""
    co = np.array([tuple(v.co) for v in obj.data.vertices], np.float64)
    mw = np.array(obj.matrix_world, np.float64)
    co = co @ mw[:3, :3].T + mw[:3, 3]
    return np.stack([-co[:, 0], co[:, 2], -co[:, 1]], axis=1)


def bounds_game(obj):
    G = game_coords(obj)
    return [[round(float(v), 4) for v in G.min(0)], [round(float(v), 4) for v in G.max(0)]]


def cull(obj, target_m, lod_factor=LOD_FACTOR):
    """Design C.4 with the 1.6.2f1 formula (FS10; [U] until SkiProbeLodA): cull at m_Bias 0 =
    lod_factor x (sx + sy + sz) / 3 x 2^-thin (thin = -log2(sqrt(area / box surface)),
    MeshSystem.cs:505-511); m_Bias = log2(cull0 / target) so the set culls at the host's switch.
    With LodProperties the <= 5 m small-mesh rule does not apply (MeshSystem.cs:458 vs 497)."""
    G = game_coords(obj)
    size = G.max(0) - G.min(0)
    sx, sy, sz = (max(float(v), 1e-4) for v in size)
    area = float(sum(p.area for p in obj.data.polygons))
    surf = 2.0 * (sx * sy + sy * sz + sz * sx)
    thin = -math.log2(math.sqrt(min(max(area / surf, 1e-6), 1.0)))
    csum = sx + sy + sz
    c0 = lod_factor * csum / 3.0 * 2.0 ** -thin
    bias = math.log2(c0 / float(target_m)) if target_m else 0.0
    return {"extent_m": [round(sx, 3), round(sy, 3), round(sz, 3)], "area_m2": round(area, 4),
            "thin_bias": round(thin, 4), "cull_bias0_m": round(c0, 1), "target_m": round(float(target_m), 1),
            "m_Bias": round(bias, 2), "cull_at_bias_m": round(c0 * 2.0 ** -round(bias, 2), 1),
            "lod_factor": lod_factor, "small_mesh_rule": "not applied (LodProperties present)",
            "status": "[U] formula until SkiProbeLodA / F5"}


# ======================================================================================
# CIDs, staging (beacon pattern, surgery/towers-transfer.md B2-B3)
# ======================================================================================

PLACEHOLDER_CID = "0" * 32


def staging_dir(spec):
    return os.path.join(STAGING_ROOT, spec.letter, spec.prop)


def staged_cid(spec):
    return FD.staged_cid(spec.prop, spec.letter, kind="signs")


def mint_cid(spec):
    """The object prefab's hand-minted CID: read the staged .cid, else mint ONCE (uuid4 hex,
    32 lowercase hex, no newline, written with newline='' ) and stage it. Never re-minted."""
    cur = staged_cid(spec)
    if cur:
        return cur
    d = staging_dir(spec)
    os.makedirs(d, exist_ok=True)
    cid = uuid.uuid4().hex
    with open(os.path.join(d, f"{spec.prop}.Prefab.cid"), "w", encoding="utf-8", newline="") as fh:
        fh.write(cid)
    return cid


# Names pass 2026-10-13 (docs/research/art/assets-2026-10/NAMES-2026-10-13.md §4): a sign set placed on its own
# floats at its host's board offsets, so it is hidden from the Find It mod. Find It (pdx 77240 v58, decompiled
# PrefabIndexingSystem.cs:223-229) skips a prefab whose EditorAssetCategoryOverride has a NON-EMPTY include list and an
# exclude entry starting with "FindIt"; the vanilla Editor files it under "Ski Resort/Internal" (EditorAssetCategorySystem
# .AddOverrides, 1.6.0f1 :1023-1061). Serialization copied from vanilla WarningLight01 (docs/research/art/
# prop-emissive-lights.md); positional type table 0-8, validate_prefab.ps1 OK. Display names: LocaleEN.cs Assets.NAME.
OBJECT_PREFAB_TMPL = """{
    "$id": 0,
    "$type": "0|Game.Prefabs.StaticObjectPrefab, Game",
    "name": "@PROP@",
    "active": true,
    "components": {
        "$id": 1,
        "$type": "1|System.Collections.Generic.List`1[[Game.Prefabs.ComponentBase, Game]], mscorlib",
        "$rlength": 1,
        "$rcontent": [
            {
                "$id": 2,
                "$type": "2|Game.Prefabs.EditorAssetCategoryOverride, Game",
                "name": "EditorAssetCategoryOverride",
                "active": true,
                "m_IncludeCategories": {
                    "$id": 3,
                    "$type": "3|System.String[], mscorlib",
                    "$rlength": 1,
                    "$rcontent": [
                        "Ski Resort/Internal"
                    ]
                },
                "m_ExcludeCategories": {
                    "$id": 4,
                    "$type": 3,
                    "$rlength": 1,
                    "$rcontent": [
                        "FindIt"
                    ]
                }
            }
        ]
    },
    "m_Meshes": {
        "$id": 5,
        "$type": "4|Game.Prefabs.ObjectMeshInfo[], Game",
        "$rlength": 1,
        "$rcontent": [
            {
                "$id": 6,
                "$type": "5|Game.Prefabs.ObjectMeshInfo, Game",
                "m_Mesh": $fstrref:"CID:REPLACE_@MESH@_MESH_CID",
                "m_Position": {
                    "$type": "6|Unity.Mathematics.float3, Unity.Mathematics",
                    "x": 0,
                    "y": 0,
                    "z": 0
                },
                "m_Rotation": {
                    "$type": "7|Unity.Mathematics.quaternion, Unity.Mathematics",
                    "value": {
                        "$type": "8|Unity.Mathematics.float4, Unity.Mathematics",
                        "x": 0,
                        "y": 0,
                        "z": 0,
                        "w": 1
                    }
                },
                "m_RequireState": 0
            }
        ]
    },
    "m_Circular": false
}
"""

LOD_FRAGMENT_TMPL = """            {
                "$id": 900,
                "$type": "@T1@|Game.Prefabs.LodProperties, Game",
                "name": "LodProperties",
                "active": true,
                "m_Bias": @BIAS@,
                "m_ShadowBias": 0,
                "m_LodMeshes": {
                    "$id": 901,
                    "$type": "@T2@|Game.Prefabs.RenderPrefab[], Game",
                    "$rlength": 0,
                    "$rcontent": [
                    ]
                }
            },
"""


def stage_prefabs(spec, cid, bias):
    """Write the staging folder: <prop>.Prefab (object prefab template, the ONE literal
    REPLACE_<mesh>_MESH_CID), <mesh> Mesh.LodProperties.fragment (insert as the FIRST element of
    the imported mesh prefab's components $rcontent, $rlength + 1, @T1@ / @T2@ = the next two
    unused type ids, @$id@ 900 / 901 unused) and <mesh> Mesh.surgery.json (the literal edits).
    UTF-8 without BOM, LF. Returns the paths."""
    d = staging_dir(spec)
    os.makedirs(d, exist_ok=True)
    out = []
    p = os.path.join(d, f"{spec.prop}.Prefab")
    with open(p, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(OBJECT_PREFAB_TMPL.replace("@PROP@", spec.prop).replace("@MESH@", spec.mesh))
    out.append(p)
    p = os.path.join(d, f"{spec.mesh} Mesh.LodProperties.fragment")
    with open(p, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(LOD_FRAGMENT_TMPL.replace("@BIAS@", f"{float(bias):g}"))
    out.append(p)
    p = os.path.join(d, f"{spec.mesh} Mesh.surgery.json")
    data = {
        "object_prefab": spec.prop, "object_cid": cid, "mesh": spec.mesh,
        "object_prefab_edit": f"replace the ONE literal REPLACE_{spec.mesh}_MESH_CID with the mesh CID "
                              f"(<SEED>\\{spec.mesh}\\{spec.mesh} Mesh_<hash>.Prefab.cid)",
        "mesh_prefab_edits": [
            {"what": "EmissiveProperties (importer-written, purpose 33 / 34, intensity 5)",
             "edit": f'"intensity": 5 -> "intensity": {EMISSIVE_INTENSITY}', "why": "H2 vanilla shop sign"},
            {"what": "LodProperties (new)", "edit": f"insert {spec.mesh} Mesh.LodProperties.fragment as the "
             f"first components element, components $rlength + 1, @T1@ / @T2@ = the next two unused "
             f"type ids", "m_Bias": float(bias), "why": "design C.4 cull at the host's LOD2 switch [U]"},
            {"what": "ColorProperties", "edit": "none (ControlMask 0: signs are never tinted)"}],
        "row": "fine_detail.sign_set_row: identity, m_ParentMesh 0, m_GroupIndex 0, m_Probability 100",
        "gates": "tools/validate_prefab.ps1 on both files, tools/preflight.ps1 0 / 0, tools/sync-gamedata.ps1",
    }
    with open(p, "w", encoding="utf-8", newline="\n") as fh:
        json.dump(data, fh, indent=2)
    out.append(p)
    return out


# ======================================================================================
# Export
# ======================================================================================

def letter_dir_for(spec, export, out=None):
    """assets/import/<category>/<letter> with --export; beside a scratch host's letter folder
    otherwise (<out>/../<category>/<letter>)."""
    if export and not out:
        return os.path.join(IMPORT_ROOT, spec.category, spec.letter)
    base = os.path.dirname(os.path.abspath(out)) if out else os.path.join(
        __import__("tempfile").gettempdir(), "lodge")
    return os.path.join(base, spec.category, spec.letter)


def export_sign_set(obj, spec, letter_dir, *, host, cid, mint=False, kinds=SHEET):
    """Write <letter_dir>/<spec.mesh>/: the FBX, settings.json, the manifest and, for the sheet
    owner, the five sheet PNGs; a sharer gets shared_from settings and no PNG. host = {name,
    recipe, lot_half (gx, gz), switch_lod2_m, x_convention}. Returns the result dict."""
    import cs2_asset
    import cs2_validate
    name = spec.mesh
    asset_dir = os.path.join(letter_dir, name)
    os.makedirs(asset_dir, exist_ok=True)
    for f in os.listdir(asset_dir):
        p = os.path.join(asset_dir, f)
        if os.path.isfile(p) and (f.startswith(name) or f == "settings.json"):
            os.remove(p)
    owner = name == SHEET_OWNER
    metrics = None
    if owner:
        maps, metrics = paint_sheet(kinds)
        write_sheet(maps, asset_dir, name)
        cs2_asset.write_settings(asset_dir, name)
    else:
        cs2_asset.write_settings(asset_dir, name, shared_from=f"../{SHEET_OWNER}/{SHEET_OWNER}",
                                 shared_maps=MAPS)
    cs2_asset.export_fbx(obj, os.path.join(asset_dir, f"{name}.fbx"))
    tris = cs2_asset.tri_count(obj) if obj.name in __import__("bpy").data.objects else None
    cu = cull(obj, host.get("switch_lod2_m"))
    bg = bounds_game(obj)
    info = json.loads(obj.get("cs2_signset", "{}"))
    row = FD.sign_set_row(spec, cid)
    staged = stage_prefabs(spec, cid, cu["m_Bias"]) if mint else []
    data = {
        "generator": "cs2_signset.py (via lodge.py)", "spec": "fine-detail step 2",
        "kind": "sign set (building sheet)", "object_prefab": spec.prop, "mesh": name,
        "object_cid": cid, "cid_state": "staged" if cid != PLACEHOLDER_CID else "placeholder (not minted)",
        "host": {k: v for k, v in host.items() if k != "recipe"}, "host_recipe": host.get("recipe"),
        "tris": tris, "bounds_game": {"min": bg[0], "max": bg[1]}, "faces": info,
        "cull": cu, "emissive": {"intensity_at_surgery": EMISSIVE_INTENSITY, "purpose": "importer 33 / 34",
                                 "lit_kinds": [k.key for k in kinds if k.lit and k.state == "painted"],
                                 "status": "lit while the owner chain has power (FS16: the "
                                           "host's ElectricityConsumer, else its efficiency)"},
        "row": {k: (list(v) if isinstance(v, tuple) else v) for k, v in row.items()},
        "x_convention": FD.X_CONVENTION,
        "sheet": {"owner": SHEET_OWNER, "is_owner": owner, "size": SHEET_A, "ppm": SHEET_PPM,
                  "gutter_px": GUTTER, "dilate_px": DILATE, "maps": list(MAPS),
                  "layout_sha": layout_hash(kinds), "free_rows_top": free_rows(kinds),
                  "kinds": [{"key": k.key, "host": k.host, "size_m": list(k.size), "rect": list(k.rect),
                             "state": k.state, "lit": k.lit, "note": k.note} for k in kinds],
                  "metrics": metrics},
        "folder_layout": {"owner": f"assets/import/signs/{SHEET_LETTER}/{SHEET_OWNER}/",
                          "same_round_sharer": f"assets/import/signs/{SHEET_LETTER}/SkiSignSet<Host>{SHEET_LETTER}/"
                                               f" -> ../{SHEET_OWNER}/{SHEET_OWNER}_<Map>.png",
                          "later_round_sharer": f"assets/import/signs/<L>/SkiSignSet<Host><L>/ -> "
                                                f"../../{SHEET_LETTER}/{SHEET_OWNER}/{SHEET_OWNER}_<Map>.png [U]; "
                                                f"fallback: m_SurfaceAssets repoint to the owner Surface CID (FS8)",
                          "staging": f"assets/staging/prefabs/signs/<L>/{spec.prop}/"},
        "staging": [os.path.relpath(p, REPO).replace("\\", "/") for p in staged],
    }
    cs2_validate.write_manifest(asset_dir, name, data)
    return {"asset_dir": asset_dir, "name": name, "tris": tris, "cull": cu, "bounds_game": bg,
            "metrics": metrics, "row": row, "owner": owner, "staged": staged}


def validate_sign_set(asset_dir, name, owner_dir=None):
    """cs2_validate on the folder (a sharer staged with the owner's PNGs under its own names,
    as the importer resolves sharedAssets)."""
    import shutil
    import tempfile
    import cs2_validate
    spec = {"kind": "prop", "lod0_cap": SG6_CAP, "min_texture": 128, "expected_lods": (0,)}
    if owner_dir is None:
        f, s = cs2_validate.validate_asset_folder_ex(asset_dir, name, spec)
    else:
        tmp = tempfile.mkdtemp(prefix="signset_stage_")
        try:
            d = os.path.join(tmp, name)
            os.makedirs(d)
            shutil.copy2(os.path.join(asset_dir, name + ".fbx"), d)
            for m in MAPS:
                shutil.copy2(os.path.join(owner_dir, f"{SHEET_OWNER}_{m}.png"), os.path.join(d, f"{name}_{m}.png"))
            with open(os.path.join(d, "settings.json"), "w", encoding="utf-8") as fh:
                fh.write("{}")
            f, s = cs2_validate.validate_asset_folder_ex(d, name, spec)
        finally:
            shutil.rmtree(tmp, ignore_errors=True)
    return {"errors": [m for lv, c, m in f if lv == "ERROR"], "warnings": [m for lv, c, m in f if lv == "WARN"],
            "tris": s.get("tris"), "tex": sorted(s.get("tex") or [])}


# ======================================================================================
# Contracts (tests/test_signset.py runs them on real builds AND on mutated inputs)
# ======================================================================================

def sg1_uv_in_rect(obj, spec, kinds=SHEET, A=SHEET_A, tol=0.5):
    """Each face's UV bbox (texels) equals its kind's rect within tol texels. -> problems."""
    import cs2_asset
    me = obj.data
    table = json.loads(me[cs2_asset.PART_TABLE_PROP])
    pv = np.zeros(len(me.polygons), np.int32)
    me.attributes["part"].data.foreach_get("value", pv)
    by_face = dict(spec.faces)
    lay = me.uv_layers["UVMap"].data
    bad = []
    for p in me.polygons:
        face = table[pv[p.index] - 1]
        r = kind(by_face[face]).rect
        uv = np.array([tuple(lay[li].uv) for li in p.loop_indices]) * A
        got = (uv[:, 0].min(), uv[:, 1].min(), uv[:, 0].max(), uv[:, 1].max())
        if max(abs(a - b) for a, b in zip(got, r)) > tol:
            bad.append(f"{face}: UV bbox {tuple(round(v, 2) for v in got)} vs rect {r}")
    return bad


def sg2_positions(set_game_by_face, host_game_by_face, tol=0.001):
    """{face: (4, 3) game coords} of the sign set vs the pre-change host's own label quads:
    every corner within tol (corner sets compared order-free). -> problems."""
    bad = []
    for f, P in sorted(set_game_by_face.items()):
        Q = host_game_by_face.get(f)
        if Q is None:
            bad.append(f"{f}: not in the pre-change host")
            continue
        d = max(float(np.min(np.linalg.norm(np.asarray(Q) - p, axis=1))) for p in np.asarray(P))
        if d > tol:
            bad.append(f"{f}: {d * 1000:.2f} mm from the host board")
    return bad


def sg3_no_routed_left(tables, labels_block, spec):
    """No routed face in any host part table (LOD0 / 1 / 2) nor in the manifest labels block."""
    want = set(spec.face_names())
    bad = [f"{t}: {sorted(want & set(names))}" for t, names in tables.items() if want & set(names)]
    if labels_block and want & set(labels_block):
        bad.append(f"labels block: {sorted(want & set(labels_block))}")
    return bad


def sg4_identity_row(block, spec, cid):
    """Exactly one role-signset row: this spec's prop, the CID, position 0, identity, parent 0."""
    rows = [o for o in (block or {}).get("objects", []) if o.get("kind") == "mod" and o.get("role") == "signset"]
    if len(rows) != 1:
        return [f"{len(rows)} signset rows"]
    r = rows[0]
    bad = []
    if r.get("prefab") != spec.prop or r.get("m_Object") != cid or r.get("mesh") != spec.mesh:
        bad.append(f"row names {r.get('prefab')} / {r.get('m_Object')} / {r.get('mesh')}")
    if any(abs(float(v)) > 1e-9 for v in r.get("position", ())):
        bad.append(f"position {r.get('position')}")
    if [float(v) for v in r.get("rotation", ())] != [0.0, 0.0, 0.0, 1.0]:
        bad.append(f"rotation {r.get('rotation')}")
    if r.get("m_ParentMesh") != 0:
        bad.append(f"m_ParentMesh {r.get('m_ParentMesh')}")
    return bad


def sg5_in_lot(bounds, lot_half, margin=0.0):
    """Sign-set game bounds inside the lot (gx within +-lot_half[0], gz within +-lot_half[1])."""
    (lx, _ly, lz), (hx, _hy, hz) = bounds
    bad = []
    if lx < -lot_half[0] + margin or hx > lot_half[0] - margin:
        bad.append(f"gx {lx}..{hx} vs +-{lot_half[0]}")
    if lz < -lot_half[1] + margin or hz > lot_half[1] - margin:
        bad.append(f"gz {lz}..{hz} vs +-{lot_half[1]}")
    return bad


def sg6_tris(n, cap=SG6_CAP):
    return [] if int(n) <= cap else [f"{n} tris > {cap}"]


def infer_settings_args(data):
    """(supplied_lods, dedicated) that write_settings needs to reproduce a shipped settings.json,
    or None when the file is not a write_settings default output (e.g. a sharer's)."""
    lods = sorted(int(k[3:]) for k in data if k.startswith("LOD") and k[3:].isdigit())
    shared = data.get("sharedAssets", {})
    ded = set()
    for key, val in shared.items():
        if "_LOD" in key and key == val:
            ded.add(int(key.split("_LOD")[1].split("_")[0]))
    return tuple(lods), tuple(sorted(ded))


def sg7_settings_proof(paths, write_settings=None):
    """Regenerate every listed shipped settings.json with cs2_asset.write_settings DEFAULT keywords
    (supplied / dedicated LODs inferred) into a temp folder: byte-identical. Files that are not a
    default output (sharers: sharedAssets to another folder) are skipped and listed.
    -> (identical, differ, skipped)."""
    import shutil
    import tempfile
    import cs2_asset
    ws = write_settings or cs2_asset.write_settings
    same, diff, skip = [], [], []
    tmp = tempfile.mkdtemp(prefix="sg7_")
    try:
        for p in paths:
            raw = open(p, "rb").read()
            data = json.loads(raw.decode("utf-8"))
            name = os.path.basename(os.path.dirname(p))
            if any("/" in str(v) or "\\" in str(v) for v in data.get("sharedAssets", {}).values()):
                skip.append(p)
                continue
            lods, ded = infer_settings_args(data)
            out = ws(tmp, name, supplied_lods=lods, dedicated_texture_lods=ded)
            (same if open(out, "rb").read() == raw else diff).append(p)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    return same, diff, skip


# ======================================================================================
# Fine-detail step 3d: repaint the OWNER's sheet in place (a reserved rect painted at approval / a kind
# appended in free space; build/fine-detail-step3d-patrol-sign.md)
# ======================================================================================

def owner_dir_shipped():
    return os.path.join(IMPORT_ROOT, "signs", SHEET_LETTER, SHEET_OWNER)


def repaint_owner(owner_dir=None, kinds=SHEET):
    """Repaint the sheet owner's five PNGs from `kinds` (= the frozen-then-appended SHEET) and update ONLY the
    sheet-derived blocks of its manifest: sheet.layout_sha / free_rows_top / kinds / metrics, emissive.lit_kinds
    and the files block's bytes / sha1 of the five PNGs (+ a "sheet_repaint" note). The owner's FBX, settings.json
    and every other manifest key are left byte-equal, so the school (the owner's host) needs no re-export:
    painting reserved rects / appending kinds never moves a rect the school's faces use (SS4). Refuses an owner
    folder without its manifest. Returns {map: sha1} of the written PNGs."""
    import datetime
    import hashlib
    d = owner_dir or owner_dir_shipped()
    mp = os.path.join(d, f"{SHEET_OWNER}.manifest.json")
    if not os.path.isfile(mp):
        raise FileNotFoundError(f"repaint_owner: no owner manifest {mp}")
    with open(mp, "r", encoding="utf-8") as fh:
        man = json.load(fh)
    bad = check_layout(kinds)
    if bad:
        raise RuntimeError(f"repaint_owner: layout problems {bad[:3]}")
    old = (man.get("sheet") or {}).get("kinds") or []
    for ok_ in old:                         # SS4 at runtime: no rect an exported owner listed may move
        k = [x for x in kinds if x.key == ok_["key"]]
        if not k or list(k[0].rect) != list(ok_["rect"]) or list(k[0].size) != list(ok_["size_m"]):
            raise RuntimeError(f"repaint_owner: kind {ok_['key']} moved / vanished (append-only sheet)")
    maps, metrics = paint_sheet(kinds)
    paths = write_sheet(maps, d, SHEET_OWNER)
    sh = man.setdefault("sheet", {})
    sh["layout_sha"] = layout_hash(kinds)
    sh["free_rows_top"] = free_rows(kinds)
    sh["kinds"] = [{"key": k.key, "host": k.host, "size_m": list(k.size), "rect": list(k.rect),
                    "state": k.state, "lit": k.lit, "note": k.note} for k in kinds]
    sh["metrics"] = metrics
    if isinstance(man.get("emissive"), dict):
        man["emissive"]["lit_kinds"] = [k.key for k in kinds if k.lit and k.state == "painted"]
    out = {}
    files = man.setdefault("files", {})
    for p in paths:
        f = os.path.basename(p)
        with open(p, "rb") as fh:
            b = fh.read()
        files[f] = {"bytes": len(b), "sha1": hashlib.sha1(b).hexdigest()}
        out[f] = files[f]["sha1"][:12]
    man["sheet_repaint"] = {
        "by": "cs2_signset.repaint_owner (fine-detail step 3d)",
        "utc": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "why": "reserved rects painted at approval / kinds appended in free space; no rect moved (SS4), "
               "the owner's FBX / settings.json untouched",
        "n_kinds": len(kinds), "painted": [k.key for k in kinds if k.state == "painted"],
        "reserved": [k.key for k in kinds if k.state == "reserved"]}
    with open(mp, "w", encoding="utf-8") as fh:
        json.dump(man, fh, indent=2, sort_keys=False, default=str)
    print(f"[signset] repainted the owner sheet {d}: {len(kinds)} kinds, layout {sh['layout_sha']}, "
          f"free rows {sh['free_rows_top']}, PNG {out}")
    return out


__all__ = ["SHEET", "Kind", "kind", "layout_hash", "check_layout", "shelf_pack", "paint_sheet",
           "write_sheet", "route", "host_recipe", "build_sign_set", "export_sign_set", "cull",
           "bounds_game", "game_coords", "mint_cid", "staged_cid", "stage_prefabs", "letter_dir_for",
           "validate_sign_set", "sg1_uv_in_rect", "sg2_positions", "sg3_no_routed_left",
           "sg4_identity_row", "sg5_in_lot", "sg6_tris", "sg7_settings_proof", "SHEET_OWNER",
           "SHEET_PPM", "SHEET_A", "MAPS", "EMISSIVE_INTENSITY", "PLACEHOLDER_CID",
           "Typeface", "TYPEFACES", "typeface", "typeface_path", "draw_type", "draw_blank", "ttf_line", "ttf_ref",
           "track_for", "face_record", "NC_LODGE_FACE", "NC_UTILITY_FACE", "FONT_DIR"]


if __name__ == "__main__":
    # fine-detail step 3d: blender --background --factory-startup --python-exit-code 1 --python cs2_signset.py --
    #   --repaint-owner [DIR]   repaint the sheet owner's PNGs + manifest sheet blocks (default: the shipped owner)
    _argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if "--repaint-owner" in _argv:
        _i = _argv.index("--repaint-owner")
        _d = _argv[_i + 1] if len(_argv) > _i + 1 and not _argv[_i + 1].startswith("--") else None
        repaint_owner(_d)
    else:
        print("cs2_signset.py: nothing to do (use -- --repaint-owner [DIR])")
