// Compare the two fingerprint outputs: every A line (HEAD = Phase A numerics) must appear unchanged in B (working tree);
// B may add lines only for fields A does not have. Prints the FNV-1a 64 of A's lines and of B's lines restricted to
// A's field names, per row group and overall.
"use strict";
const fs = require("fs");
const a = fs.readFileSync(process.argv[2], "utf8").split(/\r?\n/).filter(Boolean);
const b = fs.readFileSync(process.argv[3], "utf8").split(/\r?\n/).filter(Boolean);
const key = (l) => l.slice(0, l.indexOf(" = "));
const bMap = new Map(b.map((l) => [key(l), l]));
const aKeys = new Set(a.map(key));
let diff = 0, missing = 0;
for (const l of a) {
  const k = key(l);
  if (!bMap.has(k)) { missing++; console.log("MISSING in B: " + l); continue; }
  if (bMap.get(k) !== l) { diff++; console.log("DIFF: A " + l + " | B " + bMap.get(k)); }
}
const added = b.filter((l) => !aKeys.has(key(l)));
const addedFields = new Set(added.map((l) => key(l).replace(/^[A-Z]\d\.(Result|Part\d\d)\./, "$1.")));
function fnv(lines) {
  let h = 0xcbf29ce484222325n;
  const p = 0x100000001b3n;
  for (const l of lines) for (const byte of Buffer.from(l + "\n", "utf8")) { h ^= BigInt(byte); h = (h * p) & 0xffffffffffffffffn; }
  return "0x" + h.toString(16).toUpperCase().padStart(16, "0");
}
const bCommon = b.filter((l) => aKeys.has(key(l)));
for (const row of ["T1", "T2", "T3", "W1", "W2", "W3"]) {
  const ar = a.filter((l) => l.startsWith(row + "."));
  const br = bCommon.filter((l) => l.startsWith(row + "."));
  console.log(`${row}: ${ar.length} common field values; A ${fnv(ar)}  B ${fnv(br)}  ${fnv(ar) === fnv(br) ? "IDENTICAL" : "DIFFERENT"}`);
}
console.log(`all: A ${a.length} lines, B ${b.length} lines (${added.length} added: ${[...addedFields].sort().join(", ")})`);
console.log(`common ${bCommon.length}: A ${fnv(a)}  B ${fnv(bCommon)}; ${diff} differing, ${missing} missing in B`);
