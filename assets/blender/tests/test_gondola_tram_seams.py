"""
test_gondola_tram_seams.py - cross-generator seams of the gondola + tram family (integration
2026-10-11, docs/research/art/assets-2026-10/build/gondola-tram-integration.md).

lift_cabin.py, tram_tower.py and tram_station.py each assert their own contracts. This test
asserts the numbers one asset hands to another, and builds nothing (it imports the three shipped
cabin FBXs per LOD; about 2 s):

  1. CODE: the generators' constants agree with each other and with the rulings R27-R31.
  2. EXPORTS: the shipped manifests under assets/import (cabins/A, tramtowers/A, towers/O lattice,
     stations/tram/A) agree with each other and with the code; the shipped cabin MESHES fit the
     carrier envelopes the towers were checked with. All ten ship, so a missing manifest or FBX
     FAILS (fixer 2026-10-11, review note 9); `-- --allow-missing` turns that back into a note
     for pre-export runs.

Seams (S#):
  S1 tram hang chain (R27): hang 10.50, rope = pivot + 10.00, haul 0.40 below the rope, in the
     cabin, the tower and the stations.
  S2 tram lanes (R28): track HV x +-5.5, haul x +-5.0; cabin haul grips at dy = track - haul.
  S3 R30: carriage half-width 1.10; the cabin's keep-outs k1 / k2 == the tower's R30 bands.
  S4 drawn rope radii (HV 0.100, LV 0.060) in the cabin wheel seat and the tower saddles.
  S5 tram envelope: every vertex of the shipped SkiTramCabinA LOD0/1/2 lies inside the union of
     tram_tower.CARRIER_TRAM (lane frame, both lanes, dz = y - 10.00).
  S6 gondola envelope: every vertex of the shipped SkiGondolaCabin8A LOD0/1/2 lies inside the
     union of tram_tower.CARRIER_GD in BOTH pin frames (rope point on the lane; grip top on the
     lane = what ChairPinSystem does today, cabin manifest clearance note).
  S7 gondola line: cabin gd8 lane / hang / axle == tram_tower GD (H_top 2.0 on the GD12 spec
     top 12.0 -> axle 10.0); NetSubObjects y = axle + grip-above-rope + 0.03 = 10.11.
  S8 lane prefab identity: FindAnchors pairs a stub with a lane only by prefab (PHASE3 10.3), so
     the tram tower stubs name the same lane prefabs (and fallbacks) as the tram station stubs;
     the gondola HV stubs name SkiGondolaHaulRope (round-2-contract 10.1).
  S9 manifests: tower datum / stubs / UtilityObject; station levels, stubs, docks and hosted
     poses (base only, identity, m_ParentMesh 0; top hosts none); cabin contract rows; the
     cabin's hosted-pose formula (pivot = deck - 0.30) == the stations' deck / pivot.
  S10 lift_terminal_detach --gondola's carrier envelope (GD_ENV: per height band, off-center
     OUTBOARD by the 6th field) encloses every vertex of the shipped SkiGondolaCabin8A
     LOD0/1/2. A FAIL once stations/gondola/A is exported (a NOTE before).
  S12 gondola stations (draft 1, 2026-10-11) vs cabin, towers and catalog: the shipped
     stations/gondola/A manifests carry gauge 6.0 (= cabin gd8 lane 3.0 = GD towers), lane /
     rope y 4.85 = deck 0.30 + step-in 0.05 + hang 4.50 (carriers.json gd8), wheel (0, 4.85,
     9.40 / 5.40) and stub tip -16 == the SkiCatalog.cs station rows, HV anchors at x +-3.0
     naming SkiGondolaHaulRope (== tram_tower.STUB_LANES gd_haul), the line rows y 10.11 ==
     the cabin manifest, the hosted entry-tower pose (0, 0, -11) 180 deg / rope 4.85 on the
     base only (the tower is NOT built yet: towers-transfer audit G2), and the build's real-
     cabin sweep (G8) >= 0.30 m made with the CURRENT shipped cabin FBX (sha1).
  S11 catalog: the carriers.json gd8 / tram rows carry lift_cabin.CARRIERS' CONTRACT FIELDS
     (lift_cabin.CONTRACT_FIELDS: hang, grip-above-rope, lane, axle, haul x / dy, bounds,
     carriage half-width, lane hanging; numbers within 1e-6). Catalog-owned keys (line_prefab,
     lift_type, owner, seats, speed, spacing) are the catalog owner's (round-2-contract 10.2) and
     never compared (fixer 2026-10-11, review finding 1). lift_cabin, tram_tower and tram_station
     read the rows with no drift.
  S13 bay gates vs the cabin doors (revision 1e): lift_cabin's door (leaves -+T["door"]["x_out"] 0.850
     about the cabin center = TRAM_STATION dock z = the cabin manifest's hosted z_bay = tram_station
     DOCK_Z = GATE_DOOR_Z) against tram_station's fence layout (gate_layout, nothing built) on both
     stations: no post or leaf in the door span (1.70 m), a clear opening >= 1.70 centered on the seam
     with leaves beside it long enough to close on the seam (each >= half the opening),
     the gate line outside the cabin's lateral bound; the 1d grid (GATES_AT_DOOR False) would fail the
     same predicate (self-check); the shipped station manifests' C17 blocks carry the same door and four
     centered openings.
  S14 the tram top's haul loop (revision 2, design 8 / skeptic MC9): recomputed HERE from tram_station's
     constants (the internal tangent of the foot sheave and the bullwheel, the lane wheel's top and back points),
     every tangent point of tram_station.haul_route_top() lies on its lane-wheel / foot-sheave / bullwheel rope
     circle within 1 mm, the level run from the stubs is unchanged (y 15.60 at x +-5.00 from z -12.00 to the lane
     wheel's top at z DW_Z), the lane-wheel / foot-sheave identities hold, and the shipped top manifest's
     prefab_contract.wheel_centres equals the code (bullwheel, lane wheels, foot sheaves); the base's drive wheel
     and deflection sheaves the same way (haul_route_base, its manifest).
  Nitpick A3 (2026-10-13, build/nitpick-a3-cabin-nose.md): the gondola cabin is mirrored to OUTBOARD -Y (its right on the
     stations' spin). S6 / S10 read the shipped cabin's outboard side from carriers.json gd8 "outboard" (outboard =
     sign * raw x); S11 gates ONE side: carriers.json gd8 == lift_cabin.GD_OUTBOARD == the shipped cabin's manifest
     (args, prefab numbers, catalog agree) == -Y.
  S15 (nitpick A3) the SHIPPED gondola cabin through every GD sheave train on the stations' spin (lift_chair.carrier_sweep
     + carrier_verdict "gantry", the S9 family rule of test_dq_family_seams): GD12 / GD16 / GD20, the ETGD and the gondola
     top's nose train, both lanes, both pin heights; its stalk on authoring -Y; the approved +Y cabin (its mirror) must
     cross the rocker mains of each. Builds 4 towers + the gondola top (~1 min); `-- --no-s15` skips it (quick runs).
  Nitpick B2 (2026-10-13, build/nitpick-b2-tram-ramps.md): the tram base's lot 6 x 3 moved its building 4.00 m toward
     -X; its manifest's prefab_contract.axis_x (-4.00) is the line axis, and S9 / S14 compare the base's stubs, hosted
     cabins and wheel centers RELATIVE to it (the seams are relative: lane gauge, hang, haul offset, wheel geometry).
  INFO: the parked wheel float over the stations' thinner modeled track rope.

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_gondola_tram_seams.py
"""

import json
import os
import sys
import time

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
BLENDER_DIR = os.path.dirname(HERE)
REPO = os.path.dirname(os.path.dirname(BLENDER_DIR))
sys.path.insert(0, BLENDER_DIR)

import cs2_validate  # noqa: E402
import lift_cabin as LC  # noqa: E402
import tram_station as TS  # noqa: E402
import tram_tower as TT  # noqa: E402

IMPORT = os.path.join(REPO, "assets", "import")
CARRIERS_JSON = os.path.join(REPO, "catalog", "carriers.json")
ARGV = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ALLOW_MISSING = "--allow-missing" in ARGV
NO_S15 = "--no-s15" in ARGV      # skip the S15 cabin sweep (builds 4 towers + the gondola top, ~1 min): quick runs only


def gd_outboard_sign():
    """Nitpick A3 (2026-10-13): the gondola cabin's outboard side as an authoring-y sign, from catalog/carriers.json gd8
    "outboard" ("-Y" -> -1: the carrier's right on the stations' spin). Raw FBX x = authoring y, so the outboard-
    positive lateral of a shipped cabin vertex is sign * x, and sign * z keeps the frame a proper rotation."""
    with open(CARRIERS_JSON, "r", encoding="utf-8") as fh:
        side = json.load(fh)["carriers"]["gd8"].get("outboard")
    return {"+Y": 1.0, "-Y": -1.0}[side]
FAILS = []
NOTES = []
EPS = 1e-6
ENV_TOL = 0.005          # a cabin vertex may sit this far outside the tower envelope (rounding)


def check(cond, label, detail=""):
    if cond:
        print(f"  ok   {label} {detail}".rstrip())
    else:
        print(f"  FAIL {label} {detail}".rstrip())
        FAILS.append(label)


def note(msg):
    NOTES.append(msg)
    print(f"  note {msg}")


def close(a, b, eps=EPS):
    return a is not None and b is not None and abs(float(a) - float(b)) <= eps


def missing(what):
    """A shipped export is gone: FAIL (all ten ship), or a note under --allow-missing."""
    if ALLOW_MISSING:
        note(f"skipped {what} (--allow-missing)")
    else:
        check(False, f"shipped export present: {what}", "(pass -- --allow-missing before the export)")


def manifest(rel, name):
    p = os.path.join(IMPORT, rel, name, f"{name}.manifest.json")
    if not os.path.isfile(p):
        missing(f"{rel}/{name}: no manifest")
        return None
    with open(p, "r", encoding="utf-8") as fh:
        return json.load(fh)


def fbx_verts(rel, name, lod=""):
    """Raw FBX vertices in meters = prefab-local (x lateral, y up, z travel)."""
    p = os.path.join(IMPORT, rel, name, f"{name}{lod}.fbx")
    if not os.path.isfile(p):
        return None
    objs = cs2_validate.import_fbx_temp(p)
    try:
        o = [x for x in objs if x.type == "MESH"][0]
        co = np.empty(len(o.data.vertices) * 3, np.float64)
        o.data.vertices.foreach_get("co", co)
    finally:
        cs2_validate.remove_objects(objs)
    return co.reshape(-1, 3) / 100.0


def outside_union(P, boxes):
    """Distance of each lane-frame point (travel, dy, dz) outside the union of carrier boxes."""
    best = np.full(len(P), np.inf)
    for _bn, bx, dy, dz, _al in boxes:
        lo = np.array([bx[0], dy[0], dz[0]])
        hi = np.array([bx[1], dy[1], dz[1]])
        d = np.linalg.norm(np.maximum(0.0, np.maximum(lo - P, P - hi)), axis=1)
        best = np.minimum(best, d)
    return best


# --------------------------------------------------------------------------------------
# 1. code
# --------------------------------------------------------------------------------------

def code_seams():
    print("[seams] 1. code")
    ct = LC.CARRIERS["tram"]
    dims = LC.tram_dims(ct["hang_m"])
    # S1 hang chain
    check(close(ct["hang_m"], 10.50) and close(TT.TRAM["hang"], 10.50) and close(TS.HANG_M, 10.50),
          "S1 hang 10.50 in cabin / tower / station (R27)",
          f"({ct['hang_m']}, {TT.TRAM['hang']}, {TS.HANG_M})")
    check(close(dims["rope"], 10.00) and close(TT.TRAM["rope_above_floor"], 10.00)
          and close(TS.ROPE_ABOVE_PIVOT_M, 10.00) and close(TS.TRACK_Y - TS.PIVOT_Y, 10.00),
          "S1 track rope = pivot + 10.00 (cabin tram_dims, tower, station TRACK_Y - PIVOT_Y)",
          f"({dims['rope']}, {TT.TRAM['rope_above_floor']}, {TS.TRACK_Y - TS.PIVOT_Y})")
    check(close(TS.CARRIAGE_TOP_Y - TS.TRACK_Y, LC.T["grip_above_rope"])
          and close(LC.T["grip_above_rope"], 0.50),
          "S1 carriage top 0.50 above the track rope (station dock, cabin)")
    check(close(LC.T["haul_dz"], -0.40) and close(ct["haul_dy_m"], -0.40)
          and close(TT.TRAM["haul_dz"], -0.40) and close(TS.HAUL_DY, -0.40)
          and close(dims["haul_z"], dims["rope"] - 0.40) and close(TS.HAUL_Y, TS.TRACK_Y - 0.40),
          "S1 haul rope 0.40 below the track rope everywhere (R28)")
    # S2 lanes
    check(close(ct["lane_x_m"], 5.5) and close(TT.TRAM["lane_y"], 5.5) and close(TS.TRACK_X, 5.5),
          "S2 track HV lanes x +-5.5 (cabin row, tower, station)")
    check(close(ct["haul_x_m"], 5.0) and close(TT.TRAM["haul_y"], 5.0) and close(TS.HAUL_X, 5.0),
          "S2 haul lanes x +-5.0 (cabin row, tower, station)")
    check(close(LC.T["haul_dy"], TS.TRACK_X - TS.HAUL_X) and close(LC.T["haul_dy"], 0.50),
          "S2 cabin haul grips at dy = track - haul = 0.50 (inboard on each bay)")
    # S1b the station is the anchor owner: the cabin's hosted-pose rows use its deck / dock / track
    st = LC.TRAM_STATION
    check(close(st["deck_y"], TS.DECK_Y) and close(st["dock_z"], TS.DOCK_Z)
          and close(st["track_y"], TS.TRACK_Y) and close(st["deck_y"] - 0.30 + dims["rope"], TS.TRACK_Y)
          and close(st["deck_y"] - 0.30 + dims["hang"], TS.CARRIAGE_TOP_Y),
          "S1b lift_cabin.TRAM_STATION == tram_station deck 6.30 / dock z -5.50 / track 16.00 / top 16.50",
          f"({st})")
    check(close(TS.TRACK_X - st["deck_edge_dx"], 3.20) and close(TS.TRACK_X + st["deck_edge_dx"], 7.80),
          "S1b cabin row deck edge lane +-2.30 == the station's bay deck edges x +-3.20 / +-7.80 (C5)")
    # S3 R30
    check(close(LC.T["half_w_max"], 1.10) and close(TT.TRAM["carriage_half_w"], 1.10)
          and close(TS.CARRIAGE_HALF_W, 1.10) and LC.T["fork_half"] <= 1.10 + EPS,
          "S3 R30 carriage/hanger half-width 1.10 (cabin, tower, station); fork 1.08 <= 1.10")
    z30 = {z[0]: z for z in TT.R30_ZONES}
    k1, k2 = LC.T["k1"], LC.T["k2"]
    check(close(k1[0], z30["R30a_out"][2][0]) and close(k1[2], z30["R30a_out"][1][0]),
          "S3 cabin keep-out k1 (dz -0.80, |dy| 0.36) == tower R30a band",
          f"(k1 {k1}, R30a {z30['R30a_out'][1:3]})")
    check(close(k2[0], z30["R30b_out"][2][0]) and close(k2[1], z30["R30b_out"][2][1])
          and close(k2[2], z30["R30b_out"][1][0]),
          "S3 cabin keep-out k2 (dz -3.80..-0.70, |dy| 0.80) == tower R30b band",
          f"(k2 {k2}, R30b {z30['R30b_out'][1:3]})")
    # S4 drawn rope radii
    check(close(LC.HV_ROPE_R, TT.TRAM["rope_r_hv"]) and close(LC.HV_ROPE_R, TT.GD["rope_r_hv"])
          and close(LC.HV_ROPE_R, 0.100),
          "S4 drawn HV rope r 0.100 in the cabin wheel seat and both tower families")
    check(close(LC.LV_ROPE_R, TT.TRAM["rope_r_lv"]) and close(LC.LV_ROPE_R, TT.GD["rope_r_lv"]),
          "S4 drawn LV rope r 0.060 in the cabin haul clamps and the tower haul rollers / comm")
    # S7 gondola line
    cg = LC.CARRIERS["gd8"]
    gd_axle = TT.GD["tubular_tops"][0] - TT.GD["h_top"]
    check(close(cg["lane_x_m"], TT.GD["lane_y"]) and close(cg["lane_x_m"], 3.0),
          "S7 gondola gauge +-3.0 (cabin gd8, lattice towers)")
    check(close(cg["hang_m"], TT.GD["hang"]) and close(cg["hang_m"], LC.G["hang"]),
          "S7 gondola hang 4.50 (cabin row, cabin G, lattice envelope)")
    check(close(cg["axle_m"], gd_axle),
          "S7 gondola line axle 10.0 = GD12 spec top 12.0 - H_top 2.0 (lattice manifests)",
          f"({cg['axle_m']} vs {gd_axle})")
    check(close(cg["grip_top_above_rope_m"], LC.G["hang"] - LC.G["rope"]),
          "S7 gondola grip top 0.08 above the rope (row, G)")
    # S8 lane prefab identity (code)
    tr_track, tr_haul = TT.STUB_LANES["tram_track"], TT.STUB_LANES["tram_haul"]
    check(tr_track == (TS.LANE_TRACK["prefab"], TS.LANE_TRACK["fallback"]),
          "S8 tram track stubs: tower lane prefab + fallback == station", f"{tr_track}")
    check(tr_haul == (TS.LANE_HAUL["prefab"], TS.LANE_HAUL["fallback"]),
          "S8 tram haul stubs: tower lane prefab + fallback == station", f"{tr_haul}")
    check(TT.STUB_LANES["gd_haul"][0] == "SkiGondolaHaulRope",
          "S8 gondola HV stubs name SkiGondolaHaulRope (round-2-contract 10.1)")
    # S11 catalog: the gd8 / tram rows of catalog/carriers.json are the ONE source the C# catalog
    # mirrors (integration 2026-10-11 pasted lift_cabin.md section 5); every generator reads them
    # without drift
    with open(CARRIERS_JSON, "r", encoding="utf-8") as fh:
        rows = json.load(fh)["carriers"]
    check(close(rows["tram"]["hang_m"], 10.50) and close(rows["tram"]["lane_x_m"], 5.5)
          and close(rows["tram"]["haul_x_m"], 5.0) and close(rows["tram"]["haul_dy_m"], -0.40)
          and close(rows["tram"]["grip_top_above_rope_m"], 0.50),
          "S11 carriers.json tram row: hang 10.50, track 5.5, haul 5.0 / -0.40, carriage top 0.50 (R27/R28)")
    check(close(rows["gd8"]["hang_m"], 4.50) and close(rows["gd8"]["lane_x_m"], 3.0)
          and close(rows["gd8"]["axle_m"], 10.0) and close(rows["gd8"]["grip_top_above_rope_m"], 0.08),
          "S11 carriers.json gd8 row: hang 4.50, lane 3.0, axle 10.0, grip 0.08 above the rope")
    for key in ("gd8", "tram"):
        mine, cat = LC.CARRIERS[key], rows[key]
        fields = [f for f in LC.CONTRACT_FIELDS if f in mine]
        bad = {f: (cat.get(f), mine[f]) for f in fields if f not in cat or not LC.same_value(cat[f], mine[f])}
        check(not bad and len(fields) >= 6,
              f"S11 carriers.json {key} row carries lift_cabin.CARRIERS' contract fields {fields}",
              f"({bad or 'all equal'}; catalog-only keys not compared: "
              f"{sorted(set(cat) - set(LC.CONTRACT_FIELDS))})")
    d_gd = LC.catalog_drift("gondola", CARRIERS_JSON)["drift"]
    d_tr = LC.catalog_drift("tram", CARRIERS_JSON, dims["hang"])["drift"]
    _rows, d_tt = TT.load_catalog(CARRIERS_JSON)
    d_ts = (TS.read_carriers(CARRIERS_JSON) or {"drift": ["unread"]})["drift"]
    check(not d_gd and not d_tr and not d_tt and not d_ts,
          "S11 no catalog drift reported by lift_cabin (gd8, tram), tram_tower, tram_station",
          f"({d_gd}, {d_tr}, {d_tt}, {d_ts})")
    float_m = LC.HV_ROPE_TOP - TS.TRACK_ROPE_R
    note(f"parked tram wheels sit on the DRAWN HV rope top (+{LC.HV_ROPE_TOP}); the stations model a "
         f"r {TS.TRACK_ROPE_R} track rope, so the parked wheel groove floats {float_m:.4f} m above it "
         "(accepted convention: station ropes are thinner than drawn lanes, DQ 0.055 vs 0.100)")


# --------------------------------------------------------------------------------------
# 2. exports
# --------------------------------------------------------------------------------------

def envelope_seams():
    print("[seams] 2a. shipped cabin meshes inside the tower carrier envelopes")
    rope_t = LC.tram_dims(LC.CARRIERS["tram"]["hang_m"])["rope"]
    for lod in ("", "_LOD1", "_LOD2"):
        V = fbx_verts("cabins/A", "SkiTramCabinA", lod)
        if V is None:
            missing(f"SkiTramCabinA{lod}.fbx")
            continue
        worst = 0.0
        for s in (1, -1):        # cabin A on the -x lane (dy = -x), cabin B on +x (dy = +x)
            P = np.stack([V[:, 2], s * V[:, 0], V[:, 1] - rope_t], axis=1)
            worst = max(worst, float(outside_union(P, TT.CARRIER_TRAM).max()))
        check(worst <= ENV_TOL, f"S5 SkiTramCabinA{lod or '_LOD0'} ({len(V)} verts) inside CARRIER_TRAM",
              f"(worst {worst:.4f} m outside)")
    for lod in ("", "_LOD1", "_LOD2"):
        V = fbx_verts("cabins/A", "SkiGondolaCabin8A", lod)
        if V is None:
            missing(f"SkiGondolaCabin8A{lod}.fbx")
            continue
        out = {}
        sg = gd_outboard_sign()            # nitpick A3: outboard = sign * raw x (the cabin ships -Y)
        for tag, ref in (("rope-point", LC.G["rope"]), ("grip-top", LC.G["hang"])):
            P = np.stack([sg * V[:, 2], sg * V[:, 0], V[:, 1] - ref], axis=1)    # outboard = dy +
            out[tag] = float(outside_union(P, TT.CARRIER_GD).max())
        check(max(out.values()) <= ENV_TOL,
              f"S6 SkiGondolaCabin8A{lod or '_LOD0'} ({len(V)} verts) inside CARRIER_GD, both pin frames",
              f"(worst {out})")


def manifest_seams():
    print("[seams] 2b. manifests")
    # cabins
    mt = manifest("cabins/A", "SkiTramCabinA")
    if mt:
        c = mt["contract"]
        check(close(c["hang_m"], 10.5) and close(c["track_rope_m"], 10.0) and close(c["haul_rope_m"], 9.6)
              and close(c["haul_dy_m"], 0.5) and close(c["half_width_max_m"], 1.1),
              "S9 SkiTramCabinA manifest contract rows (hang, ropes, haul dy, half-width)")
        pn = mt["prefab_numbers"]
        check(pn["UtilityObject"]["m_UtilityPosition"] == [0.0, 10.5, 0.0]
              and pn["expected_mesh_bounds_prefab_local"]["y"] == [0.0, 10.5],
              "S9 SkiTramCabinA UtilityObject y 10.50 = bounds top")
        hs = pn["hosted_poses"].get("station", {})
        check(close(hs.get("Yp"), TS.PIVOT_Y) and close(hs.get("track_y"), TS.TRACK_Y)
              and close(hs.get("haul_y"), TS.HAUL_Y) and close(hs.get("carriage_top_y"), TS.CARRIAGE_TOP_Y)
              and close(hs.get("z_bay"), TS.DOCK_Z) and hs.get("matches_station_track_y") is True,
              "S9 SkiTramCabinA manifest hosted poses use the station's Yp 6.00 / track 16.00 / haul 15.60 "
              "/ top 16.50 / z_bay -5.50", f"({ {k: hs.get(k) for k in ('Yp', 'track_y', 'haul_y')} })")
        check(not mt.get("catalog", {}).get("drift"),
              "S11 SkiTramCabinA manifest records no catalog drift", f"({mt.get('catalog', {}).get('drift')})")
    mg = manifest("cabins/A", "SkiGondolaCabin8A")
    if mg:
        pn = mg["prefab_numbers"]
        check(close(pn["grip_top_m"], 4.5) and close(pn["rope_m"], 4.42) and close(pn["lane_offset_m"], 3.0)
              and close(pn["axle_m"], 10.0) and pn["NetSubObjects"]["m_Position"] == "(+-3.0, 10.11, 0)",
              "S7 SkiGondolaCabin8A manifest: grip 4.50, rope 4.42, lane 3.0, axle 10.0, rows y 10.11")
        check(close(10.0 + pn["grip_top_above_rope_m"] + 0.03, 10.11),
              "S7 gondola rows y = axle + grip-above-rope + 0.03 (AnchorTop rule, 7.15 precedent)")
        check(not mg.get("catalog", {}).get("drift"),
              "S11 SkiGondolaCabin8A manifest records no catalog drift", f"({mg.get('catalog', {}).get('drift')})")
        # nitpick A3: ONE outboard side - carriers.json gd8 (the stations pose the cabin by it), lift_cabin's
        # GD_OUTBOARD (the default build) and the shipped cabin (its manifest + prefab numbers)
        with open(CARRIERS_JSON, "r", encoding="utf-8") as fh:
            cat_side = json.load(fh)["carriers"]["gd8"].get("outboard")
        built = (mg.get("args") or {}).get("outboard")
        check(cat_side == LC.GD_OUTBOARD == built == pn.get("outboard_authoring") == "-Y"
              and pn.get("outboard") == "-X" and (mg.get("catalog") or {}).get("outboard", {}).get("agree") is True,
              "S11 gondola outboard side: carriers.json gd8 == lift_cabin.GD_OUTBOARD == the shipped cabin == -Y "
              "(the carrier's right on the stations' spin, nitpick A3)",
              f"(catalog {cat_side}, generator {LC.GD_OUTBOARD}, shipped args {built}, prefab "
              f"{pn.get('outboard')} / {pn.get('outboard_authoring')})")
    # tram towers
    for H in TT.HEIGHTS["tram"]:
        name = f"SkiTramTower{H}A"
        m = manifest("tramtowers/A", name)
        if not m:
            continue
        z0 = H - TT.TRAM["h_top"]
        pf = m["prefab"]
        check(close(m["datum"]["z0"], z0) and pf["utility_object"]["position"] == [0.0, z0, 0.0]
              and close(pf["bounds_top"], H),
              f"S9 {name} datum / UtilityObject y {z0} / bounds top {H}")
        want = {(-5.5, z0), (5.5, z0), (-5.0, round(z0 - 0.4, 3)), (5.0, round(z0 - 0.4, 3))}
        got = {(s["pos"][0], s["pos"][1]) for s in pf["stubs"]}
        check(got == want and all(s["pos"][2] == 0.0 for s in pf["stubs"]),
              f"S9 {name} stubs at (+-5.5, {z0}) track and (+-5.0, {z0 - 0.4:.2f}) haul", f"{sorted(got)}")
        check(all(s.get("node_index") == [i, i] for i, s in enumerate(pf["stubs"])),
              f"S9 {name} stubs single-point, node index x == y")
        lanes = {(s["lane"], s.get("lane_prefab"), s.get("lane_fallback")) for s in pf["stubs"]}
        check(lanes == {("HV track", TS.LANE_TRACK["prefab"], TS.LANE_TRACK["fallback"]),
                        ("LV haul", TS.LANE_HAUL["prefab"], TS.LANE_HAUL["fallback"])},
              f"S8 {name} stubs name the station's lane prefabs (FindAnchor pairs by prefab)", f"{lanes}")
        check(pf["piece_lanes"]["hv_track"] == [5.5, 26.25] and pf["piece_lanes"]["lv_haul"] == [5.0, 25.85],
              f"S9 {name} piece lanes (5.5, 26.25) / (5.0, 25.85) = every variant's stub over the node")
        check(m.get("catalog_drift") == [], f"S11 {name} manifest records no catalog drift", f"({m.get('catalog_drift')})")
    # gondola lattice
    for H in TT.HEIGHTS["gondola"]:
        name = f"SkiGondolaLattice{H}O"
        m = manifest("towers/O", name)
        if not m:
            continue
        A = H - TT.GD["h_top"]
        pf = m["prefab"]
        got = {(s["pos"][0], s["pos"][1]) for s in pf["stubs"]}
        check(close(m["datum"]["z0"], A) and got == {(-3.0, A), (3.0, A), (0.0, round(A + 1.2, 3))},
              f"S9 {name} axle {A}, HV stubs (+-3.0, {A}), comm (0, {A + 1.2:.1f})")
        hv = [s for s in pf["stubs"] if s["lane"] == "HV haul"]
        check(len(hv) == 2 and all(s.get("lane_prefab") == "SkiGondolaHaulRope" for s in hv),
              f"S8 {name} HV stubs name SkiGondolaHaulRope")
        check(pf["piece_lanes"]["hv"] == [3.0, 10.0] and close(pf["top_anchor_y"], 12.0),
              f"S7 {name} piece lane (3.0, 10.0), family spec y 12.000 (GD12, provisional)")
        check(m.get("catalog_drift") == [], f"S11 {name} manifest records no catalog drift", f"({m.get('catalog_drift')})")
    # stations
    for role in ("base", "top"):
        name = TS.NAMES[role]
        m = manifest("stations/tram/A", name)
        if not m:
            continue
        pc = m["prefab_contract"]
        check(close(pc["track_y"], 16.0) and close(pc["haul_y"], 15.6) and close(pc["pivot_y"], 6.0)
              and close(pc["carriage_top_y"], 16.5) and close(pc["track_x"], 5.5) and close(pc["haul_x"], 5.0),
              f"S9 {name} levels: track 16.00 / haul 15.60 / pivot 6.00 / carriage top 16.50, x 5.5 / 5.0")
        check(close(pc["deck_y"] - pc["pivot_y"], TS.INTERIOR_FLOOR_M) and close(TS.INTERIOR_FLOOR_M, 0.30),
              f"S9 {name} pivot = deck - 0.30 (the cabin manifest's hosted-pose formula)")
        check(m.get("carriers_json") == [], f"S11 {name} manifest records no catalog drift",
              f"({m.get('carriers_json')})")
        ax = float(pc.get("axis_x", 0.0))          # nitpick B2: the line axis (the RAMPS base: -4.00)
        a = {(s["kind"], round(s["a"][0] - ax, 6), s["a"][1], s["a"][2]) for s in pc["stubs"]}
        check(a == {("track", -5.5, 16.0, -12.0), ("track", 5.5, 16.0, -12.0),
                    ("haul", -5.0, 15.6, -12.0), ("haul", 5.0, 15.6, -12.0)},
              f"S9 {name} stubs a at z -12.00 (track 16.00, haul 15.60)")
        for d in pc["docks"]:
            check(close(d["rope"][1] - d["pivot"][1], 10.0) and close(d["carriage_top"][1] - d["pivot"][1], 10.5),
                  f"S1 {name} dock x {d['pivot'][0]}: rope pivot + 10.00, carriage top pivot + 10.50")
        cab = [h for h in pc["hosted"] if h["what"].startswith("SkiTramCabin")]
        if role == "base":
            poses = sorted((round(h["m_Position"][0] - ax, 6), h["m_Position"][1], h["m_Position"][2]) for h in cab)
            check(poses == [(-5.5, 6.0, -5.5), (5.5, 6.0, -5.5)]
                  and all(h["m_Rotation"] == [0.0, 0.0, 0.0, 1.0] and h["m_ParentMesh"] == 0 for h in cab),
                  "S9 base hosts both cabins at (+-5.5, 6.0, -5.5), identity, m_ParentMesh 0 (R29)",
                  f"{poses}")
        else:
            check(not cab, "S9 top station hosts no cabin (R29: parked in the base bays)")


def gate_span_report(role):
    """The fence of one berth edge for `role` against lift_cabin's door span: (parts overlapping the
    span, opening width, opening center, posts bounding it, the lengths of the two leaves beside the
    opening). Pure arithmetic on tram_station.gate_layout."""
    c, half = float(LC.TRAM_STATION["dock_z"]), float(LC.T["door"]["x_out"])
    lo_s, hi_s = c - half, c + half
    posts, leaves = TS.gate_layout(TS.GATE_Z0[role])
    parts = [(f"post@{z:.3f}", z - TS.GATE_POST_HALF, z + TS.GATE_POST_HALF) for z in posts]
    parts += [(f"leaf@{a:.3f}..{b:.3f}", a, b) for a, b in leaves]
    inside = [n for n, a, b in parts if b > lo_s + 1e-6 and a < hi_s - 1e-6]
    below = [(b, n) for n, a, b in parts if b <= lo_s + 1e-6]
    above = [(a, n) for n, a, b in parts if a >= hi_s - 1e-6]
    if not below or not above:
        return inside, 0.0, None, ("?", "?"), (0.0, 0.0)
    (b, nb), (a, na) = max(below), min(above)
    len_lo = max(((y - x, y) for x, y in leaves if y <= lo_s + 1e-6), key=lambda v: v[1], default=(0.0, 0.0))[0]
    len_hi = min(((y - x, x) for x, y in leaves if x >= hi_s - 1e-6), key=lambda v: v[1], default=(0.0, 0.0))[0]
    return inside, a - b, (a + b) / 2.0, (nb, na), (len_lo, len_hi)


def gate_door_seams():
    """S13 (revision 1e): the bay gates against the cabin doors - code, then the shipped manifests."""
    print("[seams] 5. bay gates vs the cabin doors (revision 1e)")
    door = LC.T["door"]
    c, half = float(LC.TRAM_STATION["dock_z"]), float(door["x_out"])
    check(close(c, TS.DOCK_Z) and close(c, TS.GATE_DOOR_Z) and close(half, 0.85)
          and close(2 * half, 1.70) and close(door["x_meet"], 0.012) and TS.GATES_AT_DOOR is True,
          "S13 door centre = lift_cabin dock z = tram_station DOCK_Z = GATE_DOOR_Z; leaves -+0.850 about the "
          "seam (1.70 m span), gates laid out from the door",
          f"(cabin {c}, station {TS.DOCK_Z}, gates {TS.GATE_DOOR_Z}, leaf outer {half}, meet {door['x_meet']})")
    check(close(TS.GATE_OPEN_HALF * 2.0, 1.90) and TS.GATE_OPEN_HALF * 2.0 >= TS.C17_OPEN_MIN - EPS
          and TS.C17_OPEN_MIN >= 2 * half - EPS and TS.GATE_OPEN_HALF >= half + 0.05,
          f"S13 built opening {2 * TS.GATE_OPEN_HALF:.2f} m >= the C17 minimum {TS.C17_OPEN_MIN} >= the door span "
          f"{2 * half:.2f}, 0.10 m of clear on each side of the leaves")
    # the gate line stands outside the cabin's lateral bound: the door faces are at lane -+ y_front
    for ex in TS.GATE_EDGES:
        gap = abs(abs(ex) - TS.TRACK_X)                            # berth edge to the lane center
        check(gap - float(door["y_front"]) >= 0.40,
              f"S13 gate line x {ex}: {gap:.2f} m from the lane centre, {gap - float(door['y_front']):.3f} m "
              f"outside the door face ({door['y_front']})")
    for role in ("base", "top"):
        inside, opening, centre, bound, leaf = gate_span_report(role)
        check(not inside and opening >= 1.70 - EPS and centre is not None and abs(centre - c) <= TS.C17_CENTRE_TOL
              and min(leaf) >= opening / 2.0 - EPS,
              f"S13 {role} fence from z {TS.GATE_Z0[role]}: nothing in the door span z {c - half:.2f}..{c + half:.2f}, "
              f"opening {opening:.3f} m centred {centre} between {bound}, the leaves beside it "
              f"{[round(v, 3) for v in leaf]} m close on the seam (each >= half the opening)",
              f"(parts in the span {inside})")
    TS.GATES_AT_DOOR = False
    try:
        old = {role: gate_span_report(role) for role in ("base", "top")}
    finally:
        TS.GATES_AT_DOOR = True
    check(all(old[r][0] for r in old) and old["base"][0] and any("post@-6.200" in n for n in old["base"][0])
          and any("post@-5.600" in n for n in old["top"][0]),
          "S13 self-check: the 1d fence grid fails the same predicate (base post -6.200, top post -5.600 in the span)",
          f"({ {r: old[r][0] for r in old} })")
    # the shipped station manifests carry the C17 block of the build that exported them
    mt = manifest("cabins/A", "SkiTramCabinA")
    zb = None if not mt else mt["prefab_numbers"]["hosted_poses"]["station"]["z_bay"]
    for role in ("base", "top"):
        name = TS.NAMES[role]
        m = manifest("stations/tram/A", name)
        if not m:
            continue
        c17 = m.get("C17")
        if not c17:
            check(False, f"S13 {name} manifest carries a C17 block", "(re-export after revision 1e)")
            continue
        ed = c17["edges"]
        check(len(ed) == 4 and close(c17["door"]["centre"], c) and close(c17["door"]["half"], half)
              and (zb is None or close(c17["door"]["manifest_z_bay"], zb)) and c17["door"]["t13_held"] is True
              and all(e_["opening_m"] >= 1.70 - EPS and abs(e_["centre_z"] - c) <= TS.C17_CENTRE_TOL
                      and e_["margin_lo_m"] >= 0 and e_["margin_hi_m"] >= 0 for e_ in ed.values()),
              f"S13 {name} manifest C17: door centre {c17['door']['centre']} leaves -+{c17['door']['half']} "
              f"(cabin z_bay {zb}), 4 berth edges, openings {sorted({e_['opening_m'] for e_ in ed.values()})}",
              f"(span {c17.get('span_z')})")
        inside, opening, centre, _b, _leaf = gate_span_report(role)
        e0 = ed["0"] if "0" in ed else ed[0]
        check(close(e0["opening_m"], opening, 1e-3) and close(e0["centre_z"], centre, 1e-3)
              and e0["posts"] == len(TS.gate_layout(TS.GATE_Z0[role])[0])
              and e0["leaves"] == len(TS.gate_layout(TS.GATE_Z0[role])[1]),
              f"S13 {name} manifest edge 0 == the code layout (opening {opening:.3f}, centre {centre}, "
              f"{e0['posts']} posts / {e0['leaves']} leaves)")


def haul_loop_seams():
    """S14 (revision 2): the tram haul loops - code geometry recomputed independently, then the shipped manifests."""
    import math
    print("[seams] 6. tram haul loops (revision 2)")
    tol = 0.001
    hx, hy = TS.HAUL_X, TS.HAUL_Y
    # independent tangent geometry (top): internal tangent of the -X foot sheave and the bullwheel in the plane
    # z = BULL_Z; the rope leaves the foot sheave going counter-clockwise under it and arrives over the bullwheel
    c1, r1 = (-TS.FS_X, TS.FS_Y), TS.FS_RR
    c2, r2 = (0.0, TS.BULL_Y), TS.BULL_RR
    d = math.hypot(c2[0] - c1[0], c2[1] - c1[1])
    th = math.atan2(c2[1] - c1[1], c2[0] - c1[0]) + math.asin((r1 + r2) / d)
    t1 = (c1[0] + r1 * math.sin(th), c1[1] - r1 * math.cos(th))          # exit point (normal = (sin, -cos))
    t2 = (c2[0] - r2 * math.sin(th), c2[1] + r2 * math.cos(th))          # arrival point (opposite normal)
    lane_top = (-hx, TS.DW_Y + TS.DW_RR, TS.DW_Z)
    lane_back = (-hx, TS.DW_Y, TS.DW_Z + TS.DW_RR)
    foot_out = (-TS.FS_X - TS.FS_RR, TS.FS_Y, TS.BULL_Z)
    route = TS.haul_route_top()
    pts = [tuple(p) for p in route["points"]]

    def has(p):
        return min(math.dist(p, q) for q in pts) <= tol
    tangents = {"lane-wheel top (level run arrives)": lane_top, "lane-wheel back (down strand leaves)": lane_back,
                "foot-sheave outer point": foot_out, "foot-sheave exit": (t1[0], t1[1], TS.BULL_Z),
                "bullwheel arrival": (t2[0], t2[1], TS.BULL_Z)}
    for nm, p in tangents.items():
        for side in (-1, 1):
            q = (side * -p[0] if side > 0 else p[0], p[1], p[2])
            check(has(q), f"S14 top haul loop: the {nm} {tuple(round(v, 4) for v in q)} is a point of the route",
                  f"(nearest {min(math.dist(q, x) for x in pts) * 1000:.2f} mm)")
    on = [(abs(math.dist(lane_top, (-hx, TS.DW_Y, TS.DW_Z)) - TS.DW_RR), "lane top on the lane-wheel rope circle"),
          (abs(math.dist(lane_back, (-hx, TS.DW_Y, TS.DW_Z)) - TS.DW_RR), "lane back on the lane-wheel rope circle"),
          (abs(math.dist(t1, c1) - r1), "exit on the foot-sheave rope circle"),
          (abs(math.dist(t2, c2) - r2), "arrival on the bullwheel rope circle"),
          (abs(foot_out[0] + hx), "foot-sheave outer tangent at x -HAUL_X")]
    for e, nm in on:
        check(e <= tol, f"S14 top haul loop: {nm}", f"({e * 1000:.3f} mm)")
    check(abs(TS.DW_Y - (hy - TS.DW_RR)) < 1e-9 and abs(TS.DW_Z - (TS.BULL_Z - TS.DW_RR)) < 1e-9,
          "S14 top lane wheels take the rope on TOP (DW_Y = HAUL_Y - DW_RR) and send it down the haul plane "
          "(DW_Z = BULL_Z - DW_RR)", f"(DW {TS.DW_Y:.3f}, {TS.DW_Z:.3f})")
    run = [p for p in pts if abs(p[2] - TS.STUB_Z) < 1e-9 or (abs(p[1] - hy) < 1e-9 and p[2] <= TS.DW_Z + 1e-9)]
    check(len(run) == 4 and all(abs(abs(p[0]) - hx) < 1e-9 and abs(p[1] - hy) < 1e-9 for p in run)
          and sorted(round(p[2], 4) for p in run) == sorted([TS.STUB_Z, TS.STUB_Z, round(TS.DW_Z, 4), round(TS.DW_Z, 4)]),
          f"S14 top level run unchanged: y {hy} at x +-{hx} from the stubs z {TS.STUB_Z} to the lane-wheel tops z "
          f"{TS.DW_Z:.2f}", f"({[tuple(round(v, 3) for v in p) for p in run]})")
    # the base: the deflection sheaves' outer tangent is the haul lane, external tangents to the drive wheel
    rb = TS.haul_route_base()
    for nm, c, _pl, r, ap in rb["arcs"]:
        e = max(abs(math.dist(p, c) - r) for p in (ap[0], ap[-1]))
        check(e <= tol, f"S14 base haul loop: the {nm}'s tangent points on its rope circle", f"({e * 1000:.3f} mm)")
    check(abs(TS.BASE_DS_X + TS.BASE_DS_RR - hx) <= tol, "S14 base deflection sheaves' outer tangent at x +-HAUL_X")
    # the shipped manifests carry the same wheel centers
    for role, want in (("top", {"bullwheel": ([0.0, TS.BULL_Y, TS.BULL_Z], TS.BULL_RR),
                                "deflection": [([s * hx, TS.DW_Y, TS.DW_Z], TS.DW_RR) for s in (-1, 1)],
                                "foot_sheaves": [([s * TS.FS_X, TS.FS_Y, TS.BULL_Z], TS.FS_RR) for s in (-1, 1)]}),
                       ("base", {"bullwheel": (list(TS.BW_CENTRE), TS.BASE_BW_RR),
                                 "deflection": [([s * TS.BASE_DS_X, hy, TS.BASE_DS_Z], TS.BASE_DS_RR) for s in (-1, 1)]})):
        name = TS.NAMES[role]
        m = manifest("stations/tram/A", name)
        if not m:
            continue
        wc = m["prefab_contract"]["wheel_centres"]
        ax = float(m["prefab_contract"].get("axis_x", 0.0))    # nitpick B2: wheel centers relative to the line axis
        bad = []
        for key, w in want.items():
            got = wc.get(key)
            rows = w if isinstance(w, list) else [w]
            gots = got if isinstance(got, list) else [got]
            if got is None or len(gots) != len(rows):
                bad.append(key)
                continue
            for (c, r), g in zip(rows, gots):
                if max(abs(a - b) for a, b in zip(c, [g["centre"][0] - ax] + list(g["centre"][1:]))) > 1e-6 \
                        or abs(g["rope_r"] - r) > 1e-6:
                    bad.append(key)
        check(not bad, f"S14 {name} manifest wheel_centres == the code", f"(differ: {bad})")


def latent_gondola_station():
    print("[seams] 3. latent: gondola station carrier envelope vs the built cabin")
    try:
        import lift_terminal_detach as LTD  # noqa: E402 (pulls cs2_bake -> cs2_preview -> lift_tower)
    except Exception as exc:  # noqa: BLE001 - another fleet's module in progress
        note(f"S10 skipped: lift_terminal_detach import failed ({type(exc).__name__}: {exc})")
        return
    carrier = LTD.load_carrier("gd8", CARRIERS_JSON)      # the row the station build reads
    p = LTD.params_for("base", True, carrier)
    worst, where = 0.0, None
    for lod in ("", "_LOD1", "_LOD2"):
        V = fbx_verts("cabins/A", "SkiGondolaCabin8A", lod)
        if V is None:
            note(f"S10 skipped: no SkiGondolaCabin8A{lod} FBX")
            return
        sg = gd_outboard_sign()            # nitpick A3: the outboard-positive lateral is sign * raw x
        for x, y, z in np.stack([sg * V[:, 0], V[:, 1], sg * V[:, 2]], axis=1):   # lateral (+outboard), up, travel
            d_best = np.inf
            for e in p.env:
                label, tr, la, z0, z1 = e[:5]
                dy = e[5] if len(e) > 5 else 0.0          # gondola draft 1: outboard offset
                lo, hi = np.array([dy - la / 2, z0, -tr / 2]), np.array([dy + la / 2, z1, tr / 2])
                pt = np.array([x, y, z])
                d_best = min(d_best, float(np.linalg.norm(np.maximum(0, np.maximum(lo - pt, pt - hi)))))
            if d_best > worst:
                worst, where = d_best, (lod or "_LOD0", round(float(x), 3), round(float(y), 3),
                                        round(float(z), 3))
    shipped = os.path.isdir(os.path.join(IMPORT, "stations", "gondola", "A"))
    msg = (f"lift_terminal_detach --gondola env ({len(p.env)} boxes) leaves the built cabin up to "
           f"{worst:.4f} m outside (worst vertex lod/lateral/up/travel {where})")
    if shipped:
        check(worst <= ENV_TOL, "S10 gondola station C8 envelope covers the built cabin LOD0/1/2", msg)
    else:
        note(f"S10 LATENT (stations/gondola/A not exported): {msg}")


def gondola_station_seams():
    """S12 (gondola stations draft 1): the shipped station manifests against the cabin, the
    towers, carriers.json and the code catalog (SkiCatalog.cs station rows)."""
    print("[seams] 4. gondola stations (stations/gondola/A) vs cabin, towers, catalog")
    import hashlib
    import re
    try:
        import lift_terminal_detach as LTD  # noqa: E402
    except Exception as exc:  # noqa: BLE001
        check(False, "S12 lift_terminal_detach imports", f"({type(exc).__name__}: {exc})")
        return
    if not os.path.isdir(os.path.join(IMPORT, "stations", "gondola", "A")):
        note("S12 skipped: stations/gondola/A not exported")
        return
    with open(CARRIERS_JSON, "r", encoding="utf-8") as fh:
        gd8 = json.load(fh)["carriers"]["gd8"]
    lane, hang = float(gd8["lane_x_m"]), float(gd8["hang_m"])
    rope_y = round(LTD.DECK_H + LTD.GONDOLA_STEP_IN + hang, 4)
    rows_y = round(float(gd8["axle_m"]) + float(gd8["grip_top_above_rope_m"]) + 0.03, 3)
    mg = manifest("cabins/A", "SkiGondolaCabin8A")
    cab_rows = None
    if mg:
        mp = mg["prefab_numbers"]["NetSubObjects"]["m_Position"]
        cab_rows = float(mp.split(",")[1])
    cab_fbx = os.path.join(IMPORT, "cabins", "A", "SkiGondolaCabin8A", "SkiGondolaCabin8A.fbx")
    cab_sha = None
    if os.path.isfile(cab_fbx):
        with open(cab_fbx, "rb") as fh:
            cab_sha = hashlib.sha1(fh.read()).hexdigest()
    cs_path = os.path.join(REPO, "mod", "SkiResortMod", "Systems", "SkiCatalog.cs")
    cat = {}
    if os.path.isfile(cs_path):
        with open(cs_path, "r", encoding="utf-8") as fh:
            src = fh.read()
        pat = (r'Station\(lifts, Names\.StationGondola(Base|Top), StationRole\.\w+, "gondola", '
               r'new float3\(([-\d.]+)f, ([-\d.]+)f, ([-\d.]+)f\), ([-\d.]+)f')
        for m in re.finditer(pat, src):
            cat[m.group(1).lower()] = ([float(m.group(i)) for i in (2, 3, 4)], float(m.group(5)))
    gd_haul = TT.STUB_LANES["gd_haul"]
    for role in ("base", "top"):
        name = f"SkiGondola{role.capitalize()}StationA"
        m = manifest("stations/gondola/A", name)
        if not m:
            continue
        c = m["contract"]
        check(close(c["gauge_m"], 2 * lane) and close(c["gauge_m"], 2 * TT.GD["lane_y"])
              and close(c["turn_radius_code_m"], lane) and close(c["wheel_radius_m"], lane),
              f"S12 {name} gauge {c['gauge_m']} = 2 x cabin gd8 lane {lane} = GD towers, "
              f"wheel r {c['wheel_radius_m']}")
        check(close(c["carrier"]["hang_m"], hang) and close(c["carrier"]["hang_m"], LC.G["hang"])
              and close(c["lane_rope_y"], rope_y),
              f"S12 {name} hang {c['carrier']['hang_m']} (cabin {LC.G['hang']}), lane / rope y "
              f"{c['lane_rope_y']} = deck {LTD.DECK_H} + step {LTD.GONDOLA_STEP_IN} + hang")
        wl = c["wheel_local_prefab"]
        want_z = 9.40 if role == "base" else 5.40
        check(close(wl[0], 0.0) and close(wl[1], rope_y) and close(wl[2], want_z)
              and close(c["stub_tip_local_z"], -16.0),
              f"S12 {name} wheel {wl}, stub tip {c['stub_tip_local_z']}")
        if role in cat:
            cw, ct = cat[role]
            check(all(close(a, b, 1e-4) for a, b in zip(cw, wl)) and close(ct, c["stub_tip_local_z"]),
                  f"S12 {name} == SkiCatalog.cs StationGondola{role.capitalize()} row "
                  f"(wheel {cw}, stub tip {ct})")
        else:
            check(False, f"S12 SkiCatalog.cs has a StationGondola{role.capitalize()} row", f"({cs_path})")
        an = c["anchors_hv"]
        check(sorted(round(a["a"][0], 4) for a in an) == [-lane, lane]
              and all(close(a["a"][1], rope_y) for a in an)
              and all(a.get("lane_prefab") == gd_haul[0] and a.get("lane_fallback") == gd_haul[1]
                      for a in an),
              f"S12 {name} HV anchors x +-{lane} at y {rope_y}, lane prefab {gd_haul[0]} "
              f"(== tram_tower.STUB_LANES gd_haul: FindAnchors pairs by prefab)")
        ny = c["net_sub_objects_y"]["y"]
        check(close(ny, rows_y) and (cab_rows is None or close(ny, cab_rows)),
              f"S12 {name} line rows y {ny} = axle + grip-above-rope + 0.03 (cabin manifest {cab_rows})")
        ht = c["hosted_tower"]
        if role == "base":
            check(ht is not None and ht["position"] == [0.0, 0.0, -11.0]
                  and ht["rotation_quat"] == [0, 1, 0, 0] and close(ht["rope_y_required"], rope_y)
                  and close(ht["gauge_m"], 2 * lane),
                  f"S12 {name} hosted entry tower pose (0, 0, -11) 180 deg, rope {rope_y}, gauge {2 * lane}")
            # gondola revision 1: the entry tower exists (SkiLiftTowerETGDO, towers/O); the station's
            # G6 records its LOD0 / LOD1 seam against the revised hall (no overlap, the gap)
            ts = (m.get("gondola") or {}).get("tower_seam")
            if ts is None:
                note(f"S12 {name}: no tower seam record (the station predates revision 1 or the tower "
                     f"is not exported)")
            else:
                gaps = {k: (v or {}).get("min_gap_m") for k, v in sorted(ts.items())}
                check(sorted(ts) == ["LOD0", "LOD1"] and all(g is not None and g > 0.0 for g in gaps.values()),
                      f"S12 {name} the hosted SkiLiftTowerETGDO LOD0 / LOD1 clear the hall (gaps {gaps})")
        else:
            check(ht is None, f"S12 {name} hosts no tower (nose rope guides in the mesh)")
        g = m.get("gondola") or {}
        sw = g.get("cabin_sweep") or {}
        check(sw.get("min_clear_m", 0.0) >= 0.30 - 1e-9,
              f"S12 {name} real-cabin sweep (G8) min clearance {sw.get('min_clear_m')} m >= 0.30 "
              f"({sw.get('nearest_part')}, {sw.get('placements')} poses, {sw.get('pin')})")
        check(cab_sha is not None and (sw.get("cabin") or {}).get("sha1") == cab_sha,
              f"S12 {name} sweep made with the CURRENT shipped cabin FBX",
              f"(manifest {(sw.get('cabin') or {}).get('sha1')}, shipped {cab_sha}; re-export the "
              f"stations after a cabin re-export)")
        cf = g.get("cabin_floor") or {}
        check(close(cf.get("underside_y_today"), rope_y - hang) and close(cf.get("platform_y"), LTD.DECK_H),
              f"S12 {name} boarding platform {cf.get('platform_y')} under the cabin floor "
              f"{cf.get('underside_y_today')} (step {cf.get('step_in_m')})")
        if m.get("gondola_rev1"):
            # revision 1: the 3 x 4 lot holds the stub tip (-16) and, on the base, the hosted tower
            check(c.get("lot") == [3, 4] and close(c["stub_tip_local_z"], -16.0),
                  f"S12 {name} lot {c.get('lot')} (3 across x 4 along the travel) with the stub tip -16")


S15 = {}


def _s15_cabin():
    """The shipped SkiGondolaCabin8A LOD0 in the AUTHORING frame (x travel = raw z, y lateral = raw x, z up = raw y,
    underside at 0) as lift_chair.carrier_sweep's carrier tuple (coords, polys, names, hang); None when not shipped."""
    p = os.path.join(IMPORT, "cabins", "A", "SkiGondolaCabin8A", "SkiGondolaCabin8A.fbx")
    if not os.path.isfile(p):
        return None
    objs = cs2_validate.import_fbx_temp(p)
    try:
        o = [x for x in objs if x.type == "MESH"][0]
        V = np.array([v.co[:] for v in o.data.vertices], dtype=np.float64) / 100.0
        polys = [tuple(int(i) for i in pl.vertices) for pl in o.data.polygons]
    finally:
        cs2_validate.remove_objects(objs)
    a = np.stack([V[:, 2], V[:, 0], V[:, 1]], axis=1)
    a[:, 2] -= a[:, 2].min()
    return (a, polys, ["cabin"] * len(polys), float(a[:, 2].max()))


def s15_cabin_sweep():
    """S15 (nitpick A3, 2026-10-13; build/nitpick-a3-cabin-nose.md): the SHIPPED gondola cabin through every GD sheave
    train on the stations' spin - lift_chair.carrier_sweep (the carrier on lane s faces f = -s, so its RIGHT, authoring
    -Y, is outboard: the mover's yaw; 1 cm steps over the whole train plus the cabin, both lanes) at both pin heights
    (the grip top ON the lane = ChairPin today, the rope point on the lane = +0.08) + carrier_verdict "gantry" (nothing
    crossed but the F10 convention set - rims, liners, sub rockers + sub pins - and every other part, the rocker mains /
    links / girders first, >= CARRIER_CLEAR_M): GD12 / GD16 / GD20 and the ETGD (their shipped rebuild heads, lane flat
    at the axle as S9 does it) and the gondola top's nose train (station lane 4.85, rope tangent on its rims). The cabin
    is read as shipped (its own side, no catalog sign: S11 gates the sign). Negative: its exact y-mirror (= the approved
    +Y cabin: stalk, carriage and doors on its left = INBOARD on this spin) must cross the mains on every head."""
    print("[seams] 5. S15 the shipped gondola cabin through every GD sheave train (stations' spin)")
    if NO_S15:
        note("S15 skipped (-- --no-s15)")
        return
    import lift_chair as LCH  # noqa: E402 (carrier_sweep / carrier_verdict, nitpick A2)
    import lift_terminal  # noqa: E402
    import lift_terminal_detach as LTD  # noqa: E402
    import lift_tower as LTW  # noqa: E402
    new = _s15_cabin()
    if new is None:
        missing("SkiGondolaCabin8A.fbx (S15)")
        return
    a = new[0].copy()
    a[:, 1] = -a[:, 1]
    old = (a, [tuple(reversed(pl)) for pl in new[1]], new[2], new[3])
    band = new[0][(new[0][:, 2] > 2.80) & (new[0][:, 2] < 3.85)]
    check(len(band) > 0 and float(band[:, 1].max()) < -0.40,
          "S15 the shipped cabin's stalk on authoring -Y (raw FBX x < 0): its right, outboard on the stations' spin",
          f"(stalk y {band[:, 1].min():+.3f}..{band[:, 1].max():+.3f})" if len(band) else "(no stalk band)")

    def named(obj, table):
        me = obj.data
        co = np.empty(len(me.vertices) * 3)
        me.vertices.foreach_get("co", co)
        q = np.zeros(len(me.polygons), dtype=np.int32)
        me.attributes["part"].data.foreach_get("value", q)
        return co.reshape(-1, 3), [tuple(pl.vertices) for pl in me.polygons], [table[i - 1] for i in q]

    def line(v):
        return ", ".join(f"lane {s:+.0f} mains {d['mains'][0] if d['mains'] else '-'} gated "
                         f"{d['gated'][0] if d['gated'] else '-'} ({d['gated'][2] if d['gated'] else ''}) "
                         f"fails {len(d['fail'])}" for s, d in sorted(v.items()))

    def sweep(tag, target, lane, axle):
        res = {}
        for pin, dz in (("grip-top", 0.0), ("rope-point", float(LC.G["hang"] - LC.G["rope"]))):
            zl = (lambda x, z=axle + dz: z)
            vn = LCH.carrier_verdict(LCH.carrier_sweep(new, target, lane, zl), "gantry")
            vo = LCH.carrier_verdict(LCH.carrier_sweep(old, target, lane, zl), "gantry")
            res[pin] = {"new": vn, "old": vo}
            ok = all(not d["fail"] and d["gated"] is not None and d["gated"][0] >= LCH.CARRIER_CLEAR_M
                     for d in vn.values())
            check(ok, f"S15 {tag} ({pin} pin): the shipped cabin clears every gated part >= "
                      f"{LCH.CARRIER_CLEAR_M} on both lanes, nothing but the F10 set crossed", f"({line(vn)})")
            bad = all(any(LCH.carrier_part_core(f[2]).startswith("rocker_main_") for f in d["fail"])
                      for d in vo.values())
            check(bad, f"S15 {tag} ({pin} pin): negative - the approved +Y cabin (its mirror: stalk inboard) "
                       f"crosses the rocker mains", f"({line(vo)})")
        S15[tag] = res
    for key in ("GD12", "GD16", "GD20", "ETGD"):
        LTW.clear_scene()
        spec = LTW.VARIANTS[key]
        t, table, _s, _i = LTW.build_tower_v2(spec, spec.heads[0], finish=False)
        co, polys, names = named(t, table)
        tg = (np.stack([-co[:, 1], co[:, 0], co[:, 2]], axis=1), polys, names)     # net -> authoring
        sweep(f"{key} ({spec.heads[0]})", tg, spec.gauge / 2.0, spec.axle)
    LTW.clear_scene()
    lift_terminal.clear_scene()
    top = LTD.params_for("top", True, LTD.load_carrier("gd8", CARRIERS_JSON))
    parts = LTD.build_station(top)
    st, stable, _src = LTD.assemble(parts, True, top)
    co, polys, names = named(st, stable)
    k = [i for i, n in enumerate(names) if n.startswith(("sheave_guide_", "guideframe_"))]
    sweep(f"gondola top nose train (axle {LTD.nose_train_layout(top)['axle']:.3f}, lane {top.z_rope})",
          (co, [polys[i] for i in k], [names[i] for i in k]), top.lane_y, top.z_rope)
    lift_terminal.clear_scene()


def main():
    t0 = time.time()
    code_seams()
    envelope_seams()
    manifest_seams()
    gate_door_seams()
    haul_loop_seams()
    latent_gondola_station()
    gondola_station_seams()
    s15_cabin_sweep()
    dt = time.time() - t0
    if FAILS:
        print(f"FAIL test_gondola_tram_seams: {len(FAILS)} seam(s) broken: {FAILS}")
        sys.exit(1)
    print(f"PASS test_gondola_tram_seams: tram hang / lanes / R30 / rope radii agree in cabin, tower and "
          f"station; the shipped cabins fit the tower envelopes (both gondola pin frames); manifests agree; "
          f"stub lane prefabs match; the bay gates clear the cabin doors; the haul loops are tangent and the manifests "
          f"carry their wheels"
          + (f"; S15 the shipped -Y gondola cabin clears GD12/16/20, the ETGD and the gondola top nose on the "
             f"stations' spin, both lanes, both pins (min {min(d['gated'][0] for r in S15.values() for v in r.values() for d in v['new'].values()):.4f} m; "
             f"the approved +Y cabin fails each)" if S15 else "")
          + f" ({len(NOTES)} note(s), {dt:.1f} s)")


if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        raise
    except Exception:  # noqa: BLE001
        import traceback
        traceback.print_exc()
        sys.exit(1)
