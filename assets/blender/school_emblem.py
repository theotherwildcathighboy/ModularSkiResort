"""
school_emblem.py - the ski school's emblem designs (T278, 1.0.4): original pictograms in the ski_launcher_glyph primitive
format (CANVAS 256, x right, y DOWN; ("line", pts, w) | ("fill", pts) | ("disk", cx, cy, r) | ("ring", cx, cy, r, w) |
("rect", x0, y0, x1, y1, r)). No bpy: tests and the design sheet import it in plain Python.

WHY. The 1.0.3 tower field drew ski_launcher_glyph.cand_d, a launcher-icon candidate (skier on a solid slope wedge). On
the building it read as a figure kneeling on a sled: the skis sank into the white wedge, the one bent leg read as a
kneel, and nothing said "ski" at 45 m (owner review 2026-11-20, T278).

WHERE IT IS DRAWN. The school's tower field (emblem_school_face, 3.2 m, the v1 glyph at 0.95 = 3.04 m) and the two flag
cloths sit in the school's label rect of the FAMILY SHEET (55.9 px/m: 170 px at mip 0). A 1080p frame at 45 m shows
1,304 / 45 = 29 px/m, so the game samples mip 1 (85 px across the glyph). Rule (test_school_emblem E1): every stroke
>= 3 texels at that mip = 9 canvas units; the flag (22 px at mip 1) is a color read only.

DROP-IN. A design is a function returning the primitive list, registered in EMBLEMS. recipes/school.py names the one
the v1 build paints (EMBLEM_V1). A sign or mascot variant drops in as one more function plus that one constant: same
face, same rect, same painters (cs2_bldg.draw_glyph through cs2_bldg.glyph_prims).

Every design here is drawn for this mod. None copies a ski-school logo, mascot or trademark.
"""

CANVAS = 256
# The read rule (tests/test_school_emblem.py E1 / E2): the tower field's glyph is FIELD_GLYPH_M across on the family sheet
# at SHEET_PPM (152 texels at mip 0); a 1080p frame at 45 m (screen 1,304 / 45 = 29 px/m) samples mip MIP_45M (76 texels).
FIELD_GLYPH_M = 3.04            # recipes/school.py FIELD_FRAC_V1 0.95 x the 3.2 m field (1.0.3: 0.85 = 2.72 m)
SHEET_PPM = 55.9                # SkiTileSheetW, label rect emblem_school_face (manifest tiled.tile_mesh.label_rects)
MIP_45M = 1
MIN_STROKE_TEXELS = 3.0


def glyph_texels(mip=MIP_45M):
    """Texels across the tower field's glyph at `mip`."""
    return FIELD_GLYPH_M * SHEET_PPM / 2 ** mip


def skier_b():
    """(B, recommended) A recreational skier going downhill to the right: skis on an 18 deg line with an upturned tip, both
    knees bent forward (one leg drawn, the classic pictogram), hips back, torso leaning in, the arm forward with its pole
    planted ahead. Head clear of the shoulders (a 13-unit gap = 3.9 texels at mip 1), no ground wedge: the skis read as skis."""
    ski = ("line", [(28.0, 166.0), (204.0, 222.0), (228.0, 208.0)], 16.0)
    leg = ("line", [(118.0, 186.0), (152.0, 146.0), (110.0, 120.0)], 28.0)    # ankle, knee, hip
    torso = ("line", [(110.0, 120.0), (142.0, 80.0)], 32.0)                   # hip -> shoulder
    head = ("disk", 174.0, 40.0, 22.0)
    arm = ("line", [(142.0, 84.0), (182.0, 108.0)], 18.0)                     # shoulder -> hand
    pole = ("line", [(182.0, 108.0), (214.0, 184.0)], 15.0)                   # 15 = 0.8 x the arm (critic r7)
    return [ski, leg, torso, head, arm, pole]


def skier_speed():
    """(C, the fun one) skier_b at speed: two speed lines behind the figure, parallel to the ski. (A first draft threw
    snow-spray dots behind the tails; at the 45 m mip they clumped into a smudge, critic r7.)"""
    lines = [("line", [(30.0, 100.0), (74.0, 114.0)], 12.0), ("line", [(16.0, 128.0), (70.0, 145.0)], 12.0)]
    return skier_b() + lines


def kid_pizza():
    """(D, the school's own lesson) a child in the snowplow ("pizza") seen from the front: big head, arms out for
    balance, legs in an A, the ski tips meeting in front. (No bobble hat: head + pompom read as a snowman, critic r7.)"""
    head = ("disk", 128.0, 48.0, 26.0)
    body = ("fill", [(106.0, 90.0), (150.0, 90.0), (160.0, 150.0), (96.0, 150.0)])
    arms = ("line", [(58.0, 132.0), (104.0, 96.0), (152.0, 96.0), (198.0, 132.0)], 18.0)
    legl = ("line", [(112.0, 146.0), (90.0, 200.0)], 24.0)
    legr = ("line", [(144.0, 146.0), (166.0, 200.0)], 24.0)
    skil = ("line", [(62.0, 204.0), (124.0, 234.0)], 16.0)
    skir = ("line", [(194.0, 204.0), (132.0, 234.0)], 16.0)
    return [head, body, arms, legl, legr, skil, skir]


EMBLEMS = {
    "emblem_skier_b": skier_b,
    "emblem_skier_speed": skier_speed,
    "emblem_kid_pizza": kid_pizza,
}


def prims(name):
    """The primitive list of a registered emblem (KeyError for a name that is not one)."""
    return EMBLEMS[name]()


def min_stroke(pr):
    """Thinnest stroke of a primitive list in canvas units (line width, disk diameter, ring width); fills count by their
    shortest edge-to-edge span is not measured (none is thin here)."""
    ws = [float(p[2]) for p in pr if p[0] == "line"] + [2.0 * float(p[3]) for p in pr if p[0] == "disk"] + \
         [float(p[4]) for p in pr if p[0] == "ring"]
    return min(ws) if ws else None
