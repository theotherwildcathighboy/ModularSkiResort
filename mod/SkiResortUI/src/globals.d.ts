/*
 * src/globals.d.ts - compile-time constants and module shapes the synced game typings do not cover.
 *
 * Written for this repo (MIT). These declarations cannot live in types/: that folder is
 * re-synced from the game install on every build and is git-ignored (tools/sync-types.js).
 */

/**
 * The build stamp of this bundle, injected by webpack's DefinePlugin (webpack.config.js,
 * SkiStampPlugin). From a C# build it equals SkiResortMod.BuildStamp.Value in the DLL deployed
 * beside it (SkiResortMod.csproj, targets SkiUiBuild + SkiBuildStamp); from a standalone
 * `npm run build` / `npm run dev` it starts with "ui-". Read it through BUNDLE_STAMP in
 * src/bindings.ts, never directly.
 */
declare const __BUILD_STAMP__: string;

/** CSS modules (css-loader, modules.auto): class name -> generated "sr-<name>_<hash>". */
declare module "*.module.scss" {
  const classes: { readonly [className: string]: string };
  export default classes;
}
