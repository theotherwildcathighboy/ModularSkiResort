/*
 * tools/strip-pdb-path.js - shortens the PDB path in a PE file's CodeView (RSDS) debug record to the bare file name, in place.
 * Written for this repo (MIT). Release review 2026-10-30, B2. mod/SkiResortMod/SkiPublish.targets runs it on the deploy folder's
 * DLLs in publish builds only; the build output in bin\ keeps the original files.
 *
 * Why: the CS2 ModPostProcessor runs the Burst compiler with its temp folder fixed under the LocalApplicationData known folder
 * (%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\.cache\Modding\Burst, PathSet.TempDir; no argument or variable
 * changes it), and the linker writes that absolute PDB path into <name>_win_x86_64.dll. The path holds the Windows profile folder
 * name. Every subscribed Burst mod in the local cache carries the same record. Unity's own AOT pipeline avoids it with the linker
 * option PdbAltPath (com.unity.burst Editor/BurstAotCompiler.cs:758-761), which the post-processor does not pass. This does the same
 * after the link: the file size, the code, the GUID and the age (what a debugger matches a PDB by) stay; only the path string
 * changes, and the Windows loader never reads it. A non-zero PE checksum is recomputed.
 *
 * Usage: node tools/strip-pdb-path.js <file.dll> [...]
 * Exit 0 = shortened, or nothing to do; 1 = not a PE file or an unexpected debug record (that file is not written); 2 = usage.
 * Prints file names only, never the old path.
 */
"use strict";
const fs = require("fs");
const path = require("path");

const IMAGE_DEBUG_TYPE_CODEVIEW = 2;

function fail(msg) {
  const e = new Error(msg);
  e.expected = true;
  throw e;
}

function parsePe(buf) {
  if (buf.length < 64 || buf.readUInt16LE(0) !== 0x5a4d) fail("not a PE file (no MZ header)");
  const pe = buf.readUInt32LE(0x3c);
  if (pe + 24 > buf.length || buf.readUInt32LE(pe) !== 0x00004550) fail("not a PE file (no PE signature)");
  const numSections = buf.readUInt16LE(pe + 6);
  const optSize = buf.readUInt16LE(pe + 20);
  const opt = pe + 24;
  const magic = buf.readUInt16LE(opt);
  if (magic !== 0x10b && magic !== 0x20b) fail(`unknown optional header magic 0x${magic.toString(16)}`);
  const numDirs = buf.readUInt32LE(opt + (magic === 0x20b ? 108 : 92));
  const dirs = opt + (magic === 0x20b ? 112 : 96);
  const sections = [];
  for (let i = 0; i < numSections; i++) {
    const s = opt + optSize + i * 40;
    if (s + 40 > buf.length) fail("the section table runs past the end of the file");
    sections.push({ vsize: buf.readUInt32LE(s + 8), va: buf.readUInt32LE(s + 12), rawSize: buf.readUInt32LE(s + 16), raw: buf.readUInt32LE(s + 20) });
  }
  const rvaToOffset = (rva) => {
    for (const s of sections) if (rva >= s.va && rva < s.va + Math.max(s.vsize, s.rawSize)) return rva - s.va + s.raw;
    return -1;
  };
  const debugRva = numDirs > 6 ? buf.readUInt32LE(dirs + 48) : 0;
  const debugSize = numDirs > 6 ? buf.readUInt32LE(dirs + 52) : 0;
  return { checksumOffset: opt + 64, debugRva, debugSize, rvaToOffset };
}

// Every CodeView record: { start, nul } = the PDB path's first byte and its terminating NUL (file offsets).
function codeViewPaths(buf, pe) {
  const out = [];
  if (!pe.debugRva || !pe.debugSize) return out;
  const dir = pe.rvaToOffset(pe.debugRva);
  if (dir < 0 || dir + pe.debugSize > buf.length) fail("the debug directory lies outside the file");
  for (let e = dir; e + 28 <= dir + pe.debugSize; e += 28) {
    if (buf.readUInt32LE(e + 12) !== IMAGE_DEBUG_TYPE_CODEVIEW) continue;
    const size = buf.readUInt32LE(e + 16);
    let ptr = buf.readUInt32LE(e + 24);
    if (!ptr) ptr = pe.rvaToOffset(buf.readUInt32LE(e + 20));
    if (ptr <= 0 || ptr + size > buf.length) fail("a CodeView record lies outside the file");
    if (size < 25 || buf.toString("latin1", ptr, ptr + 4) !== "RSDS") fail("a CodeView record is not RSDS (not handled)");
    const start = ptr + 24;
    const nul = buf.indexOf(0, start);
    if (nul < 0 || nul >= ptr + size) fail("a CodeView PDB path is not NUL-terminated inside its record");
    out.push({ start, nul });
  }
  return out;
}

// The PE image checksum (ImageHlp CheckSumMappedFile): 16-bit sum with carry folding, the checksum field counted as zero,
// plus the file length.
function peChecksum(buf, checksumOffset) {
  let sum = 0;
  for (let i = 0; i < buf.length; i += 2) {
    if (i === checksumOffset || i === checksumOffset + 2) continue;
    sum += i + 1 < buf.length ? buf.readUInt16LE(i) : buf[i];
    sum = (sum & 0xffff) + (sum >>> 16);
  }
  sum = (sum & 0xffff) + (sum >>> 16);
  return (sum + buf.length) >>> 0;
}

function strip(file) {
  const buf = fs.readFileSync(file);
  const pe = parsePe(buf);
  const records = codeViewPaths(buf, pe);
  if (!records.length) return "no CodeView record, nothing to do";
  const notes = [];
  let changed = 0;
  for (const { start, nul } of records) {
    const old = buf.toString("utf8", start, nul);
    const bare = old.split(/[\\/]/).pop();
    if (bare === old) { notes.push(`"${bare}" is already a bare file name`); continue; }
    const repl = Buffer.from(bare, "utf8");
    repl.copy(buf, start);
    buf.fill(0, start + repl.length, nul + 1);
    changed++;
    notes.push(`PDB path shortened to "${bare}" (${nul - start - repl.length} byte(s) cleared)`);
  }
  if (!changed) return notes.join("; ");
  const oldSum = buf.readUInt32LE(pe.checksumOffset);
  if (oldSum !== 0) {
    buf.writeUInt32LE(peChecksum(buf, pe.checksumOffset), pe.checksumOffset);
    notes.push("PE checksum recomputed");
  }
  fs.writeFileSync(file, buf);
  // Read back: every CodeView path must now be a bare file name.
  const again = fs.readFileSync(file);
  for (const { start, nul } of codeViewPaths(again, parsePe(again))) {
    if (/[\\/]/.test(again.toString("utf8", start, nul))) fail("read-back still finds a directory in a CodeView path");
  }
  return notes.join("; ");
}

const files = process.argv.slice(2);
if (!files.length || files.includes("--help")) {
  console.log("Usage: node tools/strip-pdb-path.js <file.dll> [...]");
  process.exit(2);
}
let bad = 0;
for (const f of files) {
  try {
    console.log(`strip-pdb-path: ${path.basename(f)}: ${strip(f)}`);
  } catch (err) {
    bad++;
    // fs messages quote the full path, so only the code is printed.
    const why = err.expected ? err.message : err.code ? `cannot read or write the file (${err.code})` : `malformed PE file (${err.name})`;
    console.log(`strip-pdb-path: ${path.basename(f)}: FAILED - ${why}`);
  }
}
process.exit(bad ? 1 : 0);
