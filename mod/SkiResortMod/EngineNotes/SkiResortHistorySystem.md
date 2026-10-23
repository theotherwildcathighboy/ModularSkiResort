# SkiResortHistorySystem: how the monthly history is stored, sampled, mirrored and saved

The hub's monthly history store, the C# half. The element and its bytes are pure (SkiResortHistory.cs); SkiResortHistorySystem.cs
holds their ECS halves and the system that fills them. `SkiResortHubUISystem` publishes the `history` binding from these buffers.

## Store

A `DynamicBuffer<SkiResortHistoryMonth>` on each Welcome Sign, `[InternalBufferCapacity(0)]` (off-chunk), one element per closed
calendar month, oldest first, keys strictly increasing (`SkiResortHistory.Append`: same key replaces, a key more than one month on
inserts one Gap marker first, an older key is rejected), 1200-month safety cap. A deleted ski area's history goes with its sign.

Saved by the game like our components: every `ISerializable` TypeManager type of a mod assembly gets a serializer (Colossal
ComponentSerializerLibrary.cs:73-87, 1.6.2f1); a buffer is written as int32 length + each element's Serialize
(BufferElementDataSerializer.cs:80-92, BinaryWriter.cs:120-126). Nothing is added in Mod.cs for that.

## Sampling

GameSimulation, UpdateAfter `SkiGuestCounterSystem` with the counter's interval (16), so it inherits the counter's offset and runs
right after it in the same frame (UpdateSystem.cs:400-431). Each tick it compares the counter's `LastMonthStep.Generation` with the
last one it handled:

- Rolled: for every live sign (the counter's `LiveSigns` plus the ledger's linked entries) a pending element from the ledger's Last
  (guests, tickets paid, ticket / lodge / pass / lesson income, lift charge, run upkeep) with Partial / Short from the counter's
  closed-month labels; `SkiResortStatsSystem.RequestRebuild("history")` once. The pending set is committed when the stats system has
  rebuilt since the request (Rebuilds moved: buildings, wages, attraction, snow % from the sign's `ResortLive` row) and
  `SkiDaySystem` has stepped to the new month (ski days = its `ArrivedLastMonth`), or after `Tuning.SnapshotTimeoutFrames` with
  SnapshotMissing / skiDays -1. The buffer is added on first use (main thread).
- Jumped: the month in progress before the jump is recorded from the mirror (below; <= `MirrorEveryFrames` old) as Partial +
  SnapshotMissing; the months the calendar skipped become a Gap marker at the next roll (Append).
- Started (the first counter tick after a load): the seed (below).

## Month in progress

`SkiResortMonthInProgress` on each live sign mirrors the ledger's This of that area (+ its pass season, the month key, the
partial / by-jump state and the frames simulated so far) every `Tuning.MirrorEveryFrames` sim frames when the ledger's Revision
moved, and at every start / roll / jump. A save therefore holds the month in progress up to 256 frames (~0.1 % of a month at the
default 12 days a year) behind. After a load, at the counter's Started step, every saved component whose key is the ledger's month
is added back (`SkiResortLedger.SeedMonth` + `SeedThis`) and the counter's month measures follow (`SeedMonthMeasures`); a component
of another month seeds nothing and the month stays "since load" (the hub's "(partial)"). Not linked (`EntityRef.Null`) has no sign
and is not restored.

Why N = 256: the cost is a handful of `SetComponentData` per mirror and nothing when the ledger did not move; the bound on what a
save can miss is N frames of bookings, 256 / 262,144 = ~0.1 % of a month at 12 days a year (1.2 % at 1 day a year). 256 is also the
guest counter's leisure-scan period (16 x 16), so every leisure scan is followed by a mirror at most one scan later.

## Logs

Silence impossible: created; ARMED per load with the areas holding history and their lengths; `History [load]` once per load at the
seed (seeded / stale / none); `History [month]` per area per roll; `History [jump]` per jump; the house "!!!" error pattern
(disabled for the load). No per-mirror lines.

## Cost

Per tick a few field reads; per roll one stats rebuild and one buffer append per area; per mirror N signs x one component write.
`AddBuffer` / `AddComponentData` are structural changes (a sync point), once per sign per city.

## Binding

The hub reads two counters of this system: `Revision` (moves when a stored month changes: an append, a replace, a gap marker, and
at every city load) and `ProgressRevision` (moves at every month-in-progress mirror write).
