/*
 * tools/user-env.js - read a Cities: Skylines II toolchain variable (CSII_*) the way the C#
 * build does.
 *
 * Written for this repo (MIT). Derived from: the game toolchain's Mod.props, which reads every
 * CSII_* variable from the Windows USER environment scope
 * (`[System.Environment]::GetEnvironmentVariable('CSII_...', 'EnvironmentVariableTarget.User')`,
 * Mod.props:14-47; docs/research/resort-hub-understand.md section 2.A.3 item 5). A shell started
 * before the in-game "Modding toolchain" installer set those variables does not have them in
 * process.env, so the User scope (registry HKCU\Environment) is the source of truth.
 *
 * Contract: readCsiiVar(name) returns { value, source } where source is "user-registry",
 * "process-env" or null (value null). It never throws. Every caller prints the source, so the
 * build log always says where a path came from (silence must be impossible).
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
    // REG_EXPAND_SZ values may hold %VARS%; expand them from this process' environment.
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
