"""
snow_weather.py - texture-side weathering + smoothness parity for the generation-B snowmaking
props (2026-10-17). Write-up: docs/research/art/snow-props-weather-2026-10-17.md.

Why texture-side: every B class sits 1-8 tris under its HARD cap (snow-props-detail-2026-10-16.md
section 10.3, DECISIONS 254), so weathering is texture work, never triangles. This module
changes PIXELS ONLY: it runs inside lift_tower.apply_finish_textures' `post_compose` slot
(linear light, AO already multiplied, stamped strips already restored, before the sRGB
encode) and returns new base / normal / mask / control arrays. The mesh, its UVs, its part
table, the LOD chain and every file name and size are untouched - the FBX is byte-for-byte
the same geometry with or without it (proved per class in the write-up, section 6).

Inputs per texel come from rasterising the finished mesh into its own UVUnique layout:
face id, world position, interpolated normal, the atlas ZONE of the face (from the face's
UVAtlas centroid, cs2_asset.snow_prop_zones - so the material class is exact, not guessed
from colour), the PART name (the "part" face attribute + cs2_part_table), the distance to
the face's own feature edges (convex dihedral > 25 deg or open rim) and texels per metre.

Treatments, each a PARTIAL overlay driven by world-metre value noise (cs2_weather.fbm, the
buildings' vocabulary: never a uniform tone shift), each with the reference photo it came
from (Desktop/ski resort assets reference images/ski snowmakers/):
  T0 class parity     smoothness / metal per zone onto the measured vanilla bands; galvanised
                      albedo lifted to the vanilla metal band (write-up section 2)
  T1 galv mottle      dull zinc mottle + white-rust patches          (shop close-up, "50" hydrant)
  T2 salt/water       streaks along masts, columns, lance tubes      (shop A-frame, lance photo)
  T3 rust bloom       welds, flanges, nuts, cast feet, clamps + short rust runs below them
                                                                      (shop ring nuts, "fan gun in foreground")
  T4 ground grime     dirt band at grade, foot grime on pads, lid and floor plate, mud on tyres
                                                                      (shop floor, "fan gun in foreground")
  T5 paint wear       chips to primer at convex edges (+ rare field chips), heavier on the
                      fan-gun bell and the lance jacket; dirt streaks on the housing
                                                                      (shop close-up: TR8 / TR10 bodies)
  T6 soot / oil       compressor intakes and oil spots under them    (lance photo, shop floor)
  T7 guard rust       the fan guard mesh a little rusty              ("fan gun in foreground" guard)
  T8 relief           a shallow normal-map relief for chips and rust, renormalised

Contract rules kept in code (guards raise):
  * recolourable texels (ControlMask slot > 0.5) keep a LIGHT albedo: the dirt factor on them
    is clamped (DIRT_FLOOR) so the picker tint still multiplies correctly (RECOLOR_ALBEDO);
    paint chips REMOVE the slot (ctl -> 0) instead of darkening it, so a chip reads as primer
    under any livery;
  * BaseColor alpha stays 1.0 (apply_finish_textures writes it after the hook; asserted);
  * MaskMap G and B stay 0, ControlMask alpha stays 0, ControlMask channels stay exclusive
    (only ever multiplied down);
  * metal only on real metal: galvanised / aluminium zone (and never under rust, white rust,
    chips or dirt).

Public API:
    make_hook(obj, name) -> WeatherHook   (callable with the post_compose signature)
    hook.fields, hook.report               (after the bake ran)
    measure(base, msk, ctl, fields)        per-zone stats (used for before / after tables)
    build_fields(mesh, A, matrix)          the raster (also used to measure exported maps)
Determinism: seeds are zlib.crc32(f"{name}:{effect}"); no RNG state, no clock.
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
from cs2_weather import fbm, uniform, smoothstep  # noqa: E402  (read-only reuse of the noise)

LUMA = np.array((0.2126, 0.7152, 0.0722), dtype=np.float32)

# ---------------------------------------------------------------------------------------
# T0: class parity. Targets from the measured vanilla bands (write-up section 2):
#   vanilla metal (R = 1.0) on utility props: smoothness 0.47-0.62 (fence 0.51, antenna 0.47,
#   oil pump 0.51, generator 0.59, substation 0.62), albedo sRGB luma 0.51-0.84 (fence 0.72);
#   vanilla painted / dielectric utility props 0.18-0.73, median ~0.44 (electrical box 0.18,
#   hydrant 0.38, substation 0.38, oil pump 0.44); building walls 0.25-0.38, glass 0.69.
# (smooth target, gain on the existing variation, metal)
# ---------------------------------------------------------------------------------------
ZONE_TARGET = {
    "concrete":  (0.38, 0.60, 0.0),
    "galv":      (0.54, 0.50, 1.0),
    "livery":    (0.42, 0.50, 0.0),
    "accent":    (0.38, 0.50, 0.0),
    "plastic":   (0.40, 0.50, 0.0),
    "rubber":    (0.18, 1.00, 0.0),
    "grate":     (0.30, 1.00, 0.0),
    "amber":     (0.70, 1.00, 0.0),
    "plate":     (0.40, 1.00, 0.0),
    "reflect":   (0.55, 1.00, 0.0),
    "chevron":   (0.38, 1.00, 0.0),
    "label":     (0.38, 1.00, 0.0),
}
GALV_LUMA_SRGB = 0.64        # clean galvanised albedo (vanilla metal band 0.51-0.84, fence 0.72)
GALV_DESAT = 0.80            # pull Metal032's blue cast toward neutral
SLOT_ZONES = {"livery": 0, "plastic": 1, "accent": 2}    # zone -> ControlMask channel
SLOT_KEEP = 0.90             # guard: slot-texel albedo mean (sRGB) stays >= 90 % of the B value
DIRT_FLOOR = 0.78            # slot texels: weathering may darken to 78 % at most (chips excepted)

# Linear-light colours
RUST_LIN = np.array((0.205, 0.072, 0.024), np.float32)       # = cs2_weather.RUST_LIN
RUST_BLOOM_LIN = np.array((0.30, 0.115, 0.035), np.float32)  # fresh orange bloom
ZINC_WHITE_LIN = np.array((0.42, 0.42, 0.40), np.float32)    # white rust / salt (sRGB ~0.68)
GRIME_MUL = np.array((0.60, 0.56, 0.50), np.float32)         # = cs2_weather.GRIME_MUL
MUD_LIN = np.array((0.075, 0.058, 0.040), np.float32)
PRIMER_LIN = np.array((0.16, 0.16, 0.15), np.float32)        # grey primer (sRGB ~0.44)
SOOT_LIN = np.array((0.018, 0.017, 0.016), np.float32)
OIL_LIN = np.array((0.012, 0.011, 0.010), np.float32)

STRIP_ZONES = ("amber", "plate", "chevron", "reflect", "label", "floodlens")
NO_WEATHER_PREFIXES = ("HeadBlades", "GunBlades", "HeadSpinner", "GunSpinner", "HeadFan",
                       "GunFan", "HeadBore", "GunBore", "Beacon", "Marker")

# T3 rust sources: prefix -> (coverage on the part, run length below it in m)
RUST_SOURCES = (
    (("FootNut", "HeadBolt", "GunBolt", "HeadStud", "GunStud"), 0.45, 0.14),
    (("Flange", "RiserFlange", "MastCollar", "WeldCollar"), 0.28, 0.55),
    (("MastFoot", "ColFoot", "PipeFoot", "JackFoot", "BasePlate"), 0.24, 0.0),
    (("Clamp", "HoseClamp", "Gland", "Coupling", "Hinge", "Pin", "HubCap", "Elbow"), 0.30, 0.25),
    (("Standoff", "Knee", "Cheek", "Stub", "RiserStub", "Axle"), 0.20, 0.30),
    (("HeadCollar", "GunCollar"), 0.12, 0.10),
)
STREAK_PREFIXES = ("Mast", "Column", "Lance", "Riser", "Stile", "Pipe", "Pedestal", "Brace",
                   "Chassis", "Cross", "DrawbarL", "DrawbarR", "Rail", "CageBar", "Jack",
                   "HeadForkArm", "GunForkArm", "Body", "Outlet")
HEAVY_WEAR = ("GunBell", "HeadBell", "BaseJacket")
# = snow_props.BEVEL_PREFIXES (copied: this module must not import the generator)
CHAMFER_PREFIXES = ("Pad", "BasePlate", "Cabinet", "Platform", "Body", "Lid", "Compressor",
                    "DriveHousing", "PushPlate", "HeadCompressor")
COMPRESSOR_SOURCES = ("CompIntake", "LouvreCompressor", "HeadCompressor")
GUARD_PREFIXES = ("HeadGuard", "GunGuard")


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
    """Rasterise `me` into an A x A grid of its `uv_layer`. Returns Fields."""
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

    # feature edges (where paint chips and rust start): open rims always; convex dihedrals
    # > 40 deg on the chamfered boxes (their 1-segment chamfer splits a 90 deg corner into two
    # 45 deg edges), > 60 deg elsewhere - so the 8-14 sided lathes (bell 45 deg, housing 30 deg
    # between segments) do NOT count every facet seam as an edge.
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
                        rh=float(np.max(np.linalg.norm((X - c)[:, :2], axis=1))))
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


# ---------------------------------------------------------------------------------------
# The weathering itself
# ---------------------------------------------------------------------------------------

def _n01(F, cells, name, effect, octaves=3, coords=None):
    c = F.P if coords is None else coords
    return uniform(fbm(c, cells, F.ppm, _seed(name, effect), octaves=octaves))


def _axis_coords(F, sel):
    """Per texel (across1, across2, along) in its part's principal frame (streak coordinates)."""
    out = np.zeros((int(sel.sum()), 3))
    pids = F.part_id[sel]
    Psel = F.P[sel]
    for k in np.unique(pids):
        pp = F.parts[int(k)]
        a = pp["axis"] / max(np.linalg.norm(pp["axis"]), 1e-9)
        if pp["elong"] < 3.0 or pp["length"] < 0.6:
            a = np.array((0.0, 0.0, 1.0))
        t = np.array((1.0, 0.0, 0.0)) if abs(a[0]) < 0.9 else np.array((0.0, 1.0, 0.0))
        e1 = np.cross(a, t); e1 /= np.linalg.norm(e1)
        e2 = np.cross(a, e1)
        s = pids == k
        X = Psel[s]
        out[s] = np.stack([X @ e1, X @ e2, X @ a], 1)
    return out


def weather(base, nrm, msk, ctl, ao, F, name):
    """All treatments on covered texels. Arrays are (A, A, 4) linear; returns new arrays + stats."""
    A = F.A
    flat = lambda arr: arr.reshape(-1, 4)[F.idx].astype(np.float64)
    B = flat(base); Nm = flat(nrm); Mk = flat(msk); C = flat(ctl)
    occ = (ao.reshape(-1, 4)[F.idx, 0].astype(np.float64) * 0.92 + 0.08) if ao is not None \
        else np.ones(len(F.idx))
    n = len(F.idx)
    z = F.P[:, 2] - F.zmin
    Nz = F.N[:, 2]
    zone = F.zone
    stats = {}
    w = F.w

    def share(mask, sel=None):
        sel = np.ones(n, bool) if sel is None else sel
        return round(float((np.clip(mask, 0, 1)[sel] * w[sel]).sum() / max(w[sel].sum(), 1e-12)), 3)

    exempt = _startswith(F, NO_WEATHER_PREFIXES)
    is_strip = np.isin(zone, STRIP_ZONES)
    galv = (zone == "galv")
    paint = np.isin(zone, ("livery", "accent")) | ((zone == "plastic") & _startswith(F, ("BaseJacket",)))
    slot_zone = np.isin(zone, tuple(SLOT_ZONES))
    dirt_mul = np.ones(n)                       # tracked on slot texels for the RECOLOR guard

    # ---- T0 class parity -------------------------------------------------------------
    for zn, (tgt, gain, metal) in ZONE_TARGET.items():
        sel = zone == zn
        if not sel.any():
            continue
        mean = float((Mk[sel, 3] * w[sel]).sum() / w[sel].sum())
        Mk[sel, 3] = np.clip(tgt + (Mk[sel, 3] - mean) * gain, 0.05, 0.95)
        Mk[sel, 0] = metal
    if galv.any():
        raw = B[galv, :3] / occ[galv, None]
        lum = raw @ LUMA
        grey = lum[:, None] * np.ones(3)
        raw = raw * (1.0 - GALV_DESAT) + grey * GALV_DESAT
        k = float(srgb_to_lin(GALV_LUMA_SRGB)) / max(float((raw @ LUMA * w[galv]).sum() / w[galv].sum()), 1e-6)
        B[galv, :3] = np.clip(raw * k, 0, 1) * occ[galv, None]
        stats["galv_albedo_scale"] = round(k, 3)

    # ---- T1 galvanised mottle + white rust (shop close-up A-frame; "50" hydrant) --------
    sel = galv & ~exempt
    if sel.any():
        big = _n01(F, 0.20, name, "galv_mottle")[sel]
        fine = _n01(F, 0.05, name, "galv_fine")[sel]
        wr = smoothstep(0.60, 0.86, big)                       # ~20-25 % white-rust patches
        B[sel, :3] = (B[sel, :3] * (1 - 0.30 * wr[:, None]) + ZINC_WHITE_LIN * occ[sel, None] * 0.30 * wr[:, None])
        B[sel, :3] *= (1.0 - 0.10 * fine)[:, None]
        Mk[sel, 0] = 1.0 - 0.75 * wr
        Mk[sel, 3] = Mk[sel, 3] * (1 - wr) + 0.28 * wr - 0.05 * fine
        stats["T1_white_rust_share_galv"] = share(_put(n, sel, wr), galv)

    # ---- T2 salt / water streaks along masts, columns, lance tubes -------------------
    elong = _startswith(F, STREAK_PREFIXES) & galv & ~exempt
    vert = galv & ~exempt & ~elong & (np.abs(Nz) < 0.75)
    sel = elong | vert
    if sel.any():
        Q = _axis_coords(F, sel)
        s1 = uniform(fbm(Q, (0.022, 0.022, 0.9), F.ppm[sel], _seed(name, "salt"), octaves=3))
        s2 = uniform(fbm(Q, (0.030, 0.030, 0.7), F.ppm[sel], _seed(name, "water"), octaves=3))
        salt = smoothstep(0.70, 0.84, s1)
        water = smoothstep(0.68, 0.82, s2)
        B[sel, :3] = B[sel, :3] * (1 - 0.22 * salt[:, None]) + ZINC_WHITE_LIN * occ[sel, None] * 0.22 * salt[:, None]
        B[sel, :3] *= (1.0 - 0.18 * water)[:, None]
        Mk[sel, 0] *= (1.0 - 0.5 * salt)
        Mk[sel, 3] -= 0.07 * water + 0.05 * salt
        # underside grime line on leaning tubes (lance): water runs along the bottom
        under = smoothstep(-0.25, -0.85, Nz[sel]) * _startswith(F, ("Lance", "Riser", "Brace"))[sel]
        B[sel, :3] *= (1.0 - 0.22 * under * (0.6 + 0.4 * s2))[:, None]
        stats["T2_streak_share_galv"] = share(_put(n, sel, np.maximum(salt, water)), galv)

    # ---- T3 rust bloom at welds / flanges / nuts / feet + runs below them -------------
    rust_all = np.zeros(n)
    rust_runs = np.zeros(n)
    rn = _n01(F, 0.028, name, "rust")
    rn2 = _n01(F, 0.012, name, "rust_fine")
    for prefixes, cov, run in RUST_SOURCES:
        src = _startswith(F, prefixes) & galv & ~exempt
        if not src.any():
            continue
        # on the part: noise coverage, more at its bottom and edges
        pids = np.unique(F.part_id[src])
        lo = np.array([F.parts[int(k)]["lo"][2] for k in pids])
        hi = np.array([F.parts[int(k)]["hi"][2] for k in pids])
        lut_lo = dict(zip(pids.tolist(), lo)); lut_hi = dict(zip(pids.tolist(), hi))
        zl = np.array([lut_lo[int(k)] for k in F.part_id[src]])
        zh = np.array([lut_hi[int(k)] for k in F.part_id[src]])
        low = 1.0 - np.clip((F.P[src, 2] - zl) / np.maximum(zh - zl, 1e-3), 0, 1)
        edge = 1.0 - smoothstep(0.004, 0.02, F.ED[src])
        score = rn[src] * 0.65 + 0.20 * rn2[src] + 0.30 * low * low + 0.12 * edge - 0.08
        r = smoothstep(1.0 - cov, 1.0 - cov + 0.12, score)
        rust_all[src] = np.maximum(rust_all[src], r)
        # runs below each source part, on galvanised texels (not the source itself)
        if run > 0:
            tgt = galv & ~exempt & ~src
            if tgt.any():
                Pt = F.P[tgt]
                col = uniform(fbm(Pt * np.array((1, 1, 1)), (0.012, 0.012, 0.35), F.ppm[tgt],
                                  _seed(name, "rustrun"), octaves=2))
                acc = np.zeros(int(tgt.sum()))
                for k in pids:
                    pp = F.parts[int(k)]
                    dz = pp["lo"][2] - Pt[:, 2]
                    dh = np.linalg.norm(Pt[:, :2] - pp["c"][:2], axis=1)
                    inside = (dz > -0.005) & (dz < run) & (dh <= pp["rh"] + 0.012)
                    if not inside.any():
                        continue
                    fade = np.clip(1.0 - dz / run, 0, 1) ** 1.5
                    acc = np.maximum(acc, inside * fade)
                rr = acc * smoothstep(0.62, 0.78, col)
                rust_runs[tgt] = np.maximum(rust_runs[tgt], rr)
    if rust_all.any() or rust_runs.any():
        r = np.clip(rust_all, 0, 1)
        rcol = RUST_LIN * (1 - rn2[:, None] * 0.3) + RUST_BLOOM_LIN * (rn2[:, None] * 0.3)
        B[:, :3] = B[:, :3] * (1 - 0.72 * r[:, None]) + rcol * occ[:, None] * 0.72 * r[:, None]
        Mk[:, 0] *= (1.0 - r)
        Mk[:, 3] = Mk[:, 3] * (1 - r) + 0.15 * r
        q = np.clip(rust_runs, 0, 1) * 0.45
        B[:, :3] = B[:, :3] * (1 - q[:, None]) + RUST_BLOOM_LIN * occ[:, None] * q[:, None]
        Mk[:, 0] *= (1.0 - 0.8 * q)
        Mk[:, 3] -= 0.10 * q
        stats["T3_rust_share_galv"] = share(np.maximum(r, q), galv)

    # ---- T7 guard mesh a little rusty ("fan gun in foreground": the guard) -------------
    gsel = _startswith(F, GUARD_PREFIXES)
    if gsel.any():
        gl = _n01(F, 0.05, name, "guard")[gsel]
        pids = F.part_id[gsel]
        zl = np.array([F.parts[int(k)]["lo"][2] for k in pids])
        zh = np.array([F.parts[int(k)]["hi"][2] for k in pids])
        low = 1.0 - np.clip((F.P[gsel, 2] - zl) / np.maximum(zh - zl, 1e-3), 0, 1)
        g = smoothstep(0.62, 0.80, gl + 0.12 * low) * 0.7
        B[gsel, :3] = B[gsel, :3] * (1 - g[:, None]) + RUST_LIN * occ[gsel, None] * g[:, None]
        Mk[gsel, 3] = Mk[gsel, 3] * (1 - g) + 0.18 * g
        stats["T7_guard_rust_share"] = share(_put(n, gsel, g / 0.7), gsel)

    # ---- T5 paint wear: chips to primer at edges, heavier on bell + jacket --------------
    chip = np.zeros(n)
    if paint.any():
        heavy = _startswith(F, HEAVY_WEAR)
        wpart = np.where(heavy, 0.030, 0.018)
        wedge = np.maximum(wpart, 2.5 / np.maximum(F.ppm, 1e-3))
        e = 1.0 - smoothstep(0.3 * wedge, wedge, F.ED)
        c1 = _n01(F, 0.045, name, "chip_edge")
        c2 = _n01(F, 0.022, name, "chip_field")
        thr = np.where(heavy, 0.55, 0.76)
        ce = smoothstep(thr, thr + 0.06, e * (0.35 + 0.65 * c1))
        fthr = np.where(heavy, 0.982, 0.994)
        cf = smoothstep(fthr, fthr + 0.012, c2) * (1.0 - 0.5 * smoothstep(0.3, 0.9, Nz))
        chip = np.where(paint & ~exempt, np.maximum(ce, cf), 0.0)
        for zn, ch in SLOT_ZONES.items():
            s = zone == zn
            C[s, ch] *= (1.0 - chip[s])
        B[:, :3] = B[:, :3] * (1 - chip[:, None]) + PRIMER_LIN * occ[:, None] * chip[:, None]
        Mk[:, 0] *= (1.0 - chip)
        Mk[:, 3] = Mk[:, 3] * (1 - chip) + 0.30 * chip
        stats["T5_chip_share_paint"] = share(chip, paint)
        hv = paint & heavy
        if hv.any():
            stats["T5_chip_share_bell_jacket"] = share(chip, hv)
        # dirt streaks on the housing (vertical faces) + dust on the tops
        ps = paint & ~exempt
        st = uniform(fbm(F.P[ps], (0.035, 0.035, 0.6), F.ppm[ps], _seed(name, "paint_streak"), octaves=3))
        sk = smoothstep(0.66, 0.82, st) * (1.0 - smoothstep(0.5, 0.9, np.abs(Nz[ps])))
        top = smoothstep(0.45, 0.9, Nz[ps]) * smoothstep(0.50, 0.70, _n01(F, 0.12, name, "paint_dust")[ps])
        dm = (1.0 - 0.14 * sk) * (1.0 - 0.14 * top)
        dirt_mul[ps] *= dm
        Mk[ps, 3] -= 0.06 * sk + 0.08 * top
        stats["T5_dirt_share_paint"] = share(_put(n, ps, np.maximum(sk, top)), paint)

    # ---- T6 soot / oil at compressor intakes ------------------------------------------
    srcm = _startswith(F, COMPRESSOR_SOURCES)
    if srcm.any():
        sn = _n01(F, 0.05, name, "soot")
        soot = np.zeros(n)
        oil = np.zeros(n)
        on = _n01(F, 0.05, name, "oil")
        for k in np.unique(F.part_id[srcm]):
            pp = F.parts[int(k)]
            lo, hi = pp["lo"], pp["hi"]
            d = np.linalg.norm(np.maximum(np.maximum(lo - F.P, F.P - hi), 0.0), axis=1)
            near = np.clip(1.0 - d / 0.14, 0, 1)
            own = (F.part_id == k)
            under = smoothstep(0.2, -0.6, Nz)
            s = np.where(own, 0.55 + 0.45 * under, near * 0.8) * smoothstep(0.30, 0.65, sn)
            soot = np.maximum(soot, s)
            # oil spots on up-facing surfaces under the compressor footprint
            foot = ((F.P[:, 0] > lo[0] - 0.12) & (F.P[:, 0] < hi[0] + 0.12) &
                    (F.P[:, 1] > lo[1] - 0.12) & (F.P[:, 1] < hi[1] + 0.12) &
                    (F.P[:, 2] < lo[2] + 0.01) & (F.P[:, 2] > lo[2] - 1.0) & (Nz > 0.6))
            oil = np.maximum(oil, foot * smoothstep(0.62, 0.74, on))
        soot = np.where(exempt | is_strip, 0.0, soot)
        oil = np.where(exempt | is_strip, 0.0, oil)
        slot_t = slot_zone & (chip < 0.5)
        # on slot texels soot is a bounded dirt factor (the tint must still multiply)
        dirt_mul[slot_t] *= (1.0 - 0.55 * soot[slot_t])
        ns = ~slot_t
        B[ns, :3] = B[ns, :3] * (1 - 0.75 * soot[ns, None]) + SOOT_LIN * 0.75 * soot[ns, None]
        B[:, :3] = B[:, :3] * (1 - 0.8 * oil[:, None]) + OIL_LIN * 0.8 * oil[:, None]
        Mk[:, 0] *= (1.0 - soot) * (1.0 - oil)
        Mk[:, 3] = Mk[:, 3] * (1 - soot) + 0.16 * soot
        Mk[:, 3] = np.minimum(Mk[:, 3] * (1 - oil) + 0.42 * oil, np.maximum(Mk[:, 3], 0.42))
        stats["T6_soot_area_m2"] = round(float((soot * w).sum()), 3)
        stats["T6_oil_area_m2"] = round(float((oil * w).sum()), 3)

    # ---- T4 ground grime, foot grime on pads / lid / floor, mud on tyres --------------
    gn = _n01(F, 0.10, name, "grime")
    band = (1.0 - np.clip(z / 0.45, 0, 1)) ** 1.2
    g = band * (0.45 + 0.55 * gn)
    g = np.where(exempt | is_strip, 0.0, g)
    up = Nz > 0.65
    pad_top = (zone == "concrete") & up
    if pad_top.any():
        pn = _n01(F, 0.22, name, "pad_foot")[pad_top]
        r = np.linalg.norm(F.P[pad_top, :2], axis=1)
        ring = 1.0 - smoothstep(0.25, 0.65, r)
        edgeg = 1.0 - smoothstep(0.01, 0.06, F.ED[pad_top])
        g[pad_top] = np.maximum(g[pad_top], smoothstep(0.40, 0.70, pn) * 0.6 + 0.30 * ring + 0.25 * edgeg)
    lid = _startswith(F, ("Lid",)) & up
    if lid.any():
        ln = _n01(F, 0.07, name, "lid_boot")[lid]
        g[lid] = np.maximum(g[lid], smoothstep(0.50, 0.72, ln) * 0.9)
    floor = _startswith(F, ("Floor", "Deck", "PushPlate")) & up
    if floor.any():
        fnz = _n01(F, 0.12, name, "floor_foot")[floor]
        g[floor] = np.maximum(g[floor], smoothstep(0.45, 0.70, fnz) * 0.8)
    g = np.clip(g, 0, 1)
    ns = ~slot_zone
    B[ns, :3] *= (1.0 - g[ns, None] * (1.0 - GRIME_MUL) * 0.9)
    dirt_mul[slot_zone] *= (1.0 - 0.30 * g[slot_zone])
    Mk[:, 0] *= (1.0 - 0.6 * g)
    Mk[:, 3] -= 0.08 * g
    stats["T4_grime_share_all"] = share(g)
    if pad_top.any():
        stats["T4_grime_share_pad_top"] = share(g, pad_top)
    if lid.any():
        stats["T4_grime_share_lid_top"] = share(g, lid)
    tyre = _startswith(F, ("Wheel", "Castor")) & (zone == "rubber")
    if tyre.any():
        pids = F.part_id[tyre]
        zc = np.array([0.5 * (F.parts[int(k)]["lo"][2] + F.parts[int(k)]["hi"][2]) for k in pids])
        zl = np.array([F.parts[int(k)]["lo"][2] for k in pids])
        lowt = 1.0 - np.clip((F.P[tyre, 2] - zl) / np.maximum(zc - zl, 1e-3), 0, 1)
        mud = np.clip(lowt * 0.6 + 0.4 * smoothstep(0.45, 0.75, _n01(F, 0.05, name, "mud")[tyre]), 0, 1) * 0.75
        B[tyre, :3] = B[tyre, :3] * (1 - mud[:, None]) + MUD_LIN * occ[tyre, None] * mud[:, None]
        Mk[tyre, 3] = Mk[tyre, 3] * (1 - mud) + 0.12 * mud
        stats["T4_mud_share_tyres"] = share(_put(n, tyre, mud / 0.75), tyre)
    hose = _startswith(F, ("Hose", "Coil", "Feed", "HoseLead", "Conduit")) & (zone == "rubber") & (z < 0.25)
    if hose.any():
        dust = 0.35 * smoothstep(0.4, 0.7, _n01(F, 0.06, name, "hose_dust")[hose])
        B[hose, :3] = B[hose, :3] * (1 - dust[:, None]) + MUD_LIN * 1.6 * occ[hose, None] * dust[:, None]

    # ---- RECOLOR guard: slot texels keep a light albedo; apply the bounded dirt -----------
    dm = np.clip(dirt_mul, DIRT_FLOOR, 1.0)
    on_slot = slot_zone & (chip < 0.5)
    B[on_slot, :3] *= dm[on_slot, None]
    stats["slot_dirt_min"] = round(float(dm[on_slot].min()) if on_slot.any() else 1.0, 3)

    # ---- T8 relief: rust bumps raised, chips recessed, then renormalise all covered -------
    h = 0.35 * rust_all * (rn2 - 0.5) + 0.25 * rust_all - 0.45 * chip
    H = _scatter(F, h)
    cov = F.cov
    gy, gx = np.gradient(H)
    okx = cov & np.roll(cov, 1, 1) & np.roll(cov, -1, 1)
    oky = cov & np.roll(cov, 1, 0) & np.roll(cov, -1, 0)
    gx = np.where(okx, gx, 0.0).ravel()[F.idx]
    gy = np.where(oky, gy, 0.0).ravel()[F.idx]
    v = Nm[:, :3] * 2.0 - 1.0
    v[:, 0] -= gx * 2.0
    v[:, 1] -= gy * 2.0
    v[:, 2] = np.maximum(v[:, 2], 0.05)
    v /= np.maximum(np.linalg.norm(v, axis=1, keepdims=True), 1e-9)
    Nm[:, :3] = v * 0.5 + 0.5
    Nm[:, 3] = 1.0

    # ---- clamp + invariants ---------------------------------------------------------------
    B[:, :3] = np.clip(B[:, :3], 0.0, 1.0)
    Mk[:, 0] = np.clip(Mk[:, 0], 0.0, 1.0)
    Mk[:, 3] = np.clip(Mk[:, 3], 0.05, 0.95)
    Mk[:, 1] = 0.0
    Mk[:, 2] = 0.0
    C[:, :3] = np.clip(C[:, :3], 0.0, 1.0)
    # metal only on real metal: nothing outside the galvanised zone carries metal
    Mk[~galv, 0] = 0.0

    out = []
    for src_arr, vals in ((base, B), (nrm, Nm), (msk, Mk), (ctl, C)):
        a = src_arr.reshape(-1, 4).copy()
        a[F.idx] = vals.astype(np.float32)
        out.append(a.reshape(A, A, 4))
    base2, nrm2, msk2, ctl2 = out
    ctl2[..., 3] = ctl[..., 3]
    base2, nrm2, msk2, ctl2 = _dilate((base2, nrm2, msk2, ctl2), F.cov, steps=10)
    return base2, nrm2, msk2, ctl2, stats


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
    # normals in the gutter: renormalise the averaged vectors
    n = arrays[1]
    v = n[..., :3] * 2.0 - 1.0
    v /= np.maximum(np.linalg.norm(v, axis=-1, keepdims=True), 1e-9)
    n[..., :3] = np.where(done[..., None] & ~cov[..., None], v * 0.5 + 0.5, n[..., :3])
    return arrays


class WeatherHook:
    """post_compose(base, nrm, msk, ctl, ao, mesh, A) -> (base, nrm, msk, ctl)."""

    def __init__(self, obj, name, compare_dir=None):
        self.obj = obj
        self.name = name
        self.compare_dir = compare_dir      # optional: an exported folder to diff "before" against
        self.fields = None
        self.report = {}
        self.before = None

    def __call__(self, base, nrm, msk, ctl, ao, mesh, A):
        M = [list(r) for r in self.obj.matrix_world]
        F = build_fields(mesh, A, M)
        self.fields = F
        enc = lambda b: np.concatenate([lin_to_srgb(b[..., :3]), b[..., 3:]], -1)
        before = measure(enc(base), msk, ctl, F)
        # the maps exactly as the un-weathered export would write them (alpha forced 1, as
        # apply_finish_textures does after the hook) - for the before == exported-B diff
        bb = enc(base).copy(); bb[..., 3] = 1.0
        self.before = {"BaseColor": bb, "Normal": nrm.copy(), "MaskMap": msk.copy(),
                       "ControlMask": ctl.copy()}
        b2, n2, m2, c2, stats = weather(base, nrm, msk, ctl, ao, F, self.name)
        after = measure(enc(b2), m2, c2, F)
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
                       "covered_texels": int(F.cov.sum()), "A": A}
        print(f"[weather] {self.name}: " + ", ".join(f"{k} {v}" for k, v in stats.items()))
        return b2, n2, m2, c2


def make_hook(obj, name):
    return WeatherHook(obj, name)
