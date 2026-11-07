# Contributing: comment style

Audience: a senior C# / TypeScript programmer who does not know this project's history. Comments explain WHY, never WHAT.

## Keep (shortened)
- Why the code is the way it is, when the code cannot say it. This covers engine quirks, workarounds, invariants, ordering
  constraints, threading / Burst / ECS sync points, units and frames of reference, and the reason for a magic number.
- Engine citations that justify non-obvious code. Keep `File.cs:line` (decompile baseline 1.6.0f1), one per claim.
  Do not retell the engine code in prose.
- `Mod.cs` system registration phase / anchor comments. They are load-bearing: they say why a system runs where it does.
- The log-discipline rule in the ChairPinSystem header, in a few lines.
- Product rules the code enforces. State the rule ("Trams keep running in summer; other lifts close with no open
  run"), not who decided it or when.
- `///` summaries on types and non-obvious public members. Use one line where the name is not enough.

## Remove
- Ticket ids (T123), DECISIONS numbers, round / session / agent names, dates, people ("the owner ruled"), field-log
  timestamps, "was X, now Y" history, "Phase B", "round 2". Exception: keep a reference only when the reason itself
  is too long for the code and lives in one doc. Then cite the doc path, not a ticket.
- Anything a senior programmer reads directly from the code: restated conditions, loop descriptions, parameter
  echoes, "returns the value", section banners that repeat the method name, commented-out code.
- Multi-paragraph file headers. Cut them to the purpose (1-3 lines) plus the non-obvious constraints.

## Long engine explanations
- A file header stays at 3-8 lines. Move an explanation of engine behavior that is longer than that (with its `File.cs:line`
  citations) to `mod/SkiResortMod/EngineNotes/<SourceFileName>.md`. That file is tracked and ships in the public repo.
  The header keeps the purpose, the one-line constraints and `// Engine notes: EngineNotes/<SourceFileName>.md`.
  Inline comments keep their one-line citation where the code depends on it.
- Never point code at `docs/`. It is gitignored, so the link is dead in the public repo. Replace a pointer to
  `docs/research/...` with an EngineNotes note (the part the code needs), or drop the pointer.

## Form
- C#: use `//` line comments in sentence case. A fragment needs no trailing period. Use `///` only for summaries.
- Length: most comments take one line. A header is rarely over 8 lines.
- US English (`tools/check-us-english.js`).

## Gate
- A comment pass changes comments only. Run `dotnet run --project tools/comment-diff` (C#) and, from mod/SkiResortUI,
  `node tools/comment-diff-ts.js` (TS / JS). Both must PASS against the commit before the pass.
