// check-no-user-paths.js — fails when a tracked (or staged) file contains an absolute Windows user path
// (C:\Users\... or C:/Users/...). Release blocker found 2026-10-22: generators wrote the author's home folder
// into committed manifests. Paths in text must be repo-relative or %USERPROFILE% / %TEMP% placeholders.
// Usage: node tools/check-no-user-paths.js   (exit 1 + one line per hit)
const { execFileSync } = require("child_process");
const fs = require("fs");
const path = require("path");

const root = path.resolve(__dirname, "..");
const files = execFileSync("git", ["ls-files", "-z", "--cached", "--others", "--exclude-standard"], { cwd: root })
  .toString("utf8").split("\0").filter(Boolean);
const re = /C:[\\/]+Users[\\/]/i;
const self = "tools/check-no-user-paths.js";
let hits = 0;
for (const f of files) {
  if (f === self) continue;
  const p = path.join(root, f);
  let buf;
  try { buf = fs.readFileSync(p); } catch { continue; }
  if (buf.includes(0)) continue; // binary
  const lines = buf.toString("utf8").split(/\r?\n/);
  for (let i = 0; i < lines.length; i++) {
    if (re.test(lines[i])) { console.log(`${f}:${i + 1}: absolute user path`); hits++; }
  }
}
console.log(`check-no-user-paths: ${files.length} file(s) scanned, ${hits} hit(s) - ${hits ? "FAIL" : "PASS"}`);
process.exit(hits ? 1 : 0);
