/*
* tools/test-gate-fixtures.js - deliberately broken fixtures. They prove that the check-bundle gate tightenings FAIL.
* Each fixture copies the real C# tree (or src/) into the OS temp dir and applies ONE fault. It runs
* `node tools/check-bundle.js --contract-only` against the copy (--cs-dir= / --src=). It expects exit 1 plus the rule's message.
* A baseline run on the unmodified copies must PASS first. Then a fixture cannot fail for an unrelated reason.
*   R1   a constructed binding that is never passed to AddBinding( (drop `AddBinding(m_History);`)
*   R2   a Write(...) of the wrong value kind (a bool written for RunRow.kind, a number)
*   R11  an always-mounted file (hub/HubRoot.tsx) importing a panel component (StatusBlock)
*   R15  a hub cs2/ui Button without focusKey={FOCUS_DISABLED} (the Runs view piste-map toggle)
*   R19  a plain element (<span>) as a LocText argument through a variable (the Guests view lodge name)
* Nothing under mod/ or src/ changes. The faults live only in the temp copies.
* Usage: node tools/test-gate-fixtures.js      -> `test-gate-fixtures: N fixture(s), failures=0` (exit 0)
*/
"use strict";

const fs = require("fs");
const os = require("os");
const path = require("path");
const { spawnSync } = require("child_process");

const ROOT = path.resolve(__dirname, "..");
const CS_REAL = path.resolve(ROOT, "..", "SkiResortMod");
const SRC_REAL = path.join(ROOT, "src");
const GATE = path.join(__dirname, "check-bundle.js");

function copyTree(from, to, filter) {
  fs.mkdirSync(to, { recursive: true });
  for (const e of fs.readdirSync(from, { withFileTypes: true })) {
    if (e.isDirectory()) {
      if (/^(bin|obj|\.vs|node_modules|Library)$/i.test(e.name)) continue;
      copyTree(path.join(from, e.name), path.join(to, e.name), filter);
    } else if (filter(e.name)) {
      fs.copyFileSync(path.join(from, e.name), path.join(to, e.name));
    }
  }
}

function edit(file, from, to) {
  let s = fs.readFileSync(file, "utf8");
  const crlf = s.includes("\r\n");
  if (crlf) s = s.replace(/\r\n/g, "\n");
  if (!s.includes(from)) throw new Error(`fixture setup: "${from.slice(0, 60)}" not found in ${file}`);
  s = s.replace(from, () => to);
  fs.writeFileSync(file, crlf ? s.replace(/\n/g, "\r\n") : s);
}

function runGate(csDir, srcDir) {
  const args = [GATE, "--contract-only", `--cs-dir=${csDir}`, `--src=${srcDir}`];
  const r = spawnSync(process.execPath, args, { encoding: "utf8", cwd: ROOT });
  return { code: r.status, text: (r.stdout || "") + (r.stderr || "") };
}

function main() {
  const tmp = fs.mkdtempSync(path.join(os.tmpdir(), "gate-fixtures-"));
  let checks = 0;
  const failures = [];
  const expect = (ok, what) => { checks++; if (!ok) failures.push(what); };
  try {
    const fresh = (name) => {
      const dir = path.join(tmp, name);
      const cs = path.join(dir, "cs");
      const src = path.join(dir, "src");
      copyTree(CS_REAL, cs, (f) => /\.cs$/i.test(f));
      copyTree(SRC_REAL, src, () => true);
      return { cs, src, hub: path.join(cs, "Systems", "SkiResortHubUISystem.cs") };
    };

    const base = fresh("baseline");
    const b = runGate(base.cs, base.src);
    expect(b.code === 0 && /CONTRACT PASS/.test(b.text), `baseline copy did not pass (exit ${b.code}): ${b.text.split("\n").filter((l) => /contract:|error|FAIL/i.test(l)).slice(0, 4).join(" | ")}`);

    const f1 = fresh("r1");
    edit(f1.hub, "AddBinding(m_History);", "/* fixture: AddBinding dropped */");
    const r1 = runGate(f1.cs, f1.src);
    expect(r1.code !== 0 && /never passes it to AddBinding\(/.test(r1.text) && /"history"/.test(r1.text), `R1 fixture did not fail with the AddBinding message (exit ${r1.code})`);

    const f2 = fresh("r2");
    edit(f2.hub, 'writer.PropertyName("kind");\n                writer.Write((int)r.Kind);', 'writer.PropertyName("kind");\n                writer.Write(r.Bound);');
    const r2 = runGate(f2.cs, f2.src);
    expect(r2.code !== 0 && /writes "kind" with Write\(r\.Bound\), a bool, but TS ResortRow\.kind is .* \(a number\)/.test(r2.text), `R2 fixture did not fail with the value-kind message (exit ${r2.code})`);

    const f3 = fresh("r11");
    const root = path.join(f3.src, "hub", "HubRoot.tsx");
    fs.writeFileSync(root, 'import { StatusBlock } from "./StatusBlock";\n' + fs.readFileSync(root, "utf8"));
    const r3 = runGate(f3.cs, f3.src);
    expect(r3.code !== 0 && /always-mounted src\/hub\/HubRoot\.tsx imports from the panel file src\/hub\/StatusBlock\.tsx/.test(r3.text), `R11 fixture did not fail with the always-mounted import message (exit ${r3.code})`);

    const f4 = fresh("r19");
    edit(path.join(f4.src, "hub", "GuestsView.tsx"), "            : t(tier);", "            : <span>{t(tier)}</span>;");
    const r4 = runGate(f4.cs, f4.src);
    expect(r4.code !== 0 && /<span> is an argument of <LocText> .*rule R19/.test(r4.text), `R19 fixture did not fail with the loc-argument message (exit ${r4.code})`);

    // R15 hub-wide: the baseline proves that the Runs view's piste-map toggle passes WITH focusKey. This copy drops it
    const f5 = fresh("r15");
    edit(path.join(f5.src, "hub", "RunsView.tsx"), "<Button focusKey={FOCUS_DISABLED} theme={{ button: styles.pisteToggle }}",
      "<Button theme={{ button: styles.pisteToggle }}");
    const r5 = runGate(f5.cs, f5.src);
    expect(r5.code !== 0 && /src\/hub\/RunsView\.tsx:\d+: <Button> \(cs2\/ui Button\) without focusKey=\{FOCUS_DISABLED\}.*mouse-only.*rule R15/.test(r5.text),
      `R15 fixture did not fail with the hub-wide focusKey message (exit ${r5.code})`);
  } finally {
    try { fs.rmSync(tmp, { recursive: true, force: true }); } catch { /* temp dir: leave it */ }
  }
  console.log(`test-gate-fixtures: ${checks} fixture check(s), failures=${failures.length}`);
  for (const f of failures) console.log("  FAIL: " + f);
  process.exit(failures.length ? 1 : 0);
}

main();
