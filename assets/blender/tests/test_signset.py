"""
test_signset - contracts of the fine-detail engine's sign sets and rows (cs2_signset.py, fine_detail.py,
build/fine-detail-step2-engine.md, design/fine-detail-pass.md C / G.2 SG1-SG6 + skeptic FS12 SG7). Every gate runs on the
real school build AND on a mutated input that must fail.

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_signset.py [-- --quick]

SHEET (cs2_signset.SHEET / paint_sheet)

- SS1 layout: rects lie inside the 2048^2 sheet, with >= 6 px gutters and no overlap, size x 300 px/m (negatives: an
  overlapping copy, a rect 3 px from a neighbor, one off the sheet).
- SS2 vector only: paint_sheet succeeds with cs2_bldg._font5x7 trapped (raises if called) (negative: the 5x7 draw_text on
  a canvas trips the same trap).
- SS3 stroke: every word / number has a stroke >= 4 px. SKI SCHOOL and RESTAURANT are tier T1 (negative: a 0.10 m cap at
  300 px/m reports < 4 px).
- SS4 frozen: layout_hash of the frozen prefix = PINNED. Appending a kind keeps the prefix hash. Moving a rect changes
  it.
- SS5 emissive: lit texels lie only inside lit painted rects. Every lit painted rect has lit texels. Reserved rects are
  blank (negative: a stray lit texel is found).
- SS6 ControlMask: 0 everywhere (negative: one tinted texel is found).
- SS7 determinism: two paints hash identically (full mode).
- SS8 typography: nitpick C (2026-10-13). The font table's kinds are painted in their installed typefaces (Bookman Old
  Style Bold: GRAND LODGE with lozenges + rule, SKI AREA. Franklin Gothic Demi Cond: SKI SCHOOL, RESTAURANT, SKI PATROL x
  2, the school markings). The font files are present and sha1-pinned. No lit Bfont name board is left. The thinner
  stroke is >= 4 px (negatives: a changed font file and a missing one raise).
- SS9 blank: hut.picto is a blank board (flat inside the trim rim, unlit) (negative: the fork-and-knife).
- SS10 no fonts: no TTF / OTF / TTC under assets/ (the fonts are rendered, never shipped).

SIGN SET on the real school (lodge.py blockout, the shipping recipe)

- SG1: each face's UV bbox = its kind rect +-0.5 texel (negative: one UV shifted 1 texel).
- SG2: each sign-set board is within 1 mm of the PRE-CHANGE host's own label quad (`--no-fine` build) (negative: a corner
  moved 2 mm).
- SG3: no routed face in the host's LOD0 / LOD1b / LOD2b part tables or labels block (negative: a table holding one).
- SG4: exactly one identity signset row in the host manifest (negatives: rotated, parent -1, doubled).
- SG5: sign-set game bounds inside the school's lot (recipe: 4 x 5 since school revision 2) (negative: a 2 x 1 lot).
- SG6: tris <= 400 (negative: 201 quads through build_sign_set = 402 tris).
- SG7: cs2_asset.write_settings default output is byte-identical on every shipped settings.json that is a default output
  (sharers skipped, listed). shared_from writes the owner keys. shared_from + LODs raises (negative: a patched
  write_settings is caught).

ROWS

- KIT: fine_detail.KIT CIDs = the staged .cid files (negative: a flipped digit is caught).
- LEAN: kit_lean_row puts the rest plane ON the wall: tools 0.3925 m out (the foot-line rule), akja, spine board
  (negative: a row moved 1 cm reports 0.01 m).
- REJ: cs2_liftdress.sub_object_rows refuses the two rejected mixed-spawn GUIDs. SUBOBJ holds the three fine-detail
  placeholders with the verified GUIDs. The BUILDING path does the same (step 3a): lodge.sub_objects_block passes the
  school and raises on a rejected GUID, an unverified GUID (not in SUBOBJ / subobj_bounds.json) and a kit row with a
  foreign CID (negatives).
- EFF: fine_detail.effective returns a plain Recipe unchanged (identity). --no-fine restores the legacy sub_objects and
  drops the sign set. --no-signset keeps the fine rows.

The test exits 1 on any failure and prints one PASS line on success. It writes only to a temp folder.
"""

import copy
import glob
import hashlib
import json
import os
import shutil
import sys
import tempfile
import time
import traceback
from dataclasses import replace

import bpy
import numpy as np

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REPO = os.path.normpath(os.path.join(HERE, os.pardir, os.pardir))
sys.path.insert(0, HERE)
import bldg_recipes as R      # noqa: E402
import cs2_asset              # noqa: E402
import cs2_bldg as K          # noqa: E402
import cs2_liftdress as LD    # noqa: E402
import cs2_signset as SS      # noqa: E402
import fine_detail as FD      # noqa: E402
import lodge                  # noqa: E402

QUICK = "--quick" in sys.argv
FAILS = []
CHECKS = []
# : SS4 - the frozen prefix (the first 20 kinds = the 2026-10-12 table). A later append adds a new (n, hash) pair here.
# These two never change while the sheet owner letter is B.
# : 22: school revision 2 (2026-10-12, build/school-rev2.md) appended school.markstaff / school.marknosled (the red
# markings) in the last shelf's free space. The 20-prefix is unchanged.
# : 24: fine-detail step 3d (2026-10-12, build/fine-detail-step3d-patrol-sign.md) painted the reserved P1 / P2 rects in
# place (state / draws only: the 20- and 22-prefixes are unchanged). It appended gate.header / gate.flake (the welcome
# sign's SKI AREA lettering panel and its snowflake end panels) in free space.
PINNED = {20: "2c01662b97fccb4c", 22: "6107f478878da5e4", 24: "afee251d75da1b40"}


def ok(cond, msg):
    if not cond:
        FAILS.append(msg)
        print(f"[test_signset] FAIL {msg}")
    else:
        CHECKS.append(msg)
        print(f"[test_signset] ok   {msg}")
    return cond


# ======================================================================================
# SHEET
# ======================================================================================

def ss1_layout():
    bad = SS.check_layout()
    ok(not bad, f"SS1 layout: {len(SS.SHEET)} rects inside 2048^2, gutters >= 6 px, 300 px/m ({bad[:3]})")
    k0 = SS.SHEET[0]
    over = SS.SHEET + (replace(k0, key="neg.overlap"),)
    close = SS.SHEET + (SS.Kind("neg.close", "neg", (0.30, 0.30),
                                (k0.rect[2] + 3, k0.rect[1], k0.rect[2] + 93, k0.rect[1] + 90), "reserved"),)
    off = SS.SHEET + (SS.Kind("neg.off", "neg", (0.30, 0.30), (2000, 2000, 2090, 2090), "reserved"),)
    ok(SS.check_layout(over) and SS.check_layout(close) and SS.check_layout(off),
       "SS1 negatives: overlap, a 3 px gap and an off-sheet rect are all reported")
    ok(SS.free_rows() >= 200, f"SS1 free rows above the layout for appends: {SS.free_rows()}")


def ss4_frozen():
    n = min(PINNED)
    h = SS.layout_hash(n=n)
    if PINNED[n] == "pinned-at-first-run":
        print(f"[test_signset] SS4 pin: layout_hash(n={n}) = {h}")
    else:
        ok(h == PINNED[n], f"SS4 frozen layout hash {h} == {PINNED[n]}")
    for m, hm in sorted(PINNED.items()):
        if m != n and m <= len(SS.SHEET):
            ok(SS.layout_hash(n=m) == hm, f"SS4 appended prefix ({m} kinds) hash {SS.layout_hash(n=m)} == {hm}")
    # free space of the last shelf right of the school revision-2 markings (x 1120.., rows 1616..)
    app = SS.SHEET + (SS.Kind("neg.append", "neg", (0.30, 0.30), (1120, 1616, 1210, 1706), "reserved"),)
    moved = (replace(SS.SHEET[0], rect=(9, 834, 1269, 1104)),) + SS.SHEET[1:]
    ok(SS.layout_hash(app, n=n) == h and not SS.check_layout(app),
       "SS4 append-only: a kind appended in free space keeps the frozen prefix hash and the layout valid")
    ok(SS.layout_hash(moved, n=n) != h, "SS4 negative: a rect moved by 1 px changes the frozen hash")
    return h


class _Trap(Exception):
    pass


def _trap(*_a, **_k):
    raise _Trap("the 5x7 font was called")


def paint_trapped():
    orig = K._font5x7
    K._font5x7 = _trap
    try:
        t0 = time.perf_counter()
        maps, metrics = SS.paint_sheet()
        dt = time.perf_counter() - t0
        # negative: the 5x7 path trips the trap
        base = np.zeros((40, 200, 4), np.float32)
        cv = SS._canvas(base, (0, 0, 200, 40), (2.0, 0.4), {})
        tripped = False
        try:
            K.draw_text(("AB",))(cv)
        except _Trap:
            tripped = True
    finally:
        K._font5x7 = orig
    ok(True, f"SS2 vector only: the whole sheet painted with cs2_bldg._font5x7 trapped ({dt:.1f} s)")
    ok(tripped, "SS2 negative: cs2_bldg.draw_text (5x7) on a canvas trips the trap")
    return maps, metrics


def ss3_strokes(metrics):
    words = []
    for key, m in metrics.items():
        for d in m.get("draws", []):
            if d.get("kind") in ("text", "bibs"):
                words.append((key, d["stroke_px"], d.get("tier"), d["cap_m"]))
    low = [w for w in words if w[1] < 4.0]
    ok(words and not low, f"SS3 stroke: {len(words)} word / number draws, min stroke "
                          f"{min(w[1] for w in words):.2f} px (>= 4) ({low})")
    tiers = {w[0]: w[2] for w in words}
    ok(tiers.get("school.name") == "T1" and tiers.get("hut.name") == "T1",
       f"SS3 name boards T1: SKI SCHOOL {tiers.get('school.name')}, RESTAURANT {tiers.get('hut.name')}")
    base = np.zeros((60, 300, 4), np.float32)
    last = {}
    cv = SS._canvas(base, (0, 0, 300, 60), (1.0, 0.2), last)
    SS.draw_text_vec(("SLOW",), cap_frac=0.3)(cv)
    d = last["draws"][0]
    ok(d["stroke_px"] < 4.0, f"SS3 negative: a {d['cap_m']} m cap at 300 px/m strokes {d['stroke_px']} px (< 4)")


def ss5_ss6(maps):
    emi = maps["Emissive"][0][..., :3].max(axis=-1)
    ctl = maps["ControlMask"][0]
    lit_mask = np.zeros(emi.shape, bool)
    for k in SS.SHEET:
        x0, y0, x1, y1 = k.rect
        if k.lit and k.state == "painted":
            lit_mask[y0:y1, x0:x1] = True
            ok(int((emi[y0:y1, x0:x1] > 0.5).sum()) > 1000, f"SS5 {k.key}: lit letters present")
        if k.state == "reserved":
            b = maps["BaseColor"][0][y0:y1, x0:x1, :3]
            ok(float(b.max() - b.min()) < 1e-6 and float(emi[y0:y1, x0:x1].max()) == 0.0,
               f"SS5 {k.key}: reserved rect blank")
    stray = int(((emi > 0.0) & ~lit_mask).sum())
    ok(stray == 0, f"SS5 emissive only inside lit painted rects ({stray} stray)")
    e2 = maps["Emissive"][0].copy()
    e2[2047, 2047, 0] = 1.0
    stray2 = int(((e2[..., :3].max(axis=-1) > 0.0) & ~lit_mask).sum())
    ok(stray2 == 1, "SS5 negative: one stray lit texel is found")
    ok(float(np.abs(ctl).max()) == 0.0, "SS6 ControlMask 0 everywhere (signs are never tinted)")
    c2 = ctl.copy()
    c2[10, 10, 1] = 0.5
    ok(float(np.abs(c2).max()) > 0.0, "SS6 negative: one tinted texel is found")


# ======================================================================================
# Nitpick C (2026-10-13, build/nitpick-c-lodges.md): real typefaces, blank boards, no font file in the repo
# ======================================================================================

# : the font table (kind -> the typeface it must be painted in). Every lettered NAME board leaves Bfont
NC_FACES = {"l5.name": "bookman_bold", "gate.header": "bookman_bold", "school.name": "franklin_demi_cond",
            "hut.name": "franklin_demi_cond", "p1.name": "franklin_demi_cond", "p2.name": "franklin_demi_cond",
            "school.markstaff": "franklin_demi_cond", "school.marknosled": "franklin_demi_cond"}
NC_BLANK = ("hut.picto",)


def ss8_typography(metrics):
    """SS8: each kind of the font table is painted in its typeface (manifest metrics name it, its pinned file resolves).
    Every lit painted kind is a typeface (no Bfont name board is left). Strokes are >= 4 px on the THINNER of stem and
    hairline. The GRAND LODGE board carries its two lozenges, and the lettering is letter-spaced. Negatives: a typeface
    whose pinned sha1 differs raises, and a missing file raises."""
    bad = []
    for key, face in NC_FACES.items():
        tf = SS.typeface(face)
        d = [x for x in metrics[key].get("draws", []) if x.get("kind") == "text"]
        if len(d) != 1 or d[0].get("font") != tf.name or d[0].get("font_file") != tf.file:
            bad.append((key, d[0].get("font") if d else None))
            continue
        if min(d[0]["stem_px"], d[0]["thin_px"]) < 4.0 or d[0]["stroke_px"] != min(d[0]["stem_px"], d[0]["thin_px"]):
            bad.append((key, "stroke", d[0]["stem_px"], d[0]["thin_px"]))
        SS.typeface_path(tf)                     # raises when the pinned file is missing / changed
    lit_bfont = [k.key for k in SS.SHEET if k.lit and k.state == "painted" and
                 any(x.get("font") == "Bfont vector" for x in metrics[k.key].get("draws", []))]
    ok(not bad and not lit_bfont, f"SS8 typography: {len(NC_FACES)} kinds in their typefaces "
       f"({sorted(set(NC_FACES.values()))}), files present + sha1 pinned, strokes >= 4 px ({bad}); lit Bfont "
       f"name boards left: {lit_bfont}")
    l5 = [x for x in metrics["l5.name"]["draws"] if x.get("kind") == "text"][0]
    ok((l5.get("ornament") or {}).get("kind") == "diamond" and l5["tracking_cap"] > 0
       and any(x.get("kind") == "frame" for x in metrics["l5.name"]["draws"]),
       f"SS8 GRAND LODGE: lozenges {l5.get('ornament')}, tracking {l5['tracking_cap']} cap, inner rule, cap "
       f"{l5['cap_m']} m, stem {l5['stem_px']} / thin {l5['thin_px']} px")
    wrong = replace(SS.typeface("bookman_bold"), key="neg_sha", sha1="0" * 16)
    gone = replace(SS.typeface("bookman_bold"), key="neg_file", file="NoSuchFont-NitpickC.ttf")
    raised = []
    for tf, exc in ((wrong, RuntimeError), (gone, FileNotFoundError)):
        try:
            SS.typeface_path(tf)
        except exc:
            raised.append(tf.key)
    ok(raised == ["neg_sha", "neg_file"], f"SS8 negatives: a changed font file ({raised}) and a missing one raise")


def ss9_blank(maps, metrics):
    """SS9: hut.picto is a BLANK board: inside the rim the rect is one flat color (no pictogram), the rim is the hut trim,
    and nothing is emissive. Negative: the old fork-and-knife draw on the same rect leaves pictogram texels."""
    base = maps["BaseColor"][0]
    emi = maps["Emissive"][0][..., :3].max(axis=-1)
    for key in NC_BLANK:
        k = SS.kind(key)
        x0, y0, x1, y1 = k.rect
        d = [x for x in metrics[key]["draws"] if x.get("kind") == "blank"]
        pad = int(round((d[0]["inset_m"] + d[0]["rim_m"]) * SS.SHEET_PPM)) + 2 if d else 0
        inner = base[y0 + pad:y1 - pad, x0 + pad:x1 - pad, :3]
        flat = float((inner.max(axis=(0, 1)) - inner.min(axis=(0, 1))).max()) if inner.size else 1.0
        ok(len(d) == 1 and flat < 1e-6 and float(emi[y0:y1, x0:x1].max()) == 0.0 and d[0]["rim_px"] >= 4.0,
           f"SS9 {key}: blank board (inside the rim one color, spread {flat:.2e}), rim {d[0]['rim_px'] if d else None} "
           f"px in the trim color {d[0]['rim'] if d else None}, unlit")
    k = SS.kind(NC_BLANK[0])
    old = replace(k, draws=(("pictogram", {"kind": "restaurant", "square": True, "bg": SS.BOARD_BROWN}),))
    m2, _ = SS.paint_sheet((old,))
    x0, y0, x1, y1 = k.rect
    b2 = m2["BaseColor"][0][y0 + 20:y1 - 20, x0 + 20:x1 - 20, :3]
    ok(float((b2.max(axis=(0, 1)) - b2.min(axis=(0, 1))).max()) > 0.1, "SS9 negative: the fork-and-knife draw is not flat")


def ss10_no_font_files():
    """SS10: the Windows TTFs are rendered, never shipped: no font file anywhere under assets/."""
    found = [p for ext in ("ttf", "otf", "ttc", "TTF", "OTF", "TTC")
             for p in glob.glob(os.path.join(REPO, "assets", "**", f"*.{ext}"), recursive=True)]
    ok(not found, f"SS10 no font file under assets/ ({found[:3]})")


def _hash_maps(maps):
    return {k: hashlib.sha1(np.ascontiguousarray(v[0]).tobytes()).hexdigest()[:12] for k, v in maps.items()}


# ======================================================================================
# SIGN SET on the real school
# ======================================================================================

def blockout(args_extra, tmp, tag):
    recipe = R.get_recipe("school")
    args = lodge.parse_args(["--school", "--blockout", "--out", os.path.join(tmp, tag, "A")] + args_extra)
    res = lodge.run(recipe, args)
    obj = bpy.data.objects[res["name"]]
    return res, obj


def faces_game(obj, names):
    """{part name: (4, 3) game coords} of single-quad parts in a joined, net-oriented mesh."""
    me = obj.data
    table = json.loads(me[cs2_asset.PART_TABLE_PROP])
    pv = np.zeros(len(me.polygons), np.int32)
    me.attributes["part"].data.foreach_get("value", pv)
    G = SS.game_coords(obj)
    out = {}
    for p in me.polygons:
        n = table[pv[p.index] - 1]
        if n in names:
            out[n] = G[list(p.vertices)]
    return out


def sign_set_checks(tmp):
    spec = R.get_recipe("school").sign_set
    res, host = blockout([], tmp, "fine")
    sobj = res["sign_set"]["obj"]
    names = spec.face_names()
    # SG1
    bad = SS.sg1_uv_in_rect(sobj, spec)
    ok(not bad, f"SG1 {len(names)} faces: UV bbox = sheet rect +-0.5 texel ({bad[:2]})")
    lay = sobj.data.uv_layers["UVMap"].data
    loops = list(sobj.data.polygons[0].loop_indices)
    keep = [tuple(lay[li].uv) for li in loops]
    for li, (u, v) in zip(loops, keep):
        lay[li].uv = (u + 1.0 / SS.SHEET_A, v)
    ok(bool(SS.sg1_uv_in_rect(sobj, spec)), "SG1 negative: one face's UVs shifted 1 texel is reported")
    for li, uv in zip(loops, keep):
        lay[li].uv = uv
    # SG3 (LOD0 + blockout LOD1 / LOD2 tables) + SG4 / SG5 / SG6 on the blockout manifest
    me = host.data
    table = json.loads(me[cs2_asset.PART_TABLE_PROP])
    (t1, t2), _plan = lodge.plan_lod_targets(host, table, R.get_recipe("school"))
    d1, d2 = lodge.delete_lists(R.get_recipe("school"), table)
    lods, _inf = K.lod_chain(host, "SGchk", (t1, t2), (d1, d2), R.get_recipe("school").ground_prefixes)
    tables = {"LOD0": table}
    for i, lod in enumerate(lods, start=1):
        pv = np.zeros(len(lod.data.polygons), np.int32)
        lod.data.attributes["part"].data.foreach_get("value", pv)
        tables[f"LOD{i}b"] = [table[v - 1] for v in sorted(set(pv.tolist()))]
    man = json.load(open(os.path.join(res["asset_dir"], f"{res['name']}.manifest.json"), encoding="utf-8"))
    ok(not SS.sg3_no_routed_left(tables, man.get("labels") or {}, spec),
       f"SG3 no routed face in LOD0 / LOD1b / LOD2b ({[len(v) for v in tables.values()]} parts) or labels")
    ok(bool(SS.sg3_no_routed_left({"LOD0": table + [names[0]]}, {}, spec)),
       "SG3 negative: a table holding sign_school_face is reported")
    for lod in lods:
        m = lod.data
        bpy.data.objects.remove(lod, do_unlink=True)
        bpy.data.meshes.remove(m)
    block = man["sub_objects"]
    cid = res["sign_set"]["cid"]
    ok(not SS.sg4_identity_row(block, spec, cid), f"SG4 one identity signset row (CID {cid[:8]})")
    b2 = copy.deepcopy(block)
    row = [o for o in b2["objects"] if o.get("role") == "signset"][0]
    row["rotation"] = [0.0, 1.0, 0.0, 0.0]
    b3 = copy.deepcopy(block)
    [o for o in b3["objects"] if o.get("role") == "signset"][0]["m_ParentMesh"] = -1
    b4 = copy.deepcopy(block)
    b4["objects"].append(copy.deepcopy([o for o in block["objects"] if o.get("role") == "signset"][0]))
    ok(all(SS.sg4_identity_row(b, spec, cid) for b in (b2, b3, b4)),
       "SG4 negatives: rotated, parent -1 and a doubled row are each reported")
    bounds = SS.bounds_game(sobj)
    lw, ld = R.get_recipe("school").lot          # school revision 2: 4 x 5 (was 4 x 2)
    ok(not SS.sg5_in_lot(bounds, (4.0 * lw, 4.0 * ld)),
       f"SG5 sign-set bounds {bounds} inside the {lw} x {ld} lot +-{4.0 * lw} x +-{4.0 * ld}")
    ok(bool(SS.sg5_in_lot(bounds, (8.0, 4.0))), "SG5 negative: a 2 x 1 lot is reported")
    n = cs2_asset.tri_count(sobj)
    ok(not SS.sg6_tris(n), f"SG6 tris {n} <= {SS.SG6_CAP}")
    # SG6 negative through the builder: 201 door-plate quads
    fake = []
    for i in range(201):
        o = K.pane_quad(f"neg_face{i}", K.WallRef("+X", 4.0), 0.3 * i, 0.3 * i + 0.3, 1.0, 1.3)[0][0]
        fake.append((o, "label"))
    fspec = FD.SignSetSpec("NegSet", "B", "neg", tuple((o.name, "school.door1") for o, _k in fake))
    big = SS.build_sign_set(fake, fspec)
    ok(bool(SS.sg6_tris(cs2_asset.tri_count(big))), f"SG6 negative: {cs2_asset.tri_count(big)} tris reported")
    for o, _k in fake:
        m = o.data
        bpy.data.objects.remove(o, do_unlink=True)
        bpy.data.meshes.remove(m)
    set_faces = {}
    st = json.loads(sobj.data[cs2_asset.PART_TABLE_PROP])
    pv = np.zeros(len(sobj.data.polygons), np.int32)
    sobj.data.attributes["part"].data.foreach_get("value", pv)
    G = SS.game_coords(sobj)
    for p in sobj.data.polygons:
        set_faces[st[pv[p.index] - 1]] = G[list(p.vertices)]
    # SG2 against the PRE-CHANGE host (the legacy school: its own label quads)
    lodge.clear_scene()
    res0, host0 = blockout(["--no-fine"], tmp, "legacy")
    ok(res0.get("sign_set") is None, "SG2 legacy build carries no sign set")
    old = faces_game(host0, set(names))
    bad = SS.sg2_positions(set_faces, old)
    ok(len(old) == len(names) and not bad, f"SG2 {len(names)} boards within 1 mm of the pre-change host's "
                                           f"label quads ({bad[:2]})")
    moved = {k: v.copy() for k, v in set_faces.items()}
    moved[names[0]][0, 0] += 0.002
    ok(bool(SS.sg2_positions(moved, old)), "SG2 negative: a corner moved 2 mm is reported")
    return res, bounds, n


# ======================================================================================
# SG7 write_settings
# ======================================================================================

def sg7():
    paths = sorted(glob.glob(os.path.join(REPO, "assets", "import", "**", "settings.json"), recursive=True))
    paths = [p for p in paths if os.sep + "archive" + os.sep not in p]
    # a tiled sheet OWNER (the school X / Y, the tram base B) writes its own settings.json (LOD2 maps = the LOD1 far set,
    # cs2_tilesheet), not a default write_settings output. It is a sharer in all but name, so it is left out
    def tiled_owner(p):
        d = os.path.dirname(p)
        mp = os.path.join(d, os.path.basename(d) + ".manifest.json")
        if not os.path.isfile(mp):
            return False
        sheet = (json.load(open(mp, encoding="utf-8")).get("tiled") or {}).get("sheet") or {}
        return sheet.get("owner") == os.path.basename(d)
    owners = [p for p in paths if tiled_owner(p)]
    paths = [p for p in paths if p not in owners]
    # 2026-10-13: cs2_asset.write_settings gained `surface=` (the fence fleet: a "Surface" block written last). A file
    # carrying one is proven with that keyword (its own Surface dict), every other one with the defaults
    surf = {p: json.load(open(p, encoding="utf-8")).get("Surface") for p in paths}
    plain = [p for p in paths if surf[p] is None]
    same, diff, skip = SS.sg7_settings_proof(plain)
    for p in (p for p in paths if surf[p] is not None):
        def ws(out_dir, name, supplied_lods=(), dedicated_texture_lods=(), _s=surf[p]):
            return cs2_asset.write_settings(out_dir, name, supplied_lods, dedicated_texture_lods, surface=_s)
        s2, d2s, k2 = SS.sg7_settings_proof([p], write_settings=ws)
        same, diff, skip = same + s2, diff + d2s, skip + k2
    n_surf = len(paths) - len(plain)
    ok(not diff and len(same) >= 100, f"SG7 write_settings default: {len(same)} shipped settings.json "
                                      f"byte-identical ({n_surf} with a Surface block, proven with it), "
                                      f"{len(skip)} sharers + {len(owners)} tiled owners skipped ({diff[:2]})")
    orig = cs2_asset.write_settings

    def patched(out_dir, name, supplied_lods=(), dedicated_texture_lods=()):
        p = orig(out_dir, name, supplied_lods, dedicated_texture_lods)
        with open(p, "a", encoding="utf-8") as fh:
            fh.write(" ")
        return p
    _s, d2, _k = SS.sg7_settings_proof(same[:5], write_settings=patched)     # files the default writer reproduces
    ok(len(d2) == 5, "SG7 negative: a patched write_settings is caught on every file")
    tmp = tempfile.mkdtemp(prefix="sg7b_")
    try:
        p = cs2_asset.write_settings(tmp, "SkiSignSetHutB", shared_from="../SkiSignSetSchoolB/SkiSignSetSchoolB",
                                     shared_maps=SS.MAPS)
        data = json.load(open(p, encoding="utf-8"))
        want = {f"SkiSignSetHutB_{m}.png": f"../SkiSignSetSchoolB/SkiSignSetSchoolB_{m}.png" for m in SS.MAPS}
        ok(data == {"sharedAssets": want}, "SG7 shared_from writes the five owner keys and nothing else")
        raised = False
        try:
            cs2_asset.write_settings(tmp, "X", supplied_lods=(1,), shared_from="../A/A")
        except ValueError:
            raised = True
        ok(raised, "SG7 shared_from with LODs raises")
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


# ======================================================================================
# ROWS
# ======================================================================================

def rows():
    # kit color variants (2026-10-12): the UNCOLORED props' CIDs = their staged .cid files. The six COLORED props' base
    # prefabs are retired from staging (no Parent color ships), and every staged variant's .cid = its seeded CID
    bad = [p for p, k in FD.KIT.items() if not k.coloured and FD.staged_cid(p) != k.cid]
    ok(not bad, f"KIT {sum(1 for k in FD.KIT.values() if not k.coloured)} uncoloured kit CIDs = the staged .cid "
                f"files ({bad})")
    live = [p for p, k in FD.KIT.items() if k.coloured and FD.staged_cid(p) is not None]
    ok(not live, f"KIT the colored base object prefabs are retired from staging ({live})")
    drift = [(p, c) for p, k in FD.KIT.items() if k.coloured for c in FD.COLOUR_SETS
             if FD.staged_kit_cid(p, c) not in (None, FD.kit_cid(p, c))]
    ok(not drift, f"KIT staged variant .cid files = their seeded CIDs ({drift})")
    k0 = next(k for k in FD.KIT.values() if not k.coloured)
    flipped = k0.cid[:-1] + ("0" if k0.cid[-1] != "0" else "1")
    ok(flipped != FD.staged_cid(k0.prop), "KIT negative: a flipped digit differs from the staged .cid")
    gaps = {}
    for prop, wall, n in (("SkiDressTools", (3.0, 0.0, 4.0), (0.0, 1.0)),
                          ("SkiDressAkja", (-2.0, 0.43, 1.0), (1.0, 0.0)),
                          ("SkiDressSpineBoard", (0.0, 0.0, -3.0), (-0.7071068, -0.7071068))):
        r = FD.kit_lean_row(prop, wall, n, wall[1], colour_set="red" if FD.KIT[prop].coloured else None)
        gaps[prop] = FD.lean_rest_gap(r)
        d = ((r["position"][0] - wall[0]) * n[0] + (r["position"][2] - wall[2]) * n[1]) / \
            max(1e-9, (n[0] ** 2 + n[1] ** 2) ** 0.5)
        ok(abs(gaps[prop]) <= FD.FOOT_TOL and abs(d + FD.KIT[prop].rest_z) < 1e-6,
           f"LEAN {prop}: pivot {d:.4f} m out from the wall, rest plane gap {gaps[prop]:.4f} m")
    r = FD.kit_lean_row("SkiDressTools", (3.0, 0.0, 4.0), (0.0, 1.0), 0.0)
    r["position"] = (r["position"][0], 0.0, r["position"][2] + 0.01)
    ok(abs(FD.lean_rest_gap(r) - 0.01) < 1e-6, "LEAN negative: a tools row moved 1 cm reports 0.010 m")
    ok(abs(FD.KIT["SkiDressTools"].rest_z + 0.3925) < 1e-9, "LEAN the tools foot-line rule: rest plane 0.3925 m")
    # REJ
    want = {"BarrelRandom01": "fb4247f7a6f75b1478d9dd01ac88ce86",
            "SafetyConeRandom01": "dc8abeea43367b24ba2668784a309e56",
            "PotRandom01": "8d8722db083c3564291e3778cceeb1ae"}
    ok(all(LD.SUBOBJ[p]["guid"] == g for p, g in want.items()), "REJ SUBOBJ holds the three verified GUIDs")
    ok(set(LD.REJECTED_GUIDS) == {"ebe3622b9123c104783710893194cc55", "95cbbe17c442dc940be6e7986155c05d"}
       and not any(s["guid"] in LD.REJECTED_GUIDS for s in LD.SUBOBJ.values()),
       "REJ the two mixed-spawn GUIDs are rejected and in no SUBOBJ entry")
    saved = dict(LD.SUBOBJ["BarrelRandom01"])
    LD.SUBOBJ["BarrelRandom01"]["guid"] = "ebe3622b9123c104783710893194cc55"
    raised = False
    try:
        LD.sub_object_rows("X", [("BarrelRandom01", (0, 0, 0), (0, 0, 0, 1), -1)])
    except ValueError:
        raised = True
    finally:
        LD.SUBOBJ["BarrelRandom01"] = saved
    ok(raised, "REJ negative: a row on a rejected GUID raises")
    # REJ on the BUILDING path (fine-detail step 3a, step-2 check item 16): lodge.sub_objects_block accepts the shipping
    # school and raises on a rejected GUID, on a GUID nobody verified and on a mod row whose CID is not a kit object
    # prefab
    sch0 = R.get_recipe("school")
    blk = lodge.sub_objects_block(sch0)
    ok(len(blk["objects"]) == len(sch0.sub_objects), f"REJ building path: the school's {len(blk['objects'])} rows pass")
    cone = next(i for i, e in enumerate(sch0.sub_objects)
                if isinstance(e, dict) and e.get("prefab") == "SafetyConeRandom01")
    rack = next(i for i, e in enumerate(sch0.sub_objects) if isinstance(e, dict) and e.get("kind") == "mod")

    def _raises(i, **kw):
        rows = list(sch0.sub_objects)
        rows[i] = dict(rows[i], **kw)
        try:
            lodge.sub_objects_block(replace(sch0, sub_objects=tuple(rows)))
        except ValueError as e:
            return str(e)
        return None
    e1 = _raises(cone, m_Object="ebe3622b9123c104783710893194cc55")
    e2 = _raises(cone, m_Object="95cbbe17c442dc940be6e7986155c05d")
    e3 = _raises(cone, m_Object="0123456789abcdef0123456789abcdef")
    e4 = _raises(rack, m_Object="0123456789abcdef0123456789abcdef")
    ok(bool(e1) and "REJECTED" in e1 and bool(e2) and "REJECTED" in e2,
       "REJ negative (building): a school cone row on either rejected mixed-spawn GUID raises")
    ok(bool(e3) and "not dump-verified" in e3, "REJ negative (building): an unverified GUID raises")
    ok(bool(e4) and "dressing-kit" in e4, "REJ negative (building): a kit row with a foreign CID raises")
    # kit color variants (2026-10-12): the retired Parent base CID and a color set that is not the variant's raise
    rk = sch0.sub_objects[rack]
    e5 = _raises(rack, m_Object=FD.KIT[rk["prefab"]].cid)
    e6 = _raises(rack, colour_set="green")
    ok(bool(e5) and "dressing-kit" in e5, "REJ negative (building): a colored kit row on the retired base CID raises")
    ok(bool(e6), "REJ negative (building): a kit row whose color set is not its variant's / the recipe's raises")
    # EFF (a plain recipe: P1. L1 opts in since fine-detail step 3a, and P1 / P2 opt in since step 3d. So the plain
    # Recipe is now built from P1's base Recipe fields, independent of which hosts have opted in)
    import dataclasses
    p1 = R.get_recipe("P1")
    l1 = R.Recipe(**{f.name: getattr(p1, f.name) for f in dataclasses.fields(R.Recipe)})
    ok(not isinstance(l1, FD.FineRecipe) and FD.effective(l1) is l1 and FD.effective(l1, fine=False) is l1,
       "EFF plain Recipe (P1's base fields) passes through (identity)")
    sch = R.get_recipe("school")
    leg = FD.effective(sch, fine=False)
    nos = FD.effective(sch, signset=False)
    # 14 = the school's 20 minus the six picket-fence rows T87 removed (DECISIONS 302)
    ok(leg.sign_set is None and not leg.fine_detail and len(leg.sub_objects) == 14,
       f"EFF --no-fine: legacy rows {len(leg.sub_objects)}, no sign set")
    ok(nos.sign_set is None and nos.fine_detail and len(nos.sub_objects) == len(sch.sub_objects),
       "EFF --no-signset keeps the fine rows")


def main():
    t0 = time.perf_counter()
    tmp = tempfile.mkdtemp(prefix="test_signset_")
    try:
        ss1_layout()
        h = ss4_frozen()
        maps, metrics = paint_trapped()
        ss3_strokes(metrics)
        ss5_ss6(maps)
        ss8_typography(metrics)
        ss9_blank(maps, metrics)
        ss10_no_font_files()
        if not QUICK:
            maps2, _m2 = SS.paint_sheet()
            ok(_hash_maps(maps) == _hash_maps(maps2), f"SS7 determinism: two paints {_hash_maps(maps)}")
        del maps
        rows()
        sg7()
        res, bounds, n = sign_set_checks(tmp)
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        FAILS.append("exception")
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    print(f"[test_signset] {'PASS' if not FAILS else 'FAIL'}: {len(CHECKS)} checks, {len(FAILS)} failure(s) "
          f"({time.perf_counter() - t0:.0f} s)")
    sys.exit(1 if FAILS else 0)


if __name__ == "__main__":
    main()
