/*
 * tools/check-no-vanilla-art.js - no game art in the repo. The hub may REFERENCE an installed game icon by its `Media/` path (vanilla
 * first), but a copy of game art in the repo would be published with it (the docs site, the public repo at 1.0). Our art is
 * CC BY-NC-SA; the game's is not ours to license.
 *
 *   node tools/check-no-vanilla-art.js              check every tracked or new (not ignored) .svg / .png / .jpg in the repo
 *   node tools/check-no-vanilla-art.js --self-test  prove the detector catches a byte copy and a copied SVG path inside our own file
 *
 * A finding is:
 *   - a file byte-equal to a file under the installed Cities2_Data/Content/Game/UI/Media (PNG / JPG compared only when the sizes match);
 *   - an SVG that contains a vanilla SVG path: any `d="..."` of 40 characters or more that also appears in a vanilla SVG.
 * Without an installed game (CSII_INSTALLATIONPATH, as check-bundle R10) the check prints SKIP and passes: CI has no game.
 */
"use strict";
const fs = require("fs");
const path = require("path");
const crypto = require("crypto");
const { execFileSync } = require("child_process");

const ROOT = path.resolve(__dirname, "..");
const IMG = /\.(svg|png|jpe?g)$/i;
const MIN_D = 40;

function installDir() {
  try {
    const v = require(path.join(ROOT, "mod", "SkiResortUI", "tools", "user-env")).readCsiiVar("CSII_INSTALLATIONPATH").value;
    if (v) return v;
  } catch { /* fall through */ }
  return process.env.CSII_INSTALLATIONPATH || null;
}

function walk(dir, out) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, e.name);
    if (e.isDirectory()) walk(full, out);
    else if (IMG.test(e.name)) out.push(full);
  }
  return out;
}

const sha = (buf) => crypto.createHash("sha256").update(buf).digest("hex");
const pathsOf = (text) => [...text.matchAll(/\sd="([^"]+)"/g)].map((m) => m[1].replace(/\s+/g, " ").trim()).filter((d) => d.length >= MIN_D);

/** The vanilla index: SVG hashes and long path strings, and the raster files by size (hashed only on a size match). */
function vanillaIndex(media) {
  const files = walk(media, []);
  const svgHash = new Map(), dSet = new Map(), bySize = new Map();
  for (const f of files) {
    if (/\.svg$/i.test(f)) {
      const buf = fs.readFileSync(f);
      svgHash.set(sha(buf), f);
      for (const d of pathsOf(buf.toString("utf8"))) if (!dSet.has(d)) dSet.set(d, f);
    } else {
      const size = fs.statSync(f).size;
      if (!bySize.has(size)) bySize.set(size, []);
      bySize.get(size).push(f);
    }
  }
  return { count: files.length, svgHash, dSet, bySize };
}

function findings(file, buf, idx, media) {
  const rel = (p) => path.relative(media, p).split(path.sep).join("/");
  const out = [];
  if (/\.svg$/i.test(file)) {
    const hit = idx.svgHash.get(sha(buf));
    if (hit) out.push(`a byte copy of Media/${rel(hit)}`);
    else for (const d of pathsOf(buf.toString("utf8"))) {
      const src = idx.dSet.get(d);
      if (src) { out.push(`contains a path copied from Media/${rel(src)}`); break; }
    }
  } else {
    const same = idx.bySize.get(buf.length);
    if (same) {
      const h = sha(buf);
      const hit = same.find((f) => sha(fs.readFileSync(f)) === h);
      if (hit) out.push(`a byte copy of Media/${rel(hit)}`);
    }
  }
  return out;
}

function main() {
  const install = installDir();
  const media = install && path.join(install, "Cities2_Data", "Content", "Game", "UI", "Media");
  if (!media || !fs.existsSync(media)) { console.log("check-no-vanilla-art: SKIP (no installed game: CSII_INSTALLATIONPATH not set)"); return 0; }
  const idx = vanillaIndex(media);

  if (process.argv.includes("--self-test")) {
    const svg = [...idx.svgHash.values()].find((f) => pathsOf(fs.readFileSync(f, "utf8")).length > 0);
    const buf = fs.readFileSync(svg);
    const d = pathsOf(buf.toString("utf8"))[0];
    const planted = Buffer.from(`<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16"><path d="${d}" fill="rgb(240,251,255)"/></svg>`);
    const ours = Buffer.from('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16"><path d="M0.8 4.6 L15.2 0.8 L15.2 2.8 L0.8 6.6 Z M1 1 L2 2 L3 3"/></svg>');
    const got = [findings("copy.svg", buf, idx, media).length, findings("mixed.svg", planted, idx, media).length, findings("ours.svg", ours, idx, media).length];
    const ok = got[0] === 1 && got[1] === 1 && got[2] === 0;
    console.log(`check-no-vanilla-art --self-test: byte copy ${got[0]}, copied path ${got[1]}, own art ${got[2]} - ${ok ? "PASS" : "FAIL"}`);
    return ok ? 0 : 1;
  }

  const listed = execFileSync("git", ["ls-files", "-z", "--cached", "--others", "--exclude-standard"], { cwd: ROOT, maxBuffer: 64 << 20 })
    .toString("utf8").split("\0").filter((f) => f && IMG.test(f) && fs.existsSync(path.join(ROOT, f)));
  const bad = [];
  for (const f of listed) for (const why of findings(f, fs.readFileSync(path.join(ROOT, f)), idx, media)) bad.push(`${f}: ${why}`);
  for (const b of bad) console.error(`check-no-vanilla-art: FAIL ${b}`);
  console.log(`check-no-vanilla-art: ${listed.length} repo image(s) against ${idx.count} installed game file(s), ${idx.dSet.size} game path(s): ` +
    (bad.length ? `${bad.length} copy(ies) - FAIL` : "no game art - PASS"));
  return bad.length ? 1 : 0;
}

process.exit(main());
