"""
test_school_emblem.py - T278 (1.0.4): the ski school's emblem (school_emblem.py, recipes/school.py EMBLEM_V1 / v1_labels,
cs2_bldg.glyph_prims). Why the part is there: the tower field is the school's landmark from the slope; a pictogram reads
where "SKI SCHOOL" text is too small. 1.0.3's cand_d read as a figure kneeling on a sled at 45 m.

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_school_emblem.py

- E1 strokes: every registered design's thinnest stroke (line width, disk diameter) is >= MIN_STROKE_TEXELS texels at the
  mip a 45 m 1080p frame samples (school_emblem.glyph_texels: 3.04 m at 55.9 px/m, mip 1 = 85 texels). Negative: a
  design with a 6-unit pole fails.
- E2 head: rendered at mip 1 (85 texels, 2 x 2 box filter of the 170-texel mip 0) and cut at 0.5 coverage, each design's
  head (its largest disk) is a blob of its own: the 4-connected component under the head center spans no more than the
  disk plus 2 texels. Negative: the head moved onto the shoulders fails.
- E3 wiring: cs2_bldg.glyph_prims resolves every school_emblem name and still resolves cand_d to
  ski_launcher_glyph.cand_d (flag off unchanged). school.v1_labels: the same label prefixes in the same order as
  RECIPE.labels; the field (at FIELD_FRAC_V1) and both flags draw EMBLEM_V1 (field without ground, flags on SCHOOL_RED); every other
  entry is RECIPE's own. RECIPE.labels (flag off) keeps the _Lazy cand_d field painter and cand_d flags. The school's
  field numbers give FIELD_GLYPH_M.
- E4 build: lodge.build_parts with opts["texpass"] = "v1" hands back recipe_replace["labels"] == v1_labels(); without
  the flag no "labels" key (flag-off builds paint as 1.0.3).
"""
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
    thin = E.skier_b()[:-1] + [("line", [(182.0, 108.0), (210.0, 186.0)], 6.0)]
    check(stroke_texels(thin) < E.MIN_STROKE_TEXELS, "E1 negative: a 6-unit pole fails",
          f"({stroke_texels(thin):.2f} texels)")
    sunk = [p if p[0] != "disk" else ("disk", 150.0, 74.0, 22.0) for p in E.skier_b()]
    check(not head_blob(sunk)[0], "E2 negative: the head on the shoulders fails", head_blob(sunk)[1])


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
          and fd["glyph"]["frac"] == sch["FIELD_FRAC_V1"], "E3 v1 field: display glyph EMBLEM_V1, no ground, FIELD_FRAC_V1")
    for s in "AB":
        g = dict(by[f"emblem_schoolflag{s}_face"][2]["draws"])["glyph"]
        check(g["prims"] == em and tuple(g["bg"]) == tuple(sch["SCHOOL_RED"]), f"E3 v1 flag {s}: EMBLEM_V1 on SCHOOL_RED")
    same = [e for e in v1 if not e[0].startswith(("emblem_schoolflag", sch["FIELD_FACE"]))]
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


def main():
    e1_e2()
    e3()
    e4()
    if FAILS:
        print(f"FAIL test_school_emblem: {len(FAILS)} failed: {FAILS}")
        sys.exit(1)
    print(f"PASS test_school_emblem: E1-E4 + 2 negatives ({N_OK[0]} ok)")


main()
