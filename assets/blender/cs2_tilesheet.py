"""
cs2_tilesheet.py - the TILED texture route for procedural buildings (T16 texture-parity probe,
2026-10-16; docs/research/art/texture-parity-2026-10-16.md).

WHY
---
Every building ships ONE unique-UV atlas (cs2_bake.bake_unique): 1,741 m2 of ski school on a
2048^2 map is 20 px/m, and a 6.5 cm brick course is smaller than one texel. Vanilla gets
300-500 px/m on buildings of the same size from ONE main surface whose UVs OVERLAP onto a
4096^2 sheet of tileable material swatches + trims (measured: EU_CommercialHotel01_L1_3x2,
2,017 m2, 4096^2, p50 518 px/m, summed UV area 37.2; ElementarySchool01 99 px/m, UV sum 15.3;
the EU house is the small-building exception, 346 px/m from a unique 4096^2 on 153 m2).
Mod textures import non-VT (full residency), so the vanilla house's "unique 4096^2 per
building" route would cost 12 x 4096^2 for the school; the overlapped-sheet route costs one
shared sheet for the whole family.

WHAT (behind Recipe.tiled / lodge.py --tiled; nothing else calls this module)
    build_sheet(out_dir, name, A=4096, swatch=1024, pad=32, labels=None) -> layout dict
        A family SHEET: 4 x 4 swatches. 14 tiled swatches (one per tiled BLDG_MATERIALS key,
        content C = swatch - 2 pad texels over the material's square tile, wrap-padded so
        bilinear / mips never read a neighbour), one PALETTE swatch (4 x 4 flat cells for
        the flat keys) and one LABEL swatch (per-building painted label rects). Writes
        <name>_{BaseColor,Normal,MaskMap,ControlMask,Emissive}.png (the CS2 map contract:
        BaseColor sRGB alpha 1, Normal OpenGL, MaskMap R metal / A smoothness, ControlMask
        R/G/B slots, A 0).
    tile_mesh(obj, uvtile, keys, layout, A, label_prefixes) -> report
        Replaces obj.data by a re-meshed copy: every face of a tiled key is CLIPPED at the
        integer lines of its UVTile coordinates (world metres / tile) so each piece maps
        inside ONE swatch's content square (main textures import with Clamp wrap, so UVs
        must stay in 0..1: no repeat); T-junctions are closed by inserting every split
        point into every polygon that shares the original edge. Flat keys map into their
        palette cell, label parts get their own rect in the label swatch. One UV layer
        "UVUnique" (cs2_uv.UNIQUE_LAYER), material slots cleared, face attributes copied.
    face_ppm(obj, A, keys_per_face) -> {key: {p10, p50, p90, area_m2}}
        Shipped px/m per material (area-weighted, A x sqrt(uv area / world area)).

Smoothness (MaskMap A) is authored from MEASURED vanilla values (bl_smooth.py, the doc's
section 3): pitched roofs 0.25-0.35, walls 0.26-0.41, trims 0.22-0.44.
"""

import math
import os
import sys

import bpy
import bmesh
import numpy as np

_HERE = os.path.dirname(os.path.abspath(__file__))
if _HERE not in sys.path:
    sys.path.insert(0, _HERE)
import cs2_asset     # noqa: E402
import cs2_bake      # noqa: E402
import cs2_bldg as K  # noqa: E402
import cs2_compose   # noqa: E402
import cs2_uv        # noqa: E402

LUMA = np.array((0.2126, 0.7152, 0.0722))
SHEET_A = 4096
SWATCH = 1024
PAD = 32

# swatch slot (col, row) in units of `swatch`, rows bottom-up (Blender UV v = row)
SWATCHES = {
    "timber_lap": (0, 0), "timber_vboard": (1, 0), "siding_paint": (2, 0), "timber_shake": (3, 0),
    "deck": (0, 1), "timber_heavy": (1, 1), "stone_rubble": (2, 1), "rock": (3, 1),
    "slate": (0, 2), "plaster": (1, 2), "concrete": (2, 2), "galv": (3, 2),
    "metal_roof": (0, 3), "metal_roof_s1": (1, 3),
}
PALETTE_SLOT = (2, 3)
LABEL_SLOT = (3, 3)
PALETTE_GRID = 4

# Flat-colour tiles (BldgMat.set None) that still tile with a procedural detail normal
FLAT_TILE_M = {"metal_roof": 3.0, "metal_roof_s1": 3.0}

# MaskMap A targets, measured on vanilla (texture-parity-2026-10-16.md section 3; area-weighted
# means of EU / NA ResidentialLow01, EU_CommercialLow01, EU_CommercialHotel01, ElementarySchool01,
# Barn01). A CC0 roughness map keeps its variation at SMOOTH_GAIN around the target mean.
SMOOTH_TARGET = {
    "timber_lap": 0.35, "timber_vboard": 0.35, "siding_paint": 0.38, "timber_shake": 0.30,
    "deck": 0.30, "timber_heavy": 0.30, "stone_rubble": 0.25, "rock": 0.25,
    "slate": 0.30, "plaster": 0.30, "concrete": 0.38, "galv": 0.40,
    "metal_roof": 0.33, "metal_roof_s1": 0.33,
    # palette (flat): vanilla "other" (trims, frames) 0.22-0.44; glass_dark: hotel dark wall
    # band 0.69 (glass reads by the _Win sub-mesh, these are opaque dark panels)
    "trim": 0.35, "trim_white": 0.35, "accent": 0.30, "glass_dark": 0.70, "label": 0.30,
    "lamp": 0.45, "ski_black": 0.45, "ski_blue": 0.45, "ski_red": 0.45, "rubber": 0.15,
    "safety_yellow": 0.35, "fire_red": 0.40, "lamp_red": 0.45,
}
SMOOTH_GAIN = 0.5
# metal_roof: vanilla Barn01's corrugated roof is metal 1.0 / smooth 0.35; ours stays a
# painted (dielectric-ish) metal at 0.1, the shine fix is the smoothness (0.45 -> 0.33)
METAL_OVERRIDE = {}
LABEL_PPM = 160.0           # label rects in the label swatch (building signs ~300 px/m moved to sign sets)


def _lin(c):
    return np.asarray(K.srgb_to_linear(K._rgb3(c)), np.float64)


def _load(path):
    return K._load_raw(path)                    # (h, w, 4) float64 bottom-up, raw values


def swatch_px(key, A=SHEET_A, swatch=SWATCH, pad=PAD):
    """(x0, y0, C): content origin (texels, bottom-up) and content size of a swatch."""
    col, row = SWATCHES[key]
    return col * swatch + pad, row * swatch + pad, swatch - 2 * pad


def tile_m_of(key):
    m = K.BLDG_MATERIALS[key]
    if m.set:
        return K.square_tile_m(m)
    return FLAT_TILE_M[key]


def tiled_keys():
    return tuple(sorted(SWATCHES))


def _ribs_height(C, tile_m, spec, along_u):
    """DetailSpec 'ribs' grooves (lines of constant horizontal coordinate) as an (C, C) height
    field in metres. along_u True: the horizontal in-plane axis is u (columns vary along u)."""
    s = (np.arange(C) + 0.5) / C * tile_m
    pitch = float(spec.seams_m)
    w = float(getattr(spec, "seam_width_m", 0.012) or 0.012)
    d = float(getattr(spec, "seam_depth_m", 0.004) or 0.004)
    ph = np.mod(s, pitch)
    dist = np.minimum(ph, pitch - ph)
    prof = np.where(dist < w / 2.0, -d * 0.5 * (1.0 + np.cos(np.pi * dist / (w / 2.0))), 0.0)
    if along_u:
        return np.broadcast_to(prof[None, :], (C, C)).copy()
    return np.broadcast_to(prof[:, None], (C, C)).copy()


def _swatch_maps(key, C, cache_dir=None):
    """Linear base (C,C,3), normal 0..1 (C,C,3), metal, smooth (C,C), slot, info."""
    m = K.BLDG_MATERIALS[key]
    tile = tile_m_of(key)
    ppm = C / tile
    info = {"key": key, "tile_m": round(tile, 4), "ppm": round(ppm, 1), "set": m.set}
    if m.set:
        pf = K.prefilter_set(m.set, K.image_tile_m(m), ppm, cache_dir=cache_dir)
        if pf["N"] != C:
            raise RuntimeError(f"{key}: prefilter N {pf['N']} != content {C}")
        tag = os.path.basename(pf["dir"])
        col = _load(os.path.join(pf["dir"], f"{tag}_Color.png"))[..., :3]
        nrm = _load(os.path.join(pf["dir"], f"{tag}_NormalGL.png"))[..., :3]
        rough = _load(os.path.join(pf["dir"], f"{tag}_Roughness.png"))[..., 0]
        lin = K.srgb_to_linear(col)
        if m.tint is not None:
            tint = np.asarray(K._rgb3(m.tint))
        elif m.target_srgb is not None:
            tint = _lin(m.target_srgb) / np.maximum(np.asarray(pf["mean_lin"]), 1e-6)
        else:
            tint = np.ones(3)
        base = lin * tint
        info["tint_lin"] = [round(float(t), 4) for t in tint]
        sm = 1.0 - rough if m.smooth is None else np.full((C, C), float(m.smooth))
    else:
        base = np.broadcast_to(_lin(m.target_srgb), (C, C, 3)).copy()
        nrm = np.broadcast_to(np.array((0.5, 0.5, 1.0)), (C, C, 3)).copy()
        sm = np.full((C, C), float(m.smooth if m.smooth is not None else 0.4))
    if m.recolor:
        lum = base @ LUMA
        k = float(K.srgb_to_linear(cs2_asset.RECOLOR_ALBEDO)) / max(float(lum.mean()), 1e-6)
        base = np.repeat(np.clip(lum * k, 0, 1)[..., None], 3, -1)
        info["recolor_scale"] = round(k, 4)
    if m.detail is not None and getattr(m.detail, "seam_axes", None) == "ribs" and m.detail.seams_m:
        rot = int(m.uv_rot) % 180
        along_u = (rot == 0)            # planar / box: u is the horizontal in-plane axis
        h = _ribs_height(C, tile, m.detail, along_u)
        det = cs2_compose.height_to_normal(h.astype(np.float32), ppm)
        nrm = cs2_compose.blend_rnm(nrm.astype(np.float32), det)
        info["detail"] = f"ribs {m.detail.seams_m} m, {'columns' if along_u else 'rows'}"
    src_mean = float(sm.mean())
    tgt = SMOOTH_TARGET.get(key, src_mean)
    sm = np.clip(tgt + (sm - src_mean) * SMOOTH_GAIN, 0.0, 1.0)
    info["smooth_src_mean"] = round(src_mean, 3)
    info["smooth_mean"] = round(float(sm.mean()), 3)
    metal = METAL_OVERRIDE.get(key, float(m.metal))
    info["metal"] = metal
    info["slot"] = int(m.slot)
    return base, np.asarray(nrm, np.float64), metal, sm, int(m.slot), info


def _put(dst, x0, y0, block):
    h, w = block.shape[:2]
    dst[y0:y0 + h, x0:x0 + w] = block


def build_sheet(out_dir, name, A=SHEET_A, swatch=SWATCH, pad=PAD, keys=None, cache_dir=None):
    """The family sheet (module doc). `keys` limits the tiled swatches (default: all 14).
    Returns the layout: {"A", "swatch", "pad", "tiles": {key: info + rect}, "palette":
    {key: cell rect}, "label_rect": (x0, y0, size), "maps": {...arrays (linear base)}}."""
    if A != 4 * swatch:
        raise ValueError("sheet is a 4 x 4 swatch grid")
    base = np.zeros((A, A, 4)); base[..., 3] = 1.0     # LINEAR rgb + alpha (stamp_on_face wants 4)
    nrm = np.zeros((A, A, 3)); nrm[..., :] = (0.5, 0.5, 1.0)
    msk = np.zeros((A, A, 4))
    ctl = np.zeros((A, A, 4))
    emi = np.zeros((A, A, 3))
    C = swatch - 2 * pad
    layout = {"A": A, "swatch": swatch, "pad": pad, "C": C, "tiles": {}, "palette": {}}
    for key in sorted(keys or SWATCHES):
        b, n, metal, sm, slot, info = _swatch_maps(key, C, cache_dir)
        col, row = SWATCHES[key]
        X, Y = col * swatch, row * swatch
        wrap = lambda a: np.pad(a, ((pad, pad), (pad, pad)) + ((0, 0),) * (a.ndim - 2), mode="wrap")
        _put(base[..., :3], X, Y, wrap(b))
        _put(nrm, X, Y, wrap(n))
        mm = np.zeros((C, C, 4)); mm[..., 0] = metal; mm[..., 3] = sm
        _put(msk, X, Y, wrap(mm))
        if slot:
            cc = np.zeros((C, C, 4)); cc[..., slot - 1] = 1.0
            _put(ctl, X, Y, wrap(cc))
        info["rect_px"] = [X + pad, Y + pad, C]
        layout["tiles"][key] = info
    # palette: flat keys in a 4 x 4 grid of cells inside the palette swatch
    flat = sorted(k for k, m in K.BLDG_MATERIALS.items() if not m.set and k not in SWATCHES)
    cell = swatch // PALETTE_GRID
    if len(flat) > PALETTE_GRID * PALETTE_GRID:
        raise RuntimeError(f"palette overflow: {len(flat)} flat keys")
    PX, PY = PALETTE_SLOT[0] * swatch, PALETTE_SLOT[1] * swatch
    for i, key in enumerate(flat):
        m = K.BLDG_MATERIALS[key]
        cx, cy = PX + (i % PALETTE_GRID) * cell, PY + (i // PALETTE_GRID) * cell
        alb = _lin(m.target_srgb)
        if m.recolor:
            alb = np.full(3, float(K.srgb_to_linear(cs2_asset.RECOLOR_ALBEDO)))
        base[cy:cy + cell, cx:cx + cell, :3] = alb
        msk[cy:cy + cell, cx:cx + cell, 0] = float(m.metal)
        msk[cy:cy + cell, cx:cx + cell, 3] = SMOOTH_TARGET.get(key, m.smooth if m.smooth is not None else 0.4)
        if m.slot:
            ctl[cy:cy + cell, cx:cx + cell, m.slot - 1] = 1.0
        if m.emissive_srgb is not None:
            emi[cy:cy + cell, cx:cx + cell] = _lin(m.emissive_srgb)
        layout["palette"][key] = {"rect_px": [cx, cy, cell], "smooth": float(msk[cy + 1, cx + 1, 3]),
                                  "metal": float(m.metal), "slot": int(m.slot),
                                  "emissive": m.emissive_srgb is not None}
    LX, LY = LABEL_SLOT[0] * swatch, LABEL_SLOT[1] * swatch
    lab = K.BLDG_MATERIALS["label"]
    base[LY:LY + swatch, LX:LX + swatch, :3] = _lin(lab.target_srgb)
    msk[LY:LY + swatch, LX:LX + swatch, 3] = SMOOTH_TARGET["label"]
    layout["label_rect"] = [LX, LY, swatch]
    layout["maps"] = {"base": base, "nrm": nrm, "msk": msk, "ctl": ctl, "emi": emi}
    layout["name"] = name
    layout["out_dir"] = out_dir
    return layout


def write_sheet(layout, emissive=True):
    """Encode + write the five PNGs (BaseColor sRGB, Emissive sRGB, the rest raw)."""
    A = layout["A"]
    mp = layout["maps"]
    name, out_dir = layout["name"], layout["out_dir"]
    os.makedirs(out_dir, exist_ok=True)

    def rgba(rgb, a=1.0):
        o = np.ones((A, A, 4), np.float32)
        o[..., :3] = np.clip(rgb, 0, 1)
        o[..., 3] = a
        return o
    n = mp["nrm"] * 2 - 1
    n[..., 2] = np.maximum(n[..., 2], 0.05)
    n /= np.maximum(np.linalg.norm(n, axis=-1, keepdims=True), 1e-6)
    msk = np.clip(mp["msk"], 0, 1).astype(np.float32); msk[..., 1] = 0.0; msk[..., 2] = 0.0
    ctl = np.clip(mp["ctl"], 0, 1).astype(np.float32); ctl[..., 3] = 0.0
    over = ctl[..., :3].sum(-1) > 1.0 + 1e-3
    if over.any():
        raise RuntimeError(f"sheet ControlMask not exclusive on {int(over.sum())} texels")
    files = []
    maps = [(rgba(K.linear_to_srgb(mp["base"][..., :3])), "BaseColor", False),
            (rgba(n * 0.5 + 0.5), "Normal", True), (msk, "MaskMap", True), (ctl, "ControlMask", True)]
    if emissive:
        maps.append((rgba(K.linear_to_srgb(mp["emi"])), "Emissive", False))
    for arr, suf, is_data in maps:
        p = os.path.join(out_dir, f"{name}_{suf}.png")
        cs2_asset._write_png_raw(p, f"{name}_{suf}", arr, A, is_data)
        files.append(os.path.basename(p))
    return files


# ======================================================================================
# Geometry: clip tiled faces at tile boundaries, map every face into the sheet
# ======================================================================================

def _face_attrs(me):
    """FACE-domain attributes to carry (name -> (data_type, array))."""
    out = {}
    for a in me.attributes:
        if a.domain != "FACE" or a.name.startswith(".") or a.name in ("sharp_face", "material_index"):
            continue
        if a.data_type == "INT":
            arr = np.zeros(len(me.polygons), np.int32)
        elif a.data_type == "FLOAT":
            arr = np.zeros(len(me.polygons), np.float32)
        elif a.data_type == "BOOLEAN":
            arr = np.zeros(len(me.polygons), bool)
        else:
            continue
        a.data.foreach_get("value", arr)
        out[a.name] = (a.data_type, arr)
    return out


def _clip(poly, axis, c, keep_ge):
    """Sutherland-Hodgman against uv[axis] >= c (keep_ge) or <= c. poly: list of dicts
    {uv: (2,), p: (3,), on: frozenset of original edges it lies on, key: hashable}."""
    out = []
    n = len(poly)
    if n == 0:
        return out
    ins = lambda q: (q["uv"][axis] >= c - 1e-9) if keep_ge else (q["uv"][axis] <= c + 1e-9)
    for i in range(n):
        P, Q = poly[i], poly[(i + 1) % n]
        pi, qi = ins(P), ins(Q)
        if pi:
            out.append(P)
        if pi != qi:
            dp, dq = P["uv"][axis] - c, Q["uv"][axis] - c
            t = dp / (dp - dq)
            uv = P["uv"] + (Q["uv"] - P["uv"]) * t
            uv[axis] = c
            common = P["on"] & Q["on"]
            out.append({"uv": uv, "p": P["p"] + (Q["p"] - P["p"]) * t, "on": common,
                        "edge_t": None, "key": None, "_pq": (P, Q, t)})
    return out


def tile_mesh(obj, uvtile, keys, layout, label_prefixes=(), label_ppm=LABEL_PPM, max_cells=4096,
              ground_prefixes=(), ground_band=0.011):
    """
    Re-mesh `obj` for the sheet (module doc). `uvtile`: (n_loops, 2) UVTile coordinates of
    obj's CURRENT loops (tiles); `keys`: sorted material keys (material_index -> key).
    Returns a report dict (tris before/after, sliced faces, pieces, T-junction inserts).
    """
    me = obj.data
    A = layout["A"]
    C = layout["C"]
    table = __import__("json").loads(me[cs2_asset.PART_TABLE_PROP])
    # -- triangulate a working bmesh, carrying UVTile as a UV layer --------------------
    tmp_uv = me.uv_layers.new(name="CS2TS_tile", do_init=False)
    cs2_uv.set_loop_uvs(tmp_uv, uvtile)
    bm = bmesh.new()
    bm.from_mesh(me)
    me.uv_layers.remove(me.uv_layers["CS2TS_tile"])
    part_l = bm.faces.layers.int.get("part")
    _pn = lambda pi: table[pi - 1] if 1 <= pi <= len(table) else ""
    _lab = lambda f: part_l is not None and any(_pn(f[part_l]).startswith(pre) for pre in label_prefixes)
    uvl = bm.loops.layers.uv["CS2TS_tile"]

    def _convex(f):
        if len(f.loops) == 3:
            return True
        if len(f.loops) > 4:
            return False
        q = [l[uvl].uv for l in f.loops]
        cr = [(q[(i + 1) % 4] - q[i]).cross(q[(i + 2) % 4] - q[(i + 1) % 4]) for i in range(4)]
        return all(c > 1e-12 for c in cr) or all(c < -1e-12 for c in cr)
    # label faces stay whole (oriented painters want their one quad); convex quads are clipped as
    # quads (fewer pieces than their two triangles); everything else -> triangles
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if not _lab(f) and not _convex(f)])
    bm.verts.index_update(); bm.faces.index_update()
    V = np.array([v.co[:] for v in bm.verts], np.float64)
    Mw = np.array(obj.matrix_world, np.float64)
    Zw = V @ Mw[2, :3] + Mw[2, 3]                       # world z (engine-base band test)
    tris, tuv, fmat, fpart, forig = [], [], [], [], []
    for f in bm.faces:
        tris.append([l.vert.index for l in f.loops])
        tuv.append([l[uvl].uv[:] for l in f.loops])
        fmat.append(f.material_index)
        fpart.append(f[part_l] if part_l is not None else 0)
    # face attributes, per triangle (bmesh carries FACE attributes as layers)
    fattr = {}
    for name_, (dt, _arr) in _face_attrs(me).items():
        lay = (bm.faces.layers.int.get(name_) if dt == "INT" else
               bm.faces.layers.float.get(name_) if dt == "FLOAT" else
               bm.faces.layers.bool.get(name_) if dt == "BOOLEAN" else None)
        if lay is None:
            continue
        fattr[name_] = (dt, np.array([f[lay] for f in bm.faces]))
    bm.free()
    tuv = [np.array(t, np.float64) for t in tuv]
    for i, k_ in enumerate(fmat):
        kk = keys[k_]
        if kk in FLAT_TILE_M:                      # projection_specs: flat keys use 1 m per unit
            tuv[i] = tuv[i] / FLAT_TILE_M[kk]
    fmat = np.array(fmat, np.int64); fpart = np.array(fpart, np.int64)
    n_tris0 = len(tris)
    tiles = layout["tiles"]; pal = layout["palette"]
    # -- label rects (one per label prefix, shelf-packed in the label swatch) ------------
    LX, LY, LS = layout["label_rect"]
    part_name = lambda pi: table[pi - 1] if 1 <= pi <= len(table) else ""
    is_label = np.array([any(part_name(int(p)).startswith(pre) for pre in label_prefixes)
                         for p in fpart], bool)
    label_map = {}
    cur_x, cur_y, shelf_h, gut = LX + 8, LY + 8, 0, 8
    for pre in label_prefixes:
        sel = np.array([part_name(int(p)).startswith(pre) for p in fpart], bool)
        if not sel.any():
            continue
        mpt = np.array([tile_m_of(keys[k]) if keys[k] in SWATCHES else 1.0 for k in fmat[sel]])
        uvm = np.concatenate([tuv[i] * m for i, m in zip(np.flatnonzero(sel), mpt)])   # metres
        lo, hi = uvm.min(0), uvm.max(0)
        w_px = int(math.ceil((hi[0] - lo[0]) * label_ppm)) + 1
        h_px = int(math.ceil((hi[1] - lo[1]) * label_ppm)) + 1
        scale = label_ppm
        if w_px > LS - 16 or h_px > LS - 16:
            scale = label_ppm * min((LS - 16) / w_px, (LS - 16) / h_px)
            w_px, h_px = int(w_px * scale / label_ppm), int(h_px * scale / label_ppm)
        if cur_x + w_px > LX + LS - gut:
            cur_x, cur_y, shelf_h = LX + 8, cur_y + shelf_h + gut, 0
        if cur_y + h_px > LY + LS - gut:
            raise RuntimeError("label swatch overflow")
        kk = np.bincount(fmat[sel]).argmax()
        label_map[pre] = dict(origin=(cur_x, cur_y), lo=lo, ppm=scale, size=(w_px, h_px), key=keys[int(kk)])
        cur_x += w_px + gut
        shelf_h = max(shelf_h, h_px)
    # -- per-key palette affine (all faces of a flat key share one map) ----------------
    flat_aff = {}
    for k in sorted(set(fmat.tolist())):
        key = keys[k]
        if key in SWATCHES:
            continue
        if key not in pal:
            raise KeyError(f"tile_mesh: key {key!r} is neither tiled nor in the palette")
        sel = (fmat == k) & ~is_label
        if not sel.any():
            continue
        pts = np.concatenate([tuv[i] for i in np.flatnonzero(sel)])
        lo, hi = pts.min(0), pts.max(0)
        ext = float(max(hi[0] - lo[0], hi[1] - lo[1], 1e-6))
        cx, cy, cell = pal[key]["rect_px"]
        s = (cell * 0.5) / ext                               # texels per UVTile unit
        flat_aff[k] = (np.array((cx + cell * 0.25, cy + cell * 0.25)) - lo * s, s)
    # -- clip ----------------------------------------------------------------------------
    vreg = {}            # vertex key -> output index
    vpos = []
    def vid(key, p):
        i = vreg.get(key)
        if i is None:
            i = len(vpos); vreg[key] = i; vpos.append(np.asarray(p, np.float64))
        return i
    edge_pts = {}        # original edge (a, b) -> {t_q: (vkey)}
    out_polys = []       # (list of (vkey, uvpx, edge (a,b) or None, t or None), tri index)
    n_sliced = n_pieces = 0
    n_ground_skip = [0]
    for ti in range(len(tris)):
        vs = [int(x) for x in tris[ti]]
        k = int(fmat[ti]); key = keys[k]
        uv3 = tuv[ti]
        nvs = len(vs)
        recs = []
        for vi, v in enumerate(vs):
            ons = {tuple(sorted((vs[vi - 1], v))), tuple(sorted((v, vs[(vi + 1) % nvs])))}
            recs.append({"uv": uv3[vi].copy(), "p": V[v].copy(), "on": frozenset(ons),
                         "key": ("o", v), "vert": v})
        pieces = []
        if key in SWATCHES and not is_label[ti]:
            lo = np.floor(uv3.min(0) + 1e-7); hi = np.ceil(uv3.max(0) - 1e-7)
            nx, ny = int(max(hi[0] - lo[0], 1)), int(max(hi[1] - lo[1], 1))
            if nx * ny > max_cells:
                raise RuntimeError(f"tri {ti} ({key}) spans {nx}x{ny} tiles")
            if nx * ny == 1:
                pieces.append((recs, (lo[0], lo[1])))
            else:
                n_sliced += 1
                ground = any(part_name(int(fpart[ti])).startswith(g) for g in ground_prefixes)

                def low_piece(pc):
                    # a piece lying wholly inside the engine-base band would BLOCK the base
                    # edge it touches (cs2_validate.ground_ring_report): never cut one off
                    return len(pc) >= 3 and max(float(r["p"] @ Mw[2, :3] + Mw[2, 3]) for r in pc) <= ground_band + 1e-4

                cuts = []
                for ax, l0, n_ in ((0, lo[0], nx), (1, lo[1], ny)):
                    cs = []
                    for j in range(1, n_):
                        c_ = l0 + j
                        if ground and (low_piece(_clip(recs, ax, c_, True)) or low_piece(_clip(recs, ax, c_, False))):
                            n_ground_skip[0] += 1
                            continue
                        cs.append(c_)
                    cuts.append([None] + cs + [None])
                for ia in range(len(cuts[0]) - 1):
                    strip = recs
                    if cuts[0][ia] is not None:
                        strip = _clip(strip, 0, cuts[0][ia], True)
                    if cuts[0][ia + 1] is not None:
                        strip = _clip(strip, 0, cuts[0][ia + 1], False)
                    for ib in range(len(cuts[1]) - 1):
                        pc = strip
                        if cuts[1][ib] is not None:
                            pc = _clip(pc, 1, cuts[1][ib], True)
                        if cuts[1][ib + 1] is not None:
                            pc = _clip(pc, 1, cuts[1][ib + 1], False)
                        if len(pc) >= 3:
                            cen = np.mean([r["uv"] for r in pc], 0)
                            pieces.append((pc, (math.floor(cen[0]), math.floor(cen[1]))))
        else:
            pieces.append((recs, None))
        tx0 = tiles.get(key, {}).get("rect_px")
        for pc, cell in pieces:
            poly = []
            for r in pc:
                if r.get("key") is None:
                    # new point: on an original edge (t along a->b) or interior (by uv)
                    common = r["on"]
                    if common:
                        ea, eb = sorted(common)[0]
                        P, Q, t = r["_pq"]
                        # parametric t along (ea, eb) from the endpoints' own t
                        tp = _t_on(P, ea, eb, V); tq = _t_on(Q, ea, eb, V)
                        tt = tp + (tq - tp) * t
                        r["key"] = ("e", ea, eb, round(tt, 7))
                        r["edge_t"] = ((ea, eb), tt)
                        r["p"] = V[ea] + (V[eb] - V[ea]) * tt
                    else:
                        r["key"] = ("i", ti, round(float(r["uv"][0]), 7), round(float(r["uv"][1]), 7))
                vi_ = vid(r["key"], r["p"])
                # uv in sheet texels
                if is_label[ti]:
                    pre = next(p for p in label_map if part_name(int(fpart[ti])).startswith(p))
                    L = label_map[pre]
                    mpt = tile_m_of(key) if key in SWATCHES else 1.0
                    uvpx = np.asarray(L["origin"]) + (r["uv"] * mpt - L["lo"]) * L["ppm"]
                elif cell is not None:
                    fr = np.clip(r["uv"] - np.asarray(cell), 0.0, 1.0)
                    uvpx = np.array((tx0[0], tx0[1])) + fr * tx0[2]
                else:
                    off, s = flat_aff[k]
                    uvpx = off + r["uv"] * s
                et = r.get("edge_t")
                if et is None and r["key"][0] == "o":
                    et = None
                poly.append((vi_, uvpx, r))
            # area check (UV and 3D)
            P3 = np.array([vpos[q[0]] for q in poly])
            if len(poly) < 3 or _poly_area3(P3) < 1e-9:
                continue
            out_polys.append((poly, ti))
            n_pieces += 1
            for vi_, _uv, r in poly:
                if r["key"][0] == "e":
                    (ea, eb), tt = r["edge_t"]
                    edge_pts.setdefault((ea, eb), {})[round(tt, 7)] = vi_
    # -- T-junctions: insert every split point of an original edge into every polygon
    #    edge lying on that original edge ----------------------------------------------
    for (ea, eb), d in edge_pts.items():
        d[0.0] = vreg[("o", ea)] if ("o", ea) in vreg else None
        d[1.0] = vreg[("o", eb)] if ("o", eb) in vreg else None
    n_ins = 0
    n_tj_skip = 0
    final = []
    for poly, ti in out_polys:
        if is_label[ti]:
            final.append(([(q[0], q[1]) for q in poly], ti))
            continue
        newp = []
        gpoly = any(part_name(int(fpart[ti])).startswith(g) for g in ground_prefixes)
        n = len(poly)
        for i in range(n):
            vi_, uvp, r = poly[i]
            vj, uvq, s = poly[(i + 1) % n]
            newp.append((vi_, uvp))
            ce = _edge_common(r, s)
            if ce is None:
                continue
            tp = _t_of(r, ce, V); tq = _t_of(s, ce, V)
            pts = edge_pts.get(ce)
            if not pts:
                continue
            lo_, hi_ = min(tp, tq), max(tp, tq)
            mids = sorted((t, v) for t, v in pts.items() if v is not None and lo_ + 1e-6 < t < hi_ - 1e-6
                          and v != vi_ and v != vj)
            if tq < tp:
                mids = mids[::-1]
            zw = lambda q: float(vpos[q] @ Mw[2, :3] + Mw[2, 3])
            on_ground_edge = max(abs(zw(vi_)), abs(zw(vj))) <= 1e-5
            for t, v in mids:
                if gpoly and not on_ground_edge and zw(v) <= ground_band + 1e-4:
                    # an in-band point on a raised edge of a ground part would make an all-in-band
                    # triangle (a BLOCKED engine-base edge): leave that T-junction open
                    n_tj_skip += 1
                    continue
                f = (t - tp) / (tq - tp)
                newp.append((v, uvp + (uvq - uvp) * f))
                n_ins += 1
        final.append((newp, ti))
    # -- build the new mesh -------------------------------------------------------------
    new = bpy.data.meshes.new(me.name + "_tiled")
    nv = len(vpos)
    new.vertices.add(nv)
    new.vertices.foreach_set("co", np.array(vpos, np.float32).ravel())
    loop_tot = np.array([len(p) for p, _ in final], np.int32)
    loop_start = np.concatenate([[0], np.cumsum(loop_tot)[:-1]]).astype(np.int32)
    lv = np.array([q[0] for p, _ in final for q in p], np.int32)
    luv = np.array([q[1] for p, _ in final for q in p], np.float64) / A
    new.loops.add(len(lv))
    new.loops.foreach_set("vertex_index", lv)
    new.polygons.add(len(final))
    new.polygons.foreach_set("loop_start", loop_start)
    new.polygons.foreach_set("loop_total", loop_tot)
    src = np.array([ti for _p, ti in final], np.int64)
    new.update(calc_edges=True)
    lay = new.uv_layers.new(name=cs2_uv.UNIQUE_LAYER)
    cs2_uv.set_loop_uvs(lay, luv)
    for name_, (dt, arr) in fattr.items():
        at = new.attributes.get(name_) or new.attributes.new(name_, dt, "FACE")
        at.data.foreach_set("value", arr[src])
    # material index kept as an attribute for measurement (slots are cleared)
    km = new.attributes.new("cs2ts_mat", "INT", "FACE")
    km.data.foreach_set("value", fmat[src].astype(np.int32))
    for k_, v_ in me.items():
        new[k_] = v_
    old = obj.data
    obj.data = new
    bpy.data.meshes.remove(old)
    new.name = obj.name
    # triangulate (collinear T-junction points make n-gons; the export triangulates anyway)
    bm = bmesh.new(); bm.from_mesh(new)
    lab_l = bm.faces.layers.int.get("part")
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if not (lab_l is not None and any(
        part_name(f[lab_l]).startswith(pre) for pre in label_prefixes))],
        quad_method="BEAUTY", ngon_method="BEAUTY")
    deg = [f for f in bm.faces if f.calc_area() < 1e-10]
    if deg:
        bmesh.ops.delete(bm, geom=deg, context="FACES")
    bm.to_mesh(new); bm.free()
    new.update()
    cs2_asset.shade_by_angle(obj, 66.0, weighted=True)
    return {"tris_before": int(n_tris0), "tris_after": int(len(new.polygons)), "sliced_tris": int(n_sliced),
            "pieces": int(n_pieces), "tjunction_inserts": int(n_ins), "verts": int(len(new.vertices)),
            "label_rects": {p: {"origin_px": [int(v) for v in L["origin"]], "size_px": list(L["size"]),
                                "ppm": round(float(L["ppm"]), 1), "key": L["key"]} for p, L in label_map.items()},
            "degenerate_removed": len(deg), "ground_cuts_skipped": n_ground_skip[0],
            "ground_tjunctions_left": n_tj_skip}


def _poly_area3(P):
    s = np.zeros(3)
    for i in range(1, len(P) - 1):
        s += np.cross(P[i] - P[0], P[i + 1] - P[0])
    return 0.5 * float(np.linalg.norm(s))


def _t_on(rec, ea, eb, V):
    """parameter of a record along original edge (ea, eb): 0 at ea, 1 at eb."""
    k = rec["key"]
    if k is not None and k[0] == "o":
        return 0.0 if k[1] == ea else 1.0
    if rec.get("edge_t") is not None:
        return rec["edge_t"][1]
    d = V[eb] - V[ea]
    return float(np.dot(rec["p"] - V[ea], d) / max(np.dot(d, d), 1e-18))


def _edge_common(r, s):
    c = r["on"] & s["on"]
    if not c:
        return None
    return sorted(c)[0]


def _t_of(rec, edge, V):
    return _t_on(rec, edge[0], edge[1], V)


# ======================================================================================
# Measurement
# ======================================================================================

def face_ppm(obj, A, keys, mat_attr="cs2ts_mat", uv_layer=None, mat_index=None):
    """{key: {p10, p50, p90, area_m2, n}} shipped px/m per material on `uv_layer`."""
    me = obj.data
    lay = me.uv_layers[uv_layer] if uv_layer else (me.uv_layers.get(cs2_uv.UNIQUE_LAYER) or me.uv_layers.active)
    uv = cs2_uv.loop_uvs(lay)
    co = np.empty(len(me.vertices) * 3, np.float32); me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3).astype(np.float64)
    M = np.array(obj.matrix_world)
    co = co @ M[:3, :3].T
    if mat_index is None:
        mat_index = np.zeros(len(me.polygons), np.int32)
        me.attributes[mat_attr].data.foreach_get("value", mat_index)
    out = {}
    ppm = np.zeros(len(me.polygons)); area = np.zeros(len(me.polygons))
    for p in me.polygons:
        vs = [me.loops[i].vertex_index for i in range(p.loop_start, p.loop_start + p.loop_total)]
        ls = list(range(p.loop_start, p.loop_start + p.loop_total))
        a3 = 0.0; au = 0.0
        for i in range(1, len(vs) - 1):
            a3 += 0.5 * np.linalg.norm(np.cross(co[vs[i]] - co[vs[0]], co[vs[i + 1]] - co[vs[0]]))
            d1 = uv[ls[i]] - uv[ls[0]]; d2 = uv[ls[i + 1]] - uv[ls[0]]
            au += 0.5 * abs(d1[0] * d2[1] - d1[1] * d2[0])
        area[p.index] = a3
        ppm[p.index] = A * math.sqrt(au / a3) if a3 > 1e-9 else 0.0
    for k in sorted(set(int(x) for x in mat_index)):
        sel = (mat_index == k) & (area > 1e-9)
        if not sel.any():
            continue
        x, w = ppm[sel], area[sel]
        o = np.argsort(x); xs, ws = x[o], w[o]; c = np.cumsum(ws) / ws.sum()
        q = lambda f: float(xs[min(np.searchsorted(c, f), len(xs) - 1)])
        out[keys[k]] = {"p10": round(q(0.1), 1), "p50": round(q(0.5), 1), "p90": round(q(0.9), 1),
                        "area_m2": round(float(w.sum()), 1), "faces": int(sel.sum())}
    return out


def bc7_mb(A, maps=4):
    """Raw BC7 with a full mip chain: 1 byte / texel x 4/3."""
    return round(A * A * 4.0 / 3.0 * maps / 1e6, 2)


def mask_stats(obj, maskmap_path, keys, mat_attr="cs2ts_mat", mat_index=None, uv_layer=None):
    """{key: {A_mean, A_p10, A_p50, A_p90, metal_mean, area_m2}}: the shipped MaskMap sampled at
    each triangle's centroid UV (3D-area weighted, so overlapped UVs count per use)."""
    me = obj.data
    img = K._load_raw(maskmap_path)
    H, W = img.shape[:2]
    lay = me.uv_layers[uv_layer] if uv_layer else (me.uv_layers.get(cs2_uv.UNIQUE_LAYER) or me.uv_layers.active)
    uv = cs2_uv.loop_uvs(lay)
    co = np.empty(len(me.vertices) * 3, np.float32); me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3).astype(np.float64)
    if mat_index is None:
        mat_index = np.zeros(len(me.polygons), np.int32)
        me.attributes[mat_attr].data.foreach_get("value", mat_index)
    acc = {}
    for p in me.polygons:
        ls = list(range(p.loop_start, p.loop_start + p.loop_total))
        vs = [me.loops[i].vertex_index for i in ls]
        for i in range(1, len(vs) - 1):
            a3 = 0.5 * np.linalg.norm(np.cross(co[vs[i]] - co[vs[0]], co[vs[i + 1]] - co[vs[0]]))
            if a3 <= 1e-9:
                continue
            c = (uv[ls[0]] + uv[ls[i]] + uv[ls[i + 1]]) / 3.0
            x = int(np.clip(c[0] * W, 0, W - 1)); y = int(np.clip(c[1] * H, 0, H - 1))
            acc.setdefault(int(mat_index[p.index]), []).append((img[y, x, 3], img[y, x, 0], a3))
    out = {}
    for k, rows in sorted(acc.items()):
        r = np.array(rows)
        a, m, w = r[:, 0], r[:, 1], r[:, 2]
        o = np.argsort(a); xs, ws = a[o], w[o]; cc = np.cumsum(ws) / ws.sum()
        q = lambda f: float(xs[min(np.searchsorted(cc, f), len(xs) - 1)])
        out[keys[k]] = {"A_mean": round(float((a * w).sum() / w.sum()), 3), "A_p10": round(q(0.1), 3),
                        "A_p50": round(q(0.5), 3), "A_p90": round(q(0.9), 3),
                        "metal_mean": round(float((m * w).sum() / w.sum()), 3),
                        "area_m2": round(float(w.sum()), 1)}
    return out


def fill_label_backgrounds(layout, label_rects, margin=6):
    """Give every label rect its host material's mean look before the painter runs (a recolour
    slot keeps its ControlMask, so a painted field still takes the building's tint)."""
    mp = layout["maps"]
    for pre, L in label_rects.items():
        key = L["key"]
        if key in layout["tiles"]:
            x0, y0, C = layout["tiles"][key]["rect_px"]
            sl = (slice(y0, y0 + C), slice(x0, x0 + C))
        else:
            cx, cy, cell = layout["palette"][key]["rect_px"]
            sl = (slice(cy + 4, cy + cell - 4), slice(cx + 4, cx + cell - 4))
        (ox, oy), (w, h) = L["origin_px"], L["size_px"]
        r = (slice(oy - margin, oy + h + margin), slice(ox - margin, ox + w + margin))
        mp["base"][r[0], r[1], :3] = mp["base"][sl[0], sl[1], :3].reshape(-1, 3).mean(0)
        mp["msk"][r[0], r[1]] = mp["msk"][sl[0], sl[1]].reshape(-1, 4).mean(0)
        mp["ctl"][r[0], r[1]] = np.round(mp["ctl"][sl[0], sl[1]].reshape(-1, 4).mean(0))
        mp["nrm"][r[0], r[1]] = (0.5, 0.5, 1.0)


def check_uv_cells(obj, layout, keys, mat_attr="cs2ts_mat", tol_px=0.51):
    """Every loop of a face must sample inside its key's region (tile content square, palette
    cell or the label swatch). Returns {key: n faces outside} (empty = clean)."""
    me = obj.data
    A = layout["A"]
    uv = cs2_uv.loop_uvs(me.uv_layers[cs2_uv.UNIQUE_LAYER]) * A
    mi = np.zeros(len(me.polygons), np.int32)
    me.attributes[mat_attr].data.foreach_get("value", mi)
    LX, LY, LS = layout["label_rect"]
    bad = {}
    for p in me.polygons:
        key = keys[int(mi[p.index])]
        q = uv[p.loop_start:p.loop_start + p.loop_total]
        rects = []
        if key in layout["tiles"]:
            x0, y0, C = layout["tiles"][key]["rect_px"]; rects.append((x0, y0, C))
        else:
            rects.append(tuple(layout["palette"][key]["rect_px"]))
        rects.append((LX, LY, LS))
        ok = any(((q[:, 0] >= x - tol_px) & (q[:, 0] <= x + s + tol_px) & (q[:, 1] >= y - tol_px)
                  & (q[:, 1] <= y + s + tol_px)).all() for x, y, s in rects)
        if not ok:
            bad[key] = bad.get(key, 0) + 1
    return bad
