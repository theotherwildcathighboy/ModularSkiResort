/*
 * tools/check-cok.js - the packaging check of docs/RELEASE-CHECKLIST.md section 7 (release-prep, 2026-10-22). Written for this repo (MIT). Reads only.
 *
 * Opens a Paradox Mods .cok (a plain ZIP: the game's own container, entries "<Name>.Prefab" and "<Name>.Prefab.cid" side by side) and checks it
 * against tools/shipped-prefabs.json, the frozen list of our hand-made prefabs:
 *   MISSING   a shipped prefab with no "<name>.Prefab" entry in the .cok. This is the exact failure the checklist fears: the Editor package
 *             step left a hand-made prefab out, so players would see "Unknown prefab ID".
 *   CID       the entry is there, but its .cid content id differs from the frozen "cid"
 *   EXTRA     a .Prefab entry in the .cok that is not in the frozen list (information only). The list does not track generated prefabs.
 * Exit 0 when nothing is MISSING and no CID differs. Exit 1 otherwise. Exit 2 on a usage or read error.
 * Usage: node tools/check-cok.js "<path to the .cok>"
 *   The unlisted upload's file after you subscribe in game: %CSII_PDXMODSPATH%\pdx_mods\<ModId>_<version>\*.cok
 * Reader: minimal ZIP central-directory parser with zlib. It reads stored and deflate entries only. It has no dependencies.
 */
"use strict";
const fs = require("fs");
const path = require("path");
const zlib = require("zlib");

function readZip(file) {
  const buf = fs.readFileSync(file);
  let eocd = -1;
  for (let i = buf.length - 22; i >= Math.max(0, buf.length - 66000); i--) {
    if (buf.readUInt32LE(i) === 0x06054b50) { eocd = i; break; }
  }
  if (eocd < 0) throw new Error("not a ZIP file (no end-of-central-directory record)");
  const count = buf.readUInt16LE(eocd + 10);
  let p = buf.readUInt32LE(eocd + 16);
  const entries = new Map();
  for (let n = 0; n < count; n++) {
    if (buf.readUInt32LE(p) !== 0x02014b50) throw new Error("bad central directory entry " + n);
    const method = buf.readUInt16LE(p + 10);
    const csize = buf.readUInt32LE(p + 20);
    const nameLen = buf.readUInt16LE(p + 28);
    const extraLen = buf.readUInt16LE(p + 30);
    const commentLen = buf.readUInt16LE(p + 32);
    const local = buf.readUInt32LE(p + 42);
    const name = buf.toString("utf8", p + 46, p + 46 + nameLen);
    entries.set(name, { method, csize, local });
    p += 46 + nameLen + extraLen + commentLen;
  }
  const read = (name) => {
    const e = entries.get(name);
    if (!e) return null;
    const dataAt = e.local + 30 + buf.readUInt16LE(e.local + 26) + buf.readUInt16LE(e.local + 28);
    const raw = buf.subarray(dataAt, dataAt + e.csize);
    if (e.method === 0) return raw;
    if (e.method === 8) return zlib.inflateRawSync(raw);
    throw new Error("unsupported compression method " + e.method + " for " + name);
  };
  return { names: [...entries.keys()], read };
}

function main() {
  const file = process.argv[2];
  if (!file || !fs.existsSync(file)) {
    console.error('usage: node tools/check-cok.js "<path to the .cok>"');
    process.exit(2);
  }
  const shipped = JSON.parse(fs.readFileSync(path.join(__dirname, "shipped-prefabs.json"), "utf8")).prefabs;
  let zip;
  try { zip = readZip(file); } catch (e) { console.error("cannot read " + file + ": " + e.message); process.exit(2); }
  const prefabEntries = zip.names.filter((n) => /\.Prefab$/i.test(n));
  const have = new Set(prefabEntries.map((n) => n.toLowerCase()));
  let missing = 0, cidDiff = 0;
  for (const s of shipped) {
    const entry = s.name + ".Prefab";
    if (!have.has(entry.toLowerCase())) { console.log("MISSING " + s.name + " (" + s.type + ")"); missing++; continue; }
    const real = prefabEntries.find((n) => n.toLowerCase() === entry.toLowerCase());
    const cidBytes = zip.read(real + ".cid");
    const cid = cidBytes ? cidBytes.toString("utf8").trim().toLowerCase() : null;
    if (cid && s.cid && cid !== String(s.cid).toLowerCase()) { console.log("CID     " + s.name + " cok " + cid + " frozen " + s.cid); cidDiff++; }
  }
  const frozen = new Set(shipped.map((s) => (s.name + ".Prefab").toLowerCase()));
  const extra = prefabEntries.filter((n) => !frozen.has(n.toLowerCase()));
  for (const n of extra.slice(0, 20)) console.log("EXTRA   " + n);
  if (extra.length > 20) console.log("EXTRA   ... " + (extra.length - 20) + " more");
  console.log("shipped " + shipped.length + ", .Prefab entries in the .cok " + prefabEntries.length + ", missing " + missing + ", cid differs " + cidDiff + ", extra " + extra.length);
  console.log(missing === 0 && cidDiff === 0 ? "PASS" : "FAIL");
  process.exit(missing === 0 && cidDiff === 0 ? 0 : 1);
}

main();
