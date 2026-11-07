"""
snow_weather.py - the CLEAN-STYLE texture pass for the generation-B snowmaking props (REDO, wiki style,
2026-10-17). Write-up: docs/research/art/snow-props-weather-2026-10-17.md, section "Redo (wiki style)".
Brief: docs/research/art/wiki-best-practices-gap-2026-10-17.md sections 0, 2a, 2b, 3.

History: rev 1-4 of this module painted texel-scale weathering (galvanized spangle and haze, salt
streaks, rust bloom, chips to primer, water runs, soot / oil, a rusty guard, relief) on 9-26 mm texels
with a resolution gate that admitted 1.5-3 texel features. It was rejected (DECISIONS 264 VERDICT FAIL):
"too much weathering or the wrong style ... looked better before ... too pixelated". The wiki's only
style rule is "clean, minimal grunge, no repetitive patterns". Detail under ~5 cm belongs in the NORMAL
map, and vanilla props have near-flat albedo. Every texel-scale overlay is RETIRED here, not tuned (the
rev 4 code is in git, commit 0f7a4cf).

It runs inside lift_tower.apply_finish_textures' `post_compose` slot (linear light, AO already
multiplied, stamped strips already restored, before the sRGB encode) and changes PIXELS ONLY. Mesh, UVs,
part table, LOD chain and file names are untouched.

STEP 1 (`step=1`, "one variable at a time"): resolution + repack + parity, no soiling.
  N0 re-orient    every reprojected atlas normal is turned by its face's atlas -> unique UV rotation
                  (reorient_normals). The REDO packer unfolds and rotates islands, and the EMIT bake
                  copies tangent-space normals verbatim.
  T0 parity       MaskMap smoothness / metal per zone onto the measured vanilla bands (constant per
                  zone: the atlas photo roughness was stretched by the atlas squeeze). Metal only on
                  the bare galvanized zone. Galvanized albedo lifted toward sRGB luma 0.72 (was 0.64).
  T0f flat base   every zone except the stamped strip and concrete takes a per-PART flat albedo and a
                  flat normal. The atlas photo / procedural grain was projected through smart-project
                  islands squeezed into small atlas rects (median 1.4 : 1, p90 3.5 : 1, lance tube
                  28 : 1), so its scale and direction were accidents. At the REDO densities it would
                  read as stripes and noise. Concrete keeps its photo at CONCRETE_DETAIL contrast.
  N1 normal detail  panel seams, bolt heads, weld beads, molded ribs, a lid panel and a world-
                  anchored grating, drawn as height by cs2_compose.detail_height (the existing
                  height -> normal path) ONLY where the feature spans >= 3 texels
                  (NORMAL_TEXEL_FLOOR). The specs are per class in NORMAL_DETAIL_CLASSES / normal_detail_specs.
  AO              the finish bake's gentle occ = ao * 0.92 + 0.08 is kept, DENOISED with a 5 x 5
                  binomial over covered texels (AO_DENOISE). The 24-sample bake's per-texel noise was
                  the largest high-frequency term left once the flat base removed the atlas grain.
                  Islands sit >= 8 texels apart, so nothing mixes across islands.
STEP 2 (`step=2`): step 1 + LOW-FREQUENCY soiling only (soil_overlays):
  S1 grade        smooth darkening from grade to 0.5 m (<= 12 % sRGB luma), broken by 0.6 m noise
  S2 pad top      broad soft dirt on the concrete pad tops (<= 12 %)
  S3 lid top      broad soft dirt on hydrant lid tops (<= 10 %, a recolorable slot: bounded)
  S4 collar run   at most ONE soft run >= 10 cm wide below a collar (tower mast, lance column, 8 %)
  The grade ramp is a tonal gradient, capped by its own luma limit. The STAINS (S2-S4) carry the
  coverage budget. A 0.5 m ramp alone covers most of a 0.35 m hydrant box or a 0.45 m fan chassis,
  so a total budget would have faded the whole step to nothing. The code reports both coverages.
  Every overlay declares its feature size and soft edge. overlay_gate() RAISES on a feature under
  MIN_FEATURE_M (5 cm) or a soft edge under MIN_SOFT_M. It weights texels by
  smoothstep(6, 10, feature x px/m) (>= 8 texels). An overlay whose mean gate is below 0.5 is SKIPPED,
  never shrunk. The hook re-checks that nothing was painted where the feature spans
  < 6 texels.

Contract rules kept in code (guards raise):
- Recolorable texels keep a LIGHT albedo (slot mean >= SLOT_KEEP of the bake's, dirt clamped at
  DIRT_FLOOR).
- BaseColor alpha 1.
- MaskMap G = B = 0.
- ControlMask alpha 0 and channels exclusive (only ever copied).
- Metal only on galvanized.

Public API:
    make_hook(obj, name, step=2, key=None) -> WeatherHook (post_compose signature)
    ReorientHook(obj, inner=None)            the N0 fix alone (the packer runs without --weather)
    hook.fields, hook.report                 after the bake ran
    measure(base, msk, ctl, fields)          per-zone stats (before / after tables)
    albedo_hf(base_srgb, fields)             per-zone high-frequency luma rms (vanilla EU house 0.010)
    build_fields(mesh, A, matrix)            the raster (also used to measure exported maps)
    overlay_gate(F, sel, name, feature_m, soft_m)   the overlay-scale gate (raises)
Determinism: seeds are zlib.crc32(f"{name}:{effect}"). There is no RNG state and no clock.
"""

import json
import math
import os
import sys
import zlib

import numpy as np

_HERE = os.path.dirname(os.path.abspath(__file__))
if _HERE not in sys.path:
    sys.path.insert(0, _HERE)
import cs2_asset  # noqa: E402
from cs2_weather import fbm, uniform, smoothstep, hash01  # noqa: E402,F401  (read-only reuse)

LUMA = np.array((0.2126, 0.7152, 0.0722), dtype=np.float32)

# ---------------------------------------------------------------------------------------
# T0 parity. Targets from the measured vanilla bands (write-up section 2):
# - vanilla metal (R = 1.0) on utility props 0.47-0.62 at a LIGHT albedo (sRGB luma 0.51-0.84, the
#   galvanized fence 0.51 / 0.72)
# - painted / dielectric utility props 0.18-0.73 (median ~0.44: hydrant 0.38, substation 0.38)
# - building walls 0.25-0.41
# - glass 0.69
#
# Constant per zone (the atlas roughness variation was stretched like its albedo). (smoothness, metal)
# ---------------------------------------------------------------------------------------
ZONE_TARGET = {
    "concrete":  (0.33, 0.0),
    "galv":      (0.49, 1.0),
    "livery":    (0.40, 0.0),
    "accent":    (0.36, 0.0),
    "plastic":   (0.38, 0.0),
    "rubber":    (0.18, 0.0),
    "grate":     (0.30, 0.0),
    "amber":     (0.70, 0.0),
    "floodlens": (0.70, 0.0),
    "plate":     (0.40, 0.0),
    "reflect":   (0.55, 0.0),
    "chevron":   (0.38, 0.0),
    "label":     (0.38, 0.0),
}
CONCRETE_DETAIL = 0.5        # concrete keeps half its photo contrast (albedo, smoothness, normal)
GALV_LUMA_SRGB = 0.72        # clean galvanized albedo (vanilla metal band 0.51-0.84, fence 0.72, industrial
                             # light pole 0.74. 2026-10-17 after the street-light comparison: was 0.64)
GALV_DESAT = 0.80            # pull Metal032's blue cast toward neutral
SLOT_ZONES = {"livery": 0, "plastic": 1, "accent": 2}    # zone -> ControlMask channel
SLOT_KEEP = 0.90             # guard: slot-texel albedo mean (sRGB) stays >= 90 % of the bake's
DIRT_FLOOR = 0.78            # slot texels: soiling may darken to 78 % (linear) at most

STRIP_ZONES = ("amber", "plate", "chevron", "reflect", "label", "floodlens")
NO_WEATHER_PREFIXES = ("HeadBlades", "GunBlades", "HeadSpinner", "GunSpinner", "HeadFan",
                       "GunFan", "HeadBore", "GunBore", "Beacon", "Marker")
# = snow_props.BEVEL_PREFIXES (copied: this module must not import the generator). It is used by
# build_fields' feature-edge test
CHAMFER_PREFIXES = ("Pad", "BasePlate", "Cabinet", "Platform", "Body", "Lid", "Compressor",
                    "DriveHousing", "PushPlate", "HeadCompressor")

# ---------------------------------------------------------------------------------------
# N1 normal detail and the overlay-scale gate
# ---------------------------------------------------------------------------------------
NORMAL_TEXEL_FLOOR = 3.0     # a normal-map feature is drawn only where it spans >= 3 texels
MIN_FEATURE_M = 0.05         # no overlay feature under 5 cm in the world ...
MIN_SOFT_M = 0.05            # ... nor a soft edge under 5 cm
GATE_TEXELS = (6.0, 10.0)    # ... and none under 8 texels: weight smoothstep(6, 10, f x px/m)
GATE_SKIP_MEAN = 0.5         # an overlay whose mean gate weight is below this is skipped whole
COVERAGE_MAX = 0.10          # area-weighted mean STAIN weight (S2-S4) per class
AO_DENOISE = True            # 5 x 5 binomial blur of the baked occlusion over covered texels
# per-channel weight of the sRGB darkening (luma-normalized: the luma drop is exactly k x w): warm, dusty
GRIME_TINT = np.array((0.90, 1.0, 1.15), np.float64) / float(np.dot(LUMA, (0.90, 1.0, 1.15)))


class OverlayScaleError(ValueError):
    """An overlay below the minimum feature size or soft edge (the rejected pass's failure)."""


def _seed(name, effect):
    return zlib.crc32(f"{name}:{effect}".encode()) & 0xFFFFFFFF


def lin_to_srgb(x):
    x = np.clip(x, 0.0, 1.0)
    return np.where(x <= 0.0031308, x * 12.92, 1.055 * np.power(x, 1.0 / 2.4) - 0.055)


def srgb_to_lin(x):
    x = np.clip(x, 0.0, 1.0)
    return np.where(x <= 0.04045, x / 12.92, np.power((x + 0.055) / 1.055, 2.4))


# ---------------------------------------------------------------------------------------
# Raster: per-texel fields of the finished mesh in its UVUnique layout
# ---------------------------------------------------------------------------------------

class Fields:
    """Compact per-texel fields (one row per covered texel, order of `idx`)."""
    pass


def _zone_of(uv, zones):
    u, v = uv
    best, bd = None, 1e9
    for zn, (u0, v0, u1, v1) in zones.items():
        if zn == "strip":
            continue
        du = max(u0 - u, 0.0, u - u1)
        dv = max(v0 - v, 0.0, v - v1)
        d = du * du + dv * dv
        if d < bd or (d == bd == 0.0 and zn in STRIP_ZONES):
            best, bd = zn, d
    return best


def build_fields(me, A, matrix=None, uv_layer="UVUnique", atlas_layer="UVAtlas"):
    """Rasterize `me` into an A x A grid of its `uv_layer`. Returns Fields."""
    M = np.array(matrix if matrix is not None else np.eye(4), dtype=np.float64).reshape(4, 4)
    R3 = M[:3, :3]
    nv, nl, npoly = len(me.vertices), len(me.loops), len(me.polygons)
    co = np.empty(nv * 3, np.float32); me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3).astype(np.float64) @ R3.T + M[:3, 3]
    lv = np.empty(nl, np.int32); me.loops.foreach_get("vertex_index", lv)
    le = np.empty(nl, np.int32); me.loops.foreach_get("edge_index", le)
    uvu = np.empty(nl * 2, np.float32); me.uv_layers[uv_layer].data.foreach_get("uv", uvu)
    uvu = uvu.reshape(-1, 2).astype(np.float64)
    if atlas_layer in me.uv_layers:
        uva = np.empty(nl * 2, np.float32); me.uv_layers[atlas_layer].data.foreach_get("uv", uva)
        uva = uva.reshape(-1, 2).astype(np.float64)
    else:
        uva = None
    cn = np.empty(nl * 3, np.float32); me.corner_normals.foreach_get("vector", cn)
    cn = cn.reshape(-1, 3).astype(np.float64) @ R3.T
    ls = np.empty(npoly, np.int32); me.polygons.foreach_get("loop_start", ls)
    lt_ = np.empty(npoly, np.int32); me.polygons.foreach_get("loop_total", lt_)
    poly_of_loop = np.repeat(np.arange(npoly), lt_)
    ev = np.empty(len(me.edges) * 2, np.int32); me.edges.foreach_get("vertices", ev)
    ev = ev.reshape(-1, 2)

    # part ids and table
    table = json.loads(me[cs2_asset.PART_TABLE_PROP]) if cs2_asset.PART_TABLE_PROP in me else []
    if "part" in me.attributes:
        pid = np.empty(npoly, np.int32); me.attributes["part"].data.foreach_get("value", pid)
        pid = pid - 1
    else:
        pid = np.zeros(npoly, np.int32)
        table = table or ["?"]

    # face normals / centroids from geometry (world)
    fn = np.zeros((npoly, 3)); fc = np.zeros((npoly, 3))
    np.add.at(fc, poly_of_loop, co[lv])
    fc /= lt_[:, None]
    # Newell normal (robust on n-gons)
    nxt = np.arange(nl) + 1
    last = ls + lt_ - 1
    nxt[last] = ls
    a = co[lv]; b = co[lv[nxt]]
    cr = np.stack([(a[:, 1] - b[:, 1]) * (a[:, 2] + b[:, 2]),
                   (a[:, 2] - b[:, 2]) * (a[:, 0] + b[:, 0]),
                   (a[:, 0] - b[:, 0]) * (a[:, 1] + b[:, 1])], 1)
    np.add.at(fn, poly_of_loop, cr)
    farea = 0.5 * np.linalg.norm(fn, axis=1)
    fn = fn / np.maximum(np.linalg.norm(fn, axis=1, keepdims=True), 1e-12)

    # zone per face from the atlas UV centroid
    zones = cs2_asset.snow_prop_zones(A)
    fzone = []
    if uva is not None:
        cuv = np.zeros((npoly, 2)); np.add.at(cuv, poly_of_loop, uva); cuv /= lt_[:, None]
        fzone = [_zone_of(tuple(c), zones) for c in cuv]
    else:
        fzone = ["?"] * npoly

    # feature edges (where paint chips and rust start): open rims always, and convex dihedrals above 40
    # deg on the chamfered boxes (their 1-segment chamfer splits a 90 deg corner into two 45 deg edges)
    # or above 60 deg elsewhere. So the 8-14 sided lathes (bell 45 deg, housing 30 deg between segments)
    # do NOT count every facet seam as an edge.
    chamf_part = np.array([n.startswith(CHAMFER_PREFIXES) for n in table], dtype=bool)         if table else np.zeros(1, bool)
    e_polys = {}
    for li in range(nl):
        e_polys.setdefault(int(le[li]), []).append(int(poly_of_loop[li]))
    cos40 = math.cos(math.radians(40.0))
    cos60 = math.cos(math.radians(60.0))
    feat = {}
    for e, ps in e_polys.items():
        ps = sorted(set(ps))
        f = False
        if len(ps) == 1:
            f = True
        elif len(ps) == 2:
            p, q = ps
            lim = cos40 if (chamf_part[pid[p]] or chamf_part[pid[q]]) else cos60
            if float(fn[p] @ fn[q]) < lim and float(fn[p] @ (fc[q] - fc[p])) < -1e-6:
                f = True
        if f:
            for p in ps:
                feat.setdefault(p, []).append(e)

    # raster
    fid = np.full((A, A), -1, np.int32)
    P = np.zeros((A, A, 3), np.float32)
    N = np.zeros((A, A, 3), np.float32)
    ED = np.full((A, A), np.inf, np.float32)
    me.calc_loop_triangles()
    nt = len(me.loop_triangles)
    tl = np.empty(nt * 3, np.int32); me.loop_triangles.foreach_get("loops", tl); tl = tl.reshape(-1, 3)
    tp = np.empty(nt, np.int32); me.loop_triangles.foreach_get("polygon_index", tp)
    uvpx_area = np.zeros(npoly)
    for t in range(nt):
        L3 = tl[t]
        p = int(tp[t])
        U = uvu[L3] * A - 0.5
        ux, uy = U[:, 0], U[:, 1]
        uvpx_area[p] += 0.5 * abs((ux[1] - ux[0]) * (uy[2] - uy[0]) - (ux[2] - ux[0]) * (uy[1] - uy[0]))
        c0 = max(0, int(math.floor(ux.min()))); c1 = min(A - 1, int(math.ceil(ux.max())))
        r0 = max(0, int(math.floor(uy.min()))); r1 = min(A - 1, int(math.ceil(uy.max())))
        if c1 < c0 or r1 < r0:
            continue
        gx, gy = np.meshgrid(np.arange(c0, c1 + 1, dtype=np.float64),
                             np.arange(r0, r1 + 1, dtype=np.float64))
        (ax, ay), (bx, by), (cx, cy) = U
        d = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
        if abs(d) < 1e-12:
            continue
        l1 = ((by - cy) * (gx - cx) + (cx - bx) * (gy - cy)) / d
        l2 = ((cy - ay) * (gx - cx) + (ax - cx) * (gy - cy)) / d
        l3 = 1.0 - l1 - l2
        ins = (l1 >= -1e-4) & (l2 >= -1e-4) & (l3 >= -1e-4)
        sub = fid[r0:r1 + 1, c0:c1 + 1]
        ins &= sub < 0
        if not ins.any():
            continue
        rr, cc = np.nonzero(ins)
        w = np.stack([l1[ins], l2[ins], l3[ins]], 1)
        Pv = co[lv[L3]]
        pos = w @ Pv
        nrm = w @ cn[L3]
        nrm /= np.maximum(np.linalg.norm(nrm, axis=1, keepdims=True), 1e-9)
        R_, C_ = rr + r0, cc + c0
        fid[R_, C_] = p
        P[R_, C_] = pos
        N[R_, C_] = nrm
        segs = feat.get(p)
        if segs:
            dmin = np.full(len(pos), np.inf)
            for e in segs:
                s0, s1 = co[ev[e, 0]], co[ev[e, 1]]
                sd = s1 - s0
                ll = float(sd @ sd)
                tt = np.clip(((pos - s0) @ sd) / max(ll, 1e-12), 0.0, 1.0)
                dd = np.linalg.norm(pos - (s0 + tt[:, None] * sd), axis=1)
                dmin = np.minimum(dmin, dd)
            ED[R_, C_] = dmin

    F = Fields()
    F.A = A
    F.cov = fid >= 0
    F.idx = np.flatnonzero(F.cov.ravel())
    F.fid = fid.ravel()[F.idx]
    F.P = P.reshape(-1, 3)[F.idx].astype(np.float64)
    F.N = N.reshape(-1, 3)[F.idx].astype(np.float64)
    F.FN = fn[F.fid]
    F.ED = ED.ravel()[F.idx].astype(np.float64)
    texels = np.bincount(F.fid, minlength=npoly).astype(np.float64)
    F.ppm_face = np.sqrt(np.maximum(uvpx_area, 1e-9) / np.maximum(farea, 1e-9))
    F.ppm = F.ppm_face[F.fid]
    F.w = (farea / np.maximum(texels, 1.0))[F.fid]          # m^2 per texel (area weights)
    F.zone = np.array(fzone, dtype=object)[F.fid]
    F.part_id = pid[F.fid]
    F.table = table
    F.part = np.array(table, dtype=object)[F.part_id] if table else np.array(["?"] * len(F.fid))
    F.npoly = npoly
    F.pid_poly = pid
    F.farea = farea
    # per-part geometry: bbox, centroid, principal axis, elongation
    parts = {}
    vparts = {}
    for p in range(npoly):
        vparts.setdefault(int(pid[p]), set()).update(lv[ls[p]:ls[p] + lt_[p]].tolist())
    for k, vs in vparts.items():
        X = co[sorted(vs)]
        lo, hi = X.min(0), X.max(0)
        c = X.mean(0)
        if len(X) >= 3:
            cov_ = np.cov((X - c).T)
            ev_, evec = np.linalg.eigh(cov_)
            axis = evec[:, -1]
            elong = math.sqrt(max(ev_[-1], 1e-12) / max(ev_[-2], 1e-12))
        else:
            axis, elong = np.array((0.0, 0.0, 1.0)), 1.0
        proj = (X - c) @ axis
        parts[k] = dict(lo=lo, hi=hi, c=c, axis=axis, elong=elong,
                        length=float(proj.max() - proj.min()),
                        rh=float(np.max(np.linalg.norm((X - c)[:, :2], axis=1))),
                        evals=(ev_ if len(X) >= 3 else np.ones(3)),
                        evecs=(evec if len(X) >= 3 else np.eye(3)))
    F.parts = parts
    F.zmin = float(co[:, 2].min())
    return F


def _scatter(F, arr, fill=0.0):
    out = np.full(F.A * F.A, fill, np.float32)
    out[F.idx] = arr
    return out.reshape(F.A, F.A)


def _startswith(F, prefixes):
    pref = tuple(prefixes)
    names = F.table
    hit = np.array([n.startswith(pref) for n in names], dtype=bool) if names else np.zeros(1, bool)
    return hit[F.part_id]


# ---------------------------------------------------------------------------------------
# Measurement (before / after tables)
# ---------------------------------------------------------------------------------------

def _wstats(x, w):
    if len(x) == 0 or w.sum() <= 0:
        return None
    o = np.argsort(x); xs = x[o]; ws = w[o]; c = np.cumsum(ws) / ws.sum()
    q = lambda p: float(xs[min(np.searchsorted(c, p), len(xs) - 1)])
    return dict(mean=round(float((x * w).sum() / w.sum()), 3), p10=round(q(0.1), 3),
                p90=round(q(0.9), 3), area_m2=round(float(w.sum()), 3))


def measure(base_srgb, msk, ctl, F):
    """Per-zone stats over covered texels, area weighted. base_srgb = display-encoded RGB."""
    b = base_srgb.reshape(-1, base_srgb.shape[-1])[F.idx]
    m = msk.reshape(-1, 4)[F.idx]
    c = ctl.reshape(-1, 4)[F.idx]
    lum = b[:, :3] @ LUMA
    out = {}
    for zn in sorted(set(F.zone.tolist())):
        sel = F.zone == zn
        if not sel.any():
            continue
        r = dict(smooth=_wstats(m[sel, 3], F.w[sel]), metal=_wstats(m[sel, 0], F.w[sel]),
                 luma_srgb=_wstats(lum[sel], F.w[sel]))
        if zn in SLOT_ZONES:
            ch = SLOT_ZONES[zn]
            slot = sel & (c[:, ch] > 0.5)
            r["slot_luma_srgb"] = _wstats(lum[slot], F.w[slot])
            r["slot_share"] = round(float(F.w[slot].sum() / max(F.w[sel].sum(), 1e-12)), 3)
        out[zn] = r
    allw = F.w
    out["_all"] = dict(smooth=_wstats(m[:, 3], allw), metal=_wstats(m[:, 0], allw))
    return out


def _put(n, sel, vals):
    out = np.zeros(n)
    out[sel] = vals
    return out


def _dilate(arrays, cov, steps=10):
    """Bake-margin style: grow covered texels' values into the gutter (`steps` px), so mips
    never pull the clean pre-weathering gutter back in. Texels further out are untouched."""
    A = cov.shape[0]
    done = cov.copy()
    arrays = [a.copy() for a in arrays]
    for _ in range(steps):
        acc = [np.zeros_like(a) for a in arrays]
        cnt = np.zeros((A, A), np.float32)
        for dr, dc in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            m = np.roll(done, (dr, dc), (0, 1))
            if dr == 1:
                m[0, :] = False
            if dr == -1:
                m[-1, :] = False
            if dc == 1:
                m[:, 0] = False
            if dc == -1:
                m[:, -1] = False
            for k, a in enumerate(arrays):
                acc[k] += np.where(m[..., None], np.roll(a, (dr, dc), (0, 1)), 0.0)
            cnt += m
        new = (~done) & (cnt > 0)
        if not new.any():
            break
        for k, a in enumerate(arrays):
            a[new] = acc[k][new] / cnt[new][:, None]
        done |= new
    # normals in the gutter: renormalize the averaged vectors
    n = arrays[1]
    v = n[..., :3] * 2.0 - 1.0
    v /= np.maximum(np.linalg.norm(v, axis=-1, keepdims=True), 1e-9)
    n[..., :3] = np.where(done[..., None] & ~cov[..., None], v * 0.5 + 0.5, n[..., :3])
    return arrays


def albedo_hf(base_srgb, F):
    """Per zone: area-weighted RMS of (sRGB luma - its 3x3 box mean) over texels whose whole 3x3
    neighborhood lies on the SAME face (island edges and face seams do not count). The vanilla
    EU house measures 0.010 (vanilla-detail-benchmark section 2.2)."""
    A = F.A
    lum = (np.asarray(base_srgb)[..., :3].reshape(A, A, 3) @ LUMA).astype(np.float64)
    fid = np.full(A * A, -1, np.int64)
    fid[F.idx] = F.fid
    fid = fid.reshape(A, A)
    acc = np.zeros((A, A))
    same = fid >= 0
    for dr in (-1, 0, 1):
        for dc in (-1, 0, 1):
            acc += np.roll(lum, (dr, dc), (0, 1))
            same &= np.roll(fid, (dr, dc), (0, 1)) == fid
    hf = (lum - acc / 9.0).ravel()[F.idx]
    ok = same.ravel()[F.idx]
    out = {}
    for zn in sorted(set(F.zone.tolist())):
        sel = ok & (F.zone == zn)
        if sel.sum() < 16:
            continue
        out[zn] = round(float(np.sqrt((hf[sel] ** 2 * F.w[sel]).sum() / F.w[sel].sum())), 4)
    sel = ok & ~np.isin(F.zone, STRIP_ZONES)
    out["_all"] = round(float(np.sqrt((hf[sel] ** 2 * F.w[sel]).sum() / max(F.w[sel].sum(), 1e-12))), 4)
    return out


def _denoise_occ(F, occ):
    """5 x 5 binomial normalized convolution of the per-texel occlusion over covered texels."""
    img = _scatter(F, occ.astype(np.float32)).astype(np.float64)
    m = F.cov.astype(np.float64)
    k = (1.0, 4.0, 6.0, 4.0, 1.0)

    def blur(x):
        y = sum(k[i] * np.roll(x, i - 2, 0) for i in range(5))
        return sum(k[i] * np.roll(y, i - 2, 1) for i in range(5))
    return (blur(img * m) / np.maximum(blur(m), 1e-9)).ravel()[F.idx]


# ---------------------------------------------------------------------------------------
# N0: re-orient the reprojected tangent-space normals
# ---------------------------------------------------------------------------------------

def face_rotations(mesh, uv_from="UVAtlas", uv_to="UVUnique"):
    """(npoly, 2, 2): per face the orthogonal factor R of M = U_to @ inv(U_from), the linear map
    from the face's atlas UVs to its unique UVs (first three corners). A tangent-space normal's
    (x, y) baked in the atlas frame becomes R @ (x, y) in the unique frame (both maps near-
    conformal: R is the polar factor, so a squash along an island's axis does not tilt normals).
    Identity where either triangle is degenerate."""
    n = len(mesh.polygons)
    ls = np.empty(n, np.int64)
    mesh.polygons.foreach_get("loop_start", ls)
    nl = len(mesh.loops)
    ua = np.empty(nl * 2, np.float32)
    mesh.uv_layers[uv_from].data.foreach_get("uv", ua)
    uu = np.empty(nl * 2, np.float32)
    mesh.uv_layers[uv_to].data.foreach_get("uv", uu)
    ua = ua.reshape(-1, 2).astype(np.float64)
    uu = uu.reshape(-1, 2).astype(np.float64)
    Ua = np.stack([ua[ls + 1] - ua[ls], ua[ls + 2] - ua[ls]], axis=-1)       # (n, 2, 2) columns
    Uu = np.stack([uu[ls + 1] - uu[ls], uu[ls + 2] - uu[ls]], axis=-1)
    det = np.linalg.det(Ua)
    ok = (np.abs(det) > 1e-14) & (np.abs(np.linalg.det(Uu)) > 1e-14)
    R = np.tile(np.eye(2), (n, 1, 1))
    if ok.any():
        M = Uu[ok] @ np.linalg.inv(Ua[ok])
        U, _S, Vt = np.linalg.svd(M)
        R[ok] = U @ Vt
    return R


def reorient_normals(Nm, mesh, F):
    """Nm: (k, 4) encoded normals of the covered texels (order F.idx). Returns (new Nm, stats)."""
    R = face_rotations(mesh)[F.fid]
    v = Nm[:, :3] * 2.0 - 1.0
    v[:, :2] = np.einsum("kij,kj->ki", R, v[:, :2])
    v /= np.maximum(np.linalg.norm(v, axis=1, keepdims=True), 1e-9)
    out = Nm.copy()
    out[:, :3] = v * 0.5 + 0.5
    ang = np.degrees(np.abs(np.arctan2(R[:, 1, 0], R[:, 0, 0])))
    return out, {"N0_rotation_deg_p50": round(float(np.median(ang)), 1),
                 "N0_rotation_deg_p90": round(float(np.percentile(ang, 90)), 1),
                 "N0_mirrored_share": round(float((np.linalg.det(R) < 0).mean()), 4)}


# ---------------------------------------------------------------------------------------
# N1: geometry-keyed normal detail (>= 3 texels)
# ---------------------------------------------------------------------------------------

def _pid(F, name):
    """Part index (0-based, F.parts key) of the part named exactly `name`, else None."""
    try:
        return F.table.index(name)
    except ValueError:
        return None


def _lathe_axis(pp):
    """The symmetry axis of a lathe part: the eigenvector whose eigenvalue is most isolated."""
    e, V = np.asarray(pp["evals"]), np.asarray(pp["evecs"])
    i = 0 if (e[1] - e[0]) > (e[2] - e[1]) else 2
    a = V[:, i]
    return a / max(np.linalg.norm(a), 1e-12)


def _door_bolts(F, door, body, inset=0.04, r=0.011, h=0.004):
    """Two rows of two bolt heads at the four corners of a thin door / lid part (Marks)."""
    import cs2_compose as C
    pd, pb = _pid(F, door), _pid(F, body)
    if pd is None:
        return ()
    d = F.parts[pd]
    lo, hi = np.asarray(d["lo"]), np.asarray(d["hi"])
    ext = hi - lo
    thin = int(np.argmin(ext))
    out = np.zeros(3)
    ref = np.asarray(F.parts[pb]["c"]) if pb is not None else np.asarray(d["c"]) - np.eye(3)[thin]
    sgn = 1.0 if (np.asarray(d["c"]) - ref)[thin] >= 0 else -1.0
    out[thin] = sgn
    others = [i for i in range(3) if i != thin]
    iu, iv = sorted(others, key=lambda i: -ext[i])           # u = the longer in-plane axis
    u, v = np.eye(3)[iu], np.eye(3)[iv]
    if float(np.cross(u, v) @ out) < 0:
        v = -v
    c = 0.5 * (lo + hi)
    c[thin] = hi[thin] if sgn > 0 else lo[thin]
    hu, hv = 0.5 * ext[iu] - inset, 0.5 * ext[iv] - inset
    if hu <= 0 or hv <= 0:
        return ()
    pitch = 2.0 * hu
    return tuple(C.Mark("bolts", tuple(c + s * hv * v), u=tuple(u), v=tuple(v), half_u=pitch, half_v=0.0,
                        pitch=pitch, bolt_r=r, h=h, reach_m=0.02, ang_deg=20.0) for s in (-1.0, 1.0))


def _band(F, part, centre, axis, half_w, h, r_max=0.0):
    import cs2_compose as C
    if _pid(F, part) is None:
        return ()
    return (C.Mark("band", tuple(float(x) for x in centre), n=tuple(float(x) for x in axis),
                   half_w=half_w, h=h, r_max=r_max),)


NORMAL_DETAIL_CLASSES = {
    "tower":   "HeadHousing seam ring (groove 2.4 cm / 1.5 mm); cabinet door: 4 corner bolt heads "
               "(2.2 cm); mast-foot fillet weld bead (3.2 cm / 2.5 mm)",
    "fan":     "GunHousing seam ring; galvanised cabinet door: 4 corner bolt heads; tank: two end "
               "weld beads (2 cm); compressor lid: 4 corner bolt heads",
    "lance":   "BaseJacket: two molded ribs (2.4 cm / 2 mm); compressor drum: two end seams "
               "(groove 2 cm); column-foot fillet weld bead (2.8 cm)",
    "hydrant": "lid: a raised molded panel (1.5 mm, 6 cm in from the edge); standpipe-foot fillet "
               "weld bead (3 cm)",
}


def normal_detail_specs(F, key):
    """{part name: DetailSpec} for the class (NORMAL_DETAIL_CLASSES). Names are exact parts. A longer name
    that would inherit a spec by prefix gets an inactive spec."""
    import cs2_compose as C
    D = C.DetailSpec
    specs = {}
    z = np.array((0.0, 0.0, 1.0))

    def put(part, marks):
        if marks and _pid(F, part) is not None:
            specs[part] = D(marks=tuple(specs[part].marks if part in specs else ()) + tuple(marks))

    def foot_bead(part, foot, half_w, h):
        pp, pf = _pid(F, part), _pid(F, foot)
        if pp is None or pf is None:
            return
        c = np.asarray(F.parts[pp]["c"]).copy()
        c[2] = float(F.parts[pf]["hi"][2]) + half_w
        put(part, _band(F, part, c, z, half_w, h, r_max=float(F.parts[pp]["rh"]) * 1.5 + 0.02))

    def housing_seam(part):
        pp = _pid(F, part)
        if pp is None:
            return
        put(part, _band(F, part, F.parts[pp]["c"], _lathe_axis(F.parts[pp]), 0.012, -0.0015))

    if key == "tower":
        housing_seam("HeadHousing")
        put("CabDoorCabinet", _door_bolts(F, "CabDoorCabinet", "Cabinet"))
        foot_bead("Mast", "MastFoot", 0.016, 0.0025)
        for nm in ("MastFoot", "MastCollar"):
            specs.setdefault(nm, D())
    elif key == "fan":
        housing_seam("GunHousing")
        put("CabDoorCabinet", _door_bolts(F, "CabDoorCabinet", "Cabinet"))
        put("CabLidCompressor", _door_bolts(F, "CabLidCompressor", "Compressor", inset=0.035))
        pt = _pid(F, "Tank")
        if pt is not None:
            a = np.asarray(F.parts[pt]["axis"])
            L = float(F.parts[pt]["length"])
            c = np.asarray(F.parts[pt]["c"])
            for s in (-1.0, 1.0):
                put("Tank", _band(F, "Tank", c + s * (0.5 * L - 0.03) * a, a, 0.010, 0.002))
    elif key == "lance":
        pj = _pid(F, "BaseJacket")
        if pj is not None:
            lo, hi = float(F.parts[pj]["lo"][2]), float(F.parts[pj]["hi"][2])
            for t in (0.35, 0.70):
                c = np.asarray(F.parts[pj]["c"]).copy()
                c[2] = lo + t * (hi - lo)
                put("BaseJacket", _band(F, "BaseJacket", c, z, 0.012, 0.002))
        pe, pi = _pid(F, "CompEnd"), _pid(F, "CompIntake")
        if pe is not None and pi is not None and _pid(F, "CompDrum") is not None:
            ce, ci = np.asarray(F.parts[pe]["c"]), np.asarray(F.parts[pi]["c"])
            a = (ci - ce) / max(np.linalg.norm(ci - ce), 1e-9)
            for c in (ce + 0.04 * a, ci - 0.04 * a):
                put("CompDrum", _band(F, "CompDrum", c, a, 0.010, -0.0015))
        foot_bead("Column", "ColFoot", 0.014, 0.0025)
        specs.setdefault("ColumnTop", D())
    elif key == "hydrant":
        pl = _pid(F, "Lid")
        if pl is not None:
            lo, hi = np.asarray(F.parts[pl]["lo"]), np.asarray(F.parts[pl]["hi"])
            half = 0.5 * float(min(hi[0] - lo[0], hi[1] - lo[1])) - 0.06
            if half > 0.05:
                c = (0.5 * (lo[0] + hi[0]), 0.5 * (lo[1] + hi[1]), float(hi[2]))
                put("Lid", (C.Mark("plate", c, u=(1.0, 0.0, 0.0), v=(0.0, 1.0, 0.0), half_u=half,
                                   half_v=half, h=0.0015, reach_m=0.02, ang_deg=20.0),))
        foot_bead("Pipe", "PipeFoot", 0.015, 0.0025)
        specs.setdefault("PipeFoot", D())
    return specs


def normal_detail(mesh, F, key, matrix):
    """Height field (meters) of the class's N1 features at >= NORMAL_TEXEL_FLOOR texels, the
    encoded detail normal over the covered texels, and the drawn / skipped counts."""
    import cs2_compose as C
    specs = normal_detail_specs(F, key)
    if not any(s.active() for s in specs.values()):
        return None, None, {}
    floor0 = C.TEXEL_FLOOR
    try:
        C.TEXEL_FLOOR = NORMAL_TEXEL_FLOOR             # the >= 3 texel rule (restored below)
        h = C.detail_height(mesh, F.A, specs, uv_layer="UVUnique", matrix=matrix, verbose=False)
        stats = {"drawn": dict(C.LAST_DETAIL_STATS.get("drawn", {})),
                 "skipped": dict(C.LAST_DETAIL_STATS.get("skipped", {}))}
    finally:
        C.TEXEL_FLOOR = floor0
    ppm = _scatter(F, F.ppm.astype(np.float32))
    nd = C.height_to_normal(h, ppm)
    return h, nd.reshape(-1, 3)[F.idx].astype(np.float64), stats


# ---------------------------------------------------------------------------------------
# Step 2: low-frequency soiling, every overlay through overlay_gate
# ---------------------------------------------------------------------------------------

def overlay_gate(F, sel, name, feature_m, soft_m):
    """The overlay-scale gate (the lesson of the rejected pass). RAISES OverlayScaleError for a feature
    under MIN_FEATURE_M or a soft edge under MIN_SOFT_M. Returns the per-texel weight
    smoothstep(GATE_TEXELS, feature x px/m) on `sel` (0 elsewhere), or None when the overlay's
    area-weighted mean gate is below GATE_SKIP_MEAN (skipped whole, never shrunk)."""
    if feature_m < MIN_FEATURE_M - 1e-12:
        raise OverlayScaleError(f"{name}: feature {feature_m * 100:.1f} cm < {MIN_FEATURE_M * 100:.0f} cm")
    if soft_m < MIN_SOFT_M - 1e-12:
        raise OverlayScaleError(f"{name}: soft edge {soft_m * 100:.1f} cm < {MIN_SOFT_M * 100:.0f} cm")
    g = np.where(sel, smoothstep(GATE_TEXELS[0], GATE_TEXELS[1], feature_m * F.ppm), 0.0)
    if not sel.any():
        return None
    mean = float((g[sel] * F.w[sel]).sum() / max(F.w[sel].sum(), 1e-12))
    return g if mean >= GATE_SKIP_MEAN else None


def _n01(F, cells, name, effect):
    """Single-octave world-meter value noise in [0, 1]: cells >= 0.3 m, no texel-scale octave."""
    return uniform(fbm(F.P, cells, F.ppm, _seed(name, effect), octaves=1))


def soil_overlays(F, name, key, exempt, is_strip):
    """[(tag, weight (n,), k (sRGB luma drop at weight 1), feature_m)] - S1..S4 (module doc)."""
    n = len(F.idx)
    z = F.P[:, 2] - F.zmin
    Nz = F.N[:, 2]
    live = ~exempt & ~is_strip
    out, skipped = [], []

    def add(tag, sel, shape, k, feature_m, soft_m):
        g = overlay_gate(F, sel, tag, feature_m, soft_m)
        if g is None:
            skipped.append(tag)
            return
        out.append((tag, np.clip(g * shape, 0.0, 1.0), k, feature_m))

    # S1 grade: grade -> 0.5 m, broken by 0.6 m noise (feature 0.5 m, soft = the whole ramp)
    ramp = 1.0 - smoothstep(0.0, 0.5, z)
    add("S1_grade", live & (z < 0.5), ramp * (0.75 + 0.25 * _n01(F, 0.6, name, "grade")), 0.12, 0.5, 0.5)
    up = Nz > 0.65
    # S2 pad tops: a soft ring about the pad's own center + broad 0.5 m patches (>= 30 cm, soft 0.15)
    pad = (F.zone == "concrete") & up & live
    if pad.any():
        shape = np.zeros(n)
        for k_ in np.unique(F.part_id[pad]):
            m = pad & (F.part_id == k_)
            c = np.asarray(F.parts[int(k_)]["c"])
            r = np.linalg.norm(F.P[m, :2] - c[:2], axis=1)
            rad = 0.5 * float(min(np.ptp(F.P[m, 0]), np.ptp(F.P[m, 1])))
            ring = 1.0 - smoothstep(0.35 * rad, 0.95 * rad, r)
            patch = smoothstep(0.50, 0.80, _n01(F, 0.5, name, "pad")[m])
            shape[m] = np.clip(0.55 * ring + 0.6 * patch, 0.0, 1.0)
        add("S2_pad_top", pad, shape, 0.12, 0.30, 0.15)
    # S3 hydrant lid top: broad soft patch (0.35 m cells on a 0.53 m lid)
    lid = _startswith(F, ("Lid",)) & up & live
    if lid.any():
        # dirt gathers toward the rim (a 0.53 m lid holds at most one 0.35 m noise cell, so the noise
        # alone left the Bare variant clean): a soft rim ramp + one broad patch
        shape = np.zeros(n)
        for k_ in np.unique(F.part_id[lid]):
            m = lid & (F.part_id == k_)
            c = np.asarray(F.parts[int(k_)]["c"])
            half = 0.5 * float(min(np.ptp(F.P[m, 0]), np.ptp(F.P[m, 1])))
            r = np.max(np.abs(F.P[m, :2] - c[:2]), axis=1) / max(half, 1e-6)
            shape[m] = np.clip(0.7 * smoothstep(0.35, 1.0, r)
                               + 0.5 * smoothstep(0.5, 0.8, _n01(F, 0.35, name, "lid")[m]), 0.0, 1.0)
        add("S3_lid_top", lid, shape, 0.10, 0.17, 0.17)
    # S4 one soft run under a collar: tower mast below MastCollar, lance column below WeldCollar
    run_src = {"tower": ("Mast", "MastCollar", 0.9), "lance": ("Column", "WeldCollar", 0.5)}.get(key)
    if run_src is not None:
        pt, pc = _pid(F, run_src[0]), _pid(F, run_src[1])
        if pt is not None and pc is not None:
            sel = (F.part_id == pt) & live & (np.abs(Nz) < 0.75)
            pp = F.parts[pt]
            zc = float(F.parts[pc]["lo"][2])
            th0 = 2.0 * math.pi * hash01(np.array([pt]), np.array([1]), np.array([2]), _seed(name, "run"))[0]
            th = np.arctan2(F.P[:, 1] - pp["c"][1], F.P[:, 0] - pp["c"][0])
            rad = np.linalg.norm(F.P[:, :2] - np.asarray(pp["c"])[:2], axis=1)
            dth = np.abs((th - th0 + math.pi) % (2.0 * math.pi) - math.pi)
            arc = dth * rad
            across = 1.0 - smoothstep(0.05, 0.10, arc)              # 10 cm core, 5 cm soft edges
            dz = zc - F.P[:, 2]
            L = run_src[2]
            along = smoothstep(0.0, 0.05, dz) * (1.0 - smoothstep(0.4 * L, L, dz))
            add("S4_collar_run", sel & (dz > 0) & (dz < L), across * along, 0.08, 0.10, 0.05)
    return out, skipped


# ---------------------------------------------------------------------------------------
# The pass
# ---------------------------------------------------------------------------------------

def weather(base, nrm, msk, ctl, ao, F, name, mesh=None, matrix=None, step=2, key=None, skip=()):
    """Arrays are (A, A, 4) linear. Returns new arrays + stats. `skip`: ids to leave out for diagnosis (N0,
    T0f, N1, S1_grade, S2_pad_top, S3_lid_top, S4_collar_run). The shipping path skips nothing."""
    skip = set(skip)
    A = F.A
    flat = lambda arr: arr.reshape(-1, 4)[F.idx].astype(np.float64)
    B = flat(base); Nm = flat(nrm); Mk = flat(msk); C = flat(ctl)
    occ = (ao.reshape(-1, 4)[F.idx, 0].astype(np.float64) * 0.92 + 0.08) if ao is not None \
        else np.ones(len(F.idx))
    n = len(F.idx)
    zone = F.zone
    w = F.w
    stats = {"step": int(step)}

    def share(mask, sel=None):
        sel = np.ones(n, bool) if sel is None else sel
        return round(float((np.clip(mask, 0, 1)[sel] * w[sel]).sum() / max(w[sel].sum(), 1e-12)), 4)

    exempt = _startswith(F, NO_WEATHER_PREFIXES)
    is_strip = np.isin(zone, STRIP_ZONES)
    galv = zone == "galv"
    slot_zone = np.isin(zone, tuple(SLOT_ZONES))

    # ---- N0 re-orient the reprojected normals ---------------------------------------------
    if mesh is not None and "N0" not in skip and "UVAtlas" in mesh.uv_layers:
        Nm, st = reorient_normals(Nm, mesh, F)
        stats.update(st)

    # ---- T0 parity + T0f flat base ----------------------------------------------------------
    raw = B[:, :3] / occ[:, None]
    for zn, (tgt, metal) in ZONE_TARGET.items():
        sel = zone == zn
        if not sel.any():
            continue
        if zn == "concrete":
            mean = float((Mk[sel, 3] * w[sel]).sum() / w[sel].sum())
            Mk[sel, 3] = np.clip(tgt + (Mk[sel, 3] - mean) * CONCRETE_DETAIL, 0.05, 0.95)
        else:
            Mk[sel, 3] = tgt
        Mk[sel, 0] = metal
    if "T0f" not in skip:
        zmap = {z_: i for i, z_ in enumerate(sorted(ZONE_TARGET))}
        zid = np.array([zmap.get(z_, 63) for z_ in zone], np.int64)
        flatten = ~is_strip
        _u, inv = np.unique(F.part_id.astype(np.int64) * 64 + zid, return_inverse=True)
        ws = np.bincount(inv, weights=w, minlength=len(_u))
        mean = np.stack([np.bincount(inv, weights=w * raw[:, c], minlength=len(_u)) for c in range(3)], 1)
        mean = (mean / np.maximum(ws, 1e-12)[:, None])[inv]
        conc = zone == "concrete"
        f2 = flatten & conc
        raw[f2] = mean[f2] + (raw[f2] - mean[f2]) * CONCRETE_DETAIL
        f1 = flatten & ~conc
        raw[f1] = mean[f1]
        v = Nm[:, :3] * 2.0 - 1.0
        v[conc, :2] *= CONCRETE_DETAIL
        v[flatten & ~conc] = (0.0, 0.0, 1.0)
        v /= np.maximum(np.linalg.norm(v, axis=1, keepdims=True), 1e-9)
        Nm[:, :3] = v * 0.5 + 0.5
    if AO_DENOISE and "AO" not in skip:
        occ_s = _denoise_occ(F, occ)
        stats["ao_noise_rms"] = round(float(np.sqrt(((occ - occ_s) ** 2 * w).sum() / w.sum())), 4)
        occ = np.where(is_strip, occ, occ_s)
    if galv.any():
        lum = raw[galv] @ LUMA
        raw[galv] = raw[galv] * (1.0 - GALV_DESAT) + lum[:, None] * GALV_DESAT
        k = float(srgb_to_lin(GALV_LUMA_SRGB)) / max(float((raw[galv] @ LUMA * w[galv]).sum() / w[galv].sum()), 1e-6)
        raw[galv] = np.clip(raw[galv] * k, 0, 1)
        stats["galv_albedo_scale"] = round(k, 3)
    B[:, :3] = raw * occ[:, None]

    # ---- N1 geometry-keyed normal detail ------------------------------------------------------
    if mesh is not None and key and "N1" not in skip:
        import cs2_compose
        _h, nd, st = normal_detail(mesh, F, key, matrix)
        if nd is not None:
            Nm[:, :3] = cs2_compose.blend_rnm(Nm[None, :, :3].astype(np.float32),
                                              nd[None].astype(np.float32))[0]
        stats["N1"] = st
        stats["N1_classes"] = NORMAL_DETAIL_CLASSES.get(key, "")

    # ---- step 2: low-frequency soiling ----------------------------------------------------------
    dirt_mul = np.ones(n)
    if int(step) >= 2:
        overlays, skipped = soil_overlays(F, name, key, exempt, is_strip)
        overlays = [o for o in overlays if o[0] not in skip]
        live = ~exempt & ~is_strip
        Ws = np.zeros(n)                                   # the stains (S2-S4) carry the budget
        for tag, wt, _k, _f in overlays:
            if not tag.startswith("S1"):
                Ws = np.maximum(Ws, wt)
        cov = share(Ws, live)
        scale = min(1.0, COVERAGE_MAX / cov) if cov > 0 else 1.0
        overlays = [(t, wt if t.startswith("S1") else wt * scale, k, f) for t, wt, k, f in overlays]
        W = np.zeros(n)
        for _tag, wt, _k, _f in overlays:
            W = np.maximum(W, wt)
        lum0 = lin_to_srgb(B[:, :3]) @ LUMA
        drop_k = np.zeros(n)                     # overlays combine by MAX, never stack past one k
        per = {}
        for tag, wt, k, feat in overlays:
            # the overlay-scale assertion: nothing painted where the feature spans < 6 texels
            bad = (wt > 0) & (feat * F.ppm < GATE_TEXELS[0])
            if bad.any():
                raise OverlayScaleError(f"{name}: {tag} painted {int(bad.sum())} texels below "
                                        f"{GATE_TEXELS[0]:.0f} texels")
            drop_k = np.maximum(drop_k, k * wt)
            per[tag] = {"coverage_mean": share(wt, live), "touched_share": share(wt >= 0.25, live),
                        "feature_m": feat, "k_luma": k}
        Wd = W
        # the darkening is applied in sRGB (display) terms, so the luma limit holds on dark and light
        # texels alike (a linear factor f^2.2 doubles the drop on the near-black rubber / grate)
        old = B[:, :3].copy()
        new = srgb_to_lin(lin_to_srgb(old) * (1.0 - drop_k[:, None] * GRIME_TINT[None, :]))
        ns = ~slot_zone
        B[ns, :3] = new[ns]
        ratio = (new @ LUMA) / np.maximum(old @ LUMA, 1e-9)
        dirt_mul[slot_zone] *= ratio[slot_zone]
        Mk[:, 3] -= 0.05 * Wd
        Mk[galv, 0] *= 1.0 - 0.3 * Wd[galv]
        lum1 = lin_to_srgb(np.where(slot_zone[:, None], B[:, :3] * np.clip(dirt_mul, DIRT_FLOOR, 1.0)[:, None],
                                    B[:, :3])) @ LUMA
        drop = 1.0 - lum1 / np.maximum(lum0, 1e-6)
        for tag, wt, _k, _f in overlays:
            sel = wt > 0.01
            per[tag]["luma_drop_srgb_p99"] = round(float(np.percentile(drop[sel], 99)), 4) if sel.any() else 0.0
            per[tag]["luma_drop_srgb_max"] = round(float(drop[sel].max()), 4) if sel.any() else 0.0
        stats["soil"] = {"overlays": per, "skipped": skipped,
                         "coverage_stains_mean": share(Ws * scale, live),
                         "coverage_stains_touched": share(Ws * scale >= 0.25, live),
                         "coverage_total_mean": share(Wd, live), "coverage_total_touched": share(Wd >= 0.25, live),
                         "stain_budget_scale": round(scale, 3), "coverage_stains_unbudgeted": cov}

    # ---- RECOLOR guard: slot texels keep a light albedo ------------------------------------------
    dm = np.clip(dirt_mul, DIRT_FLOOR, 1.0)
    B[slot_zone, :3] *= dm[slot_zone, None]
    stats["slot_dirt_min"] = round(float(dm[slot_zone].min()) if slot_zone.any() else 1.0, 3)

    # ---- clamp + invariants -----------------------------------------------------------------------
    B[:, :3] = np.clip(B[:, :3], 0.0, 1.0)
    Mk[:, 0] = np.clip(Mk[:, 0], 0.0, 1.0)
    Mk[:, 3] = np.clip(Mk[:, 3], 0.05, 0.95)
    Mk[:, 1] = 0.0
    Mk[:, 2] = 0.0
    Mk[~galv, 0] = 0.0                       # metal only on the bare galvanized zone
    Nm[:, 3] = 1.0

    out = []
    for src_arr, vals in ((base, B), (nrm, Nm), (msk, Mk), (ctl, C)):
        a = src_arr.reshape(-1, 4).copy()
        a[F.idx] = vals.astype(np.float32)
        out.append(a.reshape(A, A, 4))
    base2, nrm2, msk2, ctl2 = out
    ctl2[..., 3] = ctl[..., 3]
    base2, nrm2, msk2, ctl2 = _dilate((base2, nrm2, msk2, ctl2), F.cov, steps=10)
    return base2, nrm2, msk2, ctl2, stats


class WeatherHook:
    """post_compose(base, nrm, msk, ctl, ao, mesh, A) -> (base, nrm, msk, ctl)."""

    def __init__(self, obj, name, step=2, key=None, skip=()):
        self.obj = obj
        self.name = name
        self.step = int(step)
        self.key = key
        self.skip = tuple(skip)
        self.fields = None
        self.report = {}
        self.before = None

    def __call__(self, base, nrm, msk, ctl, ao, mesh, A):
        M = [list(r) for r in self.obj.matrix_world]
        F = build_fields(mesh, A, M)
        self.fields = F
        enc = lambda b: np.concatenate([lin_to_srgb(b[..., :3]), b[..., 3:]], -1)
        before = measure(enc(base), msk, ctl, F)
        hf0 = albedo_hf(enc(base), F)
        bb = enc(base).copy(); bb[..., 3] = 1.0
        self.before = {"BaseColor": bb, "Normal": nrm.copy(), "MaskMap": msk.copy(),
                       "ControlMask": ctl.copy()}
        b2, n2, m2, c2, stats = weather(base, nrm, msk, ctl, ao, F, self.name, mesh=mesh, matrix=M,
                                        step=self.step, key=self.key, skip=self.skip)
        after = measure(enc(b2), m2, c2, F)
        hf1 = albedo_hf(enc(b2), F)
        # guards
        if np.any(m2[..., 1] > 1e-6) or np.any(m2[..., 2] > 1e-6):
            raise RuntimeError(f"{self.name}: MaskMap G/B must stay 0")
        if np.any(c2[..., 3] > 1e-6):
            raise RuntimeError(f"{self.name}: ControlMask alpha must stay 0")
        if np.any(c2[..., :3].sum(-1) > 1.0 + 1e-3):
            raise RuntimeError(f"{self.name}: ControlMask channels must stay exclusive")
        for zn in SLOT_ZONES:
            s0 = before.get(zn, {}).get("slot_luma_srgb")
            s1 = after.get(zn, {}).get("slot_luma_srgb")
            if s0 and s1 and s1["mean"] < SLOT_KEEP * s0["mean"]:
                raise RuntimeError(f"{self.name}: slot zone {zn} albedo mean {s1['mean']} < "
                                   f"{SLOT_KEEP} x {s0['mean']} (RECOLOR_ALBEDO rule: the picker "
                                   f"tint must multiply a light base)")
        self.report = {"before_hook": before, "after": after, "stats": stats,
                       "albedo_hf": {"before_hook": hf0, "after": hf1},
                       "covered_texels": int(F.cov.sum()), "A": A, "step": self.step}
        soil = stats.get("soil", {})
        print(f"[weather] {self.name} step {self.step}: N1 {stats.get('N1', {}).get('drawn', {})}, "
              f"soil stains {soil.get('coverage_stains_mean', '-')} total {soil.get('coverage_total_mean', '-')}, "
              f"hf {hf0.get('_all')} -> {hf1.get('_all')}")
        return b2, n2, m2, c2


class ReorientHook:
    """N0 alone, for --repack without --weather (the packer rotates islands): post_compose."""

    def __init__(self, obj, inner=None):
        self.obj = obj
        self.inner = inner
        self.report = {}

    def __call__(self, base, nrm, msk, ctl, ao, mesh, A):
        F = build_fields(mesh, A, [list(r) for r in self.obj.matrix_world])
        Nm, st = reorient_normals(nrm.reshape(-1, 4)[F.idx].astype(np.float64), mesh, F)
        n2 = nrm.reshape(-1, 4).copy()
        n2[F.idx] = Nm.astype(np.float32)
        base, n2, msk, ctl = _dilate((base, n2.reshape(A, A, 4), msk, ctl), F.cov, steps=10)
        self.report = st
        if self.inner is not None:
            return self.inner(base, n2, msk, ctl, ao, mesh, A)
        return base, n2, msk, ctl


def make_hook(obj, name, step=2, key=None, skip=()):
    return WeatherHook(obj, name, step=step, key=key, skip=skip)
