"""
cs2_liftdress.py - the shared dressing kit of detail pass 1 (lift stations: DQ base / top,
tram base / top). Build contract: docs/research/art/assets-2026-10/design/detail-pass-1-lifts.md
section 2, as amended by its skeptic (design/detail-pass-1-lifts-skeptic.md MC1-MC11, all
applied). Build report: docs/research/art/assets-2026-10/build/detail-pass-1-lifts-shared.md.

What is here (and what is NOT)
------------------------------
    builders     maze, swing_gate, board, flat_label, stripe, chevrons, carpet, extinguisher,
                 hose_reel, tools, ski_rack, estop_post, flood_mast, fixed_ladder,
                 hatch_frame, bridge, beacon, crane_hook, gauge
                 -> each returns the generators' parts list [(obj, material_key)]
    materials    DRESS_MATERIALS (MaterialSpec field dicts, linear albedo), merge_materials
    painters     paint_board(kind, rect, ppm, base, ctl), board_painter(kind) (a
                 cs2_compose.stamp_on_face painter), BOARDS (the kinds), board_px
    label sheet  pack_label_sheet(items, region_px, gutter_px=6), px_rect_to_uv
    sharing      share_identical(obj, table, whitelist, *, detail_specs, ...)
                 -> the `share=` list of cs2_uv.pack_unique / cs2_bake.bake_unique
    weathering   DQ_MAT_ROLES, TRAM_MAT_ROLES, NEW_ROLES, lift_mat_roles (by material INDEX),
                 station_weather_spec, label_protect_px, weather_budget_ok,
                 highest_clean_step, DRESS_WEATHER_OVERRIDES
    vanilla      SUBOBJ (placeholder GUIDs, verified against the dump), verify_subobj,
                 sub_object_rows, subobj_box, facing_quat
    tables       DRESS_LOD1_DELETE / DRESS_LOD1_KEEP / DRESS_LOD2_DELETE, DRESS_DENSITY,
                 DRESS_CONTACTS (tram C14 kind pairs inside each builder)
    frames       game_to_blender, blender_to_game, tram_point, tram_facing, tram_to_game, rule_t_pos,
                 rule_t_rot
    NOT HERE     no reachability checker (skeptic MC3: one rule = the shipped C15 in
                 tram_station.py and climb_room in lift_terminal_detach.py, house numbers);
                 fixed_ladder only NAMES its parts so those two parsers see them.

Frames
------
Every builder takes points in the BLENDER AUTHORING frame (z up), the frame both lift
generators build in before cs2_asset.orient_for_net: the DQ authoring frame as is
(x travel, y lateral, z up), the tram DESIGN (FBX-raw) frame through tram_point(X, Y, Z) = (Z, X, Y)
(tram_station.P). Facings are "+X" "-X" "+Y" "-Y" (or a plan vector) in that frame;
tram_facing maps a design facing ("+X" -> "+Y", "+Z" -> "+X"). The game local frame of a
sub-object is (gx, gy, gz) = (-y, z, x) of the authoring point on EVERY generator: gx is NEGATED (the game
negates FBX x on import; x-sign gate settled N by the 2026-10-15 playtest, DECISIONS 201 S1b-N, applied
2026-10-16 / T14; analysis-assets.md section 3). The tram's design frame is NOT game: game = tram_to_game(
design) = (-X, Y, Z) (rule_t_pos / rule_t_rot for the rows).

Rules every builder keeps
-------------------------
- parts are cs2_kit objects (bmesh in local space, loc / rot on the object, "UVMap" set),
  named <family>_<name>_..., family prefixes = LOD / weather / density / contact keys;
- nothing below its own z0 (the K1 test asserts it); ground parts are not built here;
- label quads (`label_<n>`): ONE quad, UVMap u = viewer right, v = up, 0..1, normal = the
  facing, LABEL_PROUD in front of their backing (never touching it: no C14 contact);
- material keys are DRESS_MATERIALS keys or the generators' own "galv" / "darksteel" /
  "rubber" (DRESS_MATERIALS carries DQ-identical definitions of those three).

Determinism: no RNG, no clock; ski colors by zlib.crc32. Text: welcome_sign.text_triangles
(Bfont, lazy import, the lift_terminal_detach.digit_triangles path); pictograms: signed
distance primitives of ski_launcher_glyph (its ("line"|"fill"|"disk"|"ring"|"rect") list on a
256 canvas, y down); the resort glyph is ski_launcher_glyph.cand_c. Never imports lodge or
recipes directly (welcome_sign does, lazily, on the first painted word). One exception,
lazily and inside ski_rack only: cs2_bldg, whose ski_gear is THE ski-gear part project-wide
(Richard, 2026-10-11: the stations' racks must match the A-frame's, no bindings).
Headless only for builders / painters (bpy); the tables import without side effects.
"""

import math
import os
import re
import sys
import zipfile
import zlib

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

_HERE = os.path.dirname(os.path.abspath(__file__))
if _HERE not in sys.path:
    sys.path.insert(0, _HERE)
import cs2_asset    # noqa: E402
import cs2_compose  # noqa: E402
import cs2_kit      # noqa: E402
import cs2_preview  # noqa: E402  (_srgb_to_linear, the shared transfer function)
import cs2_uv       # noqa: E402

LABEL_PROUD = 0.003         # label quad in front of its backing / surface (design 2.1)
SRC_MAT_PREFIX = "CS2SRC_"  # = cs2_bake.SRC_MAT_PREFIX (material_sources placeholders)


# ======================================================================================
# Frames
# ======================================================================================

def game_to_blender(p):
    """Game local (gx, gy, gz) -> Blender authoring (x, y, z) = (gz, -gx, gy) (gx NEGATED, x-sign gate N)."""
    return (float(p[2]), -float(p[0]) + 0.0, float(p[1]))


def blender_to_game(p):
    """Blender authoring (x, y, z) -> game local (gx, gy, gz) = (-y, z, x) (gx NEGATED: the game negates FBX
    x on import; x-sign gate settled N, 2026-10-15 playtest). Same map as lift_terminal.m_to_game."""
    return (-float(p[1]) + 0.0, float(p[2]), float(p[0]))


def tram_point(X, Y, Z):
    """tram_station.P: a tram DESIGN-frame point (X lateral, Y up, Z travel = the FBX-raw frame, NOT game) ->
    the Blender authoring point (Z, X, Y). Game = blender_to_game(tram_point(X, Y, Z)) = (-X, Y, Z): see
    tram_to_game."""
    return (float(Z), float(X), float(Y))


def tram_to_game(p):
    """A tram design-frame point (X, Y, Z) -> game local (-X, Y, Z) = blender_to_game(tram_point(*p))."""
    return (-float(p[0]) + 0.0, float(p[1]), float(p[2]))


def rule_t_pos(p):
    """Rule T (x-sign gate N): a point written in the OLD +x frame -> game, x -> -x."""
    return (-float(p[0]) + 0.0, float(p[1]), float(p[2]))


def rule_t_rot(q):
    """Rule T on a quaternion (qx, qy, qz, qw) -> (qx, -qy, -qz, qw): the mirror image of a yaw about +Y."""
    return (float(q[0]) + 0.0, -float(q[1]) + 0.0, -float(q[2]) + 0.0, float(q[3]) + 0.0)


_TRAM_FACING = {"+X": "+Y", "-X": "-Y", "+Z": "+X", "-Z": "-X", "+Y": "+Z", "-Y": "-Z"}


def tram_facing(f):
    """A tram prefab facing ("+X" lateral, "+Z" travel) -> the Blender authoring facing."""
    return _TRAM_FACING[f]


_DIRV = {"+X": (1.0, 0.0, 0.0), "-X": (-1.0, 0.0, 0.0), "+Y": (0.0, 1.0, 0.0),
         "-Y": (0.0, -1.0, 0.0), "+Z": (0.0, 0.0, 1.0), "-Z": (0.0, 0.0, -1.0)}


def _dir3(facing):
    """Unit Vector for a facing string or a (dx, dy[, dz]) tuple."""
    if isinstance(facing, str):
        return Vector(_DIRV[facing.upper() if facing[0] in "+-" else "+" + facing.upper()])
    v = Vector((float(facing[0]), float(facing[1]), float(facing[2]) if len(facing) > 2 else 0.0))
    if v.length < 1e-9:
        raise ValueError(f"zero facing {facing!r}")
    return v.normalized()


def _plan(facing):
    """Horizontal unit normal (n) and the viewer's right (up x n) for a wall facing."""
    n = _dir3(facing)
    n = Vector((n.x, n.y, 0.0))
    if n.length < 1e-9:
        raise ValueError(f"wall facing must be horizontal, got {facing!r}")
    n.normalize()
    right = Vector((-n.y, n.x, 0.0))            # up x n
    return n, right


def _frame_rot(xa, ya, za):
    """Euler for the rotation whose columns are the local axes x, y, z (orthonormal)."""
    m = Matrix((xa, ya, za)).transposed()
    return tuple(m.to_euler())


# ======================================================================================
# Part helpers (cs2_kit objects)
# ======================================================================================

def _obj(name, bm, loc=(0.0, 0.0, 0.0), rot=(0.0, 0.0, 0.0), uv="box"):
    if uv == "box":
        cs2_kit._box_project_uv(bm)
    return cs2_kit._new_obj(name, bm, tuple(loc), tuple(rot), "geo")


def _box(name, size, loc, rot=(0.0, 0.0, 0.0), drop=()):
    """Box of `size` (sx, sy, sz) centered on loc, local axes rotated by `rot`; `drop` deletes
    faces by LOCAL direction keys ("+X" ... "-Z") before the rotation."""
    bm = cs2_kit._box_bm(tuple(float(s) for s in size), tuple(drop))
    return _obj(name, bm, loc, rot)


def _beam(name, p0, p1, w, d, up=(0.0, 0.0, 1.0), drop=()):
    """Box from p0 to p1 (local x along the member), w across, d along `up` (projected);
    `drop` ("+X" end, "-X" end, "-Z" underside ... in the member frame)."""
    a, b = Vector(p0), Vector(p1)
    t = b - a
    L = t.length
    if L < 1e-6:
        raise ValueError(f"{name}: zero-length member")
    t.normalize()
    u = Vector(up) - t * Vector(up).dot(t)
    if u.length < 1e-6:
        alt = Vector((0.0, 1.0, 0.0)) if abs(t.y) < 0.9 else Vector((1.0, 0.0, 0.0))
        u = alt - t * alt.dot(t)
    u.normalize()
    s = u.cross(t).normalized()                  # local y: x cross y = z  ->  y = z x x
    bm = cs2_kit._box_bm((L, float(w), float(d)), tuple(drop))
    return _obj(name, bm, (a + b) / 2.0, _frame_rot(t, s, u))


def _cyl(name, r, p0, p1, segs, caps="none", r2=None):
    """Cylinder (cone with r2) from p0 to p1, fan caps (cs2_kit.add_cyl)."""
    a, b = Vector(p0), Vector(p1)
    d = b - a
    rot = tuple(d.to_track_quat("Z", "Y").to_euler())
    bm = cs2_kit._cyl_bm(float(r), d.length, int(segs), caps, r2)
    return _obj(name, bm, (a + b) / 2.0, rot, uv=None)


def _quad(name, centre, right, up, w, h, uv01=True):
    """One quad centered on `centre` (local origin), u = right, v = up, normal = right x up.
    UVMap 0..1 with u along `right`, v along `up` (the stamp frame, DQ check_sign_uv)."""
    R, U = Vector(right).normalized(), Vector(up).normalized()
    bm = bmesh.new()
    lay = bm.loops.layers.uv.new("UVMap")
    co = [(-0.5, -0.5), (0.5, -0.5), (0.5, 0.5), (-0.5, 0.5)]
    vs = [bm.verts.new(R * (sx * w) + U * (sy * h)) for sx, sy in co]
    f = bm.faces.new(vs)
    for lp, (sx, sy) in zip(f.loops, co):
        lp[lay].uv = (sx + 0.5, sy + 0.5) if uv01 else (sx * w, sy * h)
    bm.normal_update()
    return _obj(name, bm, centre, (0.0, 0.0, 0.0), uv=None)


def _polys(name, polys, normal, loc=(0.0, 0.0, 0.0)):
    """One object from a list of planar polygons (lists of local points), each wound so its
    normal follows `normal`; polygons with > 4 corners are triangulated (no n-gons)."""
    n = Vector(normal)
    bm = bmesh.new()
    vmap = {}

    def v(p):
        k = (round(p[0], 7), round(p[1], 7), round(p[2], 7))
        if k not in vmap:
            vmap[k] = bm.verts.new(k)
        return vmap[k]
    for poly in polys:
        vs = []
        for p in poly:
            x = v(p)
            if x not in vs:
                vs.append(x)
        if len(vs) < 3:
            continue
        f = bm.faces.new(vs)
        f.normal_update()
        if f.normal.dot(n) < 0.0:
            f.normal_flip()
        if len(vs) > 4:
            bmesh.ops.triangulate(bm, faces=[f])
    bm.normal_update()
    return _obj(name, bm, loc, (0.0, 0.0, 0.0))


def _tube_poly(name, pts, r, segs=4):
    """Square / n-gon tube through `pts` with one ring per point (miter-free, open ends)."""
    P = [Vector(p) for p in pts]
    bm = bmesh.new()
    rings = []
    for i, p in enumerate(P):
        if i == 0:
            t = (P[1] - P[0]).normalized()
        elif i == len(P) - 1:
            t = (P[-1] - P[-2]).normalized()
        else:
            t = ((P[i] - P[i - 1]).normalized() + (P[i + 1] - P[i]).normalized()).normalized()
        ref = Vector((0.0, 1.0, 0.0)) if abs(t.y) < 0.9 else Vector((1.0, 0.0, 0.0))
        a = t.cross(ref).normalized()
        b = t.cross(a).normalized()
        rings.append([bm.verts.new(p + (a * math.cos(2 * math.pi * k / segs)
                                        + b * math.sin(2 * math.pi * k / segs)) * r)
                      for k in range(segs)])
    for ra, rb in zip(rings, rings[1:]):
        for k in range(segs):
            k1 = (k + 1) % segs
            bm.faces.new((ra[k], ra[k1], rb[k1], rb[k]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return _obj(name, bm)


# ======================================================================================
# Materials (design 2.1 "New material keys"; linear albedo, MaterialSpec fields)
# ======================================================================================

def _lin(srgb):
    return tuple(float(cs2_preview._srgb_to_linear(np.float32(c))) for c in srgb)


GREY80_LIN = _lin((0.80, 0.80, 0.80))
DRESS_MATERIALS = {
    "rope_orange": dict(albedo=(0.90, 0.35, 0.05), metal=0.0, rough=0.80),
    "flag_red": dict(albedo=(0.75, 0.08, 0.06), metal=0.0, rough=0.70),
    "safety_yellow": dict(albedo=(0.90, 0.70, 0.08), metal=0.0, rough=0.65),
    "safety_black": dict(albedo=(0.05, 0.05, 0.05), metal=0.0, rough=0.70),
    "fire_red": dict(albedo=(0.70, 0.08, 0.06), metal=0.0, rough=0.40),
    # painted over by paint_board in the post-compose hook; slot 0, never tinted
    "label": dict(albedo=GREY80_LIN, metal=0.0, rough=0.50),
    "led": dict(albedo=(0.03, 0.03, 0.03), metal=0.0, rough=0.40, emissive=(1.00, 0.55, 0.10)),
    "lamp_red": dict(albedo=(0.55, 0.10, 0.08), metal=0.0, rough=0.40,
                     emissive=(0.95, 0.12, 0.08)),
    "lamp_green": dict(albedo=(0.10, 0.45, 0.20), metal=0.0, rough=0.40,
                       emissive=(0.10, 0.85, 0.30)),
    "lamp_flood": dict(albedo=(0.85, 0.88, 0.90), metal=0.0, rough=0.40,
                       emissive=(0.90, 0.93, 1.00)),
    # the ski gear's keys = cs2_bldg.BLDG_MATERIALS' (ski_rack delegates to cs2_bldg.ski_gear,
    # 2026-10-11; replaces the old ski_a / ski_b / ski_c): flat paints, rough = 1 - smooth;
    # `accent` is the slot-2 recolor (the station's slot-2 color set), the rest are fixed
    "accent": dict(albedo=GREY80_LIN, metal=0.0, rough=0.75, slot=2, recolor_albedo=True),
    "trim_white": dict(albedo=_lin((0.85, 0.85, 0.85)), metal=0.0, rough=0.60),
    "ski_black": dict(albedo=_lin((0.06, 0.06, 0.065)), metal=0.0, rough=0.45),
    "ski_blue": dict(albedo=_lin((0.09, 0.26, 0.64)), metal=0.0, rough=0.45),
    "ski_red": dict(albedo=_lin((0.72, 0.09, 0.08)), metal=0.0, rough=0.45),
    # the three reused keys, with lift_terminal_detach.MATERIALS' exact definitions (the DQ
    # table has them; the tram table adds them through merge_materials)
    "rubber": dict(albedo=(0.04, 0.04, 0.045), metal=0.0, rough=0.70),
    "darksteel": dict(albedo=tuple(cs2_asset.STEEL_DEFAULT_RGB), metal=0.2, rough=0.60),
    "galv": dict(texture_set="Metal032", metres_per_tile=2.0, metal=1.0, rough=0.65),
}
REUSED_KEYS = ("rubber", "darksteel", "galv")


def _spec_fields(v, key="?"):
    """MaterialSpec or dict -> every MaterialSpec field (defaults filled), comparable."""
    import cs2_bake
    s = cs2_bake._as_material_spec(v, key)
    return {f: (tuple(round(float(c), 6) for c in x) if isinstance(x, (list, tuple)) else x)
            for f, x in ((f, getattr(s, f)) for f in cs2_bake.MaterialSpec.__dataclass_fields__)}


def merge_materials(table, keys):
    """A NEW material table: `table` plus DRESS_MATERIALS[k] for every k in `keys` the table
    lacks (dicts; cs2_bake.material_sources takes dicts and MaterialSpecs alike). A key the
    table already has must carry the same set fields (galv: the texture set + tile size),
    else ValueError - one key, one look on every station."""
    out = dict(table)
    for k in sorted(set(keys)):
        if k not in DRESS_MATERIALS:
            if k in out:
                continue
            raise KeyError(f"merge_materials: {k!r} is neither in the table nor a dress key")
        mine = DRESS_MATERIALS[k]
        if k in out:
            have = _spec_fields(out[k], k)
            want = _spec_fields(mine, k)
            if k == "galv":         # the tram's galv has no metal / rough: its set decides
                have = {f: have.get(f) for f in ("texture_set", "metres_per_tile")}
                want = {f: want.get(f) for f in ("texture_set", "metres_per_tile")}
            if have != want:
                raise ValueError(f"merge_materials: {k!r} differs: table {have} vs dress {want}")
            continue
        out[k] = dict(mine)
    return out


def part_keys(parts):
    """Sorted material keys a parts list uses (the material_sources index order)."""
    return sorted({str(m) for _o, m in parts})


# ======================================================================================
# Builders
# ======================================================================================

def maze(name, runs, z0, h=1.0, post_every=1.40, rope_z=(0.55, 0.95), flag_every=0.45,
         post_r=0.03, *, fine=False):
    """Queue maze (Collins rope-and-flag): `runs` = list of plan polylines [(x, y), ...].
    Posts maze_post_<n>_<i> (4-seg r 0.03, cap top, galv) at every run end and at most
    `post_every` apart; ropes maze_rope_<n>_<r>_<s>_<lo|hi> (0.02 square, rope_orange) post to
    post at z0 + rope_z; flags maze_flag_<n>_<k> (a 0.12 x 0.10 pennant under the upper rope,
    two-sided, flag_red) every `flag_every` along each span, clear of the posts.
    fine=True (fine-detail pass, design B.1, opt-in; False = this geometry vertex for vertex):
    8-seg posts with a top cap (22 tris instead of 10), 6-sided ropes 0.02 across, open ends
    (12 instead of 8); names, materials and the flags unchanged."""
    out = []
    posts = []
    k_post = 0
    k_flag = 0
    for ri, run in enumerate(runs):
        P = [Vector((float(x), float(y), float(z0))) for x, y in run]
        if len(P) < 2:
            raise ValueError(f"maze {name}: run {ri} needs >= 2 points")
        pts = []
        for a, b in zip(P, P[1:]):
            n = max(1, int(math.ceil((b - a).length / post_every - 1e-9)))
            for j in range(n + 1):
                q = a + (b - a) * (j / n)
                if not pts or (q - pts[-1]).length > 1e-6:
                    pts.append(q)
        for q in pts:
            if any((q - s).length < 1e-6 for s in posts):
                continue
            posts.append(q)
            out.append((_cyl(f"maze_post_{name}_{k_post}", post_r, q, q + Vector((0, 0, h)),
                             8 if fine else 4, "top"), "galv"))
            k_post += 1
        for si, (a, b) in enumerate(zip(pts, pts[1:])):
            t = (b - a).normalized()
            for tag, dz in (("lo", rope_z[0]), ("hi", rope_z[1])):
                p0 = a + t * post_r * 0.5 + Vector((0, 0, dz))
                p1 = b - t * post_r * 0.5 + Vector((0, 0, dz))
                if fine:
                    out.append((_cyl(f"maze_rope_{name}_{ri}_{si}_{tag}", 0.01, p0, p1, 6, "none"),
                                "rope_orange"))
                    continue
                out.append((_beam(f"maze_rope_{name}_{ri}_{si}_{tag}", p0, p1, 0.02, 0.02,
                                  drop=("+X", "-X")), "rope_orange"))
            span = (b - a).length
            nf = int(math.floor((span - 2 * post_r - 0.12) / flag_every + 1e-9))
            if nf <= 0:
                continue
            s0 = (span - (nf - 1) * flag_every) / 2.0
            across = Vector((-t.y, t.x, 0.0))
            for j in range(nf):
                c = a + t * (s0 + j * flag_every) + Vector((0, 0, rope_z[1] - 0.01))
                out.append((_pennant(f"maze_flag_{name}_{k_flag}", c, t, across), "flag_red"))
                k_flag += 1
    return out


def _pennant(name, c, t, across, w=0.12, drop=0.10, gap=0.002):
    """Two-sided triangular pennant hanging from c: top edge along t (w), apex `drop` below;
    front face normal +across, back face (gap behind) normal -across. 2 tris."""
    bm = bmesh.new()
    for side, off in ((1.0, gap / 2.0), (-1.0, -gap / 2.0)):
        o = across * off
        vs = [bm.verts.new(t * (-w / 2.0) + o), bm.verts.new(t * (w / 2.0) + o),
              bm.verts.new(Vector((0.0, 0.0, -drop)) + o)]
        f = bm.faces.new(vs)
        f.normal_update()
        if f.normal.dot(across * side) < 0.0:
            f.normal_flip()
    bm.normal_update()
    return _obj(name, bm, c)


def swing_gate(name, hinge, latch, z0, h=0.90, panel=False, tube=0.025, *, fine=False):
    """Swing gate leaf drawn closed between plan points hinge -> latch: stiles gate_<n>_hinge /
    _latch (4-seg r 0.025, galv), bottom rail gate_<n>_bottom (galv), top rail gate_<n>_top
    (safety_yellow), optional infill gate_<n>_panel (galv sheet, z0 + 0.25 .. z0 + h - 0.08,
    for a WAIT HERE label: mount a board(mount="wall") on its face).
    fine=True (design B.1, opt-in): 8-seg capped stiles, 6-sided round rails r 0.02 (open ends);
    the panel unchanged."""
    a = Vector((float(hinge[0]), float(hinge[1]), float(z0)))
    b = Vector((float(latch[0]), float(latch[1]), float(z0)))
    t = (b - a)
    if t.length < 0.2:
        raise ValueError(f"swing_gate {name}: leaf shorter than 0.2 m")
    t.normalize()
    segs = 8 if fine else 4
    out = [(_cyl(f"gate_{name}_hinge", tube, a, a + Vector((0, 0, h)), segs, "top"), "galv"),
           (_cyl(f"gate_{name}_latch", tube, b, b + Vector((0, 0, h)), segs, "top"), "galv")]
    for tag, zz, mat in (("bottom", 0.12, "galv"), ("top", h - 0.03, "safety_yellow")):
        if fine:
            out.append((_cyl(f"gate_{name}_{tag}", 0.02, a + t * tube + Vector((0, 0, zz)),
                             b - t * tube + Vector((0, 0, zz)), 6, "none"), mat))
            continue
        out.append((_beam(f"gate_{name}_{tag}", a + t * tube + Vector((0, 0, zz)),
                          b - t * tube + Vector((0, 0, zz)), 0.04, 0.04, drop=("+X", "-X")), mat))
    if panel:
        za, zb = 0.25, h - 0.08
        c = (a + b) / 2.0 + Vector((0, 0, (za + zb) / 2.0))
        across = Vector((-t.y, t.x, 0.0))
        out.append((_box(f"gate_{name}_panel", ((b - a).length - 2 * tube - 0.01, 0.01, zb - za), c,
                         _frame_rot(t, across, Vector((0, 0, 1))), drop=("+X", "-X")), "galv"))
    return out


def board(name, centre, facing, w, h, mount="wall", post_z0=None, rods_to=None, t=0.02,
          mat="galv", label_mat="label", post_r=0.035, *, fine=False):
    """Sign board. `centre` = center of the LABEL face (what the viewer reads), `facing` its
    outward normal (horizontal). board_<n> backing w x h x t behind it (back face dropped on a
    wall); label_<n> ONE quad LABEL_PROUD in front of the backing (u right, v up); mounts:
    "wall" (the backing's back sits on the wall at center - facing (t + LABEL_PROUD)), "post"
    (board_<n>_post 6-seg r 0.035 from post_z0 to the board top - 0.02, behind the backing),
    "hang" (board_<n>_rod0/1 4-seg r 0.010 from the board top up to rods_to).
    fine=True (design B.1, opt-in): a 12-seg capped post, 6-seg rods; backing / label unchanged."""
    n, right = _plan(facing)
    C = Vector(centre)
    back_c = C - n * (LABEL_PROUD + t / 2.0)
    rot = _frame_rot(n, right, Vector((0, 0, 1)))
    drop = ("-X",) if mount == "wall" else ()
    out = [(_box(f"board_{name}", (t, w, h), back_c, rot, drop=drop), mat),
           (_quad(f"label_{name}", C, right, Vector((0, 0, 1)), w, h), label_mat)]
    if mount == "post":
        if post_z0 is None:
            raise ValueError(f"board {name}: mount 'post' needs post_z0 (the floor z)")
        pc = C - n * (LABEL_PROUD + t + post_r)
        top = C.z + h / 2.0 - 0.02
        out.append((_cyl(f"board_{name}_post", post_r, (pc.x, pc.y, post_z0), (pc.x, pc.y, top),
                         12 if fine else 6, "top"), mat))
    elif mount == "hang":
        if rods_to is None:
            raise ValueError(f"board {name}: mount 'hang' needs rods_to (the soffit z)")
        ztop = C.z + h / 2.0
        if rods_to <= ztop:
            raise ValueError(f"board {name}: rods_to {rods_to} not above the board top {ztop}")
        for k, s in enumerate((-1.0, 1.0)):
            p = back_c + right * (s * (w / 2.0 - 0.06))
            out.append((_cyl(f"board_{name}_rod{k}", 0.010, (p.x, p.y, ztop), (p.x, p.y, rods_to),
                             6 if fine else 4, "none"), mat))
    elif mount != "wall":
        raise ValueError(f"board {name}: mount {mount!r} not in wall / post / hang")
    return out


def flat_label(name, x0, x1, y0, y1, z, read_up=(1.0, 0.0), mat="label"):
    """Floor label: one up-facing quad LABEL_PROUD above z over x0..x1 / y0..y1; `read_up` =
    the plan direction that is the label's "up" (v); u = v turned -90 deg (seen from above)."""
    v = Vector((float(read_up[0]), float(read_up[1]), 0.0)).normalized()
    u = Vector((v.y, -v.x, 0.0))
    if abs(v.x) > 0.5:
        w, hh = (y1 - y0), (x1 - x0)
    else:
        w, hh = (x1 - x0), (y1 - y0)
    c = ((x0 + x1) / 2.0, (y0 + y1) / 2.0, z + LABEL_PROUD)
    return [(_quad(f"label_{name}", c, u, v, w, hh), mat)]


def stripe(name, p0, p1, width, z, mat="safety_yellow", t=0.006):
    """Painted line as geometry (stripes are geometry: flat color, 0 texels): stripe_<n>, a
    box p0 -> p1 (plan) `width` across, from z to z + t, underside dropped."""
    a = Vector((float(p0[0]), float(p0[1]), z + t / 2.0))
    b = Vector((float(p1[0]), float(p1[1]), z + t / 2.0))
    return [(_beam(f"stripe_{name}", a, b, width, t, drop=("-Z",)), mat)]


def _clip_a(poly, a0, a1):
    """Sutherland-Hodgman clip of a 2D polygon [(a, b), ...] to a0 <= a <= a1."""
    def clip(pts, keep, edge):
        out = []
        for i, p in enumerate(pts):
            q = pts[(i + 1) % len(pts)]
            pin, qin = keep(p), keep(q)
            if pin:
                out.append(p)
            if pin != qin:
                s = (edge - p[0]) / (q[0] - p[0])
                out.append((edge, p[1] + s * (q[1] - p[1])))
        return out
    pts = clip(poly, lambda p: p[0] >= a0 - 1e-12, a0)
    if pts:
        pts = clip(pts, lambda p: p[0] <= a1 + 1e-12, a1)
    return pts


def chevrons(name, facing, c, a_rng, b_rng, period=0.30, band=0.15, slant=45.0, t=0.006):
    """Hazard band as geometry on a face: chev_<n>_base (safety_yellow plate t thick, back
    dropped) and chev_<n>_k (all black slanted stripes, ONE part, LABEL_PROUD in front).
    The face: facing "+X"/"-X" -> plane x = c, a = y, b = z; "+Y"/"-Y" -> plane y = c, a = x,
    b = z; "+Z" -> plane z = c (a top), a = x, b = y. Stripes `band` wide every `period`
    along a, slanted `slant` degrees, clipped to the plate."""
    f = facing.upper()
    sgn = 1.0 if f[0] == "+" else -1.0
    ax = f[1]
    a0, a1 = sorted(float(v) for v in a_rng)
    b0, b1 = sorted(float(v) for v in b_rng)

    def P(a, b, off):
        if ax == "X":
            return Vector((c + sgn * off, a, b))
        if ax == "Y":
            return Vector((a, c + sgn * off, b))
        return Vector((a, b, c + sgn * off))
    n = _dir3(f)
    centre = P((a0 + a1) / 2.0, (b0 + b1) / 2.0, t / 2.0)
    # the plate: a box (t thick) with its back face dropped
    if ax == "X":
        size, drop = (t, a1 - a0, b1 - b0), ("-X",) if sgn > 0 else ("+X",)
    elif ax == "Y":
        size, drop = (a1 - a0, t, b1 - b0), ("-Y",) if sgn > 0 else ("+Y",)
    else:
        size, drop = (a1 - a0, b1 - b0, t), ("-Z",) if sgn > 0 else ("+Z",)
    out = [(_box(f"chev_{name}_base", size, centre, drop=drop), "safety_yellow")]
    sh =(b1 - b0) * math.tan(math.radians(slant))
    polys = []
    k0 = int(math.floor((a0 - sh - a0) / period)) - 1
    k1 = int(math.ceil((a1 - a0) / period)) + 1
    off = t + LABEL_PROUD
    loc = P((a0 + a1) / 2.0, (b0 + b1) / 2.0, off)
    for k in range(k0, k1 + 1):
        s = a0 + k * period
        quad = [(s, b0), (s + band, b0), (s + band + sh, b1), (s + sh, b1)]
        cl = _clip_a(quad, a0, a1)
        if len(cl) >= 3:
            area = 0.5 * abs(sum(cl[i][0] * cl[(i + 1) % len(cl)][1]
                                 - cl[(i + 1) % len(cl)][0] * cl[i][1] for i in range(len(cl))))
            if area > 1e-5:
                polys.append([P(a, b, off) - loc for a, b in cl])
    if polys:
        out.append((_polys(f"chev_{name}_k", polys, n, loc), "safety_black"))
    return out


def carpet(name, x0, x1, y0, y1, z0, t=0.03, edge_w=0.05):
    """Loading carpet: carpet_<n> rubber slab (underside dropped) on z0, and two yellow edge
    stripes carpet_<n>_edge0/1 (0.003 thick) along its y0 / y1 edges, on its top."""
    out = [(_box(f"carpet_{name}", (x1 - x0, y1 - y0, t), ((x0 + x1) / 2, (y0 + y1) / 2, z0 + t / 2),
                 drop=("-Z",)), "rubber")]
    for k, (ya, yb) in enumerate(((y0, y0 + edge_w), (y1 - edge_w, y1))):
        out.append((_box(f"carpet_{name}_edge{k}", (x1 - x0, yb - ya, 0.003),
                         ((x0 + x1) / 2, (ya + yb) / 2, z0 + t + 0.0015), drop=("-Z",)),
                    "safety_yellow"))
    return out


def extinguisher(name, mount, facing, z, r=0.08, h=0.45):
    """Wall extinguisher: ext_<n> (8-seg body, fire_red) standing 0.02 off the wall point
    `mount` (x, y) with its bottom at z; ext_<n>_brk wall bracket (darksteel), ext_<n>_head
    valve (safety_black)."""
    n, right = _plan(facing)
    m = Vector((float(mount[0]), float(mount[1]), float(z)))
    c = m + n * (0.02 + r)
    rot = _frame_rot(n, right, Vector((0, 0, 1)))
    return [(_cyl(f"ext_{name}", r, c, c + Vector((0, 0, h)), 8, "top"), "fire_red"),
            (_box(f"ext_{name}_brk", (0.02, 0.10, 0.06), m + n * 0.01 + Vector((0, 0, 0.62 * h)),
                  rot, drop=("-X",)), "darksteel"),
            (_box(f"ext_{name}_head", (0.05, 0.05, 0.06), c + Vector((0, 0, h + 0.03)), rot,
                  drop=("-Z",)), "safety_black")]


def hose_reel(name, mount, facing, z, r=0.30, w=0.18):
    """Fire hose reel on a wall point `mount` (x, y), center height z: hosereel_<n>_plate
    (0.70 square, 0.02, back dropped), hosereel_<n>_drum (8-seg r 0.6 r, axis = facing),
    hosereel_<n>_fl0 (wall side, outer face only) / _fl1 (front, both faces), all fire_red."""
    n, right = _plan(facing)
    m = Vector((float(mount[0]), float(mount[1]), float(z)))
    rot = _frame_rot(n, right, Vector((0, 0, 1)))
    out = [(_box(f"hosereel_{name}_plate", (0.02, 2 * r + 0.10, 2 * r + 0.10), m + n * 0.01, rot,
                 drop=("-X",)), "fire_red")]
    a = m + n * 0.02
    out.append((_cyl(f"hosereel_{name}_drum", 0.6 * r, a + n * 0.01, a + n * (w - 0.01), 8,
                     "none"), "fire_red"))
    out.append((_cyl(f"hosereel_{name}_fl0", r, a, a + n * 0.01, 8, "top"), "fire_red"))
    out.append((_cyl(f"hosereel_{name}_fl1", r, a + n * (w - 0.01), a + n * w, 8, "top"),
                "fire_red"))
    return out


def tools(name, foot, facing, z0, lean_deg=75.0, gap=0.35):
    """Ice scraper and snow shovel leaning back (lean_deg from horizontal) toward the wall
    whose outward normal is `facing`: tool_<n>_scraper (galv blade, handle) and tool_<n>_shovel
    (darksteel), feet on z0 at `foot` (x, y) and `foot` + gap along the wall. Put the foot
    about 1.25 cos(lean) = 0.32 m off the wall so the tops touch it."""
    n, right = _plan(facing)
    out = []
    L = 1.25
    ca, sa = math.cos(math.radians(lean_deg)), math.sin(math.radians(lean_deg))
    axis = (-n * ca + Vector((0, 0, sa))).normalized()
    for k, (tag, bw, bl, mat) in enumerate((("scraper", 0.30, 0.10, "galv"),
                                             ("shovel", 0.25, 0.30, "darksteel"))):
        f = Vector((float(foot[0]), float(foot[1]), float(z0) + 0.01)) + right * (k * gap)
        bm = bmesh.new()
        side = axis.cross(right).normalized()       # the blade's face normal
        for (w_, l_, d_, off) in ((bw, bl, 0.012, bl / 2.0 + 0.0), (0.035, L - bl, 0.035,
                                                                       bl + (L - bl) / 2.0)):
            cen = f + axis * off + side * (d_ / 2.0)
            hx, hy, hz = right * (w_ / 2.0), axis * (l_ / 2.0), side * (d_ / 2.0)
            cs = [cen + sx * hx + sy * hy + sz * hz for sx in (-1, 1) for sy in (-1, 1)
                  for sz in (-1, 1)]
            vs = [bm.verts.new(c - f) for c in cs]
            for q in ((0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4),
                      (1, 5, 7, 3)):
                bm.faces.new([vs[i] for i in q])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        out.append((_obj(f"tool_{name}_{tag}", bm, f), mat))
    return out


# The ONE ski-gear part project-wide (Richard, eleventh look, 2026-10-11: "the skis outside
# the tram building look totally different from the ones on the A frame - the A-frame ones
# way preferred"; ninth look: "I don't think we need to render the bindings"): the rack's
# items are cs2_bldg's gear (a ski PAIR base to base with sidecut and curled tips, a snowboard
# with kick and graphic bands; NO bindings), in cs2_bldg's materials (accent = the slot-2
# recolor + trim_white / ski_black / ski_blue / ski_red). Colors are (body, tip) / (top,
# stripe) pairs picked by zlib.crc32 (no RNG), as the lodges' racks.
GEAR_SKI_COLOURS = (("trim_white", "ski_blue"), ("accent", "ski_black"), ("ski_red", "ski_black"),
                    ("ski_blue", "trim_white"), ("ski_black", "accent"), ("trim_white", "ski_red"))
GEAR_BOARD_COLOURS = (("ski_black", "accent"), ("ski_blue", "trim_white"),
                      ("trim_white", "ski_red"), ("accent", "ski_black"))
GEAR_LEAN_PAD = 0.02        # the rail's half size: the item's back plane touches its outer top edge


def gear_pick(name, tag, kind):
    """Deterministic (color pair, length) of item `tag` of rack `name` (zlib.crc32): skis
    1.62-1.76 m, boards 1.46-1.58 m (the lodges' racks hold 1.46-1.80)."""
    c = zlib.crc32(f"{name}:{tag}".encode("utf-8"))
    if kind == "board":
        return GEAR_BOARD_COLOURS[c % len(GEAR_BOARD_COLOURS)], 1.46 + 0.02 * ((c >> 8) % 7)
    return GEAR_SKI_COLOURS[c % len(GEAR_SKI_COLOURS)], 1.62 + 0.02 * ((c >> 8) % 8)


def _adopt(o, name):
    """A cs2_bldg part (world-space verts, cross-net UV layers) -> a liftdress part (local
    frame at its bounding-box center, box-projected "UVMap", this module's `_obj`); the source
    object and mesh are removed."""
    me = o.data
    bm = bmesh.new()
    bm.from_mesh(me)
    for lay in [l for l in bm.loops.layers.float if l.name in ("netu", "netv")]:
        bm.loops.layers.float.remove(lay)
    for lay in [l for l in bm.faces.layers.int if l.name == "netf"]:
        bm.faces.layers.int.remove(lay)
    pts = [v.co.copy() for v in bm.verts]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    c0 = (lo + hi) * 0.5
    for v in bm.verts:
        v.co -= c0
    bpy.data.objects.remove(o, do_unlink=True)
    bpy.data.meshes.remove(me)
    return _obj(name, bm, c0)


def ski_rack(name, x0, x1, y, z0, pairs=7, boards=2, side=-1.0, h=1.30, stand_off=0.40):
    """Ski rack along x (x0..x1) at y on z0: A-frame legs skirack_<n>_leg0..3 and rails
    skirack_<n>_rail0/1 (galv, top rail at z0 + h), then `pairs` ski pairs and `boards`
    snowboards standing on z0 `stand_off` m out on the `side` (-1: -y), their backs leaning on
    the top rail's outer top edge. The items are cs2_bldg.ski_gear's (ONE call, the lodges'
    gear: no bindings), parts ski_<n>_<code> one per matkey (cs2_bldg.GEAR_CODE: A accent,
    W trim_white, K ski_black, B ski_blue, R ski_red). Neighbors may not overlap across
    (cs2_bldg.GEAR_W): ValueError."""
    import cs2_bldg as K    # lazy: the dressing kit stays importable without the building kit
    out = []
    zt, zl = z0 + h, z0 + 0.35
    for k, x in enumerate((x0 + 0.05, x1 - 0.05)):
        for j, s in enumerate((-1.0, 1.0)):
            a = Vector((x, y + s * 0.30, z0 + 0.015))
            b = Vector((x, y, zt + 0.02))
            out.append((_beam(f"skirack_{name}_leg{2 * k + j}", a, b, 0.04, 0.04,
                              up=(1.0, 0.0, 0.0), drop=("-X",)), "galv"))
    for j, zz in enumerate((zt, zl)):
        out.append((_beam(f"skirack_{name}_rail{j}", (x0 + 0.03, y, zz), (x1 - 0.03, y, zz), 0.04, 0.04,
                          drop=("+X", "-X")), "galv"))
    n_items = pairs + boards
    pitch = (x1 - x0 - 0.30) / max(1, n_items)
    sg = -1.0 if side < 0.0 else 1.0
    zf = max(float(z0), K.GROUND_LIFT) + 0.015      # the kit's foot: 0.035 above the rack base
    kinds = ["ski"] * pairs + ["board"] * boards
    for a_, b_ in zip(kinds, kinds[1:]):
        if 0.5 * (K.GEAR_W[a_] + K.GEAR_W[b_]) > pitch + 1e-9:
            raise ValueError(f"ski_rack {name}: slot pitch {pitch:.3f} m < "
                             f"{0.5 * (K.GEAR_W[a_] + K.GEAR_W[b_]):.3f} m ({a_} next to {b_})")
    items = []
    for i, kind in enumerate(kinds):
        x = x0 + 0.15 + (i + 0.5) * pitch
        tag = f"sb{i - pairs}" if kind == "board" else f"{i}"
        colour, length = gear_pick(name, tag, kind)
        items.append((kind, (x, y + sg * stand_off, zf), (x, y + sg * GEAR_LEAN_PAD, zt + GEAR_LEAN_PAD),
                      colour, length))
    tmp = f"_ld_{name}"
    for o, mat in K.ski_gear(tmp, items):
        code = o.name[len(f"skirack_{tmp}g"):]
        out.append((_adopt(o, f"ski_{name}_{code}"), mat))
    return out


def aframe_rack(name, x, y, skis, z0=0.0, along="Y"):
    """The A-frame KIT rack (cs2_bldg.ski_rack_frame: 8 slots, gear=True, no bindings) - ONE rack look
    project-wide (Richard, eleventh look 2026-10-11: "the A-frame ones way preferred (sans bindings)").
    Center (x, y) in the Blender authoring frame, standing on z0, long axis `along` ("X" | "Y"); `skis` =
    cs2_bldg gear entries (slot, side, kind[, color[, length]]). Parts `skirack_<name>` (the galv frame) and
    `skirack_<name>g<code>` (one per gear matkey), each passed through _adopt like ski_rack's gear (tram rev 2
    skeptic MC5: liftdress-normalized - local frame at its bbox center, box-projected UVMap, no net layers);
    names and tri counts are the kit call's. Added for tram stations revision 2; `ski_rack` is unchanged
    (its other callers switch at their owners' next re-export)."""
    import cs2_bldg as K    # lazy, as in ski_rack
    out = []
    for o, mat in K.ski_rack_frame(name, x, y, slots=8, mat="galv", along=along, skis=skis, ski_mat="accent",
                                   gear=True, z0=z0):
        nm = o.name
        # the cross-net layers go by the attribute API first: _adopt's bmesh pass removes the first of two float
        # loop layers and can leave the second (measured: `netv` survived on two of the parts)
        for key in ("netu", "netv", "netf"):
            a = o.data.attributes.get(key)
            while a is not None:
                o.data.attributes.remove(a)
                a = o.data.attributes.get(key)
        out.append((_adopt(o, nm), mat))
    return out


def estop_post(name, at, z0, h=1.00):
    """Emergency stop post: estop_<n>_post (6-seg r 0.04, galv), estop_<n>_box (yellow
    enclosure on top), estop_<n>_btn (red mushroom button on the box top)."""
    p = Vector((float(at[0]), float(at[1]), float(z0)))
    return [(_cyl(f"estop_{name}_post", 0.04, p, p + Vector((0, 0, h)), 6, "top"), "galv"),
            (_box(f"estop_{name}_box", (0.16, 0.12, 0.16), p + Vector((0, 0, h + 0.08)),
                  drop=("-Z",)), "safety_yellow"),
            (_cyl(f"estop_{name}_btn", 0.035, p + Vector((0, 0, h + 0.16)),
                  p + Vector((0, 0, h + 0.19)), 6, "top"), "fire_red")]


def flood_mast(name, at, z0, aim, h=8.0, heads=2, arm=1.0, tilt_deg=25.0):
    """Floodlight mast: floodbase_<n> plate (galv, underside dropped), flood_<n>_mast (8-seg
    taper r 0.11 -> 0.07, cap top), flood_<n>_arm crossarm (galv) at h - 0.15 across the aim,
    floodbrk_<n>_<k> brackets, lamp_flood<n>_<k> heads 0.40 x 0.25 x 0.15 (lamp_flood) turned
    to `aim` (x, y) and tilted down tilt_deg. Top = z0 + h exactly (the mast cap)."""
    base = Vector((float(at[0]), float(at[1]), float(z0)))
    d = Vector((float(aim[0]) - base.x, float(aim[1]) - base.y, 0.0))
    if d.length < 1e-6:
        raise ValueError(f"flood_mast {name}: aim on the mast")
    d.normalize()
    across = Vector((-d.y, d.x, 0.0))
    out = [(_box(f"floodbase_{name}", (0.40, 0.40, 0.03), base + Vector((0, 0, 0.015)),
                 drop=("-Z",)), "galv"),
           (_cyl(f"flood_{name}_mast", 0.11, base + Vector((0, 0, 0.03)), base + Vector((0, 0, h)),
                 8, "top", r2=0.07), "galv")]
    za = h - 0.15
    out.append((_beam(f"flood_{name}_arm", base + across * (-arm / 2) + Vector((0, 0, za)),
                      base + across * (arm / 2) + Vector((0, 0, za)), 0.08, 0.08), "galv"))
    offs = [0.0] if heads == 1 else list(np.linspace(-arm / 2 + 0.12, arm / 2 - 0.12, heads))
    for k, o in enumerate(offs):
        c = base + across * float(o) + Vector((0, 0, za - 0.04))      # under the arm
        out.append((_box(f"floodbrk_{name}_{k}", (0.04, 0.04, 0.12), c + Vector((0, 0, -0.06)),
                         drop=("+Z",)), "galv"))
        hc = c + Vector((0, 0, -0.12 - 0.06)) + d * 0.06
        aim3 = (Vector((float(aim[0]), float(aim[1]), float(z0))) - hc).normalized()
        yaw = math.atan2(aim3.y, aim3.x)
        rot = (0.0, math.radians(tilt_deg), yaw)
        out.append((_box(f"lamp_flood{name}_{k}", (0.15, 0.40, 0.25), hc, rot), "lamp_flood"))
    return out


LADDER_STYLES = ("dq", "tram")


def fixed_ladder(name, line, climb, rung_z, rail_z, width=0.48, style="dq", standoffs=()):
    """Fixed ladder. `line` = (x, y) of the rung centerline's middle; `climb` = the plan unit
    direction from the rungs toward the climber (the climbing side); rungs across it at the
    heights `rung_z`; rails from rail_z[0] to rail_z[1], `width` apart (centers).
    style "dq"   -> lrail_<n>0/1 (r 0.03, 6-seg, cap top) + lrung_<n>NN (r 0.015, 4-seg, 01..),
                    standoffs lstand_<n><k>: lift_terminal_detach._ladder's parts exactly
                    (climb_room / C7 / C8 read these prefixes);
    style "tram" -> ladder_<n>_a/b (0.05 square) + lrung_<n>_<k> (r 0.018, 3-seg, 0..),
                    standoffs ladder_<n>_s<k>: tram_station.build_pcat_ladder's names, which
                    C15 walk_ladders parses (`lrung_<tag>_<k>` + `ladder_<tag>_*`).
    `standoffs` = [(z, depth), ...]: a 0.04 bar per rail from the rail back (-climb) `depth` m.
    Reachability itself is NOT judged here (skeptic MC3): the generator's C15 / climb_room."""
    if style not in LADDER_STYLES:
        raise ValueError(f"fixed_ladder {name}: style {style!r} not in {LADDER_STYLES}")
    c = Vector((float(climb[0]), float(climb[1]), 0.0))
    if c.length < 1e-9:
        raise ValueError(f"fixed_ladder {name}: zero climb direction")
    c.normalize()
    across = Vector((-c.y, c.x, 0.0))
    L = Vector((float(line[0]), float(line[1]), 0.0))
    z0, z1 = float(rail_z[0]), float(rail_z[1])
    rungs = [float(z) for z in rung_z]
    if any(not (z0 < z < z1) for z in rungs):
        raise ValueError(f"fixed_ladder {name}: rungs {rungs} outside the rails {z0}..{z1}")
    out = []
    half = width / 2.0
    for k, s in enumerate((-1.0, 1.0)):
        p = L + across * (s * half)
        if style == "dq":
            out.append((cs2_kit.add_cyl(f"lrail_{name}{k}", 0.03, z1 - z0, (p.x, p.y, (z0 + z1) / 2.0),
                                        segments=6, caps="top"), "galv"))
        else:
            out.append((_box(f"ladder_{name}_{'ab'[k]}", (0.05, 0.05, z1 - z0),
                             (p.x, p.y, (z0 + z1) / 2.0)), "galv"))
    for i, z in enumerate(rungs):
        a, b = L - across * half, L + across * half
        if style == "dq":
            # lift_terminal_detach._ladder / _ladder_x exactly: length 2 half - 0.02, centered,
            # the same euler (so the 4-gon's corners land where the generator's do)
            mid = (L.x, L.y, z)
            if abs(across.y) > 0.999:
                rot = (math.pi / 2.0, 0.0, 0.0)
            elif abs(across.x) > 0.999:
                rot = (0.0, math.pi / 2.0, 0.0)
            else:
                rot = tuple(across.to_track_quat("Z", "Y").to_euler())
            out.append((cs2_kit.add_cyl(f"lrung_{name}{i + 1:02d}", 0.015, width - 0.02, mid, rot=rot,
                                        segments=4, caps="none"), "galv"))
        else:
            out.append((_cyl(f"lrung_{name}_{i}", 0.018, (a.x, a.y, z), (b.x, b.y, z), 3, "none"),
                        "galv"))
    for j, (z, depth) in enumerate(standoffs):
        for k, s in enumerate((-1.0, 1.0)):
            p = L + across * (s * half)
            a = Vector((p.x, p.y, float(z))) - c * 0.03
            b = a - c * float(depth)
            nm = f"lstand_{name}{2 * j + k}" if style == "dq" else f"ladder_{name}_s{2 * j + k}"
            out.append((_beam(nm, a, b, 0.04, 0.04, drop=("-X",)), "galv"))
    return out


def hatch_frame(name, centre, size=0.80, frame_w=0.06, frame_d=0.04):
    """Roof / soffit hatch seen from below, on a DOWN-facing surface at center z: the ring
    hatch_<n>_frame (frame_w wide, frame_d deep, its top against the surface, no top face)
    and hatch_<n>_leaf (darksteel, 0.01 thick, recessed 0.005 below the surface)."""
    cx, cy, z = (float(v) for v in centre)
    o, i_ = size + 2 * frame_w, size
    bm = cs2_kit._ring_bm((o, o), (i_, i_), -frame_d, 0.0, lambda u, v, d: (u, v, d),
                          faces=("outer", "inner", "bottom"))
    out = [(_obj(f"hatch_{name}_frame", bm, (cx, cy, z), uv=None), "galv")]
    out.append((_box(f"hatch_{name}_leaf", (size - 0.01, size - 0.01, 0.01),
                     (cx, cy, z - 0.005 - 0.005), drop=("+Z",)), "darksteel"))
    return out


def bridge(name, x0, x1, y0, y1, z, rails=(), t=0.06, beam_d=0.20, post_every=1.5):
    """Grated bridge at deck level z (top): grate_<n> + grate_<n>_lip (cs2_kit.grating, galv;
    a WALK prefix for the tram C15), bridgebeam_<n>0/1 under its two long edges, and the
    kit handrail on every plan polyline in `rails` (names post_<n><k>_*, rail_<n><k>_top/knee,
    kick_<n><k>_*)."""
    lx, ly = x1 - x0, y1 - y0
    out = [(o, "galv") for o, _q in cs2_kit.grating(name, lx, ly, t=t,
                                                     loc=((x0 + x1) / 2, (y0 + y1) / 2, z - t / 2))]
    along_x = lx >= ly
    for k in range(2):
        if along_x:
            yy = (y0 + 0.05) if k == 0 else (y1 - 0.05)
            a, b = (x0, yy, z - t - beam_d / 2), (x1, yy, z - t - beam_d / 2)
        else:
            xx = (x0 + 0.05) if k == 0 else (x1 - 0.05)
            a, b = (xx, y0, z - t - beam_d / 2), (xx, y1, z - t - beam_d / 2)
        out.append((_beam(f"bridgebeam_{name}{k}", a, b, 0.10, beam_d, drop=("+Z",)), "galv"))
    for k, path in enumerate(rails):
        pts = [(float(x), float(y), float(z)) for x, y in path]
        out += [(o, "galv") for o, _q in cs2_kit.handrail(f"{name}{k}", pts, h=1.10, knee=0.55,
                                                          kick=0.10, post_every=post_every,
                                                          segs=6, radius=0.028)]
    return out


def beacon(name, centre, r=0.10, h=0.20, mat="lamp_red"):
    """Obstruction / warning beacon lens: lamp_<n> (8-seg, both caps), base center (x, y, z)."""
    c = Vector(tuple(float(v) for v in centre))
    return [(_cyl(f"lamp_{name}", r, c, c + Vector((0, 0, h)), 8, "both"), mat)]


def crane_hook(name, lug, drop=1.20):
    """Hook block on a chain under a crane lug point (x, y, z = the lug's underside):
    hook_<n>_chain (4-seg r 0.012), hook_<n>_block (0.16 x 0.08 x 0.22), hook_<n>_hook (a J of
    a 4-sided tube), all darksteel. The hook's tip is `drop` below the lug."""
    p = Vector(tuple(float(v) for v in lug))
    zb = p.z - (drop - 0.36)                    # block top
    out = [(_cyl(f"hook_{name}_chain", 0.012, p, (p.x, p.y, zb), 4, "none"), "darksteel"),
           (_box(f"hook_{name}_block", (0.16, 0.08, 0.22), (p.x, p.y, zb - 0.11)), "darksteel")]
    zh = zb - 0.22
    pts = [(p.x, p.y, zh), (p.x, p.y, zh - 0.10), (p.x + 0.035, p.y, zh - 0.14),
           (p.x + 0.07, p.y, zh - 0.08)]
    out.append((_tube_poly(f"hook_{name}_hook", pts, 0.016, 4), "darksteel"))
    return out


def gauge(name, mount, facing, z, size=0.30, needle_deg=20.0):
    """Wall gauge: gauge_<n>_case (darksteel, size + 0.02 square, 0.04 deep, back dropped),
    label_<n> dial quad LABEL_PROUD in front of it (paint_board kind "tension" draws the
    scale), gauge_<n>_ptr (darksteel needle, 0.42 size, LABEL_PROUD in front of the dial),
    rotated needle_deg clockwise from straight up (the painted green band is centered on 0)."""
    n, right = _plan(facing)
    m = Vector((float(mount[0]), float(mount[1]), float(z)))
    rot = _frame_rot(n, right, Vector((0, 0, 1)))
    depth = 0.04
    out = [(_box(f"gauge_{name}_case", (depth, size + 0.02, size + 0.02), m + n * (depth / 2), rot,
                 drop=("-X",)), "darksteel")]
    dial_c = m + n * (depth + LABEL_PROUD)
    out.append((_quad(f"label_{name}", dial_c, right, Vector((0, 0, 1)), size, size), "label"))
    a = math.radians(needle_deg)
    up_r = Vector((0, 0, 1)) * math.cos(a) + right * math.sin(a)
    L = 0.42 * size
    p0 = dial_c + n * (LABEL_PROUD + 0.002) - up_r * 0.02
    p1 = p0 + up_r * (L + 0.02)
    out.append((_beam(f"gauge_{name}_ptr", p0, p1, 0.012, 0.004, up=tuple(n), drop=("-Z",)),
                "darksteel"))
    return out


# What each builder may produce: family prefixes (K1 asserts every part matches one).
BUILDER_PREFIXES = {
    "maze": ("maze_post_", "maze_rope_", "maze_flag_"),
    "swing_gate": ("gate_",),
    "board": ("board_", "label_"),
    "flat_label": ("label_",),
    "stripe": ("stripe_",),
    "chevrons": ("chev_",),
    "carpet": ("carpet_",),
    "extinguisher": ("ext_",),
    "hose_reel": ("hosereel_",),
    "tools": ("tool_",),
    "ski_rack": ("skirack_", "ski_"),
    "aframe_rack": ("skirack_",),
    "estop_post": ("estop_",),
    "flood_mast": ("floodbase_", "flood_", "floodbrk_", "lamp_flood"),
    "fixed_ladder": ("lrail_", "lrung_", "lstand_", "ladder_"),
    "hatch_frame": ("hatch_",),
    "bridge": ("grate_", "bridgebeam_", "post_", "rail_", "kick_"),
    "beacon": ("lamp_",),
    "crane_hook": ("hook_",),
    "gauge": ("gauge_", "label_"),
}

# LOD policy (design DL8 / rule 1.5): the new dressing goes at LOD1 except the mast shafts +
# arms, bridge gratings + beams and ladder rails (they stay to LOD1); nothing new at LOD2.
DRESS_LOD1_KEEP = ("flood_", "grate_", "bridgebeam_", "lrail_", "ladder_")
DRESS_LOD1_DELETE = ("maze_", "gate_", "board_", "label_", "stripe_", "chev_", "carpet_", "ext_",
                     "hosereel_", "tool_", "skirack_", "ski_", "estop_", "floodbase_",
                     "floodbrk_", "lamp_", "lrung_", "lstand_", "hatch_", "hook_", "gauge_",
                     "post_", "rail_", "kick_")
DRESS_LOD2_DELETE = DRESS_LOD1_DELETE + DRESS_LOD1_KEEP
# NOTE "flood_" prefixes nothing deleted at LOD1 ("floodbase_" / "floodbrk_" are their own
# families on purpose); tram ladders (ladder_) are LOD1-deleted by the tram's own
# LOD1_DELETE today (pcat) - keeping them to LOD1 is the tram group's call. Do NOT merge the
# family tuples above into a generator's lists blindly ("grate_", "post_", "rail_" also name
# station parts): use lod_split, which returns the dressing's own part NAMES.


def lod_split(parts):
    """{"lod1_delete": names, "lod1_keep": names} for a dressing parts list (DL8): the EXACT
    part names to append to a generator's LOD1_DELETE (lod1_delete) and LOD2_DELETE (both).
    A name that matches neither policy raises (every builder family is classified)."""
    dele, keep = [], []
    for o, _m in parts:
        nm = o.name
        # longest matching family decides ("floodbase_" before "flood_", "lrail_" before ...)
        best = None
        for pfx, pol in [(p, "keep") for p in DRESS_LOD1_KEEP] + [(p, "del") for p in DRESS_LOD1_DELETE]:
            if nm.startswith(pfx) and (best is None or len(pfx) > len(best[0])):
                best = (pfx, pol)
        if best is None:
            raise KeyError(f"lod_split: {nm!r} has no LOD policy")
        (keep if best[1] == "keep" else dele).append(nm)
    return {"lod1_delete": tuple(sorted(dele)), "lod1_keep": tuple(sorted(keep))}

# Density weights for the new families (design 3.7; generators merge what they use).
DRESS_DENSITY = {"lamp_": 1.0, "board_": 0.6, "maze_": 0.5, "carpet_": 0.6, "skirack_": 0.8,
                 "ski_": 0.8, "flood_": 0.6, "floodbase_": 0.6, "floodbrk_": 0.6,
                 "hosereel_": 1.0, "ext_": 1.0, "estop_": 1.0, "tool_": 0.6, "stripe_": 0.6,
                 "chev_": 0.6, "gate_": 0.7, "hatch_": 0.6, "hook_": 0.6, "gauge_": 1.0,
                 "bridgebeam_": 0.6}

# Tram C14 (kind = leading letters, tram_station.part_kind): the kind pairs that touch INSIDE
# one builder's output (measured by tests/test_liftdress.py K1 with BVH overlap), with the
# reason. The station-side pairs (a board on the hut, a ladder on a deck) are the generator
# groups' declarations (skeptic MC8).
DRESS_CONTACTS = {
    "queue maze kit (ropes into their posts)": "maze:maze",
    "swing gate kit (rails into their stiles)": "gate:gate",
    "sign board kit (post / rods into the backing)": "board:board",
    "extinguisher kit (body, bracket, valve)": "ext:ext",
    "hose reel kit (plate, drum, flanges)": "hosereel:hosereel",
    "ski rack kit (legs, rails, gear on the top rail; two matkeys share tip / stripe edges)":
        "skirack:skirack ski:skirack ski:ski",
    "e-stop post kit (post, box, button)": "estop:estop",
    "floodlight mast kit (base, mast, arm, brackets, heads)":
        "flood:floodbase flood:flood flood:floodbrk floodbrk:lamp",
    "fixed ladder kit (rungs into rails, standoffs)": "lrail:lrung lrail:lstand ladder:lrung "
                                                      "ladder:ladder",
    "hatch kit (leaf in its frame)": "hatch:hatch",
    "bridge kit (grating + lip, beams, railing posts on the grating)":
        "grate:grate bridgebeam:grate grate:post kick:post post:rail rail:rail kick:kick",
    "crane hook kit (chain, block, hook)": "hook:hook",
    "carpet kit (edge stripes on the slab)": "carpet:carpet",
    "tool pair (the two tools lean side by side)": "tool:tool",
    "gauge kit (needle in front of the case)": "gauge:gauge",
}
DRESS_CONTACT_KINDS = {tuple(sorted(tok.split(":"))): why for why, toks in DRESS_CONTACTS.items()
                       for tok in toks.split()}


# ======================================================================================
# Painters (design 2.1 "Painters", 2.2 legibility). 2026-10-12 lift-board redraw (Richard,
# twenty-second look: "the guy is getting his HEAD SLAMMED BY THE BAR ... the SKIS LOOK LIKE THEY
# HAVE BLOCKS ON THEM ... trying to avoid AI slop"; build/lift-boards-redraw.md): every lift board
# is drawn in the REAL North-American signage style of docs/research/art/assets-2026-10/ref-signs/
# - yellow placards with a black inset border, a black circle pictogram and black bold caps,
# blue boards with white border / words / pictogram, two bolt dots - from shared vector glyph
# primitives (g_* below, 256 canvas, y down) so every station draws the same rider, chair, bar,
# ski, arrow. The lift boards keep their size class, mount and 400 px/m rects (geometry and UVs
# are untouched; only the paint changes). The words are bold vector caps (_btext_*: Bfont through
# a FONT curve outline offset, the "vector Bfont path"); the regular _text_* helpers below stay
# byte-identical for cs2_signset / fine_probes.
# ======================================================================================

INK = {
    "white": _lin((0.95, 0.95, 0.93)), "black": _lin((0.06, 0.06, 0.07)),
    "blue": _lin((0.00, 0.33, 0.62)), "navy": _lin((0.08, 0.16, 0.32)),
    "yellow": _lin((0.98, 0.78, 0.05)), "orange": _lin((0.98, 0.45, 0.04)),
    "red": _lin((0.80, 0.10, 0.10)), "green": _lin((0.10, 0.60, 0.25)),
    "grey": _lin((0.55, 0.55, 0.55)),
    "amber": (1.00, 0.55, 0.10), "amber_off": (0.06, 0.035, 0.012), "led_bg": (0.012, 0.012, 0.012),
    # 2026-10-12 redraw: the backer plate a placard is bolted to (painted dark, ref 04's black
    # pole / ref 03's posts), its rim, the bolt heads
    "backer": _lin((0.16, 0.16, 0.17)), "backer_rim": _lin((0.60, 0.61, 0.62)),
    "bolt": _lin((0.78, 0.78, 0.76)), "bolt_rim": _lin((0.35, 0.35, 0.35)),
}

MARGIN_M = 0.025            # board edge to its content (the dial / LED layouts)
CAP_T2 = 0.07               # T2 close-up word cap (design L3)
PICTO_T1 = 0.40             # T1 50 m read: primary pictogram height (design L3)
STROKE_T2_PX = 4.0          # T2 stroke floor in texels (design L3)
CONDENSE_MIN = 0.80         # horizontal text condense floor (sign lettering); stems narrow
TEXT_SS = 8                 # point samples per texel axis for words (the DQ digit path)
SDF_SS = 4                  # samples per texel axis for pictograms / the glyph
DOT_ON = 0.30               # LED dot lit when the words cover this share of its cell (the
                            # Bfont stem is ~1 dot pitch wide at 7 rows: 0.5 dropped strokes)
# Bfont's "1" is a bare stem (width = the "I"'s): it would read as I / l on a seat plate, so
# the digit layout draws a flagged 1 (256 canvas, stem 176 units = the cap height)
ONE_PRIMS = [("line", [(140, 40), (140, 216)], 30), ("line", [(140, 44), (94, 84)], 26)]
# 2026-10-12 redraw: sign lettering weight. The FONT curve's outline offset (font units at size 1;
# Bfont's cap is 0.682 there) turns Bfont's regular stem (0.144 cap) into a sign-bold one
# (~0.20 cap, Highway-Gothic-like); the character spacing gives back the gap the offset eats.
SIGN_WEIGHT = 0.024
SIGN_TRACK = 1.06
SIGN_LINE_GAP = 0.30        # line gap / cap
# sign-bold words may condense further than CONDENSE_MIN (Bfont regular): the bold stem keeps
# >= STROKE_T2_PX texels at a 0.07 m cap down to ~0.71 (Highway Gothic C is as narrow)
SIGN_CONDENSE_MIN = 0.72
# placard (ref 03 / 04 / 05): 2:3 portrait, inset border, circle pictogram, bolts top / bottom
PLACARD_RATIO = 1.45        # height / width (12 x 18 in is 1.5; 1.45 lets UNLOAD hold a 0.07 m cap at 4 texel stems)
PLACARD_EDGE = 0.035        # edge -> border line, x placard width
PLACARD_LINE = 0.032        # border line width, x placard width (>= STROKE_T2_PX * 1.1 texels)
PLACARD_RADIUS = 0.06       # corner radius, x placard width
PLACARD_BAND = 0.10         # bolt band inside the border (top and bottom), x placard width
PLACARD_D_MAX = 0.80        # circle diameter cap, x placard width
PLACARD_D_MIN = 0.55        # the words shrink before the circle drops below this
PLATE_MARGIN_M = 0.010      # backer plate visible above / below a placard on a plate board
BOLT_D_M = 0.012            # bolt head (4.8 texels at 400 px/m)
RING_FRAC = 0.045           # pictogram ring stroke, x circle diameter (floored at 4.4 texels)
T1_READ_M_PER_M = 50.0 / PICTO_T1   # design L3's screen basis: a D m pictogram reads to 125 x D m


def _xf(prims, s=1.0, dx=0.0, dy=0.0, cx=128.0, cy=128.0):
    """Scale a ski_launcher_glyph primitive list about (cx, cy) by s, then shift."""
    def P(p):
        return (cx + (p[0] - cx) * s + dx, cy + (p[1] - cy) * s + dy)
    out = []
    for p in prims:
        k = p[0]
        if k == "line":
            out.append(("line", [P(q) for q in p[1]], p[2] * s))
        elif k == "fill":
            out.append(("fill", [P(q) for q in p[1]]))
        elif k == "disk":
            x, y = P((p[1], p[2]))
            out.append(("disk", x, y, p[3] * s))
        elif k == "ring":
            x, y = P((p[1], p[2]))
            out.append(("ring", x, y, p[3] * s, p[4] * s))
        elif k == "rect":
            x0, y0 = P((p[1], p[2]))
            x1, y1 = P((p[3], p[4]))
            out.append(("rect", x0, y0, x1, y1, p[5] * s))
        else:
            raise ValueError(k)
    return out


# --------------------------------------------------------------------------------------
# Glyph primitives (2026-10-12). ski_launcher_glyph prim tuples on the 256 canvas, y DOWN; a
# pictogram is drawn into its circle's bounding square. Strokes >= 11 units = 4.1 texels in a
# 0.235 m circle at 400 px/m (the smallest shipped one). Shared by every station's boards.
# --------------------------------------------------------------------------------------

def g_arc(cx, cy, r, a0, a1, n=12):
    """Points on a circle arc; degrees, 0 = +x, positive toward +y (DOWN on the canvas)."""
    return [(cx + r * math.cos(math.radians(a0 + (a1 - a0) * i / n)),
             cy + r * math.sin(math.radians(a0 + (a1 - a0) * i / n))) for i in range(n + 1)]


def _rot(pts, cx, cy, deg):
    a = math.radians(deg)
    return [(cx + (x - cx) * math.cos(a) - (y - cy) * math.sin(a),
             cy + (x - cx) * math.sin(a) + (y - cy) * math.cos(a)) for x, y in pts]


def g_arrow(x0, y0, x1, y1, w=16.0, head=30.0, dashed=False, dash=18.0, gap=12.0):
    """Arrow from (x0, y0) to its tip (x1, y1): a filled head and a solid shaft, or square dashes
    (PREPARE TO UNLOAD, ref 04)."""
    L = math.hypot(x1 - x0, y1 - y0)
    ux, uy = (x1 - x0) / L, (y1 - y0) / L
    px, py = -uy, ux
    hl = head * 1.15
    bx, by = x1 - ux * hl, y1 - uy * hl
    out = [("fill", [(x1, y1), (bx + px * head, by + py * head), (bx - px * head, by - py * head)])]
    if not dashed:
        out.append(("line", [(x0, y0), (bx + ux * 2.0, by + uy * 2.0)], w))
        return out
    if abs(ux) > 1e-9:
        raise ValueError("g_arrow: dashed arrows are vertical")
    t, sl = 0.0, L - hl - 4.0
    while t < sl - 1e-6:
        t1 = min(t + dash, sl)
        out.append(("rect", x0 - w / 2.0, min(y0 + uy * t, y0 + uy * t1), x0 + w / 2.0,
                    max(y0 + uy * t, y0 + uy * t1), 1.0))
        t = t1 + gap
    return out


def g_double_arrow(x, y0, y1, w=8.0, head=12.0):
    """Vertical double arrow between y0 and y1 (the seat-height gauge)."""
    m = (y0 + y1) / 2.0
    return g_arrow(x, m, x, y0, w, head) + g_arrow(x, m, x, y1, w, head)


def g_dashes(x0, y0, x1, y1, w, dash, gap):
    """A dashed straight line (capsule dashes)."""
    L = math.hypot(x1 - x0, y1 - y0)
    ux, uy = (x1 - x0) / L, (y1 - y0) / L
    out, t = [], 0.0
    while t < L - 1e-6:
        t1 = min(t + dash, L)
        out.append(("line", [(x0 + ux * t, y0 + uy * t), (x0 + ux * t1, y0 + uy * t1)], w))
        t = t1 + gap
    return out


def g_seated(hx, hy):
    """Seated rider, side view facing +x (ISO pictogram proportions), hip at (hx, hy): head (a
    clear gap above the shoulders), torso, thigh, shin."""
    return [("disk", hx + 9.0, hy - 63.0, 15.0),
            ("line", [(hx + 5.0, hy - 36.0), (hx, hy)], 22.0),
            ("line", [(hx, hy), (hx + 44.0, hy)], 19.0),
            ("line", [(hx + 44.0, hy), (hx + 47.0, hy + 38.0)], 16.0)]


def g_chair(hx, hy, top_y):
    """The chair under a g_seated rider: the hanger straight up the back post to `top_y` (the rope
    or the ring), the back behind the torso, a rounded corner, the seat under the thigh."""
    bx, sy = hx - 26.0, hy + 21.0
    return [("line", [(bx, top_y), (bx, sy - 8.0), (bx + 8.0, sy), (hx + 50.0, sy)], 12.0)]


def g_bar(hx, hy, state):
    """Restraint bar, side view: a bent tube from its pivot on the hanger - forward ABOVE the head,
    a round bend, the down-tube ahead of the face, the cross-bar across the CHEST (never through the
    head). 'down' = lowered (ride position): kit color-variant fleet 2026-10-12, the checker's nit "the
    LOWER BAR bracket sits tight around the head" - a LOOSER hoop: pivot 8 units higher, bend radius 20,
    down-tube 6 units further forward = 20 canvas units over the head top (was 12), 24 ahead of the face
    (was 18); the cross-bar end is unchanged (chest height, 6 off the torso). 'up' = the original tube
    (pivot hy - 96, bend 18) swung up about its pivot (RAISE BAR, unchanged)."""
    px, py = hx - 26.0, hy - 96.0
    rb = 18.0
    if state == "down":
        pyd, rbd, xa = hy - 104.0, 20.0, hx + 34.0
        pts = [(px, pyd)] + g_arc(xa, pyd + rbd, rbd, -90.0, 0.0, 8) + [(xa + rbd, hy - 26.0), (hx + 28.0, hy - 26.0)]
        return [("line", pts, 12.0)]
    # raised: the same tube folded up about the pivot - a short arm up beside the hanger, the down-tube
    # now running forward over the head, the cross-bar hanging from its end
    xa = px + 30.0
    pts = [(px, py)] + g_arc(xa, py + rb, rb, -90.0, 0.0, 8) + [(xa + rb, py + 82.0), (xa + rb - 22.0, py + 82.0)]
    return [("line", _rot(pts, px, py, -78.0), 12.0)]


def g_ski(x0, x1, y, w=12.0, tip=20.0):
    """A ski: a thin straight line whose front (+x) end curves UP (a quarter arc) - no blocks."""
    return [("line", [(x0, y), (x1 - tip, y)] + g_arc(x1 - tip, y - tip, tip, 90.0, 0.0, 8)[1:], w)]


def g_rope(x0, y0, x1, y1, w=12.0):
    return [("line", [(x0, y0), (x1, y1)], w)]


def g_grip(x, y):
    """The grip clamped on the rope at (x, y)."""
    return [("rect", x - 9.0, y - 9.0, x + 9.0, y + 7.0, 3.0)]


def g_falling(cx=128.0, cy=128.0):
    """A falling figure (ref 04): pitched forward, one arm flung back (where the snag is), legs
    trailing."""
    return [("disk", cx + 40.0, cy - 40.0, 15.0),
            ("line", [(cx + 20.0, cy - 18.0), (cx - 14.0, cy + 8.0)], 21.0),
            ("line", [(cx + 16.0, cy - 16.0), (cx - 12.0, cy - 34.0), (cx - 40.0, cy - 30.0)], 12.0),
            ("line", [(cx + 22.0, cy - 12.0), (cx + 46.0, cy + 6.0), (cx + 58.0, cy + 30.0)], 12.0),
            ("line", [(cx - 14.0, cy + 8.0), (cx + 14.0, cy + 34.0), (cx + 6.0, cy + 62.0)], 16.0),
            ("line", [(cx - 14.0, cy + 8.0), (cx - 34.0, cy + 40.0), (cx - 14.0, cy + 60.0)], 16.0)]


def g_x(cx, cy, s=21.0, w=12.0):
    """The red X (the snag) of CHECK FOR LOOSE CLOTHING."""
    return [("line", [(cx - s, cy - s), (cx + s, cy + s)], w), ("line", [(cx - s, cy + s), (cx + s, cy - s)], w)]


def g_pole():
    """REMOVE POLE STRAPS (ref 03): the glove at the left, the strap pulled off the wrist (dashes
    + an arrow head at the grip), the pole at the right (grip, shaft, basket)."""
    pole = [("line", [(176.0, 44.0), (173.0, 84.0)], 22.0),
            ("line", [(173.0, 84.0), (158.0, 214.0)], 12.0),
            ("line", [(138.0, 192.0), (180.0, 197.0)], 12.0)]
    glove = [("rect", 36.0, 92.0, 90.0, 132.0, 18.0),
             ("line", [(70.0, 96.0), (60.0, 76.0)], 15.0),
             ("rect", 86.0, 96.0, 106.0, 128.0, 4.0)]
    strap = g_dashes(112.0, 110.0, 146.0, 82.0, 12.0, 9.0, 8.0) + g_arrow(146.0, 82.0, 161.0, 70.0, w=12.0, head=13.0)
    return pole + glove + strap


def g_footprint(cx, top, tilt=0.0, L=128.0):
    """One shoe print, toe up: ONE sole silhouette (round toe, the ball widest, a narrow waist, the
    heel) tilted `tilt` deg about its center - a split heel reads as '!'."""
    def prof(t):
        if t < 0.20:
            return 25.0 * math.sqrt(max(0.0, 1.0 - ((0.20 - t) / 0.20) ** 2))
        if t < 0.40:
            return 25.0
        if t < 0.62:
            return 25.0 - 11.0 * (t - 0.40) / 0.22
        if t < 0.80:
            return 14.0 + 4.0 * (t - 0.62) / 0.18
        return 18.0 * math.sqrt(max(0.0, 1.0 - ((t - 0.80) / 0.20) ** 2))
    ts = [i / 40.0 for i in range(41)]
    pts = [(cx - prof(t), top + t * L) for t in ts] + [(cx + prof(t), top + t * L) for t in reversed(ts)]
    return [("fill", _rot(pts, cx, top + L / 2.0, tilt))]


def g_feet():
    """WAIT HERE: a stop line ahead of two shoe prints."""
    return [("line", [(48.0, 50.0), (208.0, 50.0)], 16.0)] + g_footprint(98.0, 76.0, -7.0) + g_footprint(160.0, 76.0, 7.0)


def g_seat_height():
    """LOADING SEAT HEIGHT (ref 03): rope, hanger, the chair (back + seat), the ground line and a
    double arrow between the seat and the ground."""
    return ([("line", [(42.0, 58.0), (214.0, 58.0)], 12.0),
             ("line", [(128.0, 58.0), (128.0, 78.0), (110.0, 94.0), (110.0, 128.0), (118.0, 136.0),
                       (172.0, 136.0)], 12.0),
             ("line", [(84.0, 208.0), (196.0, 208.0)], 12.0)] + g_double_arrow(152.0, 146.0, 198.0, 12.0, 14.0))


def g_question():
    """A question mark (ref 03 'ask the attendant'): the hook as one stroke, the dot apart."""
    return [("line", g_arc(128.0, 92.0, 40.0, 180.0, 380.0, 20) + [(128.0, 136.0), (128.0, 158.0)], 28.0),
            ("disk", 128.0, 198.0, 17.0)]


# the rider's hip in the circle: the bar signs sit lower (room for the raised bar above the head)
BAR_HIP = (104.0, 182.0)
TIPS_HIP = (104.0, 168.0)
# the arrow beside the rider (kit color-variant fleet 2026-10-12, the checker's nit "arrowheads fuse with the
# ring on LOWER / RAISE / TIPS UP": at x 212 / 214 with a 30-unit head the head corner sat 0.7-1.4 units INSIDE
# the ring's inner edge, R 115): x 204 and a 24-unit head keep >= 10 units (3.5 texels in the shipped 0.225 m
# circle at 400 px/m) to the ring and >= 10 to the rider / bar / ski (test_liftdress K9 asserts both)
ARROW_X = 204.0
ARROW_HEAD = 24.0


def _picto_bar(state):
    hx, hy = BAR_HIP
    arrow = (g_arrow(ARROW_X, 102.0, ARROW_X, 186.0, head=ARROW_HEAD) if state == "down" else
             g_arrow(ARROW_X, 186.0, ARROW_X, 102.0, head=ARROW_HEAD) if state == "up" else [])
    return [("ink", g_chair(hx, hy, 0.0) + g_seated(hx, hy) + g_bar(hx, hy, "up" if state == "up" else "down")
             + arrow)]


def _picto_tipsup():
    """KEEP TIPS UP (ref 05): the rope rising to the right with the grip, the chair hanging from it,
    the rider, a ski under the boot with its tip curved up, an arrow up beside it."""
    hx, hy = TIPS_HIP
    ry = lambda x: 104.0 - (x - 10.0) * 0.40      # noqa: E731
    gx = hx - 26.0
    return [("ink", g_rope(10.0, ry(10.0), 246.0, ry(246.0)) + g_grip(gx, ry(gx)) + g_chair(hx, hy, ry(gx))
             + g_seated(hx, hy) + g_ski(hx - 8.0, hx + 88.0, hy + 52.0)
             + [("line", [(hx + 47.0, hy + 38.0), (hx + 50.0, hy + 46.0)], 15.0)]
             + g_arrow(ARROW_X, 178.0, ARROW_X, 96.0, head=ARROW_HEAD))]


# Circle pictograms: [(layer, prims)]; layer "ink" (the board's ink), "red" (INK red), "field" (the
# board's field color, punched). "ask" is the ref 03 white disc with a field-colored "?" (words).
PICTOS = {
    "lowerbar": _picto_bar("down"),
    "raisebar": _picto_bar("up"),
    "stay": _picto_bar(None),
    "tipsup": _picto_tipsup(),
    "unload": [("ink", g_arrow(128.0, 214.0, 128.0, 44.0, w=22.0, head=40.0))],
    "prepare": [("ink", g_arrow(128.0, 214.0, 128.0, 44.0, w=20.0, head=38.0, dashed=True, dash=20.0, gap=13.0))],
    "loose": [("ink", g_falling()), ("red", g_x(70.0, 104.0))],
    "pole": [("ink", g_pole())],
    "feet": [("ink", g_feet())],
    "seatheight": [("ink", g_seat_height())],
    # the tram: track rope, hanger, carriage, cabin (windows punched in the field color); the LED
    # departure board draws it in dots (unchanged)
    "tram": [("ink", [("line", [(4, 34), (252, 34)], 9), ("rect", 96, 24, 160, 48, 6),
                      ("line", [(128, 46), (128, 98)], 12), ("rect", 54, 96, 202, 226, 20)]),
             ("field", [("rect", 70, 112, 118, 160, 6), ("rect", 138, 112, 186, 160, 6),
                        ("line", [(54, 184), (202, 184)], 6)])],
}

# The kinds. `words` = the display lines (D11 / manifests join them with a space); sizes are the
# shipped label quads (never change one: the atlas rects / UVs follow it). Layouts:
#   plate     a 2:3 placard on the dark backer plate the label face is (the 0.70 x 0.55 boards)
#   placard   the whole label is one portrait placard (the reference vocabulary)
#   landscape border, circle pictogram left, words right (WAIT HERE on the gate)
#   row       border, the resort glyph left, words right (LIFT OPS)
#   words     border, words centered (SLOW, TICKETS)
#   stn       the station board: border, resort glyph, tram pictogram over the name
#   digit     a seat-position plate: border, one bold digit
#   dots / dial  the LED departure board / the tension dial (unchanged by the redraw)
# style: yellow (black ink) | blue (white ink) | orange (black ink) | red (white ink).
# vocab=True: the real-signage vocabulary of ref-signs (README) drawn for the cluster board a
# station may get later; NO station carries one today (test_liftdress K3 asserts it). fineprint:
# their words cannot reach T2 on a placard this size (real placards carry ~2.5 cm print too).
BOARDS = {
    "lowerbar": dict(layout="plate", size=(0.70, 0.55), style="yellow", picto="lowerbar", words=("LOWER", "BAR")),
    "raisebar": dict(layout="plate", size=(0.70, 0.55), style="yellow", picto="raisebar", words=("RAISE", "BAR")),
    "tipsup": dict(layout="plate", size=(0.70, 0.55), style="yellow", picto="tipsup", words=("KEEP", "TIPS UP")),
    "unload": dict(layout="plate", size=(0.70, 0.55), style="yellow", picto="unload", words=("UNLOAD",)),
    "stay": dict(layout="plate", size=(0.70, 0.55), style="yellow", picto="stay", words=("STAY", "SEATED")),
    "waithere": dict(layout="landscape", size=(0.60, 0.35), style="yellow", picto="feet", words=("WAIT", "HERE")),
    "seat1": dict(layout="digit", size=(0.25, 0.25), style="blue", words=("1",)),
    "seat2": dict(layout="digit", size=(0.25, 0.25), style="blue", words=("2",)),
    "seat3": dict(layout="digit", size=(0.25, 0.25), style="blue", words=("3",)),
    "seat4": dict(layout="digit", size=(0.25, 0.25), style="blue", words=("4",)),
    "ops": dict(layout="row", size=(1.20, 0.35), style="blue", words=("LIFT OPS",)),
    "slow": dict(layout="words", size=(1.20, 0.35), style="orange", words=("SLOW",), cap=0.28),
    "tickets": dict(layout="words", size=(1.80, 0.35), style="blue", words=("TICKETS",), cap=0.25),
    "dep": dict(layout="dots", size=(2.00, 0.80), field="led_bg", ink="amber",
                words=("NEXT TRAM", "10:15"), picto="tram", dot_pitch=0.025, dot_d=0.02,
                dot_rows=7),
    "stn": dict(layout="stn", size=(1.20, 0.50), style="red", words=("TRAM",), picto="tram"),
    "tension": dict(layout="dial", size=(0.30, 0.30), field="white", ink="black",
                    words=("MIN", "MAX")),
    # the reference vocabulary (ref-signs 03 / 04 / 01): drawn, previewed, carried by no station
    "prepare": dict(layout="placard", size=(0.36, 0.54), style="yellow", picto="prepare",
                    words=("PREPARE", "TO UNLOAD"), vocab=True, fineprint=True),
    "looseclothing": dict(layout="placard", size=(0.36, 0.54), style="yellow", picto="loose",
                          words=("CHECK FOR", "LOOSE CLOTHING", "AND", "EQUIPMENT"), vocab=True, fineprint=True),
    "polestraps": dict(layout="placard", size=(0.36, 0.54), style="yellow", picto="pole",
                       words=("REMOVE", "POLE STRAPS", "FROM WRISTS"), vocab=True, fineprint=True),
    "askattendant": dict(layout="placard", size=(0.36, 0.54), style="blue", picto="ask",
                         words=("IF YOU HAVE NOT", "USED A LIFT", "BEFORE PLEASE", "ASK ATTENDANT"), vocab=True,
                         fineprint=True),
    "seatheight": dict(layout="placard", size=(0.45, 0.60), style="blue", picto="seatheight",
                       words=("LOADING", "SEAT HEIGHT", "21 ± 2 INCHES"), vocab=True, fineprint=True),
    "instructions": dict(layout="instructions", size=(0.45, 0.60), style="blue",
                         words=("INSTRUCTIONS", "AND WARNINGS"),
                         bullets=(("YOU MUST KNOW HOW TO LOAD,", "RIDE AND UNLOAD SAFELY"),
                                  ("SNOW VEHICLES MAY BE", "ENCOUNTERED"), ("DO NOT BOUNCE, SWING OR", "JUMP FROM CHAIRS"),
                                  ("DO NOT THROW ANYTHING", "FROM CHAIRS")), vocab=True, fineprint=True),
    "capacity": dict(layout="placard", size=(0.45, 0.60), style="blue", picto=None,
                     words=("DOWNHILL", "LOADING CAPACITY", "NOT TO EXCEED", "FOUR LOADED", "NON-CONSECUTIVE",
                            "CHAIRS"), vocab=True, fineprint=True),
}
STYLES = {"yellow": ("yellow", "black"), "blue": ("blue", "white"), "orange": ("orange", "black"),
          "red": ("red", "white")}


def board_colours(spec):
    """(field key, ink key) of a BOARDS spec (style, or the dots / dial field / ink)."""
    if "style" in spec:
        return STYLES[spec["style"]]
    return spec["field"], spec["ink"]


_TEXT = {}
_STEM = {}


def _text_tris(body, cap):
    key = (body, round(float(cap), 9))
    if key not in _TEXT:
        import welcome_sign             # lazy: the FONT path (Bfont), as the DQ digits
        _TEXT[key] = welcome_sign.text_triangles(body, float(cap))
    return _TEXT[key]


def stem_ratio():
    """Bfont vertical stem width / cap height (the "I" glyph's width), measured once."""
    if "I" not in _STEM:
        _tris, w = _text_tris("I", 1.0)
        _STEM["I"] = float(w)
    return _STEM["I"]


def text_width(body, cap):
    return _text_tris(body, cap)[1]


def _text_cov(body, cap, cx, cy, cond, W, H, w, h):
    """(h, w) coverage (rows bottom-up) of `body` at cap height `cap`, centered on (cx, cy)
    board meters, x scaled by `cond`, on a W x H m board drawn into w x h texels."""
    import welcome_sign
    tris, _w = _text_tris(body, cap)
    ox, oy = cx - W / 2.0, cy - H / 2.0
    tt = [tuple((x * cond + ox, y + oy) for x, y in tri) for tri in tris]
    pu, pv = w / W, h / H
    return welcome_sign._tri_coverage(tt, w, h, pu, pv, ss=TEXT_SS)


# -- sign-bold words (2026-10-12): Bfont through a FONT curve with an outline offset ------------

_BTEXT = {}
_BREF = {}


def _font_tris(body, weight, track, align="LEFT"):
    """Triangles (font units, size 1) of `body` in Bfont with the outline offset `weight` and the
    character spacing `track` (a temporary FONT curve, removed again); align LEFT: every string
    starts at the same x, so a prefix's right edge is a glyph position."""
    cu = bpy.data.curves.new("LDBoardText", type="FONT")
    cu.body = body
    cu.size = 1.0
    cu.align_x = align
    cu.offset = weight
    cu.space_character = track
    ob = bpy.data.objects.new("LDBoardText", cu)
    bpy.context.scene.collection.objects.link(ob)
    try:
        dg = bpy.context.evaluated_depsgraph_get()
        me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
        bm = bmesh.new()
        bm.from_mesh(me)
        bmesh.ops.triangulate(bm, faces=bm.faces[:])
        tris = [tuple((v.co.x, v.co.y) for v in f.verts) for f in bm.faces]
        bm.free()
        bpy.data.meshes.remove(me)
    finally:
        bpy.data.objects.remove(ob, do_unlink=True)
        bpy.data.curves.remove(cu)
    return tris


def _one_flags(body, weight, track, lo, hi, stem):
    """Bfont's '1' is a bare stem (it reads as I): a flag (two triangles, font units) on the top
    left of every '1' in `body`, placed from the right edge of the prefix ending in it."""
    out = []
    capf = hi - lo
    t = 0.85 * stem
    for i, ch in enumerate(body):
        if ch != "1":
            continue
        pre = np.array([p for tri in _font_tris(body[:i + 1], weight, track) for p in tri], np.float64)
        xl = float(pre[:, 0].max()) - stem
        p0, p3 = (xl + 0.5 * stem, hi), (xl + 0.5 * stem, hi - t)
        p1, p2 = (xl - 0.30 * capf, hi - 0.24 * capf), (xl - 0.30 * capf, hi - 0.24 * capf - t)
        out += [(p0, p1, p2), (p0, p2, p3)]
    return out


def _bref(weight=SIGN_WEIGHT, track=SIGN_TRACK):
    """(cap bottom, cap top, stem) of 'H' / 'I' at this weight (font units): the cap box every
    line is scaled and centered on, so lines with Q's tail or a comma keep their baseline."""
    k = (round(weight, 6), round(track, 6))
    if k not in _BREF:
        th = np.array([p for t in _font_tris("H", weight, track) for p in t])
        ti = np.array([p for t in _font_tris("I", weight, track) for p in t])
        _BREF[k] = (float(th[:, 1].min()), float(th[:, 1].max()), float(ti[:, 0].max() - ti[:, 0].min()))
    return _BREF[k]


def btext_tris(body, cap, weight=SIGN_WEIGHT, track=SIGN_TRACK):
    """Sign-bold caps: (triangles in meters centered on the line's width and the cap box, width_m,
    stem_m) at cap height `cap` (the bold 'H' is exactly `cap` tall)."""
    key = (body, round(float(cap), 9), round(weight, 6), round(track, 6))
    if key not in _BTEXT:
        lo, hi, stem = _bref(weight, track)
        s = float(cap) / (hi - lo)
        tris = _font_tris(body, weight, track) + _one_flags(body, weight, track, lo, hi, stem)
        pts = np.array([p for t in tris for p in t], np.float64)
        xc = 0.5 * (pts[:, 0].min() + pts[:, 0].max())
        yc = 0.5 * (lo + hi)
        out = [tuple(((p[0] - xc) * s, (p[1] - yc) * s) for p in t) for t in tris]
        _BTEXT[key] = (out, float((pts[:, 0].max() - pts[:, 0].min()) * s), float(stem * s))
    return _BTEXT[key]


def btext_width(body, cap):
    return btext_tris(body, cap)[1]


def bstem_ratio():
    """Sign-bold stem / cap (the bold 'I' width over the bold 'H' height): ~0.20."""
    return btext_tris("I", 1.0)[2]


def _btext_cov(body, cap, cx, cy, cond, W, H, w, h):
    """(h, w) coverage (rows bottom-up) of sign-bold `body` at cap `cap`, centered on (cx, cy) board
    meters, x scaled by `cond` (8 x 8 samples per texel, the _text_cov path)."""
    import welcome_sign
    tris, _w, _s = btext_tris(body, cap)
    ox, oy = cx - W / 2.0, cy - H / 2.0
    tt = [tuple((x * cond + ox, y + oy) for x, y in tri) for tri in tris]
    return welcome_sign._tri_coverage(tt, w, h, w / W, h / H, ss=TEXT_SS)


def _sdf_cov(prims, x0, y0, S, w, h, pu, pv, ss=SDF_SS):
    """(h, w) coverage (rows bottom-up) of 256-canvas primitives drawn into the board square
    [x0, x0 + S] x [y0, y0 + S] meters (canvas y down = meters y up); anisotropic-safe: the
    square stays square in METERS whatever pu / pv are."""
    import ski_launcher_glyph as slg
    cov = np.zeros((h, w), dtype=np.float32)
    j0, j1 = max(0, int(math.floor(x0 * pu))), min(w, int(math.ceil((x0 + S) * pu)))
    i0, i1 = max(0, int(math.floor(y0 * pv))), min(h, int(math.ceil((y0 + S) * pv)))
    if j1 <= j0 or i1 <= i0:
        return cov
    xs = (np.arange(j0 * ss, j1 * ss, dtype=np.float64) + 0.5) / ss / pu
    ys = (np.arange(i0 * ss, i1 * ss, dtype=np.float64) + 0.5) / ss / pv
    X, Y = np.meshgrid((xs - x0) / S * slg.CANVAS, (y0 + S - ys) / S * slg.CANVAS)
    d = np.full(X.shape, np.inf)
    for p in prims:
        d = np.minimum(d, slg.sd_prim(p, X, Y))
    step = slg.CANVAS / S / (ss * min(pu, pv))           # canvas units per sample
    c = np.clip(0.5 - d / step, 0.0, 1.0)
    cov[i0:i1, j0:j1] = c.reshape(i1 - i0, ss, j1 - j0, ss).mean(axis=(1, 3))
    return cov


def _cov_m(prims, w, h, pu, pv, box=None, ss=SDF_SS):
    """(h, w) coverage (rows bottom-up) of primitives in BOARD METERS (x right, y up): the
    ski_launcher_glyph kinds plus ("rrline", x0, y0, x1, y1, r, width) = a round-rect outline
    stroked on its centerline. `box` (x0, y0, x1, y1) limits the work to that area."""
    import ski_launcher_glyph as slg
    cov = np.zeros((h, w), np.float32)
    if box is None:
        j0, j1, i0, i1 = 0, w, 0, h
    else:
        j0, j1 = max(0, int(math.floor(box[0] * pu)) - 1), min(w, int(math.ceil(box[2] * pu)) + 1)
        i0, i1 = max(0, int(math.floor(box[1] * pv)) - 1), min(h, int(math.ceil(box[3] * pv)) + 1)
    if j1 <= j0 or i1 <= i0:
        return cov
    xs = (np.arange(j0 * ss, j1 * ss, dtype=np.float64) + 0.5) / ss / pu
    ys = (np.arange(i0 * ss, i1 * ss, dtype=np.float64) + 0.5) / ss / pv
    X, Y = np.meshgrid(xs, ys)
    d = np.full(X.shape, np.inf)
    for p in prims:
        if p[0] == "rrline":
            _k, x0, y0, x1, y1, r, wd = p
            d = np.minimum(d, np.abs(slg.sd_round_rect(X, Y, x0, y0, x1, y1, r)) - wd / 2.0)
        else:
            d = np.minimum(d, slg.sd_prim(p, X, Y))
    c = np.clip(0.5 - d * (ss * min(pu, pv)), 0.0, 1.0)
    cov[i0:i1, j0:j1] = c.reshape(i1 - i0, ss, j1 - j0, ss).mean(axis=(1, 3))
    return cov


def _over(col, cov, rgb):
    col[...] = col * (1.0 - cov[..., None]) + np.asarray(rgb, np.float32) * cov[..., None]


def _fit_line(body, cap, box_w):
    """(condense, fits) for one line at cap in box_w meters."""
    w = text_width(body, cap)
    if w <= box_w:
        return 1.0, True
    k = box_w / w
    return k, k >= CONDENSE_MIN - 1e-9


def _layer_rgb(key, spec):
    if key == "ink":
        return INK[board_colours(spec)[1]]
    if key == "field":
        return INK[board_colours(spec)[0]]
    return INK[key]


def _paint_picto(col, name, spec, x0, y0, S, w, h, pu, pv):
    for key, prims in PICTOS[name]:
        _over(col, _sdf_cov(prims, x0, y0, S, w, h, pu, pv), _layer_rgb(key, spec))


def _glyph_prims():
    import ski_launcher_glyph as slg
    return slg.cand_c()


# -- the redraw's layout pieces (board meters, y up) ------------------------------------

class _Canvas:
    """One label being painted: linear color (h, w, 3) rows bottom-up, its size in meters and
    texel density, the metrics collected on the way."""

    def __init__(self, col, W, H, pu, pv):
        self.col, self.W, self.H, self.pu, self.pv = col, W, H, pu, pv
        self.h, self.w = col.shape[0], col.shape[1]
        self.ppm = min(pu, pv)
        self.strokes = []           # every painted pictogram / border / ring stroke, texels
        self.text = []              # (cap_m, condense, stem_px) per painted word line

    def fill(self, prims, key, box=None):
        _over(self.col, _cov_m(prims, self.w, self.h, self.pu, self.pv, box=box), INK[key])

    def words(self, lines, cap, cond, cx, ytop, key, gap=SIGN_LINE_GAP):
        """Sign-bold lines from the cap-box top `ytop` down, centered on cx."""
        y = ytop - cap / 2.0
        stem = btext_tris("I", cap)[2]
        for s in lines:
            _over(self.col, _btext_cov(s, cap, cx, y, cond, self.W, self.H, self.w, self.h), INK[key])
            self.text.append((cap, cond, stem * cond * self.pu))
            y -= cap * (1.0 + gap)

    def bolt(self, x, y, d=BOLT_D_M):
        box = (x - d, y - d, x + d, y + d)
        self.fill([("disk", x, y, d / 2.0)], "bolt", box)
        self.fill([("ring", x, y, d / 2.0, 0.0022)], "bolt_rim", box)

    def border(self, x0, y0, x1, y1, edge, line, radius, field, ink):
        """A sign blank: the field to its rounded edge, the inset border line; returns the inner
        rect (inside the line)."""
        box = (x0, y0, x1, y1)
        self.fill([("rect", x0, y0, x1, y1, radius)], field, box)
        a = edge + line / 2.0
        self.fill([("rrline", x0 + a, y0 + a, x1 - a, y1 - a, max(radius - a, line), line)], ink, box)
        self.strokes.append(line * self.ppm)
        e = edge + line
        return (x0 + e, y0 + e, x1 - e, y1 - e)

    def circle(self, name, cx, cy, D, spec, field, ink):
        """The circle pictogram: the ring (RING_FRAC x D, >= 4.4 texels) and the glyph layers
        clipped to its inside; picto 'ask' = a filled disc with a field-colored '?'."""
        x0, y0 = cx - D / 2.0, cy - D / 2.0
        ring = max(RING_FRAC * D, STROKE_T2_PX * 1.1 / self.ppm)
        rc = ring / D * 256.0
        if name == "ask":
            _over(self.col, _sdf_cov([("disk", 128.0, 128.0, 127.5)], x0, y0, D, self.w, self.h, self.pu, self.pv),
                  INK[ink])
            q = g_question()
            _over(self.col, _sdf_cov(q, x0, y0, D, self.w, self.h, self.pu, self.pv), INK[field])
            self.strokes.append(q[0][2] / 256.0 * D * self.ppm)
            return
        _over(self.col, _sdf_cov([("ring", 128.0, 128.0, 128.0 - rc / 2.0 - 0.5, rc)], x0, y0, D, self.w, self.h,
                                 self.pu, self.pv), INK[ink])
        self.strokes.append(ring * self.ppm)
        clip = _sdf_cov([("disk", 128.0, 128.0, 128.0 - rc - 0.5)], x0, y0, D, self.w, self.h, self.pu, self.pv)
        for key, prims in PICTOS[name]:
            k = {"ink": ink, "field": field}.get(key, key)
            _over(self.col, _sdf_cov(prims, x0, y0, D, self.w, self.h, self.pu, self.pv) * clip, INK[k])
            for p in prims:
                if p[0] == "line":
                    self.strokes.append(p[2] / 256.0 * D * self.ppm)
                elif p[0] == "ring":
                    self.strokes.append(p[4] / 256.0 * D * self.ppm)


def _fit_block(lines, cap_max, box_w, box_h):
    """(cap, condense) for a block of sign-bold lines: the largest cap <= cap_max whose widest line
    fits box_w at condense >= CONDENSE_MIN and whose block (SIGN_LINE_GAP between lines) fits box_h;
    when that cap falls under CAP_T2, the words may condense to SIGN_CONDENSE_MIN to hold CAP_T2."""
    def fit(cap, floor):
        while cap > 0.004:
            cond = min([1.0] + [box_w / wd for wd in (btext_width(s, cap) for s in lines) if wd > box_w])
            if cond >= floor - 1e-9 and _block_h(len(lines), cap) <= box_h + 1e-9:
                return round(cap, 5), cond
            cap *= 0.98
        return None
    got = fit(float(cap_max), CONDENSE_MIN)
    if (got is None or got[0] < CAP_T2 - 1e-9) and cap_max >= CAP_T2:
        alt = fit(CAP_T2, SIGN_CONDENSE_MIN)
        if alt is not None and (got is None or alt[0] > got[0]):
            got = alt
    if got is None:
        raise ValueError(f"_fit_block: {lines} do not fit {box_w:.3f} x {box_h:.3f} m")
    return got


def _block_h(n, cap):
    return n * cap + (n - 1) * SIGN_LINE_GAP * cap


def _placard(cv, x0, y0, PW, PH, spec):
    """A real-style portrait placard (ref 03 / 04 / 05) at (x0, y0), PW x PH meters: field to the
    rounded edge, inset border line, a bolt top and bottom, the circle pictogram, the bold caps
    centered under it (largest cap <= CAP_T2 that fits; the circle shrinks to PLACARD_D_MIN first)."""
    field, ink = board_colours(spec)
    ppm = cv.ppm
    line = max(PLACARD_LINE * PW, STROKE_T2_PX * 1.1 / ppm)
    ix0, iy0, ix1, iy1 = cv.border(x0, y0, x0 + PW, y0 + PH, PLACARD_EDGE * PW, line, PLACARD_RADIUS * PW,
                                   field, ink)
    cx = x0 + PW / 2.0
    band = PLACARD_BAND * PW
    cv.bolt(cx, iy1 - band / 2.0)
    cv.bolt(cx, iy0 + band / 2.0)
    top, bot = iy1 - band, iy0 + band
    box_w = (ix1 - ix0) - 0.03 * PW
    gap = 0.06 * PW
    lines = list(spec["words"])
    if spec.get("picto") is None:
        cap, cond = _fit_block(lines, CAP_T2, box_w, top - bot)
        cv.words(lines, cap, cond, cx, (top + bot) / 2.0 + _block_h(len(lines), cap) / 2.0, ink)
        return dict(cap_m=cap, condense=cond, glyph_m=0.0)
    d_min = PLACARD_D_MIN * PW
    cap, cond = _fit_block(lines, CAP_T2, box_w, top - bot - gap - d_min)
    D = min(PLACARD_D_MAX * PW, top - bot - gap - _block_h(len(lines), cap), ix1 - ix0 - 0.04 * PW)
    used = D + gap + _block_h(len(lines), cap)
    ytop = (top + bot) / 2.0 + used / 2.0
    cv.circle(spec["picto"], cx, ytop - D / 2.0, D, spec, field, ink)
    cv.words(lines, cap, cond, cx, ytop - D - gap, ink)
    return dict(cap_m=cap, condense=cond, glyph_m=round(D, 4))


def _lay_plate(cv, spec):
    """The 0.70 x 0.55 boards: the label face is the dark backer plate (a light rim, two bolts at
    mid-height on its flanks) with ONE real-size 2:3 placard bolted on, centered."""
    W, H = cv.W, cv.H
    cv.col[...] = np.asarray(INK["backer"], np.float32)
    cv.fill([("rrline", 0.003, 0.003, W - 0.003, H - 0.003, 0.004, 0.004)], "backer_rim")
    PH = H - 2.0 * PLATE_MARGIN_M
    PW = PH / PLACARD_RATIO
    for x in (0.035, W - 0.035):
        cv.bolt(x, H / 2.0)
    met = _placard(cv, (W - PW) / 2.0, PLATE_MARGIN_M, PW, PH, spec)
    met["placard_m"] = [round(PW, 4), round(PH, 4)]
    return met


def _lay_placard(cv, spec):
    met = _placard(cv, 0.0, 0.0, cv.W, cv.H, spec)
    met["placard_m"] = [cv.W, cv.H]
    return met


def _landscape_blank(cv, spec, edge=0.010, radius=0.020, bolts=True):
    """A landscape sign blank: field, inset border line (>= 4.4 texels), bolts at mid-left and
    mid-right inside the line; returns (field, ink, inner rect, the content x range)."""
    field, ink = board_colours(spec)
    line = max(0.011, STROKE_T2_PX * 1.1 / cv.ppm)
    ix0, iy0, ix1, iy1 = cv.border(0.0, 0.0, cv.W, cv.H, edge, line, radius, field, ink)
    if not bolts:
        return field, ink, (ix0, iy0, ix1, iy1), (ix0 + 0.012, ix1 - 0.012)
    bx = BOLT_D_M * 1.1
    cv.bolt(ix0 + bx, cv.H / 2.0)
    cv.bolt(ix1 - bx, cv.H / 2.0)
    return field, ink, (ix0, iy0, ix1, iy1), (ix0 + 2.0 * bx + 0.006, ix1 - 2.0 * bx - 0.006)


def _lay_landscape(cv, spec):
    """WAIT HERE: border, bolts, the circle pictogram at the left, the words to its right."""
    field, ink, (ix0, iy0, ix1, iy1), (cx0, cx1) = _landscape_blank(cv, spec)
    D = (iy1 - iy0) - 0.05
    cv.circle(spec["picto"], cx0 + D / 2.0, cv.H / 2.0, D, spec, field, ink)
    tx0 = cx0 + D + 0.025
    lines = list(spec["words"])
    cap, cond = _fit_block(lines, 0.12, cx1 - tx0, (iy1 - iy0) - 0.03)
    cv.words(lines, cap, cond, (tx0 + cx1) / 2.0, cv.H / 2.0 + _block_h(len(lines), cap) / 2.0, ink)
    return dict(cap_m=cap, condense=cond, glyph_m=round(D, 4))


def _lay_row(cv, spec):
    """LIFT OPS: border, bolts, the resort glyph (cand_c) at the left in the ink, the words right."""
    field, ink, (ix0, iy0, ix1, iy1), (cx0, cx1) = _landscape_blank(cv, spec)
    S = (iy1 - iy0) - 0.03
    _over(cv.col, _sdf_cov(_glyph_prims(), cx0, (cv.H - S) / 2.0, S, cv.w, cv.h, cv.pu, cv.pv), INK[ink])
    tx0 = cx0 + S + 0.03
    cap, cond = _fit_block(list(spec["words"]), 0.20, cx1 - tx0, (iy1 - iy0) - 0.04)
    cv.words(spec["words"], cap, cond, (tx0 + cx1) / 2.0, cv.H / 2.0 + cap / 2.0, ink)
    return dict(cap_m=cap, condense=cond, glyph_m=round(S, 4))


def _lay_words(cv, spec):
    """SLOW / TICKETS: border close to the edge, bolts, the word(s) centered at the spec's cap
    (SLOW keeps 0.28 m: its T1 claim) or the largest that fits."""
    field, ink, (ix0, iy0, ix1, iy1), (cx0, cx1) = _landscape_blank(cv, spec, edge=0.008, radius=0.016)
    lines = list(spec["words"])
    cap, cond = _fit_block(lines, float(spec.get("cap", CAP_T2)), cx1 - cx0, (iy1 - iy0) - 0.02)
    cv.words(lines, cap, cond, (cx0 + cx1) / 2.0, cv.H / 2.0 + _block_h(len(lines), cap) / 2.0, ink)
    return dict(cap_m=cap, condense=cond, glyph_m=0.0)


def _lay_stn(cv, spec):
    """The tram's station board (ref 01's station board: red, white border and lettering): the
    resort glyph (>= 0.40 m, T1) at the left, the tram pictogram over the station word at the right;
    no bolts (a framed board)."""
    field, ink, (ix0, iy0, ix1, iy1), _cx = _landscape_blank(cv, spec, edge=0.010, radius=0.020, bolts=False)
    S = (iy1 - iy0) - 0.024
    gx0 = ix0 + 0.020
    _over(cv.col, _sdf_cov(_glyph_prims(), gx0, (cv.H - S) / 2.0, S, cv.w, cv.h, cv.pu, cv.pv), INK[ink])
    bx0, bx1 = gx0 + S + 0.03, ix1 - 0.02
    cap, cond = _fit_block(list(spec["words"]), 0.10, bx1 - bx0, 0.10)
    P = (iy1 - iy0) - 0.024 - cap - 0.03
    pb = iy0 + 0.012 + cap + 0.03
    _paint_picto(cv.col, spec["picto"], spec, (bx0 + bx1) / 2.0 - P / 2.0, pb, P, cv.w, cv.h, cv.pu, cv.pv)
    cv.words(spec["words"], cap, cond, (bx0 + bx1) / 2.0, iy0 + 0.012 + cap, ink)
    return dict(cap_m=cap, condense=cond, glyph_m=round(S, 4), picto_m=round(P, 4))


def _lay_digit(cv, spec):
    """A seat-position plate: rounded blank with the inset border line, one sign-bold digit (the '1'
    carries its flag: btext_tris adds one to Bfont's bare stem)."""
    field, ink, (ix0, iy0, ix1, iy1), _cx = _landscape_blank(cv, spec, edge=0.006, radius=0.022, bolts=False)
    cap = round(0.62 * (iy1 - iy0), 4)
    cv.words(spec["words"], cap, 1.0, cv.W / 2.0, cv.H / 2.0 + cap / 2.0, ink)
    return dict(cap_m=cap, condense=1.0, glyph_m=cap)


def _lay_instructions(cv, spec):
    """INSTRUCTIONS AND WARNINGS (ref 03 / ref 01): a blue header with the white title, a white panel
    inset below with blue bullets, each wrapped over its lines (fine print, as on the real board)."""
    field, ink = board_colours(spec)
    W, H = cv.W, cv.H
    cv.fill([("rect", 0.0, 0.0, W, H, 0.06 * W)], field)
    e = 0.035 * W
    hb = 0.30 * H
    title = list(spec["words"])
    cap, cond = _fit_block(title, CAP_T2, W - 2 * e - 0.04, hb - 0.04)
    cv.words(title, cap, cond, W / 2.0, H - e - (hb - _block_h(len(title), cap)) / 2.0, ink)
    py0, py1 = e, H - e - hb
    line = max(0.011, STROKE_T2_PX * 1.1 / cv.ppm)
    cv.fill([("rect", e, py0, W - e, py1, 0.03 * W)], ink)
    cv.fill([("rrline", e + line / 2.0 + 0.008, py0 + line / 2.0 + 0.008, W - e - line / 2.0 - 0.008,
              py1 - line / 2.0 - 0.008, 0.02 * W, line)], field)
    cv.strokes.append(line * cv.ppm)
    bul = [tuple(b) for b in spec["bullets"]]
    flat = [s for b in bul for s in b]
    bx0 = e + 0.045
    tx0 = bx0 + 0.025
    n_gap = len(bul) - 1                        # an extra half line between bullets
    bcap, bcond = _fit_block(flat, 0.05, (W - e - 0.03) - tx0, (py1 - py0) - 0.07 - n_gap * 0.5 * 0.05)
    step = bcap * (1.0 + SIGN_LINE_GAP)
    total = _block_h(len(flat), bcap) + n_gap * 0.5 * bcap
    y = (py0 + py1) / 2.0 + total / 2.0
    for b in bul:
        cv.fill([("disk", bx0, y - bcap / 2.0, 0.26 * bcap)], field)
        for s in b:
            wd = btext_width(s, bcap) * bcond
            cv.words([s], bcap, bcond, tx0 + wd / 2.0, y, field)
            y -= step
        y -= 0.5 * bcap
    return dict(cap_m=min(cap, bcap), condense=min(cond, bcond), glyph_m=0.0)


_LAYOUTS = {"plate": _lay_plate, "placard": _lay_placard, "landscape": _lay_landscape, "row": _lay_row,
            "words": _lay_words, "stn": _lay_stn, "digit": _lay_digit, "instructions": _lay_instructions}


def paint_board(kind, rect, ppm, base, ctl, *, size_m=None, words=None):
    """Paint one label of `kind` (BOARDS) into texel rect (row0, row1, col0, col1) of the
    LINEAR, rows-bottom-up maps `base` / `ctl` - either the full (A, A, 4) arrays or the
    in-place slices over the rect (cs2_compose.stamp_on_face hands slices). The label's size
    in meters is `size_m` (default BOARDS[kind]["size"]); `ppm` (scalar or (pu, pv)) is only
    checked against rect / size (None: derived). ControlMask 0 over the whole rect (a label is
    never tinted), BaseColor alpha 1. `words` overrides the kind's word lines.
    Returns the metrics dict (also on paint_board.last): kind, rect, px_per_m (pu, pv),
    size_m, lines, cap_m (the smallest word cap), condense, stroke_px (the narrowest word stem),
    glyph_m (the circle / glyph), picto_stroke_px (the narrowest pictogram / border stroke),
    read_m (design L3's basis: 125 x glyph_m), t1_ok / t2_ok (design L3 at this density),
    l1_ppm_ok, fineprint (a vocabulary kind whose words cannot reach T2)."""
    spec = dict(BOARDS[kind])
    if words is not None:
        spec["words"] = tuple(words)
    W, H = (float(v) for v in (size_m or spec["size"]))
    r0, r1, c0, c1 = (int(v) for v in rect)
    h, w = r1 - r0, c1 - c0
    if h <= 0 or w <= 0:
        raise ValueError(f"paint_board {kind}: empty rect {rect}")
    if base.shape[0] == h and base.shape[1] == w:
        b_sl, c_sl = base, ctl
    else:
        b_sl, c_sl = base[r0:r1, c0:c1], ctl[r0:r1, c0:c1]
    pu, pv = w / W, h / H
    if ppm is not None:
        want = (float(ppm), float(ppm)) if np.isscalar(ppm) else tuple(float(v) for v in ppm)
        if abs(want[0] - pu) > 0.02 * want[0] + 1.0 or abs(want[1] - pv) > 0.02 * want[1] + 1.0:
            raise ValueError(f"paint_board {kind}: rect {w} x {h} px over {W} x {H} m is "
                             f"{pu:.0f} / {pv:.0f} px/m, not {want}")
    col = np.empty((h, w, 3), dtype=np.float32)
    col[...] = np.asarray(INK[board_colours(spec)[0]], np.float32)
    met = {"kind": kind, "rect": [r0, r1, c0, c1], "px_per_m": [round(pu, 2), round(pv, 2)],
           "size_m": [W, H], "lines": list(spec["words"])}
    lay = spec["layout"]
    cv = None
    if lay == "dots":
        met.update(_paint_dots(col, spec, W, H, w, h, pu, pv))
    elif lay == "dial":
        met.update(_paint_dial(col, spec, W, H, w, h, pu, pv))
    elif lay in _LAYOUTS:
        cv = _Canvas(col, W, H, pu, pv)
        met.update(_LAYOUTS[lay](cv, spec))
    else:
        raise ValueError(f"paint_board {kind}: layout {lay!r}")
    if cv is not None:
        met["cap_m"] = round(min([t[0] for t in cv.text] or [met.get("cap_m", 0.0)]), 4)
        met["condense"] = round(min([t[1] for t in cv.text] or [1.0]), 4)
        met["stroke_px"] = round(min([t[2] for t in cv.text] or [0.0]), 2)
        met["picto_stroke_px"] = round(min(cv.strokes), 2) if cv.strokes else None
        met["style"] = spec["style"]
    elif lay == "dial":
        met["stroke_px"] = round(stem_ratio() * met["cap_m"] * met["condense"] * pu, 2)
    met["glyph_m"] = round(float(met.get("glyph_m", 0.0)), 4)
    met["read_m"] = round(T1_READ_M_PER_M * met["glyph_m"], 1)
    ppm_min = min(pu, pv)
    big = max(W, H) > 1.50
    if lay == "dial":           # a scale, not words: MIN / MAX are close-up print (no T2 claim)
        met["t2_ok"] = None
    else:
        met["t2_ok"] = bool(met["stroke_px"] >= STROKE_T2_PX - 1e-6
                            and (met["cap_m"] >= CAP_T2 - 1e-9 or lay == "dots")
                            and (met.get("picto_stroke_px") is None or met["picto_stroke_px"] >= STROKE_T2_PX - 1e-6))
    met["t1_ok"] = bool(met.get("glyph_m", 0.0) >= PICTO_T1 - 1e-9 or met["cap_m"] >= 0.28 - 1e-9)
    met["l1_ppm_ok"] = bool(big or (math.sqrt(pu * pv) >= 400.0 - 1e-6 and ppm_min >= 380.0 - 1e-6))
    if spec.get("fineprint"):
        met["fineprint"] = True
    b_sl[..., 0:3] = col
    b_sl[..., 3] = 1.0
    c_sl[..., 0:3] = 0.0
    paint_board.last = met
    return met


paint_board.last = None


def _paint_dots(col, spec, W, H, w, h, pu, pv):
    """Amber dot matrix (design TB1): lit dots where the words / pictogram cover the dot
    center, unlit dots dim; dot pitch / diameter in meters (0.02 m = 4 texels at 200 px/m)."""
    p, dd, rows = float(spec["dot_pitch"]), float(spec["dot_d"]), int(spec["dot_rows"])
    m = 0.05
    S = H - 2 * m
    nx, ny = int(math.floor((W - 2 * m) / p)), int(math.floor((H - 2 * m) / p))
    gx0 = (W - nx * p) / 2.0
    gy0 = (H - ny * p) / 2.0
    # design coverage at dot-grid resolution: rasterize into a (ny, nx) "texel" grid of pitch p
    lit = np.zeros((ny, nx), dtype=np.float32)
    cap = rows * p
    tx0 = m + S + m
    box = W - tx0 - m
    lines = list(spec["words"])
    cond = 1.0
    for ln in lines:
        k, _ok = _fit_line(ln, cap, box)
        cond = min(cond, k)
    # render words and pictogram on the dot grid (one sample per dot = the dot's center)
    gW, gH = nx * p, ny * p
    yc = [gy0 + gH - (gH - len(lines) * cap - (len(lines) - 1) * 2 * p) / 2.0 - cap / 2.0]
    for _ in lines[1:]:
        yc.append(yc[-1] - cap - 2 * p)
    for ln, y in zip(lines, yc):
        c = _text_cov(ln, cap, tx0 + box / 2.0 - gx0, y - gy0, cond, gW, gH, nx, ny)
        lit = np.maximum(lit, c)
    for key, prims in PICTOS[spec["picto"]]:
        if key == "ink":
            lit = np.maximum(lit, _sdf_cov(prims, m - gx0, m - gy0, S, nx, ny, 1.0 / p, 1.0 / p, ss=1))
        elif key == "field":
            lit = np.minimum(lit, 1.0 - _sdf_cov(prims, m - gx0, m - gy0, S, nx, ny, 1.0 / p, 1.0 / p,
                                                  ss=1))
    on = lit >= DOT_ON
    # draw every dot as a disc (SDF in meters), lit amber / unlit dim
    X, Y = np.meshgrid((np.arange(w) + 0.5) / pu, (np.arange(h) + 0.5) / pv)
    ix = np.clip(np.floor((X - gx0) / p).astype(int), 0, nx - 1)
    iy = np.clip(np.floor((Y - gy0) / p).astype(int), 0, ny - 1)
    cxs = gx0 + (ix + 0.5) * p
    cys = gy0 + (iy + 0.5) * p
    dist = np.hypot(X - cxs, Y - cys) - dd / 2.0
    aa = 1.0 / min(pu, pv)
    disc = np.clip(0.5 - dist / aa, 0.0, 1.0).astype(np.float32)
    inside = (X >= gx0) & (X <= gx0 + gW) & (Y >= gy0) & (Y <= gy0 + gH)
    disc[~inside] = 0.0
    state = on[iy, ix]
    _over(col, disc * state, INK["amber"])
    _over(col, disc * (~state), INK["amber_off"])
    return {"cap_m": round(cap, 4), "condense": round(cond, 4), "glyph_m": round(S, 4),
            "dots": [nx, ny], "dots_lit": int(on.sum()), "dot_px": round(dd * min(pu, pv), 2),
            "stroke_px": round(dd * min(pu, pv), 2)}


def _paint_dial(col, spec, W, H, w, h, pu, pv):
    """Rope-tension dial: a 270 deg scale (red - green - red bands, 11 ticks), the rim ring,
    MIN / MAX words; the needle is geometry (gauge)."""
    X, Y = np.meshgrid((np.arange(w) + 0.5) / pu, (np.arange(h) + 0.5) / pv)
    cx, cy = W / 2.0, H / 2.0
    R = min(W, H) / 2.0
    r = np.hypot(X - cx, Y - cy)
    ang = np.degrees(np.arctan2(X - cx, Y - cy))           # 0 = up, + clockwise
    aa = 1.0 / min(pu, pv)

    def band(r0, r1):
        return np.clip(np.minimum(r - r0, r1 - r) / aa + 0.5, 0.0, 1.0).astype(np.float32)
    rim = band(R - 0.018, R - 0.004)
    _over(col, rim, INK["black"])
    arc = band(0.62 * R, 0.80 * R)
    inarc = np.abs(ang) <= 135.0
    zones = [(-135.0, -90.0, "red"), (-60.0, 60.0, "green"), (90.0, 135.0, "red")]
    for a0, a1, key in zones:
        sel = (ang >= a0) & (ang <= a1)
        _over(col, arc * sel, INK[key])
    tick = band(0.80 * R, 0.90 * R)
    for k in range(11):
        a = -135.0 + 27.0 * k
        d = np.abs(((ang - a + 180.0) % 360.0) - 180.0) * np.pi / 180.0 * r
        line = np.clip(0.5 - (d - 0.004) / aa, 0.0, 1.0).astype(np.float32)
        _over(col, tick * line * inarc, INK["black"])
    _over(col, band(0.0, 0.06 * R), INK["black"])          # the needle hub print
    cap = 0.035
    for word, sx in zip(spec["words"], (-1.0, 1.0)):
        _over(col, _text_cov(word, cap, cx + sx * 0.40 * R, cy - 0.36 * R, 1.0, W, H, w, h),
              INK["black"])
    return {"cap_m": cap, "condense": 1.0, "glyph_m": round(2 * R, 4)}


def board_painter(kind, size_m=None, words=None):
    """A cs2_compose.stamp_on_face painter for `kind`: painter(rect, base_slice, ctl_slice);
    the metrics land on painter.last."""
    def painter(rect, base_sl, ctl_sl):
        painter.last = paint_board(kind, rect, None, base_sl, ctl_sl, size_m=size_m, words=words)
    painter.last = None
    return painter


def board_px(kind, ppm=400.0, size_m=None):
    """(w_px, h_px) of a label of `kind` (or `size_m`) at `ppm` (the sheet item size)."""
    W, H = size_m or BOARDS[kind]["size"]
    return int(round(W * ppm)), int(round(H * ppm))


def board_rgb8(kind, ppm, size_m=None):
    """A label painted alone at `ppm` -> (uint8 sRGB rows TOP-DOWN, metrics): the preview image of
    exactly what paint_board writes (the same code path, any density)."""
    w, h = board_px(kind, ppm, size_m)
    base = np.zeros((h, w, 4), np.float32)
    ctl = np.zeros((h, w, 4), np.float32)
    met = paint_board(kind, (0, h, 0, w), None, base, ctl, size_m=size_m)
    s = cs2_preview._linear_to_srgb(np.clip(base[..., 0:3], 0.0, 1.0))
    return (np.clip(s, 0.0, 1.0) * 255.0 + 0.5).astype(np.uint8)[::-1], met


def render_board_preview(kind, path, long_px=1024, shipped_ppm=None):
    """Write `kind` as a PNG whose longer side is `long_px` (vector paint at that density); with
    `shipped_ppm` the image is the label at that density instead, nearest-upscaled to ~long_px
    (what the atlas carries). Returns the metrics."""
    import ski_launcher_glyph as slg
    W, H = BOARDS[kind]["size"]
    if shipped_ppm is None:
        img, met = board_rgb8(kind, long_px / max(W, H))
    else:
        img, met = board_rgb8(kind, shipped_ppm)
        k = max(1, int(round(long_px / max(img.shape[0], img.shape[1]))))
        img = np.repeat(np.repeat(img, k, 0), k, 1)
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    slg.write_png(path, img)
    return met


# ======================================================================================
# Label sheet (design 2.1 pack_label_sheet; DQ reserved strip, tram packed islands)
# ======================================================================================

def pack_label_sheet(items, region_px, gutter_px=6, align="top"):
    """Deterministic shelf pack. `items` = {name: (w_px, h_px)} or [(name, w, h)];
    `region_px` = (x0, y0, x1, y1) texels (x = column, y = row from the bottom, half-open).
    Items sorted by height descending, then name; filled left to right on shelves `gutter_px`
    apart (between items and between shelves; none at the region edges); shelves stack
    from the region top (align "top") or bottom. Returns {name: (x0, y0, x1, y1)}; raises
    ValueError when an item is wider than the region or the shelves overflow it."""
    if isinstance(items, dict):
        rows = [(str(k), int(v[0]), int(v[1])) for k, v in items.items()]
    else:
        rows = [(str(k), int(a), int(b)) for k, a, b in items]
    if len({r[0] for r in rows}) != len(rows):
        raise ValueError("pack_label_sheet: duplicate item names")
    X0, Y0, X1, Y1 = (int(v) for v in region_px)
    RW, RH = X1 - X0, Y1 - Y0
    rows.sort(key=lambda r: (-r[2], r[0]))
    shelves = []                                  # [height, used_width, [(name, w, h)]]
    for name, w, h in rows:
        if w <= 0 or h <= 0:
            raise ValueError(f"pack_label_sheet: {name} has an empty size {w} x {h}")
        if w > RW:
            raise ValueError(f"pack_label_sheet: {name} ({w} px) wider than the region ({RW} px)")
        if shelves and shelves[-1][1] + gutter_px + w <= RW:
            shelves[-1][1] += gutter_px + w
            shelves[-1][2].append((name, w, h))
        else:
            shelves.append([h, w, [(name, w, h)]])
    total = sum(s[0] for s in shelves) + gutter_px * (len(shelves) - 1)
    if total > RH:
        raise ValueError(f"pack_label_sheet: shelves need {total} px, the region has {RH} px")
    out = {}
    y = Y1 if align == "top" else Y0
    for sh_h, _used, its in shelves:
        x = X0
        for name, w, h in its:
            if align == "top":
                out[name] = (x, y - h, x + w, y)
            else:
                out[name] = (x, y, x + w, y + h)
            x += w + gutter_px
        y = y - sh_h - gutter_px if align == "top" else y + sh_h + gutter_px
    return out


def px_rect_to_uv(rect, A):
    """(x0, y0, x1, y1) texels -> (u0, v0, u1, v1) for an A x A atlas (reserved-rect form)."""
    x0, y0, x1, y1 = rect
    return (x0 / float(A), y0 / float(A), x1 / float(A), y1 / float(A))


def uv_rect_to_px(rect, A):
    """(u0, v0, u1, v1) -> (row0, row1, col0, col1) texel slices (cs2_compose._rect_slices)."""
    sl = cs2_compose._rect_slices(rect, A)
    return (sl[0].start, sl[0].stop, sl[1].start, sl[1].stop)


# ======================================================================================
# Bay sharing (design 2.5, skeptic MC6)
# ======================================================================================

NEVER_SHARE = ("label_", "board_", "signband_", "pictodisc", "pictoplinth", "ctface_", "lamp_")
DQ_SHARE_HERO = ("roof_", "band_", "soffit_", "deck", "plinth_", "ped_", "crosshead_", "spine_")
TRAM_SHARE_HERO = ("roof", "stone", "col_", "strut_", "headbeam_", "plinth")
# design 2.5 whitelists (area <= cap_m2 parts are eligible anyway); share_identical drops
# every prefix with an ACTIVE DetailSpec (MC6: detail_height sums world-anchored relief per
# face, so a stacked copy would draw every copy's seams / bolts / grate holes at its own phase)
DQ_SHARE_WHITELIST = ("beam_", "detrail_", "haulrope_", "gutter_", "bandlining_", "endmull_",
                      "cabmull_")
TRAM_SHARE_WHITELIST = ("glazepanel_", "fin_", "mullion_", "gate_", "brace_", "deckbeam_",
                        "legbrace_", "bullspoke_", "dwspoke_", "drumspoke_", "bwspoke_",
                        "sgpost_", "leg_", "footing_", "svcpost_", "transom_", "annexframe_",
                        "outrigger_", "purlin_", "stairstr_", "edgestrip_")


class ShareList(list):
    """cs2_uv `share` list [(canon_polys, [dup_polys, ...]), ...] with a `.report` dict."""
    report = None


def _relief_active(name, detail_specs):
    if not detail_specs:
        return False
    s = cs2_compose._spec_for(name, detail_specs)
    return s is not None and s.active()


def share_identical(obj, table, whitelist, *, detail_specs=None, never=NEVER_SHARE, hero=(),
                    cap_m2=3.0, tol=0.001, reserved=(), uv_layer=None, normal_tol=0.01,
                    ground_band=None):
    """Bay sharing (design 2.5 as amended by MC6). Groups TRANSLATION-identical parts of the
    joined mesh `obj` (part table `table`): same polygon count, loop counts, material index
    and density weight per polygon, every loop's position relative to the part's bbox min
    within `tol` (1 mm), every loop normal within `normal_tol` (rotated / mirrored copies
    fail this: their tangent frames differ). Eligible parts: (area <= cap_m2 or a
    `whitelist` prefix) and NOT a `never` / `hero` prefix, NOT a prefix whose DetailSpec in
    `detail_specs` is active (MC6), no face pinned by `reserved` rects on `uv_layer`.
    `ground_band` = (ground_z, band_m), e.g. (spec.ground_z, 0.60): a copy whose lowest point
    lies within the weathering ground-contact band only stacks with copies that also do (the
    contact grime is a world-z field, so a deck-level post must not show a grade post's grime).
    Returns a ShareList (canonical = the lowest part index of each group) for
    cs2_uv.pack_unique / cs2_bake.bake_unique(share=...); `.report` = counts and the groups.
    A stacked texel carries the LAST copy's bake sample, AO and weathering (cs2_bake doc)."""
    me = obj.data
    n = len(me.polygons)
    part = np.zeros(n, dtype=np.int32)
    attr = me.attributes.get("part")
    if attr is None or attr.domain != "FACE":
        raise RuntimeError(f"{obj.name}: no 'part' face attribute (cs2_asset.tag_parts)")
    attr.data.foreach_get("value", part)
    start, total = cs2_uv.face_loop_table(me)
    mi = np.empty(n, dtype=np.int32)
    me.polygons.foreach_get("material_index", mi)
    wts = cs2_uv.face_density_weights(me)
    area = cs2_uv.face_world_areas(me)
    co = np.empty(len(me.vertices) * 3, dtype=np.float64)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    lv = np.empty(len(me.loops), dtype=np.int64)
    me.loops.foreach_get("vertex_index", lv)
    try:
        cn = np.empty(len(me.loops) * 3, dtype=np.float32)
        me.corner_normals.foreach_get("vector", cn)
        cn = cn.reshape(-1, 3).astype(np.float64)
    except (AttributeError, TypeError, RuntimeError):
        cn = None
    pinned = set()
    if reserved:
        lay = me.uv_layers.get(uv_layer) if uv_layer else me.uv_layers.active
        pinned = cs2_uv.pinned_faces(me, lay, list(reserved))
    order = np.argsort(part, kind="stable")
    bounds = np.searchsorted(part[order], np.arange(1, len(table) + 2))
    excluded = {"never": [], "hero": [], "relief": [], "size": [], "pinned": []}
    buckets = {}
    for p in range(1, len(table) + 1):
        polys = order[bounds[p - 1]:bounds[p]]
        if not len(polys):
            continue
        nm = table[p - 1]
        if nm.startswith(tuple(never)):
            excluded["never"].append(nm)
            continue
        if hero and nm.startswith(tuple(hero)):
            excluded["hero"].append(nm)
            continue
        if _relief_active(nm, detail_specs):
            excluded["relief"].append(nm)
            continue
        if float(area[polys].sum()) > cap_m2 and not nm.startswith(tuple(whitelist)):
            excluded["size"].append(nm)
            continue
        if pinned and any(int(f) in pinned for f in polys):
            excluded["pinned"].append(nm)
            continue
        polys = np.sort(polys)
        loops = np.concatenate([np.arange(start[f], start[f] + total[f]) for f in polys])
        P = co[lv[loops]]
        rel = P - P.min(axis=0)
        low = None if ground_band is None else \
            bool(float(P[:, 2].min()) <= float(ground_band[0]) + float(ground_band[1]))
        key = (tuple(int(total[f]) for f in polys), tuple(int(mi[f]) for f in polys),
               tuple(round(float(wts[f]), 4) for f in polys), low,
               np.round(rel / max(tol, 1e-6) / 10.0).astype(np.int64).tobytes())
        buckets.setdefault(key, []).append((p, polys, rel, loops))
    share = ShareList()
    groups = []
    for key in sorted(buckets, key=lambda k: buckets[k][0][0]):
        members = buckets[key]
        if len(members) < 2:
            continue
        left = list(members)
        while len(left) > 1:
            cp, cpolys, crel, cloops = left[0]
            dups, rest = [], []
            for m in left[1:]:
                ok = float(np.abs(m[2] - crel).max()) <= tol
                if ok and cn is not None:
                    ok = float(np.abs(cn[m[3]] - cn[cloops]).max()) <= normal_tol
                (dups if ok else rest).append(m)
            if dups:
                share.append(([int(f) for f in cpolys], [[int(f) for f in d[1]] for d in dups]))
                groups.append((table[cp - 1], [table[d[0] - 1] for d in dups]))
            left = rest
    dup_faces = sum(len(d) for _c, ds in share for d in ds)
    share.report = {"groups": len(share), "dup_parts": sum(len(g[1]) for g in groups),
                    "dup_faces": dup_faces,
                    "excluded": {k: len(v) for k, v in excluded.items()},
                    "excluded_relief": sorted(set(re.sub(r"\d+$", "", x) for x in excluded["relief"])),
                    "group_names": groups}
    print(f"[liftdress] share_identical {obj.name}: {len(share)} group(s), "
          f"{share.report['dup_parts']} dup part(s), {dup_faces} dup faces; excluded "
          f"{share.report['excluded']}")
    return share


# ======================================================================================
# Weathering helpers (design 2.6 as amended by skeptic MC7; DECISIONS 171)
# ======================================================================================

NEW_ROLES = {"rope_orange": "generic", "flag_red": "none", "safety_yellow": "paint",
             "safety_black": "paint", "fire_red": "paint", "label": "none", "led": "none",
             "lamp_red": "none", "lamp_green": "none", "lamp_flood": "none",
             # ski gear (cs2_bldg's keys): under 3 texels wide at station densities, left
             # unweathered as the old ski_a / b / c were (the `ski_` prefix override is "none")
             "accent": "none", "trim_white": "none", "ski_black": "none", "ski_blue": "none",
             "ski_red": "none", "rubber": "pad", "darksteel": "paint_steel", "galv": "galv"}
DQ_MAT_ROLES = {"accent3": "paint", "cedar": "timber", "concrete": "masonry",
                "darksteel": "paint_steel", "galv": "galv", "glass": "none", "lamp": "none",
                "pad2": "pad", "paint1": "paint_steel", "rope": "none", "rubber": "pad",
                "signband": "none", "stone": "masonry"}
DQ_MAT_ROLES = {**NEW_ROLES, **DQ_MAT_ROLES}
TRAM_MAT_ROLES = {"concrete": "masonry", "galv": "galv", "steel": "paint_steel",
                  "roof": "paint_metal", "trim": "paint", "stone": "masonry", "rock": "masonry",
                  "timber": "timber", "glass": "none", "rope": "none", "safety": "paint",
                  "lamp": "none", "clockface": "none", "dplate": "galv", "render": "masonry",
                  "wheel": "paint_steel"}
TRAM_MAT_ROLES = {**NEW_ROLES, **TRAM_MAT_ROLES}

STATION_INTENSITY = 0.40        # DECISIONS 171 (applied): the default; raise per asset
STATION_SLOT_WEAR = "free"      # DECISIONS 171: slot surfaces may weather beyond +-10 %
INTENSITY_STEPS = (0.40, 0.45, 0.50, 0.55, 0.60, 0.65, 0.70)
EDGE_BAND_MIN_M = 0.08          # MC7: the as-built cs2_weather default; never below it


def mat_keys_of(mesh):
    """The material keys of a joined mesh in slot (= material index) order: the
    cs2_bake.material_sources placeholders are named CS2SRC_<key>. Read BEFORE bake_unique
    (it swaps in CS2BAKE_ materials, then clears the slots)."""
    keys = []
    for m in mesh.materials:
        nm = m.name if m is not None else ""
        if not nm.startswith(SRC_MAT_PREFIX):
            raise ValueError(f"{mesh.name}: material slot {len(keys)} is {nm!r}, not a "
                             f"{SRC_MAT_PREFIX}<key> placeholder - read the keys before the bake")
        keys.append(nm[len(SRC_MAT_PREFIX):])
    return keys


def lift_mat_roles(mesh_or_keys, roles):
    """{material INDEX: role} (skeptic MC7a: the joined mesh's slots are CS2SRC_<key>, so a
    by-name lookup raises on the first slot). `mesh_or_keys` = the joined mesh (keys from its
    CS2SRC_ slot names, index = slot) or the sorted key list itself (material_sources: index =
    sorted(keys).index(key)). Raises KeyError on a key `roles` does not map."""
    keys = list(mesh_or_keys) if isinstance(mesh_or_keys, (list, tuple)) else mat_keys_of(mesh_or_keys)
    missing = [k for k in keys if k not in roles]
    if missing:
        raise KeyError(f"lift_mat_roles: no weather role for material key(s) {missing}")
    return {i: roles[k] for i, k in enumerate(keys)}


def station_weather_spec(name, *, ground, ground_z=0.0, overrides=(), traffic_extra=(),
                         intensity=STATION_INTENSITY, slot_wear=STATION_SLOT_WEAR, **extra):
    """cs2_weather.WeatherSpec.preset("station", ...) with the DECISIONS 171 defaults:
    intensity 0.40 (raise per asset to its highest budget-clean step: highest_clean_step),
    slot_wear "free", snow 0.0 (no baked snow, entry 76 / 171), edge_band_m left at the
    as-built default 0.08 (MC7c; a smaller value raises). `overrides` = ((prefix, {PartWeather
    fields}), ...); traffic_prefixes = cs2_weather.TRAFFIC_PREFIXES + traffic_extra.
    Returns the validated spec."""
    import cs2_weather as CW
    if "snow" in extra and float(extra["snow"]) != 0.0:
        raise ValueError("station_weather_spec: no baked snow on the lift stations "
                         "(DECISIONS 76 / 171)")
    extra.pop("snow", None)
    eb = extra.get("edge_band_m")
    if eb is not None and float(eb) < EDGE_BAND_MIN_M - 1e-12:
        raise ValueError(f"station_weather_spec: edge_band_m {eb} below the as-built "
                         f"{EDGE_BAND_MIN_M} (3-texel floor reasoning, skeptic MC7c)")
    ov = tuple((str(p), CW.PartWeather(**dict(kw))) for p, kw in overrides)
    return CW.WeatherSpec.preset("station", name, intensity=float(intensity), snow=0.0,
                                 slot_wear=slot_wear, ground=ground, ground_z=float(ground_z),
                                 overrides=ov,
                                 traffic_prefixes=tuple(CW.TRAFFIC_PREFIXES) + tuple(traffic_extra),
                                 **extra)


# Weather overrides for the dressing families (merge into a generator's overrides tuple).
DRESS_WEATHER_OVERRIDES = (
    ("label_", {"role": "none"}), ("board_", {"scale": 0.7}), ("lamp_", {"role": "none"}),
    ("maze_", {"scale": 0.5}), ("skirack_", {"role": "galv"}), ("ski_", {"role": "none"}),
    ("stripe_", {"scuff": 1.5}), ("chev_", {"scuff": 1.0}), ("ext_", {"scale": 0.5}),
    ("hosereel_", {"scale": 0.5}), ("estop_", {"scale": 0.5}), ("gauge_", {"scale": 0.5}),
    ("carpet_", {"role": "pad", "scuff": 1.5}), ("lrail_", {"role": "galv", "edge": 1.5}),
    ("lrung_", {"role": "galv", "scuff": 1.0}), ("flood_", {"scale": 0.7}),
)


def label_protect_px(names):
    """protect_px callable for cs2_weather.make_weather_hook: the UVUnique texel bbox of every
    part named exactly in `names` (labels, LED board, dials) restored 1:1 - the stamp-protect
    rule for stamped faces whose island is NOT a reserved rect (tram labels)."""
    names = tuple(sorted(set(names)))

    def rects(mesh, A):
        table = cs2_compose.read_part_table(mesh)
        part = cs2_compose.part_indices(mesh)
        uvs = cs2_compose.read_uvs(mesh, cs2_uv.UNIQUE_LAYER).astype(np.float64)
        start, total = cs2_uv.face_loop_table(mesh)
        out = []
        for nm in names:
            if nm not in table:
                continue
            faces = np.nonzero(part == table.index(nm) + 1)[0]
            if not len(faces):
                continue
            idx = np.concatenate([np.arange(start[f], start[f] + total[f]) for f in faces])
            q = uvs[idx] * A
            r0, r1 = max(0, int(math.floor(q[:, 1].min()))), min(A, int(math.ceil(q[:, 1].max())))
            c0, c1 = max(0, int(math.floor(q[:, 0].min()))), min(A, int(math.ceil(q[:, 0].max())))
            if r1 > r0 and c1 > c0:
                out.append((r0, r1, c0, c1))
        return out
    return rects


def weather_budget_ok(report):
    """True when a WeatherHook report is inside the vanilla proxy budget (visible texels, no
    WARN): report["vanilla"]["ok"]."""
    return bool(report and report.get("vanilla", {}).get("ok", False))


def highest_clean_step(evaluate, steps=INTENSITY_STEPS):
    """Walk `steps` upward calling evaluate(intensity) -> WeatherHook report; returns
    (highest budget-clean intensity, {intensity: ok}). Stops at the first WARN. Raises
    when even steps[0] WARNs (the asset needs a look, not a lower default)."""
    seen = {}
    best = None
    for s in steps:
        ok = weather_budget_ok(evaluate(float(s)))
        seen[float(s)] = ok
        if not ok:
            break
        best = float(s)
    if best is None:
        raise RuntimeError(f"highest_clean_step: WARN already at {steps[0]}")
    return best, seen


# ======================================================================================
# Vanilla sub-objects (design 1.7 / 3.3 / 4.3 / 5.3 / 6.3; skeptic row 6 + MC10)
# ======================================================================================

DUMP_ZIP = os.path.join(os.environ.get("TEMP", ""), "pltar", "dump.zip")

# Placeholder GUIDs, harvested from the vanilla dump: every variant's
# SpawnableObject.m_Placeholders names the placeholder's GUID (the placeholder prefab file
# carries no GUID of its own: "inferred" in the preflight whitelist sense). bounds = the union
# of the placeholder mesh and every variant mesh found by name (RenderPrefab "<V> Mesh",
# m_Bounds, game local frame, y up); tris = max over those meshes. verify_subobj re-derives
# all of it and raises on any mismatch (tests/test_liftdress.py K7).
SUBOBJ = {
    "TrashbinRandom01": dict(
        guid="41e3c9ba6e1d6b948a6b1bc3499a5397", variants=("Trashbin01", "Trashbin02",
                                                         "Trashbin03", "Trashbin04"),
        components=("QuantityObject", "SpawnableObject"), mount="ground",
        bounds=((-0.246, 0.0, -0.246), (0.246, 1.036, 0.246)), tris=364),
    "GardenBenchRandom01": dict(
        guid="8df0703be09426948b5bc3262d86bc04", variants=("GardenBench01", "GardenBench02",
                                                         "GardenBench03", "GardenBench04"),
        components=("ActivityLocation", "SpawnableObject"), mount="ground",
        bounds=((-1.059, 0.0, -0.496), (1.059, 1.054, 0.496)), tris=906),
    "RoofLightRandom01": dict(
        guid="b0ea4a4915b28f14a826bb809e02db7a", variants=("RoofLight01", "RoofLight02",
                                                         "RoofLight03", "RoofLight04"),
        components=("AttachedObject", "EffectSource", "SpawnableObject", "StreetLightObject"),
        mount="ceiling", bounds=((-0.169, -0.152, -0.169), (0.169, 0.0, 0.169)), tris=380),
    "WallLightShortRangeRandom01": dict(
        guid="cfc12fde01d9da24b86ddedd657eaae1", variants=("WallLightShortRange01",
                                                         "WallLightShortRange02",
                                                         "WallLightShortRange03"),
        components=("AttachedObject", "EffectSource", "SpawnableObject", "StreetLightObject"),
        mount="wall", bounds=((-0.142, -0.058, 0.0), (0.142, 0.058, 0.115)), tris=138),
    "LightpoleParkRandom01": dict(
        guid="c987b53e24a01e744b4a00ee5b1f8a08", variants=("LightpolePark01", "LightpolePark02",
                                                         "LightpolePark03", "LightpolePark04"),
        components=("EffectSource", "SpawnableObject", "StandingObject", "StreetLightObject"),
        mount="ground", bounds=((-0.267, 0.0, -0.235), (0.267, 3.268, 0.833)), tris=1788),
    "WarningLightRandom01": dict(
        guid="6b6861547d4e1964aa977434af734126", variants=("WarningLight01", "WarningLight02"),
        components=("EffectSource", "SpawnableObject"), mount="ground",
        bounds=((-0.104, 0.0, -0.22), (0.104, 1.954, 0.224)), tris=708),
    "ParkInfosignRandom01": dict(
        guid="3f8dafc1558bce24597bd8ca898947fa", variants=("ParkInfosign01", "ParkInfosign02",
                                                         "ParkInfosign03"),
        components=("SpawnableObject",), mount="ground",
        bounds=((-1.212, 0.0, -0.852), (1.212, 2.793, 0.852)), tris=128),
    # a cafe table with its two chairs (ActivityLocation seats at z +-0.75), the base game's
    # commercial table set; the L1 deck umbrellas stand over ten of them (bldg_recipes.L1_TABLES,
    # buildings pass 2026-10-11) and the L2 hut uses the same GUID. Worst variant corner
    # (TableSetCommercial04) 1.566 m from the pivot: inside the L1 canopy rim (r 1.55) but for
    # the bounding-box corner; every variant is <= 1.09 m tall, the rim is at 2.18 m
    "TableSetCommercialRandom01": dict(
        guid="fc058a3d03b337e4794c432a84170316", variants=("TableSetCommercial01",
                                                         "TableSetCommercial02",
                                                         "TableSetCommercial03",
                                                         "TableSetCommercial04",
                                                         "TableSetCommercial05",
                                                         "TableSetCommercial06"),
        components=("ActivityLocation", "SpawnableObject"), mount="ground",
        bounds=((-1.099, -0.013, -1.259), (1.099, 1.088, 1.259)), tris=1636),
    # fine-detail pass (design/fine-detail-pass.md A.1 "to add", verified against the dump
    # 2026-10-12 by the step-2 builder: placeholder carries PlaceholderObject, every variant lists
    # the GUID in its m_Placeholders, bounds / tris = verify_subobj's union, no ForbidSnow /
    # RequireSnow on any variant). REJECTED mixed-spawn placeholders (never use):
    # ebe3622b9123c104783710893194cc55 (Barrel01-03 + CardboardBox + Crate + RubblePile* + Trashbag
    # + Trashpile: 33 variants) and 95cbbe17c442dc940be6e7986155c05d (ElectricalBox01/02 AND
    # EU_ / NA_FireHydrant01: it would spawn hydrants on a deck).
    "BarrelRandom01": dict(
        guid="fb4247f7a6f75b1478d9dd01ac88ce86", variants=("Barrel01", "Barrel02", "Barrel03"),
        components=("SpawnableObject",), mount="ground",
        bounds=((-0.345, 0.0, -0.345), (0.345, 0.864, 0.345)), tris=408),
    "SafetyConeRandom01": dict(
        guid="dc8abeea43367b24ba2668784a309e56", variants=("SafetyCone01", "SafetyCone02",
                                                         "SafetyCone03", "SafetyCone04"),
        components=("SpawnableObject",), mount="ground",
        bounds=((-0.202, 0.0, -0.202), (0.202, 1.082, 0.202)), tris=258),
    # the variants are named FlowerPot / PlantPot, so cs2_subobj.DumpIndex.guid_of (stem match)
    # cannot infer this GUID: it is read from the four variants' m_Placeholders
    "PotRandom01": dict(
        guid="8d8722db083c3564291e3778cceeb1ae", variants=("FlowerPot01", "FlowerPot02",
                                                         "PlantPot01", "PlantPot02"),
        components=("PlantObject", "SpawnableObject"), mount="ground",
        bounds=((-0.549, 0.0, -0.597), (0.56, 1.103, 0.543)), tris=690),
}
#: placeholder GUIDs that spawn unrelated families: refused by sub_object_rows and the fine-detail rows
REJECTED_GUIDS = {"ebe3622b9123c104783710893194cc55": "barrels + crates + rubble + trash (33 variants)",
                  "95cbbe17c442dc940be6e7986155c05d": "ElectricalBox01/02 + EU_/NA_FireHydrant01"}

# game local yaw quaternions (x, y, z, w): the prop's local +Z turned to face ...
FACING_QUAT = {"+Z": (0.0, 0.0, 0.0, 1.0), "-Z": (0.0, 1.0, 0.0, 0.0),
               "+X": (0.0, 0.7071068, 0.0, 0.7071068), "-X": (0.0, -0.7071068, 0.0, 0.7071068)}


def facing_quat(facing, frame="game"):
    """Yaw quaternion (x, y, z, w) turning a vanilla prop's local +Z (its front, unverified
    until placement: design 1.6) to `facing`, given in the game frame ("+X" "-X" "+Z" "-Z")
    or the Blender authoring frame (frame="blender": "+X" travel -> game +Z, "+Y" -> game -X, "-Y" -> game +X:
    game x = -authoring y, x-sign gate N; the same table as lift_terminal.M_FACING_TO_GAME)."""
    if frame == "blender":
        facing = {"+X": "+Z", "-X": "-Z", "+Y": "-X", "-Y": "+X"}[facing]
    return FACING_QUAT[facing]


def _yaw_of(q):
    x, y, z, w = (float(v) for v in q)
    if abs(x) > 1e-6 or abs(z) > 1e-6:
        raise ValueError(f"sub-object rotation {q} is not a pure yaw about game +Y")
    return 2.0 * math.atan2(y, w)


def subobj_box(prefab, pos, rot=(0.0, 0.0, 0.0, 1.0), frame="game", pad=0.0):
    """AABB (lo, hi) of a vanilla sub-object's variant-union bounds placed at `pos` (game
    local) with yaw `rot`, grown by `pad`; frame="blender" returns it in the authoring frame
    (for the generators' box tests, MC10)."""
    s = SUBOBJ[prefab]
    lo, hi = (np.array(v, dtype=np.float64) for v in s["bounds"])
    a = _yaw_of(rot)
    c, sn = math.cos(a), math.sin(a)
    corners = np.array([[x, y, z] for x in (lo[0], hi[0]) for y in (lo[1], hi[1])
                        for z in (lo[2], hi[2])])
    # rotation about +Y: x' = x cos + z sin, z' = -x sin + z cos
    rx = corners[:, 0] * c + corners[:, 2] * sn
    rz = -corners[:, 0] * sn + corners[:, 2] * c
    P = np.stack([rx, corners[:, 1], rz], axis=1) + np.asarray(pos, dtype=np.float64)
    blo, bhi = P.min(axis=0) - pad, P.max(axis=0) + pad
    if frame == "blender":
        # game_to_blender negates gx (authoring y = -gx), so the lo / hi corners swap on that axis
        a, b = np.array(game_to_blender(blo)), np.array(game_to_blender(bhi))
        blo, bhi = np.minimum(a, b), np.maximum(a, b)
    return blo, bhi


#: the station rows' frame (lift_terminal_detach / tram_station: blender_to_game), written into a fine block
STATION_X_CONVENTION = ("game (gx, gy, gz) = (-y, z, x) of authoring; gx NEGATED (x-sign gate settled N, "
                        "2026-10-15 playtest; cs2_liftdress.blender_to_game = lift_terminal.m_to_game; the tram's "
                        "design frame goes through tram_to_game = (-X, Y, Z)); a +Y facing is game -X")


def sub_object_rows(target_prefab, rows, *, fine=False, x_convention=None):
    """Manifest block for the surgery round: {"target_prefab", "frame": "game local",
    "objects": [{prefab, guid, pos, rot, parent}]}. `rows` = [(prefab, pos, rot, parent), ...]
    or dicts with those keys; prefab must be a SUBOBJ key; pos / rot game local.
    fine=True (fine-detail pass step 3b, the station row path; opt-in - fine=False returns this block
    byte for byte as before and refuses mod rows): `rows` may also hold OUR OWN props, the dicts
    fine_detail.kit_row / kit_lean_row build ({"kind": "mod", "m_Object": <32-hex object-prefab CID>,
    "prefab", "mesh", "position", "rotation", "m_ParentMesh"[, "lean", "note", "role", "colour_set", "variant"]});
    a colored kit prop's CID is its color-set variant's (build/kit-colour-variants.md); each is written
    {"kind": "mod", "ref": "CID", "prefab", "mesh", "guid": "CID:<cid>", "pos", "rot", "parent"[, ...]}
    (the surgery pastes $fstrref:"<guid>" for both kinds; cs2_subobj reads the CID from "guid"), and the
    block gains "x_convention" (default STATION_X_CONVENTION) and "kinds" {vanilla, mod}."""
    objs = []
    n_mod = 0
    for r in rows:
        if isinstance(r, dict) and r.get("kind") == "mod":
            if not fine:
                raise KeyError(f"sub_object_rows: mod row {r.get('prefab')!r} needs fine=True")
            cid = str(r.get("m_Object", ""))
            if not re.match(r"^[0-9a-f]{32}$", cid):
                raise ValueError(f"sub_object_rows: mod row {r.get('prefab')!r} CID {cid!r} is not 32 hex")
            pos, rot, parent = r["position"], r["rotation"], int(r.get("m_ParentMesh", 0))
            _yaw_of(rot)
            if parent not in (-1, 0):
                raise ValueError(f"sub_object_rows: mod row {r.get('prefab')!r} m_ParentMesh {parent}")
            o = {"kind": "mod", "ref": "CID", "prefab": str(r["prefab"]), "mesh": str(r["mesh"]),
                 "guid": "CID:" + cid, "pos": [round(float(v), 4) for v in pos],
                 "rot": [round(float(v), 7) for v in rot], "parent": parent}
            for k in ("role", "lean", "note", "colour_set", "variant"):    # + the kit color variant (2026-10-12)
                if k in r:
                    o[k] = r[k]
            objs.append(o)
            n_mod += 1
            continue
        if isinstance(r, dict):
            pf, pos, rot, parent = r["prefab"], r["pos"], r.get("rot", (0, 0, 0, 1)), r.get("parent", 0)
        else:
            pf, pos, rot, parent = r
        if pf not in SUBOBJ:
            raise KeyError(f"sub_object_rows: {pf!r} is not a verified SUBOBJ placeholder")
        if SUBOBJ[pf]["guid"] in REJECTED_GUIDS:
            raise ValueError(f"sub_object_rows: {pf!r} uses a rejected mixed-spawn GUID")
        _yaw_of(rot)
        objs.append({"prefab": pf, "guid": "UnityGUID:" + SUBOBJ[pf]["guid"],
                     "pos": [round(float(v), 4) for v in pos],
                     "rot": [round(float(v), 7) for v in rot], "parent": int(parent)})
    block = {"target_prefab": str(target_prefab), "frame": "game local", "objects": objs}
    if fine:
        block["x_convention"] = str(x_convention) if x_convention else STATION_X_CONVENTION
        block["kinds"] = {"vanilla": len(objs) - n_mod, "mod": n_mod}
    return block


_PH_RE = re.compile(r'"m_Placeholders"\s*:\s*\{.*?"\$rcontent"\s*:\s*\[(.*?)\]', re.S)
_BOUNDS_RE = re.compile(r'"m_Bounds".*?"min".*?"x":\s*([-0-9.Ee]+),\s*"y":\s*([-0-9.Ee]+),\s*'
                        r'"z":\s*([-0-9.Ee]+).*?"max".*?"x":\s*([-0-9.Ee]+),\s*"y":\s*'
                        r'([-0-9.Ee]+),\s*"z":\s*([-0-9.Ee]+)', re.S)


def verify_subobj(dump_zip=DUMP_ZIP, consumers=True):
    """Re-derive SUBOBJ from the vanilla prefab dump (LZMA zip, read with this Python):
    each GUID's variants (StaticObjectPrefabs whose m_Placeholders name it), the placeholder
    prefab (exists, has PlaceholderObject), the variants' components, the union bounds and
    max tris of the placeholder + variant meshes, and (consumers=True) the number of base-game
    BuildingPrefabs placing the GUID. Raises AssertionError on any mismatch with SUBOBJ;
    returns the derived dict."""
    z = zipfile.ZipFile(dump_zip)
    names = z.namelist()
    static = {n.split("/")[2]: n for n in names
              if n.startswith("00_BaseGame/Game.Prefabs.StaticObjectPrefab/") and n.endswith(".Prefab")}
    render = {n.split("/")[2]: n for n in names
              if n.startswith("00_BaseGame/Game.Prefabs.RenderPrefab/") and n.endswith(".Prefab")}
    gtov = {}
    for k in sorted(static):
        t = z.read(static[k]).decode("utf-8", "replace")
        m = _PH_RE.search(t)
        if m:
            for g in re.findall(r"UnityGUID:([0-9a-f]{32})", m.group(1)):
                gtov.setdefault(g, []).append(k)
    cons = {}
    if consumers:
        want = {s["guid"]: pf for pf, s in SUBOBJ.items()}
        for n in names:
            if not (n.startswith("00_BaseGame/Game.Prefabs.BuildingPrefab/") and n.endswith(".Prefab")):
                continue
            t = z.read(n).decode("utf-8", "replace")
            for g, pf in want.items():
                if g in t:
                    cons[pf] = cons.get(pf, 0) + 1
    out, bad = {}, []
    for pf, s in SUBOBJ.items():
        vs = tuple(sorted(gtov.get(s["guid"], [])))
        if pf not in static:
            bad.append(f"{pf}: no placeholder prefab in the dump")
            continue
        ph = z.read(static[pf]).decode("utf-8", "replace")
        is_ph = "Game.Prefabs.PlaceholderObject, Game" in ph
        comps = set()
        for v in vs:
            comps.update(re.findall(r'"\$type": "\d+\|Game\.Prefabs\.(\w+), Game",\s*"name"',
                                    z.read(static[v]).decode("utf-8", "replace")))
        lo, hi, tris = None, None, 0
        for rn in (pf,) + vs:
            if rn + " Mesh" not in render:
                continue
            t = z.read(render[rn + " Mesh"]).decode("utf-8", "replace")
            b = [float(x) for x in _BOUNDS_RE.search(t).groups()]
            ic = int(re.search(r'"m_IndexCount":\s*(\d+)', t).group(1))
            lo = np.array(b[:3]) if lo is None else np.minimum(lo, b[:3])
            hi = np.array(b[3:]) if hi is None else np.maximum(hi, b[3:])
            tris = max(tris, ic // 3)
        d = {"variants": vs, "placeholder": is_ph, "components": sorted(comps),
             "bounds": (tuple(round(float(v), 3) for v in lo), tuple(round(float(v), 3) for v in hi)),
             "tris": tris, "consumers": cons.get(pf)}
        out[pf] = d
        if vs != tuple(s["variants"]):
            bad.append(f"{pf}: variants {vs} != {s['variants']}")
        if not is_ph:
            bad.append(f"{pf}: the prefab carries no PlaceholderObject")
        miss = [c for c in s["components"] if c not in comps]
        if miss:
            bad.append(f"{pf}: components {miss} missing in the variants")
        for want_b, got_b in zip(s["bounds"], d["bounds"]):
            if any(abs(a - b) > 0.0015 for a, b in zip(want_b, got_b)):
                bad.append(f"{pf}: bounds {d['bounds']} != {s['bounds']}")
                break
        if tris != s["tris"]:
            bad.append(f"{pf}: tris {tris} != {s['tris']}")
    if bad:
        raise AssertionError("verify_subobj: " + "; ".join(bad))
    return out


__all__ = [
    "maze", "swing_gate", "board", "flat_label", "stripe", "chevrons", "carpet", "extinguisher",
    "hose_reel", "tools", "ski_rack", "aframe_rack", "estop_post", "flood_mast", "fixed_ladder", "hatch_frame",
    "bridge", "beacon", "crane_hook", "gauge", "paint_board", "board_painter", "board_px",
    "pack_label_sheet", "px_rect_to_uv", "uv_rect_to_px", "share_identical", "lift_mat_roles", "lod_split",
    "mat_keys_of", "station_weather_spec", "label_protect_px", "weather_budget_ok",
    "highest_clean_step", "verify_subobj", "sub_object_rows", "subobj_box", "facing_quat",
    "REJECTED_GUIDS",
    "merge_materials", "part_keys", "game_to_blender", "blender_to_game", "tram_point", "tram_to_game", "rule_t_pos", "rule_t_rot",
    "tram_facing", "DRESS_MATERIALS", "BOARDS", "PICTOS", "SUBOBJ", "DQ_MAT_ROLES",
    "TRAM_MAT_ROLES", "NEW_ROLES", "DRESS_LOD1_DELETE", "DRESS_LOD1_KEEP", "DRESS_LOD2_DELETE",
    "DRESS_DENSITY", "DRESS_CONTACTS", "DRESS_WEATHER_OVERRIDES", "DQ_SHARE_WHITELIST",
    "TRAM_SHARE_WHITELIST", "DQ_SHARE_HERO", "TRAM_SHARE_HERO", "NEVER_SHARE",
]
