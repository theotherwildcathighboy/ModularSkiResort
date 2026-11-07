# StationAlign: station stub-tip geometry, the sign convention, readout classes and place, and move episodes

Pure geometry of the station alignment aid (read-only guide + readout + an "aligned" state at 0.5 m / 0.5 deg), used by
`SkiStationAlignSystem`, `SkiLiftTooltipSystem`, `LiftCensus` and the tests. Citations are Game.dll 1.6.0f1.

## Geometry (verified in the prefab JSON)

- Both station prefabs carry their high-voltage connection stub as an ObjectSubNet of the SkiLiftMarker net. The stub runs from
  station-local (0,0,-9) to (0,0,-12), node indices {0,1}. See SkiTopStationBuilding.Prefab:58-106 (tip z at :86) and
  SkiBaseStationBuilding.Prefab:65-112 (net :67, tip z at :92). The base's SECOND stub at x = -6 is the low-voltage feed, NOT the lift
  axis. The lift line leaves the station along local -Z through x = 0 on every station but the tram base. The tram base has a 6 x 3 lot.
  This lot puts its line axis at prefab x +4.00 in the game frame (the 4-argument StubTip). So:
  - tip P = LocalToWorld(T, (0,0,StubTipLocalZ)) (ObjectUtils.cs:208-211: position + mul(rotation, local))
  - axis f = normalize(xz(mul(R, (0,0,-1))))
- Measure(partner p, subject s), in the horizontal plane (x, z):
  - d = xz(P_s - P_p)
  - lateral = cross(f_p, d) = f_p.x*d.z - f_p.z*d.x (signed, see the sign convention)
  - along = dot(f_p, d) (> 0 = in front of the partner's stub)
  - yaw = signed angle from -f_p to f_s, degrees in (-180, 180]
  - aligned <=> |lateral| <= AlignedOffsetM and |yaw| <= AlignedYawDeg and along > 0

  "Perfectly 180" = the subject's tip on the partner's axis and the two stubs pointing at each other. The formula is symmetric in role,
  so it works whichever station is placed second.
- Bearing error (the partner pick) = atan2(|lateral|, along) in degrees: how far off the candidate partner's axis the subject sits
  (0 = dead ahead, 90 = abeam, 180 = behind).

## Sign convention

Unity / CS2 world space is left-handed. Seen from above (+y), x points right and z points up the screen. So turning from +z to +x is
CLOCKWISE. With cross(a, b) = a.x*b.z - a.z*b.x:

- lateral > 0 = the subject's tip is LEFT of the partner's axis (seen from the partner, looking out along its stub). lateral < 0 = right.
- yaw > 0 = the subject's stub is turned COUNTER-CLOCKWISE (seen from above) from pointing straight back at the partner. yaw < 0 =
  clockwise.

## Tolerance 0.5 m / 0.5 deg

The vanilla object tool's right-drag rotation turns 2*pi*0.002 rad = 0.72 deg per pixel (ObjectToolSystem.cs:3495). The precise-rotation
input turns 90 deg/s x input (:3478-3479). So 0.5 deg is reachable with the precise input or a one-pixel drag, not by a coarse drag.

## Readout class (Classify)

The best and the worst state must not both say 180 (at +-180 the sign is noise, and the UI prints whole degrees from 100). First match wins:

- Straight: r.Aligned.
- PartnerFacingAway: r.Valid and AlongM <= 0: the subject's tip is behind (or abeam of) the PARTNER's stub, that is, the other station's
  line leaves away from this one. Turning the SUBJECT cannot fix this. Example: a base faces its top correctly while the top faces away. The base measures yaw 180, and "turn it around" would be wrong advice for the base. The top, measured against the base, is in front and gets FacingAway. Reachable only with a ROW partner: a free candidate is picked only with along > 0.
- FacingAway: r.Valid and FacesAway(YawDeg): |yaw| > FacingAwayDeg (90 deg, exclusive), the subject's own line leaves away from the
  partner: "turn it around".
- Numbers: everything else (offset + signed angle, the fine-tuning view).

## Readout place (PlaceOf)

Where `SkiLiftTooltipSystem` puts the readout rows, PlaceOf(preview, moved, defaultTool), first match wins:

- the subject is the object tool's preview -> Mouse (the preview sits under the cursor).
- the default tool is active -> World (a group at the subject's stub tip).
- any other tool, the subject JUST MOVED -> Mouse (SkiStationAlignSystem subject 2: a Move It nudge).
- any other tool, the subject only SELECTED -> None. A station left selected must not add its rows to the hub's run brush, the Net Tool, the bulldozer or the tree tool.

## Move episodes (StationMoveEpisodes)

A station whose height re-settles after placement would otherwise log `Align: moved and settled` once per 15-frame pause (18 lines in
3.7 s for one station). `StationMoveEpisodes` folds the census's "last moved station" stream into one episode PER STATION:

- A move frame at least MoveGapFrames after that station's previous one counts as a new move.
- Each episode closes on its own after QuietSeconds of wall clock AND MoveGapFrames frames without a move of that station.

The system writes one line per closed episode. The tracker works per station. It does not "close when another station moves". If the
census's last-moved station alternates between two stations frame by frame, a single-episode tracker would close and log every frame.
QuietSeconds = 2 s matches `SkiStationAlignSystem.MovedWindowS` (unmeasured). It follows the pattern of LiftReconnectEpisodes.cs.

## Reversed snap refusal (FIX-LIST 54(b), StationFacingRefusalSystem)

The vanilla object tool snaps a station's stub sub-net onto a net node. It has two candidates: the tool's own rotation, and the stub
aligned to the edge. It ranks them by snap priority from the cursor distance alone (ObjectToolSystem.cs:689-700, NetIterator). So a
station turned the wrong way snaps onto a lift line end, places and attaches. Example: the field steep tram base at yaw 128.7, its lot
front along the line toward the top, stood 28.8 m above the predicted front.

**Either stub node lands on the end** (in-game check 2026-11-04, field/2026-11-04/plan-fixes-2/RESULTS.md). Every station's HV stub
(SkiLiftMarker) runs `StubLengthM` (3 m) from its inner node at StubTipLocalZ + 3 to the tip (FG / tram -9 to -12, DQ / gondola -13 to
-16). EI-48 checks the mirror. At the steep tram base, vanilla offered two reversed snaps:

- The INNER node on the end node. This snap merged (the end node's Temp copy took the SkiLiftMarker prefab, `Temp.m_Original` the end
  node) and PLACED.
- The tip on the node. Vanilla already refuses this snap ("Invalid shape").

The first version (7e52477) tested the tip only and missed the snap that places. An FG top preview showed the same inner merge.

**The rule** (pure, LiftBatch EI-54b):

- `LineEnds(edges)` gives the TRUE ends of the built lift-line net. These are the nodes with exactly one lift-line edge, each with the
  line's next node. An interior tower node has two edges and is never judged. 7e52477 also refused a station on a tower node with the
  next span behind, and logged it as a "line end".
- `AttachesBackward(axis, tip, inner, end, next)` returns the stub node (Tip / Inner, the nearer) within `AttachTolM` (1 m) of the end
  node, when the next node lies behind the stub measured from the end node (`LineLeavesBackward`). Otherwise it returns None.

**The system** applies the rule to every Temp catalog station (not Delete). It checks the station against the true ends that this
frame's preview touches: an end node that some Temp node (not Delete) copies, `Temp.m_Original` == the end. Vanilla makes that copy for
every existing node that a preview's net definition connects to, and hides the original (GenerateNodesSystem.cs:1226-1228, :1306). The
54(a) probe saw the copy on every station snap, merged into the stub or not. The edges must meet these conditions: lift-line prefab, no
Owner (not a station's own stub), not Temp, not Deleted.

A match gets ErrorSeverity.Error for the station in ValidationSystem.Components' error map (PowerJoinRefusalSystem's mechanism and
anchor: UpdateBefore Components at ModificationEnd). The results:

- The preview is red and cannot be placed.
- StationFacingTooltipSystem shows SkiResort.StationFacing.REFUSED.
- The INFO line names the stub node, the end node, its Temp copy and the next node.

The system refuses and never re-snaps, because picking the aligned candidate would mean patching the vanilla tool. Other cases:

- The system never refuses a station placed loose (no Temp copy of the end). The Align readout keeps its "facing away" hint.
- An aligned station with its inner node on the end (the line leaving ahead through the stub tip) is not a facing case. The system
  leaves it to vanilla.
- Not covered: Move It (no validation pass).

Tests: LiftBatch EI-54b (both field snaps, the aligned snap, the FG top, LineEnds, 8 stations x 36 yaws x 2 stub nodes x 10 bearings).
The tests fail on 7e52477's rule.
