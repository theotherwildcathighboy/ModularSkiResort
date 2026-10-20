"""
recipes/_lean.py - generator CONTRACT "LEAN" (Richard, 2026-10-11, fifteenth look: "on the ski
patrol shacks, a few of the toboggans lean against nothing"). NOT a recipe: load_recipes()
skips files whose name starts with "_". It imports bpy at module level, so recipe modules
load it lazily inside their builders (as recipes/_civic.py).

Every LEANING part (rescue akja, spine board, bamboo pole ...: the recipe names them by
prefix) must REST on a solid face of the building and STAND on a surface:
    lean direction  n = the horizontal direction from the part's top end to its foot
                    (centroids of the top / bottom 10 % of its height); the support is
                    BEHIND the part, along -n
    contact line    the TOP of the part's BACK face: the vertices nearest the support
                    (largest projection on -n, within 3 mm), sampled at SAMPLES points
                    across the part's width, 5 mm down the part's axis (on the face, off
                    the edge). From each sample a ray starts 0.03 m in front (+n) and runs
                    along -n: it must hit a SOLID part (not a leaning part, not NOT_SOLID:
                    glass, door leaves, lamps, painted decals, soffits, flags) on a face
                    that looks at the part (normal . n >= 0.5) with a gap of
                    -GAP_PEN .. GAP_MAX m behind the line
    foot            the part's lowest vertices, sampled the same way; rays go straight
                    down: grade (z 0) or a solid face 0 .. GAP_MAX m below the foot
                    (the vertices within 1 mm of the lowest)
    pass            both on >= COVER of the samples
check() prints one line per part (name, support part, gap, coverage, foot) and raises
RuntimeError naming every violation, so a recipe builder that calls it fails the build.
measure() returns the same numbers without raising (probes, tests).
Deterministic: sorted parts, fixed sample counts, no randomness.
check_rows() (fine-detail step 3d, end of the file) applies the same measurement to dressing-kit
LEAN rows: the real kit mesh placed by the row, on the support the row declares.
"""

import math
import os
from collections import Counter

import bpy  # noqa: F401  (the module is only loaded inside a Blender build)
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

GAP_MAX = 0.02          # the support face lies at most this far behind the contact line
GAP_PEN = 0.002         # ... and the part may sink at most this far into it
COVER = 0.80            # share of the part's width that must rest / stand
SAMPLES = 11
FRONT = 0.03            # ray start in front of the line
REACH = 2.0             # probe reach for the report (the gap of a failing part)
NOT_SOLID = ("win_", "door_", "lamp_", "emblem_", "stripe_", "soffit_", "flag_", "sign_")


def _verts(obj):
    me = obj.data
    co = np.empty(len(me.vertices) * 3, np.float64)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    M = np.array(obj.matrix_world, np.float64)
    return co @ M[:3, :3].T + M[:3, 3]


def _support_tree(parts, lean_prefixes):
    verts, polys, owner = [], [], []
    for o, _k in sorted(parts, key=lambda p: p[0].name):
        if o.name.startswith(tuple(lean_prefixes)) or o.name.startswith(NOT_SOLID):
            continue
        if o.type != "MESH" or not o.data.polygons:
            continue
        base = len(verts)
        verts.extend(Vector(tuple(v)) for v in _verts(o))
        for p in o.data.polygons:
            polys.append([base + i for i in p.vertices])
            owner.append(o.name)
    return BVHTree.FromPolygons(verts, polys, all_triangles=False, epsilon=0.0), owner


def _line(V, key, tol=0.003):
    """The extreme vertices along `key` (within tol) as a segment (a, b) across the part."""
    d = V @ key
    sel = V[d >= d.max() - tol]
    return sel


def _samples(sel, side):
    s = sel @ side
    a, b = sel[int(np.argmin(s))], sel[int(np.argmax(s))]
    width = float(s.max() - s.min())
    if width < 1e-4:
        return [a], 0.0
    return [a + (b - a) * t for t in np.linspace(0.02, 0.98, SAMPLES)], width


def _cast(tree, owner, origin, direction, reach, facing):
    """First hit -> (part, distance, faces_us) or None."""
    loc, nrm, idx, dist = tree.ray_cast(Vector(origin), Vector(direction), reach)
    if loc is None:
        return None
    return owner[idx], float(dist), float(Vector(nrm).dot(Vector(facing))) >= 0.5


def measure_part(obj, tree, owner):
    V = _verts(obj)
    z0, z1 = float(V[:, 2].min()), float(V[:, 2].max())
    h = z1 - z0
    top = V[V[:, 2] >= z1 - 0.1 * h].mean(0)
    bot = V[V[:, 2] <= z0 + 0.1 * h].mean(0)
    nh = np.array((bot[0] - top[0], bot[1] - top[1], 0.0))
    if np.linalg.norm(nh) < 1e-3:
        return dict(name=obj.name, error="stands vertical (no lean direction)")
    n = nh / np.linalg.norm(nh)
    side = np.cross((0.0, 0.0, 1.0), n)
    axis = (top - bot) / np.linalg.norm(top - bot)
    # contact line: top of the back face
    sel = _line(V, -n)
    pts, width = _samples(sel, side)
    hits = []
    for p in pts:
        q = p - axis * 0.005
        r = _cast(tree, owner, q + n * FRONT, -n, FRONT + REACH, n)
        if r is None:
            hits.append((None, None, False))
            continue
        part, dist, faces = r
        gap = dist - FRONT
        hits.append((part, gap, faces and -GAP_PEN <= gap <= GAP_MAX))
    good = [hh for hh in hits if hh[2]]
    near = [hh for hh in hits if hh[0] is not None]
    pool = good or near
    support = Counter(hh[0] for hh in pool).most_common(1)[0][0] if pool else None
    gaps = sorted(hh[1] for hh in pool if hh[0] == support)
    gap = gaps[len(gaps) // 2] if gaps else None
    cover = len(good) / float(len(hits))
    zc = float(np.mean([p[2] for p in pts]))
    # foot: the lowest vertices, rays down
    fsel = V[V[:, 2] <= z0 + 0.001]
    fpts, fwidth = _samples(fsel, side)
    fhits = []
    for p in fpts:
        best = ("grade", float(p[2])) if p[2] >= -GAP_PEN else None
        r = _cast(tree, owner, p + np.array((0, 0, FRONT)), (0, 0, -1), FRONT + REACH, (0, 0, 1))
        if r is not None and r[2]:
            g = r[1] - FRONT
            if best is None or g < best[1]:
                best = (r[0], g)
        fhits.append((best[0], best[1], -GAP_PEN <= best[1] <= GAP_MAX + 1e-4) if best else
                     (None, None, False))
    fgood = [hh for hh in fhits if hh[2]]
    fpool = fgood or [hh for hh in fhits if hh[0] is not None]
    fsup = Counter(hh[0] for hh in fpool).most_common(1)[0][0] if fpool else None
    fg = sorted(hh[1] for hh in fpool if hh[0] == fsup)
    return dict(name=obj.name, support=support, gap=gap, cover=cover, width=width,
                z=zc, top=(float(top[0]), float(top[1]), float(top[2])),
                n=(round(float(n[0]), 3), round(float(n[1]), 3)),
                lean_deg=math.degrees(math.atan2(float(top[2] - bot[2]),
                                                 float(np.linalg.norm(nh)))),
                foot=fsup, foot_gap=fg[len(fg) // 2] if fg else None,
                foot_cover=len(fgood) / float(len(fhits)), foot_z=z0,
                ok=cover >= COVER - 1e-9 and len(fgood) / float(len(fhits)) >= COVER - 1e-9)


def measure(parts, lean_prefixes):
    """[measure_part dict] for every part whose name starts with one of lean_prefixes."""
    bpy.context.view_layer.update()
    tree, owner = _support_tree(parts, lean_prefixes)
    rows = []
    for o, _k in sorted(parts, key=lambda p: p[0].name):
        if o.name.startswith(tuple(lean_prefixes)):
            rows.append(measure_part(o, tree, owner))
    return rows


def fmt(r):
    if "error" in r:
        return f"{r['name']}: {r['error']}"
    g = "none" if r["gap"] is None else f"{r['gap']:+.3f} m"
    fg = "none" if r["foot_gap"] is None else f"{r['foot_gap']:+.3f} m"
    return (f"{r['name']}: rests on {r['support']} gap {g} over {100 * r['cover']:.0f} % of "
            f"{r['width']:.2f} m at z {r['z']:.3f} (lean {r['lean_deg']:.1f} deg); foot on "
            f"{r['foot']} gap {fg} over {100 * r['foot_cover']:.0f} % "
            f"{'OK' if r['ok'] else 'FAIL'}")


def check(parts, lean_prefixes, label, expect=None):
    """CONTRACT LEAN: print one line per leaning part, raise on any violation. `expect`
    (optional) = the exact sorted part names the recipe declares as leaning (guards a
    renamed or vanished part)."""
    rows = measure(parts, lean_prefixes)
    for r in rows:
        print(f"[lean] {label} {fmt(r)}")
    bad = [r["name"] for r in rows if "error" in r or not r["ok"]]
    names = sorted(r["name"] for r in rows)
    if expect is not None and names != sorted(expect):
        raise RuntimeError(f"CONTRACT LEAN {label}: leaning parts {names} != declared "
                           f"{sorted(expect)}")
    if bad:
        raise RuntimeError(f"CONTRACT LEAN {label}: {len(bad)} leaning part(s) rest on nothing "
                           f"or float (gap <= {GAP_MAX} m on >= {COVER:.0%} of the width): {bad}")
    print(f"[lean] CONTRACT LEAN {label}: {len(rows)} leaning part(s) rest on a solid face and "
          f"stand on grade / a deck / a tread (gap <= {GAP_MAX} m, >= {COVER:.0%} of the width)")
    return rows


# ======================================================================================
# Fine-detail step 3d: the same contract on dressing-kit ROWS (build/fine-detail-step3d-patrol-sign.md)
# ======================================================================================
# A host that replaces a modeled leaning part with a dressing-kit "mod" row (SkiDressAkja,
# SkiDressSpineBoard, SkiDressTools: fine_detail.KIT mount "lean") keeps the contract's semantics: the
# REAL kit mesh (the shipped props/B FBX, prefab frame) placed by the row is measured exactly like a
# modeled leaning part (measure_part: the top of its back face on a SOLID face <= GAP_MAX behind it over
# >= COVER of its width, its foot on grade / a solid face), against the host's own parts, and it must rest
# on the SAME support part the row declares (row["lean"]["support"], the part the modeled one leaned
# on). On top (the step-3b F1 idea): the placed kit mesh may overlap no host triangle except its support
# and the surface its foot stands on.
# Frames: rows are game local (gx, gy, gz) = authoring (y, z, x), gx NOT negated (fine_detail.X_CONVENTION);
# the host parts are in the authoring frame (pre-yaw, z up), so a kit vertex v goes to
# authoring (gz, gx, gy) of R(q) v + position.

KIT_ROOT = None          # assets/import/props/B (resolved lazily from cs2_validate's location)
ROW_PREFIX = "kitrow_"
_KIT_MESH = {}


def _kit_root():
    global KIT_ROOT
    if KIT_ROOT is None:
        import cs2_validate
        here = os.path.dirname(os.path.abspath(cs2_validate.__file__))
        KIT_ROOT = os.path.normpath(os.path.join(here, os.pardir, "import", "props", "B"))
    return KIT_ROOT


def load_kit_mesh(mesh):
    """The shipped kit FBX <KIT_ROOT>/<mesh>/<mesh>.fbx as {"V" (N, 3) prefab frame (x, y up, z) m,
    "tris"}: raw FBX cm / 100 (the hosted-tower / step-3b F1 reading), checked against the kit manifest's
    bounds_m (1 mm; guards the axis convention). Cached by the FBX sha1; the import's objects, materials
    and images are removed again."""
    import hashlib
    import json
    import cs2_validate
    path = os.path.join(_kit_root(), mesh, f"{mesh}.fbx")
    if not os.path.isfile(path):
        raise RuntimeError(f"CONTRACT LEAN: no dressing-kit FBX at {path} (props/B must ship first)")
    with open(path, "rb") as fh:
        sha = hashlib.sha1(fh.read()).hexdigest()
    if sha in _KIT_MESH:
        return _KIT_MESH[sha]
    mats0, imgs0 = set(bpy.data.materials.keys()), set(bpy.data.images.keys())
    objs = cs2_validate.import_fbx_temp(path)
    try:
        o = [x for x in objs if x.type == "MESH"][0]
        me = o.data
        co = np.empty(len(me.vertices) * 3, np.float64)
        me.vertices.foreach_get("co", co)
        V = co.reshape(-1, 3) / 100.0
        tris = []
        for pl in me.polygons:
            vs = list(pl.vertices)
            for j in range(1, len(vs) - 1):
                tris.append((vs[0], vs[j], vs[j + 1]))
    finally:
        cs2_validate.remove_objects(objs)
        for k in sorted(set(bpy.data.materials.keys()) - mats0):
            bpy.data.materials.remove(bpy.data.materials[k])
        for k in sorted(set(bpy.data.images.keys()) - imgs0):
            bpy.data.images.remove(bpy.data.images[k])
    with open(os.path.join(_kit_root(), mesh, f"{mesh}.manifest.json"), "r", encoding="utf-8") as fh:
        bm = json.load(fh).get("bounds_m") or {}
    if not bm or np.abs(V.min(axis=0) - np.array(bm["min"])).max() > 1e-3 \
            or np.abs(V.max(axis=0) - np.array(bm["max"])).max() > 1e-3:
        raise RuntimeError(f"CONTRACT LEAN {mesh}: FBX bounds {V.min(axis=0).round(4).tolist()} .. "
                           f"{V.max(axis=0).round(4).tolist()} != the kit manifest's {bm}")
    _KIT_MESH[sha] = {"V": V, "tris": tris, "sha1": sha, "mesh": mesh}
    return _KIT_MESH[sha]


def _quat_apply(q, V):
    x, y, z, w = (float(c) for c in q)
    u = np.array((x, y, z))
    return V + 2.0 * np.cross(u, np.cross(u, V) + w * V)


def row_authoring(row):
    """((N, 3) authoring-frame vertices, tris) of the row's real kit mesh."""
    import fine_detail as FD
    k = FD.KIT[row["prefab"]]
    km = load_kit_mesh(k.mesh)
    G = _quat_apply(row["rotation"], km["V"]) + np.asarray(row["position"], np.float64)
    return np.stack([G[:, 2], G[:, 0], G[:, 1]], axis=1), km["tris"]


def row_object(row, name):
    """A temporary (unlinked) mesh object of the row's kit mesh in the authoring frame."""
    A, tris = row_authoring(row)
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(p) for p in A], [], [tuple(t) for t in tris])
    me.update()
    return bpy.data.objects.new(name, me)


def _remove(o):
    me = o.data
    bpy.data.objects.remove(o, do_unlink=True)
    if me is not None and me.users == 0:
        bpy.data.meshes.remove(me)


def _row_name(row, i):
    return ROW_PREFIX + str((row.get("lean") or {}).get("replaces") or f"{row['prefab']}{i}")


def measure_rows(parts, rows, lean_prefixes):
    """[measure_part dict + row / declared support / overlaps] for every kit lean row."""
    bpy.context.view_layer.update()
    tree, owner = _support_tree(parts, lean_prefixes)
    out = []
    for i, r in enumerate(rows):
        o = row_object(r, _row_name(r, i))
        try:
            m = measure_part(o, tree, owner)
            A, tris = row_authoring(r)
            rt = BVHTree.FromPolygons([Vector(tuple(p)) for p in A], [list(t) for t in tris],
                                      all_triangles=True, epsilon=0.0)
            hits = sorted({owner[b] for _a, b in rt.overlap(tree)})
        finally:
            _remove(o)
        lean = r.get("lean") or {}
        m.update(row=i, prefab=r["prefab"], replaces=lean.get("replaces"), declared=lean.get("support"),
                 overlaps=[h for h in hits if h not in (lean.get("support"), m.get("support"), m.get("foot"))])
        out.append(m)
    return out


def check_rows(parts, rows, label, lean_prefixes, expect=None):
    """CONTRACT LEAN on the kit rows: one [lean] line per row; raise on a row that rests on nothing / on
    another part than it declares, floats, or overlaps a host face other than its support / foot surface.
    `expect` = the sorted names the rows replace (guards a dropped row)."""
    rows = list(rows)
    res = measure_rows(parts, rows, lean_prefixes)
    for m in res:
        extra = f"; declared {m['declared']}" + (f"; OVERLAPS {m['overlaps']}" if m["overlaps"] else "")
        print(f"[lean] {label} row {m['prefab']} ({m['replaces']}): {fmt(m)}{extra}")
    bad = [m["replaces"] or m["name"] for m in res
           if "error" in m or not m["ok"] or m["overlaps"]
           or (m["declared"] is not None and m["support"] != m["declared"])]
    names = sorted(str(m["replaces"]) for m in res)
    if expect is not None and names != sorted(expect):
        raise RuntimeError(f"CONTRACT LEAN {label}: kit lean rows {names} != declared {sorted(expect)}")
    if bad:
        raise RuntimeError(f"CONTRACT LEAN {label}: {len(bad)} kit row(s) rest on nothing / not on their "
                           f"declared support, float or overlap the host: {bad}")
    print(f"[lean] CONTRACT LEAN {label}: {len(res)} kit row(s) rest on their declared support and stand on "
          f"grade / a deck (gap <= {GAP_MAX} m, >= {COVER:.0%} of the width), no host overlap")
    return res
