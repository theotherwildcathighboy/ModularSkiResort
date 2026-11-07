# SkiNightLightSystem: why Night-gated lights on run props need a re-evaluation stamp

## Why the system exists

`EffectConditionFlags.Night` (8192) evaluates correctly on a `LightEffect`. But nothing in the engine re-evaluates it on a plain
static area-border sub-object. On our props, in Game mode, nothing stamps the re-evaluation tags on a cadence. So a Night-gated
light loaded at night stays ON all the next day until a tool hover corrects it.

The hover mechanism: hover adds `Hidden` to the original area (GenerateAreasSystem.cs:172-174), and `SubObjectHiddenSystem` cascades
`Hidden` + `BatchesUpdated` onto the border props (:177-196). This forces the light OFF via the Hidden gate
(EffectControlData.cs:669). Un-hover removes `Hidden` and stamps `BatchesUpdated` again (ToolClearSystem.cs:96-106). That re-enters
`EffectControlSystem`'s `m_UpdatedEffectsQuery`, the same Any-list (Updated, Deleted, EffectsUpdated, BatchesUpdated,
EffectControlSystem.cs:1087-1097) this system feeds. The Night condition is then re-tested fresh.

Vanilla street lights work because `StreetLightSystem` stamps `EffectsUpdated` at dusk and dawn (StreetLightSystem.cs:204, :218,
:236). This system is our `StreetLightSystem`.

Stamping also closes the fly-by hazard: `Game.Effects.SearchSystem`'s distance path re-enables an out-of-range light on approach
WITHOUT re-testing its conditions (`ActionFlags.SkipEnabled`, `checkEnabled: false`, EffectControlSystem.cs:700). So a wrongly-on
light survives at most one flag pass here.

## Registration is load-bearing

- Anchor: `UpdateAfter<EffectFlagSystem>(GameSimulation)`. We read the flag vanilla just wrote (`EffectFlagSystem.OnUpdate`:155
  sets `m_IsNightTime` from `TimeSystem.normalizedTime` vs `kNightBegin` 0.75 / `kDayBegin`) via the public `GetData()`, one clock.
- Interval: `GetUpdateInterval` must return EffectFlagSystem's own 2048. `UpdateSystem` inherits an anchor's runtime offset only when
  the intervals are EQUAL (UpdateSystem.cs:406/:431, `PatchSystemOffset` :457-517). An unequal interval gets a fixed
  scatter-assigned offset. That offset either aligns with the anchor or never coincides with it. Which one happens can flip whenever the
  registration set changes (e.g. another mod adds a system).
- Tag lifetime: a tag stamped here (GameSimulation, LateUpdate) survives into the next frame's PreCulling, where
  `EffectControlSystem` (SystemOrder.cs:636) consumes it. The tag dies at that frame's `CleanUpSystem`.

## Settle window

`IsEnabled` staggers Night on-switching by up to `kNightRandomTicks` (15000) past `m_LastTimeChange` (EffectFlagSystem.cs:66).
Today that is dead code (`OnUpdate` never assigns `m_LastTimeChange` for the time branch), so one stamp would do. But if a patch
fixes it, a single stamp at the flip would freeze most lights OFF. So after every flip we re-stamp for `SettlePasses` passes:
8 x 2048 = 16384 > 15000. In the current engine the extra stamps are harmless no-ops.

## Scope

The system stamps placed props of our runs only, by the `SkiRunPropMember` tag (EngineNotes/SkiRunPropMember.md). Masts face inward from the placer, and this system no longer turns them. In a city with no resort (`ResortPresence`) or no run area, the system returns before it reads any query result. Stamping a prop with no
effects is a no-op. Per-pass cost is one batched `AddComponent`, twice per game day plus settle.
