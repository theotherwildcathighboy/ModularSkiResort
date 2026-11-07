"""
Bake smoke + shipping-path gate for lift_terminal_detach.py (contract dq-terminal.md section 14, delivered after
build/dq-family-review-pipeline.md major 1). Before this file, no test called lift_terminal_detach.main(). So the
lift_terminal table overrides, the band stamps (stamp_signbands / check_sign_uv since Revision 1, 2026-10-11),
engine_base_gate, the LOD chain, the export and settings.json only ran at export time.

1. C8 negative:
- A 0.2 m box inside the hanger column of the carrier envelope (lane straight, under the soffit slot) must make
  check_clearance raise ContractError("C8 ...").
- Revision 1 negatives: a handrail on the left flank (y -4.0) must raise "R1d". A second underside ladder (on the
  nose side of pedestal F) must raise "R1b".
- Revision 2 (second look 3', front ladder to the ground) negatives: the nose ladder without its footing must raise
  "R2a". A grating re-closing the landing's ladder-way must raise "R2c".
- Revision 1c (revision2-check-dq-stations item 6): the same per kind. The BASE's ground flight now stands beside
  the landing (lfoot_gf). The +Y landing wing put back over its climbing side must raise "R2c". The pre-1c base
  (GF_ON_BASE False: the straight ground run through the ladder-way, 0.25 m) must fail R2c's climbing-room gate
  (0.61 m).

2. Bake smoke, DQ BASE, through main() with --out <tmp> --finish --lods --atlas 512 --ao-samples 4 --no-preview
   (validation on, main raises on any validator ERROR):
- validate_asset_folder_ex 0 ERROR (hero)
- BaseColor (and _Emissive when shipped) alpha 255
- MaskMap B 0
- ControlMask A 0 and R + G + B <= 1 (exclusive)
- _Emissive present and lit somewhere WHEN the build has a lamp_ part (the gondola). The DQ stations ship FOUR maps
  since 2026-10-12 (no lamp_ part: no map, a stale one is removed, the manifest blocks say "no map", settings.json
  never named it).
- 95th-percentile BaseColor under each ControlMask slot (R / B: 0.75..0.85 = RECOLOR_ALBEDO 0.80. G: >= 0.45, see
  below).
- SIGN BANDS (Revision 1 (e), replaced the name bands): each side's rect IS its reserved UV rect x A (pinned,
  density-protected). px/m >= 4x the old band (scaled with the atlas). The emblem's full-coverage texels
  (D.emblem_alpha) are MARK_WHITE with ControlMask B = R = 0. Its zero-coverage texels stay slot 3 (B). Every plate
  texel has ControlMask 0.
- Revision 1b (polish, 2026-10-11, vector digits): the plate border is the dark color and every plate-field texel
  is the linear-light mix of light and dark by D.digit_coverage (the Bfont outline, anti-aliased). The coverage sum
  is 80-120 % of the outline area x (px/m)^2 and equals the manifest's. The edge shows >= D.DIGIT_AA_LEVELS_MIN
  partial levels and the PNG has in-between texels (no 5x7 staircase). At the plate edge, the Normal map tilts on
  the panel's rounded edge (top bevel G > 0.5, bottom G < 0.5, OpenGL) and is flat in the emblem panel's field. The
  emblem panel's background is darker at its border than in its field (occlusion).
- T2 engine base: the manifest's LOD0 + per-LOD verdicts OK, and cs2_validate.ground_ring_report on every
  re-imported LOD FBX: verdict OK, 0 blocked, 0 below.
- T3 budgets / LOD chain: LOD0 <= cap, LOD1 <= 50 % LOD0 and <= the LOD1 cap, LOD2 < 500 (F9 margin), every level's
  re-imported tris == the manifest, settings.json wires LOD1 / LOD2.
- T5 _Win: `_Win`, `_LOD1_Win`, `_LOD2_Win` exist. `_Win` = cabin 6 tris + 4 housing windows (Revision 1 (f)) in
  exactly the room cells D.expected_room_ids ([7, 13]).
- T6 names: validate_asset_name + lint_asset_name clean. Every FBX stem is <Name>[_suffix].
- T7 front ladder (Revision 2): the manifest's front_ladder block (open-bottom footing, rails from its top, the
  rung count on the 0.30 grid, no cage). In the re-imported LOD0 and LOD1 FBX the footing has vertices at z 0 and
  the rails run from its top to the crown + 0.35. LOD2 carries no footing (dropped with the rails). Revision 1c
  (base): the ground flight's block (footing, rails, rungs, climbing room >= CLIMB_MIN to fixed objects and to the
  chairs, step-across GF_STEP) and, in the FBX, its rails footing -> landing + GF_EXT while the front ladder runs
  landing -> crown + 0.35.

2b. Gondola draft 1 follow-up (detail pass 1, 2026-10-11, draft1-check-gondola-stations F1 / F5): the NEGATIVES of
G1-G8. Each contract must FAIL on a deliberately wrong gondola build (assert_gondola on a blockout):
- G1 a portal narrowed to u_hi 1.30 (the cabin body box + 0.30 leaves it)
- G2 the boarding edge moved to |y| 4.20 (0.11 m off the cabin)
- G3 the doors put back as leaves on solid walls (GD_DOOR_OPENINGS off: rays through the opening meet the wall)
- G4 a box in the nose ladder's climbing zone
- G5 a hall window removed
- G6 a box in the hosted entry tower's keep-out (base)
- G7 the top's draft-1 portal sign height 6.40 (it sat behind the nose band)
- G8 a box at the boarding edge 0.11 m off the real cabin's path

A gondola BAKE SMOKE through main() (base, 512, AO 4) checks: the folder / channel / sign (n + p own their rects, q
shares p) / engine base / budget / `_Win` (18 tris, cells 7 + 13) / names checks, the nose ladder's footing on the
ground (LOD0 / LOD1) and gone at LOD2, the manifest's G3 doors block (three real openings).

Gondola revision 1 (2026-10-11, the long glazed hall, build/gondola-stations-rev1.md): the negatives follow the
hall:
- G5 a curtain-wall pane removed (a hole)
- G7 the top's portal sign raised to 7.00 (into the nose ring beam)
- G9 a box past the 3 x 4 lot's lateral line
- G10 a dressing board pushed into a mullion (an undeclared part clash)

The smoke checks the `_Win` of the manifest's room allocation (2 tris per pane, one cell per id, <= 25 ids), p and
q sharing n's sign rect, the labels pinned, the weathering step, the vanilla sub-object rows (verified GUIDs,
parent 0, 4 lamp posts), the lot [3, 4] and the hosted tower seam record (base).

DQ follow-up (2026-10-11, build/dq-stations-followup.md, rev-1 checker F3 / F4): two more G3 negatives: revision
1's fixed center post back in the +Y double door (the clear-opening rays count the fixed frame) and an undeclared 1
x 1 m flat top (the standable-top census). The smoke checks the manifest's clear opening (1.80 x 2.30, no center
post, head 2.60) and the declared not-walkable tops with reasons.

Detail pass 1 (DQ): the sign band +Y shares the -Y rect (one stamp: sign_band holds signband_n with shared_with
signband_p), the base's front ladder is 0.48 wide (MC4), the manifest's detail1 block carries the labels (pinned
rects), the weathering ladder, bay sharing and the sub-object rows.

Fine-detail step 3b (2026-10-12, build/fine-detail-step3b-stations.md, FINE_DETAIL on): the sub-object blocks hold
the vanilla rows AND the dressing kit's "mod" rows (guid CID:<the staged object-prefab CID>, parent 0, the block's
kinds count). The DQ's _Emissive was dark (its box lamps are vanilla rows now) and is DROPPED (2026-10-12: four
maps, no map file, no stale file). The gondola's is still lit (the linear soffit lights).

3. --review byte identity, DQ TOP: main() runs twice in this process (FBX header clock frozen, as
   tests/test_review_side_effects.py), without and with --review <tmp>. Every file in the asset folder must be
   byte-identical (FBX included: same process = same UID salt) and the manifests equal without written_utc. The
   pack lands in <tmp>, never in the asset folder. The top build gets the channel / T2 / T3 / T5 / T6 checks as
   well.

ControlMask G (slot-2 pedestal pads) is held to >= 0.45 only. The canopy's baked AO multiplies the recolored 0.80
down to ~0.54 under the canopy (pipeline review finding 4, open: a cs2_bake AO floor on recolor texels or a
DECISIONS acceptance). Raise the G bound to 0.75 when it lands.

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_terminal_detach_bake.py
"""
import datetime
import hashlib
import json
import math
import os
import shutil
import sys
import tempfile
import time
import types

import bpy
import numpy as np
from mathutils import Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
import cs2_asset  # noqa: E402
import cs2_validate  # noqa: E402
import lift_terminal  # noqa: E402
import lift_terminal_detach as D  # noqa: E402

FAILS = []
ARGS = ["--finish", "--lods", "--atlas", "512", "--ao-samples", "4", "--no-preview"]
FROZEN_FBX_TIME = datetime.datetime(2026, 1, 1, 0, 0, 0)
MARK_SRGB = (0.85, 0.86, 0.88)              # lift_terminal_detach.MARK_WHITE as written
PLATE_LIGHT_SRGB = D.PLATE_LIGHT_SRGB
PLATE_DARK_SRGB = D.PLATE_DARK_SRGB
P95_RB = (0.75, 0.85)
P95_RB_WEATHERED = (0.75, 0.90)             # detail pass 1: free slot wear lifts worn edges
P95_G_MIN = 0.45                            # pipeline review 4 open (see the docstring)


def check(cond, label):
    print(f"[bake-test] {'ok  ' if cond else 'FAIL'} {label}")
    if not cond:
        FAILS.append(label)
    return cond


def file_hashes(folder):
    out = {}
    for fn in sorted(os.listdir(folder)):
        with open(os.path.join(folder, fn), "rb") as fh:
            out[fn] = hashlib.sha1(fh.read()).hexdigest()
    return out


def freeze_fbx_clock():
    """io_scene_fbx stamps datetime.now() into CreationTimeStamp (export_fbx_bin.py:3235)."""
    import io_scene_fbx.export_fbx_bin as fbxbin

    class _Frozen(datetime.datetime):
        @classmethod
        def now(cls, tz=None):
            return FROZEN_FBX_TIME

    fbxbin.datetime = types.SimpleNamespace(datetime=_Frozen)


def tris_of(me):
    return int(sum(len(p.vertices) - 2 for p in me.polygons))


# --------------------------------------------------------------------------------------
# 1. C8 negative
# --------------------------------------------------------------------------------------

def c8_negative():
    lift_terminal.clear_scene()
    p = D.params_for("base", False, D.load_carrier("dq"))
    parts = D.build_station(p)
    x, y = p.x_wheel - 6.0, p.lane_y
    z = p.lowest_abs + 2.40                      # hanger column: lowest + 1.60 .. + 3.30
    parts.append((D._box("cabinet_c8probe", x - 0.1, x + 0.1, y - 0.1, y + 0.1, z - 0.1, z + 0.1),
                  "galv"))
    t, table, _src = D.assemble(parts, False, p)
    try:
        D.check_clearance(t, table, p)
    except D.ContractError as e:
        check(str(e).startswith("C8 chair clearance"),
              f"C8 negative: a box in the hanger column raises ({str(e)[:110]})")
        return
    check(False, "C8 negative: a box in the hanger column did NOT raise")


def r1_negatives():
    """Revision 1: a railing on the left flank (R1d) and a second underside ladder (R1b)
    must each fail the authoring-frame contracts."""
    for label, extra, want in (
            ("R1d", lambda p: D._m(D.cs2_kit.handrail("probe", [(0.0, -4.0, D.DECK_H),
                                                                (2.0, -4.0, D.DECK_H)]), "galv"),
             "R1d"),
            ("R1b", lambda p: D._ladder("px", p.ped_f - 1.35, 0.0, D.DECK_H, p.z_soffit), "R1b")):
        lift_terminal.clear_scene()
        p = D.params_for("base", False, D.load_carrier("dq"))
        parts = D.build_station(p) + extra(p)
        t, table, _src = D.assemble(parts, False, p)
        me = t.data
        try:
            D.assert_revision1(me, table, D._face_parts(me), D._co(me), p)
        except D.ContractError as e:
            check(str(e).startswith(want), f"{label} negative raises ({str(e)[:100]})")
            continue
        check(False, f"{label} negative did NOT raise")


def r2_negatives():
    """Revision 2 (second look 3') and Revision 1c: per kind, a ground ladder without its footing (R2a: base lfoot_gf,
    top lfoot_nz) and a grating in the climber's way (R2c: base the trimmed +Y landing wing put back over the
    ground flight's climbing side, top the ladder-way re-closed) must each fail assert_revision2. The pre-1c BASE
    (the straight ground run up the nose line through the ladder-way, GF_ON_BASE False, the shipped Revision 2
    base) must fail R2c's climbing-room gate (0.25 m < CLIMB_MIN 0.61)."""
    def no_footing(name):
        def edit(p, parts):
            keep = []
            for o, m in parts:
                if o.name == name:
                    me = o.data
                    bpy.data.objects.remove(o, do_unlink=True)
                    bpy.data.meshes.remove(me)
                else:
                    keep.append((o, m))
            return keep
        return edit

    def closed_well(p, parts):
        wx0, wx1, wy0, wy1 = D.nose_ladder_layout(p)["well"]
        g = D.cs2_kit.grating("balc_probe", wx1 - wx0, wy1 - wy0, t=0.06,
                              loc=((wx0 + wx1) / 2.0, (wy0 + wy1) / 2.0, p.z_soffit - 0.03))
        return parts + D._m(g, "galv")

    def wing_back(p, parts):
        (l0, l1, lh), _g, _l = D.nose_access(p)
        ye = D.ground_flight(p)["edge_y"]
        g = D.cs2_kit.grating("balc_probe", l1 - l0, lh - ye, t=0.06,
                              loc=((l0 + l1) / 2.0, (ye + lh) / 2.0, p.z_soffit - 0.03))
        return parts + D._m(g, "galv")

    cases = (("base", "R2a", no_footing("lfoot_gf")), ("base", "R2c", wing_back),
             ("top", "R2a", no_footing("lfoot_nz")), ("top", "R2c", closed_well))
    for kind, label, edit in cases:
        lift_terminal.clear_scene()
        p = D.params_for(kind, False, D.load_carrier("dq"))
        parts = edit(p, D.build_station(p))
        t, table, _src = D.assemble(parts, False, p)
        me = t.data
        try:
            D.assert_revision2(t, me, table, D._face_parts(me), D._co(me), p)
        except D.ContractError as e:
            check(str(e).startswith(label), f"{kind} {label} negative raises ({str(e)[:100]})")
            continue
        check(False, f"{kind} {label} negative did NOT raise")
    # Revision 1c: the shipped Revision 2 base (straight run through the ladder-way)
    lift_terminal.clear_scene()
    D.GF_ON_BASE = False
    try:
        p = D.params_for("base", False, D.load_carrier("dq"))
        t, table, _src = D.assemble(D.build_station(p), False, p)
        me = t.data
        try:
            D.assert_revision2(t, me, table, D._face_parts(me), D._co(me), p)
            check(False, "pre-1c base (straight ground run) did NOT fail the climbing-room gate")
        except D.ContractError as e:
            check(str(e).startswith("R2c climbing room"),
                  f"pre-1c base fails the climbing-room gate ({str(e)[:150]})")
    finally:
        D.GF_ON_BASE = True


# --------------------------------------------------------------------------------------
# 2. folder checks (shared by the base smoke and the top builds)
# --------------------------------------------------------------------------------------

def run_main(kind, out, review=None, gondola=False):
    argv = ["--kind", kind, "--out", out] + (["--gondola"] if gondola else []) + ARGS +            (["--review", review] if review else [])
    D.main(argv)
    p = D.params_for(kind, gondola, D.load_carrier("gd8" if gondola else "dq"))
    name = p.stem + "A"
    return p, name, os.path.join(out, name)


def check_folder(p, name, asset_dir):
    tag = name
    files = sorted(os.listdir(asset_dir))
    with open(os.path.join(asset_dir, f"{name}.manifest.json"), encoding="utf-8") as fh:
        man = json.load(fh)
    # 2026-10-12: the _Emissive map exists only while a modeled lamp_ part does
    # (lift_terminal_detach.has_emissive_map). The fine-detail DQ stations ship FOUR maps, the gondola (lamp_eave*)
    # five
    n_lamps = int(((man.get("contract") or {}).get("emissive") or {}).get("lamps", 1))
    has_emi = n_lamps > 0
    maps = ("BaseColor", "Normal", "MaskMap", "ControlMask") + (("Emissive",) if has_emi else ())
    want = sorted([f"{name}.fbx", f"{name}_LOD1.fbx", f"{name}_LOD2.fbx", f"{name}_Win.fbx",
                   f"{name}_LOD1_Win.fbx", f"{name}_LOD2_Win.fbx", "settings.json",
                   f"{name}.manifest.json"] + [f"{name}_{m}.png" for m in maps])
    check(files == want, f"{tag} folder holds exactly {len(want)} files ({len(files)}): "
                         f"{'five maps incl. _Emissive' if has_emi else 'four maps, NO _Emissive'}")
    check(has_emi == (p.gondola or not D.FINE_DETAIL) and (f"{name}_Emissive.png" in (man.get("files") or {})) == has_emi,
          f"{tag} _Emissive map {'present' if has_emi else 'absent'} on disk and in the manifest's files "
          f"({n_lamps} lamp_ parts; gondola {p.gondola}, fine {D.FINE_DETAIL})")
    if not has_emi:
        em_blk = (man.get("detail1") or {}).get("emissive") or {}
        ce = (man.get("contract") or {}).get("emissive") or {}
        with open(os.path.join(asset_dir, "settings.json"), encoding="utf-8") as fh:
            st_txt = fh.read()
        check(em_blk.get("map") is None and em_blk.get("lit_texels") == 0 and ce.get("map") is None
              and ce.get("purpose") is None and ce.get("intensity") is None and "Emissive" not in st_txt
              and "emissive_map" in (man.get("fine_detail") or {}),
              f"{tag} no-emissive manifest blocks: detail1.emissive {em_blk.get('map')} / lit {em_blk.get('lit_texels')}, "
              f"contract.emissive {ce.get('map')} / {ce.get('purpose')} / {ce.get('intensity')}, settings.json "
              f"names no Emissive map")
    # validator (main already raised on ERROR. This re-runs it for the record)
    findings, _summary = cs2_validate.validate_asset_folder_ex(asset_dir, name, {
        "kind": "building", "lod0_cap": p.lod0_cap, "lod2_cap": p.lod2, "hero": True,
        "expected_lods": (1, 2), "expected_submeshes": ("Win",)})
    errs = [f for f in findings if f[0] == "ERROR"]
    check(not errs, f"{tag} validate_asset_folder 0 errors ({len(findings)} findings: "
                    f"{sorted({f[1] for f in findings})})")
    check((man.get("validation") or {}).get("errors") == 0,
          f"{tag} manifest records the validation ({man.get('validation')})")

    # -- channels ----------------------------------------------------------------------
    px = {m: cs2_validate.load_png_pixels(os.path.join(asset_dir, f"{name}_{m}.png"))
          for m in ("BaseColor", "MaskMap", "ControlMask", "Normal") + (("Emissive",) if has_emi else ())}
    base, msk, ctl, emi = px["BaseColor"], px["MaskMap"], px["ControlMask"], px.get("Emissive")
    nrm_px = px["Normal"]
    check(base[..., 3].min() > 0.999 and (emi is None or emi[..., 3].min() > 0.999),
          f"{tag} BaseColor{' and _Emissive' if has_emi else ''} alpha 255")
    check(msk[..., 2].max() < 1e-6, f"{tag} MaskMap B = 0")
    check(ctl[..., 3].max() < 1e-6, f"{tag} ControlMask A = 0")
    check(ctl[..., 0:3].sum(axis=-1).max() <= 1.0 + 1e-3,
          f"{tag} ControlMask R + G + B <= 1 (exclusive)")
    # fine-detail step 3b: the DQ's modeled box lamps are vanilla rows now (no lamp_ part left), so its _Emissive
    # would be lit nowhere. 2026-10-12: it is not baked or shipped at all (folder check above). The gondola keeps
    # its linear soffit lights (lamp_eave*) and their map
    if has_emi:
        check(emi[..., 0:3].max() > 0.2, f"{tag} _Emissive lit somewhere "
                                         f"(max {emi[..., 0:3].max():.2f}; {n_lamps} lamp_ parts)")
    else:
        check(D.FINE_DETAIL and not os.path.exists(os.path.join(asset_dir, f"{name}_Emissive.png")),
              f"{tag} no _Emissive map: no modeled lamp left (fine-detail: vanilla RoofLight / WallLight rows)")
    lum = base[..., 0:3].mean(axis=-1)
    p95 = {}
    for c, ch in enumerate("RGB"):
        sel = ctl[..., c] > 0.5
        p95[ch] = float(np.percentile(lum[sel], 95)) if sel.any() else None
    print(f"[bake-test] {tag} p95 BaseColor under ControlMask {p95}")
    # detail pass 1 (DECISIONS 171, slot_wear "free"): the weathering's edge wear lifts worn slot texels above the
    # 0.80 recolor albedo (measured p95 0.855 on the 512 smoke). A weathered build's upper bound is
    # P95_RB_WEATHERED
    wblk = man.get("detail1") or man.get("rev1") or {}
    rb = P95_RB_WEATHERED if (wblk.get("weather") or {}).get("intensity") else P95_RB
    for ch in "RB":
        check(p95[ch] is not None and rb[0] <= p95[ch] <= rb[1],
              f"{tag} p95 under ctl {ch} {p95[ch]} in {rb}")
    if not p.gondola:                       # the gondola has no slot-2 part (no pads)
        check(p95["G"] is not None and p95["G"] >= P95_G_MIN,
              f"{tag} p95 under ctl G {p95['G']} >= {P95_G_MIN} (pipeline review 4 open)")

    # -- sign bands (Revision 1 (e)) -------------------------------------------------------
    sb = man.get("sign_band") or {}
    A = base.shape[0]
    lay = D.sign_layout(D.plate_digits(p))
    owned = sorted(f"signband_{k}" for k in D.sign_rects(p))
    check(sorted(sb) == owned, f"{tag} manifest sign_band owned rects {sorted(sb)} == {owned}")
    for q, src in sorted(D.sign_shared(p).items()):
        sw_ = (sb.get(f"signband_{src}") or {}).get("shared_with")
        check(sw_ == f"signband_{q}" or (isinstance(sw_, list) and f"signband_{q}" in sw_),
              f"{tag} signband_{q} shares signband_{src}'s rect (stamped once)")
    check("name_band" not in man, f"{tag} manifest carries no stale name_band")
    light, dark = np.array(PLATE_LIGHT_SRGB), np.array(PLATE_DARK_SRGB)
    for side, info in sorted(sb.items()):
        r0, r1, c0, c1 = info["rect"]
        u0, v0, u1, v1 = info["reserved_uv"]
        want = [round(v0 * A), round(v1 * A), round(u0 * A), round(u1 * A)]
        check([r0, r1, c0, c1] == want, f"{tag} {side} rect {info['rect']} == reserved rect x {A} "
                                        f"{want} (pinned)")
        check(info["px_per_m"] >= info["px_per_m_min"] > 0
              and info["px_per_m_min"] >= 4.0 * 105.8 * A / 2048.0 - 0.01,
              f"{tag} {side} {info['px_per_m']} px/m >= {info['px_per_m_min']} (4x the old band)")
        check(info["digit_h_m"] >= 0.5 and info["digits"] == D.plate_digits(p),
              f"{tag} {side} digits '{info['digits']}' {info['digit_h_m']} m tall >= 0.5")
        b = base[r0:r1, c0:c1, 0:3]
        k = ctl[r0:r1, c0:c1]
        h, w = b.shape[:2]
        pu, pv = w / lay["W"], h / lay["H"]
        E = info["emblem_px"]
        cov = D.emblem_alpha(E)[::-1, :][:h, :min(E, w)]
        full, none = cov >= 0.999, cov <= 1e-6
        bm, km = b[:cov.shape[0], :cov.shape[1]], k[:cov.shape[0], :cov.shape[1]]
        dev = float(np.abs(bm[full] - np.array(MARK_SRGB)).max()) if full.any() else 9.0
        check(full.sum() > 0.1 * cov.size and dev <= 0.01,
              f"{tag} {side} emblem: {int(full.sum())} full-coverage texels are MARK_WHITE "
              f"(max dev {dev:.4f})")
        check(km[full][:, 2].max() < 1e-6 and km[full][:, 0].max() < 1e-6,
              f"{tag} {side} ControlMask B = R = 0 under the emblem")
        frac_b = float((km[none][:, 2] > 0.5).mean()) if none.any() else 0.0
        check(frac_b > 0.9, f"{tag} {side} band around the emblem stays slot 3 ({frac_b:.1%} B)")
        ps0, ps1, pt0, pt1 = lay["plate"]
        S, T = np.meshgrid((np.arange(w) + 0.5) / pu, (np.arange(h) + 0.5) / pv)
        plate = (S >= ps0) & (S <= ps1)
        border = plate & ((S - ps0 < D.PLATE_BORDER) | (ps1 - S < D.PLATE_BORDER)
                          | (T - pt0 < D.PLATE_BORDER) | (pt1 - T < D.PLATE_BORDER))
        field = plate & ~border
        check(float(k[plate][:, 0:3].max()) < 1e-6, f"{tag} {side} ControlMask 0 over the plate")
        # Revision 1b: vector digits, mixed in linear light by their anti-aliased coverage
        dcov = D.digit_coverage(lay, w, h, pu, pv)
        lin_l = D.cs2_preview._srgb_to_linear(light)
        lin_d = D.cs2_preview._srgb_to_linear(dark)
        c3 = dcov[..., None]
        want = D.cs2_preview._linear_to_srgb(lin_l * (1.0 - c3) + lin_d * c3)
        dmix = float(np.abs(b[field] - want[field]).max())
        dd = float(np.abs(b[border] - dark).max())
        dl = float(np.abs(b[field & (dcov <= 0.0)] - light).max())
        check(dmix <= 0.01 and dd <= 0.01 and dl <= 0.01,
              f"{tag} {side} plate: border dark, field = light / dark mixed by the digit coverage "
              f"(max dev mix {dmix:.4f} / border {dd:.4f} / light {dl:.4f})")
        g = D.digit_triangles(lay)
        area_px = g["area_m2"] * pu * pv
        ratio = float(dcov.sum()) / area_px
        check(0.8 <= ratio <= 1.2 and abs(float(dcov.sum()) - info["digit_texels"]) <= 1.0
              and info["digit_texels"] >= 0.8 * info["digit_texels_expected"]
              and info["digit_h_m"] >= 0.5 and info["digit_font"] == D.DIGIT_FONT,
              f"{tag} {side} vector digits '{info['digits']}' {info['digit_h_m']} m "
              f"({info['digit_font']}): coverage {dcov.sum():.1f} texels = {ratio:.3f} x the "
              f"outline area (manifest {info['digit_texels']} / {info['digit_texels_expected']})")
        mid = (field & (np.abs(b - light).max(axis=-1) > 0.02)
               & (np.abs(b - dark).max(axis=-1) > 0.02))
        check(info["digit_aa_levels"] >= D.DIGIT_AA_LEVELS_MIN and int(mid.sum()) > 0
              and float(dcov[~field].sum()) == 0.0,
              f"{tag} {side} anti-aliased edge: {info['digit_aa_levels']} coverage levels, "
              f"{int(mid.sum())} in-between texels in the PNG, nothing on the border")
        # Revision 1b plate edge: Normal tilts on the rounded edge, flat in the panel field
        n = nrm_px[r0:r1, c0:c1, 0:3] * 2.0 - 1.0
        tilt = np.degrees(np.arccos(np.clip(n[..., 2] / np.linalg.norm(n, axis=-1), -1.0, 1.0)))
        d_out = np.minimum(np.minimum(S, lay["W"] - S), np.minimum(T, lay["H"] - T))
        d_em = np.minimum(np.minimum(S, ps0 - S), np.minimum(T, lay["H"] - T))
        edge = d_out < D.SIGN_BEVEL_M
        top = edge & (lay["H"] - T < D.SIGN_BEVEL_M) & (S > 0.1) & (S < ps0 - 0.1)
        bot = edge & (T < D.SIGN_BEVEL_M) & (S > 0.1) & (S < ps0 - 0.1)
        pfield = ~plate & (d_em > D.SIGN_BEAD_INSET_M + D.SIGN_BEAD_W_M + 3.0 / pu)
        gt, gb = float(n[top][:, 1].mean()), float(n[bot][:, 1].mean())
        check(float(tilt[edge].max()) >= 10.0 and float(tilt[pfield].max()) <= 1.5
              and gt > 0.05 and gb < -0.05,
              f"{tag} {side} plate edge relief: edge tilt max {tilt[edge].max():.1f} deg, field "
              f"{tilt[pfield].max():.2f} deg, top bevel n.y {gt:+.2f}, bottom {gb:+.2f}")
        lum = b.mean(axis=-1)
        bg = ~plate & (np.pad(cov, ((0, h - cov.shape[0]), (0, w - cov.shape[1]))) <= 1e-6)
        rim, inner = bg & (d_em < 0.25 * D.SIGN_EDGE_AO_M), bg & (d_em > D.SIGN_EDGE_AO_M)
        occ_ratio = float(lum[rim].mean() / lum[inner].mean()) if rim.any() and inner.any() else 1.0
        check(occ_ratio < 0.97, f"{tag} {side} emblem panel edge occlusion: border / field "
                                f"luminance {occ_ratio:.3f} (< 0.97)")

    # -- T2 engine base, T3 budgets, T5 _Win, T6 names ----------------------------------
    eb = man.get("engine_base") or {}
    ebl = man.get("engine_base_lods") or {}
    check(eb.get("verdict") == "OK" and sorted(ebl) == ["1", "2"]
          and all(v.get("verdict") == "OK" and not v.get("blocked_edges") for v in ebl.values()),
          f"{tag} T2 manifest engine base OK on LOD0 + LOD1 + LOD2 ({eb.get('verdict')}, {ebl})")
    yup = Matrix.Rotation(math.radians(90.0), 4, "X") @ Matrix.Scale(0.01, 4)  # prefab cm -> m Z-up
    tris = man["tris"]
    for lvl, suf in ((0, ""), (1, "_LOD1"), (2, "_LOD2")):
        objs = cs2_validate.import_fbx_temp(os.path.join(asset_dir, f"{name}{suf}.fbx"))
        try:
            mesh = [o for o in objs if o.type == "MESH"]
            check(len(mesh) == 1, f"{tag} LOD{lvl} FBX holds one mesh")
            if not mesh:
                continue
            r = cs2_validate.ground_ring_report(mesh[0], matrix=yup)
            check(r["verdict"] == "OK" and not r["blocked_edges"] and not r["below_verts"],
                  f"{tag} T2 LOD{lvl} FBX ground ring {r['verdict']}, {r['ok_edges']} OK / "
                  f"{r['blocked_edges']} blocked / {r['below_verts']} below")
            n = tris_of(mesh[0].data)
            check(n == int(tris[str(lvl)]), f"{tag} T3 LOD{lvl} FBX {n} tris == manifest "
                                            f"{tris[str(lvl)]}")
        finally:
            cs2_validate.remove_objects(objs)
    n0, n1, n2 = (int(tris[str(k)]) for k in range(3))
    check(n0 <= p.lod0_cap, f"{tag} T3 LOD0 {n0} <= cap {p.lod0_cap}")
    check(n1 <= 0.5 * n0 and (not p.lod1_max or n1 <= p.lod1_max),
          f"{tag} T3 LOD1 {n1} <= 50 % LOD0 and <= {p.lod1_max}")
    check(n2 < p.lod2, f"{tag} T3 LOD2 {n2} < cap {p.lod2} (F9 margin {D.LOD2_MARGIN})")
    with open(os.path.join(asset_dir, "settings.json"), encoding="utf-8-sig") as fh:
        st = json.dumps(json.load(fh))
    check(f"{name}_LOD1" in st and f"{name}_LOD2" in st, f"{tag} T3 settings.json wires LOD1/LOD2")
    objs = cs2_validate.import_fbx_temp(os.path.join(asset_dir, f"{name}_Win.fbx"))
    try:
        mesh = [o for o in objs if o.type == "MESH"]
        cells = cs2_validate._win_cells(mesh[0].data) if mesh else {}
        # Revision 1 (f): cabin band 3 panes (6 tris, cell 7) + 4 housing window n-gons of 4 x (WIN_CORNER_SEGS +
        # 1) vertices (cell 13). Gondola draft 1: 1 operator pane (2 tris, cell 7) + 8 hall panes (16 tris, cell
        # 13)
        want_tris = 18 if p.gondola else 6 + 4 * (4 * (D.WIN_CORNER_SEGS + 1) - 2)
        want_cells = D.expected_room_ids(p)
        if p.gondola and D.GD_REV1:
            # revision 1: every curtain-wall pane (2 tris) in its room's cell, one cell per id
            gl = (man.get("gondola") or {}).get("glazing") or {}
            want_tris = int(gl.get("pane_tris", -1))
            want_cells = list(gl.get("room_ids") or [])
            check(0 < len(want_cells) <= 25 and want_tris == 2 * len(D.gd_hall_layout(p)["panes"]),
                  f"{tag} T5 revision 1 rooms -> {len(want_cells)} ids (<= 25), {want_tris} pane tris")
        check(mesh and tris_of(mesh[0].data) == want_tris and sorted(cells) == want_cells
              and man.get("tris_win") == want_tris
              and man.get("win_cells") == want_cells,
              f"{tag} T5 _Win {tris_of(mesh[0].data) if mesh else 0} tris (want {want_tris}), "
              f"cells {cells} (want {want_cells})")
    finally:
        cs2_validate.remove_objects(objs)
    # -- T7 front ladder to the ground (Revision 2, second look 3') ------------------------
    fl = man.get("front_ladder") or {}
    if p.gondola:
        check_gondola_folder(p, name, asset_dir, man)
        rep = cs2_validate._Report(name)
        cs2_validate.lint_asset_name(name, rep)
        check(cs2_asset.validate_asset_name(name) == name and not any(f[0] == "ERROR" for f in rep),
              f"{tag} T6 validate_asset_name + lint_asset_name clean")
        return man
    check_detail1(p, name, asset_dir, man)
    nl = D.nose_ladder_layout(p)
    hw = D.nose_ladder_half(p)
    gf = nl["ground"]                       # Revision 1c: the base's ground flight (else None)
    if gf is not None:
        fx0, fx1, fy0, fy1, _fz0, fz1 = gf["foot"]
        g = fl.get("ground_flight") or {}
        check(g.get("footing", {}).get("open_bottom") is True and fl.get("cage") is False
              and abs(g.get("rails_z", [9, 9])[0] - fz1) < 1e-3
              and g.get("rungs", {}).get("n") == len(gf["rungs"])
              and g.get("climb_room_m", {}).get("fixed", 0) >= D.CLIMB_MIN
              and g.get("climb_room_m", {}).get("chairs", 0) >= D.CLIMB_MIN
              and abs(g.get("step_across_m", 0) - D.GF_STEP) < 1e-3
              and abs(fl.get("rails_z", [9, 9])[0] - p.z_soffit) < 1e-3,
              f"{tag} T7 manifest front_ladder (1c): ground flight on an open-bottom footing, rails "
              f"from {fz1}, {len(gf['rungs'])} rungs, climbing room {g.get('climb_room_m')} >= "
              f"{D.CLIMB_MIN}, step-across {g.get('step_across_m')}; front ladder from the landing")
    else:
        fx0, fx1, fy0, fy1, _fz0, fz1 = nl["foot"]
        check(fl.get("footing", {}).get("open_bottom") is True and fl.get("cage") is False
              and abs(fl.get("rails_z", [9, 9])[0] - fz1) < 1e-3
              and fl.get("rungs", {}).get("pitch_m") == D.NZ_PITCH
              and fl.get("rungs", {}).get("n") == len(nl["rungs"]),
              f"{tag} T7 manifest front_ladder: open-bottom footing, rails from {fz1}, "
              f"{len(nl['rungs'])} rungs at {D.NZ_PITCH}, no cage ({fl.get('rungs')})")
    for lvl, suf in ((0, ""), (1, "_LOD1"), (2, "_LOD2")):
        objs = cs2_validate.import_fbx_temp(os.path.join(asset_dir, f"{name}{suf}.fbx"))
        try:
            mesh = [o for o in objs if o.type == "MESH"]
            if not mesh:
                check(False, f"{tag} T7 LOD{lvl} FBX mesh")
                continue
            raw = np.array([tuple(v.co) for v in mesh[0].data.vertices], dtype=np.float64) / 100.0
            a = raw[:, [2, 0, 1]]                   # prefab (x, y up, z travel) -> authoring
            inf = ((a[:, 0] >= fx0 - 1e-4) & (a[:, 0] <= fx1 + 1e-4) & (a[:, 1] >= fy0 - 1e-4)
                   & (a[:, 1] <= fy1 + 1e-4))
            ground = int((inf & (np.abs(a[:, 2]) <= 1e-4)).sum())
            rails = a[(np.abs(a[:, 0] - nl["x"]) <= 0.035)
                      & (np.abs(np.abs(a[:, 1] - nl["y"]) - hw) <= 0.035)]
            if lvl < 2 and gf is not None:
                # 1c: the ground flight's rails (x0 / x1 on the rung line y) from the footing to the landing +
                # GF_EXT. The front ladder's run from the landing to the crown
                grails = a[(np.minimum(np.abs(a[:, 0] - gf["x"][0]), np.abs(a[:, 0] - gf["x"][1])) <= 0.035)
                           & (np.abs(a[:, 1] - gf["y"]) <= 0.035) & (a[:, 2] <= gf["z_top"] + 1e-3)]
                ok = (ground >= 4 and len(grails) and len(rails)
                      and abs(grails[:, 2].min() - fz1) < 1e-3
                      and grails[:, 2].max() >= gf["z_top"] - 1e-3
                      and abs(rails[:, 2].min() - p.z_soffit) < 2e-3
                      and rails[:, 2].max() >= p.z_crown + 0.35)
                check(ok, f"{tag} T7 LOD{lvl} FBX: ground flight footing on the ground ({ground} verts "
                          f"at z 0), its rails z {grails[:, 2].min() if len(grails) else 0:.3f} -> "
                          f"{grails[:, 2].max() if len(grails) else 0:.3f}; front ladder z "
                          f"{rails[:, 2].min() if len(rails) else 0:.3f} -> "
                          f"{rails[:, 2].max() if len(rails) else 0:.3f}")
            elif lvl < 2:
                ok = (ground >= 4 and len(rails) and abs(rails[:, 2].min() - fz1) < 1e-3
                      and rails[:, 2].max() >= p.z_crown + 0.35)
                check(ok, f"{tag} T7 LOD{lvl} FBX: footing on the ground ({ground} verts at z 0), "
                          f"rails z {rails[:, 2].min() if len(rails) else 0:.3f} -> "
                          f"{rails[:, 2].max() if len(rails) else 0:.3f}")
            else:
                check(ground == 0, f"{tag} T7 LOD2 FBX: footing dropped with the rails "
                                   f"({ground} verts left)")
        finally:
            cs2_validate.remove_objects(objs)
    rep = cs2_validate._Report(name)
    cs2_validate.lint_asset_name(name, rep)
    check(cs2_asset.validate_asset_name(name) == name and not any(f[0] == "ERROR" for f in rep),
          f"{tag} T6 validate_asset_name + lint_asset_name clean")
    stems = sorted(os.path.splitext(f)[0] for f in files if f.endswith((".fbx", ".png")))
    check(all(s == name or (s.startswith(name + "_") and "_" not in name) for s in stems),
          f"{tag} T6 every FBX / PNG is <Name>[_suffix]")
    return man


# --------------------------------------------------------------------------------------
# 3. --review byte identity (top)
# --------------------------------------------------------------------------------------

def check_detail1(p, name, asset_dir, man):
    """Detail pass 1 (DQ) in a bake folder: the manifest's detail1 block, every dressing label
    pinned in its sheet rect (ControlMask 0 over it, painted), the weathering ladder and its
    guards, bay sharing, the vanilla sub-object rows (verified GUIDs)."""
    import cs2_liftdress as LD
    tag = name
    d = man.get("detail1")
    if not check(d is not None, f"{tag} manifest carries the detail1 block"):
        return
    A = int(man["atlas_px"])
    k = A / float(D.ATLAS)
    sheet = D.label_sheet(p)
    labs = d.get("labels") or {}
    check(sorted(labs) == sorted(D.d1_labels(p)), f"{tag} detail1 labels {sorted(labs)}")
    base = cs2_validate.load_png_pixels(os.path.join(asset_dir, f"{name}_BaseColor.png"))
    ctl = cs2_validate.load_png_pixels(os.path.join(asset_dir, f"{name}_ControlMask.png"))
    for nm, (x0, y0, x1, y1) in sorted(sheet.items()):
        want = [int(round(y0 * k)), int(round(y1 * k)), int(round(x0 * k)), int(round(x1 * k))]
        info = labs.get(nm) or {}
        r0, r1, c0, c1 = want
        sl = base[r0:r1, c0:c1, 0:3]
        check(info.get("rect") == want and float(ctl[r0:r1, c0:c1, 0:3].max()) < 1e-6
              and float(sl.std()) > 0.02,
              f"{tag} {nm} pinned at {want} (sheet x {k}), ControlMask 0, painted (std {sl.std():.3f})")
    w = d.get("weather") or {}
    g = w.get("guards") or {}
    check(w.get("intensity") in [round(s, 2) for s in LD.INTENSITY_STEPS] and w.get("ladder")
          and w.get("snow") == 0.0 and w.get("slot_wear") == "free"
          and g.get("G5_ctl_identity") and g.get("G6_protect_identical") and g.get("G2_msk_GB_kept"),
          f"{tag} weathering {w.get('intensity')} (ladder {[(r['intensity'], r['ok']) for r in w.get('ladder') or []]}), "
          f"ControlMask untouched, protected rects identical, MaskMap G/B kept")
    check((d.get("shared_faces") or 0) > 0 and (d.get("share") or {}).get("groups", 0) > 0,
          f"{tag} bay sharing: {d.get('shared_faces')} shared faces in {(d.get('share') or {}).get('groups')} groups")
    sub = d.get("sub_objects") or {}
    check_rows(tag, p, sub, D.dq_subobjects(p))


def check_rows(tag, p, sub, vanilla_rows):
    """The sub-object block: the vanilla rows (verified GUIDs) + fine-detail step 3b's kit rows ("kind": "mod",
    guid CID:<the staged object-prefab CID>), parent 0, the block's kinds count."""
    import cs2_liftdress as LD
    import fine_detail as FD
    objs = sub.get("objects") or []
    van = [o for o in objs if o.get("kind") != "mod"]
    # T17-DQ 2026-10-16: lamp posts -> SkiPisteFloodlightLow: the 2 floodlight "mod" rows per station are not kit
    # rows (no staged kit CID). Literal expectations (game local pos, yaw quaternion x y z w)
    fl = [o for o in objs if o.get("kind") == "mod" and o["prefab"] == "SkiPisteFloodlightLow"]
    mod = [o for o in objs if o.get("kind") == "mod" and o["prefab"] != "SkiPisteFloodlightLow"]
    q155, q205 = (0.0, 0.976296, 0.0, 0.2164396), (0.0, 0.976296, 0.0, -0.2164396)
    zg = -9.0 if p.kind == "base" else -11.0
    want_fl = ([((7.2, 0.0, zg), q205), ((-7.2, 0.0, zg), q155)] if p.gondola else
               [((-5.1, 0.3, 2.4), (0.0, 0.9914449, 0.0, 0.1305262)), ((7.1, 0.3, 5.2), q205)] if p.kind == "base" else
               [((5.3, 0.3, -9.6), (0.0, 0.9914449, 0.0, -0.1305262)), ((7.1, 0.3, 0.0), q205)])
    check(len(fl) == 2 and not any(str(o["prefab"]).startswith("LightpolePark") for o in objs)
          and all(o["guid"] == "CID:6827e281427c087a3b7ef19c824ca28b" and o["mesh"] == "SkiPisteFloodlightLowA Mesh"
                  and o["parent"] == 0 and all(abs(a - b) < 1e-6 for a, b in zip(o["pos"], pw))
                  and all(abs(a - b) < 1e-6 for a, b in zip(o["rot"], qw)) for o, (pw, qw) in zip(fl, want_fl)),
          f"{tag} T17-DQ: no LightpolePark rows, 2 SkiPisteFloodlightLow rows {[o['pos'] for o in fl]}")
    guids = {"UnityGUID:" + v["guid"] for v in LD.SUBOBJ.values()}
    check(len(van) == len(vanilla_rows) and all(o["guid"] in guids and o["parent"] == 0 for o in van),
          f"{tag} vanilla sub-object rows {len(van)} (verified GUIDs, parent 0) for {sub.get('target_prefab')}")
    specs = D.fd_kit_specs(p)
    check(len(mod) == len(specs) and all(o["guid"] == "CID:" + FD.kit_cid(o["prefab"], o.get("colour_set"))
                                         == "CID:" + str(FD.staged_kit_cid(o["prefab"], o.get("colour_set")))
                                         and o["parent"] == 0 and o["mesh"] == FD.KIT[o["prefab"]].mesh for o in mod)
          and (not D.FINE_DETAIL or sub.get("kinds") == {"vanilla": len(van), "mod": len(mod) + len(fl)}),
          f"{tag} kit rows {len(mod)} (staged CIDs, parent 0): {sorted(o['prefab'] for o in mod)}; kinds {sub.get('kinds')}")


def check_gondola_folder(p, name, asset_dir, man):
    """Gondola draft 1 follow-up in a bake folder: the manifest's G3 doors (three real openings),
    the real-cabin sweep record, the nose ladder's footing on the ground with its rails in the LOD0 /
    LOD1 FBX, no footing at LOD2."""
    tag = name
    g = man.get("gondola") or {}
    doors = g.get("doors") or {}
    check(sorted(doors) == ["n", "opr", "p"] and all(v.get("rays_clear") == 9 for v in doors.values()),
          f"{tag} G3 doors are real openings: {sorted(doors)}")
    if D.GD_REV1:
        check_gondola_rev1(p, name, asset_dir, man, g, doors)
    sw = g.get("cabin_sweep") or {}
    check((sw.get("min_clear_m") or 0) >= D.GD_CLEAR - 1e-6, f"{tag} G8 sweep {sw.get('min_clear_m')} >= {D.GD_CLEAR}")
    nl = (g.get("front_ladder") or {}).get("nose_ladder") or {}
    room = nl.get("climb_room") or {}
    check(nl.get("foot_ok_edges", 0) >= 4 and room.get("fixed", [0])[0] >= D.CLIMB_MIN
          and room.get("chairs", [0])[0] >= D.CLIMB_MIN,
          f"{tag} G4 nose ladder: footing {nl.get('foot_ok_edges')} OK edges, climbing room {room}")
    L = D.gd_ladder_layout(p)
    fx0, fx1, fy0, fy1, _fz0, fz1 = L["foot"]
    for lvl, suf in ((0, ""), (1, "_LOD1"), (2, "_LOD2")):
        objs = cs2_validate.import_fbx_temp(os.path.join(asset_dir, f"{name}{suf}.fbx"))
        try:
            mesh = [o for o in objs if o.type == "MESH"]
            if not mesh:
                check(False, f"{tag} T7 LOD{lvl} FBX mesh")
                continue
            raw = np.array([tuple(v.co) for v in mesh[0].data.vertices], dtype=np.float64) / 100.0
            a = raw[:, [2, 0, 1]]
            inf = ((a[:, 0] >= fx0 - 1e-4) & (a[:, 0] <= fx1 + 1e-4) & (a[:, 1] >= fy0 - 1e-4)
                   & (a[:, 1] <= fy1 + 1e-4))
            ground = int((inf & (np.abs(a[:, 2]) <= 1e-4)).sum())
            rails = a[(np.abs(a[:, 0] - L["x"]) <= 0.035) & (np.abs(np.abs(a[:, 1] - L["y"]) - 0.28) <= 0.035)]
            if lvl < 2:
                check(ground >= 4 and len(rails) and abs(rails[:, 2].min() - fz1) < 1e-3
                      and rails[:, 2].max() >= L["z_rim"] + 1.0,
                      f"{tag} T7 LOD{lvl} FBX: nose ladder footing on the ground ({ground} verts at z 0), "
                      f"rails z {rails[:, 2].min() if len(rails) else 0:.3f} -> "
                      f"{rails[:, 2].max() if len(rails) else 0:.3f}")
            else:
                check(ground == 0, f"{tag} T7 LOD2 FBX: footing dropped with the rails ({ground} verts left)")
        finally:
            cs2_validate.remove_objects(objs)


def check_gondola_rev1(p, name, asset_dir, man, g, doors):
    """Gondola revision 1 in a bake folder: the double skier doors, the lot, the G5 / G9 / G10
    records, the vanilla sub-object rows (verified GUIDs, parent 0, four lamp posts), the labels
    pinned in the freed strip, the weathering step and its guards, the hosted tower seam (base)."""
    import cs2_liftdress as LD
    tag = name
    check(doors["n"].get("opening_m") == list(D.GD_SIDE_DOOR) and doors["p"].get("opening_m") == list(D.GD_SIDE_DOOR),
          f"{tag} G3 revision 1 skier entries are {D.GD_SIDE_DOOR} m double doors")
    # DQ follow-up (rev-1 checker F3 / F4): the clear opening proved with the fixed frame counted (no center post,
    # head at the platform + 2.30), and the standable-top census declared
    check(all(doors[s_].get("clear_opening_m") == list(D.GD_SIDE_DOOR) and doors[s_].get("clear_rays", 0) >= 54
              and doors[s_].get("centre_post") is False
              and abs(doors[s_].get("head_y", 0) - (D.DECK_H + D.GD_SIDE_DOOR[1])) < 1e-6 for s_ in ("n", "p")),
          f"{tag} G3 clear opening {D.GD_SIDE_DOOR} m, no center post, head {D.DECK_H + D.GD_SIDE_DOOR[1]:.2f} "
          f"({doors['p'].get('clear_rays')} rays per door)")
    nw = g.get("not_walkable") or []
    check(nw and all(isinstance(x, dict) and x.get("why") for x in nw)
          and any(x.get("part") == "opr_roof" for x in nw) and any(str(x.get("part")).startswith("canopy_") for x in nw),
          f"{tag} G3 standable-top census: {[x.get('part') for x in nw]} declared with reasons")
    c = man.get("contract") or {}
    check(c.get("lot") == list(D.GD_LOT), f"{tag} G9 lot {c.get('lot')} == {list(D.GD_LOT)}")
    for k_ in ("G5", "G9", "G10"):
        check(k_ in (man.get("contracts") or {}), f"{tag} contract {k_} recorded")
    rb_ = man.get("rev1") or {}
    sub = rb_.get("sub_objects") or {}
    objs = sub.get("objects") or []
    # T17-DQ 2026-10-16: lamp posts -> SkiPisteFloodlightLow: 0 vanilla lamp posts, 2 floodlight rows (the
    # literal positions / yaws are asserted in check_rows)
    check(sum(1 for o in objs if o["prefab"].startswith("LightpolePark")) == 0
          and sum(1 for o in objs if o["prefab"] == "SkiPisteFloodlightLow") == 2,
          f"{tag} G10 sub-object rows: 0 lamp posts, 2 SkiPisteFloodlightLow")
    check_rows(tag, p, sub, D.gd_subobjects(p))
    A = int(man["atlas_px"])
    k = A / float(D.ATLAS)
    base = cs2_validate.load_png_pixels(os.path.join(asset_dir, f"{name}_BaseColor.png"))
    ctl = cs2_validate.load_png_pixels(os.path.join(asset_dir, f"{name}_ControlMask.png"))
    labs = rb_.get("labels") or {}
    check(sorted(labs) == sorted(D.d1_labels(p)), f"{tag} revision 1 labels {sorted(labs)}")
    for nm, (x0, y0, x1, y1) in sorted(D.label_sheet(p).items()):
        want = [int(round(y0 * k)), int(round(y1 * k)), int(round(x0 * k)), int(round(x1 * k))]
        r0, r1, c0, c1 = want
        check((labs.get(nm) or {}).get("rect") == want and float(ctl[r0:r1, c0:c1, 0:3].max()) < 1e-6
              and float(base[r0:r1, c0:c1, 0:3].std()) > 0.02,
              f"{tag} {nm} pinned at {want}, ControlMask 0, painted")
    w = rb_.get("weather") or {}
    gd = w.get("guards") or {}
    check(w.get("intensity") in [round(s_, 2) for s_ in LD.INTENSITY_STEPS] and w.get("snow") == 0.0
          and w.get("slot_wear") == "free" and gd.get("G5_ctl_identity") and gd.get("G6_protect_identical"),
          f"{tag} revision 1 weathering {w.get('intensity')} (station preset, guards held)")
    if p.hosted_tower:
        ts = g.get("tower_seam") or {}
        gaps = {k2: (v2 or {}).get("min_gap_m") for k2, v2 in ts.items()}
        check(sorted(ts) == ["LOD0", "LOD1"] and all(v is not None for v in gaps.values()),
              f"{tag} G6 the real SkiLiftTowerETGDO LOD0 / LOD1 seam recorded ({gaps})")


def gd_negatives():
    """Gondola draft 1 follow-up (draft1-check-gondola-stations F5): every G contract must FAIL on a deliberately
    wrong build. assert_gondola runs on a blockout, one fault each."""
    import lift_terminal_detach as DD

    def box(name, x, y, z, h=0.10):
        return (DD._box(name, x - h, x + h, y - h, y + h, z - h, z + h), "galv")

    def drop_part(name):
        def edit(p, parts):
            keep = []
            for o, m in parts:
                if o.name == name:
                    me = o.data
                    bpy.data.objects.remove(o, do_unlink=True)
                    bpy.data.meshes.remove(me)
                else:
                    keep.append((o, m))
            return keep
        return edit

    def add(maker):
        return lambda p, parts: parts + [maker(p)]

    cases = (
        ("G1", "base", {"GD_PORTAL": dict(DD.GD_PORTAL, u_hi=1.30)}, None,
         "a portal narrowed to u_hi 1.30 (the body box + 0.30 reaches 1.39)"),
        ("G2", "base", {"GD_PLAT_IN": 4.20}, None, "the boarding edge at |y| 4.20 (0.11 m off the cabin)"),
        ("G3", "base", {"GD_DOOR_OPENINGS": False}, None,
         "the doors as draft 1's leaves on solid walls (no opening behind them)"),
        ("G4", "base", {}, add(lambda p: box("cabinet_g4probe", p.x_nose - 0.50, DD.GD_NL_Y, 3.0)),
         "a box 0.28 m in front of the nose ladder's rungs"),
        ("G5", "top", {}, drop_part("win_n02_n02t1"), "a curtain-wall pane removed (a hole in the glazing)"),
        ("G6", "base", {}, add(lambda p: box("cabinet_g6probe", p.x_nose - 1.0, 0.0, 1.0)),
         "a box in the hosted entry tower's keep-out"),
        ("G7", "top", {"GD_SIGN_NOSE_Z": dict(DD.GD_SIGN_NOSE_Z, top=7.00)}, None,
         "the top's portal sign raised to 7.00 (its top into the nose ring beam)"),
        ("G8", "base", {}, add(lambda p: box("cabinet_g8probe", p.x_wheel - 6.0,
                                             p.lane_y + 1.094 + 0.16, 1.0, h=0.05)),
         "a box 0.11 m off the real cabin's outboard side on the -X straight"),
        ("G9", "top", {}, add(lambda p: box("cabinet_g9probe", 0.0, DD.GD_LOT_HALF[1] + 0.40, 1.0)),
         "a box past the 3 x 4 lot's lateral line (|y| 12.4)"),
        ("G10", "base", {}, add(lambda p: box("board_g10probe", DD.gd_hall_layout(p)["mullions_x"][2],
                                              p.w_half, 3.5, h=0.08)),
         "a dressing board pushed into a mullion (an undeclared part clash)"),
        # DQ follow-up (rev-1 checker F3 / F4): the clear-opening rays count the fixed door frame, and every
        # standable flat top is on a route or declared
        ("G3", "base", {}, add(lambda p: (DD._box("door_p_centrepost", DD.gd_hall_layout(p)["xd"] - 0.03,
                                                   DD.gd_hall_layout(p)["xd"] + 0.03, p.w_half - 0.08,
                                                   p.w_half + 0.04, DD.DECK_H, DD.GD_DOOR_HEAD), "paint1")),
         "revision 1's fixed 0.06 m center post back in the +Y double door (two 0.87 m passages)"),
        ("G3", "top", {}, add(lambda p: (DD._box("shelf_g3probe", p.x_back + 2.5, p.x_back + 3.5, -0.5, 0.5,
                                                  1.00, 1.05), "galv")),
         "an undeclared 1 x 1 m flat top (the standable-top census)"),
    )
    for label, kind, patch, edit, why in cases:
        saved = {k: getattr(DD, k) for k in patch}
        try:
            for k, v in patch.items():
                setattr(DD, k, v)
            lift_terminal.clear_scene()
            p = DD.params_for(kind, True, DD.load_carrier("gd8"))
            parts = DD.build_station(p)
            if edit is not None:
                parts = edit(p, parts)
            t, table, _src = DD.assemble(parts, False, p)
            me = t.data
            try:
                DD.assert_gondola(t, me, table, DD._face_parts(me), DD._co(me), p)
            except DD.ContractError as e:
                check(str(e).startswith(label), f"gondola {label} negative raises: {why} ({str(e)[:110]})")
                continue
            check(False, f"gondola {label} negative did NOT raise: {why}")
        finally:
            for k, v in saved.items():
                setattr(DD, k, v)


def g11_checks():
    """Nitpick A2 (2026-10-13): G11 - the gondola operator room on the station's up lane (the base's departing +Y, the
    top's ARRIVING -Y) with LIFT OPS, the operator-door wall light and the hose reel. The stair and the deck's
    stairwell are on the other side. Positives on both stations (fine on, as shipped). Negatives must raise: the
    old top (GD_OPR_LEGACY: the room on +Y), the base's room flipped onto -Y, the top's LIFT OPS board + label left
    on +Y, the top's hose-reel row left on the +Y wall. Nitpick A3 (F2): WAIT HERE at the loading (+Y) entry on
    both stations (positives). The pre-2026-10-13 -Y board (GD_WAIT_LEGACY) raises on each."""
    import lift_terminal_detach as DD
    out = {}

    def run(kind, patch=None, edit=None):
        saved = {k: getattr(DD, k) for k in (patch or {})}
        try:
            for k, v in (patch or {}).items():
                setattr(DD, k, v)
            lift_terminal.clear_scene()
            p = DD.params_for(kind, True, DD.load_carrier("gd8"))
            parts = DD.build_station(p)
            if edit is not None:
                parts = edit(p, parts)
            t, table, _src = DD.assemble(parts, False, p)
            me = t.data
            return DD.assert_orientation_gd(me, table, DD._face_parts(me), DD._co(me), p)
        finally:
            for k, v in saved.items():
                setattr(DD, k, v)

    for kind, want in (("base", 1.0), ("top", -1.0)):
        r, nums = run(kind)
        cy = nums["measured_centre_y"]
        check(nums["up_lane_y_authoring"] == want and nums["operator_room_side_y"] == want
              and all(v * want > 0.0 for v in cy.values()) and nums["stair_centre_y"] * want < 0.0,
              f"G11 gondola {kind}: the room + LIFT OPS on y {want:+.0f}, the stair opposite ({r['G11']}; {cy})")
        # nitpick A3 (F2): WAIT HERE at the loading (+Y) entry on BOTH stations, facing +Y
        wh = nums["wait_here"]
        check(wh["loading_side_y"] == 1.0 and all(v > 4.0 for v in wh["centre_y"].values())
              and wh["label_normal_y"] >= 0.9,
              f"G11 gondola {kind}: WAIT HERE at the loading +Y entry ({wh})")
        out[kind] = cy

    def back_to_plus(prefixes):
        def edit(p, parts):
            for o, _m in parts:
                if o.name.startswith(prefixes):
                    DD._mirror_part_y(o)
            return parts
        return edit

    cases = (
        ("top", {"GD_OPR_LEGACY": True}, None, "the pre-2026-10-13 top (room on +Y, the departing side)"),
        ("base", {"GD_OPR_SIDE": dict(DD.GD_OPR_SIDE, base=-1.0)}, None, "the base's room flipped onto -Y"),
        ("top", {}, back_to_plus(("board_ops", "label_ops")), "the top's LIFT OPS board + label left on +Y"),
        # nitpick A3 (F2): the pre-2026-10-13 WAIT HERE at the -Y (arriving) entry, on either station
        ("base", {"GD_WAIT_LEGACY": True}, None, "the base's WAIT HERE at the -Y (arriving) entry"),
        ("top", {"GD_WAIT_LEGACY": True}, None, "the top's WAIT HERE at the -Y (arriving) entry"),
    )
    for kind, patch, edit, why in cases:
        try:
            run(kind, patch, edit)
        except DD.ContractError as e:
            check(str(e).startswith("G11"), f"gondola G11 negative raises: {why} ({str(e)[:120]})")
            continue
        check(False, f"gondola G11 negative did NOT raise: {why}")
    # the reel row alone back on the +Y wall (fd_kit_specs patched): G11 reads the row
    orig = DD.fd_kit_specs

    def specs_plus(p, _f=orig):
        s = _f(p)
        for d in s:
            if d["prop"] == "SkiDressHoseReel":
                d["pos"] = (d["pos"][0], abs(d["pos"][1]), d["pos"][2])
        return s
    DD.fd_kit_specs = specs_plus
    try:
        run("top")
    except DD.ContractError as e:
        check(str(e).startswith("G11"), f"gondola G11 negative raises: the top's hose-reel row on +Y ({str(e)[:120]})")
    else:
        check(False, "gondola G11 negative did NOT raise: the top's hose-reel row on +Y")
    finally:
        DD.fd_kit_specs = orig
    return out


def manifest_core(path):
    with open(path, encoding="utf-8") as fh:
        m = json.load(fh)
    m.pop("written_utc", None)
    return m


def review_identity(work):
    freeze_fbx_clock()
    plain_out, rev_out, pack_dir = (os.path.join(work, d) for d in ("plain", "reviewed", "pack"))
    for d in (plain_out, rev_out, pack_dir):
        os.makedirs(d, exist_ok=True)
    p, name, a = run_main("top", plain_out)
    check_folder(p, name, a)
    _p, _n, b = run_main("top", rev_out, review=pack_dir)
    ha, hb = file_hashes(a), file_hashes(b)
    check(sorted(ha) == sorted(hb), f"--review: same file set ({len(ha)} vs {len(hb)})")
    mf = f"{name}.manifest.json"
    for fn in sorted(ha):
        if fn == mf:
            continue
        same = ha[fn] == hb.get(fn)
        print(f"[bake-test]   {fn:<34} {ha[fn][:12]} {'==' if same else '!='} "
              f"{(hb.get(fn) or '-')[:12]}")
        check(same, f"--review: {fn} byte-identical")
    check(manifest_core(os.path.join(a, mf)) == manifest_core(os.path.join(b, mf)),
          "--review: manifests equal without written_utc")
    check(os.path.isfile(os.path.join(pack_dir, f"{name}.blend")), "--review: pack written")
    check(not any(f.endswith(".blend") for f in os.listdir(b)), "--review: no pack in the asset folder")


def main():
    t0 = time.perf_counter()
    work = tempfile.mkdtemp(prefix="detach_bake_")
    try:
        c8_negative()
        r1_negatives()
        r2_negatives()
        gd_negatives()
        g11_checks()                 # nitpick A2: the gondola operator room on the up lane
        # 2026-10-12: a STALE <name>_Emissive.png from an earlier export sits in the folder (the bake never deletes
        # a file). The generator removes it before the bake because the fine-detail DQ has no lamp_ part left
        stale = os.path.join(work, "base", "SkiDQBaseStationA", "SkiDQBaseStationA_Emissive.png")
        os.makedirs(os.path.dirname(stale), exist_ok=True)
        with open(stale, "wb") as fh:
            fh.write(b"stale _Emissive.png of an earlier export")
        p, name, asset_dir = run_main("base", os.path.join(work, "base"))
        check(not os.path.exists(stale), "a stale DQ base _Emissive.png from an earlier export is removed by the bake")
        man = check_folder(p, name, asset_dir)
        print(f"[bake-test] base tris {man['tris']}, sign band {sorted(man['sign_band'])}")
        # gondola draft 1 follow-up: the bake smoke the checker found missing (F5)
        p, name, asset_dir = run_main("base", os.path.join(work, "gbase"), gondola=True)
        man = check_folder(p, name, asset_dir)
        print(f"[bake-test] gondola base tris {man['tris']}, sign band {sorted(man['sign_band'])}")
        review_identity(work)
    finally:
        shutil.rmtree(work, ignore_errors=True)
    if FAILS:
        print(f"FAIL test_terminal_detach_bake: {len(FAILS)} failure(s): {FAILS[:6]}")
        sys.exit(1)
    print(f"PASS test_terminal_detach_bake: C8 / R1d / R1b / R2a / R2c negatives raise (base + top), "
          f"the pre-1c base fails the climbing-room gate, gondola G1-G10 each fail on a wrong build; "
          f"nitpick A2 G11 (the gondola operator room on the up lane: base +Y, top -Y) holds and 4 wrong builds "
          f"raise; nitpick A3 WAIT HERE at the loading +Y entry on both (the old -Y board raises on each); "
          f"DQ base + top and gondola base bake "
          f"through main() (512, AO 4): validator 0 errors, alpha 255, ControlMask exclusive, "
          f"_Emissive (gondola) / no _Emissive + a stale one removed (DQ), pinned sign bands (emblem + vector-digit plate, plate-edge relief; DQ +Y on "
          f"the -Y rect, gondola p and q on n), detail-pass labels pinned + weathering + bay sharing (DQ), "
          f"real door openings (gondola), the gondola revision 1 glazed hall (rooms -> ids, labels, "
          f"weathering, vanilla rows, lot 3 x 4, the real entry tower seam), engine base on every LOD, "
          f"budgets, _Win cells, names, ladder footings on the ground (LOD0/1, gone at LOD2); "
          f"top --review byte-identical "
          f"({time.perf_counter() - t0:.0f} s)")


try:
    main()
except SystemExit:
    raise
except Exception:  # noqa: BLE001
    import traceback
    traceback.print_exc()
    print("FAIL test_terminal_detach_bake: exception")
    sys.exit(1)
