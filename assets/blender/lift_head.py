"""
lift_head.py - the Head bone round (DECISIONS 594; spike docs/research/tilted-towers-spike-2026-11-04.md option B).

Every DQ / GD line member and its B2 twins (Hold, Combo) get a fourth bone, `Head` (child of Root, on the trains' pivot
line, identity rest, as the halves): the head (gantry, deck, rails, hangers, plates) turns about the rope line to the mean
of the two halves (TrainPitchSystem: LiftAlignMath.HeadPitch, clamped to the type's planner max, DQ 42.0 / GD 50.2), the
halves to head +- 20. The ladder, its landings, the mast and the pad stay on Root.

The art this needs (all on the member's shipped atlas: 0 MB, lift_tower.twin_apply_atlas + a fitted island per new part):
- trunnion: two Root cheeks on the lowered mast top carry two Root stub pins on the pivot line; the head's hubs sleeve
  them (Head lugs), webs carry the hubs up to a cross-head (`gantry_trunnion_yoke`) under the gantry. DQ: the Kagelok
  girder band (gantry_00 / the GD girders, under pivot + GIRDER_ZONE) is cut out inside |y| GIRDER_X; the hubs, webs
  and cross-head take the girders' inner ends.
- comm wire on the pivot line: its anchor moves from the old hanger (comm y, gone) to a Root sleeve between the cheeks
  at (0, pivot y, 0); the import round moves the comm sub-lane point there (prefab surgery). The wire then leaves the
  tower at its span's pitch, so everything Root under its cone is cut: the mast top (MAST_DROP), the fall-arrest rail,
  any ladder rung inside the cone (cone_z).
- the landings' outer lips step in to LAND_X (a 7 mm gap to the deck lip: the deck turns, the landing does not).
Geometry is authored in the twin frame (x travel, y lateral, z up), gated in the net frame (x lateral, y travel).

Gates here (raise on failure): T0 names, the twin contracts (tops unchanged, anchors, C9 / C18), G6 with the Head bone
over cs2_skin.joint_grid(type max) with Head = (front + back) / 2 (head vs mast / trunnion / ladder / landing, halves vs
head, pins, ropes AND the comm wire on the pivot line, every crown seated), the negative (the same steep poses with the
head held level must fail), LOD budgets (5 % cap rule). Recorded, not raised: the posed head's bounds against the rest
bounds (HEAD_BOUNDS; the deck ends rise past the rest top on steep poses: an owner call, see build.md).

Usage (headless; scratch folders only, nothing exported):
  blender --background --factory-startup --python-exit-code 1 --python lift_head.py -- --keys DQ10,DQ10Hold
      --out <scratch> [--preview <dir>] [--lineup] [--report <json>]
"""
import argparse
import dataclasses
import json
import math
import os
import sys
import time

import bpy
import bmesh
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cs2_asset  # noqa: E402
import cs2_sheaves as S  # noqa: E402
import cs2_skin  # noqa: E402
import lift_tower as LT  # noqa: E402
import cs2_kit as K  # noqa: E402

HEAD_LETTER = "S"                       # the round's mesh letter (tower letters in use: J..R; twins A)
HEAD_BONE = "Head"
HEAD_BONES = ("Root", "TrainFront", "TrainBack", HEAD_BONE)
HEAD_MEMBERS = ("DQ10", "DQ12", "DQ14", "DQ16", "DQ18", "DQ20", "GD12", "GD16", "GD20")
HEAD_TWINS = tuple(k for k, (m, kind) in LT.TWINS.items() if m in HEAD_MEMBERS and kind in ("Hold", "Combo"))
HEAD_KEYS = HEAD_MEMBERS + HEAD_TWINS   # 9 + 8 = 17 meshes
# the deployed member meshes each head build reads its atlas from (gamedata-mirror prefab -> mesh CID, 2026-11-03; the
# same table as tests/test_grip_clearance.SHIPPED): lift_tower.TWIN_ATLAS names only the members with twins
HEAD_ATLAS = {"DQ10": "Q/SkiLiftTowerDQ10Q", "DQ12": "R/SkiLiftTowerDQ12R", "DQ14": "Q/SkiLiftTowerDQ14Q",
              "DQ16": "Q/SkiLiftTowerDQ16Q", "DQ18": "Q/SkiLiftTowerDQ18Q", "DQ20": "Q/SkiLiftTowerDQ20Q",
              "GD12": "R/SkiLiftTowerGD12R", "GD16": "Q/SkiLiftTowerGD16Q", "GD20": "Q/SkiLiftTowerGD20Q"}
# non-train parts on the head; everything else that is not a train part stays on Root
HEAD_PARTS = ("gantry_", "grate_deck", "kick_deck", "post_deck", "rail_deck", "endplate_", "hanger_", "plate_",
              "platerim_", "gusset_")
HEAD_LUGS = ("hanger_", "gantry_")      # Head parts a Root pin runs through (coaxial: the pin gap is 0 by construction)
HEAD_PINS = ("pivot_main_", "mast_trunnion_pin_")
MAST_DROP = {"DQ": 0.50, "GD": 0.70}    # m, mast top below the pivot: under the comm cone at the mast radius
CENTER_X = 0.125                        # m: the trunnion's half width (cheeks inside, hubs / webs outside)
GIRDER_X = 0.20                         # m: the girders' inner ends after the center cut (DQ ingest: through, GD 0.12)
GIRDER_ZONE = 0.40                      # m over the pivot (+ the head raise): the girder band the center cut touches
# the cheeks' top corner (travel 0.08, z 0.08: r 0.113) stays under the cross-head's underside (0.12) at every head
# pitch; the cross-head's underside clears the comm wire (<= 20 deg off the head) over its travel half width by 36 mm
TRUNNION = dict(cheek_x=(0.075, 0.115), cheek_w=0.16, cheek_top=0.08, pin_r=0.05, hub_r=0.10, web_w=0.12,
                yoke_z=(0.12, 0.17), yoke_w=0.16, sleeve_r=0.07, sleeve_l=0.08, arm_w=0.06, arm_t=0.04)
LAND_X = 0.688                          # m: the landings' outer lip (deck lip at 0.695)
COMM_START = 0.12                       # m out of the pivot: the comm runs start past the sleeve
CONE_MARGIN = 0.008                     # m, vertical: >= 5 mm square to the wire at the type max (DQ 5.9, GD 5.1)
HEAD_BOUNDS_XZ_TOL = 0.01               # m: the posed head's lateral / travel extents may pass the rest AABB by this
GATE = True                             # debug probes build with False (the sweep reports, never raises)


def type_max(fam):
    return LT.SKIN_TYPE_MAX_SLOPE[fam]


def cone_z(fam, zp, travel):
    """The comm wire's underside over a point `travel` m out of the pivot, at the type's steepest pitch (the vanilla LV
    hexagon, flats top / bottom: its bottom flat h / cos below the axis), minus CONE_MARGIN."""
    a = math.radians(type_max(fam))
    return zp - abs(travel) * math.tan(a) - LT.LANE_ROPE["h"] / math.cos(a) - CONE_MARGIN


def headed_spec(key):
    """The twin-shaped spec dict of a headed build: a twin key (LT.twin_spec) or a member key (kind "Member": the member
    itself, head raise 0). comm_y is None on every spec (the old comm anchor and its hanger are gone; the wire anchors
    on the pivot line)."""
    if key in LT.TWINS:
        tw = LT.twin_spec(key)
    else:
        fam = LT.VARIANTS[key].family
        if fam == "GD":
            LT.SKIN_GD["sheaves"] = LT.TWIN_GD_SHEAVES["atlas"]
        base = LT.skin_spec(LT.VARIANTS[key])
        tw = {"key": key, "member": key, "kind": "Member", "base": base, "build": base, "twin": base,
              "seat": LT.crown_seat(base), "lane": LT.lane_y(base, True), "dz": 0.0}
    for k in ("base", "build", "twin"):
        tw[k] = dataclasses.replace(tw[k], comm_y=None)
    tw["comm_old"] = LT.VARIANTS[tw["member"]].comm_y
    return tw


def head_name(tw):
    """SkiLiftTowerDQ10S, SkiLiftTowerDQ10HoldS: the name stem + the round letter (prefabs keep their names)."""
    return cs2_asset.validate_asset_name(f"{tw['twin'].name_stem}{HEAD_LETTER}")


# -- the head's geometry (twin frame: x travel, y lateral, z up) ---------------------------------------------------------

def _uv_fit(obj, atlas, donor):
    """TWIN_UV on a new part: its own box UVs squeezed into the donor's largest atlas polygon (a patch around that
    polygon's first triangle's centroid, a quarter of its edges: plain painted / galvanized surface). The donor island's
    bounding box (twin_apply_atlas's fitted rule) spans decals and stencils on these atlases, so it is not used here.
    Returns the donor."""
    a = np.array(atlas[donor]["uv"])
    best, k = None, 0
    for n in atlas[donor]["counts"]:
        p = a[k:k + n]
        k += n
        area = 0.5 * abs(float(np.dot(p[:, 0], np.roll(p[:, 1], -1)) - np.dot(p[:, 1], np.roll(p[:, 0], -1))))
        if best is None or area > best[0]:
            best = (area, p[:3])
    A, B, C = best[1]
    cen = (A + B + C) / 3.0
    me = obj.data
    own = me.uv_layers.get("UVMap")
    b = np.array([tuple(own.data[li].uv) for li in range(len(me.loops))]) if own else np.zeros((len(me.loops), 2))
    blo, bhi = b.min(axis=0), b.max(axis=0)
    span = np.where(bhi - blo > 1e-9, bhi - blo, 1.0)
    q = (b - blo) / span - 0.5
    f = cen + 0.25 * (q[:, :1] * (B - A) + q[:, 1:] * (C - A))
    lay = me.uv_layers.get(LT.TWIN_UV) or me.uv_layers.new(name=LT.TWIN_UV, do_init=False)
    for li in range(len(me.loops)):
        lay.data[li].uv = tuple(f[li])
    return donor


def _donor(atlas, *prefixes):
    for p in prefixes:
        hit = sorted(n for n in atlas if n.startswith(p))
        if hit:
            return hit[0]
    raise LT.TowerContractError(f"no atlas donor among {prefixes}")


def _fill_cut(bm, on_plane, lay):
    """Close the open boundary a cut left (edges with both verts on the plane) and give the new faces UVs from each
    vertex's existing loops."""
    edges = [e for e in bm.edges if e.is_boundary and all(on_plane(v.co) for v in e.verts)]
    if not edges:
        return 0
    res = bmesh.ops.holes_fill(bm, edges=edges, sides=0)
    new = res.get("faces", [])
    if lay is not None:
        for f in new:
            for lp in f.loops:
                other = next((l2 for l2 in lp.vert.link_loops if l2.face not in new), None)
                if other is not None:
                    lp[lay].uv = other[lay].uv
    return len(new)


def _cut_center(obj, zlim):
    """The girder band (faces wholly under zlim) cut at |y| = GIRDER_X and its middle removed, both cut faces capped:
    the comm wire runs through the gap; the trunnion's hubs, webs and cross-head take the girders' inner ends."""
    out = {}

    def fn(bm):
        lay = bm.loops.layers.uv.get(LT.TWIN_UV)
        for s in (-1.0, 1.0):
            band = [f for f in bm.faces if all(v.co.z < zlim for v in f.verts)]
            geom = list({e for f in band for e in f.edges}) + band + list({v for f in band for v in f.verts})
            bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0.0, s * GIRDER_X, 0.0), plane_no=(0.0, 1.0, 0.0))
        dead = [f for f in bm.faces if all(abs(v.co.y) <= GIRDER_X + 1e-5 and v.co.z < zlim for v in f.verts)]
        out["removed_faces"] = len(dead)
        bmesh.ops.delete(bm, geom=dead, context="FACES")
        out["cap_faces"] = sum(_fill_cut(bm, lambda c, s=s: abs(c.y - s * GIRDER_X) < 1e-4 and c.z < zlim, lay)
                               for s in (-1.0, 1.0))
    LT._bm_edit(obj, fn)
    return out


RAIL_CLEAR = 0.012                      # m: the ladder rails' tops under the swept head (G6 asks 5 mm)
RAIL_MIN_OVER_LANDING = 0.30            # m: a shorter grab extension than this is an owner call (raises)


def _clip_ladder_rails(by, tw, zp):
    """The ladder rails' grab extensions over the landing, cut under the head's swept envelope: every Head part's
    faces sampled where they cross the rails' lateral band, turned about the pivot line by every head angle of the
    grid (both signs), the lowest point over the rails' travel window less RAIL_CLEAR. Only the raised twin heads
    (Hold) reach the rails; members and most twins leave them as built. Returns {rail: (old top, new top)} rel pivot."""
    rails = [(n, o) for n, (o, _k) in by.items() if n.startswith("ladder_rail_")]
    head = [o for n, (o, _k) in by.items() if n.startswith(HEAD_PARTS)]
    land = [v.co.z for n, (o, _k) in by.items() if n.startswith("grate_land") for v in o.data.vertices]
    angs = sorted({abs(math.radians(p[HEAD_BONE])) for p in head_grid(tw)})
    out = {}
    bcache = {}

    def bary(k):                            # a k-step barycentric grid (k from the triangle's size: <= 2 cm steps)
        if k not in bcache:
            g = np.linspace(0.0, 1.0, k + 1)
            bcache[k] = np.array([(a, b, 1.0 - a - b) for a in g for b in g if a + b <= 1.0 + 1e-9])
        return bcache[k]
    for n, o in rails:
        rv = np.array([tuple(v.co) for v in o.data.vertices])
        x0, x1 = rv[:, 0].min() - RAIL_CLEAR, rv[:, 0].max() + RAIL_CLEAR
        y0, y1 = rv[:, 1].min() - RAIL_CLEAR, rv[:, 1].max() + RAIL_CLEAR
        pts = []
        for h in head:
            me = h.data
            me.calc_loop_triangles()
            hv = np.array([tuple(v.co) for v in me.vertices])
            for tri in me.loop_triangles:
                c = hv[list(tri.vertices)]
                if c[:, 1].max() < y0 or c[:, 1].min() > y1:
                    continue
                edge = max(np.linalg.norm(c[0] - c[1]), np.linalg.norm(c[1] - c[2]), np.linalg.norm(c[2] - c[0]))
                p = bary(int(min(200, max(2, math.ceil(edge / 0.02))))) @ c
                pts.append(p[(p[:, 1] >= y0) & (p[:, 1] <= y1)])
        if not pts:
            continue
        p = np.concatenate(pts)
        zmin = None
        for a in angs:
            for s in (-1.0, 1.0):
                ca, sa = math.cos(s * a), math.sin(s * a)
                x = p[:, 0] * ca - (p[:, 2] - zp) * sa
                z = zp + p[:, 0] * sa + (p[:, 2] - zp) * ca
                m = (x >= x0) & (x <= x1)
                if m.any():
                    zmin = float(z[m].min()) if zmin is None else min(zmin, float(z[m].min()))
        top = float(rv[:, 2].max())
        if zmin is None or zmin - RAIL_CLEAR >= top:
            continue
        zc = zmin - RAIL_CLEAR
        lt = max(land) if land else zp
        LT._check(zc - lt >= RAIL_MIN_OVER_LANDING, f"[{tw['key']}] H2 {n} grab extension over the landing after the "
                  f"head clip >= {RAIL_MIN_OVER_LANDING}", f"({zc - lt:.3f})")
        LT._bm_edit(o, lambda bm, z=zc: [setattr(v.co, "z", min(v.co.z, z)) for v in bm.verts])
        out[n] = (round(top - zp, 4), round(zc - zp, 4))
    return out


def head_transform(parts, tw, atlas):
    """The head round's geometry on the atlas-mapped parts (after twin_transform for twins). Returns (parts, report)."""
    spec = tw["twin"]
    fam = spec.family
    zp = tw["lane"]
    T = TRUNNION
    rep = {"cut": {}, "rungs_removed": [], "arrest_top": None, "mast_top": None, "landing_x": LAND_X, "new": {}}
    by = {o.name: (o, k) for o, k in parts}
    # mast: lowered under the comm cone at its radius
    zm = round(zp - MAST_DROP[fam], 6)
    worst = cone_z(fam, zp, spec.mast_r)
    LT._check(zm <= worst, f"[{spec.key}] H1 mast top {zm:.3f} under the comm cone at r {spec.mast_r}",
              f"(cone {worst:.3f})")
    LT._bm_edit(by["mast"][0], lambda bm: [setattr(v.co, "z", min(v.co.z, zm)) for v in bm.verts])
    rep["mast_top"] = zm
    # the head's center: DQ king post / GD girder ends
    zlim = zp + GIRDER_ZONE + tw["dz"]
    for n in (("gantry_00",) if fam == "DQ" else ("gantry_girder_n", "gantry_girder_p")):
        rep["cut"][n] = _cut_center(by[n][0], zlim)
    # where the head's girders start beside the trunnion (the hubs reach them)
    gy = []
    for n, (o, _k) in by.items():
        if n.startswith("gantry_"):
            for v in o.data.vertices:
                if CENTER_X + 1e-3 < abs(v.co.y) < 0.8 and abs(v.co.z - zp) < 0.9:
                    gy.append(abs(v.co.y))
    x_end = round(min(gy) + 0.01, 4) if gy else 0.30
    rep["hub_to"] = x_end
    # the girders' underside at their inner ends: the webs reach up past it (a raised Hold head included)
    gz = [v.co.z for n, (o, _k) in by.items() if n.startswith("gantry_") for v in o.data.vertices
          if GIRDER_X - 1e-3 < abs(v.co.y) < x_end + 0.05 and zp - 0.9 < v.co.z < zlim]
    web_top = max(zp + T["yoke_z"][1], (min(gz) + 0.03) if gz else zp)
    rep["web_top"] = round(web_top - zp, 4)
    # landings: the outer lips step in (the deck turns past them)
    for n, (o, _k) in by.items():
        if n.startswith("grate_land"):
            LT._bm_edit(o, lambda bm: [setattr(v.co, "y", math.copysign(LAND_X, v.co.y)) for v in bm.verts
                                       if abs(v.co.y) > LAND_X + 1e-4])
    # ladder rungs and standoffs inside the comm wire's wedge go; the fall-arrest rail is cut to it
    drop = []
    for n, (o, _k) in list(by.items()):
        if n.startswith(("ladder_rung_", "ladder_standoff_")):
            if any(v.co.z > cone_z(fam, zp, v.co.x) for v in o.data.vertices):
                drop.append(n)
    for n in drop:
        o = by.pop(n)[0]
        me = o.data
        bpy.data.objects.remove(o, do_unlink=True)
        bpy.data.meshes.remove(me)
    rep["rungs_removed"] = sorted(drop)
    rep["ladder_rail_top"] = _clip_ladder_rails(by, tw, zp)
    for n, (o, _k) in by.items():
        if n.startswith("arrest_"):
            xs = [abs(v.co.x) for v in o.data.vertices]
            ztop = cone_z(fam, zp, max(xs))        # the far face: the wire is lowest there
            LT._bm_edit(o, lambda bm, z=ztop: [setattr(v.co, "z", min(v.co.z, z)) for v in bm.verts])
            rep["arrest_top"] = round(ztop, 4)
    # the trunnion and the comm sleeve
    x0, x1 = T["cheek_x"]
    new = []
    for tag, s in (("n", -1.0), ("p", 1.0)):
        h = zp + T["cheek_top"] - (zm - 0.02)
        new.append((K.add_box(f"mast_cheek_{tag}", (T["cheek_w"], x1 - x0, h),
                              (0.0, s * (x0 + x1) / 2.0, (zp + T["cheek_top"] + zm - 0.02) / 2.0)), "paint", "mast"))
        pa, pb = x0, (CENTER_X + x_end) / 2.0
        new.append((K.add_cyl(f"mast_trunnion_pin_{tag}", T["pin_r"], pb - pa, (0.0, s * (pa + pb) / 2.0, zp),
                              (math.radians(90.0), 0.0, 0.0), segments=6, caps="none"), "galv", "pivot_main_"))
        new.append((K.add_cyl(f"gantry_trunnion_hub_{tag}", T["hub_r"], x_end - CENTER_X,
                              (0.0, s * (CENTER_X + x_end) / 2.0, zp), (math.radians(90.0), 0.0, 0.0), segments=8,
                              caps="both"), "paint", "gantry_"))
        z0, z1 = zp, web_top
        new.append((K.add_box(f"gantry_trunnion_web_{tag}", (T["web_w"], x_end - CENTER_X, z1 - z0),
                              (0.0, s * (CENTER_X + x_end) / 2.0, (z0 + z1) / 2.0)), "paint", "gantry_"))
        new.append((K.add_box(f"comm_sleeve_arm_{tag}", (T["arm_w"], x0 + 0.002 - (T["sleeve_r"] - 0.01), T["arm_t"]),
                              (0.0, s * (x0 + 0.002 + T["sleeve_r"] - 0.01) / 2.0, zp)), "galv", "comm_"))
    yz0, yz1 = T["yoke_z"]
    new.append((K.add_box("gantry_trunnion_yoke", (T["yoke_w"], 2.0 * x_end, yz1 - yz0),
                          (0.0, 0.0, zp + (yz0 + yz1) / 2.0)), "paint", "gantry_"))
    new.append((K.add_cyl("comm_sleeve", T["sleeve_r"], T["sleeve_l"], (0.0, 0.0, zp), (0.0, math.radians(90.0), 0.0),
                          segments=6, caps="none"), "galv", "comm_"))
    for o, key, dp in new:
        LT._apply_transforms([(o, key)])
        rep["new"][o.name] = _uv_fit(o, atlas, _donor(atlas, dp, "gantry_", "mast"))
        by[o.name] = (o, key)
    return [v for v in by.values()], rep


def build_headed(tw, head, atlas, name):
    """build_twin with the head round: the member's parts at tw["build"] -> bevel -> transforms -> detail_post -> atlas ->
    the kind (twins) -> head_transform -> tag -> join -> contracts -> orient -> the bones (train + Head) -> G6 (headed).
    Returns (obj, table, info)."""
    spec, build = tw["twin"], tw["build"]
    parts, info = LT.dq_parts(build, head, LT.KAGELOK_BLEND)
    info["head"] = head
    LT._bevel(parts, LT.DQ_BEVEL)
    LT._apply_transforms(parts)
    info["detail_post"] = LT.detail_post(parts, build, info)
    LT.check_c10(parts, build)
    info["atlas_map"] = LT.twin_apply_atlas(parts, atlas)
    if tw["kind"] != "Member":
        parts, info["twin_transform"] = LT.twin_transform(parts, tw, info)
    else:
        info["twin_transform"] = {}
    parts, info["head_transform"] = head_transform(parts, tw, atlas)
    l1, l2 = LT.lod_prefixes(spec)
    known = tuple(LT.density_table(spec)) + l1 + l2 + LT.DQ_KEEP
    names = [o.name for o, _k in parts]
    LT._check(len(set(names)) == len(names) and all(n.startswith(known) for n in names),
              f"[{spec.key}] T0 part names unique and in the DENSITY / LOD / keep tables",
              f"({[n for n in names if not n.startswith(known)][:6]})")
    info["part_material"] = {o.name: k for o, k in parts}
    table = cs2_asset.tag_parts(parts)
    bpy.ops.object.select_all(action="DESELECT")
    for o, _k in parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0][0]
    bpy.ops.object.join()
    t = bpy.context.active_object
    t.name = t.data.name = name
    for lay in [l for l in t.data.uv_layers if l.name != LT.TWIN_UV]:
        t.data.uv_layers.remove(lay)
    t.data.uv_layers[LT.TWIN_UV].active = True
    t.data.uv_layers[LT.TWIN_UV].active_render = True
    bm = bmesh.new()
    bm.from_mesh(t.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=0.0005)
    bm.to_mesh(t.data)
    bm.free()
    t.data.update()
    cs2_asset.store_part_table(t, table)
    cs2_asset.shade_by_angle(t, LT.SHADE_ANGLE_V2, weighted=True)
    ctw = dict(tw, kind="Break" if tw["kind"] == "Member" else tw["kind"])   # Member: every wheel under the rope
    info["contracts_authoring"] = LT.assert_twin_contracts(t, ctw, "authoring")
    cs2_asset.orient_for_net(t)
    info["contracts_net"] = LT.assert_twin_contracts(t, ctw, "net")
    info["parts"] = len(table)
    info["skin_part_bone"] = head_part_bones(t, table, spec)
    info["skin"] = headed_sweep(t, table, tw, info["skin_part_bone"], gate=GATE)
    return t, table, info


def head_part_bones(t, table, spec):
    """{part: bone} for every non-Root part: the halves by travel side (lift_tower.skin_part_bones), the head parts
    (HEAD_PARTS) on Head."""
    pb = dict(LT.skin_part_bones(t, table, spec))
    for n in table:
        if n not in pb and n.startswith(HEAD_PARTS):
            pb[n] = HEAD_BONE
    return pb


# -- G6 with the head -----------------------------------------------------------------------------------------------------

def head_grid(tw, tmax=None):
    """[{TrainFront, TrainBack, Head}] deg: cs2_skin.joint_grid(type max) (Hold: the concave half), Head = the mean."""
    fam = tw["twin"].family
    tmax = type_max(fam) if tmax is None else tmax
    brk = LT.TWIN_HALF_BREAK.get(tw["kind"], cs2_skin.HALF_BREAK_MAX)
    grid = cs2_skin.joint_grid(tmax, brk)
    if LT.TWIN_CONCAVE_ONLY.get(tw["kind"], False):
        grid = [(f, b) for f, b in grid if f - b <= 1e-9]
    return [{"TrainFront": f, "TrainBack": b, HEAD_BONE: round((f + b) / 2.0, 4)} for f, b in grid]


def comm_runs(spec, pose, zp, run=None, start=COMM_START):
    """The comm wire as the game draws it on the pivot line: per half one straight run from the sleeve outward at that
    half's pitch (its span's rope pitch: the comm wire spans the same nodes), lateral 0, the vanilla LV hexagon."""
    ext = LT.skin_half_extent(spec) + (LT.SKIN_ROPE_RUN if run is None else run)
    out = []
    for b, sign in (("TrainFront", cs2_skin.FRONT_SIGN_NET), ("TrainBack", -cs2_skin.FRONT_SIGN_NET)):
        out.append(cs2_skin.rope_run(0.0, (0.0, zp), pose.get(b, 0.0), sign, ext, r=LT.LANE_ROPE["r"], start=start))
    return out


def headed_sweep(obj, table, tw, part_bone, poses=None, label=None, gate=True):
    """G6 with four bones (cs2_skin.sweep): the halves and the Head posed about the one pivot line, Root fixed; pins
    (main pins, trunnion pins) exempt vs the lugs (yokes; Head hangers / gantry parts); the ropes and the comm wire;
    every crown seated (support crowns from below, compression crowns mirrored, twin_sweep's rule). Then the posed
    head's bounds against the rest AABB (recorded). Raises on a failed gate when `gate`."""
    spec = tw["twin"]
    fam = spec.family
    zp = LT.lane_y(spec, True)
    bones = list(HEAD_BONES)
    names, fb, vb = cs2_skin.face_bones(obj, table, part_bone, bones)
    co = cs2_skin.coords(obj)
    polys = [tuple(p.vertices) for p in obj.data.polygons]
    piv = LT.skin_pivot(spec)
    pivots = {b: piv for b in bones[1:]}
    poses = head_grid(tw) if poses is None else poses
    is_wheel = lambda n: n.startswith(("sheave_", "liner_"))       # noqa: E731
    fx = np.array([co[list(p), 0].mean() for p in polys])
    fz = {}
    for i, n in enumerate(names):
        if is_wheel(n):
            fz.setdefault(n, []).append(i)
    over = {n: float(np.mean([co[list(polys[i]), 2].mean() for i in ids])) > zp for n, ids in fz.items()}
    wf = {}
    for b in bones[1:3]:
        for side in (-1.0, 1.0):
            for up in (False, True):
                wf[(b, side, up)] = [i for i, n in enumerate(names) if is_wheel(n) and bones[fb[i]] == b
                                     and np.sign(fx[i]) == side and over[n] == up]
    ext = LT.skin_half_extent(spec) + 0.30
    R, h = LT.LANE_ROPE["r"], LT.LANE_ROPE["h"]

    def seats(pc, ang):
        out = {}
        pm = pc.copy()
        pm[:, 2] = 2.0 * zp - pm[:, 2]
        for b, sign in (("TrainFront", cs2_skin.FRONT_SIGN_NET), ("TrainBack", -cs2_skin.FRONT_SIGN_NET)):
            for side in (-1.0, 1.0):
                k = f"{b}_{'n' if side < 0 else 'p'}"
                out[k] = cs2_skin.seat_half(pc, polys, wf[(b, side, False)], piv, ang.get(b, 0.0),
                                            side * spec.gauge / 2.0, sign, ext, R, h)
                out[k + "_c"] = cs2_skin.seat_half(pm, polys, wf[(b, side, True)], piv, -ang.get(b, 0.0),
                                                   side * spec.gauge / 2.0, sign, ext, R, h)
        return out

    lug = LT.SKIN_LUG[fam]
    lugs = {"TrainFront": lambda n: n.startswith(lug), "TrainBack": lambda n: n.startswith(lug),
            HEAD_BONE: lambda n: n.startswith(HEAD_LUGS)}
    rep = cs2_skin.sweep(co, polys, names, fb, bones, pivots, poses,
                         pins=lambda n: n.startswith(HEAD_PINS), lugs=lugs, wheels=is_wheel,
                         ropes=lambda ang: LT.skin_rope_runs(spec, ang) + comm_runs(spec, ang, zp), seats=seats,
                         clear_min=LT.SKIN_CLEAR_MIN, pin_gap_max=LT.SKIN_PIN_GAP_MAX, label=label or spec.key,
                         log=lambda s: None)
    rep["bounds"] = head_bounds(co, vb, bones, pivots, poses)
    bad = None
    try:
        cs2_skin.check_rows(rep["poses"], label or spec.key, LT.SKIN_CLEAR_MIN, LT.SKIN_PIN_GAP_MAX,
                            seat_tops=2 * spec.sheaves, seat_gap=LT.SKIN_SEAT_GAP)
    except cs2_skin.SkinError as e:
        bad = str(e)
    rep["failed"] = bad
    mc = rep["min_clear"]
    print(f"[head] G6 [{label or spec.key}] {len(poses)} poses (type max {type_max(fam)}, Head = mean): min clearance "
          f"{mc['m'] * 1000:.1f} mm at {mc['pose']} ({' / '.join(str(n) for n in mc['parts'] or [])}); "
          f"{'FAIL ' + bad[:300] if bad else 'pass'}")
    if bad and gate:
        raise LT.TowerContractError(bad)
    return rep


def head_bounds(co, vb, bones, pivots, poses):
    """The posed head against the rest AABB (net frame, the whole mesh at rest): per pose how far its vertices rise past
    the rest top (> 0: past it; the bounds' max y is the AnchorTop seat and is never padded) and pass the rest box
    laterally / along travel. Returns the worst of each with its pose."""
    hi = bones.index(HEAD_BONE)
    lo_r, hi_r = co.min(axis=0), co.max(axis=0)
    m = vb == hi
    worst = {"top_over_m": -9.0, "top_pose": None, "xy_over_m": -9.0, "xy_pose": None}
    for ang in poses:
        pc = cs2_skin.posed(co, vb, bones, ang, pivots)
        h = pc[m]
        top = float(h[:, 2].max() - hi_r[2])
        xy = float(max((h[:, :2].max(axis=0) - hi_r[:2]).max(), (lo_r[:2] - h[:, :2].min(axis=0)).max()))
        if top > worst["top_over_m"]:
            worst.update(top_over_m=round(top, 4), top_pose=ang)
        if xy > worst["xy_over_m"]:
            worst.update(xy_over_m=round(xy, 4), xy_pose=ang)
    worst["pass_top"] = worst["top_over_m"] <= 0.0
    worst["pass_xy"] = worst["xy_over_m"] <= HEAD_BOUNDS_XZ_TOL
    worst["rest_top"] = round(float(hi_r[2]), 4)
    return worst


def pitch_for_top(co, vb, bones, pivots, step=0.5, tmax=60.0):
    """The largest uniform head pitch (deg, both signs) whose posed head stays under the rest top."""
    hi = bones.index(HEAD_BONE)
    top = co[:, 2].max()
    a = 0.0
    while a + step <= tmax:
        ok = True
        for s in (1.0, -1.0):
            pc = cs2_skin.posed(co, vb, bones, {HEAD_BONE: s * (a + step)}, pivots)
            ok &= bool(pc[vb == hi, 2].max() <= top + 1e-6)
        if not ok:
            break
        a += step
    return a


def negative(t, table, tw, pb):
    """The head held level on the steep uniform poses must fail G6 (the gate sees the head)."""
    fam = tw["twin"].family
    tm = type_max(fam)
    poses = [{"TrainFront": s * tm, "TrainBack": s * tm, HEAD_BONE: 0.0} for s in (1.0, -1.0)]
    rep = headed_sweep(t, table, tw, pb, poses=poses, label=f"{tw['key']} negative (head level)", gate=False)
    LT._check(rep["failed"] is not None, f"[{tw['key']}] G6 negative: the head held level at +-{tm} deg fails",
              f"(min clearance {rep['min_clear']})")
    return {"poses": len(poses), "failed": rep["failed"][:200], "min_clear": rep["min_clear"]}


# -- previews --------------------------------------------------------------------------------------------------------------

def _mesh_objs(runs, name, mat):
    objs = []
    for v, f in runs:
        me = bpy.data.meshes.new(name)
        me.from_pydata(v, [], f)
        me.materials.append(mat)
        objs.append(bpy.data.objects.new(me.name, me))
        bpy.context.collection.objects.link(objs[-1])
    return objs


def _rope_mat(name):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = (0.03, 0.03, 0.03, 1.0)
    bsdf.inputs["Metallic"].default_value = 0.7
    bsdf.inputs["Roughness"].default_value = 0.45
    return mat


def _drop(objs):
    for o in objs:
        me = o.data
        bpy.data.objects.remove(o, do_unlink=True)
        if me is not None and me.users == 0:
            bpy.data.meshes.remove(me)


def render_pose(t, arm, spec, pose, asset_dir, name, raw, views, comm=True, extras=()):
    """`t` posed by `arm` at `pose` with the ropes and the comm wire at that pose; returns the frame paths."""
    zp = LT.lane_y(spec, True)
    pivots = {b: LT.skin_pivot(spec) for b in HEAD_BONES[1:]}
    tmp = cs2_skin.posed_copy(t, arm, pose, pivots, t.name + "Posed")
    mat = _rope_mat(name + "_HeadRope")
    runs = LT.skin_rope_runs(spec, pose, run=2.0)
    if comm:
        runs += comm_runs(spec, pose, zp, run=4.0)
    own = _mesh_objs(runs, name + "Rope", mat)
    try:
        return LT.render_tower_views(tmp, asset_dir, name, raw, views, extras=own + list(extras), light=LT.SKIN_LIGHT)
    finally:
        _drop(own + [tmp])


def montage(paths_grid, out_path, gap=12):
    """Rows x cols of equal-size PNGs into one sheet (white gaps). Missing cells stay white."""
    imgs = [[bpy.data.images.load(p) if p else None for p in row] for row in paths_grid]
    first = next(i for row in imgs for i in row if i is not None)
    w, h = first.size
    rows, cols = len(imgs), max(len(r) for r in imgs)
    W, H = cols * w + (cols + 1) * gap, rows * h + (rows + 1) * gap
    sheet = np.ones((H, W, 4), dtype=np.float32)
    for r, row in enumerate(imgs):
        for c, im in enumerate(row):
            if im is None:
                continue
            px = np.array(im.pixels[:], dtype=np.float32).reshape(h, w, 4)
            y0 = H - (r + 1) * (h + gap)
            x0 = gap + c * (w + gap)
            sheet[y0:y0 + h, x0:x0 + w] = px
            bpy.data.images.remove(im)
    out = bpy.data.images.new(os.path.basename(out_path), W, H, alpha=True)
    out.pixels = sheet.ravel()
    out.filepath_raw = out_path
    out.file_format = "PNG"
    out.save()
    bpy.data.images.remove(out)
    return out_path


def preview_angles(fam):
    return (0.0, 20.0, 42.0 if fam == "DQ" else 50.0)


def lineup_views(tw, a):
    zp = tw["lane"]
    spec = tw["twin"]
    k = 50.0 / 15.6
    train = LT.skin_half_extent(spec) + 0.9
    tgt = (0.0, 0.0, zp + 0.45)
    tag = f"p{int(round(a))}"
    return [dict(name=f"side_{tag}", dir=(1.0, 0.0, 0.08), target=tgt, lens=50.0, dist=(train + 0.6) * k),
            dict(name=f"q34_{tag}", dir=(1.0, -1.1, 0.35), target=tgt, lens=50.0,
                 dist=(spec.gauge / 2.0 + 0.9) * k * 1.3)]


def key_previews(t, arm, lods, tw, asset_dir, name, raw, ref=None):
    """Per key: the lineup frames (0 / 20 / max uniform rope pitch, side at rope height and 3/4); members also the
    trunnion close-ups, the carrier's grip past the raised deck end, the LOD sheet and the comm wire before / after."""
    spec, fam = tw["twin"], tw["twin"].family
    zp = tw["lane"]
    out = {"lineup": {}}
    for a in preview_angles(fam):
        pose = {"TrainFront": a, "TrainBack": a, HEAD_BONE: a}
        out["lineup"][a] = render_pose(t, arm, spec, pose, asset_dir, name, raw, lineup_views(tw, a))
    if tw["kind"] != "Member":
        return out
    tm = type_max(fam)
    pose = {"TrainFront": tm, "TrainBack": tm, HEAD_BONE: tm}
    piv = (0.0, 0.0, zp)
    out["trunnion"] = render_pose(t, arm, spec, pose, asset_dir, name, raw, [
        dict(name="trunnion_34", dir=(1.0, -1.0, 0.55), target=piv, lens=50.0, dist=2.4),
        dict(name="trunnion_end", dir=(0.0, 1.0, 0.25), target=piv, lens=50.0, dist=2.2)])
    # the carrier's grip on the n rope past the raised deck end at the steepest pitch (the 592 i clash)
    grip, _row = LT._grip_object(fam, 1.0)
    lane_x = -spec.gauge / 2.0
    deck = float(np.abs(cs2_skin.coords(t)[:, 1]).max())
    best = None
    for sign in (1.0, -1.0):
        p = cs2_skin.rotate_x(np.array([[lane_x, sign * (deck - 0.2), zp]]), tm, (0.0, zp))[0]
        if best is None or p[2] > best[1][2]:
            best = (sign, p)
    sign, p = best
    gco = cs2_skin.coords(grip) + np.array([lane_x, sign * (deck - 0.2), zp])
    gco = cs2_skin.rotate_x(gco, tm, (0.0, zp))
    for i, v in enumerate(grip.data.vertices):
        v.co = tuple(gco[i])
    grip.data.update()
    try:
        out["carrier"] = render_pose(t, arm, spec, pose, asset_dir, name, raw, [
            dict(name="carrier_end", dir=(-1.0, -0.15, 0.12), target=tuple(p), lens=50.0, dist=3.2),
            dict(name="carrier_end_34", dir=(-1.0, 0.9 * sign, 0.45), target=tuple(p), lens=50.0, dist=3.8)],
            extras=(grip,))
    finally:
        _drop([grip])
    # the LOD sheet: every level posed at 20 deg (every level is weighted)
    tgt_top = spec.top / 2.0
    view = dict(name="lod", dir=(1.0, -1.1, 0.30), target=(0.0, 0.0, tgt_top), lens=35.0,
                dist=(tgt_top + 1.0) * 35.0 / 12.0)
    lv = []
    for level, ob in enumerate([t] + list(lods)):
        lv += render_pose(ob, arm, spec, {"TrainFront": 20.0, "TrainBack": 20.0, HEAD_BONE: 20.0}, asset_dir, name,
                          raw, [dict(view, name=f"lod{level}_p20")])
    out["lod"] = lv
    # the comm wire: before (the shipped member at rest, its wire level at the old anchor) / after (the pivot line)
    side = dict(name="comm_after", dir=(1.0, 0.35, -0.45), target=(0.0, 0.0, zp - 0.4), lens=35.0, dist=9.0)
    out["comm_after"] = render_pose(t, arm, spec, pose, asset_dir, name, raw, [side])
    if ref is not None and tw["comm_old"] is not None:
        mat = _rope_mat(name + "_HeadRope")
        runs = [cs2_skin.rope_run(0.0, (0.0, tw["comm_old"]), 0.0, s, 4.0, r=LT.LANE_ROPE["r"]) for s in (1.0, -1.0)]
        runs += LT.skin_rope_runs(tw["base"], {}, run=2.0)
        ex = _mesh_objs(runs, name + "Rope", mat)
        rname = ref.name
        rdir = os.path.join(os.path.dirname(asset_dir), rname)
        LT.stage_textures(tw["member"], rdir, rname)
        ref.hide_render = False
        try:
            out["comm_before"] = LT.render_tower_views(ref, rdir, rname, raw, [dict(side, name="comm_before")],
                                                       extras=ex, light=LT.SKIN_LIGHT)
        finally:
            ref.hide_render = True
            _drop(ex)
    return out


# -- one key ---------------------------------------------------------------------------------------------------------------

def head_lod_chain(t, name, spec):
    """lod_chain_v2 + the trunnion's small parts: the pins (inside the hubs) leave at LOD1, the hubs at LOD2; the cheeks
    and webs stay on every level (without them the LOD2 head floats 0.5 / 0.7 m over the lowered mast top; the comm sleeve
    leaves at LOD1 with comm_, the cross-head stays with gantry_)."""
    l1p, l2p = LT.lod_prefixes(spec)
    l1p = tuple(l1p) + ("mast_trunnion_pin_",)
    l2p = tuple(l2p) + ("mast_trunnion_pin_", "gantry_trunnion_hub_")
    return cs2_asset.make_lod_chain(t, name, targets=(spec.lod1, spec.lod2), delete_prefixes=(l1p, l2p),
                                    tol=LT.LOD_TOL, shade_angle=LT.SHADE_ANGLE_V2, fixed=S.lod_fixed())


TRUNNION_PREFIXES = ("mast_cheek_", "mast_trunnion_", "gantry_trunnion_", "comm_sleeve")


def run_key(key, out, preview=None, keep_ref=False):
    t0 = time.perf_counter()
    member = LT.TWINS[key][0] if key in LT.TWINS else key
    fam = LT.VARIANTS[member].family
    head = LT.TWIN_HEAD[fam]
    LT.clear_scene()
    atlas, arep, ref = LT.twin_atlas(member, head, keep_ref=keep_ref)
    if ref is not None:
        ref.hide_render = True
    tw = headed_spec(key)
    spec = tw["twin"]
    name = head_name(tw)
    asset_dir = os.path.join(out, name)
    os.makedirs(asset_dir, exist_ok=True)
    t, table, info = build_headed(tw, head, atlas, name)
    pb = info["skin_part_bone"]
    neg = negative(t, table, tw, pb)
    _names, _fb, vb = cs2_skin.face_bones(t, table, pb, list(HEAD_BONES))
    piv = {b: LT.skin_pivot(spec) for b in HEAD_BONES[1:]}
    fits_to = pitch_for_top(cs2_skin.coords(t), vb, list(HEAD_BONES), piv)
    lods = head_lod_chain(t, name, spec)
    cap0 = spec.cap if tw["kind"] != "Member" else LT.LOD0_CAP[fam]
    bud = LT.twin_budgets(t, lods, dict(tw, twin=dataclasses.replace(spec, cap=cap0)))
    heads = {"Root": (0.0, 0.0, 0.0)}
    heads.update({b: (0.0, 0.0, tw["lane"]) for b in HEAD_BONES[1:]})
    arm = cs2_skin.build_armature(f"{name}Rig", heads)
    weights = {}
    for level, ob in enumerate([t] + list(lods)):
        w = cs2_skin.assign(ob, table, pb, list(HEAD_BONES))
        weights[str(level)] = {b: (len(v) if hasattr(v, "__len__") else v) for b, v in w.items()}
        cs2_asset.check_skin_weights(ob, arm)
    frames = None
    if preview:
        LT.stage_textures(member, asset_dir, name)
        frames = key_previews(t, arm, lods, tw, asset_dir, name, os.path.join(preview, "raw"), ref=ref)
    sk = info["skin"]
    hb = sk["bounds"]
    ptris = {}
    part = np.zeros(len(t.data.polygons), dtype=np.int32)
    t.data.attributes["part"].data.foreach_get("value", part)
    lt = np.zeros(len(t.data.polygons), dtype=np.int32)
    t.data.polygons.foreach_get("loop_total", lt)
    for q, n in zip(part, lt):
        ptris[table[q - 1]] = ptris.get(table[q - 1], 0) + int(n) - 2
    new_tris = sum(v for k, v in ptris.items() if k.startswith(TRUNNION_PREFIXES))
    rep = {"key": key, "name": name, "member": member, "kind": tw["kind"], "family": fam,
           "pivot_y": tw["lane"], "top": spec.top, "comm_anchor_old_y": tw["comm_old"], "comm_anchor_new_y": tw["lane"],
           "tris": bud["tris"], "cap": bud["cap"], "cap_raised_to": bud["cap_raised_to"], "trunnion_tris": new_tris,
           "atlas": dict(arep, exact=info["atlas_map"]["exact"], donor=info["atlas_map"]["donor"],
                         fitted=info["atlas_map"]["fitted"], textures_added_mb=0.0),
           "head_transform": info["head_transform"],
           "g6": {"poses": len(sk["poses"]), "min_clear": sk["min_clear"], "type_max_deg": type_max(fam),
                  "worst_rope_clear_m": min(r["rope_clear_m"] for r in sk["poses"]),
                  "max_pin_gap_m": max(r["pin_gap_m"] for r in sk["poses"])},
           "negative": neg, "bounds": hb, "head_under_top_to_deg": fits_to,
           "head_parts": sorted(n for n, b in pb.items() if b == HEAD_BONE),
           "weights": weights,
           "contracts": {k: info["contracts_net"][k] for k in ("top", "E", "top_gap", "top_nearest", "lane_y")},
           "frames": frames, "seconds": round(time.perf_counter() - t0, 1)}
    raised = f", raised to {bud['cap_raised_to']}" if bud["cap_raised_to"] else ""
    print(f"[head] done {name}: LOD0/1/2 {bud['tris']} (cap {bud['cap']}{raised}), trunnion {new_tris} tris, G6 "
          f"{len(sk['poses'])} poses min {sk['min_clear']['m'] * 1000:.1f} mm, negative fails, bounds top over "
          f"{hb['top_over_m']:+.3f} m (head under the top to {fits_to} deg), xy over {hb['xy_over_m']:+.3f}, rungs cut "
          f"{info['head_transform']['rungs_removed']}, {rep['seconds']} s")
    return rep, (t, arm, lods, tw, name, asset_dir)


def parse(argv):
    ap = argparse.ArgumentParser(prog="lift_head.py")
    ap.add_argument("--keys", required=True, help=f"comma list of {', '.join(HEAD_KEYS)} (or 'all')")
    ap.add_argument("--out", required=True, help="scratch folder (never assets/import)")
    ap.add_argument("--preview", default=None, help="preview folder (raw frames under <preview>/raw)")
    ap.add_argument("--lineup", default=None, help="also the family lineup sheets <preview>/<this>_side.png / _q34.png")
    ap.add_argument("--report", default=None)
    return ap.parse_args(argv)


def main_cli():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    args = parse(argv)
    out = os.path.abspath(args.out)
    if os.path.normcase(out).startswith(os.path.normcase(os.path.join(LT._REPO, "assets", "import"))):
        raise SystemExit("--out is in the shipping tree assets/import: this round exports nothing")
    keys = list(HEAD_KEYS) if args.keys == "all" else args.keys.split(",")
    bad = [k for k in keys if k not in HEAD_KEYS]
    if bad:
        raise SystemExit(f"unknown keys {bad}")
    S.set_wheels("v2")
    S.set_wheel_dress(True)
    LT.set_skinned(True)
    LT.TWIN_ATLAS.update(HEAD_ATLAS)
    reps = []
    for k in keys:
        rep, _objs = run_key(k, out, args.preview, keep_ref=bool(args.preview) and k not in LT.TWINS)
        reps.append(rep)
        if args.report:
            with open(args.report, "w", encoding="utf-8", newline="\n") as f:
                json.dump(reps, f, indent=1, default=lambda o: o.item() if hasattr(o, "item") else str(o))
    if args.preview and args.lineup:
        for view in ("side", "q34"):
            grid = []
            for r in reps:
                lu = r["frames"]["lineup"]
                grid.append([next((p for p in lu[a] if f"_view_{view}_" in os.path.basename(p)), None)
                             for a in sorted(lu)])
            print(f"[head] lineup sheet {montage(grid, os.path.join(args.preview, f'{args.lineup}_{view}.png'))}")
    if args.preview:
        for r in reps:
            fr = r["frames"] or {}
            if r["kind"] != "Member" or "trunnion" not in fr:
                continue
            k = r["key"]
            for tag, grid in (("trunnion_carrier", [fr["trunnion"], fr["carrier"]]), ("lod", [fr["lod"]]),
                              ("comm", [fr.get("comm_before", [None]) + fr["comm_after"]])):
                print(f"[head] sheet {montage(grid, os.path.join(args.preview, f'{k}_{tag}.png'))}")
    print(f"PASS lift_head: {len(reps)} headed builds, every gate held: "
          + ", ".join(f"{r['name']} {r['tris']}" for r in reps))


if __name__ == "__main__":
    try:
        main_cli()
    except Exception:  # noqa: BLE001 - unambiguous exit status for the harness
        import traceback
        traceback.print_exc()
        sys.exit(1)
