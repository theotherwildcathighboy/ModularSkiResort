"""
school_emblem.py - the ski school's emblem designs (T278, 1.0.4): original pictograms in the ski_launcher_glyph primitive
format (CANVAS 256, x right, y DOWN; ("line", pts, w) | ("fill", pts) | ("disk", cx, cy, r) | ("ring", cx, cy, r, w) |
("rect", x0, y0, x1, y1, r)). No bpy: tests and the design sheet import it in plain Python.

WHY. The 1.0.3 tower field drew ski_launcher_glyph.cand_d, a launcher-icon candidate (skier on a solid slope wedge). On
the building it read as a figure kneeling on a sled: the skis sank into the white wedge, the one bent leg read as a
kneel, and nothing said "ski" at 45 m (owner review 2026-11-20, T278).

WHERE IT IS DRAWN. The school's tower field (emblem_school_face, 3.2 m) becomes a sign tile: a white rim, SCHOOL_RED
inside, the glyph at 0.82 of the face (2.62 m, the figure from head to ski 2.04 m) in white. The field and the two flag
cloths sit in the school's label rect of the FAMILY SHEET (55.9 px/m: 147 px across the glyph at mip 0). A 1080p frame at
45 m shows 1,304 / 45 = 29 px/m, so the game samples mip 1 (73 px across the glyph). Rule (test_school_emblem E1): every
stroke >= 3 texels at that mip = 10.5 canvas units; the flag (22 px at mip 1) is a color read only.

THE DESIGN follows the r7 research (RESEARCH.md section 2, the Olympic / Paralympic pictogram conventions, nothing copied):
the head a disk of its own, the torso 37 deg forward of vertical, the knees bent with an open angle between thigh and
torso, ONE long ski on a 15 deg slope with its tip turned up, a slope line under it, the arm reaching forward and the pole
angled back from the hand with a basket.

DROP-IN. A design is a function returning the primitive list, registered in EMBLEMS. recipes/school.py names the one
the v1 build paints (EMBLEM_V1). A sign or mascot variant drops in as one more function plus that one constant: same
face, same rect, same painters (cs2_bldg.draw_glyph through cs2_bldg.glyph_prims).

Every design here is drawn for this mod. None copies a ski-school logo, mascot or trademark.
"""

CANVAS = 256
# The read rule (tests/test_school_emblem.py E1 / E2): the tower field's glyph is FIELD_GLYPH_M across on the family sheet
# at SHEET_PPM (147 texels at mip 0); a 1080p frame at 45 m (screen 1,304 / 45 = 29 px/m) samples mip MIP_45M (73 texels).
FIELD_GLYPH_M = 2.624           # recipes/school.py FIELD_FRAC_V1 0.82 x the 3.2 m field (1.0.3: 0.85 = 2.72 m, no tile)
SHEET_PPM = 55.9                # SkiTileSheetW, label rect emblem_school_face (manifest tiled.tile_mesh.label_rects)
MIP_45M = 1
MIN_STROKE_TEXELS = 3.0
SLOPE_DEG = 15.0


def glyph_texels(mip=MIP_45M):
    """Texels across the tower field's glyph at `mip`."""
    return FIELD_GLYPH_M * SHEET_PPM / 2 ** mip


def _on_ski(x):
    """The ski's center line: y at canvas x (the slope SLOPE_DEG, down to the right)."""
    return 160.0 + (x - 22.0) * 0.266


def skier_parts():
    """skier_b by part name (tests swap one part for a negative)."""
    return {
        "slope": ("line", [(16.0, _on_ski(16.0) + 26.0), (240.0, _on_ski(240.0) + 26.0)], 12.0),   # 12-unit gap to the ski
        "ski": ("line", [(22.0, 160.0), (206.0, _on_ski(206.0)), (230.0, 195.0)], 16.0),          # tip turned up
        "leg": ("line", [(116.0, _on_ski(116.0)), (150.0, 146.0), (108.0, 121.0)], 28.0),         # boot, knee, hip
        "torso": ("line", [(108.0, 121.0), (138.0, 81.0)], 32.0),                                 # 37 deg forward
        "head": ("disk", 170.0, 40.0, 22.0),                                                      # 14 units clear
        "arm": ("line", [(138.0, 86.0), (196.0, 100.0)], 18.0),                                   # reaching forward
        "pole": ("line", [(196.0, 100.0), (178.0, 172.0)], 14.0),                                 # angled back
        "basket": ("disk", 180.0, 164.0, 9.0),
    }


def skier_b():
    """The school's v1 emblem: a recreational skier going downhill to the right on a slope (see the module docstring)."""
    return list(skier_parts().values())


EMBLEMS = {
    "emblem_skier_b": skier_b,
}


def prims(name):
    """The primitive list of a registered emblem (KeyError for a name that is not one)."""
    return EMBLEMS[name]()


def min_stroke(pr):
    """Thinnest stroke of a primitive list in canvas units (line width, disk diameter, ring width); fills are not
    measured (none here)."""
    ws = [float(p[2]) for p in pr if p[0] == "line"] + [2.0 * float(p[3]) for p in pr if p[0] == "disk"] +          [float(p[4]) for p in pr if p[0] == "ring"]
    return min(ws) if ws else None


def extent(pr):
    """(x0, y0, x1, y1) canvas units covered by the strokes (line half widths, disk radii)."""
    xs, ys = [], []
    for p in pr:
        if p[0] == "line":
            h = float(p[2]) / 2.0
            xs += [q[0] - h for q in p[1]] + [q[0] + h for q in p[1]]
            ys += [q[1] - h for q in p[1]] + [q[1] + h for q in p[1]]
        elif p[0] == "disk":
            xs += [p[1] - p[3], p[1] + p[3]]
            ys += [p[2] - p[3], p[2] + p[3]]
    return min(xs), min(ys), max(xs), max(ys)
