"""
cs2_skin: articulated (skinned) sheave trains. It holds the rig, the weights, the poses and the G6 joint sweep (lift
alignment phase 2, round 2: hinged half trains, docs/research/field/2026-11-02/liftalign/articulation-design-note.md).

CS2 draws each span's rope as its own straight curve to the tower's anchor, so over a train the rope CORNERS at the
anchor. A rigid train cannot seat every wheel on a cornered rope. Two half trains hinged on the anchor's lateral line
can, at any break. Contract (every rigged tower, lift_tower and tram_tower):
- bones: `Root` (static), `TrainFront` = the half on the tower's GAME +z side, `TrainBack` = the -z side. A second rope
  that corners at another anchor on the same half gets `TrainFront<Suffix>` / `TrainBack<Suffix>` (the tram haul
  rollers: TrainFrontHaul / TrainBackHaul). Each bone's head = its pivot = the anchor its rope corners at. Every bone
  points up with roll 0 (identity rest rotation in the game). The half bones are children of Root.
- axis: game +z = Blender net-frame -Y. The net frame is the authoring frame after cs2_asset.orient_for_net ((x, y, z)
  -> (y, -x, z): authoring +X travel -> net -Y). cs2_asset.export_fbx_skinned bakes -90 deg about X ((x, y, z) -> (x,
  z, -y), cs2_asset.py export_fbx_skinned), and the game negates FBX x on import (PIPELINE frame rule T). So game =
  (-x_net, z_net, -y_net). FRONT_SIGN_NET = -1: a part whose travel centroid has net y < 0 rides TrainFront.
  roundtrip() re-reads the exported FBX and holds it (raw file z = game z: TrainFront z > 0).
- pose: a bone pitch theta (deg) turns its half about +X through its pivot (Matrix.Rotation(theta, 'X')). A positive
  theta lowers the game +z end. The code pitches TrainFront* by the rope leaving toward +z and TrainBack* by the rope
  leaving toward -z. It clamps each to the type's theta_max, and it clamps the half-break |front - back| / 2 to
  HALF_BREAK_MAX (20 deg). front > back = a crest (both outer ends down), front < back = a dip.

G6 joint sweep (sweep): a grid of (front, back) pitches, each in {-tmax, -tmax / 2, 0, +tmax / 2, +tmax} with |front -
back| <= 2 x HALF_BREAK_MAX. At each of those means, the grid also has the half-breaks +-HALF_BREAK_MAX (each half
clamped to tmax, as the code clamps). At every pose the sweep checks:
- no BVH face overlap, half vs head and half vs half
- a vertex-to-surface clearance >= clear_min (the main pins vs the lugs they run through are exempt)
- the pin joint opening 2 d sin(|theta| / 2) <= pin_gap_max (d = the pin axis off its bone's pivot line)
- the rope runs (one straight run per half from its pivot) are clear of the head and the non-wheel train parts
- the rope of each half is seated on every wheel / saddle crown of that half (seat())
"""
import math

import bpy
import mathutils
import numpy as np

BONE_LEN = 0.25
FRONT_SIGN_NET = -1.0           # net-frame travel sign of the game +z half (see the module docstring)
HALF_BREAK_MAX = 20.0           # deg: |front - back| / 2 clamp (the code's value, the art clears it)
# deg: every family's half clamps to min(the type's max slope, TILT_CAP). A train pitched much past 30 deg reads absurd
# on an upright tower (tilted tower bodies are a later step). The joint grid runs to that clamp
TILT_CAP = 30.0


def theta_max(type_max):
    """The half clamp (deg) of a type whose planner max slope is `type_max`: min(type_max, TILT_CAP)."""
    return min(float(type_max), TILT_CAP)
SEAT_GAP = (-0.001, 0.002)      # m: rope underside over each crown, contact, not inside (lift_tower.SKIN_SEAT_GAP)


class SkinError(RuntimeError):
    pass


# --------------------------------------------------------------------------------------
# Geometry helpers (net frame: X lateral, -Y = game forward, Z up)
# --------------------------------------------------------------------------------------

def rotate_x(co, theta_deg, piv):
    """(N, 3) net-frame coordinates turned theta about +X through (y, z) = piv (Matrix.Rotation(theta, 'X'))."""
    t = math.radians(theta_deg)
    c, s = math.cos(t), math.sin(t)
    out = np.array(co, dtype=np.float64, copy=True)
    y, z = out[:, 1] - piv[0], out[:, 2] - piv[1]
    out[:, 1] = c * y - s * z + piv[0]
    out[:, 2] = s * y + c * z + piv[1]
    return out


def coords(obj):
    me = obj.data
    buf = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertices.foreach_get("co", buf)
    return buf.reshape(-1, 3).astype(np.float64)


def face_parts(obj, table):
    me = obj.data
    part = np.zeros(len(me.polygons), dtype=np.int32)
    me.attributes["part"].data.foreach_get("value", part)
    return [table[q - 1] for q in part]


def part_centroids(obj, table):
    """{part: (mean vertex, min, max)} over the vertices its faces use (net or authoring frame, as the object is)."""
    me = obj.data
    names = face_parts(obj, table)
    co = coords(obj)
    acc = {}
    for nm, p in zip(names, me.polygons):
        acc.setdefault(nm, set()).update(p.vertices)
    out = {}
    for nm, vs in acc.items():
        c = co[np.array(sorted(vs))]
        out[nm] = (c.mean(axis=0), c.min(axis=0), c.max(axis=0))
    return out


def bones_by_travel(obj, table, is_member, front, back, lug=0.15):
    """{part: bone} for the parts `is_member(name)` accepts: `front` when the part's travel centroid lies on the game
    +z side (net y * FRONT_SIGN_NET > 0), else `back`. A part reaching more than `lug` m across travel 0 on BOTH sides
    is ambiguous and raises (a half's lug end may pass its pin by up to `lug`)."""
    out = {}
    for nm, (mean, lo, hi) in part_centroids(obj, table).items():
        if not is_member(nm):
            continue
        if lo[1] < -lug and hi[1] > lug:
            raise SkinError(f"{nm}: spans travel {lo[1]:+.3f}..{hi[1]:+.3f} across the pivot (which half?)")
        out[nm] = front if mean[1] * FRONT_SIGN_NET > 0.0 else back
    return out


def face_bones(obj, table, part_bone, bones):
    """(per-face part names, per-face bone index, per-vertex bone index) with bone index = bones.index(bone) (parts not
    in part_bone ride bones[0], Root). Raises when a vertex is used by faces of two bones (each half must be its own
    islands)."""
    me = obj.data
    names = face_parts(obj, table)
    fb = np.array([bones.index(part_bone.get(n, bones[0])) for n in names], dtype=np.int8)
    npoly = len(me.polygons)
    start = np.empty(npoly, dtype=np.int32)
    total = np.empty(npoly, dtype=np.int32)
    me.polygons.foreach_get("loop_start", start)
    me.polygons.foreach_get("loop_total", total)
    lv = np.empty(len(me.loops), dtype=np.int32)
    me.loops.foreach_get("vertex_index", lv)
    order = np.concatenate([np.arange(a, a + n) for a, n in zip(start, total)]) if npoly else lv
    lb = np.repeat(fb, total)
    vl = lv[order]
    seen = np.zeros((len(me.vertices), len(bones)), dtype=bool)
    seen[vl, lb] = True
    mixed = np.nonzero(seen.sum(axis=1) > 1)[0]
    if len(mixed):
        raise SkinError(f"{obj.name}: {len(mixed)} vertices shared by faces of two bones (first {mixed[:5].tolist()}): "
                        f"every half train must be its own islands")
    vb = np.argmax(seen, axis=1).astype(np.int8)        # an unused vertex rides Root (index 0)
    return names, fb, vb


def assign(obj, table, part_bone, bones):
    """Vertex groups named after `bones` on `obj` (LOD0 or a LOD copy: the 'part' attribute rides the chain), each
    vertex weight 1.0 on its bone. Returns {bone: vertex count}."""
    _n, _fb, vb = face_bones(obj, table, part_bone, bones)
    for vg in list(obj.vertex_groups):
        obj.vertex_groups.remove(vg)
    out = {}
    for i, b in enumerate(bones):
        vg = obj.vertex_groups.new(name=b)
        idx = np.nonzero(vb == i)[0].tolist()
        if idx:
            vg.add(idx, 1.0, "REPLACE")
        out[b] = len(idx)
    return out


def posed(co, vb, bones, angles, pivots):
    """Net-frame coordinates with every half bone's vertices turned by angles[bone] about its pivot."""
    pc = np.array(co, dtype=np.float64, copy=True)
    for i, b in enumerate(bones):
        if i == 0 or not angles.get(b):
            continue
        m = vb == i
        if m.any():
            pc[m] = rotate_x(co[m], angles[b], pivots[b])
    return pc


# --------------------------------------------------------------------------------------
# Rig
# --------------------------------------------------------------------------------------

def build_armature(name, heads, root="Root"):
    """The rig in the net frame: `heads` {bone: (x, y, z)} in bone order (Root first). Every bone points up (+Z) with roll
    0, and every non-root bone is a child of Root. Raises unless every rest is the up / roll 0 frame (identity rest in
    the game, PIPELINE 'Skinned (procedural-bone) assets'). Returns the armature object."""
    arm_data = bpy.data.armatures.new(name)
    arm = bpy.data.objects.new(name, arm_data)
    bpy.context.collection.objects.link(arm)
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    rb = None
    for b, h in heads.items():
        eb = arm_data.edit_bones.new(b)
        eb.head = h
        eb.tail = (h[0], h[1], h[2] + BONE_LEN)
        eb.roll = 0.0
        eb.use_deform = True
        if b == root:
            rb = eb
        else:
            eb.parent = rb
            eb.use_connect = False
    bpy.ops.object.mode_set(mode="OBJECT")
    want = mathutils.Matrix.Rotation(math.radians(90.0), 4, "X").to_quaternion()
    for b in arm_data.bones:
        if b.matrix_local.to_quaternion().rotation_difference(want).angle > 1e-4:
            raise SkinError(f"{name}: bone {b.name} rest is not the up / roll 0 frame")
    return arm


def pose(arm, angles):
    """Pose every half bone theta about its local X (= net X through its head)."""
    for pb in arm.pose.bones:
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = (math.radians(angles.get(pb.name, 0.0)), 0.0, 0.0)
    bpy.context.view_layer.update()


def posed_copy(t, arm, angles, pivots, name):
    """A new mesh object: `t` (vertex groups from assign) deformed by `arm` posed at `angles` (an Armature modifier,
    evaluated), so a preview shows what the skin does. Raises unless that is posed() of the weights (the pose the sweep
    measured) to 0.1 mm."""
    pose(arm, angles)
    tmp = t.copy()
    bpy.context.collection.objects.link(tmp)
    mod = tmp.modifiers.new("Armature", type="ARMATURE")
    mod.object = arm
    me = bpy.data.meshes.new_from_object(tmp.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    bpy.data.objects.remove(tmp, do_unlink=True)
    pose(arm, {})
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    co, got = coords(t), coords(ob)
    bones = [b.name for b in arm.data.bones]
    vb = np.zeros(len(co), dtype=np.int8)
    for i, b in enumerate(bones):
        if b in t.vertex_groups:
            gi = t.vertex_groups[b].index
            for v in t.data.vertices:
                if any(g.group == gi and g.weight > 0.0 for g in v.groups):
                    vb[v.index] = i
    want = posed(co, vb, bones, angles, pivots)
    err = float(np.abs(got - want).max()) if len(got) == len(co) else 9.0
    if err > 1e-4:
        raise SkinError(f"{name}: the armature pose {angles} is not the sweep's pose (max {err:.5f} m)")
    return ob


# --------------------------------------------------------------------------------------
# The joint sweep (G6)
# --------------------------------------------------------------------------------------

def joint_grid(tmax, brk=HALF_BREAK_MAX):
    """[(front, back)] deg: {-tmax, -tmax / 2, 0, tmax / 2, tmax}^2 with |front - back| <= 2 brk. Then, at each of those
    means, the half-breaks +-brk (each half clamped to +-tmax, as the code clamps). Deterministic order."""
    vals = [-tmax, -tmax / 2.0, 0.0, tmax / 2.0, tmax]
    out = []

    def add(p):
        p = (round(p[0], 4), round(p[1], 4))
        if p not in out:
            out.append(p)
    for f in vals:
        for b in vals:
            if abs(f - b) <= 2.0 * brk + 1e-9:
                add((f, b))
    means = sorted({round((f + b) / 2.0, 6) for f, b in out})
    cl = lambda a: max(-tmax, min(tmax, a))   # noqa: E731
    for m in means:
        for d in (brk, -brk):
            add((cl(m + d), cl(m - d)))
    return out


def rope_run(lane_x, piv, theta_deg, sign, length, r=0.100, start=0.0, drop=0.0):
    """(verts, faces) of a hexagonal rope prism (circumradius r, corners on the lateral axis = flats top / bottom: the
    vanilla HV cable). The prism runs from `start` m to `length` m out of the pivot piv = (y, z) along net y * sign, at
    lateral x = lane_x, and the run is turned theta about the pivot. `drop` (previews only, 0 = the game's rope on the
    lane) lowers the axis that far below the pivot line in the half's frame: a real-diameter stand-in seated in the
    groove."""
    ring = [(lane_x + r * math.cos(a), r * math.sin(a) - drop) for a in (math.radians(60.0 * k) for k in range(6))]
    verts = []
    for d in (start, length):
        verts += [(x, piv[0] + sign * d, piv[1] + z) for x, z in ring]
    co = rotate_x(np.array(verts), theta_deg, piv)
    faces = [tuple(range(5, -1, -1)), tuple(range(6, 12))]
    faces += [(k, (k + 1) % 6, 6 + (k + 1) % 6, 6 + k) for k in range(6)]
    return [tuple(v) for v in co], faces


def seat_half(pc, polys, wheel_faces, piv, theta_deg, lane_x, sign, extent, rope_r, rope_h, step=0.004, nx=21,
              zspan=0.6):
    """The rope of one half on its crowns. The function turns the posed wheel / saddle faces `wheel_faces` back by -theta
    about the half's pivot (so its rope is horizontal through the pivot). Then it casts rays straight down through the
    rope's underside every `step` m of travel out to `extent` on the half's side. The underside is the vanilla hexagon:
    bottom flat +-R / 2 at -h, slanted faces up to +-R. Per crown (a run of hits), it takes the smallest gap, rope
    underside - crown (m), and returns [(travel, gap)]. `nx` odd with (nx - 1) % 4 == 0 puts rays exactly under the
    hexagon's bottom corners (+-R / 2), where it meets the V groove's flanks. 15 rays measured FG 4 mm / DQ 6 mm of air
    the 21 of lift_tower's pilot (and this default) do not."""
    if (nx - 1) % 4:
        raise ValueError(f"seat_half: nx {nx} misses the rope's bottom corners (want (nx - 1) % 4 == 0)")
    from mathutils.bvhtree import BVHTree
    if not wheel_faces:
        return []
    used = sorted({v for i in wheel_faces for v in polys[i]})
    idx = {v: k for k, v in enumerate(used)}
    loc = rotate_x(pc[np.array(used)], -theta_deg, piv)
    tree = BVHTree.FromPolygons([tuple(v) for v in loc], [tuple(idx[v] for v in polys[i]) for i in wheel_faces],
                                epsilon=0.0)
    down = mathutils.Vector((0.0, 0.0, -1.0))
    runs, cur = [], None
    for d in np.arange(0.0, extent, step):
        y = piv[0] + sign * d
        g = None
        for dx in np.linspace(-rope_r, rope_r, nx):
            hit = tree.ray_cast(mathutils.Vector((lane_x + dx, y, piv[1] + zspan)), down, 2.0 * zspan)
            if hit[0] is not None:
                gg = piv[1] - rope_h * min(1.0, (rope_r - abs(dx)) / (rope_r / 2.0)) - hit[0].z
                g = gg if g is None else min(g, gg)
        if g is None:
            cur = None
        elif cur is None or g < cur[1]:
            if cur is None:
                runs.append([float(d), g])
                cur = runs[-1]
            cur[0], cur[1] = float(d), g
    return [(round(d, 3), round(g, 5)) for d, g in runs]


def sweep(co, polys, names, fb, bones, pivots, poses, *, pins, lugs, wheels, ropes, seats, clear_min, pin_gap_max,
          label, near=0.6, log=print):
    """
    G6 on a joined net-frame LOD0.
    - `fb`: per-face bone index into `bones` (0 = Root).
    - `pivots`: {bone: (y, z)}.
    - `poses`: [{bone: deg}].
    - `pins`: predicate on a part name (Root main pins, exempt vs the lugs).
    - `lugs`: {bone: predicate} = the parts of that bone a pin runs through.
    - `wheels`: predicate (the rope's crowns: exempt vs the rope).
    - `ropes(pose)` -> [(verts, faces)] (the rope runs at that pose).
    - `seats(pc, pose)` -> {key: [(travel, gap)]}.

    Returns the report (meters). It raises SkinError on a failed gate after logging every pose.
    """
    from mathutils.bvhtree import BVHTree
    nb = len(bones)
    vb = np.zeros(len(co), dtype=np.int8)
    for i, p in enumerate(polys):
        vb[list(p)] = fb[i]
    F = {b: [i for i in range(len(polys)) if fb[i] == b] for b in range(nb)}
    pin_f = [i for i in F[0] if pins(names[i])]
    head_f = [i for i in F[0] if not pins(names[i])]
    lug_f = {b: [i for i in F[b] if lugs.get(bones[b], lambda n: False)(names[i])] for b in range(1, nb)}
    rest_f = {b: [i for i in F[b] if i not in set(lug_f[b])] for b in range(1, nb)}
    ride_f = {b: [i for i in F[b] if not wheels(names[i])] for b in range(1, nb)}

    def verts_of(ids):
        return np.unique(np.concatenate([np.array(polys[i]) for i in ids])) if ids else np.zeros(0, dtype=np.int64)

    def bvh(c, ids):
        return BVHTree.FromPolygons([tuple(v) for v in c], [polys[i] for i in ids], epsilon=0.0) if ids else None

    # the head near the trains only (vertex-to-surface queries): within reach of a pivot
    reach = 0.0
    for b in range(1, nb):
        v = verts_of(F[b])
        if len(v):
            p = pivots[bones[b]]
            reach = max(reach, float(np.hypot(co[v, 1] - p[0], co[v, 2] - p[1]).max()))
    xs = co[vb > 0, 0] if (vb > 0).any() else np.zeros(1)
    hv_all = verts_of(head_f)
    dmin = np.full(len(hv_all), 9.0)
    for b in range(1, nb):
        p = pivots[bones[b]]
        dmin = np.minimum(dmin, np.hypot(co[hv_all, 1] - p[0], co[hv_all, 2] - p[1]))
    lat = (co[hv_all, 0] >= xs.min() - near) & (co[hv_all, 0] <= xs.max() + near)
    hv = hv_all[(dmin <= reach + near) & lat]
    pv = verts_of(pin_f)
    h_all = bvh(co, pin_f + head_f)
    h_rest = bvh(co, head_f)
    h_all_ids = pin_f + head_f
    rows = []
    worst = {"clear": (9.0, None), "pairs": {}}
    for ang in poses:
        pc = posed(co, vb, bones, ang, pivots)
        T = {b: bvh(pc, F[b]) for b in range(1, nb)}
        TR = {b: bvh(pc, rest_f[b]) for b in range(1, nb)}
        TL = {b: bvh(pc, lug_f[b]) for b in range(1, nb)}
        ov = []
        for b in range(1, nb):
            if TR[b] is not None:
                ov += [(rest_f[b][i], h_all_ids[j]) for i, j in TR[b].overlap(h_all)]
            if TL[b] is not None and h_rest is not None:
                ov += [(lug_f[b][i], head_f[j]) for i, j in TL[b].overlap(h_rest)]
            for c in range(b + 1, nb):
                if T[b] is not None and T[c] is not None:
                    ov += [(F[b][i], F[c][j]) for i, j in T[b].overlap(T[c])]
        best = (9.0, None, None)

        def probe(vs, src, tree, ids):
            nonlocal best
            if tree is None:
                return
            for v in vs:
                hit = tree.find_nearest(mathutils.Vector(src[v]))
                if hit[0] is not None and hit[3] < best[0]:
                    best = (hit[3], int(v), ids[hit[2]])
        for b in range(1, nb):
            vr, vl = verts_of(rest_f[b]), verts_of(lug_f[b])
            probe(vr, pc, h_all, h_all_ids)
            probe(vl, pc, h_rest, head_f)
            probe(hv, co, T[b], F[b])
            probe(pv, co, TR[b], rest_f[b])
            for c in range(1, nb):
                if c != b:
                    probe(verts_of(F[b]), pc, T[c], F[c])
        vname = None
        if best[1] is not None:
            vname = next((names[i] for i in range(len(polys)) if best[1] in polys[i]), None)
        pair = [vname, names[best[2]] if best[2] is not None else None]
        # pins: joint opening 2 d sin(|theta| / 2), d = the pin axis off its lug bone's pivot line
        gaps = []
        for i_pin in sorted({names[i] for i in pin_f}):
            ids = [i for i in pin_f if names[i] == i_pin]
            vs = co[verts_of(ids)]
            c = (vs.min(axis=0) + vs.max(axis=0)) / 2.0
            for b in range(1, nb):
                if lug_f[b]:
                    p = pivots[bones[b]]
                    d = math.hypot(c[1] - p[0], c[2] - p[1])
                    gaps.append(2.0 * d * math.sin(math.radians(abs(ang.get(bones[b], 0.0))) / 2.0))
        rope_ov, rope_clear = 0, 9.0
        ride = [i for b in range(1, nb) for i in ride_f[b]]
        t_ride = bvh(pc, ride)
        near_pts = np.concatenate([co[hv], pc[verts_of(ride)]]) if ride else co[hv]
        for rv, rf in ropes(ang):
            rt = BVHTree.FromPolygons(rv, rf, epsilon=0.0)
            rope_ov += len(rt.overlap(h_all)) + (len(rt.overlap(t_ride)) if t_ride is not None else 0)
            rx = np.array(rv)[:, 0]
            sel = near_pts[(near_pts[:, 0] > rx.min() - 0.5) & (near_pts[:, 0] < rx.max() + 0.5)]
            for v in sel:
                hit = rt.find_nearest(mathutils.Vector(v))
                if hit[0] is not None:
                    rope_clear = min(rope_clear, hit[3])
        seat = seats(pc, ang)
        sg = [g for runs in seat.values() for _d, g in runs]
        row = {"pose": {k: round(v, 3) for k, v in ang.items()}, "overlaps": len(ov),
               "overlap_pairs": sorted({(names[a], names[b]) for a, b in ov})[:6], "clear_m": round(best[0], 5),
               "nearest": pair, "pin_gap_m": round(max(gaps) if gaps else 0.0, 6), "rope_overlaps": rope_ov,
               "rope_clear_m": round(rope_clear, 5), "seat_tops": len(sg),
               "seat_m": [round(min(sg), 5), round(max(sg), 5)] if sg else None}
        rows.append(row)
        if best[0] < worst["clear"][0]:
            worst["clear"] = (best[0], row["pose"], pair)
        log(f"[skin] G6 [{label}] {' / '.join(f'{k} {v:+6.2f}' for k, v in row['pose'].items())}: overlaps "
            f"{row['overlaps']}, clearance {row['clear_m'] * 1000:.1f} mm ({' / '.join(str(n) for n in pair)}), pin gap "
            f"{row['pin_gap_m'] * 1000:.2f} mm, rope overlaps {rope_ov} clear {rope_clear * 1000:.1f} mm, seat "
            f"{len(sg)} tops {row['seat_m'][0] * 1000 if sg else float('nan'):.2f}.."
            f"{row['seat_m'][1] * 1000 if sg else float('nan'):.2f} mm")
    return {"poses": rows, "min_clear": {"m": round(worst["clear"][0], 5), "pose": worst["clear"][1],
                                         "parts": worst["clear"][2]}, "reach_m": round(reach, 3),
            "head_verts_probed": int(len(hv))}


def check_rows(rows, label, clear_min, pin_gap_max, seat_tops=None, seat_gap=SEAT_GAP):
    """Raise SkinError on the first failed gate of a sweep report's rows (after the whole sweep was logged)."""
    bad = []
    for r in rows:
        why = []
        if r["overlaps"]:
            why.append(f"{r['overlaps']} overlaps {r['overlap_pairs'][:3]}")
        if r["clear_m"] < clear_min:
            why.append(f"clearance {r['clear_m'] * 1000:.1f} mm {r['nearest']}")
        if r["pin_gap_m"] > pin_gap_max:
            why.append(f"pin gap {r['pin_gap_m'] * 1000:.2f} mm")
        if r["rope_overlaps"]:
            why.append(f"rope overlaps {r['rope_overlaps']}")
        if seat_tops is not None and (r["seat_tops"] != seat_tops or r["seat_m"] is None
                                      or r["seat_m"][0] < seat_gap[0] or r["seat_m"][1] > seat_gap[1]):
            why.append(f"seat {r['seat_tops']} tops (want {seat_tops}) {r['seat_m']}")
        if why:
            bad.append((r["pose"], why))
    if bad:
        raise SkinError(f"[{label}] G6 failed at {len(bad)} of {len(rows)} poses, first {bad[:3]}")


# --------------------------------------------------------------------------------------
# FBX round trip (the raw file frame the game reads) + the axis test
# --------------------------------------------------------------------------------------

def roundtrip(asset_dir, name, levels, heads, check, label):
    """Re-import every exported level (rig_probe.roundtrip) and hold the rig to the design:
    - bones exactly `heads` (Root first)
    - every non-root bone under Root at its head (game frame cm: (-x, z, -y) x 100 of the net head)
    - identity rests
    - every vertex on exactly one bone
    - the AXIS TEST: in the raw file frame (z = game z) every TrainFront* group's bbox center has z > 0 and every
      TrainBack* group's z < 0

    `check(ok, label, detail)`. Removes what the import made. Returns {level: {...}}."""
    import os
    import rig_probe
    out = {}
    for i in range(levels + 1):
        fn = f"{name}.fbx" if i == 0 else f"{name}_LOD{i}.fbx"
        before = {c: set(getattr(bpy.data, c).keys()) for c in ("objects", "meshes", "armatures", "materials", "images")}
        try:
            rep, _arm, _me = rig_probe.roundtrip(os.path.join(asset_dir, fn))
        finally:
            for c in ("objects", "meshes", "armatures", "materials", "images"):
                coll = getattr(bpy.data, c)
                for n in [n for n in coll.keys() if n not in before[c]]:
                    coll.remove(coll[n])
        bones = {b["name"]: b for b in rep["bones"]}
        m = rep["mesh"]
        want = {b: ((-h[0] * 100.0, h[2] * 100.0, -h[1] * 100.0) if b != "Root" else (0.0, 0.0, 0.0))
                for b, h in heads.items()}
        ok = sorted(bones) == sorted(heads) and m["unweighted_verts"] == 0 and m["multi_bone_verts"] == 0
        # the importer's local positions are in the raw frame (rig_probe: x as written). Rule T negates x in the game
        for b, w in want.items():
            if not ok:
                break
            lp = bones[b]["local_pos"]
            got = (-lp[0], lp[1], lp[2])
            ok = (max(abs(a - c) for a, c in zip(got, w)) <= 0.06 and abs(abs(bones[b]["local_rot_xyzw"][3]) - 1.0)
                  <= 1e-5 and (b == "Root" or bones[b]["parent"] == "Root"))
        axis = {}
        for g, v in m["groups"].items():
            if g == "Root" or not v["bbox"]:
                continue
            zc = (v["bbox"][0][2] + v["bbox"][1][2]) / 2.0
            axis[g] = round(zc, 2)
            if g.startswith("TrainFront") and not zc > 0.0:
                ok = False
            if g.startswith("TrainBack") and not zc < 0.0:
                ok = False
        check(ok, f"[{label}] {fn} round trip: bones {list(heads)}, heads at the pivots, identity rests, one bone per "
                  f"vertex; axis: TrainFront* at raw z > 0 (game +z), TrainBack* at z < 0",
              f"({[(b['name'], b['parent'], b['local_pos'], b['local_rot_xyzw']) for b in rep['bones']]}, unweighted "
              f"{m['unweighted_verts']}, multi {m['multi_bone_verts']}, group z centers cm {axis})")
        out[str(i)] = {"file": fn, "verts": m["verts"], "tris": m["tris"],
                       "groups": {g: v["verts"] for g, v in m["groups"].items()},
                       "local_pos_cm": {b["name"]: b["local_pos"] for b in rep["bones"]},
                       "group_z_center_cm": axis}
    return out
