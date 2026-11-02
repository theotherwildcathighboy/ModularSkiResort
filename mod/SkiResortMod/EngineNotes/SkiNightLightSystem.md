# SkiNightLightSystem: why Night-gated lights on run props need a re-evaluation stamp

## Why the system exists

`EffectConditionFlags.Night` (8192) evaluates correctly on a `LightEffect`, but nothing in the engine re-evaluates it on a plain
static area-border sub-object: on our props, in Game mode, nothing stamps the re-evaluation tags on a cadence, so a Night-gated
light loaded at night stays ON all the next day until a tool hover corrects it.

The hover mechanism: hover adds `Hidden` to the original area (GenerateAreasSystem.cs:172-174) and `SubObjectHiddenSystem` cascades
`Hidden` + `BatchesUpdated` onto the border props (:177-196), which forces the light OFF via the Hidden gate
(EffectControlData.cs:669). Un-hover removes `Hidden` and stamps `BatchesUpdated` again (ToolClearSystem.cs:96-106), which
re-enters `EffectControlSystem`'s `m_UpdatedEffectsQuery`, the same Any-list (Updated, Deleted, EffectsUpdated, BatchesUpdated;
EffectControlSystem.cs:1087-1097) this system feeds, and the Night condition is re-tested fresh.

Vanilla street lights work because `StreetLightSystem` stamps `EffectsUpdated` at dusk and dawn (StreetLightSystem.cs:204, :218,
:236); this system is our `StreetLightSystem`.

Stamping also closes the fly-by hazard: `Game.Effects.SearchSystem`'s distance path re-enables an out-of-range light on approach
WITHOUT re-testing its conditions (`ActionFlags.SkipEnabled`, `checkEnabled: false`, EffectControlSystem.cs:700), so a wrongly-on
light survives at most one flag pass here.

## Registration is load-bearing

- Anchor: `UpdateAfter<EffectFlagSystem>(GameSimulation)`; we read the flag vanilla just wrote (`EffectFlagSystem.OnUpdate`:155
  sets `m_IsNightTime` from `TimeSystem.normalizedTime` vs `kNightBegin` 0.75 / `kDayBegin`) via the public `GetData()`, one clock.
- Interval: `GetUpdateInterval` must return EffectFlagSystem's own 2048. `UpdateSystem` inherits an anchor's runtime offset only when
  the intervals are EQUAL (UpdateSystem.cs:406/:431, `PatchSystemOffset` :457-517). An unequal interval gets a fixed
  scatter-assigned offset that either aligns with the anchor or never coincides, and which one can flip whenever the registration
  set changes (e.g. another mod adds a system).
- Tag lifetime: a tag stamped here (GameSimulation, LateUpdate) survives into the next frame's PreCulling, where
  `EffectControlSystem` (SystemOrder.cs:636) consumes it, and dies at that frame's `CleanUpSystem`.

## Settle window

`IsEnabled` staggers Night on-switching by up to `kNightRandomTicks` (15000) past `m_LastTimeChange` (EffectFlagSystem.cs:66).
Today that is dead code (`OnUpdate` never assigns `m_LastTimeChange` for the time branch), so one stamp would do; but if a patch
fixes it, a single stamp at the flip would freeze most lights OFF. So after every flip we re-stamp for `SettlePasses` passes:
8 x 2048 = 16384 > 15000. In the current engine the extra stamps are harmless no-ops.

## Scope

Placed props of our runs only, by the `SkiRunPropMember` tag (EngineNotes/SkiRunPropMember.md); masts face inward from the placer, this system no longer turns them. A city with no resort (`ResortPresence`) or no run area returns before any query result is read. Stamping a prop with no
effects is a no-op; per-pass cost is one batched `AddComponent`, twice per game day plus settle.
