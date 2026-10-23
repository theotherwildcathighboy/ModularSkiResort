# StationAlign: station stub-tip geometry, the sign convention, readout classes and place, and move episodes

Pure geometry of the station alignment aid (read-only guide + readout + an "aligned" state at 0.5 m / 0.5 deg), used by
`SkiStationAlignSystem`, `SkiLiftTooltipSystem`, `LiftCensus` and the tests. Citations are Game.dll 1.6.0f1.

## Geometry (verified in the prefab JSON)

- Both station prefabs carry their high-voltage connection stub as an ObjectSubNet of the SkiLiftMarker net from station-local (0,0,-9)
  to (0,0,-12), node indices {0,1}: SkiTopStationBuilding.Prefab:58-106 (tip z at :86), SkiBaseStationBuilding.Prefab:65-112 (net :67,
  tip z at :92). The base's SECOND stub at x = -6 is the low-voltage feed, NOT the lift axis. The lift line leaves the station along
  local -Z through x = 0 on every station but the tram base, whose 6 x 3 lot puts its line axis at prefab x +4.00 in the game frame (the
  4-argument StubTip), so:
  - tip P = LocalToWorld(T, (0,0,StubTipLocalZ)) (ObjectUtils.cs:208-211: position + mul(rotation, local))
  - axis f = normalize(xz(mul(R, (0,0,-1))))
- Measure(partner p, subject s), in the horizontal plane (x, z):
  - d = xz(P_s - P_p)
  - lateral = cross(f_p, d) = f_p.x*d.z - f_p.z*d.x (signed; see the sign convention)
  - along = dot(f_p, d) (> 0 = in front of the partner's stub)
  - yaw = signed angle from -f_p to f_s, degrees in (-180, 180]
  - aligned <=> |lateral| <= AlignedOffsetM and |yaw| <= AlignedYawDeg and along > 0

  "Perfectly 180" = the subject's tip on the partner's axis and the two stubs pointing at each other. The formula is symmetric in role,
  so it works whichever station is placed second.
- Bearing error (the partner pick) = atan2(|lateral|, along) in degrees: how far off the candidate partner's axis the subject sits
  (0 = dead ahead, 90 = abeam, 180 = behind).

## Sign convention

Unity / CS2 world space is left-handed: seen from above (+y), x points right and z points up the screen, so turning from +z to +x is
CLOCKWISE. With cross(a, b) = a.x*b.z - a.z*b.x: lateral > 0 = the subject's tip is LEFT of the partner's axis (seen from the partner,
looking out along its stub), < 0 = right; yaw > 0 = the subject's stub is turned COUNTER-CLOCKWISE (seen from above) from pointing
straight back at the partner, < 0 = clockwise.

## Tolerance 0.5 m / 0.5 deg

The vanilla object tool's right-drag rotation turns 2*pi*0.002 rad = 0.72 deg per pixel (ObjectToolSystem.cs:3495) and the
precise-rotation input 90 deg/s x input (:3478-3479), so 0.5 deg is reachable with the precise input or a one-pixel drag, not by a
coarse drag.

## Readout class (Classify)

The best and the worst state must not both say 180 (at +-180 the sign is noise; the UI prints whole degrees from 100). First match wins:

- Straight: r.Aligned.
- PartnerFacingAway: r.Valid and AlongM <= 0: the subject's tip is behind (or abeam of) the PARTNER's stub, i.e. the other station's
  line leaves away from this one. Turning the SUBJECT cannot fix this (a base facing its top correctly while the top faces away
  measures yaw 180 from the base, and "turn it around" would be wrong advice for the base; the top, measured against the base, is in
  front and gets FacingAway). Reachable only with a ROW partner: a free candidate is picked only with along > 0.
- FacingAway: r.Valid and FacesAway(YawDeg): |yaw| > FacingAwayDeg (90 deg, exclusive), the subject's own line leaves away from the
  partner: "turn it around".
- Numbers: everything else (offset + signed angle, the fine-tuning view).

## Readout place (PlaceOf)

Where `SkiLiftTooltipSystem` puts the readout rows, PlaceOf(preview, moved, defaultTool), first match wins:

- the subject is the object tool's preview -> Mouse (the preview sits under the cursor);
- the default tool is active -> World (a group at the subject's stub tip);
- any other tool, the subject JUST MOVED -> Mouse (SkiStationAlignSystem subject 2: a Move It nudge);
- any other tool, the subject only SELECTED -> None (a station left selected must not add its rows to the hub's run brush, the Net Tool,
  the bulldozer or the tree tool).

## Move episodes (StationMoveEpisodes)

A station whose height re-settles after placement would otherwise log `Align: moved and settled` once per 15-frame pause (18 lines in
3.7 s for one station). `StationMoveEpisodes` folds the census's "last moved station" stream into one episode PER STATION: a move frame
at least MoveGapFrames after that station's previous one counts as a new move; each episode closes on its own after QuietSeconds of wall
clock AND MoveGapFrames frames without a move of that station. The system writes one line per closed episode. Per station, not "close
when another station moves": should the census's last-moved station alternate between two stations frame by frame, a single-episode
tracker would close and log every frame. QuietSeconds = 2 s matches `SkiStationAlignSystem.MovedWindowS` (unmeasured). The pattern of
LiftReconnectEpisodes.cs.
