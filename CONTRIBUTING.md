# Contributing: comment style

Audience: a senior C# / TypeScript programmer who does not know this project's history. Comments explain WHY, never WHAT.

## Keep (shortened)
- Why the code is the way it is when the code cannot say it: engine quirks, workarounds, invariants, ordering
  constraints, threading / Burst / ECS sync points, units and frames of reference, the reason for a magic number.
- Engine citations that justify non-obvious code: keep `File.cs:line` (decompile baseline 1.6.0f1), one per claim,
  no prose retelling of the engine code.
- `Mod.cs` system registration phase / anchor comments (load-bearing: they say why a system runs where it does).
- The log-discipline rule in the ChairPinSystem header (CLAUDE.md points to it), in a few lines.
- Product rules the code enforces, stated as the rule ("Trams keep running in summer; other lifts close with no open
  run"), not as who decided it or when.
- `///` summaries on types and non-obvious public members: one line where the name is not enough.

## Remove
- Ticket ids (T123), DECISIONS numbers, round / session / agent names, dates, people ("the owner ruled"), field-log
  timestamps, "was X, now Y" history, "Phase B", "round 2". Exception: keep a reference only when the reason itself
  is too long for the code and lives in one doc; then cite the doc path, not a ticket.
- Anything a senior programmer reads directly from the code: restated conditions, loop descriptions, parameter
  echoes, "returns the value", section banners that repeat the method name, commented-out code.
- Multi-paragraph file headers: cut to the purpose (1-3 lines) plus the non-obvious constraints.

## Long engine explanations
- A file header stays at 3-8 lines. An explanation of engine behavior longer than that (with its `File.cs:line` citations)
  moves to `mod/SkiResortMod/EngineNotes/<SourceFileName>.md` (tracked, ships in the public repo); the header keeps the
  purpose, the one-line constraints and `// Engine notes: EngineNotes/<SourceFileName>.md`. Inline comments keep their
  one-line citation where the code depends on it.
- Never point code at `docs/` (gitignored: the link is dead in the public repo). A pointer to `docs/research/...` either
  becomes an EngineNotes note (the part the code needs) or is dropped.

## Form
- C#: `//` line comments, sentence case, no trailing period needed on fragments; `///` only for summaries.
- Length: most comments one line; a header rarely over 8 lines.
- US English (`tools/check-us-english.js`).

## Gate
- A comment pass changes comments only: `dotnet run --project tools/comment-diff` (C#) and, from mod/SkiResortUI,
  `node tools/comment-diff-ts.js` (TS / JS) must PASS against the commit before the pass.
