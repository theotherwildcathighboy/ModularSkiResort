# QuickRunsControls.tsx: the Quick runs tool in the hub

- `QuickRunsChip`: the Build strip's Runs tab, in the slot beside the Tree tool chip, ok panel only (`FooterInfo.quickRuns`). It reads
  the non-CORE `quickRuns` binding, so the contract-mismatch footer never draws it. The chip label is "Resort Planner (experimental)"
  (player name since DECISIONS 443). The code keeps QuickRuns*. While the tool is unavailable, the chip is disabled (grayed) and its
  tooltip gives C#'s reason (`quickRuns.reason`, SENT_KEYS). A click on an available chip opens the tool (`quickRunsToggle`). A click on
  the lit chip closes it. The pattern is the lift generator chip's pattern.
- `QuickRunsRows`: while the tool is open, the Runs view draws these rows instead of the tree tool's (`TreeRows`). In order:
  1. The tool button (closes), Scan / Build / Undo.
  2. The step's lines: region size against the cap, scan progress, totals and the mix against its target, the hint, the last build.
  3. The refusal status, the notice, every warning and every cluster line.
  4. Mix, Runs (density) + Shuffle, Kinds.
  5. The tree filler: on / off, Plant, Undo trees, density (the glade levels), tree line (-50 / -10 / +10 / +50 m, "Above the slope"
     when none), the group chips and a species stepper (left past the first species = the whole group).
- Every knob is a button, so the owner never needs a hotkey to find anything. Enter and right click only repeat Scan / Build / back.
- Every action is `quickRunsSet(key, value)`. The UI sends ints, and C# clamps them. Knobs wait (disabled) while C# scans, builds or plants.
- Staged build and planting:
  - "Building runs N / M" (QUICK_BUILDING_PROGRESS) shows above the building line, with Stop after Undo.
  - "Planting trees N / M" (QUICK_PLANTING_PROGRESS) shows in place of the tree counts, with Stop in place of Plant (beside its progress).
  - Stop sends `stop`.
  - The Build and Plant tips and the building line say "A large region can take a few seconds to a minute."
- Rows use `.quickRow`: the tree row's look with wrap. So a narrow panel or text scale 1.25 wraps chips instead of clipping. The Runs
  view may scroll (listAll), and it does while the tool is open (test-hub-layout: "Quick runs open" +310 rem at 1080p, allowed).
- The harness scenario "Quick runs open, Ash Flat (runs, lifts, problems)" draws a preview with the filler on. "Quick runs building"
  (4 / 11) and "Quick runs planting" (2,400 / 9,000) draw the progress states. Shots `quick-runs-open-1080`, `quick-runs-building-1080`
  and `quick-runs-planting-1080` are in test-hub-layout's SHOTS list.

## Resort Planner v2 (stage A, task A7)
- Two knob rows follow Kinds:
  - **Lifts** (`-` value `+`). The label is "Lifts: auto (N)", with N = the proposed lifts of the showing plan, or "-" before a scan.
    With a fixed value, the label is "Lifts: N". The value steps Auto, 1 .. 9 (`liftsKnob`).
  - **Way home**: one button that cycles Auto -> Green -> Blue. Its label is the current level (`home`).
- Both go through `quickRunsSet("lifts" | "home", n)`. C# clamps the value (`QuickRunsPlanRules.ClampLifts /
  ClampHome`) and re-plans. A knob in the Preview is a Reselect on the cached field. During a scan, the knob cancels the scan and scans
  again.
- Both wait (disabled) while a scan, a build or a tree job runs, like every other knob. This is the hub's "busy: scanning" state: the
  scanning line with its progress stays, and the buttons gray.
- `PlanRows` draw between the Way home row and the tree filler, only in the Preview with pods or lifts. In order:
  1. The plan line (QUICK_PLAN_LINE).
  2. The proposal note ("Build also builds each lift that is not grayed out") and, since R1-d, the lifts total line.
  3. The terrain's honest mix (QUICK_MIX_HONEST, whole percents). Black includes double black.
  4. One toggle row per lift: a check box, the lift line "P1: Detachable, 1.3 km, 540 m up, serves 7 runs", its way-home verdict, and its
     pod's enabled runs by color as the map's band markers with a number. A pinned lift reads "Existing lift".
  5. One row per pod WITHOUT a lift ("Pod C: Advanced, 3 runs, no lift planned").

  A click on a row sends `quickRunsSet("lift" | "pod", key)`. The key is the plan's unsigned FNV key as a signed int. C# casts it back.
  A left-out row dims. The Runs view scrolls as a whole while the tool is open. So the plan may grow (up to 9 lifts and 12 pods) without
  a layout rule.
- The step lines gain the site verdict's detail line (QUICK_SITE_DETAIL: "Pods with a green way home: G of N. Largest bench: H ha"). It
  shows only with pods. The v1 `sitePercent` / `siteTier` line is unchanged. The empty Region step gains the hint QUICK_AROUND_LIFT. The
  cluster lines (v1 "One lift here serves N runs") are not drawn when the plan has pods. The pod and lift rows replace them. None of this
  draws with the tool closed (`QuickRunsRows` is not mounted) or in a v1-shaped preview (no plan fields set).
- Contract: `QuickRunsState` gained liftsKnob, home, pods, planLifts, plan (QuickRunsPod, QuickRunsLift, QuickRunsPlan in bindings.ts,
  rule R2). **This task does NOT bump HUB_CONTRACT.** The parent bumps it once at the merge to main (and re-runs
  `npm run contract:lock`). Until then, check-bundle's R3 ("the contract changed but HUB_CONTRACT is still 21") fails by design. The
  task's gates ran with a local, uncommitted bump to 22 (bindings.ts, SkiResortHubUISystem.cs, harness/scenarios.js `base.contract`).
- Harness scenario "Resort Planner plan, Ash Flat (runs, lifts, problems)": 4 pods, 2 proposals + 1 pinned lift, one lift off, Way home
  Green. Shots `resort-planner-plan-1080` and `resort-planner-plan-1440-fs125` are in test-hub-layout's SHOTS.

## The plan sheet's lifts block (task R1-d, contract 41)
The planner proposes lifts and the engine builds them (StationPlaceToolSystem.md). The hub shows that work in one block. The block reads
`quickRuns.sheet`, so it does not depend on the preview. It stays after the run build and with the planner closed.

- Where it draws:
  - With the planner closed, `RunsView` draws `LiftSheetRows` at the top of the Runs view when `sheetShows(q)` is true. It sits above
    the rows of any other tool and a Divider separates them.
  - With the planner open, `QuickRunsRows` draws the same block (`LiftSheetBlock`) after the tree filler and before the plan list.
  - `sheetShows` is true when the sheet has rows and the phase is not Preview and not Scanning. A preview has its own lift rows. A new scan
    replaces the sheet, so old rows are hidden while it runs.
- In the preview only the lifts total shows (`LiftsTotalLine`, under the plan line). Reason: a Build lifts button in a preview would hand
  the tool to the station placer. That closes the planner and drops the preview. C# refuses the lift calls then too (`plannerBusy`: a phase other than
  Region, or planting). The UI disables the buttons in the same cases.
- Block, top to bottom:
  1. Build lifts (Stop while the queue runs, disabled once Stop was asked) and Clear list (disabled while the queue runs).
  2. "Building lift N / M" or "Stopping after lift N / M" while the queue runs.
  3. The notice of a refused call (`sheet.notice`, a locale key, it ends by itself after about 7 seconds).
  4. The lifts total: "N of M lifts to build, about COST" (N = switched on and not built, M = not built), or "All N lifts built".
  5. One row per lift (`SheetRow`): the lift line with its cost, the state line, and a Build button while the lift can be built.
- State line, in priority order: building now, waiting for its turn (queued), then by `state`:
  - Built. Not built yet (Proposed). Switched off in the plan (Proposed and off).
  - Stations placed, no line yet (StationsPlaced). With a reason: "Stations placed, no line: WHY".
  - Not built: WHY (Refused). Waiting in the lift tool: WHY (NeedsFitting).
  - WHY is `reasonKey`, a `QUICK_LIFT_WHY[group]` key in SENT_KEYS. C# maps the queue's reason keys to a group (`ResortPlanSheet.ReasonGroup`).
    An empty key on a Refused or Waiting row falls back to the GAME text. The warning color marks Refused, Waiting and a stopped row.
    The color is `.planRowSub.warnText` (R1-f): `.planRowSub` sets the dim color after `.warnText` with the same weight, so `.warnText` alone
    never showed on a sheet row (pass r1e note 3). The Waiting reasons have their own texts (PLAN_TRAIN: "one tower bends the rope more than its
    wheels can follow").
  - The progress line reads the run's total ("Building lift 2 / 3", "Stopping after lift 1 / 3"). It stays on screen while the queue opens the
    lift tool: `RunsTabFollow` (HubPanel.tsx) holds the Lifts view switch while `sheet.running` (`toolTabFollow(seen, key, hold)`).
- A row is not a toggle. The plan's on and off switches are the toggle rows in the preview (`PlanRows`). Here a left-out lift only dims.
- Every action is `quickLiftsAct(action, key)`: buildAll, build (key = the row key), stop, clear. The calls only queue keys in C#.
  C# answers a call it cannot take with a notice and a log line (ResortLiftBuildSystem.HubAct).
- Layout: `.sheetRow` is a flex row with the text block on the left and the button on the right. `.sheetList` has no height cap and no inner
  scroll. The Runs view scrolls as a whole (up to 15 lifts).
- Harness scenarios (names end with "Ash Flat (runs, lifts, problems)" so the layout test checks them):
  - "Resort Planner lifts, queue building (every row state)": planner closed, lift 2 / 3. It has 7 rows: built, building, queued,
    refused (the longest reason), waiting in the lift tool, stations placed after a closed tool, and switched off.
  - "Resort Planner lifts, queue stopping (planner open)": the same rows under the open planner, Stop asked.
  - "Resort Planner lifts, idle with a refusal and a notice": a money refusal and the RunsBusy notice.
  - "Resort Planner lifts, all built": the "All 2 lifts built" total.
  - "Resort Planner plan" (the preview) gained a sheet: the lifts total line shows.
- Shots in test-hub-layout's SHOTS list: `resort-planner-lifts-building-1080`, `resort-planner-lifts-stopping-1080`,
  `resort-planner-lifts-idle-1080`.
