/*
* tools/user-env.js - read a Cities: Skylines II toolchain variable (CSII_*) the way the C# build does.
*
* The game toolchain's Mod.props reads every CSII_* variable from the Windows USER environment scope, and a shell
* started before the installer set them does not have them in process.env, so the registry (HKCU\Environment) is
* the source of truth.
*
* readCsiiVar(name) returns { value, source }, source being "user-registry", "process-env" or null (value null).
* Never throws; callers print the source so the build log always says where a path came from.
*/
"use strict";

const { execFileSync } = require("child_process");

function readUserRegistry(name) {
  if (process.platform !== "win32") return null;
  try {
    const out = execFileSync("reg", ["query", "HKCU\\Environment", "/v", name], {
      encoding: "utf8",
      stdio: ["ignore", "pipe", "ignore"],
      windowsHide: true,
    });
    // Line shape: "    CSII_X    REG_SZ    C:\path with spaces"
    const m = out.match(/REG_(?:EXPAND_)?SZ[ \t]+(.+?)\s*$/m);
    if (!m) return null;
    // REG_EXPAND_SZ values may hold %VARS%
    return m[1].replace(/%([^%]+)%/g, (all, v) => (process.env[v] !== undefined ? process.env[v] : all));
  } catch {
    return null; // value absent (reg exits 1) or reg.exe unavailable
  }
}

function readCsiiVar(name) {
  const fromRegistry = readUserRegistry(name);
  if (fromRegistry) return { value: fromRegistry, source: "user-registry" };
  const fromEnv = process.env[name];
  if (fromEnv) return { value: fromEnv, source: "process-env" };
  return { value: null, source: null };
}

module.exports = { readCsiiVar };
