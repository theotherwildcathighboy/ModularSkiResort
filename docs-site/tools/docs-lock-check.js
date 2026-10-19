#!/usr/bin/env node
// Protected writing check. The author's own text sits between <!-- LOCK --> and <!-- /LOCK -->,
// and a page with `locked: true` in its frontmatter is locked whole. This script hashes each
// locked region and compares it with docs-lock.json. Any change, or a locked region that
// disappeared, exits 1. An AI session must not edit inside a locked region; relock only when the
// author asks: `npm run lock:relock`.
//
// Usage: node tools/docs-lock-check.js [--relock] [--root <docs-site dir>]
//   --relock  rewrite docs-lock.json from the current files (no check)
//   --root    check another docs-site folder (used by the scratch test)

import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
const relock = args.includes('--relock');
const rootIndex = args.indexOf('--root');
const root = rootIndex >= 0 ? path.resolve(args[rootIndex + 1]) : path.resolve(here, '..');
const docsDir = path.join(root, 'src', 'content', 'docs');
const lockFile = path.join(root, 'docs-lock.json');
const OPEN = '<!-- LOCK -->';
const CLOSE = '<!-- /LOCK -->';

function walk(dir) {
  const out = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) out.push(...walk(full));
    else if (entry.name.endsWith('.mdx')) out.push(full);
  }
  return out;
}

function hash(text) {
  return crypto.createHash('sha256').update(text.trim()).digest('hex');
}

function isLockedPage(text) {
  const fm = text.match(/^---\r?\n([\s\S]*?)\r?\n---/);
  return Boolean(fm) && /^locked:\s*true\s*$/m.test(fm[1]);
}

// Keys are "<path>#page" for a locked page, "<path>#<n>" for the n-th LOCK region (0-based).
function regionsOf(rel, text, errors) {
  const regions = {};
  if (isLockedPage(text)) regions[`${rel}#page`] = hash(text);
  let index = 0;
  let pos = 0;
  for (;;) {
    const open = text.indexOf(OPEN, pos);
    const close = text.indexOf(CLOSE, pos);
    if (open < 0 && close < 0) break;
    if (open < 0 || (close >= 0 && close < open)) {
      errors.push(`${rel}: <!-- /LOCK --> without a matching <!-- LOCK -->`);
      break;
    }
    const end = text.indexOf(CLOSE, open + OPEN.length);
    if (end < 0) {
      errors.push(`${rel}: <!-- LOCK --> without a closing <!-- /LOCK -->`);
      break;
    }
    regions[`${rel}#${index}`] = hash(text.slice(open + OPEN.length, end));
    index += 1;
    pos = end + CLOSE.length;
  }
  return regions;
}

if (!fs.existsSync(docsDir)) {
  console.error(`docs-lock: no docs folder at ${docsDir}`);
  process.exit(1);
}

const current = {};
const errors = [];
for (const file of walk(docsDir).sort()) {
  const rel = path.relative(root, file).split(path.sep).join('/');
  Object.assign(current, regionsOf(rel, fs.readFileSync(file, 'utf8'), errors));
}

if (errors.length > 0) {
  for (const e of errors) console.error(`FAIL ${e}`);
  process.exit(1);
}

if (relock) {
  fs.writeFileSync(lockFile, JSON.stringify(current, null, 2) + '\n');
  console.log(`docs-lock: relocked ${Object.keys(current).length} region(s) into ${path.relative(process.cwd(), lockFile)}`);
  process.exit(0);
}

const locked = fs.existsSync(lockFile) ? JSON.parse(fs.readFileSync(lockFile, 'utf8')) : {};
const failures = [];
for (const [key, h] of Object.entries(locked)) {
  if (!(key in current)) failures.push(`missing: ${key} (locked region or page not found)`);
  else if (current[key] !== h) failures.push(`changed: ${key}`);
}
for (const key of Object.keys(current)) {
  if (!(key in locked)) failures.push(`not locked: ${key} (relock only if the author asked)`);
}

if (failures.length > 0) {
  for (const f of failures) console.error(`FAIL ${f}`);
  console.error(`docs-lock: ${failures.length} problem(s)`);
  process.exit(1);
}
console.log(`docs-lock: pass (${Object.keys(current).length} region(s) checked)`);
