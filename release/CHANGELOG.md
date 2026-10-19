# Changelog

Newest release first. Every release entry is a `## X.Y.Z (PDX version N, YYYY-MM-DD)` heading followed by the
three fixed lines, then the notes. `tests/SaveBatch` (SL-CL) checks them: the newest entry's "Layout versions" line
must equal `SaveLayouts.ChangelogLine` (mod/SkiResortMod/Systems/SaveLayouts.cs), and an entry whose layouts differ
from the previous entry's must say "Do not load in older versions: yes". Rules: docs/research/release-update-
constraints-2026-10-18.md section 3 (patch rules) and section 4 item 9; the test-from-old-save checklist is
docs/research/field/old-save-checklist.md (local docs).

- **Save compatible with previous**: yes = a save made on the previous release loads with nothing lost (checklist
  run); no = say what is lost and why. `n/a` only on the first release.
- **Layout versions**: copy `SaveLayouts.ChangelogLine` (SaveBatch prints a FAIL with the exact text if it differs).
- **Do not load in older versions**: yes whenever any layout version went up (players can pick an older version on
  Paradox Mods; an older build reading a longer block throws "Data size mismatch"), or a new persisted type or a new
  prefab that an older build would turn into a placeholder was added. Then say it in the notes too.

Template (copy above the newest entry):

```
## X.Y.Z (PDX version N, YYYY-MM-DD)
- Save compatible with previous: yes
- Layout versions: Binding 1, TreeMember 2, SeasonPass 1, HistoryMonth 1, MonthInProgress 1
- Do not load in older versions: no

Notes...
```

## 1.0.0 (PDX version -, unreleased)
- Save compatible with previous: n/a (first release)
- Layout versions: Binding 1, TreeMember 2, SeasonPass 1, HistoryMonth 1, MonthInProgress 1
- Do not load in older versions: no

First public release. Notes to be written at release.

## Beta notes (pre-release, not a release entry)
- First-ever working chairlift network for CS2: drawable lift line, station snapping,
  cable anchoring, sagging haul rope with chairs riding the catenary
- Dynamic tower heights (8/12/16 m) selected by terrain, with foundation piers
- Base + top stations (no terrain flattening — site them like a real resort)
- Vanilla recolor support (steel + safety pads)
- Snow-line-aware code mod groundwork
