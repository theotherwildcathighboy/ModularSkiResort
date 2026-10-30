// Maps pixel coordinates on a runs-first render (<id>-draw.png, or the right panel of <id>.png) to world x,z, using the
// <id>-draw.map file the rf host writes next to it. Usage: node tools/rf-pix2world.cjs <id>-draw.map [--panel] u,v u,v ...
// Prints "x,z x,z ...", ready for a regions file row (or a hole after "hole").
const fs = require('fs');
const [mapFile, ...rest] = process.argv.slice(2);
const m = JSON.parse(fs.readFileSync(mapFile, 'utf8'));
const panel = rest[0] === '--panel';
const out = (panel ? rest.slice(1) : rest).map(tok => {
  let [u, v] = tok.split(',').map(Number);
  if (panel) u -= m.panelW + m.gap;   // the right (runs-first) panel of <id>.png
  let i, j;
  switch (m.rot) {
    case 0: i = u; j = m.d - 1 - v; break;
    case 1: j = m.d - 1 - u; i = m.w - 1 - v; break;
    case 2: i = m.w - 1 - u; j = v; break;
    default: i = v; j = u; break;
  }
  return Math.round(m.ox + (m.x0 + i) * m.t) + ',' + Math.round(m.oz + (m.z0 + j) * m.t);
});
console.log(out.join(' '));
