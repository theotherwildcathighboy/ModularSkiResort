Field captures for tests/SlopeBatch (run-feedback-spec.md §9.4 R2-k, format §9.5)
==================================================================================

Every *.skislope file in this folder is replayed on every SlopeBatch run:
  dotnet run --project tests\SlopeBatch\SlopeBatch.csproj -c Release   (from the ModularSkiResort root)
SlopeBatch re-plans the polygon from its nodes (with the captured step and lattice origin), fills
every Need node from the captured heights (matched by world lattice index), classifies (with the
R2b diagonal rule when the captured multiple is >= 2, as the game does), runs the
brute-force crux oracle on it, and prints one "INFO  capture <file>" line: area, drop, mean,
SteepestGrade, P90Grade, RatingGrade, band, the R1-rule band, and what the game rated it at
capture time. No files here = one INFO line, not a failure.

Where captures come from
------------------------
In game, the "Ski Runs" dev tab's "Dump to log" button writes
  <Application.persistentDataPath>/ModsData/SkiResortMod/captures/run-yyyyMMdd-HHmmss.skislope
and logs the full path. Copy the file into this folder and give it a descriptive name
(e.g. meadow-corner-bank.skislope).

Format (plain text, invariant culture, single spaces; floats "G9" and doubles "G17" since R2b,
run-feedback-spec.md §9.8 -- captures written by R2 builds use round-trip "R" and still load)
------------------------------------------------------------------------------------------------
  SKISLOPE 1
  step <float> texel <float> multiple <int>
  origin <double> <double>
  grid <I0> <J0> <Nx> <Nz>
  rated <RatingGrade> <SteepestGrade> <MeanGrade> <Drop> <Band>
  nodes <count>
  <x> <y> <z>                 (count lines: the polygon nodes, world metres)
  need <count>
  <i> <j> <height>            (count lines: every Need node of the plan, grid-local i j, metres)

Asserting a band: the "expect" line
-----------------------------------
Add one line, anywhere after the first line:
  expect <Band>
where <Band> is Flat, Green, Blue, Black or DoubleBlack (case-insensitive) -- the band the run
SHOULD have in your judgement. SlopeBatch then FAILs whenever the classifier rates it otherwise,
so the capture becomes a permanent regression fixture.

Notes
-----
- Blank lines and lines starting with # are ignored by the loader (an extension of the §9.5
  format, for notes such as who drew the run and why it should rate as expected). The game's
  writer never emits them.
- A capture FAILs (SlopeBatch CaptureProblem, R2 fixer round) when:
    * it does not parse;
    * its step is not exactly multiple x texel (the game's step is always m x texel);
    * re-planning its nodes does not return Ok (BadStep, NoCells, TooLarge, ...) -- the game
      writes captures of RATED polygons only, so this means Plan or the rasterizer changed
      since the capture was taken, or the file is corrupt;
    * the re-planned grid (I0 J0 Nx Nz) differs from its "grid" line;
    * the current Plan needs a height the capture does not contain (Plan changed, or the file
      was truncated);
    * the replay's status is not a rating (Ok, ShortRun or Polyline);
    * it has an "expect" line and the band differs.
  SlopeBatch's round-trip self-test writes one bad capture of each kind and checks that every
  one is reported.
- The "rated" line records what the game said at capture time; it is printed for comparison,
  never asserted (CoreCLR vs the game's Mono bit equality is unverified, and the classifier may
  have changed since).
- Run length: the INFO line also prints the
  run's horizontal Length with its two parts (rect = equivalent rectangle, axis = fall-line
  extent). The eight 2026-10-01 captures are REGRESSION fixtures of that Length (±0.1 %) and their
  LengthRect is cross-checked against the spec's skeptic values (±1 m) — the table is
  CaptureLengthCheck in Program.cs. A capture added later prints "no length regression value"
  as INFO (not a failure) until its value is recorded there.
