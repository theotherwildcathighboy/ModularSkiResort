# Credits & Attribution

- **Modular Golf Course Pack** (Paradox Mods ID 144768) — the modular-recreation concept
  that inspired this project's architecture.
- **Meapy — CS2-TourismOverhaul** — the tourism/attraction framework this mod integrates
  with; studied for compatibility and design reference.
- **Yenyang — Recolor** — the asset color convention (channel masks, light-albedo regions)
  follows the standard their mod established; hosted towers are recolorable via its painter.
- **krzychu124 — Scene Explorer** — indispensable for live ECS debugging during development.
- **CitiesSkylinesModding community** — agents-plugins toolchain and documentation.
- Reference photography: Arapahoe Basin's original Pallavicini lift (Yan double) — this
  mod's fixed-grip hardware is a nostalgia tribute.
- **Lift Blog (liftblog.com, Peter Landsman)** — the lift-engineering reference behind the lift
  family: tower, terminal and tram designs were studied against its photo essays (notably the
  Peak 2 Peak Gondola and the Jackson Hole Aerial Tram pages). Photos were used as reference only;
  nothing from the site is redistributed.
- Built with Claude (Anthropic) doing the heavy lifting on decompilation, prefab surgery,
  and procedural asset generation.
- **tomkpunkt — ParkManager** (Paradox Mods 161179, GPL-3.0) — studied 2026-09-30 as a research
  reference for programmatic net/object creation, ordered cleanup and Gameface UI patterns; no code
  copied, no runtime dependency (docs/research/parkmanager-dissection.md).
- **klyte45 — Write Everywhere** (Paradox Mods 92908) — optional enhancement: if installed, the Welcome
  Sign shows the ski area's name through a layout file we ship (`K45_WE/`), and the lodge and school
  name boards become writable through the same folder; nothing of Write Everywhere is included and
  the mod never requires it.
- **Lettering** — the sign sets (lodge, school, patrol and gate boards) carry text rasterised at build
  time from typefaces installed on the build machine (Bookman Old Style Bold for the resort-identity
  boards, Franklin Gothic Demi Cond for the utility boards). Only the rendered textures ship; no font
  file is included or required. The lift boards are lettered with Blender's built-in font.

## Third-party 3D models (CC BY 4.0)

The following models were used as modelling references and, where stated, as mesh sources. Each is
licensed under Creative Commons Attribution 4.0 (https://creativecommons.org/licenses/by/4.0/) and
was modified for Cities: Skylines II (re-scaled, re-topologised, re-UV'd and re-textured; see
`assets/external/*/SOURCE.md` for exactly what was reused).

- **"Chair Lift" by Kagelok** — https://sketchfab.com/3d-models/chair-lift-afd305cdd4cb450f80b59f24b4b3e236 —
  CC BY 4.0. Planned (nothing derived ships yet): tower sheave-train and catwalk geometry adapted;
  chair, grip and terminal proportions traced.
- **"Ski gongola and lift station detailed drafts" by Wilin_Amaurea** —
  https://sketchfab.com/3d-models/ski-gongola-and-lift-station-detailed-drafts-a8fabd3e955643a2a9e9f09bd46635db —
  CC BY 4.0. Planned (nothing derived ships yet): gondola cabin shell adapted; hanger, carriage and
  station layout used as reference.
- Tiling materials: CC0 sets from ambientCG (https://ambientcg.com), no attribution required.
