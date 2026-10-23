/*
* tools/sync-types.js - copy the game's cs2/* TypeScript declarations into ./types (git-ignored).
*
* The declarations ship only inside the game install (Cities2_Data/Content/Game/.ModdingToolchain/
* npx-create-csii-ui-mod/template/types) and carry no license text, so they are copied at build time, every build,
* instead of committed. That also makes a game patch's new typings arrive automatically.
*
* - Resolves the install path from the USER environment scope first (tools/user-env.js), then process.env.
* - Copies every *.d.ts except validateTypes.ts (it imports ../src/index, and its "only default export is
*   processed" comment is wrong: hasCSS is read too).
* - Writes a file only when its content changed; deletes stale *.d.ts the game no longer ships.
* - Exits non-zero (failing `npm run build` and the C# build's SkiUiBuild target) when the folder or a declaration
*   for an externalized module is missing.
*/
"use strict";

const fs = require("fs");
const path = require("path");
const { readCsiiVar } = require("./user-env");

const ROOT = path.resolve(__dirname, "..");
const DEST = path.join(ROOT, "types");
const TEMPLATE_TYPES_REL = path.join(
  "Cities2_Data", "Content", "Game", ".ModdingToolchain", "npx-create-csii-ui-mod", "template", "types");
// One declaration file per window external in webpack.config.js
const REQUIRED = ["api.d.ts", "bindings.d.ts", "cohtml.d.ts", "input.d.ts", "l10n.d.ts", "modding.d.ts", "ui.d.ts", "utils.d.ts"];

function fail(msg) {
  console.error(`sync-types: FAILED - ${msg}`);
  process.exit(1);
}

function main() {
  const install = readCsiiVar("CSII_INSTALLATIONPATH");
  if (!install.value) {
    fail("CSII_INSTALLATIONPATH is not set (User scope or process env). Install the modding " +
      "toolchain from the game's Options > Modding page, then open a new shell.");
  }
  const src = path.join(install.value, TEMPLATE_TYPES_REL);
  if (!fs.existsSync(src)) {
    fail(`typings folder not found: ${src} (CSII_INSTALLATIONPATH from ${install.source})`);
  }

  const files = fs.readdirSync(src).filter((f) => f.endsWith(".d.ts")).sort();
  const missing = REQUIRED.filter((f) => !files.includes(f));
  if (missing.length) fail(`the game's typings folder lacks ${missing.join(", ")} (${src})`);

  fs.mkdirSync(DEST, { recursive: true });
  let changed = 0;
  let newest = 0;
  for (const f of files) {
    const from = path.join(src, f);
    const to = path.join(DEST, f);
    const data = fs.readFileSync(from);
    newest = Math.max(newest, fs.statSync(from).mtimeMs);
    let same = false;
    try { same = fs.readFileSync(to).equals(data); } catch { same = false; }
    if (!same) { fs.writeFileSync(to, data); changed++; }
  }
  let removed = 0;
  for (const f of fs.readdirSync(DEST)) {
    if (f.endsWith(".d.ts") && !files.includes(f)) { fs.unlinkSync(path.join(DEST, f)); removed++; }
  }

  const newestDate = new Date(newest).toISOString().slice(0, 10);
  console.log(`sync-types: ${files.length} declaration files from the game install ` +
    `(CSII_INSTALLATIONPATH via ${install.source}), ${changed} updated, ${removed} removed, ` +
    `newest ${newestDate} -> types/`);
}

main();
