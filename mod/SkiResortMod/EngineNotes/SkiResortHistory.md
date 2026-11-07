# SkiResortHistory: the frozen save layout and flags of the hub's history store

Pure part of the hub's history store, linked by tests/ResortBatch (HistoryTests.cs). It has these types:

- `SkiResortHistoryMonth`: one ski area x one closed calendar month. The ECS half (`IBufferElementData`, `ISerializable`,
  `[InternalBufferCapacity(0)]`) is the other `partial` in SkiResortHistorySystem.cs. The bytes are written here (`WriteTo` /
  `ReadFrom` through `IHistoryWriter` / `IHistoryReader`), so the round-trip test checks exactly what the save gets.
- `SkiResortMonthInProgress`: the month in progress of the ledger for one ski area. It is mirrored onto the sign and seeded back into the
  ledger on load when the month key matches. Same split: ECS half in SkiResortHistorySystem.cs.
- `SkiResortHistory`: ring ops (Append with dedupe by month key and automatic GAP marker, 1200-month safety cap, no range cap),
  season / bucket sums (the July-June ski year of `SkiMoneyRules.SeasonKeyOf`), and the run-upkeep split.
- `IHistoryWriter` / `IHistoryReader` / `IHistoryStore`: the seams to Colossal's `IWriter` / `IReader` and to `DynamicBuffer`.

## Save layout v2 (frozen at release)

`SkiResortHistoryMonth` v2, 63 bytes, little-endian (Colossal `BinaryWriter`). v1 (59 bytes, the same without the last field)
still loads, and its depth reads -1 (T142 B7b, 2026-10-24, the pre-1.0 append the paragraph below allows):

```
byte version | byte flags | int32 monthKey | int32 tourists | int32 residents | int32 skiDays | int32 ticketsPaid |
int32 ticketIncome | int32 lodgeIncome | int32 passIncome | int32 lessonIncome | int32 liftCost | int32 runCost |
int32 buildingsCost | int32 wagesCost | int32 attraction | byte snowPct | int32 snowDepthCm (v2)
```

`SkiResortMonthInProgress`, 150 bytes (v2 appended `BusCharged` before the first release, and a v1 block reads it as 0): see its LAYOUT comment.
A closed month has no bus column: `FromLedger` folds the coach bill into `m_LiftCost`, so the History net stays true and its Lifts row includes it.

After 1.0 these layouts never change. No payload length is written, so an older build that reads a longer element cannot skip the tail.
It reads the rest of the block shifted (and a garbage int32 becomes the element count of the buffer). A new field goes in a new
`ISerializable` type (a companion component or buffer, e.g. `SkiResortHistoryMonth2`). An older build skips such a type cleanly as an
unknown type. Before the 1.0 freeze, an append + `CurrentVersion` bump (read under `if (m_Version >= 2)`) is still allowed. Never
reorder, retype or rename these types. Never move their namespace. Never turn the buffer into a component (the saved type name is the
`AssemblyQualifiedName`). The game writes a buffer as int32 length + the own Serialize of each element, stride 0
(Colossal.Serialization.Entities BufferElementDataSerializer.cs:80-92, BinaryWriter.cs:120-126, 158-161).

## Flags

`HistoryFlags` (the hub binding uses the same bits):

- Partial 1: counting started partway (a load whose month in progress was not seeded, or a calendar jump).
- Short 2: fewer than 95 % of the month's frames were simulated (`SkiMoneyRules.IsShortMonth`).
- CostsEstimated 4: reserved (v1 never sets it: every cost but buildings / wages is booked).
- SnapshotMissing 8: buildings / wages / attraction / snow were not taken (the stats rebuild did not answer in time).
- Gap 16: a marker, not a month: the months from its key up to the next element's key - 1 are missing (calendar jump, mod off).
  Every value of a Gap element is "unknown".
- SkiDaysPartial 32: the ski-day count of the month is since-load / since-jump while the ledger's month is whole (`SkiDaySystem`
  keeps its month in memory only).
- First 64: the area's first recorded month (the area may have been placed partway through it).

## Values

Money in whole ₵ as int32, clamped (`ClampInt`). The lift charge (double) is rounded. Unknown: skiDays / buildingsCost / wagesCost /
attraction -1, snowPct 255. Net is never stored (the UI derives it, so it cannot disagree with its parts).
`HubRules.NetMonth(element.Money)` is that same net, the one definition the Finances tile uses.
