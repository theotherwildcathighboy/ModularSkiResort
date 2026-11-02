# SnowGunPlumeRules: when a run's snow guns count as running, how the pack is measured, and the plume lease

Pure rules shared by the snow injection (whether a gun run's cold texels are raised, and so billed, this pass) and
`SkiSnowGunPlumeSystem` (whether a gun shows its vapor plume), so a plume never shows on a run the injection is not raising and the bill
never runs while the guns are stopped.

Cat tracks: `TrackGunsCanRun` = in gun season and a cold share above 0 at the day-mean estimate (`RunSnowRules.ThresholdC` gives a track freezing + the gun margin). There is no pack to fill and nothing is raised, so
the hub's row never reads "deep enough". `TrackBilledHa` is the plan area while they can run.

## A run's guns run only when

1. The temperature gate passes (the injection's per-texel thermal test; no cold texel means never running).
2. The pack is below the stop depth, with hysteresis: stop at `RunSnowRules.GunStopDepthM` (within the merge's at-target tolerance
   `RunSnowRules.HalfQuantum`: MergeRowsJob treats a sub-quantum shortfall as no raise), restart only below GunStopDepthM -
   GunRestartBandM.
3. The pack is not already that deep from natural snow (the measure clamps each texel at its own target).
4. It is the gun season (`InGunSeason`, 2 months before the climate's winter to 1 month after: October to March for a December winter, April to September for a June one). Without this, a high run on a warm-base night can have a few cold texels at
   the top while the guns could never open the run (it opens on 60 % of its texels), so they would bill and plume for nothing. Out of
   season the fold raises nothing and no plume shows; the state still steps.

Warm-spell protection holds warm texels, which are never cold, so a protection-only pass is never "running".

## Pack measure (PackDepthM)

Over the run's cold texels that have a target: GunStopDepthM x sum(min(depth, own target)) / sum(own target), from the snapshot the pass
measures from (the previous real pass's readback). The own target is the fold's EdgeValue (the fringe's partial target at the edge), so
fringe texels never hold the guns on. NaN when nothing was measured.

## Plume (PlumeOn)

Setting on, the run's guns running this pass (GunsRunning), and the gun's own elevation cold: GunLocalC <= threshold, the fold's
per-texel expression (`SnowLineSystem.GetLocalTemperature`, freezing + the margin), so where the snow line crosses a run the guns above
it plume and the guns below do not.

## Lease

The plume EffectPrefab (SkiSnowGunPlume, m_RequiredFlags = Disaster 0x800) is enabled while
`Game.Buildings.EarlyDisasterWarningDuration.m_EndFrame` > the simulation frame (EffectControlData.cs:419-424, Game.dll 1.6.0f1). A
running gun holds a lease ending PlumeLeaseFrames ahead, renewed when less than PlumeRenewFrames is left; a stopped gun's lease is cut
to 0. Without our code (mod removed, assets kept) every lease runs out on its own.
