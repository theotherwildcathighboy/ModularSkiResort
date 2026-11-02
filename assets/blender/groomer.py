"""
groomer.py - SkiGroomerA, a generic snow groomer (snowcat).

    blender --background --factory-startup --python-exit-code 1 --python assets/blender/groomer.py -- \
            --out <letter folder> --export --preview <folder> --review

USE. A moving prop whose Transform our code writes, following the terrain like the lift chairs (docs/V2-IDEAS.md,
"Driving groomers on the piste"), and the same asset parked. Not a vehicle prefab: no bones and no moving parts in v1, so
the tracks are static geometry that has to read as tracks. Seen from city-builder distance, sometimes in Photo Mode.

DIMENSIONS (references and status of each number: docs/research/art/groomer-refs/SOURCES.md, git-ignored)
    overall length, blade lip to comb tail   8.92 m    PistenBully 600 data sheet (Bison X catalog 8.96, unverified)
    width over tracks                        4.21 m    same sheet: 1.648 m per track, gap 0.914 m
    hull width                               2.50 m    same sheet
    blade                                    4.79 m wide with the wings swept forward (sheet: 4.41 closed, 5.25 open), 1.17 m high
    tiller and comb                          5.30 m    between the Bison X tiller widths (5.365 m raised; repo spec 4.1)
    roof / light bar / beacon top            2.80 / 2.93 / 3.03 m   (sheet 2.88 to the roof fittings; Bison X 3.03, unverified)
    track length                             5.00 m    MEASURED from pb600-burgeralpe-12.jpg and leitwolf-plateau-rosa.jpg (+-0.3 m)
    top of track, road wheels, cleat pitch   0.88 m, 5 wheels 0.77 m apart, 0.30 m   measured from photos (estimates)
The older note of a 0.85 m track unit in docs/research/art/snowmaking-props-spec.md section 4 is what shows outside the
hull from above (pb400-wallis.jpg), not the track.

AXES. Authored with +X = forward, +Y = the machine's left, +Z = up, origin on the ground at the center of the footprint
(blade lip to comb tail, tiller end to tiller end). cs2_asset.orient_for_net + export_fbx bake that to raw FBX (y, z, x)
cm, so forward = CS2 +Z, up = CS2 +Y; the game negates FBX x on import, so an author point (x, y, z) sits at game local
(-y, z, x) (frame rule T, cs2_liftdress.blender_to_game). The track center is TRACK_CENTER_X ahead of the origin: a driver
that samples the terrain under the tracks needs that offset; it is in the manifest.

BUDGET (docs/research/art/snow-gun-pipeline-plan.md, PIPELINE.md Constants). LOD0 3,500 to 5,000 tris, hard. LOD1 <= 50 % of
LOD0, LOD2 <= 500. 1024 x 1024: never 2048 under 10 m (snow-prop-materials.md section 5); the floor is 512. Density
per part per cs2_validate (prop: ERROR < 30, WARN < 120 px/m).

HOW IT IS BUILT. One explicit vertex and face list (no bpy.ops, no canonical sort), so element order is a pure function of
this file and rigid copies keep their loop order. That loop order is what lets rotated copies stack on one UV island:
cleats, road wheels, idlers and lamps are placed through proper rotations of one template (never mirrored), so all share
one island; single-color bars, rods and hoses stack too, each on its biggest copy; the belts share one tile per face through
tile coordinates (Asset.tile). Parts a rigid unfold would shred are mapped by projection (Asset.set_planar). Everything else
is unfolded and shelf-packed by snow_props (_unfold_islands, _pack_once) with the mip-safe margin: at 16 px per island the
island count decides the density, which is why so much stacks. lift_tower.apply_finish_textures bakes the flat zone atlas
onto the unique layout and bakes AO; the post_compose hook paints detail in the final UV space from the author-frame position
of every texel, at twice the atlas size and reduced (PAINT_SS), so seams and holes do not alias into stairs: door seams, slats,
perforation, spokes, belts, comb grooves live in the maps, not the mesh. Stacked islands take a constant occlusion because a
bake would write the AO of whichever copy came last.

LODS. Each level is LOD0 minus whole parts (LOD_DELETE), no collapse. LOD1 has no real cleats: the outer belt tile paints a bar
under every cleat, so it still reads as cleated. Every level keeps the blade and the tiller attached to the machine
(audit_lods_attached), through the push frame and the rear arms, and the parts the top-down camera sees (deck rails, hood panel)
do not pop at the first switch.

COLOR. The red is the game's own fire engine red, baked into BaseColor. Measured on EU_FireTruck01: tint channel 0 (1, 0.2028, 0.2028), read
as sRGB and uploaded linear (BatchDataSystem.cs:716-718), over a flat BaseColor of 180 under its ControlMask, so (180, 33, 33) in the end,
with gloss 0.85 and coat 1. ControlMask is empty, so the machine does not recolor and needs no ColorProperties. One palette: ZONES
below. No brand name, logo or photo texture on the model; names appear only in the source notes above.

GLASS is opaque by design (alpha 255 everywhere, no sub-mesh): a low-frequency gradient with an interior silhouette and high
smoothness.

LIGHTS. One Emissive map (sRGB, RGBA, the texel is the emitted color, black is dark): amber beacons, white roof lamps, two white slits on the
nose panel. The mesh prefab's single light is purpose 71, so it follows the prefab's Night effects, and BaseColor is untouched, so nothing shows
by day. That is what lets the far levels keep their lights: the bar under the lamps and the beacons also emits, hidden by them on LOD0 and LOD1
and on its own on LOD2, where they are deleted. settings.json shares the map with both levels. The engine path, the surgery and the mover's
one small job are in docs/research/groomer-lights-2026-11-01.md.
"""

import argparse
import json
import math
import os
import shutil
import sys

import bpy
import mathutils
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.append(HERE)
import cs2_asset     # noqa: E402
import cs2_compose   # noqa: E402
import cs2_preview as P  # noqa: E402
import cs2_review    # noqa: E402
import cs2_uv        # noqa: E402
import cs2_validate  # noqa: E402
import lift_tower    # noqa: E402
import snow_props    # noqa: E402

NAME = "SkiGroomerA"
ATLAS = 1024
LOD0_MIN, LOD0_CAP = 3500, 5000
LOD2_CAP = 500
RESERVED = (0.0, 0.985, 1.0, 1.0)     # empty top strip: apply_finish_textures falls back to the crash pad without one

# Key stations (author frame). Footprint center is the origin, so the track center is off it.
OVERALL_L = 8.92
TRACK_L = 5.00
TRACK_CENTER_X = 0.21
XF, XR = TRACK_CENTER_X + TRACK_L / 2.0, TRACK_CENTER_X - TRACK_L / 2.0   # track front, track rear
YI, TW = 0.457, 1.648                 # inner edge of a track, width of a track
TB, CH = 0.05, 0.09                   # belt thickness, cleat height
CLEAT_BASE, CLEAT_TOP = 0.05, 0.032   # half widths: a 10 cm bar every 30 cm, about a third of the belt (leitwolf-kronplatz-067.jpg)
ZG = CH                               # outer belt surface on the ground run (cleat tips at z = 0)
FRONT_C, FRONT_R = (XF - 0.32, 0.56), 0.32   # front idler center and outer belt radius
REAR_C, REAR_R = (XR + 0.34, 0.52), 0.34     # rear sprocket center and outer belt radius
GROUND_F, GROUND_R = XF - 1.05, XR + 0.85    # where the ground run ends
WHEEL_R, WHEEL_W, WHEEL_Y = 0.33, 0.30, 1.50
WHEEL_X = (1.65, 0.88, 0.11, -0.66, -1.43)
Z_SILL = 0.98
HALF_L = OVERALL_L / 2.0
TILLER_Y = 2.65
BLADE_Z0 = 0.06                       # blade carried just off the snow, so it does not dig on a terrain crest
BAR_LAMP_Y, BAR_LAMP_Z = (0.48, 0.68, 0.88), 2.862   # the front roof lamps, each side
REAR_LAMP_Y = (0.56, 0.76)            # the rear roof lamps, each side, same height
BEACON_X, BEACON_Y = 2.18, 0.70       # the two beacons on the front bar
GLASS_BASE_Z = 1.40                   # windshield base: the hull rim stays at this height until it reaches the cab's corner
RIM_END_X = 2.44                      # where the rim runs into the cab's corner facet

# (sRGB byte color, metal, gloss)
ZONES = {
    "red":    ((189, 35, 35), 0.0, 0.85),     # ships at a median of (180, 33, 33), EU_FireTruck01's tinted paint, once baked AO has darkened it; its gloss 0.85
    "black":  ((36, 37, 40), 0.0, 0.40),
    "steel":  ((66, 69, 74), 0.5, 0.45),
    "gray":   ((122, 126, 132), 0.6, 0.50),
    "alu":    ((172, 176, 180), 0.85, 0.55),
    "cleat":  ((205, 208, 212), 0.6, 0.55),
    "rubber": ((26, 26, 28), 0.0, 0.22),
    "glass":  ((24, 31, 38), 0.0, 0.93),
    "lens":   ((232, 232, 222), 0.0, 0.92),
    "amber":  ((236, 138, 22), 0.0, 0.85),
    "tail":   ((150, 20, 18), 0.0, 0.80),
    "chrome": ((205, 208, 212), 1.0, 0.85),
}
DARK_ZONES = ("black", "steel", "rubber", "glass")
COAT = {"red": 1.0}                   # MaskMap G: EU_FireTruck01's paint carries a full coat
# (relative px/m. The heroes are small islands shared by dozens of copies, so density there is cheap and shows everywhere)
TIER_WEIGHT = {"shell": 1.0, "mech": 0.7, "small": 0.8, "hidden": 0.5, "stack": 0.9, "hero": 1.6, "lamp": 2.4}

# Every level is the LOD0 mesh minus whole parts, per level (part-name prefixes), with no collapse step: a collapse moved
# the cleat tips and the bounds and left faces with no UV area. The budgets are checked in audit_lod_chain.
LOD_DELETE = (
    ("Cleat", "BladeTeeth", "WheelBand", "Hose", "Wiper", "GrabRail", "DoorHandle", "BladeBackRib", "BladeRib", "BeltInner", "BeltEdge",
     "CrossTube", "Step", "ExhaustTip", "WingRod", "BladeRod", "ArmRod", "TailLamp", "Drum", "BladeHinge",
     "LampRoofR"),
    ("Cleat", "BladeTeeth", "Hose", "Wiper", "Rail", "GrabRail", "DoorHandle", "BladeBackRib", "BladeRib", "BeltInner", "BeltEdge",
     "CrossTube", "Step", "ExhaustTip", "WingRod", "BladeRod", "ArmRod", "TailLamp", "CabPillar", "Drum",
     "BladeHinge", "Lamp", "Beacon", "Mirror", "Exhaust", "WingRam", "BladeLift", "LiftRam", "Wheel",
     "TillerYoke", "HoodVent", "HoodRim", "HoodEnd", "Glass", "BladeRail", "BladeCross", "ArmLower", "MudflapLip", "Hitch",
     "BladeWLTip", "BladeWRTip", "ChassisWall", "ChassisEnd", "Apron", "TillerDrive", "TillerEnd", "LightBarRear"),
)

BANNED_WORDS = ("pisten", "bully", "prinoth", "bison", "leitwolf", "kassbohrer", "kaessbohrer", "logo")


class GroomerAuditError(RuntimeError):
    """A gate of this generator failed. Raised, never printed and ignored."""


# --------------------------------------------------------------------------------------
# Asset: one explicit vertex and face list
# --------------------------------------------------------------------------------------

def _newell(pts):
    n = Vector((0.0, 0.0, 0.0))
    for i, a in enumerate(pts):
        b = pts[(i + 1) % len(pts)]
        n += a.cross(b)
    return n


class Asset:
    def __init__(self):
        self.V, self.F, self.fpart = [], [], []
        self.names, self.zones, self.tiers = [], [], []
        self.meta = {}
        self.stacks = {}
        self.tiles = {}
        self.lights = {}
        self.planar = {}

    def add(self, name, zone, verts, faces, stack=None, tier="shell", raw=False, meta=None):
        """`faces`: index lists, or (index list, wanted direction); the winding flips to match (center-out when no
        direction is given). raw=True keeps the winding as given. Unused vertices are dropped. Returns the face ids."""
        if name in self.names:
            raise ValueError(f"duplicate part name {name}")
        if zone not in ZONES:
            raise ValueError(f"{name}: unknown zone {zone}")
        pts = [Vector(v) for v in verts]
        c = sum(pts, Vector((0.0, 0.0, 0.0))) / max(len(pts), 1)
        self.names.append(name)
        self.zones.append(zone)
        self.tiers.append(tier)
        pid = len(self.names)
        out = []
        for fc in faces:
            idx, want = fc if (isinstance(fc, tuple) and len(fc) == 2 and not isinstance(fc[1], int)) else (fc, None)
            idx = list(idx)
            if len(idx) > 4:
                raise ValueError(f"{name}: {len(idx)}-gon; tessellate it")
            if len(set(idx)) != len(idx):
                raise ValueError(f"{name}: repeated vertex in a face {idx}")
            q = [pts[i] for i in idx]
            n = _newell(q)
            if n.length < 2e-8:
                raise ValueError(f"{name}: degenerate face {idx}")
            if not raw:
                w = Vector(want) if want is not None else (sum(q, Vector((0.0, 0.0, 0.0))) / len(q) - c)
                if n.dot(w) < 0.0:
                    idx = idx[::-1]
            out.append(idx)
        used = sorted({i for f in out for i in f})
        remap = {o: len(self.V) + k for k, o in enumerate(used)}
        self.V.extend(tuple(pts[o]) for o in used)
        ids = []
        for f in out:
            ids.append(len(self.F))
            self.F.append([remap[i] for i in f])
            self.fpart.append(pid)
        if stack is not None:
            st = self.stacks.setdefault(stack, [])
            if st and [len(self.F[i]) for i in st[0]] != [len(self.F[i]) for i in ids]:
                raise ValueError(f"{name}: stack {stack} has a different face layout")
            st.append(ids)
        if meta is not None:
            self.meta[name] = meta
        return ids

    def set_planar(self, name, normal, seed):
        """Map this part by projection onto a plane instead of unfolding it: right-handed, so never mirrored."""
        n = Vector(normal).normalized()
        u = Vector(seed)
        u = (u - n * u.dot(n)).normalized()
        self.planar[name] = (u, n.cross(u))

    def tile(self, key, face, corners=None):
        """Belt tile: the first face with `corners=None` is the canonical island; every later face gets its UVs by
        bilinear lookup inside it from (t, w) per loop, each in 0..1 of the tile."""
        t = self.tiles.setdefault(key, {"canon": None, "dups": []})
        if corners is None:
            if t["canon"] is not None:
                raise ValueError(f"tile {key}: two canonical faces")
            t["canon"] = face
        else:
            t["dups"].append((face, corners))

    def _faces_area(self, ids):
        return round(sum(_newell([Vector(self.V[i]) for i in self.F[f]]).length * 0.5 for f in ids), 6)

    def share_list(self):
        """[(canonical faces, [duplicate faces, ...]), ...] in the cs2_uv._share_loops format."""
        out = []
        for k, st in sorted(self.stacks.items()):
            if len(st) > 1:
                order = sorted(st, key=self._faces_area, reverse=True)
                out.append((order[0], order[1:]))
        for k, t in sorted(self.tiles.items()):
            out.append(([t["canon"]], [[f] for f, _ in t["dups"]]))
        return out

    def dup_faces(self):
        d = set()
        for canon, dups in self.share_list():
            for dp in dups:
                d.update(dp)
        return d

    def stacked_faces(self):
        d = self.dup_faces()
        for canon, dups in self.share_list():
            d.update(canon)
        return d

    def part_faces(self, name):
        pid = self.names.index(name) + 1
        return [i for i, p in enumerate(self.fpart) if p == pid]

    def to_object(self, name=NAME):
        me = bpy.data.meshes.new(name)
        me.from_pydata(self.V, [], [tuple(f) for f in self.F])
        me.update()
        if len(me.polygons) != len(self.F):
            raise GroomerAuditError(f"from_pydata kept {len(me.polygons)} of {len(self.F)} faces")
        attr = me.attributes.new("part", "INT", "FACE")
        attr.data.foreach_set("value", self.fpart)
        rects = zone_rects(ATLAS)
        layer = me.uv_layers.new(name="UVMap")
        uv = np.zeros(len(me.loops) * 2, np.float32)
        k = 0
        for f, pid in zip(self.F, self.fpart):
            r = rects[self.zones[pid - 1]]
            for _ in f:
                uv[k], uv[k + 1] = 0.5 * (r[0] + r[2]), 0.5 * (r[1] + r[3])
                k += 2
        layer.data.foreach_set("uv", uv)
        ob = bpy.data.objects.new(name, me)
        bpy.context.collection.objects.link(ob)
        cs2_asset.store_part_table(ob, self.names)
        return ob


# --------------------------------------------------------------------------------------
# Shapes
# --------------------------------------------------------------------------------------

def box(b, name, zone, lo, hi, skip=(), **kw):
    """Axis-aligned box. skip: any of '+x', '-x', '+y', '-y', '+z', '-z'."""
    x0, y0, z0 = lo
    x1, y1, z1 = hi
    V = [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0), (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]
    all_f = {"-z": ([0, 1, 2, 3], (0, 0, -1)), "+z": ([4, 5, 6, 7], (0, 0, 1)), "-y": ([0, 1, 5, 4], (0, -1, 0)),
             "+y": ([3, 2, 6, 7], (0, 1, 0)), "-x": ([0, 3, 7, 4], (-1, 0, 0)), "+x": ([1, 2, 6, 5], (1, 0, 0))}
    F = [(idx, want) for key, (idx, want) in all_f.items() if key not in skip]
    return b.add(name, zone, V, F, **kw)


def bar(b, name, zone, p0, p1, w, h, up=(0, 0, 1), caps=(False, False), taper=1.0, **kw):
    """Rectangular bar: w along the side axis, h along the bar's own up; `taper` scales the p1 end."""
    a, c = Vector(p0), Vector(p1)
    d = (c - a).normalized()
    ref = Vector(up)
    if abs(d.dot(ref.normalized())) > 0.95:
        ref = Vector((1, 0, 0))
    side = d.cross(ref).normalized()
    upv = side.cross(d).normalized()
    corners = [(+1, +1), (-1, +1), (-1, -1), (+1, -1)]
    V = []
    for p, s in ((a, 1.0), (c, taper)):
        for sx, sy in corners:
            V.append(p + side * (sx * w * 0.5 * s) + upv * (sy * h * 0.5 * s))
    F = []
    for k in range(4):
        k2 = (k + 1) % 4
        want = side * (corners[k][0] + corners[k2][0]) + upv * (corners[k][1] + corners[k2][1])
        F.append(([k, k2, 4 + k2, 4 + k], want))
    if caps[0]:
        F.append(([0, 1, 2, 3], -d))
    if caps[1]:
        F.append(([4, 5, 6, 7], d))
    return b.add(name, zone, V, F, **kw)


def _frame(axis):
    """Right-handed frame (u, v, a) with u x v = a; deterministic."""
    a = Vector(axis).normalized()
    ref = Vector((0, 0, 1)) if abs(a.z) < 0.9 else Vector((1, 0, 0))
    u = ref.cross(a).normalized()
    v = a.cross(u)
    return u, v, a


def tube(b, name, zone, p0, p1, r, n=6, caps=(False, True), r1=None, **kw):
    a, c = Vector(p0), Vector(p1)
    u, v, ax = _frame(c - a)
    r1 = r if r1 is None else r1
    V = []
    for p, rr in ((a, r), (c, r1)):
        for j in range(n):
            ang = 2.0 * math.pi * j / n
            V.append(p + u * (math.cos(ang) * rr) + v * (math.sin(ang) * rr))
    F = []
    for j in range(n):
        j2 = (j + 1) % n
        ang = 2.0 * math.pi * (j + 0.5) / n
        F.append(([j, j2, n + j2, n + j], u * math.cos(ang) + v * math.sin(ang)))
    if caps[0]:
        F += [([0, j, j + 1], -ax) for j in range(1, n - 1)]
    if caps[1]:
        F += [([n, n + j, n + j + 1], ax) for j in range(1, n - 1)]
    return b.add(name, zone, V, F, **kw)


def hose(b, name, zone, pts, r, n=4, smooth=2, **kw):
    """Tube through a polyline, Catmull-Rom smoothed, with mitered rings, parallel-transported so it never twists; the ends
    are buried in what they connect."""
    Q = [Vector(p) for p in pts]
    P_ = []
    for i in range(len(Q) - 1):
        p0, p1, p2, p3 = Q[max(i - 1, 0)], Q[i], Q[i + 1], Q[min(i + 2, len(Q) - 1)]
        for k in range(smooth):
            t = k / smooth
            P_.append(0.5 * ((2.0 * p1) + (p2 - p0) * t + (2.0 * p0 - 5.0 * p1 + 4.0 * p2 - p3) * t * t + (3.0 * p1 - p0 - 3.0 * p2 + p3) * t * t * t))
    P_.append(Q[-1])
    tans = []
    for i in range(len(P_)):
        t = (P_[min(i + 1, len(P_) - 1)] - P_[max(i - 1, 0)]).normalized()
        tans.append(t)
    u, v, _ = _frame(tans[0])
    V = []
    frames = []
    for i, t in enumerate(tans):
        if i:
            rot = tans[i - 1].rotation_difference(t)
            u, v = rot @ u, rot @ v
        frames.append((u.copy(), v.copy()))
        for j in range(n):
            ang = 2.0 * math.pi * j / n
            V.append(P_[i] + u * (math.cos(ang) * r) + v * (math.sin(ang) * r))
    F = []
    for i in range(len(P_) - 1):
        uu, vv = frames[i]
        for j in range(n):
            j2 = (j + 1) % n
            ang = 2.0 * math.pi * (j + 0.5) / n
            F.append(([i * n + j, i * n + j2, (i + 1) * n + j2, (i + 1) * n + j], uu * math.cos(ang) + vv * math.sin(ang)))
    return b.add(name, zone, V, F, **kw)


def loft(b, name, zone, rings, closed=True, skip=None, want=None, caps=(False, False), cap_want=(None, None), **kw):
    """Rings of equal length joined by quads. `skip(i, j)` drops a face; `want(i, j)` gives its direction."""
    K = len(rings[0])
    V = [p for r in rings for p in r]
    F = []
    for i in range(len(rings) - 1):
        for j in range(K if closed else K - 1):
            if skip and skip(i, j):
                continue
            j2 = (j + 1) % K
            idx = [i * K + j, i * K + j2, (i + 1) * K + j2, (i + 1) * K + j]
            F.append((idx, want(i, j)) if want else idx)
    F += _caps(rings, K, caps, cap_want)
    return b.add(name, zone, V, F, **kw)


def _caps(rings, K, caps, cap_want):
    F = []
    for ci, ring_i in ((0, 0), (1, len(rings) - 1)):
        if not caps[ci]:
            continue
        ring = [Vector(p) for p in rings[ring_i]]
        tris = mathutils.geometry.tessellate_polygon([ring])
        for t in tris:
            idx = [ring_i * K + t[0], ring_i * K + t[1], ring_i * K + t[2]]
            F.append((idx, cap_want[ci]) if cap_want[ci] is not None else idx)
    return F


def extrude_y(b, name, zone, poly_xz, y0, y1, caps=True, **kw):
    """A convex-ish XZ polygon extruded from y0 to y1."""
    rings = [[(x, y0, z) for x, z in poly_xz], [(x, y1, z) for x, z in poly_xz]]
    cw = ((0, -1 if y0 < y1 else 1, 0), (0, 1 if y0 < y1 else -1, 0))
    return loft(b, name, zone, rings, closed=True, caps=(caps, caps), cap_want=cw, **kw)


def pane(b, name, zone, pts, normal, lift=0.014, inset=0.014, skirt=None, **kw):
    """A flat panel proud of a surface: outer ring on it, skirt up to an inset ring, tessellated face. `skirt` names a separate
    part for the skirt, so a far LOD can drop it and keep the face."""
    O = [Vector(p) for p in pts]
    n = Vector(normal).normalized()
    c = sum(O, Vector((0, 0, 0))) / len(O)
    I = [p + (c - p).normalized() * inset + n * lift for p in O]
    V = O + I
    N = len(O)
    Fs, Ft = [], []
    for k in range(N):
        k2 = (k + 1) % N
        Fs.append(([k, k2, N + k2, N + k], ((O[k] + O[k2]) * 0.5 - c).normalized() + n))
    for t in mathutils.geometry.tessellate_polygon([I]):
        Ft.append(([N + t[0], N + t[1], N + t[2]], n))
    if skirt:
        b.add(skirt, zone, V, Fs, **kw)
        return b.add(name, zone, V, Ft, **kw)
    return b.add(name, zone, V, Fs + Ft, **kw)


def lamp(b, name, center, axis, r, kind="work", n=8):
    """Round lamp through one template (all stack): a bezel skirt and a shallow lens cone. The frame is a pure rotation."""
    u, v, a = _frame(axis)
    c = Vector(center)
    V = [c + u * (math.cos(2 * math.pi * j / n) * r * 1.16) + v * (math.sin(2 * math.pi * j / n) * r * 1.16) for j in range(n)]
    V += [c + a * (r * 0.30) + u * (math.cos(2 * math.pi * j / n) * r) + v * (math.sin(2 * math.pi * j / n) * r) for j in range(n)]
    V.append(c + a * (r * 0.40))
    F = []
    for j in range(n):
        j2 = (j + 1) % n
        ang = 2.0 * math.pi * (j + 0.5) / n
        F.append(([j, j2, n + j2, n + j], (u * math.cos(ang) + v * math.sin(ang)) + a))
        F.append(([n + j, n + j2, 2 * n], a))
    b.add(name, "lens", V, F, stack="lamp", tier="lamp", meta={"c": tuple(c), "a": tuple(a), "r": r})
    b.set_planar(name, a, u)
    b.lights.setdefault(kind, []).append(tuple(round(float(x), 3) for x in c))


# --------------------------------------------------------------------------------------
# Tracks
# --------------------------------------------------------------------------------------

class TrackPath:
    """Outer belt surface in the XZ plane: top run forward, front arc, ramp, ground run back, ramp, rear arc."""

    def __init__(self):
        cf, cr = Vector(FRONT_C), Vector(REAR_C)
        gf, gr = Vector((GROUND_F, ZG)), Vector((GROUND_R, ZG))
        df = gf - cf
        a_f = math.atan2(df.y, df.x) + math.acos(FRONT_R / df.length)
        dr = gr - cr
        a_r = math.atan2(dr.y, dr.x) - math.acos(REAR_R / dr.length)
        self.segs = [
            ("line", Vector((cr.x, cr.y + REAR_R)), Vector((cf.x, cf.y + FRONT_R))),
            ("arc", cf, FRONT_R, math.pi / 2.0, a_f),
            ("line", cf + Vector((math.cos(a_f), math.sin(a_f))) * FRONT_R, gf),
            ("line", gf, gr),
            ("line", gr, cr + Vector((math.cos(a_r), math.sin(a_r))) * REAR_R),
            ("arc", cr, REAR_R, a_r, -1.5 * math.pi),
        ]
        self.lens = []
        for s in self.segs:
            self.lens.append((s[2] - s[1]).length if s[0] == "line" else abs(s[4] - s[3]) * s[2])
        self.L = sum(self.lens)
        self.n_cleats = int(round(self.L / 0.297))
        self.p = self.L / self.n_cleats

    def at(self, s):
        """(x, z), tangent (tx, tz) at arc length s."""
        s = s % self.L
        for seg, ln in zip(self.segs, self.lens):
            if s <= ln + 1e-9:
                if seg[0] == "line":
                    d = (seg[2] - seg[1]).normalized()
                    return seg[1] + d * s, d
                a = seg[3] - s / seg[2]
                return seg[1] + Vector((math.cos(a), math.sin(a))) * seg[2], Vector((math.sin(a), -math.cos(a)))
            s -= ln
        raise AssertionError

    def kind(self, s):
        s = s % self.L
        for i, ln in enumerate(self.lens):
            if s <= ln + 1e-9:
                return i
            s -= ln
        return 0

    def boundaries(self):
        """Belt vertex positions in cleat-pitch units: the straight runs and ramps take the longest step that stays on the
        run, at most 3 pitches (one tile); the arcs take 1/2 so the round ends stay round."""
        out = [0.0]
        k = 0.0
        p = self.p
        while k < self.n_cleats - 1e-6:
            kd = self.kind(k * p + 1e-6)
            st = 0.5
            if kd in (0, 2, 3, 4) and abs(k - round(k)) < 1e-6:
                for cand in (3.0, 2.0, 1.0):
                    if self.kind((k + cand) * p - 1e-6) == kd:
                        st = cand
                        break
            k = min(k + st, float(self.n_cleats))
            out.append(k)
        return out


def _p3(path, k, s, y=None, n=0.0):
    """Point at pitch position k on side s, lifted n along the outer normal."""
    q, t = path.at(k * path.p)
    nv = Vector((-t.y, t.x))           # T x Y in the XZ plane
    # outer normal in XZ: T = (tx, tz), N = (-tz, tx)
    return (q.x + nv.x * n, s * (YI if y is None else y), q.y + nv.y * n)


def _tracks(b):
    path = TrackPath()
    b.path = path
    bnd = path.boundaries()
    tile_t = 3.0
    for s, tag in ((1, "L"), (-1, "R")):
        rows_o, rows_i = [], []
        for k in bnd:
            rows_o.append((_p3(path, k, s, YI), _p3(path, k, s, YI + TW)))
            rows_i.append((_p3(path, k, s, YI, -TB), _p3(path, k, s, YI + TW, -TB)))
        # outer surface: T' = s T, Y' = s Y, normal +N
        for surf, rows, sigma in (("BeltOuter", rows_o, s), ("BeltInner", rows_i, -s)):
            V, F, tiles = [], [], []
            for r in rows:
                V += [r[0], r[1]]
            n_rows = len(rows)
            for i in range(n_rows - 1):
                k0, k1 = bnd[i], bnd[i + 1]
                dk = k1 - k0
                if sigma > 0:
                    idx = [2 * i, 2 * (i + 1), 2 * (i + 1) + 1, 2 * i + 1]
                    t0 = k0 - math.floor(k0)
                else:
                    idx = [2 * (i + 1), 2 * i, 2 * i + 1, 2 * (i + 1) + 1]
                    t0 = (-k1) - math.floor(-k1)
                F.append(idx)
                tiles.append((t0, t0 + dk))
            ids = b.add(f"{surf}{tag}", "rubber", V, F, raw=True, tier="stack")
            for fid, (t0, t1) in zip(ids, tiles):
                if abs(t1 - t0 - tile_t) < 1e-6 and b.tiles.get(surf, {}).get("canon") is None:
                    b.tile(surf, fid)
                    b.meta[surf + "Canon"] = {"face": fid, "x0": b.V[b.F[fid][0]][0], "pitch": path.p}
                else:
                    b.tile(surf, fid, [(t0 / tile_t, 0.0), (t1 / tile_t, 0.0), (t1 / tile_t, 1.0), (t0 / tile_t, 1.0)])
        # outer edge of the belt: from the outer to the inner surface, along T' = s T
        V, F, tiles = [], [], []
        for ro, ri in zip(rows_o, rows_i):
            V += [ro[1], ri[1]]
        for i in range(len(bnd) - 1):
            k0, k1 = bnd[i], bnd[i + 1]
            if s > 0:
                F.append([2 * i, 2 * (i + 1), 2 * (i + 1) + 1, 2 * i + 1])
                t0 = k0 - math.floor(k0)
            else:
                F.append([2 * (i + 1), 2 * i, 2 * i + 1, 2 * (i + 1) + 1])
                t0 = (-k1) - math.floor(-k1)
            tiles.append((t0, t0 + k1 - k0))
        ids = b.add(f"BeltEdge{tag}", "rubber", V, F, raw=True, tier="stack")
        for fid, (t0, t1) in zip(ids, tiles):
            if abs(t1 - t0 - tile_t) < 1e-6 and b.tiles.get("edge", {}).get("canon") is None:
                b.tile("edge", fid)
                b.meta["BeltEdgeCanon"] = {"face": fid, "x0": b.V[b.F[fid][0]][0], "pitch": path.p}
            else:
                b.tile("edge", fid, [(t0 / tile_t, 0.0), (t1 / tile_t, 0.0), (t1 / tile_t, 1.0), (t0 / tile_t, 1.0)])
        # cleats: one template placed through the frame (T', Y', N): pure rotations, so all stack on one island
        tmpl = [(-CLEAT_BASE, 0.0), (-CLEAT_TOP, CH), (CLEAT_TOP, CH), (CLEAT_BASE, 0.0)]
        for c in range(path.n_cleats):
            q, t = path.at((c + 0.5) * path.p)
            T = Vector((t.x, 0.0, t.y)) * s
            N = Vector((-t.y, 0.0, t.x))
            Y = Vector((0.0, float(s), 0.0))
            o = Vector((q.x, s * YI, q.y))
            V = []
            for w in (0.0, TW):
                for tt, nn in tmpl:
                    V.append(o + T * tt + Y * w + N * nn)
            F = [([0, 1, 5, 4], -T + N), ([1, 2, 6, 5], N), ([2, 3, 7, 6], T + N), ([4, 5, 6, 7], Y)]
            letter = "A" if c % 2 == 0 else "B"
            b.add(f"Cleat{letter}{c:02d}{tag}", "cleat", V, F, stack="cleat", tier="hero",
                  meta={"o": tuple(o), "T": tuple(T), "N": tuple(N), "s": s})
        # road wheels and idlers: one template each. Frame u x v = a, a = outward
        for i, wx in enumerate(WHEEL_X):
            _wheel(b, f"{i}{tag}", s, wx, ZG + TB + WHEEL_R, WHEEL_R)
        _wheel(b, f"F{tag}", s, FRONT_C[0], FRONT_C[1], FRONT_R - TB)
        _wheel(b, f"R{tag}", s, REAR_C[0], REAR_C[1], REAR_R - TB)
        # a dark plate inside each track: what the eye reads between the wheels instead of daylight, and what LOD2 keeps
        # (symmetric about the track center and inside the belt on both ends, so the right plate is the left one rotated
        # 180 degrees about Z and both stack on one island; seen from one side only)
        poly = [(-2.16, 0.80), (2.16, 0.80), (2.38, 0.62), (2.36, 0.42), (1.40, 0.155), (-1.40, 0.155), (-2.36, 0.42), (-2.38, 0.62)]
        V = [Vector((TRACK_CENTER_X + s * x, s * WHEEL_Y, z)) for x, z in poly]
        tris = mathutils.geometry.tessellate_polygon([V])
        b.add(f"TrackBack{tag}", "black", V, [([t[0], t[1], t[2]], (0, s, 0)) for t in tris], stack="trackback", tier="hidden")


def _wheel(b, tag, s, x, z, r, n=12):
    u = Vector((float(s), 0.0, 0.0))
    v = Vector((0.0, 0.0, -1.0))
    a = u.cross(v)
    c = Vector((x, s * WHEEL_Y, z))
    V = []
    for off, rr in ((0.0, r), (WHEEL_W, r), (WHEEL_W - 0.025, r * 0.66)):
        for j in range(n):
            ang = 2.0 * math.pi * j / n
            V.append(c + a * off + u * (math.cos(ang) * rr) + v * (math.sin(ang) * rr))
    V.append(c + a * (WHEEL_W + 0.03))
    band, face = [], []
    for j in range(n):
        j2 = (j + 1) % n
        ang = 2.0 * math.pi * (j + 0.5) / n
        rad = u * math.cos(ang) + v * math.sin(ang)
        band.append(([j, j2, n + j2, n + j], rad))
        face.append(([n + j, n + j2, 2 * n + j2, 2 * n + j], a + rad * 0.3))
        face.append(([2 * n + j, 2 * n + j2, 3 * n], a))
    meta = {"c": tuple(c), "u": tuple(u), "v": tuple(v), "a": tuple(a), "r": r}
    b.add(f"WheelBand{tag}", "rubber", V, band, stack="wheelband", tier="stack", meta=meta)
    b.add(f"WheelFace{tag}", "rubber", V, face, stack="wheelface", tier="hero", meta=meta)
    b.set_planar(f"WheelFace{tag}", a, u)


def _chassis(b):
    # the belly between the tracks: walls and ends only (a rotated pair each, so they stack); top and bottom never show
    xa, xb, ya, za, zb = XR - 0.02, XF + 0.12, 0.40, 0.42, 1.00
    xm = 0.5 * (xa + xb)
    for s, tag in ((1, "L"), (-1, "R")):
        wall = [(xm - s * (xb - xa) * 0.5, s * ya, za), (xm + s * (xb - xa) * 0.5, s * ya, za),
                (xm + s * (xb - xa) * 0.5, s * ya, zb), (xm - s * (xb - xa) * 0.5, s * ya, zb)]
        b.add(f"ChassisWall{tag}", "steel", wall, [([0, 1, 2, 3], (0, s, 0))], stack="chwall", tier="hidden")
        x_end = xb if s > 0 else xa
        end = [(x_end, -s * ya, za), (x_end, s * ya, za), (x_end, s * ya, zb), (x_end, -s * ya, zb)]
        b.add(f"ChassisEnd{tag}", "steel", end, [([0, 1, 2, 3], (s, 0, 0))], stack="chend", tier="hidden")
    for s, tag in ((1, "L"), (-1, "R")):
        for i, x in enumerate((1.10, -0.90)):
            bar(b, f"CrossTube{i}{tag}", "steel", (x, s * 0.38, 0.47), (x, s * (WHEEL_Y + 0.01), 0.47), 0.10, 0.10, stack="barSteel", tier="hidden")


# --------------------------------------------------------------------------------------
# Body
# --------------------------------------------------------------------------------------

GH_Z0, GH_Z1 = 1.20, 2.66


def gh_ring(z):
    """Plan of the cab at height z: rake and tumblehome are straight lines (8 corners, left then right)."""
    t = (z - GH_Z0) / (GH_Z1 - GH_Z0)
    xf = 2.784 + (2.163 - 2.784) * t
    wm = 1.19 + (1.03 - 1.19) * t
    xr = 0.62 + (0.72 - 0.62) * t
    ax = 0.66 + (0.56 - 0.66) * t
    return [(xf, ax), (xf - 0.30, wm), (xr + 0.16, wm), (xr, wm - 0.22), (xr, -(wm - 0.22)), (xr + 0.16, -wm), (xf - 0.30, -wm), (xf, -ax)]


def cab_wm(z):
    t = (z - GH_Z0) / (GH_Z1 - GH_Z0)
    return 1.19 + (1.03 - 1.19) * t


def _hull(b):
    # the two front rows follow the windshield rake down to the black frame (pb600-villard-1.jpg, pb600-a.jpg)
    st = [(2.86, 0.78, 0.64, 1.01, 1.09, 1.13), (2.79, 1.08, 0.94, 0.99, 1.17, 1.27), (2.72, 1.20, 1.06, Z_SILL, 1.24, 1.40),
          (2.20, 1.25, 1.12, Z_SILL, 1.30, 1.54), (1.50, 1.25, 1.10, Z_SILL, 1.45, 1.66), (0.90, 1.25, 1.02, Z_SILL, 1.85, 2.06),
          (0.56, 1.25, 1.02, Z_SILL, 1.85, 2.06)]
    for x_new in (RIM_END_X, 1.18):
        i = next(k for k in range(len(st) - 1) if st[k][0] > x_new >= st[k + 1][0])
        t = (st[i][0] - x_new) / (st[i][0] - st[i + 1][0])
        st.insert(i + 1, tuple(a + (c - a) * t for a, c in zip(st[i], st[i + 1])))
    # the rim stays level with the windshield base up to the cab's corner facet, so it shows no tab beside the glass; behind that it is inside the cab
    k = next(k for k, row in enumerate(st) if abs(row[0] - RIM_END_X) < 1e-6)
    st[k] = st[k][:5] + (GLASS_BASE_Z,)
    rings = [[(x, w, zb), (x, w, zm), (x, wt, zt), (x, -wt, zt), (x, -w, zm), (x, -w, zb)] for x, w, wt, zb, zm, zt in st]

    def skip(i, j):
        return j == 2 and i >= 2

    def want(i, j):
        x0, w0, wt0, zb0, zm0, zt0 = st[i]
        forward = 0.35 if i < 2 else 0.0
        return {0: (forward, 1, 0), 1: (forward, 1, 1), 2: (forward, 0, 1), 3: (forward, -1, 1), 4: (forward, -1, 0)}[j]

    loft(b, "LowerBody", "red", rings, closed=False, skip=skip, want=want, caps=(True, True), cap_want=((1, 0, 0), (-1, 0, 0)))
    # front apron under the nose: black, ahead of and between the tracks
    box(b, "Apron", "black", (XF + 0.01, -0.44, 0.62), (2.86, 0.44, 1.02), skip=("-x", "+z"), tier="mech")
    box(b, "ApronLip", "black", (2.72, -1.00, 0.93), (2.93, 1.00, 1.04), skip=("-x",), tier="mech")
    for s, tag in ((1, "L"), (-1, "R")):
        # no lamp on the nose (the working lamps are on the roof): this is only a spot for a game light effect, on the panel
        b.lights.setdefault("headlight", []).append((2.845, round(s * 0.86, 3), 1.06))
        # door furniture
        bar(b, f"DoorHandle{tag}", "black", (1.42, s * 1.268, 1.36), (1.22, s * 1.268, 1.36), 0.03, 0.03, up=(0, 1, 0), caps=(True, True), stack="handle", tier="small")
        bar(b, f"GrabRail{tag}", "steel", (2.12, s * 1.27, 1.26), (1.83, s * 1.10, 2.34), 0.035, 0.035, up=(1, 0, 0), stack="barSteel", tier="small")
        box(b, f"Step{tag}", "steel", (1.60, s * 1.24 if s > 0 else -1.40, 0.985), (2.02, 1.40 if s > 0 else -1.24, 1.03), skip=("-z",), stack="step", tier="small")


def _cab(b):
    r0, r1 = gh_ring(GH_Z0), gh_ring(GH_Z1)
    rings = [[(x, y, GH_Z0) for x, y in r0], [(x, y, GH_Z1) for x, y in r1]]
    loft(b, "Greenhouse", "black", rings, closed=True, tier="hidden")

    # wrapped windshield: three facets, one connected panel
    zb, zt = 1.40, 2.60
    rb, rt = gh_ring(zb), gh_ring(zt)

    def lerp2(a, c, t):
        return (a[0] + (c[0] - a[0]) * t, a[1] + (c[1] - a[1]) * t)

    def row(ring, z, lift, t=0.86):
        al, bl, ar, br = ring[0], ring[1], ring[7], ring[6]
        pts = [lerp2(ar, br, t), ar, al, lerp2(al, bl, t)]
        out = []
        for i, (x, y) in enumerate(pts):
            nx, ny = {0: (0.60, -0.80), 1: (0.92, -0.20), 2: (0.92, 0.20), 3: (0.60, 0.80)}[i]
            out.append((x + nx * lift, y + ny * lift, z))
        return out
    oring = row(rb, zb, 0.0, 0.92) + row(rt, zt, 0.0, 0.92)[::-1]
    iring = [(*p[:2], p[2] + 0.014) for p in row(rb, zb, 0.016, 0.89)] + [(*p[:2], p[2] - 0.014) for p in row(rt, zt, 0.016, 0.89)][::-1]
    V = oring + iring
    F = []
    M = len(oring)
    for k in range(M):
        k2 = (k + 1) % M
        F.append(([k, k2, M + k2, M + k], (0.8, 0.0, 0.25)))
    for j in range(3):
        F.append(([M + j, M + j + 1, M + 6 - j, M + 7 - j], (0.9, 0.0, 0.42)))
    b.add("GlassFront", "glass", V, F)

    # side glass on the side facets
    for s, tag in ((1, "L"), (-1, "R")):
        def on_side(px, pz):
            return (px, s * cab_wm(pz), pz)
        door = [(2.31, 1.50), (1.50, 1.67), (1.24, 1.87), (1.24, 2.56), (1.83, 2.56)]
        pane(b, f"GlassDoor{tag}", "glass", [on_side(*p) for p in door], (0, s, 0.11))
        # the red rear pillar closes the side facet and its corner
        rp_side = [(1.20, 1.66), (0.84, 1.98), (0.885, 2.655), (1.20, 2.655)]
        pane(b, f"CabPillarS{tag}", "red", [on_side(*p) for p in rp_side], (0, s, 0.11), inset=0.006, lift=0.010, stack="pillarS")
        rb2, rt2 = gh_ring(1.98), gh_ring(2.655)
        i0, i1 = (2, 3) if s > 0 else (5, 4)
        corner = [(rb2[i0][0], rb2[i0][1], 1.98), (rb2[i1][0], rb2[i1][1], 1.98), (rt2[i1][0], rt2[i1][1], 2.655), (rt2[i0][0], rt2[i0][1], 2.655)]
        pane(b, f"CabPillarC{tag}", "red", corner, (-0.8, s * 0.6, 0.05), inset=0.006, lift=0.010, stack="pillarC")
        # mirror on a long arm, two stays
        bar(b, f"MirrorArmA{tag}", "black", (1.84, s * 1.02, 2.62), (2.24, s * 1.50, 2.56), 0.028, 0.028, stack="barBlack", tier="small")
        bar(b, f"MirrorArmB{tag}", "black", (2.22, s * 1.12, 1.72), (2.25, s * 1.52, 2.20), 0.028, 0.028, stack="barBlack", tier="small")
        box(b, f"MirrorHead{tag}", "black", (2.20, s * 1.47 if s > 0 else -1.65, 2.16), (2.29, 1.65 if s > 0 else -1.47, 2.58), stack="mirrorhead", tier="small")
        # wiper on the center facet
        x0 = rb[0][0] + 0.03
        bar(b, f"Wiper{tag}", "black", (x0, s * 0.42, 1.44), (x0 - 0.33, s * 0.16, 2.22), 0.03, 0.022, up=(0.92, 0, 0.39), stack="barBlack", tier="small")
    rz0, rz1 = 2.10, 2.57
    xa, xc = gh_ring(rz0)[3][0], gh_ring(rz1)[3][0]
    pane(b, "GlassRear", "glass", [(xa, -0.72, rz0), (xa, 0.72, rz0), (xc, 0.72, rz1), (xc, -0.72, rz1)], (-1, 0, 0.1))


def _roof(b):
    """Red roof cap over the cab, overhanging, with light bars and beacons."""
    def ring(grow_f, grow_s, grow_r, z):
        r = gh_ring(GH_Z1)
        o = []
        for i, (x, y) in enumerate(r):
            sy = 1 if y > 0 else -1
            if i in (0, 7):
                o.append((x + grow_f, y, z))
            elif i in (1, 6):
                o.append((x + grow_f * 0.6, y + sy * grow_s, z))
            elif i in (2, 5):
                o.append((x, y + sy * grow_s, z))
            else:
                o.append((x - grow_r, y + sy * grow_s * 0.5, z))
        return o
    rings = [ring(-0.02, -0.02, -0.02, 2.63), ring(0.09, 0.06, 0.07, 2.70), ring(0.08, 0.05, 0.06, 2.76), ring(0.0, -0.05, -0.04, 2.80)]
    loft(b, "Roof", "red", rings, closed=True, caps=(False, True), cap_want=(None, (0, 0, 1)))
    # front light bar: on the roof, proud over the windshield
    prof = [(2.33, 2.795), (2.31, 2.93), (2.08, 2.93), (2.08, 2.79)]
    extrude_y(b, "LightBar", "black", prof, -1.04, 1.04, tier="mech")
    for s, tag in ((1, "L"), (-1, "R")):
        for i, y in enumerate(BAR_LAMP_Y):
            lamp(b, f"LampRoofF{i}{tag}", (2.322, s * y, BAR_LAMP_Z), (1.0, 0.0, -0.05), 0.058, kind="roof_front")
        # amber beacons on the bar
        cz = 2.93
        cc = Vector((BEACON_X, s * BEACON_Y, cz))
        rings2 = []
        for rr, h in ((0.072, -0.005), (0.066, 0.055), (0.036, 0.095)):
            rings2.append([cc + Vector((math.cos(2 * math.pi * j / 8) * rr, math.sin(2 * math.pi * j / 8) * rr, h)) for j in range(8)])
        loft(b, f"Beacon{tag}", "amber", rings2, closed=True, caps=(False, True), cap_want=(None, (0, 0, 1)),
             want=lambda i, j: (math.cos(2 * math.pi * (j + 0.5) / 8), math.sin(2 * math.pi * (j + 0.5) / 8), 0.3 + 0.5 * i),
             stack="beacon", tier="lamp", meta={"c": tuple(cc)})
        b.lights.setdefault("beacon", []).append((round(cc.x, 3), round(cc.y, 3), round(cz + 0.095, 3)))
        for i, y in enumerate(REAR_LAMP_Y):
            lamp(b, f"LampRoofR{i}{tag}", (0.678, s * y, BAR_LAMP_Z), (-1.0, 0.0, -0.05), 0.058, kind="roof_rear")
    prof_r = [(0.86, 2.76), (0.86, 2.93), (0.68, 2.93), (0.68, 2.74)]
    extrude_y(b, "LightBarRear", "black", prof_r, -0.92, 0.92, tier="mech")
    tube(b, "Antenna", "black", (0.80, -0.30, 2.79), (0.80, -0.30, 3.015), 0.016, n=3, caps=(False, False), tier="small")


HOOD_STATIONS = [(0.60, 2.00), (0.15, 1.86), (-0.40, 1.62), (-0.95, 1.36)]   # (x, top z) of the engine hood behind the cab


def _hood(b):
    st = HOOD_STATIONS
    rings = []
    for x, zt in st:
        w = 1.235 - 0.04 * (0.60 - x) / 1.55
        zs = max(zt - 0.30, 1.04)
        rings.append([(x, w, Z_SILL), (x, w, zs), (x, w - 0.14, zt - 0.04), (x, w - 0.38, zt), (x, -(w - 0.38), zt), (x, -(w - 0.14), zt - 0.04),
                      (x, -w, zs), (x, -w, Z_SILL)])
    want = {0: (0, 1, 0), 1: (0, 1, 1), 2: (0, 0.3, 1), 3: (0.1, 0, 1), 4: (0, -0.3, 1), 5: (0, -1, 1), 6: (0, -1, 0)}
    loft(b, "Hood", "red", rings, closed=False, want=lambda i, j: want[j])
    # (its rear end shows only as a 6 cm strip above the deck: LOD2 drops it)
    end = [Vector(q) for q in rings[-1]]
    b.add("HoodEnd", "red", end, [(list(t), (-1, 0, 0)) for t in mathutils.geometry.tessellate_polygon([end])])
    for s, tag in ((1, "L"), (-1, "R")):
        wv = 1.235 - 0.04 * (0.60 - 0.1) / 1.55
        vent = [(0.42, 1.14), (0.42, 1.58), (-0.22, 1.33), (-0.22, 1.14)]
        pane(b, f"HoodVent{tag}", "black", [(x, s * wv, z) for x, z in vent], (0, s, 0), lift=0.010, inset=0.012)
    # perforated panel on the sloping hood top
    def top_z(x):
        return float(np.interp(-x, [-st[i][0] for i in range(4)], [st[i][1] for i in range(4)]))
    gx0, gx1 = -0.85, -0.18
    pane(b, "HoodGrille", "black", [(gx0, -0.60, top_z(gx0)), (gx1, -0.60, top_z(gx1)), (gx1, 0.60, top_z(gx1)), (gx0, 0.60, top_z(gx0))], (0.40, 0, 1.0), lift=0.012, inset=0.012, skirt="HoodRim")
    # one exhaust stack with a perforated guard, right of the cab (pb600-a.jpg): two matched towers read as a truck
    ex, ey = 0.36, -0.78
    tube(b, "ExhaustPipe", "steel", (ex, ey, 1.86), (ex, ey, 2.62), 0.05, n=6, caps=(False, False), tier="small")
    tube(b, "ExhaustGuard", "alu", (ex, ey, 2.00), (ex, ey, 2.46), 0.085, n=8, caps=(True, True), tier="small")
    hose(b, "ExhaustTip", "steel", [(ex, ey, 2.60), (ex, ey, 2.70), (ex - 0.07, ey, 2.77), (ex - 0.18, ey, 2.80)], 0.05, n=6, smooth=1, tier="small")


def _deck(b):
    box(b, "Deck", "gray", (XR + 0.08, -1.25, 1.18), (-0.90, 1.25, 1.30), skip=("-z",), tier="small")
    box(b, "RearBody", "black", (XR + 0.08, -1.21, 0.97), (-0.88, 1.21, 1.18), skip=("+z", "+x", "-z"), tier="mech")
    for s, tag in ((1, "L"), (-1, "R")):
        # dome mudflap over the rear of the track
        c = Vector((REAR_C[0], REAR_C[1]))
        Rf = 0.51
        prof = [(XR + 0.95, REAR_C[1] + Rf + 0.01), (c.x + 0.0, c.y + Rf)]
        for ang in (22, 45, 66, 86):
            prof.append((c.x - Rf * math.sin(math.radians(ang)), c.y + Rf * math.cos(math.radians(ang))))
        rings = []
        for y, drop in ((1.20, 0.0), (2.03, 0.0), (2.13, 0.05)):
            rings.append([(x, s * y, z - drop) for x, z in prof])
        def want(i, j, cx=c.x, cy=c.y, prof=prof):
            mx = 0.5 * (prof[j][0] + prof[j + 1][0])
            mz = 0.5 * (prof[j][1] + prof[j + 1][1])
            return (mx - cx if j > 0 else 0.0, 0.3 * s * i, max(mz - cy, 0.02))
        # (the outer lip is its own part: LOD2 keeps the dome and drops the lip)
        loft(b, f"Mudflap{tag}", "black", rings[:2], closed=False, want=want, stack="mudflap", tier="mech")
        loft(b, f"MudflapLip{tag}", "black", rings[1:], closed=False, want=lambda i, j, w=want: w(1, j), stack="mudfliplip", tier="mech")
        # rails: side rail and posts on the deck
        zr = 1.74
        xs = (XR + 0.16, -1.55, -1.02)
        for i, x in enumerate(xs):
            bar(b, f"RailPost{i}{tag}", "gray", (x, s * 1.20, 1.29), (x, s * 1.20, zr), 0.035, 0.035, up=(1, 0, 0), stack="barGray", tier="small")
        bar(b, f"RailSide{tag}", "gray", (xs[0], s * 1.20, zr), (xs[2], s * 1.20, zr), 0.035, 0.035, stack="barGray", tier="small")
        # tail lamp on the rear body
        pane(b, f"TailLamp{tag}", "tail", [(XR + 0.08, s * 0.92, 1.03), (XR + 0.08, s * 1.14, 1.03), (XR + 0.08, s * 1.14, 1.13), (XR + 0.08, s * 0.92, 1.13)],
             (-1, 0, 0), lift=0.012, inset=0.006, stack="taillamp", tier="small")
        b.lights.setdefault("tail", []).append((round(XR + 0.07, 3), round(s * 1.03, 3), 1.08))
    bar(b, "RailRear", "gray", (XR + 0.16, -1.20, 1.74), (XR + 0.16, 1.20, 1.74), 0.035, 0.035, stack="barGray", tier="small")


# --------------------------------------------------------------------------------------
# Blade
# --------------------------------------------------------------------------------------

BLADE_REF_X = 3.78
BLADE_HALF_C = 1.30
WING_L = 1.22
WING_SWEEP = math.radians(26.0)
MOLD = [(0.16, 0.00), (0.11, 0.14), (-0.01, 0.38), (0.00, 0.62), (0.09, 0.80)]   # front: (forward, up), bottom to top
MOLD_BACK = [(-0.10, 0.80), (-0.12, 0.08), (0.02, 0.00)]                        # top to bottom
BAND_TOP = 1.17
TOP_F = 0.15
TOOTH_PITCH = 0.105


def _panel(b, name, rear, p0, p1, fwd, end_cap=False):
    """One moldboard panel between plan points p0 and p1 (x, y), front normal `fwd`: the working face and top as `name`,
    the plain back as `rear` (hidden tier: it never carries detail)."""
    f = Vector((fwd[0], fwd[1], 0.0))
    sec_f = MOLD + MOLD_BACK[:1]
    rings = [[(p[0] + f.x * a, p[1] + f.y * a, BLADE_Z0 + z) for a, z in sec_f] for p in (p0, p1)]

    def want(i, j):
        return (0, 0, 1) if j == len(MOLD) - 1 else tuple(f + Vector((0, 0, 0.25)))
    loft(b, name, "black", rings, closed=False, want=want, meta={"mold_f": tuple(f)})
    sec_b = MOLD_BACK + [MOLD[0]]
    rings = [[(p[0] + f.x * a, p[1] + f.y * a, BLADE_Z0 + z) for a, z in sec_b] for p in (p0, p1)]

    def want_b(i, j):
        return tuple(-f) if j == 0 else (tuple(-f + Vector((0, 0, -1))) if j == 1 else (0, 0, -1))
    loft(b, rear, "black", rings, closed=False, want=want_b, stack=("bladerear" if end_cap else None), tier="hidden")
    if end_cap:
        sec = MOLD + MOLD_BACK
        ring = [Vector((p1[0] + f.x * a, p1[1] + f.y * a, BLADE_Z0 + z)) for a, z in sec]
        out = Vector((p1[0] - p0[0], p1[1] - p0[1], 0.0))
        tris = mathutils.geometry.tessellate_polygon([ring])
        b.add(f"{name}Tip", "black", ring, [([t[0], t[1], t[2]], tuple(out)) for t in tris], stack="bladetip", tier="mech")


def _teeth(b, name, p0, p1, fwd, stack=None):
    """The saw edge under the moldboard: one flat strip, the only geometry below the lip. The tips stop above the ground."""
    f = Vector((fwd[0], fwd[1], 0.0))
    a, c = Vector((p0[0], p0[1], 0.0)) + f * (MOLD[0][0] + 0.003), Vector((p1[0], p1[1], 0.0)) + f * (MOLD[0][0] + 0.003)
    n_t = max(4, int(round((c - a).length / TOOTH_PITCH)))
    z_top, z_valley, z_tip = BLADE_Z0 + 0.05, BLADE_Z0 - 0.005, BLADE_Z0 - 0.05
    pts = [a + Vector((0, 0, z_top)), c + Vector((0, 0, z_top))]
    for i in range(n_t, 0, -1):
        pts.append(a.lerp(c, (i - 0.5) / n_t) + Vector((0, 0, z_tip)))
        pts.append(a.lerp(c, (i - 1.0) / n_t) + Vector((0, 0, z_valley)))
    tris = mathutils.geometry.tessellate_polygon([pts])
    b.add(name, "steel", pts, [([t[0], t[1], t[2]], tuple(f)) for t in tris], stack=stack, tier="small")
    b.set_planar(name, f, (0, 0, 1))


def _band(b, tag, p0, p1, fwd):
    """Perforated band on top of a panel: a plate (opaque, the holes are painted) with a rail and end uprights."""
    f = Vector((fwd[0], fwd[1], 0.0))
    z0, z1 = BLADE_Z0 + MOLD[-1][1], BLADE_Z0 + BAND_TOP
    a0, a1 = MOLD[-1][0], TOP_F
    th = 0.018
    V = []
    for p in (p0, p1):
        q = Vector((p[0], p[1], 0.0))
        V.append(q + f * a0 + Vector((0, 0, z0)))
        V.append(q + f * a1 + Vector((0, 0, z1)))
        V.append(q + f * (a1 - th) + Vector((0, 0, z1)))
        V.append(q + f * (a0 - th) + Vector((0, 0, z0)))
    # (the back of the plate stacks on the front: both show the same holes)
    b.add(f"BladeBand{tag}", "steel", V, [([0, 1, 5, 4], tuple(f))], stack=f"band{tag}", tier="shell", meta={"f": tuple(f)})
    b.add(f"BladeBandBack{tag}", "steel", V, [([3, 2, 6, 7], tuple(-f))], stack=f"band{tag}", tier="shell", meta={"f": tuple(f)})
    t0 = Vector((p0[0], p0[1], z1)) + f * (a1 - th * 0.5)
    t1 = Vector((p1[0], p1[1], z1)) + f * (a1 - th * 0.5)
    bar(b, f"BladeRail{tag}", "black", t0, t1, 0.05, 0.05, stack="barBlack", tier="small")
    u0 = Vector((p0[0], p0[1], z0)) + f * (a0 - th * 0.5)
    u1 = Vector((p1[0], p1[1], z0)) + f * (a0 - th * 0.5)
    for i, t in enumerate((0.0, 1.0) if tag != "C" else (0.0, 0.5, 1.0)):
        lo = u0.lerp(u1, t)
        hi = t0.lerp(t1, t)
        bar(b, f"BladeRib{tag}{i}", "black", lo, hi, 0.05, 0.04, up=tuple(f), stack="barBlack", tier="small")


def blade_tips():
    """Plan points of the wing tips (author frame), for tests and the footprint."""
    return [(BLADE_REF_X + WING_L * math.sin(WING_SWEEP), s * (BLADE_HALF_C + WING_L * math.cos(WING_SWEEP))) for s in (1, -1)]


def _blade(b):
    xr = BLADE_REF_X
    c0, c1 = (xr, -BLADE_HALF_C), (xr, BLADE_HALF_C)
    _panel(b, "BladeC", "BladeRearC", c0, c1, (1.0, 0.0))
    _teeth(b, "BladeTeethC", c0, c1, (1.0, 0.0))
    _band(b, "C", c0, c1, (1.0, 0.0))
    for s, tag in ((1, "L"), (-1, "R")):
        hinge = (xr, s * BLADE_HALF_C)
        tip = (xr + WING_L * math.sin(WING_SWEEP), s * (BLADE_HALF_C + WING_L * math.cos(WING_SWEEP)))
        fwd = (math.cos(WING_SWEEP), -s * math.sin(WING_SWEEP))
        _panel(b, f"BladeW{tag}", f"BladeRear{tag}", hinge, tip, fwd, end_cap=True)
        _teeth(b, f"BladeTeeth{tag}", hinge, tip, fwd, stack="bladeteeth")
        _band(b, tag, hinge, tip, fwd)
        tube(b, f"BladeHinge{tag}", "steel", (xr - 0.04, s * BLADE_HALF_C, BLADE_Z0 + 0.06), (xr - 0.04, s * BLADE_HALF_C, BLADE_Z0 + 0.86), 0.075, n=8, caps=(False, True), stack="hinge", tier="small")
        # back ribs
        for i, t in enumerate((0.35, 0.8)):
            q = Vector(hinge).lerp(Vector(tip), t)
            bar(b, f"BladeBackRib{tag}{i}", "steel", (q.x - fwd[0] * 0.10, q.y - fwd[1] * 0.10, BLADE_Z0 + 0.12), (q.x - fwd[0] * 0.10, q.y - fwd[1] * 0.10, BLADE_Z0 + 0.78), 0.05, 0.12, up=(fwd[0], fwd[1], 0), stack="barSteel", tier="hidden")
    for i, y in enumerate((-0.95, -0.55, 0.55, 0.95)):
        bar(b, f"BladeBackRibC{i}", "steel", (xr - 0.11, y, BLADE_Z0 + 0.12), (xr - 0.11, y, BLADE_Z0 + 0.78), 0.05, 0.12, up=(1, 0, 0), stack="barSteel", tier="hidden")
    # push frame
    box(b, "BladeCarrier", "black", (3.28, -0.62, BLADE_Z0 + 0.22), (xr - 0.10, 0.62, BLADE_Z0 + 0.68), skip=("+x", "-x", "-z"), tier="mech")
    box(b, "BladeCross", "black", (3.10, -0.62, 0.58), (3.30, 0.62, 0.78), skip=("+y", "-y"), tier="mech")
    for s, tag in ((1, "L"), (-1, "R")):
        bar(b, f"BladeArm{tag}", "black", (2.80, s * 0.34, 0.95), (3.34, s * 0.55, 0.55), 0.24, 0.28, stack="bladearm", tier="mech")
        # lift ram: cylinder on the arm side, chrome rod going down to the carrier
        tube(b, f"BladeLift{tag}", "steel", (2.80, s * 0.22, 1.10), (3.24, s * 0.26, 0.98), 0.075, n=6, caps=(True, True), stack="ram", tier="small")
        tube(b, f"BladeRod{tag}", "chrome", (3.22, s * 0.26, 1.00), (3.50, s * 0.30, BLADE_Z0 + 0.66), 0.026, n=6, caps=(False, False), stack="rod", tier="small")
        # wing ram across the back of the blade
        tip_q = Vector((xr, s * BLADE_HALF_C)).lerp(Vector((xr + WING_L * math.sin(WING_SWEEP), s * (BLADE_HALF_C + WING_L * math.cos(WING_SWEEP)))), 0.45)
        tube(b, f"WingRam{tag}", "steel", (3.44, s * 0.60, BLADE_Z0 + 0.50), (3.56, s * 1.10, BLADE_Z0 + 0.54), 0.04, n=6, caps=(True, True), stack="ram", tier="small")
        tube(b, f"WingRod{tag}", "chrome", (3.55, s * 1.08, BLADE_Z0 + 0.54), (tip_q.x - 0.10, tip_q.y, BLADE_Z0 + 0.56), 0.022, n=6, caps=(False, False), stack="rod", tier="small")
    # hydraulic hose bundle between the nose and the carrier
    for i, y in enumerate((-0.16, 0.0, 0.16)):
        hose(b, f"HoseFront{i}", "rubber", [(2.80, y, 1.12), (3.10, y * 1.2, 1.36 - abs(y) * 0.4), (3.28, y * 1.6, 1.26), (3.40, y * 2.0, 1.00), (3.44, y * 2.2, BLADE_Z0 + 0.66)], 0.024, n=5, stack="hose", tier="small")


# --------------------------------------------------------------------------------------
# Rear arm, tiller, comb
# --------------------------------------------------------------------------------------

def _rear(b):
    box(b, "Hitch", "steel", (XR - 0.42, -0.30, 0.50), (XR + 0.10, 0.30, 1.04), skip=("+x", "-z"), tier="mech")
    for s, tag in ((1, "L"), (-1, "R")):
        bar(b, f"ArmLower{tag}", "gray", (XR - 0.32, s * 0.21, 0.62), (-3.20, s * 0.58, 0.54), 0.16, 0.20, stack="armlower", tier="mech")
    bar(b, "ArmUpper", "gray", (XR + 0.10, 0.0, 0.96), (-3.20, 0.0, 0.80), 0.26, 0.18, caps=(False, True), tier="mech")
    # lift ram from the deck rear to the yoke
    tube(b, "LiftRam", "steel", (XR + 0.10, 0.0, 1.24), (-2.74, 0.0, 1.10), 0.085, n=6, caps=(True, True), stack="ram", tier="small")
    tube(b, "ArmRod", "chrome", (-2.70, 0.0, 1.12), (-3.10, 0.0, 0.92), 0.03, n=6, caps=(False, False), stack="rod", tier="small")
    # lifting yoke across the tiller
    tube(b, "TillerYoke", "alu", (-3.19, -1.15, 0.74), (-3.19, 1.15, 0.74), 0.048, n=6, caps=(True, True), tier="mech")
    # hose loops off the deck to the tiller
    for i, s in enumerate((-1, 1)):
        hose(b, f"HoseRear{i}", "rubber", [(XR + 0.14, s * 0.22, 1.29), (XR - 0.12, s * 0.26, 1.46), (-2.78, s * 0.36, 1.42), (-3.00, s * 0.50, 1.14), (-3.14, s * 0.62, 0.72)], 0.026, n=5, stack="hose", tier="small")
        hose(b, f"HoseRearB{i}", "rubber", [(XR + 0.14, s * 0.34, 1.29), (XR - 0.16, s * 0.40, 1.38), (-2.82, s * 0.54, 1.30), (-3.02, s * 0.72, 1.04), (-3.16, s * 0.92, 0.70)], 0.022, n=5, stack="hose", tier="small")


HOOD_PROFILE = [(-2.95, 0.26), (-2.98, 0.50), (-3.16, 0.70), (-3.42, 0.74), (-3.62, 0.62), (-3.72, 0.42), (-3.74, 0.22)]
COMB_FRONT_X, COMB_FRONT_Z = -3.70, 0.25


def _tiller(b):
    for s, tag, (y0, y1) in ((1, "L", (0.04, TILLER_Y - 0.02)), (-1, "R", (-(TILLER_Y - 0.02), -0.04))):
        # both sections run in +y and differ by a translation: they share one island
        rings = [[(x, y0, z) for x, z in HOOD_PROFILE], [(x, y1, z) for x, z in HOOD_PROFILE]]
        K = len(HOOD_PROFILE)
        def want(i, j, p=HOOD_PROFILE):
            mx, mz = 0.5 * (p[j][0] + p[j + 1][0]), 0.5 * (p[j][1] + p[j + 1][1])
            return (mx + 3.35, 0.0, mz - 0.30)
        loft(b, f"TillerHood{tag}", "red", rings, closed=False, want=want, stack="thood", tier="stack")
        # drum
        cx, cz, rd = -3.35, 0.29, 0.27
        n = 8
        rr = []
        for y in (y0, y1):
            rr.append([(cx + math.cos(2 * math.pi * j / n) * rd, y, cz + math.sin(2 * math.pi * j / n) * rd) for j in range(n)])
        # (only the lower half shows under the hood)
        loft(b, f"Drum{tag}", "steel", rr, closed=True, want=lambda i, j: (math.cos(2 * math.pi * (j + 0.5) / n), 0.0, math.sin(2 * math.pi * (j + 0.5) / n)),
             skip=lambda i, j: j < 4, stack="drum", tier="hidden")
        # comb: a ribbed rubber mat lying on the snow behind the drum, with a straight rear edge (pb-laterns-carrier.jpg,
        # pb600-laterns-6plus-02.jpg); the ribs along the direction of travel are painted (p_comb)
        L = HALF_L + COMB_FRONT_X
        pts = [(COMB_FRONT_X, y0, COMB_FRONT_Z), (COMB_FRONT_X, y1, COMB_FRONT_Z), (-HALF_L, y1, 0.0), (-HALF_L, y0, 0.0)]
        b.add(f"Comb{tag}", "black", pts, [([0, 1, 2, 3], (0.32, 0, 1))], stack="comb", tier="stack", meta={"y0": y0, "y1": y1})
        b.set_planar(f"Comb{tag}", (COMB_FRONT_Z, 0.0, L), (0, 1, 0))
        # end plate, ski-shaped
        plate = [(-2.82, 0.44), (-2.98, 0.08), (-3.46, 0.02), (-3.92, 0.04), (-3.96, 0.38), (-3.52, 0.82), (-3.10, 0.82)]
        ye = s * (TILLER_Y - 0.05)
        extrude_y(b, f"TillerEnd{tag}", "black", plate, ye if s > 0 else ye - 0.05, ye + 0.05 if s > 0 else ye, stack="tend", tier="stack")
    box(b, "TillerDrive", "black", (-3.74, -0.06, 0.16), (-3.00, 0.06, 0.78), skip=("-z",), tier="mech")


def build_all(b=None):
    b = b or Asset()
    _tracks(b)
    _chassis(b)
    _hull(b)
    _cab(b)
    _roof(b)
    _hood(b)
    _deck(b)
    _blade(b)
    _rear(b)
    _tiller(b)
    return b


# --------------------------------------------------------------------------------------
# Source atlas: flat zones. The bake carries them onto the packed layout with its margin.
# --------------------------------------------------------------------------------------

def _lin_to_srgb(c):
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(np.maximum(c, 1e-9), 1.0 / 2.4) - 0.055)


def _srgb_to_lin(c):
    c = np.asarray(c, np.float64)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


ZL = {k: _srgb_to_lin(np.array(v[0], np.float64) / 255.0).astype(np.float32) for k, v in ZONES.items()}
BELT_BAND = _srgb_to_lin(np.array((40, 40, 44), np.float64) / 255.0).astype(np.float32)   # the narrow belts, against near-black gaps


def zone_rects(A):
    """(u0, v0, u1, v1) per zone in a coarse grid below the reserved strip."""
    names = sorted(ZONES)
    cols = 4
    cell = 0.2
    out = {}
    for i, z in enumerate(names):
        c, r = i % cols, i // cols
        out[z] = (c * cell + 0.02, r * cell + 0.02, (c + 1) * cell - 0.02, (r + 1) * cell - 0.02)
    return out


def write_source_atlas(asset_dir, A=ATLAS):
    os.makedirs(asset_dir, exist_ok=True)
    base = np.zeros((A, A, 4), np.float32)
    base[..., 3] = 1.0
    nrm = np.zeros((A, A, 4), np.float32)
    nrm[...] = (0.5, 0.5, 1.0, 1.0)
    msk = np.zeros((A, A, 4), np.float32)
    ctl = np.zeros((A, A, 4), np.float32)
    for z, r in zone_rects(A).items():
        sl = (slice(int(r[1] * A), int(r[3] * A)), slice(int(r[0] * A), int(r[2] * A)))
        rgb, metal, gloss = ZONES[z]
        base[sl][..., :3] = np.array(rgb, np.float32) / 255.0
        msk[sl][..., 0] = metal
        msk[sl][..., 3] = gloss
    for arr, sfx, data in ((base, "BaseColor", False), (nrm, "Normal", True), (msk, "MaskMap", True), (ctl, "ControlMask", True)):
        cs2_asset._write_png_raw(os.path.join(asset_dir, f"{NAME}_{sfx}.png"), f"{NAME}_{sfx}", arr, A, data)


# --------------------------------------------------------------------------------------
# Packer
# --------------------------------------------------------------------------------------

UNFOLD_FILL, UNFOLD_LEN_M = 0.5, 3.0


def _apply_planar(b, mesh, dup):
    """Overwrite the start layout of the planar parts (b.set_planar). Meters, like the unfold; the packer places them."""
    lay = mesh.uv_layers[cs2_uv.UNIQUE_LAYER]
    uv = cs2_uv.loop_uvs(lay)
    start, total = cs2_uv.face_loop_table(mesh)
    for name, (u, v) in b.planar.items():
        for f in b.part_faces(name):
            if f in dup:
                continue
            s0 = int(start[f])
            for k, vi in enumerate(b.F[f]):
                p = Vector(b.V[vi])
                uv[s0 + k] = (p.dot(u), p.dot(v))
    cs2_uv.set_loop_uvs(lay, uv)


def make_packer(b, report):
    def pack(obj, mesh, A, reserved):
        lay = mesh.uv_layers[cs2_uv.UNIQUE_LAYER]
        mesh.uv_layers.active = lay
        pinned = cs2_uv.pinned_faces(mesh, lay, reserved)
        if pinned:
            raise GroomerAuditError(f"{len(pinned)} faces inside the reserved strip")
        share = b.share_list()
        dup = b.dup_faces()
        # (a looser unfold than the props': this machine has long folded shells, and every extra island costs its 16 px margin)
        keep = snow_props.UNFOLD_MIN_FILL, snow_props.UNFOLD_MAX_LEN_M
        snow_props.UNFOLD_MIN_FILL, snow_props.UNFOLD_MAX_LEN_M = UNFOLD_FILL, UNFOLD_LEN_M
        try:
            unfold = snow_props._unfold_islands(mesh, dup)
        finally:
            snow_props.UNFOLD_MIN_FILL, snow_props.UNFOLD_MAX_LEN_M = keep
        _apply_planar(b, mesh, dup)
        n = len(mesh.polygons)
        rel = np.array([TIER_WEIGHT[b.tiers[p - 1]] for p in b.fpart], np.float32)
        dark = np.array([b.zones[p - 1] in DARK_ZONES for p in b.fpart])
        tmp = "groomer_density_w"
        attr = mesh.attributes.new(tmp, "FLOAT", "FACE")
        attr.data.foreach_set("value", rel.tolist())
        info = snow_props._pack_once(obj, mesh, A, reserved, tmp, share=share, dark_face=dark)
        mesh.attributes.remove(mesh.attributes[tmp])
        _apply_tiles(b, mesh)
        lay = mesh.uv_layers[cs2_uv.UNIQUE_LAYER]
        d = cs2_uv.face_px_per_m(mesh, lay, A)
        wa = cs2_uv.face_world_areas(mesh)
        tier_px = {}
        tiers = np.array([b.tiers[p - 1] for p in b.fpart])
        for t in TIER_WEIGHT:
            sel = (tiers == t) & np.isfinite(d)
            if sel.any():
                tier_px[t] = round(float(np.sqrt((d[sel] ** 2 * wa[sel]).sum() / wa[sel].sum())), 1)
        canon = np.ones(n, bool)
        canon[list(dup)] = False
        report.update({"islands": info["islands"], "scale": info["scale_px_per_unit"], "tier_px_per_m": tier_px, "squashed": info["squashed"],
                       "unfold": unfold, "area_m2_all": round(float(wa.sum()), 1), "area_m2_unique": round(float(wa[canon].sum()), 1)})
        print(f"[groomer] pack: {info['islands']} islands, {tier_px} px/m, area {report['area_m2_all']} m2 "
              f"({report['area_m2_unique']} unique), squashed {info['squashed']}")
        by_part = {}
        for i in np.nonzero(canon)[0]:
            nm = b.names[b.fpart[i] - 1]
            by_part[nm] = by_part.get(nm, 0.0) + float(wa[i])
        top = sorted(by_part.items(), key=lambda kv: -kv[1])[:24]
        print("[groomer] unique area by part: " + ", ".join(f"{k} {v:.1f}" for k, v in top))
        counts = _islands_by_part(b, mesh, lay, dup)
        print("[groomer] islands by part: " + ", ".join(f"{k} {v}" for k, v in sorted(counts.items(), key=lambda kv: -kv[1])[:40]))
    return pack


def _islands_by_part(b, mesh, lay, dup):
    """UV islands per part (faces joined by an edge whose two loops agree in UV), copies not counted."""
    uv = cs2_uv.loop_uvs(lay)
    start, total = cs2_uv.face_loop_table(mesh)
    lv = np.empty(len(mesh.loops), np.int64)
    mesh.loops.foreach_get("vertex_index", lv)
    seen = {}
    parent = {}

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i
    for f in range(len(mesh.polygons)):
        if f in dup:
            continue
        parent[f] = f
        s0, n = int(start[f]), int(total[f])
        for k in range(n):
            a, c = int(lv[s0 + k]), int(lv[s0 + (k + 1) % n])
            ua, uc = tuple(np.round(uv[s0 + k], 6)), tuple(np.round(uv[s0 + (k + 1) % n], 6))
            key = (min(a, c), max(a, c))
            val = (ua, uc) if a < c else (uc, ua)
            if key in seen and seen[key][1] == val:
                parent[find(f)] = find(seen[key][0])
            else:
                seen[key] = (f, val)
    counts = {}
    for r in {find(f) for f in parent}:
        nm = b.names[b.fpart[r] - 1]
        counts[nm] = counts.get(nm, 0) + 1
    return counts


def _apply_tiles(b, mesh):
    lay = mesh.uv_layers[cs2_uv.UNIQUE_LAYER]
    uv = cs2_uv.loop_uvs(lay)
    start, total = cs2_uv.face_loop_table(mesh)
    for key, t in b.tiles.items():
        c = int(t["canon"])
        s0 = int(start[c])
        q = uv[s0:s0 + 4].copy()       # (t,w) = (0,0), (1,0), (1,1), (0,1)
        for f, corners in t["dups"]:
            s = int(start[f])
            for k, (tt, ww) in enumerate(corners):
                uv[s + k] = (q[0] * (1 - tt) + q[1] * tt) * (1 - ww) + (q[3] * (1 - tt) + q[2] * tt) * ww
    cs2_uv.set_loop_uvs(lay, uv)


# --------------------------------------------------------------------------------------
# Paint: per-texel detail in the final UV space, from the author-frame position of each texel
# --------------------------------------------------------------------------------------

def lat(s, p, o=0.0):
    """Distance to the nearest o + k p."""
    return np.abs((s - o + 0.5 * p) % p - 0.5 * p)


def holes(a, c, pitch, r):
    """Staggered round holes on a lattice."""
    row = np.floor(c / pitch)
    a2 = a + (row % 2) * pitch * 0.5
    return lat(a2, pitch) ** 2 + lat(c, pitch) ** 2 < r * r


def sstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


class Tx:
    """One face's texel fields; painters write to alb, metal, gloss and H (meters), paint_lights to emit."""
    __slots__ = ("x", "y", "z", "n", "alb", "metal", "gloss", "coat", "emit", "H", "meta", "name", "meta_f")

    def tone(self, where, k):
        self.alb[where] *= k

    def zone(self, where, z):
        rgb, m, g = ZONES[z]
        self.alb[where] = ZL[z]
        self.metal[where] = m
        self.gloss[where] = g
        self.coat[where] = COAT.get(z, 0.0)


BELT_GAP = 0.035    # half width of the painted gap between two belts (7 cm open)


def p_cleat(c):
    m = c.meta
    o, T, N = np.array(m["o"]), np.array(m["T"]), np.array(m["N"])
    Y = np.array((0.0, float(m["s"]), 0.0))
    dx, dy, dz = c.x - o[0], c.y - o[1], c.z - o[2]
    n = (dx * N[0] + dy * N[1] + dz * N[2]) / CH
    t = dx * T[0] + dy * T[1] + dz * T[2]
    w = dx * Y[0] + dy * Y[1] + dz * Y[2]
    top = n > 0.985
    c.alb *= (0.80 + 0.20 * np.clip(n, 0.0, 1.0))[..., None]
    c.tone(top, 1.12)
    # the fine saw along one edge of the top, as in the track photos
    tooth = np.abs(((w / 0.085) % 1.0) - 0.5) * 2.0
    notch = top & (t > CLEAT_TOP - 0.014 * tooth)
    c.zone(notch, "steel")
    c.tone(notch, 0.55)
    c.H[notch] -= 0.008
    bolts = top & (lat(w, 0.55, 0.275) < 0.014) & (np.abs(t + 0.008) < 0.012)
    c.tone(bolts, 0.45)


def p_belt(c):
    """Outer face: six narrow belts with near-black gaps. The cleat bars are painted under the real cleats at their width, so
    LOD1 and LOD2 (no cleats) still read as a bright ladder."""
    m = c.meta
    t = np.abs(c.x - m["x0"]) / m["pitch"]
    w = np.abs(c.y) - YI
    c.alb[...] = BELT_BAND
    gap = lat(w, TW / 6.0) < BELT_GAP
    c.alb[gap] = ZL["rubber"] * 0.12
    c.H[gap] -= 0.006
    bar_ = lat(t, 1.0, 0.5) < 0.16
    c.zone(bar_, "cleat")
    c.tone(bar_, 0.88)
    c.H[bar_] += 0.008


def p_belt_in(c):
    """Inner face: the same six belts, with a dark ladder of steel links."""
    m = c.meta
    t = np.abs(c.x - m["x0"]) / m["pitch"]
    w = np.abs(c.y) - YI
    c.alb[...] = BELT_BAND
    gap = lat(w, TW / 6.0) < BELT_GAP
    c.alb[gap] = ZL["rubber"] * 0.12
    c.H[gap] -= 0.006
    bar_ = lat(t, 1.0, 0.5) < 0.13
    c.zone(bar_, "steel")
    c.tone(bar_, 0.80)
    c.metal[bar_] = 0.1
    c.gloss[bar_] = 0.3
    c.H[bar_] += 0.008


def p_belt_edge(c):
    """The belt's outer edge, seen from the side: a thin band with a dark link gap halfway between two cleats."""
    m = c.meta
    t = np.abs(c.x - m["x0"]) / m["pitch"]
    c.alb[...] = BELT_BAND
    gap = lat(t, 1.0) < 0.10
    c.alb[gap] = ZL["rubber"] * 0.12
    c.H[gap] -= 0.006


def p_wheel(c):
    m = c.meta
    C, U, V, Ax = (np.array(m[k]) for k in ("c", "u", "v", "a"))
    dx, dy, dz = c.x - C[0], c.y - C[1], c.z - C[2]
    a = dx * Ax[0] + dy * Ax[1] + dz * Ax[2]
    pu = dx * U[0] + dy * U[1] + dz * U[2]
    pv = dx * V[0] + dy * V[1] + dz * V[2]
    rho = np.hypot(pu, pv) / m["r"]
    side = float(np.dot(c.n, Ax)) > 0.5
    if not side:
        groove = lat(a, 0.075, 0.03) < 0.012
        c.tone(groove, 0.45)
        c.H[groove] -= 0.008
        return
    ang = np.arctan2(pv, pu)
    spoke = lat(ang + 0.32, 2.0 * math.pi / 5.0) * rho * m["r"] < 0.042
    rim = (rho < 0.70) & (rho > 0.585)
    disc = rho <= 0.585
    c.zone(disc, "black")
    c.tone(disc, 0.25)
    c.zone(disc & spoke, "steel")
    c.H[disc & spoke] += 0.018
    c.zone(rim, "steel")
    c.H[rim] += 0.012
    hub = rho < 0.22
    c.zone(hub, "gray")
    c.H[hub] += 0.012
    nuts = (rho > 0.11) & (rho < 0.18) & (lat(ang, 2.0 * math.pi / 5.0) * rho < 0.05)
    c.tone(nuts, 0.45)


def p_lamp(c):
    m = c.meta
    C, Ax = np.array(m["c"]), np.array(m["a"])
    dx, dy, dz = c.x - C[0], c.y - C[1], c.z - C[2]
    a = dx * Ax[0] + dy * Ax[1] + dz * Ax[2]
    rad = np.sqrt(np.maximum(dx * dx + dy * dy + dz * dz - a * a, 0.0)) / m["r"]
    mid = (rad < 0.86) & (rad >= 0.30)
    c.alb[mid] *= 0.62
    ring = (rad < 0.34) & (rad >= 0.24)
    c.alb[ring] *= 0.45
    c.zone(rad >= 0.86, "chrome")
    c.tone(rad >= 0.86, 0.35)
    c.zone(rad > 1.0, "black")


def p_lowerbody(c):
    x, z = c.x, c.z
    if float(c.n[0]) > 0.99:
        c.zone(np.ones_like(x, dtype=bool), "black")     # the nose panel: part of the black frame under the red chin
        return
    side = abs(float(c.n[1])) > 0.4
    sill = z < 1.075
    c.zone(sill, "black")
    if side:
        for sx in (2.27, 1.18):
            seam = np.abs(x - sx) < 0.014
            c.zone(seam, "black")
            c.tone(seam, 0.6)
            c.H[seam] -= 0.006
    crease = np.abs(z - 1.078) < 0.012
    c.H[crease] -= 0.004


def p_hood(c):
    x, y, z = c.x, c.y, c.z
    sill = z < 1.075
    c.zone(sill, "black")
    # two hood-panel seams and the service seam behind the cab
    s1 = (np.abs(x + 0.52) < 0.013) & (z > 1.075)
    c.zone(s1, "black")
    c.H[s1] -= 0.006
    if float(c.n[2]) > 0.8:
        s2 = np.abs(np.abs(y) - 0.74) < 0.013
        c.zone(s2, "black")
        c.H[s2] -= 0.006


def p_vent(c):
    slat = lat(c.z, 0.082) < 0.024
    c.tone(slat, 0.28)
    c.H[slat] -= 0.014
    lip = ~slat
    c.alb[lip] *= 1.35


def p_perf(pitch, r):
    def f(c):
        if abs(float(c.n[2])) > 0.75:
            a, cc = c.x, c.y
        elif abs(float(c.n[0])) > 0.7:
            a, cc = c.y, c.z
        else:
            a, cc = c.x + c.y, c.z
        h = holes(a, cc, pitch, r)
        c.alb[h] = ZL["rubber"] * 0.25
        c.metal[h] = 0.0
        c.gloss[h] = 0.1
        c.H[h] -= 0.012
    return f


def p_band(c):
    """The blade's perforated band; opaque, so the square holes are dark texels in a lighter plate, inside a solid margin."""
    f = np.array(c.meta["f"])
    along = c.x * (-f[1]) + c.y * f[0]
    zr = c.z - BLADE_Z0
    c.alb[...] = ZL["black"] * 1.9
    inside = (zr > 0.86) & (zr < 1.125)
    h = inside & (lat(along, 0.082) < 0.030) & (lat(zr, 0.082) < 0.030)
    c.alb[h] = ZL["rubber"] * 0.16
    c.metal[h] = 0.0
    c.gloss[h] = 0.08
    c.H[h] -= 0.012


def p_mold(c):
    """Working face of the moldboard: a straight worn strip on the lip with a bolt row and a slot under the band."""
    if c.meta_f is None:
        return
    if float(c.n[0]) * float(c.meta_f[0]) + float(c.n[1]) * float(c.meta_f[1]) < 0.1:
        return
    zr = c.z - BLADE_Z0
    along = c.x * (-c.meta_f[1]) + c.y * c.meta_f[0]
    edge = zr < 0.135
    c.zone(edge, "steel")
    line = np.abs(zr - 0.14) < 0.010
    c.tone(line, 0.4)
    c.H[line] -= 0.006
    bolts = (lat(along, 0.26, 0.13) < 0.016) & (np.abs(zr - 0.09) < 0.016)
    c.tone(bolts, 0.35)
    c.H[bolts] += 0.006
    slot = (np.abs(zr - 0.70) < 0.011)
    c.alb[slot] = ZL["steel"] * 1.3
    c.H[slot] -= 0.006
    # a low-frequency sheen where snow rolls off the curve
    c.alb += (ZL["steel"] - c.alb) * (sstep(0.18, 0.40, zr) * sstep(0.62, 0.42, zr) * 0.25)[..., None]


def p_thood(c):
    y, z = c.y, c.z
    flap = z < 0.50
    c.zone(flap, "black")
    rib = lat(y, 0.10) < 0.028
    c.H[flap & rib] += 0.012
    c.alb[flap & rib] = ZL["steel"] * 1.2
    seam = np.abs(z - 0.50) < 0.014
    c.zone(seam, "black")
    c.tone(seam, 0.5)
    c.H[seam] -= 0.006


def p_comb(c):
    y = c.y
    # a black rubber mat with fine, shallow ribs (pb600-laterns-6plus-02); deep steel-gray ribs read as corrugated sheet
    groove = lat(y, 0.062) < 0.016
    c.zone(np.ones_like(y, dtype=bool), "rubber")
    c.alb[~groove] *= 1.6
    c.tone(groove, 0.8)
    c.H[groove] -= 0.004
    wear = sstep(-3.95, -4.40, c.x)
    c.alb += (ZL["gray"] * 0.70 - c.alb) * (wear * 0.18 * (~groove))[..., None]


def p_drum(c):
    x, y, z = c.x, c.y, c.z
    ang = np.arctan2(z - 0.29, x + 3.35)
    spike = (lat(y + ang * 0.15, 0.13) < 0.024) & (lat(ang, 2 * math.pi / 8) < 0.20)
    c.zone(spike, "gray")
    c.H[spike] += 0.015


def p_deck(c):
    x, y, z = c.x, c.y, c.z
    if float(c.n[2]) < 0.8:
        return
    # tread plate
    d1 = lat(x + y, 0.15) < 0.012
    d2 = lat(x - y, 0.15) < 0.012
    tread = d1 ^ d2
    c.H[tread] += 0.003
    c.tone(tread, 1.07)
    # perforated center panel, as seen from above in the top-view reference
    panel = (np.abs(y) < 0.58) & (x > -1.98) & (x < -1.08)
    c.zone(panel, "alu")
    h = holes(x, y, 0.064, 0.017)
    c.alb[panel & h] = ZL["rubber"] * 0.5
    c.H[panel & h] -= 0.008
    frame = (np.abs(np.abs(y) - 0.60) < 0.014) & (x > -2.0) & (x < -1.06)
    frame |= ((np.abs(x + 2.0) < 0.014) | (np.abs(x + 1.06) < 0.014)) & (np.abs(y) < 0.61)
    c.tone(frame, 0.6)
    edge = np.abs(np.abs(y) - 1.13) < 0.05
    c.zone(edge, "black")


def p_roof(c):
    x, y = c.x, c.y
    if float(c.n[2]) < 0.8:
        return
    hatch = (np.abs(y) < 0.29) & (x > 1.10) & (x < 1.56)
    c.zone(hatch, "glass")
    rim = (np.abs(np.abs(y) - 0.31) < 0.02) & (x > 1.08) & (x < 1.58)
    rim |= ((np.abs(x - 1.08) < 0.02) | (np.abs(x - 1.58) < 0.02)) & (np.abs(y) < 0.33)
    c.zone(rim, "black")
    c.H[rim] += 0.010


def p_glass(c):
    """Opaque glass: darker low, a little sky high (low frequency), a soft seat shape behind it."""
    x, y, z = c.x, c.y, c.z
    k = sstep(1.45, 2.65, z)
    c.alb[...] = ZL["glass"] * (1.0 + 2.1 * k)[..., None]
    dark = ZL["rubber"] * 0.7

    def blob(d, w, amount):
        cov = np.clip(0.5 - d / w, 0.0, 1.0) * amount
        c.alb += (dark - c.alb) * cov[..., None]
    if abs(float(c.n[1])) > 0.7:
        blob(np.maximum(np.abs(x - 1.40) - 0.20, np.maximum(1.62 - z, z - 2.16)), 0.05, 0.75)
        blob(np.hypot(x - 1.38, z - 2.24) - 0.11, 0.04, 0.75)
        blob(np.maximum(np.abs(x - 1.98) - 0.09, z - 1.95), 0.05, 0.75)
    elif float(c.n[0]) > 0.6:
        for sy in (0.40, -0.40):
            blob(np.maximum(np.abs(y - sy) - 0.20, z - 2.05), 0.12, 0.35)
        blob(z - 1.60, 0.10, 0.8)


def p_mirror(c):
    if float(c.n[0]) < -0.7:
        c.zone(np.ones_like(c.x, dtype=bool), "glass")
        c.alb *= 2.4


PAINTERS = (
    ("Cleat", p_cleat), ("Wheel", p_wheel), ("Idler", p_wheel), ("Lamp", p_lamp),
    ("LowerBody", p_lowerbody), ("Hood", None), ("Glass", p_glass), ("Deck", p_deck), ("Roof", p_roof),
    ("BladeBand", p_band), ("BladeC", p_mold), ("BladeW", p_mold), ("TillerHood", p_thood), ("Comb", p_comb),
    ("Drum", p_drum), ("Exhaust", None),
)


def _painter(name):
    if name.startswith("BeltOuter"):
        return p_belt
    if name.startswith("BeltInner"):
        return p_belt_in
    if name.startswith("BeltEdge"):
        return p_belt_edge
    if name.startswith("HoodVent"):
        return p_vent
    if name.startswith("HoodGrille"):
        return p_perf(0.06, 0.020)
    if name == "Hood":
        return p_hood
    if name.startswith("MirrorHead"):
        return p_mirror
    if name == "ExhaustGuard":
        return p_exhaust
    for pre, fn in PAINTERS:
        if fn is not None and name.startswith(pre):
            return fn
    return None


def p_exhaust(c):
    ang = np.arctan2(c.y + 0.78, c.x - 0.36)
    a = ang * 0.09
    h = holes(a, c.z, 0.052, 0.018)
    c.alb[h] = ZL["rubber"] * 0.25
    c.metal[h] = 0.0
    c.H[h] -= 0.008


# Emitted color, linear. The white is the flood effect's own color (SkiPisteFloodSource), the amber vanilla EU_Snowplow01's Warning1 / Warning2.
LIGHT_WHITE = _srgb_to_lin(np.array((1.0, 0.965, 0.92), np.float64)).astype(np.float32)
LIGHT_AMBER = _srgb_to_lin(np.array((1.0, 0.698, 0.0), np.float64)).astype(np.float32)
NOSE_SLIT = (0.44, 0.72, 1.065, 1.115)     # |y| and z: two nose lights without lamp geometry, a slit in the black panel above the apron lip
HALO_LAMP = (0.066, 0.085)                 # full under the lamp's 0.067 bezel, gone 2 cm outside it
HALO_BEACON = (0.072, 0.10)                # full under the dome's 0.072 foot, gone 3 cm outside it


def _soft(d, r0, r1):
    """1 inside r0, 0 outside r1."""
    return np.clip((r1 - d) / (r1 - r0), 0.0, 1.0)


def paint_lights(c):
    """The Emissive map, one face at a time. BaseColor stays as it is, so what is painted here is invisible by day. The bars emit under their lamps and
    beacons: covered on LOD0 but for a soft rim, and what is left once a level deletes them (the rear lamps at LOD1, the front lamps and beacons at LOD2)."""
    name = c.name
    if name.startswith("LampRoof"):
        m = c.meta
        C, Ax = np.array(m["c"]), np.array(m["a"])
        dx, dy, dz = c.x - C[0], c.y - C[1], c.z - C[2]
        a = dx * Ax[0] + dy * Ax[1] + dz * Ax[2]
        rad = np.sqrt(np.maximum(dx * dx + dy * dy + dz * dz - a * a, 0.0)) / m["r"]
        c.emit[rad < 0.86] = LIGHT_WHITE
    elif name.startswith("Beacon"):
        c.emit[...] = LIGHT_AMBER
    elif name == "LightBar":
        if float(c.n[2]) > 0.9:
            d = np.hypot(c.x - BEACON_X, np.abs(c.y) - BEACON_Y)
            c.emit += LIGHT_AMBER * _soft(d, *HALO_BEACON)[..., None]
        elif float(c.n[0]) > 0.9:
            d = np.min([np.hypot(np.abs(c.y) - y, c.z - BAR_LAMP_Z) for y in BAR_LAMP_Y], axis=0)
            c.emit += LIGHT_WHITE * _soft(d, *HALO_LAMP)[..., None]
    elif name == "LightBarRear":
        if float(c.n[0]) < -0.9:
            d = np.min([np.hypot(np.abs(c.y) - y, c.z - BAR_LAMP_Z) for y in REAR_LAMP_Y], axis=0)
            c.emit += LIGHT_WHITE * _soft(d, *HALO_LAMP)[..., None]
    elif name == "LowerBody" and float(c.n[0]) > 0.99:
        y0, y1, z0, z1 = NOSE_SLIT
        c.emit[(np.abs(c.y) > y0) & (np.abs(c.y) < y1) & (c.z > z0) & (c.z < z1)] = LIGHT_WHITE


PAINT_SS = 2


def _box3(a):
    return sum(np.roll(np.roll(a, i, 0), j, 1) for i in (-1, 0, 1) for j in (-1, 0, 1)) / 9.0


def make_hook(b, report):
    def hook(base, nrm, msk, ctl, ao, mesh, A):
        table = b.names
        Ah = A * PAINT_SS
        alb = np.zeros((Ah, Ah, 3), np.float32)
        metal = np.zeros((Ah, Ah), np.float32)
        gloss = np.zeros((Ah, Ah), np.float32)
        coat = np.zeros((Ah, Ah), np.float32)
        emit = np.zeros((Ah, Ah, 3), np.float32)
        H = np.zeros((Ah, Ah), np.float32)
        cover = np.zeros((Ah, Ah), np.float32)
        stacked_hi = np.zeros((Ah, Ah), np.float32)
        ppm_map = np.full((Ah, Ah), 120.0 * PAINT_SS, np.float32)
        dup = b.dup_faces()
        stacked = b.stacked_faces()
        canon_belts = {b.meta[k]["face"]: b.meta[k] for k in ("BeltOuterCanon", "BeltInnerCanon", "BeltEdgeCanon")}
        ax = np.eye(3)
        painted = 0
        for poly, sl, mask, X, Y, q, Pv, ppm in cs2_compose.iter_face_rasters(mesh, Ah, "UVUnique", dilate_px=2.0):
            fi = poly.index
            if fi in dup:
                continue
            pid = b.fpart[fi]
            name = table[pid - 1]
            zone = b.zones[pid - 1]
            h, w = mask.shape
            rgb, mm, gg = ZONES[zone]
            t = Tx()
            qm, Pm, Mt = cs2_compose._affine_uv_to_world(q, Pv)
            t.x = cs2_compose._world_coord_field(ax[0], X, Y, qm, Pm, Mt)
            t.y = cs2_compose._world_coord_field(ax[1], X, Y, qm, Pm, Mt)
            t.z = cs2_compose._world_coord_field(ax[2], X, Y, qm, Pm, Mt)
            t.n = np.array(tuple(poly.normal))
            t.alb = np.tile(ZL[zone], (h, w, 1))
            t.metal = np.full((h, w), mm, np.float32)
            t.gloss = np.full((h, w), gg, np.float32)
            t.coat = np.full((h, w), COAT.get(zone, 0.0), np.float32)
            t.emit = np.zeros((h, w, 3), np.float32)
            t.H = np.zeros((h, w), np.float32)
            t.name = name
            t.meta = b.meta.get(name, {})
            t.meta_f = t.meta.get("mold_f")
            if fi in canon_belts:
                t.meta = canon_belts[fi]
            fn = _painter(name)
            if fn is not None:
                fn(t)
            paint_lights(t)
            alb[sl][mask] = t.alb[mask]
            metal[sl][mask] = t.metal[mask]
            gloss[sl][mask] = t.gloss[mask]
            coat[sl][mask] = t.coat[mask]
            emit[sl][mask] = t.emit[mask]
            H[sl][mask] = t.H[mask]
            ppm_map[sl][mask] = ppm
            cover[sl][mask] = 1.0
            if fi in stacked:
                stacked_hi[sl][mask] = 1.0
            painted += 1
        n_hi = cs2_compose.height_to_normal(H, ppm_map, 1.0) * 2.0 - 1.0
        ss = PAINT_SS

        def down(a):
            """Coverage-weighted mean of each ss x ss block."""
            w = cover.reshape(A, ss, A, ss)
            shape = (A, ss, A, ss) + a.shape[2:]
            if a.ndim == 3:
                return (a.reshape(shape) * w[..., None]).sum((1, 3))
            return (a.reshape(shape) * w).sum((1, 3))
        ws = cover.reshape(A, ss, A, ss).sum((1, 3))
        inside = ws > 0.0
        safe = np.maximum(ws, 1.0)
        occ = ao[..., 0]
        occ = _box3(_box3(occ))
        occ = np.clip(occ * 0.92 + 0.08, 0.0, 1.0)
        frac = down(stacked_hi) / safe
        occ = occ * (1.0 - frac) + 0.94 * frac
        out_b = base.copy()
        out_b[..., :3][inside] = (down(alb)[inside] / safe[inside][..., None]) * occ[inside][..., None]
        out_b[..., 3] = 1.0
        out_n = nrm.copy()
        nn = down(n_hi)[inside]
        nn /= np.maximum(np.linalg.norm(nn, axis=1, keepdims=True), 1e-6)
        out_n[..., :3][inside] = nn * 0.5 + 0.5
        out_n[..., 3] = 1.0
        out_m = msk.copy()
        out_m[..., 0][inside] = np.clip(down(metal)[inside] / safe[inside], 0.0, 1.0)
        out_m[..., 1] = 0.0
        out_m[..., 1][inside] = np.clip(down(coat)[inside] / safe[inside], 0.0, 1.0)
        out_m[..., 2] = 0.0
        out_m[..., 3][inside] = np.clip(down(gloss)[inside] / safe[inside], 0.0, 1.0)
        out_c = np.zeros_like(ctl)
        report["painted_faces"] = painted
        # written by build, beside the four maps the finish pass writes
        out_e = np.zeros((A, A, 4), np.float32)
        out_e[..., 3] = 1.0
        out_e[..., :3][inside] = _lin_to_srgb(np.clip(down(emit)[inside] / safe[inside][..., None], 0.0, 1.0))
        report["emissive_rgba"] = out_e
        return out_b, out_n, out_m, out_c
    return hook


# --------------------------------------------------------------------------------------
# Audits (raise on failure)
# --------------------------------------------------------------------------------------

def audit_names(b):
    for n in b.names:
        low = n.lower()
        for w in BANNED_WORDS:
            if w in low:
                raise GroomerAuditError(f"part {n}: brand word {w!r} on the model")
        if "_" in n or "." in n:
            raise GroomerAuditError(f"part {n}: separators in a part name")
    cs2_asset.validate_asset_name(NAME)


def audit_footprint(b):
    co = np.array(b.V, np.float64)
    lo, hi = co.min(0), co.max(0)
    if abs(lo[2]) > 1e-4:
        raise GroomerAuditError(f"lowest point z {lo[2]:.4f}, must be 0")
    if abs(lo[0] + hi[0]) > 0.02 or abs(lo[1] + hi[1]) > 0.02:
        raise GroomerAuditError(f"origin is off the footprint center: x {lo[0]:.3f}..{hi[0]:.3f}, y {lo[1]:.3f}..{hi[1]:.3f}")
    ext = hi - lo
    if abs(ext[0] - OVERALL_L) > 0.03:
        raise GroomerAuditError(f"overall length {ext[0]:.3f} vs {OVERALL_L}")
    return {"x": (round(float(lo[0]), 3), round(float(hi[0]), 3)), "y": (round(float(lo[1]), 3), round(float(hi[1]), 3)),
            "z": (round(float(lo[2]), 3), round(float(hi[2]), 3))}


def connected_components(b, tol=0.025, skip=()):
    """Union-find over parts: two parts are joined when their surfaces intersect or come within `tol`. Parts whose name
    starts with a prefix in `skip` do not count (the parts a LOD deletes)."""
    from mathutils.bvhtree import BVHTree
    n = len(b.names)
    verts = [[] for _ in range(n)]
    faces = [[] for _ in range(n)]
    vmap = [{} for _ in range(n)]
    for f, pid in zip(b.F, b.fpart):
        p = pid - 1
        loc = []
        for vi in f:
            m = vmap[p]
            if vi not in m:
                m[vi] = len(verts[p])
                verts[p].append(Vector(b.V[vi]))
            loc.append(m[vi])
        faces[p].append(loc)
    trees = [BVHTree.FromPolygons(verts[i], faces[i], all_triangles=False, epsilon=0.0) for i in range(n)]
    lo = np.array([np.min(np.array(v), axis=0) for v in verts])
    hi = np.array([np.max(np.array(v), axis=0) for v in verts])
    parent = list(range(n))
    alive = [not nm.startswith(tuple(skip)) for nm in b.names]

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    for i in range(n):
        if not alive[i]:
            continue
        near = np.all((lo[i + 1:] - tol <= hi[i]) & (hi[i + 1:] + tol >= lo[i]), axis=1)
        for k in np.nonzero(near)[0]:
            j = i + 1 + int(k)
            if not alive[j]:
                continue
            ri, rj = find(i), find(j)
            if ri == rj:
                continue
            touch = bool(trees[i].overlap(trees[j]))
            if not touch:
                small, big = (i, j) if len(verts[i]) <= len(verts[j]) else (j, i)
                for v in verts[small]:
                    if trees[big].find_nearest(v, tol)[0] is not None:
                        touch = True
                        break
                if not touch:
                    for v in verts[big]:
                        if trees[small].find_nearest(v, tol)[0] is not None:
                            touch = True
                            break
            if touch:
                parent[ri] = rj
    groups = {}
    for i in range(n):
        if alive[i]:
            groups.setdefault(find(i), []).append(i)
    return sorted(groups.values(), key=len, reverse=True)


def audit_nothing_floats(b):
    comps = connected_components(b)
    if len(comps) != 1:
        lost = [b.names[i] for c in comps[1:] for i in c]
        raise GroomerAuditError(f"{len(comps)} separate pieces: floating parts {lost[:24]}")
    return len(comps)


# the tracks sit under the hull with a shadow gap; below LOD0 the cross tubes that carry them are gone, which no camera can tell
LOD_ATTACH_IGNORE = ("Belt", "Wheel", "TrackBack")


def audit_lods_attached(b, lod_delete=LOD_DELETE):
    """The float gate on the lower levels: what a level keeps, the tracks aside, must still be one piece."""
    for lv, prefixes in enumerate(lod_delete, 1):
        comps = connected_components(b, skip=tuple(prefixes) + LOD_ATTACH_IGNORE)
        if len(comps) != 1:
            lost = [b.names[i] for c in comps[1:] for i in c]
            raise GroomerAuditError(f"LOD{lv}: {len(comps)} separate pieces: floating parts {lost[:24]}")
    return len(lod_delete)


def audit_lod0_tris(n):
    if n > LOD0_CAP:
        raise GroomerAuditError(f"LOD0 {n} tris over the hard cap {LOD0_CAP}")
    if n < LOD0_MIN:
        raise GroomerAuditError(f"LOD0 {n} tris under the budget floor {LOD0_MIN}")


def audit_lod_chain(tris):
    if tris[1] > 0.5 * tris[0]:
        raise GroomerAuditError(f"LOD1 {tris[1]} > 50 % of LOD0 {tris[0]}")
    if tris[2] > LOD2_CAP or tris[2] >= tris[1]:
        raise GroomerAuditError(f"LOD2 {tris[2]} tris (cap {LOD2_CAP}, LOD1 {tris[1]})")


def audit_no_mirror(mesh, layer="UVUnique"):
    """Never mirror a UV island: every face's tangent frame must be right-handed."""
    uv = cs2_uv.loop_uvs(mesh.uv_layers[layer])
    start, total = cs2_uv.face_loop_table(mesh)
    bad = []
    for p in mesh.polygons:
        s = int(start[p.index])
        q = uv[s:s + int(total[p.index])]
        a = 0.0
        for i in range(len(q)):
            a += q[i][0] * q[(i + 1) % len(q)][1] - q[(i + 1) % len(q)][0] * q[i][1]
        if a <= 0.0:
            bad.append(p.index)
    if bad:
        table = json.loads(mesh[cs2_asset.PART_TABLE_PROP])
        attr = mesh.attributes["part"].data
        names = sorted({table[attr[i].value - 1] for i in bad})
        raise GroomerAuditError(f"{len(bad)} faces with a mirrored or empty UV island: faces {bad[:8]}, parts {names[:12]}")
    return 0


def audit_textures(asset_dir):
    for m in ("BaseColor", "Normal", "MaskMap", "ControlMask"):
        p = os.path.join(asset_dir, f"{NAME}_{m}.png")
        w, h, depth, ctype = cs2_validate.read_png_header(p)
        if w != h or w < 512 or w > cs2_asset_ceiling():
            raise GroomerAuditError(f"{m}: {w} x {h} outside 512..{cs2_asset_ceiling()}")
    px = cs2_validate.load_png_pixels(os.path.join(asset_dir, f"{NAME}_BaseColor.png"))
    if float(px[..., 3].min()) < 1.0 - 0.5 / 255.0:
        raise GroomerAuditError("BaseColor alpha below 255 flips the surface to the cutout shader")
    p = os.path.join(asset_dir, f"{NAME}_Emissive.png")
    w, h, depth, ctype = cs2_validate.read_png_header(p)
    if w != ATLAS or h != ATLAS:
        raise GroomerAuditError(f"Emissive: {w} x {h}, the pack is {ATLAS}")
    if ctype != 6:
        raise GroomerAuditError(f"Emissive: PNG color type {ctype}: a map without alpha ships uncompressed (prop-emissive-lights.md 1.3)")
    px = cs2_validate.load_png_pixels(p)
    if float(px[..., 3].min()) < 1.0 - 0.5 / 255.0:
        raise GroomerAuditError("Emissive alpha below 255")
    lit = np.max(px[..., :3], axis=2) > 2.0 / 255.0
    share = float(lit.mean())
    if not 0.0005 < share < EMISSIVE_MAX_SHARE:
        raise GroomerAuditError(f"Emissive: {share:.2%} of the atlas is lit, wanted small lights, under {EMISSIVE_MAX_SHARE:.0%}")


# Lit area each level must keep, m2: about two thirds of what the build measures (the manifest's lit_m2). A level that loses its lights fails here.
EMISSIVE_MAX_SHARE = 0.03
LIGHT_AREA_MIN = {0: {"white": 0.20, "amber": 0.09}, 1: {"white": 0.18, "amber": 0.09}, 2: {"white": 0.10, "amber": 0.035}}
LIGHT_SAMPLE_M = 0.015                # sample spacing: the lights are discs of 16 cm on faces up to 2 m long


def light_areas(asset_dir, objs):
    """Lit surface per level and color, m2: an even grid over each triangle, on the Emissive map through the shipped UVs."""
    px = cs2_validate.load_png_pixels(os.path.join(asset_dir, f"{NAME}_Emissive.png"))
    H, W = px.shape[:2]
    grids = {}

    def grid(n):
        """The centroids of a triangle cut into n x n equal triangles, as barycentric weights."""
        if n not in grids:
            i, j = np.meshgrid(np.arange(n), np.arange(n), indexing="ij")
            keep_up, keep_dn = i + j < n, i + j < n - 1
            a = np.concatenate([(i[keep_up] + 1 / 3) / n, (i[keep_dn] + 2 / 3) / n])
            c = np.concatenate([(j[keep_up] + 1 / 3) / n, (j[keep_dn] + 2 / 3) / n])
            grids[n] = np.stack([a, c, 1.0 - a - c], axis=1)
        return grids[n]

    out = {}
    for lv, ob in objs.items():
        me = ob.data
        me.calc_loop_triangles()
        uv = cs2_uv.loop_uvs(me.uv_layers[cs2_uv.UNIQUE_LAYER])
        white = amber = 0.0
        for tri in me.loop_triangles:
            n = max(1, min(160, int(math.ceil(math.sqrt(2.0 * tri.area) / LIGHT_SAMPLE_M))))
            pts = grid(n) @ uv[list(tri.loops)]
            t = px[np.clip((pts[:, 1] * H).astype(int), 0, H - 1), np.clip((pts[:, 0] * W).astype(int), 0, W - 1)]
            r, g, bl = t[:, 0], t[:, 1], t[:, 2]
            w_lit = (bl > 0.5) & (g > 0.5)
            a_lit = (r > 0.4) & (bl < 0.1) & (g < 0.9 * r)
            white += tri.area * float(w_lit.sum()) / len(pts)
            amber += tri.area * float(a_lit.sum()) / len(pts)
        out[lv] = {"white": round(white, 4), "amber": round(amber, 4)}
    return out


def audit_lights(asset_dir, objs, minimum=None):
    """Every level keeps both colors of light. Lights live in the texture, so this is the only gate that a level really shows them."""
    areas = light_areas(asset_dir, objs)
    minimum = LIGHT_AREA_MIN if minimum is None else minimum
    for lv, a in areas.items():
        for color, need in minimum[lv].items():
            if a[color] < need:
                raise GroomerAuditError(f"LOD{lv} shows {a[color]:.3f} m2 of {color} light, wanted at least {need:.3f}")
    return areas


def cs2_asset_ceiling():
    return snow_props.TEX_CEIL_UNDER_10M


# --------------------------------------------------------------------------------------
# Build pipeline
# --------------------------------------------------------------------------------------

def author_to_game(p):
    """Frame rule T: author (x, y, z) -> game local (-y, z, x)."""
    return (round(-p[1], 3), round(p[2], 3), round(p[0], 3))


def lights_table(b):
    return {k: {"author_xyz": v, "game_local_xyz": [author_to_game(p) for p in v]} for k, v in sorted(b.lights.items())}


def make_lods(obj):
    table = json.loads(obj.data[cs2_asset.PART_TABLE_PROP])
    attr = obj.data.attributes["part"].data
    targets = []
    for prefixes in LOD_DELETE:
        targets.append(sum(len(p.vertices) - 2 for p in obj.data.polygons if not table[attr[p.index].value - 1].startswith(prefixes)))
    return cs2_asset.make_lod_chain(obj, NAME, targets, LOD_DELETE)


def build(asset_dir, export=True):
    """Everything up to and including the validator. Returns a dict with the objects and facts."""
    b = build_all()
    audit_names(b)
    foot = audit_footprint(b)
    audit_nothing_floats(b)
    audit_lods_attached(b)
    obj = b.to_object(NAME)
    tris0 = cs2_asset.tri_count(obj)
    audit_lod0_tris(tris0)
    print(f"[groomer] LOD0 {tris0} tris, {len(b.names)} parts, footprint {foot}")
    report = {}
    cs2_asset.shade_by_angle(obj, 66.0, weighted=False)
    write_source_atlas(asset_dir)
    ok = lift_tower.apply_finish_textures(obj, asset_dir, NAME, atlas_size=ATLAS, reserved_strips=[RESERVED], ao_samples=128,
                                          post_compose=make_hook(b, report), packer=make_packer(b, report))
    if not ok:
        raise GroomerAuditError("finish bake failed")
    cs2_asset._write_png_raw(os.path.join(asset_dir, f"{NAME}_Emissive.png"), f"{NAME}_Emissive", report.pop("emissive_rgba"), ATLAS, False)
    audit_no_mirror(obj.data)
    audit_textures(asset_dir)
    cs2_asset.orient_for_net(obj)
    cs2_asset.shade_by_angle(obj, 66.0, weighted=True)
    lods = make_lods(obj)
    tris = {0: cs2_asset.tri_count(obj), 1: cs2_asset.tri_count(lods[0]), 2: cs2_asset.tri_count(lods[1])}
    audit_lod_chain(tris)
    print(f"[groomer] LOD tris {tris}")
    areas = audit_lights(asset_dir, {0: obj, 1: lods[0], 2: lods[1]})
    print(f"[groomer] lit surface, m2: {areas}")
    report["lit_m2"] = {f"LOD{k}": v for k, v in areas.items()}
    out = {"b": b, "lod0": obj, "lod1": lods[0], "lod2": lods[1], "tris": tris, "footprint": foot, "pack": report, "dir": asset_dir}
    if export:
        export_folder(out)
    return out


VALIDATE_SPEC = {"kind": "prop", "lod0_cap": LOD0_CAP, "lod1_ratio": 0.5, "lod2_cap": LOD2_CAP, "expected_lods": (1, 2), "max_texture": snow_props.TEX_CEIL_UNDER_10M}


# The effects' lamps have no mesh, so they sit where they light best: the flood at the bar's center, the amber well above it, where it leaves no hot spot.
# The rear flood mirrors the front one, aimed back and 25 degrees down: real rear work lights light the slope behind the tiller as well as
# ahead. It sits at the front one's height over the tiller hood's rear edge, so every red panel is outside its 110 degree cone (in game,
# 2026-11-01/02): on the rear roof bar it burned a hot spot on the engine hood 1 to 2 m below, and over the deck's rear end (x -2.21) it lit
# the red tiller hood 2.5 m away at full strength, which read as an amber light bar from 350 m. From here the pool starts behind the comb.
FLOOD_AUTHOR, AMBER_AUTHOR = (2.36, 0.0, 2.90), (BEACON_X, 0.0, 3.35)
REAR_FLOOD_AUTHOR = (-3.60, 0.0, 2.90)


def _game_row(p):
    return [v + 0.0 for v in author_to_game(p)]     # + 0.0: no -0.0 in the manifest


# What the prefabs need at surgery, machine readable (docs/research/groomer-lights-2026-11-01.md). Positions are game local.
LIGHT_SURGERY = {
    "mesh_lod0_single_light": {"purpose": 71, "color": [1, 1, 1, 1], "colorOff": [0, 0, 0, 1], "intensity": 1.0, "luminance": 0, "responseTime": 0,
                               "animationIndex": -1, "materialId": 0},
    "mesh_lod1_lod2": "no EmissiveProperties: the levels use the main mesh's (ManagedBatchSystem.cs:943-946)",
    # Row 0 stays the front flood: purpose 71 follows the first enabled effect, so row 0 must be Night-gated. Rotations are game local: the
    # front one 25 degrees about +x (forward and down), the rear one 180 degrees about +y times that (back and down).
    "effect_source": [
        {"effect": "SkiPisteFloodSource", "cid": "0fff229caa4bf7238884ed882d59fbb1", "m_PositionOffset": _game_row(FLOOD_AUTHOR),
         "m_Rotation_xyzw": [0.21644, 0.0, 0.0, 0.976296], "m_Intensity": 1, "m_ParentMesh": 0, "m_AnimationIndex": -1},
        {"effect": "SkiSnowGunBeaconSource", "cid": "d487a21aa832aaea20c94fb8d04ac3f4", "m_PositionOffset": _game_row(AMBER_AUTHOR),
         "m_Rotation_xyzw": [0.0, 0.0, 0.0, 1.0], "m_Intensity": 1, "m_ParentMesh": 0, "m_AnimationIndex": 0},
        {"effect": "SkiPisteFloodSource", "cid": "0fff229caa4bf7238884ed882d59fbb1", "m_PositionOffset": _game_row(REAR_FLOOD_AUTHOR),
         "m_Rotation_xyzw": [0.0, 0.976296, -0.21644, 0.0], "m_Intensity": 1, "m_ParentMesh": 0, "m_AnimationIndex": -1},
    ],
    "effect_source_curves": "one row: m_AnimationCurves[0] of vanilla EU_Snowplow01.Prefab (0.5 s), for the amber light's pulse",
}


def share_emissive(d):
    """The levels' surfaces get the map through settings.json, the wiki's five-entry LOD1 example. cs2_asset.write_settings shares only the four."""
    path = os.path.join(d, "settings.json")
    with open(path, encoding="utf-8") as fh:
        data = json.load(fh)
    for lod in (1, 2):
        data["sharedAssets"][f"{NAME}_LOD{lod}_Emissive.png"] = f"{NAME}_Emissive.png"
    with open(path, "w", encoding="utf-8") as fh:
        json.dump(data, fh, indent=2)


def export_folder(built):
    d = built["dir"]
    cs2_asset.export_fbx(built["lod0"], os.path.join(d, f"{NAME}.fbx"))
    cs2_asset.export_fbx(built["lod1"], os.path.join(d, f"{NAME}_LOD1.fbx"))
    cs2_asset.export_fbx(built["lod2"], os.path.join(d, f"{NAME}_LOD2.fbx"))
    cs2_asset.write_settings(d, NAME, supplied_lods=(1, 2))
    share_emissive(d)
    data = {
        "generator": "groomer.py", "kind": "prop (moving prop, Transform written by code; also parked)", "letter": "A",
        "tris": {f"LOD{k}": v for k, v in built["tris"].items()}, "lod0_cap": LOD0_CAP, "texture": ATLAS,
        "footprint_author_m": built["footprint"], "track_center_x_author_m": TRACK_CENTER_X,
        "track_center_game_local_z_m": TRACK_CENTER_X, "lights": lights_table(built["b"]),
        "pack": built["pack"], "recolor": "none (ControlMask empty)", "surgery": LIGHT_SURGERY,
        "emissive": {"map": f"{NAME}_Emissive.png", "white_srgb": [1.0, 0.965, 0.92], "amber_srgb": [1.0, 0.698, 0.0], "shared_by": [f"{NAME}_LOD1", f"{NAME}_LOD2"]},
        "axes": "author +X forward, +Y left, +Z up; game local = (-y, z, x)",
    }
    cs2_validate.write_manifest(d, NAME, data)
    findings, summary = cs2_validate.validate_asset_folder_ex(d, NAME, VALIDATE_SPEC)
    for o in (built["lod0"], built["lod1"], built["lod2"]):
        findings += cs2_validate.validate_mesh(o, kind="prop")
    errs = [f for f in findings if f[0] == "ERROR"]
    warns = [f for f in findings if f[0] == "WARN"]
    for f in errs + warns:
        print(f"[groomer] {f[0]} {f[1]}: {f[2] if len(f) > 2 else ''}")
    built["validation"] = {"errors": len(errs), "warnings": len(warns), "density_min": summary.get("density_min"), "tris": summary["tris"]}
    print(f"[groomer] validation: {len(errs)} errors, {len(warns)} warnings, density min {summary.get('density_min')}")
    if errs:
        raise GroomerAuditError(f"validator: {len(errs)} errors, first {errs[0]}")
    return built


# --------------------------------------------------------------------------------------
# Previews (net frame: machine forward = -Y, machine left = +X)
# --------------------------------------------------------------------------------------

VIEWS = (
    ("front34", (1.0, -1.2, 0.45)), ("front34R", (-1.0, -1.2, 0.30)), ("rear34", (1.0, 1.2, 0.45)), ("rear34R", (-1.0, 1.2, 0.30)),
    ("front", (0.0, -1.0, 0.14)), ("rear", (0.0, 1.0, 0.14)), ("sideL", (1.0, 0.0, 0.06)), ("sideR", (-1.0, 0.0, 0.06)),
    ("top", (0.0, -0.001, 1.0)),
)
# (name, target in author coordinates, direction in the net frame, distance m)
CLOSEUPS = (
    ("closeBlade", (3.7, 0.9, 0.6), (0.9, -1.0, 0.42), 4.6),
    ("closeTrack", (0.2, 2.0, 0.5), (1.0, -0.42, 0.30), 4.2),
    ("closeTiller", (-3.6, 0.8, 0.45), (0.8, 1.0, 0.55), 4.6),
    ("closeCab", (1.6, 0.6, 2.0), (1.0, -1.15, 0.12), 4.8),
)


def author_to_net(p):
    return (p[1], -p[0], p[2])


def flood_aim_author(row):
    """The effect's forward (game local +Z) through the row's rotation, in the author frame: game (gx, gy, gz) -> author (gz, -gx, gy)."""
    x, y, z, w = row["m_Rotation_xyzw"]
    v = mathutils.Quaternion((w, x, y, z)) @ Vector((0.0, 0.0, 1.0))
    return Vector((v.z, -v.x, v.y))


def flood_aim_net(row):
    return Vector(author_to_net(tuple(flood_aim_author(row))))


def _fit(cam, obj, direction, res, margin=1.02):
    """Aim at the bounding box center from `direction`, as close as the frame allows."""
    d = Vector(direction).normalized()
    up = Vector((0.0, 0.0, 1.0)) if abs(d.z) < 0.98 else Vector((0.0, 1.0, 0.0))
    r = up.cross(d).normalized()
    u = d.cross(r)
    tv = math.tan(math.radians(22.5))
    th = tv * res[0] / res[1]
    corners = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    ctr = sum(corners, Vector((0.0, 0.0, 0.0))) / 8.0
    dist = 0.0
    for p in corners:
        v = p - ctr
        dist = max(dist, v.dot(d) + abs(v.dot(r)) / th, v.dot(d) + abs(v.dot(u)) / tv)
    P._aim(cam, ctr, direction, dist * margin)


def _figure(rig, x=2.75, y=-1.0):
    """A plain 1.80 m figure for the scale shot: not part of the asset."""
    mat = rig.track_data(bpy.data.materials, bpy.data.materials.new("PreviewFigure"))
    mat.use_nodes = True
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = (0.05, 0.12, 0.45, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.6
    parts = [("cyl", 0.08, 0.84, (x, y - 0.10, 0.42)), ("cyl", 0.08, 0.84, (x, y + 0.10, 0.42)), ("cyl", 0.19, 0.70, (x, y, 1.19)),
             ("cyl", 0.05, 0.62, (x, y - 0.25, 1.22)), ("cyl", 0.05, 0.62, (x, y + 0.25, 1.22)), ("sph", 0.115, 0.0, (x, y, 1.685))]
    for kind, r, depth, loc in parts:
        if kind == "cyl":
            bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=r, depth=depth, location=loc)
        else:
            bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=10, radius=r, location=loc)
        rig.track(bpy.context.active_object).data.materials.append(mat)


def render_previews(built, out_dir, res=(1400, 1000)):
    """The day set with the lights off, as the game has them by day, then the night set."""
    saved = P.EMISSIVE_STRENGTH
    P.EMISSIVE_STRENGTH = 0.0
    try:
        written = render_day(built, out_dir, res)
    finally:
        P.EMISSIVE_STRENGTH = saved
    written += render_night(built, out_dir, res)
    for w in written:
        print(f"[groomer] preview {w}")
    return written


# A preview's stand-ins for the game's night: a dark sky, a moon, and the two effects as Blender lamps with no shadows (the game's have none:
# Game.Effects/LightCullingSystem.cs). Tuned to judge the lights by, not measured; the game's bloom is not here. Standard view, because AgX bleaches
# amber to peach at these strengths and the point is the color on the map; the game has its own tonemapper.
NIGHT_SKY, NIGHT_MOON, NIGHT_EMISSION = 0.006, 0.12, 1.15
NIGHT_SPOT_W, NIGHT_POINT_W = 240.0, 10.0
NIGHT_EMISSION_FAR = P.EMISSIVE_STRENGTH   # a far lamp is under a pixel, so its light is diluted and its color survives; the game's emissive is HDR (kIntensityMultiplier 100)
NIGHT_GROUND_M = 1200.0
NIGHT_FAR_LOD2_M = 250.0
NIGHT_VIEW = "Standard"
NIGHT_CITY_DIR = (1.0, -1.25, 1.1)      # about 35 degrees down: the city camera, where the bar's top shows


def _night_rig(rig, obj, asset_dir, label):
    cam = P._make_camera(rig)
    world, moon = P._make_world(rig, elevation_deg=38.0, sun_energy=NIGHT_MOON)
    world.node_tree.nodes["Background"].inputs[1].default_value = NIGHT_SKY
    moon.data.color = (0.62, 0.72, 1.0)
    P._make_ground(rig)
    P._assign(obj, P._pbr_material(rig, asset_dir, NAME, None, mat_name=f"{NAME}_Night{label}"))
    for row in LIGHT_SURGERY["effect_source"]:
        kind = "SPOT" if row["effect"] == "SkiPisteFloodSource" else "POINT"
        gx, gy, gz = row["m_PositionOffset"]
        bpy.ops.object.light_add(type=kind, location=author_to_net((gz, -gx, gy)))
        lamp_ob = rig.track(bpy.context.active_object)
        lamp_ob.data.use_shadow = False
        if kind == "SPOT":
            lamp_ob.data.energy = NIGHT_SPOT_W
            lamp_ob.data.spot_size = math.radians(110.0)
            lamp_ob.data.spot_blend = 0.25
            lamp_ob.data.color = (1.0, 0.93, 0.83)
            lamp_ob.rotation_euler = flood_aim_net(row).to_track_quat("-Z", "Y").to_euler()
        else:
            lamp_ob.data.energy = NIGHT_POINT_W
            lamp_ob.data.color = (1.0, 0.40, 0.012)
            lamp_ob.data.shadow_soft_size = 0.06
    return cam


def _night_far(obj, asset_dir, path, dist, label, direction=None):
    """[full frame | 4x nearest crop of the center], the pixels the machine gets at that distance."""
    rig = P._Rig(obj, None)
    try:
        cam = _night_rig(rig, obj, asset_dir, f"Far{label}")
        rig.scene.render.resolution_x, rig.scene.render.resolution_y = 1000, 700
        bb = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
        ctr = sum(bb, Vector((0.0, 0.0, 0.0))) / 8.0
        P._aim(cam, ctr, snow_props.FAR_DIR if direction is None else direction, dist)
        tmp = path[:-4] + "_tmp.png"
        P._render(rig, tmp, engine="EEVEE", view_transform=NIGHT_VIEW)
        img = P._load_rgba(tmp)
        h, w = img.shape[:2]
        crop = img[h // 2 - 87:h // 2 + 88, w // 2 - 125:w // 2 + 125]
        P._write_rgba(path, P._hstack([img, np.repeat(np.repeat(crop, 4, axis=0), 4, axis=1)[:h]]))
        os.remove(tmp)
    finally:
        rig.restore()
    return path


def render_night(built, out_dir, res=(1400, 1000)):
    os.makedirs(out_dir, exist_ok=True)
    d = built["dir"]
    written = []
    saved = P.EMISSIVE_STRENGTH, P.GROUND_SIZE_M
    P.EMISSIVE_STRENGTH, P.GROUND_SIZE_M = NIGHT_EMISSION, NIGHT_GROUND_M
    try:
        for level, o, views, size in ((0, built["lod0"], (("front34", (1.0, -1.2, 0.45)), ("rear34", (1.0, 1.2, 0.45))), res),
                                      (1, built["lod1"], (("LOD1_front34", (1.0, -1.2, 0.45)),), (1000, 700)),
                                      (2, built["lod2"], (("LOD2_front34", (1.0, -1.2, 0.45)),), (1000, 700))):
            rig = P._Rig(o, None)
            try:
                cam = _night_rig(rig, o, d, f"L{level}")
                rig.scene.render.resolution_x, rig.scene.render.resolution_y = size
                for vn, direction in views:
                    _fit(cam, o, direction, size)
                    cam.location = cam.location + (cam.location - Vector((0.0, 0.0, 1.3))).normalized() * 3.0     # a little snow around it, for the pool of light
                    path = os.path.join(out_dir, f"{NAME}_night_{vn}.png")
                    P._render(rig, path, engine="EEVEE", view_transform=NIGHT_VIEW)
                    written.append(path)
            finally:
                rig.restore()
        table = P.lod_switch_table(built["lod0"].dimensions, [built["tris"][k] for k in (0, 1, 2)], area=None)
        level = 0
        for k, dist in sorted(table["switch"].items()):
            if 150.0 >= dist:
                level = k
        far_obj = {0: built["lod0"], 1: built["lod1"], 2: built["lod2"]}[level]
        P.EMISSIVE_STRENGTH = NIGHT_EMISSION_FAR
        written.append(_night_far(far_obj, d, os.path.join(out_dir, f"{NAME}_night_far150m_LOD{level}.png"), 150.0, f"{level}"))
        written.append(_night_far(built["lod2"], d, os.path.join(out_dir, f"{NAME}_night_far{NIGHT_FAR_LOD2_M:.0f}m_LOD2.png"), NIGHT_FAR_LOD2_M, "2", NIGHT_CITY_DIR))
    finally:
        P.EMISSIVE_STRENGTH, P.GROUND_SIZE_M = saved
    return written


def render_day(built, out_dir, res=(1400, 1000)):
    os.makedirs(out_dir, exist_ok=True)
    obj = built["lod0"]
    d = built["dir"]
    written = []
    rig = P._Rig(obj, None)
    try:
        cam = P._make_camera(rig)
        P._make_world(rig, **P.NOON)
        P._make_ground(rig)
        P._assign(obj, P._pbr_material(rig, d, NAME, None, mat_name=f"{NAME}_Prev"))
        sc = rig.scene
        sc.render.resolution_x, sc.render.resolution_y = res
        for vn, direction in VIEWS:
            _fit(cam, obj, direction, res)
            path = os.path.join(out_dir, f"{NAME}_{vn}.png")
            P._render(rig, path, engine="EEVEE")
            written.append(path)
        for vn, tgt, direction, dist in CLOSEUPS:
            P._aim(cam, author_to_net(tgt), direction, dist)
            path = os.path.join(out_dir, f"{NAME}_{vn}.png")
            P._render(rig, path, engine="EEVEE")
            written.append(path)
        _figure(rig)
        P._aim(cam, (1.8, -0.2, 1.3), (1.0, -0.38, 0.12), 11.5)
        path = os.path.join(out_dir, f"{NAME}_scale.png")
        P._render(rig, path, engine="EEVEE")
        written.append(path)
    finally:
        rig.restore()

    # the far shot uses the level the LOD table picks at 150 m
    table = P.lod_switch_table(obj.dimensions, [built["tris"][k] for k in (0, 1, 2)], area=None)
    level = 0
    for k, dist in sorted(table["switch"].items()):
        if 150.0 >= dist:
            level = k
    far_obj = {0: obj, 1: built["lod1"], 2: built["lod2"]}[level]
    print(f"[groomer] LOD table: {table['switch']}; 150 m draws LOD{level}")
    built["far_level"] = level
    built["lod_table"] = {k: round(v, 1) for k, v in table["switch"].items()}
    path = os.path.join(out_dir, f"{NAME}_far150m_LOD{level}.png")
    snow_props._far_shot(far_obj, d, NAME, path, None, f"LOD{level}")
    written.append(path)
    # every LOD from the same angle, for the chain
    for level_k, o in ((1, built["lod1"]), (2, built["lod2"])):
        rig = P._Rig(o, None)
        try:
            cam = P._make_camera(rig)
            P._make_world(rig, **P.NOON)
            P._make_ground(rig)
            P._assign(o, P._pbr_material(rig, d, NAME, None, mat_name=f"{NAME}_PrevL{level_k}"))
            rig.scene.render.resolution_x, rig.scene.render.resolution_y = 1000, 700
            for vn, direction in (("front34", (1.0, -1.2, 0.45)), ("rear34", (1.0, 1.2, 0.45)), ("top", (0.0, -0.001, 1.0))):
                _fit(cam, o, direction, (1000, 700))
                path = os.path.join(out_dir, f"{NAME}_LOD{level_k}_{vn}.png")
                P._render(rig, path, engine="EEVEE")
                written.append(path)
        finally:
            rig.restore()
    return written


def write_review(built, args_list, out_dir=None):
    meta = {"generator": "groomer.py", "args": args_list, "kind": "prop", "letter": "A", "caps": {0: LOD0_CAP, 2: LOD2_CAP}}
    path = cs2_review.write_pack(NAME, built["dir"], {0: built["lod0"], 1: built["lod1"], 2: built["lod2"]}, manifest={}, meta=meta, out_dir=out_dir)
    print(f"[groomer] review pack {path}")
    return path


def clear_scene():
    snow_props.clear_scene()


def parse_args(argv):
    ap = argparse.ArgumentParser(prog="groomer.py", description="SkiGroomerA, a generic snow groomer")
    ap.add_argument("--out", required=True, help="letter folder; the asset lands in <out>/SkiGroomerA (never assets/import without a verdict)")
    ap.add_argument("--export", action="store_true", help="write FBX, settings, manifest and run the validator")
    ap.add_argument("--preview", default=None, help="folder for the preview renders")
    ap.add_argument("--review", action="store_true", help="also write the review pack (assets/preview/review)")
    ap.add_argument("--review-dir", default=None, help="review pack folder override")
    return ap.parse_args(argv)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    args = parse_args(argv)
    clear_scene()
    asset_dir = os.path.join(os.path.abspath(args.out), NAME)
    if os.path.isdir(asset_dir):
        shutil.rmtree(asset_dir)
    built = build(asset_dir, export=args.export)
    print("[groomer] lights (author xyz, game local xyz):")
    print(json.dumps(lights_table(built["b"]), indent=1))
    if args.preview:
        render_previews(built, os.path.abspath(args.preview))
    if args.review:
        write_review(built, ["--export", "--review"], out_dir=args.review_dir)
    print("[groomer] done")


if __name__ == "__main__":
    main()
