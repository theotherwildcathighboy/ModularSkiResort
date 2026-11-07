# SnowGunPlumeRules: when a run's snow guns count as running, how the pack is measured, and the plume lease

Pure rules shared by two systems:
- The snow injection decides whether the cold texels of a gun run are raised, and so billed, this pass.
- `SkiSnowGunPlumeSystem` decides whether a gun shows its vapor plume.

So a plume never shows on a run that the injection is not raising, and the bill never runs while the guns are stopped.

Cat tracks: `TrackGunsCanRun` = in gun season and a cold share above 0 at the day-mean estimate (`RunSnowRules.ThresholdC` gives a
track freezing + the gun margin). There is no pack to fill and nothing is raised, so the row of the hub never reads "deep enough".
`TrackBilledHa` is the plan area while they can run and the gun value of the track is above Off (`gunsOn`, DECISIONS 563). It is 0
otherwise.

## A run's guns run only when

1. The temperature gate passes (the per-texel thermal test of the injection, no cold texel means never running).
2. The pack is below the stop depth, with hysteresis. Stop at `RunSnowRules.GunStopDepthM` (within the at-target tolerance of the merge,
   `RunSnowRules.HalfQuantum`: MergeRowsJob treats a sub-quantum shortfall as no raise). Restart only below GunStopDepthM -
   GunRestartBandM.
3. The pack is not already that deep from natural snow (the measure clamps each texel at its own target).
4. It is the gun season (`InGunSeason`, 2 months before the winter of the climate to 1 month after: October to March for a December
   winter, April to September for a June one). Without this rule, a high run on a warm-base night can have a few cold texels at the
   top. The guns could never open the run (it opens on 60 % of its texels). So they would bill and plume for nothing. Out of season
   the fold raises nothing and no plume shows. The state still steps.

Warm-spell protection holds warm texels, which are never cold, so a protection-only pass is never "running".

## Pack measure (PackDepthM)

The measure runs over the cold texels of the run that have a target: GunStopDepthM x sum(min(depth, own target)) / sum(own target). It
uses the snapshot that the pass measures from (the readback of the previous real pass). The own target is the EdgeValue of the fold (the partial target
of the fringe at the edge), so fringe texels never hold the guns on. NaN when nothing was measured.

## Plume (PlumeOn)

A gun plumes when all of these are true:
- The setting is on.
- The guns of the run are running this pass (GunsRunning).
- The elevation of the gun itself is cold: GunLocalC <= threshold, the per-texel expression of the fold
  (`SnowLineSystem.GetLocalTemperature`, freezing + the margin).

So where the snow line crosses a run, the guns above it plume and the guns below do not.

## Lease

The plume EffectPrefab (SkiSnowGunPlume, m_RequiredFlags = Disaster 0x800) is enabled while
`Game.Buildings.EarlyDisasterWarningDuration.m_EndFrame` > the simulation frame (EffectControlData.cs:419-424, Game.dll 1.6.0f1). A
running gun holds a lease that ends PlumeLeaseFrames ahead. The lease renews when less than PlumeRenewFrames is left. The lease of a
stopped gun is cut to 0. Without our code (mod removed, assets kept), every lease runs out on its own.
