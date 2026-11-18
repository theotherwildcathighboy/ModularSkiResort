/*
 * tools/check-cok.js - the packaging check of docs/RELEASE-CHECKLIST.md section 7 (release-prep, 2026-10-22). Written for this repo (MIT). Reads only.
 *
 * Opens a Paradox Mods .cok (a plain ZIP: the game's own container, entries "<Name>.Prefab" and "<Name>.Prefab.cid" side by side) and checks it
 * against tools/shipped-prefabs.json, the frozen list of our hand-made prefabs:
 *   MISSING   a shipped prefab with no "<name>.Prefab" entry in the .cok. This is the exact failure the checklist fears: the Editor package
 *             step left a hand-made prefab out, so players would see "Unknown prefab ID".
 *   CID       the entry is there, but its .cid content id differs from the frozen "cid"
 *   ICON      a hub icon id that no ".cid" entry of the .cok carries: every gamedata-mirror/ImportedData/SkiResortIcons/*Card.png.cid and every
 *             assetdb://global/<id> of mod/SkiResortUI/src/hub/buildItems.ts. No prefab references the Card PNGs, so the package step may leave
 *             them out (blank hub tiles; 1.0.1 shipped without them).
 *   EXTRA     a .Prefab entry in the .cok that is not in the frozen list (information only). The list does not track generated prefabs.
 * Exit 0 when nothing is MISSING, no CID differs and no ICON is missing. Exit 1 otherwise. Exit 2 on a usage or read error.
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
  // The package step names each entry after its source file (decompile 1.6.4f1 Game.UI.Editor/PackageHelper.cs:97-98), so a prefab
  // whose library file carries a hash suffix (SkiLiftChair4_<hash>.Prefab) is found by the frozen "file" basename, not by its name.
  const entriesOf = (s) => [s.name + ".Prefab", s.file ? path.posix.basename(s.file) : null].filter(Boolean).map((n) => n.toLowerCase());
  for (const s of shipped) {
    const entry = entriesOf(s).find((n) => have.has(n));
    if (!entry) { console.log("MISSING " + s.name + " (" + s.type + ")"); missing++; continue; }
    const real = prefabEntries.find((n) => n.toLowerCase() === entry);
    const cidBytes = zip.read(real + ".cid");
    const cid = cidBytes ? cidBytes.toString("utf8").trim().toLowerCase() : null;
    if (cid && s.cid && cid !== String(s.cid).toLowerCase()) { console.log("CID     " + s.name + " cok " + cid + " frozen " + s.cid); cidDiff++; }
  }
  const frozen = new Set(shipped.flatMap(entriesOf));
  const extra = prefabEntries.filter((n) => !frozen.has(n.toLowerCase()));
  for (const n of extra.slice(0, 20)) console.log("EXTRA   " + n);
  if (extra.length > 20) console.log("EXTRA   ... " + (extra.length - 20) + " more");

  // Assets that no prefab references are not packed: the package step walks references from the prefabs (DependencyCollector.cs
  // CollectPrefabReferences: AssetReference fields + UIIconField strings). The hub's Card icons (T210) are loaded by id from buildItems.ts only,
  // so they must be checked here. Every id below must be the content of some "<asset>.cid" entry of the .cok.
  const cokIds = new Set();
  for (const n of zip.names) {
    if (!/\.cid$/i.test(n)) continue;
    const b = zip.read(n);
    if (b) cokIds.add(b.toString("utf8").trim().toLowerCase());
  }
  const root = path.join(__dirname, "..");
  const wanted = new Map(); // id -> label
  const iconDir = path.join(root, "gamedata-mirror", "ImportedData", "SkiResortIcons");
  if (fs.existsSync(iconDir)) {
    for (const f of fs.readdirSync(iconDir).filter((x) => /Card\.png\.cid$/i.test(x)).sort()) {
      wanted.set(fs.readFileSync(path.join(iconDir, f), "utf8").trim().toLowerCase(), f.replace(/\.cid$/i, ""));
    }
  }
  const cardCount = wanted.size;
  const buildItems = path.join(root, "mod", "SkiResortUI", "src", "hub", "buildItems.ts");
  let hubIds = 0;
  if (fs.existsSync(buildItems)) {
    for (const line of fs.readFileSync(buildItems, "utf8").split(/\r?\n/)) {
      for (const m of line.matchAll(/assetdb:\/\/global\/([0-9a-f]{32})/gi)) {
        const id = m[1].toLowerCase();
        if (!wanted.has(id)) { wanted.set(id, (line.match(/\/\/\s*(\S+\.png)/) || [])[1] || id); }
        hubIds++;
      }
    }
  }
  let iconMissing = 0;
  for (const [id, label] of wanted) {
    if (!cokIds.has(id)) { console.log("ICON    missing " + label + " (" + id + ")"); iconMissing++; }
  }
  console.log("hub icon ids " + wanted.size + " (" + cardCount + " Card files in the mirror, " + hubIds + " assetdb ids in buildItems.ts), missing " + iconMissing);
  if (cardCount === 0 && fs.existsSync(iconDir) === false) console.log("WARN    no SkiResortIcons folder: icon check skipped");

  console.log("shipped " + shipped.length + ", .Prefab entries in the .cok " + prefabEntries.length + ", missing " + missing + ", cid differs " + cidDiff + ", extra " + extra.length);
  const ok = missing === 0 && cidDiff === 0 && iconMissing === 0;
  console.log(ok ? "PASS" : "FAIL");
  process.exit(ok ? 0 : 1);
}

main();
