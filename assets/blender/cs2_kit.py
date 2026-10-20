"""
cs2_kit - parts kit for the CS2 lift assets (art pipeline v2 section 3.2, round L spec).

CONTRACT
    Primitives return ONE object (name used verbatim):
        add_cyl(name, r, depth, loc, rot=(0,0,0), segments=12, caps="both", r2=None)
        add_box(name, size, loc, rot=(0,0,0), open_bottom=False)
        add_wedge(name, x0, x1, width, z0, z1, open_bottom=False)
        tube_along(name, pts, radius, segments=8, caps="none", bend_rings=8)
    Assemblies return a parts list [(obj, quad), ...] - the same shape every generator
    already feeds to cs2_asset.tag_parts / unwrap_into_quadrant / join:
        flange, gusset, stiffener_ring, handrail, grating, cable_clamp, sheave, door,
        hatch, drip_edge, axle_cap
    Every object created here carries obj["relief"] in
        {"geo", "map:seams", "map:bolts", "map:corrugation", "map:grate", "map:louvre", "map:helix"}
    ("geo" = the relief IS the geometry; "map:<kind>" = cs2_compose.detail_height paints it).
    Assembly objects are named with a family PREFIX so make_lod_chain delete lists can match
    them: flange_, gusset_, stiff_, post_, rail_, kick_, grate_, clamp_, sheave_, door_,
    hatch_, drip_, axlecap_ - plus bolt_ for every real stud (flange bolts, clamp studs,
    axle-cap studs) so LOD1 can drop all bolt geometry with one prefix. tube_along names its
    object verbatim (callers pass "tube_..." or "rail_..." as they need).
    Quads are cs2_asset.QUADS indices: 0 concrete, 1 galvanized, 2 glass, 3 painted steel;
    every assembly has a trailing `quad=` (or quad_rim/quad_hub) keyword with the default
    today's generators use for that part family.

ENGINE-BASE RULE (the reason open_bottom exists; spec "Engine facts", BatchMeshHelpers
AddBaseLines): a Building mesh gets its foundation skirt built by the ENGINE from every
edge whose two vertices lie in the band y = 0 +- 0.01 with a face rising above it, and an
edge is DROPPED when any face touching it lies entirely at/below the band. So a ground
part must have NO bottom face and NO geometry below z = 0 (Blender Z = CS2 Y): build it
with open_bottom=True and loc.z = size_z / 2 so the side-face bottom edges land exactly
on z = 0. tests/test_kit.py asserts exactly that.

GEOMETRY CONVENTIONS
    - Meshes are built with bmesh in LOCAL space; `loc` / `rot` go on the object (as
      today's primitive_*_add parts), so join() bakes them. Vertex positions are float32:
      loc.z = size_z / 2 and the local -size_z / 2 cancel exactly.
    - Axes: `axis` is one of "X" "Y" "Z" (optionally signed "-Y" where the sign is the
      OUTWARD direction, e.g. axle_cap) or an explicit (rx, ry, rz) euler in radians.
    - `facing` (door, hatch) is "+X" "-X" "+Y" "-Y" or an (dx, dy) vector: the OUTWARD wall
      normal; the part is built for +X and yawed.
    - Every part gets a "UVMap" layer: cylinders/cubes use bmesh calc_uvs, custom shells a
      per-face dominant-axis box projection in meters, tube_along its strip
      (u = k / segments with the seam on ring vertex 0, v = arc length / (2 pi r)).
      Generators normally overwrite these with cs2_asset.unwrap_into_quadrant.
    - No top-level side effects; nothing here shades, tags, joins or exports.
"""

import math

import bpy
import bmesh
from mathutils import Vector, Matrix, Euler

import cs2_asset  # noqa: F401  (shared constants; QUADS indices documented above)

QUAD_CONCRETE, QUAD_GALV, QUAD_GLASS, QUAD_PAINT = 0, 1, 2, 3

RELIEF_KINDS = ("geo", "map:seams", "map:bolts", "map:corrugation", "map:grate",
                "map:louvre", "map:helix")

# Every family prefix an LOD delete list may match (bolt_ covers all real studs).
PREFIXES = ("flange_", "gusset_", "stiff_", "post_", "rail_", "kick_", "grate_", "clamp_",
            "sheave_", "door_", "hatch_", "drip_", "axlecap_", "tube_", "bolt_")

_FACE_DIRS = {"+X": Vector((1, 0, 0)), "-X": Vector((-1, 0, 0)),
              "+Y": Vector((0, 1, 0)), "-Y": Vector((0, -1, 0)),
              "+Z": Vector((0, 0, 1)), "-Z": Vector((0, 0, -1))}


# --------------------------------------------------------------------------------------
# Internal helpers
# --------------------------------------------------------------------------------------

def _axis_rot(axis):
    """Euler that maps local +Z onto `axis` ("X","Y","Z", signed, or an euler tuple)."""
    if not isinstance(axis, str):
        return tuple(axis)
    a = axis.upper().replace("+", "")
    table = {"Z": (0.0, 0.0, 0.0), "-Z": (math.pi, 0.0, 0.0),
             "X": (0.0, math.pi / 2, 0.0), "-X": (0.0, -math.pi / 2, 0.0),
             "Y": (-math.pi / 2, 0.0, 0.0), "-Y": (math.pi / 2, 0.0, 0.0)}
    if a not in table:
        raise ValueError(f"axis must be X/Y/Z (optionally signed) or an euler, got {axis!r}")
    return table[a]


def _axis_vec(axis):
    """World unit vector for `axis` (same spellings as _axis_rot)."""
    return Euler(_axis_rot(axis)).to_matrix() @ Vector((0.0, 0.0, 1.0))


def _facing_yaw(facing):
    """Yaw (radians about Z) that turns local +X into the outward direction `facing`."""
    if isinstance(facing, str):
        f = facing.upper().replace("+", "")
        table = {"X": 0.0, "-X": math.pi, "Y": math.pi / 2, "-Y": -math.pi / 2}
        if f not in table:
            raise ValueError(f"facing must be +X/-X/+Y/-Y or a (dx, dy) vector, got {facing!r}")
        return table[f]
    dx, dy = facing[0], facing[1]
    return math.atan2(dy, dx)


def _yaw_point(x, y, yaw):
    c, s = math.cos(yaw), math.sin(yaw)
    return (c * x - s * y, s * x + c * y)


def _new_obj(name, bm, loc=(0, 0, 0), rot=(0, 0, 0), relief="geo"):
    """bmesh -> linked mesh object with loc/rot on the object and the relief tag."""
    if relief not in RELIEF_KINDS:
        raise ValueError(f"{name}: relief {relief!r} not in {RELIEF_KINDS}")
    if bm.loops.layers.uv.get("UVMap") is None:
        bm.loops.layers.uv.new("UVMap")
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.update()
    obj = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(obj)
    obj.location = loc
    obj.rotation_euler = rot
    obj["relief"] = relief
    return obj


def _box_project_uv(bm, layer=None, scale=1.0):
    """Per-face dominant-axis planar projection in meters (no mirrored islands)."""
    layer = layer or bm.loops.layers.uv.get("UVMap") or bm.loops.layers.uv.new("UVMap")
    bm.normal_update()
    for f in bm.faces:
        n = f.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        sgn = 1.0 if n[ax] >= 0 else -1.0
        others = [i for i in range(3) if i != ax]
        for lp in f.loops:
            co = lp.vert.co
            lp[layer].uv = (sgn * co[others[0]] * scale, co[others[1]] * scale)


def _drop_faces(bm, dirs, tol=0.9):
    """Delete faces whose normal aligns with any of the given world-direction keys."""
    if not dirs:
        return
    bm.normal_update()
    vecs = [_FACE_DIRS[d] for d in dirs]
    dead = [f for f in bm.faces if any(f.normal.dot(v) > tol for v in vecs)]
    if dead:
        bmesh.ops.delete(bm, geom=dead, context="FACES")


def _cyl_bm(r, depth, segments, caps="both", r2=None):
    if caps not in ("both", "top", "bottom", "none"):
        raise ValueError(f"caps must be both/top/bottom/none, got {caps!r}")
    bm = bmesh.new()
    bm.loops.layers.uv.new("UVMap")
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=True, segments=int(segments),
                          radius1=float(r), radius2=float(r if r2 is None else r2),
                          depth=float(depth), calc_uvs=True)
    bm.normal_update()
    drop = []
    if caps in ("none", "bottom"):
        drop.append("+Z")
    if caps in ("none", "top"):
        drop.append("-Z")
    _drop_faces(bm, drop)
    return bm


def _box_bm(size, drop=()):
    bm = bmesh.new()
    bm.loops.layers.uv.new("UVMap")
    bmesh.ops.create_cube(bm, size=1.0, calc_uvs=True)
    sx, sy, sz = size
    for v in bm.verts:
        v.co = (v.co.x * sx, v.co.y * sy, v.co.z * sz)
    bm.normal_update()
    _drop_faces(bm, drop)
    # meters-scale projection so the default UVs keep 3D proportions after the scale
    _box_project_uv(bm)
    return bm


def _ring_bm(outer, inner, d0, d1, map_fn, faces=("outer", "inner", "top", "bottom")):
    """
    Rectangular ring (frame) in a (u, v, d) frame mapped to local XYZ by `map_fn`:
    outer/inner are (w, h) rectangles centered on the origin, d0..d1 the depth extent.
    Face groups: outer walls, inner walls, "top" (d1 rim) and "bottom" (d0 rim).
    """
    bm = bmesh.new()
    bm.loops.layers.uv.new("UVMap")
    (wo, ho), (wi, hi) = outer, inner
    ro = [(-wo / 2, -ho / 2), (wo / 2, -ho / 2), (wo / 2, ho / 2), (-wo / 2, ho / 2)]
    ri = [(-wi / 2, -hi / 2), (wi / 2, -hi / 2), (wi / 2, hi / 2), (-wi / 2, hi / 2)]
    V = {}
    for tag, rect in (("o", ro), ("i", ri)):
        for k, (u, v) in enumerate(rect):
            for dtag, d in (("0", d0), ("1", d1)):
                V[tag + dtag + str(k)] = bm.verts.new(map_fn(u, v, d))
    for k in range(4):
        k1 = (k + 1) % 4
        if "outer" in faces:
            bm.faces.new((V[f"o0{k}"], V[f"o0{k1}"], V[f"o1{k1}"], V[f"o1{k}"]))
        if "inner" in faces:
            bm.faces.new((V[f"i0{k1}"], V[f"i0{k}"], V[f"i1{k}"], V[f"i1{k1}"]))
        if "top" in faces:
            bm.faces.new((V[f"o1{k}"], V[f"o1{k1}"], V[f"i1{k1}"], V[f"i1{k}"]))
        if "bottom" in faces:
            bm.faces.new((V[f"o0{k1}"], V[f"o0{k}"], V[f"i0{k}"], V[f"i0{k1}"]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    _box_project_uv(bm)
    return bm


def _orient_outward(bm, probe_dir_fn):
    """
    Flip all faces if the face nearest the probe points inward. `probe_dir_fn(face)`
    returns the expected outward direction for a face; the face with the largest
    |co| is used as the sample.
    """
    bm.normal_update()
    f = max(bm.faces, key=lambda f: f.calc_center_median().length)
    if f.normal.dot(probe_dir_fn(f)) < 0:
        bmesh.ops.reverse_faces(bm, faces=bm.faces[:])
        bm.normal_update()


def _object_matrix(obj):
    """
    loc/rot/scale matrix built from the object's own transform: matrix_world is stale
    on an object created in this script until the depsgraph updates (no parenting used).
    """
    return Matrix.LocRotScale(obj.location, obj.rotation_euler, obj.scale)


def _mesh_world_bounds(obj):
    """World-space AABB from the mesh vertices (bound_box needs a depsgraph update)."""
    mw = _object_matrix(obj)
    pts = [mw @ v.co for v in obj.data.vertices]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return lo, hi


# --------------------------------------------------------------------------------------
# Primitives
# --------------------------------------------------------------------------------------

def add_cyl(name, r, depth, loc, rot=(0, 0, 0), segments=12, caps="both", r2=None):
    """
    Cylinder (or cone when r2 differs) along local Z centered on `loc`, bmesh create_cone
    with FAN caps (no n-gon). caps in {"both", "top", "bottom", "none"}: drop the caps
    that end up buried (rungs, posts, links, legs inside other parts). Rule of thumb from
    the plan: r < 0.02 m -> segments=4.
    """
    bm = _cyl_bm(r, depth, segments, caps, r2)
    return _new_obj(name, bm, loc, rot, "geo")


def add_box(name, size, loc, rot=(0, 0, 0), open_bottom=False, drop=()):
    """
    Axis-aligned box of `size` (sx, sy, sz) centered on `loc`. open_bottom=True deletes the
    -Z face so the side-face bottom edges become engine-base edges (place with
    loc.z = sz / 2 for a ground part). `drop` is an extra set of face keys
    ("+X", "-X", "+Y", "-Y", "+Z", "-Z") to delete for buried faces.
    """
    keys = set(drop)
    if open_bottom:
        keys.add("-Z")
    bm = _box_bm(size, keys)
    return _new_obj(name, bm, loc, rot, "geo")


def add_wedge(name, x0, x1, width, z0, z1, open_bottom=False):
    """
    Vertical-sided ramp block at the origin: flat bottom at z = 0, top sloping from z0 (at
    x0) to z1 (at x1), `width` across Y. A zero height at either end (the ramp toe, z0 = 0)
    collapses that end cleanly (no zero-area faces). open_bottom removes the bottom face
    so the engine builds the skirt from the z = 0 side edges.
    """
    hw = width / 2.0
    bm = bmesh.new()
    bm.loops.layers.uv.new("UVMap")
    P = [(x0, -hw, 0), (x0, hw, 0), (x1, hw, 0), (x1, -hw, 0),
         (x0, -hw, z0), (x0, hw, z0), (x1, hw, z1), (x1, -hw, z1)]
    V = [bm.verts.new(p) for p in P]
    F = [(4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    if not open_bottom:
        F.insert(0, (0, 3, 2, 1))
    for idx in F:
        vs = [V[i] for i in idx]
        uniq = []
        for v in vs:
            if not any((v.co - u.co).length < 1e-7 for u in uniq):
                uniq.append(v)
        if len(uniq) >= 3:
            bm.faces.new(uniq)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-6)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    _box_project_uv(bm)
    return _new_obj(name, bm, (0, 0, 0), (0, 0, 0), "geo")


def _catmull_fillet(A, B, tin, tout, n):
    """
    n points (A inclusive .. B inclusive) on a cubic Hermite / Catmull-Rom blend from A to
    B whose end tangents are the incoming and outgoing straight directions (tangent
    continuous with both straights, so the tube shows no kink).
    """
    m = (B - A).length
    pts = []
    for k in range(n):
        t = k / (n - 1)
        h00 = 2 * t ** 3 - 3 * t ** 2 + 1
        h10 = t ** 3 - 2 * t ** 2 + t
        h01 = -2 * t ** 3 + 3 * t ** 2
        h11 = t ** 3 - t ** 2
        pts.append(A * h00 + tin * (m * h10) + B * h01 + tout * (m * h11))
    return pts


def tube_along(name, pts, radius, segments=8, caps="none", bend_rings=8, fillet=None,
               bend_min_deg=1.0):
    """
    Tube swept along the polyline `pts` (>= 2 points). Rings sit ONLY at the path ends and
    across each bend: every interior point whose direction change exceeds `bend_min_deg`
    is replaced by a Catmull-Rom/Hermite fillet that leaves the incoming straight `f`
    before the corner and rejoins the outgoing one `f` after it, carrying `bend_rings`
    rings (both fillet ends included); straight runs get no intermediate rings.
    `f` = min(fillet, 0.5 * shorter adjacent span) with fillet=None meaning the fillets of
    neighboring bends meet (a continuous sweep, e.g. the chair J-stem). Ring frames are
    parallel-transported (no twist, no up-vector singularity).
    Ring count on a path with b bends and no touching fillets = 2 + b * bend_rings.
    UV strip: u = k / segments (seam on ring vertex 0), v = arc length / (2 pi radius).
    caps in {"none", "both", "start", "end"} - fan caps; "none" where the ends are buried.
    Named verbatim (pass "tube_..." / "rail_..." yourself). Returns the object.
    """
    if caps not in ("none", "both", "start", "end"):
        raise ValueError(f"caps must be none/both/start/end, got {caps!r}")
    if bend_rings < 2:
        raise ValueError("bend_rings must be >= 2")
    P = [Vector(p) for p in pts]
    if len(P) < 2:
        raise ValueError(f"{name}: tube_along needs >= 2 points")
    # centerline samples (position, tangent)
    dirs = []
    for a, b in zip(P, P[1:]):
        d = b - a
        if d.length < 1e-9:
            raise ValueError(f"{name}: coincident consecutive points in path")
        dirs.append(d.normalized())
    samples = [(P[0].copy(), dirs[0].copy())]
    for i in range(1, len(P) - 1):
        tin, tout = dirs[i - 1], dirs[i]
        ang = math.degrees(tin.angle(tout))
        if ang < bend_min_deg:
            continue                                   # collinear: no ring on a straight
        lin, lout = (P[i] - P[i - 1]).length, (P[i + 1] - P[i]).length
        f = 0.5 * min(lin, lout)
        if fillet is not None:
            f = min(f, float(fillet))
        A, B = P[i] - tin * f, P[i] + tout * f
        for k, q in enumerate(_catmull_fillet(A, B, tin, tout, bend_rings)):
            t = (k / (bend_rings - 1))
            tang = (tin * (1 - t) + tout * t)
            if k > 0:
                prev = samples[-1][0]
                tang = (q - prev) if (q - prev).length > 1e-9 else tang
            samples.append((q, tang.normalized()))
    samples.append((P[-1].copy(), dirs[-1].copy()))
    # dedupe coincident rings (touching fillets)
    clean = [samples[0]]
    for q, t in samples[1:]:
        if (q - clean[-1][0]).length > 1e-6:
            clean.append((q, t))
    # smooth tangents at ring positions: central difference along the ring chain
    ring_pts = [q for q, _ in clean]
    tangents = []
    for i, (q, t) in enumerate(clean):
        if 0 < i < len(clean) - 1:
            t = (ring_pts[i + 1] - ring_pts[i - 1])
        elif i == 0 and len(clean) > 1:
            t = clean[0][1]
        tangents.append(t.normalized())
    # parallel-transported frames
    t0 = tangents[0]
    up = Vector((0, 0, 1)) if abs(t0.z) < 0.9 else Vector((1, 0, 0))
    N = (up - t0 * up.dot(t0)).normalized()
    frames = []
    for t in tangents:
        N = (N - t * N.dot(t))
        if N.length < 1e-6:
            up = Vector((0, 0, 1)) if abs(t.z) < 0.9 else Vector((1, 0, 0))
            N = (up - t * up.dot(t))
        N = N.normalized()
        frames.append((t, N, t.cross(N)))
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    rings = []
    for q, (t, n, b) in zip(ring_pts, frames):
        ring = []
        for k in range(segments):
            a = 2 * math.pi * k / segments
            ring.append(bm.verts.new(q + n * (radius * math.cos(a)) + b * (radius * math.sin(a))))
        rings.append(ring)
    arc = [0.0]
    for a, b in zip(ring_pts, ring_pts[1:]):
        arc.append(arc[-1] + (b - a).length)
    circ = 2 * math.pi * radius
    for i, (ra, rb) in enumerate(zip(rings, rings[1:])):
        va, vb = arc[i] / circ, arc[i + 1] / circ
        for k in range(segments):
            k1 = (k + 1) % segments
            f = bm.faces.new((ra[k], ra[k1], rb[k1], rb[k]))
            u0, u1 = k / segments, (k + 1) / segments
            for lp, (u, v) in zip(f.loops, ((u0, va), (u1, va), (u1, vb), (u0, vb))):
                lp[uv].uv = (u, v)
    def cap(ring, centre, tang, v_at, start):
        c = bm.verts.new(centre)
        for k in range(segments):
            k1 = (k + 1) % segments
            tri = (c, ring[k1], ring[k]) if start else (c, ring[k], ring[k1])
            f = bm.faces.new(tri)
            for lp in f.loops:
                d = lp.vert.co - centre
                lp[uv].uv = (0.5 + d.dot(frames[0][1] if start else frames[-1][1]) / circ,
                             v_at + d.dot(frames[0][2] if start else frames[-1][2]) / circ)
    if caps in ("both", "start"):
        cap(rings[0], ring_pts[0], tangents[0], arc[0] / circ, True)
    if caps in ("both", "end"):
        cap(rings[-1], ring_pts[-1], tangents[-1], arc[-1] / circ, False)
    bm.normal_update()
    obj = _new_obj(name, bm, (0, 0, 0), (0, 0, 0), "geo")
    obj["rings"] = len(rings)
    return obj


# --------------------------------------------------------------------------------------
# Assemblies
# --------------------------------------------------------------------------------------

def flange(name, r, t=0.03, bolts=8, segs=16, loc=(0, 0, 0), axis="Z", bolt_geo=False,
           quad=QUAD_PAINT, quad_bolt=QUAD_GALV, caps="both"):
    """
    Base / collar flange: a disc of radius r, thickness t, centered on `loc` with its axis
    along `axis`. The bolt circle is painted (relief "map:bolts") unless bolt_geo=True,
    which adds `bolts` real 6-seg studs 0.03 m proud of the +axis face (ground-level leg
    and mast flanges only). Names: flange_<name>, bolt_<name>_<k>.
    """
    rot = _axis_rot(axis)
    disc = add_cyl(f"flange_{name}", r, t, loc, rot, segs, caps)
    disc["relief"] = "geo" if bolt_geo else "map:bolts"
    parts = [(disc, quad)]
    if bolt_geo:
        rb = min(0.025, max(0.010, 0.06 * r))
        hb = 0.03
        M = Euler(rot).to_matrix()
        for k in range(bolts):
            a = 2 * math.pi * (k + 0.5) / bolts
            local = Vector((0.78 * r * math.cos(a), 0.78 * r * math.sin(a), t / 2 + hb / 2))
            stud = add_cyl(f"bolt_{name}_{k}", rb, hb, Vector(loc) + M @ local, rot, 6, "top")
            parts.append((stud, quad_bolt))
    return parts


def gusset(name, a, b, t=0.02, loc=(0, 0, 0), rot=(0, 0, 0), quad=QUAD_PAINT):
    """
    Triangular web plate: right triangle in the local XZ plane with legs a (along +X) and
    b (along +Z), thickness t across Y (centered). Place `loc` at the inside corner (the
    beam/leg junction) and `rot` it into the joint. 8 tris. Name: gusset_<name>.
    """
    bm = bmesh.new()
    bm.loops.layers.uv.new("UVMap")
    h = t / 2.0
    p = [(0, -h, 0), (a, -h, 0), (0, -h, b), (0, h, 0), (a, h, 0), (0, h, b)]
    V = [bm.verts.new(q) for q in p]
    for idx in ((0, 2, 1), (3, 4, 5), (0, 1, 4, 3), (0, 3, 5, 2), (1, 2, 5, 4)):
        bm.faces.new([V[i] for i in idx])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    _box_project_uv(bm)
    return [(_new_obj(f"gusset_{name}", bm, loc, rot, "geo"), quad)]


def stiffener_ring(name, r, w=0.06, t=0.02, loc=(0, 0, 0), axis="Z", segs=16,
                   quad=QUAD_PAINT, r_in=None):
    """
    Welded stiffener ring around a tube of radius r: an annulus from r_in (default 0.97 r,
    tucked inside the host's facets) to r + w, thickness t along `axis`. The inner wall is
    buried in the tube and is not built: outer wall + two annuli = 6 * segs tris.
    Name: stiff_<name>.
    """
    ri = r * 0.97 if r_in is None else r_in
    ro = r + w
    bm = bmesh.new()
    bm.loops.layers.uv.new("UVMap")
    rings = {}
    for tag, rad in (("i", ri), ("o", ro)):
        for ztag, z in (("0", -t / 2), ("1", t / 2)):
            rings[tag + ztag] = [bm.verts.new((rad * math.cos(2 * math.pi * k / segs),
                                               rad * math.sin(2 * math.pi * k / segs), z))
                                 for k in range(segs)]
    for k in range(segs):
        k1 = (k + 1) % segs
        bm.faces.new((rings["o0"][k], rings["o0"][k1], rings["o1"][k1], rings["o1"][k]))
        bm.faces.new((rings["o1"][k], rings["o1"][k1], rings["i1"][k1], rings["i1"][k]))
        bm.faces.new((rings["o0"][k1], rings["o0"][k], rings["i0"][k], rings["i0"][k1]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    _box_project_uv(bm)
    return [(_new_obj(f"stiff_{name}", bm, loc, _axis_rot(axis), "geo"), quad)]


def handrail(name, path_pts, h=1.10, knee=0.55, kick=0.10, post_every=1.5, segs=6,
             radius=0.028, quad=QUAD_GALV, post_radius=None):
    """
    Guard rail along a 3D polyline (deck-level points): uncapped `segs`-gon posts every
    `post_every` m (both ends of every span always get one), a top rail at +h and a knee
    rail at +knee (tube_along, tight fillets at the bends, overhanging the end posts by
    1.5 radius so the open post tops stay covered), and a kick plate 0.02 thick x `kick`
    tall along each span. Names: post_<name>_<i>, rail_<name>_top / _knee, kick_<name>_<i>.
    """
    P = [Vector(p) for p in path_pts]
    if len(P) < 2:
        raise ValueError(f"handrail {name}: need >= 2 path points")
    pr = radius if post_radius is None else post_radius
    parts = []
    # posts
    post_pts = []
    for a, b in zip(P, P[1:]):
        d = b - a
        n = max(1, int(math.ceil(d.length / post_every - 1e-6)))
        for j in range(n + 1):
            q = a + d * (j / n)
            if not any((q - s).length < 1e-6 for s in post_pts):
                post_pts.append(q)
    for i, q in enumerate(post_pts):
        parts.append((add_cyl(f"post_{name}_{i}", pr, h, (q.x, q.y, q.z + h / 2),
                              (0, 0, 0), segs, "none"), quad))
    # rails: extend the path past the end posts so the uncapped post tops stay covered
    ext = 1.5 * radius
    d0, d1 = (P[1] - P[0]).normalized(), (P[-1] - P[-2]).normalized()
    for tag, dz in (("top", h), ("knee", knee)):
        rp = [P[0] - d0 * ext] + P[1:-1] + [P[-1] + d1 * ext] if len(P) > 2 else \
             [P[0] - d0 * ext, P[-1] + d1 * ext]
        rp = [(q.x, q.y, q.z + dz) for q in rp]
        # Fillet = 3 radii, not 1: with a fillet as tight as the tube itself the rings on
        # the INSIDE of a 90 deg bend cross each other (ring spacing 14 mm vs 2r*sin 15 deg
        # = 14.5 mm) and leave a folded sliver facet with zero UV area - the top station's
        # bay rail tripped the validator on exactly that (DECISIONS 108). At 3r the inside
        # spacing stays positive on any bend, and an 8 cm bent-tube corner is what real
        # guard rails look like anyway.
        rail = tube_along(f"rail_{name}_{tag}", rp, radius, segs, "both", 4, fillet=3.0 * radius)
        parts.append((rail, quad))
    # kick plates per span
    for i, (a, b) in enumerate(zip(P, P[1:])):
        d = b - a
        mid = (a + b) / 2
        rot = d.to_track_quat("X", "Z").to_euler()
        plate = add_box(f"kick_{name}_{i}", (d.length, 0.02, kick),
                        (mid.x, mid.y, mid.z + kick / 2 + 0.005), rot)
        parts.append((plate, quad))
    return parts


def grating(name, w, l, t=0.03, loc=(0, 0, 0), quad=QUAD_GALV, lip=0.03):
    """
    OPAQUE catwalk grating w (X) by l (Y) by t, centered on `loc`: a top + bottom plate
    (relief "map:grate" - the holes are painted/normal-mapped; a see-through cutout would
    flip the whole surface to DefaultCutout) framed by a `lip`-wide rectangular rim ring.
    28 tris. Names: grate_<name>, grate_<name>_lip.
    """
    wi, li = w - 2 * lip, l - 2 * lip
    bm = bmesh.new()
    bm.loops.layers.uv.new("UVMap")
    top = [bm.verts.new((sx * wi / 2, sy * li / 2, t / 2)) for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    bot = [bm.verts.new((sx * wi / 2, sy * li / 2, -t / 2)) for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    bm.faces.new(top)                      # CCW from +Z -> normal +Z
    bm.faces.new(list(reversed(bot)))      # -> normal -Z (explicit: two loose faces)
    _box_project_uv(bm)
    plate = _new_obj(f"grate_{name}", bm, loc, (0, 0, 0), "map:grate")
    rim = _ring_bm((w, l), (wi, li), -t / 2, t / 2, lambda u, v, d: (u, v, d),
                   faces=("outer", "top", "bottom"))
    lip_obj = _new_obj(f"grate_{name}_lip", rim, loc, (0, 0, 0), "geo")
    return [(plate, quad), (lip_obj, quad)]


def cable_clamp(name, r_cable, loc=(0, 0, 0), rot=(0, 0, 0), quad=QUAD_GALV):
    """
    Two-piece cable clamp at `loc` (a point on the cable axis), local X along the cable
    (`rot` orients it): an upper and a lower block, and two studs on top. Names:
    clamp_<name>_u / _l, bolt_<name>_<k>.
    """
    L = 4 * r_cable + 0.04
    W = 2 * r_cable + 0.03
    H = r_cable + 0.015
    gap = 0.003
    M = Euler(rot).to_matrix()
    parts = []
    for tag, sgn in (("u", 1), ("l", -1)):
        c = Vector(loc) + M @ Vector((0, 0, sgn * (H / 2 + gap)))
        parts.append((add_box(f"clamp_{name}_{tag}", (L, W, H), c, rot), quad))
    rb = max(0.006, 0.5 * r_cable)
    hb = 0.02
    for k, sx in enumerate((-1, 1)):
        c = Vector(loc) + M @ Vector((sx * (L / 2 - rb - 0.008), 0, H + gap + hb / 2))
        parts.append((add_cyl(f"bolt_{name}_{k}", rb, hb, c, rot, 4, "top"), quad))
    return parts


def sheave(name, r, w, loc, axis, segs=12, quad_rim=QUAD_GALV, quad_hub=QUAD_GALV,
           hub=True, r_hub=None, groove=None):
    """
    Line sheave (or bullwheel rim) of radius r and width w at `loc`, axle along `axis`:
    bmesh.ops.spin of a 7-point groove profile (hub radius -> flange -> V groove -> flange
    -> hub radius) = 12 * segs tris, plus a proud `segs`-gon hub cylinder (hub=True).
    r_hub defaults to 0.45 r, groove depth to max(0.02, 0.18 r). Names: sheave_<name>_rim,
    sheave_<name>_hub. Set hub=False for the bullwheel rim (its spokes/hub already exist).
    """
    rh = 0.45 * r if r_hub is None else r_hub
    gd = max(0.02, 0.18 * r) if groove is None else groove
    gw = 0.3 * w
    prof = [(rh, -w / 2), (r, -w / 2), (r, -gw), (r - gd, 0.0), (r, gw), (r, w / 2), (rh, w / 2)]
    bm = bmesh.new()
    bm.loops.layers.uv.new("UVMap")
    V = [bm.verts.new((x, 0.0, z)) for x, z in prof]
    E = [bm.edges.new((V[i], V[i + 1])) for i in range(len(V) - 1)]
    bmesh.ops.spin(bm, geom=V + E, cent=(0, 0, 0), axis=(0, 0, 1), dvec=(0, 0, 0),
                   angle=2 * math.pi, steps=int(segs), use_merge=True,
                   use_normal_flip=False, use_duplicate=False)
    # DETERMINISM (review F1, DECISIONS 107): bmesh.ops.spin(use_merge=True) hands back
    # its faces in an order that varies between runs, and everything downstream
    # (smart_project islands, the unique pack, the AO bake, decimation) is order-
    # dependent - two builds of the same station shipped different textures. Sort the
    # elements by geometry so the order is a pure function of the profile.
    cs2_asset.canonical_order_bmesh(bm)
    _orient_outward(bm, lambda f: Vector((f.calc_center_median().x,
                                          f.calc_center_median().y, 0.0)))
    _box_project_uv(bm)
    rot = _axis_rot(axis)
    parts = [(_new_obj(f"sheave_{name}_rim", bm, loc, rot, "geo"), quad_rim)]
    if hub:
        parts.append((add_cyl(f"sheave_{name}_hub", rh * 0.999, w + 0.04, loc, rot, segs,
                              "both"), quad_hub))
    return parts


def door(name, w=0.85, h=1.95, reveal=0.04, loc=(0, 0, 0), facing="+X", quad=QUAD_GALV,
         quad_frame=None):
    """
    Personnel door on a wall: `loc` is the bottom-center of the opening ON the wall face,
    `facing` the outward wall normal. Frame = 2 jambs + head, 0.06 wide, `reveal` proud
    of the wall; the leaf sits 0.02 proud (recessed reveal - 0.02 behind the frame face,
    back face open against the wall; relief "map:seams" for the panel pressing), sill gap
    0.02 above loc.z (keeps the leaf out of the engine's ground band); vertical handle
    bar r 0.012 and a 0.25 m kick plate. Names: door_<name>_leaf / _jamb0 / _jamb1 /
    _head / _handle / _kick.
    """
    qf = quad if quad_frame is None else quad_frame
    yaw = _facing_yaw(facing)
    rot = (0, 0, yaw)
    lx, ly, lz = loc

    def at(x, y, z):
        px, py = _yaw_point(x, y, yaw)
        return (lx + px, ly + py, lz + z)
    jw, sill, leaf_t = 0.06, 0.02, 0.02
    parts = []
    leaf = add_box(f"door_{name}_leaf", (leaf_t, w, h - sill), at(leaf_t / 2, 0, sill + (h - sill) / 2),
                   rot, drop=("-X",))
    leaf["relief"] = "map:seams"
    parts.append((leaf, quad))
    for k, sy in enumerate((-1, 1)):
        parts.append((add_box(f"door_{name}_jamb{k}", (reveal, jw, h + jw),
                              at(reveal / 2, sy * (w / 2 + jw / 2), (h + jw) / 2), rot,
                              drop=("-X",)), qf))
    parts.append((add_box(f"door_{name}_head", (reveal, w, jw), at(reveal / 2, 0, h + jw / 2),
                          rot, drop=("-X",)), qf))
    hx = leaf_t + 0.04
    handle = add_cyl(f"door_{name}_handle", 0.012, 0.16, at(hx, w / 2 - 0.10, 1.00), rot, 6, "both")
    parts.append((handle, qf))
    parts.append((add_box(f"door_{name}_kick", (0.008, w - 0.04, 0.25),
                          at(leaf_t + 0.004, 0, sill + 0.125 + 0.005), rot, drop=("-X",)), qf))
    return parts


def hatch(name, w, h, reveal=0.03, loc=(0, 0, 0), facing="+X", quad=QUAD_GALV):
    """
    Louvred access hatch on a wall: `loc` is the center of the hatch ON the wall face,
    `facing` the outward normal. A rectangular frame ring `reveal` proud, the panel 0.01
    proud inside it (relief "map:louvre", back face open) and two hinge blocks on one
    side. Names: hatch_<name>_panel / _frame / _hinge0 / _hinge1.
    """
    yaw = _facing_yaw(facing)
    rot = (0, 0, yaw)
    lx, ly, lz = loc

    def at(x, y, z):
        px, py = _yaw_point(x, y, yaw)
        return (lx + px, ly + py, lz + z)
    band, pt = 0.04, 0.01
    parts = []
    panel = add_box(f"hatch_{name}_panel", (pt, w - 2 * band, h - 2 * band), at(pt / 2, 0, 0), rot,
                    drop=("-X",))
    panel["relief"] = "map:louvre"
    parts.append((panel, quad))
    frame = _ring_bm((w, h), (w - 2 * band, h - 2 * band), 0.0, reveal,
                     lambda u, v, d: (d, u, v), faces=("outer", "inner", "top"))
    parts.append((_new_obj(f"hatch_{name}_frame", frame, loc, rot, "geo"), quad))
    for k, sz in enumerate((-1, 1)):       # knuckles sit ON the frame face (back buried)
        parts.append((add_box(f"hatch_{name}_hinge{k}", (0.02, 0.03, 0.08),
                              at(reveal + 0.01, -(w / 2 - band / 2), sz * (h / 2 - 0.12)), rot,
                              drop=("-X",)), quad))
    return parts


def drip_edge(box_obj, drop=0.06, t=0.02, quad=QUAD_GALV, lip=0.01):
    """
    Four drip flashings around a roof box (world AABB of its vertices; axis-aligned boxes
    only): each a `t` thick strip outside a side face, from `lip` above the roof top down
    `drop` below it, inner face open against the roof. 40 tris. Names:
    drip_<box name>_<k> for k in +x, -x, +y, -y.
    """
    lo, hi = _mesh_world_bounds(box_obj)
    cx, cy = (lo.x + hi.x) / 2, (lo.y + hi.y) / 2
    lx, ly = hi.x - lo.x, hi.y - lo.y
    zc = hi.z + lip - (drop + lip) / 2
    hz = drop + lip
    parts = []
    specs = [("px", (t, ly + 2 * t, hz), (hi.x + t / 2, cy, zc), "-X"),
             ("nx", (t, ly + 2 * t, hz), (lo.x - t / 2, cy, zc), "+X"),
             ("py", (lx, t, hz), (cx, hi.y + t / 2, zc), "-Y"),
             ("ny", (lx, t, hz), (cx, lo.y - t / 2, zc), "+Y")]
    for k, size, c, inner in specs:
        parts.append((add_box(f"drip_{box_obj.name}_{k}", size, c, (0, 0, 0), drop=(inner,)), quad))
    return parts


def axle_cap(name, r, loc, axis, quad=QUAD_GALV, studs_geo=True, segs=8, t=0.02):
    """
    Axle end cap: `segs`-gon disc of radius r and thickness t whose BASE center sits at
    `loc`, growing outward along `axis` (signed, e.g. "-Y" for the far side of a sheave
    arm); only the outer cap face is built. Four studs (4-seg, caps top) when
    studs_geo=True, else the disc carries relief "map:bolts". Names: axlecap_<name>,
    bolt_<name>_<k>.
    """
    rot = _axis_rot(axis)
    d = _axis_vec(axis)
    c = Vector(loc) + d * (t / 2)
    disc = add_cyl(f"axlecap_{name}", r, t, c, rot, segs, "top")
    disc["relief"] = "geo" if studs_geo else "map:bolts"
    parts = [(disc, quad)]
    if studs_geo:
        rb = min(0.02, max(0.006, 0.12 * r))
        hb = 0.015
        M = Euler(rot).to_matrix()
        for k in range(4):
            a = math.pi / 4 + k * math.pi / 2
            local = Vector((0.65 * r * math.cos(a), 0.65 * r * math.sin(a), t + hb / 2))
            parts.append((add_cyl(f"bolt_{name}_{k}", rb, hb, Vector(loc) + M @ local, rot, 4,
                                  "top"), quad))
    return parts


__all__ = ["add_cyl", "add_box", "add_wedge", "tube_along", "flange", "gusset",
           "stiffener_ring", "handrail", "grating", "cable_clamp", "sheave", "door",
           "hatch", "drip_edge", "axle_cap", "RELIEF_KINDS", "PREFIXES",
           "QUAD_CONCRETE", "QUAD_GALV", "QUAD_GLASS", "QUAD_PAINT"]
