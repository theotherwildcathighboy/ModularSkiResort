// regions-from-log.js: the regions the owner scanned with the Resort Planner, read from the mod log, one line per scan:
//   <id> x,z x,z ...
// Usage: node tools/regions-from-log.js <SkiResortMod.Mod.log> <prefix> > regions.txt
// Corners follow the tool's own lines: "corner N at (x, z", "corner N moved to (x, z)", "undo ... K corner(s) left".
'use strict';
const fs = require('fs');
const [log, prefix = 'r'] = process.argv.slice(2);
let corners = [];
const out = [];
const seen = new Set();
for (const line of fs.readFileSync(log, 'utf8').split(/\r?\n/)) {
  let m;
  if ((m = line.match(/QuickRuns \[region\]: corner (\d+) at \((-?\d+), (-?\d+)/))) {
    corners = corners.slice(0, +m[1] - 1);
    corners.push([+m[2], +m[3]]);
  } else if ((m = line.match(/QuickRuns \[region\]: corner (\d+) moved to \((-?\d+), (-?\d+)\)/))) {
    corners[+m[1] - 1] = [+m[2], +m[3]];
  } else if ((m = line.match(/QuickRuns \[region\]: undo .* (\d+) corner\(s\) left/))) {
    corners = corners.slice(0, +m[1]);
  } else if (/QuickRuns \[scan\]: START \(Enter\)/.test(line) && corners.length >= 3) {
    const key = corners.map(c => c.join(',')).join(' ');
    if (!seen.has(key)) {
      seen.add(key);
      out.push(`${prefix}${out.length + 1} ${key}`);
    }
  }
}
process.stdout.write(out.join('\n') + '\n');
