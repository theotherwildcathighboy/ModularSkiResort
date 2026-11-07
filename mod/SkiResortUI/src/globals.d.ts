/* Compile-time constants and module shapes the synced game typings (types/, git-ignored, re-synced on every build) do not cover. */

/**
 * Build stamp injected by webpack DefinePlugin: equals SkiResortMod.BuildStamp.Value in the deployed DLL,
 * or starts with "ui-" for a standalone npm build. Read it through BUNDLE_STAMP in src/bindings.ts.
 */
declare const __BUILD_STAMP__: string;

/** CSS modules (css-loader, modules.auto): class name -> generated "sr-<name>_<hash>". */
declare module "*.module.scss" {
  const classes: { readonly [className: string]: string };
  export default classes;
}

/**
 * Images imported from src/ (webpack asset/resource): the "coui://ui-mods/SkiResortMod-img/<name>" URL.
 * Declared here because the synced types/assets.d.ts is not required by the build (tools/sync-types.js).
 */
declare module "*.svg" {
  const url: string;
  export default url;
}

/**
 * The synced DropdownToggle typings declare no focusKey, but the hub gate requires focusKey={FOCUS_DISABLED} on every
 * DropdownToggle in a Panel header. No-op at runtime: DropdownToggleBase sets focusKey after spreading props
 * (cs2/ui index.js@718643). The Dropdown's own focusKey keeps the control out of the header's focus slot.
 * Interface merge only, no runtime value.
 */
declare module "cs2/ui" {
  interface DropdownToggleBaseProps {
    focusKey?: FocusKey;
  }
}
