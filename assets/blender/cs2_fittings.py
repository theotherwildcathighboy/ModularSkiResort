"""Fittings v1 (2026-10-17, docs/research/art/fittings-2026-10-17.md): the small items vanilla buildings and
Richard's reference photos have and the eight buildings lacked. `lodge.py --fittings v1` (with --door-kit v2).

What lives where:
  * cs2_bldg (inside `cs2_bldg.fittings()`): the downspout SHOE (every downspout() call unless shoe=False), the
    hidden bracket tops of snow_guard(), and the builders snow_pads() / chimney_shroud().
  * the recipes (gated on `cs2_bldg.FITTINGS["version"] >= 1`): recipe-local modelled fittings (L5 downpipes and
    snow fences, L3 snow pads, L2's no-shoe drain pipes).
  * this module, build(): recipe-independent additions read off the built parts: the chimney shrouds (from the
    `chimcap_<name>` crown bounds) and the VANILLA SUB-OBJECT ROWS (vanilla props first: entry wall lights, canopy
    downlights, plumbing vents, kitchen exhaust flues), each placed by a ray cast against the parts and checked.

Frames. Parts are built in the authoring frame; lodge.run then applies yaw and orient_for_net, so the exported
(FINAL) frame = Rz(yaw - 90) . authoring. The tables below are written in the FINAL frame (the frame of the
previews, the door-kit door groups and docs/research/art/fittings-2026-10-17.data/results/dry.json). A design row
(lodge.sub_objects_block applies rule T to it) is (x_f, z_f, -y_f); a prop facing the final horizontal normal
(nx, ny) gets the yaw q = (0, sin(t/2), 0, cos(t/2)) with sin t = nx, cos t = -ny (checked against the shipped
rows: school wingL lamp identity on a -y wall, L3 Bn lamp game q (0, -0.707, 0, 0.707) on a +x wall).

Gates (all raise): a wall prop must hit a non-glass wall face within 1.5 m whose normal is within 25 deg of the
asked normal; a ceiling prop must hit a downward face within 4 m; a roof prop's four footprint corners and centre
must all land on roof_ / ridge_ faces, its base (origin - sink) must be under every corner, at least 60 % of its
height must stand above the roof at the centre, and its above-roof box must not meet another part's box (a
chimney, a vent stack, a dormer, a snow rail)."""
import json
import math
import os

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

import cs2_bldg as K

_HERE = os.path.dirname(os.path.abspath(__file__))
VERSION = 1

# vanilla props (dump-verified: subobj_bounds.json holds each GUID with its union bounds, game local, y up)
WALL_LIGHT = "WallLightShortRangeRandom01"          # the project's entry-lamp prop (H4); 3 bulkhead variants
ROOF_LIGHT = "RoofLightRandom01"                    # ceiling-mounted downlight (hangs 0.15 below its origin)
VENT = "ACVentRoofRandom01"                         # small roof vents: capped pipes, mushroom cap, turbine
KITCHEN = "CommercialACVentRoofRandom01"            # commercial exhaust stacks / fans (1.5-2.0 m)

# The tables (FINAL frame). wall: (prefab, (x, y, z) near the wall face, (nx, ny) outward normal, note);
# ceiling: (prefab, (x, y, z_from) cast up, note); roof: (prefab, (x, y), note).
ROWS = {
    "school": {
        "roof": ((VENT, (10.5, -5.5), "plumbing vent, upper roof (washrooms)"),
                 (VENT, (-5.0, -5.5), "plumbing vent, upper roof")),
    },
    "L1": {
        "roof": ((KITCHEN, (-12.0, -4.5), "kitchen exhaust on the ridge (A-Basin A-frame / 6th Alley: capped "
                                          "vents near the ridge)"),),
    },
    "L2": {
        "roof": ((VENT, (3.0, 2.5), "plumbing vent, east slope (the stovepipe is on the west)"),),
    },
    "L3": {
        "wall": ((WALL_LIGHT, (-2.17, 13.48, 2.22), (0.0, 1.0), "podW door: the one kit door without a lamp"),),
        "roof": ((KITCHEN, (-9.0, 9.0), "restaurant kitchen exhaust, wing A rear slope (Courchevel is a "
                                        "restaurant)"),
                 (VENT, (0.0, -3.5), "plumbing vent, B back slope"),
                 (VENT, (-13.0, -4.5), "plumbing vent, B back slope")),
    },
    "L4": {
        "ceiling": ((ROOF_LIGHT, (-3.6, -14.5, 2.4), "entry porch ceiling downlight (glazing entry pair)"),
                    (ROOF_LIGHT, (-1.4, -14.5, 2.4), "entry porch ceiling downlight")),
        "roof": ((KITCHEN, (-5.0, 9.0), "kitchen exhaust on the dining wing's low roof"),
                 (VENT, (8.5, -4.0), "plumbing vent, main rear slope (east of dormer u3)"),
                 (VENT, (-6.0, -4.0), "plumbing vent, main rear slope (east of dormer u2)")),
    },
    "L5": {
        "wall": ((WALL_LIGHT, (17.85, -19.1, 2.34), (0.0, -1.0), "clock-tower door ctxp"),
                 (WALL_LIGHT, (13.9, -15.15, 2.34), (-1.0, 0.0), "clock-tower door ctyn"),
                 (WALL_LIGHT, (17.85, -19.1, 6.71), (0.0, -1.0), "clock-tower deck door ctdk")),
        "roof": ((KITCHEN, (-6.0, -3.0), "kitchen exhaust on the M ridge (west of the hall)"),
                 (KITCHEN, (6.0, -3.0), "kitchen exhaust on the M ridge (east of the hall)"),
                 (VENT, (-4.0, 1.5), "plumbing vent, M courtyard slope (between dormers d4 / d5)"),
                 (VENT, (4.0, 1.5), "plumbing vent, M courtyard slope (between dormers d5 / d6)"),
                 (VENT, (26.0, 1.5), "plumbing vent, north wing slope")),
    },
}
# chimney shrouds over the crown slab of chimneys that have no flue pots (L4 / L5's M stacks have pots; L5's
# other stacks get none: L5 has no triangles for them). L1's north stack is the building's top (crown 20.26): a
# shroud raises the game extent 0.56 m, and the extent is a contract of this round, so L1 keeps its crown.
SHROUDS = {"L3": ("B",)}

ROOF_PREFIXES = ("roof_", "ridge_")
GLASS_PREFIXES = ("win_", "wall_ghgl", "glass")


def _bounds(prefab):
    with open(os.path.join(_HERE, "subobj_bounds.json"), encoding="utf-8") as fh:
        props = json.load(fh)["props"]
    hits = [v for v in props.values() if v.get("prefab") == prefab]
    if len(hits) != 1 or not hits[0].get("guid"):
        raise RuntimeError(f"fittings: {prefab} is not one dump-verified entry of subobj_bounds.json")
    return hits[0]["guid"], hits[0]["bounds"]


def _phi(recipe):
    return math.radians(float(recipe.yaw_deg) - 90.0)


def final_to_auth(recipe, p):
    """FINAL (x, y[, z]) -> authoring: the inverse rotation of Rz(yaw - 90)."""
    a = -_phi(recipe)
    x, y = p[0], p[1]
    out = (x * math.cos(a) - y * math.sin(a), x * math.sin(a) + y * math.cos(a))
    return out + tuple(p[2:])


def auth_to_final(recipe, p):
    a = _phi(recipe)
    x, y = p[0], p[1]
    return (x * math.cos(a) - y * math.sin(a), x * math.sin(a) + y * math.cos(a)) + tuple(p[2:])


def design_row(prefab, guid, pf, normal=None):
    """(prefab, guid, design position, q, parent) from a FINAL-frame point (+ outward normal for wall props)."""
    q = (0.0, 0.0, 0.0, 1.0)
    if normal is not None:
        t = math.atan2(normal[0], -normal[1])
        q = (0.0, round(math.sin(t / 2), 7) + 0.0, 0.0, round(math.cos(t / 2), 7) + 0.0)
    return (prefab, guid, (round(pf[0], 4) + 0.0, round(pf[2], 4) + 0.0, round(-pf[1], 4) + 0.0), q, 0)


class _Scene:
    """One BVH over every part (authoring frame), with the part name of each polygon."""

    def __init__(self, parts):
        verts, polys, self.names, self.boxes = [], [], [], {}
        for o, _m in parts:
            if o.type != "MESH":
                continue
            M = o.matrix_world
            base = len(verts)
            co = [M @ v.co for v in o.data.vertices]
            verts += [tuple(c) for c in co]
            for p in o.data.polygons:
                polys.append([base + i for i in p.vertices])
                self.names.append(o.name)
            if co:
                lo = Vector((min(c.x for c in co), min(c.y for c in co), min(c.z for c in co)))
                hi = Vector((max(c.x for c in co), max(c.y for c in co), max(c.z for c in co)))
                self.boxes[o.name] = (lo, hi)
        self.bvh = BVHTree.FromPolygons(verts, polys, all_triangles=False)

    def box_hits(self, lo, hi, skip=()):
        """Parts (not starting with `skip`) whose triangles cross the box, or that fit inside it whole."""
        c = [(x, y, z) for x in (lo.x, hi.x) for y in (lo.y, hi.y) for z in (lo.z, hi.z)]
        f = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
        box = BVHTree.FromPolygons(c, f, all_triangles=False)
        names = {self.names[i] for i, _j in self.bvh.overlap(box)}
        for name, (plo, phi) in self.boxes.items():
            if all(lo[i] <= plo[i] and phi[i] <= hi[i] for i in range(3)):
                names.add(name)
        return sorted(n for n in names if not n.startswith(skip))

    def inside(self, p, reach=2.0):
        """True when 3+ of 4 horizontal rays from p hit a face from behind (p inside a closed volume)."""
        back = 0
        for d in ((1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0)):
            h = self.cast(p, d, reach)
            if h is not None and h[1].dot(Vector(d)) > 0.0:
                back += 1
        return back >= 3

    def cast(self, org, d, dist):
        loc, nrm, idx, t = self.bvh.ray_cast(Vector(org), Vector(d).normalized(), dist)
        if loc is None:
            return None
        return loc, nrm, self.names[idx], t


def _place_wall(sc, recipe, prefab, pf, n, note):
    na = Vector(final_to_auth(recipe, (n[0], n[1])) + (0.0,)).normalized()
    pa = Vector(final_to_auth(recipe, pf))
    hit = sc.cast(pa + na * 1.5, -na, 3.0)
    if hit is None:
        raise RuntimeError(f"fittings {recipe.key} {prefab} ({note}): no wall within 1.5 m of {pf}")
    loc, nrm, part, _t = hit
    if part.startswith(GLASS_PREFIXES):
        raise RuntimeError(f"fittings {recipe.key} {prefab} ({note}): the wall at {pf} is glass ({part})")
    if abs((loc - pa).dot(na)) > 1.5 or nrm.dot(na) < math.cos(math.radians(25.0)):
        raise RuntimeError(f"fittings {recipe.key} {prefab} ({note}): wall hit {part} normal {tuple(nrm)}")
    lf = auth_to_final(recipe, tuple(loc))
    return lf, part


def _place_ceiling(sc, recipe, prefab, pf, note):
    pa = Vector(final_to_auth(recipe, pf))
    hit = sc.cast(pa, (0, 0, 1), 4.0)
    if hit is None or hit[1].z > -0.9:
        raise RuntimeError(f"fittings {recipe.key} {prefab} ({note}): no ceiling over {pf}: {hit}")
    loc, _n, part, _t = hit
    return auth_to_final(recipe, tuple(loc)), part


def _place_roof(sc, recipe, prefab, bounds, pf, note):
    """Origin z = min(roof z at the 4 footprint corners) + sink - 0.02, so the base is under every corner."""
    lo, hi = bounds
    sink, height = -float(lo[1]), float(hi[1])
    hx, hz = max(abs(lo[0]), abs(hi[0])), max(abs(lo[2]), abs(hi[2]))
    zs, parts = [], set()
    pts = [(0.0, 0.0)] + [(sx * hx, sz * hz) for sx in (-1, 1) for sz in (-1, 1)]
    for gx, gz in pts:
        # game local x / z of a yaw-0 prop = FINAL (-x, -y) offsets (design x = x_f, design z = -y_f; rule T
        # negates x): the footprint is symmetric, so the signs only pick the corner
        q = (pf[0] + gx, pf[1] + gz, 200.0)
        hit = sc.cast(final_to_auth(recipe, q), (0, 0, -1), 400.0)
        if hit is None or not hit[2].startswith(ROOF_PREFIXES):
            raise RuntimeError(f"fittings {recipe.key} {prefab} ({note}): footprint point {q[:2]} lands on "
                               f"{hit[2] if hit else 'nothing'}, not a roof")
        zs.append(hit[0].z)
        parts.add(hit[2])
    z0 = min(zs[1:]) + sink - 0.02
    above = (z0 + height) - zs[0]
    if above < 0.6 * (sink + height):
        raise RuntimeError(f"fittings {recipe.key} {prefab} ({note}): only {above:.2f} m of "
                           f"{sink + height:.2f} m stands above the roof (pitch too steep for this prop)")
    # the above-roof box vs every other part: triangle overlap (BVH), plus whole small parts inside the box
    ca = Vector(final_to_auth(recipe, (pf[0], pf[1], 0.0)))
    r = max(hx, hz)
    blo = Vector((ca.x - r, ca.y - r, zs[0] + 0.05))
    bhi = Vector((ca.x + r, ca.y + r, z0 + height))
    hit = sc.box_hits(blo, bhi, skip=ROOF_PREFIXES + ("soffit_", "fascia_", "rake_", "gable_"))
    if hit:
        raise RuntimeError(f"fittings {recipe.key} {prefab} ({note}): meets part(s) {hit[:4]}")
    return (pf[0], pf[1], z0), sorted(parts), round(above, 3)


def build(recipe, parts, opts):
    """(new parts, design rows, manifest info) for recipe.key. Pure function of the parts and the tables."""
    key = recipe.key
    spec = ROWS.get(key, {})
    sc = _Scene(parts)
    rows, info = [], []
    for prefab, pf, n, note in spec.get("wall", ()):
        guid, _b = _bounds(prefab)
        lf, part = _place_wall(sc, recipe, prefab, pf, n, note)
        rows.append(design_row(prefab, guid, lf, n))
        info.append({"kind": "wall", "prefab": prefab, "final": [round(v, 3) for v in lf], "on": part,
                     "note": note})
    for prefab, pf, note in spec.get("ceiling", ()):
        guid, _b = _bounds(prefab)
        lf, part = _place_ceiling(sc, recipe, prefab, pf, note)
        rows.append(design_row(prefab, guid, lf))
        info.append({"kind": "ceiling", "prefab": prefab, "final": [round(v, 3) for v in lf], "on": part,
                     "note": note})
    for prefab, pf, note in spec.get("roof", ()):
        guid, b = _bounds(prefab)
        lf, on, above = _place_roof(sc, recipe, prefab, b, pf, note)
        rows.append(design_row(prefab, guid, lf))
        info.append({"kind": "roof", "prefab": prefab, "final": [round(v, 3) for v in lf], "on": on,
                     "above_m": above, "note": note})
    # shoe gate: every shoe outlet (built inside cs2_bldg.fittings) stands outside the building
    shoes = list(K.FIT_SHOES) if opts.get("fittings") else []
    for nm, p_out, d in shoes:
        if sc.inside(Vector(p_out) + Vector(d) * 0.02):
            raise RuntimeError(f"fittings {key}: the shoe of {nm} kicks into the building at {p_out}")
    info.append({"kind": "shoes", "n": len(shoes), "parts": sorted(nm for nm, _p, _d in shoes)})
    new = []
    by = {o.name: o for o, _m in parts}
    for nm in SHROUDS.get(key, ()):
        o = by.get(f"chimcap_{nm}")
        if o is None:
            raise RuntimeError(f"fittings {key}: no chimcap_{nm} for the shroud")
        co = [o.matrix_world @ v.co for v in o.data.vertices]
        lo = tuple(min(c[i] for c in co) for i in range(3))
        hi = tuple(max(c[i] for c in co) for i in range(3))
        new += K.chimney_shroud(nm, lo, hi)
        info.append({"kind": "shroud", "on": f"chimcap_{nm}", "part": f"fit_shroud{nm}"})
    return new, rows, {"version": VERSION, "rows": len(rows), "items": info,
                       "shoes": bool(opts.get("fittings"))}
