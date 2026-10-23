# Credits & Attribution

- **Modular Golf Course Pack** by joergeske (Paradox Mods ID 144768) — the modular-recreation
  concept that inspired this project's architecture. Credit only; not required.
- **Meapy — CS2-TourismOverhaul** — studied for how tourism and attractions work and for
  compatibility. Credit only; not required. The mod detects it by assembly name to show a hub
  notice and changes nothing in it.
- **Yenyang — Recolor** — the asset color convention (channel masks, light-albedo regions)
  follows the standard their mod established; its painter can paint line towers, which the game's own color tool cannot select (optional).
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
- **Lettering** — the sign sets (lodge, school, patrol and gate boards) carry text rasterized at build
  time from typefaces installed on the build machine (Bookman Old Style Bold for the resort-identity
  boards, Franklin Gothic Demi Cond for the utility boards). Only the rendered textures ship; no font
  file is included or required. The lift boards are lettered with Blender's built-in font.

## Asset inspirations

Each asset follows a real lift or building (reference only; nothing is copied):
A-frame lodge, Arapahoe Basin (Colorado); fixed-grip chairlift, the Pallavicini lift at Arapahoe Basin;
detachable quad, the Collins lift at Alta (Utah); gondola, loosely the Aspen gondola; aerial tram,
the Jackson Hole Aerial Tram (Wyoming); Grand lodge, the Peak 7 lodge at Breckenridge (Colorado);
chalet, a peak lodge at Courchevel (France); Ski School, the kids' ski school at Palisades Tahoe
(California); small patrol hut, the Lady Morgan patrol shack at Deer Valley (Utah); large patrol
hut, the patrol building at Song Mountain (New York). Source: docs-site credits page.

## Third-party 3D models (CC BY 4.0)

The following models were used as modeling references and, where stated, as mesh sources. Each is
licensed under Creative Commons Attribution 4.0 (https://creativecommons.org/licenses/by/4.0/) and
was modified for Cities: Skylines II (re-scaled, re-topologized, re-UV'd and re-textured; see
`assets/external/*/SOURCE.md` for exactly what was reused).

- **"Chair Lift" by Kagelok** — https://sketchfab.com/3d-models/chair-lift-afd305cdd4cb450f80b59f24b4b3e236 —
  CC BY 4.0. Planned (nothing derived ships yet): tower sheave-train and catwalk geometry adapted;
  chair, grip and terminal proportions traced.
- **"Ski gongola and lift station detailed drafts" by Wilin_Amaurea** —
  https://sketchfab.com/3d-models/ski-gongola-and-lift-station-detailed-drafts-a8fabd3e955643a2a9e9f09bd46635db —
  CC BY 4.0. Planned (nothing derived ships yet): gondola cabin shell adapted; hanger, carriage and
  station layout used as reference.
- Tiling materials: CC0 sets from ambientCG (https://ambientcg.com), no attribution required.
