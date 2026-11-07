# ste-lint (vendored)

`ste-lint.py` is the structural ASD-STE100 linter from github.com/danyuchn/asd-ste100-skill (commit f38838a, MIT, see LICENSE).
`tools/check-ste.js` runs it as the STE ratchet gate (DECISIONS 614). `baseline.json` holds the allowed hard-finding count per target.
Lower it with `node tools/check-ste.js --write` after a cleanup. Never raise a number by hand.

Local changes to the upstream file: one comment word ("initialisms" -> "abbreviations", line 152) for the US English gate.
