/*
 * tools/sync-listing.js - keeps the Paradox Mods listing text in step (release-prep, 2026-10-22). Written for this repo (MIT).
 *
 * release/DESCRIPTION.md is the source of the listing copy. mod/SkiResortMod/Properties/PublishConfiguration.xml carries the same text in
 * <ShortDescription Value="..."/> and <LongDescription>...</LongDescription>. If you copy by hand, the two drift, so this script copies it.
 * The script fills <ChangeLog> the same way from the newest entry of release/CHANGELOG.md. (It had drifted: no depot, an old layout version.)
 *   node tools/sync-listing.js            write the short and long description from DESCRIPTION.md into the XML (BOM and line endings kept)
 *   node tools/sync-listing.js --check    change nothing. Exit 1 when any of these is true:
 *                                         - the XML text differs from DESCRIPTION.md
 *                                         - the text holds a character the XML or the Paradox Mods markdown subset cannot take (< > & `)
 *                                         - a line starts with a space or tab (the platform renders leading whitespace as a code block)
 *                                         - the short description is over 200 characters (the longest on any live listing)
 *                                         The check also prints the unresolved link tokens ({{DOCS_URL}}, {{GITHUB_URL}}).
 *   node tools/sync-listing.js --public   with --check: also FAIL while a {{token}} is left or AccessLevel is not Public (the flip gate).
 * Source text: from the line "**Build a ski resort in" up to the end of the line that starts "**License**:". The short description is the
 * first non-empty line after the "**Short description**" label.
 */
"use strict";
const fs = require("fs");
const path = require("path");

const ROOT = path.resolve(__dirname, "..");
const DESC = path.join(ROOT, "release", "DESCRIPTION.md");
const XML = path.join(ROOT, "mod", "SkiResortMod", "Properties", "PublishConfiguration.xml");
const args = process.argv.slice(2);
const check = args.includes("--check");
const publicGate = args.includes("--public");

const d = fs.readFileSync(DESC, "utf8").replace(/\r\n/g, "\n");
const a = d.indexOf("**Build a ski resort in");
const licStart = d.indexOf("**License**:");
if (a < 0 || licStart < 0) { console.error("DESCRIPTION.md: long description markers not found"); process.exit(2); }
let licEnd = d.indexOf("\n", licStart);
if (licEnd < 0) licEnd = d.length;
const long = d.slice(a, licEnd).trim();
const sm = d.match(/\*\*Short description\*\*[^\n]*\n\n([^\n]+)\n/);
if (!sm) { console.error("DESCRIPTION.md: short description not found"); process.exit(2); }
const short = sm[1].trim();

const problems = [];
if (/[<>&`]/.test(long)) problems.push("long description holds one of < > & ` (XML or markdown-subset hazard)");
if (/[<>&`]/.test(short)) problems.push("short description holds one of < > & `");
if (/^[ \t]+\S/m.test(long)) problems.push("long description has a line starting with a space or tab (renders as a code block)");
if (short.length > 200) problems.push("short description is " + short.length + " characters (live maximum is 200)");

const raw = fs.readFileSync(XML, "utf8");
const bom = raw.charCodeAt(0) === 0xFEFF;
const crlf = /\r\n/.test(raw);
let x = raw.replace(/\r\n/g, "\n");
const longRe = /<LongDescription>\n?([\s\S]*?)\n?<\/LongDescription>/;
const shortRe = /<ShortDescription Value="([^"]*)" \/>/;
const curLong = (x.match(longRe) || [])[1];
const curShort = (x.match(shortRe) || [])[1];
if (curLong === undefined || curShort === undefined) { console.error("PublishConfiguration.xml: ShortDescription / LongDescription not found"); process.exit(2); }
const same = curLong.trim() === long && curShort === short;

// The change log: release/CHANGELOG.md's newest entry, one line per bullet, into <ChangeLog>. The docs page repeats its "Layout versions" line.
const cl = fs.readFileSync(path.join(ROOT, "release", "CHANGELOG.md"), "utf8").replace(/\r\n/g, "\n");
const clStart = cl.search(/^## \d+\.\d+\.\d+ \(PDX version -?\d*, (unreleased|\d{4}-\d{2}-\d{2})\)$/m);
if (clStart < 0) { console.error("CHANGELOG.md: newest entry not found"); process.exit(2); }
const clRest = cl.slice(clStart);
const clEnd = clRest.indexOf("\n## ", 1);
const log = (clEnd < 0 ? clRest : clRest.slice(0, clEnd)).split("\n").slice(1).join("\n").trim()
  .replace(/\n[ \t]+/g, " ")
  .replace(/ Draft notes \(check against the build at upload\):/, "");
if (/[<>&`]/.test(log)) problems.push("change log holds one of < > & `");
const logRe = /<ChangeLog>\n?([\s\S]*?)\n?[ \t]*<\/ChangeLog>/;
const curLog = (x.match(logRe) || [])[1];
if (curLog === undefined) { console.error("PublishConfiguration.xml: ChangeLog not found"); process.exit(2); }
const sameLog = curLog.trim() === log;
const layoutLine = (log.match(/^- Layout versions: .*$/m) || [""])[0];
const mdx = fs.readFileSync(path.join(ROOT, "docs-site", "src", "content", "docs", "changelog.mdx"), "utf8");
if (!layoutLine || !mdx.includes(layoutLine)) problems.push("docs-site changelog.mdx does not carry the line: " + layoutLine);

const tokens = (long.match(/\{\{[A-Z_]+\}\}/g) || []).concat(x.match(/\{\{[A-Z_]+\}\}/g) || []);
const access = (x.match(/<AccessLevel Value="([^"]*)"/) || [])[1];

if (check) {
  if (!same) problems.push("PublishConfiguration.xml text differs from release/DESCRIPTION.md: run node tools/sync-listing.js");
  if (!sameLog) problems.push("PublishConfiguration.xml ChangeLog differs from release/CHANGELOG.md: run node tools/sync-listing.js");
  if (publicGate) {
    if (tokens.length) problems.push("unresolved link tokens: " + [...new Set(tokens)].join(" "));
    if (access !== "Public") problems.push("AccessLevel is " + access + ", not Public");
  } else if (tokens.length) {
    console.log("note: unresolved link tokens (fine for the unlisted stage, fatal for --public): " + [...new Set(tokens)].join(" "));
  }
  console.log("long " + long.length + " chars, short " + short.length + " chars, AccessLevel " + access);
  for (const p of problems) console.log("FAIL " + p);
  console.log(problems.length ? "FAIL" : "PASS");
  process.exit(problems.length ? 1 : 0);
}

if (problems.length) { for (const p of problems) console.log("FAIL " + p); process.exit(1); }
x = x.replace(longRe, () => "<LongDescription>\n" + long + "\n</LongDescription>");
x = x.replace(shortRe, () => '<ShortDescription Value="' + short + '" />');
x = x.replace(logRe, () => "<ChangeLog>\n" + log + "\n\t</ChangeLog>");
if (crlf) x = x.replace(/\n/g, "\r\n");
if (bom && x.charCodeAt(0) !== 0xFEFF) x = "﻿" + x;
fs.writeFileSync(XML, x, "utf8");
console.log((same ? "already in step; rewrote " : "synced ") + "PublishConfiguration.xml (long " + long.length + " chars, short " + short.length + " chars)");
