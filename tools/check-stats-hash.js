#!/usr/bin/env node
// Every field of a hub stats row must feed its change hash: SkiResortStatsSystem publishes a row list only when the hash moves, so a
// field left out goes stale in the panel until some other field changes (the run tooltip's gun line kept "out of season" into October).
"use strict";
const fs = require("fs");
const path = require("path");

const file = path.join(__dirname, "..", "mod", "SkiResortMod", "Systems", "SkiResortStatsSystem.cs");
const src = fs.readFileSync(file, "utf8");

function blockAfter(start) {
  let i = src.indexOf("{", start), depth = 0;
  for (let j = i; j < src.length; j++) {
    if (src[j] === "{") depth++;
    else if (src[j] === "}" && --depth === 0) return src.slice(i + 1, j);
  }
  throw new Error("unbalanced block");
}

function fieldsOf(structName) {
  const m = new RegExp("(?:struct|class)\\s+" + structName + "\\b").exec(src);
  if (!m) return null;
  const names = [];
  const body = blockAfter(m.index)
    .replace(/\/\/.*$/gm, "")
    .replace(/\/\*[\s\S]*?\*\//g, "");
  // Depth 0 only: members with bodies (computed properties, methods) are not row data.
  let flat = "", depth = 0;
  for (const c of body) {
    if (c === "{") depth++;
    else if (c === "}") depth--;
    else if (depth === 0) flat += c;
  }
  for (const stmt of flat.split(";")) {
    if (stmt.includes("=>")) continue; // a computed property, not row data
    const d = /^\s*public\s+(?!static|const)(?:readonly\s+)?[\w.<>\[\], ]+?\s+(\w+(?:\s*(?:=[^,]*)?,\s*\w+)*)\s*(?:=[^,]*)?$/.exec(stmt);
    if (d) d[1].split(",").forEach((n) => names.push(n.trim().split(/\s|=/)[0]));
  }
  return names;
}

let failures = 0, checked = 0;
const re = /private static ulong (Hash\w+)\(List<(\w+)> rows\)/g;
let m;
while ((m = re.exec(src))) {
  const fields = fieldsOf(m[2]);
  if (!fields || fields.length === 0) {
    console.log(`FAIL ${m[1]}: no fields read from ${m[2]}`);
    failures++;
    continue;
  }
  const body = blockAfter(m.index);
  for (const f of fields) {
    checked++;
    if (!new RegExp("\\br\\." + f + "\\b").test(body)) {
      console.log(`FAIL ${m[1]}: ${m[2]}.${f} is not in the hash`);
      failures++;
    }
  }
}
if (checked === 0) {
  console.log("FAIL no hash functions found");
  failures++;
}
console.log(`check-stats-hash: ${checked} field(s), failures=${failures}`);
process.exit(failures ? 1 : 0);
