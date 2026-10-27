# QuickRunsControls.tsx: the Quick runs tool in the hub

- `QuickRunsChip`: the Build strip's Runs tab, in the slot beside the Tree tool chip, ok panel only (`FooterInfo.quickRuns`; it reads
  the non-CORE `quickRuns` binding, so the contract-mismatch footer never draws it). Labeled "Resort Planner (experimental)" (player name since DECISIONS 443; code keeps QuickRuns*). While the tool
  is unavailable the chip is disabled (grayed) and its tooltip gives C#'s reason (`quickRuns.reason`, SENT_KEYS); a click on an
  available chip opens the tool (`quickRunsToggle`), a click on the lit chip closes it. The lift generator chip's pattern.
- `QuickRunsRows`: while the tool is open the Runs view draws these rows instead of the tree tool's (`TreeRows`): the tool button
  (closes), Scan / Build / Undo; the step's lines (region size against the cap, scan progress, totals and the mix against its target,
  the hint, the last build); the refusal status, the notice, every warning and every cluster line; Mix, Runs (density) + Shuffle, Kinds;
  then the tree filler: on / off, Plant, Undo trees, density (the glade levels), tree line (-50 / -10 / +10 / +50 m; "Above the slope"
  when none), the group chips and a species stepper (left past the first species = the whole group).
- Every knob is a button, so the owner never needs a hotkey to find anything; Enter and right click only repeat Scan / Build / back.
- Every action is `quickRunsSet(key, value)`; the UI sends ints and C# clamps. Knobs wait (disabled) while C# scans, builds or plants.
- Staged build and planting: "Building runs N / M" (QUICK_BUILDING_PROGRESS) above the building line, with Stop after Undo; "Planting
  trees N / M" (QUICK_PLANTING_PROGRESS) in place of the tree counts, with Stop in place of Plant (beside its progress). Stop sends
  `stop`. The Build and Plant tips and the building line say "Large areas can take a few seconds to a minute."
- Rows use `.quickRow` (the tree row's look with wrap, so a narrow panel or text scale 1.25 wraps chips instead of clipping). The Runs
  view may scroll (listAll), and does while the tool is open (test-hub-layout: "Quick runs open" +310 rem at 1080p, allowed).
- The harness scenario "Quick runs open, Ash Flat (runs, lifts, problems)" draws a preview with the filler on; "Quick runs building"
  (4 / 11) and "Quick runs planting" (2,400 / 9,000) draw the progress states. Shots `quick-runs-open-1080`, `quick-runs-building-1080`
  and `quick-runs-planting-1080` are in test-hub-layout's SHOTS list.
