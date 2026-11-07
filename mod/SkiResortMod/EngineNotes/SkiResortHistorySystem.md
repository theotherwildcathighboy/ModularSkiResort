# SkiResortHistorySystem: how the monthly history is stored, sampled, mirrored and saved

This is the C# half of the hub's monthly history store. The element and its bytes are pure (SkiResortHistory.cs). SkiResortHistorySystem.cs
holds their ECS halves and the system that fills them. `SkiResortHubUISystem` publishes the `history` binding from these buffers.

## Store

Each Welcome Sign has a `DynamicBuffer<SkiResortHistoryMonth>` with `[InternalBufferCapacity(0)]` (off-chunk). The buffer holds one element
per closed calendar month, oldest first, with keys strictly increasing. `SkiResortHistory.Append` enforces this:

- The same key replaces.
- A key more than one month on inserts one Gap marker first.
- An older key is rejected.

The buffer has a 1200-month safety cap. A deleted ski area's history goes with its sign.

The game saves the buffer like our components: every `ISerializable` TypeManager type of a mod assembly gets a serializer (Colossal
ComponentSerializerLibrary.cs:73-87, 1.6.2f1). The game writes a buffer as int32 length + each element's Serialize
(BufferElementDataSerializer.cs:80-92, BinaryWriter.cs:120-126). Mod.cs needs no addition for that.

## Sampling

The system runs in GameSimulation, UpdateAfter `SkiGuestCounterSystem` with the counter's interval (16). So it inherits the counter's offset
and runs right after the counter in the same frame (UpdateSystem.cs:400-431). Each tick it compares the counter's `LastMonthStep.Generation`
with the last one it handled. Three cases exist:

- Rolled:
  - For every live sign (the counter's `LiveSigns` plus the ledger's linked entries), the system builds a pending element. The element
    comes from the ledger's Last (guests, tickets paid, ticket / lodge / pass / lesson income, lift charge, run upkeep). Partial / Short
    come from the counter's closed-month labels.
  - It calls `SkiResortStatsSystem.RequestRebuild("history")` once.
  - It commits the pending set in one of two cases. In the first case, the stats system rebuilt since the request (Rebuilds moved:
    buildings, wages, attraction, snow % from the sign's `ResortLive` row) AND `SkiDaySystem` stepped to the new month (ski days = its
    `ArrivedLastMonth`). In the second case, `Tuning.SnapshotTimeoutFrames` passed, and the element gets SnapshotMissing / skiDays -1.
  - The system adds the buffer on first use (main thread).
- Jumped: the system records the month in progress before the jump from the mirror (below, <= `MirrorEveryFrames` old) as Partial +
  SnapshotMissing. The months the calendar skipped become a Gap marker at the next roll (Append).
- Started (the first counter tick after a load): the system seeds (below).

## Month in progress

`SkiResortMonthInProgress` on each live sign mirrors the ledger's This of that area. The mirror also holds the pass season, the month key,
the partial / by-jump state and the frames simulated so far. The mirror runs in two cases: every `Tuning.MirrorEveryFrames` sim frames when
the ledger's Revision moved, and at every start / roll / jump. A save therefore holds a month in progress that is up to 256 frames behind
(~0.1 % of a month at the default 12 days a year).

After a load, at the counter's Started step, the system does these steps:

1. It adds back every saved component whose key is the ledger's month (`SkiResortLedger.SeedMonth` + `SeedThis`).
2. The counter's month measures follow (`SeedMonthMeasures`).

A component of another month seeds nothing, and the month stays "since load" (the hub's "(partial)"). Not linked (`EntityRef.Null`) has no
sign and is not restored.

Why N = 256: the cost is a handful of `SetComponentData` per mirror and nothing when the ledger did not move. The bound on what a save can
miss is N frames of bookings: 256 / 262,144 = ~0.1 % of a month at 12 days a year (1.2 % at 1 day a year). 256 is also the guest
counter's leisure-scan period (16 x 16). So every leisure scan is followed by a mirror at most one scan later.

## Logs

The logging makes silence impossible. The system writes these lines:

- created
- ARMED per load, with the areas holding history and their lengths
- `History [load]` once per load at the seed (seeded / stale / none)
- `History [month]` per area per roll
- `History [jump]` per jump
- the house "!!!" error pattern (disabled for the load)

There are no per-mirror lines.

## Cost

Per tick: a few field reads. Per roll: one stats rebuild and one buffer append per area. Per mirror: N signs x one component write.
`AddBuffer` / `AddComponentData` are structural changes (a sync point), once per sign per city.

## Binding

The hub reads two counters of this system. `Revision` moves when a stored month changes: an append, a replace, a gap marker, and at every
city load. `ProgressRevision` moves at every month-in-progress mirror write.
