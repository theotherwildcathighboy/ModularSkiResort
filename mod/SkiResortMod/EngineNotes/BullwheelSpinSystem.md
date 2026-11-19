# BullwheelSpinSystem: a station's bullwheel turns with its lift's rope (T256)

Files:
- `BullwheelMath.cs`: the pure math. tests/LiftBatch links it (`WheelTests`).
- `BullwheelSpinSystem.cs`: the two systems.
- `ChairPinSystem.Wheels.cs`: the data from the loop cache.

Engine citations are the 1.6.0f1 baseline decompile.

## How the engine animates bones
- A mesh with `ProceduralAnimationProperties` gives its object `Skeleton` and `Bone` buffers (`Game.Prefabs/ObjectGeometryPrefab.cs:84-153`).
  Buildings follow the same rule.
- The buffers hold data only while the object is in draw distance. `InitializeBonesSystem` fills them with the bind pose on entry.
  It clears them on exit (`Game.Rendering/InitializeBonesSystem.cs:52-86`).
- Each frame, `ObjectInterpolateSystem.UpdateStaticAnimations` calls `AnimateStaticBone` for every bone of a near object
  (`Game.Rendering/ObjectInterpolateSystem.cs:252-262, :266-389`).
- The switch in `AnimateStaticBone` has cases for the rotating types (`:796-1056`). They turn the bone about its local Y
  (`AnimateRotatingBoneY :3826-3845`).
- The switch has no case for type None. A None bone keeps our value until the engine clears the buffers.
- `ProceduralUploadSystem` skins from the instance `Bone` buffer when `Skeleton.m_CurrentUpdated` is set
  (`Game.Rendering/ProceduralUploadSystem.cs:129-142`).

## Why vanilla's rotating types do not fit
PoweredRotation reads the object's ElectricityConsumer or its efficiency (`ObjectInterpolateSystem.cs:1275-1321`). Three cases go wrong:
- A top station has no consumer. Its wheel would turn without power.
- A closed lift would turn.
- The tram's rope reverses each trip. A vanilla type cannot reverse.

## The mesh contract (art)
- The station mesh is skinned. `Root` is static.
- A bone whose name starts with `Bullwheel` turns (`BullwheelMath.BonePrefix`).
- That bone has type None and an identity rest rotation. Its head is on the axle. Its local Y is the axle.
- Every LOD carries the same bones. A LOD without `ProceduralAnimationProperties` draws frozen in the rest pose
  (`Game.Rendering/BatchDataSystem.cs:731-757`).
- After every import, do the root bone surgery (PIPELINE section 6 "Skinned assets", gate BONEROOT).
- A station mesh without such a bone is skipped. The build line counts it. Nothing else changes for it.

## BullwheelSpinSystem (Modification4B, after ChairPinSystem)
At a loop cache build (`ChairPinSystem.WheelBuilds` changed):
1. `CollectWheelDrives` gives one row per loop and station. The row comes from the turn arc whose center is the station's catalog
   wheel (`WheelLocal`).
2. The row holds the arc's radius and `BullwheelMath.TurnSign(m_ArcB, m_ArcU)`.
3. The system finds the wheel bones of the station prefab by name (the `TrainPitchSystem.RigOf` idiom).
4. The engine sorts `ProceduralBone`. The bone's slot is the entry whose `m_BindIndex` equals the bone's index in `m_Bones`.
5. The system writes the runtime buffer `BullwheelBone` on the station. This is the only structural change.
6. Each station gets one slot. A station keeps its angle across builds.

Each frame:
1. The system completes the bone job of the last frame.
2. It reads the step of the mover's clock (`ChairPinSystem.TimeS`).
3. It advances every slot with `BullwheelMath.Advance` and the loop's rope speed (`WheelSpeedMps`).
4. It writes the angles to a persistent `NativeArray<float>`. It makes no ECS write.

## BullwheelBoneSystem (Rendering, after InitializeBonesSystem)
A Burst `IJobChunk` runs over the stations with `BullwheelBone`, `Skeleton` and `Bone`.
For an allocated skeleton, the job writes `rest x RotateY(angle)` to the bone.
It sets `m_CurrentUpdated` only when the rotation changed. A wheel that stands uploads nothing.

## Rules of the math (WheelTests)
- **Direction.** The sign comes from the loop's own arc. With the up lane on the right looking uphill, the rope turns counterclockwise
  seen from above at both terminals. The sign is -1 there.
- Unity's `RotateY(+a)` is clockwise seen from above. The rig probe showed this in game.
- A swapped loop turns both wheels the other way. The heading of the line does not change the sign.
- **Rim speed = rope speed.** The angle is the rope's travel divided by the radius.
- **Held loops stand.** `WheelSpeedMps` is 0 while the loop is held (no power, closed). A paused game gives a clock step of 0.
- **A resume has no jump.** The system adds to the angle each frame. It does not derive the angle from `PhaseOf`.
  The carriers' resume moves their offsets, and a wheel has no offset.
- A clock step back leaves the angle. A step over `MaxStepS` (a load) leaves it too.
- A negative speed turns the wheel back. The tram's return trip needs this.

## Cost
- Main thread, each frame: one loop over the slots. A lift has two slots at most.
- One chunk job over the stations that carry `BullwheelBone`.
- A station out of draw distance costs one length test.
- Measure with the bridge `perf systemCost` before a release.

## Not saved
`BullwheelBone` is a plain buffer element with no serializer. The first loop build of a load adds it again.
The angle starts at 0 on every load.

## Log lines
- `Lifts [wheels]: <prefab> has N bullwheel bone(s); they turn with the rope`: once per prefab per load.
- `Lifts [wheels]: build N: W bullwheel(s) turn with their rope, ...`: when the counts change.
- `!!! Lifts [wheels]: ...`: a throw. The system is off for the load, and the wheels keep their last angle.
