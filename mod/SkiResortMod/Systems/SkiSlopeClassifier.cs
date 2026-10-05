// SkiSlopeClassifier — pure, deterministic slope-difficulty rating for a ski-run polygon
// (docs/research/run-feedback-spec.md §2-§3 as amended by §9 "Round R2" and its last subsection
// §9.8 "Round R2b", which win where they disagree; DECISIONS 113, 116, 118; SPEC §6.1 =
// docs/SPEC.md:235-241, bands amended by §9.2).
// Entities-free on purpose: usings are System, System.Collections.Generic and
// Unity.Mathematics ONLY, so the same file is compiled into the net48 mod (default **/*.cs
// glob) AND linked into the net8 headless test host tests/SlopeBatch (run: `dotnet run
// --project tests\SlopeBatch\SlopeBatch.csproj -c Release` from the ModularSkiResort root).
// Nothing here is copied from ParkManager.
//
// CONTRACT: no static mutable state (consts only), never logs, never throws on bad input —
// every degenerate input returns a SlopeStatus and leaves the outputs cleared; a tampered grid
// (public fields edited after Plan) cannot index out of range. Zero managed allocation per call
// once the caller's reused SlopeGrid / SlopeResult buffers have grown (buffers grow, never
// shrink; fixed top-k insertion arrays, a counting sort, union-find, a binary heap (open last
// bucket only) and BFS on reused arrays; per-row run lists; no Sort, no LINQ).
//
// WHAT THE RATING IS (R2 §9.3, end zones and connectivity as replaced by R2b §9.8). A resort
// rates a run by its hardest part that a skier cannot AVOID. R1 rated the steepest ~86 m² (the
// k-th largest 20 m window), which on the field's 68,300 m² meadow was a corner touching a bank:
// Double black 41.7 % on a 17.2 % mean run (DECISIONS 116). R2 / R2b:
//   RatingGrade = the crux of the easiest line = min over all connected paths of inside cells
//   from the run's top end zone to its bottom end zone of the max per-cell value o on the path
//   (a bottleneck / minimax path). A steep corner or side bank the line can go around does not
//   count; a full-width headwall or a chute does.
//   - Per-cell value o (published as CellGrade; CellBand = BandOf(o)) = R1's overlay value: the
//     centred 20 m fall-line window when it fits the Mask, else the longest centred window of
//     15 / 10 / 7 m that fits, else |g| (Classify below).
//   - Connectivity (R2b, §9.8): SlopeGrid.DiagonalRule, set by the CALLER (true when its step
//     multiple >= 2; the classifier does not know m) and copied into SlopeResult.DiagonalRule.
//     false (m = 1): the 8 neighbours — R2's code path, unchanged and bit-identical (R2b round,
//     a scratch A/B linking R2's file: 2,999 random cases, every output identical with the
//     height key forced). true (m >= 2): the 4 orthogonal neighbours, plus a diagonal only when
//     BOTH cells it shares orthogonally with the step are outside the Mask (so a one-cell-wide
//     diagonal sliver stays connected), in the union-find AND the crux BFS. Why: at 7 m cells an
//     8-connected path slipped diagonally through a one-cell-wide band of high values (a
//     full-width 12 m bank @ 50 % read up to 8.1 % low at m = 2).
//   - End zones (R2b, §9.8). n = inside cells, ḡ = (Σgx, Σgz) / n the mean box gradient
//     (MeanGradient, internal). KEY: when |ḡ| >= EndZoneMinMeanGrade (3 %) OR FallCoherence =
//     |Σg| / Σ|g| >= EndZoneMinCoherence (0.5; the OR clause added in the R2b fixer round), the
//     position along the mean fall line, key = -(FallLine · (i, j))·step ("up the fall line" =
//     larger key), computed inline per cell (ZoneKey; no per-cell buffer); otherwise (cones,
//     craters, saddles, flat ground: no usable axis) the node height, exactly as R2. Published
//     as ZoneByFallLine. Coherence does not REPLACE the |ḡ| clause: coherence falls with wall
//     steepness, so a coherence-only gate put steep-sided valleys back on the height rule (§9.8:
//     10 % floor with 50 % walls Black 50.9 % under a 0.3 coherence gate; SlopeBatch R2b end
//     zones: Green 8.99-9.96 %, coherence 0.27, taken by |ḡ|). It is ADDED because the |ḡ|
//     clause alone sent a long gentle run whose gradients all agree (coherence ~1) to the
//     height key and R2's area cap below 3 %, which skipped a full-width pitch near an end
//     again (review finding code-1; SlopeBatch "key gate", 8 frames, R2b as first built in
//     brackets): a 30 m 45 % drop-in atop a 60 m-wide 1 % run reads 42.71-44.96 % Black at
//     every length [500 m Black, 600 m 12.90-44.96 % Green..Black by orientation, 650 m and
//     longer 1.89-12.90 % Flat..Green: the band flipped as the end node crossed the gate, and
//     the tooltip called the unavoidable drop-in "avoidable"]; §9.8's 10 × 2000 trail with a
//     60 m 50 % wall on 1 % / 0 % ground 50.00-50.18 % [0.95-1.16 / 0.00 % Flat]. Cones,
//     craters and saddles (coherence <= 0.007) and noise on flat ground (<= 0.304 for ±0.3 m
//     at 5 m) keep the height fallback. SIZE: z = clamp(round(EndZoneShare·n), 1, max(1, cap))
//     with cap = floor(n·EndZoneDepthM / L), L = (max - min key over the inside cells) + step,
//     on the fall-line key — EndZoneDepthM of run at the run's MEAN width (it is a cell count;
//     at an end narrower than the mean the zone runs deeper, limit (g)) — and R2's area cap
//     floor(EndZoneMaxM2 / step²) on the height key.
//     SELECTION (unchanged): 1024 equal bins over the key's inside range (one bin below 1 mm),
//     whole bins from each end until >= z cells, so a zone can hold more than z (deterministic,
//     independent of scan order). The key range is read from the clamped Mask runs' END cells
//     (the key is linear along a row and float rounding is monotone: exact, O(runs)); the same
//     ZoneKey / HeightBin call bins every cell in the histogram (pass 1) and in the zone bits
//     (pass 2), and the BFS reads those bits (no third evaluation). FallLine, MeanGradient and
//     FallCoherence are final before Crux on every path (ClassifyRing sets them first;
//     CruxForTest re-reads the stored values). Zones, not the single highest / lowest cell,
//     because a drawn run's entry and exit are unknown and its very top is exactly where it
//     pokes onto a bank (the field case); a cap stops a long runout from swallowing a short
//     pitch at the top. Zones
//     may overlap (flat ground): a shared cell is a zero-length path. WHY R2b (§9.8): R2 ranked
//     by raw node height, so on any polygon with side ground the highest / lowest cells sat on
//     that side ground, not at the run's ends; and its cap was an AREA, so it skipped
//     EndZoneMaxM2 / width of run at each end (33 m at 60 m wide, 100 m at 20 m, 200 m at 10 m).
//   - Algorithm, O(n) (O(n + k log k) when the minimax is in the open last bucket, k = its
//     cells): counting sort of the inside cells into RatingBuckets = 2048 buckets,
//     bucket = min(2047, (int)(o·1024)) (1/1024 = 0.098 % wide up to 200 %); union-find (path
//     halving, union by size) over flat cell indices plus virtual TOP / BOTTOM nodes; buckets
//     activated in ascending order, each cell unioned with its active inside neighbours (the
//     connectivity above: one union per run of active 8-neighbours on R2's path, one per allowed
//     neighbour under the diagonal rule) and with TOP / BOTTOM when in a zone, until TOP ~
//     BOTTOM. RatingGrade = the largest o in that
//     bucket: order-independent and at most one bucket width above the exact minimax (SlopeBatch
//     checks every ring rating it makes against a brute-force minimax oracle). Bucket 2047 is
//     OPEN ([199.9 %, inf)), where "the largest o in the bucket" would be the largest value of
//     any cell above 199.9 %, avoidable or not (review finding, R2 fixer round: a full-width
//     235 % step plus an avoidable 60 m pit rated 310.7 % with the crux ring in the pit). So that
//     bucket's cells are activated in ascending o (a binary min-heap over its Order segment)
//     with a TOP ~ BOTTOM test per cell, and RatingGrade is the EXACT minimax there — the bound
//     "at most one bucket width above the exact minimax" now holds at every grade (§9.3 as
//     amended in the R2 fixer round; SlopeBatch R2-l and the oracle assert it).
//   - Not connected (the Mask falls apart between the zones: self-intersecting or hair-thin
//     polygons): Connected = false, RatingGrade = P90Grade.
//   - Crux cell (CruxI / CruxJ grid-local, CruxHeight; the overlay's marker), the full traversal
//     contract (pinned in the R2 fixer round so SlopeBatch's independent BFS can assert the cell
//     exactly): traversable = the inside cells with o <= RatingGrade (below 200 % exactly the
//     buckets <= the connecting one); seeds = the traversable top-zone cells in row-major scan
//     order — a seed that is also in the bottom zone is the crux at once (a zero-length path);
//     breadth-first, each dequeued cell's neighbours expanded in row-major order (NW N NE W E
//     SW S SE = flat offsets -Nx-1, -Nx, -Nx+1, -1, +1, Nx-1, Nx, Nx+1; under the diagonal rule a
//     diagonal is skipped unless both cells it shares orthogonally are outside the Mask); stop
//     when the first bottom-zone cell is DISCOVERED (not when it is dequeued); walk the parents
//     back; the crux is the path cell with the largest o, the one nearest the top on ties.
//   - Also published: SteepestGrade = R1's statistic unchanged (k-th largest full window, k =
//     max(MinRatingCells, ceil(RatingAreaM2 / step²)); in ShortRun the k-th / last largest
//     overlay value) — "the steepest patch, avoidable or not"; P90Grade = the smallest bucket top
//     (b + 1) / 1024 with >= 90 % of inside cells in buckets <= b (saturates at 200 %);
//     TopZoneCells / BottomZoneCells, ZoneByFallLine, DiagonalRule. Band = BandOf(RatingGrade).
//     Status: ShortRun when fewer than k full windows fit (unchanged meaning; the rating is
//     still the crux), else Ok.
//   - Polyline mode keeps its statistic: a line has exactly one path, so its k-th window IS its
//     crux. SteepestGrade = P90Grade = RatingGrade, Connected = true, CruxValid = false,
//     ZoneByFallLine = false.
//
// WHAT R2b FIXED (SlopeBatch "R2b end zones", asserted, m = 1, 8 frames = rotations 0/17/45/73°
// × 2 origins; R2's value in brackets): valley 600 × 120 (40 m floor at 15 %, 30 % sides)
// 14.24-14.94 % Green [31.8-33.6 % Blue]; steep-wall valleys (10 % floor, 40 / 50 % walls)
// 9.23-9.96 / 8.99-9.96 % Green; open bowl 400 × 240 (15 % floor, 42 % walls) 14.14-14.84 %
// Green [41.9-42.0 % Black]; ridges 300 × 60 / 600 × 120 / 1000 × 80 / 2000 × 80 (12 % crest,
// 27.7 % flanks) 13.04-15.33 % Green [600 × 120 27.6-27.7 % Blue]; a full-length 50 m 45 % side
// bank on a 150 m-wide 15 % run 14.61-15.04 % Green [44.8-45.0 % Black]; the F116 meadow with
// 48 % corner pokes 60 × 80 / 80 × 100 / 120 × 100 m 19.04-19.24 / 19.43-20.11 / 20.50-21.07 %
// Green [80 × 100: 42.3-43.5 % Black]; a 71 % sliver along the top side (13 % of the area)
// 16.01-16.11 % Green; a full-width 40 m 71 % top face on 800 × 125 70.90-71.12 % Double black;
// a 68 % mountain face 68.07-68.26 %; 20 × 1500 trails at 20 % with a full-width 40 m 45 % wall
// at s 60-100 / 60-100 m above the bottom 45.00-45.18 % Black [31.6-37.7 / 30.6-38.7 % Blue]; a
// 60 m 50 % wall at s 100-160 of a 10 × 2000 trail 50.00-50.18 % [20.1-20.2 % Green]; the 30 m
// 45 % drop-in atop a 12 % run at 20 / 60 / 125 / 200 m wide 43.30-45.00 % Black at every width
// [12.0-12.2 / 20.8-25.6 / 44.8-45.2 / 44.9-45.2 %]; wide-short 150 × 400 with a full-width 45 %
// headwall 45.00-45.18 %; an L-run with a 45 % pitch in its second leg 44.90-45.01 %; cone /
// crater / saddle: |ḡ| <= 0.002, height key taken, rating bit-identical to the forced height
// key (R2's rule): 29.98-30.08 / 29.98-30.08 / 41.30-41.80 %. Unchanged: R2-a..R2-d, the
// runouts, the DECISIONS 116 field fixtures (Green 19.04-19.14 %). At m = 2 (diagonal rule) a
// full-width 12 m bank's crux - oracle is -2.88 / -2.66 / -2.95 % on 60 / 80 / 100 m-wide runs
// [-8.06 / -6.65 / -4.45 %], the 5 m bank -0.46 / -0.15 / -0.15 % [-0.46 / -1.28 / -3.92 %].
// R2b FIXER ROUND (the key gate's coherence clause; "below the gate" fixtures, asserted: |ḡ| <
// 3 %, fall-line key by coherence 1.000, no "avoidable" tooltip line; R2b as first built in
// brackets): §9.8's 10 × 2000 trail with a 60 m 50 % wall at s 100-160 on 1 % / 0 % ground
// 50.00-50.18 % Black [0.95-1.16 / 0.00 % Flat]; a 20 × 1200 trail on 1.5 % with a 40 m 45 % wall
// at s 60-100 44.96-45.18 % [15.93-31.97 % Green..Blue]; the 30 m 45 % drop-in atop a 60 m-wide
// 1 % run 700 m long 42.77-44.96 % [1.89-12.90 %], atop a 2 % run 1,500 m long 42.87-44.96 %
// [13.53-18.05 % Green]. Every other SlopeBatch line is unchanged by the clause (scratch A/B of
// the full suite before the tests were added: identical apart from 39 classifications' key).
//
// KNOWN AND DELIBERATE (§9.3 / §9.8), with SlopeBatch's numbers:
//   (a) a headwall or drop-in entirely inside an END zone is skipped, at the top AND at the
//       bottom. R2b: the fall-line zone is ~EndZoneDepthM = 20 m of run on a run of uniform
//       width (top zones 96-102 cells on a 60 m-wide run ≈ the top 20.8 m; deeper at an end
//       narrower than the mean, (g)), so R2-e, a 45 % band at
//       the top of a 60 × 600 m run, reads 26.70-31.84 % Blue at 20 m long (it straddles the
//       zone depth; R2: 10.5-10.7 % Green), 44.90-45.18 % at 40 m (R2: 37.1-42.2 %, a §9.4 (2)
//       SPEC-GAP of -2.81 % that is gone: worst shift -0.28 %), 45.01-45.18 % at 80 m (3 starts ×
//       7 angles). The 30 m drop-in reads 41.85-45.18 % (Black) over those 21 frames at 125 /
//       200 m wide (43.30-45.00 % on the 8 asserted frames). On the height key (cones, craters,
//       saddles; gentle runs whose gradients disagree, (j)) R2's area cap still skips
//       EndZoneMaxM2 / width at each end.
//   (b) not monotone under supersets: widening a run can add an easier line (old invariant (5)
//       dropped; SlopeBatch's nested pairs: 5 of 28 fall, worst -2.05 % — R2: 3, -0.11 %; a
//       wider polygon also moves the mean fall line and so the zones). SteepestGrade still is.
//   (c) a narrow traverse is still rated by its sidehill (no run-axis input).
//   (d) among equal-valued cells the crux follows scan order (deterministic).
//   (e) R2b, a run that curves strongly around convex terrain (fall line turning with the run):
//       position along the MEAN fall line is not position along the run. SlopeBatch's C-run
//       (two straight 150 m arms 40 m wide joined by a 180° bend of radius 60 m around a hill
//       falling 10 % outward; 40 % for the first 120 m, then 12 %): the arms' along-run fall
//       lines cancel in the mean, so the fall-line key puts both arms' far ends (the run's start
//       AND its end) in the top zone and the bend in the bottom zone, and the easiest "line"
//       climbs the gentle last arm: 15.62 % Green on the fall-line key, crux in the last arm,
//       against 41.20-41.40 % Black on the height key (crux in the steep first arm) — lenient.
//       A switchback whose fall line is constant (three 300 m traverses on 20 % / 35 % ground)
//       reads 34.96-35.15 % Blue on both keys.
//   (f) R2b, a full-length avoidable side cliff covering ≳ 40-55 % of the width tilts the mean
//       fall line onto itself (geometry-dependent): a 60 % cliff over 40 % of a 150 m-wide 10.5 %
//       run reads 12.50-31.68 % (Green..Blue) by orientation (R2: 59.9-60.1 %).
//   (g) R2b fixer round (review finding rule-and-tests-zone-depth-uses-mean-width): the depth cap
//       is a cell COUNT, n·EndZoneDepthM / L = EndZoneDepthM at the MEAN width, so at an end
//       narrower than the mean the zone runs EndZoneDepthM × mean / end width down the run, and
//       R2's limit (b) returns by shape (SlopeBatch "zone depth", 8 frames; two WARN SPEC-GAP
//       lines against §9.8's "~20 m whatever the width"): a 600 m run at 15 % whose top 200 m
//       is a 20 m trail opening into a 150 m bowl: top zone 103.0-106.7 m deep, a full-width 60
//       (40) m 45 % pitch at s 20-80 (40-80) 15.04-15.15 % Green; a 150 m slope narrowing into a
//       20 m cat track for its last 150 m: bottom zone 108.6-117.1 m, a 40 m pitch 30-70 m above
//       the bottom 15.04-15.17 %; 50 / 150 m funnels 44.8-48.7 m, 17.32-22.80 / 19.15-22.49 %.
//       Constant-width controls 45.00-45.18 % Black, zone 17.5-20.7 m. Measured option, NOT
//       applied (a trade-off, Richard's call): also stop each bin walk at 2.5·EndZoneDepthM of
//       key — the trail / cat-track rows read Black 44.99-45.18 %, but (f) moves from 12.4-31.7 %
//       to 38.4-56.2 % (Blue..Black), a 60 × 80 poke under a 100 m-slanted top edge from 19.0-22.7
//       % Green to 26.6-36.5 % Blue, 80 × 100 from 39.5-39.8 % to 40.2-40.9 % (14-frame scratch
//       lab, review's variant A); a pure 20 m key-depth cap breaks slanted top edges (R2-a
//       slanted 60 m 30.9-42.1 %, the 30 × 40 field poke rotated 30° 24.0-35.7 %; variant B):
//       rejected.
//   (h) R2b fixer round (review finding rule-and-tests-fall-key-zone-past-crest): the key is a
//       position, it never reads height, so a polygon drawn more than ~EndZoneDepthM past a
//       crest (past a valley floor) puts the top (bottom) zone on the back slope (counter-slope)
//       and every line climbs it (SlopeBatch "past a crest", 8 frames; 400 m at 20 % below the
//       crest, 60 m wide): 20 m past 20.02-20.44 % Green (asserted); 30 / 40 / 60 m onto a 30 %
//       back slope 27.63-30.05 / 29.97-30.05 / 29.97-30.05 % Blue, onto 45 % 41.74-45.14 /
//       45.00-45.14 / 45.01-45.18 % Black, 30 m onto 60 % 56.07-60.05 %; 30 / 40 / 60 m past a
//       floor up a 30 % counter-slope 27.10-30.05 / 29.97-30.05 / 29.97-30.05 %, 40 m up 45 %
//       45.01-45.18 % — the height key reads 20.02-20.20 % on all of them. Across a crest at
//       s 100 (45 % up, 15 % down; |ḡ| 0.029-0.031, coherence 0.14): 15.04-45.18 % by orientation.
//       Measured option, NOT applied (a trade-off, Richard's call): extend each zone to the key
//       bin of the highest (lowest) inside cell when that bin lies in the top (bottom) quarter of
//       the key range, ties within 1 m going to the own end — every overshoot row Green
//       20.02-20.67 % (60 % back slope 20.18-26.98 %), across-crest 15.04-22.10 %, every other
//       SlopeBatch line unchanged; but a knoll in the top quarter higher than the top edge pulls
//       the zone down to it (a 40 m knoll at s 110: a full-width headwall at s 40-80 goes from
//       43.16-44.11 % to 20.02-20.21 %), as R2's height key does (scratch lab, review's variant X).
//   (i) R2b fixer round (review finding rule-and-tests-rotated-poke-not-exercised): §9.8's
//       "large corner pokes read Green" holds for a top edge roughly ALONG the contours. With the
//       polygon rotated about the poke corner (corner highest) or its top edge slanted, the
//       count-capped top zone collects in the highest corner wedge (SlopeBatch "pokes with the
//       top edge NOT along the contours", F116 meadow, 8 frames): the 30 × 40 field-size poke
//       19.04-19.24 % Green at -30 / 20 / 30 / 45° and slants -60 / 60 / 100 m (asserted); 60 × 80
//       up to 20.02-25.36 % (30°); 80 × 100 19.04-26.14 / 41.39-42.18 / 37.19-40.89 % at 20 / 30 /
//       45°, 22.83-36.12 / 39.54-39.84 % at 60 / 100 m slant; 120 × 100 29.20-37.74 / 41.49-42.06
//       / 40.16-41.10 %, 37.97-47.24 / 47.74-48.61 %.
//   (j) R2b fixer round, the coherence clause's own threshold: below 3 % mean gradient, terrain
//       whose gradients partly cancel (coherence < 0.5) keeps R2's height key and area cap, and
//       their limits (SlopeBatch "key gate: coherence survey", 8 frames): the 30 m drop-in atop a
//       1 % run with a ±1 m 140 × 110 m wave reads 41.37-43.14 % at 700 m long (coherence
//       0.55-0.56) and 4.10-5.08 % Flat at 1,500 m (0.45-0.46); §9.8's trail10 on 0 % ground with
//       ±1 m noise at 5 m wavelength reads 49.90-51.81 % at coherence 0.58-0.65 on the 8 frames,
//       but straddles 0.5 over 14 frames (0.28-0.65; 0.19-51.81 %, scratch lab). As with the |ḡ|
//       clause alone, dragging such a run across the threshold flips the band; the clause moves
//       the flip from every coherent gentle run near 3 % to textured or cancelling terrain.
// MEASURED LIMITS of the crux (SlopeBatch; the line follows the LOWEST-reading cells, so
// per-cell under-reads become rating under-reads):
//   - Knife-edge ridge (12 % crest, 25 % flanks): the line runs down the crest, whose cells read
//     12 % on a node and up to ~20 % between nodes. R2b, m = 1: 300 × 60 12.29-15.23 %, worst
//     rotation / translation shift +1.69 % (R2: 13.8-19.9 %, +6.07 %), 2,000 × 80 12.29-15.33 %,
//     +1.78 % (R2 read 26.8-27.7 % there, its bottom zone on a flank, within 0.82 %): §9.4 (2)'s
//     "rotation changes the rating <= 1 %" is not met (two WARN SPEC-GAP lines, envelopes 2.2 /
//     2.3 %; all Green). At m = 2 the diagonal rule links a crest running diagonally across the
//     lattice only through flank cells: 2,600 × 80 reads 12.53-20.49 % (worst +6.45 %; R2
//     -5.60 %), the trade-off the R2 review predicted ("a diagonal ridge crest reading up to
//     20.5 % instead of ~14 %"), still Green.
//   - A full-width pitch SHORTER than ~17 m: its 20 m-window plateau (20 m minus the pitch
//     length) is narrower than a 3.5 m cell, so the lattice nodes straddle the peak and every
//     per-cell value reads below it (~2 %/m off the plateau). 50 % pitches in a 10.5 % run at
//     m = 1 (SlopeBatch ShortHeadwallTests, closed-form oracle 42.1 / 46.05 / 50 %): 16 m crux
//     -2.0..-0.1 %, 18 m -3.4..-1.1 %, 20 m -4.7..-2.1 % (SteepestGrade -0.7..+0.2, -1.5..0.0,
//     -3.5..-1.4 %); regression envelopes, no spec bound. 12 m / 5 m banks (plateaus 8 / 15 m)
//     and 60 m pitches read within 0.4 %.
//   - Next to steep ground outside the polygon, box gradients tilt toward it and in-run windows
//     run off the fall line, reading g·cos(tilt): a 60 % cliff one step outside a 10.5 % run
//     moves the crux by -0.55 % (SteepestGrade +0.23 %).
//   - At m = 2 (runs over 200,704 m², 7 m cells) the 7 m lattice still blurs narrow features:
//     with the diagonal rule a full-width 12 m bank @ 50 % in a 10 % run (analytic 34 %) reads
//     crux - oracle -2.88 / -2.66 / -2.95 % on 60 / 80 / 100 m-wide runs (-2.66 % at 20 and 30 m
//     wide; R2's 8-connected slip read -8.06 / -6.65 / -4.45 %, -14.71 % at 20 m), the same
//     size as SteepestGrade's -2.66 % there (SlopeBatch regression envelopes, no spec bound);
//     short 50 % headwalls (report only at m = 2) 16 / 18 / 20 m -4.78..-0.12 / -5.98..-1.54 /
//     -9.23..-1.43 % (R2 -10.45..-1.65 / -12.81..-3.99 / -15.02..-6.70 %). The rule's measured
//     costs: the m = 2 noise shift (±0.3 m at 5 m) 2.45-2.49 % (R2 2.01 %, bound 3 %), the
//     diagonal ridge crest above, and the rotation shift of an xl bank at m = 2 1.71-2.12 % (R2
//     6.41-7.21 %); a ±3 m spike moves the m = 2 crux <= 0.25 % (R2 0.30 %).
//
// RUN LENGTH (R3, docs/research/run-binding-glade-spec.md §5.1; decision D14: HORIZONTAL map
// metres, which differ from along-the-slope metres by < 5 % below a 30 % grade). Published as
// SlopeResult.Length / LengthRect / AxisLength (the drawing tooltip's "Length" line). Geometry
// only, computed in Classify, reset by Clear(); NOTHING in the rating reads it — every rating
// output is bit-identical to R2b (SlopeBatch's rating fingerprint: 34,058 classifications,
// FNV-1a 0x3FAEB2F59B9897D1 with the pre-R3 and the R3 file).
//   Ring:     Length = max(LengthRect, AxisLength).
//             LengthRect = (P + sqrt(max(0, P² − 16A))) / 4, the centreline of the rectangle with
//             the sanitized ring's perimeter P (closing edge added explicitly: Plan stripped the
//             duplicated closing vertex) and |shoelace| area A. Exact for any constant-width band
//             (P² − 16A = 4(L − W)² for an L × W rectangle; mitred bends and circular arcs keep
//             A = L·W and P = 2L + 2W), whatever its bends: SlopeBatch's L, dogleg and C-run
//             read 500.0 / 550.0 / 488.5 m for 500 / 550 / 488.5 m centrelines.
//             AxisLength = the fall-line key range of the inside cells + step (the depth cap's
//             L, Crux) when ZoneByFallLine, else 0. It rescues outlines whose perimeter
//             under-states their length (a pointed or tapered outline) and over-reads a straight
//             run by up to about one cell (cell CENTRES + step on a rotated lattice: rect 600 × 60
//             reads 600.0-603.3 m over 4 frames).
//   Polyline: Length = the horizontal arc length (LengthRect = AxisLength = 0).
// KNOWN LIMITS (accepted, spec §5.1; SlopeBatch "length" rows, report-only against the designed
// centreline, regression-asserted at ±0.1 %):
//   (L1) a bowl funnelling into a trail OVER-reads: its wide top adds perimeter, not length.
//        SlopeBatch funnel (200 m bowl × 150 m, 100 m taper, 30 m trail × 400 m): 722.2 m for
//        650 m (+11.1 %); the spec's skeptic measured ~19 % on its own funnel.
//   (L2) a diamond or tapered outline on the HEIGHT-key fallback (cones, craters, flat ground:
//        AxisLength = 0) reads LengthRect alone: a 500 × 240 m diamond on flat ground reads
//        407.3 m (−18.5 %; the spec's "407 vs 500 m"); a 500 m taper 150 → 30 m wide 504.4 m.
//        On the fall-line key the axis term restores them.
// Field captures (2026-10-01, all on the fall-line key; LengthRect / AxisLength → Length):
// 092600 433.8 / 509.9 → 509.9; 093404 1010.8 / 1030.6 → 1030.6; 093455 and 093534 239.8 / 308.5
// → 308.5; 093640 833.1 / 703.8 → 833.1; 093940 2170.2 / 1940.4 → 2170.2; 094240 2665.6 /
// 2618.5 → 2665.6; 095100 825.6 / 864.4 → 864.4 (LengthRect agrees with the spec's skeptic
// recomputation to the metre on all eight).
//
// ENGINE FACTS this rests on (1.6.0f1 citations; the caller relies on them too):
//  - One height texel = 1 / hd.scale.x world metres: scale = float3(res.x, res.y-1, res.z) /
//    float3(14336, heightScale, 14336) (Game.Simulation/TerrainSystem.cs:2618-2620; 1.6.2f1
//    :2630-2632) = 3.5 m on a 4096-wide heightmap. Vertical quantum = heightScale / 65535
//    (≈ 0.0625 m at the default 4096). Texel centres sit at world -hd.offset.xz + k·texel
//    (TerrainUtils.cs:35-38; offset carries the -0.5 texel shift, TerrainSystem.cs:2621-2622),
//    so the caller anchors the lattice there and the lattice is WORLD-fixed.
//  - TerrainUtils.SampleHeight(ref data, pos) is a plain bilinear over four texels
//    (Game.Simulation/TerrainUtils.cs:82-93 dispatch, :121-137 SampleHeightInternal). At step
//    multiple m = 1 the lattice nodes ARE the texels, so this file's own bilinear over the
//    node heights reproduces the game's surface exactly (no double smoothing).
//  - The three-argument overload's `normal` is in RAW heightmap units per texel
//    (TerrainUtils.cs:139-157, built at :154-155), so |n.xz| / n.y overstates grade by
//    scale.y / scale.x (≈ 56× at the default height scale). Never used here.
//  - Area triangles are NOT terrain-conformed between nodes: GeometrySystem triangulates the
//    node positions (Game.Areas/GeometrySystem.cs:101-113) and keeps terrain only as
//    Triangle.m_HeightRange, so a triangle plane is the node-to-node average SPEC §6.1 rules
//    out. Grade therefore comes from height differences on a lattice. An open polyline with
//    >= 3 nodes is triangulated as if closed (GeometrySystem.cs:99-112), hence ring mode below.
//
// TWO PHASES.
//  Plan(points, count, latticeOrigin, step, grid): sanitize (NaN/Inf -> NonFinite; step <= 0,
//    NaN or > 100 m -> BadStep; strip consecutive duplicates and a duplicated closing vertex,
//    xz within 1 mm; < 2 points -> TooFewPoints). Ring mode when >= 3 points and |shoelace|
//    >= 1 m², else polyline mode. Lattice index of a world x = floor((x - origin.x) / step);
//    bbox padded by Dilation + 1 (low side) / Dilation + 2 (high side); > 1 << 21 bbox cells
//    -> TooLarge. Ring: even-odd scanline per lattice row in double -> Mask (cell CENTRE =
//    lattice node inside), with the crossings bucketed by row (each edge visits only the rows
//    it spans: O(rows + vertices + crossings), R2 fixer round; more crossings than MaxBboxCells
//    -> TooLarge); Need = Mask dilated by Dilation (Chebyshev), because the 5×5 box gradient
//    reads ±3 nodes and the bilinear lookups read the +1 neighbour. Polyline: Need = nodes
//    within Dilation (Chebyshev) of the rounded node of every walk sample (spacing <= step / 2)
//    along the segments. Both sets are also recorded as exact per-row RUN LISTS (SlopeGrid
//    MaskRuns / NeedRuns, R2 fixer round) that every later pass walks. No inside cell ->
//    NoCells. Plan returns Ok in BOTH modes (grid.PolylineMode tells them apart) and writes NaN
//    into every Need height, so a node the caller forgets to fill surfaces as NonFinite instead
//    of rating garbage.
//  CALLER: for every q with Need[q] != 0: Heights[q] = SampleHeight at world
//    (Origin.x + (I0 + i)·Step, Origin.y + (J0 + j)·Step), q = j·Nx + i — walking
//    grid.NeedRuns (internal; the mod's feedback system and SlopeBatch do) visits exactly those.
//  Classify(grid, result): Heights null/short -> BadHeights; any Need height non-finite ->
//    NonFinite. Ring mode, per Mask cell p:
//      g      = (Σ of the 25 central differences in the 5×5 box) / (50·step)  (17.5 m scale at
//               3.5 m; reads unmasked Need heights, so a cell's value never depends on where
//               the polygon edge is — superset polygons keep every old window bit-identical)
//      d̂      = -normalize(g), fallback (1, 0) when |g| < 1e-6
//      window = |H(p - 10 d̂) - H(p + 10 d̂)| / 20, bilinear on the grid, VALID only when the
//               rounded node at every check point (spacing <= 1 cell) along the segment is a
//               Mask cell. ABSOLUTE drop on purpose: on bowls, saddles and washboards the box
//               direction can point uphill relative to the 20 m chord.
//      overlay value o (CellGrade; CellBand = BandOf(o)) = the full window when valid, else
//               the longest centred window from {15, 10, 7} m (never shorter than 2 steps)
//               that fits, else |g|.
//    SteepestGrade (R1's rating, unchanged) = the k-th largest FULL window, k =
//    max(MinRatingCells, ceil(RatingAreaM2 / step²)) — an ABSOLUTE area, never a fraction of
//    the run (a fraction dilutes with length). Fewer than k full windows -> ShortRun,
//    SteepestGrade = k-th (or last) largest overlay value. RatingGrade = the crux (WHAT THE
//    RATING IS above). Also MeanGrade (mean |g|), Drop (max - min height over inside cells),
//    FallLine = normalize(-Σg) ((0, 0) when Σg vanishes), FallCoherence = |Σg| / Σ|g|,
//    MeanGradient = |Σg| / n (internal; the R2b end-zone key gate), DiagonalRule (the grid's),
//    BandShare (per-cell bands, sums to 1), Area = inside cells·step², ValidWindows, CellBand
//    (BandOutside outside the Mask), CellGrade (only Mask cells meaningful), a copy of the
//    grid geometry for the overlay, and (R3) Length / LengthRect / AxisLength (RUN LENGTH above;
//    polyline mode: Length = arc length).
//    Polyline mode: SegmentGrade[i] = |H(b) - H(a)| / |ab|xz with SegmentBand[i]; windows
//    |H(s) - H(s + 20)| / 20 over arc length at ds = step / 2; rating = k-th largest with
//    k = max(PolylineMinWindows = 11, ceil(8 / ds)) (spec §3 as amended; see the constant) ->
//    Status Polyline; fewer than k windows (length < 20 m + 10·ds) -> ShortRun with the last
//    largest; total length < 20 m -> endpoint grade over the full length, ShortRun.
//    MeanGrade = length-weighted mean segment grade, BandShare = length share per segment
//    band, Drop over the arc samples; FallLine, FallCoherence and Area stay 0; CellBand is
//    BandOutside everywhere and CellGrade is not meaningful (no Mask in polyline mode).
//
// BANDS (§9.2): Flat < 6 % <= Green < 25 % <= Blue < 40 % <= Black < 60 % <= Double black.
// Half-open, band = number of thresholds <= grade (exactly 25 % is Blue). Grades are rise/run
// fractions (0.25 = 25 %), not degrees. The R1 values 6 / 15 / 25 / 40 were one class harsher
// than resort convention (DECISIONS 116); 60 % for double black is our choice.
//
// KNOWN AND DELIBERATE (R1, still true): the ShortRun -> Ok transition as a small run grows
// can LOWER SteepestGrade (shorter windows read steeper). Values carry up to +0.3 %
// height-quantisation bias on planes (SlopeBatch planes: rating - g in [-0.12 %, +0.26 %]),
// hence DisplayHysteresis for the live readout.
//
// MinRatingCells (7) and PolylineMinWindows (11) are the smallest values that keep a ±3 m
// single-texel spike to "same band, |Δ| <= 1 %" (run-feedback-spec.md §3 invariant (7), (11))
// in the SlopeBatch sweeps; the original 5 and 3 do not (numbers at the constants). They now
// define SteepestGrade and the polyline rating. The crux needs no such count: a spike raises
// a few cells the easiest line goes around (SlopeBatch R2-f, 21,600 cases on 15.5 / 32.5 /
// 50 % planes: worst |Δ| 0.10 % at m = 1, 0.25 % at m = 2 with R2b's rules (R2 0.30 %), no
// band flip).
//
// LATTICE RESOLUTION: a valid window's endpoint may lie up to half a cell past the last inside
// node (rounded mask check), so its bilinear can read ONE node beyond the polygon: terrain
// within one step outside the edge leaks into SteepestGrade (SlopeBatch: a 60 % cliff starting
// exactly at the boundary of a 10.5 % run reads up to +4.7 %; the crux avoids those edge cells,
// +0.03 %; the stricter endpoint rules tried are listed in Window). And a coarse step blurs
// narrow features: StepMultiple budgets by AREA (CellBudget 16384, decided 2026-09-30), so
// only a run over 200,704 m² gets m = 2: longer than 2,007 m at 100 m wide (2,509 m at 80 m,
// 3,345 m at 60 m; 6,690 m at 30 m, 10,035 m at 20 m). There a 12 m bank's SteepestGrade
// reads up to 2.66 % low (spec §3 (8) holds at m = 1 only: WARN SPEC-GAP lines on 3,400 × 60,
// 2,600 × 80 and 3,000 × 100) and its crux up to 2.95 % low with R2b's diagonal rule (8.1 %
// in R2; MEASURED LIMITS above).
//
// COST (R2 fixer round): Plan is O(rows + vertices + crossings) for the scanline (edges
// bucketed by row) plus O(Need) for the run lists; every later pass — the per-cell loop, the
// crux pass, the caller's fill and strips — walks the exact per-row RUN LISTS, so managed work is
// O(inside) / O(Need) whatever the shape, plus O(bbox) memsets (Plan's Array.Clear of Mask and
// Need, FillOutside of CellBand; memset speed). Before this round every pass walked each row's
// first..last span, which bridges the outside gap of a row crossing two arms of a concave run
// (span = 37× the inside cells for a 15 m × r1500 C, 163× for a U with 10 m arms 4 km apart:
// 2.5-5 ms on CoreCLR, reviewer-measured, over the budget). The crux pass is O(n) (O(n + k log k)
// in the open last bucket): two passes over the Mask runs, the union-find (near-constant finds),
// a BFS; its per-cell state lives in a bbox-sized int buffer (Uf, 4·(Nx·Nz + 2) bytes; up to
// 8 MB at MaxBboxCells, retained), initialised over the Mask runs (dilated by one cell) only.
// Its cost is memory latency (bucket order visits cells at random), hence one union per run of
// active neighbours (8-neighbour path) and zone bits packed into the bucket-ordered entries;
// R2b's BFS reads the zone bits from its Unvisited states instead of re-binning each cell. The
// key gate is two compares per classification (the coherence clause of the R2b fixer round
// reads FallCoherence, already computed). The
// fall-line key adds an O(runs) range pass and one multiply-add per cell in passes 1 and 2; the
// diagonal rule (m >= 2) unions per allowed neighbour and reads CellBand for an active diagonal.
// The bbox is bounded only by MaxBboxCells, not CellBudget: a 2-node 5 km diagonal at m = 1 has
// ~1.04M bbox cells. SlopeBatch TIME, CoreCLR, mean plan + fill + classify, ranges over six
// full runs of the R2b round (budget 1.5 ms, §9.4): 2.5 km × 80 m at m = 1 (~16k inside cells,
// the costliest convex m = 1 run) 1.14-1.46 ms, crux 0.39-0.48 ms = 37-42 % of classify (R2:
// 1.15-1.85 ms, crux 0.40-0.67 ms, 38-56 %); 2 km × 80 m 0.91-1.19 ms, crux 34-42 %; 2.6 km ×
// 80 m at m = 2 (diagonal rule) 0.24-0.40 ms, crux 0.071-0.116 ms = 32-44 % (R2's 8-neighbour
// path on the same grids 0.058-0.136 ms); 5 km diagonal / sliver 0.20-0.41 ms; concave, all
// m = 1: C 15 m × r1500 330° (662 vertices) 1.19-1.61 ms, U with 10 / 30 / 60 m arms
// 0.87-1.42 / 1.11-2.22 / 1.08-1.26 ms (C and U 30 m over 1.5 ms in one of six runs each:
// single-run noise, as R2's thin margins); a 250 % cliff 400 × 100 (every cell in the open
// bucket) 0.29-0.33 ms, and the stress case 2.5 km × 80 m all at 250 % 1.85-2.06 ms (the heap;
// over budget, not a realistic run). Interleaved A/B against R2's file on the same grids
// (scratch, 3 × 200 reps): crux pass at m = 1 5-12 % FASTER (no re-binning in the BFS), at
// m = 2 within ±4 % (the per-neighbour path costs no more than R2's on these runs). The game's
// Mono ran R1's recompute at ~5-8× CoreCLR (DECISIONS 116); R2 / R2b under Mono is unmeasured.
//
// Cross-runtime bit equality (CoreCLR test host vs the game's Mono) is unverified; within a
// runtime a replay is bit-identical (proven in SlopeBatch).

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public enum SlopeBand : byte
    {
        Flat = 0,
        Green = 1,
        Blue = 2,
        Black = 3,
        DoubleBlack = 4,
    }

    public enum SlopeStatus
    {
        Ok,
        ShortRun,
        Polyline,
        TooFewPoints,
        NonFinite,
        NoCells,
        TooLarge,
        BadStep,
        BadHeights,
    }

    /// <summary>Lattice plan of one polygon. Reused by the caller; buffers grow, never shrink.
    /// After Plan: world x of node (i, j) = Origin.x + (I0 + i)·Step, z = Origin.y + (J0 + j)·Step,
    /// flat index j·Nx + i.</summary>
    public sealed class SlopeGrid
    {
        public int I0, J0, Nx, Nz;              // lattice index of cell (0,0) and extent
        public float Step;
        public double2 Origin;
        public byte[] Mask;                     // 1 = cell centre inside the polygon (ring mode)
        public byte[] Need;                     // 1 = the caller must fill Heights for this node
        public float[] Heights;                 // caller-filled, length >= Nx * Nz, index j * Nx + i
        public bool PolylineMode;
        public int InsideCells, NeedCells;
        public float3[] Points;                 // sanitized input copy (polyline mode reads it)
        public int PointCount;

        /// <summary>R2b (run-feedback-spec.md §9.8) connectivity of the crux pass, set by the
        /// CALLER (the classifier does not know the step multiple): true when the caller's step
        /// multiple is >= 2. false (default) = 8-neighbour connectivity, bit-identical to R2;
        /// true = the four orthogonal neighbours plus a diagonal neighbour only when BOTH cells
        /// orthogonally shared between the two are outside the Mask. Plan leaves it untouched;
        /// Classify copies it into SlopeResult.DiagonalRule.</summary>
        public bool DiagonalRule;

        // Classifier-private state (not part of the shared contract).
        internal SlopeStatus PlanStatus = SlopeStatus.NoCells;
        internal double[] LocalU, LocalV;       // sanitized points in LOCAL lattice units (node (0,0) = 0)
        internal double[] Crossings;            // scanline scratch: every row's crossings, bucketed by row
        internal int[] CrossStart, CrossFill;   // scanline scratch: per-row offsets into Crossings / fill cursors
        internal double[] ArcLength;            // polyline cumulative arc length, metres

        // Per-row RUN LISTS (R2 fixer round; replace R1's per-row first/last spans, which bridged
        // the outside gap of every row that crosses two arms of a concave ring). Row j's runs are
        // the inclusive column pairs (Runs[2k], Runs[2k + 1]) for k in [RowStart[j], RowStart[j + 1]),
        // RowStart has Nz + 1 entries. Runs are EXACT, sorted, disjoint and non-adjacent: every
        // cell of a Mask run is a Mask cell and every Mask cell lies in exactly one Mask run (same
        // for Need). Ring mode has Mask and Need runs; polyline mode has Need runs only (no Mask
        // run). Consumers (Classify, SkiRunDrawFeedbackSystem's fill / strips / capture, SlopeBatch)
        // walk these, so managed work is O(Mask) / O(Need), never O(bbox) — the bbox-sized
        // memsets (Plan's Array.Clear of Mask and Need, Classify's FillOutside) excepted.
        internal int[] MaskRuns, MaskRowStart;
        internal int[] NeedRuns, NeedRowStart;
        internal int MaskRunCount, NeedRunCount;
        internal int[] NeedLo, NeedHi;          // polyline-mode marking scratch only: per row first/last Need column
        internal int[] MergeHead;               // ring-mode Need merge scratch (2·Dilation + 1 row cursors)
    }

    public sealed class SlopeResult
    {
        public SlopeStatus Status;
        public SlopeBand Band;                  // BandOf(RatingGrade)
        public float RatingGrade, MeanGrade, Drop, MinHeight, MaxHeight, Area, FallCoherence;
        public float2 FallLine;
        public readonly float[] BandShare = new float[5];
        public int ValidWindows;
        public byte[] CellBand;                 // per cell, SkiSlopeClassifier.BandOutside (255) outside the mask
        public int I0, J0, Nx, Nz;              // grid geometry copy for the overlay
        public float Step;
        public double2 Origin;
        public float[] SegmentGrade;            // polyline mode
        public byte[] SegmentBand;
        public int SegmentCount;

        // R2 (run-feedback-spec.md §9.3). Ring mode: RatingGrade = the crux of the easiest line.
        public float[] CellGrade;               // per cell overlay value o, index j·Nx + i; only Mask cells are meaningful
        public float SteepestGrade;             // R1 statistic: k-th largest full 20 m window (ShortRun: k-th/last overlay value)
        public float P90Grade;                  // smallest bucket top with >= 90 % of inside cells at or below it
        public float CruxHeight;                // node height of the crux cell (valid when CruxValid)
        public bool Connected;                  // a top-zone -> bottom-zone path exists over Mask cells (DiagonalRule's connectivity)
        public bool CruxValid;                  // ring mode and Connected: CruxI/CruxJ name the crux cell
        public int CruxI, CruxJ;                // grid-local index of the crux cell
        public int TopZoneCells, BottomZoneCells;

        // R2b (run-feedback-spec.md §9.8).
        public bool ZoneByFallLine;             // ring mode: end zones ranked along the mean fall line (else by node height, as R2)
        public bool DiagonalRule;               // copy of SlopeGrid.DiagonalRule this classification used

        // R3 (run-binding-glade-spec.md §5.1, decision D14: HORIZONTAL map metres). Geometry only:
        // computed in Classify from the sanitized points and the end-zone key range, never read by
        // the rating (every rating output is bit-identical to R2b; SlopeBatch fingerprint).
        public float Length;                    // m; ring: max(LengthRect, AxisLength); polyline: arc length
        public float LengthRect;                // m; ring: (P + sqrt(max(0, P² − 16A))) / 4 over the sanitized ring (closing edge included); polyline 0
        public float AxisLength;                // m; ring: ZoneByFallLine ? fall-line key range of the inside cells + step : 0; polyline 0

        // Classifier-private: |Σg| / n, the mean box gradient's magnitude (ring mode) — the
        // end-zone key gate's input, kept so CruxForTest re-decides the key exactly as Classify.
        internal float MeanGradient;

        // Classifier-private top-k insertion buffers (descending), grown to k.
        internal float[] TopFull, TopCell;

        // Classifier-private crux-pass buffers (grow, never shrink): union-find / BFS state per
        // flat cell index plus the two virtual nodes TOP = Nx·Nz and BOTTOM = Nx·Nz + 1; inside
        // cells in bucket order; BFS queue; bucket bounds (RatingBuckets + 1); end-zone key
        // histogram; the neighbour offset tables of the current grid (see Crux).
        internal int[] Uf, Order, Queue, BucketBound, HeightHist, Nbr;
        internal float[] HeapKey;               // open last bucket: the heap's keys, parallel to its Order segment

        /// <summary>Resets every output (arrays keep their capacity; Nx = Nz = SegmentCount = 0
        /// marks their contents as void). A cleared result reads Status NoCells, never Ok.</summary>
        public void Clear()
        {
            Status = SlopeStatus.NoCells;
            Band = SlopeBand.Flat;
            RatingGrade = 0f;
            SteepestGrade = 0f;
            P90Grade = 0f;
            CruxHeight = 0f;
            Connected = false;
            CruxValid = false;
            CruxI = 0;
            CruxJ = 0;
            TopZoneCells = 0;
            BottomZoneCells = 0;
            ZoneByFallLine = false;
            DiagonalRule = false;
            Length = 0f;
            LengthRect = 0f;
            AxisLength = 0f;
            MeanGradient = 0f;
            MeanGrade = 0f;
            Drop = 0f;
            MinHeight = 0f;
            MaxHeight = 0f;
            Area = 0f;
            FallCoherence = 0f;
            FallLine = new float2(0f, 0f);
            for (int b = 0; b < BandShare.Length; b++)
                BandShare[b] = 0f;
            ValidWindows = 0;
            I0 = 0;
            J0 = 0;
            Nx = 0;
            Nz = 0;
            Step = 0f;
            Origin = new double2(0.0, 0.0);
            SegmentCount = 0;
        }
    }

    public static class SkiSlopeClassifier
    {
        public const byte BandOutside = 255;

        // SPEC §6.1 as amended by run-feedback-spec.md §9.2 (R2, DECISIONS 116): 6 / 25 / 40 / 60 %.
        // Half-open: band = number of thresholds <= grade. 60 % for double black is our choice
        // (no numeric convention exists); the R1 values 6 / 15 / 25 / 40 were one class too harsh.
        public const float GradeGreen = 0.06f, GradeBlue = 0.25f, GradeBlack = 0.40f, GradeDoubleBlack = 0.60f;
        public const int BaseValueFlat = 0, BaseValueGreen = 6, BaseValueBlue = 12, BaseValueBlack = 20, BaseValueDoubleBlack = 28;

        // Crux end zones (§9.3 as replaced by §9.8 "Round R2b"). Key: position along the mean fall
        // line (key = -(FallLine · (i, j))·step) when the mean box gradient |Σg| / n >=
        // EndZoneMinMeanGrade OR FallCoherence = |Σg| / Σ|g| >= EndZoneMinCoherence (the second
        // clause added in the R2b fixer round, see the header), else node height (R2). Size: z =
        // clamp(round(EndZoneShare·n), 1, max(1, cap)) inside cells at each end, cap =
        // floor(n·EndZoneDepthM / L) on the fall-line key (L = key range of the inside cells +
        // step: ~EndZoneDepthM of run at the run's MEAN width — deeper at an end narrower than
        // the mean, header limit (g)), floor(EndZoneMaxM2 / step²) (R2's area cap) on the height
        // key. Whole histogram bins of the key from each end. RatingBuckets = the counting-sort
        // resolution of the per-cell values: bucket = min(2047, (int)(o·1024)), i.e. 1/1024 =
        // 0.098 % grade wide up to 200 %.
        public const float EndZoneShare = 0.10f, EndZoneMaxM2 = 2000f;
        public const float EndZoneDepthM = 20f, EndZoneMinMeanGrade = 0.03f, EndZoneMinCoherence = 0.5f;
        public const int RatingBuckets = 2048;
        private const float BucketsPerUnit = 1024f;           // buckets per 1.0 of grade
        private const int HeightBins = 1024;                  // end-zone key histogram
        private const float OneBinRange = 1e-3f;              // m; key range below this -> one bin
        private const int Inactive = 0;                       // union-find state in Uf (see Crux)
        // BFS state in Uf (see Crux): Unvisited | UnvisitedTop / UnvisitedBottom = traversable, not
        // yet reached, in the top / bottom zone (the zone bits of its Order entry). Every value
        // below UnvisitedLimit is unvisited; Inactive, -1 (seed) and predecessors (> 0) are not.
        private const int Unvisited = int.MinValue, UnvisitedTop = 1, UnvisitedBottom = 2, UnvisitedLimit = int.MinValue + 4;
        private const int TopBit = 1 << 29, BottomBit = 1 << 30, CellMask = TopBit - 1;   // Order entries (see Crux)
        private const int ZoneShift = 29;                     // (entry >> ZoneShift) & 3 = UnvisitedTop | UnvisitedBottom bits

        public const float WindowLength = 20f, RatingAreaM2 = 60f, DisplayHysteresis = 0.005f;
        // MinRatingCells = 7, not the spec's original 5: measured in SlopeBatch (SPIKE lines; ±3 m single
        // texel on 10.5/20/32.5 % planes, 180 orientations × 2 offsets × 5 spots), worst shift at
        // m = 1: k=5 +1.96 % (315 of 10800 cases >= 1 %), k=6 +1.14 % (59), k=7 +0.40 % (0);
        // m = 2 stays <= 0.23 % for k = 5..7. A texel spike perturbs the 5×5 box direction of the
        // cells around it, so up to ~6 cells' windows reach it, not the 4 of the bilinear footprint.
        // CellBudget 16384 (was 4096; decided 2026-09-30, spec §3 "Step budget"): every run up to
        // 200,704 m² (e.g. 2.5 km × 80 m) rates at m = 1, where spec §3 (2) and (8) hold; at
        // 4096 every run over 50,176 m² fell to m = 2 and narrow banks rated up to 14.7 % low.
        public const int MinRatingCells = 7, CellBudget = 16384, Dilation = 3;

        private const int CellBudgetDown = 12288;             // StepMultiple hysteresis: back down only below this (3/4 of CellBudget)
        private const float MaxStep = 100f;                   // m; larger -> BadStep
        private const long MaxBboxCells = 1L << 21;           // larger -> TooLarge
        private const long MaxCrossings = MaxBboxCells;       // scanline row crossings (8 B each, retained); more than bbox cells -> TooLarge (a ring zigzagging across thousands of rows)
        private const double MaxLatticeIndex = 1.0e9;         // |index| beyond this -> TooLarge (int safety)
        private const double DuplicateEpsSq = 1e-6;           // 1 mm in xz
        private const double MinRingArea = 1.0;               // m²
        private const float FlatGradient = 1e-6f;             // |g| below this -> fallback direction (1, 0)
        // Polyline k = max(PolylineMinWindows, ceil(8 m / ds)). 11, not the spec's original 3: windows are
        // sampled every half cell and a texel's bilinear support crossed diagonally spans up to
        // 2√2 cells, so a spike touches up to ~6 window starts AND ~6 window ends — and with the
        // absolute drop both ends raise a near-contour line. Measured (SlopeBatch SPIKE-POLY,
        // m = 1): min 5 -> worst +6.54 % with 202 band flips, 7 -> +3.32 %, 9 -> +0.89 %,
        // 11 -> +0.13 %.
        private const int PolylineMinWindows = 11;
        private const float PolylineShareM = 8f;
        private const int MaxStepMultiple = 1 << 20;

        /// <summary>Half-open band of a grade (rise/run fraction). NaN and negatives are Flat.</summary>
        public static SlopeBand BandOf(float grade)
        {
            int b = 0;
            if (grade >= GradeGreen)
                b++;
            if (grade >= GradeBlue)
                b++;
            if (grade >= GradeBlack)
                b++;
            if (grade >= GradeDoubleBlack)
                b++;
            return (SlopeBand)b;
        }

        /// <summary>Display rule of the tooltip's "Steepest patch (avoidable)" line (spec §9.6
        /// item 4 as amended in the R2 fixer round); kept here, pure and Entities-free, so the
        /// net8 test host can sweep it (SlopeBatch TooltipLineTests). Shown only when ALL hold:
        /// (a) Connected — with a broken Mask the rating is the P90 fallback, not a crux, so no
        /// patch can be called "avoidable" relative to it; (b) both grades finite;
        /// (c) SteepestGrade >= RatingGrade + DisplayHysteresis (0.5 %) — on a uniform plane
        /// height quantisation alone puts SteepestGrade up to +0.29 % above the crux
        /// (reviewer sweep: 61-68 of 936 near-threshold plane cases fired without it);
        /// (d) the two rounded percentages differ (the tooltip shows round(100·g)), so the line
        /// never repeats line 2's number; (e) BandOf(SteepestGrade) is above the SHOWN band.
        /// Polyline mode never shows it (the feedback system publishes SteepestGrade ==
        /// RatingGrade there, which fails (c)).</summary>
        public static bool SteepestPatchShown(float ratingGrade, float steepestGrade, SlopeBand shownBand, bool connected)
        {
            if (!connected || !IsFinite(ratingGrade) || !IsFinite(steepestGrade))
                return false;
            if (!(steepestGrade >= ratingGrade + DisplayHysteresis))
                return false;
            if ((int)math.round(100f * steepestGrade) <= (int)math.round(100f * ratingGrade))
                return false;
            return (int)BandOf(steepestGrade) > (int)shownBand;
        }

        /// <summary>SPEC §6.1 base value of a band (docs/SPEC.md:235-241); unknown values -> 0.</summary>
        public static int BaseValue(SlopeBand band)
        {
            switch (band)
            {
                case SlopeBand.Green:
                    return BaseValueGreen;
                case SlopeBand.Blue:
                    return BaseValueBlue;
                case SlopeBand.Black:
                    return BaseValueBlack;
                case SlopeBand.DoubleBlack:
                    return BaseValueDoubleBlack;
                default:
                    return BaseValueFlat;
            }
        }

        /// <summary>Step multiple m (step = m·texel) for a polygon of the given shoelace area.
        /// Without a current value (current &lt; 1): the smallest m >= 1 with area/(m·texel)² <=
        /// CellBudget. With one: go UP (to that smallest m) only when the current m would exceed
        /// CellBudget estimated cells; go DOWN only to an m whose estimate is below CellBudgetDown
        /// (12288). Never
        /// sized from the bbox. Invalid area or texel -> current (or 1).</summary>
        public static int StepMultiple(double areaM2, float texel, int current)
        {
            int keep = current >= 1 ? current : 1;
            if (!(texel > 0f) || float.IsInfinity(texel) || !(areaM2 >= 0.0) || double.IsInfinity(areaM2))
                return keep;
            int up = SmallestMultiple(areaM2, texel, CellBudget, false);
            if (current < 1)
                return up;
            if (EstimatedCells(areaM2, texel, current) > CellBudget)
                return up;
            int down = SmallestMultiple(areaM2, texel, CellBudgetDown, true);
            return down < current ? down : current;
        }

        private static double EstimatedCells(double areaM2, float texel, int m)
        {
            double s = (double)m * texel;
            return areaM2 / (s * s);
        }

        private static int SmallestMultiple(double areaM2, float texel, int budget, bool strict)
        {
            double guess = math.ceil(math.sqrt(areaM2 / budget) / texel);
            int m = guess < 1.0 ? 1 : guess > MaxStepMultiple ? MaxStepMultiple : (int)guess;
            // Correct float edge effects of the closed form in both directions.
            while (m > 1 && Fits(EstimatedCells(areaM2, texel, m - 1), budget, strict))
                m--;
            while (m < MaxStepMultiple && !Fits(EstimatedCells(areaM2, texel, m), budget, strict))
                m++;
            return m;
        }

        private static bool Fits(double cells, int budget, bool strict)
        {
            return strict ? cells < budget : cells <= budget;
        }

        // ================================================================== Plan

        public static SlopeStatus Plan(float3[] points, int count, double2 latticeOrigin, float step, SlopeGrid grid)
        {
            if (grid == null)
                return SlopeStatus.NoCells; // never throw; nothing to plan into
            ResetGrid(grid, latticeOrigin, step);
            SlopeStatus status = PlanCore(points, count, latticeOrigin, step, grid);
            if (status != SlopeStatus.Ok)
                ResetGrid(grid, latticeOrigin, step);
            grid.PlanStatus = status;
            return status;
        }

        private static void ResetGrid(SlopeGrid grid, double2 origin, float step)
        {
            grid.I0 = 0;
            grid.J0 = 0;
            grid.Nx = 0;
            grid.Nz = 0;
            grid.Step = step;
            grid.Origin = origin;
            grid.PolylineMode = false;
            grid.InsideCells = 0;
            grid.NeedCells = 0;
            grid.PointCount = 0;
            grid.MaskRunCount = 0;
            grid.NeedRunCount = 0;
        }

        private static SlopeStatus PlanCore(float3[] points, int count, double2 origin, float step, SlopeGrid grid)
        {
            if (points == null || count < 1)
                return SlopeStatus.TooFewPoints;
            int n = count < points.Length ? count : points.Length;
            for (int k = 0; k < n; k++)
            {
                float3 p = points[k];
                if (!IsFinite(p.x) || !IsFinite(p.y) || !IsFinite(p.z))
                    return SlopeStatus.NonFinite;
            }
            if (!(step > 0f) || step > MaxStep)
                return SlopeStatus.BadStep; // also NaN and +Inf
            if (!IsFinite(origin.x) || !IsFinite(origin.y))
                return SlopeStatus.NonFinite;

            // ---- sanitize into grid.Points
            Grow(ref grid.Points, n);
            float3[] pts = grid.Points;
            int m = 0;
            for (int k = 0; k < n; k++)
            {
                float3 p = points[k];
                if (m > 0 && DistSqXz(pts[m - 1], p) < DuplicateEpsSq)
                    continue;
                pts[m++] = p;
            }
            while (m > 1 && DistSqXz(pts[m - 1], pts[0]) < DuplicateEpsSq)
                m--;
            grid.PointCount = m;
            if (m < 2)
                return SlopeStatus.TooFewPoints;

            // ---- lattice coordinates (double; world-anchored)
            Grow(ref grid.LocalU, m);
            Grow(ref grid.LocalV, m);
            double[] u = grid.LocalU, v = grid.LocalV;
            double sd = step;
            double umin = double.MaxValue, umax = double.MinValue, vmin = double.MaxValue, vmax = double.MinValue;
            for (int k = 0; k < m; k++)
            {
                double uk = (pts[k].x - origin.x) / sd;
                double vk = (pts[k].z - origin.y) / sd;
                u[k] = uk;
                v[k] = vk;
                if (uk < umin) umin = uk;
                if (uk > umax) umax = uk;
                if (vk < vmin) vmin = vk;
                if (vk > vmax) vmax = vk;
            }
            if (!(math.abs(umin) < MaxLatticeIndex) || !(math.abs(umax) < MaxLatticeIndex) ||
                !(math.abs(vmin) < MaxLatticeIndex) || !(math.abs(vmax) < MaxLatticeIndex))
                return SlopeStatus.TooLarge;
            long i0 = (long)math.floor(umin) - Dilation - 1;
            long i1 = (long)math.floor(umax) + Dilation + 2;
            long j0 = (long)math.floor(vmin) - Dilation - 1;
            long j1 = (long)math.floor(vmax) + Dilation + 2;
            long nxl = i1 - i0 + 1, nzl = j1 - j0 + 1;
            if (nxl * nzl > MaxBboxCells)
                return SlopeStatus.TooLarge;

            // ---- ring or polyline (shoelace in lattice units, relative to point 0)
            bool ring = false;
            if (m >= 3)
            {
                double s2 = 0.0;
                for (int k = 0; k < m; k++)
                {
                    int l = k + 1 < m ? k + 1 : 0;
                    double ax = u[k] - u[0], az = v[k] - v[0], bx = u[l] - u[0], bz = v[l] - v[0];
                    s2 += ax * bz - bx * az;
                }
                ring = math.abs(0.5 * s2) * sd * sd >= MinRingArea;
            }

            int nx = (int)nxl, nz = (int)nzl, total = nx * nz;
            grid.I0 = (int)i0;
            grid.J0 = (int)j0;
            grid.Nx = nx;
            grid.Nz = nz;
            grid.PolylineMode = !ring;
            for (int k = 0; k < m; k++)
            {
                u[k] -= i0;
                v[k] -= j0;
            }

            Grow(ref grid.Mask, total);
            Grow(ref grid.Need, total);
            Grow(ref grid.Heights, total);
            Array.Clear(grid.Mask, 0, total);
            Array.Clear(grid.Need, 0, total);
            Grow(ref grid.MaskRowStart, nz + 1);
            Grow(ref grid.NeedRowStart, nz + 1);

            if (ring)
            {
                int inside = Rasterize(grid, m);
                if (inside < 0)
                    return SlopeStatus.TooLarge;   // more than MaxCrossings row crossings
                grid.InsideCells = inside;
                if (inside == 0)
                    return SlopeStatus.NoCells;
                BuildRingNeedRuns(grid);
            }
            else
            {
                int[] ms = grid.MaskRowStart;
                for (int j = 0; j <= nz; j++)
                    ms[j] = 0;   // polyline mode: no Mask run
                grid.MaskRunCount = 0;
                MarkPolylineNeed(grid, m);
            }

            // Count Need and poison every Need height: a node the caller forgets to fill reads
            // as NonFinite instead of rating garbage. The Need runs keep this O(Need), not
            // O(bbox) (and not O(row spans), which bridge the outside gap of concave rings).
            float[] h = grid.Heights;
            byte[] need = grid.Need;
            int[] runs = grid.NeedRuns, rs = grid.NeedRowStart;
            int needCount = 0;
            for (int j = 0; j < nz; j++)
            {
                int row = j * nx;
                for (int k = rs[j]; k < rs[j + 1]; k++)
                {
                    for (int i = runs[2 * k]; i <= runs[2 * k + 1]; i++)
                    {
                        need[row + i] = 1;
                        h[row + i] = float.NaN;
                    }
                    needCount += runs[2 * k + 1] - runs[2 * k] + 1;
                }
            }
            grid.NeedCells = needCount;
            return SlopeStatus.Ok;
        }

        /// <summary>Even-odd scanline per lattice row (local v = j exactly) in double; a node is
        /// inside when it lies in [x_2k, x_2k+1] of the sorted crossings. Records each row's
        /// inside runs into grid.MaskRuns (sorted crossings give ascending runs; two consecutive
        /// pairs can share or touch one node only when x_2k+1 and x_2k+2 meet at a node, and are
        /// then merged, so the runs are disjoint and non-adjacent). Returns the inside count, or
        /// -1 when the polygon has more than MaxCrossings row crossings (Plan: TooLarge).
        ///
        /// EDGE BUCKETING (R2 fixer round): R1 tested every edge against every row, O(rows ·
        /// vertices) — 0.42 ms of Plan for a 662-vertex C-shaped run on CoreCLR. Row j crosses
        /// edge (k, l) iff (v_k > j) != (v_l > j), i.e. iff min(v_k, v_l) <= j < max(v_k, v_l) for
        /// an integer j, so each edge visits exactly the rows ceil(min) .. ceil(max) - 1: a count
        /// pass, a prefix sum and a fill pass bucket every crossing by row in O(rows + vertices +
        /// crossings); each row's bucket is then insertion-sorted (few crossings per row; no Sort).
        /// The crossing x is the same expression as R1's, and a sorted list of equal values is
        /// the same list whatever the insertion order, so the Mask is bit-identical to R1's.</summary>
        private static int Rasterize(SlopeGrid grid, int m)
        {
            double[] u = grid.LocalU, v = grid.LocalV;
            int nx = grid.Nx, nz = grid.Nz;
            byte[] mask = grid.Mask;
            Grow(ref grid.CrossStart, nz + 1);
            Grow(ref grid.CrossFill, nz);
            int[] cs = grid.CrossStart, fill = grid.CrossFill;
            for (int j = 0; j <= nz; j++)
                cs[j] = 0;
            long totalCross = 0;
            for (int k = 0; k < m; k++)
            {
                int l = k + 1 < m ? k + 1 : 0;
                CrossedRows(v[k], v[l], nz, out int ja, out int jb);
                for (int j = ja; j <= jb; j++)
                    cs[j + 1]++;
                if (jb >= ja)
                    totalCross += jb - ja + 1;
            }
            if (totalCross > MaxCrossings)
                return -1;
            for (int j = 0; j < nz; j++)
            {
                cs[j + 1] += cs[j];
                fill[j] = cs[j];
            }
            Grow(ref grid.Crossings, (int)totalCross);
            double[] xs = grid.Crossings;
            for (int k = 0; k < m; k++)
            {
                int l = k + 1 < m ? k + 1 : 0;
                double va = v[k], vb = v[l];
                CrossedRows(va, vb, nz, out int ja, out int jb);
                for (int j = ja; j <= jb; j++)
                {
                    double row = j;
                    xs[fill[j]++] = u[k] + (row - va) * (u[l] - u[k]) / (vb - va);   // R1's expression
                }
            }

            int[] rowStart = grid.MaskRowStart;
            int inside = 0, runCount = 0;
            for (int j = 0; j < nz; j++)
            {
                rowStart[j] = runCount;
                int rowFirst = runCount;
                int c0 = cs[j], c = cs[j + 1] - c0;
                // insertion sort of this row's bucket (few crossings per row; no Sort)
                for (int s = c0 + 1; s < c0 + c; s++)
                {
                    double x = xs[s];
                    int p = s;
                    while (p > c0 && xs[p - 1] > x)
                    {
                        xs[p] = xs[p - 1];
                        p--;
                    }
                    xs[p] = x;
                }
                int rowBase = j * nx;
                for (int k = c0; k + 1 < c0 + c; k += 2)
                {
                    double a = math.ceil(xs[k]), b = math.floor(xs[k + 1]);
                    int ia = a < 0.0 ? 0 : a > nx - 1 ? nx : (int)a;
                    int ib = b > nx - 1 ? nx - 1 : b < 0.0 ? -1 : (int)b;
                    if (ia > ib)
                        continue;
                    for (int i = ia; i <= ib; i++)
                    {
                        int q = rowBase + i;
                        if (mask[q] == 0)
                        {
                            mask[q] = 1;
                            inside++;
                        }
                    }
                    if (runCount > rowFirst && ia <= grid.MaskRuns[2 * runCount - 1] + 1)
                    {
                        if (ib > grid.MaskRuns[2 * runCount - 1])
                            grid.MaskRuns[2 * runCount - 1] = ib;   // shares / touches the previous run
                    }
                    else
                    {
                        GrowKeep(ref grid.MaskRuns, 2 * runCount + 2);
                        grid.MaskRuns[2 * runCount] = ia;
                        grid.MaskRuns[2 * runCount + 1] = ib;
                        runCount++;
                    }
                }
            }
            rowStart[nz] = runCount;
            grid.MaskRunCount = runCount;
            return inside;
        }

        /// <summary>The lattice rows j in [0, nz - 1] an edge from local v = va to v = vb crosses
        /// under the half-open rule (va > j) != (vb > j): min <= j < max, i.e. j from
        /// ceil(min) to ceil(max) - 1 (exact for integer j). jb < ja = none (also a horizontal
        /// edge, va == vb).</summary>
        private static void CrossedRows(double va, double vb, int nz, out int ja, out int jb)
        {
            double lo = va < vb ? va : vb, hi = va < vb ? vb : va;
            double a = math.ceil(lo), b = math.ceil(hi) - 1.0;
            if (a < 0.0) a = 0.0;
            if (b > nz - 1) b = nz - 1;
            if (!(a <= b))
            {
                ja = 0;
                jb = -1;
                return;
            }
            ja = (int)a;
            jb = (int)b;
        }

        /// <summary>Ring-mode Need runs = the Mask dilated by Dilation (Chebyshev), row by row:
        /// row j's Need is the union over rows j - Dilation .. j + Dilation of their Mask runs
        /// widened by Dilation on each side (the Chebyshev dilation of a row run is exactly that
        /// rectangle). Each source row's runs are sorted, so a (2·Dilation + 1)-way merge by
        /// run start emits row j's union in ascending order, merging overlapping and adjacent
        /// intervals: O(7·runs) per row, no Sort, never O(bbox). Plan writes the Need bytes from
        /// these runs afterwards.</summary>
        private static void BuildRingNeedRuns(SlopeGrid grid)
        {
            int nx = grid.Nx, nz = grid.Nz;
            int[] mRuns = grid.MaskRuns, mStart = grid.MaskRowStart, nStart = grid.NeedRowStart;
            Grow(ref grid.MergeHead, 2 * Dilation + 1);
            int[] head = grid.MergeHead;
            int runCount = 0;
            for (int j = 0; j < nz; j++)
            {
                nStart[j] = runCount;
                int j0 = j - Dilation < 0 ? 0 : j - Dilation;
                int j1 = j + Dilation > nz - 1 ? nz - 1 : j + Dilation;
                for (int r = j0; r <= j1; r++)
                    head[r - j0] = mStart[r];
                bool open = false;
                int curA = 0, curB = 0;
                while (true)
                {
                    int best = -1, bestA = int.MaxValue;
                    for (int r = j0; r <= j1; r++)
                    {
                        int hk = head[r - j0];
                        if (hk < mStart[r + 1] && mRuns[2 * hk] < bestA)
                        {
                            bestA = mRuns[2 * hk];
                            best = r;
                        }
                    }
                    if (best < 0)
                        break;
                    int bestB = mRuns[2 * head[best - j0] + 1];
                    head[best - j0]++;
                    int a = bestA - Dilation, b = bestB + Dilation;
                    if (a < 0) a = 0;                 // never bites after Plan (bbox padded by Dilation + 1)
                    if (b > nx - 1) b = nx - 1;
                    if (open && a <= curB + 1)
                    {
                        if (b > curB)
                            curB = b;
                    }
                    else
                    {
                        if (open)
                            AppendRun(ref grid.NeedRuns, ref runCount, curA, curB);
                        curA = a;
                        curB = b;
                        open = true;
                    }
                }
                if (open)
                    AppendRun(ref grid.NeedRuns, ref runCount, curA, curB);
            }
            nStart[nz] = runCount;
            grid.NeedRunCount = runCount;
        }

        private static void AppendRun(ref int[] runs, ref int runCount, int a, int b)
        {
            GrowKeep(ref runs, 2 * runCount + 2);
            runs[2 * runCount] = a;
            runs[2 * runCount + 1] = b;
            runCount++;
        }

        /// <summary>Polyline Need: walk every segment at <= 0.5 cell spacing and mark the
        /// (2·Dilation+1)² box around each sample's rounded node. Any point on a segment is
        /// within 0.25 cell of a sample, so every bilinear read (floor, floor + 1) is covered.
        /// Then each row's exact Need runs are read back from its marked first..last span (a
        /// polyline crossing a row twice bridges the gap here, once, in Plan only).</summary>
        private static void MarkPolylineNeed(SlopeGrid grid, int m)
        {
            int nx = grid.Nx, nz = grid.Nz;
            Grow(ref grid.NeedLo, nz);
            Grow(ref grid.NeedHi, nz);
            int[] lo = grid.NeedLo, hi = grid.NeedHi;
            for (int j = 0; j < nz; j++)
            {
                lo[j] = int.MaxValue;
                hi[j] = -1;
            }
            double[] u = grid.LocalU, v = grid.LocalV;
            byte[] need = grid.Need;
            for (int k = 0; k + 1 < m; k++)
            {
                double du = u[k + 1] - u[k], dv = v[k + 1] - v[k];
                double len = math.sqrt(du * du + dv * dv);
                int samples = (int)math.ceil(len / 0.5);
                if (samples < 1)
                    samples = 1;
                for (int c = 0; c <= samples; c++)
                {
                    double t = (double)c / samples;
                    int ci = (int)math.floor(u[k] + du * t + 0.5);
                    int cj = (int)math.floor(v[k] + dv * t + 0.5);
                    int ja = cj - Dilation < 0 ? 0 : cj - Dilation;
                    int jb = cj + Dilation > nz - 1 ? nz - 1 : cj + Dilation;
                    int ia = ci - Dilation < 0 ? 0 : ci - Dilation;
                    int ib = ci + Dilation > nx - 1 ? nx - 1 : ci + Dilation;
                    if (ia > ib)
                        continue;
                    for (int j = ja; j <= jb; j++)
                    {
                        int r = j * nx;
                        for (int i = ia; i <= ib; i++)
                            need[r + i] = 1;
                        if (ia < lo[j]) lo[j] = ia;
                        if (ib > hi[j]) hi[j] = ib;
                    }
                }
            }
            int[] nStart = grid.NeedRowStart;
            int runCount = 0;
            for (int j = 0; j < nz; j++)
            {
                nStart[j] = runCount;
                int r = j * nx;
                int i = lo[j];
                while (i <= hi[j])
                {
                    if (need[r + i] == 0)
                    {
                        i++;
                        continue;
                    }
                    int a = i;
                    while (i + 1 <= hi[j] && need[r + i + 1] != 0)
                        i++;
                    AppendRun(ref grid.NeedRuns, ref runCount, a, i);
                    i++;
                }
            }
            nStart[nz] = runCount;
            grid.NeedRunCount = runCount;
        }

        // ================================================================== Classify

        public static SlopeStatus Classify(SlopeGrid grid, SlopeResult result)
        {
            return Classify(grid, result, EndZoneShare, EndZoneMaxM2, EndZoneDepthM, EndZoneMinMeanGrade, EndZoneMinCoherence);
        }

        /// <summary>Test-only overload (tests/SlopeBatch: R2-j sensitivity table, the curved-run
        /// report rows under the height key via endZoneMinMeanGrade = endZoneMinCoherence = +inf,
        /// the key-gate rows with the coherence clause off (+inf), the random oracle sweep; the
        /// host links this file, so `internal` is visible there). The mod calls the public
        /// overload, which passes the shipped constants.</summary>
        internal static SlopeStatus Classify(SlopeGrid grid, SlopeResult result, float endZoneShare, float endZoneMaxM2,
                                             float endZoneDepthM, float endZoneMinMeanGrade, float endZoneMinCoherence)
        {
            if (result == null)
                return SlopeStatus.NoCells; // never throw; nowhere to write
            result.Clear();
            SlopeStatus pre = Validate(grid);
            if (pre != SlopeStatus.Ok)
            {
                result.Status = pre;
                return pre;
            }

            result.DiagonalRule = grid.DiagonalRule;
            result.I0 = grid.I0;
            result.J0 = grid.J0;
            result.Nx = grid.Nx;
            result.Nz = grid.Nz;
            result.Step = grid.Step;
            result.Origin = grid.Origin;
            Grow(ref result.CellBand, grid.Nx * grid.Nz);
            Grow(ref result.CellGrade, grid.Nx * grid.Nz);

            SlopeStatus status = grid.PolylineMode
                ? ClassifyPolyline(grid, result)
                : ClassifyRing(grid, result, endZoneShare, endZoneMaxM2, endZoneDepthM, endZoneMinMeanGrade, endZoneMinCoherence);
            if (status != SlopeStatus.Ok && status != SlopeStatus.ShortRun && status != SlopeStatus.Polyline)
            {
                result.Clear();
            }
            else if (grid.PolylineMode)
            {
                // §9.3: a line has exactly one path, so its k-th window statistic IS its crux.
                result.SteepestGrade = result.RatingGrade;
                result.P90Grade = result.RatingGrade;
                result.Connected = true;
            }
            result.Status = status;
            return status;
        }

        /// <summary>Test-only: re-runs the crux pass alone on a result that a ring-mode Classify
        /// of the SAME grid just produced (SlopeBatch times the crux share of Classify with it).
        /// Idempotent with the parameters of that Classify: it recomputes exactly what Classify
        /// wrote (the end-zone key gate reads the MeanGradient, FallCoherence and FallLine Classify
        /// stored). Returns false (and touches nothing) when the pair does not look like that.</summary>
        internal static bool CruxForTest(SlopeGrid grid, SlopeResult result, float endZoneShare, float endZoneMaxM2,
                                         float endZoneDepthM, float endZoneMinMeanGrade, float endZoneMinCoherence)
        {
            if (grid == null || result == null || grid.PolylineMode || Validate(grid) != SlopeStatus.Ok)
                return false;
            if (result.Status != SlopeStatus.Ok && result.Status != SlopeStatus.ShortRun)
                return false;
            int total = grid.Nx * grid.Nz;
            if (result.Nx != grid.Nx || result.Nz != grid.Nz || result.CellBand == null || result.CellBand.Length < total ||
                result.CellGrade == null || result.CellGrade.Length < total)
                return false;
            result.DiagonalRule = grid.DiagonalRule;
            Crux(grid, result, endZoneShare, endZoneMaxM2, endZoneDepthM, endZoneMinMeanGrade, endZoneMinCoherence);
            result.Band = BandOf(result.RatingGrade);
            return true;
        }

        private static SlopeStatus Validate(SlopeGrid grid)
        {
            if (grid == null)
                return SlopeStatus.NoCells;
            if (grid.PlanStatus != SlopeStatus.Ok)
                return grid.PlanStatus;
            if (!(grid.Step > 0f) || grid.Step > MaxStep)
                return SlopeStatus.BadStep;
            int nx = grid.Nx, nz = grid.Nz;
            // A planned grid is always >= 2·Dilation + 3 wide; anything smaller was tampered with.
            if (nx < 2 * Dilation + 3 || nz < 2 * Dilation + 3 || (long)nx * nz > MaxBboxCells)
                return SlopeStatus.NoCells;
            int total = nx * nz;
            if (grid.Mask == null || grid.Mask.Length < total || grid.Need == null || grid.Need.Length < total)
                return SlopeStatus.NoCells;
            if (grid.PolylineMode && (grid.Points == null || grid.LocalU == null || grid.LocalV == null ||
                                      grid.PointCount < 2 || grid.PointCount > grid.Points.Length ||
                                      grid.PointCount > grid.LocalU.Length || grid.PointCount > grid.LocalV.Length))
                return SlopeStatus.TooFewPoints;
            if (!RunsValid(grid.MaskRuns, grid.MaskRowStart, nx, nz) || !RunsValid(grid.NeedRuns, grid.NeedRowStart, nx, nz))
                return SlopeStatus.NoCells;
            float[] h = grid.Heights;
            if (h == null || h.Length < total)
                return SlopeStatus.BadHeights;
            byte[] need = grid.Need;
            int[] runs = grid.NeedRuns, rs = grid.NeedRowStart;
            for (int j = 0; j < nz; j++)
            {
                int row = j * nx;
                for (int k = rs[j]; k < rs[j + 1]; k++)
                {
                    for (int i = runs[2 * k]; i <= runs[2 * k + 1]; i++)
                    {
                        if (need[row + i] != 0 && !IsFinite(h[row + i]))
                            return SlopeStatus.NonFinite;
                    }
                }
            }
            return SlopeStatus.Ok;
        }

        /// <summary>Run-list structure check (a tampered grid must not index out of range or
        /// visit a cell twice): RowStart has Nz + 1 non-decreasing entries from 0 within Runs,
        /// every run lies in [0, Nx - 1] with start <= end, and a row's runs are strictly
        /// ascending and disjoint. O(Nz + runs).</summary>
        private static bool RunsValid(int[] runs, int[] rowStart, int nx, int nz)
        {
            if (rowStart == null || rowStart.Length < nz + 1 || rowStart[0] != 0)
                return false;
            int count = rowStart[nz];
            if (count < 0 || (count > 0 && (runs == null || runs.Length < 2L * count)))
                return false;
            for (int j = 0; j < nz; j++)
            {
                int k0 = rowStart[j], k1 = rowStart[j + 1];
                if (k0 > k1 || k1 > count)
                    return false;
                int prevB = -2;
                for (int k = k0; k < k1; k++)
                {
                    int a = runs[2 * k], b = runs[2 * k + 1];
                    if (a <= prevB || a > b || a < 0 || b > nx - 1)
                        return false;
                    prevB = b;
                }
            }
            return true;
        }

        /// <summary>k for ring mode: max(MinRatingCells, ceil(RatingAreaM2 / step²)).</summary>
        private static int RingK(float step)
        {
            double k = math.ceil((double)RatingAreaM2 / ((double)step * step));
            int ki = k > 1.0e6 ? 1000000 : (int)k;
            return ki > MinRatingCells ? ki : MinRatingCells;
        }

        private static SlopeStatus ClassifyRing(SlopeGrid grid, SlopeResult result, float endZoneShare, float endZoneMaxM2,
                                                float endZoneDepthM, float endZoneMinMeanGrade, float endZoneMinCoherence)
        {
            int nx = grid.Nx, nz = grid.Nz;
            float s = grid.Step;
            float[] h = grid.Heights;
            byte[] mask = grid.Mask;
            byte[] cellBand = result.CellBand;
            float[] cellGrade = result.CellGrade;

            int k = RingK(s);
            Grow(ref result.TopFull, k);
            Grow(ref result.TopCell, k);
            float[] topFull = result.TopFull, topCell = result.TopCell;
            int topFullN = 0, topCellN = 0;

            float halfFull = 0.5f * WindowLength / s;                 // cells
            int checksFull = Checks(WindowLength, s);
            // Shorter centred windows (overlay only), longest first, never shorter than 2 steps.
            float l1 = 15f, l2 = 10f, l3 = 7f;
            bool use1 = l1 >= 2f * s, use2 = l2 >= 2f * s, use3 = l3 >= 2f * s;
            float half1 = 0.5f * l1 / s, half2 = 0.5f * l2 / s, half3 = 0.5f * l3 / s;
            int checks1 = Checks(l1, s), checks2 = Checks(l2, s), checks3 = Checks(l3, s);

            float inv50 = 1f / (50f * s);
            int nx2 = 2 * nx, nx3 = 3 * nx;
            double sumAbs = 0.0, sumGx = 0.0, sumGz = 0.0;
            float hmin = float.MaxValue, hmax = float.MinValue;
            int n = 0, valid = 0, c0 = 0, c1 = 0, c2 = 0, c3 = 0, c4 = 0;

            FillOutside(cellBand, nx * nz);

            // Plan never puts a Mask cell within Dilation of the border; the clamps below only
            // make a tampered grid unable to index out of range (such cells stay BandOutside).
            // Walks the Mask RUNS (Validate checked their structure): O(inside), not O(row span).
            int[] mRuns = grid.MaskRuns, mStart = grid.MaskRowStart;
            for (int j = Dilation; j < nz - Dilation; j++)
            {
                int row = j * nx;
                for (int rk = mStart[j]; rk < mStart[j + 1]; rk++)
                {
                    int lo = mRuns[2 * rk] < Dilation ? Dilation : mRuns[2 * rk];
                    int hi = mRuns[2 * rk + 1] > nx - 1 - Dilation ? nx - 1 - Dilation : mRuns[2 * rk + 1];
                    for (int i = lo; i <= hi; i++)
                    {
                        int q = row + i;
                        if (mask[q] == 0)
                            continue;
                        n++;
                        float hq = h[q];
                        if (hq < hmin) hmin = hq;
                        if (hq > hmax) hmax = hq;

                        // 5×5 box of central differences, telescoped per row / column.
                        float ax = 0f, az = 0f;
                        for (int b = -2; b <= 2; b++)
                        {
                            int r = q + b * nx;
                            ax += h[r + 3] + h[r + 2] - h[r - 2] - h[r - 3];
                            int c = q + b;
                            az += h[c + nx3] + h[c + nx2] - h[c - nx2] - h[c - nx3];
                        }
                        float gx = ax * inv50, gz = az * inv50;
                        float gm = math.sqrt(gx * gx + gz * gz);
                        sumAbs += gm;
                        sumGx += gx;
                        sumGz += gz;
                        float dx = 1f, dz = 0f;
                        if (gm >= FlatGradient)
                        {
                            dx = -gx / gm;
                            dz = -gz / gm;
                        }

                        float o = Window(h, mask, nx, nz, i, j, dx, dz, halfFull, checksFull, WindowLength);
                        if (o >= 0f)
                        {
                            valid++;
                            Push(topFull, ref topFullN, k, o);
                        }
                        else
                        {
                            if (use1)
                                o = Window(h, mask, nx, nz, i, j, dx, dz, half1, checks1, l1);
                            if (o < 0f && use2)
                                o = Window(h, mask, nx, nz, i, j, dx, dz, half2, checks2, l2);
                            if (o < 0f && use3)
                                o = Window(h, mask, nx, nz, i, j, dx, dz, half3, checks3, l3);
                            if (o < 0f)
                                o = gm;
                        }
                        Push(topCell, ref topCellN, k, o);
                        cellGrade[q] = o;
                        SlopeBand band = BandOf(o);
                        cellBand[q] = (byte)band;   // 0..4: also the crux pass's "processed inside cell" marker
                        switch (band)
                        {
                            case SlopeBand.Flat: c0++; break;
                            case SlopeBand.Green: c1++; break;
                            case SlopeBand.Blue: c2++; break;
                            case SlopeBand.Black: c3++; break;
                            default: c4++; break;
                        }
                    }
                }
            }

            if (n == 0)
                return SlopeStatus.NoCells;

            // R1 statistic, kept as SteepestGrade ("the steepest patch, avoidable or not").
            SlopeStatus status;
            float steepest;
            if (valid >= k)
            {
                steepest = topFull[k - 1];
                status = SlopeStatus.Ok;
            }
            else
            {
                steepest = topCell[(k < n ? k : n) - 1];
                status = SlopeStatus.ShortRun;
            }

            result.SteepestGrade = steepest;
            result.ValidWindows = valid;
            result.MeanGrade = (float)(sumAbs / n);
            result.MinHeight = hmin;
            result.MaxHeight = hmax;
            result.Drop = hmax - hmin;
            result.Area = n * s * s;
            float inv = 1f / n;
            result.BandShare[0] = c0 * inv;
            result.BandShare[1] = c1 * inv;
            result.BandShare[2] = c2 * inv;
            result.BandShare[3] = c3 * inv;
            result.BandShare[4] = c4 * inv;
            double fx = -sumGx, fz = -sumGz;
            double fl = math.sqrt(fx * fx + fz * fz);
            if (fl > 1e-12)
            {
                result.FallLine = new float2((float)(fx / fl), (float)(fz / fl));
                double coh = sumAbs > 0.0 ? fl / sumAbs : 0.0;
                result.FallCoherence = (float)(coh > 1.0 ? 1.0 : coh);
            }
            result.MeanGradient = (float)(fl / n);   // |ḡ| = |Σg| / n, the R2b end-zone key gate (§9.8); NaN only for tampered heights

            // The crux (needs MinHeight / MaxHeight, CellGrade, CellBand, and the FINAL FallLine,
            // MeanGradient and FallCoherence: the end-zone key gate reads them, so they are set
            // above on every path).
            Crux(grid, result, endZoneShare, endZoneMaxM2, endZoneDepthM, endZoneMinMeanGrade, endZoneMinCoherence);
            result.Band = BandOf(result.RatingGrade);

            // R3 (run-binding-glade-spec.md §5.1): run length = max(equivalent rectangle, fall-line
            // extent). Reads only the sanitized points and AxisLength (set by Crux); writes only
            // the three length fields — no rating output depends on them.
            float rect = (float)EquivalentRectLength(grid.Points, grid.PointCount);
            result.LengthRect = rect;
            result.Length = rect > result.AxisLength ? rect : result.AxisLength;
            return status;
        }

        /// <summary>R3 (run-binding-glade-spec.md §5.1): the centreline length of the rectangle
        /// with the ring's perimeter P and area A, (P + sqrt(max(0, P² − 16A))) / 4, horizontal
        /// metres (xz only). P includes the closing edge explicitly (Plan stripped the duplicated
        /// closing vertex, so pts[m - 1] → pts[0] is an edge here); A = |shoelace| relative to
        /// point 0, in double. Exact for an L × W rectangle (P² − 16A = 4(L − W)², the root
        /// returns the longer side) and for any constant-width band whose area is centreline ×
        /// width and whose perimeter is 2·centreline + 2·width (mitred polygonal bends, circular
        /// arcs). Shapes far from a band clamp the root at 0 (a circle reads P / 4). Returns 0
        /// for fewer than 3 points, a short buffer (tampered grid) or a non-finite result.</summary>
        private static double EquivalentRectLength(float3[] pts, int m)
        {
            if (pts == null || m < 3 || m > pts.Length)
                return 0.0;
            double x0 = pts[0].x, z0 = pts[0].z;
            double per = 0.0, s2 = 0.0;
            for (int k = 0; k < m; k++)
            {
                int l = k + 1 < m ? k + 1 : 0;
                double ax = pts[k].x - x0, az = pts[k].z - z0, bx = pts[l].x - x0, bz = pts[l].z - z0;
                double dx = bx - ax, dz = bz - az;
                per += math.sqrt(dx * dx + dz * dz);
                s2 += ax * bz - bx * az;
            }
            double area = math.abs(0.5 * s2);
            double disc = per * per - 16.0 * area;
            if (!(disc > 0.0))
                disc = 0.0;
            double len = (per + math.sqrt(disc)) * 0.25;
            return IsFinite(len) ? len : 0.0;
        }

        // ---------------------------------------------------------------- crux (R2, R2b)

        /// <summary>RatingGrade = min over all connected Mask paths (8-neighbour, or the R2b
        /// diagonal rule when result.DiagonalRule) from the top end zone to the bottom end zone
        /// of the max per-cell value o along the path (§9.3 / §9.8), via a counting sort into
        /// RatingBuckets buckets and a union-find with TOP / BOTTOM virtual nodes; then a BFS for
        /// the crux cell. Also P90Grade, Connected, the zone sizes and ZoneByFallLine.
        ///
        /// END ZONES (R2b): the key is ZoneKey (position along the mean fall line) when the
        /// FallLine is non-zero and MeanGradient >= endZoneMinMeanGrade OR FallCoherence >=
        /// endZoneMinCoherence (the second clause: R2b fixer round), else the node height; the
        /// cap is DepthCap (fall-line key) or AreaCap (height key). See the file header.
        ///
        /// "Inside cell" here = a cell the per-cell loop processed, i.e. CellBand != BandOutside
        /// (CellBand is memset to BandOutside every Classify and the loop writes 0..4 exactly for
        /// the Mask cells of its clamped Mask runs). Every processed cell lies >= Dilation (3)
        /// from the grid border, so its 8 neighbours (and the diagonal rule's shared orthogonal
        /// cells, which are among them) are in range even on a tampered grid. Per-cell state is
        /// initialised over the clamped Mask RUNS dilated by one cell (O(inside + runs), never
        /// O(bbox) and never O(row span)), so every state this pass reads is fresh.
        ///
        /// Uf encoding (one int per flat index, Nx·Nz + 2 long). Pass 1 sets every cell of the
        /// dilated runs to Inactive (0); only inside cells are ever activated, so "Uf != 0" alone
        /// means "an active inside cell". 0 is never a parent pointer: parents are inside cells,
        /// which lie >= Dilation from the border (never flat index 0), or TOP / BOTTOM.
        /// Activation makes a cell a root; roots hold -size, other active cells their parent
        /// (> 0). After the union-find, the active cells (= every cell with o <= RatingGrade =
        /// the traversable cells, see the last bucket below) are reset to Unvisited ORed with
        /// their Order entry's zone bits (UnvisitedTop / UnvisitedBottom; R2b, so the BFS uses
        /// exactly the union-find's zones) and Uf becomes the BFS predecessor map (-1 = seed,
        /// > 0 = predecessor's flat index); everything else in the dilated runs is still
        /// Inactive, so "Uf < UnvisitedLimit" alone means "traversable and not yet reached".
        ///
        /// THE OPEN LAST BUCKET (R2 fixer round). Bucket 2047 holds every o >= 2047/1024
        /// (199.9 %), so "the largest o in the connecting bucket" would be the largest value of
        /// ANY cell above 199.9 %, avoidable or not (reviewer's case: a full-width 235 % step plus
        /// an avoidable 60 m pit rated 310.7 % with the crux in the pit). When the union-find
        /// reaches bucket 2047 unconnected, that bucket's cells are activated in ASCENDING o
        /// (an in-place binary min-heap over its Order segment, O(k log k) for its k cells, no
        /// Sort, deterministic) with a TOP ~ BOTTOM test after every cell: RatingGrade is then
        /// the EXACT minimax (the o of the connecting cell), and the traversable set is every
        /// cell with o <= RatingGrade (ties included). Below 200 % nothing changes: the
        /// traversable set "buckets <= connecting" IS "o <= RatingGrade" there.</summary>
        private static void Crux(SlopeGrid grid, SlopeResult result, float endZoneShare, float endZoneMaxM2,
                                 float endZoneDepthM, float endZoneMinMeanGrade, float endZoneMinCoherence)
        {
            int nx = grid.Nx, nz = grid.Nz, total = nx * nz;
            float[] h = grid.Heights;
            byte[] band = result.CellBand;
            float[] cg = result.CellGrade;
            int[] mRuns = grid.MaskRuns, mStart = grid.MaskRowStart;
            Grow(ref result.Uf, total + 2);
            Grow(ref result.BucketBound, RatingBuckets + 1);
            Grow(ref result.HeightHist, HeightBins);
            int[] uf = result.Uf, bound = result.BucketBound, hist = result.HeightHist;
            Array.Clear(bound, 0, RatingBuckets + 1);
            Array.Clear(hist, 0, HeightBins);

            // ---- end-zone key (R2b, §9.8; Crux summary "END ZONES"). The fall-line key is
            // computed inline per cell by ZoneKey (no per-cell buffer); it is linear in i along a
            // row and float rounding is monotone, so its range over the inside cells is the range
            // over the clamped Mask runs' END cells (O(runs)). The SAME ZoneKey / HeightBin call
            // feeds the histogram (pass 1) and the zone bits (pass 2); the BFS reads those bits.
            float s = grid.Step;
            float2 fall = result.FallLine;
            float flx = fall.x, flz = fall.y;
            // Gate (§9.8, coherence clause added in the R2b fixer round): a usable axis when the
            // mean box gradient is >= 3 % OR the box gradients agree (|Σg| / Σ|g| >= 0.5, a long
            // gentle run). NaN on either side compares false -> height.
            bool byFall = (flx != 0f || flz != 0f) &&
                          (result.MeanGradient >= endZoneMinMeanGrade || result.FallCoherence >= endZoneMinCoherence);
            result.ZoneByFallLine = byFall;
            float hmin, range;
            double keyLength = 0.0;
            if (byFall)
            {
                float kLo = float.MaxValue, kHi = float.MinValue;
                for (int j = Dilation; j < nz - Dilation; j++)
                {
                    for (int rk = mStart[j]; rk < mStart[j + 1]; rk++)
                    {
                        int lo = mRuns[2 * rk] < Dilation ? Dilation : mRuns[2 * rk];
                        int hi = mRuns[2 * rk + 1] > nx - 1 - Dilation ? nx - 1 - Dilation : mRuns[2 * rk + 1];
                        if (lo > hi)
                            continue;
                        float ka = ZoneKey(flx, flz, lo, j, s), kb = ZoneKey(flx, flz, hi, j, s);
                        if (ka < kLo) kLo = ka;
                        if (ka > kHi) kHi = ka;
                        if (kb < kLo) kLo = kb;
                        if (kb > kHi) kHi = kb;
                    }
                }
                hmin = kLo;
                range = kHi - kLo;
                keyLength = ((double)kHi - kLo) + s;   // L = key range of the inside cells + step (§9.8)
            }
            else
            {
                hmin = result.MinHeight;
                range = result.MaxHeight - result.MinHeight;
            }
            // R3 (run-binding-glade-spec.md §5.1): the run's extent along the mean fall line, the
            // same L as the depth cap (an output only; nothing below reads it). 0 on the height key.
            result.AxisLength = byFall ? (float)keyLength : 0f;
            int bins = range >= OneBinRange ? HeightBins : 1;   // also 1 for a NaN range (tampered heights)
            float binScale = bins > 1 ? HeightBins / range : 0f;

            // ---- pass 1 (Mask runs): union-find init, count, histogram. Uf is set to Inactive
            // over every Mask run DILATED BY ONE CELL (rows j - 1 .. j + 1, columns lo - 1 ..
            // hi + 1), so every 8-neighbour of an inside cell holds fresh state and the later
            // passes test Uf alone (no CellBand read per neighbour); O(3·inside + 6·runs).
            int n = 0;
            for (int j = Dilation; j < nz - Dilation; j++)
            {
                int row = j * nx;
                for (int rk = mStart[j]; rk < mStart[j + 1]; rk++)
                {
                    int lo = mRuns[2 * rk] < Dilation ? Dilation : mRuns[2 * rk];
                    int hi = mRuns[2 * rk + 1] > nx - 1 - Dilation ? nx - 1 - Dilation : mRuns[2 * rk + 1];
                    if (lo > hi)
                        continue; // a run entirely in the clamped border (tampered grids only)
                    for (int jj = row - nx; jj <= row + nx; jj += nx)
                        for (int i = lo - 1; i <= hi + 1; i++)
                            uf[jj + i] = Inactive;
                    for (int i = lo; i <= hi; i++)
                    {
                        int q = row + i;
                        if (band[q] == BandOutside)
                            continue;
                        n++;
                        bound[BucketOf(cg[q])]++;
                        hist[HeightBin(byFall ? ZoneKey(flx, flz, i, j, s) : h[q], hmin, binScale, bins)]++;
                    }
                }
            }
            if (n == 0)
                return; // cannot happen: ClassifyRing returns NoCells first

            // ---- bucket ends (exclusive prefix sums) and P90 in the same walk
            int run = 0, p90Bucket = RatingBuckets - 1;
            bool p90Found = false;
            long need90 = 9L * n;                                // 10·count >= 9·n  <=>  count >= 90 % of n
            for (int b = 0; b < RatingBuckets; b++)
            {
                run += bound[b];
                bound[b] = run;
                if (!p90Found && 10L * run >= need90)
                {
                    p90Bucket = b;
                    p90Found = true;
                }
            }
            bound[RatingBuckets] = run;
            result.P90Grade = (p90Bucket + 1) / BucketsPerUnit;   // exact in float; saturates at 2.0 (200 %)

            // ---- end zones: whole key bins from each end until >= z cells. Cap: the depth cap
            // floor(n·EndZoneDepthM / L) on the fall-line key, R2's area cap on the height key.
            int z = ZoneSize(n, endZoneShare, byFall ? DepthCap(n, endZoneDepthM, keyLength) : AreaCap(s, endZoneMaxM2));
            int topBin = bins, topCells = 0;
            while (topBin > 0 && topCells < z)
            {
                topBin--;
                topCells += hist[topBin];
            }
            int bottomBin = -1, bottomCells = 0;
            while (bottomBin < bins - 1 && bottomCells < z)
            {
                bottomBin++;
                bottomCells += hist[bottomBin];
            }
            result.TopZoneCells = topCells;
            result.BottomZoneCells = bottomCells;

            // ---- pass 2 (Mask runs): counting-sort placement; afterwards bound[b] = start of
            // bucket b and bound[b + 1] = its end (in-bucket order is irrelevant to the result
            // below bucket 2047; the last bucket is ordered by the heap below).
            // Each entry carries its cell's zone membership in bits 29 / 30 (flat indices are
            // < MaxBboxCells + 2 < 2^29): this sequential scan reads the heights cache-friendly,
            // the bucket-ordered union-find below would read them at random.
            Grow(ref result.Order, n);
            int[] order = result.Order;
            for (int j = Dilation; j < nz - Dilation; j++)
            {
                int row = j * nx;
                for (int rk = mStart[j]; rk < mStart[j + 1]; rk++)
                {
                    int lo = mRuns[2 * rk] < Dilation ? Dilation : mRuns[2 * rk];
                    int hi = mRuns[2 * rk + 1] > nx - 1 - Dilation ? nx - 1 - Dilation : mRuns[2 * rk + 1];
                    for (int i = lo; i <= hi; i++)
                    {
                        int q = row + i;
                        if (band[q] == BandOutside)
                            continue;
                        int zb = HeightBin(byFall ? ZoneKey(flx, flz, i, j, s) : h[q], hmin, binScale, bins);   // = pass 1's bin
                        int entry = q | (zb >= topBin ? TopBit : 0) | (zb <= bottomBin ? BottomBit : 0);
                        order[--bound[BucketOf(cg[q])]] = entry;
                    }
                }
            }

            // ---- union-find, bucket by bucket (ascending); stop at the first connecting bucket.
            // Activation turns Inactive into a size-1 root and joins the cell to its ACTIVE
            // inside neighbours under the grid's connectivity rule.
            //  * 8-neighbour (DiagonalRule false; R2's code path, unchanged): walking the
            //    neighbours as a cycle (NW N NE E SE S SW W), consecutive ones are 8-adjacent to
            //    each other, so by induction on activation order every run of consecutive active
            //    neighbours is already one set: one union per run gives exactly the partition of
            //    unioning with all of them (usually one union instead of up to eight; the
            //    union-find is most of this pass's cost).
            //  * DiagonalRule (R2b, step multiple >= 2): N W E S, plus a diagonal only when both
            //    cells it shares orthogonally with q are outside the Mask. Two orthogonal
            //    neighbours of q are NOT adjacent under this rule, so the run trick above does not
            //    hold: one union per allowed active neighbour.
            Grow(ref result.Nbr, 32);
            int[] nbr = result.Nbr;                 // [0..7] row-major (BFS), [8..15] cyclic (union-find), [16..31] DiagonalRule (below)
            nbr[0] = -nx - 1;
            nbr[1] = -nx;
            nbr[2] = -nx + 1;
            nbr[3] = -1;
            nbr[4] = 1;
            nbr[5] = nx - 1;
            nbr[6] = nx;
            nbr[7] = nx + 1;
            nbr[8] = -nx - 1;
            nbr[9] = -nx;
            nbr[10] = -nx + 1;
            nbr[11] = 1;
            nbr[12] = nx + 1;
            nbr[13] = nx;
            nbr[14] = nx - 1;
            nbr[15] = -1;
            // DiagonalRule: for row-major neighbour e, the two cells it shares orthogonally with q
            // (nbr[16 + e], nbr[24 + e]); 0 for the orthogonal neighbours (always allowed).
            for (int e = 16; e < 32; e++)
                nbr[e] = 0;
            nbr[16] = -nx; nbr[24] = -1;            // NW: N and W
            nbr[18] = -nx; nbr[26] = 1;             // NE: N and E
            nbr[21] = nx; nbr[29] = -1;             // SW: S and W
            nbr[23] = nx; nbr[31] = 1;              // SE: S and E
            bool diagonalRule = result.DiagonalRule;
            int top = total, bottom = total + 1;
            uf[top] = -1;
            uf[bottom] = -1;
            int connecting = -1;
            const int last = RatingBuckets - 1;
            int heapEnd = bound[RatingBuckets];     // last bucket only: its popped (activated or tied) cells end up in order[heapEnd .. n)
            float exactRating = float.NaN;
            float[] key = null;
            for (int b = 0; b < RatingBuckets && connecting < 0; b++)
            {
                int start = bound[b], end = bound[b + 1];
                if (start == end)
                    continue;
                bool exact = b == last;             // the open bucket [199.9 %, inf): ascending o, test per cell (Crux summary)
                if (exact)
                {
                    // Keys copied once into a contiguous array parallel to the segment, so the heap
                    // never reads CellGrade at random (halves the cost on a cliff face).
                    Grow(ref result.HeapKey, end - start);
                    key = result.HeapKey;
                    for (int k = start; k < end; k++)
                        key[k - start] = ValueKey(cg[order[k] & CellMask]);
                    HeapifyByKey(order, key, start, end);
                }
                for (int k = start; k < end; k++)
                {
                    int entry;
                    if (exact)
                    {
                        entry = PopMinByKey(order, key, start, heapEnd);
                        heapEnd--;
                    }
                    else
                    {
                        entry = order[k];
                    }
                    int q = entry & CellMask;
                    uf[q] = -1;
                    if (!diagonalRule)
                    {
                        bool prevActive = uf[q + nbr[15]] != Inactive;
                        bool linked = false;
                        for (int e = 8; e < 16; e++)
                        {
                            int r = q + nbr[e];
                            bool active = uf[r] != Inactive;
                            if (active && !prevActive)
                            {
                                Union(uf, q, r); // first cell of a run
                                linked = true;
                            }
                            prevActive = active;
                        }
                        if (!linked && prevActive)
                            Union(uf, q, q + nbr[8]); // all eight active: one run without a start
                    }
                    else
                    {
                        for (int e = 0; e < 8; e++)
                        {
                            int r = q + nbr[e];
                            if (uf[r] == Inactive)
                                continue;
                            int a = nbr[16 + e];
                            if (a != 0 && (band[q + a] != BandOutside || band[q + nbr[24 + e]] != BandOutside))
                                continue; // a diagonal past an inside orthogonal cell
                            Union(uf, q, r);
                        }
                    }
                    if ((entry & TopBit) != 0)
                        Union(uf, q, top);
                    if ((entry & BottomBit) != 0)
                        Union(uf, q, bottom);
                    if (exact && Find(uf, top) == Find(uf, bottom))
                    {
                        connecting = b;
                        exactRating = key[heapEnd - start];   // the popped cell's key (= ValueKey(o))
                        // Ties join the traversable set (o <= RatingGrade); they stay Inactive
                        // in the union-find, which is finished.
                        while (heapEnd > start && key[0] <= exactRating)
                        {
                            PopMinByKey(order, key, start, heapEnd);
                            heapEnd--;
                        }
                        break;
                    }
                }
                if (!exact && Find(uf, top) == Find(uf, bottom))
                    connecting = b;
            }

            if (connecting < 0)
            {
                // The Mask falls apart between the zones (self-intersecting or hair-thin polygon).
                result.Connected = false;
                result.RatingGrade = result.P90Grade;
                return;
            }

            float rating = -1f;
            if (connecting == last)
            {
                // Exact minimax (Crux summary, "the open last bucket").
                rating = exactRating < float.PositiveInfinity ? exactRating : last / BucketsPerUnit;   // +inf = NaN / non-finite values (tampered heights)
            }
            else
            {
                // Largest value in the connecting bucket: order-independent, and at most one
                // bucket width (1/1024) above the exact minimax (which lies in the same bucket).
                for (int k = bound[connecting]; k < bound[connecting + 1]; k++)
                {
                    float o = cg[order[k] & CellMask];
                    if (o > rating)
                        rating = o;
                }
            }
            if (!(rating >= 0f))
                rating = connecting / BucketsPerUnit; // only reachable with non-finite values (tampered heights)
            result.Connected = true;
            result.RatingGrade = rating;

            // ---- crux cell: BFS from the top-zone cells (row-major seed order) through the
            // traversable cells (o <= RatingGrade), neighbours expanded in row-major order
            // (nbr[0..7]: NW N NE W E SW S SE; under DiagonalRule a diagonal only where the
            // union-find above allows it), stopped when the first bottom-zone cell is
            // DISCOVERED (not dequeued), walked back. Below bucket 2047 the traversable cells
            // are order[0 .. bound[connecting + 1]); in the last bucket, order[0 .. bound[2047])
            // plus the popped segment order[heapEnd .. n). Each traversable cell's Unvisited
            // state carries its Order entry's zone bits, so seeds and the stop test read exactly
            // the zones the union-find used (no key recomputation here; R2 recomputed the height
            // bin, which is the same value).
            int traversable = connecting == last ? bound[last] : bound[connecting + 1];
            for (int k = 0; k < traversable; k++)
            {
                int entry = order[k];
                uf[entry & CellMask] = Unvisited | ((entry >> ZoneShift) & 3);
            }
            if (connecting == last)
            {
                for (int k = heapEnd; k < bound[RatingBuckets]; k++)
                {
                    int entry = order[k];
                    uf[entry & CellMask] = Unvisited | ((entry >> ZoneShift) & 3);
                }
            }
            Grow(ref result.Queue, n);
            int[] queue = result.Queue;
            int head = 0, tail = 0, found = -1;
            for (int j = Dilation; j < nz - Dilation && found < 0; j++)
            {
                int row = j * nx;
                for (int rk = mStart[j]; rk < mStart[j + 1] && found < 0; rk++)
                {
                    int lo = mRuns[2 * rk] < Dilation ? Dilation : mRuns[2 * rk];
                    int hi = mRuns[2 * rk + 1] > nx - 1 - Dilation ? nx - 1 - Dilation : mRuns[2 * rk + 1];
                    for (int i = lo; i <= hi; i++)
                    {
                        int q = row + i;
                        int u = uf[q];
                        if (u >= UnvisitedLimit || (u & UnvisitedTop) == 0)
                            continue; // not an inside cell, not traversable (Inactive), or not in the top zone
                        uf[q] = -1;
                        if ((u & UnvisitedBottom) != 0)
                        {
                            found = q; // the zones share a cell: a zero-length path, the crux is that cell
                            break;
                        }
                        queue[tail++] = q;
                    }
                }
            }
            while (head < tail && found < 0)
            {
                int q = queue[head++];
                for (int e = 0; e < 8; e++)   // row-major neighbour order
                {
                    int r = q + nbr[e];
                    int u = uf[r];
                    if (u >= UnvisitedLimit)
                        continue; // visited, outside or not traversable (both Inactive)
                    if (diagonalRule)
                    {
                        int a = nbr[16 + e];
                        if (a != 0 && (band[q + a] != BandOutside || band[q + nbr[24 + e]] != BandOutside))
                            continue; // the same diagonal rule as the union-find
                    }
                    uf[r] = q;
                    if ((u & UnvisitedBottom) != 0)
                    {
                        found = r;
                        break;
                    }
                    queue[tail++] = r;
                }
            }
            if (found < 0)
                return; // cannot happen once connected; CruxValid stays false

            // Walk bottom -> top; ">=" lets a later (nearer the top) cell win ties.
            int crux = found, cur = found, steps = 0;
            float cruxValue = cg[found];
            while (true)
            {
                if (cg[cur] >= cruxValue)
                {
                    cruxValue = cg[cur];
                    crux = cur;
                }
                int p = uf[cur];
                if (p < 0 || ++steps > n)
                    break;
                cur = p;
            }
            result.CruxValid = true;
            result.CruxJ = crux / nx;
            result.CruxI = crux - result.CruxJ * nx;
            result.CruxHeight = h[crux];
        }

        /// <summary>z = clamp(round(share·n), 1, max(1, cap)), never above n. Round half up, in
        /// double. Non-finite or negative parameters degrade to the clamps (the overload taking
        /// them is test-only). With cap = AreaCap this is R2's ZoneSize, operation for
        /// operation.</summary>
        private static int ZoneSize(int n, float share, double cap)
        {
            double raw = (double)share * n;
            double z = raw >= 0.0 ? math.floor(raw + 0.5) : 0.0;           // NaN -> 0
            if (!(cap >= 1.0))
                cap = 1.0;
            if (z > cap)
                z = cap;
            if (z < 1.0)
                z = 1.0;
            if (z > n)
                z = n;
            return (int)z;
        }

        /// <summary>R2's area cap (height key): floor(EndZoneMaxM2 / step²) cells.</summary>
        private static double AreaCap(float step, float maxM2)
        {
            return math.floor((double)maxM2 / ((double)step * step));
        }

        /// <summary>R2b depth cap (fall-line key, §9.8): floor(n·EndZoneDepthM / L) cells, L = the
        /// key range of the inside cells + step (> 0) — EndZoneDepthM of run at the run's MEAN
        /// width n·step² / L. It is a cell COUNT, so at an end narrower than the mean the zone
        /// reaches EndZoneDepthM × mean / end width along the fall line (header limit (g), R2b
        /// fixer round).</summary>
        private static double DepthCap(int n, float depthM, double keyLength)
        {
            return math.floor((double)n * depthM / keyLength);
        }

        /// <summary>R2b fall-line end-zone key of node (i, j) (§9.8): -(FallLine · (i, j))·step,
        /// "up the fall line" = larger key. Grid-local i, j (the key is only ranked, so the
        /// origin does not matter). The ONE expression for the key range, the histogram and the
        /// zone bits.</summary>
        private static float ZoneKey(float flx, float flz, int i, int j, float step)
        {
            return -(flx * i + flz * j) * step;
        }

        /// <summary>Counting-sort bucket of a per-cell value: min(2047, (int)(o·1024)); NaN,
        /// negative and >= 200 % go to the last bucket (§9.3 guard; cannot occur with finite
        /// heights below 200 %).</summary>
        private static int BucketOf(float o)
        {
            if (!(o >= 0f) || !(o < 2f))
                return RatingBuckets - 1;
            int b = (int)(o * BucketsPerUnit);
            return b < RatingBuckets ? b : RatingBuckets - 1;
        }

        /// <summary>Heap key of a per-cell value for the open last bucket: o itself, +inf for
        /// NaN or negative (cannot occur with finite heights), so such cells come last.</summary>
        private static float ValueKey(float o)
        {
            return o >= 0f ? o : float.PositiveInfinity;
        }

        /// <summary>In-place binary min-heap over the Order segment a[start .. end) keyed by
        /// key[0 .. end - start) (parallel arrays, swapped together). Deterministic for a given
        /// input order.</summary>
        private static void HeapifyByKey(int[] a, float[] key, int start, int end)
        {
            int count = end - start;
            for (int i = count / 2 - 1; i >= 0; i--)
                SiftDownByKey(a, key, start, count, i);
        }

        /// <summary>Removes the minimum of the heap a[start .. heapEnd): swaps it (and its key) to
        /// the heap's last slot a[heapEnd - 1] / key[heapEnd - 1 - start], restores the heap over
        /// the rest and returns it (the caller decrements heapEnd).</summary>
        private static int PopMinByKey(int[] a, float[] key, int start, int heapEnd)
        {
            int lastSlot = heapEnd - 1 - start;
            int min = a[start];
            float kmin = key[0];
            a[start] = a[heapEnd - 1];
            key[0] = key[lastSlot];
            a[heapEnd - 1] = min;
            key[lastSlot] = kmin;
            SiftDownByKey(a, key, start, lastSlot, 0);
            return min;
        }

        private static void SiftDownByKey(int[] a, float[] key, int start, int count, int i)
        {
            float ki = key[i];
            int ai = a[start + i];
            while (true)
            {
                int l = 2 * i + 1;
                if (l >= count)
                    break;
                int c = l;
                float kc = key[l];
                int r = l + 1;
                if (r < count && key[r] < kc)
                {
                    c = r;
                    kc = key[r];
                }
                if (!(kc < ki))
                    break;
                key[i] = kc;
                a[start + i] = a[start + c];
                i = c;
            }
            key[i] = ki;
            a[start + i] = ai;
        }

        /// <summary>End-zone key bin: (int)((key - min key) · (1024 / key range)) in float, clamped
        /// to [0, bins - 1]; always 0 with one bin. NaN -> 0. The key is the node height (R2,
        /// min key = MinHeight) or ZoneKey (R2b fall-line key).</summary>
        private static int HeightBin(float hv, float hmin, float scale, int bins)
        {
            if (bins <= 1)
                return 0;
            float t = (hv - hmin) * scale;
            if (!(t >= 1f))
                return 0;
            return t < bins ? (int)t : bins - 1;
        }

        /// <summary>Union-find root with path halving (Uf[x] < 0 = root).</summary>
        private static int Find(int[] uf, int x)
        {
            while (true)
            {
                int p = uf[x];
                if (p < 0)
                    return x;
                int gp = uf[p];
                if (gp < 0)
                    return p;
                uf[x] = gp;
                x = gp;
            }
        }

        /// <summary>Union by size (sizes stored negated in the roots).</summary>
        private static void Union(int[] uf, int a, int b)
        {
            int ra = Find(uf, a), rb = Find(uf, b);
            if (ra == rb)
                return;
            if (uf[ra] > uf[rb])
            {
                int t = ra;
                ra = rb;
                rb = t;
            }
            uf[ra] += uf[rb];
            uf[rb] = ra;
        }

        private static int Checks(float lengthM, float step)
        {
            int c = (int)math.ceil(lengthM / step);
            return c < 2 ? 2 : c;
        }

        /// <summary>Centred window through node (i, j) along (dx, dz), half-length `half` cells:
        /// |H(p - half·d) - H(p + half·d)| / lengthM, or -1 when any of the checks + 1 evenly
        /// spaced points (both endpoints included) rounds to a node outside the Mask. Every
        /// accepted endpoint lies within 0.5 cell of an interior Mask node, so the bilinear
        /// reads stay inside the Need set — but may read one node outside the Mask (header,
        /// LATTICE RESOLUTION). All positions are OFFSETS from the node, computed
        /// without (i, j), so a cell's value is bit-identical wherever the grid's local origin
        /// lies (a superset polygon or a panning bbox never perturbs an old window).</summary>
        private static float Window(float[] h, byte[] mask, int nx, int nz, int i, int j, float dx, float dz,
                                    float half, int checks, float lengthM)
        {
            float ox1 = dx * half, oz1 = dz * half;
            float ox0 = -ox1, oz0 = -oz1;
            float ddx = ox1 - ox0, ddz = oz1 - oz0;
            for (int c = 0; c <= checks; c++)
            {
                float px, pz;
                if (c == checks)
                {
                    px = ox1;
                    pz = oz1;
                }
                else
                {
                    float t = (float)c / checks;
                    px = ox0 + ddx * t;
                    pz = oz0 + ddz * t;
                }
                int ii = i + (int)math.floor(px + 0.5f);
                int jj = j + (int)math.floor(pz + 0.5f);
                if (ii < 1 || jj < 1 || ii >= nx - 1 || jj >= nz - 1 || mask[jj * nx + ii] == 0)
                    return -1f;
            }
            // Rounded endpoint check on purpose: the bilinear can read ONE node past the last
            // inside node (header, LATTICE RESOLUTION). Stricter endpoint rules were measured in
            // SlopeBatch (fixer round 2026-09-30) and each broke a spec §3 invariant: four inside
            // corners -> (10) 30x15 ShortRun in 12 of 14 cases, (9) ridge m=2 26.07 % vs 27.73 %;
            // renormalised inside-corner weights -> (1) planes up to +2.46 % at m = 2; pull the end
            // inward <= 1 cell -> (4) runout -0.11 %, (10) 2 cases, classify +70 % (worst 1.6 ms).
            float a = BilinearAt(h, nx, i, j, ox0, oz0);
            float b = BilinearAt(h, nx, i, j, ox1, oz1);
            return math.abs(a - b) / lengthM;
        }

        /// <summary>Bilinear over the four nodes around node (i, j) + offset (ox, oz) — the shape
        /// of TerrainUtils.SampleHeightInternal (TerrainUtils.cs:121-137): lerp along x per row,
        /// then along z. Callers guarantee the four nodes are inside the grid.</summary>
        private static float BilinearAt(float[] h, int nx, int i, int j, float ox, float oz)
        {
            float fi = math.floor(ox), fj = math.floor(oz);
            float fx = ox - fi, fz = oz - fj;
            int q = (j + (int)fj) * nx + i + (int)fi;
            float a = h[q], b = h[q + 1], c = h[q + nx], d = h[q + nx + 1];
            float r0 = a + (b - a) * fx;
            float r1 = c + (d - c) * fx;
            return r0 + (r1 - r0) * fz;
        }

        /// <summary>Descending top-k insertion (ties keep the earlier value first).</summary>
        private static void Push(float[] top, ref int count, int k, float value)
        {
            if (count == k && !(value > top[k - 1]))
                return;
            int p = count < k ? count++ : k - 1;
            while (p > 0 && top[p - 1] < value)
            {
                top[p] = top[p - 1];
                p--;
            }
            top[p] = value;
        }

        // ---------------------------------------------------------------- polyline

        private static SlopeStatus ClassifyPolyline(SlopeGrid grid, SlopeResult result)
        {
            int nx = grid.Nx, nz = grid.Nz;
            int m = grid.PointCount;
            float s = grid.Step;
            float[] h = grid.Heights;
            double[] u = grid.LocalU, v = grid.LocalV;
            // No Mask in polyline mode: CellBand is BandOutside everywhere (no consumer reads it
            // here; the overlay draws segments). Memset-speed fill, not a managed per-cell pass.
            FillOutside(result.CellBand, nx * nz);

            int segs = m - 1;
            Grow(ref result.SegmentGrade, segs);
            Grow(ref result.SegmentBand, segs);
            Grow(ref grid.ArcLength, m);
            double[] arc = grid.ArcLength;
            arc[0] = 0.0;
            double sd = s;
            double weighted = 0.0;
            for (int k = 0; k < segs; k++)
            {
                double du = u[k + 1] - u[k], dv = v[k + 1] - v[k];
                double len = sd * math.sqrt(du * du + dv * dv);
                arc[k + 1] = arc[k] + len;
                float ha = BilinearClamped(h, nx, nz, u[k], v[k]);
                float hb = BilinearClamped(h, nx, nz, u[k + 1], v[k + 1]);
                float g = len > 0.0 ? (float)(math.abs((double)hb - ha) / len) : 0f;
                result.SegmentGrade[k] = g;
                SlopeBand band = BandOf(g);
                result.SegmentBand[k] = (byte)band;
                weighted += g * len;
                result.BandShare[(int)band] += (float)len;
            }
            result.SegmentCount = segs;
            double totalLen = arc[segs];
            if (!(totalLen > 0.0))
                return SlopeStatus.TooFewPoints; // cannot happen after sanitize (1 mm dedupe)
            // R3 (run-binding-glade-spec.md §5.1): a line's length is its horizontal arc length
            // (LengthRect / AxisLength stay 0: no ring, no fall-line key).
            result.Length = (float)totalLen;
            for (int b = 0; b < 5; b++)
                result.BandShare[b] = (float)(result.BandShare[b] / totalLen);
            result.MeanGrade = (float)(weighted / totalLen);

            // Heights along the arc at ds = step / 2 (plus the end point) for Min/Max/Drop.
            double ds = 0.5 * sd;
            float hmin = float.MaxValue, hmax = float.MinValue;
            int seg = 0;
            long samples = (long)math.floor(totalLen / ds);
            for (long c = 0; c <= samples + 1; c++)
            {
                double at = c <= samples ? c * ds : totalLen;
                float hv = HeightAt(grid, h, nx, nz, ref seg, at);
                if (hv < hmin) hmin = hv;
                if (hv > hmax) hmax = hv;
            }
            result.MinHeight = hmin;
            result.MaxHeight = hmax;
            result.Drop = hmax - hmin;

            float hStart = BilinearClamped(h, nx, nz, u[0], v[0]);
            float hEnd = BilinearClamped(h, nx, nz, u[m - 1], v[m - 1]);
            if (totalLen < WindowLength)
            {
                float g = (float)(math.abs((double)hEnd - hStart) / totalLen);
                result.RatingGrade = g;
                result.Band = BandOf(g);
                return SlopeStatus.ShortRun;
            }

            int k2 = (int)math.ceil(PolylineShareM / ds);
            if (k2 < PolylineMinWindows)
                k2 = PolylineMinWindows;
            Grow(ref result.TopFull, k2);
            float[] top = result.TopFull;
            int topN = 0;
            long count = (long)math.floor((totalLen - WindowLength) / ds + 1e-9) + 1;
            int segA = 0, segB = 0;
            for (long c = 0; c < count; c++)
            {
                double a = c * ds;
                double b = a + WindowLength;
                if (b > totalLen)
                    b = totalLen;
                float ha = HeightAt(grid, h, nx, nz, ref segA, a);
                float hb = HeightAt(grid, h, nx, nz, ref segB, b);
                Push(top, ref topN, k2, math.abs(ha - hb) / WindowLength);
            }
            int windows = count > int.MaxValue ? int.MaxValue : (int)count;
            result.ValidWindows = windows;
            float rating;
            SlopeStatus status;
            if (topN >= k2)
            {
                rating = top[k2 - 1];
                status = SlopeStatus.Polyline;
            }
            else
            {
                rating = top[topN - 1];
                status = SlopeStatus.ShortRun;
            }
            result.RatingGrade = rating;
            result.Band = BandOf(rating);
            return status;
        }

        /// <summary>Height at arc length `at` (metres) along the sanitized polyline; `seg` is a
        /// forward-only cursor (calls must be non-decreasing in `at`).</summary>
        private static float HeightAt(SlopeGrid grid, float[] h, int nx, int nz, ref int seg, double at)
        {
            double[] arc = grid.ArcLength, u = grid.LocalU, v = grid.LocalV;
            int last = grid.PointCount - 2;
            while (seg < last && arc[seg + 1] < at)
                seg++;
            double len = arc[seg + 1] - arc[seg];
            double t = len > 0.0 ? (at - arc[seg]) / len : 0.0;
            if (t < 0.0) t = 0.0;
            if (t > 1.0) t = 1.0;
            double x = u[seg] + (u[seg + 1] - u[seg]) * t;
            double z = v[seg] + (v[seg + 1] - v[seg]) * t;
            return BilinearClamped(h, nx, nz, x, z);
        }

        /// <summary>Bilinear with the cell index clamped into the grid — polyline points lie at
        /// least Dilation + 1 cells inside by construction, so the clamp never bites after Plan;
        /// it only keeps a tampered grid from indexing out of range.</summary>
        private static float BilinearClamped(float[] h, int nx, int nz, double x, double z)
        {
            float xf = (float)x, zf = (float)z;
            float fi = math.floor(xf), fj = math.floor(zf);
            if (fi < 0f) fi = 0f;
            if (fj < 0f) fj = 0f;
            if (fi > nx - 2) fi = nx - 2;
            if (fj > nz - 2) fj = nz - 2;
            float fx = xf - fi, fz = zf - fj;
            if (fx < 0f) fx = 0f;
            if (fx > 1f) fx = 1f;
            if (fz < 0f) fz = 0f;
            if (fz > 1f) fz = 1f;
            int q = (int)fj * nx + (int)fi;
            float a = h[q], b = h[q + 1], c = h[q + nx], d = h[q + nx + 1];
            float r0 = a + (b - a) * fx;
            float r1 = c + (d - c) * fx;
            return r0 + (r1 - r0) * fz;
        }

        // ================================================================== helpers

        /// <summary>a[0..n) = BandOutside by doubling Buffer.BlockCopy (memset-speed; net48 has
        /// no Array.Fill). The lattice is the polygon bbox (up to MaxBboxCells), so a managed
        /// per-byte loop here was an O(bbox) pass on every Classify.</summary>
        private static void FillOutside(byte[] a, int n)
        {
            if (a == null || n <= 0)
                return;
            if (n > a.Length)
                n = a.Length;
            int seed = n < 64 ? n : 64;
            for (int q = 0; q < seed; q++)
                a[q] = BandOutside;
            int done = seed;
            while (done < n)
            {
                int len = done < n - done ? done : n - done;
                Buffer.BlockCopy(a, 0, a, done, len);
                done += len;
            }
        }

        private static bool IsFinite(float f)
        {
            return !float.IsNaN(f) && !float.IsInfinity(f);
        }

        private static bool IsFinite(double d)
        {
            return !double.IsNaN(d) && !double.IsInfinity(d);
        }

        private static double DistSqXz(float3 a, float3 b)
        {
            double dx = (double)a.x - b.x, dz = (double)a.z - b.z;
            return dx * dx + dz * dz;
        }

        private static void Grow(ref float3[] a, int n)
        {
            if (a == null || a.Length < n)
                a = new float3[Capacity(a == null ? 0 : a.Length, n)];
        }

        private static void Grow(ref double[] a, int n)
        {
            if (a == null || a.Length < n)
                a = new double[Capacity(a == null ? 0 : a.Length, n)];
        }

        private static void Grow(ref float[] a, int n)
        {
            if (a == null || a.Length < n)
                a = new float[Capacity(a == null ? 0 : a.Length, n)];
        }

        private static void Grow(ref int[] a, int n)
        {
            if (a == null || a.Length < n)
                a = new int[Capacity(a == null ? 0 : a.Length, n)];
        }

        private static void Grow(ref byte[] a, int n)
        {
            if (a == null || a.Length < n)
                a = new byte[Capacity(a == null ? 0 : a.Length, n)];
        }

        /// <summary>Grow keeping the contents (the run lists are appended to while built).</summary>
        private static void GrowKeep(ref int[] a, int n)
        {
            if (a != null && a.Length >= n)
                return;
            int[] b = new int[Capacity(a == null ? 0 : a.Length, n)];
            if (a != null)
                Array.Copy(a, b, a.Length);
            a = b;
        }

        /// <summary>Geometric growth (×1.5, minimum 16) so a polygon that grows node by node
        /// while the player draws reallocates O(log n) times, not every recompute.</summary>
        private static int Capacity(int current, int needed)
        {
            long c = current + (current >> 1);
            if (c < 16)
                c = 16;
            if (c < needed)
                c = needed;
            return c > int.MaxValue ? int.MaxValue : (int)c;
        }
    }
}
