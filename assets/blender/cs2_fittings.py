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
must all land on roof_ / ridge_ faces, it is seated so that EVERY variant's base rings touch or sink into the roof
(close look: fittings_vanilla_bases.json), every variant must show >= 60 % of its body above the roof at the
centre, and its above-roof box must not meet another part's box (a chimney, a vent stack, a dormer, a snow rail).

Close look (2026-10-17, docs/research/art/fittings-2026-10-17.md section 11): build() first runs the FIX pass
(closelook: refit_gutters, route_downspouts, clip_runs) and last the CONTACT + INTERSECTION audit (audit), which
raises on any violation: nothing floats, nothing passes through what it should not (PIPELINE.md section 1)."""
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
    # L1: v1's kitchen exhaust on the A-frame ridge is gone (close look, 2026-10-17): seated so that no variant
    # shows air under its base (the gate below), CommercialACVentRoof04 kept only 0.32 of its 0.54 m above the
    # 60-degree ridge, and the five modelled ridge vent stacks already carry the A-Basin read
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
        # close look (2026-10-17): v1's two kitchen exhausts on the 45-degree M ridge are gone: seated with no air
        # under any variant's base, CommercialACVentRoof04 kept 0.21 of its 0.54 m above the ridge
        "roof": ((VENT, (-4.0, 1.5), "plumbing vent, M courtyard slope (between dormers d4 / d5)"),
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
    # close look (2026-10-17): seat it so that EVERY variant's base rings (its skirt bottom, a flashing plate) touch
    # or sink into the roof at every ring point (v1 sank the union bounds' 0.33 m below the lowest corner, which
    # left ACVentRoof04's plate 24-30 cm and CommercialACVentRoof02's 37 cm over the roof); then every variant must
    # still show >= 60 % of its body (top minus its highest base ring) above the roof at the centre
    variants = _vanilla_bases().get(prefab, {})
    if not variants:
        raise RuntimeError(f"fittings: no variant bases for {prefab} in fittings_vanilla_bases.json")
    cands = []
    for _vn, v in sorted(variants.items()):
        for y, ring in v["rings"]:
            for gx, gz in ring:
                # identity row: game local (x, y, z) -> FINAL offset (-x, -z, y)
                q = (pf[0] - gx, pf[1] - gz, 200.0)
                hit = sc.cast(final_to_auth(recipe, q), (0, 0, -1), 400.0)
                if hit is None or not hit[2].startswith(ROOF_PREFIXES):
                    raise RuntimeError(f"fittings {recipe.key} {prefab} ({note}): base point {q[:2]} lands on "
                                       f"{hit[2] if hit else 'nothing'}, not a roof")
                cands.append(hit[0].z - y - 0.003)
    z0 = min(cands)
    above = 9.0
    for vn, v in sorted(variants.items()):
        top, vis0 = float(v["hi"][1]), max(0.0, max(y for y, _r in v["rings"]))
        a = (z0 + top) - zs[0]
        above = min(above, a)
        if a < 0.6 * (top - vis0):
            raise RuntimeError(f"fittings {recipe.key} {prefab} ({note}): {vn} shows only {a:.2f} m of "
                               f"{top - vis0:.2f} m above the roof (pitch too steep for this prop)")
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
    sfix = structure_fix(recipe, parts) if opts.get("structure") else None    # T58 (2026-10-18): snap pass
    fix = closelook(recipe, parts, opts)          # close look (2026-10-17): gutters, downpipes, clips
    sc = _Scene(parts)
    rows, info, placed = [], [], []
    for prefab, pf, n, note in spec.get("wall", ()):
        guid, _b = _bounds(prefab)
        lf, part = _place_wall(sc, recipe, prefab, pf, n, note)
        rows.append(design_row(prefab, guid, lf, n))
        placed.append(("wall", prefab, lf, rows[-1]))
        info.append({"kind": "wall", "prefab": prefab, "final": [round(v, 3) for v in lf], "on": part,
                     "note": note})
    for prefab, pf, note in spec.get("ceiling", ()):
        guid, _b = _bounds(prefab)
        lf, part = _place_ceiling(sc, recipe, prefab, pf, note)
        rows.append(design_row(prefab, guid, lf))
        placed.append(("ceiling", prefab, lf, rows[-1]))
        info.append({"kind": "ceiling", "prefab": prefab, "final": [round(v, 3) for v in lf], "on": part,
                     "note": note})
    for prefab, pf, note in spec.get("roof", ()):
        guid, b = _bounds(prefab)
        lf, on, above = _place_roof(sc, recipe, prefab, b, pf, note)
        rows.append(design_row(prefab, guid, lf))
        placed.append(("roof", prefab, lf, rows[-1]))
        info.append({"kind": "roof", "prefab": prefab, "final": [round(v, 3) for v in lf], "on": on,
                     "above_m": above, "note": note})
    # shoe gate: every shoe outlet (built inside cs2_bldg.fittings) stands outside the building
    shoes = list(K.FIT_SHOES) if opts.get("fittings") else []
    shoe_bad = []
    for nm, p_out, d in shoes:
        if sc.inside(Vector(p_out) + Vector(d) * 0.02):
            if AUDIT_MODE != "report":
                raise RuntimeError(f"fittings {key}: the shoe of {nm} kicks into the building at {p_out}")
            shoe_bad.append(("shoe", nm, 0.0, f"kicks into the building at {[round(v, 2) for v in p_out]}"))
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
    # close look (2026-10-17): the contact + intersection audit of every fitting part and vanilla row (gate)
    a = audit(recipe, list(parts) + list(new), placed, sc)
    a["violations"] = shoe_bad + a["violations"]
    LAST_AUDIT.clear()
    LAST_AUDIT.update(a)
    info.append({"kind": "closelook", "gutters": fix["gutters"], "pipes": fix["pipes"]})
    info.append({"kind": "audit", "shells": a["n_shells"], "rows": len(a["rows"]),
                 "violations": len(a["violations"])})
    if a["violations"] and AUDIT_MODE == "raise":
        lines = "; ".join(f"{r} {nm} {v} mm: {d}" for r, nm, v, d in a["violations"][:12])
        raise RuntimeError(f"fittings {key}: close-look audit, {len(a['violations'])} violation(s): {lines}")
    if opts.get("structure"):                     # T59: a recipe's structure-v1 sub-object rows (L5 fire pits)
        # the builder's own module globals (bldg_recipes loads recipes/*.py by file, outside sys.modules)
        extra = tuple(getattr(getattr(recipe, "builder", None), "__globals__", {}).get("STRUCT_SUB_OBJECTS", ()))
        rows = list(rows) + list(extra)
        if extra:
            info.append({"kind": "struct_rows", "n": len(extra), "prefabs": sorted({r[0] for r in extra})})
    out = {"version": VERSION, "rows": len(rows), "items": info, "shoes": bool(opts.get("fittings"))}
    if opts.get("structure"):                     # T58: the structure gate (raises in STRUCT_AUDIT_MODE "raise")
        st = audit_structure(recipe, list(parts) + list(new))
        gl = audit_glass(recipe, list(parts) + list(new))     # round 2: nothing crosses glass (raises)
        out["structure"] = {"version": STRUCT_VERSION, "parts": st["parts"], "shells": st["n_shells"],
                            "roles": st["roles"], "violations": len(st["violations"]),
                            "snaps": [m for m in sfix if m["fix"] != "none"],
                            "unsnapped": len([m for m in sfix if m["fix"] == "none"]),
                            "glass": {"panes": gl["panes"], "shells": gl["shells"],
                                      "violations": len(gl["violations"]),
                                      "declared": sorted({d[0].rsplit("#", 1)[0] for d in gl["declared"]})}}
    return new, rows, out


# =====================================================================================================================
# Close look (2026-10-17, Richard on the v1 previews: "the gutters are too blocky and it looks like some of the
# fittings are floating like a chimney, or go through other objects they shouldn't"): the CONTACT + INTERSECTION
# audit of every fitting part, run by build() on every fittings build. It RAISES (AUDIT_MODE "raise", the default)
# on a violation; AUDIT_MODE "report" only records it (the before-table of docs/research/art/fittings-2026-10-17.md
# section 11). PIPELINE.md section 1 idiom: "nothing floats: every fitting proves contact".
#
# Units: metres, authoring frame (before yaw); reports carry FINAL-frame centres (auth_to_final) for the doc.
# Shells: each fitting part is split into its connected shells (a downspout = pipe + elbow + shoe; a bracket part
# = one shell per bracket; a pad part = one shell per pad) and every shell is audited.
#   contact   every shell reaches the building (non-fitting geometry, or grade: the lifted grade plane GROUND_LIFT
#             above a ground level) through a chain of contacts <= CONTACT_M (a pad on the slab; a rail on its
#             brackets on the slab; a hood plate on its legs on the crown on the stack);
#   gutter    a gutter run touches the building (fascia / rafter / wall) within CONTACT_M at every 0.5 m station;
#   pipe      a vertical downpipe run is held (wall contact, a clip, the elbow, the shoe) at least every PIPE_HOLD_M;
#   chimney   every point of a stack's / shoulder's base outline is buried (enclosed by the building or by
#             another stack) or within CONTACT_M of the surface under it: no air under any corner;
#   intersect a shell crosses a non-fitting part only as a DECLARED embed (EMBEDS: depth cap, or a "through"
#             support such as a chimney through its roof) and another fitting only as a declared joint (JOINTS);
#   rows      every vanilla row of this build: the back plate of a wall light and the top plate of a downlight lie
#             on their surface within CONTACT_M at every outline point; a roof prop's base rings (the skirt
#             bottom, a flashing plate) of EVERY variant (placeholders pick one per instance) have no air
#             under any point (fittings_vanilla_bases.json, read from the game's meshes).
# =====================================================================================================================
import numpy as np                                      # noqa: E402

AUDIT_MODE = "raise"            # "raise" | "report"
LAST_AUDIT = {}                 # the last build's audit (report mode reads it)
LAST_FIX = {}                   # the last build's close-look fix report
CONTACT_M = 0.005
EMBED_M = 0.015
PIPE_HOLD_M = 2.0
STATION_M = 0.5
GUTTER_SPAN_M = 1.0             # a gutter run may bridge at most 1 m off its fascia (hangers every <= 1 m)
SAMPLE_M = 0.10
FIT_PREFIXES = ("gutter_", "downspout_", "snowguard_", "chim_", "chimcap_", "fit_", "vent_", "stovepipe_", "flue_")
# declared embeds of a fitting into a NON-fitting part: (fitting prefix, part prefixes, max depth m | None = through,
# reason). Anything else that crosses is a violation; a listed pair deeper than its cap is one too.
EMBEDS = (
    ("chim_", ("roof_", "ridge_", "rake_"), None, "a chimney stack passes through its roof"),
    ("vent_", ("roof_", "ridge_"), None, "a vent stack passes through its roof"),
    ("stovepipe_", ("roof_", "ridge_"), None, "a stovepipe passes through its roof"),
    ("gutter_", ("fascia_", "rake_", "roof_"), EMBED_M, "the gutter's back rim seats on the fascia"),
    ("snowguard_", ("roof_",), EMBED_M, "rail brackets / pads seat on the slab"),
    ("downspout_", ("wall_", "col_", "pier_", "plinth_", "plinthcap_", "gable_", "trim_", "stone_", "log_",
                    "siding_", "chim"), EMBED_M, "a pipe clip / the pipe's back seats on the wall"),
    ("downspout_", ("fascia_", "soffit_"), EMBED_M, "the outlet seats in a fascia gutter (the school's drip edge)"),
    ("downspout_", ("deck_",), EMBED_M, "a pipe with no room for a shoe runs into a deck drain"),
    ("chim_", ("wall_", "gable_", "trim_", "plinth_", "plinthcap_"), None,
     "a chimney built into its wall: the wall, its belt and its plinth cap die into the stack"),
)
# declared joints between two fittings (prefix a, prefix b, reason); "same: ..." = only the same chimney / rail /
# pipe name (a bracket into ITS rail, not a neighbour's)
JOINTS = (
    ("downspout_", "gutter_", "outlet: the elbow / drop enters its gutter"),
    ("gutter_", "gutter_", "a corner: two runs meet"),
    ("downspout_", "downspout_", "same: a pipe and its run-on extension"),
    ("snowguard_", "snowguard_", "same: a bracket into its own rail"),
    ("chim_", "chim_", "same: a stack on its own shoulder"),
    ("chim_", "chimcap_", "same: the crown on its stack"),
    ("flue_", "chimcap_", "a flue pot through the crown"),
    ("flue_", "chim_", "a flue pot into the stack top"),
    ("stovepipe_", "stovepipe_", "same: the cap on its pipe"),
    ("fit_shroud", "chimcap_", "same: the hood legs on the crown"),
    ("fit_clip", "downspout_", "a clip round its pipe"),
)


def _is_fit(name):
    return name.startswith(FIT_PREFIXES)


def _geo(o):
    me = o.data
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    M = np.array(o.matrix_world)
    co = co @ M[:3, :3].T + M[:3, 3]
    return co, [tuple(p.vertices) for p in me.polygons]


def _shells(co, polys):
    """Connected shells: [[polys]] (by shared vertex index)."""
    parent = list(range(len(co)))

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a
    for p in polys:
        r = find(p[0])
        for v in p[1:]:
            parent[find(v)] = r
    groups = {}
    for p in polys:
        groups.setdefault(find(p[0]), []).append(p)
    return [ps for _r, ps in sorted(groups.items(), key=lambda kv: min(min(p) for p in kv[1]))]


def _samples(co, polys, step=SAMPLE_M):
    pts = [c for c in co]
    seen = set()
    for p in polys:
        pts.append(co[list(p)].mean(0))
        for i in range(len(p)):
            a, b = p[i], p[(i + 1) % len(p)]
            if (b, a) in seen or (a, b) in seen:
                continue
            seen.add((a, b))
            n = int(float(np.linalg.norm(co[b] - co[a])) / step)
            for k in range(1, n + 1):
                pts.append(co[a] + (co[b] - co[a]) * (k / (n + 1)))
    return np.array(pts)


class _Shell:
    def __init__(self, part, k, co, polys):
        self.part, self.k = part, k
        self.name = f"{part}#{k}"
        vi = sorted({v for p in polys for v in p})
        remap = {v: i for i, v in enumerate(vi)}
        self.co = co[vi]
        self.polys = [tuple(remap[v] for v in p) for p in polys]
        self.lo, self.hi = self.co.min(0), self.co.max(0)
        self.bvh = BVHTree.FromPolygons([tuple(c) for c in self.co], self.polys, all_triangles=False)
        self.smp = _samples(self.co, self.polys)
        self.inter, self.rules = [], {}


def _ground_gap(recipe, grade, p):
    """Gap from a point to grade (the design terrain of a plinth recipe, z 0 under an engine base) lifted by the
    closed-part lift (GROUND_LIFT: closed parts at grade start there); a point below grade is buried: 0."""
    g = grade(float(p[0]), float(p[1]))
    if p[2] < g:
        return 0.0
    return max(0.0, float(p[2]) - (g + _lift(recipe)))


def _declared_embed(fit_name, other):
    for fp, op, cap, why in EMBEDS:
        if fit_name.startswith(fp) and other.startswith(op):
            return cap, why
    return False, None


def _root(name, pre):
    r = name[len(pre):]
    for suf in ("ext", "sh", "top", "cap", "b", "p"):
        if r.endswith(suf) and len(r) > len(suf):
            r = r[: -len(suf)]
            break
    return r


def _declared_joint(a, b):
    for pa, pb, why in JOINTS:
        for x, y in ((a, b), (b, a)):
            if x.startswith(pa) and y.startswith(pb):
                if why.startswith("same") and _root(x, pa) != _root(y, pb):
                    continue
                return why
    return None


_DIRS6 = tuple(Vector(d).normalized() for d in ((1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1),
                                                  (0, 0, -1)))


def _inside(p, bvh, reach=200.0):
    """p inside the part `bvh`: >= 5 of 6 axis rays meet it from behind (an open bottom / a dropped face loses
    one ray; a point merely near an open part does not pass)."""
    back = 0
    for k, d in enumerate(_DIRS6):
        r = bvh.ray_cast(p, d, reach)
        if r[0] is not None and r[1].dot(d) > 0.0:
            back += 1
        if back + (5 - k) < 5:
            return False
    return back >= 5


def _is_sheet(co, polys):
    """A single-surface part (soffit, pane, a flat ring): every face coplanar-parallel; it has no inside."""
    ns = []
    for p in polys[:64]:
        a, b, c = (Vector(co[p[0]]), Vector(co[p[1]]), Vector(co[p[2]]))
        n = (b - a).cross(c - a)
        if n.length > 1e-12:
            ns.append(n.normalized())
    return bool(ns) and all(abs(n.dot(ns[0])) > 0.999 for n in ns)


def _through_sheet(points, bvh, cap=0.5):
    """Max distance of `points` behind a sheet (opposite its normal), i.e. how far a shell pokes through it."""
    d = 0.0
    for p in points:
        loc, nrm, _i, dist = bvh.find_nearest(Vector(p), cap)
        if loc is not None and (Vector(p) - loc).dot(nrm) < -1e-6 and abs((Vector(p) - loc).normalized().dot(nrm)) > 0.9:
            d = max(d, dist)
    return d


_DEEP = [None]


def _depth(points, bvh, lo=None, hi=None, cap=1.0):
    """Max distance from `points` that lie INSIDE the part `bvh` to its surface (the overlap depth); points outside
    the box lo..hi (+ cap) are skipped."""
    d = 0.0
    for p in points:
        if lo is not None and (np.any(p < lo - cap) or np.any(p > hi + cap)):
            continue
        P = Vector(p)
        loc, _n, _i, dist = bvh.find_nearest(P, cap)
        if loc is None or dist <= d:
            continue
        if _inside(P, bvh):
            # the way out: the shortest axis ray that leaves through a face from behind (an internal joint face
            # between two boxes of one part, met front-on, is not a way out)
            out = None
            for dv in _DIRS6:
                r = bvh.ray_cast(P, dv, cap)
                if r[0] is not None and r[1].dot(dv) > 0.0 and (out is None or r[3] < out):
                    out = r[3]
            dd = out if out is not None else dist
            if dd > d:
                d = dd
                _DEEP[0] = tuple(round(float(v), 3) for v in p)
    return d


def _hit_any(world, others, o, d, dist):
    best = None
    r = world.ray_cast(o, d, dist)
    if r[0] is not None:
        best = (r[3], r[1])
    for t in others:
        r = t.bvh.ray_cast(o, d, dist)
        if r[0] is not None and (best is None or r[3] < best[0]):
            best = (r[3], r[1])
    return best


def _enclosed(world, others, p):
    """p hidden inside the building: covered from above (the up ray meets the roof / a slab / a stack) and walled in
    (every horizontal ray and an up-diagonal meet building geometry within 300 m): an attic, the inside of a slab or
    a wall. A point under an eave OUTSIDE the walls is not (its outward ray escapes)."""
    for d in ((0, 0, 1), (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0.6, 0.6, 0.53), (-0.6, -0.6, 0.53)):
        if _hit_any(world, others, p, Vector(d).normalized(), 300.0) is None:
            return False
    return True


def _down_gap(world, others, p, recipe, grade):
    h = _hit_any(world, others, p, Vector((0, 0, -1)), 60.0)
    g = _ground_gap(recipe, grade, p)
    return min(g, h[0]) if h is not None else g


def audit(recipe, parts, rows=None, sc=None):
    """The close-look audit (see the block comment). Returns {"shells": [...], "violations": [...], ...}."""
    geo = {o.name: _geo(o) for o, _m in parts if o.type == "MESH" and len(o.data.polygons)}
    grade = _grade_fn(recipe)
    verts, polys, owner = [], [], []
    for nm, (co, ps) in geo.items():
        if _is_fit(nm):
            continue
        base = len(verts)
        verts += [tuple(c) for c in co]
        polys += [tuple(base + v for v in p) for p in ps]
        owner += [nm] * len(ps)
    world = BVHTree.FromPolygons(verts, polys, all_triangles=False)
    _pb, _ps = {}, {}

    def part_bvh(nm):
        if nm not in _pb:
            co, ps = geo[nm]
            _pb[nm] = BVHTree.FromPolygons([tuple(c) for c in co], ps, all_triangles=False)
        return _pb[nm]

    def part_smp(nm):
        if nm not in _ps:
            _ps[nm] = _samples(*geo[nm])
        return _ps[nm]
    shells = []
    for nm, (co, ps) in sorted(geo.items()):
        if _is_fit(nm):
            for k, sp in enumerate(_shells(co, ps)):
                shells.append(_Shell(nm, k, co, sp))
    viol = []

    def fin(p):
        return [round(v, 3) for v in auth_to_final(recipe, tuple(float(x) for x in p))]
    # 1. contact to the building / grade, and intersections with non-fitting parts
    for s in shells:
        hits = sorted({owner[i] for i, _j in world.overlap(s.bvh)})
        d_world, sup = 1e9, None
        for p in s.smp:
            loc, _n, idx, dist = world.find_nearest(Vector(p), 0.5)
            if loc is not None and dist < d_world:
                d_world, sup = dist, owner[idx]
        d_ground = _ground_gap(recipe, grade, s.co[int(np.argmin(s.co[:, 2]))])
        if d_ground < d_world:
            d_world, sup = d_ground, "grade"
        if hits:
            d_world, sup = 0.0, hits[0]
        s.d_world, s.support = d_world, sup
        for h in hits:
            hlo, hhi = geo[h][0].min(0), geo[h][0].max(0)
            # how deep the fitting goes into the part; the part's own points inside the fitting only when none of
            # the fitting's are (a 2 cm board through a pipe between two of its samples)
            dep = _depth(s.smp, part_bvh(h), hlo, hhi) or _depth(part_smp(h), s.bvh, s.lo, s.hi)
            if dep <= 0.001 and _is_sheet(*geo[h]):          # a soffit / pane quad: how far the shell pokes through
                dep = _through_sheet(s.smp, part_bvh(h))
            if dep <= 0.001:
                s.inter.append({"with": h, "depth_mm": round(dep * 1000, 1), "declared": "touch", "ok": True})
                continue
            cap, why = _declared_embed(s.part, h)
            ok_ = why is not None and (cap is None or dep <= cap + 1e-4)
            s.inter.append({"with": h, "depth_mm": round(dep * 1000, 1), "declared": why, "ok": bool(ok_),
                            "at": fin(_DEEP[0]) if _DEEP[0] else None})
            if not ok_:
                viol.append(("intersect", s.name, round(dep * 1000, 1), f"crosses {h} (not a declared embed)"
                             if why is None else f"crosses {h} deeper than {cap * 1000:.0f} mm ({why})"))
    # 2. fitting <-> fitting contacts and joints (AABB candidates within CONTACT_M + 1 mm)
    n = len(shells)
    lo = np.array([s.lo for s in shells]) if n else np.zeros((0, 3))
    hi = np.array([s.hi for s in shells]) if n else np.zeros((0, 3))
    e = CONTACT_M + 0.001
    nb = {i: [] for i in range(n)}
    for i in range(n):
        for j in np.where(np.all(lo[i] <= hi + e, 1) & np.all(lo <= hi[i] + e, 1))[0]:
            j = int(j)
            if j <= i:
                continue
            a, b = shells[i], shells[j]
            ov = bool(a.bvh.overlap(b.bvh))
            gap = 0.0 if ov else 1e9
            if not ov:
                for P, T in ((a.smp, b.bvh), (b.smp, a.bvh)):
                    for p in P:
                        r = T.find_nearest(Vector(p), e)
                        if r[0] is not None:
                            gap = min(gap, r[3])
            if gap <= CONTACT_M:
                nb[i].append((j, gap))
                nb[j].append((i, gap))
            if ov and a.part != b.part:
                why = _declared_joint(a.part, b.part)
                dep = _depth(a.smp, b.bvh, b.lo, b.hi) or _depth(b.smp, a.bvh, a.lo, a.hi)
                if dep <= 0.001:
                    why = why or "touch"
                a.inter.append({"with": b.name, "depth_mm": round(dep * 1000, 1), "declared": why,
                                "ok": why is not None})
                if why is None:
                    viol.append(("intersect", a.name, round(dep * 1000, 1), f"crosses fitting {b.name}"))
    reach = {i for i, s in enumerate(shells) if s.d_world <= CONTACT_M}
    todo = list(reach)
    while todo:
        i = todo.pop()
        for j, _g in nb[i]:
            if j not in reach:
                reach.add(j)
                todo.append(j)
    for i, s in enumerate(shells):
        s.near_fit = min([g for _j, g in nb[i]], default=None)
        s.held = i in reach
        if not s.held:
            viol.append(("contact", s.name, round(s.d_world * 1000, 1),
                         f"floats: {s.d_world * 1000:.0f} mm from {s.support}, no contact chain to the building"))
    # 3. gutter runs: contact at every station
    for s in shells:
        ext = s.hi - s.lo
        if s.part.startswith("gutter_") and max(ext[0], ext[1]) > 1.0 and ext[2] < 0.4:
            ax = 0 if ext[0] >= ext[1] else 1
            bins = {}
            for p in s.smp:
                r = world.find_nearest(Vector(p), 0.5)
                d = r[3] if r[0] is not None else 0.5
                if d > CONTACT_M and _inside(Vector(p), world):
                    d = 0.0                              # seated (inside the board it hangs on)
                if d > CONTACT_M:                        # the seat, measured along the four horizontal axes
                    for dv in _DIRS6[:4]:
                        h = world.ray_cast(Vector(p), dv, 0.5)
                        if h[0] is not None:
                            d = min(d, h[3])
                b = int((p[ax] - s.lo[ax]) / STATION_M)
                bins[b] = min(bins.get(b, 1e9), d)
            run_, worst_run = 0, 0
            for b in range(min(bins), max(bins) + 1):
                run_ = run_ + 1 if bins.get(b, 1.0) > CONTACT_M else 0
                worst_run = max(worst_run, run_)
            worst = max(bins.values())
            bad = sum(1 for v in bins.values() if v > CONTACT_M)
            s.rules["gutter"] = {"stations": len(bins), "worst_mm": round(worst * 1000, 1), "bad": bad,
                                 "best_mm": round(min(bins.values()) * 1000, 1),
                                 "bad_at": [round(float(s.lo[ax]) + STATION_M * (b + 0.5), 2)
                                            for b, v in sorted(bins.items()) if v > CONTACT_M][:12],
                                 "longest_unsupported_m": worst_run * STATION_M}
            if worst_run * STATION_M > GUTTER_SPAN_M:
                viol.append(("gutter", s.name, round(worst * 1000, 1),
                             f"{worst_run * STATION_M:.1f} m off the fascia / wall ({bad}/{len(bins)} stations)"))
    # 4. vertical pipe runs: held at least every PIPE_HOLD_M
    for i, s in enumerate(shells):
        ext = s.hi - s.lo
        if not (s.part.startswith("downspout_") and ext[2] > 1.0 and max(ext[0], ext[1]) < 0.25):
            continue
        holds, wall = [], 1e9
        z = float(s.lo[2])
        while z <= float(s.hi[2]):                       # the four vertical edges, every 2 cm: wall / trim contact
            for ex in (s.lo[0], s.hi[0]):
                for ey in (s.lo[1], s.hi[1]):
                    r = world.find_nearest(Vector((ex, ey, z)), 0.5)
                    if r[0] is not None:
                        wall = min(wall, r[3])
                        if r[3] <= CONTACT_M:
                            holds.append(z)
            z += 0.02
        for j, _g in nb[i]:                              # a clip, the neck, the shoe touching the run
            t = shells[j]
            holds.append(min(max(0.5 * float(t.lo[2] + t.hi[2]), float(s.lo[2])), float(s.hi[2])))
        if _ground_gap(recipe, grade, s.co[int(np.argmin(s.co[:, 2]))]) <= CONTACT_M:
            holds.append(float(s.lo[2]))
        hz = sorted(set(round(z, 3) for z in holds))
        span = float(ext[2]) if not hz else max(
            [hz[0] - float(s.lo[2]), float(s.hi[2]) - hz[-1]] + [b - a for a, b in zip(hz, hz[1:])])
        s.rules["pipe"] = {"height_m": round(float(ext[2]), 2), "wall_gap_mm": round(wall * 1000, 1),
                           "max_unheld_m": round(span, 2)}
        if span > PIPE_HOLD_M:
            viol.append(("pipe", s.name, round(wall * 1000, 1),
                         f"hangs {span:.2f} m unheld ({wall * 1000:.0f} mm off the wall, no clip)"))
    # 5. chimney bases: no air under any point of the base outline
    for s in shells:
        if not s.part.startswith("chim_"):
            continue
        zb = float(s.lo[2])
        base = s.co[np.abs(s.co[:, 2] - zb) < 1e-3]
        if len(base) < 3:
            continue
        cxy = base[:, :2].mean(0)
        pts = []
        for k in range(len(base)):
            for b in base[k + 1:]:
                for t in (0.0, 0.25, 0.5, 0.75):
                    q = (base[k] + (b - base[k]) * t).copy()
                    q[:2] += (cxy - q[:2]) * 0.02             # 2 % inset: off the stack's own corner lines
                    pts.append(q)
        others = [t for t in shells if t.part.startswith("chim_") and t.part != s.part]
        worst, buried, air_at = 0.0, 0, None
        for q in pts:
            Q = Vector(q) + Vector((0, 0, -0.001))
            if _enclosed(world, others, Q):
                buried += 1
                continue
            if any(abs(t.hi[2] - zb) <= CONTACT_M and t.lo[0] - 1e-3 <= q[0] <= t.hi[0] + 1e-3
                   and t.lo[1] - 1e-3 <= q[1] <= t.hi[1] + 1e-3 for t in others):
                buried += 1                                  # seated on its shoulder (whose top is dropped)
                continue
            d = _down_gap(world, others, Q, recipe, grade)
            if d > worst:
                worst, air_at = d, q
        s.rules["chimney"] = {"base_z": round(zb, 3), "points": len(pts), "buried": buried,
                              "worst_air_mm": round(worst * 1000, 1),
                              "air_at": fin(air_at) if air_at is not None else None}
        if worst > CONTACT_M:
            viol.append(("chimney", s.name, round(worst * 1000, 1),
                         f"air under the base at {fin(air_at)} ({buried}/{len(pts)} points buried)"))
    # 6. vanilla rows
    row_rec = audit_rows(recipe, rows or [], sc) if sc is not None else []
    for r in row_rec:
        if r["air_mm"] > CONTACT_M * 1000:
            viol.append(("row", r["prefab"] + "@" + ",".join(f"{v:.2f}" for v in r["final"][:2]), r["air_mm"],
                         r["detail"]))
    rec = [{"shell": s.name, "final_c": fin((s.lo + s.hi) / 2), "size": [round(float(v), 3) for v in (s.hi - s.lo)],
            "zmin": round(float(s.lo[2]), 3), "contact_mm": round(s.d_world * 1000, 1), "support": s.support,
            "near_fit_mm": None if s.near_fit is None else round(s.near_fit * 1000, 1), "held": s.held,
            "inter": s.inter, "rules": s.rules} for s in shells]
    return {"key": recipe.key, "shells": rec, "rows": row_rec, "violations": viol, "n_shells": len(shells)}


_BASES = None


def _vanilla_bases():
    global _BASES
    if _BASES is None:
        with open(os.path.join(_HERE, "fittings_vanilla_bases.json"), encoding="utf-8") as fh:
            _BASES = json.load(fh)["props"]
    return _BASES


def _row_frame(recipe, q):
    """Game-local x / y / z axes of a yaw-only row, as FINAL-frame vectors (bl_fit_render's F @ R @ F^-1 with
    game local (x, y, z) -> FINAL (-x, -z, y))."""
    qx, qy, qz, qw = q
    t = 2.0 * math.atan2(qy, qw)              # yaw about game y
    # game local x / z after the yaw, in game coordinates (x right, z forward)
    gx = (math.cos(t), 0.0, -math.sin(t))
    gz = (math.sin(t), 0.0, math.cos(t))
    f = lambda v: Vector((-v[0], -v[2], v[1]))          # noqa: E731  game -> FINAL
    return f(gx), Vector((0.0, 0.0, 1.0)), f(gz)


def audit_rows(recipe, rows, sc):
    """rows = the placed rows [(kind, prefab, FINAL point (x, y, z), design row)]: per outline point the gap to
    the surface it mounts on, for every variant of the placeholder."""
    out = []
    B = _vanilla_bases()
    for kind, prefab, pf, drow in rows:
        q = drow[3]
        ax, up, az = _row_frame(recipe, q)
        P0 = Vector(pf)
        worst, where, var_w = -1e9, None, None
        for vname, v in sorted(B.get(prefab, {}).items()):
            pts = []
            if kind == "wall":
                pts = [(P0 + ax * x + up * y, -az) for x, y in v["back"]]
            elif kind == "ceiling":
                pts = [(P0 + ax * x + az * z, Vector((0, 0, 1))) for x, z in v["top"]]
            else:
                pts = [(P0 + ax * x + az * z + up * y, Vector((0, 0, -1))) for y, ring in v["rings"]
                       for x, z in ring]
            for p, d in pts:
                pa = Vector(final_to_auth(recipe, tuple(p)))
                da = Vector(final_to_auth(recipe, tuple(d))).normalized() if kind == "wall" else d
                if kind == "roof":
                    h = sc.cast(Vector((pa.x, pa.y, pa.z + 50.0)), (0, 0, -1), 100.0)
                    gap = (pa.z - h[0].z) if h is not None else 9.0
                else:
                    h = sc.cast(pa - da * 0.05, da, 0.5)
                    gap = (h[3] - 0.05) if h is not None else 0.45
                if gap > worst:
                    worst, where, var_w = gap, p, vname
        out.append({"kind": kind, "prefab": prefab, "final": [round(c, 3) for c in pf],
                    "air_mm": round(max(worst, 0.0) * 1000, 1), "worst_variant": var_w,
                    "detail": f"{var_w}: {worst * 1000:.0f} mm of air under its "
                              f"{'back plate' if kind == 'wall' else 'top plate' if kind == 'ceiling' else 'base'}"
                              f" at {[round(c, 2) for c in where] if where is not None else None}"})
    return out


# =====================================================================================================================
# Close look, the FIX pass (2026-10-17), run by build() before the audit on every fittings build:
#   refit_gutters      every gutter_ box is rebuilt as a profiled run whose back rim seats 2 mm into the face it
#                      hangs on (found by rays: v1's boxes hung 10 mm off their fascia, L1's 60 mm), cut where it
#                      would run into a wall / bay / another roof (L4 entry, L5 wings, bays and hall), with end
#                      caps; profiles per building (GUTTER_PROFILE) and LOD0-only hanger straps where the budget
#                      allows (GUTTER_STRAPS);
#   route_downspouts   every downspout() call (cs2_bldg.FIT_PIPES) is rebuilt with the building in view: an outlet
#                      drop from inside its gutter, a 45-degree neck back to the wall UNDER the soffit / fascia
#                      (v1's elbows cut through them), the pipe standing just clear of the proudest trim on its line
#                      (v1 ran through belts, quoins and corner boards), clips to the wall wherever a stretch would
#                      hang more than PIPE_HOLD_M, and the shoe seated on its support (v1's hovered ~43 mm);
#   clip_runs          vertical pipe parts built outside downspout() (L2's plinth runs) get the same clips.
# All of it is geometry in the existing parts (same names, materials, part order): no new part, no new key.
# =====================================================================================================================
import bmesh                                            # noqa: E402

GUTTER_PROFILE = {"L1": "kstyle", "L4": "kstyle", "L5": "kstyle"}   # default "half" (chalets, hut, patrol)
GUTTER_STRAPS = {"L1": 0.9, "L3": 0.9, "L4": 0.9, "P2": 0.9}          # hanger straps every ~0.9 m (LOD0 only)
HALF_R, HALF_SEG = 0.0625, 5              # half-round: 12.5 cm, a 5-segment arc
KSTYLE = ((0.0, -0.11), (0.09, -0.11), (0.115, -0.085), (0.115, -0.05), (0.13, -0.035), (0.13, 0.0))
CHAMFER = ((0.0, -0.10), (0.07, -0.10), (0.12, -0.05), (0.12, 0.0))   # the cheapest that still reads round
GUTTER_EMBED = 0.002
CLIP = (0.03, 0.03)                       # clip bar cross-section (along the wall, height)
GRADE_LIFT = {}                           # per recipe: closed parts at grade start this high (default GROUND_LIFT)


def gutter_profile(kind):
    """(u, v) from the back-top rim (0, 0): u outward from the face it hangs on, v up; back face flag."""
    if kind == "half":
        pts = [(HALF_R + HALF_R * math.cos(math.pi + k * math.pi / HALF_SEG),
                HALF_R * math.sin(math.pi + k * math.pi / HALF_SEG)) for k in range(HALF_SEG + 1)]
        pts[0], pts[-1] = (0.0, 0.0), (2 * HALF_R, 0.0)
        return pts, False
    tab = KSTYLE if kind == "kstyle" else CHAMFER if kind == "chamfer" else None
    if tab is None:
        raise ValueError(f"gutter profile {kind!r}")
    return [(0.0, 0.0)] + list(tab), True


def gutter_tris_per_run(kind, straps=0):
    pts, back = gutter_profile(kind)
    sides = len(pts) - 1 - (1 if back else 0)
    return 2 * sides + 2 + 2 * (len(pts) - 2) + 4 * straps


def _grade_fn(recipe):
    """Grade z(x, y): the design terrain of a plinth recipe, else z 0 (the engine base flattens the lot)."""
    t = _terrain_fn(recipe)
    return t if t is not None else (lambda x, y: 0.0)


def _terrain_fn(recipe):
    """Design terrain z(x, y) of a plinth recipe (authoring frame) or None: L4 at its steepest supported slope
    (t30: a base must be buried for every slope it supports), L2 at its 35 % design slope."""
    try:
        if recipe.key == "L4":
            from recipes import l4_swanky_top as M
            return lambda x, y: M.t_at(x, M.S_MAX)
        if recipe.key == "L2":
            from recipes import lodge_hut as M
            return lambda x, y: M.terrain_z(x)
    except Exception:            # noqa: BLE001
        return None
    return None


def _lift(recipe):
    return GRADE_LIFT.get(recipe.key, K.GROUND_LIFT)


class _World:
    """The non-fitting parts as one BVH (+ owner per polygon); the frame of the fix pass."""

    def __init__(self, parts):
        verts, polys, self.owner = [], [], []
        self.pnames, plo, phi, self.pgeo, self._pb = [], [], [], [], {}
        for o, _m in parts:
            if o.type != "MESH" or _is_fit(o.name) or not len(o.data.polygons):
                continue
            co, ps = _geo(o)
            self.pnames.append(o.name)
            plo.append(co.min(0))
            phi.append(co.max(0))
            self.pgeo.append((co, ps))
            base = len(verts)
            verts += [tuple(c) for c in co]
            polys += [tuple(base + v for v in p) for p in ps]
            self.owner += [o.name] * len(ps)
        self.bvh = BVHTree.FromPolygons(verts, polys, all_triangles=False)
        self.plo, self.phi = np.array(plo), np.array(phi)

    def inside_part(self, p):
        """p inside ONE part (tested against that part alone: a neighbour's faces cannot block its rays)."""
        q = np.asarray(tuple(p))
        for i in np.where(np.all(self.plo - 1e-4 <= q, 1) & np.all(q <= self.phi + 1e-4, 1))[0]:
            if i not in self._pb:
                co, ps = self.pgeo[i]
                self._pb[i] = BVHTree.FromPolygons([tuple(c) for c in co], ps, all_triangles=False)
            if _inside(Vector(tuple(p)), self._pb[i], 200.0):
                return True
        return False

    def cast(self, o, d, dist):
        loc, nrm, idx, t = self.bvh.ray_cast(Vector(o), Vector(d).normalized(), dist)
        return None if loc is None else (loc, nrm, self.owner[idx], t)

    def inside(self, p):
        return _inside(Vector(p), self.bvh, 200.0)    # 200 m: a point inside a 77 m fascia board

    def enclosed4(self, p, reach=2.0):
        """The v1 shoe gate's test (3+ of 4 horizontal rays meet a face from behind): a pier / plinth with no top."""
        back = 0
        for d in ((1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0)):
            h = self.cast(Vector(p), d, reach)
            if h is not None and h[1].dot(Vector(d)) > 0.0:
                back += 1
        return back >= 3


def _write(o, g):
    """Replace part o's mesh with the _Geo g (keeps the object, its name and its slot in the part list)."""
    bmesh.ops.remove_doubles(g.bm, verts=g.bm.verts[:], dist=1e-6)
    g.bm.normal_update()
    me = o.data
    g.bm.to_mesh(me)
    g.bm.free()
    me.update()


def _gutter_run(o):
    co, _p = _geo(o)
    lo, hi = co.min(0), co.max(0)
    ext = hi - lo
    ax = 0 if ext[0] >= ext[1] else 1
    c = 1 - ax
    return lo, hi, ax, c


def refit_gutters(recipe, parts, W, report):
    kind = GUTTER_PROFILE.get(recipe.key, "half")
    pts, back = gutter_profile(kind)
    vmin = min(v for _u, v in pts)
    width = max(u for u, _v in pts)
    every = GUTTER_STRAPS.get(recipe.key)
    runs = []
    for o, _m in parts:
        if not o.name.startswith("gutter_") or o.type != "MESH":
            continue
        lo, hi, ax, c = _gutter_run(o)
        A = Vector((1.0, 0.0, 0.0)) if ax == 0 else Vector((0.0, 1.0, 0.0))
        C = Vector((0.0, 1.0, 0.0)) if ax == 0 else Vector((1.0, 0.0, 0.0))
        a0, a1 = float(lo[ax]), float(hi[ax])
        cc, zr = 0.5 * float(lo[c] + hi[c]), float(hi[2])
        hits = {1: [], -1: []}
        k = 0
        while True:
            a = a0 + 0.25 + 0.5 * k
            if a > a1 - 0.25:
                break
            k += 1
            for sg in (1, -1):
                best = None
                for dz in (0.02, 0.05, 0.08):
                    p = A * a + C * cc + Vector((0, 0, zr - dz))
                    h = W.cast(p, C * sg, 0.5)
                    if h is not None and h[1].dot(C * sg) < -0.3 and (best is None or h[3] < best):
                        best = h[3]
                if best is not None:
                    hits[sg].append((a, best))
        side = 1 if (len(hits[1]), -np.median([d for _a, d in hits[1]]) if hits[1] else 0) >= \
            (len(hits[-1]), -np.median([d for _a, d in hits[-1]]) if hits[-1] else 0) else -1
        if not hits[side]:
            raise RuntimeError(f"fittings {recipe.key}: {o.name} has no face behind it within 0.5 m")
        d_s = float(np.median([d for _a, d in hits[side]]))
        O = -C * side                                      # outward: away from the face it hangs on
        B0 = A * 0.0 + C * (cc + side * (d_s + GUTTER_EMBED)) + Vector((0, 0, zr))

        def P(t, u, v):
            return B0 + A * t + O * u + Vector((0, 0, v))
        # blocked stations: the cross-section centre / front inside a wall, bay, another roof
        # every profile point (pulled 1 cm off the back face it seats on, 3 mm in from the outer skin) and the centre
        # a grid over the whole cross-section (u every ~2.5 cm, v every ~2.5 cm inside the profile, 3 mm in from
        # its skin, 1 cm off the face it seats on): a crossing board that clips only the top or the lip still cuts
        def _vbot(u):
            lo_ = 0.0
            for (u0, v0), (u1, v1) in zip(pts, pts[1:]):
                if min(u0, u1) - 1e-9 <= u <= max(u0, u1) + 1e-9 and abs(u1 - u0) > 1e-9:
                    lo_ = min(lo_, v0 + (v1 - v0) * (u - u0) / (u1 - u0))
            return lo_
        tests = []
        for i_ in range(6):
            u = 0.01 + (width - 0.013) * i_ / 5.0
            vb = _vbot(u) + 0.003
            for k_ in range(5):
                tests.append((u, -0.003 + (vb + 0.003) * k_ / 4.0))

        def blocked(t):
            return any(W.inside_part(P(t, u, v)) for u, v in tests)
        step = 0.02
        ts = [a0 + step * i for i in range(int((a1 - a0) / step) + 1)] + [a1]
        flags = [blocked(t) for t in ts]
        segs, start = [], None
        for i, (t, f) in enumerate(zip(ts, flags)):
            if not f and start is None:
                start = t if i == 0 else _edge(t, ts[i - 1], blocked)
            if f and start is not None:
                segs.append((start, _edge(ts[i - 1], t, blocked)))
                start = None
        if start is not None:
            segs.append((start, a1))
        segs = [(s0, s1) for s0, s1 in segs if s1 - s0 >= 0.5]       # no slivers between two bays
        if not segs:
            raise RuntimeError(f"fittings {recipe.key}: {o.name} is blocked along its whole run")
        g = K._Geo()
        nstrap = 0
        seats = []
        for s0, s1 in segs:
            # each segment seats on the face behind IT (L5's courtyard run steps past the wing fascias)
            ds = [d for a, d in hits[side] if s0 <= a <= s1]
            d_seg = float(np.median(ds)) if ds else d_s
            off = C * (side * (d_seg - d_s))

            def P(t, u, v, off=off):
                return B0 + off + A * t + O * u + Vector((0, 0, v))
            seats.append(round((d_seg - 0.06) * 1000, 1))
            ring0 = [g.v(P(s0, u, v)) for u, v in pts]
            ring1 = [g.v(P(s1, u, v)) for u, v in pts]
            for i in range(len(pts) - 1):
                if back and i == 0:
                    continue                              # the back face lies on the fascia
                u = 0.5 * (pts[i][0] + pts[i + 1][0])
                v = 0.5 * (pts[i][1] + pts[i + 1][1])
                mid = O * (u - 0.5 * width) + Vector((0, 0, v - 0.5 * vmin))
                g.face([ring0[i], ring0[i + 1], ring1[i + 1], ring1[i]], mid)
            g.face([ring0[-1], ring0[0], ring1[0], ring1[-1]], Vector((0, 0, 1)))       # top (the water line)
            g.face(list(ring0), -A)
            g.face(list(ring1), A)
            if every and s1 - s0 > 0.6:
                n_s = max(1, int(round((s1 - s0 - 0.3) / every)))
                for j in range(n_s + 1):
                    t = s0 + 0.15 + (s1 - s0 - 0.3) * (j / n_s if n_s else 0.5)
                    q = [P(t + dt, u, v) for dt in (-0.0125, 0.0125) for u, v in
                         ((-0.002, 0.003), (width + 0.004, 0.003), (width + 0.004, -0.04))]
                    vv = [g.v(x) for x in q]                # (t-, back), (t-, lip top), (t-, lip low), (t+, ...)
                    g.face([vv[0], vv[1], vv[4], vv[3]], Vector((0, 0, 1)))
                    g.face([vv[1], vv[2], vv[5], vv[4]], O)
                    nstrap += 1
        _write(o, g)
        runs.append({"name": o.name, "A": A, "O": O, "B0": B0, "segs": segs, "rim": zr, "vmin": vmin,
                     "width": width})
        report.append({"gutter": o.name, "profile": kind, "back_gap_was_mm": round((d_s - 0.06) * 1000, 1),
                       "segments": [[round(s0, 3), round(s1, 3)] for s0, s1 in segs],
                       "length_m": round(sum(s1 - s0 for s0, s1 in segs), 2), "straps": nstrap,
                       "seat_gap_was_mm": seats})
    return runs


def _edge(t_free, t_blocked, blocked, n=7):
    """Bisect the boundary between a free and a blocked station; returns a point 1 cm short of it (the run ends
    with a stop end just clear of the wall, bay or crossing fascia it meets)."""
    a, b = t_free, t_blocked
    for _ in range(n):
        m = 0.5 * (a + b)
        if blocked(m):
            b = m
        else:
            a = m
    return a - (0.01 if b > a else -0.01)        # a stop end 1 cm short of the wall / fascia it meets


def _wall_dir(W, p, reach=0.6, z_lo=None, z_hi=None):
    """Outward normal (horizontal) of the wall a pipe at p runs on, its distance, or None. With a run z_lo..z_hi,
    the face that stands behind the pipe over MOST of the run wins (at a corner: the solid face, not the one
    with a glazing band or the one that stops under an A-frame roof); ties go to the nearer."""
    zs = [p.z] if z_lo is None else [z_lo + (z_hi - z_lo) * k / 12.0 for k in range(13)]
    for square in (-0.9, -0.3):
        best = _wall_pass(W, p, reach, zs, square)
        if best is not None:
            return best
    return None


def _wall_pass(W, p, reach, zs, square):
    best = None
    for k in range(16):
        a = 2 * math.pi * k / 16
        d = Vector((math.cos(a), math.sin(a), 0.0))
        cover, dist, nrm = 0, [], None
        for z in zs:
            h = W.cast(Vector((p.x, p.y, z)), d, reach)
            if h is None or h[2].startswith(GLASS_PREFIXES) or h[1].dot(d) > square:
                continue                                 # a face squarely behind the pipe can take a clip
            cover += 1
            dist.append(h[3])
            if nrm is None:
                nrm = Vector((h[1].x, h[1].y, 0.0)).normalized()
        if not cover:
            continue
        key = (cover, -min(dist))
        if best is None or key > best[0]:
            best = (key, nrm, min(dist))
    return None if best is None else (best[1], best[2])


FREE_STANDING = ("pier_", "col_", "post_", "rail_", "trail_", "stair", "balc", "deck_", "lamp_", "sign_",
                 "bench_", "flag_", "mast_", "umb", "skirack_", "akja_", "drum_", "log_stack", "fp_", "pool")


def _proud_hit(W, org, d, reach):
    """Distance along d to the first face that belongs to the wall a pipe runs on (the wall, its trims, belts,
    ledges and plinth caps), seen from the front: free-standing things in front (a pier, a post, a rail, a deck)
    and faces met from behind are passed through."""
    t = 0.0
    for _k in range(8):
        h = W.cast(org + d * t, d, reach - t)
        if h is None:
            return None
        if h[1].dot(d) < -0.3 and not h[2].startswith(GLASS_PREFIXES + FREE_STANDING):
            return t + h[3]
        t += h[3] + 1e-3
    return None


def _support_below(W, recipe, p, grade, reach=3.0):
    """(z, kind) of what a foot at p would stand on: a part's top within `reach`, else grade (the design terrain of
    a plinth recipe, z 0 under an engine base) lifted by the closed-part lift."""
    h = W.cast(p, (0, 0, -1), reach)
    cands = [(grade(p.x, p.y) + _lift(recipe), "grade")]
    if h is not None and h[1].z > 0.5:
        cands.append((h[0].z, "part:" + h[2]))
    cands = [c for c in cands if c[0] <= p.z + 1e-6]
    return max(cands) if cands else (p.z, "none")


def _shoe_low(sd, size):
    """Lowest corner of the shoe (cs2_bldg._shoe_into) relative to its zb."""
    run, top, out_z = K.SHOE
    P0 = Vector((0, 0, top))
    P1 = Vector((0, 0, out_z)) + Vector((sd.x, sd.y, 0.0)).normalized() * run
    ax = (P1 - P0).normalized()
    side = ax.cross(Vector((0, 0, 1))).normalized()
    upv = side.cross(ax).normalized()
    c = (P0 + P1) * 0.5
    L = (P1 - P0).length
    return min((c + ax * (sx * 0.5 * L) + side * (sy * 0.5 * size) + upv * (sz * 0.5 * size)).z
               for sx in (-1, 1) for sy in (-1, 1) for sz in (-1, 1))


def _clip_into(g, W, pc, nw, size, z, rep):
    """A clip bar from the pipe's back face to the wall behind it at height z (5 mm into each); None if the wall
    behind is glass or farther than 0.4 m."""
    tw = nw.cross(Vector((0, 0, 1)))
    for t_ in (0.0, -0.3 * size, 0.3 * size):
        back = pc - nw * (0.5 * size) + tw * t_
        h = W.cast(Vector((back.x, back.y, z)), -nw, 0.4)
        if h is not None and not h[2].startswith(GLASS_PREFIXES):
            break
    else:
        return False
    L = h[3] + 0.01
    if L <= 0.006 + 1e-4:
        return True                                   # touching already
    c = Vector((back.x, back.y, z)) - nw * (0.5 * L - 0.005)
    g.obb(c, (nw, tw, Vector((0, 0, 1))), (0.5 * L, 0.5 * CLIP[0], 0.5 * CLIP[1]), drop=("-X", "+X", "-Z"))
    rep.append(round(z, 2))
    return True


def _clips(g, W, pc, nw, size, z_lo, z_hi, holds, rep):
    """Clips so that no stretch of the run [z_lo, z_hi] goes more than PIPE_HOLD_M between holds."""
    zs = []
    z = z_lo
    while z <= z_hi:
        zs.append(z)
        z += 0.05
    back = pc - nw * (0.5 * size)
    for z in zs:
        h = W.cast(Vector((back.x, back.y, z)), -nw, 0.4)
        if h is not None and h[3] <= CONTACT_M:
            holds.append(z)
    hz = sorted(set([z_lo, z_hi] + [z for z in holds if z_lo <= z <= z_hi]))
    for a, b in zip(hz, hz[1:]):
        span = b - a
        if span <= PIPE_HOLD_M:
            continue
        k = int(math.ceil(span / (0.9 * PIPE_HOLD_M))) - 1
        for i in range(k):
            zc = a + span * (i + 1) / (k + 1)
            for dz in (0.0, 0.1, -0.1, 0.2, -0.2, 0.3, -0.3, 0.45, -0.45):
                if _clip_into(g, W, pc, nw, size, zc + dz, rep):
                    break
            else:
                rep.append(f"NO WALL @{zc:.2f}")         # the audit's pipe rule then names the stretch


def route_downspouts(recipe, parts, W, runs, grade, report):
    by = {o.name: o for o, _m in parts}
    for R in K.FIT_PIPES:
        o = by.get(R["name"])
        if o is None:
            continue
        R = dict(R)
        if "lo0" in R:                                 # follow a recipe that moved its parts after building them
            co, _p = _geo(o)
            dx, dy, dz = (float(co[:, i].min()) - R["lo0"][i] for i in range(3))
            R["x"], R["y"], R["z_top"], R["z_bottom"] = R["x"] + dx, R["y"] + dy, R["z_top"] + dz, R["z_bottom"] + dz
            if R["leader"] is not None:
                R["leader"] = (R["leader"][0] + dx, R["leader"][1] + dy)
        s = R["size"]
        n = Vector(R["n"])
        n = Vector((n.x, n.y, 0.0)).normalized()
        if R["leader"] is not None:
            G = Vector((R["x"], R["y"], 0.0))
            W0 = Vector((R["leader"][0], R["leader"][1], 0.0))
        else:
            G = Vector((R["x"], R["y"], 0.0)) + n * R["wall_gap"]
            W0 = Vector((R["x"], R["y"], 0.0))
        zt = R["z_top"]
        # the gutter this outlet drains
        gut, best = None, 0.35
        for r in runs:
            rel = G - Vector((r["B0"].x, r["B0"].y, 0.0))
            t = rel.dot(r["A"])
            u = rel.dot(r["O"])
            if abs(r["rim"] - zt) > 0.35:
                continue
            for s0, s1 in r["segs"]:
                if s0 - 0.1 <= t <= s1 + 0.1 and abs(u - 0.5 * r["width"]) < best:
                    best, gut = abs(u - 0.5 * r["width"]), (r, min(max(t, s0 + 0.5 * s + 0.03), s1 - 0.5 * s - 0.03))
        if gut is not None:
            r, t = gut
            G = Vector((r["B0"].x, r["B0"].y, 0.0)) + r["A"] * t + r["O"] * (0.5 * r["width"])
            z_out = r["rim"] - 0.03
            z_gb = r["rim"] + r["vmin"]
        else:                                          # no gutter (the school's fascia gutter): from its underside
            h = W.cast(Vector((G.x, G.y, zt - 0.8)), (0, 0, 1), 1.2)
            z_out = (h[0].z + 0.005) if h is not None else zt - 0.05
            z_gb = z_out - 0.01
        # the wall the pipe runs on, and how proud its trims stand on the pipe's line, height by height
        wd = _wall_dir(W, Vector((W0.x, W0.y, 0.0)), 0.6, R["z_bottom"] + 0.1, zt - 0.4)
        nw = None if wd is None else wd[0]
        z_lo, z_hi = R["z_bottom"], zt - 0.3
        W_lo = W_up = Vector(W0)
        z_split, jog_mode, ledge_z = None, None, None
        if nw is not None and R["shoe"]:
            tw = nw.cross(Vector((0, 0, 1)))
            prof = []
            z = z_lo
            while z <= z_hi:
                q_ = None
                for t_ in (-0.5 * s, 0.0, 0.5 * s):
                    h = _proud_hit(W, Vector((W0.x, W0.y, z)) + nw * 0.8 + tw * t_, -nw, 2.4)
                    if h is not None:
                        q_ = (0.8 - h) if q_ is None else max(q_, 0.8 - h)
                prof.append((z, q_))
                z += 0.05
            known = [(z, q_) for z, q_ in prof if q_ is not None]
            if known:
                p_all = max(q_ for _z, q_ in known)
                z_feat = max(z for z, q_ in known if q_ >= p_all - 0.01)
                z_fbot = min(z for z, q_ in known if q_ >= p_all - 0.01)
                upper = [q_ for z, q_ in known if z > z_feat + 0.02]
                lower = [q_ for z, q_ in known if z < z_fbot - 0.02]
                W_lo = W_up = W0 + nw * (p_all + 0.5 * s + 0.003)
                if upper and z_feat < z_hi - 0.4 and max(upper) - p_all < -0.25:
                    # a deep ledge under the run (L5's first-floor terrace caps, 0.6 m): the pipe runs on the upper
                    # wall and discharges onto the ledge (its shoe on the ledge top), as real upper pipes do
                    W_lo = W_up = W0 + nw * (max(upper) + 0.5 * s + 0.003)
                    R["z_bottom"] = z_feat + 0.05
                    jog_mode, ledge_z = "onto", z_feat
                elif upper and z_feat < z_hi - 0.4 and max(upper) < p_all - 0.04:
                    # a setback above the proudest feature on the line (a ledge, a plinth cap, a belt): the pipe
                    # runs on the upper wall and jogs out over the feature (a swan neck), then on down in front of it
                    W_up = W0 + nw * (max(upper) + 0.5 * s + 0.003)
                    z_split, jog_mode = z_feat, "over"
                elif lower and not upper and z_fbot > z_lo + 0.6 and max(lower) < p_all - 0.04:
                    # the proud part is the top of the run (L3's jettied upper storey): the pipe runs down it, jogs
                    # in under it and on down the wall below
                    W_lo = W0 + nw * (max(lower) + 0.5 * s + 0.003)
                    z_split, jog_mode = z_fbot, "under"
        if nw is None and R["shoe"]:
            # no wall on the line (L1's slope-side pipes drop from the eave onto the deck): strap the pipe to the
            # nearest thing within 12 cm (the deck rail) - it then touches it at that height
            near = None
            z = R["z_bottom"] + 0.1
            while z <= zt - 0.4:
                for k in range(16):
                    d = Vector((math.cos(2 * math.pi * k / 16), math.sin(2 * math.pi * k / 16), 0.0))
                    h = W.cast(Vector((W0.x, W0.y, z)), d, 0.12)
                    if h is not None and (near is None or h[3] < near[0]):
                        near = (h[3], d)
                z += 0.05
            if near is not None and near[0] - 0.5 * s > CONTACT_M:
                W_lo = W_up = W0 + near[1] * (near[0] - 0.5 * s - 0.003)
        elif nw is not None and R["shoe"]:
            # a pipe half past a corner board: slide it along the wall until a wall stands behind its centre
            tw = nw.cross(Vector((0, 0, 1)))
            zs_ = [R["z_bottom"] + 0.2 + (zt - 0.6 - R["z_bottom"]) * k / 15.0 for k in range(16)]

            def behind(Wq):                       # a solid (not glass) face behind the centre over >= 85 % of the run
                ok_ = 0
                for z in zs_:
                    h = W.cast(Vector((Wq.x, Wq.y, z)) - nw * (0.5 * s), -nw, 0.4)
                    if h is not None and not h[2].startswith(GLASS_PREFIXES):
                        ok_ += 1
                return ok_ >= 0.85 * len(zs_)
            if not behind(W_lo):
                for k in range(1, 11):
                    hit = None
                    for sg in (1, -1):
                        if behind(W_lo + tw * (sg * 0.02 * k)):
                            hit = tw * (sg * 0.02 * k)
                            break
                    if hit is not None:
                        W_lo, W_up = W_lo + hit, W_up + hit
                        break
        for Wq, lim in ((W_lo, 0.25 if jog_mode not in ("under", "onto") else 1.5), (W_up, 1.5)):
            if (Wq - W0).length > lim:
                raise RuntimeError(f"fittings {recipe.key}: {R['name']} would stand {(Wq - W0).length:.2f} m off "
                                   f"its line")
        moved = (W_lo - W0).length
        # the neck: from under the gutter back to the (upper) pipe line, below everything in between
        GW = Vector((W_up.x - G.x, W_up.y - G.y, 0.0))
        g = K._Geo()
        if GW.length < 0.03:
            z_pt = z_out
            neck = None
        else:
            zu = []
            for i in range(11):
                p = G + GW * (i / 10.0)
                h = W.cast(Vector((p.x, p.y, z_out - 1.0)), (0, 0, 1), 1.2)
                if h is not None:
                    zu.append(h[0].z)
            z_n0 = min([z_gb - 0.5 * s - 0.02] + [z - 0.5 * s - 0.02 for z in zu])
            z_n1 = z_n0 - max(0.10, GW.length)
            neck = (Vector((G.x, G.y, z_n0)), Vector((W_up.x, W_up.y, z_n1)))
            K._beam_into(g, neck[0], neck[1], s, s)
            g.aabb((G.x - 0.5 * s, G.y - 0.5 * s, z_n0 - 0.5 * s), (G.x + 0.5 * s, G.y + 0.5 * s, z_out),
                   drop=("+Z",))
            z_pt = z_n1 + 0.5 * s
        clips, jog = [], None
        z_lt = z_pt                                    # top of the lower (or only) run
        if z_split is not None:
            d = Vector((W_lo.x - W_up.x, W_lo.y - W_up.y, 0.0)).length
            if jog_mode == "over":
                z_j1 = z_split + 0.06 + 0.5 * s
                z_j0 = z_j1 + max(0.10, d)
            else:
                z_j0 = z_split - 0.06 - 0.5 * s
                z_j1 = z_j0 - max(0.10, d)
            if z_j0 > z_pt - 0.15:
                raise RuntimeError(f"fittings {recipe.key}: {R['name']}: no room for the jog over the feature at "
                                   f"z {z_split:.2f}")
            K._beam_into(g, Vector((W_up.x, W_up.y, z_j0)), Vector((W_lo.x, W_lo.y, z_j1)), s, s)
            g.aabb((W_up.x - 0.5 * s, W_up.y - 0.5 * s, z_j0 - 0.5 * s), (W_up.x + 0.5 * s, W_up.y + 0.5 * s, z_pt))
            _clips(g, W, Vector((W_up.x, W_up.y, 0.0)), nw, s, z_j0, z_pt - 0.05, [], clips)
            z_lt = z_j1 + 0.5 * s
            jog = [jog_mode, round(z_split, 2)]
        # the foot
        sd = Vector(R["shoe_dir"]) if nw is None else Vector(nw)      # the shoe kicks away from its wall ...
        sd = Vector((sd.x, sd.y, 0.0)).normalized()
        Gx = W_lo
        if nw is not None and R["shoe"]:                              # ... or along it, where outward is blocked
            tw = nw.cross(Vector((0, 0, 1)))
            free = None
            for cand in (sd, tw, -tw):
                ok_ = True
                for f in (0.35, 0.7, 1.0):
                    for dz in (0.12, 0.3):
                        q = Vector((Gx.x, Gx.y, R["z_bottom"] + dz)) + cand * (0.22 * f)
                        if W.inside(q) or W.enclosed4(q):
                            ok_ = False
                if ok_:
                    free = sd = cand
                    break
            if free is None:
                R["shoe"] = False                    # no room for a shoe (L5: a stone pier stands in front of the
                R["z_bottom"] = _support_below(W, recipe, Vector((Gx.x, Gx.y, R["z_bottom"] + 0.3)), grade)[0] - 0.01
                K.FIT_SHOES[:] = [e for e in K.FIT_SHOES if e[0] != R["name"]]     # foot): it runs into a drain
        if R["shoe"]:
            # the first thing under the run (a terrace the recipe's foot height ran through), else grade
            z_from = max(R["z_bottom"] + 0.3, z_lt - 0.1)
            sup = [_support_below(W, recipe, Vector((Gx.x, Gx.y, z_from)), grade, 60.0),
                   _support_below(W, recipe, Vector((Gx.x + sd.x * 0.2, Gx.y + sd.y * 0.2, z_from)), grade, 60.0)]
            z_s, kind = max(sup)
            zb = z_s - _shoe_low(sd, s)
            z_pb = zb + K.SHOE[1] - 0.5 * s
            g.aabb((Gx.x - 0.5 * s, Gx.y - 0.5 * s, z_pb), (Gx.x + 0.5 * s, Gx.y + 0.5 * s, z_lt), drop=("-Z",))
            p_out = K._shoe_into(g, Gx.x, Gx.y, zb, sd, s)
            foot = {"z": round(z_s, 3), "on": kind}
            K.FIT_SHOES[:] = [e for e in K.FIT_SHOES if e[0] != R["name"]] + [(R["name"], tuple(p_out), tuple(sd))]
        else:
            z_pb = R["z_bottom"]
            g.aabb((Gx.x - 0.5 * s, Gx.y - 0.5 * s, z_pb), (Gx.x + 0.5 * s, Gx.y + 0.5 * s, z_lt))
            foot = {"z": round(z_pb, 3), "on": "runs on (no shoe)"}
        if nw is not None:
            _clips(g, W, Vector((Gx.x, Gx.y, 0.0)), nw, s, z_pb + 0.05, z_lt - 0.05, [], clips)
        drain = None
        if jog_mode == "onto" and R.get("ledge_drain"):
            drain = _ledge_drain(g, W, recipe, grade, Vector((W_up.x, W_up.y, 0.0)), nw, ledge_z, s, clips)
        _write(o, g)
        if jog_mode == "onto":
            jog = ["onto", round(R["z_bottom"] - 0.05, 2)]
        if drain is not None:
            jog = jog + ["drain to " + drain] if jog else ["drain to " + drain]
        report.append({"pipe": R["name"], "moved_mm": round(moved * 1000, 1), "neck": neck is not None,
                       "nw": None if nw is None else [round(v, 3) for v in nw], "W_lo": [round(W_lo.x, 3),
                       round(W_lo.y, 3)], "W_up": [round(W_up.x, 3), round(W_up.y, 3)], "jog_over_z": jog, "foot": foot, "clips": clips, "gutter": gut[0]["name"] if gut else None})


def _ledge_drain(g, W, recipe, grade, Wu, nw, zl, s, clips):
    """The ledge an upper pipe lands on is a flat roof (L1: the base storey's top; A-Basin's lower storey is a
    flat-roofed deck the A-frame sheds onto): a scupper through its outer edge, a drop under it, a 45-degree neck
    back to the wall below, and a pipe of its own down to grade with clips and a shoe. Returns where it stands."""
    Z = Vector((0.0, 0.0, 1.0))
    zs = zl - 0.06                                         # scupper axis, just under the ledge top
    h = W.cast(Vector((Wu.x, Wu.y, zs)) + nw * 3.0, -nw, 3.0)
    if h is None:
        raise RuntimeError(f"fittings {recipe.key}: no ledge edge in front of the pipe at {tuple(Wu)}")
    E = Vector((h[0].x, h[0].y, 0.0))                      # the ledge's outer face on the pipe's line
    # the spout seats 12 mm into the ledge's face (a declared embed) and stands 12 cm out
    K._beam_into(g, Vector((E.x, E.y, zs)) - nw * 0.012, Vector((E.x, E.y, zs)) + nw * 0.12, s, 0.75 * s)
    Gd = E + nw * (0.12 - 0.5 * s)                         # the drop under the spout's end
    hu = W.cast(Vector((Gd.x, Gd.y, zl - 1.5)), Z, 1.6)
    z_under = hu[0].z if hu is not None else zl - 0.3      # the ledge underside / cap bottom over the drop
    prof = []
    z = grade(Gd.x, Gd.y) + 0.1
    while z <= z_under - 0.15:
        q_ = None
        for t_ in (-0.5 * s, 0.0, 0.5 * s):
            hh = _proud_hit(W, Vector((Gd.x, Gd.y, z)) + nw * 0.8 + nw.cross(Z) * t_, -nw, 2.4)
            if hh is not None:
                q_ = (0.8 - hh) if q_ is None else max(q_, 0.8 - hh)
        if q_ is not None:
            prof.append(q_)
        z += 0.05
    if not prof:
        raise RuntimeError(f"fittings {recipe.key}: no wall under the ledge at {tuple(E)}")
    Wl = Gd + nw * (max(prof) + 0.5 * s + 0.003)
    d = Vector((Wl.x - Gd.x, Wl.y - Gd.y, 0.0)).length
    z_n0 = z_under - 0.5 * s - 0.03
    g.aabb((Gd.x - 0.5 * s, Gd.y - 0.5 * s, z_n0 - 0.5 * s), (Gd.x + 0.5 * s, Gd.y + 0.5 * s, zs),
           drop=("+Z",))
    z_n1 = z_n0 - max(0.10, d)
    K._beam_into(g, Vector((Gd.x, Gd.y, z_n0)), Vector((Wl.x, Wl.y, z_n1)), s, s)
    z_s, kind = _support_below(W, recipe, Vector((Wl.x, Wl.y, z_n1 - 0.2)), grade, 60.0)
    zb = z_s - _shoe_low(nw, s)
    z_pb = zb + K.SHOE[1] - 0.5 * s
    g.aabb((Wl.x - 0.5 * s, Wl.y - 0.5 * s, z_pb), (Wl.x + 0.5 * s, Wl.y + 0.5 * s, z_n1 + 0.5 * s),
           drop=("-Z",))
    K._shoe_into(g, Wl.x, Wl.y, zb, nw, s)
    _clips(g, W, Vector((Wl.x, Wl.y, 0.0)), nw, s, z_pb + 0.05, z_n1 + 0.5 * s - 0.05, [], clips)
    return f"{kind} at {z_s:.2f} m via a scupper"


def clip_runs(recipe, parts, W, report):
    """Clips for the vertical pipe parts built outside downspout() (L2's plinth runs)."""
    routed = {R["name"] for R in K.FIT_PIPES}
    for o, _m in parts:
        if not o.name.startswith("downspout_") or o.name in routed or o.type != "MESH":
            continue
        co, _p = _geo(o)
        lo, hi = co.min(0), co.max(0)
        ext = hi - lo
        if ext[2] <= PIPE_HOLD_M or max(ext[0], ext[1]) > 0.25:
            continue
        pc = Vector((0.5 * (lo[0] + hi[0]), 0.5 * (lo[1] + hi[1]), 0.0))
        wd = _wall_dir(W, Vector((pc.x, pc.y, 0.0)), 0.6, float(lo[2]) + 0.1, float(hi[2]) - 0.1)
        if wd is None:
            continue
        g = K._Geo()
        g.bm.from_mesh(o.data)
        clips = []
        size = float(min(ext[0], ext[1]))
        _clips(g, W, pc, wd[0], size, float(lo[2]) + 0.05, float(hi[2]) - 0.05, [float(lo[2]), float(hi[2])],
               clips)
        _write(o, g)
        report.append({"pipe": o.name, "clips": clips, "run_on": True})


def closelook(recipe, parts, opts):
    """The close-look fix pass (gutters, downpipes, clips). Returns its report."""
    W = _World(parts)
    rep = {"gutters": [], "pipes": []}
    LAST_FIX.clear()
    LAST_FIX.update(rep)
    runs = refit_gutters(recipe, parts, W, rep["gutters"])
    route_downspouts(recipe, parts, W, runs, _grade_fn(recipe), rep["pipes"])
    clip_runs(recipe, parts, W, rep["pipes"])
    return rep


# =====================================================================================================================
# STRUCTURE (T58, 2026-10-18; Richard's Blender review: "a pass on all buildings for what is structurally unrealistic";
# docs/research/art/structure-2026-10-18.md): the fittings close look extended to every DECLARED STRUCTURAL PART.
# Same frame and units as the fittings audit (authoring frame, before yaw; metres; 5 mm contact). A part is declared
# by its name (`struct_role`: prefix / suffix rules, plus STRUCT_DECLARE per recipe for the exceptions, each with a
# reason) and split into connected shells (a boxes_part of six posts = six posts). Per role:
#   post   (post_, col_, logpost_, stilt_, gazebocol_, lanterncol_, logframe_, the kit rails' `trail_<n>p`; pier_ /
#          footing_ / step_ / stairblk_ blocks): the FOOT is carried: the median foot point (the outline at the
#          lowest 1 cm, every 2 cm) is <= 5 mm over the first surface straight below it, buried in another part, or
#          on grade (the lifted grade plane / the plinth recipe's design terrain; below grade = buried); and the TOP
#          (highest 1 cm) touches what it carries within 5 mm, unless the part is declared free-topped (a mast).
#   member (beam_, strut_, truss_, joist_, logplate_, rail_, the kit rails' `trail_<n>r` / `cap`, stair stringers
#          `stair_<n>_str`; a post that is not vertical): BOTH ENDS land (an end = the last 15 % of its length along
#          its main axis, 5 cm .. 60 cm): some end point within 5 mm of another part or inside one, or on grade;
#          a stringer's LOWER end like a post foot (carried from below). tail_ (rafter tails) are cantilevers: one end.
#   bracket (bracket_): touches >= 2 distinct things (the wall and the load) within 5 mm.
#   slab   (balc_, deck_, floor_, carpetdeck_, a canopy roof_*canopy*): touches a carrier within 5 mm (a post, a
#          bracket, a beam, a wall it penetrates, a pier, grade) - its own railing / parapet does not count.
#   held   (stair treads, glassrail_, balcwall_, kick_, mast_, the rest): touches anything structural within 5 mm.
# Fittings never carry structure (they are not in the world). The gate RAISES (STRUCT_AUDIT_MODE "raise") on every
# `lodge.py --structure v1` build; "report" only records (the before-table).
# =====================================================================================================================
STRUCT_VERSION = 1
STRUCT_AUDIT_MODE = "raise"          # "raise" | "report"
LAST_STRUCT = {}
STRUCT_END = (0.15, 0.05, 0.60)      # end window: fraction of the length, min, max (m)
STRUCT_BAND = 0.01                   # foot / top band (m)
STRUCT_STEP = 0.02                   # edge sampling inside the bands / windows (m)
LOAD_SEAT_M = 0.10                   # a bracket's top may be seated this deep into the load it carries
STRUCT_RULES = (
    # (role, prefixes, excluded prefixes)
    ("slab", ("balc_", "deck_", "floor_", "carpetdeck_"), ("balcwall_", "deckfascia_")),
    ("bracket", ("bracket_",), ()),
    ("railpost", ("post_",), ()),
    ("post", ("col_", "logpost_", "stilt_", "gazebocol_", "lanterncol_", "logframe_", "pier_",
              "footing_", "step_", "stairblk_", "balcpost_"), ()),
    ("member", ("beam_", "strut_", "truss_", "trim_truss", "joist_", "logplate_", "rail_", "tail_"), ()),
    ("held", ("glassrail_", "balcwall_", "kick_", "mast_", "trsplate_", "galleryrib_"), ()),
    ("post", ("flag_",), ()),
)
CANTILEVER = ("tail_",)              # rafter tails: one end in the eave, the other free by design
FREE_TOP = ("mast_", "flag_", "step_", "stairblk_", "footing_")   # a mast's top is free; a step / footing top is walked on
# a rail post may be bolted to the SIDE of what carries it (a stringer, a deck edge, a fascia, a cap): its lower
# 40 % touches one of these within 5 mm (never a wall: a post hanging on a wall with its foot in air is the defect)
BEARING = ("step_", "stairblk_", "pier_", "plinth_", "plinthcap_", "footing_")   # a stringer end may bear on these
SIDE_MOUNT = ("stair_", "deck_", "deckfascia_", "balc_", "plinthcap_", "step_", "stairblk_", "beam_", "joist_",
              "floor_", "carpet")
# per recipe: {part name or prefix: (role | "skip", reason)} - the declared exceptions (and the tags a recipe adds)
STRUCT_DECLARE = {
    "P2": {"logpost_": ("railpost", "the deck rail's log newels: the steel bars run through them, the tops are free")},
    "L4": {"trim_dposts": ("post", "the cantilever deck's glulam posts (unbevelled, so renamed trim_)")},
    "L5": {"trim_twpost": ("railpost", "the tower deck parapet's posts (unbevelled, so renamed trim_)"),
           "trim_twcap": ("member", "the tower deck parapet's cap rail")},
}
# never structure (dressing, signs, lamps, fittings); checked before the rules
STRUCT_SKIP = ("deckfascia_", "lamp_", "sign_", "flagpanel_", "emblem_", "bench_", "skirack_", "tobrack_", "pole_", "bar_", "wood_")


def struct_role(name, key=None):
    """The structural role of part `name` (None = not structure)."""
    if _is_fit(name):
        return None
    for pre, (role, _why) in sorted(STRUCT_DECLARE.get(key, {}).items(), key=lambda kv: -len(kv[0])):
        if name == pre or name.startswith(pre):
            return None if role == "skip" else role
    if name.startswith(STRUCT_SKIP):
        return None
    if name.startswith("trail_"):
        if name.endswith("cap") or name.endswith("r"):
            return "member"
        if name.endswith("p"):
            return "railpost"
        return "held"
    if name.startswith("stair_"):
        return "member" if "_str" in name else "held"
    if name.startswith("roof_") and "canopy" in name:
        return "slab"
    for role, pres, excl in STRUCT_RULES:
        if name.startswith(pres) and not name.startswith(excl):
            return role
    return None


def _face_pts(co, polys, step):
    """Points every `step` over every polygon's interior (fan triangles, barycentric grid): a wide face resting on a
    thin support (a rail cap on its glass panel) touches it away from its edges."""
    out = []
    for p in polys:
        for i in range(1, len(p) - 1):
            A, B, C = co[p[0]], co[p[i]], co[p[i + 1]]
            n = int(max(np.linalg.norm(B - A), np.linalg.norm(C - A), np.linalg.norm(C - B)) / step) + 1
            if n < 2:
                continue
            ij = np.array([(a, b) for a in range(n + 1) for b in range(n + 1 - a)], dtype=float) / n
            out.append(A + np.outer(ij[:, 0], B - A) + np.outer(ij[:, 1], C - A))
    return np.vstack(out) if out else np.zeros((0, 3))


def _edge_pts(co, polys, step):
    """Vertices + points every `step` along every edge (numpy)."""
    E = set()
    for p in polys:
        for i in range(len(p)):
            a, b = p[i], p[(i + 1) % len(p)]
            E.add((a, b) if a < b else (b, a))
    out = [co]
    if E:
        A = np.array([co[a] for a, _b in E])
        B = np.array([co[b] for _a, b in E])
        L = np.linalg.norm(B - A, axis=1)
        n = np.maximum(1, np.ceil(L / step)).astype(int)
        for k in range(1, int(n.max())):
            m = n > k
            t = (k / n[m])[:, None]
            out.append(A[m] + (B[m] - A[m]) * t)
    return np.vstack(out)


class _SWorld:
    """Every non-fitting part as one BVH; polygon owner = (part, shell id)."""

    def __init__(self, geo, shell_of):
        verts, polys, self.owner = [], [], []
        self.names, plo, phi, self.geo, self._pb = [], [], [], [], {}
        for nm, (co, ps) in geo.items():
            if _is_fit(nm):
                continue
            sid = shell_of.get(nm)
            base = len(verts)
            verts += [tuple(c) for c in co]
            polys += [tuple(base + v for v in p) for p in ps]
            self.owner += [(nm, sid[i] if sid is not None else 0) for i in range(len(ps))]
            self.names.append(nm)
            plo.append(co.min(0))
            phi.append(co.max(0))
            self.geo.append((co, ps))
            self.sid = getattr(self, "sid", {})
            self.sid[nm] = sid
        self.bvh = BVHTree.FromPolygons(verts, polys, all_triangles=False)
        self.plo, self.phi = np.array(plo), np.array(phi)

    def near(self, p, me, r):
        """[(dist, owner)] of the non-self polygons within r of p."""
        out = []
        for _loc, _n, idx, d in self.bvh.find_nearest_range(Vector(tuple(p)), r):
            ow = self.owner[idx]
            if ow != me:
                out.append((d, ow))
        return out

    def seat(self, p, me):
        """(gap, owner) of the nearest non-self UPWARD face at or under p within CONTACT_M (a foot resting on it), or
        None (an open-bottom slab has no underside for the down ray to meet)."""
        best = None
        for loc, nrm, idx, d in self.bvh.find_nearest_range(Vector(tuple(p)), CONTACT_M):
            ow = self.owner[idx]
            if ow != me and nrm.z > 0.5 and loc.z <= float(p[2]) + 1e-4 and (best is None or d < best[0]):
                best = (d, ow)
        return best

    def under(self, p, me):
        """Owners of the non-self DOWNWARD faces at most CONTACT_M over p or LOAD_SEAT_M under it (what rests on p:
        a bracket top seated into the slab it carries)."""
        out = set()
        for loc, nrm, idx, _d in self.bvh.find_nearest_range(Vector(tuple(p)), CONTACT_M + LOAD_SEAT_M):
            ow = self.owner[idx]
            # a downward face just over the point, or one the top is seated into (<= LOAD_SEAT_M)
            if ow != me and nrm.z < -0.5 and float(p[2]) - LOAD_SEAT_M <= loc.z <= float(p[2]) + CONTACT_M:
                out.add(ow[0])
        return out

    def inside_other(self, p, me_part, me_shell=None):
        """The part p lies inside (not me_part; with me_shell, the OTHER shells of me_part count: a truss's brace
        end buried in its own tie beam)."""
        q = np.asarray(tuple(p))
        for i in np.where(np.all(self.plo - 1e-4 <= q, 1) & np.all(q <= self.phi + 1e-4, 1))[0]:
            key = i
            if self.names[i] == me_part:
                sid = self.sid.get(me_part)
                if me_shell is None or sid is None:
                    continue
                key = ("shells", i)
                if key not in self._pb:            # one BVH per OTHER shell (a combined one breaks the ray test)
                    co, ps = self.geo[i]
                    by = {}
                    for p_, s_ in zip(ps, sid):
                        by.setdefault(s_, []).append(p_)
                    lst = []
                    for s_, pl in sorted(by.items()):
                        vv = co[sorted({v for p_ in pl for v in p_})]
                        lst.append((s_, vv.min(0), vv.max(0),
                                    BVHTree.FromPolygons([tuple(c) for c in co], pl, all_triangles=False)))
                    self._pb[key] = lst
                for s_, lo_, hi_, bv in self._pb[key]:
                    if s_ == me_shell or np.any(q < lo_ - 1e-4) or np.any(q > hi_ + 1e-4):
                        continue
                    if _inside(Vector(tuple(p)), bv, 200.0):
                        return self.names[i]
                continue
            if key not in self._pb:
                co, ps = self.geo[i]
                self._pb[key] = BVHTree.FromPolygons([tuple(c) for c in co], ps, all_triangles=False)
            if _inside(Vector(tuple(p)), self._pb[key], 200.0):
                return self.names[i]
        return None

    def embedded(self, p, me, reach=0.10):
        """A part whose box holds p and two of whose faces p lies just BEHIND (two axis rays meet them from behind
        within `reach`): an end seated in an open-shelled pier / wall (a part with dropped faces fails _inside)."""
        q = np.asarray(tuple(p))
        for i in np.where(np.all(self.plo - 1e-4 <= q, 1) & np.all(q <= self.phi + 1e-4, 1))[0]:
            if self.names[i] == me[0]:
                continue
            if i not in self._pb:
                co, ps = self.geo[i]
                self._pb[i] = BVHTree.FromPolygons([tuple(c) for c in co], ps, all_triangles=False)
            back = 0
            for d in _DIRS6:
                r = self._pb[i].ray_cast(Vector(tuple(p)), d, reach)
                if r[0] is not None and r[1].dot(d) > 0.0:
                    back += 1
            if back >= 2:                          # one back face alone is a curved sheet's neighbour facet
                return self.names[i]
        return None

    def down(self, p, me, reach=60.0):
        """(gap, owner) of the first non-self surface straight below p."""
        o = Vector((float(p[0]), float(p[1]), float(p[2]) - 1e-4))
        d = Vector((0.0, 0.0, -1.0))
        gone = 0.0
        for _k in range(8):
            loc, nrm, idx, t = self.bvh.ray_cast(o, d, reach - gone)
            if loc is None:
                return None
            if self.owner[idx] != me:
                if nrm.z < -0.5:                       # met an underside from behind: p is inside that part
                    return 0.0, self.owner[idx]
                return gone + t + 1e-4, self.owner[idx]
            gone += t + 1e-4
            o = loc + d * 1e-4
        return None


def _inset(pts, d=0.002):
    """Foot points moved d toward their centroid in plan (off the outline a flush support shares)."""
    c = pts[:, :2].mean(0)
    v = c - pts[:, :2]
    n = np.linalg.norm(v, axis=1)[:, None]
    out = pts.copy()
    out[:, :2] += v * np.minimum(1.0, d / np.maximum(n, 1e-9))
    return out


def _foot_gap(W, recipe, grade, p, me):
    """Gap (m) under a foot point and what carries it: buried in another part / grade -> 0."""
    g = _ground_gap(recipe, grade, p)
    if g <= CONTACT_M:
        return 0.0, "grade"
    st = W.seat(p, me)
    if st is not None:
        return st[0], st[1][0]
    q = (float(p[0]), float(p[1]), float(p[2]) - 0.002)
    inn = W.inside_other(q, me[0], me[1])
    if inn:
        return 0.0, inn
    h = W.down(p, me)
    if h is not None and h[0] < g:
        return h[0], h[1][0]
    return g, "grade"


def _landed(W, recipe, grade, pts, me):
    """(gap m, by) of an end / top: the closest non-self surface over the points; inside another part or on grade
    = 0."""
    best, by = 9.0, None
    for p in pts:
        if _ground_gap(recipe, grade, p) <= CONTACT_M:
            return 0.0, "grade"
        nb = W.near(p, me, CONTACT_M)
        if nb:
            m = min(nb)
            return m[0], m[1][0]
    for p in pts[:: max(1, len(pts) // 24)]:
        inn = W.inside_other(p, me[0], me[1])
        if inn:
            return 0.0, inn
    for p in pts[:: max(1, len(pts) // 24)]:
        emb = W.embedded(p, me)
        if emb:
            return 0.0, emb
    for p in pts[:: max(1, len(pts) // 12)]:
        nb = W.near(p, me, 2.0)
        if nb and min(nb)[0] < best:
            best, by = min(nb)[0], min(nb)[1][0]
    return best, by


def audit_structure(recipe, parts):
    """The T58 structure audit (see the block comment). Returns {"shells": [...], "violations": [...], ...}."""
    bpy.context.view_layer.update()            # kit rail posts carry an object transform (stale matrix_world)
    key = recipe.key
    geo = {o.name: _geo(o) for o, _m in parts if o.type == "MESH" and len(o.data.polygons)}
    grade = _grade_fn(recipe)
    roles, shell_of, shells = {}, {}, []
    for nm in sorted(geo):
        r = struct_role(nm, key)
        if r is None:
            continue
        roles[nm] = r
        co, ps = geo[nm]
        groups = _shells(co, ps)
        idx = {p: k for k, sp in enumerate(groups) for p in sp}
        shell_of[nm] = [idx[p] for p in ps]
        for k, sp in enumerate(groups):
            vi = sorted({v for p in sp for v in p})
            remap = {v: i for i, v in enumerate(vi)}
            shells.append((nm, k, co[vi], [tuple(remap[v] for v in p) for p in sp]))
    W = _SWorld(geo, shell_of)
    viol, rec, brk = [], [], {}

    def fin(p):
        return [round(v, 3) for v in auth_to_final(recipe, tuple(float(x) for x in p))]
    for nm, k, co, ps in shells:
        role = roles[nm]
        me = (nm, k)
        if role in ("post", "railpost") and float(co[:, 2].min()) > float(geo[nm][0][:, 2].min()) + 0.5                 and float(np.ptp(co[:, 2])) < 0.5:
            role = "held"                          # a finial / cap shell on top of its post
        name = f"{nm}#{k}"
        lo, hi = co.min(0), co.max(0)
        ext = hi - lo
        c = co.mean(0)
        ev, evec = np.linalg.eigh(np.cov((co - c).T) + np.eye(3) * 1e-12)
        ax = evec[:, int(np.argmax(ev))]
        srt = np.sort(ev)[::-1]
        elong = math.sqrt(max(srt[0], 1e-12) / max(srt[1], 1e-12))
        vertical = abs(ax[2]) >= 0.9 or (elong < 3.0 and ext[2] >= max(ext[0], ext[1]))
        pts = _edge_pts(co, ps, STRUCT_STEP)
        if role in ("member", "post", "railpost"):     # ends / tops / feet also sample the faces' interiors
            pts = np.vstack([pts, _face_pts(co, ps, 0.04)])
        r = {"shell": name, "role": role, "final_c": fin((lo + hi) / 2), "size": [round(float(v), 3) for v in ext]}
        bad = []
        # every shell: touches something (or grade): a crossing first (BVH overlap), else the nearest surface
        sb = BVHTree.FromPolygons([tuple(v) for v in co], ps, all_triangles=False)
        ovo = {W.owner[i] for i, _j in W.bvh.overlap(sb) if W.owner[i] != me}
        ov = sorted({o_[0] for o_ in ovo})
        if ov:
            held, hb = 0.0, ov[0]
        else:
            held, hb = _landed(W, recipe, grade, pts, me)          # every point: it returns at the first contact
        r["held_mm"], r["held_by"] = round(held * 1000, 1), hb
        if held > CONTACT_M:
            bad.append(("contact", held, f"touches nothing: {held * 1000:.0f} mm from {hb or 'anything'}", c))
        elif role in ("post", "railpost") and (vertical or elong < 3.0):
            foot = _inset(pts[pts[:, 2] <= lo[2] + STRUCT_BAND])
            gaps = sorted(_foot_gap(W, recipe, grade, p, me) for p in foot)
            med = gaps[len(gaps) // 2]
            r["foot_mm"], r["foot_on"] = round(med[0] * 1000, 1), med[1]
            if med[0] > CONTACT_M and role in ("railpost", "post"):
                low = pts[pts[:, 2] <= lo[2] + 0.4 * max(float(ext[2]), 0.05)]
                for p in low[:: max(1, len(low) // 60)]:
                    sm = [ow[0] for _d, ow in W.near(p, me, CONTACT_M) if ow[0].startswith(SIDE_MOUNT)]
                    if sm:
                        r["foot_mm"], r["foot_on"] = 0.0, "side:" + sm[0]
                        med = (0.0, "side:" + sm[0])
                        break
            if med[0] > CONTACT_M:
                bad.append(("foot", med[0], f"foot {med[0] * 1000:.0f} mm over {med[1]} (median of {len(foot)} "
                                            f"foot points)", foot.mean(0)))
            if role == "post" and not nm.startswith(FREE_TOP):
                top = pts[pts[:, 2] >= hi[2] - STRUCT_BAND]
                g, by = _landed(W, recipe, grade, top, me)
                r["top_mm"], r["top_on"] = round(g * 1000, 1), by
                if g > CONTACT_M:
                    bad.append(("top", g, f"top ends in air: {g * 1000:.0f} mm from {by or 'anything'}",
                                top.mean(0)))
        elif role in ("member", "post", "railpost"):
            t = (pts - c) @ ax
            L = float(t.max() - t.min())
            w = min(STRUCT_END[2], max(STRUCT_END[1], STRUCT_END[0] * L))
            ends = [pts[t <= t.min() + w], pts[t >= t.max() - w]]
            ends.sort(key=lambda e: round(float(e[:, 2].mean()), 2))   # level ends: t-min first (stable)
            gs = []
            for j, e in enumerate(ends):
                if j == 0 and "_str" in nm and float(ext[2]) > 0.3:     # a stringer's lower end: carried from below
                    foot = _inset(e[e[:, 2] <= e[:, 2].min() + STRUCT_BAND])
                    gg = sorted(_foot_gap(W, recipe, grade, p, me) for p in foot)
                    g0 = gg[len(gg) // 2]
                    if g0[0] > CONTACT_M:              # or its end bears on a landing / slab edge (a hanger)
                        for p in e[:: max(1, len(e) // 80)]:
                            bear = [ow[0] for _d, ow in W.near(p, me, CONTACT_M)
                                    if struct_role(ow[0], key) == "slab" or ow[0].startswith(BEARING)]
                            if bear:
                                g0 = (0.0, "edge:" + bear[0])
                                break
                    gs.append(g0)
                else:
                    gs.append(_landed(W, recipe, grade, e, me))
            r["ends_mm"] = [round(g * 1000, 1) for g, _b in gs]
            r["ends_on"] = [b for _g, b in gs]
            n_bad = [j for j, (g, _b) in enumerate(gs) if g > CONTACT_M]
            if nm.startswith(CANTILEVER):
                n_bad = n_bad if len(n_bad) == 2 else []
            for j in n_bad:
                g, by = gs[j]
                bad.append(("end", g, f"{'lower' if j == 0 else 'upper'} end in air: {g * 1000:.0f} mm from "
                                      f"{by or 'anything'} (L {L:.2f} m)", ends[j].mean(0)))
        elif role == "bracket":                    # judged per bracket UNIT (arm + strut ...) after the loop
            sup = set(ovo)
            for p in pts[:: max(1, len(pts) // 300)]:
                for _d, ow in W.near(p, me, CONTACT_M):
                    sup.add(ow)
                if _ground_gap(recipe, grade, p) <= CONTACT_M:
                    sup.add(("grade", 0))
            load = set()
            tb = pts[pts[:, 2] >= hi[2] - STRUCT_BAND]
            for p in tb[:: max(1, len(tb) // 120)]:
                load |= W.under(p, me)
            r["load"] = sorted(load)
            brk.setdefault(nm, []).append((k, sup, c, r, load))
        elif role == "slab":
            sup = {o_[0] for o_ in ovo if not (struct_role(o_[0], key) == "held" or o_[0].startswith(
                ("trail_", "rail_", "post_", "kick_", "glassrail_", "balcwall_")))}
            for p in pts[:: max(1, len(pts) // 300)]:
                for _d, ow in W.near(p, me, CONTACT_M):
                    if struct_role(ow[0], key) == "held" or ow[0].startswith(("trail_", "rail_", "post_", "kick_",
                                                                              "glassrail_", "balcwall_")):
                        continue
                    sup.add(ow[0])
                if _ground_gap(recipe, grade, p) <= CONTACT_M:
                    sup.add("grade")
            r["carried_by"] = sorted(sup)[:8]
            if not sup:
                bad.append(("slab", 0.0, "carried by nothing but its own railing", c))
        r["violations"] = [(chk, round(g * 1000, 1), d, fin(at)) for chk, g, d, at in bad]
        rec.append(r)
        for chk, g, d, at in bad:
            viol.append((chk, name, round(g * 1000, 1), f"{d} at {fin(at)}"))
    # brackets: shells of one part that touch each other are one bracket; it touches >= 2 things outside itself
    for nm, lst in sorted(brk.items()):
        par = {k: k for k, _s, _c, _r, _l in lst}

        def find(a):
            while par[a] != a:
                a = par[a]
            return a
        for k, sup, _c, _r, _l in lst:
            for ow in sup:
                if ow[0] == nm and ow[1] in par:
                    par[find(ow[1])] = find(k)
        units = {}
        for k, sup, c, r, ld in lst:
            units.setdefault(find(k), []).append((k, sup, c, r, ld))
        for _u, mem in sorted(units.items()):
            ext = sorted({ow[0] for _k, sup, _c, _r, _l in mem for ow in sup if ow[0] != nm})
            load = sorted({x for _k, _s, _c, _r, ld in mem for x in ld if x != nm})
            for _k, _s, _c, r, _l in mem:
                r["carries"] = ext
            if len(ext) < 2 or not load:
                k0, _s, c0, r0, _l = mem[0]
                d = (f"bracket ({len(mem)} member(s)) touches {len(ext)} thing(s) {ext}, carries "
                     f"{load or 'nothing on its top'}: it needs the wall and its load")
                r0["violations"].append(("bracket", 0.0, d, fin(c0)))
                viol.append(("bracket", f"{nm}#{k0}", 0.0, f"{d} at {fin(c0)}"))
    out = {"key": key, "version": STRUCT_VERSION, "n_shells": len(shells), "parts": len(roles),
           "roles": {k: sum(1 for v in roles.values() if v == k) for k in sorted(set(roles.values()))},
           "shells": rec, "violations": viol}
    LAST_STRUCT.clear()
    LAST_STRUCT.update(out)
    if viol and STRUCT_AUDIT_MODE == "raise":
        lines = "; ".join(f"{c_} {n_} {v_} mm: {d_}" for c_, n_, v_, d_ in viol[:12])
        raise RuntimeError(f"structure {key}: {len(viol)} violation(s) (T58 gate, cs2_fittings.audit_structure): "
                           f"{lines}")
    return out


# ---------------------------------------------------------------------------------------------------------------------
# NOTHING CROSSES GLASS (structure v1 round 2, Richard 2026-10-18 on the L5 posts preview: "I still see some brackets
# going into the bottom window"): no structural part (bracket, post, rail post, member: struts, beams, trusses,
# braces) may overlap a window opening in the wall's plane. A window opening = a glass pane part (`win_`, one quad
# 5 cm proud of its wall, facing out); "in the wall's plane" = from GLASS_BEHIND_M behind the pane (a member
# pocketed into the wall over a window) to GLASS_REACH_M in front of it (a bracket / strut / brace on the wall face;
# the L5 check_gable_braces / pane-ray reach). A sample of the part's edges and faces strictly inside the pane's
# outline (GLASS_TOL_M) in that band is a violation. Free-standing posts further out (balcony posts 1.4 m out, deck
# posts) and guard rails / stair stringers / rafter tails (GLASS_FREE) are not judged. Runs on every
# `lodge.py --structure v1` build after audit_structure; RAISES in STRUCT_AUDIT_MODE "raise".
# ---------------------------------------------------------------------------------------------------------------------
GLASS_PANES = ("win_",)
GLASS_REACH_M = 0.60
GLASS_BEHIND_M = 0.10
GLASS_TOL_M = 0.005
GLASS_ROLES = ("bracket", "post", "railpost", "member")
GLASS_FREE = ("rail_", "trail_", "stair_", "tail_")
# per recipe: {part prefix: (pane prefix it may stand in front of, reason)} - DECLARED overlaps, reported under
# "declared" (never silently passed); anything else over glass is a violation. Both are glass WALLS with their own
# timber structure in front, not a bracket / strut hanging over a punched window.
GLASS_DECLARE = {
    "L4": {"trim_dposts": ("win_d", "the dining glass box's glulam posts stand on its curtain's mullion lines "
                                    "(0.30 wide over a mullion: 11 cm of each neighbouring pane's edge); they are "
                                    "the glass box's structure"),
           "trim_trussd": ("win_dgab", "the dining gable's feature king-post truss 0.75 m in front of its glazed "
                                       "gable (the L5 hall idiom)")},
    "L5": {"truss_h": ("win_h", "the great hall's feature truss 0.75 m in front of its glazed gable / upper "
                                "curtain (the hall's design idiom; recipe PANE_BLOCKERS note)")},
}
LAST_GLASS = {}


def audit_glass(recipe, parts):
    """The round-2 glass gate (see the block comment). Returns {"panes", "shells", "violations": [(part#shell,
    pane, depth mm, final-frame centre of the overlap)]}."""
    bpy.context.view_layer.update()
    key = recipe.key
    panes, shells = [], []
    for o, _m in parts:
        if o.type != "MESH" or not len(o.data.polygons):
            continue
        nm = o.name
        if nm.startswith(GLASS_PANES):
            co, ps = _geo(o)
            p0 = co[list(ps[0])]
            n = np.cross(p0[1] - p0[0], p0[2] - p0[0])
            if np.linalg.norm(n) < 1e-12:
                continue
            n = n / np.linalg.norm(n)
            M = np.array(o.matrix_world)[:3, :3]
            ln = M @ np.array(o.data.polygons[0].normal, dtype=np.float64)   # the pane's own facing (outward)
            if float(n @ ln) < 0.0:
                n = -n
            e1 = np.cross(n, (0.0, 0.0, 1.0))
            e1 = e1 / np.linalg.norm(e1) if np.linalg.norm(e1) > 1e-6 else np.array((1.0, 0.0, 0.0))
            e2 = np.cross(n, e1)
            uv = np.stack([co @ e1, co @ e2], 1)
            d0 = float((co @ n).mean())
            band = np.vstack([co + n * GLASS_REACH_M, co - n * GLASS_BEHIND_M])
            panes.append((nm, n, e1, e2, uv.min(0) + GLASS_TOL_M, uv.max(0) - GLASS_TOL_M, d0,
                          band.min(0), band.max(0)))
            continue
        role = struct_role(nm, key)
        if role not in GLASS_ROLES or nm.startswith(GLASS_FREE):
            continue
        co, ps = _geo(o)
        for k, sp in enumerate(_shells(co, ps)):
            vi = sorted({v for p in sp for v in p})
            remap = {v: i for i, v in enumerate(vi)}
            sc, sps = co[vi], [tuple(remap[v] for v in p) for p in sp]
            pts = np.vstack([_edge_pts(sc, sps, STRUCT_STEP), _face_pts(sc, sps, 0.04)])
            shells.append((f"{nm}#{k}", role, pts, pts.min(0), pts.max(0)))
    viol, declared = [], []
    decl = GLASS_DECLARE.get(key, {})
    for sn, role, pts, lo, hi in shells:
        for pn, n, e1, e2, uvlo, uvhi, d0, blo, bhi in panes:
            if np.any(hi < blo) or np.any(lo > bhi):
                continue
            dep = pts @ n - d0
            u, v = pts @ e1, pts @ e2
            m = ((dep >= -GLASS_BEHIND_M) & (dep <= GLASS_REACH_M) & (u > uvlo[0]) & (u < uvhi[0])
                 & (v > uvlo[1]) & (v < uvhi[1]))
            if m.any():
                at = pts[m].mean(0)
                rec = (sn, pn, round(float(dep[m].min()) * 1000, 1),
                       [round(x, 3) for x in auth_to_final(recipe, tuple(float(x) for x in at))])
                d = [pre for pre, (pp, _why) in decl.items() if sn.startswith(pre) and pn.startswith(pp)]
                (declared if d else viol).append(rec)
    out = {"key": key, "panes": len(panes), "shells": len(shells), "violations": viol, "declared": declared}
    LAST_GLASS.clear()
    LAST_GLASS.update(out)
    if viol and STRUCT_AUDIT_MODE == "raise":
        lines = "; ".join(f"{s_} over {p_} ({d_} mm off the pane) at {a_}" for s_, p_, d_, a_ in viol[:12])
        raise RuntimeError(f"glass {key}: {len(viol)} structural part(s) over a window opening (round-2 gate, "
                           f"cs2_fittings.audit_glass): {lines}")
    return out


# ---------------------------------------------------------------------------------------------------------------------
# STRUCTURE FIX PASS (T58; run by build() before the fittings close look on every `--structure v1` build). The kit and
# the owned recipes fix their own structure at the source (cs2_bldg.bracket into its wall, L2 / L5 recipe changes);
# this pass closes the SMALL gaps the audit finds on any building, generically, and reports each move:
#   end      a member end <= SNAP_END_M short of what it meets along its own axis: the end face runs on to it
#            (+ STRUCT_SEAT_M); a stringer's lower end runs down onto the first surface below
#   foot     a post / rail post foot <= SNAP_FOOT_M over the first surface straight below: the foot runs down to it
#   contact  a shell <= SNAP_SEAT_M from everything (a strip, a rail cap piece): it moves onto the nearest surface
#   bracket  a bracket with nothing on its top and a downward face <= SNAP_LOAD_M over it: it moves up under it
# Anything larger is a design defect for the recipe and stays a violation (the gate then raises).
# ---------------------------------------------------------------------------------------------------------------------
SNAP_END_M = 0.08
SNAP_FOOT_M = 0.60
SNAP_SEAT_M = 0.03
SNAP_LOAD_M = 0.40
STRUCT_SEAT_M = 0.003
LAST_STRUCT_FIX = {}


def _move_verts(o, idx, d):
    """Move mesh vertices `idx` of part o by the world vector d (the part may carry an object transform)."""
    M = o.matrix_world
    Mi = M.inverted()
    D = Vector(tuple(float(x) for x in d))
    for i in idx:
        v = o.data.vertices[int(i)]
        v.co = Mi @ (M @ v.co + D)
    o.data.update()


def _board_sides(o, quad_vi, d):
    """Close a single quad into a board: four side faces from its edges to the copy of its verts moved by the world
    vector d (the copy face itself is not built: it lies in the surface the board sits on)."""
    M = o.matrix_world
    Mi = M.inverted()
    D = Vector(tuple(float(x) for x in d))
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bm.verts.ensure_lookup_table()
    top = [bm.verts[int(i)] for i in quad_vi]
    low = [bm.verts.new(Mi @ (M @ v.co + D)) for v in top]
    c = sum((v.co for v in top), Vector()) / 4.0
    for i in range(4):
        a, b = top[i], top[(i + 1) % 4]
        f = bm.faces.new((a, b, low[(i + 1) % 4], low[i]))
        mid = (a.co + b.co) * 0.5
        f.normal_update()
        if f.normal.dot(mid - c) < 0.0:                # sides face out of the board
            f.normal_flip()
    bm.to_mesh(o.data)
    bm.free()
    o.data.update()


def _shell_verts(o, k):
    """(world co of the part, the shell's vertex indices, the shell's polys remapped to those indices)."""
    co, ps = _geo(o)
    groups = _shells(co, ps)
    vi = sorted({v for p in groups[k] for v in p})
    remap = {v: i for i, v in enumerate(vi)}
    return co, np.array(vi, dtype=int), [tuple(remap[v] for v in p) for p in groups[k]]


def _ray_hit(W, me, org, d, reach):
    """First non-self hit (distance) from org along d within reach (skipping the shell's own faces)."""
    o = Vector(tuple(float(x) for x in org))
    dv = Vector(tuple(float(x) for x in d)).normalized()
    gone = 0.0
    for _k in range(8):
        loc, _n, idx, t = W.bvh.ray_cast(o, dv, reach - gone)
        if loc is None:
            return None
        if W.owner[idx] != me:
            return gone + t
        gone += t + 1e-5
        o = loc + dv * 1e-5
    return None


def structure_fix(recipe, parts):
    """The generic snap pass (see the block comment). Returns the list of moves."""
    global STRUCT_AUDIT_MODE
    mode, STRUCT_AUDIT_MODE = STRUCT_AUDIT_MODE, "report"
    try:
        a = audit_structure(recipe, parts)
    finally:
        STRUCT_AUDIT_MODE = mode
    by = {o.name: o for o, _m in parts}
    geo = {o.name: _geo(o) for o, _m in parts if o.type == "MESH" and len(o.data.polygons)}
    shell_of = {}
    for nm in geo:
        if struct_role(nm, recipe.key) is not None:
            co, ps = geo[nm]
            groups = _shells(co, ps)
            idx = {p: k for k, sp in enumerate(groups) for p in sp}
            shell_of[nm] = [idx[p] for p in ps]
    W = _SWorld(geo, shell_of)
    moves = []
    done = set()
    for chk, name, mm, detail in sorted(a["violations"]):
        nm, k = name.rsplit("#", 1)
        k = int(k)
        if (nm, k, chk) in done or nm not in by:
            continue
        done.add((nm, k, chk))
        o = by[nm]
        co, vi, sps = _shell_verts(o, k)
        sc = co[vi]
        me = (nm, k)
        if chk == "end":
            c = sc.mean(0)
            ev, evec = np.linalg.eigh(np.cov((sc - c).T) + np.eye(3) * 1e-12)
            ax = evec[:, int(np.argmax(ev))]
            t = (sc - c) @ ax
            L = float(t.max() - t.min())
            w = min(STRUCT_END[2], max(STRUCT_END[1], STRUCT_END[0] * L))
            ends = [t <= t.min() + w, t >= t.max() - w]
            ends.sort(key=lambda m: round(float(sc[m][:, 2].mean()), 2))   # as the audit
            j = 0 if detail.startswith("lower") else 1
            m = ends[j]
            wv, wi = sc[m], vi[m]
            # the end's own direction: the main axis of the end window's edge samples (an L-shaped rail turns; a
            # box beam's window holds only its 4 end vertices, so the vertices alone would give the end face's axis)
            cw = wv.mean(0)
            ep = _edge_pts(sc, sps, STRUCT_STEP)
            te = (ep - c) @ ax
            ep = ep[(te <= t.min() + w) if t[m].mean() <= t.mean() else (te >= t.max() - w)]
            if len(ep) >= 2 and np.ptp(ep, 0).max() > 1e-4:
                e2, v2 = np.linalg.eigh(np.cov((ep - ep.mean(0)).T) + np.eye(3) * 1e-12)
                d = v2[:, int(np.argmax(e2))]
            else:
                d = ax
            far = sc[~m].mean(0) if (~m).any() else c
            if float((cw - far) @ d) < 0:
                d = -d
            tt = (wv - cw) @ d
            sel = tt >= tt.max() - 1e-3
            ids, pts = wi[sel], wv[sel]
            if j == 0 and "_str" in nm:
                d = np.array((0.0, 0.0, -1.0))            # a stringer foot runs straight down
            hits = [_ray_hit(W, me, p, d, SNAP_END_M + 0.02) for p in [pts.mean(0)] + list(pts)]
            hits = [h for h in hits if h is not None]
            if j == 0 and "_str" in nm and not hits:
                g = _ground_gap(recipe, _grade_fn(recipe), pts[int(np.argmin(pts[:, 2]))])
                hits = [g] if 0.0 < g <= SNAP_FOOT_M else []
            if hits and min(hits) <= (SNAP_FOOT_M if (j == 0 and "_str" in nm) else SNAP_END_M):
                s = min(hits) + STRUCT_SEAT_M
                _move_verts(o, ids, np.asarray(d) * s)
                moves.append({"fix": "end", "shell": name, "mm": round(s * 1000, 1), "was": detail})
                continue
            # the end runs on beside what it meets (a rail cap past a pier's corner): stretch it along its own
            # direction until some end point lands (<= CONTACT_M or inside another part), at most SNAP_END_M
            if not (j == 0 and "_str" in nm):
                got = None
                for s in np.arange(0.005, SNAP_END_M + 1e-9, 0.005):
                    q = pts + np.asarray(d) * s
                    if any(W.near(p, me, CONTACT_M) or W.inside_other(p, nm, k) for p in q):
                        got = float(s)
                        break
                if got is not None:
                    s = got + STRUCT_SEAT_M
                    _move_verts(o, ids, np.asarray(d) * s)
                    moves.append({"fix": "end-stretch", "shell": name, "mm": round(s * 1000, 1), "was": detail})
                    continue
            # nothing along the member: it stands off a face beside its end (a strut 1 cm proud of its gable):
            # the whole member moves onto the nearest surface within SNAP_SEAT_M
            best = None
            for p in _edge_pts(sc, sps, STRUCT_STEP)[:: 3]:
                for loc, _n, idx, dd in W.bvh.find_nearest_range(Vector(tuple(p)), SNAP_SEAT_M + 0.005):
                    if W.owner[idx] != me and (best is None or dd < best[0]):
                        best = (dd, np.array(loc) - p)
            if best is not None and best[0] <= SNAP_SEAT_M:
                v = best[1] / max(np.linalg.norm(best[1]), 1e-9)
                s = best[0] + STRUCT_SEAT_M
                _move_verts(o, vi, v * s)
                moves.append({"fix": "end-seat", "shell": name, "mm": round(s * 1000, 1), "was": detail})
                continue
        elif chk == "foot":
            low = sc[:, 2] <= sc[:, 2].min() + 1e-3
            pts = _inset(sc[low])
            gaps = [_foot_gap(W, recipe, _grade_fn(recipe), p, me)[0] for p in pts]
            g = max(gaps)
            if 0.0 < g <= SNAP_FOOT_M:
                s = g + STRUCT_SEAT_M
                _move_verts(o, vi[low], (0.0, 0.0, -s))
                moves.append({"fix": "foot", "shell": name, "mm": round(s * 1000, 1), "was": detail})
                continue
            # its foot level touches the SIDE of a slab / rim / stringer (SIDE_MOUNT): the post runs down that side
            # to the support's underside (at most SNAP_FOOT_M) and is bolted along it (the audit's side mount)
            z0 = float(sc[:, 2].min())
            sides = set()
            for p in sc[low]:
                for _d, ow in W.near(p, me, CONTACT_M):
                    if ow[0].startswith(SIDE_MOUNT):
                        sides.add(ow[0])
            if sides:
                zb = min(float(W.geo[W.names.index(nm2)][0][:, 2].min()) for nm2 in sides)
                s = min(z0 - zb, SNAP_FOOT_M)
                if s > 0.05:
                    _move_verts(o, vi[low], (0.0, 0.0, -s))
                    moves.append({"fix": "foot-side", "shell": name, "mm": round(s * 1000, 1),
                                  "on": sorted(sides), "was": detail})
                    continue
        elif chk == "contact":
            pts = _edge_pts(sc, sps, STRUCT_STEP)
            best = None
            for p in pts:
                for loc, _n, idx, dd in W.bvh.find_nearest_range(Vector(tuple(p)), SNAP_SEAT_M + 0.005):
                    if W.owner[idx] != me and W.owner[idx][0] != nm and (best is None or dd < best[0]):
                        best = (dd, np.array(loc) - p)
            if best is not None and best[0] <= SNAP_SEAT_M:
                v = best[1] / max(np.linalg.norm(best[1]), 1e-9)
                s = best[0] + STRUCT_SEAT_M
                if len(sps) == 1 and len(sps[0]) == 4:
                    # a single face standing off a parallel surface (a frame strip modelled as the board's front):
                    # moving it would bury it, so it gets the board's four sides down onto the surface (+8 tris)
                    A = sc[list(sps[0])]
                    nrm = np.cross(A[1] - A[0], A[2] - A[0])
                    nrm /= max(np.linalg.norm(nrm), 1e-12)
                    if abs(float(nrm @ v)) > 0.85:
                        _board_sides(o, vi[list(sps[0])], v * s)
                        moves.append({"fix": "board", "shell": name, "mm": round(s * 1000, 1), "was": detail})
                        continue
                _move_verts(o, vi, v * s)
                moves.append({"fix": "contact", "shell": name, "mm": round(s * 1000, 1), "was": detail})
                continue
        elif chk == "bracket":
            # its top face (the arm's) rises to the nearest downward face over it: a deeper arm, the wall
            # contact kept
            co_all, _ps = _geo(o)
            topm = co_all[:, 2] >= co_all[:, 2].max() - 0.005
            hs = [_ray_hit(W, (nm, -1), p + np.array((0.0, 0.0, 1e-4)), (0, 0, 1), SNAP_LOAD_M)
                  for p in co_all[topm]]
            hs = [h for h in hs if h is not None and h > CONTACT_M]
            touches = int(detail.split("touches ")[1].split(" ")[0]) if "touches " in detail else 2
            if touches < 2:
                # the wall-side ends (both members start at the wall plane) run back onto the wall
                top_sh = co_all[topm]
                c0 = top_sh.mean(0)
                ev, evec = np.linalg.eigh(np.cov((co_all - co_all.mean(0)).T) + np.eye(3) * 1e-12)
                ax = evec[:, int(np.argmax(ev))].copy()
                ax[2] = 0.0
                ax /= max(np.linalg.norm(ax), 1e-9)
                t = (co_all - c0) @ ax
                best = None
                for sg in (-1.0, 1.0):
                    m = (sg * t) >= (sg * t).max() - 0.02
                    hh = [_ray_hit(W, (nm, -1), p + ax * sg * 1e-4, ax * sg, 0.15) for p in co_all[m]]
                    hh = [h for h in hh if h is not None]
                    if hh and (best is None or min(hh) < best[0]):
                        best = (min(hh), sg, np.where(m)[0])
                if best is not None:
                    s = best[0] + K.STRUCT_WALL_EMBED
                    _move_verts(o, best[2], ax * best[1] * s)
                    moves.append({"fix": "bracket-wall", "shell": name, "mm": round(s * 1000, 1), "was": detail})
                    co_all, _ps = _geo(o)
                    topm = co_all[:, 2] >= co_all[:, 2].max() - 0.005
                    hs = [_ray_hit(W, (nm, -1), p + np.array((0.0, 0.0, 1e-4)), (0, 0, 1), SNAP_LOAD_M)
                          for p in co_all[topm]]
                    hs = [h for h in hs if h is not None and h > CONTACT_M]
                    if "carries nothing" not in detail:
                        continue
            if hs and "carries nothing" in detail:
                s = min(hs) + STRUCT_SEAT_M
                _move_verts(o, np.where(topm)[0], (0.0, 0.0, s))
                moves.append({"fix": "bracket", "shell": name, "mm": round(s * 1000, 1), "was": detail})
                continue
        moves.append({"fix": "none", "shell": name, "mm": mm, "was": detail})
    bpy.context.view_layer.update()
    LAST_STRUCT_FIX.clear()
    LAST_STRUCT_FIX.update({"key": recipe.key, "moves": moves})
    return moves
