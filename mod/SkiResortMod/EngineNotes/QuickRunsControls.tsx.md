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

## Resort Planner v2 (stage A, task A7)
- Two knob rows after Kinds: **Lifts** (`-` value `+`: "Lifts: auto (N)" with N = the proposed lifts of the showing plan, "-" before a
  scan, else "Lifts: N"; the value steps Auto, 1 .. 9; `liftsKnob`) and **Way home** (one button that cycles Auto -> Green -> Blue,
  its label the current level; `home`). Both go through `quickRunsSet("lifts" | "home", n)`; C# clamps (`QuickRunsPlanRules.ClampLifts /
  ClampHome`) and re-plans (a knob in the Preview is a Reselect on the cached field; during a scan it cancels and scans again). Both wait
  (disabled) while a scan, a build or a tree job runs, like every other knob: that is the hub's "busy: scanning" state (the scanning line
  with its progress stays, the buttons gray).
- `PlanRows` (drawn between the Way home row and the tree filler, only in the Preview with pods or lifts): the plan line (QUICK_PLAN_LINE),
  the lifts-are-proposals note, the terrain's honest mix (QUICK_MIX_HONEST, whole percents; black includes double black), then one toggle
  row per lift (check box, the lift line "P1: Detachable, 1.3 km, 540 m up, serves 7 runs", its way-home verdict and its pod's enabled
  runs by color as the map's band markers with a number; a pinned lift reads "Existing lift"), then one row per pod WITHOUT a lift
  ("Pod C: Advanced, 3 runs, no lift planned"). A click on a row sends `quickRunsSet("lift" | "pod", key)` (the key is the plan's unsigned FNV
  key as a signed int; C# casts it back). A left-out row dims. The Runs view scrolls as a whole while the tool is open, so the plan may
  grow (up to 9 lifts and 12 pods) without a layout rule.
- The step lines gain the site verdict's detail line (QUICK_SITE_DETAIL: "Pods with a green way home: G of N. Largest bench: H ha", only
  with pods; the v1 `sitePercent` / `siteTier` line is unchanged), and the empty Region step the hint QUICK_AROUND_LIFT. The cluster lines
  (v1 "One lift here serves N runs") are not drawn when the plan has pods: the pod and lift rows replace them. Nothing of this draws with
  the tool closed (`QuickRunsRows` is not mounted) or in a v1-shaped preview (no plan fields set).
- Contract: `QuickRunsState` gained liftsKnob, home, pods, planLifts, plan (QuickRunsPod, QuickRunsLift, QuickRunsPlan in bindings.ts;
  rule R2). **HUB_CONTRACT is NOT bumped in this task**: the parent bumps it once at the merge to main (and re-runs
  `npm run contract:lock`). Until then check-bundle's R3 ("the contract changed but HUB_CONTRACT is still 21") fails by design; the task's
  gates were run with a local, uncommitted bump to 22 (bindings.ts, SkiResortHubUISystem.cs, harness/scenarios.js `base.contract`).
- Harness: scenario "Resort Planner plan, Ash Flat (runs, lifts, problems)" (4 pods, 2 proposals + 1 pinned lift, one lift off, Way home
  Green); shots `resort-planner-plan-1080` and `resort-planner-plan-1440-fs125` in test-hub-layout's SHOTS.
