"""
lift_terminal_detach.py - detachable-quad terminals (Leitner-Poma "loaf"), base (drive) and
top (return), plus the --gondola variant (walls, portals, stone base) on the same machine.

Contract: docs/research/art/assets-2026-10/design/dq-terminal.md with the 13 MUST-CHANGE items
of design/dq-terminal-skeptic.md applied (the skeptic wins where the two disagree). Build report:
docs/research/art/assets-2026-10/build/lift_terminal_detach.md.

    blender --background --factory-startup --python-exit-code 1 --python lift_terminal_detach.py -- \
        --kind base|top [--gondola] (--out <letter dir> | --export) [--finish] [--lods]
        [--preview <dir> | --no-preview] [--review [DIR]] [--name N] [--letter A]
        [--atlas 2048] [--ao-samples 48] [--ab <prev asset dir>] [--carriers <json>] [--no-validate]
        [--overwrite-shipped]

--export writes into the contract folder (assets/import/stations/detachable/A, or
stations/gondola/A with --gondola) and renders previews into assets/preview/stations/<type>/A.

DATUMS (MUST-CHANGE 1, 2, 7). The carrier row (hang_m, grip_top_above_rope_m, lane_x_m,
axle_m, seat_top_m) is read from catalog/carriers.json when it exists, else from the dq-chair.md
section 2/3 values (hang 3.300, grip top above rope 0.220, seat top 0.400 over the lowest
point, footrest down). The LANE height Z_ROPE is the height ChairPinSystem pins the chair's
GRIP TOP to today (bounds.max.y, ChairPinSystem.cs header "HOW A CHAIR IS PINNED" 4):
    Z_ROPE = DECK_H 0.30 + LOAD_CLEAR + hang_m,  LOAD_CLEAR = SEAT_OVER_DECK 0.50 - seat_top_m
           = 0.30 + 0.10 + 3.30 = 3.70  (DQ)   |  0.30 + 0.05 (step-in) + 4.50 = 4.85 (gondola)
The modelled haul rope, the rope torus, the wheel table y, the HV anchors and the hosted-tower
rope height all sit at Z_ROPE (the +0.05 Pali wheel quirk is dropped, skeptic claim 3). Seat top
over the deck: 0.50 today; 0.72 if dq-chair Q1 (pin the rope point, Phase B) lands - every
clearance contract (C8) is checked at both heights.

FRAMES (MUST-CHANGE 8). Authoring: metres, origin = lot centre on the ground, -X = nose / line
side, +X = back end, Y lateral, Z up. orient_for_net maps (x, y, z) -> (y, -x, z) and the export
bake then gives FBX-raw (y, z, x); the game negates x on import, so prefab-local game = (-y, z, x)
(cs2_liftdress.blender_to_game; x-sign gate N, 2026-10-15). Contracts C1-C3, C6-C10 and C13 run on the joined mesh in
the AUTHORING frame, before orient_for_net; C4, C5, C11, C12 run after it.

Library reuse (never copied): lift_terminal.clear_scene, apply_finish_bevels, split_windows and
make_post_compose, driven through a temporary override of their module tables (BEVEL_PREFIXES,
WIN_CELLS, detail_specs); cs2_bake.material_sources -> bake_unique (MUST-CHANGE 3, 4), packed from
a per-part seed layer UVSeed while every material samples UVTile (seed_parts); cs2_review.write_pack
as built (MUST-CHANGE 5); cs2_kit.tube_along for the U-turn rail (MUST-CHANGE 13). The engine-base
gate is engine_base_gate (lift_terminal.check_engine_base's rule with a float64 uv-zero recount).

Headless only (R10). Deterministic: fixed part order, tag_parts canonical order, sorted material
keys, fixed bake seed, no set/dict iteration into geometry.

Review fixes (build/dq-family-review-*.md, "Fixes applied" in dq-family-integration.md): the
base kind's nose balcony is narrowed to |y| <= 0.60 so the hosted ETDQ clears it (F1, seam S8);
maze rails leave with their posts at LOD1 (F4); rooms 0.05 inboard and C6 on the lateral lot
line (F8); LOD2 target 490 (F9); C5 on every LOD (F11); validate -> manifest -> pack order
(pipeline 2); shipping-tree guard (pipeline 6); name-band px-per-cell and texel asserts
(pipeline 5/10). Tests: test_terminal_detach_determinism.py, test_terminal_detach_bake.py.

Revision 1 (Richard's first live review, docs/research/art/assets-2026-10/verdicts-2026-10-11.md
#3-4, reference = the Collins lift at Alta): the outer ladder climbs the NOSE end from a nose
landing to the crown (never the back); ONE underside ladder (front pedestal); a small grated
gangplank with handrails out of the nose (base: between the hosted ETDQ's deck strips, open to
its ladder landing, seam S8 kept empty; top: inside the nose guide frames, closed); no railings
on the left flank looking from the back (-Y: the queue corral moved to the loading / operator
side +Y); the "S 4" name band became a sign band per side (launcher glyph emblem + a 5x7
lift-number plate with 0.70 m digits) pinned in a reserved atlas strip at 448 px/m; two
rounded windows per housing side in their own `_Win` room (cell 13, C12 now {7, 13}).
Asserted as R1a-R1f in assert_revision1; build report section "Revision 1".

Revision 1b (polish, 2026-10-11; build/revision1-check-stations.md 3e / 4e PARTIAL, build
report section "Revision 1b (polish)"): the plate number is VECTOR, not the 5x7 bitmap - the
outline of a bpy FONT curve (builtin Bfont) through welcome_sign.text_triangles (the civic
sign's header path, reused, never copied), point-sampled DIGIT_SS x DIGIT_SS per texel into
the same reserved strip at 448 px/m (anti-aliased edges, ControlMask 0 under the plate),
0.70 m cap height inside the unchanged 0.50 x 0.70 m text box. The emblem stays the crisp
launcher glyph; the sign gets a PLATE EDGE (sign_relief): a rounded bevel round the panel, a
pressed bead round the emblem panel and the number plate standing 2 mm proud of it in the
Normal map, plus a soft edge occlusion on the emblem panel's BaseColor, so the panel stops
reading as a flat rectangle. Geometry, UVs, the reserved rects and every count are
unchanged. Asserted in R1e (vector glyph >= 0.5 m, inside its box), stamp_signbands
(coverage vs the outline area, anti-aliasing levels, nothing outside the box) and
relief_signbands (the edge tilts, the field stays flat).

Revision 2 (Richard's second look, verdicts-2026-10-11.md "Second look" 3', 2026-10-11; build
report section "Revision 2 (second look)"): "front ladder good; it must EXTEND TO THE GROUND".
The nose ladder keeps its line (x_nose - 0.22, y -0.70) and now runs from a concrete footing
(lfoot_nz: open-bottom ground part, edges exactly at z 0, kept on LOD0/LOD1) up the nose face,
through a LADDER-WAY in the nose landing (the landing is four gratings round the opening; the
Revision 1 handrail stands on its outer rim), to the crown + 0.4, on ONE 0.30 rung grid with a
rung flush with the landing top; two clamps under the landing hold the rails. No safety cage
(none of the Collins / base-station reference ladders has one). Gangplank, S8, the 3.70
datum, the `_Win` cells and the vector digits are untouched. Asserted as R2a-R2d in
assert_revision2 (nose_ladder_layout holds every number; manifest key front_ladder).

Revision 1c (revision2-check-dq-stations item 6, 2026-10-11; build report section "Revision 1c
(leftovers)"): on the BASE the straight ground flight left 0.25 m of climbing room at the landing
(rim 0.25, handrail 0.28; the gangplank and the hosted ETDQ's decks box the nose line in, S8). The
base's ground flight is now a THROUGH ladder on the landing's +Y edge (ground_flight: rungs along
X, lfoot_gf footing, climber on +Y stepping between the rails onto the landing), the landing one
grating trimmed to the 0.18 m step-across, the front ladder on the landing again (Revision 1).
R2c GATES the climbing room (climb_room: station mesh, the hosted ETDQ keep-out, the C8 chair
envelope): base ground flight >= CLIMB_MIN 0.61 m (the old straight run fails), top >= its shipped
0.60 (CLIMB_TOP_FLOOR). The top station is unchanged; base: assert_revision1c.

Gondola stations, draft 1 (2026-10-11; build report section "Gondola stations, draft 1"; --gondola
only - every DQ code path, table entry and hash is unchanged): the walled hall (GD_* constants).
Two cabin portals in the nose wall cut to the cabin SILHOUETTE (gd_portal_outline: body + hanger
slot, the GD_ENV carrier boxes + GD_CLEAR at both pin heights inside, G1), reaching grade over the
cabin trough (floor GD_FLOOR_Z); boarding platforms at DECK_H = the cabin floor - 0.05 with a
yellow edge stripe GD_CLEAR outboard of the cabin (G2); every walkable surface reachable from
grade (G3: ramps + side doors -> platforms, walkway -> operator room, internal two-flight stair
-> machinery deck, a z 0 footing -> nose ladder on the portal face -> roof ladder -> crown); the
DQ Revision 1c climbing-room gate on the nose ladder and stair headroom / pitch (G4); a glazing
band of 4 `_Win` panes per side wall + the operator room's panes (G5, cells 13 / 7); nothing but
the haul ropes in the unbuilt hosted entry tower's keep-out (G6); the sign on the PORTAL face
and both side bands (G7, the -Y one shares the +Y rect); and the real shipped SkiGondolaCabin8A
swept through the portals, along the platforms and round the wheel with >= GD_CLEAR to every
non-machine part (G8, gd_cabin_sweep). Asserted in assert_gondola; manifest block "gondola".

Gondola stations, revision 1 (2026-10-11, Richard's eighth look: "go with the LONG GLAZED HALL and 3x4
lot ... maybe slightly bigger openings for the skiers to enter into"; build report
build/gondola-stations-rev1.md; --gondola only, GD_REV1): the timber barn becomes the Peak 2 Peak /
Aspen hall - a stone plinth (1.20 + a concrete coping) under FULL-HEIGHT `_Win` glazing between slim
steel mullions on both long sides and the back (one room per bay, the rooms' ids reused beyond 3 m
by cs2_bldg.room_graph / assign_rooms, <= 25; the operator room its own), ring beams at the head,
the portal wall (draft 1's keyhole portals) as the end gable with a glazing band above its lintel
round the sign spandrel, a shallow standing-seam roof (crown = eave + 0.95) on 1.20 m deep eaves
(steel outriggers, cedar soffits, linear soffit lights, snow guards, gutters, downspouts), double
glass skier doors (1.80 x 2.30 m clear) under canopies with 2 m ramps, the three signs on one rect,
the detach detail pass's kit where it fits (labels at 400 px/m, extinguishers, hose reel, e-stops,
WAIT HERE, the shared ski rack, tools) and VANILLA lamp posts / wall / ceiling lights / props as
sub-object rows (gd_subobjects). New contracts: G5 the glazed hall, G6 the REAL hosted
SkiLiftTowerETGDO clear of the hall, G9 the 3 x 4 lot, G10 part clash + the vanilla footprints;
weathering at the station preset's highest budget-clean step; previews with the `_Win` and vanilla
stand-ins (render_gd_previews: arrival, portal, platform, glazed_side (+ x-ray), night).

DQ follow-up (2026-10-11, Richard's eleventh / fourteenth / fifteenth looks + the gondola rev-1 checker;
build report build/dq-stations-followup.md). DQ: the two modelled 8 m flood masts per station are gone -
four VANILLA LightpoleParkRandom01 rows per station on the deck (D1_LAMPS, box-tested in D8 against the
mesh, the dressing, the C8 chair / rider envelope, the hosted ETDQ keep-out and the other rows); the SLOW
boards stand on two short posts of their own (D1_SLOW); the top's STAY SEATED hung BEFORE the unload
point (D1_HANG_BOARDS; REMOVED 2026-10-12, see the last paragraph) and D11 (assert_board_order) gates the
board order along the chair's travel on both stations, measured on the built faces. Gondola: the skier doors lose their fixed centre post and the
head transom sits on the 2.60 door head (GD_DOOR_HEAD: 1.80 x 2.30 m clear), G3 adds a clear-opening ray
grid that counts the fixed frame, and a standable-top census (gd_flat_tops: every flat top on a route or
declared in GD_NOT_WALKABLE with its reason). Previews: the vanilla rows as the review packs' labelled
stand-in boxes (subobj_standins) in the gondola views and the DQ close-ups, + follow-up close-ups
(FU_CLOSEUP_DIR). Follow-up resume (2026-10-12, after the first attempt died at the usage limit): the two
+Y-facing apron lamp posts move out to y -7.10 (their heads stood 0.09 m off band_n's face); D8 gates every
lamp head >= D1_LAMP_HEAD_CLEAR from the station faces, D9 a deck face under every deck prop's origin and every
union box inside the lot (C16-style); the lamp clearance numbers are true face distances.

Fine-detail pass, step 3b (2026-10-12; build report build/fine-detail-step3b-stations.md; the engine of
build/fine-detail-step2-engine.md on all four stations; design/fine-detail-pass.md A.2 / B / D as cut by
design/fine-detail-pass-skeptic.md FS1 / FS5 / FS6 / FS7 / FS15). FINE_DETAIL (default on; --no-fine = the
approved pre-pass station byte for byte): (1) the dressing the shared kit replaces (assets/import/props/B)
leaves the host and becomes "kind": "mod" rows by object-prefab CID (fine_detail.kit_row / kit_lean_row through
cs2_liftdress.sub_object_rows(fine=True)) - the one-sided ski rack (frame + gear), hose reels, extinguishers,
e-stops and the scraper + shovel (the 0.393 m foot-line rule); (2) fine geometry for what stays modelled: the
maze (8-seg capped posts, 6-sided ropes), the load gate (8-seg stiles, 6-sided rails), the boards' 12-seg posts
and 6-seg rods, and the control cabinets lose their finish bevel strips for a 2 cm lid lip (no bevel strips,
benchmark 2.2); (3) the DQ box lamps go vanilla: the soffit lenses lamp_s0-3 -> RoofLightRandom01 rows, the door
lamps lamp_cab / lamp_eng -> the two existing WallLightShortRangeRandom01 rows MOVE onto them (FS15). The weather
mast stays (FS1: frozen bounds); the LIFT BOARDS stay in the host atlas at 400 px/m (FS5: no station sign set).
Rows are written in the game frame (game = blender_to_game(authoring) = (-y, z, x), gx NEGATED: x-sign gate N,
applied 2026-10-16 / T14); a kit row's yaw turns its +Z to the removed part's facing (facing_quat): identity only
where that is authoring +X, and an authoring +Y facing is game -X. F1
(assert_kit_rows) box- and mesh-tests every kit row: the real kit mesh vs the station faces, its mount (deck /
grade under the foot, the whole back on the wall, the lean props' rest plane on the wall), the C8 chair / rider
envelope at both pins, the hosted ETDQ / ETGDO, the gondola nose ladder zone, every vanilla and kit row, the lot.

STAY SEATED removed at the top; DQ _Emissive dropped (2026-10-12; build report
build/fine-detail-step3b-stations.md, last section). (1) Richard, nineteenth look: "We don't need the STAY SEATED at
the top DQ station": the board (post / rods / panel / label and its atlas rect) is gone from the DQ top in BOTH
FINE_DETAIL modes - D1_HANG_BOARDS / D1_LABELS / D1_TRAVEL hold RAISE BAR -> UNLOAD, D11 `forbid`s any STAY SEATED
in any mount; the base is untouched (WAIT HERE -> carpet -> load line -> LOWER BAR -> SKI TIPS UP), and so is the
gondola. `--no-fine` is still the approved pre-pass station byte for byte EXCEPT the DQ top, which also loses this
board. (2) With no `lamp_` part left (fine DQ: the box lamps are vanilla rows) the `_Emissive` map is all black, so
the bake no longer writes it (has_emissive_map: the lodge.py rule): the DQ folders ship four maps, a stale
`<name>_Emissive.png` is removed before the bake, `detail1.emissive` / `contract.emissive` say "no map" and the
importer writes no EmissiveProperties (the surgery has no purpose-72 step for the DQ stations). A build with a
`lamp_` part (the gondola's lamp_eave*, DQ `--no-fine`) keeps the map and its manifest blocks exactly as before.

Lift-board redraw (2026-10-12; build/lift-boards-redraw.md; Richard, twenty-second look: the LOWER BAR rider "is
getting his head slammed by the bar", the SKI TIPS UP skis "look like they have blocks on them"): every label this
module paints (cs2_liftdress.paint_board through stamp_labels) is redrawn in the real North-American signage style -
a yellow 2:3 placard (black border, circle pictogram, bold caps, two bolts) on the dark backer the 0.70 x 0.55 hang
boards are; WAIT HERE yellow, LIFT OPS blue, SLOW orange, seat marks blue. TEXTURE ONLY: the label quads, their
sheet rects and every UV are unchanged, so the FBX geometry is byte-identical; SKI TIPS UP now reads KEEP TIPS UP
(the D11 record follows cs2_liftdress.BOARDS). T1's 0.40 m pictogram is no longer claimed for the hang boards (real
size placards: read_m >= 25 m, T2 kept).

Nitpick A (2026-10-13; Richard's twenty-fifth look S-1 / S-2; build/nitpick-a-stations.md). (1) ORIENTATION: both DQ
stations are laid out round ONE up lane (D1_TRAVEL lane: the base loads on +Y, the top unloads on -Y - the same
physical lane, the right one looking uphill in the authoring frame); the top's operator cabin stood on +Y, across the
bullwheel from its own unload, and now stands on the -Y apron before the unload line (cab_layout / TOP_CAB: on the
deck, no opbase; LIFT OPS, door lite, sill, wall-light row, extinguisher and reel moved with it; TOP_CAB_LEGACY =
the old +Y cabin). Contract O1 gates it on the built faces (cabin, hang boards, load carpet + line / unload stripe on
the up lane's side; the boards facing the riders). The top's prefab x max follows the mesh: 7.97 -> 5.50 (the cabin
left the +Y side; recorded deviation). (2) SHEAVE TRAINS: the top's nose rope guides (DQ and gondola) are the
towers' gantry train (cs2_sheaves.dq_train, extracted from lift_tower; NOSE_TRAIN, build_nose_train), the rope
tangent on the sheave tops; contract ST1. The bases have no rope-guide group of their own (the hosted ETDQ / ETGDO
tower carries the entry train - a lift_tower build).

Nitpick A2 (2026-10-13; orchestrator ruling "Nitpick A follow-ups" (2) on nitpick-a-check.md item 9;
build/nitpick-a2-hanger-gondola.md): the gondola stations get the DQ stations' rule - the operator room stands on the
station's UP lane (the base's departing +Y, unchanged; the TOP's ARRIVING -Y). The top's room (floor, walls, door,
inner glazing band `oprglass`, ceiling slab), its LIFT OPS board, the operator-door wall-light row and the hose-reel
row move to -Y; the back corner they leave takes the stair and the deck's stairwell: the whole back-corner pair is
mirrored across y = 0 (GD_SIDE_MIRROR, an exact mirror of each part; the boards are rebuilt at the mirrored place so
their labels read right), the platforms' walkway ends follow (gd_platform_layout), the room's `_Win` panes are the -Y
wall's (gd_hall_layout). GD_OPR_SIDE / gd_opr_side; GD_OPR_LEGACY = the old top (room on +Y). Contract G11
(assert_orientation_gd) gates it on the built faces and the rows; every frozen number holds (wheels, hang 4.50, gauge,
rope 4.85, stubs, hosted ETGDO, lot 3 x 4, bounds).

Nitpick A3 (2026-10-13; orchestrator rulings D1 / F1 / F2 on build/nitpick-a2-hanger-gondola.md;
build/nitpick-a3-cabin-nose.md). (D1) The DQ top's nose train sits with its AXLE ON THE LANE, the towers' convention
(NOSE_AXLE_ON_LANE["DQ"]): the code lane / modelled rope runs through the sheave centres as on every DQ tower and the
hosted ETDQ (dq-family-review-geometry F10), so the mirrored DQ chair's grip clears the nose mains as it clears the
towers' (11.5 mm; the rope-on-the-rims train put the axle 0.278 m under the lane and the grip's wheels through the
mains). ST1 reads the rule per head: DQ = rim centres on the lane (1 mm), the gondola's GD head keeps the rope tangent on
the rims (unchanged). NOSE_AXLE_LEGACY = True rebuilds the nitpick-A2 DQ top. (F1) The gondola cabin is mirrored to
OUTBOARD -Y (lift_cabin.GD_OUTBOARD); its pose here (load_gd_cabin: G8, the previews; G1 / G2 / GD_ENV are written
outboard-positive) reads catalog/carriers.json gd8 "outboard": u = sign * raw x (outboard +), t = sign * raw z - a
proper rotation that faces the -Y cabin along its travel (the mover's yaw) and leaves the approved +Y cabin's pose as it
was. G8 also checks the shipped cabin's stalk is on the side the catalog names. (F2) WAIT HERE stands at the LOADING
entry, the departing lane's +Y side, on BOTH gondola stations (GD_LOAD_SIDE; GD_WAIT_LEGACY = the old -Y board), gated
in G11. The DQ base and every frozen number are unchanged.

Nitpick A4 (2026-10-13; orchestrator ruling on build/nitpick-a3-check.md items 1c / 1d, N3; build/nitpick-a3-cabin-nose.md
"Nose overlaps"). The DQ top's nose lost its four real part clashes: the comm clamp / cable vs the guide beam (the beams
and their girder stubs move 0.08 m outboard, NOSE_BEAM_IN) and the hanger boxes vs the detach rails (the hangers move to
their yokes' x, NOSE_HANGER_AT_YOKE; cs2_sheaves.dq_train had left them at the tower's x 0, 11 m behind). NEW contract C14
(assert_nose_clash): in the nose zone (NOSE_CLASH_SET) every intersecting part pair is a declared contact (NOSE_CONTACTS,
the rope-through-the-sheave-centres F10 pairs among them) or an exact NOSE_KNOWN_OPEN finding, none contained in another
part's closed volume; ST1 gates the hanger x. The pitched-lane case is a documented limit (NOSE_LANE_PITCH_LIMIT, manifest
sheave_trains.lane_pitch_limit, S9 note). DQ top only; the DQ base, the gondola stations and every frozen number
(bounds, rows, tris, wheel) are unchanged. NOSE_A4_LEGACY = True rebuilds the nitpick-A3 DQ top byte for byte.
"""

import argparse
import contextlib
import json
import math
import os
import sys
import dataclasses
from dataclasses import dataclass, asdict

import bpy
import bmesh
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import cs2_asset  # noqa: E402
import cs2_bake  # noqa: E402
import cs2_compose  # noqa: E402
import cs2_kit  # noqa: E402
import cs2_preview  # noqa: E402
import cs2_submesh  # noqa: E402
import cs2_uv  # noqa: E402
import cs2_validate  # noqa: E402
import lift_terminal  # noqa: E402  (clear_scene, bevels, _Win split, compose hook)
import lift_tower  # noqa: E402  (world_bounds)

REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
GENERATOR = "lift_terminal_detach.py"

# --------------------------------------------------------------------------------------
# Carrier row (MUST-CHANGE 1: read, not [D])
# --------------------------------------------------------------------------------------

CARRIERS_PATH = os.path.join(REPO, "catalog", "carriers.json")
# dq-chair.md section 2 (table) and section 3 (post-pivot datums: seat top 0.400, footbar 0.000)
CARRIER_FALLBACK = {
    "dq": {"hang_m": 3.300, "grip_top_above_rope_m": 0.220, "lane_x_m": 2.6, "axle_m": 8.0,
           "seat_top_m": 0.400},
    "gd8": {"hang_m": 4.50, "grip_top_above_rope_m": None, "lane_x_m": 3.0, "axle_m": 10.0,
            "seat_top_m": 0.0},       # cabin: floor = lowest point
}
SEAT_OVER_DECK = 0.50          # [D] real 0.45-0.50 (contract 3.1); LOAD_CLEAR = this - seat_top_m
GONDOLA_STEP_IN = 0.05         # contract 13: floor -> deck step


def load_carrier(key, path=CARRIERS_PATH):
    """The carrier row; every field says where it came from (the json, else dq-chair.md)."""
    row = dict(CARRIER_FALLBACK[key])
    from_json = []
    if path and os.path.isfile(path):
        with open(path, "r", encoding="utf-8") as fh:
            data = json.load(fh)
        got = (data.get("carriers") or {}).get(key)
        if got is None:
            raise KeyError(f"{path}: no carrier row {key!r}")
        for k in ("hang_m", "grip_top_above_rope_m", "lane_x_m", "axle_m", "seat_top_m"):
            if k in got:
                row[k] = got[k]
                from_json.append(k)
    rel = os.path.relpath(path, REPO).replace("\\", "/") if path else "-"
    rest = [k for k in ("hang_m", "grip_top_above_rope_m", "lane_x_m", "axle_m", "seat_top_m")
            if k not in from_json]
    row["source"] = (f"{rel} [{', '.join(from_json)}]" if from_json else "") +         ("; " if from_json and rest else "") +         (f"dq-chair.md section 2/3 [{', '.join(rest)}]" if rest else "")
    return row


# --------------------------------------------------------------------------------------
# Parameters (contract section 3, datums per MUST-CHANGE 1/2)
# --------------------------------------------------------------------------------------

RISE_RATIO = 5.449 / 21.87     # [K] Kagelok St Verre rise / width 0.2492
ROOF_SEGS = 28
ROOF_T = 0.12
ROOF_END = 0.15
BAND_T = 0.15
FILLET_R = 0.35
DECK_H = 0.30
ROPE_R = 0.055
RIM_W = 0.45
CORRIDOR = 1.10
LOT = (2, 4)                   # cells (16 x 32 m)
LOT_HALF = (16.0, 8.0)         # authoring X (prefab z) / Y (prefab x)
LOT_TOL = 0.5
WALL_T = 0.25                  # gondola
STONE_T = 0.05
STONE_H = 1.20
PORTAL = (3.5, 5.5)            # gondola portal width / height (sill 0.30 -> head 5.80)
# (the pre-verdict 6.0 x 0.9 m name band and its 2.5 px-per-cell floor are superseded by the
# Revision 1 sign band below: SIGN_*, sign_layout, sign_rects, stamp_signbands)
# Nose balcony half-width (top kind). F1 (build/dq-family-review-geometry.md): the hosted
# ETDQ's decks (|y| 0.695..2.15) and A-braces (|y| 1.53..1.72) reach x -8.61 in this frame;
# the pre-verdict base balcony (x_nose - 1.0 .. x_nose) was narrowed to |y| <= 0.60 for it.
# Revision 1 replaces that on the base with nose_access(): a landing that stops at x -8.55
# (|y| <= 1.30) + a gangplank between the deck strips (|y| <= 0.55, LAND_* / GANG_*_HOSTED).
# The top station hosts no tower and keeps its 3.6 m balcony. Gate: seam S8 in
# tests/test_dq_family_seams.py (overlap must be empty).
BALC_HALF = 1.80
# F9: LOD2 lands this far under the 500 cap (the top station sat at 500 = the validator cap)
LOD2_MARGIN = 10

# -- Revision 1 (Richard's first live review, verdicts 3-4, 2026-10-11; reference = the
# Collins lift at Alta + lpoa-04 / detach2 / img_6201). Build report section "Revision 1".
# (a) the outer housing ladder rises from the nose landing up the NOSE end face to the crown
#     (+0.4 grab rails), never at the back end;
# (b) ONE underside ladder, the front one (pedestal F); the rear one is gone;
# (c) a small GANGPLANK at the nose: grated walkway + handrails. Base: from a nose landing
#     that stops 0.06 m short of the hosted ETDQ (its inboard end is x -8.612) out between
#     the ETDQ deck strips (|y| < 0.695) to 0.05 m short of the tower's ladder landing
#     (x -10.15), open at that end; seam S8 must stay empty. Top: 1.0 m past the balcony
#     between the nose guide frames (|y| 1.94), closed by the U handrail;
# (d) NO railings on the LEFT flank looking from the back (authoring -Y, the side with no
#     loading / unloading flow): the queue corral moves to the operator / loading side (+Y),
#     every rail_ / post_ / kick_ vertex has y >= LEFT_RAIL_Y (asserted, R1d);
# (e) the low-res "S 4" name band becomes a SIGN BAND per side: our peak-and-snowflake
#     emblem (ski_launcher_glyph cand_c, the shipped launcher glyph) + a lift-number plate
#     (light plate, dark digits 0.70 m tall; vector outlines since Revision 1b, the 5x7
#     bitmap read as a staircase), on ONE quad per side whose texels sit in a
#     RESERVED strip at the top of the atlas (pinned, density-protected, 448 px/m at 2048 =
#     4.0-4.2x the old band's 106 / 111 px/m);
# (f) two WINDOWS per housing side at operator level (rounded 1.30 x 0.95 panes, Collins
#     proportions), a `_Win` room of their own (cell (3, 2) = 13) + a dark gasket ring.
LAND_D_HOSTED, LAND_HALF_HOSTED = 0.55, 1.30   # base nose landing (x_nose - 0.55 .. x_nose)
GANG_END_HOSTED = -10.10       # base gangplank outer end (ETDQ landing plates from -10.15)
GANG_HALF_HOSTED = 0.55        # inside the ETDQ deck strips' inner edge 0.695
GANG_L_TOP, GANG_HALF_TOP = 1.00, 0.90         # top: past the 1.0 m balcony, inside |y| 1.94
NOSE_LADDER_Y = -0.70          # off the comm bracket (|y| <= 0.15) and the cable (+Y side)
NOSE_LADDER_OFF = 0.22         # rails 0.22 in front of the nose face (roof rim at 0.15: 0.04 gap)
LEFT_RAIL_Y = -2.0             # (d): no rail / post / kick vertex below this y
SIGN_H = 0.875                 # emblem square side = plate height (m)
SIGN_PAD = 0.0875              # plate margin = one digit cell
SIGN_CELL = (SIGN_H - 2.0 * SIGN_PAD) / 7.0    # 5x7 grid cell 0.10 -> text box 0.70 x 0.50 m/digit
SIGN_GAP = 0.20                # emblem -> plate
SIGN_PX_PER_M = 448.0          # at the 2048 shipping atlas; scales with the atlas
SIGN_MIN_X_OLD = 4.0           # >= 4x the old band (111 px/m, top) -> 444 px/m at 2048
SIGN_OLD_PX_PER_M = 110.7      # pre-verdict manifest: 664 px / 6.0 m (top), 635 / 6.0 (base)
SIGN_STRIP_TOP_PX = 8          # strip top margin at 2048 (texels)
SIGN_STRIP_LEFT_PX = (16, 1040)                # per-side rect u0 at 2048 (n, p)
PLATE_LIGHT_SRGB = (0.90, 0.90, 0.86)          # = lift_cabin / lift_chair number plates
PLATE_DARK_SRGB = (0.08, 0.08, 0.09)
PLATE_BORDER = 0.025
# Revision 1b (polish): vector digits and the plate edge (revision1-check-stations 3e / 4e)
DIGIT_FONT = "Bfont (builtin)"  # bpy FONT default, as welcome_sign's header (family unverified)
DIGIT_SS = 8                   # point samples per texel axis: 64 coverage levels on the edge
DIGIT_AA_LEVELS_MIN = 8        # partial-coverage levels a vector edge must show (a bitmap: 0)
SIGN_BEVEL_M = 0.016           # rounded edge round the whole panel (the quad is 0.012 proud) ...
SIGN_BEVEL_H_M = 0.005         # ... 5 mm high: profile 1 - (1 - d / bevel)^2, 32 deg at the edge
PLATE_STEP_M = 0.008           # the number plate's own edge ...
PLATE_STEP_H_M = 0.002         # ... standing 2 mm proud of the panel
SIGN_BEAD_INSET_M = 0.026      # a pressed bead (embossed rim) round the emblem panel: centre inset,
SIGN_BEAD_W_M = 0.010          # width (cosine profile) and ...
SIGN_BEAD_H_M = 0.0015         # ... height; the glyph keeps >= 60 mm from the panel border
SIGN_EDGE_AO = 0.14            # emblem panel occlusion at its border (BaseColor x 0.86) ...
SIGN_EDGE_AO_M = 0.045         # ... fading (smoothstep) to none 45 mm inside (clear of the glyph)
WIN_W, WIN_H, WIN_R = 1.30, 0.95, 0.14         # housing window pane (Collins ~1.37 : 1)
WIN_CORNER_SEGS = 3
WIN_FRAME = 0.05               # dark gasket ring width
WIN_DX = 3.0                   # pane centres at x_mid +- WIN_DX
WIN_CELL_HOUSING = (3, 2)      # room id 13 (the operator cabin keeps (2, 1) = 7)
SIGN_Z_C_ABOVE_FILLET = 1.05   # sign / window centre line = z_soffit + FILLET_R + this
# -- Revision 2 (Richard's second look, verdicts-2026-10-11.md "Second look" 3', 2026-10-11):
# "front ladder good; it must EXTEND TO THE GROUND". The nose ladder keeps its line (x_nose -
# 0.22, y -0.70) and continues straight down the nose face to a concrete footing at z 0, on
# the landing's 0.30 rung grid; it passes the landing through a LADDER-WAY (the landing is
# four gratings round the opening; the Revision 1 handrail stands on the outer rim and guards
# it) and is clamped under the landing. No safety cage: none of the reference ladders has one
# (collins lift 2.jpg: the tower-1 ladder; detachable quad base station.jpg: the inclined
# ground ladder to the nose platform). Asserted as R2a-R2d in assert_revision2.
NZ_FOOT_H = 0.10               # footing top = rail foot (open-bottom ground part lfoot_nz)
NZ_FOOT_X = (-0.35, 0.12)      # footing x about the rail line: -0.35 on the climbing side (stays
#                                inboard of the hosted ETDQ's inboard end -8.612), +0.12 (0.10
#                                short of the deck's nose face)
NZ_FOOT_HALF_Y = 0.40          # |y - ladder y|: 0.85 m wide footing (C8 bench edge at |y| 1.25)
NZ_PITCH = 0.30                # = _ladder's rung pitch
NZ_FIRST_RUNG_MIN = 0.20       # lowest rung >= this above the footing (lands at 0.35 above grade)
WELL_HALF_Y = 0.38             # ladder-way |y - ladder y| (rails at +-0.28, r 0.03: 0.07 clear)
WELL_FRONT = 0.08              # ladder-way nose-side edge past the rail line (+X)
WELL_DEPTH_MAX = 0.62          # ladder-way depth in front of the rungs where the landing allows
#                                (detail pass 1, MC3: 0.60 -> 0.62 so the top meets CLIMB_MIN)
WELL_RIM = 0.08                # landing rim kept outboard of the ladder-way (handrail posts at
#                                l0 + 0.03, r 0.028; the base's rim is what limits its depth)
ETDQ_INBOARD_X = -8.612        # hosted ETDQ inboard end in this frame (S6 / S8, review F1)
# -- Revision 1c (revision2-check-dq-stations item 6, 2026-10-11): on the BASE the straight ground
# flight dead-ended under the landing - 0.25 m from the rungs to the landing's outer rim, 0.28 m to
# its handrail (29 CFR 1910.23(d): 0.76 m on the climbing side, 0.61 m at an unavoidable
# obstruction). No rung line on the nose face can get that room: at landing height the gangplank
# (|y| <= 0.55) and the hosted ETDQ's deck strips (|y| 0.695-2.15, from x -8.612) sit 0.33-0.39 m
# behind it, and both stay (S8). So the base's ground flight moves SIDEWAYS to the landing's +Y edge
# and turns 90 deg: a THROUGH ladder (rungs along X, rails inside the landing's x span) whose climber
# stands outboard (+Y), faces the station centreline and steps between the rails onto the landing;
# the rails run on GF_EXT above it as the guard and grab rails at that edge (the rail gap). Its
# climbing side is open air between the landing and the +Y chair lane (C8 bench edge |y| 1.25).
# The landing's +Y wing is trimmed to the step-across, the ladder-way closes and the front (nose)
# ladder stands on the landing again (Revision 1: landing -> crown + 0.4). The top station keeps its
# Revision 2 straight run (0.60 m to the ladder-way edge: CLIMB_TOP_FLOOR, frozen this round).
# R2c GATES the climbing room (climb_room): station mesh, the hosted ETDQ keep-out and the C8 boxes.
GF_ON_BASE = True              # False = the Revision 2 straight run on the base (the test negative)
GF_Y = 0.60                    # base ground flight: the rung line's y (rails along X)
GF_X_IN = (0.02, 0.05)         # rail centres inside the landing's outer (-X) / nose (+X) edges:
#                                c/c 0.48, 0.42 clear (OSHA's 16 in minimum, unverified); the nose
#                                band (x >= x_nose) 0.02 behind the +X rail's surface
GF_STEP = 0.18                 # step-across: rung line -> the landing's trimmed +Y edge (7 in)
GF_EXT = 1.07                  # rails above the landing (42 in), no rungs above it (step-through)
GF_FOOT_Y = (-0.10, 0.37)      # footing about the rung line (the climber stands on +Y)
GF_FOOT_X = (0.05, 0.04)       # footing past the rail centres (-X; +X stays 1 cm off the deck face)
CLIMB_MIN = 0.61               # climbing room, rung centreline -> nearest obstruction (24 in)
CLIMB_TOP_FLOOR = 0.61         # was 0.60 (the top's no-regression floor); detail pass 1 (MC3):
#                                one rule, the top's nose ladder is gated at CLIMB_MIN as well
CLIMB_HEAD = 1.90              # climbing zone above the step-off rung (a climber standing on it)
# the hosted ETDQ in this frame for the climbing room (lift_tower DQ_DECK_Y0 / DQ_DECK_Y1 deck
# strips from its inboard end outward, deck top 3.70 - 0.296 under rails 1.10 high; S8 in
# tests/test_dq_family_seams.py is the geometry gate)
ETDQ_KEEPOUT = dict(x=(-14.0, ETDQ_INBOARD_X), y=(0.695, 2.15), z=(3.30, 4.55))
# MUST-CHANGE 2: SLOT_W >= 0.90 (hanger column |dy| <= 0.45 both sides). 1.10 because in the
# turn the column's +-0.50 travel extent puts its corners at r 3.09 / 2.21 about the wheel and
# the soffit arcs are 16-gon chords (inscribed): 3.15 * cos(5.625 deg) = 3.135 > 3.09.
SLOT_HALF = 0.55

# -- Gondola stations, draft 1 (2026-10-11; build report section "Gondola stations, draft 1").
# The --gondola variant is a walled HALL round the detachable machine: two cabin portals in the
# nose wall cut to the cabin SILHOUETTE (body + hanger slot, >= GD_CLEAR to SkiGondolaCabin8A at
# both pin frames), a boarding platform per lane at the cabin-floor height with a safety edge
# stripe, the hall floor (cabin trough) at grade, an operator room in the +Y back corner (`_Win`
# cell 7, panes on the +Y wall), two hall windows per side wall (`_Win` cell 13), a machinery deck
# over the bullwheel (drive / tension stand on it) reached by an INTERNAL two-flight stair from the
# -Y platform, the roof crown by an EXTERNAL nose ladder from a ground footing (the DQ Revision 2 /
# 1c rules: z 0 footing, 0.30 rung grid, climbing room >= 0.61 m gated) plus a roof ladder over the
# curve; the sign (emblem + vector "8", 448 px/m) on the PORTAL face and on both side walls (the
# two side quads share the "p" reserved rect), lamps for the night read, the stone base all round.
# The glazed back curtain stays (D10). Authoring frame as above; on a lane OUTBOARD = away from the
# centreline = the cabin's door + hanger side = the carrier's RIGHT facing its travel (nitpick A3: the
# cabin ships with them on its authoring -Y, carriers.json gd8 outboard; load_gd_cabin reads the sign).
GD_W_HALF = 6.25               # outer wall faces (interior +-6.00; platforms 1.58 m wide)
GD_Z_TOP = {"base": 8.00, "top": 7.60}     # eave: machinery deck 5.60 + >= 2.0 m headroom
GD_WHEEL_BACK = 4.95           # wheel -> back wall outer face; inner face 4.70 (turn r 4.19 + 0.51)
GD_CLEAR = 0.30                # cabin -> every non-machine part (C8 boxes, the real-cabin sweep G8)
GD_FLOOR_Z = 0.03              # hall floor / cabin trough (grade + 3 cm): 0.32 under the cabin
GD_PLAT_IN = 4.42              # boarding platform inner edge |y| (cabin outboard 4.094 + 0.30 + slack)
GD_WALK_IN = 4.90              # walkway inner edge |y| past the turn start (turn boxes reach 4.64)
GD_PLAT_END = -1.60            # the boarding strip ends at x_wheel + this (the turn starts)
GD_STRIPE_W = 0.12             # safety edge stripe (fixed yellow) along the boarding edge
GD_DECK_Z = 5.60               # machinery deck top (grip top 4.93 + 0.30 = 5.23 < beams 5.30)
GD_DECK_X0 = -2.20             # deck front edge at x_wheel + this
GD_BEAM_Z0 = 5.30              # deck beams 5.30 .. 5.54 (detach rail top 5.265 below)
GD_BEAMS = (-2.00, -0.90, 0.90, 2.20)      # x_wheel + these (off the rail hangers)
GD_STAIR_W = 0.85              # = x_back - WALL_T - (x_wheel + GD_STAIR_X[1])
GD_STAIR_X = (1.20, 3.85)      # flight 1 foot / landing edge at x_wheel + these
GD_STAIR_Y2 = -2.55            # flight 2 top (deck edge) y; r 4.62 from the wheel at the corner
GD_RISERS = 13                 # per flight: 2.65 m in 13 risers of 0.204, 12 treads
GD_OPR_X0 = 2.45               # operator room from x_wheel + this to the back wall ...
GD_OPR_Y0 = 4.05               # ... and from |y| this to the +Y wall (corner r 4.73)
GD_OPR_H = 2.60
# Nitpick A2 (2026-10-13; module docstring): the operator room's authoring y sign per kind = the station's UP lane
# (the base's departing lane +Y, the top's arriving lane -Y: the DQ stations' D1_TRAVEL / OP_SIDE_Y rule, contract
# G11). The builders above stay written for +Y; on a -Y kind build_gd_interior mirrors the room AND the stair /
# stairwell it trades places with (GD_SIDE_MIRROR, exact mirrors across y = 0) and the platforms, panes, board, rows
# and contracts read the sign. GD_OPR_LEGACY = True rebuilds the pre-2026-10-13 top (room on +Y; the G11 negative).
GD_OPR_SIDE = {"base": 1.0, "top": -1.0}
GD_OPR_LEGACY = False
# G11: the station's UP lane, authoring y sign - the base's DEPARTING lane, the top's ARRIVING lane (the right one
# looking uphill in this frame: the DQ stations' D1_TRAVEL lane, nitpick A O1); GD_OPR_SIDE must equal it
GD_UP_LANE = {"base": 1.0, "top": -1.0}
GD_SIDE_MIRROR = ("opr_", "door_opr_", "oprglass", "stread_", "stringer_", "grate_stl", "stpost", "post_st_",
                  "rail_st_", "grate_dkb", "dkbeam_4", "post_dks_", "rail_dks_", "kick_dks_")
# Nitpick A3 (2026-10-13; orchestrator ruling F2 on nitpick-a2 F2): WAIT HERE stands at the LOADING entry = the
# DEPARTING lane's side, authoring +Y on BOTH gondola stations (the base loads the up lane, the top the down lane; both
# depart on +Y). The board keeps its part / label name `waitn` (its sheet rect is keyed by the label name).
# GD_WAIT_LEGACY = True = the pre-2026-10-13 board at the -Y (arriving) entry (the A-B proof and the G11 negative).
GD_LOAD_SIDE = 1.0
GD_WAIT_LEGACY = False


def gd_wait_side():
    """The authoring y sign of the gondola WAIT HERE board (both stations): the loading entry (+Y)."""
    return -1.0 if GD_WAIT_LEGACY else GD_LOAD_SIDE
GD_SIGN_NOSE_Z = {"base": 6.40, "top": 6.14}   # portal-face sign centre (below the nose band, above
#                                the slots); top 6.40 -> 6.14 in detail pass 1: G7 (now a ray test)
#                                found draft 1's top sign 0.24 m up behind the nose band (7.60 - 1.0)
GD_COMM_Y = 1.15               # comm cable drop line on the central pier (sign |y| <= 0.875)
# (draft 1's 4-pane hall band GD_HALL_WIN* is gone: revision 1 glazes the whole wall, gd_hall_layout)
GD_TRIM_W, GD_TRIM_T = 0.12, 0.06          # portal frame trim width / proud
GD_NL_Y = -5.40                # nose ladder rung line y (outer pier, clear of the hosted tower)
GD_NL_OFF = 0.22               # rails in front of the nose face (roof rim at 0.15: 0.04 gap)
GD_RL_X = (0.08, 0.64)         # roof ladder rails at x_nose + these (0.56 apart)
GD_RL_LIFT = 0.12              # roof ladder rail axis above the roof outer surface
GD_RL_TOP_Y = -0.30            # roof ladder ends here, at the crown
# the hosted gondola entry compression tower (towers-transfer audit G2: NOT BUILT) - what the
# station leaves free for it: nothing but the haul ropes at x < x_nose - dx, |y| <= y, z < z
GD_TOWER_KEEPOUT = {"dx": 0.40, "y": 4.60, "z": 7.0}
GD_CABIN_FBX = os.path.join(REPO, "assets", "import", "cabins", "A", "SkiGondolaCabin8A",
                            "SkiGondolaCabin8A.fbx")
# The portal opening in lane coordinates (u outboard of the lane, z up): the cabin silhouette
# (GD_ENV inflated by GD_CLEAR at the rope-pin height 0.43 + 0.08) plus a margin: body sides
# u_lo / u_hi, shoulders r_sh round to the flat top z_body, a concave fillet r_cc into the hanger
# slot s_lo..s_hi, slot top z_slot with corners r_sl (gd_portal_outline)
GD_PORTAL = {"u_lo": -1.45, "u_hi": 1.50, "z_sh": 3.15, "r_sh": 0.40, "z_body": 3.55,
             "s_lo": -0.45, "s_hi": 1.10, "r_cc": 0.20, "z_slot": 5.35, "r_sl": 0.25}
# Carrier envelope of the gondola (S10 in tests/test_gondola_tram_seams.py checks it encloses every
# vertex of the shipped SkiGondolaCabin8A LOD0/1/2): (label, travel, lateral width, h0, h1, lateral
# centre outboard), cabin frame = h above the underside. Measured 2026-10-11 on the shipped LOD0 +
# LOD1 + LOD2 FBX vertices by height band (underside 0 .. grip top 4.50; the hanger stalk, carriage
# and grip sit OUTBOARD of the lane), +5 mm on every side.
GD_ENV = (("body", 2.370, 2.154, -0.005, 2.005, 0.0220),       # u -1.050..1.094, |t| 1.180
          ("roof", 2.270, 2.070, 1.995, 2.455, 0.0000),        # u -1.030..1.030, |t| 1.130
          ("hat", 1.530, 1.326, 2.445, 2.755, -0.0221),        # u -0.680..0.636, |t| 0.760
          ("hanger", 0.210, 0.258, 2.745, 3.905, 0.6251),      # u  0.501..0.749, |t| 0.100
          ("carriage", 1.110, 0.800, 3.895, 4.405, 0.3550),    # u -0.040..0.750, |t| 0.550
          ("grip", 0.310, 0.350, 4.395, 4.505, 0.1300))        # u -0.040..0.300, |t| 0.150

# -- Gondola stations, revision 1 (2026-10-11; Richard's eighth look, verdicts-2026-10-11.md: "go with
# the LONG GLAZED HALL and 3x4 lot ... maybe slightly bigger openings for the skiers to enter into";
# references: Peak 2 Peak (ref-liftblog/peak2peak 12 / 20 / 22 / 28) and the Aspen gondola - a long
# hall with floor-to-eave glazing between slim steel mullions on a stone plinth, a shallow standing-
# seam roof on deep eaves, the machinery behind the glass, the portals as the end gables). Build
# report: build/gondola-stations-rev1.md. --gondola only; every frozen number of draft 1 holds (hang
# 4.50, gauge +-3.0, wheel / stub, portals, platforms 0.30, deck + stair, operator room, ladders, G6).
GD_REV1 = True                 # False = draft 1's timber barn (kept for nothing: rev 1 replaced its
#                                builders; the switch only documents the gate)
GD_LOT = (3, 4)                # m_LotWidth 3 = 24 m across (prefab x = authoring Y), m_LotDepth 4 =
GD_LOT_HALF = (16.0, 12.0)     # 32 m along travel (prefab z = authoring X): authoring X / Y halves
GD_LOD0_TARGET = {"base": 12000, "top": 9000}  # ROUND-SPEC section 4 budgets (caps 13,000 / 10,000)
# T62: round 2 raised the top to 9,700 / cap 10,200 (224-tri wheels); round 3 (98-tri wheels: 8,661) restores 9,000 / 10,000
GD_EAVE = 1.20                 # deep eaves: roof edge 1.20 m past the long walls' outer faces
GD_ROOF_RISE = 0.95            # shallow segmental standing-seam roof: crown = eave + 0.95 (0 -> 14.5 deg)
GD_RAKE_BACK = 0.60            # back gable overhang (the nose keeps ROOF_END 0.15: the nose ladder)
GD_SILL = 1.26                 # stone plinth 1.20 + a 0.06 coping: the glazing starts here
# DQ follow-up (rev-1 checker F3, Richard's "slightly bigger openings"): the skier doors' head is the
# platform 0.30 + the 2.30 clear height = 2.60, and the first transom sits ON it (centre 2.64, underside
# 2.60); revision 1 had the transom centred on 2.60, its underside at 2.56 (2.26 m clear)
GD_DOOR_HEAD = 2.60           # = DECK_H + GD_SIDE_DOOR[1]
GD_TRANSOMS = (2.64, 5.60)     # door head transom / first tier top; the machinery-deck line (hides its edge)
GD_HEAD_DROP = 0.35            # side / back curtain head = ring beam underside at z_top - this
GD_NOSE_HEAD_DROP = 0.45       # nose ring beam underside (it carries the comm bracket at z_top - 0.30)
GD_LINTEL_Z = 5.95             # portal wall top: slot tops 5.35 + trim 0.12 + 0.48
GD_GLASS_IN = 0.04             # pane plane this far inside the wall line (behind the mullion fronts)
GD_MULL_W = 0.10               # slim steel mullions: 0.10 wide ...
GD_MULL_Y = (-0.12, 0.08)      # ... faces about the wall line (0.12 proud of the glass)
GD_BAY = 2.20                  # target mullion pitch
GD_SIDE_DOOR = (1.80, 2.30)    # skier entries: double glass doors, clear w x h (sill = platform 0.30)
GD_DOOR_BAY = 2.20             # the door bay between its two mullions (frame jambs 0.15 each side)
GD_NOTCH_HALF = 1.00           # plinth / coping / stone notch at a door: x_door +- this
GD_CANOPY = (3.00, 1.80, 2.80, 0.15)       # entry canopy: width, depth, underside z, thickness
GD_RAMP_L = 2.00               # entry ramp 0 -> 0.30 (15 %), 2.00 wide
GD_NOSE_BAYS = (-6.0, -4.5, -3.0, -1.15, 1.15, 3.0, 4.5, 6.0)   # nose glazing bays (|y| <= wi); the
#                                centre bay is the sign spandrel; +-1.15 carries the comm cable
GD_BACK_BAYS = (-6.0, -4.0, -2.0, 0.0, 2.0, 4.0, 6.0)
GD_SIDE_SIGN_Z = 4.10          # side signs: centre z on a plate across one bay (tier 2.60 .. 5.60)
GD_SNOW_IN = 0.55              # snow-guard rail this far in from the eave edge
GD_EAVE_LAMP = (0.18, 0.30)    # linear soffit lights (no vanilla equivalent): |y| - W2 range
# weathering (DECISIONS 171): the station preset; the gondola stands on grade (no deck datum)
GD_HIDDEN = ("rooflining", "ceil_", "detrail_", "railhang_", "tyre", "drive_", "tension_",
             "sheave_bull", "spoke_", "wheelhub", "wheelrope", "haulrope_")
GD_WEATHER = dict(
    ground="grade", ground_z=0.0,
    traffic_extra=("platf_", "floor", "stepside_", "grate_", "lfoot_", "stread_", "apron_"),
    overrides=tuple((p_, {"role": "none"}) for p_ in GD_HIDDEN) + (
        # the stone plinth: a light grime only - with the ground-contact band over half its 1.20 m it
        # led the VL4 budget (1.07 at 0.40 with grime 1.3, the DQ plinth's value)
        ("stonebase_", {"rust": 0.0, "grime": 0.5, "scale": 0.6}), ("coping_", {"rust": 0.0}),
        ("wall_", {"rust": 0.0}), ("door_", {"rust": 0.0}),
        # the slim painted frame: little edge wear (free slot wear lifts worn slot-1 edges above the 0.80
        # recolour albedo; on 0.08-0.10 m members the 3-texel edge band is the whole member)
        ("mull_", {"edge": 0.25}), ("transom_", {"edge": 0.25}), ("outrig_", {"edge": 0.3}),
        ("ringbeam_", {"edge": 0.4}), ("gable_", {"edge": 0.4}), ("canopy_", {"edge": 0.5}),
        ("lrail_", {"role": "galv", "edge": 1.5}), ("lrung_", {"role": "galv", "scuff": 1.0}),
        ("signplate_", {"scale": 0.6}), ("spandrel_", {"scale": 0.6}), ("label_", {"role": "none"}),
        ("board_", {"scale": 0.7}), ("pstripe_", {"scuff": 1.5})))

# -- Detail pass 1, lifts (DQ only; design/detail-pass-1-lifts.md sections 3 / 4 / 7 as amended by
# design/detail-pass-1-lifts-skeptic.md MC1-MC11, all applied; build report
# build/detail-pass-1-lifts-detach.md). Every switch below is DQ-only: the gondola keeps its tables.
DETAIL1 = True                 # the DQ dressing / labels / weathering / bay sharing (False = 1c look)
# MC4 (base roof route on 1c): the front ladder rises from the 0.55 m landing; the outer landing rail
# segment between post_gangn_1 and post_gangn_2 and its kick plate go (an open rail gap at the
# ladder: Richard's taste default), the ladder moves to y -0.875 with rails 0.48 apart so a 0.61 m
# corridor clears the two remaining rails by 0.042 m a side; the bottom stop's standing surface is
# the landing (0.33 m deep in front of the rungs) and, past a 0.062 m gap, the hosted ETDQ's -Y deck
# strip (ETDQ:grate_deckp, top 3.404 = the stop + 0.054)
ROOF_ROUTE_D1 = True           # False = the 1c front ladder (y -0.70, the closed outer rail): R2e fails
NOSE_LADDER_Y_D1 = -0.875      # base front ladder rung-line centre y (MC4)
NOSE_LADDER_HALF_D1 = 0.24     # rails 0.48 apart (0.42 clear, OSHA 16 in, unverified)
GANG_KICK_X0_D1 = -8.88        # the gangplank's -Y kick plate starts here (outside the 0.61 zone)
# MC5: the pedestal (pf) ladder and its soffit hatch move off spine_main (|y| <= 0.30, z 2.75-3.35)
# to |y| 0.77 (rails 0.49 / 1.05, inside C7's 1.10, 0.20 inside the C8 bench edge 1.25); rungs on a
# 0.30 grid from 0.30 above the deck to 0.05 under the hatch (lrung_pf09 is no longer in the spine);
# the climber stands on +X; the hatch opening is cut into soffit_tongue and closed by its leaf
PF_OFF_SPINE = True            # False = the 1c pf ladder on the spine, no hatch: R2f fails
PF_Y = 0.77                    # pf rung-line centre y and hatch centre y
PF_X_OFF = {"base": 1.35, "top": 1.90}     # rung line at ped_f + this (top: the hatch frame 0.115 m
#                                            clear of beam_2t, the rails 0.245 m)
PF_HALF = 0.28                 # rails 0.56 apart (the 1c _ladder width)
PF_HATCH = 0.80                # hatch opening (cs2_liftdress.hatch_frame size, frame 0.06)
PF_HATCH_DX = 0.30             # hatch centre x = rung line + this (the opening spans the climber)
# MC3: ONE reachability rule, the house numbers (C15 / 1c climb_room): climbing depth AND corridor
# width >= 0.61, the C8 chair envelopes count, the zone runs from the lowest rung to the terminus +
# 1.80 (a climber standing at the stop); the top's ladder-way grows 0.60 -> 0.62 (WELL_DEPTH_MAX) so
# its nose ladder meets 0.61 as well (the 0.60 yardstick exception is gone)
CLIMB_WIDTH = 0.61             # corridor width across the rails
CLIMB_TERMINUS_HEAD = 1.80     # the zone reaches this far above the last stop
CLIMB_HEADROOM = 2.03          # stair headroom (house number; the gondola stair, G4)
# Dressing labels in the reserved strip (MC: signband_p shares signband_n's rect, skeptic row 7):
# shelf-packed (cs2_liftdress.pack_label_sheet) at LABEL_PPM into this region at 2048 (x0, y0, x1,
# y1 texels; 4-texel aligned so every rect is whole texels at the 512 smoke atlas too)
LABEL_PPM = 400.0
LABEL_REGION_PX = (808, 1648, 2040, 2040)
LABEL_GUTTER_PX = 8
DQ_SIGN_SHARED = {"p": "n"}    # the +Y sign band reuses the -Y band's rect (one stamp, one relief)
# Detail pass 1 density weights (design 3.7 + cs2_liftdress.DRESS_DENSITY), merged over
# DENSITY_WEIGHTS for the DQ only
DQ_D1_DENSITY = {"deck": 0.6, "rampback": 0.6, "ped_": 1.3, "crosshead_": 1.3, "spine_": 1.3,
                 "lamp_": 1.0, "doorlite_": 0.8, "sill_": 0.6, "label_": 1.0}
# the LOD0 budget targets of this pass (ROUND-SPEC section 4: 11,000 / 8,000; caps 12,000 / 10,000)
# T62: round 2 raised the top to 8,500 (224-tri wheels); round 3 (98-tri wheels: 7,453) restores 8,000
DETAIL_LOD0_TARGET = {"base": 11000, "top": 8000}
# weathering (DECISIONS 171; design 3.6 corrected by MC7): hidden machinery role none, the deck top
# is the ground line (the DQ lot does not level), the station's own overrides first
DQ_HIDDEN = ("rooflining", "bandlining_", "ceil_", "detrail_", "railhang_", "tyre", "drive_",
             "tension_", "sheave_bull", "spoke_", "wheelhub", "wheelrope", "haulrope_")
DQ_WEATHER = dict(
    ground="grade", ground_z=DECK_H,
    traffic_extra=("deck", "rampback", "opbase", "grate_", "carpet_", "lfoot_"),
    overrides=tuple((p_, {"role": "none"}) for p_ in DQ_HIDDEN) + (
        ("pad_", {"role": "pad", "scuff": 1.0}), ("carpet_", {"role": "pad", "scuff": 1.5}),
        ("plinth_", {"rust": 0.0, "grime": 1.3}), ("door_", {"rust": 0.0}),
        ("lrail_", {"role": "galv", "edge": 1.5}), ("lrung_", {"role": "galv", "scuff": 1.0}),
        ("maze_", {"scale": 0.5}), ("skirack_", {"role": "galv"}), ("stripe_", {"scuff": 1.5}),
        ("chev_", {"scuff": 1.0}), ("ext_", {"scale": 0.5}), ("label_", {"role": "none"}),
        ("board_", {"scale": 0.7})))
# 2x close-ups of this pass (assets/preview/stations/detachable/A/<this folder>)
D1_CLOSEUP_DIR = "detail1-closeups-2026-10-11"
D1_CLOSEUP_RES = (2600, 2000)
# -- DQ follow-up (2026-10-11; Richard's eleventh + fourteenth looks; build/dq-stations-followup.md) ------
# (1) "Lights on top of posts are looking a little square - there should be in-game vanilla lamp posts
# we could use": the detail pass's two modelled 8 m flood masts per station are GONE; four VANILLA
# LightpoleParkRandom01 rows per station stand on the deck instead (cs2_liftdress.SUBOBJ, GUID verified
# against the dump; union bounds 0.53 x 3.27 m, the arm + head 0.83 m toward the prop's local +Z =
# `facing`; parent 0). Every footprint is box-tested in assert_detail1 (D8: mesh, dressing, C8 chair /
# rider envelope, the hosted ETDQ keep-out, the other rows). The +Y edge strip (deck y 4.50 .. 5.50, the
# band above |y| <= 4.50 from z 3.35) only takes posts whose arm runs ALONG the edge (+-X); the -Y apron
# (deck to y -7.50) takes arms toward the station from its OUTER side. Follow-up resume (2026-10-12): the
# two +Y-facing posts first stood at y -5.40, which put their heads 0.09 m off band_n's outer face (true
# face distance; D8 tested overlap only) - they stand at y -7.10 now, heads over the apron 1.2 m clear of
# the band, out of the unloading skiers' path; D8 gates every lamp head (D1_LAMP_HEAD_*).
# T17-DQ 2026-10-16: lamp posts -> SkiPisteFloodlightLow (Richard: "same swap on the DQ stations, the lamppost
# just doesn't work on the lift stations"; precedent build/T17-T23-T25-dressing.md, the FG stations). The four
# vanilla LightpoleParkRandom01 rows per station (arm-direction facings, D8 head gates) are GONE; at most TWO
# SkiPisteFloodlightLow rows stand per station (no floodlit stadium), at the old spots of the two posts nearest
# the loading platform (base) / the unload apron (top). The rows are OUR OWN prop ("mod" rows: CID, no SUBOBJ
# placeholder), emitted by d1_flood_rows into the manifest; dq_subobjects no longer lists any lamp. The prop's
# aim is its local +Z with a 25-degree down-tilt baked in, so a row chooses only the yaw (d1_flood_yaw).
# Authoring (x, y) of each post, deck top. Dropped: base (13.75, 5.20) / (13.75, -5.20) (the back corners,
# plaza / maze side); top (9.60, -5.30) (over the exit ramp) / (-6.50, 5.10) (the departure side).
D1_LAMPS = {
    "base": ((2.40, 5.10),       # loading area, +Y front corner (the load line is x 2.85)
             (5.20, -7.10)),     # -Y apron's outer side across the lanes from the load point
    "top": ((-9.60, -5.30),      # unload apron, front corner (the old tN mast spot)
            (0.00, -7.10)),      # unload apron's outer side at the unload line
}
D1_FLOOD_PREFAB = "SkiPisteFloodlightLow"
D1_FLOOD_CID = "6827e281427c087a3b7ef19c824ca28b"        # the 8 m variant (SkiPisteFloodlightLow.Prefab.cid)
D1_FLOOD_MESH = "SkiPisteFloodlightLowA Mesh"
D1_FLOOD_REACH = 17.0          # m: where the 25-degree beam lands ahead of the 8.03 m light point (8.03 / tan 25)
D1_FLOOD_STEP = 5.0            # degrees: the cross-platform angle is rounded to this


def d1_flood_yaw(gx):
    """The floodlight's yaw (degrees about game +Y; direction (sin, cos) in game (x, z)) for a post at game
    local x `gx`: local +Z points to the lift line (game -Z, yaw 180) turned toward the line axis (prefab x 0)
    by atan(|gx| / D1_FLOOD_REACH) rounded to D1_FLOOD_STEP, so the beam lands on the axis about 17 m ahead
    (FG precedent: x 3.4 -> 10 degrees, yaw 190). A post at x < 0 aims toward +X (yaw < 180), x > 0 toward -X."""
    a = D1_FLOOD_STEP * round(math.degrees(math.atan2(abs(gx), D1_FLOOD_REACH)) / D1_FLOOD_STEP)
    return 180.0 if gx == 0 else (180.0 - a if gx < 0 else 180.0 + a)


def d1_flood_rows(p):
    """The SkiPisteFloodlightLow "mod" rows of this station (fine_detail._row shape, game local =
    blender_to_game, m_ParentMesh 0): the DQ stations' D1_LAMPS spots on the deck top, the gondola stations'
    portal-face corner pair (gd_flood_spots)."""
    LD = _LD()
    if p.gondola:
        spots = [(x_, y_, 0.0) for x_, y_ in gd_flood_spots(p)]
    else:
        spots = [(x_, y_, DECK_H) for x_, y_ in D1_LAMPS[p.kind]]
    rows = []
    for pos in spots:
        g = LD.blender_to_game(pos)
        yaw = d1_flood_yaw(g[0])
        h = math.radians(yaw) / 2.0
        rows.append({"kind": "mod", "ref": "CID", "m_Object": D1_FLOOD_CID, "prefab": D1_FLOOD_PREFAB,
                     "mesh": D1_FLOOD_MESH, "position": tuple(float(v) for v in g),
                     "rotation": (0.0, round(math.sin(h), 7), 0.0, round(math.cos(h), 7)), "m_ParentMesh": 0,
                     "note": f"replaces LightpolePark; yaw {yaw:g} (beam to the line, across to the axis)"})
    return rows
# D8 lamp-head gate (C16-style, the tram's check_subobjects + a head standoff): the head zone of each
# LightpolePark union box - from D1_LAMP_HEAD_REACH along `facing` to the arm end, the top
# D1_LAMP_HEAD_DROP m - keeps >= D1_LAMP_HEAD_CLEAR m from every station face (a lamp head is not bolted to
# the fascia; its light is not blocked by it); measured on 2 cm surface samples (BVH find_nearest)
D1_LAMP_HEAD_REACH = 0.30
D1_LAMP_HEAD_DROP = 0.60
D1_LAMP_HEAD_CLEAR = 0.30
# The SLOW boards stay separate (a real resort does not hang its SLOW sign on a lamp post): each on TWO
# short galv posts of its own, board 1.20 x 0.35 with its bottom D1_SLOW_Z0 above the deck. Base: the
# -Y back corner beside the lamp post, facing the arrivals from the back ramp (+X); top: the -Y apron's
# back end, facing the unloaded skiers on their way to the back ramp (-X).
D1_SLOW = {"base": ((13.88, -6.60), "+X"), "top": ((9.50, -6.70), "-X")}
D1_SLOW_Z0 = 0.90
# (2) Board ORDER ("on one of the stations we have an UNLOAD HERE sign immediately followed by a STAY
# SEATED sign": the top's STAY SEATED hung at x 2.20, past the unload line x 0.0). The hang-slot boards
# per station: (board, x, lane side, facing). Base: the chair travels -X on the +Y lane (it leaves the
# carpet toward the nose); top: +X on the -Y lane (it arrives from the nose, riders unload at x 0.0).
# 2026-10-12 (Richard, nineteenth look: "We don't need the STAY SEATED at the top DQ station, doesn't quite
# make sense"): the top has NO STAY SEATED board (post / rods / board / label / atlas rect all gone); its
# sequence is RAISE BAR -> UNLOAD. The old slot x -2.50 (between beam_2t x -3.62 .. -3.38 and beam_3t
# -0.88 .. -0.62) is simply empty; D11 fails any hanging board that is not in D1_TRAVEL's sequence.
D1_HANG_BOARDS = {
    "base": (("lowerbar", 0.60, 1.0, "+X"), ("tipsup", -1.50, 1.0, "+X")),
    "top": (("raisebar", -7.60, -1.0, "-X"), ("unload", -0.20, -1.0, "-X")),
}
# D11 (assert_board_order): what a rider meets along the chair's travel, in order, measured on the
# built faces; the base adds the walk to the chair (WAIT HERE at the maze gate, the loading carpet, the
# load line). `end`: that board stands within D1_AT_LINE m before its line and nothing hangs on the
# lane past the line (nothing contradictory beyond the unload point).
D1_TRAVEL = {"base": dict(dir=-1.0, lane=1.0, seq=("waithere", "carpet", "load", "lowerbar", "tipsup")),
             "top": dict(dir=1.0, lane=-1.0, seq=("raisebar", "unload"), end=("unload", "stripe_unload"),
                         forbid=("stay",))}
# `forbid`: board kinds the station must not carry in ANY mount (hanging, on a post, on a wall): a part named
# board_<kind>* / label_<kind>* raises D11 (the DQ top: no STAY SEATED, Richard's nineteenth look)
D1_AT_LINE = 0.50
D1_UNLOAD_X = 0.0              # the top's unload line (stripe_unload across the -Y lane)
# follow-up close-ups (lamp posts as the review packs' labelled stand-in boxes + the board sequence)
FU_CLOSEUP_DIR = "followup-2026-10-11"
# -- Fine-detail pass, step 3b (2026-10-12; build/fine-detail-step3b-stations.md; see the module docstring) -----
# False = the approved pre-pass stations byte for byte (CLI --no-fine; main() sets it from its args every run) -
# except the DQ top, which has had no STAY SEATED board since 2026-10-12 in both modes
FINE_DETAIL = True
FD_CLOSEUP_DIR = "fine-detail-2026-10-12"
FD_LID_LIP = 0.015             # control cabinet lid: overhang past every free side ...
FD_LID_T = 0.02                # ... and thickness (a lid lip instead of the finish bevel strips)
FD_MOUNT_GAP = 0.01            # F1: a kit prop's mount face within this of its host face (deck / wall)
FD_CLEAR_OFF = 0.01            # F1: the real kit mesh, backed this far off its mount, may touch no face
FD_KIT_ROOT = os.path.join(REPO, "assets", "import", "props", "B")
#: the DQ base's hose reel (r 0.22 on the engine room's -X face, 0.62 m between the door jamb and the corner)
#: cannot take the kit's 0.70 m plate there; the engine room's -Y face is fronted by cabinet_0 / _1 (0.45 m off
#: it) and the cabin's -Y face by the queue maze (its back rope 0.20 m off it): no access (F1's access box). The
#: kit reel hangs on the operator CABIN's +X face (the open-ground side, beside the door), under the cabin
#: extinguisher - a fire point: plate centre at y FD_DQ_REEL[0], FD_DQ_REEL[1] above grade (0.65 over the
#: opbase; the DQ top's reel hangs at 0.92, 0.77 over it)
FD_DQ_REEL = (7.45, 0.80)
FD_ACCESS = 0.40               # F1: wall props keep this much free space in front of their face (no station face,
#                                no other row): a reel / extinguisher must be reachable
#: wall-rack loadouts alternate by station (FS6: dress-kit W1 = the 7 pairs + 2 boards the station racks held)
FD_WALL_RACK = {("dq", "base"): "SkiDressSkiRackWall", ("gd", "base"): "SkiDressSkiRackWallV2",
                ("gd", "top"): "SkiDressSkiRackWall"}
#: kit colour variants (Richard 2026-10-12 ~18:50: the accents stay "a colour that makes sense for that building";
#: build/kit-colour-variants.md): the colour set (fine_detail.COLOUR_SETS) of every COLOURED kit row of a station
#: = its slot-2 accent, the PAD orange of the crash pads (pad2) / GD_TINT G. The DQ top has no coloured row.
FD_KIT_COLOUR = {("dq", "base"): "orange", ("dq", "top"): "orange", ("gd", "base"): "orange",
                 ("gd", "top"): "orange"}


@dataclass(frozen=True)
class Params:
    variant: str
    kind: str
    gondola: bool
    carrier_key: str
    carrier_src: str
    lane_y: float
    rim_r: float
    torus_r: float
    rim_segs: int
    torus_segs: int
    hang: float
    grip_above_rope: object
    seat_top: float
    load_clear: float
    z_rope: float
    z_wheel: float
    line_axle: float
    z_soffit: object
    z_top: float               # band top (DQ) / eave (gondola) = roof eave
    z_ceil: float
    chord_half: float
    roof_rise: float
    z_crown: float
    w_half: float
    x_nose: float
    x_back: float
    length: float
    wheel_back: float
    x_wheel: float
    ped_f: object
    ped_r: object
    anchor_x: float
    slot_half: float
    notch_top: object
    lowest_abs: float
    env: tuple                 # ((label, travel, lateral, z0, z1) chair-local, z from lowest)
    lod0_cap: int
    lod1_max: object
    lod2: int
    hosted_tower: bool
    engine_base: tuple
    win_part: str
    folder: str
    stem: str
    clear: float = 0.0         # gondola draft 1: C8 boxes inflated by this (GD_CLEAR)


def params_for(kind, gondola, carrier):
    base = kind == "base"
    lane = float(carrier["lane_x_m"])
    hang = float(carrier["hang_m"])
    seat = float(carrier.get("seat_top_m") or 0.0)
    x_nose, x_back = (-8.0, 14.0) if base else (-10.0, 10.0)
    anchor_x = -11.0 if base else -12.0
    if not gondola:
        load_clear = round(SEAT_OVER_DECK - seat, 4)
        z_rope = round(DECK_H + load_clear + hang, 4)
        z_soffit = round(z_rope - 0.35, 4)
        z_top = round(z_soffit + (2.80 if base else 2.40), 4)
        chord_half, w_half = 4.60, 4.50
        rise = RISE_RATIO * 2.0 * chord_half
        wheel_back = 4.40
        x_wheel = x_back - wheel_back
        length = x_back - x_nose
        return Params(
            variant=f"dq-{kind}", kind=kind, gondola=False, carrier_key="dq",
            carrier_src=carrier["source"], lane_y=lane, rim_r=lane, torus_r=lane + 0.02,
            rim_segs=40, torus_segs=32, hang=hang,
            grip_above_rope=carrier.get("grip_top_above_rope_m"), seat_top=seat,
            load_clear=load_clear, z_rope=z_rope, z_wheel=z_rope,
            line_axle=float(carrier["axle_m"]), z_soffit=z_soffit, z_top=z_top, z_ceil=z_top,
            chord_half=chord_half, roof_rise=rise, z_crown=z_top + rise, w_half=w_half,
            x_nose=x_nose, x_back=x_back, length=length, wheel_back=wheel_back, x_wheel=x_wheel,
            ped_f=x_nose + 0.25 * length, ped_r=x_wheel - 2.5, anchor_x=anchor_x,
            slot_half=SLOT_HALF, notch_top=round(z_rope + 0.60, 4),
            lowest_abs=DECK_H + load_clear,
            env=(("bench", 1.3, 2.7, 0.0, 1.60), ("hanger", 1.0, 0.90, 1.60, hang)),
            lod0_cap=12000 if base else 10000, lod1_max=5000 if base else 3600, lod2=500,
            hosted_tower=base,
            # nitpick A (2026-10-13): the top's cabin stands on the deck (no opbase; cab_layout)
            engine_base=("deck", "rampback", "plinth_f", "plinth_r")
            + (("opbase",) if (base or TOP_CAB_LEGACY) else ()),
            win_part="cabwin", folder="stations/detachable/A",
            stem="SkiDQBaseStation" if base else "SkiDQTopStation")
    z_rope = round(DECK_H + GONDOLA_STEP_IN + hang, 4)
    # Gondola draft 1: a taller (machinery deck), wider (1.58 m platforms) and longer (back wall
    # 0.51 m behind the real cabin's turn radius) hall; x_wheel stays 9.40 / 5.40 (SkiCatalog rows)
    z_top = GD_Z_TOP[kind]
    w_half = GD_W_HALF
    # revision 1: the deep eaves (GD_EAVE past the long walls) on a shallow segmental roof
    chord_half = w_half + (GD_EAVE if GD_REV1 else 0.10)
    rise = GD_ROOF_RISE if GD_REV1 else RISE_RATIO * 2.0 * chord_half
    x_wheel = x_back - 4.60                     # contract 13 / SkiCatalog StationDef rows
    wheel_back = GD_WHEEL_BACK
    x_back = x_wheel + wheel_back
    return Params(
        variant=f"gd-{kind}", kind=kind, gondola=True, carrier_key="gd8",
        carrier_src=carrier["source"], lane_y=lane, rim_r=lane, torus_r=lane + 0.02,
        rim_segs=44, torus_segs=36, hang=hang,
        grip_above_rope=carrier.get("grip_top_above_rope_m"), seat_top=0.0,
        load_clear=GONDOLA_STEP_IN, z_rope=z_rope, z_wheel=z_rope,
        line_axle=float(carrier["axle_m"]), z_soffit=None, z_top=z_top, z_ceil=z_top,
        chord_half=chord_half, roof_rise=rise, z_crown=z_top + rise, w_half=w_half,
        x_nose=x_nose, x_back=x_back, length=x_back - x_nose, wheel_back=wheel_back,
        x_wheel=x_wheel, ped_f=None, ped_r=None, anchor_x=anchor_x, slot_half=0.0,
        notch_top=None, lowest_abs=DECK_H + GONDOLA_STEP_IN,
        env=GD_ENV,
        lod0_cap=13000 if base else 10000, lod1_max=None, lod2=500, hosted_tower=base,
        engine_base=("wall_n", "wall_p", "wall_back", "wall_nose", "stonebase_n", "stonebase_p",
                     "stonebase_back", "stonebase_nose", "stepside_n", "stepside_p"),
        win_part="ctrlwin", folder="stations/gondola/A",
        stem="SkiGondolaBaseStation" if base else "SkiGondolaTopStation", clear=GD_CLEAR)


# --------------------------------------------------------------------------------------
# Materials (MUST-CHANGE 3: built MaterialSpec fields; rough = 1 - smooth)
# --------------------------------------------------------------------------------------

def _lin(rgb):
    return tuple(float(cs2_preview._srgb_to_linear(np.float32(c))) for c in rgb)


GREY80 = _lin((0.80, 0.80, 0.80))      # normalised to RECOLOR_ALBEDO by the bake anyway
_MS = cs2_bake.MaterialSpec
MATERIALS = {
    "accent3": _MS(albedo=GREY80, metal=0.10, rough=0.50, slot=3, recolor_albedo=True),
    "cedar": _MS(albedo=(0.60, 0.32, 0.12), metal=0.0, rough=0.70),
    "concrete": _MS(texture_set="Concrete034", metres_per_tile=3.0, tint=0.58, metal=0.0,
                    rough=0.82),
    "darksteel": _MS(albedo=cs2_asset.STEEL_DEFAULT_RGB, metal=0.2, rough=0.60),
    "galv": _MS(texture_set="Metal032", metres_per_tile=2.0, metal=1.0, rough=0.65),
    "glass": _MS(albedo=(0.07, 0.09, 0.11), metal=0.0, rough=0.08),
    "lamp": _MS(albedo=(0.85, 0.82, 0.75), metal=0.0, rough=0.40, emissive=(0.95, 0.85, 0.65)),
    "pad2": _MS(albedo=GREY80, metal=0.0, rough=0.75, slot=2, recolor_albedo=True),
    "paint1": _MS(albedo=GREY80, metal=0.10, rough=0.55, slot=1, recolor_albedo=True,
                  projection="planar"),
    # T27: the vanilla line cable (cs2_asset.ROPE_MATERIAL; was albedo (0.30, 0.30, 0.32) metal 0.9 rough 0.65)
    "rope": _MS(**cs2_asset.ROPE_MATERIAL),
    "rubber": _MS(albedo=(0.04, 0.04, 0.045), metal=0.0, rough=0.70),
    # Revision 1 (e): the sign-band quads, pinned in the reserved strip (one rect, one
    # material: cs2_bake._restore_reserved); slot 3 like the band, the emblem / plate are
    # painted over it in the post-compose hook
    "signband": _MS(albedo=GREY80, metal=0.10, rough=0.50, slot=3, recolor_albedo=True),
    "stone": _MS(albedo=(0.55, 0.50, 0.45), metal=0.0, rough=0.80),
    # gondola draft 1: the boarding-edge safety stripe (fixed safety yellow, sRGB ~(0.93, 0.74,
    # 0.10); not a recolour slot - the platform edge reads the same in every colour set)
    "stripe": _MS(albedo=(0.85, 0.52, 0.01), metal=0.0, rough=0.60),
}
CANOPY_RGB = (0.16, 0.30, 0.20)        # proposal green (linear), slot 1 + slot 3 one-tone default
TWOTONE = {"R": (0.08, 0.16, 0.30), "G": cs2_asset.PAD_RGB, "B": (0.10, 0.55, 0.80)}
MARK_WHITE = _lin((0.85, 0.86, 0.88))  # lift_terminal.py:240 white, as written to the PNG

# --------------------------------------------------------------------------------------
# Pipeline tables (contract 3.4, 4.1-4.3)
# --------------------------------------------------------------------------------------

ATLAS = 2048
AO_SAMPLES = 48
PACK_MARGIN_PX = 6
SHADE_ANGLE = 66.0
BLOCKOUT_ATLAS = 512
# band_ left out on purpose: the band ring is welded at 45 deg mitres; a per-piece bevel
# would move the mitre profiles apart (the profile carries its own 0.35 m fillet).
BEVEL_PREFIXES = ("plinth_", "ped_", "crosshead_", "spine_", "cab_", "cabroof", "engroom_",
                  "engroof", "opbase", "deck", "cabinet_", "drive_", "tension_", "wall_",
                  "stonebase_", "opr_", "dkbeam_")
# gondola draft 1: wall_nose is a triangulated polygon with the portal reveals - its 90 deg
# reveal edges would bevel, its fan diagonals not; kept unbevelled (a clean silhouette edge)
BEVEL_EXCLUDE = ("cabwin", "ctrlwin", "hsgwin", "winframe_", "endglass_", "signband_", "lamp_",
                 "wall_nose")

# rail_maze: F4 - the maze rails floated 0.44 m once their posts (post_) went at LOD1
LOD1_DELETE = ("bolt_", "clamp_", "drip_", "kick_", "post_", "lrung_", "hatch_", "tyre",
               "detrail_", "railhang_", "sheave_bull", "spoke_", "wheelhub", "wheelrope",
               "drive_", "tension_", "lamp_", "wx", "commcable_", "commbracket_", "insul_",
               "cabinet_", "stovepipe", "gusset_", "sheave_guide", "cabmull_", "rail_maze",
               "rail_balc", "rail_gang", "lstand_",
               # gondola draft 1: the stair and deck rails leave with their posts (F4), the
               # treads / stringers / landing post with the rungs
               "rail_dk", "rail_st", "stread_", "stringer_", "stpost", "oprglass",
               # detail pass 1 (DQ dressing, design DL8 / cs2_liftdress.DRESS_LOD1_DELETE; none
               # of these families exists on the gondola): everything new goes at LOD1 except
               # the flood-mast shafts + arms (flood_, LOD2) and the ladder rails (lrail_, LOD2)
               "maze_", "gate_", "board_", "label_", "stripe_", "chev_", "carpet_", "ext_",
               "hosereel_", "tool_", "skirack_", "ski_", "estop_", "floodbase_", "floodbrk_",
               "gauge_", "doorlite_", "sill_")
# Revision 1: the nose-access rails leave with their posts at LOD1 (the F4 rule, applied to
# the balcony + gangplank); the ladder standoffs at LOD1, the window gaskets at LOD2
LOD2_DELETE = LOD1_DELETE + ("rail_", "grate_", "flange_", "door_", "haulrope_", "lrail_",
                             "ceil_", "gutter_", "downspout_", "endmull_", "beam_",
                             "guideframe_", "pad_", "spine_", "portalframe_", "winframe_",
                             "lfoot_",
                             # gondola draft 1: the hall interior is invisible past ~500 m
                             "platf_", "pstripe_", "opr_", "dkbeam_", "floor", "wallin_",
                             "apron_",
                             # detail pass 1: the flood-mast shafts + arms (nothing new at LOD2)
                             "flood_")
# Revision 2: the nose ladder's footing stays while its rails do (LOD0 / LOD1) and leaves with
# them at LOD2; its rail clamps (lstand_nzl*) go at LOD1 with the other standoffs

DENSITY_WEIGHTS = {
    "signband_": 1.0,            # pinned in the reserved strip: the weight never applies
    "winframe_": 0.8, "lstand_": 0.6, "roof_": 1.5, "cab_": 1.5, "haulrope_": 1.5, "wheelrope": 1.5,
    "band_": 1.2, "ped_": 1.2, "engroom_": 1.2, "plinth_": 1.0, "crosshead_": 1.0,
    "spine_": 1.0, "pad_": 1.0, "cabroof": 1.0, "endmull_": 1.0, "endglass_": 0.8,
    "beam_": 0.8, "sheave_guide": 0.8, "soffit_": 0.6, "gutter_": 0.6, "downspout_": 0.6,
    "lrail_": 0.6, "lrung_": 0.6, "post_": 0.6, "rail_": 0.6, "grate_": 0.6, "lamp_": 0.6,
    "cabinet_": 0.6, "deck": 0.5, "rampback": 0.5, "opbase": 0.5, "lfoot_": 0.5, "ceil_": 0.2,
    "rooflining": 0.2, "bandlining_": 0.2,
    "sheave_bull": 0.3, "spoke_": 0.3, "wheelhub": 0.3, "detrail_": 0.3, "railhang_": 0.3,
    "tyre": 0.3, "drive_": 0.3, "tension_": 0.3,
    # gondola
    "wall_": 1.2, "stonebase_": 1.0, "portalframe_": 1.0, "floor": 0.3, "stepside_": 0.5,
    # gondola draft 1: interior faces seen only through the portals / never
    "wallin_": 0.25, "platf_": 0.5, "pstripe_": 0.6, "apron_": 0.3, "opr_": 0.4, "winband_": 0.3,
    "oprglass": 0.3, "stread_": 0.4, "stringer_": 0.4, "stpost": 0.4, "dkbeam_": 0.3,
}
# gondola draft 1 only (assemble merges these over DENSITY_WEIGHTS for p.gondola, so the DQ
# weights stay byte-identical): the walled hall hides ~1,500 m2 of interior / lining / painted
# glass; they give their texels to the walls, roof, bands and stone (measured 2026-10-11 before
# this table: walls 31.7, roof 39.5 px/m, interior + glass 5-21)
GD_DENSITY = {"endglass_": 0.45, "grate_": 0.25, "ceil_": 0.12, "rooflining": 0.10,
              "floor": 0.25, "wallin_": 0.20, "haulrope_": 1.0, "dkbeam_": 0.2, "stread_": 0.3,
              "stringer_": 0.3, "opr_": 0.35, "lamp_": 0.4, "winband_": 0.3,
              "roof_": 1.6, "band_": 1.3, "wall_": 1.3, "stonebase_": 1.1, "portalframe_": 1.2}
# revision 1 (the glazed hall): the glass is `_Win` (no texels), so the texels go to the roof, its
# eave soffits, the stone plinth, the steel frame and the portal wall
GD_DENSITY_R1 = {"eavesof_": 0.6, "outrig_": 0.6, "mull_": 0.9, "transom_": 0.7, "ringbeam_": 0.8,
                 "coping_": 0.9, "gable_": 0.9, "canopy_": 1.0, "canopyrod_": 0.7, "snowguard_": 0.6,
                 "spandrel_": 1.0, "signplate_": 1.0, "door_": 0.8, "lamp_": 0.6, "gutter_": 0.6,
                 "downspout_": 0.6, "label_": 1.0, "stepside_": 0.9, "lfoot_": 0.7, "platf_": 0.6,
                 "floor": 0.3, "opr_": 0.4,
                 # the shallow roof (a big flat standing-seam field: 1.35 keeps it at the draft's ~38
                 # px/m and frees texels for the frame), the plinth walls' faces (their outer face is
                 # under the stone shell, their inner face seen through the portals / doors only), the
                 # portal wall (the hall's end gable, the front of the station)
                 "roof_": 1.35, "wall_n": 0.35, "wall_p": 0.35, "wall_back": 0.35, "wall_nose": 1.3}


def detail_specs():
    """Relief per prefix (contract 4.3). The bake runs on the NET-ORIENTED mesh, so the
    in-plane world axes of cs2_compose are post-orient: on horizontal faces e_h = X' =
    authoring Y, hence the soffit boards along authoring X need "ribs", not "rings"."""
    D = cs2_compose.DetailSpec
    steel = dict(seams_m=1.2, bolt_pitch_m=0.15, bolt_r_m=0.018)
    cedar = D(seams_m=0.15, seam_axes="ribs", seam_depth_m=0.004)
    leaf = D(seams_m=0.30, seam_depth_m=0.003, seam_axes="rings")
    return {
        "roof_": D(seams_m=0.50, seam_axes="ribs", seam_width_m=0.04, seam_depth_m=0.004),
        "band_": D(seams_m=1.50, seam_axes="ribs", seam_depth_m=0.002),
        "soffit_": D(seams_m=0.14, seam_axes="ribs", seam_depth_m=0.003),
        "cab_": cedar, "engroom_": cedar, "wall_": cedar,
        "ped_": D(seam_axes="rings", **steel),
        "crosshead_": D(**steel), "spine_": D(**steel), "beam_": D(**steel),
        "plinth_": D(seams_m=1.2, seam_axes="rings", seam_depth_m=0.002),
        "haulrope_": D(helix_m=0.10), "wheelrope": D(helix_m=0.10),
        "grate_": D(grate_m=0.03), "grate_balc_lip": D(), "grate_gang_lip": D(),
        "grate_balc_n_lip": D(), "grate_balc_o_lip": D(), "grate_balc_i_lip": D(),
        "door_cab_leaf": leaf, "door_eng_leaf": leaf, "door_n_leaf": leaf, "door_p_leaf": leaf,
        "stonebase_": D(seams_m=0.35, seam_axes="both", seam_width_m=0.02, seam_depth_m=0.006),
        "signband_": D(), "winframe_": D(), "endglass_": D(), "lamp_": D(),
        # gondola draft 1
        "wallin_": cedar, "opr_walls": cedar, "door_opr_leaf": leaf,
        "platf_": D(seams_m=1.5, seam_axes="both", seam_depth_m=0.002),
        "stread_": D(grate_m=0.03), "dkbeam_": D(**steel), "pstripe_": D(), "apron_": D(),
    }


def detail_specs_dq():
    """Detail pass 1 relief for the DQ (design 3.7; the gondola keeps detail_specs()): steel bolt
    heads 0.018 -> 0.025 m radius (drawn where 2 r px/m >= 2, i.e. at >= 40 px/m: the pedestals,
    crossheads and spine now carry weight 1.3), the grating bearing-bar pitch 0.03 -> 0.12 m (2.4
    texels a pitch at weight 0.6: the gratings stop rendering flat), the band joints 0.012 ->
    0.025 m wide and 0.004 deep; the new dressing carries no relief (labels are pinned, stripes and
    chevrons are geometry)."""
    D = cs2_compose.DetailSpec
    out = detail_specs()
    steel = dict(seams_m=1.2, bolt_pitch_m=0.15, bolt_r_m=0.025)
    out.update({
        "ped_": D(seam_axes="rings", **steel),
        "crosshead_": D(**steel), "spine_": D(**steel), "beam_": D(**steel),
        "grate_": D(grate_m=0.12),
        "band_": D(seams_m=1.50, seam_axes="ribs", seam_width_m=0.025, seam_depth_m=0.004),
        "label_": D(), "board_": D(), "stripe_": D(), "chev_": D(), "carpet_": D(),
        "doorlite_": D(), "sill_": D(), "hatch_sofpf": D(),
    })
    return out


def detail_specs_gd():
    """Gondola revision 1 relief (the glazed hall): standing seams on the shallow roof (0.50 m pitch,
    0.04 m wide, 6 mm deep - the cs2_bldg STANDING_SEAM grooves, a little wider for the game
    distance), soffit panel joints under the deep eaves, concrete lift lines on the portal wall and
    the plinth (the stone shell keeps its coursed joints), no relief on the steel frame, the glass
    door leaves, the sign plates or the dressing (labels are pinned)."""
    D = cs2_compose.DetailSpec
    out = detail_specs()
    out.update({
        "roof_": D(seams_m=0.50, seam_axes="ribs", seam_width_m=0.04, seam_depth_m=0.006),
        "eavesof_": D(seams_m=0.30, seam_axes="ribs", seam_depth_m=0.003),
        "wall_": D(seams_m=1.2, seam_axes="rings", seam_depth_m=0.002),
        "door_n_leaf": D(), "door_p_leaf": D(), "door_": D(),
        "mull_": D(), "transom_": D(), "ringbeam_": D(), "coping_": D(), "gable_": D(),
        "canopy_": D(), "outrig_": D(), "snowguard_": D(), "spandrel_": D(), "signplate_": D(),
        "label_": D(), "board_": D(),
    })
    return out


def specs_for(p):
    """The relief table of this station (a zero-argument function: lift_terminal's hook calls it)."""
    if p.gondola and GD_REV1:
        return detail_specs_gd
    return detail_specs_dq if (DETAIL1 and not p.gondola) else detail_specs


def density_for(p):
    """Density weights of this station: gondola draft 1 merges GD_DENSITY; detail pass 1 (DQ)
    merges cs2_liftdress.DRESS_DENSITY and DQ_D1_DENSITY (design 3.7); gondola revision 1 adds the
    dressing's DRESS_DENSITY and GD_DENSITY_R1 (the glazed hall's frame, roof, plinth)."""
    if p.gondola and GD_REV1:
        return {**DENSITY_WEIGHTS, **GD_DENSITY, **_LD().DRESS_DENSITY, **GD_DENSITY_R1}
    if p.gondola:
        return {**DENSITY_WEIGHTS, **GD_DENSITY}
    if not DETAIL1:
        return DENSITY_WEIGHTS
    LD = _LD()
    return {**DENSITY_WEIGHTS, **LD.DRESS_DENSITY, **DQ_D1_DENSITY}


def _LD():
    import cs2_liftdress
    return cs2_liftdress


# Envelope parts the clearance check (C8) may touch: the rope machine the carriers ride.
C8_ALLOWED = ("haulrope_", "wheelrope", "sheave_bull", "spoke_", "wheelhub", "detrail_",
              "railhang_", "tyre", "drive_", "tension_", "sheave_guide", "guideframe_")
C7_PREFIXES = ("ped_", "plinth_", "pad_", "flange_", "gusset_ped", "lrail_p", "lrung_p",
               "cabinet_comm")

# --------------------------------------------------------------------------------------
# Mesh helpers
# --------------------------------------------------------------------------------------


class _MB:
    """bmesh builder: vertices shared by rounded coordinate, every face wound to a hint."""

    def __init__(self):
        self.bm = bmesh.new()
        self.vmap = {}

    def v(self, co):
        key = (round(float(co[0]), 6), round(float(co[1]), 6), round(float(co[2]), 6))
        vert = self.vmap.get(key)
        if vert is None:
            vert = self.bm.verts.new(key)
            self.vmap[key] = vert
        return vert

    def f(self, cos, hint):
        vs = []
        for c in cos:
            x = self.v(c)
            if x not in vs:
                vs.append(x)
        if len(vs) < 3:
            return None
        face = self.bm.faces.new(vs)
        face.normal_update()
        if face.normal.length < 0.5:
            raise ValueError(f"degenerate face {cos}")
        if face.normal.dot(Vector(hint)) < 0.0:
            face.normal_flip()
        return face

    def obj(self, name):
        me = bpy.data.meshes.new(name)
        self.bm.normal_update()
        self.bm.to_mesh(me)
        self.bm.free()
        me.update()
        o = bpy.data.objects.new(name, me)
        bpy.context.collection.objects.link(o)
        return o


def _box(name, x0, x1, y0, y1, z0, z1, open_bottom=False, drop=()):
    return cs2_kit.add_box(name, (x1 - x0, y1 - y0, z1 - z0),
                           ((x0 + x1) / 2.0, (y0 + y1) / 2.0, (z0 + z1) / 2.0),
                           open_bottom=open_bottom, drop=drop)


def _cyl_pts(name, p0, p1, r, segs, caps="none"):
    a, b = Vector(p0), Vector(p1)
    d = b - a
    rot = tuple(d.to_track_quat("Z", "Y").to_euler())
    return cs2_kit.add_cyl(name, r, d.length, tuple((a + b) / 2.0), rot=rot, segments=segs,
                           caps=caps)


def _quad(name, corners, hint):
    mb = _MB()
    mb.f(corners, hint)
    return mb.obj(name)


def _m(kit_parts, mat, **over):
    """Kit assembly [(obj, quad int)] -> [(obj, material name)] (MUST-CHANGE 4: the kit's
    int quads would otherwise become material keys "0".."3")."""
    rules = sorted(over.items(), key=lambda kv: -len(kv[0]))
    out = []
    for o, _q in kit_parts:
        m = mat
        for pfx, mm in rules:
            if o.name.startswith(pfx):
                m = mm
                break
        out.append((o, m))
    return out


_DIRS = {"+X": (1, 0, 0), "-X": (-1, 0, 0), "+Y": (0, 1, 0), "-Y": (0, -1, 0),
         "+Z": (0, 0, 1), "-Z": (0, 0, -1)}


def _drop_dir(obj, *dirs):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.normal_update()
    vecs = [Vector(_DIRS[d]) for d in dirs]
    kill = [f for f in bm.faces if any(f.normal.dot(v) > 0.9 for v in vecs)]
    bmesh.ops.delete(bm, geom=kill, context="FACES")
    bm.to_mesh(obj.data)
    bm.free()
    return obj


@contextlib.contextmanager
def _lt_override(**names):
    """Temporarily point lift_terminal's module tables at this generator's, so its helpers
    (bevels, _Win split, compose hook) are REUSED, never copied."""
    saved = {k: getattr(lift_terminal, k) for k in names}
    for k, v in names.items():
        setattr(lift_terminal, k, v)
    try:
        yield
    finally:
        for k, v in saved.items():
            setattr(lift_terminal, k, v)


# --------------------------------------------------------------------------------------
# Canopy geometry
# --------------------------------------------------------------------------------------

def arch(p):
    """Roof outer arc (y, z) eave -> crown -> eave, ROOF_SEGS segments, plus (R, zc).
    Circular segment traced from Kagelok (rise/chord 0.2492, contract D2)."""
    c, h = p.chord_half, p.roof_rise
    R = (c * c + h * h) / (2.0 * h)
    th = math.asin(c / R)
    zc = p.z_top + h - R
    pts = []
    for i in range(ROOF_SEGS + 1):
        a = -th + 2.0 * th * i / ROOF_SEGS
        pts.append((R * math.sin(a), zc + R * math.cos(a)))
    pts[0], pts[-1] = (-c, p.z_top), (c, p.z_top)
    pts[ROOF_SEGS // 2] = (0.0, p.z_top + h)
    return pts, R, zc


def arc_z(p, y, inner=True):
    _pts, R, zc = arch(p)
    return zc + math.sqrt(max(R * R - y * y, 0.0)) - (ROOF_T if inner else 0.0)


def build_roof(p):
    """roof_shell (outer arc, end rims, eave fascias) + rooflining (the underside, arc shifted
    down ROOF_T): 228 tris together. The lining is its own part only so it can take a low
    texel density - the ceiling hides all of it except the 0.10 / 0.15 m overhang strips."""
    pts, _R, zc = arch(p)
    xa, xb, t = p.x_nose - ROOF_END, p.x_back + ROOF_END, ROOF_T
    mb, ml, mr = _MB(), _MB(), _MB()
    for i in range(ROOF_SEGS):
        (y0, z0), (y1, z1) = pts[i], pts[i + 1]
        n = (0.0, (y0 + y1) / 2.0, (z0 + z1) / 2.0 - zc)
        mb.f([(xa, y0, z0), (xb, y0, z0), (xb, y1, z1), (xa, y1, z1)], n)
        ml.f([(xa, y0, z0 - t), (xb, y0, z0 - t), (xb, y1, z1 - t), (xa, y1, z1 - t)],
             (0.0, -n[1], -n[2]))
        for x, s in ((xa, -1.0), (xb, 1.0)):
            mr.f([(x, y0, z0), (x, y1, z1), (x, y1, z1 - t), (x, y0, z0 - t)], (s, 0.0, 0.0))
    for (y, z), s in ((pts[0], -1.0), (pts[-1], 1.0)):
        mb.f([(xa, y, z), (xb, y, z), (xb, y, z - t), (xa, y, z - t)], (0.0, s, 0.0))
    return [(mb.obj("roof_shell"), "paint1"), (mr.obj("roof_rim"), "paint1"),
            (ml.obj("rooflining"), "paint1")]


def band_profile(p):
    """Band C-section in (d = inward depth from the outer face, z): outer face, a split at
    the nose-notch height, 4-segment fillet r 0.35 curling inward to the soffit; inner face
    BAND_T in with a concentric r 0.20 fillet. 7 points each, paired for the caps."""
    zs, zt, zn = p.z_soffit, p.z_top, p.notch_top
    ri = FILLET_R - BAND_T
    outer = [(0.0, zt), (0.0, zn), (0.0, zs + FILLET_R)]
    inner = [(BAND_T, zt), (BAND_T, zn), (BAND_T, zs + FILLET_R)]
    for k in range(1, 5):
        a = math.radians(180.0 + 22.5 * k)
        outer.append((FILLET_R + FILLET_R * math.cos(a), zs + FILLET_R + FILLET_R * math.sin(a)))
        inner.append((FILLET_R + ri * math.cos(a), zs + FILLET_R + ri * math.sin(a)))
    outer[-1] = (FILLET_R, zs)
    inner[-1] = (FILLET_R, zs + BAND_T)
    return outer, inner


def _sweep(name, W, ddir, tdir, t0, t1, outer, inner, caps=(False, False), cap_from=1):
    """One straight band piece: W(t, d, z) -> world point; t0(d) / t1(d) the piece ends
    (a mitre makes them depend on d). Outer strip faces outward, inner strip and the return
    face the canopy interior; caps close square ends below profile point `cap_from`."""
    mb, mi = _MB(), _MB()

    def nv(nd, nz):
        return (nd * ddir[0], nd * ddir[1], nd * ddir[2] + nz)

    for prof, sgn, tgt in ((outer, 1.0, mb), (inner, -1.0, mi)):
        for i in range(len(prof) - 1):
            (da, za), (db, zb) = prof[i], prof[i + 1]
            dd, dz = db - da, zb - za
            tgt.f([W(t0(da), da, za), W(t1(da), da, za), W(t1(db), db, zb), W(t0(db), db, zb)],
                  nv(dz * sgn, -dd * sgn))
    (do, zo), (di, zi) = outer[-1], inner[-1]
    mb.f([W(t0(do), do, zo), W(t1(do), do, zo), W(t1(di), di, zi), W(t0(di), di, zi)],
         nv(1.0, 0.0))
    for cap, tf, s in ((caps[0], t0, -1.0), (caps[1], t1, 1.0)):
        if not cap:
            continue
        for k in range(cap_from, len(outer) - 1):
            (oa, oza), (ob, ozb) = outer[k], outer[k + 1]
            (ia, iza), (ib, izb) = inner[k], inner[k + 1]
            mb.f([W(tf(oa), oa, oza), W(tf(ob), ob, ozb), W(tf(ib), ib, izb), W(tf(ia), ia, iza)],
                 (s * tdir[0], s * tdir[1], s * tdir[2]))
    # the inner strip faces the closed canopy box: own part, low texel density
    return mb.obj(name), mi.obj(name.replace("band_", "bandlining_", 1))


def build_band_ring(p):
    """The loaf's lower band as one welded ring: two long sides, the back end and the nose
    (centre panel + two side panels around the chair notches + two lintels over them).
    Corners are 45 deg mitres sharing their profile vertices (remove_doubles welds them)."""
    outer, inner = band_profile(p)
    W2, xn, xb = p.w_half, p.x_nose, p.x_back
    yi, yo = p.lane_y - p.slot_half, p.lane_y + p.slot_half
    parts = []
    for s, tag in ((1.0, "p"), (-1.0, "n")):
        parts.append(_sweep(f"band_{tag}", lambda t, d, z, s=s: (t, s * (W2 - d), z),
                            (0.0, -s, 0.0), (1.0, 0.0, 0.0),
                            lambda d: xn + d, lambda d: xb - d, outer, inner))
    parts.append(_sweep("band_back", lambda t, d, z: (xb - d, t, z), (-1.0, 0.0, 0.0),
                        (0.0, 1.0, 0.0), lambda d: -W2 + d, lambda d: W2 - d, outer, inner))
    nose = lambda t, d, z: (xn + d, t, z)  # noqa: E731
    parts.append(_sweep("band_nose_c", nose, (1.0, 0.0, 0.0), (0.0, 1.0, 0.0),
                        lambda d: -yi, lambda d: yi, outer, inner, caps=(True, True)))
    parts.append(_sweep("band_nose_p", nose, (1.0, 0.0, 0.0), (0.0, 1.0, 0.0),
                        lambda d: yo, lambda d: W2 - d, outer, inner, caps=(True, False)))
    parts.append(_sweep("band_nose_n", nose, (1.0, 0.0, 0.0), (0.0, 1.0, 0.0),
                        lambda d: -W2 + d, lambda d: -yo, outer, inner, caps=(False, True)))
    flat = [o for pair in parts for o in pair]
    for s, tag in ((1.0, "p"), (-1.0, "n")):
        y0, y1 = sorted((s * yi, s * yo))
        flat.append(_box(f"band_nose_l{tag}", xn, xn + BAND_T, y0, y1, p.notch_top, p.z_top,
                         drop=("+Z", "+Y", "-Y")))
    return [(o, "accent3") for o in flat]


def plate_digits(p):
    """The lift-number plate text: the carrier's seat count (contract Q8 default; a per-lift
    number needs an optional Write Everywhere layout, ROUND-SPEC 0.7)."""
    return "8" if p.gondola else "4"


def sign_layout(digits):
    """Revision 1 (e): one sign panel per side, viewer frame (s right, t up, metres from the
    panel's lower-left corner): the emblem square on the left, the number plate on the
    right. The text box keeps the 5x7 cell grid's size (SIGN_CELL 0.10: 0.50 m wide per
    digit, 0.70 m tall), so the quad, the reserved rect and the plate are unchanged; since
    Revision 1b the digits inside it are vector outlines (digit_triangles) at a 0.70 m cap
    height, shrunk only if a string is wider than its box."""
    n = len(digits)
    text_w = n * 5 * SIGN_CELL + (n - 1) * SIGN_CELL
    plate_w = text_w + 2.0 * SIGN_PAD
    W = SIGN_H + SIGN_GAP + plate_w
    return {"W": W, "H": SIGN_H, "digits": digits, "cell": SIGN_CELL,
            "emblem": (0.0, SIGN_H, 0.0, SIGN_H),
            "plate": (W - plate_w, W, 0.0, SIGN_H),
            "text": (W - plate_w + SIGN_PAD, W - SIGN_PAD, SIGN_PAD, SIGN_H - SIGN_PAD),
            "digit_h_m": 7 * SIGN_CELL, "digit_w_m": 5 * SIGN_CELL}


def sign_rects(p):
    """Reserved UV rects (u0, v0, u1, v1) of the two sign panels ({"n": .., "p": ..}): a
    strip at the top of the atlas, SIGN_PX_PER_M at 2048 on both axes, texel-aligned at
    2048 and 512. bake_unique pins the faces inside and squeezes everything else into the
    v-band below (cs2_uv.free_v_band treats a rect as full width)."""
    lay = sign_layout(plate_digits(p))
    k = SIGN_PX_PER_M / float(ATLAS)            # UV per metre
    v1 = 1.0 - SIGN_STRIP_TOP_PX / float(ATLAS)
    v0 = v1 - lay["H"] * k
    shared = sign_shared(p)
    out = {}
    for tag, u0px in zip(("n", "p"), SIGN_STRIP_LEFT_PX):
        if tag in shared:                       # detail pass 1 (DQ): p is painted on n's rect
            continue
        u0 = u0px / float(ATLAS)
        out[tag] = (u0, v0, u0 + lay["W"] * k, v1)
    if "p" in out and (out["n"][2] > out["p"][0] or out["p"][2] > 1.0):
        raise ContractError(f"sign rects overlap or leave the atlas: {out}")
    return out


def sign_shared(p):
    """{side: owner side} of the sign quads that reuse another side's reserved rect: gondola draft 1
    q -> p (SIGN_SHARED); detail pass 1 (DQ) p -> n (DQ_SIGN_SHARED, skeptic row 7: the freed
    784 x 392 px hold the dressing labels; stamped and relief-blended ONCE)."""
    if p.gondola:
        # revision 1: all three gondola signs (portal face, +Y and -Y walls) carry the same emblem + "8",
        # so p and q share n's rect - the freed strip (LABEL_REGION_PX) holds the gondola's labels
        return dict(GD_SIGN_SHARED_R1) if GD_REV1 else dict(SIGN_SHARED)
    return dict(DQ_SIGN_SHARED) if DETAIL1 else {}


def sign_z_centre(p):
    """The sign / window centre line on the band (DQ) or on the gondola wall-top band."""
    if p.gondola:
        return p.z_top - 0.5
    return p.z_soffit + FILLET_R + SIGN_Z_C_ABOVE_FILLET


def build_signbands(p, y_face):
    """signband_n / signband_p: ONE quad per side, 0.012 proud of the band, centred at the
    band's mid-length. Its UVSeed is the reserved rect (seed_parts), u -> viewer right."""
    lay = sign_layout(plate_digits(p))
    W, H = lay["W"], lay["H"]
    xm = (p.x_nose + p.x_back) / 2.0
    zc = sign_z_centre(p)
    z0, z1 = zc - H / 2.0, zc + H / 2.0
    out = []
    for s, tag in ((1.0, "p"), (-1.0, "n")):
        y = s * (y_face + 0.012)
        out.append((_quad(f"signband_{tag}", [(xm - W / 2, y, z0), (xm + W / 2, y, z0),
                                              (xm + W / 2, y, z1), (xm - W / 2, y, z1)],
                          (0.0, s, 0.0)), "signband"))
    return out


def _round_rect(xc, zc, w, h, r, segs):
    """(x, z) outline of a rounded rectangle, 4 corners x (segs + 1) points in angle order
    (the _MB builder winds each face to its hint). Corner centres do not depend on r, so
    the outline of (w + 2f, h + 2f, r + f) is the same shape offset by f (the gasket)."""
    pts = []
    for cx, cz, a0 in ((xc + w / 2 - r, zc + h / 2 - r, 0.0), (xc - w / 2 + r, zc + h / 2 - r, 90.0),
                       (xc - w / 2 + r, zc - h / 2 + r, 180.0), (xc + w / 2 - r, zc - h / 2 + r, 270.0)):
        for i in range(segs + 1):
            a = math.radians(a0 + 90.0 * i / segs)
            pts.append((cx + r * math.cos(a), cz + r * math.sin(a)))
    return pts


def win_cells(p):
    """`_Win` pane groups -> room cell (col, row): the operator cabin (2, 1) = 7 as before;
    Revision 1 (f) adds the housing windows (3, 2) = 13 (DQ only). The gondola keeps its
    control-room panes; gondola draft 1 adds its hall windows in the same cell 13."""
    if p.gondola:
        return {"ctrlwin": (2, 1), "hsgwin": WIN_CELL_HOUSING}
    return {"cabwin": (2, 1), "hsgwin": WIN_CELL_HOUSING}


def expected_room_ids(p):
    """C12 (changed by Revision 1 (f)): the room ids the `_Win` sub-mesh must carry."""
    return sorted(row * 5 + col for col, row in win_cells(p).values())


def window_centres(p):
    """Revision 1 (f): two housing windows per side at x_mid +- WIN_DX, on the sign line."""
    xm = (p.x_nose + p.x_back) / 2.0
    return [(xm - WIN_DX, sign_z_centre(p)), (xm + WIN_DX, sign_z_centre(p))]


def build_housing_windows(p):
    """hsgwin_<side><k>: one rounded-rectangle pane (an n-gon, 0.015 proud of the band) per
    window -> the `_Win` sub-mesh (room cell WIN_CELL_HOUSING); winframe_<side><k>: the dark
    gasket ring around it (0.020 proud, main mesh)."""
    out = []
    n = WIN_CORNER_SEGS
    for s, tag in ((1.0, "p"), (-1.0, "n")):
        for k, (xc, zc) in enumerate(window_centres(p)):
            inner = _round_rect(xc, zc, WIN_W, WIN_H, WIN_R, n)
            outer = _round_rect(xc, zc, WIN_W + 2 * WIN_FRAME, WIN_H + 2 * WIN_FRAME,
                                WIN_R + WIN_FRAME, n)
            yp, yf = s * (p.w_half + 0.015), s * (p.w_half + 0.020)
            mb = _MB()
            mb.f([(x, yp, z) for x, z in inner], (0.0, s, 0.0))
            out.append((mb.obj(f"hsgwin_{tag}{k}"), "glass"))
            mb = _MB()
            m = len(inner)
            for i in range(m):
                j = (i + 1) % m
                mb.f([(inner[i][0], yf, inner[i][1]), (outer[i][0], yf, outer[i][1]),
                      (outer[j][0], yf, outer[j][1]), (inner[j][0], yf, inner[j][1])],
                     (0.0, s, 0.0))
            out.append((mb.obj(f"winframe_{tag}{k}"), "darksteel"))
    return out


def build_soffit(p):
    """soffit_tongue (|y| <= lane - slot/2, semicircle around the wheel) and soffit_outer
    (outside the U-slot out to the band fillets), both one down-facing layer at z_soffit."""
    zs, xw, n = p.z_soffit, p.x_wheel, 16
    x0 = p.x_nose + FILLET_R
    xe = p.x_back - FILLET_R
    ye = p.w_half - FILLET_R
    hi, ho = p.lane_y - p.slot_half, p.lane_y + p.slot_half
    dn = (0.0, 0.0, -1.0)
    mb = _MB()
    pf = pf_layout(p)
    if pf is None:
        mb.f([(x0, -hi, zs), (xw, -hi, zs), (xw, 0.0, zs), (x0, 0.0, zs)], dn)
        mb.f([(x0, 0.0, zs), (xw, 0.0, zs), (xw, hi, zs), (x0, hi, zs)], dn)
    else:
        # detail pass 1 (MC5): the pf hatch opening is CUT into the tongue (the hatch leaf closes
        # it 5 mm above the frame). Both halves split at the hole's x (no T-junction on the y 0
        # seam or on the fan's diameter at x_wheel); the hole column keeps a cell below and above
        # the hole - its 4 T-vertices are the hole corners, under the hatch frame ring
        hx0, hx1, hy0, hy1 = pf["hole"]
        if not (x0 < hx0 < hx1 < xw and 0.0 < hy0 < hy1 < hi):
            raise ContractError(f"pf hatch hole {pf['hole']} leaves the soffit tongue")
        xs = (x0, hx0, hx1, xw)
        for xa, xb in zip(xs, xs[1:]):
            mb.f([(xa, -hi, zs), (xb, -hi, zs), (xb, 0.0, zs), (xa, 0.0, zs)], dn)
        mb.f([(x0, 0.0, zs), (hx0, 0.0, zs), (hx0, hi, zs), (x0, hi, zs)], dn)
        mb.f([(hx1, 0.0, zs), (xw, 0.0, zs), (xw, hi, zs), (hx1, hi, zs)], dn)
        mb.f([(hx0, 0.0, zs), (hx1, 0.0, zs), (hx1, hy0, zs), (hx0, hy0, zs)], dn)
        mb.f([(hx0, hy1, zs), (hx1, hy1, zs), (hx1, hi, zs), (hx0, hi, zs)], dn)
    for i in range(n):
        a0 = math.radians(-90.0 + 180.0 * i / n)
        a1 = math.radians(-90.0 + 180.0 * (i + 1) / n)
        mb.f([(xw, 0.0, zs), (xw + hi * math.cos(a0), hi * math.sin(a0), zs),
              (xw + hi * math.cos(a1), hi * math.sin(a1), zs)], dn)
    tongue = mb.obj("soffit_tongue")
    mb = _MB()
    for s in (-1.0, 1.0):
        mb.f([(x0, s * ho, zs), (xw, s * ho, zs), (xw, s * ye, zs), (x0, s * ye, zs)], dn)
    outer = mb.obj("soffit_outer")
    mb = _MB()
    ac = math.degrees(math.atan2(ye, xe - xw))
    angles = sorted(set([round(-90.0 + 180.0 * i / n, 9) for i in range(n + 1)] + [-ac, ac]))

    def ray(a):
        c, s = math.cos(math.radians(a)), math.sin(math.radians(a))
        tx = (xe - xw) / c if c > 1e-9 else float("inf")
        ty = ye / abs(s) if abs(s) > 1e-9 else float("inf")
        t = min(tx, ty)
        return (xw + t * c, t * s, zs), (xw + ho * c, ho * s, zs)

    for a0, a1 in zip(angles, angles[1:]):
        (o0, i0), (o1, i1) = ray(a0), ray(a1)
        mb.f([i0, i1, o1, o0], dn)
    turn = mb.obj("soffit_turn")
    return [(tongue, "cedar"), (outer, "cedar"), (turn, "cedar")]


def build_endglass(p, which, y_lim, z_bottom=None, y_rect=None):
    """endglass_<which>: arched glazing under the roof's inner arc above z_top (a fan from
    (0, z_top)); with z_bottom (gondola back curtain) a rectangle |y| <= y_rect from z_bottom
    to z_top below it. Painted glass on the main mesh (contract 7)."""
    pts, R, zc = arch(p)
    sgn = -1.0 if which == "nose" else 1.0
    x = p.x_nose + 0.10 if which == "nose" else p.x_back - 0.10
    arc = [(-y_lim, arc_z(p, y_lim))]
    arc += [(y, z - ROOF_T) for y, z in pts if abs(y) < y_lim - 1e-6]
    arc += [(y_lim, arc_z(p, y_lim))]
    mb = _MB()
    h = (sgn, 0.0, 0.0)
    for (ya, za), (yb, zb) in zip(arc, arc[1:]):
        mb.f([(x, 0.0, p.z_top), (x, ya, za), (x, yb, zb)], h)
    for ys in (-y_lim, y_lim):          # close the sliver between the fan and z_top
        if arc_z(p, y_lim) - p.z_top > 1e-4:
            mb.f([(x, 0.0, p.z_top), (x, ys, p.z_top), (x, ys, arc_z(p, y_lim))], h)
    if z_bottom is not None:
        yr = y_rect
        mb.f([(x, -yr, z_bottom), (x, 0.0, z_bottom), (x, 0.0, p.z_top), (x, -yr, p.z_top)], h)
        mb.f([(x, 0.0, z_bottom), (x, yr, z_bottom), (x, yr, p.z_top), (x, 0.0, p.z_top)], h)
        # the fan's outer ends sit on the rectangle's top corners (|y| = y_rect)
    return mb.obj(f"endglass_{which}")


def build_endmullions(p, which, ys, z0, transom_z, y_lim, sill=True):
    """endmull_<which>_*: vertical mullions up to the inner arc, a transom and a sill, 0.03
    proud of the glass plane (contract 4: 3 mullions + transom + sill per end)."""
    sgn = -1.0 if which == "nose" else 1.0
    xg = (p.x_nose + 0.10 if which == "nose" else p.x_back - 0.10) + sgn * 0.03
    out = []
    for k, y in enumerate(ys):
        out.append(_box(f"endmull_{which}_{k}", xg - 0.04, xg + 0.04, y - 0.04, y + 0.04, z0,
                        arc_z(p, abs(y) + 0.04), drop=("-Z",)))
    _pts, R, zc = arch(p)
    yt = min(math.sqrt(max(R * R - (transom_z + 0.04 + ROOF_T - zc) ** 2, 0.0)), y_lim)
    out.append(_box(f"endmull_{which}_t", xg - 0.035, xg + 0.035, -yt, yt, transom_z - 0.04,
                    transom_z + 0.04))
    if sill:
        out.append(_box(f"endmull_{which}_s", xg - 0.04, xg + 0.04, -y_lim, y_lim, z0, z0 + 0.08))
    return [(o, "paint1") for o in out]


def build_gutters(p, z_low):
    c = p.chord_half
    out = []
    for s, tag in ((1.0, "p"), (-1.0, "n")):
        y0, y1 = sorted((s * c, s * (c + 0.12)))
        out.append((_box(f"gutter_{tag}", p.x_nose - ROOF_END, p.x_back + ROOF_END, y0, y1,
                         p.z_top - 0.14, p.z_top - 0.02, drop=("+Z",)), "galv"))
        out.append((cs2_kit.add_cyl(f"downspout_{tag}", 0.05, p.z_top - 0.14 - z_low,
                                    (p.x_back - 0.40, s * (c + 0.06),
                                     (p.z_top - 0.14 + z_low) / 2.0),
                                    segments=6, caps="none"), "galv"))
    return out


# --------------------------------------------------------------------------------------
# Ground, pedestals, underframe (DQ)
# --------------------------------------------------------------------------------------

DECK_Y = (-7.5, 5.5)            # maze side -Y, operator +Y (contract 3.2)
PLINTH = (2.40, 2.20, 1.00)


def build_deck(p):
    """deck: open-bottom slab 0.30, no +X face (the ramp continues it). The plinths pass
    through two holes; the holes get NO walls - the plinths' own side faces close them, so
    no coincident hole walls eat texels (skeptic claim 9 gap 3; the plinth sides inside the
    slab, 2 x 2.76 m2, are the accepted hidden band)."""
    y0, y1 = DECK_Y
    holes = [(x - PLINTH[0] / 2, x + PLINTH[0] / 2) for x in (p.ped_f, p.ped_r)]
    xs = sorted(set([p.x_nose, p.x_back] + [v for h in holes for v in h]))
    ys = [y0, -PLINTH[1] / 2, PLINTH[1] / 2, y1]
    z = DECK_H
    mb = _MB()
    for xa, xb in zip(xs, xs[1:]):
        for ya, yb in zip(ys, ys[1:]):
            cx, cy = (xa + xb) / 2, (ya + yb) / 2
            if abs(cy) < PLINTH[1] / 2 and any(h0 < cx < h1 for h0, h1 in holes):
                continue
            mb.f([(xa, ya, z), (xb, ya, z), (xb, yb, z), (xa, yb, z)], (0, 0, 1))
    for ya, yb in zip(ys, ys[1:]):
        mb.f([(p.x_nose, ya, 0.0), (p.x_nose, yb, 0.0), (p.x_nose, yb, z), (p.x_nose, ya, z)],
             (-1, 0, 0))
    for xa, xb in zip(xs, xs[1:]):
        for y, s in ((y0, -1), (y1, 1)):
            mb.f([(xa, y, 0.0), (xb, y, 0.0), (xb, y, z), (xa, y, z)], (0, s, 0))
    deck = mb.obj("deck")
    ramp = cs2_kit.add_wedge("rampback", p.x_back, p.x_back + 2.0, y1 - y0, DECK_H, 0.0,
                             open_bottom=True)
    ramp.location = (0.0, (y0 + y1) / 2.0, 0.0)
    _drop_dir(ramp, "-X")
    return [(deck, "concrete"), (ramp, "concrete")]


def z_ped_top(p):
    return p.z_soffit - 0.60


def build_pedestals(p):
    """Two galvanized pedestals on the centreline in front of the wheel (D5): open-bottom
    plinth, bolted flange, frustum column, slot-2 crash pad, 4 head gussets, crosshead."""
    zt = z_ped_top(p)
    out = []
    for tag, xp in (("f", p.ped_f), ("r", p.ped_r)):
        out.append((_box(f"plinth_{tag}", xp - PLINTH[0] / 2, xp + PLINTH[0] / 2,
                         -PLINTH[1] / 2, PLINTH[1] / 2, 0.0, PLINTH[2], open_bottom=True),
                    "concrete"))
        out += _m(cs2_kit.flange(f"ped{tag}", 1.0, t=0.04, bolts=8, segs=16,
                                 loc=(xp, 0.0, PLINTH[2] + 0.02), axis="Z", bolt_geo=True,
                                 caps="top"), "galv")
        z0 = PLINTH[2] + 0.04
        mb = _MB()
        bot = [(xp - 0.70, -0.60), (xp + 0.70, -0.60), (xp + 0.70, 0.60), (xp - 0.70, 0.60)]
        top = [(xp - 0.50, -0.45), (xp + 0.50, -0.45), (xp + 0.50, 0.45), (xp - 0.50, 0.45)]
        for i in range(4):
            j = (i + 1) % 4
            mid = ((bot[i][0] + bot[j][0]) / 2 - xp, (bot[i][1] + bot[j][1]) / 2)
            mb.f([(bot[i][0], bot[i][1], z0), (bot[j][0], bot[j][1], z0),
                  (top[j][0], top[j][1], zt), (top[i][0], top[i][1], zt)],
                 (mid[0], mid[1], 0.0))
        out.append((mb.obj(f"ped_{tag}"), "galv"))
        out.append((_box(f"pad_{tag}", xp - 0.8, xp + 0.8, -0.7, 0.7, z0, zt - 0.16,
                         drop=("+Z", "-Z")), "pad2"))
        for s in (-1.0, 1.0):
            for k, gx in enumerate((-0.30, 0.30)):
                # web in the YZ plane: local +X -> outboard (s Y), local +Z -> down;
                # legs 0.60 / 0.50 keep it inside the corridor (0.45 + 0.60 <= 1.10)
                out += _m(cs2_kit.gusset(f"ped{tag}_{'p' if s > 0 else 'n'}{k}", 0.60, 0.50,
                                         0.02, loc=(xp + gx, s * 0.45, zt),
                                         rot=(math.pi, 0.0, s * math.pi / 2.0)), "galv")
        out.append((_box(f"crosshead_{tag}", xp - 0.8, xp + 0.8, -1.8, 1.8, zt, zt + 0.5),
                    "galv"))
    out.append((_box("spine_main", p.x_nose + 0.6, p.x_wheel - 1.6, -0.3, 0.3, zt, zt + 0.6),
                "galv"))
    return out


def build_beams(p):
    """beam_*: underframe ribs below the soffit (tongue + both outer strips per station),
    pitch 2.75 from x_nose + 1.0, skipped within 1.2 of a pedestal, only x < x_wheel - 0.5."""
    zs = p.z_soffit
    hi, ho, ye = p.lane_y - p.slot_half, p.lane_y + p.slot_half, p.w_half - FILLET_R
    out = []
    k = 0
    x = p.x_nose + 1.0
    while x < p.x_wheel - 0.5:
        if min(abs(x - p.ped_f), abs(x - p.ped_r)) >= 1.2:
            for tag, (y0, y1) in (("t", (-hi, hi)), ("n", (-ye, -ho)), ("p", (ho, ye))):
                out.append((_box(f"beam_{k}{tag}", x - 0.125, x + 0.125, y0, y1, zs - 0.20, zs,
                                 drop=("+Z",)), "galv"))
            k += 1
        x += 2.75
    return out


# --------------------------------------------------------------------------------------
# The rope machine (DQ and gondola)
# --------------------------------------------------------------------------------------

def z_rail(p):
    return p.z_wheel + RIM_W / 2.0 + 0.10


def build_machine(p):
    X, Zw, R, Zr = p.x_wheel, p.z_wheel, p.lane_y, p.z_rope
    out = []
    # bullwheel rim (MUST-CHANGE 10: loc passed), spokes, hub, rope torus at the lane height
    out += _m(cs2_kit.sheave("bull", p.rim_r, RIM_W, (X, 0.0, Zw), "Z", segs=p.rim_segs,
                             hub=False, groove=0.07, r_hub=p.rim_r - 0.30), "darksteel")
    r_in, r_out = 0.36, p.rim_r - 0.28
    for k in range(8):
        a = k * math.pi / 4.0
        rm = (r_in + r_out) / 2.0
        out.append((cs2_kit.add_box(f"spoke_{k}", (r_out - r_in, 0.12, 0.08),
                                    (X + rm * math.cos(a), rm * math.sin(a), Zw),
                                    rot=(0.0, 0.0, a)), "galv"))
    out.append((cs2_kit.add_cyl("wheelhub", 0.40, 0.70, (X, 0.0, Zw), segments=12), "galv"))
    bpy.ops.mesh.primitive_torus_add(major_radius=p.torus_r, minor_radius=ROPE_R,
                                     major_segments=p.torus_segs, minor_segments=6,
                                     location=(X, 0.0, Zw))
    torus = bpy.context.active_object
    torus.name = "wheelrope"
    torus.data.name = "wheelrope"
    out.append((torus, "rope"))
    for s, tag in ((-1.0, "n"), (1.0, "p")):
        # T27: from the anchor (anchor_z = Zr unless ROPE_ON_CROWN) to the wheel tangent
        out.append((_cyl_pts(f"haulrope_{tag}", (p.anchor_x, s * R, anchor_z(p)), (X, s * R, Zr), ROPE_R,
                             6), "rope"))
    # detach rails: straights + a U-turn tube (MUST-CHANGE 13: kit tube_along, bend_rings 2)
    zr, rr = z_rail(p), R - 0.20
    for s, tag in ((-1.0, "n"), (1.0, "p")):
        out.append((_box(f"detrail_{tag}", p.x_nose + 0.5, X, s * rr - 0.05, s * rr + 0.05,
                         zr - 0.09, zr + 0.09), "galv"))
    upts = [(X + rr * math.cos(math.radians(a)), rr * math.sin(math.radians(a)), zr)
            for a in np.linspace(-90.0, 90.0, 17)]
    out.append((cs2_kit.tube_along("detrail_u", upts, 0.05, segments=4, caps="none",
                                   bend_rings=2), "galv"))
    hx = [p.x_nose + 1.5, p.x_nose + 0.40 * (X - p.x_nose), p.x_nose + 0.70 * (X - p.x_nose),
          X - 0.30]
    k = 0
    for s in (-1.0, 1.0):
        for x in hx:
            out.append((_box(f"railhang_{k}", x - 0.03, x + 0.03, s * rr - 0.03, s * rr + 0.03,
                             zr + 0.09, p.z_ceil, drop=("+Z", "-Z")), "galv"))
            k += 1
    for a in (-60.0, -20.0, 20.0, 60.0):
        cx, cy = X + rr * math.cos(math.radians(a)), rr * math.sin(math.radians(a))
        out.append((_box(f"railhang_{k}", cx - 0.03, cx + 0.03, cy - 0.03, cy + 0.03,
                         zr + 0.05, p.z_ceil, drop=("+Z", "-Z")), "galv"))
        k += 1
    # deceleration tyres in the nose zone: 6 per lane, 2 bank housings per lane
    zt = zr + 0.15
    for s, tag in ((-1.0, "n"), (1.0, "p")):
        yt = s * (R + 0.25)
        for i in range(6):
            out.append((cs2_kit.add_cyl(f"tyre_{tag}{i}", 0.20, 0.12,
                                        (p.x_nose + 0.9 + 0.55 * i, yt, zt),
                                        rot=(math.pi / 2.0, 0.0, 0.0), segments=8), "rubber"))
        for j in range(2):
            xc = p.x_nose + 0.9 + 0.55 * (3 * j + 1)
            out.append((_box(f"tyrebank_{tag}{j}", xc - 0.875, xc + 0.875, yt - 0.11, yt + 0.11,
                             zt + 0.25, zt + 0.45), "darksteel"))
    # drive (base) / tension (top) machinery above the hub, scaled to the headroom; gondola
    # draft 1: it stands ON the machinery deck (the shaft rises from the hub through the grating)
    zm0 = GD_DECK_Z if p.gondola else Zw + 0.50
    H = min(1.2, p.z_ceil - 0.15 - zm0)
    k = H / 1.2
    pre = "drive" if p.kind == "base" else "tension"
    out.append((cs2_kit.add_cyl(f"{pre}_shaft", 0.18, zm0 - (Zw + 0.35),
                                (X, 0.0, (zm0 + Zw + 0.35) / 2.0), segments=10, caps="none"),
                "darksteel"))
    if p.kind == "base":
        out.append((_box("drive_gearbox", X - 0.75, X + 0.75, -0.70, 0.70, zm0, zm0 + 0.80 * k),
                    "darksteel"))
        out.append((cs2_kit.add_cyl("drive_motor", 0.32, 1.20,
                                    (X - 1.35, 0.0, zm0 + 0.45 * k),
                                    rot=(0.0, math.pi / 2.0, 0.0), segments=12), "darksteel"))
        out.append((cs2_kit.add_cyl("drive_brake", 0.50, 0.08,
                                    (X - 0.35, 0.0, zm0 + 0.80 * k + 0.20 * k),
                                    segments=16), "darksteel"))
    else:
        out.append((_box("tension_carriage", X - 0.70, X + 1.30, -0.70, 0.70, zm0,
                         zm0 + 0.50 * k), "darksteel"))
        for s, tag in ((-1.0, "n"), (1.0, "p")):
            out.append((cs2_kit.add_cyl(f"tension_ram_{tag}", 0.12, 2.0,
                                        (X + 1.8, s * 0.45, zm0 + 0.25 * k),
                                        rot=(0.0, math.pi / 2.0, 0.0), segments=8),
                        "darksteel"))
        out.append((_box("tension_anchor", X + 2.80, X + 3.10, -0.80, 0.80, zm0,
                         zm0 + 0.60 * k), "darksteel"))
    return out


# Nitpick A (2026-10-13; Richard twenty-fifth look S-2: "Let's all apply the same PULLEY WHEELS / ROLLERS we use on
# the lift tower onto the lift stations"; build/nitpick-a-stations.md): the top's rope guides are the TOWERS' gantry
# sheave train (cs2_sheaves.dq_train - the code lift_tower builds every DQ / ETDQ / GD / ETGD head with): per lane
# NOSE_TRAIN_N sheaves with rubber liners on the towers' pitch, two sub rockers + pins, a main rocker + pin, the yoke and
# the hanger, every carrier INBOARD of the sheaves (the DQ head on the DQ top: r 0.223 V-groove, carriers at 2.512 /
# 2.44; the GD head on the gondola top: r 0.25 flat rims, carriers + 0.39), bevelled / finished as the towers
# (cs2_sheaves.finish_train). The rope runs ON the sheave tops (axle = z_rope - ROPE_R - r: here the modelled rope IS
# the code lane, the towers draw theirs on the axle line); the outer rim tip stays at the old guide frame's end
# (anchor_x - 0.10 = the frozen prefab z min). The hanger hangs off a girder stub on the guide beam out of the nose
# (the old guideframe_ line: |y| lane - 0.60 / - 0.70; the gondola's still hangs from the portal lintel). Parts in the
# old name space ("sheave_guide_*" = the train, LOD1 delete, C8 / G8 rope machine; "guideframe_*" = the beam, LOD2).
NOSE_TRAIN = True              # False = the pre-2026-10-13 3-sheave guides (A-B / negatives)
NOSE_TRAIN_N = 4               # sheaves per lane (2 subs on 1 main: the towers' smallest even train)
NOSE_TRAIN_PREFIX = "sheave_guide_"
NOSE_TRAIN_MAT = {"paint": "darksteel", "galv": "galv", "rubber": "rubber",   # tower key -> station key
                  "wheelsteel": "wheelsteel"}                                  # T62 round 2: cs2_sheaves.WHEEL_KEY
NOSE_GIRDER_X = 0.186          # lift_tower.DQ_GIRDER x: the girder stub's width along travel
NOSE_CARRIER_IN = 0.03         # ST1: every carrier vertex |y| <= lane - this (the towers' heads: 0.039 DQ / 0.064 GD)
# Nitpick A3 (2026-10-13; orchestrator ruling D1 on build/nitpick-a2-hanger-gondola.md): the axle rule per head. True =
# the AXLE ON THE LANE, the towers' convention (the code lane / modelled rope through the sheave centres, F10; every DQ
# tower and the hosted ETDQ carry the lane so): the DQ top, so the mirrored dq grip (lift_chair.DQ_OUTBOARD -1) clears
# the nose mains as it clears the towers' (S9: 11.5 mm). False = the rope tangent on the rim tops (axle = z_rope -
# ROPE_R - r, nitpick A's rule; the gondola top keeps it: the -Y cabin clears its mains by 0.12 m, S15).
# NOSE_AXLE_LEGACY = True rebuilds the nitpick-A2 DQ top (axle 3.422, 0.278 under the lane: the D1 negative).
NOSE_AXLE_ON_LANE = {"DQ": True, "GD": False}
NOSE_AXLE_LEGACY = False


def nose_axle_on_lane(family):
    """True when the `family` ("DQ" / "GD") nose train carries the lane through its sheave centres."""
    return bool(NOSE_AXLE_ON_LANE[family]) and not NOSE_AXLE_LEGACY


# T27 ROPE ON THE CROWN (2026-10-17, docs/research/field/2026-10-15/build/T27-rope-path.md; lift_tower.ROPE_ON_CROWN).
# False (default) = the shipped stations byte for byte (the HV sub-lane anchors at z_rope: base = the hosted ETDQ / ETGD
# compression AXLE, DQ top = the nose train's axle (rope through the sheaves, nitpick A3 D1), gondola top = on its rim
# tops for the 0.055 modelled rope). True (CLI --rope-on-crown): the rope rides every train it meets, and z_rope (the
# loading height: deck + clearance + hang) never moves - a first try that moved the anchors instead failed C8 (the DQ /
# gondola base chairs drag on the deck when the anchor drops under the compression train, the DQ top's chairs hit the
# RAISE BAR / UNLOAD boards when it rises onto the support crowns). So: base = the hosted compression tower's train
# rises (lift_tower.set_rope_on_crown: ETDQ / ETGD axle + crown_seat; a tower re-export, no station change); DQ top = the
# nose train turns hold-down (compression, NOSE "hold_down": axle = z_rope + seat, the rope under the sheaves - the line
# dives into the station, so a hold-down is also the right side); gondola top = unchanged train, the anchor on the rim
# tops for the VANILLA cable (axle + r + h = z_rope + 0.032; the modelled rope is 0.055, the vanilla 0.0866 high).
ROPE_ON_CROWN = False


def anchor_z(p):
    """The HV sub-lane anchor height of station `p` (z_rope unless ROPE_ON_CROWN on the gondola top)."""
    if not ROPE_ON_CROWN or not (p.kind == "top" and p.gondola):
        return p.z_rope
    import lift_tower as LTw
    T = nose_train_layout(p)
    groove = max(0.02, 0.18 * T["r"]) if T["groove"] is None else T["groove"]
    return round(T["axle"] + LTw.rope_seat(T["r"], T["w"], groove, chamfer=LTw.wheel_chamfer(T["r"], T["w"])), 4)


def rope_z_at(p, x):
    """The in-station rope / code-chord height at authoring x: anchor_z at anchor_x, z_rope at the wheel (and round it)."""
    za = anchor_z(p)
    if x >= p.x_wheel or abs(za - p.z_rope) < 1e-9:
        return p.z_rope
    t = max(0.0, (x - p.anchor_x) / (p.x_wheel - p.anchor_x))
    return za + (p.z_rope - za) * t


def rope_on_crown_block(p):
    """Manifest block (ROPE_ON_CROWN only): what T27 changes on this station (prefab frame: game (x, y, z) = (-y, z, x))."""
    za = anchor_z(p)
    d = round(za - p.z_rope, 4)
    if p.kind == "base":
        import lift_tower as LTw
        host = "ETGD" if p.gondola else "ETDQ"
        seat = LTw.crown_seat((LTw.VARIANTS_SHIPPED or LTw.VARIANTS)[host])
        how = (f"hosted {host} compression train rises {seat} (lift_tower.set_rope_on_crown: a tower re-export + its "
               f"prefab's m_Mesh); this station's prefab and mesh do not change")
    elif not p.gondola:
        how = f"the nose train turns hold-down: axle {nose_train_layout(p)['axle']} (was {p.z_rope}), rope under the rims"
    else:
        how = "the nose train is unchanged; the anchor sits on its rim tops for the vanilla cable (h 0.0866)"
    return {"anchor_z_old": p.z_rope, "anchor_z_new": za, "delta": d, "how": how,
            "rows": [{"field": f"ObjectSubLanes HV x {s * p.lane_y:+.1f} m_BezierCurve.{k}.y", "old": round(p.z_rope + o, 4),
                      "new": round(za + o, 4)} for s in (1, -1) for k, o in (("a", 0.0), ("b", 0.01), ("c", 0.02),
                                                                                  ("d", 0.03))] if d else [],
            "unchanged": "WheelLocal (the wheel stays at z_rope), StubTipLocalZ, the LV comm sub-lane, the hosted tower pose"}


# Nitpick A4 (2026-10-13; orchestrator ruling on build/nitpick-a3-check.md items 1c / 1d and N3). A BVH part-pair scan of
# the joined DQ top LOD0, A3 vs A2 (NOSE_AXLE_LEGACY), found 16 nose pairs the +0.278 rise created: 12 are the intended
# rope-through-the-sheave-centres ones (haulrope x liner / sub pin, declared in C14 below), 4 are real:
#  (a) clamp_comm0_l / _u x guideframe_p. The comm cable drops down the nose face at |y| 1.95 with its first clamp at the
#      middle of the drop (z 4.035). The guide beams rose to z 3.950..4.120 at |y| 1.94..2.06, so the clamps (|y| to
#      1.985) and the cable itself (to 1.969) sat inside the beam's inboard skin. The beam and its girder stub move
#      OUTBOARD: centre line lane - 0.52 (was - 0.60), inner face |y| 2.02 = 0.035 m clear of the clamps, 0.05 m of the
#      cable; the beam's end still sockets 3 cm into the nose band's edge (|y| 2.05).
#  (b) detrail_n_1 / detrail_p_1 x sheave_guide_hanger_n / _p. cs2_sheaves.dq_train places the hanger box at x 0, the
#      TOWER's centre (the tower's train is centred there); the station's train is shifted to x0 -10.99 (centres, yoke,
#      pins, girder stub), so the hangers stood 11 m behind their yokes, floating at the lane mid-station, where the rope
#      side detach rails cross them since the A3 rise. The hanger moves to its yoke's x (build_nose_train); ST1 gates it.
# DQ only: the gondola top's nose train keeps its beams and hangers as they were (its export is not part of A4; the same
# x 0 offset is there - an open finding in build/nitpick-a3-cabin-nose.md). NOSE_A4_LEGACY = True rebuilds the nitpick-A3
# DQ top (hangers at x 0, beams at lane - 0.60) byte for byte; ST1 reads the layout's own hanger x, C14 is skipped.
NOSE_A4_LEGACY = False
NOSE_HANGER_AT_YOKE = {"DQ": True, "GD": False}
NOSE_BEAM_IN = {"DQ": 0.52, "GD": 0.70}              # the guide beam's centre line = lane - this
NOSE_BEAM_IN_A3 = {"DQ": 0.60, "GD": 0.70}           # nitpick A3 (NOSE_A4_LEGACY)


def nose_hanger_at_yoke(family):
    """True when the `family` nose train's hanger boxes stand at their yokes' x (nitpick A4; DQ)."""
    return bool(NOSE_HANGER_AT_YOKE[family]) and not NOSE_A4_LEGACY


# Nitpick A4: the DOCUMENTED LIMIT of the DQ top's nose clearance (A3 check 1c; manifest sheave_trains.lane_pitch_limit,
# test_dq_family_seams S9 note, which re-measures every number below with lift_chair.carrier_sweep and fails on drift).
# Every clearance number in this family (S9: the shipped dq grip vs the rocker mains, 11.5 mm on DQ10-DQ20, the ETDQ and
# this nose) is measured on a LEVEL lane at the axle's height. The gap is a vertical one (the grip's wheels beside the
# main rockers): it closes 1:1 with the lane's height above the axle and ~10.6 mm per 1 % of lane pitch about the nose
# centre. No geometry change: the towers carry the same margin. Units: metres, nearest wheel / rocker_main_ pair, both
# lanes equal, 0.1 mm; "crossed" = the wheel's triangles cross the main's. S9's gate is 0.010.
NOSE_LANE_PITCH_LIMIT = {
    "limit": ("the 11.5 mm wheel / rocker_main clearance holds on a level lane at the axle's height only: the gate (>= 10 mm) "
              "is lost at 0.14 % pitch or +1.5 mm of lane height, the wheel crosses the main from 1.1 % or +12 mm"),
    "clearance_m_pitch_about_nose_centre": {"0 %": 0.0115, "0.14 %": 0.0100, "0.5 %": 0.0062, "1.0 %": 0.0009,
                                            "-1.0 %": 0.0008, "1.1 %": "crossed", "-1.1 %": "crossed", "25 %": "crossed"},
    "clearance_m_lane_height_above_axle": {"-0.010": 0.0215, "0": 0.0115, "+0.002": 0.0095, "+0.010": 0.0015,
                                           "+0.012": "crossed", "+0.030": "crossed"},
    "same_margin_on_towers": "DQ10-DQ20 and the ETDQ keep the same 11.5 mm on a level lane (S9); no geometry change",
    "in_game_nose_train": ("level in the world: 0 % at anchor a; at most 0.17 % (-3 cm over the 17.7 m run to the wheel) at "
                           "anchor d. The lane height is the open number: the HV anchors a..d rise 3.70..3.73 "
                           "(prefab_numbers stub()), and a lane that takes b / c / d rides +10 / +20 / +30 mm above the axle "
                           "(b: about 2 mm clear, c and d: crossed); which anchor each lane takes is unverified in game"),
    "basis": ("ChairPinSystem hangs every chair plumb (yaw-only rotation, ChairPinSystem.cs:201). LiftLoopCache "
              "(AppendTerminalTurn) runs the lane from the final span's end, the station-owned HV anchor (the train's outer "
              "tip, prefab z -12.125..-11.875), to the bullwheel tangent point as ONE straight 3D line; the wheel is at y "
              "3.70. A CS2 building is a rigid body placed with yaw only (ObjectToolSystem.cs:3811-3816; StationAlign.cs:156), "
              "so that run is level in the world on any terrain. The stations do NOT flatten the ground (DECISIONS 7; the "
              "NO-LEVEL BuildingTerraformOverride, PIPELINE section 6): the level lane is the rigid mesh, not a flattened "
              "lot. The final span's grade (line envelope up to 0.9, SkiCatalog detachable row) ends at the anchors, before "
              "the first main rocker (x -11.88): a swept kink at -12.125 / -12.0 / -11.875 with 5-90 % grade keeps 11.5 mm."),
}


def nose_train_layout(p):
    """The top's nose train numbers (authoring): the tower head it copies, sheaves / pitch / r / w / segs / groove,
    the axle (nitpick A3: ON the lane for the DQ head - the towers' convention; the rope tangent on the sheave tops
    for the GD head), the centre x (outer rim tip = anchor_x - 0.10), the carriers' |y|, the hanger box, the beam and
    the girder stub (both hung off the axle: they move with it)."""
    import cs2_sheaves as S
    gd = bool(p.gondola)
    fam = "GD" if gd else "DQ"
    sh = S.GD_SHEAVE if gd else S.DQ_SHEAVE
    lane_tower = 3.0 if gd else 2.6
    if abs(p.lane_y - lane_tower) > 1e-9:
        raise ContractError(f"ST1: the {fam} head's carriers are placed for lane {lane_tower}, station lane {p.lane_y}")
    n, r, w, pitch = NOSE_TRAIN_N, float(sh["r"]), float(sh["w"]), float(sh["pitch"])
    # T62 v2: cs2_sheaves.wheel (round 3: the one 16-segment grooved wheel at the family r / w); v1: the head's own wheel
    wv2 = S.wheel_spec(fam) if S.wheels_v2() else None
    segs = int(wv2["segs"]) if wv2 else int(sh.get("segs", 12))
    groove = None if wv2 else sh.get("groove")
    on_lane = nose_axle_on_lane(fam)
    axle = round(p.z_rope if on_lane else p.z_rope - ROPE_R - r, 6)
    hold = bool(ROPE_ON_CROWN and on_lane)          # T27: the DQ head goes OVER the rope (hold-down, rim bottoms on it)
    if hold:
        import lift_tower as LTw
        g = max(0.02, 0.18 * r) if groove is None else groove
        axle = round(p.z_rope + LTw.rope_seat(r, w, g, chamfer=LTw.wheel_chamfer(r, w)), 6)
        on_lane = False
    x_out = round(p.anchor_x - 0.10, 6)
    x0 = round(x_out + r + (n - 1) / 2.0 * pitch, 6)
    cdy = S.GD_CAR_DY if gd else 0.0
    hb = dict(S.DQ_HANGER)
    if gd:
        hb.update(y0=round(S.DQ_HANGER["y0"] + cdy, 6), y1=round(S.DQ_HANGER["y1"] + cdy, 6))
    yf = p.lane_y - (NOSE_BEAM_IN_A3 if NOSE_A4_LEGACY else NOSE_BEAM_IN)[fam]
    z0, z1 = round(axle + hb["z0"], 6), round(axle + hb["z1"], 6)
    at_yoke = nose_hanger_at_yoke(fam)
    out = dict(family=fam, n=n, r=r, w=w, pitch=pitch, segs=segs, groove=groove, ngon=bool(wv2 and wv2["ngon"]),
                axle=axle, axle_on_lane=on_lane, x_out=x_out, x0=x0, x_in=round(x0 + (n - 1) / 2.0 * pitch + r, 6),
                hanger_at_yoke=at_yoke, hanger_x=x0 if at_yoke else 0.0,
                sub_y=round(S.DQ_SUB_Y + cdy, 6), main_y=round(S.DQ_MAIN_Y + cdy, 6),
                pin_s=S.GD_PIN_S if gd else S.DQ_PIN_S, hanger=hb, hz=S.DQ_HANGER_Z_INSET, link=S.DQ_LINK,
                beam=dict(x0=x_out, x1=p.x_nose + 0.05, y0=yf - 0.06, y1=yf + 0.06, z0=z0, z1=z1),
                girder=dict(x0=round(x0 - NOSE_GIRDER_X / 2.0, 6), x1=round(x0 + NOSE_GIRDER_X / 2.0, 6),
                            y0=yf + 0.06, y1=hb["y0"], z0=z0, z1=z1))
    if hold:
        out["hold_down"] = True
    return out


def build_nose_train(p):
    """The top's nose sheave trains (NOSE_TRAIN): cs2_sheaves.dq_train for both lanes, finished as the towers'
    (finish_train), the girder stubs and the guide beams (+ the gondola's lintel straps). [(obj, material key)]."""
    import cs2_sheaves as S
    T = nose_train_layout(p)
    pre = NOSE_TRAIN_PREFIX

    def cents(s):
        return S.dq_centres(T["n"], T["pitch"], T["axle"], 0.0, 2.0 * p.lane_y, s, x0=T["x0"])
    raw = S.dq_train(cents, T["n"], T["axle"], T["r"], T["w"], T["segs"], T["groove"], T["sub_y"], T["main_y"],
                     T["pin_s"], T["hanger"], T["hz"], link=T["link"], prefix=pre, ngon=T["ngon"])
    for o, _k in raw:
        # nitpick A4 (b): dq_train puts the hanger boxes at x 0 (the tower's centre); the station's yokes stand at x0
        if o.name in (f"{pre}hanger_n", f"{pre}hanger_p"):
            o.location.x = T["hanger_x"]
    S.finish_train(raw, "DQ", prefix=pre)
    out = [(o, NOSE_TRAIN_MAT[k]) for o, k in raw]
    B, G = T["beam"], T["girder"]
    for s, tag in ((-1.0, "n"), (1.0, "p")):
        ya, yb = sorted((s * G["y0"], s * G["y1"]))
        out.append((_box(f"{pre}girder_{tag}", G["x0"], G["x1"], ya, yb, G["z0"], G["z1"]), "darksteel"))
        ya, yb = sorted((s * B["y0"], s * B["y1"]))
        out.append((_box(f"guideframe_{tag}", B["x0"], B["x1"], ya, yb, B["z0"], B["z1"]), "galv"))
        if p.gondola:   # hang the beam from the lintel above the portal (as the 3-sheave frame did)
            yc = s * (B["y0"] + B["y1"]) / 2.0
            out.append((_box(f"guideframe_h{tag}", p.x_nose - 0.16, p.x_nose - 0.04, yc - 0.05, yc + 0.05,
                             B["z1"], 5.80), "galv"))
    return out


def nose_train_numbers(p):
    """Manifest block `sheave_trains` (top stations): the head copied, counts / gauge / pitch / r, the axle and the
    rope tangent, the train's x extent and the carriers' |y|."""
    import cs2_sheaves as S
    T = nose_train_layout(p)
    rule = ("axle on the lane: the code lane / modelled rope through the sheave centres (the towers' convention, "
            "F10; nitpick A3 D1)" if T["axle_on_lane"] else "rope tangent on the rim tops (nitpick A)")
    return {"builder": "cs2_sheaves.dq_train (lift_tower's gantry train) + finish_train", "head": T["family"],
            "per_lane": T["n"], "lanes_y": [-p.lane_y, p.lane_y], "gauge_m": round(2.0 * p.lane_y, 3),
            "pitch_m": T["pitch"], "sheave_r_m": T["r"], "sheave_w_m": T["w"], "segments": T["segs"],
            "groove": "flat rim" if T["groove"] == 0.0 else "V (0.18 r)", "axle_z": T["axle"],
            "axle_rule": rule, "axle_on_lane": T["axle_on_lane"],
            "rope_z": p.z_rope, "rim_top_z": round(T["axle"] + T["r"], 6),
            "rope_on_sheave_top": None if T["axle_on_lane"] else round(T["axle"] + T["r"] + ROPE_R, 6),
            "x_extent": [T["x_out"], T["x_in"]], "x_nose": p.x_nose,
            "carriers_abs_y": {"sub": T["sub_y"], "main_yoke": T["main_y"],
                               "hanger": [T["hanger"]["y0"], T["hanger"]["y1"]]},
            **({"wheels": {"generation": "v2 (T62 round 3: cs2_sheaves.wheel, 16 segments, groove, seen hub bosses)",
                           "builder": S.WHEEL_ID, "tris_per_wheel_max": S.WHEEL_TRIS, "material": S.WHEEL_KEY,
                           "lod": {str(k): v["form"] for k, v in S.WHEEL_LOD.items()}}} if S.wheels_v2() else {})}


def build_nose_guides(p):
    """Top station rope guides: NOSE_TRAIN (2026-10-13) = the towers' sheave train (build_nose_train); off = the
    pre-2026-10-13 guides (build_nose_guides_legacy)."""
    return build_nose_train(p) if NOSE_TRAIN else build_nose_guides_legacy(p)


def build_nose_guides_legacy(p):
    """Top station: 3 support sheaves per lane under the rope from the nose to the anchor
    (guide tip), carried by a frame inboard of the hanger column (|y| = lane - 0.60)."""
    out = []
    za = p.z_rope - ROPE_R - 0.20
    # gondola draft 1: 0.70 inboard, so the lintel hangers clear the portal frame trim
    yf = p.lane_y - (0.70 if p.gondola else 0.60)
    for s, tag in ((-1.0, "n"), (1.0, "p")):
        out.append((_box(f"guideframe_{tag}", p.anchor_x - 0.10, p.x_nose + 0.05, s * yf - 0.06,
                         s * yf + 0.06, za - 0.11, za + 0.11), "galv"))
        if p.gondola:   # hang the frame from the lintel above the portal
            out.append((_box(f"guideframe_h{tag}", p.x_nose - 0.16, p.x_nose - 0.04,
                             s * yf - 0.05, s * yf + 0.05, za + 0.11, 5.80), "galv"))
        for k, dx in enumerate((0.35, 1.00, 1.65)):
            x = p.x_nose - dx
            out.append((cs2_kit.add_cyl(f"sheave_guide_{tag}{k}", 0.20, 0.10, (x, s * p.lane_y, za),
                                        rot=(math.pi / 2.0, 0.0, 0.0), segments=10), "galv"))
            out.append((_cyl_pts(f"sheave_guide_axle_{tag}{k}", (x, s * (yf + 0.06), za),
                                 (x, s * (p.lane_y - 0.05), za), 0.03, 6), "galv"))
    return out


# --------------------------------------------------------------------------------------
# Access, buildings, comm, dressing
# --------------------------------------------------------------------------------------

def _ladder(tag, x, y, z0, z1, half=0.28, rungs=None):
    """lrail_<tag>0/1 (r 0.03, 6 seg, caps top, standing on the floor) + lrung_<tag>NN
    (r 0.015, 4 seg, no caps) at a 0.30 pitch, 0.56 wide across Y. `rungs` = explicit rung
    heights (Revision 2: the nose ladder's grid, see nose_ladder_layout), else z0 + 0.30 i."""
    out = []
    for k, dy in enumerate((-half, half)):
        out.append((cs2_kit.add_cyl(f"lrail_{tag}{k}", 0.03, z1 - z0, (x, y + dy, (z0 + z1) / 2.0),
                                    segments=6, caps="top"), "galv"))
    if rungs is None:
        n = int((z1 - z0 - 0.15) / 0.30)
        rungs = [z0 + 0.30 * i for i in range(1, n + 1)]
    for i, z in enumerate(rungs, start=1):
        out.append((cs2_kit.add_cyl(f"lrung_{tag}{i:02d}", 0.015, 2.0 * half - 0.02,
                                    (x, y, z), rot=(math.pi / 2.0, 0.0, 0.0),
                                    segments=4, caps="none"), "galv"))
    return out


def nose_access(p):
    """Revision 1 (a, c): the nose landing (x0, x1, half-width), the gangplank (x0, x1,
    half-width, closed end?) and the front ladder (x, y). Base: the landing stops 0.06 short
    of the hosted ETDQ (inboard end x -8.612) and the gangplank runs out between its deck
    strips (inner edge 0.695) to 0.05 short of its ladder landing plates (x -10.15), open
    there. Top: the 1.0 m balcony + a 1.0 m gangplank inside the guide frames (|y| 1.94),
    closed by the U handrail."""
    xn = p.x_nose
    if p.hosted_tower:
        land = (xn - LAND_D_HOSTED, xn, LAND_HALF_HOSTED)
        gang = (GANG_END_HOSTED, xn - LAND_D_HOSTED, GANG_HALF_HOSTED, False)
    else:
        land = (xn - 1.0, xn, BALC_HALF)
        gang = (xn - 1.0 - GANG_L_TOP, xn - 1.0, GANG_HALF_TOP, True)
    ladder = (xn - NOSE_LADDER_OFF, NOSE_LADDER_Y_D1 if roof_route_d1(p) else NOSE_LADDER_Y)
    return land, gang, ladder


def roof_route_d1(p):
    """Detail pass 1, MC4: the base's front ladder (from the landing) moved to NOSE_LADDER_Y_D1 /
    NOSE_LADDER_HALF_D1 and the outer landing rail segment opened (base with the 1c ground flight
    only; the top keeps its straight run through the ladder-way)."""
    return (ROOF_ROUTE_D1 and not p.gondola and p.hosted_tower and GF_ON_BASE)


def nose_ladder_half(p):
    return NOSE_LADDER_HALF_D1 if roof_route_d1(p) else 0.28


def nose_ladder_layout(p):
    """Revision 2 (second look 3'): every number of the ground-to-crown nose ladder, shared
    by build_access, assert_revision2 and the manifest. The rung grid is the landing's
    (z_soffit + 0.30 k, k < 0 below it): one rung sits flush with the landing top in the
    ladder-way, the upper rungs keep their Revision 1 heights, the lowest is 0.35 above
    grade. The ladder-way is cut from the rails' nose side (+X, WELL_FRONT) to WELL_DEPTH_MAX
    in front of the rungs, stopping WELL_RIM inside the landing's outer edge, whose handrail
    posts stand on that rim; |y - ladder y| <= WELL_HALF_Y."""
    (l0, l1, lh), _gang, (lx, ly) = nose_access(p)
    zs = p.z_soffit
    z_top = p.z_crown + 0.4
    k_lo = math.ceil((NZ_FOOT_H + NZ_FIRST_RUNG_MIN - zs) / NZ_PITCH - 1e-9)
    k_hi = math.floor((z_top - 0.15 - zs) / NZ_PITCH + 1e-9)
    gf = ground_flight(p)
    if gf is not None:
        # Revision 1c (base): the front ladder stands on the landing (Revision 1 rungs), no
        # ladder-way, no footing of its own; the ground flight is gf (beside the landing)
        return {"x": lx, "y": ly, "z_foot": zs, "z_top": z_top,
                "rungs": [zs + NZ_PITCH * k for k in range(1, k_hi + 1)], "well": None,
                "foot": None, "landing": (l0, l1, lh), "ground": gf}
    rungs = [zs + NZ_PITCH * k for k in range(k_lo, k_hi + 1)]
    wx0 = max(l0 + WELL_RIM, lx - WELL_DEPTH_MAX)
    well = (wx0, lx + WELL_FRONT, ly - WELL_HALF_Y, ly + WELL_HALF_Y)
    foot = (lx + NZ_FOOT_X[0], lx + NZ_FOOT_X[1], ly - NZ_FOOT_HALF_Y, ly + NZ_FOOT_HALF_Y,
            0.0, NZ_FOOT_H)
    return {"x": lx, "y": ly, "z_foot": NZ_FOOT_H, "z_top": z_top, "rungs": rungs,
            "well": well, "foot": foot, "landing": (l0, l1, lh), "ground": None}


def ground_flight(p):
    """Revision 1c: the BASE's ground flight beside the landing (GF_* above), or None (top,
    gondola, GF_ON_BASE off). A through ladder at the landing's +Y edge: rails at x0 / x1
    (along X) on the rung line y = GF_Y, from the footing top to the landing + GF_EXT; rungs on
    the landing's 0.30 grid from 0.35 above grade up to the step-off rung flush with the
    landing; the landing's +Y edge at y - GF_STEP; the climber on +Y (climbing direction +Y)."""
    if p.gondola or not p.hosted_tower or not GF_ON_BASE:
        return None
    (l0, l1, _lh), _gang, _lad = nose_access(p)
    zs = p.z_soffit
    x0, x1 = l0 + GF_X_IN[0], l1 - GF_X_IN[1]
    k_lo = math.ceil((NZ_FOOT_H + NZ_FIRST_RUNG_MIN - zs) / NZ_PITCH - 1e-9)
    y = GF_Y
    return {"x": (x0, x1), "y": y, "z_foot": NZ_FOOT_H, "z_top": zs + GF_EXT,
            "rungs": [zs + NZ_PITCH * k for k in range(k_lo, 1)], "edge_y": round(y - GF_STEP, 6),
            "dir": (0.0, 1.0), "half": (x1 - x0) / 2.0,
            "foot": (x0 - GF_FOOT_X[0], x1 + GF_FOOT_X[1], y + GF_FOOT_Y[0], y + GF_FOOT_Y[1],
                     0.0, NZ_FOOT_H)}


def pf_layout(p):
    """Detail pass 1 (MC5): the pedestal ladder beside spine_main (None = the 1c ladder on the
    spine). Rung line along Y at x = ped_f + PF_X_OFF, centre y PF_Y, rails PF_HALF either side,
    climber on +X; rungs every 0.30 from 0.30 above the deck to 0.05 under the soffit (the stop),
    rails from the deck to 0.02 under the soffit (inside the hatch opening, below its leaf);
    the hatch (cs2_liftdress.hatch_frame, PF_HATCH opening) centred PF_HATCH_DX in front of the
    rung line on soffit_tongue's underside, its opening cut into the soffit (build_soffit)."""
    if p.gondola or not (DETAIL1 and PF_OFF_SPINE):
        return None
    zs = p.z_soffit
    x = p.ped_f + PF_X_OFF[p.kind]
    top = round(zs - 0.05, 4)
    n = int(math.floor((top - DECK_H) / NZ_PITCH + 1e-9))
    rungs = [round(top - NZ_PITCH * k, 4) for k in range(n - 1, -1, -1)]   # 0.60 .. 3.30
    hx = x + PF_HATCH_DX
    h2 = PF_HATCH / 2.0
    return {"x": x, "y": PF_Y, "half": PF_HALF, "z_top": round(zs - 0.02, 4), "rungs": rungs,
            "hatch": (hx, PF_Y), "hole": (hx - h2, hx + h2, PF_Y - h2, PF_Y + h2), "stop": zs,
            "dir": (1.0, 0.0)}


def _ladder_x(tag, x0, x1, y, z0, z1, rungs):
    """Revision 1c: a ladder in the XZ plane - lrail_<tag>0/1 at x0 / x1 (r 0.03, 6 seg, caps
    top) and lrung_<tag>NN along X (r 0.015, 4 seg) at the given heights; _ladder's parts."""
    out = []
    for k, x in enumerate((x0, x1)):
        out.append((cs2_kit.add_cyl(f"lrail_{tag}{k}", 0.03, z1 - z0, (x, y, (z0 + z1) / 2.0),
                                    segments=6, caps="top"), "galv"))
    for i, z in enumerate(rungs, start=1):
        out.append((cs2_kit.add_cyl(f"lrung_{tag}{i:02d}", 0.015, (x1 - x0) - 0.02,
                                    ((x0 + x1) / 2.0, y, z), rot=(0.0, math.pi / 2.0, 0.0),
                                    segments=4, caps="none"), "galv"))
    return out


def build_access(p):
    """Revision 1: nose landing + gangplank (gratings, handrails), the outer ladder up the
    NOSE end to the crown + 0.4 with 4 standoffs to the band, ONE underside ladder (front
    pedestal), and on the base a queue corral on the operator / loading side (+Y).
    Revision 2: the nose ladder runs from a ground footing (lfoot_nz, z 0) through a
    ladder-way in the landing (four landing gratings round it) to the crown + 0.4, clamped
    under the landing (lstand_nzl*); no cage (Collins has none). Revision 1c (BASE only,
    ground_flight): the ground flight is a through ladder on the landing's +Y edge (lrail_gf*,
    lrung_gf*, lfoot_gf, lstand_gfl*), the landing one grating trimmed to its step-across,
    the front ladder on the landing (Revision 1), the gangplank's +Y rail from its own end."""
    zs = p.z_soffit
    (l0, l1, lh), (g0, g1, gh, closed), (lx, ly) = nose_access(p)
    lay = nose_ladder_layout(p)
    gf = lay["ground"]
    out = []
    if gf is None:
        wx0, wx1, wy0, wy1 = lay["well"]
        # the landing = 4 gratings round the ladder-way; the strips' faces against the main
        # pieces are buried and dropped (the gangplank's +X lip rule)
        for nm, x0, x1, y0, y1, drop in (("balc", l0, l1, wy1, lh, ()),
                                         ("balc_n", l0, l1, -lh, wy0, ()),
                                         ("balc_o", l0, wx0, wy0, wy1, ("+Y", "-Y")),
                                         ("balc_i", wx1, l1, wy0, wy1, ("+Y", "-Y"))):
            g = cs2_kit.grating(nm, x1 - x0, y1 - y0, t=0.06,
                                loc=((x0 + x1) / 2.0, (y0 + y1) / 2.0, zs - 0.03))
            for o, _q in g:
                if o.name.endswith("_lip") and drop:
                    _drop_dir(o, *drop)
            out += _m(g, "galv")
    else:
        # Revision 1c (base): one landing grating, no ladder-way, its +Y edge trimmed to the
        # ground flight's step-across
        ye = gf["edge_y"]
        out += _m(cs2_kit.grating("balc", l1 - l0, ye + lh, t=0.06,
                                  loc=((l0 + l1) / 2.0, (ye - lh) / 2.0, zs - 0.03)), "galv")
    gang = cs2_kit.grating("gang", g1 - g0, 2.0 * gh, t=0.06, loc=((g0 + g1) / 2.0, 0.0, zs - 0.03))
    for o, _q in gang:              # the lip face against the landing is hidden: drop it
        if o.name.endswith("_lip") and gf is None:      # 1c: |y| edge_y..gh of it now shows
            _drop_dir(o, "+X")
    out += _m(gang, "galv")
    ly_r, gy_r = lh - 0.05, gh - 0.05
    xe = g0 + 0.05
    if closed:
        paths = {"gang": [(l1 - 0.02, -ly_r), (l0 + 0.05, -ly_r), (l0 + 0.05, -gy_r), (xe, -gy_r),
                          (xe, gy_r), (l0 + 0.05, gy_r), (l0 + 0.05, ly_r), (l1 - 0.02, ly_r)]}
    else:                           # open at the tower's ladder landing
        paths = {f"gang{tag}": [(l1 - 0.02, s * ly_r), (l0 + 0.03, s * ly_r), (l0 + 0.03, s * gy_r),
                                (xe, s * gy_r)] for s, tag in ((-1.0, "n"), (1.0, "p"))}
        if gf is not None:
            # 1c: the +Y landing wing is gone - the ground flight's rails guard that edge (the
            # rail gap between them is the step-through); the gangplank's +Y rail starts on it
            paths["gangp"] = [(l0 - 0.03, gy_r), (xe, gy_r)]
        if roof_route_d1(p):
            # detail pass 1, MC4: the outer landing rail segment between post_gangn_1 and
            # post_gangn_2 (x l0 + 0.03, y -1.25 .. -0.50) and its kick plate go - an open rail
            # gap at the front ladder onto the hosted ETDQ's deck strip (the bottom stop's
            # standing surface); the landing's -Y edge rail (gangna) and the gangplank's -Y rail
            # (gangnb) keep their posts at the gap's ends
            del paths["gangn"]
            paths["gangna"] = [(l1 - 0.02, -ly_r), (l0 + 0.03, -ly_r)]
            paths["gangnb"] = [(l0 + 0.03, -gy_r), (xe, -gy_r)]
    for nm in sorted(paths):
        hr = cs2_kit.handrail(nm, [(x, y, zs) for x, y in paths[nm]], h=1.10, knee=0.55,
                              kick=0.10, post_every=1.5, segs=6, radius=0.028)
        if nm == "gangnb":
            # MC4: the gangplank's -Y kick plate starts outside the front ladder's climbing zone
            # (x < lx - CLIMB_MIN - 0.05): the zone's floor is the landing + the ETDQ deck strip
            keep = []
            for o, q in hr:
                if o.name.startswith("kick_"):
                    me = o.data
                    bpy.data.objects.remove(o, do_unlink=True)
                    bpy.data.meshes.remove(me)
                else:
                    keep.append((o, q))
            x0k = GANG_KICK_X0_D1
            hr = keep + [(cs2_kit.add_box("kick_gangnb_0", (x0k - xe, 0.02, 0.10),
                                          ((x0k + xe) / 2.0, -gy_r, zs + 0.05 + 0.005)), "q")]
        out += _m(hr, "galv")
    # (a) the outer ladder, in front of the nose band / glass; Revision 2 (3'): from the
    # ground footing through the landing's ladder-way to the crown + 0.4; detail pass 1 (MC4):
    # on the base 0.48 wide at y NOSE_LADDER_Y_D1
    hw = nose_ladder_half(p)
    out += _ladder("nz", lx, ly, lay["z_foot"], lay["z_top"], half=hw, rungs=lay["rungs"])
    if lay["foot"] is not None:
        out.append((_box("lfoot_nz", *lay["foot"], open_bottom=True), "concrete"))
    k = 0
    for z in (zs + 1.0, p.z_top - 0.30):
        for dy in (-hw, hw):
            out.append((_box(f"lstand_nz{k}", lx + 0.02, p.x_nose, ly + dy - 0.02, ly + dy + 0.02,
                             z - 0.02, z + 0.02, drop=("+X",)), "galv"))
            k += 1
    if gf is None:
        for k, dy in enumerate((-0.28, 0.28)):     # rail clamps under the ladder-way's nose strip
            out.append((_box(f"lstand_nzl{k}", lx + 0.02, wx1 + 0.06, ly + dy - 0.02,
                             ly + dy + 0.02, zs - 0.11, zs - 0.06), "galv"))
    else:
        # Revision 1c (base): the ground flight beside the landing - a through ladder on the
        # landing's +Y edge from its own footing, clamped under the landing's edge
        (x0, x1), y = gf["x"], gf["y"]
        out += _ladder_x("gf", x0, x1, y, gf["z_foot"], gf["z_top"], gf["rungs"])
        out.append((_box("lfoot_gf", *gf["foot"], open_bottom=True), "concrete"))
        for k, x in enumerate((x0, x1)):
            out.append((_box(f"lstand_gfl{k}", x - 0.02, x + 0.02, gf["edge_y"] - 0.06, y - 0.025,
                             zs - 0.11, zs - 0.06), "galv"))
    # (b) ONE underside ladder: the front pedestal's (the rear one and the back-end ladder
    # are gone); detail pass 1 (MC5): beside the spine at |y| PF_Y, up to a soffit hatch
    pf = pf_layout(p)
    if pf is None:
        out += _ladder("pf", p.ped_f + 1.35, 0.0, DECK_H, zs)
    else:
        out += _ladder("pf", pf["x"], pf["y"], DECK_H, pf["z_top"], half=PF_HALF, rungs=pf["rungs"])
        out += _LD().hatch_frame("sofpf", (pf["hatch"][0], pf["hatch"][1], zs), size=PF_HATCH)
    if p.kind == "base":
        # (d) the queue corral on the loading side (+Y), back corner beside the bullwheel
        # turn (chair envelope r <= 4.0 about the wheel), clear of the downspout (x_back - 0.4)
        for i, y in enumerate((4.30, 5.40)):
            rail = cs2_kit.handrail(f"maze{i}", [(p.x_wheel + 0.30, y, DECK_H),
                                                (p.x_back - 1.0, y, DECK_H)],
                                    h=1.0, knee=0.5, kick=0.10, post_every=2.0, segs=6,
                                    radius=0.028)
            keep = [(o, q) for o, q in rail if not o.name.startswith("kick_")]
            for o, _q in rail:           # contract: kick 0 -> no kick plate at all (a
                if o.name.startswith("kick_"):   # zero-height box would be degenerate)
                    me = o.data
                    bpy.data.objects.remove(o, do_unlink=True)
                    bpy.data.meshes.remove(me)
            out += _m(keep, "galv")
    return out


CAB = (3.0, 2.2, 2.6)
OPB_Y = (5.60, 7.90)
# Nitpick A (2026-10-13, Richard twenty-fifth look S-1: "make sure the ORIENTATION of the top one makes sense for
# which way the lifts will spin ... you wouldn't watch from the far side of the lift"; build/nitpick-a-stations.md):
# the operator cabin stands beside the station's UP lane - the base's loading lane (+Y: carpet, LOWER BAR, maze, WAIT
# HERE) and the top's ARRIVING / unload lane (-Y: RAISE BAR, UNLOAD, the unload stripe, the 7.5 m apron). The DQ
# top's cabin stood on +Y across the bullwheel from its unload; it moves onto the -Y apron BEFORE the unload line
# (its door, +X, opens toward the line 1.6 m away; windows toward the arriving chairs, the lane and the apron). It
# stands ON the deck (floor = DECK_H, no opbase), inside the deck's -Y edge with its roof slab + drips (-7.47).
# OP_SIDE_Y = the cabin's authoring y sign per kind = D1_TRAVEL[kind]["lane"] (contract O1 gates it).
OP_SIDE_Y = {"base": 1.0, "top": -1.0}
TOP_CAB = dict(x=(-4.60, -1.60), y=(-7.30, -5.10), z=0.30)   # the top's cabin on the -Y apron (z = DECK_H)
TOP_CAB_LEGACY = False         # True = the pre-2026-10-13 top cabin on +Y (the O1 negative)


def cab_layout(p):
    """The operator cabin of this station (DQ): x0, x1, y0, y1 (walls), zf (floor), lane_y (the face toward the
    lane: its window sill / LIFT OPS board), out_y (the outer face), door_y (the +X door centre, at the lane end),
    s (the side: +1 = +Y, -1 = -Y). Base and the legacy top: the +Y cabin beside the bullwheel (x_wheel - 3.5 ..
    x_wheel - 0.5, y 5.60 .. 7.80, on the opbase 0.15); the top since 2026-10-13: TOP_CAB on the -Y apron."""
    if p.kind == "base" or TOP_CAB_LEGACY:
        cy0, cy1, zf = 5.60, 7.80, 0.15
        return dict(x0=p.x_wheel - 3.5, x1=p.x_wheel - 0.5, y0=cy0, y1=cy1, zf=zf, lane_y=cy0, out_y=cy1,
                    door_y=cy0 + 0.75, s=1.0, opbase=True)
    (x0, x1), (y0, y1), zf = TOP_CAB["x"], TOP_CAB["y"], TOP_CAB["z"]
    return dict(x0=x0, x1=x1, y0=y0, y1=y1, zf=zf, lane_y=y1, out_y=y0, door_y=y1 - 0.75, s=-1.0, opbase=False)


def _hut(prefix, roof, x0, x1, y0, y1, z0, h):
    """Cedar box walls (no top / bottom), paint1 roof slab + drip edges."""
    out = [(_box(f"{prefix}_walls", x0, x1, y0, y1, z0, z0 + h, drop=("+Z", "-Z")), "cedar")]
    slab = _box(roof, x0 - 0.15, x1 + 0.15, y0 - 0.15, y1 + 0.15, z0 + h, z0 + h + 0.12)
    out.append((slab, "paint1"))
    out += _m(cs2_kit.drip_edge(slab, drop=0.06, t=0.02), "galv")
    return out


def build_buildings(p):
    """Operator cabin (both) and engine room (base) on one open-bottom opbase (MUST-CHANGE 9:
    the engine room gets y 5.65-7.85 beside the cabin and its footing is the extended opbase)."""
    xw = p.x_wheel
    # F8: 0.05 inboard of MUST-CHANGE 9's 5.65-7.85, so roof slab + drip edge end at y 7.97,
    # inside the lot line 8.0 (they reached 8.02); C6 checks the lateral lot line exactly
    # Nitpick A (2026-10-13): the top's cabin stands on the -Y apron beside its unload lane (cab_layout)
    L = cab_layout(p)
    cy0, cy1 = L["y0"], L["y1"]
    zf = L["zf"]
    x_lo = xw - 6.7 if p.kind == "base" else xw - 3.6
    out = []
    if L["opbase"]:
        out.append((_box("opbase", x_lo, xw - 0.4, OPB_Y[0], OPB_Y[1], 0.0, zf, open_bottom=True),
                    "concrete"))
    cx0, cx1 = L["x0"], L["x1"]
    out += _hut("cab", "cabroof", cx0, cx1, cy0, cy1, zf, CAB[2])
    zb = zf + 1.65
    # MUST-CHANGE 12: the +X (door) pane is dropped on both kinds
    out.append((_box("cabwin", cx0 - 0.025, cx1 + 0.025, cy0 - 0.025, cy1 + 0.025, zb - 0.5,
                     zb + 0.5, drop=("+Z", "-Z", "+X")), "glass"))
    for i, (mx, my) in enumerate(((cx0, cy0), (cx0, cy1), (cx1, cy0), (cx1, cy1))):
        out.append((_box(f"cabmull_{i}", mx - 0.045, mx + 0.045, my - 0.045, my + 0.045,
                         zb - 0.5, zb + 0.5), "galv"))
    out += _m(cs2_kit.door("cab", w=0.85, h=1.95, reveal=0.04, loc=(cx1, L["door_y"], zf),
                           facing="+X"), "galv", door_cab_leaf="paint1")
    out.append((cs2_kit.add_cyl("stovepipe", 0.055, 0.85, (cx0 + 0.6, L["out_y"] - L["s"] * 0.5,
                                                          zf + CAB[2] + 0.12 + 0.40),
                                segments=6, caps="top"), "galv"))
    if not FINE_DETAIL:            # fine-detail: the existing WallLightShortRange row moves onto it
        ly0, ly1 = sorted((L["lane_y"] + L["s"] * 0.60, L["lane_y"] + L["s"] * 0.90))
        out.append((_box("lamp_cab", cx1 + 0.02, cx1 + 0.12, ly0, ly1, zf + 2.18,
                         zf + 2.28), "lamp"))
    if p.kind == "base":
        ex0, ex1 = xw - 6.6, xw - 3.6
        out += _hut("engroom", "engroof", ex0, ex1, cy0, cy1, zf, CAB[2])
        out += _m(cs2_kit.door("eng", w=0.85, h=1.95, reveal=0.04,
                               loc=(ex0, (cy0 + cy1) / 2.0, zf), facing="-X"), "galv",
                  door_eng_leaf="paint1")
        for j, hx in enumerate((ex0 + 0.85, ex1 - 0.85)):
            out += _m(cs2_kit.hatch(f"eng{j}", 0.9, 0.7, reveal=0.03, loc=(hx, cy1, zf + 1.6),
                                    facing="+Y"), "galv")
        if not FINE_DETAIL:        # fine-detail: the existing WallLightShortRange row moves onto it
            out.append((_box("lamp_eng", ex0 - 0.12, ex0 - 0.02, (cy0 + cy1) / 2.0 - 0.15,
                             (cy0 + cy1) / 2.0 + 0.15, zf + 2.18, zf + 2.28), "lamp"))
    # two electrical cabinets on the deck, operator side
    for k, x in enumerate((p.ped_r - 2.0, p.ped_r - 3.0)):
        out += _cabinet(f"cabinet_{k}", x - 0.45, x + 0.45, 4.65, 5.15, DECK_H, DECK_H + 1.4)
    return out


def _cabinet(name, x0, x1, y0, y1, z0, z1, wall=()):
    """A control cabinet (galv box). Fine-detail pass (FINE_DETAIL): no finish bevel strips (bevel_prefixes
    drops cabinet_; benchmark 2.2: vanilla boxy props carry none) - the box loses its hidden top and gets a
    lid lip <name>_lid, FD_LID_T thick, overhanging FD_LID_LIP on every side not in `wall` (the authoring
    faces "+X" "-X" "+Y" "-Y" that stand against a wall). Off: the pre-pass box (bevelled by the finish)."""
    if not FINE_DETAIL:
        return [(_box(name, x0, x1, y0, y1, z0, z1), "galv")]
    o = {f: (0.0 if f in wall else FD_LID_LIP) for f in ("+X", "-X", "+Y", "-Y")}
    return [(_box(name, x0, x1, y0, y1, z0, z1, drop=("+Z",)), "galv"),
            (_box(f"{name}_lid", x0 - o["-X"], x1 + o["+X"], y0 - o["-Y"], y1 + o["+Y"], z1, z1 + FD_LID_T),
             "galv")]


def bevel_prefixes():
    """The finish-bevel prefixes of this build: fine-detail drops cabinet_ (its edges read from the lid lip)."""
    if not FINE_DETAIL:
        return BEVEL_PREFIXES
    return tuple(x for x in BEVEL_PREFIXES if x != "cabinet_")


def build_soffit_lamps(p):
    if FINE_DETAIL:                # fine-detail: four RoofLightRandom01 rows on the same spots (fd_soffit_lights)
        return []
    out = []
    zs = p.z_soffit
    k = 0
    for dx in (1.5, 4.5):
        for s in (-1.0, 1.0):
            x = p.x_wheel - dx
            out.append((_box(f"lamp_s{k}", x - 0.20, x + 0.20, s * 3.6 - 0.10, s * 3.6 + 0.10,
                             zs - 0.10, zs, drop=("+Z",)), "lamp"))
            k += 1
    return out


def fd_soffit_lights(p):
    """Fine-detail (DQ): the vanilla rows that replace the modelled soffit lenses lamp_s0-3 - RoofLightRandom01
    on the lens centres (x_wheel - 1.5 / - 4.5, +-3.6) on the soffit, in lamp_s order (design A.2)."""
    out = []
    for dx in (1.5, 4.5):
        for s in (-1.0, 1.0):
            out.append(("RoofLightRandom01", (round(p.x_wheel - dx, 4), s * 3.6, p.z_soffit), None, "soffit"))
    return out


def comm_bracket_centre(p):
    """C13: (x_nose - 0.15, 0, band top / eave - 0.30)."""
    return (p.x_nose - 0.15, 0.0, p.z_top - 0.30)


def build_comm(p):
    """LV comm bracket on the nose band (Q11), 2 insulators, the cable (tube_along r 0.02,
    4 seg, bend_rings 2) down to a junction box (cabinet_comm) and 3 cable clamps."""
    bx, by, bz = comm_bracket_centre(p)
    out = [(_box("commbracket_nose", bx - 0.20, bx + 0.20, -0.15, 0.15, bz - 0.15, bz + 0.15),
            "galv")]
    for k, y in enumerate((-0.08, 0.08)):
        out.append((cs2_kit.add_cyl(f"insul_{k}", 0.04, 0.15, (bx, y, bz + 0.15 + 0.075),
                                    segments=6, caps="top"), "galv"))
    if not p.gondola:
        xp = p.ped_f
        zc = z_ped_top(p) - 0.08
        jb = (xp - 1.0, 0.0, PLINTH[2])
        out += _cabinet("cabinet_comm", jb[0] - 0.15, jb[0] + 0.15, -0.20, 0.20, jb[2], jb[2] + 0.40)
        # bracket +Y face -> along the band face beside the balcony (|y| 1.95) -> down to
        # under the crossheads -> along +X -> inward outside the crash pad -> junction box
        pts = [(bx, 0.15, bz - 0.05), (bx, 1.95, bz - 0.05), (bx, 1.95, zc),
               (jb[0], 1.95, zc), (jb[0], 0.0, zc), (jb[0], 0.0, jb[2] + 0.40)]
        clamp_at = [(1, 2, 0.5), (3, 4, 0.5), (4, 5, 0.5)]
    else:
        # gondola draft 1: the portal-face sign sits on the centreline below the bracket, so
        # the cable steps sideways to y GD_COMM_Y (between the sign and the +Y portal trim)
        # and drops to the junction box on the central pier, above the stone base
        yc = GD_COMM_Y
        jb = (p.x_nose - 0.15, yc, STONE_H + 0.10)
        out += _cabinet("cabinet_comm", p.x_nose - 0.30, p.x_nose, yc - 0.20, yc + 0.20, jb[2], jb[2] + 0.40,
                        wall=("+X",))
        pts = [(bx, 0.15, bz - 0.05), (bx, yc, bz - 0.05), (bx, yc, jb[2] + 0.40)]
        clamp_at = [(1, 2, 0.25), (1, 2, 0.5), (1, 2, 0.75)]
    out.append((cs2_kit.tube_along("commcable_nose", pts, 0.02, segments=4, caps="none",
                                   bend_rings=2), "galv"))
    for k, (i, j, f) in enumerate(clamp_at):
        a, b = Vector(pts[i]), Vector(pts[j])
        u = (b - a).normalized()
        c = a.lerp(b, f)
        clamp = cs2_kit.cable_clamp(f"comm{k}", 0.02, loc=tuple(c),
                                    rot=tuple(u.to_track_quat("X", "Z").to_euler()))
        keep = [(o, q) for o, q in clamp if o.name.startswith("clamp_")]
        for o, _q in clamp:          # the 2 studs of a 2 cm clamp are 1e-4 m2 faces: below a
            if o.name.startswith("bolt_"):   # texel even at 2048, and pack_unique rejects
                me = o.data                  # them as zero-area islands - drop them
                bpy.data.objects.remove(o, do_unlink=True)
                bpy.data.meshes.remove(me)
        out += _m(keep, "galv")
    return out


def build_wx(p):
    """Wind mast on the crown at x_back - 1.0: total 1.20 above the crown (claim 10)."""
    x, zc = p.x_back - 1.0, p.z_crown
    return [(cs2_kit.add_cyl("wxmast", 0.025, 1.06, (x, 0.0, zc + 0.53), segments=6, caps="none"),
             "galv"),
            (cs2_kit.add_cyl("wxarm", 0.018, 0.36, (x, 0.0, zc + 1.00),
                             rot=(math.pi / 2.0, 0.0, 0.0), segments=6), "galv"),
            (_box("wxbox", x - 0.045, x + 0.045, -0.045, 0.045, zc + 1.06, zc + 1.20), "galv")]


# --------------------------------------------------------------------------------------
# Detail pass 1 (DQ): dressing, labels, vanilla sub-objects (design sections 3 / 4, skeptic MC10)
# --------------------------------------------------------------------------------------

# label kinds per station: label part name -> cs2_liftdress.BOARDS kind (the painter)
D1_LABELS = {
    "base": {"label_lowerbar": "lowerbar", "label_tipsup": "tipsup", "label_waithere": "waithere",
             "label_seat1": "seat1", "label_seat2": "seat2", "label_seat3": "seat3",
             "label_seat4": "seat4", "label_ops": "ops", "label_slow": "slow"},
    # 2026-10-12: no label_stay (the DQ top's STAY SEATED board is gone, nineteenth look); its atlas rect goes with
    # it (label_sheet packs exactly these labels)
    "top": {"label_raisebar": "raisebar", "label_unload": "unload",
            "label_ops": "ops", "label_slow": "slow", "label_tension": "tension"},
}
D1_HANG = dict(y=1.65, z=2.575, w=0.70, h=0.55)      # the hang slot (design DB5; y sign = lane side)
# gondola revision 1: LIFT OPS on the operator room, WAIT HERE at an entry (both kinds; nitpick A3: the loading +Y
# entry, gd_wait_side - revision 1 put it at the -Y entry, which is the ARRIVING side on the stations' spin)
GD_LABELS = {"label_ops": "ops", "label_waitn": "waithere"}


def d1_labels(p):
    if p.gondola:
        return dict(GD_LABELS) if GD_REV1 else {}
    return {} if not DETAIL1 else dict(D1_LABELS[p.kind])


def label_sheet(p):
    """{label part name: (x0, y0, x1, y1) texels at ATLAS}: cs2_liftdress.pack_label_sheet of the
    station's labels at LABEL_PPM in LABEL_REGION_PX (the strip the shared sign rect frees)."""
    labs = d1_labels(p)
    if not labs:
        return {}
    LD = _LD()
    items = {nm: LD.board_px(kind, LABEL_PPM) for nm, kind in sorted(labs.items())}
    return LD.pack_label_sheet(items, LABEL_REGION_PX, gutter_px=LABEL_GUTTER_PX)


def label_rects(p):
    """Every reserved UV rect of the station: the sign band rect(s) (sign_rects) + the label sheet
    (detail pass 1, DQ). The bake pins the faces inside them; compose / weathering keep them 1:1."""
    out = {f"sign_{k}": v for k, v in sign_rects(p).items()}
    for nm, r in label_sheet(p).items():
        out[nm] = tuple(v / float(ATLAS) for v in r)
    return out


def _tag_label(parts, right, up, kind):
    """Record a label quad's reading frame (u = right, v = up) on its object for the seed."""
    for o, _m in parts:
        if o.name.startswith("label_"):
            o["d1_right"] = [float(v) for v in right]
            o["d1_up"] = [float(v) for v in up]
            o["d1_kind"] = kind
    return parts


def _close_open(obj):
    """Fill the open boundary loops of a part (a kit box built with a dropped face, now seen from
    behind): bmesh holes_fill, normals made consistent outward."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    edges = [e for e in bm.edges if e.is_boundary]
    if edges:
        bmesh.ops.holes_fill(bm, edges=edges, sides=0)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    return obj


def build_detail(p):
    """Detail pass 1 dressing (design 3.2 / 4.2 with the skeptic's MC10 moves; every slot and
    footprint is box-tested in assert_detail1): cs2_liftdress builders on the deck (z DECK_H), the
    soffit hang slot, the operator cabin / engine room walls, the flood masts at the corners, the
    nose band and the deck edges. Labels are ONE quad each (label_<kind>), seeded at their rect in
    the reserved strip (seed_parts) and painted in make_hook."""
    if p.gondola or not DETAIL1:
        return []
    LD = _LD()
    zd, zs, xn, xw = DECK_H, p.z_soffit, p.x_nose, p.x_wheel
    CL = cab_layout(p)                          # operator cabin (build_buildings; nitpick A: the top's on -Y)
    cx0, cx1 = CL["x0"], CL["x1"]
    cy0 = OPB_Y[0]                              # cabin / engine room -Y face (base)
    up = (0.0, 0.0, 1.0)
    out = []
    fine = FINE_DETAIL                          # fine-detail step 3b: kit geometry + kit rows (module doc)
    fkw = {"fine": True} if fine else {}

    def wall_right(f):
        n, r = LD._plan(f)
        return (r.x, r.y, 0.0)

    def board(nm, c, f, w, h, **kw):
        return _tag_label(LD.board(nm, c, f, w, h, **kw, **fkw), wall_right(f), up,
                          D1_LABELS[p.kind]["label_" + nm])

    def hang(nm, x, side, facing):
        H = D1_HANG
        return board(nm, (x, side * H["y"], H["z"]), facing, H["w"], H["h"], mount="hang", rods_to=zs)

    def post_board(nm, xy, facing, z0, w, h):
        """DQ follow-up: a free-standing board on TWO short galv posts (r 0.035, 0.15 in from each
        end, from the deck to the board top - 0.02, behind the closed backing: seen from behind too).
        The kit's one centre post (cs2_liftdress.board mount "post") is replaced by the pair."""
        n, r = LD._plan(facing)
        c = (xy[0], xy[1], z0 + h / 2.0)
        parts = []
        for o, m in board(nm, c, facing, w, h, mount="post", post_z0=zd):
            if o.name == f"board_{nm}_post":
                me_ = o.data
                bpy.data.objects.remove(o, do_unlink=True)
                bpy.data.meshes.remove(me_)
                continue
            parts.append((o, m))
        pr, ztop = 0.035, c[2] + h / 2.0 - 0.02
        for k, s in enumerate((-1.0, 1.0)):
            pc = Vector(c) - n * (LD.LABEL_PROUD + 0.02 + pr) + r * (s * (w / 2.0 - 0.15))
            # fine-detail: 12-seg capped posts (the kit board's fine post, design B.1)
            parts.append((LD._cyl(f"board_{nm}_post{k}", pr, (pc.x, pc.y, zd), (pc.x, pc.y, ztop),
                                  12 if fine else 6, "top"), "galv"))
        return parts

    def doorlite_and_sill(door_y, sx0, sx1):
        # nitpick A: the sill hangs under the window band of the cabin's LANE face (base -Y face at y 5.60, the
        # top's +Y face at y -5.10), both lifted with the floor (the top's cabin stands on the deck: + 0.15)
        dz, ly, s = CL["zf"] - 0.15, CL["lane_y"], CL["s"]
        lite = _quad("doorlite_cab", [(cx1 + 0.022, door_y - 0.20, 1.30 + dz), (cx1 + 0.022, door_y + 0.20, 1.30 + dz),
                                      (cx1 + 0.022, door_y + 0.20, 1.90 + dz), (cx1 + 0.022, door_y - 0.20, 1.90 + dz)],
                     (1.0, 0.0, 0.0))
        if s > 0:
            sill = _box("sill_cabw", sx0, sx1, ly - 0.12, ly, 1.26 + dz, 1.29 + dz, drop=("+Y",))
        else:
            sill = _box("sill_cabw", sx0, sx1, ly, ly + 0.12, 1.26 + dz, 1.29 + dz, drop=("-Y",))
        return [(lite, "glass"), (sill, "galv")]

    if p.kind == "base":
        # DB1 rope-and-flag maze (Q3): joins the galv corral (x 9.90 .. 13.0, y 4.30 / 5.40, no jog);
        # its -X end closed in front of the cabinets, the lane-side run opened by the load gate
        out += LD.maze("ld", [[(9.80, 5.40), (5.70, 5.40), (5.70, 4.30)], [(7.00, 4.30), (9.80, 4.30)]],
                       z0=zd, **fkw)
        # DB2 load gate on the lane side of the maze's last bay (opens -Y into the lane behind the
        # carpet: a gate across the lane would sit in the chair path, C8) + WAIT HERE on its panel
        out += LD.swing_gate("ld", hinge=(5.77, 4.30), latch=(6.93, 4.30), z0=zd, panel=True, **fkw)
        out += board("waithere", (6.35, 4.305 + 0.023, zd + 0.535), "+Y", 0.60, 0.35, mount="wall")
        # DB3 loading carpet (top 0.33; the bench's lowest point 0.40) + DB4 load line + seat marks
        out += LD.carpet("ld", 2.80, 5.60, 1.25, 3.95, z0=zd)
        out += LD.stripe("load", (2.85, 1.30), (2.85, 3.90), 0.10, z=zd + 0.03)
        for k, yc in enumerate((1.70, 2.30, 2.90, 3.50), start=1):
            out += _tag_label(LD.flat_label(f"seat{k}", 3.05, 3.30, yc - 0.125, yc + 0.125, zd + 0.03,
                                            read_up=(-1.0, 0.0)), (0.0, 1.0, 0.0), (-1.0, 0.0, 0.0),
                              f"seat{k}")
        # DB5 / DB6 hang slot boards facing the riders (+X), loaded lane +Y: LOWER BAR first (x 0.60),
        # then SKI TIPS UP at the design's DB5 slot (x -1.50); MC10: the design's x -6.50 crossed
        # commcable_nose (the cable runs x -8.15 .. -3.5 at y 1.95, z 2.67 through the slot).
        # D1_HANG_BOARDS (the follow-up's one table; D11 gates the order along the travel)
        for nm_, x_, s_, f_ in D1_HANG_BOARDS["base"]:
            out += hang(nm_, x_, s_, f_)
        # DB7 e-stop at the load line, in the corridor between the lanes (fine-detail: kit row, fd_kit_specs)
        if not fine:
            out += LD.estop_post("ld", (2.85, 0.75), z0=zd)
        # DB8 LIFT OPS over the cabin's -Y windows, DB9 door lite + window sill
        out += board("ops", (cx0 + 1.40, cy0 - 0.023, 2.525), "-Y", 1.20, 0.35, mount="wall")
        out += doorlite_and_sill(cy0 + 0.75, cx0 + 0.20, cx1 - 0.20)
        # DB10 extinguisher by the cabin door, DB11 hose reel beside the engine-room door (r 0.22:
        # the 0.62 m between its jamb and the corner), DB12 scraper + shovel on the engine room's
        # -Y face, standing on the deck (the cabin's +X face stands over open ground: no floor).
        # Fine-detail: all three are kit rows (fd_kit_specs; the reel moves to the engine room's -Y face)
        if not fine:
            out += LD.extinguisher("cab", (cx1, 7.30), "+X", z=1.20)
            out += LD.hose_reel("eng", (xw - 6.6, 7.49), "-X", z=1.25, r=0.22, w=0.16)
            out += LD.tools("eng", foot=(xw - 6.5, cy0 - 0.32), facing="-Y", z0=zd)
        # DB13 ski rack with skis on the -Y deck (a rack, not a railing: R1d untouched); fine-detail: the kit
        # wall rack row (its own frame + loadout; the host's one-sided frame and gear go)
        if not fine:
            out += LD.ski_rack("b", 10.2, 13.2, y=-6.60, z0=zd, pairs=7, boards=2, side=-1.0)
        # DB14 the two 8 m flood masts are gone (follow-up: vanilla LightpoleParkRandom01 rows,
        # D1_LAMPS / dq_subobjects); DB15 SLOW for arrivals from the ramp on two short posts of its own
        xy_, f_ = D1_SLOW["base"]
        out += post_board("slow", xy_, f_, zd + D1_SLOW_Z0, 1.20, 0.35)
        # nose panels broken round post_gangna_0 (y -1.25) and the 1c ground flight's corridor
        # (y 0.60 .. 1.21 against the band face, R2g)
        chev = ((-2.05, -1.32), (-1.18, 0.50), (1.25, 2.05), (-4.45, -3.15), (3.15, 4.45))
        gang = (-10.05, 0.44)
    else:
        # DT1 unload line across the -Y lane, DT2-DT4 hang slot boards facing the arrivals (-X).
        # Follow-up (Richard, fourteenth look: UNLOAD HERE was immediately followed by STAY SEATED):
        # STAY SEATED moved BEFORE the unload point (x -2.50; it was x 2.20, past the line). Nineteenth look
        # (2026-10-12): "We don't need the STAY SEATED at the top DQ station" - the board is gone; the rider
        # meets RAISE BAR -> UNLOAD at the line and nothing beyond it (D11, `forbid` bars any STAY SEATED)
        out += LD.stripe("unload", (D1_UNLOAD_X, -3.90), (D1_UNLOAD_X, -1.30), 0.10, z=zd)
        for nm_, x_, s_, f_ in D1_HANG_BOARDS["top"]:
            out += hang(nm_, x_, s_, f_)
        if not fine:                            # fine-detail: e-stop, extinguisher, reel, tools = kit rows
            out += LD.estop_post("ul", (0.0, -0.75), z0=zd)
        # nitpick A (2026-10-13): the cabin stands on the -Y apron beside the unload lane (cab_layout): LIFT OPS on
        # its LANE face (+Y, toward the arriving chairs), the door lite on its +X door, the sill under the lane
        # face's window band; the extinguisher / reel at its outer end (cab_out_y), all lifted with the deck floor
        dzc = CL["zf"] - 0.15
        if CL["s"] > 0:
            out += board("ops", (cx0 + 1.50, cy0 - 0.023, 2.525), "-Y", 1.20, 0.35, mount="wall")
        else:
            out += board("ops", (cx0 + 1.50, CL["lane_y"] + 0.023, 2.525 + dzc), "+Y", 1.20, 0.35, mount="wall")
        out += doorlite_and_sill(CL["door_y"], cx0 + 0.20, cx1 - 0.20)
        y_out = CL["out_y"] - CL["s"] * 0.50      # 7.30 on the +Y cabin, -6.80 on the -Y one
        if not fine:
            out += LD.extinguisher("cab", (cx1, y_out), "+X", z=1.20 + dzc)
            # DT9 the hose reel under the cabin's window band (glass z 1.30 .. 2.30 wraps the cabin)
            out += LD.hose_reel("cab", (cx0, y_out), "-X", z=0.92 + dzc)
        # DT10 scraper + shovel against cabinet_0's +X face (the cabin's +X face stands over open
        # ground), DT11 the rope-tension gauge on cabinet_1's -Y face (toward the deck)
        xc0 = p.ped_r - 2.0
        if not fine:
            out += LD.tools("cab", foot=(xc0 + 0.45 + 0.32, 4.70), facing="+X", z0=zd)
        out += _tag_label(LD.gauge("tension", (p.ped_r - 3.0, 4.65), "-Y", z=1.35),
                          wall_right("-Y"), up, "tension")
        # the flood masts are gone (vanilla lamp posts, D1_LAMPS); SLOW faces the unloaded skiers
        # heading for the back ramp, on two short posts of its own at the apron's back end
        xy_, f_ = D1_SLOW["top"]
        out += post_board("slow", xy_, f_, zd + D1_SLOW_Z0, 1.20, 0.35)
        chev = ((-1.68, 1.68), (-4.45, -3.15), (3.15, 4.45))
        gang = (-11.85, 0.78)
    # DB16 / DT13 nose chevrons on the band's flat face just above the fillet (z 3.70 .. 3.85),
    # broken round the landing / ladder rails that touch the face; DB17 edge stripes on the
    # gangplank's end and the deck's nose edge
    zc0 = zs + FILLET_R
    for k, (a0, a1) in enumerate(chev):
        out += LD.chevrons(f"nose{k}", "-X", c=xn, a_rng=(a0, a1), b_rng=(zc0, zc0 + 0.15))
    out += LD.stripe("gang", (gang[0], -gang[1]), (gang[0], gang[1]), 0.10, z=zs)
    out += LD.stripe("decknose", (xn + 0.05, -4.5), (xn + 0.05, 4.5), 0.10, z=zd)
    return out


def dq_subobjects(p):
    """Vanilla sub-objects of detail pass 1 (design 3.3 / 4.3; GUIDs = cs2_liftdress.SUBOBJ,
    verified against the dump). Rows (prefab, authoring position, facing in the authoring frame or
    None, mount): the surgery round adds them to the station's ObjectSubObjects (parent 0, game
    local = (-y, z, x), gx negated). MC10 moves: the ParkInfosign (variant-union bounds 2.42 x 2.79 x 1.70 m)
    off the bullwheel turn's chair envelope to the -Y back corner, the second bin out of its way. DQ
    follow-up: + the four LightpoleParkRandom01 lamp posts of D1_LAMPS on the deck top (the flood masts'
    replacement; appended, so the earlier rows keep their order)."""
    if p.gondola or not DETAIL1:
        return []
    zd, zs = DECK_H, p.z_soffit
    # T17-DQ 2026-10-16: lamp posts -> SkiPisteFloodlightLow (Richard): no vanilla lamp rows any more; the
    # floodlights are "mod" rows (d1_flood_rows, appended by subobject_rows), so no SUBOBJ box test lists them
    lamps = []
    # fine-detail step 3b (design A.2 / FS15): the cabin / engine-room WallLightShortRange rows MOVE onto the
    # modelled door lamps they replace (lamp_cab over the cabin door, y cy0 + 0.75; lamp_eng over the engine
    # door, y (cy0 + cy1) / 2; lens centre z zf + 2.23 = 2.38), and RoofLights take the soffit lenses
    # (appended, so every earlier row keeps its index)
    cy0, cy1 = 5.60, 7.80
    wl_cab = (6.35, 2.38) if FINE_DETAIL else (7.25, 2.40)
    wl_eng = ((cy0 + cy1) / 2.0, 2.38) if FINE_DETAIL else (6.00, 2.40)
    soffit = fd_soffit_lights(p) if FINE_DETAIL else []
    if p.kind == "base":
        return [("TrashbinRandom01", (13.75, 4.05, zd), None, "deck"),
                ("TrashbinRandom01", (10.60, -5.60, zd), None, "deck"),
                ("GardenBenchRandom01", (8.00, -6.60, zd), "+Y", "deck"),
                ("ParkInfosignRandom01", (12.50, -5.00, zd), "+X", "deck"),
                ("RoofLightRandom01", (0.00, 0.75, zs), None, "soffit"),
                ("RoofLightRandom01", (0.00, -0.75, zs), None, "soffit"),
                ("RoofLightRandom01", (2.80, 0.75, zs), None, "soffit"),
                ("RoofLightRandom01", (2.80, -0.75, zs), None, "soffit"),
                ("WallLightShortRangeRandom01", (p.x_wheel - 0.5 + 0.02, wl_cab[0], wl_cab[1]), "+X", "wall"),
                ("WallLightShortRangeRandom01", (p.x_wheel - 6.6 - 0.02, wl_eng[0], wl_eng[1]), "-X", "wall")] \
            + lamps + soffit
    # nitpick A (2026-10-13): the top's cabin stands on the -Y apron (cab_layout); its door lamp row moves with the
    # door (+X face, the door's centre y, the lens 2.23 over the cabin floor)
    CL = cab_layout(p)
    if CL["s"] > 0:
        wl_top = (p.x_wheel - 0.5 + 0.02, wl_cab[0], wl_cab[1])
    else:
        wl_top = (round(CL["x1"] + 0.02, 4), CL["door_y"], round(wl_cab[1] + CL["zf"] - 0.15, 4))
    return [("TrashbinRandom01", (-1.20, 4.90, zd), None, "deck"),
            ("GardenBenchRandom01", (-4.00, 4.90, zd), "-Y", "deck"),
            ("RoofLightRandom01", (-1.60, 0.75, zs), None, "soffit"),
            ("RoofLightRandom01", (-1.60, -0.75, zs), None, "soffit"),
            ("RoofLightRandom01", (1.60, 0.75, zs), None, "soffit"),
            ("RoofLightRandom01", (1.60, -0.75, zs), None, "soffit"),
            ("WallLightShortRangeRandom01", wl_top, "+X", "wall")] \
        + lamps + soffit


def _FD():
    import fine_detail
    return fine_detail


def fd_kit_specs(p):
    """Fine-detail step 3b: this station's dressing-kit rows in the AUTHORING frame, one per removed builder
    call (design A.2 / D, dress-kit.md 3 pivots): dict(prop (KIT object prefab), pos, facing (authoring),
    mount, removed (the old parts' name prefixes), was). mount "ground": pos = the foot centre on its floor
    (deck 0.30 / platform 0.30 / grade 0); "wall": pos = the old mount point ON the wall face at the prop's
    bottom (hose reel: the old centre height - 0.35, the plate bottom; extinguisher: the old bottom), facing =
    the wall's outward normal; "lean": pos = the old pair's centre ON the wall face at the floor z, facing = the
    wall's outward normal (kit_lean_row: the rest plane on the face, the pivot 0.3925 m out - the foot-line
    rule). [] with FINE_DETAIL off."""
    if not FINE_DETAIL:
        return []
    zd = DECK_H
    out = []
    if not p.gondola:
        if not DETAIL1:
            return []
        xw = p.x_wheel
        cx0, cx1 = xw - 3.5, xw - 0.5
        cy0 = OPB_Y[0]
        if p.kind == "base":
            out += [dict(prop=FD_WALL_RACK[("dq", "base")], pos=(11.70, -6.60, zd), facing="-Y", mount="ground",
                         removed=("skirack_b_", "ski_b_"),
                         was="ski_rack('b', x 10.2 .. 13.2, y -6.60, side -1): frame + 7 pairs + 2 boards"),
                    dict(prop="SkiDressEStop", pos=(2.85, 0.75, zd), facing="+Y", mount="ground",
                         removed=("estop_ld_",), was="estop_post('ld', (2.85, 0.75))"),
                    dict(prop="SkiDressExtinguisher", pos=(cx1, 7.30, 1.20), facing="+X", mount="wall",
                         removed=("ext_cab",), was="extinguisher('cab', (cx1, 7.30), +X, z 1.20)"),
                    dict(prop="SkiDressHoseReel", pos=(cx1, FD_DQ_REEL[0], round(FD_DQ_REEL[1] - 0.35, 4)),
                         facing="+X", mount="wall", removed=("hosereel_eng_",),
                         was="hose_reel('eng', (xw - 6.6, 7.49), -X, z 1.25, r 0.22): MOVED to the cabin's +X face "
                             "under the extinguisher (the kit's 0.70 m plate does not fit the 0.62 m beside the "
                             "engine-room door; the engine room's / cabin's -Y faces are fronted by the cabinets / "
                             "the maze)"),
                    dict(prop="SkiDressTools", pos=(round(xw - 6.5 + 0.175, 4), cy0, zd), facing="-Y", mount="lean",
                         removed=("tool_eng_",), was="tools('eng', foot (xw - 6.5, cy0 - 0.32), -Y, gap 0.35)")]
        else:
            xc0 = p.ped_r - 2.0
            # nitpick A (2026-10-13): the cabin moved onto the -Y apron beside the unload lane (cab_layout); its
            # extinguisher (+X face) and reel (-X face) stay at the cabin's OUTER end, lifted with the deck floor
            CL = cab_layout(p)
            tx0, tx1 = CL["x0"], CL["x1"]
            y_out = round(CL["out_y"] - CL["s"] * 0.50, 4)
            dzc = round(CL["zf"] - 0.15, 4)
            moved = "" if CL["s"] > 0 else "; nitpick A 2026-10-13: the cabin moved to the -Y apron (unload side)"
            out += [dict(prop="SkiDressEStop", pos=(0.0, -0.75, zd), facing="-Y", mount="ground",
                         removed=("estop_ul_",), was="estop_post('ul', (0.0, -0.75))"),
                    dict(prop="SkiDressExtinguisher", pos=(tx1, y_out, round(1.20 + dzc, 4)), facing="+X",
                         mount="wall", removed=("ext_cab",),
                         was=f"extinguisher('cab', (cx1, {y_out:.2f}), +X, z {1.20 + dzc:.2f}){moved}"),
                    dict(prop="SkiDressHoseReel", pos=(tx0, y_out, round(0.92 + dzc - 0.35, 4)), facing="-X",
                         mount="wall", removed=("hosereel_cab_",),
                         was=f"hose_reel('cab', (cx0, {y_out:.2f}), -X, z {0.92 + dzc:.2f}){moved}"),
                    dict(prop="SkiDressTools", pos=(round(xc0 + 0.45, 4), 4.70 + 0.175, zd), facing="+X",
                         mount="lean", removed=("tool_cab_",),
                         was="tools('cab', foot (cabinet_0 +X face + 0.32, 4.70), +X, gap 0.35)")]
        return out
    if not GD_REV1:
        return []
    L = gd_hall_layout(p)
    X, xd, W2, wi = p.x_wheel, L["xd"], p.w_half, L["wi"]
    for s, tag in ((-1.0, "n"), (1.0, "p")):
        f_in = "+Y" if s < 0 else "-Y"
        out += [dict(prop="SkiDressExtinguisher", pos=(round(X - 1.30, 4), s * wi, DECK_H + 0.15), facing=f_in,
                     mount="wall", removed=(f"ext_pl{tag}",), was=f"extinguisher('pl{tag}', (X - 1.30, {s:+.0f} wi))"),
                dict(prop="SkiDressEStop", pos=(round(X - 1.90, 4), s * 5.70, DECK_H), facing=f_in, mount="ground",
                     removed=(f"estop_pl{tag}_",), was=f"estop_post('pl{tag}', (X - 1.90, {s * 5.70:+.2f}))")]
    so = gd_opr_side(p)                 # nitpick A2: the reel stays with the operator room's side
    out += [dict(prop="SkiDressHoseReel", pos=(round(xd - 1.60, 4), so * wi, round(DECK_H + 0.45 - 0.35, 4)),
                 facing="-Y" if so > 0 else "+Y", mount="wall", removed=("hosereel_pl_",),
                 was="hose_reel('pl', (xd - 1.60, wi), -Y, z 0.75, r 0.22): same centre, the kit's 0.70 m plate"
                     + ("" if so > 0 else "; nitpick A2 2026-10-13: mirrored onto the -Y wall with the operator room")),
            dict(prop=FD_WALL_RACK[("gd", p.kind)], pos=(round(xd + 3.7, 4), -(W2 + 1.05), 0.0), facing="-Y",
                 mount="ground", removed=("skirack_gn_", "ski_gn_"),
                 was="ski_rack('gn', xd + 2.2 .. 5.2, y -(W2 + 1.05), z0 0.02, side -1): frame + 7 pairs + 2 boards"),
            # the plinth is no plain wall: the coping (z 1.20 .. 1.26) stands 0.05 proud of the stone face, the
            # glazing behind it - the tools lean on the coping's top outer EDGE (ledge): pos = the coping face
            dict(prop="SkiDressTools", pos=(round(xd + 5.75 + 0.175, 4), -(W2 + 0.10), 0.0), facing="-Y",
                 mount="lean", ledge=(STONE_H, GD_SILL), removed=("tool_gn_",),
                 was="tools('gn', foot (xd + 5.75, plinth - 0.32), -Y): leans on the coping's top edge")]
    return out


def kit_ledge_offset(mesh, h0, h1, step=0.001):
    """A leaning kit prop against a LEDGE (a face spanning heights h0 .. h1 above the floor, nothing at its
    plane above h1): the distance d >= 0 its rest plane (prefab z = rest_z, the kit's min z) must sit BEHIND
    the ledge face so the prop just touches the ledge's top edge - d = (the kit faces' min prefab z over
    h0 .. h1, sliced every `step`) - rest_z. Measured on the shipped kit FBX (load_kit_mesh)."""
    km = load_kit_mesh(mesh)
    key = (km["sha1"], round(float(h0), 4), round(float(h1), 4), step)
    if key in _KIT_LEDGE:
        return _KIT_LEDGE[key]
    V, T = km["V"], km["tris"]
    zmin = None
    for h in np.arange(h0, h1 + step / 2.0, step):
        for tr in T:
            P = V[list(tr)]
            for a, b in ((0, 1), (1, 2), (2, 0)):
                ya, yb = P[a, 1], P[b, 1]
                if ya != yb and (ya - h) * (yb - h) <= 0.0:
                    z = P[a, 2] + (h - ya) / (yb - ya) * (P[b, 2] - P[a, 2])
                    zmin = z if zmin is None else min(zmin, z)
    if zmin is None:
        raise ContractError(f"{mesh}: no kit face between heights {h0} and {h1}")
    _KIT_LEDGE[key] = round(max(0.0, float(zmin) - float(V[:, 2].min())), 4)
    return _KIT_LEDGE[key]


_KIT_LEDGE = {}


def fd_kit_colour(p, colour_set=None):
    """The station's kit colour set: `colour_set` if given, else FD_KIT_COLOUR[(family, kind)]."""
    return colour_set or FD_KIT_COLOUR[("gd" if p.gondola else "dq", p.kind)]


def fd_kit_rows(p, colour_set=None):
    """The kit rows (fine_detail.kit_row / kit_lean_row dicts, game local = blender_to_game) of fd_kit_specs,
    m_ParentMesh 0 like every row of these stations; yaw = facing_quat(facing, authoring) - the prop's +Z (its
    front / out of the wall) to the old part's facing. Coloured props (the wall racks) name the station's colour
    set (fd_kit_colour: FD_KIT_COLOUR or `colour_set`)."""
    FD, LD = _FD(), _LD()
    rows = []
    cset = fd_kit_colour(p, colour_set)
    for s in fd_kit_specs(p):
        note = f"replaces {s['was']}"
        cs = cset if FD.is_coloured(s["prop"]) else None
        if s["mount"] == "lean":
            n = LD._dir3(s["facing"])
            pos = s["pos"]
            if s.get("ledge"):
                # leaning on a ledge's top edge: the rest plane d behind the ledge face (kit_ledge_offset)
                d = kit_ledge_offset(FD.KIT[s["prop"]].mesh, *s["ledge"])
                pos = (pos[0] - n.x * d, pos[1] - n.y * d, pos[2])
                note += f"; leans on the ledge's top edge at z {s['ledge'][1]}: rest plane {d} m behind its face"
            wp = LD.blender_to_game(pos)
            ng = LD.blender_to_game((n.x, n.y, 0.0))
            rows.append(FD.kit_lean_row(s["prop"], wp, (ng[0], ng[2]), wp[1], parent=0, note=note, colour_set=cs))
        else:
            rows.append(FD.kit_row(s["prop"], LD.blender_to_game(s["pos"]),
                                   rot=LD.facing_quat(s["facing"], frame="blender"), parent=0, note=note,
                                   colour_set=cs))
    return rows


def subobject_rows(p, name):
    """The manifest / surgery block (cs2_liftdress.sub_object_rows, game local frame)."""
    LD = _LD()
    rows = []
    for pf, pos, facing, _mount in (gd_subobjects(p) if p.gondola else dq_subobjects(p)):
        rot = LD.facing_quat(facing, frame="blender") if facing else (0.0, 0.0, 0.0, 1.0)
        rows.append((pf, LD.blender_to_game(pos), rot, 0))
    target = name.replace("StationA", "StationBuilding")
    # T17-DQ 2026-10-16: lamp posts -> SkiPisteFloodlightLow (Richard): our own prop, a "mod" row (CID), so the
    # block is written fine=True; placed after the vanilla rows (where the lamp rows were) and before the kit
    flood = d1_flood_rows(p) if (p.gondola and GD_REV1) or (not p.gondola and DETAIL1) else []
    if FINE_DETAIL:
        # fine-detail step 3b: + the dressing kit's "mod" rows (cs2_liftdress.sub_object_rows fine=True: kinds,
        # x_convention = this generator's blender_to_game frame)
        kit = fd_kit_rows(p)
        return LD.sub_object_rows(target, rows + flood + kit, fine=True) if rows or flood or kit else None
    return LD.sub_object_rows(target, rows + flood, fine=bool(flood)) if rows or flood else None


# --------------------------------------------------------------------------------------
# Gondola hall (contract 13; draft 1 2026-10-11 - GD_* constants above)
# --------------------------------------------------------------------------------------

def _tess(poly):
    """tessellate_polygon on a 2D polygon, deterministic order, zero-area (collinear)
    triangles dropped (scanfill emits them where collinear vertices span a gap)."""
    from mathutils.geometry import tessellate_polygon
    out = []
    for tri in tessellate_polygon([[Vector((a, b, 0.0)) for a, b in poly]]):
        i, j, k = (int(v) for v in tri)
        (x0, y0), (x1, y1), (x2, y2) = poly[i], poly[j], poly[k]
        if abs((x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0)) > 1e-9:
            out.append((i, j, k))
    return sorted(out)


def _arc_pts(cx, cz, r, a0, a1, n):
    return [(cx + r * math.cos(math.radians(a0 + (a1 - a0) * i / n)),
             cz + r * math.sin(math.radians(a0 + (a1 - a0) * i / n))) for i in range(n + 1)]


def gd_portal_outline():
    """The portal opening (GD_PORTAL) in lane coordinates (u outboard of the lane, z up), from
    the inboard foot (u_lo, 0) up the inboard side, round the inboard shoulder, along the body
    top, through a concave fillet up the hanger slot, over the slot top, down the slot's
    outboard side onto the outboard shoulder and down to the outboard foot (u_hi, 0): the
    cabin's silhouette with GD_CLEAR + margin (G1). The opening reaches the ground (the cabin
    trough); explicit points at the stone-base top STONE_H (the frame trim starts there)."""
    P = GD_PORTAL
    pts = [(P["u_lo"], 0.0), (P["u_lo"], STONE_H)]
    pts += _arc_pts(P["u_lo"] + P["r_sh"], P["z_sh"], P["r_sh"], 180.0, 90.0, 5)
    pts += _arc_pts(P["s_lo"] - P["r_cc"], P["z_body"] + P["r_cc"], P["r_cc"], 270.0, 360.0, 3)
    pts += _arc_pts(P["s_lo"] + P["r_sl"], P["z_slot"] - P["r_sl"], P["r_sl"], 180.0, 90.0, 4)
    pts += _arc_pts(P["s_hi"] - P["r_sl"], P["z_slot"] - P["r_sl"], P["r_sl"], 90.0, 0.0, 4)
    pts += _arc_pts(P["s_hi"], P["z_sh"], P["u_hi"] - P["s_hi"], 90.0, 0.0, 5)
    pts += [(P["u_hi"], STONE_H), (P["u_hi"], 0.0)]
    out = []
    for u, z in pts:
        q = (round(u, 6), round(z, 6))
        if not out or abs(q[0] - out[-1][0]) + abs(q[1] - out[-1][1]) > 1e-6:
            out.append(q)
    return out


def gd_portal_yz(p, s):
    """The portal outline of the lane on side s (+1 = +Y) in (y, z), outline order."""
    return [(s * (p.lane_y + u), z) for u, z in gd_portal_outline()]


def build_gd_nose_wall(p):
    """wall_nose: the portal (line-side) wall between the side walls, |y| <= w_half - WALL_T,
    z 0 .. eave: the outer and inner faces (one polygon with the two portal notches,
    tessellated) and the reveals round each opening through the wall. Open bottom: its z 0
    edges (outer, inner and the reveal feet) are engine-base edges."""
    from mathutils.geometry import tessellate_polygon
    xn, wt = p.x_nose, WALL_T
    wi = p.w_half - wt
    poly = [(-wi, 0.0)]
    for s in (-1.0, 1.0):
        seq = gd_portal_yz(p, s)
        poly += list(reversed(seq)) if s < 0 else seq
    poly += [(wi, 0.0), (wi, p.z_top), (-wi, p.z_top)]
    tris = _tess(poly)
    mb = _MB()
    for tri in tris:
        a, b, c = (poly[i] for i in tri)
        mb.f([(xn, a[0], a[1]), (xn, b[0], b[1]), (xn, c[0], c[1])], (-1.0, 0.0, 0.0))
        mb.f([(xn + wt, a[0], a[1]), (xn + wt, b[0], b[1]), (xn + wt, c[0], c[1])],
             (1.0, 0.0, 0.0))
    for s in (-1.0, 1.0):
        seq = gd_portal_yz(p, s)
        for (y0, z0), (y1, z1) in zip(seq, seq[1:]):
            mb.f([(xn, y0, z0), (xn + wt, y0, z0), (xn + wt, y1, z1), (xn, y1, z1)],
                 (0.0, s * (z1 - z0), -s * (y1 - y0)))     # into the opening
    return mb.obj("wall_nose")


def build_gd_portal_trims(p):
    """portalframe_<side>: a GD_TRIM_W wide frame GD_TRIM_T proud of the portal face round
    each opening, from the stone-base top up and over (front face + both edge faces)."""
    xn = p.x_nose
    xf = xn - GD_TRIM_T
    out = []
    for s, tag in ((-1.0, "n"), (1.0, "p")):
        seq = [q for q in gd_portal_yz(p, s) if q[1] >= STONE_H - 1e-6]
        segn = []
        for (y0, z0), (y1, z1) in zip(seq, seq[1:]):
            ny, nz = s * (z1 - z0), -s * (y1 - y0)
            L = math.hypot(ny, nz)
            segn.append((ny / L, nz / L))
        vn = []
        for i in range(len(seq)):
            a = segn[max(i - 1, 0)]
            b = segn[min(i, len(segn) - 1)]
            my, mz = a[0] + b[0], a[1] + b[1]
            L = math.hypot(my, mz)
            vn.append((my / L, mz / L))
        outer = [(y - GD_TRIM_W * ny, z - GD_TRIM_W * nz) for (y, z), (ny, nz) in zip(seq, vn)]
        mb = _MB()
        for i in range(len(seq) - 1):
            (y0, z0), (y1, z1) = seq[i], seq[i + 1]
            (Y0, Z0), (Y1, Z1) = outer[i], outer[i + 1]
            n = segn[i]
            mb.f([(xf, y0, z0), (xf, y1, z1), (xf, Y1, Z1), (xf, Y0, Z0)], (-1.0, 0.0, 0.0))
            mb.f([(xf, y0, z0), (xn, y0, z0), (xn, y1, z1), (xf, y1, z1)], (0.0, n[0], n[1]))
            mb.f([(xf, Y0, Z0), (xn, Y0, Z0), (xn, Y1, Z1), (xf, Y1, Z1)], (0.0, -n[0], -n[1]))
        out.append((mb.obj(f"portalframe_{tag}"), "paint1"))
    return out


def _ring_quads(name, inner, outer, y_face, s):
    """A flat ring (rounded-rect gasket) between two equal-length (x, z) outlines at y_face."""
    mb = _MB()
    m = len(inner)
    for i in range(m):
        j = (i + 1) % m
        mb.f([(inner[i][0], y_face, inner[i][1]), (outer[i][0], y_face, outer[i][1]),
              (outer[j][0], y_face, outer[j][1]), (inner[j][0], y_face, inner[j][1])],
             (0.0, s, 0.0))
    return mb.obj(name)


def _prism(name, poly, z0, z1, skip=()):
    """Vertical prism over the (x, y) polygon `poly`: top face (tessellated) at z1 and one side
    quad per edge i (poly[i] -> poly[i + 1]) unless i is in `skip` (buried faces); no bottom."""
    from mathutils.geometry import tessellate_polygon
    area = sum(x0 * y1 - x1 * y0 for (x0, y0), (x1, y1) in zip(poly, poly[1:] + poly[:1]))
    o = 1.0 if area > 0 else -1.0             # CCW: outward normal (dy, -dx)
    mb = _MB()
    tris = _tess(poly)
    for tri in tris:
        mb.f([(poly[i][0], poly[i][1], z1) for i in tri], (0.0, 0.0, 1.0))
    n = len(poly)
    for i in range(n):
        if i in skip:
            continue
        (xa, ya), (xb, yb) = poly[i], poly[(i + 1) % n]
        mb.f([(xa, ya, z0), (xb, yb, z0), (xb, yb, z1), (xa, ya, z1)],
             (o * (yb - ya), -o * (xb - xa), 0.0))
    return mb.obj(name)


def gd_platform_layout(p):
    """Boarding platforms (top DECK_H = the cabin floor GONDOLA_STEP_IN under the cabin's
    underside): per side the boarding strip from the portal wall to x_wheel + GD_PLAT_END at
    |y| >= GD_PLAT_IN (edge stripe GD_STRIPE_W) and a walkway at |y| >= GD_WALK_IN on to the
    stair foot / the operator room door (nitpick A2: the room on gd_opr_side - the base +Y, the top -Y)."""
    X, wi = p.x_wheel, p.w_half - WALL_T
    x0 = p.x_nose + WALL_T
    xm1 = X + GD_PLAT_END
    so = gd_opr_side(p)
    out = {}
    for s, tag in ((-1.0, "n"), (1.0, "p")):
        xw1 = X + (GD_OPR_X0 if s == so else GD_STAIR_X[0])
        out[tag] = {"s": s, "x0": x0, "x_board_end": xm1, "x_walk_end": xw1,
                    "edge_y": GD_PLAT_IN, "stripe_y": GD_PLAT_IN + GD_STRIPE_W,
                    "walk_y": GD_WALK_IN, "wall_y": wi, "top": DECK_H}
    return out


def build_gd_platforms(p):
    out = []
    for tag, L in sorted(gd_platform_layout(p).items()):
        s = L["s"]
        x0, xm1, xw1 = L["x0"], L["x_board_end"], L["x_walk_end"]
        ys, ye, yw, wi = L["stripe_y"], L["edge_y"], L["walk_y"], L["wall_y"]
        poly = [(x0, ys), (xm1, ys), (xm1, yw), (xw1, yw), (xw1, wi), (x0, wi)]
        # buried: 0 against the stripe, 4 against the side wall; 3 against the operator
        # room floor (the room's side) - on the stair's side it is the stair foot's riser
        skip = (0, 4) + ((3,) if s == gd_opr_side(p) else ())
        out.append((_prism(f"platf_{tag}", [(x, s * y) for x, y in poly], GD_FLOOR_Z, DECK_H,
                           skip), "concrete"))
        stripe = [(x0, ye), (xm1, ye), (xm1, ys), (x0, ys)]
        out.append((_prism(f"pstripe_{tag}", [(x, s * y) for x, y in stripe], GD_FLOOR_Z, DECK_H,
                           (2,)), "stripe"))
    return out


def build_gd_oproom(p):
    """The operator room in the +Y back corner at platform level: floor slab, two inner walls
    (cedar) with a door from the +Y walkway and an inner glazing band toward the turn, a roof
    slab; its outer panes on the hall's +Y wall are ctrlwin (`_Win` cell 7)."""
    X, xb, wi = p.x_wheel, p.x_back, p.w_half - WALL_T
    ox0, oy0, ox1 = X + GD_OPR_X0, GD_OPR_Y0, xb - WALL_T
    zf, zr = DECK_H, DECK_H + GD_OPR_H
    yd = (GD_WALK_IN + wi) / 2.0
    if GD_DOOR_OPENINGS:
        # checker F1: the operator door is a REAL opening in the room's -X wall (two wall pieces
        # either side, a lintel with its head reveal; the pieces' end faces are the jambs)
        d0, d1, zh = yd - GD_DOOR_W / 2.0, yd + GD_DOOR_W / 2.0, zf + GD_DOOR_H
        walls = _multi_box("opr_walls", [(ox0, ox1, oy0, oy0 + 0.10, zf, zr),
                                         (ox0, ox0 + 0.10, oy0 + 0.10, d0, zf, zr),
                                         (ox0, ox0 + 0.10, d1, wi, zf, zr),
                                         (ox0, ox0 + 0.10, d0, d1, zh, zr, "keep_bottom")])
    else:
        walls = _multi_box("opr_walls", [(ox0, ox1, oy0, oy0 + 0.10, zf, zr),
                                         (ox0, ox0 + 0.10, oy0 + 0.10, wi, zf, zr)])
    out = [(_box("opr_floor", ox0, ox1, oy0, wi, GD_FLOOR_Z, zf, drop=("-Z", "+X", "+Y")),
            "concrete"), (walls, "cedar")]
    dp = cs2_kit.door("opr", w=GD_DOOR_W, h=GD_DOOR_H, reveal=0.04, loc=(ox0, yd, zf), facing="-X")
    if GD_DOOR_OPENINGS:
        _close_door_leaf(dp, "door_opr_leaf")
    out += _m(dp, "galv", door_opr_leaf="paint1")
    y = oy0 - 0.012
    out.append((_quad("oprglass", [(ox0 + 0.35, y, zf + 1.0), (ox1 - 0.35, y, zf + 1.0),
                                   (ox1 - 0.35, y, zf + 2.0), (ox0 + 0.35, y, zf + 2.0)],
                      (0.0, -1.0, 0.0)), "glass"))
    out.append((_box("opr_roof", ox0 - 0.10, ox1, oy0 - 0.10, wi, zr, zr + 0.12,
                     drop=("+X", "+Y")), "paint1"))
    return out


def gd_stair_layout(p):
    """The internal stair from the -Y walkway (DECK_H) to the machinery deck (GD_DECK_Z):
    flight 1 along +X against the -Y wall to a corner landing, flight 2 along +Y against the
    back wall up to the deck edge y GD_STAIR_Y2; GD_RISERS risers / GD_RISERS - 1 treads per
    flight. Every walkable height of the route is in here (G3)."""
    X, wi, xbw = p.x_wheel, p.w_half - WALL_T, p.x_back - WALL_T
    x0, x1 = X + GD_STAIR_X[0], X + GD_STAIR_X[1]
    ya, yb = -wi + GD_STAIR_W, GD_STAIR_Y2
    z0, zt = DECK_H, GD_DECK_Z
    zl = round((z0 + zt) / 2.0, 4)
    n = GD_RISERS
    return {"x0": x0, "x1": x1, "xbw": xbw, "y_wall": -wi, "ya": ya, "yb": yb, "z0": z0,
            "zl": zl, "zt": zt, "n": n,
            "rise1": (zl - z0) / n, "going1": (x1 - x0) / (n - 1),
            "rise2": (zt - zl) / n, "going2": (yb - ya) / (n - 1)}


def build_gd_stair(p):
    S = gd_stair_layout(p)
    x0, x1, xbw, yw, ya, yb = S["x0"], S["x1"], S["xbw"], S["y_wall"], S["ya"], S["yb"]
    z0, zl, zt, n = S["z0"], S["zl"], S["zt"], S["n"]
    r1, g1, r2, g2 = S["rise1"], S["going1"], S["rise2"], S["going2"]
    out = []
    nos, t = 0.02, 0.04
    for k in range(1, n):
        z = z0 + k * r1
        out.append((_box(f"stread_1_{k:02d}", x0 + (k - 1) * g1 - nos, x0 + k * g1, yw + 0.05,
                         ya - 0.05, z - t, z), "galv"))
        z = zl + k * r2
        out.append((_box(f"stread_2_{k:02d}", x1 + 0.05, xbw - 0.05, ya + (k - 1) * g2 - nos,
                         ya + k * g2, z - t, z), "galv"))
    # stringers: slanted plates under both edges of each flight, 0.10 below the nosing line
    a1 = math.atan2(zl - z0, x1 - x0)
    L1 = math.hypot(x1 - x0, zl - z0) + 0.10
    for j, y in enumerate((yw + 0.025, ya - 0.025)):
        out.append((cs2_kit.add_box(f"stringer_1{j}", (L1, 0.05, 0.25),
                                    ((x0 + x1) / 2.0, y, (z0 + zl) / 2.0 + r1 / 2.0 - 0.10),
                                    rot=(0.0, -a1, 0.0)), "galv"))
    a2 = math.atan2(zt - zl, yb - ya)
    L2 = math.hypot(yb - ya, zt - zl) + 0.10
    for j, x in enumerate((x1 + 0.025, xbw - 0.025)):
        out.append((cs2_kit.add_box(f"stringer_2{j}", (0.05, L2, 0.25),
                                    (x, (ya + yb) / 2.0, (zl + zt) / 2.0 + r2 / 2.0 - 0.10),
                                    rot=(a2, 0.0, 0.0)), "galv"))
    out += _m(cs2_kit.grating("stl", xbw - x1, ya - yw, t=0.06,
                              loc=((x1 + xbw) / 2.0, (yw + ya) / 2.0, zl - 0.03)), "galv")
    out.append((cs2_kit.add_cyl("stpost", 0.05, zl - 0.06 - GD_FLOOR_Z,
                                (x1 + 0.06, ya - 0.06, (zl - 0.06 + GD_FLOOR_Z) / 2.0),
                                segments=6, caps="top"), "galv"))
    # handrail on the open side: flight 1 (y = ya), round the landing corner, flight 2 (x = x1)
    path = [(x0 + 0.15, ya - 0.03, z0 + r1 + 0.15 * r1 / g1), (x1 + 0.03, ya - 0.03, zl),
            (x1 + 0.03, yb - 0.15, zl + r2 + (yb - 0.15 - ya) * r2 / g2)]
    rail = cs2_kit.handrail("st", path, h=1.0, knee=0.5, kick=0.10, post_every=1.2, segs=6,
                            radius=0.028)
    keep = [(o, q) for o, q in rail if not o.name.startswith("kick_")]
    for o, _q in rail:               # no kick plates on a stair (they would hang off the pitch)
        if o.name.startswith("kick_"):
            me = o.data
            bpy.data.objects.remove(o, do_unlink=True)
            bpy.data.meshes.remove(me)
    out += _m(keep, "galv")
    return out


def build_gd_deck(p):
    """The machinery deck over the bullwheel at GD_DECK_Z, wall to wall from x_wheel +
    GD_DECK_X0 to the back wall less the stairwell; beams 5.30 .. 5.54 (above the detach rail,
    0.37 above the rope-pin grip top); guard rails on its open front edge and along the
    stairwell. The drive (base) / tension carriage (top) stand on it (build_machine)."""
    X, xb, wi = p.x_wheel, p.x_back, p.w_half - WALL_T
    dx0, dx1, xbw = X + GD_DECK_X0, X + GD_STAIR_X[1], xb - WALL_T
    out = []
    out += _m(cs2_kit.grating("dka", dx1 - dx0, 2.0 * wi, t=0.06,
                              loc=((dx0 + dx1) / 2.0, 0.0, GD_DECK_Z - 0.03)), "galv")
    g = cs2_kit.grating("dkb", xbw - dx1, wi - GD_STAIR_Y2, t=0.06,
                        loc=((dx1 + xbw) / 2.0, (wi + GD_STAIR_Y2) / 2.0, GD_DECK_Z - 0.03))
    for o, _q in g:
        if o.name.endswith("_lip"):
            _drop_dir(o, "-X")           # against deck a
    out += _m(g, "galv")
    for k, bx in enumerate(GD_BEAMS):
        out.append((_box(f"dkbeam_{k}", X + bx - 0.10, X + bx + 0.10, -wi, wi, GD_BEAM_Z0,
                         GD_DECK_Z - 0.06, drop=("+Y", "-Y")), "galv"))
    xbb = (dx1 + xbw) / 2.0
    out.append((_box(f"dkbeam_{len(GD_BEAMS)}", xbb - 0.10, xbb + 0.10, GD_STAIR_Y2, wi,
                     GD_BEAM_Z0, GD_DECK_Z - 0.06, drop=("+Y",)), "galv"))
    out += _m(cs2_kit.handrail("dkf", [(dx0 + 0.05, -wi + 0.05, GD_DECK_Z),
                                       (dx0 + 0.05, wi - 0.05, GD_DECK_Z)],
                               h=1.10, knee=0.55, kick=0.10, post_every=1.5, segs=6,
                               radius=0.028), "galv")
    out += _m(cs2_kit.handrail("dks", [(dx1 - 0.05, -wi + 0.05, GD_DECK_Z),
                                       (dx1 - 0.05, GD_STAIR_Y2 + 0.05, GD_DECK_Z)],
                               h=1.10, knee=0.55, kick=0.10, post_every=1.5, segs=6,
                               radius=0.028), "galv")
    return out


def gd_opr_side(p):
    """Nitpick A2: the operator room's authoring y sign (GD_OPR_SIDE[kind]; +1 under GD_OPR_LEGACY and on the DQ)."""
    if not p.gondola or GD_OPR_LEGACY:
        return 1.0
    return float(GD_OPR_SIDE[p.kind])


def _mirror_part_y(obj):
    """Mirror one part across y = 0 in place (world' = D @ world, D = diag(1, -1, 1)): a proper placement D M D and
    the mesh mirrored locally with its winding reversed (normals outward, triangles unchanged). The station assembly
    strips every UV and projects world UVs afterwards, so nothing mirrored survives in the texture."""
    from mathutils import Matrix
    D = Matrix.Diagonal((1.0, -1.0, 1.0, 1.0))
    obj.matrix_basis = D @ obj.matrix_basis @ D
    obj.data.transform(D)
    obj.data.flip_normals()
    obj.data.update()


def build_gd_interior(p):
    out = []
    out += build_gd_platforms(p)
    room = build_gd_oproom(p)
    deck = build_gd_deck(p)
    stair = build_gd_stair(p)
    if gd_opr_side(p) < 0.0:
        # nitpick A2: the room onto the -Y back corner, the stair + the deck's stairwell onto +Y (exact mirrors)
        for o, _m in room + deck + stair:
            if o.name.startswith(GD_SIDE_MIRROR):
                _mirror_part_y(o)
    out += room
    out += deck
    out += stair
    return out


def gd_ladder_layout(p):
    """The roof route (DQ Revision 2 / 1c rules): the nose ladder on the -Y outer pier of the
    portal face from a concrete footing at z 0 (rails from the footing top, rungs on the 0.30
    grid from 0.35 to the roof rim, rails GF_EXT above the rim as grab rails) and a roof ladder
    over the curve to the crown (rails GD_RL_LIFT above the roof, a rung every 0.30 m of arc)."""
    lx, ly = p.x_nose - GD_NL_OFF, GD_NL_Y
    _pts, R, zc = arch(p)
    z_rim = zc + math.sqrt(R * R - ly * ly)
    k_hi = int(math.floor((z_rim + 0.05 - 0.35) / NZ_PITCH + 1e-9))
    rungs = [round(0.35 + NZ_PITCH * k, 4) for k in range(0, k_hi + 1)]
    foot = (lx + NZ_FOOT_X[0], lx + NZ_FOOT_X[1], ly - NZ_FOOT_HALF_Y, ly + NZ_FOOT_HALF_Y,
            0.0, NZ_FOOT_H)
    a0, a1 = math.asin(ly / R), math.asin(GD_RL_TOP_Y / R)
    Rl = R + GD_RL_LIFT

    def at(a, rad):
        return (rad * math.sin(a), zc + rad * math.cos(a))
    rail = [at(a0 + (a1 - a0) * i / 10.0, Rl) for i in range(11)]
    n_r = int(math.floor((a1 - a0) * Rl / NZ_PITCH))
    rr = [at(a0 + (a1 - a0) * (i + 0.5) / n_r, Rl) for i in range(n_r)]
    roof_rungs = [(round(y, 5), round(z, 5)) for y, z in rr]
    stands = [(at(a, Rl), at(a, R)) for a in (a0 + (a1 - a0) * f for f in (0.05, 0.35, 0.65, 0.95))]
    return {"x": lx, "y": ly, "z_foot": NZ_FOOT_H, "z_rim": z_rim, "z_top": z_rim + GF_EXT,
            "rungs": rungs, "foot": foot, "roof_x": tuple(p.x_nose + d for d in GD_RL_X),
            "roof_rail": rail, "roof_rungs": roof_rungs, "roof_stands": stands,
            "crown": p.z_crown}


def build_gd_ladders(p):
    L = gd_ladder_layout(p)
    lx, ly = L["x"], L["y"]
    out = _ladder("nz", lx, ly, L["z_foot"], L["z_top"], rungs=L["rungs"])
    out.append((_box("lfoot_nz", *L["foot"], open_bottom=True), "concrete"))
    k = 0
    # revision 1: the top standoff clamps to the nose ring beam (its face 0.10 proud of the portal
    # wall); below it the portal wall carries the rails (the glazing band between is not a mount)
    zs4 = p.z_top - GD_NOSE_HEAD_DROP / 2.0 if GD_REV1 else p.z_top - 0.5
    for z in (1.5, 3.5, 5.5, zs4):
        if GD_REV1:
            xe = p.x_nose - (0.10 if z > GD_LINTEL_Z else 0.0)
        else:
            xe = p.x_nose - (0.04 if z > p.z_top - 1.0 else 0.0)      # the nose band is 0.04 proud
        for dy in (-0.28, 0.28):
            out.append((_box(f"lstand_nz{k}", lx + 0.02, xe, ly + dy - 0.02, ly + dy + 0.02,
                             z - 0.02, z + 0.02, drop=("+X",)), "galv"))
            k += 1
    xr0, xr1 = L["roof_x"]
    for j, x in enumerate((xr0, xr1)):
        # straight 6-gon pieces along the arc (a tube_along over 4.5 deg bends left sliver
        # fillet quads of ~4e-8 m2 that pack to zero UV area); 1 cm overlaps hide the joints
        pts = L["roof_rail"]
        for i, ((ya, za), (yb, zb)) in enumerate(zip(pts, pts[1:])):
            d = Vector((0.0, yb - ya, zb - za)).normalized() * 0.01
            a = Vector((x, ya, za)) - (d if i else Vector())
            b = Vector((x, yb, zb)) + (d if i < len(pts) - 2 else Vector())
            out.append((_cyl_pts(f"lrail_rf{j}_{i:02d}", tuple(a), tuple(b), 0.03, 6,
                                 caps="none" if 0 < i < len(pts) - 2 else
                                 ("bottom" if i == 0 else "top")), "galv"))
        for i, ((ya, za), (yb, zb)) in enumerate(L["roof_stands"]):
            out.append((_cyl_pts(f"lstand_rf{j}{i}", (x, ya, za - 0.03), (x, yb, zb), 0.018, 4),
                        "galv"))
    for i, (y, z) in enumerate(L["roof_rungs"], start=1):
        out.append((cs2_kit.add_cyl(f"lrung_rf{i:02d}", 0.015, (xr1 - xr0) - 0.02,
                                    ((xr0 + xr1) / 2.0, y, z), rot=(0.0, math.pi / 2.0, 0.0),
                                    segments=4, caps="none"), "galv"))
    return out


def _multi_box(name, boxes, drop=()):
    """Several open-bottom axis-aligned boxes as ONE part (bounds tuples x0,x1,y0,y1,z0,z1; a 7th
    element "keep_bottom" keeps that box's -Z face: a lintel's head reveal over an opening)."""
    mb = _MB()
    for b in boxes:
        x0, x1, y0, y1, z0, z1 = b[:6]
        dropset = set(drop) | ({"-Z"} if not (len(b) > 6 and b[6] == "keep_bottom") else set())
        faces = {"+X": ([(x1, y0, z0), (x1, y1, z0), (x1, y1, z1), (x1, y0, z1)], (1, 0, 0)),
                 "-X": ([(x0, y0, z0), (x0, y1, z0), (x0, y1, z1), (x0, y0, z1)], (-1, 0, 0)),
                 "+Y": ([(x0, y1, z0), (x1, y1, z0), (x1, y1, z1), (x0, y1, z1)], (0, 1, 0)),
                 "-Y": ([(x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1)], (0, -1, 0)),
                 "+Z": ([(x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)], (0, 0, 1)),
                 "-Z": ([(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0)], (0, 0, -1))}
        for key in ("+X", "-X", "+Y", "-Y", "+Z", "-Z"):
            if key not in dropset:
                mb.f(*faces[key])
    return mb.obj(name)


# -- gondola: real door openings (checker F1 of draft1-check-gondola-stations; GD_DOOR_*) ------
GD_DOOR_OPENINGS = True        # False = draft 1's leaves on solid walls (the G3 opening negative)
GD_DOOR_W, GD_DOOR_H = 0.85, 1.95          # = cs2_kit.door's opening (the leaf covers it)


def _close_door_leaf(door_parts, leaf_name):
    """Gondola real door openings: the kit leaf's back face (dropped against a solid wall) is
    filled, so the leaf reads as a door from inside the hall too."""
    for o, _q in door_parts:
        if o.name == leaf_name:
            _close_open(o)
            return o
    raise ContractError(f"{leaf_name}: no door leaf to close")


# --------------------------------------------------------------------------------------
# Gondola revision 1: the long glazed hall (GD_REV1; build report build/gondola-stations-rev1.md)
# --------------------------------------------------------------------------------------

def gd_roof_z(p, y, inner=False):
    """The shallow roof's outer surface height at y (the arch circle; exactly z_top at the eave
    edges |y| = chord_half), or its underside (inner: ROOF_T lower)."""
    _pts, R, zc = arch(p)
    if abs(abs(y) - p.chord_half) < 1e-9:
        z = p.z_top
    else:
        z = zc + math.sqrt(max(R * R - y * y, 0.0))
    return z - (ROOF_T if inner else 0.0)


def gd_hall_layout(p):
    """Every number of the revision-1 hall (authoring frame). Side bays [(x0, x1, kind)] between the
    corner columns: the front run to the door bay, the door bay (GD_DOOR_BAY, centred on the side door
    at x_wheel - 3.0), the run to the operator room's -X wall and the operator bay (its first tier is
    the room's window on +Y). Tiers sill -> transoms -> ring beam. The `_Win` panes [(part, room,
    plane, rect)]: one room per bay (all its tiers), the operator room's own room "opr" (side + back
    first tier), the nose bays above the portal lintel (the centre bay is the sign spandrel), the back
    bays. Panes run edge to edge between mullion centre lines (the mullions stand proud in front)."""
    W2, wt = p.w_half, WALL_T
    wi = W2 - wt
    xn, xb, X = p.x_nose, p.x_back, p.x_wheel
    xd = X - 3.0
    hb = GD_DOOR_BAY / 2.0
    ox0 = X + GD_OPR_X0

    def split(a, b):
        n = max(1, int(round((b - a) / GD_BAY)))
        return [round(a + (b - a) * i / n, 6) for i in range(n + 1)]
    front = split(xn + wt, xd - hb)
    mid = split(xd + hb, ox0)
    bays = [(a, b, "hall") for a, b in zip(front, front[1:])]
    bays.append((round(xd - hb, 6), round(xd + hb, 6), "door"))
    bays += [(a, b, "hall") for a, b in zip(mid, mid[1:])]
    bays.append((round(ox0, 6), round(xb - wt, 6), "opr"))
    z_head = round(p.z_top - GD_HEAD_DROP, 6)
    z_nose_head = round(p.z_top - GD_NOSE_HEAD_DROP, 6)
    tiers = [(GD_SILL, GD_TRANSOMS[0]), (GD_TRANSOMS[0], GD_TRANSOMS[1]), (GD_TRANSOMS[1], z_head)]
    # the side signs: the front-run bay nearest the hall's middle, never the one beside the door
    # bay (the canopy's tie rods rise on that mullion)
    xm = (xn + xb) / 2.0
    sign_bay = min((abs((a + b) / 2.0 - xm), k) for k, (a, b, _kd) in enumerate(bays[:len(front) - 2]))[1]
    so = gd_opr_side(p)                       # nitpick A2: the operator room's side
    panes = []
    for s, tag in ((-1.0, "n"), (1.0, "p")):
        y = round(s * (W2 - GD_GLASS_IN), 6)
        for k, (x0, x1, kind) in enumerate(bays):
            for t, (z0, z1) in enumerate(tiers):
                if kind == "door" and t == 0:
                    continue                  # the double door
                room = "opr" if (kind == "opr" and s == so and t == 0) else f"{tag}{k:02d}"
                panes.append((f"win_{room}_{tag}{k:02d}t{t}", room, ("y", y, s), (x0, x1, z0, z1)))
    xg = round(xn + 0.10, 6)
    for k, (y0, y1) in enumerate(zip(GD_NOSE_BAYS, GD_NOSE_BAYS[1:])):
        if abs(y0 + y1) < 1e-9:
            continue                          # the centre bay: the sign spandrel
        panes.append((f"win_f{k}_f{k}", f"f{k}", ("x", xg, -1.0), (y0, y1, GD_LINTEL_Z, z_nose_head)))
    xk = round(xb - GD_GLASS_IN, 6)
    for k, (y0, y1) in enumerate(zip(GD_BACK_BAYS, GD_BACK_BAYS[1:])):
        for t, (z0, z1) in enumerate(tiers):
            yo = y0 if so > 0 else -y1            # the bay's inner edge measured toward the room's side
            room = "opr" if (yo >= GD_OPR_Y0 - 0.05 - 1e-9 and t == 0) else f"b{k}"
            panes.append((f"win_{room}_b{k}t{t}", room, ("x", xk, 1.0), (y0, y1, z0, z1)))
    mull = [b[0] for b in bays[1:]]
    return {"wi": wi, "xd": xd, "ox0": ox0, "bays": bays, "tiers": tiers, "z_head": z_head,
            "z_nose_head": z_nose_head, "mullions_x": mull,
            "outriggers_x": [round(xn + 0.075, 6)] + mull + [round(xb - 0.085, 6)],
            "sign_bay": sign_bay, "panes": panes,
            "notch": (round(xd - GD_NOTCH_HALF, 6), round(xd + GD_NOTCH_HALF, 6)),
            "door_clear": (round(xd - GD_SIDE_DOOR[0] / 2.0, 6), round(xd + GD_SIDE_DOOR[0] / 2.0, 6))}


def _pane(name, plane, rect):
    """One `_Win` pane quad. plane ("y", y, s): rect (x0, x1, z0, z1) in the plane y facing s Y;
    ("x", x, s): rect (y0, y1, z0, z1) in the plane x facing s X."""
    ax, c, s = plane
    a0, a1, z0, z1 = rect
    if ax == "y":
        return _quad(name, [(a0, c, z0), (a1, c, z0), (a1, c, z1), (a0, c, z1)], (0.0, s, 0.0))
    return _quad(name, [(c, a0, z0), (c, a1, z0), (c, a1, z1), (c, a0, z1)], (s, 0.0, 0.0))


def _mb_box(mb, x0, x1, y0, y1, z0, z1, drop=()):
    """Add an axis-aligned box's faces to an _MB builder (drop: "+X" ... "-Z")."""
    faces = {"+X": ([(x1, y0, z0), (x1, y1, z0), (x1, y1, z1), (x1, y0, z1)], (1, 0, 0)),
             "-X": ([(x0, y0, z0), (x0, y1, z0), (x0, y1, z1), (x0, y0, z1)], (-1, 0, 0)),
             "+Y": ([(x0, y1, z0), (x1, y1, z0), (x1, y1, z1), (x0, y1, z1)], (0, 1, 0)),
             "-Y": ([(x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1)], (0, -1, 0)),
             "+Z": ([(x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)], (0, 0, 1)),
             "-Z": ([(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0)], (0, 0, -1))}
    for key in ("+X", "-X", "+Y", "-Y", "+Z", "-Z"):
        if key not in drop:
            mb.f(*faces[key])


def _gd_plinth(name, s, x0, x1, wi, W2, notch=None, zt=STONE_H):
    """A side plinth wall: y between s wi and s W2, x0 .. x1, z 0 .. zt (no top face - the coping sits
    on it - no bottom face: an engine-base part - and no outer face: the stone shell covers it), with the
    door notch (n0, n1) cut from the platform top DECK_H up (a threshold and two jamb faces). The inner
    face is split at the notch x and at DECK_H, so no face carries a T-vertex."""
    mb = _MB()
    yo, yi = s * W2, s * wi
    if notch is None:
        cols = [(x0, x1, (0.0, zt))]
    else:
        n0, n1 = notch
        cols = [(x0, n0, (0.0, DECK_H, zt)), (n0, n1, (0.0, DECK_H)), (n1, x1, (0.0, DECK_H, zt))]
    for xa, xc, zs_ in cols:
        for za, zc_ in zip(zs_, zs_[1:]):
            # the outer face lies under the stone shell (stonebase_*, 0.05 proud, z 0 .. STONE_H) and is
            # not built; the inner face carries the engine-base edges
            mb.f([(xa, yi, za), (xc, yi, za), (xc, yi, zc_), (xa, yi, zc_)], (0.0, -s, 0.0))
    if notch is not None:
        n0, n1 = notch
        mb.f([(n0, yi, DECK_H), (n1, yi, DECK_H), (n1, yo, DECK_H), (n0, yo, DECK_H)], (0.0, 0.0, 1.0))
        mb.f([(n0, yi, DECK_H), (n0, yo, DECK_H), (n0, yo, zt), (n0, yi, zt)], (1.0, 0.0, 0.0))
        mb.f([(n1, yi, DECK_H), (n1, yo, DECK_H), (n1, yo, zt), (n1, yi, zt)], (-1.0, 0.0, 0.0))
    for x, nx in ((x0, -1.0), (x1, 1.0)):
        mb.f([(x, yi, 0.0), (x, yo, 0.0), (x, yo, zt), (x, yi, zt)], (nx, 0.0, 0.0))
    return mb.obj(name)


def build_gd_nose_wall_r1(p):
    """wall_nose (revision 1): the portal wall across the full width |y| <= w_half, z 0 .. GD_LINTEL_Z,
    with the two cabin-silhouette portals (draft 1's outline, G1) - outer / inner faces, the reveals, the
    end faces and the top (the nose glazing's sill). Open bottom: an engine-base part."""
    xn, wt, W2 = p.x_nose, WALL_T, p.w_half
    zl = GD_LINTEL_Z
    poly = [(-W2, 0.0)]
    for s in (-1.0, 1.0):
        seq = gd_portal_yz(p, s)
        poly += list(reversed(seq)) if s < 0 else seq
    poly += [(W2, 0.0), (W2, zl), (-W2, zl)]
    mb = _MB()
    for tri in _tess(poly):
        a, b, c = (poly[i] for i in tri)
        mb.f([(xn, a[0], a[1]), (xn, b[0], b[1]), (xn, c[0], c[1])], (-1.0, 0.0, 0.0))
        mb.f([(xn + wt, a[0], a[1]), (xn + wt, b[0], b[1]), (xn + wt, c[0], c[1])], (1.0, 0.0, 0.0))
    for s in (-1.0, 1.0):
        seq = gd_portal_yz(p, s)
        for (y0, z0), (y1, z1) in zip(seq, seq[1:]):
            mb.f([(xn, y0, z0), (xn + wt, y0, z0), (xn + wt, y1, z1), (xn, y1, z1)],
                 (0.0, s * (z1 - z0), -s * (y1 - y0)))
        mb.f([(xn, s * W2, 0.0), (xn + wt, s * W2, 0.0), (xn + wt, s * W2, zl), (xn, s * W2, zl)],
             (0.0, s, 0.0))
    mb.f([(xn, -W2, zl), (xn + wt, -W2, zl), (xn + wt, W2, zl), (xn, W2, zl)], (0.0, 0.0, 1.0))
    return mb.obj("wall_nose")


def build_gd_gables(p):
    """gable_nose / gable_back: the steel fascia between the end ring beams' tops (z_top) and the
    shallow roof's underside, |y| <= w_half + 0.10, 0.10 in front of the end faces."""
    W2 = p.w_half
    pts, _R, _zc = arch(p)
    lim = W2 + 0.10
    ys = sorted(set([-lim, lim] + [round(y, 6) for y, _z in pts if abs(y) < lim - 1e-6]))
    out = []
    for which, x, sgn in (("nose", p.x_nose - 0.10, -1.0), ("back", p.x_back + 0.10, 1.0)):
        mb = _MB()
        for y0, y1 in zip(ys, ys[1:]):
            mb.f([(x, y0, p.z_top), (x, y1, p.z_top), (x, y1, gd_roof_z(p, y1, True)),
                  (x, y0, gd_roof_z(p, y0, True))], (sgn, 0.0, 0.0))
        out.append((mb.obj(f"gable_{which}"), "paint1"))
    return out


def build_gd_entry(p, s, tag, L):
    """A skier entry (Richard: "slightly bigger openings for the skiers to enter into"): double glass
    doors with GD_SIDE_DOOR clear (1.80 x 2.30 m; draft 1 had one 0.85 x 1.95 leaf) in the door bay -
    a frame of two jambs (no centre post: the two leaves meet at the middle, so the whole 1.80 m is one
    passage when both stand open; the rev-1 checker's F3), two glass leaves in darksteel stile frames
    with push bars - a 2.00 m ramp from grade to the platform top, a steel canopy on two tie rods to
    the door bay's mullions. The head is GD_DOOR_HEAD (the first transom's underside). Every door part
    is named door_<tag>_* (G3: the rays through the opening may only meet the door's own parts; the
    clear-opening rays count the fixed frame and only open the leaves, stiles and bars)."""
    W2, xd = p.w_half, L["xd"]
    dw, dh = GD_SIDE_DOOR
    hw, hb = dw / 2.0, GD_DOOR_BAY / 2.0
    z0, z1 = DECK_H, GD_DOOR_HEAD
    if abs((z1 - z0) - dh) > 1e-9 or abs(GD_TRANSOMS[0] - 0.04 - z1) > 1e-9:
        raise ContractError(f"G3 door head {z1} - sill {z0} != {dh} or the transom is not on the head")

    def ys(a, b):
        return tuple(sorted((s * (W2 + a), s * (W2 + b))))
    out = []
    jy = ys(-0.10, 0.06)
    out.append((_multi_box(f"door_{tag}_frame", [(xd - hb + GD_MULL_W / 2.0, xd - hw) + jy + (z0, z1),
                                                  (xd + hw, xd + hb - GD_MULL_W / 2.0) + jy + (z0, z1)]),
                "paint1"))
    for k, (a, b) in enumerate(((xd - hw, xd - 0.005), (xd + 0.005, xd + hw))):
        gy = ys(-0.055, -0.035)
        out.append((_box(f"door_{tag}_leaf{k}", a + 0.01, b - 0.01, gy[0], gy[1], z0 + 0.02, z1 - 0.02),
                    "glass"))
        fy = ys(-0.065, -0.025)
        st = 0.07
        out.append((_multi_box(f"door_{tag}_stile{k}",
                               [(a, a + st) + fy + (z0 + 0.02, z1 - 0.02),
                                (b - st, b) + fy + (z0 + 0.02, z1 - 0.02),
                                (a + st, b - st) + fy + (z1 - 0.10, z1 - 0.02),
                                (a + st, b - st) + fy + (z0 + 0.02, z0 + 0.24)]), "darksteel"))
        out.append((cs2_kit.add_cyl(f"door_{tag}_bar{k}", 0.018, (b - a) - 0.24,
                                    ((a + b) / 2.0, s * (W2 - 0.005), 1.05), rot=(0.0, math.pi / 2.0, 0.0),
                                    segments=6, caps="both"), "galv"))
    st = cs2_kit.add_wedge(f"stepside_{tag}", 0.0, GD_RAMP_L, 2.0 * GD_NOTCH_HALF, DECK_H, 0.0,
                           open_bottom=True)
    st.rotation_euler = (0.0, 0.0, s * math.pi / 2.0)
    st.location = (xd, s * W2, 0.0)
    _drop_dir(st, "-X")
    out.append((st, "concrete"))
    wc, dc, zc0, tc = GD_CANOPY
    cy = ys(GD_MULL_Y[1], dc)
    out.append((_box(f"canopy_{tag}", xd - wc / 2.0, xd + wc / 2.0, cy[0], cy[1], zc0, zc0 + tc,
                     drop=("-Y" if s > 0 else "+Y",)), "paint1"))
    for k, sx in enumerate((-1.0, 1.0)):
        x = xd + sx * hb
        out.append((_cyl_pts(f"canopyrod_{tag}{k}", (x, s * (W2 + dc - 0.18), zc0 + tc),
                             (x, s * (W2 + GD_MULL_Y[1]), 4.40), 0.02, 4), "galv"))
    return out


def build_gd_signs_r1(p, L):
    """The three signs (emblem + vector "8", one reserved rect): signband_n on the portal face's sign
    spandrel above the portals (GD_SIGN_NOSE_Z), signband_p / q on accent plates across one side bay
    (GD_SIDE_SIGN_Z), mounted on the mullion fronts."""
    lay = sign_layout(plate_digits(p))
    W, H = lay["W"], lay["H"]
    xn, W2 = p.x_nose, p.w_half
    out = []
    x = xn - 0.06 - 0.012
    zn0, zn1 = gd_sign_nose_z(p) - H / 2.0, gd_sign_nose_z(p) + H / 2.0
    out.append((_quad("signband_n", [(x, W / 2, zn0), (x, -W / 2, zn0), (x, -W / 2, zn1), (x, W / 2, zn1)],
                      (-1.0, 0.0, 0.0)), "signband"))
    a, b, _kind = L["bays"][L["sign_bay"]]
    xs = (a + b) / 2.0
    z0, z1 = GD_SIDE_SIGN_Z - H / 2.0, GD_SIDE_SIGN_Z + H / 2.0
    for s, tag in ((1.0, "p"), (-1.0, "q")):
        py = tuple(sorted((s * (W2 + GD_MULL_Y[1]), s * (W2 + GD_MULL_Y[1] + 0.04))))
        out.append((_box(f"signplate_{tag}", a - GD_MULL_W / 2.0, b + GD_MULL_W / 2.0, py[0], py[1],
                         z0 - 0.06, z1 + 0.06), "accent3"))
        y = s * (W2 + GD_MULL_Y[1] + 0.04 + 0.012)
        out.append((_quad(f"signband_{tag}", [(xs - W / 2, y, z0), (xs + W / 2, y, z0), (xs + W / 2, y, z1),
                                              (xs - W / 2, y, z1)], (0.0, s, 0.0)), "signband"))
    return out


def build_gd_hall(p):
    """The revision-1 hall: the cabin trough and the hall ceiling; the STONE PLINTH (side walls with
    the door notches, the back wall, the stone shell 0.05 proud, a concrete coping) under FULL-HEIGHT
    GLAZING between slim steel mullions on both long sides and the back (`_Win` panes, transoms at the
    door head and the deck line, ring beams at the head, corner columns); the PORTAL wall (stone-faced
    plinth, concrete above) with the keyhole portals, a glazing band above the lintel round the sign
    spandrel, the nose ring beam (comm bracket) and the gable fascias; the skier entries (double doors,
    ramps, canopies) and the three signs."""
    L = gd_hall_layout(p)
    W2, wt, wi = p.w_half, WALL_T, L["wi"]
    xn, xb, ze = p.x_nose, p.x_back, p.z_top
    zh, znh = L["z_head"], L["z_nose_head"]
    P = GD_PORTAL
    yi, yo = p.lane_y + P["u_lo"], p.lane_y + P["u_hi"]
    notch = L["notch"] if GD_DOOR_OPENINGS else None
    out = []
    out.append((_quad("floor", [(xn, -wi, GD_FLOOR_Z), (xb - wt, -wi, GD_FLOOR_Z),
                                (xb - wt, wi, GD_FLOOR_Z), (xn, wi, GD_FLOOR_Z)], (0, 0, 1)), "concrete"))
    out.append((_quad("ceil_hall", [(xn + wt, -wi, ze), (xb - wt, -wi, ze), (xb - wt, wi, ze),
                                    (xn + wt, wi, ze)], (0, 0, -1)), "darksteel"))
    # the stone plinth
    for s, tag in ((-1.0, "n"), (1.0, "p")):
        out.append((_gd_plinth(f"wall_{tag}", s, xn + wt, xb, wi, W2, notch=notch), "concrete"))
    out.append((_box("wall_back", xb - wt, xb, -wi, wi, 0.0, STONE_H, open_bottom=True,
                     drop=("+Y", "-Y", "+Z", "+X")), "concrete"))     # +X: under stonebase_back
    out.append((build_gd_nose_wall_r1(p), "concrete"))
    for s, tag in ((-1.0, "n"), (1.0, "p")):
        y0, y1 = sorted((s * yi, s * yo))
        out.append((_quad(f"apron_{tag}", [(xn, y0, 0.0), (xn, y1, 0.0), (xn, y1, GD_FLOOR_Z),
                                           (xn, y0, GD_FLOOR_Z)], (-1.0, 0.0, 0.0)), "concrete"))
    for s, tag in ((-1.0, "n"), (1.0, "p")):
        y0, y1 = sorted((s * W2, s * (W2 + STONE_T)))
        segs = [(xn - STONE_T, notch[0]), (notch[1], xb + STONE_T)] if notch else [(xn - STONE_T, xb + STONE_T)]
        out.append((_multi_box(f"stonebase_{tag}", [(a, b, y0, y1, 0.0, STONE_H) for a, b in segs],
                               drop=("-Y" if s > 0 else "+Y",)), "stone"))
    out.append((_box("stonebase_back", xb, xb + STONE_T, -W2, W2, 0.0, STONE_H, open_bottom=True,
                     drop=("-X",)), "stone"))
    out.append((_multi_box("stonebase_nose", [(xn - STONE_T, xn, -W2, -yo, 0.0, STONE_H),
                                              (xn - STONE_T, xn, -yi, yi, 0.0, STONE_H),
                                              (xn - STONE_T, xn, yo, W2, 0.0, STONE_H)],
                           drop=("+X",)), "stone"))
    out += build_gd_portal_trims(p)
    # the coping on the plinth (the glazing's sill)
    for s, tag in ((-1.0, "n"), (1.0, "p")):
        y0, y1 = sorted((s * (wi - 0.02), s * (W2 + 0.10)))
        segs = [(xn + wt, notch[0]), (notch[1], xb + 0.10)] if notch else [(xn + wt, xb + 0.10)]
        out.append((_multi_box(f"coping_{tag}", [(a, b, y0, y1, STONE_H, GD_SILL, "keep_bottom")
                                                 for a, b in segs]), "concrete"))
    out.append((_box("coping_back", xb - wt - 0.02, xb + 0.10, -(wi - 0.02), wi - 0.02, STONE_H, GD_SILL,
                     drop=("+Y", "-Y")), "concrete"))
    # the steel frame: corner columns, side mullions, transoms, ring beams
    for s, tag in ((-1.0, "n"), (1.0, "p")):
        ya, yb = sorted((s * (W2 - wt), s * (W2 + GD_MULL_Y[1])))
        out.append((_box(f"mull_cf{tag}", xn - 0.10, xn + wt, ya, yb, STONE_H, znh, drop=("+Z",)), "paint1"))
        out.append((_box(f"mull_cb{tag}", xb - wt, xb + GD_MULL_Y[1], ya, yb, GD_SILL, zh, drop=("+Z",)),
                    "paint1"))
        ya, yb = sorted((s * (W2 + GD_MULL_Y[0]), s * (W2 + GD_MULL_Y[1])))
        for k, xm in enumerate(L["mullions_x"]):
            out.append((_box(f"mull_{tag}{k:02d}", xm - GD_MULL_W / 2.0, xm + GD_MULL_W / 2.0, ya, yb,
                             GD_SILL, zh, drop=("+Z", "-Z")), "paint1"))
        ya, yb = sorted((s * (W2 - 0.10), s * (W2 + 0.06)))
        for k, zt in enumerate(GD_TRANSOMS):
            out.append((_box(f"transom_{tag}{k}", xn + wt, xb - wt, ya, yb, zt - 0.04, zt + 0.04,
                             drop=("+X", "-X")), "paint1"))
        ya, yb = sorted((s * (W2 - wt), s * (W2 + 0.10)))
        out.append((_box(f"ringbeam_{tag}", xn + wt, xb - wt, ya, yb, zh, gd_roof_z(p, wi, inner=True),
                         drop=("+X", "-X", "+Z")), "paint1"))
    # the nose: glazing above the portal lintel round the sign spandrel, the ring beam
    for k, y in enumerate(GD_NOSE_BAYS[1:-1]):
        out.append((_box(f"mull_f{k}", xn - 0.10, xn + 0.12, y - GD_MULL_W / 2.0, y + GD_MULL_W / 2.0,
                         GD_LINTEL_Z, znh, drop=("+Z", "-Z")), "paint1"))
    lay = sign_layout(plate_digits(p))
    zs0 = min(GD_LINTEL_Z, gd_sign_nose_z(p) - lay["H"] / 2.0 - 0.05)
    out.append((_box("spandrel_nose", xn - 0.06, xn + 0.18, -1.15 + GD_MULL_W / 2.0, 1.15 - GD_MULL_W / 2.0,
                     zs0, znh, drop=("+Z",)), "accent3"))
    out.append((_box("ringbeam_nose", xn - 0.10, xn + wt, -(W2 + 0.10), W2 + 0.10, znh, ze, drop=("+Z",)),
                "paint1"))
    # the back: glazing over the back plinth
    for k, y in enumerate(GD_BACK_BAYS[1:-1]):
        out.append((_box(f"mull_b{k}", xb - 0.12, xb + 0.08, y - GD_MULL_W / 2.0, y + GD_MULL_W / 2.0,
                         GD_SILL, zh, drop=("+Z", "-Z")), "paint1"))
    for k, zt in enumerate(GD_TRANSOMS):
        out.append((_box(f"transom_b{k}", xb - 0.10, xb + 0.06, -wi, wi, zt - 0.04, zt + 0.04,
                         drop=("+Y", "-Y")), "paint1"))
    out.append((_box("ringbeam_back", xb - wt, xb + 0.10, -(W2 + 0.10), W2 + 0.10, zh, ze, drop=("+Z",)),
                "paint1"))
    out += build_gd_gables(p)
    for nm, _room, plane, rect in L["panes"]:
        out.append((_pane(nm, plane, rect), "glass"))
    for s, tag in ((-1.0, "n"), (1.0, "p")):
        out += build_gd_entry(p, s, tag, L)
    out += build_gd_signs_r1(p, L)
    return out


def build_gd_roof(p):
    """The shallow standing-seam roof (revision 1): the outer shell over the arch (eave edges at
    z_top, |y| = w_half + GD_EAVE), the underside split into the hall lining (|y| <= w_half, above the
    ceiling) and the two cedar EAVE SOFFITS (|y| >= w_half, seen from the ground), the end rims; the nose
    rake ROOF_END (the nose ladder passes it), the back rake GD_RAKE_BACK."""
    pts, _R, zc = arch(p)
    c, W2 = p.chord_half, p.w_half
    ys = sorted(set([round(y, 6) for y, _z in pts] + [-W2, W2]))
    xa, xb = p.x_nose - ROOF_END, p.x_back + GD_RAKE_BACK
    t = ROOF_T
    mb, ml, mr = _MB(), _MB(), _MB()
    ms = {"n": _MB(), "p": _MB()}
    for y0, y1 in zip(ys, ys[1:]):
        z0, z1 = gd_roof_z(p, y0), gd_roof_z(p, y1)
        n = (0.0, (y0 + y1) / 2.0, (z0 + z1) / 2.0 - zc)
        mb.f([(xa, y0, z0), (xb, y0, z0), (xb, y1, z1), (xa, y1, z1)], n)
        if y0 >= -W2 - 1e-9 and y1 <= W2 + 1e-9:
            tgt = ml
        else:
            tgt = ms["n"] if y1 <= -W2 + 1e-9 else ms["p"]
        tgt.f([(xa, y0, z0 - t), (xb, y0, z0 - t), (xb, y1, z1 - t), (xa, y1, z1 - t)], (0.0, -n[1], -n[2]))
        for x, sx in ((xa, -1.0), (xb, 1.0)):
            mr.f([(x, y0, z0), (x, y1, z1), (x, y1, z1 - t), (x, y0, z0 - t)], (sx, 0.0, 0.0))
    for y, sy in ((ys[0], -1.0), (ys[-1], 1.0)):
        z = gd_roof_z(p, y)
        mb.f([(xa, y, z), (xb, y, z), (xb, y, z - t), (xa, y, z - t)], (0.0, sy, 0.0))
    return [(mb.obj("roof_shell"), "paint1"), (mr.obj("roof_rim"), "paint1"),
            (ml.obj("rooflining"), "paint1"), (ms["n"].obj("eavesof_n"), "cedar"),
            (ms["p"].obj("eavesof_p"), "cedar")]


def build_gd_eaves(p):
    """Under and on the deep eaves: tapered steel outriggers at every mullion (0.30 deep at the ring
    beam, 0.12 at the eave), linear soffit lights between them (the modelled lamps kept: no vanilla
    linear light exists), a snow-guard rail on posts GD_SNOW_IN in from each eave edge, the eave
    gutters and four downspouts routed back under the eave to the wall at the corners."""
    L = gd_hall_layout(p)
    W2, c, ze = p.w_half, p.chord_half, p.z_top
    xn, xb = p.x_nose, p.x_back
    xs = L["outriggers_x"]
    out = []
    y0, y1 = W2 + 0.10, c - 0.06
    t0, t1 = gd_roof_z(p, y0, True) - 0.002, gd_roof_z(p, y1, True) - 0.002
    b0, b1 = t0 - 0.30, t1 - 0.12
    la, lb = W2 + GD_EAVE_LAMP[0], W2 + GD_EAVE_LAMP[1]
    zl = gd_roof_z(p, lb, True) - 0.003
    yg = c - GD_SNOW_IN
    zr = gd_roof_z(p, yg)
    for s, tag in ((-1.0, "n"), (1.0, "p")):
        mb = _MB()
        for x in xs:
            for xx, nx in ((x - 0.05, -1.0), (x + 0.05, 1.0)):
                mb.f([(xx, s * y0, b0), (xx, s * y1, b1), (xx, s * y1, t1), (xx, s * y0, t0)], (nx, 0.0, 0.0))
            mb.f([(x - 0.05, s * y0, b0), (x + 0.05, s * y0, b0), (x + 0.05, s * y1, b1), (x - 0.05, s * y1, b1)],
                 (0.0, 0.0, -1.0))
            mb.f([(x - 0.05, s * y1, b1), (x + 0.05, s * y1, b1), (x + 0.05, s * y1, t1), (x - 0.05, s * y1, t1)],
                 (0.0, s, 0.0))
        out.append((mb.obj(f"outrig_{tag}"), "paint1"))
        mb = _MB()
        ya, yb = sorted((s * la, s * lb))
        for xa_, xb_ in zip(xs, xs[1:]):
            if (xb_ - 0.10) - (xa_ + 0.10) < 0.5:
                continue
            _mb_box(mb, xa_ + 0.10, xb_ - 0.10, ya, yb, zl - 0.05, zl, drop=("+Z",))
        out.append((mb.obj(f"lamp_eave{tag}"), "lamp"))
        mb = _MB()
        _mb_box(mb, xn + 0.30, xb - 0.30, s * yg - 0.025, s * yg + 0.025, zr + 0.08, zr + 0.13)
        for x in [xn + 0.40] + xs[1:-1] + [xb - 0.40]:
            _mb_box(mb, x - 0.02, x + 0.02, s * yg - 0.02, s * yg + 0.02, zr - 0.02, zr + 0.08, drop=("-Z", "+Z"))
        out.append((mb.obj(f"snowguard_{tag}"), "galv"))
        g0, g1 = sorted((s * c, s * (c + 0.12)))
        out.append((_box(f"gutter_{tag}", xn - ROOF_END, xb + GD_RAKE_BACK, g0, g1, ze - 0.14, ze - 0.02,
                         drop=("+Z",)), "galv"))
        for k, xg in enumerate((xn + 0.40, xb - 0.45)):
            pts = [(xg, s * (c + 0.06), ze - 0.14), (xg, s * (c + 0.06), ze - 0.45),
                   (xg, s * (W2 + 0.22), ze - 0.85), (xg, s * (W2 + 0.22), 0.30),
                   (xg, s * (W2 + 0.40), 0.12)]
            out.append((cs2_kit.tube_along(f"downspout_{tag}{k}", pts, 0.05, segments=6, caps="none",
                                           bend_rings=2), "galv"))
    return out


# the dressing families of revision 1 (_dress_names: part clash, LOD, density, weathering)
GD_DRESS_FAMS = ("board_", "label_", "ext_", "hosereel_", "estop_", "skirack_", "ski_", "tool_")


def build_gd_dress(p):
    """Revision 1 dressing - the detach detail pass's kit (cs2_liftdress) where it fits the glazed
    hall: LIFT OPS over the operator room door, WAIT HERE on a post at the loading (+Y, gd_wait_side) entry,
    extinguishers on the plinth by both platforms' walkways, a hose reel by the +Y entry, e-stop posts
    at both boarding ends; outside the -Y entry a ski rack (the shared cs2_liftdress.ski_rack gear) and
    a scraper + shovel against the plinth. Labels are ONE quad each (seeded at their sheet rect)."""
    if not (p.gondola and GD_REV1):
        return []
    LD = _LD()
    L = gd_hall_layout(p)
    X, xd, W2, wi, ox0 = p.x_wheel, L["xd"], p.w_half, L["wi"], L["ox0"]
    so = gd_opr_side(p)                 # nitpick A2: the room's side (its door's walkway is so * yd)
    yd = (GD_WALK_IN + wi) / 2.0
    up = (0.0, 0.0, 1.0)

    def wall_right(f):
        _n, r = LD._plan(f)
        return (r.x, r.y, 0.0)
    out = []
    fine = FINE_DETAIL                  # fine-detail step 3b: the kit props become rows (fd_kit_specs)
    fkw = {"fine": True} if fine else {}
    zb = DECK_H + GD_DOOR_H + 0.06 + 0.06 + 0.175      # above the door frame's head (0.06 + reveal)
    out += _tag_label(LD.board("ops", (ox0 - 0.023, so * (yd - 0.10), zb), "-X", 1.20, 0.35, mount="wall", **fkw),
                      wall_right("-X"), up, "ops")
    sw = gd_wait_side()                 # nitpick A3: the loading (+Y) entry, facing the skiers coming in
    fw = "+Y" if sw > 0 else "-Y"
    out += _tag_label(LD.board("waitn", (xd + 1.30, sw * (GD_PLAT_IN + 0.62), DECK_H + 1.30), fw, 0.60, 0.35,
                               mount="post", post_z0=DECK_H, **fkw), wall_right(fw), up, "waithere")
    if fine:
        return out
    for s, tag in ((-1.0, "n"), (1.0, "p")):
        out += LD.extinguisher(f"pl{tag}", (X - 1.30, s * wi), "+Y" if s < 0 else "-Y", z=DECK_H + 0.15)
        out += LD.estop_post(f"pl{tag}", (X - 1.90, s * 5.70), z0=DECK_H)
    out += LD.hose_reel("pl", (xd - 1.60, so * wi), "-Y" if so > 0 else "+Y", z=DECK_H + 0.45, r=0.22, w=0.16)
    out += LD.ski_rack("gn", xd + 2.2, xd + 5.2, y=-(W2 + 1.05), z0=0.02, pairs=7, boards=2, side=-1.0)
    out += LD.tools("gn", foot=(xd + 5.75, -(W2 + STONE_T + 0.32)), facing="-Y", z0=0.02)
    return out


def gd_flood_spots(p):
    """T17-DQ 2026-10-16: lamp posts -> SkiPisteFloodlightLow (Richard): the gondola stations' two floodlights,
    the portal-face corner posts of the old LightpolePark rows (authoring (x_nose - 1.0, +-7.2), on grade);
    yaw by d1_flood_yaw (beam to the line, across to the axis). The entry posts (x_door - 2.4, +-(W2 + 2.2))
    are dropped (two floodlights per station)."""
    return [(round(p.x_nose - 1.0, 4), s * 7.2) for s in (-1.0, 1.0)]


def gd_subobjects(p):
    """Revision 1 vanilla sub-objects (cs2_liftdress.SUBOBJ GUIDs, verified against the dump): the lamp
    posts are VANILLA LightpolePark props (Richard, eleventh look: no modelled post lights), the
    wall / ceiling lights vanilla WallLight / RoofLight. Rows (prefab, authoring position, facing in the
    authoring frame or None, mount): at each portal a WallLight over the slot and a LightpolePark at the
    portal-face corner; at each entry a RoofLight under the canopy, a LightpolePark beside the ramp, a
    bin and a bench; three RoofLights over each platform on the hall ceiling; a WallLight at the
    operator door; the base adds a park info sign by the +Y entry. The surgery round adds them to the
    station's ObjectSubObjects (parent 0, game local = (-y, z, x), gx negated)."""
    if not (p.gondola and GD_REV1):
        return []
    L = gd_hall_layout(p)
    xn, W2, X, xd, ox0, ze = p.x_nose, p.w_half, p.x_wheel, L["xd"], L["ox0"], p.z_top
    _wc, _dc, zc0, _tc = GD_CANOPY
    rows = []
    for s in (-1.0, 1.0):
        fac_in = "-Y" if s > 0 else "+Y"
        rows.append(("WallLightShortRangeRandom01", (xn - 0.02, s * (p.lane_y + 0.33), 5.65), "-X", "wall"))
        # T17-DQ 2026-10-16: lamp posts -> SkiPisteFloodlightLow (Richard): the two LightpoleParkRandom01 rows
        # per side are gone (the portal-face corner post (xn - 1.0, s * 7.2) is now a floodlight, see
        # gd_flood_spots; the entry post (xd - 2.4, s * (W2 + 2.2)) is dropped: two floodlights per station)
        rows.append(("RoofLightRandom01", (xd, s * (W2 + 0.95), zc0), None, "ceiling"))
        for x in (xn + 2.5, (xn + X) / 2.0, X - 3.0):
            rows.append(("RoofLightRandom01", (round(x, 4), s * 5.15, ze), None, "ceiling"))
        rows.append(("TrashbinRandom01", (xd + 1.70, s * (W2 + 1.20), 0.0), None, "ground"))
        rows.append(("GardenBenchRandom01", (xd - 4.4, s * (W2 + 0.75), 0.0), "+Y" if s > 0 else "-Y",
                     "ground"))
    rows.append(("WallLightShortRangeRandom01", (ox0 - 0.02, gd_opr_side(p) * 4.55, 2.10), "-X", "wall"))
    if p.kind == "base":
        rows.append(("ParkInfosignRandom01", (xd - 7.2, W2 + 2.6, 0.0), "+Y", "ground"))
    return rows


def gd_split_rooms(t, table, rooms, name):
    """Revision 1 `_Win` split: the rooms grouped by their id (G5), ONE cs2_submesh.split_submesh per id
    with every room's pane prefix, so rooms that reuse an id (>= 3 m apart) are tiled inside that one
    5 x 5 cell without overlapping (the validator's sub-mesh rule; the importer rewrites UV0 and reads
    only the cell). The subs are joined into <name>_Win (the cs2_bldg.split_rooms pattern)."""
    by_id = {}
    for r, i in sorted(rooms.items()):
        by_id.setdefault(int(i), []).append(r)
    subs = []
    for k, (cid, rs) in enumerate(sorted(by_id.items())):
        sub = cs2_submesh.split_submesh(t, table, tuple(f"win_{r}_" for r in rs), "Win",
                                        win_cell=(cid % 5, cid // 5))
        sub.name = f"{name}Win{k}"
        sub.data.name = sub.name
        subs.append(sub)
    main_sub = subs[0]
    if len(subs) > 1:
        bpy.ops.object.select_all(action="DESELECT")
        for s_ in subs:
            s_.select_set(True)
        bpy.context.view_layer.objects.active = main_sub
        bpy.ops.object.join()
        main_sub = bpy.context.active_object
    main_sub.name = cs2_submesh.submesh_name(name, "Win")
    main_sub.data.name = main_sub.name
    if len(main_sub.data.uv_layers) != 1:
        raise ContractError(f"{main_sub.name}: {len(main_sub.data.uv_layers)} UV layers")
    print(f"[detach] _Win: {len(rooms)} rooms -> {len(by_id)} ids (one cell each), "
          f"{len(main_sub.data.polygons)} panes / {cs2_asset.tri_count(main_sub)} tris")
    return main_sub


# --------------------------------------------------------------------------------------
# Station
# --------------------------------------------------------------------------------------

def build_station(p):
    """[(obj, material name)] in a fixed order (determinism)."""
    parts = []
    if not p.gondola:
        parts += build_deck(p)
        parts += build_pedestals(p)
        parts += build_beams(p)
        parts += build_roof(p)
        parts.append((_quad("ceil_canopy", [(p.x_nose + BAND_T, -(p.w_half - BAND_T), p.z_top),
                                            (p.x_back - BAND_T, -(p.w_half - BAND_T), p.z_top),
                                            (p.x_back - BAND_T, p.w_half - BAND_T, p.z_top),
                                            (p.x_nose + BAND_T, p.w_half - BAND_T, p.z_top)],
                            (0, 0, -1)), "darksteel"))
        parts += build_band_ring(p)
        parts += build_signbands(p, p.w_half)
        parts += build_housing_windows(p)
        for which in ("nose", "back"):
            parts.append((build_endglass(p, which, p.w_half), "glass"))
            parts += build_endmullions(p, which, (-2.25, 0.0, 2.25), p.z_top,
                                       p.z_top + 0.45 * p.roof_rise, p.w_half)
        parts += build_gutters(p, DECK_H)
        parts += build_soffit(p)
        parts += build_machine(p)
        if p.kind == "top":
            parts += build_nose_guides(p)
        parts += build_access(p)
        parts += build_buildings(p)
        parts += build_soffit_lamps(p)
        parts += build_comm(p)
        parts += build_wx(p)
        parts += build_detail(p)             # detail pass 1: dressing + labels (DQ)
    else:
        # revision 1 (the long glazed hall): plinth + curtain walls + portal wall + entries + signs,
        # the shallow roof on deep eaves, its outriggers / lights / snow guards / gutters
        parts += build_gd_hall(p)
        parts += build_gd_roof(p)
        parts += build_gd_eaves(p)
        parts += build_machine(p)
        if p.kind == "top":
            parts += build_nose_guides(p)
        parts += build_gd_interior(p)        # draft 1: platforms, operator room, deck, stair
        parts += build_gd_ladders(p)         # draft 1: nose ladder from the ground + roof ladder
        parts += build_comm(p)
        parts += build_wx(p)
        parts += build_gd_dress(p)           # revision 1: the detail pass's kit where it fits
    parts = cut_long_parts(parts)
    names = [o.name for o, _ in parts]
    bad = sorted(n for n in names if "." in n)
    if bad or len(set(names)) != len(names):
        raise RuntimeError(f"part names collided (Blender renamed): {bad[:8]}")
    missing = sorted({m for _o, m in parts} - set(materials_for(p, parts)))
    if missing:
        raise RuntimeError(f"parts use undefined materials {missing}")
    import cs2_sheaves as S
    S.cull_hidden_bosses(parts, label=p.stem)     # T62 round 3: hidden hub bosses dropped (measured; v1 no-op)
    return parts


def assemble(parts, finish, p):
    """bevels -> strip UVs -> density -> tag_parts -> material_sources -> join ->
    remove_doubles -> part table -> shade (contract 9 step 3, MUST-CHANGE 3/4). Returns the
    joined object in the AUTHORING frame (orient_for_net runs after the contracts)."""
    if finish:
        with _lt_override(BEVEL_PREFIXES=bevel_prefixes(), BEVEL_EXCLUDE=BEVEL_EXCLUDE):
            lift_terminal.apply_finish_bevels(parts)
    for obj, _m_ in parts:                      # MUST-CHANGE 4: kit / bpy.ops parts carry
        me = obj.data                           # "UVMap", local builders none -> strip all
        while me.uv_layers:
            me.uv_layers.remove(me.uv_layers[0])
    cs2_uv.set_density(parts, density_for(p))
    table = cs2_asset.tag_parts(parts)
    bpy.context.view_layer.update()             # project_world_uv reads matrix_world
    seed_parts(parts, p)
    sources = cs2_bake.material_sources(materials_for(p, parts), parts)
    # Revision 1 (e): the reserved sign rects are restored 1:1 from the material that fills
    # them (cs2_bake._restore_reserved), which must sample the PACKED source layer; the
    # signband material is a constant (nothing to copy), so its sampling layer is moot
    keys = sorted({str(m) for _o, m in parts})
    for pinned in ("signband", "label"):        # detail pass 1: the label quads fill their rects too
        if pinned in keys:
            i = keys.index(pinned)
            sources[i] = dataclasses.replace(sources[i], uv=None)
    bpy.ops.object.select_all(action="DESELECT")
    for obj, _m_ in parts:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = parts[0][0]
    bpy.ops.object.join()
    t = bpy.context.active_object
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=0.0005)
    bpy.ops.object.mode_set(mode="OBJECT")
    cs2_asset.store_part_table(t, table)
    cs2_asset.shade_by_angle(t, SHADE_ANGLE)
    layers = sorted(l.name for l in t.data.uv_layers)
    if layers != sorted([cs2_bake.TILE_LAYER, SEED_LAYER]):
        raise RuntimeError(f"joined mesh UV layers {layers}, expected UVTile + {SEED_LAYER}")
    t.data.uv_layers.active = t.data.uv_layers[cs2_bake.TILE_LAYER]
    print(f"[detach] assembled {len(table)} parts, {cs2_asset.tri_count(t)} tris, "
          f"{len(sources)} materials {sorted(materials_for(p, parts))}")
    return t, table, sources


def materials_for(p, parts=None):
    """The material table of this station: MATERIALS; detail pass 1 (DQ) adds the dressing keys
    the parts use through cs2_liftdress.merge_materials (one key, one look on every station: the
    reused galv / darksteel / rubber must equal this table's)."""
    import cs2_sheaves as S
    wm = S.wheel_materials(_MS)              # T62 round 2: the one wheel material under v2 ({} under v1)
    if p.gondola and not GD_REV1:
        return {**MATERIALS, **wm}
    if not p.gondola and not DETAIL1:
        return {**MATERIALS, **wm}
    LD = _LD()
    keys = LD.part_keys(parts) if parts is not None else sorted(LD.DRESS_MATERIALS)
    return {**LD.merge_materials(MATERIALS, [k for k in keys if k in LD.DRESS_MATERIALS]), **wm}



# Long parts are cut into pieces along X before the pack seed: smart_project keeps a part
# connected, and 20-33 m weighted strips (roof, band sides, haul ropes) left the shared
# packer (cs2_uv.pack_unique, AABB, rotate=False) a third of the square (measured 33 %
# coverage, median 18 px/m at 2048). Piece names keep the part prefix (LOD / relief /
# density / contract keys).
CUT_PIECES = {"roof_shell": 3, "rooflining": 2, "band_p": 3, "band_n": 3, "bandlining_p": 2,
              "bandlining_n": 2, "haulrope_n": 3, "haulrope_p": 3, "spine_main": 2,
              "gutter_p": 2, "gutter_n": 2, "soffit_tongue": 2, "soffit_outer": 2,
              "detrail_n": 2, "detrail_p": 2, "ceil_canopy": 2, "ceil_hall": 2, "floor": 2}



def cut_long_parts(parts):
    """[(obj, mat)] -> same list with every CUT_PIECES part replaced by k pieces cut at
    equal X intervals of its world extent (bmesh bisect, the cut verts coincide and are
    welded again by remove_doubles after the join)."""
    out = []
    for obj, mat in parts:
        k = CUT_PIECES.get(obj.name)
        if not k:
            out.append((obj, mat))
            continue
        M = obj.matrix_basis.copy()
        xs = [(M @ v.co).x for v in obj.data.vertices]
        x0, x1 = min(xs), max(xs)
        cuts = [x0 + (x1 - x0) * i / k for i in range(1, k)]
        base = obj.name
        src = obj.data
        for i in range(k):
            lo = cuts[i - 1] if i > 0 else None
            hi = cuts[i] if i < k - 1 else None
            bm = bmesh.new()
            bm.from_mesh(src)
            bm.transform(M)
            for plane_x, keep_pos in ((lo, True), (hi, False)):
                if plane_x is None:
                    continue
                geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
                bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-6, plane_co=(plane_x, 0.0, 0.0),
                                       plane_no=(1.0, 0.0, 0.0), clear_inner=keep_pos,
                                       clear_outer=not keep_pos)
            name = f"{base}{i}" if base.endswith("_") else f"{base}_{i}"
            me = bpy.data.meshes.new(name)
            bm.to_mesh(me)
            bm.free()
            o = bpy.data.objects.new(name, me)
            bpy.context.collection.objects.link(o)
            for key, val in obj.items():
                o[key] = val
            out.append((o, mat))
        bpy.data.objects.remove(obj, do_unlink=True)
        bpy.data.meshes.remove(src)
    return out

SEED_LAYER = "UVSeed"
SEED_SIGN = "signband_"                         # Revision 1 (e): seeded AT the reserved rect
SEED_OFFSET_U = 2.0            # every other seed island moves out of [0, 1] so that no smart-
#                                project island can fall inside a reserved rect and be pinned
SEED_FACE = ("roof_rim", "soffit_turn")        # thin arcs / C-shapes: one island per face
SIGN_SHARED = {"q": "p"}       # gondola draft 1: the -Y side sign reuses the +Y sign's rect
GD_SIGN_SHARED_R1 = {"p": "n", "q": "n"}       # gondola revision 1: both side signs on n's rect


def _seed_strip(obj, centre, R, r):
    """Torus-like part (rope torus, U-turn rail) -> ONE straight strip: u = major angle x R,
    v = minor angle x r, unwrapped per face about its own first corner (no wrap inside a
    face). u x v = the outward normal (not mirrored)."""
    me = obj.data
    M = obj.matrix_basis
    cx, cy, cz = centre
    lay = me.uv_layers[SEED_LAYER].data
    for pl in me.polygons:
        P = [M @ me.vertices[me.loops[li].vertex_index].co for li in pl.loop_indices]
        th = [math.atan2(q.y - cy, q.x - cx) for q in P]
        ph = [math.atan2(q.z - cz, math.hypot(q.x - cx, q.y - cy) - R) for q in P]
        tc, pc = th[0], ph[0]
        for li, a, b in zip(pl.loop_indices, th, ph):
            a = tc + (a - tc + math.pi) % (2 * math.pi) - math.pi
            b = pc + (b - pc + math.pi) % (2 * math.pi) - math.pi
            lay[li].uv = (a * R, b * r)


def _seed_faces(obj):
    """One island per face: each face in its own plane frame (u along its first edge,
    v = n x u, so u x v = n), shifted apart so no two faces share a UV."""
    me = obj.data
    lay = me.uv_layers[SEED_LAYER].data
    for k, pl in enumerate(me.polygons):
        P = [Vector(me.vertices[me.loops[li].vertex_index].co) for li in pl.loop_indices]
        n = pl.normal.normalized()
        u = (P[1] - P[0]).normalized()
        v = n.cross(u)
        for li, q in zip(pl.loop_indices, P):
            d = q - P[0]
            lay[li].uv = (d.dot(u) + 100.0 * k, d.dot(v))


def _seed_smart(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66.0), island_margin=0.0,
                             area_weight=0.0, correct_aspect=True, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode="OBJECT")


def seed_parts(parts, p):
    """The PACK SOURCE for bake_unique (src_uv), one layer on EVERY part before the join.
    UVTile (world-anchored per-face box/planar frames, material_sources) stays the layer the
    materials SAMPLE; as the pack source it gave ~1,300 islands, and parts welded by the join
    (band ring, soffit U, torus) became rings whose empty bounding boxes took most of the
    square (measured: median 11 px/m, 17 % coverage at 2048)."""
    centres = {"wheelrope": ((p.x_wheel, 0.0, p.z_wheel), p.torus_r, ROPE_R),
               "detrail_u": ((p.x_wheel, 0.0, z_rail(p)), p.lane_y - 0.20, 0.05)}
    rects = sign_rects(p)
    shared = sign_shared(p)
    labels = label_sheet(p)
    for obj, _m_ in parts:
        obj.data.uv_layers.new(name=SEED_LAYER, do_init=False)
        if obj.name.startswith(SEED_SIGN):
            key = obj.name[len(SEED_SIGN):]
            _seed_sign(obj, rects[shared.get(key, key)])
            continue
        if obj.name in labels:                  # detail pass 1: a label quad AT its sheet rect
            _seed_label(obj, tuple(v / float(ATLAS) for v in labels[obj.name]))
            continue
        if obj.name in centres:
            _seed_strip(obj, *centres[obj.name])
        elif obj.name.startswith(SEED_FACE):
            _seed_faces(obj)
        else:
            _seed_smart(obj)
        lay = obj.data.uv_layers[SEED_LAYER].data
        uv = np.empty(len(lay) * 2, dtype=np.float32)
        lay.foreach_get("uv", uv)
        uv = uv.reshape(-1, 2)
        uv[:, 0] += SEED_OFFSET_U - np.floor(uv[:, 0].min())
        lay.foreach_set("uv", uv.ravel())


def _seed_label(obj, rect):
    """Detail pass 1: a label quad's seed IS its sheet rect (pinned by the bake): u along the
    label's reading right, v along its up (the frame recorded by build_detail, d1_right / d1_up;
    walls: right = up x n, up = +Z; floor marks: the read-up plan direction)."""
    me = obj.data
    if len(me.polygons) != 1 or me.polygons[0].loop_total != 4:
        raise ContractError(f"{obj.name}: a label is ONE quad")
    if "d1_right" not in obj or "d1_up" not in obj:
        raise ContractError(f"{obj.name}: no reading frame recorded (build_detail)")
    right = Vector(tuple(obj["d1_right"])).normalized()
    up = Vector(tuple(obj["d1_up"])).normalized()
    M = obj.matrix_basis
    pl = me.polygons[0]
    P = [M @ Vector(me.vertices[me.loops[li].vertex_index].co) for li in pl.loop_indices]
    ss = [q.dot(right) for q in P]
    ts = [q.dot(up) for q in P]
    s0, s1, t0, t1 = min(ss), max(ss), min(ts), max(ts)
    u0, v0, u1, v1 = rect
    lay = me.uv_layers[SEED_LAYER].data
    for li, s_, t_ in zip(pl.loop_indices, ss, ts):
        lay[li].uv = (u0 + (s_ - s0) / (s1 - s0) * (u1 - u0), v0 + (t_ - t0) / (t1 - t0) * (v1 - v0))


def _seed_sign(obj, rect):
    """Revision 1 (e): the sign quad's seed IS its reserved rect: u -> the viewer's right
    (cross(+Z, n)), v -> +Z, so the stamp reads unmirrored on both sides (asserted again on
    UVUnique by check_sign_uv). pack_unique pins it there (all loops inside the rect)."""
    me = obj.data
    if len(me.polygons) != 1 or me.polygons[0].loop_total != 4:
        raise ContractError(f"{obj.name}: a sign panel is ONE quad")
    pl = me.polygons[0]
    n = Vector(pl.normal).normalized()
    right = Vector((0.0, 0.0, 1.0)).cross(n).normalized()
    P = [Vector(me.vertices[me.loops[li].vertex_index].co) for li in pl.loop_indices]
    ss = [q.dot(right) for q in P]
    zs = [q.z for q in P]
    s0, s1, z0, z1 = min(ss), max(ss), min(zs), max(zs)
    u0, v0, u1, v1 = rect
    lay = me.uv_layers[SEED_LAYER].data
    for li, q in zip(pl.loop_indices, P):
        fu = (q.dot(right) - s0) / (s1 - s0)
        fv = (q.z - z0) / (z1 - z0)
        lay[li].uv = (u0 + fu * (u1 - u0), v0 + fv * (v1 - v0))


# --------------------------------------------------------------------------------------
# Geometry contracts (contract 5)
# --------------------------------------------------------------------------------------

class ContractError(RuntimeError):
    pass


def _face_parts(me):
    arr = np.empty(len(me.polygons), dtype=np.int32)
    me.attributes["part"].data.foreach_get("value", arr)
    return arr


def _co(me):
    co = np.empty(len(me.vertices) * 3, dtype=np.float64)
    me.vertices.foreach_get("co", co)
    return co.reshape(-1, 3)


def _vidx(me, table, fparts, test):
    """Vertex indices used by faces whose part name passes `test`."""
    keep = np.array([test(n) for n in table], dtype=bool)
    sel = keep[fparts - 1]
    idx = set()
    for p_ in np.nonzero(sel)[0]:
        idx.update(me.polygons[int(p_)].vertices)
    return np.array(sorted(idx), dtype=np.int64)


def _pv(me, table, fparts, co, prefix):
    ix = _vidx(me, table, fparts, lambda n: n.startswith(prefix))
    if len(ix) == 0:
        raise ContractError(f"no geometry for part prefix {prefix!r}")
    return co[ix]


def _near(a, b, tol, what):
    if abs(a - b) > tol:
        raise ContractError(f"{what}: {a:.4f} vs {b:.4f} (tol {tol})")


def envelope_boxes(p, inflate=0.0):
    """Every carrier-envelope placement (contract C8): straights every 1.0 m from the
    anchor to the wheel on both lanes, the turn every 10 deg; at today's pin height and,
    when the carrier row has a grip offset, 0.22 higher (dq-chair Q1 / Phase B).
    Gondola draft 1: straights every 0.5 m and the turn every 5 deg, boxes off-centre
    OUTBOARD by their 6th field (the hanger side: +-Y on the straights, radially out in
    the turn), every box grown by `inflate` (C8 runs it at GD_CLEAR)."""
    if not p.gondola:
        dzs = [0.0] + ([float(p.grip_above_rope)] if p.grip_above_rope else [])
        places = []
        n = int(math.floor(p.x_wheel - p.anchor_x)) + 1
        xs = [p.anchor_x + i for i in range(n)] + [p.x_wheel]
        for s in (-1.0, 1.0):
            for x in xs:
                places.append(((x, s * p.lane_y), (1.0, 0.0), (0.0, 1.0)))
        for a in range(-90, 91, 10):
            r = math.radians(a)
            c = (p.x_wheel + p.lane_y * math.cos(r), p.lane_y * math.sin(r))
            places.append((c, (-math.sin(r), math.cos(r)), (math.cos(r), math.sin(r))))
        boxes = []
        for dz in dzs:
            for c, ex, ey in places:
                dzr = rope_z_at(p, c[0]) - p.z_rope if ROPE_ON_CROWN else 0.0     # T27: the sloped chord
                for label, tr, la, z0, z1 in p.env:
                    boxes.append((label, dz, c, ex, ey, tr / 2.0, la / 2.0,
                                  p.lowest_abs + dz + z0 + dzr, p.lowest_abs + dz + z1 + dzr))
        return boxes
    dzs = [0.0] + ([float(p.grip_above_rope)] if p.grip_above_rope else [])
    places = []                     # (centre, travel axis, lateral axis, outboard sign)
    n = int(math.floor((p.x_wheel - p.anchor_x) / 0.5)) + 1
    xs = [p.anchor_x + 0.5 * i for i in range(n)] + [p.x_wheel]
    for s in (-1.0, 1.0):
        for x in xs:
            places.append(((x, s * p.lane_y), (1.0, 0.0), (0.0, 1.0), s))
    for a in range(-90, 91, 5):
        r = math.radians(a)
        c = (p.x_wheel + p.lane_y * math.cos(r), p.lane_y * math.sin(r))
        places.append((c, (-math.sin(r), math.cos(r)), (math.cos(r), math.sin(r)), 1.0))
    z_under = p.z_rope - p.hang
    boxes = []
    for dz in dzs:
        for c, ex, ey, so in places:
            dzr = rope_z_at(p, c[0]) - p.z_rope if ROPE_ON_CROWN else 0.0          # T27: the sloped chord
            for label, tr, la, z0, z1, dy in p.env:
                cc = (c[0] + so * dy * ey[0], c[1] + so * dy * ey[1])
                boxes.append((label, dz, cc, ex, ey, tr / 2.0 + inflate, la / 2.0 + inflate,
                              z_under + dz + z0 - inflate + dzr, z_under + dz + z1 + inflate + dzr))
    return boxes


def _box_tris(c, ex, ey, hx, hy, z0, z1, eps):
    hx, hy = hx - eps, hy - eps
    z0, z1 = z0 + eps, z1 - eps
    corners = []
    for sz in (z0, z1):
        for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
            corners.append((c[0] + sx * hx * ex[0] + sy * hy * ey[0],
                            c[1] + sx * hx * ex[1] + sy * hy * ey[1], sz))
    quads = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    return corners, quads


def check_clearance(t, table, p):
    me = t.data
    fparts = _face_parts(me)
    co = _co(me)
    allowed = np.array([n.startswith(C8_ALLOWED) for n in table], dtype=bool)
    polys = [tuple(pl.vertices) for pl, fp in zip(me.polygons, fparts) if not allowed[fp - 1]]
    pidx = [i for i, fp in enumerate(fparts) if not allowed[fp - 1]]
    bvh = BVHTree.FromPolygons([tuple(v) for v in co], polys, epsilon=0.0)
    vix = _vidx(me, table, fparts, lambda n: not n.startswith(C8_ALLOWED))
    V = co[vix]
    hits = []
    boxes = envelope_boxes(p, inflate=p.clear)        # gondola draft 1: GD_CLEAR all round
    for label, dz, c, ex, ey, hx, hy, z0, z1 in boxes:
        corners, quads = _box_tris(c, ex, ey, hx, hy, z0, z1, 0.002)
        bb = BVHTree.FromPolygons(corners, quads, epsilon=0.0)
        ov = bvh.overlap(bb)
        d = V[:, :2] - np.array(c)
        lx = d @ np.array(ex)
        ly = d @ np.array(ey)
        inside = (np.abs(lx) < hx - 0.002) & (np.abs(ly) < hy - 0.002) & \
                 (V[:, 2] > z0 + 0.002) & (V[:, 2] < z1 - 0.002)
        if ov or inside.any():
            names = sorted({table[fparts[pidx[i]] - 1] for i, _j in ov})
            hits.append((label, round(dz, 3), (round(c[0], 2), round(c[1], 2)), names[:6],
                         int(inside.sum())))
    if hits:
        raise ContractError(f"C8 chair clearance: {len(hits)} envelope placements hit the mesh, "
                            f"first {hits[:4]}")
    return len(boxes)


def assert_revision1(me, table, fparts, co, p):
    """Revision 1 (Richard's verdicts 3-4) in the AUTHORING frame. R1a outer ladder on the
    nose, none in the back half; R1b ONE underside ladder (the front one); R1c the
    gangplank spans the nose_access numbers with its handrail; R1d no railing on the left
    flank (y >= LEFT_RAIL_Y); R1e two sign quads of the sign_layout size, digits >= 0.5 m;
    R1f two windows per side, Collins proportions, on the band's flat face."""
    res = {}
    lay = sign_layout(plate_digits(p))
    # R1e
    if not p.gondola:
        for tag, s in (("n", -1.0), ("p", 1.0)):
            v = _pv(me, table, fparts, co, f"signband_{tag}")
            w_, h_ = float(np.ptp(v[:, 0])), float(np.ptp(v[:, 2]))
            _near(w_, lay["W"], 1e-3, f"R1e signband_{tag} width")
            _near(h_, lay["H"], 1e-3, f"R1e signband_{tag} height")
            if float(v[:, 1].mean()) * s <= 0:
                raise ContractError(f"R1e signband_{tag} on the wrong side")
    else:
        # gondola draft 1: n on the PORTAL face (x = x_nose - 0.012, centred), p / q on the
        # +Y / -Y wall-top bands
        v = _pv(me, table, fparts, co, "signband_n")
        _near(float(np.ptp(v[:, 1])), lay["W"], 1e-3, "R1e signband_n width")
        _near(float(np.ptp(v[:, 2])), lay["H"], 1e-3, "R1e signband_n height")
        _near(float(v[:, 0].mean()), p.x_nose - (0.072 if GD_REV1 else 0.012), 1e-3,
              "R1e signband_n on the portal face")
        _near(float(v[:, 1].mean()), 0.0, 1e-3, "R1e signband_n centred")
        for tag, s in (("p", 1.0), ("q", -1.0)):
            v = _pv(me, table, fparts, co, f"signband_{tag}")
            _near(float(np.ptp(v[:, 0])), lay["W"], 1e-3, f"R1e signband_{tag} width")
            _near(float(np.ptp(v[:, 2])), lay["H"], 1e-3, f"R1e signband_{tag} height")
            if float(v[:, 1].mean()) * s <= 0:
                raise ContractError(f"R1e signband_{tag} on the wrong side")
    if lay["digit_h_m"] < 0.5 - 1e-9:
        raise ContractError(f"R1e digits {lay['digit_h_m']} m < 0.5 m")
    # Revision 1b: the vector glyph itself is >= 0.5 m tall and stays inside its text box
    g = digit_triangles(lay)
    tx0, tx1, ty0, ty1 = lay["text"]
    s0, s1, t0, t1 = g["bbox"]
    if g["h_m"] < 0.5 - 1e-9 or not g["tris"]:
        raise ContractError(f"R1e vector digits '{lay['digits']}' {g['h_m']:.3f} m tall "
                            f"({g['n_tris']} tris) < 0.5 m")
    if s0 < tx0 - 1e-6 or s1 > tx1 + 1e-6 or t0 < ty0 - 1e-6 or t1 > ty1 + 1e-6:
        raise ContractError(f"R1e vector digits bbox s {s0:.3f}..{s1:.3f}, t {t0:.3f}..{t1:.3f} "
                            f"leave the text box {tx0:.3f}..{tx1:.3f} x {ty0:.3f}..{ty1:.3f}")
    res["R1e"] = (f"sign {lay['W']:.3f} x {lay['H']:.3f} m per side, digits '{lay['digits']}' "
                  f"vector {DIGIT_FONT} {g['h_m']:.3f} x {g['w_m']:.3f} m ({g['n_tris']} tris) "
                  f"in the {tx1 - tx0:.2f} x {ty1 - ty0:.2f} m box, reserved "
                  f"{SIGN_PX_PER_M:.0f} px/m at {ATLAS}")
    # R1d
    rails = _vidx(me, table, fparts, lambda n: n.startswith(("rail_", "post_", "kick_")))
    if p.gondola:
        # gondola draft 1: the walled hall's railings (deck, stair) are all INSIDE; R1d (no
        # railing on the exterior left flank) becomes: no rail / post / kick vertex outside
        # the hall's walls
        V = co[rails] if len(rails) else np.zeros((0, 3))
        outside = (V[:, 0] < p.x_nose - 1e-6) | (V[:, 0] > p.x_back + 1e-6) | \
                  (np.abs(V[:, 1]) > p.w_half - WALL_T + 1e-6)
        if outside.any():
            raise ContractError(f"R1d gondola: {int(outside.sum())} railing vertices outside the "
                                f"hall (first {V[outside][0].round(3).tolist()})")
        res["R1d"] = (f"all {len(rails)} rail / post / kick vertices inside the hall walls "
                      f"(no exterior flank railing)")
        return res
    ymin = float(co[rails][:, 1].min()) if len(rails) else 0.0
    if ymin < LEFT_RAIL_Y - 1e-6:
        raise ContractError(f"R1d railing on the left flank: min y {ymin:.3f} < {LEFT_RAIL_Y}")
    res["R1d"] = f"rail / post / kick min y {ymin:.3f} >= {LEFT_RAIL_Y} (left flank clear)"
    (l0, l1, lh), (g0, g1, gh, closed), (lx, ly) = nose_access(p)
    # R1a
    nz = _pv(me, table, fparts, co, "lrail_nz")
    _near(float(nz[:, 0].mean()), lx, 1e-3, "R1a nose ladder x")
    _near(float(nz[:, 1].mean()), ly, 1e-3, "R1a nose ladder y")
    if nz[:, 2].min() > p.z_soffit + 1e-3 or nz[:, 2].max() < p.z_crown + 0.35:
        raise ContractError(f"R1a nose ladder z {nz[:, 2].min():.3f}..{nz[:, 2].max():.3f}, "
                            f"want <= landing {p.z_soffit} .. crown + 0.35 "
                            f"({p.z_crown + 0.35:.3f}); Revision 2: from the ground (R2a)")
    lad = co[_vidx(me, table, fparts, lambda n: n.startswith(("lrail_", "lrung_")))]
    xmid = (p.x_nose + p.x_back) / 2.0
    if lad[:, 0].max() > xmid:
        raise ContractError(f"R1a ladder geometry in the back half: max x {lad[:, 0].max():.3f}")
    res["R1a"] = (f"outer ladder at the nose (x {lx:.2f}, y {ly:.2f}), z {nz[:, 2].min():.2f} -> "
                  f"{nz[:, 2].max():.2f}; no ladder past x {xmid:.1f}")
    # R1b
    tags = sorted({n[len("lrail_"):-1] for n in table if n.startswith("lrail_")})
    under = []
    for tg in tags:
        v = _pv(me, table, fparts, co, f"lrail_{tg}")
        if v[:, 2].max() <= p.z_soffit + 0.01:
            under.append((tg, float(v[:, 0].mean())))
    if len(under) != 1 or under[0][1] > (p.ped_f + p.ped_r) / 2.0:
        raise ContractError(f"R1b underside ladders {under}: want exactly one, the front one")
    res["R1b"] = f"one underside ladder '{under[0][0]}' at x {under[0][1]:.2f} (front pedestal)"
    # R1c
    g = _pv(me, table, fparts, co, "grate_gang")
    _near(float(g[:, 0].min()), g0, 1e-3, "R1c gangplank outer end")
    _near(float(g[:, 0].max()), g1, 1e-3, "R1c gangplank inner end")
    _near(float(np.abs(g[:, 1]).max()), gh, 1e-3, "R1c gangplank half-width")
    _pv(me, table, fparts, co, "rail_gang")
    res["R1c"] = (f"gangplank x {g0:.2f}..{g1:.2f}, |y| <= {gh:.2f}, handrail "
                  f"{'closed' if closed else 'open'} at the outer end; landing x {l0:.2f}..{l1:.2f}, "
                  f"|y| <= {lh:.2f}")
    # R1f
    yp = p.w_half + 0.015
    for tag, s in (("n", -1.0), ("p", 1.0)):
        for k, (xc, zc) in enumerate(window_centres(p)):
            v = _pv(me, table, fparts, co, f"hsgwin_{tag}{k}")
            _near(float(np.ptp(v[:, 0])), WIN_W, 1e-3, f"R1f hsgwin_{tag}{k} width")
            _near(float(np.ptp(v[:, 2])), WIN_H, 1e-3, f"R1f hsgwin_{tag}{k} height")
            _near(float(v[:, 1].mean()), s * yp, 1e-3, f"R1f hsgwin_{tag}{k} plane")
            if v[:, 2].min() < p.z_soffit + FILLET_R or v[:, 2].max() > p.z_top - 0.15:
                raise ContractError(f"R1f hsgwin_{tag}{k} z {v[:, 2].min():.2f}..{v[:, 2].max():.2f}"
                                    f" off the band's flat face")
    n_win = sum(1 for n in table if n.startswith("hsgwin_"))
    if n_win != 4:
        raise ContractError(f"R1f {n_win} housing windows, want 2 per side")
    res["R1f"] = (f"2 windows per side {WIN_W} x {WIN_H} m (ratio {WIN_W / WIN_H:.2f}), centres "
                  f"x {[round(c[0], 2) for c in window_centres(p)]}, z {sign_z_centre(p):.3f}")
    return res


def _part_bvh(me, table, fparts, co, test):
    polys = [tuple(pl.vertices) for pl, fp in zip(me.polygons, fparts) if test(table[fp - 1])]
    if not polys:
        return None, 0
    return BVHTree.FromPolygons([tuple(v) for v in co], polys, epsilon=0.0), len(polys)


def _keepouts(p, with_subobjects=True):
    """Revision 1c: axis-aligned obstruction boxes the station mesh does not carry - the hosted
    ETDQ's deck strips + rails (base, both sides) and the AABB of every C8 chair-envelope
    placement (both pin heights) - as [(label, lo, hi)] in the authoring frame. Gondola revision 1
    (with_subobjects): the vanilla sub-object footprints (variant-union bounds) too, so the nose
    ladder's climbing room counts the lamp posts as fixed obstructions."""
    out = []
    if p.gondola and GD_REV1 and with_subobjects:
        LD = _LD()
        for pfab, pos, facing, _mount in gd_subobjects(p):
            rot = LD.facing_quat(facing, frame="blender") if facing else (0.0, 0.0, 0.0, 1.0)
            lo, hi = LD.subobj_box(pfab, LD.blender_to_game(pos), rot, frame="blender")
            out.append((f"vanilla {pfab}", np.array(lo, dtype=np.float64), np.array(hi, dtype=np.float64)))
    if FINE_DETAIL and with_subobjects:
        # fine-detail step 3b: the dressing-kit rows (the real kit mesh's AABB) are fixed obstructions too - for
        # the climbing rooms, D8's dressing / vanilla footprints (DQ) and the gondola ladder zone
        out += [(lab_, np.array(lo, dtype=np.float64), np.array(hi, dtype=np.float64))
                for lab_, lo, hi in fd_kit_boxes(p)]
    if p.hosted_tower and not p.gondola:
        # detail pass 1 (MC4): the ETDQ keep-out as its two real parts - the deck strips (a
        # walking surface, top 3.404) and the outer deck rails (|y| 2.05, r 0.028, 1.10 high) -
        # so a zone above the deck (the front ladder from the landing) is not hit by the deck's
        # empty air; every zone that touched the old box touches one of these (lift_tower
        # DQ_DECK_Y0 / Y1 / DQ_RAIL_Y)
        k = ETDQ_KEEPOUT
        for lab_, yy, zz in (("hosted ETDQ deck", k["y"], (k["z"][0], 3.41)),
                             ("hosted ETDQ rail", (1.95, k["y"][1]), (3.40, k["z"][1]))):
            for s in (-1.0, 1.0):
                ys = sorted((s * yy[0], s * yy[1]))
                out.append((lab_, np.array([k["x"][0], ys[0], zz[0]]),
                            np.array([k["x"][1], ys[1], zz[1]])))
    if p.hosted_tower and p.gondola:
        # the (unbuilt) gondola entry compression tower's provisional envelope (G6)
        k = GD_TOWER_KEEPOUT
        out.append(("hosted gondola entry tower (provisional)",
                    np.array([p.anchor_x - 3.0, -k["y"], 0.0]),
                    np.array([p.x_nose - k["dx"], k["y"], k["z"]])))
    for label, dz, c, ex, ey, hx, hy, z0, z1 in envelope_boxes(p):
        cs = [(c[0] + sx * hx * ex[0] + sy * hy * ey[0], c[1] + sx * hx * ex[1] + sy * hy * ey[1])
              for sx in (-1, 1) for sy in (-1, 1)]
        a = np.array(cs)
        out.append((f"C8 {label}", np.array([a[:, 0].min(), a[:, 1].min(), z0]),
                    np.array([a[:, 0].max(), a[:, 1].max(), z1])))
    return out


def climb_room(me, table, fparts, co, p, own, rung, d, half, z0, z1, depth):
    """Revision 1c: the climbing room of one ladder flight (AUTHORING frame). `rung` = (x, y)
    of the rung centreline's mid point, `d` = the climbing direction (+-X or +-Y, unit), `half`
    = half the width across the rails (outer faces), [z0, z1] = the climbed heights, `own` =
    the flight's part prefixes (ignored). Returns (hits, room): `hits` = what stands inside the
    zone `depth` deep on the climbing side (station parts by face overlap or vertex, the hosted
    ETDQ / chair-envelope keep-outs); `room` = {"fixed": (m, owner), "chairs": (m, owner)}, the
    smallest distance from the rung line along `d` to the station mesh or the hosted ETDQ, and
    to the C8 chair envelope (rays every 0.05 m up, 7 across; capped at 1.5)."""
    from mathutils.bvhtree import BVHTree
    dx, dy = d
    ax = np.array([abs(dy), abs(dx)])                # across the rails
    polys, names = [], []
    for pl, fp in zip(me.polygons, fparts):
        nm = table[fp - 1]
        if not nm.startswith(own):
            polys.append(tuple(pl.vertices))
            names.append(nm)
    bvh = BVHTree.FromPolygons([tuple(v) for v in co], polys, epsilon=0.0)
    eps = 0.002
    c0 = np.array(rung, dtype=np.float64)
    p_lo = c0 + np.minimum(0.0, np.array(d) * depth) - ax * half
    p_hi = c0 + np.maximum(0.0, np.array(d) * depth) + ax * half
    lo = np.array([p_lo[0], p_lo[1], z0]) + eps
    hi = np.array([p_hi[0], p_hi[1], z1]) - eps
    corners = [(x, y, z) for z in (lo[2], hi[2]) for x, y in ((lo[0], lo[1]), (hi[0], lo[1]),
                                                                (hi[0], hi[1]), (lo[0], hi[1]))]
    quads = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    zone = BVHTree.FromPolygons(corners, quads, epsilon=0.0)
    hits = sorted({names[i] for i, _j in bvh.overlap(zone)})
    used = sorted({v for pl in polys for v in pl})
    V = co[used]
    inside = (V > lo).all(axis=1) & (V < hi).all(axis=1)
    if inside.any():
        vix = set(np.array(used)[inside].tolist())
        hits = sorted(set(hits) | {names[i] for i, pl in enumerate(polys) if vix & set(pl)})
    kos = _keepouts(p)
    for label, klo, khi in kos:
        if (klo < hi).all() and (khi > lo).all():
            hits.append(label)
    # the profile: rays from the rung line along d; fixed objects (station mesh, the hosted
    # ETDQ) and the passing chairs (C8 envelope) are reported apart
    room = {"fixed": (1.5, None), "chairs": (1.5, None)}

    def near(cat, dist, who):
        if dist < room[cat][0]:
            room[cat] = (dist, who)
    dv = Vector((dx, dy, 0.0))
    for u in np.linspace(-half, half, 7):
        for z in np.arange(z0, z1 + 1e-9, 0.05):
            o = Vector((c0[0] + ax[0] * u, c0[1] + ax[1] * u, float(z)))
            loc, _n, idx, dist = bvh.ray_cast(o, dv, 1.5)
            if loc is not None:
                near("fixed", dist, names[idx])
            for label, klo, khi in kos:
                if not (klo[2] <= z <= khi[2]):
                    continue
                i, j = (0, 1) if dx else (1, 0)          # i = along the ray, j = across it
                s = dx if dx else dy
                if not (klo[j] <= o[j] <= khi[j]):
                    continue
                t0 = ((klo[i] if s > 0 else khi[i]) - o[i]) / s
                t1 = ((khi[i] if s > 0 else klo[i]) - o[i]) / s
                if t1 >= 0.0:
                    near("chairs" if label.startswith("C8") else "fixed", max(t0, 0.0), label)
    return hits, {k: (round(float(v[0]), 3), v[1]) for k, v in room.items()}


def assert_revision2(t, me, table, fparts, co, p):
    """Revision 2 (Richard's second look 3', 2026-10-11) in the AUTHORING frame: the front
    (nose) ladder reaches the GROUND. Returns ({label: message}, numbers for the manifest).
    R2a the footing lfoot_nz is an open-bottom ground part (min z exactly 0, no face inside
         the ground band, its bottom edges OK engine-base edges, nothing below 0) and both
         rails stand on it, from its top to the crown + 0.35;
    R2b the rungs run on ONE 0.30 grid from <= 0.40 above grade to the top, one flush with
         the landing top (the step-off rung);
    R2c the ladder passes the landing through an OPEN ladder-way: no ladder face meets a
         landing grating, a 5 x 5 probe of the opening hits no grating, every landing /
         gangplank handrail post stands on grating; the climbing room in front of the rungs
         (to the opening's edge and to the nearest handrail) is reported;
    R2d no cage, and (base) every Revision 2 vertex stays inboard of the hosted ETDQ
         (x > ETDQ_INBOARD_X; seam S8 in tests/test_dq_family_seams.py is the geometry gate)."""
    if p.gondola:
        return {}, None
    res = {}
    lay = nose_ladder_layout(p)
    if lay["ground"] is not None:
        return assert_revision1c(t, me, table, fparts, co, p, lay)
    lx, ly, zs = lay["x"], lay["y"], p.z_soffit
    fx0, fx1, fy0, fy1, _fz0, fz1 = lay["foot"]
    band = cs2_validate.GROUND_BAND_M
    # R2a: the footing
    if "lfoot_nz" not in table:
        raise ContractError("R2a no footing lfoot_nz: the nose ladder does not reach the ground")
    fid = table.index("lfoot_nz") + 1
    f = _pv(me, table, fparts, co, "lfoot_nz")
    for a, b, k in ((f[:, 0].min(), fx0, "x0"), (f[:, 0].max(), fx1, "x1"), (f[:, 1].min(), fy0, "y0"),
                    (f[:, 1].max(), fy1, "y1"), (f[:, 2].max(), fz1, "top")):
        _near(float(a), b, 1e-4, f"R2a footing {k}")
    if abs(float(f[:, 2].min())) > 1e-6:
        raise ContractError(f"R2a footing bottom z {f[:, 2].min():.6f}: its edges must sit exactly "
                            f"at z 0")
    flat = [i for i, (pl, fp) in enumerate(zip(me.polygons, fparts))
            if fp == fid and max(co[v][2] for v in pl.vertices) <= band]
    if flat:
        raise ContractError(f"R2a footing has {len(flat)} face(s) inside the ground band (a bottom "
                            f"face blocks the engine base): it must be open-bottom")
    gr = cs2_validate.ground_ring_report(t)
    n_ok = int(gr["ok_parts"].get("lfoot_nz", 0))
    if n_ok < 4 or "lfoot_nz" in gr["offending_parts"] or gr["below_verts"]:
        raise ContractError(f"R2a footing engine base: {n_ok} OK edges, offending "
                            f"{gr['offending_parts']}, {gr['below_verts']} verts below")
    lad = co[_vidx(me, table, fparts, lambda n: n.startswith(("lrail_nz", "lrung_nz", "lfoot_nz",
                                                              "lstand_nzl")))]
    if lad[:, 2].min() < -1e-6:
        raise ContractError(f"R2a nose ladder below grade: min z {lad[:, 2].min():.4f}")
    rails = []
    for k in (0, 1):
        v = _pv(me, table, fparts, co, f"lrail_nz{k}")
        z0, z1 = float(v[:, 2].min()), float(v[:, 2].max())
        _near(z0, lay["z_foot"], 1e-4, f"R2a rail lrail_nz{k} foot (footing top)")
        if z1 < p.z_crown + 0.35:
            raise ContractError(f"R2a rail lrail_nz{k} top {z1:.3f} < crown + 0.35")
        if v[:, 0].min() < fx0 or v[:, 0].max() > fx1 or v[:, 1].min() < fy0 or v[:, 1].max() > fy1:
            raise ContractError(f"R2a rail lrail_nz{k} foot leaves the footing")
        rails.append((round(z0, 3), round(z1, 3)))
    res["R2a"] = (f"front ladder from the ground: footing lfoot_nz x {fx0:.2f}..{fx1:.2f}, y "
                  f"{fy0:.2f}..{fy1:.2f}, z 0 -> {fz1:.2f} (open bottom, {n_ok} OK engine-base "
                  f"edges, 0 below), rails z {rails[0][0]:.2f} -> {rails[0][1]:.2f}")
    # R2b: one rung grid
    rz = []
    for n in table:
        if n.startswith("lrung_nz"):
            rz.append(float(_pv(me, table, fparts, co, n)[:, 2].mean()))
    rz.sort()
    d = np.diff(rz)
    if len(rz) != len(lay["rungs"]) or not len(d) or np.abs(d - NZ_PITCH).max() > 1e-3:
        raise ContractError(f"R2b nose rungs {len(rz)} (want {len(lay['rungs'])}), pitch "
                            f"{d.min() if len(d) else 0:.3f}..{d.max() if len(d) else 0:.3f} "
                            f"(want {NZ_PITCH} throughout)")
    if rz[0] > 0.40 + 1e-6 or rz[0] < lay["z_foot"] + 0.15:
        raise ContractError(f"R2b lowest rung {rz[0]:.3f} above grade (want footing + 0.15 .. 0.40)")
    if min(abs(z - zs) for z in rz) > 1e-3:
        raise ContractError(f"R2b no rung flush with the landing top {zs}")
    if rz[-1] > lay["z_top"] - 0.15 + 1e-6 or rz[-1] < lay["z_top"] - 0.15 - NZ_PITCH:
        raise ContractError(f"R2b top rung {rz[-1]:.3f} vs rail top {lay['z_top']:.3f}")
    n_below = sum(1 for z in rz if z < zs - 1e-3)
    res["R2b"] = (f"{len(rz)} rungs on one {NZ_PITCH:.2f} grid, {rz[0]:.2f} -> {rz[-1]:.2f} "
                  f"({n_below} below the landing, one flush with it at {zs:.2f})")
    # R2c: the ladder-way
    wx0, wx1, wy0, wy1 = lay["well"]
    land = lambda n: n.startswith("grate_balc")   # noqa: E731
    gbvh, _n = _part_bvh(me, table, fparts, co, land)
    lbvh, _n = _part_bvh(me, table, fparts, co, lambda n: n.startswith(("lrail_nz", "lrung_nz")))
    if gbvh is None or lbvh is None:
        raise ContractError("R2c no landing grating / nose ladder geometry")
    hits = lbvh.overlap(gbvh)
    if hits:
        raise ContractError(f"R2c the nose ladder pierces the landing grating ({len(hits)} face "
                            f"pairs): the ladder-way is closed")
    probe = 0
    for fu in np.linspace(0.1, 0.9, 5):
        for fv in np.linspace(0.1, 0.9, 5):
            x, y = wx0 + fu * (wx1 - wx0), wy0 + fv * (wy1 - wy0)
            loc, _n, _i, _d = gbvh.ray_cast(Vector((x, y, zs + 0.5)), Vector((0.0, 0.0, -1.0)), 1.0)
            if loc is not None:
                probe += 1
    if probe:
        raise ContractError(f"R2c ladder-way x {wx0:.2f}..{wx1:.2f}, y {wy0:.2f}..{wy1:.2f}: "
                            f"{probe} of 25 probes hit a landing grating (closed)")
    abvh, _n = _part_bvh(me, table, fparts, co, lambda n: n.startswith("grate_"))
    floating = []
    n_posts = 0
    for n in table:
        if not n.startswith("post_gang"):
            continue
        v = _pv(me, table, fparts, co, n)
        if abs(float(v[:, 2].min()) - zs) > 0.01:
            continue
        n_posts += 1
        c = (float(v[:, 0].mean()), float(v[:, 1].mean()))
        loc, _n, _i, _d = abvh.ray_cast(Vector((c[0], c[1], zs + 0.01)), Vector((0.0, 0.0, -1.0)),
                                        0.1)
        if loc is None:
            floating.append((n, round(c[0], 3), round(c[1], 3)))
    if floating or not n_posts:
        raise ContractError(f"R2c handrail posts off the grating (over the ladder-way?): "
                            f"{floating[:4]} of {n_posts}")
    hv = co[_vidx(me, table, fparts, lambda n: n.startswith(("rail_gang", "post_gang")))]
    zone = hv[(hv[:, 0] < lx) & (np.abs(hv[:, 1] - ly) <= 0.30) & (hv[:, 2] >= zs - 1e-3)
              & (hv[:, 2] <= zs + 1.2)]
    to_rail = float(lx - zone[:, 0].max()) if len(zone) else None
    to_edge = float(lx - wx0)
    res["R2c"] = (f"ladder-way x {wx0:.2f}..{wx1:.2f}, y {wy0:.2f}..{wy1:.2f} open (0 ladder x "
                  f"grating pairs, 0 / 25 probes hit), {n_posts} handrail posts on grating; "
                  f"climbing room in front of the rungs {to_edge:.2f} m to the opening edge, "
                  + (f"{to_rail:.2f} m to the handrail" if to_rail is not None else "no handrail")
                  + " in the zone")
    # Revision 1c: the climbing room is GATED (it was only reported): nothing within the floor
    # in front of the rungs over the whole flight (base CLIMB_MIN - the straight run fails it;
    # top CLIMB_TOP_FLOOR, its shipped ladder-way). The message above stays the shipped one.
    floor = CLIMB_MIN if p.hosted_tower else CLIMB_TOP_FLOOR
    hits, room = climb_room(me, table, fparts, co, p, ("lrail_nz", "lrung_nz", "lfoot_nz", "lstand_nz"),
                            (lx, ly), (-1.0, 0.0), 0.31, lay["z_foot"], lay["z_top"], floor)
    if hits:
        raise ContractError(f"R2c climbing room in front of the nose rungs < {floor} m over the "
                            f"flight z {lay['z_foot']:.2f}..{lay['z_top']:.2f}: {hits[:6]} (nearest "
                            f"fixed {room['fixed']}, chairs {room['chairs']}; 29 CFR 1910.23(d) 0.61 m "
                            f"at an obstruction)")
    print(f"[contract] R2c climbing room (gate >= {floor} m): fixed {room['fixed']}, chairs "
          f"{room['chairs']}")
    # R2d: no cage; inboard of the hosted ETDQ
    cage = sorted(n for n in table if n.startswith(("lcage", "cage_", "hoop_")))
    if cage:
        raise ContractError(f"R2d a ladder cage {cage[:3]}: none of the reference ladders has one")
    r2v = co[_vidx(me, table, fparts, lambda n: n.startswith(("lfoot_", "lrail_nz", "lrung_nz",
                                                              "lstand_nzl", "grate_balc")))]
    xmin = float(r2v[:, 0].min())
    if p.hosted_tower and xmin <= ETDQ_INBOARD_X + 0.02:
        raise ContractError(f"R2d Revision 2 geometry at x {xmin:.3f} reaches the hosted ETDQ "
                            f"(inboard end {ETDQ_INBOARD_X})")
    res["R2d"] = ("no cage (Collins: none); " +
                  (f"min x {xmin:.3f} > hosted ETDQ inboard end {ETDQ_INBOARD_X} "
                   f"(+{xmin - ETDQ_INBOARD_X:.3f})" if p.hosted_tower else
                   f"min x {xmin:.3f} (no hosted tower)"))
    numbers = {
        "revision": "2 (second look 3', 2026-10-11): front ladder to the ground",
        "x": round(lx, 4), "y": round(ly, 4), "rails_z": [rails[0][0], rails[0][1]],
        "footing": {"part": "lfoot_nz", "x": [round(fx0, 4), round(fx1, 4)],
                    "y": [round(fy0, 4), round(fy1, 4)], "z": [0.0, round(fz1, 4)],
                    "open_bottom": True, "engine_base_ok_edges": n_ok},
        "rungs": {"n": len(rz), "pitch_m": NZ_PITCH, "lowest_m": round(rz[0], 4),
                  "top_m": round(rz[-1], 4), "below_landing": n_below,
                  "flush_with_landing_m": round(zs, 4)},
        "ladder_way": {"x": [round(wx0, 4), round(wx1, 4)], "y": [round(wy0, 4), round(wy1, 4)],
                       "room_to_edge_m": round(to_edge, 3),
                       "room_to_handrail_m": round(to_rail, 3) if to_rail is not None else None},
        "cage": False,
        "min_x": round(xmin, 4),
        "hosted_etdq_inboard_x": ETDQ_INBOARD_X if p.hosted_tower else None,
    }
    return res, numbers


def assert_revision1c(t, me, table, fparts, co, p, lay):
    """Revision 1c (revision2-check-dq-stations item 6) on the BASE, AUTHORING frame: R2a-R2d
    re-pointed at the ground flight beside the landing (ground_flight). Returns
    ({label: message}, numbers for the manifest key front_ladder).
    R2a the footing lfoot_gf is an open-bottom ground part and both rails lrail_gf* stand on
         it, up to the landing + GF_EXT; the front ladder (lrail_nz*) stands on the landing
         (feet on grating) up to the crown + 0.35; there is no lfoot_nz;
    R2b the ground-flight rungs run on the landing's 0.30 grid from <= 0.40 above grade to
         the step-off rung flush with the landing, none above it (step-through); the front
         ladder's rungs are all above the landing;
    R2c the ground flight touches no landing grating; the step-across (rung line -> the
         landing's +Y edge) is GF_STEP; every handrail post stands on grating; nothing - station
         mesh, the hosted ETDQ, the C8 chair envelope - stands within CLIMB_MIN of the rung line
         on the climbing side over the whole flight (footing top -> step-off rung + CLIMB_HEAD);
         the front ladder's mounting room from the landing is reported (Revision 1, unchanged);
    R2d no cage; every Revision 1c / 2 vertex inboard of the hosted ETDQ (x > ETDQ_INBOARD_X +
         0.02; seam S8 is the geometry gate)."""
    res = {}
    gf = lay["ground"]
    zs = p.z_soffit
    lx, ly = lay["x"], lay["y"]
    (x0, x1), y = gf["x"], gf["y"]
    fx0, fx1, fy0, fy1, _fz0, fz1 = gf["foot"]
    band = cs2_validate.GROUND_BAND_M
    # R2a
    if "lfoot_gf" not in table:
        raise ContractError("R2a no footing lfoot_gf: the ground flight does not reach the ground")
    if "lfoot_nz" in table or any(n.startswith("lstand_nzl") for n in table):
        raise ContractError("R2a the front ladder still runs to the ground (lfoot_nz / lstand_nzl)")
    fid = table.index("lfoot_gf") + 1
    f = _pv(me, table, fparts, co, "lfoot_gf")
    for a, b, k in ((f[:, 0].min(), fx0, "x0"), (f[:, 0].max(), fx1, "x1"), (f[:, 1].min(), fy0, "y0"),
                    (f[:, 1].max(), fy1, "y1"), (f[:, 2].max(), fz1, "top")):
        _near(float(a), b, 1e-4, f"R2a footing {k}")
    if abs(float(f[:, 2].min())) > 1e-6:
        raise ContractError(f"R2a footing bottom z {f[:, 2].min():.6f}: its edges must sit exactly at z 0")
    flat = [i for i, (pl, fp) in enumerate(zip(me.polygons, fparts))
            if fp == fid and max(co[v][2] for v in pl.vertices) <= band]
    if flat:
        raise ContractError(f"R2a footing has {len(flat)} face(s) inside the ground band: it must be "
                            f"open-bottom")
    gr = cs2_validate.ground_ring_report(t)
    n_ok = int(gr["ok_parts"].get("lfoot_gf", 0))
    if n_ok < 4 or "lfoot_gf" in gr["offending_parts"] or gr["below_verts"]:
        raise ContractError(f"R2a footing engine base: {n_ok} OK edges, offending "
                            f"{gr['offending_parts']}, {gr['below_verts']} verts below")
    rails = []
    for k in (0, 1):
        v = _pv(me, table, fparts, co, f"lrail_gf{k}")
        z0, z1 = float(v[:, 2].min()), float(v[:, 2].max())
        _near(z0, gf["z_foot"], 1e-4, f"R2a rail lrail_gf{k} foot (footing top)")
        if z1 < zs + GF_EXT - 1e-3:
            raise ContractError(f"R2a rail lrail_gf{k} top {z1:.3f} < landing + {GF_EXT}")
        if v[:, 0].min() < fx0 or v[:, 0].max() > fx1 or v[:, 1].min() < fy0 or v[:, 1].max() > fy1:
            raise ContractError(f"R2a rail lrail_gf{k} foot leaves the footing")
        rails.append((round(z0, 3), round(z1, 3)))
    abvh, _n = _part_bvh(me, table, fparts, co, lambda n: n.startswith("grate_"))
    nz_rails = []
    for k in (0, 1):
        v = _pv(me, table, fparts, co, f"lrail_nz{k}")
        z0, z1 = float(v[:, 2].min()), float(v[:, 2].max())
        c = (float(v[:, 0].mean()), float(v[:, 1].mean()))
        loc, _nn, _i, _d = abvh.ray_cast(Vector((c[0], c[1], zs + 0.01)), Vector((0.0, 0.0, -1.0)), 0.05)
        if abs(z0 - zs) > 1e-3 or loc is None or z1 < p.z_crown + 0.35:
            raise ContractError(f"R2a front ladder rail lrail_nz{k} z {z0:.3f}..{z1:.3f}: want on the "
                                f"landing grating at {zs} up to the crown + 0.35")
        nz_rails.append((round(z0, 3), round(z1, 3)))
    res["R2a"] = (f"ground flight beside the landing: footing lfoot_gf x {fx0:.2f}..{fx1:.2f}, y "
                  f"{fy0:.2f}..{fy1:.2f}, z 0 -> {fz1:.2f} (open bottom, {n_ok} OK engine-base "
                  f"edges, 0 below), rails x {x0:.2f} / {x1:.2f} at y {y:.2f}, z {rails[0][0]:.2f} -> "
                  f"{rails[0][1]:.2f}; front ladder on the landing, z {nz_rails[0][0]:.2f} -> "
                  f"{nz_rails[0][1]:.2f}")
    # R2b
    rz = sorted(float(_pv(me, table, fparts, co, n)[:, 2].mean()) for n in table
                if n.startswith("lrung_gf"))
    d = np.diff(rz)
    if len(rz) != len(gf["rungs"]) or not len(d) or np.abs(d - NZ_PITCH).max() > 1e-3:
        raise ContractError(f"R2b ground-flight rungs {len(rz)} (want {len(gf['rungs'])}) off the "
                            f"{NZ_PITCH} grid")
    if rz[0] > 0.40 + 1e-6 or rz[0] < gf["z_foot"] + 0.15:
        raise ContractError(f"R2b lowest rung {rz[0]:.3f} above grade (want footing + 0.15 .. 0.40)")
    if abs(rz[-1] - zs) > 1e-3:
        raise ContractError(f"R2b the step-off rung {rz[-1]:.3f} is not flush with the landing {zs}")
    nzr = [float(_pv(me, table, fparts, co, n)[:, 2].mean()) for n in table if n.startswith("lrung_nz")]
    if len(nzr) != len(lay["rungs"]) or min(nzr) <= zs + 1e-3:
        raise ContractError(f"R2b front ladder rungs {len(nzr)} (want {len(lay['rungs'])}), lowest "
                            f"{min(nzr):.3f} must be above the landing")
    res["R2b"] = (f"{len(rz)} ground-flight rungs on the landing's {NZ_PITCH:.2f} grid, {rz[0]:.2f} -> "
                  f"{rz[-1]:.2f} (the step-off rung flush with the landing, none above it); front "
                  f"ladder {len(nzr)} rungs {min(nzr):.2f} -> {max(nzr):.2f}")
    # R2c
    land = lambda n: n.startswith("grate_balc")   # noqa: E731
    gbvh, _n = _part_bvh(me, table, fparts, co, land)
    lbvh, _n = _part_bvh(me, table, fparts, co, lambda n: n.startswith(("lrail_gf", "lrung_gf")))
    if gbvh is None or lbvh is None:
        raise ContractError("R2c no landing grating / ground-flight geometry")
    hits = lbvh.overlap(gbvh)
    if hits:
        raise ContractError(f"R2c the ground flight pierces the landing grating ({len(hits)} face pairs)")
    edge = float(co[_vidx(me, table, fparts, land)][:, 1].max())
    step = y - edge
    if abs(step - GF_STEP) > 1e-3 or not (0.18 - 1e-3 <= step <= 0.30 + 1e-3):
        raise ContractError(f"R2c step-across {step:.3f} m (rung line y {y:.3f} -> landing edge "
                            f"{edge:.3f}; want {GF_STEP}, 7-12 in)")
    floating, n_posts = [], 0
    for n in table:
        if not n.startswith("post_gang"):
            continue
        v = _pv(me, table, fparts, co, n)
        if abs(float(v[:, 2].min()) - zs) > 0.01:
            continue
        n_posts += 1
        c = (float(v[:, 0].mean()), float(v[:, 1].mean()))
        loc, _nn, _i, _d = abvh.ray_cast(Vector((c[0], c[1], zs + 0.01)), Vector((0.0, 0.0, -1.0)), 0.1)
        if loc is None:
            floating.append((n, round(c[0], 3), round(c[1], 3)))
    if floating or not n_posts:
        raise ContractError(f"R2c handrail posts off the grating: {floating[:4]} of {n_posts}")
    zc1 = rz[-1] + CLIMB_HEAD
    hits, room = climb_room(me, table, fparts, co, p, ("lrail_gf", "lrung_gf", "lfoot_gf", "lstand_gf"),
                            ((x0 + x1) / 2.0, y), gf["dir"], gf["half"] + 0.03, gf["z_foot"], zc1,
                            CLIMB_MIN)
    if hits or room["fixed"][0] < CLIMB_MIN or room["chairs"][0] < CLIMB_MIN:
        raise ContractError(f"R2c climbing room of the ground flight < {CLIMB_MIN} m over z "
                            f"{gf['z_foot']:.2f}..{zc1:.2f}: {hits[:6]} (nearest fixed {room['fixed']}, "
                            f"chairs {room['chairs']})")
    _h, front = climb_room(me, table, fparts, co, p, ("lrail_nz", "lrung_nz", "lstand_nz"), (lx, ly),
                           (-1.0, 0.0), 0.31, zs + 0.01, zs + CLIMB_HEAD, 0.01)
    res["R2c"] = (f"ground flight: climbing room (+Y, footing -> step-off rung + {CLIMB_HEAD}) "
                  f"{room['fixed'][0]:.2f} m to fixed objects, {room['chairs'][0]:.2f} m to the C8 chair "
                  f"envelope (gate {CLIMB_MIN}); step-across {step:.2f} m onto the landing between the "
                  f"rails (0 ladder x grating pairs); {n_posts} handrail posts on grating; front ladder "
                  f"mounted from the landing: {front['fixed'][0]:.2f} m to {front['fixed'][1]} "
                  f"(Revision 1, reported)")
    # R2d
    cage = sorted(n for n in table if n.startswith(("lcage", "cage_", "hoop_")))
    if cage:
        raise ContractError(f"R2d a ladder cage {cage[:3]}: none of the reference ladders has one")
    r2v = co[_vidx(me, table, fparts, lambda n: n.startswith(("lfoot_", "lrail_gf", "lrung_gf", "lstand_gf",
                                                              "lrail_nz", "lrung_nz", "grate_balc")))]
    xmin = float(r2v[:, 0].min())
    if xmin <= ETDQ_INBOARD_X + 0.02:
        raise ContractError(f"R2d Revision 1c geometry at x {xmin:.3f} reaches the hosted ETDQ "
                            f"(inboard end {ETDQ_INBOARD_X})")
    res["R2d"] = (f"no cage (Collins: none); min x {xmin:.3f} > hosted ETDQ inboard end "
                  f"{ETDQ_INBOARD_X} (+{xmin - ETDQ_INBOARD_X:.3f})")
    numbers = {
        "revision": "1c (2026-10-11): ground flight beside the landing (+Y edge, a through ladder); "
                    "front ladder from the landing (Revision 1)",
        "x": round(lx, 4), "y": round(ly, 4), "rails_z": [nz_rails[0][0], nz_rails[0][1]],
        "ground_flight": {
            "x": [round(x0, 4), round(x1, 4)], "y": round(y, 4), "climb_dir": "+y",
            "rails_z": [rails[0][0], rails[0][1]],
            "footing": {"part": "lfoot_gf", "x": [round(fx0, 4), round(fx1, 4)],
                        "y": [round(fy0, 4), round(fy1, 4)], "z": [0.0, round(fz1, 4)],
                        "open_bottom": True, "engine_base_ok_edges": n_ok},
            "rungs": {"n": len(rz), "pitch_m": NZ_PITCH, "lowest_m": round(rz[0], 4),
                      "top_m": round(rz[-1], 4), "flush_with_landing_m": round(zs, 4)},
            "landing_edge_y": round(edge, 4), "step_across_m": round(step, 3),
            "climb_room_m": {"fixed": room["fixed"][0], "chairs": room["chairs"][0]},
            "climb_min_m": CLIMB_MIN},
        "front_from_landing_room_m": front["fixed"][0],
        "cage": False,
        "min_x": round(xmin, 4),
        "hosted_etdq_inboard_x": ETDQ_INBOARD_X,
    }
    return res, numbers


# --------------------------------------------------------------------------------------
# Detail pass 1 (DQ): one reachability rule (MC3), the base roof route (MC4), the pf ladders
# off the spine (MC5), every board slot / sub-object footprint box-tested (MC10)
# --------------------------------------------------------------------------------------

def _room_rule(me, table, fparts, co, p, label, own, rung, d, z0, z1, width=CLIMB_WIDTH):
    """MC3: the house numbers through climb_room - a zone `width` wide (>= CLIMB_WIDTH) across the
    climbing direction d, CLIMB_MIN deep, from z0 to z1, clear of the station mesh, the hosted ETDQ
    and every C8 chair-envelope box (both pin heights); raises ContractError("<label> ...")."""
    hits, room = climb_room(me, table, fparts, co, p, own, rung, d, width / 2.0, z0, z1, CLIMB_MIN)
    if hits or room["fixed"][0] < CLIMB_MIN - 1e-9 or room["chairs"][0] < CLIMB_MIN - 1e-9:
        raise ContractError(f"{label} climbing room < {CLIMB_MIN} m (corridor {width:.2f} m) over z "
                            f"{z0:.2f}..{z1:.2f}: {hits[:6]} (nearest fixed {room['fixed']}, chairs "
                            f"{room['chairs']})")
    return room


def assert_access_d1(t, me, table, fparts, co, p):
    """Detail pass 1 access (AUTHORING frame; raise -> nothing ships). ONE rule (MC3, the house
    numbers C15 / 1c share): every DQ ladder flight keeps a corridor >= CLIMB_WIDTH wide and
    CLIMB_MIN deep on its climbing side, from its lowest rung to its last stop + CLIMB_TERMINUS_HEAD,
    clear of the station mesh, the hosted ETDQ and the C8 chair envelopes.
    R2e roof route: base (MC4) the front ladder from the landing, its bottom-stop standing room
         (the landing + the ETDQ deck strip, nothing standing on it in the zone: the opened rail
         gap), the zone to the crown + 1.80; top the straight run through its 0.62 ladder-way;
    R2f pedestal ladder (MC5): off spine_main (no face pair, no rung in the spine box), the hatch
         opening cut through the soffit (rays up meet the leaf), a rung within 0.05 of the stop,
         rails ending under the leaf, the corridor through the opening to the stop + 1.80, >= 0.10
         to beam_*t / crosshead_* / the soffit RoofLight footprints;
    R2g base ground flight (1c): the same corridor rule at 0.61 wide - the corridor is the 0.612 m
         between the hosted ETDQ's inboard end and the band face.
    Returns ({label: message}, numbers)."""
    res, nums = {}, {}
    zs = p.z_soffit
    lay = nose_ladder_layout(p)
    lx, ly = lay["x"], lay["y"]
    nz = sorted(float(_pv(me, table, fparts, co, n)[:, 2].mean()) for n in table
                if n.startswith("lrung_nz"))
    term = p.z_crown + CLIMB_TERMINUS_HEAD
    own_nz = ("lrail_nz", "lrung_nz", "lstand_nz", "lfoot_nz")
    if lay["ground"] is not None:
        # base: the front ladder rises from the landing (1c); the bottom stop's standing room
        (l0, _l1, _lh) = lay["landing"]
        # the zone above the two standing surfaces (landing 3.35, ETDQ deck 3.404): the 0.10 m
        # kick plates and the posts / rails of the removed segment would stand in it
        etdq_top = p.z_rope + DQ_DECK_TOP_BELOW_ROPE
        stand = _room_rule(me, table, fparts, co, p, "R2e bottom stop (landing + ETDQ deck)", own_nz,
                           (lx, ly), (-1.0, 0.0), max(zs, etdq_top) + 0.01, nz[0], CLIMB_WIDTH)
        room = _room_rule(me, table, fparts, co, p, "R2e roof route", own_nz, (lx, ly), (-1.0, 0.0),
                          nz[0], term)
        cy0, cy1 = ly - CLIMB_WIDTH / 2.0, ly + CLIMB_WIDTH / 2.0
        k = ETDQ_KEEPOUT
        deck_under = max(0.0, min(cy1, -k["y"][0]) - max(cy0, -k["y"][1]))
        res["R2e"] = (f"roof route: front ladder from the landing (rails 0.48 at y {ly:.3f}), corridor "
                      f"{CLIMB_WIDTH} x {CLIMB_MIN} m clear from the lowest rung {nz[0]:.2f} to the crown "
                      f"+ {CLIMB_TERMINUS_HEAD} ({term:.2f}): {room['fixed'][0]:.2f} m to fixed objects, "
                      f"{room['chairs'][0]:.2f} m to the chairs; bottom stop: nothing in the zone from "
                      f"the landing up (the outer rail segment + kick gone: an open rail gap); standing "
                      f"surface = the landing ({lx - l0:.2f} m in front of the rungs) + past a "
                      f"{l0 - ETDQ_INBOARD_X:.3f} m gap the hosted ETDQ's -Y deck strip "
                      f"(ETDQ:grate_deckp, top {p.z_rope + DQ_DECK_TOP_BELOW_ROPE:.3f} = the stop "
                      f"+ {p.z_rope + DQ_DECK_TOP_BELOW_ROPE - zs:.3f}) under {deck_under:.2f} m of the "
                      f"corridor's width: it IS the stop-level standing surface")
        nums["roof_route"] = {"ladder_y": round(ly, 4), "rails_c2c_m": round(2 * nose_ladder_half(p), 3),
                              "corridor_m": [CLIMB_WIDTH, CLIMB_MIN], "z": [round(nz[0], 3), round(term, 3)],
                              "room_fixed_m": room["fixed"][0], "room_chairs_m": room["chairs"][0],
                              "bottom_stop": {"landing_depth_m": round(lx - l0, 3),
                                              "gap_to_etdq_m": round(l0 - ETDQ_INBOARD_X, 3),
                                              "etdq_deck_top": round(p.z_rope + DQ_DECK_TOP_BELOW_ROPE, 3),
                                              "etdq_deck_is_stop_surface": True,
                                              "zone_room_m": stand["fixed"][0]},
                              "rail_gap": "open (Q10 default): post_gangna_1 .. post_gangnb_0"}
        # R2g: the 1c ground flight at the corridor width rule
        gf = lay["ground"]
        (gx0, gx1), gy = gf["x"], gf["y"]
        c_lo, c_hi = ETDQ_INBOARD_X + 0.001, p.x_nose - 0.001
        if c_hi - c_lo < CLIMB_WIDTH - 1e-9:
            raise ContractError(f"R2g ground flight: only {c_hi - c_lo:.3f} m between the ETDQ and the band")
        gz0 = gf["rungs"][0]
        groom = _room_rule(me, table, fparts, co, p, "R2g ground flight", ("lrail_gf", "lrung_gf", "lfoot_gf",
                                                                             "lstand_gf"),
                           ((c_lo + c_hi) / 2.0, gy), gf["dir"], gz0, zs + CLIMB_HEAD, c_hi - c_lo)
        res["R2g"] = (f"ground flight corridor {c_hi - c_lo:.3f} m wide (x {c_lo:.3f}..{c_hi:.3f}, the hosted "
                      f"ETDQ's inboard end to the band face) x {CLIMB_MIN} m deep from the lowest rung "
                      f"{gz0:.2f}: {groom['fixed'][0]:.2f} m to {groom['fixed'][1]}, "
                      f"{groom['chairs'][0]:.2f} m to the chairs")
        nums["ground_flight_corridor"] = {"x": [round(c_lo, 4), round(c_hi, 4)],
                                          "room_fixed_m": groom["fixed"][0],
                                          "room_chairs_m": groom["chairs"][0]}
    else:
        # top: the straight run (footing -> ladder-way -> crown + 0.4), WELL_DEPTH_MAX 0.62
        room = _room_rule(me, table, fparts, co, p, "R2e roof route", own_nz, (lx, ly), (-1.0, 0.0),
                          nz[0], term, 2.0 * 0.31)
        res["R2e"] = (f"roof route: the straight run through the {WELL_DEPTH_MAX} m ladder-way, corridor "
                      f"0.62 x {CLIMB_MIN} m clear from the lowest rung {nz[0]:.2f} to the crown + "
                      f"{CLIMB_TERMINUS_HEAD} ({term:.2f}): {room['fixed'][0]:.2f} m fixed, "
                      f"{room['chairs'][0]:.2f} m chairs (the 0.60 yardstick exception is gone)")
        nums["roof_route"] = {"ladder_y": round(ly, 4), "well_depth_m": WELL_DEPTH_MAX,
                              "z": [round(nz[0], 3), round(term, 3)], "room_fixed_m": room["fixed"][0],
                              "room_chairs_m": room["chairs"][0]}
    # R2f: the pedestal ladder
    pf = pf_layout(p)
    if pf is None:
        raise ContractError("R2f pf ladder: on the spine (PF_OFF_SPINE off) - no hatch, rungs in the spine")
    sp = _part_bvh(me, table, fparts, co, lambda n: n.startswith("spine_"))[0]
    pfb = _part_bvh(me, table, fparts, co, lambda n: n.startswith(("lrail_pf", "lrung_pf", "hatch_sofpf")))[0]
    pairs = pfb.overlap(sp) if (sp and pfb) else []
    sv = _pv(me, table, fparts, co, "spine_")
    s_lo, s_hi = sv.min(axis=0), sv.max(axis=0)
    rz = []
    in_spine = []
    for n in table:
        if n.startswith("lrung_pf"):
            c = _pv(me, table, fparts, co, n).mean(axis=0)
            rz.append(float(c[2]))
            if (c > s_lo).all() and (c < s_hi).all():
                in_spine.append(n)
    if pairs or in_spine:
        raise ContractError(f"R2f pf ladder in the spine: {len(pairs)} face pairs, rungs {in_spine}")
    rz.sort()
    if min(abs(z - zs) for z in rz) > 0.05 + 1e-6:
        raise ContractError(f"R2f no pf rung within 0.05 of the stop {zs} (top rung {rz[-1]:.3f})")
    rails = _pv(me, table, fparts, co, "lrail_pf")
    if rails[:, 2].max() > zs - 0.005 or rails[:, 2].max() < zs - 0.10:
        raise ContractError(f"R2f pf rails top {rails[:, 2].max():.3f}: want under the leaf, within 0.10 of "
                            f"the hatch {zs}")
    if np.abs(rails[:, 1]).max() > CORRIDOR + 1e-3:
        raise ContractError(f"R2f pf rails |y| {np.abs(rails[:, 1]).max():.3f} > C7 {CORRIDOR}")
    # the opening is real: rays up through the hole meet the hatch leaf before anything else
    # (the ladder's own rails / rungs left out: they run up into the opening)
    keep = lambda n: not n.startswith(("lrail_pf", "lrung_pf"))   # noqa: E731
    allb, _n = _part_bvh(me, table, fparts, co, keep)
    kept = [i for i, fp in enumerate(fparts) if keep(table[fp - 1])]
    hx0, hx1, hy0, hy1 = pf["hole"]
    shut = []
    for fu in np.linspace(0.15, 0.85, 4):
        for fv in np.linspace(0.15, 0.85, 4):
            o = Vector((hx0 + fu * (hx1 - hx0), hy0 + fv * (hy1 - hy0), zs - 0.20))
            loc, _nn, idx, _d = allb.ray_cast(o, Vector((0.0, 0.0, 1.0)), 0.5)
            who = None
            if loc is not None:
                who = _poly_part(me, table, fparts, kept[idx])
            if who is None or not who.startswith("hatch_sofpf_leaf"):
                shut.append(who)
    if shut:
        raise ContractError(f"R2f the soffit is not cut at the hatch: rays up meet {sorted(set(map(str, shut)))}")
    (hx, hy) = pf["hatch"]
    room = _room_rule(me, table, fparts, co, p, "R2f pf ladder", ("lrail_pf", "lrung_pf", "hatch_sofpf"),
                      (pf["x"], pf["y"]), pf["dir"], rz[0], zs + CLIMB_TERMINUS_HEAD)
    # >= 0.10 from the beams under the tongue, the crossheads and the RoofLight footprints
    hv = co[_vidx(me, table, fparts, lambda n: n.startswith(("lrail_pf", "lrung_pf", "hatch_sofpf")))]
    h_lo, h_hi = hv.min(axis=0), hv.max(axis=0)
    near = []
    for n in sorted(set(table)):
        if n.startswith(("beam_", "crosshead_")):
            v = _pv(me, table, fparts, co, n)
            near.append((_aabb_gap(h_lo, h_hi, v.min(axis=0), v.max(axis=0)), n))
    LD = _LD()
    for pfab, pos, facing, mount in dq_subobjects(p):
        if mount == "soffit":
            lo, hi = LD.subobj_box(pfab, LD.blender_to_game(pos), frame="blender")
            near.append((_aabb_gap(h_lo, h_hi, lo, hi), f"{pfab}@{pos[0]:.2f},{pos[1]:.2f}"))
    near.sort()
    if near and near[0][0] < 0.10 - 1e-6:
        raise ContractError(f"R2f pf ladder / hatch {near[0][0]:.3f} m from {near[0][1]} (< 0.10)")
    spine_gap = float(np.abs(rails[:, 1]).min() - s_hi[1])
    res["R2f"] = (f"pf ladder off the spine at y {pf['y']} (rails {pf['y'] - PF_HALF:.2f} / "
                  f"{pf['y'] + PF_HALF:.2f}, inside C7 {CORRIDOR}), x {pf['x']:.2f}, {len(rz)} rungs "
                  f"{rz[0]:.2f} -> {rz[-1]:.2f} (no rung in the spine), hatch_sofpf "
                  f"{PF_HATCH} m cut through soffit_tongue at ({hx:.2f}, {hy:.2f}): 16 / 16 rays meet "
                  f"the leaf; corridor {CLIMB_WIDTH} x {CLIMB_MIN} m to the hatch + "
                  f"{CLIMB_TERMINUS_HEAD}: {room['fixed'][0]:.2f} m fixed, {room['chairs'][0]:.2f} m "
                  f"chairs; nearest {near[0][1]} {near[0][0]:.2f} m; step-off: the spine top "
                  f"{spine_gap:.2f} m beside the near rail")
    nums["pf_ladder"] = {"x": round(pf["x"], 4), "y": pf["y"], "rails_c2c_m": 2 * PF_HALF,
                         "rungs": [round(rz[0], 3), round(rz[-1], 3), len(rz)],
                         "hatch": {"centre": [round(hx, 4), round(hy, 4), zs], "opening_m": PF_HATCH,
                                   "cut": True},
                         "room_fixed_m": room["fixed"][0], "room_chairs_m": room["chairs"][0],
                         "nearest": [near[0][1], round(near[0][0], 3)],
                         "spine_step_m": round(spine_gap, 3)}
    for k_ in sorted(res):
        print(f"[contract] {k_} ok: {res[k_]}")
    return res, nums


DQ_DECK_TOP_BELOW_ROPE = -0.296        # lift_tower.DQ_DECK: the hosted ETDQ's deck top under its rope


def _aabb_gap(alo, ahi, blo, bhi):
    """Euclidean gap between two AABBs (0 when they touch or overlap)."""
    d = np.maximum(0.0, np.maximum(np.asarray(blo) - np.asarray(ahi), np.asarray(alo) - np.asarray(bhi)))
    return float(np.linalg.norm(d))


def _box_surface_pts(lo, hi, step=0.02):
    """Points on the six faces of the AABB (lo, hi) at <= `step` spacing (a box-to-mesh face gap via
    BVH find_nearest is then exact to about step / sqrt(2))."""
    lo, hi = np.asarray(lo, dtype=np.float64), np.asarray(hi, dtype=np.float64)
    g = [np.linspace(lo[i], hi[i], max(2, int(np.ceil((hi[i] - lo[i]) / step)) + 1)) for i in range(3)]
    pts = []
    for ax in range(3):
        o1, o2 = [a for a in range(3) if a != ax]
        A, B = np.meshgrid(g[o1], g[o2], indexing="ij")
        for val in (lo[ax], hi[ax]):
            P = np.empty((A.size, 3))
            P[:, ax], P[:, o1], P[:, o2] = val, A.ravel(), B.ravel()
            pts.append(P)
    return np.vstack(pts)


def _bvh_gap(tree, pts, maxd):
    """(smallest distance from `pts` to the faces of `tree` within maxd, that face's index or None)."""
    best, bi = float(maxd), None
    for q in pts:
        r = tree.find_nearest(Vector(q), best)
        if r[0] is not None and r[3] < best:
            best, bi = float(r[3]), r[2]
    return best, bi


def _lamp_head_zone(lo, hi, pos, facing):
    """D8: the head zone of a LightpolePark union box - from D1_LAMP_HEAD_REACH along `facing` (the arm,
    authoring frame) to the box end, the top D1_LAMP_HEAD_DROP m."""
    f = _LD()._dir3(facing)
    ax = 0 if abs(f.x) > 0.5 else 1
    hlo, hhi = np.array(lo, dtype=np.float64), np.array(hi, dtype=np.float64)
    if f[ax] > 0:
        hlo[ax] = max(hlo[ax], pos[ax] + D1_LAMP_HEAD_REACH)
    else:
        hhi[ax] = min(hhi[ax], pos[ax] - D1_LAMP_HEAD_REACH)
    hlo[2] = hhi[2] - D1_LAMP_HEAD_DROP
    return hlo, hhi


def _poly_part(me, table, fparts, idx):
    """The part name of the idx-th polygon of a _part_bvh(.., lambda n: True) tree (all faces)."""
    q = int(fparts[idx])
    return table[q - 1] if 0 < q <= len(table) else None


# MC10: the dressing contacts each kit instance may make with the station (host prefixes), and
# with other dressing; any other overlap is a placement error (BVH, real faces)
D1_CONTACTS = (
    # DQ follow-up: the SLOW boards stand on the deck on their own two posts (board_slow_post0/1)
    (("board_", "label_"), ("cab_walls", "engroom_walls", "gate_ld_panel", "soffit_", "deck")),
    (("maze_",), ("deck",)), (("gate_",), ("deck", "board_waithere")),
    (("carpet_",), ("deck",)), (("stripe_",), ("deck", "carpet_", "grate_")),
    (("label_seat",), ("carpet_",)),
    (("estop_",), ("deck",)), (("ext_",), ("cab_walls",)), (("hosereel_",), ("cab_walls", "engroom_walls")),
    (("tool_",), ("deck", "engroom_walls", "cabinet_", "cabwin")),
    (("skirack_", "ski_"), ("deck", "skirack_", "ski_")),
    (("chev_",), ("band_nose",)), (("hatch_sofpf",), ("soffit_",)),
    (("gauge_", "label_tension"), ("cabinet_",)), (("doorlite_",), ("door_cab_leaf",)),
    (("sill_",), ("cab_walls", "cabwin")),
)


_KIT_OF = {"board": "sign", "label": "sign", "gauge": "sign", "maze_post": "maze", "maze_rope": "maze",
           "maze_flag": "maze", "skirack": "skirack", "ski": "skirack", "floodbase": "flood",
           "floodbrk": "flood", "flood": "flood", "lamp_flood": "flood"}


def _dress_family(n):
    """The kit instance a dressing part belongs to: (kit, instance) - board_ops + label_ops ->
    ('sign', 'ops'), floodbase_bN + flood_bN_mast + lamp_floodbN_0 -> ('flood', 'bN'), ..."""
    import re
    m = re.match(r"(board|label|maze_post|maze_rope|maze_flag|gate|carpet|stripe|estop|ext|hosereel|"
                 r"tool|skirack|ski|floodbase|floodbrk|flood|lamp_flood|chev|hatch|gauge|doorlite|sill)"
                 r"_?([A-Za-z]+[0-9]*)", n)
    if not m:
        return (n, n)
    return (_KIT_OF.get(m.group(1), m.group(1)), m.group(2))


def assert_detail1(t, me, table, fparts, co, p):
    """MC10 (AUTHORING frame): every dressing part is box-tested on its REAL faces against the
    station and the other dressing (BVH overlap; the declared host contacts D1_CONTACTS allowed),
    against the C8 chair envelope (both pins) and the hosted ETDQ keep-out; every vanilla
    sub-object footprint (cs2_liftdress.SUBOBJ variant-UNION bounds, its mount face backed off
    0.01) is clear of the station mesh, the dressing, the C8 boxes and the other sub-objects; D9
    placement: deck props on the deck top, soffit lights on the soffit >= 0.10 from beam_ /
    hatch_ / lrail_, wall lights <= 0.05 off their wall facing its normal. Returns
    ({"D8": message, "D9": message}, numbers)."""
    LD = _LD()
    dress = set(_dress_names(p, table))
    if not dress:
        raise ContractError("D8: no detail-pass dressing in the build")
    res, nums = {}, {}
    names = [table[q - 1] for q in fparts]
    groups = {}
    for i, n in enumerate(names):
        if n in dress:
            groups.setdefault(n, []).append(i)
    allowed = []
    for pre, hosts in D1_CONTACTS:
        allowed.append((pre, hosts))

    def ok_pair(a, b):
        for pre, hosts in allowed:
            if a.startswith(pre) and b.startswith(hosts):
                return True
            if b.startswith(pre) and a.startswith(hosts):
                return True
        # parts of the same kit instance touch by construction (cs2_liftdress.DRESS_CONTACTS)
        return a in dress and b in dress and _dress_family(a) == _dress_family(b)
    verts = [tuple(v) for v in co]
    tree_all = BVHTree.FromPolygons(verts, [tuple(pl.vertices) for pl in me.polygons], epsilon=0.0)
    bad = []
    for n in sorted(groups):
        sub = BVHTree.FromPolygons(verts, [tuple(me.polygons[i].vertices) for i in groups[n]], epsilon=0.0)
        for _i, j in sub.overlap(tree_all):
            other = names[j]
            if other == n or ok_pair(n, other):
                continue
            bad.append((n, other))
    bad = sorted(set(bad))
    if bad:
        raise ContractError(f"D8 dressing overlaps undeclared parts: {bad[:8]} ({len(bad)} pairs)")
    # C8 + ETDQ keep-out on every dressing vertex (C8 itself runs on the whole mesh in
    # check_clearance; this reports the dressing's own margin)
    dvi = _vidx(me, table, fparts, lambda n: n in dress)
    dv = co[dvi]
    kos = _keepouts(p)
    near_c8 = 9.0
    for lab_, lo, hi in kos:
        inside = ((dv > lo + 0.002) & (dv < hi - 0.002)).all(axis=1)
        if inside.any():
            # (fine-detail step 3b: name the parts; the vertex -> part map is the first face using it)
            vpart = {}
            for pl, q in zip(me.polygons, fparts):
                if table[q - 1] in dress:
                    for v in pl.vertices:
                        vpart.setdefault(v, table[q - 1])
            who = sorted({vpart.get(int(v)) for v in np.asarray(dvi)[inside]} - {None})
            raise ContractError(f"D8 dressing inside {lab_}: {int(inside.sum())} vertices of {who[:4]}")
        if lab_.startswith("C8"):
            g = np.maximum(0.0, np.maximum(lo - dv, dv - hi))
            near_c8 = min(near_c8, float(np.linalg.norm(g, axis=1).min()))
    # sub-object footprints
    rows = dq_subobjects(p)
    boxes, heads = [], []
    for pfab, pos, facing, mount in rows:
        rot = LD.facing_quat(facing, frame="blender") if facing else (0.0, 0.0, 0.0, 1.0)
        lo, hi = LD.subobj_box(pfab, LD.blender_to_game(pos), rot, frame="blender")
        lo, hi = np.array(lo), np.array(hi)
        if mount == "deck":
            if abs(pos[2] - DECK_H) > 0.01:
                raise ContractError(f"D9 {pfab} at z {pos[2]} is not on the deck top {DECK_H}")
            # follow-up resume (C16-style, the tram's check_subobjects): a face <= 0.03 m under the origin
            hs_ = tree_all.ray_cast(Vector((pos[0], pos[1], pos[2] + 0.02)), Vector((0.0, 0.0, -1.0)), 0.20)
            if hs_[0] is None or hs_[3] - 0.02 > 0.03:
                raise ContractError(f"D9 {pfab} at {pos}: no deck face within 0.03 m under its origin "
                                    f"({None if hs_[0] is None else round(hs_[3] - 0.02, 3)})")
            lo[2] += 0.01
        elif mount == "soffit":
            if abs(pos[2] - p.z_soffit) > 0.02:
                raise ContractError(f"D9 {pfab} at z {pos[2]} is not on the soffit {p.z_soffit}")
            hi[2] -= 0.01
        elif mount == "wall":
            n_ = LD._dir3(facing)
            back = Vector(pos) - n_ * 0.05
            loc, _nn, idx, dist = tree_all.ray_cast(Vector(pos), -n_, 0.05)
            if loc is None:
                raise ContractError(f"D9 {pfab} at {pos} is more than 0.05 m off a wall (facing {facing})")
            ax = int(np.argmax(np.abs(np.array(n_))))
            if n_[ax] > 0:
                lo[ax] += 0.01
            else:
                hi[ax] -= 0.01
        # follow-up resume (C16-style): the union box stays inside the 2 x 4 lot (authoring y = prefab x
        # +-4 LOT[0], x = prefab z +-4 LOT[1])
        if max(-lo[1], hi[1]) > 4.0 * LOT[0] + 1e-6 or max(-lo[0], hi[0]) > 4.0 * LOT[1] + 1e-6:
            raise ContractError(f"D9 {pfab} at {pos}: its union box leaves the {LOT[0]} x {LOT[1]} lot")
        boxes.append((pfab, pos, lo, hi))
        if pfab.startswith("LightpolePark"):
            heads.append((pos, facing) + _lamp_head_zone(lo, hi, pos, facing))
    sub_hits = []
    # D8 lamp heads (D1_LAMP_HEAD_*): the head zone keeps its standoff from every station face
    head_gaps = {}
    for pos, facing, hlo, hhi in heads:
        g_, i_ = _bvh_gap(tree_all, _box_surface_pts(hlo, hhi), D1_LAMP_HEAD_CLEAR + 1.0)
        head_gaps[pos] = (g_, None if i_ is None else names[i_])
        if g_ < D1_LAMP_HEAD_CLEAR - 1e-6:
            sub_hits.append((f"LightpoleParkRandom01@{pos[0]:.2f},{pos[1]:.2f} facing {facing}",
                             [f"head {g_:.3f} m from {names[i_]} < {D1_LAMP_HEAD_CLEAR}"]))
    for pfab, pos, lo, hi in boxes:
        corners = [(x, y, z) for z in (lo[2], hi[2]) for x, y in ((lo[0], lo[1]), (hi[0], lo[1]),
                                                                    (hi[0], hi[1]), (lo[0], hi[1]))]
        quads = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
        bb = BVHTree.FromPolygons(corners, quads, epsilon=0.0)
        hit = sorted({names[i] for i, _j in tree_all.overlap(bb)})
        inside = ((co > lo) & (co < hi)).all(axis=1)
        if inside.any():
            hit = sorted(set(hit) | {names[i] for i, pl in enumerate(me.polygons)
                                     if inside[list(pl.vertices)].any()})
        for lab_, klo, khi in kos:
            if (klo < hi).all() and (khi > lo).all():
                hit.append(lab_)
        for pfab2, pos2, lo2, hi2 in boxes:
            if pos2 != pos and (lo2 < hi).all() and (hi2 > lo).all():
                hit.append(f"{pfab2}@{pos2[0]:.2f},{pos2[1]:.2f}")
        if hit:
            sub_hits.append((f"{pfab}@{pos[0]:.2f},{pos[1]:.2f}", sorted(set(hit))[:5]))
        if pfab.startswith("RoofLight"):
            for n in sorted(set(table)):
                if n.startswith(("beam_", "hatch_", "lrail_")):
                    v = _pv(me, table, fparts, co, n)
                    g = _aabb_gap(lo, hi, v.min(axis=0), v.max(axis=0))
                    if g < 0.10 - 1e-6:
                        sub_hits.append((f"{pfab}@{pos[0]:.2f},{pos[1]:.2f}", [f"{n} {g:.3f} m < 0.10"]))
    if sub_hits:
        raise ContractError(f"D8 sub-object footprints (variant-union bounds) collide: {sub_hits[:6]}")
    res["D8"] = (f"{len(groups)} dressing parts box-tested on their faces: no undeclared overlap; "
                 f"none in the C8 envelope (nearest {near_c8:.3f} m) or the hosted ETDQ; "
                 f"{len(boxes)} vanilla footprints (variant-union bounds) clear of the mesh, the "
                 f"dressing, the C8 boxes and each other"
                 + (f"; {len(heads)} lamp heads >= {D1_LAMP_HEAD_CLEAR} m from every station face "
                    f"(nearest {min(v[0] for v in head_gaps.values()):.3f} m)" if heads else ""))
    res["D9"] = (f"sub-objects: deck props on {DECK_H} with a deck face <= 0.03 m under the origin, every union "
                 f"box inside the {LOT[0]} x {LOT[1]} lot, soffit lights on {p.z_soffit} >= 0.10 from beam_ / "
                 f"hatch_ / lrail_, wall lights <= 0.05 off their wall")
    nums = {"dress_parts": len(groups), "c8_nearest_m": round(near_c8, 3), "sub_objects": len(boxes)}
    # DQ follow-up: each lamp post's union box to the nearest station / dressing FACE (the deck it stands on
    # excluded; follow-up resume: true face distances on 2 cm box samples - the first attempt reported
    # nearest VERTICES, which hid the 0.09 m head-to-band gap), its head zone's gap (D8 gate) and the
    # nearest C8 / ETDQ keep-out box - the report's clearance numbers
    lamp_clear = []
    kpolys = [i for i, fp in enumerate(fparts) if table[fp - 1] != "deck"]
    keep_b, _nk = _part_bvh(me, table, fparts, co, lambda n_: n_ != "deck")
    for pfab, pos, lo, hi in boxes:
        if not pfab.startswith("LightpolePark"):
            continue
        g, gi = _bvh_gap(keep_b, _box_surface_pts(lo, hi), 3.0)
        kg = min((_aabb_gap(lo, hi, klo, khi), lab_) for lab_, klo, khi in kos)
        hg, hn = head_gaps[pos]
        lamp_clear.append({"at": [round(pos[0], 3), round(pos[1], 3)], "box_face_gap_m": round(g, 3),
                           "nearest": None if gi is None else table[fparts[kpolys[gi]] - 1],
                           "head_face_gap_m": round(hg, 3), "head_nearest": hn,
                           "keepout_gap_m": round(float(kg[0]), 3), "nearest_keepout": kg[1]})
    if lamp_clear:
        nums["lamp_posts"] = lamp_clear
        print("[detach] lamp posts (union box -> nearest face, deck excluded / head zone -> nearest face / "
              "keep-out): " + "; ".join(
                  f"({c_['at'][0]}, {c_['at'][1]}) {c_['box_face_gap_m']} m {c_['nearest']} / head "
                  f"{c_['head_face_gap_m']} m {c_['head_nearest']} / {c_['keepout_gap_m']} m {c_['nearest_keepout']}"
                  for c_ in lamp_clear))
    for k_ in sorted(res):
        print(f"[contract] {k_} ok: {res[k_]}")
    return res, nums


def assert_board_order(me, table, fparts, co, p):
    """D11 (DQ follow-up; Richard, fourteenth look: "an UNLOAD HERE sign immediately followed by a STAY
    SEATED sign"): the boards a rider meets along the chair's direction of travel, MEASURED on the built
    faces (AUTHORING x = game z): every hanging board (a board_<n> with rods to the soffit) must be in
    D1_TRAVEL[kind]["seq"], on its lane, in that order; the base's walk to the chair comes first (WAIT
    HERE on the maze gate -> the loading carpet -> the load line on the carpet's far end); the top's
    UNLOAD board stands within D1_AT_LINE m before the unload line and nothing hangs on the lane past
    it. 2026-10-12 (nineteenth look: "We don't need the STAY SEATED at the top DQ station"): the top's
    sequence is RAISE BAR -> UNLOAD and D1_TRAVEL's `forbid` bars every board kind listed there (the top:
    "stay") in ANY mount - a hanging, post or wall STAY SEATED raises D11. Returns ({"D11": message},
    {"sequence": [...]})."""
    LD = _LD()
    spec = D1_TRAVEL[p.kind]
    d, lane = spec["dir"], spec["lane"]
    names = set(table)

    def ext(prefix):
        v = _pv(me, table, fparts, co, prefix)
        return v.min(axis=0), v.max(axis=0)

    for k_ in spec.get("forbid", ()):
        bad_ = sorted(n for n in names if n in (f"board_{k_}", f"label_{k_}")
                      or n.startswith((f"board_{k_}_", f"label_{k_}_")))
        if bad_:
            raise ContractError(f"D11 the {p.kind} carries a {' '.join(LD.BOARDS[k_]['words'])} board "
                                f"({bad_}): not wanted here (Richard, nineteenth look)")
    hanging = sorted(n[len("board_"):-len("_rod0")] for n in names
                     if n.startswith("board_") and n.endswith("_rod0"))
    items = {}
    for nm in hanging:
        lo, hi = ext(f"label_{nm}")
        c = (lo + hi) / 2.0
        if (c[1] > 0) != (lane > 0):
            raise ContractError(f"D11 the hanging board {nm} is off the {('+' if lane > 0 else '-')}Y lane "
                                f"(y {c[1]:.2f})")
        items[nm] = (d * c[0], d * c[0], float(c[0]), float(c[0]))
    want = list(spec["seq"])
    extra = sorted(set(items) - set(want))
    if extra:
        raise ContractError(f"D11 hanging boards not in the {p.kind}'s travel sequence {want}: {extra}")
    if "waithere" in want:
        lo, hi = ext("label_waithere")
        x = float((lo[0] + hi[0]) / 2.0)
        items["waithere"] = (d * x, d * x, x, x)
    if "carpet" in want:
        lo, hi = ext("carpet_")
        a, b = sorted((d * float(lo[0]), d * float(hi[0])))
        items["carpet"] = (a, b, float(hi[0] if d < 0 else lo[0]), float(lo[0] if d < 0 else hi[0]))
    if "load" in want:
        lo, hi = ext("stripe_load")
        x = float((lo[0] + hi[0]) / 2.0)
        items["load"] = (d * x, d * x, x, x)
        ca, cb = items["carpet"][0], items["carpet"][1]
        if not (cb - 0.30 - 1e-6 <= d * x <= cb + 1e-6):
            raise ContractError(f"D11 the load line x {x:.2f} is not on the carpet's far end")
    missing = [k for k in want if k not in items]
    if missing:
        raise ContractError(f"D11 {p.kind}: {missing} missing from the build")
    seq = [(k, items[k]) for k in want]
    bad = [(a, b) for (a, ia), (b, ib) in zip(seq, seq[1:]) if not ib[0] > ia[0] + 1e-6]
    if bad:
        raise ContractError(f"D11 board order along the travel broken at {bad}: measured "
                            + ", ".join(f"{k} x {v[2]:.2f}" for k, v in seq))
    line = None
    if spec.get("end"):
        bname, lpart = spec["end"]
        lo, hi = ext(lpart)
        line = float((lo[0] + hi[0]) / 2.0)
        sb = items[bname][0]
        if not (0.0 <= d * line - sb <= D1_AT_LINE + 1e-6):
            raise ContractError(f"D11 the {bname} board x {items[bname][2]:.2f} is not within {D1_AT_LINE} m "
                                f"before its line x {line:.2f}")
        past = [k for k, v in items.items() if k in hanging and v[0] > d * line + 1e-6]
        if past:
            raise ContractError(f"D11 boards hang on the lane PAST the {lpart} line x {line:.2f}: {past}")

    def word(k):
        if k == "carpet":
            return "loading carpet"
        if k == "load":
            return "load line"
        return " ".join(LD.BOARDS[k]["words"])
    parts_ = []
    for k, v in seq:
        parts_.append(f"{word(k)} z {v[2]:.2f}" if v[2] == v[3] else f"{word(k)} z {v[2]:.2f}..{v[3]:.2f}")
    msg = (f"along the chair's travel ({'+' if d > 0 else '-'}z on the {'+' if lane > 0 else '-'}x lane, prefab "
           f"frame): " + " -> ".join(parts_)
           + (f" | {spec['end'][0]} {D1_AT_LINE} m window before the line z {line:.2f}, nothing hangs past it"
              if line is not None else "")
           + "".join(f" | no {word(k_)} board (any mount)" for k_ in spec.get("forbid", ())))
    nums = {"travel_dir_z": d, "lane_x_sign": lane,
            "sequence": [{"item": k, "words": word(k), "z": [round(v[2], 3), round(v[3], 3)]} for k, v in seq],
            "unload_line_z": None if line is None else round(line, 3),
            **({"forbidden": [{"item": k_, "words": word(k_)} for k_ in spec["forbid"]]}
               if spec.get("forbid") else {})}
    print(f"[contract] D11 ok: {msg}")
    return {"D11": msg}, nums


# --------------------------------------------------------------------------------------
# Fine-detail step 3b: the dressing kit's rows (F1, C16-style, on the REAL kit meshes)
# --------------------------------------------------------------------------------------

_KIT_MESH = {}


def load_kit_mesh(mesh):
    """The shipped dressing-kit FBX assets/import/props/B/<mesh>/<mesh>.fbx as numpy in its PREFAB frame (raw
    FBX cm / 100 = prefab-local (x, y up, z), as the hosted towers), triangulated; checked against the kit
    manifest's bounds_m (1 mm: guards the axis convention). Cached by sha1; the import's objects, materials
    and images are removed again and the active object restored (load_gd_cabin's pattern)."""
    import hashlib
    path = os.path.join(FD_KIT_ROOT, mesh, f"{mesh}.fbx")
    if not os.path.isfile(path):
        raise ContractError(f"F1 no dressing-kit FBX at {path} (props/B must ship first)")
    with open(path, "rb") as fh:
        sha = hashlib.sha1(fh.read()).hexdigest()
    if sha in _KIT_MESH:
        return _KIT_MESH[sha]
    mats0, imgs0 = set(bpy.data.materials.keys()), set(bpy.data.images.keys())
    active = bpy.context.view_layer.objects.active
    objs = cs2_validate.import_fbx_temp(path)
    try:
        o = [x for x in objs if x.type == "MESH"][0]
        me = o.data
        co = np.empty(len(me.vertices) * 3, np.float64)
        me.vertices.foreach_get("co", co)
        V = co.reshape(-1, 3) / 100.0
        tris = []
        for pl in me.polygons:
            vs = list(pl.vertices)
            for j in range(1, len(vs) - 1):
                tris.append((vs[0], vs[j], vs[j + 1]))
    finally:
        cs2_validate.remove_objects(objs)
        for k in sorted(set(bpy.data.materials.keys()) - mats0):
            bpy.data.materials.remove(bpy.data.materials[k])
        for k in sorted(set(bpy.data.images.keys()) - imgs0):
            bpy.data.images.remove(bpy.data.images[k])
        try:
            bpy.context.view_layer.objects.active = active
        except (ReferenceError, RuntimeError):
            pass
    with open(os.path.join(FD_KIT_ROOT, mesh, f"{mesh}.manifest.json"), "r", encoding="utf-8") as fh:
        bm = json.load(fh).get("bounds_m") or {}
    if not bm or np.abs(V.min(axis=0) - np.array(bm["min"])).max() > 1e-3 \
            or np.abs(V.max(axis=0) - np.array(bm["max"])).max() > 1e-3:
        raise ContractError(f"F1 {mesh}: FBX bounds {V.min(axis=0).round(4).tolist()} .. "
                            f"{V.max(axis=0).round(4).tolist()} != the manifest's {bm}")
    _KIT_MESH[sha] = {"V": V, "tris": tris, "sha1": sha,
                      "path": os.path.relpath(path, REPO).replace("\\", "/")}
    return _KIT_MESH[sha]


def kit_to_authoring(V, row):
    """Prefab-local kit vertices placed by a row (game local: the yaw about +Y as cs2_liftdress.subobj_box,
    x' = x cos + z sin, z' = -x sin + z cos, then + position) -> the AUTHORING frame (game_to_blender)."""
    a = _LD()._yaw_of(row["rotation"])
    c, s = math.cos(a), math.sin(a)
    px, py, pz = (float(v) for v in row["position"])
    gx = V[:, 0] * c + V[:, 2] * s + px
    gz = -V[:, 0] * s + V[:, 2] * c + pz
    gy = V[:, 1] + py
    return np.stack([gz, -gx, gy], axis=1)       # game_to_blender: authoring y = -gx (x-sign gate N)


def fd_kit_boxes(p):
    """[(label, lo, hi)] AABBs (authoring) of this station's kit rows: the REAL kit mesh placed by its row."""
    out = []
    for s, r in zip(fd_kit_specs(p), fd_kit_rows(p)):
        A = kit_to_authoring(load_kit_mesh(r["mesh"])["V"], r)
        out.append((f"kit {s['prop']}@{s['pos'][0]:.2f},{s['pos'][1]:.2f}", A.min(axis=0), A.max(axis=0)))
    return out


def assert_kit_rows(t, me, table, fparts, co, p):
    """F1 (fine-detail step 3b, C16-style; AUTHORING frame): every dressing-kit row of fd_kit_specs -
      - its CID is the staged object-prefab CID (fine_detail.staged_cid) of an uncoloured prop or the colour-set
        VARIANT's seeded CID of a coloured one (fine_detail.row_colour_error), and the parts it replaces are gone;
      - mount: ground props have a floor face <= FD_MOUNT_GAP under the foot centre and every AABB bottom corner
        (inset 3 cm; grade rows of the gondola stand at z 0 exactly), wall props have a wall face <= FD_MOUNT_GAP
        behind the centre and every corner of their back rectangle (the whole plate on the wall: no corner over
        a door, an edge or air), lean props keep the foot-line rule (|fine_detail.lean_rest_gap| <=
        fine_detail.FOOT_TOL), every rest-plane vertex has a wall face <= FD_MOUNT_GAP behind it, and the floor
        is under the feet;
      - the REAL kit mesh, backed FD_CLEAR_OFF off its mount, overlaps NO station face (BVH);
      - its AABB is clear of the C8 chair / rider envelope (both pins), the hosted ETDQ keep-out / the gondola
        tower keep-out + the real SkiLiftTowerETGDO AABB, the gondola nose ladder's climbing zone, every vanilla
        row's variant-union box and every other kit row, and inside the lot.
    Returns ({"F1": message}, numbers)."""
    FD, LD = _FD(), _LD()
    specs, rows = fd_kit_specs(p), fd_kit_rows(p)
    if not specs:
        return {}, None
    names = [table[q - 1] for q in fparts]
    tree_all = BVHTree.FromPolygons([tuple(v) for v in co], [tuple(pl.vertices) for pl in me.polygons],
                                    epsilon=0.0)
    left = sorted({n for n in set(table) for s in specs for pre in s["removed"] if n.startswith(pre)})
    if left:
        raise ContractError(f"F1 the parts the kit rows replace are still in the host: {left[:6]}")
    vboxes = []
    for pfab, pos, facing, mount in (gd_subobjects(p) if p.gondola else dq_subobjects(p)):
        rot = LD.facing_quat(facing, frame="blender") if facing else (0.0, 0.0, 0.0, 1.0)
        lo, hi = LD.subobj_box(pfab, LD.blender_to_game(pos), rot, frame="blender")
        lo, hi = np.array(lo, dtype=np.float64), np.array(hi, dtype=np.float64)
        if mount in ("deck", "ground"):
            lo[2] += 0.01
        elif mount in ("soffit", "ceiling"):
            hi[2] -= 0.01
        elif mount == "wall":
            n_ = LD._dir3(facing)
            ax = int(np.argmax(np.abs(np.array(n_))))
            if n_[ax] > 0:
                lo[ax] += 0.01
            else:
                hi[ax] -= 0.01
        vboxes.append((f"{pfab}@{pos[0]:.2f},{pos[1]:.2f}", lo, hi))
    kos = list(_keepouts(p, with_subobjects=False))
    if p.gondola and p.hosted_tower:
        tw = load_gd_tower()
        if tw is not None:
            kos.append(("SkiLiftTowerETGDO (real LOD0 AABB)", tw[0]["V"].min(axis=0), tw[0]["V"].max(axis=0)))
    if p.gondola:
        Lg = gd_ladder_layout(p)
        kos.append(("nose ladder climbing zone", np.array([Lg["x"] - CLIMB_MIN, Lg["y"] - 0.31, 0.0]),
                    np.array([Lg["x"], Lg["y"] + 0.31, Lg["z_top"] + 0.5])))
    lx, ly = GD_LOT_HALF if (p.gondola and GD_REV1) else LOT_HALF
    down = Vector((0.0, 0.0, -1.0))

    def floor_gap(x, y, z):
        h = tree_all.ray_cast(Vector((x, y, z + 0.02)), down, 0.05 + FD_MOUNT_GAP)
        return None if h[0] is None else h[3] - 0.02

    out, kboxes, bad = [], [], []
    for s, r in zip(specs, rows):
        tag = f"{s['prop']}@{s['pos'][0]:.2f},{s['pos'][1]:.2f}"
        # the CID: KIT's staged one (uncoloured) or the colour-set VARIANT's (coloured; kit colour variants)
        cerr = FD.row_colour_error(r)
        if cerr:
            raise ContractError(f"F1 {tag}: CID {r['m_Object']}: {cerr}")
        km = load_kit_mesh(r["mesh"])
        A = kit_to_authoring(km["V"], r)
        n3 = LD._dir3(s["facing"])
        nv = np.array([n3.x, n3.y, 0.0])
        rec = {"prop": s["prop"], "cid": r["m_Object"], "mount": s["mount"], "pos_authoring": [round(v, 4) for v in s["pos"]],
               "row_pos": list(r["position"]), "row_rot": list(r["rotation"]), "was": s["was"]}
        if r.get("colour_set"):
            rec["colour_set"] = r["colour_set"]
        if s["mount"] == "ground":
            lo, hi = A.min(axis=0), A.max(axis=0)
            if p.gondola and abs(s["pos"][2]) < 1e-9:
                if abs(lo[2]) > 1e-4:
                    bad.append((tag, f"grade prop bottom z {lo[2]:.4f} != 0"))
                rec["floor"] = "grade (z 0)"
            else:
                pts = [(s["pos"][0], s["pos"][1])] + [(x, y) for x in (lo[0] + 0.03, hi[0] - 0.03)
                                                      for y in (lo[1] + 0.03, hi[1] - 0.03)]
                gaps = [floor_gap(x, y, s["pos"][2]) for x, y in pts]
                if any(g is None or abs(g) > FD_MOUNT_GAP for g in gaps):
                    bad.append((tag, f"no floor face within {FD_MOUNT_GAP} m under the foot / corners {gaps}"))
                rec["floor_gap_m"] = round(max(abs(g) for g in gaps if g is not None), 4) if all(
                    g is not None for g in gaps) else None
            off = np.array([0.0, 0.0, FD_CLEAR_OFF])
        else:
            # the back plane / rest plane in the wall's (right, up) frame
            right = np.array([-nv[1], nv[0], 0.0])
            if s["mount"] == "wall":
                # the wall must stand behind the prop's whole silhouette (its AABB projected on the wall plane):
                # a plate over a door, a corner or air fails
                plane = np.array(s["pos"], dtype=np.float64)
                u = (A - plane) @ right
                w = A[:, 2]
                pts = [plane + right * (u.min() + 0.02) + np.array([0, 0, w.min() + 0.02 - plane[2]]),
                       plane + right * (u.max() - 0.02) + np.array([0, 0, w.min() + 0.02 - plane[2]]),
                       plane + right * (u.min() + 0.02) + np.array([0, 0, w.max() - 0.02 - plane[2]]),
                       plane + right * (u.max() - 0.02) + np.array([0, 0, w.max() - 0.02 - plane[2]]),
                       plane + right * (0.5 * (u.min() + u.max())) + np.array([0, 0, 0.5 * (w.min() + w.max()) - plane[2]])]
                rec["wall_silhouette_m"] = [round(float(u.max() - u.min()), 3), round(float(w.max() - w.min()), 3)]
                # access: FD_ACCESS m of free space in front of the prop (no station face, no other row's box)
                d_front = float(((A - plane) @ nv).max())
                cs = [plane + right * uu + nv * dd + np.array([0.0, 0.0, ww - plane[2]])
                      for uu in (u.min(), u.max()) for dd in (d_front + 0.005, d_front + FD_ACCESS)
                      for ww in (w.min(), w.max())]
                alo, ahi = np.min(cs, axis=0), np.max(cs, axis=0)
                acc = [(x, y, z) for z in (alo[2], ahi[2]) for x, y in ((alo[0], alo[1]), (ahi[0], alo[1]),
                                                                        (ahi[0], ahi[1]), (alo[0], ahi[1]))]
                abvh = BVHTree.FromPolygons(acc, [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6),
                                                  (3, 0, 4, 7)], epsilon=0.0)
                blocked = sorted({names[j] for _i, j in abvh.overlap(tree_all)}
                                 | {names[i] for i, pl in enumerate(me.polygons)
                                    if ((co[list(pl.vertices)] > alo) & (co[list(pl.vertices)] < ahi)).all(axis=1).any()})
                blocked += [lab_ for lab_, vlo, vhi in vboxes + kboxes if (vlo < ahi).all() and (vhi > alo).all()]
                rec["access_m"] = FD_ACCESS
                if blocked:
                    bad.append((tag, f"no access: {blocked[:4]} within {FD_ACCESS} m in front of it"))
            else:
                # the foot-line rule (fine_detail.lean_rest_gap against the row's own wall plane: the wall face,
                # or the plane d behind a ledge face, kit_ledge_offset) and a real CONTACT: the kit mesh moved
                # FD_MOUNT_GAP toward the wall must meet a station face (it rests on the wall / the ledge edge)
                g = FD.lean_rest_gap(r)
                rec["rest_gap_m"] = round(g, 5)
                if s.get("ledge"):
                    rec["ledge_z"] = list(s["ledge"])
                    rec["ledge_offset_m"] = round(float(np.dot(np.array(s["pos"]) - A[np.argmin(A @ nv)], nv)), 4)
                if abs(g) > FD.FOOT_TOL + 1e-9:
                    bad.append((tag, f"foot-line rule: rest plane {g:.4f} m off the wall (tol {FD.FOOT_TOL})"))
                Bt = A - nv * FD_MOUNT_GAP + np.array([0.0, 0.0, FD_CLEAR_OFF])   # lifted: the floor never counts
                ktt = BVHTree.FromPolygons([tuple(v) for v in Bt], km["tris"], epsilon=0.0)
                touch = sorted({names[j] for _i, j in ktt.overlap(tree_all)})
                rec["rests_on"] = touch[:4]
                if not touch:
                    bad.append((tag, f"lean prop touches nothing within {FD_MOUNT_GAP} m (no contact with the wall)"))
                feet = A[A[:, 2] <= A[:, 2].min() + 0.002]
                fg = [floor_gap(float(v[0]), float(v[1]), float(s["pos"][2])) for v in feet[:: max(1, len(feet) // 8)]] \
                    if not (p.gondola and abs(s["pos"][2]) < 1e-9) else [0.0]
                if any(x is None or abs(x) > FD_MOUNT_GAP + 0.002 for x in fg):
                    bad.append((tag, f"lean prop feet: no floor within {FD_MOUNT_GAP} m ({fg[:4]})"))
                pts = []
            wg = []
            for q in pts:
                h = tree_all.ray_cast(Vector(tuple(np.asarray(q) + nv * 0.005)), Vector(tuple(-nv)), 0.005 + FD_MOUNT_GAP)
                wg.append(None if h[0] is None else round(h[3] - 0.005, 4))
            if any(x is None for x in wg):
                bad.append((tag, f"{s['mount']}: {sum(1 for x in wg if x is None)} of {len(wg)} back points "
                                 f"have no wall face within {FD_MOUNT_GAP} m behind them"))
            if wg:
                rec["wall_gap_m"] = max((abs(x) for x in wg if x is not None), default=None)
            off = nv * FD_CLEAR_OFF + (np.array([0.0, 0.0, FD_CLEAR_OFF]) if s["mount"] == "lean" else 0.0)
        B = A + off
        kt = BVHTree.FromPolygons([tuple(v) for v in B], km["tris"], epsilon=0.0)
        hits = sorted({names[j] for _i, j in kt.overlap(tree_all)})
        if hits:
            bad.append((tag, f"the kit mesh (backed {FD_CLEAR_OFF} m off its mount) cuts {hits[:5]}"))
        lo, hi = B.min(axis=0), B.max(axis=0)
        for lab_, klo, khi in kos:
            if (np.asarray(klo) < hi).all() and (np.asarray(khi) > lo).all():
                bad.append((tag, f"AABB in {lab_}"))
        for lab_, vlo, vhi in vboxes:
            if (vlo < hi).all() and (vhi > lo).all():
                bad.append((tag, f"AABB overlaps the vanilla row {lab_}"))
        for lab_, olo, ohi in kboxes:
            if (olo < hi).all() and (ohi > lo).all():
                bad.append((tag, f"AABB overlaps the kit row {lab_}"))
        if max(-lo[0], hi[0]) > lx + LOT_TOL or max(-lo[1], hi[1]) > ly + 1e-6:
            bad.append((tag, f"leaves the lot ({lo.round(2).tolist()} .. {hi.round(2).tolist()})"))
        c8 = [_aabb_gap(lo, hi, klo, khi) for lab_, klo, khi in kos if lab_.startswith("C8")]
        rec["c8_gap_m"] = round(min(c8), 3) if c8 else None
        others = [(_aabb_gap(lo, hi, vlo, vhi), lab_) for lab_, vlo, vhi in vboxes + kboxes]
        if others:
            g_, l_ = min(others)
            rec["nearest_row"] = [l_, round(g_, 3)]
        rec["aabb_authoring"] = [lo.round(4).tolist(), hi.round(4).tolist()]
        rec["kit_fbx_sha1"] = km["sha1"][:12]
        kboxes.append((f"kit {tag}", lo, hi))
        out.append(rec)
    if bad:
        raise ContractError(f"F1 kit rows: {bad[:6]} ({len(bad)} findings)")
    by = {}
    for s in specs:
        by[s["prop"]] = by.get(s["prop"], 0) + 1
    msg = (f"{len(specs)} dressing-kit rows {by}: CIDs staged, the replaced parts gone, mounts held (floor / whole "
           f"back on the wall / foot-line rule), the real kit meshes clear of every station face, the C8 envelope "
           f"(nearest {min(r_['c8_gap_m'] for r_ in out if r_['c8_gap_m'] is not None):.3f} m), the hosted tower / "
           f"keep-outs, every vanilla and kit row, inside the lot")
    print(f"[contract] F1 ok: {msg}")
    return {"F1": msg}, {"rows": out}


# --------------------------------------------------------------------------------------
# Gondola draft 1 contracts (G1-G8) and the real-cabin sweep
# --------------------------------------------------------------------------------------

def _seg_dist(pt, a, b):
    """2D distance from pt to the segment a-b."""
    ax, ay = a
    bx, by = b
    dx, dy = bx - ax, by - ay
    L2 = dx * dx + dy * dy
    t = 0.0 if L2 == 0.0 else max(0.0, min(1.0, ((pt[0] - ax) * dx + (pt[1] - ay) * dy) / L2))
    return math.hypot(pt[0] - ax - t * dx, pt[1] - ay - t * dy)


def _in_poly(pt, poly):
    x, y = pt
    inside = False
    n = len(poly)
    for i in range(n):
        (x0, y0), (x1, y1) = poly[i], poly[(i + 1) % n]
        if (y0 > y) != (y1 > y) and x < x0 + (y - y0) * (x1 - x0) / (y1 - y0):
            inside = not inside
    return inside


def gd_portal_margin(p):
    """G1: every carrier box (GD_ENV) grown by GD_CLEAR, at both pin heights, lies inside the
    portal outline (closed below grade); returns (smallest outline-to-box margin m, box,
    pin). Raises when a corner leaves the opening or an outline vertex enters a box."""
    outline = gd_portal_outline()
    poly = outline + [(outline[-1][0], -1.0), (outline[0][0], -1.0)]
    segs = list(zip(outline, outline[1:]))
    best = (math.inf, None, None)
    dzs = [0.0] + ([float(p.grip_above_rope)] if p.grip_above_rope else [])
    for dz in dzs:
        zu = p.z_rope - p.hang + dz
        for label, _tr, la, h0, h1, dy in p.env:
            u0, u1 = dy - la / 2.0 - GD_CLEAR, dy + la / 2.0 + GD_CLEAR
            z0, z1 = zu + h0 - GD_CLEAR, zu + h1 + GD_CLEAR
            for c in ((u0, z0), (u1, z0), (u1, z1), (u0, z1)):
                if not _in_poly(c, poly):
                    raise ContractError(f"G1 portal: carrier box {label} (+{GD_CLEAR}) corner "
                                        f"({c[0]:.3f}, {c[1]:.3f}) at pin +{dz} leaves the opening")
                m = min(_seg_dist(c, a, b) for a, b in segs)
                if m < best[0]:
                    best = (m, label, dz)
            for u, z in outline:
                if u0 < u < u1 and z0 < z < z1:
                    raise ContractError(f"G1 portal: outline point ({u:.3f}, {z:.3f}) inside "
                                        f"carrier box {label} (+{GD_CLEAR}) at pin +{dz}")
                if z > 0.0:
                    m = math.hypot(max(u0 - u, 0.0, u - u1), max(z0 - z, 0.0, z - z1))
                    if m < best[0]:
                        best = (m, label, dz)
    return best


_GD_CABIN = {}
GD_CABIN_STALK_H = (2.80, 3.85)  # nitpick A3: the cabin's stalk band (GD_ENV "hanger" 2.745..3.905, nothing else there)
GD_CABIN_STALK_U = 0.40          # ... every stalk vertex sits this far OUTBOARD of the lane (GD_ENV u 0.501..0.749)


def gd_cabin_outboard(path=CARRIERS_PATH):
    """Nitpick A3 (ruling F1): the gondola cabin's outboard side as an authoring y sign, read from
    catalog/carriers.json gd8 "outboard" ("+Y" -> +1, "-Y" -> -1; lift_cabin.GD_OUTBOARD builds it)."""
    with open(path, "r", encoding="utf-8") as fh:
        side = (json.load(fh).get("carriers") or {}).get("gd8", {}).get("outboard")
    if side not in ("+Y", "-Y"):
        raise ContractError(f"G8: carriers.json gd8 outboard {side!r} (want '+Y' or '-Y')")
    return 1.0 if side == "+Y" else -1.0


def load_gd_cabin(path=GD_CABIN_FBX, outboard=None):
    """The shipped SkiGondolaCabin8A LOD0 as numpy, cabin frame (u lateral +outboard, h up from
    the underside, t travel; raw FBX cm = prefab-local / 100), cached by (sha1, side). The import's
    objects, materials and images are removed again (the scene stays as it was).

    Nitpick A3 (ruling F1): `outboard` = the cabin's outboard authoring-y sign (None = carriers.json gd8,
    gd_cabin_outboard). u = sign * raw x, h = raw y, t = sign * raw z: a PROPER rotation of the cabin (a
    half turn about the vertical when the sign is -1). Placed by gd_cabin_placements (u -> the outboard
    unit, t -> its travel unit (s, 0), which runs AGAINST the travel on both lanes) the -Y cabin faces its
    travel - the mover's yaw (prefab +z = authoring +X along the travel) - and the +Y cabin keeps the pose
    G1 / G8 always gave it (the lens's "forward -X"). Raises when the stalk is not on that side."""
    import hashlib
    if not os.path.isfile(path):
        raise ContractError(f"G8 cabin sweep: no cabin FBX at {path} (cabins/A must ship first)")
    sgn = float(outboard) if outboard is not None else gd_cabin_outboard()
    with open(path, "rb") as fh:
        sha = hashlib.sha1(fh.read()).hexdigest()
    if (sha, sgn) in _GD_CABIN:
        return _GD_CABIN[(sha, sgn)]
    mats0, imgs0 = set(bpy.data.materials.keys()), set(bpy.data.images.keys())
    active = bpy.context.view_layer.objects.active
    objs = cs2_validate.import_fbx_temp(path)
    try:
        o = [x for x in objs if x.type == "MESH"][0]
        me = o.data
        co = np.empty(len(me.vertices) * 3, np.float64)
        me.vertices.foreach_get("co", co)
        V = co.reshape(-1, 3) / 100.0
        tris = []
        for pl in me.polygons:
            vs = list(pl.vertices)
            for j in range(1, len(vs) - 1):
                tris.append((vs[0], vs[j], vs[j + 1]))
    finally:
        cs2_validate.remove_objects(objs)
        for k in sorted(set(bpy.data.materials.keys()) - mats0):
            bpy.data.materials.remove(bpy.data.materials[k])
        for k in sorted(set(bpy.data.images.keys()) - imgs0):
            bpy.data.images.remove(bpy.data.images[k])
        try:
            bpy.context.view_layer.objects.active = active
        except (ReferenceError, RuntimeError):
            pass
    E = np.array([np.linalg.norm(V[a] - V[b]) for tr in tris for a, b in ((tr[0], tr[1]),
                                                                         (tr[1], tr[2]),
                                                                         (tr[2], tr[0]))])
    rel = os.path.relpath(path, REPO).replace("\\", "/")
    raw_bounds = [V.min(axis=0).round(4).tolist(), V.max(axis=0).round(4).tolist()]
    V = np.stack([sgn * V[:, 0], V[:, 1], sgn * V[:, 2]], axis=1)       # (u outboard +, h, t)
    band = V[(V[:, 1] > GD_CABIN_STALK_H[0]) & (V[:, 1] < GD_CABIN_STALK_H[1])]
    if not len(band) or float(band[:, 0].min()) < GD_CABIN_STALK_U:
        raise ContractError(f"G8: the shipped cabin's stalk (h {GD_CABIN_STALK_H}) is not outboard under the outboard "
                            f"sign {sgn:+.0f} (carriers.json gd8): u {band[:, 0].min() if len(band) else None} < "
                            f"{GD_CABIN_STALK_U} - the catalog and cabins/A disagree")
    _GD_CABIN[(sha, sgn)] = {"V": V, "tris": tris, "sha1": sha, "path": rel,
                             "outboard_authoring": "+Y" if sgn > 0 else "-Y",
                             "frame": "u = sign * raw x (outboard +), h = raw y, t = sign * raw z (proper; nitpick A3)",
                             "stalk_u_m": [round(float(band[:, 0].min()), 4), round(float(band[:, 0].max()), 4)],
                             "bounds": [V.min(axis=0).round(4).tolist(), V.max(axis=0).round(4).tolist()],
                             "bounds_raw": raw_bounds, "max_edge_m": round(float(E.max()), 4)}
    return _GD_CABIN[(sha, sgn)]


def gd_cabin_placements(p, step=0.25, astep=3.0):
    """Cabin poses along the loop: both straights from 1 m outside the anchor to the wheel
    (cabins arrive from the line THROUGH the portals and run along the platforms), then round
    the bullwheel. (label, centre (x, y), outboard unit, travel unit) - a proper rotation, the
    -Y lane is the +Y pose turned 180 deg. The "travel unit" (s, 0) on lane s runs AGAINST the stations'
    spin (both stations depart on +Y toward -X); load_gd_cabin's t = sign * raw z makes the -Y cabin
    (nitpick A3) face its real travel in these poses (the mover's yaw)."""
    out = []
    xs = list(np.arange(p.anchor_x - 1.0, p.x_wheel, step)) + [p.x_wheel]
    for s in (-1.0, 1.0):
        for x in xs:
            out.append((f"lane {'+' if s > 0 else '-'}Y x {x:+.2f}", (float(x), s * p.lane_y),
                        (0.0, s), (s, 0.0)))
    for a in np.arange(-90.0 + astep, 90.0 - 1e-9, astep):
        r = math.radians(float(a))
        out.append((f"turn {a:+.0f} deg", (p.x_wheel + p.lane_y * math.cos(r),
                                           p.lane_y * math.sin(r)),
                    (math.cos(r), math.sin(r)), (math.sin(r), -math.cos(r))))
    return out


def gd_cabin_sweep(t, table, p, lim=None):
    """G8 (the tram T8 idea, with a margin): the REAL shipped SkiGondolaCabin8A swept through
    the portals, along the platforms and round the bullwheel at both pin heights (grip top on
    the lane = ChairPinSystem today; rope point on the lane = +grip_above_rope). Clearance =
    the smallest of: every cabin vertex to the station surface, every nearby station vertex to
    the cabin surface, and 0 if the two meshes overlap - against every part except the rope
    machine the cabin rides (C8_ALLOWED). AUTHORING frame. Gate >= GD_CLEAR."""
    cab = load_gd_cabin()
    lim = GD_CLEAR + 0.25 if lim is None else lim
    me = t.data
    fparts = _face_parts(me)
    co = _co(me)
    allowed = np.array([n.startswith(C8_ALLOWED) for n in table], dtype=bool)
    keep = [i for i, fp in enumerate(fparts) if not allowed[fp - 1]]
    polys = [tuple(me.polygons[i].vertices) for i in keep]
    pnames = [table[fparts[i] - 1] for i in keep]
    bvh = BVHTree.FromPolygons([tuple(v) for v in co], polys, epsilon=0.0)
    vid = sorted({v for pl in polys for v in pl})
    vname = {}
    for pl, nm in zip(polys, pnames):
        for v in pl:
            vname.setdefault(v, nm)
    Vs = co[vid]
    Vc = cab["V"]
    U, H, T = Vc[:, 0], Vc[:, 1], Vc[:, 2]
    cab_bvh = BVHTree.FromPolygons([tuple(v) for v in Vc], cab["tris"], epsilon=0.0)
    gtar = float(p.grip_above_rope) if p.grip_above_rope else 0.0
    frames = (("grip-top pin", 0.0),) + ((("rope-point pin", gtar),) if gtar else ())
    best = {"d": math.inf}
    per_part = {}
    n_place = 0

    def note(d, part, label, frame, how):
        if d < per_part.get(part, (math.inf,))[0]:
            per_part[part] = (d, label, frame)
        if d < best["d"]:
            best.update(d=d, part=part, at=label, frame=frame, how=how)
    for frame, dz in frames:
        zu = p.z_rope - p.hang + dz
        for label, c, eo, et in gd_cabin_placements(p):
            n_place += 1
            W = np.stack([c[0] + U * eo[0] + T * et[0], c[1] + U * eo[1] + T * et[1], zu + H], axis=1)
            placed = BVHTree.FromPolygons([tuple(w) for w in W], cab["tris"], epsilon=0.0)
            ov = bvh.overlap(placed)
            if ov:
                note(0.0, pnames[ov[0][0]], label, frame, "overlap")
                continue
            for w in W:
                loc, _n, idx, d = bvh.find_nearest(Vector(w), lim)
                if loc is not None:
                    note(d, pnames[idx], label, frame, "cabin vertex -> station")
            lo, hi = W.min(axis=0) - lim, W.max(axis=0) + lim
            sel = np.nonzero(((Vs > lo) & (Vs < hi)).all(axis=1))[0]
            for i in sel:
                v = Vs[i]
                d2 = (v[0] - c[0], v[1] - c[1])
                q = Vector((d2[0] * eo[0] + d2[1] * eo[1], v[2] - zu, d2[0] * et[0] + d2[1] * et[1]))
                loc, _n, _idx, d = cab_bvh.find_nearest(q, lim)
                if loc is not None:
                    note(d, vname[vid[i]], label, frame, "station vertex -> cabin")
    if best["d"] == math.inf:
        best.update(d=lim, part=None, at=None, frame=None, how=f"nothing within {lim} m")
    worst = sorted(per_part.items(), key=lambda kv: kv[1][0])[:10]
    return {"min_clear_m": round(float(best["d"]), 4), "nearest_part": best["part"],
            "at": best["at"], "pin": best["frame"], "how": best["how"],
            "gate_m": GD_CLEAR, "placements": n_place, "search_m": lim,
            "nearest_by_part": {k: {"m": round(float(v[0]), 4), "at": v[1], "pin": v[2]}
                                for k, v in worst},
            "cabin": {"fbx": cab["path"], "sha1": cab["sha1"], "verts": int(len(Vc)),
                      "tris": len(cab["tris"]), "bounds_m": cab["bounds"],
                      "max_edge_m": cab["max_edge_m"], "outboard_authoring": cab["outboard_authoring"],
                      "frame": cab["frame"], "stalk_u_m": cab["stalk_u_m"]},
            "excluded_machine": list(C8_ALLOWED),
            "method": "BVH overlap per pose + vertex-to-surface both ways (cabin LOD0 vertices; "
                      "station vertices within the pose box); straights every 0.25 m from 1 m "
                      "outside the anchor, the turn every 3 deg"}


def gd_door_openings(p, me, table, fparts, co):
    """G3 (checker F1): every gondola door is a real opening. Per door 3 x 3 rays across the
    opening (x / y +-0.30 about its centre, z 0.45 / 1.25 / 2.05 above the platform), each from
    0.30 m outside the wall face through the wall to 0.30 m inside it, against every part but the
    door's own (door_<tag>_*: leaf, frame, handle, kick). Raises when any ray meets a wall."""
    W2, wi = p.w_half, p.w_half - WALL_T
    X = p.x_wheel
    xd = X - 3.0
    ox0 = X + GD_OPR_X0
    yd = gd_opr_side(p) * (GD_WALK_IN + wi) / 2.0       # nitpick A2: the room's side
    # revision 1: the skier entries are double doors GD_SIDE_DOOR wide - the rays spread +-0.60
    side = (GD_SIDE_DOOR, 0.60) if GD_REV1 else ((GD_DOOR_W, GD_DOOR_H), 0.30)
    opr = ((GD_DOOR_W, GD_DOOR_H), 0.30)
    doors = [("n", (0.0, -1.0, 0.0), lambda a, z: Vector((xd + a, -W2, z)), WALL_T, side),
             ("p", (0.0, 1.0, 0.0), lambda a, z: Vector((xd + a, W2, z)), WALL_T, side),
             ("opr", (-1.0, 0.0, 0.0), lambda a, z: Vector((ox0, yd + a, z)), 0.10, opr)]
    out = {}
    for tag, nrm, at, thick, (opening, spread) in doors:
        own = f"door_{tag}_"
        bvh, _n = _part_bvh(me, table, fparts, co, lambda n, own=own: not n.startswith(own))
        polys = [i for i, fp in enumerate(fparts) if not table[fp - 1].startswith(own)]
        nv = Vector(nrm)
        blocked = []
        for a in (-spread, 0.0, spread):
            for dz in (0.15, 0.95, 1.75):
                o = at(a, DECK_H + dz) + nv * 0.30
                loc, _nn, idx, d = bvh.ray_cast(o, -nv, 0.30 + thick + 0.30)
                if loc is not None:
                    blocked.append((table[fparts[polys[idx]] - 1], round(a, 2), round(DECK_H + dz, 2),
                                    round(d - 0.30, 3)))
        if blocked:
            raise ContractError(f"G3 door_{tag} is a plate on a solid wall: {len(blocked)} / 9 rays "
                                f"through its opening meet {sorted(set(b[0] for b in blocked))}")
        out[tag] = {"opening_m": list(opening), "sill_y": DECK_H, "wall_m": thick,
                    "rays_clear": 9, "ray_spread_m": spread}
        if GD_REV1 and tag in ("n", "p"):
            # DQ follow-up (rev-1 checker F3): the CLEAR opening - 9 across x 6 up rays over the whole
            # GD_SIDE_DOOR (2 cm in from the jambs, 2 cm under the head) against everything but the
            # leaves, their stiles and push bars (they open): the fixed frame (jambs, a centre post)
            # and the head transom count, so a post or a low head fails here
            dw_, dh_ = opening
            movable = tuple(f"door_{tag}_{k}" for k in ("leaf", "stile", "bar"))
            cb, _n2 = _part_bvh(me, table, fparts, co, lambda n, mv=movable: not n.startswith(mv))
            cpolys = [i for i, fp in enumerate(fparts) if not table[fp - 1].startswith(movable)]
            hit = []
            xs_ = [(-dw_ / 2.0 + 0.02) + i * (dw_ - 0.04) / 8.0 for i in range(9)]
            zs_ = [DECK_H + 0.02 + i * (dh_ - 0.04) / 5.0 for i in range(6)]
            for a in xs_:
                for z in zs_:
                    o = at(a, z) + nv * 0.30
                    loc, _nn, idx, d = cb.ray_cast(o, -nv, 0.30 + thick + 0.30)
                    if loc is not None:
                        hit.append((table[fparts[cpolys[idx]] - 1], round(a, 2), round(z, 2)))
            if hit:
                raise ContractError(f"G3 door_{tag} clear opening {dw_} x {dh_} m: {len(hit)} / "
                                    f"{len(xs_) * len(zs_)} rays meet fixed parts "
                                    f"{sorted(set(h[0] for h in hit))} (e.g. {hit[:3]})")
            out[tag].update({"clear_rays": len(xs_) * len(zs_), "clear_opening_m": [dw_, dh_],
                             "head_y": round(DECK_H + dh_, 3), "centre_post": False})
    return out


def gd_sign_occlusion(me, table, fparts, co, p):
    """G7: (sign, hit part) pairs where a ray from 1 m in front of a sign quad (5 x 5 points over its
    CONTENT - the emblem and the number plate inside the SIGN_PAD margin) meets another part before
    the sign: nothing may cover what a sign says (the gondola top's draft-1 portal sign sat 0.24 m
    behind the nose band; the eave gutters overhang the side signs' top margin by 0.08 m, not
    their content)."""
    bvh, _n = _part_bvh(me, table, fparts, co, lambda n: True)
    out = []
    for tag in ("n", "p", "q"):
        nm = f"signband_{tag}"
        if nm not in table:
            continue
        faces = [i for i, fp in enumerate(fparts) if table[fp - 1] == nm]
        pl = me.polygons[faces[0]]
        n = Vector(pl.normal).normalized()
        P = np.array([co[v] for v in pl.vertices])
        lo, hi = P.min(axis=0), P.max(axis=0)
        ins = np.array([0.0 if abs(n.x) > 0.5 else SIGN_PAD + 0.005,
                        0.0 if abs(n.y) > 0.5 else SIGN_PAD + 0.005, SIGN_PAD + 0.005])
        lo, hi = lo + ins, hi - ins
        for fu in np.linspace(0.0, 1.0, 5):
            for fv in np.linspace(0.0, 1.0, 5):
                # in-plane: the two non-normal axes take fu / fv, the normal axis its plane value
                if abs(n.x) > 0.5:
                    q = np.array([lo[0], lo[1] + (hi[1] - lo[1]) * fu, lo[2] + (hi[2] - lo[2]) * fv])
                else:
                    q = np.array([lo[0] + (hi[0] - lo[0]) * fu, lo[1], lo[2] + (hi[2] - lo[2]) * fv])
                o = Vector(tuple(q)) + n * 1.0
                loc, _nn, idx, _d = bvh.ray_cast(o, -n, 1.05)
                who = table[fparts[idx] - 1] if loc is not None else None
                if who != nm:
                    out.append((nm, who))
    return sorted(set(out))


# DQ follow-up (rev-1 checker F4, "declared-not-reached surfaces are exemptions, not passes"): every
# STANDABLE flat top of the hall (up-facing faces, normal z >= 0.98, above 0.10 m, summed per part to
# >= GD_STAND_AREA m2 with a footprint >= GD_STAND_MIN m on both axes) is either on a G3 route
# (GD_WALKABLE prefixes) or declared here WITH the reason it is not a walking surface; anything else
# fails G3 (gd_flat_tops; the list below is the census of revision 1, base + top). The reasons are
# design, not oversight:
GD_STAND_AREA = 0.50
GD_STAND_MIN = 0.40
GD_WALKABLE = ("floor", "platf_", "pstripe_", "opr_floor", "grate_stl", "grate_dk", "roof_shell", "stepside_")
GD_NOT_WALKABLE = {
    "opr_roof": "the operator room's ceiling slab INSIDE the hall, 2.3 m under the machinery deck: a room's "
                "roof, not a platform (no railing, nothing to service up there, hidden behind the glass)",
    "canopy_": "the entry canopies are roofs over the doors (3.0 x 1.8 m sheet steel on two tie rods), not "
               "walking surfaces; nothing on them needs service and snow is pushed off from the platform",
    "sheave_bull": "the bullwheel rim (machine, under the deck grating)",
    "wheelrope": "the haul rope round the bullwheel (machine)",
    "drive_": "the drive gearbox / brake tops (machine; serviced from the deck grating beside them)",
    "tension_": "the tension carriage top (machine; serviced from the deck grating beside it)",
}


def gd_flat_tops(me, table, fparts, co):
    """{part: (area m2, footprint min side m, top z)} of every standable flat top (GD_STAND_AREA /
    GD_STAND_MIN): up-facing faces (normal z >= 0.98) above 0.10 m, summed per part."""
    acc = {}
    for pl, fp in zip(me.polygons, fparts):
        if pl.normal.z < 0.98:
            continue
        v = co[list(pl.vertices)]
        z = float(v[:, 2].max())
        if z < 0.10:
            continue
        n = table[fp - 1]
        a, lo, hi, zt = acc.get(n, (0.0, v.min(axis=0), v.max(axis=0), z))
        acc[n] = (a + pl.area, np.minimum(lo, v.min(axis=0)), np.maximum(hi, v.max(axis=0)), max(zt, z))
    out = {}
    for n, (a, lo, hi, z) in acc.items():
        side = float(min(hi[0] - lo[0], hi[1] - lo[1]))
        if a >= GD_STAND_AREA and side >= GD_STAND_MIN:
            out[n] = (round(float(a), 3), round(side, 3), round(z, 3))
    return out


def gd_sign_nose_z(p):
    """The portal-face sign's centre z (GD_SIGN_NOSE_Z per kind; G7 keeps it between the slot tops
    and the nose band)."""
    return GD_SIGN_NOSE_Z[p.kind]


# -- gondola revision 1 contracts: G5 (the glazed hall), G6 (the real entry tower), G9 (the 3 x 4 lot),
# G10 (fit: part clash + the vanilla sub-object footprints) --------------------------------------

GD_TOWER_FBX = os.path.join(REPO, "assets", "import", "towers", "O", "SkiLiftTowerETGDO",
                            "SkiLiftTowerETGDO.fbx")
_GD_TOWER = {}


def load_gd_tower(path=GD_TOWER_FBX):
    """The shipped gondola entry compression tower SkiLiftTowerETGDO (towers/O, LOD0 + LOD1) placed as
    the base hosts it - (0, 0, -11) prefab-local, quat (0, 1, 0, 0) - in the AUTHORING frame: raw FBX
    cm / 100 = tower prefab-local (x, y, z); hosted (-x, y, -z - 11); authoring = (z, x, y). Cached by
    sha1; the import's objects / materials / images are removed again. None if not exported."""
    import hashlib
    out = []
    for suf in ("", "_LOD1"):
        f = path.replace(".fbx", f"{suf}.fbx")
        if not os.path.isfile(f):
            return None
        with open(f, "rb") as fh:
            sha = hashlib.sha1(fh.read()).hexdigest()
        if sha not in _GD_TOWER:
            mats0, imgs0 = set(bpy.data.materials.keys()), set(bpy.data.images.keys())
            objs = cs2_validate.import_fbx_temp(f)
            try:
                o = [x for x in objs if x.type == "MESH"][0]
                me = o.data
                co = np.empty(len(me.vertices) * 3, np.float64)
                me.vertices.foreach_get("co", co)
                T = co.reshape(-1, 3) / 100.0
                tris = []
                for pl in me.polygons:
                    vs = list(pl.vertices)
                    for j in range(1, len(vs) - 1):
                        tris.append((vs[0], vs[j], vs[j + 1]))
            finally:
                cs2_validate.remove_objects(objs)
                for k in sorted(set(bpy.data.materials.keys()) - mats0):
                    bpy.data.materials.remove(bpy.data.materials[k])
                for k in sorted(set(bpy.data.images.keys()) - imgs0):
                    bpy.data.images.remove(bpy.data.images[k])
            V = np.stack([-T[:, 2] - 11.0, -T[:, 0], T[:, 1]], axis=1)
            _GD_TOWER[sha] = {"V": V, "tris": tris, "sha1": sha,
                              "path": os.path.relpath(f, REPO).replace("\\", "/")}
        out.append(_GD_TOWER[sha])
    return out


def gd_tower_seam(t, table, p):
    """G6 (revision 1, the S8 idea for the gondola): the REAL hosted SkiLiftTowerETGDO (LOD0 and LOD1)
    against the station mesh - no triangle overlap with any part but the haul ropes it carries, and
    the smallest vertex-to-surface gap both ways (within 1 m). Returns the record (None when the tower
    is not exported); raises on an overlap."""
    lods = load_gd_tower()
    if lods is None:
        return None
    me = t.data
    fparts = _face_parts(me)
    co = _co(me)
    skip = ("haulrope_",)
    keep = [i for i, fp in enumerate(fparts) if not table[fp - 1].startswith(skip)]
    polys = [tuple(me.polygons[i].vertices) for i in keep]
    names = [table[fparts[i] - 1] for i in keep]
    bvh = BVHTree.FromPolygons([tuple(v) for v in co], polys, epsilon=0.0)
    used = sorted({v for pl in polys for v in pl})
    rec = {}
    for lvl, tw in zip(("LOD0", "LOD1"), lods):
        V = tw["V"]
        tb = BVHTree.FromPolygons([tuple(v) for v in V], tw["tris"], epsilon=0.0)
        ov = bvh.overlap(tb)
        if ov:
            raise ContractError(f"G6 the hosted SkiLiftTowerETGDO {lvl} overlaps the station: "
                                f"{sorted({names[i] for i, _j in ov})[:6]}")
        best = (math.inf, None)
        for v in V:
            loc, _n, idx, d = bvh.find_nearest(Vector(v), 1.0)
            if loc is not None and d < best[0]:
                best = (d, names[idx])
        lo, hi = V.min(axis=0) - 1.0, V.max(axis=0) + 1.0
        for i in used:
            v = co[i]
            if (v > lo).all() and (v < hi).all():
                loc, _n, _idx, d = tb.find_nearest(Vector(v), 1.0)
                if loc is not None and d < best[0]:
                    best = (d, "station vertex")
        rec[lvl] = {"min_gap_m": round(float(best[0]), 4) if best[1] else None, "nearest": best[1],
                    "fbx": tw["path"], "sha1": tw["sha1"],
                    "aabb_authoring": [V.min(axis=0).round(3).tolist(), V.max(axis=0).round(3).tolist()]}
    return rec


def gd_glazing(t, me, table, fparts, co, p):
    """G5 (revision 1): the long glazed hall. Every `_Win` pane of gd_hall_layout is present (a hole
    fails), vertical, in its plane, at its rect; each long wall's panes cover >= 0.85 of the wall
    between the corner columns from the coping to the ring beam; the mullions are <= 0.12 m wide; the
    nose carries glazing above the portal lintel, the back is glazed, the operator room keeps its own
    room. Rooms -> `_Win` ids by cs2_bldg.room_graph / assign_rooms (Room.Merge 2 m, the conservative
    3 m slice: ids are REUSED beyond NEIGHBOUR_M, never between hard neighbours), <= 25 distinct ids from
    the 1..23 pool, re-checked here pair by pair. Returns (message, numbers)."""
    import cs2_bldg as B
    L = gd_hall_layout(p)
    want = sorted(nm for nm, *_r in L["panes"])
    have = sorted(n for n in table if n.startswith("win_"))
    if have != want:
        raise ContractError(f"G5 glazing: panes missing {sorted(set(want) - set(have))[:6]}, extra "
                            f"{sorted(set(have) - set(want))[:6]}")
    area = {"n": 0.0, "p": 0.0, "f": 0.0, "b": 0.0}
    for nm, room, (ax, c, _s), (a0, a1, z0, z1) in L["panes"]:
        v = _pv(me, table, fparts, co, nm)
        k = 1 if ax == "y" else 0
        _near(float(np.ptp(v[:, k])), 0.0, 1e-4, f"G5 {nm} plane")
        _near(float(v[:, k].mean()), c, 1e-4, f"G5 {nm} plane position")
        j = 0 if ax == "y" else 1
        _near(float(v[:, j].min()), a0, 1e-4, f"G5 {nm} rect")
        _near(float(v[:, j].max()), a1, 1e-4, f"G5 {nm} rect")
        _near(float(v[:, 2].min()), z0, 1e-4, f"G5 {nm} rect z")
        _near(float(v[:, 2].max()), z1, 1e-4, f"G5 {nm} rect z")
        if ax == "y":
            side = "n" if c < 0.0 else "p"
        else:
            side = "f" if abs(c - (p.x_nose + 0.10)) < 1e-6 else "b"
        area[side] += (a1 - a0) * (z1 - z0)
    W2, wt = p.w_half, WALL_T
    wall_len = (p.x_back - wt) - (p.x_nose + wt)
    wall_h = L["z_head"] - GD_SILL
    frac = {tag: round(area[tag] / (wall_len * wall_h), 4) for tag in ("n", "p")}
    if min(frac.values()) < 0.85:
        raise ContractError(f"G5 glazed fraction of the long walls {frac} < 0.85")
    mw = 0.0
    for n in sorted(table):
        if n.startswith("mull_") and not n.startswith(("mull_cf", "mull_cb")):
            v = _pv(me, table, fparts, co, n)
            w_ = float(min(np.ptp(v[:, 0]), np.ptp(v[:, 1])))
            mw = max(mw, w_)
    if mw > 0.12 + 1e-6:
        raise ContractError(f"G5 mullions {mw:.3f} m wide > 0.12 (not slim)")
    if area["f"] <= 0.0 or area["b"] <= 0.0 or not any(r == "opr" for _n, r, *_x in L["panes"]):
        raise ContractError("G5 the nose / back glazing or the operator room's panes are missing")
    boxes, hard, soft = B.room_graph(t, table, B.NEIGHBOUR_M)
    ids = B.assign_rooms(sorted(boxes), hard, soft, p.variant, B.WIN_POOL)
    distinct = sorted(set(ids.values()))
    bad = [(a, b) for a, b in hard if ids[a] == ids[b]]
    if len(distinct) > B.MAX_ROOM_IDS or any(not (1 <= i <= 23) for i in distinct) or bad:
        raise ContractError(f"G5 rooms: {len(distinct)} ids {distinct}, hard neighbours sharing an id {bad[:4]}")
    # independent re-check: same-id rooms whose pane boxes overlap vertically lie >= NEIGHBOUR_M apart
    for a in sorted(boxes):
        for b in sorted(boxes):
            if a >= b or ids[a] != ids[b]:
                continue
            (al, ah), (bl, bh) = boxes[a], boxes[b]
            gx = max(0.0, bl[0] - ah[0], al[0] - bh[0])
            gy = max(0.0, bl[1] - ah[1], al[1] - bh[1])
            vo = min(ah[2], bh[2]) - max(al[2], bl[2])
            if vo > 1e-6 and math.hypot(gx, gy) < B.NEIGHBOUR_M - 1e-6:
                raise ContractError(f"G5 rooms {a} / {b} share id {ids[a]} {math.hypot(gx, gy):.2f} m apart")
    n_panes = len(L["panes"])
    msg = (f"long glazed hall: {n_panes} `_Win` panes (sides {frac} of the wall glazed, slim mullions "
           f"<= {mw:.2f} m), nose band above the lintel, glazed back, operator room panes; {len(boxes)} "
           f"rooms -> {len(distinct)} ids {distinct} (reused beyond {B.NEIGHBOUR_M} m, {len(hard)} hard pairs "
           f"apart)")
    nums = {"panes": n_panes, "pane_tris": 2 * n_panes, "glazed_fraction": frac,
            "mullion_w_max_m": round(mw, 3), "rooms": {k: int(v) for k, v in sorted(ids.items())},
            "room_ids": distinct, "neighbour_m": B.NEIGHBOUR_M, "hard_pairs": len(hard),
            "pane_area_m2": {k: round(v, 2) for k, v in area.items()},
            "tiers_z": [list(t_) for t_ in L["tiers"]], "bays_x": [list(b[:2]) + [b[2]] for b in L["bays"]],
            "nose_bays_y": list(GD_NOSE_BAYS), "back_bays_y": list(GD_BACK_BAYS),
            "door": {"clear_m": list(GD_SIDE_DOOR), "x": round(L["xd"], 3), "leaves": 2,
                     "draft1_clear_m": [GD_DOOR_W, GD_DOOR_H]}}
    return msg, nums


GD_FIT_PARTS = ("canopy_", "canopyrod_", "downspout_", "snowguard_", "lamp_eave", "outrig_", "signplate_",
                "spandrel_") + GD_DRESS_FAMS
# declared contacts (prefix pairs): a fitted part may touch only these hosts
GD_CONTACTS = (
    (("canopy_",), ("mull_", "canopyrod_")), (("canopyrod_",), ("canopy_", "mull_")),
    (("downspout_",), ("gutter_",)), (("snowguard_",), ("roof_shell",)),
    (("lamp_eave",), ("eavesof_",)), (("outrig_",), ("ringbeam_", "eavesof_")),
    (("signplate_",), ("mull_", "signband_")), (("spandrel_",), ("mull_", "wall_nose", "ringbeam_", "signband_")),
    (("board_", "label_"), ("opr_walls", "platf_")), (("ext_",), ("wall_",)), (("hosereel_",), ("wall_",)),
    (("estop_",), ("platf_",)), (("tool_",), ("stonebase_", "coping_")),
)


def _gd_family(n):
    """Kit instance of a fitted part: same-instance parts touch by construction."""
    import re
    if n.startswith(("canopy_", "canopyrod_")):
        return ("canopy", n.split("_")[1][:1])
    m = re.match(r"(board|label|ext|hosereel|estop|skirack|ski|tool|downspout|snowguard|lamp_eave|outrig|"
                 r"signplate|spandrel)_?([A-Za-z]+)", n)
    if not m:
        return (n, n)
    kit = {"label": "board", "ski": "skirack"}.get(m.group(1), m.group(1))
    return (kit, m.group(2))


def gd_fit(t, me, table, fparts, co, p, tower):
    """G9 + G10 (revision 1). G9 the 3 x 4 lot: every station vertex within |x| <= 16 + LOT_TOL
    (travel) and |y| <= 12 (lateral), the hosted tower's AABB and the HV stub tip (-16) inside it, every
    vanilla footprint inside it. G10 part clash: every fitted part (canopies, rods, downspouts, snow
    guards, eave lights, outriggers, sign plates, the spandrel, the dressing) box-tested on its REAL faces
    against the whole station (BVH overlap) - only the declared host contacts GD_CONTACTS and its own
    kit instance may touch; none of them in the C8 envelope or the hosted tower keep-out; every vanilla
    sub-object footprint (variant-UNION bounds, its mount face backed off 0.01) clear of the mesh, the
    C8 boxes, the tower (keep-out and the real ETGDO AABB), the nose ladder's climbing zone and each
    other; mounts: ground props on grade, ceiling lights <= 0.06 under a ceiling face, wall lights <= 0.05
    off a wall facing its normal. Returns ({"G9": .., "G10": ..}, numbers)."""
    LD = _LD()
    res, nums = {}, {}
    lx, ly = GD_LOT_HALF
    if np.abs(co[:, 0]).max() > lx + LOT_TOL or np.abs(co[:, 1]).max() > ly + 1e-4:
        raise ContractError(f"G9 lot {GD_LOT}: x {co[:, 0].min():.2f}..{co[:, 0].max():.2f}, "
                            f"y {co[:, 1].min():.2f}..{co[:, 1].max():.2f}")
    tw_box = None
    if tower is not None:
        tw_box = (np.array(tower["LOD0"]["aabb_authoring"][0]), np.array(tower["LOD0"]["aabb_authoring"][1]))
        if tw_box[0][0] < -lx - LOT_TOL or np.abs(tw_box[0][1]) > ly or np.abs(tw_box[1][1]) > ly:
            raise ContractError(f"G9 the hosted tower {tw_box} leaves the lot")
    names = [table[q - 1] for q in fparts]
    fit = sorted({n for n in table if n.startswith(GD_FIT_PARTS)})
    groups = {}
    for i, n in enumerate(names):
        if n in fit:
            groups.setdefault(n, []).append(i)
    verts = [tuple(v) for v in co]
    tree_all = BVHTree.FromPolygons(verts, [tuple(pl.vertices) for pl in me.polygons], epsilon=0.0)

    def ok_pair(a, b):
        for pre, hosts in GD_CONTACTS:
            if (a.startswith(pre) and b.startswith(hosts)) or (b.startswith(pre) and a.startswith(hosts)):
                return True
        return a in groups and b in groups and _gd_family(a) == _gd_family(b)
    bad = []
    for n in sorted(groups):
        sub = BVHTree.FromPolygons(verts, [tuple(me.polygons[i].vertices) for i in groups[n]], epsilon=0.0)
        for _i, j in sub.overlap(tree_all):
            other = names[j]
            if other != n and not ok_pair(n, other):
                bad.append((n, other))
    bad = sorted(set(bad))
    if bad:
        raise ContractError(f"G10 part clash: fitted parts overlap undeclared parts {bad[:8]} ({len(bad)} pairs)")
    kos = _keepouts(p, with_subobjects=False)
    dv = co[_vidx(me, table, fparts, lambda n: n in groups)]
    near_c8 = 9.0
    for lab_, lo, hi in kos:
        inside = ((dv > lo + 0.002) & (dv < hi - 0.002)).all(axis=1)
        if inside.any():
            raise ContractError(f"G10 fitted parts inside {lab_}: {int(inside.sum())} vertices")
        if lab_.startswith("C8"):
            g = np.maximum(0.0, np.maximum(lo - dv, dv - hi))
            near_c8 = min(near_c8, float(np.linalg.norm(g, axis=1).min()))
    # the nose ladder's climbing zone (G4's box: 0.61 deep x the rails' width, footing -> top + 1.80)
    Lg = gd_ladder_layout(p)
    zone = (np.array([Lg["x"] - CLIMB_MIN, Lg["y"] - 0.31, 0.0]),
            np.array([Lg["x"], Lg["y"] + 0.31, Lg["z_top"] + 0.5]))
    boxes = []
    for pfab, pos, facing, mount in gd_subobjects(p):
        rot = LD.facing_quat(facing, frame="blender") if facing else (0.0, 0.0, 0.0, 1.0)
        lo, hi = LD.subobj_box(pfab, LD.blender_to_game(pos), rot, frame="blender")
        lo, hi = np.array(lo, dtype=np.float64), np.array(hi, dtype=np.float64)
        if mount == "ground":
            if abs(pos[2]) > 1e-6:
                raise ContractError(f"G10 {pfab} at z {pos[2]} is not on grade")
            lo[2] += 0.01
        elif mount == "ceiling":
            loc, _nn, _idx, _d = tree_all.ray_cast(Vector((pos[0], pos[1], pos[2] - 0.03)), Vector((0, 0, 1)), 0.06)
            if loc is None:
                raise ContractError(f"G10 {pfab} at {pos} hangs more than 0.03 m under any ceiling")
            hi[2] -= 0.01
        elif mount == "wall":
            n_ = LD._dir3(facing)
            loc, _nn, _idx, _d = tree_all.ray_cast(Vector(pos), -n_, 0.05)
            if loc is None:
                raise ContractError(f"G10 {pfab} at {pos} is more than 0.05 m off a wall (facing {facing})")
            ax = int(np.argmax(np.abs(np.array(n_))))
            if n_[ax] > 0:
                lo[ax] += 0.01
            else:
                hi[ax] -= 0.01
        if np.abs(lo[0]) > lx + LOT_TOL or np.abs(hi[0]) > lx + LOT_TOL or max(abs(lo[1]), abs(hi[1])) > ly:
            raise ContractError(f"G9 {pfab}@{pos} leaves the lot")
        boxes.append((pfab, pos, lo, hi))
    sub_hits = []
    for pfab, pos, lo, hi in boxes:
        corners = [(x, y, z) for z in (lo[2], hi[2]) for x, y in ((lo[0], lo[1]), (hi[0], lo[1]),
                                                                    (hi[0], hi[1]), (lo[0], hi[1]))]
        quads = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
        bb = BVHTree.FromPolygons(corners, quads, epsilon=0.0)
        hit = sorted({names[i] for i, _j in tree_all.overlap(bb)})
        inside = ((co > lo) & (co < hi)).all(axis=1)
        if inside.any():
            hit = sorted(set(hit) | {names[i] for i, pl in enumerate(me.polygons) if inside[list(pl.vertices)].any()})
        for lab_, klo, khi in kos:
            if (klo < hi).all() and (khi > lo).all():
                hit.append(lab_)
        if tw_box is not None and (tw_box[0] < hi).all() and (tw_box[1] > lo).all():
            hit.append("SkiLiftTowerETGDO")
        if (zone[0] < hi).all() and (zone[1] > lo).all():
            hit.append("nose ladder climbing zone")
        for pfab2, pos2, lo2, hi2 in boxes:
            if pos2 != pos and (lo2 < hi).all() and (hi2 > lo).all():
                hit.append(f"{pfab2}@{pos2[0]:.2f},{pos2[1]:.2f}")
        if hit:
            sub_hits.append((f"{pfab}@{pos[0]:.2f},{pos[1]:.2f}", sorted(set(hit))[:5]))
    if sub_hits:
        raise ContractError(f"G10 sub-object footprints (variant-union bounds) collide: {sub_hits[:6]}")
    res["G9"] = (f"lot {GD_LOT[0]} x {GD_LOT[1]} (24 m across = prefab x, 32 m along the travel = prefab z): "
                 f"mesh x {co[:, 0].min():.2f}..{co[:, 0].max():.2f} (<= {lx} + {LOT_TOL}), y "
                 f"{co[:, 1].min():.2f}..{co[:, 1].max():.2f} (<= {ly}); hosted tower "
                 f"{'x %.2f..%.2f' % (tw_box[0][0], tw_box[1][0]) if tw_box is not None else 'not exported'}, "
                 f"HV stub tip -16 inside; {len(boxes)} vanilla footprints inside")
    res["G10"] = (f"{len(groups)} fitted parts box-tested on their faces: no undeclared overlap, none in the "
                  f"C8 envelope (nearest {near_c8:.3f} m) or the tower keep-out; {len(boxes)} vanilla "
                  f"footprints clear of the mesh, the cabins, the tower, the ladder zone and each other")
    nums = {"fit_parts": len(groups), "c8_nearest_m": round(near_c8, 3), "sub_objects": len(boxes),
            "lot_cells": list(GD_LOT), "mesh_x": [round(float(co[:, 0].min()), 3), round(float(co[:, 0].max()), 3)],
            "mesh_y": [round(float(co[:, 1].min()), 3), round(float(co[:, 1].max()), 3)]}
    return res, nums


def assert_gondola(t, me, table, fparts, co, p):
    """Gondola draft 1 contracts in the AUTHORING frame (raise -> nothing ships); returns
    ({label: message}, numbers for the manifest block "gondola").
    G1 portals: the carrier boxes + GD_CLEAR (both pin heights) inside the silhouette opening;
    G2 boarding platforms: top = the cabin floor (underside) - GONDOLA_STEP_IN, edge >=
       GD_CLEAR outboard of the cabin, the yellow edge stripe along it;
    G3 every walkable surface reachable from grade (the route table below, each link checked);
    G4 climbing room of the nose ladder >= CLIMB_MIN (DQ Revision 1c gate) and stair headroom
       >= 2.0 m over every tread, pitch <= 50 deg, risers <= 0.21, goings >= 0.20;
    G5 revision 1: the long glazed hall (gd_glazing: every `_Win` pane, the glazed fraction, slim
       mullions, nose / back glazing, the rooms -> ids by distance, <= 25);
    G6 (base) nothing but the haul ropes in the hosted entry tower's provisional keep-out, and
       (revision 1) the REAL SkiLiftTowerETGDO LOD0 / LOD1 clear of the station (gd_tower_seam);
    G7 the sign on the portal face (R1e) - reported here with its numbers;
    G8 the real SkiGondolaCabin8A swept through the hall clears every non-machine part by
       >= GD_CLEAR (gd_cabin_sweep);
    G9 / G10 revision 1: the 3 x 4 lot, the part clash and the vanilla footprints (gd_fit)."""
    res, nums = {}, {}
    X, xn, xb = p.x_wheel, p.x_nose, p.x_back
    wi = p.w_half - WALL_T
    gtar = float(p.grip_above_rope) if p.grip_above_rope else 0.0
    under = p.z_rope - p.hang
    # G1
    m, lab, dz = gd_portal_margin(p)
    P = GD_PORTAL
    res["G1"] = (f"portals: cabin silhouette opening u {P['u_lo']}..{P['u_hi']} (slot "
                 f"{P['s_lo']}..{P['s_hi']} to z {P['z_slot']}) holds every carrier box + {GD_CLEAR} "
                 f"at both pins; smallest margin beyond that {m:.3f} m ({lab}, pin +{dz})")
    nums["portal"] = {"outline_lane_uz": [list(q) for q in gd_portal_outline()],
                      "clear_m": GD_CLEAR, "margin_beyond_clear_m": round(m, 4),
                      "tightest_box": lab, "tightest_pin_dz": dz,
                      "openings_y": [[round(s * (p.lane_y + P["u_lo"]), 3),
                                      round(s * (p.lane_y + P["u_hi"]), 3)] for s in (-1.0, 1.0)]}
    # G2
    PL = gd_platform_layout(p)
    plat = {}
    for tag, L in sorted(PL.items()):
        v = _pv(me, table, fparts, co, f"platf_{tag}")
        sv = _pv(me, table, fparts, co, f"pstripe_{tag}")
        _near(float(v[:, 2].max()), DECK_H, 1e-4, f"G2 platf_{tag} top")
        _near(float(sv[:, 2].max()), DECK_H, 1e-4, f"G2 pstripe_{tag} top")
        _near(float(np.abs(sv[:, 1]).min()), GD_PLAT_IN, 1e-4, f"G2 pstripe_{tag} edge")
        if GD_PLAT_IN - (p.lane_y + 1.094) < GD_CLEAR - 1e-6:
            raise ContractError("G2 platform edge closer than GD_CLEAR to the cabin's outboard side")
        plat[tag] = {"top_y": DECK_H, "edge_abs_y": GD_PLAT_IN, "stripe_m": GD_STRIPE_W,
                     "board_x": [round(L["x0"], 3), round(L["x_board_end"], 3)],
                     "board_length_m": round(L["x_board_end"] - L["x0"], 3),
                     "walk_x": [round(L["x_board_end"], 3), round(L["x_walk_end"], 3)],
                     "walk_abs_y": [GD_WALK_IN, wi]}
    step_today = round(under - DECK_H, 3)
    res["G2"] = (f"boarding platforms at {DECK_H} = cabin floor (underside {under:.2f}) - "
                 f"{step_today} step (+{gtar} with the rope pin), edge |y| {GD_PLAT_IN} "
                 f"({GD_PLAT_IN - p.lane_y - 1.094:.3f} m outboard of the cabin), boarding "
                 f"length {plat['p']['board_length_m']} m, yellow edge stripe {GD_STRIPE_W} m")
    nums["platforms"] = plat
    nums["cabin_floor"] = {"underside_y_today": round(under, 3),
                           "underside_y_rope_pin": round(under + gtar, 3),
                           "platform_y": DECK_H, "step_in_m": step_today}
    # G3 walkable routes from grade
    routes = []
    ap = _pv(me, table, fparts, co, "apron_")
    _near(float(ap[:, 2].min()), 0.0, 1e-4, "G3 apron foot")
    routes.append({"surface": "floor (cabin trough)", "y": GD_FLOOR_Z, "from": "grade",
                   "by": f"the portal mouths: the openings reach grade, {GD_FLOOR_Z} m apron step"})
    xd = X - 3.0
    # checker F1: every door is a REAL opening - rays through the opening (3 across x 3 up),
    # from 0.30 m outside the wall to 0.30 m inside it, meet nothing but the door's own parts
    openings = gd_door_openings(p, me, table, fparts, co)
    for tag, L in sorted(PL.items()):
        st = _pv(me, table, fparts, co, f"stepside_{tag}")
        _near(float(st[:, 2].max()), DECK_H, 1e-4, f"G3 ramp stepside_{tag} top")
        dl = _pv(me, table, fparts, co, f"door_{tag}_leaf")
        _near(float(dl[:, 0].mean()), xd, 0.01, f"G3 door_{tag} x")
        dw_, dh_ = GD_SIDE_DOOR if GD_REV1 else (GD_DOOR_W, GD_DOOR_H)
        if not (L["x0"] <= xd - dw_ / 2.0 and xd + dw_ / 2.0 <= L["x_board_end"]):
            raise ContractError(f"G3 door_{tag} at x {xd} opens off its platform")
        ramp = (f"a {GD_RAMP_L:.2f} m ramp stepside_{tag} (0 -> {DECK_H}, under a canopy)" if GD_REV1
                else f"ramp stepside_{tag} (0 -> {DECK_H})")
        routes.append({"surface": f"platf_{tag} (boarding)", "y": DECK_H, "from": "grade",
                       "by": f"{ramp} + {'double ' if GD_REV1 else ''}side door at x {xd:.2f} (a "
                             f"{dw_} x {dh_} m opening cut through the wall, sill = the "
                             f"platform top)"})
    so = gd_opr_side(p)                       # nitpick A2: the room's side; the stair is on -so
    otag, stag = ("p", "n") if so > 0 else ("n", "p")
    of = _pv(me, table, fparts, co, "opr_floor")
    _near(float(of[:, 2].max()), DECK_H, 1e-4, "G3 opr_floor top")
    od = _pv(me, table, fparts, co, "door_opr_leaf")
    if not (GD_WALK_IN - 1e-3 <= float((so * od[:, 1]).min()) and float((so * od[:, 1]).max()) <= wi + 1e-3):
        raise ContractError(f"G3 the operator room door does not open onto the {'+' if so > 0 else '-'}Y walkway")
    _near(PL[otag]["x_walk_end"], X + GD_OPR_X0, 1e-6, "G3 walkway -> operator room")
    routes.append({"surface": "opr_floor (operator room)", "y": DECK_H, "from": f"platf_{otag}",
                   "by": f"walkway |y| >= 4.90 past the turn -> door on the room's -X wall (a "
                         f"{GD_DOOR_W} x {GD_DOOR_H} m opening cut through it)"})
    S = gd_stair_layout(p)
    fy = so                                   # world y -> the stair-as-built (-Y) frame and back (its own inverse)
    r1, g1, r2, g2 = S["rise1"], S["going1"], S["rise2"], S["going2"]
    pitch = max(math.degrees(math.atan2(r1, g1)), math.degrees(math.atan2(r2, g2)))
    if max(r1, r2) > 0.21 + 1e-9 or min(g1, g2) < 0.20 - 1e-9 or pitch > 50.0:
        raise ContractError(f"G4 stair risers {r1:.3f}/{r2:.3f}, goings {g1:.3f}/{g2:.3f}, "
                            f"pitch {pitch:.1f} deg")
    _near(PL[stag]["x_walk_end"], S["x0"], 1e-6, "G3 walkway -> stair foot")
    t1 = _pv(me, table, fparts, co, "stread_1_01")
    _near(float(t1[:, 2].max()), S["z0"] + r1, 1e-4, "G3 first tread")
    tl = _pv(me, table, fparts, co, f"stread_1_{S['n'] - 1:02d}")
    _near(float(tl[:, 2].max()), S["zl"] - r1, 1e-4, "G3 flight 1 last tread")
    lg = _pv(me, table, fparts, co, "grate_stl")
    _near(float(lg[:, 2].max()), S["zl"], 1e-4, "G3 landing")
    t2 = _pv(me, table, fparts, co, f"stread_2_{S['n'] - 1:02d}")
    _near(float(t2[:, 2].max()), S["zt"] - r2, 1e-4, "G3 flight 2 last tread")
    _near(float((fy * t2[:, 1]).max()), S["yb"], 1e-4, "G3 flight 2 ends at the deck edge")
    dk = _pv(me, table, fparts, co, "grate_dk")
    _near(float(dk[:, 2].max()), GD_DECK_Z, 1e-4, "G3 deck top")
    db = _pv(me, table, fparts, co, "grate_dkb")
    _near(float((fy * db[:, 1]).min()), S["yb"], 1e-3, "G3 deck b meets flight 2")
    routes.append({"surface": "grate_stl (stair landing)", "y": S["zl"], "from": f"platf_{stag}",
                   "by": f"stair flight 1 ({S['n']} risers of {r1:.3f}, goings {g1:.3f}) from "
                         f"the {'-' if so > 0 else '+'}Y walkway end x {S['x0']:.2f}"})
    routes.append({"surface": "grate_dka / grate_dkb (machinery deck)", "y": GD_DECK_Z,
                   "from": "grate_stl", "by": f"stair flight 2 ({S['n']} risers of {r2:.3f}, "
                                              f"goings {g2:.3f}) to the deck edge y {fy * S['yb']}"})
    # stair headroom: a ray up from every tread / the landing centre to the first other part
    own = ("stread_", "stringer_", "stpost", "rail_st", "post_st", "grate_stl")
    hb, _np = _part_bvh(me, table, fparts, co, lambda n: not n.startswith(own))
    head = math.inf
    for k in range(1, S["n"]):
        for x_, y_, z_ in (((S["x0"] + (k - 0.5) * g1), (S["y_wall"] + S["ya"]) / 2.0, S["z0"] + k * r1),
                           ((S["x1"] + S["xbw"]) / 2.0, S["ya"] + (k - 0.5) * g2, S["zl"] + k * r2)):
            loc, _n, _i, d = hb.ray_cast(Vector((x_, fy * y_, z_ + 0.01)), Vector((0.0, 0.0, 1.0)), 10.0)
            head = min(head, d if loc is not None else 10.0)
    if head < CLIMB_HEADROOM:
        raise ContractError(f"G4 stair headroom {head:.2f} m < {CLIMB_HEADROOM}")
    nums["stair"] = {k: (round(v, 4) if isinstance(v, float) else v) for k, v in S.items()}
    nums["stair"]["pitch_deg"] = round(pitch, 2)
    nums["stair"]["headroom_min_m"] = round(head, 3)
    # the roof route: nose ladder from a z 0 footing (DQ Revision 2 / 1c) + roof ladder
    L = gd_ladder_layout(p)
    ring = cs2_validate.ground_ring_report(t, table=table)
    foot_ok = int(ring["ok_parts"].get("lfoot_nz", 0))
    fv = _pv(me, table, fparts, co, "lfoot_nz")
    if abs(float(fv[:, 2].min())) > 1e-6 or foot_ok < 4:
        raise ContractError(f"G3 nose ladder footing lfoot_nz min z {fv[:, 2].min():.4f}, "
                            f"{foot_ok} OK engine-base edges (want z 0, >= 4)")
    rv = _pv(me, table, fparts, co, "lrail_nz")
    if rv[:, 2].min() > NZ_FOOT_H + 1e-3 or rv[:, 2].max() < L["z_rim"] + 1.0:
        raise ContractError(f"G3 nose ladder rails z {rv[:, 2].min():.2f}..{rv[:, 2].max():.2f}: "
                            f"want footing top {NZ_FOOT_H} .. roof rim {L['z_rim']:.2f} + 1.0")
    rg = L["rungs"]
    if rg[0] > 0.40 + 1e-9 or rg[-1] < L["z_rim"] - NZ_PITCH or \
            any(abs((b - a) - NZ_PITCH) > 1e-6 for a, b in zip(rg, rg[1:])):
        raise ContractError(f"G3 nose ladder rungs {rg[0]}..{rg[-1]} off the 0.30 grid / rim")
    rr = L["roof_rungs"]
    xr = sum(L["roof_x"]) / 2.0
    gap = math.dist((L["x"], L["y"], rg[-1]), (xr, rr[0][0], rr[0][1]))
    if gap > 0.90 or abs(rr[-1][0]) > 0.60:
        raise ContractError(f"G3 roof ladder: first rung {gap:.2f} m from the nose ladder's top "
                            f"rung (<= 0.90), last rung at |y| {abs(rr[-1][0]):.2f} (<= 0.60)")
    routes.append({"surface": "roof crown (wind mast)", "y": round(p.z_crown, 3), "from": "grade",
                   "by": f"lfoot_nz (z 0) -> nose ladder on the portal face y {L['y']} "
                         f"({len(rg)} rungs 0.35 -> {rg[-1]:.2f}, rails to {L['z_top']:.2f}) -> roof "
                         f"ladder over the curve ({len(rr)} rungs, ends |y| {abs(rr[-1][0]):.2f})"})
    nums["walkable"] = routes
    nums["doors"] = openings
    if GD_REV1:
        # revision 1: the shallow roof (<= 14.5 deg) is walkable from the roof ladder it carries
        routes.append({"surface": "roof shell (shallow standing seam, 0 -> 14.5 deg)",
                       "y": round(p.z_top, 3), "from": "roof crown",
                       "by": "steps off the roof ladder (it lies on the roof); snow guards along the eaves"})
    if GD_REV1:
        # DQ follow-up (rev-1 checker F4): the standable-top census - every flat top a person could
        # stand on is on a route above or DECLARED (GD_NOT_WALKABLE, with its reason); else G3 fails
        tops = gd_flat_tops(me, table, fparts, co)
        undeclared = sorted(n for n in tops if not n.startswith(GD_WALKABLE)
                            and not n.startswith(tuple(GD_NOT_WALKABLE)))
        if undeclared:
            raise ContractError(f"G3 standable flat tops neither on a route nor declared: "
                                f"{[(n, tops[n]) for n in undeclared[:6]]}")
        used = sorted({k for k in GD_NOT_WALKABLE for n in tops if n.startswith(k)})
        nums["not_walkable"] = [{"part": n, "area_m2": v[0], "min_side_m": v[1], "top_y": v[2],
                                 "why": next(GD_NOT_WALKABLE[k] for k in GD_NOT_WALKABLE if n.startswith(k))}
                                for n, v in sorted(tops.items()) if not n.startswith(GD_WALKABLE)]
        nums["standable_census"] = {"rule": f"up faces (n.z >= 0.98) above 0.10 m, >= {GD_STAND_AREA} m2 and "
                                            f">= {GD_STAND_MIN} m both ways per part",
                                    "walkable": sorted(n for n in tops if n.startswith(GD_WALKABLE)),
                                    "declared": used}
    else:
        nums["not_walkable"] = ["opr_roof (under the deck, no access needed)",
                                "roof shell off the crown (curved standing seam, reached only by the roof ladder)"]
    res["G3"] = (f"{len(routes)} walkable surfaces, each reached from grade: "
                 + "; ".join(f"{r['surface']} <- {r['from']}" for r in routes)
                 + (f"; standable-top census: {len(nums['not_walkable'])} tops declared not walkable "
                    f"({', '.join(x['part'] for x in nums['not_walkable'])}), none undeclared" if GD_REV1 else ""))
    # G4 climbing room of the nose ladder (the DQ Revision 1c gate, CLIMB_MIN)
    z1 = min(L["z_top"], rg[-1] + CLIMB_HEAD)
    hits, room = climb_room(me, table, fparts, co, p, ("lrail_nz", "lrung_nz", "lfoot_nz",
                                                         "lstand_nz"),
                            (L["x"], L["y"]), (-1.0, 0.0), 0.28 + 0.03, NZ_FOOT_H, z1, CLIMB_MIN)
    if hits or room["fixed"][0] < CLIMB_MIN - 1e-9 or room["chairs"][0] < CLIMB_MIN - 1e-9:
        raise ContractError(f"G4 nose ladder climbing room < {CLIMB_MIN} m over z "
                            f"{NZ_FOOT_H}..{z1:.2f}: {hits[:6]} (fixed {room['fixed']}, "
                            f"cabins {room['chairs']})")
    res["G4"] = (f"nose ladder climbing room {room['fixed'][0]:.2f} m to fixed objects, "
                 f"{room['chairs'][0]:.2f} m to the cabins (gate {CLIMB_MIN}); stair pitch "
                 f"{pitch:.1f} deg, risers {r1:.3f} / {r2:.3f}, headroom {head:.2f} m")
    nums["front_ladder"] = {"nose_ladder": {"x": round(L["x"], 4), "y": L["y"],
                                            "z_foot": L["z_foot"], "z_top": round(L["z_top"], 4),
                                            "z_rim": round(L["z_rim"], 4), "rungs": len(rg),
                                            "rung_z": [rg[0], rg[-1]], "foot": [round(v, 4) for v in L["foot"]],
                                            "foot_ok_edges": foot_ok, "climb_room": room},
                            "roof_ladder": {"x": [round(v, 4) for v in L["roof_x"]],
                                            "rungs": len(rr), "first": list(rr[0]), "last": list(rr[-1]),
                                            "first_rung_gap_m": round(gap, 3)}}
    # G5 revision 1: the long glazed hall
    res["G5"], nums["glazing"] = gd_glazing(t, me, table, fparts, co, p)
    # G6 hosted-tower keep-out (base)
    if p.hosted_tower:
        k = GD_TOWER_KEEPOUT
        ix = _vidx(me, table, fparts, lambda n: not n.startswith("haulrope_"))
        V = co[ix]
        bad = (V[:, 0] < xn - k["dx"]) & (np.abs(V[:, 1]) <= k["y"]) & (V[:, 2] < k["z"])
        if bad.any():
            raise ContractError(f"G6 {int(bad.sum())} station vertices in the hosted tower's "
                                f"keep-out (x < {xn - k['dx']}, |y| <= {k['y']}, z < {k['z']})")
        # revision 1: the entry tower EXISTS now (SkiLiftTowerETGDO, towers/O) - its real LOD0 / LOD1
        # placed as hosted must clear the revised hall (the S8 seam, gondola side)
        seam = gd_tower_seam(t, table, p)
        nums["tower_seam"] = seam
        sm = ("tower not exported" if seam is None else
              "real SkiLiftTowerETGDO clear (no overlap; gap " + ", ".join(
                  f"{k_} {v_['min_gap_m']} m to {v_['nearest']}" for k_, v_ in sorted(seam.items())) + ")")
        res["G6"] = (f"hosted entry tower keep-out x < {xn - k['dx']:.2f}, |y| <= {k['y']}, "
                     f"z < {k['z']}: only the haul ropes; {sm}")
        nums["tower_keepout_authoring"] = {"x_max": round(xn - k["dx"], 3), "abs_y_max": k["y"],
                                           "z_max": k["z"]}
    else:
        seam = None
    # G7 sign on the portal face (an explicit contract since detail pass 1: the portal-face sign
    # sits on the face between the slot tops and the nose band, and nothing covers any sign -
    # rays from 1 m in front of each sign quad meet the sign first)
    lay = sign_layout(plate_digits(p))
    zc = gd_sign_nose_z(p)
    v = _pv(me, table, fparts, co, "signband_n")
    _near(float((v[:, 2].min() + v[:, 2].max()) / 2.0), zc, 1e-3, "G7 signband_n centre z")
    # revision 1: the sign spandrel under the nose ring beam (draft 1: under the nose band)
    band_lo = p.z_top - GD_NOSE_HEAD_DROP if GD_REV1 else p.z_top - 1.0
    if v[:, 2].max() > band_lo - 0.02 + 1e-6 or v[:, 2].min() < GD_PORTAL["z_slot"] + 0.05 - 1e-6:
        raise ContractError(f"G7 portal-face sign z {v[:, 2].min():.3f}..{v[:, 2].max():.3f} not between the "
                            f"slot tops + 0.05 ({GD_PORTAL['z_slot'] + 0.05:.2f}) and the nose band / ring beam "
                            f"- 0.02 ({band_lo - 0.02:.2f})")
    covered = gd_sign_occlusion(me, table, fparts, co, p)
    if covered:
        raise ContractError(f"G7 signs covered: {covered[:6]}")
    res["G7"] = (f"sign on the PORTAL face (centre y 0, z {zc}) between the slot tops and the nose "
                 f"{'ring beam' if GD_REV1 else 'band'} + both sides, {lay['W']:.3f} x {lay['H']:.3f} m, "
                 f"emblem + vector '{lay['digits']}' at {SIGN_PX_PER_M:.0f} px/m "
                 f"({'p and q share rect n' if GD_REV1 else 'side q shares rect p'}); 3 x 25 rays "
                 f"from 1 m in front meet each sign first")
    # G8 the real cabin
    sw = gd_cabin_sweep(t, table, p)
    if sw["min_clear_m"] < GD_CLEAR - 1e-6:
        raise ContractError(f"G8 SkiGondolaCabin8A sweep: {sw['min_clear_m']:.3f} m < {GD_CLEAR} "
                            f"to {sw['nearest_part']} at {sw['at']} ({sw['pin']}, {sw['how']})")
    res["G8"] = (f"real SkiGondolaCabin8A swept ({sw['placements']} poses, both pins): min "
                 f"clearance {sw['min_clear_m']:.3f} m to {sw['nearest_part']} at {sw['at']} "
                 f"({sw['pin']}) >= {GD_CLEAR}")
    nums["cabin_sweep"] = sw
    if GD_REV1:
        rf, fnums = gd_fit(t, me, table, fparts, co, p, seam)
        res.update(rf)
        nums["fit"] = fnums
    return res, nums


def assert_nose_train(me, table, fparts, co, p):
    """ST1 (nitpick A, 2026-10-13; top stations with NOSE_TRAIN): the nose rope guides are the towers' sheave train,
    measured on the built faces (AUTHORING frame): per lane NOSE_TRAIN_N rims, each rim on its head's axle rule
    (nitpick A3, NOSE_AXLE_ON_LANE): DQ = the rim CENTRE on the lane (rim z mid = z_rope, 1 mm; the rim top r above
    it, 2 mm: the code lane / modelled rope through the sheave centres, as on every tower), GD = the rim's top on the
    rope's underside (axle + r = z_rope - ROPE_R, 2 mm: the modelled rope stays tangent); every rim centred on its lane
    (1 mm); every carrier part (rockers, pins, yoke, links, hanger, girder stub) INBOARD of its lane's sheave inner face
    (the towers' F9 rule: the carrier hanger passes outboard); the whole train and the beam at x >= anchor_x - 0.10 (the
    frozen prefab z min) and the haul rope over every sheave. Nitpick A4 (NOSE_HANGER_AT_YOKE, DQ): each hanger box stands
    at its yoke's x (centre, 1 mm) and inside its girder stub's x span - cs2_sheaves.dq_train's tower-centred x 0 left
    them 11 m behind the yokes. Returns ({"ST1": msg}, numbers)."""
    T = nose_train_layout(p)
    pre = NOSE_TRAIN_PREFIX
    names = sorted(n for n in table if n.startswith(pre) or n.startswith("guideframe_"))
    if not names:
        raise ContractError("ST1: no nose train parts")
    on_lane = bool(T["axle_on_lane"])
    rope_bot = p.z_rope - ROPE_R
    rim_top = p.z_rope + T["r"] if on_lane else rope_bot           # where every rim top must sit
    worst_t, worst_lat, n_rims, worst_in = 0.0, 0.0, {}, -1.0
    xr = {}                                                         # core -> (x min, x max) of the hanger / yoke / girder
    for nm in names:
        v = _pv(me, table, fparts, co, nm)
        if float(v[:, 0].min()) < T["x_out"] - 1e-4:
            raise ContractError(f"ST1: {nm} reaches x {v[:, 0].min():.4f} < {T['x_out']} (the frozen bounds' z min)")
        core = nm[len(pre):] if nm.startswith(pre) else nm
        side = 1.0 if float(v[:, 1].mean()) > 0.0 else -1.0
        if core.startswith("sheave_") and core.endswith("_rim"):
            n_rims[side] = n_rims.get(side, 0) + 1
            dt = abs(float(v[:, 2].max()) - rim_top)
            worst_t = max(worst_t, dt)
            if on_lane:
                zc = float((v[:, 2].min() + v[:, 2].max()) / 2.0)
                worst_t = max(worst_t, abs(zc - p.z_rope))
                if abs(zc - p.z_rope) > 1e-3 or dt > 0.002:
                    raise ContractError(f"ST1: {nm} centre z {zc:.4f} / top {v[:, 2].max():.4f} vs the lane "
                                        f"{p.z_rope:.4f} / {rim_top:.4f} (axle not on the lane)")
            elif T.get("hold_down"):            # T27: the rim bottom on the vanilla rope's top (axle - r)
                dt = abs(float(v[:, 2].min()) - (T["axle"] - T["r"]))
                worst_t = max(worst_t, dt)
                if dt > 0.002:
                    raise ContractError(f"ST1: {nm} bottom {v[:, 2].min():.4f} vs the hold-down datum "
                                        f"{T['axle'] - T['r']:.4f} (rim not on the rope)")
            elif dt > 0.002:
                raise ContractError(f"ST1: {nm} top {v[:, 2].max():.4f} vs the rope underside {rope_bot:.4f} "
                                    f"(rope not tangent)")
            yc = float((v[:, 1].min() + v[:, 1].max()) / 2.0)
            worst_lat = max(worst_lat, abs(yc - side * p.lane_y))
            if abs(yc - side * p.lane_y) > 1e-3:
                raise ContractError(f"ST1: {nm} centred at y {yc:.4f}, lane {side * p.lane_y}")
        elif not core.startswith(("sheave_", "liner_")):
            if core.startswith(("hanger_", "yoke_", "girder_")):
                xr[core] = (float(v[:, 0].min()), float(v[:, 0].max()))
            # the towers' F9 rule: every carrier on the INBOARD half of its lane (the pins end <= lane - 0.039 on
            # the DQ head, lane - 0.064 on the GD head); the outboard half stays free for the carrier's hanger
            lim = p.lane_y - NOSE_CARRIER_IN
            far = float((side * v[:, 1]).max())
            worst_in = max(worst_in, far - p.lane_y)
            if far > lim + 1e-4:
                raise ContractError(f"ST1: carrier {nm} reaches |y| {far:.4f} past lane - {NOSE_CARRIER_IN} = {lim:.4f} "
                                    f"(the hanger side: carriers stay inboard)")
    if n_rims.get(-1.0) != T["n"] or n_rims.get(1.0) != T["n"]:
        raise ContractError(f"ST1: rims per lane {n_rims}, want {T['n']}")
    worst_hx = 0.0
    if T["hanger_at_yoke"]:
        for tag in ("n", "p"):
            h, y_, g = xr.get(f"hanger_{tag}"), xr.get(f"yoke_{tag}"), xr.get(f"girder_{tag}")
            if h is None or y_ is None or g is None:
                raise ContractError(f"ST1: lane {tag} lacks a hanger / yoke / girder stub")
            hc, yc = (h[0] + h[1]) / 2.0, (y_[0] + y_[1]) / 2.0
            worst_hx = max(worst_hx, abs(hc - yc))
            if abs(hc - yc) > 1e-3 or h[0] < g[0] - 1e-4 or h[1] > g[1] + 1e-4:
                raise ContractError(f"ST1: hanger_{tag} centre x {hc:.4f} vs its yoke {yc:.4f} (girder stub x "
                                    f"{g[0]:.3f}..{g[1]:.3f}): the hanger is not at its yoke")
    for s, tag in ((-1.0, "n"), (1.0, "p")):
        r = _pv(me, table, fparts, co, f"haulrope_{tag}")
        if float(r[:, 0].min()) > T["x0"] - 1.5 * T["pitch"] + 1e-3:
            raise ContractError(f"ST1: haulrope_{tag} starts at x {r[:, 0].min():.3f}, past the outer sheave")
    tris = sum(1 for i, fp in enumerate(fparts) if table[fp - 1].startswith((pre, "guideframe_"))
               for _ in range(len(me.polygons[i].vertices) - 2))
    errk = "axle_on_lane_err_m" if on_lane else "tangent_err_m"
    nums = dict(nose_train_numbers(p), lateral_err_m=round(worst_lat, 5),
                carrier_max_dy_from_lane_m=round(worst_in, 4), parts=len(names), tris_lod0=tris)
    nums[errk] = round(worst_t, 5)
    how = (f"axle on the lane {p.z_rope} (rope through the sheave centres, the towers' convention)" if on_lane
           else "rope tangent on the rims")
    msg = (f"{T['family']} head x {T['n']} per lane, {how} (err {worst_t * 1000:.2f} mm), carriers "
           f"<= lane {worst_in:+.3f} (gate -{NOSE_CARRIER_IN}), x >= {T['x_out']}, {len(names)} parts / {tris} tris")
    if T["family"] == "DQ":
        # nitpick A4 (DQ only: the gondola top's manifest keeps its keys): where the hanger boxes and the beams stand
        bm = T["beam"]
        nums.update(hanger_at_yoke=bool(T["hanger_at_yoke"]), hanger_x=T["hanger_x"],
                    hanger_x_err_m=round(worst_hx, 5) if T["hanger_at_yoke"] else None,
                    guide_beam_abs_y=[round(bm["y0"], 4), round(bm["y1"], 4)],
                    guide_beam_z=[round(bm["z0"], 4), round(bm["z1"], 4)])
        msg += (f"; hangers at their yokes x {T['hanger_x']:.3f} (err {worst_hx * 1000:.2f} mm), beams |y| "
                f"{bm['y0']:.2f}..{bm['y1']:.2f}" if T["hanger_at_yoke"] else
                f"; hangers at x {T['hanger_x']} (nitpick-A3 layout), beams |y| {bm['y0']:.2f}..{bm['y1']:.2f}")
    return {"ST1": msg}, nums


# --------------------------------------------------------------------------------------
# C14 (nitpick A4): part-against-part clashes at the DQ top's nose
# --------------------------------------------------------------------------------------
# The tram's C14 idiom (tram_station.check_part_clashes: a BVH triangle-pair test per bbox-overlapping pair; an
# intersecting pair must be a DECLARED contact or an exact KNOWN_OPEN finding, else it fails; a part wholly inside
# another's closed volume fails too), scoped to the parts the nitpick A rise touched: the nose train, the guide
# beams and girder stubs, the modelled haul ropes, and the comm cable / clamps / bracket that run past them. Every
# intersecting pair in that zone must be one of the rules below, each with its reason; anything else raises. Part
# names are matched with the CUT_PIECES suffix removed (haulrope_n_2 -> haulrope_n, soffit_tongue_0 -> soffit_tongue).
# The rope rule is the F10 convention (dq-family-review-geometry F10): the drawn rope runs through the sheave centres,
# so it may cross exactly what the chair may cross - lift_chair.CARRIER_PASS["gantry"] with the station prefix
# (test_detail_detach asserts the two sets are equal).
NOSE_CLASH_SET = ("sheave_guide_", "guideframe_", "haulrope_", "clamp_comm", "commcable_nose", "commbracket_nose")
NOSE_ROPE_PASS = ("sheave_", "liner_", "rocker_sub_", "pivot_n_s", "pivot_p_s")      # = lift_chair.CARRIER_PASS["gantry"]
NOSE_CONTACTS = (
    (r"sheave_guide_pivot_[np]_s\d+", r"sheave_guide_rocker_(?:sub|main)_[np]_\d+",
     "a sub pivot pin runs through its sub rocker and the main rocker (the towers' train)"),
    (r"sheave_guide_pivot_[np]_m\d+", r"sheave_guide_(?:rocker_main_[np]_\d+|yoke_[np])",
     "the main pivot pin runs through the main rocker and the yoke (the towers' train)"),
    (r"sheave_guide_hanger_[np]", r"sheave_guide_yoke_[np]",
     "the hanger's outboard end runs 6 mm into the yoke (cs2_sheaves.dq_train)"),
    (r"sheave_guide_girder_[np]", r"sheave_guide_hanger_[np]",
     "the hanger hangs off the girder stub: a butt joint on coplanar end faces (nitpick A4)"),
    (r"sheave_guide_hanger_[np]", r"sheave_guide_pivot_[np]_m\d+",
     "the main pivot pin's top touches the hanger's underside, tangent (the towers' train)"),
    (r"sheave_guide_liner_[np]_\d+", r"sheave_guide_sheave_[np]_\d+_rim",
     "the rubber liner seats on its rim: touching faces, welded by the join (the towers' train)"),
    (r"sheave_guide_sheave_hub[np]_\d+", r"sheave_guide_sheave_[np]_\d+_rim",
     "T62 round 3: the seen hub boss (cs2_sheaves.hub_name part) stands on its own wheel's disc"),
    (r"haulrope_[np]", r"sheave_guide_(?:" + "|".join(NOSE_ROPE_PASS) + r")\w*",
     "F10: the drawn rope runs through the sheave centres, through the liners and the sub pins' ends"),
    (r"haulrope_[np]", r"wheelrope", "the rope tube ends inside the bullwheel's rope torus (C1 / C2)"),
    (r"guideframe_[np]", r"band_nose_c", "the guide beam's end sockets 3 cm into the nose band's edge"),
    (r"clamp_comm\d+_[lu]", r"commcable_nose", "a cable clamp closes round its cable"),
    (r"commbracket_nose", r"commcable_nose", "the cable leaves the bracket's face"),
    (r"band_nose_c", r"commbracket_nose", "the comm bracket sockets into the nose band"),
    (r"cabinet_comm_lid", r"commcable_nose", "the cable lands on the junction box's lid"),
)
# Exact pairs that intersect and are NOT designed (listed on every build, like tram_station.KNOWN_OPEN): the comm
# cable's drop-to-run corner is filleted by cs2_kit.tube_along with f = half the shorter span (1.365 m here), which
# swings the cable diagonally through the nose band, its lining and the soffit tongue (hidden inside them, 8-10 triangle
# pairs each). Pre-existing since the first DQ top (not an A3 / A4 change); owner build_comm; an open finding.
NOSE_KNOWN_OPEN = {
    ("band_nose_c", "commcable_nose"): "comm cable corner fillet through the nose band",
    ("bandlining_nose_c", "commcable_nose"): "comm cable corner fillet through the band lining",
    ("commcable_nose", "soffit_tongue"): "comm cable corner fillet through the soffit tongue",
}


def _clash_name(n):
    import re as _re
    m = _re.fullmatch(r"(.+)_(\d+)", n)
    return m.group(1) if (m and m.group(1) in CUT_PIECES) else n


def nose_clash_scan(me, table, fparts, co):
    """The C14 scan on a joined mesh (AUTHORING frame): {"names", "nose", "pairs": {(a, b): triangle-pair count},
    "box_pairs", "contained": [(inner, outer)]} for every nose-zone part (NOSE_CLASH_SET) against every
    bbox-overlapping part."""
    polys = {}
    for i, pl in enumerate(me.polygons):
        polys.setdefault(table[int(fparts[i]) - 1], []).append(tuple(int(v) for v in pl.vertices))
    names = sorted(polys)
    box, trees, closed = {}, {}, {}
    for n in names:
        v = co[sorted({k for pl in polys[n] for k in pl})]
        box[n] = (v.min(axis=0), v.max(axis=0))

    def tree(n):
        if n not in trees:
            ids = sorted({k for pl in polys[n] for k in pl})
            rm = {k: j for j, k in enumerate(ids)}
            tr = [(rm[pl[0]], rm[pl[j]], rm[pl[j + 1]]) for pl in polys[n] for j in range(1, len(pl) - 1)]
            trees[n] = BVHTree.FromPolygons([tuple(float(c) for c in co[k]) for k in ids], tr, all_triangles=True)
        return trees[n]

    def is_closed(n):
        if n not in closed:
            cnt = {}
            for pl in polys[n]:
                for j in range(len(pl)):
                    e = tuple(sorted((pl[j], pl[(j + 1) % len(pl)])))
                    cnt[e] = cnt.get(e, 0) + 1
            closed[n] = bool(cnt) and all(c == 2 for c in cnt.values())
        return closed[n]

    dirs = [Vector(d).normalized() for d in ((0.37, 0.56, 0.74), (-0.61, 0.29, 0.73), (0.23, -0.81, 0.54))]

    def inside(outer, pt):
        votes = 0
        for d in dirs:
            o, hits = Vector(pt), 0
            for _ in range(256):
                hit, _nrm, _i, _d = tree(outer).ray_cast(o, d)
                if hit is None:
                    break
                hits += 1
                o = hit + d * 1e-5
            votes += hits % 2
        return votes >= 2

    nose = [n for n in names if n.startswith(NOSE_CLASH_SET)]
    pairs, contained, n_box = {}, [], 0
    for a in nose:
        for b in names:
            if b == a or (b in nose and b < a):
                continue
            if np.any(box[a][0] > box[b][1] + 1e-6) or np.any(box[a][1] < box[b][0] - 1e-6):
                continue
            n_box += 1
            ov = tree(a).overlap(tree(b))
            if ov:
                pairs[tuple(sorted((a, b)))] = len(ov)
                continue
            for inner, outer in ((a, b), (b, a)):      # no triangle pair: is one wholly inside the other's volume?
                if (np.all(box[inner][0] >= box[outer][0] - 1e-6) and np.all(box[inner][1] <= box[outer][1] + 1e-6)
                        and is_closed(outer) and inside(outer, co[polys[inner][0][0]])):
                    contained.append((inner, outer))
    return {"names": names, "nose": nose, "pairs": pairs, "box_pairs": n_box, "contained": contained}


def assert_nose_clash(me, table, fparts, co, p):
    """C14 (nitpick A4; the DQ top, NOSE_TRAIN): no undeclared part-against-part clash in the nose zone. Returns
    ({"C14": msg}, numbers): every intersecting pair must match a NOSE_CONTACTS rule or be an exact NOSE_KNOWN_OPEN
    finding; no nose part may sit wholly inside another part's closed volume."""
    import re as _re
    scan = nose_clash_scan(me, table, fparts, co)
    rules = [(_re.compile(a), _re.compile(b), why) for a, b, why in NOSE_CONTACTS]
    declared, known, undeclared = {}, [], []
    for (a, b), cnt in sorted(scan["pairs"].items()):
        ca, cb = _clash_name(a), _clash_name(b)
        why = next((w for ra, rb, w in rules if (ra.fullmatch(ca) and rb.fullmatch(cb))
                    or (ra.fullmatch(cb) and rb.fullmatch(ca))), None)
        if why is not None:
            declared[why] = declared.get(why, 0) + 1
        elif tuple(sorted((ca, cb))) in NOSE_KNOWN_OPEN:
            known.append(f"{a} | {b}: {NOSE_KNOWN_OPEN[tuple(sorted((ca, cb)))]}")
        else:
            undeclared.append((a, b, cnt))
    msgs = []
    if undeclared:
        msgs.append(f"{len(undeclared)} undeclared nose part clashes (part | part: triangle pairs): "
                    + ", ".join(f"{a} | {b}: {c}" for a, b, c in undeclared[:12]))
    if scan["contained"]:
        msgs.append("nose parts inside another part's closed volume: "
                    + ", ".join(f"{a} in {b}" for a, b in scan["contained"][:12]))
    if msgs:
        raise ContractError("C14 " + "; ".join(msgs))
    nums = {"scope": list(NOSE_CLASH_SET), "nose_parts": len(scan["nose"]), "box_pairs_tested": scan["box_pairs"],
            "intersecting_pairs": len(scan["pairs"]), "declared_pairs": sum(declared.values()),
            "declared_by_rule": dict(sorted(declared.items())), "known_open": known, "undeclared": 0, "contained": 0,
            "rope_pass_set": list(NOSE_ROPE_PASS)}
    msg = (f"{len(scan['nose'])} nose-zone parts vs {scan['box_pairs']} bbox-overlapping partners: "
           f"{len(scan['pairs'])} intersecting pairs = {sum(declared.values())} declared ({len(declared)} rules) + "
           f"{len(known)} known-open (comm cable corner fillet, pre-existing), 0 undeclared, 0 contained")
    return {"C14": msg}, nums


def assert_orientation_dq(me, table, fparts, co, p):
    """O1 (nitpick A, 2026-10-13; Richard twenty-fifth look S-1): the station is laid out round ONE up lane
    (D1_TRAVEL[kind]["lane"]: the base LOADS the departing chairs on +Y, the top UNLOADS the arriving chairs on -Y
    - the same physical lane, the right one looking uphill in this AUTHORING frame) and everything a rider or the
    liftie uses sits on that lane's side: the operator cabin (centroid y sign), the hanging lane boards (centroid y
    sign, facing the riders' travel), the load carpet + load line (base) / the unload stripe (top). Measured on the
    built faces. Returns ({"O1": msg}, numbers)."""
    tv = D1_TRAVEL[p.kind]
    L = float(tv["lane"])
    if OP_SIDE_Y[p.kind] != L:
        raise ContractError(f"O1: OP_SIDE_Y[{p.kind}] {OP_SIDE_Y[p.kind]} != the up lane {L}")

    def cy(prefix):
        v = _pv(me, table, fparts, co, prefix)
        return float((v[:, 1].min() + v[:, 1].max()) / 2.0)
    got = {"cab_walls": cy("cab_walls")}
    for nm, _x, s_, f_ in D1_HANG_BOARDS[p.kind]:
        want_f = "+X" if tv["dir"] < 0 else "-X"
        if s_ != L or f_ != want_f:
            raise ContractError(f"O1: hang board {nm} on side {s_} facing {f_}; the up lane {L}, riders face {want_f}")
        got[f"board_{nm}"] = cy(f"board_{nm}")
    for pre in (("carpet_ld", "stripe_load") if p.kind == "base" else ("stripe_unload",)):
        got[pre] = cy(pre)
    bad = {k: round(v, 3) for k, v in got.items() if v * L <= 0.0}
    if bad:
        raise ContractError(f"O1: not on the up lane's side (y sign {L:+.0f}): {bad}")
    nums = {"up_lane_y_authoring": L, "rider_travel_x": tv["dir"],
            "measured_centre_y": {k: round(v, 3) for k, v in sorted(got.items())},
            "cab": {k: v for k, v in cab_layout(p).items() if k in ("x0", "x1", "y0", "y1", "zf")},
            "frame": "authoring (x travel, -X = line; y lateral); A rows are written game x = -y (cs2_liftdress."
                     "blender_to_game; x-sign gate settled N, 2026-10-15 playtest, S1b-N applied 2026-10-16)",
            "spin": "the up lane is the RIGHT one looking uphill in this frame (base +Y loads, top -Y unloads); "
                    "StationAlign.UpLaneIsSecond puts chain A on the right looking uphill IN GAME: the same lane "
                    "under branch N, the mirror under branch P (S1b then flips the rule's sign, not the meshes)"}
    msg = (f"up lane y {L:+.0f}: cabin, hang boards ({', '.join(b[0] for b in D1_HANG_BOARDS[p.kind])}) and the "
           f"{'load carpet + line' if p.kind == 'base' else 'unload stripe'} all on it")
    return {"O1": msg}, nums


def assert_orientation_gd(me, table, fparts, co, p):
    """G11 (nitpick A2, 2026-10-13; the DQ stations' O1 for the gondola): the operator room stands on the station's
    UP lane (GD_UP_LANE[kind]: the base's departing lane +Y, the top's ARRIVING lane -Y - the right one looking uphill
    in this AUTHORING frame) - measured on the built faces: the room's floor, walls, ceiling slab, inner glazing and
    door leaf, LIFT OPS (board + label, the label facing -X toward the walkway), the room's `_Win` panes when they are
    in the mesh, the stair on the OTHER side; and in the rows: the operator-door WallLight and the hose reel (kit row
    with FINE_DETAIL, else the part) on the room's side. Returns ({"G11": msg}, numbers)."""
    L = float(GD_UP_LANE[p.kind])
    so = gd_opr_side(p)
    if so != L:
        raise ContractError(f"G11: the operator room's side {so:+.0f} != the {p.kind}'s up lane {L:+.0f} "
                            f"({'departing' if p.kind == 'base' else 'arriving'} lane)")

    def cy(prefix):
        v = _pv(me, table, fparts, co, prefix)
        return float((v[:, 1].min() + v[:, 1].max()) / 2.0)
    got = {n: cy(n) for n in ("opr_floor", "opr_walls", "opr_roof", "oprglass", "door_opr_leaf", "board_ops",
                              "label_ops")}
    if any(n.startswith("win_opr_") for n in table):
        got["win_opr_"] = cy("win_opr_")
    bad = {k: round(v, 3) for k, v in got.items() if v * L <= 0.0}
    st = cy("stread_")
    if st * L >= 0.0:
        bad["stread_ (the stair: the other side)"] = round(st, 3)
    li = [i for i, fp in enumerate(fparts) if table[fp - 1].startswith("label_ops")]
    nx = float(np.mean([me.polygons[i].normal.x for i in li])) if li else 0.0
    if nx > -0.9:
        bad["label_ops normal x (want -X, toward the walkway)"] = round(nx, 3)
    L_ = gd_hall_layout(p)
    lights = [r for r in gd_subobjects(p) if r[0].startswith("WallLight") and abs(r[1][0] - (L_["ox0"] - 0.02)) < 1e-6]
    if len(lights) != 1:
        raise ContractError(f"G11: {len(lights)} operator-door WallLight rows (want 1)")
    rows = {"WallLight (operator door)": float(lights[0][1][1])}
    reel = [s for s in fd_kit_specs(p) if s["prop"] == "SkiDressHoseReel"]
    if reel:
        rows["SkiDressHoseReel (kit row)"] = float(reel[0]["pos"][1])
    else:
        rows["hosereel_pl_ (part)"] = cy("hosereel_pl_")
    bad.update({k: round(v, 3) for k, v in rows.items() if v * L <= 0.0})
    if bad:
        raise ContractError(f"G11: not on the up lane's side (y sign {L:+.0f}, {p.kind}): {bad}")
    # nitpick A3 (ruling F2): WAIT HERE at the LOADING entry - the departing lane's side (GD_LOAD_SIDE +Y on both
    # stations), board + label centred there and the label facing the skiers coming in through that entry
    wl = float(GD_LOAD_SIDE)
    wait = {n: cy(n) for n in ("board_waitn", "label_waitn")}
    lw = [i for i, fp in enumerate(fparts) if table[fp - 1].startswith("label_waitn")]
    ny = float(np.mean([me.polygons[i].normal.y for i in lw])) if lw else 0.0
    badw = {k: round(v, 3) for k, v in wait.items() if v * wl <= 0.0}
    if ny * wl < 0.9:
        badw[f"label_waitn normal y (want {wl:+.0f}, toward the loading entry)"] = round(ny, 3)
    if badw:
        raise ContractError(f"G11: WAIT HERE not at the loading entry (y sign {wl:+.0f}, {p.kind}): {badw}")
    nums = {"up_lane_y_authoring": L, "operator_room_side_y": so, "stair_side_y": -so,
            "measured_centre_y": {k: round(v, 3) for k, v in sorted(got.items())},
            "stair_centre_y": round(st, 3), "label_ops_normal_x": round(nx, 3),
            "rows_y": {k: round(v, 3) for k, v in sorted(rows.items())},
            "wait_here": {"loading_side_y": wl, "centre_y": {k: round(v, 3) for k, v in sorted(wait.items())},
                          "label_normal_y": round(ny, 3)},
            "frame": "authoring (x travel, -X = line; y lateral); gondola rows are written game x = -y "
                     "(cs2_liftdress.blender_to_game; x-sign gate settled N, 2026-10-15, S1b-N applied 2026-10-16)",
            "spin": "the up lane is the RIGHT one looking uphill in this frame (the base departs on +Y, the top's "
                    "cabins arrive on -Y), the DQ stations' O1 rule; nitpick A2 moved the top's room from +Y"}
    msg = (f"operator room, LIFT OPS, door light + hose reel rows on the up lane y {L:+.0f} "
           f"({'departing' if p.kind == 'base' else 'arriving'}), the stair on {-L:+.0f}; WAIT HERE at the loading "
           f"entry y {wl:+.0f}")
    return {"G11": msg}, nums


def assert_contracts(t, table, p):
    """C1-C3, C6-C10, C13 in the AUTHORING frame (before orient_for_net)."""
    me = t.data
    fparts = _face_parts(me)
    co = _co(me)
    res = {}
    # C1 wheel
    w = _pv(me, table, fparts, co, "wheelrope")
    cen = w.mean(axis=0)
    for a, b, k in ((cen[0], p.x_wheel, "x"), (cen[1], 0.0, "y"), (cen[2], p.z_wheel, "z")):
        _near(a, b, 1e-3, f"C1 wheelrope centroid {k}")
    _near(float(np.hypot(w[:, 0] - p.x_wheel, w[:, 1]).mean()), p.torus_r, 0.005,
          "C1 torus major radius")
    rim = _pv(me, table, fparts, co, "sheave_bull_rim")
    _near(float(np.hypot(rim[:, 0] - p.x_wheel, rim[:, 1]).max()), p.rim_r, 1e-3,
          "C1 rim max radial")
    res["C1"] = f"wheel ({p.x_wheel}, 0, {p.z_wheel}), torus {p.torus_r}, rim {p.rim_r}"
    # C2 gauge / rope
    for s, tag in ((-1.0, "n"), (1.0, "p")):
        r = _pv(me, table, fparts, co, f"haulrope_{tag}")
        _near(float(r[:, 1].mean()), s * p.lane_y, 1e-3, f"C2 haulrope_{tag} y")
        if ROPE_ON_CROWN:      # T27: the rope runs from anchor_z at the anchor to z_rope at the wheel
            _near(float(r[r[:, 0].argmin(), 2]), anchor_z(p), ROPE_R + 1e-3, f"C2 haulrope_{tag} anchor z")
            _near(float(r[r[:, 0].argmax(), 2]), p.z_rope, ROPE_R + 1e-3, f"C2 haulrope_{tag} wheel z")
        else:
            _near(float(r[:, 2].mean()), p.z_rope, 1e-3, f"C2 haulrope_{tag} z")
        _near(float(r[:, 0].min()), p.anchor_x, 0.01, f"C2 haulrope_{tag} anchor end")
        _near(float(r[:, 0].max()), p.x_wheel, 0.01, f"C2 haulrope_{tag} wheel end")
    res["C2"] = f"lanes +-{p.lane_y} at z {p.z_rope}, x {p.anchor_x} -> {p.x_wheel}"
    # C3 canopy
    roof = _pv(me, table, fparts, co, "roof_")
    _near(float(roof[:, 2].max()), p.z_crown, 0.01, "C3 crown")
    _near(float(np.abs(roof[:, 1]).max()), p.chord_half, 1e-3, "C3 eave half-chord")
    if not p.gondola:
        _near(float(_pv(me, table, fparts, co, "soffit_")[:, 2].min()), p.z_soffit, 1e-3,
              "C3 soffit")
        _near(float(np.abs(_pv(me, table, fparts, co, "band_")[:, 1]).max()), p.w_half, 1e-3,
              "C3 band half-width")
    else:
        _near(float(np.abs(_pv(me, table, fparts, co, "wall_")[:, 1]).max()), p.w_half, 1e-3,
              "C3 wall half-width")
    res["C3"] = (f"crown {p.z_crown:.3f}, eave +-{p.chord_half}"
                 + ("" if p.gondola else f", soffit {p.z_soffit}, band +-{p.w_half}"))
    # C6 lot fit: travel within the lot + LOT_TOL; lateral within the lot line itself (F8)
    lot, lot_half = (GD_LOT, GD_LOT_HALF) if (p.gondola and GD_REV1) else (LOT, LOT_HALF)
    if np.abs(co[:, 0]).max() > lot_half[0] + LOT_TOL or np.abs(co[:, 1]).max() > lot_half[1] + 1e-4:
        raise ContractError(f"C6 lot fit: x {co[:, 0].min():.2f}..{co[:, 0].max():.2f}, "
                            f"y {co[:, 1].min():.2f}..{co[:, 1].max():.2f}")
    res["C6"] = (f"x {co[:, 0].min():.2f}..{co[:, 0].max():.2f}, "
                 f"y {co[:, 1].min():.2f}..{co[:, 1].max():.2f} within lot {lot}")
    # C7 corridor (DQ pedestal zone)
    if not p.gondola:
        ix = _vidx(me, table, fparts, lambda n: n.startswith(C7_PREFIXES))
        v = co[ix]
        v = v[v[:, 0] < p.x_wheel]
        worst = float(np.abs(v[:, 1]).max())
        if worst > CORRIDOR + 1e-3:
            raise ContractError(f"C7 corridor: |y| {worst:.3f} > {CORRIDOR}")
        res["C7"] = f"corridor max |y| {worst:.3f} <= {CORRIDOR}"
    # C8 chair clearance
    n = check_clearance(t, table, p)
    res["C8"] = f"{n} envelope placements clear (dz 0" + \
        (f" and +{p.grip_above_rope}" if p.grip_above_rope else "") + ")"
    # C9 crease safety
    if not p.gondola:
        a = set(_vidx(me, table, fparts, lambda n: n.startswith("roof_")).tolist())
        b = set(_vidx(me, table, fparts, lambda n: n.startswith("band_")).tolist())
        if a & b:
            raise ContractError(f"C9: roof_ and band_ share {len(a & b)} vertices")
        res["C9"] = "roof_ / band_ share no vertex"
    # C10 name lint happens in main (needs the name)
    # C13 comm bracket
    bk = _pv(me, table, fparts, co, "commbracket_")
    bc = (bk.min(axis=0) + bk.max(axis=0)) / 2.0
    want = comm_bracket_centre(p)
    for i, k in enumerate("xyz"):
        _near(float(bc[i]), want[i], 1e-3, f"C13 comm bracket {k}")
    res["C13"] = f"bracket centre {tuple(round(v, 3) for v in want)}"
    # nitpick A (2026-10-13): ST1 the towers' sheave train at the top's nose; O1 the DQ layout round its up lane
    st_nums = or_nums = None
    if p.kind == "top" and NOSE_TRAIN:
        r_st, st_nums = assert_nose_train(me, table, fparts, co, p)
        res.update(r_st)
        if not p.gondola and not NOSE_A4_LEGACY:
            # nitpick A4: C14 no undeclared part clash in the nose zone (the A3 check 1d pairs); the pitched-lane limit
            r_c14, c14_nums = assert_nose_clash(me, table, fparts, co, p)
            res.update(r_c14)
            st_nums["part_clash"] = c14_nums
            st_nums["lane_pitch_limit"] = NOSE_LANE_PITCH_LIMIT
    if not p.gondola and DETAIL1:
        r_or, or_nums = assert_orientation_dq(me, table, fparts, co, p)
        res.update(r_or)
    if p.gondola and GD_REV1:
        # nitpick A2 (2026-10-13): G11 the gondola operator room on the up lane (manifest block "orientation")
        r_or, or_nums = assert_orientation_gd(me, table, fparts, co, p)
        res.update(r_or)
    res.update(assert_revision1(me, table, fparts, co, p))
    if p.gondola:
        r2, gnums = assert_gondola(t, me, table, fparts, co, p)
        numbers = gnums["front_ladder"]
    else:
        r2, numbers = assert_revision2(t, me, table, fparts, co, p)
        gnums = None
        if DETAIL1:
            # detail pass 1 (MC3 / MC4 / MC5 / MC10): one reachability rule on every DQ ladder,
            # the dressing and the vanilla footprints box-tested
            ra, anums = assert_access_d1(t, me, table, fparts, co, p)
            rd, dnums = assert_detail1(t, me, table, fparts, co, p)
            # DQ follow-up: D11 the board order along the chair's travel
            ro, onums = assert_board_order(me, table, fparts, co, p)
            r2.update(ra)
            r2.update(rd)
            r2.update(ro)
            if numbers is not None:
                numbers = dict(numbers)
                numbers["detail1_access"] = anums
                numbers["detail1_dressing"] = dnums
                numbers["detail1_board_order"] = onums
    fnums = None
    if FINE_DETAIL:
        # fine-detail step 3b: F1 the dressing kit's rows on the real kit meshes
        rf, fnums = assert_kit_rows(t, me, table, fparts, co, p)
        r2.update(rf)
    res.update(r2)
    for k in sorted(res):
        print(f"[contract] {k} ok: {res[k]}")
    if numbers is not None:
        res["_front_ladder"] = numbers          # main moves it to the manifest (not a contract)
    if gnums is not None:
        res["_gondola"] = gnums                 # gondola draft 1: manifest block "gondola"
    if fnums is not None:
        res["_fine"] = fnums                    # fine-detail step 3b: manifest block "fine_detail"
    if st_nums is not None:
        res["_sheaves"] = st_nums               # nitpick A: manifest block "sheave_trains"
    if or_nums is not None:
        res["_orientation"] = or_nums           # nitpick A: manifest block "orientation"
    return res


# --------------------------------------------------------------------------------------
# Sign band painter (Revision 1 (e); MUST-CHANGE 6 kept: one stamp per side, no mirroring)
# --------------------------------------------------------------------------------------

_EMBLEM = {}


def emblem_alpha(px):
    """The shipped launcher glyph (c) (peak + snowflake, ski_launcher_glyph.cand_c) as
    px x px coverage, row 0 = top, rendered AT the destination size (signed-distance
    coverage, 4x4 supersampled: no resampling, crisp at any atlas)."""
    if px not in _EMBLEM:
        import ski_launcher_glyph as slg
        _EMBLEM[px] = np.asarray(slg.render_alpha(slg.cand_c(), int(px), ss=4), dtype=np.float32)
    return _EMBLEM[px]


_DIGIT_TRIS = {}
_DIGIT_COV = {}


def digit_triangles(lay):
    """Revision 1b: the plate number as VECTOR outlines. welcome_sign.text_triangles (a bpy
    FONT curve in the builtin Bfont -> mesh -> triangles; the civic sign header's path,
    reused, never copied) at a cap height of lay["digit_h_m"], shrunk only if the string is
    wider than the text box, centred in the box. Returns {"tris": triangles ((s, t) x 3) in
    sign metres, "bbox": (s0, s1, t0, t1), "area_m2", "w_m", "h_m", "scale", "n_tris"};
    cached per layout (the FONT object lives for a few ms, before the bake: R1e runs first
    and make_hook warms the cache)."""
    tx0, tx1, ty0, ty1 = lay["text"]
    key = (lay["digits"], round(lay["digit_h_m"], 9), round(tx0, 9), round(tx1, 9),
           round(ty0, 9), round(ty1, 9))
    if key not in _DIGIT_TRIS:
        import welcome_sign  # lazy: only the sign painter and R1e need the FONT path
        tris, w = welcome_sign.text_triangles(lay["digits"], lay["digit_h_m"])
        k = min(1.0, (tx1 - tx0) / w)
        cs, ct = 0.5 * (tx0 + tx1), 0.5 * (ty0 + ty1)
        out = tuple(tuple((cs + k * x, ct + k * y) for x, y in tri) for tri in tris)
        pts = np.array([q for tri in out for q in tri], dtype=np.float64)
        area = sum(0.5 * abs((b[0] - a[0]) * (c[1] - a[1]) - (c[0] - a[0]) * (b[1] - a[1]))
                   for a, b, c in out)
        lo, hi = pts.min(axis=0), pts.max(axis=0)
        _DIGIT_TRIS[key] = {"tris": out, "bbox": (float(lo[0]), float(hi[0]), float(lo[1]),
                                                  float(hi[1])),
                            "area_m2": float(area), "w_m": float(hi[0] - lo[0]),
                            "h_m": float(hi[1] - lo[1]), "scale": k, "n_tris": len(out)}
    return _DIGIT_TRIS[key]


def digit_coverage(lay, w, h, pu, pv, ss=DIGIT_SS):
    """Revision 1b: (h, w) float32 coverage of the vector digits over one sign rect (rows
    bottom-up = +t, cols = +s, texel j spans s j/pu .. (j+1)/pu), anti-aliased by ss x ss
    point samples per texel (welcome_sign._tri_coverage) on the texel sub-rect round the
    glyph bbox only. Cached per (digits, rect, density); returns a copy."""
    key = (lay["digits"], int(w), int(h), round(float(pu), 6), round(float(pv), 6), int(ss))
    if key not in _DIGIT_COV:
        import welcome_sign
        g = digit_triangles(lay)
        s0, s1, t0, t1 = g["bbox"]
        j0, j1 = max(0, int(math.floor(s0 * pu)) - 1), min(w, int(math.ceil(s1 * pu)) + 1)
        i0, i1 = max(0, int(math.floor(t0 * pv)) - 1), min(h, int(math.ceil(t1 * pv)) + 1)
        sc, tc = 0.5 * (j0 + j1) / pu, 0.5 * (i0 + i1) / pv
        tris = [tuple((x - sc, y - tc) for x, y in tri) for tri in g["tris"]]
        cov = np.zeros((h, w), dtype=np.float32)
        cov[i0:i1, j0:j1] = welcome_sign._tri_coverage(tris, j1 - j0, i1 - i0, pu, pv, ss=ss)
        _DIGIT_COV[key] = cov
    return _DIGIT_COV[key].copy()


def sign_relief(lay, h, w, pu, pv):
    """Revision 1b plate edge over one sign rect (rows bottom-up = +t, cols = +s):
    (height (h, w) metres, occ (h, w)). Height: a rounded bevel SIGN_BEVEL_M wide and
    SIGN_BEVEL_H_M high round the whole panel (profile 1 - (1 - d / bevel)^2, steepest at the
    edge, flat inside), a pressed bead (cosine, SIGN_BEAD_*) round the emblem panel, and the
    number plate PLATE_STEP_H_M proud of the panel on its own PLATE_STEP_M edge. occ: the
    emblem panel's soft edge occlusion, 1 - SIGN_EDGE_AO at its
    border (the outer edges and the plate's step) rising (smoothstep) to 1 at SIGN_EDGE_AO_M
    inside; 1 on the plate (its colours stay exact)."""
    S, T = np.meshgrid((np.arange(w) + 0.5) / pu, (np.arange(h) + 0.5) / pv)
    W, H = lay["W"], lay["H"]
    ps0, ps1, pt0, pt1 = lay["plate"]

    def roll(d, width):
        x = np.clip(d / width, 0.0, 1.0)
        return 1.0 - (1.0 - x) ** 2

    d_out = np.minimum(np.minimum(S, W - S), np.minimum(T, H - T))
    height = SIGN_BEVEL_H_M * roll(d_out, SIGN_BEVEL_M)
    on_plate = (S >= ps0) & (S <= ps1)
    d_pl = np.minimum(np.minimum(S - ps0, ps1 - S), np.minimum(T - pt0, pt1 - T))
    height = height + np.where(on_plate, PLATE_STEP_H_M * roll(d_pl, PLATE_STEP_M), 0.0)
    d_em = np.minimum(np.minimum(S, ps0 - S), np.minimum(T, H - T))
    xb = (d_em - SIGN_BEAD_INSET_M) / (0.5 * SIGN_BEAD_W_M)
    bead = np.where(np.abs(xb) < 1.0, 0.5 * (1.0 + np.cos(np.pi * xb)), 0.0)
    height = height + np.where(on_plate, 0.0, SIGN_BEAD_H_M * bead)
    x = np.clip(d_em / SIGN_EDGE_AO_M, 0.0, 1.0)
    occ = np.where(on_plate, 1.0, 1.0 - SIGN_EDGE_AO * (1.0 - x * x * (3.0 - 2.0 * x)))
    return height.astype(np.float32), occ.astype(np.float32)


def paint_sign(base_sl, ctl_sl, lay):
    """Paint one sign panel into its texel rect (rows bottom-up = +v = +Z, cols = viewer
    right): the emblem panel's edge occlusion (sign_relief) over the baked slot-3 band, the
    emblem in MARK_WHITE over it (ControlMask B faded out under it by coverage), the plate
    light with a dark border and the VECTOR digits dark, mixed by their anti-aliased
    coverage in linear light (Revision 1b), ControlMask 0 over the whole plate (never
    tinted). Returns the texel facts for the manifest / asserts."""
    h, w = base_sl.shape[:2]
    pu, pv = w / lay["W"], h / lay["H"]
    # Revision 1b plate edge: the emblem panel darkens softly toward its border (occ = 1 on
    # the plate); the glyph is painted over it, so its full-coverage texels stay exact
    _height, occ = sign_relief(lay, h, w, pu, pv)
    base_sl[..., 0:3] *= occ[..., None]
    # emblem: rendered at its own texel size into the left square
    E = int(round(lay["H"] * pv))
    cov = emblem_alpha(E)[::-1, :]                      # rows bottom-up
    ew = min(E, w)
    cov = cov[:h, :ew]
    view = base_sl[:cov.shape[0], :ew, 0:3]
    view[...] = view * (1.0 - cov[..., None]) + np.array(MARK_WHITE, np.float32) * cov[..., None]
    for ch in (0, 2):
        ctl_sl[:cov.shape[0], :ew, ch] *= (1.0 - cov)
    # plate
    ps0, ps1, pt0, pt1 = lay["plate"]
    tx0, tx1, ty0, ty1 = lay["text"]
    sc = (np.arange(w) + 0.5) / pu                      # texel centres, metres
    tc = (np.arange(h) + 0.5) / pv
    S, T = np.meshgrid(sc, tc)                          # (h, w)
    plate = (S >= ps0) & (S <= ps1)
    border = plate & ((S - ps0 < PLATE_BORDER) | (ps1 - S < PLATE_BORDER)
                      | (T - pt0 < PLATE_BORDER) | (pt1 - T < PLATE_BORDER))
    hu, hv = 0.5 / pu, 0.5 / pv                         # texels that OVERLAP the text box
    box = (S + hu >= tx0) & (S - hu <= tx1) & (T + hv >= ty0) & (T - hv <= ty1)
    g = digit_triangles(lay)
    dcov = digit_coverage(lay, w, h, pu, pv)
    light = np.array(_lin(PLATE_LIGHT_SRGB), np.float32)
    dark = np.array(_lin(PLATE_DARK_SRGB), np.float32)
    field = plate & ~border
    c = dcov[field][:, None]
    base_sl[plate, 0:3] = light
    base_sl[field, 0:3] = light * (1.0 - c) + dark * c
    base_sl[border, 0:3] = dark
    ctl_sl[plate, 0:3] = 0.0
    part = dcov[(dcov > 0.0) & (dcov < 1.0)]
    pc = np.nonzero(plate[0])[0]
    cov_sum = float(dcov.sum())
    expected = g["area_m2"] * pu * pv
    return {"px_per_m": round(float(min(pu, pv)), 2), "px_per_m_uv": [round(pu, 2), round(pv, 2)],
            "emblem_px": E, "emblem_texels": int((cov > 0.5).sum()),
            "plate_cols": [int(pc.min()), int(pc.max()) + 1] if pc.size else None,
            "plate_texels": int(plate.sum()),
            "digit_texels": round(cov_sum, 1), "digit_texels_expected": round(expected, 1),
            "digit_coverage_ratio": round(cov_sum / expected, 4) if expected > 0 else 0.0,
            "digit_full_texels": int((dcov >= 1.0).sum()), "digit_aa_texels": int(part.size),
            "digit_aa_levels": int(np.unique(np.round(part * DIGIT_SS * DIGIT_SS)).size),
            "digit_outside_box": round(float(dcov[~(box & field)].sum()), 4),
            "digit_font": DIGIT_FONT, "digit_ss": DIGIT_SS, "digit_tris": g["n_tris"],
            "digit_px_h": round(g["h_m"] * pv, 1),
            "digit_h_m": round(g["h_m"], 4), "digit_w_m": round(g["w_m"], 4),
            "digit_box_m": [round(tx1 - tx0, 4), round(ty1 - ty0, 4)],
            "relief": {"bevel_m": SIGN_BEVEL_M, "bevel_h_m": SIGN_BEVEL_H_M,
                       "plate_step_m": PLATE_STEP_M, "plate_step_h_m": PLATE_STEP_H_M,
                       "bead_inset_m": SIGN_BEAD_INSET_M, "bead_w_m": SIGN_BEAD_W_M,
                       "bead_h_m": SIGN_BEAD_H_M,
                       "edge_ao": SIGN_EDGE_AO, "edge_ao_m": SIGN_EDGE_AO_M,
                       "occ_min": round(float(occ.min()), 4)},
            "digits": lay["digits"]}


def check_sign_uv(me, table, prefix):
    """The stamped sign reads correctly only if UVUnique u runs to the viewer's right and v
    up on the panel (pinned in the reserved rect at seed orientation; pack_unique never
    rotates). Asserted per side; raises otherwise."""
    faces = cs2_compose.faces_of_prefix(me, table, prefix)
    if len(faces) != 1:
        raise ContractError(f"{prefix}: expected one face, got {len(faces)}")
    poly = me.polygons[faces[0]]
    uv = me.uv_layers[cs2_uv.UNIQUE_LAYER].data
    ls = list(poly.loop_indices)[:3]
    P = [np.array(me.vertices[me.loops[l].vertex_index].co, dtype=np.float64) for l in ls]
    Q = [np.array(uv[l].uv, dtype=np.float64) for l in ls]
    E = np.stack([P[1] - P[0], P[2] - P[0]], axis=1)
    Qm = np.stack([Q[1] - Q[0], Q[2] - Q[0]], axis=1)
    J = E @ np.linalg.inv(Qm)
    du, dv = J[:, 0] / np.linalg.norm(J[:, 0]), J[:, 1] / np.linalg.norm(J[:, 1])
    n = np.array(poly.normal, dtype=np.float64)
    right = np.cross((0.0, 0.0, 1.0), n)
    right /= np.linalg.norm(right)
    if du @ right < 0.99 or dv[2] < 0.99:
        raise ContractError(f"{prefix}: UV frame u.right {du @ right:.3f}, v.up {dv[2]:.3f} - "
                            f"the stamp would read mirrored/rotated")
    return float(du @ right), float(dv[2])


def stamp_signbands(base, ctl, me, table, p):
    """Revision 1 (e): one stamp per side into its RESERVED rect. Asserts (raise -> nothing
    ships): the face's UVUnique bbox IS the reserved rect (pinned, density-protected), the
    UV frame reads unmirrored, px/m >= SIGN_MIN_X_OLD x the pre-verdict band (scaled with
    the atlas for smoke bakes), digits >= 0.5 m, the digit texels >= 80 % of expected.
    Revision 1b (vector digits): expected = the outline area x (px/m)^2, and the coverage
    sum must also stay <= 120 % of it; the edge shows >= DIGIT_AA_LEVELS_MIN anti-aliasing
    levels; no coverage outside the text box; the vector glyph itself >= 0.5 m tall."""
    A = base.shape[0]
    lay = sign_layout(plate_digits(p))
    rects = sign_rects(p)
    floor_ppm = round(SIGN_MIN_X_OLD * SIGN_OLD_PX_PER_M * A / float(ATLAS), 2)
    if lay["digit_h_m"] < 0.5 - 1e-9:
        raise ContractError(f"sign digits {lay['digit_h_m']} m tall < 0.5 m")
    out = {}
    for tag in sorted(rects):                   # the OWNED rects (detail pass 1: n only on the DQ)
        prefix = f"signband_{tag}"
        check_sign_uv(me, table, prefix)
        u0, v0, u1, v1 = rects[tag]
        want = (int(round(v0 * A)), int(round(v1 * A)), int(round(u0 * A)), int(round(u1 * A)))
        info = {}

        def painter(rect, base_sl, ctl_sl, info=info):
            info["rect"] = [int(v) for v in rect]
            info.update(paint_sign(base_sl, ctl_sl, lay))

        cs2_compose.stamp_on_face(base, ctl, me, table, prefix, painter)
        bad = []
        if not info:
            bad.append("not painted")
        else:
            if tuple(info["rect"]) != want:
                bad.append(f"rect {info['rect']} != reserved {list(want)} (not pinned)")
            if info["px_per_m"] < floor_ppm:
                bad.append(f"{info['px_per_m']} px/m < {floor_ppm}")
            if info["digit_texels"] < 0.8 * info["digit_texels_expected"]:
                bad.append(f"digit texels {info['digit_texels']} < 80 % of "
                           f"{info['digit_texels_expected']}")
            if info["digit_texels"] > 1.2 * info["digit_texels_expected"]:
                bad.append(f"digit texels {info['digit_texels']} > 120 % of "
                           f"{info['digit_texels_expected']}")
            if info["digit_aa_levels"] < DIGIT_AA_LEVELS_MIN:
                bad.append(f"{info['digit_aa_levels']} anti-aliasing levels < "
                           f"{DIGIT_AA_LEVELS_MIN} (not a vector edge)")
            if info["digit_outside_box"] > 0.0:
                bad.append(f"digit coverage {info['digit_outside_box']} outside the text box")
            if info["digit_h_m"] < 0.5 - 1e-9:
                bad.append(f"vector digits {info['digit_h_m']} m tall < 0.5 m")
        if bad:
            raise ContractError(f"{prefix}: sign stamp {bad}")
        info["px_per_m_min"] = floor_ppm
        info["reserved_uv"] = [round(v, 8) for v in rects[tag]]
        out[prefix] = info
    if sign_shared(p):
        # gondola draft 1: the -Y side sign shares the +Y sign's reserved rect (SIGN_SHARED);
        # detail pass 1 (DQ): the +Y sign shares the -Y sign's (DQ_SIGN_SHARED) - pinned on
        # exactly the same texels, read unmirrored on its own side, stamped and relieved ONCE
        for q, src in sorted(sign_shared(p).items()):
            check_sign_uv(me, table, f"signband_{q}")
            r_q = cs2_compose.uv_bbox_px(me, table, f"signband_{q}", A)
            if tuple(int(v) for v in r_q) != tuple(out[f"signband_{src}"]["rect"]):
                raise ContractError(f"signband_{q}: rect {r_q} != signband_{src}'s "
                                    f"{out[f'signband_{src}']['rect']} (not sharing)")
            have = out[f"signband_{src}"].get("shared_with")
            if have is None:                    # one sharer (DQ, gondola draft 1): a string
                out[f"signband_{src}"]["shared_with"] = f"signband_{q}"
            else:                               # gondola revision 1: p and q both on n -> a list
                out[f"signband_{src}"]["shared_with"] = sorted(
                    ([have] if isinstance(have, str) else list(have)) + [f"signband_{q}"])
    return out


def relief_signbands(nrm, stamps, p):
    """Revision 1b plate edge in the Normal map, per stamped side: sign_relief's height
    through cs2_compose.height_to_normal at the rect's own px/m, RNM-blended over the baked
    (flat) sign normal and renormalised. The tangent frame is the stamp's (u -> viewer
    right, v -> up: check_sign_uv), so G > 0.5 on the top bevel (OpenGL, lit from above).
    Asserts (raise -> nothing ships): the edge tilts >= 10 deg somewhere, the emblem panel's
    field away from every edge stays flat (< 0.5 deg). Records the numbers per side."""
    lay = sign_layout(plate_digits(p))
    for prefix, info in sorted(stamps.items()):
        r0, r1, c0, c1 = info["rect"]
        h, w = r1 - r0, c1 - c0
        pu, pv = w / lay["W"], h / lay["H"]
        height, _occ = sign_relief(lay, h, w, pu, pv)
        sl = nrm[r0:r1, c0:c1]
        det = cs2_compose.height_to_normal(height, 0.5 * (pu + pv))
        n = cs2_compose.blend_rnm(sl[..., 0:3], det) * 2.0 - 1.0
        n /= np.maximum(np.linalg.norm(n, axis=-1, keepdims=True), 1e-6)
        sl[..., 0:3] = n * 0.5 + 0.5
        tilt = np.degrees(np.arccos(np.clip(n[..., 2], -1.0, 1.0)))
        S, T = np.meshgrid((np.arange(w) + 0.5) / pu, (np.arange(h) + 0.5) / pv)
        ps0 = lay["plate"][0]
        d_em = np.minimum(np.minimum(S, ps0 - S), np.minimum(T, lay["H"] - T))
        field = d_em > (max(SIGN_BEVEL_M, SIGN_BEAD_INSET_M + 0.5 * SIGN_BEAD_W_M)
                        + 2.0 / min(pu, pv))
        tmax = float(tilt.max())
        tin = float(tilt[field].max()) if field.any() else 0.0
        if tmax < 10.0 or tin > 0.5:
            raise ContractError(f"{prefix}: plate-edge relief tilt max {tmax:.2f} deg (want "
                                f">= 10), field {tin:.2f} deg (want < 0.5)")
        info["relief"].update({"tilt_max_deg": round(tmax, 2), "tilt_field_deg": round(tin, 3),
                               "tilted_texels": int((tilt > 2.0).sum())})


def make_hook(t, table, p):
    """post_compose = lift_terminal.make_post_compose (density report, rasterised relief
    with THIS generator's detail_specs, compose_maps, normal renormalise) with the sign
    strip as its reserved rects (compose_maps keeps those texels exactly as baked: no part
    noise, relief or gloss variation under the sign), then the two sign stamps and
    (Revision 1b) their plate-edge relief in the Normal map. The vector digit outlines are
    built here, before the bake, so no FONT object is created inside it.
    Detail pass 1 (DQ): the reserved rects are the sign rect + the label sheet (label_rects), the
    relief table is detail_specs_dq, and the dressing labels are painted (stamp_labels) after the
    sign; the compose log's drawn / skipped feature counts are kept (hook.detail_stats)."""
    inner = lift_terminal.make_post_compose(t, list(label_rects(p).values()))
    digit_triangles(sign_layout(plate_digits(p)))
    specs = specs_for(p)
    labels = d1_labels(p)
    if labels:
        _LD().stem_ratio()                      # build the Bfont outlines before the bake

    def hook(base, nrm, msk, ctl, ao, mesh, A):
        with _lt_override(detail_specs=specs):
            base, nrm, msk, ctl = inner(base, nrm, msk, ctl, ao, mesh, A)
        hook.detail_stats = json.loads(json.dumps(cs2_compose.LAST_DETAIL_STATS))
        hook.stamps = stamp_signbands(base, ctl, mesh, table, p)
        relief_signbands(nrm, hook.stamps, p)
        if labels:
            hook.labels = stamp_labels(base, ctl, mesh, table, p)
        hook.ctl_scaled = ctl_8bit_exclusive(ctl)
        return base, nrm, msk, ctl

    hook.inner = inner
    hook.stamps = None
    hook.labels = None
    hook.detail_stats = None
    hook.ctl_scaled = None
    return hook


def _awm(vals, wts):
    """Area-weighted median."""
    o = np.argsort(vals, kind="stable")
    v, w = np.asarray(vals)[o], np.asarray(wts)[o]
    c = np.cumsum(w)
    return float(v[int(np.searchsorted(c, 0.5 * c[-1]))])


def _density_class(name):
    """Density / starving-class key of a part: its DQ_HIDDEN prefix (hidden machinery, exempt),
    else its leading letters (+ '_' when the name continues with one)."""
    for h in DQ_HIDDEN:
        if name.startswith(h):
            return h
    k = ""
    for ch in name:
        if not ch.isalpha():
            break
        k += ch
    return k + ("_" if len(name) > len(k) and name[len(k)] == "_" else "")


def density_classes(t, table, A, reserved=()):
    """Shipped texel density (UVUnique at A): the w = 1 density (area-weighted median of px/m /
    density weight, pinned rects excluded), the median, per class the area-weighted RMS px/m with
    its area and weight, and the visible classes under 14 px/m with >= 0.5 m2 (R23 floor)."""
    me = t.data
    lay = me.uv_layers[cs2_uv.UNIQUE_LAYER]
    dens = cs2_uv.face_px_per_m(me, lay, A)
    area = cs2_uv.face_world_areas(me)
    wts = cs2_uv.face_density_weights(me)
    pinned = cs2_uv.pinned_faces(me, lay, list(reserved)) if reserved else set()
    part = _face_parts(me)
    ok = np.isfinite(dens) & (area > 0)
    if pinned:
        ok &= ~np.isin(np.arange(len(dens)), sorted(pinned))
    names = np.array([_density_class(table[q - 1]) if 0 < q <= len(table) else "?" for q in part])
    out = {}
    for k in sorted(set(names.tolist())):
        sel = ok & (names == k)
        a = float(area[sel].sum())
        if a <= 0:
            continue
        out[k] = {"px_per_m": round(math.sqrt(float((area[sel] * dens[sel] ** 2).sum()) / a), 1),
                  "m2": round(a, 2), "w": round(float(np.median(wts[sel])), 2)}
    starving = sorted(k for k, v in out.items() if v["px_per_m"] < 14.0 and v["m2"] >= 0.5
                      and not k.startswith(DQ_HIDDEN))
    return {"w1_px_per_m": round(_awm(dens[ok] / wts[ok], area[ok]), 2),
            "median_px_per_m": round(_awm(dens[ok], area[ok]), 2), "classes": out,
            "starving_lt14": starving}


def has_emissive_map(table):
    """The `_Emissive` map exists only while a MODELLED lamp does (a `lamp_` part; the lodge.py rule, the L3
    precedent): fine-detail DQ stations have none left (the box lamps are RoofLight / WallLight rows), so
    their map was all black (0 lit texels, 5.59 MB raw at 2048) and is dropped (2026-10-12, orchestrator
    ruling on the step-3b open item). The gondola (lamp_eave*) and a DQ `--no-fine` build keep it."""
    return any(n.startswith("lamp_") for n in table)


NO_EMISSIVE_WHY = ("no lamp_ part left: every DQ light is a vanilla sub-object row, so no <name>_Emissive.png is "
                   "baked or shipped and the importer writes no EmissiveProperties (no purpose-72 surgery step)")


def emissive_stats(asset_dir, name, thr=0.02):
    """Lit texels of the shipped _Emissive (any channel > thr) and the flood heads' colour check
    (cool white: mean B >= mean R over the lit texels that are bluer than the warm lamps). No map (the
    fine-detail DQ stations, has_emissive_map False): zero lit texels and the reason, no file read."""
    path = os.path.join(asset_dir, f"{name}_Emissive.png")
    if not os.path.isfile(path):
        return {"map": None, "lit_texels": 0, "cool_texels": 0, "warm_texels": 0, "why": NO_EMISSIVE_WHY}
    em = cs2_validate.load_png_pixels(path)
    rgb = em[..., 0:3]
    lit = rgb.max(axis=-1) > thr
    cool = lit & (rgb[..., 2] >= rgb[..., 0])
    return {"lit_texels": int(lit.sum()), "cool_texels": int(cool.sum()),
            "warm_texels": int((lit & ~cool).sum())}


def detail1_block(t, table, p, A, asset_dir, name, dq_hook, hook, share, report):
    """The manifest block "detail1" (detail pass 1, DQ): density classes + w = 1, the lit emissive
    texels, labels, the weathering ladder and the shipped step, bay sharing, the compose log's
    drawn relief, the vanilla sub-object rows, the dressing's LOD0 triangles."""
    import cs2_weather as CW
    LD = _LD()
    dens = density_classes(t, table, A, reserved=list(label_rects(p).values()))
    em = emissive_stats(asset_dir, name)
    weather = None
    if isinstance(hook, SteppedWeather):
        rep = json.loads(CW.report_json(hook.report))
        weather = {"intensity": hook.chosen, "ladder": hook.ladder, "preset": "station",
                   "slot_wear": LD.STATION_SLOT_WEAR, "snow": 0.0,
                   "vanilla": {k: rep["vanilla"].get(k) for k in
                               ("VL1_mean_rel_dL", "VL2_share_rel_dL_gt_0.15", "VL3_mean_dsmooth",
                                "VL3_max_abs_dsmooth", "VL4_max", "VL5_snow_share", "visible_texels",
                                "ok", "failed")},
                   "guards": rep.get("guards"), "perceptual": rep.get("perceptual"),
                   "coverage": rep.get("coverage"), "sources": rep.get("sources"),
                   "roles": rep.get("roles")}      # no timings: the manifest is deterministic
    me = t.data
    fparts = _face_parts(me)
    tri = np.array([len(pl.vertices) - 2 for pl in me.polygons], dtype=np.int64)
    dress = set(_dress_names(p, table))
    dress_tris = int(sum(int(n) for q, n in zip(fparts, tri) if table[q - 1] in dress))
    block = {
        "spec": ("gondola revision 1 (verdicts-2026-10-11 eighth look: the long glazed hall, 3 x 4 lot, "
                 "bigger skier entries) + the detach detail pass's kit where it fits (labels, weathering, "
                 "vanilla sub-objects); build/gondola-stations-rev1.md" if p.gondola else
                 "design/detail-pass-1-lifts.md sections 3 / 4 / 7 + skeptic MC1-MC11 (detach group)"),
        "density": dens, "emissive": em,
        "labels": dq_hook.labels, "label_ppm": LABEL_PPM,
        "weather": weather,
        "share": ({k: v for k, v in (share.report or {}).items() if k != "group_names"}
                  if share is not None else None),
        "share_groups": ([[c, len(d)] for c, d in (share.report or {}).get("group_names", [])]
                         if share is not None else None),
        "shared_faces": report.get("shared_faces"),
        "detail_stats": dq_hook.detail_stats,
        "sub_objects": subobject_rows(p, name),
        "dress_tris_lod0": dress_tris,
        "lod0_target": (GD_LOD0_TARGET if p.gondola else DETAIL_LOD0_TARGET)[p.kind],
        "batches": 9,
    }
    print(f"[detach] detail1: w1 {dens['w1_px_per_m']} px/m, median {dens['median_px_per_m']}, "
          f"starving {dens['starving_lt14']}; emissive lit {em['lit_texels']} (cool {em['cool_texels']}); "
          f"dressing {dress_tris} tris; shared faces {report.get('shared_faces')}; "
          + (f"weathering {weather['intensity']} ({[(r['intensity'], r['ok']) for r in weather['ladder']]})"
             if weather else "weathering off"))
    return block


def fine_detail_block(p, name, table, fine_nums):
    """The manifest block "fine_detail" (step 3b): the switch, the row frame, the rows by kind, the kit rows
    with their F1 numbers, what left the host (and why), what stays modelled and why."""
    LD = _LD()
    blk = subobject_rows(p, name) or {}
    kit_removed = sorted({pre for s in fd_kit_specs(p) for pre in s["removed"]})
    lamps = ([] if p.gondola else
             [{"part": f"lamp_s{k}", "now": "RoofLightRandom01 row (soffit, the lens centre)"} for k in range(4)]
             + [{"part": "lamp_cab", "now": "the cabin WallLightShortRangeRandom01 row moved onto it (y 6.35, z 2.38)"}]
             + ([{"part": "lamp_eng", "now": "the engine-room WallLightShortRangeRandom01 row moved onto it (y 6.70, "
                                              "z 2.38)"}] if p.kind == "base" else []))
    return {
        "step": "3b 2026-10-12 (build/fine-detail-step3b-stations.md; engine build/fine-detail-step2-engine.md)",
        "flag": True,
        "x_convention": LD.STATION_X_CONVENTION,
        "rows": blk.get("kinds"),
        "kit_rows": (fine_nums or {}).get("rows"),
        "removed_to_kit_rows": kit_removed,
        "lamps_to_vanilla_rows": lamps,
        "lamp_parts_left": sorted(n for n in table if n.startswith("lamp_")),
        **({} if has_emissive_map(table) else {"emissive_map": "dropped 2026-10-12: " + NO_EMISSIVE_WHY}),
        "fine_geometry": (["maze 8-seg capped posts + 6-sided ropes", "load gate 8-seg stiles + 6-sided rails",
                           "board posts 12-seg (SLOW: two 12-seg posts), hang rods 6-seg"] if not p.gondola else
                          ["board posts 12-seg (WAIT HERE)"])
        + ["control cabinets: no finish bevel strips, a 0.02 m lid lip (0.015 overhang on every free side)"],
        "kept_modelled": ["weather mast wx* (FS1: it sets the frozen bounds top)",
                          "LIFT boards + labels in the host atlas at 400 px/m (FS5: no station sign set)"]
        + (["gondola linear soffit lights lamp_eave* (no vanilla linear light)"] if p.gondola else
           ["maze / gate / carpet / stripes / chevrons / gauge (no vanilla match)"])
        + ["control cabinets (ElectricalBox01 / 02 need the S1 Editor GUID harvest; 95cbbe17 rejected)"],
    }


def _dress_names(p, table):
    """Part names that belong to the detail-pass dressing (build_detail families + the hatch)."""
    fams = ("maze_", "gate_", "board_", "label_", "stripe_", "chev_", "carpet_", "ext_",
            "hosereel_", "tool_", "skirack_", "ski_", "estop_", "floodbase_", "floodbrk_", "flood_",
            "lamp_flood", "gauge_", "doorlite_", "sill_", "hatch_sofpf")
    if p.gondola:
        fams = GD_DRESS_FAMS
    return [n for n in table if n.startswith(fams)]


def dq_weather_spec(name, intensity):
    """Detail pass 1 weathering spec (DECISIONS 171 via cs2_liftdress.station_weather_spec:
    preset station, slot_wear free, snow 0, edge band 0.08): DQ_WEATHER (hidden machinery role
    none, the deck top as the ground line) before the dressing's DRESS_WEATHER_OVERRIDES (on an
    equal prefix cs2_weather keeps the first)."""
    LD = _LD()
    w = DQ_WEATHER
    return LD.station_weather_spec(name, ground=w["ground"], ground_z=w["ground_z"],
                                   overrides=tuple(w["overrides"]) + tuple(LD.DRESS_WEATHER_OVERRIDES),
                                   traffic_extra=w["traffic_extra"], intensity=intensity)


def gd_mat_roles():
    """Gondola revision 1 weathering roles by material key: the DQ table (cs2_liftdress.DQ_MAT_ROLES,
    the dressing keys included) + the gondola's own safety stripe (paint)."""
    import cs2_sheaves as S
    return {**_LD().DQ_MAT_ROLES, "stripe": "paint", **S.wheel_roles()}


def gd_weather_spec(name, intensity):
    """Gondola revision 1 weathering spec (DECISIONS 171 via cs2_liftdress.station_weather_spec: preset
    station, slot_wear free, snow 0, edge band 0.08): GD_WEATHER (the hidden machinery and the hall
    lining role none, grade as the ground line - the hall stands on the ground, not on a deck) before
    the dressing's DRESS_WEATHER_OVERRIDES."""
    LD = _LD()
    w = GD_WEATHER
    return LD.station_weather_spec(name, ground=w["ground"], ground_z=w["ground_z"],
                                   overrides=tuple(w["overrides"]) + tuple(LD.DRESS_WEATHER_OVERRIDES),
                                   traffic_extra=w["traffic_extra"], intensity=intensity)


class SteppedWeather:
    """Detail pass 1 weathering (DECISIONS 171: per asset the HIGHEST budget-clean intensity on
    visible texels): a cs2_bake post_compose hook that runs `inner` (make_hook) ONCE, then
    cs2_weather's layer on fresh copies of its maps at each cs2_liftdress.INTENSITY_STEPS step,
    upward, until the vanilla proxy budget WARNs, and ships the last clean step's maps (the
    tram_station.SteppedWeather pattern). Attributes the generator reads (stamps, labels,
    detail_stats, ctl_scaled) delegate to `inner`; `report` = the shipped step's weather report,
    `ladder` = every step tried. Below the shipping atlas the 3-texel edge floor covers whole
    members, so a first-step WARN ships the first step there (recorded); at ATLAS it raises."""

    def __init__(self, inner, spec_at, steps, **hook_kw):
        self.inner = inner
        self.spec_at = spec_at
        self.steps = tuple(float(s) for s in steps)
        self.hook_kw = hook_kw
        self.report = None
        self.chosen = None
        self.ladder = []

    def __getattr__(self, name):
        if name.startswith("__"):
            raise AttributeError(name)
        inner = self.__dict__.get("inner")
        if inner is None:
            raise AttributeError(name)
        return getattr(inner, name)

    def __call__(self, base, nrm, msk, ctl, ao, mesh, A):
        import cs2_weather as CW
        LD = _LD()
        b0, n0, m0, c0 = self.inner(base, nrm, msk, ctl, ao, mesh, A)
        b0, n0, m0 = (np.array(x, dtype=np.float32, copy=True) for x in (b0, n0, m0))
        self.ladder = []
        best = None
        for s in self.steps:
            hk = CW.make_weather_hook(lambda *_a: (b0.copy(), n0.copy(), m0.copy(), c0),
                                      self.spec_at(s), **self.hook_kw)
            out = hk(base, nrm, msk, ctl, ao, mesh, A)
            rep = hk.report
            ok = LD.weather_budget_ok(rep)
            v, pd = rep["vanilla"], rep["perceptual"]
            self.ladder.append({"intensity": s, "ok": ok,
                                "VL1": round(float(v["VL1_mean_rel_dL"]), 4),
                                "VL2": round(float(v["VL2_share_rel_dL_gt_0.15"]), 4),
                                "VL3": round(float(v["VL3_mean_dsmooth"]), 4),
                                "VL4": round(float(v["VL4_max"]), 3),
                                "dLstar_mean": round(float(pd["mean_abs_dLstar"]), 3),
                                "dLstar_gt2": round(float(pd["share_dLstar_gt_2"]), 4)})
            print(f"[detach] weathering step {s}: ok {ok}, VL1 {v['VL1_mean_rel_dL']:.4f} VL2 "
                  f"{v['VL2_share_rel_dL_gt_0.15']:.4f} VL4 parts "
                  f"{[(q['part'], q['ratio'], q['floored']) for q in v.get('VL4_parts', [])]}")
            if not ok:
                break
            best = (s, out, rep)
        if best is None:
            if A >= ATLAS:
                raise ContractError(f"W: weathering WARNs already at intensity {self.steps[0]} "
                                    f"({self.ladder[0]})")
            hk = CW.make_weather_hook(lambda *_a: (b0.copy(), n0.copy(), m0.copy(), c0),
                                      self.spec_at(self.steps[0]), **self.hook_kw)
            best = (self.steps[0], hk(base, nrm, msk, ctl, ao, mesh, A), hk.report)
        self.chosen, out, self.report = best
        print(f"[detach] weathering: intensity {self.chosen} (highest budget-clean step of "
              f"{[(r['intensity'], r['ok']) for r in self.ladder]})")
        return out


def check_label_uv(me, table, name, right=None, up=None):
    """Detail pass 1: a painted label reads upright and unmirrored iff UVUnique u runs along its
    reading right and v along its up. Walls: right = +Z x n, up = +Z (check_sign_uv's rule);
    floor marks pass the oriented (post orient_for_net) right / up. Raises otherwise."""
    if right is None:
        return check_sign_uv(me, table, name)
    faces = cs2_compose.faces_of_prefix(me, table, name)
    if len(faces) != 1:
        raise ContractError(f"{name}: expected one face, got {len(faces)}")
    poly = me.polygons[faces[0]]
    uv = me.uv_layers[cs2_uv.UNIQUE_LAYER].data
    ls = list(poly.loop_indices)[:3]
    P = [np.array(me.vertices[me.loops[l].vertex_index].co, dtype=np.float64) for l in ls]
    Q = [np.array(uv[l].uv, dtype=np.float64) for l in ls]
    J = np.stack([P[1] - P[0], P[2] - P[0]], axis=1) @ np.linalg.inv(
        np.stack([Q[1] - Q[0], Q[2] - Q[0]], axis=1))
    du, dv = J[:, 0] / np.linalg.norm(J[:, 0]), J[:, 1] / np.linalg.norm(J[:, 1])
    r, u_ = np.array(right, np.float64), np.array(up, np.float64)
    if du @ r < 0.99 or dv @ u_ < 0.99:
        raise ContractError(f"{name}: UV frame u.right {du @ r:.3f}, v.up {dv @ u_:.3f} - the label "
                            f"would read mirrored / rotated")
    return float(du @ r), float(dv @ u_)


# floor labels: (authoring right, authoring up); orient_for_net maps (x, y, z) -> (y, -x, z)
D1_FLOOR_FRAME = {"seat": ((0.0, 1.0, 0.0), (-1.0, 0.0, 0.0))}


def _oriented(v):
    return (v[1], -v[0], v[2])


def stamp_labels(base, ctl, me, table, p):
    """Detail pass 1: paint every dressing label (cs2_liftdress.paint_board through
    stamp_on_face) into its pinned sheet rect. Asserts (raise -> nothing ships): the painted rect
    IS the sheet rect x A (pinned), the UV frame reads upright and unmirrored, px/m >= LABEL_PPM
    (and >= 380 per axis, design L1) scaled with the atlas, the painter's T2 / T1 flags where the
    design claims them. Returns {label: metrics} for the manifest."""
    LD = _LD()
    A = base.shape[0]
    k = A / float(ATLAS)
    sheet = label_sheet(p)
    out = {}
    bad = []
    for nm, kind in sorted(d1_labels(p).items()):
        x0, y0, x1, y1 = sheet[nm]
        want = (int(round(y0 * k)), int(round(y1 * k)), int(round(x0 * k)), int(round(x1 * k)))
        frame = next((f for pre, f in D1_FLOOR_FRAME.items() if kind.startswith(pre)), None)
        if frame is None:
            check_label_uv(me, table, nm)
        else:
            check_label_uv(me, table, nm, _oriented(frame[0]), _oriented(frame[1]))
        painter = LD.board_painter(kind)
        rect = cs2_compose.uv_bbox_px(me, table, nm, A)
        cs2_compose.stamp_on_face(base, ctl, me, table, nm, painter)
        m = dict(painter.last or {})
        if tuple(int(v) for v in (rect or ())) != want:
            bad.append(f"{nm} rect {rect} != sheet {want} (not pinned)")
        pu, pv = m.get("px_per_m", [0.0, 0.0])
        floor, axis = LABEL_PPM * k, 380.0 * k
        if math.sqrt(pu * pv) < floor - 0.5 or min(pu, pv) < axis - 0.5:
            bad.append(f"{nm} {pu:.0f} x {pv:.0f} px/m < {floor:.0f} (axis {axis:.0f})")
        if A >= ATLAS and kind != "tension" and not m.get("t2_ok"):
            bad.append(f"{nm} fails T2 (stroke {m.get('stroke_px')} px, cap {m.get('cap_m')} m)")
        m["rect"] = list(want)
        m["reserved_uv"] = [round(v / float(ATLAS), 8) for v in (x0, y0, x1, y1)]
        out[nm] = m
    if bad:
        raise ContractError(f"L1-L5 labels: {bad}")
    return out


def ctl_8bit_exclusive(ctl):
    """ControlMask R + G + B <= 1 must survive the 8-bit PNG write: island-seam texels where
    two slots meet bake to 0.5 / 0.5, which rounds to 128 + 128 = 1.0039 (measured on the
    Revision 1 smoke bake, 113 texels at 512, after the reserved strip squeezed the packed
    band to v 0..0.80). Mixed texels (two or more channels > 0) whose sum would exceed 255
    after rounding are scaled to (255 - 1.5) / 255; single-slot texels are untouched.
    In place; returns the number of texels scaled."""
    c = ctl[..., 0:3]
    mixed = (c > 1e-6).sum(axis=-1) >= 2
    s = c.sum(axis=-1)
    lim = (255.0 - 1.5) / 255.0
    m = mixed & (s > lim)
    if m.any():
        c[m] = c[m] * (lim / s[m])[:, None]
    return int(m.sum())


# --------------------------------------------------------------------------------------
# Previews
# --------------------------------------------------------------------------------------

def _post(v):
    """authoring -> post-orient Blender frame (orient_for_net: (x, y, z) -> (y, -x, z))."""
    return (v[1], -v[0], v[2])


def gd_preview_poses(p):
    """Render-only cabin poses (authoring): entering the -Y portal, on both platforms, in
    the turn (label, centre, outboard unit, travel unit)."""
    X, xn, L = p.x_wheel, p.x_nose, p.lane_y
    out = [("in -Y portal", (xn - 1.4, -L), (0.0, -1.0), (-1.0, 0.0)),
           ("+Y nose", (xn + 2.6, L), (0.0, 1.0), (1.0, 0.0)),
           ("-Y platform", ((xn + X) / 2.0, -L), (0.0, -1.0), (-1.0, 0.0)),
           ("+Y platform", (X - 4.0, L), (0.0, 1.0), (1.0, 0.0))]
    for a in (-50.0, 0.0, 50.0):
        r = math.radians(a)
        out.append((f"turn {a:+.0f}", (X + L * math.cos(r), L * math.sin(r)),
                    (math.cos(r), math.sin(r)), (math.sin(r), -math.cos(r))))
    return out


def gd_preview_cabins(rig, p):
    """The shipped SkiGondolaCabin8A (G8's mesh) placed at gd_preview_poses, grip top on the
    lane, as render-only objects tracked by the preview rig (never exported)."""
    cab = load_gd_cabin()
    V, tris = cab["V"], cab["tris"]
    mat = rig.track_data(bpy.data.materials, bpy.data.materials.new("StandInCabin"))
    mat.use_nodes = True
    b = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    b.inputs["Base Color"].default_value = (0.70, 0.07, 0.06, 1.0)
    b.inputs["Roughness"].default_value = 0.35
    zu = p.z_rope - p.hang
    objs = []
    for i, (_lab, c, eo, et) in enumerate(gd_preview_poses(p)):
        W = [_post((c[0] + u * eo[0] + tt * et[0], c[1] + u * eo[1] + tt * et[1], zu + h))
             for u, h, tt in V]
        me = rig.track_data(bpy.data.meshes, bpy.data.meshes.new(f"standin_cabin{i}"))
        me.from_pydata(W, [], tris)
        me.update()
        o = rig.track(bpy.data.objects.new(f"standin_cabin{i}", me))
        bpy.context.collection.objects.link(o)
        me.materials.append(mat)
        objs.append(o)
    return objs


def gd_interior_light(rig, p):
    """A render-only area light under the hall ceiling so the interior close-ups (platform,
    stair, portal) read; the sun cannot reach inside the walled hall."""
    ld = rig.track_data(bpy.data.lights, bpy.data.lights.new("StandInHallLight", "AREA"))
    ld.energy = 4000.0
    ld.size = 10.0
    o = rig.track(bpy.data.objects.new("StandInHallLight", ld))
    bpy.context.collection.objects.link(o)
    o.location = Vector(_post(((p.x_nose + p.x_wheel) / 2.0, 0.0, p.z_top - 0.5)))
    return o


def gd_closeup_shots(p):
    """(name, target, eye[, (cut z, ortho scale)]) in the authoring frame: the portal-face
    sign and a side sign, a portal with a cabin entering, the platform from inside, the stair,
    the nose ladder + roof ladder, a side elevation, and two cutaway plans (below the deck:
    platforms / trough / turn / operator room; below the ceiling: the machinery deck)."""
    X, xn, xb = p.x_wheel, p.x_nose, p.x_back
    xm = (xn + xb) / 2.0
    zb = sign_z_centre(p)
    so = gd_opr_side(p)                 # nitpick A2: the operator room's side (the stair on -so)
    W2 = p.w_half
    lay = gd_ladder_layout(p)
    return [("signband_n", (xn, 0.0, gd_sign_nose_z(p)), (xn - 7.5, 1.0, gd_sign_nose_z(p) + 0.6)),
            ("signband_p", (xm, W2, zb), (xm - 1.5, W2 + 7.5, zb + 0.6)),
            ("portal", (xn, -p.lane_y + 0.3, 2.7), (xn - 8.5, -1.2, 2.4)),
            ("platform", (xn + 1.0, 3.6, 1.6), (X - 3.0, 5.45, 1.95)),
            ("stair", (X + 3.0, -5.0 * so, 3.0), (X - 1.6, -4.7 * so, 1.9)),
            ("nose_ladder", (xn - 0.3, GD_NL_Y, 0.5 * p.z_crown), (xn - 16.0, GD_NL_Y - 5.0, 3.0)),
            ("roof_ladder", (xn + 0.4, -3.0, p.z_top + 2.0), (xn - 7.0, -13.0, p.z_crown + 5.0)),
            ("windows_p", (xm, W2, 4.5), (xm + 2.0, W2 + 22.0, 6.5)),
            # detail pass 1 (checker F1): the side door and the operator door as real openings, from
            # the walkway inside the hall (the leaves stay, closed, in the reveal)
            ("door_p_inside", (X - 3.0, W2, 1.30), (X - 5.2, GD_WALK_IN + 0.10, 1.75)),
            ("door_opr", (X + GD_OPR_X0, so * (GD_WALK_IN + W2 - WALL_T) / 2.0, 1.30),
             (X + GD_OPR_X0 - 2.4, so * (GD_WALK_IN + 0.05), 1.75)),
            ("plan", (xm, 0.0, 0.0), (xm, 0.0, 40.0), (GD_DECK_Z - 0.35, 27.0)),
            ("machinery", (xm, 0.0, 0.0), (xm, 0.0, 40.0), (p.z_top - 0.3, 27.0))]


def render_closeups(t, asset_dir, name, out_dir, p, tint, win=None):
    """Close-ups the shared rig cannot frame yet (N6): both sign bands, the soffit slot from
    below, the nose notch, the nose access and the +Y windows - with render-only CHAIR_ENV
    stand-ins at 12 loop positions and the `_Win` panes as dark glass (never exported; the
    rig removes them)."""
    rig = cs2_preview._Rig(t, None)
    written = []
    try:
        cam = cs2_preview._make_camera(rig)
        cs2_preview._make_world(rig, **cs2_preview.NOON)
        cs2_preview._make_ground(rig)
        cs2_preview._assign(t, cs2_preview._pbr_material(rig, asset_dir, name, tint))
        mat = rig.track_data(bpy.data.materials, bpy.data.materials.new("StandInEnv"))
        mat.use_nodes = True
        bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
        bsdf.inputs["Base Color"].default_value = (0.9, 0.15, 0.6, 1.0)
        # gondola draft 1: the REAL SkiGondolaCabin8A at 7 loop poses instead of the boxes
        picks = [] if p.gondola else [b for b in envelope_boxes(p) if b[1] == 0.0]
        if p.gondola:
            for o in gd_preview_cabins(rig, p):
                o.hide_render = False
        step = max(1, len(picks) // 24)
        for i, (label, _dz, c, ex, ey, hx, hy, z0, z1) in enumerate(picks[::step][:24]):
            corners, quads = _box_tris(c, ex, ey, hx, hy, z0, z1, 0.0)
            mb = _MB()
            for q in quads:
                pts = [corners[j] for j in q]
                cen = np.mean(pts, axis=0)
                mid = (c[0], c[1], (z0 + z1) / 2.0)
                mb.f([_post(x) for x in pts], _post(tuple(cen - np.array(mid))))
            o = rig.track(mb.obj(f"standin_{i:02d}"))
            o.data.materials.append(mat)
            o.hide_render = False
        if win is not None:
            # Revision 1 (f): the `_Win` panes (cabin band + housing windows) as a render-only
            # dark-glass stand-in, so the close-ups show glazing where the game draws its
            # interior parallax (cs2_preview's main views render the main mesh only)
            gm = rig.track_data(bpy.data.materials, bpy.data.materials.new("StandInGlass"))
            gm.use_nodes = True
            gb = next(n for n in gm.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
            gb.inputs["Base Color"].default_value = (0.03, 0.05, 0.07, 1.0)
            gb.inputs["Roughness"].default_value = 0.08
            wo = rig.track(bpy.data.objects.new("standin_win", win.data.copy()))
            bpy.context.collection.objects.link(wo)
            wo.matrix_world = win.matrix_world.copy()
            wo.data.materials.clear()
            wo.data.materials.append(gm)
            wo.hide_render = False
        zb = sign_z_centre(p)
        xm = (p.x_nose + p.x_back) / 2.0
        # Revision 1: the sign band per side (was nameband_*), the nose access (landing,
        # gangplank, outer ladder) and a side elevation with the two housing windows
        shots = [("signband_p", (xm, p.w_half, zb), (xm - 1.5, p.w_half + 7.5, zb + 0.6)),
                 ("signband_n", (xm, -p.w_half, zb), (xm + 1.5, -p.w_half - 7.5, zb + 0.6))]
        if not p.gondola:
            shots += [("soffit_slot", (p.x_wheel - 1.0, p.lane_y, p.z_soffit),
                       (p.x_wheel - 7.5, p.lane_y + 2.5, 1.7)),
                      ("nose_notch", (p.x_nose, p.lane_y, p.z_soffit + 0.6),
                       (p.x_nose - 8.0, p.lane_y + 4.0, 2.4)),
                      ("nose_access", (p.x_nose - 1.0, -0.4, p.z_soffit + 1.6),
                       (p.x_nose - 10.0, -6.5, p.z_soffit + 3.2)),
                      # Revision 2: the whole front ladder, footing (z 0) to crown, face-on
                      # from the line side down the gap between the two lanes' stand-ins,
                      # and the ladder-way from above
                      ("nose_ladder", (p.x_nose - 0.3, nose_access(p)[2][1], 0.50 * p.z_crown),
                       (p.x_nose - 15.0, nose_access(p)[2][1] - 1.2, 1.9))]
            gf = ground_flight(p)
            if gf is None:
                shots.append(("ladder_way", (p.x_nose - 0.3, NOSE_LADDER_Y, p.z_soffit),
                              (p.x_nose - 3.2, NOSE_LADDER_Y - 1.4, p.z_soffit + 3.6)))
            else:
                # Revision 1c (base): the ground flight beside the landing, from above its
                # climbing side (over the +Y chair stand-ins)
                xg = (gf["x"][0] + gf["x"][1]) / 2.0
                shots.append(("ground_flight", (xg, gf["y"] + 0.15, 2.0),
                              (p.x_nose - 2.6, 3.2, 6.0)))
            shots.append(("windows_p", (xm, p.w_half, zb), (xm + 2.0, p.w_half + 17.0, zb + 2.5)))
        else:
            shots = gd_closeup_shots(p)
            gd_interior_light(rig, p)
        res = (1300, 1000)
        sc = rig.scene
        sc.render.resolution_x, sc.render.resolution_y = res
        for shot in shots:
            vname, tgt, eye = shot[:3]
            cut = shot[3] if len(shot) > 3 else None
            tg, ey_ = Vector(_post(tgt)), Vector(_post(eye))
            cs2_preview._aim(cam, tg, ey_ - tg, (ey_ - tg).length)
            clip0 = cam.data.clip_start
            if cut is not None:
                # cutaway plan: orthographic from above, the roof / ceiling / deck clipped away
                # above z = cut (camera clip_start), everything below it drawn
                cam.data.type = "ORTHO"
                cam.data.ortho_scale = cut[1]
                cam.data.clip_start = ey_.z - cut[0]
            path = os.path.join(out_dir, f"{name}_closeup_{vname}.png")
            written.append(cs2_preview._render(rig, path, engine="EEVEE"))
            cam.data.type = "PERSP"
            cam.data.clip_start = clip0
            print(f"[preview] closeup -> {path}")
    finally:
        rig.restore()
    return written


def d1_closeup_shots(p):
    """Detail pass 1 close-ups (authoring frame: name, target, eye), rendered at 2x the close-up
    resolution without the chair stand-ins: the loading / unloading area, a hang-slot board, the
    roof route (base: the front ladder from the landing through the opened rail gap), the pf
    ladder and its soffit hatch, the back corner (maze / ski rack / SLOW board; the follow-up's lamp posts
    are vanilla stand-in boxes now)."""
    zs, xn = p.z_soffit, p.x_nose
    pf = pf_layout(p) or {"x": p.ped_f + 1.35, "y": 0.0}
    lx, ly = nose_access(p)[2]
    if p.kind == "base":
        return [("loading", (4.6, 3.4, 0.7), (0.4, 1.0, 1.65)),
                ("gate_maze", (6.6, 4.5, 0.8), (4.4, 1.5, 2.1)),
                ("board_lowerbar", (0.60, 1.65, 2.575), (3.30, 1.10, 2.05)),
                ("board_tipsup", (-1.50, 1.65, 2.575), (1.20, 1.10, 2.05)),
                ("roof_route", (lx, ly, zs + 1.2), (xn - 4.2, ly - 3.4, zs + 2.6)),
                ("roof_route_gap", (lx - 0.2, ly, zs + 0.4), (xn - 1.9, ly - 2.0, zs + 2.4)),
                ("waithere", (6.35, 4.33, 0.84), (7.95, 5.30, 1.55)),
                ("pf_hatch", (pf["x"] + 0.3, pf["y"], 3.2), (pf["x"] + 2.3, pf["y"] + 2.1, 0.9)),
                ("back_corner", (11.8, -4.6, 1.4), (17.2, -9.6, 3.6)),
                ("ops_cabin", (7.6, 5.6, 1.9), (6.0, 1.2, 2.2))]
    return [("unloading", (0.0, -2.6, 0.7), (-4.0, -1.0, 1.7)),
            ("board_unload", (-0.20, -1.65, 2.575), (-3.0, -1.10, 2.05)),
            ("roof_route", (lx, ly, zs + 1.2), (xn - 4.2, ly - 3.4, zs + 2.6)),
            ("pf_hatch", (pf["x"] + 0.3, pf["y"], 3.2), (pf["x"] + 2.3, pf["y"] + 2.1, 0.9)),
            ("cabinet_gauge", (0.6, 4.6, 1.2), (1.4, 1.6, 1.8)),
            ("exit_corner", (8.6, -5.0, 1.6), (4.0, -9.8, 3.4))]


def fu_closeup_shots(p):
    """DQ follow-up close-ups (authoring frame: name, target, eye), the vanilla lamp posts as the review
    packs' labelled stand-in boxes: per DQ station the board sequence along the chair's travel (base:
    from the maze lane past WAIT HERE on the gate and the carpet to LOWER BAR / SKI TIPS UP; top: from
    the nose along the arrival lane, RAISE BAR -> UNLOAD at the unload line; STAY SEATED was removed
    2026-10-12) and the lamp posts from outside (base: the back ramp, the maze entrance and the plaza with
    the SLOW board on its posts; top: the unload apron from the -Y side)."""
    if p.kind == "base":
        return [("sequence_load", (1.0, 2.4, 1.7), (10.6, 4.95, 2.35)),
                ("lamps_back", (10.0, 0.0, 1.5), (30.0, -2.5, 8.5))]
    # follow-up resume: the eye moves out to y -8.6 (was -3.90, where STAY SEATED hid half of UNLOAD: 2 deg
    # apart on a 3 deg board; the board is gone now, the eye stays); the sightlines to RAISE BAR and UNLOAD
    # pass either side of the front-corner lamp box (x -9.84 .. -8.77, y -5.57 .. -5.03)
    return [("sequence_unload", (-2.9, -1.65, 2.55), (-13.0, -8.6, 2.1)),
            ("lamps_apron", (0.0, -4.0, 1.4), (0.0, -30.0, 9.0))]


def render_d1_closeups(t, asset_dir, name, out_dir, p, tint, win=None, shots=None, tag="d1"):
    """Detail pass 1: the d1_closeup_shots (or `shots`) at D1_CLOSEUP_RES (2x the close-up size), noon,
    the `_Win` panes as dark glass; no chair stand-ins (the dressing reads on its own). DQ follow-up: the
    vanilla sub-objects drawn as the review packs' labelled boxes (subobj_standins) in every frame."""
    rig = cs2_preview._Rig(t, None)
    written = []
    os.makedirs(out_dir, exist_ok=True)
    try:
        cam = cs2_preview._make_camera(rig)
        cs2_preview._make_world(rig, **cs2_preview.NOON)
        cs2_preview._make_ground(rig)
        cs2_preview._assign(t, cs2_preview._pbr_material(rig, asset_dir, name, tint))
        if win is not None:
            gm = rig.track_data(bpy.data.materials, bpy.data.materials.new("StandInGlassD1"))
            gm.use_nodes = True
            gb = next(n for n in gm.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
            gb.inputs["Base Color"].default_value = (0.03, 0.05, 0.07, 1.0)
            gb.inputs["Roughness"].default_value = 0.08
            wo = rig.track(bpy.data.objects.new("standin_win_d1", win.data.copy()))
            bpy.context.collection.objects.link(wo)
            wo.matrix_world = win.matrix_world.copy()
            wo.data.materials.clear()
            wo.data.materials.append(gm)
            wo.hide_render = False
        sm = subobj_standins(rig, p, name)
        print(f"[preview] {tag} closeups: {sm.get('n', 0)} vanilla sub-object stand-ins {sm.get('families')}")
        sc = rig.scene
        sc.render.resolution_x, sc.render.resolution_y = D1_CLOSEUP_RES
        for vname, tgt, eye in (d1_closeup_shots(p) if shots is None else shots):
            tg, ey_ = Vector(_post(tgt)), Vector(_post(eye))
            cs2_preview._aim(cam, tg, ey_ - tg, (ey_ - tg).length)
            path = os.path.join(out_dir, f"{name}_{tag}_{vname}.png")
            written.append(cs2_preview._render(rig, path, engine="EEVEE"))
            print(f"[preview] {tag} closeup -> {path}")
    finally:
        rig.restore()
    return written


def render_fu_gondola(t, asset_dir, name, out_dir, p, tint, win):
    """DQ follow-up close-up of a gondola station (FU_CLOSEUP_DIR): the +Y skier entry from outside -
    the double glass doors with NO centre post (1.80 x 2.30 m clear), the ramp and the canopy - with the
    `_Win` day glass, the vanilla stand-in boxes and the hall light, at D1_CLOSEUP_RES."""
    L = gd_hall_layout(p)
    xd, W2 = L["xd"], p.w_half
    os.makedirs(out_dir, exist_ok=True)
    rig = cs2_preview._Rig(t, None)
    written = []
    try:
        cam = cs2_preview._make_camera(rig)
        cs2_preview._make_world(rig, **cs2_preview.NOON)
        cs2_preview._make_ground(rig)
        cs2_preview._assign(t, cs2_preview._pbr_material(rig, asset_dir, name, tint))
        gd_glass_standin(rig, win, "day")
        gd_vanilla_standins(rig, p, name=name)
        gd_interior_light(rig, p)
        sc = rig.scene
        sc.render.resolution_x, sc.render.resolution_y = D1_CLOSEUP_RES
        tg, ey_ = Vector(_post((xd, W2, 1.50))), Vector(_post((xd + 1.6, W2 + 5.6, 1.85)))
        cs2_preview._aim(cam, tg, ey_ - tg, (ey_ - tg).length)
        path = os.path.join(out_dir, f"{name}_fu_door_p_outside.png")
        written.append(cs2_preview._render(rig, path, engine="EEVEE"))
        print(f"[preview] follow-up closeup -> {path}")
    finally:
        rig.restore()
    return written


# -- gondola revision 1 previews (the `_Win` glazing and the vanilla sub-objects are part of the look:
# cs2_preview's lit views draw the main mesh only, so the hall's lit views are rendered here) --------

GD_TINT = {"R": cs2_asset.STEEL_DEFAULT_RGB, "G": cs2_asset.PAD_RGB, "B": CANOPY_RGB}
GD_VIEW_RES = (1600, 1000)


def _gd_mat(rig, name, rgb, rough=0.6, emit=None, strength=0.0, alpha=None, cull=False):
    m = rig.track_data(bpy.data.materials, bpy.data.materials.new(name))
    m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    b.inputs["Base Color"].default_value = tuple(rgb) + (1.0,)
    b.inputs["Roughness"].default_value = rough
    if emit is not None:
        b.inputs["Emission Color"].default_value = tuple(emit) + (1.0,)
        b.inputs["Emission Strength"].default_value = strength
    if alpha is not None:
        b.inputs["Alpha"].default_value = alpha
        for attr, val in (("surface_render_method", "BLENDED"), ("blend_method", "BLEND")):
            try:
                setattr(m, attr, val)
            except (AttributeError, TypeError):
                pass
    m.use_backface_culling = cull
    return m


def gd_glass_standin(rig, win, mode):
    """The `_Win` panes as a render-only stand-in (never exported): "day" dark reflective glass,
    back faces culled (seen from inside the hall the panes vanish, as single-sided panes do in the
    game); "dusk" / "night" about 70 % of the rooms lit warm (the game lights `_Win` rooms at night,
    per room; which ones is the game's choice - here crc32 of the cell id); "xray" 25 % opaque, to
    read the machinery deck, bullwheel and stair behind (NOT the in-game look: the game draws its
    interior parallax in `_Win` panes)."""
    import zlib
    wo = rig.track(bpy.data.objects.new(f"standin_win_{mode}", win.data.copy()))
    bpy.context.collection.objects.link(wo)
    wo.matrix_world = win.matrix_world.copy()
    me = wo.data
    me.materials.clear()
    if mode == "xray":
        me.materials.append(_gd_mat(rig, "StandInGlassXray", (0.05, 0.08, 0.11), 0.05, alpha=0.25))
    else:
        me.materials.append(_gd_mat(rig, f"StandInGlass_{mode}", (0.035, 0.055, 0.075), 0.06, cull=True))
        if mode in ("dusk", "night"):
            me.materials.append(_gd_mat(rig, f"StandInGlassLit_{mode}", (0.10, 0.08, 0.05), 0.3,
                                        emit=(1.0, 0.66, 0.34), strength=0.9 if mode == "night" else 0.6,
                                        cull=True))
            lay = me.uv_layers.active.data
            for pl in me.polygons:
                u, v = lay[pl.loop_indices[0]].uv
                cid = int(v * 5) * 5 + int(u * 5)
                pl.material_index = 1 if zlib.crc32(f"room{cid}".encode()) % 10 < 6 else 0
    wo.hide_render = False
    return wo


def subobj_standins(rig, p, name):
    """DQ follow-up (Richard, fifteenth look: "stand-ins are great, much easier to see - let's do that for
    all assets"): the review packs' labelled semi-transparent BOXES (cs2_subobj.add_standins: the prop's
    variant-union bounds, colour per family, the prefab name above it) for every vanilla sub-object row of
    this station, as render-only objects in the preview rig (never exported; the rig removes them, their
    text curves, materials and collections). The rows are the manifest's own (subobject_rows), so the
    previews show exactly what the surgery round places. Returns the add_standins summary."""
    import cs2_subobj as SO
    block = subobject_rows(p, name)
    if not block:
        return {"n": 0}
    rows = SO.rows_from_manifest({("rev1" if p.gondola else "detail1"): {"sub_objects": block}})
    root = rig.track_data(bpy.data.collections, bpy.data.collections.new(f"PV_{name}_props"))
    sub = rig.track_data(bpy.data.collections, bpy.data.collections.new(f"PV_{name}_SUB"))
    rig.scene.collection.children.link(root)
    root.children.link(sub)
    before = {m.name for m in bpy.data.materials}
    summary = SO.add_standins(f"{name}PV", rows, root, sub)
    for nm in summary.get("objects", []):
        o = bpy.data.objects.get(nm)
        if o is None:
            continue
        rig.track(o)
        if o.type == "FONT":
            rig.track_data(bpy.data.curves, o.data)
        o.hide_render = False
    for m in bpy.data.materials:
        if m.name not in before:
            rig.track_data(bpy.data.materials, m)
    for c_ in list(root.children):
        if c_.get("cs2r_role") == SO.NOPROPS_ROLE:
            rig.track_data(bpy.data.collections, c_)
    return summary


def gd_vanilla_standins(rig, p, night=False, name=None):
    """Render-only stand-ins of the vanilla sub-objects (gd_subobjects). DQ follow-up: drawn as the review
    packs' labelled boxes (subobj_standins; the revision-1 pole + arm + box head read as the square-headed
    posts Richard rejected - rev-1 checker F6); returns the light points [(post-frame location, watts)] for
    the night views (approximate vanilla pools)."""
    LD = _LD()
    subobj_standins(rig, p, name or (p.stem + "A"))
    lights = []
    for pfab, pos, facing, _mount in gd_subobjects(p):
        if pfab.startswith("LightpolePark"):
            rot = LD.facing_quat(facing, frame="blender") if facing else (0.0, 0.0, 0.0, 1.0)
            _lo, hi = LD.subobj_box(pfab, LD.blender_to_game(pos), rot, frame="blender")
            f = LD._dir3(facing) if facing else Vector((0.0, 0.0, 0.0))
            lights.append((_post((pos[0] + f.x * 0.70, pos[1] + f.y * 0.70, float(hi[2]) - 0.35)), 250.0))
        elif pfab.startswith("WallLight"):
            n_ = LD._dir3(facing)
            lights.append((_post((pos[0] + n_.x * 0.3, pos[1] + n_.y * 0.3, pos[2] - 0.1)), 80.0))
        elif pfab.startswith("RoofLight"):
            lights.append((_post((pos[0], pos[1], pos[2] - 0.4)), 120.0 if pos[2] < 4.0 else 300.0))
    return lights


def gd_tower_standin(rig, p):
    """The base's hosted SkiLiftTowerETGDO (the real LOD0, galvanised grey) as a render-only stand-in."""
    if not p.hosted_tower:
        return None
    lods = load_gd_tower()
    if lods is None:
        return None
    tw = lods[0]
    me = rig.track_data(bpy.data.meshes, bpy.data.meshes.new("standin_tower"))
    me.from_pydata([_post(tuple(v)) for v in tw["V"]], [], tw["tris"])
    me.update()
    o = rig.track(bpy.data.objects.new("standin_tower", me))
    bpy.context.collection.objects.link(o)
    me.materials.append(_gd_mat(rig, "StandInTower", (0.45, 0.46, 0.47), 0.45))
    o.hide_render = False
    return o


def gd_view_shots(p):
    """Revision 1 named views (authoring frame: name, target, eye, interior light?) - the five Richard
    reads the hall by (arrival, portal, platform, glazed_side, night) + the x-ray of the side - and the
    technical close-ups (signs, entry, eave, stair, ladders, doors, cutaway plans)."""
    L = gd_hall_layout(p)
    X, xn, xb, W2 = p.x_wheel, p.x_nose, p.x_back, p.w_half
    xm, xd, sz = (xn + xb) / 2.0, L["xd"], gd_sign_nose_z(p)
    so = gd_opr_side(p)                 # nitpick A2: the operator room's side (the stair on -so)
    a, b, _k = L["bays"][L["sign_bay"]]
    xs = (a + b) / 2.0
    lay = gd_ladder_layout(p)
    port_eye = (xn - 21.0, -7.0, 11.0) if p.hosted_tower else (xn - 18.0, -4.0, 6.5)
    views = [("arrival", (xd + 1.0, W2, 3.2), (xd + 13.0, W2 + 13.0, 2.6), False, "day"),
             ("portal", (xn, 0.0, 4.6), port_eye, False, "day"),
             ("platform", (xn + 2.0, -3.4, 1.7), (X - 1.4, -5.45, 1.95), True, "day"),
             ("glazed_side", (xm, W2, 4.2), (xm - 5.0, W2 + 30.0, 10.0), False, "day"),
             ("glazed_side_xray", (xm, W2, 4.2), (xm - 5.0, W2 + 30.0, 10.0), True, "xray"),
             ("night", (xm, 0.0, 4.0), (xm - 24.0, W2 + 26.0, 11.0), False, "night")]
    closeups = [("signband_n", (xn, 0.0, sz), (xn - 7.5, 1.0, sz + 0.6), False),
                ("signband_p", (xs, W2, GD_SIDE_SIGN_Z), (xs - 1.5, W2 + 7.5, GD_SIDE_SIGN_Z + 0.6), False),
                ("entry_p", (xd, W2, 1.6), (xd + 4.5, W2 + 6.5, 2.3), False),
                ("eave", (xm, W2 + 0.7, p.z_top - 0.2), (xm + 3.0, W2 + 4.8, 4.3), False),
                ("portal", (xn, -p.lane_y + 0.3, 2.7), (xn - 8.5, -1.2, 2.4), True),
                ("platform", (xn + 1.0, 3.6, 1.6), (X - 3.0, 5.45, 1.95), True),
                ("stair", (X + 3.0, -5.0 * so, 3.0), (X - 1.6, -4.7 * so, 1.9), True),
                ("nose_ladder", (xn - 0.3, GD_NL_Y, 0.5 * p.z_crown), (xn - 16.0, GD_NL_Y - 5.0, 3.0), False),
                ("roof_ladder", (xn + 0.4, -3.0, p.z_top + 0.8), (xn - 7.0, -13.0, p.z_crown + 5.0), False),
                ("door_p_inside", (xd, W2, 1.30), (xd - 2.2, GD_WALK_IN + 0.10, 1.75), True),
                ("door_opr", (X + GD_OPR_X0, so * (GD_WALK_IN + W2 - WALL_T) / 2.0, 1.30),
                 (X + GD_OPR_X0 - 2.4, so * (GD_WALK_IN + 0.05), 1.75), True),
                ("plan", (xm, 0.0, 0.0), (xm, 0.0, 40.0), True, (GD_DECK_Z - 0.35, 27.0)),
                ("machinery", (xm, 0.0, 0.0), (xm, 0.0, 40.0), True, (p.z_top - 0.3, 27.0))]
    return views, closeups


def render_gd_previews(t, asset_dir, name, out_dir, p, tint, win, views):
    """Gondola revision 1 previews, all with the `_Win` stand-in and the vanilla sub-object stand-ins:
    pbr / dusk in the standard views, two-tone, the named views (gd_view_shots: arrival, portal with the
    hosted tower on the base, platform, glazed_side + its x-ray, night) at GD_VIEW_RES, the close-ups at
    1300 x 1000. Returns the written paths."""
    written = []
    os.makedirs(out_dir, exist_ok=True)
    tw_dir = os.path.join(out_dir, "twotone")
    os.makedirs(tw_dir, exist_ok=True)
    named, closeups = gd_view_shots(p)

    def setup(world, tint_, glass, night=False, cabins=False, tower=False, interior=False):
        rig = cs2_preview._Rig(t, None)
        cam = cs2_preview._make_camera(rig)
        cs2_preview._make_world(rig, **world)
        cs2_preview._make_ground(rig)
        cs2_preview._assign(t, cs2_preview._pbr_material(rig, asset_dir, name, tint_))
        gd_glass_standin(rig, win, glass)
        lights = gd_vanilla_standins(rig, p, night=night, name=name)
        if night:
            for i, (loc, w) in enumerate(lights):
                ld = rig.track_data(bpy.data.lights, bpy.data.lights.new(f"StandInPool{i}", "POINT"))
                ld.energy = w
                ld.color = (1.0, 0.86, 0.66)
                ld.shadow_soft_size = 0.2
                lo = rig.track(bpy.data.objects.new(f"StandInPool{i}", ld))
                bpy.context.collection.objects.link(lo)
                lo.location = Vector(loc)
        if cabins:
            for o in gd_preview_cabins(rig, p):
                o.hide_render = False
        if tower:
            gd_tower_standin(rig, p)
        if interior:
            gd_interior_light(rig, p)
        return rig, cam

    def shoot(rig, cam, path, tgt, eye, res, cut=None):
        sc = rig.scene
        sc.render.resolution_x, sc.render.resolution_y = res
        tg, ey_ = Vector(_post(tgt)), Vector(_post(eye))
        cs2_preview._aim(cam, tg, ey_ - tg, (ey_ - tg).length)
        clip0 = cam.data.clip_start
        if cut is not None:
            cam.data.type = "ORTHO"
            cam.data.ortho_scale = cut[1]
            cam.data.clip_start = ey_.z - cut[0]
        written.append(cs2_preview._render(rig, path, engine="EEVEE"))
        cam.data.type = "PERSP"
        cam.data.clip_start = clip0
        print(f"[preview] gondola rev1 -> {path}")

    night_world = dict(elevation_deg=-1.5, sun_energy=0.0)
    for mode, world, glass, tint_, folder, vv in (("pbr", cs2_preview.NOON, "day", tint, out_dir, views),
                                                 ("dusk", cs2_preview.DUSK, "dusk", tint, out_dir, views),
                                                 ("pbr", cs2_preview.NOON, "day", TWOTONE, tw_dir,
                                                  list(cs2_preview.DEFAULT_VIEWS))):
        rig, cam = setup(world, tint_, glass, night=(mode == "dusk"))
        try:
            for vname, vdir in vv:
                rig.scene.render.resolution_x, rig.scene.render.resolution_y = 1300, 1000
                cs2_preview._frame(cam, t, vdir, (1300, 1000))
                path = os.path.join(folder, f"{name}_{mode}_{vname}.png")
                written.append(cs2_preview._render(rig, path, engine="EEVEE"))
                print(f"[preview] gondola rev1 {mode} -> {path}")
        finally:
            rig.restore()
    # one rig per glazing mode: the cabins, the hosted tower (base) and the hall light in every one
    for glass in ("day", "xray", "night"):
        shots = [v for v in named if v[4] == glass]
        night = glass == "night"
        rig, cam = setup(night_world if night else cs2_preview.NOON, tint, glass, night=night,
                         cabins=True, tower=True, interior=not night)
        try:
            for vname, tgt, eye, _interior, _g in shots:
                shoot(rig, cam, os.path.join(out_dir, f"{name}_view_{vname}.png"), tgt, eye, GD_VIEW_RES)
        finally:
            rig.restore()
    rig, cam = setup(cs2_preview.NOON, tint, "day", cabins=True, interior=True)
    try:
        for shot in closeups:
            vname, tgt, eye = shot[:3]
            cut = shot[4] if len(shot) > 4 else None
            shoot(rig, cam, os.path.join(out_dir, f"{name}_closeup_{vname}.png"), tgt, eye, (1300, 1000), cut)
    finally:
        rig.restore()
    return written


# --------------------------------------------------------------------------------------
# Prefab numbers + manifest contract block (contract 11/12, MUST-CHANGE 7/11)
# --------------------------------------------------------------------------------------

def _r(v, n=4):
    return [round(float(x), n) for x in v]


def prefab_numbers(p, bounds_authoring):
    """Everything the prefab surgery reads, prefab-local (x lateral, y up, z travel; line
    at -z). Authoring (x, y, z) -> prefab (y, z, x) = the FBX-raw frame: the x components below (stubs, lanes,
    bounds, wheel, hosted tower) are NOT game x - the game negates FBX x on import (x_frame key; the sub-object
    rows are already written in game frame, cs2_liftdress.blender_to_game). UNVERIFIED which surgery steps read
    these numbers un-negated: apply rule T (x -> -x, bounds min / max swapped) when pasting them."""
    def stub(x, y, z_centre):
        return {"a": _r((x, y, z_centre - 0.125)), "b": _r((x, y + 0.01, z_centre - 0.04)),
                "c": _r((x, y + 0.02, z_centre + 0.04)), "d": _r((x, y + 0.03, z_centre + 0.125))}
    lo, hi = bounds_authoring
    bmin, bmax = _r((lo[1], lo[2], lo[0])), _r((hi[1], hi[2], hi[0]))
    bx = comm_bracket_centre(p)
    gtar = p.grip_above_rope
    seat_today = round(p.z_rope - p.hang + p.seat_top - DECK_H, 3)
    nums = {
        "variant": p.variant,
        "x_frame": "FBX-raw x (= authoring y); game x = -x (x-sign gate settled N 2026-10-15): negate every x here "
                   "(bounds_prefab min / max swap) when writing prefab rows; sub_objects rows are already game",
        "carrier": {"row": p.carrier_key, "source": p.carrier_src, "hang_m": p.hang,
                    "grip_top_above_rope_m": gtar, "seat_top_above_lowest_m": p.seat_top},
        "gauge_m": round(2 * p.lane_y, 3), "wheel_radius_m": p.rim_r,
        "turn_radius_code_m": p.lane_y, "load_clear_m": p.load_clear,
        "lane_rope_y": p.z_rope, "deck_top_y": DECK_H,
        "soffit_y": p.z_soffit, "band_top_or_eave_y": p.z_top, "crown_y": round(p.z_crown, 4),
        "pin_convention": "lane = chair GRIP TOP today (ChairPinSystem bounds.max.y); "
                          "the modelled rope, torus, wheel table and anchors sit on the lane",
        "seat_over_deck_m": {"today": seat_today,
                             "if_dq_chair_Q1_pins_rope_point": (round(seat_today + gtar, 3)
                                                                if gtar else None)},
        "wheel_local_prefab": _r((0.0, p.z_rope, p.x_wheel)),
        "wheel_centre_authoring": _r((p.x_wheel, 0.0, p.z_wheel)),
        "anchors_hv": [stub(-p.lane_y, anchor_z(p), p.anchor_x), stub(p.lane_y, anchor_z(p), p.anchor_x)],
        "anchor_comm": stub(0.0, bx[2], bx[0]),
        "hosted_tower": ({"position": [0.0, 0.0, p.anchor_x], "rotation_quat": [0, 1, 0, 0],
                          "rope_y_required": p.z_rope,
                          "note": "UtilityObject + StandingObject only; DQ entry compression "
                                  "tower (towers/O) holding the rope at this y"}
                         if p.hosted_tower else None),
        "nose_guide_tip_z": None if p.hosted_tower else p.anchor_x,
        "stub_hv": [[0.0, 0.0, -13.0], [0.0, 0.0, -16.0]],
        "stub_lv": [[6.0, 0.0, -13.0], [6.0, 0.0, -16.0]] if p.kind == "base" else None,
        "lv_pole": [6.0, 0.0, -16.0] if p.kind == "base" else None,
        "stub_tip_local_z": -16.0,
        "stub_tip_touchpoints": "SkiCatalog StationDef row (per-row StubTipLocalZ = -16) and every "
                                "reader of the single const SkiCatalog.StubTipLocalZ (-12, aliased "
                                "StationAlign.StubTipLocalZ: StationAlign, SkiResortStatsSystem "
                                "StubTip, LiftCensus) - code-economy Phase A touchpoint list",
        "lot": list(LOT),
        "anchor_to_wheel_run_m": round(p.x_wheel - p.anchor_x, 3),
        "max_wheel_run_m": 30.0,
        "bounds_prefab": {"min": bmin, "max": bmax}, "bounds_top_y": bmax[1],
        "net_sub_objects_y": ({"rule": "AnchorTop: line axle + grip_top_above_rope_m + 0.03",
                               "line_axle": p.line_axle,
                               "y": round(p.line_axle + gtar + 0.03, 3) if gtar else None,
                               "owner": "line lens (pre-pin fallback only)"}),
        "emissive": {"purpose": 72, "intensity": 0.6},
    }
    if ROPE_ON_CROWN:
        nums["rope_on_crown"] = rope_on_crown_block(p)
    if p.gondola:
        # gondola draft 1: what the surgery sheet needs beyond the DQ columns
        k = GD_TOWER_KEEPOUT
        hv = {"prefab": "SkiGondolaHaulRope", "fallback": "UnityGUID:5d88ac45a40b30341a79535930e3b735",
              "why": "FindAnchors pairs stubs with lanes BY PREFAB: the gondola towers' HV stubs "
                     "name SkiGondolaHaulRope (tram_tower.STUB_LANES gd_haul, round-2-contract 10.1)"}
        for a in nums["anchors_hv"]:
            a["lane_prefab"] = hv["prefab"]
            a["lane_fallback"] = hv["fallback"]
        nums["anchor_comm"]["lane_prefab"] = "UnityGUID:37c8abdb015384f4e8b7ce9b5b2b6c9b"
        nums["lane_prefabs"] = {"hv_haul": hv, "lv_comm": "UnityGUID:37c8abdb015384f4e8b7ce9b5b2b6c9b"}
        nums["line_prefab"] = "SkiGondolaHV"
        nums["carrier_prefab"] = "SkiGondolaCabin8 (mesh SkiGondolaCabin8A)"
        if p.hosted_tower:
            nums["hosted_tower"] = {
                "what": ("gondola entry compression tower SkiLiftTowerETGDO (towers/O, prefab "
                         "SkiLiftTerminalTowerGD: UtilityObject + StandingObject only, never PillarObject); "
                         "its LOD0 / LOD1 clear this hall (G6 seam)" if GD_REV1 else
                         "gondola entry compression tower - NOT BUILT (towers-transfer audit G2); "
                         "keep this pose on the surgery sheet, add the ObjectSubObjects row only "
                         "when its prefab exists (UtilityObject + StandingObject only)"),
                "position": [0.0, 0.0, p.anchor_x], "rotation_quat": [0, 1, 0, 0],
                "rope_y_required": p.z_rope, "gauge_m": round(2 * p.lane_y, 3),
                "lanes_x": [-p.lane_y, p.lane_y],
                "station_keepout_prefab": {"z_max": round(p.x_nose - k["dx"], 3),
                                           "abs_x_max": k["y"], "y_max": k["z"],
                                           "note": "the station puts nothing there but the haul "
                                                   "ropes (G6); the tower must stay inside it"}}
        nums["lot"] = list(GD_LOT) if GD_REV1 else list(LOT)
        nums["lot_note"] = (
            f"m_LotWidth 3 x m_LotDepth 4 (Richard, eighth look): 24 m ACROSS the line = prefab x "
            f"(lateral |x| <= 12; the mesh spans x {bmin[0]:.2f} .. {bmax[0]:.2f}, the vanilla sub-objects "
            f"stay inside too, G9), 32 m ALONG the travel = prefab z (z -16 .. 16 + 0.5; the mesh spans z "
            f"{bmin[2]:.2f} .. {bmax[2]:.2f}; the hosted entry tower at z -11 spans z -13.52 .. -8.48 and the "
            f"HV stub runs z -13 -> -16 = SkiCatalog StubTipLocalZ, on the lot's front edge, as on 2 x 4)"
            if GD_REV1 else
            "m_LotWidth 2 x m_LotDepth 4 (16 x 32 m; the ROUND-SPEC row's '4x3 "
            "cells' read as width 4 x depth 3 cannot hold the hosted tower at "
            "z -11 + the stub to -16 = SkiCatalog StubTipLocalZ; dq-terminal "
            "section 13 ruling). The mesh fits any larger lot")
        nums["win_room_ids"] = (
            "revision 1: one room per curtain-wall bay (all its tiers), the operator room its own; ids "
            "reused beyond 3 m (manifest gondola.glazing.rooms / room_ids)" if GD_REV1 else
            {"7": "operator room panes (ctrlwin, +Y wall)", "13": "hall glazing band (hsgwin, 4 per side wall)"})
        if GD_REV1:
            nums["color_defaults"] = {
                "slot1": {"rgb": [0.25, 0.26, 0.275], "what": "roof, mullions, transoms, ring beams, corner "
                          "columns, outriggers, gable fascias, canopies, door frames, portal trims (dark steel; "
                          "towers-transfer G5 compensated value)"},
                "slot3": {"rgb": [0.16, 0.30, 0.20], "what": "sign spandrel + side sign plates + the sign "
                          "fields (the DQ / draft-1 green default)"},
                "slot2": {"rgb": [0.85, 0.25, 0.10], "what": "the ski rack gear's accent colour (the shared "
                          "cs2_bldg gear via cs2_liftdress.ski_rack, material 'accent'); the platform stripes "
                          "are fixed safety yellow"}}
        nums["deck_top_y"] = DECK_H
        nums["floor_y"] = GD_FLOOR_Z
        nums["machinery_deck_y"] = GD_DECK_Z
    return nums


def print_prefab_numbers(nums):
    print("[prefab] ---- prefab-surgery numbers (prefab-local: x lateral, y up, z travel) ----")
    for k in ("variant", "carrier", "gauge_m", "wheel_radius_m", "turn_radius_code_m",
              "lane_rope_y", "seat_over_deck_m", "wheel_local_prefab", "wheel_centre_authoring",
              "stub_tip_local_z", "stub_tip_touchpoints", "stub_hv", "stub_lv", "lv_pole",
              "anchors_hv", "anchor_comm", "hosted_tower", "nose_guide_tip_z", "lot",
              "anchor_to_wheel_run_m", "bounds_prefab", "bounds_top_y", "net_sub_objects_y",
              "emissive", "pin_convention", "lane_prefabs", "line_prefab", "carrier_prefab",
              "lot_note", "win_room_ids", "machinery_deck_y", "color_defaults"):
        if k in nums:
            print(f"[prefab] {k}: {json.dumps(nums[k])}")


# --------------------------------------------------------------------------------------
# main
# --------------------------------------------------------------------------------------

def parse_args(argv=None):
    if argv is None:
        argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser(description="detachable / gondola terminal generator")
    ap.add_argument("--kind", default="base", choices=("base", "top", "drive", "return"))
    ap.add_argument("--gondola", action="store_true")
    ap.add_argument("--rope-on-crown", action="store_true", dest="rope_on_crown",
                    help="T27 preview flag (ROPE_ON_CROWN): the HV anchor where the vanilla cable rides the hosted / "
                         "nose train, the haul rope sloped to the wheel; off = the shipped station byte for byte")
    ap.add_argument("--out", default=None, help="letter folder; the asset lands in <out>/<Name>/")
    ap.add_argument("--export", action="store_true",
                    help="write into the contract folder assets/import/<folder> (+ previews)")
    ap.add_argument("--name", default=None)
    ap.add_argument("--letter", default="A")
    ap.add_argument("--finish", action="store_true")
    ap.add_argument("--lods", action="store_true")
    ap.add_argument("--preview", default=None)
    ap.add_argument("--no-preview", action="store_true")
    ap.add_argument("--review", nargs="?", const="", default=None, metavar="DIR")
    ap.add_argument("--atlas", type=int, default=ATLAS)
    ap.add_argument("--ao-samples", type=int, default=AO_SAMPLES)
    ap.add_argument("--ab", default=None)
    ap.add_argument("--carriers", default=CARRIERS_PATH)
    ap.add_argument("--no-validate", action="store_true")
    ap.add_argument("--overwrite-shipped", action="store_true",
                    help="allow writing over an asset folder another generator wrote")
    ap.add_argument("--weather", choices=("on", "off"), default="on",
                    help="detail pass 1 (DQ finish builds): the weathering layer at the highest "
                         "budget-clean intensity (off = the same build, dressing and bay sharing "
                         "without the layer; the gondola has none)")
    ap.add_argument("--no-fine", action="store_true",
                    help="fine-detail step 3b OFF: the approved pre-pass station byte for byte (modelled "
                         "dressing + box lamps, no kit rows; the DQ TOP alone differs - it has no STAY SEATED "
                         "board since 2026-10-12); default = the fine-detail pass (FINE_DETAIL)")
    ap.add_argument("--wheels", choices=("v1", "v2"), default="v2",
                    help="T62 sheave wheel generation: v2 (default) = cs2_sheaves.wheel on the top's nose train (the GD "
                         "head too: the round-3 wheel); v1 = the pre-T62 wheels byte for byte")
    args = ap.parse_args(argv)
    args.kind = {"drive": "base", "return": "top"}.get(args.kind, args.kind)
    return args


SHIPPING_ROOT = os.path.join(REPO, "assets", "import")


def _under(path, root):
    path, root = os.path.normcase(os.path.abspath(path)), os.path.normcase(os.path.abspath(root))
    try:
        return os.path.commonpath([path, root]) == root
    except ValueError:              # different drives
        return False


def shipping_guard(args, out_root, asset_dir):
    """Pipeline review 6 (generator-local until the shared cs2_asset helper lands): --out may
    not point into assets/import without --export, and no build overwrites a folder whose
    manifest names another generator unless --overwrite-shipped is given."""
    if not args.export and _under(out_root, SHIPPING_ROOT):
        raise SystemExit(f"--out {out_root} is in the shipping tree assets/import: pass "
                         f"--export (after the verdict)")
    mpath = os.path.join(asset_dir, f"{os.path.basename(asset_dir)}.manifest.json")
    if os.path.isfile(mpath) and not args.overwrite_shipped:
        try:
            with open(mpath, "r", encoding="utf-8") as fh:
                gen = str(json.load(fh).get("generator", ""))
        except (OSError, ValueError):
            gen = "<unreadable manifest>"
        if not gen.startswith(GENERATOR):
            raise SystemExit(f"{asset_dir} was written by {gen!r}, not {GENERATOR}: refusing "
                             f"to overwrite it (pass --overwrite-shipped to force)")


def engine_base_gate(t, p):
    """C5 on the BAKED mesh (one UV layer): cs2_validate.validate_mesh(kind="building") with
    no ERROR, verdict OK, every ENGINE_BASE_PARTS name among the OK parts, 0 blocked edges,
    0 verts below -1 cm. Same rule as lift_terminal.check_engine_base, which cannot be reused
    here as is: before the bake the mesh carries tile-space UVTile + UVSeed (uv-range and
    layer-count ERRORs), after it validate_mesh's float32 shoelace reads faces of 0.03-0.6 px2
    as zero-area (measured: 153 'uv-zero' faces whose float64 UV area is 7e-9..1.4e-7). The
    uv-zero rule is re-run here in float64; a real unmapped face still fails."""
    findings = cs2_validate.validate_mesh(t, kind="building")
    me = t.data
    uv = cs2_uv.loop_uvs(me.uv_layers.active)
    ua = np.abs(cs2_uv.face_uv_areas(me, uv))
    wa = np.array([pl.area for pl in me.polygons])
    zero64 = int(((wa > 1e-10) & (ua < 1e-12)).sum())
    errors = [f for f in findings if f[0] == "ERROR" and f[1] != "uv-zero"]
    r = cs2_validate.ground_ring_report(t)
    missing = [n for n in p.engine_base if n not in r["ok_parts"]]
    print(f"[detach] engine base: verdict {r['verdict']}, {r['ok_edges']} OK edges / "
          f"{r['blocked_edges']} blocked / {r['below_verts']} verts below -1 cm; float64 "
          f"zero-UV faces {zero64}; ok parts {sorted(r['ok_parts'])}")
    if errors or zero64 or r["verdict"] != "OK" or missing or r["blocked_edges"] \
            or r["below_verts"]:
        raise ContractError(f"C5 engine-base gate: errors {errors}, zero64 {zero64}, verdict "
                            f"{r['verdict']}, missing {missing}, offending {r['offending_parts']}")
    return r


def lod_lists(p):
    """(LOD1 delete prefixes, LOD2 delete prefixes) of this station. The gondola with real door
    openings (detail pass 1) keeps its two side door LEAVES at LOD2: without them the cut openings
    look through the hollow LOD2 shell (its interior and linings are deleted there); the frames,
    handles, kick plates and the interior operator door still go."""
    if p.gondola and GD_REV1:
        # revision 1: the outriggers, snow guards, canopy rods and the door stiles / push bars at LOD1
        # (the glass leaves stay to LOD2, the cut openings would look into the hollow LOD2 shell);
        # the mullions and transoms at LOD2 (the `_LOD2_Win` panes run edge to edge behind them)
        extra1 = ("outrig_", "snowguard_", "canopyrod_", "door_n_bar", "door_p_bar", "door_n_stile",
                  "door_p_stile")
        l2 = tuple(x for x in LOD2_DELETE if x != "door_") + extra1 + (
            "door_n_frame", "door_p_frame", "door_opr_", "mull_", "transom_")
        return LOD1_DELETE + extra1, l2
    if p.gondola and GD_DOOR_OPENINGS:
        l2 = tuple(x for x in LOD2_DELETE if x != "door_") + tuple(
            f"door_{t_}_{k}" for t_ in ("n", "p") for k in ("jamb", "head", "handle", "kick")) + ("door_opr_",)
        return LOD1_DELETE, l2
    return LOD1_DELETE, LOD2_DELETE


def lod_targets(t, table, n0, p):
    """LOD1 target = min(0.45 LOD0, LOD1 cap, count left after the LOD1 deletes) (skeptic
    claim 11: make_lod_chain raises when the deletes alone undershoot the target by > 25 %);
    LOD2 likewise against its deletes."""
    me = t.data
    fparts = _face_parts(me)
    tri = np.array([len(pl.vertices) - 2 for pl in me.polygons], dtype=np.int64)

    def left(prefixes):
        dead = np.array([n.startswith(prefixes) for n in table], dtype=bool)
        return int(tri[~dead[fparts - 1]].sum())

    d1_, d2_ = lod_lists(p)
    l1 = left(d1_)
    l2 = left(d2_)
    t1 = int(round(0.45 * n0))
    if p.lod1_max:
        t1 = min(t1, p.lod1_max)
    t1 = min(t1, l1)
    t2 = min(p.lod2 - LOD2_MARGIN, l2)        # F9: 490, never the 500 cap itself
    print(f"[detach] LOD targets: LOD1 {t1} (after deletes {l1}), LOD2 {t2} (after deletes {l2})")
    return t1, t2


def main(argv=None):
    """argv: the generator arguments (None = after `--` in sys.argv); tests pass a list."""
    if not bpy.app.background:
        raise RuntimeError("lift_terminal_detach runs headless only (ROUND-SPEC R10)")
    args = parse_args(argv)
    global FINE_DETAIL
    FINE_DETAIL = not args.no_fine          # fine-detail step 3b: every run follows its own args
    global ROPE_ON_CROWN
    ROPE_ON_CROWN = bool(args.rope_on_crown)     # T27: every run follows its own args (tests call main() in-process)
    import cs2_sheaves as S
    S.set_wheels(args.wheels)                    # T62: every run follows its own args, before any part is built
    carrier = load_carrier("gd8" if args.gondola else "dq", args.carriers)
    p = params_for(args.kind, args.gondola, carrier)
    name = cs2_asset.validate_asset_name(args.name or (p.stem + args.letter))
    rep = cs2_validate._Report(name)
    cs2_validate.lint_asset_name(name, rep)
    if any(f[0] == "ERROR" for f in rep):
        raise ContractError(f"C10 name lint failed: {list(rep)}")
    if args.export:
        out_root = os.path.join(REPO, "assets", "import", *p.folder.split("/"))
        if args.preview is None and not args.no_preview:
            args.preview = os.path.join(REPO, "assets", "preview", *p.folder.split("/"))
    elif args.out:
        out_root = os.path.abspath(args.out)
    else:
        raise SystemExit("give --out <letter dir> or --export")
    if args.no_preview:
        args.preview = None
    asset_dir = os.path.join(out_root, name)
    shipping_guard(args, out_root, asset_dir)
    os.makedirs(asset_dir, exist_ok=True)
    print(f"[detach] {name}: variant {p.variant}, carrier {p.carrier_key} ({p.carrier_src}); "
          f"lane +-{p.lane_y} at z {p.z_rope}, load clear {p.load_clear}, wheel x {p.x_wheel}, "
          f"crown {p.z_crown:.3f}; -> {asset_dir}")

    lift_terminal.clear_scene()
    parts = build_station(p)
    t, table, sources = assemble(parts, args.finish, p)
    t.name = name
    t.data.name = name
    contracts = assert_contracts(t, table, p)
    front_ladder = contracts.pop("_front_ladder", None)
    gondola_nums = contracts.pop("_gondola", None)          # gondola draft 1 (G1-G8 numbers)
    fine_nums = contracts.pop("_fine", None)                # fine-detail step 3b (F1 numbers)
    sheave_nums = contracts.pop("_sheaves", None)           # nitpick A: ST1 numbers (top stations)
    orient_nums = contracts.pop("_orientation", None)       # nitpick A: O1 numbers (DQ)
    contracts["C10"] = f"{name}: validate_asset_name + lint ok"
    bb_lo = _co(t.data).min(axis=0)
    bb_hi = _co(t.data).max(axis=0)
    cs2_asset.orient_for_net(t)

    # -- `_Win` split, engine base, budget (C4, C5, C11, C12) --------------------------------
    g1 = p.gondola and GD_REV1
    if g1:
        # revision 1: every curtain-wall bay is a `_Win` room (cs2_bldg.split_rooms, the lodges'
        # allocator), its id fixed by G5 in the authoring frame (rooms -> ids reused by distance)
        rooms = gondola_nums["glazing"]["rooms"]
        win = gd_split_rooms(t, table, rooms, name)
        wc = {"win_": None}
        want_cells = sorted(set(rooms.values()))
    else:
        wc = win_cells(p)
        with _lt_override(WIN_CELLS=wc):
            win = lift_terminal.split_windows(t, name, table)
        want_cells = expected_room_ids(p)
    if win is None:
        raise ContractError("C12: no _Win sub-mesh")
    cells = cs2_validate._win_cells(win.data)
    fparts = _face_parts(t.data)
    still = sum(1 for fp in fparts if table[fp - 1].startswith(tuple(wc)))
    if sorted(cells) != want_cells or still:
        raise ContractError(f"C12: _Win cells {cells} (want {want_cells}), {still} pane faces "
                            f"left in main")
    contracts["C12"] = (f"_Win room cells {sorted(cells.items())} (want {want_cells}: "
                        + ("revision 1: one room per curtain-wall bay + the operator room, ids reused "
                           "beyond 3 m" if g1 else
                           "operator room 7, hall glazing band 13" if p.gondola else
                           "cabin 7, housing windows 13") + "), no pane left in main")
    zmin = float(_co(t.data)[:, 2].min())
    if zmin < -1e-5:
        raise ContractError(f"C4: min z {zmin:.5f} below grade")
    contracts["C4"] = f"min z {zmin:.5f}"
    r = cs2_validate.ground_ring_report(t)          # C5 pre-bake (geometry only)
    miss = [n for n in p.engine_base if n not in r["ok_parts"]]
    if r["verdict"] != "OK" or miss or r["blocked_edges"] or r["below_verts"]:
        raise ContractError(f"C5 engine base: {r['verdict']}, missing {miss}, "
                            f"offending {r['offending_parts']}")
    n0 = cs2_asset.tri_count(t)
    if n0 > p.lod0_cap:
        raise ContractError(f"C11: LOD0 {n0} tris > cap {p.lod0_cap}")
    contracts["C11"] = f"LOD0 {n0} <= {p.lod0_cap}"
    d1 = DETAIL1 and not p.gondola
    if d1 and n0 > DETAIL_LOD0_TARGET[p.kind]:
        raise ContractError(f"C11 detail pass 1: LOD0 {n0} tris > target {DETAIL_LOD0_TARGET[p.kind]}")
    if d1:
        contracts["C11"] += f", detail pass 1 target {DETAIL_LOD0_TARGET[p.kind]}"
    if g1 and n0 > GD_LOD0_TARGET[p.kind]:
        raise ContractError(f"C11 gondola revision 1: LOD0 {n0} tris > budget {GD_LOD0_TARGET[p.kind]}")
    if g1:
        contracts["C11"] += f", revision 1 budget {GD_LOD0_TARGET[p.kind]} (ROUND-SPEC section 4)"
    for k in ("C4", "C10", "C11", "C12"):
        print(f"[contract] {k} ok: {contracts[k]}")

    # -- bake (MUST-CHANGE 3: material_sources dict straight into bake_unique) --------------
    A = args.atlas if args.finish else BLOCKOUT_ATLAS
    ao = args.ao_samples if args.finish else 0
    rects = list(label_rects(p).values())
    # MC7b: keep the UNWRAPPED generator hook (its .inner is lift_terminal's compose closure with
    # .density / .height_range); a weathering wrapper's .inner is this hook, not that closure
    dq_hook = make_hook(t, table, p) if args.finish else None
    hook = dq_hook
    share = None
    if d1 and args.finish:
        LD = _LD()
        # MC6: bay sharing on the bake mesh (after split_windows), never a prefix with an active
        # DetailSpec (detail_height sums world-anchored relief per face), labels / boards / sign
        # bands / lamps, the hero list or a pinned face; grade-contact copies stack apart
        share = LD.share_identical(t, table, LD.DQ_SHARE_WHITELIST, detail_specs=detail_specs_dq(),
                                   hero=LD.DQ_SHARE_HERO, reserved=rects, uv_layer=SEED_LAYER,
                                   ground_band=(DQ_WEATHER["ground_z"], 0.60))
        if args.weather == "on":
            # MC7a: roles by material INDEX (the joined mesh's CS2SRC_<key> slots, read before the
            # bake); labels + sign live in reserved rects (restored 1:1); compose_contact off
            import cs2_sheaves as S
            roles = LD.lift_mat_roles(t.data, {**LD.DQ_MAT_ROLES, **S.wheel_roles()})
            hook = SteppedWeather(dq_hook, lambda s: dq_weather_spec(name, s), LD.INTENSITY_STEPS,
                                  mat_roles=roles, detail_specs=detail_specs_dq(), reserved=rects,
                                  compose_contact=False)
    if g1 and args.finish and args.weather == "on":
        # revision 1: the station preset at the highest budget-clean step (DECISIONS 171), roles by
        # material INDEX, the sign + label rects protected, compose_contact off (no bay sharing)
        LD = _LD()
        roles = LD.lift_mat_roles(t.data, gd_mat_roles())
        hook = SteppedWeather(dq_hook, lambda s: gd_weather_spec(name, s), LD.INTENSITY_STEPS,
                              mat_roles=roles, detail_specs=detail_specs_gd(), reserved=rects,
                              compose_contact=False)
    # 6 px at 2048 (lift_terminal PACK_MARGIN_PX); scaled with A, because ~1,250 islands at a
    # 512 blockout with a 6 px margin leave the margins ~70 % of the square (measured ~1 px/m)
    margin = max(2, int(round(PACK_MARGIN_PX * A / 2048.0)))
    # 2026-10-12: the _Emissive map only while a modelled lamp_ part exists (fine-detail DQ: none, the map was
    # all black). The bake never deletes a file, so a stale <name>_Emissive.png from an earlier export goes now
    # (else it would stay in the folder and in the manifest's `files`, the L3 lesson).
    emis_map = has_emissive_map(table)
    stale_emis = os.path.join(asset_dir, f"{name}_Emissive.png")
    if not emis_map and os.path.isfile(stale_emis):
        os.remove(stale_emis)
        print(f"[detach] no lamp_ part: removed the stale {os.path.basename(stale_emis)} (no _Emissive map)")
    report = cs2_bake.bake_unique(t, asset_dir, name, A, sources,
                                  reserved=rects,
                                  margin_px=margin, ao_samples=ao, post_compose=hook,
                                  emissive=emis_map, src_uv=SEED_LAYER,
                                  **({} if share is None else {"share": share}))
    base_report = engine_base_gate(t, p)
    contracts["C5"] = (f"engine base {base_report['verdict']}, {base_report['ok_edges']} OK edges, "
                       f"parts {sorted(p.engine_base)} all OK")
    print(f"[contract] C5 ok: {contracts['C5']}")
    cs2_asset.export_fbx(t, os.path.join(asset_dir, f"{name}.fbx"))
    cs2_submesh.export_submeshes(win, name, "Win", asset_dir, lods=(1, 2) if args.lods else ())

    lods = []
    if args.lods:
        t1, t2 = lod_targets(t, table, n0, p)
        import cs2_sheaves as S
        lods = cs2_asset.make_lod_chain(t, name, targets=(t1, t2),
                                        delete_prefixes=lod_lists(p),
                                        shade_angle=SHADE_ANGLE, fixed=S.lod_fixed())   # T62 round 2: WHEEL_LOD
        for i, lod in enumerate(lods, start=1):
            cs2_asset.export_fbx(lod, os.path.join(asset_dir, f"{name}_LOD{i}.fbx"))
    # F11: LOD meshes inherit MeshFlags.Base (1.6.0f1 ObjectInitializeSystem.cs:1457) and draw
    # their own engine base, so C5's ground ring holds on every level, not on LOD0 only
    lod_base = {}
    for i, lod in enumerate(lods, start=1):
        rl = cs2_validate.ground_ring_report(lod)
        miss = [n for n in p.engine_base if n not in rl["ok_parts"]]
        if rl["verdict"] != "OK" or miss or rl["blocked_edges"] or rl["below_verts"]:
            raise ContractError(f"C5 engine base on LOD{i}: {rl['verdict']}, missing {miss}, "
                                f"{rl['blocked_edges']} blocked, {rl['below_verts']} below, "
                                f"offending {rl['offending_parts']}")
        # Revision 2: the nose ladder's footing is a ground part wherever its rails are (LOD1);
        # Revision 1c (base): the ground flight's footing lfoot_gf
        foot = "lfoot_gf" if front_ladder is not None and "ground_flight" in front_ladder else "lfoot_nz"
        foot_ok = int(rl["ok_parts"].get(foot, 0))
        if front_ladder is not None and i == 1 and foot_ok < 4:
            raise ContractError(f"R2a LOD1: the ground ladder footing {foot} has {foot_ok} OK "
                                f"engine-base edges (its rails stay on LOD1)")
        lod_base[i] = {k: rl[k] for k in ("verdict", "ok_edges", "blocked_edges")}
        print(f"[contract] C5 LOD{i} ok: engine base {rl['verdict']}, {rl['ok_edges']} OK edges"
              + (f", footing {foot} {foot_ok}" if front_ladder is not None else ""))
    cs2_asset.write_settings(asset_dir, name, supplied_lods=tuple(range(1, len(lods) + 1)))
    tris = {0: cs2_asset.tri_count(t)}
    tris.update({i: cs2_asset.tri_count(l) for i, l in enumerate(lods, start=1)})
    tris_win = cs2_asset.tri_count(win)
    print(f"[detach] tris LOD0 {tris[0]}, chain {[tris[i] for i in sorted(tris) if i]}, "
          f"_Win {tris_win}")
    # detail pass 1: what the shipped layout delivers (density classes on UVUnique, the lit
    # emissive texels, the labels, the weathering ladder, bay sharing, the drawn relief)
    detail1 = None
    if (d1 or g1) and args.finish:
        detail1 = detail1_block(t, table, p, A, asset_dir, name, dq_hook, hook, share, report)

    # -- previews --------------------------------------------------------------------------
    tint = GD_TINT if g1 else {"R": CANOPY_RGB, "G": cs2_asset.PAD_RGB, "B": CANOPY_RGB}
    if args.preview:
        prev = os.path.abspath(args.preview)
        ab = args.ab
        if ab is None and not p.gondola:     # gondola: no earlier generation to A/B against
            cand = os.path.join(REPO, "assets", "import", "stations", "fixedgrip", "L",
                                "SkiLiftBaseStationL" if p.kind == "base" else "SkiLiftTopStationL")
            ab = cand if os.path.isdir(cand) else None
        modes = ["pbr", "dusk", "normals", "density"] + (["lod"] if lods else []) + \
                (["ab"] if ab else [])
        if p.gondola:
            # gondola draft 1: + a low head-on 'portal' view of the line side (post-orient the
            # nose is +Y); the DQ soffit 'under' view has nothing to show on a walled hall
            views = list(cs2_preview.DEFAULT_VIEWS) + [("back", (-1.0, -0.8, 0.35)),
                                                       ("portal", (0.12, 1.0, 0.07))]
        else:
            views = list(cs2_preview.DEFAULT_VIEWS) + [("back", (-1.0, -0.8, 0.35)),
                                                       ("under", (0.25, -1.0, 0.04))]
        if g1:
            # revision 1: the technical modes from the shared rig (main mesh only); the lit views, the
            # two-tone, the named views and the close-ups with the `_Win` + vanilla stand-ins here
            for f in cs2_preview.preview(t, asset_dir, name, prev,
                                         modes=tuple(m_ for m_ in modes if m_ not in ("pbr", "dusk")),
                                         views=views, lods=lods or None, prev_generation=ab, tint=tint):
                print(f"[detach] preview -> {f}")
            for f in render_gd_previews(t, asset_dir, name, prev, p, tint, win, views):
                print(f"[detach] preview -> {f}")
        else:
            for f in cs2_preview.preview(t, asset_dir, name, prev, modes=tuple(modes), views=views,
                                         lods=lods or None, prev_generation=ab, tint=tint):
                print(f"[detach] preview -> {f}")
            for f in cs2_preview.preview(t, asset_dir, name, os.path.join(prev, "twotone"),
                                         modes=("pbr",), views=list(cs2_preview.DEFAULT_VIEWS),
                                         tint=TWOTONE):
                print(f"[detach] preview (two-tone) -> {f}")
            for f in render_closeups(t, asset_dir, name, prev, p, tint, win=win):
                print(f"[detach] preview -> {f}")
        if d1:
            for f in render_d1_closeups(t, asset_dir, name, os.path.join(prev, D1_CLOSEUP_DIR), p, tint,
                                        win=win):
                print(f"[detach] preview -> {f}")
            # DQ follow-up: the lamp posts as stand-in boxes + the board sequence in frame
            for f in render_d1_closeups(t, asset_dir, name, os.path.join(prev, FU_CLOSEUP_DIR), p, tint,
                                        win=win, shots=fu_closeup_shots(p), tag="fu"):
                print(f"[detach] preview -> {f}")
        if g1:
            for f in render_fu_gondola(t, asset_dir, name, os.path.join(prev, FU_CLOSEUP_DIR), p, tint, win):
                print(f"[detach] preview -> {f}")
        t.data.materials.clear()

    # -- numbers ---------------------------------------------------------------------------
    nums = prefab_numbers(p, (bb_lo, bb_hi))
    nums["emissive"]["lamps"] = sum(1 for n in table if n.startswith("lamp_"))
    if not emis_map:
        # no map -> the importer writes no EmissiveProperties: the surgery has nothing to set (the purpose-72 /
        # intensity-0.6 pair of the lit builds is kept as `was` for the ledger, not as a value to paste)
        was = {k_: nums["emissive"][k_] for k_ in ("purpose", "intensity")}
        nums["emissive"] = {"map": None, "purpose": None, "intensity": None, "lamps": 0, "was": was,
                            "why": NO_EMISSIVE_WHY}
    print_prefab_numbers(nums)
    switch = None
    if lods:
        _c, size = lift_tower.world_bounds(t)
        switch = cs2_preview.lod_switch_table(size, [tris[i] for i in sorted(tris)])

    # -- validation, then the manifest (records it), then the pack (pipeline review 2: the
    # lift_terminal.py / lift_chair.py order, so a pack failure never leaves new files beside
    # a stale manifest; validate_asset_folder raises on any ERROR) -------------------------
    validation = None
    if not args.no_validate:
        # free the names so the validator's re-imports do not land as `<stem>.001`; the LODs
        # stay alive (renamed) for the review pack below, as lift_terminal.py:1327-1328
        t.name, t.data.name = name + "Src", name + "Src"
        win.name, win.data.name = name + "WinSrc", name + "WinSrc"
        for i, lod in enumerate(lods, start=1):
            lod.name, lod.data.name = f"{name}LOD{i}Src", f"{name}LOD{i}Src"
        findings = cs2_validate.validate_asset_folder(asset_dir, name, {
            "kind": "building", "lod0_cap": p.lod0_cap, "lod2_cap": p.lod2, "hero": True,
            "expected_lods": tuple(range(1, len(lods) + 1)), "expected_submeshes": ("Win",)})
        n_err = sum(1 for f in findings if f[0] == "ERROR")
        n_warn = sum(1 for f in findings if f[0] == "WARN")
        validation = {"errors": n_err, "warnings": n_warn,
                      "warn_checks": sorted({f[1] for f in findings if f[0] == "WARN"})}
        print(f"[detach] validate_asset_folder: {n_err} errors, {n_warn} warnings")

    # MC7b: the density report / relief range live on the UNWRAPPED hook's inner closure
    density = dq_hook.inner.density if dq_hook is not None else None
    stamps = dq_hook.stamps if dq_hook is not None else None
    fine_block = fine_detail_block(p, name, table, fine_nums) if FINE_DETAIL else None
    cs2_validate.write_manifest(asset_dir, name, {
        "generator": GENERATOR, "kind": "building", "variant": p.variant,
        "spec": "dq-terminal.md + dq-terminal-skeptic.md MUST-CHANGE 1-13 + Revision 1 "
                "(verdicts-2026-10-11.md #3-4: front ladder, one underside ladder, gangplank, "
                "no left-flank railings, sign band, housing windows) + Revision 1b polish "
                "(vector plate digits, plate-edge relief / occlusion) + Revision 2 (second "
                "look 3': the front ladder extends to the ground)",
        "revision": 2,
        "revision_polish": "1b 2026-10-11: vector Bfont plate digits (anti-aliased, 448 px/m), "
                           "plate-edge relief + emblem panel edge occlusion",
        "revision2": "2 2026-10-11 (second look 3'): the nose ladder runs from a ground footing "
                     "(open-bottom, z 0) through a ladder-way in the landing to the crown, one "
                     "0.30 rung grid, no cage",
        "front_ladder": front_ladder,
        **({"gondola_draft": "1 2026-10-11: walled hall, cabin-silhouette portals, boarding "
                             "platforms + edge stripes, operator room, machinery deck + internal "
                             "stair, nose + roof ladder from a z 0 footing, glazing band, "
                             "portal-face sign, real-cabin sweep (build report 'Gondola "
                             "stations, draft 1')",
            "gondola": gondola_nums} if p.gondola else {}),
        **({"gondola_rev1": "1 2026-10-11 (eighth look): the LONG GLAZED HALL - stone plinth + coping, "
                            "full-height `_Win` glazing between slim steel mullions on both long sides, "
                            "the back and above the portal lintel, ring beams, a shallow standing-seam "
                            "roof on deep eaves (outriggers, cedar soffits, linear soffit lights, snow "
                            "guards, gutters, downspouts), double-door skier entries with canopies and "
                            "ramps, three signs on one rect, vanilla lamp posts / lights / props as "
                            "sub-objects, the detail pass's dressing + labels + weathering, lot 3 x 4 "
                            "(build/gondola-stations-rev1.md)"} if g1 else {}),
        "finish": "shipping" if args.finish else "blockout",
        "atlas_px": A, "ao_samples": ao, "pack_margin_px": margin,
        "tris": tris, "tris_win": tris_win, "parts": len(table),
        "caps": {"lod0": p.lod0_cap, "lod1_max": p.lod1_max, "lod2": p.lod2},
        "materials": sorted(materials_for(p, parts)), "bake_materials": {str(k): v for k, v in
                                                                        report["materials"].items()},
        "detail": sorted(specs_for(p)()),
        "relief_height_m": dq_hook.inner.height_range if dq_hook is not None else None,
        "sign_band": stamps,
        "ctl_seam_texels_scaled": dq_hook.ctl_scaled if dq_hook is not None else None,
        "sign_reserved_uv": {k: [round(v, 8) for v in r] for k, r in sorted(sign_rects(p).items())},
        **({"detail1": detail1} if d1 else {}),
        **({"rev1": detail1} if g1 else {}),
        **({"fine_detail": fine_block} if fine_block is not None else {}),
        # nitpick A (2026-10-13, build/nitpick-a-stations.md): the towers' sheave train at the top's nose (ST1) and
        # the DQ layout round its up lane (O1)
        **({"sheave_trains": sheave_nums} if sheave_nums is not None else {}),
        **({"orientation": orient_nums} if orient_nums is not None else {}),
        "win_cells": want_cells,
        "density_worst": [{"part": table[q - 1] if 0 < q <= len(table) else q,
                           "source_px_per_m": round(s, 1), "shipped_px_per_m": round(d, 1)}
                          for q, s, d in (density or [])[:8]],
        "submeshes": ["Win"],
        "engine_base": {k: base_report[k] for k in ("verdict", "ok_edges", "blocked_edges")},
        "engine_base_lods": lod_base,
        "lod_switch_m": switch,
        "validation": validation,
        "contracts": contracts,
        "contract": nums,
        "params": asdict(p),
    })
    # kit colour variants (2026-10-12): a shipping export stages the variants its kit rows name
    import dress_kit
    dress_kit.stage_from_manifest(asset_dir, name)

    # -- live-review pack: DUPLICATES of the built LOD0..2 + `_Win`, after export, previews,
    # validation and the manifest (write_pack names its copies from `name`, not from the
    # renamed build objects; gate: tests/test_terminal_detach_bake.py --review case) --------
    try:
        if args.review is not None:
            import cs2_review
            cs2_review.write_pack(
                name, asset_dir, dict(enumerate([t] + lods)), {"Win": win},
                meta={"generator": GENERATOR, "args": vars(args), "kind": "building",
                      "caps": {0: p.lod0_cap, 1: p.lod1_max or int(0.5 * n0), 2: p.lod2},
                      "letter": args.letter, "tint": tint, "contract": nums},
                out_dir=args.review or None)
    finally:
        for lod in lods:
            me = lod.data
            bpy.data.objects.remove(lod, do_unlink=True)
            bpy.data.meshes.remove(me)
    print(f"[detach] asset folder: {asset_dir}")
    print(f"PASS lift_terminal_detach: {name} ({p.variant}) LOD0 {tris[0]} tris, chain "
          f"{[tris[i] for i in sorted(tris) if i]}, _Win {tris_win}, engine base "
          f"{base_report['verdict']}, wheel {nums['wheel_local_prefab']}, stub tip -16, "
          f"finish {'on' if args.finish else 'off'}"
          + (f", fine-detail on ({(fine_block or {}).get('rows')})" if FINE_DETAIL else ", fine-detail off"))


if __name__ == "__main__":
    try:
        main()
    except Exception:  # noqa: BLE001 - unambiguous exit status for the harness
        import traceback
        traceback.print_exc()
        sys.exit(1)
