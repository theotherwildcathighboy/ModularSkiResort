// check-no-user-paths.js - fails when a tracked (or staged) file contains
//   (1) an absolute Windows user path (C:\Users\... or C:/Users/...). Release blocker found 2026-10-22: generators wrote the
//       author's home folder into committed manifests. Paths in text must be repo-relative or %USERPROFILE% / %TEMP% placeholders;
//   (2) the word "Richard": text in tracked files states the rule or fact, never who decided it ("the owner" names a role where the
//       role matters). The full-name credit lines in CREDIT_ALLOWLIST below are the only exceptions until the history rewrite
//       replaces them; an allowlist entry that matches no line is itself a failure, so the list cannot go stale.
// Usage: node tools/check-no-user-paths.js [--self-test]   (exit 1 + one line per hit)
const { execFileSync } = require("child_process");
const fs = require("fs");
const path = require("path");

const root = path.resolve(__dirname, "..");
const self = "tools/check-no-user-paths.js";
const reUserPath = /C:[\\/]+Users[\\/]/i;
const reName = /\bRichard\b/;

// [file, text]: a line of that file that contains `text` may hold the word. Credit lines carry the full name; the two
// others are a credit string wrapped across two lines and a C# runtime message (not a comment, so a comment-only pass left it).
const CREDIT_ALLOWLIST = [
  ["LICENSE", "theotherwildcathighboy"],
  ["assets/LICENSE", "theotherwildcathighboy"],
  ["assets/LICENSE", '("Modular Ski Resort by Richard'],
  ["assets/external/kagelok-chair-lift/SOURCE.md", "theotherwildcathighboy"],
  ["assets/external/wilin-gondola/SOURCE.md", "theotherwildcathighboy"],
  ["mod/SkiResortMod/Properties/PublishConfiguration.xml", "theotherwildcathighboy"],
  ["mod/SkiResortUI/mod.json", "theotherwildcathighboy"],
  ["release/DESCRIPTION.md", "theotherwildcathighboy"],
];

function problems(file, line, used) {
  const out = [];
  if (reUserPath.test(line)) out.push("absolute user path");
  if (reName.test(line)) {
    const i = CREDIT_ALLOWLIST.findIndex(([f, t]) => f === file && line.includes(t));
    if (i >= 0) used.add(i);
    else out.push('the word "Richard" (state the rule, not who decided it; "the owner" where a role matters)');
  }
  return out;
}

if (process.argv.includes("--self-test")) {
  const used = new Set();
  const must = [
    ["a.cs", "// C:\\Users\\x\\y", 1],
    ["a.cs", "// per Richard: keep it", 1],
    ["a.cs", "// Richard's verdict", 1],
    ["LICENSE", "Copyright (c) 2026 theotherwildcathighboy", 0],
    ["a.cs", "// Copyright theotherwildcathighboy", 1], // the full name is allowed only on the listed files
    ["a.cs", "// the owner decides", 0],
    ["a.cs", "// theotherwildcathighboy, richard", 0],
  ];
  let bad = 0;
  for (const [f, l, n] of must) {
    const got = problems(f, l, used).length;
    if (got !== n) { console.log(`self-test FAIL: ${f}: ${l} -> ${got} problem(s), expected ${n}`); bad++; }
  }
  console.log(`check-no-user-paths self-test: ${bad ? "FAIL" : "PASS"}`);
  process.exit(bad ? 1 : 0);
}

const files = execFileSync("git", ["ls-files", "-z", "--cached", "--others", "--exclude-standard"], { cwd: root, maxBuffer: 1 << 28 })
  .toString("utf8").split("\0").filter(Boolean);
const used = new Set();
let hits = 0;
for (const f of files) {
  if (f === self) continue;
  const p = path.join(root, f);
  let buf;
  try { buf = fs.readFileSync(p); } catch { continue; }
  if (buf.includes(0)) continue; // binary
  const lines = buf.toString("utf8").split(/\r?\n/);
  for (let i = 0; i < lines.length; i++) {
    for (const what of problems(f, lines[i], used)) { console.log(`${f}:${i + 1}: ${what}`); hits++; }
  }
}
CREDIT_ALLOWLIST.forEach(([f, t], i) => {
  if (!used.has(i)) { console.log(`${self}: stale allowlist entry (matches no line): ${f} ~ ${t}`); hits++; }
});
console.log(`check-no-user-paths: ${files.length} file(s) scanned, ${hits} hit(s) - ${hits ? "FAIL" : "PASS"}`);
process.exit(hits ? 1 : 0);
