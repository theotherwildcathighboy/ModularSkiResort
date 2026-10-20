"""
tram_station.py - aerial-tram stations for the Modular Ski Resort (asset round 2026-10, P5).

    SkiTramBaseStationA   --role base   5 x 3 lot, Jackson Hole base terminal (JH-04/05/07): tall
                                        glazed hall (mullions + horizontal fins) under a deep-eave
                                        metal roof on outriggers, stone piers, clock tower, canopy
                                        on timber V-struts; the uphill end OPEN: galvanised face
                                        portal, guide horns either side of each berth, a moving
                                        platform on a rail across the open face, the dock on a
                                        one-story plinth with an upright painted pictogram; curved
                                        saddle girders on a saddle portal behind the berths carry
                                        the track ropes down into the counterweight shafts; inner
                                        glazed wall + annex door and an operator door at dock level
    SkiTramTopStationA    --role top    3 x 3 lot, Jackson Hole top terminal (JH-02/08/11/12/14/16,
                                        P2P-01): concrete plinth with the track-rope ANCHOR DRUMS on
                                        its side faces, an open steel frame (no roof) carrying the
                                        curved saddle girders, yellow (slot 2) deflection wheels +
                                        vertical bullwheel, goalpost crane beam, maintenance stair,
                                        open grating deck on braced legs, operator shack (_Win 1)

Revision 1 (Richard's first live review, 2026-10-11, verdicts 10 / 11): the build report's
"Revision 1" section lists every change and deviation. The integration levels, stubs and hosted
poses below are unchanged (test_gondola_tram_seams pins them).
Revision 1b (fixups, 2026-10-11; revision1-check-tram-stations F1): contract C14 (part-against-
part clashes, check_part_clashes) and the top's bullwheel frame legs moved inboard of the operator
hut (BULLFRAME_FOOT_X); the deck binoculars moved out of the face struts (SCOPE_X).
Revision 1c (left-platform stair, C15; Richard's fourth look 2026-10-11: "the platform on the left
side has no staircase"): contract C15 (check_reachability) builds the access graph of every walkable
surface and fails a build with a surface no stair / ladder path joins to grade. It found the top's -X
outer deck AND the plinth deck / center deck / service platform orphaned, and the base's catwalk under
the portal girder. Fixed: a -X ground stair on the top (two flights with a mid landing, mirroring the
+X stair) and a grating gangway from the plinth deck to its landing (LEFT_STAIR); a fixed ladder from
the base's center deck to the catwalk (PCAT_LADDER). Both flags False rebuild the 1b LAYOUT (the C15
negatives); the top's flights keep the dropped tread bottoms, so LEFT_STAIR False gives 12,678 tris
against the 1b file's 12,804 (126 fewer: revision1c-check-tram-stair N5).
Detail pass 1 (lifts, 2026-10-11; design/detail-pass-1-lifts.md as amended by its skeptic MC1-MC11,
build report build/detail-pass-1-lifts-tram.md): the shared dressing kit (cs2_liftdress) on both
stations (DETAIL1) - base: amber departure board, ticket-window frames + TICKETS, LIFT OPS, EXIT
light, extinguishers, hose reel, platform-edge chevrons, deck nosings, a ski rack with skis, the clock
dials redrawn (resort glyph, hour bars, minute dots); top: red beacon lens, crane hook block,
berth-edge stripes, station name board, rope-tension gauge, extinguisher, hose reel, ice scraper +
shovel - with vanilla sub-objects (SUBOBJ_ROWS, box-tested by check_subobjects), labels painted at
computed px/m (label_weights), the weathering layer at the highest budget-clean intensity
(SteppedWeather, --weather on|off), bay sharing (cs2_liftdress.share_identical, MC6-safe) and the
top's track ropes at 6 segments (MC9 budget). C15 gains a crossing-width term on every join between
two walkable surfaces (>= 0.61 m; the 1c gangway mouth measured 0.574 at sgpost_L0, now moved:
SGPOST0_DEG) and an explicit NOT_WALKABLE list with reasons (an undeclared standable surface fails).
Revision 1e (bay gates vs the cabin doors, 2026-10-11; revision2-check-tram-cabin section C): the tram
cabin's revision 2 put a pair of plug doors centered on each long side (the dock, DOCK_Z -5.50), and the
1d fence grid did not know: the base had a post at z -6.20 inside the door span and a leaf 0.30 m off
the center, the top a post at -5.60, 0.10 m from the seam. The fence is now laid out from the door
(gate_layout: a clear 1.90 m opening centered on the seam, posts at its ends, equal bays out to the
unchanged fence ends) and contract C17 (check_gate_door) reads the door from lift_cabin.py and fails a
post or leaf in the span, an opening under 1.70 m or off the seam. GATES_AT_DOOR False rebuilds the 1d grid.
Revision 2 (2026-10-12; design/tram-stations-rev2.md with its skeptic's MC1-MC9 and the orchestrator's
rulings on Q1-Q8; build report build/tram-stations-rev2.md): "not a total redo" - accuracy and details
against the Snowbird / Jackson Hole photos (ref-liftblog/digest.md section 7). See-through: every sheave
rim's bore is closed (close_rim, RIM_BORE_CLOSED) and the drip strips sit flush (DRIP_LIP) - contracts
C18 (walk-around back-face first hits, LOD0 / LOD1 / LOD2) and C18b (closed rims). Shimmer: same-facing
exact coplanar steel offset by rule (head girders, wheel head box, saddle tie, deck frame T-joints,
X-crossings, stair kit, base braces / piers / trench walls / doors / transoms) - contract C19 (visible
coplanar overlap > 25 cm2 per part pair, labeled from the in-process part table). Top: the yellow
bullwheel to scale (10.4 -> 3.6 m, center 19.30 -> 12.40, 5 broad spokes, galvanised hub), 2.9 m lane
wheels taking the rope over their top, two foot sheaves and an "M" haul loop (haul_route_top, C20), the
crane beam lowered to 18.00-18.70 and shortened to 12.4 m with its lugs over the track lanes, the 8 m mast
dropped (ruling Q3: the bound shrinks 27.00 -> MAST_TOP 20.70; a 1.8 m Snowbird roof mast with a dish, a
panel antenna and the beacon lens stays at LOD1), vanilla LightpolePark / WallLight rows for the square
modeled lamps (Q1), 2.2 m perforated berth gates (Q2; C17 layout unchanged), a medallion resting on the
knee rail in every dock-rail panel, steel-plate deck bays on a 1.2 m seam grid, three trail boards facing
the deck (Q7) and a frame-only guard in front of the wheel (MC6). Base: the drive bullwheel to scale too
(Q6: 10.6 -> 4.0 m, two 2.0 m deflection sheaves behind the saddle girders, JH-04's orange-yellow,
haul_route_base), the A-frame kit ski rack (cs2_liftdress.aframe_rack), the pendant / lantern lamp boxes
gone (the vanilla RoofLight rows hang from ceiling plates, MC3), medallions on every dock-rail panel.
Every other frozen prefab number holds (levels, stubs, docks, hosted cabins, lanes, lot, terraform, base
bounds 25.00); the top's bounds y is the one change (27.00 -> 20.70, a choice: the prefab is not imported).
Fine-detail pass, step 3c (2026-10-12; build/fine-detail-step3c-tram-pali.md; FINE_DETAIL, CLI --no-fine = the
approved revision-2 stations byte for byte): the dressing the dressing kit (props/B) replaces leaves the mesh as "mod"
sub-object rows by object-prefab CID (fd_kit_specs / fd_kit_rows -> cs2_liftdress.sub_object_rows(fine=True), this
generator's +y frame): base 2 extinguishers, the drive-house hose reel, the A-frame rack (SkiDressSkiRackV3 on the
grade); top the hut's extinguisher (moved to the +X face beside the reel: the -Z face has no 0.40 m access), hose reel
and scraper + shovel. The annex door's lamp bar becomes a WallLightShortRangeRandom01 row (FD_ANNEX_LIGHT). Contract F1
(check_kit_rows -> cs2_kitrows.check_kit_rows) tests each row on the REAL kit mesh (mount, contact, access, clearance,
the C4 zones + the T8 cabin-sweep box, the LV pole, every row, the lot); C15 measures its crossings with the kit meshes
(fd_probe). Every LIFT board / label (FS5), the anemometer mast (FS1), the beacon lens (frozen top 20.70), the EXIT
light, gates and medallions stay modeled and vertex-identical.
Nitpick B (2026-10-13; Richard's twenty-fifth look T-1 / T-2, build/nitpick-b-tram.md; photos ref-richard-2026-10-13):
base only (JH_DETAILS; False = the shipped 3c / kit-color base byte for byte; the pass is not FINE_DETAIL-gated, so
--no-fine is the revision-2 base byte for byte only with JH_DETAILS False too). The Jackson Hole details the base lacked,
cheap ones first: the JH-04 canopy on V-shaped timber struts across the village front (a timber-soffit canopy with a dark
metal roof sheet at mid height, one leaning V per front stone pier on a steel shoe, steel ties to the front columns),
LIFT TICKETS over the annex's three grade ticket windows on the clock-tower (+X) side with trim frames and sills, an
ENTRANCE board over the village doors (both on the existing painter: the TICKETS kind with LABEL_WORDS) and a chalkboard
A-frame at the front ticket windows (modeled PLACEHOLDER). The two ramps were STOPPED (the report's section 1): a
<= 1:8 ramp from grade to the base's skier level (5.20 / 6.30 m) needs 42-50 m of run and the +X side has 0.40 m between
the annex and the frozen lot / bound (x 20.00); a slightly inclined top ramp cannot reach grade from the 6.30 m deck
inside the 3 x 3 lot (12.3 m of run: 51 %). The top is unchanged.
Nitpick B, second pass (2026-10-13; build/nitpick-b2-tram-ramps.md; the orchestrator's rulings in verdicts-2026-10-11
"Nitpick B (tram) - first pass landed"): REAL 1:8 RAMPS (RAMPS; False = the nitpick-B base and the shipped top byte for
byte). BASE: the lot grows 5 x 3 -> 6 x 3 with the new cell on the clock-tower (+X) side. A CS2 lot is centered on the
prefab origin, so the building moves BASE_DX = -4.0 m toward -X: every table constant of this module stays in the
DESIGN frame (the lift axis at x 0), assemble() translates the joined base mesh by BASE_DX, and every prefab-frame
number (stubs, docks, hosted cabins, the LV pole, the sub-object / kit rows, wheel centers, bounds) is design + BASE_DX
(pf(); prefab_contract.axis_x = the line axis). A 1:8 switchback ramp (two 2.60 m lanes, 2.42 m clear between the
rails, a 1.60 m turn landing) climbs from the new cell's plaza paving along the clock-tower side of the annex to a top
landing + balcony at the annex cap level 5.20 and a glazed double door cut into the annex's +X glazing (the side area);
two vanilla park lamps and a bin on the paving. TOP: 1:8 ramps (3.90 m grating decks, rails both sides) replace both end
stairs - from head landings at the deck (6.30, z 0.50) along each side to a concrete pad at the back lot edge (z 11.60,
top 5.10); the terrain is molded up to the pads (no level). Contract C15 learns ramps (grade <= 1:8, clear width
between the rails, headroom, a landing >= 1.50 m deep at each end, rails both sides at every station, end crossings
>= 0.70), and C15b counts a ramp foot on a pad at the lot edge as grade on a NO-LEVEL station only (DECISIONS 166
amended); C15 is unchanged for everything else.
Nitpick B3 (2026-10-13; build/nitpick-b2-tram-ramps.md "Annex stair (B2 check F2)"; ANNEX_STAIR, False = the B2 base byte
for byte): the side area got a real way out. The new ramp's glazed door enters the annex at its 5.20 floor, but the annex's
only inner door to the hall (sfront_*, sill 6.30) had nothing between (C15 listed annexcap NOT_WALKABLE and could not see the
dead end). Now: annexback is cut at the inner door (annexback_0..2: the doorway z -7.70..-5.50 up to the door head), a
concrete landing block (landing_annex) carries the hall door's sill 6.30 across the 0.47 m gap between the buildings and
through the wall, and a straight concrete stair (tread_annex_1..6: 7 risers of 0.157 m, 0.30 m goings, 2.20 m wide) comes
down onto the annex floor, with rails both sides (the flight's without kick plates: the solid steps). C15 now treats annexcap
as a WALKABLE surface (it leaves NOT_WALKABLE; ANNEX_STAIR False restores the old entry) and proves the route grade -> ramp A
-> turn landing -> ramp B -> landing -> balcony -> (side-area door) -> annex floor -> stair -> landing -> (hall door) -> deck:
the report's `route` (no other stair may carry it), with the stair >= 1.20 m wide between rails and the doorway clear.

Revision 3 (2026-10-13; Richard's twenty-seventh look T-3: "Can we have the whole FOUNDATION and SUPPORTS go LOWER? I'm
imagining sticking it into a cliff edge, so players will bury some of it and then parts of it stick out and reach further.
Or if they place it on a flat mountain top the whole support structure just gets buried. Also it'll help to clear the
low-down bullwheels."; build/tram-top-rev3.md; PLINTH_TOP, False = the shipped nitpick-B2 top byte for byte): the TOP in
PLINTH MODE, the L4 pattern. No engine base (C6p: no vertex within 0.01 of y 0 on any LOD, min y -12.00 = PLINTH depth on
every LOD, no OK base edge; the prefab gets BaseProperties {m_BaseType: null} and unclamped bounds min y -12), and the
structure itself continues below grade, closed (C6p closed: every open edge of a below-grade part lies on another part's
face). The concrete pier (plinth), both ramp pads, the eight ramp columns, both stair posts and the two rear-portal caps
stand on a FOUNDATION BLOCK (x +-11.70, z -2.85..11.50, top -0.05: the pier's housing widened under the anchor-drum bays and
the ramps to the lot's sides) on a 0.60 m FOOTING SLAB to -12.00; the face-portal and deck-leg caps became closed pile caps
(-0.05..0.80) on two cross-braced steel LATTICE BENTS (photo 01; x +-10.10, columns at z -9.60 / -7.00 / -4.40, girts at
-1.00 / -6.00, X braces in each panel, ties into the block's line face) down to concrete pad footings at -12.00. Above
y 0 every vertex is the shipped top's (test T9); lot, bound 20.70, stubs, docks, lanes, rows, C17, the no-level terraform
block unchanged; prefab delta = bounds min y 0 -> -12 + BaseProperties. LOD1 keeps the plinth and the bents (their X braces
go), LOD2 the block + slab (the plinth reaches -12 on every LOD, as L4's). C15b's ground rule reads "stands on or continues
below the grade". Previews: a sketched cliff edge (cliff_y, render only) and the flat summit (CLOSEUP_SHOTS_R3).

Build contract: docs/research/art/assets-2026-10/design/tram-stations.md with
tram-stations-skeptic.md MUST-CHANGE 1-11 applied and ROUND-SPEC R27-R31 overriding it.
What the rulings changed (everything else is the contract verbatim; the build report
docs/research/art/assets-2026-10/build/tram_station.md lists each deviation):

  R27  hang 10.50 m = cabin underside (pivot) -> carriage top; the track rope sits 0.50 below
       the carriage top (rope = pivot + 10.00). The contract's TRACK_Y 16.00 / HAUL_Y 15.60 are
       KEPT on both stations (every rope-machinery clearance the skeptic re-computed holds at
       that height), so the cabin pivot drops to 6.00 and the boarding deck = pivot + 0.30 =
       6.30 (cabin interior floor, cabins.md 4.1). The skeptic measured the JH deck at ~5 m
       (claim 13), so the lower deck is also the better read; the hall stays 22.00 m.
  R28  stubs: track ropes = HV rider lanes at x +-5.50, haul = non-HV lane at x +-5.00, 0.40
       below. Lane prefabs per R31: SkiTramTrackRope (copy of SkiLiftHaulRopeB, UtilityType 64,
       m_Hanging 0.004) and SkiTramHaulRope (the same with UtilityType 8). The vanilla HV/LV
       cable lanes of the contract are the fallback (manifest `lane_fallback`).
  R29  both cabins hosted parked in the base bays, identity rotation, pivot at the underside:
       (+-5.50, 6.00, -5.50), m_ParentMesh 0 (skeptic MC1: -1 snaps them to ground level).
  R30  carriage/hanger half-width <= 1.10. The BUILT cabin (lift_cabin.py) meets R29 with a
       mirror-symmetric fork: a closed ring of uprights (|dy| 0.82..1.08), crossbar and
       carriage head around the track and haul ropes. Any rope support in the docking path
       would cross that ring's sweep, so the station has NONE there: the ropes run free from
       the stub tips to supports behind the docked cabin, and C4 asserts the whole ring
       (+-1.10 from the cabin roof to the carriage top + 0.20) plus the cabin body clear.
       The contract's face saddles / face haul sheaves are not built (see the module notes
       above `bay_gates`); skeptic MC6's saddle-frame split becomes moot.
  R31  see R28 (the manifest's prefab_contract names the lane prefabs and their hanging).

Frames: every table coordinate is PREFAB-local (X lateral, Y up, Z along the lot, line leaves
toward -Z, village front +Z). Parts are authored through P(X, Y, Z) -> Blender (Z, X, Y) and
the joined mesh is turned by cs2_asset.orient_for_net, exactly as every lift generator.
After orientation: prefab (X, Y, Z) = Blender (x, z, -y).

CLI (Blender 5.2.2 headless only; the generator refuses to run in a UI session):
    blender --background --factory-startup --python-exit-code 1 --python tram_station.py -- \
        --role base|top [--out <letter dir>] [--name N] [--finish] [--lods] [--export]
        [--preview [DIR]] [--review [DIR]] [--atlas auto|2048|4096|<pow2>] [--min-ppm 25]
        [--ao-samples 48] [--clock-tower|--no-clock-tower] [--wheels|--no-wheels]
        [--skirt <m> --skirt-ack-probe] [--area-only] [--cabin <SkiTramCabinA dir>]
        [--carriers catalog/carriers.json] [--no-validate] [--no-fine]
  --export   write <out>/<Name>/ (default out: assets/import/stations/tram/A); without it the
             folder goes to a temp dir (bake, previews and checks still run).
Run order: --area-only -> blockout --preview --review -> verdict -> --finish --lods --export
--preview --review -> tools/assets-check.ps1 -Root assets/import/stations/tram/A -Kind building.

Every geometry contract (C1-C20 + the R30 carrier clear) is asserted; a violated contract
raises and nothing is exported. Deterministic: fixed part order, canonical element order
(cs2_asset.tag_parts), sorted materials, CPU bakes with seed 0.
"""

import argparse
import hashlib
import json
import math
import os
import re
import sys
import tempfile

import bpy
import bmesh
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import cs2_asset     # noqa: E402
import cs2_kit       # noqa: E402
import cs2_uv        # noqa: E402
import cs2_compose   # noqa: E402
import cs2_submesh   # noqa: E402
import cs2_validate  # noqa: E402
# cs2_bake / cs2_preview / cs2_review are imported lazily: cs2_preview imports lift_tower,
# which another fleet is rebuilding; the geometry build and its tests must not depend on it.

REPO = os.path.normpath(os.path.join(HERE, os.pardir, os.pardir))
DEFAULT_OUT = os.path.join(REPO, "assets", "import", "stations", "tram", "A")
DEFAULT_PREVIEW = os.path.join(REPO, "assets", "preview", "stations")
DEFAULT_CARRIERS = os.path.join(REPO, "catalog", "carriers.json")
# revision 2: its own folder (the detail-pass-1 close-ups stay where they were; copies in superseded-rev2-2026-10-11)
DEFAULT_CLOSEUPS = os.path.join(REPO, "assets", "preview", "stations", "rev2-closeups-2026-10-12")

# --------------------------------------------------------------------------------------
# Carrier contract (R27-R31, cabins.md section 4)
# --------------------------------------------------------------------------------------

HANG_M = 10.50                      # pivot (cabin underside) -> carriage top (R27)
ROPE_BELOW_TOP_M = 0.50             # track rope center below the carriage top (R27)
ROPE_ABOVE_PIVOT_M = HANG_M - ROPE_BELOW_TOP_M      # 10.00
TRACK_X = 5.50                      # HV rider lanes (R28; R4 gauge)
HAUL_X = 5.00                       # non-HV haul lane (R28)
HAUL_DY = -0.40                     # (R28)
INTERIOR_FLOOR_M = 0.30             # cabin interior floor above its underside (cabins.md 4.1)
CABIN_HALF_W = 1.85                 # cabins.md 8.1 bounds x +-1.85
CABIN_HALF_L = 4.00                 # cabins.md 8.1 bounds z +-4.00
CABIN_BODY_H = 3.00                 # cabins.md 4.1 s8 roof (+ roof boxes to 3.50)
CARRIAGE_HALF_W = 1.10              # R30
CARRIAGE_HALF_L = 2.80              # cabins.md 4.2 top beams 5.60 long
TRACK_ROPE_R = 0.035                # ~70 mm track rope [K]
HAUL_ROPE_R = 0.025

# --------------------------------------------------------------------------------------
# Station frame (contract sections 2-4, R27 levels)
# --------------------------------------------------------------------------------------

STUB_Z = -12.00                     # line-ward stub tip (curve point a); Phase A StubTipLocalZ
STUB_LEN = 0.25                     # a -> d runs inward
FACE_Z = -10.40                     # outermost non-rope geometry (free air 1.60 line-ward)
DOCK_Z = -5.50                      # parked cabin center
TRACK_Y = 16.00                     # track rope center at the stubs and through the dock
HAUL_Y = TRACK_Y + HAUL_DY          # 15.60
PIVOT_Y = TRACK_Y - ROPE_ABOVE_PIVOT_M              # 6.00 cabin underside at the dock
DECK_Y = PIVOT_Y + INTERIOR_FLOOR_M                 # 6.30 boarding level = interior floor
CARRIAGE_TOP_Y = PIVOT_Y + HANG_M                   # 16.50
LOT = {"base": (5, 3), "top": (3, 3)}
LOT_SLACK = 1e-4                    # C7 float slack only (strict lot fit, review F1)
NAMES = {"base": "SkiTramBaseStationA", "top": "SkiTramTopStationA"}
PREFAB_NAMES = {"base": "SkiTramBaseStationBuilding", "top": "SkiTramTopStationBuilding"}

# Lane prefabs (R31 own lanes; contract section 3 vanilla lanes as the fallback)
LANE_TRACK = {"prefab": "SkiTramTrackRope", "copy_of": "SkiLiftHaulRopeB",
              "m_UtilityType": 64, "m_Hanging": 0.004,
              "fallback": "UnityGUID:5d88ac45a40b30341a79535930e3b735"}
LANE_HAUL = {"prefab": "SkiTramHaulRope", "copy_of": "SkiLiftHaulRopeB",
             "m_UtilityType": 8, "m_Hanging": 0.004,
             "fallback": "UnityGUID:37c8abdb015384f4e8b7ce9b5b2b6c9b"}
LV_POLE_GUID = "UnityGUID:4010d5851d69dbb418c62e6bd4d19759"   # PowerLinePole01 Placeholder

# Base hall
STONE_TOP = 4.80                    # stone ground story
CAP_TOP = 5.20                      # concrete band / pit floor
CAP_DROP = 0.02                     # revision 2 (C19): piers / trench walls stop this far under the caps' top
XCROSS_SHALLOW = 0.04               # revision 2 (C19): the second member of every X-crossing is this much shallower
ROOFLIGHT_Y = 14.45                 # the hall's vanilla RoofLight rows hang from ceiling plates at this height
STAIRFOOT_H = 0.11                  # revision 2 (C19, Z6): stair foot pads 0.02 under the last tread (was 0.15)
STRINGER_INSET = 0.02               # revision 2 (C19, Z6): stringers inside the flight edges
HALL_X = 12.00
HALL_Z = 10.00
ROOF_Z = 10.40                      # roof slab z extent (face end = FACE_Z)
ROOF_HIGH = 22.00                   # top plane at z -10.40 (line face)
ROOF_LOW = 16.00                    # top plane at z +10.40 (village front)
ROOF_T = 0.40
CT_LOC = (16.0, 0.0, 9.40)          # clock tower (contract 5.1)
CT_H = 25.00                        # [task]/[DB] choice; measured ~22 m (skeptic claim 13)
BW_CENTRE = (0.0, HAUL_Y, 2.00)     # base drive bullwheel (haul plane)
BW_R = 5.30                         # top --no-wheels return wheel only (never shipped): rope radius = HAUL_X
# Revision 2 (ruling Q6, JH-04: "two small orange-yellow wheels behind the lower ends of the saddle
# girders", no huge wheel anywhere): the base drive bullwheel shrinks 10.6 -> 4.0 m (rope radius 1.80,
# rim 2.00, closed bore 1.60, 5 broad spokes) in the same horizontal haul plane, and two horizontal 2.0 m
# deflection sheaves (rope radius 0.85) behind the saddle girders turn the x +-5.00 strands inward onto it
# (external tangents, haul_route_base). Rims slot-free JH orange-yellow (`safety`, BASE_WHEEL_MAT).
BASE_BW_R = 2.00                    # drive wheel rim outer radius (diameter 4.00 m; C20b 3.40-4.20)
BASE_BW_RR = 1.80                   # its rope radius
BASE_BW_BORE = 1.60
BASE_BW_W = 0.40
BASE_BW_SEGS = 28
BASE_BW_SPOKES = 5
BASE_DS_X = 4.15                    # deflection sheave centers x +-4.15 (rope at x +-5.00 = HAUL_X)
BASE_DS_Z = -0.80                   # their center z (rim front -1.80 = SADDLE_Z0, behind the docked carriage)
BASE_DS_RR = 0.85                   # rope radius (rim 1.00: diameter 2.0 m, JH-04 "about 2-3 m")
BASE_DS_RIM = BASE_DS_RR + 0.15
BASE_WHEEL_MAT = "safety"           # JH-04 orange-yellow (the base slot 2 is the roof gray)

# Base revision 1 (verdict 10, Jackson Hole JH-04 / JH-05 / JH-07)
EAVE_X = 14.00                      # deep side eaves on outriggers (JH-07; clock-tower shaft at x 14.25)
ROOF_Z1 = 11.20                     # village-end eave (lot edge 12.00)
TRENCH_Z0, TRENCH_Z1 = -10.00, -8.40  # moving-platform pit across the open face (JH-04)
TRENCH_Y = 3.60                     # pit floor: the plinth face below it carries the pictogram
RAIL_Z = (-9.55, -8.75)             # the moving platform's rails, running ACROSS the open face
PORTAL_X = 10.90                    # galvanised face-portal columns (JH-07), x +-(10.60..11.20)
GIRDER_HW = 0.18                    # saddle-girder half width (x lane +-0.18: clear of a bullwheel rim at 5.30)
SADDLE_Z0 = -1.80                   # saddle girders start here: the real cabin's carriage reaches
                                    # z -2.66 at the dock (+-2.84 about -5.50), the C4 zone -2.55
BASE_SADDLE = (-1.40, 8.00, 40.0)   # base: rope leaves the level run at z, radius, leave angle (deg)
ANNEX_DOOR_Z = (-7.70, -5.50)       # +X inner wall: double door to the annex (side building)
STAIR_DOOR_Z = (-2.30, -0.25)       # -X wall: the stair-landing door opening (inner leaf added)

# Top exoskeleton
HEAD_Y0, HEAD_Y1 = 17.10, 18.30     # portal head beams (envelope top 16.70)
REAR_Z = -2.00                      # rear portal (moved from -0.40: the saddles / wheels sit behind it)
TOP_PLX = 5.85                      # concrete plinth half width (drums on its side faces, JH-08/14)
TOP_CAPX = 5.95
TOP_SADDLE = (-1.60, 6.00)          # top: rope leaves the level run at z, radius
DRUM_C = (7.60, 2.80)               # anchor drum center (z, y) on each plinth side face (P2P-01)
DRUM_RR = 1.90                      # rope-center radius on the drum
DRUM_X = 6.40                       # rope / drum mid-plane (disc x 6.05..6.75)
DRUM_WRAP = 320.0                   # degrees of wrap (with a 0.24 m helix along x)
# haul (revision 2, design T1-T4 as amended by its skeptic MC9; JH-08 / JH-11 / JH-12 / JH-14): the
# vertical LANE wheels (y-z plane, x +-HAUL_X, 2.90 m) take each strand over their TOP and send it DOWN
# in the haul plane z = BULL_Z behind them; two small FOOT sheaves (x-y plane, above the hut roof) turn
# it inward and it rises on the internal tangent onto ONE vertical yellow bullwheel (x-y plane, axle
# along z, 3.60 m) and over its top: an "M" (haul_route_top). The wheel's plane and the crane beam's
# orientation already matched the photos (the digest's T2 / T4 were misreads, design 1.4); its SIZE
# and HEIGHT did not (10.4 m, center 2.7 m ABOVE the crest; JH: 3.4-4.2 m, center ~2 m BELOW it).
DW_RR = 1.30                        # lane-wheel rope radius (rim 2.90 m, JH-08 ~3.0)
DW_Y = HAUL_Y - DW_RR               # 14.30: the rope arrives on TOP of the lane wheel
BULL_Z = 1.60                       # haul plane = bullwheel plane (lane wheels: z -1.15..1.75)
DW_Z = BULL_Z - DW_RR               # 0.30 (asserted in check_constants)
BULL_Y = 12.40                      # bullwheel center: 3.60 below the crest (TRACK_Y), 6.10 above the deck
BULL_RIM = 1.80                     # rim outer radius: diameter 3.60 m (C20: 3.40-4.20)
BULL_RR = 1.60                      # its rope radius
BULL_BORE = 1.40                    # closed bore (S1)
BULL_SEGS = 28
BULL_SPOKES = 5                     # JH-12: 4-6 broad flat spokes (C20)
BULL_HUB_R = 0.70                   # JH-12: hub 1.3-1.5 m (galvanised, digest 7.1)
FS_X, FS_Y, FS_RR = 4.50, 10.40, 0.50   # foot sheaves (x-y plane) at x +-FS_X: outer tangent x = HAUL_X
FS_RIM = FS_RR + 0.15
HAUL_SEGS = {"lane": 3, "foot": 3, "bull": 5}   # rope arc segments (skeptic MC9: 888 tris)
GOAL_Y0, GOAL_Y1 = 18.00, 18.70     # goalpost crane beam (JH-11 / 12 / 14: ~2 m above the crest)
GOAL_X = 6.20                       # beam half length (12.4 m, about the lane gauge; lugs over the lanes)
GOAL_Z = (5.00, 5.60)
MAST_H = 1.80                       # ruling Q3: a short Snowbird roof mast (SB-01 / 07 / 20 / 21) on the beam
MAST_TOP = round(GOAL_Y1 + MAST_H + 0.20, 6)    # 20.70 = bounds top: the beacon lens (BEACON_H) on the mast
ANT_DISH_Y = 19.25                  # the roof mast's dish (faces -Z, down the line) and panel antenna (+X side)
ANT_PANEL_Y = (19.60, 20.05)
CWGUARD_X = 1.95                    # the frame-only guard in front of the bullwheel: posts at x +-CWGUARD_X
SVC_Y = 11.70                       # maintenance platform behind the bullwheel (30 x 0.18 above the deck)
HUT = (1.50, 4.90, -0.40, 2.60, DECK_Y + 3.00)   # operator hut x0, x1, z0, z1, wall top
HUT_ROOF_TOP = HUT[4] + 0.25        # 9.55
# Revision 2 (shimmer, C19): the head girders stop HEADGIRDER_INSET under the head beams' top and inside
# their outer faces (design Z1; 0.0 rebuilds the 1e coplanar head frame, the C19 negative)
HEADGIRDER_INSET = 0.06
SGTIE_CLEAR = 0.15                  # the saddle tie's top under the lane-wheel rims (Z3)
# Revision 1b (revision1-check-tram-stations F1): the bullwheel frame legs stand INBOARD of the
# operator hut (x 1.50..4.90; roof + drip to x 1.33). The R1 feet at x +-2.60 put bullframe_R
# through the hut and its roof (52 triangle pairs). At +-0.90 the legs (0.40 square, raking to
# the axle head at +-0.35) keep >= 0.36 m from the hut group; C14 asserts >= 0.30.
BULLFRAME_FOOT_X = 0.90
SCOPE_X = 8.60                      # deck binoculars (were 9.40: inside strut_face_L/R, C14)
TOP_BW_Z = 0.00                     # --no-wheels: horizontal return bullwheel (clear of the goalposts)
# Revision 1c (left-platform stair, C15). The top's -X outer deck (grate_bayL) had no stair, and the
# plinth deck, the center deck and the service platform reach nothing but the cabins. A ground stair
# off the -X outer deck's back corner mirrors the +X one, in two flights (3.06 + 3.24 m: no flight
# rises more than 3.7 m) with a mid landing, and a grating gangway behind the -X berth joins the plinth
# deck to its top landing (the landing runs back to the gangway's back edge, z 0.50). False rebuilds
# the 1b top's LAYOUT (the C15 negative in the test), not its file: the flights keep the dropped tread
# bottoms, 12,678 tris against the 1b file's 12,804 (126 fewer; revision1c-check-tram-stair N5).
LEFT_STAIR = True
LS_FLIGHT_X = (-11.70, -10.10)      # the -X flights (mirror of the +X stair's 10.10..11.70)
LS_LANDING = (-11.70, -10.20, -1.60, 0.50)    # top landing x0, x1, z0, z1 (beside bayL + the gangway)
LS_RISERS = (17, 18)                # 0.18 risers: 3.06 + 3.24 m
LS_GOING = 0.25                     # as the +X stair
LS_MID = 1.20                       # mid landing along the run
GANG = (-10.20, -TOP_CAPX, -0.70, 0.50)       # gangway x0, x1, z0, z1 at the deck level
# The base catwalk under the portal girder (grate_pcat, 17.20) had no access: a fixed ladder from the
# center deck in the lattice towers' ladder language (tram_tower.LADDER: rails 0.05 square at +-0.21,
# triangular rungs r 0.018 every 0.30 from 0.25 up to the floor, rails 1.10 above it), its rung plane
# 0.20 behind the catwalk's open back edge (z -8.00), climbing side +Z, clear of the lamp at x 0.
# False rebuilds the 1b base (the C15 negative).
PCAT_LADDER = True
PCAT_Y = 17.20
PCAT_LADDER_X = 1.50
PCAT_LADDER_Z = -7.80
# NOTE (revision1c-check-tram-stair N4, detail pass 1): the catwalk ladder's rails are 0.37 m apart
# inside (0.42 center to center), the lattice towers' ladder language; common standards ask for about
# 0.40 m (unverified, no citation). Kept as is to match the tower ladders; C15 does not gate width.
# Detail pass 1 (N1, revision1c-check-tram-stair): the gangway from the -X stair meets the plinth cap
# through the gap between the `capf` rail (inner face z -0.626) and saddle-girder post sgpost_L0. At the
# 1c angle (70 deg on the girder arc, z -0.052..0.248) that opening was 0.574 m < 0.61. The first post
# pair (and the tie under it) moves to 68.5 deg: post z 0.070..0.370 -> 0.696 m; the tie keeps 0.08 m
# from the deflection-wheel posts (dwpost, z 0.45). C15 now measures every join's crossing width.
SGPOST0_DEG = 68.5
# Detail pass 1 (skeptic MC9: the top had 42 tris of headroom under its 14,000 cap): the top's two track
# ropes drop from 8 to 6 tube segments (-584 tris, 1,168 -> 876 each; a 35 mm rope at 6 sides is
# indistinguishable at any game distance). The base keeps 8 (it has the budget).
TRACK_ROPE_SEGS = {"base": 8, "top": 6}
# T76 (--tiled, docs/research/art/tram-tile-route-plan-2026-10-18.md Q2, Richard 2026-10-18): the base's track ropes
# drop to the top's 6 sides (-272 tris) so the tiled base fits its 16,000 cap. TILED is set by apply_tiled() from the
# run's args (main / build_checked), like FINE_DETAIL; flag off reads TRACK_ROPE_SEGS as before.
TILED = False
TILED_TRACK_ROPE_SEGS = 6
# T76 R2 (parent's call on D4, 2026-10-18): under --tiled the unique bake is the LOD1 / LOD2 far set (seen past ~250 m);
# its weathering ladder starts lower so a 1024 far set (the top) finds a budget-clean step instead of a 2048 atlas
TILED_FAR_WEATHER_STEPS = (0.10, 0.15, 0.20, 0.25, 0.30, 0.35)
# Detail pass 1: the dressing (cs2_liftdress builders + painted labels). False rebuilds the 1c
# geometry plus the N1 / rope changes (tests compare against it).
DETAIL1 = True
# Fine-detail pass, step 3c (2026-10-12; build/fine-detail-step3c-tram-pali.md; design/fine-detail-pass.md A.2 / B /
# D as cut by its skeptic FS1 / FS5 / FS6 / FS7). FINE_DETAIL False (CLI --no-fine; main() / build_checked() set it
# from their args every run) = the approved revision-2 stations byte for byte. True: the host-modeled dressing the
# dressing kit (props/B) replaces leaves the mesh and becomes "mod" sub-object rows (fd_kit_specs: the base's two
# extinguishers, the drive-house hose reel and the A-frame ski rack, the top's hut extinguisher, hose reel and
# scraper + shovel), the annex door's lamp bar becomes a vanilla WallLightShortRangeRandom01 row (FD_ANNEX_LIGHT);
# contract F1 (cs2_kitrows.check_kit_rows) tests every kit row on the REAL kit mesh, and C15 counts the kit meshes
# as obstacles (cs2_kitrows.probe_object). Stays modeled: every LIFT board / label (FS5: host atlas), the roof
# mast's anemometer arm (FS1), the beacon lens (it sets the frozen top bound 20.70), the green EXIT light (no
# vanilla exit sign), the gates (boxes stay boxes; C17 unchanged), the medallions (FD_MEDAL_RING below).
FINE_DETAIL = True
FD_CLOSEUP_DIR = "fine-detail-2026-10-12"
# the base's one A-frame rack: the kit loadout R3 (8 pairs + 2 boards + 2 pole pairs, 600 tris), the closest to
# the removed rack's 10 items and the cheapest; the kit racks alternate by host (dress-kit.md 1.2)
FD_RACK = "SkiDressSkiRackV3"
#: kit color variants (Richard 2026-10-12 ~18:50; build/kit-colour-variants.md): the color set
#: (fine_detail.COLOUR_SETS) of the colored kit rows per role - the base rack goes back to revision 2's red /
#: white look (TRAM_RACK_GEAR: ski_red / trim_white; red = the base's slot-3 red, TINT['base']['B']); the top has
#: no colored row
FD_KIT_COLOUR = {"base": "red", "top": "red"}
FD_TOP_EXT = 1.90           # the top's kit extinguisher: z on the hut's +X face (the reel's plate spans 0.65..1.35)
# the annex door's lamp bar (lamp_annexdoor, x 11.57..11.87, y 9.40..9.58, z -7.00..-6.20) -> a short-range
# wall light on the glazing over the door, lens center on the bar's (pivot 0.02 off the face, as the entrance rows)
FD_ANNEX_LIGHT = ("WallLightShortRangeRandom01", (HALL_X - 0.13 - 0.02, DECK_Y + 2.80 + 0.39,
                                                   (ANNEX_DOOR_Z[0] + ANNEX_DOOR_Z[1]) / 2.0), "-X", 0, "wall")
# the medallion ring (design B "if cheap"): a 6-gon annulus is 48 tris against the 20-tri disc, +448 tris on the
# top (16) / +476 on the base (17); the top would keep ~200 of its 14,000-tri cap and a 6-gon ring still reads
# hexagonal at the 10 m minimum zoom - not cheap: the approved discs stay
FD_MEDAL_RING = False
# Nitpick B (2026-10-13; Richard's twenty-fifth look T-1: "Let me know if I've missed any other significant details we
# should add"; build/nitpick-b-tram.md; JH photos ref-richard-2026-10-13 03 / 04, ref 07): the Jackson Hole details the
# base lacked, cheap ones first. False = the shipped 3c / kit-color base byte for byte (no top part reads it).
#   JH_CANOPY   the JH-04 canopy across the hall's village front at mid height: a timber soffit slab (jhcanopy) under a
#               dark metal roof sheet (jhroof, slot 2), from just in front of the front wall's X-braces (their front
#               face z 10.26) to 0.05 inside the lot edge; steel ties (jhtie_) into the front columns carry its back
#               edge. The roof's eave above (z 11.20, y ~15.4) leaves the canopy's 1.65 m projection in the open.
#   JH_V        one timber V per front stone pier (JH_V_PIERS = stonepier_6..9): both legs from one foot on a steel
#               shoe (jhshoe_, on the pier top 5.18, overhanging its front: the braces stand 0.04 behind the strut
#               feet), leaning out 0.48 m to the canopy soffit (JH-04: the V's lean out toward the canopy edge); the
#               second leg XCROSS_SHALLOW shallower (C19, as the entry canopy's vstrut_ pairs); the end V's narrower
#               (they keep 0.12 m inside the downspouts at x +-12.39)
#   LIFT TICKETS on the annex's +X (clock-tower side) stone face over its three grade ticket windows (tickwin_a0..2),
#               with trim frames + sills like the front pair (JH-04 "Lift Tickets", right of the clock tower)
#   ENTRANCE    over the village doors under the entry canopy (ref 07's ENTRANCE board)
#   chalkboard  an A-frame stand at the front ticket windows (ref 07 / JH-04's A-frame): two timber leaves with black
#               chalk faces. PLACEHOLDER (DECISIONS 183): the only vanilla A-frame is AStand01 (a brand-spawned ad stand:
#               its variants AStand01 - <brand> carry BrandObject; placeholder GUID 655ebd65... inferred from the dump,
#               unverified on a host without a company) - swap at the first import look if Richard prefers an ad stand
JH_DETAILS = True
JH_CANOPY = {"x": 12.25, "z0": 10.30, "z1": 11.95, "y0": 8.80, "y_back": 9.40, "y_front": 9.10, "roof_t": 0.08}
# (a timber wedge: flat soffit, 0.60 deep at the wall, a 0.30 fascia; its roof sheet flush on top - the wedge's top and
# the sheet's bottom are dropped (hidden against each other: atlas, not geometry), the two shells weld at the seam)
JH_V_PIERS = (-11.40, -6.00, 2.40, 11.40)      # x of the front stone piers stonepier_6..9 (z 10.00..10.40, top 5.18)
JH_V = {"foot_y": 5.27, "foot_z": 10.44, "head_y": 8.86, "head_z": 10.92, "spread": 0.85, "spread_end": 0.70,
        "w": 0.28, "d": 0.28}       # the foot's back face 0.04 in front of the X-braces (z 10.26); end V heads inside x 12.25
JH_SHOE = {"hw": 0.30, "y0": 5.18, "y1": 5.24, "z0": 10.27, "z1": 10.64}   # z0: 0.02 in front of the columns (10.25)
JH_TIES_X = (-12.0, -6.0, 0.0, 6.0, 12.0)    # the front columns col_4 / col_10..12 / col_9 (z 9.75..10.25)
JH_TICKWIN_A = (-4.0, 0.0, 4.0)              # the annex ticket windows' z centers (tickwin_a, +-0.80, y 1.20..3.00)
JH_CHALK = {"x": -7.90, "z": 11.30, "w": 0.60, "h": 0.95, "foot": 0.25, "t": 0.03, "y0": 0.02}
# the two new boards (cs2_liftdress.board, wall mount): name -> (label center (prefab), facing, w, h)
JH_ANNEX_X = 19.60                           # the annex's +X stone face (build_base ax1)
JH_BOARDS = {"lifttickets": ((JH_ANNEX_X + 0.023, 3.40, 0.0), "+X", 2.20, 0.30),
             "entrance": ((6.00, 3.45, HALL_Z + 0.023), "+Z", 1.60, 0.30)}
# Nitpick B, second pass (2026-10-13; build/nitpick-b2-tram-ramps.md; module docstring). False = the nitpick-B base and
# the shipped top byte for byte (lot 5 x 3, no frame shift, the base's 0.40 m strip, the top's two end stairs).
RAMPS = True
# base: the 6 x 3 lot keeps its center on the prefab origin, so the building moves BASE_DX (the new cell is x 16..24 in
# the prefab frame = x 20..28 in this module's DESIGN frame). pf() / frame_dx() apply it; nothing else changes.
BASE_DX = -4.0
LOT_RAMPS = {"base": (6, 3), "top": (3, 3)}
TERRAFORM = {"base": "flatten", "top": "no-level"}  # prefab_contract terraform modes; C15b reads them
RAMP = {"grade": 1.0 / 8.0,            # the decks the builders lay (1:8; the orchestrator's "real 1:8")
        "max_grade": 1.0 / 8.0,        # C15: the steepest deck a ramp may have (the builders' grade may not exceed it)
        "clear": {"base": 2.40, "top": 3.40},   # clear width between the rails (the rulings)
        "landing": 1.50,               # a walking area at least this deep beyond each ramp end (grade excepted)
        "crossing": 0.70,              # every ramp-end crossing and every join of a ramp landing at least this wide
        "cross_slope": 0.005,          # max cross fall of a deck
        "station": 1.0,                # clear-width / headroom sample pitch along the run
        "band": (0.15, 1.20),          # the clear-width band above the deck (kick plates under it, the top rail in it)
        "rail_kinds": ("post", "rail", "kick")}   # what may bound the clear width (a handrail kit on each side)
C1_CORRIDOR_X = 19.60                  # C1's line corridor on the RAMPS base (design |x|: the annex's +X face)
C15B = {"edge": 0.45,                  # a pad's outer face within this of the lot line (= the C15 join depth)
        "ground": 0.01}                # ... standing on the grade or (revision 3, plinth mode) continuing below it
# base ramp (DESIGN frame): the plaza paving over the new cell (0.05 m concrete slabs; the clock-tower side's walkway
# x 19.62..22.60 stays clear in front of the annex's three LIFT TICKETS windows), lane A (outer) from the paving at the
# village end up toward the line, the turn landing, lane B (inner) back up to the top landing + balcony at the annex
# cap (CAP_TOP 5.20) and the side-area door in the annex's +X glazing (pane z 3.55..7.05; the door against the frame
# column at z 7.05..7.35). base_ramp_plan() derives every level and asserts the 1:8.
NB2_PAVE = {"x0": 19.62, "x1": 27.95, "z0": -11.95, "z1": 11.95, "t": 0.03}  # t > pin_ground eps 0.02
NB2_LANE = {"A": (25.30, 27.90), "B": (22.60, 25.20)}       # deck x0, x1 (2.60 m; rails 0.06 in: 2.424 m clear)
NB2_FOOT_Z = 11.90                                          # lane A's foot on the paving (village end)
NB2_TURN_Z = (-11.90, -10.30)                               # the turn landing (1.60 m deep)
NB2_TOP_Z1 = 10.70                                          # the top landing's village edge
NB2_BALC = {"x0": JH_ANNEX_X + 0.16, "z0": 4.50}            # the balcony: 0.01 off the frame column (x 19.75)
NB2_DOOR = {"z": (5.00, 6.80), "jamb_lo": 4.80, "jamb_hi": 7.05, "head": (7.55, 7.70), "leaf_x": (19.58, 19.62),
            "jamb_x": (19.50, 19.70)}
NB2_RAIL_IN = 0.06                                          # rail center line inside a deck edge
NB2_DECK_T = 0.25                                           # landing slab depth (base)
NB2_RAMP_T = 0.30                                           # ramp deck depth (grating on its edge girders, one part)
NB2_POST_EVERY = 3.0                                        # ramp railing posts (the station rails: 2.5)
NB2_COL = 0.20                                              # square steel columns
# the new cell's vanilla rows (on our paving: m_ParentMesh 0, "deck" mount at the paving top): a park lamp at the ramp
# foot, one at the walkway's line-side end, a bin at the foot (DECISIONS 183: vanilla first; no new GUID)
NB2_ROWS = (("LightpoleParkRandom01", (24.20, NB2_PAVE["t"], 11.35), "-Z", 0, "deck"),
            ("LightpoleParkRandom01", (21.10, NB2_PAVE["t"], -11.20), "+Z", 0, "deck"),
            ("TrashbinRandom01", (22.95, NB2_PAVE["t"], 11.45), "+Z", 0, "deck"))
# Nitpick B3 (ANNEX_STAIR; build/nitpick-b2-tram-ramps.md "Annex stair (B2 check F2)"): the annex's inside connection from its
# floor (CAP_TOP 5.20) to the hall's inner door sill (DECK_Y 6.30). DESIGN frame. The doorway is the hall door's opening
# (ANNEX_DOOR_Z); the stair is as wide as the doorway and runs away from the wall toward +X, so it stands 11 m from the ramp
# door's swing (z 4.8..7.05, x 18.7..19.6), 2.2 m above the ticket windows and clear of every sub-object row (the
# annex has none inside). risers/going: 7 x 0.157 m rise, 0.30 m going (2 x 0.157 + 0.30 = 0.61: the comfort rule).
# knobs the T2n negatives patch: flight False (no stair), wall_cut False (the doorway not cut), inset (treads narrower than
# the doorway), link_x0 (where the landing block begins: 0.01 m off the hall glazing's outer face 11.93 - not a vertex merge
# with its lower pane: a merge changed the LOD2 decimation and gave that pane 19 back-face first hits)
ANNEX_STAIR = True
NB3 = {"risers": 7, "going": 0.30, "wall_x": (12.40, 12.52), "link_x0": 11.94, "landing_x1": 14.02, "rail_in": 0.02,
       "inset": 0.0, "flight": True, "wall_cut": True}
ANNEX_ROUTE = {"width": 1.20,           # the route's flight: at least this wide, and this clear between its rails
               "passage": 0.15,         # the doorway passage is tested this far in from each jamb ...
               "after": 0.02}           # ... and from this far past the hall door's jambs' annex face
ANNEX_WALK = ("annexcap",)              # the annex floor: a C15 walkable surface while ANNEX_STAIR (else NOT_WALKABLE below)
# top ramps (each side x: 7.80..11.70 deck = 3.90 m, rails 0.06 in: 3.724 m clear >= 3.40): head at the deck 6.30 on
# z 0.50 (the -X stair's landing / gangway back edge, mirrored by a +X head grating + landing), 1:8 down to the pad
TOP_RAMP = {"x": (7.80, 11.70), "z0": 0.50, "pad": (10.10, 11.60), "head_grate": (7.80, 10.20, -0.70, 0.50)}
# Revision 3 (2026-10-13; Richard's twenty-seventh look T-3: "Can we have the whole FOUNDATION and SUPPORTS go LOWER? I'm
# imagining sticking it into a cliff edge ... if they place it on a flat mountain top the whole support structure just
# gets buried"; build/tram-top-rev3.md): the TOP in PLINTH MODE, the L4 pattern (recipes/l4_swanky_top.py + cs2_bldg
# check_base_mode "plinth"): no engine base (no vertex within PLINTH band of y 0 on any LOD, so the importer finds no
# base edge; the prefab carries BaseProperties {m_BaseType: null} and unclamped bounds min y -12), the structure itself
# continues below grade to min y -PLINTH depth, closed (every open edge of a below-grade part lies on another part's
# face; a footing slab under the foundation). Everything that met the grade continues down: the concrete pier (plinth)
# and the pads stand on a FOUNDATION BLOCK whose top is `seat` below grade (it also carries the ramp columns, the stair
# posts and the rear-portal caps: the pier's housing under the anchor-drum bays, out to the lot's sides) on a footing
# slab; the line-side pile caps (face portal + deck legs) sit on cross-braced steel LATTICE BENTS (photo 01) down to
# concrete pad footings at the same depth. Above y 0 every vertex is the shipped (nitpick B2) top's. False = the shipped
# top byte for byte (engine base, everything on the grade).
PLINTH_TOP = True
PLINTH = {"depth": 12.00,              # min y of the top: L4's plinth depth (bounds min y; the prefab delta)
          "band": 0.01,                # no vertex within this of y 0 anywhere, on any LOD (the engine-base band)
          # the foundation block's top (what the pier, pads, ramp columns, posts, rear caps stand on) and the line-side pile
          # caps' underside (on the lattice columns): L4's rule, a part either reaches >= 0.02 or stops <= -0.05 below grade.
          # 0.05 keeps the above-grade parts' texels out of the buried band (the first scratch export seated them at -0.30 /
          # -0.60: their buried strips, grimed by the plinth ground mode inside cs2_weather's visible band (z >= -0.5),
          # pushed the weathering budget's VL2 over at 0.55 -> 0.50 shipped; at -0.05 the sweep keeps 0.55)
          "seat": -0.05,
          "cap": -0.05,
          "slab_t": 0.60,              # the footing slab / the lattice pad footings: their thickness (top -11.40)
          "closed_tol": 0.003}         # C6p closed: an open edge below grade lies within this of another part's face
# the foundation block (concrete; prefab x +-11.70, z -2.85..11.50; y slab top .. seat) on its footing slab (x +-11.80 =
# the frozen bound, z -3.15..11.60 = the frozen back bound): the rear caps (z -2.75..-1.25) stand on it 0.10 in from its
# line face; the pads overhang it 0.10 on their outer and back faces (closed undersides)
FOUND_BLOCK = {"x": 11.70, "z0": -2.85, "z1": 11.50}
FOUND_SLAB = {"x": 11.80, "z0": -3.15, "z1": 11.60}
# the lattice bents (steel, photo 01: cross-braced legs on concrete footings): one per side in the plane x = +-LAT x,
# columns under the face-portal cap (z -9.60) and the two deck-leg caps (z -7.00 / -4.40), girts at `girt_y`, an X brace
# in each panel (two bays x two tiers), two tie girts from the last column into the block's line face
LAT = {"x": 10.10, "z": (-9.60, -7.00, -4.40), "col": 0.30, "girt": 0.20, "brace": 0.15, "pad": 0.70,
       "girt_y": (-1.00, -6.00), "into_cap": 0.03, "into": 0.20}     # a column runs 0.03 into its cap, 0.20 into its pad
# Revision 2 (see-through, design S1 / S2 / B3; contracts C18 + C18b). Every cs2_kit.sheave rim was an
# OPEN ring (its 7-point profile has no bore face): in game (back-face culling) the open bore shows what
# is behind the rim - 3,798 back-face first hits per walk-around on the 1e top, 1,958 on the 10.4 m
# bullwheel rim. close_rim bridges the two bore loops (+2 tris a segment). False = the open 1e rims
# (the C18b / C18 negative). DRIP_LIP: the drip strips' lip above the roof top (cs2_kit default 0.01
# left their dropped inner face open above the roof). The design's 0.0 (flush) still failed C18 on the FINISH
# build: the roof boxes carry the 0.02 m bevel, whose chamfer opens a wedge over the strip's dropped inner face
# (hut 6-7, annex 18-24 back hits per strip). -0.02 = the strip's top sits just under the chamfer, its open
# inner side wholly against the flat roof side (0 tris).
RIM_BORE_CLOSED = True
DRIP_LIP = -0.02
# Revision 2 (ruling Q2, SB-11 / 13 / 36): the top's berth gates at door height (2.2 m perforated leaves,
# posts 2.30); the base keeps its 1.1 m gates (JH-04 shows a platform guard rail). (leaf top, post top)
# above the deck; C17's z layout is unchanged.
GATE_H = {"base": (1.20, 1.25), "top": (2.20, 2.30)}
# Revision 2 (JH-04 / JH-11 / JH-02): a medallion ring in every dock-rail panel, a 6-gon r 0.13 x 0.012
# standing VERTEX-DOWN on the knee rail (skeptic S7) in the rail plane; the 1e discs floated.
MEDAL_R, MEDAL_T = 0.13, 0.012
RAIL_KNEE_TOP = 0.55 + 0.028        # cs2_kit.handrail knee rail center + tube radius, above the deck

# Expected bounds tops (C8)
HEIGHT = {("base", True): CT_H, ("base", False): ROOF_HIGH,
          ("top", True): MAST_TOP, ("top", False): MAST_TOP}
HEIGHT_TOL = {("base", True): 0.05, ("base", False): 0.05, ("top", True): 0.05,
              ("top", False): 0.05}

# Budgets (ROUND-SPEC section 4 / task). LOD2 is built <= 500: tools/assets-check.ps1 runs
# cs2_validate with the default lod2_cap 500 (the lod2_cap override of RS section 6 is not in
# the CLI), so a 600-700 LOD2 would fail the gate; <= 500 satisfies the 700 / 600 caps too.
LOD0_TARGET = {"base": 14000, "top": 12000}
LOD0_CAP = {"base": 16000, "top": 14000}
LOD1_TARGET = {"base": 6000, "top": 5000}
LOD2_CAP = {"base": 700, "top": 600}
LOD2_TARGET = {"base": 480, "top": 460}
LOD2_CLI_CAP = 500
BATCH_CAP = 16

ENGINE_BASE_PARTS = {"base": ("stonebase", "annexbase", "ctbase", "stairfoot_"),
                     "top": ("plinth", "footing_", "stairfoot_")}
ENGINE_BASE_PARTS_LOD = {"base": ("stonebase", "annexbase"), "top": ("plinth",)}
# `_Win` room cells (col, row) -> id row*5+col (contract 8.3)
WIN_CELLS = {"base": {"opcabwin": (1, 1), "annexwinlo": (2, 3), "annexwinhi": (3, 3),
                      "tickwin": (3, 1)},
             "top": {"hutwin": (2, 1)}}

SHADE_ANGLE = 66.0
PACK_MARGIN = {2048: 6, 4096: 12}
TINT = {  # VariationSet colors (contract 12.3), slot -> rgb (picker space)
    "base": {"R": (0.200, 0.325, 0.250), "G": (0.250, 0.263, 0.275), "B": (0.775, 0.125, 0.100)},
    # top slot 2 = the bullwheels (Jackson Hole's are yellow, JH-08 / JH-12); revision 1
    "top": {"R": (0.775, 0.800, 0.825), "G": (0.900, 0.700, 0.080), "B": (0.775, 0.125, 0.100)},
}
# Stamped labels (painted in post_compose): their UVs are re-projected upright after the smart
# project (orient_label_uvs) and asserted upright on UVMap and UVUnique (contract C13). Detail pass 1:
# every dressing label face (`label_<n>`, design L4) joins them.
LABEL_PREFIXES = ("pictodisc", "pictoplinth", "ctface_", "label_")


def check_constants(role=None):
    """The R27 identities every other number hangs off (asserted at build start). Revision 2 (design T4):
    the lane wheels take the rope on TOP (DW_Y = HAUL_Y - DW_RR) and send it down in the haul plane
    (DW_Z = BULL_Z - DW_RR); the foot sheaves' outer tangent is the haul lane (FS_X + FS_RR = HAUL_X). The
    bullwheel-to-scale rules are contract C20 (check_wheels, a ContractError, run right after this)."""
    assert abs(TRACK_Y - (PIVOT_Y + ROPE_ABOVE_PIVOT_M)) < 1e-9
    assert abs(CARRIAGE_TOP_Y - (TRACK_Y + ROPE_BELOW_TOP_M)) < 1e-9
    assert abs(DECK_Y - (PIVOT_Y + INTERIOR_FLOOR_M)) < 1e-9
    assert abs(HAUL_Y - (TRACK_Y - 0.40)) < 1e-9
    assert CARRIAGE_HALF_W <= 1.10 + 1e-9                       # R30
    assert abs(FACE_Z - (STUB_Z + 1.60)) < 1e-9
    # rope supports start behind the docked carriage (C4 carriage zone ends DOCK_Z + 2.80 + 0.20)
    assert SADDLE_Z0 > DOCK_Z + CARRIAGE_HALF_L + 0.20 + 0.60
    assert DW_Z - DW_RR - 0.15 > DOCK_Z + CARRIAGE_HALF_L + 0.20     # lane-wheel rims clear too
    assert BASE_DS_Z - BASE_DS_RIM >= SADDLE_Z0 - 1e-9              # base deflection sheaves too
    if RAMPS:                                                   # nitpick B2: the 1:8 identities of the role built
        if role in (None, "base"):                                  # (None: both)
            base_ramp_plan()
        if role in (None, "top"):
            top_ramp_plan()
    if ANNEX_STAIR and role in (None, "base"):                  # nitpick B3: the annex stair's levels fit the annex
        base_annex_plan()
    if PLINTH_TOP and role in (None, "top"):
        top_plinth_plan()                                       # revision 3: the below-grade layout fits


def top_plinth_plan():
    """Revision 3 (PLINTH_TOP): the below-grade levels and the layout identities, asserted: the plinth rule needs the
    shipped ramps (the end stairs' foot pads stood on the grade) and every grade-standing part's new seat below the band;
    the foundation block carries the rear caps, the pads, the ramp columns and the stair posts and stays inside the
    frozen bound; the slab and the lattice footings reach exactly -depth; the face footing stays behind FACE_Z (C1)."""
    D, band, seat, cap = PLINTH["depth"], PLINTH["band"], PLINTH["seat"], PLINTH["cap"]
    ys = round(-D + PLINTH["slab_t"], 6)
    bx, bz0, bz1 = FOUND_BLOCK["x"], FOUND_BLOCK["z0"], FOUND_BLOCK["z1"]
    fx, fz0, fz1 = FOUND_SLAB["x"], FOUND_SLAB["z0"], FOUND_SLAB["z1"]
    p = {"depth": D, "seat": seat, "cap": cap, "slab_top": ys, "block": {"x": [-bx, bx], "z": [bz0, bz1], "y": [ys, seat]},
         "slab": {"x": [-fx, fx], "z": [fz0, fz1], "y": [-D, ys]},
         "lattice": {"x": LAT["x"], "z": list(LAT["z"]), "girt_y": list(LAT["girt_y"]),
                     "col_y": [round(cap + LAT["into_cap"], 6), round(ys - LAT["into"], 6)]}}
    assert RAMPS, "PLINTH_TOP needs the ramps (the end stairs' foot pads stand on the grade)"
    assert seat <= -0.05 and cap <= -0.05 and cap + LAT["into_cap"] < -band and -D < ys < cap, p
    rear = [(xc, zc, h) for nm, xc, zc, h in top_footings() if nm.startswith("footing_r")]
    assert all(abs(xc) + h <= bx and bz0 + 0.05 <= zc - h and zc + h <= bz1 for xc, zc, h in rear), (rear, p)
    x0, x1 = TOP_RAMP["x"]
    e = 1e-9
    assert x0 >= TOP_PLX and TOP_PAD_X1 - 0.10 - e <= bx <= TOP_PAD_X1 and TOP_RAMP["pad"][1] - 0.10 - e <= bz1, p   # pads on it
    assert bz0 <= -1.60 and 11.68 <= bx and bx < fx <= 11.80 and fz1 <= 11.60 and fz0 < bz0 and bz1 < fz1, p
    assert min(LAT["z"]) - LAT["pad"] >= FACE_Z + 0.05, p      # C1: the face footing stays behind the free-air face
    assert LAT["z"][-1] + LAT["pad"] + 0.30 <= bz0 and abs(LAT["x"]) + LAT["pad"] <= fx, p
    return p


# --------------------------------------------------------------------------------------
# Nitpick B2 frames (RAMPS): the lot, the design -> prefab shift, the ramp levels
# --------------------------------------------------------------------------------------

def lot_cells(role):
    """(W, D) lot cells of this build: 6 x 3 on the base with RAMPS (the new cell on the clock-tower side)."""
    return LOT_RAMPS[role] if RAMPS else LOT[role]


def frame_dx(role):
    """Prefab x of the DESIGN frame's origin (the lift axis): BASE_DX on the RAMPS base, else 0. The joined mesh is
    translated by it in assemble(); every design-frame table number meets the mesh / the manifest through pf()."""
    return BASE_DX if (RAMPS and role == "base") else 0.0


def pf(role, p):
    """A DESIGN-frame point (X, Y, Z) -> the prefab frame (X + frame_dx, Y, Z), as a tuple of floats; a shifted x is
    rounded to 1 um (9.80 - 4.00 = 5.8, not 5.800000000000001: the manifest numbers stay the design's decimals)."""
    dx = frame_dx(role)
    return (round(float(p[0]) + dx, 6) if dx else float(p[0]), float(p[1]), float(p[2]))


def base_ramp_plan():
    """The base ramp's levels (DESIGN frame) from NB2_*: lane A rises 1:8 from the paving top at NB2_FOOT_Z toward -Z
    to the turn landing, lane B 1:8 back toward +Z to the annex cap level CAP_TOP (the side-area floor); the top
    landing runs from lane B's head to NB2_TOP_Z1, the balcony along the annex face from NB2_BALC z0. Asserted: both
    lanes at exactly RAMP grade, the landings >= RAMP landing deep, everything inside the new lot (x <= 28, |z| <= 12)."""
    g = RAMP["grade"]
    y0 = NB2_PAVE["t"]
    run_a = NB2_FOOT_Z - NB2_TURN_Z[1]
    y_turn = y0 + run_a * g
    run_b = (CAP_TOP - y_turn) / g
    z_top0 = NB2_TURN_Z[1] + run_b
    p = {"y_foot": y0, "y_turn": round(y_turn, 6), "y_top": CAP_TOP, "run_a": round(run_a, 6),
         "run_b": round(run_b, 6), "z_top0": round(z_top0, 6), "rise_a": round(run_a * g, 6),
         "rise_b": round(CAP_TOP - y_turn, 6), "turn_depth": round(NB2_TURN_Z[1] - NB2_TURN_Z[0], 6),
         "top_depth": round(NB2_TOP_Z1 - z_top0, 6), "lane_w": {k_: round(b - a, 6) for k_, (a, b) in NB2_LANE.items()},
         "grade": g}
    W, D = LOT_RAMPS["base"]
    hx = 4.0 * W - BASE_DX                  # the lot's +X line in the design frame (28.0)
    assert p["turn_depth"] >= RAMP["landing"] - 1e-9 and p["top_depth"] >= RAMP["landing"] - 1e-9, p
    assert NB2_PAVE["x1"] <= hx and max(NB2_LANE["A"][1], NB2_LANE["B"][1]) <= hx and -4.0 * D <= NB2_TURN_Z[0]
    assert NB2_FOOT_Z <= 4.0 * D and NB2_TOP_Z1 <= 4.0 * D and z_top0 > NB2_DOOR["jamb_hi"]
    # (the clear width between the rails is C15's to assert: build_checked raises ContractError on a narrow lane)
    return p


def base_annex_plan():
    """The annex stair's levels (DESIGN frame) from NB3 / ANNEX_DOOR_Z: the landing block at DECK_Y from the hall glazing's
    outer face (link_x0) through the annex wall to landing_x1, then `risers` equal risers of (DECK_Y - CAP_TOP) / risers down
    onto the annex floor over `risers - 1` treads of `going` (the top riser is at landing_x1, the bottom one at the last
    tread's end). Asserted: the stair stands inside the annex between its walls and well short of the ramp door's swing (the
    +X glazing, x 19.60, door z 4.80..7.05), the landing is >= 1.20 m deep inside the annex, the rails fit on the treads.
    (The riser range, the width and the headroom are C15's: a wrong stair is a ContractError, not an assert.)"""
    n, g = int(NB3["risers"]), NB3["going"]
    d0, d1 = ANNEX_DOOR_Z
    wx0, wx1 = NB3["wall_x"]
    x_top = NB3["landing_x1"]
    rise = (DECK_Y - CAP_TOP) / n
    run = g * (n - 1)
    x_foot = x_top + run
    ins = NB3["inset"]
    zl, zr = d0 + ins + NB3["rail_in"], d1 - ins - NB3["rail_in"]
    p = {"rise_total": round(DECK_Y - CAP_TOP, 6), "risers": n, "riser": round(rise, 6), "going": g,
         "treads": n - 1, "run": round(run, 6), "x_top": x_top, "x_foot": round(x_foot, 6),
         "x_link": NB3["link_x0"], "z": [d0, d1], "width": round(d1 - d0 - 2.0 * ins, 6),
         "landing_inside": round(x_top - wx1, 6), "rail_z": [round(zl, 6), round(zr, 6)],
         "tread_tops": [round(DECK_Y - rise * (i + 1), 6) for i in range(n - 1)],
         "rails_clear": round(zr - zl - 2.0 * 0.028, 6)}
    ax0, ax1, az0, az1 = 12.40, 19.60, -8.00, 7.20
    assert n >= 2 and g > 0.0 and run > 0.0, p
    assert wx1 + 1.20 - 1e-9 <= x_top and x_foot + 0.50 <= JH_ANNEX_X - 0.15 - 2.0, p      # inside; > 2 m short of the door
    assert az0 + 0.10 <= d0 and d1 <= az1 - 0.10 and NB3["link_x0"] < x_top - 1.0, p
    return p


def top_ramp_plan():
    """The top's two ramps (mirror images about x 0): from the head landings at DECK_Y on z0 down 1:8 to the pads'
    front edge; the pad top = the ramp foot. Asserted: the pad at the frozen back bound (z 11.60) inside the lot, the
    pad >= RAMP landing deep, the clear width >= RAMP clear top."""
    g = RAMP["grade"]
    z0, (p0, p1) = TOP_RAMP["z0"], TOP_RAMP["pad"]
    run = p0 - z0
    y_pad = DECK_Y - run * g
    x0, x1 = TOP_RAMP["x"]
    p = {"z0": z0, "z1": p0, "run": round(run, 6), "y_head": DECK_Y, "y_pad": round(y_pad, 6),
         "rise": round(run * g, 6), "pad_z": [p0, p1], "pad_depth": round(p1 - p0, 6), "x": [x0, x1],
         "width": round(x1 - x0, 6), "clear": round(x1 - x0 - 2.0 * NB2_RAIL_IN - 2.0 * 0.028, 6), "grade": g}
    assert p["pad_depth"] >= RAMP["landing"] - 1e-9, p              # (the clear width: C15)
    assert p1 <= 11.60 + 1e-9 and x1 <= 11.80 + 1e-9
    return p


# --------------------------------------------------------------------------------------
# Materials (contract 7.1; rough = 1 - smooth, skeptic MC5)
# --------------------------------------------------------------------------------------

def material_table(role, finish=True):
    """{key: MaterialSpec fields}. Blockout (finish=False) swaps every texture set for a
    flat linear color of the same read (skeptic F4: the blockout needs its own route)."""
    steel_top = role == "top"
    t = {
        "concrete": dict(texture_set="Concrete034", metres_per_tile=3.0, tint=0.58),
        "galv": dict(texture_set="Metal032", metres_per_tile=2.0),
        "steel": dict(albedo=(0.8, 0.8, 0.8), metal=0.50 if steel_top else 0.10,
                      rough=0.60 if steel_top else 0.55, slot=1),
        "roof": dict(albedo=(0.8, 0.8, 0.8), metal=0.10, rough=0.60, slot=2),
        "trim": dict(albedo=(0.8, 0.8, 0.8), metal=0.10, rough=0.50, slot=3),
        "stone": dict(texture_set="Bricks096", metres_per_tile=2.0),
        "rock": dict(texture_set="Rock030", metres_per_tile=4.0),
        "timber": dict(texture_set="WoodSiding013", metres_per_tile=2.0),
        "glass": dict(albedo=(0.07, 0.09, 0.11), metal=0.0, rough=0.08),
        # T27: the vanilla line cable (cs2_asset.ROPE_MATERIAL; was albedo (0.30, 0.30, 0.32) metal 0.9 rough 0.65)
        "rope": dict(cs2_asset.ROPE_MATERIAL),
        "safety": dict(albedo=(0.85, 0.70, 0.10), metal=0.0, rough=0.70),
        "lamp": dict(albedo=(0.85, 0.82, 0.75), metal=0.0, rough=0.40,
                     emissive=(0.95, 0.85, 0.65)),
        "clockface": dict(albedo=(0.92, 0.92, 0.92), metal=0.0, rough=0.50,
                          emissive=(0.90, 0.88, 0.80)),
        "dplate": dict(texture_set="Metal032", metres_per_tile=1.0),
        # revision 1: the JH plinth face is a tan render (JH-07); the bullwheels are slot 2
        "render": dict(texture_set="Plaster002", metres_per_tile=3.0, tint=(0.80, 0.62, 0.45)),
        "wheel": dict(albedo=(0.8, 0.8, 0.8), metal=0.10, rough=0.45, slot=2),
    }
    if steel_top:
        # slot 2 is the bullwheels' yellow on the top: the hut roof is a fixed dark sheet
        t["roof"] = dict(albedo=(0.045, 0.045, 0.050), metal=0.30, rough=0.60)
    if not finish:
        flat = {"concrete": (0.42, 0.42, 0.40), "galv": (0.55, 0.56, 0.58),
                "stone": (0.45, 0.36, 0.27), "rock": (0.33, 0.31, 0.29),
                "timber": (0.38, 0.24, 0.14), "dplate": (0.55, 0.56, 0.58),
                "render": (0.50, 0.38, 0.27)}
        for k, rgb in flat.items():
            metal = 1.0 if k in ("galv", "dplate") else 0.0
            t[k] = dict(albedo=rgb, metal=metal, rough=0.6)
    return t


# --------------------------------------------------------------------------------------
# Frame + part builder
# --------------------------------------------------------------------------------------

def P(X, Y, Z):
    """Prefab-local point -> Blender authoring point (orient_for_net applied later)."""
    return Vector((Z, X, Y))


_DROP = {"+X": "+Y", "-X": "-Y", "+Y": "+Z", "-Y": "-Z", "+Z": "+X", "-Z": "-X"}
_AXIS = {"X": "Y", "Y": "Z", "Z": "X", "-X": "-Y", "-Y": "-Z", "-Z": "-X"}
_FACING = {"+X": "+Y", "-X": "-Y", "+Z": "+X", "-Z": "-X"}


def _orient_convex(bm):
    """Outward normals for a convex (possibly open) shell: each face points away from the
    vertex centroid (recalc_face_normals guesses on open bands like the battered plinth)."""
    bm.normal_update()
    cen = sum((v.co for v in bm.verts), Vector()) / max(len(bm.verts), 1)
    flip = [f for f in bm.faces if f.normal.dot(f.calc_center_median() - cen) < 0]
    if flip:
        bmesh.ops.reverse_faces(bm, faces=flip)
    bm.normal_update()


def close_rim(obj):
    """Revision 2 (design S1): bridge the two open bore loops of a cs2_kit.sheave rim (the 7-point profile ends
    at r_hub on both faces: an open ring, i.e. a hole in game once back faces are culled). In place, in the
    rim's local frame; normals recomputed outward, elements re-sorted (cs2_asset.canonical_order_bmesh: the
    spin's order is not deterministic, review F1), UVs box-projected. cs2_kit.sheave itself is not changed
    (towers and the other stations use it). Raises when the rim is not exactly two open loops."""
    me = obj.data
    bm = bmesh.new()
    bm.from_mesh(me)
    # the rim was spun about its LOCAL z: the two open loops are the profile's end points at r_hub on the two
    # side planes z = -+w/2. Built quad by quad in angle order (bmesh.ops.bridge_loops picked its start edge
    # differently from run to run: T1 caught the hash drift), each turned to face the axis (the bore surface)
    bv = {v for e in bm.edges if e.is_boundary for v in e.verts}
    if not bv:
        bm.free()
        return 0
    zs = sorted({round(v.co.z, 5) for v in bv})
    rings = [sorted((v for v in bv if round(v.co.z, 5) == z), key=lambda v: math.atan2(v.co.y, v.co.x)) for z in zs]
    if len(rings) != 2 or len(rings[0]) != len(rings[1]) or len(rings[0]) < 3:
        bm.free()
        raise RuntimeError(f"close_rim {obj.name}: open loops {[len(r) for r in rings]} at z {zs} (want 2 equal rings)")
    n = len(rings[0])
    # pair each ring-0 vertex with the ring-1 vertex at its angle (never sort the two rings independently: a
    # vertex at -+pi could land at opposite ends of the two sorts)
    rings[1] = [min(rings[1], key=lambda w, v=v: (w.co.x - v.co.x) ** 2 + (w.co.y - v.co.y) ** 2) for v in rings[0]]
    for i in range(n):
        j = (i + 1) % n
        f = bm.faces.new((rings[0][i], rings[0][j], rings[1][j], rings[1][i]))
        f.normal_update()
        c = f.calc_center_median()
        if f.normal.dot(Vector((c.x, c.y, 0.0))) > 0.0:
            f.normal_flip()
    made = n
    cs2_asset.canonical_order_bmesh(bm)
    if bm.loops.layers.uv.get("UVMap") is None:
        bm.loops.layers.uv.new("UVMap")
    cs2_kit._box_project_uv(bm)
    bm.to_mesh(me)
    bm.free()
    me.update()
    return made


def _mk(name, bm):
    """bmesh (authoring coordinates, object at the origin) -> linked object with UVMap."""
    if bm.loops.layers.uv.get("UVMap") is None:
        bm.loops.layers.uv.new("UVMap")
    cs2_kit._box_project_uv(bm)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.update()
    obj = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(obj)
    obj["relief"] = "geo"
    return obj


class Builder:
    """Collects [(obj, material key)] in a fixed order; every helper takes PREFAB coords."""

    def __init__(self, role):
        self.role = role
        self.parts = []
        self._names = set()

    def add(self, obj, mat):
        if obj.name in self._names or "." in obj.name:
            raise RuntimeError(f"part name clash / rename: {obj.name!r}")
        self._names.add(obj.name)
        self.parts.append((obj, mat))
        return obj

    def kit(self, kit_parts, mat, rename=None):
        out = []
        for obj, _q in kit_parts:
            if rename:
                new = rename(obj.name)
                obj.name = new
                obj.data.name = new
            out.append(self.add(obj, mat(obj.name) if callable(mat) else mat))
        return out

    # -- primitives -------------------------------------------------------------------
    def box(self, name, x0, x1, y0, y1, z0, z1, mat, open_bottom=False, drop=()):
        if x1 <= x0 or y1 <= y0 or z1 <= z0:
            raise ValueError(f"{name}: empty box")
        size = (z1 - z0, x1 - x0, y1 - y0)
        loc = P((x0 + x1) / 2.0, (y0 + y1) / 2.0, (z0 + z1) / 2.0)
        if open_bottom and abs(y0) > 1e-9:
            raise ValueError(f"{name}: open_bottom parts start at y 0")
        o = cs2_kit.add_box(name, size, loc, open_bottom=open_bottom,
                            drop=tuple(_DROP[d] for d in drop))
        return self.add(o, mat)

    def prism(self, name, bot, top, mat, open_bottom=False, open_top=False):
        """Hexahedron from 4 bottom + 4 top prefab points (same winding)."""
        bm = bmesh.new()
        vb = [bm.verts.new(P(*p)) for p in bot]
        vt = [bm.verts.new(P(*p)) for p in top]
        for i in range(4):
            j = (i + 1) % 4
            bm.faces.new((vb[i], vb[j], vt[j], vt[i]))
        if not open_bottom:
            bm.faces.new(tuple(reversed(vb)))
        if not open_top:
            bm.faces.new(tuple(vt))
        _orient_convex(bm)
        return self.add(_mk(name, bm), mat)

    def member(self, name, a, b, w, d, mat, up=(0.0, 1.0, 0.0)):
        """Oriented box between prefab points a, b: w across (side), d along the up-ish axis."""
        A, B = P(*a), P(*b)
        t = (B - A)
        if t.length < 1e-6:
            raise ValueError(f"{name}: zero-length member")
        t.normalize()
        upv = P(*up)
        u = upv - t * upv.dot(t)
        if u.length < 1e-6:
            alt = Vector((1.0, 0.0, 0.0)) if abs(t.x) < 0.9 else Vector((0.0, 1.0, 0.0))
            u = alt - t * alt.dot(t)
        u.normalize()
        s = t.cross(u).normalized()
        bm = bmesh.new()
        rings = []
        for end in (A, B):
            rings.append([bm.verts.new(end + s * (i * w / 2.0) + u * (j * d / 2.0))
                          for i, j in ((-1, -1), (1, -1), (1, 1), (-1, 1))])
        for i in range(4):
            j = (i + 1) % 4
            bm.faces.new((rings[0][i], rings[0][j], rings[1][j], rings[1][i]))
        bm.faces.new(tuple(reversed(rings[0])))
        bm.faces.new(tuple(rings[1]))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        return self.add(_mk(name, bm), mat)

    def cyl(self, name, r, a, b, mat, segs=12, caps="both"):
        A, B = P(*a), P(*b)
        d = B - A
        rot = d.to_track_quat("Z", "Y").to_euler()
        o = cs2_kit.add_cyl(name, r, d.length, (A + B) / 2.0, rot, segs, caps)
        return self.add(o, mat)

    def quad(self, name, pts, mat, facing):
        """Single quad (panes, dials, the pictogram) whose normal points along `facing`."""
        bm = bmesh.new()
        vs = [bm.verts.new(P(*p)) for p in pts]
        f = bm.faces.new(vs)
        bm.normal_update()
        if f.normal.dot(P(*facing)) < 0:
            bmesh.ops.reverse_faces(bm, faces=[f])
        return self.add(_mk(name, bm), mat)

    def loft(self, name, sections, mat, caps=(True, True)):
        """Swept rectangle: `sections` = list of 4 prefab corners each (consistent order)."""
        bm = bmesh.new()
        rings = [[bm.verts.new(P(*p)) for p in sec] for sec in sections]
        for ra, rb in zip(rings, rings[1:]):
            for i in range(4):
                j = (i + 1) % 4
                bm.faces.new((ra[i], ra[j], rb[j], rb[i]))
        if caps[0]:
            bm.faces.new(tuple(reversed(rings[0])))
        if caps[1]:
            bm.faces.new(tuple(rings[-1]))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        return self.add(_mk(name, bm), mat)

    def pyramid(self, name, x0, x1, z0, z1, y0, apex_y, mat):
        bm = bmesh.new()
        base = [bm.verts.new(P(x, y0, z)) for x, z in ((x0, z0), (x1, z0), (x1, z1), (x0, z1))]
        ap = bm.verts.new(P((x0 + x1) / 2.0, apex_y, (z0 + z1) / 2.0))
        for i in range(4):
            bm.faces.new((base[i], base[(i + 1) % 4], ap))
        _orient_convex(bm)
        return self.add(_mk(name, bm), mat)

    def tube(self, name, pts, r, mat, segs=6, caps="none", bend_rings=3, fillet=None):
        if fillet is None and name.startswith("rope_"):
            # ropes follow finely sampled arcs: fillets that MEET (fillet=None) leave ring pairs
            # < 1 mm apart, i.e. ~1e-5 m2 sliver quads that smart_project splits into their own
            # islands - sub-texel after the unique pack (zero-area island at 512, revision 1).
            # 0.4 x the shortest span keeps a straight gap between neighboring fillets.
            spans = [(Vector(b) - Vector(a)).length for a, b in zip(pts, pts[1:])]
            fillet = 0.4 * min(spans)
        o = cs2_kit.tube_along(name, [P(*p) for p in pts], r, segs, caps, bend_rings, fillet)
        # touching fillets can leave rings ~1e-6 m apart (sliver faces of ~3e-8 m2 that the
        # unique pack collapses to zero UV area): weld them and drop the degenerates. The end
        # rings are 2 pi r / segs apart, so the stub-tip ring stays exact (C2).
        bm = bmesh.new()
        bm.from_mesh(o.data)
        bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-4)
        bmesh.ops.dissolve_degenerate(bm, dist=1e-5, edges=bm.edges[:])
        # folded quads where neighboring rings twist past each other inside a tight
        # deflection arc (~3e-8 m2): invisible on a 50-70 mm rope, fatal to the unique pack
        folded = [f for f in bm.faces if f.calc_area() < 1e-6]
        if folded:
            bmesh.ops.delete(bm, geom=folded, context="FACES")
        bm.to_mesh(o.data)
        bm.free()
        o.data.update()
        return self.add(o, mat)

    # -- kit wrappers -----------------------------------------------------------------
    def handrail(self, name, path, mat, h=1.10, post_every=2.5):
        return self.kit(cs2_kit.handrail(name, [P(*p) for p in path], h=h,
                                         post_every=post_every), mat)

    def grating(self, name, x0, x1, z0, z1, ytop, t=0.06, mat="galv"):
        return self.kit(cs2_kit.grating(name, z1 - z0, x1 - x0, t,
                                        P((x0 + x1) / 2.0, ytop - t / 2.0, (z0 + z1) / 2.0)),
                        mat)

    def sheave(self, name, r, w, c, axis, mat="galv", segs=12, hub=True, r_hub=None,
               groove=None, rename=None):
        """cs2_kit.sheave; revision 2 (S1): the rim's bore is closed (close_rim) unless RIM_BORE_CLOSED is
        False. `mat` may be a callable of the (renamed) part name (a yellow rim with a galvanised hub)."""
        parts = cs2_kit.sheave(name, r, w, P(*c), _AXIS[axis], segs=segs, hub=hub, r_hub=r_hub,
                               groove=groove)
        if RIM_BORE_CLOSED:
            for obj, _q in parts:
                if obj.name.endswith("_rim"):
                    close_rim(obj)
        return self.kit(parts, mat, rename=rename)

    def door(self, name, w, h, loc, facing, mat="steel"):
        return self.kit(cs2_kit.door(name, w=w, h=h, loc=P(*loc), facing=_FACING[facing]), mat)

    def drip(self, box_obj, mat="galv"):
        # revision 2 (S2): lip DRIP_LIP (0.0) - the strip no longer stands above the roof top, so its
        # dropped inner face lies wholly against the roof side (1e: 0.01 m open band, C18)
        return self.kit(cs2_kit.drip_edge(box_obj, lip=DRIP_LIP), mat)

    def prism6(self, name, centre, along, r, t, mat):
        """Revision 2 medallion: a hexagonal prism (r, thickness t) in the vertical plane through `centre`
        that contains the horizontal unit vector `along` (prefab x, z), standing VERTEX-DOWN (skeptic S7).
        20 tris (fan caps, no n-gon)."""
        ax, az = along
        n = (-az, 0.0, ax)                                  # plane normal (horizontal)
        bm = bmesh.new()
        rings = []
        for s in (-0.5, 0.5):
            ring = []
            for i in range(6):
                a = math.radians(270.0 + 60.0 * i)
                p = (centre[0] + r * math.cos(a) * ax + s * t * n[0], centre[1] + r * math.sin(a),
                     centre[2] + r * math.cos(a) * az + s * t * n[2])
                ring.append(bm.verts.new(P(*p)))
            rings.append(ring)
        for ring in rings:
            for j in range(1, 5):
                bm.faces.new((ring[0], ring[j], ring[j + 1]))
        for i in range(6):
            j = (i + 1) % 6
            bm.faces.new((rings[0][i], rings[0][j], rings[1][j], rings[1][i]))
        _orient_convex(bm)
        return self.add(_mk(name, bm), mat)

    def cone(self, name, apex, base_c, r, mat, segs=8):
        """Revision 2 (the roof-mast dish): a closed cone from `apex` to a `segs`-gon of radius r round base_c,
        its base capped (fan from a rim vertex: no n-gon). 2 segs - 2 tris."""
        A, B = Vector(apex), Vector(base_c)
        ax = (B - A).normalized()
        ref = Vector((0.0, 1.0, 0.0)) if abs(ax.y) < 0.9 else Vector((1.0, 0.0, 0.0))
        u = ax.cross(ref).normalized()
        v = ax.cross(u).normalized()
        bm = bmesh.new()
        tip = bm.verts.new(P(*A))
        ring = [bm.verts.new(P(*(B + (u * math.cos(2.0 * math.pi * i / segs) + v * math.sin(2.0 * math.pi * i / segs)) * r)))
                for i in range(segs)]
        for i in range(segs):
            bm.faces.new((tip, ring[i], ring[(i + 1) % segs]))
        for j in range(1, segs - 1):
            bm.faces.new((ring[0], ring[j + 1], ring[j]))
        _orient_convex(bm)
        return self.add(_mk(name, bm), mat)

    def dress(self, parts):
        """Detail pass 1: a cs2_liftdress builder's [(obj, material key)] (built in the Blender
        authoring frame through cs2_liftdress.tram_point = P) appended in order."""
        for obj, mat in parts:
            self.add(obj, mat)
        return [o for o, _m in parts]


# --------------------------------------------------------------------------------------
# Geometry helpers
# --------------------------------------------------------------------------------------

def roof_top(z):
    """Base mono-pitch roof top plane (high at the line face)."""
    return ROOF_HIGH - (z + ROOF_Z) * (ROOF_HIGH - ROOF_LOW) / (2.0 * ROOF_Z)


def roof_under(z):
    return roof_top(z) - ROOF_T


def arc_yz(cy, cz, r, a0, a1, n, x):
    """Points on a circle in the plane x = const, angle measured in (z, y): (cos -> z, sin -> y)."""
    return [(x, cy + r * math.sin(a0 + (a1 - a0) * i / n), cz + r * math.cos(a0 + (a1 - a0) * i / n))
            for i in range(n + 1)]


def arc_xz(cx, cz, r, a0, a1, n, y):
    """Points on a horizontal circle at height y, angle in (x, z): (cos -> x, sin -> z)."""
    return [(cx + r * math.cos(a0 + (a1 - a0) * i / n), y, cz + r * math.sin(a0 + (a1 - a0) * i / n))
            for i in range(n + 1)]


def _dedupe(pts):
    out = []
    for p in pts:
        if not out or (Vector(p) - Vector(out[-1])).length > 1e-6:
            out.append(tuple(p))
    return out


def tangent_wrap(S, C, R, sweep, n):
    """2D (z, y): tangent from S onto the circle (C, R) on the line-ward (smaller z) side,
    then `sweep` radians of wrap continuing in the travel sense. Returns [(z, y), ...]."""
    sz, sy = S
    cz, cy = C
    dz, dy = cz - sz, cy - sy
    d = math.hypot(dz, dy)
    if d <= R:
        raise ValueError("tangent_wrap: start inside the circle")
    base = math.atan2(dy, dz)
    alpha = math.asin(R / d)
    best = None
    for sgn in (1.0, -1.0):
        ang = base + sgn * alpha                     # tangent line direction
        L = math.sqrt(d * d - R * R)
        T = (sz + L * math.cos(ang), sy + L * math.sin(ang))
        if best is None or T[0] < best[0][0]:
            best = (T, ang)
    (tz, ty), ang = best
    u = (math.cos(ang), math.sin(ang))
    rz, ry = tz - cz, ty - cy                        # radius vector at T
    cross = rz * u[1] - ry * u[0]                    # +: counter-clockwise travel
    sense = 1.0 if cross > 0 else -1.0
    a0 = math.atan2(ry, rz)
    pts = [(tz, ty)]
    for i in range(1, n + 1):
        a = a0 + sense * sweep * i / n
        pts.append((cz + R * math.cos(a), cy + R * math.sin(a)))
    return pts


# --------------------------------------------------------------------------------------
# Revision 2: the haul loops as pure geometry (the builders, contract C20 and the seams test S14 share
# them; nothing here touches bpy)
# --------------------------------------------------------------------------------------

def int_tangent(c1, r1, c2, r2):
    """Internal tangent in a plane (2D points): the rope runs counter-clockwise UNDER circle 1, leaves on the
    tangent that crosses between the circles and wraps circle 2 clockwise OVER its top. Circle 1 is the left
    (-X) one; the +X side is the mirror. Returns (phi1, phi2, theta) in radians: the exit angle on circle 1,
    the arrival angle on circle 2 (both from +x toward +y) and the strand direction."""
    dx, dy = c2[0] - c1[0], c2[1] - c1[1]
    d = math.hypot(dx, dy)
    if d <= r1 + r2:
        raise ContractError(f"C20: the foot sheave and the bullwheel overlap (centres {d:.3f} m apart, radii "
                            f"{r1} + {r2})")
    theta = math.atan2(dy, dx) + math.asin((r1 + r2) / d)
    return theta - math.pi / 2.0, theta + math.pi / 2.0, theta


def _arc(c, r, a0, a1, n, plane, w):
    """n + 1 points on a circle: plane "xy" (z = w; angle from +x toward +y) or "xz" (y = w; angle from +x
    toward +z)."""
    out = []
    for i in range(n + 1):
        a = a0 + (a1 - a0) * i / n
        if plane == "xy":
            out.append((c[0] + r * math.cos(a), c[1] + r * math.sin(a), w))
        else:
            out.append((c[0] + r * math.cos(a), w, c[1] + r * math.sin(a)))
    return out


def _mirror_x(pts):
    return [(-x, y, z) for x, y, z in reversed(pts)]


def _path_len(pts):
    return float(sum(math.dist(a, b) for a, b in zip(pts, pts[1:])))


def haul_route_top():
    """Revision 2 (design 1.5 / T3, skeptic MC9): the top's haul loop, an "M" in the plane z = BULL_Z behind the
    lane wheels. Stub (-HAUL_X, HAUL_Y, STUB_Z) -> level run to the TOP of the -X lane wheel (z DW_Z) -> over it
    and down its back (90 deg, y-z plane) -> straight down the outer side of the -X foot sheave -> UNDER it
    (counter-clockwise, x-y plane) -> internal tangent up-inward -> OVER the bullwheel (clockwise) -> the
    mirror -> stub (+HAUL_X). Returns {"points", "segments" (the straight runs and the circles each must be
    tangent to), "arcs" (points and the circle they lie on), "wrap_deg", "strand_deg", "length_m"}."""
    hx, zb = HAUL_X, BULL_Z
    phi1, phi2, theta = int_tangent((-FS_X, FS_Y), FS_RR, (0.0, BULL_Y), BULL_RR)
    lane = [(-hx, y, z) for (_x, y, z) in arc_yz(DW_Y, DW_Z, DW_RR, math.pi / 2.0, 0.0, HAUL_SEGS["lane"], -hx)]
    foot = _arc((-FS_X, FS_Y), FS_RR, math.pi, phi1 + 2.0 * math.pi, HAUL_SEGS["foot"], "xy", zb)
    bull = _arc((0.0, BULL_Y), BULL_RR, phi2, math.pi - phi2, HAUL_SEGS["bull"], "xy", zb)
    left = [(-hx, HAUL_Y, STUB_Z)] + lane + foot
    pts = _dedupe(left + bull + _mirror_x(left))
    cl, cf, cb = (-hx, DW_Y, DW_Z), (-FS_X, FS_Y, zb), (0.0, BULL_Y, zb)
    mx = lambda c: (-c[0], c[1], c[2])                                  # noqa: E731
    segs = [("level run", (-hx, HAUL_Y, STUB_Z), lane[0], [(cl, "yz", DW_RR)]),
            ("down strand", lane[-1], foot[0], [(cl, "yz", DW_RR), (cf, "xy", FS_RR)]),
            ("rising strand", foot[-1], bull[0], [(cf, "xy", FS_RR), (cb, "xy", BULL_RR)])]
    segs += [(f"{n} (+X)", (-q[0], q[1], q[2]), (-p[0], p[1], p[2]), [(mx(c), pl, r) for c, pl, r in cs])
             for n, p, q, cs in segs]
    arcs = [("lane wheel -X", cl, "yz", DW_RR, lane), ("foot sheave -X", cf, "xy", FS_RR, foot),
            ("bullwheel", cb, "xy", BULL_RR, bull),
            ("lane wheel +X", mx(cl), "yz", DW_RR, [(-x, y, z) for x, y, z in lane]),
            ("foot sheave +X", mx(cf), "xy", FS_RR, [(-x, y, z) for x, y, z in foot])]
    return {"points": pts, "segments": segs, "arcs": arcs,
            "wrap_deg": {"lane": 90.0, "foot": round(math.degrees(phi1 + math.pi), 3),
                         "bull": round(math.degrees(2.0 * phi2 - math.pi), 3)},
            "strand_deg": round(math.degrees(theta), 3), "length_m": round(_path_len(pts), 3)}


def haul_route_base():
    """Revision 2 (ruling Q6): the base's haul loop in the horizontal haul plane y = HAUL_Y. Stub (-HAUL_X, HAUL_Y,
    STUB_Z) -> level run to the outer (-X) point of the -X deflection sheave (z BASE_DS_Z) -> round its +Z side ->
    external tangent onto the drive wheel's +Z (back) side -> round it -> the mirror -> stub (+HAUL_X). Returns
    the haul_route_top dict shape."""
    c1, c2 = (-BASE_DS_X, BASE_DS_Z), (BW_CENTRE[0], BW_CENTRE[2])
    dx, dz = c2[0] - c1[0], c2[1] - c1[1]
    R = math.hypot(dx, dz)
    s = (BASE_BW_RR - BASE_DS_RR) / R
    if not -1.0 < s < 1.0:
        raise ContractError(f"C20b: no external tangent between the deflection sheave and the drive wheel ({s:.3f})")
    alpha = math.atan2(dz, dx) + math.asin(s)
    phi = math.pi / 2.0 + alpha                     # the tangent's normal angle (x, z): exit = arrival angle
    ds = _arc(c1, BASE_DS_RR, math.pi, phi, 3, "xz", HAUL_Y)
    bw = _arc(c2, BASE_BW_RR, phi, math.pi - phi, 6, "xz", HAUL_Y)
    left = [(-HAUL_X, HAUL_Y, STUB_Z)] + ds
    pts = _dedupe(left + bw + _mirror_x(left))
    cd, cw = (c1[0], HAUL_Y, c1[1]), (c2[0], HAUL_Y, c2[1])
    mx = lambda c: (-c[0], c[1], c[2])                                  # noqa: E731
    segs = [("level run", (-HAUL_X, HAUL_Y, STUB_Z), ds[0], [(cd, "xz", BASE_DS_RR)]),
            ("tangent", ds[-1], bw[0], [(cd, "xz", BASE_DS_RR), (cw, "xz", BASE_BW_RR)])]
    segs += [(f"{n} (+X)", (-q[0], q[1], q[2]), (-p[0], p[1], p[2]), [(mx(c), pl, r) for c, pl, r in cs])
             for n, p, q, cs in segs]
    arcs = [("deflection sheave -X", cd, "xz", BASE_DS_RR, ds), ("drive wheel", cw, "xz", BASE_BW_RR, bw),
            ("deflection sheave +X", mx(cd), "xz", BASE_DS_RR, [(-x, y, z) for x, y, z in ds])]
    return {"points": pts, "segments": segs, "arcs": arcs,
            "wrap_deg": {"deflection": round(math.degrees(math.pi - phi), 3),
                         "drive": round(math.degrees(2.0 * phi - math.pi), 3)},
            "strand_deg": round(math.degrees(alpha), 3), "length_m": round(_path_len(pts), 3)}


C20 = {"dia": (3.40, 4.20), "above_deck": 4.5, "below_crest": 2.0, "hut_wheel": 0.30, "hut_foot": 0.15,
       "spokes": (4, 6), "tol": 0.001, "wrap_bull": 60.0, "wrap_foot": 90.0, "wrap_defl": 30.0}


def _line_dist(c, p, q):
    p, q, c = np.asarray(p, float), np.asarray(q, float), np.asarray(c, float)
    u = (q - p) / np.linalg.norm(q - p)
    return float(np.linalg.norm(np.cross(c - p, u)))


def check_wheels(role, wheels=True):
    """C20 (revision 2, design 5 as amended: the skeptic's S1 diameter cap 4.20). Asserted at build start, before
    any other contract. Top (the shipped wheels variant): 3.40 <= 2 BULL_RIM <= 4.20 (JH 3.4-4.2 m); DECK_Y + 4.5
    <= BULL_Y <= TRACK_Y - 2.0 (JH-14: ~5 m above the deck, ~2 m below the crest); the bullwheel rim >= 0.30 and
    every foot sheave >= 0.15 above the hut-roof top; 4-6 spokes; the lane-wheel identities DW_Y = HAUL_Y - DW_RR,
    DW_Z = BULL_Z - DW_RR and the foot sheaves' outer tangent x = HAUL_X; every haul-rope arc point on its
    wheel's rope circle and every straight run tangent to the circles it joins within 1 mm; wrap >= 60 deg on
    the bullwheel and >= 90 deg on each foot sheave. Base (C20b): 3.40 <= 2 BASE_BW_R <= 4.20, the deflection
    sheaves' outer tangent x = HAUL_X, the same tangency rule, wrap >= 60 deg on the drive wheel and >= 30 deg on
    each deflection sheave. The top --no-wheels variant (never shipped) is not checked. Returns the report;
    raises ContractError("C20 ...") / ("C20b ...")."""
    tol = C20["tol"]
    msgs = []
    if role == "top" and not wheels:
        return {"skipped": "--no-wheels: the horizontal 10.6 m return wheel variant (never shipped)"}
    if role == "top":
        code = "C20"
        d = 2.0 * BULL_RIM
        if not C20["dia"][0] - 1e-9 <= d <= C20["dia"][1] + 1e-9:
            msgs.append(f"bullwheel diameter {d:.2f} m outside {C20['dia'][0]:.2f}-{C20['dia'][1]:.2f} (JH-11/12/14)")
        if not DECK_Y + C20["above_deck"] - 1e-9 <= BULL_Y <= TRACK_Y - C20["below_crest"] + 1e-9:
            msgs.append(f"bullwheel centre y {BULL_Y:.2f} outside deck + {C20['above_deck']} .. crest - "
                        f"{C20['below_crest']} ({DECK_Y + C20['above_deck']:.2f}..{TRACK_Y - C20['below_crest']:.2f})")
        if BULL_Y - BULL_RIM < HUT_ROOF_TOP + C20["hut_wheel"] - 1e-9:
            msgs.append(f"bullwheel rim bottom {BULL_Y - BULL_RIM:.2f} < hut-roof top {HUT_ROOF_TOP:.2f} + "
                        f"{C20['hut_wheel']} (hut clearance)")
        if FS_Y - FS_RIM < HUT_ROOF_TOP + C20["hut_foot"] - 1e-9:
            msgs.append(f"foot sheave bottom {FS_Y - FS_RIM:.2f} < hut-roof top {HUT_ROOF_TOP:.2f} + "
                        f"{C20['hut_foot']} (hut clearance)")
        if not C20["spokes"][0] <= BULL_SPOKES <= C20["spokes"][1]:
            msgs.append(f"{BULL_SPOKES} bullwheel spokes (JH-12: {C20['spokes'][0]}-{C20['spokes'][1]})")
        if abs(DW_Y - (HAUL_Y - DW_RR)) > 1e-9 or abs(DW_Z - (BULL_Z - DW_RR)) > 1e-9:
            msgs.append(f"lane-wheel identities DW_Y {DW_Y} != HAUL_Y - DW_RR or DW_Z {DW_Z} != BULL_Z - DW_RR")
        if abs(FS_X + FS_RR - HAUL_X) > tol:
            msgs.append(f"foot-sheave outer tangent x {FS_X + FS_RR:.3f} != HAUL_X {HAUL_X}")
        if msgs:                                    # the route below needs sane numbers
            _fail(code, "; ".join(msgs))
        route = haul_route_top()
        w = route["wrap_deg"]
        if w["bull"] < C20["wrap_bull"]:
            msgs.append(f"bullwheel wrap {w['bull']:.1f} deg < {C20['wrap_bull']}")
        if w["foot"] < C20["wrap_foot"]:
            msgs.append(f"foot-sheave wrap {w['foot']:.1f} deg < {C20['wrap_foot']}")
    else:
        code = "C20b"
        d = 2.0 * BASE_BW_R
        if not C20["dia"][0] - 1e-9 <= d <= C20["dia"][1] + 1e-9:
            msgs.append(f"drive wheel diameter {d:.2f} m outside {C20['dia'][0]:.2f}-{C20['dia'][1]:.2f}")
        if abs(BASE_DS_X + BASE_DS_RR - HAUL_X) > tol:
            msgs.append(f"deflection-sheave outer tangent x {BASE_DS_X + BASE_DS_RR:.3f} != HAUL_X {HAUL_X}")
        if not C20["spokes"][0] <= BASE_BW_SPOKES <= C20["spokes"][1]:
            msgs.append(f"{BASE_BW_SPOKES} drive-wheel spokes")
        if msgs:
            _fail(code, "; ".join(msgs))
        route = haul_route_base()
        w = route["wrap_deg"]
        if w["drive"] < C20["wrap_bull"]:
            msgs.append(f"drive-wheel wrap {w['drive']:.1f} deg < {C20['wrap_bull']}")
        if w["deflection"] < C20["wrap_defl"]:
            msgs.append(f"deflection-sheave wrap {w['deflection']:.1f} deg < {C20['wrap_defl']}")
    worst = 0.0
    for name, c, _pl, r, pts in route["arcs"]:
        for p in pts:
            e = abs(math.dist(p, c) - r)
            worst = max(worst, e)
            if e > tol:
                msgs.append(f"{name}: rope point {tuple(round(v, 3) for v in p)} is {e * 1000:.1f} mm off its "
                            f"rope circle r {r}")
                break
    for name, p, q, circles in route["segments"]:
        for c, _pl, r in circles:
            e = abs(_line_dist(c, p, q) - r)
            worst = max(worst, e)
            if e > tol:
                msgs.append(f"{name}: not tangent to the circle at {tuple(round(v, 3) for v in c)} r {r} "
                            f"({e * 1000:.1f} mm)")
    if msgs:
        _fail(code, "; ".join(msgs))
    rep = {"code": code, "wrap_deg": w, "strand_deg": route["strand_deg"], "length_m": route["length_m"],
           "tangency_worst_mm": round(worst * 1000.0, 4), "points": len(route["points"])}
    if role == "top":
        rep.update({"bullwheel_dia_m": round(2.0 * BULL_RIM, 3), "bullwheel_centre": [0.0, BULL_Y, BULL_Z],
                    "above_deck_m": round(BULL_Y - DECK_Y, 3), "below_crest_m": round(TRACK_Y - BULL_Y, 3),
                    "rim_over_hut_m": round(BULL_Y - BULL_RIM - HUT_ROOF_TOP, 3),
                    "foot_over_hut_m": round(FS_Y - FS_RIM - HUT_ROOF_TOP, 3), "spokes": BULL_SPOKES})
    else:
        rep.update({"drive_dia_m": round(2.0 * BASE_BW_R, 3), "drive_centre": list(BW_CENTRE),
                    "deflection_dia_m": round(2.0 * BASE_DS_RIM, 3), "spokes": BASE_BW_SPOKES})
    return rep


# --------------------------------------------------------------------------------------
# Shared modules: gates, clock tower
#
# NO rope support stands in the docking path (z < DOCK_Z + 0.5). The built tram cabin
# (lift_cabin.py, build/lift_cabin.md R29/R30 rows) hangs from a mirror-symmetric fork: two
# uprights at |dy| 0.82..1.08 joined by a crossbar below the ropes (dz -4.10..-3.82) and by
# the carriage head above them, i.e. a closed ring around the track AND haul ropes. Any
# saddle or sheave support inside that path crosses the ring's sweep (the tower lens hit the
# same topology and accepted a clip, build/tram_tower.md). A station does not have to: its
# ropes run free from the stub tips to supports BEHIND the docked cabin (base: the
# counterweight saddles and the bullwheel; top: the rear saddles, bollards and the haul
# deflection sheaves). The contract's face saddles / face haul sheaves (5.5, 6.5) and the
# inboard saddle frame of the first build are therefore not built.
# --------------------------------------------------------------------------------------

# Revision 1e (bay gates vs the cabin doors; revision2-check-tram-cabin section C / lift_cabin.md R2.5).
# The cabin's revision 2 centers a pair of plug doors on each long side (leaves 0.838 m wide meeting at
# the cabin's length center = the dock, DOCK_Z), but the 1d fence grid (5 posts every 2.0 m from `z0`)
# knew nothing of them: on the base a post at z -6.20 stood inside the door span (-6.35..-4.65) and the
# nearest leaf was 0.30 m off the door center, on the top a post at -5.60 sat 0.10 m from the seam. The
# fence is now laid out FROM THE DOOR: each berth edge carries a bi-parting gate whose leaves close on
# the door seam (z = GATE_DOOR_Z) and is DRAWN OPEN - the leaf pair parked either side of a clear opening
# of 2 x GATE_OPEN_HALF = 1.90 m (>= 1.70 m, the door span) centered on the seam, so no post and no leaf
# stands in front of a door. The two runs between the opening posts and the fence ends (z0 and
# z0 + GATE_RUN, unchanged) are divided into equal bays of at most GATE_PITCH_MAX. Contract C17
# (`check_gate_door`) reads the door from lift_cabin.py (its manifest carries the dock z only) and FAILS
# on any post or leaf inside the door span, an opening under 1.70 m, an opening off the seam, or a leaf
# beside the opening shorter than half of it (the parked pair could not close on the seam).
# GATES_AT_DOOR False rebuilds the 1d grid (the C17 negative in the test).
GATES_AT_DOOR = True
GATE_EDGES = (-7.80, -3.20, 3.20, 7.80)    # the four berth edges: -X outer / inner, +X inner / outer
GATE_Z0 = {"base": -8.20, "top": -9.60}    # first fence post (base: behind the moving-platform pit)
GATE_RUN = 8.0                             # fence length along z (first post -> last post)
GATE_DOOR_Z = DOCK_Z                       # the door seam the opening is centered on (parked cabin center)
GATE_OPEN_HALF = 0.95                      # half the clear opening between the two opening posts' faces
GATE_POST_HALF = 0.04                      # post half extent along z
GATE_PITCH_MAX = 2.00                      # largest post pitch between the opening and a fence end
GATE_LEAF_INSET = 0.05                     # leaf ends this far inside the post center (0.01 clear of its face)
C17_OPEN_MIN = 1.70                        # C17: the opening is at least this wide (= the door span 2 x 0.85)
C17_CENTRE_TOL = 0.02                      # C17: opening center vs door seam
CABIN_MANIFEST = os.path.join(REPO, "assets", "import", "cabins", "A", "SkiTramCabinA",
                              "SkiTramCabinA.manifest.json")


def gate_layout(z0, z1=None):
    """One berth edge's fence along z: (posts [z centers], leaves [(z_lo, z_hi)]). The 1d grid when
    GATES_AT_DOOR is False (5 posts every 2.0 m, 4 leaves 1.90 m long); otherwise the opening posts
    stand at GATE_DOOR_Z -+ (GATE_OPEN_HALF + GATE_POST_HALF) and each run out to a fence end is
    split into the fewest equal bays of at most GATE_PITCH_MAX."""
    z1 = z0 + GATE_RUN if z1 is None else z1
    if not GATES_AT_DOOR:
        posts = [z0 + 2.0 * i for i in range(5)]
        leaves = [(z0 + 1.0 + 2.0 * i - 0.95, z0 + 1.0 + 2.0 * i + 0.95) for i in range(4)]
        return posts, leaves
    dc = GATE_DOOR_Z
    pa, pb = dc - (GATE_OPEN_HALF + GATE_POST_HALF), dc + (GATE_OPEN_HALF + GATE_POST_HALF)
    posts, leaves = [], []
    for lo, hi in ((z0, pa), (pb, z1)):
        if hi - lo < 4.0 * GATE_LEAF_INSET:
            raise RuntimeError(f"bay gates: the run {lo:.3f}..{hi:.3f} is too short for a leaf")
        n = max(1, int(math.ceil((hi - lo) / GATE_PITCH_MAX - 1e-9)))
        zs = [lo if i == 0 else hi if i == n else round(lo + (hi - lo) * i / n, 4) for i in range(n + 1)]
        posts += zs
        leaves += [(a + GATE_LEAF_INSET, b - GATE_LEAF_INSET) for a, b in zip(zs[:-1], zs[1:])]
    return posts, leaves


def bay_gates(k, edges, z0=-9.60, role="top"):
    """Posts and leaves per bay edge (contract 5.1, laid out from the cabin door: gate_layout),
    leaves slot 3. `z0` = the first post (base revision 1: -8.20, behind the moving-platform pit).
    Revision 2 (ruling Q2): heights per station from GATE_H (top 2.20 / 2.30, base 1.20 / 1.25)."""
    posts, leaves = gate_layout(z0)
    leaf_top, post_top = GATE_H[role]
    for e, x in enumerate(edges):
        sx = 1.0 if x > 0 else -1.0
        onplat = -sx if abs(x) < TRACK_X else sx       # toward the platform the edge bounds
        px0 = min(x + onplat * 0.02, x + onplat * 0.10)
        px1 = max(x + onplat * 0.02, x + onplat * 0.10)
        for i, zc in enumerate(posts):
            k.box(f"gatepost_{e}_{i}", px0, px1, DECK_Y, DECK_Y + post_top, zc - GATE_POST_HALF,
                  zc + GATE_POST_HALF, "steel", drop=("-Y",))
        lx0, lx1 = (min(x, x + onplat * 0.04), max(x, x + onplat * 0.04))
        for i, (za, zb) in enumerate(leaves):
            k.box(f"gate_{e}_{i}", lx0, lx1, DECK_Y + 0.08, DECK_Y + leaf_top, za, zb, "trim")


def rail_panels(path, post_every=2.5):
    """Revision 2 medallions: (panel start, panel end, horizontal unit direction) of every panel of a
    cs2_kit.handrail along `path` (prefab deck-level points), with the kit's post rule (each segment cut into
    ceil(L / post_every) equal spans)."""
    out = []
    for a, b in zip(path, path[1:]):
        L = math.dist(a, b)
        n = max(1, int(math.ceil(L / post_every - 1e-6)))
        d = ((b[0] - a[0]) / L, (b[2] - a[2]) / L)
        for j in range(n):
            p = [a[i] + (b[i] - a[i]) * j / n for i in range(3)]
            q = [a[i] + (b[i] - a[i]) * (j + 1) / n for i in range(3)]
            out.append((p, q, d))
    return out


MEDAL_AVOID = {"top": (((-9.95, 6.60, -10.05), (-9.05, 7.50, -9.15)),     # strut_face_L through the outerL rail
                       ((9.05, 6.60, -10.05), (9.95, 7.50, -9.15)))}       # strut_face_R through the outerR rail


def medallions(k, prefix, paths, avoid=()):
    """One hexagonal medallion per rail panel, centered in the rail plane, resting vertex-down on the knee rail
    (JH-04 / JH-11: a ring in every panel); mid-span unless that spot meets an `avoid` box (a face strut through
    the panel), then the nearest free spot along the panel. Names <prefix><k>; returns the count."""
    n = 0
    for path in paths:
        for p, q, d in rail_panels(path):
            for f in (0.5, 0.4, 0.6, 0.3, 0.7, 0.25, 0.75, 0.2, 0.8):
                c = (p[0] + (q[0] - p[0]) * f, p[1] + RAIL_KNEE_TOP + MEDAL_R, p[2] + (q[2] - p[2]) * f)
                ext = (abs(d[0]) * MEDAL_R + abs(d[1]) * MEDAL_T, MEDAL_R, abs(d[1]) * MEDAL_R + abs(d[0]) * MEDAL_T)
                if not any(all(c[i] + ext[i] > lo[i] and c[i] - ext[i] < hi[i] for i in range(3)) for lo, hi in avoid):
                    break
            else:
                raise RuntimeError(f"medallions {prefix}{n}: no free spot on the panel {p}..{q}")
            k.prism6(f"{prefix}{n}", c, d, MEDAL_R, MEDAL_T, "trim")
            n += 1
    return n


def landmark_tower(k, prefix="ct", *, h=25.0, w=4.0, base_h=7.0, stage=(19.0, 22.0),
                   cap_h=2.4, eave=0.8, face="clock", faces=("+X", "-X", "+Z", "-Z"),
                   loc=(0.0, 0.0, 0.0), mats=None, dial_r=1.1):
    """
    Station-local clock / lantern tower with the contract section 5.6 interface (cs2_bldg
    will expose its own `landmark_tower`; this one is local because cs2_bldg is being
    written). Pivot = base center at y 0; `<p>base` is an open-bottom stone box whose side
    edges sit on y 0 (engine base); bounds top = exactly h (the finial is inside h).
    Returns (parts, meta {"top", "face_parts", "ground_parts", "dial_r"}).
    """
    m = {"base": "stone", "shaft": "timber", "cap": "roof", "trim": "trim", "face": "clockface"}
    m.update(mats or {})
    X0, _y, Z0 = loc
    hw = w / 2.0
    n0 = len(k.parts)
    # revision 2 (B3, C18): the base keeps its top face - the shaft above is 0.25 m narrower each side, and
    # with the top dropped a 0.25 m slot ring opened into the box (175 back-face first hits per walk-around)
    k.box(f"{prefix}base", X0 - hw, X0 + hw, 0.0, base_h, Z0 - hw, Z0 + hw, m["base"],
          open_bottom=True)
    for i, (sx, sz) in enumerate(((-1, -1), (1, -1), (1, 1), (-1, 1))):
        cx, cz = X0 + sx * (hw - 0.25), Z0 + sz * (hw - 0.25)
        k.box(f"{prefix}basepier_{i}", cx - 0.40, cx + 0.40, 0.0, base_h + 0.40,
              cz - 0.40, cz + 0.40, m["base"], open_bottom=True)
    sw = hw - 0.25
    # revision 2 (C19, skeptic S3): the shaft stops 0.02 under the stage floor - its 12.25 m2 top lay exactly
    # in the cornice's top plane (a z-fight inside the stage); still closed, so the LOD2 shaft (the cornice
    # goes at LOD2) keeps a lid under the open-bottomed stage
    k.box(f"{prefix}shaft", X0 - sw, X0 + sw, base_h, stage[0] - 0.02, Z0 - sw, Z0 + sw, m["shaft"],
          drop=("-Y",))
    k.box(f"{prefix}cornice", X0 - hw - 0.15, X0 + hw + 0.15, stage[0] - 0.30, stage[0],
          Z0 - hw - 0.15, Z0 + hw + 0.15, m["trim"])
    k.box(f"{prefix}stage", X0 - hw, X0 + hw, stage[0], stage[1], Z0 - hw, Z0 + hw, m["shaft"],
          drop=("-Y",))
    face_parts = []
    yc = (stage[0] + stage[1]) / 2.0
    s = 1.5
    if face in ("clock", "blank"):
        for d in faces:
            nm = f"{prefix}face_{d[0].replace('+', 'p').replace('-', 'n')}{d[1].lower()}"
            off = hw + 0.02
            if d == "+X":
                pts = [(X0 + off, yc - s, Z0 - s), (X0 + off, yc - s, Z0 + s),
                       (X0 + off, yc + s, Z0 + s), (X0 + off, yc + s, Z0 - s)]
                fv = (1, 0, 0)
            elif d == "-X":
                pts = [(X0 - off, yc - s, Z0 - s), (X0 - off, yc - s, Z0 + s),
                       (X0 - off, yc + s, Z0 + s), (X0 - off, yc + s, Z0 - s)]
                fv = (-1, 0, 0)
            elif d == "+Z":
                pts = [(X0 - s, yc - s, Z0 + off), (X0 + s, yc - s, Z0 + off),
                       (X0 + s, yc + s, Z0 + off), (X0 - s, yc + s, Z0 + off)]
                fv = (0, 0, 1)
            else:
                pts = [(X0 - s, yc - s, Z0 - off), (X0 + s, yc - s, Z0 - off),
                       (X0 + s, yc + s, Z0 - off), (X0 - s, yc + s, Z0 - off)]
                fv = (0, 0, -1)
            k.quad(nm, pts, m["face"] if face == "clock" else m["trim"], fv)
            face_parts.append(nm)
    elif face == "lantern":
        for i, (sx, sz) in enumerate(((-1, -1), (1, -1), (1, 1), (-1, 1))):
            cx, cz = X0 + sx * (hw - 0.15), Z0 + sz * (hw - 0.15)
            k.box(f"{prefix}lanternpost_{i}", cx - 0.15, cx + 0.15, stage[0], stage[1],
                  cz - 0.15, cz + 0.15, m["shaft"])
        k.box(f"lamp_{prefix}lantern", X0 - 0.6, X0 + 0.6, stage[0] + 0.4, stage[1] - 0.6,
              Z0 - 0.6, Z0 + 0.6, "lamp")
    else:
        raise ValueError(f"landmark_tower face {face!r}")
    e = hw + eave
    y_cap0 = stage[1]
    k.box(f"{prefix}cap", X0 - e, X0 + e, y_cap0, y_cap0 + 0.30, Z0 - e, Z0 + e, m["cap"])
    apex = h - 0.60
    k.pyramid(f"{prefix}capspire", X0 - e, X0 + e, Z0 - e, Z0 + e, y_cap0 + 0.30, apex, m["cap"])
    k.cyl(f"{prefix}finial", 0.07, (X0, apex - 0.10, Z0), (X0, h, Z0), m["trim"], segs=6,
          caps="top")
    meta = {"top": h, "face_parts": face_parts, "ground_parts": [f"{prefix}base"],
            "dial_r": dial_r, "plate_m": 2 * s, "stage": list(stage), "cap_h": cap_h}
    return k.parts[n0:], meta


# --------------------------------------------------------------------------------------
# BASE station
# --------------------------------------------------------------------------------------

def build_base(k, opts):
    # -- one-story plinth: stone ground story, cap, piers (open-bottom ground parts, F7) -----
    # revision 1: the moving-platform pit runs across the open (line) face; its floor is
    # TRENCH_Y and the plinth face below it is a tan render panel with the pictogram (JH-07)
    k.box("stonebase", -HALL_X, HALL_X, 0.0, STONE_TOP, TRENCH_Z1, HALL_Z, "stone",
          open_bottom=True, drop=("+Y",))
    k.box("stonecap", -HALL_X - 0.10, HALL_X + 0.10, STONE_TOP, CAP_TOP, TRENCH_Z1 - 0.10,
          HALL_Z + 0.10, "concrete", drop=("-Y",))
    k.box("stonebasefront", -HALL_X, HALL_X, 0.0, TRENCH_Y - 0.20, TRENCH_Z0, TRENCH_Z1, "stone",
          open_bottom=True, drop=("+Y", "+Z"))
    k.box("trenchcap", -HALL_X - 0.10, HALL_X + 0.10, TRENCH_Y - 0.20, TRENCH_Y, TRENCH_Z0 - 0.25,
          TRENCH_Z1, "concrete", drop=("-Y",))
    # revision 2 (C19, B4 + skeptic MC4): the trench walls, and the stone piers below, stop CAP_DROP under the
    # caps' top (5.20) - their tops lay in the stonecap / annexcap plane where they poke past the caps
    for sx, tag in ((-1, "L"), (1, "R")):
        x0, x1 = sorted((sx * (HALL_X - 0.40), sx * HALL_X))
        k.box(f"trenchwall_{tag}", x0, x1, TRENCH_Y, CAP_TOP - CAP_DROP, TRENCH_Z0, TRENCH_Z1, "stone")
    # revision 2 (B3, C18): the render panel keeps its +Z face (it overhangs stonebasefront by 0.10 m at
    # each end, and the dropped face left those ends open)
    k.box("plinthface", -HALL_X - 0.10, HALL_X + 0.10, 0.0, TRENCH_Y - 0.20, TRENCH_Z0 - 0.20,
          TRENCH_Z0, "render", open_bottom=True)
    zp = TRENCH_Z0 - 0.205
    k.quad("pictoplinth", [(-1.50, 0.25, zp), (1.50, 0.25, zp), (1.50, 3.25, zp),
                           (-1.50, 3.25, zp)], "render", (0, 0, -1))
    i = 0
    for sx in (-1, 1):
        for zc in (-5.0, 0.0, 5.0):
            x0, x1 = (HALL_X, HALL_X + 0.40) if sx > 0 else (-HALL_X - 0.40, -HALL_X)
            k.box(f"stonepier_{i}", x0, x1, 0.0, CAP_TOP - CAP_DROP, zc - 0.60, zc + 0.60, "stone",
                  open_bottom=True, drop=("-X" if sx > 0 else "+X",))
            i += 1
    for xc in (-11.40, -6.00, 2.40, 11.40):
        k.box(f"stonepier_{i}", xc - 0.60, xc + 0.60, 0.0, CAP_TOP - CAP_DROP, HALL_Z, HALL_Z + 0.40,
              "stone", open_bottom=True, drop=("-Z",))
        i += 1

    # -- steel hall -----------------------------------------------------------------------
    cols = [(sx * HALL_X, z) for sx in (-1, 1) for z in (-10.0, -5.0, 0.0, 5.0, 10.0)]
    cols += [(x, HALL_Z) for x in (-6.0, 0.0, 6.0)]
    for n, (x, z) in enumerate(cols):
        k.box(f"col_{n}", x - 0.25, x + 0.25, CAP_TOP, roof_under(z) + 0.05, z - 0.25, z + 0.25,
              "steel", drop=("-Y",))
    # galvanised face portal over the open end (JH-07): two columns from the pit floor and the
    # face girder (its underside = the contract face opening 18.60); no gable band, so the
    # structure of the deep roof reads above it
    for sx, tag in ((-1, "L"), (1, "R")):
        x0, x1 = sorted((sx * (PORTAL_X - 0.30), sx * (PORTAL_X + 0.30)))
        k.box(f"col_portal{tag}", x0, x1, TRENCH_Y, 18.60, TRENCH_Z0, TRENCH_Z0 + 0.60, "galv")
        k.member(f"portalknee_{tag}", (sx * PORTAL_X, 16.40, -9.70),
                 (sx * (PORTAL_X - 2.20), 18.60, -9.70), 0.30, 0.30, "galv", up=(0, 0, 1))
    k.box("girderface", -HALL_X, HALL_X, 18.60, 19.80, FACE_Z, -9.60, "galv")
    for n, z in enumerate((-5.0, 0.0, 5.0)):
        k.box(f"girderroof_{n}", -HALL_X, HALL_X, roof_under(z) - 0.80, roof_under(z) + 0.02,
              z - 0.20, z + 0.20, "steel")
    # deep-eave roof (JH-07: roof ~1.5 x the hall width), outriggers, purlins (T71: the corner fins were removed)
    rx = EAVE_X
    bot = [(-rx, roof_under(-ROOF_Z), FACE_Z), (rx, roof_under(-ROOF_Z), FACE_Z),
           (rx, roof_under(ROOF_Z1), ROOF_Z1), (-rx, roof_under(ROOF_Z1), ROOF_Z1)]
    top = [(x, y + ROOF_T, z) for x, y, z in bot]
    k.prism("roof", bot, top, "roof")
    for sx, tag in ((-1, "L"), (1, "R")):
        xo = sx * (rx + 0.03)
        xi = sx * rx
        x0, x1 = min(xo, xi), max(xo, xi)
        k.prism(f"fascia_{tag}",
                [(x0, roof_under(-ROOF_Z) - 0.10, FACE_Z), (x1, roof_under(-ROOF_Z) - 0.10, FACE_Z),
                 (x1, roof_under(ROOF_Z1) - 0.10, ROOF_Z1), (x0, roof_under(ROOF_Z1) - 0.10, ROOF_Z1)],
                [(x0, roof_top(-ROOF_Z) + 0.03, FACE_Z), (x1, roof_top(-ROOF_Z) + 0.03, FACE_Z),
                 (x1, roof_top(ROOF_Z1) + 0.03, ROOF_Z1), (x0, roof_top(ROOF_Z1) + 0.03, ROOF_Z1)],
                "roof")
    k.box("fascia_front", -rx - 0.03, rx + 0.03, roof_under(ROOF_Z1) - 0.10,
          roof_top(ROOF_Z1) + 0.03, ROOF_Z1, ROOF_Z1 + 0.05, "roof")
    n = 0
    for sx in (-1, 1):
        for z in (-10.0, -5.0, 0.0, 5.0, 10.0):
            k.member(f"outrigger_{n}", (sx * (HALL_X + 0.25), roof_under(z) - 1.25, z),
                     (sx * (EAVE_X - 0.10), roof_under(z) - 0.17, z), 0.20, 0.30, "steel",
                     up=(0, 1, 0))
            n += 1
    for sx, tag in ((-1, "L"), (1, "R")):
        for j, xp in enumerate((12.80, 13.55)):
            za, zb = FACE_Z + 0.05, ROOF_Z1 - 0.05
            k.member(f"purlin_{tag}{j}", (sx * xp, roof_under(za) - 0.11, za),
                     (sx * xp, roof_under(zb) - 0.11, zb), 0.12, 0.20, "steel", up=(0, 1, 0))
        # T71 (review 8, Richard 2026-10-18 "it didn't add anything"): the timber corner fins (timberfin_L/R: 0.20 x
        # 0.80 m, y 9.50..19.80, hung 4.3 m over the plinth) are gone; the steel face columns are the corner

    # glazing (painted) and transoms; the two side-wall door bays leave their openings at the
    # boarding level (-X: the stair-landing door, +X: the annex door, revision 1)
    yt = 12.0
    gi = 0
    for sx in (-1, 1):
        xg = sx * (HALL_X - 0.10)
        for zb0, zb1 in ((-10.0, -5.0), (-5.0, 0.0), (0.0, 5.0), (5.0, 10.0)):
            za, zb = zb0 + 0.25, zb1 - 0.25
            if sx < 0 and zb0 == -5.0:                 # -X door bay (stair landing door)
                d0, _d1 = STAIR_DOOR_Z
                panes = [(za, d0, CAP_TOP, yt - 0.10), (d0, zb, CAP_TOP, DECK_Y),
                         (d0, zb, DECK_Y + 2.45, yt - 0.10)]
            elif sx > 0 and zb0 == -10.0:              # +X door bay (annex door)
                d0, d1 = ANNEX_DOOR_Z
                panes = [(za, d0, CAP_TOP, yt - 0.10), (d0, d1, CAP_TOP, DECK_Y),
                         (d0, d1, DECK_Y + 2.95, yt - 0.10), (d1, zb, CAP_TOP, yt - 0.10)]
            else:
                panes = [(za, zb, CAP_TOP, yt - 0.10)]
            for z0, z1, y0, y1 in panes:
                k.box(f"glazepanel_{gi}", xg - 0.03, xg + 0.03, y0, y1, z0, z1, "glass")
                gi += 1
            ub0, ub1 = roof_under(za) - 0.05, roof_under(zb) - 0.05
            k.prism(f"glazepanel_{gi}",
                    [(xg - 0.03, yt + 0.10, za), (xg + 0.03, yt + 0.10, za),
                     (xg + 0.03, yt + 0.10, zb), (xg - 0.03, yt + 0.10, zb)],
                    [(xg - 0.03, ub0, za), (xg + 0.03, ub0, za),
                     (xg + 0.03, ub1, zb), (xg - 0.03, ub1, zb)], "glass")
            gi += 1
    zf = HALL_Z - 0.10
    for xb0, xb1 in ((-12.0, -6.0), (-6.0, 0.0), (0.0, 6.0), (6.0, 12.0)):
        xa, xb = xb0 + 0.25, xb1 - 0.25
        k.box(f"glazepanel_{gi}", xa, xb, CAP_TOP, yt - 0.10, zf - 0.03, zf + 0.03, "glass")
        gi += 1
        k.box(f"glazepanel_{gi}", xa, xb, yt + 0.10, roof_under(HALL_Z) - 0.05, zf - 0.03,
              zf + 0.03, "glass")
        gi += 1
    for sx, tag in ((-1, "L"), (1, "R")):
        x = sx * HALL_X
        k.box(f"transom_{tag}", x - 0.12, x + 0.12, yt - 0.10, yt + 0.10, -HALL_Z, HALL_Z, "steel")
    # revision 2 (C19, B4): the front transom's ends stop 0.01 inside the side transoms they butt (their top /
    # bottom faces shared 0.12 x 0.12 m of plane at each corner)
    k.box("transom_F", -HALL_X + 0.11, HALL_X - 0.11, yt - 0.10, yt + 0.10, HALL_Z - 0.12, HALL_Z + 0.12,
          "steel")
    # side walls (JH-05 / JH-07): dark mullions every 1.25 m and horizontal fins outside the
    # glazing replace the old outer X-bracing; the village (+Z) wall keeps its X-bracing
    mi = fi = 0
    for sx in (-1, 1):
        xm0, xm1 = sorted((sx * (HALL_X - 0.05), sx * (HALL_X + 0.10)))
        xf0, xf1 = sorted((sx * (HALL_X + 0.10), sx * (HALL_X + 0.45)))
        door = STAIR_DOOR_Z if sx < 0 else ANNEX_DOOR_Z
        for zb0 in (-10.0, -5.0, 0.0, 5.0):
            for j in (1, 2, 3):
                zm = zb0 + 1.25 * j
                if door[0] - 0.10 < zm < door[1] + 0.10:
                    continue
                k.box(f"mullion_{mi}", xm0, xm1, CAP_TOP, roof_under(zm) - 0.05, zm - 0.04,
                      zm + 0.04, "steel")
                mi += 1
            za, zb = zb0 + 0.30, zb0 + 4.70
            ytop = min(roof_under(za), roof_under(zb)) - 0.45
            yy = 7.40
            while yy <= ytop:
                skip = 11.75 < yy < 12.30                                   # the transom
                skip |= sx > 0 and yy < 12.70                               # annex roof (+X)
                skip |= sx < 0 and zb0 == -5.0 and yy < 9.00                # stair door (-X)
                if not skip:
                    k.box(f"fin_{fi}", xf0, xf1, yy - 0.03, yy + 0.03, za, zb, "steel")
                    fi += 1
                yy += 1.40
    bi = 0
    zb_ = HALL_Z + 0.20
    for xb0, xb1 in ((-12.0, -6.0), (-6.0, 0.0), (0.0, 6.0), (6.0, 12.0)):
        xa, xb = xb0 + 0.25, xb1 - 0.25
        for y0, y1 in ((CAP_TOP + 0.2, yt), (yt, roof_under(HALL_Z) - 0.3)):
            k.member(f"brace_{bi}", (xa, y0, zb_), (xb, y1, zb_), 0.25, 0.12, "steel",
                     up=(0, 0, 1))
            # revision 2 (C19, B4): the second member of each X is XCROSS_SHALLOW shallower, so the two
            # never share a face plane where they cross
            k.member(f"brace_{bi + 1}", (xa, y1, zb_), (xb, y0, zb_), 0.25, 0.12 - XCROSS_SHALLOW, "steel",
                     up=(0, 0, 1))
            bi += 2

    # -- platforms (boarding level = cabin interior floor), painted edge strips (F8); they
    # start behind the moving-platform pit (revision 1) -------------------------------------
    zd0 = TRENCH_Z1
    k.box("deck_centre", -2.90, 2.90, CAP_TOP, DECK_Y, zd0, -0.80, "concrete", drop=("-Y",))
    for sx, tag in ((-1, "L"), (1, "R")):
        k.box(f"edgestrip_c{tag}", min(sx * 2.90, sx * 3.20), max(sx * 2.90, sx * 3.20),
              CAP_TOP, DECK_Y, zd0, -0.80, "safety", drop=("-Y",))
        k.box(f"deck_outer{tag}", min(sx * 8.10, sx * 11.80), max(sx * 8.10, sx * 11.80),
              CAP_TOP, DECK_Y, zd0, -0.80, "concrete", drop=("-Y",))
        k.box(f"edgestrip_o{tag}", min(sx * 7.80, sx * 8.10), max(sx * 7.80, sx * 8.10),
              CAP_TOP, DECK_Y, zd0, -0.80, "safety", drop=("-Y",))
    k.box("deck_cross", -11.80, 11.80, CAP_TOP, DECK_Y, -0.80, 3.40, "concrete", drop=("-Y",))
    bay_gates(k, GATE_EDGES, z0=GATE_Z0["base"], role="base")
    rails = []
    for sx, tag in ((-1, "L"), (1, "R")):
        pf = [(sx * 7.90, DECK_Y, zd0 + 0.05), (sx * 11.70, DECK_Y, zd0 + 0.05)]
        pc = [(sx * 11.70, DECK_Y, 3.35), (sx * 1.40, DECK_Y, 3.35)]
        k.handrail(f"face{tag}", pf, "steel")
        k.handrail(f"cross{tag}", pc, "steel")
        rails += [pf, pc]
    build_base_openend(k)
    # revision 2 (B5, JH-04: a bucking-horse ring on every panel of the platform rail): one medallion per
    # panel on the moving platform (3), the face rails (2 + 2) and the cross rails (5 + 5) = 17
    mx = 3.05
    medallions(k, "medal_b", [[(-mx + 0.05, DECK_Y, TRENCH_Z0 + 0.10), (mx - 0.05, DECK_Y, TRENCH_Z0 + 0.10)]]
               + rails)

    # -- operator cabin (_Win (1,1) -> 6); kept at LOD2 (skeptic MC4) ----------------------
    ox, oz0, oz1 = 2.40, -3.20, -0.80
    k.box("opcab", -ox, ox, DECK_Y, DECK_Y + 1.00, oz0, oz1, "steel", drop=("-Y",))
    for n, (x, z) in enumerate(((-ox, oz0), (ox, oz0), (ox, oz1), (-ox, oz1))):
        k.box(f"opcabpost_{n}", x - 0.06, x + 0.06, DECK_Y + 1.00, DECK_Y + 2.40, z - 0.06,
              z + 0.06, "steel")
    k.box("opcabtop", -ox, ox, DECK_Y + 2.40, DECK_Y + 2.80, oz0, oz1, "steel")
    roof_o = k.box("opcabroof", -ox - 0.15, ox + 0.15, DECK_Y + 2.80, DECK_Y + 3.00,
                   oz0 - 0.15, oz1 + 0.15, "roof")
    k.drip(roof_o)
    y0w, y1w = DECK_Y + 1.00, DECK_Y + 2.40
    e = 0.005
    k.quad("opcabwin_nx", [(-ox - e, y0w, oz0), (-ox - e, y0w, oz1), (-ox - e, y1w, oz1),
                           (-ox - e, y1w, oz0)], "glass", (-1, 0, 0))
    k.quad("opcabwin_px", [(ox + e, y0w, oz0), (ox + e, y0w, oz1), (ox + e, y1w, oz1),
                           (ox + e, y1w, oz0)], "glass", (1, 0, 0))
    # -Z (line-facing) band: the operator door at x -1.50 takes its left end (revision 1)
    k.quad("opcabwin_nz", [(-0.95, y0w, oz0 - e), (ox, y0w, oz0 - e), (ox, y1w, oz0 - e),
                           (-0.95, y1w, oz0 - e)], "glass", (0, 0, -1))
    k.quad("opcabwin_pz", [(-ox, y0w, oz1 + e), (ox, y0w, oz1 + e), (ox, y1w, oz1 + e),
                           (-ox, y1w, oz1 + e)], "glass", (0, 0, 1))
    k.door("opcab", 0.90, 2.10, (-1.50, DECK_Y, oz0), "-Z", mat="steel")
    k.box("opcabfill_0", -ox + 0.06, -2.01, y0w, y1w, oz0 - 0.02, oz0, "steel")
    k.box("opcabfill_1", -2.01, -0.99, DECK_Y + 2.16, y1w, oz0 - 0.02, oz0, "steel")

    # -- rope machinery behind the dock: drive bullwheel ----------------------------------------
    # revision 2 (ruling Q6): 4.0 m (was 10.6), JH-04 orange-yellow, 5 broad spokes, closed bore (S1); the
    # two deflection sheaves are in build_base_drive
    cx, cy, cz = BW_CENTRE
    k.box("drivehouse", -1.20, 1.20, DECK_Y, DECK_Y + 2.00, cz - 1.20, cz + 1.20, "steel",
          drop=("-Y",))
    k.cyl("bwshaft", 0.25, (cx, DECK_Y + 2.00, cz), (cx, cy - 0.35, cz), "galv", segs=12,
          caps="none")
    k.sheave("bw", BASE_BW_R, BASE_BW_W, BW_CENTRE, "Y", segs=BASE_BW_SEGS, hub=False, r_hub=BASE_BW_BORE,
             groove=BASE_BW_R - (BASE_BW_RR - HAUL_ROPE_R), mat=BASE_WHEEL_MAT, rename=lambda n: "bwrim")
    for n in range(BASE_BW_SPOKES):
        a = 2.0 * math.pi * n / BASE_BW_SPOKES
        k.member(f"bwspoke_{n}", (cx + 0.55 * math.cos(a), cy, cz + 0.55 * math.sin(a)),
                 (cx + (BASE_BW_BORE + 0.02) * math.cos(a), cy, cz + (BASE_BW_BORE + 0.02) * math.sin(a)),
                 0.36, 0.10, BASE_WHEEL_MAT)
    k.cyl("bwhub", 0.60, (cx, cy - 0.35, cz), (cx, cy + 0.35, cz), "galv", segs=16)
    build_base_drive(k)

    # -- track-rope anchorage: counterweight shafts; curved saddle girders on a saddle portal
    for side in (-1, 1):
        tag = "L" if side < 0 else "R"
        x0, x1 = min(side * 4.30, side * 6.70), max(side * 4.30, side * 6.70)
        k.box(f"cwshaft_{tag}", x0, x1, CAP_TOP, 12.50, 4.00, 7.00, "concrete", drop=("-Y",))
        k.box(f"cwlid_{tag}", x0 + 0.10, x1 - 0.10, 12.50, 12.56, 4.10, 6.90, "galv")
    build_base_saddles(k)

    # -- haul loop (starts EXACTLY at the stub tips, C2); revision 2: round the two deflection sheaves and the
    # 4.0 m drive wheel (haul_route_base, C20b) ---------------------------------------------------
    k.tube("rope_haul", haul_route_base()["points"], HAUL_ROPE_R, "rope", segs=6)

    # -- platform lamps hung from the roof. Revision 2 (Richard: vanilla lamps; JH-04: downlights, no pole
    # lights): the modeled pendant boxes and the two entrance lantern boxes are gone - the vanilla
    # RoofLightRandom01 rows (SUBOBJ_ROWS) ARE the lamps, each hung from a ceiling plate on its rod (skeptic
    # MC3: C16's ceiling ray needs a face over the row; the 4-seg rods are open tubes). The entrance's two
    # vanilla WallLightShortRange rows stay (ruling Q4: small wall lights only).
    n = 0
    for xc in (-9.80, 0.0, 9.80):
        for zc in (-7.50, -3.00):
            k.box(f"lamp_plate_{n}", xc - 0.18, xc + 0.18, ROOFLIGHT_Y, ROOFLIGHT_Y + 0.03, zc - 0.18, zc + 0.18,
                  "galv")
            k.cyl(f"lamp_rod_{n}", 0.02, (xc, ROOFLIGHT_Y + 0.01, zc), (xc, roof_under(zc) + 0.05, zc), "galv",
                  segs=4, caps="none")
            n += 1

    # -- village front: entrance, canopy on timber V-struts and stone piers (JH-05), ticket
    # windows, pictogram wall ----------------------------------------------------------------
    # revision 2 (C19, B4): door centers 5.365 / 6.635 - the two 0.06 m jambs now abut at x 6.00 instead of
    # overlapping by 0.01 (a shared jamb plane, 314 cm2)
    for tag, xc in (("entL", 6.0 - 0.635), ("entR", 6.0 + 0.635)):
        k.door(tag, 1.15, 3.00, (xc, 0.05, HALL_Z), "+Z", mat="steel")
    k.box("entrycanopy", 3.00, 9.00, 4.20, 4.45, HALL_Z, 11.90, "timber")
    k.box("canopyroof", 2.95, 9.05, 4.45, 4.52, HALL_Z, 11.95, "roof")
    for tag, xc in (("L", 3.60), ("R", 8.40)):
        k.box(f"vpier_{tag}", xc - 0.45, xc + 0.45, 0.0, 1.20, 11.15, 11.95, "stone",
              open_bottom=True)
        for s, dx in (("a", -0.55), ("b", 0.55)):
            # revision 2 (C19): the second strut of each V is XCROSS_SHALLOW shallower (shared foot plane)
            k.member(f"vstrut_{tag}{s}", (xc, 1.20, 11.55), (xc + dx, 4.20, 11.55), 0.22,
                     0.22 - (XCROSS_SHALLOW if s == "b" else 0.0), "timber", up=(0, 0, 1))
    for n, (x0, x1) in enumerate(((-10.60, -8.90), (-8.50, -6.80))):
        k.quad(f"tickwin_f{n}", [(x0, 1.20, HALL_Z + 0.03), (x1, 1.20, HALL_Z + 0.03),
                                 (x1, 3.00, HALL_Z + 0.03), (x0, 3.00, HALL_Z + 0.03)],
               "glass", (0, 0, 1))
        k.box(f"sill_f{n}", x0 - 0.05, x1 + 0.05, 1.08, 1.20, HALL_Z, HALL_Z + 0.15, "concrete")
    k.box("pictowall", -4.40, 1.60, 0.60, 4.60, HALL_Z, HALL_Z + 0.25, "stone", drop=("-Z",))
    k.quad("pictodisc", [(-2.90, 1.10, HALL_Z + 0.27), (0.10, 1.10, HALL_Z + 0.27),
                         (0.10, 4.10, HALL_Z + 0.27), (-2.90, 4.10, HALL_Z + 0.27)],
           "stone", (0, 0, 1))
    for sx, tag in ((-1, "L"), (1, "R")):
        x = sx * (HALL_X + 0.45)
        k.cyl(f"downspout_{tag}", 0.06, (x, CAP_TOP, HALL_Z + 0.45),
              (x, roof_under(ROOF_Z) - 0.05, HALL_Z + 0.45), "roof", segs=6, caps="none")

    # -- annex (waiting hall = the side building, _Win rows 17/18 + ground windows 8) -------
    ax0, ax1, az0, az1 = 12.40, 19.60, -8.00, 7.20
    k.box("annexbase", ax0, ax1, 0.0, STONE_TOP, az0, az1, "stone", open_bottom=True,
          drop=("+Y",))
    k.box("annexcap", ax0 - 0.10, ax1 + 0.10, STONE_TOP, CAP_TOP, az0 - 0.10, az1 + 0.10,
          "concrete", drop=("-Y",))
    if ANNEX_STAIR and NB3["wall_cut"]:
        # nitpick B3: the hall's inner door opens THROUGH this wall - cut at the doorway (ANNEX_DOOR_Z, up to the door's
        # head), the two wall pieces and the header closed boxes like the old one; the landing block fills the sill
        d0_, d1_ = ANNEX_DOOR_Z
        k.box("annexback_0", ax0, ax0 + 0.12, CAP_TOP, 11.60, az0, d0_, "steel")
        k.box("annexback_1", ax0, ax0 + 0.12, CAP_TOP, 11.60, d1_, az1, "steel")
        k.box("annexback_2", ax0, ax0 + 0.12, DECK_Y + 2.80, 11.60, d0_, d1_, "steel")
    else:
        k.box("annexback", ax0, ax0 + 0.12, CAP_TOP, 11.60, az0, az1, "steel")
    ytr0, ytr1, ytop = 8.30, 8.50, 11.60
    fcols = [(ax1, z) for z in (-8.0, -4.2, -0.4, 3.4, 7.2)] + [(16.0, az0), (16.0, az1)]
    for n, (x, z) in enumerate(fcols):
        k.box(f"annexframe_{n}", x - 0.15, x + 0.15, CAP_TOP, ytop, z - 0.15, z + 0.15, "steel",
              drop=("-Y",))
    k.box("annextransom_x", ax1 - 0.12, ax1 + 0.12, ytr0, ytr1, az0, az1, "steel")
    # revision 2 (C19, B4): the side transoms start 0.02 inside the back wall (their end face lay in its -X
    # face) and stop 0.01 inside the front transom (shared top / bottom planes at the corners)
    k.box("annextransom_n", ax0 + 0.02, ax1 - 0.11, ytr0, ytr1, az0 - 0.12, az0 + 0.12, "steel")
    k.box("annextransom_p", ax0 + 0.02, ax1 - 0.11, ytr0, ytr1, az1 - 0.12, az1 + 0.12, "steel")
    wn = 0
    for rowtag, y0, y1 in (("lo", CAP_TOP, ytr0), ("hi", ytr1, ytop)):
        zs = (-8.0, -4.2, -0.4, 3.4, 7.2)
        for za, zb in zip(zs, zs[1:]):
            zb_ = zb - 0.15
            if RAMPS and rowtag == "lo" and za == 3.4:
                # nitpick B2: the side-area door takes the pane's +Z part (build_base_side_door); the pane keeps its name
                # up to the left jamb, a transom pane (same _Win cell) spans the door over its head
                zb_ = NB2_DOOR["jamb_lo"]
                yt0 = NB2_DOOR["head"][1]
                k.quad("annexwinlo_dt", [(ax1 + 0.005, yt0, NB2_DOOR["jamb_lo"]),
                                         (ax1 + 0.005, yt0, NB2_DOOR["jamb_hi"]),
                                         (ax1 + 0.005, y1, NB2_DOOR["jamb_hi"]),
                                         (ax1 + 0.005, y1, NB2_DOOR["jamb_lo"])], "glass", (1, 0, 0))
            k.quad(f"annexwin{rowtag}_{wn}", [(ax1 + 0.005, y0, za + 0.15), (ax1 + 0.005, y0, zb_),
                                              (ax1 + 0.005, y1, zb_), (ax1 + 0.005, y1, za + 0.15)],
                   "glass", (1, 0, 0))
            wn += 1
        for xa, xb in ((ax0 + 0.12, 15.85), (16.15, ax1 - 0.15)):
            k.quad(f"annexwin{rowtag}_{wn}", [(xa, y0, az0 - 0.005), (xb, y0, az0 - 0.005),
                                              (xb, y1, az0 - 0.005), (xa, y1, az0 - 0.005)],
                   "glass", (0, 0, -1))
            wn += 1
            k.quad(f"annexwin{rowtag}_{wn}", [(xa, y0, az1 + 0.005), (xb, y0, az1 + 0.005),
                                              (xb, y1, az1 + 0.005), (xa, y1, az1 + 0.005)],
                   "glass", (0, 0, 1))
            wn += 1
    # x1 19.98: the 0.02 drip flashing then ends ON the +X lot line (20.00), not 0.02 past it
    # (gondola-tram-review-geometry F1; C7 is strict)
    ar_o = k.box("annexroof", 12.30, 19.98, ytop, ytop + 0.60, -8.30, 7.35, "roof")
    k.drip(ar_o)
    for n, zc in enumerate((-4.0, 0.0, 4.0)):
        k.quad(f"tickwin_a{n}", [(ax1 + 0.03, 1.20, zc - 0.80), (ax1 + 0.03, 1.20, zc + 0.80),
                                 (ax1 + 0.03, 3.00, zc + 0.80), (ax1 + 0.03, 3.00, zc - 0.80)],
               "glass", (1, 0, 0))
    build_inner_doors(k)

    # -- clock tower (station-local module, contract 5.6) ----------------------------------
    ct_meta = None
    if opts.clock_tower:
        # eave 0.60 (was 0.80): cap / spire half-width 2.60 about CT_LOC z 9.40 ends ON the
        # front (+Z) lot line 12.00 (review F1). Moving the tower to z 9.20 instead would push its
        # base into the annex (annexcap / frame column to z 7.30-7.35).
        _p, ct_meta = landmark_tower(k, "ct", h=CT_H, w=4.0, base_h=7.0, stage=(19.0, 22.0),
                                     eave=0.6, face="clock", loc=CT_LOC)

    # -- switchback stair on -X to the hall door at the boarding level -------------------
    build_base_stair(k)
    k.door("hall", 1.70, 2.40, (-HALL_X - 0.02, DECK_Y + 0.02, -1.30), "-X", mat="steel")
    if RAMPS:
        build_base_side_door(k)
        build_base_ramp(k)
    if ANNEX_STAIR:
        build_base_annex_stair(k)
    if JH_DETAILS:
        build_base_jh(k)
    if DETAIL1:
        build_base_detail(k)
    return {"clock_tower": ct_meta}


def build_base_jh(k):
    """Nitpick B (JH_DETAILS; JH-04 / JH-03 / ref 07): the canopy on V-shaped timber struts across the village front,
    the annex ticket windows' sills, the chalkboard A-frame stand. The LIFT TICKETS / ENTRANCE boards and the annex
    ticket-window frames are dressing (build_base_detail)."""
    c = JH_CANOPY
    x, z0, z1, y0 = c["x"], c["z0"], c["z1"], c["y0"]

    def y_top(z):                       # the canopy's top plane: falls from the wall to the fascia (drains outward)
        return c["y_back"] + (c["y_front"] - c["y_back"]) * (z - z0) / (z1 - z0)
    rb = [(-x, y_top(z0), z0), (x, y_top(z0), z0), (x, y_top(z1), z1), (-x, y_top(z1), z1)]
    k.prism("jhcanopy", [(-x, y0, z0), (x, y0, z0), (x, y0, z1), (-x, y0, z1)], rb, "timber", open_top=True)
    k.prism("jhroof", rb, [(p[0], p[1] + c["roof_t"], p[2]) for p in rb], "roof", open_bottom=True)
    # steel ties from the front columns' faces (z 10.25) into the canopy's back edge, under its soffit plane
    for i, x in enumerate(JH_TIES_X):
        x0, x1 = max(x - 0.10, -c["x"]), min(x + 0.10, c["x"])
        k.box(f"jhtie_{i}", x0, x1, c["y0"] + 0.04, c["y0"] + 0.18, 10.23, c["z0"] + 0.04, "steel")
    v, s = JH_V, JH_SHOE
    for i, xc in enumerate(JH_V_PIERS):
        k.box(f"jhshoe_{i}", xc - s["hw"], xc + s["hw"], s["y0"], s["y1"], s["z0"], s["z1"], "steel")
        spread = v["spread_end"] if abs(xc) > 11.0 else v["spread"]
        foot = (xc, v["foot_y"], v["foot_z"])
        for leg, dx in (("a", -spread), ("b", spread)):
            # the second leg XCROSS_SHALLOW shallower: the two legs share their foot (C19, as vstrut_)
            k.member(f"jhstrut_{i}{leg}", foot, (xc + dx, v["head_y"], v["head_z"]), v["w"],
                     v["d"] - (XCROSS_SHALLOW if leg == "b" else 0.0), "timber", up=(0, 0, 1))
    # the annex's three grade ticket windows (tickwin_a0..2, +X face) get sills like the front pair (sill_f*)
    for n, zc in enumerate(JH_TICKWIN_A):
        k.box(f"sill_a{n}", JH_ANNEX_X, JH_ANNEX_X + 0.15, 1.08, 1.20, zc - 0.85, zc + 0.85, "concrete")
    # the chalkboard A-frame (PLACEHOLDER): two timber leaves leaning together to a 0.03 m hinge line, black chalk
    # faces on their outer sides; 0.02 m off the grade (out of the engine-base band +-0.01, as the kit rack)
    a = JH_CHALK
    x0, x1 = a["x"] - a["w"] / 2.0, a["x"] + a["w"] / 2.0
    y0, y1 = a["y0"], a["y0"] + a["h"]
    for leg, sz in (("a", 1.0), ("b", -1.0)):
        zb_o, zb_i = a["z"] + sz * a["foot"], a["z"] + sz * (a["foot"] - a["t"])
        zt_o, zt_i = a["z"] + sz * a["t"], a["z"]
        bot = [(x0, y0, zb_i), (x1, y0, zb_i), (x1, y0, zb_o), (x0, y0, zb_o)]
        top = [(x0, y1, zt_i), (x1, y1, zt_i), (x1, y1, zt_o), (x0, y1, zt_o)]
        k.prism(f"chalkboard_{leg}", bot, top, "timber")
        # the chalk face: the outer face's plane (bottom-outer edge -> top-outer edge), inset 0.05, 3 mm proud
        dz, dy = zt_o - zb_o, y1 - y0
        L = math.hypot(dz, dy)
        nz, ny = sz * dy / L, abs(dz) / L                  # outward normal (away from the hinge, up)
        pts = []
        for f_, x_ in ((0.08, x0 + 0.05), (0.08, x1 - 0.05), (0.90, x1 - 0.05), (0.90, x0 + 0.05)):
            pts.append((x_, y0 + f_ * dy + 0.003 * ny, zb_o + f_ * dz + 0.003 * nz))
        k.quad(f"chalkboard_{leg}face", pts, "safety_black", (0.0, ny, nz))


def build_base_side_door(k):
    """Nitpick B2 (RAMPS; T-1 "into the side area"): the side-area entrance, a REAL opening in the annex's +X glazing
    (build_base cuts the pane: annexwinlo_3 stops at the left jamb, annexwinlo_dt spans the door over its head) with
    a galvanised storefront double door - jambs, head, mid stile, two leaves with glass lites and push bars - its sill
    on the annex cap (CAP_TOP 5.20 = the side area's floor), the ramp's balcony 0.06 m off its jambs. The leaves stay
    at LOD1 (the opening never reads as a hole), the lites / bars go (LOD1_DELETE)."""
    D = NB2_DOOR
    x0, x1 = D["jamb_x"]
    z0, z1 = D["z"]
    yh0, yh1 = D["head"]
    k.box("annexdoor_jamb0", x0, x1, CAP_TOP, yh0, D["jamb_lo"], z0, "galv", drop=("-Y",))
    k.box("annexdoor_jamb1", x0, x1, CAP_TOP, yh0, z1, D["jamb_hi"], "galv", drop=("-Y",))
    k.box("annexdoor_head", x0, x1, yh0, yh1, D["jamb_lo"], D["jamb_hi"], "galv")
    zm = (z0 + z1) / 2.0
    lx0, lx1 = D["leaf_x"]
    k.box("annexdoor_mid", lx0 - 0.02, lx1 + 0.02, CAP_TOP, yh0, zm - 0.03, zm + 0.03, "galv", drop=("-Y",))
    for n, (za, zb) in enumerate(((z0, zm - 0.03), (zm + 0.03, z1))):
        k.box(f"annexdoor_leaf{n}", lx0, lx1, CAP_TOP + 0.02, yh0 - 0.02, za, zb, "galv")
        xl = lx1 + 0.005
        k.quad(f"doorlite_side{n}", [(xl, CAP_TOP + 0.45, za + 0.10), (xl, CAP_TOP + 0.45, zb - 0.10),
                                     (xl, yh0 - 0.16, zb - 0.10), (xl, yh0 - 0.16, za + 0.10)], "glass", (1, 0, 0))
        k.cyl(f"annexdoor_bar{n}", 0.022, (lx1 + 0.04, CAP_TOP + 1.05, za + 0.15),
              (lx1 + 0.04, CAP_TOP + 1.05, zb - 0.15), "galv", segs=6)


def _ramp_deck(k, name, x0, x1, z_foot, y_foot, z_head, y_head, t, on_grade=False):
    """A sloped deck across x0..x1 from (z_foot, y_foot) to (z_head, y_head) (top surface), `t` deep under it (one closed
    part: the grating relief draws on every face). On the grade (`on_grade`: the foot ON a floor at y_foot) its
    underside meets that floor `t / grade` up the slope: the profile foot -> head -> head - t -> the sole point (a
    quad, no knife edge, nothing below the floor)."""
    s = 1.0 if z_head > z_foot else -1.0
    if on_grade:
        zs = z_foot + s * t / RAMP["grade"]
        prof = [(z_foot, y_foot), (z_head, y_head), (z_head, y_head - t), (zs, y_foot)]
    else:
        prof = [(z_foot, y_foot), (z_head, y_head), (z_head, y_head - t), (z_foot, y_foot - t)]
    return k.prism(name, [(x0, y, z) for z, y in prof], [(x1, y, z) for z, y in prof], "galv")


def _rails(k, runs, mat):
    """Nitpick B2: straight railing runs (the kit handrail, posts every NB2_POST_EVERY): (name, a, b) each, every run
    stopping 0.10 short of the next one's end post (the base stair's rule) - no fillet rings at the ramp / landing
    bends (a polyline rail costs 96 tris a bend in fillet rings: the 16,000 cap)."""
    for name, a, b in runs:
        k.handrail(name, [a, b], mat, post_every=NB2_POST_EVERY)


def build_base_ramp(k):
    """Nitpick B2 (RAMPS; T-1 "a RAMP on the right side, by the clock tower, into the side area"; JH-04 / JH-05 / ref 04:
    the long climb along the hall's own side wall). DESIGN frame (the new cell is x 20..28). A 1:8 switchback in the new
    cell beside the annex: the plaza paving (concrete, at NB2_PAVE t over the flattened lot: `plaza_walk` - the
    walkway in front of the annex's LIFT TICKETS windows, x 19.62..22.60, clear at grade - and `plaza_front` at the
    ramp foot; `plazaunder_*` under the decks), lane A (outer) up from the paving at the village end, the turn landing
    at the line end, lane B (inner) back up to the top landing at the annex cap level CAP_TOP and the balcony along
    the annex's +X glazing to the side-area door (build_base_side_door); the balcony bridges the walkway at 5.20.
    Grating decks (relief) NB2_RAMP_T deep, steel columns on the paving, rails both sides of every lane and round
    every landing (straight kit runs). base_ramp_plan() holds the levels."""
    P = base_ramp_plan()
    g = RAMP["grade"]
    y0, yt, yc = P["y_foot"], P["y_turn"], P["y_top"]
    zt0, zt1 = NB2_TURN_Z
    ztop0, ztop1 = P["z_top0"], NB2_TOP_Z1
    (ax0, ax1), (bx0, bx1) = NB2_LANE["A"], NB2_LANE["B"]
    t, T = NB2_RAMP_T, NB2_DECK_T
    px0, px1, pz0, pz1 = NB2_PAVE["x0"], NB2_PAVE["x1"], NB2_PAVE["z0"], NB2_PAVE["z1"]
    # the paving: single up-facing quads NB2_PAVE t over the flattened lot (out of the engine band, no skirt)
    for name, x0, x1, z0, z1 in (("plaza_walk", px0, bx0, pz0, pz1), ("plaza_front", bx0, ax0, ztop0, pz1),
                                 ("plazaunder_0", bx0, px1, pz0, ztop0), ("plazaunder_1", ax0, px1, ztop0, pz1)):
        k.quad(name, [(x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1)], "concrete", (0, 1, 0))
    # decks: lane A from the paving (village end) up toward -Z to the turn landing; lane B from it back up toward +Z
    _ramp_deck(k, "ramp_A", ax0, ax1, NB2_FOOT_Z, y0, zt1, yt, t, on_grade=True)
    _ramp_deck(k, "ramp_B", bx0, bx1, zt1, yt, ztop0, yc, t)
    k.box("landing_turn", bx0, ax1, yt - T, yt, zt0, zt1, "galv")
    k.box("landing_rtop", NB2_BALC["x0"], bx1, yc - T, yc, ztop0, ztop1, "galv")
    k.box("landing_balc", NB2_BALC["x0"], bx0, yc - T, yc, NB2_BALC["z0"], ztop0, "galv")
    # columns on the paving: three bents under both lanes, the turn landing's and the top landing's corners, the
    # balcony's free corner (its annex edge rests on a ledger against the annex cap)
    c = NB2_COL / 2.0
    cols = []
    for zb in (-5.30, -0.30, 4.70):
        for x in (ax0 + 0.15, ax1 - 0.15):
            cols.append((x, zb, y0 + (NB2_FOOT_Z - zb) * g - t))
        for x in (bx0 + 0.15, bx1 - 0.15):
            cols.append((x, zb, yt + (zb - zt1) * g - t))
    for x in (bx0 + 0.15, ax1 - 0.15):
        for zb in (zt0 + 0.15, zt1 - 0.15):
            cols.append((x, zb, yt - T))
    for x in (NB2_BALC["x0"] + 0.15, bx1 - 0.15):
        for zb in (ztop0 + 0.15, ztop1 - 0.15):
            cols.append((x, zb, yc - T))
    cols.append((bx0 - 0.15, NB2_BALC["z0"] + 0.15, yc - T))
    for i, (x, zb, ytop) in enumerate(cols):
        k.box(f"rampcol_{i}", x - c, x + c, y0, ytop, zb - c, zb + c, "steel", drop=("-Y",))
    # the balcony's annex edge on a ledger against the annex cap's +X face (x 19.70; the cap overhangs the stone 0.10)
    k.box("rampbeam_ledger", JH_ANNEX_X + 0.10, NB2_BALC["x0"] + 0.25, yc - T - 0.20, yc - T, NB2_BALC["z0"], 7.20,
          "steel")
    # rails: round the switchback's outside (lane A's outer edge, the turn landing, lane B's walkway edge), between the
    # lanes, round the top landing and the balcony (its annex edge open: the door)
    ri, e = NB2_RAIL_IN, 0.10
    zf = NB2_FOOT_Z - 0.05
    yf = y0 + 0.05 * g                                  # lane A's deck top 0.05 m up from its foot
    xa_o, xa_i, xb_o, xb_i = ax1 - ri, ax0 + ri, bx0 + ri, bx1 - ri
    xw = NB2_BALC["x0"] + ri                            # the top landing's / balcony's west rail line
    _rails(k, (("rampAo", (xa_o, yf, zf), (xa_o, yt, zt1)),
               ("rampAi", (xa_i, yt, zt1), (xa_i, yf, zf)),
               ("rampTe", (xa_o, yt, zt1 - e), (xa_o, yt, zt0 + ri)),
               ("rampTl", (xa_o - e, yt, zt0 + ri), (xb_o + e, yt, zt0 + ri)),
               ("rampTw", (xb_o, yt, zt0 + ri), (xb_o, yt, zt1 - e)),
               ("rampBo", (xb_o, yt, zt1), (xb_o, yc, ztop0)),
               ("rampBi", (xb_i, yt, zt1), (xb_i, yc, ztop0)),
               ("ramptopE", (xb_i, yc, ztop0 + e), (xb_i, yc, ztop1 - ri)),
               ("ramptopN", (xb_i - e, yc, ztop1 - ri), (xw + e, yc, ztop1 - ri)),
               ("ramptopW", (xw, yc, ztop1 - ri), (xw, yc, 7.45)),
               ("balcE", (bx0 - ri, yc, ztop0 - e), (bx0 - ri, yc, NB2_BALC["z0"] + ri)),
               ("balcS", (bx0 - ri - e, yc, NB2_BALC["z0"] + ri), (xw, yc, NB2_BALC["z0"] + ri))), "steel")


def _rail_nokick(k, name, a, b, mat, post_every=NB2_POST_EVERY):
    """Nitpick B3: a straight railing run of the kit WITHOUT its kick plate (posts at the ends, a top and a knee rail).
    The annex flight's steps are solid concrete boxes: a kick plate sloping along the rail line would cut their
    nosings, and a solid stair needs none. The kit builds the kick as a scene object; it is removed again."""
    parts = cs2_kit.handrail(name, [P(*a), P(*b)], h=1.10, post_every=post_every)
    keep = []
    for obj, q in parts:
        if obj.name.startswith("kick_"):
            me = obj.data
            bpy.data.objects.remove(obj, do_unlink=True)
            if me.users == 0:
                bpy.data.meshes.remove(me)
        else:
            keep.append((obj, q))
    return k.kit(keep, mat)


def build_base_annex_stair(k):
    """Nitpick B3 (ANNEX_STAIR; the B2 check's F2): the annex's inside connection. DESIGN frame. `landing_annex`: a closed
    concrete block from 0.01 m off the hall glazing's outer face (NB3 link_x0, the inner door's sill 6.30 flush with the glazing's
    lower pane) through the doorway cut in annexback (build_base) to NB3 landing_x1, standing on the annex floor; then
    `risers` equal risers down onto the floor over `risers - 1` solid concrete treads (tread_annex_1..); a kit railing each
    side of the landing (horizontal, with kick plates) and of the flight (sloped from the landing's end post to a post on
    the floor past the last riser, so every post stands on a surface and none stands in a tread's headroom). The solid
    steps drop their bottom and the face the previous step covers. base_annex_plan() holds the levels."""
    P_ = base_annex_plan()
    d0, d1 = ANNEX_DOOR_Z
    x_top, g, n = NB3["landing_x1"], NB3["going"], NB3["risers"]
    ins = NB3["inset"]
    k.box("landing_annex", NB3["link_x0"], x_top, CAP_TOP, DECK_Y, d0, d1, "concrete")
    if not NB3["flight"]:
        return
    for i in range(1, n):
        k.box(f"tread_annex_{i}", x_top + g * (i - 1), x_top + g * i, CAP_TOP, DECK_Y - P_["riser"] * i,
              d0 + ins, d1 - ins, "concrete", drop=("-Y", "-X"))
    x_foot = P_["x_foot"]
    xw = NB3["wall_x"][1] + 0.10
    for tag, z in (("n", P_["rail_z"][0]), ("s", P_["rail_z"][1])):
        # the landing run stops 0.10 short of the flight run's top post (the B2 rule: no two posts on one spot)
        k.handrail(f"annexl{tag}", [(xw, DECK_Y, z), (x_top - 0.15, DECK_Y, z)], "steel", post_every=NB2_POST_EVERY)
        _rail_nokick(k, f"annexf{tag}", (x_top - 0.05, DECK_Y, z), (x_foot + 0.05, CAP_TOP, z), "steel")


def build_base_drive(k):
    """Revision 2 (ruling Q6; JH-04 "orange-yellow wheel rims behind the lower end of each saddle girder in a
    galvanised box housing"): the two horizontal deflection sheaves (rope radius BASE_DS_RR, 2.0 m rims,
    `bwdrim_<s>` + `bwdhub_<s>`) that turn the x +-5.00 haul strands inward onto the 4.0 m drive wheel, each
    on a vertical axle (`bwdaxle_<s>`) hung from a galvanised arm (`bwdarm_<s>`) cantilevered off the saddle
    girder's inner web above the rope."""
    for side in (-1, 1):
        tag = "L" if side < 0 else "R"
        c = (side * BASE_DS_X, HAUL_Y, BASE_DS_Z)
        k.sheave(f"bwd{tag}", BASE_DS_RIM, 0.24, c, "Y", segs=12, hub=True, r_hub=BASE_DS_RR - 0.10,
                 groove=0.15 + HAUL_ROPE_R, mat=lambda nm: BASE_WHEEL_MAT if "rim" in nm else "galv",
                 rename=lambda nm, t=tag: f"bwdrim_{t}" if nm.endswith("_rim") else f"bwdhub_{t}")
        y_arm = HAUL_Y + 0.20                       # arm underside: 0.175 over the rope, 0.06 over the hub
        x_web = side * (TRACK_X - GIRDER_HW + 0.02)     # 0.02 into the saddle girder's inner web
        x_end = side * (BASE_DS_X - 0.12)
        k.box(f"bwdarm_{tag}", min(x_web, x_end), max(x_web, x_end), y_arm, y_arm + 0.12, BASE_DS_Z - 0.10,
              BASE_DS_Z + 0.10, "galv")
        k.cyl(f"bwdaxle_{tag}", 0.07, (c[0], HAUL_Y + 0.12, c[2]), (c[0], y_arm + 0.02, c[2]), "galv", segs=8,
              caps="none")


def build_base_openend(k):
    """The open uphill end (JH-04 / JH-07): rails ACROSS the pit, the moving platform parked
    between the berths (steel truss frame, grating top at the boarding level, wheels on the
    rails), guide horns either side of each berth, the catwalk under the portal girder."""
    for n, rz in enumerate(RAIL_Z):
        k.box(f"trackrail_{n}", -HALL_X + 0.40, HALL_X - 0.40, TRENCH_Y, TRENCH_Y + 0.12,
              rz - 0.04, rz + 0.04, "galv")
    mx, mz0, mz1 = 3.05, TRENCH_Z0 + 0.05, TRENCH_Z1 - 0.05
    yb0, yb1, ytc = TRENCH_Y + 0.35, TRENCH_Y + 0.60, DECK_Y - 0.26
    k.box("mplatbase", -mx, mx, yb0, yb1, mz0, mz1, "steel")
    k.box("mplattop", -mx, mx, ytc, DECK_Y - 0.06, mz0, mz1, "steel")
    for n, (x, z) in enumerate(((-mx, mz0), (mx, mz0), (mx, mz1), (-mx, mz1))):
        xa, xb = (x, x + 0.15) if x < 0 else (x - 0.15, x)
        za, zb = (z, z + 0.15) if z < -9.0 else (z - 0.15, z)
        k.box(f"mplatpost_{n}", xa, xb, yb1, ytc, za, zb, "steel")
    bi = 0
    for zf in (mz0 + 0.06, mz1 - 0.06):
        xs = (-mx + 0.10, -1.00, 1.00, mx - 0.10)
        for j, (a, b) in enumerate(zip(xs, xs[1:])):
            ya, yb = (yb1, ytc) if j % 2 == 0 else (ytc, yb1)
            # revision 2 (C19, B4): the middle brace of each W 0.02 shallower (shared planes at the joints)
            k.member(f"mplatbrace_{bi}", (a, ya, zf), (b, yb, zf), 0.12, 0.08 - (0.02 if j % 2 else 0.0),
                     "steel", up=(0, 0, 1))
            bi += 1
    k.grating("mplat", -mx, mx, mz0, mz1, DECK_Y)
    n = 0
    for rz in RAIL_Z:
        for x in (-2.55, 2.55):
            k.cyl(f"mplatwheel_{n}", 0.17, (x, TRENCH_Y + 0.29, rz - 0.06),
                  (x, TRENCH_Y + 0.29, rz + 0.06), "galv", segs=10)
            n += 1
    k.handrail("mplat", [(-mx + 0.05, DECK_Y, mz0 + 0.05), (mx - 0.05, DECK_Y, mz0 + 0.05)],
               "galv")
    # guide horns (curved tubes + a chord strut), rising from the pit floor and leaning out,
    # away from their berth, toward the arriving car (JH-07); clear of the cabin body (+-2.00)
    zf = TRENCH_Z0 + 0.10
    for n, (x, s) in enumerate(((-7.72, -1.0), (-3.28, 1.0), (3.28, -1.0), (7.72, 1.0))):
        pts = [(x, TRENCH_Y, zf), (x, 9.60, zf), (x + s * 0.55, 11.90, zf - 0.12),
               (x + s * 1.35, 13.70, zf - 0.27), (x + s * 1.75, 14.50, zf - 0.32)]
        k.tube(f"horn_{n}", pts, 0.09, "galv", segs=8, caps="end", bend_rings=3)
        k.tube(f"hornstrut_{n}", [(x, 6.90, zf), (x + s * 1.00, 12.90, zf - 0.20)], 0.055, "galv",
               segs=6, caps="both")
        k.box(f"hornbase_{n}", x - 0.22, x + 0.22, TRENCH_Y, TRENCH_Y + 0.06, zf - 0.16,
              zf + 0.16, "galv")
    # catwalk hung under the portal girder (JH-07 / JH-04 center platform)
    k.grating("pcat", -2.60, 2.60, -9.80, -8.00, 17.20, t=0.08)
    for n, (x, z) in enumerate(((-2.45, -9.72), (2.45, -9.72), (-2.45, -8.10), (2.45, -8.10))):
        ytop = 18.60 if z < -9.6 else roof_under(z) + 0.05
        k.cyl(f"pcathanger_{n}", 0.035, (x, 17.20, z), (x, ytop, z), "galv", segs=6, caps="none")
    k.handrail("pcat", [(-2.55, 17.20, -8.05), (-2.55, 17.20, -9.75), (2.55, 17.20, -9.75),
                        (2.55, 17.20, -8.05)], "galv")
    if PCAT_LADDER:
        build_pcat_ladder(k)


def build_pcat_ladder(k):
    """Revision 1c (C15): the catwalk's fixed ladder from the center deck (6.30) to 17.20, 10.9 m in
    one flight, tied at the deck and at the catwalk's back edge (lip at z -8.00). Rails `ladder_pcat_a/b`,
    brackets `ladder_pcat_ta/tb`, rungs `lrung_pcat_<k>` (LOD0 only: both prefixes are in LOD1_DELETE)."""
    x, z = PCAT_LADDER_X, PCAT_LADDER_Z
    for s, sx in (("a", -1.0), ("b", 1.0)):
        xr = x + sx * 0.21
        k.box(f"ladder_pcat_{s}", xr - 0.025, xr + 0.025, DECK_Y, PCAT_Y + 1.10, z - 0.025, z + 0.025,
              "galv")
        k.box(f"ladder_pcat_t{s}", xr - 0.025, xr + 0.025, PCAT_Y - 0.08, PCAT_Y, -8.00, z - 0.025,
              "galv")
    n, y = 0, DECK_Y + 0.25
    while y <= PCAT_Y + 1e-6:
        k.cyl(f"lrung_pcat_{n}", 0.018, (x - 0.21, y, z), (x + 0.21, y, z), "galv", segs=3,
              caps="none")
        n += 1
        y += 0.30


def build_base_saddles(k):
    """Curved saddle girders (box girders, JH-04) under each track rope, BEHIND the docked
    carriage: the rope runs level through the dock (C3), leaves it at BASE_SADDLE z over the
    girder's arc and drops into its counterweight shaft. A galvanised saddle portal (two
    columns + beam) carries the girders over the cross deck."""
    za, R, phi_deg = BASE_SADDLE
    cz, cy = za, TRACK_Y - R
    rt, rb = R - TRACK_ROPE_R, R - TRACK_ROPE_R - 0.90
    pe = math.radians(phi_deg)
    n = 10
    angs = [math.pi / 2.0 - (math.pi / 2.0 - pe) * j / n for j in range(n + 1)]
    for side in (-1, 1):
        tag = "L" if side < 0 else "R"
        xl = side * TRACK_X
        k.loft(f"sgirder_{tag}", girder_sections(xl, cz, cy, rt, rb, angs), "galv")
        ze, ye = cz + R * math.cos(pe), cy + R * math.sin(pe)
        t = (ye - 12.50) / math.cos(pe)
        pts = [(xl, TRACK_Y, STUB_Z)] + [(xl, cy + R * math.sin(a), cz + R * math.cos(a))
                                         for a in angs]
        pts.append((xl, 12.50, ze + t * math.sin(pe)))
        k.tube(f"rope_track_{tag}", _dedupe(pts), TRACK_ROPE_R, "rope",
               segs=TILED_TRACK_ROPE_SEGS if TILED else TRACK_ROPE_SEGS["base"])
    zp0, zp1 = 0.35, 0.85
    yb = min(cy + math.sqrt(rb ** 2 - (z - cz) ** 2) for z in (zp0, zp1))
    k.box("sportal_beam", -6.85, 6.85, yb - 0.50, yb - 0.02, zp0, zp1, "galv")
    for sx, tag in ((-1, "L"), (1, "R")):
        x0, x1 = sorted((sx * 6.35, sx * 6.85))
        k.box(f"sportal_col{tag}", x0, x1, DECK_Y, yb - 0.50, zp0, zp1, "galv", drop=("-Y",))


def girder_sections(xl, cz, cy, rt, rb, angs):
    """Box-girder sections under a rope arc (center (cz, cy) in z-y, top / bottom radius rt /
    rb), preceded by a level lead-in section at SADDLE_Z0."""
    def sec(zt, yt, zb, yb):
        return [(xl - GIRDER_HW, yb, zb), (xl + GIRDER_HW, yb, zb),
                (xl + GIRDER_HW, yt, zt), (xl - GIRDER_HW, yt, zt)]
    secs = [sec(SADDLE_Z0, cy + rt, SADDLE_Z0, cy + rb)]
    for a in angs:
        secs.append(sec(cz + rt * math.cos(a), cy + rt * math.sin(a),
                        cz + rb * math.cos(a), cy + rb * math.sin(a)))
    return secs


def build_inner_doors(k):
    """The open end exposes the hall, so the doors at the boarding level read from the line
    side (verdict 10): the double door from the annex (the side building) in a glazed inner
    wall - galvanised storefront frame, glazed leaves, opaque lower panel (JH-04 left) - and
    the stair door's inner leaf. The operator door is on the operator cabin (build_base)."""
    xi = HALL_X - 0.13                          # inner face of the +X glazing (11.87)
    d0, d1 = ANNEX_DOOR_Z
    yh = DECK_Y + 2.80
    k.box("sfront_jamb0", xi - 0.20, xi, DECK_Y, yh + 0.15, d0 - 0.10, d0, "galv")
    k.box("sfront_jamb1", xi - 0.20, xi, DECK_Y, yh + 0.15, d1, d1 + 0.10, "galv")
    k.box("sfront_head", xi - 0.20, xi, yh, yh + 0.15, d0, d1, "galv")
    zm = (d0 + d1) / 2.0
    k.box("sfront_mid", xi - 0.16, xi, DECK_Y, yh, zm - 0.03, zm + 0.03, "galv")
    for n, (za, zb) in enumerate(((d0 + 0.02, zm - 0.04), (zm + 0.04, d1 - 0.02))):
        k.box(f"door_annex_leaf{n}", xi - 0.10, xi - 0.04, DECK_Y + 0.02, yh - 0.02, za, zb,
              "galv")
        xl = xi - 0.105
        k.quad(f"doorlite_annex{n}", [(xl, DECK_Y + 0.45, za + 0.10), (xl, DECK_Y + 0.45, zb - 0.10),
                                      (xl, yh - 0.14, zb - 0.10), (xl, yh - 0.14, za + 0.10)],
               "glass", (-1, 0, 0))
        k.cyl(f"door_annex_bar{n}", 0.022, (xi - 0.13, DECK_Y + 1.05, za + 0.15),
              (xi - 0.13, DECK_Y + 1.05, zb - 0.15), "galv", segs=6)
    k.box("spandrel_annex", xi - 0.05, xi, DECK_Y, DECK_Y + 1.00, -9.75, d0 - 0.10, "steel")
    if not (FINE_DETAIL and DETAIL1):   # fine-detail step 3c: a vanilla short-range wall light row (FD_ANNEX_LIGHT)
        k.box("lamp_annexdoor", xi - 0.30, xi, yh + 0.30, yh + 0.48, zm - 0.40, zm + 0.40, "lamp")
    xo = -(HALL_X - 0.13)
    s0, s1 = STAIR_DOOR_Z
    k.box("sfrontW_jamb0", xo, xo + 0.20, DECK_Y, DECK_Y + 2.60, s0, s0 + 0.10, "galv")
    k.box("sfrontW_jamb1", xo, xo + 0.20, DECK_Y, DECK_Y + 2.60, s1 - 0.10, s1, "galv")
    k.box("sfrontW_head", xo, xo + 0.20, DECK_Y + 2.45, DECK_Y + 2.60, s0 + 0.10, s1 - 0.10,
          "galv")
    k.box("door_stairin_leaf", xo + 0.04, xo + 0.10, DECK_Y + 0.02, DECK_Y + 2.43, s0 + 0.12,
          s1 - 0.12, "galv")
    xl = xo + 0.105
    k.quad("doorlite_stair", [(xl, DECK_Y + 0.45, s0 + 0.25), (xl, DECK_Y + 0.45, s1 - 0.25),
                              (xl, DECK_Y + 2.30, s1 - 0.25), (xl, DECK_Y + 2.30, s0 + 0.25)],
           "glass", (1, 0, 0))
    k.cyl("door_stairin_bar", 0.022, (xo + 0.13, DECK_Y + 1.05, s0 + 0.30),
          (xo + 0.13, DECK_Y + 1.05, s1 - 0.30), "galv", segs=6)


def stair_flight(k, tag, x0, x1, y_top, z_top, n_risers, going, direction, mat_tread="galv",
                 slab=False):
    """Treads + 2 stringers of one flight descending from (y_top, z_top) along `direction`
    (+1 = toward +Z); riser height = y drop / n. Returns (y_bottom, z_bottom). `slab` adds a
    thin sloped plate under the treads (`stairslab_<tag>`, kept at LOD2: the treads go at
    LOD1, and without it the flight reads as two hairlines at distance - revision 1, top).
    Revision 1c: over a slab the tread bottoms are hidden (slab below, stringers either side), so
    they are dropped (-2 tris a tread; the top's 14,000-tri cap with the -X stair)."""
    rise = 0.18
    y_bot = y_top - rise * n_risers
    z_bot = z_top + direction * going * (n_risers - 1)
    if slab:
        k.member(f"stairslab_{tag}",
                 ((x0 + x1) / 2.0, y_top - rise - 0.20, z_top + direction * going * 0.5),
                 ((x0 + x1) / 2.0, y_top - rise * (n_risers - 2) - 0.20,
                  z_top + direction * going * (n_risers - 2.5)),
                 (x1 - x0) - 0.20, 0.06, "steel", up=(0, 1, 0))
    for kk in range(1, n_risers):
        y = y_top - rise * kk
        za, zb = z_top + direction * going * (kk - 1), z_top + direction * going * kk
        k.box(f"tread_{tag}_{kk}", x0 + 0.08, x1 - 0.08, y - 0.05, y, min(za, zb), max(za, zb),
              mat_tread, drop=("-Y",) if slab else ())
    # revision 2 (C19, Z6): the stringers sit STRINGER_INSET inside the flight's edges - their outer faces lay in
    # the landings' side faces where the top ends run into them (69-76 cm2 each)
    for s, xs in (("a", x0 + 0.05 + STRINGER_INSET), ("b", x1 - 0.05 - STRINGER_INSET)):
        k.member(f"stairstr_{tag}{s}", (xs, y_top - 0.15, z_top), (xs, y_bot + 0.20, z_bot),
                 0.10, 0.30, "steel", up=(0, 1, 0))
    return y_bot, z_bot


def build_base_stair(k):
    xa0, xa1 = -16.40, -14.60          # upper flight
    xb0, xb1 = -14.20, -12.40          # lower flight
    k.box("landing_top", -16.40, -12.10, DECK_Y - 0.30, DECK_Y, -2.40, 0.20, "steel")
    yb, zb = stair_flight(k, "u", xa0, xa1, DECK_Y, 0.20, 17, 0.28, +1)
    k.box("landing_mid", -16.40, -12.40, yb - 0.30, yb, zb, zb + 2.00, "steel")
    y0, z0 = stair_flight(k, "l", xb0, xb1, yb, zb, 18, 0.28, -1)
    k.box("stairfoot_0", xb0 - 0.10, xb1 + 0.10, 0.0, STAIRFOOT_H, z0 - 0.70, z0 + 0.30, "concrete",
          open_bottom=True)
    for n, (x, z) in enumerate(((-16.30, -2.30), (-16.30, 0.10), (-16.30, zb + 1.90),
                                (-12.50, zb + 1.90))):
        top = (DECK_Y - 0.30) if z < 1.0 else (yb - 0.30)
        k.cyl(f"stairpost_{n}", 0.08, (x, 0.0, z), (x, top, z), "steel", segs=8, caps="none")
    k.handrail("stairuo", [(xa0 + 0.05, DECK_Y, 0.20), (xa0 + 0.05, yb, zb)], "steel")
    k.handrail("stairui", [(xa1 - 0.05, DECK_Y, 0.20), (xa1 - 0.05, yb, zb)], "steel")
    k.handrail("stairlo", [(xb1 - 0.05, yb, zb), (xb1 - 0.05, 0.15, z0)], "steel")
    k.handrail("stairli", [(xb0 + 0.05, yb, zb), (xb0 + 0.05, 0.15, z0)], "steel")
    k.handrail("landtop", [(-12.15, DECK_Y, -2.35), (-16.35, DECK_Y, -2.35),
                           (-16.35, DECK_Y, 0.05)], "steel")   # ends short of stairuo's post
    k.handrail("landmid", [(-16.35, yb, zb + 0.10), (-16.35, yb, zb + 1.95),
                           (-12.45, yb, zb + 1.95), (-12.45, yb, zb + 0.10)], "steel")


# --------------------------------------------------------------------------------------
# TOP station
# --------------------------------------------------------------------------------------

def strut_x(x_bot, y_bot, x_top, y_top, y):
    return x_bot + (x_top - x_bot) * (y - y_bot) / (y_top - y_bot)


def top_footings():
    """(name, x center, z center, half size) of every open-bottom footing pad (0 -> 0.80):
    the face portal, the rear portal (REAR_Z) and the deck legs under the outer decks."""
    out = []
    for sx, tag in ((-1, "L"), (1, "R")):
        out.append((f"footing_{tag}", sx * 10.60, -9.60, 0.75))
        out.append((f"footing_r{tag}", sx * 9.60, REAR_Z, 0.75))
        for i, zc in enumerate((-7.00, -4.40)):
            out.append((f"footing_l{tag}{i}", sx * 10.00, zc, 0.45))
    return out


def ext_tangent(c1, r1, c2, r2):
    """(z, y) circles. The rope runs CLOCKWISE over the top of circle 1 (the saddle arc) and
    leaves on the external tangent onto circle 2 (the anchor drum), which it wraps clockwise
    too (over its back). Returns phi: the leave angle on circle 1 = the arrival angle on
    circle 2 (rad, from +z toward +y). Bisection; f(0) > 0 > f(pi/2) is asserted."""
    def f(phi):
        t1 = (c1[0] + r1 * math.cos(phi), c1[1] + r1 * math.sin(phi))
        d = (math.sin(phi), -math.cos(phi))
        v = (c2[0] - t1[0], c2[1] - t1[1])
        return d[0] * v[1] - d[1] * v[0] + r2
    lo, hi = 0.0, math.pi / 2.0
    if not (f(lo) > 0.0 > f(hi)):
        raise ValueError("ext_tangent: no tangent between the saddle and the drum")
    for _ in range(60):
        mid = (lo + hi) / 2.0
        if f(mid) > 0.0:
            lo = mid
        else:
            hi = mid
    return (lo + hi) / 2.0


def build_top(k, opts):
    # -- concrete plinth (cast in place, JH-08 / JH-14), open bottom; the anchor drums sit on
    # its side faces; the front stays behind the cabin envelope (skeptic MC2/F1) ------------
    if PLINTH_TOP:
        # revision 3 (plinth mode): the pier continues below grade onto the foundation block (its bottom ring on the
        # block's top at PLINTH seat; build_top_foundation): nothing is left on the y 0 line
        k.box("plinth", -TOP_PLX, TOP_PLX, PLINTH["seat"], DECK_Y - 0.60, -0.50, 11.40, "concrete",
              drop=("+Y", "-Y"))
    else:
        k.box("plinth", -TOP_PLX, TOP_PLX, 0.0, DECK_Y - 0.60, -0.50, 11.40, "concrete",
              open_bottom=True, drop=("+Y",))
    k.box("plinthcap", -TOP_CAPX, TOP_CAPX, DECK_Y - 0.60, DECK_Y, -0.70, 11.60, "concrete")
    for nm, xc, zc, h in top_footings():
        if not PLINTH_TOP:
            k.box(nm, xc - h, xc + h, 0.0, 0.80, zc - h, zc + h, "concrete", open_bottom=True)
        elif nm.startswith("footing_r"):
            # revision 3: the rear-portal caps stand on the foundation block (0.10 in from its line face)
            k.box(nm, xc - h, xc + h, PLINTH["seat"], 0.80, zc - h, zc + h, "concrete", drop=("-Y",))
        else:
            # revision 3: the face-portal and deck-leg caps are pile caps on the lattice bents' columns, closed underneath
            k.box(nm, xc - h, xc + h, PLINTH["cap"], 0.80, zc - h, zc + h, "concrete")

    # -- open steel frame, no roof: face + rear portals, head beams / girders, braces ------
    yh = (HEAD_Y0 + HEAD_Y1) / 2.0
    for sx, tag in ((-1, "L"), (1, "R")):
        # revision 2 (C19, Z4): 0.76 deep along z (front face -9.98, was -10.00): no deck beam or grating lip
        # shares its front plane any more
        k.member(f"strut_face_{tag}", (sx * 10.60, 0.80, -9.60), (sx * 7.60, yh, -9.60), 0.80,
                 0.76, "steel", up=(0, 0, 1))
        k.member(f"strut_rear_{tag}", (sx * 9.60, 0.80, REAR_Z), (sx * 7.60, yh, REAR_Z), 0.70,
                 0.70, "steel", up=(0, 0, 1))
        fy0, fy1 = 8.60, 16.20
        fa = (strut_x(sx * 10.60, 0.80, sx * 7.60, yh, fy0), fy0, -9.60)
        fb = (strut_x(sx * 10.60, 0.80, sx * 7.60, yh, fy1), fy1, -9.60)
        ra = (strut_x(sx * 9.60, 0.80, sx * 7.60, yh, fy0), fy0, REAR_Z)
        rb = (strut_x(sx * 9.60, 0.80, sx * 7.60, yh, fy1), fy1, REAR_Z)
        k.member(f"brace_{tag}a", fa, rb, 0.35, 0.35, "steel", up=(sx, 0, 0))
        k.member(f"brace_{tag}b", fb, ra, 0.35, 0.35, "steel", up=(sx, 0, 0))
    k.box("headbeam_face", -8.00, 8.00, HEAD_Y0, HEAD_Y1, -10.10, -9.10, "steel")
    k.box("headbeam_rear", -8.00, 8.00, HEAD_Y0, HEAD_Y1, REAR_Z - 0.50, REAR_Z + 0.50, "steel")
    # revision 2 (C19, Z1 - the "shimmer on the top part" Richard saw in Blender, skeptic MC1): the head girders
    # shared their top (18.30) and both end faces with the head beams - 4.36 m2 of same-facing coplanar steel
    # at y 17.3-18.3. They now stop HEADGIRDER_INSET under the beams' top and inside their outer faces.
    gi = HEADGIRDER_INSET
    for sx, tag in ((-1, "L"), (1, "R")):
        k.box(f"headgirder_{tag}", min(sx * 7.30, sx * 7.90), max(sx * 7.30, sx * 7.90),
              HEAD_Y0 + 0.20, HEAD_Y1 - gi, -10.10 + gi, REAR_Z + 0.50 - gi, "steel")

    # -- open grating deck on braced legs over the drop (JH-14 / JH-16) ---------------------
    k.grating("bayc", -3.20, 3.20, -10.00, -0.70, DECK_Y)
    for sx, tag in ((-1, "L"), (1, "R")):
        k.grating(f"bay{tag}", min(sx * 7.80, sx * 10.20), max(sx * 7.80, sx * 10.20), -10.00,
                  -0.70, DECK_Y)
    # revision 2 (C19, Z4): the deck frame's T-joints no longer share faces - the longitudinal beams start at
    # z -9.97 (their ends lay in the front cross beams' front face), the cross beams stop 0.03 inside the
    # longitudinal beams' outer faces and are 0.02 shallower underneath (their soffits lay in the longitudinal
    # beams' where they cross: hidden from the walk set, which looks down, but seen from the valley below)
    n = 0
    for x in (-10.10, -7.90, -3.10, 3.10, 7.90, 10.10):
        k.box(f"deckbeam_{n}", x - 0.10, x + 0.10, DECK_Y - 0.36, DECK_Y - 0.06, -9.97, -0.70,
              "steel")
        n += 1
    for z in (-9.90, -5.00):
        for x0, x1 in ((-3.20, 3.20), (-10.20, -7.80), (7.80, 10.20)):
            k.box(f"deckbeam_{n}", x0 + 0.03, x1 - 0.03, DECK_Y - 0.34, DECK_Y - 0.06, z - 0.10, z + 0.10,
                  "steel")
            n += 1
    for sx, tag in ((-1, "L"), (1, "R")):
        k.member(f"deckstrut_{tag}", (sx * 2.00, DECK_Y - 0.36, -9.60), (sx * 2.00, 1.00, -0.45),
                 0.30, 0.30, "steel", up=(1, 0, 0))
        x0, x1 = sorted((sx * 9.85, sx * 10.15))
        for i, zc in enumerate((-7.00, -4.40)):
            k.box(f"leg_{tag}{i}", x0, x1, 0.80, DECK_Y - 0.36, zc - 0.15, zc + 0.15, "steel")
        xb = sx * 10.00
        k.member(f"legbrace_{tag}a", (xb, 1.30, -7.00), (xb, DECK_Y - 0.80, -4.40), 0.15, 0.15,
                 "steel", up=(sx, 0, 0))
        k.member(f"legbrace_{tag}b", (xb, DECK_Y - 0.80, -7.00), (xb, 1.30, -4.40), 0.15,
                 0.15 - XCROSS_SHALLOW, "steel", up=(sx, 0, 0))     # revision 2 (C19, Z5)
    bay_gates(k, GATE_EDGES, z0=GATE_Z0["top"], role="top")

    # -- railings (galvanised kit handrail) -----------------------------------------------------------
    xr = TOP_CAPX - 0.10
    rails = {"facec": [(-3.10, DECK_Y, -9.95), (3.10, DECK_Y, -9.95)]}
    if LEFT_STAIR:
        # revision 1c: the -X outer deck opens at its back corner onto the -X stair landing (as +X),
        # and its back edge onto the gangway
        rails["outerL"] = [(-7.90, DECK_Y, -9.95), (-10.15, DECK_Y, -9.95), (-10.15, DECK_Y, -1.75)]
    else:
        rails["outerL"] = [(-7.90, DECK_Y, -9.95), (-10.15, DECK_Y, -9.95), (-10.15, DECK_Y, -0.75),
                           (-7.85, DECK_Y, -0.75)]
    # +X: the ground-stair landing opens off the outer deck's back corner (z -1.65..-0.75)
    rails["outerR"] = [(7.90, DECK_Y, -9.95), (10.15, DECK_Y, -9.95), (10.15, DECK_Y, -1.75)]
    if not RAMPS:
        # nitpick B2: the outer deck's back edge opens onto the +X ramp's head grating (build_top_ramps)
        rails["outerRb"] = [(10.05, DECK_Y, -0.75), (7.85, DECK_Y, -0.75)]
    if LEFT_STAIR:
        # revision 1c: the cap railing opens on -X onto the gangway (z -0.70..0.50): it starts behind
        # the gangway's back-edge rail (build_top_left_stair) and runs on round the cap as before (the
        # +X spans keep their posts); `capf` guards the cap + gangway front over the -X berth.
        rails["cap"] = [(-xr, DECK_Y, GANG[3] + 0.07), (-xr, DECK_Y, 11.50), (xr, DECK_Y, 11.50),
                        (xr, DECK_Y, -0.65), (3.25, DECK_Y, -0.65)]
        rails["capf"] = [(-3.25, DECK_Y, -0.65), (-7.85, DECK_Y, -0.65)]
    else:
        rails["cap"] = [(-3.25, DECK_Y, -0.65), (-xr, DECK_Y, -0.65), (-xr, DECK_Y, 11.50),
                        (xr, DECK_Y, 11.50), (xr, DECK_Y, -0.65), (3.25, DECK_Y, -0.65)]
    for nm in ("facec", "outerL", "outerR", "outerRb", "cap", "capf"):
        if nm in rails:
            k.handrail(nm, rails[nm], "galv")
    # revision 2 (D3, JH-11 / JH-02: a ring in EVERY dock-rail panel; the 1e discs r 0.11 floated between the
    # rails, 3 + 8 of them): one 6-gon r 0.13 per panel resting on the knee rail - facec 3, outerL 5, outerR 5,
    # outerRb 1, capf 2 = 16 (the cap's rear / side rails face the plinth deck, not the berths)
    medallions(k, "medal_", [rails[nm] for nm in ("facec", "outerL", "outerR", "outerRb", "capf") if nm in rails],
               avoid=MEDAL_AVOID["top"])

    # -- operator shack at the dock (JH-11 "lift shack"; _Win (2,1) -> 7; kept at LOD2) -----
    hx0, hx1, hz0, hz1, hy1 = HUT
    k.box("hut", hx0, hx1, DECK_Y, hy1, hz0, hz1, "steel", drop=("-Y",))
    hr = k.box("hutroof", hx0 - 0.15, hx1 + 0.15, hy1, hy1 + 0.25, hz0 - 0.15, hz1 + 0.15, "roof")
    k.drip(hr)
    k.door("hut", 0.90, 2.10, (hx0, DECK_Y, 0.20), "-X", mat="steel")
    k.quad("hutwin_a", [(2.00, 7.30, hz0 - 0.02), (4.40, 7.30, hz0 - 0.02),
                        (4.40, 8.60, hz0 - 0.02), (2.00, 8.60, hz0 - 0.02)], "glass", (0, 0, -1))
    k.quad("hutwin_b", [(hx0 - 0.02, 7.30, 1.00), (hx0 - 0.02, 7.30, 2.30),
                        (hx0 - 0.02, 8.60, 2.30), (hx0 - 0.02, 8.60, 1.00)], "glass", (-1, 0, 0))
    # revision 2 (L3): the modeled 0.20 m cube lamp over the hut door (`lamp_hut`) is gone - a vanilla
    # WallLightShortRangeRandom01 row takes its place (SUBOBJ_ROWS)

    # -- deck dressing: diamond-plate boxes, binoculars ---------------------------------------
    for n, (x, z) in enumerate(((-2.20, 10.70), (-0.80, 10.70), (3.60, 10.70))):
        k.box(f"dplate_{n}", x - 0.60, x + 0.60, DECK_Y, DECK_Y + 1.00, z - 0.40, z + 0.40,
              "dplate", drop=("-Y",))
    for sx, tag in ((-1, "L"), (1, "R")):
        # revision 1b: x +-9.40 -> +-8.60. At +-9.40 the viewer head cut through strut_face_L/R
        # and its post stood wholly inside the strut (found by the C14 clash scan).
        x, z = sx * SCOPE_X, -9.40
        k.cyl(f"scope_post_{tag}", 0.05, (x, DECK_Y, z), (x, DECK_Y + 1.10, z), "galv", segs=6,
              caps="top")
        k.cyl(f"scope_head_{tag}", 0.09, (x, DECK_Y + 1.18, z + 0.18), (x, DECK_Y + 1.28, z - 0.18),
              "galv", segs=8)
    # revision 2 (L1, Richard's eleventh look "lights on top of posts are looking a little square"): the four
    # modeled 3 m posts with 0.30 m cube heads are gone; vanilla LightpoleParkRandom01 rows stand in their
    # place (SUBOBJ_ROWS, ruling Q1)

    # -- track ropes: curved saddle girders + anchor drums on the plinth side faces ----------
    drums = build_top_saddles(k)

    # -- haul loop: deflection wheels + vertical bullwheel (default) or one horizontal return
    # bullwheel (--no-wheels) -----------------------------------------------------------------
    if opts.wheels:
        build_top_wheels(k)
    else:
        build_top_bullwheel(k)
    build_top_crane(k)

    # -- stair to the ground on +X: a landing off the back corner of the +X outer deck, one
    # straight flight descending toward the village (+Z) beside the plinth, so the +X anchor
    # drum stays in view (only the stair foot passes below its front edge) ------------------
    # nitpick B2 (RAMPS): the landing runs back to the ramp's head line (z 0.50, as the -X landing) and the stair goes
    k.box("landing_top", 10.20, 11.70, DECK_Y - 0.30, DECK_Y, -1.60, TOP_RAMP["z0"] if RAMPS else -0.70, "steel")
    if not RAMPS:
        y0, z0 = stair_flight(k, "s", 10.10, 11.70, DECK_Y, -0.70, 35, 0.25, +1, slab=True)
        k.box("stairfoot_0", 10.00, 11.80, 0.0, STAIRFOOT_H, z0 - 0.25, z0 + 0.60, "concrete",
              open_bottom=True)
    k.cyl("stairpost_0", 0.08, (11.60, PLINTH["seat"] if PLINTH_TOP else 0.0, -1.50), (11.60, DECK_Y - 0.30, -1.50),
          "steel", segs=8, caps="none")             # revision 3: on the foundation block
    if not RAMPS:
        k.handrail("stairo", [(10.25, DECK_Y, -1.65), (11.65, DECK_Y, -1.65), (11.65, DECK_Y, -0.70),
                              (11.65, 0.15, z0)], "galv")
        k.handrail("stairi", [(10.15, DECK_Y, -0.70), (10.15, 0.15, z0)], "galv")
    if LEFT_STAIR:
        build_top_left_stair(k)
    if RAMPS:
        build_top_ramps(k)
    if DETAIL1:
        build_top_detail(k)
    if PLINTH_TOP:
        build_top_foundation(k)                     # revision 3: below grade only (appended: the parts above keep their order)
    return {"wheels": bool(opts.wheels), "drums": drums}


def build_top_foundation(k):
    """Revision 3 (PLINTH_TOP; Richard's twenty-seventh look T-3; photo 01: lattice legs on concrete footings in front of the
    concrete housing that carries the counterweight wheel; photo 02: the wheel on its concrete wall). Everything is BELOW
    grade (max y PLINTH seat / cap), so on a flat summit the terrain hides it and on a cliff edge it reaches down the face:
      - the FOUNDATION BLOCK (`found_block`): the pier's housing widened under the anchor-drum bays and the ramps out to
        the lot's sides, x +-FOUND_BLOCK x, z FOUND_BLOCK z0..z1, from the slab up to PLINTH seat - the pier (`plinth`),
        both pads, the eight ramp columns, both stair posts and the two rear-portal caps stand on its top; bottom face
        dropped (it sits on the slab);
      - the FOOTING SLAB (`found_slab`) under it, PLINTH slab_t thick to min y -PLINTH depth, stepping out to the frozen
        bound (x +-11.80, the back z 11.60) and 0.30 in front of the block's line face: closed;
      - two LATTICE BENTS (photo 01), one per side in the plane x = +-LAT x under the face-portal cap (z -9.60) and the two
        deck-leg caps (z -7.00 / -4.40): square steel columns (`latcol_`) from inside the pile caps down into concrete pad
        footings (`latpad_`, closed), girts (`latgirt_`) at LAT girt_y between the columns and as ties into the block's line
        face, an X brace (`latbrace_`) in each panel (two bays x two tiers; the second member of every X XCROSS_SHALLOW
        shallower, as the deck legs' braces)."""
    D, seat = PLINTH["depth"], PLINTH["seat"]
    ys = round(-D + PLINTH["slab_t"], 6)                    # -11.40: the slab's / the pad footings' top
    fx, fz0, fz1 = FOUND_SLAB["x"], FOUND_SLAB["z0"], FOUND_SLAB["z1"]
    bx, bz0, bz1 = FOUND_BLOCK["x"], FOUND_BLOCK["z0"], FOUND_BLOCK["z1"]
    k.box("found_slab", -fx, fx, -D, ys, fz0, fz1, "concrete")
    k.box("found_block", -bx, bx, ys, seat, bz0, bz1, "concrete", drop=("-Y",))
    L = LAT
    yt, ym = L["girt_y"]
    zs = L["z"]
    for sx, tag in ((-1, "L"), (1, "R")):
        x = sx * L["x"]
        for i, z in enumerate(zs):
            k.box(f"latpad_{tag}{i}", x - L["pad"], x + L["pad"], -D, ys, z - L["pad"], z + L["pad"], "concrete")
            k.member(f"latcol_{tag}{i}", (x, PLINTH["cap"] + L["into_cap"], z), (x, ys - L["into"], z), L["col"],
                     L["col"], "steel", up=(0, 0, 1))
        g_in = L["col"] / 2.0 - 0.05                      # a girt runs 0.05 into each column (no shared end rings)
        for j, y in enumerate((yt, ym)):
            for i in range(len(zs) - 1):
                k.member(f"latgirt_{tag}{i}{'ab'[j]}", (x, y, zs[i] + g_in), (x, y, zs[i + 1] - g_in), L["girt"], L["girt"],
                         "steel", up=(0, 1, 0))
            k.member(f"latgirt_{tag}t{'ab'[j]}", (x, y, zs[-1] + g_in), (x, y, bz0 + 0.05), L["girt"], L["girt"], "steel",
                     up=(0, 1, 0))
        b = L["brace"]
        for i in range(len(zs) - 1):
            for j, (y0, y1) in enumerate(((yt, ym), (ym, ys))):
                # (up +X on both sides: the two bents are translation copies, so they share UV islands: R3_SHARE)
                k.member(f"latbrace_{tag}{i}{'ab'[j]}0", (x, y0, zs[i]), (x, y1, zs[i + 1]), b, b, "steel",
                         up=(1, 0, 0))
                k.member(f"latbrace_{tag}{i}{'ab'[j]}1", (x, y1, zs[i]), (x, y0, zs[i + 1]), b, b - XCROSS_SHALLOW,
                         "steel", up=(1, 0, 0))


def left_stair_plan():
    """Levels of the -X ground stair (prefab): flight 1 from the top landing's back edge (z_head) to
    the mid landing (y_mid, z_mid0..z_mid1), flight 2 on to the ground (y_foot ~ 0, z_foot)."""
    r = 0.18
    z_head = LS_LANDING[3]
    y_mid = DECK_Y - r * LS_RISERS[0]
    z_mid0 = z_head + LS_GOING * (LS_RISERS[0] - 1)
    z_mid1 = z_mid0 + LS_MID
    return {"z_head": z_head, "y_mid": y_mid, "z_mid0": z_mid0, "z_mid1": z_mid1,
            "y_foot": y_mid - r * LS_RISERS[1], "z_foot": z_mid1 + LS_GOING * (LS_RISERS[1] - 1)}


def build_top_left_stair(k):
    """Revision 1c (C15; Richard's fourth look): the -X ground stair - top landing beside the -X outer
    deck's back corner, two flights descending toward +Z (the +X stair's tread, stringer and slab kit)
    with a mid landing on posts, a concrete foot pad - and the grating gangway on two deck beams from
    the plinth cap to the landing. Railings as on the base stair: one straight run per flight side
    and per mid-landing side, the top landing's front + outer edge, the gangway's back edge; each
    run stops 0.10 short of the next one's end post."""
    p = left_stair_plan()
    lx0, lx1, lz0, lz1 = LS_LANDING
    fx0, fx1 = LS_FLIGHT_X
    gx0, gx1, gz0, gz1 = GANG
    k.box("landing_topW", lx0, lx1, DECK_Y - 0.30, DECK_Y, lz0, lz1, "steel")
    k.grating("gangL", gx0, gx1, gz0, gz1, DECK_Y)
    for i, z in enumerate((gz0 + 0.10, gz1 - 0.10)):
        k.box(f"deckbeam_{12 + i}", gx0 + 0.02, gx1, DECK_Y - 0.36, DECK_Y - 0.06, z - 0.10, z + 0.10, "steel")
    if RAMPS:
        # nitpick B2: the landing and the gangway are the -X ramp's head (build_top_ramps; its outer rail takes the
        # landing's): no flights, no foot pad; the landing keeps its post, the gangway its back rail east of the ramp
        k.cyl("stairpost_1", 0.08, (-11.60, PLINTH["seat"] if PLINTH_TOP else 0.0, -1.50), (-11.60, DECK_Y - 0.30, -1.50),
              "steel", segs=8, caps="none")         # revision 3: on the foundation block
        k.handrail("gangb", [(-(TOP_RAMP["x"][0] + NB2_RAIL_IN) + 0.10, DECK_Y, gz1 - 0.03), (gx1, DECK_Y, gz1 - 0.03)],
                   "galv")
        return
    yb, zb = stair_flight(k, "wu", fx0, fx1, DECK_Y, lz1, LS_RISERS[0], LS_GOING, +1, slab=True)
    k.box("landing_midW", fx0, fx1, yb - 0.30, yb, zb, zb + LS_MID, "steel")
    y0, z0 = stair_flight(k, "wl", fx0, fx1, yb, zb + LS_MID, LS_RISERS[1], LS_GOING, +1, slab=True)
    assert abs(yb - p["y_mid"]) < 1e-9 and abs(z0 - p["z_foot"]) < 1e-9 and abs(y0) < 1e-9
    k.box("stairfoot_1", fx0 - 0.10, fx1 + 0.10, 0.0, STAIRFOOT_H, z0 - 0.25, z0 + 0.60, "concrete",
          open_bottom=True)
    zp = zb + LS_MID - 0.10
    for n, (x, z, top) in enumerate(((-11.60, -1.50, DECK_Y - 0.30), (-11.60, zp, yb - 0.30),
                                     (fx1 - 0.10, zp, yb - 0.30)), start=1):
        k.cyl(f"stairpost_{n}", 0.08, (x, 0.0, z), (x, top, z), "steel", segs=8, caps="none")
    k.handrail("landtopW", [(-10.25, DECK_Y, lz0 - 0.05), (-11.65, DECK_Y, lz0 - 0.05),
                            (-11.65, DECK_Y, lz1 - 0.10)], "galv")
    k.handrail("gangb", [(fx1 + 0.05, DECK_Y, gz1 - 0.03), (gx1, DECK_Y, gz1 - 0.03)], "galv")
    for s, xs in (("o", fx0 + 0.05), ("i", fx1 - 0.05)):
        k.handrail(f"stairwu{s}", [(xs, DECK_Y, lz1), (xs, yb, zb)], "galv")
        k.handrail(f"landwm{s}", [(xs, yb, zb + 0.10), (xs, yb, zb + LS_MID - 0.10)], "galv")
        k.handrail(f"stairwl{s}", [(xs, yb, zb + LS_MID), (xs, 0.15, z0)], "galv")


TOP_PAD_X1 = 11.80                  # the pads' outer faces on the frozen bound x +-11.80 (the end stairs' foot pads')


def build_top_ramps(k):
    """Nitpick B2 (RAMPS; T-2 "slightly inclined LONG RAMPS off the ends instead of stairs"; photo 05: a long grated
    ramp deck with rails both sides down to the stone summit plaza). Mirror images about x 0 (the -X one with LEFT_STAIR,
    as the stair it replaces): from the head line z 0.50 at the deck (+X: a head grating `grate_headR` behind the
    outer deck and the landing run back to it; -X: the gangway + landing_topW of revision 1c) 1:8 down along each
    side, beside the anchor drums (x 6.05..6.82), to a concrete pad `pad_<s>` at the back lot edge (z 10.10..11.60,
    top 5.10, outer face on the frozen bound x 11.80): the terrain is molded up to the pads (no level; C15b). The deck
    is grating (relief) NB2_RAMP_T deep, its head end on the head beam / landing, a mid bent of two columns, its foot
    on the pad; rails both sides (straight kit runs), round the head landing and along the pad's sides (its back edge,
    the exit onto the terrain, open)."""
    P = top_ramp_plan()
    g = RAMP["grade"]
    x0, x1 = TOP_RAMP["x"]
    z0, (p0, p1) = TOP_RAMP["z0"], TOP_RAMP["pad"]
    yp = P["y_pad"]
    t, ri, e = NB2_RAMP_T, NB2_RAIL_IN, 0.10
    dbeam = (DECK_Y - 0.36, DECK_Y - 0.06)          # the bays' deck-beam depth
    c = NB2_COL / 2.0
    for s, tag in ((1, "R"), (-1, "L")):
        if s < 0 and not LEFT_STAIR:
            continue
        xa, xb = sorted((s * x0, s * x1))
        xi, xo = s * (x0 + 0.15), s * (x1 - 0.15)          # column lines (inner, outer)
        # +X: the head grating behind the outer deck on a deck beam (0.02 short of the head line and of the landing -
        # C19: their faces lay in the landing's); -X: the gangway's own deckbeam_13 carries the inner side
        if s > 0:
            hx0, hx1, hz0, hz1 = TOP_RAMP["head_grate"]
            k.grating("headR", hx0, hx1, hz0, hz1, DECK_Y)
            k.box("deckbeam_rR", xa, hx1 - 0.02, dbeam[0], dbeam[1], z0 - 0.22, z0 - 0.02, "steel")
        _ramp_deck(k, f"ramp_{tag}", xa, xb, p0, yp, z0, DECK_Y, t)
        if PLINTH_TOP:
            # revision 3: the pad continues below grade onto the foundation block; it overhangs the block 0.10 on its
            # outer and back faces (the frozen bound), so it keeps a closed underside
            k.box(f"pad_{tag}", min(s * x0, s * TOP_PAD_X1), max(s * x0, s * TOP_PAD_X1), PLINTH["seat"], yp, p0, p1,
                  "concrete")
        else:
            k.box(f"pad_{tag}", min(s * x0, s * TOP_PAD_X1), max(s * x0, s * TOP_PAD_X1), 0.0, yp, p0, p1, "concrete",
                  open_bottom=True)
        zm = (z0 + p0) / 2.0
        ym = DECK_Y - (zm - z0) * g - t                     # the deck soffit at the mid bent
        for j, (x, z, ytop) in enumerate(((xi, z0 - 0.12, dbeam[0]), (xo, z0 - 0.12, DECK_Y - 0.30), (xi, zm, ym),
                                          (xo, zm, ym))):
            if PLINTH_TOP:                                  # revision 3: on the foundation block
                k.box(f"rampcol_{tag}{j}", x - c, x + c, PLINTH["seat"], ytop, z - c, z + c, "steel", drop=("-Y",))
            else:
                k.box(f"rampcol_{tag}{j}", x - c, x + c, 0.0, ytop, z - c, z + c, "steel", open_bottom=True)
        xri, xro = s * (x0 + ri), s * (x1 - ri)
        y1 = DECK_Y - e * g                                  # the deck 0.10 m down from its head
        runs = [(f"head{tag}f", (s * 10.25, DECK_Y, -1.65), (xro, DECK_Y, -1.65)),
                (f"head{tag}o", (xro, DECK_Y, -1.65 + e), (xro, DECK_Y, z0)),
                (f"ramp{tag}o", (xro, y1, z0 + e), (xro, yp, p0)),
                (f"pad{tag}o", (xro, yp, p0 + e), (xro, yp, p1 - 0.05)),
                (f"ramp{tag}i", (xri, DECK_Y, z0) if s < 0 else (xri, y1, z0 + e), (xri, yp, p0)),
                (f"pad{tag}i", (xri, yp, p0 + e), (xri, yp, p1 - 0.05))]
        if s > 0:
            runs.insert(4, ("headRi", (xri, DECK_Y, TOP_RAMP["head_grate"][2] + 0.05), (xri, DECK_Y, z0)))
        _rails(k, runs, "galv")


def build_top_saddles(k):
    """Track ropes (JH-08 / JH-12 / JH-14, P2P-01): level through the dock (C3), then over a
    curved saddle box-girder behind the docked carriage, then down on the external tangent to
    an anchor drum on the plinth side face, wrapped DRUM_WRAP degrees with a small helix.
    Each girder sits on two posts on the plinth cap and hangs from the rear head beam."""
    za, R = TOP_SADDLE
    c1 = (za, TRACK_Y - R)
    rt, rb = R - TRACK_ROPE_R, R - TRACK_ROPE_R - 1.00
    phi = ext_tangent(c1, R, DRUM_C, DRUM_RR)
    n = 9
    angs = [math.pi / 2.0 - (math.pi / 2.0 - phi) * j / n for j in range(n + 1)]
    out = []
    for side in (-1, 1):
        tag = "L" if side < 0 else "R"
        xl = side * TRACK_X
        k.loft(f"sgirder_{tag}", girder_sections(xl, c1[0], c1[1], rt, rb, angs), "galv")
        # posts under the girder (on the cap) at SGPOST0_DEG (70 deg until detail pass 1, N1) and
        # just inside the leave angle
        for j, a in enumerate((math.radians(SGPOST0_DEG), phi + math.radians(6.0))):
            zc, yb = c1[0] + rb * math.cos(a), c1[1] + rb * math.sin(a)
            k.box(f"sgpost_{tag}{j}", xl - 0.15, xl + 0.15, DECK_Y, yb + 0.05, zc - 0.15,
                  zc + 0.15, "galv", drop=("-Y",))
        for s, dx in (("a", -1.0), ("b", 1.0)):
            xh = xl + dx * (GIRDER_HW + 0.05)
            k.box(f"sghanger_{tag}{s}", xh - 0.03, xh + 0.03, c1[1] + rb + 0.20, HEAD_Y0,
                  SADDLE_Z0 + 0.02, SADDLE_Z0 + 0.12, "galv")
        # anchor drum (axle along X) on the plinth side face
        xd = side * DRUM_X
        cz, cy = DRUM_C
        k.sheave(f"drum{tag}", DRUM_RR + 0.15, 0.70, (xd, cy, cz), "X", segs=24, hub=False,
                 r_hub=DRUM_RR - 0.10, groove=0.15 + TRACK_ROPE_R,
                 rename=lambda nm, t=tag: f"drumrim_{t}")
        for j, (dy, dz) in enumerate(((1.0, 0.0), (0.0, 1.0))):
            L = DRUM_RR - 0.05
            # revision 2 (C19, Z5): the second spoke of the X 0.04 thinner (shared face planes at the cross)
            k.member(f"drumspoke_{tag}{j}", (xd, cy - dy * L, cz - dz * L),
                     (xd, cy + dy * L, cz + dz * L), 0.30, 0.14 - (XCROSS_SHALLOW if j else 0.0), "galv",
                     up=(1, 0, 0))
        k.cyl(f"drumhub_{tag}", 0.30, (side * TOP_PLX, cy, cz), (xd + side * 0.42, cy, cz),
              "galv", segs=12)
        # the rope
        x0, x1 = side * (DRUM_X - 0.12), side * (DRUM_X + 0.12)
        pts = [(xl, TRACK_Y, STUB_Z)]
        pts += [(xl, c1[1] + R * math.sin(a), c1[0] + R * math.cos(a)) for a in angs]
        nw = 14
        for i in range(nw + 1):
            f = i / nw
            a = phi - math.radians(DRUM_WRAP) * f
            pts.append((x0 + (x1 - x0) * f, cy + DRUM_RR * math.sin(a), cz + DRUM_RR * math.cos(a)))
        check_rope_clear(pts, side, f"rope_track_{tag}")
        k.tube(f"rope_track_{tag}", _dedupe(pts), TRACK_ROPE_R, "rope", segs=TRACK_ROPE_SEGS["top"])
        out.append({"centre": [xd, cy, cz], "rope_r": DRUM_RR, "axle": "x",
                    "leave_deg": round(math.degrees(phi), 2)})
    # tie beam under both girders at the first posts. Revision 2 (Z3 / T2): moved down under the 2.90 m lane
    # wheels (rim bottom DW_Y - DW_RR - 0.15 = 12.85; the tie keeps 0.15 under it) and 0.36 deep (posts 0.30):
    # its z faces lay in the posts' (C19)
    a = math.radians(SGPOST0_DEG)
    zc = c1[0] + rb * math.cos(a)
    y1 = DW_Y - DW_RR - 0.15 - SGTIE_CLEAR
    k.box("sgtie", -TRACK_X - 0.20, TRACK_X + 0.20, y1 - 0.50, y1, zc - 0.18, zc + 0.18, "galv")
    return out


def check_rope_clear(pts, side, name):
    """Design guard: the descending track rope keeps 5 cm from the plinth + cap (x TOP_CAPX,
    below the deck) and from the cap railing (tube r 0.028 at x TOP_CAPX - 0.10, up to the
    deck + 1.10)."""
    P = [Vector(p) for p in pts]
    for a, b in zip(P, P[1:]):
        for s in range(21):
            q = a + (b - a) * (s / 20.0)
            x, y, z = abs(q.x), q.y, q.z
            if not -0.70 <= z <= 11.60:
                continue
            if y <= DECK_Y + 0.05:
                need = TOP_CAPX
            elif y <= DECK_Y + 1.20:
                need = TOP_CAPX - 0.10 + 0.028
            else:
                continue
            if x < need + 0.05 + TRACK_ROPE_R:
                raise ValueError(f"{name}: rope at ({side * x:.2f}, {y:.2f}, {z:.2f}) clips the "
                                 f"plinth / cap / railing")


def build_top_wheels(k):
    """Haul loop, JH style - revision 2 (design T1-T3, skeptic MC9; JH-08 / JH-11 / JH-12 / JH-14): each strand
    runs level from its stub onto the TOP of a vertical lane wheel (y-z plane, x +-HAUL_X, 2.90 m, slot 2
    yellow), over it and down its back into the haul plane z = BULL_Z, down the outer side of a small foot
    sheave (x-y plane, above the hut roof, dark steel: JH-12's sheave battery), under it, and up the internal
    tangent onto ONE vertical 3.60 m yellow bullwheel (x-y plane, axle along z) and over its top: an "M"
    (haul_route_top, C20). The bullwheel hangs its lower 1.1 m below the maintenance platform (JH-12) on an
    A-frame (head box, tie), behind a frame-only guard on the platform rail (build_top_crane, MC6)."""
    for side in (-1, 1):
        tag = "L" if side < 0 else "R"
        xh = side * HAUL_X
        k.sheave(f"dw{tag}", DW_RR + 0.15, 0.24, (xh, DW_Y, DW_Z), "X", segs=20, hub=False,
                 r_hub=DW_RR - 0.05, groove=0.15 + HAUL_ROPE_R, mat="wheel",
                 rename=lambda nm, t=tag: f"dwrim_{t}")
        for s in range(6):
            a = 2.0 * math.pi * (s + 0.5) / 6.0
            r1 = DW_RR - 0.04                       # 0.01 into the closed bore face (r DW_RR - 0.05)
            k.member(f"dwspoke_{tag}{s}", (xh, DW_Y + 0.14 * math.sin(a), DW_Z + 0.14 * math.cos(a)),
                     (xh, DW_Y + r1 * math.sin(a), DW_Z + r1 * math.cos(a)),
                     0.08, 0.10, "wheel", up=(1, 0, 0))
        k.cyl(f"dwhub_{tag}", 0.16, (xh - side * 0.14, DW_Y, DW_Z), (xh + side * 0.14, DW_Y, DW_Z),
              "galv", segs=10)
        k.cyl(f"dwaxle_{tag}", 0.07, (side * 4.70, DW_Y, DW_Z), (xh - side * 0.14, DW_Y, DW_Z),
              "galv", segs=8, caps="none")
    k.box("dwbeam", -4.70, 4.70, DW_Y - 0.25, DW_Y + 0.25, DW_Z - 0.25, DW_Z + 0.25, "steel")
    for sx, tag in ((-1, "L"), (1, "R")):
        x0, x1 = sorted((sx * 0.65, sx * 0.95))
        k.box(f"dwpost_{tag}", x0, x1, DECK_Y, DW_Y - 0.25, DW_Z - 0.15, DW_Z + 0.15, "steel",
              drop=("-Y",))
    # the bullwheel (yellow rim + 5 broad spokes, galvanised hub: digest 7.1), axle and A-frame (behind it)
    c = (0.0, BULL_Y, BULL_Z)
    k.sheave("bull", BULL_RIM, 0.36, c, "Z", segs=BULL_SEGS, hub=False, r_hub=BULL_BORE,
             groove=BULL_RIM - (BULL_RR - HAUL_ROPE_R), mat="wheel", rename=lambda nm: "bullrim")
    for s in range(BULL_SPOKES):
        a = math.pi / 2.0 + 2.0 * math.pi * s / BULL_SPOKES      # one pointing up
        r0 = BULL_HUB_R - 0.04                  # inside the 16-gon hub's facets
        k.member(f"bullspoke_{s}", (r0 * math.cos(a), BULL_Y + r0 * math.sin(a), BULL_Z),
                 ((BULL_BORE + 0.02) * math.cos(a), BULL_Y + (BULL_BORE + 0.02) * math.sin(a), BULL_Z),
                 0.40, 0.10, "wheel", up=(0, 0, 1))
    k.cyl("bullhub", BULL_HUB_R, (0.0, BULL_Y, BULL_Z - 0.20), (0.0, BULL_Y, BULL_Z + 0.20), "galv",
          segs=16)
    k.cyl("bullaxle", 0.18, (0.0, BULL_Y, BULL_Z + 0.20), (0.0, BULL_Y, 2.00), "galv", segs=10,
          caps="none")
    xf = BULLFRAME_FOOT_X
    for sx, tag in ((-1, "L"), (1, "R")):
        k.member(f"bullframe_{tag}", (sx * xf, DECK_Y, 2.20), (sx * 0.35, BULL_Y, 2.20), 0.40,
                 0.40, "steel", up=(0, 0, 1))
    yt = 9.35
    xt = strut_x(xf, DECK_Y, 0.35, BULL_Y, yt)
    k.box("bullframe_t", -xt, xt, yt - 0.15, yt + 0.15, 2.05, 2.35, "steel")
    # head box 0.05 proud of the 0.40 legs on both z faces (C19, Z2: the 1e box shared the legs' face planes)
    k.box("bullframe_hd", -0.50, 0.50, BULL_Y - 0.40, BULL_Y + 0.40, 1.95, 2.45, "steel")
    # the two foot sheaves (dark steel, JH-12) on a beam whose front face carries their hub discs and which
    # runs 0.05 into the A-frame legs; 0.10 under the bullwheel rim, 0.65 above the hut roof
    for side in (-1, 1):
        tag = "L" if side < 0 else "R"
        k.sheave(f"fs{tag}", FS_RIM, 0.20, (side * FS_X, FS_Y, BULL_Z), "Z", segs=10, hub=True, r_hub=0.45,
                 groove=FS_RIM - (FS_RR - HAUL_ROPE_R), mat="steel",
                 rename=lambda nm, t=tag: f"fsrim_{t}" if nm.endswith("_rim") else f"fshub_{t}")
    k.box("fsbeam", -FS_X - 0.10, FS_X + 0.10, FS_Y - 0.20, FS_Y + 0.10, BULL_Z + 0.12, 2.05, "steel")
    # the loop
    k.tube("rope_haul", haul_route_top()["points"], HAUL_ROPE_R, "rope", segs=6)


def build_top_bullwheel(k):
    """--no-wheels: the haul loop closes round one horizontal return bullwheel behind the
    dock (rope radius = HAUL_X), as at the base."""
    cx, cy, cz = 0.0, HAUL_Y, TOP_BW_Z
    k.cyl("bwshaft", 0.25, (cx, DECK_Y, cz), (cx, cy - 0.35, cz), "galv", segs=12, caps="none")
    k.sheave("bw", BW_R, 0.40, (cx, cy, cz), "Y", segs=40, hub=False, r_hub=4.95,
             groove=BW_R - (HAUL_X - HAUL_ROPE_R), mat="wheel", rename=lambda n: "bwrim")
    for n in range(12):
        a = 2.0 * math.pi * n / 12.0
        k.member(f"bwspoke_{n}", (cx + 0.55 * math.cos(a), cy, cz + 0.55 * math.sin(a)),
                 (cx + 4.97 * math.cos(a), cy, cz + 4.97 * math.sin(a)), 0.12, 0.20, "wheel")
    k.cyl("bwhub", 0.60, (cx, cy - 0.35, cz), (cx, cy + 0.35, cz), "wheel", segs=16)
    semi = [(cx - HAUL_X * math.cos(f), HAUL_Y, cz + HAUL_X * math.sin(f))
            for f in (math.pi * i / 14.0 for i in range(15))]
    k.tube("rope_haul", _dedupe([(-HAUL_X, HAUL_Y, STUB_Z)] + semi + [(HAUL_X, HAUL_Y, STUB_Z)]),
           HAUL_ROPE_R, "rope", segs=6)


def build_top_crane(k):
    """Goalpost crane beam (two posts, one long beam overhanging both rope lanes, lifting lugs
    at the tips, anemometer mast; JH-11 / JH-12 / JH-14), the maintenance platform behind the
    bullwheel and its stair from the plinth deck."""
    z0, z1 = GOAL_Z
    zc = (z0 + z1) / 2.0
    # revision 2 (T6, JH-11 / 12 / 14: the beam spans about the lane gauge, ~1.4-2 m above the saddle crest):
    # 20.0 m at y 25.0-25.8 -> 12.4 m at GOAL_Y0..GOAL_Y1 18.00-18.70, the lugs over the TRACK lanes
    for sx, tag in ((-1, "L"), (1, "R")):
        x0, x1 = sorted((sx * 2.30, sx * 2.90))
        k.box(f"goalpost_{tag}", x0, x1, DECK_Y, GOAL_Y0, z0, z1, "steel", drop=("-Y",))
        k.member(f"goalknee_{tag}", (sx * 2.60, GOAL_Y0 - 1.60, zc), (sx * 4.40, GOAL_Y0 + 0.05, zc),
                 0.25, 0.25, "steel", up=(0, 0, 1))
        x0, x1 = sorted((sx * (TRACK_X - 0.125), sx * (TRACK_X + 0.125)))
        k.box(f"lug_{tag}", x0, x1, GOAL_Y0 - 0.45, GOAL_Y0, zc - 0.08, zc + 0.08, "galv")
    k.box("goalbeam", -GOAL_X, GOAL_X, GOAL_Y0, GOAL_Y1, z0, z1, "steel")
    # revision 2 (ruling Q3): the 8 m anemometer mast propping the old 27.00 bound is gone; a 1.80 m roof mast
    # (Snowbird SB-01 / 07 / 20 / 21: masts, panel antennas, a dish) on the beam carries the anemometer arm, a
    # dish facing down the line, a panel antenna and the red beacon lens (TT1) to MAST_TOP 20.70 = the new
    # bounds top. LOD1 keeps the mast, dish, panel and arm (only the 0.20 m lens goes: no LOD height pop over
    # 1 m); LOD2 keeps the mast (LOD2_KEEP).
    mast_top = MAST_TOP - BEACON_H if DETAIL1 else MAST_TOP
    k.cyl("mast", 0.06, (0.0, GOAL_Y1 - 0.02, zc), (0.0, mast_top, zc), "galv", segs=6, caps="top")
    k.box("mastarm", -0.45, 0.45, MAST_TOP - 0.55, MAST_TOP - 0.50, zc - 0.03, zc + 0.03, "galv")
    k.cone("antdish", (0.0, ANT_DISH_Y, zc - 0.05), (0.0, ANT_DISH_Y, zc - 0.05 - 0.16), 0.26, "trim_white")
    k.box("antpanel", 0.05, 0.15, ANT_PANEL_Y[0], ANT_PANEL_Y[1], zc - 0.08, zc + 0.08, "trim_white")
    # maintenance platform + stair (45 deg, 30 risers x 0.18)
    k.grating("svcplat", -4.60, 2.90, 2.50, 5.00, SVC_Y)
    for n, (x, z) in enumerate(((-4.50, 2.75), (2.75, 2.95), (-3.25, 4.90))):
        k.box(f"svcpost_{n}", x - 0.08, x + 0.08, DECK_Y, SVC_Y - 0.06, z - 0.08, z + 0.08,
              "steel", drop=("-Y",))
    k.handrail("svcA", [(-4.55, SVC_Y, 4.95), (-4.55, SVC_Y, 2.55), (2.85, SVC_Y, 2.55),
                        (2.85, SVC_Y, 4.95)], "galv")
    # revision 2 (T5 as amended by the skeptic's MC6: JH-12's guard in front of the wheel, but no opaque panel -
    # pipeline gratings are opaque and a 4 x 1.8 m one hid half the 3.6 m wheel from the village side): a
    # frame-only guard in the svcA rail plane - two posts, a top bar and a mid bar to 1.90 m over the platform
    gz = 2.55
    for sx, tag in ((-1, "L"), (1, "R")):
        x = sx * CWGUARD_X
        k.box(f"cwguard_post{tag}", x - 0.03, x + 0.03, SVC_Y, SVC_Y + 1.90, gz - 0.03, gz + 0.03, "galv",
              drop=("-Y",))
    for tag, y in (("top", SVC_Y + 1.87), ("mid", SVC_Y + 1.47)):
        k.box(f"cwguard_{tag}", -CWGUARD_X + 0.03, CWGUARD_X - 0.03, y - 0.025, y + 0.025, gz - 0.025,
              gz + 0.025, "galv")
    k.handrail("svcB", [(-3.35, SVC_Y, 4.95), (2.25, SVC_Y, 4.95)], "galv")
    yb, zb = stair_flight(k, "m", -4.60, -3.40, SVC_Y, 5.00, 30, 0.178, +1, slab=True)
    k.handrail("stairmo", [(-4.55, SVC_Y, 5.00), (-4.55, yb + 0.10, zb)], "galv")
    k.handrail("stairmi", [(-3.45, SVC_Y, 5.00), (-3.45, yb + 0.10, zb)], "galv")


# --------------------------------------------------------------------------------------
# Detail pass 1 (lifts): dressing, labels, vanilla sub-objects
# (design/detail-pass-1-lifts.md sections 5 / 6 as amended by its skeptic MC1-MC11; every
# placement below is box-tested: parts by C4 / C14 / C15, sub-objects by C16)
# --------------------------------------------------------------------------------------

BEACON_H = 0.20                     # TT1 lens on the anemometer mast (r 0.10)
# Labels painted in post_compose: part -> (cs2_liftdress.BOARDS kind, size override, target px/m).
# Long side <= 1.50 m: 400 px/m (design L1); the departure board / TICKETS are large graphics (L2:
# LED dot 0.02 m = 4 texels at 200 px/m, TICKETS cap 0.25 stroke 8.6 texels at 240 px/m). The
# LIFT OPS board is 0.30 tall (not the kind's 0.35): it sits on the operator cabin's 0.40 head band
# and keeps 0.06 above the window band (design TB4 "assert >= 0.05").
# Revision 2 (D4, Snowbird SB-07 "trail-sign boards hung on the deck rail", SB-04 / 19; ruling Q7: difficulty
# symbols + arrows only, facing the deck - skeptic S8: the design's +Z boards faced outward over the drop):
# three boards clamped on top of the rear cap rail's top tube, mid-panel, label face toward the deck (-Z),
# above the diamond-plate boxes (top 7.30). (x center, symbol, arrow)
TRAIL_BOARDS = ((-2.34, "green_circle", "left"), (0.00, "blue_square", "right"), (2.34, "black_diamond", "right"))
TRAIL_BOARD = {"w": 0.90, "h": 0.28, "t": 0.03, "label": (0.86, 0.24)}
LABEL_KINDS = {"label_dep": ("dep", None, 200.0), "label_tickets": ("tickets", None, 240.0),
               "label_ops": ("ops", (1.20, 0.30), 400.0), "label_stn": ("stn", None, 400.0),
               "label_tension": ("tension", None, 400.0),
               # revision 2 (D4): the trail boards - a symbol + arrow graphic, no text: L2 (200 px/m, the
               # design), painted by paint_trail_board (kind "trail:<symbol>:<arrow>")
               **{f"label_trail_{i}": (f"trail:{sym}:{arr}", TRAIL_BOARD["label"], 200.0)
                  for i, (_x, sym, arr) in enumerate(TRAIL_BOARDS)},
               # nitpick B (JH_DETAILS): LIFT TICKETS on the annex's +X face, ENTRANCE over the village doors - the
               # TICKETS kind (blue, white words) with their own words (LABEL_WORDS); L2 (> 1.5 m) at the departure
               # board's 200 px/m (cap 0.217, stroke 8.1-8.7 texels: T2 holds; the atlas cost of a 240 target was w1 -2 %)
               **{f"label_{nm}": ("tickets", (w, h), 200.0) for nm, (_c, _f, w, h) in JH_BOARDS.items()}}
# the words a label paints instead of its kind's (cs2_liftdress.paint_board words=)
LABEL_WORDS = {"label_lifttickets": ("LIFT TICKETS",), "label_entrance": ("ENTRANCE",)}
# Lift-board redraw (2026-10-12; build/lift-boards-redraw.md): ops / tickets / stn repaint in the real signage
# style (cs2_liftdress.paint_board: blue LIFT OPS / TICKETS with a white border, the station board red like the
# Park City reference); the LED departure board, the tension dial and the trail boards are unchanged. Texture
# only: the label islands, their weights and every UV are untouched.
# L2 labels (large graphics, design L2): their target may sit under the L1 400 px/m of a <= 1.5 m label
LABEL_L2 = ("label_dep", "label_tickets", "label_trail_", "label_lifttickets", "label_entrance")
LABEL_W0 = 16.0                     # starting density weight of a label (label_weights refines it)
LABEL_MIN_ATLAS = 2048              # L1 / L2 are asserted at shipping atlases only (tests bake 512)
# _Emissive masked by the painted marks (dials since revision 1; the LED board since detail pass 1)
EMISSIVE_MASKED = ("ctface_", "label_dep")


def _LD():
    """cs2_liftdress, imported lazily: it imports cs2_preview, which imports lift_tower (the module
    imports at the top explain why the geometry build keeps that chain optional)."""
    import cs2_liftdress
    return cs2_liftdress


def _place(parts, M):
    """Re-place cs2_kit objects by the matrix M (applied after their own loc / rot)."""
    from mathutils import Matrix
    for obj, _m in parts:
        loc, rot, _s = (M @ Matrix.LocRotScale(obj.location, obj.rotation_euler, obj.scale)).decompose()
        obj.location = loc
        obj.rotation_euler = rot.to_euler()
    return parts


def build_base_detail(k):
    """Detail pass 1, base (design 5.2 as amended; prefab meters). Every board's center is its LABEL
    face (cs2_liftdress.board), LABEL_PROUD + 0.02 in front of its wall."""
    LD = _LD()
    P3, face = LD.tram_point, LD.tram_facing
    xi = HALL_X - 0.13                  # inner face of the side glazing (11.87)
    # TB1 departure board on the +X inner glazing over the annex door: bottom 9.70 = 0.12 over
    # lamp_annexdoor (9.40..9.58; skeptic F / MC10 "above Y 9.65"), z centered on the door
    k.dress(LD.board("dep", P3(xi - 0.023, 10.10, sum(ANNEX_DOOR_Z) / 2.0), face("-X"), 2.00, 0.80,
                     mount="wall", label_mat="led"))
    # TB3 (MC10): the base already has two grade ticket windows (tickwin_f0/f1, _Win cell 8, sills
    # sill_f0/f1, x -10.60..-6.80); the design's NEW openings at x 9.60..11.60 cut stonepier_9 and
    # duplicated them. Frames (trim, slot 3) round the existing panes and TICKETS above them.
    for n, (x0, x1) in enumerate(((-10.60, -8.90), (-8.50, -6.80))):
        k.box(f"tickframe_f{n}_l", x0 - 0.06, x0, 1.20, 3.06, HALL_Z, HALL_Z + 0.05, "trim", drop=("-Z",))
        k.box(f"tickframe_f{n}_r", x1, x1 + 0.06, 1.20, 3.06, HALL_Z, HALL_Z + 0.05, "trim", drop=("-Z",))
        k.box(f"tickframe_f{n}_h", x0, x1, 3.00, 3.06, HALL_Z, HALL_Z + 0.05, "trim", drop=("-Z",))
    k.dress(LD.board("tickets", P3(-8.70, 3.35, HALL_Z + 0.023), face("+Z"), 1.80, 0.35, mount="wall"))
    if JH_DETAILS:
        # nitpick B (JH-04 "Lift Tickets"): trim frames round the annex's three grade ticket windows on its +X stone
        # face (tickwin_a0..2, 0.03 proud of it) as the front pair, LIFT TICKETS above the middle one, ENTRANCE over
        # the village doors (both painted by the TICKETS kind with LABEL_WORDS)
        for n, zc in enumerate(JH_TICKWIN_A):
            z0, z1 = zc - 0.80, zc + 0.80
            k.box(f"tickframe_a{n}_l", JH_ANNEX_X, JH_ANNEX_X + 0.05, 1.20, 3.06, z0 - 0.06, z0, "trim", drop=("-X",))
            k.box(f"tickframe_a{n}_r", JH_ANNEX_X, JH_ANNEX_X + 0.05, 1.20, 3.06, z1, z1 + 0.06, "trim", drop=("-X",))
            k.box(f"tickframe_a{n}_h", JH_ANNEX_X, JH_ANNEX_X + 0.05, 3.00, 3.06, z0, z1, "trim", drop=("-X",))
        for nm, (c, fc, w, h) in JH_BOARDS.items():
            k.dress(LD.board(nm, P3(*c), face(fc), w, h, mount="wall"))
    # TB4 LIFT OPS on the operator cabin's -Z head band (opcabtop 8.70..9.10): 8.76..9.06
    k.dress(LD.board("ops", P3(0.0, 8.91, -3.20 - 0.023), face("-Z"), 1.20, 0.30, mount="wall"))
    # TB5 EXIT light on the -X inner glazing over the stair-in door (head 8.75..8.90)
    s0, s1 = STAIR_DOOR_Z
    zc = (s0 + s1) / 2.0
    k.box("lamp_exit", -xi, -xi + 0.06, 9.00, 9.15, zc - 0.20, zc + 0.20, "lamp_green")
    # TB6 extinguishers on hall columns (the side walls are glass): col (12, -5) -X face, col (-12, 0)
    # +X face beside the stair-in door
    # TB7 hose reel on the drive house's line-side face (the design's spot X 11.70 / Z -4.00 is glass)
    # fine-detail step 3c: both extinguishers and the reel are dressing-kit rows (fd_kit_specs), not mesh
    if not FINE_DETAIL:
        k.dress(LD.extinguisher("annex", (-4.90, HALL_X - 0.25), face("-X"), DECK_Y + 0.90))
        k.dress(LD.extinguisher("hall", (0.00, -HALL_X + 0.25), face("+X"), DECK_Y + 0.90))
        k.dress(LD.hose_reel("drive", (BW_CENTRE[2] - 1.20, 0.0), face("-Z"), DECK_Y + 1.10))
    # TB8 platform-edge chevrons on the four edge strips; TB9 deck-front nosings at the pit edge
    for tag, (x0, x1) in (("cL", (-3.20, -2.90)), ("cR", (2.90, 3.20)), ("oL", (-8.10, -7.80)),
                          ("oR", (7.80, 8.10))):
        k.dress(LD.chevrons(tag, "+Z", DECK_Y, (TRENCH_Z1, -0.80), (x0, x1), period=0.60, band=0.30))
    for tag, (x0, x1) in (("nosL", (-11.80, -8.11)), ("nosC", (-2.89, 2.89)), ("nosR", (8.11, 11.80))):
        k.dress(LD.stripe(tag, (TRENCH_Z1 + 0.05, x0), (TRENCH_Z1 + 0.05, x1), 0.10, DECK_Y, mat="safety"))
    # TB10 ski rack. Revision 2 (B1, Richard's eleventh look: "the skis outside the tram building look totally
    # different from the ones on the A frame - the A-frame ones way preferred (sans bindings)"): the A-frame
    # KIT rack itself (cs2_liftdress.aframe_rack -> cs2_bldg.ski_rack_frame, 8 slots, gear, no bindings) with
    # L1's entry-rack contents, on the village front east of the canopy, long axis along prefab X (Blender
    # frame: x = prefab Z, y = prefab X); 0.02 off the grade (out of the engine-base band +-0.01).
    # fine-detail step 3c: the kit's own A-frame rack (FD_RACK) as a grade row on the same footprint
    if not FINE_DETAIL:
        k.dress(LD.aframe_rack("t", x=TRAM_RACK_AT[2], y=TRAM_RACK_AT[0], skis=TRAM_RACK_GEAR, z0=TRAM_RACK_AT[1],
                               along="Y"))


# Revision 2 (B1): L1's `entryS` rack contents (bldg_recipes.L1_SKI_RACKS[0], copied as a literal) with every
# slot-2 "accent" color swapped for a fixed one, alternating ski_red / trim_white (the tram base's slot 2 is
# the roof gray: accent skis would read gray). (slot, side, kind, color, length)
TRAM_RACK_GEAR = (
    (0, 1, "ski", ("trim_white", "ski_blue"), 1.72), (1, 1, "ski", ("ski_black", "ski_red"), 1.64),
    (2, 1, "board", ("ski_black", "trim_white"), 1.54), (4, 1, "ski", ("ski_red", "ski_black"), 1.60),
    (5, 1, "poles", "ski_black", 1.38), (6, 1, "ski", ("ski_blue", "trim_white"), 1.78),
    (1, -1, "ski", ("ski_red", "ski_black"), 1.70), (3, -1, "board", ("trim_white", "ski_red"), 1.50),
    (4, -1, "ski", "trim_white", 1.58), (6, -1, "ski", ("ski_black", "trim_white"), 1.75))
TRAM_RACK_AT = (10.80, 0.02, 11.20)     # prefab (X, z0, Z) of the rack center: X 9.95-11.65, Z 10.52-11.88


def build_top_detail(k):
    """Detail pass 1, top (design 6.2 as amended; prefab meters). The hut-mounted pieces are C14
    HUT_NEIGHBOURS (skeptic MC8): the hut's -Z face carries the gauge (x 1.60..1.92), the window,
    the name board over it and the extinguisher (x 4.62..4.78); the +X face the hose reel; the +Z face
    the scraper and shovel. hutwin_b (-X face, z 1.00..2.30) left clear: the design's gauge / extinguisher
    spots there sat on the window."""
    LD = _LD()
    P3, face = LD.tram_point, LD.tram_facing
    zc = (GOAL_Z[0] + GOAL_Z[1]) / 2.0
    k.dress(LD.beacon("beacon", P3(0.0, MAST_TOP - BEACON_H, zc), h=BEACON_H))          # TT1
    # TT2 under lug_R, now over the +X track lane (revision 2, T6)
    k.dress(LD.crane_hook("R", P3(TRACK_X, GOAL_Y0 - 0.45, zc), drop=1.20))
    # TT3 berth-edge stripes, inboard of the bay-gate posts (posts at |x| 3.10..3.18 / 7.82..7.90)
    for tag, x in (("bcL", -3.00), ("bcR", 3.00), ("bL", -8.00), ("bR", 8.00)):
        k.dress(LD.stripe(tag, (-9.85, x), (-0.80, x), 0.10, DECK_Y, mat="safety"))
    hz0, hx1 = -0.40, 4.90
    k.dress(LD.board("stn", P3(3.20, 8.91, hz0 - 0.023), face("-Z"), 1.20, 0.50, mount="wall"))  # TT4
    k.dress(LD.gauge("tension", (hz0, 1.76), face("-Z"), 7.75, size=0.30))             # TT5
    if not FINE_DETAIL:         # fine-detail step 3c: TT6-TT8 are dressing-kit rows (fd_kit_specs)
        k.dress(LD.extinguisher("hut", (hz0, 4.70), face("-Z"), DECK_Y + 0.70))         # TT6
        k.dress(LD.hose_reel("hut", (1.00, hx1), face("+X"), DECK_Y + 1.10))            # TT7
        k.dress(LD.tools("hut", (2.60 + 0.32, 4.30), face("+Z"), DECK_Y))               # TT8
    build_trail_boards(k)


def build_trail_boards(k):
    """Revision 2 (D4): `board_trail_<i>` (galv, 0.90 x 0.28 x 0.03) standing on the rear cap rail's top tube
    (y 7.40 = its center; the back face 0.008 into the tube: a clamp), and `label_trail_<i>` (one quad 0.86 x
    0.24, LABEL_PROUD in front of the board's -Z face, painted by paint_trail_board in post_compose)."""
    W, H, T = TRAIL_BOARD["w"], TRAIL_BOARD["h"], TRAIL_BOARD["t"]
    lw, lh = TRAIL_BOARD["label"]
    z_rail = 11.50 - 0.028                          # the rear cap rail tube's inner (deck-side) face
    zb1 = z_rail + 0.008
    zb0 = zb1 - T
    y0 = DECK_Y + 1.10
    for i, (xc, _sym, _arrow) in enumerate(TRAIL_BOARDS):
        k.box(f"board_trail_{i}", xc - W / 2.0, xc + W / 2.0, y0, y0 + H, zb0, zb1, "galv")
        zq = zb0 - 0.003
        yc = y0 + H / 2.0
        k.quad(f"label_trail_{i}", [(xc + lw / 2.0, yc - lh / 2.0, zq), (xc - lw / 2.0, yc - lh / 2.0, zq),
                                    (xc - lw / 2.0, yc + lh / 2.0, zq), (xc + lw / 2.0, yc + lh / 2.0, zq)],
               "label", (0, 0, -1))


# Vanilla sub-objects (design 5.3 / 6.3 as amended by MC10; cs2_liftdress.SUBOBJ GUIDs verified
# against the dump). Game local = prefab (X, Y, Z). mount: grade | deck | beam (stands on the surface
# below) / wall (back on a wall, facing out) / ceiling (hangs from the surface above). parent: -1 =
# snapped to the terrain (flattened base lot only), 0 = relative to the station mesh.
SUBOBJ_ROWS = {
    "base": (
        ("TrashbinRandom01", (2.50, 0.0, 11.40), "+Z", -1, "grade"),
        ("TrashbinRandom01", (-9.50, 0.0, 11.40), "+Z", -1, "grade"),
        ("TrashbinRandom01", (-10.60, DECK_Y, 2.90), "+Z", 0, "deck"),
        ("TrashbinRandom01", (10.60, DECK_Y, 2.90), "+Z", 0, "deck"),
        ("GardenBenchRandom01", (-5.50, 0.0, 11.30), "+Z", -1, "grade"),
        ("GardenBenchRandom01", (-2.00, 0.0, 11.30), "+Z", -1, "grade"),
        # the entrance's two small vanilla wall lights stay (revision 2: the modeled lantern boxes over them
        # are gone; ruling Q4: small wall lights only)
        ("WallLightShortRangeRandom01", (3.60, 2.85, HALL_Z + 0.02), "+Z", 0, "wall"),
        ("WallLightShortRangeRandom01", (8.40, 2.85, HALL_Z + 0.02), "+Z", 0, "wall"),
    ) + tuple(("RoofLightRandom01", (xc, ROOFLIGHT_Y, zc), "+Z", 0, "ceiling")
              for xc in (-9.80, 0.0, 9.80) for zc in (-7.50, -3.00)),
    "top": (
        # revision 2 (T8): on the shortened crane beam's tips (top 20.654 < the mesh top MAST_TOP 20.70: the 1e
        # "v02 reaches 0.754 m above the mesh top" note is gone)
        ("WarningLightRandom01", (-(GOAL_X - 0.20), GOAL_Y1, 5.30), "+Z", 0, "beam"),
        ("WarningLightRandom01", (GOAL_X - 0.20, GOAL_Y1, 5.30), "+Z", 0, "beam"),
        ("LightpoleParkRandom01", (-2.60, DECK_Y, -9.40), "+Z", 0, "deck"),
        ("LightpoleParkRandom01", (2.60, DECK_Y, -9.40), "+Z", 0, "deck"),
        # revision 2 (L2, ruling Q1): the four square-headed modeled posts -> vanilla park lamps. Rear cap
        # corners, arm over the deck; outer bays at z -7.50, arm toward the berth (skeptic S4: the design's / 1e's
        # -5.70 stood on the gate-exit line, a 0.97 m walkway; its -8.20 put the 3.27 m head into the frame's
        # X-brace brace_La / Ra, found by C16 - at -7.50 the brace passes 0.9 m over the head, the box keeps
        # 0.78 m off the gate opening z -6.45)
        ("LightpoleParkRandom01", (-5.40, DECK_Y, 11.15), "-Z", 0, "deck"),
        ("LightpoleParkRandom01", (5.40, DECK_Y, 11.15), "-Z", 0, "deck"),
        ("LightpoleParkRandom01", (-9.70, DECK_Y, -7.50), "+X", 0, "deck"),
        ("LightpoleParkRandom01", (9.70, DECK_Y, -7.50), "-X", 0, "deck"),
        # skeptic F: the design's (-1.60, 10.70) bench stood on dplate_0 / dplate_1. One bench at the
        # back rail between dplate_1 and dplate_2, one 0.40 in front of dplate_0/1 facing the line
        ("GardenBenchRandom01", (1.40, DECK_Y, 10.70), "+Z", 0, "deck"),
        ("GardenBenchRandom01", (-1.60, DECK_Y, 9.40), "-Z", 0, "deck"),
        ("TrashbinRandom01", (4.60, DECK_Y, 10.90), "+Z", 0, "deck"),
        # beside the hut door on the -Z face (the design's (1.48, 8.55, 1.05) sat on hutwin_b)
        ("WallLightShortRangeRandom01", (1.76, 8.75, -0.40), "-Z", 0, "wall"),
        # revision 2 (L3): over the hut door on the -X face, 0.10 above the door head (replaces `lamp_hut`)
        ("WallLightShortRangeRandom01", (HUT[0], 8.62, 0.20), "-X", 0, "wall"),
    ),
}


def subobj_rows(role):
    """The vanilla rows of this build: SUBOBJ_ROWS (rows 1..14 / 1..13 keep their order), + the annex door's
    short-range wall light on the base with FINE_DETAIL on (fine-detail step 3c, FD_ANNEX_LIGHT), + (nitpick B2, RAMPS)
    the new cell's park lamps and bin (NB2_ROWS, rows 16..18). In the PREFAB frame (pf: the RAMPS base x - 4.00; the
    tables stay in the design frame, so a test patching SUBOBJ_ROWS keeps its design-frame numbers)."""
    rows = tuple(SUBOBJ_ROWS[role])
    if FINE_DETAIL and DETAIL1 and role == "base":
        rows += (FD_ANNEX_LIGHT,)
    if RAMPS and DETAIL1 and role == "base":
        rows += tuple(NB2_ROWS)
    return tuple((p_, pf(role, pos), f_, par, m_) for p_, pos, f_, par, m_ in rows)


# --------------------------------------------------------------------------------------
# Fine-detail pass, step 3c: the dressing kit's rows (build/fine-detail-step3c-tram-pali.md)
# --------------------------------------------------------------------------------------

_FD_DIR = {"+X": (1.0, 0.0), "-X": (-1.0, 0.0), "+Z": (0.0, 1.0), "-Z": (0.0, -1.0)}


def fd_kit_specs(role):
    """The dressing-kit rows of this station in the PREFAB (= game) frame, one per removed builder call (design
    A.2 / D, build/dress-kit.md 3 pivots): dict(prop (fine_detail.KIT object prefab), pos, facing (prefab), mount,
    parent, removed (the old parts' name prefixes), was[, grade]). ground: pos = the foot center on its floor (the
    rack on the grade, y 0, a parent -1 lot row like the base's benches); wall: pos = the old mount point ON the
    wall face at the prop's bottom (extinguisher: the old bottom; hose reel: the old center height - 0.35, the
    plate bottom), facing = the wall's outward normal; lean: pos = the old pair's center ON the wall face at the
    floor, facing = the wall's outward normal (fine_detail.kit_lean_row: the 0.3925 m foot-line rule).
    [] with FINE_DETAIL off."""
    if not (FINE_DETAIL and DETAIL1):
        return []
    xi = HALL_X - 0.25                  # the hall columns' faces the extinguishers hang on (cs2_liftdress mount)
    if role == "base":
        return [dict(prop="SkiDressExtinguisher", pos=(xi, DECK_Y + 0.90, -4.90), facing="-X", mount="wall", parent=0,
                     removed=("ext_annex",), was="extinguisher('annex', col (12, -5) -X face, bottom DECK_Y + 0.90)"),
                dict(prop="SkiDressExtinguisher", pos=(-xi, DECK_Y + 0.90, 0.00), facing="+X", mount="wall", parent=0,
                     removed=("ext_hall",), was="extinguisher('hall', col (-12, 0) +X face, bottom DECK_Y + 0.90)"),
                dict(prop="SkiDressHoseReel", pos=(0.00, round(DECK_Y + 1.10 - 0.35, 4), BW_CENTRE[2] - 1.20),
                     facing="-Z", mount="wall", parent=0, removed=("hosereel_drive_",),
                     was="hose_reel('drive', drive house -Z face, centre DECK_Y + 1.10, r 0.30 = the kit plate)"),
                dict(prop=FD_RACK, pos=(TRAM_RACK_AT[0], 0.0, TRAM_RACK_AT[2]), facing="+Z", mount="ground",
                     parent=-1, grade=True, removed=("skirack_t",),
                     was="aframe_rack('t', centre (10.80, 11.20), z0 0.02, along prefab X): frame + L1's entry gear "
                         "(ski_rack_frame slots 8); the kit rack stands on the grade (foot drop 0.02)")]
    hx1 = HUT[1]
    # the hut's -Z face (the old spot, x 4.70) has the plinth cap's front railing 0.23 m in front of it (F1 access:
    # rail_cap_top): the kit extinguisher hangs on the hut's +X face beside the hose reel instead (a fire point),
    # FD_TOP_EXT z along that face; its -X / -Z / +Z alternatives fail F1 (the door leaf, hutwin_b, sgpost_R0 / R1)
    return [dict(prop="SkiDressExtinguisher", pos=(hx1, DECK_Y + 0.70, FD_TOP_EXT), facing="+X", mount="wall",
                 parent=0, removed=("ext_hut",),
                 was="extinguisher('hut', hut -Z face x 4.70, bottom DECK_Y + 0.70): MOVED to the hut's +X face "
                     "beside the hose reel (no 0.40 m access in front of the -Z face: the cap railing 0.23 m off it)"),
            dict(prop="SkiDressHoseReel", pos=(hx1, round(DECK_Y + 1.10 - 0.35, 4), 1.00), facing="+X", mount="wall",
                 parent=0, removed=("hosereel_hut_",),
                 was="hose_reel('hut', hut +X face z 1.00, centre DECK_Y + 1.10, r 0.30 = the kit plate)"),
            # the builder pair's center: foot + right * gap / 2 (cs2_liftdress.tools lays the 2nd tool 0.35 to its right)
            dict(prop="SkiDressTools", pos=(round(4.30 + 0.175, 4), DECK_Y, HUT[3]), facing="+Z", mount="lean", parent=0,
                 removed=("tool_hut_",), was="tools('hut', foot (x 4.30, hut +Z face + 0.32), +Z): the pair centre x 4.475")]


def fd_kit_rows(role, colour_set=None):
    """fine_detail.kit_row / kit_lean_row dicts (DESIGN = prefab-numbers frame, X = FBX-raw x, NOT game: check_subobjects
    writes them through rule T; yaw = the prop's +Z to `facing`) of fd_kit_specs. Colored props (the rack) name the role's color set (`colour_set` or FD_KIT_COLOUR[role])."""
    import fine_detail as FD
    rows = []
    cset = colour_set or FD_KIT_COLOUR[role]
    for s in fd_kit_specs(role):
        note = f"replaces {s['was']}"
        d = _FD_DIR[s["facing"]]
        cs = cset if FD.is_coloured(s["prop"]) else None
        pos = pf(role, s["pos"])                    # nitpick B2: the manifest rows are prefab frame (specs: design)
        if s["mount"] == "lean":
            rows.append(FD.kit_lean_row(s["prop"], pos, d, pos[1], parent=s["parent"], note=note,
                                        colour_set=cs))
        else:
            rows.append(FD.kit_row(s["prop"], pos, facing=d, parent=s["parent"], note=note, colour_set=cs))
    return rows


def _fd_host(P):
    """Tram design (prefab-numbers, FBX-raw x) frame (X, Y, Z) -> the z-up host frame of cs2_kitrows (Z, X, Y)
    (= tram_point). The design frame is NOT game: game = (-X, Y, Z) (cs2_liftdress.tram_to_game)."""
    P = np.asarray(P, dtype=np.float64)
    return P[..., [2, 0, 1]]


def fd_vanilla_boxes(role):
    """[(label, lo, hi)] host frame: every vanilla row's variant-union box (cs2_liftdress.subobj_box), its mount
    face pulled 0.01 off (a kit prop may stand beside a row on the same floor / wall)."""
    LD = _LD()
    out = []
    for prefab, pos, facing, _parent, mount in subobj_rows(role):
        lo, hi = LD.subobj_box(prefab, pos, LD.facing_quat(facing), frame="game")
        lo, hi = np.array(lo, dtype=np.float64), np.array(hi, dtype=np.float64)
        if mount in ("grade", "deck", "beam"):
            lo[1] += 0.01
        elif mount == "ceiling":
            hi[1] -= 0.01
        elif mount == "wall":
            fx, fz = _FD_DIR[facing]
            if fx > 0:
                lo[0] += 0.01
            elif fx < 0:
                hi[0] -= 0.01
            elif fz > 0:
                lo[2] += 0.01
            else:
                hi[2] -= 0.01
        h = _fd_host(np.array([lo, hi]))
        out.append((f"{prefab}@{pos[0]:.2f},{pos[2]:.2f}", h.min(axis=0), h.max(axis=0)))
    return out


def fd_envelopes(role):
    """[(label, lo, hi)] host frame: the C4 carrier zones (cabin body, hanger ring, carriage, both lanes) and the
    T8 real-cabin sweep AABB per bay (the real SkiTramCabinA x +-1.88, z +-4.03, swept from the line to the dock;
    test_detail_tram D8's box)."""
    out = []
    for label, lo, hi, _allowed in carrier_zones(role):
        h = _fd_host(np.array([lo, hi]))
        out.append((f"C4 {label}", h.min(axis=0), h.max(axis=0)))
    for s_ in (-1, 1):
        xl = s_ * TRACK_X + frame_dx(role)
        lo = (xl - 1.88 - 0.05, PIVOT_Y - 0.10, -12.5)
        hi = (xl + 1.88 + 0.05, CARRIAGE_TOP_Y + 0.20, DOCK_Z + 4.03 + 0.05)
        h = _fd_host(np.array([lo, hi]))
        out.append((f"T8 cabin sweep {'L' if s_ < 0 else 'R'}", h.min(axis=0), h.max(axis=0)))
    return out


def fd_keepouts(role):
    """[(label, lo, hi)] host frame: the base's LV feed pole at (16, 0, -12) (a 1.0 m square, the hosted
    PowerLinePole01) - nothing else is hosted outside the carrier envelopes."""
    if role != "base":
        return []
    lo, hi = pf(role, (15.5, 0.0, -12.5)), pf(role, (16.5, 12.0, -11.5))    # nitpick B2: prefab frame
    h = _fd_host(np.array([lo, hi]))
    return [(f"LV feed pole ({lo[0] + 0.5:g}, 0, -12)", h.min(axis=0), h.max(axis=0))]


def check_kit_rows(t, role, table):
    """F1 (fine-detail step 3c; cs2_kitrows.check_kit_rows on the REAL kit meshes, host frame (Z, X, Y)): mount,
    contact, the 0.40 m access in front of the wall props, the real kit mesh backed 0.01 m off its mount clear of
    every station triangle, the C4 carrier zones + the T8 cabin-sweep box, the LV pole, every vanilla row's union
    box and every other kit row, inside the lot. Returns (message dict, numbers) or ({}, None) with FINE_DETAIL
    off; raises ContractError("F1 ...")."""
    import cs2_kitrows as KR
    specs = fd_kit_specs(role)
    if not specs:
        return {}, None
    rows = fd_kit_rows(role)
    tri, names = triangles(t)
    co = _fd_host(tri.reshape(-1, 3))
    tris = np.arange(len(co), dtype=np.int64).reshape(-1, 3)
    W, D = lot_cells(role)
    hspecs = [dict(s, pos=tuple(_fd_host(np.array(pf(role, s["pos"])))),
                   normal=(_FD_DIR[s["facing"]][1], _FD_DIR[s["facing"]][0])) for s in specs]
    try:
        return KR.check_kit_rows(hspecs, rows, co, tris, names, _fd_host, all_names=table,
                                 keepouts=fd_keepouts(role), envelopes=fd_envelopes(role),
                                 vboxes=fd_vanilla_boxes(role), lot=(-4.0 * D, 4.0 * D, -4.0 * W, 4.0 * W))
    except KR.KitRowError as e:
        _fail("F1", str(e))


def fine_detail_block(role, report):
    """The manifest's `fine_detail` block (None with FINE_DETAIL off): flag, frame, rows by kind, the kit rows
    with their F1 numbers, what left the mesh and why, lamps -> vanilla, what stays modeled, placeholders."""
    if not (FINE_DETAIL and DETAIL1):
        return None
    msg, nums = report.get("F1") or ({}, None)
    blk = (report.get("C16") or {}).get("block") or {}
    lamps = ([{"part": "lamp_annexdoor", "row": FD_ANNEX_LIGHT[0], "pos": [round(v, 4) + 0.0 for v in _LD().rule_t_pos(pf(role, FD_ANNEX_LIGHT[1]))],
               "facing": FD_ANNEX_LIGHT[2] + " (design frame; pos is game: rule T)", "why": "design A.2 (the tram base's wall lamps -> short-range wall "
                                                  "lights); the bar's lens centre"}] if role == "base" else [])
    kept = {"labels / LIFT boards": "FS5: the host atlas (TICKETS, LIFT OPS, departure board, station name, "
                                    "tension, the three trail boards)",
            "gates": "boxes stay boxes (benchmark 2.2); the 2.2 m perforated top leaves and the C17 layout unchanged",
            "medallions": "6-gon discs (FD_MEDAL_RING False: a ring is +28 tris each, not cheap on the top)"}
    if role == "base":
        kept["lamp_exit"] = "the green EXIT light: no vanilla exit sign (PLACEHOLDER)"
        kept["lamp_plate_*, lamp_rod_*"] = "the ceiling plates the vanilla RoofLight rows hang from (C16 support)"
    else:
        kept["lamp_beacon"] = "the red lens on the roof mast: it sets the frozen top bound 20.70 (PLACEHOLDER)"
        kept["mast, mastarm, antdish, antpanel"] = ("FS1: the anemometer mast stays; dish / panel PLACEHOLDERS "
                                                    "(vanilla SatelliteDish / Antenna exist, not in SUBOBJ)")
    return {"flag": True, "step": "3c", "report": "build/fine-detail-step3c-tram-pali.md",
            "x_convention": blk.get("x_convention"), "rows": blk.get("kinds"), "F1": msg.get("F1"),
            "kit_rows": (nums or {}).get("rows"),
            "removed": [{"prop": s["prop"], "removed": list(s["removed"]), "was": s["was"]} for s in fd_kit_specs(role)],
            "lamps_to_vanilla": lamps, "fine_geometry": "none (no maze, swing gate, post board or cabinet on the "
                                                        "tram stations)",
            "kept_modelled": kept}


# nitpick B: the families JH_DETAILS adds (manifest `nitpick_b` block: LOD0 tris per family, measured on the mesh)
JH_FAMILIES = {"canopy (soffit wedge + roof sheet)": ("jhcanopy", "jhroof"), "V-struts": ("jhstrut_",),
               "strut shoes": ("jhshoe_",), "canopy ties": ("jhtie_",),
               "annex ticket windows (frames + sills)": ("tickframe_a", "sill_a"),
               "LIFT TICKETS board + label": ("board_lifttickets", "label_lifttickets"),
               "ENTRANCE board + label": ("board_entrance", "label_entrance"),
               "chalkboard A-frame (PLACEHOLDER)": ("chalkboard_",)}


def nitpick_b_block(role, t):
    """The manifest's `nitpick_b` block (None on the top or with JH_DETAILS off): what the pass added (LOD0 tris per
    family on the joined mesh), the PLACEHOLDER, the details proposed but not added, the two STOPPED ramps."""
    if role != "base" or not JH_DETAILS:
        return None
    _tri, names = triangles(t)
    fam = {k_: sum(1 for n_ in names if n_.startswith(p_)) for k_, p_ in JH_FAMILIES.items()}
    return {"flag": "JH_DETAILS", "report": "build/nitpick-b-tram.md",
            "photos": "ref-richard-2026-10-13 03 / 04 / 07 (JH tram base, guest-services building)",
            "added_tris_lod0": fam, "added_tris_lod0_total": sum(fam.values()),
            "labels": {nm: list(w) for nm, w in sorted(LABEL_WORDS.items())},
            "placeholders": {"chalkboard_": "no vanilla chalkboard; the vanilla AStand01 is a brand-spawned ad stand "
                                            "(BrandObject variants), unverified on a host without a company"},
            "ramps": ("BUILT by the second pass (RAMPS; the `nitpick_b2` block, build/nitpick-b2-tram-ramps.md): the "
                      "lot 5 x 3 -> 6 x 3 and a 1:8 switchback to the side area" if RAMPS else
                      "STOPPED (build/nitpick-b-tram.md section 1): base - a <= 1:8 ramp from grade to the skier "
                      "level (annex 5.20 / deck 6.30) needs 41.6-50.4 m of run, the +X side has 0.40 m between the "
                      "annex (x 19.60) and the frozen lot / bound (x 20.00); top - see the report")}


# nitpick B2: the families RAMPS adds (manifest `nitpick_b2` block: LOD0 tris per family on the joined mesh)
NB2_FAMILIES = {"base": {"ramp decks": ("ramp_",), "ramp landings + balcony": ("landing_turn", "landing_rtop",
                                                                                "landing_balc"),
                         "columns + ledger": ("rampcol_", "rampbeam_"),
                         "rails (posts, rails, kick plates)": ("post_ramp", "rail_ramp", "kick_ramp", "post_balc",
                                                               "rail_balc", "kick_balc"),
                         "plaza paving (4 quads)": ("plaza",), "side-area door": ("annexdoor", "doorlite_side"),
                         "annex transom pane over the door": ("annexwinlo_dt",)},
                "top": {"ramp decks": ("ramp_",), "pads": ("pad_",), "head grating + beams": ("grate_headR",
                                                                                             "deckbeam_r"),
                        "columns": ("rampcol_",),
                        "rails (posts, rails, kick plates)": tuple(f"{a}_{b}" for a in ("post", "rail", "kick")
                                                                   for b in ("head", "ramp", "pad"))}}


def nitpick_b2_block(role, t, report):
    """The manifest's `nitpick_b2` block (None with RAMPS off): the frame shift and the re-expressed frozen numbers
    (design -> prefab), the ramps as C15 measured them, C15b, the side-area door, the added rows, LOD0 tris per family."""
    if not RAMPS:
        return None
    _tri, names = triangles(t)
    fam = {k_: sum(1 for n_ in names if n_.startswith(p_)) for k_, p_ in NB2_FAMILIES[role].items()}
    c15 = report.get("C15") or {}
    dx = frame_dx(role)
    shift = None
    if dx:
        shift = {"design_to_prefab_dx": dx,
                 "stubs": [{"kind": s["kind"], "was": [round(s["a"][0] - dx, 6), s["a"][1], s["a"][2]], "now": list(s["a"])}
                           for s in stub_table(role)],
                 "docks_rope": [{"was": [s * TRACK_X, TRACK_Y, DOCK_Z], "now": [round(s * TRACK_X + dx, 6), TRACK_Y, DOCK_Z]}
                                for s in (-1, 1)],
                 "hosted": [{"what": h["what"], "was": [round(h["m_Position"][0] - dx, 6)] + h["m_Position"][1:],
                             "now": h["m_Position"]} for h in hosted_table(role)],
                 "wheels": {"bullwheel": [list(BW_CENTRE), list(pf(role, BW_CENTRE))],
                            "deflection": [[[s * BASE_DS_X, HAUL_Y, BASE_DS_Z], [round(s * BASE_DS_X + dx, 6), HAUL_Y, BASE_DS_Z]]
                                           for s in (-1, 1)]},
                 "rows": "every ObjectSubObjects row (vanilla + kit) x + dx; rows 1..15 keep their order, NB2_ROWS = "
                         "rows 16..18",
                 "lot": [list(LOT[role]), list(lot_cells(role))]}
    return {"flag": "RAMPS", "report": "build/nitpick-b2-tram-ramps.md",
            "rulings": "verdicts-2026-10-11 'Nitpick B (tram) - first pass landed' ORCHESTRATOR RULINGS",
            "frame_shift": shift, "axis_x": dx, "lot": list(lot_cells(role)),
            "plan": base_ramp_plan() if role == "base" else top_ramp_plan(),
            "ramps": c15.get("ramps"), "C15b": c15.get("C15b"), "goal": c15.get("goal"),
            "rows_added": ([{"prefab": p_, "pos": list(pf(role, pos)), "facing": f_, "parent": par, "mount": m_}
                            for p_, pos, f_, par, m_ in NB2_ROWS] if role == "base" else []),
            "added_tris_lod0": fam, "added_tris_lod0_total": sum(fam.values()),
            "removed": ([] if role == "base" else
                        ["the +X end stair (flight s, its foot pad and rails stairo / stairi, the outer deck's back "
                         "rail outerRb)", "the -X end stair (flights wu / wl, landing_midW, stairfoot_1, stairposts "
                         "2-3, rails landtopW / stairwu* / landwm* / stairwl*; the gangway's back rail shortened)"])}


# nitpick B3: the families ANNEX_STAIR adds / replaces (manifest `nitpick_b3` block: LOD0 tris on the joined mesh)
NB3_FAMILIES = {"wall pieces at the doorway (annexback_0..2; was one box, 12 tris)": ("annexback_",),
                "landing block through the wall": ("landing_annex",), "solid steps": ("tread_annex_",),
                "rails (posts, rails, kick plates)": ("post_annex", "rail_annex", "kick_annex")}


def nitpick_b3_block(role, t, report):
    """The manifest's `nitpick_b3` block (None on the top / with ANNEX_STAIR off): the stair's plan, the C15 route that
    proves grade -> ramp -> annex -> stair -> hall -> boarding deck, LOD0 tris per family."""
    if not ANNEX_STAIR or role != "base":
        return None
    _tri, names = triangles(t)
    fam = {k_: sum(1 for n_ in names if n_.startswith(p_)) for k_, p_ in NB3_FAMILIES.items()}
    c15 = report.get("C15") or {}
    return {"flag": "ANNEX_STAIR", "report": "build/nitpick-b2-tram-ramps.md (Annex stair (B2 check F2))",
            "plan": base_annex_plan(), "knobs": {k_: (list(v) if isinstance(v, tuple) else v) for k_, v in NB3.items()},
            "route": c15.get("route"), "tris_lod0": fam, "tris_lod0_total": sum(fam.values()),
            "walkable": "annexcap is a C15 walkable surface (ANNEX_WALK); the NOT_WALKABLE entry is NOT_WALKABLE_ANNEX_OFF",
            "removed": "annexback (one 12-tri box) -> annexback_0..2"}


# revision 3 (PLINTH_TOP): the families the plinth mode adds or continues below grade (manifest `plinth_top`: LOD0 tris)
R3_FAMILIES = {"foundation block + footing slab": ("found_",), "lattice columns": ("latcol_",),
               "lattice girts + ties": ("latgirt_",), "lattice X braces": ("latbrace_",),
               "lattice pad footings": ("latpad_",),
               "continued below grade (pier, pile caps, pads, ramp columns, stair posts)":
                   ("plinth", "footing_", "pad_", "rampcol_", "stairpost_")}
R3_CONTINUED = ("plinth", "footing_", "pad_", "rampcol_", "stairpost_")   # the shipped parts that now reach below grade
# the wheels whose clearance the report measures (the lowest first: the anchor drums' rims stand 0.75 m over the grade)
R3_WHEELS = ("drumrim_L", "drumrim_R", "fsrim_L", "fsrim_R", "bullrim", "dwrim_L", "dwrim_R")


def plinth_clearances(t):
    """Revision 3: each wheel rim's lowest point, and its nearest distance (rim vertices to the other part's surface) to
    the foundation block, the pier (plinth) and the plinth deck; the track rope's lowest point over the block."""
    from mathutils.bvhtree import BVHTree
    pf, parts = _part_tris(t)

    def bvh(nm):
        tr = parts.get(nm)
        return None if tr is None else BVHTree.FromPolygons(pf.tolist(), tr.tolist(), all_triangles=True)
    targets = {k_: bvh(k_) for k_ in ("found_block", "plinth", "plinthcap")}
    out = {}
    for nm in R3_WHEELS:
        tr = parts.get(nm)
        if tr is None:
            continue
        vs = pf[np.unique(tr.ravel())]
        row = {"rim_low_y": round(float(vs[:, 1].min()), 3)}
        for k_, b in targets.items():
            if b is not None:
                row[f"to_{k_}_m"] = round(min(b.find_nearest(Vector(v.tolist()))[3] for v in vs), 3)
        row["over_seat_m"] = round(row["rim_low_y"] - PLINTH["seat"], 3)
        out[nm] = row
    ropes = [n for n in parts if n.startswith("rope_track_")]
    low = min(float(pf[np.unique(parts[n].ravel())][:, 1].min()) for n in ropes) if ropes else None
    return {"wheels": out, "track_rope_low_y": round(low, 3) if low is not None else None,
            "track_rope_over_seat_m": round(low - PLINTH["seat"], 3) if low is not None else None,
            "how": "rim vertices to the target part's surface (BVH nearest); prefab frame"}


def plinth_top_block(role, t, report, detail1=None):
    """The manifest's `plinth_top` block (None on the base / with PLINTH_TOP off): the plan, the plinth rule per LOD
    (C6p), the families' LOD0 tris, the wheel clearances, the below-grade density classes."""
    if base_mode(role) != "plinth":
        return None
    _tri, names = triangles(t)
    fam = {k_: sum(1 for n_ in names if n_.startswith(p_)) for k_, p_ in R3_FAMILIES.items()}
    dens = ((detail1 or {}).get("density") or {}).get("below_grade")
    return {"flag": "PLINTH_TOP", "report": "build/tram-top-rev3.md",
            "verdict": "verdicts-2026-10-11 'Twenty-seventh look' T-3", "mode": "plinth",
            "plan": top_plinth_plan(), "C6p": report.get("C6p"), "tris_lod0": fam,
            "tris_lod0_new": sum(v for k_, v in fam.items() if not k_.startswith("continued")),
            "continued": list(R3_CONTINUED), "clearances": plinth_clearances(t), "density_below_grade": dens,
            "rules": {"engine_base": "none: no vertex within PLINTH band of y 0 on LOD0 / LOD1 / LOD2 (C6p); the prefab's "
                                     "LOD0 RenderPrefab gets BaseProperties {m_BaseType: null, m_UseMinBounds: false}",
                      "depth": f"min y -{PLINTH['depth']:.2f} on every LOD (the slab + the lattice pad footings)",
                      "closed": "every open edge of a below-grade part lies on another part's face (C6p closed, LOD0 + LOD1)",
                      "above_grade": "vertex-identical to the shipped nitpick-B2 top above y 0 (test T9)",
                      "C15b": "a pad counts as grade on the no-level top when it stands on OR continues below the grade "
                              "(lowest vertex <= 0.01; the pads now reach the foundation block at -0.05)"}}


def fd_probe(t, role):
    """A temporary copy of the oriented LOD0 with the kit rows' real meshes appended (parts kitrow_*): C15
    measures its crossings with them (oriented Blender = (X, -Z, Y) of prefab). None with FINE_DETAIL off."""
    import cs2_kitrows as KR
    rows = fd_kit_rows(role)
    if not rows:
        return None
    return KR.probe_object(t, rows, lambda G: np.stack([G[:, 0], -G[:, 2], G[:, 1]], axis=1),
                           cs2_asset.PART_TABLE_PROP, name=f"{t.name}KitProbe")


def build_skirt(k, depth):
    """--skirt D (top): rock skirt from y 0 down to -D under the plinth and footings, for the
    BaseProperties {m_BaseType null} route ONLY (probe pending, RS section 9)."""
    x, z0, z1 = TOP_PLX, -0.50, 11.40
    k.prism("skirtplinth", [(-x, -depth, z0), (x, -depth, z0), (x, -depth, z1), (-x, -depth, z1)],
            [(-x, 0.0, z0), (x, 0.0, z0), (x, 0.0, z1), (-x, 0.0, z1)],
            "rock", open_bottom=True, open_top=True)
    for nm, xc, zc, h in top_footings():
        k.box(f"skirt{nm}", xc - h, xc + h, -depth, 0.0, zc - h, zc + h, "concrete",
              drop=("+Y", "-Y"))


# --------------------------------------------------------------------------------------
# Finish data: bevels, density, relief, LOD delete lists
# --------------------------------------------------------------------------------------

BEVEL = {
    "base": ("stonebase", "stonepier_", "stonecap", "annexbase", "annexcap", "col_",
             "girderface", "girderroof_", "deck_", "roof", "annexroof", "drivehouse",
             "cwshaft_", "opcab", "pictowall", "ctbase", "ctshaft", "ctstage", "stairstr_",
             "landing_", "trenchcap", "sgirder_", "sportal_", "mplatbase", "mplattop",
             "vpier_"),
    "top": ("plinth", "footing_", "strut_", "headbeam_", "headgirder_", "hut", "hutroof",
            "dplate_", "stairstr_", "landing_", "sgirder_", "goalbeam", "goalpost_", "dwbeam",
            "bullframe_"),
}
BEVEL_EXCLUDE = ("opcabwin", "annexwin", "tickwin", "hutwin", "glazepanel_", "opcabfill_",
                 # revision 1c: the -X stair's stringers and landings stay square-edged (192 tris:
                 # the top's 14,000-tri LOD0 cap with the stair; a 2 cm bevel on a 0.10 m stringer)
                 "stairstr_w", "landing_topW", "landing_midW",
                 # nitpick B2: the ramp landings and the paving stay square-edged (grating slabs; the 16,000 cap)
                 "landing_turn", "landing_rtop", "landing_balc", "plaza_",
                 # nitpick B3: the annex stair's landing block stays square-edged (a hidden interior block)
                 "landing_annex")

DENSITY_WEIGHTS = {
    "pictodisc": 2.5, "pictoplinth": 2.5, "ctface_": 2.5,
    "stonebase": 1.3, "stonepier_": 1.3, "pictowall": 1.3, "ctbase": 1.3, "plinth": 1.3,
    "strut_": 1.3, "headbeam_": 1.3,
    "hut": 1.5, "dplate_": 2.0,
    "roof": 1.0, "annexroof": 1.0, "girderface": 1.0, "col_": 1.0, "ctshaft": 1.0, "ctcap": 1.0,
    "glazepanel_": 0.6,
    "brace_": 0.7, "rail_": 0.45, "post_": 0.7, "gate_": 0.7, "stairstr_": 0.7,
    "bwspoke_": 0.7, "bwrim": 0.6, "medal_": 1.0,
    "girderroof_": 0.4, "deckbeam_": 0.4, "cwshaft_": 0.4, "drivehouse": 0.5,
    "deck_": 0.4,
    # revision 1. Thin curved strips (ropes 50-70 mm, rail tubes 56 mm, rim annuli, fins,
    # mullions) pack as islands whose AABB is 1-7 % filled: measured, they took ~12 % of the
    # top atlas and ~8 % of the base one. Their texture is a few texels wide at any weight, so
    # they are scaled down (rope_ 1.0 -> 0.35, rail_ 0.7 -> 0.45, bwrim 0.8 -> 0.6).
    "rope_": 0.35, "sgirder_": 1.0, "bullrim": 0.6, "bullspoke_": 0.6, "dwrim_": 0.6,
    "dwspoke_": 0.6, "drumrim_": 0.8, "drumspoke_": 0.8, "goalbeam": 1.0, "goalpost_": 1.0,
    "fin_": 0.35, "mullion_": 0.35, "outrigger_": 0.5, "purlin_": 0.4,
    "horn_": 0.55, "hornstrut_": 0.45, "mplat": 0.8, "trackrail_": 0.4, "trenchwall_": 0.6,
    "sfront": 1.2, "door_annex": 1.2, "door_stairin": 1.2, "doorlite_": 1.0, "spandrel_": 1.0,
    # revision 1c: the catwalk ladder (thin, like the rails)
    "ladder_": 0.45, "lrung_": 0.45,
}
# Detail pass 1: the dressing families (cs2_liftdress.DRESS_DENSITY values; labels start at LABEL_W0
# and are re-weighted for their px/m target before the bake, label_weights) and the resolution moves
# per station (design 5.7 / 6.5: the visible classes under 14 px/m)
# (board_ / tool_ / stripe_ / chev_ raised over the kit's 0.6: at the top's w = 1 of ~20.7 px/m 0.6
# shipped 12.4, under the 14 px/m floor; girderroof_ 0.4 -> 0.6: design 8 listed it under 14 and
# section 5.7 forgot it)
DENSITY_DETAIL1 = {"board_": 0.7, "label_": LABEL_W0,
                   "ext_": 1.0, "hosereel_": 1.0, "tool_": 1.0, "skirack_": 0.8, "ski_": 0.8,
                   "chev_": 0.7, "stripe_": 0.7, "hook_": 0.7, "gauge_": 1.0, "tickframe_": 0.7,
                   "lamp_": 1.0}
DENSITY_ROLE = {"base": {"deck_": 0.8, "purlin_": 0.7, "trackrail_": 0.6, "outrigger_": 0.8,
                         "girderroof_": 0.6,
                         # revision 2: the 4.0 m drive wheel and its deflection sheaves (JH-04 yellow)
                         "bwrim": 1.0, "bwspoke_": 1.0, "bwdrim_": 1.0},
                "top": {"deckbeam_": 0.7, "bullrim": 1.0, "bullspoke_": 1.0, "dwrim_": 1.0,
                        "dwspoke_": 1.0, "gate_": 1.0,
                        # revision 2 (design 5 "Density")
                        "fsrim_": 1.0, "cwguard": 0.7, "antdish": 1.0, "antpanel": 1.0}}


# nitpick B (JH_DETAILS, base only): the canopy and its V-struts read at the plaza like the stone piers under them; the
# shoes, ties and sills are small members; the chalkboard is a hero-scale prop at 10 m
# (horn_ 0.55 -> 0.57: the pass costs the atlas w = 1 about 3-4 % (25.82 -> 24.81 px/m shipped); the guide horns,
# 14.2 px/m before it, dropped under the 14 px/m floor (test_detail_tram D10) at 13.8 on a trial export; 14.1 shipped)
DENSITY_JH = {"jhcanopy": 1.0, "jhroof": 0.7, "jhstrut_": 1.0, "jhshoe_": 0.6, "jhtie_": 0.6, "sill_a": 0.7,
              "chalkboard_": 1.0, "horn_": 0.57}


# nitpick B2 (RAMPS): the ramps, their landings, pads, paving and the side-area door. Every visible new class stays over
# the D10 14 px/m line at the shipped w = 1 (base ~24.4 @4096, top ~20 @2048: 0.6 / 0.75); the plaza paving is the
# biggest new surface (192 m2) and the reason the base floors move (build/nitpick-b2-tram-ramps.md)
DENSITY_NB2 = {"base": {"ramp_": 0.62, "rampcol_": 0.6, "rampbeam_": 0.6, "plaza_": 0.62, "landing_turn": 0.6,
                        "landing_rtop": 0.6, "landing_balc": 0.6, "annexdoor": 0.7, "doorlite_side": 0.7,
                        "kick_ramp": 0.6, "kick_balc": 0.6, "post_ramp": 0.6, "post_balc": 0.6,
                        # the paving under the decks (lane A / lane B / the turn landing cover it from above):
                        # DENSITY_HIDDEN, like the drive house under the bullwheel
                        "plazaunder_": 0.3,
                        # the atlas cost of the ramps (w = 1 24.81 -> ~23.5): the guide horns back over 14 px/m
                        "horn_": 0.60},
               "top": {"ramp_": 0.75, "rampcol_": 0.75, "pad_": 0.75, "deckbeam_r": 0.75, "grate_headR": 0.75}}


# nitpick B3 (ANNEX_STAIR): the annex stair's landing and steps stand inside the painted glazing (seen through the
# doorway and the door's lites only): 0.7, which keeps them over the D10 14 px/m line at the shipped w = 1 (~23 px/m)
DENSITY_NB3 = {"landing_annex": 0.7, "tread_annex_": 0.7}


# revision 3 (PLINTH_TOP): the below-grade structure. Hidden on a flat summit and seen only where a cliff exposes it (from
# a distance, from below): the foundation's big plain concrete faces at a low weight (its slab's underside lowest), the
# lattice members at the deck braces' class; DENSITY_BELOW keeps them off the 14 px/m starving list (D10 floors them
# separately). Measured cost on the shipped atlas: build/tram-top-rev3.md.
DENSITY_R3 = {"found_block": 0.15, "found_slab": 0.12, "latpad_": 0.35, "latcol_": 0.50, "latgirt_": 0.50,
              "latbrace_": 0.50}
DENSITY_BELOW = ("found", "lat")
# revision 3: the lattice kit is built translation-identical (both sides with the same member `up`), so its copies share
# one UV island per shape (cs2_liftdress.share_identical; columns 6 -> 1, bay girts 8 -> 1, ties 4 -> 1, braces 16 -> 4, pad
# footings 6 -> 1): thin members pay the pack margin once, not per copy (the first scratch export, unshared, cost the 2048
# atlas 6.4 % of w = 1)
R3_SHARE = ("latcol_", "latgirt_", "latbrace_", "latpad_")


def density_weights(role):
    """DENSITY_WEIGHTS, plus (DETAIL1) the dressing families and the station's resolution moves, plus (JH_DETAILS) the
    nitpick-B parts, plus (RAMPS) the nitpick-B2 ramps, plus (ANNEX_STAIR) the nitpick-B3 annex stair."""
    w = dict(DENSITY_WEIGHTS)
    if DETAIL1:
        w.update(DENSITY_DETAIL1)
        w.update(DENSITY_ROLE[role])
    if JH_DETAILS and role == "base":
        w.update(DENSITY_JH)
    if RAMPS:
        w.update(DENSITY_NB2[role])
    if ANNEX_STAIR and role == "base":
        w.update(DENSITY_NB3)
    if PLINTH_TOP and role == "top":
        w.update(DENSITY_R3)
    return w

# LOD1 drops sub-pixel hardware; LOD2 keeps the masses (MC4: opcab / hut stay, so the
# shared `_LOD2_Win` panes never float). Prefix hygiene: girderface vs girderroof_,
# bw* vs brace_ / bull*, hut / hutroof, deck_ vs deckbeam_ / deckstrut_, leg_ vs legbrace_,
# sgirder_ vs sghanger_ / sgpost_ / sgtie, drumrim_ vs drumspoke_.
LOD1_DELETE = ("bolt_", "clamp_", "drip_", "kick_", "post_", "tread_", "lrung_", "ladder_",
               "lamp_", "gate_", "gatepost_", "gusset_", "flange_", "stiff_", "axlecap_",
               "bwspoke_", "rope_", "downspout_", "door_", "hatch_",
               "ctfinial", "sheave_", "scope_", "medal_", "deckstrut_",
               "transom_", "stairpost_", "sill_", "cwlid_", "opcabpost_", "annextransom",
               # revision 1
               "fin_", "mullion_", "purlin_", "horn", "mplatwheel_", "mplatbrace_",
               "mplatpost_", "trackrail_", "pcathanger_", "sfront", "doorlite_", "spandrel_",
               "opcabfill_", "portalknee_", "sghanger_", "legbrace_", "drumspoke_", "drumhub_",
               "dwspoke_", "dwhub_", "dwaxle_", "bullspoke_", "bullaxle", "lug_",
               "svcpost_", "goalknee_",
               # revision 2: the foot-sheave hub discs, the frame-only wheel guard and the base deflection
               # sheaves' hubs + axles go at LOD1. The roof mast, its dish / panel / arm stay (ruling Q3:
               # no LOD height pop over 1 m; only the 0.20 m beacon lens goes with lamp_)
               "fshub_", "cwguard", "bwdhub_", "bwdaxle_",
               # detail pass 1: every dressing family goes at LOD1 (design DL8; none of the tram's
               # dressing is a mast shaft, bridge or ladder rail). Family prefixes, checked against
               # the station's own part names (no collision: the test asserts it)
               "label_", "board_", "ext_", "hosereel_", "tool_", "skirack_", "ski_", "chev_",
               "stripe_", "hook_", "gauge_", "tickframe_",
               # nitpick B (base): the canopy's shoes and ties and the chalkboard go at LOD1 (the canopy and its V's
               # stay: the JH-04 silhouette at the LOD1 distance; they go at LOD2 with the entry canopy)
               "jhshoe_", "jhtie_", "chalkboard_",
               # nitpick B2: the side-area door's push bars (its leaves stay: no hole in the glazing at LOD1)
               "annexdoor_bar",
               # nitpick B3: the annex stair's railing tubes (its posts, kick plates and treads go with post_ / kick_ /
               # tread_); the landing block stays (it fills the doorway's sill in the cut wall)
               "rail_annex",
               # revision 3 (PLINTH_TOP): the lattice bents' X braces (their columns and girts stay at LOD1: the legs a cliff
               # shows at the LOD1 distance)
               "latbrace_")
LOD2_COMMON = LOD1_DELETE + (
    "rail_", "deck_", "deckbeam_", "edgestrip_", "bw",
    "drivehouse", "cwshaft_", "girderroof_", "stonepier_", "pictowall", "pictodisc",
    "landing_", "fascia_", "entrycanopy", "canopyroof", "ctface_", "ctcornice", "ctbasepier_",
    "dplate_", "annexframe_", "annexback", "opcabtop", "stairfoot_",
    # revision 1
    "sgpost_", "sgtie", "drum", "dw", "bullhub", "bullframe_", "leg_", "grate_svcplat",
    "grate_pcat", "grate_mplat",
    # revision 2: foot sheaves + beam, the dish / panel / anemometer arm (the mast itself stays: LOD2_KEEP)
    "fs", "ant", "mastarm")
# Kept on purpose at LOD2: the caps over the open-topped ground shells (stonecap, annexcap,
# trenchcap, plinthcap - without them the shells read hollow from above), the glazing (the
# hall walls), footings, and on the top the braces + decks + saddle girders + bullwheel rim +
# crane beam that carry the Jackson Hole silhouette. On the base the saddle girders, outriggers
# and the open-end dressing go at LOD2 (the roof hides them from above).
LOD2_DELETE = {"base": LOD2_COMMON + ("brace_", "grate_", "sgirder_", "sportal_", "outrigger_",
                                      "vstrut_", "vpier_", "trenchwall_",
                                      "pictoplinth", "mplat",
                                      # nitpick B: the front canopy under the roof eave (0.75 m of it seen from
                                      # above) and its V's
                                      "jhcanopy", "jhroof", "jhstrut_",
                                      # nitpick B2: the ramp's columns and ledger, the paving and the side-area door
                                      # go at LOD2 (the landings with LOD2_COMMON's landing_); the two decks stay:
                                      # the switchback's mass in the silhouette, and the LOD2 bounds keep the new
                                      # cell (deleting the decks too drifted them 8.0 m: the lod-bounds WARN)
                                      "rampcol_", "rampbeam_", "plaza", "annexdoor"),
               # top: the maintenance-stair stringers and the deck-leg footings go too, so the
               # 460-tri decimation has less to chew (the sloped stairslab_ keeps both flights)
               "top": LOD2_COMMON + ("stairstr_m", "footing_l",
                                     # revision 1c: the -X stair keeps its sloped slabs at LOD2
                                     "stairstr_w", "grate_gang",
                                     # nitpick B2: the ramps keep their decks and pads at LOD2 (the photo-05 read)
                                     "rampcol_", "grate_headR",
                                     # revision 3: the lattice bents and their pad footings go at LOD2 (sub-pixel members);
                                     # the foundation block and its slab stay: the plinth (min y -12) on every LOD, as L4
                                     "lat")}
# survivors asserted by the tests (contract 15 T5, adapted to the <= 500 LOD2)
LOD2_KEEP = {"base": ("stonebase", "stonecap", "col_", "girderface", "glazepanel_", "roof",
                      "annexbase", "annexcap", "annexroof", "opcab", "ctbase", "ctshaft", "ctcap",
                      "stairstr_"),
             "top": ("plinth", "plinthcap", "footing_", "strut_", "brace_", "headbeam_",
                     "headgirder_", "grate_bay", "hut", "bullrim", "sgirder_", "goalbeam",
                     "goalpost_", "pad_" if RAMPS else "stairstr_", "mast")      # nitpick B2: the pads, not the stair
                     + (("found_block", "found_slab") if PLINTH_TOP else ())}   # revision 3: the plinth at LOD2


# grating pitch per station (design S1 / 5.7 / 6.5: at 0.06 m the bars were 1.2-1.5 texels apart, under
# cs2_compose's 2-texel floor, so every grating rendered flat). Detail pass 1: 0.09 (base) / 0.11 (top)
# = ~2.2 texels at the shipped w = 1 density. The bars are 1 texel: drawn, not crisp (skeptic note).
GRATE_PITCH = {"base": 0.09, "top": 0.11}


def detail_specs(role=None):
    D = cs2_compose.DetailSpec
    steel = dict(seams_m=1.2, bolt_pitch_m=0.15, bolt_r_m=0.018)
    grate_m = GRATE_PITCH[role] if (DETAIL1 and role in GRATE_PITCH) else 0.06
    return {
        "col_": D(seam_axes="rings", **steel), "girderface": D(**steel),
        "strut_": D(seam_axes="rings", **steel), "headbeam_": D(**steel),
        "headgirder_": D(**steel),
        "roof": D(corrugation_m=0.50, corr_depth_m=0.010),
        "annexroof": D(corrugation_m=0.50, corr_depth_m=0.010),
        "hutroof": D(corrugation_m=0.50, corr_depth_m=0.010),
        "ctcap": D(corrugation_m=0.50, corr_depth_m=0.010),
        "glazepanel_": D(seams_m=1.25, seam_depth_m=0.010),
        "hut": D(corrugation_m=0.076, corr_depth_m=0.002),
        "hutwin": D(), "hutroof_": D(),
        "cwshaft_": D(seams_m=3.0),
        "grate_": D(grate_m=grate_m), "grate_bayc_lip": D(), "grate_bayL_lip": D(),
        "grate_bayR_lip": D(), "grate_mplat_lip": D(), "grate_pcat_lip": D(),
        "grate_svcplat_lip": D(), "grate_gangL_lip": D(),
        "dplate_": D(seams_m=0.12, seam_depth_m=0.002),
        "door_": D(seams_m=0.30, seam_depth_m=0.003, seam_axes="rings"),
        "door_annex_leaf": D(), "door_stairin_leaf": D(),
        "rope_": D(helix_m=0.10),
        "girderroof_": D(), "deck_": D(), "brace_": D(),
        # revision 1
        "sgirder_": D(**steel), "goalbeam": D(**steel), "goalpost_": D(seam_axes="rings", **steel),
        "plinthface": D(seams_m=1.20, seam_depth_m=0.006),
        # revision 2 (D1, JH-11: the top deck is solid blue-gray steel plate on a ~1.2 m seam grid, grating
        # only at the stair apron; SB-11 / 13 non-slip berth plates): the four big deck faces read as plate
        # (their exact `_lip` entries above keep the lips plain; the service platform stays grating, JH-12).
        # Side effect: the 0.11 m bar pattern (~2.3 texels, a grazing-angle shimmer) leaves those faces
        "grate_bayc": D(seams_m=1.20, seam_depth_m=0.004), "grate_bayL": D(seams_m=1.20, seam_depth_m=0.004),
        "grate_bayR": D(seams_m=1.20, seam_depth_m=0.004), "grate_gangL": D(seams_m=1.20, seam_depth_m=0.004),
        # revision 2 (ruling Q2, SB-11 / 36): the top's 2.2 m gate leaves read perforated (a painted bar relief
        # at the grating pitch; the leaves stay opaque - pipeline cutouts flip the whole surface)
        **({"gate_": D(grate_m=grate_m)} if role == "top" else {}),
        # nitpick B: the front canopy's roof sheet is the hall roof's corrugated metal
        **({"jhroof": D(corrugation_m=0.50, corr_depth_m=0.010)} if (JH_DETAILS and role == "base") else {}),
        # nitpick B2: the ramps and their landings are grating (photo 05's grated ramp deck), the plaza paving reads as
        # 1.2 m concrete slabs, the +X head grating's lip stays plain like the others
        **({"ramp_": D(grate_m=grate_m), "landing_turn": D(grate_m=grate_m), "landing_rtop": D(grate_m=grate_m),
            "landing_balc": D(grate_m=grate_m), "plaza_": D(seams_m=1.20, seam_depth_m=0.004),
            "grate_headR_lip": D()} if RAMPS else {}),
    }


# --------------------------------------------------------------------------------------
# Assembly
# --------------------------------------------------------------------------------------

def clear_scene():
    if bpy.context.object is not None and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.cameras, bpy.data.lights,
                 bpy.data.images):
        for item in list(coll):
            if item.users == 0:
                coll.remove(item)


def build_parts(role, opts):
    check_constants(role)
    check_wheels(role, bool(getattr(opts, "wheels", True)))     # C20 (revision 2) before any geometry
    k = Builder(role)
    meta = build_base(k, opts) if role == "base" else build_top(k, opts)
    if role == "top" and getattr(opts, "skirt", 0.0):
        build_skirt(k, float(opts.skirt))
    return k, meta


def materials_for(role, finish, parts):
    """material_table plus the dressing's material keys (cs2_liftdress.DRESS_MATERIALS). On a finish
    build cs2_liftdress.merge_materials also checks that a key both tables carry (galv) has one look;
    the blockout's flat galv is a deliberate stand-in, so there the keys are only added."""
    t = material_table(role, finish)
    keys = sorted({str(m) for _o, m in parts})
    extra = [k_ for k_ in keys if k_ not in t]
    if not extra and not finish:
        return t
    LD = _LD()
    if finish:
        return LD.merge_materials(t, keys)
    for k_ in extra:
        t[k_] = dict(LD.DRESS_MATERIALS[k_])
    return t


def check_no_duplicate_parts(parts):
    """Two parts with the same vertex set would be welded into one by remove_doubles and one
    part tag would vanish (store_part_table raises later, less clearly): refuse early."""
    from mathutils import Matrix
    seen = {}
    for obj, _m in parts:
        mw = Matrix.LocRotScale(obj.location, obj.rotation_euler, obj.scale)
        pts = sorted(tuple(round(c, 4) for c in (mw @ v.co)) for v in obj.data.vertices)
        key = hashlib.sha1(repr(pts).encode()).hexdigest()
        if key in seen:
            raise RuntimeError(f"parts {seen[key]!r} and {obj.name!r} are coincident duplicates")
        seen[key] = obj.name


def apply_bevels(parts, role):
    pre = BEVEL[role]
    for obj, _m in parts:
        if obj.name.startswith(BEVEL_EXCLUDE) or not obj.name.startswith(pre):
            continue
        mod = obj.modifiers.new("Bevel", type="BEVEL")
        mod.width = 0.02
        mod.segments = 1
        mod.limit_method = "ANGLE"
        mod.angle_limit = math.radians(45.0)
        mod.use_clamp_overlap = True
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=mod.name)


def assemble(role, opts, finish, inject=None):
    """Build -> (bevels) -> density -> part tags -> material sources -> join -> weld ->
    part table -> smart-project UVMap -> shade -> origin -> orient_for_net.
    `inject(builder)` (tests only) adds parts after the build, to prove a contract fails.
    Returns (obj, table, sources, meta)."""
    import cs2_bake
    k, meta = build_parts(role, opts)
    if inject is not None:
        inject(k)
    parts = k.parts
    check_no_duplicate_parts(parts)
    if finish:
        apply_bevels(parts, role)
    cs2_uv.set_density(parts, density_weights(role))
    table = cs2_asset.tag_parts(parts)
    sources = cs2_bake.material_sources(materials_for(role, finish, parts), parts)
    bpy.ops.object.select_all(action="DESELECT")
    for obj, _m in parts:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = parts[0][0]
    bpy.ops.object.join()
    t = bpy.context.active_object
    me = t.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=0.0005)
    bm.to_mesh(me)
    bm.free()
    cs2_asset.store_part_table(t, table)
    me.uv_layers.active = me.uv_layers["UVMap"]
    bpy.ops.object.select_all(action="DESELECT")
    t.select_set(True)
    bpy.context.view_layer.objects.active = t
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66.0), island_margin=0.0)
    bpy.ops.object.mode_set(mode="OBJECT")
    # smart_project rotates islands freely: the painted labels came out 90 deg left (verdict
    # 10). Re-project every label face upright before the pack copies the layer (C13).
    orient_label_uvs(t, table, "UVMap")
    cs2_asset.shade_by_angle(t, SHADE_ANGLE)
    bpy.context.scene.cursor.location = (0.0, 0.0, 0.0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    cs2_asset.orient_for_net(t)
    dx = frame_dx(role)
    if dx:
        # nitpick B2: the design frame -> the prefab frame (oriented: prefab X = Blender x); every contract below runs
        # on the prefab-frame mesh against pf()-shifted table numbers
        from mathutils import Matrix
        t.data.transform(Matrix.Translation((dx, 0.0, 0.0)))
        t.data.update()
    t.name = t.data.name = f"Tram{role.capitalize()}Joined"
    print(f"[tram] {role}: assembled {len(table)} parts, {cs2_asset.tri_count(t)} tris")
    return t, table, sources, meta


def geometry_hash(t):
    """Rounded vertex coords + polygon vertex lists + part ids + material indices + UVMap."""
    me = t.data
    h = hashlib.sha1()
    co = np.empty(len(me.vertices) * 3, np.float32)
    me.vertices.foreach_get("co", co)
    h.update(np.round(co.astype(np.float64), 4).tobytes())
    for p in me.polygons:
        h.update((",".join(str(i) for i in p.vertices) + f"/{p.material_index};").encode())
    part = np.empty(len(me.polygons), np.int32)
    me.attributes["part"].data.foreach_get("value", part)
    h.update(part.tobytes())
    uv = np.empty(len(me.loops) * 2, np.float32)
    me.uv_layers["UVMap"].data.foreach_get("uv", uv)
    h.update(np.round(uv.astype(np.float64), 5).tobytes())
    return h.hexdigest()[:16], len(me.vertices), len(me.polygons)


# --------------------------------------------------------------------------------------
# Prefab-frame mesh access (oriented object: prefab X = x, Y = z, Z = -y)
# --------------------------------------------------------------------------------------

def mesh_arrays(obj):
    me = obj.data
    mw = np.array(obj.matrix_world, dtype=np.float64)
    n = len(me.vertices)
    co = np.empty(n * 3, np.float32)
    me.vertices.foreach_get("co", co)
    w = co.reshape(n, 3).astype(np.float64) @ mw[:3, :3].T + mw[:3, 3]
    pf = np.stack([w[:, 0], w[:, 2], -w[:, 1]], axis=1)          # prefab X, Y, Z
    npoly = len(me.polygons)
    part = np.zeros(npoly, np.int32)
    if me.attributes.get("part") is not None:
        me.attributes["part"].data.foreach_get("value", part)
    table = json.loads(me.get(cs2_asset.PART_TABLE_PROP, "[]"))
    return pf, part, table


def poly_vertices(me):
    start = np.empty(len(me.polygons), np.int32)
    total = np.empty(len(me.polygons), np.int32)
    me.polygons.foreach_get("loop_start", start)
    me.polygons.foreach_get("loop_total", total)
    lv = np.empty(len(me.loops), np.int32)
    me.loops.foreach_get("vertex_index", lv)
    return start, total, lv


def part_names_of_polys(part, table):
    return [table[p - 1] if 0 < p <= len(table) else "?" for p in part]


def triangles(obj):
    """Prefab-frame triangles (N, 3, 3) and their part names (fan split of each polygon)."""
    pf, part, table = mesh_arrays(obj)
    me = obj.data
    start, total, lv = poly_vertices(me)
    tris, names = [], []
    pn = part_names_of_polys(part, table)
    for i in range(len(start)):
        s, c = int(start[i]), int(total[i])
        vs = lv[s:s + c]
        for j in range(1, c - 1):
            tris.append((vs[0], vs[j], vs[j + 1]))
            names.append(pn[i])
    tri = pf[np.array(tris, dtype=np.int64)] if tris else np.zeros((0, 3, 3))
    return tri, names


def _tri_box_overlap(tri, lo, hi):
    """Vectorized separating-axis test (Akenine-Moeller) of triangles (N,3,3) vs one AABB."""
    c = (lo + hi) / 2.0
    h = (hi - lo) / 2.0
    v = tri - c
    e = [v[:, 1] - v[:, 0], v[:, 2] - v[:, 1], v[:, 0] - v[:, 2]]
    ok = np.ones(len(tri), dtype=bool)
    # box face normals
    for a in range(3):
        mn, mx = v[:, :, a].min(axis=1), v[:, :, a].max(axis=1)
        ok &= ~((mn > h[a]) | (mx < -h[a]))
    # triangle normal
    n = np.cross(e[0], e[1])
    d = np.einsum("ij,ij->i", n, v[:, 0])
    r = np.abs(n) @ h
    ok &= ~(np.abs(d) > r)
    # 9 edge cross axes
    for ed in e:
        for a in range(3):
            ax = np.zeros_like(ed)
            ax[:, (a + 1) % 3] = -ed[:, (a + 2) % 3]
            ax[:, (a + 2) % 3] = ed[:, (a + 1) % 3]
            p = np.einsum("ijk,ik->ij", v, ax)
            rr = np.abs(ax) @ h
            ok &= ~((p.min(axis=1) > rr) | (p.max(axis=1) < -rr))
    return ok


# --------------------------------------------------------------------------------------
# Contracts C1-C8, C11, C12 and the R30 carrier clear
# --------------------------------------------------------------------------------------

class ContractError(RuntimeError):
    pass


def _fail(code, msg):
    raise ContractError(f"{code}: {msg}")


def carrier_zones(role=None):
    """Axis-aligned clear zones per lane (label, lo, hi, allowed prefixes), from the BUILT
    tram cabin (build/lift_cabin.md: mirror-symmetric fork, half-width 1.080 above the fin,
    haul grips at dy +-0.50 / dz -0.40, carriage ~1.2 wide above the rope; R29 identity
    hosting in both bays). R30's band rules hold a fortiori: nothing of the station is inside
    +-1.10 of a lane between the cabin roof and the carriage top in the docking path.
    Nitpick B2: in the PREFAB frame of `role` (the lanes at pf(role) of +-TRACK_X)."""
    zones = []
    zb = DOCK_Z + CABIN_HALF_L + 0.30          # cabin body ends (z)
    zr = DOCK_Z + 0.60                         # hanger ring (cabin center) ends
    zf = DOCK_Z + CARRIAGE_HALF_L + 0.20       # carriage + haul grips end
    dx = frame_dx(role) if role else 0.0
    for s_ in (-1, 1):
        xl = s_ * TRACK_X + dx
        tag = "L" if s_ < 0 else "R"
        hw = CABIN_HALF_W + 0.15
        zones.append((f"body{tag}", (xl - hw, PIVOT_Y - 0.30, -12.5),
                      (xl + hw, PIVOT_Y + CABIN_BODY_H + 0.60, zb), ()))
        zones.append((f"ring{tag}", (xl - CARRIAGE_HALF_W, PIVOT_Y + CABIN_BODY_H + 0.60, -12.5),
                      (xl + CARRIAGE_HALF_W, CARRIAGE_TOP_Y + 0.20, zr), ("rope_",)))
        zones.append((f"carriage{tag}", (xl - 0.65, TRACK_Y - 0.50, -12.5),
                      (xl + 0.65, CARRIAGE_TOP_Y + 0.20, zf), ("rope_",)))
    return zones


def check_contracts(t, role, opts, report):
    """C1-C5, C7, C8 on the joined, oriented LOD0 (before the `_Win` split)."""
    pf, part, table = mesh_arrays(t)
    me = t.data
    start, total, lv = poly_vertices(me)
    names = part_names_of_polys(part, table)
    vert_part = {}
    for i in range(len(start)):
        for v in lv[int(start[i]):int(start[i]) + int(total[i])]:
            vert_part.setdefault(int(v), set()).add(names[i])
    rope_v = np.array([all(n.startswith("rope_") for n in vert_part.get(v, {"?"}))
                       for v in range(len(pf))])
    # C1 face rule. Nitpick B2: the rule keeps the LINE CORRIDOR free (1.60 m of air line-ward of the face for the
    # stubs' first span); on the RAMPS base the new cell beyond the annex's +X face (design x > C1_CORRIDOR_X, >= 14.5 m
    # from the nearest rope) is outside it: the switchback's turn landing and the paving reach the line-side lot edge
    face = ~rope_v
    if RAMPS and role == "base":
        face &= np.abs(pf[:, 0] - frame_dx(role)) <= C1_CORRIDOR_X + 1e-6
    zmin = pf[face, 2].min()
    if zmin < FACE_Z - 0.001:
        bad = [n for v in np.where((pf[:, 2] < FACE_Z - 0.001) & face)[0][:5]
               for n in vert_part.get(int(v), ())]
        _fail("C1", f"non-rope geometry at z {zmin:.3f} < FACE_Z {FACE_Z} ({sorted(set(bad))})")
    report["C1"] = f"non-rope z min {zmin:.3f} >= {FACE_Z}" + (
        f" (the line corridor |x - axis| <= {C1_CORRIDOR_X}; the RAMPS new cell outside it)"
        if RAMPS and role == "base" else "")

    # C2 stub coincidence + C3 dock (rope part vertex clusters)
    def verts_of(prefix):
        idx = set()
        for i in range(len(start)):
            if names[i].startswith(prefix):
                idx.update(int(v) for v in lv[int(start[i]):int(start[i]) + int(total[i])])
        return np.array(sorted(idx), dtype=np.int64)
    stubs = stub_table(role)
    dx = frame_dx(role)
    measured = []
    for st in stubs:
        side = "L" if st["a"][0] < dx else "R"          # nitpick B2: the side of the line axis (prefab x dx)
        prefix = "rope_track_" + side if st["kind"] == "track" else "rope_haul"
        vs = verts_of(prefix)
        if not len(vs):
            _fail("C2", f"no rope part {prefix}")
        P_ = pf[vs]
        sel = (np.abs(P_[:, 2] - STUB_Z) < 0.002) & (np.sign(P_[:, 0] - dx) == np.sign(st["a"][0] - dx))
        if sel.sum() < 4:
            _fail("C2", f"{prefix}: no end ring at z {STUB_Z} on side {side}")
        cen = P_[sel].mean(axis=0)
        err = float(np.linalg.norm(cen - np.array(st["a"])))
        if err > 0.001:
            _fail("C2", f"{prefix} end ring centre {np.round(cen, 4)} vs stub a {st['a']} "
                        f"(err {err:.4f})")
        measured.append({"lane": st["kind"], "a": st["a"], "ring_centre": [round(float(c), 4)
                                                                             for c in cen]})
    report["C2"] = measured
    for s in (-1, 1):
        prefix = "rope_track_" + ("L" if s < 0 else "R")
        P_ = pf[verts_of(prefix)]
        sel = (P_[:, 2] >= STUB_Z - 0.01) & (P_[:, 2] <= DOCK_Z + CARRIAGE_HALF_L)
        dev = np.hypot(P_[sel, 0] - (s * TRACK_X + dx), P_[sel, 1] - TRACK_Y).max()
        span = (P_[sel, 2].min(), P_[sel, 2].max())
        if dev > TRACK_ROPE_R + 0.02 or not (span[0] <= DOCK_Z <= span[1]) and \
                not (P_[:, 2].min() <= DOCK_Z <= P_[:, 2].max()):
            _fail("C3", f"{prefix}: not level through the dock (dev {dev:.3f})")
    report["C3"] = f"track ropes level at y {TRACK_Y} through dock z {DOCK_Z}"

    # C4 envelopes (vertex + triangle SAT test)
    tri, tnames = triangles(t)
    tn = np.array(tnames)
    hits = []
    for label, lo, hi, allowed in carrier_zones(role):
        lo = np.array(lo) + 0.05
        hi = np.array(hi) - 0.05
        cand = np.ones(len(tri), dtype=bool)
        for pre in allowed:
            cand &= ~np.char.startswith(tn, pre)
        if not cand.any():
            continue
        ov = _tri_box_overlap(tri[cand], lo, hi)
        if ov.any():
            hits.append((label, sorted(set(tn[cand][ov].tolist()))[:6]))
    if hits:
        _fail("C4", f"carrier clear violated: {hits}")
    report["C4"] = f"{len(carrier_zones(role))} carrier zones clear (R30 + cabin body)"

    # C5 boarding level
    deck_pre = ("deck_", "edgestrip_") if role == "base" else ("grate_bay",)
    edges = {round(x + dx, 6): 0 for x in (-7.80, -3.20, 3.20, 7.80)}     # nitpick B2: prefab x, rounded
    nrm = np.empty(len(me.polygons) * 3, np.float32)
    me.polygons.foreach_get("normal", nrm)
    nrm = nrm.reshape(-1, 3)
    for i in range(len(start)):
        if not names[i].startswith(deck_pre) or names[i].endswith("_lip"):
            continue
        if nrm[i, 2] < 0.9:                       # Blender z = prefab Y
            continue
        vs = lv[int(start[i]):int(start[i]) + int(total[i])]
        xs = pf[vs, 0]
        near = [x for x in edges if np.any(np.abs(xs - x) < 0.06)]
        if not near:
            continue
        ys = pf[vs, 1]
        if np.abs(ys - DECK_Y).max() > 0.01:
            _fail("C5", f"{names[i]} top at y {ys.mean():.3f} != deck {DECK_Y}")
        for x in near:
            edges[x] += 1
    if not all(edges.values()):
        _fail("C5", f"bay edges without a deck face at the boarding level: {edges}")
    report["C5"] = f"all 4 bay edges have deck tops at y {DECK_Y} (cabin interior floor) {edges}"

    # C7 lot fit, STRICT (review F1: the +0.5 tolerance hid a 0.20 m clock-cap overhang; whether
    # the engine overlaps by mesh bounds or by the lot is unverified, so nothing leaves the lot).
    # Every vertex within the 8 m-cell lot (+1e-4 float slack); the failure names the parts.
    W, D = lot_cells(role)                  # nitpick B2: 6 x 3 on the RAMPS base (prefab frame, centered)
    lo, hi = pf.min(axis=0), pf.max(axis=0)
    hx, hz = 4.0 * W, 4.0 * D
    over = np.maximum(np.abs(pf[:, 0]) - hx, np.abs(pf[:, 2]) - hz)
    out_v = np.where(over > LOT_SLACK)[0]
    if len(out_v):
        by_part = {}
        for v in out_v:
            for n in vert_part.get(int(v), {"?"}):
                by_part[n] = max(by_part.get(n, 0.0), float(over[v]))
        worst = sorted(by_part.items(), key=lambda kv: (-kv[1], kv[0]))
        _fail("C7", f"bounds x [{lo[0]:.3f}, {hi[0]:.3f}] z [{lo[2]:.3f}, {hi[2]:.3f}] leave the "
                    f"{W}x{D} lot (x +-{hx:.0f}, z +-{hz:.0f}); overhang by part: "
                    + ", ".join(f"{n} {d:.3f}" for n, d in worst[:10])
                    + (f" (+{len(worst) - 10} more)" if len(worst) > 10 else ""))
    report["C7"] = (f"x [{lo[0]:.3f}, {hi[0]:.3f}] z [{lo[2]:.3f}, {hi[2]:.3f}] inside the {W}x{D} "
                    f"lot (x +-{hx:.0f}, z +-{hz:.0f}; strict)")
    key = (role, bool(opts.clock_tower if role == "base" else opts.wheels))
    want = HEIGHT[key]
    if abs(hi[1] - want) > HEIGHT_TOL[key]:
        _fail("C8", f"bounds max y {hi[1]:.3f} != {want:.3f} +- {HEIGHT_TOL[key]}")
    if base_mode(role) == "engine" and lo[1] < -0.001 and not getattr(opts, "skirt", 0.0):
        _fail("C6", f"geometry below y 0 ({lo[1]:.4f})")
    # revision 3: in plinth mode geometry below y 0 is the design (C6p in check_stage: no band vertex, min y -depth,
    # closed); nothing may reach deeper than the plinth
    if base_mode(role) == "plinth" and lo[1] < -PLINTH["depth"] - 0.01:
        _fail("C6p", f"plinth: geometry below the plinth depth ({lo[1]:.4f} < -{PLINTH['depth']:.2f})")
    report["C8"] = f"bounds y [{lo[1]:.3f}, {hi[1]:.3f}] (want top {want:.2f})"
    report["bounds"] = {"min": [round(float(v), 3) for v in lo],
                        "max": [round(float(v), 3) for v in hi]}


def engine_base_parts(role, opts):
    """C6 ground parts for this build (ctbase only when the clock tower is built)."""
    parts = ENGINE_BASE_PARTS[role]
    if role == "base" and not opts.clock_tower:
        parts = tuple(p for p in parts if p != "ctbase")
    if role == "top" and RAMPS:
        # nitpick B2: the end stairs' foot pads are gone; the ramps' concrete pads stand on the grade instead
        parts = tuple("pad_" if p == "stairfoot_" else p for p in parts)
    return parts


def check_engine_base(obj, parts_req, label="LOD0"):
    """C6 / C10: ground-ring verdict OK and every required ground part contributes OK edges."""
    r = cs2_validate.ground_ring_report(obj)
    ok = list(r["ok_parts"].keys())
    missing = [p for p in parts_req if not any(n.startswith(p) for n in ok)]
    print(f"[tram] engine base {label}: verdict {r['verdict']}, {r['ok_edges']} OK / "
          f"{r['blocked_edges']} blocked, below {r['below_verts']}; missing {missing}")
    if r["verdict"] != "OK" or missing or r["below_verts"]:
        raise ContractError(f"C6/C10 engine base {label}: verdict {r['verdict']}, missing "
                            f"{missing}, offending {r['offending_parts']}, below "
                            f"{r['below_verts']}")
    return r


def base_mode(role):
    """Revision 3: 'plinth' on the PLINTH_TOP top (the L4 rule: no engine base, the structure continues below grade),
    'engine' everywhere else (C6 / C10: the engine draws the base from the y 0 outline)."""
    return "plinth" if (role == "top" and PLINTH_TOP) else "engine"


PLINTH_CLOSED_SAMPLES = (0.1, 0.3, 0.5, 0.7, 0.9)    # C6p closed: points along each open edge below grade


def check_plinth(obj, label="LOD0", closed=True):
    """C6p (revision 3, plinth mode; the cs2_bldg check_base_mode("plinth") rule L4 ships, plus closure). On any LOD:
      - no engine base: ground_ring_report finds no OK base edge (the importer then builds no base ribbon);
      - no vertex within PLINTH band of y 0 (the engine band) - nothing is left ON the grade line;
      - min y == -PLINTH depth (+-0.01): the plinth depth (the prefab's bounds min y);
      - closed (`closed`): every OPEN edge (an edge with one face of its own part) of a part that reaches below grade,
        where it runs below grade, lies within PLINTH closed_tol of ANOTHER part's face - a dropped face seated on its
        support (the pier's bottom ring on the block, a ramp column on the block, the block on its slab). An open
        bottom, an open box side or a gap below grade FAILS (the footing slab closes the foundation).
    Raises ContractError ("C6p ..."); returns the report (verdict / ok_edges / blocked_edges as check_engine_base's, for
    the manifest's engine_base block)."""
    from mathutils.bvhtree import BVHTree
    r = cs2_validate.ground_ring_report(obj)
    pf, part, table = mesh_arrays(obj)
    me = obj.data
    start, total, lv = poly_vertices(me)
    names = part_names_of_polys(part, table)
    y = pf[:, 1]
    band = int((np.abs(y) <= PLINTH["band"]).sum())
    ymin = float(y.min())
    problems = []
    if r["ok_edges"]:
        problems.append(f"{r['ok_edges']} OK engine-base edges (plinth mode wants none; parts {list(r['ok_parts'])[:6]})")
    if band:
        idx = np.where(np.abs(y) <= PLINTH["band"])[0]
        on = sorted({names[i] for i in range(len(start)) if np.isin(lv[int(start[i]):int(start[i]) + int(total[i])],
                                                                     idx).any()})
        problems.append(f"{band} vertices within {PLINTH['band']} of y 0 (parts {on[:8]})")
    if abs(ymin + PLINTH["depth"]) > 0.01:
        problems.append(f"min y {ymin:.3f} != -{PLINTH['depth']:.2f}")
    rep = {"mode": "plinth", "verdict": r["verdict"], "ok_edges": r["ok_edges"], "blocked_edges": r["blocked_edges"],
           "band_vertices": band, "min_y": round(ymin, 4), "max_y": round(float(y.max()), 4), "label": label}
    if closed:
        tri_v, tri_p = [], []
        edge_parts = {}
        pid = {}
        for i in range(len(start)):
            vs = [int(v) for v in lv[int(start[i]):int(start[i]) + int(total[i])]]
            k_ = pid.setdefault(names[i], len(pid))
            for j in range(1, len(vs) - 1):
                tri_v.append((vs[0], vs[j], vs[j + 1]))
                tri_p.append(k_)
            for a, b in zip(vs, vs[1:] + vs[:1]):
                e = (min(a, b), max(a, b))
                d = edge_parts.setdefault(e, {})
                d[k_] = d.get(k_, 0) + 1
        bvh = BVHTree.FromPolygons(pf.tolist(), tri_v, all_triangles=True)
        tri_p = np.array(tri_p)
        part_min = {}
        for i in range(len(start)):
            k_ = pid[names[i]]
            part_min[k_] = min(part_min.get(k_, 1e9), float(y[lv[int(start[i]):int(start[i]) + int(total[i])]].min()))
        below_parts = {k_ for k_, m in part_min.items() if m < -PLINTH["band"]}
        inv = {v: k_ for k_, v in pid.items()}
        open_len, open_bad = {}, {}
        tol = PLINTH["closed_tol"]
        for (a, b), d in edge_parts.items():
            for k_, cnt in d.items():
                if cnt != 1 or k_ not in below_parts:
                    continue
                if min(y[a], y[b]) >= -PLINTH["band"]:
                    continue                                    # an open edge above grade (seated caps, rims): not C6p's
                pa, pb = pf[a], pf[b]
                L = float(np.linalg.norm(pb - pa))
                nm = inv[k_]
                open_len[nm] = open_len.get(nm, 0.0) + L
                for t_ in PLINTH_CLOSED_SAMPLES:
                    q = pa + (pb - pa) * t_
                    if q[1] >= -PLINTH["band"]:
                        continue
                    hits = bvh.find_nearest_range(Vector(q.tolist()), tol)
                    if not any(int(tri_p[h[2]]) != k_ for h in hits):
                        open_bad.setdefault(nm, []).append([round(float(c), 3) for c in q])
                        break
        rep["closed"] = {"open_edges_below_grade_m": {k_: round(v, 3) for k_, v in sorted(open_len.items())},
                         "uncovered": {k_: v[:3] for k_, v in sorted(open_bad.items())},
                         "parts_below_grade": sorted(inv[k_] for k_ in below_parts)}
        if open_bad:
            problems.append("open below grade (an open edge on no other part's face): " + ", ".join(
                f"{k_} at {v[0]}" for k_, v in sorted(open_bad.items())[:8]))
    print(f"[tram] C6p plinth {label}: verdict {r['verdict']} ({r['ok_edges']} OK edges), {band} band vertices, "
          f"min y {ymin:.3f}" + (f", open edges below grade seated on other parts: "
                                 f"{len(rep['closed']['open_edges_below_grade_m'])} parts, uncovered "
                                 f"{sorted(rep['closed']['uncovered'])}" if closed else ""))
    if problems:
        _fail("C6p", f"plinth {label}: " + "; ".join(problems))
    return rep


def survivor_tris(obj, table, prefixes):
    """Triangles left after deleting `prefixes` (the LOD target never exceeds what survives:
    make_lod_chain cannot up-sample and fails outside +-25 % of its target)."""
    me = obj.data
    part = np.empty(len(me.polygons), np.int32)
    me.attributes["part"].data.foreach_get("value", part)
    total = np.empty(len(me.polygons), np.int32)
    me.polygons.foreach_get("loop_total", total)
    keep = np.array([not table[p - 1].startswith(tuple(prefixes)) for p in part], dtype=bool)
    return int((total[keep] - 2).sum())


# Revision 2 (C18 LOD2, the skeptic's MC8 baseline: top LOD2 grate_bayL 5-7 back-face first hits, already in 1e):
# at the 460-tri LOD2 the decimator flattens a deck grating's two plates (0.06 apart) into ONE criss-crossed
# plane at y 6.27 - two up-facing and two down-facing triangles over the same rectangle - so from above (the only
# side seen at the LOD2 distance) the deck flickers between its top and see-through. fix_lod_decks drops a
# deck part's down-facing faces that an up-facing face of the same part covers within LOD_FLAT_TOL (a collapsed
# plate pair only: the intact plates at LOD0 / LOD1 are 0.06 apart), and turns a part's faces up when it has no
# up-facing face left.
LOD_UP_PARTS = ("grate_bay", "grate_gang", "grate_mplat", "plinthcap", "ramp_")      # ramp_: nitpick B2
LOD_FLAT_TOL = 0.01


def fix_lod_decks(lod):
    """The LOD deck fix-up above; re-shades the LOD when anything changed. Returns {part: {"dropped", "flipped"}}."""
    pf, part, table = mesh_arrays(lod)
    me = lod.data
    start, total, lv = poly_vertices(me)
    names = part_names_of_polys(part, table)
    polys, ny = [], []
    for i in range(len(start)):
        vs = lv[int(start[i]):int(start[i]) + int(total[i])]
        P = pf[vs]
        n = np.zeros(3)
        for j in range(1, len(vs) - 1):
            n += np.cross(P[j] - P[0], P[j + 1] - P[0])
        L = np.linalg.norm(n)
        ny.append(n[1] / L if L > 0 else 0.0)
        polys.append(P)
    ny = np.array(ny)

    def covered(i, ups):
        c = polys[i].mean(axis=0)
        for j in ups:
            Q = polys[j]
            if abs(float(Q[:, 1].mean()) - c[1]) > LOD_FLAT_TOL:
                continue
            for k_ in range(1, len(Q) - 1):
                a, b, d = Q[0], Q[k_], Q[k_ + 1]
                den = (b[2] - d[2]) * (a[0] - d[0]) + (d[0] - b[0]) * (a[2] - d[2])
                if abs(den) < 1e-12:
                    continue
                l1 = ((b[2] - d[2]) * (c[0] - d[0]) + (d[0] - b[0]) * (c[2] - d[2])) / den
                l2 = ((d[2] - a[2]) * (c[0] - d[0]) + (a[0] - d[0]) * (c[2] - d[2])) / den
                if l1 >= -1e-6 and l2 >= -1e-6 and l1 + l2 <= 1 + 1e-6:
                    return True
        return False
    drop, flip, out = [], [], {}
    for nm in sorted({n_ for n_ in names if n_.startswith(LOD_UP_PARTS)}):
        idx = [i for i, n_ in enumerate(names) if n_ == nm]
        ups = [i for i in idx if ny[i] > 0.5]
        downs = [i for i in idx if ny[i] < -0.5]
        if not ups and downs:
            flip += downs
            out[nm] = {"flipped": len(downs)}
        elif ups:
            d_ = [i for i in downs if covered(i, ups)]
            if d_:
                drop += d_
                out[nm] = {"dropped": len(d_)}
    if not out:
        return {}
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    if flip:
        bmesh.ops.reverse_faces(bm, faces=[bm.faces[i] for i in flip])
    if drop:
        bmesh.ops.delete(bm, geom=[bm.faces[i] for i in drop], context="FACES")
    bm.to_mesh(me)
    bm.free()
    me.update()
    cs2_asset.shade_by_angle(lod, SHADE_ANGLE, weighted=False)     # as make_lod_chain
    return out


def pin_ground(obj, eps=0.02):
    """Snap LOD ground vertices (|z| < eps) back to z 0 after decimation (contract C10)."""
    me = obj.data
    co = np.empty(len(me.vertices) * 3, np.float32)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    sel = np.abs(co[:, 2]) < eps
    moved = int((sel & (co[:, 2] != 0.0)).sum())
    co[sel, 2] = 0.0
    me.vertices.foreach_set("co", co.ravel())
    me.update()
    return moved


def split_windows(t, name, table, cells):
    """`_Win` sub-mesh: one cs2_submesh.split_submesh per pane group (own room cell), joined
    (contract 8.3; lift_terminal.split_windows parameterized)."""
    groups = [(pfx, cell) for pfx, cell in cells.items() if any(n.startswith(pfx) for n in table)]
    if not groups:
        return None
    subs = []
    for pfx, cell in groups:
        sub = cs2_submesh.split_submesh(t, table, (pfx,), "Win", win_cell=cell)
        sub.name = f"{name}Win{len(subs)}"
        sub.data.name = sub.name
        subs.append(sub)
    main_sub = subs[0]
    if len(subs) > 1:
        bpy.ops.object.select_all(action="DESELECT")
        for s in subs:
            s.select_set(True)
        bpy.context.view_layer.objects.active = main_sub
        bpy.ops.object.join()
        main_sub = bpy.context.active_object
    main_sub.name = cs2_submesh.submesh_name(name, "Win")
    main_sub.data.name = main_sub.name
    return main_sub


def check_win_cells(win, role):
    got = set(cs2_validate._win_cells(win.data).keys())
    want = {row * 5 + col for col, row in WIN_CELLS[role].values()}
    if got != want:
        _fail("C11", f"_Win room ids {sorted(got)} != {sorted(want)}")
    return sorted(got)


def _label_polys(me, table):
    """{label part name: [polygon]} for every LABEL_PREFIXES part (sorted names)."""
    part = np.empty(len(me.polygons), np.int32)
    me.attributes["part"].data.foreach_get("value", part)
    ids = {i + 1: n for i, n in enumerate(table) if n.startswith(LABEL_PREFIXES)}
    out = {n: [] for n in sorted(ids.values())}
    for p in me.polygons:
        n = ids.get(int(part[p.index]))
        if n is not None:
            out[n].append(p)
    return out


def _label_frame(obj, poly):
    """World (right, up) of a vertical label face as seen by a viewer facing it: up = world
    +Z (Blender; prefab +Y before and after orient_for_net), right = up x outward normal."""
    mw = obj.matrix_world
    n = (mw.to_3x3() @ poly.normal).normalized()
    up = Vector((0.0, 0.0, 1.0))
    r = up.cross(n)
    if r.length < 1e-6:
        raise ContractError(f"C13: label face {poly.index} is horizontal")
    r.normalize()
    pts = [mw @ obj.data.vertices[v].co for v in poly.vertices]
    return r, up, pts


def orient_label_uvs(obj, table, layer="UVMap"):
    """Re-project every label face's UVs to its upright frame (u = viewer's right, v = up),
    keeping the island's UV center and area. smart_project rotates islands freely and the
    painters paint v-up (the pictogram came out rotated 90 deg left, verdict 10). The unique
    pack never rotates (cs2_uv rotate=False), so the shipped layout stays upright (C13)."""
    me = obj.data
    uvl = me.uv_layers[layer]
    n_faces = 0
    for _name, polys in _label_polys(me, table).items():
        for p in polys:
            r, up, pts = _label_frame(obj, p)
            loops = list(p.loop_indices)
            uv = [Vector(uvl.data[l].uv) for l in loops]
            s = [q.dot(r) for q in pts]
            t = [q.dot(up) for q in pts]
            m = len(loops)
            a_uv = abs(sum(uv[i].x * uv[(i + 1) % m].y - uv[(i + 1) % m].x * uv[i].y
                           for i in range(m))) / 2.0
            a_st = abs(sum(s[i] * t[(i + 1) % m] - s[(i + 1) % m] * t[i] for i in range(m))) / 2.0
            kk = math.sqrt(a_uv / a_st) if a_uv > 0.0 and a_st > 0.0 else 0.01
            cu, cv = sum(v.x for v in uv) / m, sum(v.y for v in uv) / m
            cs, ct = sum(s) / m, sum(t) / m
            for l, si, ti in zip(loops, s, t):
                uvl.data[l].uv = (cu + kk * (si - cs), cv + kk * (ti - ct))
            n_faces += 1
    return n_faces


def check_label_uvs(obj, table, layer, tol=0.02):
    """C13: every painted label reads upright and unmirrored on `layer`: per face the UV
    Jacobian against the face's (right, up) frame is a positive uniform scale (du/ds, dv/dt
    > 0; cross terms and the scale mismatch < tol). Returns {label: worst deviation}."""
    me = obj.data
    uvl = me.uv_layers[layer]
    res = {}
    for name, polys in _label_polys(me, table).items():
        if not polys:
            _fail("C13", f"label {name} has no faces")
        worst = 0.0
        for p in polys:
            r, up, pts = _label_frame(obj, p)
            S = np.array([[q.dot(r), q.dot(up), 1.0] for q in pts], dtype=np.float64)
            U = np.array([tuple(uvl.data[l].uv) for l in p.loop_indices], dtype=np.float64)
            J = np.linalg.lstsq(S, U, rcond=None)[0]
            a, b, c, d = J[0, 0], J[1, 0], J[0, 1], J[1, 1]     # du/ds du/dt dv/ds dv/dt
            kk = math.sqrt(abs(a * d - b * c))
            if kk <= 0.0 or a <= 0.0 or d <= 0.0:
                _fail("C13", f"{name} on {layer}: not upright / mirrored (du/ds {a:.3g}, "
                             f"dv/dt {d:.3g}, du/dt {b:.3g}, dv/ds {c:.3g})")
            dev = max(abs(b), abs(c), abs(a - d)) / kk
            if dev > tol:
                _fail("C13", f"{name} on {layer}: rotated / skewed by {dev:.3f} (du/ds {a:.3g}, "
                             f"dv/dt {d:.3g}, du/dt {b:.3g}, dv/ds {c:.3g})")
            worst = max(worst, dev)
        res[name] = round(worst, 5)
    return res


# --------------------------------------------------------------------------------------
# C14 part-against-part clashes (revision 1b; revision1-check-tram-stations F1: the R1 top's
# bullwheel A-frame leg ran through the operator hut and every other contract passed)
# --------------------------------------------------------------------------------------
# Declared contacts by part KIND (the leading letters of a part name: post_cap_3 -> post,
# sgpost_R0 -> sgpost, drip_hutroof_nx -> drip). Two parts may share triangles (touch or
# interpenetrate) only when their kinds are declared here - designed joints, kits, feet on
# their decks, ropes in their grooves. Measured on base / base --no-clock-tower / top /
# top --no-wheels, blockout and finish (revision 1b). Anything else that intersects FAILS,
# and so does a part wholly inside another part's closed volume (no triangle pair there).
CONTACTS = {
    "railing kit (posts, rails, kick plates, medallions)":
        "post:rail kick:post kick:kick rail:rail medal:post",
    "railings and bay gates on decks / landings / stairs, and where they meet members":
        "grate:post deck:post plinthcap:post landing:post post:stairstr post:stairfoot "
        "edgestrip:post grate:kick kick:landing kick:stairstr kick:pcathanger pcathanger:rail "
        "kick:strut rail:strut gatepost:grate",
    "stair kits (stringers, treads, slabs, landings, posts, feet) and their seats":
        "stairstr:tread stairslab:stairstr stairfoot:stairstr stairfoot:tread stairpost:stairstr "
        "landing:stairpost landing:stairstr landing:tread deckbeam:stairstr deckbeam:tread "
        "deckbeam:landing grate:landing col:landing landing:stonepier stairstr:stonepier "
        "stairfoot:stonepier",
    "ladder kit (rails, rungs, top brackets) on its deck and at the catwalk lip (revision 1c)":
        "ladder:ladder ladder:lrung deck:ladder grate:ladder",
    "door / storefront kits in their walls and on their decks":
        "door:door sfront:sfront sfront:spandrel glazepanel:sfront glazepanel:spandrel "
        "deck:sfront deck:door door:hut door:opcab door:opcabfill door:stonebase door:plinthcap",
    "drip flashings on their roofs": "drip:drip drip:hutroof drip:opcabroof annexroof:drip",
    "base glazed hall walls (columns, mullions, transoms, panels)":
        "col:glazepanel glazepanel:transom glazepanel:glazepanel mullion:transom "
        "transom:transom col:transom mullion:stonecap mullion:trenchwall glazepanel:stonecap "
        "girderface:glazepanel glazepanel:lamp col:spandrel",
    "base hall frame and roof":
        "brace:brace brace:col col:deck col:girderface col:girderroof col:outrigger "
        "col:portalknee col:roof col:stonecap col:stonepier col:trackrail girderface:portalknee "
        "girderface:pcathanger girderroof:roof outrigger:purlin fascia:fascia fascia:roof "
        "pcathanger:roof grate:pcathanger lamp:roof",
    "base annex":
        "annexback:annexcap annexback:annexroof annexback:annextransom annexback:stonepier "
        "annexcap:annexframe annexcap:ctbasepier annexcap:stonepier annexframe:annexroof "
        "annexframe:annextransom annextransom:annextransom",
    "base clock tower":
        "ctbase:ctbasepier ctbasepier:ctshaft ctcap:ctcapspire ctcap:ctface ctcap:ctstage "
        "ctcapspire:ctfinial ctcornice:ctface ctcornice:ctshaft ctcornice:ctstage",
    "base entrance (canopy, V-struts, wall lamps, pictogram wall)":
        "canopyroof:entrycanopy canopyroof:stonebase canopyroof:stonepier entrycanopy:stonebase "
        "entrycanopy:vstrut vpier:vstrut vstrut:vstrut lamp:stonebase pictowall:stonebase",
    "base masonry, decks, pit, counterweight shafts":
        "stonebase:stonepier stonebase:trenchcap stonebase:trenchwall stonecap:stonepier "
        "stonecap:trenchwall plinthface:stonebasefront plinthface:trenchcap deck:stonecap "
        "deck:deck deck:edgestrip edgestrip:stonecap edgestrip:gatepost deck:gatepost "
        "deck:trenchwall deck:drivehouse deck:opcab deck:sportal sportal:sportal cwlid:cwshaft "
        "trenchcap:trenchwall",
    "base open end (horns, platform rails, moving-platform kit)":
        "horn:hornbase horn:hornstrut horn:trenchcap trackrail:trenchwall mplatbase:mplatbrace "
        "mplatbase:mplatpost mplatbase:mplatwheel mplatbrace:mplatbrace mplatbrace:mplatpost "
        "mplatbrace:mplattop mplatpost:mplattop grate:mplattop",
    "base operator cabin": "opcab:opcabpost opcabfill:opcabtop opcabpost:opcabtop",
    "ropes seated in grooves, on saddles and drums, into the counterweight shafts":
        "rope:sgirder drumrim:rope drumspoke:rope dwrim:rope bullrim:rope bwrim:rope bwspoke:rope "
        "cwlid:rope cwshaft:rope",
    "wheel kits (rims, spokes, hubs, axles, shafts, frames)":
        "bullaxle:bullframe bullaxle:bullhub bullframe:bullframe bullhub:bullspoke bwhub:bwshaft "
        "bwhub:bwspoke bwrim:bwspoke bwshaft:drivehouse bwshaft:plinthcap drumhub:drumspoke "
        "drumhub:plinth drumrim:drumspoke drumspoke:drumspoke dwaxle:dwbeam dwaxle:dwhub "
        "dwbeam:dwpost dwhub:dwspoke",
    "top feet on the plinth cap, gratings and footings":
        "bullframe:plinthcap dwpost:plinthcap goalpost:plinthcap sgpost:plinthcap "
        "svcpost:plinthcap dplate:plinthcap hut:plinthcap lamp:plinthcap grate:lamp grate:scope "
        "grate:svcpost goalpost:grate grate:plinthcap plinth:plinthcap deckbeam:plinthcap "
        "deckstrut:plinth footing:leg footing:strut",
    "top steel frame (struts, braces, heads, deck beams, legs, saddles, crane)":
        "brace:strut headbeam:headgirder headbeam:strut headgirder:strut deckbeam:deckbeam "
        "deckbeam:grate deckbeam:strut grate:strut leg:legbrace legbrace:legbrace sgirder:sgpost "
        "sgirder:sgtie sgpost:sgtie goalbeam:goalknee goalbeam:goalpost goalbeam:lug "
        "goalbeam:mast goalknee:goalpost mast:mastarm",
    "top operator hut kit": "hut:hutroof hut:lamp",
    "lamps on their posts and brackets": "lamp:lamp",
    "grating kits: grating + its lip touch coplanar (revision1b-check-tram N3, flips in the FBX)": "grate:grate",
    # detail pass 1: the kit pairs INSIDE the cs2_liftdress builders the tram uses (a subset of
    # cs2_liftdress.DRESS_CONTACTS, BVH-measured there; test_detail_tram asserts the subset)
    "dressing kits (extinguisher, hose reel, ski rack, tools, gauge, crane hook)":
        "ext:ext hosereel:hosereel skirack:skirack ski:skirack tool:tool gauge:gauge hook:hook",
    # detail pass 1, station side (skeptic MC8: real tram kinds, each pair measured by C14 on the
    # build): wall-mounted pieces on the hut walls (HUT_NEIGHBOURS too), the tools leaning on the hut,
    # painted stripes lying on the bay gratings, the hook chain under its crane lug, the beacon lens
    # on the mast top
    "detail pass 1 dressing on the top station":
        "board:hut ext:hut gauge:hut hosereel:hut hut:tool grate:stripe hook:lug lamp:mast",
    # base: the departure board on the inner glazing, LIFT OPS on the operator cabin's head band,
    # extinguishers on the hall columns, the hose reel on the drive house; the chevron plates lie on
    # the edge strips flush with the decks either side and the bay-gate posts / face-rail posts and
    # kick plates stand through them (a paint layer); the nosings lie on the deck fronts under the
    # face-rail posts and kick plates
    "detail pass 1 dressing on the base station":
        "board:glazepanel board:opcabtop col:ext drivehouse:hosereel chev:deck chev:edgestrip "
        "chev:gatepost chev:kick chev:post deck:stripe kick:stripe post:stripe",
    # coplanar touches with no triangle pair in-process that may flip in the float32 cm FBX (the N3
    # class): the TICKETS board on the stone wall, the ticket-window frames on the wall / sill and
    # at their corners
    "detail pass 1 coplanar touches (FBX flips)":
        "board:stonebase stonebase:tickframe sill:tickframe tickframe:tickframe",
    # revision 2 (design 5 "C14 declarations to add", each measured by C14 on the build): the top's foot
    # sheaves (rope in their grooves, the kit hub disc in the closed bore, the beam carrying the hubs and running
    # into the A-frame legs), the bullwheel's spokes into its closed bore, the lane wheels' spokes into theirs,
    # the lowered saddle tie through the lane-wheel posts, the frame-only wheel guard on the platform rail /
    # kick plate / grating, the roof mast's dish and panel antenna, the trail boards clamped on the cap rail,
    # the medallions resting on the knee rails; the base's deflection sheaves (rope, hub in the bore, axle,
    # arm into the saddle girder's web) and the ceiling plates the RoofLight rows hang from (lamp:lamp)
    "revision 2 wheels, guard, mast, boards, medallions":
        "fsrim:rope fshub:fsrim fsbeam:fshub bullframe:fsbeam bullrim:bullspoke dwrim:dwspoke dwpost:sgtie "
        "cwguard:kick cwguard:post cwguard:rail cwguard:cwguard cwguard:grate antdish:mast antpanel:mast "
        "board:rail board:post medal:rail bwdrim:rope bwdhub:bwdrim bwdaxle:bwdhub bwdarm:bwdaxle "
        "bwdarm:sgirder",
    # revision 2 (measured): the haul rope's arcs are polylines on the rope circle (3 segments a wheel, skeptic
    # MC9's 888-tri budget), so each chord sags up to 4.4 cm (lane wheel) / 4.0 cm (foot sheave) under the rope
    # radius - INSIDE the rim, below the groove seat, where the lane-wheel spoke tips meet the closed bore face and
    # the foot sheave's kit hub disc sits in its bore. Hidden inside the rims
    "revision 2 rope chords inside the wheel rims": "dwspoke:rope fshub:rope bwdhub:rope bwspoke:rope",
    # nitpick B (JH_DETAILS): the front canopy's roof sheet on its soffit slab, the V legs into the slab and onto their
    # steel shoes (the two legs share a foot), the shoes on the stone piers, the ties from the front columns into the
    # slab's back edge; the annex's LIFT TICKETS board, window frames and sills on its stone face (the front pair's
    # board / tickframe / sill pairs, annex kind); the chalkboard A-frame's leaves at their hinge and its chalk faces
    "nitpick B JH canopy, V-struts, annex ticket windows, chalkboard":
        "jhcanopy:jhroof jhcanopy:jhstrut jhcanopy:jhtie col:jhtie jhshoe:jhstrut jhshoe:stonepier jhstrut:jhstrut "
        "annexbase:board annexbase:sill annexbase:tickframe chalkboard:chalkboard",
    # nitpick B2 (RAMPS): a ramp deck meets its landings / head gratings / head beams / pad end-on and bears on its
    # columns; the columns stand on the paving (base) and carry the landings and head beams; the railing kit stands on
    # the decks, landings and pads; the balcony's ledger on the annex cap; the side-area door's storefront in the annex
    # cap / frame column, its lites on its leaves
    "nitpick B2 ramps, landings, pads, paving, the side-area door":
        "ramp:landing ramp:pad ramp:grate ramp:post ramp:kick ramp:plaza ramp:plazaunder ramp:deckbeam ramp:rampcol "
        "rampcol:landing rampcol:plaza rampcol:plazaunder rampcol:deckbeam rampbeam:landing annexcap:rampbeam "
        "annexbase:rampbeam pad:post pad:kick landing:landing "
        "annexdoor:annexdoor annexcap:annexdoor annexdoor:annexframe annexdoor:doorlite",
    # nitpick B3 (ANNEX_STAIR): the annex's cut wall (its three pieces meet at the doorway's jambs and head), the landing
    # block through the doorway on the annex floor and on the stone cap's nose,
    # the solid steps on the floor and on one another, the railing posts on the floor
    "nitpick B3 annex stair, doorway, landing block":
        "annexback:annexback annexback:landing annexcap:landing landing:stonecap annexcap:tread "
        "tread:tread annexcap:post",
    # revision 3 (PLINTH_TOP, below grade): the pier, both pads, the ramp columns, the stair posts and the rear caps seated
    # on the foundation block, the block on its footing slab, the bents' tie girts into the block's line face; the lattice
    # kit (columns into the pile caps and into their pad footings, girts and X braces into the columns, the X pairs
    # crossing, the bottom braces onto the pad footings)
    "revision 3 plinth: foundation block, footing slab, lattice bents":
        "found:plinth found:found found:pad found:rampcol found:stairpost footing:found found:latgirt "
        "footing:latcol latcol:latpad latcol:latgirt latbrace:latcol latbrace:latbrace latbrace:latgirt latbrace:latpad",
    # revision 3 (measured): the saddle-girder hangers end ON the rear head beam's underside (top = HEAD_Y0 = the beam's
    # bottom: a designed face-to-face contact). The pair reads as touching or not at float32 epsilon: the plinth-mode
    # join origin (the first part, `plinth`, moved its center) re-rounds every vertex by ~1e-7 m, and the BVH overlap
    # then sees the shared plane. Declared, not moved (nothing above grade moves in revision 3)
    "saddle-girder hangers under the rear head beam (face contact)": "headbeam:sghanger",
}
CONTACT_KINDS = {tuple(sorted(tok.split(":"))): why for why, toks in CONTACTS.items()
                 for tok in toks.split()}
# Exact part pairs that intersect and are NOT designed: open findings the C14 report lists on
# every build (owner: the station generator; the base was not re-exported in revision 1b).
KNOWN_OPEN = {
    ("mplatpost_2", "stonecap"): "F2 (revision1-check-tram-stations): moving platform 0.05 m "
                                 "into the stone-cap nose",
    ("mplatpost_3", "stonecap"): "F2", ("mplatbrace_3", "stonecap"): "F2",
    ("mplatbrace_4", "stonecap"): "F2", ("mplatbrace_5", "stonecap"): "F2",
    # revision 2: F3 (lamp_rod_3 through the 10.6 m drive-wheel rim) closed - the 4.0 m wheel (ruling Q6)
    # spans z 0.0..4.0, the rod stands at z -3.00
    ("bwshaft", "sgtie"): "top --no-wheels only (not shipped): the return-wheel shaft passes "
                          "through the saddle tie",
}
# The operator hut (top): every part but the hut group and the cap it stands on stays OUTSIDE the
# hut group's boxes (box distance, so a member wholly inside the open-bottom hut reads 0 - the
# triangle test cannot see that); the bullwheel frame keeps 0.30 (F1). First matching prefix.
# Measured revision 1b: svcpost_1 0.10 behind the roof drip, the cap railing 0.22 in front.
# Revision 2: `lamp_hut` is gone (a vanilla wall-light row).
HUT_GROUP = ("hut", "door_hut_", "drip_hutroof_")
# detail pass 1 (skeptic MC8): the hut-mounted dressing sits ON the hut walls by design - the name
# board, the tension gauge, the extinguisher, the hose reel, the scraper + shovel leaning on it
HUT_NEIGHBOURS = ("plinthcap", "board_stn", "label_stn", "gauge_tension", "label_tension", "ext_hut",
                  "hosereel_hut", "tool_hut")
HUT_CLEAR = (("bullframe_", 0.30), ("", 0.05))


def part_kind(name):
    m = re.match(r"[a-z]+", name)
    return m.group(0) if m else name


def _part_tris(obj):
    """Prefab-frame vertices and {part: triangle vertex ids (N, 3)} (fan split per polygon)."""
    pf, part, table = mesh_arrays(obj)
    start, total, lv = poly_vertices(obj.data)
    names = part_names_of_polys(part, table)
    out = {}
    for i in range(len(start)):
        s, c = int(start[i]), int(total[i])
        lst = out.setdefault(names[i], [])
        for j in range(1, c - 1):
            lst.append((int(lv[s]), int(lv[s + j]), int(lv[s + j + 1])))
    return pf, {n: np.array(v, dtype=np.int64) for n, v in out.items()}


def _edge_samples(pf, tris, step, lo=None, hi=None):
    """Vertices + points every `step` m along every edge of `tris` (edges whose box misses
    [lo, hi] are skipped)."""
    e = np.sort(np.concatenate([tris[:, [0, 1]], tris[:, [1, 2]], tris[:, [2, 0]]]), axis=1)
    e = np.unique(e, axis=0)
    a, b = pf[e[:, 0]], pf[e[:, 1]]
    if lo is not None:
        keep = np.all(np.minimum(a, b) <= hi, axis=1) & np.all(np.maximum(a, b) >= lo, axis=1)
        a, b = a[keep], b[keep]
    out = [pf[np.unique(tris)]]
    for p, q in zip(a, b):
        n = max(2, int(math.ceil(float(np.linalg.norm(q - p)) / step)) + 1)
        out.append(p + (q - p) * np.linspace(0.0, 1.0, n)[:, None])
    return np.concatenate(out)


def _box_dist(P, lo, hi):
    return np.linalg.norm(np.maximum(np.maximum(lo - P, 0.0), P - hi), axis=1)


def check_part_clashes(obj, role):
    """C14 (revision 1b). On the joined, oriented LOD0:
      clash     - for every pair of parts whose boxes overlap, a BVH triangle-pair test; a pair
                  that intersects must be a declared contact (CONTACTS by kind) or a listed
                  open finding (KNOWN_OPEN, exact names), else FAIL;
      contained - an undeclared part whose box lies inside another part's box must not sit
                  inside that part's CLOSED volume (ray parity; no triangle pair can see it);
      hut       - (top) every part but the hut group and its declared neighbors keeps
                  HUT_CLEAR from the hut group's boxes (sampled every 0.02 m both ways).
    Returns the report; raises ContractError("C14 ...") on any violation."""
    from mathutils.bvhtree import BVHTree
    pf, parts = _part_tris(obj)
    names = sorted(parts)
    LO = np.array([pf[parts[n]].reshape(-1, 3).min(axis=0) for n in names])
    HI = np.array([pf[parts[n]].reshape(-1, 3).max(axis=0) for n in names])
    trees, closed = {}, {}

    def tree(n):
        if n not in trees:
            ids = np.unique(parts[n])
            remap = {int(v): i for i, v in enumerate(ids)}
            tr = [[remap[int(v)] for v in t_] for t_ in parts[n]]
            trees[n] = BVHTree.FromPolygons(pf[ids].tolist(), tr, all_triangles=True)
        return trees[n]

    def is_closed(n):
        if n not in closed:
            t_ = parts[n]
            e = np.sort(np.concatenate([t_[:, [0, 1]], t_[:, [1, 2]], t_[:, [2, 0]]]), axis=1)
            _u, cnt = np.unique(e, axis=0, return_counts=True)
            closed[n] = bool(len(cnt)) and bool(np.all(cnt == 2))
        return closed[n]

    dirs = [Vector(d).normalized() for d in ((0.37, 0.56, 0.74), (-0.61, 0.29, 0.73),
                                              (0.23, -0.81, 0.54))]

    def inside(n, p):
        votes = 0
        for d in dirs:
            o, hits = Vector(p), 0
            for _ in range(256):
                hit, _nrm, _i, _d = tree(n).ray_cast(o, d)
                if hit is None:
                    break
                hits += 1
                o = hit + d * 1e-5
            votes += hits % 2
        return votes >= 2

    def contained_in(inner, outer):
        for v in np.unique(parts[inner])[:4]:
            p = Vector(pf[v])
            near = tree(outer).find_nearest(p)
            if near[0] is not None and near[3] > 1e-4:
                return inside(outer, p)
        return False

    n_box, n_decl, undeclared, contained, known = 0, 0, [], [], []
    for i, a in enumerate(names):
        m = np.all(LO[i + 1:] <= HI[i] + 1e-6, axis=1) & np.all(HI[i + 1:] >= LO[i] - 1e-6, axis=1)
        for j in np.nonzero(m)[0]:
            b = names[i + 1 + j]
            n_box += 1
            declared = tuple(sorted((part_kind(a), part_kind(b)))) in CONTACT_KINDS
            ov = tree(a).overlap(tree(b))
            if ov:
                if (a, b) in KNOWN_OPEN:
                    known.append(f"{a} | {b}: {KNOWN_OPEN[(a, b)]}")
                elif declared:
                    n_decl += 1
                else:
                    undeclared.append((a, b, len(ov)))
            elif not declared:
                for inner, outer, k_in, k_out in ((a, b, i, i + 1 + j), (b, a, i + 1 + j, i)):
                    if (np.all(LO[k_in] >= LO[k_out] - 1e-6) and np.all(HI[k_in] <= HI[k_out] + 1e-6)
                            and is_closed(outer) and contained_in(inner, outer)):
                        contained.append((inner, outer))
    rep = {"parts": len(names), "box_pairs": n_box, "declared_contacts": n_decl,
           "known_open": known}
    msgs = []
    if undeclared:
        msgs.append(f"{len(undeclared)} undeclared part clashes (part | part: triangle pairs): "
                    + ", ".join(f"{a} | {b}: {c}" for a, b, c in undeclared[:12]))
    if contained:
        msgs.append("parts inside another part's closed volume: "
                    + ", ".join(f"{a} in {b}" for a, b in contained[:12]))

    # -- hut clearance --------------------------------------------------------------------------
    hut = [n for n in names if n.startswith(HUT_GROUP)]
    if hut:
        need_max = max(c for _p, c in HUT_CLEAR)
        boxes = [(pf[parts[n]].reshape(-1, 3).min(axis=0), pf[parts[n]].reshape(-1, 3).max(axis=0))
                 for n in hut]
        ulo = np.min([b[0] for b in boxes], axis=0) - need_max
        uhi = np.max([b[1] for b in boxes], axis=0) + need_max
        corner = np.array([[i_ >> k & 1 for k in range(3)] for i_ in range(8)], dtype=np.float64)
        dist = {}
        for k_, n in enumerate(names):
            if n in hut or n.startswith(HUT_NEIGHBOURS):
                continue
            if np.any(LO[k_] > uhi) or np.any(HI[k_] < ulo):
                continue
            S = _edge_samples(pf, parts[n], 0.02, ulo, uhi)
            d = min(float(_box_dist(S, blo, bhi).min()) for blo, bhi in boxes)
            for blo, bhi in boxes:                         # the hut boxes' edges onto the part
                if np.any(blo > HI[k_] + need_max) or np.any(bhi < LO[k_] - need_max):
                    continue
                C = blo + corner * (bhi - blo)
                for p_, q_ in ((0, 1), (0, 2), (0, 4), (1, 3), (1, 5), (2, 3), (2, 6), (3, 7),
                               (4, 5), (4, 6), (5, 7), (6, 7)):
                    L = float(np.linalg.norm(C[q_] - C[p_]))
                    for s in np.linspace(0.0, 1.0, max(2, int(math.ceil(L / 0.02)) + 1)):
                        hit = tree(n).find_nearest(Vector(C[p_] + (C[q_] - C[p_]) * s), need_max)
                        if hit[0] is not None:
                            d = min(d, float(hit[3]))
            dist[n] = d
        too_close = []
        for n, d in sorted(dist.items(), key=lambda kv: kv[1]):
            need = next(c for p_, c in HUT_CLEAR if n.startswith(p_))
            if d < need - 1e-6:
                too_close.append(f"{n} {d:.3f} < {need:.2f}")
        near = sorted(dist.items(), key=lambda kv: kv[1])[:4]
        frame = [d for n, d in dist.items() if n.startswith("bullframe_")]
        rep["hut"] = {"group": hut, "nearest_m": {n: round(d, 3) for n, d in near},
                      "bullframe_m": round(min(frame), 3) if frame else None}
        if too_close:
            msgs.append("hut clearance (box distance to the hut group): " + ", ".join(too_close[:8]))
    if msgs:
        _fail("C14", "; ".join(msgs))
    return rep

# --------------------------------------------------------------------------------------
# C15 platform reachability (revision 1c; Richard's fourth look 2026-10-11: "every walkable platform
# on a station must be reachable from grade by a stair or ladder" - a contract, not a visual check)
# --------------------------------------------------------------------------------------
# Walkable surfaces = the up-facing tops of these parts: grating plates (a `_lip` is the grating's
# rim, not a surface), decks, edge strips, landings, the top's plinth deck. Nitpick B3: and the annex
# floor `annexcap` (ANNEX_WALK: the side area has a stair to the hall now). Not walkable by design:
# the base pit floor `trenchcap` (moving-platform rails), the slab under the decks `stonecap`,
# roofs, lids, beams, footings. A railing post must stand on a
# walkable surface or a stair flight, so a railed platform cannot escape the test by its name.
WALK_PREFIXES = ("deck_", "edgestrip_", "grate_", "landing_", "plinthcap", "pad_")   # pad_: nitpick B2 (C15b)
# Detail pass 1 (revision1c-check-tram-stair N2 / skeptic MC2): NOT walkable by design, each with its
# reason. C15 discovers every part whose top a person could stand on (C15_STAND: an up-facing top of
# >= 1 m2, >= 0.60 m up, >= 0.45 m across, with a 0.30 m body clear for 1.80 m above at least one
# point): it must be a walkable surface (WALK_PREFIXES) or listed here, else C15 FAILS ("undeclared
# standable surface") - a new unrailed platform cannot slip in by its name. Longest prefix wins.
NOT_WALKABLE = (
    ("stonecap", "the slab under the boarding decks: where it is open (the lobby, 1.1 m below the decks"
                 " behind the dark glazing, and the two cabin berth pits under the parked cabins) it is a"
                 " hall interior with no modelled interior stair (the entrance doors are at grade) - not"
                 " a platform (N2)"),
    ("trenchcap", "the moving-platform pit floor between the rails: a machine space reached only for"
                  " maintenance, never walked (C15 since 1c; the design's pit ladder TB-A1 dropped,"
                  " skeptic MC2)"),
    ("mplatbase", "the moving platform's underframe below its own grating deck (machine space)"),
    ("roof", "roof sheets (hall, annex, operator cabin, hut, entrance canopy): no roof access"),
    ("annexroof", "the annex roof sheet: no roof access"),
    ("opcabroof", "the operator cabin's roof: no roof access"),
    ("hutroof", "the operator hut's roof: no roof access"),
    ("canopyroof", "the entrance canopy roof: no roof access"),
    ("cwlid_", "the counterweight-shaft lids (maintenance hatch covers)"),
    ("drivehouse", "the drive house's top under the bullwheel (machinery)"),
    ("bw", "the base drive bullwheel's rim and hub tops (machinery)"),
    ("sportal_", "the saddle-portal beam (steel member, not a walkway)"),
    ("ct", "the clock tower: its cornice ledge and shaft top lie inside the open-bottom stage, the cap"
           " under the spire - no access"),
    ("headbeam_", "the top's portal head beams: steel members"),
    ("headgirder_", "the top's head girders: steel members"),
    ("goalbeam", "the crane goal beam: steel member (its tips carry the warning lights)"),
    ("dwbeam", "the deflection-wheel beam: steel member"),
    ("footing_", "the frame's foundation pads round the leg / strut feet (0.80 m): not platforms"),
    ("jhroof", "the JH front canopy's roof sheet (nitpick B): no roof access"),
    ("jhcanopy", "the JH front canopy's soffit slab under its roof sheet (nitpick B): no roof access"),
)
# Nitpick B3: the annex floor was listed above ("the enclosed annex floor behind its painted glazing: an interior, not a
# platform", N2) while the annex was a dead end with no way up to the hall's inner door (B2 check F2: C15 could not see
# it). It is a WALKABLE surface now (ANNEX_WALK): the side area is a room with a stair, and C15 proves the route through
# it. ANNEX_STAIR False (the B2 base, byte for byte) restores the entry:
NOT_WALKABLE_ANNEX_OFF = (
    ("annexcap", "the enclosed annex floor behind its painted glazing: an interior, not a platform (N2)"),
)
C15_STAND = {"area": 1.0, "level": 0.60, "short": 0.45, "body": 0.30, "head": 1.80, "grid": 0.60,
             "samples": 15}
C15_JOIN = {"depth": 0.45, "foot": 0.10, "head": 1.80,
            # nitpick B3: the side-area door's leaves and push bars are doors too (the annex floor is walkable now)
            "door": ("door_", "doorlite_", "annexdoor_leaf", "annexdoor_bar")}
C15 = {
    "land": 0.30,          # a flight / ladder end lands on a surface: height and plan gap within this
    "grade": 0.30,         # ... or on the ground: end height <= this
    "level": 0.05,         # two surfaces within this height ...
    "gap": 0.40,           # ... and this plan gap are one walking area (a door sill / glazing line)
    "clear": 0.61,         # stair clear width; ladder corridor width and climbing-side depth (24 in)
    "headroom": 2.03,      # clear above every tread (6 ft 8 in)
    "rise": (0.10, 0.24),  # riser range (OSHA 1910.25: <= 9.5 in)
    "going": 0.10,         # minimum going (a regular flight; the top's 45 deg maintenance stair has 0.178)
    "pitch": 0.36,         # maximum rung pitch (OSHA 1910.23: 10-14 in)
    "first": 0.40,         # first rung above the ladder foot, at most
    "post_on": 0.10,       # railing post foot to its surface (plan), height within `level`
}


def _walk_prefixes():
    """WALK_PREFIXES, plus (nitpick B3, ANNEX_STAIR) the annex floor."""
    return tuple(WALK_PREFIXES) + (tuple(ANNEX_WALK) if ANNEX_STAIR else ())


def walk_surfaces(pf, parts):
    """{part: {"y", "x": [lo, hi], "z": [lo, hi]}}: top height (highest up-facing face) and the part's
    plan box (the bevel's chamfers are not up-facing, so the box is taken from the whole part)."""
    out = {}
    wp = _walk_prefixes()
    for nm in sorted(parts):
        if not nm.startswith(wp) or nm.endswith("_lip"):
            continue
        T = pf[parts[nm]]
        n = np.cross(T[:, 1] - T[:, 0], T[:, 2] - T[:, 0])
        L = np.linalg.norm(n, axis=1)
        up = (L > 1e-12) & (n[:, 1] > 0.95 * L)
        if not up.any():
            continue
        Q = T.reshape(-1, 3)
        out[nm] = {"y": round(float(T[up][:, :, 1].max()), 4),
                   "x": [round(float(Q[:, 0].min()), 4), round(float(Q[:, 0].max()), 4)],
                   "z": [round(float(Q[:, 2].min()), 4), round(float(Q[:, 2].max()), 4)]}
    return out


def _plan_gap(px, pz, s):
    return math.hypot(max(s["x"][0] - px, 0.0, px - s["x"][1]), max(s["z"][0] - pz, 0.0, pz - s["z"][1]))


def _surf_gap(a, b):
    return math.hypot(max(a["x"][0] - b["x"][1], 0.0, b["x"][0] - a["x"][1]),
                      max(a["z"][0] - b["z"][1], 0.0, b["z"][0] - a["z"][1]))


def walk_flights(pf, parts):
    """Stair flights from their treads (`tread_<tag>_<k>`): per flight the riser / going series, the
    clear width, the head point (one going behind the top tread, one riser above it) and the foot
    point (one going past the bottom tread, one riser below it), and the per-tread headroom boxes."""
    groups = {}
    for nm in parts:
        m = re.fullmatch(r"tread_(.+)_(\d+)", nm)
        if m:
            groups.setdefault(m.group(1), []).append(nm)
    out = {}
    for tag in sorted(groups):
        rows = []
        for nm in groups[tag]:
            P = pf[parts[nm]].reshape(-1, 3)
            rows.append((float(P[:, 1].max()), P.min(axis=0), P.max(axis=0), nm))
        rows.sort(key=lambda r: (-r[0], r[3]))
        f = {"treads": len(rows)}
        out[tag] = f
        if len(rows) < 2:
            f["error"] = "fewer than 2 treads"
            continue
        ys = np.array([r[0] for r in rows])
        lo = np.array([r[1] for r in rows])
        hi = np.array([r[2] for r in rows])
        cen = (lo + hi) / 2.0
        d = cen[-1] - cen[0]
        ax = 2 if abs(d[2]) >= abs(d[0]) else 0          # travel axis (prefab z or x)
        cx = 2 - ax                                       # across
        sgn = 1.0 if d[ax] > 0 else -1.0                  # descent direction along ax
        rises, goings = -np.diff(ys), sgn * np.diff(cen[:, ax])
        rise, going = float(np.median(rises)), float(np.median(goings))
        head, foot = cen[0].copy(), cen[-1].copy()
        head[ax] -= sgn * going
        head[1] = ys[0] + rise
        foot[ax] += sgn * going
        foot[1] = ys[-1] - rise
        boxes = []
        for y_, l_, h_ in zip(ys, lo, hi):
            blo, bhi = np.empty(3), np.empty(3)
            blo[cx], bhi[cx] = l_[cx] + 0.05, h_[cx] - 0.05
            blo[ax], bhi[ax] = l_[ax] + 0.01, h_[ax] - 0.01
            blo[1], bhi[1] = y_ + 0.05, y_ + C15["headroom"]
            boxes.append((blo, bhi))
        own = set(groups[tag]) | {f"stairstr_{tag}a", f"stairstr_{tag}b", f"stairslab_{tag}"}
        f.update({"axis": "xyz"[ax], "rise": round(rise, 4), "going": round(going, 4),
                  "rise_rng": [round(float(rises.min()), 4), round(float(rises.max()), 4)],
                  "going_min": round(float(goings.min()), 4),
                  "width": round(float((hi[:, cx] - lo[:, cx]).min()), 4),
                  "head": [round(float(v), 4) for v in head], "foot": [round(float(v), 4) for v in foot],
                  "rise_total": round(float(head[1] - foot[1]), 3),
                  "lo": np.minimum(lo.min(axis=0), np.minimum(head, foot)),
                  "hi": np.maximum(hi.max(axis=0), np.maximum(head, foot)),
                  "boxes": boxes, "own": own})
    return out


RAMP_DECK_RE = r"ramp_([A-Za-z0-9]+)"     # nitpick B2: a ramp's deck plate (its rails are the handrail kit's)
WALK_PADS = ("pad_",)                     # nitpick B2: concrete pads a ramp ends on (C15b)
# nitpick B2: the ramps' own landings - a join of one of these crosses >= RAMP crossing (0.70; the older C15 joins keep
# C15 clear 0.61: the -X gangway mouth onto the plinth cap measures 0.696)
RAMP_LANDINGS = ("landing_turn", "landing_rtop", "landing_balc", "grate_headR", "pad_")


def walk_ramps(pf, parts):
    """Nitpick B2: the ramps from their deck plates (`ramp_<tag>`): the deck top's plane (least squares on the
    up-facing faces' vertices: y = a + bx x + cz z), the travel axis (the steeper of x / z), grade and cross fall, the
    run and the cross range, the head (high end) and foot (low end) points on the center line."""
    out = {}
    for nm in sorted(parts):
        m = re.fullmatch(RAMP_DECK_RE, nm)
        if not m:
            continue
        r = {"part": nm}
        out[m.group(1)] = r
        T = pf[parts[nm]]
        n = np.cross(T[:, 1] - T[:, 0], T[:, 2] - T[:, 0])
        L = np.linalg.norm(n, axis=1)
        up = (L > 1e-12) & (n[:, 1] > 0.90 * L)
        if not up.any():
            r["error"] = "no up-facing deck face"
            continue
        Q = np.unique(np.round(T[up].reshape(-1, 3), 6), axis=0)
        A = np.c_[np.ones(len(Q)), Q[:, 0], Q[:, 2]]
        (a0, bx, cz), *_ = np.linalg.lstsq(A, Q[:, 1], rcond=None)
        ax = 2 if abs(cz) >= abs(bx) else 0
        cx = 2 - ax
        slope, cross = (cz, bx) if ax == 2 else (bx, cz)
        lo, hi = Q.min(axis=0), Q.max(axis=0)
        mid = (lo[cx] + hi[cx]) / 2.0
        ends = []
        for s_ in (lo[ax], hi[ax]):
            p = [0.0, 0.0, 0.0]
            p[ax], p[cx] = float(s_), float(mid)
            p[1] = float(a0 + bx * p[0] + cz * p[2])
            ends.append(p)
        head, foot = (ends[1], ends[0]) if ends[1][1] >= ends[0][1] else (ends[0], ends[1])
        r.update({"ax": ax, "cx": cx, "axis": "xyz"[ax], "plane": (float(a0), float(bx), float(cz)),
                  "grade": abs(float(slope)), "cross": abs(float(cross)), "run": [float(lo[ax]), float(hi[ax])],
                  "across": [float(lo[cx]), float(hi[cx])], "mid": float(mid), "head": head, "foot": foot,
                  "rise": float(head[1] - foot[1]), "length": float(hi[ax] - lo[ax])})
    return out


def _ramp_y(r, px, pz):
    a0, bx, cz = r["plane"]
    return a0 + bx * px + cz * pz


def _free_across(T_all, N_all, T_lo, T_hi, names, own, ax, cx, a_lo, a_hi, y_lo, y_hi, c_lo, c_hi, centre):
    """The free stretch across a ramp (axis cx) containing `centre`, in the band a_lo..a_hi along its axis and
    y_lo..y_hi: (lo, hi, part bounding it below, part bounding it above) - None for a band end. Foreign triangles
    (not in `own`) clipped to the band block their cross extent."""
    blo, bhi = np.empty(3), np.empty(3)
    blo[ax], bhi[ax] = a_lo, a_hi
    blo[cx], bhi[cx] = c_lo, c_hi
    blo[1], bhi[1] = y_lo, y_hi
    cand = np.nonzero(np.all(T_lo <= bhi, axis=1) & np.all(T_hi >= blo, axis=1))[0]
    cand = np.array([i for i in cand if names[N_all[i]] not in own], dtype=np.int64)
    blocked = []
    if len(cand):
        for i in cand[_tri_box_overlap(T_all[cand], blo, bhi)]:
            poly = _clip_poly([T_all[i][0], T_all[i][1], T_all[i][2]], ax, a_lo, a_hi)
            poly = _clip_poly(poly, 1, y_lo, y_hi) if poly else poly
            if poly:
                vals = [float(p[cx]) for p in poly]
                blocked.append((max(min(vals), c_lo), min(max(vals), c_hi), names[N_all[i]]))
    lo_b = [b for b in blocked if b[1] <= centre]
    hi_b = [b for b in blocked if b[0] >= centre]
    if any(b[0] < centre < b[1] for b in blocked):
        over = next(b for b in blocked if b[0] < centre < b[1])
        return centre, centre, over[2], over[2]
    lo = max(lo_b, key=lambda b: b[1]) if lo_b else None
    hi = min(hi_b, key=lambda b: b[0]) if hi_b else None
    return (lo[1] if lo else c_lo, hi[0] if hi else c_hi, lo[2] if lo else None, hi[2] if hi else None)


def walk_ladders(pf, parts):
    """Fixed ladders from their rungs (`lrung_<tag>_<k>`) and rails (`ladder_<tag>_*`): rung axis,
    rung plane, foot (lowest rail vertex), rungs and pitch."""
    groups = {}
    for nm in parts:
        m = re.fullmatch(r"lrung_(.+)_(\d+)", nm)
        if m:
            groups.setdefault(m.group(1), []).append(nm)
    out = {}
    for tag in sorted(groups):
        rows = []
        for nm in groups[tag]:
            P = pf[parts[nm]].reshape(-1, 3)
            rows.append(((P.min(axis=0) + P.max(axis=0)) / 2.0, P.max(axis=0) - P.min(axis=0)))
        rows.sort(key=lambda r: r[0][1])
        cen = np.array([r[0] for r in rows])
        ext = np.array([r[1] for r in rows]).mean(axis=0)
        rax = 0 if ext[0] >= ext[2] else 2                # along the rungs
        nax = 2 - rax                                     # rung-plane normal
        rails = [n_ for n_ in parts if n_.startswith(f"ladder_{tag}_")]
        foot_y = min(float(pf[parts[n_]][:, :, 1].min()) for n_ in rails) if rails else None
        ys = cen[:, 1]
        out[tag] = {"rungs": len(rows), "rax": rax, "nax": nax,
                    "plane": round(float(cen[:, nax].mean()), 4), "mid": round(float(cen[:, rax].mean()), 4),
                    "foot_y": None if foot_y is None else round(foot_y, 4), "head_y": round(float(ys[-1]), 4),
                    "first": None if foot_y is None else round(float(ys[0] - foot_y), 4),
                    "pitch": round(float(np.diff(ys).max()) if len(ys) > 1 else 9.0, 4),
                    "own_prefixes": (f"ladder_{tag}_", f"lrung_{tag}_")}
    return out


def _clip_poly(poly, ax, lo, hi):
    """Sutherland-Hodgman clip of a 3D polygon (list of arrays) to lo <= p[ax] <= hi."""
    for bound, sgn in ((lo, 1.0), (hi, -1.0)):
        out, n = [], len(poly)
        for i in range(n):
            p, q = poly[i], poly[(i + 1) % n]
            pin, qin = sgn * (p[ax] - bound) >= -1e-9, sgn * (q[ax] - bound) >= -1e-9
            if pin:
                out.append(p)
            if pin != qin:
                out.append(p + (q - p) * ((bound - p[ax]) / (q[ax] - p[ax])))
        poly = out
        if not poly:
            break
    return poly


def join_crossing(a, b, surf, T_all, N_all, T_lo, T_hi, names):
    """Detail pass 1 (revision1c-check-tram-stair N1): the clear crossing between two joined walkable
    surfaces. The band straddles their shared boundary C15_JOIN depth into each, from level + foot to
    level + head (doors open: their leaves, lites and bars are not obstacles); every foreign triangle in
    it blocks its extent along the boundary. The join holds when a C15 clear wide free stretch has its
    center over the shared boundary. A surface narrower than C15 clear (an edge strip) is part of its
    neighbor's walking area, not a hand-off: exempt. Returns the join report."""
    sa, sb = surf[a], surf[b]
    clear = C15["clear"]
    if min(s[k][1] - s[k][0] for s in (sa, sb) for k in ("x", "z")) < clear - 1e-9:
        return {"ok": True, "opening": None, "why": "strip (narrower than the clear width)"}
    gx = max(sa["x"][0] - sb["x"][1], sb["x"][0] - sa["x"][1])
    gz = max(sa["z"][0] - sb["z"][1], sb["z"][0] - sa["z"][1])
    nk, bk = ("x", "z") if gx >= gz else ("z", "x")
    nax, bax = (0, 2) if nk == "x" else (2, 0)
    s0, s1 = (sa, sb) if sa[nk][0] + sa[nk][1] <= sb[nk][0] + sb[nk][1] else (sb, sa)
    line = (s0[nk][1] + s1[nk][0]) / 2.0
    ov = (max(sa[bk][0], sb[bk][0]), min(sa[bk][1], sb[bk][1]))
    dom = (min(sa[bk][0], sb[bk][0]), max(sa[bk][1], sb[bk][1]))
    if ov[1] - ov[0] < 1e-6:
        return {"ok": False, "opening": 0.0, "why": "corner (no shared edge)"}
    y = max(sa["y"], sb["y"])
    blo, bhi = np.empty(3), np.empty(3)
    blo[nax], bhi[nax] = line - C15_JOIN["depth"], line + C15_JOIN["depth"]
    blo[bax], bhi[bax] = dom
    blo[1], bhi[1] = y + C15_JOIN["foot"], y + C15_JOIN["head"]
    own = {a, b, a + "_lip", b + "_lip"}
    cand = np.nonzero(np.all(T_lo <= bhi, axis=1) & np.all(T_hi >= blo, axis=1))[0]
    cand = np.array([i for i in cand if names[N_all[i]] not in own
                     and not names[N_all[i]].startswith(C15_JOIN["door"])], dtype=np.int64)
    blocked = []
    if len(cand):
        for i in cand[_tri_box_overlap(T_all[cand], blo, bhi)]:
            poly = _clip_poly([T_all[i][0], T_all[i][1], T_all[i][2]], nax, blo[nax], bhi[nax])
            poly = _clip_poly(poly, 1, blo[1], bhi[1]) if poly else poly
            if poly:
                vals = [float(p[bax]) for p in poly]
                blocked.append((max(min(vals), dom[0]), min(max(vals), dom[1]), names[N_all[i]]))
    blocked.sort()
    free, cur, last = [], dom[0], None
    for s_, e_, n_ in blocked:
        if s_ > cur + 1e-9:
            free.append((cur, s_, last, n_))
        if e_ > cur:
            cur, last = e_, n_
    if cur < dom[1] - 1e-9:
        free.append((cur, dom[1], last, None))
    best = None
    for f0, f1, by0, by1 in free:
        if f1 < ov[0] or f0 > ov[1]:
            continue
        if best is None or f1 - f0 > best[1] - best[0]:
            best = (f0, f1, by0, by1)
    ok = any(f1 - f0 >= clear - 1e-9 and max(f0 + clear / 2.0, ov[0]) <= min(f1 - clear / 2.0, ov[1]) + 1e-9
             for f0, f1, _b0, _b1 in free)
    rep = {"ok": bool(ok), "opening": round(best[1] - best[0], 3) if best else 0.0,
           "across": bk, "at": [round(best[0], 3), round(best[1], 3)] if best else None,
           "between": sorted({n_ for n_ in (best[2], best[3]) if n_}) if best else []}
    return rep


def standable_parts(pf, parts, names, T_all, N_all, T_lo, T_hi):
    """Detail pass 1 (revision1c-check-tram-stair N2): every part, other than the walkable surfaces,
    whose up-facing top a person could stand on (C15_STAND thresholds; a body box clear above at least
    one sample point of its top). {part: {"y", "area", "at"}}."""
    from mathutils.bvhtree import BVHTree
    st = C15_STAND
    own_of = np.asarray(N_all)
    out = {}
    nT = len(T_all)
    bvh = BVHTree.FromPolygons(T_all.reshape(-1, 3).tolist(),
                               [(3 * i, 3 * i + 1, 3 * i + 2) for i in range(nT)], all_triangles=True)
    upv = Vector((0.0, 1.0, 0.0))

    def inside_volume(px, y, pz):
        """True when the first face straight above is seen from its back: the point is inside a
        closed (or open-bottom) volume - under a spire, inside a stage box - not under the sky."""
        hit, nrm, _i, _d = bvh.ray_cast(Vector((float(px), y + 0.01, float(pz))), upv)
        return hit is not None and nrm.y > 0.0
    for k_, nm in enumerate(names):
        if nm.startswith(_walk_prefixes()) or re.fullmatch(RAMP_DECK_RE, nm):   # nitpick B2: a ramp deck is walked
            continue
        T = pf[parts[nm]]
        n = np.cross(T[:, 1] - T[:, 0], T[:, 2] - T[:, 0])
        L = np.linalg.norm(n, axis=1)
        up = (L > 1e-12) & (n[:, 1] > 0.95 * L)
        if not up.any():
            continue
        U = T[up]
        area = 0.5 * float(L[up].sum())
        level = float(U[:, :, 1].max())
        if area < st["area"] or level < st["level"]:
            continue
        Q = U.reshape(-1, 3)
        lo, hi = Q.min(axis=0), Q.max(axis=0)
        if min(hi[0] - lo[0], hi[2] - lo[2]) < st["short"]:
            continue
        nx = int(min(st["samples"], max(1, math.ceil((hi[0] - lo[0]) / st["grid"]))))
        nz = int(min(st["samples"], max(1, math.ceil((hi[2] - lo[2]) / st["grid"]))))
        a0, a1, a2 = U[:, 0], U[:, 1], U[:, 2]
        found = None
        for px in lo[0] + (np.arange(nx) + 0.5) * (hi[0] - lo[0]) / nx:
            for pz in lo[2] + (np.arange(nz) + 0.5) * (hi[2] - lo[2]) / nz:
                d = (a1[:, 2] - a2[:, 2]) * (a0[:, 0] - a2[:, 0]) + (a2[:, 0] - a1[:, 0]) * (a0[:, 2] - a2[:, 2])
                with np.errstate(divide="ignore", invalid="ignore"):
                    l1 = ((a1[:, 2] - a2[:, 2]) * (px - a2[:, 0]) + (a2[:, 0] - a1[:, 0]) * (pz - a2[:, 2])) / d
                    l2 = ((a2[:, 2] - a0[:, 2]) * (px - a2[:, 0]) + (a0[:, 0] - a2[:, 0]) * (pz - a2[:, 2])) / d
                inside = (np.abs(d) > 1e-12) & (l1 >= 0) & (l2 >= 0) & (l1 + l2 <= 1)
                if not inside.any():
                    continue
                l1, l2 = l1[inside], l2[inside]
                ys = l1 * a0[inside, 1] + l2 * a1[inside, 1] + (1 - l1 - l2) * a2[inside, 1]
                y = float(ys.max())
                b = st["body"] / 2.0
                blo = np.array([px - b, y + 0.05, pz - b])
                bhi = np.array([px + b, y + st["head"], pz + b])
                cand = np.all(T_lo <= bhi, axis=1) & np.all(T_hi >= blo, axis=1) & (own_of != k_)
                if (not cand.any() or not _tri_box_overlap(T_all[cand], blo, bhi).any()) and \
                        not inside_volume(px, y, pz):
                    found = [round(float(px), 2), round(y, 2), round(float(pz), 2)]
                    break
            if found:
                break
        if found:
            out[nm] = {"y": round(level, 3), "area": round(area, 2), "at": found}
    return out


def _not_walkable_reason(name):
    best = None
    for p_, why in tuple(NOT_WALKABLE) + (() if ANNEX_STAIR else NOT_WALKABLE_ANNEX_OFF):
        if name.startswith(p_) and (best is None or len(p_) > len(best[0])):
            best = (p_, why)
    return best


def check_reachability(obj, role):
    """C15 (revision 1c). On the joined, oriented LOD0:
      surfaces - every walkable surface (WALK_PREFIXES) is a node; surfaces within C15 level / gap
                 are one walking area;
      flights  - a stair flight (its treads) is an edge when its head and foot each land within
                 C15 land of a surface (or the foot on the ground), its risers / goings are regular,
                 its clear width >= C15 clear and the headroom above every tread holds no foreign
                 triangle;
      ladders  - a fixed ladder is an edge when its rail foot and its top rung land on surfaces (or
                 the ground), the rung pitch <= C15 pitch, the first rung <= C15 first above the foot
                 and its climbing corridor (C15 clear wide, C15 clear deep on the side away from the
                 surface it arrives on, foot + 0.10 .. top rung + 1.0) holds no foreign triangle;
      graph    - every surface is joined to the ground (breadth-first from grade);
      railings - every railing-kit post stands on a walkable surface or on a flight.
    Returns the report; raises ContractError("C15 ...") on any violation."""
    pf, parts = _part_tris(obj)
    names = sorted(parts)
    surf = walk_surfaces(pf, parts)
    flights = walk_flights(pf, parts)
    ladders = walk_ladders(pf, parts)
    ramps = walk_ramps(pf, parts)                         # nitpick B2
    T_all = np.concatenate([pf[parts[n]] for n in names])
    N_all = np.repeat(np.arange(len(names)), [len(parts[n]) for n in names])
    T_lo, T_hi = T_all.min(axis=1), T_all.max(axis=1)
    msgs = []

    def blocked(lo, hi, own):
        cand = np.all(T_lo <= hi, axis=1) & np.all(T_hi >= lo, axis=1)
        idx = np.nonzero(cand)[0]
        idx = idx[[names[N_all[i]] not in own for i in idx]] if len(idx) else idx
        if not len(idx):
            return []
        ov = _tri_box_overlap(T_all[idx], np.asarray(lo, float), np.asarray(hi, float))
        return sorted({names[N_all[i]] for i in idx[ov]})

    def lands(y, px, pz):
        out = [s for s, d in surf.items() if abs(d["y"] - y) <= C15["land"] and
               _plan_gap(px, pz, d) <= C15["land"]]
        if y <= C15["grade"]:
            out.append("GRADE")
        return out

    adj = {n: set() for n in list(surf) + ["GRADE"]}
    sl = sorted(surf)
    joins = {}
    for i, a in enumerate(sl):
        for b in sl[i + 1:]:
            if abs(surf[a]["y"] - surf[b]["y"]) <= C15["level"] and _surf_gap(surf[a], surf[b]) <= C15["gap"]:
                # detail pass 1 (N1): the join must leave a C15 clear crossing
                j = join_crossing(a, b, surf, T_all, N_all, T_lo, T_hi, names)
                joins[f"{a} | {b}"] = j
                if j["ok"]:
                    adj[a].add(b)
                    adj[b].add(a)
    rep_f, rep_l = {}, {}
    fl_edges = {}                                   # nitpick B3: flight tag -> the surface pairs it joins
    for tag, f in flights.items():
        if "error" in f:
            msgs.append(f"flight {tag}: {f['error']}")
            continue
        bad = []
        if not (C15["rise"][0] <= f["rise_rng"][0] and f["rise_rng"][1] <= C15["rise"][1]):
            bad.append(f"risers {f['rise_rng']} outside {C15['rise']}")
        if f["going_min"] < C15["going"]:
            bad.append(f"going {f['going_min']} < {C15['going']}")
        if f["width"] < C15["clear"]:
            bad.append(f"clear width {f['width']} < {C15['clear']}")
        hit = set()
        for blo, bhi in f["boxes"]:
            hit.update(blocked(blo, bhi, f["own"]))
        if hit:
            bad.append(f"headroom blocked by {sorted(hit)[:6]}")
        heads = lands(f["head"][1], f["head"][0], f["head"][2])
        feet = lands(f["foot"][1], f["foot"][0], f["foot"][2])
        if not heads:
            bad.append(f"head {f['head']} lands on no walkable surface")
        if not feet:
            bad.append(f"foot {f['foot']} lands on no walkable surface or the ground")
        rep_f[tag] = {"treads": f["treads"], "rise_m": f["rise_total"], "riser": f["rise"],
                      "going": f["going"], "width": f["width"], "from": heads, "to": feet}
        if bad:
            msgs.append(f"flight {tag}: " + "; ".join(bad))
            continue
        for a in heads:
            for b in feet:
                if a != b:
                    adj[a].add(b)
                    adj[b].add(a)
                    fl_edges.setdefault(tag, set()).add(frozenset((a, b)))
    for tag, L in ladders.items():
        bad = []
        if L["foot_y"] is None:
            msgs.append(f"ladder {tag}: no rails (ladder_{tag}_*)")
            continue
        pt = [0.0, 0.0, 0.0]
        pt[L["rax"]], pt[L["nax"]] = L["mid"], L["plane"]
        heads = [s for s in lands(L["head_y"], pt[0], pt[2]) if s != "GRADE"]
        feet = lands(L["foot_y"], pt[0], pt[2])
        if L["pitch"] > C15["pitch"] + 1e-6:
            bad.append(f"rung pitch {L['pitch']} > {C15['pitch']}")
        if L["first"] > C15["first"] + 1e-6:
            bad.append(f"first rung {L['first']} above the foot > {C15['first']}")
        if not heads:
            bad.append(f"top rung {L['head_y']} arrives on no walkable surface")
        if not feet:
            bad.append(f"foot {L['foot_y']} on no walkable surface or the ground")
        if heads:
            s = surf[heads[0]]
            c = (s["x"][0] + s["x"][1]) / 2.0 if L["nax"] == 0 else (s["z"][0] + s["z"][1]) / 2.0
            side = -1.0 if c > L["plane"] else 1.0              # climb on the side away from it
            lo, hi = np.empty(3), np.empty(3)
            lo[L["rax"]], hi[L["rax"]] = L["mid"] - C15["clear"] / 2.0, L["mid"] + C15["clear"] / 2.0
            a_, b_ = L["plane"] + side * 0.05, L["plane"] + side * C15["clear"]
            lo[L["nax"]], hi[L["nax"]] = min(a_, b_), max(a_, b_)
            lo[1], hi[1] = L["foot_y"] + 0.10, L["head_y"] + 1.0
            own = {n_ for n_ in names if n_.startswith(L["own_prefixes"])}
            hit = blocked(lo, hi, own)
            if hit:
                bad.append(f"climbing corridor blocked by {hit[:6]}")
        rep_l[tag] = {"rungs": L["rungs"],
                      "rise_m": round((surf[heads[0]]["y"] if heads else L["head_y"]) - L["foot_y"], 3),
                      "pitch": L["pitch"], "from": feet, "to": heads}
        if bad:
            msgs.append(f"ladder {tag}: " + "; ".join(bad))
            continue
        for a in heads:
            for b in feet:
                if a != b:
                    adj[a].add(b)
                    adj[b].add(a)
    # -- nitpick B2: ramps (RAMP) - an edge between the walkable surfaces at its head and at its foot (or the grade)
    # when the deck is <= 1:8 with no cross fall, >= RAMP clear wide between a handrail on EACH side at every station,
    # clear for C15 headroom over that width, and each end crosses >= RAMP crossing onto its landing, which is
    # >= RAMP landing deep (the walking area it belongs to, measured beyond the ramp end over the ramp's width)
    rep_r = {}
    need_clear = RAMP["clear"].get(role, RAMP["clear"]["base"])
    doors = {n_ for n_ in names if n_.startswith(C15_JOIN["door"])}
    ramp_ends = {}
    pad_arrivals = {}                                   # C15b: pad -> [(ramp axis, outward sign beyond the foot)]
    for tag, r in ramps.items():
        if "error" in r:
            msgs.append(f"ramp {tag}: {r['error']}")
            continue
        bad = []
        ax, cx = r["ax"], r["cx"]
        own = {r["part"]}
        if r["grade"] > RAMP["max_grade"] + 1e-4:
            bad.append(f"grade 1:{1.0 / r['grade']:.2f} ({r['grade'] * 100:.2f} %) steeper than "
                       f"1:{1.0 / RAMP['max_grade']:.0f}")
        if r["cross"] > RAMP["cross_slope"]:
            bad.append(f"cross fall {r['cross']:.4f} > {RAMP['cross_slope']}")
        c_lo, c_hi = r["across"][0] - 0.5, r["across"][1] + 0.5
        n_st = max(2, int(math.floor((r["length"] - 1.0) / RAMP["station"])) + 1)
        stations = [r["run"][0] + 0.5 + i * (r["length"] - 1.0) / (n_st - 1) for i in range(n_st)]
        widths, sides_bad, head_hit = [], [], set()
        for s_ in stations:
            p = [0.0, 0.0, 0.0]
            p[ax], p[cx] = s_, r["mid"]
            ys = _ramp_y(r, p[0], p[2])
            f0, f1, b0, b1 = _free_across(T_all, N_all, T_lo, T_hi, names, own, ax, cx, s_ - 0.05, s_ + 0.05,
                                          ys + RAMP["band"][0], ys + RAMP["band"][1], c_lo, c_hi, r["mid"])
            widths.append(f1 - f0)
            for b in (b0, b1):
                if b is None or part_kind(b) not in RAMP["rail_kinds"]:
                    sides_bad.append(f"{s_:.2f}: {b or 'nothing'}")
            a_lo, a_hi = s_ - RAMP["station"] / 2.0, s_ + RAMP["station"] / 2.0
            q0, q1 = list(p), list(p)
            q0[ax], q1[ax] = a_lo, a_hi
            y_a, y_b = _ramp_y(r, q0[0], q0[2]), _ramp_y(r, q1[0], q1[2])
            blo, bhi = np.empty(3), np.empty(3)
            blo[ax], bhi[ax] = a_lo, a_hi
            blo[cx], bhi[cx] = f0 + 0.05, f1 - 0.05
            blo[1], bhi[1] = max(y_a, y_b) + 0.10, min(y_a, y_b) + C15["headroom"]
            if bhi[cx] > blo[cx]:
                head_hit.update(blocked(blo, bhi, own))
        clear = min(widths) if widths else 0.0
        if clear < need_clear - 1e-6:
            bad.append(f"clear width {clear:.3f} < {need_clear} (between the rails)")
        if sides_bad:
            bad.append(f"no handrail bounding the walking width at {sides_bad[:4]}")
        if head_hit:
            bad.append(f"headroom blocked by {sorted(head_hit)[:6]}")
        heads = [x for x in lands(r["head"][1], r["head"][0], r["head"][2]) if x != "GRADE"]
        feet = lands(r["foot"][1], r["foot"][0], r["foot"][2])
        if not heads:
            bad.append(f"head {[round(v, 3) for v in r['head']]} lands on no walkable surface")
        if not feet:
            bad.append(f"foot {[round(v, 3) for v in r['foot']]} lands on no walkable surface or the ground")
        out_sign = 1.0 if r["head"][ax] > r["foot"][ax] else -1.0
        for x in feet:
            if x.startswith(WALK_PADS):
                pad_arrivals.setdefault(x, []).append((ax, -out_sign))
        ends = {}
        for end_tag, pnt, on, sgn in (("head", r["head"], heads, out_sign), ("foot", r["foot"], feet, -out_sign)):
            own_e = own | doors | {x for x in on} | {x + "_lip" for x in on}
            e_ax = pnt[ax]
            f0, f1, _b0, _b1 = _free_across(T_all, N_all, T_lo, T_hi, names, own_e, ax, cx, e_ax - C15_JOIN["depth"],
                                            e_ax + C15_JOIN["depth"], pnt[1] + C15_JOIN["foot"],
                                            pnt[1] + C15_JOIN["head"], c_lo, c_hi, r["mid"])
            ends[end_tag] = {"at": [round(v, 3) for v in pnt], "on": on, "crossing": round(f1 - f0, 3)}
            ramp_ends.setdefault(tag, []).extend(x for x in on if x != "GRADE")
            if on and f1 - f0 < RAMP["crossing"] - 1e-6:
                bad.append(f"{end_tag} crossing {f1 - f0:.3f} < {RAMP['crossing']}")
        rep_r[tag] = {"part": r["part"], "axis": r["axis"], "grade": round(r["grade"], 5),
                      "grade_1_in": round(1.0 / r["grade"], 3) if r["grade"] > 0 else None,
                      "cross_fall": round(r["cross"], 5), "run_m": round(r["length"], 3), "rise_m": round(r["rise"], 3),
                      "deck_width_m": round(r["across"][1] - r["across"][0], 3), "clear_m": round(clear, 3),
                      "stations": len(stations), "ends": ends, "from": feet, "to": heads}
        if bad:
            msgs.append(f"ramp {tag}: " + "; ".join(bad))
            continue
        for a in heads:
            for b in feet:
                if a != b:
                    adj[a].add(b)
                    adj[b].add(a)
    # -- C15b (nitpick B2; DECISIONS 166 as amended): on a station with the NO-LEVEL terraform override the player
    # molds the terrain up to the lot, so a concrete pad that a ramp ends on, standing on the grade, with its face
    # BEYOND the ramp's foot at the lot edge (within C15B edge of the lot line: the walker steps off it onto the molded
    # terrain) counts as grade. Every other station (and a pad off the edge, or one no ramp ends on) keeps C15 literal
    rep_b = {}
    W_, D_ = lot_cells(role)
    hx_, hz_ = 4.0 * W_, 4.0 * D_
    mode = TERRAFORM.get(role)
    for s in sl:
        if not s.startswith(WALK_PADS):
            continue
        d = surf[s]
        arr = pad_arrivals.get(s, [])
        lines = {0: ("x", hx_), 2: ("z", hz_)}
        edge = min([(lines[a_][1] - d[lines[a_][0]][1]) if sg > 0 else (d[lines[a_][0]][0] + lines[a_][1])
                    for a_, sg in arr] or [float("inf")])
        gy = float(pf[parts[s]][:, :, 1].min())
        why = []
        if mode != "no-level":
            why.append(f"the station's terraform is {mode!r}, not no-level")
        if not arr:
            why.append("no ramp ends on it")
        elif edge > C15B["edge"] + 1e-6:
            why.append(f"its outer face beyond the ramp's foot is {edge:.3f} m from the lot line (> {C15B['edge']})")
        if gy > C15B["ground"]:
            why.append(f"it does not stand on the grade (lowest y {gy:.3f})")
        rep_b[s] = {"terraform": mode, "edge_m": round(edge, 3) if arr else None, "ground_y": round(gy, 4),
                    "top_y": d["y"], "ramps_arriving": len(arr), "counts_as_grade": not why,
                    "why": why or ["a ramp's pad at the lot edge on a no-level station (C15b)"]}
        if not why:
            adj[s].add("GRADE")
            adj["GRADE"].add(s)
    seen, todo = {"GRADE"}, ["GRADE"]
    while todo:
        for b in sorted(adj[todo.pop()]):
            if b not in seen:
                seen.add(b)
                todo.append(b)
    orphans = [n for n in sl if n not in seen]
    if orphans:
        narrow = [f"{k_} {j['opening']:.3f} < {C15['clear']} (between {j['between']})"
                  for k_, j in sorted(joins.items()) if not j["ok"] and any(o in k_.split(" | ") for o in orphans)]
        msgs.append("orphan surfaces (no stair / ladder path to grade): " + ", ".join(
            f"{n} (y {surf[n]['y']:.2f}, x {surf[n]['x'][0]:.2f}..{surf[n]['x'][1]:.2f}, "
            f"z {surf[n]['z'][0]:.2f}..{surf[n]['z'][1]:.2f})" for n in orphans)
            + (("; narrow joins (crossing width): " + ", ".join(narrow)) if narrow else ""))
        nb = [f"{s} ({'; '.join(b['why'])})" for s, b in sorted(rep_b.items()) if not b["counts_as_grade"]]
        if nb:
            msgs.append("C15b: pads not counted as grade: " + ", ".join(nb))
    # nitpick B2: the ramps' landings are deep enough (the walking area beyond each end, over the ramp's width) and
    # every join of a ramp landing crosses >= RAMP crossing
    def _area(s0):
        grp, stack = set(), [s0]
        while stack:
            a = stack.pop()
            if a in grp:
                continue
            grp.add(a)
            stack += [b for b in adj[a] if b in surf and abs(surf[a]["y"] - surf[b]["y"]) <= C15["level"]
                      and _surf_gap(surf[a], surf[b]) <= C15["gap"]]
        return grp
    k_of = {0: "x", 2: "z"}
    for tag, rr in rep_r.items():
        r = ramps[tag]
        ax, cx = r["ax"], r["cx"]
        out_sign = 1.0 if r["head"][ax] > r["foot"][ax] else -1.0
        for end_tag, sgn in (("head", out_sign), ("foot", -out_sign)):
            e = rr["ends"][end_tag]
            on = [x for x in e["on"] if x != "GRADE"]
            if not on:
                e["landing_m"] = None
                continue
            e_ax = r["head"][ax] if end_tag == "head" else r["foot"][ax]
            depth = 0.0
            for s in sorted(set().union(*(_area(x) for x in on))):
                d = surf[s]
                ov = min(d[k_of[cx]][1], r["across"][1]) - max(d[k_of[cx]][0], r["across"][0])
                if ov < 1.0:
                    continue
                ext = (d[k_of[ax]][1] - e_ax) if sgn > 0 else (e_ax - d[k_of[ax]][0])
                depth = max(depth, ext)
            e["landing_m"] = round(depth, 3)
            if depth < RAMP["landing"] - 1e-3:                  # the joined mesh is float32 (1.4999995)
                msgs.append(f"ramp {tag}: the {end_tag} landing ({on}) is {depth:.3f} m deep < {RAMP['landing']}")
    ramp_land = {x for v in ramp_ends.values() for x in v}
    for k_, j in sorted(joins.items()):
        a, b = k_.split(" | ")
        if (a in ramp_land or b in ramp_land) and j.get("opening") is not None and j["opening"] < RAMP["crossing"] \
                and (a.startswith(RAMP_LANDINGS) or b.startswith(RAMP_LANDINGS)):
            msgs.append(f"ramp landing join {k_}: crossing {j['opening']:.3f} < {RAMP['crossing']}")
    # nitpick B2: the base ramp's goal - the side-area door (annexdoor_jamb0 / 1) opens onto a reached surface at its sill.
    # Nitpick B3: the OUTSIDE surface (it reaches past the jambs' outer face - the annex floor ends at it), reached from
    # grade by the ramps / joins alone (no flight): the stair inside must not stand in for a missing ramp
    def _bfs(graph, starts):
        seen_, par_, todo_ = set(starts), {s_: None for s_ in starts}, list(starts)
        while todo_:
            cur_ = todo_.pop(0)
            for b_ in sorted(graph[cur_]):
                if b_ not in seen_:
                    seen_.add(b_)
                    par_[b_] = cur_
                    todo_.append(b_)
        return seen_, par_

    def _without(graph, drop_tags):
        cut_ = set().union(*(fl_edges.get(t_, set()) for t_ in drop_tags)) if drop_tags else set()
        return {n_: {m_ for m_ in nb_ if frozenset((n_, m_)) not in cut_} for n_, nb_ in graph.items()}
    seen_ramps, _p = _bfs(_without(adj, list(fl_edges)), ["GRADE"])
    goal = None
    jambs = [n_ for n_ in names if n_.startswith("annexdoor_jamb")]
    if len(jambs) == 2:
        J = [pf[parts[n_]].reshape(-1, 3) for n_ in sorted(jambs)]
        xo = max(float(j_[:, 0].max()) for j_ in J)
        sill = min(float(j_[:, 1].min()) for j_ in J)
        z0d, z1d = float(J[0][:, 2].max()), float(J[1][:, 2].min())
        zc = (z0d + z1d) / 2.0
        at = [s for s, d in surf.items() if abs(d["y"] - sill) <= C15["level"] and _plan_gap(xo, zc, d) <= C15["land"]
              and d["z"][0] <= z0d + 1e-6 and d["z"][1] >= z1d - 1e-6 and d["x"][1] > xo + 0.02]
        goal = {"door": "annexdoor (the side area, annex +X glazing)", "sill_y": round(sill, 3),
                "opening_m": round(z1d - z0d, 3), "plane_x": round(xo, 3), "z": [round(z0d, 3), round(z1d, 3)],
                "onto": at, "reached": bool(at) and all(s in seen_ramps for s in at)}
        if not goal["reached"] or goal["opening_m"] < 0.90:
            msgs.append(f"the side-area door (sill {sill:.2f}, x {xo:.2f}, z {z0d:.2f}..{z1d:.2f}, opening "
                        f"{z1d - z0d:.2f}) opens onto {at or 'no walkable surface'}"
                        + ("" if at and all(s in seen_ramps for s in at) else " not reached from grade"))
    # -- nitpick B3 (ANNEX_STAIR): the side area is not a dead end. The hall's inner door (sfront_jamb0 / 1, the annex
    # door of the boarding hall) opens, past the wall of the annex, onto the annex's stair landing: from grade, over the
    # ramps and through the side-area door onto the annex floor (a walkable surface now), up a stair that is >= 1.20 m
    # wide and railed both sides, over the landing and the hall door, to the boarding deck - with NO other stair carrying
    # it (the -X stair would reach that deck anyway and hide a dead end). Reported as `route`
    route = None
    sj = sorted(n_ for n_ in names if n_ in ("sfront_jamb0", "sfront_jamb1"))
    if ANNEX_STAIR and goal is not None and len(sj) == 2 and "annexcap" in surf:
        Jh = [pf[parts[n_]].reshape(-1, 3) for n_ in sj]
        jx0 = min(float(j_[:, 0].min()) for j_ in Jh)
        jx1 = max(float(j_[:, 0].max()) for j_ in Jh)
        sill_i = min(float(j_[:, 1].min()) for j_ in Jh)
        zi0, zi1 = float(Jh[0][:, 2].max()), float(Jh[1][:, 2].min())
        zci = (zi0 + zi1) / 2.0
        cov = lambda d: d["z"][0] <= zi0 + 0.01 and d["z"][1] >= zi1 - 0.01
        at_hall = [s for s, d in surf.items() if abs(d["y"] - sill_i) <= C15["level"] and d["x"][1] <= jx1 + 1e-6
                   and _plan_gap(jx0, zci, d) <= C15["land"] and cov(d)]
        at_annex = [s for s, d in surf.items() if abs(d["y"] - sill_i) <= C15["level"] and d["x"][1] > jx1 + 0.02
                    and _plan_gap(jx1, zci, d) <= C15["land"] and cov(d)]
        rtags = sorted(t_ for t_ in fl_edges if any("annexcap" in e_ for e_ in fl_edges[t_]))
        others = [t_ for t_ in fl_edges if t_ not in rtags]
        reach_r, par_r = _bfs(_without(adj, others), ["GRADE"])
        rfl = {t_: rep_f[t_] for t_ in rtags}
        bad_r = []
        if not rtags:
            bad_r.append("no stair or ramp joins the annex floor (annexcap) to the hall - a dead end past the "
                         "side-area door")
        if not at_annex:
            bad_r.append(f"the hall's inner door (sill {sill_i:.2f}, z {zi0:.2f}..{zi1:.2f}) opens onto no walkable "
                         "landing on the annex side")
        if not at_hall:
            bad_r.append("the hall's inner door opens onto no walkable surface on the hall side")
        lost = [s for s in at_annex + at_hall if s not in reach_r]
        if lost and rtags and at_annex and at_hall:
            bad_r.append(f"{lost} not reached from grade through the side area (ramps -> side-area door -> annex floor "
                         f"-> stair {rtags})")
        for t_, r_ in rfl.items():
            if r_["width"] < ANNEX_ROUTE["width"] - 1e-6:
                bad_r.append(f"stair {t_} is {r_['width']:.3f} m wide < {ANNEX_ROUTE['width']}")
        # railed both sides: the kit rails along each flight edge (above its treads, within 0.30 m of the edge)
        rail_clear = {}
        for t_ in rtags:
            f_ = flights[t_]
            ax_, cx_ = (2, 0) if f_["axis"] == "z" else (0, 2)
            lo_, hi_ = f_["lo"], f_["hi"]
            side_lo, side_hi = [], []
            for n_ in names:
                if part_kind(n_) != "rail":
                    continue
                Q_ = pf[parts[n_]].reshape(-1, 3)
                qlo, qhi = Q_.min(axis=0), Q_.max(axis=0)
                if qlo[ax_] < lo_[ax_] - 0.30 or qhi[ax_] > hi_[ax_] + 0.30 or qlo[1] < lo_[1] or qhi[1] > hi_[1] + 2.0:
                    continue
                c_ = (qlo[cx_] + qhi[cx_]) / 2.0
                if lo_[cx_] - 0.30 <= c_ <= lo_[cx_] + 0.30:
                    side_lo.append(float(qhi[cx_]))
                elif hi_[cx_] - 0.30 <= c_ <= hi_[cx_] + 0.30:
                    side_hi.append(float(qlo[cx_]))
            if not side_lo or not side_hi:
                bad_r.append(f"stair {t_} has no handrail on its {'low' if not side_lo else 'high'} side")
            else:
                rail_clear[t_] = round(min(side_hi) - max(side_lo), 3)
                if rail_clear[t_] < ANNEX_ROUTE["width"] - 1e-6:
                    bad_r.append(f"stair {t_} is {rail_clear[t_]:.3f} m clear between its rails < {ANNEX_ROUTE['width']}")
        # the doorway between the hall door and the stair head: a body-wide passage (z in from each jamb) at C15
        # headroom over the sill, from just past the hall door's annex face to the landing's far edge
        passage = None
        if at_annex and at_hall:
            x_far = max(surf[s]["x"][1] for s in at_annex)
            plo = np.array([jx1 + ANNEX_ROUTE["after"], 0.0, 0.0])
            phi = np.array([x_far, 0.0, 0.0])
            plo[1], phi[1] = sill_i + C15_JOIN["foot"], sill_i + C15["headroom"]
            plo[2], phi[2] = zi0 + ANNEX_ROUTE["passage"], zi1 - ANNEX_ROUTE["passage"]
            hit_p = blocked(plo, phi, doors | set(at_annex) | set(at_hall))
            passage = {"x": [round(float(plo[0]), 3), round(float(phi[0]), 3)],
                       "z": [round(float(plo[2]), 3), round(float(phi[2]), 3)],
                       "y": [round(float(plo[1]), 3), round(float(phi[1]), 3)], "blocked_by": hit_p}
            if hit_p:
                bad_r.append(f"the doorway between the hall door and the stair head is blocked by {hit_p[:6]}")
        chain = None
        tgt = next((s for s in at_hall if s in reach_r), None)
        if tgt is not None:
            chain, cur = [], tgt
            while cur is not None:
                chain.append(cur)
                cur = par_r[cur]
            chain.reverse()
        route = {"door_inner": "sfront (the boarding hall's +X wall, the annex door)", "sill_y": round(sill_i, 3),
                 "opening_m": round(zi1 - zi0, 3), "annex_side": at_annex, "hall_side": at_hall,
                 "stairs": rtags, "stair_width_m": {t_: r_["width"] for t_, r_ in rfl.items()},
                 "stair_between_rails_m": rail_clear, "passage": passage,
                 "chain": chain, "ok": not bad_r, "problems": bad_r,
                 "carried_by_no_other_stair": [t_ for t_ in others], "reached": sorted(reach_r - {"GRADE"})}
        if bad_r:
            msgs.append("the side area is a dead end: " + "; ".join(bad_r))
    # detail pass 1 (N2): every standable top is a walkable surface or declared not walkable
    stand = standable_parts(pf, parts, names, T_all, N_all, T_lo, T_hi)
    undeclared = {n: d for n, d in stand.items() if _not_walkable_reason(n) is None}
    if undeclared:
        msgs.append("undeclared standable surfaces (add the prefix to WALK_PREFIXES with access, or to "
                    "NOT_WALKABLE with a reason): " + ", ".join(
                        f"{n} (y {d['y']:.2f}, {d['area']:.1f} m2, clear at {d['at']})"
                        for n, d in sorted(undeclared.items())[:8]))
    # railings imply a walkable surface: every kit post stands on one or on a flight
    loose = []
    for n in names:
        if not re.fullmatch(r"post_.+_\d+", n):
            continue
        P = pf[parts[n]].reshape(-1, 3)
        fy = float(P[:, 1].min())
        F = P[P[:, 1] <= fy + 1e-4]
        fx, fz = float(F[:, 0].mean()), float(F[:, 2].mean())
        ok = any(abs(d["y"] - fy) <= C15["level"] and _plan_gap(fx, fz, d) <= C15["post_on"]
                 for d in surf.values())
        for f in flights.values():
            if ok or "lo" not in f:
                continue
            lo, hi = f["lo"], f["hi"]
            ok = (lo[0] - 0.15 <= fx <= hi[0] + 0.15 and lo[2] - 0.15 <= fz <= hi[2] + 0.15
                  and lo[1] - C15["land"] <= fy <= hi[1] + C15["land"])
        for r in ramps.values():                       # nitpick B2: a post on a ramp deck (its plane under the foot)
            if ok or "error" in r:
                continue
            pa, pc = (fx, fz) if r["ax"] == 0 else (fz, fx)
            ok = (r["run"][0] - 0.15 <= pa <= r["run"][1] + 0.15 and r["across"][0] - 0.15 <= pc <= r["across"][1] + 0.15
                  and abs(fy - _ramp_y(r, fx, fz)) <= C15["land"])
        if not ok:
            loose.append(f"{n} ({fx:.2f}, {fy:.2f}, {fz:.2f})")
    if loose:
        msgs.append("railing posts on no walkable surface or flight (a railed platform C15 does not "
                    "know: add its prefix to WALK_PREFIXES): " + ", ".join(loose[:8]))
    areas, done = [], set()
    for n in sl:
        if n in done:
            continue
        grp, stack = set(), [n]
        while stack:
            a = stack.pop()
            if a in grp:
                continue
            grp.add(a)
            stack += [b for b in adj[a] if b in surf and abs(surf[a]["y"] - surf[b]["y"]) <= C15["level"]
                      and _surf_gap(surf[a], surf[b]) <= C15["gap"]]
        done |= grp
        areas.append(sorted(grp))
    rep = {"surfaces": {n: [surf[n]["y"], surf[n]["x"], surf[n]["z"]] for n in sl},
           "areas": areas, "flights": rep_f, "ladders": rep_l, "orphans": orphans,
           "ramps": rep_r, "C15b": rep_b, "goal": goal,             # nitpick B2
           "route": route,                                   # nitpick B3 (None unless ANNEX_STAIR on the base)
           "posts": sum(1 for n in names if re.fullmatch(r"post_.+_\d+", n)),
           "joins": joins,
           "not_walkable": {n: {"why": _not_walkable_reason(n)[0], **d} for n, d in sorted(stand.items())
                            if _not_walkable_reason(n) is not None},
           "undeclared": sorted(undeclared)}
    if msgs:
        _fail("C15", "; ".join(msgs))
    return rep


def check_subobjects(obj, role):
    """C16 (detail pass 1, skeptic MC10: box-test every sub-object footprint). For each SUBOBJ_ROWS
    entry, the vanilla placeholder's variant-UNION box (cs2_liftdress.subobj_box; e.g. the park light's
    v03 arm to +0.83, the warning light's 1.95 m v02) at its pose:
      clear   - shrunk 0.01 m it holds no station triangle;
      carrier - it stays out of the C4 carrier zones (cabin body, hanger ring, carriage);
      lot     - it stays inside the lot in plan;
      support - grade / deck / beam: a surface (or the grade, y 0) <= 0.03 m under its origin; wall:
                a face <= 0.05 m behind it; ceiling: a face <= 0.03 m above it.
    Returns {"rows", "block"} (block = the surgery manifest rows); raises ContractError("C16 ...")."""
    from mathutils.bvhtree import BVHTree
    LD = _LD()
    tri, tnames = triangles(obj)
    lo_t, hi_t = tri.min(axis=1), tri.max(axis=1)
    bvh = BVHTree.FromPolygons(tri.reshape(-1, 3).tolist(),
                               [(3 * i, 3 * i + 1, 3 * i + 2) for i in range(len(tri))], all_triangles=True)
    W, D = lot_cells(role)
    rows, msgs = [], []
    top_mesh = float(tri[..., 1].max())
    for prefab, pos, facing, parent, mount in subobj_rows(role):
        q = LD.facing_quat(facing)
        blo, bhi = LD.subobj_box(prefab, pos, q, frame="game")
        r = {"prefab": prefab, "pos": [float(v) for v in pos], "rot": list(q), "parent": parent,
             "mount": mount, "box": [[round(float(v), 3) for v in blo], [round(float(v), 3) for v in bhi]]}
        lo_s, hi_s = blo + 0.01, bhi - 0.01
        cand = np.nonzero(np.all(lo_t <= hi_s, axis=1) & np.all(hi_t >= lo_s, axis=1))[0]
        hits = sorted({tnames[i] for i in cand[_tri_box_overlap(tri[cand], lo_s, hi_s)]}) if len(cand) else []
        if hits:
            msgs.append(f"{prefab} at {pos}: box crosses {hits[:6]}")
        for label, zlo, zhi, _allowed in carrier_zones(role):
            zlo, zhi = np.array(zlo) + 0.05, np.array(zhi) - 0.05
            if np.all(blo < zhi) and np.all(bhi > zlo):
                msgs.append(f"{prefab} at {pos}: inside carrier zone {label}")
        if max(-blo[0], bhi[0]) > 4.0 * W + LOT_SLACK or max(-blo[2], bhi[2]) > 4.0 * D + LOT_SLACK:
            msgs.append(f"{prefab} at {pos}: box leaves the {W}x{D} lot")
        x, y, z = (float(v) for v in pos)
        sup, tol = None, 0.03
        if mount == "grade" and abs(y) < 1e-6:
            sup = 0.0
        elif mount in ("grade", "deck", "beam"):
            hit = bvh.ray_cast(Vector((x, y + 0.02, z)), Vector((0.0, -1.0, 0.0)), 0.20)
            sup = None if hit[0] is None else float(hit[3]) - 0.02
        elif mount == "ceiling":
            hit = bvh.ray_cast(Vector((x, y - 0.02, z)), Vector((0.0, 1.0, 0.0)), 0.20)
            sup = None if hit[0] is None else float(hit[3]) - 0.02
        elif mount == "wall":
            fx, fz = {"+X": (1.0, 0.0), "-X": (-1.0, 0.0), "+Z": (0.0, 1.0), "-Z": (0.0, -1.0)}[facing]
            hit = bvh.ray_cast(Vector((x + 0.02 * fx, y, z + 0.02 * fz)), Vector((-fx, 0.0, -fz)), 0.20)
            sup, tol = (None if hit[0] is None else float(hit[3]) - 0.02), 0.05
        r["support_m"] = None if sup is None else round(sup, 4)
        if sup is None or sup > tol:
            msgs.append(f"{prefab} at {pos}: no {mount} support within {tol} m ({sup})")
        if bhi[1] > top_mesh + 1e-6:
            r["above_mesh_top_m"] = round(float(bhi[1]) - top_mesh, 3)
        rows.append(r)
    if msgs:
        _fail("C16", "; ".join(msgs))
    # x-sign gate N (2026-10-15 playtest; T14 / S1b-N 2026-10-16): everything above is the DESIGN (prefab-numbers)
    # frame, X = FBX-raw x, which the game negates on import. The manifest block is the GAME frame: rule T
    # (x -> -x, q -> (qx, -qy, -qz, qw); cs2_liftdress.rule_t_pos / rule_t_rot, fine_detail.design_row_to_game).
    # r["pos"] / r["rot"] in the report rows stay design-frame (the box tests above run in it).
    vrows = [(r["prefab"], LD.rule_t_pos(r["pos"]), LD.rule_t_rot(r["rot"]), r["parent"]) for r in rows]
    kit = fd_kit_rows(role)
    if FINE_DETAIL and DETAIL1:
        import fine_detail as FD
        kit = [FD.design_row_to_game(k) for k in kit]
        # fine-detail step 3c: + the dressing kit's "mod" rows (cs2_liftdress.sub_object_rows fine=True: kinds,
        # x_convention = the default STATION_X_CONVENTION: game x = -authoring y = -(design X))
        block = LD.sub_object_rows(PREFAB_NAMES[role], vrows + kit, fine=True)
    else:
        block = LD.sub_object_rows(PREFAB_NAMES[role], vrows)
    return {"rows": rows, "block": block}


def cabin_door_geometry():
    """C17 input (revision 1e): the tram cabin's door as lift_cabin.py builds it - the leaves span
    +-x_out (0.850) about the cabin's length center (they meet at travel 0), the jamb seals reach
    jamb[1] (0.866) - and the dock z the cabin is hosted at. The shipped cabin manifest carries the
    hosted z_bay and the T13 door contract's name but no door numbers, so the numbers come from the
    module (the same constants the cabin's T13 asserts) and the manifest, when exported, must agree
    on the dock z and report T13 held on LOD0."""
    try:
        import lift_cabin as LCab
        D = LCab.T["door"]
        half, jamb = float(D["x_out"]), float(D["jamb"][1])
        z_code = float(LCab.TRAM_STATION["dock_z"])
    except Exception as e:  # noqa: BLE001
        _fail("C17", f"cannot read the cabin door geometry from lift_cabin.py ({e!r})")
    out = {"source": "lift_cabin.T['door'] (x_out, jamb) + TRAM_STATION['dock_z']", "centre": z_code,
           "half": half, "jamb_half": jamb, "meet_half": float(D["x_meet"]),
           "manifest": None, "manifest_z_bay": None, "t13_held": None}
    if os.path.isfile(CABIN_MANIFEST):
        with open(CABIN_MANIFEST, encoding="utf-8") as fh:
            mf = json.load(fh)
        zb = float(mf["prefab_numbers"]["hosted_poses"]["station"]["z_bay"])
        held = "T13" in (mf.get("contracts_held") or {}).get("LOD0", [])
        out.update({"manifest": os.path.relpath(CABIN_MANIFEST, REPO).replace("\\", "/"),
                    "manifest_z_bay": zb, "t13_held": bool(held)})
        if abs(zb - z_code) > 1e-6:
            _fail("C17", f"the cabin manifest hosts the cabin at z_bay {zb} but lift_cabin.py says {z_code}")
        if not held:
            _fail("C17", "the cabin manifest does not list T13 (the door contract) held on LOD0")
    return out


def check_gate_door(obj, role):
    """C17 (revision 1e; revision2-check-tram-cabin section C): gate span vs cabin door. On the joined,
    oriented LOD0, for each of the four berth edges (GATE_EDGES) - the cabin docks on both sides of both
    bays, doors on both long sides (R29) - the `gatepost_<e>_<i>` / `gate_<e>_<i>` parts must leave the
    door span clear:
      center  - the door center (the cabin's hosted z, from lift_cabin.py and its manifest) is DOCK_Z;
      inside  - no post and no leaf has z overlap with the door span (center -+ x_out, 1.70 m): a part
                standing in front of a door FAILS (the 1d base post at -6.20, the 1d top post at -5.60);
      opening - the clear gap round the door, from the nearest part below to the nearest part above, is
                >= C17_OPEN_MIN (1.70 m) wide, is bounded by a POST each side, and is centered on the door
                seam within C17_CENTRE_TOL, and the leaf each side of it is at least half the opening
                long, so the parked leaf pair can close on the seam.
    Returns the report {"door", "edges": {e: ...}}; raises ContractError("C17 ...")."""
    door = cabin_door_geometry()
    c, half = door["centre"], door["half"]
    msgs = []
    if abs(c - DOCK_Z) > 1e-6:
        msgs.append(f"the cabin's door centre z {c} is not the station's DOCK_Z {DOCK_Z}")
    pf, parts = _part_tris(obj)
    span = (c - half, c + half)
    open_min = max(C17_OPEN_MIN, 2.0 * half)
    edges = {}
    for e, ex in enumerate(GATE_EDGES):
        ex = round(ex + frame_dx(role), 6)         # nitpick B2: the berth edges in the prefab frame
        rows = []
        for nm, tris in parts.items():
            m = re.fullmatch(r"(gatepost|gate)_(\d+)_(\d+)", nm)
            if not m or int(m.group(2)) != e:
                continue
            P = pf[tris].reshape(-1, 3)
            rows.append((float(P[:, 2].min()), float(P[:, 2].max()), nm, m.group(1),
                         float(P[:, 0].min()), float(P[:, 0].max())))
        if not rows:
            msgs.append(f"edge {e} (x {ex}): no gate parts")
            continue
        rows.sort()
        xs = (min(r[4] for r in rows), max(r[5] for r in rows))
        if not (abs(xs[0] - ex) <= 0.12 and abs(xs[1] - ex) <= 0.12):
            msgs.append(f"edge {e}: gate parts at x {xs[0]:.3f}..{xs[1]:.3f}, not on the berth edge {ex}")
        inside = [(nm, round(lo, 3), round(hi, 3)) for lo, hi, nm, _k, _x0, _x1 in rows
                  if hi > span[0] + 1e-6 and lo < span[1] - 1e-6]
        if inside:
            msgs.append(f"edge {e} (x {ex}): {len(inside)} gate part(s) inside the door span "
                        f"z {span[0]:.2f}..{span[1]:.2f}: "
                        + ", ".join(f"{nm} z {lo}..{hi}" for nm, lo, hi in inside[:6]))
            continue
        below = [r for r in rows if r[1] <= span[0] + 1e-6]
        above = [r for r in rows if r[0] >= span[1] - 1e-6]
        if not below or not above:
            msgs.append(f"edge {e} (x {ex}): no gate part on the "
                        f"{'-z' if not below else '+z'} side of the door (the opening has no end)")
            continue
        lo_r = max(below, key=lambda r: r[1])
        hi_r = min(above, key=lambda r: r[0])
        opening, centre = hi_r[0] - lo_r[1], (hi_r[0] + lo_r[1]) / 2.0
        # the leaf pair parked either side must be long enough to close on the seam (each >= half the opening)
        leaf_lo = max((r for r in below if r[3] == "gate"), key=lambda r: r[1], default=None)
        leaf_hi = min((r for r in above if r[3] == "gate"), key=lambda r: r[0], default=None)
        len_lo = None if leaf_lo is None else leaf_lo[1] - leaf_lo[0]
        len_hi = None if leaf_hi is None else leaf_hi[1] - leaf_hi[0]
        rep = {"x": ex, "opening_m": round(opening, 4), "centre_z": round(centre, 4),
               "lo_post": lo_r[2], "hi_post": hi_r[2],
               "margin_lo_m": round(span[0] - lo_r[1], 4), "margin_hi_m": round(hi_r[0] - span[1], 4),
               "leaf_lo_m": None if len_lo is None else round(len_lo, 4),
               "leaf_hi_m": None if len_hi is None else round(len_hi, 4),
               "posts": sum(1 for r in rows if r[3] == "gatepost"),
               "leaves": sum(1 for r in rows if r[3] == "gate")}
        edges[e] = rep
        if len_lo is None or len_hi is None or min(len_lo, len_hi) < opening / 2.0 - 1e-6:
            lens = ", ".join("none" if v is None else f"{v:.3f}" for v in (len_lo, len_hi))
            msgs.append(f"edge {e} (x {ex}): the leaves beside the opening ({lens} m) cannot close on "
                        f"the seam (each must reach half the {opening:.3f} m opening)")
        if lo_r[3] != "gatepost" or hi_r[3] != "gatepost":
            msgs.append(f"edge {e}: the opening is bounded by {lo_r[2]} / {hi_r[2]}, not by posts")
        if opening < open_min - 1e-6:
            msgs.append(f"edge {e} (x {ex}): opening {opening:.3f} m < {open_min:.2f} m (door span)")
        if abs(centre - c) > C17_CENTRE_TOL:
            msgs.append(f"edge {e} (x {ex}): opening centre z {centre:.3f} is {centre - c:+.3f} m off the "
                        f"door seam {c}")
    if msgs:
        _fail("C17", "; ".join(msgs))
    return {"door": door, "span_z": [round(span[0], 4), round(span[1], 4)], "open_min_m": open_min,
            "edges": edges}


# --------------------------------------------------------------------------------------
# Revision 2: C18 / C18b see-through (walk-around back-face first hits, closed rims) and C19 shimmer
# (visible same-facing exact coplanar overlaps). Measured on the 1e builds first (design 1.1 / 1.2, skeptic
# MC8 baselines; build/tram-stations-rev2.md section 4): top LOD0 3,798 back hits of 482,292, LOD1 1,723,
# LOD2 196; base 565 / 74 / 61 - the open rims, drips, the clock-tower base and the render panel.
# --------------------------------------------------------------------------------------

# The walk-around camera set (design 1.1): pinhole cameras aimed at the station's sphere, azimuth 0-345 step 15,
# per LOD a distance / elevation set and a ray grid; a ray whose FIRST hit is a back face (winding normal . ray
# > 0) shows what lies behind that face in game (back-face culling).
C18_WALK = {"top": ((0.0, 13.5, 0.0), 19.0), "base": ((1.8, 11.0, 0.0), 22.0)}
C18_LEVELS = {0: {"dists": (45.0, 90.0, 160.0), "elevs": (12, 25, 40, 55, 70), "n": 72, "max": 5},
              1: {"dists": (160.0, 260.0), "elevs": (25, 45, 65), "n": 64, "max": 3},
              2: {"dists": (400.0,), "elevs": (25, 45), "n": 48, "max": 3}}
C18_AZ = tuple(range(0, 360, 15))
# Known single-sided parts C18 reports but does not fail (exact owner and reason; the C14 KNOWN_OPEN pattern): the
# A-frame KIT rack's gear (cs2_bldg._ski_pair_into / _board_into build ONE-sided top sheets and bases, 3 cm
# apart; seen edge-on from behind a strip shows what lies behind it). The tram must carry the kit's rack
# unchanged (design D1: one rack look project-wide, names + tris identical); the L1 / L2 / school / L5 racks
# are the same. Owner: the kit (cs2_bldg), build/tram-stations-rev2.md open items.
C18_KNOWN = {r"^skirack_[a-z0-9]+g[A-Z]+$": "cs2_bldg ski gear strips are single-sided (kit trait; owner cs2_bldg)"}
C19_FAIL_CM2 = 25.0                 # per part pair, visible
C19_LIST_CM2 = 1.0                  # every pair at least this big is listed in the manifest (C19.all)


def walk_views(role, level):
    """[(eye, fwd, right, up, tan(half fov), n)] of the C18 walk set (numpy, prefab frame)."""
    (cx, cy, cz), R = C18_WALK[role]
    spec = C18_LEVELS[level]
    C = np.array((cx, cy, cz), np.float64)
    out = []
    for d in spec["dists"]:
        half = math.atan(R / d)
        for el in spec["elevs"]:
            for az in C18_AZ:
                e, a = math.radians(el), math.radians(az)
                eye = C + np.array((d * math.cos(e) * math.sin(a), d * math.sin(e), d * math.cos(e) * math.cos(a)))
                fwd = (C - eye) / np.linalg.norm(C - eye)
                right = np.cross(fwd, (0.0, 1.0, 0.0))
                right /= np.linalg.norm(right)
                out.append((eye, fwd, right, np.cross(right, fwd), math.tan(half), spec["n"]))
    return out


def _tri_bvh(tri):
    from mathutils.bvhtree import BVHTree
    return BVHTree.FromPolygons(tri.reshape(-1, 3).tolist(), [(3 * i, 3 * i + 1, 3 * i + 2) for i in range(len(tri))],
                                all_triangles=True)


def walk_back_faces(tri, names, role, level, bvh=None):
    """The C18 walk on prefab triangles: {"views", "rays", "hits", "back", "parts": [{part, back, front, to_sky,
    views, y}], "eyes"} ("to_sky" = the next hit behind the back face is nothing)."""
    bvh = bvh or _tri_bvh(tri)
    fn = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0])
    views = walk_views(role, level)
    back, front, sky, vws = {}, {}, {}, {}
    ylo, yhi = {}, {}
    rays = hits = 0
    for eye, fwd, right, up, th, N in views:
        g = (2.0 * (np.arange(N) + 0.5) / N - 1.0) * th
        U, V = np.meshgrid(g, g, indexing="ij")
        D = fwd[None, :] + right[None, :] * U.reshape(-1, 1) + up[None, :] * V.reshape(-1, 1)
        D /= np.linalg.norm(D, axis=1)[:, None]
        e = Vector(eye.tolist())
        seen = set()
        for dr in D.tolist():
            rays += 1
            loc, _nor, idx, _dist = bvh.ray_cast(e, dr)
            if idx is None:
                continue
            hits += 1
            nm = names[idx]
            f = fn[idx]
            if f[0] * dr[0] + f[1] * dr[1] + f[2] * dr[2] > 0.0:
                back[nm] = back.get(nm, 0) + 1
                seen.add(nm)
                ylo[nm] = min(ylo.get(nm, loc.y), loc.y)
                yhi[nm] = max(yhi.get(nm, loc.y), loc.y)
                if bvh.ray_cast(loc + Vector(dr) * 0.002, dr)[2] is None:
                    sky[nm] = sky.get(nm, 0) + 1
            else:
                front[nm] = front.get(nm, 0) + 1
        for nm in seen:
            vws[nm] = vws.get(nm, 0) + 1
    rows = [{"part": nm, "back": k_, "front": front.get(nm, 0), "to_sky": sky.get(nm, 0), "views": vws[nm],
             "y": [round(ylo[nm], 2), round(yhi[nm], 2)]}
            for nm, k_ in sorted(back.items(), key=lambda kv: (-kv[1], kv[0]))]
    return {"level": level, "views": len(views), "rays": rays, "hits": hits, "back": int(sum(back.values())),
            "parts": rows, "eyes": [v[0] for v in views]}


def open_rim_edges(tri, names):
    """C18b: {part: boundary edges} of every `*rim*` part (vertices welded at 1e-5 m)."""
    out = {}
    groups = {}
    for i, nm in enumerate(names):
        if "rim" in nm:
            groups.setdefault(nm, []).append(i)
    for nm, ii in sorted(groups.items()):
        key = np.round(tri[ii].reshape(-1, 3) / 1e-5).astype(np.int64)
        _u, inv = np.unique(key, axis=0, return_inverse=True)
        v = inv.reshape(-1, 3)
        e = np.sort(np.concatenate([v[:, [0, 1]], v[:, [1, 2]], v[:, [2, 0]]]), axis=1)
        e = e[e[:, 0] != e[:, 1]]
        _ue, cnt = np.unique(e, axis=0, return_counts=True)
        out[nm] = int((cnt == 1).sum())
    return out


def check_see_through(obj, role, level=0):
    """C18 (revision 2, design 5 / skeptic MC1 + MC8): on the joined, oriented mesh of LOD `level` (after the
    `_Win` split), the walk-around set of C18_LEVELS[level]; FAIL when any part's first hit is a back face on
    more than C18_LEVELS[level]["max"] rays (LOD0 5, LOD1 / LOD2 3), naming the part, the count, its y band,
    the rays that see sky through it and the views. C18b (LOD0): every `*rim*` part is closed (0 boundary
    edges) - it names the cause before the walk. Returns (report, walk) - walk carries the eyes + BVH that C19
    reuses; raises ContractError("C18b ..." / "C18 ...")."""
    tri, names = triangles(obj)
    bvh = _tri_bvh(tri)
    msgs, code = [], "C18"
    rims = open_rim_edges(tri, names) if level == 0 else None
    if rims and any(rims.values()):
        code = "C18b"
        msgs.append("open rim bores (boundary edges): " + ", ".join(f"{n} {c}" for n, c in rims.items() if c))
    w = walk_back_faces(tri, names, role, level, bvh)
    mx = C18_LEVELS[level]["max"]
    known = [dict(r, why=why) for r in w["parts"] for pat, why in C18_KNOWN.items() if re.match(pat, r["part"])]
    bad = [r for r in w["parts"] if r["back"] > mx and not any(re.match(p_, r["part"]) for p_ in C18_KNOWN)]
    if bad:
        msgs.append(f"LOD{level} back-face first hits (> {mx} per part, of {w['hits']} hits / {w['rays']} rays, "
                    f"{w['views']} views): " + ", ".join(
                        f"{r['part']} {r['back']} (y {r['y'][0]:.2f}-{r['y'][1]:.2f}, {r['to_sky']} to sky, "
                        f"{r['views']} views)" for r in bad[:8]))
    rep = {k_: w[k_] for k_ in ("level", "views", "rays", "hits", "back")}
    rep.update({"max_per_part": mx, "parts": w["parts"][:20], "rims_open": rims, "known": known})
    if msgs:
        _fail(code, "; ".join(msgs))
    return rep, {"eyes": w["eyes"], "bvh": bvh, "tri": tri, "names": names}


def kit_key(name):
    """C19 exemption (design 5): the parts of ONE kit builder call share a key - a handrail (post_ / rail_ /
    kick_<n>_*), a grating and its lip, a sheave (rim + hub, renamed ones too), a ski rack, a door kit, one
    cs2_liftdress dressing builder; None for everything else."""
    pats = ((r"^(?:post|rail|kick)_(.+)_[^_]+$", "rail"), (r"^grate_(.+?)(?:_lip)?$", "grate"),
            (r"^sheave_(.+)_(?:rim|hub)$", "sheave"), (r"^(?:fs|bwd)(?:rim|hub)_(\w+)$", "sheave"),
            (r"^skirack_([a-z0-9]+?)(?:g[A-Z]+)?$", "rack"),
            (r"^door_(.+)_(?:leaf|jamb0|jamb1|head|handle|kick)$", "door"),
            (r"^(?:ext|hosereel|chev|gauge|hook|tool)_([A-Za-z0-9]+)", "dress"))
    for pat, kind in pats:
        m = re.match(pat, name)
        if m:
            return (kind, re.match(r"[a-z]+", name).group(0) if kind == "dress" else "", m.group(1))
    return None


def coplanar_pairs(tri, names, ang_deg=1.0, tol=0.0005, min_m2=1e-5):
    """Same-facing exact coplanar overlap per part pair (the tram_tower.coplanar_overlaps tolerances: normals within
    1 deg, both planes within 0.5 mm on the clipped overlap polygon, area > 0.1 cm2; triangles sharing an edge
    skipped), candidates bucketed by normal. Labels come from the in-process part table (skeptic MC2: never by
    nearest triangle). {(part_a, part_b): {"cm2", "polys": [(area, 3D polygon)], "n"}}."""
    P = tri
    fn = np.cross(P[:, 1] - P[:, 0], P[:, 2] - P[:, 0])
    ln = np.linalg.norm(fn, axis=1)
    keep = np.nonzero(ln > 2e-10)[0]
    n = np.zeros_like(fn)
    n[keep] = fn[keep] / ln[keep, None]
    _u, inv = np.unique(np.round(P.reshape(-1, 3) / 1e-5).astype(np.int64), axis=0, return_inverse=True)
    vid = inv.reshape(-1, 3)
    nq = np.round(n * 60.0).astype(np.int64)
    buckets = {}
    for i in keep.tolist():
        buckets.setdefault(tuple(nq[i]), []).append(i)
    lo, hi = P.min(axis=1), P.max(axis=1)
    cosmax = math.cos(math.radians(ang_deg))
    out, done = {}, set()
    for bk in sorted(buckets):
        cand = set()
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                for dz in (-1, 0, 1):
                    cand.update(buckets.get((bk[0] + dx, bk[1] + dy, bk[2] + dz), ()))
        A = np.array(buckets[bk], np.int64)
        B = np.array(sorted(cand), np.int64)
        for s0 in range(0, len(A), 256):
            a_ = A[s0:s0 + 256]
            m = np.all((lo[a_][:, None, :] <= hi[B][None, :, :] + 6e-4)
                       & (lo[B][None, :, :] <= hi[a_][:, None, :] + 6e-4), axis=2)
            for ia, jb in zip(*[x.tolist() for x in np.nonzero(m)]):
                a, b = int(a_[ia]), int(B[jb])
                if a == b or (min(a, b), max(a, b)) in done:
                    continue
                done.add((min(a, b), max(a, b)))
                if float(n[a] @ n[b]) < cosmax or len(set(vid[a].tolist()) & set(vid[b].tolist())) >= 2:
                    continue
                if np.abs((P[b] - P[a, 0]) @ n[a]).max() > 0.05:
                    continue
                o = P[a, 0]
                ex = (P[a, 1] - o) / np.linalg.norm(P[a, 1] - o)
                ey = np.cross(n[a], ex)
                t1 = [((p - o) @ ex, (p - o) @ ey) for p in P[a]]
                t2 = [((p - o) @ ex, (p - o) @ ey) for p in P[b]]
                for t_ in (t1, t2):
                    if (t_[1][0] - t_[0][0]) * (t_[2][1] - t_[0][1]) - (t_[1][1] - t_[0][1]) * (t_[2][0] - t_[0][0]) < 0:
                        t_.reverse()
                ov = _clip_convex2(t2, t1)
                if len(ov) < 3:
                    continue
                area = 0.5 * abs(sum(ov[i][0] * ov[(i + 1) % len(ov)][1] - ov[(i + 1) % len(ov)][0] * ov[i][1]
                                     for i in range(len(ov))))
                if area <= min_m2:
                    continue
                pts3 = [o + ex * q[0] + ey * q[1] for q in ov]
                if max(abs(float((q - P[b, 0]) @ n[b])) for q in pts3) > tol:
                    continue
                key = tuple(sorted((names[a], names[b])))
                d = out.setdefault(key, {"cm2": 0.0, "polys": [], "n": n[a].copy()})
                d["cm2"] += area * 1e4
                d["polys"].append((area, np.array(pts3)))
    return out


def _clip_convex2(subj, clipper):
    """Sutherland-Hodgman: convex `subj` clipped by convex CCW `clipper` (2D point lists)."""
    out = list(subj)
    for i in range(len(clipper)):
        (ax, ay), (bx, by) = clipper[i], clipper[(i + 1) % len(clipper)]
        inp, out = out, []
        if not inp:
            break
        s = inp[-1]
        for e in inp:
            ein = (bx - ax) * (e[1] - ay) - (by - ay) * (e[0] - ax) >= -1e-12
            sin_ = (bx - ax) * (s[1] - ay) - (by - ay) * (s[0] - ax) >= -1e-12
            if ein != sin_:
                dx, dy, ex_, ey_ = e[0] - s[0], e[1] - s[1], bx - ax, by - ay
                den = dx * ey_ - dy * ex_
                if abs(den) > 1e-18:
                    t = ((ax - s[0]) * ey_ - (ay - s[1]) * ex_) / den
                    out.append((s[0] + t * dx, s[1] + t * dy))
            if ein:
                out.append(e)
            s = e
    return out


def overlap_visible(bvh, eyes, polys, n, tol=0.003, max_polys=6):
    """Skeptic S2: an overlap is visible when a walk eye on its front side sees a sample point INSIDE the overlap
    polygons (the centroid and 5 / 10 of the way to each vertex of the largest polygons) unoccluded; a pair hidden
    in a closed volume (the clock shaft under the stage) then reports without failing."""
    pts = []
    for _a, Q in sorted(polys, key=lambda t: -t[0])[:max_polys]:
        c = Q.mean(axis=0)
        pts += [c] + [c + 0.6 * (q - c) for q in Q]
    for p in pts:
        p_ = p + n * 0.001
        for eye in eyes:
            v = p_ - eye
            if float(v @ n) >= 0.0:
                continue
            dist = float(np.linalg.norm(v))
            hit = bvh.ray_cast(Vector(eye.tolist()), (v / dist).tolist(), dist + 0.01)
            if hit[0] is None or hit[3] >= dist - tol:
                return True
    return False


def check_coplanar(obj, role, walk=None):
    """C19 (revision 2, design 5 as amended by the skeptic's MC2 / S2): same-facing exact coplanar overlap > 25 cm2
    per part pair FAILS when it is visible from the C18 LOD0 walk eyes (overlap_visible). Pairs whose parts come
    from ONE kit builder call (kit_key; a part with itself) are exempt and reported (`kit`); every pair >= 1 cm2
    is listed (`all`). Returns the report; raises ContractError("C19 ...")."""
    if walk is None:
        tri, names = triangles(obj)
        bvh = _tri_bvh(tri)
        eyes = [v[0] for v in walk_views(role, 0)]
    else:
        tri, names, bvh, eyes = walk["tri"], walk["names"], walk["bvh"], walk["eyes"]
    pairs = coplanar_pairs(tri, names)
    rows, fail, kit = [], [], []
    for (a, b), d in sorted(pairs.items(), key=lambda kv: (-kv[1]["cm2"], kv[0])):
        is_kit = a == b or (kit_key(a) is not None and kit_key(a) == kit_key(b))
        vis = None
        if d["cm2"] > C19_FAIL_CM2 and not is_kit:
            vis = overlap_visible(bvh, eyes, d["polys"], d["n"])
        Q = np.concatenate([q for _a, q in d["polys"]])
        row = {"a": a, "b": b, "cm2": round(float(d["cm2"]), 1), "kit": is_kit, "visible": vis,
               "lo": [round(float(v), 2) for v in Q.min(axis=0)], "hi": [round(float(v), 2) for v in Q.max(axis=0)]}
        if d["cm2"] >= C19_LIST_CM2:
            rows.append(row)
        if is_kit and d["cm2"] > C19_FAIL_CM2:
            kit.append(row)
        if vis:
            fail.append(row)
    rep = {"pairs": len(pairs), "cm2": round(float(sum(d["cm2"] for d in pairs.values())), 1),
           "over_25_cm2": sum(1 for d in pairs.values() if d["cm2"] > C19_FAIL_CM2),
           "fail": fail, "kit": kit, "all": rows}
    if fail:
        _fail("C19", f"{len(fail)} visible same-facing coplanar part pairs > {C19_FAIL_CM2:.0f} cm2 (z-fight): "
                     + ", ".join(f"{r['a']} | {r['b']} {r['cm2']:.0f} cm2 at {r['lo']}..{r['hi']}" for r in fail[:10]))
    return rep


def check_hygiene(obj):
    """C12 (after the bake): one UV layer, no color attributes, no n-gons, no '.'."""
    me = obj.data
    if "." in obj.name:
        _fail("C12", f"name {obj.name!r}")
    if len(me.uv_layers) != 1:
        _fail("C12", f"{len(me.uv_layers)} UV layers")
    if me.color_attributes:
        _fail("C12", "colour attributes present")
    ngons = sum(1 for p in me.polygons if p.loop_total > 4)
    if ngons:
        _fail("C12", f"{ngons} n-gons")


def stub_table(role=None):
    """The four line stubs (track +-TRACK_X, haul +-HAUL_X) in the PREFAB frame of `role` (nitpick B2: the RAMPS base's
    x - 4.00; None = the design frame = the top)."""
    out = []
    dx = frame_dx(role) if role else 0.0
    for n, (kind, x, y) in enumerate((("track", -TRACK_X, TRACK_Y), ("track", TRACK_X, TRACK_Y),
                                      ("haul", -HAUL_X, HAUL_Y), ("haul", HAUL_X, HAUL_Y))):
        x = x + dx
        a = (x, y, STUB_Z)
        d = (x, y, STUB_Z + STUB_LEN)
        lane = LANE_TRACK if kind == "track" else LANE_HAUL
        out.append({"kind": kind, "a": a, "b": (x, y, STUB_Z + STUB_LEN / 3.0),
                    "c": (x, y, STUB_Z + 2.0 * STUB_LEN / 3.0), "d": d,
                    "lane": lane["prefab"], "lane_fallback": lane["fallback"],
                    "m_NodeIndex": [n, n], "m_ParentMesh": [0, 0]})
    return out


def hosted_table(role):
    if role != "base":
        return []
    out = []
    dx = frame_dx(role)                             # nitpick B2: prefab frame (the RAMPS base: x - 4.00)
    for tag, x in (("A", -TRACK_X), ("B", TRACK_X)):
        x = x + dx
        out.append({"what": f"SkiTramCabin {tag}", "m_Object": "CID:<SkiTramCabin prefab>",
                    "m_Position": [x, PIVOT_Y, DOCK_Z], "m_Rotation": [0.0, 0.0, 0.0, 1.0],
                    "m_ParentMesh": 0, "m_GroupIndex": 0, "m_Probability": 100,
                    "carriage_top": [x, CARRIAGE_TOP_Y, DOCK_Z], "rope": [x, TRACK_Y, DOCK_Z]})
    out.append({"what": "LV feed pole", "m_Object": LV_POLE_GUID, "m_Position": [16.0 + dx, 0.0, -12.0],
                "m_Rotation": [0.0, 0.0, 0.0, 1.0], "m_ParentMesh": -1, "m_GroupIndex": 0,
                "m_Probability": 100})
    return out


def prefab_contract(role, report, atlas_rule, base_report, switch):
    W, D = lot_cells(role)
    dx = frame_dx(role)                 # nitpick B2: every position below in the PREFAB frame (the RAMPS base: x - 4)
    terraform =({"mode": "flatten (BuildingTerraformOverride removed, DECISIONS 36)",
                  "recommended": {"m_SmoothMinOffset": [-6, -6], "m_SmoothMaxOffset": [6, 6],
                                  "why": "skeptic R1: the default flatten leaves a ~1 m smoothing "
                                         "band at the lot edge"}}
                 if role == "base" else
                 {"mode": "no-level (FG top override kept)", "m_LevelMinOffset": [1600, 1600],
                  "m_LevelMaxOffset": [0, 0], "DontRaise": True, "DontLower": True})
    if role == "base":
        # revision 2 (ruling Q6): the 4.0 m drive wheel and its two deflection sheaves (informational keys)
        wheels = {"bullwheel": {"centre": list(pf(role, BW_CENTRE)), "rope_r": BASE_BW_RR, "rim_r": BASE_BW_R,
                                "axle": "y"},
                  "deflection": [{"centre": [round(s * BASE_DS_X + dx, 6), HAUL_Y, BASE_DS_Z], "rope_r": BASE_DS_RR,
                                  "rim_r": BASE_DS_RIM, "axle": "y"} for s in (-1, 1)]}
    elif report.get("wheels"):
        # revision 2 (T1 / T2): the 3.6 m bullwheel, the lane ("deflection") wheels taking the rope on top, the
        # new foot sheaves
        wheels = {"bullwheel": {"centre": [0.0, BULL_Y, BULL_Z], "rope_r": BULL_RR, "rim_r": BULL_RIM, "axle": "z",
                                "slot": 2},
                  "deflection": [{"centre": [s * HAUL_X, round(DW_Y, 6), round(DW_Z, 6)], "rope_r": DW_RR, "axle": "x",
                                  "slot": 2} for s in (-1, 1)],
                  "foot_sheaves": [{"centre": [s * FS_X, FS_Y, BULL_Z], "rope_r": FS_RR, "axle": "z"}
                                   for s in (-1, 1)]}
    else:
        wheels = {"bullwheel": {"centre": [0.0, HAUL_Y, TOP_BW_Z], "rope_r": HAUL_X, "axle": "y",
                                "slot": 2}}
    if role == "top":
        wheels["anchor_drums"] = report.get("drums")
    out = {
        "x_frame": "design / FBX-raw x; game x = -x (x-sign gate settled N 2026-10-15): every x below (stubs, docks, "
                   "hosted, wheel centres, bounds, axis_x) is NOT game - negate it when writing prefab rows; the "
                   "sub_objects block is already written through rule T (game frame)",
        "prefab": PREFAB_NAMES[role], "lot": [W, D], "stub_tip_z": STUB_Z, "face_z": FACE_Z,
        "deck_y": DECK_Y, "pivot_y": PIVOT_Y, "track_y": TRACK_Y, "haul_y": HAUL_Y,
        "carriage_top_y": CARRIAGE_TOP_Y, "hang_m": HANG_M,
        "rope_below_carriage_top_m": ROPE_BELOW_TOP_M, "track_x": TRACK_X, "haul_x": HAUL_X,
        "lanes": {"track": LANE_TRACK, "haul": LANE_HAUL},
        "stubs": [{k_: (list(v) if isinstance(v, tuple) else v) for k_, v in s.items()}
                  for s in stub_table(role)],
        "stubs_measured": report.get("C2"),
        "docks": [{"carriage_top": [s * TRACK_X + dx, CARRIAGE_TOP_Y, DOCK_Z],
                   "rope": [s * TRACK_X + dx, TRACK_Y, DOCK_Z],
                   "pivot": [s * TRACK_X + dx, PIVOT_Y, DOCK_Z]} for s in (-1, 1)],
        "hosted": hosted_table(role),
        "terraform": terraform, "wheel_centres": wheels,
        "bounds": report.get("bounds"), "atlas_rule": atlas_rule,
        "engine_base": {k_: base_report[k_] for k_ in ("verdict", "ok_edges", "blocked_edges")},
        "lod_switch_m": switch,
    }
    if base_mode(role) == "plinth":
        # revision 3 (plinth mode, the L4 sheet): no engine base by design; what the surgery writes
        out["engine_base"]["mode"] = "plinth"
        out["base_mode"] = "plinth"
        out["plinth"] = {
            "depth_m": PLINTH["depth"], "min_y": (report.get("bounds") or {}).get("min", [None, None])[1],
            "band_m": PLINTH["band"], "per_lod": {k_: {f: v[f] for f in ("min_y", "band_vertices", "ok_edges")}
                                                   for k_, v in (report.get("C6p") or {}).items()},
            "surgery": {"LOD0 RenderPrefab BaseProperties": {"m_BaseType": None, "m_UseMinBounds": False},
                        "m_Bounds": "unclamped: min y -12.00 on LOD0 / LOD1 / LOD2 (MC11, as L4; culling then keeps "
                                    "the plinth: no HasBase, so PreCullingSystem uses the mesh bounds)",
                        "terraform": "unchanged: the FG-top no-level block (LevelMin 1600, DontRaise + DontLower)"},
            "why": "revision 3 (Richard T-3): the foundation and supports continue below grade so the station can be set "
                   "into a cliff edge or buried on a flat summit"}
    if RAMPS:
        # nitpick B2: the line axis in prefab x (the stubs / docks / hosted cabins sit +-TRACK_X / +-HAUL_X about it)
        # and the design -> prefab shift of this build (SkiCatalog.cs StationDef.StubTipLocalX reads axis_x)
        out["axis_x"] = dx
        out["frame"] = {"design_to_prefab_dx": dx, "lot_was": list(LOT[role]),
                        "why": "nitpick B2: the 6 x 3 lot is centred on the prefab origin; the new cell is on the "
                               "clock-tower (+X) side, so the building moved 4.00 m toward -X" if dx else
                               "unchanged (the top keeps its 3 x 3 lot)"}
    return out


# --------------------------------------------------------------------------------------
# Atlas rule, bake hook, painters
# --------------------------------------------------------------------------------------

def area_weighted_median(vals, weights):
    ok = np.isfinite(vals) & (weights > 0)
    v, w = vals[ok], weights[ok]
    o = np.argsort(v)
    c = np.cumsum(w[o])
    return float(v[o][np.searchsorted(c, c[-1] / 2.0)])


def measure_density(t, A, margin, share=None, w1=False):
    """Pack a COPY at A and return the area-weighted shipped median px/m (contract 7.4) and the
    surface; `share` = the bake's bay-sharing list (detail pass 1: the same list the bake packs with);
    w1=True also returns the w = 1 density (area-weighted median of px/m / density weight: every
    island gets the same global pack scale, so this is the px/m a weight-1 face ships at)."""
    cp = t.copy()
    cp.data = t.data.copy()
    bpy.context.collection.objects.link(cp)
    try:
        cp.data.uv_layers.active = cp.data.uv_layers["UVMap"]
        bpy.ops.object.select_all(action="DESELECT")
        cp.select_set(True)
        bpy.context.view_layer.objects.active = cp
        kw = {} if share is None else {"share": share}
        cs2_uv.pack_unique(cp, A, (), margin_px=margin, **kw)
        dens = cs2_uv.face_px_per_m(cp.data, cp.data.uv_layers[cs2_uv.UNIQUE_LAYER], A)
        area = cs2_uv.face_world_areas(cp.data)
        out = (area_weighted_median(dens, area), float(area.sum()))
        if w1:
            out += (area_weighted_median(dens / cs2_uv.face_density_weights(cp.data), area),)
        return out
    finally:
        me = cp.data
        bpy.data.objects.remove(cp, do_unlink=True)
        bpy.data.meshes.remove(me)


def label_weights(t, table, A, margin, share):
    """Detail pass 1 (design 5.4): every label's density weight w = ceil(1.05 target / w1, 0.25) from
    the pre-bake w = 1 density of a packed copy (same share), written onto its faces; L1 / L2 are then
    asserted on the shipped layout (label_density). Below LABEL_MIN_ATLAS (tests) the starting
    weight stays and nothing is asserted."""
    names = [n for n in sorted(LABEL_KINDS) if n in table]
    if not names:
        return {}
    if A < LABEL_MIN_ATLAS:
        return {"w1_px_per_m": None, "weights": {n: LABEL_W0 for n in names},
                "note": f"atlas {A} < {LABEL_MIN_ATLAS}: starting weights, L1 / L2 not asserted"}
    _m, _a, w1 = measure_density(t, A, margin, share, w1=True)
    me = t.data
    part = np.empty(len(me.polygons), np.int32)
    me.attributes["part"].data.foreach_get("value", part)
    attr = me.attributes[cs2_uv.DENSITY_ATTR]
    wv = np.empty(len(me.polygons), np.float32)
    attr.data.foreach_get("value", wv)
    out = {}
    for nm in names:
        w = math.ceil(1.05 * LABEL_KINDS[nm][2] / w1 / 0.25) * 0.25
        wv[part == table.index(nm) + 1] = w
        out[nm] = w
    attr.data.foreach_set("value", wv)
    me.update()
    _m2, _a2, w1b = measure_density(t, A, margin, share, w1=True)
    print(f"[tram] label weights at {A}: w1 {w1:.2f} -> {w1b:.2f} px/m; {out}")
    return {"w1_px_per_m": round(w1, 3), "w1_after_px_per_m": round(w1b, 3), "weights": out}


def label_density(t, table, A, layer=None):
    """{label: {"px_per_m", "u_px_per_m", "v_px_per_m"}} on the shipped layout: the area density and the
    per-axis densities of the label face's UV Jacobian against its upright (right, up) frame."""
    me = t.data
    uvl = me.uv_layers[layer or cs2_uv.UNIQUE_LAYER]
    out = {}
    for name, polys in _label_polys(me, table).items():
        if not name.startswith("label_"):
            continue
        ua, wa, us, vs = 0.0, 0.0, [], []
        for p in polys:
            r, up, pts = _label_frame(t, p)
            S = np.array([[q.dot(r), q.dot(up), 1.0] for q in pts], dtype=np.float64)
            U = np.array([tuple(uvl.data[l].uv) for l in p.loop_indices], dtype=np.float64)
            J = np.linalg.lstsq(S, U, rcond=None)[0]
            us.append(abs(J[0, 0]) * A)
            vs.append(abs(J[1, 1]) * A)
            m = len(U)
            ua += abs(sum(U[i, 0] * U[(i + 1) % m, 1] - U[(i + 1) % m, 0] * U[i, 1] for i in range(m))) / 2.0
            wa += p.area
        out[name] = {"px_per_m": round(math.sqrt(ua * A * A / wa), 1) if wa > 0 else 0.0,
                     "u_px_per_m": round(min(us), 1), "v_px_per_m": round(min(vs), 1)}
    return out


DENSITY_HIDDEN = ("cwshaft", "drivehouse", "rope", "bwshaft", "bwhub", "ladder", "lrung",
                  "plazaunder")       # nitpick B2: the paving under the ramp decks


def density_classes(t, table, A):
    """Shipped texel density by part kind (part_kind): area-weighted RMS px/m, surface, weight; the
    w = 1 density; the visible kinds under 14 px/m with >= 0.5 m2 (R23 floor, design 1.2 / 8)."""
    me = t.data
    dens = cs2_uv.face_px_per_m(me, me.uv_layers[cs2_uv.UNIQUE_LAYER], A)
    area = cs2_uv.face_world_areas(me)
    wts = cs2_uv.face_density_weights(me)
    part = np.empty(len(me.polygons), np.int32)
    me.attributes["part"].data.foreach_get("value", part)
    kinds = np.array([part_kind(table[p - 1]) if 0 < p <= len(table) else "?" for p in part])
    ok = np.isfinite(dens) & (area > 0)
    w1 = area_weighted_median(dens[ok] / wts[ok], area[ok])
    out = {}
    for k_ in sorted(set(kinds.tolist())):
        sel = ok & (kinds == k_)
        a = float(area[sel].sum())
        if a <= 0:
            continue
        out[k_] = {"px_per_m": round(math.sqrt(float((area[sel] * dens[sel] ** 2).sum()) / a), 1),
                   "m2": round(a, 2), "w": round(float(np.median(wts[sel])), 2)}
    starving = sorted(k_ for k_, v in out.items() if v["px_per_m"] < 14.0 and v["m2"] >= 0.5
                      and not k_.startswith(DENSITY_HIDDEN) and not k_.startswith(DENSITY_BELOW))
    res = {"w1_px_per_m": round(w1, 2), "median_px_per_m": round(area_weighted_median(dens[ok], area[ok]), 2),
           "classes": out, "starving_lt14": starving}
    below = {k_: v for k_, v in out.items() if k_.startswith(DENSITY_BELOW)}
    if below:                       # revision 3: the plinth-mode top's below-grade classes, reported (D10 floors them)
        res["below_grade"] = below
    return res


def emissive_lit_texels(asset_dir, name, thr=0.02):
    em = cs2_validate.load_png_pixels(os.path.join(asset_dir, f"{name}_Emissive.png"))
    return int((em[..., 0:3].max(axis=-1) > thr).sum())


def _lin(srgb):
    return float(((srgb + 0.055) / 1.055) ** 2.4) if srgb > 0.04045 else srgb / 12.92


def paint_tram_pictogram(rect, base, ctl):
    """Red disc (slot 3: light albedo, ControlMask B 1) with a white tram cabin hanging from two
    rope lines over three peaks, painted v-up (rows = v): the label faces' UVs are re-projected
    upright after the smart project (orient_label_uvs) and asserted upright on the shipped
    layout (C13) - smart_project had rotated the first build's disc 90 deg left (verdict 10)."""
    hgt, wid = base.shape[0], base.shape[1]
    v, u = np.meshgrid((np.arange(hgt) + 0.5) / hgt, (np.arange(wid) + 0.5) / wid, indexing="ij")
    du, dv = u - 0.5, v - 0.5
    disc = du * du + dv * dv <= 0.483 ** 2
    red = _lin(0.80)
    white = _lin(0.90)
    base[disc, 0:3] = red
    ctl[disc, 0:3] = (0.0, 0.0, 1.0)
    sym = np.abs(du)
    cabin = (sym <= 0.18) & (v >= 0.36) & (v <= 0.56)
    hanger = (sym <= 0.018) & (v >= 0.56) & (v <= 0.71)
    ropes = (sym <= 0.40) & ((np.abs(v - 0.715) <= 0.012) | (np.abs(v - 0.755) <= 0.012))
    peaks = np.zeros_like(disc)
    for pc, ph, pw in ((0.0, 0.31, 0.22), (0.23, 0.25, 0.17)):
        peaks |= (v >= 0.12) & (v <= 0.12 + ph * (1.0 - np.abs(sym - pc) / pw)) & \
                 (np.abs(sym - pc) <= pw)
    sym_sel = (cabin | hanger | ropes | peaks) & disc
    base[sym_sel, 0:3] = white
    ctl[sym_sel, 0:3] = 0.0


TRAIL_INK = {"green_circle": (0.12, 0.55, 0.22), "blue_square": (0.08, 0.30, 0.78),
             "black_diamond": (0.06, 0.06, 0.06)}


def trail_board_painter(kind, size_m):
    """Revision 2 (D4, ruling Q7: difficulty symbols + arrows only): a cs2_compose.stamp_on_face painter for a
    trail board, kind "trail:<symbol>:<arrow>". White field with a dark 12 mm border, the difficulty symbol
    (green circle / blue square / black diamond, ~0.17 m) at the left, a bold arrow (shaft 0.05 m, head 0.12 m)
    pointing left or right beside it; painted v-up in meters on the label (rows = v, u = the viewer's right:
    orient_label_uvs, C13); ControlMask 0 over the rect. Metrics on painter.last."""
    _t, sym, arrow = kind.split(":")
    W, H = size_m
    ink = np.array([_lin(c) for c in TRAIL_INK[sym]], np.float32)

    def painter(rect, base, ctl):
        hgt, wid = base.shape[0], base.shape[1]
        v, u = np.meshgrid((np.arange(hgt) + 0.5) / hgt, (np.arange(wid) + 0.5) / wid, indexing="ij")
        X, Y = u * W, v * H
        dark = _lin(0.08)
        base[..., 0:3] = _lin(0.92)
        ctl[..., 0:3] = 0.0
        b = 0.012
        base[(X < b) | (X > W - b) | (Y < b) | (Y > H - b), 0:3] = dark
        cx, cy, s = 0.14, H / 2.0, 0.085
        if sym == "green_circle":
            m = np.hypot(X - cx, Y - cy) <= s
        elif sym == "blue_square":
            m = (np.abs(X - cx) <= 0.9 * s) & (np.abs(Y - cy) <= 0.9 * s)
        else:
            m = (np.abs(X - cx) + np.abs(Y - cy)) <= 1.1 * s
        base[m, 0:3] = ink
        x0, x1, head = 0.30, W - 0.06, 0.12
        if arrow == "right":
            shaft = (X >= x0) & (X <= x1 - head) & (np.abs(Y - cy) <= 0.025)
            tip = (X >= x1 - head) & (X <= x1) & (np.abs(Y - cy) <= (x1 - X) * 0.70)
        else:
            shaft = (X >= x0 + head) & (X <= x1) & (np.abs(Y - cy) <= 0.025)
            tip = (X >= x0) & (X <= x0 + head) & (np.abs(Y - cy) <= (X - x0) * 0.70)
        base[shaft | tip, 0:3] = dark
        painter.last = {"symbol": sym, "arrow": arrow, "size_m": [W, H], "px": [int(wid), int(hgt)],
                        "symbol_px": round(2.0 * s * wid / W, 1)}
    painter.last = None
    return painter


CLOCK_PLATE_M = 3.0
CLOCK_RING_M = 1.10                 # slot-3 ring radius (0.08 wide)
CLOCK_GLYPH = (0.0, -0.42, 0.56)    # resort glyph center (x, y) and square size, m from the dial center


def paint_clock_dial(rect, base, ctl):
    """Detail pass 1 (design TB2): white field, slot-3 ring (red in game), 12 hour bars 0.14 x 0.28 m,
    a minute track of 48 dots (0.06 m) just outside them, the resort glyph (ski_launcher_glyph.cand_c, the cabins' and sign bands'
    peak-and-snowflake) in slate blue in the lower half, hands at 10:10 (contract 5.6), all in meters
    on the 3.0 m plate. Every mark is >= 0.13 m wide (8 texels at the dials' ~61 px/m, design L2)
    except the minute dots; the 1c dial's 0.06 m ticks and 0.04 m minute hand were 2.5-3.7 texels.
    The glyph moves from the dial center (design) to the lower half so the 10:10 hands never cross it."""
    hgt, wid = base.shape[0], base.shape[1]
    S = CLOCK_PLATE_M
    v, u = np.meshgrid((np.arange(hgt) + 0.5) / hgt, (np.arange(wid) + 0.5) / wid, indexing="ij")
    X, Y = (u - 0.5) * S, (v - 0.5) * S           # meters from the center, Y up (rows = v)
    r = np.hypot(X, Y)
    field, dark = _lin(0.92), _lin(0.08)
    base[..., 0:3] = field
    ctl[..., 0:3] = 0.0
    ring = np.abs(r - CLOCK_RING_M) <= 0.04
    base[ring, 0:3] = _lin(0.80)
    ctl[ring, 0:3] = (0.0, 0.0, 1.0)
    gx, gy, gs = CLOCK_GLYPH
    LD = _LD()
    cov = LD._sdf_cov(LD._glyph_prims(), S / 2.0 + gx - gs / 2.0, S / 2.0 + gy - gs / 2.0, gs, wid, hgt,
                      wid / S, hgt / S)
    slate = np.array([_lin(c) for c in (0.30, 0.36, 0.45)], np.float32)
    base[..., 0:3] = base[..., 0:3] * (1.0 - cov[..., None]) + slate * cov[..., None]
    for hh in range(12):
        a = 2.0 * math.pi * hh / 12.0
        along = X * math.sin(a) + Y * math.cos(a)
        perp = np.abs(X * math.cos(a) - Y * math.sin(a))
        base[(along >= 0.68) & (along <= 0.96) & (perp <= 0.07), 0:3] = dark
    for m in range(60):
        if m % 5:
            a = 2.0 * math.pi * m / 60.0
            base[np.hypot(X - 1.005 * math.sin(a), Y - 1.005 * math.cos(a)) <= 0.03, 0:3] = dark
    for a_deg, length, half_w in ((305.0, 0.55, 0.08), (60.0, 0.82, 0.065)):
        a = math.radians(a_deg)
        dx, dy = math.sin(a), math.cos(a)
        along = X * dx + Y * dy
        perp = np.abs(-X * dy + Y * dx)
        taper = half_w * np.clip(1.0 - 0.35 * np.clip(along, 0.0, None) / length, 0.0, 1.0)
        base[(along >= -0.12) & (along <= length) & (perp <= taper), 0:3] = dark
    base[r <= 0.10, 0:3] = dark


def mask_dial_emissive(asset_dir, name, stamps):
    """The clock plates' material is emissive as a whole (bake_unique writes a constant per
    material and post_compose has no emissive buffer, contract 14). Scale the `_Emissive`
    texels of each dial by the painted BaseColor so ticks / hands / ring stay dark and the
    face reads at night instead of a blank lit square. Detail pass 1: the amber LED board
    (`label_dep`, material led) the same way - lit dots glow, unlit dots and the field stay dark.
    `stamps` = {part: texel rect}. Returns the faces masked."""
    ep = os.path.join(asset_dir, f"{name}_Emissive.png")
    bp = os.path.join(asset_dir, f"{name}_BaseColor.png")
    rects = [(nm, r) for nm, r in sorted(stamps.items()) if nm.startswith(EMISSIVE_MASKED) and r]
    if not rects or not os.path.isfile(ep):
        return 0
    em = cs2_validate.load_png_pixels(ep)
    bc = cs2_validate.load_png_pixels(bp)
    lw = np.array((0.2126, 0.7152, 0.0722), np.float32)
    for _nm, (r0, r1, c0, c1) in rects:
        lum = bc[r0:r1, c0:c1, 0:3] @ lw
        em[r0:r1, c0:c1, 0:3] *= np.clip((lum - 0.30) / 0.55, 0.0, 1.0)[..., None]
    em[..., 3] = 1.0
    cs2_asset._write_png_raw(ep, f"{name}_Emissive", em, em.shape[0], False)
    return len(rects)


def make_post_compose(obj, role, table):
    def hook(base, nrm, msk, ctl, ao, mesh, A):
        hook.density = cs2_uv.density_report(obj, A)
        part_map, ppm_map = cs2_compose.rasterise_face_fields(mesh, A)
        h = cs2_compose.detail_height(mesh, A, detail_specs(role))
        hook.detail_stats = {k_: dict(v) for k_, v in getattr(cs2_compose, "LAST_DETAIL_STATS", {}).items()}
        hook.height_range = (float(h.min()), float(h.max()))
        uncovered = nrm[..., 0:3].max(axis=-1) < 1e-3
        base, nrm, msk, ctl = cs2_compose.compose_maps(
            base, nrm, msk, ctl, None, h, ppm_map, {"part_map": part_map, "seed": 0})
        for nm in table:
            if nm.startswith("ctface_"):
                cs2_compose.stamp_on_face(base, ctl, mesh, table, nm, paint_clock_dial)
                hook.stamps[nm] = cs2_compose.uv_bbox_px(mesh, table, nm, A)
        if role == "base":
            for nm in ("pictodisc", "pictoplinth"):
                cs2_compose.stamp_on_face(base, ctl, mesh, table, nm, paint_tram_pictogram)
                hook.stamps[nm] = cs2_compose.uv_bbox_px(mesh, table, nm, A)
        # detail pass 1: the dressing labels (cs2_liftdress painters; ControlMask 0 under them)
        for nm in sorted(LABEL_KINDS):
            if nm not in table:
                continue
            kind, size, _ppm = LABEL_KINDS[nm]
            painter = (trail_board_painter(kind, size) if kind.startswith("trail:")
                       else _LD().board_painter(kind, size_m=size, words=LABEL_WORDS.get(nm)))
            cs2_compose.stamp_on_face(base, ctl, mesh, table, nm, painter)
            rect = cs2_compose.uv_bbox_px(mesh, table, nm, A)
            hook.labels[nm] = dict(painter.last or {}, rect=list(rect) if rect else None)
        n = nrm[..., 0:3] * 2.0 - 1.0
        n[..., 2] = np.maximum(n[..., 2], 0.05)
        n /= np.maximum(np.linalg.norm(n, axis=-1, keepdims=True), 1e-6)
        nrm[..., 0:3] = n * 0.5 + 0.5
        nrm[uncovered, 0:3] = (0.5, 0.5, 1.0)
        nrm[..., 3] = 1.0
        return base, nrm, msk, ctl
    hook.density = None
    hook.height_range = None
    hook.stamps = {}
    hook.labels = {}
    hook.detail_stats = {}
    return hook


# Detail pass 1 weathering specs (design 5.6 / 6.5; cs2_liftdress.station_weather_spec adds the
# DECISIONS 171 defaults: preset station, slot_wear free, snow 0, edge band 0.08). The station's own
# overrides come FIRST: on an equal prefix cs2_weather keeps the first (chev_ scuff 1.5 over the
# kit's 1.0). The top's lot is no-level: the grade line is unknown, so the ground mode is "plinth".
TRAM_WEATHER = {
    "base": dict(ground="grade", ground_z=0.0,
                 traffic_extra=("grate_", "mplat", "edgestrip_", "chev_", "stripe_"),
                 overrides=(("horn_", {"edge": 1.5}),                 # guide horns scraped by cabins
                            ("edgestrip_", {"role": "paint", "scuff": 1.5}), ("chev_", {"scuff": 1.5}),
                            ("trackrail_", {"role": "galv", "rust": 1.3}), ("grate_", {"scuff": 1.2}),
                            ("plinthface", {"rust": 0.0}), ("cwshaft_", {"rust": 0.0}),
                            ("door_", {"rust": 0.0}),
                            ("fin_", {"edge": 0.0, "rust": 0.0}),    # thin flat members
                            ("mullion_", {"edge": 0.0, "rust": 0.0}))),
    "top": dict(ground="plinth", ground_z=0.0,
                traffic_extra=("grate_", "stairslab_", "stripe_"),
                overrides=(("drumrim_", {"role": "galv", "rust": 1.3}), ("drumspoke_", {"role": "galv"}),
                           ("footing_", {"grime": 1.3}), ("plinth", {"rust": 0.0}), ("door_", {"rust": 0.0}),
                           ("grate_", {"scuff": 1.2}))),
}


# nitpick B2: the ramps, their landings, the pads and the paving are walked (traffic) and scuffed like the gratings
NB2_TRAFFIC = {"base": ("ramp_", "landing_turn", "landing_rtop", "landing_balc", "plaza"), "top": ("ramp_", "pad_")}


def weather_spec(name, role, intensity):
    LD = _LD()
    w = TRAM_WEATHER[role]
    traffic = tuple(w["traffic_extra"]) + (NB2_TRAFFIC[role] if RAMPS else ())
    overrides = tuple(w["overrides"]) + ((("ramp_", {"scuff": 1.2}),) if RAMPS else ())
    if PLINTH_TOP and role == "top":
        # revision 3: the foundation concrete grimes like the footings, never rusts; the lattice is galvanised-steel
        # framing like the deck legs (the "plinth" ground mode already gives everything below grade a ground-dirt tone)
        overrides += (("found_", {"grime": 1.3, "rust": 0.0}), ("latpad_", {"grime": 1.3, "rust": 0.0}))
    return LD.station_weather_spec(name, ground=w["ground"], ground_z=w["ground_z"],
                                   overrides=overrides + tuple(LD.DRESS_WEATHER_OVERRIDES),
                                   traffic_extra=traffic, intensity=intensity)


class SteppedWeather:
    """Detail pass 1 weathering (DECISIONS 171 applied: station preset, slot_wear free, no snow; per
    asset the HIGHEST budget-clean intensity on visible texels). A cs2_bake post_compose hook: runs
    `inner` (make_post_compose) once, then cs2_weather's layer on fresh copies of inner's maps at each
    step of cs2_liftdress.INTENSITY_STEPS, upward, until the vanilla proxy budget WARNs; ships the last
    clean step's maps. Attributes the generator reads (stamps, labels, density, height_range) delegate
    to `inner`; `report` is the shipped step's weather report, `ladder` every step tried."""

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
            if not ok:
                break
            best = (s, out, rep)
        if best is None:
            if A >= LABEL_MIN_ATLAS:
                raise ContractError(f"W: weathering WARNs already at intensity {self.steps[0]} "
                                    f"({self.ladder[0]})")
            # below the shipping atlases (the tests bake 512) the 3-texel edge-band floor covers whole
            # members (~0.6 m at ~5 px/m): the budget is not representative there, so the first step
            # ships and the ladder records the WARN (the shipping exports keep the raise)
            hk = CW.make_weather_hook(lambda *_a: (b0.copy(), n0.copy(), m0.copy(), c0),
                                      self.spec_at(self.steps[0]), **self.hook_kw)
            best = (self.steps[0], hk(base, nrm, msk, ctl, ao, mesh, A), hk.report)
        self.chosen, out, self.report = best
        print(f"[tram] weathering: intensity {self.chosen} (highest budget-clean step of "
              f"{[r['intensity'] for r in self.ladder]})")
        return out


# --------------------------------------------------------------------------------------
# Hosted-pose preview (station + cabin stand-ins, Workbench)
# --------------------------------------------------------------------------------------

def _Q(X, Y, Z):
    """Prefab point -> oriented Blender point (after orient_for_net)."""
    return Vector((X, -Z, Y))


def standins(role, cabin_dir=None):
    """Cabin stand-ins at the hosted poses (base) / docked (top), 1.8 m people, rope stubs (design-frame numbers
    placed in the prefab frame: pf)."""
    objs = []

    def box(name, c, size, col):
        bpy.ops.mesh.primitive_cube_add(size=1.0, location=_Q(*pf(role, c)))
        o = bpy.context.active_object
        o.name = name
        o.scale = (size[0], size[2], size[1])
        o.color = col
        objs.append(o)
    for s in (-1, 1):
        x = s * TRACK_X
        if cabin_dir:
            continue
        box(f"SB_body{s}", (x, PIVOT_Y + 1.5, DOCK_Z), (3.7, 3.0, 8.0), (0.75, 0.10, 0.08, 1))
        box(f"SB_column{s}", (x, PIVOT_Y + 6.6, DOCK_Z), (0.5, 7.2, 0.75), (0.6, 0.6, 0.6, 1))
        box(f"SB_carriage{s}", (x, CARRIAGE_TOP_Y - 0.25, DOCK_Z), (0.9, 0.5, 5.6),
            (0.3, 0.3, 0.3, 1))
    for s in (-1, 1):                                    # the live HV lane leaving each stub
        x = s * TRACK_X
        dy = 2.8 if role == "base" else -2.8
        box(f"SB_rope{s}", (x, TRACK_Y + dy / 2.0, STUB_Z - 6.0), (0.08, 0.08, 12.0),
            (0.2, 0.2, 0.2, 1))
    for n, (x, z) in enumerate(((0.0, -6.0), (-9.8, -4.0), (9.8, -7.0))):
        box(f"SB_person{n}", (x, DECK_Y + 0.9, z), (0.45, 1.8, 0.3), (0.1, 0.3, 0.8, 1))
    if cabin_dir:
        import cs2_preview
        fbx = os.path.join(cabin_dir, os.path.basename(os.path.normpath(cabin_dir)) + ".fbx")
        for s in (-1, 1):
            o = cs2_preview.import_generation(fbx)
            o.location = _Q(*pf(role, (s * TRACK_X, PIVOT_Y, DOCK_Z)))
            o.color = (0.75, 0.10, 0.08, 1)
            objs.append(o)
    return objs


def render_hosted(t, name, out_dir, role, cabin_dir=None):
    """Workbench frames of the station with the cabin stand-ins at the hosted poses."""
    sc = bpy.context.scene
    saved = (sc.render.engine, sc.camera, sc.render.resolution_x, sc.render.resolution_y,
             sc.display.shading.color_type, sc.display.shading.light)
    hide = {o.name: o.hide_render for o in sc.objects}
    objs = standins(role, cabin_dir)
    t.color = (0.78, 0.78, 0.76, 1)
    bpy.ops.object.camera_add()
    cam = bpy.context.active_object
    cam.data.lens = 28.0
    cam.data.clip_end = 5000.0
    written = []
    try:
        sc.camera = cam
        sc.render.engine = "BLENDER_WORKBENCH"
        sc.display.shading.color_type = "OBJECT"
        sc.display.shading.light = "STUDIO"
        sc.render.resolution_x, sc.render.resolution_y = 1400, 1000
        for o in sc.objects:
            o.hide_render = not (o is t or o in objs)
        for view, d, dist in (("hosted_line", (0.35, 1.0, 0.30), 62.0),
                              ("hosted_section", (1.0, 0.05, 0.12), 55.0),
                              ("hosted_game", (0.6, -0.6, 1.2), 85.0)):
            dv = Vector(d).normalized()
            target = Vector((frame_dx(role), 3.0, 9.0))
            cam.location = target + dv * dist
            cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
            path = os.path.join(out_dir, f"{name}_{view}.png")
            sc.render.filepath = path
            bpy.ops.render.render(write_still=True)
            written.append(path)
            print(f"[tram] preview {view} -> {path}")
    finally:
        sc.render.engine, sc.camera = saved[0], saved[1]
        sc.render.resolution_x, sc.render.resolution_y = saved[2], saved[3]
        sc.display.shading.color_type, sc.display.shading.light = saved[4], saved[5]
        for o in objs + [cam]:
            data = o.data
            bpy.data.objects.remove(o, do_unlink=True)
            if data is not None and data.users == 0:
                (bpy.data.meshes if isinstance(data, bpy.types.Mesh) else bpy.data.cameras).remove(data)
        for o in sc.objects:
            if o.name in hide:
                o.hide_render = hide[o.name]
    return written


# Revision 1c: one Workbench close-up per station of the access C15 asked for (prefab target, prefab
# view direction, distance): the top's -X stair + gangway, the base's catwalk ladder.
ACCESS_SHOTS = {"top": ("access_stair_w", (-10.5, 3.5, 3.5), (-1.0, 0.55, 0.9), 26.0),
                "base": ("access_ladder", (1.5, 12.0, -7.8), (0.25, 0.10, -1.0), 24.0)}


def render_access(t, name, out_dir, role):
    """Workbench close-up of the revision-1c access (ACCESS_SHOTS), like the hosted frames."""
    sc = bpy.context.scene
    saved = (sc.render.engine, sc.camera, sc.render.resolution_x, sc.render.resolution_y,
             sc.display.shading.color_type, sc.display.shading.light)
    hide = {o.name: o.hide_render for o in sc.objects}
    view, tgt, d, dist = ACCESS_SHOTS[role]
    tgt = pf(role, tgt)                                         # nitpick B2: the design-frame target in the prefab frame
    t.color = (0.78, 0.78, 0.76, 1)
    bpy.ops.object.camera_add()
    cam = bpy.context.active_object
    cam.data.lens = 28.0
    cam.data.clip_end = 5000.0
    path = os.path.join(out_dir, f"{name}_{view}.png")
    try:
        sc.camera = cam
        sc.render.engine = "BLENDER_WORKBENCH"
        sc.display.shading.color_type = "OBJECT"
        sc.display.shading.light = "STUDIO"
        sc.render.resolution_x, sc.render.resolution_y = 1400, 1000
        for o in sc.objects:
            o.hide_render = o is not t
        target = Vector((tgt[0], -tgt[2], tgt[1]))              # prefab -> Blender (x, -z, y)
        cam.location = target + Vector((d[0], -d[2], d[1])).normalized() * dist
        cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
        sc.render.filepath = path
        bpy.ops.render.render(write_still=True)
        print(f"[tram] preview {view} -> {path}")
    finally:
        sc.render.engine, sc.camera = saved[0], saved[1]
        sc.render.resolution_x, sc.render.resolution_y = saved[2], saved[3]
        sc.display.shading.color_type, sc.display.shading.light = saved[4], saved[5]
        data = cam.data
        bpy.data.objects.remove(cam, do_unlink=True)
        if data is not None and data.users == 0:
            bpy.data.cameras.remove(data)
        for o in sc.objects:
            if o.name in hide:
                o.hide_render = hide[o.name]
    return path


# Detail pass 1 close-ups (prefab target, prefab eye, lighting passes): the departure board, the
# station name board and the gangway mouth (the task's three), plus the TICKETS / LIFT OPS boards,
# a clock dial, the crane tip and the beacon.
# Revision 2 adds (design 9 / skeptic MC6 / the orchestrator's brief): the yellow wheel from the berth deck,
# its BACK from the plinth deck at eye height (JH-12's framing, through the frame-only guard), the wheel from
# the side at 60 m, the top from 120 m at 45 deg (the walk view that showed the open bore), the trail boards,
# the 2.2 m gates, a medallion panel, the roof mast; base: the A-frame rack at 8 m, the 4.0 m drive wheel and
# its deflection sheaves, a medallion panel, the hall's ceiling plates. Every close-up shows the vanilla
# sub-object rows as semi-transparent stand-in boxes (cs2_subobj's look; lights yellow, seating blue, bins green).
# nitpick B (JH_DETAILS): 10 m shots of the added details (target, eye: |eye - target| = 10.0 m but the 30 m overview)
CLOSEUP_SHOTS_NB = (
    ("nb_canopy_low_10m", (-3.0, 6.8, 10.6), (-0.5, 2.0, 19.0), ("noon", "dusk")),
    ("nb_canopy_high_10m", (-3.0, 7.5, 10.6), (0.0, 13.0, 18.4), ("noon",)),
    ("nb_vstrut_10m", (-6.0, 6.8, 10.7), (-2.5, 4.5, 19.8), ("noon",)),
    ("nb_entrance_10m", (6.0, 2.6, 10.0), (7.5, 3.4, 19.85), ("noon",)),
    ("nb_chalkboard_10m", (-7.9, 0.6, 11.3), (-5.2, 3.8, 20.3), ("noon",)),
    ("nb_lifttickets_10m", (19.6, 2.6, 0.0), (28.6, 3.8, 4.2), ("noon",)),
    ("nb_clocktower_side_10m", (17.0, 5.0, 3.0), (25.0, 9.0, 7.5), ("noon",)),
    ("nb_front_30m", (2.0, 8.0, 8.0), (14.0, 16.0, 34.0), ("noon", "dusk")),
)
# nitpick B2 (RAMPS): 10 m and 30 m shots of the ramps (DESIGN frame, pf() places them; |eye - target| 10 / 30 m): the
# base switchback from the clock-tower side (a neighbor's lot) and from the plaza, the balcony at the side-area door,
# the turn landing; the top ramps from the dock and from below, with the molded-terrain sketch line (_terrain_sketch)
CLOSEUP_SHOTS_NB2 = {
    "base": (("nb2_ramp_ctside_10m", (25.5, 2.5, 2.0), (34.5, 6.0, 4.5), ("noon", "dusk")),
             ("nb2_ramp_ctside_30m", (23.5, 3.0, 0.0), (49.5, 16.0, 6.0), ("noon",)),
             ("nb2_ramp_plaza_10m", (23.0, 3.0, 8.0), (21.5, 4.5, 17.8), ("noon", "dusk")),
             ("nb2_ramp_plaza_30m", (21.0, 5.0, 2.0), (14.0, 15.0, 29.5), ("noon",)),
             ("nb2_door_10m", (19.7, 6.3, 5.9), (27.5, 11.0, 10.5), ("noon",)),
             ("nb2_turn_10m", (25.2, 2.8, -11.0), (29.5, 7.0, -19.5), ("noon",))),
    "top": (("nb2_top_ramp_dock_10m", (9.75, 5.6, 6.0), (10.6, 8.0, -3.5), ("noon",)),
            ("nb2_top_ramp_dock_30m", (9.0, 5.0, 5.0), (18.0, 16.0, -22.0), ("noon",)),
            ("nb2_top_ramp_below_10m", (10.0, 5.5, 5.5), (18.59, 0.71, 3.66), ("noon",)),
            ("nb2_top_ramp_below_30m", (8.0, 5.0, 6.0), (37.4, 3.16, 11.5), ("noon",)),
            ("nb2_top_ramps_back_30m", (4.0, 5.0, 6.0), (27.3, 17.8, 20.0), ("noon", "dusk"))),
}
def _eye(target, toward, dist=10.0):
    """The eye `dist` m from `target` along the direction `toward` (a vector, any length): a close-up's 10 m exactly."""
    n = math.sqrt(sum(c * c for c in toward))
    return tuple(round(t_ + dist * c / n, 3) for t_, c in zip(target, toward))


# nitpick B3 (ANNEX_STAIR): 10 m shots of the annex interior (DESIGN frame; the eye stands inside the annex, between its
# walls and under its roof, the side area's painted glazing behind it): the stair and the doorway from the annex's
# south-east, and the doorway / landing from the floor beside the foot
CLOSEUP_SHOTS_NB3 = (
    ("nb3_annex_stair_10m", (14.90, 5.90, -6.60), _eye((14.90, 5.90, -6.60), (4.4, 2.4, 8.7)), ("noon", "dusk")),
    ("nb3_annex_doorway_10m", (13.30, 6.70, -6.60), _eye((13.30, 6.70, -6.60), (5.9, 0.4, 8.0)), ("noon",)),
)
# revision 3 (PLINTH_TOP): the cliff-edge shots (render_closeups puts a sketched cliff under them: cliff_y) - from 10 m
# below grade looking up at the lattice legs and the foundation, the side elevation, down the face from the dock's edge, the
# arriving cabin's view of the dock - and the summit case (the flat ground at y 0 hides the plinth). Prefab frame = design
CLOSEUP_SHOTS_R3 = (
    ("r3_cliff_below_legs", (8.0, -3.0, -5.0), (16.0, -10.0, -26.0), ("noon",)),
    ("r3_cliff_below_front", (0.0, -1.5, -4.0), (-6.0, -10.0, -30.0), ("noon", "dusk")),
    ("r3_cliff_side_40m", (2.0, -3.0, -1.0), (42.0, 2.0, -14.0), ("noon",)),
    ("r3_cliff_dock_down", (10.1, -8.0, -7.0), (5.5, 9.0, -12.0), ("noon",)),
    ("r3_cliff_approach_30m", (2.0, 1.0, -4.0), (4.0, 8.0, -34.0), ("noon", "dusk")),
    ("r3_summit_30m", (0.0, 5.0, 0.0), (26.0, 14.0, -20.0), ("noon",)),
    ("r3_summit_dock_10m", (6.0, 5.0, -6.0), _eye((6.0, 5.0, -6.0), (-0.55, 0.35, -0.75)), ("noon",)),
)
CLOSEUP_SHOTS = {
    "base": (("dep",(HALL_X - 0.15, 10.10, -6.60), (7.40, 9.30, -5.90), ("noon", "dusk")),
             ("tickets", (-8.70, 2.30, HALL_Z), (-7.90, 2.10, 15.80), ("noon",)),
             ("ops", (0.0, 8.40, -3.20), (-1.20, 8.40, -8.20), ("noon",)),
             ("clock", (CT_LOC[0], 20.50, CT_LOC[2] + 2.02), (CT_LOC[0] - 1.20, 18.80, CT_LOC[2] + 8.50),
              ("noon", "dusk")),
             ("rack_8m", (TRAM_RACK_AT[0], 0.80, TRAM_RACK_AT[2]), (TRAM_RACK_AT[0] - 3.20, 3.00, TRAM_RACK_AT[2] + 7.20),
              ("noon",)),
             ("drive_wheel", (0.0, 15.40, 0.80), (-2.60, 8.20, -7.80), ("noon",)),
             ("medallions", (-9.80, 7.00, -8.35), (-9.00, 7.90, -4.80), ("noon",)),
             ("hall_lamps", (0.0, 14.30, -5.20), (-3.50, 8.20, 1.60), ("noon", "dusk")),
             # T71 (review 8 / 18.webp): the +X glass-wall corner at the open end, from outside (before / after the
             # timber corner fins were removed): three-quarter from the line side, and head-on along the line
             ("t71_corner_3q", (12.60, 13.00, -10.00), (24.00, 8.00, -22.00), ("noon",)),
             ("t71_corner_front", (12.60, 12.00, -10.00), (6.00, 8.00, -27.00), ("noon",)))
            # nitpick B (JH_DETAILS): the canopy on its V-struts from the plaza, a V on its pier, LIFT TICKETS on the
            # clock-tower side (from a neighbor's lot), ENTRANCE, the chalkboard - each at 10 m (CLOSEUP_SHOTS_NB)
            + (() if not JH_DETAILS else tuple(CLOSEUP_SHOTS_NB))
            + (() if not RAMPS else CLOSEUP_SHOTS_NB2["base"])
            + (() if not ANNEX_STAIR else CLOSEUP_SHOTS_NB3),
    "top": (("stn", (3.20, 8.50, -0.42), (2.40, 8.00, -5.40), ("noon", "dusk")),
            ("gangway_mouth", (-5.60, 7.00, -0.20), (-8.90, 7.95, -0.05), ("noon",)),
            ("gangway_plan", (-6.00, 6.40, -0.10), (-6.60, 12.50, -2.40), ("noon",)),
            ("crane_tip", (GOAL_X - 0.20, GOAL_Y1 - 0.40, 5.30), (GOAL_X - 3.20, GOAL_Y1 - 2.50, -0.20), ("noon",)),
            ("beacon", (0.0, MAST_TOP - 0.70, 5.30), (2.50, MAST_TOP - 2.20, 1.00), ("dusk",)),
            ("wheel_deck", (0.0, 12.40, 1.60), (-1.20, 8.00, -8.50), ("noon",)),
            ("wheel_back", (0.0, 12.10, 1.60), (0.80, 8.00, 10.50), ("noon", "dusk")),
            ("wheel_back_high", (0.0, 12.40, 1.60), (2.50, 17.50, 15.50), ("noon",)),
            ("wheel_side_60m", (0.0, 13.00, 1.00), (58.0, 18.00, 9.00), ("noon",)),
            ("top_120m_45deg", (0.0, 11.00, 0.0), (42.4, 95.9, 73.5), ("noon",)),
            ("trail_boards", (0.0, 7.55, 11.44), (0.30, 8.00, 7.40), ("noon",)),
            ("gates", (3.20, 7.40, -5.50), (0.40, 8.00, -1.20), ("noon",)),
            ("medallions", (-10.15, 7.00, -6.10), (-8.40, 7.80, -4.20), ("noon",)),
            ("roof_mast", (0.0, 19.60, 5.30), (3.20, 17.80, 1.20), ("noon",)))
           + (() if not RAMPS else CLOSEUP_SHOTS_NB2["top"])
           + (() if not PLINTH_TOP else CLOSEUP_SHOTS_R3),
}


def terrain_sketch_y(z):
    """Nitpick B2 (the top's previews only; NOT geometry): the molded terrain the top's ramps assume (DESIGN z):
    the summit plateau at the pads' top behind the station (z >= the pads' back face), a slope down under the ramps to
    the plinth's foot level at z 0, level to the face, then the cliff on the line side (photos 01 / 05)."""
    p1 = TOP_RAMP["pad"][1]
    y_pad = top_ramp_plan()["y_pad"]
    if z >= p1:
        return y_pad
    if z >= 0.0:
        return y_pad * z / p1
    if z >= STUB_Z:
        return 0.0
    return -25.0 * min(1.0, (STUB_Z - z) / 8.0)


def _terrain_sketch(rig, role):
    """Orange sketch lines of the molded terrain (terrain_sketch_y) along both lot sides (x +-12.4) and the back lot
    edge, for the top's ramp close-ups: render-only, tracked by the rig (removed after the shots)."""
    if role != "top" or not RAMPS:
        return []
    mat = rig.track_data(bpy.data.materials, bpy.data.materials.new("CS2T_terrain_line"))
    mat.use_nodes = True
    b = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    b.inputs["Base Color"].default_value = (1.0, 0.42, 0.05, 1.0)
    try:
        b.inputs["Emission Color"].default_value = (1.0, 0.42, 0.05, 1.0)
        b.inputs["Emission Strength"].default_value = 2.0
    except KeyError:
        pass
    out = []
    zs = [STUB_Z - 8.0 + 0.5 * i for i in range(int((24.0 + 8.0 + 8.0) / 0.5) + 1)]
    lines = [[(x, terrain_sketch_y(z) + 0.02, z) for z in zs] for x in (-12.4, 12.4)]
    lines.append([(x, terrain_sketch_y(12.0) + 0.02, 12.0) for x in (-16.0, 16.0)])
    for i, pts in enumerate(lines):
        o = cs2_kit.tube_along(f"CS2T_terrain_{i}", [_Q(*pf(role, q)) for q in pts], 0.06, 6, "both", 2)
        o.data.materials.clear()
        o.data.materials.append(mat)
        o.hide_render = False
        out.append(rig.track(o))
    return out
# revision 3 (PLINTH_TOP): the render-only sketched CLIFF EDGE across the lot (cliff_y; NOT geometry) and the summit case
# (cs2_preview's flat ground at y 0, which hides everything below grade). The cliff: the summit plateau at the pads' top
# behind the station (B2's assumption), a molded bank down to the station's grade in front of the pads, the grade up to
# the brink at z 0 (the plinth's line face is at -0.50), then a ~49 deg face falling toward the line side
CLIFF = {"bank": (9.00, 11.60), "slope": 1.15, "floor": -45.0, "half_x": 70.0, "z": (-60.0, 45.0), "step": 0.5}


def cliff_y(z):
    """Revision 3 (the top's cliff previews only; NOT geometry): the sketched terrain height at prefab z (see CLIFF)."""
    b0, b1 = CLIFF["bank"]
    y_pad = top_ramp_plan()["y_pad"]
    if z >= b1:
        return y_pad
    if z >= b0:
        return y_pad * (z - b0) / (b1 - b0)
    if z >= 0.0:
        return 0.0
    return max(CLIFF["floor"], CLIFF["slope"] * z)


def _cliff_terrain(rig, role):
    """Revision 3: the cliff_y heightfield as a render-only mesh (rock-brown, rough), tracked by the rig. None unless the
    top is in plinth mode. Built in the prefab frame through _Q (the oriented Blender frame)."""
    if base_mode(role) != "plinth":
        return None
    hx = CLIFF["half_x"]
    z0, z1 = CLIFF["z"]
    n = int(round((z1 - z0) / CLIFF["step"]))
    zs = [z0 + (z1 - z0) * i / n for i in range(n + 1)]
    bm = bmesh.new()
    rows = [[bm.verts.new(_Q(x, cliff_y(z), z)) for x in (-hx, hx)] for z in zs]
    for a, b in zip(rows, rows[1:]):
        f = bm.faces.new((a[0], a[1], b[1], b[0]))
    bm.normal_update()
    for f in bm.faces:
        if f.normal.z < 0:
            f.normal_flip()
    me = rig.track_data(bpy.data.meshes, bpy.data.meshes.new("CS2T_cliff"))
    bm.to_mesh(me)
    bm.free()
    mat = rig.track_data(bpy.data.materials, bpy.data.materials.new("CS2T_cliff"))
    mat.use_nodes = True
    b_ = next(n_ for n_ in mat.node_tree.nodes if n_.type == "BSDF_PRINCIPLED")
    b_.inputs["Base Color"].default_value = (0.42, 0.33, 0.24, 1.0)
    b_.inputs["Roughness"].default_value = 0.95
    me.materials.append(mat)
    o = rig.track(bpy.data.objects.new("CS2T_cliff", me))
    bpy.context.collection.objects.link(o)
    o.hide_render = True
    return o


STANDIN_RGB = (("Light", (1.00, 0.82, 0.18)), ("Bench", (0.25, 0.45, 0.95)), ("Trashbin", (0.25, 0.80, 0.35)))


def _closeup_standins(rig, role):
    """Semi-transparent boxes of the SUBOBJ_ROWS union bounds (cs2_liftdress.subobj_box; cs2_subobj's mesh and
    material), so the close-ups show where the vanilla lamps / benches / bins go (DECISIONS 183)."""
    import cs2_subobj as SO
    LD = _LD()
    mats = {}
    for i, (prefab, pos, facing, _par, _mount) in enumerate(subobj_rows(role)):     # prefab frame (nitpick B2)
        rgb = next((c for key, c in STANDIN_RGB if key in prefab), (0.85, 0.30, 0.85))
        if rgb not in mats:
            mats[rgb] = rig.track_data(bpy.data.materials, SO._box_material(f"CS2T_standin_{len(mats)}", rgb))
        lo, hi = LD.subobj_box(prefab, pos, LD.facing_quat(facing), frame="game")
        corners = [tuple(_Q(x, y, z)) for x in (lo[0], hi[0]) for y in (lo[1], hi[1]) for z in (lo[2], hi[2])]
        me = rig.track_data(bpy.data.meshes, SO._box_mesh(f"CS2T_standin_{i}", corners))
        me.materials.append(mats[rgb])
        o = rig.track(bpy.data.objects.new(f"CS2T_standin_{i}", me))
        bpy.context.collection.objects.link(o)
        o.hide_render = False


def _interior_fill(rig, at, energy=1800.0):
    """Nitpick B3 (render only): a soft point light at `at` for the annex-interior close-ups - the sun cannot reach the
    inside of a closed building (roof + dark stand-in glass), so the stair would read as a black cut-out."""
    ld = bpy.data.lights.new("CS2T_fill", "POINT")
    ld.energy, ld.shadow_soft_size = energy, 1.5
    ld.specular_factor = 0.0                    # no highlight of the light itself in the stand-in glass (a white disc)
    o = rig.track(bpy.data.objects.new("CS2T_fill", ld))
    bpy.context.collection.objects.link(o)
    o.location = at
    o.visible_camera = False                    # lights the scene, never seen by the camera (the eye is inside its sphere)
    return o


def render_closeups(t, asset_dir, name, out_dir, role, tint, win=None, only=None):
    """PBR close-ups (cs2_preview rig, the shipped textures) of CLOSEUP_SHOTS at noon / dusk; the
    `_Win` panes as render-only dark glass (as lift_terminal_detach.render_closeups). Revision 2: the vanilla
    sub-object rows render as semi-transparent stand-in boxes (_closeup_standins)."""
    import cs2_preview
    os.makedirs(out_dir, exist_ok=True)
    rig = cs2_preview._Rig(t, None)
    written = []
    try:
        cam = cs2_preview._make_camera(rig)
        cs2_preview._assign(t, cs2_preview._pbr_material(rig, asset_dir, name, tint))
        _closeup_standins(rig, role)
        sketch = _terrain_sketch(rig, role)                      # nitpick B2 (top): the molded-terrain sketch line
        cliff = _cliff_terrain(rig, role)                        # revision 3 (plinth top): the sketched cliff edge
        if win is not None:
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
        sc = rig.scene
        sc.render.resolution_x, sc.render.resolution_y = 1300, 1000
        for mode, world_spec in (("noon", cs2_preview.NOON), ("dusk", cs2_preview.DUSK)):
            shots = [s for s in CLOSEUP_SHOTS[role] if mode in s[3] and (not only or s[0].startswith(only))]
            if not shots:
                continue
            _world, sun = cs2_preview._make_world(rig, **world_spec)
            gnd = cs2_preview._make_ground(rig)
            for vname, tgt, eye, _modes in shots:
                # revision 3: the r3_cliff* shots stand on the sketched cliff (the flat ground hidden), the other r3_ shots
                # on the flat summit ground; the B2 terrain sketch lines show on the B2 shots only
                on_cliff = cliff is not None and vname.startswith("r3_cliff")
                gnd.hide_render = on_cliff
                if cliff is not None:
                    cliff.hide_render = not on_cliff
                for o_ in sketch or ():
                    o_.hide_render = vname.startswith("r3_")
                tg, ey = _Q(*pf(role, tgt)), _Q(*pf(role, eye))          # design-frame shots (nitpick B2)
                cs2_preview._aim(cam, tg, ey - tg, (ey - tg).length)
                path = os.path.join(out_dir, f"{name}_{vname}_{mode}.png")
                fill = _interior_fill(rig, ey) if vname.startswith("nb3_") else None    # nitpick B3: inside the annex
                written.append(cs2_preview._render(rig, path, engine="EEVEE"))
                if fill is not None:
                    ld = fill.data
                    rig.objects.remove(fill)
                    bpy.data.objects.remove(fill, do_unlink=True)
                    if ld.users == 0:
                        bpy.data.lights.remove(ld)
            for o in (gnd, sun):
                rig.objects.remove(o)
                bpy.data.objects.remove(o, do_unlink=True)
            sc.world = None
    finally:
        rig.restore()
    return written


# --------------------------------------------------------------------------------------
# CLI / main
# --------------------------------------------------------------------------------------

def check_stage(t, table, name, role, args, report):
    """Rename to the asset name, then C1-C5/C7/C8 + R30 clear, the `_Win` split (C11), the
    engine base (C6), the LOD0 cap (C9), part clashes (C14), platform reachability (C15), the
    sub-object boxes (C16) and the gate span vs the cabin door (C17).
    Raises ContractError on any violation."""
    t.name = t.data.name = name                    # export_fbx writes obj.name (texture pairing)
    check_contracts(t, role, args, report)
    win = split_windows(t, name, table, WIN_CELLS[role])
    if win is None:
        _fail("C11", "no `_Win` panes")
    report["C11"] = check_win_cells(win, role)
    report["C13"] = check_label_uvs(t, table, "UVMap")
    if base_mode(role) == "plinth":
        base_report = check_plinth(t, "LOD0")             # revision 3: C6p replaces C6 (no engine base)
        report["C6p"] = {"LOD0": base_report}
    else:
        base_report = check_engine_base(t, engine_base_parts(role, args)) if not args.skirt else \
            cs2_validate.ground_ring_report(t)
    n0 = cs2_asset.tri_count(t)
    if n0 > LOD0_CAP[role]:
        _fail("C9", f"LOD0 {n0} tris > cap {LOD0_CAP[role]}")
    if n0 > LOD0_TARGET[role] * 1.1:
        print(f"[tram] WARN LOD0 {n0} tris > target {LOD0_TARGET[role]} x 1.1")
    report["C14"] = check_part_clashes(t, role)          # every older negative fails first
    c14 = report["C14"]
    probe = fd_probe(t, role)                            # fine-detail step 3c: C15 counts the kit meshes
    try:
        report["C15"] = check_reachability(probe if probe is not None else t, role)    # revision 1c
    finally:
        if probe is not None:
            import cs2_kitrows
            cs2_kitrows.remove_probe(probe)
    c15 = report["C15"]
    report["C16"] = check_subobjects(t, role) if DETAIL1 else None   # detail pass 1
    report["F1"] = check_kit_rows(t, role, table)        # fine-detail step 3c ({}, None with FINE_DETAIL off)
    report["C17"] = check_gate_door(t, role)             # revision 1e: every older negative fails first
    c17 = report["C17"]
    # revision 2: see-through (C18 / C18b on LOD0; LOD1 / LOD2 after the chain in main), shimmer (C19, reusing
    # the walk's eyes), the wheels to scale (C20, also asserted at build start)
    report["C18"], walk = check_see_through(t, role, 0)
    report["C18"] = {"LOD0": report["C18"]}
    report["C19"] = check_coplanar(t, role, walk)
    report["C20"] = check_wheels(role, bool(getattr(args, "wheels", True)))
    c18, c19 = report["C18"]["LOD0"], report["C19"]
    print(f"[tram] C18 LOD0: {c18['back']} back-face first hits of {c18['hits']} hits / {c18['rays']} rays "
          f"({c18['views']} views), worst part "
          + (f"{c18['parts'][0]['part']} {c18['parts'][0]['back']}" if c18["parts"] else "none")
          + f" (<= {c18['max_per_part']}); C18b rims closed {sorted(c18['rims_open'] or {})}")
    print(f"[tram] C19: {c19['pairs']} same-facing coplanar part pairs ({c19['cm2']:.0f} cm2), "
          f"{c19['over_25_cm2']} over {C19_FAIL_CM2:.0f} cm2, 0 visible outside one kit; kit-exempt "
          f"{[(r['a'], r['b'], r['cm2']) for r in c19['kit']][:6]}")
    print(f"[tram] {report['C20'].get('code', 'C20')}: {report['C20']}")
    print(f"[tram] contracts C1-C8, C11, C13-C20 hold; LOD0 {n0} tris (target {LOD0_TARGET[role]}, "
          f"cap {LOD0_CAP[role]}); _Win cells {report['C11']}; C14 {c14['box_pairs']} box pairs, "
          f"{c14['declared_contacts']} declared contacts, {len(c14['known_open'])} known open"
          + (f", hut clear {c14['hut']['nearest_m']}" if c14.get("hut") else ""))
    for k_ in c14["known_open"]:
        print(f"[tram] C14 known open: {k_}")
    print(f"[tram] C15 reachability: {len(c15['surfaces'])} walkable surfaces in {len(c15['areas'])} "
          f"areas, {len(c15['flights'])} flights, {len(c15['ladders'])} ladders, {c15['posts']} railing "
          f"posts seated; every surface reaches grade")
    for tag, f in sorted(c15["flights"].items()):
        print(f"[tram] C15 flight {tag}: {f['treads']} treads, rise {f['rise_m']} m, {f['from']} -> {f['to']}")
    for tag, L in sorted(c15["ladders"].items()):
        print(f"[tram] C15 ladder {tag}: {L['rungs']} rungs, rise {L['rise_m']} m, {L['from']} -> {L['to']}")
    for tag, r in sorted((c15.get("ramps") or {}).items()):         # nitpick B2
        print(f"[tram] C15 ramp {tag}: 1:{r['grade_1_in']} ({r['grade'] * 100:.2f} %), run {r['run_m']} m, rise "
              f"{r['rise_m']} m, deck {r['deck_width_m']} m, clear {r['clear_m']} m ({r['stations']} stations), "
              f"{r['from']} -> {r['to']}; landings {r['ends']['foot'].get('landing_m')} / "
              f"{r['ends']['head'].get('landing_m')} m, crossings {r['ends']['foot']['crossing']} / "
              f"{r['ends']['head']['crossing']} m")
    for s, b in sorted((c15.get("C15b") or {}).items()):
        print(f"[tram] C15b {s}: {'GRADE' if b['counts_as_grade'] else 'not grade'} (top {b['top_y']}, edge "
              f"{b['edge_m']} m, ground {b['ground_y']}, terraform {b['terraform']})")
    if c15.get("goal"):
        print(f"[tram] C15 goal: {c15['goal']}")
    if c15.get("route"):                                                # nitpick B3
        rt = c15["route"]
        print(f"[tram] C15 route (the side area is not a dead end): {' -> '.join(rt['chain'] or ['NONE'])}; stair(s) "
              f"{rt['stairs']} {rt['stair_width_m']} m wide, {rt['stair_between_rails_m']} m between the rails; the "
              f"doorway passage {rt['passage']}; carried by no other stair (not used: {rt['carried_by_no_other_stair']})")
    for k_, j in sorted(c15["joins"].items()):
        if j.get("opening") is not None:
            print(f"[tram] C15 join {k_}: crossing {j['opening']:.3f} m {'ok' if j['ok'] else 'NARROW'} "
                  f"(between {j['between']})")
    print(f"[tram] C15 not walkable by design: {sorted(c15['not_walkable'])}")
    ops = sorted({e_["opening_m"] for e_ in c17["edges"].values()})
    print(f"[tram] C17 gate span vs cabin door: door z {c17['span_z'][0]}..{c17['span_z'][1]} (centre "
          f"{c17['door']['centre']}, leaves -+{c17['door']['half']}), {len(c17['edges'])} berth edges, "
          f"opening {ops} m (>= {c17['open_min_m']}), centred on the seam, no post / leaf in the span; "
          f"posts {sorted({e_['posts'] for e_ in c17['edges'].values()})} leaves "
          f"{sorted({e_['leaves'] for e_ in c17['edges'].values()})} per edge")
    if report["C16"] is not None:
        print(f"[tram] C16 sub-objects: {len(report['C16']['rows'])} placed, every union box clear, supported "
              f"and outside the carrier zones"
              + "".join(f"; {r['prefab']} {r['pos']} reaches {r['above_mesh_top_m']} m above the mesh top"
                        for r in report["C16"]["rows"] if r.get("above_mesh_top_m")))
    if report["F1"][0]:
        print(f"[tram] F1 {report['F1'][0]['F1']}")
    return win, base_report, n0


def apply_fine(args):
    """FINE_DETAIL follows the run's own args (--no-fine), every run (main / build_checked)."""
    global FINE_DETAIL
    FINE_DETAIL = not bool(getattr(args, "no_fine", False))
    return FINE_DETAIL


def apply_tiled(args):
    """T76: TILED follows the run's own args (--tiled), every run (main / build_checked)."""
    global TILED
    TILED = bool(getattr(args, "tiled", False))
    return TILED


def build_checked(argv, inject=None):
    """Tests: parse `argv`, clear, assemble (with an optional violation injected) and run the
    contract stage. Returns (t, win, table, report)."""
    args = parse_args(argv)
    apply_fine(args)
    apply_tiled(args)
    clear_scene()
    t, table, _src, _meta = assemble(args.role, args, bool(args.finish), inject=inject)
    report = {"role": args.role, "wheels": bool(args.wheels) if args.role == "top" else None}
    win, _base, _n0 = check_stage(t, table, args.name or NAMES[args.role], args.role, args, report)
    return t, win, table, report


def parse_args(argv=None):
    if argv is None:
        argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser(prog="tram_station.py")
    ap.add_argument("--role", choices=("base", "top"), required=True)
    ap.add_argument("--out", default=DEFAULT_OUT)
    ap.add_argument("--name", default=None)
    ap.add_argument("--finish", action="store_true")
    ap.add_argument("--lods", action="store_true")
    ap.add_argument("--export", action="store_true",
                    help="write <out>/<Name>/ (without it: a temp folder)")
    ap.add_argument("--preview", nargs="?", const=DEFAULT_PREVIEW, default=None, metavar="DIR")
    ap.add_argument("--review", nargs="?", const="", default=None, metavar="DIR")
    ap.add_argument("--atlas", default=None, help="auto | 2048 | 4096 | <pow2 >= 256>")
    ap.add_argument("--min-ppm", type=float, default=25.0)
    ap.add_argument("--ao-samples", type=int, default=48)
    ap.add_argument("--clock-tower", dest="clock_tower", action="store_true", default=True)
    ap.add_argument("--no-clock-tower", dest="clock_tower", action="store_false")
    ap.add_argument("--wheels", dest="wheels", action="store_true", default=True)
    ap.add_argument("--no-wheels", dest="wheels", action="store_false")
    ap.add_argument("--skirt", type=float, default=0.0)
    ap.add_argument("--skirt-ack-probe", action="store_true")
    ap.add_argument("--area-only", action="store_true")
    ap.add_argument("--cabin", default=None)
    ap.add_argument("--carriers", default=DEFAULT_CARRIERS)
    ap.add_argument("--no-validate", action="store_true")
    ap.add_argument("--weather", choices=("on", "off"), default="on",
                    help="detail pass 1: the weathering layer on finish builds (off = the same "
                         "geometry, sharing and labels without it; the D4 guard compares the two)")
    ap.add_argument("--closeups", nargs="?", const=DEFAULT_CLOSEUPS, default=None, metavar="DIR",
                    help="detail pass 1: render the label / access close-ups (CLOSEUP_SHOTS)")
    ap.add_argument("--closeups-only", default=None, metavar="PREFIX",
                    help="nitpick B3: with --closeups, render only the shots whose name starts with PREFIX (nb3_)")
    ap.add_argument("--no-fine", dest="no_fine", action="store_true",
                    help="fine-detail step 3c off: the approved revision-2 station byte for byte (FINE_DETAIL)")
    # T76 (docs/research/art/tram-tile-route-plan-2026-10-18.md; tram_tilesheet.py): off = byte-identical
    ap.add_argument("--tiled", action="store_true",
                    help="T76: LOD0 on the station's own swatch sheet (the base folder owns it, the top shares it), "
                         "glass to _Gls, track ropes 6 sides; LOD1 / LOD2 = today's bake as a far set (needs --finish)")
    ap.add_argument("--sheet-tex", type=int, default=4096, choices=(2048, 4096), help="tiled: station sheet size")
    ap.add_argument("--far-tex", type=int, default=None, choices=(512, 1024, 2048, 4096),
                    help="tiled: the LOD1 / LOD2 far set atlas (default base 2048, top 1024)")
    ap.add_argument("--sheet-dir", default=None,
                    help="tiled: the sheet workspace (default <out>/_SkiTramSheet<letter>, beside the asset folders)")
    ap.add_argument("--tiled-step", type=int, default=0, choices=(0, 1, 2),
                    help="tiled: the fallback ladder (plan 3.3) steps 1..N applied (0 = the plan's set)")
    a = ap.parse_args(argv)
    if a.tiled and not a.finish:
        ap.error("--tiled needs --finish (the sheet LOD0 is a finished build)")
    if a.atlas is None:
        a.atlas = "auto" if a.role == "base" else "2048"
    if a.skirt and a.role != "top":
        ap.error("--skirt is a top-station option")
    if a.skirt and not a.skirt_ack_probe:
        ap.error("--skirt needs --skirt-ack-probe: the BaseProperties {m_BaseType null} probe "
                 "has not passed (ROUND-SPEC section 9)")
    if a.skirt and PLINTH_TOP:
        ap.error("--skirt is moot in plinth mode (revision 3, PLINTH_TOP): the top's own foundation continues below "
                 "grade; its rock skirt would put vertices on the y 0 line")
    return a


def read_carriers(path):
    try:
        with open(path, encoding="utf-8") as fh:
            row = json.load(fh)["carriers"]["tram"]
    except (OSError, KeyError, ValueError) as e:
        print(f"[tram] WARN carriers.json not read ({e}); module constants used")
        return None
    drift = []
    if abs(float(row.get("hang_m", HANG_M)) - HANG_M) > 1e-6:
        drift.append(f"hang_m {row.get('hang_m')} != R27 {HANG_M}")
    if abs(float(row.get("lane_x_m", TRACK_X)) - TRACK_X) > 1e-6:
        drift.append(f"lane_x_m {row.get('lane_x_m')} != {TRACK_X}")
    for d in drift:
        print(f"[tram] WARN catalog/carriers.json tram row is stale: {d} (humans own the file; "
              f"the generator builds the rulings)")
    return {"row": row, "drift": drift}


def main(argv=None):
    if not bpy.app.background:
        raise RuntimeError("tram_station.py runs headless only (ROUND-SPEC R10): "
                           "blender --background --factory-startup --python tram_station.py -- ...")
    args = parse_args(argv)
    apply_fine(args)                        # fine-detail step 3c: every run follows its own args (--no-fine)
    apply_tiled(args)                       # T76: every run follows its own args (--tiled)
    clear_scene()
    role = args.role
    name = cs2_asset.validate_asset_name(args.name or NAMES[role])
    carriers = read_carriers(args.carriers)
    report = {"role": role, "wheels": bool(args.wheels) if role == "top" else None}
    finish = bool(args.finish)

    # -- area-only (run first, pipeline-gap-skeptic miss 6) ---------------------------------
    t, table, sources, meta = assemble(role, args, finish)
    report["drums"] = (meta or {}).get("drums")
    if args.area_only:
        area = float(cs2_uv.face_world_areas(t.data).sum())
        for A in (2048, 4096):
            ceil = A / math.sqrt(area)
            med, _a = measure_density(t, A, PACK_MARGIN.get(A, 6))
            print(f"[tram] area-only {name}: surface {area:.0f} m2, {cs2_asset.tri_count(t)} tris; "
                  f"@{A}: ceiling {ceil:.1f} px/m, packed area-weighted median {med:.1f} px/m")
        return {"area_m2": area}

    win, base_report, n0 = check_stage(t, table, name, role, args, report)
    gls = lod0_src = TT = None
    if args.tiled:
        # T76: the hall glass leaves the main mesh (_Gls, DECISIONS 281); the un-baked copy becomes the sheet LOD0
        if args.preview or args.review is not None or args.closeups:
            raise RuntimeError("--tiled R1 builds no previews / review pack / close-ups (T76 round 2)")
        import tram_tilesheet as TT
        gls, lod0_src = TT.split_glass_and_copy(t, name, table)
        report["T9_panes_m"] = TT.pane_thickness(gls, table)
    kind = "building"
    asset_dir = os.path.join(os.path.abspath(args.out), name) if args.export else \
        os.path.join(tempfile.mkdtemp(prefix="tramstation_"), name)
    os.makedirs(asset_dir, exist_ok=True)
    for f in os.listdir(asset_dir):          # stale files would fail the stray-file rule
        p = os.path.join(asset_dir, f)
        if os.path.isfile(p) and f.startswith(name):
            os.remove(p)

    # -- atlas rule (contract 7.4) -----------------------------------------------------------
    import cs2_bake
    area = float(cs2_uv.face_world_areas(t.data).sum())
    atlas_rule = {"requested": args.atlas, "surface_m2": round(area, 1)}
    # detail pass 1: bay sharing (MC6-safe: prefixes with an active DetailSpec, the hero list, labels,
    # lamps and pinned faces never share), the same list for every pre-bake pack and the bake
    LD = _LD() if (finish and DETAIL1) else None
    share = None
    if LD is not None:
        share = LD.share_identical(t, table, LD.TRAM_SHARE_WHITELIST + (R3_SHARE if base_mode(role) == "plinth" else ()),
                                   detail_specs=detail_specs(role),
                                   hero=LD.TRAM_SHARE_HERO, uv_layer="UVMap",
                                   ground_band=(TRAM_WEATHER[role]["ground_z"], 0.60))
    if args.tiled:
        A = int(args.far_tex or TT.FAR_TEX[role])          # T76: the unique bake is the LOD1 / LOD2 far set
        atlas_rule.update(chosen=A, far_set=True)
        report["far_A"] = A
    elif not finish:
        A = 512 if args.atlas in ("auto", "2048", "4096") else int(args.atlas)
        atlas_rule["chosen"] = A
    elif args.atlas == "auto":
        med, _ = measure_density(t, 2048, PACK_MARGIN[2048], share)
        atlas_rule["median_2048"] = round(med, 2)
        A = 4096 if med < args.min_ppm else 2048
        atlas_rule["chosen"] = A
        print(f"[tram] atlas auto: area-weighted median {med:.1f} px/m @2048 "
              f"({'<' if med < args.min_ppm else '>='} {args.min_ppm}) -> {A} (pin --atlas {A} "
              f"in the run sheet, skeptic F9)")
    else:
        A = int(args.atlas)
        atlas_rule["chosen"] = A
    margin = PACK_MARGIN.get(A, max(2, int(round(6 * A / 2048))))
    lw = label_weights(t, table, A, margin, share) if LD is not None else {}
    compose = make_post_compose(t, role, table)
    if LD is not None and args.weather == "on":
        # DECISIONS 171: station preset, slot_wear free, no snow, the highest budget-clean intensity on
        # visible texels; roles by material INDEX (CS2SRC_<key> slots, read before the bake, MC7a);
        # every painted label / dial protected 1:1 by its exact name (stamped)
        roles = LD.lift_mat_roles(t.data, LD.TRAM_MAT_ROLES)
        stamped = sorted(n for n in table if n.startswith(LABEL_PREFIXES))
        compose = SteppedWeather(compose, lambda s: weather_spec(name, role, s),
                                 (TILED_FAR_WEATHER_STEPS + LD.INTENSITY_STEPS) if args.tiled else LD.INTENSITY_STEPS,
                                 mat_roles=roles, detail_specs=detail_specs(role), stamped=stamped,
                                 compose_contact=False)
    t.data.uv_layers.active = t.data.uv_layers["UVMap"]
    bake = cs2_bake.bake_unique(t, asset_dir, name, A, sources, margin_px=margin,
                                ao_samples=args.ao_samples if finish else 0,
                                post_compose=compose, emissive=True, src_uv="UVMap",
                                **({} if share is None else {"share": share}))
    masked = dict(compose.stamps)
    masked.update({nm: tuple(v["rect"]) for nm, v in compose.labels.items() if v.get("rect")})
    n_dials = mask_dial_emissive(asset_dir, name, masked)
    if n_dials:
        print(f"[tram] _Emissive: {n_dials} faces (clock dials, LED board) masked by their painted marks")
    detail1 = None
    if LD is not None:
        lab = label_density(t, table, A)
        for nm, d in lab.items():
            d.update(target_px_per_m=LABEL_KINDS[nm][2], weight=(lw.get("weights") or {}).get(nm),
                     **{k_: v for k_, v in compose.labels.get(nm, {}).items()
                        if k_ in ("cap_m", "stroke_px", "glyph_m", "t1_ok", "t2_ok", "rect", "dots_lit")
                        or (k_ == "lines" and nm in LABEL_WORDS)})
        if A >= LABEL_MIN_ATLAS:
            low = [f"{nm} {d['px_per_m']} (u {d['u_px_per_m']}, v {d['v_px_per_m']}) < {d['target_px_per_m']}"
                   for nm, d in sorted(lab.items())
                   if min(d["px_per_m"], d["u_px_per_m"], d["v_px_per_m"]) < 0.95 * d["target_px_per_m"]
                   or d["px_per_m"] < d["target_px_per_m"] - 1e-6]
            if low and args.tiled:
                report["far_set_L1_low"] = low          # T76: the far set's labels are seen past the LOD1 switch only
            elif low:
                _fail("L1", "labels under their px/m target on the shipped layout: " + "; ".join(low))
        weather = None
        if isinstance(compose, SteppedWeather):
            import cs2_weather as CW
            rep_w = json.loads(CW.report_json(compose.report))
            weather = {"intensity": compose.chosen, "ladder": compose.ladder,
                       "slot_wear": LD.STATION_SLOT_WEAR, "snow": 0.0, "preset": "station",
                       "vanilla": {k_: rep_w["vanilla"].get(k_) for k_ in
                                   ("VL1_mean_rel_dL", "VL2_share_rel_dL_gt_0.15", "VL3_mean_dsmooth",
                                    "VL4_max", "visible_texels", "ok", "failed")},
                       "perceptual": rep_w.get("perceptual"), "seconds": rep_w.get("seconds")}
        detail1 = {"labels": lab, "label_weights": lw, "weather": weather,
                   "share": {k_: v for k_, v in (share.report or {}).items() if k_ != "group_names"},
                   "share_groups": [[c, len(d)] for c, d in (share.report or {}).get("group_names", [])],
                   "shared_faces": bake.get("shared_faces"),
                   "density": density_classes(t, table, A),
                   "emissive_lit_texels": emissive_lit_texels(asset_dir, name),
                   "detail_stats": getattr(compose, "detail_stats", None),
                   "sub_objects": report.get("C16")}
        print(f"[tram] detail1: w1 {detail1['density']['w1_px_per_m']} px/m, median "
              f"{detail1['density']['median_px_per_m']}, starving {detail1['density']['starving_lt14']}; "
              f"emissive lit texels {detail1['emissive_lit_texels']}; labels "
              + ", ".join(f"{nm} {d['px_per_m']}" for nm, d in sorted(lab.items()))
              + (f"; weathering {weather['intensity']} ({[(r['intensity'], r['ok']) for r in weather['ladder']]})"
                 if weather else "; weathering off"))
    far, A_lod0, n_src = t, A, n0
    if args.tiled:
        t, far = TT.tiled_lod0(sys.modules[__name__], far, lod0_src, role, name, args, asset_dir, bake, table, report)
        A_lod0, n_src, n0 = report["tiled"]["sheet"]["A"], cs2_asset.tri_count(far), cs2_asset.tri_count(t)
    dens = cs2_uv.face_px_per_m(t.data, t.data.uv_layers[cs2_uv.UNIQUE_LAYER], A_lod0)
    shipped_med = area_weighted_median(dens, cs2_uv.face_world_areas(t.data))
    atlas_rule["shipped_median"] = round(shipped_med, 2)
    if finish and shipped_med < args.min_ppm:
        print(f"[tram] WARN shipped area-weighted median {shipped_med:.1f} px/m < {args.min_ppm} "
              f"at {A} (report; R18 vs RS section 4 is Richard's call)")
    check_hygiene(t)
    report["C13_unique"] = check_label_uvs(t, table, cs2_uv.UNIQUE_LAYER)   # the shipped layout
    vm = cs2_validate.validate_mesh(t, kind=kind)
    errs = [f for f in vm if f[0] == "ERROR"]
    if any(f[1] == "uv-zero" for f in errs):
        # validate_mesh's shoelace runs in float32: sub-pixel faces at small atlases read as
        # zero. Re-check in float64 and fail only on genuinely unmapped faces.
        me = t.data
        a = cs2_uv.face_uv_areas(me, cs2_uv.loop_uvs(me.uv_layers[cs2_uv.UNIQUE_LAYER]))
        w = cs2_uv.face_world_areas(me)
        unmapped = int(((a < 1e-14) & (w > 1e-10)).sum())
        print(f"[tram] uv-zero re-check (float64): {unmapped} unmapped faces "
              f"(validate_mesh float32 flagged {errs[0][2]})")
        if unmapped:
            _fail("C12", f"{unmapped} faces with zero UV area")
        errs = [f for f in errs if f[1] != "uv-zero"]
        report["uv_zero_float32_artifact"] = [f for f in vm if f[1] == "uv-zero"][0][2]
    if errs:
        _fail("C12", f"validate_mesh errors {errs}")

    cs2_asset.export_fbx(t, os.path.join(asset_dir, f"{name}.fbx"))
    cs2_submesh.export_submeshes(win, name, "Win", asset_dir, lods=(1, 2) if args.lods else ())
    if gls is not None:
        cs2_submesh.export_submeshes(gls, name, "Gls", asset_dir, lods=(1, 2) if args.lods else ())

    # -- LOD chain (C9, C10) -------------------------------------------------------------------
    lods = []
    tris = {0: n0}
    if args.lods:
        l1 = min(LOD1_TARGET[role], int(0.45 * n_src), survivor_tris(far, table, LOD1_DELETE))
        l2 = min(LOD2_TARGET[role], survivor_tris(far, table, LOD2_DELETE[role]))
        lods = cs2_asset.make_lod_chain(far, name, targets=(l1, l2),
                                        delete_prefixes=(LOD1_DELETE, LOD2_DELETE[role]),
                                        shade_angle=SHADE_ANGLE)
        for i, lod in enumerate(lods, start=1):
            if base_mode(role) == "engine":
                moved = pin_ground(lod)
                if moved:
                    print(f"[tram] LOD{i}: pinned {moved} ground vertices to y 0")
            flipped = fix_lod_decks(lod)                  # revision 2 (C18 LOD2: a flattened deck faces up)
            if flipped:
                print(f"[tram] LOD{i}: flattened decks turned up {flipped}")
                report.setdefault("lod_decks_flipped", {})[i] = flipped
            if base_mode(role) == "plinth":
                # revision 3: the plinth on every LOD (L4 keeps it at LOD1 / LOD2 too: min y -12, nothing on the grade
                # line, no engine base); closure is asserted on LOD1 (decimation keeps the closed parts), measured on LOD2
                rp = check_plinth(lod, f"LOD{i}", closed=True) if i == 1 else check_plinth(lod, f"LOD{i}", closed=False)
                report["C6p"][f"LOD{i}"] = rp
            else:
                check_engine_base(lod, ENGINE_BASE_PARTS_LOD[role], f"LOD{i}")
            tris[i] = cs2_asset.tri_count(lod)
            cs2_asset.export_fbx(lod, os.path.join(asset_dir, f"{name}_LOD{i}.fbx"))
        if tris[1] > 0.5 * n0:
            _fail("C9", f"LOD1 {tris[1]} > 50 % of LOD0 {n0}")
        if tris[2] > min(LOD2_CAP[role], LOD2_CLI_CAP):
            _fail("C9", f"LOD2 {tris[2]} > {min(LOD2_CAP[role], LOD2_CLI_CAP)}")
        report["lod_parts"] = {}
        for i, lod in enumerate(lods, start=1):
            pf, part, tb = mesh_arrays(lod)
            report["lod_parts"][i] = sorted({tb[p - 1] for p in set(part.tolist()) if p > 0})
            # revision 2 (C18 on the decimated levels, skeptic MC8: LOD2 still keeps the 3.6 m bull rim)
            rep_l, _w = check_see_through(lod, role, i)
            report["C18"][f"LOD{i}"] = rep_l
            report.setdefault("lod_top_y", {})[i] = round(float(pf[:, 1].max()), 3)
            print(f"[tram] C18 LOD{i}: {rep_l['back']} back-face first hits of {rep_l['hits']} hits "
                  f"({rep_l['views']} views), worst " + (f"{rep_l['parts'][0]['part']} {rep_l['parts'][0]['back']}"
                                                       if rep_l["parts"] else "none")
                  + f" (<= {rep_l['max_per_part']}); top y {report['lod_top_y'][i]} (LOD0 {report['bounds']['max'][1]})")
    if args.tiled:
        TT.write_settings(asset_dir, name, len(lods), report["tiled"])
    else:
        cs2_asset.write_settings(asset_dir, name, supplied_lods=tuple(range(1, len(lods) + 1)))
    batches = sum(1 + (1 if (lvl == 0 or args.lods) else 0) + 1 for lvl in range(len(lods) + 1))
    if gls is not None:
        batches += len(lods) + 1                         # T76: the _Gls sub-mesh on every level
    if batches > BATCH_CAP:
        _fail("batches", f"{batches} > {BATCH_CAP}")
    report["batches"] = batches
    print(f"[tram] tris {tris}; batches {batches} (cap {BATCH_CAP})")

    # -- previews ------------------------------------------------------------------------------
    import cs2_preview
    lo, hi = report["bounds"]["min"], report["bounds"]["max"]
    size = (hi[0] - lo[0], hi[1] - lo[1], hi[2] - lo[2])
    switch = cs2_preview.lod_switch_table(size, [tris[i] for i in sorted(tris)])
    switch = {"csum": round(switch["csum"], 2),
              "switch": {k_: round(v, 1) for k_, v in switch["switch"].items()},
              "cull": round(switch["cull"], 1), "note": "formula only (lodFactor 652, unverified)"}
    if args.preview:
        prev = os.path.abspath(args.preview)
        os.makedirs(prev, exist_ok=True)
        views = [("three_quarter", (0.9, -1.0, 0.45)), ("front", (0.15, -1.0, 0.30)),
                 ("line", (0.2, 1.0, 0.35)), ("side", (-1.0 if role == "base" else 1.0, 0.1, 0.30)),
                 ("game", (0.6, -0.6, 1.2))]
        if role == "top":           # revision 1c: the -X stair + gangway (the +X side has "side")
            views.append(("stair_w", (-1.0, -0.9, 0.55)))
        modes = ("pbr", "dusk", "normals") + (("lod",) if lods else ()) + ("density",)
        for f in cs2_preview.preview(t, asset_dir, name, prev, modes=modes, views=views,
                                     lods=lods or None, tint=TINT[role]):
            print(f"[tram] preview -> {f}")
        render_hosted(t, name, prev, role, args.cabin)
        render_access(t, name, prev, role)
        t.data.materials.clear()
    if args.closeups:
        for f in render_closeups(t, asset_dir, name, os.path.abspath(args.closeups), role, TINT[role], win,
                                 only=args.closeups_only):
            print(f"[tram] closeup -> {f}")
        t.data.materials.clear()

    # -- manifest + folder validation ----------------------------------------------------------
    contract = prefab_contract(role, report, atlas_rule, base_report, switch)
    fine_block = fine_detail_block(role, report)         # fine-detail step 3c (None with FINE_DETAIL off)
    nb_block = nitpick_b_block(role, t)                  # nitpick B (None on the top / with JH_DETAILS off)
    nb2_block = nitpick_b2_block(role, t, report)        # nitpick B2 (None with RAMPS off)
    nb3_block = nitpick_b3_block(role, t, report)        # nitpick B3 (None on the top / with ANNEX_STAIR off)
    r3_block = plinth_top_block(role, t, report, detail1)  # revision 3 (None on the base / with PLINTH_TOP off)
    print("[tram] hosted local transforms (prefab space):")
    for h in contract["hosted"] or [{"what": "none (top hosts nothing; ObjectSubObjects removed)"}]:
        print(f"[tram]   {h['what']}: pos {h.get('m_Position')} rot {h.get('m_Rotation')} "
              f"parent {h.get('m_ParentMesh')}")
    density = compose.density or []
    cs2_validate.write_manifest(asset_dir, name, {
        "generator": "tram_station.py", "role": role, "kind": kind, "letter": "A",
        "spec": "tram-stations.md + skeptic MC1-11 + ROUND-SPEC R27-R31; revision 2: tram-stations-rev2.md + "
                "skeptic MC1-MC9 + rulings Q1-Q8",
        "args": {k_: v for k_, v in vars(args).items() if k_ not in ("out",)},
        "tris": tris, "parts": len(table), "atlas_px": A, "finish": finish,
        "bake": {"margin_px": margin, "ao": bake.get("ao"),
                 "uncovered_texels": bake.get("uncovered_texels"),
                 "height_range_m": compose.height_range,
                 "stamps_px": {k_: list(v) if v else None for k_, v in compose.stamps.items()}},
        "density_worst": [{"part": table[p - 1] if 0 < p <= len(table) else p,
                           "source_px_per_m": round(s, 1), "shipped_px_per_m": round(d, 1)}
                          for p, s, d in density[:8]],
        "submeshes": ["Win"] + (["Gls"] if gls is not None else []), "win_cells": report["C11"],
        "batches": report["batches"],
        "contracts": {k_: report[k_] for k_ in ("C1", "C3", "C4", "C5", "C7", "C8", "C13",
                                                 "C13_unique")},
        "detail1": detail1,
        **({"fine_detail": fine_block} if fine_block else {}),
        **({"nitpick_b": nb_block} if nb_block else {}),
        **({"nitpick_b2": nb2_block} if nb2_block else {}),
        **({"nitpick_b3": nb3_block} if nb3_block else {}),
        **({"plinth_top": r3_block} if r3_block else {}),
        "C15": {k_: report["C15"][k_] for k_ in ("flights", "ladders", "joins", "not_walkable", "orphans")
                + (("ramps", "C15b", "goal") if RAMPS else ())
                + (("route",) if (ANNEX_STAIR and role == "base") else ())},
        "C17": report["C17"],
        # revision 2: see-through per LOD (C18, C18b), visible coplanar steel (C19: failing, kit-exempt, every
        # pair >= 1 cm2), the wheels to scale + the haul loop (C20 / C20b), the LOD tops (the Q3 height pop)
        "rev2": {"C18": report["C18"], "C19": report["C19"], "C20": report["C20"],
                 "lod_top_y": report.get("lod_top_y"),
                 "haul_route": {k_: v for k_, v in (haul_route_top() if (role == "top" and args.wheels)
                                                    else haul_route_base() if role == "base" else {}).items()
                                if k_ in ("wrap_deg", "strand_deg", "length_m")}},
        "carriers_json": carriers["drift"] if carriers else "unread",
        "prefab_contract": contract,
        **({"tiled": dict(report["tiled"], panes_m=report.get("T9_panes_m"), far_A=A,
                          far_set_L1_low=report.get("far_set_L1_low"),
                          lod_glazepanel_parts={i: sum(1 for n in v if n.startswith("glazepanel_"))
                                                for i, v in (report.get("lod_parts") or {}).items()})}
           if args.tiled else {}),
    })
    # kit color variants (2026-10-12): a shipping export stages the variants its kit rows name
    import dress_kit
    dress_kit.stage_from_manifest(asset_dir, name)
    if not args.no_validate:
        t.name, t.data.name = name + "Src", name + "Src"
        win.name, win.data.name = name + "WinSrc", name + "WinSrc"
        if gls is not None:
            gls.name, gls.data.name = name + "GlsSrc", name + "GlsSrc"
        for i, lod in enumerate(lods, start=1):
            lod.name, lod.data.name = f"{name}LOD{i}Src", f"{name}LOD{i}Src"
        findings = cs2_validate.validate_asset_folder(asset_dir, name, {
            "kind": kind, "lod0_cap": LOD0_CAP[role], "lod2_cap": min(LOD2_CAP[role], LOD2_CLI_CAP),
            "hero": True, "expected_lods": tuple(range(1, len(lods) + 1)),
            "expected_submeshes": ("Win",) + (("Gls",) if gls is not None else ()), "max_texture": 4096})
        report["validate"] = (sum(1 for f in findings if f[0] == "ERROR"),
                              sum(1 for f in findings if f[0] == "WARN"))
        print(f"[tram] validate_asset_folder: {report['validate'][0]} errors, "
              f"{report['validate'][1]} warnings")

    # -- review pack (R10), after export / manifest / validation ------------------------------
    if args.review is not None:
        import cs2_review
        cs2_review.write_pack(name, asset_dir, dict(enumerate([t] + lods)), {"Win": win},
                              meta={"generator": "tram_station.py", "args": vars(args),
                                    "kind": kind, "letter": "A", "tint": TINT[role],
                                    "caps": {0: LOD0_CAP[role],
                                             2: min(LOD2_CAP[role], LOD2_CLI_CAP)}},
                              out_dir=args.review or None)
    for lod in lods:
        me = lod.data
        bpy.data.objects.remove(lod, do_unlink=True)
        bpy.data.meshes.remove(me)
    report.update({"name": name, "asset_dir": asset_dir, "tris": tris, "atlas": atlas_rule,
                   "prefab_contract": contract, "stamps": dict(compose.stamps), "A": A,
                   "labels": dict(compose.labels), "detail1": detail1})
    print(f"[tram] asset folder: {asset_dir}")
    print(f"PASS tram_station: {name} LOD0 {tris[0]} tris, chain "
          f"{[tris[i] for i in sorted(tris) if i]}, atlas {A}, _Win {report['C11']}, engine base "
          f"{base_report['verdict']}"
          + (f" (plinth mode, min y {base_report['min_y']:.2f}, 0 band vertices on LOD0-{len(lods)})"
             if base_mode(role) == "plinth" else "")
          + f", batches {report['batches']}, finish {'on' if finish else 'off'}"
          + (f", fine-detail on ({fine_block['rows']})" if fine_block else ", fine-detail off"))
    report["fine_detail"] = fine_block
    return report


if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        raise
    except Exception:  # noqa: BLE001 - unambiguous exit status for the harness
        import traceback
        traceback.print_exc()
        sys.exit(1)
