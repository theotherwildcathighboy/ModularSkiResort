/*
 * tools/check-manifest-args.js - every generator manifest records the arguments that built it (preflight MANIFESTARGS, T256).
 *
 *   node tools/check-manifest-args.js              check every tracked or new (not ignored) assets/import/**\/*.manifest.json
 *   node tools/check-manifest-args.js --shrink     drop baseline entries that now record args or are gone (never adds one)
 *   node tools/check-manifest-args.js --self-test  prove the rule on synthetic manifests
 *
 * Why: the shipped fixed-grip stations (letter O) were built with --thin-rope (both) and --no-hosted-tower (base), and no manifest
 * said so. A rebuild without the flags differs (base 7,950 tris against the shipped 8,166), and the flags' help text called "off"
 * the shipped station. Only the derived fields rope_r_m and nose_train showed it (r4 tex FG audit, 2026-11-19).
 *
 * The rule: a manifest has a top-level "args": an object (vars(args)) or a non-empty array (argv). The manifests written before this
 * gate are listed in tools/manifest-args-baseline.json. That list only shrinks: a new manifest without "args" fails, a rebuilt one
 * that now records args must leave the list (--shrink). The "known_flags" map of the baseline records what is known about the
 * build flags of a listed manifest. assets/import/archive/ is history and is not checked.
 *
 * Generators that wrote manifests without "args" at the time of the gate: lift_terminal.py (fixed in T256), lift_tower.py (T256),
 * lift_head.py, lift_terminal_detach.py, cs2_signset.py, groomer.py, tram_saddle.py. Their next export fails here until the
 * manifest writes "args": vars(args).
 */
"use strict";
const fs = require("fs");
const path = require("path");
const { execFileSync } = require("child_process");

const ROOT = path.resolve(__dirname, "..");
const BASELINE = path.join(__dirname, "manifest-args-baseline.json");

function hasArgs(j) {
  if (!j || typeof j !== "object") return false;
  const a = j.args;
  if (Array.isArray(a)) return a.length > 0;
  return a !== null && typeof a === "object" && Object.keys(a).length > 0;
}

/** findings for a set of {rel, json} against a baseline list; returns { fails: [string], stale: [rel] } */
function judge(manifests, baselinePaths) {
  const listed = new Set(baselinePaths);
  const seen = new Set();
  const fails = [];
  const stale = [];
  for (const m of manifests) {
    seen.add(m.rel);
    if (m.json === undefined) {
      fails.push(`${m.rel}: not valid JSON`);
      continue;
    }
    const ok = hasArgs(m.json);
    if (!ok && !listed.has(m.rel))
      fails.push(`${m.rel}: no "args" (write "args": vars(args) in the generator's manifest; see the header of this check)`);
    if (ok && listed.has(m.rel)) stale.push(m.rel);
  }
  for (const p of baselinePaths) if (!seen.has(p)) stale.push(p);
  return { fails, stale };
}

function listManifests() {
  const out = execFileSync("git", ["ls-files", "--cached", "--others", "--exclude-standard", "--", "assets/import"],
    { cwd: ROOT, encoding: "utf8" });
  return [...new Set(out.split(/\r?\n/).filter(f => f.endsWith(".manifest.json") && !f.startsWith("assets/import/archive/")))]
    .filter(f => fs.existsSync(path.join(ROOT, f)))
    .sort()
    .map(rel => {
      let json;
      try { json = JSON.parse(fs.readFileSync(path.join(ROOT, rel), "utf8").replace(/^﻿/, "")); } catch { json = undefined; }
      return { rel, json };
    });
}

function readBaseline() {
  return JSON.parse(fs.readFileSync(BASELINE, "utf8"));
}

function selfTest() {
  const A = { args: { kind: "drive" } }, V = { args: ["--kind", "drive"] }, N = { generator: "x" }, E = { args: {} }, Z = { args: [] };
  const ms = [{ rel: "a", json: A }, { rel: "v", json: V }, { rel: "n", json: N }, { rel: "e", json: E }, { rel: "z", json: Z },
    { rel: "old", json: N }, { rel: "fixed", json: A }, { rel: "bad", json: undefined }];
  const r = judge(ms, ["old", "fixed", "gone"]);
  const failed = r.fails.map(f => f.split(":")[0]).sort().join(",");
  const stale = r.stale.sort().join(",");
  const want = { failed: "bad,e,n,z", stale: "fixed,gone" };
  if (failed !== want.failed || stale !== want.stale) {
    console.log(`self-test FAIL: fails [${failed}] (want [${want.failed}]), stale [${stale}] (want [${want.stale}])`);
    process.exit(1);
  }
  console.log("check-manifest-args self-test: object / argv accepted; missing, empty and invalid caught; stale entries found - PASS");
}

function main() {
  const argv = process.argv.slice(2);
  if (argv.includes("--self-test")) return selfTest();
  const base = readBaseline();
  const manifests = listManifests();
  const { fails, stale } = judge(manifests, base.paths);
  if (argv.includes("--shrink")) {
    const drop = new Set(stale);
    base.paths = base.paths.filter(p => !drop.has(p));
    for (const p of drop) if (base.known_flags) delete base.known_flags[p];
    fs.writeFileSync(BASELINE, JSON.stringify(base, null, 2) + "\n");
    console.log(`check-manifest-args: baseline shrunk by ${drop.size} to ${base.paths.length}`);
    return;
  }
  for (const s of stale) fails.push(`${s}: listed in tools/manifest-args-baseline.json but records args or is gone (run --shrink)`);
  for (const f of fails) console.log(f);
  const withArgs = manifests.filter(m => hasArgs(m.json)).length;
  console.log(`check-manifest-args: ${manifests.length} manifest(s), ${withArgs} record args, ${base.paths.length} on the baseline, ` +
    `${fails.length} finding(s) - ${fails.length ? "FAIL" : "PASS"}`);
  process.exit(fails.length ? 1 : 0);
}

main();
