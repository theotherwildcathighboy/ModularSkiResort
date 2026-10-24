"""
Gate for tram_station.py (asset round 2026-10, P5; contract tram-stations.md section 15 with the
skeptic's MUST-CHANGEs and ROUND-SPEC R27-R31).

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_tram_station_determinism.py

  T1  determinism: each role assembled twice in one process (blockout) hashes identically
      (rounded vertex coords, polygon vertex lists + material indices, part ids, UVMap).
  T2  contracts on the blockout: base, base --no-clock-tower, top, top --no-wheels pass
      C1-C8, the R30 carrier clear (C4), C11 `_Win` cells, C6 engine-base ring, lot fit.
  T2n a wrong build FAILS: one injected violation per contract (C1 face rule, C2 stub,
      C4 cabin body / hanger ring both sides (R29/R30) / carriage, C5 boarding level, C6 closed ground
      part + geometry below 0, C7 lot (and C7 strict: 0.05 m past the lot fails, by part), C8
      height, C9 LOD0 cap, C11 cells, C13 painted label rotated 90 deg left / mirrored - revision 1;
      C14 part clashes - revision 1b: the R1 A-frame through the hut, the R1 binoculars inside the
      face struts, an undeclared clash, a part wholly inside a closed part, a member inside the hut).
      T2 also asserts C14 on every variant: the open findings (F2 / F3 base, --no-wheels shaft)
      are listed by name, the hut group is clear, the bullwheel frame keeps >= 0.30 m (F1).
      C15 platform reachability - revision 1c (fourth look): T2 asserts on every variant
      that every walkable surface reaches grade through the expected flights / ladders (top: the
      +X stair s, the maintenance stair m, the new -X flights wu + wl, each <= 3.7 m; base: u + l and
      the catwalk ladder pcat). T2n: the 1b top (LEFT_STAIR False: grate_bayL, grate_bayc,
      plinthcap, grate_svcplat orphaned), the 1b base (PCAT_LADDER False: grate_pcat), a floating
      grating, a beam in the -X stair's headroom, a box in the catwalk ladder's climbing corridor
      and a railing on no walkable surface each FAIL C15.
      Detail pass 1: T2 asserts the C15 crossing width on every join (>= 0.61, the gangway mouth
      included), the NOT_WALKABLE list (the base's interior floors stonecap / annexcap / trenchcap by
      name) and C16 (vanilla sub-object boxes: 14 base / 8 top). T2n: the 1c gangway mouth
      (SGPOST0_DEG 70: 0.574 m), an undeclared standable slab, and the design's tram-top bench on the
      diamond-plate boxes (C16) each FAIL.
      C17 gate span vs cabin door - revision 1e (revision2-check-tram-cabin section C): T2 asserts on
      every variant that all four berth edges leave a clear 1.90 m opening (>= 1.70) centered on the
      cabin's door seam (DOCK_Z -5.50, read from lift_cabin.py and its manifest) with no post or leaf in
      the door span (z -6.35..-4.65). T2n: the 1d fence grid (GATES_AT_DOOR False: base post -6.20 +
      leaf off center, top post -5.60 at the seam), an injected post, an injected closed leaf across the
      door, an opening under the minimum, an opening 0.04 m off the seam, an opening wider than the leaves
      beside it can close, and a cabin manifest that hosts the cabin elsewhere each FAIL C17. T4 asserts
      the C17 block of the exported manifest.
      Revision 2 (design/tram-stations-rev2.md + skeptic MC1-MC9; build/tram-stations-rev2.md): T2 asserts on
      every variant C18 (no part with more than 5 back-face first hits in the LOD0 walk-around; the cs2_bldg gear
      reported as known), C18b (every rim closed), C19 (no visible same-facing coplanar pair > 25 cm2 outside one
      kit), C20 / C20b (the wheels to scale, haul loops tangent within 1 mm), the haul rope <= 1,000 tris, the
      top bound 20.70 (ruling Q3), C16 13 top / 14 base rows. T2n: the open 1e rims (RIM_BORE_CLOSED False ->
      C18b + C18 names bullrim), a single-sided 2 x 2 m quad facing down (C18), the 1e drip lip 0.01 (C18 names
      drip_hutroof_*), the 1e head girders (HEADGIRDER_INSET 0 -> C19 headbeam | headgirder), the 10.4 m wheel
      (BULL_RIM 5.20 -> C20 diameter), a foot sheave at y 9.60 (C20 hut clearance), the 1e outer-bay lamp spot
      (LightpolePark at (9.95, 6.30, -5.70) -> C16 clash with the outer rail) each FAIL with their own code.
  T3  finish at --atlas 512 --lods --export --weather off (base AO 4, top AO 0; temp dir): C9 +
      C10 inside main, folder validates (0 errors), `_Win` / `_LOD1_Win` / `_LOD2_Win` /
      `_Emissive` present, BaseColor alpha 255, ControlMask B under the pictogram disc and
      the clock-dial rings, ControlMask R + G + B <= 1, BaseColor p95 (RAMPS base: p99) 0.78-0.82 under every
      present mask (the light-albedo contract is the pre-weathering bake: DECISIONS 171 lets the
      layer weather slot texels, test_detail_tram D4 covers the layer), batches <= 16; C13 on the
      shipped layout covers the dressing labels too (base 9, top 2).
  T4  manifest prefab_contract == module constants and == mesh-measured rope ends; hosted
      cabins identity at (+-5.50, 6.00, -5.50) with m_ParentMesh 0 (R29, skeptic MC1). Revision 2: every frozen
      prefab number equals the shipped 1e manifest's (assets/import/archive/superseded-rev2-2026-10-11): lot,
      levels, stubs, lanes, docks, hosted, terraform, prefab, base bounds; the top's bounds equal 1e except max y
      27.00 -> MAST_TOP 20.70 (ruling Q3, re-pinned). The manifest's rev2 block: C18 on LOD0 / 1 / 2 clean,
      C19 no failing pair, C20; the C16 rows 13 / 14.
  T5  LOD2 keep list survives, LOD1 / LOD2 delete lists are gone (revision 2: the top's LOD2 keeps the roof mast).
  T6  --review leaves the asset folder byte-identical (PNGs, settings, LOD0 + `_Win` FBX with
      the FBX clock frozen; LOD FBX / manifest excluded as in test_review_side_effects).
  T7  --skirt without --skirt-ack-probe exits non-zero.
  T8  the REAL exported SkiTramCabinA (assets/import/cabins/A), hosted identity in each bay
      (R29) and swept from the line to its dock, clears every non-rope station triangle
      (2 cm silhouette raster; skipped when the cabin is not exported).
  Fine-detail step 3c (build/fine-detail-step3c-tram-pali.md; tram_station.FINE_DETAIL, --no-fine): T2 counts 15 base / 13 top
      vanilla rows (+ the annex door wall light) and runs F1 inside the contract stage; T4 the manifest fine block
      (15 + 4 / 13 + 3 rows); T8 sweeps the real cabin against the dressing-kit rows' REAL meshes too.
  Nitpick B (build/nitpick-b-tram.md; tram_station.JH_DETAILS, base only): T2 runs every contract on the base with the JH
      canopy on V-struts, the annex ticket-window frames / sills / LIFT TICKETS, ENTRANCE and the chalkboard; T3 counts
      the two new labels in C13 (base 11); T2n: a V foot into the X-braces (C14), the canopy past the village lot edge
      (C7), the chalkboard on the grade (C6) each FAIL. The frozen numbers (T4) are unchanged.
  Nitpick B2 (build/nitpick-b2-tram-ramps.md; tram_station.RAMPS): the base's lot 5 x 3 -> 6 x 3 (the building moved
      BASE_DX -4.00 toward -X: every prefab-frame number = design + frame_dx) with a 1:8 switchback to the side-area door,
      the top's end stairs replaced by 1:8 ramps to concrete pads at the back lot edge. T2: the base's ramps A / B
      (GRADE -> landing_turn -> landing_rtop) and the door goal reached; the top's ramps R / L land on pad_R / pad_L,
      which C15b counts as grade (no-level), its only flight the maintenance stair m; every ramp 1:8, >= its clear width
      (2.40 base / 3.40 top), landings >= 1.50, end crossings >= 0.70; C16 18 base rows (+ 2 park lamps and a bin).
      T2n (nitpick B2): the top built as if it flattened (TERRAFORM) and the pads off the back lot edge each FAIL C15
      naming C15b; a 1:7 ramp, a lane narrowed under 2.40, a missing ramp rail, the balcony cut back off the side-area
      door each FAIL C15; the C7 probe moves past the new lot (x 24); the -X stair's headroom negative becomes the -X
      ramp's. T4 re-pinned for the base ONLY by the lot shift: the 1e numbers + frame_dx (stubs, docks, hosted), the lot
      6 x 3, bounds min x - 4.00 and max x = the new cell's paving 23.95; the top's frozen numbers and bounds unchanged.
      T8 sweeps the cabin at the shifted docks.
  Nitpick B3 (build/nitpick-b2-tram-ramps.md "Annex stair (B2 check F2)"; tram_station.ANNEX_STAIR): the annex got an inside
      stair from its 5.20 floor to the hall's inner door (sill 6.30) through a doorway cut in annexback; the annex floor
      (annexcap) is a C15 WALKABLE surface now (it left NOT_WALKABLE). T2 (base): the flights are u + l + annex; the
      route grade -> landing_turn -> landing_rtop -> landing_balc -> annexcap -> landing_annex -> deck_outerR is proved with
      no other stair carrying it (rt["chain"], carried_by_no_other_stair l + u unused), the stair 7 risers of 0.157 m, 6
      treads of 0.30, >= 1.20 m wide and between its rails, the doorway passage clear; annexcap is a surface and not in the
      not-walkable list. T2n NB3 (each FAILS C15 with "the side area is a dead end"): no stair (the B2 base's dead end),
      the wall not cut, 3 risers of 0.367 m, treads 1.0 m wide, the landing block begun inside the annex; ANNEX_STAIR False
      is the B2 base again (annexcap not walkable, flights u + l, no route, door goal reached); the pure plan numbers.
  Revision 3 (build/tram-top-rev3.md; tram_station.PLINTH_TOP; twenty-seventh look T-3): the TOP in PLINTH MODE
      (the L4 rule): no engine base, the foundation and supports continue below grade to min y -12.00, closed.
  T9  (a) above grade NOTHING moved: the plinth-mode top blockout vs the PLINTH_TOP False blockout (= the shipped nitpick-B2
      top) in process - every vertex above y 0.01 identical (0.1 mm), every old part present, the parts whose vertices
      changed are exactly the ones that now continue below grade (R3_CONTINUED: the pier, the pile caps, the pads, the ramp
      columns, the stair posts), every new part (found_*, lat*) wholly below grade; (b) the plinth on the contract build:
      C6p (no engine-base edge, no vertex within 0.01 of y 0, min y -12.00, every open edge below grade seated on another
      part) and an independent ray test - 3,456 rays from 1 m under the plinth floor up and from the four sides into the
      below-grade volume: no back face is ever the first hit (the PLINTH_TOP False top shows its open pad / footing bottoms
      there: the self-check); (c) bounds (-11.80, -12.00, -12.00) -> (11.80, 20.70, 11.60): only min y moved; (d) T9n: a part
      on the grade line, a part below the plinth floor, an open-bottom box floating below grade, the foundation stopped
      short of the plinth depth each FAIL C6p; PLINTH_TOP False passes C6 (engine base) again. T3 / T4 on the top's
      export: prefab_contract base_mode plinth, engine_base NONE (mode plinth), bounds min y -12.00 (RE-PINNED: the 1e
      manifest's 0.00 -> -12.00, the prefab delta; every other frozen number unchanged), the plinth rule on LOD0 / LOD1 /
      LOD2, the manifest plinth_top block (clearances), and the exported LOD0 vertex-identical above y 0.01 to the archived
      shipped top (assets/archive/.../superseded-tramtop-rev3-2026-10-13; skipped when the archive is absent).
Exit 1 on any failure.
"""
import datetime
import hashlib
import json
import os
import shutil
import sys
import tempfile
import time
import types

import bpy
import numpy as np

LIB = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, LIB)
import tram_station as ts  # noqa: E402
import cs2_validate  # noqa: E402

FAILS = []
FROZEN = datetime.datetime(2026, 1, 1, 0, 0, 0)
# revision 2: the shipped 1e exports, copied before the revision-2 re-export (T4 compares the frozen numbers)
REPO_1E = os.path.join(os.path.dirname(os.path.dirname(LIB)), "assets", "import", "archive",
                       "superseded-rev2-2026-10-11", "stations", "tram", "A")


def check(cond, msg):
    print(f"[tram-test] {'ok  ' if cond else 'FAIL'} {msg}")
    if not cond:
        FAILS.append(msg)
    return cond


def expect_fail(label, code, argv, inject=None, patch=None):
    saved = {}
    try:
        if patch:
            for k_, v in patch.items():
                saved[k_] = dict(getattr(ts, k_))
                getattr(ts, k_).update(v)
        ts.build_checked(argv, inject=inject)
        check(False, f"T2n {label}: build passed but must fail with {code}")
    except ts.ContractError as e:
        check(str(e).startswith(code) or code in str(e)[:12],
              f"T2n {label}: fails with {str(e)[:110]}")
    finally:
        for k_, v in saved.items():
            getattr(ts, k_).clear()
            getattr(ts, k_).update(v)


def freeze_fbx_clock():
    import io_scene_fbx.export_fbx_bin as fbxbin

    class _Frozen(datetime.datetime):
        @classmethod
        def now(cls, tz=None):
            return FROZEN

    fbxbin.datetime = types.SimpleNamespace(datetime=_Frozen)


def file_hashes(folder):
    out = {}
    for fn in sorted(os.listdir(folder)):
        with open(os.path.join(folder, fn), "rb") as fh:
            out[fn] = hashlib.sha1(fh.read()).hexdigest()
    return out


# --------------------------------------------------------------------------------------

def t1_determinism():
    for role in ("base", "top"):
        hs = []
        for _ in range(2):
            args = ts.parse_args(["--role", role])
            ts.clear_scene()
            t, _tb, _s, _m = ts.assemble(role, args, False)
            hs.append(ts.geometry_hash(t))
        check(hs[0] == hs[1], f"T1 {role}: run1 {hs[0]} run2 {hs[1]}")


def t2_contracts():
    for argv in (["--role", "base"], ["--role", "base", "--no-clock-tower"],
                 ["--role", "top"], ["--role", "top", "--no-wheels"]):
        try:
            t, win, table, rep = ts.build_checked(argv)
            ok = all(k_ in rep for k_ in ("C1", "C2", "C3", "C4", "C5", "C7", "C8", "C11"))
            check(ok, f"T2 {' '.join(argv)}: C1-C8 + C11 {rep['C11']}; {rep['C7']}; {rep['C8']}")
            stubs = [(s['lane'], s['a'], s['ring_centre']) for s in rep["C2"]]
            check(len(stubs) == 4, f"T2 {' '.join(argv)}: 4 stubs measured {stubs}")
            # C14 (revision 1b): no undeclared part clash; the open findings stay listed by name
            c14 = rep["C14"]
            known = {k_.split(":")[0] for k_ in c14["known_open"]}
            # revision 2: F3 (bwrim | lamp_rod_3) closed by the 4.0 m drive wheel (ruling Q6)
            want = {"base": {"mplatbrace_3 | stonecap", "mplatbrace_4 | stonecap",
                             "mplatbrace_5 | stonecap", "mplatpost_2 | stonecap",
                             "mplatpost_3 | stonecap"},
                    "top": {"bwshaft | sgtie"} if "--no-wheels" in argv else set()}[argv[1]]
            check(known == want, f"T2 {' '.join(argv)}: C14 {c14['box_pairs']} box pairs, "
                                 f"{c14['declared_contacts']} declared contacts, known open {sorted(known)}")
            # C15 (revision 1c): every walkable surface reaches grade; the expected flights / ladders
            c15 = rep["C15"]
            fl, la = c15["flights"], c15["ladders"]
            ra = c15.get("ramps") or {}
            if argv[1] == "top" and ts.RAMPS:
                # nitpick B2: the end stairs are ramps R / L onto the pads (C15b: grade on the no-level top)
                ok = (set(fl) == {"m"} and not la and set(ra) == {"R", "L"}
                      and ra["R"]["to"] == ["grate_headR"] and ra["L"]["to"] == ["grate_gangL"]
                      and ra["R"]["from"] == ["pad_R"] and ra["L"]["from"] == ["pad_L"]
                      and all(c15["C15b"][p_]["counts_as_grade"] for p_ in ("pad_R", "pad_L"))
                      and any({"grate_bayL", "grate_bayc", "plinthcap", "grate_gangL", "landing_topW"} <= set(a)
                              for a in c15["areas"]))
            elif argv[1] == "top":
                ok = (set(fl) == {"m", "s", "wu", "wl"} and not la and fl["wu"]["to"] == ["landing_midW"]
                      and fl["wl"]["to"] == ["GRADE"] and max(fl["wu"]["rise_m"], fl["wl"]["rise_m"]) <= 3.7
                      and any({"grate_bayL", "grate_bayc", "plinthcap", "grate_gangL", "landing_topW"} <= set(a)
                              for a in c15["areas"]))
            else:
                # nitpick B3: + the annex stair (flight `annex`, landing_annex -> the annex floor annexcap)
                ok = (set(fl) == ({"u", "l", "annex"} if ts.ANNEX_STAIR else {"u", "l"}) and set(la) == {"pcat"}
                      and la["pcat"]["from"] == ["deck_centre"] and la["pcat"]["to"] == ["grate_pcat"])
                if ts.ANNEX_STAIR:
                    ok = ok and fl["annex"]["from"] == ["landing_annex"] and fl["annex"]["to"] == ["annexcap"]
                if ts.RAMPS:
                    # nitpick B2: lane A from the grade to the turn landing, lane B on to the top landing; the door
                    ok = (ok and set(ra) == {"A", "B"} and ra["A"]["from"] == ["GRADE"]
                          and ra["A"]["to"] == ["landing_turn"] and ra["B"]["from"] == ["landing_turn"]
                          and ra["B"]["to"] == ["landing_rtop"] and (c15.get("goal") or {}).get("reached") is True
                          and c15["goal"]["onto"] == ["landing_balc"] and not c15["C15b"])
            if ts.RAMPS:
                need = 3.40 if argv[1] == "top" else 2.40
                okr = all(abs(r_["grade"] - 0.125) < 1e-4 and r_["cross_fall"] < 1e-4 and r_["clear_m"] >= need
                          and all(e_["crossing"] >= 0.70 for e_ in r_["ends"].values())
                          and all(e_["landing_m"] is None or e_["landing_m"] >= 1.50 - 1e-3
                                  for e_ in r_["ends"].values())
                          for r_ in ra.values())
                check(okr, f"T2 {' '.join(argv)}: C15 ramps " + "; ".join(
                    f"{k_} 1:{r_['grade_1_in']} run {r_['run_m']} rise {r_['rise_m']} clear {r_['clear_m']} "
                    f"landings {[e_['landing_m'] for e_ in r_['ends'].values()]} crossings "
                    f"{[e_['crossing'] for e_ in r_['ends'].values()]}" for k_, r_ in sorted(ra.items()))
                      + f" (>= {need} clear, 1:8, landings >= 1.50, crossings >= 0.70)")
            if argv[1] == "base" and ts.ANNEX_STAIR and ts.RAMPS:
                # nitpick B3 (B2 check F2): grade -> ramp -> annex -> stair -> hall -> boarding deck, proved by C15 with
                # no other stair carrying it; the stair 1.10 m in 7 equal risers, >= 1.20 m wide and railed
                rt, fa = c15["route"], fl["annex"]
                okb3 = (rt is not None and rt["ok"] and not rt["problems"]
                        and rt["chain"] == ["GRADE", "landing_turn", "landing_rtop", "landing_balc", "annexcap",
                                            "landing_annex", "deck_outerR"]
                        and rt["stairs"] == ["annex"] and rt["annex_side"] == ["landing_annex"]
                        and rt["hall_side"] == ["deck_outerR"] and sorted(rt["carried_by_no_other_stair"]) == ["l", "u"]
                        and rt["stair_width_m"]["annex"] >= 1.20 and rt["stair_between_rails_m"]["annex"] >= 1.20
                        and rt["passage"]["blocked_by"] == [] and "annexcap" in c15["surfaces"]
                        and fa["treads"] == 6 and abs(fa["rise_m"] - 1.10) < 2e-3 and abs(fa["riser"] - 1.10 / 7) < 1e-3
                        and abs(fa["going"] - 0.30) < 1e-3 and fa["width"] >= 1.20
                        and any({"annexcap"} <= set(a) and "landing_balc" in a for a in c15["areas"])
                        and any({"landing_annex", "deck_outerR"} <= set(a) for a in c15["areas"]))
                check(okb3, f"T2 {' '.join(argv)}: C15 route (annex stair) {' -> '.join(rt['chain'] or ['NONE'])}; stair "
                            f"{fa['treads'] + 1} risers of {fa['riser']} m ({fa['rise_m']} m), going {fa['going']}, "
                            f"{rt['stair_width_m']} m wide, {rt['stair_between_rails_m']} m between the rails; the "
                            f"doorway passage clear ({rt['passage']['blocked_by']}); no other stair carries it "
                            f"({rt['carried_by_no_other_stair']} not used)")
            check(ok and not c15["orphans"],
                  f"T2 {' '.join(argv)}: C15 {len(c15['surfaces'])} surfaces in {len(c15['areas'])} areas, "
                  f"flights {sorted((k_, f['rise_m']) for k_, f in fl.items())}, ladders "
                  f"{sorted((k_, L['rise_m']) for k_, L in la.items())}, {c15['posts']} posts seated, "
                  f"orphans {c15['orphans']}")
            # detail pass 1: every measured join crossing >= 0.61 (N1), the interior floors declared
            # not walkable by name (N2), nothing standable undeclared, C16 sub-objects placed
            joins = {k_: j for k_, j in c15["joins"].items() if j.get("opening") is not None}
            narrow = {k_: j["opening"] for k_, j in joins.items() if not j["ok"]}
            mouth = c15["joins"].get("grate_gangL | plinthcap", {}).get("opening")
            # nitpick B3: the annex floor is a WALKABLE surface now (ANNEX_STAIR): it leaves the not-walkable list
            want_nw = ({"stonecap", "trenchcap"} | (set() if ts.ANNEX_STAIR else {"annexcap"})
                       if argv[1] == "base" else set())
            check(not c15["undeclared"] and want_nw <= set(c15["not_walkable"])
                  and (argv[1] != "base" or (("annexcap" in c15["surfaces"]) == ts.ANNEX_STAIR
                                             and ("annexcap" in c15["not_walkable"]) != ts.ANNEX_STAIR))
                  and (argv[1] == "base" or (mouth is not None and mouth >= 0.61))
                  and all(j["ok"] for k_, j in joins.items() if "landing_top |" in k_ or "| landing_top" in k_
                          or "plinthcap" in k_ or "gangL" in k_),
                  f"T2 {' '.join(argv)}: C15 joins {len(joins)} measured (gangway mouth {mouth}; narrow, "
                  f"redundant: {narrow}), not walkable {sorted(c15['not_walkable'])}, undeclared "
                  f"{c15['undeclared']}")
            c16 = rep.get("C16")
            check(c16 is not None and len(c16["rows"]) == {"base": (15 if ts.FINE_DETAIL else 14) + (3 if ts.RAMPS else 0),
                                                            "top": 13}[argv[1]]
                  and not any(r.get("above_mesh_top_m") for r in c16["rows"]),
                  f"T2 {' '.join(argv)}: C16 {len(c16['rows']) if c16 else None} sub-objects placed "
                  f"(union boxes clear, supported, outside the carrier zones, none above the mesh top)")
            # revision 2: C18 / C18b / C19 / C20 hold; the haul rope within its budget; the top bound (ruling Q3)
            c18 = rep["C18"]["LOD0"]
            worst = [r for r in c18["parts"] if r["part"] not in {k_["part"] for k_ in c18["known"]}]
            check(all(r["back"] <= 5 for r in worst) and not any((c18["rims_open"] or {}).values())
                  and set(c18["rims_open"] or {}) >= ({"bullrim", "dwrim_L", "dwrim_R", "fsrim_L", "fsrim_R",
                                                       "drumrim_L", "drumrim_R"} if argv == ["--role", "top"] else
                                                      {"bwrim"} if argv[1] == "base" else set()),
                  f"T2 {' '.join(argv)}: C18 LOD0 {c18['back']} back-face first hits of {c18['hits']} hits, worst "
                  f"{[(r['part'], r['back']) for r in worst[:3]]} (<= 5); known {[(r['part'], r['back']) for r in c18['known']]}; "
                  f"C18b rims closed {sorted(c18['rims_open'] or {})}")
            c19 = rep["C19"]
            check(not c19["fail"] and all(r["kit"] or not r["visible"] for r in c19["all"] if r["cm2"] > 25.0),
                  f"T2 {' '.join(argv)}: C19 {c19['pairs']} same-facing coplanar pairs ({c19['cm2']:.0f} cm2), "
                  f"{c19['over_25_cm2']} over 25 cm2, none visible outside a kit (kit {[(r['a'], r['b']) for r in c19['kit']]})")
            c20 = rep["C20"]
            ok20 = ("skipped" in c20 if "--no-wheels" in argv else
                    c20["code"] == ("C20" if argv[1] == "top" else "C20b") and c20["tangency_worst_mm"] <= 1.0)
            if argv == ["--role", "top"]:
                ok20 = ok20 and 3.40 <= c20["bullwheel_dia_m"] <= 4.20 and c20["wrap_deg"]["bull"] >= 60.0 \
                    and c20["wrap_deg"]["foot"] >= 90.0 and c20["above_deck_m"] >= 4.5 and c20["below_crest_m"] >= 2.0
            elif argv[1] == "base":
                ok20 = ok20 and 3.40 <= c20["drive_dia_m"] <= 4.20 and c20["wrap_deg"]["drive"] >= 60.0
            check(ok20, f"T2 {' '.join(argv)}: C20 {c20}")
            tri_, names_ = ts.triangles(t)
            n_haul = sum(1 for n_ in names_ if n_ == "rope_haul")
            top_y = rep["bounds"]["max"][1]
            check(n_haul <= 1000 and abs(top_y - (ts.MAST_TOP if argv[1] == "top" else
                                                   ts.CT_H if "--no-clock-tower" not in argv else top_y)) <= 0.05,
                  f"T2 {' '.join(argv)}: rope_haul {n_haul} tris (<= 1,000), bounds top {top_y}"
                  + (f" (ruling Q3: {ts.MAST_TOP}, was 27.00)" if argv[1] == "top" else ""))
            c17 = rep["C17"]
            ed17 = c17["edges"]
            d17 = c17["door"]
            span17 = (d17["centre"] - d17["half"], d17["centre"] + d17["half"])
            ok17 = (len(ed17) == 4 and abs(d17["centre"] - ts.DOCK_Z) < 1e-9 and abs(d17["half"] - 0.85) < 1e-9
                    and d17["manifest_z_bay"] == ts.DOCK_Z and d17["t13_held"] is True
                    and all(e_["opening_m"] >= 1.70 and abs(e_["centre_z"] - ts.DOCK_Z) <= 0.02
                            and e_["margin_lo_m"] >= 0.0 and e_["margin_hi_m"] >= 0.0
                            and min(e_["leaf_lo_m"], e_["leaf_hi_m"]) >= e_["opening_m"] / 2.0
                            and e_["lo_post"].startswith("gatepost_") and e_["hi_post"].startswith("gatepost_")
                            for e_ in ed17.values()))
            # independent of check_gate_door: no gate part's z extent overlaps the span, measured here
            pf17, parts17 = ts._part_tris(t)
            inside17 = []
            for nm_, tr_ in parts17.items():
                if nm_.startswith(("gatepost_", "gate_")):
                    zq = pf17[tr_].reshape(-1, 3)[:, 2]
                    if zq.max() > span17[0] + 1e-6 and zq.min() < span17[1] - 1e-6:
                        inside17.append(nm_)
            check(ok17 and not inside17,
                  f"T2 {' '.join(argv)}: C17 4 berth edges, opening {sorted({e_['opening_m'] for e_ in ed17.values()})} m "
                  f"(>= 1.70) centred on the door seam {d17['centre']} (door span z {span17[0]:.2f}..{span17[1]:.2f}, "
                  f"leaves -+{d17['half']}), margins {sorted({e_['margin_lo_m'] for e_ in ed17.values()})}, "
                  f"parts in the span {inside17}")
            if argv[1] == "top":
                hut = c14["hut"]
                ok = min(hut["nearest_m"].values()) >= 0.05 - 1e-6 and (
                    "--no-wheels" in argv or hut["bullframe_m"] >= 0.30 - 1e-6)
                check(ok, f"T2 {' '.join(argv)}: hut group clear of every part (nearest "
                          f"{hut['nearest_m']}), bullwheel frame {hut['bullframe_m']} m >= 0.30 (F1)")
        except Exception as e:  # noqa: BLE001
            check(False, f"T2 {' '.join(argv)}: raised {e!r}")


def t2_negative():
    B, T = ["--role", "base"], ["--role", "top"]
    expect_fail("C1 face rule", "C1", B, lambda k: k.box("probe_c1", 0.0, 1.0, 1.0, 2.0, -10.80,
                                                          -10.50, "steel"))
    expect_fail("C2 stub", "C2", B, lambda k: k.tube("rope_track_Lzz", [(-5.30, 16.0, -12.0),
                                                                        (-5.30, 16.0, -11.0)],
                                                     0.035, "rope", segs=8))
    expect_fail("C4 cabin body", "C4", B, lambda k: k.box("probe_body", 5.0, 6.0, 7.0, 8.0, -6.0,
                                                           -5.0, "steel"))
    expect_fail("C4 R30 ring, outboard (0.5-0.8 at rope height)", "C4", T,
                lambda k: k.box("probe_crank", 6.00, 6.30, 15.80, 16.20, -9.00, -8.80, "steel"))
    expect_fail("C4 ring, inboard upright band (built cabin is symmetric, R29)", "C4", B,
                lambda k: k.box("probe_inboard", -4.70, -4.50, 14.90, 15.15, -9.55, -9.25,
                                "steel"))
    expect_fail("C4 carriage frame (above the rope)", "C4", T,
                lambda k: k.box("probe_frame", 5.40, 5.60, 16.30, 16.40, -9.00, -8.00, "steel"))
    expect_fail("C4 R30 ring (outboard 0.8-1.1, dz -2)", "C4", B,
                lambda k: k.box("probe_col", -6.55, -6.35, 13.80, 14.20, -7.00, -6.80, "steel"))
    expect_fail("C5 boarding level", "C5", B, lambda k: k.box("deck_probe", 2.0, 3.2, 6.30, 6.45,
                                                               -6.0, -5.0, "concrete"))
    expect_fail("C6 closed ground part", "C6", B, lambda k: k.box("probe_c6", -18.0, -17.0, 0.0,
                                                                   1.0, 5.0, 6.0, "concrete"))
    if ts.PLINTH_TOP:
        # revision 3: the top is in plinth mode (geometry below y 0 is the design); its own negatives are T9n's
        expect_fail("C6p a part on the grade line (the top in plinth mode)", "C6p", T,
                    lambda k: k.box("probe_below", 0.0, 1.0, 0.0, 0.50, 5.0, 6.0, "concrete"))
    else:
        expect_fail("C6 below ground", "C6", T, lambda k: k.box("probe_below", 0.0, 1.0, -0.50, 0.50,
                                                                 5.0, 6.0, "concrete"))
    # nitpick B2: the base lot reaches design x 28.0 (prefab 24.0): the probe sits 0.4 m past it (it sat past 20.0)
    x7 = 28.4 if ts.RAMPS else 20.4
    expect_fail("C7 lot fit", "C7", B, lambda k: k.box("probe_c7", x7, x7 + 0.8, 0.5, 1.0, 0.0, 1.0,
                                                        "concrete"))
    # C7 is strict (review F1): 0.05 m past the top lot's front line failed nothing under the old
    # +0.5 tolerance; now it fails and the message names the part
    try:
        ts.build_checked(T, inject=lambda k: k.box("probe_c7s", 0.0, 1.0, 0.5, 1.0, 11.90, 12.05,
                                                   "concrete"))
        check(False, "T2n C7 strict: a 0.05 m overhang passed")
    except ts.ContractError as e:
        check(str(e).startswith("C7") and "probe_c7s 0.050" in str(e),
              f"T2n C7 strict: 0.05 m overhang fails by part: {str(e)[:150]}")
    expect_fail("C8 height", "C8", B, lambda k: k.box("probe_c8", 0.0, 0.5, 25.0, 25.6, 0.0, 0.5,
                                                       "steel"))
    expect_fail("C9 LOD0 cap", "C9", T, patch={"LOD0_CAP": {"top": 1000}})
    try:
        _t, win, _tb, _r = ts.build_checked(T)
        ts.check_win_cells(win, "base")
        check(False, "T2n C11 cells: top `_Win` accepted against the base cell set")
    except ts.ContractError as e:
        check(str(e).startswith("C11"), f"T2n C11 cells: fails with {str(e)[:90]}")
    # C13 (revision 1, verdict 10): a painted label whose UVs are turned 90 deg left - the bug
    # seen on the pictogram - and a mirrored one both fail the upright check
    t, _win, tb, _r = ts.build_checked(B)
    me = t.data
    uvl = me.uv_layers["UVMap"]
    polys = ts._label_polys(me, tb)["pictodisc"]
    saved = {l: tuple(uvl.data[l].uv) for p in polys for l in p.loop_indices}
    for tag, fn in (("rotated 90 deg left", lambda u, v, cu, cv: (cu - (v - cv), cv + (u - cu))),
                    ("mirrored", lambda u, v, cu, cv: (cu - (u - cu), v))):
        for p in polys:
            ls = list(p.loop_indices)
            cu = sum(saved[l][0] for l in ls) / len(ls)
            cv = sum(saved[l][1] for l in ls) / len(ls)
            for l in ls:
                uvl.data[l].uv = fn(saved[l][0], saved[l][1], cu, cv)
        try:
            ts.check_label_uvs(t, tb, "UVMap")
            check(False, f"T2n C13 label {tag}: accepted")
        except ts.ContractError as e:
            check(str(e).startswith("C13"), f"T2n C13 label {tag}: fails with {str(e)[:100]}")
    for l, uv in saved.items():
        uvl.data[l].uv = uv
    # nitpick B (JH_DETAILS): + LIFT TICKETS and ENTRANCE (5 dressing labels)
    nb13 = {"label_lifttickets", "label_entrance"} if ts.JH_DETAILS else set()
    check(set(ts.check_label_uvs(t, tb, "UVMap")) ==
          {"pictodisc", "pictoplinth", "ctface_nx", "ctface_nz", "ctface_px", "ctface_pz",
           "label_dep", "label_ops", "label_tickets"} | nb13,
          f"T2n C13: restored labels pass again (pictodisc, pictoplinth, 4 clock dials, {3 + len(nb13)} dressing labels)")
    # C14 (revision 1b): the revision-1 wrong builds and injected clashes must each FAIL C14
    for label, attr, value, must in (
            ("R1 bullwheel A-frame feet at x +-2.60 (leg through the hut, F1)", "BULLFRAME_FOOT_X",
             2.60, ("bullframe_R | hut", "bullframe_R | hutroof", "hut clearance")),
            ("R1 deck binoculars at x +-9.40 (inside the face struts)", "SCOPE_X", 9.40,
             ("scope_head_L | strut_face_L", "scope_post_L in strut_face_L"))):
        saved_v = getattr(ts, attr)
        setattr(ts, attr, value)
        try:
            ts.build_checked(T)
            check(False, f"T2n C14 {label}: build passed")
        except ts.ContractError as e:
            check(str(e).startswith("C14") and all(m_ in str(e) for m_ in must),
                  f"T2n C14 {label}: fails with {str(e)[:170]}")
        finally:
            setattr(ts, attr, saved_v)
    # revision 2: the crane beam is at y 18.00-18.70, x -6.20..6.20 (was 25.0-25.8): the probes follow it
    expect_fail("C14 undeclared clash (a post through the crane beam)", "C14", T,
                lambda k: k.box("probe_c14", 3.00, 3.40, 17.80, 18.90, 5.10, 5.50, "steel"))
    expect_fail("C14 part wholly inside a closed part (no triangle pair)", "C14", T,
                lambda k: k.box("probe_c14in", 3.00, 3.20, 18.20, 18.40, 5.20, 5.40, "steel"))
    expect_fail("C14 member inside the open-bottom hut", "C14", T,
                lambda k: k.box("probe_c14hut", 2.50, 3.00, 7.00, 8.00, 0.50, 1.00, "steel"))
    # C15 (revision 1c, fourth look): the shipped 1b builds and injected faults each FAIL
    for label, attr, argv, must in (
            ("the 1b top: no -X stair, no gangway ('left platform')", "LEFT_STAIR", T,
             ("orphan surfaces", "grate_bayL", "grate_bayc", "plinthcap", "grate_svcplat")),
            ("the 1b base: no catwalk ladder", "PCAT_LADDER", B, ("orphan surfaces", "grate_pcat"))):
        saved_v = getattr(ts, attr)
        setattr(ts, attr, False)
        try:
            ts.build_checked(argv)
            check(False, f"T2n C15 {label}: build passed")
        except ts.ContractError as e:
            check(str(e).startswith("C15") and all(m_ in str(e) for m_ in must)
                  and "grate_bayR" not in str(e) and "landing_top " not in str(e),
                  f"T2n C15 {label}: fails with {str(e)[:260]}")
        finally:
            setattr(ts, attr, saved_v)
    # detail pass 1 (revision1c-check-tram-stair N1): the 1c gangway mouth, 0.574 m between the capf rail
    # and sgpost_L0 at 70 deg, FAILS the C15 crossing width (the plinth deck group is orphaned)
    saved_v = ts.SGPOST0_DEG
    ts.SGPOST0_DEG = 70.0
    try:
        ts.build_checked(T)
        check(False, "T2n C15 the 1c gangway mouth (SGPOST0_DEG 70): build passed")
    except ts.ContractError as e:
        check(str(e).startswith("C15") and "grate_gangL | plinthcap 0.574" in str(e)
              and "plinthcap" in str(e).split(";")[0],
              f"T2n C15 the 1c gangway mouth (SGPOST0_DEG 70): fails with {str(e)[-200:]}")
    finally:
        ts.SGPOST0_DEG = saved_v
    # detail pass 1 (N2): a standable slab nobody declared FAILS; the design's top bench on the
    # diamond-plate boxes (skeptic F) FAILS C16
    expect_fail("C15 undeclared standable slab", "C15", T,
                lambda k: k.box("probe_c15s", 8.00, 10.00, 3.00, 3.20, 3.00, 5.00, "steel"))
    saved_rows = ts.SUBOBJ_ROWS["top"]
    ts.SUBOBJ_ROWS["top"] = saved_rows + (("GardenBenchRandom01", (-1.60, ts.DECK_Y, 10.70), "+Z", 0, "deck"),)
    try:
        ts.build_checked(T)
        check(False, "T2n C16 the design's bench on dplate_0 / dplate_1: build passed")
    except ts.ContractError as e:
        check(str(e).startswith("C16") and "dplate_0" in str(e) and "dplate_1" in str(e),
              f"T2n C16 the design's bench on dplate_0 / dplate_1: fails with {str(e)[:160]}")
    finally:
        ts.SUBOBJ_ROWS["top"] = saved_rows
    for label, argv, inject, must in (
            ("a floating grating platform", T,
             lambda k: k.grating("probe15", -1.00, 1.00, 8.00, 9.00, 9.00), "grate_probe15"),
            (("a beam in the -X ramp's headroom (1.6 m over its deck)", T,
              lambda k: k.box("probe_c15h", -11.00, -9.00, 7.65, 7.85, 2.40, 2.60, "steel"),
              "ramp L: headroom blocked by ['probe_c15h']") if ts.RAMPS else
             ("a beam in the -X stair's headroom", T,
              lambda k: k.box("probe_c15h", -11.50, -10.30, 6.00, 6.20, 2.40, 2.60, "steel"),
              "flight wu: headroom blocked by ['probe_c15h']")),
            ("a box in the catwalk ladder's climbing corridor", B,
             lambda k: k.box("probe_c15l", 1.40, 1.60, 12.00, 12.20, -7.50, -7.30, "steel"),
             "ladder pcat: climbing corridor blocked by ['probe_c15l']"),
            ("a railing on no walkable surface", T,
             lambda k: k.handrail("probe15", [(-1.00, 9.00, 8.50), (1.00, 9.00, 8.50)], "galv"),
             "railing posts on no walkable surface")):
        try:
            ts.build_checked(argv, inject=inject)
            check(False, f"T2n C15 {label}: build passed")
        except ts.ContractError as e:
            check(str(e).startswith("C15") and must in str(e), f"T2n C15 {label}: fails with {str(e)[:200]}")
    t2_negative_c17()
    t2_negative_rev2()
    t2_negative_nb()
    if ts.RAMPS:
        t2_negative_nb2()
    if ts.ANNEX_STAIR and ts.RAMPS:
        t2_negative_nb3()


def t2_negative_nb():
    """Nitpick B (tram_station.JH_DETAILS; build/nitpick-b-tram.md): the JH canopy / V-struts / chalkboard, misplaced,
    FAIL the contract that guards the slot: a V foot 0.14 m back (into the front wall's X-braces) -> C14; the canopy
    pushed 0.15 m past the village lot edge -> C7 (strict lot); the chalkboard set on the grade (its foot edges in the
    +-0.01 engine-base band) -> C6."""
    B = ["--role", "base"]
    for label, code, patch, must in (
            ("a V-strut foot into the X-braces (foot z 10.30)", "C14", {"JH_V": {"foot_z": 10.30}}, ("brace_", "jhstrut_")),
            ("the canopy past the village lot edge (z1 12.10)", "C7", {"JH_CANOPY": {"z1": 12.10}}, ("leave the",)),
            ("the chalkboard on the grade (y0 0.00)", "C6", {"JH_CHALK": {"y0": 0.0}}, ("chalkboard_",))):
        saved = {k_: dict(getattr(ts, k_)) for k_ in patch}
        try:
            for k_, v in patch.items():
                getattr(ts, k_).update(v)
            ts.build_checked(B)
            check(False, f"T2n NB {label}: build passed but must fail with {code}")
        except ts.ContractError as e:
            check(str(e).split(" ")[0].split(":")[0].startswith(code) and all(m_ in str(e) for m_ in must),
                  f"T2n NB {label}: fails with {str(e)[:200]}")
        finally:
            for k_, v in saved.items():
                getattr(ts, k_).clear()
                getattr(ts, k_).update(v)


def t2_negative_nb2():
    """Nitpick B2 (tram_station.RAMPS; build/nitpick-b2-tram-ramps.md): C15b and the ramp rules each FAIL a wrong build
    with C15 (and the C15b wording where the pads are at stake):
      - the top as if it flattened (TERRAFORM top 'flatten'): its pads are not grade -> every top surface orphaned, C15b
        names both pads ('not no-level');
      - the top's pads 1.0 m in from the back lot edge (TOP_RAMP pad 9.10..10.60): C15b names the edge distance;
      - 1:7 top ramps (RAMP grade 1/7; the check keeps RAMP max_grade 1:8): 'steeper than 1:8' (the base's plan
        asserts its door levels at 1:8, so a 1:7 base cannot be built: base_ramp_plan raises before C15);
      - the base's 1:8 lanes against a 1:9 check (RAMP max_grade 1/9): 'steeper than 1:9' on both lanes;
      - lane B narrowed to 2.30 m (2.132 m clear < 2.40; the shipped 2.60 m lane measures 2.432);
      - lane A's outer rail left out: 'no handrail bounding the walking width';
      - the balcony cut back to z 6.20 (the side-area door's span 5.00..6.80 lands on no surface)."""
    B, T = ["--role", "base"], ["--role", "top"]

    def run(label, argv, must, patch=None):
        saved = {k_: dict(getattr(ts, k_)) for k_ in (patch or {})}
        try:
            for k_, v in (patch or {}).items():
                getattr(ts, k_).update(v)
            ts.build_checked(argv)
            check(False, f"T2n NB2 {label}: build passed but must fail with C15")
        except ts.ContractError as e:
            check(str(e).startswith("C15") and all(m_ in str(e) for m_ in must),
                  f"T2n NB2 {label}: fails with {str(e)[:260]}")
        finally:
            for k_, v in saved.items():
                getattr(ts, k_).clear()
                getattr(ts, k_).update(v)

    run("the top flattening (C15b: a pad is grade on a NO-LEVEL station only)", T,
        ("orphan surfaces", "C15b", "pad_R", "pad_L", "not no-level", "grate_bayR", "plinthcap"),
        patch={"TERRAFORM": {"top": "flatten"}})
    run("the top's pads 1.0 m in from the back lot edge (C15b: at the lot edge)", T,
        ("C15b", "pad_R", "from the lot line", "orphan surfaces"),
        patch={"TOP_RAMP": {"pad": (9.10, 10.60)}})
    run("1:7 top ramps (the check holds 1:8)", T, ("ramp R", "steeper than 1:8", "ramp L"),
        patch={"RAMP": {"grade": 1.0 / 7.0}})
    run("the base's 1:8 lanes against a 1:9 check", B, ("ramp A", "steeper than 1:9", "ramp B"),
        patch={"RAMP": {"max_grade": 1.0 / 9.0}})
    run("lane B narrowed to 2.30 m (2.132 m clear)", B, ("ramp B", "clear width 2.13", "< 2.4"),
        patch={"NB2_LANE": {"B": (22.90, 25.20)}})
    saved_rails = ts._rails
    ts._rails = lambda k, runs, mat, _r=saved_rails: _r(k, [r_ for r_ in runs if r_[0] != "rampAo"], mat)
    try:
        run("lane A's outer rail left out", B, ("ramp A", "no handrail bounding the walking width"))
    finally:
        ts._rails = saved_rails
    run("the balcony cut back to z 6.20 (off the side-area door)", B,
        ("side-area door", "opens onto no walkable surface"), patch={"NB2_BALC": {"z0": 6.20}})


def t2_negative_nb3():
    """Nitpick B3 (tram_station.ANNEX_STAIR; build/nitpick-b2-tram-ramps.md "Annex stair (B2 check F2)"): the side area is
    not a dead end. Each wrong annex FAILS C15 (the walk graph now has the annex floor in it) with the dead-end text:
      - no stair at all (NB3 flight False: the B2 base's annex with a doorway and a landing block): no stair or ramp
        joins the annex floor to the hall - the exact dead end the B2 check found, which C15 could not see;
      - the wall not cut (NB3 wall_cut False): the doorway between the hall door and the stair head is blocked;
      - 3 risers of 0.367 m (NB3 risers 3): the flight breaks C15's riser range and the route has no stair;
      - treads 1.0 m wide (NB3 inset 0.60, the doorway is 2.20 m): the stair is under the 1.20 m the route needs;
      - the landing block begun inside the annex (NB3 link_x0 12.60): the hall's inner door opens onto no landing."""
    B = ["--role", "base"]

    def run(label, must, patch):
        saved = {k_: (dict(getattr(ts, k_)) if isinstance(getattr(ts, k_), dict) else getattr(ts, k_)) for k_ in patch}
        try:
            for k_, v in patch.items():
                getattr(ts, k_).update(v)
            ts.build_checked(B)
            check(False, f"T2n NB3 {label}: build passed but must fail with C15")
        except ts.ContractError as e:
            check(str(e).startswith("C15") and all(m_ in str(e) for m_ in must),
                  f"T2n NB3 {label}: fails with {str(e)[:300]}")
        finally:
            for k_, v in saved.items():
                getattr(ts, k_).clear()
                getattr(ts, k_).update(v)

    run("no stair (the B2 base's dead end)", ("the side area is a dead end", "no stair or ramp joins the annex floor"),
        {"NB3": {"flight": False}})
    run("the annex wall not cut at the doorway", ("the side area is a dead end", "doorway", "annexback"),
        {"NB3": {"wall_cut": False}})
    run("3 risers of 0.367 m", ("flight annex", "risers", "the side area is a dead end"), {"NB3": {"risers": 3}})
    run("treads 1.0 m wide", ("the side area is a dead end", "stair annex is 1.000 m wide < 1.2"),
        {"NB3": {"inset": 0.60}})
    run("the landing block begun inside the annex (x 12.60)", ("the side area is a dead end", "no walkable landing"),
        {"NB3": {"link_x0": 12.60}})
    # the flag off = the B2 base's own contracts: the annex floor is NOT_WALKABLE again, no `annex` flight, no route
    saved = ts.ANNEX_STAIR
    ts.ANNEX_STAIR = False
    try:
        _t, _w, _tb, rep = ts.build_checked(B)
        c15 = rep["C15"]
        check(set(c15["flights"]) == {"u", "l"} and "annexcap" in c15["not_walkable"] and "annexcap" not in c15["surfaces"]
              and c15["route"] is None and c15["goal"]["reached"] is True,
              f"T2n NB3 ANNEX_STAIR False: the B2 base's contracts hold again (flights {sorted(c15['flights'])}, annexcap "
              f"not walkable, no route, door goal reached)")
    except ts.ContractError as e:
        check(False, f"T2n NB3 ANNEX_STAIR False: the B2 base fails its own contracts: {str(e)[:200]}")
    finally:
        ts.ANNEX_STAIR = saved
    # the pure plan: 1.10 m in 7 equal risers, 6 treads of 0.30, the stair inside the annex, well short of the ramp door
    p = ts.base_annex_plan()
    check(abs(p["rise_total"] - 1.10) < 1e-9 and p["risers"] == 7 and abs(p["riser"] - 1.10 / 7) < 1e-6
          and p["treads"] == 6 and abs(p["run"] - 1.80) < 1e-9 and p["x_foot"] < ts.JH_ANNEX_X - 3.0
          and p["width"] >= 1.20 and p["rails_clear"] >= 1.20 and p["landing_inside"] >= 1.20
          and abs(2 * p["riser"] + p["going"] - 0.61) < 0.01,
          f"T2n NB3 plan: {p['risers']} risers of {p['riser']} m = {p['rise_total']} m, {p['treads']} treads of "
          f"{p['going']}, run {p['run']}, foot x {p['x_foot']} (the ramp door's glazing at {ts.JH_ANNEX_X}), "
          f"{p['width']} m wide, {p['rails_clear']} m between the rails, landing {p['landing_inside']} m inside")


def expect_rev2(label, code, argv, must, attr=None, value=None, inject=None):
    """Revision 2: a wrong build FAILS with `code` (its own code: every older contract passes first, or C20 runs
    at build start) and every substring of `must` appears in the message."""
    saved_v = getattr(ts, attr) if attr else None
    if attr:
        setattr(ts, attr, value)
    try:
        ts.build_checked(argv, inject=inject)
        check(False, f"T2n {code} {label}: build passed")
    except ts.ContractError as e:
        check(str(e).split(":")[0] == code and all(m_ in str(e) for m_ in must),
              f"T2n {code} {label}: fails with {str(e)[:260]}")
    finally:
        if attr:
            setattr(ts, attr, saved_v)


def t2_negative_rev2():
    """Revision 2 (design 8 T2n): each 1e defect, rebuilt, FAILS its new contract."""
    B, T = ["--role", "base"], ["--role", "top"]
    # the open 1e rims: C18b names them, and the walk part of the message names the bull rim's back-face hits
    expect_rev2("the open 1e rim bores (RIM_BORE_CLOSED False)", "C18b", T,
                ("open rim bores", "bullrim", "dwrim_L", "fsrim_L", "back-face first hits", "bullrim "),
                attr="RIM_BORE_CLOSED", value=False)
    expect_rev2("the open 1e base drive-wheel bore", "C18b", B, ("open rim bores", "bwrim"),
                attr="RIM_BORE_CLOSED", value=False)
    expect_rev2("a single-sided 2 x 2 m quad facing down at y 20", "C18", T, ("probe_c18",),
                inject=lambda k: k.quad("probe_c18", [(-1.0, 20.0, -1.0), (1.0, 20.0, -1.0), (1.0, 20.0, 1.0),
                                                      (-1.0, 20.0, 1.0)], "steel", (0, -1, 0)))
    expect_rev2("the 1e drip lip 0.01 (the strips' open inner face above the roof)", "C18", T,
                ("drip_hutroof_",), attr="DRIP_LIP", value=0.01)
    expect_rev2("the 1e head girders (HEADGIRDER_INSET 0: top / ends in the head beams' planes)", "C19", T,
                ("headbeam_face | headgirder_L", "headbeam_rear | headgirder_R"), attr="HEADGIRDER_INSET", value=0.0)
    expect_rev2("the 10.4 m 1e bullwheel (BULL_RIM 5.20)", "C20", T, ("bullwheel diameter 10.40",),
                attr="BULL_RIM", value=5.20)
    expect_rev2("a foot sheave at y 9.60 (onto the hut roof)", "C20", T, ("foot sheave bottom 8.95", "hut clearance"),
                attr="FS_Y", value=9.60)
    expect_rev2("the 10.6 m 1e base drive wheel (BASE_BW_R 5.30)", "C20b", B, ("drive wheel diameter 10.60",),
                attr="BASE_BW_R", value=5.30)
    saved_rows = ts.SUBOBJ_ROWS["top"]
    ts.SUBOBJ_ROWS["top"] = saved_rows + (("LightpoleParkRandom01", (9.95, ts.DECK_Y, -5.70), "+Z", 0, "deck"),)
    try:
        ts.build_checked(T)
        check(False, "T2n C16 a park lamp at the 1e outer-bay lamp spot: build passed")
    except ts.ContractError as e:
        check(str(e).startswith("C16") and "rail_outerR" in str(e),
              f"T2n C16 a park lamp at the 1e outer-bay lamp spot (9.95, 6.30, -5.70): fails with {str(e)[:200]}")
    finally:
        ts.SUBOBJ_ROWS["top"] = saved_rows
    # the haul routes are pure geometry: the M loop's tangents and wraps (design T3 / skeptic MC9 numbers)
    r = ts.haul_route_top()
    check(abs(r["strand_deg"] - 49.2) < 0.1 and abs(r["wrap_deg"]["bull"] - 98.4) < 0.1
          and abs(r["wrap_deg"]["foot"] - 139.2) < 0.1,
          f"T2n C20 haul_route_top: strand {r['strand_deg']} deg, wraps {r['wrap_deg']} (skeptic: 49.2 / 98.4 / 139.2), "
          f"{r['length_m']} m, {len(r['points'])} points")


def expect_c17(label, argv, must, inject=None, attr=None, value=None):
    """A wrong gate layout FAILS C17 (and only C17: every older contract passes first); `must` are
    substrings of the failure message. `attr` / `value` patch one module constant for the build."""
    saved_v = getattr(ts, attr) if attr else None
    if attr:
        setattr(ts, attr, value)
    try:
        ts.build_checked(argv, inject=inject)
        check(False, f"T2n C17 {label}: build passed")
    except ts.ContractError as e:
        check(str(e).startswith("C17") and all(m_ in str(e) for m_ in must),
              f"T2n C17 {label}: fails with {str(e)[:230]}")
    finally:
        if attr:
            setattr(ts, attr, saved_v)


def t2_negative_c17(work=None):
    """Revision 1e: the 1d fence grid and four injected faults each FAIL C17 (gate span vs cabin door)."""
    B, T = ["--role", "base"], ["--role", "top"]
    # the shipped 1d grids: the base post at z -6.20 and the leaf 0.30 m off the door center, the top
    # post at -5.60 with its two leaves across the doorway (revision2-check-tram-cabin section C)
    expect_c17("the 1d base grid (post -6.20 inside the door span, leaf off centre)", B,
               ("inside the door span", "gatepost_0_1 z -6.24..-6.16", "gate_0_1 z -6.15..-4.25"),
               attr="GATES_AT_DOOR", value=False)
    expect_c17("the 1d top grid (post -5.60 at the seam, leaves across the doorway)", T,
               ("inside the door span", "gatepost_0_2 z -5.64..-5.56", "gate_0_1", "gate_0_2"),
               attr="GATES_AT_DOOR", value=False)
    expect_c17("a post injected in the span (z -5.50, -X outer edge)", B, ("edge 0", "gatepost_0_9"),
               inject=lambda k: k.box("gatepost_0_9", -7.90, -7.82, ts.DECK_Y, ts.DECK_Y + 1.25, -5.54, -5.46,
                                      "steel", drop=("-Y",)))
    expect_c17("a closed leaf across the door (z -6.40..-4.60, +X inner edge)", T, ("edge 2", "gate_2_9"),
               inject=lambda k: k.box("gate_2_9", 3.16, 3.20, ts.DECK_Y + 0.08, ts.DECK_Y + 1.20, -6.40, -4.60,
                                      "trim"))
    expect_c17("an opening wider than its leaves can close (GATE_OPEN_HALF 1.40: leaves 1.23 m < 1.40)", T,
               ("cannot close on the seam", "(1.23"), attr="GATE_OPEN_HALF", value=1.40)
    expect_c17("an opening under the minimum (min 2.00 against the built 1.90)", T,
               ("opening 1.900 m < 2.00",), attr="C17_OPEN_MIN", value=2.0)
    expect_c17("an opening 0.04 m off the door seam (GATE_DOOR_Z -5.46)", B, ("off the door seam -5.5",),
               attr="GATE_DOOR_Z", value=-5.46)
    # the door center is read from the cabin manifest: a manifest that hosts the cabin elsewhere fails
    tmp = tempfile.mkdtemp(prefix="c17_manifest_")
    try:
        with open(ts.CABIN_MANIFEST, encoding="utf-8") as fh:
            mf = json.load(fh)
        mf["prefab_numbers"]["hosted_poses"]["station"]["z_bay"] = -5.0
        bad = os.path.join(tmp, "SkiTramCabinA.manifest.json")
        with open(bad, "w", encoding="utf-8") as fh:
            json.dump(mf, fh)
        expect_c17("a cabin manifest that hosts the cabin at z_bay -5.0", B, ("z_bay -5.0",),
                   attr="CABIN_MANIFEST", value=bad)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    # the pure layout: the post count and the opening hold on both fences (nothing built)
    for role, want in (("base", (6, 4)), ("top", (6, 4))):
        posts, leaves = ts.gate_layout(ts.GATE_Z0[role])
        pa = ts.GATE_DOOR_Z - ts.GATE_OPEN_HALF - ts.GATE_POST_HALF
        pb = ts.GATE_DOOR_Z + ts.GATE_OPEN_HALF + ts.GATE_POST_HALF
        check((len(posts), len(leaves)) == want and any(abs(p - pa) < 1e-9 for p in posts)
              and any(abs(p - pb) < 1e-9 for p in posts)
              and all(b - a >= 1.0 for a, b in leaves) and max(b - a for a, b in leaves) <= 1.90 + 1e-9
              and abs(posts[0] - ts.GATE_Z0[role]) < 1e-9 and abs(posts[-1] - (ts.GATE_Z0[role] + ts.GATE_RUN)) < 1e-9,
              f"T2n C17 gate_layout {role}: {len(posts)} posts / {len(leaves)} leaves, ends {posts[0]} / {posts[-1]} "
              f"unchanged, opening posts at {pa:.2f} / {pb:.2f}, leaves {[round(b - a, 3) for a, b in leaves]}")


def t3_t4_t5(work):
    out = {}
    for role in ("base", "top"):
        od = os.path.join(work, "export")
        t0 = time.perf_counter()
        # base bakes with AO 4 so the recolor-level check below also sees baked AO (review
        # minor 6); top stays AO 0
        ao = "4" if role == "base" else "0"
        try:
            rep = ts.main(["--role", role, "--finish", "--lods", "--export", "--out", od,
                           "--atlas", "512", "--ao-samples", ao, "--weather", "off"])
        except Exception as e:  # noqa: BLE001
            check(False, f"T3 {role}: main raised {e!r}")
            continue
        out[role] = rep
        name = ts.NAMES[role]
        d = os.path.join(od, name)
        files = set(os.listdir(d))
        need = {f"{name}{s}" for s in (".fbx", "_LOD1.fbx", "_LOD2.fbx", "_Win.fbx",
                                       "_LOD1_Win.fbx", "_LOD2_Win.fbx", "_BaseColor.png",
                                       "_Normal.png", "_MaskMap.png", "_ControlMask.png",
                                       "_Emissive.png", ".manifest.json")} | {"settings.json"}
        check(need <= files, f"T3 {role}: folder has {sorted(need - files) or 'every'} file "
                             f"({time.perf_counter() - t0:.0f} s)")
        check(rep.get("validate", (1, 0))[0] == 0, f"T3 {role}: validate_asset_folder errors "
                                                   f"{rep.get('validate')}")
        tris = rep["tris"]
        check(tris[1] <= 0.5 * tris[0] and tris[2] <= min(ts.LOD2_CAP[role], ts.LOD2_CLI_CAP)
              and tris[0] <= ts.LOD0_CAP[role], f"T3 {role}: C9 budgets {tris}")
        check(rep["batches"] <= ts.BATCH_CAP, f"T3 {role}: batches {rep['batches']} <= "
                                              f"{ts.BATCH_CAP}")
        bc = cs2_validate.load_png_pixels(os.path.join(d, f"{name}_BaseColor.png"))
        check(float(bc[..., 3].min()) == 1.0, f"T3 {role}: BaseColor alpha 255 everywhere")
        ctl = cs2_validate.load_png_pixels(os.path.join(d, f"{name}_ControlMask.png"))
        em = cs2_validate.load_png_pixels(os.path.join(d, f"{name}_Emissive.png"))
        A = ctl.shape[0]
        # light albedo ships under every recolor mask (review minor 6): p95 0.78-0.82 (sRGB of
        # the 0.604 linear slot albedo), ControlMask R + G + B <= 1, A 0
        check(float(ctl[..., :3].sum(axis=-1).max()) <= 1.0 + 1e-3 and float(ctl[..., 3].max()) < 1e-3,
              f"T3 {role}: ControlMask R + G + B <= 1, A 0")
        lum = bc[..., :3].mean(axis=-1)
        # nitpick B2 (RE-PINNED for the RAMPS base only; build/nitpick-b2-tram-ramps.md): AO is multiplied into
        # BaseColor, so p95 needs >= 5 % of a slot's texels unoccluded. The base's slot 1 (steel) was at the edge
        # (5.53 % at 0.80: p95 0.780); the ramp adds shaded steel (columns under the decks, kick plates) and shades the
        # annex's +X frames (4.74 %: p95 0.769) while the slot albedo is unchanged (0.80). The base reads p99 (the
        # unoccluded albedo still ships under the mask: 0.804); every other build keeps p95
        q = 99 if (role == "base" and ts.RAMPS) else 95
        for ch, slot in enumerate("RGB"):
            under = lum[ctl[..., ch] > 0.5]
            if under.size < 50:
                continue
            pq = float(np.percentile(under, q))
            p95 = float(np.percentile(under, 95))
            check(0.78 <= pq <= 0.82, f"T3 {role} (AO {ao}): BaseColor p{q} under mask {slot} = "
                                      f"{pq:.3f} (0.78..0.82, {under.size} texels; p95 {p95:.3f})")
        for nm, rect in sorted(rep["stamps"].items()):
            if nm.startswith("ctface_") and rect:
                e = em[rect[0]:rect[1], rect[2]:rect[3], 0:3].max(axis=-1)
                check(e.max() > 0.5 and e.min() < 0.15,
                      f"T3 {role}: {nm} lit field with dark marks in _Emissive "
                      f"(max {e.max():.2f}, min {e.min():.2f})")
        for nm, rect in sorted(rep["stamps"].items()):
            if rect is None:
                check(False, f"T3 {role}: stamp {nm} has no UV rect")
                continue
            r0, r1, c0, c1 = rect
            b = ctl[r0:r1, c0:c1, 2]
            check(b.max() > 0.5, f"T3 {role}: ControlMask B under {nm} (max {b.max():.2f}, "
                                 f"{int((b > 0.5).sum())} texels @{A})")
        c13 = rep.get("C13_unique")
        want13 = {"base": {"pictodisc", "pictoplinth", "ctface_nx", "ctface_nz", "ctface_px", "ctface_pz",
                           "label_dep", "label_ops", "label_tickets"}
                          # nitpick B: LIFT TICKETS (annex +X face) and ENTRANCE (village doors)
                          | ({"label_lifttickets", "label_entrance"} if ts.JH_DETAILS else set()),
                  "top": {"label_stn", "label_tension", "label_trail_0", "label_trail_1", "label_trail_2"}}[role]
        check(c13 is not None and set(c13) == want13 and all(v <= 0.02 for v in c13.values()),
              f"T3 {role}: C13 painted labels upright on the shipped UVUnique layout {c13}")
        with open(os.path.join(d, f"{name}.manifest.json"), encoding="utf-8") as fh:
            man = json.load(fh)
        t4_manifest(role, man)
        t5_lods(role, rep)
        if role == "top" and ts.PLINTH_TOP:
            t9_export_above_grade(os.path.join(d, f"{name}.fbx"))
    return out


def t4_manifest(role, man):
    pc = man["prefab_contract"]
    consts = {"stub_tip_z": ts.STUB_Z, "face_z": ts.FACE_Z, "deck_y": ts.DECK_Y,
              "pivot_y": ts.PIVOT_Y, "track_y": ts.TRACK_Y, "haul_y": ts.HAUL_Y,
              "carriage_top_y": ts.CARRIAGE_TOP_Y, "hang_m": ts.HANG_M,
              "track_x": ts.TRACK_X, "haul_x": ts.HAUL_X}
    bad = {k_: (pc.get(k_), v) for k_, v in consts.items() if abs(pc.get(k_, 1e9) - v) > 1e-9}
    check(not bad, f"T4 {role}: prefab_contract constants {bad or 'match'}")
    check(abs(ts.CARRIAGE_TOP_Y - ts.PIVOT_Y - 10.50) < 1e-9 and
          abs(ts.TRACK_Y - ts.PIVOT_Y - 10.00) < 1e-9 and abs(ts.HAUL_Y - ts.TRACK_Y + 0.40) < 1e-9,
          f"T4 {role}: R27/R28 hang 10.50, rope 10.00 above pivot, haul -0.40")
    dx = ts.frame_dx(role)              # nitpick B2: the RAMPS base's prefab frame = design x - 4.00
    want = {(round(-5.5 + dx, 3), 16.0), (round(5.5 + dx, 3), 16.0), (round(-5.0 + dx, 3), 15.6),
            (round(5.0 + dx, 3), 15.6)}
    got = {(round(s["a"][0], 3), round(s["a"][1], 3)) for s in pc["stubs"]}
    zs = {s["a"][2] for s in pc["stubs"]} | {s["d"][2] - 0.25 for s in pc["stubs"]}
    check(got == want and all(abs(z - ts.STUB_Z) < 1e-9 for z in zs),
          f"T4 {role}: stubs a {sorted(got)} at z {ts.STUB_Z}, d = a + 0.25")
    lanes = {s["kind"]: s["lane"] for s in pc["stubs"]}
    check(lanes == {"track": "SkiTramTrackRope", "haul": "SkiTramHaulRope"} and
          pc["lanes"]["track"]["m_UtilityType"] == 64 and pc["lanes"]["haul"]["m_UtilityType"] == 8
          and pc["lanes"]["track"]["m_Hanging"] <= 0.005,
          f"T4 {role}: R28/R31 lanes track HV (64) / haul LV (8), hanging "
          f"{pc['lanes']['track']['m_Hanging']}")
    meas = pc["stubs_measured"]
    err = max(float(np.linalg.norm(np.array(m["ring_centre"]) - np.array(m["a"]))) for m in meas)
    check(err <= 0.001, f"T4 {role}: mesh rope ends on the stub tips (max err {err:.4f} m)")
    docks = pc["docks"]
    check(all(abs(d_["carriage_top"][1] - ts.CARRIAGE_TOP_Y) < 1e-9 and
              abs(d_["rope"][1] - ts.TRACK_Y) < 1e-9 and abs(d_["pivot"][2] - ts.DOCK_Z) < 1e-9
              for d_ in docks), f"T4 {role}: docks {docks}")
    hosted = pc["hosted"]
    if role == "base":
        cabs = [h for h in hosted if h["what"].startswith("SkiTramCabin")]
        ok = (len(cabs) == 2 and
              sorted(tuple(h["m_Position"]) for h in cabs) ==
              [(-5.5 + dx, ts.PIVOT_Y, ts.DOCK_Z), (5.5 + dx, ts.PIVOT_Y, ts.DOCK_Z)] and
              all(h["m_Rotation"] == [0.0, 0.0, 0.0, 1.0] and h["m_ParentMesh"] == 0 for h in cabs))
        check(ok, f"T4 base: hosted cabins identity, parent 0: "
                  f"{[(h['m_Position'], h['m_ParentMesh']) for h in cabs]}")
        pole = [h for h in hosted if h["what"].startswith("LV")]
        check(len(pole) == 1 and pole[0]["m_ParentMesh"] == -1, "T4 base: LV pole parent -1")
    else:
        check(hosted == [], "T4 top: hosts nothing")
    if role == "top" and ts.PLINTH_TOP:
        # revision 3: plinth mode - no engine base by design; the plinth rule on every LOD
        pl = pc.get("plinth") or {}
        per = pl.get("per_lod") or {}
        check(pc["engine_base"]["verdict"] == "NONE" and pc["engine_base"].get("mode") == "plinth"
              and pc["engine_base"]["ok_edges"] == 0 and pc.get("base_mode") == "plinth"
              and set(per) == {"LOD0", "LOD1", "LOD2"}
              and all(abs(v["min_y"] + ts.PLINTH["depth"]) <= 0.01 and v["band_vertices"] == 0 and v["ok_edges"] == 0
                      for v in per.values())
              and (pl.get("surgery") or {}).get("LOD0 RenderPrefab BaseProperties") == {"m_BaseType": None,
                                                                                         "m_UseMinBounds": False},
              f"T4 top: engine base {pc['engine_base']} (plinth mode); per LOD {per}; surgery {pl.get('surgery')}")
        r3 = man.get("plinth_top") or {}
        cl = (r3.get("clearances") or {}).get("wheels") or {}
        check(bool(r3) and r3.get("mode") == "plinth" and set(r3.get("C6p") or {}) == {"LOD0", "LOD1", "LOD2"}
              and not ((r3.get("C6p") or {}).get("LOD0", {}).get("closed") or {}).get("uncovered", ["?"])
              and abs(cl.get("drumrim_R", {}).get("rim_low_y", 0.0) - 0.75) < 0.01
              and cl.get("drumrim_R", {}).get("to_found_block_m", 0.0) >= 0.79,
              f"T4 top: manifest plinth_top block: C6p {sorted(r3.get('C6p') or {})}, tris {r3.get('tris_lod0')}, the lowest "
              f"wheel rim (anchor drum) {cl.get('drumrim_R')}")
    else:
        check(pc["engine_base"]["verdict"] == "OK" and pc["engine_base"]["blocked_edges"] == 0,
              f"T4 {role}: engine base {pc['engine_base']}")
    c17 = man.get("C17") or {}
    ed = c17.get("edges") or {}
    check(len(ed) == 4 and abs(c17.get("door", {}).get("centre", 9.0) - ts.DOCK_Z) < 1e-9
          and abs(c17.get("door", {}).get("half", 0.0) - 0.85) < 1e-9
          and all(e_["opening_m"] >= 1.70 and abs(e_["centre_z"] - ts.DOCK_Z) <= 0.02 for e_ in ed.values()),
          f"T4 {role}: manifest C17 gate span vs door: {len(ed)} edges, openings "
          f"{sorted({e_['opening_m'] for e_ in ed.values()})}, door span {c17.get('span_z')}")
    # revision 2: every frozen prefab number == the shipped 1e manifest (archived before the re-export); the
    # top's bounds max y is the one re-pin (ruling Q3: 27.00 -> MAST_TOP, the 8 m mast dropped)
    name = ts.NAMES[role]
    p1e = os.path.join(REPO_1E, name, f"{name}.manifest.json")
    if not os.path.isfile(p1e):
        check(False, f"T4 {role}: the 1e manifest {p1e} (archive) is missing")
    else:
        with open(p1e, encoding="utf-8") as fh:
            old = json.load(fh)["prefab_contract"]
        frozen = ("prefab", "lot", "stub_tip_z", "face_z", "deck_y", "pivot_y", "track_y", "haul_y",
                  "carriage_top_y", "hang_m", "rope_below_carriage_top_m", "track_x", "haul_x", "lanes", "stubs",
                  "docks", "hosted", "terraform")
        b, b0 = pc["bounds"], old["bounds"]
        want_min = list(b0["min"])
        want_max = list(b0["max"]) if role == "base" else [b0["max"][0], round(ts.MAST_TOP, 3), b0["max"][2]]
        if role == "top" and ts.PLINTH_TOP:
            # revision 3 (RE-PINNED for the top ONLY): plinth mode - the foundation reaches -PLINTH depth (the prefab delta,
            # bounds min y 0 -> -12.00); every other frozen number is the 1e manifest's
            want_min = [b0["min"][0], -round(ts.PLINTH["depth"], 3), b0["min"][2]]
        if dx:
            # nitpick B2 (RE-PINNED by the lot shift only): the 1e numbers re-expressed in the shifted frame (every x +
            # dx, the relative geometry identical), the lot 6 x 3; bounds min x + dx, max x = the new cell's paving edge
            old = _reexpress(old, dx)
            old["lot"] = list(ts.LOT_RAMPS[role])
            want_min = [round(b0["min"][0] + dx, 3)] + list(b0["min"][1:])
            want_max = [round(ts.NB2_PAVE["x1"] + dx, 3)] + list(b0["max"][1:])
        diff = [k_ for k_ in frozen if pc.get(k_) != old.get(k_)]
        check(not diff and b["min"] == want_min and b["max"] == want_max,
              f"T4 {role}: frozen prefab numbers == the shipped 1e manifest ({len(frozen)} keys{', differ ' + str(diff) if diff else ''}); "
              f"bounds {b} (1e {b0}{'; top max y re-pinned 27.00 -> ' + str(round(ts.MAST_TOP, 2)) + ' by ruling Q3' if role == 'top' else ''}"
              f"{'; top min y re-pinned 0.00 -> -12.00 by revision 3 (plinth mode)' if role == 'top' and ts.PLINTH_TOP else ''})")
    r2 = man.get("rev2") or {}
    c18 = r2.get("C18") or {}
    ok18 = set(c18) == {"LOD0", "LOD1", "LOD2"} and all(
        all(r_["back"] <= c18[L]["max_per_part"] for r_ in c18[L]["parts"]
            if r_["part"] not in {k_["part"] for k_ in c18[L].get("known", [])}) for L in c18)
    check(ok18 and not (r2.get("C19") or {}).get("fail", ["?"]) and (r2.get("C20") or {}).get("code") in ("C20", "C20b"),
          f"T4 {role}: manifest rev2: C18 {({L: c18[L]['back'] for L in c18})} back-face hits per LOD, C19 failing "
          f"{(r2.get('C19') or {}).get('fail')}, C20 {(r2.get('C20') or {}).get('code')}, haul {r2.get('haul_route')}")
    rows = ((man.get("detail1") or {}).get("sub_objects") or {}).get("rows") or []
    nv = {"base": 15 + (3 if ts.RAMPS else 0), "top": 13}[role]          # nitpick B2: + 2 park lamps and a bin
    check(len(rows) == nv and (man.get("fine_detail") or {}).get("rows") == {"vanilla": nv,
                                                                             "mod": {"base": 4, "top": 3}[role]},
          f"T4 {role}: {len(rows)} vanilla sub-object rows in the manifest (fine-detail step 3c: + the annex wall light on the base) + kit rows {(man.get('fine_detail') or {}).get('rows')}")
    if role == "base" and ts.ANNEX_STAIR:
        # nitpick B3: the manifest carries the annex stair block, the C15 route (grade -> ... -> annexcap -> landing_annex ->
        # deck_outerR) and the `annex` flight; the frozen numbers above are untouched by it (the stair is inside the lot)
        b3, c15m = man.get("nitpick_b3") or {}, man.get("C15") or {}
        rt, pl = b3.get("route") or {}, b3.get("plan") or {}
        check(bool(b3) and rt.get("ok") is True and (c15m.get("route") or {}).get("ok") is True
              and rt.get("chain", [])[-3:] == ["annexcap", "landing_annex", "deck_outerR"] and rt["chain"][0] == "GRADE"
              and "annex" in c15m.get("flights", {}) and "annexcap" not in c15m.get("not_walkable", {})
              and pl.get("risers") == 7 and abs(pl.get("rise_total", 0) - 1.10) < 1e-9 and pl.get("width", 0) >= 1.20
              and b3.get("tris_lod0_total", 0) > 0 and "annexback_" in "".join(b3.get("tris_lod0", {})),
              f"T4 {role}: manifest nitpick_b3: route {' -> '.join(rt.get('chain') or ['NONE'])}, stair {pl.get('risers')} risers of "
              f"{pl.get('riser')} m, {pl.get('width')} m wide, {b3.get('tris_lod0_total')} LOD0 tris {b3.get('tris_lod0')}")


def _reexpress(old, dx):
    """Nitpick B2: a 1e prefab_contract with every lateral (x) position moved by dx - the stubs' a / b / c / d, the docks,
    the hosted cabins and the LV pole (rope, carriage top, pivot, m_Position): the relative geometry unchanged."""
    import copy
    o = copy.deepcopy(old)
    for s_ in o.get("stubs", []):
        for k_ in ("a", "b", "c", "d"):
            s_[k_] = [s_[k_][0] + dx] + list(s_[k_][1:])
    for d_ in o.get("docks", []):
        for k_ in ("carriage_top", "rope", "pivot"):
            d_[k_] = [d_[k_][0] + dx] + list(d_[k_][1:])
    for h in o.get("hosted", []):
        for k_ in ("m_Position", "carriage_top", "rope"):
            if k_ in h:
                h[k_] = [h[k_][0] + dx] + list(h[k_][1:])
    return o


def t5_lods(role, rep):
    lp = rep.get("lod_parts", {})
    l1, l2 = lp.get(1, []), lp.get(2, [])
    gone1 = [n for n in l1 if n.startswith(ts.LOD1_DELETE)]
    gone2 = [n for n in l2 if n.startswith(ts.LOD2_DELETE[role])]
    check(not gone1, f"T5 {role}: LOD1 delete list gone {gone1[:6] or ''}")
    check(not gone2, f"T5 {role}: LOD2 delete list gone {gone2[:6] or ''}")
    missing = [p for p in ts.LOD2_KEEP[role] if not any(n.startswith(p) for n in l2)]
    check(not missing, f"T5 {role}: LOD2 keeps {list(ts.LOD2_KEEP[role])} "
                       f"{'(missing ' + str(missing) + ')' if missing else ''}")


TSPACE_IDS = (b"Tangents", b"Binormals")


def fbx_equal_except_tspace(pa, pb, tol=1e-6):
    """(equal, note): every FBX element of the two files identical, except the tangent /
    binormal arrays, which may differ by float noise <= tol. Revision 1: the export's mikktspace
    pass (cs2_asset.export_fbx use_tspace=True) leaves ulp-level noise (max 1.8e-7) on the 12.8k
    tri top LOD0 between two PLAIN builds in one process - not a --review side effect; vertices,
    faces, UVs, normals and materials stay bit-identical."""
    import io_scene_fbx.parse_fbx as pf

    def flat(el, path, out):
        key = path + "/" + el.id.decode(errors="replace")
        out.append((key, el.id, el.props))
        for i, c in enumerate(el.elems):
            flat(c, f"{key}[{i}]", out)
    trees = []
    for p in (pa, pb):
        out = []
        for i, e in enumerate(pf.parse(p)[0].elems):
            flat(e, f"[{i}]", out)
        trees.append(out)
    if len(trees[0]) != len(trees[1]):
        return False, "element counts differ"
    worst = 0.0
    for (ka, ida, pa_), (kb, _idb, pb_) in zip(*trees):
        if ka != kb or len(pa_) != len(pb_):
            return False, f"structure differs at {ka}"
        for x, y in zip(pa_, pb_):
            if hasattr(x, "tobytes") or hasattr(x, "typecode"):
                ax, ay = np.asarray(x, dtype=np.float64), np.asarray(y, dtype=np.float64)
                if ax.shape != ay.shape:
                    return False, f"array shape differs at {ka}"
                if ida in TSPACE_IDS:
                    worst = max(worst, float(np.abs(ax - ay).max()) if ax.size else 0.0)
                    if worst > tol:
                        return False, f"{ka} differs by {worst:.2e}"
                elif not np.array_equal(ax, ay):
                    return False, f"{ka} differs"
            elif x != y and ka.split("/")[-1] not in ("CreationTime", "FileId"):
                return False, f"{ka} differs"
    return True, f"tangent / binormal noise {worst:.1e}"


def t6_review(work):
    freeze_fbx_clock()
    name = ts.NAMES["top"]
    runs, dirs = {}, {}
    for tag, extra in (("plain", []), ("review", ["--review", os.path.join(work, "review")])):
        od = os.path.join(work, f"rv_{tag}")
        ts.main(["--role", "top", "--finish", "--lods", "--export", "--out", od, "--atlas", "512",
                 "--ao-samples", "0"] + extra)
        dirs[tag] = os.path.join(od, name)
        runs[tag] = file_hashes(dirs[tag])
    skip = {f"{name}_LOD1.fbx", f"{name}_LOD2.fbx", f"{name}.manifest.json"}
    a, b = runs["plain"], runs["review"]
    check(sorted(a) == sorted(b), f"T6 file sets equal ({len(a)} files)")
    diff, notes = [], []
    for f in sorted(a):
        if f in skip or a[f] == b.get(f):
            continue
        if f.endswith(".fbx"):
            ok, note = fbx_equal_except_tspace(os.path.join(dirs["plain"], f),
                                               os.path.join(dirs["review"], f))
            notes.append(f"{f}: {note}")
            if ok:
                continue
        diff.append(f)
    check(not diff, f"T6 --review byte-identical except {sorted(skip)} (FBX tangent noise "
                    f"<= 1e-6 allowed{': ' + '; '.join(notes) if notes else ''}): "
                    f"{'differs ' + str(diff) if diff else 'all equal'}")
    pack = os.path.join(work, "review", f"{name}.blend")
    check(os.path.isfile(pack), f"T6 review pack written ({os.path.getsize(pack) if os.path.isfile(pack) else 0} bytes)")


CABIN_FBX = os.path.join(os.path.dirname(os.path.dirname(LIB)), "assets", "import", "cabins", "A",
                         "SkiTramCabinA", "SkiTramCabinA.fbx")
CELL = 0.02


def cabin_sweep_mask(fbx):
    """Rasterize the exported cabin (raw FBX cm = prefab-local) onto its X-Y cross-section
    (2 cm cells); each cell keeps the largest travel coordinate Z covering it, i.e. how far
    ahead of the cabin center that part of the silhouette reaches."""
    objs = cs2_validate.import_fbx_temp(fbx)
    try:
        o = [x for x in objs if x.type == "MESH"][0]
        me = o.data
        co = np.empty(len(me.vertices) * 3, np.float32)
        me.vertices.foreach_get("co", co)
        co = co.reshape(-1, 3).astype(np.float64) / 100.0
        tris = []
        for p in me.polygons:
            vs = list(p.vertices)
            for j in range(1, len(vs) - 1):
                tris.append((vs[0], vs[j], vs[j + 1]))
        T = co[np.array(tris)]
    finally:
        cs2_validate.remove_objects(objs)
    x0, y0 = T[..., 0].min() - CELL, T[..., 1].min() - CELL
    nx = int((T[..., 0].max() - x0) / CELL) + 3
    ny = int((T[..., 1].max() - y0) / CELL) + 3
    zmax = np.full((ny, nx), -np.inf)
    for tri in T:
        a, b, c = tri[:, :2]
        lo = np.floor((np.minimum(np.minimum(a, b), c) - (x0, y0)) / CELL).astype(int)
        hi = np.ceil((np.maximum(np.maximum(a, b), c) - (x0, y0)) / CELL).astype(int)
        gx, gy = np.meshgrid(np.arange(lo[0], hi[0] + 1), np.arange(lo[1], hi[1] + 1))
        px, py = x0 + (gx + 0.5) * CELL, y0 + (gy + 0.5) * CELL
        d = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
        if abs(d) < 1e-12:
            continue
        l1 = ((b[1] - c[1]) * (px - c[0]) + (c[0] - b[0]) * (py - c[1])) / d
        l2 = ((c[1] - a[1]) * (px - c[0]) + (a[0] - c[0]) * (py - c[1])) / d
        tol = 0.5 * CELL / max(np.linalg.norm(b - a), np.linalg.norm(c - b), 1e-6) + 1e-3
        inside = (l1 >= -tol) & (l2 >= -tol) & (1 - l1 - l2 >= -tol)
        gyi, gxi = gy[inside], gx[inside]
        ok = (gyi >= 0) & (gyi < ny) & (gxi >= 0) & (gxi < nx)
        zmax[gyi[ok], gxi[ok]] = np.maximum(zmax[gyi[ok], gxi[ok]], tri[:, 2].max())
    return zmax, x0, y0, (T[..., 0].min(), T[..., 0].max(), T[..., 2].min(), T[..., 2].max())


def t8_cabin_sweep():
    """The real exported SkiTramCabinA, hosted identity in each bay (R29), swept from the line
    (z < -12) to its dock: no non-rope station triangle may enter its silhouette. Skips when
    the cabin lens has not exported the cabin."""
    if not os.path.isfile(CABIN_FBX):
        print(f"[tram-test] SKIP T8: {CABIN_FBX} not exported")
        return
    zmax, x0, y0, ext = cabin_sweep_mask(CABIN_FBX)
    ny, nx = zmax.shape
    print(f"[tram-test] T8 cabin cross-section x [{ext[0]:.2f}, {ext[1]:.2f}], travel "
          f"[{ext[2]:.2f}, {ext[3]:.2f}] m, {int(np.isfinite(zmax).sum())} cells of {CELL} m")
    import cs2_kitrows as KR
    for argv in (["--role", "base"], ["--role", "top"], ["--role", "top", "--no-wheels"]):
        t, _win, _tb, _r = ts.build_checked(argv)
        tri, names = ts.triangles(t)
        # fine-detail step 3c: the dressing-kit rows' REAL meshes (placed by their rows, prefab = game frame) are
        # swept against too - the kit props stand where the modeled dressing stood
        kit = ts.fd_kit_rows(argv[1])
        if kit:
            kt = [KR.host_mesh(r, lambda G: np.asarray(G, dtype=np.float64)) for r in kit]
            tri = np.concatenate([tri] + [A[np.array(T_)] for A, T_, _k in kt])
            names = list(names) + [f"kitrow_{i:02d}_{r['prefab']}" for i, (r, (_A, T_, _k)) in enumerate(zip(kit, kt))
                                   for _ in range(len(T_))]
        nm = np.array(names)
        keep = ~np.char.startswith(nm, "rope_")
        tri, nm = tri[keep], nm[keep]
        hits = {}
        for s in (-1, 1):
            xl = s * ts.TRACK_X + ts.frame_dx(argv[1])          # nitpick B2: the RAMPS base's docks at x - 4.00
            sel = ((tri[..., 0].max(axis=1) >= xl + ext[0] - 0.05) &
                   (tri[..., 0].min(axis=1) <= xl + ext[1] + 0.05) &
                   (tri[..., 2].min(axis=1) <= ts.DOCK_Z + ext[3] + 0.05) &
                   (tri[..., 1].max(axis=1) >= ts.PIVOT_Y - 0.1))
            for tr, n in zip(tri[sel], nm[sel]):
                L = max(np.linalg.norm(tr[1] - tr[0]), np.linalg.norm(tr[2] - tr[1]),
                        np.linalg.norm(tr[0] - tr[2]))
                k = int(min(150, max(2, np.ceil(L / CELL))))
                u, v = np.meshgrid(np.linspace(0, 1, k + 1), np.linspace(0, 1, k + 1))
                m = (u + v) <= 1.0
                pts = tr[0] + np.outer(u[m], tr[1] - tr[0]) + np.outer(v[m], tr[2] - tr[0])
                gx = np.floor((pts[:, 0] - xl - x0) / CELL).astype(int)
                gy = np.floor((pts[:, 1] - ts.PIVOT_Y - y0) / CELL).astype(int)
                ok = (gx >= 0) & (gx < nx) & (gy >= 0) & (gy < ny)
                if not ok.any():
                    continue
                zc = zmax[gy[ok], gx[ok]]
                bad = np.isfinite(zc) & (pts[ok, 2] <= ts.DOCK_Z + zc)
                if bad.any():
                    hits[n] = hits.get(n, 0) + int(bad.sum())
        check(not hits, f"T8 {' '.join(argv)}: real SkiTramCabinA swept to both docks clears "
                        f"the station{'' if not hits else ' - HITS ' + str(sorted(hits.items())[:8])}")


# --------------------------------------------------------------------------------------
# T9 (revision 3, PLINTH_TOP; build/tram-top-rev3.md)
# --------------------------------------------------------------------------------------
R3_ARCHIVE_FBX = os.path.join(os.path.dirname(os.path.dirname(LIB)), "assets", "archive", "2026-10-11-13", "import",
                              "superseded-tramtop-rev3-2026-10-13", "stations", "tram", "A", "SkiTramTopStationA",
                              "SkiTramTopStationA.fbx")
ABOVE_Y, ABOVE_TOL = 0.01, 1e-4            # "above grade" and the vertex match tolerance (0.1 mm)


def _above(P, thr=ABOVE_Y):
    return np.unique(np.round(P[P[:, 1] > thr], 5), axis=0)


def _unmatched(P, Q, tol=ABOVE_TOL):
    """Rows of P with no row of Q within tol (KD-tree)."""
    from mathutils.kdtree import KDTree
    if not len(Q):
        return list(P)
    kd = KDTree(len(Q))
    for i, q in enumerate(Q):
        kd.insert(q.tolist(), i)
    kd.balance()
    return [p for p in P if kd.find(p.tolist())[2] > tol]


def _fbx_points(path):
    """Prefab-frame vertex positions of an exported FBX (raw cm = prefab-local, as cabin_sweep_mask reads it)."""
    objs = cs2_validate.import_fbx_temp(path)
    try:
        out = []
        for o in objs:
            if o.type != "MESH":
                continue
            co = np.empty(len(o.data.vertices) * 3, np.float32)
            o.data.vertices.foreach_get("co", co)
            out.append(co.reshape(-1, 3).astype(np.float64) / 100.0)
        return np.concatenate(out)
    finally:
        cs2_validate.remove_objects(objs)


def t9_export_above_grade(fbx):
    """T9 on the export: the exported plinth-mode LOD0 vs the archived shipped top's LOD0 (the same finish geometry: bevels
    included), above y 0.01 vertex for vertex. Skipped when the archive (gitignored) is absent."""
    if not os.path.isfile(R3_ARCHIVE_FBX):
        print(f"[tram-test] SKIP T9 export: {R3_ARCHIVE_FBX} absent")
        return
    A0, A1 = _above(_fbx_points(R3_ARCHIVE_FBX)), _above(_fbx_points(fbx))
    oa, ob = _unmatched(A0, A1), _unmatched(A1, A0)
    check(not oa and not ob, f"T9 export: the exported top LOD0 above y {ABOVE_Y} == the archived shipped top's "
                             f"({len(A0)} / {len(A1)} unique vertices; shipped-only {len(oa)}, new-only {len(ob)}, "
                             f"tol {ABOVE_TOL * 1000:.1f} mm)")


def _below_rays(t):
    """Revision 3: rays from 1 m under the plinth floor straight up (a 0.5 m grid over the lot) and horizontally from 2 m
    outside each lot side into the below-grade volume (every 1 m down to -11.5, 1 m apart): {part: back-face first hits},
    rays, hits. A back face first = an open part seen from below / the side (what a cliff edge exposes)."""
    from mathutils import Vector
    from mathutils.bvhtree import BVHTree
    tri, names = ts.triangles(t)
    nm = np.array(names)
    bvh = BVHTree.FromPolygons(tri.reshape(-1, 3).tolist(), [(3 * i, 3 * i + 1, 3 * i + 2) for i in range(len(tri))],
                               all_triangles=True)
    n = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0])
    D = ts.PLINTH["depth"]
    rays = [((x, -D - 1.0, z), (0.0, 1.0, 0.0)) for x in np.arange(-11.75, 11.76, 0.5)
            for z in np.arange(-11.75, 11.76, 0.5)]
    for y in np.arange(-0.5, -D + 0.4, -1.0):
        for c in np.arange(-11.5, 11.6, 1.0):
            rays += [((14.0, y, c), (-1.0, 0.0, 0.0)), ((-14.0, y, c), (1.0, 0.0, 0.0)),
                     ((c, y, -14.0), (0.0, 0.0, 1.0)), ((c, y, 14.0), (0.0, 0.0, -1.0))]
    back, hits = {}, 0
    for o, d in rays:
        hit, _nrm, idx, _dist = bvh.ray_cast(Vector(o), Vector(d))
        if hit is None:
            continue
        hits += 1
        if float(np.dot(n[idx], d)) > 0.0:
            back[str(nm[idx])] = back.get(str(nm[idx]), 0) + 1
    return back, len(rays), hits


def t9_plinth():
    """T9 (revision 3; module docstring): above grade nothing moved, the plinth closed and deep, the bounds, the negatives."""
    if not ts.PLINTH_TOP:
        print("[tram-test] SKIP T9: PLINTH_TOP off")
        return
    T = ["--role", "top"]
    # T9a: the plinth-mode blockout vs the PLINTH_TOP False blockout (the shipped nitpick-B2 top), in process
    builds = {}
    for flag in (False, True):
        saved = ts.PLINTH_TOP
        ts.PLINTH_TOP = flag
        try:
            args = ts.parse_args(T)
            ts.clear_scene()
            t, _tb, _s, _m = ts.assemble("top", args, False)
            pf, parts = ts._part_tris(t)
            builds[flag] = (pf.copy(), {n_: np.unique(v.ravel()) for n_, v in parts.items()})
        finally:
            ts.PLINTH_TOP = saved
    (P0, parts0), (P1, parts1) = builds[False], builds[True]
    A0, A1 = _above(P0), _above(P1)
    oa, ob = _unmatched(A0, A1), _unmatched(A1, A0)
    check(not oa and not ob, f"T9a above grade: every vertex above y {ABOVE_Y} identical to the shipped top's "
                             f"({len(A0)} / {len(A1)} unique; shipped-only {len(oa)}, new-only {len(ob)}, tol 0.1 mm)")
    missing = sorted(set(parts0) - set(parts1))
    new = sorted(set(parts1) - set(parts0))
    changed = sorted(n_ for n_ in parts0 if n_ in parts1 and (
        len(parts0[n_]) != len(parts1[n_]) or _unmatched(P0[parts0[n_]], P1[parts1[n_]], 1e-4)))
    new_low = {n_: round(float(P1[parts1[n_]][:, 1].max()), 3) for n_ in new}
    check(not missing and all(n_.startswith(ts.R3_CONTINUED) for n_ in changed)
          and all(n_.startswith(("found_", "lat")) for n_ in new) and max(new_low.values()) < -ts.PLINTH["band"]
          and {n_.split("_")[0] for n_ in changed} == {"plinth", "footing", "pad", "rampcol", "stairpost"},
          f"T9a parts: none missing {missing}; changed (they now continue below grade) {changed}; {len(new)} new parts, "
          f"all below grade (highest {max(new_low.values()):.3f}): {sorted({n_.split('_')[0] for n_ in new})}")
    # T9b: the plinth on the contract build (C6p) + the independent ray test (and its self-check on the shipped top)
    t, _win, _tb, rep = ts.build_checked(T)
    c6 = rep["C6p"]["LOD0"]
    pf, _part, _table = ts.mesh_arrays(t)
    y = pf[:, 1]
    check(c6["verdict"] == "NONE" and c6["ok_edges"] == 0 and c6["band_vertices"] == 0
          and abs(float(y.min()) + ts.PLINTH["depth"]) < 1e-3 and not (np.abs(y) <= ts.PLINTH["band"]).any()
          and not c6["closed"]["uncovered"] and "found_slab" in c6["closed"]["parts_below_grade"],
          f"T9b C6p: verdict {c6['verdict']}, {c6['ok_edges']} OK edges, {c6['band_vertices']} band vertices, min y "
          f"{float(y.min()):.3f}, open edges below grade all seated ({len(c6['closed']['open_edges_below_grade_m'])} "
          f"parts: {sorted(c6['closed']['open_edges_below_grade_m'])})")
    back, nr, nh = _below_rays(t)
    check(not back and nh > 0.6 * nr, f"T9b rays from below / the sides into the plinth: {nr} rays, {nh} hits, "
                                      f"back-face first hits {back or 'none'}")
    saved = ts.PLINTH_TOP
    ts.PLINTH_TOP = False
    try:
        t0, _w0, _t0b, rep0 = ts.build_checked(T)
        back0, _n0, _h0 = _below_rays(t0)
        check(bool(back0) and {k_.split("_")[0] for k_ in back0} <= {"pad", "footing"}
              and rep0.get("C6p") is None and "C7" in rep0,
              f"T9b self-check: the shipped (PLINTH_TOP False, engine base) top passes C6 and the same rays find its "
              f"open bottoms {sorted(back0.items())}")
    finally:
        ts.PLINTH_TOP = saved
    # T9c: only min y moved
    b = rep["bounds"]
    check(b == {"min": [-11.8, -round(ts.PLINTH["depth"], 3), -12.0], "max": [11.8, round(ts.MAST_TOP, 3), 11.6]},
          f"T9c bounds {b}: x +-11.80, z -12.00..11.60, top {ts.MAST_TOP} (frozen), min y -{ts.PLINTH['depth']:.2f} "
          f"(the prefab delta)")
    # T9n: wrong plinths FAIL C6p
    expect_fail("C6p a part on the grade line (band vertices)", "C6p", T,
                lambda k: k.box("probe_band", -1.0, 0.0, 0.0, 1.0, 8.0, 9.0, "concrete"))
    expect_fail("C6p a part below the plinth floor", "C6p", T,
                lambda k: k.box("probe_deep", 0.0, 1.0, -12.50, -11.0, -6.0, -5.0, "concrete"))
    expect_fail("C6p an open-bottom box floating below grade (not closed)", "C6p", T,
                lambda k: k.prism("probe_open", [(-1.0, -6.0, -6.0), (0.0, -6.0, -6.0), (0.0, -6.0, -5.0),
                                                 (-1.0, -6.0, -5.0)],
                                  [(-1.0, -4.0, -6.0), (0.0, -4.0, -6.0), (0.0, -4.0, -5.0), (-1.0, -4.0, -5.0)],
                                  "concrete", open_bottom=True))
    saved_fn = ts.build_top_foundation
    ts.build_top_foundation = lambda k: k.box("found_block", -11.70, 11.70, -11.40, ts.PLINTH["seat"], -2.85, 11.50,
                                              "concrete")
    try:
        ts.build_checked(T)
        check(False, "T9n the foundation stopped short of the plinth depth (no slab, no bents): build passed")
    except ts.ContractError as e:
        check(str(e).startswith("C6p") and "min y" in str(e),
              f"T9n the foundation stopped short of the plinth depth: fails with {str(e)[:160]}")
    finally:
        ts.build_top_foundation = saved_fn


def t7_skirt():
    try:
        ts.parse_args(["--role", "top", "--skirt", "6"])
        check(False, "T7 --skirt without --skirt-ack-probe accepted")
    except SystemExit as e:
        check(e.code not in (0, None), f"T7 --skirt without ack exits {e.code}")


def main():
    t0 = time.perf_counter()
    work = tempfile.mkdtemp(prefix="tram_station_test_")
    try:
        t1_determinism()
        t2_contracts()
        t2_negative()
        t9_plinth()
        t7_skirt()
        t8_cabin_sweep()
        t3_t4_t5(work)
        t6_review(work)
    finally:
        shutil.rmtree(work, ignore_errors=True)
    dt = time.perf_counter() - t0
    if FAILS:
        print(f"FAIL test_tram_station_determinism: {len(FAILS)} failure(s) in {dt:.0f} s")
        for f in FAILS:
            print(f"  - {f}")
        sys.exit(1)
    print(f"PASS test_tram_station_determinism: determinism, contracts C1-C20 + R30 (positive "
          f"and injected), export, manifest, LOD lists, --review side effects ({dt:.0f} s)")


main()
