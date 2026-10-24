// check-no-user-paths.js - fails when a tracked (or staged) file contains
//   (1) an absolute Windows user path (C:\Users\... or C:/Users/...). Paths in text must be repo-relative or %USERPROFILE% /
//       %TEMP% placeholders;
//   (2) the owner's first name or surname. Text in tracked files states the rule or fact, never who decided it ("the owner"
//       names a role where the role matters); credits and the license holder use the account name.
// The name is assembled from parts so this file never spells it out.
// Usage: node tools/check-no-user-paths.js [--self-test]   (exit 1 + one line per hit)
const { execFileSync } = require("child_process");
const fs = require("fs");
const path = require("path");

const root = path.resolve(__dirname, "..");
const self = "tools/check-no-user-paths.js";
const FIRST = "Rich" + "ard", LAST = "Ma" + "ble";
const reUserPath = /C:[\\/]+Users[\\/]/i;
const reName = new RegExp("\\b(" + FIRST + "|" + LAST + "\\b)", "i");

function problems(line) {
  const out = [];
  if (reUserPath.test(line)) out.push("absolute user path");
  if (reName.test(line)) out.push("the owner's name (state the rule, not who decided it; \"the owner\" where a role matters)");
  return out;
}

if (process.argv.includes("--self-test")) {
  const must = [
    ["// C:\\Users\\x\\y", 1],
    [`// per ${FIRST}: keep it`, 1],
    [`// ${FIRST}'s verdict`, 1],
    [`// ${FIRST}${LAST}`, 1],
    [`by ${FIRST.toLowerCase()} ${LAST.toLowerCase()}`, 1],
    ["Copyright (c) 2026 theotherwildcathighboy", 0],
    ["// the owner decides", 0],
    ["// a claimable lane", 0],
  ];
  let bad = 0;
  for (const [l, n] of must) {
    const got = problems(l).length;
    if (got !== n) { console.log(`self-test FAIL: ${l} -> ${got} problem(s), expected ${n}`); bad++; }
  }
  console.log(`check-no-user-paths self-test: ${bad ? "FAIL" : "PASS"}`);
  process.exit(bad ? 1 : 0);
}

const files = execFileSync("git", ["ls-files", "-z", "--cached", "--others", "--exclude-standard"], { cwd: root, maxBuffer: 1 << 28 })
  .toString("utf8").split("\0").filter(Boolean);
let hits = 0;
for (const f of files) {
  if (f === self) continue;
  const p = path.join(root, f);
  let buf;
  try { buf = fs.readFileSync(p); } catch { continue; }
  if (buf.includes(0)) continue; // binary
  const lines = buf.toString("utf8").split(/\r?\n/);
  for (let i = 0; i < lines.length; i++) {
    for (const what of problems(lines[i])) { console.log(`${f}:${i + 1}: ${what}`); hits++; }
  }
}
console.log(`check-no-user-paths: ${files.length} file(s) scanned, ${hits} hit(s) - ${hits ? "FAIL" : "PASS"}`);
process.exit(hits ? 1 : 0);
