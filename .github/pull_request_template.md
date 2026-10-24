## What and why

<!-- One or two sentences. Link the forum post or Discord thread if this fixes a player report. -->

## Checks

CI runs the game-free test hosts and the text gates. Run these locally (they need the game or Blender):

- [ ] `dotnet build -t:Compile` in `mod/SkiResortMod`: 0 warnings, 0 errors (no other flags: a full build deploys into the game)
- [ ] `tests/SaveBatch` green (reads the compiled DLL)
- [ ] UI: `npm run typecheck` and `npm run build` in `mod/SkiResortUI`; `contract.lock` unchanged, or the bump explained
- [ ] Prefab edits: `tools/preflight.ps1` 0 FAIL / 0 WARN and `tools/sync-gamedata.ps1` run
- [ ] Blender generator edits: the matching `assets/blender/tests/test_*.py` green
- [ ] Tested in game on a new city (say what you checked)
