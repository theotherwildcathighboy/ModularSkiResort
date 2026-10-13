"""
tools_weather_demo.py - before / after demo of the weathering layer (cs2_weather), design
docs/research/art/assets-2026-10/design/weathering-layer.md section 12, extended by the
build report's "Fix pass 1" (intensity ladder, slot_wear pair, wired-equivalent AO).

Run (headless only, game closed):
    blender --background --factory-startup --python-exit-code 1 --python tools_weather_demo.py -- [--all] [--out DIR] [--no-cube] [--no-4096] [--no-ao] [--ao-samples N] [--no-ladder]

1. Kit cube: the test fixture (tests/test_weather.build_kit_cube) baked twice through the
   REAL cs2_bake.bake_unique at A 1024 into <tempdir>/cs2_weather_demo/WeatherKitCube/
   {before,after}/: once with a relief-only inner hook (detail_height bolts and seams +
   compose_maps), once with make_weather_hook(inner, ...). The steel plates carry
   density_w 2.0 so the 18 mm bolts clear the 2-texel floor (cs2_compose.TEXEL_FLOOR).
2. Shipped assets (L2 by default; school, P1, P2 with --all): the review pack's LOD0
   (identity transform, keeps `part`, the part table and UVUnique) + the SHIPPED four maps
   from assets/import/... re-composed through build_fields / compute_masks /
   apply_weathering with prefix roles and compose_contact per base mode (engine mode: the
   shipped albedo already holds compose_maps' 6 % ground band).
   AO (fix pass 1, checker F4): the shipped albedo already has AO multiplied in, so the
   layer cannot tell a dark material from an occluded one; with ao=None the W9 floor then
   blocked most eave grime. The demo therefore re-bakes the LOD0's self-occlusion AO on
   UVUnique (Cycles CPU, seed 0, `--ao-samples`, cached under <tempdir>/cs2_weather_demo/
   _ao/ by a hash of the vertices + UVs) and hands it to the layer exactly as bake_unique
   would: the WIRED-EQUIVALENT look. `--no-ao` reproduces the old ao=None demo.
   The AFTER set is written with the bake's own writer (cs2_asset._write_png_raw after
   cs2_preview._linear_to_srgb; untouched texels keep their shipped sRGB bytes) into
   <tempdir>/cs2_weather_demo/<Name>/after/; BEFORE is a byte copy of the shipped PNGs.
   L2 also gets one frame at snow 0.6 (Q1).
3. Intensity ladder (Q3) per shipped asset: intensity 0.35 (default) / 0.5 / 0.7, the
   arrival 3/4 view and a 2x close-up, plus the school's slot_wear "bounded" | "free" pair
   at 0.5 (Q2). Every intensity is measured: vanilla budget VL1-VL5 and the perceptual
   delta (mean |dL*| and share |dL*| > 2 over the visible texels, slot-tinted with the
   preview tint), eave grime delivered vs designed, the ground-band profile.
4. Optional timing of the layer on the kit cube at A 4096 (synthetic maps).

Outputs (assets/preview/weather/, never assets/import/):
    <Name>_{before,after}_{arrival_3q,back_3q}.png   pbr renders (cs2_preview, noon), 0.35
    <Name>_ab_{arrival_3q,back_3q}.png               before | after side by side
    <Name>_ab_closeup_arrival_3q.png                 central half of 2x renders, 1:1
    <Name>_masks.png    atlas false colour: left R edge, G grime, B rust; right R scuff,
                        G snow (L2: the snow 0.6 run), B protected texels; raw masks before
                        the intensity / role factors, max-pooled to <= 1024 px
    <Name>_crops.png    1:1 atlas crops (tinted sRGB), rows: largest door, wall ground
                        band, roof edge, strongest rust streak; each row before | after
    SkiLodgeHutCommercialLowB_after_snow060_arrival_3q.png
    ladder/<Name>_ladder_arrival_3q.png          BEFORE | 0.35 | 0.50 | 0.70 (labelled)
    ladder/<Name>_ladder_closeup_arrival_3q.png  the same at 2x (central half, 1:1)
    ladder/SkiSchoolCommercialLowA_slotwear_{arrival_3q,closeup_arrival_3q}.png
                                                 BEFORE | BOUNDED 0.50 | FREE 0.50
    weather_demo_summary.json   reports, ladder metrics, crop rects, timings, sha1 check

Imports cs2_weather, cs2_bake, cs2_compose, cs2_preview, cs2_asset and the test fixture;
never cs2_bldg, bldg_recipes, lodge or recipes.* (design W1).
"""

import argparse
import hashlib
import json
import os
import shutil
import sys
import tempfile
import time

import numpy as np

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
for _p in (HERE, os.path.join(HERE, "tests")):
    if _p not in sys.path:
        sys.path.insert(0, _p)

import cs2_asset    # noqa: E402
import cs2_bake     # noqa: E402
import cs2_compose as C   # noqa: E402
import cs2_preview  # noqa: E402
import cs2_weather as W   # noqa: E402
import test_weather as TW   # noqa: E402  (kit-cube fixture; no side effects at import)

REPO = os.path.normpath(os.path.join(HERE, os.pardir, os.pardir))
IMPORT_ROOT = os.path.join(REPO, "assets", "import")
REVIEW_ROOT = os.path.join(REPO, "assets", "preview", "review")
DEFAULT_OUT = os.path.join(REPO, "assets", "preview", "weather")
TMP_ROOT = os.path.join(tempfile.gettempdir(), "cs2_weather_demo")

#: lodge.py:78 VIEWS (first two), copied: this tool never imports lodge (design W1)
VIEWS = (("arrival_3q", (0.8, -1.0, 0.45)), ("back_3q", (-0.8, 1.0, 0.45)))
MAPS = ("BaseColor", "Normal", "MaskMap", "ControlMask")

#: design section 10 / 12: (name, import sub-folder, base mode, spec kwargs)
ASSETS = (
    ("SkiLodgeHutCommercialLowB", os.path.join("lodges", "B"), "plinth", {"ground": "plinth"}),
    ("SkiSchoolCommercialLowA", os.path.join("buildings", "A"), "engine", {}),
    ("SkiPatrolHutCommercialLowA", os.path.join("buildings", "A"), "engine", {}),
    ("SkiPatrolHQCommercialLowA", os.path.join("buildings", "A"), "engine", {}),
)
#: buildings-integration.md section 5 fallback when a pack carries no meta tint
FALLBACK_TINT = {"SkiLodgeHutCommercialLowB": {"R": (0.0625, 0.3125, 0.25),
                                               "G": (0.85, 0.25, 0.10),
                                               "B": (0.30, 0.31, 0.325)}}
CROP_PX = 256
PANEL_MAX = 1024
#: Q3 intensity ladder (0.35 = the default run) and the Q2 slot_wear pair
LADDER = (0.35, 0.5, 0.7)
SLOTWEAR_ASSET = "SkiSchoolCommercialLowA"
SLOTWEAR_I = 0.5
AO_SAMPLES = 32
AO_MARGIN = 4


# --------------------------------------------------------------------------------------
# small helpers
# --------------------------------------------------------------------------------------

def sha1_file(path):
    h = hashlib.sha1()
    with open(path, "rb") as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def shipped_sha1s(names):
    out = {}
    for name, sub, _mode, _kw in ASSETS:
        if name not in names:
            continue
        d = os.path.join(IMPORT_ROOT, sub, name)
        for f in sorted(os.listdir(d)):
            if f.lower().endswith(".png"):
                out[f"{sub}/{name}/{f}".replace("\\", "/")] = sha1_file(os.path.join(d, f))
    return out


def clear_scene():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.images):
        for d in list(coll):
            if d.users == 0:
                coll.remove(d)
    for c in list(bpy.data.collections):
        bpy.data.collections.remove(c)


def jsonable(o):
    if isinstance(o, dict):
        return {str(k): jsonable(v) for k, v in o.items()}
    if isinstance(o, (list, tuple)):
        return [jsonable(v) for v in o]
    if isinstance(o, np.integer):
        return int(o)
    if isinstance(o, np.floating):
        v = float(o)
        return v if np.isfinite(v) else str(v)
    if isinstance(o, float) and not np.isfinite(o):
        return str(o)
    if isinstance(o, np.bool_):
        return bool(o)
    if isinstance(o, np.ndarray):
        return o.tolist()
    return o


def tinted_srgb(base_srgb, ctl, tint):
    """BaseColor x mix(1, slot rgb, ControlMask), linear maths (cs2_preview.py:428-454)."""
    lin = cs2_preview._srgb_to_linear(base_srgb[..., :3])
    for ch, rgb in enumerate(cs2_preview._tint_colours(tint)):
        m = np.clip(ctl[..., ch], 0.0, 1.0)[..., None]
        lin = lin * ((1.0 - m) + np.asarray(rgb, np.float32)[None, None, :] * m)
    out = np.ones(base_srgb.shape[:2] + (4,), np.float32)
    out[..., :3] = cs2_preview._linear_to_srgb(lin)
    return out


def vstack(panels, pad=6):
    w = max(p.shape[1] for p in panels)
    rows = []
    for i, p in enumerate(panels):
        if p.shape[1] != w:
            p = np.concatenate([p, np.full((p.shape[0], w - p.shape[1], 4), (0.5, 0.5, 0.5, 1.0),
                                           np.float32)], axis=1)
        rows.append(p)
        if i < len(panels) - 1:
            rows.append(np.full((pad, w, 4), (0.5, 0.5, 0.5, 1.0), np.float32))
    # images are bottom-up: the first panel goes on TOP
    return np.concatenate(rows[::-1], axis=0)


def maxpool(a, k):
    if k <= 1:
        return a
    A = a.shape[0] // k
    return a[:A * k, :A * k].reshape(A, k, A, k, *a.shape[2:]).max(axis=(1, 3))


# 5 x 7 bitmap glyphs for the panel labels (no font files in a headless factory session)
_GLYPHS = {
    "0": ("01110", "10001", "10011", "10101", "11001", "10001", "01110"),
    "1": ("00100", "01100", "00100", "00100", "00100", "00100", "01110"),
    "2": ("01110", "10001", "00001", "00010", "00100", "01000", "11111"),
    "3": ("11110", "00001", "00001", "01110", "00001", "00001", "11110"),
    "4": ("00010", "00110", "01010", "10010", "11111", "00010", "00010"),
    "5": ("11111", "10000", "11110", "00001", "00001", "10001", "01110"),
    "6": ("00110", "01000", "10000", "11110", "10001", "10001", "01110"),
    "7": ("11111", "00001", "00010", "00100", "01000", "01000", "01000"),
    "8": ("01110", "10001", "10001", "01110", "10001", "10001", "01110"),
    "9": ("01110", "10001", "10001", "01111", "00001", "00010", "01100"),
    ".": ("00000", "00000", "00000", "00000", "00000", "01100", "01100"),
    " ": ("00000",) * 7,
    "A": ("01110", "10001", "10001", "11111", "10001", "10001", "10001"),
    "B": ("11110", "10001", "10001", "11110", "10001", "10001", "11110"),
    "D": ("11110", "10001", "10001", "10001", "10001", "10001", "11110"),
    "E": ("11111", "10000", "10000", "11110", "10000", "10000", "11111"),
    "F": ("11111", "10000", "10000", "11110", "10000", "10000", "10000"),
    "I": ("01110", "00100", "00100", "00100", "00100", "00100", "01110"),
    "L": ("10000", "10000", "10000", "10000", "10000", "10000", "11111"),
    "N": ("10001", "11001", "10101", "10011", "10001", "10001", "10001"),
    "O": ("01110", "10001", "10001", "10001", "10001", "10001", "01110"),
    "R": ("11110", "10001", "10001", "11110", "10100", "10010", "10001"),
    "S": ("01111", "10000", "10000", "01110", "00001", "00001", "11110"),
    "T": ("11111", "00100", "00100", "00100", "00100", "00100", "00100"),
    "U": ("10001", "10001", "10001", "10001", "10001", "10001", "01110"),
    "W": ("10001", "10001", "10001", "10101", "10101", "10101", "01010"),
}


def label(img, text, scale=4, margin=12):
    """Draw `text` (white on a dark box) into the top-left corner of a bottom-up image."""
    text = text.upper()
    gw, gh = 5 * scale, 7 * scale
    w = len(text) * (gw + scale) + scale
    h = gh + 2 * scale
    H = img.shape[0]
    y1 = H - margin
    y0 = y1 - h
    x0 = margin
    if y0 < 0 or x0 + w > img.shape[1]:
        return img
    img[y0:y1, x0:x0 + w, :3] = 0.08
    for i, ch in enumerate(text):
        g = _GLYPHS.get(ch, _GLYPHS[" "])
        gx = x0 + scale + i * (gw + scale)
        for r, row in enumerate(g):
            for c, bit in enumerate(row):
                if bit == "1":
                    yy = y1 - scale - (r + 1) * scale        # glyph row 0 at the top
                    img[yy:yy + scale, gx + c * scale:gx + (c + 1) * scale, :3] = 0.95
    return img


def labelled_strip(paths, labels, out_path, crop=None):
    panels = []
    for p, t in zip(paths, labels):
        a = cs2_preview._load_rgba(p)
        if crop is not None:
            a = a[crop[0]:crop[1], crop[2]:crop[3]].copy()
        panels.append(label(a, t))
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    cs2_preview._write_rgba(out_path, cs2_preview._hstack(panels, pad=8))
    return out_path


# --------------------------------------------------------------------------------------
# AO (wired-equivalent demo, checker F4)
# --------------------------------------------------------------------------------------

def bake_ao(obj, A, samples=AO_SAMPLES, margin=AO_MARGIN):
    """Self-occlusion AO of `obj` on UVUnique at A x A, as cs2_bake.bake_unique bakes it
    (Cycles CPU, seed 0, every other object hidden). Returns ((A, A) float32 raw AO, cached?).
    Cached by a sha1 of the vertex positions, the UVUnique loops, A, samples and margin."""
    me = obj.data
    h = hashlib.sha1()
    co = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertices.foreach_get("co", co)
    h.update(co.tobytes())
    h.update(np.ascontiguousarray(C.read_uvs(me, "UVUnique"), dtype=np.float32).tobytes())
    h.update(f"{A}:{samples}:{margin}".encode())
    d = os.path.join(TMP_ROOT, "_ao")
    os.makedirs(d, exist_ok=True)
    path = os.path.join(d, f"{obj.name}_{A}_{samples}_{h.hexdigest()[:12]}.npy")
    if os.path.exists(path):
        return np.load(path), True
    scene = bpy.context.scene
    state = cs2_bake._SceneState(scene)
    tm = None
    active_uv = me.uv_layers.active.name if me.uv_layers.active is not None else None
    try:
        cs2_bake._object_mode()
        tm = cs2_bake._TargetMaterials(obj, "weather_ao")
        cs2_bake._activate_uv(me, "UVUnique")
        cs2_bake._setup_cycles(scene)
        cs2_bake._isolate(scene, (obj,))
        ao = cs2_bake._bake_into(obj, tm.set_target, "weather_ao", A, True, "AO", samples,
                                 margin)
    finally:
        if tm is not None:
            tm.restore()
        state.restore()
        if active_uv is not None:
            me.uv_layers.active = me.uv_layers[active_uv]
    a = np.ascontiguousarray(ao[..., 0], dtype=np.float32)
    np.save(path, a)
    return a, False


# --------------------------------------------------------------------------------------
# rendering
# --------------------------------------------------------------------------------------

def render_set(obj, set_dir, name, tag, out_dir, tint, views=VIEWS, resolution=(1300, 1000)):
    """cs2_preview pbr renders of the set in `set_dir`, renamed <name>_<tag>_<view>.png."""
    tmp = os.path.join(TMP_ROOT, "_render", name, tag)
    os.makedirs(tmp, exist_ok=True)
    files = cs2_preview.preview(obj, set_dir, name, tmp, modes=("pbr",), views=list(views),
                                tint=tint, resolution=resolution)
    out = {}
    os.makedirs(out_dir, exist_ok=True)
    for vname, _d in views:
        src = os.path.join(tmp, f"{name}_pbr_{vname}.png")
        dst = os.path.join(out_dir, f"{name}_{tag}_{vname}.png")
        shutil.copyfile(src, dst)
        out[vname] = dst
    obj.data.materials.clear()
    print(f"[demo] rendered {name} {tag}: {len(files)} view(s)")
    return out


def ab_sheets(name, before, after, out_dir):
    out = []
    for vname in before:
        a = cs2_preview._load_rgba(before[vname])
        b = cs2_preview._load_rgba(after[vname])
        p = os.path.join(out_dir, f"{name}_ab_{vname}.png")
        cs2_preview._write_rgba(p, cs2_preview._hstack([a, b], pad=8))
        out.append(p)
    return out


#: central half of a 2x render, 1:1 (= a 2x closer look); rows bottom-up
CLOSEUP_CROP = (500, 1500, 650, 1950)


def closeup_render(obj, set_dir, name, tag, tint):
    """arrival_3q at 2x resolution (the 1300 px previews cannot resolve 4 cm edge bands)."""
    v = VIEWS[:1]
    r = render_set(obj, set_dir, name, f"_closeup_{tag}", os.path.join(TMP_ROOT, "_render"),
                   tint, views=v, resolution=(2600, 2000))
    return r[v[0][0]]


def closeup_sheet(name, path_before, path_after, out_dir):
    """Central half of the 2x renders cropped 1:1 (= a 2x closer look), before | after."""
    r0, r1, c0, c1 = CLOSEUP_CROP
    pa = cs2_preview._load_rgba(path_before)[r0:r1, c0:c1]
    pb = cs2_preview._load_rgba(path_after)[r0:r1, c0:c1]
    p = os.path.join(out_dir, f"{name}_ab_closeup_{VIEWS[0][0]}.png")
    cs2_preview._write_rgba(p, cs2_preview._hstack([pa, pb], pad=8))
    return p


# --------------------------------------------------------------------------------------
# sheets: masks and crops
# --------------------------------------------------------------------------------------

def masks_sheet(name, fields, masks, out_dir, snow_masks=None):
    A = fields.A
    cov = np.zeros(A * A, bool)
    cov[fields.idx] = True
    cov = cov.reshape(A, A)
    left = np.zeros((A, A, 4), np.float32)
    right = np.zeros((A, A, 4), np.float32)
    left[..., 3] = right[..., 3] = 1.0
    for ch, key in enumerate(("edge", "grime", "rust")):
        left[..., ch] = W.scatter(fields, masks[key])
    right[..., 0] = W.scatter(fields, masks["scuff"])
    sm = snow_masks["snow"] if snow_masks is not None else masks["snow"]
    right[..., 1] = W.scatter(fields, sm)
    prot = fields.protect | W.scatter(fields, fields.protect_c, False)
    right[..., 2] = np.where(prot & cov, 0.6, 0.0)
    for pan in (left, right):
        pan[~cov, :3] = 0.08
    k = max(1, A // PANEL_MAX)
    sheet = cs2_preview._hstack([maxpool(left, k), maxpool(right, k)], pad=8)
    p = os.path.join(out_dir, f"{name}_masks.png")
    cs2_preview._write_rgba(p, sheet)
    return p


def _best_window(weight, S):
    """Top-left (row, col) of the S x S window with the largest weight sum (or None)."""
    A = weight.shape[0]
    S = min(S, A)
    I = np.zeros((A + 1, A + 1), np.float64)
    I[1:, 1:] = np.cumsum(np.cumsum(weight.astype(np.float64), 0), 1)
    tot = I[S:, S:] - I[:-S, S:] - I[S:, :-S] + I[:-S, :-S]
    if tot.max() <= 0:
        return None
    r, c = np.unravel_index(int(np.argmax(tot)), tot.shape)
    return int(r), int(c)


def crops_sheet(name, fields, masks, base_before, base_after, ctl, tint, out_dir):
    """Rows (top to bottom): largest door, wall ground band, roof edge, rust streak."""
    F = fields
    A = F.A
    S = min(CROP_PX, A)
    role = F.role.astype(np.int64)
    nz = F.ngeo[:, 2].astype(np.float64)
    rid = W.ROLE_ID
    # largest door group by texel count
    door_w = np.zeros(F.N, np.float32)
    best_door = None
    if F.doors:
        groups = {}
        for i, n in enumerate(F.part_names, start=1):
            for d in F.doors:
                if n.startswith(d["id"]):
                    groups.setdefault(d["id"], []).append(i)
        counts = {g: int(np.isin(F.tpart, ps).sum()) for g, ps in groups.items()}
        above = {d["id"] for d in F.doors if d["bottom_z"] >= -0.5}     # not a basement door
        pool = sorted(g for g in counts if g in above) or sorted(counts)
        best_door = max(pool, key=lambda g: counts[g]) if pool else None
        if best_door:
            door_w = np.isin(F.tpart, groups[best_door]).astype(np.float32)
    walls = np.isin(role, [rid["timber"], rid["masonry"], rid["paint"], rid["generic"]]) & (np.abs(nz) < 0.5)
    roofs = np.isin(role, [rid["roof_tile"], rid["paint_metal"]])
    if roofs.any():                     # the two largest roof parts, not canopy bits
        rc_ = np.bincount(F.tpart[roofs], minlength=len(F.part_names) + 1)
        roofs &= np.isin(F.tpart, np.argsort(-rc_, kind="stable")[:2])
    host = role != rid["galv"]
    picks = (("door " + str(best_door), door_w),
             ("wall ground band", masks["grime_ground"] * walls),
             ("roof edge", masks["edge"] * roofs),
             ("rust streak", masks["rust"] * host if (masks["rust"] * host).sum() > 0 else masks["rust"]))
    tb = tinted_srgb(base_before, ctl, tint)
    ta = tinted_srgb(base_after, ctl, tint)
    rows, rects = [], []
    for label_, wc in picks:
        rc = _best_window(W.scatter(F, np.asarray(wc, np.float32)), S)
        if rc is None:
            rects.append({"crop": label_, "rect": None})
            continue
        r, c = rc
        sl = (slice(r, r + S), slice(c, c + S))
        rows.append(cs2_preview._hstack([tb[sl], ta[sl]], pad=6))
        rects.append({"crop": label_, "rect_rows_cols": [r, r + S, c, c + S]})
    p = os.path.join(out_dir, f"{name}_crops.png")
    if rows:
        cs2_preview._write_rgba(p, vstack(rows))
    return p, rects


# --------------------------------------------------------------------------------------
# measurements (fix pass 1)
# --------------------------------------------------------------------------------------

def _grime_d(fields, masks, spec):
    """Designed grime darkening weight d per covered texel (apply_weathering's formula)."""
    F = fields
    role = F.role.astype(np.int64)
    tp = F.tpart.astype(np.int64)
    wgr = masks["grime"] * (1.0 + (W.GROUND_GAIN - 1.0) * masks["grime_ground"])
    d = (float(spec.intensity) * float(spec.grime) * W._coef("grime")[role]
         * F.part_mult[tp, 1] * F.part_scale[tp] * (~F.protect_c) * wgr)
    return np.minimum(d, 1.0)


def eave_check(maps, out, fields, masks, spec):
    """Wall texels more than 1 m above ground_z with cover_h <= 0.4 m (right under an eave
    or overhang): delivered L ratio after / before, the designed grime-only ratio, and the
    share of texels where the W9 floor held the darkening back."""
    F = fields
    nz = F.ngeo[:, 2].astype(np.float64)
    z = F.pos[:, 2].astype(np.float64)
    sel = (~F.protect_c) & (np.abs(nz) < 0.5) & (F.cover_h <= 0.4) & (z >= spec.ground_z + 1.0)
    if not sel.any():
        return None
    LU = W.LUMA.astype(np.float64)
    L0 = maps.base.reshape(-1, maps.base.shape[-1])[F.idx, :3].astype(np.float64) @ LU
    L1 = out.base.reshape(-1, out.base.shape[-1])[F.idx, :3].astype(np.float64) @ LU
    d = _grime_d(F, masks, spec)
    pred = 1.0 - d * W.GRIME_LUMA_DARK
    occ = W._occ(maps.ao, F)
    thr = np.minimum(L0, occ * float(spec.albedo_floor))
    held = (L1 <= thr * (1.0 + 1e-4)) & (pred < 0.999)
    s = sel & (L0 > 1e-6)
    return {"texels": int(s.sum()),
            "delivered_ratio": round(float((L1[s] / L0[s]).mean()), 4),
            "designed_grime_ratio": round(float(pred[s].mean()), 4),
            "floor_held_share": round(float(held[s].mean()), 4),
            "input_L_median": round(float(np.median(L0[s])), 5),
            "occ_median": round(float(np.median(occ[s])), 3)}


def ground_profile(maps, out, fields, spec):
    """Mean L ratio after / before on unprotected wall texels by height above ground_z."""
    F = fields
    nz = F.ngeo[:, 2].astype(np.float64)
    z = F.pos[:, 2].astype(np.float64) - float(spec.ground_z)
    LU = W.LUMA.astype(np.float64)
    L0 = maps.base.reshape(-1, maps.base.shape[-1])[F.idx, :3].astype(np.float64) @ LU
    L1 = out.base.reshape(-1, out.base.shape[-1])[F.idx, :3].astype(np.float64) @ LU
    wall = (~F.protect_c) & (np.abs(nz) < 0.5) & (F.role != W.NONE_ID) & (L0 > 1e-6)
    prof = {}
    for lo, hi in ((0.0, 0.15), (0.15, 0.3), (0.3, 0.45), (0.45, 0.6), (0.8, 1.2)):
        s = wall & (z >= lo) & (z < hi)
        prof[f"{lo:.2f}-{hi:.2f}"] = round(float((L1[s] / L0[s]).mean()), 4) if s.any() else None
    return prof


def summarise(rep):
    v = rep["vanilla"]
    return {"VL1": v["VL1_mean_rel_dL"], "VL2": v["VL2_share_rel_dL_gt_0.15"],
            "VL3": [v["VL3_mean_dsmooth"], v["VL3_max_abs_dsmooth"]], "VL4": v["VL4_max"],
            "ok": v["ok"], "failed": v["failed"], "perceptual": rep.get("perceptual"),
            "perceptual_tinted": rep.get("perceptual_tinted"), "coverage": rep.get("coverage"),
            "eave": rep.get("eave"), "ground_profile": rep.get("ground_profile"),
            "guards": rep.get("guards")}


# --------------------------------------------------------------------------------------
# shipped assets
# --------------------------------------------------------------------------------------

def load_pack_lod0(name):
    path = os.path.join(REVIEW_ROOT, f"{name}.blend")
    oname = f"CS2R_{name}_LOD0"
    cname = f"CS2R_{name}"
    with bpy.data.libraries.load(path, link=False) as (src, dst):
        if cname not in src.collections:
            raise KeyError(f"{path}: no collection {cname}")
        dst.collections = [cname]          # brings its LOD / SUB objects as datablocks
    obj = bpy.data.objects[oname]
    bpy.context.scene.collection.objects.link(obj)   # only LOD0 is in the scene
    col = bpy.data.collections[cname]
    meta = json.loads(col["cs2r_meta_json"]) if "cs2r_meta_json" in col else {}
    m = [list(r) for r in obj.matrix_world]
    if not np.allclose(np.array(m), np.eye(4), atol=1e-6):
        raise RuntimeError(f"{oname}: non-identity transform {m}")
    return obj, meta


def load_shipped(asset_dir, name):
    raw = {}
    for m in MAPS:
        raw[m] = cs2_preview._load_rgba(os.path.join(asset_dir, f"{name}_{m}.png"),
                                       is_data=(m != "BaseColor"))
    base = raw["BaseColor"].copy()
    base[..., :3] = cs2_preview._srgb_to_linear(base[..., :3])
    maps = W.WeatherMaps(base.astype(np.float32), raw["Normal"], raw["MaskMap"],
                         raw["ControlMask"], None)
    return maps, raw


def write_after(set_dir, name, maps_in, out, raw, asset_dir):
    """Write the AFTER set with the bake's writer; untouched BaseColor texels keep their
    shipped sRGB bytes exactly; ControlMask (never written, G5) and Emissive are copied."""
    os.makedirs(set_dir, exist_ok=True)
    A = out.base.shape[0]
    changed = np.any(out.base[..., :3] != maps_in.base[..., :3], axis=-1)
    b = raw["BaseColor"].copy()
    b[changed, :3] = cs2_preview._linear_to_srgb(out.base[changed, :3])
    cs2_asset._write_png_raw(os.path.join(set_dir, f"{name}_BaseColor.png"),
                             f"{name}_BaseColor_after", b, A, False)
    cs2_asset._write_png_raw(os.path.join(set_dir, f"{name}_Normal.png"),
                             f"{name}_Normal_after", np.asarray(out.nrm), A, True)
    cs2_asset._write_png_raw(os.path.join(set_dir, f"{name}_MaskMap.png"),
                             f"{name}_MaskMap_after", np.asarray(out.msk), A, True)
    for m in ("ControlMask", "Emissive"):
        src = os.path.join(asset_dir, f"{name}_{m}.png")
        if os.path.exists(src):
            shutil.copyfile(src, os.path.join(set_dir, f"{name}_{m}.png"))
    return b


def copy_before(set_dir, name, asset_dir):
    os.makedirs(set_dir, exist_ok=True)
    for m in MAPS + ("Emissive",):
        src = os.path.join(asset_dir, f"{name}_{m}.png")
        if os.path.exists(src):
            shutil.copyfile(src, os.path.join(set_dir, f"{name}_{m}.png"))


def weather_maps(obj, A, spec, maps, stamped, compose_contact, tint=None):
    t0 = time.perf_counter()
    fl = W.build_fields(obj.data, A, spec, maps, stamped=stamped)
    t1 = time.perf_counter()
    masks = W.compute_masks(maps, fl, spec, compose_contact=compose_contact)
    t2 = time.perf_counter()
    out = W.apply_weathering(maps, fl, spec, compose_contact=compose_contact, masks=masks)
    t3 = time.perf_counter()
    rep = W.weather_report(maps, out, fl, spec, masks)
    rep["seconds"] = {"fields": round(t1 - t0, 2), "masks": round(t2 - t1, 2),
                      "apply": round(t3 - t2, 2), "total": round(t3 - t0, 2)}
    rep["compose_contact"] = compose_contact
    rep["perceptual_tinted"] = W.perceptual_delta(maps, out, fl, spec,
                                                  tint=cs2_preview._tint_colours(tint))
    rep["eave"] = eave_check(maps, out, fl, masks, spec)
    rep["ground_profile"] = ground_profile(maps, out, fl, spec)
    return fl, masks, out, rep


def _ptxt(rep):
    p = rep["perceptual_tinted"]
    v = rep["vanilla"]
    return (f"VL1 {v['VL1_mean_rel_dL']:.3f} VL2 {v['VL2_share_rel_dL_gt_0.15']:.3f} "
            f"VL4 {v['VL4_max']:.2f} ok {v['ok']} | dL* {p['mean_abs_dLstar']:.2f} "
            f">2 {p['share_dLstar_gt_2']:.3f}")


def demo_shipped(name, sub, mode, kw, out_dir, snow_frame, use_ao=True, ao_samples=AO_SAMPLES,
                 ladder=True):
    print(f"\n[demo] ===== {name} ({mode}) =====")
    clear_scene()
    asset_dir = os.path.join(IMPORT_ROOT, sub, name)
    obj, meta = load_pack_lod0(name)
    tint = (meta.get("meta") or {}).get("tint") or FALLBACK_TINT.get(name) or {}
    tint = {str(k): tuple(float(c) for c in v) for k, v in tint.items()}
    maps, raw = load_shipped(asset_dir, name)
    A = maps.base.shape[0]
    ao_info = None
    if use_ao:
        t0 = time.perf_counter()
        ao, cached = bake_ao(obj, A, ao_samples)
        maps.ao = ao
        fl0 = W.build_fields(obj.data, A, W.WeatherSpec.preset("building", name, **kw), None)
        aoc = ao.reshape(-1)[fl0.idx]
        ao_info = {"samples": ao_samples, "cached": cached,
                   "seconds": round(time.perf_counter() - t0, 1),
                   "mean_covered": round(float(aoc.mean()), 4),
                   "p10_covered": round(float(np.percentile(aoc, 10)), 4)}
        del fl0
        print(f"[demo] {name}: AO {ao_info}")
    table = C.read_part_table(obj.data)
    stamped = [n for n in table if n.startswith(W.LABEL_PREFIXES) and n.endswith("_face")]
    spec = W.WeatherSpec.preset("building", name, **kw)
    cc = mode == "engine"
    fl, masks, out, rep = weather_maps(obj, A, spec, maps, stamped, cc, tint)
    rep["stamped"] = stamped
    rep["tint"] = tint
    rep["spec"] = {"preset": "building", **kw}
    rep["ao"] = ao_info
    d_before = os.path.join(TMP_ROOT, name, "before")
    d_after = os.path.join(TMP_ROOT, name, "after")
    copy_before(d_before, name, asset_dir)
    b_after = write_after(d_after, name, maps, out, raw, asset_dir)
    # round-trip check of the writer: re-read AFTER and compare with what was meant
    chk = {}
    for m, ref in (("Normal", raw["Normal"]), ("MaskMap", raw["MaskMap"])):
        got = cs2_preview._load_rgba(os.path.join(d_after, f"{name}_{m}.png"), True)
        want = np.asarray(out.nrm if m == "Normal" else out.msk)
        same_in = np.all(np.abs(want - ref) < 1e-7, axis=-1)          # texels the layer kept
        chk[m] = {"kept_texels_byte_identical": bool(np.array_equal(got[same_in], ref[same_in])),
                  "max_abs_err_vs_float": round(float(np.abs(got - want).max()), 5)}
    gb = cs2_preview._load_rgba(os.path.join(d_after, f"{name}_BaseColor.png"))
    unchanged = ~np.any(out.base[..., :3] != maps.base[..., :3], axis=-1)
    chk["BaseColor"] = {"kept_texels_byte_identical":
                        bool(np.array_equal(gb[unchanged], raw["BaseColor"][unchanged]))}
    rep["writer_check"] = chk
    before = render_set(obj, d_before, name, "before", out_dir, tint)
    after = render_set(obj, d_after, name, "after", out_dir, tint)
    files = list(before.values()) + list(after.values()) + ab_sheets(name, before, after, out_dir)
    cu = {"before": closeup_render(obj, d_before, name, "before", tint),
          "0.35": closeup_render(obj, d_after, name, "after", tint)}
    files.append(closeup_sheet(name, cu["before"], cu["0.35"], out_dir))
    snow_m = None
    if snow_frame:
        sspec = W.WeatherSpec.preset("building", name, snow=0.6, **kw)
        sfl, snow_m, sout, srep = weather_maps(obj, A, sspec, maps, stamped, cc, tint)
        d_snow = os.path.join(TMP_ROOT, name, "after_snow060")
        write_after(d_snow, name, maps, sout, raw, asset_dir)
        sv = render_set(obj, d_snow, name, "after_snow060", out_dir, tint, views=VIEWS[:1])
        files += list(sv.values())
        rep["snow060"] = {"VL5_snow_share": srep["vanilla"]["VL5_snow_share"],
                          "coverage_snow": srep["coverage"].get("snow"),
                          "seconds": srep["seconds"]}
        del sfl, sout
    files.append(masks_sheet(name, fl, masks, out_dir, snow_m))
    cp, rects = crops_sheet(name, fl, masks, raw["BaseColor"], b_after, raw["ControlMask"],
                            tint, out_dir)
    files.append(cp)
    rep["crops"] = rects
    del fl, masks
    print(f"[demo] {name} @ 0.35: {_ptxt(rep)}; eave {rep['eave']}; seconds {rep['seconds']}")

    # -- Q3 intensity ladder and the Q2 slot_wear pair --------------------------------
    lad = {f"{LADDER[0]:.2f}": summarise(rep)}
    if ladder:
        lad_dir = os.path.join(out_dir, "ladder")
        frames = {"before": before[VIEWS[0][0]], "0.35": after[VIEWS[0][0]]}
        for I in LADDER[1:]:
            ispec = W.WeatherSpec.preset("building", name, intensity=I, **kw)
            ifl, _im, iout, irep = weather_maps(obj, A, ispec, maps, stamped, cc, tint)
            tag = f"i{int(round(I * 100)):03d}"
            d_i = os.path.join(TMP_ROOT, name, f"after_{tag}")
            write_after(d_i, name, maps, iout, raw, asset_dir)
            del ifl, iout
            key = f"{I:.2f}"
            lad[key] = summarise(irep)
            print(f"[demo] {name} @ {key}: {_ptxt(irep)}; eave {irep['eave']}")
            frames[key] = render_set(obj, d_i, name, f"after_{tag}",
                                     os.path.join(TMP_ROOT, "_render"), tint,
                                     views=VIEWS[:1])[VIEWS[0][0]]
            cu[key] = closeup_render(obj, d_i, name, f"after_{tag}", tint)
        order = ["before"] + [f"{I:.2f}" for I in LADDER]
        labels = ["BEFORE"] + [f"{I:.2f}" for I in LADDER]
        files.append(labelled_strip([frames[k] for k in order], labels,
                                    os.path.join(lad_dir, f"{name}_ladder_{VIEWS[0][0]}.png")))
        files.append(labelled_strip([cu[k] for k in order], labels,
                                    os.path.join(lad_dir, f"{name}_ladder_closeup_{VIEWS[0][0]}.png"),
                                    crop=CLOSEUP_CROP))
        if name == SLOTWEAR_ASSET:
            sw = {}
            for I in LADDER:
                fspec = W.WeatherSpec.preset("building", name, intensity=I, slot_wear="free", **kw)
                ffl, _fm, fout, frep = weather_maps(obj, A, fspec, maps, stamped, cc, tint)
                sw[f"{I:.2f}"] = summarise(frep)
                print(f"[demo] {name} FREE @ {I:.2f}: {_ptxt(frep)}")
                if abs(I - SLOTWEAR_I) < 1e-9:
                    d_f = os.path.join(TMP_ROOT, name, "after_free050")
                    write_after(d_f, name, maps, fout, raw, asset_dir)
                    f1 = render_set(obj, d_f, name, "after_free050",
                                    os.path.join(TMP_ROOT, "_render"), tint,
                                    views=VIEWS[:1])[VIEWS[0][0]]
                    f2 = closeup_render(obj, d_f, name, "after_free050", tint)
                    k = f"{SLOTWEAR_I:.2f}"
                    lbl = ["BEFORE", f"BOUNDED {k}", f"FREE {k}"]
                    files.append(labelled_strip(
                        [frames["before"], frames[k], f1], lbl,
                        os.path.join(lad_dir, f"{name}_slotwear_{VIEWS[0][0]}.png")))
                    files.append(labelled_strip(
                        [cu["before"], cu[k], f2], lbl,
                        os.path.join(lad_dir, f"{name}_slotwear_closeup_{VIEWS[0][0]}.png"),
                        crop=CLOSEUP_CROP))
                del ffl, fout
            rep["slot_wear_free"] = sw
    rep["ladder"] = lad
    rep["files"] = [os.path.relpath(f, REPO).replace("\\", "/") for f in files]
    return rep


# --------------------------------------------------------------------------------------
# kit cube through the real bake
# --------------------------------------------------------------------------------------

class ReliefInner:
    """Generator-hook stand-in: detail relief (bolts, seams) + compose_maps part noise."""

    def __init__(self, dspecs):
        self.dspecs = dspecs
        self.height_range = None

    def __call__(self, base, nrm, msk, ctl, ao, mesh, A):
        part_map, ppm_map = C.rasterise_face_fields(mesh, A)
        h = C.detail_height(mesh, A, self.dspecs)
        self.height_range = (float(h.min()), float(h.max()))
        return C.compose_maps(base, nrm, msk, ctl, None, h, ppm_map,
                              {"part_map": part_map, "seed": 0})


def cs2_preview_uvs(obj):
    """The shipped UVUnique loops (the crops / masks assume both bakes packed alike)."""
    return C.read_uvs(obj.data, "UVUnique").copy()


def cube_object():
    obj, table = TW.build_kit_cube("WeatherKitCube", with_uvmap=True)
    me = obj.data
    parts = C.part_indices(me)
    ids = [i + 1 for i, n in enumerate(table) if n.startswith("steelplate")]
    w = np.where(np.isin(parts, ids), 2.0, 1.0).astype(np.float32)
    attr = me.attributes.new("density_w", "FLOAT", "FACE")
    attr.data.foreach_set("value", w)
    return obj


def demo_cube(out_dir, A=1024, ao_samples=48, margin_px=8):
    print("\n[demo] ===== WeatherKitCube (bake_unique) =====")
    clear_scene()
    name = "WeatherKitCube"
    d_before = os.path.join(TMP_ROOT, name, "before")
    d_after = os.path.join(TMP_ROOT, name, "after")
    tint = {"R": tuple(cs2_asset.STEEL_DEFAULT_RGB)}
    obj = cube_object()
    t0 = time.perf_counter()
    cs2_bake.bake_unique(obj, d_before, name, A, TW.kit_sources(), ao_samples=ao_samples,
                         post_compose=ReliefInner(TW.detail_specs()), src_uv="UVMap",
                         margin_px=margin_px)
    t_before = time.perf_counter() - t0
    before = render_set(obj, d_before, name, "before", out_dir, tint)
    cu_before = closeup_render(obj, d_before, name, "before", tint)
    uv_before = cs2_preview_uvs(obj)
    clear_scene()
    obj = cube_object()
    spec = TW.kit_spec()
    hook = W.make_weather_hook(ReliefInner(TW.detail_specs()), spec, mat_keys=TW.KEYS,
                               detail_specs=TW.detail_specs(), stamped=TW.STAMPED,
                               keep_fields=True)
    t0 = time.perf_counter()
    cs2_bake.bake_unique(obj, d_after, name, A, TW.kit_sources(), ao_samples=ao_samples,
                         post_compose=hook, src_uv="UVMap", margin_px=margin_px)
    t_after = time.perf_counter() - t0
    after = render_set(obj, d_after, name, "after", out_dir, tint)
    files = list(before.values()) + list(after.values()) + ab_sheets(name, before, after, out_dir)
    files.append(closeup_sheet(name, cu_before, closeup_render(obj, d_after, name, "after", tint),
                               out_dir))
    rep = dict(hook.report)
    rep["same_uv_layout_both_bakes"] = bool(np.array_equal(uv_before, cs2_preview_uvs(obj)))
    rep["bake_seconds"] = {"before": round(t_before, 2), "after": round(t_after, 2)}
    rep["relief_height_range"] = hook.inner.height_range
    files.append(masks_sheet(name, hook.fields, hook.masks, out_dir))
    raw_b = cs2_preview._load_rgba(os.path.join(d_before, f"{name}_BaseColor.png"))
    raw_a = cs2_preview._load_rgba(os.path.join(d_after, f"{name}_BaseColor.png"))
    ctl = cs2_preview._load_rgba(os.path.join(d_after, f"{name}_ControlMask.png"), True)
    ctl0 = cs2_preview._load_rgba(os.path.join(d_before, f"{name}_ControlMask.png"), True)
    rep["controlmask_identical"] = bool(np.array_equal(ctl, ctl0))
    cp, rects = crops_sheet(name, hook.fields, hook.masks, raw_b, raw_a, ctl, tint, out_dir)
    files.append(cp)
    rep["crops"] = rects
    rep["files"] = [os.path.relpath(f, REPO).replace("\\", "/") for f in files]
    return rep


def time_4096():
    """The layer alone on the kit cube at A 4096 (synthetic maps, rho 320 px/m)."""
    print("\n[demo] ===== timing: kit cube layer at A 4096 =====")
    clear_scene()
    obj, _t = TW.build_kit_cube()
    A = 4096
    spec = TW.kit_spec()
    t0 = time.perf_counter()
    maps, f0 = TW.synth_maps(obj, A)
    t1 = time.perf_counter()
    fl = W.build_fields(obj.data, A, spec, maps, detail_specs=TW.detail_specs(),
                        stamped=TW.STAMPED, reserved=[TW.RESERVED_RECT])
    t2 = time.perf_counter()
    masks = W.compute_masks(maps, fl, spec)
    t3 = time.perf_counter()
    out = W.apply_weathering(maps, fl, spec, masks=masks)
    t4 = time.perf_counter()
    res = {"A": A, "N": fl.N, "fields_MB": round(fl.nbytes() / 1e6, 1),
           "seconds": {"synthetic_maps": round(t1 - t0, 2), "fields": round(t2 - t1, 2),
                       "masks": round(t3 - t2, 2), "apply": round(t4 - t3, 2),
                       "layer_total": round(t4 - t1, 2)}}
    print(f"[demo] 4096: {res}")
    del out, masks, fl, maps, f0
    return res


# --------------------------------------------------------------------------------------
# main
# --------------------------------------------------------------------------------------

def print_table(summary):
    print("\n[demo] perceptual delta (visible texels, slot-tinted) and vanilla budget")
    print(f"  {'asset':<28} {'I':>5} {'mode':<8} {'mean|dL*|':>9} {'>2':>7} {'VL1':>6} "
          f"{'VL2':>6} {'VL4':>5} ok")
    for name, rep in summary["assets"].items():
        rows = [("bounded", k, v) for k, v in (rep.get("ladder") or {}).items()]
        rows += [("free", k, v) for k, v in (rep.get("slot_wear_free") or {}).items()]
        for mode, k, v in rows:
            p = v["perceptual_tinted"]
            print(f"  {name:<28} {k:>5} {mode:<8} {p['mean_abs_dLstar']:>9.3f} "
                  f"{p['share_dLstar_gt_2']:>7.4f} {v['VL1']:>6.3f} {v['VL2']:>6.4f} "
                  f"{v['VL4']:>5.3f} {v['ok']}")


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser(prog="tools_weather_demo.py")
    ap.add_argument("--all", action="store_true", help="also school, P1 and P2")
    ap.add_argument("--out", default=DEFAULT_OUT)
    ap.add_argument("--no-cube", action="store_true")
    ap.add_argument("--no-4096", action="store_true")
    ap.add_argument("--no-ao", action="store_true", help="old demo: ao=None (checker F4)")
    ap.add_argument("--ao-samples", type=int, default=AO_SAMPLES)
    ap.add_argument("--no-ladder", action="store_true")
    args = ap.parse_args(argv)
    if not bpy.app.background:
        raise RuntimeError("tools_weather_demo.py runs headless only (ROUND-SPEC R10)")
    out_dir = os.path.abspath(args.out)
    if os.path.normcase(out_dir).startswith(os.path.normcase(IMPORT_ROOT)):
        raise RuntimeError("the demo never writes under assets/import/")
    os.makedirs(out_dir, exist_ok=True)
    os.makedirs(TMP_ROOT, exist_ok=True)
    names = [a[0] for a in ASSETS] if args.all else [ASSETS[0][0]]
    t0 = time.perf_counter()
    sha_before = shipped_sha1s(names)
    summary = {"blender": bpy.app.version_string, "out": os.path.relpath(out_dir, REPO),
               "tmp": TMP_ROOT, "ao": None if args.no_ao else {"samples": args.ao_samples},
               "ladder": list(LADDER), "assets": {}}
    if not args.no_cube:
        summary["assets"]["WeatherKitCube"] = demo_cube(out_dir)
    for name, sub, mode, kw in ASSETS:
        if name in names:
            summary["assets"][name] = demo_shipped(name, sub, mode, kw, out_dir,
                                                   snow_frame=(name == ASSETS[0][0]),
                                                   use_ao=not args.no_ao,
                                                   ao_samples=args.ao_samples,
                                                   ladder=not args.no_ladder)
    if not args.no_4096:
        summary["timing_4096"] = time_4096()
    sha_after = shipped_sha1s(names)
    summary["shipped_png_sha1"] = {"files": sha_before,
                                   "unchanged": sha_before == sha_after,
                                   "changed": sorted(k for k in sha_before
                                                     if sha_after.get(k) != sha_before[k])}
    summary["seconds_total"] = round(time.perf_counter() - t0, 1)
    p = os.path.join(out_dir, "weather_demo_summary.json")
    with open(p, "w", encoding="utf-8", newline="\n") as fh:
        json.dump(jsonable(summary), fh, indent=1, sort_keys=True)
    print_table(summary)
    if not summary["shipped_png_sha1"]["unchanged"]:
        print(f"FAIL tools_weather_demo: shipped PNGs changed {summary['shipped_png_sha1']['changed']}")
        sys.exit(1)
    # the vanilla proxy budget must hold up to intensity 0.5 (fix pass 1); 0.7 only reports
    bad = []
    for k, v in summary["assets"].items():
        if not v["vanilla"]["ok"]:
            bad.append(f"{k}@{v.get('intensity')}")
        for lk, lv in (v.get("ladder") or {}).items():
            if float(lk) <= 0.5 + 1e-9 and not lv["ok"]:
                bad.append(f"{k}@{lk}")
    oks = {k: v["vanilla"]["ok"] for k, v in summary["assets"].items()}
    if bad:
        print(f"FAIL tools_weather_demo: vanilla budget broken at <= 0.5: {sorted(set(bad))}")
        sys.exit(1)
    print(f"PASS tools_weather_demo: {len(summary['assets'])} asset(s) {oks}; budget WARN-free "
          f"up to 0.5; shipped PNG sha1 unchanged ({len(sha_before)} files); summary {p} "
          f"({summary['seconds_total']} s)")


if __name__ == "__main__":
    main()
