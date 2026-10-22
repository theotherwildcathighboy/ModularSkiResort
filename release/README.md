# Release staging — Paradox Mods

Everything that ships or feeds the listing lives here; nothing in this folder is loaded by
the game. Two build artifacts, uploaded as ONE hybrid Paradox Mods item (DECISIONS 278: the DLL and the .cok share one item id for the life of the mod; never split them):

1. **Asset pack** (stations, towers, chairs, lift network) — packaged and uploaded through
   the game's Editor publishing flow (produces the `.cok`). OPEN ITEM before first upload:
   verify our hand-made prefabs (stations, terminal towers, placeholder family, net prefabs)
   are included by the Editor's packaging — see DECISIONS.md "Packaging".
2. **Code mod** (`mod/SkiResortMod`) — built via the csiimod template's
   PublishNewMod/PublishNewVersion profiles; listing metadata goes in
   `Properties/PublishConfiguration.xml`, drawing on the files here.

## Contents

- `DESCRIPTION.md` — listing copy draft (short + long description). Keep in sync with feature state.
- `LISTING-NOTES-2026-10-22.md` — every claim in the listing copy with its source, what is unverified, open questions.
- `CREDITS.md` — attribution (REQUIRED before upload, per project commitment).
- `CHANGELOG.md` — player-facing version history; seeded during beta.
- `BESTPRACTICES.md` — the player guide (build order, workflow rules). Draft from
  PHASE3 §12 workflow notes + DECISIONS.md ⚖️ entries.
- `screenshots/` — curated gallery shots (Paradox listing takes several; thumbnail needed
  too, 1:1 crop recommended).

## Pre-upload checklist (draft)

- [ ] Fresh-profile test: mod pack on a clean save, no other mods, zero boot errors
- [ ] Best-practices guide finished and linked in the description
- [ ] Credits present in listing AND in-game description
- [ ] Version + changelog set; thumbnail + ≥4 screenshots
- [ ] Code mod: PublishConfiguration.xml matches DESCRIPTION.md
- [ ] Packaging open-item resolved (hand-made prefabs in the .cok)
