"""
cs2_kitrows.py - the dressing kit's ROW PATH on a station host, shared (fine-detail step 3c, 2026-10-12;
docs/research/art/assets-2026-10/build/fine-detail-step3c-tram-pali.md).

The dressing kit (props letter B, build/dress-kit.md) replaces host-modeled dressing (racks, hose reels,
extinguishers, e-stops, tools) with "mod" sub-object rows (fine_detail.kit_row / kit_lean_row, written by
cs2_liftdress.sub_object_rows(fine=True)). This module holds what every station generator needs for those
rows, frame-agnostic:

    load_kit_mesh(mesh)          the SHIPPED kit FBX (assets/import/props/B/<mesh>/<mesh>.fbx) as numpy in its
                                 prefab frame (raw FBX cm / 100), checked against the kit manifest's bounds_m
    place_game(V, row)           prefab-local kit vertices placed by a row -> game local (yaw about +Y, then +pos)
    host_mesh(row, to_host)      the placed kit mesh in the generator's z-UP host frame
    kit_ledge_offset(mesh, h0, h1)   a leaning prop against a LEDGE: how far its rest plane sits behind the
                                 ledge face so the prop just touches the ledge's top edge
    check_kit_rows(...)          contract F1 (mount, contact, access, real-mesh clearance, envelopes,
                                 keep-outs, rows, lot) - the step-3b F1 of lift_terminal_detach, generalized
    augment(co, tris, names, rows, to_host)   the station arrays + the placed kit meshes (named kitrow_*), so a
                                 route / sweep / reachability contract keeps counting what left the mesh
    probe_object(t, rows, game_to_world)      the same as a temporary part-tagged object (contracts that read
                                 an object), removed again by remove_probe()

FRAMES. Every generator now writes game x = -authoring y (x-sign gate settled N, 2026-10-15 playtest; S1b-N / T14
2026-10-16): the DQ / gondola stations (cs2_liftdress.blender_to_game) and the Pali M stations (lift_terminal.
m_to_game) hand this module GAME rows. The tram still checks its rows in its DESIGN frame (prefab-numbers, X = FBX-raw
x, tram_station._fd_host = (Z, X, Y), not game) and only check_subobjects writes them through rule T
(fine_detail.design_row_to_game) into the manifest. Every check here runs in the generator's z-up HOST frame
(x travel, y lateral, z up); the caller passes `to_host(G)` mapping the rows' own frame (N, 3) arrays into it:
tram (design) (gz, gx, gy), Pali M (game) (gz, -gx, gy). A row is always in the caller's own frame, so the kit prop
stands exactly as authored; the Pali host frame reads the placed mesh mirrored along its long axis (the M map is
a reflection), which no contract here depends on.

lift_terminal_detach.py (step 3b) keeps its own copy of these helpers until its owner moves it here (its F1
must stay byte-identical; build/fine-detail-step3b-stations.md section 13 item 1).
"""

import hashlib
import json
import math
import os
import sys

import bpy
import bmesh
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import cs2_validate  # noqa: E402
import fine_detail as FD  # noqa: E402

REPO = os.path.normpath(os.path.join(HERE, os.pardir, os.pardir))
KIT_ROOT = os.path.join(REPO, "assets", "import", "props", "B")
MOUNT_GAP = 0.01        # F1: a kit prop's mount face within this of its host face (deck / wall)
CLEAR_OFF = 0.01        # F1: the real kit mesh, backed this far off its mount, may touch no face
ACCESS = 0.40           # F1: wall props keep this much free space in front of their face
KIT_PART_PREFIX = "kitrow_"


class KitRowError(RuntimeError):
    pass


_KIT_MESH = {}
_KIT_LEDGE = {}


def load_kit_mesh(mesh):
    """The shipped kit FBX as {"V" (N, 3) prefab frame (x, y up, z), "tris", "sha1", "path"}; cached by sha1.
    The import's objects, materials and images are removed again and the active object restored."""
    path = os.path.join(KIT_ROOT, mesh, f"{mesh}.fbx")
    if not os.path.isfile(path):
        raise KitRowError(f"F1 no dressing-kit FBX at {path} (props/B must ship first)")
    with open(path, "rb") as fh:
        sha = hashlib.sha1(fh.read()).hexdigest()
    if sha in _KIT_MESH:
        return _KIT_MESH[sha]
    mats0, imgs0 = set(bpy.data.materials.keys()), set(bpy.data.images.keys())
    active = bpy.context.view_layer.objects.active
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
        try:
            bpy.context.view_layer.objects.active = active
        except (ReferenceError, RuntimeError):
            pass
    with open(os.path.join(KIT_ROOT, mesh, f"{mesh}.manifest.json"), "r", encoding="utf-8") as fh:
        bm = json.load(fh).get("bounds_m") or {}
    if not bm or np.abs(V.min(axis=0) - np.array(bm["min"])).max() > 1e-3 \
            or np.abs(V.max(axis=0) - np.array(bm["max"])).max() > 1e-3:
        raise KitRowError(f"F1 {mesh}: FBX bounds {V.min(axis=0).round(4).tolist()} .. "
                          f"{V.max(axis=0).round(4).tolist()} != the manifest's {bm}")
    _KIT_MESH[sha] = {"V": V, "tris": tris, "sha1": sha, "path": os.path.relpath(path, REPO).replace("\\", "/")}
    return _KIT_MESH[sha]


def yaw_of(q):
    x, y, z, w = (float(v) for v in q)
    if abs(x) > 1e-6 or abs(z) > 1e-6:
        raise KitRowError(f"kit row rotation {q} is not a pure yaw about game +Y")
    return 2.0 * math.atan2(y, w)


def place_game(V, row):
    """Prefab-local kit vertices placed by a row -> game local: the yaw about +Y as
    cs2_liftdress.subobj_box (x' = x cos + z sin, z' = -x sin + z cos), then + position."""
    a = yaw_of(row["rotation"])
    c, s = math.cos(a), math.sin(a)
    px, py, pz = (float(v) for v in row["position"])
    return np.stack([V[:, 0] * c + V[:, 2] * s + px, V[:, 1] + py, -V[:, 0] * s + V[:, 2] * c + pz], axis=1)


def host_mesh(row, to_host):
    """(A (N, 3) host frame, tris, kit mesh record) of one kit row."""
    km = load_kit_mesh(row["mesh"])
    return np.asarray(to_host(place_game(km["V"], row)), dtype=np.float64), km["tris"], km


def kit_ledge_offset(mesh, h0, h1, step=0.001):
    """A leaning kit prop against a LEDGE (a face spanning heights h0 .. h1 above the floor, nothing in its
    plane above h1): the distance d >= 0 its rest plane (prefab z = rest_z, the kit's min z) must sit BEHIND
    the ledge face so the prop just touches the ledge's top edge = (the kit faces' min prefab z over h0 .. h1,
    sliced every `step`) - rest_z. Measured on the shipped kit FBX."""
    km = load_kit_mesh(mesh)
    key = (km["sha1"], round(float(h0), 4), round(float(h1), 4), step)
    if key in _KIT_LEDGE:
        return _KIT_LEDGE[key]
    V, T = km["V"], km["tris"]
    zmin = None
    for h in np.arange(h0, h1 + step / 2.0, step):
        for tr in T:
            P = V[list(tr)]
            for a, b in ((0, 1), (1, 2), (2, 0)):
                ya, yb = P[a, 1], P[b, 1]
                if ya != yb and (ya - h) * (yb - h) <= 0.0:
                    z = P[a, 2] + (h - ya) / (yb - ya) * (P[b, 2] - P[a, 2])
                    zmin = z if zmin is None else min(zmin, z)
    if zmin is None:
        raise KitRowError(f"{mesh}: no kit face between heights {h0} and {h1}")
    _KIT_LEDGE[key] = round(max(0.0, float(zmin) - float(V[:, 2].min())), 4)
    return _KIT_LEDGE[key]


def kit_tube_offset(mesh, hc, r, step=0.001):
    """A leaning kit prop against a horizontal TUBE (a railing's top rail: axis at height hc above the floor,
    radius r, running along the wall plane): the distance d >= 0 its rest plane (prefab z = rest_z) sits behind the
    tube's inner face (the tube surface at its equator) so the prop just touches the tube - the tube center goes
    where the prop's cross-section, sliced every `step` over hc -+ r, first meets the circle:
    z_c = min over h of (the kit faces' min prefab z at h - sqrt(r^2 - (h - hc)^2)), d = z_c + r - rest_z."""
    km = load_kit_mesh(mesh)
    key = ("tube", km["sha1"], round(float(hc), 4), round(float(r), 4), step)
    if key in _KIT_LEDGE:
        return _KIT_LEDGE[key]
    V, T = km["V"], km["tris"]
    zc = None
    for h in np.arange(hc - r, hc + r + step / 2.0, step):
        zmin = None
        for tr in T:
            P = V[list(tr)]
            for a, b in ((0, 1), (1, 2), (2, 0)):
                ya, yb = P[a, 1], P[b, 1]
                if ya != yb and (ya - h) * (yb - h) <= 0.0:
                    z = P[a, 2] + (h - ya) / (yb - ya) * (P[b, 2] - P[a, 2])
                    zmin = z if zmin is None else min(zmin, z)
        if zmin is None:
            continue
        c = zmin - math.sqrt(max(0.0, r * r - (h - hc) ** 2))
        zc = c if zc is None else min(zc, c)
    if zc is None:
        raise KitRowError(f"{mesh}: no kit face around height {hc}")
    _KIT_LEDGE[key] = round(max(0.0, float(zc) + r - float(V[:, 2].min())), 4)
    return _KIT_LEDGE[key]


def aabb_gap(alo, ahi, blo, bhi):
    d = np.maximum(np.maximum(np.asarray(blo) - np.asarray(ahi), np.asarray(alo) - np.asarray(bhi)), 0.0)
    return float(np.linalg.norm(d))


def _tree(co, tris):
    return BVHTree.FromPolygons(np.asarray(co, dtype=np.float64).tolist(), np.asarray(tris).tolist(),
                                all_triangles=True)


def _box_tree(lo, hi):
    c = [(x, y, z) for z in (lo[2], hi[2]) for x, y in ((lo[0], lo[1]), (hi[0], lo[1]), (hi[0], hi[1]), (lo[0], hi[1]))]
    return BVHTree.FromPolygons(c, [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)],
                                epsilon=0.0)


def check_kit_rows(specs, rows, co, tris, names, to_host, *, all_names, keepouts=(), envelopes=(), clouds=(),
                   vboxes=(), lot=None, code="F1", access=ACCESS, grade_z=0.0):
    """F1 on every kit row, in the generator's z-up HOST frame (co (N, 3), tris (T, 3) ints, names = one part
    name per triangle; `to_host` maps game-local arrays into it). `specs[i]` belongs to `rows[i]`:
    {"prop", "pos" (host: ground = foot center, wall = the mount point ON the wall face at the prop's bottom,
    lean = the wall / ledge face point at the floor), "normal" (host, horizontal: the facing / the wall's
    outward normal), "mount" (ground | wall | lean), "grade" (bool: stands on the ground, no mesh under it),
    "removed" (part-name prefixes the row replaces), "was", optional "ledge" (h0, h1)}. Checks:
      - the row's CID is the staged object-prefab CID (fine_detail.staged_cid) of an uncolored prop or the
        color-set VARIANT's seeded CID of a colored one (fine_detail.row_colour_error) and no part of `all_names`
        starts with a `removed` prefix;
      - mount: ground props have a floor face <= MOUNT_GAP under the foot center and every AABB bottom corner
        (inset 3 cm; grade props stand at grade_z exactly); wall props have a wall face <= MOUNT_GAP behind the
        center and every corner of their silhouette, and `access` m of free space in front (no station
        triangle, no vanilla box, no other kit row); lean props keep the foot-line rule (|lean_rest_gap| <=
        fine_detail.FOOT_TOL), the mesh moved MOUNT_GAP toward the wall meets a station face (contact) and the
        floor is under the feet;
      - the REAL kit mesh, backed CLEAR_OFF off its mount, overlaps no station triangle (BVH);
      - its AABB meets no keep-out / envelope box, keeps the `clouds` minimum distances (a swept-carrier point
        cloud: (label, points, min_gap)), meets no vanilla row box and no other kit row, and stays in `lot`
        ((xlo, xhi, ylo, yhi) host).
    Returns ({code: message}, {"rows": [record per row]}); raises KitRowError("<code> kit rows: ...")."""
    if not specs:
        return {}, None
    if len(specs) != len(rows):
        raise KitRowError(f"{code}: {len(specs)} specs vs {len(rows)} rows")
    co = np.asarray(co, dtype=np.float64)
    tris = np.asarray(tris, dtype=np.int64)
    T = co[tris]
    tree = _tree(co, tris)
    left = sorted({n for n in set(all_names) for s in specs for pre in s["removed"] if n.startswith(pre)})
    if left:
        raise KitRowError(f"{code} the parts the kit rows replace are still in the host: {left[:6]}")
    down = Vector((0.0, 0.0, -1.0))

    def floor_gap(x, y, z):
        h = tree.ray_cast(Vector((x, y, z + 0.02)), down, 0.05 + MOUNT_GAP)
        return None if h[0] is None else h[3] - 0.02

    out, kboxes, bad = [], [], []
    for i, (s, r) in enumerate(zip(specs, rows)):
        tag = f"{s['prop']}@{s['pos'][0]:.2f},{s['pos'][1]:.2f}"
        # the CID: KIT's staged one for an uncolored prop, the color-set VARIANT's for a colored one
        # (kit color variants 2026-10-12; fine_detail.row_colour_error)
        cerr = FD.row_colour_error(r)
        if cerr or r["prefab"] != s["prop"]:
            raise KitRowError(f"{code} {tag}: CID {r['m_Object']}: {cerr or 'prefab ' + str(r['prefab'])}")
        A, ktris, km = host_mesh(r, to_host)
        nv = np.array([float(s["normal"][0]), float(s["normal"][1]), 0.0])
        nv /= np.linalg.norm(nv)
        rec = {"row": i, "prop": s["prop"], "cid": r["m_Object"], "mount": s["mount"],
               "pos_host": [round(float(v), 4) for v in s["pos"]], "row_pos": list(r["position"]),
               "row_rot": list(r["rotation"]), "was": s["was"]}
        if r.get("colour_set"):
            rec["colour_set"] = r["colour_set"]
        if s["mount"] == "ground":
            lo, hi = A.min(axis=0), A.max(axis=0)
            if s.get("grade"):
                if abs(lo[2] - grade_z) > 1e-4:
                    bad.append((tag, f"grade prop bottom z {lo[2]:.4f} != {grade_z}"))
                rec["floor"] = f"grade (z {grade_z})"
            else:
                pts = [(s["pos"][0], s["pos"][1])] + [(x, y) for x in (lo[0] + 0.03, hi[0] - 0.03)
                                                      for y in (lo[1] + 0.03, hi[1] - 0.03)]
                gaps = [floor_gap(x, y, s["pos"][2]) for x, y in pts]
                if any(g is None or abs(g) > MOUNT_GAP for g in gaps):
                    bad.append((tag, f"no floor face within {MOUNT_GAP} m under the foot / corners {gaps}"))
                rec["floor_gap_m"] = round(max(abs(g) for g in gaps), 4) if all(g is not None for g in gaps) else None
            off = np.array([0.0, 0.0, CLEAR_OFF])
        else:
            right = np.array([-nv[1], nv[0], 0.0])
            pts = []
            if s["mount"] == "wall":
                plane = np.array(s["pos"], dtype=np.float64)
                u = (A - plane) @ right
                w = A[:, 2]
                pts = [plane + right * (u.min() + 0.02) + np.array([0, 0, w.min() + 0.02 - plane[2]]),
                       plane + right * (u.max() - 0.02) + np.array([0, 0, w.min() + 0.02 - plane[2]]),
                       plane + right * (u.min() + 0.02) + np.array([0, 0, w.max() - 0.02 - plane[2]]),
                       plane + right * (u.max() - 0.02) + np.array([0, 0, w.max() - 0.02 - plane[2]]),
                       plane + right * (0.5 * (u.min() + u.max())) + np.array([0, 0, 0.5 * (w.min() + w.max()) - plane[2]])]
                rec["wall_silhouette_m"] = [round(float(u.max() - u.min()), 3), round(float(w.max() - w.min()), 3)]
                d_front = float(((A - plane) @ nv).max())
                cs = [plane + right * uu + nv * dd + np.array([0.0, 0.0, ww - plane[2]])
                      for uu in (u.min(), u.max()) for dd in (d_front + 0.005, d_front + access)
                      for ww in (w.min(), w.max())]
                alo, ahi = np.min(cs, axis=0), np.max(cs, axis=0)
                blocked = sorted({names[j] for _a, j in _box_tree(alo, ahi).overlap(tree)}
                                 | {names[j] for j in np.nonzero(((T > alo) & (T < ahi)).all(axis=2).any(axis=1))[0]})
                blocked += [lab for lab, vlo, vhi in list(vboxes) + kboxes if (np.asarray(vlo) < ahi).all()
                            and (np.asarray(vhi) > alo).all()]
                rec["access_m"] = access
                if blocked:
                    bad.append((tag, f"no access: {blocked[:4]} within {access} m in front of it"))
            else:
                g = FD.lean_rest_gap(r)
                rec["rest_gap_m"] = round(g, 5)
                if s.get("ledge") or s.get("tube"):
                    rec["ledge_z" if s.get("ledge") else "tube_hc_r"] = list(s.get("ledge") or s.get("tube"))
                    rec["ledge_offset_m"] = round(float(np.dot(np.array(s["pos"]) - A[np.argmin(A @ nv)], nv)), 4)
                if abs(g) > FD.FOOT_TOL + 1e-9:
                    bad.append((tag, f"foot-line rule: rest plane {g:.4f} m off the wall (tol {FD.FOOT_TOL})"))
                Bt = A - nv * MOUNT_GAP + np.array([0.0, 0.0, CLEAR_OFF])        # lifted: the floor never counts
                touch = sorted({names[j] for _a, j in _tree(Bt, ktris).overlap(tree)})
                rec["rests_on"] = touch[:4]
                if not touch:
                    bad.append((tag, f"lean prop touches nothing within {MOUNT_GAP} m (no contact with the wall)"))
                feet = A[A[:, 2] <= A[:, 2].min() + 0.002]
                fg = [floor_gap(float(v[0]), float(v[1]), float(s["pos"][2])) for v in feet[:: max(1, len(feet) // 8)]] \
                    if not s.get("grade") else [0.0]
                if any(x is None or abs(x) > MOUNT_GAP + 0.002 for x in fg):
                    bad.append((tag, f"lean prop feet: no floor within {MOUNT_GAP} m ({fg[:4]})"))
            wg = []
            for q in pts:
                h = tree.ray_cast(Vector(tuple(np.asarray(q) + nv * 0.005)), Vector(tuple(-nv)), 0.005 + MOUNT_GAP)
                wg.append(None if h[0] is None else round(h[3] - 0.005, 4))
            if any(x is None for x in wg):
                bad.append((tag, f"{s['mount']}: {sum(1 for x in wg if x is None)} of {len(wg)} back points have "
                                 f"no wall face within {MOUNT_GAP} m behind them"))
            if wg:
                rec["wall_gap_m"] = max((abs(x) for x in wg if x is not None), default=None)
            off = nv * CLEAR_OFF + (np.array([0.0, 0.0, CLEAR_OFF]) if s["mount"] == "lean" else 0.0)
        B = A + off
        hits = sorted({names[j] for _a, j in _tree(B, ktris).overlap(tree)})
        if hits:
            bad.append((tag, f"the kit mesh (backed {CLEAR_OFF} m off its mount) cuts {hits[:5]}"))
        lo, hi = B.min(axis=0), B.max(axis=0)
        for lab, klo, khi in list(keepouts) + list(envelopes):
            if (np.asarray(klo) < hi).all() and (np.asarray(khi) > lo).all():
                bad.append((tag, f"AABB in {lab}"))
        for lab, vlo, vhi in vboxes:
            if (np.asarray(vlo) < hi).all() and (np.asarray(vhi) > lo).all():
                bad.append((tag, f"AABB overlaps the vanilla row {lab}"))
        for lab, olo, ohi in kboxes:
            if (olo < hi).all() and (ohi > lo).all():
                bad.append((tag, f"AABB overlaps the kit row {lab}"))
        if lot is not None and (lo[0] < lot[0] - 1e-6 or hi[0] > lot[1] + 1e-6 or lo[1] < lot[2] - 1e-6
                                or hi[1] > lot[3] + 1e-6):
            bad.append((tag, f"leaves the lot ({lo.round(2).tolist()} .. {hi.round(2).tolist()})"))
        if envelopes:
            rec["envelope_gap_m"] = round(min(aabb_gap(lo, hi, a_, b_) for _l, a_, b_ in envelopes), 3)
        for lab, P, gmin in clouds:
            P = np.asarray(P, dtype=np.float64)
            if not len(P):
                continue
            dd = float(np.linalg.norm(np.maximum(np.maximum(lo - P, 0.0), P - hi), axis=1).min())
            rec.setdefault("cloud_gap_m", {})[lab] = round(dd, 3)
            if dd < gmin:
                bad.append((tag, f"{dd:.3f} m from {lab} (< {gmin})"))
        others = [(aabb_gap(lo, hi, vlo, vhi), lab) for lab, vlo, vhi in list(vboxes) + kboxes]
        if others:
            g_, l_ = min(others)
            rec["nearest_row"] = [l_, round(g_, 3)]
        if keepouts:
            g_, l_ = min((aabb_gap(lo, hi, a_, b_), lab) for lab, a_, b_ in keepouts)
            rec["nearest_keepout"] = [l_, round(g_, 3)]
        rec["aabb_host"] = [lo.round(4).tolist(), hi.round(4).tolist()]
        rec["kit_fbx_sha1"] = km["sha1"][:12]
        kboxes.append((f"kit {tag}", lo, hi))
        out.append(rec)
    if bad:
        raise KitRowError(f"{code} kit rows: {bad[:6]} ({len(bad)} findings)")
    by = {}
    for s in specs:
        by[s["prop"]] = by.get(s["prop"], 0) + 1
    env = [r_["envelope_gap_m"] for r_ in out if r_.get("envelope_gap_m") is not None]
    cl = [min(r_["cloud_gap_m"].values()) for r_ in out if r_.get("cloud_gap_m")]
    msg = (f"{len(specs)} dressing-kit rows {by}: CIDs staged, the replaced parts gone, mounts held (floor / whole "
           f"back on the wall + {access} m access / foot-line rule + contact), the real kit meshes clear of every "
           f"station face" + (f", the carrier envelopes (nearest {min(env):.3f} m)" if env else "")
           + (f", the swept carrier cloud (nearest {min(cl):.3f} m)" if cl else "")
           + ", the keep-outs, every vanilla and kit row, inside the lot")
    print(f"[contract] {code} ok: {msg}")
    return {code: msg}, {"rows": out}


def augment(co, tris, names, rows, to_host, prefix=KIT_PART_PREFIX):
    """(co, tris, names) + every kit row's placed mesh (host frame), its triangles named
    `<prefix><index>_<prop>`: the arrays a route / sweep contract runs on once the dressing left the mesh."""
    co = np.asarray(co, dtype=np.float64)
    tris = np.asarray(tris, dtype=np.int64)
    co_l, tr_l, nm = [co], [tris], list(names)
    n = len(co)
    for i, r in enumerate(rows):
        A, kt, _km = host_mesh(r, to_host)
        co_l.append(A)
        tr_l.append(np.asarray(kt, dtype=np.int64) + n)
        nm += [f"{prefix}{i:02d}_{r['prefab']}"] * len(kt)
        n += len(A)
    return np.concatenate(co_l), np.concatenate(tr_l), nm


def probe_object(t, rows, game_to_world, part_prop, name="KitRowProbe", prefix=KIT_PART_PREFIX):
    """A temporary copy of the part-tagged joined object `t` with every kit row's placed mesh appended (world
    coordinates by `game_to_world` of the game-local placement), each a part `<prefix><index>_<prop>` in the
    copied part table (`part_prop` = cs2_asset.PART_TABLE_PROP): for the contracts that read an object
    (tram C15 / Pali M7). remove_probe() deletes it."""
    me = t.data.copy()
    me.name = name
    table = json.loads(me[part_prop])
    bm = bmesh.new()
    bm.from_mesh(me)
    lay = bm.faces.layers.int.get("part")
    if lay is None:
        bm.free()
        raise KitRowError("probe_object: the mesh has no face attribute 'part'")
    M = np.array(t.matrix_world, dtype=np.float64)
    Minv = np.linalg.inv(M)
    for i, r in enumerate(rows):
        km = load_kit_mesh(r["mesh"])
        W = np.asarray(game_to_world(place_game(km["V"], r)), dtype=np.float64)
        L = W @ Minv[:3, :3].T + Minv[:3, 3]
        table.append(f"{prefix}{i:02d}_{r['prefab']}")
        pid = len(table)
        vs = [bm.verts.new(tuple(float(c) for c in p)) for p in L]
        for tri in km["tris"]:
            try:
                f = bm.faces.new((vs[tri[0]], vs[tri[1]], vs[tri[2]]))
            except ValueError:
                continue
            f[lay] = pid
    bm.to_mesh(me)
    bm.free()
    me[part_prop] = json.dumps(table)
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    ob.matrix_world = t.matrix_world.copy()
    bpy.context.view_layer.update()
    return ob


def remove_probe(ob):
    me = ob.data
    bpy.data.objects.remove(ob, do_unlink=True)
    bpy.data.meshes.remove(me)


__all__ = ["KitRowError", "load_kit_mesh", "yaw_of", "place_game", "host_mesh", "kit_ledge_offset", "aabb_gap",
           "check_kit_rows", "augment", "probe_object", "remove_probe", "MOUNT_GAP", "CLEAR_OFF", "ACCESS",
           "KIT_ROOT", "KIT_PART_PREFIX"]
