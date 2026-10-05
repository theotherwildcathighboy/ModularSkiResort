/*
 * webpack.config.js - builds the Ski Resort UI module (SkiResortMod.mjs + SkiResortMod.css).
 *
 * Written for this repo (MIT); nothing copied. Derived from the settings the game's module loader
 * REQUIRES, as listed with their evidence in docs/research/resort-hub-understand.md section
 * 2.A.2 (the official Colossal Order template in the game install,
 * .ModdingToolchain/npx-create-csii-ui-mod/template/webpack.config.js, was read as a reference):
 *   - ES-module output (experiments.outputModule + output.library.type "module"): the game
 *     import()s every *.mjs in a mod folder and keeps `default` as the registrar
 *     (index.js@2306554 in the installed 1.6.2f1 Cities2_Data/Content/Game/UI/index.js).
 *   - externalsType "window" with one external per game module we import: the game defines 12
 *     read-only window names (React, ReactDOM, ReactDOMClient, cs2/api, cs2/bindings, cs2/l10n,
 *     cs2/ui, cs2/utils, cs2/input, cs2/modding, cohtml/cohtml, chart.js; index.js@799710).
 *   - <entry>.css beside <entry>.mjs, paired with `export const hasCSS = true` in src/index.tsx:
 *     the loader fetches mjs.replace(".mjs", ".css") and waits for it with no error path and no
 *     timeout, so a missing .css blanks every mod's UI (index.js@2306554; XHR helper @800011).
 *   - exactly ONE .mjs: no async chunks, no split chunks, no dynamic import(); a second .mjs
 *     without a default export would be registered as `undefined` and break the registrar loop
 *     (`AR`, index.js@800454: `for(const t of e)t(Q)` with no try/catch).
 *   - the leading banner with Id + Dependencies lines: the only manifest the game reads
 *     (Colossal.IO.AssetDatabase UIModuleAsset.ParseModuleInfo, 1.6.2f1, decompiled with
 *     ilspycmd); an Id line without a Dependencies line makes PostCreate call AddTags(null).
 *     The parser stops at the first non-empty line without a ':' - keep every banner line
 *     "Key: value". mod.json is read ONLY here, to write the banner.
 * Our choices: entry name = mod.json id = the C# TargetName (SkiResortMod); ts-loader WITH
 * type-checking; sass; class names prefixed "sr-" with a 5-char hash (the game's default 3-char
 * hash collides more easily across the ~28 modules sharing one document); images under
 * SkiResortMod-img/ because coui://ui-mods/ is ONE host shared by every mod folder.
 *
 * Build stamp: ONE string per build, compiled in as __BUILD_STAMP__ (DefinePlugin) and written to
 * build/.ui-stamp. The C# build (SkiResortMod.csproj, target SkiUiBuild) passes its own stamp in
 * SKI_BUILD_STAMP and compiles build/.ui-stamp into SkiResortMod.BuildStamp.Value, so the DLL and
 * the bundle it deploys carry the same value. A standalone `npm run build` / `npm run dev`
 * generates a "ui-..." stamp (a UI-only build: the DLL will report a different stamp).
 * Gate marker (fixer round U1, pipeline-safety-1): every emit deletes build/.gate-pass; only a
 * PASSING tools/check-bundle.js run writes it back (content = the stamp). The C# build and
 * --env deploy ship nothing from build/ without it (tools/check-bundle.js header).
 *
 * Modes:
 *   webpack                    -> build/ (cleaned), then `postbuild` runs tools/check-bundle.js.
 *   webpack --watch --env deploy  (npm run dev) -> build/, and after EVERY compile the same
 *                              check-bundle gate runs in-process; only a bundle that passes is
 *                              copied into %CSII_LOCALMODSPATH%\SkiResortMod (refused unless
 *                              SkiResortMod.dll is already there). The research had --env deploy
 *                              write straight into the Mods folder, ungated; this is stricter.
 */
"use strict";

const path = require("path");
const fs = require("fs");
const crypto = require("crypto");
const webpack = require("webpack");
const MiniCssExtractPlugin = require("mini-css-extract-plugin");
const TerserPlugin = require("terser-webpack-plugin");
const MOD = require("./mod.json");
const { readCsiiVar } = require("./tools/user-env");
const { checkBundle, bundleFiles, readGateMarker, removeGateMarker, STAMP_FILE } = require("./tools/check-bundle");

const ROOT = __dirname;
const SRC = path.join(ROOT, "src");
const BUILD = path.join(ROOT, "build");
const STAMP_RE = /^[A-Za-z0-9._-]{1,64}$/;

function utcStamp() {
  const d = new Date();
  const p = (n) => String(n).padStart(2, "0");
  return `${d.getUTCFullYear()}${p(d.getUTCMonth() + 1)}${p(d.getUTCDate())}-` +
    `${p(d.getUTCHours())}${p(d.getUTCMinutes())}${p(d.getUTCSeconds())}`;
}

function newStandaloneStamp() {
  return `ui-${utcStamp()}-${crypto.randomBytes(3).toString("hex")}`;
}

function bannerText(stamp) {
  // Terser wraps this as "/*! <text> */". Every non-empty line must be "* Key: value" (see header).
  return [
    "",
    " * Cities: Skylines II UI Module",
    " *",
    ` * Id: ${MOD.id}`,
    ` * Author: ${MOD.author}`,
    ` * Version: ${MOD.version}`,
    ` * Dependencies: ${(MOD.dependencies || []).join(",")}`,
    ` * Build: ${stamp}`,
    "",
  ].join("\n");
}

/** Owns the build stamp: one value per compilation, the stamp file, the one-line summary. */
class SkiStampPlugin {
  constructor(fromMsbuild) {
    this.fromMsbuild = fromMsbuild;
    this.current = fromMsbuild || newStandaloneStamp();
    this.compiles = 0;
  }
  apply(compiler) {
    compiler.hooks.compile.tap("SkiStampPlugin", () => {
      // A watch rebuild is a new bundle: give it a new stamp (MSBuild builds compile once).
      if (this.compiles++ > 0 && !this.fromMsbuild) this.current = newStandaloneStamp();
    });
    compiler.hooks.emit.tap("SkiStampPlugin", () => {
      // A new bundle is about to replace build/: it has passed no gate yet (fixer round U1,
      // pipeline-safety-1). output.clean removes the marker only on the FIRST compile; watch
      // rebuilds clean only webpack's own previous assets (webpack/lib/CleanPlugin.js), so delete it here.
      removeGateMarker(BUILD);
    });
    compiler.hooks.afterEmit.tap("SkiStampPlugin", (compilation) => {
      if (compilation.errors.length) return;
      fs.writeFileSync(path.join(BUILD, STAMP_FILE), this.current + "\n");
    });
    compiler.hooks.done.tap("SkiStampPlugin", (stats) => {
      if (stats.hasErrors()) {
        console.error(`SkiResortMod UI: webpack FAILED (${stats.compilation.errors.length} error(s)); nothing written to the stamp file.`);
        return;
      }
      const sizes = Object.keys(stats.compilation.assets).sort()
        .map((a) => `${a} ${(stats.compilation.assets[a].size() / 1024).toFixed(1)} KB`).join(", ");
      console.log(`SkiResortMod UI: built stamp ${this.current} (${this.fromMsbuild ? "from MSBuild" : "standalone UI build"}) -> ${sizes}`);
    });
  }
}

/** --env deploy: gate every compile with check-bundle, copy only a passing bundle to the Mods folder. */
class SkiDeployPlugin {
  constructor(stampPlugin) { this.stampPlugin = stampPlugin; }
  apply(compiler) {
    compiler.hooks.done.tapPromise("SkiDeployPlugin", async (stats) => {
      if (stats.hasErrors()) return; // already reported
      const mods = readCsiiVar("CSII_LOCALMODSPATH");
      if (!mods.value) {
        console.error("SkiResortMod UI deploy: REFUSED - CSII_LOCALMODSPATH is not set (User scope or process env).");
        return;
      }
      const dest = path.join(mods.value, MOD.id);
      if (!fs.existsSync(path.join(dest, `${MOD.id}.dll`))) {
        console.error(`SkiResortMod UI deploy: REFUSED - ${dest}\\${MOD.id}.dll not found. Run dotnet build in ` +
          "mod/SkiResortMod first (with the game closed): a .mjs created mid-session is not registered.");
        return;
      }
      let result;
      try {
        result = await checkBundle({ quiet: true });
      } catch (e) {
        console.error(`SkiResortMod UI deploy: REFUSED - check-bundle crashed: ${e && e.stack ? e.stack : e}`);
        return;
      }
      if (!result.ok) {
        console.error("SkiResortMod UI deploy: REFUSED - check-bundle failed:\n  " + result.errors.join("\n  "));
        return;
      }
      // The same marker the C# build trusts (check-bundle header "THE GATE MARKER").
      let built = null;
      try { built = fs.readFileSync(path.join(BUILD, STAMP_FILE), "utf8").trim(); } catch { /* reported below */ }
      const passed = readGateMarker(BUILD);
      if (!built || passed !== built || built !== this.stampPlugin.current) {
        console.error(`SkiResortMod UI deploy: REFUSED - gate marker ${passed} / build stamp ${built} / this compile ` +
          `${this.stampPlugin.current} disagree (build/ changed under the gate?)`);
        return;
      }
      const files = bundleFiles(BUILD);
      for (const rel of files) {
        const to = path.join(dest, rel);
        fs.mkdirSync(path.dirname(to), { recursive: true });
        fs.copyFileSync(path.join(BUILD, rel), to);
      }
      console.log(`SkiResortMod UI deploy: ${files.length} file(s), stamp ${this.stampPlugin.current} -> ${dest} ` +
        "(the DLL there carries a different stamp: expected for a UI-only build)");
    });
  }
}

module.exports = (env = {}) => {
  const fromMsbuild = process.env.SKI_BUILD_STAMP || "";
  if (fromMsbuild && !STAMP_RE.test(fromMsbuild)) {
    throw new Error(`SKI_BUILD_STAMP "${fromMsbuild}" is not a safe stamp (${STAMP_RE})`);
  }
  const stamp = new SkiStampPlugin(fromMsbuild);
  const deploy = !!env.deploy;

  return {
    mode: "production",
    target: ["web", "es2020"],
    devtool: false,
    stats: "errors-warnings",
    infrastructureLogging: { level: "warn" },
    performance: { hints: false },
    entry: {
      [MOD.id]: "./src/index.tsx",
    },
    externalsType: "window",
    externals: {
      react: "React",
      "react-dom": "ReactDOM",
      "cs2/api": "cs2/api",
      "cs2/bindings": "cs2/bindings",
      "cs2/l10n": "cs2/l10n",
      "cs2/ui": "cs2/ui",
      "cs2/utils": "cs2/utils",
      "cs2/input": "cs2/input",
      "cs2/modding": "cs2/modding",
      "cohtml/cohtml": "cohtml/cohtml",
      // chart.js is a window name too (index.js@799710); add it only when we draw charts.
    },
    module: {
      rules: [
        {
          test: /\.tsx?$/,
          exclude: /node_modules/,
          use: { loader: "ts-loader", options: { configFile: path.join(ROOT, "tsconfig.json") } },
        },
        {
          test: /\.s?css$/i,
          include: SRC,
          use: [
            MiniCssExtractPlugin.loader,
            {
              loader: "css-loader",
              options: {
                importLoaders: 1,
                // Leave game media and absolute coui: URLs alone; bundle everything else.
                url: { filter: (url) => !/^(Media\/|coui:|assetdb:)/i.test(url) },
                modules: {
                  auto: /\.module\.s?css$/i,
                  localIdentName: "sr-[local]_[hash:base64:5]",
                  exportLocalsConvention: "camelCase",
                },
              },
            },
            "sass-loader",
          ],
        },
        {
          test: /\.(png|svg)$/i,
          type: "asset/resource",
          generator: { filename: `${MOD.id}-img/[name][ext]` },
        },
      ],
    },
    resolve: {
      extensions: [".tsx", ".ts", ".js"],
      modules: ["node_modules", SRC],
    },
    output: {
      path: BUILD,
      filename: "[name].mjs",
      module: true,
      chunkFormat: "module",
      library: { type: "module" },
      publicPath: "coui://ui-mods/",
      asyncChunks: false,
      clean: true,
      // Always rewrite every output file: SkiResortMod.csproj (SkiUiBuild) uses the files'
      // timestamps as its incremental Outputs, and an unchanged .css kept with an old timestamp
      // would make every C# build rerun npm (research 2.A.3 item 11).
      compareBeforeEmit: false,
    },
    optimization: {
      minimize: true,
      splitChunks: false,
      runtimeChunk: false,
      minimizer: [
        new TerserPlugin({
          // The banner is written only when at least one comment was extracted; src/index.tsx
          // starts with a "/*!" comment so this never depends on react/jsx-runtime's @license.
          extractComments: { banner: () => bannerText(stamp.current) },
        }),
      ],
    },
    experiments: {
      outputModule: true,
    },
    plugins: [
      new MiniCssExtractPlugin({ filename: "[name].css" }),
      new webpack.DefinePlugin({
        __BUILD_STAMP__: webpack.DefinePlugin.runtimeValue(() => JSON.stringify(stamp.current), {
          version: () => stamp.current,
        }),
      }),
      new webpack.optimize.LimitChunkCountPlugin({ maxChunks: 1 }),
      stamp,
      ...(deploy ? [new SkiDeployPlugin(stamp)] : []),
    ],
  };
};
