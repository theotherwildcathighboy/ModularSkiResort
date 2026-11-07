// Proves that an edit changed only comments in the hub sources. For every changed .ts / .tsx / .js file under mod/SkiResortUI,
// the TypeScript printer (comments removed) must print the same text as for that file at the given git ref.
// Exit 1 on any code change.
// Usage (from mod/SkiResortUI): node tools/comment-diff-ts.js [ref=HEAD]
const { execFileSync } = require("child_process");
const ts = require("typescript");
const path = require("path");
const fs = require("fs");

const ref = process.argv[2] || "HEAD";
const root = execFileSync("git", ["rev-parse", "--show-toplevel"]).toString().trim();
const files = execFileSync("git", ["diff", "--name-only", ref, "--", "mod/SkiResortUI"], { cwd: root }).toString()
  .split("\n").map((s) => s.trim()).filter((f) => /\.(ts|tsx|js)$/.test(f) && !f.includes("/build/"));
const printer = ts.createPrinter({ removeComments: true });
function print(name, src) {
  const kind = name.endsWith(".tsx") ? ts.ScriptKind.TSX : name.endsWith(".js") ? ts.ScriptKind.JS : ts.ScriptKind.TS;
  return printer.printFile(ts.createSourceFile(name, src, ts.ScriptTarget.Latest, false, kind));
}
let bad = 0, ok = 0;
for (const rel of files) {
  const p = path.join(root, rel);
  if (!fs.existsSync(p)) { console.log(`FAIL ${rel}: deleted`); bad++; continue; }
  const before = execFileSync("git", ["show", `${ref}:${rel}`], { cwd: root, maxBuffer: 64 << 20 }).toString("utf8");
  const a = print(rel, before), b = print(rel, fs.readFileSync(p, "utf8"));
  if (a === b) { ok++; continue; }
  let i = 0; while (i < a.length && a[i] === b[i]) i++;
  console.log(`FAIL ${rel}: code differs near: ...${a.slice(Math.max(0, i - 40), i + 40)}... vs ...${b.slice(Math.max(0, i - 40), i + 40)}...`);
  bad++;
}
console.log(`comment-diff-ts: ${files.length} changed file(s) vs ${ref}: ${ok} comment-only, ${bad} with code changes - ${bad ? "FAIL" : "PASS"}`);
process.exit(bad ? 1 : 0);
