# Ski Lift Best Practices (player guide draft)

## Build order — two ways that work

**Line-first (recommended — perfect alignment for free):**
1. Draw the lift line up the mountain first — one straight segment is geometrically
   perfect; click intermediate points where you want towers (denser on rough terrain).
2. Place the **base station** snapping onto the line's lower end, then the **top
   station** on the upper end. Cables, power, and chairs reconnect automatically.

**Stations-first (classic):**
1. Place base station, then top station (eyeball the alignment).
2. Draw the line from the base snap point to the top snap point.

*(Line-first pending final in-game verification of chair re-seating — remove this note
before upload.)*

## Terrain expectations

- Fixed-grip lifts refuse grades above 80% — that's aerial tram terrain (future pack).
- Towers auto-select their height (8/12/16 m) and stand on concrete foundations. On
  reasonable lift lines they ground themselves like vanilla power pylons.
- **If you push a lift somewhere absurd, you get absurd results** — a line flung across a
  canyon or up a cliff wall will float towers, exactly like vanilla power lines trench and
  float in the same places. The game engine bends the same way for everything; build like
  a lift engineer and it looks like real engineering.
- Stations do not flatten terrain. Site them on reasonably level ground, or terraform
  first (or use Move It afterward).

## Ski school

- Terrain tip: level the ground at the top of the magic carpet bubble, then use the slope tool down to its base.

*(Draft note: reference shot docs/research/field/2026-10-15/shots/21.30.27.94.png. Remove before upload.)*

## Power

Stations are grid transformers: feed the base station's area with power and the lift line
carries it up the mountain. No power = no lift (icons will tell you).

- Lift lines show in the electricity overlay as high-voltage lines.
- The base station acts as a transformer: wire a low-voltage line into its low-voltage stub.
- Do not connect city high-voltage lines to a lift tower or lift line (untested, can break the lift).
- Remove all lifts before uninstalling the mod.

## Playing nice

- Anarchy works as the escape hatch for stubborn placements.
- Recoloring: stations recolor via the vanilla picker (steel + safety pads); line towers
  need Recolor mod's painter tool (they belong to the network).
