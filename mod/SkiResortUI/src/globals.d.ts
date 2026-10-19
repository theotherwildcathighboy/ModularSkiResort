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

/**
 * Images imported from src/ (webpack asset/resource -> "coui://ui-mods/SkiResortMod-img/<name>"):
 * the URL string. Declared here because the synced types/assets.d.ts is not required by the build
 * (tools/sync-types.js REQUIRED). Both declarations together compile (tsc 4.8.4, scratch test).
 * Round S1 F9 (docs/research/ski-days/spec/ch5-launcher.md D.2): src/hub/SkiLauncherGlyph.svg.
 */
declare module "*.svg" {
  const url: string;
  export default url;
}

/**
 * T97 P1 (2026-10-19): the synced DropdownToggle typings declare no `focusKey`, but gate rule R15 requires a literal
 * focusKey={FOCUS_DISABLED} on every DropdownToggle in a Panel header. At runtime the prop is a no-op: DropdownToggleBase
 * spreads its props into the Button and THEN sets focusKey: DROPDOWN_TOGGLE_KEY (installed 1.6.2f1 index.js@718643), so the
 * toggle always registers inside the Dropdown's own FocusKeyOverride; the Dropdown's focusKey (declared) is the one that
 * keeps the whole control out of the header's single-child focus slot. Interface merge only, no runtime value.
 */
declare module "cs2/ui" {
  interface DropdownToggleBaseProps {
    focusKey?: FocusKey;
  }
}
