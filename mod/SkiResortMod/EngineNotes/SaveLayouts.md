# SaveLayouts: how to bump a persisted layout without breaking saves

`SaveLayouts` is the frozen, append-only table of every persisted type. tests/SaveBatch checks the entries against the serializers.

Since 1.0 a row that any release carried never gets a new version. A new field goes in a NEW type with a new row (the 1.0.1
SkiTrackPack and 1.0.2 SkiRulesStamp pattern). tests/SaveBatch SL-R fails on a released row that grew, vanished or moved, and on a
released version missing from LayoutTests.Shipped. The steps below are only for a row no release has carried yet.

To bump a layout:

1. Append the new fields at the END of `Serialize`.
2. Bump `CurrentVersion`.
3. Read the new fields under `if (m_Version >= N)` in `Deserialize`.
4. Append ONE entry to the type's row in `SaveLayouts`.
5. Write the new changelog line with "Do not load in older versions: yes".

Example (1.0.4, T284): the per-area prices did not grow SkiSeasonPass (still 10 bytes, SL-P4). They added two new types:
- `SkiPassPlan` (row PassPlan, 36 bytes, on the sign).
- `SkiSeasonPassArea` (row PassArea, 6 bytes, on the household).
A save without them resolves to the 1.0.3 behavior (SL-P3). A 1.0.4 save loaded in 1.0.3 skips both types as unknown.

A new persisted concept is a new type with a new row. Never edit a shipped entry. Never reorder the fields of a row. Never delete the
row of a shipped type. The bytes are in players' saves.
