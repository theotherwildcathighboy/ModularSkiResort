"""
carrier_split.py - shared pieces of the carrier split (grip + body, 2026-11-03).

Plan: docs/research/carrier-pivot-swing-plan-2026-11-03.md sections 2, 5, 6, 8; brief:
docs/research/art/carrier-split-2026-11/BRIEF.md. Every carrier (FG triple, DQ, gondola 8, tram)
becomes two meshes so the code can pitch the GRIP with the rope and keep the BODY plumb (later:
swing it in the wind):

    grip  = the rope side (jaws / carriage / springs / wheels / trolley) + the hanger HEAD:
            a yoke (two cheek plates) that carries the pivot pin (pin ends on the cheeks' outer faces)
    body  = everything below the pin: the hanger, ending in a KNUCKLE (eye) between the cheeks,
            + the seat / cabin

The pin is a LATERAL axis (authoring y) and turns about that axis ONLY: the body may turn relative to
the grip by the pitch (+-PITCH_DEG, the rope slope) plus the fore-aft swing (+-FORE_AFT_DEG), both
about the pin. Swing convention (critique 2026-11-03, real carriers): the cross-wind sway is the
WHOLE assembly (grip + body) rolling about the rope point (chairs / gondola up to
ASSEMBLY_ROLL_DEG; tram about the track rope, up to TRAM_ROLL_DEG); it moves nothing at the pin, so
it needs no joint check. The joint is built so the pin sweep reads right:

  * the knuckle is a low-poly sphere centered on the pin center, its poles on the pin axis: a sphere
    turned about its own center is the same sphere, so the cheek gap stays the design gap c at every
    pose and nothing clips;
  * the cheeks stand c off the knuckle's poles; their footprint radius is kept small enough that the
    hanger leaving the knuckle never reaches them over the sweep (sweep_joint proves it).

Contracts (asserted by the generators through these helpers; every one raises):
  S1 rejoin: at zero pitch, body + grip = the old one-piece carrier within REJOIN_TOL_M,
     both ways (old vertex -> new surface, new vertex -> old surface), outside the joint zone (a
     sphere of radius r_x round each pin center, plus the replaced / new joint parts by name).
  S2 pin gap: at every pose of the pin sweep (pitch +-PITCH_DEG in PITCH_STEP_DEG steps, each plus
     the fore-aft steps FORE_AFT_STEPS x FORE_AFT_DEG, all about the pin) the gap between each
     knuckle and each of its cheeks is > 0 and <= GAP_MAX_M (one connected joint, no visible slot).
  S3 no clash: at every pose of that sweep no body triangle intersects a grip triangle (BVH
     overlap, the C13 method of lift_chair.py).

Frames: everything here works in the AUTHORING frame (x travel, y lateral, z up), on world vertex
arrays of the parts before the join (pitch and fore-aft = rotation about +y).
"""

import math
import os

import bmesh
import bpy
import mathutils
import numpy as np
from mathutils.bvhtree import BVHTree

PITCH_DEG = 45.0          # the rope slope at the pin (grip pitched, body plumb)
PITCH_STEP_DEG = 5.0
FORE_AFT_DEG = 3.0        # the body's fore-aft swing at the pin, on top of every pitch (same axis)
FORE_AFT_STEPS = (-1.0, 0.0, 1.0)            # fractions of FORE_AFT_DEG swept at every pitch
ASSEMBLY_ROLL_DEG = 8.0   # cross-wind sway of grip + body TOGETHER about the rope point (no joint check)
TRAM_ROLL_DEG = 2.0       # the tram's sway, cabin + carriage together about the track rope
GAP_MAX_M = 0.005         # S2: no visible gap over 5 mm
REJOIN_TOL_M = 0.002      # S1: body + grip = the old carrier within 2 mm
DESIGN_GAP_M = 0.0035     # knuckle pole -> cheek inner face (S2 holds it under 5 mm over the sweep)
KNUCKLE_SEGS = (8, 4)     # (around the pin axis, from pole to pole)


# --------------------------------------------------------------------------------------
# Geometry
# --------------------------------------------------------------------------------------

def _obj_from_bm(name, bm, relief="geo"):
    if bm.loops.layers.uv.get("UVMap") is None:
        bm.loops.layers.uv.new("UVMap")
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.update()
    obj = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(obj)
    obj["relief"] = relief
    return obj


def _box_uv(bm):
    import cs2_kit
    cs2_kit._box_project_uv(bm, bm.loops.layers.uv.get("UVMap"))


def bake_transform(obj):
    """Bake the object transform into the mesh (identity object matrix)."""
    obj.data.transform(obj.matrix_basis)
    obj.matrix_basis = mathutils.Matrix.Identity(4)
    obj.data.update()
    return obj


def knuckle(name, center, radius, segs=KNUCKLE_SEGS, relief="geo", meridian_xz=(1.0, 0.0)):
    """The body's eye: a UV sphere with its poles on the pin axis (authoring y), centered on the pin.
    segs = (around the pin axis, pole to pole); tris = 2 * u + 2 * u * (v - 2).
    meridian_xz = an (x, z) direction a meridian is turned onto: pass the direction of the plane
    the hanger tube is cut in (it contains the pin axis), so the buried cut ring meets the sphere's
    meridian polygon (inradius R cos(90 / v deg)), not a face between two meridians (lower)."""
    u, v = segs
    bm = bmesh.new()
    bm.loops.layers.uv.new("UVMap")
    bmesh.ops.create_uvsphere(bm, u_segments=int(u), v_segments=int(v), radius=float(radius),
                              calc_uvs=True)
    # create_uvsphere puts the poles on z: turn them onto y (x +90 deg)
    bmesh.ops.rotate(bm, verts=bm.verts[:], cent=(0.0, 0.0, 0.0),
                     matrix=mathutils.Matrix.Rotation(math.radians(90.0), 3, "X"))
    # turn one equator vertex onto meridian_xz (rotation about y by a: angle(x, z) -> angle - a)
    eq = [vt.co for vt in bm.verts if abs(vt.co.y) < 1e-6 * max(radius, 1.0)]
    if not eq:
        raise RuntimeError(f"{name}: v_segments={v} has no equator ring (use an even v)")
    th0 = math.atan2(eq[0].z, eq[0].x)
    phi = math.atan2(float(meridian_xz[1]), float(meridian_xz[0]))
    bmesh.ops.rotate(bm, verts=bm.verts[:], cent=(0.0, 0.0, 0.0),
                     matrix=mathutils.Matrix.Rotation(th0 - phi, 3, "Y"))
    bmesh.ops.translate(bm, verts=bm.verts[:], vec=mathutils.Vector(center))
    bm.normal_update()
    _box_uv(bm)
    obj = _obj_from_bm(name, bm, relief)
    return obj


def plate_profile(x0, x1, z_top, pin_xz, r_bottom, arc_segs=4):
    """Convex (x, z) outline of a yoke cheek, counter-clockwise seen from -y, top edge first:
    straight sides from z_top down to the pin height, closed below by a half circle of r_bottom
    about the pin. x0 / x1 = the side lines (|x - pin x| <= r_bottom keeps it convex)."""
    px, pz = pin_xz
    pts = [(x1, z_top), (x0, z_top)]
    # down the -x side, round the bottom, up the +x side (counter-clockwise seen from -y means
    # x -> -x along the top, then down: the slab helper checks convexity and orientation)
    for k in range(arc_segs + 1):
        a = math.pi + math.pi * k / arc_segs              # 180 -> 360 deg
        pts.append((px + r_bottom * math.cos(a), pz + r_bottom * math.sin(a)))
    # the arc starts at (px - r, pz) and ends at (px + r, pz); x0 / x1 must equal px -+ r
    out = []
    for p in pts:
        if not out or (abs(p[0] - out[-1][0]) > 1e-9 or abs(p[1] - out[-1][1]) > 1e-9):
            out.append(p)
    return out


def slab_y(name, pts_xz, y0, y1, open_first=True, relief="map:bolts"):
    """A convex (x, z) profile extruded from y0 to y1 (y0 < y1). The first edge (the top, buried in
    the head / bridge) is dropped when open_first. Side quads + two fan caps without a center
    vertex. Raises on a non-convex profile. Orientation-safe: the winding is fixed per face
    against the profile centroid."""
    n = len(pts_xz)
    cx = sum(p[0] for p in pts_xz) / n
    cz = sum(p[1] for p in pts_xz) / n
    # convexity (either winding)
    sgn = 0.0
    for i in range(n):
        (ax, az), (bx, bz), (qx, qz) = pts_xz[i], pts_xz[(i + 1) % n], pts_xz[(i + 2) % n]
        cr = (bx - ax) * (qz - bz) - (bz - az) * (qx - bx)
        if abs(cr) < 1e-12:
            raise ValueError(f"{name}: collinear profile points at {i + 1}")
        if sgn == 0.0:
            sgn = math.copysign(1.0, cr)
        elif math.copysign(1.0, cr) != sgn:
            raise ValueError(f"{name}: profile not convex at point {i + 1}")
    bm = bmesh.new()
    bm.loops.layers.uv.new("UVMap")
    lo = [bm.verts.new((x, y0, z)) for x, z in pts_xz]
    hi = [bm.verts.new((x, y1, z)) for x, z in pts_xz]
    ym = 0.5 * (y0 + y1)
    centre = mathutils.Vector((cx, ym, cz))

    def face(vs):
        f = bm.faces.new(vs)
        f.normal_update()
        if f.normal.dot(f.calc_center_median() - centre) < 0.0:
            f.normal_flip()
        return f

    for i in range(n):
        if open_first and i == 0:
            continue
        j = (i + 1) % n
        face((lo[i], lo[j], hi[j], hi[i]))
    for k in range(1, n - 1):
        face((lo[0], lo[k], lo[k + 1]))
        face((hi[0], hi[k], hi[k + 1]))
    bm.normal_update()
    _box_uv(bm)
    return _obj_from_bm(name, bm, relief)


def pin_end(name, centre_y_face, pin_xz, radius, length, outward, segs=6, relief="map:bolts"):
    """A pin end (hex head or nut) along y on a cheek's outer face: from y_face - 0.002 (buried) out
    to y_face + outward * length, capped on the outer end only."""
    import cs2_kit
    y_face = centre_y_face
    y_in, y_out = y_face - outward * 0.002, y_face + outward * length
    yc = 0.5 * (y_in + y_out)
    depth = abs(y_out - y_in)
    # cs2_kit cylinders run along local z; axis "Y" maps local +z onto +y: the +z cap = "top"
    cap = "top" if outward > 0 else "bottom"
    o = cs2_kit.add_cyl(name, radius, depth, (pin_xz[0], yc, pin_xz[1]), cs2_kit._axis_rot("Y"),
                        segs, cap)
    o["relief"] = relief
    return bake_transform(o)


def ring_loft(name, rings, segs, axis_pts, angle0=0.0, relief="geo"):
    """An uncapped loft of circular rings about a straight axis: rings = ((t, r), ...) with t the
    position along the axis from axis_pts[0] to axis_pts[1] (meters); angle0 = the angle of ring
    vertex 0 (to match a host tube's ring orientation). Quads only."""
    a = mathutils.Vector(axis_pts[0])
    b = mathutils.Vector(axis_pts[1])
    d = (b - a).normalized()
    ref = mathutils.Vector((1.0, 0.0, 0.0)) if abs(d.x) < 0.9 else mathutils.Vector((0.0, 1.0, 0.0))
    e1 = (ref - d * ref.dot(d)).normalized()
    e2 = d.cross(e1)
    bm = bmesh.new()
    bm.loops.layers.uv.new("UVMap")
    rv = []
    for t, r in rings:
        c = a + d * t
        rv.append([bm.verts.new(c + (e1 * math.cos(angle0 + 2 * math.pi * k / segs)
                                     + e2 * math.sin(angle0 + 2 * math.pi * k / segs)) * r)
                   for k in range(segs)])
    for i in range(len(rv) - 1):
        for k in range(segs):
            k1 = (k + 1) % segs
            f = bm.faces.new((rv[i][k], rv[i][k1], rv[i + 1][k1], rv[i + 1][k]))
            f.normal_update()
            cen = f.calc_center_median()
            axial = a + d * (cen - a).dot(d)
            if f.normal.dot(cen - axial) < 0.0:
                f.normal_flip()
    bm.normal_update()
    _box_uv(bm)
    return _obj_from_bm(name, bm, relief)


def octa(name, center, half_xy, z_top, relief="geo"):
    """Closed octahedron (8 tris): a LOD2 knuckle stand-in, its top vertex exactly at z_top (the
    body's top contract), its waist square of half-diagonal half_xy at the center height."""
    cx, cy, cz = center
    hz = z_top - cz
    bm = bmesh.new()
    bm.loops.layers.uv.new("UVMap")
    top = bm.verts.new((cx, cy, z_top))
    bot = bm.verts.new((cx, cy, cz - hz))
    ring = [bm.verts.new((cx + half_xy * math.cos(k * math.pi / 2.0),
                          cy + half_xy * math.sin(k * math.pi / 2.0), cz)) for k in range(4)]
    c = mathutils.Vector(center)
    for k in range(4):
        k1 = (k + 1) % 4
        for vs in ((ring[k], ring[k1], top), (ring[k1], ring[k], bot)):
            f = bm.faces.new(vs)
            f.normal_update()
            if f.normal.dot(f.calc_center_median() - c) < 0.0:
                f.normal_flip()
    bm.normal_update()
    _box_uv(bm)
    return _obj_from_bm(name, bm, relief)


def cut_part(obj, co, no):
    """Cut a part with the plane (co, no) and keep the side (p - co) . no <= 0 (the object's
    transform is baked first). The cut leaves an open ring: the caller buries it (knuckle / head)."""
    bake_transform(obj)
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
    bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-7, plane_co=mathutils.Vector(co),
                           plane_no=mathutils.Vector(no), clear_outer=True, clear_inner=False)
    loose = [v for v in bm.verts if not v.link_faces]
    if loose:
        bmesh.ops.delete(bm, geom=loose, context="VERTS")
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    return obj


# --------------------------------------------------------------------------------------
# Arrays (authoring frame)
# --------------------------------------------------------------------------------------

def part_arrays(parts):
    """[(obj, ...)] -> (V (n, 3) world, polys [[i...]], part index per poly, names)."""
    V, P, owner, names = [], [], [], []
    for k, item in enumerate(parts):
        o = item[0] if isinstance(item, (tuple, list)) else item
        m = o.matrix_world
        base = len(V)
        V.extend([tuple(m @ v.co) for v in o.data.vertices])
        for poly in o.data.polygons:
            P.append([base + i for i in poly.vertices])
            owner.append(k)
        names.append(o.name)
    return np.array(V, dtype=np.float64), P, np.array(owner, dtype=np.int64), names


def _tris(P):
    out = []
    for p in P:
        for k in range(1, len(p) - 1):
            out.append((p[0], p[k], p[k + 1]))
    return out


def rotate_about(V, pivot, pitch_deg, roll_deg):
    """Body pose relative to the grip: pitch about +y, then roll about +x, both through `pivot`."""
    p = np.radians(pitch_deg)
    r = np.radians(roll_deg)
    Ry = np.array([[math.cos(p), 0.0, math.sin(p)], [0.0, 1.0, 0.0], [-math.sin(p), 0.0, math.cos(p)]])
    Rx = np.array([[1.0, 0.0, 0.0], [0.0, math.cos(r), -math.sin(r)], [0.0, math.sin(r), math.cos(r)]])
    R = Rx @ Ry
    piv = np.asarray(pivot, dtype=np.float64)
    return (V - piv) @ R.T + piv


# --------------------------------------------------------------------------------------
# S1 rejoin
# --------------------------------------------------------------------------------------

def rejoin(old_parts, new_parts, pins, r_x, old_skip=(), new_skip=(), tol=REJOIN_TOL_M, label=""):
    """S1: body + grip at zero = the old carrier within `tol`, both ways, outside the joint zone
    (spheres of radius r_x round each pin center in `pins`; parts named with a prefix in
    old_skip / new_skip are joint parts and left out). Returns a report dict; raises over tol."""
    Vo, Po, owno, nameo = part_arrays(old_parts)
    Vn, Pn, ownn, namen = part_arrays(new_parts)
    to = BVHTree.FromPolygons([tuple(v) for v in Vo], Po)
    tn = BVHTree.FromPolygons([tuple(v) for v in Vn], Pn)

    def mask(V, P, own, names, skip):
        keep = np.ones(len(V), dtype=bool)
        for c in pins:
            keep &= np.linalg.norm(V - np.asarray(c), axis=1) > r_x
        bad_parts = {k for k, n in enumerate(names) if n.startswith(tuple(skip))} if skip else set()
        if bad_parts:
            used = np.zeros(len(V), dtype=bool)
            for poly, k in zip(P, own):
                if int(k) in bad_parts:
                    used[poly] = True
            keep &= ~used
        return keep

    ko = mask(Vo, Po, owno, nameo, old_skip)
    kn = mask(Vn, Pn, ownn, namen, new_skip)
    do = np.array([tn.find_nearest(tuple(v))[3] for v in Vo[ko]]) if ko.any() else np.zeros(0)
    dn = np.array([to.find_nearest(tuple(v))[3] for v in Vn[kn]]) if kn.any() else np.zeros(0)
    rep = {"old_vertices_checked": int(ko.sum()), "old_vertices_in_joint_zone": int((~ko).sum()),
           "new_vertices_checked": int(kn.sum()), "new_vertices_in_joint_zone": int((~kn).sum()),
           "max_old_to_new_mm": round(float(do.max()) * 1000.0, 3) if len(do) else 0.0,
           "max_new_to_old_mm": round(float(dn.max()) * 1000.0, 3) if len(dn) else 0.0,
           "joint_zone_r_m": r_x, "tol_mm": tol * 1000.0}
    worst = max(rep["max_old_to_new_mm"], rep["max_new_to_old_mm"]) / 1000.0
    if worst > tol:
        # name the offender for the fix
        if len(do) and do.max() > tol:
            i = int(np.flatnonzero(ko)[int(do.argmax())])
            rep["worst_old_vertex"] = [round(c, 4) for c in Vo[i]]
        if len(dn) and dn.max() > tol:
            i = int(np.flatnonzero(kn)[int(dn.argmax())])
            rep["worst_new_vertex"] = [round(c, 4) for c in Vn[i]]
        raise RuntimeError(f"S1 rejoin BROKEN {label}: {rep}")
    return rep


# --------------------------------------------------------------------------------------
# S2 / S3 sweep
# --------------------------------------------------------------------------------------

def sweep_joint(body_parts, grip_parts, joints, pitch_deg=PITCH_DEG, step_deg=PITCH_STEP_DEG,
                fore_aft_deg=FORE_AFT_DEG, fore_aft_steps=FORE_AFT_STEPS, gap_max=GAP_MAX_M, label=""):
    """S2 + S3 over the pin sweep: every pitch of +-pitch_deg plus every fore-aft step, all about the
    lateral pin (no lateral turn at the pin: the cross-wind sway rolls grip + body together, see the
    header). `joints` = [(pivot (x, y, z), body knuckle part names, grip cheek part names)]: every
    joint turns about its own pivot (several coaxial pins of one body, the tram's two lugs, share the
    axis, so one pivot serves: pass the first). The body is posed rigidly about joints[0]'s pivot.
    Raises on a clash or a gap outside (0, gap_max]."""
    Vb, Pb, ownb, nameb = part_arrays(body_parts)
    Vg, Pg, owng, nameg = part_arrays(grip_parts)
    Tg = _tris(Pg)
    tg = BVHTree.FromPolygons([tuple(v) for v in Vg], Tg)
    Tb = _tris(Pb)
    pivot = np.asarray(joints[0][0], dtype=np.float64)
    # knuckle vertex sets and cheek trees
    jsets = []
    for joint in joints:
        piv, kn, ch = joint[:3]
        floor = joint[3] if len(joint) > 3 else ()
        kidx = sorted({i for poly, k in zip(Pb, ownb) if nameb[int(k)] in kn for i in poly})
        if not kidx:
            raise RuntimeError(f"S2 {label}: no knuckle vertices for {kn}")
        trees = []
        for c in ch:
            polys = [poly for poly, k in zip(Pg, owng) if nameg[int(k)] == c]
            if not polys:
                raise RuntimeError(f"S2 {label}: no cheek faces for {c}")
            trees.append((c, BVHTree.FromPolygons([tuple(v) for v in Vg], _tris(polys))))
        fpolys = [poly for poly, k in zip(Pg, owng) if nameg[int(k)] in floor]
        ftree = BVHTree.FromPolygons([tuple(v) for v in Vg], _tris(fpolys)) if fpolys else None
        jsets.append((np.array(kidx), trees, ftree))
    pitches = np.arange(-pitch_deg, pitch_deg + 1e-9, step_deg)
    fores = [fore_aft_deg * f for f in fore_aft_steps]
    worst_gap, min_gap, poses, clashes = 0.0, 1e9, 0, []
    floor_lo, floor_hi = 1e9, 0.0
    gap_at = None
    for p in pitches:
        for r in fores:
            W = rotate_about(Vb, pivot, p + r, 0.0)
            tb = BVHTree.FromPolygons([tuple(v) for v in W], Tb)
            hits = tb.overlap(tg)
            poses += 1
            if hits:
                bi, gi = hits[0]
                bp = nameb[int(ownb[_tri_owner(Pb, bi)])]
                gp = nameg[int(owng[_tri_owner(Pg, gi)])]
                clashes.append((round(float(p), 1), round(float(r), 1), len(hits), bp, gp))
            for kidx, trees, ftree in jsets:
                for cname, tree in trees:
                    g = min(tree.find_nearest(tuple(W[i]))[3] for i in kidx)
                    min_gap = min(min_gap, g)
                    if g > worst_gap:
                        worst_gap, gap_at = g, (round(float(p), 1), round(float(r), 1), cname)
                if ftree is not None:
                    g = min(ftree.find_nearest(tuple(W[i]))[3] for i in kidx)
                    floor_lo, floor_hi = min(floor_lo, g), max(floor_hi, g)
    rep = {"poses": poses, "pitch_deg": [float(pitches[0]), float(pitches[-1]), step_deg],
           "fore_aft_deg": [float(min(fores)), float(max(fores))],
           "pin_rel_deg": [float(pitches[0] + min(fores)), float(pitches[-1] + max(fores))],
           "roll_deg": [0.0, 0.0],
           "assembly_roll_note": f"cross-wind sway = grip + body rolled together about the rope point "
                                 f"(chairs / gondola <= {ASSEMBLY_ROLL_DEG:g} deg, tram <= "
                                 f"{TRAM_ROLL_DEG:g} deg about the track rope): nothing turns at the pin, "
                                 f"no joint check",
           "clash_poses": len(clashes), "first_clashes": clashes[:4],
           "gap_mm_min": round(min_gap * 1000.0, 2), "gap_mm_max": round(worst_gap * 1000.0, 2),
           "gap_max_at": gap_at, "gap_limit_mm": gap_max * 1000.0}
    if floor_hi > 0.0:
        # report only: the slot floor (head / bridge underside) over the knuckle: the eye must
        # turn under it, so its clearance follows the knuckle polygon (c .. c + R (1 - cos(180/u)))
        rep["slot_floor_mm"] = [round(floor_lo * 1000.0, 2), round(floor_hi * 1000.0, 2)]
    if clashes:
        raise RuntimeError(f"S3 no-clash BROKEN {label}: {rep}")
    if worst_gap > gap_max or min_gap <= 0.0:
        raise RuntimeError(f"S2 pin gap BROKEN {label}: {rep}")
    return rep


def _tri_owner(P, tri_index):
    """Polygon index of triangle `tri_index` of _tris(P)."""
    n = 0
    for i, p in enumerate(P):
        k = len(p) - 2
        if tri_index < n + k:
            return i
        n += k
    return len(P) - 1


# --------------------------------------------------------------------------------------
# Manifest hygiene
# --------------------------------------------------------------------------------------

def manifest_args(args, repo):
    """vars(args) for a manifest that lands in the repo: every absolute path under `repo` becomes
    repo-relative (forward slashes), any other one under the home folder starts with %USERPROFILE%
    (tools/check-no-user-paths.js: no home paths in tracked files). The defaults of --carriers /
    --preview / --out are absolute, so a build that relies on them would otherwise leak them.
    realpath expands Windows 8.3 short names (%TEMP% often holds the short form of the home folder)."""
    home = os.path.normcase(os.path.realpath(os.path.expanduser("~")))
    repo = os.path.realpath(repo)
    root = os.path.normcase(repo)
    out = {}
    for k, v in vars(args).items():
        if isinstance(v, str) and v and os.path.isabs(v):
            p = os.path.realpath(v)
            pn = os.path.normcase(p)
            if pn == root or pn.startswith(root + os.sep):
                v = os.path.relpath(p, repo).replace(os.sep, "/")
            elif pn == home or pn.startswith(home + os.sep):
                v = "%USERPROFILE%" + p[len(home):].replace(os.sep, "/")
        out[k] = v
    return out


# --------------------------------------------------------------------------------------
# Pivot numbers
# --------------------------------------------------------------------------------------

def to_prefab(a):
    """Authoring (x travel, y lateral, z up) -> prefab / FBX raw (x lateral, y up, z travel)."""
    return [round(float(a[1]), 4), round(float(a[2]), 4), round(float(a[0]), 4)]


def pivot_numbers(rope_pt, pin, grip_z0, body_z0, hang, grip_top_above_rope, outboard_note=""):
    """The constants the code reads (BRIEF 'Pivots'): grip rope point and pin center relative to the
    grip origin, body pin center relative to the body origin; authoring and prefab frames.
    grip_z0 / body_z0 = the assembled-frame z of each mesh's origin (its lowest vertex)."""
    rp_g = (rope_pt[0], rope_pt[1], rope_pt[2] - grip_z0)
    pin_g = (pin[0], pin[1], pin[2] - grip_z0)
    pin_b = (pin[0], pin[1], pin[2] - body_z0)
    r = lambda t: [round(float(c), 4) for c in t]
    return {
        "frame_note": "authoring = (x travel, y lateral, z up); prefab = (x lateral, y up, z travel) "
                      "= FBX raw / prefab-local" + (f"; {outboard_note}" if outboard_note else ""),
        "grip_origin_in_assembled_m": [0.0, 0.0, round(float(grip_z0), 4)],
        "body_origin_in_assembled_m": [0.0, 0.0, round(float(body_z0), 4)],
        "grip_rope_point_m": {"authoring": r(rp_g), "prefab": to_prefab(rp_g)},
        "grip_pin_center_m": {"authoring": r(pin_g), "prefab": to_prefab(pin_g)},
        "body_pin_center_m": {"authoring": r(pin_b), "prefab": to_prefab(pin_b)},
        "hang_m": round(float(hang), 4),
        "grip_top_above_rope_m": round(float(grip_top_above_rope), 4),
        "pin_axis": "lateral (authoring y = prefab x)",
    }
