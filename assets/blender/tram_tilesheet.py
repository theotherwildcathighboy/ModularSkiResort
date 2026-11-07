"""
tram_tilesheet.py - T76: the two tram stations on their OWN 4096^2 swatch sheet (`SkiTramSheet<letter>`).

Plan: docs/research/art/tram-tile-route-plan-2026-10-18.md (DECISIONS 282, the Q1-Q5 answers in its section 0). Round 1
build note: docs/research/art/tram-tile-build-1-2026-10-18.md.

Only tram_station.py --tiled imports this module, so a flag-off build is byte-identical to before. The base station's
folder carries the composed sheet as its own LOD0 maps (the owner). The top shares them through settings.json
(`../SkiTramBaseStation<L>/SkiTramBaseStation<L>_<Map>.png`, the lodges' cross-asset pattern). LOD1 / LOD2 keep the old
unique bake as a FAR SET (`<Name>_LOD1_*`, LOD2 -> LOD1), as the lodges do.

REUSE, NOT EDITS. The module calls cs2_tilesheet (the lodges' module, owned by the lodge exports) and never changes it.
It calls tile_mesh (with T70's strip path), strip_region / strip_stats / face_ppm / check_uv_cells, _swatch_maps /
weather_overlay and write_sheet. Its lookups go through cs2_bldg.BLDG_MATERIALS, cs2_tilesheet.FLAT_TILE_M and
cs2_tilesheet.W_CLASS. So register_materials() adds the station swatches there IN-PROCESS under "tram_*" names
(setdefault: the cs2_tilesheet.register_trim_materials idiom). A tram process never builds the family layout, so nothing
a lodge ships can see these entries.

SHEET (plan 1.4, 8 x 8 cells of 512 at 4096, heroes 2 x 2 = 960 px content + 32 pad, singles 480 + 16)
    swatch world size T = r x the material tile, one T per swatch for BOTH stations, px/m = content / T:
    steel_b 4.8 (200) | steel_t 4.8 (200) | roof 8.0 (120) | concrete 6.0 (160) | stone 6.0 (160) | grate 4.8 (200)
    galv 4.8 (200) | timber 4.8 (200) | render 7.2 (133) | dplate 1.2 (400) | gate_perf 2.4 (200) | hut_clad 2.4 (200)
    weathered "~w" (Q4) of stone, concrete, roof, steel_b, timber on single cells: same T, half the px/m.
    palette (4, 5): 8 x 8 flat cells of 64 px. Labels: base (0, 6) 2 x 2 (1024 x 1024), top (2, 6) 2 x 1 (1024 x 512).
ROUTES (plan 2.2, rules after the table): per part and key -
    tile     faces cut at the swatch grid (design-frame box projection, origins on the lift axis / grade)
    stack    a planar patch under one swatch maps whole into one cell (cs2_tilesheet.shift_patches: 0 tris)
    strip    a long thin member on a strip key (steel, plain galv, concrete <= 0.6 m wide, roof fascias) mapped
             whole-length along u over the swatch width (T70's strip path: 0 tris)
    palette  only keys that are a flat color today (glass, rope, safety, trim red, wheels, lamps ...)
    label    dials, pictograms and boards in the role's label rect (dials and the two pictograms stacked)
    _Gls     the hall glazing leaves the main mesh (DECISIONS 281: clear plain glass). `_Win` panes unchanged
WEATHER (Q4: a weathered swatch per EXISTING piece, no band cuts): after tile_mesh, whole pieces chosen by WEATHER_RULES
move from the clean swatch to its "~w" cell (same relative position, so the pattern continues).
"""

import hashlib
import json
import math
import os
import shutil
import sys

import bmesh
import bpy
import numpy as np

_HERE = os.path.dirname(os.path.abspath(__file__))
if _HERE not in sys.path:
    sys.path.insert(0, _HERE)
import cs2_asset            # noqa: E402
import cs2_bldg as K        # noqa: E402
import cs2_compose          # noqa: E402
import cs2_submesh          # noqa: E402
import cs2_tilesheet as TS  # noqa: E402
import cs2_uv               # noqa: E402

SHEET_A = 4096
GRID = 8
SHEET_PREFIX = "SkiTramSheet"
SHEET_VERSION = "tram-1"
OWNER_PREFIX = "SkiTramBaseStation"
ROLE_PREFIX = {"base": "SkiTramBaseStation", "top": "SkiTramTopStation"}
FAR_TEX = {"base": 2048, "top": 1024}          # plan section 5 (base 1024 would be a 1.25 x margin only)
MAPS = ("BaseColor", "Normal", "MaskMap", "ControlMask", "Emissive")

# ---- swatches (plan 1.4 / 2.1), "cell" (col, row, span) in cells of A / 8, rows bottom-up ------------------------
# set swatches: CC0 set + its natural (square) tile. T = repeat x tile. Today's tram mapped the 2:1 Concrete034 /
# Bricks096 images onto SQUARE 3.0 / 2.0 m tiles (material_table metres_per_tile), so the square tile keeps today's grain
# and course height. Flat swatches (set None): T = cs2_tilesheet.FLAT_TILE_M[key], procedural detail ("proc").
SWATCHES = {
    "tram_steel_b": dict(cell=(0, 0, 2), T=4.8, slot=1, recolor=True, metal=0.10, smooth=0.45, proc="paint"),
    "tram_steel_t": dict(cell=(2, 0, 2), T=4.8, slot=1, recolor=True, metal=0.50, smooth=0.40, proc="paint"),
    "tram_roof": dict(cell=(4, 0, 2), T=8.0, slot=2, recolor=True, metal=0.10, smooth=0.40, proc="seam"),   # 0.50 m corrugation
    "tram_concrete": dict(cell=(6, 0, 2), T=6.0, set="Concrete034", tile=3.0, tint=0.58),
    "tram_stone": dict(cell=(0, 2, 2), T=6.0, set="Bricks096", tile=2.0),
    "tram_grate": dict(cell=(2, 2, 2), T=4.8, set="Metal032", tile=2.4, metal=1.0, proc="grate"),
    "tram_galv": dict(cell=(4, 2, 2), T=4.8, set="Metal032", tile=2.4, metal=1.0),
    "tram_timber": dict(cell=(6, 2, 2), T=4.8, set="WoodSiding013", tile=2.4),
    "tram_render": dict(cell=(0, 4, 2), T=7.2, set="Plaster002", tile=3.6, tint=(0.80, 0.62, 0.45)),
    "tram_dplate": dict(cell=(2, 4, 1), T=1.2, set="Metal032", tile=1.2, metal=1.0, proc="dplate"),
    "tram_gate_perf": dict(cell=(3, 4, 1), T=2.4, slot=3, recolor=True, metal=0.10, smooth=0.50, proc="perf"),
    "tram_hut_clad": dict(cell=(4, 4, 1), T=2.4, slot=1, recolor=True, metal=0.50, smooth=0.40, proc="clad"),
}
WEATHERED = {"tram_stone~w": (5, 4, 1), "tram_concrete~w": (6, 4, 1), "tram_roof~w": (7, 4, 1),
             "tram_steel_b~w": (2, 5, 1), "tram_timber~w": (3, 5, 1)}
W_CLASS = {"tram_stone": "stone", "tram_concrete": "concrete", "tram_roof": "metal_roof",
           "tram_steel_b": "paint", "tram_timber": "timber"}
PALETTE_CELL = (4, 5, 1)
PALETTE_GRID = 8
# label rects (col, row, w cells, h cells). Plan 1.4 put both in (0, 6) 2 x 2. R1 measured the base needing 1024 x 760 at
# its targets (dials 120, pictograms 100, boards 200-400 px/m) and the top 1024 x 275. So the top takes two of the spare
# cells (2, 6)-(3, 6) instead of shrinking its boards (deviation, build note section 2)
LABEL_CELLS = {"base": (0, 6, 2, 2), "top": (2, 6, 2, 1)}
# fallback ladder (plan 3.3), one measured step at a time. --tiled-step N applies steps 1..N
LADDER = {1: "roof swatch 8 -> 10 m (96 px/m)", 2: "timber canopy soffits strip along their boards"}

# ---- per-face key remap (plan 6.1) -------------------------------------------------------------------------------
REMAP = {"concrete": "tram_concrete", "stone": "tram_stone", "render": "tram_render", "timber": "tram_timber",
         "dplate": "tram_dplate", "galv": "tram_galv"}
REMAP_ROLE = {"base": {"steel": "tram_steel_b", "roof": "tram_roof"},
              "top": {"steel": "tram_steel_t", "roof": "roof_dark"}}     # the top's hut roof: a fixed dark sheet
GRATE_PREFIXES = ("grate_", "ramp_", "tread_")      # galv parts that are gratings (deck / ramp / stair treads)
GATE_PREFIXES = ("gate_",)                          # berth gate leaves (gatepost_ is not one)
HUT_PARTS = ("hut",)                                # the top's operator hut walls (exact name)
STRIP_KEYS = ("tram_steel_b", "tram_steel_t", "tram_galv")
STRIP_CONCRETE_MAX_M = 0.60
STRIP_ROOF_PREFIXES = ("fascia_",)
STRIP_TIMBER_PREFIXES = ("jhcanopy", "entrycanopy")  # ladder step 2 only
STRIP_SHORT_FRAC = 0.9                               # a strip face may be at most 0.9 T across (plan 2.2 rules)
# labels: the group prefix -> target px/m. The dials and the pictograms stack (one painted rect per group)
LABEL_STACK = ("ctface_",)
LABEL_TARGET = {"ctface_": 120.0, "pictodisc": 100.0, "pictoplinth": 100.0}
LABEL_PPM0 = 100.0            # tile_mesh packs at ONE ppm. Each group's uvtile is pre-scaled by target / LABEL_PPM0

# Q4 weathering by piece: (key, rule). Design frame: Y up, grade Y 0, +Z the base's village front, -Z the line face. A
# weathered piece samples a SINGLE cell (half the clean px/m, plan 1.4), so the rules stay narrow. They allow walls only
# on the back (the line face, -Z: the family's "the back facade plays the weather side", cs2_tilesheet.TileWeather), only
# on TILE-route parts (stacked small parts and strips stay clean), and the roof's low eave row.
WEATHER_RULES = (
    ("tram_stone", "grade_wall"),          # stone walls standing on the grade
    ("tram_concrete", "grade_wall"),       # concrete walls through the grade (base plaza walls, top plinth band)
    ("tram_roof", "eave_row"),             # the roof pieces along the low (village) eave
    ("tram_steel_b", "grade_wall"),        # tiled steel walls on the grade (the annex back wall)
    ("tram_timber", "foot_wall"),          # the lowest timber row of each part (clock-tower shaft splash zone)
)
WEATHER_BACK_NZ = -0.7                     # a wall piece is on the back when its normal's design Z < this
# T6 (R1 measurement): open-backed parts mounted on what is clear `_Gls` glass under --tiled (the C18 walk sees their
# missing back face through the glass from the hall). The code fills their open boundary loops on the LOD0 copy
GLASS_BACKED = ("door_hall_leaf", "door_opcab_leaf", "board_dep")


def register_materials(step=0):
    """The station swatches in cs2_bldg / cs2_tilesheet's lookup tables (in-process, setdefault)."""
    for key, d in SWATCHES.items():
        T = swatch_T(key, step)
        if d.get("set"):
            m = K.BldgMat(d["set"], (d["tile"], d["tile"]), None, metal=float(d.get("metal", 0.0)),
                          tint=d.get("tint"), slot=int(d.get("slot", 0)), recolor=bool(d.get("recolor", False)))
        else:
            m = K.BldgMat(None, None, (0.80, 0.80, 0.80), smooth=float(d["smooth"]), metal=float(d["metal"]),
                          slot=int(d["slot"]), recolor=bool(d["recolor"]))
            TS.FLAT_TILE_M[key] = T          # flat swatches: tile_mesh divides meters by this (T per step)
        K.BLDG_MATERIALS.setdefault(key, m)
    for k, c in W_CLASS.items():
        TS.W_CLASS.setdefault(k, c)


def swatch_T(key, step=0):
    k = TS.base_key(key)
    if k == "tram_roof" and step >= 1:
        return 10.0
    return float(SWATCHES[k]["T"])


def swatch_repeat(key, step=0):
    d = SWATCHES[TS.base_key(key)]
    if not d.get("set"):
        return 1
    r = swatch_T(key, step) / float(d["tile"])
    if abs(r - round(r)) > 1e-9:
        raise ValueError(f"{key}: T {swatch_T(key, step)} is not an integer multiple of the tile {d['tile']}")
    return int(round(r))


def sheet_names(name):
    """(sheet name, owner asset name, letter) for an asset name SkiTram{Base,Top}Station<L>."""
    for pre in ROLE_PREFIX.values():
        if name.startswith(pre):
            L = name[len(pre):]
            return cs2_asset.validate_asset_name(SHEET_PREFIX + L), OWNER_PREFIX + L, L
    raise ValueError(f"{name}: not a tram station asset name")


def sheet_layout(role, palette_keys, A=SHEET_A, step=0):
    """Rects of every swatch / palette cell / the label region and this role's label rect (no pixels)."""
    register_materials(step)
    cell = A // GRID
    out = {"A": A, "layout": "tram", "grid": GRID, "cell": cell, "tiles": {}, "palette": {}, "alias": {},
           "palette_extra": [], "repeats": {}, "step": int(step), "version": SHEET_VERSION}
    for key, (c, r, span) in [(k, d["cell"]) for k, d in SWATCHES.items()] + list(WEATHERED.items()):
        sw = span * cell
        pad = max(8, sw // 32)
        C = sw - 2 * pad
        rep = swatch_repeat(key, step)
        if C % rep:
            raise ValueError(f"{key}: content {C} not divisible by repeat {rep}")
        T = swatch_T(key, step)
        out["tiles"][key] = {"rect_px": [c * cell + pad, r * cell + pad, C], "swatch_px": sw, "pad": pad,
                             "repeat": rep, "T_m": T, "ppm": round(C / T, 3), "cell": [c, r, span]}
        out["repeats"][key] = rep
    pc, pr, ps = PALETTE_CELL
    out["palette_rect"] = [pc * cell, pr * cell, ps * cell]
    out["palette_grid"] = PALETTE_GRID
    pcell = (ps * cell) // PALETTE_GRID
    keys = sorted(palette_keys)
    if len(keys) > PALETTE_GRID ** 2:
        raise RuntimeError(f"palette overflow: {len(keys)} keys")
    for i, k in enumerate(keys):
        out["palette"][k] = {"rect_px": [pc * cell + (i % PALETTE_GRID) * pcell, pr * cell + (i // PALETTE_GRID) * pcell,
                                         pcell]}
    out["label_rects"] = {r_: [c_ * cell, r0 * cell, w_ * cell, h_ * cell]
                          for r_, (c_, r0, w_, h_) in LABEL_CELLS.items()}
    out["label_rect"] = list(out["label_rects"][role])
    out["label_region"] = list(out["label_rect"])
    out["hash"] = layout_hash(out)
    return out


def layout_hash(layout):
    d = {"v": SHEET_VERSION, "tiles": layout["tiles"], "palette": layout["palette"],
         "label_rects": layout["label_rects"], "step": layout["step"],
         "mats": {k: repr(K.BLDG_MATERIALS[k]) for k in SWATCHES}, "w": W_CLASS,
         "smooth_gain": TS.SMOOTH_GAIN}
    return hashlib.sha1(json.dumps(d, sort_keys=True, default=str).encode("utf-8")).hexdigest()[:16]


def check_layout(layout):
    """T11 (pure): no two rects overlap (swatch cells with pads, palette, label region), every T an integer multiple
    of its material tile, the label rects inside the region and apart. Returns {} or {problem: detail}."""
    bad = {}
    rects = []
    for k, t in layout["tiles"].items():
        x0, y0, C = t["rect_px"]
        p = t["pad"]
        rects.append((k, x0 - p, y0 - p, C + 2 * p, C + 2 * p))
        if TS.base_key(k) in SWATCHES and SWATCHES[TS.base_key(k)].get("set"):
            r = t["T_m"] / SWATCHES[TS.base_key(k)]["tile"]
            if abs(r - round(r)) > 1e-9:
                bad[f"{k} T"] = t["T_m"]
    rects.append(("palette",) + tuple(layout["palette_rect"]) + (layout["palette_rect"][2],))
    for r_, lr_ in sorted(layout["label_rects"].items()):
        rects.append((f"labels {r_}",) + tuple(lr_))
    for i in range(len(rects)):
        for j in range(i + 1, len(rects)):
            a, b = rects[i], rects[j]
            if a[1] < b[1] + b[3] and b[1] < a[1] + a[3] and a[2] < b[2] + b[4] and b[2] < a[2] + a[4]:
                bad[f"overlap {a[0]} / {b[0]}"] = [a[1:], b[1:]]
    for r in rects:
        if r[1] < 0 or r[2] < 0 or r[1] + r[3] > layout["A"] or r[2] + r[4] > layout["A"]:
            bad[f"outside {r[0]}"] = r[1:]
    for k, t in layout["tiles"].items():
        if k.endswith(TS.W_SUFFIX) and abs(t["T_m"] - layout["tiles"][TS.base_key(k)]["T_m"]) > 1e-9:
            bad[f"{k} T != clean"] = t["T_m"]
    return bad


# ======================================================================================
# Swatch pixels
# ======================================================================================

def _height_normal(nrm, h, ppm):
    det = cs2_compose.height_to_normal(h.astype(np.float32), ppm)
    return np.asarray(cs2_compose.blend_rnm(nrm.astype(np.float32), det), np.float64)


def _procedural(proc, key, b, n, sm, C, T, seed):
    """Swatch-specific detail on top of cs2_tilesheet._swatch_maps (base linear, normal 0..1, smooth). Rows = v,
    columns = u. Returns (base, nrm, sm, hole mask or None, metal override or None)."""
    ppm = C / T
    s = (np.arange(C) + 0.5) / ppm                     # meters along one axis
    U, V = np.meshgrid(s, s)                           # U varies along columns (u), V along rows (v)
    holes = None
    if proc == "paint":
        # painted steel: low-frequency +-3 % tone, faint orange peel (members strip-stretch: nothing directional)
        tone = TS._pnoise(C, C / 3.0, seed, 2.2)
        b = b * (1.0 + 0.03 * np.clip(tone, -2, 2))[..., None]
        sm = sm + 0.03 * np.clip(TS._pnoise(C, C / 4.0, seed + 1, 2.0), -2, 2)
        n = _height_normal(n, 0.0003 * TS._pnoise(C, C / 90.0, seed + 2, 1.4), ppm)
    elif proc == "seam":
        # today's roof relief (tram_station.detail_specs "roof": corrugation 0.50 m, 10 mm sine, across the slope. R2:
        # K.STANDING_SEAM's grooves read flat in the previews): 16 ribs a swatch, running down the slope (constant u)
        tone = TS._pnoise(C, C / 3.0, seed, 2.2)
        b = b * (1.0 + 0.025 * np.clip(tone, -2, 2))[..., None]
        h = 0.010 * np.sin(2.0 * math.pi * U / 0.50)
        n = _height_normal(n, h, ppm)
    elif proc == "clad":
        pitch = T / 32.0                                # 0.075 m corrugated sheet, ribs vertical (constant u)
        h = 0.002 * np.sin(2.0 * math.pi * U / pitch)   # today's hut relief: 0.076 m, 2 mm (detail_specs "hut")
        n = _height_normal(n, h, ppm)
        b = b * (0.97 + 0.03 * np.sin(2.0 * math.pi * U / pitch))[..., None]
    elif proc == "grate":
        # galvanized grating: bearing bars along v every T / 50 (0.096 m, today 0.09 / 0.11), 0.022 m tops, cross bars
        # every T / 12 (0.40 m). The gaps read dark (opaque: alpha would flip the surface to cutout)
        pb, wb = T / 50.0, 0.022
        pc, wc = T / 12.0, 0.012
        du = np.abs(((U + pb / 2) % pb) - pb / 2)
        dv = np.abs(((V + pc / 2) % pc) - pc / 2)
        bar = du <= wb / 2
        cross = dv <= wc / 2
        top = bar | cross
        h = np.where(top, 0.0, -0.03)
        h = np.where(cross & ~bar, -0.006, h)
        b = np.where(top[..., None], b, b * 0.16)
        sm = np.where(top, sm, sm * 0.6)
        n = _height_normal(n, h, ppm)
    elif proc == "dplate":
        # tread plate: raised lugs on a 0.03 m staggered grid, alternating +-45 deg (0.022 x 0.006 m)
        p = T / 40.0
        iu, iv = np.floor(U / p), np.floor(V / p)
        cu, cv = (U % p) - p / 2, (V % p) - p / 2
        flip = ((iu + iv) % 2) * 2 - 1
        a = (cu + flip * cv) / math.sqrt(2.0)
        c_ = (cu - flip * cv) / math.sqrt(2.0)
        lug = (np.abs(a) <= 0.011) & (np.abs(c_) <= 0.003)
        h = np.where(lug, 0.0015, 0.0)
        n = _height_normal(n, h, ppm)
        b = np.where(lug[..., None], b * 1.10, b)
    elif proc == "perf":
        # perforated panel: staggered 0.05 m pitch, 0.013 m radius holes (dark, unmasked)
        p = T / 48.0
        row = np.floor(V / p)
        uu = U + (row % 2) * p / 2
        cu, cv = (uu % p) - p / 2, (V % p) - p / 2
        r = np.hypot(cu, cv)
        holes = r <= 0.013
        h = np.where(holes, -0.004, 0.0)
        n = _height_normal(n, h, ppm)
        b = np.where(holes[..., None], 0.02, b)
        sm = np.where(holes, 0.15, sm)
    else:
        raise KeyError(proc)
    return np.clip(b, 0, 1), n, np.clip(sm, 0, 1), holes


def swatch_maps(key, C, rep, T, cache_dir=None):
    """(base linear (C,C,3), normal 0..1, metal (C,C), smooth (C,C), ctl (C,C,4), info) of one sheet entry."""
    bk = TS.base_key(key)
    b, n, metal, sm, slot, info = TS._swatch_maps(bk, C, cache_dir, repeat=rep, layout_name="tram")
    seed = int(hashlib.sha1(key.encode()).hexdigest()[:8], 16)
    holes = None
    proc = SWATCHES[bk].get("proc")
    if proc:
        b, n, sm, holes = _procedural(proc, bk, b, n, sm, C, T, seed)
        info["proc"] = proc
    mt = np.full((C, C), float(metal))
    if key.endswith(TS.W_SUFFIX):
        b, mt, sm = TS.weather_overlay(key, b, n, metal, sm, seed)
        info["weathered"] = W_CLASS[bk]
    ctl = np.zeros((C, C, 4))
    if slot:
        ctl[..., slot - 1] = 1.0
        if holes is not None:
            ctl[holes, slot - 1] = 0.0
    info.update({"T_m": T, "ppm": round(C / T, 2), "smooth_mean": round(float(sm.mean()), 3),
                 "base_mean_lin": [round(float(x), 4) for x in b.reshape(-1, 3).mean(0)]})
    return b, n, mt, sm, ctl, info


def palette_look(spec, recolor_lin):
    """(base linear, metal, smooth, slot, emissive linear | None) of a flat material-table entry."""
    slot = int(spec.get("slot", 0) or 0)
    alb = spec.get("albedo")
    if alb is None:
        raise ValueError(f"palette key has no albedo: {spec}")
    base = np.full(3, recolor_lin) if slot else np.asarray(alb[:3], np.float64)
    rough = spec.get("rough")
    smooth = 1.0 - (0.6 if rough is None else float(rough))
    em = spec.get("emissive")
    return base, float(spec.get("metal", 0.0) or 0.0), smooth, slot, (np.asarray(em[:3]) if em is not None else None)


def build_sheet(sdir, sname, layout, palette_specs, label_npz=(), cache_dir=None):
    """Compose the sheet maps (linear) per the layout, paste every label npz, write the five PNGs into `sdir`.
    Returns (files, info)."""
    A = layout["A"]
    base = np.zeros((A, A, 4)); base[..., 3] = 1.0
    nrm = np.zeros((A, A, 3)); nrm[...] = (0.5, 0.5, 1.0)
    msk = np.zeros((A, A, 4))
    ctl = np.zeros((A, A, 4))
    emi = np.zeros((A, A, 3))
    info = {}
    for key in sorted(layout["tiles"]):
        t = layout["tiles"][key]
        x0, y0, C = t["rect_px"]
        pd = t["pad"]
        b, n, mt, sm, cc, inf = swatch_maps(key, C, t["repeat"], t["T_m"], cache_dir)
        wrap = lambda a: np.pad(a, ((pd, pd), (pd, pd)) + ((0, 0),) * (a.ndim - 2), mode="wrap")
        X, Y = x0 - pd, y0 - pd
        TS._put(base[..., :3], X, Y, wrap(b))
        TS._put(nrm, X, Y, wrap(n))
        mm = np.zeros((C, C, 4)); mm[..., 0] = mt; mm[..., 3] = sm
        TS._put(msk, X, Y, wrap(mm))
        TS._put(ctl, X, Y, wrap(cc))
        info[key] = inf
    rec = float(K.srgb_to_linear(cs2_asset.RECOLOR_ALBEDO))
    for key, c in layout["palette"].items():
        cx, cy, s = c["rect_px"]
        b, mt, sm, slot, em = palette_look(palette_specs[key], rec)
        base[cy:cy + s, cx:cx + s, :3] = b
        msk[cy:cy + s, cx:cx + s, 0] = mt
        msk[cy:cy + s, cx:cx + s, 3] = sm
        if slot:
            ctl[cy:cy + s, cx:cx + s, slot - 1] = 1.0
        if em is not None:
            emi[cy:cy + s, cx:cx + s] = em
        info[key] = {"palette": True, "slot": slot, "metal": mt, "smooth": round(sm, 3), "emissive": em is not None}
    lab = palette_look(palette_specs["label"], rec) if "label" in palette_specs else (np.full(3, 0.69), 0.0, 0.4, 0, None)
    for LX, LY, LW, LH in layout["label_rects"].values():
        base[LY:LY + LH, LX:LX + LW, :3] = lab[0]
        msk[LY:LY + LH, LX:LX + LW, 3] = lab[2]
    pasted = []
    for p in label_npz:
        d = np.load(p)
        x, y, w, h = [int(v) for v in d["rect"]]
        sl = (slice(y, y + h), slice(x, x + w))
        base[sl][..., :3] = d["base"]
        msk[sl] = d["msk"]
        ctl[sl] = d["ctl"]
        nrm[sl] = d["nrm"]
        emi[sl] = d["emi"]
        pasted.append(os.path.basename(p))
    lay = dict(layout, maps={"base": base, "nrm": nrm, "msk": msk, "ctl": ctl, "emi": emi}, name=sname, out_dir=sdir)
    files = TS.write_sheet(lay, emissive=True, labels=())
    return files, {"swatches": info, "labels_pasted": pasted}


# ======================================================================================
# Re-mesh: key remap, routes, uvtile, tile_mesh, weather pieces
# ======================================================================================

def design_frame(obj, dx):
    """(P (n_verts, 3), N (n_faces, 3)) in the DESIGN frame (X lift-axis origin, Y up, Z) of an oriented tram mesh (prefab X
    = Blender x, Y = z, Z = -y, the base is shifted by frame_dx after assembly)."""
    me = obj.data
    Mw = np.array(obj.matrix_world, np.float64)
    co = np.zeros(len(me.vertices) * 3); me.vertices.foreach_get("co", co)
    w = co.reshape(-1, 3) @ Mw[:3, :3].T + Mw[:3, 3]
    P = np.stack([w[:, 0] - dx, w[:, 2], -w[:, 1]], 1)
    nr = np.zeros(len(me.polygons) * 3); me.polygons.foreach_get("normal", nr)
    nw = nr.reshape(-1, 3) @ Mw[:3, :3].T
    N = np.stack([nw[:, 0], nw[:, 2], -nw[:, 1]], 1)
    N /= np.maximum(np.linalg.norm(N, axis=1, keepdims=True), 1e-12)
    return P, N


def box_uv(P, n):
    """Unmirrored box projection in the design frame (meters): walls u = the viewer's right, v = up (courses level round
    corners). Floors / roofs u = X, v = -Z from above (+Z from below)."""
    ax = int(np.argmax(np.abs(n)))
    sg = 1.0 if n[ax] >= 0 else -1.0
    if ax == 1:
        return np.stack([P[:, 0], -sg * P[:, 2]], 1)
    if ax == 0:
        return np.stack([-sg * P[:, 2], P[:, 1]], 1)
    return np.stack([sg * P[:, 0], P[:, 1]], 1)


def remap_keys(me, table, keys0, role):
    """Per-face sheet keys (plan 6.1 remap by prefix). Returns (keys list, face key-index array, remap report)."""
    n = len(me.polygons)
    part = np.zeros(n, np.int32); me.attributes["part"].data.foreach_get("value", part)
    mi = np.zeros(n, np.int32); me.polygons.foreach_get("material_index", mi)
    rr = REMAP_ROLE[role]
    out = []
    moved = {}
    for i in range(n):
        k = keys0[mi[i]]
        nm = table[part[i] - 1] if 0 < part[i] <= len(table) else ""
        if nm.startswith(GATE_PREFIXES):
            nk = "tram_gate_perf"
        elif nm in HUT_PARTS and k == "steel":
            nk = "tram_hut_clad"
        elif k == "galv" and nm.startswith(GRATE_PREFIXES):
            nk = "tram_grate"
        else:
            nk = rr.get(k, REMAP.get(k, k))
        if nk != k:
            moved.setdefault(f"{k} -> {nk}", set()).add(nm)
        out.append(nk)
    keys = sorted(set(out))
    idx = np.array([keys.index(k) for k in out], np.int32)
    return keys, idx, {k: sorted(v) for k, v in sorted(moved.items())}


def _face_extents(P, me):
    """(long, short) in-plane extent of every face (PCA of its vertices)."""
    n = len(me.polygons)
    st = np.zeros(n, np.int32); me.polygons.foreach_get("loop_start", st)
    tot = np.zeros(n, np.int32); me.polygons.foreach_get("loop_total", tot)
    lv = np.zeros(len(me.loops), np.int32); me.loops.foreach_get("vertex_index", lv)
    ext = np.zeros((n, 2))
    for i in range(n):
        q = P[lv[st[i]:st[i] + tot[i]]]
        q = q - q.mean(0)
        if len(q) < 2 or np.ptp(q, 0).max() < 1e-9:
            continue
        _u, _s, vt = np.linalg.svd(q, full_matrices=False)
        e = np.ptp(q @ vt.T, 0)
        ext[i] = (e[0], e[1] if len(e) > 1 else 0.0)
    return ext, st, tot, lv


def plan_routes(names_of_face, fkey, ext, keys, step=0, label_prefixes=()):
    """{part: route} for the taxonomy of plan 2.2 and the strip part list. A part on a tiled key strips when it is longer
    than T, every face is thinner than 0.9 T and its key allows strips (painted steel, plain galv, concrete under 0.6 m
    wide, roof fascias, ladder step 2: the timber canopy soffits)."""
    by = {}
    for i, nm in enumerate(names_of_face):
        by.setdefault(nm, []).append(i)
    routes, strips, info = {}, set(), {}
    for nm, fs in sorted(by.items()):
        ks = sorted({keys[fkey[i]] for i in fs})
        if any(nm.startswith(p) for p in label_prefixes):
            routes[nm] = "label"; continue
        tiled = [k for k in ks if k in SWATCHES]
        if not tiled:
            routes[nm] = "palette"; continue
        e = ext[fs]
        long_, short = float(e[:, 0].max()), float(e[:, 1].max())
        k = tiled[0]
        T = swatch_T(k, step)
        ok = len(ks) == 1 and (k in STRIP_KEYS or (k == "tram_concrete" and short <= STRIP_CONCRETE_MAX_M)
                                or (k == "tram_roof" and nm.startswith(STRIP_ROOF_PREFIXES))
                                or (step >= 2 and k == "tram_timber" and nm.startswith(STRIP_TIMBER_PREFIXES)))
        info[nm] = {"keys": ks, "long_m": round(long_, 3), "short_m": round(short, 3), "T": T}
        if long_ > T and ok and short <= STRIP_SHORT_FRAC * T:
            routes[nm] = "strip"; strips.add(nm)
        elif len(tiled) < len(ks):
            routes[nm] = "tile+palette"
        else:
            routes[nm] = "tile"          # "stack" when it ends up uncut (measured after tile_mesh)
    return routes, strips, info


def strip_prefix_check(strips, all_parts, routes):
    """tile_mesh matches strip_prefixes with startswith: every part a strip name also matches must be a strip
    itself (or on no tiled key). Returns {prefix: [unexpected parts]}."""
    bad = {}
    for s in sorted(strips):
        extra = [p for p in all_parts if p != s and p.startswith(s) and routes.get(p) not in ("strip", "palette", "label")]
        if extra:
            bad[s] = extra
    return bad


def label_groups(table):
    """Label group prefixes in tile_mesh order: the stacked groups, the pictograms, then every label_ part by name."""
    g = [p for p in LABEL_STACK if any(n.startswith(p) for n in table)]
    g += [p for p in ("pictodisc", "pictoplinth") if p in table]
    labs = sorted(n for n in table if n.startswith("label_"))
    for a in labs:
        for b in labs:
            if a != b and b.startswith(a):
                raise RuntimeError(f"label part {a!r} is a prefix of {b!r}: tile_mesh would pack them as one")
    return g + labs


def uvtile_for(obj, keys, fkey, table, dx, groups, targets, ts):
    """(n_loops, 2) UVTile per loop: tiled keys = box projection / their material tile (flat swatches: meters, tile_mesh
    divides by FLAT_TILE_M), palette keys meters, label faces in their upright frame (meters, centered per part so a
    stacked group overlaps) pre-scaled by target / LABEL_PPM0."""
    me = obj.data
    P, N = design_frame(obj, dx)
    n = len(me.polygons)
    st = np.zeros(n, np.int32); me.polygons.foreach_get("loop_start", st)
    tot = np.zeros(n, np.int32); me.polygons.foreach_get("loop_total", tot)
    lv = np.zeros(len(me.loops), np.int32); me.loops.foreach_get("vertex_index", lv)
    part = np.zeros(n, np.int32); me.attributes["part"].data.foreach_get("value", part)
    uvt = np.zeros((len(me.loops), 2))
    name_of = lambda p: table[p - 1] if 0 < p <= len(table) else ""
    grp_of = {}
    for p in set(part.tolist()):
        nm = name_of(p)
        g = next((q for q in groups if nm.startswith(q)), None)
        if g is not None:
            grp_of[p] = g
    # label part centers (world) for stacking
    cen = {}
    Mw = np.array(obj.matrix_world, np.float64)
    co = np.zeros(len(me.vertices) * 3); me.vertices.foreach_get("co", co)
    W = co.reshape(-1, 3) @ Mw[:3, :3].T + Mw[:3, 3]
    for i in range(n):
        if part[i] in grp_of:
            cen.setdefault(int(part[i]), []).append(W[lv[st[i]:st[i] + tot[i]]])
    cen = {p: np.concatenate(v).mean(0) for p, v in cen.items()}
    for i in range(n):
        L = slice(st[i], st[i] + tot[i])
        k = keys[fkey[i]]
        p = int(part[i])
        if p in grp_of:
            r, up, _pts = ts._label_frame(obj, me.polygons[i])
            q = W[lv[L]] - cen[p]
            uv = np.stack([q @ np.array(r), q @ np.array(up)], 1)
            uv *= targets[grp_of[p]] / LABEL_PPM0
            if k in SWATCHES and SWATCHES[k].get("set"):
                uv /= TS.tile_m_of(k)
            uvt[L] = uv
            continue
        uv = box_uv(P[lv[L]], N[i])
        if k in SWATCHES and SWATCHES[k].get("set"):
            uv = uv / TS.tile_m_of(k)
        uvt[L] = uv
    return uvt


def _piece_rules(obj, layout, keys, table, dx, step, grid_offset, tile_parts=None):
    """Faces that move to their key's "~w" swatch (WEATHER_RULES), as {rule key: face index array}."""
    me = obj.data
    P, N = design_frame(obj, dx)
    n = len(me.polygons)
    mi = np.zeros(n, np.int32); me.attributes["cs2ts_mat"].data.foreach_get("value", mi)
    part = np.zeros(n, np.int32); me.attributes["part"].data.foreach_get("value", part)
    sf = np.zeros(n, np.int32)
    if me.attributes.get("cs2ts_strip") is not None:
        me.attributes["cs2ts_strip"].data.foreach_get("value", sf)
    st = np.zeros(n, np.int32); me.polygons.foreach_get("loop_start", st)
    tot = np.zeros(n, np.int32); me.polygons.foreach_get("loop_total", tot)
    lv = np.zeros(len(me.loops), np.int32); me.loops.foreach_get("vertex_index", lv)
    fk = np.array([keys[m] for m in mi])
    ymin = np.array([P[lv[st[i]:st[i] + tot[i]], 1].min() for i in range(n)])
    ymax = np.array([P[lv[st[i]:st[i] + tot[i]], 1].max() for i in range(n)])
    zmax = np.array([P[lv[st[i]:st[i] + tot[i]], 2].max() for i in range(n)])
    cenP = np.array([P[lv[st[i]:st[i] + tot[i]]].mean(0) for i in range(n)])
    wall = np.abs(N[:, 1]) < 0.35
    back = N[:, 2] < WEATHER_BACK_NZ
    # pieces: faces of one part / key / plane in one swatch cell (cell from the face centroid in the tile grid)
    cell = np.zeros((n, 2), np.int64)
    for i in range(n):
        k = fk[i]
        if k not in SWATCHES:
            continue
        uv = box_uv(cenP[i:i + 1], N[i])[0] / swatch_T(k, step) + grid_offset
        cell[i] = np.floor(uv + 1e-7)
    pkey = {}
    for i in range(n):
        if fk[i] not in SWATCHES or sf[i] or (tile_parts is not None and int(part[i]) not in tile_parts):
            continue
        kk = (int(part[i]), fk[i], tuple(np.round(N[i], 2)), int(cell[i, 0]), int(cell[i, 1]))
        pkey.setdefault(kk, []).append(i)
    part_ymin, part_zmax = {}, {}
    for i in range(n):
        p = int(part[i])
        part_ymin[p] = min(part_ymin.get(p, 1e9), ymin[i])
        if fk[i] == "tram_roof" and not wall[i] and N[i, 1] > 0.35:
            part_zmax[p] = max(part_zmax.get(p, -1e9), zmax[i])
    out = {}
    for (p, k, _nn, _cu, _cv), fs in pkey.items():
        fs = np.array(fs)
        rule = dict(WEATHER_RULES).get(k)
        if rule is None:
            continue
        lo, hi = float(ymin[fs].min()), float(ymax[fs].max())
        i0 = fs[0]
        hit = False
        if rule == "grade_wall":
            hit = bool(wall[i0]) and bool(back[i0]) and lo <= 0.05 and hi > 0.05
        elif rule == "eave_row":
            hit = (not wall[i0]) and N[i0, 1] > 0.35 and p in part_zmax and float(zmax[fs].max()) >= part_zmax[p] - 0.05
        elif rule == "foot_wall":
            hit = bool(wall[i0]) and bool(back[i0]) and lo <= part_ymin[p] + 0.05
        if hit:
            out.setdefault(k, []).extend(int(f) for f in fs)
    return {k: np.array(sorted(v), np.int64) for k, v in out.items()}


def apply_weather_pieces(obj, layout, keys, table, dx, step, grid_offset, tile_parts=None):
    """Move every rule piece's UVs from the clean content square into its "~w" cell (linear, same relative spot)
    and set cs2ts_w 1. Returns {key: {pieces_faces, area_m2, share}}."""
    me = obj.data
    A = layout["A"]
    sel = _piece_rules(obj, layout, keys, table, dx, step, grid_offset, tile_parts)
    uvl = me.uv_layers[cs2_uv.UNIQUE_LAYER]
    uv = cs2_uv.loop_uvs(uvl) * A
    wf = np.zeros(len(me.polygons), np.int32); me.attributes["cs2ts_w"].data.foreach_get("value", wf)
    ar = np.zeros(len(me.polygons)); me.polygons.foreach_get("area", ar)
    mi = np.zeros(len(me.polygons), np.int32); me.attributes["cs2ts_mat"].data.foreach_get("value", mi)
    rep = {}
    for k, fs in sorted(sel.items()):
        cx, cy, cC = layout["tiles"][k]["rect_px"]
        wx, wy, wC = layout["tiles"][k + TS.W_SUFFIX]["rect_px"]
        s = wC / float(cC)
        for f in fs:
            p = me.polygons[int(f)]
            L = slice(p.loop_start, p.loop_start + p.loop_total)
            q = uv[L]
            if (q[:, 0] < cx - 0.51).any() or (q[:, 0] > cx + cC + 0.51).any() or \
               (q[:, 1] < cy - 0.51).any() or (q[:, 1] > cy + cC + 0.51).any():
                raise RuntimeError(f"weather piece face {f} ({k}) is not inside its clean swatch")
            uv[L] = np.stack([wx + (q[:, 0] - cx) * s, wy + (q[:, 1] - cy) * s], 1)
            wf[int(f)] = 1
        tot = float(ar[np.array([keys[m] == k for m in mi])].sum())
        rep[k] = {"rule": dict(WEATHER_RULES)[k], "faces": int(len(fs)), "area_m2": round(float(ar[fs].sum()), 1),
                  "share": round(float(ar[fs].sum()) / max(tot, 1e-9), 3)}
    cs2_uv.set_loop_uvs(uvl, uv / A)
    me.attributes["cs2ts_w"].data.foreach_set("value", wf)
    me.update()
    return rep


def close_glass_backs(obj, table):
    """Fill every open boundary loop of the GLASS_BACKED parts (their back faces were dropped against the wall,
    which is clear glass under --tiled), the new face oriented away from the part's center. {part: tris added}."""
    ids = {table.index(n) + 1: n for n in table if n.startswith(GLASS_BACKED)}
    if not ids:
        return {}
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    pl = bm.faces.layers.int.get("part")
    out = {}
    for pid, nm in sorted(ids.items()):
        faces = [f for f in bm.faces if f[pl] == pid]
        edges = list({e for f in faces for e in f.edges if len(e.link_faces) == 1})
        pts = [v.co.copy() for f in faces for v in f.verts]
        cen = pts[0] * 0.0
        for q in pts:
            cen += q
        cen /= len(pts)
        new = bmesh.ops.holes_fill(bm, edges=edges, sides=0)["faces"] if edges else []
        for f in new:
            f[pl] = pid
            f.material_index = faces[0].material_index
            f.smooth = faces[0].smooth
            f.normal_update()
            if (f.calc_center_median() - cen).dot(f.normal) < 0:
                f.normal_flip()
        if new:
            out[nm] = sum(len(f.verts) - 2 for f in new)
            continue
        # no fillable loop (a door leaf: its open side is welded into the frame). A back plate = the largest face (the
        # leaf front), reversed, 0.5 mm behind the part's far side along its normal
        big = max(faces, key=lambda f: f.calc_area())
        nrm = big.normal.copy()
        depth = min((v.co - big.verts[0].co).dot(nrm) for f in faces for v in f.verts)
        vs = [bm.verts.new(v.co + nrm * (depth - 0.0005)) for v in reversed(big.verts)]
        f = bm.faces.new(vs)
        f[pl] = pid
        f.material_index = big.material_index
        f.smooth = big.smooth
        f.normal_update()
        out[nm] = len(vs) - 2
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    return out


def open_edge_m(obj, table):
    """{part: boundary edge length (m)} of a mesh (edges with exactly one face)."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    pl = bm.faces.layers.int.get("part")
    out = {}
    for e in bm.edges:
        if len(e.link_faces) == 1:
            f = e.link_faces[0]
            p = f[pl] if pl is not None else 0
            nm = table[p - 1] if 0 < p <= len(table) else "?"
            out[nm] = out.get(nm, 0.0) + e.calc_length()
    bm.free()
    return out


def part_tris(obj, table):
    me = obj.data
    n = len(me.polygons)
    part = np.zeros(n, np.int32); me.attributes["part"].data.foreach_get("value", part)
    tot = np.zeros(n, np.int32); me.polygons.foreach_get("loop_total", tot)
    out = {}
    for p, k in zip(part, tot - 2):
        nm = table[p - 1] if 0 < p <= len(table) else "?"
        out[nm] = out.get(nm, 0) + int(k)
    return out


def class_ppm(obj, A, keys, table, ts):
    """{class (part_kind): {key: {p10, p50, min, area_m2, faces, weathered_m2, strip}}} shipped px/m on the sheet:
    isotropic sqrt(UV area / area) for tile / stack faces, the across (larger singular value) and along density for
    strip faces (the T70 split)."""
    me = obj.data
    n = len(me.polygons)
    uv = cs2_uv.loop_uvs(me.uv_layers[cs2_uv.UNIQUE_LAYER]) * A
    co = np.zeros(len(me.vertices) * 3); me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3) @ np.array(obj.matrix_world)[:3, :3].T
    mi = np.zeros(n, np.int32); me.attributes["cs2ts_mat"].data.foreach_get("value", mi)
    wf = np.zeros(n, np.int32); me.attributes["cs2ts_w"].data.foreach_get("value", wf)
    sf = np.zeros(n, np.int32)
    if me.attributes.get("cs2ts_strip") is not None:
        me.attributes["cs2ts_strip"].data.foreach_get("value", sf)
    part = np.zeros(n, np.int32); me.attributes["part"].data.foreach_get("value", part)
    rows = {}
    for p in me.polygons:
        ls = list(range(p.loop_start, p.loop_start + p.loop_total))
        vs = [me.loops[i].vertex_index for i in ls]
        a3 = 0.0
        au = 0.0
        for j in range(1, len(vs) - 1):
            a3 += 0.5 * np.linalg.norm(np.cross(co[vs[j]] - co[vs[0]], co[vs[j + 1]] - co[vs[0]]))
            d1, d2 = uv[ls[j]] - uv[ls[0]], uv[ls[j + 1]] - uv[ls[0]]
            au += 0.5 * abs(d1[0] * d2[1] - d1[1] * d2[0])
        if a3 < 1e-6:
            continue
        i = p.index
        iso = math.sqrt(au / a3)
        across = along = iso
        if sf[i]:
            e1, e2 = co[vs[1]] - co[vs[0]], co[vs[2]] - co[vs[0]]
            x = e1 / max(np.linalg.norm(e1), 1e-12)
            y = e2 - x * (e2 @ x); y /= max(np.linalg.norm(y), 1e-12)
            E = np.array([[e1 @ x, e2 @ x], [e1 @ y, e2 @ y]])
            if abs(np.linalg.det(E)) > 1e-12:
                U = np.stack([uv[ls[1]] - uv[ls[0]], uv[ls[2]] - uv[ls[0]]], 1)
                sv = np.linalg.svd(U @ np.linalg.inv(E), compute_uv=False)
                across, along = float(sv[0]), float(sv[1])
        nm = table[part[i] - 1] if 0 < part[i] <= len(table) else "?"
        rows.setdefault((ts.part_kind(nm), keys[mi[i]]), []).append((across, along, a3, wf[i], sf[i]))
    out = {}
    for (c, k), r in sorted(rows.items()):
        r = np.array(r)
        clean = r[:, 3] == 0
        rr = r[clean] if clean.any() else r
        q = TS._wq(rr[:, 0], rr[:, 2])
        d = {"p10": round(q(0.1), 1), "p50": round(q(0.5), 1), "min": round(float(rr[:, 0].min()), 1),
             "area_m2": round(float(r[:, 2].sum()), 2), "faces": int(len(r)),
             "weathered_m2": round(float(r[~clean, 2].sum()), 2)}
        if (~clean).any():
            d["w_p50"] = round(TS._wq(r[~clean, 0], r[~clean, 2])(0.5), 1)
        if r[:, 4].any():
            s = r[r[:, 4] > 0]
            d["strip"] = {"across_p50": round(TS._wq(s[:, 0], s[:, 2])(0.5), 1),
                          "along_min": round(float(s[:, 1].min()), 1), "along_p50": round(TS._wq(s[:, 1], s[:, 2])(0.5), 1),
                          "area_m2": round(float(s[:, 2].sum()), 2)}
        out.setdefault(c, {})[k] = d
    return out


def ctl_stats(obj, ctl_png, keys, table=None, label_prefixes=()):
    """{key: [R, G, B] area-weighted ControlMask means at every triangle centroid UV} (T8), label faces out."""
    me = obj.data
    part = np.zeros(len(me.polygons), np.int32); me.attributes["part"].data.foreach_get("value", part)
    lab = {i + 1 for i, n in enumerate(table or ()) if n.startswith(tuple(label_prefixes))}
    img = K._load_raw(ctl_png)
    H, W = img.shape[:2]
    uv = cs2_uv.loop_uvs(me.uv_layers[cs2_uv.UNIQUE_LAYER])
    co = np.zeros(len(me.vertices) * 3); me.vertices.foreach_get("co", co); co = co.reshape(-1, 3)
    mi = np.zeros(len(me.polygons), np.int32); me.attributes["cs2ts_mat"].data.foreach_get("value", mi)
    acc = {}
    for p in me.polygons:
        if int(part[p.index]) in lab:
            continue
        ls = list(range(p.loop_start, p.loop_start + p.loop_total))
        vs = [me.loops[i].vertex_index for i in ls]
        for j in range(1, len(vs) - 1):
            a3 = 0.5 * np.linalg.norm(np.cross(co[vs[j]] - co[vs[0]], co[vs[j + 1]] - co[vs[0]]))
            if a3 < 1e-9:
                continue
            c = (uv[ls[0]] + uv[ls[j]] + uv[ls[j + 1]]) / 3.0
            x = int(np.clip(c[0] * W, 0, W - 1)); y = int(np.clip(c[1] * H, 0, H - 1))
            acc.setdefault(keys[mi[p.index]], []).append(np.r_[img[y, x, :3], a3])
    out = {}
    for k, r in sorted(acc.items()):
        r = np.array(r)
        w = r[:, 3]
        out[k] = [round(float((r[:, c] * w).sum() / w.sum()), 3) for c in range(3)]
    return out


# ======================================================================================
# Labels (dials, pictograms, boards) in the role's label rect
# ======================================================================================

def label_targets(ts, groups):
    t = {}
    for g in groups:
        if g in LABEL_TARGET:
            t[g] = LABEL_TARGET[g]
        elif g in ts.LABEL_KINDS:
            t[g] = float(ts.LABEL_KINDS[g][2])
        else:
            raise KeyError(f"label group {g!r} has no target px/m")
    return t


def paint_labels(ts, obj, table, layout, groups, specs, out_npz):
    """Paint this role's labels into full-size scratch maps with today's painters (cs2_compose.stamp_on_face on the
    UVUnique layer), emissive dials / LED board masked by their painted marks (mask_dial_emissive's rule), and save
    the role's label rect to `out_npz`. Returns {group: {rect_px, painted}}."""
    A = layout["A"]
    me = obj.data
    base = np.zeros((A, A, 4), np.float32); base[..., 3] = 1.0
    ctl = np.zeros((A, A, 4), np.float32)
    LX, LY, LW, LH = layout["label_rect"]
    rec = float(K.srgb_to_linear(cs2_asset.RECOLOR_ALBEDO))
    lab = palette_look(specs["label"], rec)
    base[LY:LY + LH, LX:LX + LW, :3] = lab[0]
    emi = np.zeros((A, A, 3), np.float32)
    msk = np.zeros((A, A, 4), np.float32)
    msk[LY:LY + LH, LX:LX + LW, 3] = lab[2]
    LD = ts._LD()
    out = {}
    for g in groups:
        # R2: the rect first takes its HOST material's mean look (cs2_tilesheet.fill_label_backgrounds' rule), so a
        # painter that leaves texels unpainted (the pictogram disc's corners) shows the plinth / stone, not label white
        rect0 = cs2_compose.uv_bbox_px(me, table, g, A)
        hb = _host_look(_HOST.get(g), layout, specs, rec)
        if rect0 is not None and hb is not None:
            r0, r1, c0, c1 = rect0
            base[r0:r1, c0:c1, :3] = hb[0]
            msk[r0:r1, c0:c1, 0] = hb[1]
            msk[r0:r1, c0:c1, 3] = hb[2]
            ctl[r0:r1, c0:c1, :3] = 0.0
            if hb[3]:
                ctl[r0:r1, c0:c1, hb[3] - 1] = 1.0
        if g.startswith("ctface_"):
            painter = ts.paint_clock_dial
        elif g in ("pictodisc", "pictoplinth"):
            painter = ts.paint_tram_pictogram
        else:
            kind, size, _ppm = ts.LABEL_KINDS[g]
            painter = (ts.trail_board_painter(kind, size) if kind.startswith("trail:")
                       else LD.board_painter(kind, size_m=size, words=ts.LABEL_WORDS.get(g)))
        cs2_compose.stamp_on_face(base, ctl, me, table, g, painter)
        rect = cs2_compose.uv_bbox_px(me, table, g, A)
        out[g] = {"rect_rc": list(rect) if rect else None, "last": getattr(painter, "last", None)}
        if rect is None:
            continue
        r0, r1, c0, c1 = rect
        if not (LX - 1 <= c0 and c1 <= LX + LW + 1 and LY - 1 <= r0 and r1 <= LY + LH + 1):
            raise RuntimeError(f"label {g} rect {rect} outside the role's label rect {layout['label_rect']}")
        em = specs.get(_HOST.get(g), {}).get("emissive")
        if em is not None and g.startswith(ts.EMISSIVE_MASKED):
            lum = K.linear_to_srgb(np.clip(base[r0:r1, c0:c1, :3], 0, 1)) @ np.array((0.2126, 0.7152, 0.0722))
            emi[r0:r1, c0:c1] = np.asarray(em[:3], np.float32) * np.clip((lum - 0.30) / 0.55, 0.0, 1.0)[..., None]
            out[g]["emissive_masked"] = True
    sl = (slice(LY, LY + LH), slice(LX, LX + LW))
    nrm = np.zeros((LH, LW, 3), np.float32); nrm[...] = (0.5, 0.5, 1.0)
    np.savez_compressed(out_npz, rect=np.array([LX, LY, LW, LH]), base=base[sl][..., :3], msk=msk[sl], ctl=ctl[sl],
                        nrm=nrm, emi=emi[sl])
    return out


def _host_look(key, layout, specs, rec):
    """(base linear rgb, metal, smooth, slot) mean look of a label's host key: a swatch's clean mean (a small _swatch_maps
    run of the same material and repeat) or its palette cell. None when unknown."""
    if key is None:
        return None
    if key in SWATCHES:
        t = layout["tiles"][key]
        C = 40 * t["repeat"]
        b, _n, mt, sm, ctl, _i = swatch_maps(key, C, t["repeat"], t["T_m"])
        slot = int(SWATCHES[key].get("slot", 0))
        return b.reshape(-1, 3).mean(0), float(mt.mean()), float(sm.mean()), slot
    if key in specs:
        b, mt, sm, slot, _em = palette_look(specs[key], rec)
        return b, mt, sm, slot
    return None


_HOST = {}          # label group -> its host material key (set in tiled_lod0, read by paint_labels)


# ======================================================================================
# The tiled LOD0 (called from tram_station.main)
# ======================================================================================

def split_glass_and_copy(t, name, table):
    """`_Gls` (DECISIONS 281): the glazepanel_ faces leave `t` onto <name>_Gls (no textures, a unique unwrap), then
    the un-baked copy of `t` that becomes the sheet LOD0. Returns (gls, lod0 copy)."""
    gls = cs2_submesh.split_submesh(t, table, ("glazepanel_",), "Gls") \
        if any(n.startswith("glazepanel_") for n in table) else None        # the top has no hall glazing
    cp = t.copy()
    cp.data = t.data.copy()
    # the material slots' keys, read NOW: bake_unique removes the CS2SRC_ placeholders the copy's slots point at
    cp.data["tram_keys0"] = json.dumps([m.name[len("CS2SRC_"):] if m and m.name.startswith("CS2SRC_") else None
                                        for m in t.data.materials])
    cp.name = cp.data.name = name + "Tiled"
    bpy.context.scene.collection.objects.link(cp)
    return gls, cp


def pane_thickness(gls, table):
    """{glazepanel part: thickness m} = the smallest extent of each pane part (axis-aligned panes)."""
    if gls is None:
        return {}
    me = gls.data
    n = len(me.polygons)
    part = np.zeros(n, np.int32); me.attributes["part"].data.foreach_get("value", part)
    Mw = np.array(gls.matrix_world)
    co = np.zeros(len(me.vertices) * 3); me.vertices.foreach_get("co", co)
    W = co.reshape(-1, 3) @ Mw[:3, :3].T + Mw[:3, 3]
    pts = {}
    for p in me.polygons:
        pts.setdefault(int(part[p.index]), set()).update(p.vertices)
    out = {}
    for pi, vs in pts.items():
        q = W[sorted(vs)]
        out[table[pi - 1]] = round(float(np.ptp(q, 0).min()), 4)
    return out


def palette_specs(ts, role, keys0):
    """{palette key: material-table spec} for this role's flat keys (the top's roof -> roof_dark)."""
    t = ts.materials_for(role, True, [(None, k) for k in keys0])
    out = {}
    for k in keys0:
        nk = REMAP_ROLE[role].get(k, REMAP.get(k, k))
        if nk in SWATCHES or k in ("rock",):
            continue
        out[nk] = dict(t[k])
    if "label" not in out:
        out["label"] = dict(t.get("label") or {"albedo": (0.69, 0.69, 0.69), "rough": 0.6})
    return out


def all_palette_specs(ts, mine, role):
    """Both stations' palette keys (the sheet is one for both): this role's specs + the other role's material
    table and the dressing materials for the keys it uses. A key both carry must have one look."""
    other = "top" if role == "base" else "base"
    t_other = ts.materials_for(other, True, [(None, k) for k in OTHER_KEYS[other]])
    out = {}
    for k in OTHER_KEYS[other]:
        nk = REMAP_ROLE[other].get(k, REMAP.get(k, k))
        if nk in SWATCHES or k in ("rock",):
            continue
        out[nk] = dict(t_other[k])
    for k, v in mine.items():
        if k in out and _look(out[k]) != _look(v):
            raise RuntimeError(f"palette key {k!r}: base and top looks differ ({out[k]} vs {v})")
        out[k] = v
    return out


def _look(s):
    return tuple(round(float(x), 4) for x in (s.get("albedo") or (0, 0, 0))[:3]) + (
        round(float(s.get("metal", 0) or 0), 3), round(float(s.get("rough", 0.6) if s.get("rough") is not None else 0.6), 3),
        int(s.get("slot", 0) or 0), tuple(s.get("emissive") or ()))


# the material keys each station's LOD0 carries (finish build, assembled 2026-10-18, asserted in tiled_lod0)
OTHER_KEYS = {"base": ("clockface", "concrete", "galv", "glass", "label", "lamp_green", "led", "render", "roof", "rope",
                       "safety", "safety_black", "safety_yellow", "steel", "stone", "timber", "trim"),
              "top": ("concrete", "darksteel", "dplate", "galv", "glass", "label", "lamp_red", "roof", "rope", "safety",
                      "steel", "trim", "trim_white", "wheel")}


def write_settings(asset_dir, name, n_lods, tiled):
    """The lodges' rollout settings.json (lodge._tiled_settings_rollout): LOD0 maps are shared from the sheet owner
    (cross-asset sharedAssets, AssetImportPipeline.cs:502-509 1.6.2f1). LOD1 has its own far set (identity entries). LOD2
    shares LOD1's. The owner (the base) carries the sheet as its own LOD0 maps."""
    sh = tiled["sheet"]
    shared = {} if sh["is_owner"] else {f"{name}_{m}.png": f"{sh['shared_prefix']}_{m}.png" for m in MAPS}
    if n_lods >= 1:
        for m in MAPS[:4]:
            shared[f"{name}_LOD1_{m}.png"] = f"{name}_LOD1_{m}.png"
    if n_lods >= 2:
        for m in MAPS[:4]:
            shared[f"{name}_LOD2_{m}.png"] = f"{name}_LOD1_{m}.png"
    settings = {"sharedAssets": shared}
    for lvl in range(1, n_lods + 1):
        settings[f"LOD{lvl}"] = {"mode": "Skip"}
    with open(os.path.join(asset_dir, "settings.json"), "w", encoding="utf-8", newline="\n") as fh:
        json.dump(settings, fh, indent=2)
        fh.write("\n")


def tiled_lod0(ts, far, lod0, role, name, args, asset_dir, bake, table, report):
    """The far bake's maps -> <name>_LOD1_* (emissive dropped: LOD0-only engine-side, DECISIONS 77). `lod0` is re-meshed
    onto the station sheet (remap, routes, tile_mesh, weather pieces), labels are painted into the role's rect, the sheet
    is composed in the workspace (the base, the owner, installs it as its own LOD0 maps), and every R1 measurement goes
    into report["tiled"]. Returns (lod0 renamed to `name`, far)."""
    import time
    t0 = time.perf_counter()
    step = int(getattr(args, "tiled_step", 0) or 0)
    SA = int(getattr(args, "sheet_tex", SHEET_A) or SHEET_A)
    renamed = []
    for f in bake["files"]:
        if f.endswith("_Emissive.png"):
            os.remove(os.path.join(asset_dir, f))
            continue
        dst = f.replace(f"{name}_", f"{name}_LOD1_", 1)
        os.replace(os.path.join(asset_dir, f), os.path.join(asset_dir, dst))
        renamed.append(dst)
    far.name = far.data.name = name + "Far"
    sname, oname, letter = sheet_names(name)
    out_root = os.path.dirname(os.path.abspath(asset_dir))
    sdir = os.path.abspath(args.sheet_dir) if getattr(args, "sheet_dir", None) else os.path.join(out_root, "_" + sname)
    aux = sdir + ".aux"
    os.makedirs(os.path.join(aux, "labels"), exist_ok=True)
    dx = ts.frame_dx(role)
    me = lod0.data
    keys0 = json.loads(me["tram_keys0"])
    del me["tram_keys0"]
    if None in keys0 or tuple(sorted(keys0)) != OTHER_KEYS[role]:
        raise RuntimeError(f"{name}: material keys {sorted(keys0)} != the recorded {OTHER_KEYS[role]} "
                           f"(update OTHER_KEYS: the sheet palette is built from both lists)")
    closed = close_glass_backs(lod0, table)
    # -- remap + routes + uvtile -----------------------------------------------------------------------------------
    keys, fkey, moved = remap_keys(me, table, keys0, role)
    me.materials.clear()
    for k in keys:
        me.materials.append(bpy.data.materials.get("CS2T_" + k) or bpy.data.materials.new("CS2T_" + k))
    me.polygons.foreach_set("material_index", fkey)
    me.update()
    groups = label_groups(table)
    targets = label_targets(ts, groups)
    specs_mine = palette_specs(ts, role, keys0)
    specs = all_palette_specs(ts, specs_mine, role)
    flat = sorted(k for k in specs if k not in SWATCHES)
    layout = sheet_layout(role, flat, SA, step)
    bad = check_layout(layout)
    if bad:
        raise RuntimeError(f"T11 sheet layout: {bad}")
    part = np.zeros(len(me.polygons), np.int32); me.attributes["part"].data.foreach_get("value", part)
    fnames = [table[p - 1] if 0 < p <= len(table) else "" for p in part]
    P, _N = design_frame(lod0, dx)
    ext, _st, _tot, _lv = _face_extents(P, me)
    routes, strips, rinfo = plan_routes(fnames, fkey, ext, keys, step, groups)
    pbad = strip_prefix_check(strips, sorted(set(fnames)), routes)
    for pre, extra in list(pbad.items()):
        # a strip name that prefixes other parts (deckbeam_1 -> deckbeam_10..13): those strip-map too. This is safe when
        # they are on a strip key and every face fits the band (short members map isotropic, plan 2.2)
        ok = all(len(rinfo[e]["keys"]) == 1 and rinfo[e]["keys"][0] in STRIP_KEYS
                 and rinfo[e]["short_m"] <= STRIP_SHORT_FRAC * rinfo[e]["T"] for e in extra if e in rinfo)
        if ok:
            for e in extra:
                routes[e] = "strip (prefix)"
            del pbad[pre]
    if pbad:
        raise RuntimeError(f"strip prefixes match non-strip parts (tile_mesh startswith): {pbad}")
    for g in groups:
        _HOST[g] = keys[np.bincount([fkey[i] for i, n in enumerate(fnames) if n.startswith(g)]).argmax()]
    uvt = uvtile_for(lod0, keys, fkey, table, dx, groups, targets, ts)
    before_tris = part_tris(lod0, table)
    open_before = open_edge_m(lod0, table)
    n_before = cs2_asset.tri_count(lod0)
    grid_offset = 0.5 if ts.base_mode(role) == "plinth" else 0.0
    ground = ts.ENGINE_BASE_PARTS[role] if ts.base_mode(role) == "engine" else ()
    tm = TS.tile_mesh(lod0, uvt, keys, layout, label_prefixes=tuple(groups), label_ppm=LABEL_PPM0,
                      ground_prefixes=ground, weather=None, patch_shift=True, flat_prefixes=(),
                      grid_offset=grid_offset, strip_prefixes=tuple(sorted(strips)), strip_seg_m=None)
    lod0.name = lod0.data.name = name
    after_tris = part_tris(lod0, table)
    for nm, r in routes.items():
        if r == "tile" and after_tris.get(nm, 0) == before_tris.get(nm, 0):
            routes[nm] = "stack"
    tile_ids = {table.index(nm) + 1 for nm, r in routes.items() if r == "tile"}
    wpieces = apply_weather_pieces(lod0, layout, keys, table, dx, step, grid_offset, tile_ids)
    n0 = cs2_asset.tri_count(lod0)
    cap = ts.LOD0_CAP[role]
    open_after = open_edge_m(lod0, table)
    new_open = {nm: round(open_after[nm] - open_before.get(nm, 0.0), 4) for nm in open_after
                if open_after[nm] > open_before.get(nm, 0.0) + 1e-3}
    ground_open = {nm: v for nm, v in new_open.items() if nm.startswith(ground)}
    # -- labels + sheet ---------------------------------------------------------------------------------------------
    lab_npz = os.path.join(aux, "labels", f"{role}.npz")
    labels = paint_labels(ts, lod0, table, layout, groups, specs, lab_npz)
    lab_d = label_px(ts, lod0, table, SA, groups, tm["label_rects"])
    npz = sorted(os.path.join(aux, "labels", f) for f in os.listdir(os.path.join(aux, "labels")) if f.endswith(".npz"))
    files, sinfo = build_sheet(sdir, sname, layout, specs, npz)
    with open(os.path.join(aux, f"{sname}.layout.json"), "w", encoding="utf-8", newline="\n") as fh:
        json.dump({k: v for k, v in layout.items() if k != "maps"}, fh, indent=1, sort_keys=True, default=str)
        fh.write("\n")
    # -- measurements (cs2ts_* attributes still on the mesh) ---------------------------------------------------------
    ppm_key = TS.face_ppm(lod0, SA, keys, split_weather=True)
    sstats = TS.strip_stats(lod0, SA, keys)
    cppm = class_ppm(lod0, SA, keys, table, ts)
    outside = TS.check_uv_cells(lod0, layout, keys)
    ctl = ctl_stats(lod0, os.path.join(sdir, f"{sname}_ControlMask.png"), keys, table, groups)
    smooth = TS.mask_stats(lod0, os.path.join(sdir, f"{sname}_MaskMap.png"), keys)
    tri_by_key = {}
    mi = np.zeros(len(lod0.data.polygons), np.int32); lod0.data.attributes["cs2ts_mat"].data.foreach_get("value", mi)
    for m in mi:
        tri_by_key[keys[m]] = tri_by_key.get(keys[m], 0) + 1
    dclass = {}
    for nm in set(before_tris) | set(after_tris):
        d = after_tris.get(nm, 0) - before_tris.get(nm, 0)
        if d:
            c = ts.part_kind(nm)
            dclass[c] = dclass.get(c, 0) + d
    for a in ("cs2ts_mat", "cs2ts_w", "cs2ts_strip"):
        at = lod0.data.attributes.get(a)
        if at is not None:
            lod0.data.attributes.remove(at)
    lod0.data.materials.clear()
    for lay in [l.name for l in lod0.data.uv_layers if l.name != cs2_uv.UNIQUE_LAYER]:
        lod0.data.uv_layers.remove(lod0.data.uv_layers[lay])
    # T4 on the tiled LOD0 (raises on failure, as on the flag-off LOD0): engine base ring (ground cuts skipped) /
    # the top's plinth band (the half-tile grid offset keeps every cut line off y 0)
    if ts.base_mode(role) == "plinth":
        t4 = {"C6p": ts.check_plinth(lod0, "LOD0")}
    else:
        t4 = {"C6": ts.check_engine_base(lod0, ts.engine_base_parts(role, args))}
    # T6: the C18 walk-around on the tiled LOD0 (glass out: the rays enter the hall). The code records it and the test
    # gates it
    try:
        c18, _w = ts.check_see_through(lod0, role, 0)
        t6 = {"ok": True, "back": c18["back"], "hits": c18["hits"], "rays": c18["rays"],
              "worst": c18["parts"][:5]}
    except ts.ContractError as e:
        t6 = {"ok": False, "error": str(e)[:1500]}
    glz = sum(1 for nm in after_tris if nm.startswith("glazepanel_"))
    is_owner = name == oname
    odir = os.path.join(out_root, oname)
    installed = []
    if is_owner:
        for m in MAPS:
            src = os.path.join(sdir, f"{sname}_{m}.png")
            dst = os.path.join(asset_dir, f"{name}_{m}.png")
            shutil.copyfile(src, dst)
            installed.append(os.path.basename(dst))
    rel = os.path.relpath(odir, asset_dir).replace("\\", "/")
    rc = {}
    for nm, r in routes.items():
        rc.setdefault(r, []).append(nm)
    report["tiled"] = {
        "version": SHEET_VERSION, "step": step, "ladder": {k: v for k, v in LADDER.items() if k <= step},
        "sheet": {"name": sname, "A": SA, "hash": layout["hash"], "owner": oname, "is_owner": is_owner,
                  "workspace": os.path.relpath(sdir, asset_dir).replace("\\", "/"), "files": files,
                  "installed": installed, "shared_prefix": f"{rel}/{oname}", "mb_raw_bc7": TS.bc7_mb(SA, 5),
                  "label_rect": layout["label_rect"], "labels_pasted": sinfo["labels_pasted"],
                  "tiles": {k: {kk: v[kk] for kk in ("rect_px", "T_m", "ppm", "repeat", "cell")}
                            for k, v in layout["tiles"].items()},
                  "palette": {k: v["rect_px"] for k, v in layout["palette"].items()},
                  "swatches": sinfo["swatches"]},
        "far": {"A": int(report.get("far_A") or 0), "files": renamed, "mb_raw_bc7": TS.bc7_mb(int(report.get("far_A") or 0),
                                                                                              len(renamed))},
        "keys": keys, "remap": moved, "routes": {r: sorted(v) for r, v in sorted(rc.items())},
        "route_counts": {r: len(v) for r, v in sorted(rc.items())}, "route_info": rinfo,
        "strip_parts": sorted(strips), "label_groups": groups, "label_targets": targets, "labels": labels,
        "label_px": lab_d,
        "tile_mesh": {k: v for k, v in tm.items() if k != "label_rects"}, "label_rects": tm["label_rects"],
        "lod0_tris_pretile": n_before, "lod0_tris": n0, "cap": cap, "headroom": cap - n0,
        "tris_by_key": tri_by_key, "delta_by_class": dict(sorted(dclass.items(), key=lambda kv: -kv[1])),
        "weather_pieces": wpieces, "uv_outside_region": outside, "ppm_by_key": ppm_key, "strip_by_key": sstats,
        "ppm_by_class": cppm, "ctl_by_key": ctl, "smooth_by_key": smooth,
        "open_edges_new_m": new_open, "open_edges_new_ground_m": ground_open,
        "T4": _jsonable(t4), "C18_tiled": t6, "glass_backed_closed": closed, "glazepanel_parts_main": glz, "lod0_hash": mesh_hash(lod0),
        "seconds": round(time.perf_counter() - t0, 1),
    }
    print(f"[tram] tiled: LOD0 {n_before} -> {n0} tris (cap {cap}, headroom {cap - n0}) on {sname} "
          f"({tm['sliced_tris']} tris sliced, {tm['tjunction_inserts']} T-junction inserts, "
          f"{len(strips)} strip parts, routes {report['tiled']['route_counts']})")
    print(f"[tram] tiled: uv outside region {outside}; new open edges {new_open}; weather pieces "
          + ", ".join(f"{k} {v['faces']} faces {v['area_m2']} m2" for k, v in wpieces.items()))
    for k, v in sorted(ppm_key.items()):
        print(f"[tram] tiled px/m {k:22s} p10 {v['p10']:7.1f} p50 {v['p50']:7.1f} p90 {v['p90']:7.1f} ({v['area_m2']} m2)")
    if outside:
        raise RuntimeError(f"{name}: faces sample outside their sheet region {outside}")
    if n0 > cap:
        ts._fail("C9", f"tiled LOD0 {n0} tris > cap {cap} (pre-tile {n_before}; step {step}); classes "
                       f"{list(report['tiled']['delta_by_class'].items())[:8]}")
    return lod0, far


def _jsonable(o):
    return json.loads(json.dumps(o, default=lambda v: v.tolist() if hasattr(v, "tolist") else str(v)))


def mesh_hash(obj):
    """Rounded vertex coords + polygon vertex lists + part ids + UVUnique (T12: two builds hash identically)."""
    me = obj.data
    h = hashlib.sha1()
    co = np.empty(len(me.vertices) * 3, np.float32); me.vertices.foreach_get("co", co)
    h.update(np.round(co.astype(np.float64), 4).tobytes())
    for p in me.polygons:
        h.update((",".join(str(i) for i in p.vertices) + ";").encode())
    part = np.empty(len(me.polygons), np.int32); me.attributes["part"].data.foreach_get("value", part)
    h.update(part.tobytes())
    uv = np.empty(len(me.loops) * 2, np.float32); me.uv_layers[cs2_uv.UNIQUE_LAYER].data.foreach_get("uv", uv)
    h.update(np.round(uv.astype(np.float64), 6).tobytes())
    return h.hexdigest()[:16]


def label_px(ts, obj, table, A, groups, label_rects):
    """{group: {ppm, target, u_px_per_m, v_px_per_m}} on the sheet (label_density's Jacobian per face)."""
    me = obj.data
    uvl = me.uv_layers[cs2_uv.UNIQUE_LAYER]
    part = np.zeros(len(me.polygons), np.int32); me.attributes["part"].data.foreach_get("value", part)
    out = {}
    for g in groups:
        us, vs = [], []
        for p in me.polygons:
            nm = table[part[p.index] - 1] if 0 < part[p.index] <= len(table) else ""
            if not nm.startswith(g) or p.area < 1e-6:
                continue
            r, up, pts = ts._label_frame(obj, p)
            S = np.array([[q.dot(r), q.dot(up), 1.0] for q in pts], np.float64)
            U = np.array([tuple(uvl.data[l].uv) for l in p.loop_indices], np.float64)
            J = np.linalg.lstsq(S, U, rcond=None)[0]
            us.append(abs(J[0, 0]) * A); vs.append(abs(J[1, 1]) * A)
        L = label_rects.get(g, {})
        out[g] = {"pack_ppm": L.get("ppm"), "u_px_per_m": round(min(us), 1) if us else None,
                  "v_px_per_m": round(min(vs), 1) if vs else None}
    return out
