"""
Gate for the tram stations' detail pass 1 (design/detail-pass-1-lifts.md sections 5 / 6 / 9.2 as amended
by its skeptic MC1-MC11; build report build/detail-pass-1-lifts-tram.md).

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_detail_tram.py

  D1  dressing counts per station (blockout): the exact dressing part sets (labels, boards, ticket frames,
      chevrons, nosings / berth stripes, extinguishers, hose reels, tools, ski rack, EXIT light, beacon,
      hook block, gauge); every dressing family prefix the LOD lists delete names dressing parts only;
      the kit contact pairs tram_station declares are cs2_liftdress.DRESS_CONTACTS pairs.
  D2  emissive (shipped): lit texels inside the gates; the LED board and the clock dials keep dark marks in
      `_Emissive`; the beacon lens texels are red (R >= 4 G).
  D3  labels (shipped manifest + PNGs): design L1 (<= 1.5 m: >= 400 px/m, each UV axis >= 380), L2 (the
      departure board / TICKETS at their targets), T1 / T2 flags, ControlMask 0 under every label, C13 on
      the shipped layout lists every label.
  D4  weathering: (a) shipped manifests: the station preset at the highest budget-clean intensity (the
      ladder's next step WARNs, the chosen one is ok), slot wear free, no snow; (b) in-process at 512:
      weather on vs off with the same geometry, sharing and labels -> ControlMask byte-identical, MaskMap
      G / B identical, every stamped label / dial rect identical in BaseColor (protected), BaseColor
      changed elsewhere.
  D5  determinism: two finish builds in one process (512, weather on) -> identical texture bytes.
  D6  frozen prefab numbers (design 9.1 + the 1c changes) on the shipped manifests.
  D7  access: C15 on every variant (no orphan, every measured join >= 0.61 or redundant, the gangway
      mouth >= 0.61, the interior floors declared not walkable by name).
  D8  the vanilla sub-object boxes clear the real-cabin sweep (its AABB per bay) and the carrier zones (C16).
  D9  sub-objects: GUIDs are verified SUBOBJ placeholders, rows = the manifest block, supports measured.
  D10 resolution floors (skeptic MC11: recomputed from the post-fixer baselines, -5 % of the detail-pass
      build): w = 1, the fixed classes, the only classes under 14 px/m are the accepted thin members;
      bay sharing active; grating relief drawn.
  Revision 2 (design/tram-stations-rev2.md section 8 with the skeptic's MC1-MC9; build/tram-stations-rev2.md):
      D1 the base rack is EXACTLY a direct cs2_bldg.ski_rack_frame("t", 11.20, 10.80, slots=8, gear=True, ...) call
      (part names and tri counts: one rack look project-wide) - the stale "6 + 9" rack count (the old leaning rack)
      is gone; no modelled post lamp / lamp head / hut lamp / entrance lantern / pendant box is built; the top has
      the three trail boards + labels, the frame-only wheel guard, the roof-mast dish and panel. D2 the top's
      emissive band re-based on the measured revision-2 export (-15 % / +25 %: the square lamp heads and the hut
      cube are gone). D3 the trail labels at 200 px/m (L2 graphics). D6 the top bound 20.70 (ruling Q3). D8 / D9
      13 top / 14 base sub-object rows. D10 floors re-based (-5 %) for the classes revision 2 moves.
  Fine-detail step 3c (build/fine-detail-step3c-tram-pali.md; tram_station.FINE_DETAIL, CLI --no-fine):
  F0  the switch: D1 on the --no-fine build = the approved table (the rack = the direct ski_rack_frame call); on the
      fine build the kit-replaced parts (2 + 1 extinguishers, 2 hose reels, the scraper + shovel, the A-frame rack)
      and lamp_annexdoor are gone; every shared part vertex-identical fine vs --no-fine (FS5 boards / labels, FS1
      mast, beacon, EXIT light, gates, medallions); F3 = no fine geometry on the tram (no maze / gate / post board).
  F1  tram_station.check_kit_rows passes on both stations; negatives raise F1: the base reel 5 cm into the drive
      house, the top extinguisher at its old spot (no 0.40 m access), the top tools 3 cm off the hut, the top reel
      onto sgpost_R0's access zone, the base rack on the grade bin, a base extinguisher in the cabin envelope, the
      rack turned 90 deg, a kit CID drift.
  F2  H1: the kit A-frame = the removed skirack_t frame vertex for vertex after the 0.02 m foot drop; extinguisher
      axes / bottoms (the top one MOVED, declared), reel plate backs, the tools = the removed pair 0.0725 m out.
  F4  the shipped manifests' fine block (15 + 4 / 13 + 3 rows, the +y frame string, F1 rows = the mod rows).
  D9 counts 15 / 13 vanilla + 4 / 3 kit rows by staged CID; D8 the kit rows clear the C4 zones + the T8 box (F1).
  Nitpick B (build/nitpick-b-tram.md; tram_station.JH_DETAILS, base only):
  NB1 the switch only adds (every JH-off base part vertex-identical, the new parts = NB_PARTS + NB_DRESS; the top the same
      with the flag on or off); NB2 placement (V legs / shoes in front of the X-braces, canopy inside the lot and the
      frozen bounds, chalkboard off the engine band, the annex parts on its +X face inside x 20.00); NB3 the shipped
      base manifest's nitpick_b block (LOD0 tris per family, the PLACEHOLDER, the STOPPED ramps), none on the top.
      D1 / D3 count the new boards / labels / frames (NB_DRESS); D3 checks LIFT TICKETS / ENTRANCE were painted with
      their LABEL_WORDS.
  Nitpick B2 (build/nitpick-b2-tram-ramps.md; tram_station.RAMPS): the base's lot 6 x 3 moved the building BASE_DX
      -4.00 toward -X (prefab = design + frame_dx): D6 the base's frozen numbers re-expressed (stubs / hosted / the LV pole
      x - 4.00, lot 6 x 3, bounds (-20.40, 0, -12) -> (23.95, 25, 12)); D8 the cabin sweep at the shifted docks; D9 / F4
      18 base vanilla rows (+ 2 park lamps and a bin on the new cell's paving); NB2 measures in the design frame (x -
      frame_dx); NB3 reads the ramps BUILT; D10 the base floors re-based -5 % of the measured RAMPS export (the ramp
      costs the capped 4096 atlas w = 1 24.81 -> 23.48 px/m), the new classes (ramp, landing, plaza) floored too.
  Revision 3 (build/tram-top-rev3.md; tram_station.PLINTH_TOP): the top in PLINTH MODE (the foundation and supports continue
      below grade to -12.00). D6: the top's bounds min y 0.00 -> -12.00 (the prefab delta; x / z / top unchanged). D10: the
      top's floors are UNCHANGED (w = 1 19.7, the wheels / gates 19.7, deckbeam 13.7: the plinth costs the 2048 atlas 2.0 %
      of w = 1, 20.78 -> 20.36 px/m, with the lattice kit sharing its UV islands), and the below-grade classes (hidden on a
      summit, seen on a cliff) are floored -5 % of the measured export (found 2.6, lattice members 9.7, pad footings 6.7);
      they are off the 14 px/m starving list by tram_station.DENSITY_BELOW.
Exit 1 on any failure.
"""
import hashlib
import json
import os
import re
import shutil
import sys
import tempfile
import time

import bpy  # noqa: F401
import numpy as np

LIB = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, LIB)
import tram_station as ts  # noqa: E402
import cs2_liftdress as LD  # noqa: E402
import cs2_validate  # noqa: E402

REPO = os.path.dirname(os.path.dirname(LIB))
SHIPPED = os.path.join(REPO, "assets", "import", "stations", "tram", "A")
FAILS = []


def check(cond, msg):
    print(f"[detail-tram] {'ok  ' if cond else 'FAIL'} {msg}")
    if not cond:
        FAILS.append(msg)
    return cond


def manifest(role):
    name = ts.NAMES[role]
    with open(os.path.join(SHIPPED, name, f"{name}.manifest.json"), encoding="utf-8") as fh:
        return json.load(fh)


def png(role, suffix):
    name = ts.NAMES[role]
    return cs2_validate.load_png_pixels(os.path.join(SHIPPED, name, f"{name}_{suffix}.png"))


# --------------------------------------------------------------------------------------
DRESS = {
    "base": {"board_dep", "label_dep", "board_tickets", "label_tickets", "board_ops", "label_ops",
             "lamp_exit", "ext_annex", "ext_annex_brk", "ext_annex_head", "ext_hall", "ext_hall_brk",
             "ext_hall_head", "hosereel_drive_plate", "hosereel_drive_drum", "hosereel_drive_fl0",
             "hosereel_drive_fl1", "stripe_nosL", "stripe_nosC", "stripe_nosR"}
            | {f"tickframe_f{n}_{s}" for n in (0, 1) for s in "lrh"}
            | {f"chev_{t}_{s}" for t in ("cL", "cR", "oL", "oR") for s in ("base", "k")},
    "top": {"lamp_beacon", "hook_R_chain", "hook_R_block", "hook_R_hook", "board_stn", "label_stn",
            "gauge_tension_case", "gauge_tension_ptr", "label_tension", "ext_hut", "ext_hut_brk",
            "ext_hut_head", "hosereel_hut_plate", "hosereel_hut_drum", "hosereel_hut_fl0",
            "hosereel_hut_fl1", "tool_hut_scraper", "tool_hut_shovel"}
           | {f"stripe_{t}" for t in ("bcL", "bcR", "bL", "bR")}
           # revision 2 (D4): the three trail boards on the rear cap rail
           | {f"{p}_trail_{i}" for p in ("board", "label") for i in range(3)},
}
DRESS_FAMILIES = ("label_", "board_", "ext_", "hosereel_", "tool_", "skirack_", "ski_", "chev_", "stripe_",
                  "hook_", "gauge_", "tickframe_")
# fine-detail step 3c: the dressing parts that leave the mesh as dressing-kit rows (tram_station.fd_kit_specs; the
# base's A-frame rack goes too) and the lamp that becomes a vanilla row
FD_GONE = {"base": {"ext_annex", "ext_annex_brk", "ext_annex_head", "ext_hall", "ext_hall_brk", "ext_hall_head",
                    "hosereel_drive_plate", "hosereel_drive_drum", "hosereel_drive_fl0", "hosereel_drive_fl1"},
           "top": {"ext_hut", "ext_hut_brk", "ext_hut_head", "hosereel_hut_plate", "hosereel_hut_drum",
                   "hosereel_hut_fl0", "hosereel_hut_fl1", "tool_hut_scraper", "tool_hut_shovel"}}
FD_ROWS = {"base": (15 + (3 if ts.RAMPS else 0), 4), "top": (13, 3)}   # (vanilla, kit) rows (nitpick B2: + 3 base)
# nitpick B (tram_station.JH_DETAILS; build/nitpick-b-tram.md): the base's new dressing (DETAIL1: the two boards + labels,
# the annex ticket-window frames) and its new structural parts (built with DETAIL1 off too)
NB_DRESS = ({"board_lifttickets", "label_lifttickets", "board_entrance", "label_entrance"}
            | {f"tickframe_a{n}_{s}" for n in (0, 1, 2) for s in "lrh"})
NB_PARTS = ({"jhcanopy", "jhroof", "chalkboard_a", "chalkboard_b", "chalkboard_aface", "chalkboard_bface"}
            | {f"jhshoe_{i}" for i in range(4)} | {f"jhstrut_{i}{s}" for i in range(4) for s in "ab"}
            | {f"jhtie_{i}" for i in range(5)} | {f"sill_a{n}" for n in range(3)})


def dress(role):
    """DRESS plus the nitpick-B dressing on the base while JH_DETAILS is on."""
    return DRESS[role] | (NB_DRESS if (role == "base" and ts.JH_DETAILS) else set())


def _ntris(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


def d1_counts():
    """D1 on the approved build (FINE_DETAIL off = --no-fine, F0's legacy half) and on the fine-detail build."""
    saved = ts.FINE_DETAIL
    try:
        ts.FINE_DETAIL = False
        _d1_counts(fine=False)
        ts.FINE_DETAIL = True
        _d1_counts(fine=True)
    finally:
        ts.FINE_DETAIL = saved


def _d1_counts(fine):
    # revision 2 (B1, skeptic MC5): the A-frame KIT rack, measured from a direct cs2_bldg call in a clean scene
    import cs2_bldg as KB
    tag_ = "fine" if fine else "legacy"
    ts.clear_scene()
    kit = {o.name: _ntris(o) for o, _m in KB.ski_rack_frame("t", ts.TRAM_RACK_AT[2], ts.TRAM_RACK_AT[0], slots=8,
                                                             gear=True, skis=ts.TRAM_RACK_GEAR,
                                                             z0=ts.TRAM_RACK_AT[1], along="Y", mat="galv",
                                                             ski_mat="accent")}
    # skeptic MC5: aframe_rack hands back liftdress-normalised parts (its own UVMap, no cross-net layers)
    ts.clear_scene()
    ar = LD.aframe_rack("t", ts.TRAM_RACK_AT[2], ts.TRAM_RACK_AT[0], ts.TRAM_RACK_GEAR, z0=ts.TRAM_RACK_AT[1], along="Y")
    norm = [(o.name, "UVMap" in o.data.uv_layers,
             sorted(a.name for a in o.data.attributes if a.name in ("netu", "netv", "netf"))) for o, _m in ar]
    check({n for n, _u, _a in norm} == set(kit) and all(u and not a for _n, u, a in norm),
          f"D1 base: cs2_liftdress.aframe_rack parts are the kit call's, normalised (UVMap, no net layers) {norm}")
    for role in ("base", "top"):
        args = ts.parse_args(["--role", role])
        ts.clear_scene()
        k, _m = ts.build_parts(role, args)
        names = {o.name for o, _q in k.parts}
        built = {o.name: _ntris(o) for o, _q in k.parts}
        saved = ts.DETAIL1
        ts.DETAIL1 = False
        try:
            ts.clear_scene()
            k0, _m0 = ts.build_parts(role, args)
            names0 = {o.name for o, _q in k0.parts}
        finally:
            ts.DETAIL1 = saved
        new = names - names0
        rack = {n for n in new if n.startswith(("skirack_", "ski_"))}
        want = dress(role) - (FD_GONE[role] if fine else set())
        rack_ok = (rack == set(kit) and all(built[n] == kit[n] for n in kit)) if (role == "base" and not fine) \
            else not rack
        check(want <= new and (new - rack) == want and rack_ok,
              f"D1 {tag_} {role}: dressing parts {len(new)} = {len(want)} named + ski rack {len(rack)} "
              + (f"== the direct cs2_bldg.ski_rack_frame call {sorted(kit.items())} " if role == "base" and not fine
                 else "(fine: the rack is a kit row) " if role == "base" else "")
              + f"(missing {sorted(want - new)[:6]}, unexpected {sorted(new - rack - want)[:6]}, rack "
              f"{sorted((n, built[n]) for n in rack)})")
        # fine-detail step 3c: the annex door's lamp bar is a vanilla row, every other 1c part still built
        gone_1c = {"lamp_annexdoor"} if (fine and role == "base") else set()
        check(names0 - gone_1c <= names and not (gone_1c & names),
              f"D1 {tag_} {role}: the 1c parts all still built" + (" but lamp_annexdoor (a WallLightShortRange row)"
                                                                   if gone_1c else ""))
        if fine:
            check(not (FD_GONE[role] & names), f"D1 fine {role}: no part the kit rows replace is built "
                                               f"{sorted(FD_GONE[role] & names)}")
        # LOD1_DELETE dressing families name dressing parts only (no station part shares the prefix)
        clash = sorted(n for n in names0 if n.startswith(DRESS_FAMILIES))
        check(not clash, f"D1 {role}: dressing LOD families collide with station parts {clash[:6]}")
        # revision 2: no modelled lamp a vanilla row replaced (L1 / L3 / B2); the rev-2 top parts are built
        gone = sorted(n for n in names if n.startswith(("lamp_post_", "lamp_head_", "lamp_hut", "lamp_ent_"))
                      or re.fullmatch(r"lamp_\d+", n))
        new2 = {"top": {"cwguard_postL", "cwguard_postR", "cwguard_top", "cwguard_mid", "antdish", "antpanel",
                        "mast", "fsrim_L", "fsrim_R", "fsbeam"},
                "base": {"bwdrim_L", "bwdrim_R", "bwdarm_L", "bwdarm_R"} | {f"lamp_plate_{i}" for i in range(6)}}[role]
        check(not gone and new2 <= names, f"D1 {role}: no modelled post / head / hut / lantern / pendant lamp "
                                         f"{gone}; revision-2 parts built (missing {sorted(new2 - names)})")
    kit = {tok for why, toks in ts.CONTACTS.items() if why.startswith("dressing kits") for tok in toks.split()}
    dress_kit = {tok for toks in LD.DRESS_CONTACTS.values() for tok in toks.split()}
    check(kit and kit <= dress_kit, f"D1 tram kit contacts {sorted(kit)} are cs2_liftdress.DRESS_CONTACTS pairs")


def d2_emissive():
    # revision 2 (design 7 "Emissive"): the top band re-based on the measured revision-2 export (930 lit texels:
    # the four 0.30 m lamp heads and the hut cube, modelled emissive, are vanilla rows now) -15 % / +25 %; the
    # base (205,875 after its six pendant and two lantern boxes went) stays inside its detail-pass band
    # nitpick B (JH_DETAILS): the base's canopy, boards and frames take atlas (w = 1 25.82 -> 24.81 px/m), so the same
    # lit faces cover fewer texels (201,041 -> 188,468 measured): the base floor re-based -15 % of the measured (the
    # revision-2 rule for the top band)
    gates = {"base": (160_000 if ts.JH_DETAILS else 200_000, 262_000), "top": (790, 1_163)}
    for role in ("base", "top"):
        man = manifest(role)
        d1 = man.get("detail1") or {}
        lit = d1.get("emissive_lit_texels")
        check(lit is not None and gates[role][0] <= lit <= gates[role][1],
              f"D2 {role}: _Emissive lit texels {lit} inside {gates[role]}")
        em = png(role, "Emissive")
        bc = png(role, "BaseColor")
        rects = dict(man["bake"]["stamps_px"])
        rects.update({nm: d.get("rect") for nm, d in d1.get("labels", {}).items()})
        for nm, r in sorted(rects.items()):
            if not r or not nm.startswith(ts.EMISSIVE_MASKED):
                continue
            e = em[r[0]:r[1], r[2]:r[3], 0:3].max(axis=-1)
            check(e.max() > 0.5 and e.min() < 0.15, f"D2 {role}: {nm} lit marks with dark gaps "
                                                    f"(max {e.max():.2f}, min {e.min():.2f})")
        if role == "top":
            # the beacon lens: emissive texels of the lamp_red material are red (design 6.5: R >= 4 G,
            # in linear terms - the PNG is sRGB-encoded, (0.95, 0.12, 0.08) linear reads (0.98, 0.38, 0.31))
            lin = np.where(em[..., 0:3] > 0.04045, ((em[..., 0:3] + 0.055) / 1.055) ** 2.4, em[..., 0:3] / 12.92)
            red = (lin[..., 0] > 0.5) & (lin[..., 0] >= 4.0 * lin[..., 1]) & (lin[..., 0] >= 4.0 * lin[..., 2])
            check(red.sum() > 0, f"D2 top: red beacon texels in _Emissive ({int(red.sum())}, linear R >= 4 G, 4 B)")
            del bc


def d3_labels():
    for role in ("base", "top"):
        man = manifest(role)
        labs = (man.get("detail1") or {}).get("labels", {})
        want = {n for n in ts.LABEL_KINDS if n in dress(role)}
        check(set(labs) == want, f"D3 {role}: labels {sorted(labs)}")
        ctl = png(role, "ControlMask")
        for nm, d in sorted(labs.items()):
            kind, size, target = ts.LABEL_KINDS[nm]
            W, H = size or LD.BOARDS[kind]["size"]
            big = max(W, H) > 1.50
            ok = d["px_per_m"] >= target and min(d["u_px_per_m"], d["v_px_per_m"]) >= 0.95 * target
            # revision 2: the trail boards are L2 graphics (symbol + arrow, no text: 200 px/m, design D4)
            check(ok and (big or nm.startswith(ts.LABEL_L2) or d["px_per_m"] >= 400.0), f"D3 {role}: {nm} {d['px_per_m']} px/m (u "
                  f"{d['u_px_per_m']}, v {d['v_px_per_m']}; target {target}, {'L2' if big or nm.startswith(ts.LABEL_L2) else 'L1'}), "
                  f"weight {d.get('weight')}")
            check(d.get("t2_ok") in (True, None) and (kind not in ("stn", "dep") or d.get("t1_ok")),
                  f"D3 {role}: {nm} T1 {d.get('t1_ok')} / T2 {d.get('t2_ok')} (cap {d.get('cap_m')}, stroke "
                  f"{d.get('stroke_px')} texels)")
            r = d.get("rect")
            check(r is not None and float(ctl[r[0]:r[1], r[2]:r[3], 0:3].max()) < 1e-3,
                  f"D3 {role}: ControlMask 0 under {nm} {r}")
        c13 = man["contracts"]["C13_unique"]
        check(want <= set(c13) and all(v <= 0.02 for v in c13.values()),
              f"D3 {role}: C13 upright on the shipped layout {sorted(c13)}")
        # nitpick B: LIFT TICKETS / ENTRANCE are the TICKETS kind painted with their own words (LABEL_WORDS)
        for nm, words in sorted(ts.LABEL_WORDS.items()):
            if nm in want:
                check(labs.get(nm, {}).get("lines") == list(words) and ts.LABEL_KINDS[nm][0] == "tickets",
                      f"D3 {role}: {nm} painted {labs.get(nm, {}).get('lines')} (want {list(words)}, kind tickets)")


def _texture_hashes(folder, name):
    out = {}
    for s in ("BaseColor", "Normal", "MaskMap", "ControlMask", "Emissive"):
        with open(os.path.join(folder, name, f"{name}_{s}.png"), "rb") as fh:
            out[s] = hashlib.sha1(fh.read()).hexdigest()
    return out


def d4_d5_weather(work):
    for role in ("base", "top"):
        man = manifest(role)
        w = (man.get("detail1") or {}).get("weather") or {}
        ladder = w.get("ladder") or []
        chosen = w.get("intensity")
        steps = [r["intensity"] for r in ladder]
        ok = (chosen is not None and w.get("slot_wear") == "free" and w.get("snow") == 0.0
              and w.get("preset") == "station" and (w.get("vanilla") or {}).get("ok") is True
              and all(r["ok"] for r in ladder if r["intensity"] <= chosen)
              and (chosen == max(LD.INTENSITY_STEPS) or (ladder and not ladder[-1]["ok"]
                                                         and ladder[-1]["intensity"] > chosen)))
        check(ok, f"D4 {role}: shipped weathering at {chosen} (ladder {[(r['intensity'], r['ok']) for r in ladder]}"
                  f", VL1 {(w.get('vanilla') or {}).get('VL1_mean_rel_dL')}), slot wear {w.get('slot_wear')}, "
                  f"snow {w.get('snow')}")
        del steps
    for role in ("top", "base"):
        name = ts.NAMES[role]
        dirs, reps = {}, {}
        for tag, extra in (("on", []), ("off", ["--weather", "off"]), ("on2", [])):
            od = os.path.join(work, f"w_{role}_{tag}")
            reps[tag] = ts.main(["--role", role, "--finish", "--lods", "--export", "--out", od,
                                 "--atlas", "512", "--ao-samples", "0", "--no-validate"] + extra)
            dirs[tag] = od
        h = {tag: _texture_hashes(d, name) for tag, d in dirs.items()}
        check(h["on"] == h["on2"], f"D5 {role}: two weathered finish builds -> identical textures")
        check(h["on"]["ControlMask"] == h["off"]["ControlMask"],
              f"D4 {role}: ControlMask byte-identical with and without the layer (G5)")
        load = {tag: {s: cs2_validate.load_png_pixels(os.path.join(d, name, f"{name}_{s}.png"))
                      for s in ("BaseColor", "MaskMap")} for tag, d in dirs.items() if tag != "on2"}
        check(np.array_equal(load["on"]["MaskMap"][..., 1:3], load["off"]["MaskMap"][..., 1:3]),
              f"D4 {role}: MaskMap G / B untouched (G2)")
        rects = dict(reps["on"]["stamps"])
        rects.update({nm: d.get("rect") for nm, d in reps["on"]["labels"].items()})
        same = all(np.array_equal(load["on"]["BaseColor"][r[0]:r[1], r[2]:r[3]],
                                  load["off"]["BaseColor"][r[0]:r[1], r[2]:r[3]])
                   for r in rects.values() if r)
        check(same and rects, f"D4 {role}: {len(rects)} stamped label / dial rects identical in BaseColor (protected)")
        diff = float(np.abs(load["on"]["BaseColor"][..., :3] - load["off"]["BaseColor"][..., :3]).max())
        check(diff > 0.01, f"D4 {role}: the layer changed BaseColor elsewhere (max |d| {diff:.3f})")


FROZEN = {
    # nitpick B2 (RAMPS): the 6 x 3 lot moved the building 4.00 m toward -X (min x -16.40 - 4.00) and the new cell's
    # paving reaches x 23.95 (design 27.95)
    "base": ({"bounds_y": (0.0, 25.0), "x": (-20.40, 23.95), "z": (-12.00, 12.00), "lot": [6, 3]} if ts.RAMPS else
             {"bounds_y": (0.0, 25.0), "x": (-16.40, 20.00), "z": (-12.00, 12.00), "lot": [5, 3]}),
    # revision 2 (ruling Q3): the top bound shrinks 27.00 -> MAST_TOP 20.70 (the 8 m mast dropped); x / z unchanged
    # revision 3 (PLINTH_TOP): the plinth reaches -12.00 (bounds min y, the prefab delta); x / z / top unchanged
    "top": {"bounds_y": (-ts.PLINTH["depth"] if ts.PLINTH_TOP else 0.0, ts.MAST_TOP), "x": (-11.80, 11.80),
            "z": (-12.00, 11.60), "lot": [3, 3]},
}


def d6_frozen():
    for role in ("base", "top"):
        pc = manifest(role)["prefab_contract"]
        f = FROZEN[role]
        b = pc["bounds"]
        ok = (abs(pc["deck_y"] - 6.30) < 1e-9 and abs(pc["pivot_y"] - 6.00) < 1e-9
              and abs(pc["track_y"] - 16.00) < 1e-9 and abs(pc["haul_y"] - 15.60) < 1e-9
              and abs(pc["carriage_top_y"] - 16.50) < 1e-9 and pc["lot"] == f["lot"]
              and abs(b["min"][1] - f["bounds_y"][0]) <= 0.001 and abs(b["max"][1] - f["bounds_y"][1]) <= 0.001
              and abs(b["min"][0] - f["x"][0]) <= 0.001 and abs(b["max"][0] - f["x"][1]) <= 0.001
              and abs(b["min"][2] - f["z"][0]) <= 0.001 and abs(b["max"][2] - f["z"][1]) <= 0.001)
        check(ok, f"D6 {role}: levels 6.30 / 6.00 / 16.00 / 15.60 / 16.50, lot {pc['lot']}, bounds {b}")
        dx = ts.frame_dx(role)              # nitpick B2: the RAMPS base's numbers re-expressed (x - 4.00)
        stubs = sorted((round(s["a"][0], 3), round(s["a"][1], 3), round(s["a"][2], 3), s["lane"]) for s in pc["stubs"])
        check(stubs == sorted([(round(-5.5 + dx, 3), 16.0, -12.0, "SkiTramTrackRope"),
                               (round(5.5 + dx, 3), 16.0, -12.0, "SkiTramTrackRope"),
                               (round(-5.0 + dx, 3), 15.6, -12.0, "SkiTramHaulRope"),
                               (round(5.0 + dx, 3), 15.6, -12.0, "SkiTramHaulRope")])
              and abs(pc.get("axis_x", 0.0) - dx) < 1e-9,
              f"D6 {role}: stubs {stubs}, line axis x {pc.get('axis_x', 0.0)}")
        hosted = [(h["what"], h["m_Position"], h["m_ParentMesh"]) for h in pc["hosted"]]
        want = ([("SkiTramCabin A", [-5.5 + dx, 6.0, -5.5], 0), ("SkiTramCabin B", [5.5 + dx, 6.0, -5.5], 0),
                 ("LV feed pole", [16.0 + dx, 0.0, -12.0], -1)] if role == "base" else [])
        check(hosted == want, f"D6 {role}: hosted {hosted}")
        man = manifest(role)
        check(man["win_cells"] == {"base": [6, 8, 17, 18], "top": [7]}[role]
              and man["atlas_px"] == {"base": 4096, "top": 2048}[role],
              f"D6 {role}: _Win {man['win_cells']}, atlas {man['atlas_px']}")
        tris = {int(k_): v for k_, v in man["tris"].items()}
        check(tris[0] <= ts.LOD0_CAP[role] and tris[1] <= 0.5 * tris[0] and tris[2] <= ts.LOD2_CLI_CAP
              and man["batches"] <= ts.BATCH_CAP,
              f"D6 {role}: tris {tris} (cap {ts.LOD0_CAP[role]}, LOD1 <= 50 %, LOD2 <= {ts.LOD2_CLI_CAP}), "
              f"batches {man['batches']}")


def d7_d8_d9_access_subobjects():
    zmax_cabin = 4.03 + 0.05                        # the real SkiTramCabinA: x +-1.88, z +-4.03 (T8 measured)
    for argv in (["--role", "base"], ["--role", "base", "--no-clock-tower"], ["--role", "top"],
                 ["--role", "top", "--no-wheels"]):
        role = argv[1]
        try:
            _t, _w, _tb, rep = ts.build_checked(argv)
        except ts.ContractError as e:
            check(False, f"D7 {' '.join(argv)}: contract stage raised {e}")
            continue
        c15 = rep["C15"]
        bad = {k_: j for k_, j in c15["joins"].items() if j.get("opening") is not None and not j["ok"]}
        check(not c15["orphans"] and not c15["undeclared"],
              f"D7 {' '.join(argv)}: no orphan, no undeclared standable surface; narrow (redundant) joins {bad}")
        if role == "top":
            m = c15["joins"]["grate_gangL | plinthcap"]
            check(m["ok"] and m["opening"] >= 0.61, f"D7 {' '.join(argv)}: gangway mouth {m}")
        rows = rep["C16"]["rows"]
        hits = []
        for r in rows:
            lo, hi = np.array(r["box"][0]), np.array(r["box"][1])
            for s in (-1, 1):
                xl = s * ts.TRACK_X + ts.frame_dx(role)          # nitpick B2: the RAMPS base's docks at x - 4.00
                clo = np.array([xl - 1.88 - 0.05, ts.PIVOT_Y - 0.1, -1e9])
                chi = np.array([xl + 1.88 + 0.05, ts.CARRIAGE_TOP_Y + 0.2, ts.DOCK_Z + zmax_cabin])
                if np.all(lo < chi) and np.all(hi > clo):
                    hits.append((r["prefab"], r["pos"], s))
        check(not hits, f"D8 {' '.join(argv)}: {len(rows)} sub-object boxes clear the cabin sweep AABB "
                        f"(both bays) {hits}")
        objs = rep["C16"]["block"]["objects"]
        guids = {r_["guid"] for r_ in objs if r_.get("kind") != "mod"}
        okg = guids <= {"UnityGUID:" + v["guid"] for v in LD.SUBOBJ.values()}
        sup = [(r["prefab"], r["support_m"]) for r in rows]
        # fine-detail step 3c: + the annex door's wall light (base), then the kit "mod" rows by staged CID
        import fine_detail as FD
        mods = [o for o in objs if o.get("kind") == "mod"]
        nv, nk = FD_ROWS[role]
        okm = (len(mods) == nk and rep["C16"]["block"].get("kinds") == {"vanilla": nv, "mod": nk}
               and all(o["guid"] == "CID:" + FD.kit_cid(o["prefab"], o.get("colour_set"))
                       == "CID:" + str(FD.staged_kit_cid(o["prefab"], o.get("colour_set"))) for o in mods)
               and objs[:nv] == [o for o in objs if o.get("kind") != "mod"]
               and rep["C16"]["block"].get("x_convention") == LD.STATION_X_CONVENTION)
        check(okg and all(s_ is not None and s_ <= (0.05 if r["mount"] == "wall" else 0.03) for (_p, s_), r in zip(sup, rows))
              and len(rows) == nv and okm,
              f"D9 {' '.join(argv)}: {len(rows)} vanilla rows (revision 2: 14 base / 13 top; fine: + the annex wall "
              f"light) + {len(mods)} kit rows {sorted(o['prefab'] for o in mods)} by staged CID, GUIDs verified "
              f"placeholders, frame {rep['C16']['block'].get('x_convention', '')[:40]}...; supports {sup}")
        f1, nums = rep["F1"]
        check(bool(f1) and len((nums or {}).get("rows") or []) == nk
              and all(r_.get("envelope_gap_m", 0.0) > 0.0 for r_ in nums["rows"]),
              f"D8 {' '.join(argv)}: F1 {len((nums or {}).get('rows') or [])} kit rows clear the C4 zones + the T8 "
              f"cabin-sweep box (nearest {min((r_.get('envelope_gap_m') for r_ in (nums or {}).get('rows') or []), default=None)} m)")


FLOORS = {   # skeptic MC11: -5 % of the detail-pass build's measured values (report section 5)
    # revision 2 (design 8 D10: re-based -5 % from the measured revision-2 export for the classes it moves):
    # base w1 25.73, bwrim / bwdrim 25.7 (the 4.0 m drive wheel and its deflection sheaves, weight 1.0); top w1
    # 20.76, gate 20.8 (2.2 m leaves, perforation relief: no bay sharing), the wheels 20.8 (bull / lane / foot)
    # nitpick B2 (RAMPS; build/nitpick-b2-tram-ramps.md): the ramp, its landings, rails and the paving cost the capped
    # 4096 atlas 5.4 % of w = 1 (24.81 -> 23.48 px/m): the base floors re-based -5 % of the measured RAMPS export (the
    # revision-2 rule), the ramp's own classes floored too; every visible class still >= 14 (the starving check)
    "base": ({"w1": 22.3, "classes": {"deck": 17.9, "purlin": 15.6, "trackrail": 13.4, "outrigger": 17.9,
                                      "girderroof": 13.4, "glazepanel": 13.4, "bwrim": 22.3, "bwdrim": 22.3,
                                      "ramp": 13.9, "landing": 17.4, "plaza": 13.5}} if ts.RAMPS else
             {"w1": 24.4, "classes": {"deck": 19.7, "purlin": 17.2, "trackrail": 14.8, "outrigger": 19.7,
                                      "girderroof": 14.8, "glazepanel": 14.8, "bwrim": 24.4, "bwdrim": 24.4}}),
    "top": {"w1": 19.7, "classes": {"deckbeam": 13.7, "bullrim": 19.7, "bullspoke": 19.7, "dwrim": 19.7,
                                    "dwspoke": 19.7, "gate": 19.7, "fsrim": 19.7,
                                    # revision 3 (PLINTH_TOP): the below-grade classes, -5 % of the measured export
                                    # (found 2.8, latcol / latgirt / latbrace 10.2, latpad 7.1 px/m)
                                    **({"found": 2.6, "latcol": 9.7, "latgirt": 9.7, "latbrace": 9.7, "latpad": 6.7}
                                       if ts.PLINTH_TOP else {})}},
}
THIN_ACCEPTED = {"fin", "mullion", "rail", "hornstrut"}


def d10_resolution():
    for role in ("base", "top"):
        man = manifest(role)
        d1 = man.get("detail1") or {}
        den = d1.get("density") or {}
        cls = den.get("classes", {})
        f = FLOORS[role]
        low = {k_: cls.get(k_, {}).get("px_per_m") for k_, v in f["classes"].items()
               if cls.get(k_, {}).get("px_per_m", 0.0) < v}
        check(den.get("w1_px_per_m", 0.0) >= f["w1"] and not low,
              f"D10 {role}: w1 {den.get('w1_px_per_m')} >= {f['w1']}; class floors {f['classes']} "
              f"(under: {low})")
        starving = set(den.get("starving_lt14", []))
        check(starving <= THIN_ACCEPTED, f"D10 {role}: classes under 14 px/m {sorted(starving)} are the "
                                         f"accepted thin members {sorted(THIN_ACCEPTED)}")
        sh = d1.get("share") or {}
        check((d1.get("shared_faces") or 0) > 0 and sh.get("groups", 0) > 0,
              f"D10 {role}: bay sharing {sh.get('groups')} groups, {d1.get('shared_faces')} dup faces")
        drawn = ((d1.get("detail_stats") or {}).get("drawn") or {})
        check(drawn.get("grate", 0) > 0, f"D10 {role}: grating relief drawn on {drawn.get('grate')} faces "
                                         f"(pitch {ts.GRATE_PITCH[role]} m)")


# --------------------------------------------------------------------------------------
# Fine-detail step 3c (build/fine-detail-step3c-tram-pali.md): F0 the switch, F1 the kit rows' contract (+ negatives),
# F2 H1 placement against the removed parts, F3 fine geometry, F4 the shipped fine block
# --------------------------------------------------------------------------------------
KIT_H1_TOL = 0.001                      # F2: kit vs removed part, metres


def _blockout(role, fine):
    """(t, table, tri (prefab frame), names) of a blockout with FINE_DETAIL = fine."""
    saved = ts.FINE_DETAIL
    ts.FINE_DETAIL = fine
    try:
        args = ts.parse_args(["--role", role] + ([] if fine else ["--no-fine"]))
        ts.clear_scene()
        t, table, _s, _m = ts.assemble(role, args, False)
        t.name = t.data.name = ts.NAMES[role]
        tri, names = ts.triangles(t)
    finally:
        ts.FINE_DETAIL = saved
    return t, table, tri, names


def _patched_specs(patch):
    saved = ts.fd_kit_specs
    ts.fd_kit_specs = lambda role, _s=saved: patch(role, [dict(x) for x in _s(role)])
    return saved


def _edit(prop, **kw):
    def patch(role, specs):
        out = []
        for s in specs:
            if s["prop"] == prop or (prop == "rack" and s["prop"].startswith("SkiDressSkiRack")):
                s = dict(s)
                for k_, v in kw.items():
                    if k_ == "dpos":
                        s["pos"] = tuple(a + b for a, b in zip(s["pos"], v))
                    else:
                        s[k_] = v
            out.append(s)
        return out
    return patch


def f1_kit_contracts():
    """F1 positives on both stations (blockout) + negatives, each must raise F1."""
    import dataclasses
    import fine_detail as FD
    cases = (
        ("base", _edit("SkiDressHoseReel", dpos=(0.0, 0.0, 0.05)), "the base kit reel pushed 5 cm into the drive house"),
        ("top", _edit("SkiDressExtinguisher", pos=(4.70, ts.DECK_Y + 0.70, -0.40), facing="-Z"),
         "the top kit extinguisher at the OLD spot (hut -Z face: the cap railing 0.23 m in front)"),
        ("top", _edit("SkiDressTools", dpos=(0.0, 0.0, 0.03)), "the top tools 3 cm off the hut wall (touch nothing)"),
        ("top", _edit("SkiDressHoseReel", pos=(4.90, ts.DECK_Y + 0.75, 0.20)),
         "the top reel slid along the hut's +X face onto sgpost_R0's access zone"),
        ("base", _edit("rack", pos=(2.50, 0.0, 11.20)), "the base rack on the grade trash bin's box"),
        ("base", _edit("SkiDressExtinguisher", pos=(5.50, ts.DECK_Y + 0.90, -5.50), facing="+X"),
         "a base extinguisher in the docked cabin's envelope (C4 / T8)"),
        ("base", _edit("rack", facing="+X"), "the base rack turned 90 deg (identity is right: its long axis is prefab X)"),
    )
    for role in ("base", "top"):
        t, table, _tri, _n = _blockout(role, True)       # one station at a time: clear_scene removes the last
        try:
            r, nums = ts.check_kit_rows(t, role, table)
            check("F1" in r and len(nums["rows"]) == FD_ROWS[role][1],
                  f"F1 {role}: passes, {len(nums['rows'])} kit rows ({r['F1'][:100]}...)")
        except ts.ContractError as e:
            check(False, f"F1 {role}: raised on the shipping layout: {e}")
        for crole, patch, why in cases:
            if crole != role:
                continue
            saved = _patched_specs(patch)
            try:
                ts.check_kit_rows(t, role, table)
                check(False, f"F1 negative did NOT raise: {why}")
            except ts.ContractError as e:
                check(str(e).startswith("F1"), f"F1 negative raises: {why} ({str(e)[:160]})")
            finally:
                ts.fd_kit_specs = saved
    k = FD.KIT["SkiDressHoseReel"]
    FD.KIT["SkiDressHoseReel"] = dataclasses.replace(k, cid="0" * 32)
    try:
        ts.check_kit_rows(t, "top", table)
        check(False, "F1 negative did NOT raise: a kit CID drift (KIT vs the staged .cid)")
    except (ts.ContractError, ValueError) as e:
        check("F1" in str(e) or "CID" in str(e), f"F1 negative raises: a kit CID drift ({str(e)[:110]})")
    finally:
        FD.KIT["SkiDressHoseReel"] = k


def _nearest_dev(old, new):
    d = 0.0
    for v in old:
        d = max(d, float(np.min(np.linalg.norm(new - v, axis=1))))
    return d


def f2_h1():
    """F2: every kit row (the REAL kit mesh placed by its row, host frame (Z, X, Y) = tram_point) against the
    parts it replaces on a --no-fine blockout of the same station."""
    import cs2_kitrows as KR
    out = {}
    for role in ("base", "top"):
        _t, _table, tri, names = _blockout(role, False)
        nm = np.array(names)
        saved = ts.FINE_DETAIL
        ts.FINE_DETAIL = True
        try:
            specs, rows = ts.fd_kit_specs(role), ts.fd_kit_rows(role)
        finally:
            ts.FINE_DETAIL = saved
        for s, r in zip(specs, rows):
            A = KR.host_mesh(r, ts._fd_host)[0]
            fx, fz = ts._FD_DIR[s["facing"]]
            nv = np.array([fz, fx, 0.0])
            sel = np.zeros(len(nm), dtype=bool)
            for pre in s["removed"]:
                sel |= np.char.startswith(nm, pre)
            old_all = ts._fd_host(tri[sel].reshape(-1, 3))
            rtag = f"{role} {s['prop']}"
            if s["prop"].startswith("SkiDressSkiRack"):
                old = ts._fd_host(tri[nm == "skirack_t"].reshape(-1, 3))          # the frame bars
                dz = float(A[:, 2].min() - old[:, 2].min())
                dev = _nearest_dev(np.unique(old.round(6), axis=0) + np.array([0.0, 0.0, dz]), A)
                out[rtag] = {"frame_dev_mm": round(dev * 1000, 3), "foot_dz_m": round(dz, 4)}
                check(dev <= KIT_H1_TOL, f"{rtag} F2 the kit A-frame = the removed skirack_t frame vertex for vertex: "
                                         f"max {dev * 1000:.3f} mm after a foot drop of {dz:+.4f} m")
            elif s["prop"] == "SkiDressExtinguisher":
                old = ts._fd_host(tri[nm == s["removed"][0]].reshape(-1, 3))
                ob = old[np.abs(old[:, 2] - old[:, 2].min()) < 1e-4]
                kb = A[np.abs(A[:, 2] - A[:, 2].min()) < 1e-4]
                da = float(np.linalg.norm(kb[:, :2].mean(axis=0) - ob[:, :2].mean(axis=0)))
                df = float(A[:, 2].min() - old[:, 2].min())
                out[rtag + " " + s["removed"][0]] = {"axis_dev_mm": round(da * 1000, 3), "bottom_dz_m": round(df, 4)}
                if "MOVED" in s["was"]:
                    check(da > 0.5, f"{rtag} F2 MOVED (declared): {da:.3f} m from the removed body ({s['was'][:80]})")
                else:
                    check(da <= KIT_H1_TOL and abs(df) <= KIT_H1_TOL,
                          f"{rtag} F2 the kit body axis {da * 1000:.3f} mm off the removed body's, bottom {df:+.4f} m")
            elif s["prop"] == "SkiDressHoseReel":
                old = ts._fd_host(tri[nm == s["removed"][0] + "plate"].reshape(-1, 3))
                ob = old[np.abs(old @ nv - (old @ nv).min()) < 1e-4]
                kb = A[np.abs(A @ nv - (A @ nv).min()) < 1e-4]
                dev = _nearest_dev(np.array([ob.min(axis=0), ob.max(axis=0)]), np.array([kb.min(axis=0), kb.max(axis=0)]))
                out[rtag] = {"plate_corner_dev_mm": round(dev * 1000, 3),
                             "plate_m": [round(float(ob[:, 2].max() - ob[:, 2].min()), 3),
                                         round(float(kb[:, 2].max() - kb[:, 2].min()), 3)]}
                check(dev <= KIT_H1_TOL, f"{rtag} F2 the kit plate's back = the removed plate's: max {dev * 1000:.3f} mm "
                                         f"(plate {out[rtag]['plate_m']} m)")
            elif s["prop"] == "SkiDressTools":
                of = old_all[old_all[:, 2] <= old_all[:, 2].min() + 0.002]
                kf = A[A[:, 2] <= A[:, 2].min() + 0.002]
                shift = np.array([*(kf[:, :2].mean(axis=0) - of[:, :2].mean(axis=0)), 0.0])
                dz = float(A[:, 2].min() - old_all[:, 2].min())
                dev = _nearest_dev(np.unique(old_all.round(6), axis=0) + shift + np.array([0.0, 0.0, dz]), A)
                lat = float(np.linalg.norm(shift - nv * (shift @ nv)))
                out[rtag] = {"foot_line_move_m": round(float(shift @ nv), 4), "lateral_m": round(lat, 4),
                             "dz_m": round(dz, 4), "vertex_dev_mm": round(dev * 1000, 3)}
                check(dev <= KIT_H1_TOL and abs(float(shift @ nv) - (0.3925 - 0.32)) < 1e-3 and lat < 1e-3,
                      f"{rtag} F2 the kit tools = the removed pair moved {float(shift @ nv):.4f} m out from the wall "
                      f"(the foot-line rule 0.3925 - 0.32), {dz:+.4f} m in z, max {dev * 1000:.3f} mm")
    print("[detail-tram] F2 H1 numbers: " + json.dumps(out))
    return out


def _rsort(tris):
    X = np.round(np.array(tris, dtype=np.float64).reshape(-1, 3), 6)
    return X[np.lexsort(X.T[::-1])]


def f0_f3_kept():
    """F0 / F3: every part both builds share is vertex-identical fine vs --no-fine (the LIFT boards and labels FS5,
    the roof mast + anemometer arm FS1, the beacon lens, the EXIT light, the gates, the medallions): the tram pass
    changes no kept geometry (no maze / swing gate / post board / cabinet on these stations: F3 = none)."""
    for role in ("base", "top"):
        _t0, _tb0, tri0, n0 = _blockout(role, False)
        _t1, _tb1, tri1, n1 = _blockout(role, True)
        a = {}
        for tr, n in zip(tri0, n0):
            a.setdefault(n, []).append(tr)
        b = {}
        for tr, n in zip(tri1, n1):
            b.setdefault(n, []).append(tr)
        common = sorted(set(a) & set(b))
        # vertex-identical = the same set of vertex positions within 0.01 mm (the weld picks another survivor at a
        # shared corner when the kit parts leave: 1 um float noise) and the same triangle total over the shared
        # parts (two abutting parts may trade a triangle pair through that shared corner: reported, not failed)
        def vset(tr):
            return _rsort(np.unique(np.round(np.array(tr).reshape(-1, 3), 5), axis=0))
        diff = [n for n in common if vset(a[n]).shape != vset(b[n]).shape
                or not np.allclose(vset(a[n]), vset(b[n]), atol=1e-5)]
        traded = [(n, len(a[n]), len(b[n])) for n in common if len(a[n]) != len(b[n])]
        tot = (sum(len(a[n]) for n in common), sum(len(b[n]) for n in common))
        gone = sorted(set(a) - set(b))
        want_gone = FD_GONE[role] | ({"lamp_annexdoor"} | {n for n in a if n.startswith(("skirack_t", "ski_"))}
                                     if role == "base" else set())
        keep = [n for n in common if n.startswith(("label_", "board_", "mast", "antdish", "antpanel", "lamp_", "gate",
                                                   "medal_"))]
        check(not diff and tot[0] == tot[1] and set(gone) == want_gone and not (set(b) - set(a)),
              f"F0 / F3 {role}: {len(common)} shared parts vertex-identical fine vs --no-fine ({len(keep)} kept boards / "
              f"labels / mast / lamps / gates / medallions among them); gone {len(gone)} = the kit-replaced parts"
              + (" + lamp_annexdoor" if role == "base" else "") + f"; changed {diff[:6]}; new {sorted(set(b) - set(a))[:4]}; shared tris {tot}, traded {traded}")


def f4_shipped_fine():
    """F4: the shipped manifests carry the fine block: rows by kind, the frame, F1's kit rows = the block's mod rows."""
    for role in ("base", "top"):
        man = manifest(role)
        fd = man.get("fine_detail") or {}
        blk = ((man.get("detail1") or {}).get("sub_objects") or {}).get("block") or {}
        mods = [o for o in blk.get("objects", []) if o.get("kind") == "mod"]
        nv, nk = FD_ROWS[role]
        kr = fd.get("kit_rows") or []
        ok = (fd.get("flag") is True and fd.get("rows") == {"vanilla": nv, "mod": nk} == blk.get("kinds")
              and len(kr) == nk and np.allclose([r["row_pos"] for r in kr], [o["pos"] for o in mods], atol=1e-4)
              and fd.get("x_convention") == LD.STATION_X_CONVENTION and (man.get("args") or {}).get("no_fine") is False)
        check(ok, f"F4 {role}: shipped fine block {fd.get('rows')}, {len(kr)} F1 kit rows = the block's mod rows, "
                  f"frame stated, --no-fine off")


def _blockout_jh(role, jh):
    """(tri, names) of a blockout (fine on) with JH_DETAILS = jh."""
    saved = ts.JH_DETAILS
    ts.JH_DETAILS = jh
    try:
        args = ts.parse_args(["--role", role])
        ts.clear_scene()
        t, _table, _s, _m = ts.assemble(role, args, False)
        tri, names = ts.triangles(t)
    finally:
        ts.JH_DETAILS = saved
    return tri, names


def nb_checks():
    """Nitpick B (tram_station.JH_DETAILS; build/nitpick-b-tram.md):
    NB1 the switch only ADDS: every part of the JH-off base is vertex-identical with the pass on and nothing is removed;
        the new parts are exactly NB_PARTS + NB_DRESS; the top builds the same geometry either way (no top part reads it);
    NB2 placement: every V leg stands 0.03 in front of the front wall's X-braces, the shoes under the braces' foot and in
        front of the front columns (all measured from the brace / column parts),
        the canopy and its roof sheet in front of the braces and inside the lot / the frozen bounds (z <= 12.00,
        |x| <= 12.30, under 25.00), the V heads inside the canopy's x span, the chalkboard 0.02 off the grade (out of the
        +-0.01 engine-base band) and inside the lot, the annex frames / sills / LIFT TICKETS board on the annex's +X face
        and inside x 20.00;
    NB3 the shipped base manifest carries the nitpick_b block (LOD0 tris per family = the shipped mesh's parts) and the
        chalkboard PLACEHOLDER; the top manifest has none."""
    tri0, n0 = _blockout_jh("base", False)
    tri1, n1 = _blockout_jh("base", True)
    a, b = {}, {}
    for tr, n in zip(tri0, n0):
        a.setdefault(n, []).append(tr)
    for tr, n in zip(tri1, n1):
        b.setdefault(n, []).append(tr)

    def vset(tr):
        return _rsort(np.unique(np.round(np.array(tr).reshape(-1, 3), 5), axis=0))
    common = sorted(set(a) & set(b))
    diff = [n for n in common if vset(a[n]).shape != vset(b[n]).shape or not np.allclose(vset(a[n]), vset(b[n]), atol=1e-5)]
    new = set(b) - set(a)
    check(not diff and not (set(a) - set(b)) and new == NB_PARTS | NB_DRESS,
          f"NB1 base: {len(common)} JH-off parts vertex-identical with the pass on (changed {diff[:6]}), none removed "
          f"{sorted(set(a) - set(b))[:4]}; new = NB_PARTS + NB_DRESS ({len(new)}; missing "
          f"{sorted((NB_PARTS | NB_DRESS) - new)[:6]}, unexpected {sorted(new - NB_PARTS - NB_DRESS)[:6]})")
    for role in ("top",):
        t0_, m0 = _blockout_jh(role, False)
        t1_, m1 = _blockout_jh(role, True)
        check(m0 == m1 and np.array_equal(np.round(t0_, 6), np.round(t1_, 6)),
              f"NB1 {role}: the same {len(m0)} triangles with JH_DETAILS on and off")
    # nitpick B2: the blockout is in the PREFAB frame (the RAMPS base x - 4.00); NB2 measures in the design frame
    dx = ts.frame_dx("base")
    P = {n: np.array(v).reshape(-1, 3) - np.array([dx, 0.0, 0.0]) for n, v in b.items()}
    front = [n for n in P if n.startswith("brace_") and P[n][:, 2].min() > 9.0]      # the village wall's X-braces
    braces_z = max(float(P[n][:, 2].max()) for n in front)
    braces_y = min(float(P[n][:, 1].min()) for n in front)
    cols_z = max(float(P[n][:, 2].max()) for n in P if n.startswith("col_") and P[n][:, 2].min() > 9.0)
    leg_z = min(float(P[n][:, 2].min()) for n in P if n.startswith("jhstrut_"))
    shoes = np.concatenate([P[n] for n in P if n.startswith("jhshoe_")])
    # the shoes sit on the pier tops under the braces' lowest point, in front of the front columns
    can = np.concatenate([P[n] for n in ("jhcanopy", "jhroof")])
    heads_x = max(float(np.abs(P[n][:, 0]).max()) for n in P if n.startswith("jhstrut_"))
    ch = np.concatenate([P[n] for n in P if n.startswith("chalkboard_")])
    annex = np.concatenate([P[n] for n in P if n.startswith(("tickframe_a", "sill_a", "board_lifttickets",
                                                               "label_lifttickets"))])
    # (the joined mesh is float32: equalities hold to 0.1 mm, e.g. the annex face reads 19.5999985)
    ok2 = (leg_z >= braces_z + 0.03 and float(shoes[:, 1].max()) < braces_y and float(shoes[:, 2].min()) > cols_z
           and float(can[:, 2].min()) > braces_z and float(can[:, 2].max()) <= 12.0
           and float(np.abs(can[:, 0]).max()) <= 12.30 and float(can[:, 1].max()) < 25.0
           and heads_x <= ts.JH_CANOPY["x"] + 1e-4 and abs(float(ch[:, 1].min()) - 0.02) < 1e-4
           and float(np.abs(ch[:, 2]).max()) <= 12.0 and float(annex[:, 0].min()) >= ts.JH_ANNEX_X - 1e-4
           and float(annex[:, 0].max()) <= 20.0)
    check(ok2, f"NB2 base: V legs from z {leg_z:.3f} >= the braces' front {braces_z:.3f} + 0.03; shoes top "
               f"{shoes[:, 1].max():.3f} under the braces' foot {braces_y:.3f}, from z {shoes[:, 2].min():.3f} > the "
               f"front columns {cols_z:.3f}; canopy z "
               f"{can[:, 2].min():.3f}..{can[:, 2].max():.3f} (<= 12.00), |x| <= {np.abs(can[:, 0]).max():.3f}, top "
               f"{can[:, 1].max():.3f}; V heads |x| {heads_x:.3f} <= {ts.JH_CANOPY['x']}; chalkboard bottom "
               f"{ch[:, 1].min():.3f}; annex parts x {annex[:, 0].min():.3f}..{annex[:, 0].max():.3f}")
    man = manifest("base")
    nb = man.get("nitpick_b") or {}
    fam = nb.get("added_tris_lod0") or {}
    check(bool(nb) and sum(fam.values()) == nb.get("added_tris_lod0_total") and all(v > 0 for v in fam.values())
          and set(fam) == set(ts.JH_FAMILIES) and "chalkboard_" in (nb.get("placeholders") or {})
          and ("BUILT" if ts.RAMPS else "STOPPED") in (nb.get("ramps") or "") and not manifest("top").get("nitpick_b"),
          f"NB3: shipped base nitpick_b block {fam} (total {nb.get('added_tris_lod0_total')}), the chalkboard "
          f"PLACEHOLDER, the ramps {'BUILT (nitpick B2)' if ts.RAMPS else 'STOPPED'}; the top carries no block")


def main():
    t0 = time.perf_counter()
    work = tempfile.mkdtemp(prefix="detail_tram_test_")
    try:
        nb_checks()
        f0_f3_kept()
        f1_kit_contracts()
        f2_h1()
        f4_shipped_fine()
        d1_counts()
        d2_emissive()
        d3_labels()
        d6_frozen()
        d10_resolution()
        d7_d8_d9_access_subobjects()
        d4_d5_weather(work)
    finally:
        shutil.rmtree(work, ignore_errors=True)
    dt = time.perf_counter() - t0
    if FAILS:
        print(f"FAIL test_detail_tram: {len(FAILS)} failure(s) in {dt:.0f} s")
        for f in FAILS:
            print(f"  - {f}")
        sys.exit(1)
    print(f"PASS test_detail_tram: NB1-NB3 (nitpick B: the switch only adds, placement, shipped block) + F0-F4 (fine-detail step 3c: switch, kit rows F1 + negatives, H1, shipped block) + D1-D10 (dressing, emissive, labels L1-L3 / C13, weathering on / off + "
          f"shipped ladder, determinism, frozen numbers, access C15 / C16, sub-objects, resolution floors) "
          f"({dt:.0f} s)")


main()
