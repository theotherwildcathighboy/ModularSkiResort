#!/usr/bin/env node
// Base-path link check. After `astro build`, it scans every built HTML page for root-relative URLs
// (href, src, action, poster, data-href, content="/..."). It fails when a URL lacks the configured
// base path or points at a file that is not in dist/. Under a GitHub Pages project site
// (DOCS_BASE=/<repo>) an unprefixed `/guides/lifts/` is a 404. Locally (base '/') every URL passes
// the prefix test, so the existence test still runs.
//
// Usage: DOCS_BASE=/ModularSkiResort node tools/check-base-links.js [--dist <dir>]
//   DOCS_BASE  must match the base the dist/ was built with (default '/', same as astro.config.mjs)
//   --dist     build folder to scan (default ../dist next to this script)
// Exit 0 = PASS, 1 = FAIL (offending file, URL and reason printed), 2 = no dist/.

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
const distIndex = args.indexOf('--dist');
const dist = distIndex >= 0 ? path.resolve(args[distIndex + 1]) : path.resolve(here, '..', 'dist');
const rawBase = process.env.DOCS_BASE || '/';
const trimmed = rawBase.replace(/^\/+|\/+$/g, '');
const prefix = trimmed ? '/' + trimmed : ''; // '' = root

if (!fs.existsSync(dist)) {
  console.error(`check-base-links: no build at ${dist}; run astro build first`);
  process.exit(2);
}

function walk(dir) {
  const out = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) out.push(...walk(full));
    else if (entry.name.endsWith('.html')) out.push(full);
  }
  return out;
}

// quoted or unquoted attribute values that start with a single slash
const ATTR = /\b(?:href|src|action|poster|data-href|content)\s*=\s*(?:"(\/(?!\/)[^"]*)"|'(\/(?!\/)[^']*)'|(\/(?!\/)[^\s>"']*))/gi;

function exists(urlPath) {
  const rel = decodeURIComponent(urlPath.slice(prefix.length)).replace(/^\/+/, '');
  const target = path.join(dist, rel);
  if (!target.startsWith(dist)) return false;
  if (fs.existsSync(target) && fs.statSync(target).isFile()) return true;
  return fs.existsSync(path.join(target, 'index.html')) || fs.existsSync(target + '.html');
}

const problems = [];
let pages = 0;
let checked = 0;
for (const file of walk(dist)) {
  pages += 1;
  const rel = path.relative(dist, file).split(path.sep).join('/');
  const html = fs.readFileSync(file, 'utf8');
  for (const m of html.matchAll(ATTR)) {
    const url = m[1] ?? m[2] ?? m[3];
    const pathOnly = url.split(/[?#]/)[0];
    checked += 1;
    if (prefix && pathOnly !== prefix && !pathOnly.startsWith(prefix + '/')) {
      problems.push(`${rel}: ${url} lacks base ${prefix}`);
    } else if (!exists(pathOnly)) {
      problems.push(`${rel}: ${url} does not exist in dist/`);
    }
  }
}

if (pages === 0) {
  console.error(`check-base-links: no .html pages in ${dist}`);
  process.exit(2);
}
if (problems.length > 0) {
  console.error(`check-base-links: FAIL, base ${prefix || '/'}: ${problems.length} problem(s)`);
  for (const p of problems) console.error('  ' + p);
  process.exit(1);
}
console.log(`check-base-links: PASS, base ${prefix || '/'}, ${checked} root-relative URLs in ${pages} pages, all prefixed and resolvable`);
