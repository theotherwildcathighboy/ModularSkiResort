// Gate: no system registered in a phase after ModificationSystem tags an entity Deleted. CleanUpSystem destroys Deleted entities right
// after MainLoop, so vanilla's Modification systems never see such a tag: an area stays in Game.Areas.SearchSystem's tree and a later Burst
// job crashes on it (field test 2026-10-31). UIUpdate is the phase a request from the hub lands in; queue it for PostTool instead.
"use strict";
const fs = require("fs");
const path = require("path");

const root = path.join(__dirname, "..", "mod", "SkiResortMod");
const late = /Update(?:At|Before|After)<(\w+)(?:,\s*[\w.]+)?>\(\s*SystemUpdatePhase\.(UIUpdate|UITooltip|Rendering|PreCulling|Cleanup|LateUpdate)\b/g;
const banned = /AddComponent<Deleted>|DeleteLikeBulldozer\(|\.DeleteTrack\(/;

const mod = fs.readFileSync(path.join(root, "Mod.cs"), "utf8");
const systems = new Map();
for (const m of mod.matchAll(late)) systems.set(m[1], m[2]);

function walk(dir, out) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) walk(p, out);
    else if (e.name.endsWith(".cs")) out.push(p);
  }
  return out;
}

const failures = [];
for (const file of walk(root, [])) {
  const owner = path.basename(file).split(".")[0];
  if (!systems.has(owner)) continue;
  fs.readFileSync(file, "utf8").split(/\r?\n/).forEach((line, i) => {
    if (!line.trim().startsWith("//") && banned.test(line))
      failures.push(`${path.relative(root, file)}:${i + 1} (${systems.get(owner)}): ${line.trim()}`);
  });
}

if (failures.length) {
  console.error("check-ui-phase-deletes: FAIL, a Deleted tag after ModificationSystem:\n  " + failures.join("\n  "));
  process.exit(1);
}
console.log(`check-ui-phase-deletes: PASS (${systems.size} late-phase systems scanned)`);
