# LiftReconnectEpisodes: the per-station episode state machine that throttles reconnect pokes

The pure per-station episode state machine behind `LiftReconnectSystem`'s flood fix.

## Why
Move It keyboard nudges of an already connected top station change the station Transform every frame. Transform is part of the reconnect's
poke signature, so every moved frame poked AND logged. Placement itself pokes once, which is what makes a line drawn before its station
close in the same frame.

## Contract (one instance per LiftReconnectSystem; keyed by station EntityRef)
- `OnChange(...)` is called by the system only when a station's signature (foreign-edge set + Transform) differs from the last one it saw.
  It returns what to do now:
  - no episode open and no line attached: nothing;
  - no episode open and a line attached: open the episode and poke at once; if the foreign-edge set changed (a line attached) also a full
    chair pass at once (same-frame closure on placement);
  - episode open, foreign-edge set changed and a line attached: poke + full pass at once;
  - episode open, otherwise: poke only when >= PokeIntervalFrames frames passed since the last poke; a detached line never pokes.
- `Tick(frame, settled)` is called every frame, whether or not any station changed (it must run before the system's early return on an
  empty query, else the trailing poke never fires). Every episode whose last change is >= SettleFrames old is closed and handed back; the
  system then does the trailing poke + full pass + the one log line. Tick never pokes on its own.
- `Remove(station)` closes one episode (station deleted); `FlushAll` closes every episode (game load, the system's catch). The system logs
  each flushed episode.
- Frames are the system's own update count (every frame, paused included); times are wall-clock seconds, used only for the "over N s" in
  the log line.

## Trace of a 34-change drag
At 1-frame spacing (frames 0..33, already connected station): leading poke at 0, pokes at 10, 20, 30, none for 31..33, settle at 48, then
trailing poke + full pass + one line "moved 34x ..., 5 poke(s)". tests/LiftBatch checks exactly this, plus "attach, then an immediate
full pass".

## Notes
Constants are tunable and unmeasured; printed in LiftReconnectSystem's ARM line. State is a dictionary of open episodes only; no per-frame
allocation in steady state (Tick walks the dictionary only while an episode is open).
