# SnowLineSystem: the shared climate oracle (ambient temperature, snow line, day mean) and why it samples the climate curve

Citations are Game.dll 1.6.0f1. The system is pull-based: it stays Enabled = false and does no per-tick work; consumers (for example
`SkiResortAttractivenessSystem`) call the accessors during their own recomputes, and ClimateSystem's public floats are cheap main-thread
reads.

## The lapse model

- T_local(h) = T_ambient - (h - temperatureBaseHeight) x snowTemperatureHeightScale
- snow line: T_local = freezingTemperature, so h_snow = temperatureBaseHeight + (T_ambient - freezing) / scale

Checked against visible snow lines in game (lapse 0.01 C/m, freezing 0 C). Anchors: summer ambient ~8.8 C gives a snow line near 1470 m;
winter ~-5 C gives ~100 m.

## Accessors

- `GetAmbientTemperature` samples the climate curve at the current date (`ClimateSystem.SampleClimate(float)`, ClimateSystem.cs:625-654,
  overrides applied) instead of reading `ClimateSystem.temperature`, which is written only in OnUpdate (:465-472): 0.0 before the first
  simulation tick of a session, the previous city's value on later loads. Inside GameSimulation the two are equal bit for bit
  (ClimateSystem runs earlier in the same frame, SystemOrder.cs:358-360).
- `GetDayMeanTemperature`: mean of 24 hourly SampleClimate samples ending at the start of the current game hour, cached per (game hour,
  daysPerYear, climate, temperature override); at most one recompute per game hour (SimulationSystem.cs:238, :255).
- `GetSnowmakingMarginC` is the one snowmaking margin of the mod; `IsSnowGatingEnabled` the one read of "Snow & season gating".

## Logs

The climate calibration line is written on every Game or Editor load with the cost of 100 SampleClimate reads; the first day-mean of a
load writes one `Climate [day-mean]` line.
