"""
Deterministic Build-strip icon generator for the Modular Ski Resort mod, style E.

Draws the 25 icons of the hub's Build strip (256x256 RGBA PNG + 32-hex .png.cid sidecar each): the
nine of the 2026-10-04 style-E round and, since 2026-10-11 (round 2), the 16 tiles of contract 6
(lodge tiers, school, patrol, detachable / gondola / tram) listed after the nine below.
Style E ("a small flat color picture fills a rounded plate") was chosen on 2026-10-04
from the options sheet docs/research/art/icons-2026-10-04/run-icon-options.png (rows A to F;
pick: E, with the snow gun throwing its snow UP and over, never "puking it out"). The final set
and its review sheet: docs/research/art/icons-2026-10-04/final-e/final/ (sheet.png). Design notes
and the round's record: docs/research/econ-round/e1-spec.md section 8 ("Icons, style E").

Tiles (file name = the prefab icon the hub shows; keeping a name keeps its .cid):
    SkiResortGate            Welcome Sign: board high on two tall log posts, emblem, lumpy snow
    SkiAFrameDayLodge        A-frame day lodge: steep snowy red roof, warm glass gable, door, pines
    SkiBaseStationBuilding   drive terminal at the foot of the hill: red hood on a concrete column,
                             raised bullwheel, two strands up to a lift tower, a chair, timber booth
    SkiTopStationBuilding    the same terminal mirrored on the summit plateau, the strands
                             arriving from a tower down the left flank, a chair
    SkiChairliftLine         a chair (bent hanger, footrest) on the cable between two towers
                             (new 2026-10-04; until then the lift tile reused SkiResortTab.png)
    SkiRunNaturalA           bumpy untracked snow, S-turn ski track down the fall line, two pines
    SkiRunGroomedA           four corduroy stripes along the slope, empty sky
    SkiRunSnowmakingA        a fan gun (barrel on a swivel post) throwing a jet UP that widens,
                             thins into mist at the top of its arc and drifts down as flakes onto
                             the run lower right; no corduroy (groomed vs snowmaking in grayscale:
                             stripes vs gun + plume)
    SkiRunNightA             dark sky, low moon, steel floodlight mast with amber lamps, corduroy
Round 2 (2026-10-11; verdict B2 of docs/research/art/assets-2026-10/verdicts-2026-10-11.md; build
record docs/research/art/assets-2026-10/build/icons-round-2.md; each after its asset's previews):
    SkiLodgeHut              small chalet set into a hillside on a stone plinth (tall downhill), gable
                             front, shuttered warm windows, stovepipe, a pine up the slope
    SkiLodgeMid              chalet: a taller gable-front wing behind, the long stone-and-timber
                             dining hall in front, the gazebo on the terrace
    SkiLodgeSummit           stone house (gable front, tall chimney) above a glass dining room
                             cantilevered over a pier, on a gray crag with snow ledges
    SkiLodgeGrand            redrawn 2026-10-11 for L5 revision 1 (verdict B1, the 10 x 8 U-plan),
                             seen from the slope side: two king-post wing gables with balcony
                             stacks in front, the cross gable and the long roof stepping up behind,
                             the clock tower (red spire) and two chimneys over the ridge, the pool
                             terrace (turquoise lagoon, hot tub) between the wings
    SkiSchool                timber school: low wing, tower with the open lantern, the skier sign
                             and a red flag, two-story wing; a child on the magic carpet
    SkiPatrolHut             dark-timber hut, teal metal roof, door and lamp, the patrol disc (white
                             cross on red, the tile's only red)
    SkiPatrolHQ              the HQ on posts with its deck, saltbox roof, gridded window, patrol
                             disc high in the gable
    SkiDQBaseStationBuilding the "loaf" (a fat red round-ended housing on steel legs, dark drive
    SkiDQTopStationBuilding  frame) at the foot / on the summit, booth, strands to a tower, a
                             detachable quad (C-frame hanger, padded seat, bar down, footrest)
    SkiDetachableLine        the detachable quad on the cable between two towers
    SkiGondolaBaseStationBuilding / SkiGondolaTopStationBuilding
                             the station house (cedar over stone, red loaf roof, the portal in the
                             line-end face), the cable out of the portal to a tower, a red cabin
    SkiGondolaLine           a red cabin (dark window band, white emblem) between two towers
    SkiTramBaseStationBuilding  the hall (stone base, glazing behind steel X-braces, snowy shed
                             roof), the brick clock tower, two track ropes to a lattice tower, the
                             cabin and carriage
    SkiTramTopStationBuilding   the exoskeleton: concrete block, steel portal frame with braces and
                             the face-on bullwheel, ropes from a lattice tower, the cabin
    SkiTramLine              the big cabin and its four-wheel carriage on the two track ropes
                             between two lattice towers
T38 (2026-10-17; playtest item 84, docs/research/field/2026-10-15/build/T38-run-type-icons.md), FINAL: the snowmaking
and night tiles are redrawn and SkiRunCatTrackA is new (a cat track used to borrow the groomed tile's icon);
natural and groomed keep their original pictures (verdict after three redraws). All on the family
scene (the same sky and the same white slope falling top-left to bottom-right):
    SkiRunSnowmakingA        a bigger orange fan gun and a thicker white plume on the slope (PLUME_SPEC_T38)
    SkiRunNightA             dark navy sky, lit slope, big cream crescent, two stars, a floodlight with big amber lamps
    SkiRunCatTrackA          a level green groomed ribbon with tread marks crossing the slope and a red snowcat on it
Later additions, each listed apart from STRIP_ORDER: SkiSafetyFence (2026-10-26) and SkiBusDepot (2026-11-01: the corten
tower, the shed roof, the coach at the curb).
The other 23 strip tiles regenerate byte-identical to the set installed on 2026-10-04 / 10-11. Previews:
assets/preview/hub-icons-2026-10-17/ (make_sheet.py: old vs final at 28 px and 56 px).

Launcher (not a Build-strip tile; added 2026-10-04 night, Round F7 item E1,
docs/research/field-test-7-fixes.md):
    SkiResortTab             was the hub launcher's icon (HubLauncher.tsx HUB_ICON, the floating
                             button and the Universal Mod Panel square, ~33 px at 1080p): a snowy
                             peak, one red chair on a diagonal cable. No longer drawn since S1 F9
                             (the launchers use SkiLauncherGlyph.svg, a white tinted mask made by
                             ski_launcher_glyph.py; docs/research/ski-days/spec/ch5-launcher.md);
                             kept as the rollback only. Installed in
                             ImportedData\\SkiResortCategory\\ (not SkiResortIcons) and keeps its
                             hand-minted cid aed39078... (KEEP_CID), so HUB_ICON stays as is. The
                             old dark-plate PNG: gamedata-mirror/retired/2026-10-04-launcher-pre-edit/.
                             Review sheet: ski_icons_sheet.py --launcher.

Style E: every tile is a picture that fills the rounded plate of the old icon frame (10..245 px,
corner radius 45): a sky gradient and a snow ground are the picture's background and DO fill the
plate; every object drawn on them (a "mark": trees, corduroy, gun and plume, lamps and moon, sign,
lodge, stations, chair and cable) keeps >= 20 px (design aim 22) to the plate edge, measured per
icon on every run (exit code 1 if any icon breaks it). The lift tiles (base "1", top "2", line
"3" of every lift type) keep the top-left 84 x 84 free of marks and darken the sky there for the hub's white step
digit (hub.module.scss .buildStep). Every tile ends with a darker plate foot (the hub's run type
stripe stands apart) and a thin dark rim (the plate keeps its outline on the active blue tile).

Look (taken from the game's own UI icons, Cities2_Data/Content/Game/UI/Media/Game/Icons/*.svg:
LargeParks, Trees, ParksAndRecreation, Electricity): flat fills, no outlines, two tones per object
(lit face / shade face, light from the upper left), muted natural palette, a soft shadow ellipse
under things that stand on the ground. No text in the icons.

No Blender/bpy dependency: pure numpy SDF rasterization (analytic per-sample coverage at 4x
supersampling, premultiplied compositing, box downsample) plus a minimal PNG encoder, so it runs
directly on Blender's bundled python.exe:

    "<blender>\\<ver>\\python\\bin\\python.exe" ski_icons.py --out <dir> [--only A,B] [--verbose]

A default run (no --only) writes the 25 strip tiles only. The launcher is written only when named:
--only SkiResortTab (F7 fixer: so a bulk copy of the output folder cannot duplicate its fixed cid).

Install, strip tiles: copy the PNG + .png.cid pairs into ImportedData\\SkiResortIcons\\ (game side), then run
tools/sync-gamedata.ps1 and tools/preflight.ps1 (preflight FAILS on a cid value used by two files).
A not-yet-imported prefab's icon may sit there before its prefab (round 2: the surgery round sets
each new prefab's m_Icon to its tile's cid, the table in build/icons-round-2.md).
Install, launcher (--only SkiResortTab, into its own --out folder): only the PNG, into
ImportedData\\SkiResortCategory\\; its installed .cid already holds the KEEP_CID value. The prefabs reference an icon as
"m_Icon": "assetdb://global/<cid>" (UIObject), and the hub copies the same URL into
mod/SkiResortUI/src/hub/buildItems.ts BUILD_ITEM_ICON. Review sheet (strip mock at the real
1080p / 4K sizes, grayscale, before / after): ski_icons_sheet.py next to this file.

Everything is deterministic (no randomness, no clock, no downloads), including the .cid sidecars:
each cid is the md5 hex digest of a fixed salt + png name, so re-running the script never churns
installed prefab references. (A cid only needs to be a unique 32-hex id for the asset database;
the game does not derive it from file content - see the hand-minted SkiResortTab.png.cid
precedent, which KEEP_CID reproduces instead of the md5 rule.) The 2026-10-04 port reproduces the
reviewed final set byte for byte; round 2 leaves those nine byte-identical (md5-checked).
"""

import argparse
import hashlib
import os
import struct
import sys
import zlib

import numpy as np

CANVAS = 256
SS = 4                              # supersample factor (AA); the shipped set is drawn at 4
CID_SALT = "SkiResortMod.Icons.v1/"  # md5(salt + png name) -> deterministic 32-hex cid
# cids minted before this generator: kept as they are so no reference changes
# (SkiResortTab.png: HubLauncher.tsx HUB_ICON, hand-minted 2026-08-10)
KEEP_CID = {"SkiResortTab.png": "aed3907873aa4d74bb420190d3044e78"}

PLATE = (10.0, 245.0, 45.0)         # plate extents and corner radius (the old dark-plate frame)
MARGIN = 22.0                       # design margin of every mark to the plate edge (rule: >= 20)
RULE_MARGIN = 20.0
STEP_KEEPOUT = (0.0, 0.0, 84.0, 84.0)   # top-left box kept free of marks on the step tiles

VERBOSE = False                     # --verbose: geometry notes (chair clearance, plume path)

# ---------------------------------------------------------------------------------------------
# Palette
# ---------------------------------------------------------------------------------------------
HUB_CYAN = (110, 205, 255)          # hub.module.scss: snowmaking
HUB_AMBER = (255, 196, 70)          # night
HUB_WHITE = (236, 244, 252)         # groomed

SKY_TOP, SKY_BOT = (44, 100, 172), (128, 186, 234)
CORNER_DARK = (26, 60, 116)         # step-digit corner of the three lift tiles
RIM_COL, RIM_W, RIM_A = (18, 34, 62), 6.0, 0.55     # plate rim (reads on the active blue tile)
FOOT_COL, NFOOT_COL = (150, 174, 212), (64, 78, 124)  # plate foot (keeps the type stripe apart)
FOOT_Y0, FOOT_A = 222.0, 0.85
NSKY_TOP, NSKY_BOT = (14, 22, 50), (44, 62, 112)
SNOW_HI, SNOW_LO = (252, 253, 255), (210, 225, 243)
SNOW_SHADE = (190, 210, 236)
FAR_LIT, FAR_SHADE = (214, 228, 246), (184, 204, 234)      # distant mountains
GROOVE = (124, 162, 208)
NSNOW_HI, NSNOW_LO = (156, 172, 210), (108, 126, 172)
NGROOVE = (80, 98, 146)
LAMPLIGHT = (255, 230, 168)
SHADOW = (104, 136, 186)            # ground shadow ellipses (alpha 0.3)

PINE_LIT, PINE_SHADE = (93, 165, 42), (47, 110, 20)          # ~95 / ~102 deg, vanilla Trees hue
PINE2_LIT, PINE2_SHADE = (80, 148, 38), (38, 96, 18)
CONC_LIT, CONC_SHADE = (176, 184, 196), (140, 150, 164)     # station structure
NOZZLE = (226, 234, 242)
TRUNK = (137, 94, 64)

WOOD_LIT, WOOD_SHADE, WOOD_DARK = (204, 153, 94), (160, 112, 62), (110, 74, 44)
POST_LIT, POST_SHADE = (150, 104, 60), (112, 76, 44)
ROOF_LIT, ROOF_SHADE = (206, 78, 60), (158, 54, 46)
GLASS, GLASS_HI = (86, 128, 176), (124, 166, 206)
WARM, WARM_HI = (255, 196, 84), (255, 232, 150)
STEEL_LIT, STEEL_SHADE, STEEL_DARK = (150, 162, 174), (98, 110, 124), (54, 62, 76)
EMBLEM_BG = (40, 74, 126)

GUN_LIT, GUN_SHADE, GUN_FRAME = (236, 108, 66), (182, 70, 48), (58, 66, 80)
PLUME, PLUME_SHADE = (250, 253, 255), (184, 230, 252)
MOON = (252, 240, 196)

PEAK_SHADE, PEAK_ROCK = (150, 176, 218), (112, 132, 170)    # launcher peak: shade face, rock

STRIPE_W = 16.0                     # corduroy stroke (256 canvas) -> 1.9 px at 30 px

X = Y = None


def build_grid():
    """Supersampled pixel-center coordinate grids (canvas units) for the current SS."""
    global X, Y
    n = CANVAS * SS
    c = (np.arange(n, dtype=np.float64) + 0.5) / SS
    X, Y = np.meshgrid(c, c)


def _log(msg):
    if VERBOSE:
        print(msg, flush=True)


# ---------------------------------------------------------------------------------------------
# SDF primitives (negative inside), canvas units, y down.
# ---------------------------------------------------------------------------------------------
def sd_segment(ax, ay, bx, by, w, x=None, y=None):
    """Capsule stroke of width w along segment A->B."""
    x = X if x is None else x
    y = Y if y is None else y
    px, py = x - ax, y - ay
    dx, dy = bx - ax, by - ay
    t = np.clip((px * dx + py * dy) / (dx * dx + dy * dy), 0.0, 1.0)
    return np.hypot(px - t * dx, py - t * dy) - w / 2.0


def sd_disk(cx, cy, r):
    return np.hypot(X - cx, Y - cy) - r


def sd_ring(cx, cy, r, w):
    """Circle outline (annulus) of radius r, stroke width w."""
    return np.abs(np.hypot(X - cx, Y - cy) - r) - w / 2.0


def sd_ellipse(cx, cy, rx, ry):
    k = np.hypot((X - cx) / rx, (Y - cy) / ry)
    return (k - 1.0) * min(rx, ry)


def sd_round_rect(x0, y0, x1, y1, r, x=None, y=None):
    """Filled rounded rectangle covering [x0,x1]x[y0,y1] with corner radius r."""
    x = X if x is None else x
    y = Y if y is None else y
    cx, cy = (x0 + x1) / 2.0, (y0 + y1) / 2.0
    hx, hy = (x1 - x0) / 2.0 - r, (y1 - y0) / 2.0 - r
    qx = np.abs(x - cx) - hx
    qy = np.abs(y - cy) - hy
    outside = np.hypot(np.maximum(qx, 0.0), np.maximum(qy, 0.0))
    inside = np.minimum(np.maximum(qx, qy), 0.0)
    return outside + inside - r


def sd_orect(cx, cy, ux, uy, hl, hw, r=0.0):
    """Rounded rectangle centered at (cx, cy), long axis along the unit vector (ux, uy), half
    length hl along it and half width hw across it."""
    lx = (X - cx) * ux + (Y - cy) * uy
    ly = -(X - cx) * uy + (Y - cy) * ux
    return sd_round_rect(-hl, -hw, hl, hw, r, x=lx, y=ly)


def sd_polygon(pts):
    """Filled simple polygon, pts = [(x,y), ...] in order (either winding)."""
    d = np.full(X.shape, np.inf)
    inside = np.zeros(X.shape, dtype=bool)
    n = len(pts)
    for i in range(n):
        ax, ay = pts[i]
        bx, by = pts[(i + 1) % n]
        d = np.minimum(d, sd_segment(ax, ay, bx, by, 0.0))
        cond = (ay > Y) != (by > Y)
        with np.errstate(divide="ignore", invalid="ignore"):
            xin = ax + (Y - ay) * (bx - ax) / (by - ay)
        inside ^= cond & (X < np.where(cond, xin, np.inf))
    return np.where(inside, -d, d)


def sd_polyline(pts, w):
    d = np.full(X.shape, np.inf)
    for i in range(len(pts) - 1):
        d = np.minimum(d, sd_segment(pts[i][0], pts[i][1], pts[i + 1][0], pts[i + 1][1], w))
    return d


def left_of(xs):
    """Half plane x < xs (negative inside)."""
    return X - xs


def above(ys):
    """Half plane y < ys."""
    return Y - ys


def below(ys):
    return ys - Y


def up_normal(dx, dy):
    """Unit normal of the direction (dx, dy) that points UP the screen (y down)."""
    ln = np.hypot(dx, dy)
    nx, ny = dy / ln, -dx / ln
    if ny > 0 or (ny == 0 and nx > 0):
        nx, ny = -nx, -ny
    return nx, ny


def upper_side(ax, ay, bx, by):
    """Half plane on the upper side of the line through A and B (negative inside)."""
    nx, ny = up_normal(bx - ax, by - ay)
    return -((X - ax) * nx + (Y - ay) * ny)


def union(*ds):
    out = ds[0]
    for d in ds[1:]:
        out = np.minimum(out, d)
    return out


def inter(*ds):
    out = ds[0]
    for d in ds[1:]:
        out = np.maximum(out, d)
    return out


def minus(a, b):
    return np.maximum(a, -b)


def vgrad(c0, c1, y0, y1):
    t = np.clip((Y - y0) / (y1 - y0), 0.0, 1.0)[..., None]
    return np.asarray(c0, np.float64) * (1 - t) + np.asarray(c1, np.float64) * t


# ---------------------------------------------------------------------------------------------
# Layers + rasterizer
# ---------------------------------------------------------------------------------------------
class L:
    """A paint layer: SDF, color (RGB or (n,n,3) array), alpha (scalar or array), and whether it
    is a MARK (kept >= MARGIN from the plate edge, measured) or a FIELD (sky / snow / terrain)."""

    def __init__(self, sdf, col, a=1.0, mark=True):
        self.sdf, self.col, self.a, self.mark = sdf, col, a, mark


def two_tone(sdf, lit_region, lit, shade, mark=True):
    """Shade color on the whole shape, lit color on its part inside lit_region (an SDF)."""
    return [L(sdf, shade, mark=mark), L(inter(sdf, lit_region), lit, mark=mark)]


def cov(d):
    return np.clip(0.5 - d * SS, 0.0, 1.0)


def plate_sdf():
    x0, x1, r = PLATE
    return sd_round_rect(x0, x0, x1, x1, r)


def rasterise(layers):
    """Returns (rgba uint8 256x256, mark coverage float 256x256). Everything is clipped to the
    plate; the mark coverage is UNclipped so a mark that would be cut shows up in the measure."""
    n = CANVAS * SS
    rgb = np.zeros((n, n, 3))
    al = np.zeros((n, n))
    mk = np.zeros((n, n))
    cc = cov(plate_sdf())
    for ly in layers:
        c = cov(ly.sdf)
        a = c * ly.a * cc
        col = np.asarray(ly.col, np.float64)
        if col.ndim == 1:
            col = col[None, None, :]
        rgb = rgb * (1 - a)[..., None] + col * a[..., None]
        al = al * (1 - a) + a
        if ly.mark:
            mk = np.maximum(mk, c * (ly.a if np.isscalar(ly.a) else (ly.a > 0.02)))
    rgba = np.concatenate([rgb, al[..., None] * 255.0], axis=2)
    small = rgba.reshape(CANVAS, SS, CANVAS, SS, 4).mean(axis=(1, 3))
    a = small[..., 3:4] / 255.0
    with np.errstate(divide="ignore", invalid="ignore"):
        un = np.where(a > 0, small[..., :3] / a, 0.0)
    out = np.concatenate([un, small[..., 3:4]], axis=2)
    mk_small = mk.reshape(CANVAS, SS, CANVAS, SS).mean(axis=(1, 3))
    return np.clip(np.rint(out), 0, 255).astype(np.uint8), mk_small


def scalar_plate(px, py):
    x0, x1, r = PLATE
    return float(sd_round_rect(x0, x0, x1, x1, r, x=np.array(px), y=np.array(py)))


def mark_safe(px, py):
    """<= 0 inside the area every mark must stay in."""
    return scalar_plate(px, py) + MARGIN


# ---------------------------------------------------------------------------------------------
# Shared pieces
# ---------------------------------------------------------------------------------------------
def sky(night=False):
    if night:
        return L(plate_sdf(), vgrad(NSKY_TOP, NSKY_BOT, 10, 200), mark=False)
    return L(plate_sdf(), vgrad(SKY_TOP, SKY_BOT, 10, 210), mark=False)


def step_corner():
    """Darker sky in the top-left corner of the three lift tiles, behind the hub's white step
    digit (drawn right after the sky, so terrain covers it)."""
    d = np.hypot(X - 10.0, Y - 10.0)
    a = 0.80 * np.clip(1.0 - d / 150.0, 0.0, 1.0) ** 1.2
    return L(plate_sdf(), CORNER_DARK, a, mark=False)


def finish(night=False):
    """Drawn last on every tile: a darker foot (the bottom of the picture turns mid-tone so the
    hub's white type stripe under a run tile stands apart from the snow) and a thin dark rim (the
    plate keeps its outline on the active, blue tile). Both stay inside the 22 px mark margin."""
    t = np.clip((Y - FOOT_Y0) / (245.0 - FOOT_Y0), 0.0, 1.0) ** 1.4 * FOOT_A
    foot = L(plate_sdf(), NFOOT_COL if night else FOOT_COL, t, mark=False)
    p = plate_sdf()
    rim = L(np.maximum(p, -(p + RIM_W)), RIM_COL, RIM_A, mark=False)
    return [foot, rim]


def ground(fn, y_grad0, x_from=-14.0, x_to=270.0, n=48, night=False):
    """Snow below the curve y = fn(x). Returns (sdf, layer)."""
    xs = np.linspace(x_from, x_to, n)
    pts = [(float(x), float(fn(x))) for x in xs]
    g = sd_polygon(pts + [(x_to, 300.0), (x_from, 300.0)])
    hi, lo = (NSNOW_HI, NSNOW_LO) if night else (SNOW_HI, SNOW_LO)
    return g, L(g, vgrad(hi, lo, y_grad0, 245), mark=False)


def far_range(pts, lit=FAR_LIT, shade=FAR_SHADE):
    """Distant mountain range (background field): a polyline of alternating valley / peak points
    closed to the bottom; each peak's right flank in the shade tone, bounded by a ridge line that
    runs from the peak down and to the right."""
    body = sd_polygon(pts + [(pts[-1][0], 300.0), (pts[0][0], 300.0)])
    out = [L(body, lit, mark=False)]
    for i in range(1, len(pts) - 1):
        px, py = pts[i]
        if py < pts[i - 1][1] and py < pts[i + 1][1]:          # a peak
            rx, ry = pts[i + 1]
            dy = 300.0 - py
            sh = sd_polygon([(px, py), (rx, ry), (rx + dy, 300.0), (px + 0.35 * dy, 300.0)])
            out.append(L(inter(body, sh), shade, mark=False))
    return out


def shadow(cx, cy, rx, ry, a=0.30):
    return L(sd_ellipse(cx, cy, rx, ry), SHADOW, a)


def pine(cx, base, h, hw, lit=PINE_LIT, shade=PINE_SHADE, snow=True):
    """Three-tier pine, two tones (left half lit), snow on the tier tops; base = ground y."""
    top = base - h
    trunk_h = h * 0.15
    crown_b = base - trunk_h
    ch = crown_b - top
    tiers, caps = [], []
    for i, (f0, f1, w) in enumerate(((0.00, 0.44, 0.56), (0.24, 0.72, 0.80), (0.50, 1.00, 1.00))):
        ty0 = top + ch * f0
        ty1 = top + ch * f1
        tiers.append(sd_polygon([(cx, ty0), (cx - hw * w, ty1), (cx + hw * w, ty1)]))
        if i:       # snow on the two lower tiers only (the top cap is a sliver at 30 px)
            caps.append(sd_polygon([(cx, ty0), (cx - hw * w * 0.50, ty0 + (ty1 - ty0) * 0.50),
                                    (cx + hw * w * 0.34, ty0 + (ty1 - ty0) * 0.34)]))
    crown = union(*tiers)
    out = [L(sd_round_rect(cx - max(h * 0.06, 4.5), crown_b - 6, cx + max(h * 0.06, 4.5), base,
                           2.5), TRUNK)]
    out += two_tone(crown, left_of(cx), lit, shade)
    if snow:
        out.append(L(inter(union(*caps), crown), SNOW_HI, 0.92))
    return out


def tower(x, foot, head, arm=30.0, w=12.0, arm2=None):
    """Lift tower: tapered steel pole, a dark cross-arm (the sheave train) at the head; arm2 = y of
    a second, lower cross-arm (for the second rope strand at the terminals)."""
    pole = sd_polygon([(x - w * 0.5, head), (x + w * 0.5, head), (x + w * 0.75, foot + 4),
                       (x - w * 0.75, foot + 4)])
    out = two_tone(pole, left_of(x), STEEL_LIT, STEEL_SHADE)
    out.append(L(sd_round_rect(x - arm / 2 - 2, head - 4, x + arm / 2 + 2, head + 5, 3), STEEL_DARK))
    if arm2 is not None:
        out.append(L(sd_round_rect(x - arm / 2, arm2 - 4, x + arm / 2, arm2 + 5, 3), STEEL_DARK))
    return out


def chair(gx, gy, k=1.0):
    """Chairlift chair seen from the side, grip on the cable at (gx, gy): one bent hanger from the
    grip down and back to the top of the seat back, the red seat (back to the left = downhill),
    a short footrest under the seat front. Lines stay >= 7 px at any scale."""
    hx = gx + 2 * k
    lw = max(7.0, 8.0 * k)
    grip = sd_round_rect(gx - 9 * k, gy - 7 * k, gx + 9 * k, gy + 7 * k, 3 * k)
    hanger = sd_polyline([(hx, gy), (hx, gy + 16 * k), (hx - 6 * k, gy + 26 * k),
                          (hx - 22 * k, gy + 34 * k)], lw)
    foot = sd_polyline([(hx + 32 * k, gy + 80 * k), (hx + 34 * k, gy + 96 * k),
                        (hx + 48 * k, gy + 96 * k)], max(7.0, 6.5 * k))
    back = sd_round_rect(hx - 34 * k, gy + 28 * k, hx - 18 * k, gy + 82 * k, 6 * k)
    seat = sd_round_rect(hx - 34 * k, gy + 68 * k, hx + 40 * k, gy + 84 * k, 6 * k)
    lit = union(sd_round_rect(hx - 34 * k, gy + 28 * k, hx - 24 * k, gy + 76 * k, 5 * k),
                sd_round_rect(hx - 30 * k, gy + 68 * k, hx + 40 * k, gy + 76 * k, 4 * k))
    return [L(union(grip, hanger, foot), STEEL_DARK), L(union(back, seat), ROOF_SHADE),
            L(lit, ROOF_LIT)]


def bullwheel(cx, cy, r, ring, hub, spokes=5, spoke_w=6.0, ang0=-90.0):
    """Bullwheel seen face-on: rim, hub, radial spokes."""
    parts = [sd_ring(cx, cy, r, ring), sd_disk(cx, cy, hub)]
    for k in range(spokes):
        a = np.radians(ang0 + 360.0 * k / spokes)
        parts.append(sd_segment(cx, cy, cx + np.cos(a) * r, cy + np.sin(a) * r, spoke_w))
    return union(*parts)


# ---------------------------------------------------------------------------------------------
# The five non-run tiles
# ---------------------------------------------------------------------------------------------
def icon_sign():
    ly = [sky()]
    ly += far_range([(-14, 170), (26, 138), (58, 156), (100, 112), (142, 150), (182, 122),
                     (224, 156), (270, 138)])
    _, g = ground(lambda x: 192.0 - 8.0 * np.sin(np.pi * (x + 14.0) / 284.0), 180)
    ly.append(g)
    ly.append(shadow(128, 214, 74, 7))
    # two log posts, taller below the board than the board is high, round tops with a snow cap
    for px in (84.0, 172.0):
        post = sd_round_rect(px - 10, 40, px + 10, 216, 10)
        ly += two_tone(post, left_of(px - 1), POST_LIT, POST_SHADE)
        ly.append(L(inter(sd_disk(px, 47, 13), above(50)), SNOW_HI))
    bx0, bx1, by0, by1 = 52.0, 204.0, 68.0, 128.0
    board = sd_round_rect(bx0, by0, bx1, by1, 8)
    ly.append(L(board, WOOD_SHADE))
    ly.append(L(inter(board, above(by1 - 11)), WOOD_LIT))
    pnl = (bx0 + 12, by0 + 12, bx1 - 12, by1 - 14)
    panel = sd_round_rect(*pnl, 5)
    ly.append(L(panel, EMBLEM_BG))
    base_y = pnl[3] + 1
    big = [(82, base_y), (114, base_y - 34), (146, base_y)]
    small = [(126, base_y), (150, base_y - 23), (174, base_y)]
    for pk in (small, big):
        tri = sd_polygon(pk)
        ly += two_tone(inter(tri, panel), left_of(pk[1][0]), SNOW_HI, SNOW_SHADE)
    # a thin, lumpy layer of snow on the board (drifts, one lump hanging over the right end)
    lumps = union(sd_round_rect(bx0 - 2, by0 - 5, bx1 + 2, by0 + 4, 4),
                  sd_disk(70, by0 - 3, 6.5), sd_disk(98, by0 - 6, 8.0), sd_disk(122, by0 - 2, 5.5),
                  sd_disk(150, by0 - 4, 7.0), sd_disk(178, by0 - 2, 5.5),
                  sd_disk(bx1 - 4, by0 + 3, 6.0))
    ly.append(L(lumps, SNOW_HI))
    return ly


def _inset_tri(apex, bl, br, d):
    """Symmetric A-frame triangle with the two roof edges moved inward by d (base kept)."""
    ax, ay = apex
    lx, ly_ = bl
    ex, ey = ax - lx, ay - ly_
    ln = np.hypot(ex, ey)
    nx, ny = -ey / ln, ex / ln                  # inward normal of the left edge (right / down)
    if nx < 0:
        nx, ny = -nx, -ny
    ox, oy = lx + nx * d, ly_ + ny * d
    t = (ax - ox) / ex                          # meet the axis x = ax
    apex_y = oy + ey * t
    tb = (ly_ - oy) / ey                        # meet the base line y = ly_
    base_x = ox + ex * tb
    return (ax, apex_y), (base_x, ly_), (2 * ax - base_x, ly_)


def icon_lodge():
    ly = [sky()]
    ly += far_range([(-14, 158), (34, 124), (76, 150), (128, 118), (176, 150), (222, 122),
                     (270, 150)])
    _, g = ground(lambda x: 182.0 - 6.0 * np.sin(np.pi * (x + 14.0) / 284.0), 172)
    ly.append(g)
    ly.append(shadow(128, 210, 86, 7))
    ly += pine(54.0, 204.0, 104.0, 20.0, PINE2_LIT, PINE2_SHADE)
    ly += pine(204.0, 204.0, 90.0, 17.0, PINE2_LIT, PINE2_SHADE)
    apex, bl, br = (128.0, 36.0), (52.0, 210.0), (204.0, 210.0)
    outer = sd_polygon([apex, br, bl])
    ly += two_tone(outer, left_of(128), ROOF_LIT, ROOF_SHADE)
    a1, l1, r1 = _inset_tri(apex, bl, br, 11.0)
    snow_band = minus(outer, sd_polygon([a1, r1, l1]))
    ly += two_tone(snow_band, left_of(128), SNOW_HI, SNOW_SHADE)
    a2, l2, r2 = _inset_tri(apex, bl, br, 27.0)
    gable = sd_polygon([a2, r2, l2])
    # one warm glass block over a timber base; the door below (no mullions, rail or chimney:
    # the red triangle and the two pines carry the tile)
    sill = 156.0
    ly.append(L(inter(gable, below(sill)), WOOD_LIT))
    ly.append(L(inter(gable, above(sill)), vgrad(WARM_HI, WARM, a2[1], sill)))
    ly.append(L(sd_round_rect(114, 168, 142, 212, 4), WOOD_DARK))
    ly.append(L(sd_round_rect(120, 176, 136, 190, 2), WARM))
    return ly


ROPE_W = 8.0                        # both rope strands at the terminals (>= 7 px rule)
ROPE_SEP = 20.0                     # haul / return strand spacing (center to center, across)

# Terminal layout, one dict per station. hood = (x0, x1, top, height); wheel = (cx, cy, r);
# column = (x0, x1); booth = (x0, x1, top); theta = rope angle (deg) from the wheel to the tower,
# side = +1 the line leaves to the right (up the hill), -1 to the left (down the flank);
# tower_x = the first tower; chair_x = the chair's grip on the lower strand (k = its scale).
BASE_SPEC = dict(hood=(36.0, 150.0, 100.0, 28.0), wheel=(124.0, 146.0, 19.0), column=(74.0, 90.0),
                 booth=(40.0, 66.0, 170.0), theta=30.0, side=1, tower_x=206.0, chair_x=168.0,
                 chair_k=0.56)
TOP_SPEC = dict(hood=(104.0, 222.0, 66.0, 28.0), wheel=(130.0, 112.0, 19.0), column=(164.0, 180.0),
                booth=(188.0, 216.0, 138.0), theta=22.0, side=-1, tower_x=58.0, chair_x=90.0,
                chair_k=0.52)


def rope_pair(wheel, theta, side, tx, sep=ROPE_SEP):
    """The two strands (haul and return) as parallel lines ROPE_SEP apart: the upper one leaves
    the top of the face-on bullwheel on a tangent, the lower one leaves its rim further round.
    They run to the tower at x = tx. Returns ((P_up, Q_up), (P_lo, Q_lo)) and the strand slope
    (dy/dx)."""
    cx, cy, r = wheel
    a = np.radians(theta)
    dx, dy = side * np.cos(a), -side * np.sin(a)
    nx, ny = up_normal(dx, dy)
    pu = (cx + nx * r, cy + ny * r)
    t0 = np.sqrt(max(r * r - (r - sep) ** 2, 0.0))
    pl = (cx + nx * (r - sep) + dx * t0, cy + ny * (r - sep) + dy * t0)
    slope = dy / dx
    qu = (tx, pu[1] + (tx - pu[0]) * slope)
    ql = (tx, pl[1] + (tx - pl[0]) * slope)
    return (pu, qu), (pl, ql), slope


def terminal_layers(spec, ground_y):
    """A lift terminal seen from the side (the subject is the machine): a concrete column under a
    long red hood, the bullwheel face-on under the hood's line end at rope height, a small timber
    operator booth at the other end. Returns (back layers, front layers): the strands go between
    them (wheel and hood over the rope ends)."""
    x0, x1, top, hh = spec["hood"]
    cx, cy, r = spec["wheel"]
    c0, c1 = spec["column"]
    back = []
    col = sd_round_rect(c0, top + hh - 6, c1, ground_y(0.5 * (c0 + c1)) + 3, 2)
    back += two_tone(col, left_of(c0 + (c1 - c0) * 0.55), CONC_LIT, CONC_SHADE)
    bx0, bx1, by0 = spec["booth"]
    by1 = ground_y(0.5 * (bx0 + bx1)) + 2
    wall = sd_round_rect(bx0, by0, bx1, by1, 3)
    back += two_tone(wall, left_of(bx0 + (bx1 - bx0) * 0.72), WOOD_LIT, WOOD_SHADE)
    back.append(L(sd_round_rect(bx0 + 6, by0 + 9, bx1 - 6, by0 + 21, 2.5), GLASS))
    back.append(L(sd_round_rect(bx0 + 6, by0 + 9, bx1 - 6, by0 + 15, 2.5), GLASS_HI))
    back.append(L(sd_round_rect(bx0 - 4, by0 - 7, bx1 + 4, by0 + 3, 4), SNOW_HI))
    front = [L(bullwheel(cx, cy, r, 8.0, 7.0, spokes=6, spoke_w=5.0), STEEL_DARK),
             L(sd_disk(cx, cy, 3.5), STEEL_LIT)]
    hood = sd_round_rect(x0, top, x1, top + hh, 12)
    front.append(L(hood, ROOF_SHADE))
    front.append(L(inter(hood, above(top + hh * 0.58)), ROOF_LIT))
    front.append(L(union(sd_round_rect(x0 + 3, top - 8, x1 - 3, top + 4, 6),
                         sd_disk(x0 + 0.30 * (x1 - x0), top - 6, 6.0),
                         sd_disk(x0 + 0.68 * (x1 - x0), top - 5, 5.0)), SNOW_HI))
    return back, front


def station_scene(spec, ground_y):
    """Tower, the two strands, the terminal and a chair hung from the lower strand (below both).
    Returns the layers and the strand end points."""
    (pu, qu), (pl, ql), slope = rope_pair(spec["wheel"], spec["theta"], spec["side"],
                                          spec["tower_x"])
    tx = spec["tower_x"]
    foot = ground_y(tx)
    ly = [shadow(tx, foot + 1, 13, 3.5)]
    ly += tower(tx, foot, qu[1], arm=26.0, arm2=ql[1])
    back, front = terminal_layers(spec, ground_y)
    ly += back
    ly.append(L(union(sd_segment(pu[0], pu[1], qu[0], qu[1], ROPE_W),
                      sd_segment(pl[0], pl[1], ql[0], ql[1], ROPE_W)), STEEL_DARK))
    ly += front
    # the chair hangs from the LOWER strand, so it hangs clear below both (on a rising pair the
    # upper strand's chair would have the return rope cut through its seat back)
    gx = spec["chair_x"]
    gy = pl[1] + (gx - pl[0]) * slope
    k = spec["chair_k"]
    ly += chair(gx, gy, k)
    _log(f"    chair grip ({gx:.0f},{gy:.0f}) bottom y {gy + 99.5 * k:.0f}, ground "
         f"{ground_y(gx + 30 * k):.0f}; tower head {qu[1]:.0f} / {ql[1]:.0f}, foot "
         f"{ground_y(tx):.0f}")
    return ly, (pu, qu, pl, ql)


def _hill(x):
    t = np.clip((np.asarray(x, np.float64) - 148.0) / 122.0, 0.0, 1.0)
    return 206.0 - 96.0 * t ** 1.3


def icon_base_station():
    ly = [sky(), step_corner()]
    ly += far_range([(-14, 168), (34, 132), (84, 170), (124, 150), (170, 190)])
    gsdf, g = ground(_hill, 110)
    ly.append(g)
    # the shaded side of the hill
    ly.append(L(inter(gsdf, sd_polygon([(186, 300), (262, 110), (300, 110), (300, 300)])),
                SNOW_SHADE, 0.6, mark=False))
    ly.append(shadow(98, 208, 56, 6))
    scene, _ = station_scene(BASE_SPEC, lambda x: float(_hill(x)))
    return ly + scene


TOP_MT = [(-14, 238), (58, 216), (128, 172), (200, 170), (240, 182), (270, 196)]


def _summit(x):
    xs, ys = zip(*TOP_MT)
    return float(np.interp(x, xs, ys))


def icon_top_station():
    ly = [sky(), step_corner()]
    ly += far_range([(-14, 196), (30, 150), (88, 196)])
    ly += far_range([(120, 176), (176, 112), (214, 140), (238, 118), (270, 160)])
    mt = sd_polygon(TOP_MT + [(270, 300), (-14, 300)])
    ly.append(L(mt, vgrad(SNOW_HI, SNOW_LO, 160, 245), mark=False))
    ly.append(L(inter(mt, sd_polygon([(206, 170), (270, 170), (270, 300), (236, 300)])),
                SNOW_SHADE, 0.7, mark=False))
    ly.append(shadow(160, 172, 54, 5))
    scene, _ = station_scene(TOP_SPEC, _summit)
    return ly + scene


def _lift_ground(x):
    t = np.clip((x + 14.0) / 284.0, 0.0, 1.0)
    return 226.0 - 76.0 * t ** 1.35


def icon_lift():
    ly = [sky(), step_corner()]
    gsdf, g = ground(_lift_ground, 150)
    ly.append(g)
    ly.append(L(inter(gsdf, sd_polygon([(120, 300), (270, 120), (300, 300)])), SNOW_SHADE, 0.45,
                mark=False))
    l_x, l_head = 62.0, 108.0
    r_x, r_head = 194.0, 48.0
    ly.append(shadow(l_x, _lift_ground(l_x) + 2, 14, 4))
    ly.append(shadow(r_x, _lift_ground(r_x) + 2, 14, 4))
    ly += tower(l_x, _lift_ground(l_x), l_head, arm=28.0)
    ly += tower(r_x, _lift_ground(r_x), r_head, arm=28.0)
    # cable with a slight sag between the two tower heads
    n = 24
    cable = []
    for k in range(n + 1):
        t = k / n
        cx = l_x + (r_x - l_x) * t
        cy = l_head + (r_head - l_head) * t + 10.0 * 4 * t * (1 - t) - 1
        cable.append((cx, cy))
    ly.append(L(sd_polyline(cable, 7.0), STEEL_DARK))
    # chair at t = 0.5: grip on the cable, one bent hanger, seat (back left = downhill), footrest
    gx = 0.5 * (l_x + r_x)
    gy = 0.5 * (l_head + r_head) + 10.0 - 1
    ly += chair(gx, gy, 1.0)
    return ly


# ---------------------------------------------------------------------------------------------
# Run tiles (the option-E scene: the run falls from the top-left to the bottom-right)
# ---------------------------------------------------------------------------------------------
RIDGE = dict(xa=10.0, xb=245.0, y0=46.0, y1=182.0, p=1.12)
SPAN = (-14.0, 270.0)


def profile(xs, xa, xb, y0, y1, p=1.7, bumps=0.0):
    t = np.clip((np.asarray(xs, np.float64) - xa) / (xb - xa), 0.0, 1.0)
    ys = y0 + (y1 - y0) * (1.0 - (1.0 - t) ** p)
    if bumps:
        ys = ys - bumps * np.abs(np.sin(np.pi * 3.0 * t))
    return ys


def ry(xv):
    return float(profile([xv], **RIDGE)[0])


def ridge_points(bumps=0.0, n=40):
    xs = np.linspace(SPAN[0], SPAN[1], n)
    ys = profile(xs, bumps=bumps, **RIDGE)
    return list(zip(xs.tolist(), ys.tolist()))


def offset_polyline(pts, d):
    p = np.asarray(pts)
    tan = np.gradient(p, axis=0)
    tan /= np.hypot(tan[:, 0], tan[:, 1])[:, None]
    nrm = np.stack([-tan[:, 1], tan[:, 0]], axis=1)
    return p + nrm * d


def trim_to_safe(pts, clearance, trim0=0.0, trim1=0.0):
    ok = [mark_safe(px, py) <= -clearance for px, py in pts]
    best, cur = [], []
    for p, o in zip(pts, ok):
        if o:
            cur.append(p)
        else:
            if len(cur) > len(best):
                best = cur
            cur = []
    if len(cur) > len(best):
        best = cur
    pts = np.asarray(best)
    if len(pts) < 2:
        return []
    seg = np.hypot(*np.diff(pts, axis=0).T)
    s = np.concatenate([[0.0], np.cumsum(seg)])
    a, b = trim0, s[-1] - trim1
    if b <= a:
        return []
    ss = np.linspace(a, b, max(4, int((b - a) / 8)))
    return list(zip(np.interp(ss, s, pts[:, 0]).tolist(), np.interp(ss, s, pts[:, 1]).tolist()))


class RunScene:
    def __init__(self, night=False, bumps=0.0):
        self.rp = ridge_points(bumps=bumps)
        hi, lo = (NSNOW_HI, NSNOW_LO) if night else (SNOW_HI, SNOW_LO)
        self.snow = inter(sd_polygon(self.rp + [(SPAN[1], 300.0), (SPAN[0], 300.0)]), plate_sdf())
        self.layers = [sky(night), L(self.snow, vgrad(hi, lo, 60, 245), mark=False)]


def corduroy(rp, col, offsets=(28.0, 58.0, 88.0), stagger=12.0, w=None):
    w = STRIPE_W if w is None else w
    layers = []
    for k, d in enumerate(offsets):
        line = offset_polyline(rp, d)
        pts = trim_to_safe([tuple(p) for p in line], w / 2 + 0.5,
                           trim0=4.0 + stagger * k, trim1=4.0)
        if pts:
            ln = float(np.sum(np.hypot(*np.diff(np.asarray(pts), axis=0).T)))
            if ln >= 36.0:
                layers.append(L(sd_polyline(pts, w), col))
    return layers


TRACK = (150, 180, 222)             # powder track: lighter and thinner than the corduroy


def ski_track(rp, col, d=46.0, w=9.0, wavelength=60.0, amp=13.0, trim0=6.0, trim1=6.0):
    """Linked S-turns down the fall line (top-left to bottom-right, along the slope), one thin
    line: someone skied the fresh powder."""
    line = offset_polyline(rp, d)
    pts = np.asarray(trim_to_safe([tuple(p) for p in line], w / 2 + amp + 1.0,
                                  trim0=trim0, trim1=trim1))
    seg = np.hypot(*np.diff(pts, axis=0).T)
    s = np.concatenate([[0.0], np.cumsum(seg)])
    ss = np.linspace(0.0, s[-1], max(8, int(s[-1] / 3)))
    px = np.interp(ss, s, pts[:, 0])
    py = np.interp(ss, s, pts[:, 1])
    wav = []
    for i in range(len(ss)):
        ph = np.sin(2 * np.pi * ss[i] / wavelength) * amp
        tx = px[min(i + 1, len(ss) - 1)] - px[max(i - 1, 0)]
        ty = py[min(i + 1, len(ss) - 1)] - py[max(i - 1, 0)]
        ln = np.hypot(tx, ty)
        wav.append((px[i] - ty / ln * ph, py[i] + tx / ln * ph))
    return [L(sd_polyline(wav, w), col)]


NAT_PINES = ((122.0, 84.0, 27.0, PINE2_LIT, PINE2_SHADE), (182.0, 120.0, 37.0, PINE_LIT, PINE_SHADE))


# T38 (2026-10-17, playtest item 84 "the run-type icons are not obviously different"): the natural and groomed
# tiles below are the ORIGINAL style-E pictures again. Three redraws were previewed (assets/preview/
# hub-icons-2026-10-17/) and the final verdict was to keep these two ("the colors worked better along
# with the icon itself") and take the new snowmaking, night and cat track tiles (below), which keep the same
# family scene (RunScene: blue sky, white slope falling top-left to bottom-right). The rejected natural /
# groomed redraws (trees, moguls, wavy lines, navy bands) are not in this file; the build note records them.
def icon_run_natural():
    sc = RunScene(bumps=4.0)
    ly = list(sc.layers)
    ly += ski_track(sc.rp, TRACK)
    for x, h, hw, lit, shade in NAT_PINES:
        ly.append(shadow(x + 4, ry(x) + 6, hw * 0.85, 5))
    for x, h, hw, lit, shade in NAT_PINES:
        ly += pine(x, ry(x) + 4, h, hw, lit, shade)
    return ly


GROOMED_OFFSETS = (24.0, 50.0, 76.0, 102.0)


def icon_run_groomed():
    sc = RunScene()
    return sc.layers + corduroy(sc.rp, GROOVE, offsets=GROOMED_OFFSETS, stagger=0.0, w=14.0)


def snow_gun(gx, gy, aim_deg, scale=1.0):
    """Fan snow gun standing on the slope at (gx, gy): a small skid, a steel swivel post, and on
    it a barrel (drum longer than its diameter) pointing UP at aim_deg (screen angle,
    counter-clockwise from +x: 90 = straight up). One dark band at the fan end, a light nozzle
    ring at the front (the opening the snow leaves). Returns (layers, muzzle point, unit aim)."""
    ang = np.radians(aim_deg)
    ux, uy = np.cos(ang), -np.sin(ang)
    k = scale
    piv_x, piv_y = gx, gy - 30.0 * k
    out = [shadow(gx + 3, gy + 2, 22 * k, 4.5 * k)]
    out.append(L(sd_round_rect(gx - 14 * k, gy - 7 * k, gx + 14 * k, gy + 1 * k, 3 * k),
                 STEEL_SHADE))
    post = sd_polygon([(gx - 4.5 * k, piv_y), (gx + 4.5 * k, piv_y), (gx + 6.5 * k, gy - 5 * k),
                       (gx - 6.5 * k, gy - 5 * k)])
    out += two_tone(post, left_of(gx), STEEL_LIT, STEEL_SHADE)
    hl, hw = 19.5 * k, 13.5 * k
    dcx, dcy = piv_x + ux * 4 * k, piv_y + uy * 4 * k
    drum = sd_orect(dcx, dcy, ux, uy, hl, hw, 6.0 * k)
    out += two_tone(drum, upper_side(dcx, dcy, dcx + ux, dcy + uy), GUN_LIT, GUN_SHADE)
    bx, by = dcx - ux * (hl - 3.5 * k), dcy - uy * (hl - 3.5 * k)
    out.append(L(sd_orect(bx, by, ux, uy, 3.5 * k, hw + 1.5 * k, 2.0 * k), GUN_FRAME))
    fx, fy = dcx + ux * (hl - 2.5 * k), dcy + uy * (hl - 2.5 * k)
    out.append(L(sd_orect(fx, fy, ux, uy, 3.0 * k, hw - 1.0 * k, 2.0 * k), NOZZLE))
    out.append(L(sd_disk(piv_x, piv_y, 4.5 * k), GUN_FRAME))
    return out, (dcx + ux * (hl + 1.0 * k), dcy + uy * (hl + 1.0 * k)), (ux, uy)


def drag_path(mx, my, aim_deg, v0, g, k, wind=0.0, dt=1.0, n=600):
    """Thrown snow with air drag in a light breeze: leaves the nozzle at aim_deg with speed v0,
    rises, slows, arcs over and then drifts down and downwind (terminal velocity (wind, g / k)).
    Points (x, y, distance traveled); stops at the snow surface."""
    ang = np.radians(aim_deg)
    vx, vy = v0 * np.cos(ang), -v0 * np.sin(ang)
    x, y, s = mx, my, 0.0
    pts = [(x, y, s)]
    for _ in range(n):
        vx -= k * (vx - wind) * dt
        vy += (g - k * vy) * dt
        x += vx * dt
        y += vy * dt
        s += np.hypot(vx, vy) * dt
        if vy > 0 and y > ry(x) - 6.0:
            break
        pts.append((x, y, s))
    return pts


def path_at(pts, s):
    arr = np.asarray(pts)
    return float(np.interp(s, arr[:, 2], arr[:, 0])), float(np.interp(s, arr[:, 2], arr[:, 1]))


# gun foot x on the slope and how far below the skyline it stands, gun scale, aim (deg above +x),
# angle step between the five fan paths, launch speed, gravity, drag; jet radius at the nozzle and
# its growth per px traveled; the solid jet ends at cone x (path length to the apex) and starts
# fading at fade x (its own length).
PLUME_SPEC = dict(gx=78.0, below=66.0, gun=1.4, aim=60.0, spread=5.0, v0=4.2, g=0.090,
                  k=0.030, wind=0.9, r0=5.0, grow=0.13, cone=0.90, fade=0.68)


def plume(mx, my, aim_deg, spread, v0, g, k, wind, r0, grow, cone, fade):
    """The snow plume as a real fan gun throws it: a jet that leaves the nozzle thin and dense,
    widens into a white cone as it climbs UP and away, and near the top of its arc thins into
    mist (alpha fade, no solid lip) and breaks into flakes that drift down onto the run lower
    right. Five fan paths (aim -/+ 2 spread) carry the flakes. Every disk keeps the mark margin."""
    paths = [drag_path(mx, my, aim_deg + j * spread, v0 * (1.0 - 0.03 * abs(j)), g, k, wind)
             for j in (-2, -1, 0, 1, 2)]
    spine = paths[2]
    s_top = spine[int(np.argmin([p[1] for p in spine]))][2]
    s_cone = cone * s_top
    dmin = np.full(X.shape, np.inf)
    smap = np.zeros(X.shape)
    lit = np.full(X.shape, np.inf)
    jet = []
    for i, (px, py, s) in enumerate(spine):
        if s > s_cone:
            break
        r = r0 + grow * s
        if mark_safe(px, py) > -(r + 3.0):            # + 3 = the translucent rim
            break
        jet.append((px, py, r, s))
        d = sd_disk(px, py, r)
        better = d < dmin
        dmin = np.where(better, d, dmin)
        smap = np.where(better, s, smap)
        # the lit (upper-left) side of the jet
        lit = np.minimum(lit, sd_disk(px - 0.20 * r, py - 0.26 * r, 0.74 * r))
    s_end_jet = jet[-1][3]
    f0 = fade * s_end_jet
    alpha = np.clip((s_end_jet - smap) / (s_end_jet - f0), 0.0, 1.0) ** 1.2
    out = [L(dmin - 3.0, PLUME, 0.5 * alpha), L(dmin, PLUME_SHADE, alpha),
           L(inter(dmin, lit), PLUME, alpha)]
    # flakes: from the fading end of the jet over the top of the arc and down to the snow
    # (three of the five paths, so the flakes stay apart where the paths bunch at the apex)
    flakes = []
    for j in (0, 2, 4):
        pts = paths[j]
        s_end = pts[-1][2]
        sd = s_end_jet + (2.0, 14.0, 7.0)[j // 2]
        m = 0
        while sd < s_end:
            fx, fy = path_at(pts, sd)
            fr = max(5.0, 7.0 - 0.32 * m)
            if mark_safe(fx, fy) <= -(fr + 1.0) and fy < ry(fx) - fr - 2.0:
                flakes.append((fx, fy, fr))
            sd += 20.0 + 1.5 * m
            m += 1
    if flakes:
        out.append(L(union(*[sd_disk(fx, fy, fr) for fx, fy, fr in flakes]), PLUME))
    return out, paths, jet, flakes


# T38: the gun and its plume bigger than PLUME_SPEC's (28 px tile: the gun reads as an orange blob, the
# plume as one thick white arc)
PLUME_SPEC_T38 = dict(gx=74.0, below=72.0, gun=1.75, aim=58.0, spread=5.0, v0=4.4, g=0.090,
                      k=0.030, wind=0.9, r0=10.0, grow=0.22, cone=0.95, fade=0.90)


def icon_run_snowmaking():
    sc = RunScene()
    ly = list(sc.layers)
    # the gun stands on the run and throws its plume up and away over the open sky in a high
    # arc; the snow drifts down onto the run below the far end of the arc
    p = PLUME_SPEC_T38
    gx, aim = p["gx"], p["aim"]
    gun, (mx, my), _ = snow_gun(gx, ry(gx) + p["below"], aim, p["gun"])
    pl, paths, jet, flakes = plume(mx, my, aim, p["spread"], p["v0"], p["g"], p["k"], p["wind"],
                                   p["r0"], p["grow"], p["cone"], p["fade"])
    if VERBOSE:
        apex = [min(paths[j], key=lambda q: q[1]) for j in range(5)]
        _log(f"    plume: muzzle ({mx:.0f},{my:.0f}); jet {len(jet)} disks, r {jet[0][2]:.1f} to "
             f"{jet[-1][2]:.1f}, ends at ({jet[-1][0]:.0f},{jet[-1][1]:.0f}); apexes "
             f"{[(round(a[0]), round(a[1])) for a in apex]}; landings "
             f"{[(round(q[-1][0]), round(q[-1][1])) for q in paths]}; {len(flakes)} flakes")
    return ly + pl + gun


def floodlight(snow, px, top):
    ground_y = ry(px)
    out = []
    cx, cy = px + 16, ry(px + 16)
    r = 60.0
    fall = 0.85 * np.clip(1.0 - np.hypot(X - cx, Y - cy) / r, 0.0, 1.0) ** 0.9
    out.append(L(inter(sd_disk(cx, cy, r), snow), LAMPLIGHT, fall))
    # a tapered steel mast (13 px, two-tone like the lift towers) standing on the slope
    mast = sd_polygon([(px - 5.5, top + 6), (px + 5.5, top + 6), (px + 7.5, ground_y + 5),
                       (px - 7.5, ground_y + 5)])
    out += two_tone(mast, left_of(px), STEEL_LIT, STEEL_SHADE)
    out.append(L(sd_round_rect(px - 22, top + 2, px + 22, top + 11, 3), STEEL_SHADE))
    for dx in (-18.0, 18.0):
        out.append(L(sd_round_rect(px + dx - 11, top - 6, px + dx + 11, top + 10, 4.0), HUB_AMBER))
        out.append(L(sd_round_rect(px + dx - 6, top + 3, px + dx + 6, top + 8, 2.0), (255, 246, 220)))
    return out


def twinkle(cx, cy, r):
    q = r * 0.28
    return sd_polygon([(cx, cy - r), (cx + q, cy - q), (cx + r, cy), (cx + q, cy + q),
                       (cx, cy + r), (cx - q, cy + q), (cx - r, cy), (cx - q, cy - q)])


NIGHT_MOON = (172.0, 86.0, 38.0)       # T38: the crescent is the tile's big shape
NIGHT_STARS = ((118.0, 50.0, 11.0), (214.0, 58.0, 9.0))
NIGHT_LAMP_X = 80.0


def icon_run_night():
    """T38: the family slope at night. Dark navy sky, the slope lit (a warm pool round the lamp on the
    dim snow), a big cream crescent over it, two stars and ONE floodlight mast with a big amber double lamp.
    No corduroy (the groomed tile owns the bands)."""
    sc = RunScene(night=True)
    ly = list(sc.layers)
    px = NIGHT_LAMP_X
    gy = ry(px)
    top = gy - 36.0
    pool = np.clip(1.0 - np.hypot((X - (px + 30)) / 1.6, Y - (ry(px + 30) + 14)) / 70.0, 0.0, 1.0) ** 0.8
    ly.append(L(inter(sd_ellipse(px + 30, ry(px + 30) + 14, 112, 70), sc.snow), LAMPLIGHT, 0.90 * pool,
                mark=False))
    mx, my, mr = NIGHT_MOON
    ly.append(L(minus(sd_disk(mx, my, mr), sd_disk(mx - 17, my - 13, mr * 0.86)), MOON))
    ly.append(L(union(*[twinkle(*t) for t in NIGHT_STARS]), MOON, 0.95))
    halo = np.clip(1.0 - np.hypot(X - px, Y - (top - 2)) / 54.0, 0.0, 1.0) ** 1.4
    ly.append(L(sd_disk(px, top - 2, 54), HUB_AMBER, 0.55 * halo, mark=False))
    mast = sd_polygon([(px - 6, top + 8), (px + 6, top + 8), (px + 8.5, gy + 5), (px - 8.5, gy + 5)])
    ly += two_tone(mast, left_of(px), STEEL_LIT, STEEL_SHADE)
    ly.append(L(sd_round_rect(px - 36, top + 2, px + 36, top + 14, 4), STEEL_SHADE))
    for dx in (-24.0, 24.0):
        ly.append(L(sd_round_rect(px + dx - 15, top - 14, px + dx + 15, top + 12, 6.0), HUB_AMBER))
        ly.append(L(sd_round_rect(px + dx - 9, top - 7, px + dx + 9, top + 5, 3.0), (255, 248, 226)))
    return ly


# T38: cat track = a level groomed ribbon crossing the slope (clipped to the snow, so it ends where it meets
# the skyline) with transverse tread marks, and a red snowcat with a push blade on it. The ribbon is the cat
# track's own surface (a traverse), not a road: no center dashes, no tarmac.
CAT_RIBBON_Y, CAT_RIBBON_W, CAT_K = 168.0, 50.0, 1.3
CAT_BAND, CAT_TREAD = (62, 140, 84), (30, 88, 52)


def icon_run_cattrack():
    sc = RunScene()
    ly = list(sc.layers)
    y0 = CAT_RIBBON_Y
    ribbon = inter(sd_round_rect(-30.0, y0 - CAT_RIBBON_W / 2, 300.0, y0 + CAT_RIBBON_W / 2, 0.0), sc.snow)
    ly.append(L(ribbon, CAT_BAND, mark=False))
    ly.append(L(inter(ribbon, sd_round_rect(-30.0, y0 - CAT_RIBBON_W / 2, 300.0, y0 - CAT_RIBBON_W / 2 + 5.0, 0.0)),
                (150, 214, 160), mark=False))
    ticks = union(*[sd_round_rect(x - 4.0, y0 - 11.0, x + 4.0, y0 + 13.0, 2.0) for x in np.arange(2.0, 270.0, 19.0)])
    ly.append(L(inter(ribbon, ticks), CAT_TREAD, mark=False))
    # the snowcat, facing right, on the ribbon, scaled by CAT_K about (110, ribbon center)
    k = CAT_K

    def sx(v):
        return 110.0 + (v - 110.0) * k

    def sy(v):
        return y0 + (v - y0) * k

    def rr(x0_, y0_, x1_, y1_, r):
        return sd_round_rect(sx(x0_), sy(y0_), sx(x1_), sy(y1_), r * k)

    def ur(x0_, y0_, x1_, y1_):
        return upper_side(sx(x0_), sy(y1_), sx(x1_), sy(y1_))

    ly.append(shadow(sx(112), sy(y0 + 20), 62 * k, 5 * k))
    ly.append(L(rr(58.0, y0 - 14.0, 146.0, y0 + 20.0, 15.0), STEEL_DARK))
    ly.append(L(union(*[sd_disk(sx(x), sy(y0 + 3.0), 6.0 * k) for x in (76.0, 102.0, 128.0)]), STEEL_LIT))
    ly += two_tone(rr(66.0, y0 - 54.0, 140.0, y0 - 10.0, 7.0), above(sy(y0 - 30.0)), ROOF_LIT, ROOF_SHADE)
    ly += two_tone(rr(96.0, y0 - 80.0, 136.0, y0 - 50.0, 6.0), above(sy(y0 - 66.0)), ROOF_LIT, ROOF_SHADE)
    ly.append(L(rr(104.0, y0 - 73.0, 130.0, y0 - 55.0, 3.0), GLASS_HI))
    blade = sd_polygon([(sx(146.0), sy(y0 - 30.0)), (sx(166.0), sy(y0 - 38.0)), (sx(170.0), sy(y0 + 16.0)),
                        (sx(150.0), sy(y0 + 18.0))])
    ly += two_tone(blade, above(sy(y0 - 8.0)), STEEL_LIT, STEEL_SHADE)
    return ly


# Safety fence (2026-10-26; the Runs tab's fence tile, prefab SkiSafetyFence, asset fences/A/SkiSafetyNetA): the
# orange net on dark posts set into the family slope, running along it (base = the skyline dropped by
# FENCE_DROP, posts plumb). The net is a solid orange band behind a diamond mesh: the strands are the lit tone,
# the cells the snow showing through an orange veil, so at 28 px it reads as one orange band with dark ticks.
FENCE_DROP, FENCE_H, FENCE_X = 44.0, 58.0, (36.0, 196.0)
FENCE_POSTS = (44.0, 82.0, 120.0, 158.0, 192.0)
FENCE_MESH, FENCE_STRAND = 11.0, 2.6
NET_LIT, NET_SHADE, NET_CORD = (246, 98, 38), (214, 70, 34), (176, 52, 30)
FPOST_LIT, FPOST_SHADE = (78, 86, 100), (40, 46, 58)


def icon_safety_fence():
    sc = RunScene()
    ly = list(sc.layers)
    x0, x1 = FENCE_X
    xs = np.linspace(x0, x1, 40).tolist()
    base = [(x, ry(x) + FENCE_DROP) for x in xs]
    top = [(x, y - FENCE_H) for x, y in base]
    net = sd_polygon(top + base[::-1])
    ly.append(L(sd_polygon([(x + 6.0, y - 2.0) for x, y in base] + [(x + 14.0, y + 7.0) for x, y in base[::-1]]),
                SHADOW, 0.30))
    ly.append(L(net, NET_SHADE, 0.55))
    p = FENCE_MESH
    fam = []
    for s in (X + Y, X - Y):
        m = np.mod(s, p)
        fam.append((p / 2.0 - np.abs(m - p / 2.0)) / np.sqrt(2.0) - FENCE_STRAND / 2.0)
    ly.append(L(inter(net, union(*fam)), NET_LIT))
    ly.append(L(sd_polyline(top, 4.5), NET_CORD))
    ly.append(L(sd_polyline(base, 3.0), NET_CORD))
    for px in FENCE_POSTS:
        by = ry(px) + FENCE_DROP
        post = sd_segment(px, by + 4.0, px, by - FENCE_H - 7.0, 6.5)
        ly += two_tone(post, left_of(px), FPOST_LIT, FPOST_SHADE)
    return ly

# ---------------------------------------------------------------------------------------------
# The hub launcher (SkiResortTab.png; not a Build-strip tile; Round F7 E1, 2026-10-04)
# ---------------------------------------------------------------------------------------------
LAUNCH_SUMMIT = (136.0, 36.0)
LAUNCH_LEFT = [(-14.0, 214.0), (36.0, 176.0), (78.0, 120.0), (110.0, 70.0)]
LAUNCH_RIGHT = [(160.0, 66.0), (190.0, 98.0), (220.0, 116.0), (270.0, 148.0)]
# cable ends p0 / p1 (each keeps the 22 px margin with the cable's half width), sag at mid-span,
# cable width; the chair's grip x on the cable and its scale (its footrest ends 22 px above the
# plate edge). Drafts A-I were compared at 256 / 33 px; this one keeps the chair largest.
LAUNCH_CABLE = dict(p0=(38.0, 142.0), p1=(216.0, 60.0), sag=8.0, w=11.0, chair_x=114.0, k=1.08)


def _cable_y(x, p0, p1, sag):
    t = (x - p0[0]) / (p1[0] - p0[0])
    return p0[1] + (p1[1] - p0[1]) * t + sag * 4.0 * t * (1.0 - t)


def icon_launcher():
    """The hub launcher: one snowy peak fills the plate (lit left face, blue-gray shade face right
    of a ridge line, two rock wedges under the summit, a paler range behind it on the right) and a
    red chair hangs from a diagonal cable that rises to the right across it. Drawn to read at the
    launcher's ~33 px: the white peak against the dark sky top, the cable as one dark line, the
    chair as the only warm color. The cable ends in the open sky on both sides (no towers: a
    tower at 33 px is a gray stub that crowds the chair - draft D)."""
    ly = [sky()]
    ly += far_range([(120, 210), (150, 120), (196, 74), (232, 104), (270, 96)])
    sx, sy = LAUNCH_SUMMIT
    body = sd_polygon(LAUNCH_LEFT + [LAUNCH_SUMMIT] + LAUNCH_RIGHT + [(270.0, 300.0),
                                                                      (-14.0, 300.0)])
    ly.append(L(body, vgrad(SNOW_HI, SNOW_LO, 40, 245), mark=False))
    ridge = [(sx + 10, sy + 44), (sx + 4, sy + 96), (sx + 30, 300.0)]
    shade = sd_polygon([LAUNCH_SUMMIT] + LAUNCH_RIGHT + [(270.0, 300.0)] + ridge[::-1])
    ly.append(L(inter(body, shade), PEAK_SHADE, mark=False))
    rock = union(sd_polygon([(sx + 1, sy + 6), (sx + 8, sy + 40), (sx + 20, sy + 26)]),
                 sd_polygon([(sx + 26, sy + 30), (sx + 22, sy + 52), (sx + 38, sy + 44)]))
    ly.append(L(inter(body, rock), PEAK_ROCK, mark=False))
    c = LAUNCH_CABLE
    p0, p1, sag = c["p0"], c["p1"], c["sag"]
    n = 24
    cable = []
    for i in range(n + 1):
        x = p0[0] + (p1[0] - p0[0]) * i / n
        cable.append((x, _cable_y(x, p0, p1, sag)))
    ly.append(L(sd_polyline(cable, c["w"]), STEEL_DARK))
    gx = c["chair_x"]
    ly += chair(gx, _cable_y(gx, p0, p1, sag), c["k"])
    return ly


# ---------------------------------------------------------------------------------------------
# Round 2 tiles (2026-10-11): the 16 tiles that contract 6 left as placeholders
# (docs/research/art/assets-2026-10/code-round-2/round-2-contract.md section 8.8; the
# verdict B2, docs/research/art/assets-2026-10/verdicts-2026-10-11.md: "fit the style of the
# ones we have already decided on and like"). The lodge tiers (mountain hut, chalet, summit,
# grand), the ski school, the patrol hut and HQ, and base / top / line for the detachable,
# gondola and tram lift types. Same plate, mark margin, step-digit corner, finish and palette
# rules as the nine; red stays the one accent color (roofs, terminal housings, chairs and
# cabins, the school's flag, the patrol disc). Each picture follows its asset's previews
# (assets/preview/lodges, buildings, stations, cabins, chairs). Build record:
# docs/research/art/assets-2026-10/build/icons-round-2.md.
# ---------------------------------------------------------------------------------------------
STONE_LIT, STONE_SHADE = (180, 174, 166), (138, 132, 126)   # plinths and stone bases
STONE_JOINT = (150, 144, 138)
ROCK_LIT, ROCK_SHADE = (150, 158, 178), (110, 118, 142)     # the summit crag
TIMBER_LIT, TIMBER_SHADE = (124, 94, 72), (92, 68, 52)      # dark-stained timber (patrol huts)
SLATE_LIT, SLATE_SHADE = (104, 150, 162), (66, 108, 124)    # patrol roofs (their teal metal)
CAB_GLASS, CAB_GLASS_HI = (40, 54, 80), (84, 112, 152)      # cabin windows
BRICK_LIT, BRICK_SHADE = (152, 104, 82), (114, 78, 62)      # the tram hall's clock tower
CROSS_RED = (214, 52, 48)                                   # the patrol disc
FACE = (246, 248, 250)                                      # clock faces, the white cross
DECK = (190, 152, 108)                                      # timber decks
DQ_SEP, GD_SEP, TRAM_SEP = 18.0, 14.0, 10.0                 # strand spacing per lift type


def right_of(xs):
    """Half plane x > xs."""
    return xs - X


def box(x0, y0, x1, y1, r=0.0):
    return sd_round_rect(x0, y0, x1, y1, r)


def warm_window(x0, y0, x1, y1, r=2.5):
    return L(box(x0, y0, x1, y1, r), vgrad(WARM_HI, WARM, y0, y1))


def glass_window(x0, y0, x1, y1, r=2.5, col=GLASS, hi=GLASS_HI):
    return [L(box(x0, y0, x1, y1, r), col), L(box(x0, y0, x1, y0 + 0.42 * (y1 - y0), r), hi)]


def flat_snow(x0, x1, y, lumps=((0.28, 6.0), (0.70, 5.0))):
    """Snow on a flat top from x0 to x1 (the surface at y): a slab with a few lumps, the same
    recipe as the terminal hood's."""
    parts = [box(x0, y - 7, x1, y + 4, 5)]
    for f, r in lumps:
        parts.append(sd_disk(x0 + f * (x1 - x0), y - 5, r))
    return L(union(*parts), SNOW_HI)


def stone_courses(region, y0, row=11.0, block=22.0, w=2.2):
    """Mortar joints over a stone face (only inside `region`): level courses every `row` px from y0
    and staggered head joints. Thin, so they melt into the stone tone at strip size."""
    yy = (Y - y0) / row
    course = np.abs(yy - np.rint(yy)) * row - w / 2
    m = np.mod(X + np.mod(np.floor(yy), 2.0) * block / 2, block)
    head = np.minimum(m, block - m) - w / 2
    return L(inter(region, union(course, head)), STONE_JOINT)


def grounded(x0, x1, top, ground_y, sink=3.0):
    """A face from y = top down to the ground: its bottom edge runs from the ground at x0 to the
    ground at x1 (+ sink), so a building on a slope never floats."""
    return sd_polygon([(x0, top), (x1, top), (x1, ground_y(x1) + sink), (x0, ground_y(x0) + sink)])


# --- roofs -----------------------------------------------------------------------------------
def _down_offset(p, q, d):
    """The line p-q moved d along its normal that points down the screen (into the building)."""
    dx, dy = q[0] - p[0], q[1] - p[1]
    ln = np.hypot(dx, dy)
    nx, ny = -dy / ln, dx / ln
    if ny < 0:
        nx, ny = -nx, -ny
    return (p[0] + nx * d, p[1] + ny * d), (q[0] + nx * d, q[1] + ny * d)


def _meet(p1, q1, p2, q2):
    """Intersection of the lines p1-q1 and p2-q2."""
    d1x, d1y = q1[0] - p1[0], q1[1] - p1[1]
    d2x, d2y = q2[0] - p2[0], q2[1] - p2[1]
    t = ((p2[0] - p1[0]) * d2y - (p2[1] - p1[1]) * d2x) / (d1x * d2y - d1y * d2x)
    return p1[0] + t * d1x, p1[1] + t * d1y


def _x_at(p, q, y):
    return p[0] + (q[0] - p[0]) * (y - p[1]) / (q[1] - p[1])


def roof_line(le, apex, re, d):
    """The gable roof line le-apex-re moved d into the building; its ends stay on the level lines
    through le and re (the eave ends are cut level)."""
    lp, lq = _down_offset(le, apex, d)
    rp, rq = _down_offset(apex, re, d)
    a = _meet(lp, lq, rp, rq)
    return (_x_at(lp, lq, le[1]), le[1]), a, (_x_at(rp, rq, re[1]), re[1])


def gable(le, apex, re, snow=9.0, band=14.0, lit=ROOF_LIT, shade=ROOF_SHADE):
    """A gable seen end-on (le / re = the eave ends, apex = the ridge; a saltbox when the two eaves
    differ): a snow edge on top of a roof band, both lit left of the ridge (the A-frame's
    recipe). Returns (layers, under): `under` = everything below the band's inner edge (clip it to
    the walls for the gable wall; draw the walls before the layers)."""
    p1 = roof_line(le, apex, re, snow)
    p2 = roof_line(le, apex, re, snow + band)
    sn = sd_polygon([le, apex, re, p1[2], p1[1], p1[0]])
    bd = sd_polygon([p1[0], p1[1], p1[2], p2[2], p2[1], p2[0]])
    under = sd_polygon([p2[0], p2[1], p2[2], (p2[2][0], 300.0), (p2[0][0], 300.0)])
    split = left_of(apex[0])
    return two_tone(bd, split, lit, shade) + two_tone(sn, split, SNOW_HI, SNOW_SHADE), under


def roof_side(x0, x1, ey, ry, inset, snow_to=0.45, fascia=7.0, lit=ROOF_LIT, shade=ROOF_SHADE):
    """A pitched roof seen from its eaves side: eaves x0..x1 at ey, the ridge inset at ry (hipped
    ends); snow over its upper part (a gently wavy lower edge), a darker fascia along the eave."""
    poly = sd_polygon([(x0, ey), (x1, ey), (x1 - inset, ry), (x0 + inset, ry)])
    sy = ry + (ey - ry) * snow_to
    wave = Y - (sy + 3.0 * np.sin((X - x0) / 10.0))
    return [L(poly, lit), L(inter(poly, below(ey - fascia)), shade), L(inter(poly, wave), SNOW_HI)]


def gable_wall(under, x0, x1, eave, bottom):
    """The wall face of a gable-front building: the rectangle x0..x1 from the eave line down plus
    the gable triangle above it (under the roof band)."""
    return union(box(x0, eave, x1, bottom), inter(under, box(x0, -50.0, x1, eave + 1.0)))


def chimney(x0, x1, top, bottom, col=STONE_LIT, shade=STONE_SHADE, cap=True):
    """A chimney stack (drawn before the roof that hides its foot), lit left, snow on its cap."""
    body = box(x0, top, x1, bottom, 1.5)
    out = two_tone(body, left_of(x0 + 0.55 * (x1 - x0)), col, shade)
    if cap:
        out.append(L(union(box(x0 - 3, top - 5, x1 + 3, top + 3, 3)), SNOW_HI))
    return out


def _slope(y_left, y_right, p=1.15):
    """Ground that rises (or falls) steadily across the plate: y at x = -14 and at x = 270."""
    def f(x):
        t = np.clip((np.asarray(x, np.float64) + 14.0) / 284.0, 0.0, 1.0)
        return y_left + (y_right - y_left) * t ** p
    return f


def _flat(y, a=5.0):
    return lambda x: y - a * np.sin(np.pi * (x + 14.0) / 284.0)


# --- lodge tiers -----------------------------------------------------------------------------
HUT_GROUND = _slope(214.0, 148.0)


def icon_lodge_hut():
    """Mountain hut (L2, SkiLodgeHut): a small chalet set into a hillside on a stone plinth that is
    tall on the downhill side (the asset is made for slopes: verdict B4), its gable to the front:
    snowy red roof with deep eaves, timber walls, two warm windows with shutters, the door, a
    gable window and a stovepipe; a pine up the slope behind."""
    gy = HUT_GROUND
    ly = [sky()]
    ly += far_range([(-14, 150), (36, 114), (86, 146), (146, 102), (200, 136), (270, 112)])
    gsdf, g = ground(gy, 140)
    ly.append(g)
    ly.append(L(inter(gsdf, sd_polygon([(150, 300), (270, 120), (300, 120), (300, 300)])),
                SNOW_SHADE, 0.45, mark=False))
    ly += pine(207.0, float(gy(207.0)) + 4, 66.0, 13.0, PINE2_LIT, PINE2_SHADE)
    x0, x1, eave, sill = 74.0, 182.0, 122.0, 160.0
    ly.append(shadow(128, float(gy(128)) + 4, 64, 6))
    pl = sd_polygon([(x0 - 6, sill), (x1 + 6, sill), (x1 + 6, float(gy(x1 + 6)) + 5),
                     (x0 - 6, float(gy(x0 - 6)) + 5)])
    ly.append(L(pl, STONE_LIT))
    ly.append(stone_courses(pl, sill))
    roof, under = gable((44.0, 126.0), (128.0, 56.0), (212.0, 126.0), snow=10.0, band=14.0)
    ly += chimney(158.0, 170.0, 66.0, 100.0, STEEL_SHADE, STEEL_DARK)
    wall = gable_wall(under, x0, x1, eave, sill)
    ly.append(L(wall, WOOD_LIT))
    ly.append(L(box(x0, sill - 6, x1, sill + 1), WOOD_SHADE))          # sill beam
    for wx in (86.0, 152.0):
        ly.append(L(box(wx - 7, 128, wx + 25, 150, 2), TIMBER_SHADE))  # shutters
        ly.append(warm_window(wx, 128, wx + 18, 150))
    ly.append(L(box(117, 128, 139, 160, 3), WOOD_DARK))
    ly.append(L(box(122, 133, 134, 143, 2), WARM))
    ly.append(warm_window(119, 88, 137, 106))
    ly += roof
    return ly


def icon_lodge_mid():
    """Chalet lodge (L3, SkiLodgeMid): two masses - a taller wing behind with its gable to the
    front, the long dining hall in front (stone ground floor with glass doors, timber upper floor
    with a row of warm windows, a snowy red roof) - and the gazebo on the terrace to the right."""
    gfn = _flat(200.0)
    ly = [sky()]
    ly += far_range([(-14, 156), (40, 118), (96, 152), (150, 112), (204, 144), (270, 120)])
    _, g = ground(gfn, 180)
    ly.append(g)
    ly.append(shadow(126, 205, 88, 7))
    # the taller wing (gable front, behind on the left)
    wx0, wx1 = 46.0, 116.0
    wroof, wunder = gable((34.0, 108.0), (81.0, 50.0), (128.0, 108.0), snow=9.0, band=13.0)
    ly.append(L(gable_wall(wunder, wx0, wx1, 104.0, 200.0), WOOD_LIT))
    ly.append(warm_window(72, 78, 90, 98))
    ly.append(warm_window(54, 112, 66, 128))
    ly += wroof
    # the dining hall (eaves to the front)
    hx0, hx1 = 64.0, 168.0
    hall = box(hx0, 132, hx1, 201)
    ly.append(L(hall, WOOD_LIT))
    stone = box(hx0, 166, hx1, 201)
    ly.append(L(stone, STONE_LIT))
    ly.append(stone_courses(stone, 166.0))
    for k in range(4):
        x = 74.0 + 24.0 * k
        ly.append(warm_window(x, 140, x + 14, 158))
    ly += glass_window(84, 174, 104, 201, 2)
    ly += glass_window(128, 174, 148, 201, 2)
    ly += roof_side(54.0, 178.0, 136.0, 100.0, 24.0)
    # the gazebo on the terrace
    ly.append(L(box(170, 190, 218, 198, 2), DECK))
    ly.append(L(box(170, 196, 218, 201, 1), STONE_SHADE))
    for px in (180.0, 208.0):
        ly.append(L(box(px - 3.5, 152, px + 3.5, 192, 1.5), WOOD_DARK))
    ly.append(L(box(176, 172, 212, 177, 1.5), WOOD_DARK))
    groof = sd_polygon([(194.0, 112.0), (220.0, 154.0), (168.0, 154.0)])
    ly += two_tone(groof, left_of(194.0), ROOF_LIT, ROOF_SHADE)
    ly += two_tone(inter(groof, above(128.0)), left_of(194.0), SNOW_HI, SNOW_SHADE)
    return ly


SUMMIT_CRAG = [(-14.0, 222.0), (12.0, 200.0), (28.0, 206.0), (48.0, 188.0), (62.0, 182.0),
               (212.0, 182.0), (222.0, 192.0), (236.0, 186.0), (270.0, 208.0)]


def icon_lodge_summit():
    """Summit lodge (L4, SkiLodgeSummit): stepped glass and stone on a crag - the glass dining room
    (flat snowy roof, thin dark mullions) cantilevered low on the left over a stone pier and a
    strut, the stone house above it on the right with its gable to the front and a tall stone
    chimney; the gray crag below (lit and shaded rock faces, snow on its ledges), the ranges low
    behind (it is the top of the hill)."""
    ly = [sky()]
    ly += far_range([(-14, 196), (30, 172), (76, 192), (128, 160), (178, 188), (226, 166),
                     (270, 190)])
    crag = sd_polygon(SUMMIT_CRAG + [(270.0, 300.0), (-14.0, 300.0)])
    ly.append(L(crag, ROCK_LIT, mark=False))
    for poly in ([(62, 182), (104, 182), (84, 216), (40, 300), (10, 300)],
                 [(150, 182), (212, 182), (222, 192), (236, 186), (270, 208), (270, 300),
                  (170, 300), (178, 222)]):
        ly.append(L(inter(crag, sd_polygon(poly)), ROCK_SHADE, mark=False))
    ledges = union(sd_polygon([(44, 190), (62, 182), (212, 182), (204, 192), (120, 196),
                               (70, 192)]),
                   sd_polygon([(4, 208), (12, 200), (28, 206), (20, 212)]),
                   sd_polygon([(96, 214), (124, 206), (142, 214), (112, 220)]),
                   sd_polygon([(222, 192), (236, 186), (252, 194), (234, 198)]),
                   sd_polygon([(170, 222), (196, 214), (208, 222), (184, 226)]))
    ly.append(L(inter(crag, ledges), SNOW_HI, mark=False))
    # the stone house (right, higher)
    hx0, hx1 = 130.0, 206.0
    roof, under = gable((114.0, 110.0), (168.0, 46.0), (222.0, 110.0), snow=9.0, band=13.0)
    ly += chimney(184.0, 200.0, 38.0, 90.0)
    house = box(hx0, 106, hx1, 186)
    ly.append(L(house, STONE_LIT))
    ly.append(stone_courses(house, 106.0))
    ly.append(L(inter(under, box(hx0, -50, hx1, 108)), WOOD_LIT))
    ly.append(warm_window(158, 76, 178, 96))
    ly.append(warm_window(142, 124, 158, 148))
    ly.append(warm_window(178, 124, 194, 148))
    ly += roof
    # the glass dining room (left, lower), on a stone pier with a strut
    ly.append(L(box(88, 158, 132, 188), STONE_SHADE))
    ly.append(L(sd_segment(58, 160, 92, 186, 7.0), WOOD_SHADE))
    gx0, gx1, gt, gb = 42.0, 136.0, 116.0, 158.0
    ly.append(L(box(gx0, gt, gx1, gb), vgrad(GLASS_HI, GLASS, gt, gb)))
    ly.append(L(sd_polygon([(gx0 + 10, gb), (gx0 + 40, gt), (gx0 + 54, gt), (gx0 + 24, gb)]),
                (150, 190, 226), 0.55))
    ly.append(L(union(*[sd_segment(gx0 + f * (gx1 - gx0), gt, gx0 + f * (gx1 - gx0), gb, 3.0)
                        for f in (0.25, 0.5, 0.75)]), STEEL_DARK))
    ly.append(L(box(gx0 - 2, gb - 1, gx1, gb + 6, 1), STEEL_DARK))
    ly.append(L(box(gx0 - 4, gt - 8, gx1 + 2, gt + 1, 2), STEEL_SHADE))
    ly.append(flat_snow(gx0 - 4, gx1 + 2, gt - 8, lumps=((0.22, 5.0), (0.62, 4.5))))
    return ly


# L5 revision 1 (verdict B1, 2026-10-11): the pool terrace's water and pavers
POOL_LIT, POOL_SHADE = (126, 216, 228), (62, 168, 200)
PAVER = (214, 198, 180)
GRAND_GROUND = _flat(208.0, 3.0)


def _grand_wing(x0, x1, top, eave, gy):
    """One wing of the U seen end-on (its king-post gable faces the slope): timber over a stone
    ground floor, a balcony stack (warm doors, honey rails on dark slab edges) on the axis, a tall
    warm window each side, a glass door; the snowy red gable roof overhangs the walls by 6.
    Returns layers."""
    cx = 0.5 * (x0 + x1)
    roof, under = gable((x0 - 6.0, eave + 4.0), (cx, top), (x1 + 6.0, eave + 4.0), snow=7.0,
                        band=11.0)
    bottom = gy + 1.0
    ly = [L(gable_wall(under, x0, x1, eave, bottom), WOOD_LIT)]
    stone = box(x0, 186, x1, bottom)
    ly.append(L(stone, STONE_LIT))
    ly.append(stone_courses(stone, 186.0, row=9.0, block=16.0))
    ly.append(L(inter(under, union(sd_segment(x0, eave + 1, x1, eave + 1, 4.0),
                                   sd_segment(cx, top, cx, eave, 4.0))), WOOD_DARK))
    ly.append(L(box(cx - 8, eave + 6, cx + 8, 186), vgrad(WARM_HI, WARM, eave + 6, 186)))
    for y in (eave + 16.0, eave + 32.0):
        ly.append(L(box(cx - 15, y + 4, cx + 15, y + 7, 1.0), WOOD_DARK))
        ly.append(L(box(cx - 15, y - 1, cx + 15, y + 5, 1.5), DECK))
    for wx in (x0 + 5.0, x1 - 14.0):
        ly.append(warm_window(wx, eave + 10, wx + 9, eave + 34, 2.0))
    ly += glass_window(cx - 7, 192, cx + 7, bottom, 1.5)
    return ly + roof


def icon_lodge_grand():
    """Grand lodge (L5, SkiLodgeGrand, the swanky base lodge after Breckenridge's Grand Lodge on
    Peak 7), revision 1 (verdict B1, 2026-10-11: the 10 x 8 U-plan) seen from the slope side: the
    two wings come forward with their king-post gables and balcony stacks, the main bar sits
    behind them with a cross gable over its own balcony stack, the long snowy roof behind with two
    stone chimneys and the clock tower (white clock, red spire) rising over it; the pool terrace
    fills the courtyard between the wings (paver deck on a stone wall, a turquoise lagoon pool and
    a hot tub). Stepped gables: wings in front lowest, the cross gable, the ridge, the tower."""
    gy = GRAND_GROUND
    gfront = float(gy(128.0))
    ly = [sky()]
    ly += far_range([(-14, 150), (40, 112), (94, 148), (150, 106), (206, 140), (270, 116)])
    _, g = ground(gy, 184)
    ly.append(g)
    ly.append(shadow(128, gfront + 2, 90, 7))
    # the clock tower (on the road side, behind the main bar): shaft, clock and red spire
    tx = 98.0
    shaft = box(tx - 11, 52, tx + 11, 92)
    ly += two_tone(shaft, left_of(tx + 3), WOOD_LIT, WOOD_SHADE)
    ly.append(L(sd_disk(tx, 68, 8.0), FACE))
    ly.append(L(union(sd_segment(tx, 68, tx, 62, 2.6), sd_segment(tx, 68, tx + 4.5, 70, 2.6)),
                STEEL_DARK))
    ly.append(L(box(tx - 14, 51, tx + 14, 56, 1.5), WOOD_DARK))
    spire = sd_polygon([(tx, 33), (tx + 16, 54), (tx - 16, 54)])
    ly += two_tone(spire, left_of(tx), ROOF_LIT, ROOF_SHADE)
    ly += two_tone(inter(spire, above(39)), left_of(tx), SNOW_HI, SNOW_SHADE)
    # two ridge stacks
    ly += chimney(150.0, 162.0, 54.0, 96.0)
    ly += chimney(184.0, 196.0, 60.0, 100.0)
    # the main bar (behind): walls, then its long roof
    ly.append(L(box(88, 108, 167, 180), WOOD_LIT))
    ly += roof_side(36.0, 220.0, 118.0, 78.0, 24.0, snow_to=0.32)
    mstone = box(88, 160, 167, 180)
    ly.append(L(mstone, STONE_LIT))
    ly.append(stone_courses(mstone, 160.0, row=9.0, block=16.0))
    for wx in (95.0, 150.0):
        ly.append(warm_window(wx, 128, wx + 10, 150, 2.0))
        ly += glass_window(wx - 1, 164, wx + 11, 178, 1.5)
    # the cross gable over the main bar's balcony stack
    croof, cunder = gable((106.0, 128.0), (128.0, 92.0), (150.0, 128.0), snow=7.0, band=10.0)
    ly.append(L(gable_wall(cunder, 113, 143, 124, 180), WOOD_LIT))
    ly.append(L(inter(cunder, union(sd_segment(113, 125, 143, 125, 4.0),
                                    sd_segment(128, 92, 128, 124, 4.0))), WOOD_DARK))
    ly.append(L(box(121, 130, 135, 160), vgrad(WARM_HI, WARM, 130, 160)))
    for y in (140.0, 154.0):
        ly.append(L(box(114, y + 4, 142, y + 7, 1.0), WOOD_DARK))
        ly.append(L(box(114, y - 1, 142, y + 5, 1.5), DECK))
    ly += glass_window(117, 164, 139, 180, 1.5)
    ly += croof
    # the left wing's shadow on the recessed facade (light from the upper left)
    ly.append(L(box(92, 118, 104, 180), WOOD_SHADE, 0.5))
    # the pool terrace between the wings: paver deck on a stone retaining wall
    ly.append(L(box(92, 174, 163, 199), PAVER))
    ly.append(L(box(92, 174, 104, 199), STONE_SHADE, 0.35))
    lagoon = union(sd_ellipse(120, 186, 18, 6.5), sd_ellipse(139, 187, 12, 5.5))
    ly.append(L(lagoon - 2.5, FACE))
    ly.append(L(lagoon, vgrad(POOL_LIT, POOL_SHADE, 180, 193)))
    ly.append(L(inter(lagoon, sd_segment(110, 183.5, 126, 183.5, 2.0)), FACE, 0.75))
    tub = sd_disk(152, 182, 4.5)
    ly.append(L(tub - 2.0, FACE))
    ly.append(L(tub, POOL_SHADE))
    ly.append(L(box(92, 196, 163, 200, 1.0), FACE))
    wall = box(92, 199, 163, gfront + 1)
    ly.append(L(wall, STONE_LIT))
    ly.append(stone_courses(wall, 199.0, row=7.0, block=14.0))
    # the two wings (in front)
    ly += _grand_wing(39.0, 93.0, 100.0, 138.0, gfront)
    ly += _grand_wing(162.0, 216.0, 100.0, 138.0, gfront)
    return ly


# --- school and patrol -----------------------------------------------------------------------
def icon_school():
    """Ski school (SkiSchool): the timber school house with flat snowy roofs - a low wing, the
    tower with the open lantern on top and the skier sign on its face, a two-story wing - a red
    flag over the lantern, and in front a child riding the magic carpet (the static conveyor
    beside the school, verdict B3)."""
    gfn = _flat(200.0, 4.0)
    ly = [sky()]
    ly += far_range([(-14, 152), (34, 120), (88, 150), (140, 110), (196, 146), (270, 118)])
    _, g = ground(gfn, 180)
    ly.append(g)
    ly.append(shadow(140, 205, 80, 6))
    # two-story wing (right)
    rw = box(142, 118, 214, 201)
    ly += two_tone(rw, left_of(196), WOOD_LIT, WOOD_SHADE)
    for row in (128.0, 160.0):
        for x in (150.0, 174.0):
            ly += glass_window(x, row, x + 14, row + 18, 2)
    ly.append(L(box(196, 166, 210, 201, 1), GLASS))
    ly.append(L(box(138, 110, 218, 120, 2), STEEL_SHADE))
    ly.append(flat_snow(138, 218, 110))
    # low wing (left)
    lw = box(70, 156, 104, 201)
    ly.append(L(lw, WOOD_LIT))
    ly += glass_window(78, 166, 96, 184, 2)
    ly.append(L(box(66, 148, 108, 158, 2), STEEL_SHADE))
    ly.append(flat_snow(66, 108, 148, lumps=((0.5, 5.0),)))
    # the tower, the lantern, the flag
    tw = box(100, 88, 144, 201)
    ly += two_tone(tw, left_of(130), WOOD_LIT, WOOD_SHADE)
    ly.append(L(box(94, 80, 150, 90, 2), STEEL_SHADE))
    ly.append(flat_snow(94, 150, 80, lumps=()))
    for px in (106.0, 138.0):
        ly.append(L(box(px - 3.5, 58, px + 3.5, 76, 1), FACE))
    ly.append(L(box(100, 54, 144, 62, 2), STEEL_SHADE))
    ly.append(L(sd_polygon([(98, 56), (146, 56), (132, 44), (112, 44)]), SNOW_HI))
    ly.append(L(sd_segment(122, 46, 122, 36, 3.5), STEEL_DARK))
    flag = sd_polygon([(124, 34), (150, 41), (124, 48)])
    ly += two_tone(flag, above(41), ROOF_LIT, ROOF_SHADE)
    # the school sign on the tower (a white plate with a skier)
    ly.append(L(box(108, 104, 136, 140, 4), FACE))
    ly.append(L(union(sd_disk(125, 112, 3.6),
                      sd_polyline([(123, 117), (117, 126), (123, 131)], 4.0),
                      sd_segment(112, 135, 132, 130, 3.0)), WOOD_SHADE))
    ly.append(L(box(114, 170, 130, 201, 1.5), FACE))
    # the magic carpet and a child on it
    ly.append(L(sd_segment(46, 212, 112, 202, 8.0), STEEL_DARK))
    ly.append(L(sd_segment(46, 207, 112, 197, 3.0), STEEL_SHADE))
    cx = 60.0
    ly.append(L(sd_segment(cx - 13, 207, cx + 13, 203, 4.0), STEEL_DARK))
    ly.append(L(sd_polyline([(cx - 3, 204), (cx - 1, 192)], 6.0), STEEL_DARK))
    ly.append(L(sd_polyline([(cx + 4, 203), (cx + 2, 192)], 6.0), STEEL_DARK))
    body = sd_polyline([(cx + 1, 192), (cx + 2, 178)], 11.0)
    ly += two_tone(body, left_of(cx + 2), ROOF_LIT, ROOF_SHADE)
    ly.append(L(sd_disk(cx + 2, 170, 6.5), STEEL_DARK))
    return ly


def patrol_disc(cx, cy, r):
    """The ski-patrol sign: a red disc in a white ring with a white cross."""
    a = r * 0.62
    w = r * 0.30
    cross = union(box(cx - a, cy - w / 2, cx + a, cy + w / 2, 1.0),
                  box(cx - w / 2, cy - a, cx + w / 2, cy + a, 1.0))
    return [L(sd_disk(cx, cy, r + 3.5), FACE), L(sd_disk(cx, cy, r), CROSS_RED), L(cross, FACE)]


def icon_patrol_hut():
    """Ski patrol hut (SkiPatrolHut): a small dark-timber hut, eaves to the front, a snowy teal
    metal roof with a stovepipe, the door with its lamp, a window, and the big patrol disc (white
    cross on red) on the wall - the tile's only red."""
    gfn = _flat(202.0, 4.0)
    ly = [sky()]
    ly += far_range([(-14, 150), (44, 112), (98, 150), (156, 108), (210, 142), (270, 118)])
    _, g = ground(gfn, 180)
    ly.append(g)
    ly.append(shadow(128, 207, 82, 6))
    ly += chimney(170.0, 180.0, 68.0, 110.0, STEEL_SHADE, STEEL_DARK)
    hut = box(54, 126, 202, 204)
    ly += two_tone(hut, left_of(184), TIMBER_LIT, TIMBER_SHADE)
    ly.append(L(inter(hut, union(*[np.abs(X - x) - 1.2 for x in range(66, 200, 14)])),
                TIMBER_SHADE))
    ly.append(L(box(64, 146, 88, 204, 2), SLATE_SHADE))
    ly.append(L(box(68, 132, 84, 141, 2), WARM))
    ly.append(warm_window(168, 150, 192, 174))
    ly += roof_side(42.0, 214.0, 130.0, 92.0, 22.0, snow_to=0.40, lit=SLATE_LIT, shade=SLATE_SHADE)
    ly += patrol_disc(130.0, 166.0, 22.0)
    return ly


def icon_patrol_hq():
    """Ski patrol HQ (SkiPatrolHQ): the bigger patrol building on posts with its deck, the saltbox
    roof (short steep slope left, long low slope right; teal metal, snow on top), the big gridded
    window, the door, and the patrol disc high in the gable."""
    gfn = _flat(206.0, 4.0)
    ly = [sky()]
    ly += far_range([(-14, 156), (36, 118), (92, 152), (150, 110), (206, 146), (270, 120)])
    _, g = ground(gfn, 190)
    ly.append(g)
    ly.append(shadow(128, 209, 90, 6))
    for px in (54.0, 102.0, 152.0, 200.0):
        ly.append(L(box(px - 3, 186, px + 3, 206), WOOD_DARK))
        ly.append(L(box(px - 6, 202, px + 6, 210, 1.5), STONE_LIT))
    x0, x1 = 58.0, 200.0
    roof, under = gable((44.0, 110.0), (100.0, 52.0), (214.0, 152.0), snow=9.0, band=13.0,
                        lit=SLATE_LIT, shade=SLATE_SHADE)
    wall = inter(under, box(x0, -50, x1, 186))
    ly += two_tone(wall, left_of(184), TIMBER_LIT, TIMBER_SHADE)
    ly.append(L(inter(wall, union(*[np.abs(X - x) - 1.2 for x in range(70, 200, 14)])),
                TIMBER_SHADE))
    ly.append(L(box(64, 124, 82, 178, 2), SLATE_SHADE))
    ly.append(L(box(67, 116, 79, 122, 2), WARM))
    gx0, gx1, gt, gb = 96.0, 172.0, 128.0, 172.0
    ly.append(L(box(gx0 - 4, gt - 4, gx1 + 4, gb + 4, 2), SLATE_SHADE))
    ly.append(L(box(gx0, gt, gx1, gb), GLASS))
    ly.append(L(box(gx0, gt, gx1, gt + 14), GLASS_HI))
    ly.append(L(union(sd_segment(gx0 + 25, gt, gx0 + 25, gb, 4.0),
                      sd_segment(gx0 + 51, gt, gx0 + 51, gb, 4.0),
                      sd_segment(gx0, gt + 22, gx1, gt + 22, 4.0)), SLATE_SHADE))
    ly += roof
    ly.append(L(box(40, 182, 216, 190, 1.5), DECK))
    ly += patrol_disc(100.0, 96.0, 17.0)
    return ly


OXBLOOD_LIT, OXBLOOD_SHADE = (138, 46, 46), (98, 30, 34)      # the depot's steel and its coach
CORTEN_LIT, CORTEN_SHADE = (170, 92, 58), (126, 64, 44)       # the depot's sign tower
SIGN_DARK, SIGN_RED = (40, 46, 58), (240, 92, 78)


def icon_bus_depot():
    """Ski Bus Depot (SkiBusDepot, art SkiBusDepotA): the corten tower with its dark sign board (red blocks for the
    letters, no text in a tile), the oxblood shed roof over the glass and cedar back wall, and the coach at the curb,
    which is what tells this tile from the lodges at strip size."""
    gfn = _flat(204.0, 4.0)
    ly = [sky()]
    ly += far_range([(-14, 150), (52, 112), (112, 146), (172, 104), (226, 138), (270, 116)])
    _, g = ground(gfn, 184)
    ly.append(g)
    ly.append(shadow(128, 208, 84, 6))
    # the shelter behind the coach
    ly.append(L(box(84, 96, 216, 170), WOOD_LIT))
    ly.append(L(inter(box(84, 96, 216, 170), union(*[np.abs(Y - y) - 1.2 for y in range(104, 170, 12)])), WOOD_SHADE))
    ly += glass_window(84, 100, 132, 170, 0.0)
    for px in (84.0, 132.0, 214.0):
        ly.append(L(box(px - 3.5, 92, px + 3.5, 170), OXBLOOD_SHADE))
    roof = sd_polygon([(70.0, 80.0), (224.0, 92.0), (224.0, 103.0), (70.0, 92.0)])
    ly.append(L(sd_polygon([(70.0, 73.0), (222.0, 85.0), (224.0, 94.0), (70.0, 82.0)]), SNOW_HI))
    ly += two_tone(roof, left_of(150), OXBLOOD_LIT, OXBLOOD_SHADE)
    # the tower and its sign board
    ly += two_tone(box(38, 40, 76, 204), left_of(60), CORTEN_LIT, CORTEN_SHADE)
    ly.append(L(box(43, 58, 71, 122, 2), SIGN_DARK))
    for ty in (68.0, 95.0):                         # two rows of three letter blocks: SKI, BUS
        for tx in (46.0, 54.0, 62.0):
            ly.append(L(box(tx, ty, tx + 6.0, ty + 18.0, 1), SIGN_RED))
    # the coach at the curb
    bus = box(86, 134, 224, 196, 8)
    ly += two_tone(bus, above(164), OXBLOOD_LIT, OXBLOOD_SHADE)
    ly += glass_window(95, 143, 196, 164, 2.5, col=CAB_GLASS, hi=CAB_GLASS_HI)
    for px in (120.0, 146.0, 172.0):
        ly.append(L(box(px - 1.5, 143, px + 1.5, 164), OXBLOOD_SHADE))
    ly += glass_window(202, 143, 219, 182, 2.5, col=CAB_GLASS, hi=CAB_GLASS_HI)
    ly.append(L(box(95, 171, 196, 177), FACE))
    for wx in (116.0, 194.0):
        ly.append(L(sd_disk(wx, 197, 12.0), SIGN_DARK))
        ly.append(L(sd_disk(wx, 197, 5.0), STEEL_LIT))
    return ly


# --- lift parts (round 2) --------------------------------------------------------------------
def chair_dq(gx, gy, k=1.0):
    """Detachable quad chair seen from the side, grip on the cable at (gx, gy): the detachable
    grip (a box with two running wheels over the rope), a straight hanger into the C of the side
    frame (down behind the back, forward under the seat), the thick padded red back and seat
    (back to the left = downhill), the safety bar down in front of the riders with its footrest.
    112 k tall from the grip; lines stay >= 7 px."""
    lw = max(7.0, 8.0 * k)
    bw = max(7.0, 6.5 * k)
    grip = union(box(gx - 12 * k, gy - 7 * k, gx + 12 * k, gy + 8 * k, 3 * k),
                 sd_disk(gx - 7 * k, gy - 8 * k, 4.5 * k), sd_disk(gx + 7 * k, gy - 8 * k, 4.5 * k))
    pts = [(0, 0), (0, 34), (-6, 42), (-20, 48), (-30, 60), (-33, 78), (-29, 94), (-17, 102),
           (22, 102)]
    frame = sd_polyline([(gx + px * k, gy + py * k) for px, py in pts], lw)
    bar = sd_polyline([(gx - 18 * k, gy + 54 * k), (gx + 30 * k, gy + 58 * k),
                       (gx + 38 * k, gy + 108 * k)], bw)
    foot = sd_segment(gx + 26 * k, gy + 108 * k, gx + 52 * k, gy + 108 * k, bw)
    back = box(gx - 28 * k, gy + 46 * k, gx - 10 * k, gy + 94 * k, 7 * k)
    seat = box(gx - 28 * k, gy + 80 * k, gx + 36 * k, gy + 96 * k, 7 * k)
    lit = union(box(gx - 28 * k, gy + 46 * k, gx - 18 * k, gy + 88 * k, 6 * k),
                box(gx - 22 * k, gy + 80 * k, gx + 36 * k, gy + 88 * k, 5 * k))
    return [L(union(grip, frame), STEEL_DARK), L(union(back, seat), ROOF_SHADE), L(lit, ROOF_LIT),
            L(union(bar, foot), STEEL_DARK)]


def cabin_gondola(gx, gy, k=1.0):
    """Gondola cabin seen from the side, grip on the cable at (gx, gy): the detachable grip, the
    hanger arm straight down to the roof frame, a rounded red cabin (lit front, shaded right side)
    with a big dark window band in two panes and a small white mountain emblem low on the
    front. 112 k tall from the grip."""
    lw = max(7.0, 8.0 * k)
    grip = union(box(gx - 12 * k, gy - 7 * k, gx + 12 * k, gy + 8 * k, 3 * k),
                 sd_disk(gx - 7 * k, gy - 8 * k, 4.5 * k), sd_disk(gx + 7 * k, gy - 8 * k, 4.5 * k))
    arm = sd_segment(gx, gy, gx, gy + 40 * k, lw)
    frame = box(gx - 26 * k, gy + 36 * k, gx + 26 * k, gy + 44 * k, 2.5 * k)
    body = box(gx - 36 * k, gy + 42 * k, gx + 36 * k, gy + 112 * k, 13 * k)
    out = [L(union(grip, arm, frame), STEEL_DARK)]
    out += two_tone(body, left_of(gx + 18 * k), ROOF_LIT, ROOF_SHADE)
    win = box(gx - 29 * k, gy + 51 * k, gx + 29 * k, gy + 86 * k, 6 * k)
    out.append(L(win, CAB_GLASS))
    out.append(L(inter(win, above(gy + 60 * k)), CAB_GLASS_HI))
    out.append(L(box(gx + 9 * k, gy + 51 * k, gx + 14 * k, gy + 86 * k), ROOF_SHADE))
    if k >= 0.7:
        out.append(L(sd_polygon([(gx - 22 * k, gy + 104 * k), (gx - 13 * k, gy + 92 * k),
                                 (gx - 4 * k, gy + 104 * k)]), FACE))
    return out


def cabin_tram(gx, gy, ux, uy, k=1.0, gap=TRAM_SEP):
    """Aerial tram seen from the side: the carriage riding both track ropes (upper rope through
    (gx, gy), the lower `gap` below; rope direction (ux, uy)) on four wheels, the hanger down to
    the pyramid frame, the big level cabin - a dark window band with mullions over the red band -
    94 k below the upper rope."""
    out = []
    car = sd_orect(gx, gy + gap / 2, ux, uy, 36 * k, gap / 2 + 5 * k, 3 * k)
    out.append(L(car, STEEL_SHADE))
    wh = union(*[sd_disk(gx + ux * d * k, gy + uy * d * k - 2.0, 5.5 * k)
                 for d in (-26.0, -10.0, 10.0, 26.0)])
    out.append(L(wh, STEEL_DARK))
    hy = gy + gap + 4 * k
    out.append(L(sd_segment(gx, hy, gx, gy + 42 * k, max(8.0, 9.0 * k)), STEEL_DARK))
    out.append(L(sd_polygon([(gx - 26 * k, gy + 54 * k), (gx + 26 * k, gy + 54 * k),
                             (gx, gy + 36 * k)]), STEEL_SHADE))
    body = box(gx - 52 * k, gy + 52 * k, gx + 52 * k, gy + 94 * k, 6 * k)
    out += two_tone(body, left_of(gx + 34 * k), ROOF_LIT, ROOF_SHADE)
    win = box(gx - 48 * k, gy + 56 * k, gx + 48 * k, gy + 78 * k, 3 * k)
    out.append(L(win, CAB_GLASS))
    out.append(L(inter(win, above(gy + 63 * k)), CAB_GLASS_HI))
    if k >= 0.7:
        out.append(L(inter(win, union(*[np.abs(X - (gx + d * k)) - 1.5
                                        for d in (-32.0, -16.0, 0.0, 16.0, 32.0)])), CAB_GLASS))
    out.append(L(box(gx - 48 * k, gy + 89 * k, gx + 48 * k, gy + 96 * k, 2 * k), STEEL_DARK))
    return out


def lattice_tower(x, foot, head, wb=32.0, wt=14.0, arm=34.0, panels=4):
    """Lattice tower (the tram's): two legs that taper from wb at the foot to wt at the head, X
    braces between them, the saddle cross-arm at the head; left leg lit."""
    def lx(y, s):
        t = (foot - y) / (foot - head)
        return x + s * (wb / 2 + (wt - wb) / 2 * t)
    braces = []
    for i in range(panels):
        y0 = foot - (foot - head) * i / panels
        y1 = foot - (foot - head) * (i + 1) / panels
        braces.append(sd_segment(lx(y0, -1), y0, lx(y1, 1), y1, 4.5))
        braces.append(sd_segment(lx(y0, 1), y0, lx(y1, -1), y1, 4.5))
    out = [L(union(*braces), STEEL_SHADE)]
    out.append(L(sd_segment(lx(foot, -1), foot + 3, lx(head, -1), head, 7.0), STEEL_LIT))
    out.append(L(sd_segment(lx(foot, 1), foot + 3, lx(head, 1), head, 7.0), STEEL_SHADE))
    out.append(L(box(x - arm / 2, head - 5, x + arm / 2, head + 5, 3), STEEL_DARK))
    return out


def strands(p_up, p_lo, slope, tx, w=ROPE_W):
    """Two straight rope strands from p_up / p_lo with slope dy/dx to x = tx. Returns (sdf,
    (q_up, q_lo))."""
    qu = (tx, p_up[1] + (tx - p_up[0]) * slope)
    ql = (tx, p_lo[1] + (tx - p_lo[0]) * slope)
    return union(sd_segment(p_up[0], p_up[1], qu[0], qu[1], w),
                 sd_segment(p_lo[0], p_lo[1], ql[0], ql[1], w)), (qu, ql)


def loaf(x0, x1, top, bot):
    """The detachable terminal's 'loaf' seen from the side: a long housing with round ends (the
    fixed-grip hood's recipe, fattened), lit upper part, the dark drive frame under it, snow on
    top."""
    h = bot - top
    r = 0.46 * h
    body = box(x0, top, x1, bot, r)
    out = [L(body, ROOF_SHADE), L(inter(body, above(top + h * 0.55)), ROOF_LIT)]
    out.append(L(box(x0 + r * 0.8, bot - 3, x1 - r * 0.8, bot + 6, 2), STEEL_DARK))
    out.append(L(union(box(x0 + r * 0.7, top - 8, x1 - r * 0.7, top + 4, 6),
                       sd_disk(x0 + 0.32 * (x1 - x0), top - 6, 6.5),
                       sd_disk(x0 + 0.66 * (x1 - x0), top - 5, 5.5)), SNOW_HI))
    return out


def legs(xs, top, ground_y, w=8.0):
    out = []
    for x in xs:
        col = box(x - w / 2, top, x + w / 2, ground_y(x) + 3, 1.5)
        out += two_tone(col, left_of(x), STEEL_LIT, STEEL_SHADE)
    return out


def booth(bx0, bx1, by0, ground_y):
    """The operator booth of the fixed-grip terminals (timber, glass, snow on the roof)."""
    by1 = ground_y(0.5 * (bx0 + bx1)) + 2
    wall = box(bx0, by0, bx1, by1, 3)
    out = two_tone(wall, left_of(bx0 + (bx1 - bx0) * 0.72), WOOD_LIT, WOOD_SHADE)
    out.append(L(box(bx0 + 6, by0 + 9, bx1 - 6, by0 + 21, 2.5), GLASS))
    out.append(L(box(bx0 + 6, by0 + 9, bx1 - 6, by0 + 15, 2.5), GLASS_HI))
    out.append(L(box(bx0 - 4, by0 - 7, bx1 + 4, by0 + 3, 4), SNOW_HI))
    return out


def house(x0, xe, x1, roof_top, eave, stone_top, ground_y, portal):
    """The gondola station house seen three-quarter: the long side face x0..xe (lit, windows), the
    line-end face xe..x1 (shade) with the portal (px0, px1, top: a round-topped dark opening),
    cedar walls over a stone base, the loaf roof over both. x1 < x0 mirrors it (line to the left)."""
    out = []
    lo, hi = min(x0, x1), max(x0, x1)
    sl, sh = (min(x0, xe), max(x0, xe)), (min(xe, x1), max(xe, x1))
    out.append(L(grounded(sl[0], sl[1], eave - 4, ground_y), WOOD_LIT))
    out.append(L(grounded(sh[0], sh[1], eave - 4, ground_y), WOOD_SHADE))
    sbase = grounded(sl[0], sl[1], stone_top, ground_y)
    out.append(L(sbase, STONE_LIT))
    out.append(stone_courses(sbase, stone_top))
    out.append(L(grounded(sh[0], sh[1], stone_top, ground_y), STONE_SHADE))
    n = 3
    for i in range(n):
        wx = sl[0] + (sl[1] - sl[0]) * (i + 0.5) / n
        out += glass_window(wx - 9, eave + 8, wx + 9, eave + 30, 2)
    px0, px1, pt = portal
    pb = ground_y(0.5 * (px0 + px1)) + 1
    out.append(L(box(px0, pt, px1, pb, 0.5 * (px1 - px0)), STEEL_DARK))
    out.append(L(box(px0 + 3, pt + 3, px1 - 3, pb, 0.5 * (px1 - px0) - 3), CAB_GLASS))
    out += loaf(lo - 6, hi + 6, roof_top, eave + 2)[:2]
    out.append(L(union(box(lo + 6, roof_top - 8, hi - 6, roof_top + 4, 6),
                       sd_disk(lo + 0.30 * (hi - lo), roof_top - 6, 6.5),
                       sd_disk(lo + 0.66 * (hi - lo), roof_top - 5, 5.5)), SNOW_HI))
    return out


# --- detachable ------------------------------------------------------------------------------
def _hill_shade(gsdf):
    return L(inter(gsdf, sd_polygon([(186, 300), (262, 110), (300, 110), (300, 300)])),
             SNOW_SHADE, 0.6, mark=False)


def icon_dq_base():
    """Detachable base station (SkiDQBaseStationBuilding): the 'loaf' - a long red housing with
    round ends raised on steel legs - at the foot of the hill, the operator booth, the two strands
    leaving its nose up to a tower, a detachable quad on the lower strand. Same scene as the
    fixed-grip base (ground, ranges, step corner) so the lift-type sets read as one family."""
    ly = [sky(), step_corner()]
    ly += far_range([(-14, 168), (34, 132), (84, 170), (124, 150), (170, 190)])
    gsdf, g = ground(_hill, 110)
    ly.append(g)
    ly.append(_hill_shade(gsdf))
    gy = lambda x: float(_hill(x))
    ly.append(shadow(100, 207, 58, 6))
    th = np.radians(30.0)
    slope = -np.tan(th)
    rope, (qu, ql) = strands((140.0, 124.0), (140.0, 124.0 + DQ_SEP), slope, 206.0)
    ly.append(shadow(206, gy(206) + 1, 13, 3.5))
    ly += tower(206.0, gy(206.0), qu[1], arm=26.0, arm2=ql[1])
    ly.append(L(rope, STEEL_DARK))
    ly += legs((58.0, 98.0, 138.0), 142.0, gy)
    ly.append(L(box(36, 198, 160, 208, 2), CONC_SHADE))
    ly += loaf(34.0, 160.0, 100.0, 148.0)
    ly += booth(40.0, 66.0, 168.0, gy)
    gx = 182.0
    gyy = 124.0 + DQ_SEP + (gx - 140.0) * slope
    ly += chair_dq(gx, gyy, 0.50)
    return ly


TOP_FLAT = [(-14, 238), (58, 216), (112, 176), (214, 170), (240, 182), (270, 196)]


def _top_flat(x):
    xs, ys = zip(*TOP_FLAT)
    return float(np.interp(x, xs, ys))


def _summit_scene(profile):
    ly = [sky(), step_corner()]
    ly += far_range([(-14, 196), (30, 150), (88, 196)])
    ly += far_range([(120, 176), (176, 112), (214, 140), (238, 118), (270, 160)])
    mt = sd_polygon(profile + [(270, 300), (-14, 300)])
    ly.append(L(mt, vgrad(SNOW_HI, SNOW_LO, 160, 245), mark=False))
    ly.append(L(inter(mt, sd_polygon([(206, 170), (270, 170), (270, 300), (236, 300)])),
                SNOW_SHADE, 0.7, mark=False))
    return ly


def icon_dq_top():
    """Detachable top station (SkiDQTopStationBuilding): the loaf mirrored on the summit plateau,
    the strands arriving from a tower down the left flank, a quad on the lower strand, the booth
    under the loaf's far end."""
    ly = _summit_scene(TOP_FLAT)
    gy = _top_flat
    ly.append(shadow(160, 174, 58, 5))
    slope = np.tan(np.radians(22.0))          # going left the strands drop
    p_up, p_lo = (112.0, 100.0), (112.0, 100.0 + DQ_SEP)
    qu = (58.0, p_up[1] + (p_up[0] - 58.0) * slope)
    ql = (58.0, p_lo[1] + (p_lo[0] - 58.0) * slope)
    rope = union(sd_segment(*p_up, *qu, ROPE_W), sd_segment(*p_lo, *ql, ROPE_W))
    ly.append(shadow(58, gy(58) + 1, 13, 3.5))
    ly += tower(58.0, gy(58.0), qu[1], arm=26.0, arm2=ql[1])
    ly.append(L(rope, STEEL_DARK))
    ly += legs((122.0, 162.0, 202.0), 116.0, gy)
    ly += booth(184.0, 212.0, 136.0, gy)
    ly += loaf(100.0, 220.0, 72.0, 122.0)
    gx = 88.0
    ly += chair_dq(gx, p_lo[1] + (p_lo[0] - gx) * slope, 0.48)
    return ly


def icon_dq_line():
    """Detachable lift line (SkiDetachableLine): a detachable quad (C-frame hanger, padded seat,
    safety bar down) on the cable between two towers - the fixed-grip line's scene."""
    ly = [sky(), step_corner()]
    gsdf, g = ground(_lift_ground, 150)
    ly.append(g)
    ly.append(L(inter(gsdf, sd_polygon([(120, 300), (270, 120), (300, 300)])), SNOW_SHADE, 0.45,
                mark=False))
    l_x, l_head = 62.0, 108.0
    r_x, r_head = 194.0, 48.0
    ly.append(shadow(l_x, _lift_ground(l_x) + 2, 14, 4))
    ly.append(shadow(r_x, _lift_ground(r_x) + 2, 14, 4))
    ly += tower(l_x, _lift_ground(l_x), l_head, arm=28.0)
    ly += tower(r_x, _lift_ground(r_x), r_head, arm=28.0)
    cable = [(l_x + (r_x - l_x) * t, l_head + (r_head - l_head) * t + 40.0 * t * (1 - t) - 1)
             for t in np.linspace(0, 1, 25)]
    ly.append(L(sd_polyline(cable, 7.0), STEEL_DARK))
    gx = 0.5 * (l_x + r_x)
    gyy = 0.5 * (l_head + r_head) + 10.0 - 1
    ly += chair_dq(gx, gyy - 0.45, 0.90)
    return ly


# --- gondola ---------------------------------------------------------------------------------
def icon_gd_base():
    """Gondola base station (SkiGondolaBaseStationBuilding): the station house - cedar walls over
    a stone base under the red loaf roof, the line end with its portal - at the foot of the hill,
    the cable leaving the portal up to a tower, a cabin just out of the station."""
    ly = [sky(), step_corner()]
    ly += far_range([(-14, 168), (34, 132), (84, 170), (124, 150), (170, 190)])
    gsdf, g = ground(_hill, 110)
    ly.append(g)
    ly.append(_hill_shade(gsdf))
    gy = lambda x: float(_hill(x))
    ly.append(shadow(104, 208, 62, 6))
    slope = -np.tan(np.radians(34.0))
    p_up, p_lo = (146.0, 136.0), (146.0, 136.0 + GD_SEP)
    rope, (qu, ql) = strands(p_up, p_lo, slope, 208.0, w=7.0)
    ly.append(shadow(208, gy(208) + 1, 13, 3.5))
    ly += tower(208.0, gy(208.0), qu[1], arm=24.0, arm2=ql[1])
    ly += house(40.0, 130.0, 160.0, 100.0, 126.0, 176.0, gy, (137.0, 153.0, 132.0))
    ly.append(L(rope, STEEL_DARK))
    gx = 184.0
    ly += cabin_gondola(gx, p_lo[1] + (gx - p_lo[0]) * slope, 0.52)
    return ly


def icon_gd_top():
    """Gondola top station (SkiGondolaTopStationBuilding): the station house on the summit plateau,
    its portal facing down the left flank, the cable arriving from a tower, a cabin on it."""
    ly = _summit_scene(TOP_FLAT)
    gy = _top_flat
    ly.append(shadow(160, 175, 62, 5))
    slope = np.tan(np.radians(22.0))
    p_up, p_lo = (106.0, 112.0), (106.0, 112.0 + GD_SEP)
    qu = (56.0, p_up[1] + (p_up[0] - 56.0) * slope)
    ql = (56.0, p_lo[1] + (p_lo[0] - 56.0) * slope)
    rope = union(sd_segment(*p_up, *qu, 7.0), sd_segment(*p_lo, *ql, 7.0))
    ly.append(shadow(56, gy(56) + 1, 13, 3.5))
    ly += tower(56.0, gy(56.0), qu[1], arm=24.0, arm2=ql[1])
    ly += house(212.0, 122.0, 96.0, 74.0, 100.0, 148.0, gy, (99.0, 115.0, 106.0))
    ly.append(L(rope, STEEL_DARK))
    gx = 84.0
    ly += cabin_gondola(gx, p_lo[1] + (p_lo[0] - gx) * slope, 0.50)
    return ly


def icon_gd_line():
    """Gondola lift line (SkiGondolaLine): a red cabin on the cable between two towers - the
    fixed-grip line's scene."""
    ly = [sky(), step_corner()]
    gsdf, g = ground(_lift_ground, 150)
    ly.append(g)
    ly.append(L(inter(gsdf, sd_polygon([(120, 300), (270, 120), (300, 300)])), SNOW_SHADE, 0.45,
                mark=False))
    l_x, l_head = 62.0, 108.0
    r_x, r_head = 194.0, 48.0
    ly.append(shadow(l_x, _lift_ground(l_x) + 2, 14, 4))
    ly.append(shadow(r_x, _lift_ground(r_x) + 2, 14, 4))
    ly += tower(l_x, _lift_ground(l_x), l_head, arm=28.0)
    ly += tower(r_x, _lift_ground(r_x), r_head, arm=28.0)
    cable = [(l_x + (r_x - l_x) * t, l_head + (r_head - l_head) * t + 40.0 * t * (1 - t) - 1)
             for t in np.linspace(0, 1, 25)]
    ly.append(L(sd_polyline(cable, 7.0), STEEL_DARK))
    gx = 0.5 * (l_x + r_x)
    ly += cabin_gondola(gx, 0.5 * (l_head + r_head) + 9.0, 0.86)
    return ly


# --- tram ------------------------------------------------------------------------------------
def icon_tram_base():
    """Tram base station (SkiTramBaseStationBuilding): the tram hall - stone base, a glazed upper
    story behind steel X-bracing, the snowy shed roof - with the brick clock tower in front, the
    two track ropes leaving the hall up to a lattice tower, the big cabin on them."""
    ly = [sky(), step_corner()]
    ly += far_range([(-14, 168), (34, 132), (84, 170), (124, 150), (170, 190)])
    gsdf, g = ground(_hill, 110)
    ly.append(g)
    ly.append(_hill_shade(gsdf))
    gy = lambda x: float(_hill(x))
    ly.append(shadow(96, 208, 56, 6))
    slope = -np.tan(np.radians(32.0))
    p_up, p_lo = (140.0, 120.0), (140.0, 120.0 + TRAM_SEP)
    tx = 204.0
    rope, (qu, ql) = strands(p_up, p_lo, slope, tx, w=6.0)
    ly.append(shadow(tx, gy(tx) + 1, 16, 4))
    ly += lattice_tower(tx, gy(tx), qu[1] - 2, wb=26.0, wt=12.0, arm=28.0, panels=3)
    # the hall
    hx0, hx1 = 40.0, 144.0
    gy0 = gy(90.0) + 3
    hall = box(hx0, 100, hx1, gy0)
    ly.append(L(hall, GLASS))
    ly.append(L(box(hx0, 100, hx1, 116), GLASS_HI))
    bays = 3
    bw = (hx1 - hx0) / bays
    br = []
    for i in range(bays):
        a, b = hx0 + i * bw, hx0 + (i + 1) * bw
        br += [sd_segment(a, 104, b, 164, 4.0), sd_segment(a, 164, b, 104, 4.0)]
    for i in range(bays + 1):
        a = hx0 + i * bw
        br.append(sd_segment(a, 100, a, 166, 5.0))
    ly.append(L(inter(hall, union(*br)), STEEL_LIT))
    stone = box(hx0, 164, hx1, gy0)
    ly.append(L(stone, STONE_LIT))
    ly.append(stone_courses(stone, 164.0))
    roof = sd_polygon([(hx0 - 6, 104), (hx1 + 6, 94), (hx1 + 6, 102), (hx0 - 6, 112)])
    ly.append(L(roof, STEEL_SHADE))
    ly.append(L(union(sd_polygon([(hx0 - 6, 104), (hx1 + 6, 94), (hx1 + 6, 88), (hx0 - 6, 98)]),
                      sd_disk(hx0 + 30, 99, 5.0), sd_disk(hx0 + 76, 93, 4.5)), SNOW_HI))
    ly.append(L(rope, STEEL_DARK))
    # the clock tower
    cx0, cx1 = 98.0, 122.0
    ct = box(cx0, 84, cx1, gy0 + 1)
    ly += two_tone(ct, left_of(cx0 + 15), BRICK_LIT, BRICK_SHADE)
    ly.append(L(box(cx0 - 3, 66, cx1 + 3, 88, 2), FACE))
    ly.append(L(box(cx0 - 3, 86, cx1 + 3, 92, 1.5), ROOF_SHADE))
    ly.append(L(union(sd_segment(110, 77, 110, 70, 3.0), sd_segment(110, 77, 115, 79, 3.0)),
                STEEL_DARK))
    cap = sd_polygon([(110, 48), (128, 66), (92, 66)])
    ly += two_tone(cap, left_of(110), STEEL_LIT, STEEL_SHADE)
    ly.append(L(inter(cap, above(56)), SNOW_HI))
    gx = 174.0
    ly += cabin_tram(gx, p_up[1] + (gx - p_up[0]) * slope, np.cos(np.radians(32.0)),
                     -np.sin(np.radians(32.0)), 0.50)
    return ly


def icon_tram_top():
    """Tram top station (SkiTramTopStationBuilding): the exoskeleton - a concrete block on the
    summit carrying a steel portal frame with its braces and the big face-on bullwheel - the two
    track ropes arriving from a lattice tower down the left flank, the cabin on them."""
    ly = _summit_scene(TOP_FLAT)
    gy = _top_flat
    ly.append(shadow(162, 175, 56, 5))
    slope = np.tan(np.radians(22.0))
    p_up, p_lo = (128.0, 104.0), (128.0, 104.0 + TRAM_SEP)
    tx = 64.0
    qu = (tx, p_up[1] + (p_up[0] - tx) * slope)
    ql = (tx, p_lo[1] + (p_lo[0] - tx) * slope)
    rope = union(sd_segment(*p_up, *qu, 6.0), sd_segment(*p_lo, *ql, 6.0))
    ly.append(shadow(tx, gy(tx) + 1, 15, 4))
    ly += lattice_tower(tx, gy(tx), qu[1] - 2, wb=24.0, wt=12.0, arm=28.0, panels=3)
    # the frame behind the wheel, the wheel, the frame's front legs, the block
    fx0, fx1, ftop = 128.0, 204.0, 54.0
    ly.append(L(union(sd_segment(fx1, 128, fx0 + 8, ftop + 8, 5.0),
                      sd_segment(fx0, 128, fx1 - 8, ftop + 8, 5.0)), STEEL_SHADE))
    ly.append(L(bullwheel(166.0, 92.0, 26.0, 7.0, 7.0, spokes=8, spoke_w=4.5), STEEL_DARK))
    ly.append(L(sd_disk(166.0, 92.0, 4.0), STEEL_LIT))
    for x in (fx0, fx1):
        leg = box(x - 6, ftop, x + 6, 134, 1.5)
        ly += two_tone(leg, left_of(x), STEEL_LIT, STEEL_SHADE)
    beam = box(fx0 - 12, ftop - 6, fx1 + 10, ftop + 6, 2)
    ly.append(L(beam, STEEL_LIT))
    ly.append(L(inter(beam, below(ftop + 1)), STEEL_SHADE))
    ly.append(L(box(fx0 - 10, ftop - 13, fx1 + 8, ftop - 4, 3), SNOW_HI))
    blk = box(116, 128, 212, gy(164) + 3)
    ly += two_tone(blk, left_of(190), CONC_LIT, CONC_SHADE)
    ly.append(L(box(116, 124, 212, 132, 1.5), STEEL_DARK))
    ly.append(L(rope, STEEL_DARK))
    gx = 92.0
    ly += cabin_tram(gx, p_up[1] + (p_up[0] - gx) * slope, -np.cos(np.radians(22.0)),
                     np.sin(np.radians(22.0)), 0.48)
    return ly


def icon_tram_line():
    """Aerial tram line (SkiTramLine): the big cabin and its carriage on the two track ropes
    between two lattice towers - the fixed-grip line's scene."""
    ly = [sky(), step_corner()]
    gsdf, g = ground(_lift_ground, 150)
    ly.append(g)
    ly.append(L(inter(gsdf, sd_polygon([(120, 300), (270, 120), (300, 300)])), SNOW_SHADE, 0.45,
                mark=False))
    l_x, l_head = 64.0, 112.0
    r_x, r_head = 196.0, 50.0
    ly.append(shadow(l_x, _lift_ground(l_x) + 2, 18, 4))
    ly.append(shadow(r_x, _lift_ground(r_x) + 2, 18, 4))
    ly += lattice_tower(l_x, _lift_ground(l_x), l_head, wb=30.0, wt=12.0, arm=30.0, panels=3)
    ly += lattice_tower(r_x, _lift_ground(r_x), r_head, wb=36.0, wt=12.0, arm=30.0, panels=4)
    sag = 24.0
    ropes = []
    for off in (0.0, TRAM_SEP):
        pts = [(l_x + (r_x - l_x) * t, l_head + off + (r_head - l_head) * t + sag * t * (1 - t))
               for t in np.linspace(0, 1, 25)]
        ropes.append(sd_polyline(pts, 6.0))
    ly.append(L(union(*ropes), STEEL_DARK))
    t = 0.5
    gx = l_x + (r_x - l_x) * t
    gyy = l_head + (r_head - l_head) * t + sag * t * (1 - t)
    dx, dy = (r_x - l_x), (r_head - l_head)
    ln = np.hypot(dx, dy)
    ly += cabin_tram(gx, gyy, dx / ln, dy / ln, 0.90)
    return ly


# ---------------------------------------------------------------------------------------------
# Table (Build-strip order, mod/SkiResortUI/src/hub/buildItems.ts BUILD_GROUPS: sign, lodge
# tiers | school, patrol hut, patrol HQ | per lift type base "1", top "2", line "3" | natural,
# groomed, snowmaking, night). The strip draws the picked lodge tier and lift type only.
# ---------------------------------------------------------------------------------------------
# File name = the prefab's name (keeping a name keeps its .cid); a lift LINE tile is named
# <Type>Line (the line prefab is <Type>HV). Build ids per buildItems.ts BUILD_ITEMS.
ICONS = {
    "SkiResortGate": icon_sign,                         # sign
    "SkiAFrameDayLodge": icon_lodge,                    # lodge
    "SkiLodgeHut": icon_lodge_hut,                      # lodgeHut        (round 2)
    "SkiLodgeMid": icon_lodge_mid,                      # lodgeMid        (round 2)
    "SkiLodgeSummit": icon_lodge_summit,                # lodgeSummit     (round 2)
    "SkiLodgeGrand": icon_lodge_grand,                  # lodgeGrand      (round 2)
    "SkiSchool": icon_school,                           # school          (round 2)
    "SkiPatrolHut": icon_patrol_hut,                    # patrolHut       (round 2)
    "SkiPatrolHQ": icon_patrol_hq,                      # patrolHQ        (round 2)
    "SkiBaseStationBuilding": icon_base_station,        # baseStation
    "SkiTopStationBuilding": icon_top_station,          # topStation
    "SkiChairliftLine": icon_lift,                      # lift
    "SkiDQBaseStationBuilding": icon_dq_base,           # baseStationDetachable (round 2)
    "SkiDQTopStationBuilding": icon_dq_top,             # topStationDetachable  (round 2)
    "SkiDetachableLine": icon_dq_line,                  # liftDetachable        (round 2)
    "SkiGondolaBaseStationBuilding": icon_gd_base,      # baseStationGondola    (round 2)
    "SkiGondolaTopStationBuilding": icon_gd_top,        # topStationGondola     (round 2)
    "SkiGondolaLine": icon_gd_line,                     # liftGondola           (round 2)
    "SkiTramBaseStationBuilding": icon_tram_base,       # baseStationTram       (round 2)
    "SkiTramTopStationBuilding": icon_tram_top,         # topStationTram        (round 2)
    "SkiTramLine": icon_tram_line,                      # liftTram              (round 2)
    "SkiRunNaturalA": icon_run_natural,                 # runNatural
    "SkiRunGroomedA": icon_run_groomed,                 # runGroomed
    "SkiRunSnowmakingA": icon_run_snowmaking,           # runSnowmaking
    "SkiRunNightA": icon_run_night,                     # runNight
    "SkiRunCatTrackA": icon_run_cattrack,               # runCatTrack     (T38; was the groomed tile's icon)
    "SkiSafetyFence": icon_safety_fence,                # safetyFence     (2026-10-26; was the vanilla chain-link SVG)
    "SkiBusDepot": icon_bus_depot,                      # busDepot        (2026-11-01; was the Chalet's tile)
}
# T38 (2026-10-17): the cat track tile is new (it had borrowed the groomed tile's icon); the 25-tile review
# sheets and ROUND2 keep their meaning, so it is listed apart.
T38_NEW = ["SkiRunCatTrackA"]
FENCE_NEW = ["SkiSafetyFence"]      # 2026-10-26, listed apart for the same reason
DEPOT_NEW = ["SkiBusDepot"]         # 2026-11-01, listed apart for the same reason
STRIP_ORDER = [n for n in ICONS if n not in T38_NEW + FENCE_NEW + DEPOT_NEW]
# the nine tiles of the 2026-10-04 style-E round (reproduced byte for byte; ski_icons_sheet.py's
# default sheet shows these)
E1_NINE = ["SkiResortGate", "SkiAFrameDayLodge", "SkiBaseStationBuilding", "SkiTopStationBuilding",
           "SkiChairliftLine", "SkiRunNaturalA", "SkiRunGroomedA", "SkiRunSnowmakingA",
           "SkiRunNightA"]
ROUND2 = [n for n in STRIP_ORDER if n not in E1_NINE]   # (STRIP_ORDER excludes T38_NEW)
STEP = {"SkiBaseStationBuilding": "1", "SkiTopStationBuilding": "2", "SkiChairliftLine": "3",
        "SkiDQBaseStationBuilding": "1", "SkiDQTopStationBuilding": "2", "SkiDetachableLine": "3",
        "SkiGondolaBaseStationBuilding": "1", "SkiGondolaTopStationBuilding": "2",
        "SkiGondolaLine": "3",
        "SkiTramBaseStationBuilding": "1", "SkiTramTopStationBuilding": "2", "SkiTramLine": "3"}
# not a Build-strip tile (installed in ImportedData\SkiResortCategory\, cid kept: KEEP_CID)
LAUNCHER = {"SkiResortTab": icon_launcher}
ALL_ICONS = dict(ICONS, **LAUNCHER)


def render(name):
    """The tile's picture plus the plate finish (foot + rim) drawn last. Returns (rgba uint8,
    mark coverage)."""
    if X is None or X.shape[0] != CANVAS * SS:
        build_grid()
    return rasterise(ALL_ICONS[name]() + finish(night=(name == "SkiRunNightA")))


def measure(rgba, mk, name):
    """Mark bounding box, the smallest mark clearance to the plate edge (px) and, on the step
    tiles, the largest mark coverage inside the top-left keep-out box."""
    m = mk > 0.02
    ys, xs = np.nonzero(m)
    if not len(xs):        # T38: a tile that is all field (the groomed bands) has no marks to measure
        return dict(bbox=(0, 0, 0, 0), clear=float("inf"), keepout=None)
    x0, x1, y0, y1 = int(xs.min()), int(xs.max()), int(ys.min()), int(ys.max())
    c = np.arange(CANVAS) + 0.5
    gx, gy = np.meshgrid(c, c)
    px0, px1, r = PLATE
    d = sd_round_rect(px0, px0, px1, px1, r, x=gx, y=gy)
    clear = float((-d[m]).min()) - 0.5
    keep = None
    if name in STEP:
        k0, k1, k2, k3 = (int(v) for v in STEP_KEEPOUT)
        keep = float(mk[k1:k3, k0:k2].max())
    return dict(bbox=(x0, y0, x1, y1), clear=clear, keepout=keep)


# ---------------------------------------------------------------------------------------------
# PNG + cid
# ---------------------------------------------------------------------------------------------
def encode_png(img):
    """Minimal PNG encoder: 8-bit RGBA (or RGB), no interlace, filter 0 rows."""
    img = np.ascontiguousarray(img.astype(np.uint8))
    h, w, c = img.shape
    ct = 6 if c == 4 else 2
    raw = b"".join(b"\x00" + img[r].tobytes() for r in range(h))

    def chunk(typ, data):
        cc = typ + data
        return struct.pack(">I", len(data)) + cc + struct.pack(">I", zlib.crc32(cc) & 0xFFFFFFFF)

    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, ct, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def write_png(path, img):
    with open(path, "wb") as f:
        f.write(encode_png(img))


def cid_for(png_name):
    if png_name in KEEP_CID:
        return KEEP_CID[png_name]
    return hashlib.md5((CID_SALT + png_name).encode("ascii")).hexdigest()


def main():
    global VERBOSE
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--out", default=os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                                  "icons_out"),
                    help="output directory for the PNG + .png.cid pairs")
    ap.add_argument("--only", default="",
                    help="comma-separated icon names to write (default: the nine strip tiles; the launcher "
                         "SkiResortTab only when named here)")
    ap.add_argument("--verbose", action="store_true",
                    help="print geometry notes (chair clearance, plume path)")
    args = ap.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv
                         else sys.argv[1:])
    VERBOSE = args.verbose
    only = [s.strip() for s in args.only.split(",") if s.strip()]
    unknown = [s for s in only if s not in ALL_ICONS]
    if unknown:
        ap.error(f"unknown icon name(s): {', '.join(unknown)} (known: {', '.join(ALL_ICONS)})")

    os.makedirs(args.out, exist_ok=True)
    ok = True
    for name in ALL_ICONS:
        if only and name not in only:
            continue
        # F7 fixer: the launcher only on request (--only SkiResortTab). A default run writes the nine strip tiles
        # alone, so a bulk copy of --out into ImportedData\SkiResortIcons\ cannot carry the launcher's fixed cid
        # (KEEP_CID) into a second asset.
        if not only and name in LAUNCHER:
            continue
        png_name = name + ".png"
        rgba, mk = render(name)
        write_png(os.path.join(args.out, png_name), rgba)
        cid = cid_for(png_name)
        # byte-for-byte the SkiResortTab.png.cid format: 32 lowercase hex, no newline
        with open(os.path.join(args.out, png_name + ".cid"), "wb") as f:
            f.write(cid.encode("ascii"))
        m = measure(rgba, mk, name)
        flag = "OK" if m["clear"] >= RULE_MARGIN else "FAIL"
        if m["keepout"] is not None and m["keepout"] > 0.0:
            flag += " KEEPOUT-HIT"
        ok = ok and flag == "OK"
        print(f"{png_name:28s} cid={cid}  marks x{m['bbox'][0]}..{m['bbox'][2]} "
              f"y{m['bbox'][1]}..{m['bbox'][3]}  clear {m['clear']:.1f} px"
              + (f"  keep-out {m['keepout']:.3f}" if m["keepout"] is not None else "")
              + f"  {flag}", flush=True)
    print("ALL OK" if ok else "SOME FAIL (a mark is closer than 20 px to the plate edge, or a "
          "step tile has a mark in its digit corner)")
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
