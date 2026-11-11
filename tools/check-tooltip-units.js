#!/usr/bin/env node
// check-tooltip-units.js - a ProgressTooltip in the mod uses only a unit the game itself gives a ProgressTooltip.
//
// Why: the game's fraction formatter ("value / max unit") knows only some units. Any other unit prints its raw key:
// "2658641.75 / 9000000 <area>" (the planner region, 2026-11) and "Scanning 93 / 100 <percentage>" (the planner scan,
// T155, DECISIONS 650). Vanilla uses ProgressTooltip only with "power" and "volume" (decompile 1.6.4f1:
// RaycastElectricityTooltipSystem.cs:148-160, RaycastWaterTooltipSystem.cs:115-127,
// TempRenewableElectricityProductionTooltipSystem.cs:78-83, TempWaterPumpingTooltipSystem.cs:358-363), plus its
// default "integer". For a percentage, an area or a length use an IntTooltip with that unit, as the game does.
// Run: node tools/check-tooltip-units.js   (exit 1 on a finding; preflight section 12c TOOLTIPUNITS)
"use strict";
const fs = require("fs");
const path = require("path");

const ROOT = path.resolve(__dirname, "..", "mod", "SkiResortMod");
const ALLOWED = new Set(["power", "volume", "integer"]);

function walk(dir, out) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    if (e.name === "bin" || e.name === "obj") continue;
    const p = path.join(dir, e.name);
    if (e.isDirectory()) walk(p, out);
    else if (e.name.endsWith(".cs")) out.push(p);
  }
  return out;
}

let found = 0, checked = 0;
for (const file of walk(ROOT, [])) {
  const text = fs.readFileSync(file, "utf8");
  const re = /new\s+ProgressTooltip\s*\{([^}]*)\}/g;
  let m;
  while ((m = re.exec(text))) {
    checked++;
    const unit = /unit\s*=\s*"([^"]+)"/.exec(m[1]);
    const u = unit ? unit[1] : "integer";
    if (!ALLOWED.has(u)) {
      const line = text.slice(0, m.index).split("\n").length;
      console.log(`${path.relative(path.resolve(__dirname, ".."), file)}:${line} ProgressTooltip with unit "${u}" prints its raw key; use an IntTooltip with that unit`);
      found++;
    }
  }
}
if (found) process.exit(1);
console.log(`check-tooltip-units: ${checked} ProgressTooltip(s), all with a unit the game formats`);
