"""
test_school_emblem.py - T278 (1.0.4): the ski school's emblem (school_emblem.py, recipes/school.py EMBLEM_V1 / v1_labels,
cs2_bldg.glyph_prims). Why the part is there: the tower field is the school's landmark from the slope; a pictogram reads
where "SKI SCHOOL" text is too small. 1.0.3's cand_d read as a figure kneeling on a sled at 45 m.

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_school_emblem.py

- E1 strokes: every registered design's thinnest stroke (line width, disk diameter) is >= MIN_STROKE_TEXELS texels at the
  mip a 45 m 1080p frame samples (school_emblem.glyph_texels: 2.62 m at 55.9 px/m, mip 1 = 73 texels). Negative: a 6-unit
  pole fails.
- E2 head: rendered at mip 1 (2 x 2 box filter of mip 0) and cut at 0.5 coverage, each design's head (its largest disk) is
  a blob of its own: the 4-connected component under the head center spans no more than the disk plus 2 texels. Negative:
  the head moved onto the shoulders fails.
- E5 the research spec (r7 RESEARCH.md section 2): torso 30-40 deg forward of vertical; ONE ski at 12-18 deg; the slope
  line its own blob at mip 1 (no merge with the ski); the pole angled back (its tip behind the hand); the figure (head top
  to ski bottom) >= 2.0 m on the building; the glyph inside the tile's red with >= 3 texels to spare at mip 1; the tile
  rim >= 3 texels at mip 1. Negative: a slope line 4 units under the ski merges.
- E3 wiring: cs2_bldg.glyph_prims resolves every school_emblem name and still resolves cand_d to
  ski_launcher_glyph.cand_d (flag off unchanged). school.v1_labels: the same label prefixes in the same order as
  RECIPE.labels; the field is the tile (white square, SCHOOL_RED inset by TILE_RIM) under EMBLEM_V1 at FIELD_FRAC_V1;
  both flags draw EMBLEM_V1 on SCHOOL_RED; every other entry is RECIPE's own. RECIPE.labels (flag off) keeps the _Lazy
  cand_d field painter and cand_d flags. The school's field numbers give FIELD_GLYPH_M.
- E4 build: lodge.build_parts with opts["texpass"] = "v1" hands back recipe_replace["labels"] == v1_labels(); without
  the flag no "labels" key (flag-off builds paint as 1.0.3).
"""
import math
import os
import sys

import bpy  # noqa: F401  (Blender only)
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
import bldg_recipes as R  # noqa: E402
import cs2_bldg as K  # noqa: E402
import lodge  # noqa: E402
import school_emblem as E  # noqa: E402
import ski_launcher_glyph as G  # noqa: E402

FAILS = []
N_OK = [0]


def check(cond, label, detail=""):
    if cond:
        N_OK[0] += 1
        print(f"  ok   {label} {detail}".rstrip())
    else:
        print(f"  FAIL {label} {detail}".rstrip())
        FAILS.append(label)


def stroke_texels(pr):
    return E.min_stroke(pr) / E.CANVAS * E.glyph_texels()


def mip1(pr):
    n0 = int(round(E.glyph_texels(0)))
    a = G.render_alpha(pr, n0 - n0 % 2, ss=8).astype(np.float64)
    h, w = a.shape
    return a.reshape(h // 2, 2, w // 2, 2).mean(axis=(1, 3))


def component(mask, r, c):
    seen = np.zeros_like(mask, bool)
    if not mask[r, c]:
        return seen
    st = [(r, c)]
    seen[r, c] = True
    while st:
        y, x = st.pop()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            yy, xx = y + dy, x + dx
            if 0 <= yy < mask.shape[0] and 0 <= xx < mask.shape[1] and mask[yy, xx] and not seen[yy, xx]:
                seen[yy, xx] = True
                st.append((yy, xx))
    return seen


def head_blob(pr):
    """(ok, detail): the largest disk's component at mip 1 spans <= its diameter + 2 texels."""
    a = mip1(pr)
    s = a.shape[0] / E.CANVAS
    head = max((p for p in pr if p[0] == "disk"), key=lambda p: p[3])
    _k, cx, cy, r = head
    comp = component(a >= 0.5, int(cy * s), int(cx * s))
    if not comp.any():
        return False, "(head center below 0.5 coverage)"
    ys, xs = np.nonzero(comp)
    span = max(ys.max() - ys.min() + 1, xs.max() - xs.min() + 1)
    lim = 2 * r * s + 2
    return span <= lim, f"(blob {span} texels, head {2 * r * s:.1f} + 2)"


def e1_e2():
    for nm in sorted(E.EMBLEMS):
        pr = E.prims(nm)
        t = stroke_texels(pr)
        check(t >= E.MIN_STROKE_TEXELS, f"E1 {nm}: thinnest stroke >= {E.MIN_STROKE_TEXELS} texels at mip {E.MIP_45M}",
              f"({E.min_stroke(pr)} units = {t:.2f} texels)")
        ok, det = head_blob(pr)
        check(ok, f"E2 {nm}: the head is its own blob at mip {E.MIP_45M}", det)
    parts = E.skier_parts()
    thin = list({**parts, "pole": ("line", parts["pole"][1], 6.0)}.values())
    check(stroke_texels(thin) < E.MIN_STROKE_TEXELS, "E1 negative: a 6-unit pole fails",
          f"({stroke_texels(thin):.2f} texels)")
    sunk = list({**parts, "head": ("disk", 146.0, 76.0, 22.0)}.values())
    check(not head_blob(sunk)[0], "E2 negative: the head on the shoulders fails", head_blob(sunk)[1])


def _deg(p, q):
    return math.degrees(math.atan2(q[1] - p[1], q[0] - p[0]))


def slope_apart(pr, slope):
    """The slope line's component at mip 1 (0.5 cut) holds no texel of the ski's center line."""
    a = mip1(pr)
    s = a.shape[0] / E.CANVAS
    (x0, y0), (x1, y1) = slope[1][0], slope[1][-1]
    xm, ym = (x0 + x1) / 2.0, (y0 + y1) / 2.0
    comp = component(a >= 0.5, int(ym * s), int(xm * s))
    ski = [(int(E._on_ski(x) * s), int(x * s)) for x in range(30, 200, 4)]
    return bool(comp.any()) and not any(comp[r, c] for r, c in ski if 0 <= r < comp.shape[0])


def e5():
    P = E.skier_parts()
    sch = R.get_recipe("school").builder.__globals__
    (hx, hy), (sx, sy) = P["torso"][1][0], P["torso"][1][-1]
    lean = math.degrees(math.atan2(sx - hx, hy - sy))
    check(30.0 <= lean <= 40.0, "E5 torso 30-40 deg forward of vertical", f"({lean:.1f} deg)")
    ski = _deg(P["ski"][1][0], P["ski"][1][1])
    longs = [p for p in E.skier_b() if p[0] == "line" and math.dist(p[1][0], p[1][-1]) > 150.0]
    check(12.0 <= ski <= 18.0 and len(longs) == 2, "E5 ONE ski at 12-18 deg (the long strokes: the ski and the slope line)",
          f"({ski:.1f} deg, {len(longs)} long strokes)")
    check(slope_apart(E.skier_b(), P["slope"]), "E5 the slope line is its own blob at mip 1")
    near = ("line", [(q[0], q[1] - 16.0) for q in P["slope"][1]], 12.0)
    check(not slope_apart(list({**P, "slope": near}.values()), near),
          "E5 negative: a slope line 4 units under the ski merges")
    hand, tip = P["pole"][1][0], P["pole"][1][-1]
    check(tip[0] < hand[0] and tip[1] > hand[1], "E5 the pole is angled back (tip behind and below the hand)",
          f"(hand {hand}, tip {tip})")
    m_per_unit = E.FIELD_GLYPH_M / E.CANVAS
    top = P["head"][2] - P["head"][3]
    bot = max(q[1] for q in P["ski"][1]) + P["ski"][2] / 2.0
    check((bot - top) * m_per_unit >= 2.0, "E5 the figure is >= 2.0 m tall on the building",
          f"({(bot - top) * m_per_unit:.2f} m)")
    side = sch["FIELD"][1] - sch["FIELD"][0]
    rim = sch["TILE_RIM"] * side
    tex1 = E.SHEET_PPM / 2 ** E.MIP_45M
    check(rim * tex1 >= E.MIN_STROKE_TEXELS, "E5 the tile rim >= 3 texels at mip 1", f"({rim * tex1:.1f} texels)")
    x0, y0, x1, y1 = E.extent(E.skier_b())
    half_red = side / 2.0 - rim
    far = max(abs(v - E.CANVAS / 2.0) for v in (x0, y0, x1, y1)) * m_per_unit
    check((half_red - far) * tex1 >= E.MIN_STROKE_TEXELS,
          "E5 the glyph sits inside the tile's red with >= 3 texels to spare", f"({(half_red - far) * tex1:.1f} texels)")


def e3():
    sch = R.get_recipe("school").builder.__globals__
    for nm in sorted(E.EMBLEMS):
        check(K.glyph_prims(nm) == E.prims(nm), f"E3 glyph_prims resolves {nm}")
    check(K.glyph_prims("cand_d") == G.cand_d(), "E3 glyph_prims('cand_d') = ski_launcher_glyph.cand_d (flag off)")
    rec = sch["RECIPE"]
    v1 = sch["v1_labels"]()
    em = sch["EMBLEM_V1"]
    check(em in E.EMBLEMS, f"E3 EMBLEM_V1 {em!r} is a registered design")
    check([e[0] for e in v1] == [e[0] for e in rec.labels], "E3 v1_labels: same prefixes, same order as RECIPE.labels",
          f"({len(v1)} entries)")
    by = {e[0]: e for e in v1}
    field = by[sch["FIELD_FACE"]]
    fd = dict(field[2]["draws"])
    check(field[1] == "display" and fd["glyph"]["prims"] == em and "bg" not in fd["glyph"]
          and fd["glyph"]["frac"] == sch["FIELD_FRAC_V1"], "E3 v1 field: display glyph EMBLEM_V1 at FIELD_FRAC_V1")
    items = fd.get("shapes", {}).get("items", ())
    check([d[0] for d in field[2]["draws"]] == ["shapes", "glyph"] and len(items) == 2
          and items[0][0] == items[1][0] == "square" and items[0][3] == 1.0 and tuple(items[0][4]) == (sch["WHITE"],) * 3
          and abs(items[1][3] - (1.0 - 2.0 * sch["TILE_RIM"])) < 1e-9 and tuple(items[1][4]) == tuple(sch["SCHOOL_RED"]),
          "E3 v1 field: the tile (white square, SCHOOL_RED inset by TILE_RIM) under the glyph")
    for s in "AB":
        g = dict(by[f"emblem_schoolflag{s}_face"][2]["draws"])["glyph"]
        check(g["prims"] == em and tuple(g["bg"]) == tuple(sch["SCHOOL_RED"]), f"E3 v1 flag {s}: EMBLEM_V1 on SCHOOL_RED")
    same = [e for e in v1 if not e[0].startswith(("emblem_schoolflag", sch["FIELD_FACE"]))
            and e[0] not in ("sign_meet_face", "sign_lesson3_face")]     # call 5: E6 checks those two
    check(all(e in rec.labels for e in same), "E3 v1: every other label entry is RECIPE's own", f"({len(same)})")
    f0 = rec.labels[0]
    check(f0[0] == sch["FIELD_FACE"] and callable(f0[1]) and getattr(f0[1], "__name__", "") == "glyph_cand_d",
          "E3 flag off: the field keeps the _Lazy cand_d painter")
    check(all(dict(e[2]["draws"])["glyph"]["prims"] == "cand_d" for e in rec.labels
              if e[0].startswith("emblem_schoolflag")), "E3 flag off: the flags keep cand_d")
    fx = sch["FIELD"]
    check(abs((fx[1] - fx[0]) * sch["FIELD_FRAC_V1"] - E.FIELD_GLYPH_M) < 1e-6 and abs((fx[3] - fx[2]) - (fx[1] - fx[0])) < 1e-6,
          "E3 FIELD_GLYPH_M = the school's square field x FIELD_FRAC_V1", f"({(fx[1] - fx[0]) * sch['FIELD_FRAC_V1']:.3f} m)")


def e4():
    rec = R.get_recipe("school")
    sch = rec.builder.__globals__
    for tp in (None, "v1"):
        lodge.clear_scene()
        opts = {"tower": True, "roof_windows": "win", **({"texpass": tp} if tp else {})}
        lodge.build_parts(rec, opts)
        rr = opts.get("recipe_replace") or {}
        if tp:
            check(rr.get("labels") == sch["v1_labels"](), "E4 texpass v1: build() hands back v1_labels")
        else:
            check("labels" not in rr, "E4 flag off: no labels in recipe_replace")


def e6():
    """Overnight call 5: the sign-set boards (meeting board, fourth lesson board) draw the v1 emblem under --texpass v1
    only. Flag off: sheet_for returns SHEET itself. v1: only the two kinds' draws change (rects, sizes, lit flags and
    layout_hash stay), and painting them changes only their own rects. E6_OUT=<dir>: writes the off / v1 sheets."""
    import cs2_signset as CS
    sch = R.get_recipe("school").builder.__globals__
    check(CS.sheet_for("off") is CS.SHEET and CS.sheet_for(None) is CS.SHEET, "E6 flag off: sheet_for is SHEET itself")
    check(CS.TEXPASS_EMBLEM == sch["EMBLEM_V1"], "E6 the sign set and the tower draw the same emblem",
          f"({CS.TEXPASS_EMBLEM} / {sch['EMBLEM_V1']})")
    v1 = CS.sheet_for("v1")
    same = all(a is b for a, b in zip(CS.SHEET, v1) if a.key not in CS.TEXPASS_GLYPH_KEYS)
    check(len(v1) == len(CS.SHEET) and same, "E6 v1: every other kind is the same object")
    for key in CS.TEXPASS_GLYPH_KEYS:
        a, b = CS.kind(key), next(k for k in v1 if k.key == key)
        kinds = [it[0] for d, kw in b.draws if d == "shapes" for it in kw["items"]]
        check((a.rect, a.size, a.lit, a.state) == (b.rect, b.size, b.lit, b.state), f"E6 v1 {key}: rect / size / lit kept")
        check(f"glyph:{CS.TEXPASS_EMBLEM}" in kinds and "glyph:cand_d" not in kinds, f"E6 v1 {key}: draws the emblem",
              f"({kinds})")
    check(CS.layout_hash(v1) == CS.layout_hash(), "E6 v1: layout_hash unchanged (the B sharers keep their sheet)")
    lab = {e[0]: e for e in sch["v1_labels"]()}
    for face in ("sign_meet_face", "sign_lesson3_face"):
        items = [it[0] for d in lab[face][2]["draws"] if d[0] == "shapes" for it in d[1]["items"]]
        check(f"glyph:{sch['EMBLEM_V1']}" in items and "glyph:cand_d" not in items,
              f"E6 v1_labels {face} (the --no-signset path): draws the emblem", f"({items})")
    rest_ok = all(lab[e[0]] == e for e in sch["DISPLAY_LABELS"]
                  if e[0] not in ("sign_meet_face", "sign_lesson3_face", sch["FIELD_FACE"])
                  and not e[0].startswith("emblem_schoolflag"))
    check(rest_ok, "E6 v1_labels: every other label entry unchanged")
    sub_off = [CS.kind(k) for k in CS.TEXPASS_GLYPH_KEYS]
    sub_v1 = [k for k in v1 if k.key in CS.TEXPASS_GLYPH_KEYS]
    m_off, _ = CS.paint_sheet(sub_off)
    m_v1, _ = CS.paint_sheet(sub_v1)
    b0, b1 = m_off["BaseColor"][0], m_v1["BaseColor"][0]
    inside = np.zeros(b0.shape[:2], bool)
    for k in sub_off:
        x0, y0, x1, y1 = k.rect
        inside[max(0, y0 - CS.DILATE):y1 + CS.DILATE, max(0, x0 - CS.DILATE):x1 + CS.DILATE] = True
    diff = np.any(np.abs(b0 - b1) > 1e-6, axis=2)
    check(diff[inside].any() and not diff[~inside].any(), "E6 paint: the v1 boards differ from 1.0.3 inside their rects only",
          f"({int(diff[inside].sum())} texels changed)")
    x0, y0, x1, y1 = CS.kind("school.lesson3").rect
    red = np.asarray(CS._srgb_to_lin(CS.SCHOOL_RED))
    crop = CS._srgb_to_lin(b1[y0:y1, x0:x1, 0:3])
    cov = float(np.mean(np.all(np.abs(crop - red) < 0.02, axis=2)))
    check(0.08 <= cov <= 0.5, "E6 paint: the lesson board's emblem covers a plausible share of the board", f"({cov:.3f})")
    out = os.environ.get("E6_OUT")
    if out:
        os.makedirs(out, exist_ok=True)
        CS.write_sheet(m_off, out, "e6_off")
        CS.write_sheet(m_v1, out, "e6_v1")


def main():
    e1_e2()
    e5()
    e3()
    e4()
    e6()
    if FAILS:
        print(f"FAIL test_school_emblem: {len(FAILS)} failed: {FAILS}")
        sys.exit(1)
    print(f"PASS test_school_emblem: E1-E6 + 3 negatives ({N_OK[0]} ok)")


main()
