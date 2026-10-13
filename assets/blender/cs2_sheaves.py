"""
cs2_sheaves - the towers' sheave-train builder, shared by the towers and the lift stations.

Extracted 2026-10-13 from lift_tower.py (Richard, twenty-fifth look S-2: "Let's all apply the same PULLEY
WHEELS / ROLLERS we use on the lift tower onto the lift stations"; build report
docs/research/art/assets-2026-10/build/nitpick-a-stations.md). The code below is lift_tower.py's, moved
VERBATIM (expression for expression, the same part order and names): lift_tower imports every function
back and passes its OWN module globals into the train builders at call time, so every tower regenerates
byte-identical and the tests that patch lift_tower constants (DQ_SUB_Y, DQ_LINK, GD_CAR_DY, DP1_INSET ...)
still steer the tower builds. The constants here are the towers' numbers: lift_tower aliases them (one
source), the stations read them as the defaults.

Parts:
  lite_sheave / lite_sheave_parts  the D4 "lite" sheave: an axis-closed V-groove (or flat-rim) wheel about a
                                   Y axle, 6 x segs tris; the _parts form splits the side discs ("galv") from
                                   the rope liner (LINER_KEY "rubber") - the detail-pass liners
  fg_train_side                    the Yan (FG / ET) rocker tree of ONE lane: hanger -> main rocker -> pivots
                                   -> sub rockers -> sheave arms -> sheaves + liners + axle caps, hanging
                                   below its carrier (the crossarm on a tower)
  dq_train                         the gantry (DQ / ETDQ / GD / ETGD) train of both lanes: sheaves + liners,
                                   sub rockers + pins, main rockers + pins, the yoke, links for a lone middle
                                   sub, the hanger to the girder - every carrier INBOARD of the sheaves
  dq_centres                       the gantry train's sheave centres (sag parabola)
  bevel_parts / chamfer_long_edges the tower finish of these parts (BEVEL_W bevel by prefix table; the FG
                                   1-segment long-edge chamfer of rocker_sub_ / sheave_arm_)
  TRAIN_BEVEL / TRAIN_CHAMFER      the towers' bevel / chamfer tables restricted to the train's parts

Every builder takes `prefix` (default ""): the stations put their trains in their own name space
("sheave_guide_", "sheave_hold_" ...) so their LOD / C8 / density tables apply; the towers pass "".
Materials returned are the TOWER keys ("paint" = the tower's slot-1 steel, "galv", LINER_KEY): a station
maps them onto its own table (lift_terminal_detach: paint -> darksteel; lift_terminal M: paint -> paint1).
"""

import math

import bmesh
import bpy
import mathutils

import cs2_asset
import cs2_kit as K

# -- the towers' train numbers (lift_tower aliases these; one source) ---------------------------------
BEVEL_W = 0.02
LINER_KEY = "rubber"
DP1_CHAMFER = 0.012                    # FG / ET rocker_sub_ + sheave_arm_ long edges
# FG (legacy head numbers, verbatim: lift_tower.py TowerParams / build_sheave_train)
FG_ARM_T = 0.22                        # crossarm depth (the hanger reaches into it)
FG_HANGER, FG_MAIN_TO_SUB, FG_SUB_TO_AXLE = 0.40, 0.30, 0.26     # axle = mast - 0.96
FG_HANGER_INSET = 0.005                # M4: hanger top 5 mm inside the crossarm (DP1_INSET fg_hanger_top)
FG_SHEAVE = dict(r=0.20, w=0.13, pitch=0.52)                     # lift_tower._fg_spec
# DQ gantry train (towers-v2 section 4.2, F9, Revision 1c)
DQ_SUB_Y = 2.512                       # F9: 2.517 moved 5 mm inboard (outer face 2.545)
DQ_MAIN_Y = 2.44
DQ_LINK = dict(w=0.15, t=0.072, below=0.10, bury=0.02)   # None = the pre-Revision-1c build (test)
DQ_HANGER = dict(y0=2.30, y1=2.41, z0=0.25, z1=0.42, x=0.15)
DQ_HANGER_Z_INSET = 0.005              # M4: top / bottom 5 mm inside the yoke's faces (DP1_INSET dq_hanger_z)
DQ_SHEAVE = dict(r=0.223, w=0.10, pitch=0.59)                    # lift_tower._dq_spec
DQ_PIN_S = 0.17                        # sub-rocker pivot pin length (head_kit pin_s, DQ)
# GD: the gondola head (lift_tower GD_* constants; head_kit for family "GD")
GD_SHEAVE = dict(r=0.25, w=0.10, pitch=0.62, segs=12, groove=0.0)
GD_CAR_DY = 0.39
GD_PIN_S = 0.14
# the towers' finish of the train parts (lift_tower FG_BEVEL / DQ_BEVEL / detail_post, train keys only)
TRAIN_BEVEL = {"FG": {"hanger_": 1, "rocker_main_": 2}, "DQ": {"rocker_main_": 1, "rocker_link_": 1}}
TRAIN_CHAMFER = {"FG": (("rocker_sub_", (1.0, 0.0, 0.0)), ("sheave_arm_", (0.0, 0.0, 1.0))), "DQ": ()}


# --------------------------------------------------------------------------------------
# Part helpers (bmesh-built, deterministic; every part carries exactly one "UVMap")
# --------------------------------------------------------------------------------------

def _box_uv_bm(bm, layer):
    """Per-face dominant-axis projection in metres with u x v = +normal axis (never mirrored)."""
    bm.normal_update()
    for f in bm.faces:
        n = f.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        s = 1.0 if n[ax] >= 0.0 else -1.0
        for lp in f.loops:
            c = lp.vert.co
            if ax == 0:
                uv = (s * c.y, c.z)
            elif ax == 1:
                uv = (-s * c.x, c.z)
            else:
                uv = (s * c.x, c.y)
            lp[layer].uv = uv


def _new_part(name, bm, relief="geo"):
    """bmesh (world coordinates) -> linked mesh object with identity transform."""
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.update()
    obj = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(obj)
    obj["relief"] = relief
    return obj


def lite_sheave(name, r, w, loc, segs=12, groove=None):
    """
    The towers-v2 D4 "lite" sheave, generator-local (cs2_kit.sheave is untouched so the
    stations stay byte-stable): an axis-closed 5-point V-groove profile (axle -> flange ->
    groove root -> flange -> axle) around a Y axle = 6 * segs tris (72 at 12). Built
    vertex by vertex, then canonical_order_bmesh (skeptic: determinism) and an unmirrored
    box-projected UVMap. Name used verbatim (pass "sheave_..._rim").
    """
    gd = max(0.02, 0.18 * r) if groove is None else groove
    hw = w / 2.0
    cx, cy, cz = loc
    bm = bmesh.new()
    layer = bm.loops.layers.uv.new("UVMap")
    ang = [2.0 * math.pi * k / segs for k in range(segs)]
    if gd <= 0.0:
        # Revision 1b (GD): FLAT rim = tram_tower.sheave_lite(groove=0): one band of `segs` quads
        # + two n-gon side discs = 2 segs + 2 (segs - 2) tris (44 at 12). Windings explicit:
        # band (L k, R k, R k+1, L k+1) faces out, the -Y disc in ring order faces -Y.
        L = [bm.verts.new((cx + r * math.cos(a), cy - hw, cz + r * math.sin(a))) for a in ang]
        R = [bm.verts.new((cx + r * math.cos(a), cy + hw, cz + r * math.sin(a))) for a in ang]
        for k in range(segs):
            k1 = (k + 1) % segs
            bm.faces.new((L[k], R[k], R[k1], L[k1]))
        bm.faces.new(L)
        bm.faces.new(list(reversed(R)))
        cs2_asset.canonical_order_bmesh(bm)
        _box_uv_bm(bm, layer)
        return _new_part(name, bm)
    ca = bm.verts.new((cx, cy - hw, cz))
    cb = bm.verts.new((cx, cy + hw, cz))
    rings = [[bm.verts.new((cx + rad * math.cos(a), cy + yy, cz + rad * math.sin(a)))
              for a in ang] for rad, yy in ((r, -hw), (r - gd, 0.0), (r, hw))]
    for k in range(segs):
        k1 = (k + 1) % segs
        bm.faces.new((ca, rings[0][k], rings[0][k1]))                         # -Y disc
        bm.faces.new((rings[0][k], rings[1][k], rings[1][k1], rings[0][k1]))  # groove flank
        bm.faces.new((rings[1][k], rings[2][k], rings[2][k1], rings[1][k1]))  # groove flank
        bm.faces.new((cb, rings[2][k1], rings[2][k]))                         # +Y disc
    cs2_asset.canonical_order_bmesh(bm)
    _box_uv_bm(bm, layer)
    return _new_part(name, bm)


def lite_sheave_parts(name, liner, r, w, loc, segs=12, groove=None):
    """
    lite_sheave split in two parts (design 3.1 `liner_`): `name` keeps the two side discs (the
    C1 / C2 / C9 measurements read its bbox centre: unchanged), `liner` the rope faces - the
    V-groove flanks (4 x segs tris) or the flat band (2 x segs) - in material rubber. Same
    vertices, windings and tri count as lite_sheave; remove_doubles welds the two at the join.
    Returns [(discs, "galv"), (liner, LINER_KEY)].
    """
    gd = max(0.02, 0.18 * r) if groove is None else groove
    hw = w / 2.0
    cx, cy, cz = loc
    ang = [2.0 * math.pi * k / segs for k in range(segs)]

    def ring(bm, rad, yy):
        return [bm.verts.new((cx + rad * math.cos(a), cy + yy, cz + rad * math.sin(a))) for a in ang]

    bm = bmesh.new()
    layer = bm.loops.layers.uv.new("UVMap")
    if gd <= 0.0:
        L, R = ring(bm, r, -hw), ring(bm, r, hw)
        bm.faces.new(L)
        bm.faces.new(list(reversed(R)))
    else:
        ca = bm.verts.new((cx, cy - hw, cz))
        cb = bm.verts.new((cx, cy + hw, cz))
        r0, r2 = ring(bm, r, -hw), ring(bm, r, hw)
        for k in range(segs):
            k1 = (k + 1) % segs
            bm.faces.new((ca, r0[k], r0[k1]))
            bm.faces.new((cb, r2[k1], r2[k]))
    cs2_asset.canonical_order_bmesh(bm)
    _box_uv_bm(bm, layer)
    discs = _new_part(name, bm)
    bm = bmesh.new()
    layer = bm.loops.layers.uv.new("UVMap")
    if gd <= 0.0:
        L, R = ring(bm, r, -hw), ring(bm, r, hw)
        for k in range(segs):
            k1 = (k + 1) % segs
            bm.faces.new((L[k], R[k], R[k1], L[k1]))
    else:
        rg = [ring(bm, r, -hw), ring(bm, r - gd, 0.0), ring(bm, r, hw)]
        for k in range(segs):
            k1 = (k + 1) % segs
            bm.faces.new((rg[0][k], rg[1][k], rg[1][k1], rg[0][k1]))
            bm.faces.new((rg[1][k], rg[2][k], rg[2][k1], rg[1][k1]))
    cs2_asset.canonical_order_bmesh(bm)
    _box_uv_bm(bm, layer)
    lin = _new_part(liner, bm)
    lin["dp_root_r"] = float(r - max(gd, 0.0))
    return [(discs, "galv"), (lin, LINER_KEY)]


def _unit(v):
    v = mathutils.Vector(v)
    return v / max(v.length, 1e-12)


def _kit(parts_q, key_of):
    """cs2_kit (obj, quad) list -> (obj, material key) by a name -> key rule."""
    return [(o, key_of(o.name)) for o, _q in parts_q]


# --------------------------------------------------------------------------------------
# The trains
# --------------------------------------------------------------------------------------

def fg_train_side(tag, s, y, main_z, sub_z, axle_z, n_sub, sub_span, main_span, sheave_r, sheave_w,
                  arm_t=FG_ARM_T, hanger=FG_HANGER, ins=FG_HANGER_INSET, prefix=""):
    """
    The Yan (FG / ET) rocker tree of ONE lane (`tag` "n" / "p", `s` -1 / +1, the lane at `y`), hanging
    below its carrier (lift_tower: the crossarm; its centre = main_z + hanger): the hanger (its top `ins`
    inside a carrier `arm_t` deep), the main rocker at main_z, per sub rocker a pivot pin, the sub rocker at
    sub_z and two sheave arms down to the sheaves on axle_z (lite_sheave_parts: discs + liner) with an
    outboard axle cap each. lift_tower.fg_parts' loop body verbatim (the same expressions, the same part
    order). Returns [(obj, tower material key)].
    """
    P = []
    P.append((K.add_box(f"{prefix}hanger_{tag}", (0.16, 0.15, hanger + arm_t - ins),
                        (0.0, y, main_z + hanger / 2.0 - ins / 2.0)), "paint"))
    P.append((K.add_box(f"{prefix}rocker_main_{tag}", (main_span + 0.40, arm_t * 0.7, 0.15),
                        (0.0, y, main_z)), "paint"))
    for si in range(n_sub):
        sx = (-main_span / 2.0 + si * (main_span / max(1, n_sub - 1))) if n_sub > 1 else 0.0
        P.append((K.add_cyl(f"{prefix}pivot_{tag}_{si}", 0.045, 0.22,
                            (sx, y, (main_z + sub_z) / 2.0), (math.radians(90.0), 0.0, 0.0),
                            segments=6, caps="both"), "galv"))
        P.append((K.add_box(f"{prefix}rocker_sub_{tag}_{si}", (sub_span + 0.30, arm_t * 0.55, 0.12),
                            (sx, y, sub_z)), "paint"))
        for wtag, wv in (("m", -1.0), ("p", 1.0)):
            wx = sx + wv * sub_span / 2.0
            P.append((K.add_box(f"{prefix}sheave_arm_{tag}_{si}_{wtag}", (0.09, 0.10, 0.26),
                                (wx, y, (sub_z + axle_z) / 2.0)), "paint"))
            P += lite_sheave_parts(f"{prefix}sheave_{tag}_{si}_{wtag}_rim", f"{prefix}liner_{tag}_{si}_{wtag}",
                                   sheave_r, sheave_w, (wx, y, axle_z))
            caps = _kit(K.axle_cap(f"{tag}_{si}_{wtag}", 0.09,
                                   (wx, y + s * sheave_w / 2.0, axle_z),
                                   "+Y" if s > 0 else "-Y", studs_geo=False, segs=8),
                        lambda n: "galv")
            if prefix:      # a station's train: the cap joins the prefix's name space (cs2_kit names it axlecap_<n>)
                for o, _k in caps:
                    o.name = f"{prefix}axlecap_{tag}_{si}_{wtag}"
                    o.data.name = o.name
            P += caps
    return P


def fg_spans(sheaves, pitch, sheave_r):
    """(n_sub, sub_span, main_span) of an FG train with `sheaves` per lane (lift_tower.fg_parts)."""
    n_sub = max(1, sheaves // 2)
    sub_span = pitch
    main_span = (n_sub - 1) * (sub_span + 2.0 * sheave_r + 0.10)
    return n_sub, sub_span, main_span


def dq_centres(n, pitch, axle, sag, gauge, side, x0=0.0):
    """[(X_k, Y, Z_k)] for one side: X_k = (k - (n-1)/2) pitch, Z_k = A - sag (2 X_k / L_c)^2
    (lift_tower.dq_sheave_centres); `x0` shifts the train along travel (stations; the towers pass 0 and
    take the unshifted expression)."""
    lc = (n - 1) * pitch
    out = []
    for k in range(n):
        xk = (k - (n - 1) / 2.0) * pitch
        zk = axle - sag * (2.0 * xk / lc) ** 2 if lc > 0 else axle
        out.append(((xk + x0) if x0 else xk, side * gauge / 2.0, zk))
    return out


def _sub_pairs(n_subs):
    """Sub-rocker index pairs under the main rockers + the subs no main carries. Even counts
    pair (0,1), (2,3), ... (every DQ / ETDQ build, unchanged). Odd counts (GD 10 sheaves = 5
    subs) pair from both ends inward, so the lone sub sits in the MIDDLE and the train stays
    symmetric about the portal: (0,1), (3,4) + [2]. Revision 1c: the lone sub hangs from the
    yoke on a link (DQ_LINK, _dq_train)."""
    if n_subs % 2 == 0:
        return [(2 * m, 2 * m + 1) for m in range(n_subs // 2)], []
    mid = n_subs // 2
    left = [(2 * m, 2 * m + 1) for m in range(mid // 2)]
    right = [(n_subs - 2 - 2 * m, n_subs - 1 - 2 * m) for m in range(mid // 2)][::-1]
    single = [i for i in range(n_subs) if not any(i in p for p in left + right)]
    return left + right, single


def dq_train(centres_of, n, axle, sheave_r, sheave_w, segs, groove, sub_y, main_y, pin_s, hanger_box,
             hz, link=DQ_LINK, prefix=""):
    """
    The gantry sheave trains of both lanes with every carrier INBOARD of the sheaves (towers-v2 4.2, F9):
    lift_tower._dq_train's body verbatim. `centres_of(s)` = the [(x, y, z)] sheave centres of the side s
    (-1 / +1; dq_centres), n sheaves per side (even), `axle` the train's reference height (the main rockers,
    pins, yoke and hanger sit at axle + their tower offsets), `sub_y` / `main_y` the |y| of the sub / main
    rocker plates and the yoke, `pin_s` the sub pins' length, `hanger_box` {x, y0, y1, z0, z1} (z about the
    axle), `hz` the hanger's top / bottom inset, `link` the lone-middle-sub link (DQ_LINK; None = none).
    Returns [(obj, tower material key)].
    """
    P = []
    for tag, s in (("n", -1.0), ("p", 1.0)):
        cents = centres_of(s)
        for k, (xk, yk, zk) in enumerate(cents):
            P += lite_sheave_parts(f"{prefix}sheave_{tag}_{k}_rim", f"{prefix}liner_{tag}_{k}", sheave_r,
                                   sheave_w, (xk, yk, zk), segs=segs, groove=groove)
        subs = [((cents[2 * j][0] + cents[2 * j + 1][0]) / 2.0,
                 (cents[2 * j][2] + cents[2 * j + 1][2]) / 2.0) for j in range(n // 2)]
        for j, (xs, zs) in enumerate(subs):
            P.append((K.add_box(f"{prefix}rocker_sub_{tag}_{j}", (0.81, 0.066, 0.25), (xs, s * sub_y, zs)),
                      "paint"))
            P.append((K.add_cyl(f"{prefix}pivot_{tag}_s{j}", 0.03, pin_s,
                                (xs, s * (sub_y + main_y) / 2.0, zs),
                                (math.radians(90.0), 0.0, 0.0), segments=6, caps="none"), "galv"))
        pairs, single = _sub_pairs(len(subs))
        mains = [((subs[a][0] + subs[b][0]) / 2.0, (subs[a][1] + subs[b][1]) / 2.0 - axle)
                 for a, b in pairs]
        hang = mains + [(subs[i][0], subs[i][1] - axle) for i in single]
        for m, (xm, dzm) in enumerate(mains):
            P.append((K.add_box(f"{prefix}rocker_main_{tag}_{m}", (1.78, 0.072, 0.30),
                                (xm, s * main_y, axle + 0.05 + dzm)), "paint"))
            P.append((K.add_cyl(f"{prefix}pivot_{tag}_m{m}", 0.03, 0.14,
                                (xm, s * main_y, axle + 0.225 + dzm),
                                (math.radians(90.0), 0.0, 0.0), segments=6, caps="none"), "galv"))
        xs_all = [h[0] for h in hang]
        dz_y = sum(h[1] for h in hang) / len(hang)
        span = (max(xs_all) - min(xs_all)) + 0.30
        P.append((K.add_box(f"{prefix}yoke_{tag}", (span, 0.072, 0.17),
                            ((max(xs_all) + min(xs_all)) / 2.0, s * main_y,
                             axle + 0.335 + dz_y)), "paint"))
        if link is not None:
            yoke_z0 = axle + 0.335 + dz_y - 0.17 / 2.0
            for i in single:
                xs, zs = subs[i]
                z0, z1 = zs - link["below"], yoke_z0 + link["bury"]
                P.append((K.add_box(f"{prefix}rocker_link_{tag}_{i}", (link["w"], link["t"], z1 - z0),
                                    (xs, s * main_y, (z0 + z1) / 2.0)), "paint"))
        h = hanger_box
        # detail pass 1 reclaim: the hanger's outboard end runs 6 mm into the yoke; when the
        # yoke's z band encloses the (inset) hanger the end face is never seen - dropped
        yz0, yz1 = axle + 0.335 + dz_y - 0.085, axle + 0.335 + dz_y + 0.085
        hz0, hz1 = axle + h["z0"] + hz, axle + h["z1"] - hz
        enclosed = yz0 <= hz0 + 1e-9 and hz1 <= yz1 + 1e-9 and h["y1"] > main_y - 0.036 + 1e-6
        drop = (("+Y",) if s > 0 else ("-Y",)) if enclosed else ()
        P.append((K.add_box(f"{prefix}hanger_{tag}", (h["x"], h["y1"] - h["y0"], h["z1"] - h["z0"] - 2.0 * hz),
                            (0.0, s * (h["y0"] + h["y1"]) / 2.0, axle + (h["z0"] + h["z1"]) / 2.0),
                            drop=drop), "paint"))
    return P


# --------------------------------------------------------------------------------------
# The towers' finish of the train parts
# --------------------------------------------------------------------------------------

def chamfer_long_edges(obj, axis, width=None):
    """Design 3.1: chamfer the 4 long edges of a box part (bmesh bevel, 1 segment, `width`
    0.012) - the edges parallel to `axis`. +16 tris per box (octagon section)."""
    width = DP1_CHAMFER if width is None else width
    a = _unit(axis)
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    edges = [e for e in bm.edges
             if abs((e.verts[1].co - e.verts[0].co).normalized().dot(a)) > 0.999]
    bmesh.ops.bevel(bm, geom=edges, offset=width, offset_type="OFFSET", segments=1,
                    profile=0.5, affect="EDGES", clamp_overlap=True)
    cs2_asset.canonical_order_bmesh(bm)
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    return len(edges)


def bevel_parts(parts, table, width=None, strip=""):
    """lift_tower._bevel: a BEVEL_W (or `width`) bevel modifier, `segs` segments by the longest matching
    prefix of `table`, angle limit 45 deg, clamped, hardened normals, applied. `strip`: a station prefix
    removed from the name before the table lookup (the towers pass "")."""
    w = BEVEL_W if width is None else width
    for o, _k in parts:
        segs = None
        nm = o.name[len(strip):] if (strip and o.name.startswith(strip)) else o.name
        for prefix, n in sorted(table.items(), key=lambda kv: -len(kv[0])):
            if nm.startswith(prefix):
                segs = n
                break
        if not segs:
            continue
        mod = o.modifiers.new("Bevel", type="BEVEL")
        mod.width = w
        mod.segments = segs
        mod.limit_method = "ANGLE"
        mod.angle_limit = math.radians(45.0)
        mod.use_clamp_overlap = True
        mod.harden_normals = True
        bpy.ops.object.select_all(action="DESELECT")
        o.select_set(True)
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.modifier_apply(modifier=mod.name)


def apply_transforms(parts):
    """lift_tower._apply_transforms: bake every part's object transform into its mesh."""
    for o, _k in parts:
        mb = o.matrix_basis.copy()
        if mb != mathutils.Matrix.Identity(4):
            o.data.transform(mb)
            o.matrix_basis = mathutils.Matrix.Identity(4)
            o.data.update()
    bpy.context.view_layer.update()


def finish_train(parts, family, prefix=""):
    """A STATION train finished as the towers finish theirs (lift_tower.build_tower_v2 order): the train's
    bevels (TRAIN_BEVEL[family]), the transforms baked, the FG long-edge chamfers (TRAIN_CHAMFER) - so the
    station's rockers / hangers / arms have the towers' edges. Returns the chamfered part names."""
    bevel_parts(parts, TRAIN_BEVEL[family], strip=prefix)
    apply_transforms(parts)
    done = []
    for o, _k in parts:
        nm = o.name[len(prefix):] if (prefix and o.name.startswith(prefix)) else o.name
        for pre, axis in TRAIN_CHAMFER[family]:
            if nm.startswith(pre):
                chamfer_long_edges(o, axis)
                done.append(o.name)
    return done


def train_tris(parts):
    """Triangle count of a part list (n-gons fanned)."""
    return sum(sum(len(pl.vertices) - 2 for pl in o.data.polygons) for o, _k in parts)
