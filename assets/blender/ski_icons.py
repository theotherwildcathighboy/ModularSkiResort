"""
Deterministic Build-strip icon generator for the Modular Ski Resort mod, style E.

Draws the nine icons of the hub's Build strip (256x256 RGBA PNG + 32-hex .png.cid sidecar each).
Style E ("a small flat colour picture fills a rounded plate") was chosen by Richard on 2026-10-04
from the options sheet docs/research/art/icons-2026-10-04/run-icon-options.png (rows A to F; his
verdict: E, with the snow gun throwing its snow UP and over, never "puking it out"). The final set
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
                             the run lower right; no corduroy (groomed vs snowmaking in greyscale:
                             stripes vs gun + plume)
    SkiRunNightA             dark sky, low moon, steel floodlight mast with amber lamps, corduroy
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
icon on every run (exit code 1 if any icon breaks it). The three lift tiles (base "1", top "2",
lift "3") keep the top-left 84 x 84 free of marks and darken the sky there for the hub's white step
digit (hub.module.scss .buildStep). Every tile ends with a darker plate foot (the hub's run type
stripe stands apart) and a thin dark rim (the plate keeps its outline on the active blue tile).

Look (taken from the game's own UI icons, Cities2_Data/Content/Game/UI/Media/Game/Icons/*.svg:
LargeParks, Trees, ParksAndRecreation, Electricity): flat fills, no outlines, two tones per object
(lit face / shade face, light from the upper left), muted natural palette, a soft shadow ellipse
under things that stand on the ground. No text in the icons.

No Blender/bpy dependency: pure numpy SDF rasterisation (analytic per-sample coverage at 4x
supersampling, premultiplied compositing, box downsample) plus a minimal PNG encoder, so it runs
directly on Blender's bundled python.exe:

    "<blender>\\<ver>\\python\\bin\\python.exe" ski_icons.py --out <dir> [--only A,B] [--verbose]

A default run (no --only) writes the nine strip tiles only. The launcher is written only when named:
--only SkiResortTab (F7 fixer: so a bulk copy of the output folder cannot duplicate its fixed cid).

Install, strip tiles: copy the PNG + .png.cid pairs into ImportedData\\SkiResortIcons\\ (game side), then run
tools/sync-gamedata.ps1 and tools/preflight.ps1 (preflight FAILS on a cid value used by two files).
Install, launcher (--only SkiResortTab, into its own --out folder): only the PNG, into
ImportedData\\SkiResortCategory\\; its installed .cid already holds the KEEP_CID value. The prefabs reference an icon as
"m_Icon": "assetdb://global/<cid>" (UIObject), and the hub copies the same URL into
mod/SkiResortUI/src/hub/buildItems.ts BUILD_ITEM_ICON. Review sheet (strip mock at the real
1080p / 4K sizes, greyscale, before / after): ski_icons_sheet.py next to this file.

Everything is deterministic (no randomness, no clock, no downloads), including the .cid sidecars:
each cid is the md5 hex digest of a fixed salt + png name, so re-running the script never churns
installed prefab references. (A cid only needs to be a unique 32-hex id for the asset database;
the game does not derive it from file content - see the hand-minted SkiResortTab.png.cid
precedent, which KEEP_CID reproduces instead of the md5 rule.) The 2026-10-04 port reproduces the
reviewed final set byte for byte.
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
    """Supersampled pixel-centre coordinate grids (canvas units) for the current SS."""
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
    """Rounded rectangle centred at (cx, cy), long axis along the unit vector (ux, uy), half
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
# Layers + rasteriser
# ---------------------------------------------------------------------------------------------
class L:
    """A paint layer: SDF, colour (RGB or (n,n,3) array), alpha (scalar or array), and whether it
    is a MARK (kept >= MARGIN from the plate edge, measured) or a FIELD (sky / snow / terrain)."""

    def __init__(self, sdf, col, a=1.0, mark=True):
        self.sdf, self.col, self.a, self.mark = sdf, col, a, mark


def two_tone(sdf, lit_region, lit, shade, mark=True):
    """Shade colour on the whole shape, lit colour on its part inside lit_region (an SDF)."""
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
ROPE_SEP = 20.0                     # haul / return strand spacing (centre to centre, across)

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
    Points (x, y, distance travelled); stops at the snow surface."""
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
# its growth per px travelled; the solid jet ends at cone x (path length to the apex) and starts
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


def icon_run_snowmaking():
    sc = RunScene()
    ly = list(sc.layers)
    # the gun stands on the run and throws its plume up and away over the open sky in a high
    # arc; the snow drifts down onto the run below the far end of the arc
    p = PLUME_SPEC
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


def icon_run_night():
    sc = RunScene(night=True)
    ly = list(sc.layers)
    ly += corduroy(sc.rp, NGROOVE)
    # the moon sits low over the run, below the hub's lock / hint-dot corner (top right)
    mx, my, mr = NIGHT_MOON
    ly.append(L(minus(sd_disk(mx, my, mr), sd_disk(mx + 11, my - 9, mr * 0.84)), MOON))
    ly.append(L(union(*[twinkle(*t) for t in NIGHT_STARS]), MOON, 0.95))
    ly += floodlight(sc.snow, 100.0, 54.0)
    return ly


NIGHT_MOON = (178.0, 104.0, 25.0)
NIGHT_STARS = ((150.0, 42.0, 8.0), (212.0, 68.0, 7.0))


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
    """The hub launcher: one snowy peak fills the plate (lit left face, blue-grey shade face right
    of a ridge line, two rock wedges under the summit, a paler range behind it on the right) and a
    red chair hangs from a diagonal cable that rises to the right across it. Drawn to read at the
    launcher's ~33 px: the white peak against the dark sky top, the cable as one dark line, the
    chair as the only warm colour. The cable ends in the open sky on both sides (no towers: a
    tower at 33 px is a grey stub that crowds the chair - draft D)."""
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
# Table (Build-strip order: sign, lodge | base "1", top "2", lift "3" | natural, groomed,
# snowmaking, night - mod/SkiResortUI/src/hub/buildItems.ts BUILD_GROUPS)
# ---------------------------------------------------------------------------------------------
ICONS = {
    "SkiResortGate": icon_sign,
    "SkiAFrameDayLodge": icon_lodge,
    "SkiBaseStationBuilding": icon_base_station,
    "SkiTopStationBuilding": icon_top_station,
    "SkiChairliftLine": icon_lift,
    "SkiRunNaturalA": icon_run_natural,
    "SkiRunGroomedA": icon_run_groomed,
    "SkiRunSnowmakingA": icon_run_snowmaking,
    "SkiRunNightA": icon_run_night,
}
STRIP_ORDER = list(ICONS.keys())
STEP = {"SkiBaseStationBuilding": "1", "SkiTopStationBuilding": "2", "SkiChairliftLine": "3"}
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
