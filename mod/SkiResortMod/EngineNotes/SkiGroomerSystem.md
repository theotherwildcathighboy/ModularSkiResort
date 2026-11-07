# SkiGroomerSystem: one snow groomer per bound cat track

Citations are `File.cs:line` against the decompile baseline 1.6.0f1. All statements here come from reading the code. Nothing has been tried in game yet (see "Unverified").

The groomer is a prop, not a vehicle. It has no pathfinding, lanes or traffic. Nothing reads it (snow, rating, upkeep, score). Restraint is the point. A `Track` has one `Prop` slot, so a track can never hold two groomers. `GroomerRules.MaxGroomers` (64) is a cost guard for an absurd city.

## The entity

The entity starts from the prefab's `ObjectData.m_Archetype` (a `StaticObjectPrefab`). At spawn, the code removes `Static` and adds `Stopped`, `InterpolatedTransform` and `LivePath`.

| Change | Why |
|---|---|
| no `Static` | It keeps the entity out of the static tree of `Game.Objects.SearchSystem` (query Object + Static + Updated or Deleted, `SearchSystem.cs:344-358`). It also keeps the entity out of `UpdateCollectSystem` (Object + Static + Transform + PrefabRef, `:198-213`), which feeds `OverrideSystem` and validation. Only the vehicle, creature and lane-object systems fill the moving tree. Not in a tree means no raycast hit (`RaycastJobs.cs:188-192`, and `DefaultToolSystem.cs:796` asks for StaticObjects and MovingObjects). The result: no selection, no info panel, no bulldoze, no overlap with anything. It also means no asset locale rows are needed for a panel. |
| `Stopped` | The culling query for objects outside a tree takes `Moving` or `Stopped` (`PreCullingSystem.cs:2859-2896`), the parked-car path. The only other read of `Stopped` in that file is `TempCullingJob` (`:1471`), which needs `Temp`. |
| `InterpolatedTransform` | Its presence sets `PreCullingFlags.InterpolatedTransform` (`:1918-1920`). `ObjectInterpolateSystem` copies Transform into it every rendered frame near the camera (no `TransformFrame` buffer: `:252-263`). `BatchDataSystem` uploads that (`:555`). This is the chairs' path, proven in game. |
| `LivePath` | An empty tag that `SerializerSystem.CreateQuery` excludes (`:171-209`). `PrimaryPrefabReferencesSystem.cs:392` excludes it too. Every other reader of `LivePath` also needs `Route` or `Segment` + `CurveElement`, or lists it under None (`RoutesModifiedSystem.cs:1278-1352`, `RouteBufferSystem.cs:791`, `RouteRenderSystem.cs:385`, `SegmentCurveSystem.cs:1463`, `TrafficRoutesSystem.cs:951`). The probe of `SkiCoachSystem` uses the same tag. |

`Created` and `Updated` come with the archetype of the prefab (`ObjectPrefab.RefreshArchetype`) and give the first `InitializeCullingJob` pass (`PreCullingSystem.cs:699-738`). The created query of `UpdateGroupSystem` needs `UpdateFrame`, which this archetype does not have. The design rejected `Overridden`: it skips the raycast but also hides the mesh (`:731`). The design rejected `Owner` = the run: a click would select the run (`RaycastJobs.cs:355-403`).

`CreateArchetype(ComponentType[])` does not compile here: it binds a `ReadOnlySpan` overload the mod's references do not resolve. A spawn is rare, so the four structural calls stay.

## Saved or respawned: respawned

Nothing is saved. `LivePath` keeps the prop and its prefab reference out of the save. So a save does not know the mod's groomer exists, and removing the mod leaves no trace. `ClearSystem` destroys every `PrefabRef` entity on a load and does not exempt `LivePath`, so a reload starts clean. After a load the groomers derive from the rows of the catalog. This is also why an existing save with cat tracks just works: no migration, no marker. The trip is a function of the clock (below). The clock resumes from the saved `SimulationSystem.frameIndex`, so a machine comes back where it was.

## What runs when (Modification4B, every frame)

1. The system completes the job of the last frame, in its first line. The job has long finished. The point is that the code may touch `m_Writes` again.
2. The option. Off: remove all props, stop.
3. The prefab, once per load. `SkiGroomerA`, then the stand-in `SkiDressAkja_Red`: direct `PrefabID("StaticObjectPrefab", name)`, then a scan by name over object prefabs. Neither: one WARN and off for the load.
4. `SyncWithCatalog`, only when `SkiRunCatalogSystem.Rebuilds` moved (closed hub: within 0.5 s of a run event, `ClosedMinRebuildIntervalS`. The catalog's query excludes `Temp` and `Deleted`). A row is a track when `Bound` and `GroomerRules.SourceFor(kind)` is not null. A new `OutlineXZ` reference means the outline moved (the catalog keeps the array until it does) and a path is owed.
5. `DropDeadTracks`, every frame: `EntityManager.Exists` and `HasComponent<Deleted>` read the archetype and wait for no job. Every delete route (bulldoze, hub trash, delete mode, delete-all) ends here on the same frame, without waiting for the catalog.
6. `BuildOnePath`: at most one build per frame. A failure logs one `[no path]` line for that outline and the track stays without a groomer.
7. `PollSeason`: each track every `SeasonPollS` clock seconds. `GetRunSnow` is O(1), main thread.
8. `Reconcile`: spawns and removals, before the job.
9. `RefreshLights`: `EffectsUpdated` on the props for 8 passes after a day/night flip, the only other structural change, also before the job (see "Lights").
10. `MoveProps`: pose per machine on the main thread. One Burst `IJob` on `Dependency` writes `Transform` and `CullingInfo.m_Bounds` through `ComponentLookup`s.

A main-thread read or write of `Transform` would wait for every vehicle job in the city (the 2 ms lesson of docs/research/field/2026-11-01/PERF-BIG-CITY.md). So the system never touches it outside a spawn, and it never reads it at all: the pose comes from path, clock and terrain. For terrain, the system uses `TerrainSystem.GetHeightData` (`:2603-2624`) over `m_CPUHeights`. The game writes `m_CPUHeights` on the main thread only (`:3557`), so a synchronous main-thread read needs no `AddCPUHeightReader`.

## Culling

Vanilla refreshes `CullingInfo.m_Bounds` when `Updated` or `BatchesUpdated` is set (`PreCullingSystem.cs:2430-2439`, `:699-738`). The chairs pay for that with a structural tag every so often. Because they keep `Static`, the tag also reaches the search tree and `UpdateCollectSystem`. A machine without `Static` has no such readers. The no-`TransformFrame` branch of the query (`:1176-1214`) only uses the bounds for the LOD test. So the job writes the bounds directly (`ObjectUtils.CalculateBounds`, `:127`). `BatchDataSystem` copies them to the instance (`:604`). There is no `Updated` in steady state, so there is no structural change and no sync point.

## The clock and the trip

`TimeS = (frameIndex + frameTime) / 60`, the clock of the chairs. It is frozen on pause (`RenderingSystem.cs:187-190`) and slewed at any sim speed (`:193-222`). On pause, the pass skips the write (dt 0 also leaves the smoothing alone), unless a new path owes a pose.

The trip is `GroomerRules.TripAt(clock + phase, length, sense)`, a pure periodic function. One period: drive out, stop, pivot, stop, drive back, stop, pivot, stop. So it cannot drift, loses nothing at 3x and needs no state. Phase and turning sense come from a hash of the first node of the outline, so tracks are not in step and a reload resumes the same trip.

**Speed, real against game.** `SpeedMps` = 3.33 m/s = 12 km/h, inside the 10 to 20 km/h real cats work at. That is real speed on screen at 1x, the same trade the chairs make at 2.5 m/s. A game hour is 182 clock seconds (262,144 frames a day), so against game time it is about 1/20 of real. A true 12 km/h per game hour would be about 66 m/s on screen, absurd. Readability wins. At 3x it looks like 36 km/h, as everything does.

**Pivot.** On the spot about the center, like a tracked vehicle. The turn is 180 degrees at a mean of 15 deg/s (12 s), eased so it starts and ends at rest (peak 22.5). The `TurnRad` is added to the path's own heading, so it is continuous over every leg change. The full turn at the wrap of the cycle is the same angle.

**Heading.** The chord from 4 m behind to 4 m ahead on the path, about the machine's length. No state, so the same place always gives the same heading.

## The ground

The code samples a 5 x 3 grid of `TerrainUtils.SampleHeight` over 7 x 4 m, rotated with the machine. Then it fits a least-squares plane (`FitPlane`). The terrain grid is 3.5 m, so a single sample would jitter. The plane spreads a bump of h over 15 samples. Then the code applies exponential smoothing in clock seconds:

- Height: 0.12 s (a 25 % climb lags 0.1 m, so the tracks do not sink).
- Tilt: 0.35 s (a lag in tilt does not show, and it takes out the kinks).

Slopes clamp at 0.8. A height jump over 3 m is a terrain edit or a new path: snap.

## Season

`SeasonStep` uses `GetRunSnow(...).Share`, not `.State`, because with snow gating off `State` is always Open while `Share` stays real (`SkiSnowInjectionSystem.GetRunSnow`), and the machine must still leave in summer. Cat tracks are never in the table of the injection, so the hub calls one open at or above the midpoint (`NextState(Unknown, share)`). The groomer closes at that same line, so it is never on a closed track. It reopens only at `OpenAtShare` (0.60), so a share that hovers at the midpoint does not make it pop in and out. In the band 0.50 to 0.60 after a closing, the hub may say open with no machine yet.

## Lights

Without `Car` and `TransformFrame`, vanilla's vehicle lights are zeroed (`ObjectInterpolateSystem.cs:1097-1180`, `EffectControlData.cs:449-560`). So the mesh light of `SkiGroomerA` uses purpose `EffectSource` (`ObjectInterpolateSystem.cs:1181-1193`), which follows `effects[0]`, and row 0 is a Night-gated effect. The game re-evaluates the enable state only on `Updated`, `Deleted`, `EffectsUpdated` or `BatchesUpdated` (`EffectControlSystem.cs:1087-1097`). So `RefreshLights` stamps `EffectsUpdated` at each flip of the night signal of `EffectFlagSystem`, with the settle of `SkiNightLightSystem`. The stand-in has no effects. Asset side and costs: docs/research/groomer-lights-2026-11-01.md. In game (2026-11-01, `SkiGroomerA`):

- Lit at night with `EnabledEffect` x2 and `LightState` 1.
- Dark by day with `EnabledEffect` empty.
- One `[lights]` line per flip.
- LOD1 and LOD2 lit at 120 m and 230 m.

The `Emissive` and `LightState` buffers fill only while the prop passes culling. Preflight MOVERLIGHT checks the asset side.

The light rows name owner-culled effects (`SkiGroomerFloodSource`, `SkiGroomerBeaconSource`: copies of the piste flood and the snow gun beacon with
`m_DisableDistanceCulling` false), as vanilla vehicle lights are. With the static effects themselves (`m_DisableDistanceCulling` true, so `m_OwnerCulling`
false, `EffectPrefab.cs:43`), nothing disabled the floods when the machine left `NearCamera`. Their pools stayed lit to 7.9 km after the mesh was
culled at 6.8-7.3 km. Owner-culled, `EffectCullingJob` (`EffectControlSystem.cs:299-353`) enables them with a condition check when the prop arrives
near the camera and disables them when it leaves. `EffectControlJob` skips them while the prop is far (`:215-231`), so the game picks up a flip far away
on arrival. Measured in game (2026-11-03): `EnabledEffect` 3 -> 0 in the same camera step as `m_PassedCulling` 1 -> 0, and no pool in a settled
shot past the cut. MOVERLIGHT fails an owned light row with `m_DisableDistanceCulling` true.

## Lifecycle table

| Event | What happens |
|---|---|
| Track deleted, any route | `DropDeadTracks`, same frame, `Deleted` on the prop |
| Unbound | Next catalog rebuild: not a track any more, prop removed |
| Outline edited | Next rebuild: new path next frame, phase and ground restart. The old path stays for at most those frames. No usable path: prop removed, one log line |
| Summer, or closed | `Reconcile` removes it on the next poll |
| Option off | Removed in the same pass, tracks kept for the way back |
| Game load | `OnGamePreload` forgets. `ClearSystem` destroys. The catalog respawns |
| Editor | `m_Armed` is false: no pass at all |
| Exception | One `!!!` line, props removed, off until the next load |

## Unverified

We saw this in game on 2026-11-01 with `SkiGroomerA`. The non-`Static` `Stopped` shape renders and faces its travel. It sits on the snow with `GroundOffsetM` 0 (no float or sink in close shots). It costs p50 0.017 to 0.024 ms on the main thread. Still open: hit tests, smoothness over a long watch, the forward axis of the stand-in. All are on the bridge list in docs/research/groomer-mover-2026-11-01.md.

## Adding groomed runs later

`GroomerRules.SourceFor(kind)` returns an `IGroomerPathSource`. A later pass adds a source for groomed runs (say, lanes across the polygon) and returns it for that kind. The system, trip, ground and lifecycle do not change.
