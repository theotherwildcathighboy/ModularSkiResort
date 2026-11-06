/*
 * tools/check-bundle.js - the build gate of the Ski Resort UI module.
 *
 * Why a gate: every mod's UI module shares one document and one registrar loop with no try/catch (index.js@800454 in the
 * installed 1.6.2f1 Cities2_Data/Content/Game/UI/index.js), and the loader waits forever for a missing .css
 * (index.js@2306554). A broken bundle of ours can therefore blank or truncate every mod's UI. Nothing leaves build/ for the
 * game's Mods folder unless this passes:
 *   - `npm run build` runs it as `postbuild` (so the C# build's SkiUiBuild target fails with it);
 *   - `npm run dev` (webpack --watch --env deploy) runs it in-process before every copy.
 *
 * The gate marker: a failed run must not let the next `dotnet build` skip SkiUiBuild as up to date and deploy the rejected
 * bundle (webpack has already written it and build/.ui-stamp, both newer than every input). So the result is recorded where
 * MSBuild can see it:
 *   - every run first deletes build/.gate-pass, and writes it (content = the stamp in build/.ui-stamp) only on PASS, so a FAIL,
 *     a CRASH or a killed run all leave no marker;
 *   - webpack deletes it whenever it emits a new bundle (webpack.config.js SkiStampPlugin);
 *   - SkiResortMod.csproj lists it as an Output of SkiUiBuild (a missing marker reruns the gate) and its targets SkiBuildStamp
 *     and SkiUiDeploy refuse a bundle whose marker is missing or names another stamp; SkiDeployPlugin (npm run dev) checks it
 *     before every copy.
 *
 * What it checks (each failure is one line naming the file and the rule):
 *   1. build/ holds exactly SkiResortMod.mjs + SkiResortMod.css (+ the extracted-license .txt, SkiResortMod-img/*.png|svg, the
 *      .ui-stamp file and the .gate-pass marker); a second .mjs, a .js chunk, a .map or a .json (the asset database turns
 *      *.json in a mod folder into Metadata assets, DefaultAssetFactory.cs:242 [1.6.2f1 Colossal.IO.AssetDatabase, ilspycmd])
 *      fails.
 *   2. the banner parses exactly like the game's UIModuleAsset.ParseModuleInfo (ported below): identifier line, Id = mod.json
 *      id, a Dependencies line (absent = AddTags(null) NRE in PostCreate), a System.Version-parseable Version, and Build = the
 *      stamp in build/.ui-stamp.
 *   3. the bundle contains the stamp, no dynamic import(), no internal "game-ui/" path, no .extend( / .override(, no
 *      <select>/<option>/<table> element and no inline grid / space-evenly / auto-margin style.
 *   4. the compiled CSS has no display:grid, grid-template, margin-left/right:auto (or an auto margin shorthand), :not(,
 *      space-evenly, and no var() inside a shorthand property (the UI.log warns "Custom CSS expressions are not supported in
 *      shorthand declaration"). Scoping: our CSS shares one document with the game and every mod, and it is the one failure no
 *      error boundary or try/catch can contain (`:global(div){pointer-events:none}` would freeze the whole UI). Every selector
 *      must be able to match only inside our own subtree: its subject (rightmost compound) carries one of our `.sr-` classes,
 *      or a compound carrying one is followed by a descendant or child combinator (a sibling combinator right after it,
 *      `.sr-x ~ div`, leaks beside our root). @media / @supports are checked inside; @keyframes and @font-face names must
 *      start with `sr-` (both names are global too); @charset is allowed; any other at-rule fails.
 *   5. src/ (TypeScript AST): every value import from cs2/* names a runtime export of the game (the typings declare more than
 *      exists; a missing name is `undefined` at runtime), no namespace/default import of cs2/* (except src/cs2shim.ts, whose
 *      every `<ns>.<Name>` access is checked the same way), no npm package other than react, no dynamic import(), no
 *      moduleRegistry call other than append, no .extend(/.override(, no select/option/table elements, no inline grid styles,
 *      no style import other than *.module.scss (a plain .scss compiles with global selectors), and every string / JSX text
 *      passes the glyph allowlist.
 *   6. mod/SkiResortMod/LocaleEN.cs: every SkiResort.Hub.* value passes the glyph allowlist.
 *   7. smoke test: the bundle is import()ed in Node against stub window modules (real React from node_modules); `default` must
 *      be a function, `hasCSS` must be true, the registrar must not throw, must call only moduleRegistry.append, only on the
 *      hooks we own, each time with a plain function component (append() treats a non-function second argument as a module
 *      PATH: index.js@213139), and every bindValue() must pass a fallback that is not undefined (a binding without one throws
 *      when C# lacks it: index.js@421257). Containment: the registrar is run a second time with a registry whose append()
 *      throws, as the game's does when a patch moves the ModdingHook path (append -> extend("game-ui/modding/modding-hook.tsx")
 *      throws "Module ... was not found.", installed index.js@~212540-213139); nothing may escape, because the game's registrar
 *      loop has no try/catch (index.js@800454). Each appended component is then called once as a plain function (it must stay
 *      hook-free) and must return an element whose type is an error boundary (static getDerivedStateFromError or
 *      prototype.componentDidCatch).
 *   8. contract and source rules (function checkContract below):
 *      R1 C#/TS name parity: SkiResortHubUISystem.cs `HubContract` == bindings.ts HUB_CONTRACT and `Group` == GROUP; every
 *         BINDING / TRIGGER name is registered in C# with a string literal (`new ValueBinding<T>(Group, "name", ...)`,
 *         `new RawValueBinding(Group, "name", ...)`, `new TriggerBinding<...>(Group, "name", ...)`) as the same kind, and C#
 *         registers nothing else; a value binding's C# type matches the TS type's kind; a trigger's C# generic arity equals its
 *         TS argument count. A constructed binding that is never passed to AddBinding( is never registered with the game, so
 *         the UI shows its fallback forever and C# logs nothing.
 *      R2 shape parity: for every `writer.TypeBegin("SkiResortMod.<X>")` block in mod/SkiResortMod (**\/*.cs, bin/obj
 *         excluded) the `PropertyName("...")` literals equal the fields of TS interface <X> (a typo would otherwise render
 *         `undefined` -> "–" silently); every interface a binding carries must have at least one writer.
 *      R3 contract lock (tools/contract.lock): names, types and fields per shape. A change without a HUB_CONTRACT bump FAILS; a
 *         core name (bindings.ts CORE_NAMES) renamed, retyped or removed, a core shape field removed or retyped, or a new
 *         non-optional core field FAILS. `npm run contract:lock` rewrites the lock after a deliberate bump (it refuses a core
 *         break and a change without a bump; the diff is reviewed).
 *      R4 locale parity: every `[key, english]` tuple of string literals in src/ whose key starts with SkiResort. / Assets. /
 *         SubServices. exists in root LocaleEN.cs with identical text; a SkiResort.Hub.* key used by neither src/ nor another
 *         .cs file -> WARNING.
 *      R5 placeholders: t(...) / translate(...) on an entry whose English contains "{" FAILS ("use LocalizedString": translate()
 *         never substitutes, index.js@435324).
 *      R6 inputs: <input> / <textarea> only in src/hub/RenameField.tsx, which must contain `stopPropagation` and a
 *         useLayoutEffect cleanup that calls `.blur()`.
 *      R7 no JSX `title=` attribute (it shows nothing in Gameface; use Tip).
 *      R8 CSS `gap`, `row-gap`, `column-gap`, `grid-gap` FAIL (compiled CSS and inline styles).
 *      R9 every CSS class whose name contains "blind" declares height: 0 and visibility: hidden.
 *      R10 every "Media/..." string literal in src/ exists under the installed Cities2_Data/Content/Game/UI (WARNING when no
 *         install path is set).
 *      R11 panel bindings only in panel-mounted files (PANEL_FILES); an always-mounted file may import only HubPanel from the
 *         panel files.
 *      R12 hooks never after an early return or inside a condition / loop.
 *      R13 placeholder entries only as <LocText entry={...}> (an alias of t or of the map would pass R5).
 *      R14 no untranslated JSX text / child string / visible prop string; R6 / R7 also cover createElement / jsx calls and
 *         `title` in spread or props object literals.
 *      R15 (function lintPanelEdgeFocus) a focusable cs2/ui button rendered (directly or through our components) in a cs2/ui
 *         Panel's header or footer must pass focusKey={FOCUS_DISABLED}: those slots host one focus child. Hub-wide: every other
 *         focusable cs2/ui element in src/ must pass it too (sections: a literal disableFocus): the hub is mouse-only and its
 *         controls never take the game's key focus (consistency; FIX-LIST 58 was a test-helper false positive).
 *      R16 build-item parity C# <-> TS: reads mod/SkiResortMod/Systems/SkiCatalog.cs (`--catalog-cs=` overrides it), the NAMES
 *         block (`public const string X = "Name"; // Type[, item id][, pending]`) and the BUILD-ITEMS block (`new
 *         BuildItemDef("id", Names.X, BuildItemKind.K, "type", n),`, the markers inside the braces: every line between them
 *         is a row) as raw text (the NAMES grammar lives in end-of-line comments and the markers are comments, so this rule
 *         does not strip comments), and compares them with bindings.ts BuildItemId / BUILD_ITEM_IDS / BUILD_ITEM_PREFAB /
 *         LIFT_TYPE_IDS and src/hub/buildItems.ts BUILD_ITEMS / BUILD_GROUPS / BUILD_ITEM_ICON (checkBuildItems).
 *      R17 enum parity: eight TS const objects against their C# enums.
 *      R18 the UI rules self-test (tools/test-hub-rules.js).
 *      R19 an element argument of <LocText> / <LocalizedString> (directly, through ?: && || ??, or one same-file variable) is a
 *         loc component (LOC_ARG_COMPONENTS): the game renders it through its type's renderString and prints "[name/]" for any
 *         other component (2026-11-02: "Car spaces at [_i/]" while LocText had no renderString).
 *      `node tools/check-bundle.js --contract-only` runs R1-R7, R10 and R11-R19 plus the source lint without a bundle;
 *      `--hub-cs=`, `--catalog-cs=`, `--locale=`, `--lock=`, `--src=` and `--cs-dir=` point the checks at other files (fault
 *      tests; any override is reported as a WARNING).
 *
 * Runtime export lists: parsed at check time from the installed game's index.js (so a patch that removes an export fails the
 * build on patch day). When CSII_INSTALLATIONPATH is set but that index.js cannot be read or its export table cannot be parsed,
 * the gate FAILS and names the reason (falling back silently to the 1.6.2f1 lists below would disable the patch-day check
 * exactly when a patch changed the file). The built-in lists are used only when no install path is set at all, and the report
 * says so.
 */
"use strict";

const fs = require("fs");
const path = require("path");
const { pathToFileURL } = require("url");
const { readCsiiVar } = require("./user-env");

const ROOT = path.resolve(__dirname, "..");
const BUILD = path.join(ROOT, "build");

/** Path overrides for fault tests: `--src=`, `--locale=`, `--hub-cs=`, `--cs-dir=`, `--lock=`. Every override in use is reported as a WARNING, so a gate run on the wrong files can never pass silently. */
const ARGS = process.argv.slice(2);
function argValue(name) {
  const hit = ARGS.find((a) => a.startsWith(`--${name}=`));
  return hit ? path.resolve(hit.slice(name.length + 3)) : null;
}
const OVERRIDES = [];
function pathArg(name, fallback) {
  const v = require.main === module ? argValue(name) : null;
  if (v) OVERRIDES.push(`--${name}=${v}`);
  return v || fallback;
}

const SRC = pathArg("src", path.join(ROOT, "src"));
const CS_DIR = pathArg("cs-dir", path.resolve(ROOT, "..", "SkiResortMod"));
const LOCALE_EN = pathArg("locale", path.join(CS_DIR, "LocaleEN.cs"));
/** The C# side of the binding contract (rule R1) */
const HUB_CS = pathArg("hub-cs", path.join(CS_DIR, "Systems", "SkiResortHubUISystem.cs"));
/** Rule R16: the one build-item table and the NAMES block */
const CATALOG_CS = pathArg("catalog-cs", path.join(CS_DIR, "Systems", "SkiCatalog.cs"));
/** Rule R3. Written by `npm run contract:lock` (= node tools/check-bundle.js --write-lock). */
const CONTRACT_LOCK = pathArg("lock", path.join(ROOT, "tools", "contract.lock"));
/** The one file allowed an <input> (rule R6) */
const RENAME_FIELD = "hub/RenameField.tsx";
/** The contract's source of truth */
const BINDINGS_TS = "bindings.ts";
/**
 * Rule R11: the bindings whose observers make C# compute for the panel may be referenced only in files that mount while the
 * panel is open. An always-mounted subscriber (index.tsx, HubRoot, HubLauncher, hubState) would keep the catalog / stats running
 * with the panel closed. A new panel file must be added here on purpose. SkiResortHubUISystem's Demand = build, resorts,
 * resortLive, runs, runLive, lifts, problems, city observed; selectedResort and glade are panel bindings too; pisteMap, follow,
 * history and liftLine are panel bindings without Demand.
 */
const PANEL_BINDINGS = new Set([
  "resorts$", "resortLive$", "selectedResort$", "runs$", "runLive$", "lifts$", "problems$", "city$",
  "build$", "glade$", "pisteMap$", "follow$",
  "history$",
  "liftLine$",
]);
/**
 * Rule R11: the always-mounted files (they render while the hub is closed) may import only HubPanel from the panel files (a
 * value import; type-only imports are erased). An always-mounted file that imports and renders <StatusBlock/> or any other panel
 * component passes the per-binding check (it references no panel binding itself) but mounts that component's subscriptions
 * with the panel closed.
 */
const ALWAYS_MOUNTED_FILES = new Set([
  "index.tsx", "hub/HubRoot.tsx", "hub/HubLauncher.tsx", "hub/hubState.ts", "hub/ErrorBoundary.tsx", "hub/BusCard.tsx",
]);
const PANEL_ENTRY = "hub/HubPanel.tsx";
const PANEL_FILES = new Set([
  "hub/HubPanel.tsx", "hub/HubHeader.tsx", "hub/StartCard.tsx", "hub/StatusBlock.tsx", "hub/ViewTiles.tsx",
  "hub/OverviewView.tsx", "hub/RunsView.tsx", "hub/RunList.tsx", "hub/LiftsView.tsx", "hub/LiftTable.tsx",
  "hub/GuestsView.tsx", "hub/MoneyView.tsx", "hub/BuildStrip.tsx", "hub/TreeControls.tsx",
  "hub/HistoryView.tsx", "hub/HistoryChart.tsx",
]);
/** Rule R16: the TS file that holds BUILD_GROUPS */
const BUILD_ITEMS_TS = path.join("hub", "buildItems.ts");
/**
 * Rule R17: the TS const objects of bindings.ts and the C# enums (file under CS_DIR) that must hold the same names with the same
 * values, both directions. BAND and LIFT_STATE have no C# enum (literal ints in C#) and are not compared. Systems/SkiDayRules.cs
 * is in the csproj's SkiUiInput list, so an edit there re-runs this gate.
 */
const ENUM_PAIRS = [
  ["RUN_KIND", "Systems/EntityRef.cs", "RunKind"],
  ["RESORT_STATUS", "Systems/HubRules.cs", "ResortStatus"],
  ["OPEN_STATE", "Systems/HubRules.cs", "RunOpenState"],
  ["SEASON_HINT", "Systems/HubRules.cs", "SeasonHint"],
  ["NEXT_STEP", "Systems/HubRules.cs", "NextStepKind"],
  ["LIFT_STATUS", "Systems/HubRules.cs", "HubLiftStatus"],
  ["RUN_GUN_WHY", "Systems/HubRules.cs", "RunGunWhy"],
  ["PROBLEM", "Systems/HubRules.cs", "HubProblemCode"],
  ["TICKET_GATE", "Systems/SkiMoneyRules.cs", "TicketGate"],
  ["SKI_DAY_STATE", "Systems/HubRules.cs", "HubSkiDayState"],
  ["SKI_DAY_LIMIT", "Systems/SkiDayRules.cs", "SkiDayLimit"],
];
/** Rule R19: components the game can render as a placeholder argument (a renderString and a propsAreEqual; LocText carries
 * LocalizedString's, src/hub/parts.tsx) */
const LOC_ARG_COMPONENTS = new Set(["LocText", "LocalizedString", "LocalizedNumber", "LocalizedBounds", "LocalizedFraction"]);
/** Rule R14: JSX props whose string-literal value the player reads */
const VISIBLE_PROPS = new Set(["label", "tip", "tooltip", "tooltipLabel", "placeholder", "aria-label", "alt", "title", "text", "header"]);
/** Rule R14: a letter in any script (an untranslated word) */
const LETTER_RE = /\p{L}/u;
const MOD = JSON.parse(fs.readFileSync(path.join(ROOT, "mod.json"), "utf8"));
const STAMP_FILE = ".ui-stamp";
/** Written (content = the stamp) only by a passing run; see "The gate marker" in the header */
const GATE_FILE = ".gate-pass";
const STAMP_RE = /^[A-Za-z0-9._-]{1,64}$/;
/** Text of the "/*!" comment in src/index.tsx that guarantees terser extracts >= 1 comment */
const BANNER_ANCHOR = "SkiResortMod UI module";

/** The one source file allowed a namespace import of cs2/* (src/cs2shim.ts header says why) */
const SHIM_FILE = "cs2shim.ts";

/** The append hooks this mod may use */
const ALLOWED_HOOKS = ["UniversalModMenu", "GameTopLeft", "Game"];

/**
 * Runtime exports of the game's window modules on 1.6.2f1, read from the installed Cities2_Data/Content/Game/UI/index.js: window
 * names at @799710 (`Object.defineProperties(window, {...})`), each module object built by `n.r(X),n.d(X,{name:()=>...})` -
 * cs2/api @191690, cs2/l10n @192007, cs2/ui @197852, cs2/bindings @209234, cs2/utils @209823, cs2/input @210083, cs2/modding
 * @212063. Fallback only: the live parse below wins when it succeeds.
 */
const RUNTIME_EXPORTS_1_6_2 = {
  "cs2/api": "bindEvent bindLocalValue bindMap bindTrigger bindTriggerWithArgs bindValue call trigger useMapValue useMapValueOnChange useMapValues useReducedValue useValue useValueOnChange useValueRef",
  "cs2/bindings": "budget camera chirper cinematic cityInfo climate devTree economyBudget event feature game infoview infoviewTypes life loan map milestone photo policy prefab prefabEffects prefabProperties prefabRequirements production radio selectedInfo service signatureBuilding statistics taxation time tool toolbar toolbarBottom transport tutorial upgrade",
  "cs2/l10n": "Localized LocalizedBounds LocalizedDate LocalizedDuration LocalizedEntityName LocalizedFraction LocalizedNumber LocalizedPercentage LocalizedString Unit useLocalization",
  "cs2/ui": "Button ConfirmationDialog DialogContext DialogRenderer DialogStack Dropdown DropdownItem DropdownToggle FloatingButton FormattedParagraphs FormattedText Icon MarkdownRenderer MarkupRenderer MenuButton Panel PanelFoldout PanelSection PanelSectionRow Portal Scrollable Tooltip",
  "cs2/utils": "entityEquals entityKey formatLargeNumber isNullOrEmpty parseEntityKey preloadImages shallowEqual useCssLength useFormattedLargeNumber useMemoizedValue useRem",
  "cs2/input": "ActionHintLayout ActiveControlSchemeInputHint ActiveFocusDiv AutoNavigationScope BackConsumer ControlIcon ControlIcons EventInputProvider ExpandConsumer FOCUS_AUTO FOCUS_DISABLED FocusActivation FocusBoundary FocusContext FocusControllerBase FocusDisabled FocusKeyOverride FocusNode FocusRoot FocusScope FocusSymbol FocusedInputHint GamepadAxis GamepadButton GamepadPointerEventProvider InputActionBarrier InputActionConsumer InputActionHints InputContext InputControllerImpl InputHint InputStack MultiChildFocusController NativeInputProvider NavigationDirection NavigationScope PassiveFocusDiv PointerBarrier RootInputControllerImpl SelectConsumer SelectableFocusBoundary defaultInputController disabledFocusController focusAnchorBottom focusAnchorCenter focusAnchorLeft focusAnchorRight focusAnchorTop gamepadButtonFromString getClosestKey getClosestKeyInDirection getElementFocusPosition inputActionNames$ navActions onInputActionPerformed$ onInputActionReleased$ refocusClosestKey refocusClosestKeyIfNoFocus setInputActionPriority transformNavigationInput useElementFocusController useEntityFocusChangeListener useFocusCallback useFocusChangeListener useFocused useFocusedRef useGamepadType useInputControlIcon useInputController useKeyFocusController useKeyboardLayout useLayoutMap useMultiChildFocusController usePassThroughFocusController useRootFocusController useStringFocusChangeListener useUniqueFocusKey",
  "cs2/modding": "findModule getModule",
};
/** A name each module must contain, to pick the right `n.d(X,{...})` when a minified name repeats */
const ANCHOR = { "cs2/api": "bindValue", "cs2/bindings": "tool", "cs2/l10n": "useLocalization", "cs2/ui": "Panel", "cs2/utils": "entityKey", "cs2/input": "FOCUS_AUTO", "cs2/modding": "getModule" };

/**
 * Glyphs that render in every in-game font chain (Overpass, Noto Sans, CJK, Thai), read from the fonts' character maps, beside
 * printable ASCII: arrows, ellipsis, middle dot, en dash, multiplication sign, degree, superscript two, no-break space, and
 * "≈" (U+2248: in the cmap of Overpass-Regular/-Bold.ttf and NotoSans-Regular/-Bold.ttf of the installed
 * Cities2_Data/Content/Game/UI/Fonts, and every --fontFamily chain of index.css contains "Noto Sans", index.css@1349). Not
 * safe: the em dash was not on the checked list, and triangles / circles / diamonds / check marks exist only in fallback
 * fonts. Extend this list only with evidence.
 */
const SAFE_GLYPHS = new Set(["←", "→", "↑", "↓", "…", "·", "–", "×", "°", "²", "≈"," "]);
const CSS_SHORTHANDS = ["margin", "padding", "border", "border-top", "border-right", "border-bottom", "border-left",
  "border-width", "border-style", "border-color", "border-radius", "background", "font", "flex", "flex-flow",
  "transition", "animation", "outline", "inset", "gap", "overflow", "text-decoration", "list-style", "place-items",
  "place-content", "columns", "mask"];


function listFiles(dir, base = dir) {
  const out = [];
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, e.name);
    if (e.isDirectory()) out.push(...listFiles(full, base));
    else out.push(path.relative(base, full).split(path.sep).join("/"));
  }
  return out;
}

/** The files that go next to the DLL (relative to build/). Used by the deploy plugin; the csproj mirrors it. */
function bundleFiles(buildDir = BUILD) {
  if (!fs.existsSync(buildDir)) return [];
  return listFiles(buildDir).filter((f) => f !== STAMP_FILE && f !== GATE_FILE).sort();
}

function isAllowedBuildFile(rel) {
  return rel === `${MOD.id}.mjs` || rel === `${MOD.id}.css` || rel === `${MOD.id}.mjs.LICENSE.txt` ||
    rel === STAMP_FILE || rel === GATE_FILE || new RegExp(`^${MOD.id}-img/[^/]+\\.(png|svg)$`, "i").test(rel);
}

/** Deletes build/.gate-pass. Never throws (a marker that cannot be deleted is reported by the caller's re-read). */
function removeGateMarker(buildDir = BUILD) {
  try { fs.rmSync(path.join(buildDir, GATE_FILE), { force: true }); } catch { /* reported by readGateMarker */ }
}

/** The stamp a passing run recorded in build/.gate-pass, or null */
function readGateMarker(buildDir = BUILD) {
  try { return fs.readFileSync(path.join(buildDir, GATE_FILE), "utf8").trim() || null; } catch { return null; }
}

/** Port of Colossal.IO.AssetDatabase.UIModuleAsset.ParseModuleInfo / ProcessLine (1.6.2f1) */
function parseModuleInfo(text) {
  const info = { id: null, author: null, version: null, versionRaw: null, deps: null, identifier: false, extra: {} };
  let inComment = false;
  for (const raw of text.split(/\r?\n/)) {
    const t = raw.trim();
    if (!inComment) { if (t.startsWith("/*")) inComment = true; continue; }
    if (t.startsWith("*")) {
      const line = t.replace(/^\*+/, "").trim();
      if (!line) continue;
      if (line.startsWith("Cities: Skylines II UI Module")) {
        if (line !== "Cities: Skylines II UI Module") break;
        info.identifier = true;
        continue;
      }
      const i = line.indexOf(":");
      if (i < 0) break;
      const key = line.slice(0, i).trim();
      const val = line.slice(i + 1).trim();
      if (key === "Id") info.id = val;
      else if (key === "Author") info.author = val;
      else if (key === "Version") { info.versionRaw = val; info.version = parseDotNetVersion(val); }
      else if (key === "Dependencies") info.deps = val.split(",").map((s) => s.trim());
      else info.extra[key] = val;
    } else if (t.endsWith("*/")) break;
  }
  return info;
}

/** System.Version.TryParse: 2-4 non-negative int32 components */
function parseDotNetVersion(s) {
  if (!/^\d+(\.\d+){1,3}$/.test(s)) return null;
  return s.split(".").every((p) => Number(p) <= 2147483647) ? s : null;
}


/**
 * The game's runtime exports. `problem` is non-null when an install path is set but its index.js could not be read or parsed:
 * checkBundle then FAILS. Only a missing install path falls back to the built-in 1.6.2f1 lists without an error.
 */
function readRuntimeExports() {
  const install = readCsiiVar("CSII_INSTALLATIONPATH");
  const fallback = (why, isProblem) => ({
    lists: Object.fromEntries(Object.entries(RUNTIME_EXPORTS_1_6_2).map(([k, v]) => [k, new Set(v.split(" "))])),
    source: `built-in 1.6.2f1 list (${why})`,
    drift: [],
    problem: isProblem ? why : null,
  });
  if (!install.value) return fallback("CSII_INSTALLATIONPATH not set", false);
  const indexJs = path.join(install.value, "Cities2_Data", "Content", "Game", "UI", "index.js");
  let js;
  try { js = fs.readFileSync(indexJs, "utf8"); } catch (e) { return fallback(`cannot read ${indexJs} (${e && e.code ? e.code : e})`, true); }
  const defAt = js.indexOf("Object.defineProperties(window,{");
  if (defAt < 0) return fallback(`window module table "Object.defineProperties(window,{" not found in ${indexJs}`, true);
  const defText = js.slice(defAt, defAt + 800);
  const lists = {};
  for (const mod of Object.keys(RUNTIME_EXPORTS_1_6_2)) {
    const m = defText.match(new RegExp(`"${mod.replace("/", "\\/")}":\\{value:([A-Za-z_$][\\w$]*)\\}`));
    if (!m) return fallback(`${mod} not in the window module table of ${indexJs}`, true);
    const v = m[1].replace(/\$/g, "\\$");
    const re = new RegExp(`([A-Za-z_$][\\w$]*)\\.r\\(${v}\\),\\1\\.d\\(${v},\\{([^{}]*)\\}`, "g");
    let found = null;
    for (const hit of js.matchAll(re)) {
      const names = [...hit[2].matchAll(/(?:^|,)([A-Za-z_$][\w$]*):\(\)=>/g)].map((x) => x[1]);
      if (names.includes(ANCHOR[mod])) { found = names; break; }
    }
    if (!found) return fallback(`export object of ${mod} not found in ${indexJs}`, true);
    lists[mod] = new Set(found);
  }
  const drift = [];
  for (const [mod, v] of Object.entries(RUNTIME_EXPORTS_1_6_2)) {
    const known = new Set(v.split(" "));
    for (const n of known) if (!lists[mod].has(n)) drift.push(`${mod}: '${n}' no longer exported by the installed game`);
    for (const n of lists[mod]) if (!known.has(n)) drift.push(`${mod}: '${n}' is new in the installed game`);
  }
  return { lists, source: `installed game (${indexJs})`, drift, problem: null };
}


function badGlyph(text, allowNewlines) {
  for (const ch of text) {
    const c = ch.codePointAt(0);
    if (c >= 0x20 && c <= 0x7e) continue;
    if (ch === "\t" || (allowNewlines && (ch === "\n" || ch === "\r"))) continue;
    if (SAFE_GLYPHS.has(ch)) continue;
    return `U+${c.toString(16).toUpperCase().padStart(4, "0")} '${ch}'`;
  }
  return null;
}


function lintSources(runtime, errors) {
  let ts;
  try { ts = require("typescript"); } catch { errors.push("src: typescript is not installed (run npm ci)"); return 0; }
  if (!fs.existsSync(SRC)) { errors.push("src/ is missing"); return 0; }
  const files = listFiles(SRC).filter((f) => /\.tsx?$/.test(f) && !f.endsWith(".d.ts"));
  const STYLE_RULES = {
    display: /^(inline-)?grid$/, justifyContent: /space-evenly/, alignContent: /space-evenly/,
    marginLeft: /^auto$/, marginRight: /^auto$/, margin: /\bauto\b/,
    gridTemplate: /./, gridTemplateColumns: /./, gridTemplateRows: /./, gridArea: /./,
    gap: /./, rowGap: /./, columnGap: /./, gridGap: /./,
  };
  const BAD_TAGS = new Set(["select", "option", "table", "thead", "tbody", "tr", "td", "th"]);

  for (const rel of files) {
    const file = path.join(SRC, rel);
    const text = fs.readFileSync(file, "utf8");
    const sf = ts.createSourceFile(file, text, ts.ScriptTarget.ES2020, true,
      rel.endsWith(".tsx") ? ts.ScriptKind.TSX : ts.ScriptKind.TS);
    const at = (node) => `src/${rel}:${sf.getLineAndCharacterOfPosition(node.getStart(sf)).line + 1}`;
    /** src/cs2shim.ts only: namespace identifier -> cs2 module; every `<ns>.<Name>` is checked */
    const shimNamespaces = new Map();
    const unwrap = (e) => {
      while (ts.isParenthesizedExpression(e) || ts.isAsExpression(e) || ts.isNonNullExpression(e) ||
             (ts.isTypeAssertionExpression && ts.isTypeAssertionExpression(e))) e = e.expression;
      return e;
    };

    const checkModuleRef = (node, spec, clause, isExport) => {
      if (spec.startsWith(".")) {
        // A plain .scss/.css compiles with global selectors (webpack.config.js modules.auto only localizes *.module.s?css) into a
        // document shared by every mod
        if (/\.(s[ac]ss|css|less|styl)$/i.test(spec) && !/\.module\.scss$/i.test(spec)) {
          errors.push(`${at(node)}: imports "${spec}" - only *.module.scss stylesheets (class names scoped "sr-...") may be imported`);
        }
        return;
      }
      if (spec === "react" || spec === "react/jsx-runtime" || spec === "react-dom") return;
      if (spec === "cohtml/cohtml") {
        if (!(clause && clause.isTypeOnly)) errors.push(`${at(node)}: value import from "cohtml/cohtml" - use cs2/api (trigger/bindValue) instead`);
        return;
      }
      const list = runtime.lists[spec];
      if (!list) { errors.push(`${at(node)}: imports "${spec}" - only react, cs2/* and relative files may be imported (an npm package would be bundled)`); return; }
      if (!clause) { errors.push(`${at(node)}: bare import of "${spec}"`); return; }
      if (clause.isTypeOnly) return;
      if (!isExport && clause.name) errors.push(`${at(node)}: default import from "${spec}" - the game module has no default export`);
      const nb = isExport ? clause.exportClause : clause.namedBindings;
      if (!nb) { if (isExport) errors.push(`${at(node)}: export * from "${spec}"`); return; }
      if (ts.isNamespaceImport(nb) && rel === SHIM_FILE) { shimNamespaces.set(nb.name.text, spec); return; }
      if (ts.isNamespaceImport(nb) || ts.isNamespaceExport(nb)) { errors.push(`${at(node)}: namespace import of "${spec}" - import named runtime exports so this gate can check them (only src/${SHIM_FILE} may, see its header)`); return; }
      for (const el of nb.elements) {
        if (el.isTypeOnly) continue;
        const name = (el.propertyName || el.name).text;
        if (!list.has(name)) errors.push(`${at(node)}: "${name}" is not a runtime export of ${spec} (it would be undefined in game). ` +
          "Use `import type` for types; runtime names: see tools/check-bundle.js");
      }
    };

    const visit = (node) => {
      if (ts.isImportDeclaration(node)) {
        checkModuleRef(node, node.moduleSpecifier.text, node.importClause, false);
      } else if (ts.isExportDeclaration(node) && node.moduleSpecifier) {
        checkModuleRef(node, node.moduleSpecifier.text, { isTypeOnly: node.isTypeOnly, exportClause: node.exportClause }, true);
      } else if (ts.isCallExpression(node)) {
        if (node.expression.kind === ts.SyntaxKind.ImportKeyword) errors.push(`${at(node)}: dynamic import() - the bundle must stay ONE .mjs`);
        if (ts.isPropertyAccessExpression(node.expression)) {
          const method = node.expression.name.text;
          const recv = node.expression.expression.getText(sf);
          if (method === "extend" || method === "override") errors.push(`${at(node)}: .${method}() - the hub uses the game's append hooks ONLY (OD3 deferred)`);
          else if (/registry/i.test(recv) && method !== "append") errors.push(`${at(node)}: ${recv}.${method}() - only moduleRegistry.append is allowed`);
        }
      } else if (ts.isPropertyAccessExpression(node) && shimNamespaces.size) {
        const target = unwrap(node.expression);
        const spec = ts.isIdentifier(target) ? shimNamespaces.get(target.text) : undefined;
        if (spec && !runtime.lists[spec].has(node.name.text)) {
          errors.push(`${at(node)}: "${node.name.text}" is not a runtime export of ${spec} (it would be undefined in game)`);
        }
      } else if (ts.isJsxOpeningElement(node) || ts.isJsxSelfClosingElement(node)) {
        const tag = node.tagName.getText(sf);
        if (BAD_TAGS.has(tag)) errors.push(`${at(node)}: <${tag}> - Gameface has no select/option/table; use cs2/ui Dropdown and flex rows`);
      } else if (ts.isPropertyAssignment(node) && (ts.isIdentifier(node.name) || ts.isStringLiteral(node.name)) &&
                 ts.isStringLiteralLike(node.initializer)) {
        const rule = STYLE_RULES[node.name.text];
        if (rule && rule.test(node.initializer.text)) errors.push(`${at(node)}: style ${node.name.text}: "${node.initializer.text}" is not supported by Gameface 2.2.1 (flex only, no auto margins, no space-evenly)`);
      }
      const isTemplatePart = ts.isNoSubstitutionTemplateLiteral(node) || ts.isTemplateHead(node) ||
        ts.isTemplateMiddle(node) || ts.isTemplateTail(node);
      if (ts.isStringLiteral(node) || isTemplatePart || ts.isJsxText(node)) {
        const bad = badGlyph(node.text, ts.isJsxText(node) || isTemplatePart);
        if (bad) errors.push(`${at(node)}: glyph ${bad} is not on the safe list (research 2.F.2)`);
      }
      ts.forEachChild(node, visit);
    };
    visit(sf);
  }
  return files.length;
}


function csTokens(text) {
  const toks = [];
  let i = 0, line = 1;
  while (i < text.length) {
    const c = text[i];
    if (c === "\n") { line++; i++; continue; }
    if (c === "/" && text[i + 1] === "/") { while (i < text.length && text[i] !== "\n") i++; continue; }
    if (c === "/" && text[i + 1] === "*") {
      const e = text.indexOf("*/", i + 2), end = e < 0 ? text.length : e + 2;
      line += (text.slice(i, end).match(/\n/g) || []).length; i = end; continue;
    }
    if (c === "@" && text[i + 1] === "\"") {
      let j = i + 2, v = "";
      while (j < text.length) { if (text[j] === "\"") { if (text[j + 1] === "\"") { v += "\""; j += 2; continue; } break; } if (text[j] === "\n") line++; v += text[j++]; }
      toks.push({ t: "s", v, line }); i = j + 1; continue;
    }
    if (c === "\"" || (c === "$" && text[i + 1] === "\"")) {
      let j = c === "$" ? i + 2 : i + 1, v = "";
      while (j < text.length && text[j] !== "\"") {
        if (text[j] === "\\") {
          const n = text[j + 1];
          if (n === "u") { v += String.fromCharCode(parseInt(text.slice(j + 2, j + 6), 16)); j += 6; continue; }
          v += ({ n: "\n", t: "\t", r: "\r", "0": "\0" })[n] ?? n; j += 2; continue;
        }
        v += text[j++];
      }
      toks.push({ t: "s", v, line }); i = j + 1; continue;
    }
    if (c === "'") { let j = i + 1; while (j < text.length && text[j] !== "'") j += text[j] === "\\" ? 2 : 1; i = j + 1; toks.push({ t: "x", line }); continue; }
    if (c === "{" || c === "}" || c === "," || c === "+") { toks.push({ t: c, line }); i++; continue; }
    if (/\s/.test(c)) { i++; continue; }
    toks.push({ t: "x", line }); i++;
  }
  return toks;
}

function lintLocale(errors, warnings) {
  if (!fs.existsSync(LOCALE_EN)) { warnings.push(`LocaleEN.cs not found at ${LOCALE_EN}; hub strings not checked`); return 0; }
  const toks = csTokens(fs.readFileSync(LOCALE_EN, "utf8"));
  let count = 0;
  for (let k = 1; k + 2 < toks.length; k++) {
    const key = toks[k];
    if (key.t !== "s" || !key.v.startsWith("SkiResort.Hub.") || toks[k - 1].t !== "{" || toks[k + 1].t !== ",") continue;
    let value = "", j = k + 2, ok = true;
    for (; j < toks.length && toks[j].t !== "}"; j++) {
      if (toks[j].t === "s") value += toks[j].v;
      else if (toks[j].t !== "+") { ok = false; break; }
    }
    if (!ok) continue;
    count++;
    const bad = badGlyph(value, true);
    if (bad) errors.push(`LocaleEN.cs:${key.line}: ${key.v} uses glyph ${bad}, not on the safe list (research 2.F.2)`);
  }
  return count;
}


function lintCss(css, errors) {
  const noComments = css.replace(/\/\*[\s\S]*?\*\//g, "");
  const flat = noComments.replace(/\s+/g, " ");
  if (/:not\(/i.test(flat)) errors.push(`${MOD.id}.css: ':not(' selector - unsupported by Gameface 2.2.1`);
  for (const m of noComments.matchAll(/\{([^{}]*)\}/g)) {
    for (const decl of m[1].split(";")) {
      const i = decl.indexOf(":");
      if (i < 0) continue;
      const prop = decl.slice(0, i).trim().toLowerCase();
      const val = decl.slice(i + 1).trim().toLowerCase().replace(/\s*!important$/, "");
      const where = `${MOD.id}.css: '${prop}: ${val}'`;
      if (prop === "display" && /^(inline-)?grid$/.test(val)) errors.push(`${where} - no CSS Grid in Gameface (flex only)`);
      if (prop.startsWith("grid-template") || prop === "grid-area") errors.push(`${where} - no CSS Grid in Gameface`);
      if ((prop === "margin-left" || prop === "margin-right") && val === "auto") errors.push(`${where} - auto margins arrived in Gameface 3.0 (research 2.F.1)`);
      if (prop === "margin" && /\bauto\b/.test(val)) errors.push(`${where} - auto margins arrived in Gameface 3.0 (research 2.F.1)`);
      if (/space-evenly/.test(val)) errors.push(`${where} - no space-evenly in Gameface 2.2.1`);
      if (CSS_SHORTHANDS.includes(prop) && /var\(/.test(val)) errors.push(`${where} - var() in a shorthand is not supported; write the longhand properties`);
      if (prop === "gap" || prop === "row-gap" || prop === "column-gap" || prop === "grid-gap") {
        errors.push(`${where} - flex gap is unprobed in Gameface 2.2.1 (research 2.F.1); use margins (rule R8, spec 4.7 item 8)`);
      }
    }
  }
  const bad = badGlyph(noComments, true);
  if (bad) errors.push(`${MOD.id}.css: glyph ${bad} is not on the safe list (research 2.F.2)`);
  lintBlindClasses(noComments, errors);
  return lintCssScoping(noComments, errors, "");
}

/** Every rule (selector text, declarations) of a CSS rule list, recursing into @media / @supports */
function cssRules(text, out = []) {
  for (const it of cssItems(text)) {
    if (it.block === null || it.unclosed || it.stray) continue;
    const head = it.prelude.replace(/\s+/g, " ").trim();
    if (head.startsWith("@")) {
      if (/^@(media|supports)\b/i.test(head)) cssRules(it.block, out);
      continue;
    }
    const decls = new Map();
    for (const d of it.block.split(";")) {
      const i = d.indexOf(":");
      if (i < 0) continue;
      decls.set(d.slice(0, i).trim().toLowerCase(), d.slice(i + 1).trim().toLowerCase().replace(/\s*!important$/, ""));
    }
    out.push({ selectors: splitTopLevel(head, ",").map((s) => s.trim()), decls });
  }
  return out;
}

/**
 * Rule R9: a class whose name contains "blind" is a width sample (vanilla FullWidthDigits, index.css@127211
 * `.blind_u0R{height:0;visibility:hidden}`); one that shows or takes height would put a second copy of a number on screen. Its
 * own rule(s) - selector exactly `.<class>` - must declare height: 0 and visibility: hidden.
 */
function lintBlindClasses(text, errors) {
  const rules = cssRules(text);
  const names = new Set();
  for (const r of rules) for (const s of r.selectors) for (const m of s.matchAll(/\.([A-Za-z0-9_-]*blind[A-Za-z0-9_-]*)/gi)) names.add(m[1]);
  for (const name of names) {
    let height = false, hidden = false;
    for (const r of rules) {
      if (!r.selectors.includes(`.${name}`)) continue;
      if (/^0(px|rem|em)?$/.test(r.decls.get("height") || "")) height = true;
      if ((r.decls.get("visibility") || "") === "hidden") hidden = true;
    }
    if (!height || !hidden) {
      errors.push(`${MOD.id}.css: class .${name} - a "blind" (width-sample) class must declare height: 0 and visibility: hidden ` +
        `in its own rule (missing: ${[!height && "height: 0", !hidden && "visibility: hidden"].filter(Boolean).join(", ")}; rule R9)`);
    }
  }
}


/** Index just past the string literal whose opening quote is text[i] */
function skipString(text, i) {
  const q = text[i];
  let j = i + 1;
  while (j < text.length && text[j] !== q) j += text[j] === "\\" ? 2 : 1;
  return j + 1;
}

/** Index of the "}" that closes the "{" at text[i], or -1 */
function matchBrace(text, i) {
  let depth = 0;
  for (let j = i; j < text.length; j++) {
    const c = text[j];
    if (c === "\\") { j++; continue; }
    if (c === "\"" || c === "'") { j = skipString(text, j) - 1; continue; }
    if (c === "{") depth++;
    else if (c === "}" && --depth === 0) return j;
  }
  return -1;
}

/** Top-level items of a CSS rule list (comments removed): { prelude, block|null, unclosed?, stray? } */
function cssItems(text) {
  const items = [];
  let start = 0;
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (c === "\\") { i++; continue; }
    if (c === "\"" || c === "'") { i = skipString(text, i) - 1; continue; }
    if (c === ";") {
      const p = text.slice(start, i).trim();
      if (p) items.push({ prelude: p, block: null });
      start = i + 1;
    } else if (c === "{") {
      const end = matchBrace(text, i);
      const prelude = text.slice(start, i).trim();
      if (end < 0) { items.push({ prelude, block: null, unclosed: true }); return items; }
      items.push({ prelude, block: text.slice(i + 1, end) });
      i = end;
      start = end + 1;
    } else if (c === "}") {
      items.push({ prelude: text.slice(start, i).trim(), block: null, stray: true });
      start = i + 1;
    }
  }
  const rest = text.slice(start).trim();
  if (rest) items.push({ prelude: rest, block: null });
  return items;
}

/** Splits at top-level `sep` characters (outside (), [] and strings) */
function splitTopLevel(text, sep) {
  const parts = [];
  let depth = 0, start = 0;
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (c === "\\") { i++; continue; }
    if (c === "\"" || c === "'") { i = skipString(text, i) - 1; continue; }
    if (c === "(" || c === "[") depth++;
    else if (c === ")" || c === "]") depth--;
    else if (c === sep && depth === 0) { parts.push(text.slice(start, i)); start = i + 1; }
  }
  parts.push(text.slice(start));
  return parts;
}

/** A complex selector's compounds and the combinators between them (" ", ">", "+", "~") */
function parseComplexSelector(sel) {
  const compounds = [], combinators = [];
  let cur = "", pending = null, depth = 0;
  for (let i = 0; i < sel.length; i++) {
    const c = sel[i];
    if (c === "\\") { cur += sel.slice(i, i + 2); i++; continue; }
    if (c === "\"" || c === "'") { const j = skipString(sel, i); cur += sel.slice(i, j); i = j - 1; continue; }
    if (c === "(" || c === "[") depth++;
    else if (c === ")" || c === "]") depth--;
    else if (depth === 0 && (c === ">" || c === "+" || c === "~" || /\s/.test(c))) {
      if (cur) { compounds.push(cur); cur = ""; pending = " "; }
      if (!/\s/.test(c)) pending = c;
      continue;
    }
    if (!cur && pending !== null) {
      if (compounds.length) combinators.push(pending);
      pending = null;
    }
    cur += c;
  }
  if (cur) compounds.push(cur);
  return { compounds, combinators };
}

/** Does a compound selector carry one of our classes (".sr-...") outside any (...) or [...]? */
function compoundHasOurClass(compound) {
  let depth = 0;
  for (let i = 0; i < compound.length; i++) {
    const c = compound[i];
    if (c === "\\") { i++; continue; }
    if (c === "(" || c === "[") depth++;
    else if (c === ")" || c === "]") depth--;
    else if (depth === 0 && c === "." && compound.startsWith("sr-", i + 1)) return true;
  }
  return false;
}

/**
 * Why a complex selector could match an element outside our own subtree, or null when it cannot. Safe: the subject (rightmost
 * compound) carries an .sr- class, or some compound carrying one is followed by a descendant (" ") or child (">") combinator -
 * every later compound then lies strictly inside that element's subtree (a sibling of a strict descendant is still inside it).
 */
function selectorLeak(sel) {
  const { compounds, combinators } = parseComplexSelector(sel);
  if (!compounds.length) return "empty selector";
  const n = compounds.length - 1;
  if (compoundHasOurClass(compounds[n])) return null;
  for (let i = 0; i < n; i++) {
    if (compoundHasOurClass(compounds[i]) && (combinators[i] === " " || combinators[i] === ">")) return null;
  }
  return compounds.some(compoundHasOurClass)
    ? "only a sibling combinator (+ / ~) follows our .sr- class, so it styles elements BESIDE our subtree"
    : "it carries no .sr- class of ours, so it can match the game's or another mod's elements";
}

/** Checks every selector of a CSS rule list; recurses into @media / @supports. Returns the selector count. */
function lintCssScoping(text, errors, where) {
  let count = 0;
  for (const it of cssItems(text)) {
    const head = it.prelude.replace(/\s+/g, " ");
    const show = head.length > 80 ? `${head.slice(0, 77)}...` : head;
    if (it.unclosed) { errors.push(`${MOD.id}.css: ${where}unclosed block after '${show}'`); continue; }
    if (it.stray) { errors.push(`${MOD.id}.css: ${where}stray '}'${show ? ` after '${show}'` : ""}`); continue; }
    if (head.startsWith("@")) {
      const name = ((/^@([\w-]+)/.exec(head) || [])[1] || "").toLowerCase();
      if (name === "charset" && it.block === null) continue;
      if ((name === "media" || name === "supports") && it.block !== null) {
        count += lintCssScoping(it.block, errors, `${where}${show} `);
        continue;
      }
      if (/^(-webkit-)?keyframes$/.test(name) && it.block !== null) {
        // from / to / n% inside are keyframe selectors, not element selectors. The name is global: a clash replaces the game's (or
        // another mod's) animation.
        const kname = head.slice(name.length + 1).trim().replace(/^["']|["']$/g, "");
        if (!kname.startsWith("sr-")) errors.push(`${MOD.id}.css: ${where}@keyframes "${kname}" - keyframe names are global; ours must start with "sr-" (define it in a *.module.scss)`);
        continue;
      }
      if (name === "font-face" && it.block !== null) {
        const fam = /font-family\s*:\s*([^;]*)/i.exec(it.block);
        const family = fam ? fam[1].trim().replace(/^["']|["']$/g, "") : "";
        if (!family.startsWith("sr-")) errors.push(`${MOD.id}.css: ${where}@font-face family "${family}" - font family names are global (a clash replaces the game's font); ours must start with "sr-"`);
        continue;
      }
      errors.push(`${MOD.id}.css: ${where}at-rule @${name || "?"} - not allowed (this gate checks @media, @supports, @keyframes sr-*, @font-face sr-* and @charset only)`);
      continue;
    }
    if (it.block === null) { errors.push(`${MOD.id}.css: ${where}text outside any rule: '${show}'`); continue; }
    for (const part of splitTopLevel(it.prelude, ",")) {
      const sel = part.trim().replace(/\s+/g, " ");
      count++;
      const why = selectorLeak(sel);
      if (why) errors.push(`${MOD.id}.css: ${where}selector '${sel}' - ${why}. Our CSS shares ONE document with the game and every mod (research 2.A.1): scope every rule under an .sr- class`);
    }
  }
  return count;
}


/** C# source with comments removed (strings, verbatim strings and interpolation holes respected) */
function stripCsComments(src) {
  const n = src.length;
  const isStrStart = (j) => src[j] === "\"" ||
    ((src[j] === "$" || src[j] === "@") && (src[j + 1] === "\"" || ((src[j + 1] === "$" || src[j + 1] === "@") && src[j + 2] === "\"")));
  function readChar(j) {
    let k = j + 1;
    while (k < n && src[k] !== "'" && src[k] !== "\n") k += src[k] === "\\" ? 2 : 1;
    return k + 1;
  }
  function readString(j) {
    let verbatim = false, interp = false;
    while (src[j] === "$" || src[j] === "@") { if (src[j] === "$") interp = true; else verbatim = true; j++; }
    j++;
    let depth = 0;
    while (j < n) {
      const c = src[j];
      if (depth === 0) {
        if (!verbatim && c === "\\") { j += 2; continue; }
        if (c === "\"") { if (verbatim && src[j + 1] === "\"") { j += 2; continue; } return j + 1; }
        if (interp && c === "{") { if (src[j + 1] === "{") { j += 2; continue; } depth = 1; j++; continue; }
        j++;
      } else {
        if (isStrStart(j)) { j = readString(j); continue; }
        if (c === "'") { j = readChar(j); continue; }
        if (c === "{") depth++;
        else if (c === "}") depth--;
        j++;
      }
    }
    return j;
  }
  let out = "";
  let i = 0;
  while (i < n) {
    const c = src[i];
    if (c === "/" && src[i + 1] === "/") { while (i < n && src[i] !== "\n") i++; continue; }
    if (c === "/" && src[i + 1] === "*") {
      const e = src.indexOf("*/", i + 2);
      const end = e < 0 ? n : e + 2;
      out += src.slice(i, end).replace(/[^\n]/g, " ");
      i = end;
      continue;
    }
    if (isStrStart(i)) { const e = readString(i); out += src.slice(i, e); i = e; continue; }
    if (c === "'") { const e = readChar(i); out += src.slice(i, e); i = e; continue; }
    out += c;
    i++;
  }
  return out;
}

/** Splits a C# generic argument list "A, B<C, D>" at top-level commas */
function splitGenericArgs(text) {
  const out = [];
  let depth = 0, start = 0;
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (c === "<" || c === "(" || c === "[") depth++;
    else if (c === ">" || c === ")" || c === "]") depth--;
    else if (c === "," && depth === 0) { out.push(text.slice(start, i).trim()); start = i + 1; }
  }
  const last = text.slice(start).trim();
  if (last) out.push(last);
  return out;
}

/** The C# side of R1 / R2: Group, HubContract, registrations, TypeBegin blocks */
function readHubCs(file, errors) {
  let text;
  try { text = fs.readFileSync(file, "utf8"); } catch (e) { errors.push(`contract: cannot read ${file} (${e && e.code ? e.code : e}) - rule R1 needs the C# hub system`); return null; }
  const src = stripCsComments(text);
  const lineAt = (idx) => src.slice(0, idx).split("\n").length;
  const out = { file, group: null, contract: null, regs: [] };
  const g = /\bconst\s+string\s+Group\s*=\s*"([^"]*)"/.exec(src);
  if (g) out.group = g[1];
  const c = /\bconst\s+int\s+HubContract\s*=\s*(\d+)\s*;/.exec(src);
  if (c) out.contract = Number(c[1]);
  const re = /\bnew\s+([A-Za-z_][\w.]*Binding)\s*(<((?:[^<>()]|<[^<>]*(?:<[^<>]*>[^<>]*)*>)*)>)?\s*\(\s*([A-Za-z_][\w.]*)\s*,\s*("([^"\\]*)"|[^,)]*)/g;
  for (const m of src.matchAll(re)) {
    const cls = m[1].split(".").pop();
    const generic = m[3] !== undefined ? splitGenericArgs(m[3]) : [];
    const literal = m[6] !== undefined ? m[6] : null;
    const kind = /^(TriggerBinding|CallBinding)$/.test(cls) ? "trigger" : /^(ValueBinding|RawValueBinding|GetterValueBinding|RawMapBinding|ValueBindingWithCustomEquality)$/.test(cls) ? "binding" : null;
    if (!kind) continue;
    // R1: a constructed binding that is never passed to AddBinding( is never registered with the game. Registered = the `new` sits
    // directly in an AddBinding( argument (through any `x = y =` assignment chain), or it is assigned to a name that appears as
    // AddBinding(name).
    const before = src.slice(0, m.index);
    let added = /\bAddBinding\s*\(\s*(?:[A-Za-z_][\w.]*\s*=\s*)*$/.test(before);
    if (!added) {
      const chain = /((?:[A-Za-z_][\w.]*\s*=(?!=)\s*)+)$/.exec(before);
      if (chain) {
        for (const lhs of chain[1].split("=").map((x) => x.trim()).filter(Boolean)) {
          const esc = lhs.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
          if (new RegExp("\\bAddBinding\\s*\\(\\s*(?:[A-Za-z_][\\w.]*\\s*=\\s*)*" + esc + "\\s*\\)").test(src)) { added = true; break; }
        }
      }
    }
    out.regs.push({ cls, kind, generic, name: literal, nameText: m[5].trim(), line: lineAt(m.index), added });
  }
  return out;
}

/** Every `TypeBegin("SkiResortMod.<X>")` block of every .cs under dir (bin/obj excluded) */
function readCsWriters(dir, errors) {
  const blocks = [];
  const memberKinds = new Map();
  const files = [];
  const walk = (d) => {
    let entries;
    try { entries = fs.readdirSync(d, { withFileTypes: true }); } catch { return; }
    for (const e of entries) {
      if (e.isDirectory()) { if (!/^(bin|obj|\.vs)$/i.test(e.name)) walk(path.join(d, e.name)); }
      else if (/\.cs$/i.test(e.name)) files.push(path.join(d, e.name));
    }
  };
  walk(dir);
  if (!files.length) { errors.push(`contract: no .cs file under ${dir} - rule R2 cannot run`); return { blocks, files }; }
  for (const file of files) {
    const src = stripCsComments(fs.readFileSync(file, "utf8"));
    const lineAt = (idx) => src.slice(0, idx).split("\n").length;
    const tokens = [];
    collectCsMemberKinds(src, memberKinds);
    for (const m of src.matchAll(/\.TypeBegin\s*\(\s*(?:"([^"]*)"|([^)]*))\)/g)) tokens.push({ at: m.index, t: "begin", name: m[1] !== undefined ? m[1] : null });
    for (const m of src.matchAll(/\.TypeEnd\s*\(\s*\)/g)) tokens.push({ at: m.index, t: "end" });
    for (const m of src.matchAll(/\.PropertyName\s*\(\s*(?:"([^"]*)"|([^)]*))\)/g)) tokens.push({ at: m.index, t: "prop", end: m.index + m[0].length, src, name: m[1] !== undefined ? m[1] : null, expr: m[2] });
    tokens.sort((a, b) => a.at - b.at);
    const stack = [];
    for (const tk of tokens) {
      if (tk.t === "begin") {
        const ours = tk.name && tk.name.startsWith("SkiResortMod.");
        const b = { file, line: lineAt(tk.at), name: ours ? tk.name.slice("SkiResortMod.".length) : null, raw: tk.name, props: [], dynamic: [], writes: [] };
        stack.push(b);
        if (ours) blocks.push(b);
      } else if (tk.t === "end") {
        stack.pop();
      } else if (stack.length) {
        const top = stack[stack.length - 1];
        if (tk.name !== null) { top.props.push(tk.name); top.writes.push({ name: tk.name, arg: writeArgAfter(tk.src, tk.end) }); }
        else top.dynamic.push((tk.expr || "").trim());
      }
    }
  }
  return { blocks, files, memberKinds };
}

/**
 * R2: value-kind inference for `writer.PropertyName("x"); writer.Write(<expr>);`. The checker has no C# type information, so it
 * uses the declared type of the last identifier of the argument (field, property, local, parameter or method return type
 * declared anywhere under CS_DIR; a name declared with more than one kind is "unknown" and skipped), plus the cast / Fin(...) /
 * "?? \"\"" / comparison / literal forms. "unknown" never fails: only a certain mismatch does.
 */
function collectCsMemberKinds(src, map) {
  const re = /(?:^|[\s(,;{])(bool|byte|sbyte|short|ushort|int|uint|long|ulong|float|double|string|Entity)(\[\])?\s+([A-Za-z_]\w*)\s*(?=[=;,)(\[{]|=>)/g;
  for (const m of src.matchAll(re)) {
    const k = csKind(m[1]) + (m[2] ? "[]" : "");
    if (!map.has(m[3])) map.set(m[3], new Set());
    map.get(m[3]).add(k);
  }
}

/** The argument text of the `.Write(...)` statement that directly follows a PropertyName call ending at `pos`, or null */
function writeArgAfter(src, pos) {
  const head = /^\s*;\s*[A-Za-z_][\w.]*\.Write\s*\(/.exec(src.slice(pos, pos + 160));
  if (!head) return null;
  let i = pos + head[0].length, depth = 1;
  const start = i;
  for (; i < src.length && depth > 0; i++) {
    const c = src[i];
    if (c === "\"") { i++; while (i < src.length && src[i] !== "\"") i += src[i] === "\\" ? 2 : 1; continue; }
    if (c === "(") depth++;
    else if (c === ")") depth--;
  }
  return depth === 0 ? src.slice(start, i - 1).trim() : null;
}

/** Splits at the first top-level `?` (not `??`) and its `:`; null when there is none */
function splitTernary(e) {
  let depth = 0, q = -1;
  for (let i = 0; i < e.length; i++) {
    const c = e[i];
    if (c === "\"") { i++; while (i < e.length && e[i] !== "\"") i += e[i] === "\\" ? 2 : 1; continue; }
    if (c === "(" || c === "[" || c === "<") depth++;
    else if (c === ")" || c === "]" || c === ">") depth--;
    else if (c === "?" && depth === 0 && e[i + 1] !== "?" && e[i - 1] !== "?") { q = i; break; }
  }
  if (q < 0) return null;
  depth = 0;
  for (let i = q + 1; i < e.length; i++) {
    const c = e[i];
    if (c === "\"") { i++; while (i < e.length && e[i] !== "\"") i += e[i] === "\\" ? 2 : 1; continue; }
    if (c === "(" || c === "[") depth++;
    else if (c === ")" || c === "]") depth--;
    else if (c === ":" && depth === 0) return { a: e.slice(q + 1, i).trim(), b: e.slice(i + 1).trim() };
  }
  return null;
}

/** "bool" | "number" | "string" | "entity" | "unknown" for a C# Write argument */
function kindOfWriteArg(arg, memberKinds) {
  let e = (arg || "").trim();
  if (!e) return "unknown";
  while (/^\(.*\)$/.test(e) && !/\)\s*[\w.(]/.test(e.slice(1, -1).replace(/\([^()]*\)/g, ""))) e = e.slice(1, -1).trim();
  const cast = /^\(\s*([A-Za-z_][\w.]*)\s*\)\s*\S/.exec(e);
  if (cast) { const k = csKind(cast[1]); if (k === "bool" || k === "number" || k === "string") return k; }
  if (/^(true|false)$/.test(e)) return "bool";
  if (/^-?\d/.test(e)) return "number";
  if (/^[$@]*"/.test(e) || /\?\?\s*""\s*$/.test(e)) return "string";
  if (/^Fin\(/.test(e)) return "number";
  const t = splitTernary(e);
  if (t) {
    const a = kindOfWriteArg(t.a, memberKinds), b = kindOfWriteArg(t.b, memberKinds);
    return a === b ? a : (a === "unknown" ? b : b === "unknown" ? a : "unknown");
  }
  if (/^!(?!=)/.test(e) || /(==|!=|<=|>=|&&|\|\|)/.test(e.replace(/\([^()]*\)/g, "()"))) return "bool";
  let tail = e, indexed = false;
  const call = /\((?:[^()]|\([^()]*\))*\)$/.exec(tail);
  if (call) tail = tail.slice(0, call.index);
  const idx = /\[[^\]]*\]$/.exec(tail);
  if (idx) { indexed = true; tail = tail.slice(0, idx.index); }
  const id = /([A-Za-z_]\w*)$/.exec(tail.trim());
  if (!id) return "unknown";
  const kinds = memberKinds.get(id[1]);
  if (!kinds) return "unknown";
  const pick = new Set();
  for (const k of kinds) {
    if (indexed && k.endsWith("[]")) pick.add(k.slice(0, -2));
    else if (!indexed && !k.endsWith("[]")) pick.add(k);
  }
  if (pick.size !== 1) return "unknown";
  const k = [...pick][0];
  return k === "bool" || k === "number" || k === "string" || k === "entity" ? k : "unknown";
}

/** tsKind widened for R2: unions of string literals are strings, unions of numeric literals are numbers */
function tsWriteKind(t) {
  const s = t.trim();
  const k = tsKind(s);
  if (k !== "other") return k;
  const parts = s.split("|").map((x) => x.trim());
  if (parts.length > 1 && parts.every((x) => /^["'].*["']$/.test(x))) return "string";
  if (parts.length > 1 && parts.every((x) => /^-?\d+(\.\d+)?$/.test(x))) return "number";
  return "other";
}

const LOCALE_KEY_RE = /^(SkiResort|Assets|SubServices)\.[^\s"]+$/;

/** Every `{ "key", "value" }` entry of LocaleEN.cs (plain literals, '+' concatenation) */
function readLocaleEntries(file) {
  const map = new Map();
  let text;
  try { text = fs.readFileSync(file, "utf8"); } catch { return null; }
  const toks = csTokens(text);
  for (let k = 1; k + 2 < toks.length; k++) {
    const key = toks[k];
    if (key.t !== "s" || toks[k - 1].t !== "{" || toks[k + 1].t !== ",") continue;
    let value = "", j = k + 2, ok = true;
    for (; j < toks.length && toks[j].t !== "}"; j++) {
      if (toks[j].t === "s") value += toks[j].v;
      else if (toks[j].t !== "+") { ok = false; break; }
    }
    if (ok) map.set(key.v, { value, line: key.line });
  }
  return map;
}

function typeText(sf, node) {
  return node ? node.getText(sf).replace(/\s+/g, " ").trim() : "?";
}

/** The TS side of R1-R3: read from src/bindings.ts with the TypeScript AST */
function readBindingsTs(ts, file, errors) {
  let text;
  try { text = fs.readFileSync(file, "utf8"); } catch (e) { errors.push(`contract: cannot read ${file}`); return null; }
  const sf = ts.createSourceFile(file, text, ts.ScriptTarget.ES2020, true, ts.ScriptKind.TS);
  const out = { group: null, contract: null, binding: new Map(), trigger: new Map(), core: null, shapes: new Map(), bindingTypes: new Map(), triggerArgs: new Map() };
  const unwrap = (e) => { while (e && (ts.isAsExpression(e) || ts.isParenthesizedExpression(e) || ts.isSatisfiesExpression?.(e))) e = e.expression; return e; };
  const strMap = (obj, what) => {
    const m = new Map();
    for (const p of obj.properties) {
      if (!ts.isPropertyAssignment(p) || !ts.isStringLiteralLike(p.initializer)) { errors.push(`contract: ${what} in bindings.ts must be { name: "name", ... } string literals (rule R1)`); continue; }
      const key = p.name.getText(sf).replace(/^["']|["']$/g, "");
      m.set(key, p.initializer.text);
    }
    return m;
  };
  const visit = (node) => {
    if (ts.isVariableDeclaration(node) && ts.isIdentifier(node.name) && node.initializer) {
      const name = node.name.text;
      const init = unwrap(node.initializer);
      if (name === "GROUP" && ts.isStringLiteralLike(init)) out.group = init.text;
      else if (name === "HUB_CONTRACT" && ts.isNumericLiteral(init)) out.contract = Number(init.text);
      else if (name === "BINDING" && ts.isObjectLiteralExpression(init)) out.binding = strMap(init, "BINDING");
      else if (name === "TRIGGER" && ts.isObjectLiteralExpression(init)) out.trigger = strMap(init, "TRIGGER");
      else if (name === "CORE_NAMES" && ts.isArrayLiteralExpression(init)) {
        out.core = [];
        for (const el of init.elements) {
          if (ts.isStringLiteralLike(el)) out.core.push(el.text);
          else errors.push("contract: CORE_NAMES in bindings.ts must be string literals (rule R3)");
        }
      } else if (name === "hubTriggers" && ts.isObjectLiteralExpression(init)) {
        for (const p of init.properties) {
          if (!ts.isPropertyAssignment(p) || !(ts.isArrowFunction(p.initializer) || ts.isFunctionExpression(p.initializer))) continue;
          const fn = p.initializer;
          let trig = null;
          const find = (n) => {
            if (trig) return;
            if (ts.isPropertyAccessExpression(n) && ts.isIdentifier(n.expression) && n.expression.text === "TRIGGER") trig = n.name.text;
            ts.forEachChild(n, find);
          };
          find(fn.body);
          if (!trig) { errors.push(`contract: hubTriggers.${p.name.getText(sf)} does not fire a TRIGGER.<name> (rule R1)`); continue; }
          out.triggerArgs.set(trig, fn.parameters.map((q) => typeText(sf, q.type)));
        }
      }
    } else if (ts.isInterfaceDeclaration(node)) {
      const fields = [];
      for (const m of node.members) {
        if (!ts.isPropertySignature(m)) continue;
        fields.push({ name: m.name.getText(sf), optional: !!m.questionToken, type: typeText(sf, m.type) });
      }
      out.shapes.set(node.name.text, fields);
    } else if (ts.isCallExpression(node) && ts.isIdentifier(node.expression) && node.expression.text === "bindValue") {
      const a = node.arguments[1];
      if (a && ts.isPropertyAccessExpression(a) && ts.isIdentifier(a.expression) && a.expression.text === "BINDING") {
        out.bindingTypes.set(a.name.text, node.typeArguments && node.typeArguments[0] ? typeText(sf, node.typeArguments[0]) : "?");
      } else {
        errors.push(`contract: bindValue() in bindings.ts must name its binding as BINDING.<name> (rule R1), at line ${sf.getLineAndCharacterOfPosition(node.getStart(sf)).line + 1}`);
      }
    }
    ts.forEachChild(node, visit);
  };
  visit(sf);
  if (out.contract === null) errors.push("contract: bindings.ts has no `export const HUB_CONTRACT = <integer>` (rule R1)");
  if (out.core === null) errors.push("contract: bindings.ts has no `export const CORE_NAMES = [ ... ]` (rule R3)");
  for (const [k, v] of out.binding) if (k !== v) errors.push(`contract: BINDING.${k} = "${v}" - key and name must be equal (rule R1)`);
  for (const [k, v] of out.trigger) if (k !== v) errors.push(`contract: TRIGGER.${k} = "${v}" - key and name must be equal (rule R1)`);
  for (const n of out.binding.keys()) if (!out.bindingTypes.has(n)) errors.push(`contract: BINDING.${n} has no bindValue<T>(GROUP, BINDING.${n}, FALLBACK.${n}) in bindings.ts (rule R1)`);
  for (const n of out.trigger.keys()) if (!out.triggerArgs.has(n)) errors.push(`contract: TRIGGER.${n} has no hubTriggers entry in bindings.ts (rule R1)`);
  for (const n of out.core || []) if (!out.binding.has(n) && !out.trigger.has(n)) errors.push(`contract: CORE_NAMES lists "${n}", which is neither a BINDING nor a TRIGGER (rule R3)`);
  return out;
}

/** Base interface name of a TS type ("readonly ResortRow[]" -> "ResortRow"), or null */
function baseTypeName(t) {
  const s = t.replace(/^readonly\s+/, "").replace(/\[\]$/, "").replace(/^ReadonlyArray<(.*)>$/, "$1").replace(/^Array<(.*)>$/, "$1").trim();
  return /^[A-Z][A-Za-z0-9_]*$/.test(s) ? s : null;
}

const CS_NUMBER = /^(int|uint|long|ulong|short|ushort|byte|sbyte|float|double|decimal)$/;
function tsKind(t) {
  const s = t.trim();
  if (s === "boolean") return "bool";
  if (s === "number") return "number";
  if (s === "string") return "string";
  if (s === "Entity") return "entity";
  if (/\[\]$/.test(s) || /^(Readonly)?Array</.test(s)) return "array";
  return "other";
}
function csKind(t) {
  const s = t.trim().split(".").pop();
  if (s === "bool") return "bool";
  if (CS_NUMBER.test(s)) return "number";
  if (s === "string") return "string";
  if (s === "Entity") return "entity";
  if (/\[\]$/.test(s) || /^(List|IList|IReadOnlyList)</.test(s)) return "array";
  return "other";
}

/** The lock's text for a TS snapshot (sorted, one fact per line, for a readable review diff) */
function lockText(snap) {
  const lines = [];
  lines.push("# tools/contract.lock - the Ski Resort hub's binding contract as the build gate last accepted it.");
  lines.push("# Written by `npm run contract:lock` (tools/check-bundle.js --write-lock); checked on every build");
  lines.push("# (rule R3, docs/research/hub-u2-spec.md section 4.7). Do not edit by hand: change src/bindings.ts,");
  lines.push("# bump HUB_CONTRACT (and C#'s HubContract), run `npm run contract:lock`, review this file's diff.");
  lines.push(`contract ${snap.contract}`);
  for (const l of snapFacts(snap)) lines.push(l);
  return lines.join("\n") + "\n";
}

function snapFacts(snap) {
  const facts = [];
  for (const n of [...(snap.core || [])].sort()) facts.push(`core ${n}`);
  for (const n of [...snap.binding.keys()].sort()) facts.push(`binding ${n} : ${snap.bindingTypes.get(n) || "?"}`);
  for (const n of [...snap.trigger.keys()].sort()) facts.push(`trigger ${n} (${(snap.triggerArgs.get(n) || []).join(", ")})`);
  for (const s of [...snap.shapes.keys()].sort()) {
    for (const f of [...snap.shapes.get(s)].sort((a, b) => (a.name < b.name ? -1 : a.name > b.name ? 1 : 0))) {
      facts.push(`shape ${s}.${f.name}${f.optional ? "?" : ""} : ${f.type}`);
    }
  }
  return facts;
}

function parseLock(text) {
  const lock = { contract: null, facts: [], core: [], binding: new Map(), trigger: new Map(), shapes: new Map() };
  for (const raw of text.split(/\r?\n/)) {
    const l = raw.trim();
    if (!l || l.startsWith("#")) continue;
    let m;
    if ((m = /^contract (\d+)$/.exec(l))) { lock.contract = Number(m[1]); continue; }
    lock.facts.push(l);
    if ((m = /^core (\S+)$/.exec(l))) lock.core.push(m[1]);
    else if ((m = /^binding (\S+) : (.*)$/.exec(l))) lock.binding.set(m[1], m[2]);
    else if ((m = /^trigger (\S+) \((.*)\)$/.exec(l))) lock.trigger.set(m[1], m[2]);
    else if ((m = /^shape ([A-Za-z0-9_]+)\.([A-Za-z0-9_]+)(\?)? : (.*)$/.exec(l))) {
      if (!lock.shapes.has(m[1])) lock.shapes.set(m[1], []);
      lock.shapes.get(m[1]).push({ name: m[2], optional: !!m[3], type: m[4] });
    }
  }
  return lock;
}

/** Core violations of the current snapshot against a lock (rule R3) */
function coreViolations(snap, lock) {
  const v = [];
  const nowCore = new Set(snap.core || []);
  const coreShapes = new Set();
  for (const n of lock.core) {
    if (!nowCore.has(n)) v.push(`CORE name "${n}" was removed from CORE_NAMES`);
    if (lock.binding.has(n)) {
      if (!snap.binding.has(n)) { v.push(`CORE binding "${n}" was removed or renamed`); continue; }
      const was = lock.binding.get(n), is = snap.bindingTypes.get(n) || "?";
      if (was !== is) v.push(`CORE binding "${n}" was retyped: ${was} -> ${is}`);
      const base = baseTypeName(was);
      if (base) coreShapes.add(base);
    } else if (lock.trigger.has(n)) {
      if (!snap.trigger.has(n)) { v.push(`CORE trigger "${n}" was removed or renamed`); continue; }
      const was = lock.trigger.get(n), is = (snap.triggerArgs.get(n) || []).join(", ");
      if (was !== is) v.push(`CORE trigger "${n}" was retyped: (${was}) -> (${is})`);
    }
  }
  for (const s of coreShapes) {
    const was = lock.shapes.get(s) || [];
    const is = snap.shapes.get(s);
    if (!is) { v.push(`CORE shape ${s} was removed or renamed`); continue; }
    for (const f of was) {
      const g = is.find((x) => x.name === f.name);
      if (!g) v.push(`CORE shape ${s}: field "${f.name}" was removed or renamed`);
      else if (g.type !== f.type || g.optional !== f.optional) v.push(`CORE shape ${s}: field "${f.name}" changed: ${f.name}${f.optional ? "?" : ""}: ${f.type} -> ${g.name}${g.optional ? "?" : ""}: ${g.type}`);
    }
    for (const g of is) {
      if (!was.find((x) => x.name === g.name) && !g.optional) v.push(`CORE shape ${s}: new field "${g.name}" must be optional (CORE may only gain optional fields)`);
    }
  }
  return v;
}

function factDiff(before, after) {
  const a = new Set(before), b = new Set(after);
  const removed = before.filter((x) => !b.has(x)), added = after.filter((x) => !a.has(x));
  return { removed, added, text: [...removed.slice(0, 8).map((x) => `- ${x}`), ...added.slice(0, 8).map((x) => `+ ${x}`)].join("; ") +
    (removed.length + added.length > 16 ? ` (+${removed.length + added.length - 16} more)` : "") };
}

/**
 * Rules R1-R7 and R10. Returns { snap } (the TS contract snapshot) for --write-lock.
 * `opts.lockMode`: "check" (the gate) or "none" (the writer runs its own lock logic).
 */
function checkContract(errors, warnings, report, opts = {}) {
  let ts;
  try { ts = require("typescript"); } catch { errors.push("contract: typescript is not installed (run npm ci)"); return {}; }
  for (const o of OVERRIDES) warnings.push(`contract: path override in use: ${o} - this run does NOT check the real files`);

  const snap = readBindingsTs(ts, path.join(SRC, BINDINGS_TS), errors);
  if (!snap) return {};
  const cs = readHubCs(HUB_CS, errors);
  if (cs) {
    if (cs.contract === null) errors.push(`contract: ${path.basename(HUB_CS)} has no \`const int HubContract = <integer>;\` (rule R1)`);
    else if (cs.contract !== snap.contract) errors.push(`contract: C# HubContract ${cs.contract} != bindings.ts HUB_CONTRACT ${snap.contract} (rule R1)`);
    if (cs.group !== null && snap.group !== null && cs.group !== snap.group) errors.push(`contract: C# Group "${cs.group}" != bindings.ts GROUP "${snap.group}" (rule R1)`);
    const csNames = new Map();
    for (const r of cs.regs) {
      if (r.name === null) {
        errors.push(`contract: ${path.basename(HUB_CS)}:${r.line} registers new ${r.cls}(..., ${r.nameText}, ...) - use a string literal name so this gate can check it (rule R1)`);
        continue;
      }
      if (!r.added) errors.push(`contract: ${path.basename(HUB_CS)}:${r.line} constructs new ${r.cls}(..., "${r.name}", ...) but never passes it to AddBinding( - it is never registered, so the UI would show its fallback forever and C# would log nothing (rule R1)`);
      if (csNames.has(r.name)) errors.push(`contract: ${path.basename(HUB_CS)}:${r.line} registers "${r.name}" twice (rule R1)`);
      csNames.set(r.name, r);
    }
    for (const [name, r] of csNames) {
      const isB = snap.binding.has(name), isT = snap.trigger.has(name);
      if (!isB && !isT) { errors.push(`contract: C# registers ${r.kind} "${name}" (${path.basename(HUB_CS)}:${r.line}) - not in bindings.ts BINDING / TRIGGER (rule R1)`); continue; }
      if (r.kind === "binding" && !isB) { errors.push(`contract: "${name}" is a TRIGGER in bindings.ts but C# registers a ${r.cls} (rule R1)`); continue; }
      if (r.kind === "trigger" && !isT) { errors.push(`contract: "${name}" is a BINDING in bindings.ts but C# registers a ${r.cls} (rule R1)`); continue; }
      if (r.kind === "binding") {
        const tsT = snap.bindingTypes.get(name) || "?";
        const tk = tsKind(tsT);
        if (r.cls === "RawValueBinding") {
          if (tk !== "array" && tk !== "other") errors.push(`contract: binding "${name}" is ${tsT} in TS but a RawValueBinding in C# (rule R1)`);
        } else if (r.generic.length === 1) {
          const ck = csKind(r.generic[0]);
          const ok = ck === tk || (tk === "other" && ck === "other") || (tk === "array" && ck === "array");
          if (!ok) errors.push(`contract: binding "${name}" is ${tsT} in TS but ${r.cls}<${r.generic[0]}> in C# (rule R1)`);
        }
      } else {
        const tsArgs = snap.triggerArgs.get(name) || [];
        if (r.generic.length !== tsArgs.length) {
          errors.push(`contract: trigger "${name}" takes ${tsArgs.length} argument(s) in TS (${tsArgs.join(", ")}) but C# registers ${r.cls}<${r.generic.join(", ")}> (rule R1)`);
        } else {
          for (let i = 0; i < tsArgs.length; i++) {
            const tk = tsKind(tsArgs[i]), ck = csKind(r.generic[i]);
            if (tk !== "other" && ck !== "other" && tk !== ck) errors.push(`contract: trigger "${name}" argument ${i + 1} is ${tsArgs[i]} in TS but ${r.generic[i]} in C# (rule R1)`);
          }
        }
      }
    }
    for (const n of snap.binding.keys()) if (!csNames.has(n)) errors.push(`contract: binding "${n}" (bindings.ts BINDING) is not registered in ${path.basename(HUB_CS)} - the UI would show its fallback forever (rule R1)`);
    for (const n of snap.trigger.keys()) if (!csNames.has(n)) errors.push(`contract: trigger "${n}" (bindings.ts TRIGGER) is not registered in ${path.basename(HUB_CS)} - the click would do nothing (rule R1)`);
    report.push(`contract R1: HUB_CONTRACT ${snap.contract} / C# ${cs.contract}; ${snap.binding.size} binding(s) + ${snap.trigger.size} trigger(s) in TS, ${csNames.size} registered in ${path.basename(HUB_CS)}`);
  }

  const writers = readCsWriters(CS_DIR, errors);
  const written = new Map();
  let kindChecks = 0, kindDecided = 0;
  for (const b of writers.blocks) {
    const where = `${path.relative(CS_DIR, b.file).split(path.sep).join("/")}:${b.line}`;
    const fields = snap.shapes.get(b.name);
    if (!fields) { errors.push(`contract: ${where} writes TypeBegin("SkiResortMod.${b.name}") - bindings.ts has no interface ${b.name} (rule R2: the TypeBegin name must be "SkiResortMod.<TS interface name>")`); continue; }
    written.set(b.name, (written.get(b.name) || 0) + 1);
    for (const d of b.dynamic) errors.push(`contract: ${where} SkiResortMod.${b.name} writes PropertyName(${d}) - use a string literal (rule R2)`);
    const seen = new Set();
    for (const p of b.props) {
      if (seen.has(p)) errors.push(`contract: ${where} SkiResortMod.${b.name} writes "${p}" twice (rule R2)`);
      seen.add(p);
      if (!fields.find((f) => f.name === p)) errors.push(`contract: ${where} SkiResortMod.${b.name} writes "${p}" - not a field of TS interface ${b.name} (rule R2)`);
    }
    // The value kind of each Write(...) against the TS field type (a bool written for a number, a string for a bool, ...)
    for (const w of b.writes) {
      const f = fields.find((x) => x.name === w.name);
      if (!f || w.arg === null) continue;
      const want = tsWriteKind(f.type);
      if (want !== "bool" && want !== "number" && want !== "string") continue;
      const got = kindOfWriteArg(w.arg, writers.memberKinds);
      kindChecks++;
      if (got !== "unknown") kindDecided++;
      if (got !== "unknown" && got !== want) errors.push(`contract: ${where} SkiResortMod.${b.name} writes "${w.name}" with Write(${w.arg}), a ${got}, but TS ${b.name}.${w.name} is ${f.type} (a ${want}) - the UI would read the wrong kind (rule R2)`);
    }
    for (const f of fields) if (!seen.has(f.name)) errors.push(`contract: ${where} SkiResortMod.${b.name} does not write "${f.name}" (TS interface ${b.name}${f.optional ? ", optional" : ""}) - the UI would read undefined (rule R2)`);
  }
  const carried = new Set();
  for (const t of snap.bindingTypes.values()) { const b = baseTypeName(t); if (b && b !== "Entity" && snap.shapes.has(b)) carried.add(b); }
  for (const s of carried) if (!written.has(s)) errors.push(`contract: no C# writer for TS interface ${s} (no TypeBegin("SkiResortMod.${s}") under ${CS_DIR}) (rule R2)`);
  report.push(`contract R2: ${writers.blocks.length} TypeBegin("SkiResortMod.*") block(s) in ${writers.files.length} .cs file(s); shapes written: ${[...written.keys()].sort().join(", ") || "none"}; ${kindChecks} Write(...) value kind(s) against the TS field types, ${kindDecided} decided (the rest unknown C# types, skipped)`);

  if (opts.lockMode !== "none") {
    let lockRaw = null;
    try { lockRaw = fs.readFileSync(CONTRACT_LOCK, "utf8"); } catch { /* reported below */ }
    if (lockRaw === null) {
      errors.push(`contract: ${CONTRACT_LOCK} is missing - run \`npm run contract:lock\` and commit it (rule R3)`);
    } else {
      const lock = parseLock(lockRaw);
      for (const v of coreViolations(snap, lock)) errors.push(`contract: ${v} (rule R3: CORE is frozen since contract 1, spec 2.2)`);
      const diff = factDiff(lock.facts, snapFacts(snap));
      const changed = diff.removed.length + diff.added.length > 0;
      if (changed && lock.contract === snap.contract) {
        errors.push(`contract: the contract changed but HUB_CONTRACT is still ${snap.contract} - bump it in bindings.ts AND C#, then run \`npm run contract:lock\` (rule R3). Changes: ${diff.text}`);
      } else if (lock.contract !== snap.contract) {
        errors.push(`contract: HUB_CONTRACT is ${snap.contract} but ${path.basename(CONTRACT_LOCK)} is for contract ${lock.contract} - after a deliberate bump run \`npm run contract:lock\` and review the diff (rule R3)${changed ? `. Changes: ${diff.text}` : ""}`);
      }
      report.push(`contract R3: ${path.basename(CONTRACT_LOCK)} contract ${lock.contract}, ${lock.facts.length} fact(s), ${lock.core.length} CORE name(s)${changed ? " - DIFFERS" : " - matches"}`);
    }
  }

  const locale = readLocaleEntries(LOCALE_EN);
  if (!locale) errors.push(`contract: cannot read ${LOCALE_EN} (rule R4)`);
  const files = fs.existsSync(SRC) ? listFiles(SRC).filter((f) => /\.tsx?$/.test(f) && !f.endsWith(".d.ts")) : [];
  const entryMaps = new Map();
  const allStrings = new Set();
  const parsed = [];
  for (const rel of files) {
    const text = fs.readFileSync(path.join(SRC, rel), "utf8");
    const sf = ts.createSourceFile(rel, text, ts.ScriptTarget.ES2020, true, rel.endsWith(".tsx") ? ts.ScriptKind.TSX : ts.ScriptKind.TS);
    parsed.push({ rel, sf });
    const collect = (node) => {
      if (ts.isVariableDeclaration(node) && ts.isIdentifier(node.name) && node.initializer) {
        let init = node.initializer;
        while (ts.isAsExpression(init) || ts.isParenthesizedExpression(init)) init = init.expression;
        if (ts.isObjectLiteralExpression(init)) {
          for (const p of init.properties) {
            if (!ts.isPropertyAssignment(p) || !ts.isArrayLiteralExpression(p.initializer)) continue;
            const els = p.initializer.elements;
            if (els.length === 2 && ts.isStringLiteralLike(els[0]) && ts.isStringLiteralLike(els[1])) {
              entryMaps.set(`${node.name.text}.${p.name.getText(sf)}`, els[1].text);
            }
          }
        }
      }
      if (ts.isStringLiteralLike(node)) allStrings.add(node.text);
      ts.forEachChild(node, collect);
    };
    collect(sf);
  }
  let tuples = 0, placeholders = 0, inputs = 0, media = 0;
  const install = readCsiiVar("CSII_INSTALLATIONPATH");
  const uiDir = install.value ? path.join(install.value, "Cities2_Data", "Content", "Game", "UI") : null;
  if (!uiDir) warnings.push("contract: CSII_INSTALLATIONPATH not set - Media/ paths not checked (rule R10)");
  let renameOk = { exists: false, stop: false, blurCleanup: false };
  for (const { rel, sf } of parsed) {
    const at = (node) => `src/${rel}:${sf.getLineAndCharacterOfPosition(node.getStart(sf)).line + 1}`;
    const isRename = rel === RENAME_FIELD;
    if (isRename) renameOk.exists = true;
    const englishOf = (arg) => {
      if (!arg) return null;
      let a = arg;
      while (ts.isParenthesizedExpression(a) || ts.isAsExpression(a)) a = a.expression;
      if (ts.isConditionalExpression(a)) return [englishOf(a.whenTrue), englishOf(a.whenFalse)].filter((x) => x !== null).join(" | ") || null;
      if (ts.isPropertyAccessExpression(a) && ts.isIdentifier(a.expression)) return entryMaps.get(`${a.expression.text}.${a.name.text}`) ?? null;
      if (ts.isArrayLiteralExpression(a) && a.elements.length === 2 && ts.isStringLiteralLike(a.elements[1])) return a.elements[1].text;
      return null;
    };
    const visit = (node) => {
      if (ts.isArrayLiteralExpression(node) && node.elements.length === 2 &&
          ts.isStringLiteralLike(node.elements[0]) && ts.isStringLiteralLike(node.elements[1]) && LOCALE_KEY_RE.test(node.elements[0].text)) {
        tuples++;
        const key = node.elements[0].text, en = node.elements[1].text;
        if (locale) {
          const hit = locale.get(key);
          if (!hit) errors.push(`${at(node)}: locale key "${key}" is not in ${path.basename(LOCALE_EN)} - add { "${key}", "${en}" } there (rule R4)`);
          else if (hit.value !== en) errors.push(`${at(node)}: "${key}" English differs from ${path.basename(LOCALE_EN)}:${hit.line}: TS "${en}" vs C# "${hit.value}" (rule R4)`);
        }
      }
      if (ts.isCallExpression(node)) {
        const callee = node.expression;
        const isT = (ts.isIdentifier(callee) && (callee.text === "t" || callee.text === "translate")) ||
          (ts.isPropertyAccessExpression(callee) && callee.name.text === "translate");
        if (isT) {
          const en = node.arguments.length >= 2 && ts.isStringLiteralLike(node.arguments[1]) ? node.arguments[1].text : englishOf(node.arguments[0]);
          if (en && en.includes("{")) {
            placeholders++;
            errors.push(`${at(node)}: ${callee.getText(sf)}(...) on an entry with a placeholder ("${en.slice(0, 60)}") - translate() never substitutes {...}: use LocalizedString (<LocText>) (rule R5)`);
          }
        }
        // RenameField: the useLayoutEffect cleanup must call .blur()
        if (isRename && ts.isIdentifier(callee) && callee.text === "useLayoutEffect" && node.arguments[0]) {
          const fn = node.arguments[0];
          const findBlurInReturn = (n) => {
            if (renameOk.blurCleanup) return;
            if (ts.isReturnStatement(n) && n.expression && (ts.isArrowFunction(n.expression) || ts.isFunctionExpression(n.expression))) {
              const findBlur = (m) => {
                if (ts.isCallExpression(m) && ts.isPropertyAccessExpression(m.expression) && m.expression.name.text === "blur") renameOk.blurCleanup = true;
                ts.forEachChild(m, findBlur);
              };
              findBlur(n.expression.body);
            }
            ts.forEachChild(n, findBlurInReturn);
          };
          findBlurInReturn(fn);
        }
      }
      if (isRename && ts.isIdentifier(node) && node.text === "stopPropagation") renameOk.stop = true;
      if (ts.isJsxOpeningElement(node) || ts.isJsxSelfClosingElement(node)) {
        const tag = node.tagName.getText(sf);
        if (tag === "input" || tag === "textarea") {
          inputs++;
          if (!isRename) errors.push(`${at(node)}: <${tag}> outside src/${RENAME_FIELD} - the hub's one text input lives there (keyboard capture, focus release; rule R6)`);
        }
        for (const attr of node.attributes.properties) {
          if (ts.isJsxAttribute(attr) && attr.name.getText(sf) === "title") errors.push(`${at(attr)}: JSX title= attribute - it shows nothing in Gameface; use Tip (cs2/ui Tooltip) (rule R7)`);
        }
      }
      if (ts.isStringLiteralLike(node) && /^Media\/[^\s"']+\.(svg|png)$/i.test(node.text)) {
        media++;
        if (uiDir && !fs.existsSync(path.join(uiDir, ...node.text.split("/")))) {
          errors.push(`${at(node)}: "${node.text}" does not exist under ${uiDir} - the icon would be blank (rule R10)`);
        }
      }
      ts.forEachChild(node, visit);
    };
    visit(sf);
  }
  if (renameOk.exists && (!renameOk.stop || !renameOk.blurCleanup)) {
    errors.push(`src/${RENAME_FIELD}: must contain stopPropagation (${renameOk.stop ? "ok" : "MISSING"}) and a useLayoutEffect cleanup that calls .blur() (${renameOk.blurCleanup ? "ok" : "MISSING"}) - otherwise the game's input-field focus can stay set after the panel closes (spec 4.3; rule R6)`);
  }
  if (inputs > 0 && !renameOk.exists) errors.push(`src/${RENAME_FIELD} is missing but the sources render an input (rule R6)`);

  const fixer = lintFixerRules(ts, parsed, entryMaps, errors);
  report.push(`contract R11-R14: ${fixer.panelRefs} panel-binding reference(s) in ${PANEL_FILES.size} panel file(s); ` +
    `${fixer.hookFns} function(s) hook-order checked; ${fixer.fmtRefs} placeholder-entry reference(s); ` +
    `${fixer.createCalls} createElement/jsx call(s); ${fixer.visible} visible JSX string(s) checked; ${fixer.locArgs} placeholder element argument(s) (R19)`);

  const edge = lintPanelEdgeFocus(ts, parsed, errors);
  report.push(`contract R15: ${edge.edges} Panel header/footer slot(s); ${edge.buttons} focusable cs2/ui JSX element(s) ` +
    `rendered there, each checked for focusKey={FOCUS_DISABLED} (sections: disableFocus) (${edge.components} component(s) and ` +
    `${edge.helpers} helper function(s) followed: ${edge.names.join(", ") || "none"}); hub-wide ${edge.hubButtons} further ` +
    "focusable cs2/ui JSX element(s), each checked the same way");

  // Each of R16-R18 runs on its own; a crash of one is an error, never a silent pass
  const tsLiterals = readTsLiterals(ts, errors);
  try { checkBuildItems(tsLiterals, errors, report); }
  catch (e) { errors.push(`contract: rule R16 crashed (${e && e.message ? e.message : e}) (rule R16)`); }
  try { checkEnumParity(tsLiterals, errors, report); }
  catch (e) { errors.push(`contract: rule R17 crashed (${e && e.message ? e.message : e}) (rule R17)`); }
  checkHubRules(errors, report);

  // R4 warning: SkiResort.Hub.* keys nobody uses
  let unused = 0;
  if (locale) {
    const csTexts = [];
    for (const f of writers.files) if (path.resolve(f) !== path.resolve(LOCALE_EN)) { try { csTexts.push(fs.readFileSync(f, "utf8")); } catch { /* skip */ } }
    for (const key of locale.keys()) {
      if (!key.startsWith("SkiResort.Hub.")) continue;
      if (allStrings.has(key)) continue;
      const quoted = `"${key}"`;
      if (csTexts.some((t) => t.includes(quoted))) continue;
      unused++;
      warnings.push(`${path.basename(LOCALE_EN)}:${locale.get(key).line}: "${key}" is used by neither src/ nor another .cs file - remove it (rule R4)`);
    }
  }
  report.push(`contract R4-R7/R10: ${tuples} locale tuple(s) checked against ${path.basename(LOCALE_EN)} (${locale ? locale.size : 0} entries, ${unused} unused SkiResort.Hub.* key(s)); ${placeholders} placeholder misuse(s); ${inputs} input(s); ${media} Media/ path(s)${uiDir ? "" : " (not checked)"}`);
  return { snap };
}

/**
 * R11-R14 and the R5 / R6 / R7 hardening; each rule here FAILS:
 *   R11 a panel binding (PANEL_BINDINGS) referenced outside PANEL_FILES and bindings.ts - an always-mounted subscriber keeps C#
 *       computing with the panel closed;
 *   R12 a hook call (use[A-Z]...) after an early return in the same function, or inside an if / loop / switch / ?: branch / the
 *       right side of && || ?? - React's hook order;
 *   R13 a reference to a placeholder entry (a [key, english] map entry whose English contains "{") that is not the `entry` prop
 *       of <LocText>, or the placeholder map itself used other than as `MAP.entry` - `const say = t; say(HUB_FMT.x)` rendered
 *       "{N} ..." literally;
 *   R14 untranslated text: JSX text with a letter, a string-literal JSX child with a letter, or a letter in the string-literal
 *       value of a player-visible prop (VISIBLE_PROPS) - structural props (variant, src, type, className, key, theme) and Media/
 *       paths are not checked;
 *   R6 / R7 hardening: createElement / jsx / jsxs calls with "input" / "textarea" (outside RenameField) or "select" / "option" /
 *       "table", and a `title` prop passed through a spread object literal or a createElement props literal. A spread of a
 *       non-literal object is not checkable here (stated, not hidden).
 * Returns counters for the report line.
 */
function lintFixerRules(ts, parsed, entryMaps, errors) {
  const out = { panelRefs: 0, hookFns: 0, fmtRefs: 0, createCalls: 0, visible: 0, alwaysMountedImports: 0, locArgs: 0 };
  const isHookName = (n) => /^use[A-Z0-9]/.test(n);
  const isFn = (n) => ts.isFunctionDeclaration(n) || ts.isFunctionExpression(n) || ts.isArrowFunction(n) ||
    ts.isMethodDeclaration(n) || ts.isGetAccessorDeclaration(n) || ts.isSetAccessorDeclaration(n) || ts.isConstructorDeclaration(n);
  const calleeName = (call) => {
    const c = call.expression;
    if (ts.isIdentifier(c)) return c.text;
    if (ts.isPropertyAccessExpression(c)) return c.name.text;
    return null;
  };
  const shortCircuit = new Set([ts.SyntaxKind.AmpersandAmpersandToken, ts.SyntaxKind.BarBarToken, ts.SyntaxKind.QuestionQuestionToken,
    ts.SyntaxKind.AmpersandAmpersandEqualsToken, ts.SyntaxKind.BarBarEqualsToken, ts.SyntaxKind.QuestionQuestionEqualsToken]);
  // Placeholder maps: map names with at least one "{...}" English (HUB_FMT today)
  const fmtMaps = new Set();
  for (const [k, en] of entryMaps) if (en.includes("{")) fmtMaps.add(k.slice(0, k.indexOf(".")));
  const hasLetter = (s) => LETTER_RE.test(s);
  const stringOf = (n) => (n && (ts.isStringLiteral(n) || ts.isNoSubstitutionTemplateLiteral(n)) ? n.text : null);
  const objHasTitle = (o) => !!o && ts.isObjectLiteralExpression(o) &&
    o.properties.some((p) => p.name && (ts.isIdentifier(p.name) || ts.isStringLiteral(p.name)) && p.name.text === "title");

  for (const { rel, sf } of parsed) {
    const relN = rel.split(path.sep).join("/");
    const at = (node) => `src/${relN}:${sf.getLineAndCharacterOfPosition(node.getStart(sf)).line + 1}`;
    const panelFile = PANEL_FILES.has(relN);
    const isBindings = relN === BINDINGS_TS;
    const isRename = relN === RENAME_FIELD;

    // R19: the JSX elements an argument value can evaluate to: through ?: && || ?? and parentheses, and one step through a
    // same-file variable of that name (every declaration of the name: conservative). A call result (numArg) is not followed.
    const declsOf = (name) => {
      const found = [];
      const visit = (n) => {
        if (ts.isVariableDeclaration(n) && ts.isIdentifier(n.name) && n.name.text === name && n.initializer) found.push(n.initializer);
        ts.forEachChild(n, visit);
      };
      visit(sf);
      return found;
    };
    if (relN === "hub/parts.tsx" && !/export const LocText = Object\.assign\(\w+, \{ renderString: \w+, propsAreEqual: \w+ \}\)/.test(sf.text)) {
      errors.push(`src/hub/parts.tsx: LocText must carry renderString and propsAreEqual (export const LocText = Object.assign(View, { renderString, propsAreEqual })) - R19 lets it be a placeholder argument only because of them (rule R19)`);
    }
    const argElements = (v, depth) => {
      if (!v) return [];
      if (ts.isJsxSelfClosingElement(v)) return [v];
      if (ts.isJsxElement(v)) return [v.openingElement];
      if (ts.isParenthesizedExpression(v)) return argElements(v.expression, depth);
      if (ts.isConditionalExpression(v)) return [...argElements(v.whenTrue, depth), ...argElements(v.whenFalse, depth)];
      if (ts.isBinaryExpression(v) && shortCircuit.has(v.operatorToken.kind)) return [...argElements(v.left, depth), ...argElements(v.right, depth)];
      if (ts.isIdentifier(v) && depth < 1) return declsOf(v.text).flatMap((init) => argElements(init, depth + 1));
      return [];
    };

    // R12: one pass per function body
    const containsReturn = (node) => {
      let found = false;
      const walk = (n) => {
        if (found || isFn(n)) return;
        if (ts.isReturnStatement(n)) { found = true; return; }
        ts.forEachChild(n, walk);
      };
      walk(node);
      return found;
    };
    const checkFunction = (fn) => {
      const body = fn.body;
      if (!body || !ts.isBlock(body)) return;
      out.hookFns++;
      let returned = false;
      for (const st of body.statements) {
        const scan = (node, cond) => {
          if (isFn(node)) return;
          if (ts.isCallExpression(node)) {
            const name = calleeName(node);
            if (name && isHookName(name)) {
              if (returned) errors.push(`${at(node)}: hook ${name}() after an early return in the same function - the hook count changes between renders; call every hook before the first return (rule R12)`);
              else if (cond) errors.push(`${at(node)}: hook ${name}() inside a condition or loop - call hooks unconditionally (rule R12)`);
            }
          }
          if (ts.isIfStatement(node)) {
            scan(node.expression, cond);
            scan(node.thenStatement, true);
            if (node.elseStatement) scan(node.elseStatement, true);
            return;
          }
          if (ts.isConditionalExpression(node)) {
            scan(node.condition, cond);
            scan(node.whenTrue, true);
            scan(node.whenFalse, true);
            return;
          }
          if (ts.isBinaryExpression(node) && shortCircuit.has(node.operatorToken.kind)) {
            scan(node.left, cond);
            scan(node.right, true);
            return;
          }
          if (ts.isForStatement(node) || ts.isForInStatement(node) || ts.isForOfStatement(node) ||
              ts.isWhileStatement(node) || ts.isDoStatement(node) || ts.isCaseClause(node) || ts.isDefaultClause(node)) {
            ts.forEachChild(node, (c) => scan(c, true));
            return;
          }
          ts.forEachChild(node, (c) => scan(c, cond));
        };
        scan(st, false);
        if (containsReturn(st)) returned = true;
      }
    };

    const visit = (node) => {
      if (isFn(node)) checkFunction(node);

      // R11: an always-mounted file may import only HubPanel from the panel files
      if (ts.isImportDeclaration(node) && ts.isStringLiteral(node.moduleSpecifier) && node.moduleSpecifier.text.startsWith(".") &&
          ALWAYS_MOUNTED_FILES.has(relN)) {
        const target = path.posix.normalize(path.posix.join(path.posix.dirname(relN), node.moduleSpecifier.text));
        const hit = [...PANEL_FILES].find((p) => p.replace(/\.tsx?$/, "") === target.replace(/\.tsx?$/, ""));
        if (hit) {
          const clause = node.importClause;
          const typeOnly = !!clause && (clause.isTypeOnly ||
            (!clause.name && clause.namedBindings && ts.isNamedImports(clause.namedBindings) && clause.namedBindings.elements.length > 0 &&
             clause.namedBindings.elements.every((el) => el.isTypeOnly)));
          const named = clause && clause.namedBindings && ts.isNamedImports(clause.namedBindings) ? clause.namedBindings.elements : [];
          const onlyEntry = hit === PANEL_ENTRY && clause && !clause.name && named.length > 0 &&
            named.every((el) => (el.propertyName ? el.propertyName.text : el.name.text) === "HubPanel");
          out.alwaysMountedImports++;
          if (!typeOnly && !onlyEntry) {
            errors.push(`${at(node)}: always-mounted src/${relN} imports from the panel file src/${hit} - only { HubPanel } from ${PANEL_ENTRY} may be imported (and only \`import type\` from other panel files): anything else mounts panel subscriptions while the hub is closed (spec 3.6; rule R11)`);
          }
        }
      }

      // R11: panel bindings outside panel files
      if (ts.isIdentifier(node) && PANEL_BINDINGS.has(node.text)) {
        out.panelRefs++;
        if (!panelFile && !isBindings) {
          errors.push(`${at(node)}: panel binding ${node.text} referenced in src/${relN}, which is not a panel-mounted file - an always-mounted subscriber keeps C# computing while the panel is closed (spec 3.6). Subscribe inside the panel, or add the file to PANEL_FILES if it mounts only with the panel (rule R11)`);
        }
      }

      // R13: placeholder entries only as <LocText entry={...}>
      if (ts.isPropertyAccessExpression(node) && ts.isIdentifier(node.expression) &&
          (entryMaps.get(`${node.expression.text}.${node.name.text}`) ?? "").includes("{")) {
        out.fmtRefs++;
        const p = node.parent;
        const ok = p && ts.isJsxExpression(p) && p.parent && ts.isJsxAttribute(p.parent) &&
          p.parent.name.getText(sf) === "entry" && p.parent.parent && p.parent.parent.parent &&
          (ts.isJsxOpeningElement(p.parent.parent.parent) || ts.isJsxSelfClosingElement(p.parent.parent.parent)) &&
          p.parent.parent.parent.tagName.getText(sf) === "LocText";
        if (!ok) errors.push(`${at(node)}: placeholder entry ${node.getText(sf)} used outside <LocText entry={...}> - only LocalizedString fills "{...}" (rule R13, R5 hardening)`);
      }
      if (ts.isIdentifier(node) && fmtMaps.has(node.text)) {
        const p = node.parent;
        const ok = (ts.isPropertyAccessExpression(p) && p.expression === node) || ts.isImportSpecifier(p) || ts.isExportSpecifier(p) ||
          (ts.isVariableDeclaration(p) && p.name === node);
        if (!ok) errors.push(`${at(node)}: the placeholder map ${node.text} is used as a value (aliased or spread) - reference its entries as ${node.text}.x inside <LocText entry={...}> only (rule R13, R5 hardening)`);
      }

      // R19: an element argument of a placeholder is a loc component
      if (ts.isJsxAttribute(node) && node.name.getText(sf) === "args" && node.initializer && ts.isJsxExpression(node.initializer)) {
        const owner = node.parent && node.parent.parent;
        const tag = owner && (ts.isJsxOpeningElement(owner) || ts.isJsxSelfClosingElement(owner)) ? owner.tagName.getText(sf) : "";
        const obj = node.initializer.expression;
        if ((tag === "LocText" || tag === "LocalizedString") && obj && ts.isObjectLiteralExpression(obj)) {
          for (const prop of obj.properties) {
            const v = ts.isPropertyAssignment(prop) ? prop.initializer : ts.isShorthandPropertyAssignment(prop) ? prop.name : null;
            for (const el of argElements(v, 0)) {
              out.locArgs++;
              const name = el.tagName.getText(sf);
              if (!LOC_ARG_COMPONENTS.has(name)) {
                errors.push(`${at(el)}: <${name}> is an argument of <${tag}> at ${at(node)} - the game turns an element argument into text through its type's renderString and prints "[${name}/]" for anything else; pass a string or one of ${[...LOC_ARG_COMPONENTS].join(", ")} (rule R19)`);
              }
            }
          }
        }
      }

      // R14: untranslated text
      if (ts.isJsxText(node)) {
        const text = node.text.trim();
        if (text) {
          out.visible++;
          if (hasLetter(text)) errors.push(`${at(node)}: JSX text "${text.slice(0, 40)}" is not translated - use t(HUB_LOC.x) or <LocText> with a key in LocaleEN.cs (rule R14)`);
        }
      }
      if (ts.isJsxExpression(node) && node.parent && (ts.isJsxElement(node.parent) || ts.isJsxFragment(node.parent))) {
        const s = stringOf(node.expression);
        if (s !== null) {
          out.visible++;
          if (hasLetter(s)) errors.push(`${at(node)}: JSX child string "${s.slice(0, 40)}" is not translated (rule R14)`);
        }
      }
      if (ts.isJsxAttribute(node) && VISIBLE_PROPS.has(node.name.getText(sf)) && node.initializer) {
        const init = node.initializer;
        const s = stringOf(init) ?? (ts.isJsxExpression(init) ? stringOf(init.expression) : null);
        if (s !== null) {
          out.visible++;
          if (hasLetter(s) && !/^Media\//.test(s)) errors.push(`${at(node)}: ${node.name.getText(sf)}="${s.slice(0, 40)}" is not translated (rule R14)`);
        }
      }

      // R6 / R7 hardening: createElement / jsx calls and spread props
      if (ts.isCallExpression(node)) {
        const name = calleeName(node);
        if (name && /^(createElement|jsx|jsxs|jsxDEV|_jsx|_jsxs)$/.test(name)) {
          out.createCalls++;
          const tag = stringOf(node.arguments[0]);
          if ((tag === "input" || tag === "textarea") && !isRename) errors.push(`${at(node)}: ${name}("${tag}") outside src/${RENAME_FIELD} (rule R6)`);
          if (tag === "select" || tag === "option" || tag === "table") errors.push(`${at(node)}: ${name}("${tag}") - not rendered by Gameface (source lint)`);
          if (objHasTitle(node.arguments[1])) errors.push(`${at(node)}: ${name}(..., { title }) - title shows nothing in Gameface; use Tip (rule R7)`);
        }
      }
      if (ts.isJsxSpreadAttribute(node) && objHasTitle(node.expression)) {
        errors.push(`${at(node)}: {...{ title }} spread into JSX - title shows nothing in Gameface; use Tip (rule R7)`);
      }

      ts.forEachChild(node, visit);
    };
    visit(sf);
  }
  return out;
}

/** Rule R15: cs2/ui components that register a focus key unless told not to (focusKey prop, ui.d.ts ButtonProps) */
const EDGE_FOCUSABLE = new Set(["Button", "FloatingButton", "MenuButton", "DropdownToggle", "Dropdown"]);
/**
 * Rule R15: cs2/ui sections that register a focus key unless given a literal `disableFocus` (installed index.js@198225 exports
 * PanelSection = InfoSection @721944, PanelFoldout = InfoSectionFoldout @730261, PanelSectionRow = InfoRow @731753). Without it
 * each renders an ActiveFocusDiv (`Gp` @586265: focusKey FOCUS_AUTO, activation Always); with it a pass-through div (`Bp`) whose
 * inner NavigationScope registers only when a focusable child does (activation AnyChildren, @570953), and those children are
 * checked on their own. A PanelSectionRow with `link` renders the ActiveFocusDiv whatever disableFocus says.
 */
const EDGE_SECTIONS = new Set(["PanelSection", "PanelFoldout", "PanelSectionRow"]);
/** Rule R15: the cs2/ui Panel props whose content sits under a single-child focus controller */
const EDGE_SLOTS = new Set(["header", "footer"]);

/**
 * Rule R15. cs2/ui Panel wraps its header and its footer each in a FocusKeyOverride (installed 1.6.2f1 index.js@684800 `Tp`)
 * whose KeyFocusController hosts ONE child (`bp.registerChild` @578500: "Cannot register second focus key ... KeyFocusControllers
 * can only host a single child"), and a cs2/ui Button registers itself unless its focusKey is FOCUS_DISABLED (button.tsx @629600:
 * `Gg(e??(s?Ug:Bg))`, FOCUS_AUTO by default). The vanilla title bar's close button passes focusKey:Ug (FOCUS_DISABLED, @686490).
 * FAILS: a focusable cs2/ui component (EDGE_FOCUSABLE) rendered, directly or through our own components, inside the `header` /
 * `footer` prop of a cs2/ui <Panel> without `focusKey={FOCUS_DISABLED}` (the identifier imported from cs2/input), with a spread
 * and no explicit focusKey, or with a spread after the explicit focusKey (it can override it). Also FAILS: a cs2/ui PanelSection /
 * PanelFoldout / PanelSectionRow (EDGE_SECTIONS) there without a literal `disableFocus` (bare, or ={true}), with a spread after
 * it, or - PanelSectionRow - with `link` or any spread (a spread could carry link). Also FAILS when src/ has no Panel header /
 * footer at all (the rule would check nothing). Followed: JSX tags (capitalized), and helper functions - the callee of a call in
 * the slot (`{tile(...)}`) and a function passed by name as a call argument (`.map(tile)`) - each when it is a local declaration
 * of the same file or a relative import (its exported declaration; a default import = the whole file); cs2/*, react and npm names
 * are not followed. Not followed: JSX kept in a variable outside the slot (HubPanel's `failed` fallback), a component passed as a
 * prop value, createElement calls, a function reached through a property (`parts.tile()`, `{...}.map(o.render)`) or declared
 * inside another function's body outside the slot.
 */
function lintPanelEdgeFocus(ts, parsed, errors) {
  const out = { edges: 0, buttons: 0, hubButtons: 0, components: 0, helpers: 0, names: [] };
  const byRel = new Map(parsed.map((p) => [p.rel.split(path.sep).join("/"), p]));
  const facts = new Map();
  /** Per file: value imports (local name -> { spec, name }) and top-level declarations (name -> node) */
  const factsOf = (rel) => {
    if (facts.has(rel)) return facts.get(rel);
    const p = byRel.get(rel);
    if (!p) return null;
    const imports = new Map(), decls = new Map();
    for (const st of p.sf.statements) {
      if (ts.isImportDeclaration(st) && st.importClause && !st.importClause.isTypeOnly) {
        const spec = st.moduleSpecifier.text;
        if (st.importClause.name) imports.set(st.importClause.name.text, { spec, name: "default" });
        const nb = st.importClause.namedBindings;
        if (nb && ts.isNamedImports(nb)) {
          for (const el of nb.elements) if (!el.isTypeOnly) imports.set(el.name.text, { spec, name: (el.propertyName || el.name).text });
        }
      } else if ((ts.isFunctionDeclaration(st) || ts.isClassDeclaration(st)) && st.name) {
        decls.set(st.name.text, st);
      } else if (ts.isVariableStatement(st)) {
        for (const d of st.declarationList.declarations) if (ts.isIdentifier(d.name) && d.initializer) decls.set(d.name.text, d);
      }
    }
    const f = { p, rel, imports, decls };
    facts.set(rel, f);
    return f;
  };
  const resolveRel = (fromRel, spec) => {
    const base = path.posix.normalize(path.posix.join(path.posix.dirname(fromRel), spec));
    for (const ext of ["", ".tsx", ".ts", "/index.tsx", "/index.ts"]) if (byRel.has(base + ext)) return base + ext;
    return null;
  };
  const at = (f, node) => `src/${f.rel}:${f.p.sf.getLineAndCharacterOfPosition(node.getStart(f.p.sf)).line + 1}`;
  const visited = new Set();

  const R15_WHY = "the Panel's header / footer focus controller hosts ONE child, so each further registrant logs \"Cannot " +
    "register second focus key\" to UI.log on every open and close (field test 6: 106 lines)";
  /** null when el passes focusKey={FOCUS_DISABLED} (from cs2/input) after every spread, else what is wrong */
  const focusKeyFault = (f, el) => {
    let ok = false, keyAt = -1, spreadAt = -1;
    el.attributes.properties.forEach((a, i) => {
      if (ts.isJsxSpreadAttribute(a)) { spreadAt = i; return; }
      if (a.name.getText(f.p.sf) !== "focusKey") return;
      keyAt = i;
      const e = a.initializer && ts.isJsxExpression(a.initializer) ? a.initializer.expression : null;
      const imp = e && ts.isIdentifier(e) ? f.imports.get(e.text) : null;
      ok = !!imp && imp.spec === "cs2/input" && imp.name === "FOCUS_DISABLED";
    });
    const late = ok && spreadAt > keyAt; // a later spread can override the explicit prop
    if (ok && !late) return null;
    return late ? "with focusKey={FOCUS_DISABLED} BEFORE a spread, which can override it - put focusKey after every spread"
      : "without focusKey={FOCUS_DISABLED} (imported from cs2/input)" +
        (spreadAt >= 0 ? "; a spread is not checkable, pass focusKey explicitly after it" : "");
  };
  /** Elements the Panel header / footer walk checked; the hub-wide pass skips them (one message per element) */
  const slotChecked = new Set();
  const checkFocusKey = (f, el, kind, via) => {
    slotChecked.add(el);
    const fault = focusKeyFault(f, el);
    if (fault) {
      errors.push(`${at(f, el)}: <${el.tagName.getText(f.p.sf)}> (cs2/ui ${kind}) renders in a Panel ${via} ${fault}` +
        ` - ${R15_WHY}. Vanilla's close button passes it too (rule R15)`);
    }
  };
  /** PanelSection / PanelFoldout / PanelSectionRow (EDGE_SECTIONS) need a literal disableFocus */
  const checkDisableFocus = (f, el, kind, via) => {
    slotChecked.add(el);
    let ok = false, flagAt = -1, spreadAt = -1, link = false;
    el.attributes.properties.forEach((a, i) => {
      if (ts.isJsxSpreadAttribute(a)) { spreadAt = i; return; }
      const name = a.name.getText(f.p.sf);
      if (name === "link") link = true;
      if (name !== "disableFocus") return;
      flagAt = i;
      const e = !a.initializer ? null : ts.isJsxExpression(a.initializer) ? a.initializer.expression : a.initializer;
      ok = !a.initializer || (!!e && e.kind === ts.SyntaxKind.TrueKeyword);
    });
    const row = kind === "PanelSectionRow";
    const why = row && link ? "with link - a link row registers a focus key whatever disableFocus says (InfoRow, index.js@731753)"
      : !ok ? "without a literal disableFocus (bare, or ={true})"
        : row && spreadAt >= 0 ? "with a spread - it could carry link, which disableFocus does not override; pass the props explicitly"
          : spreadAt > flagAt ? "with disableFocus BEFORE a spread, which can override it - put disableFocus after every spread"
            : null;
    if (why) {
      errors.push(`${at(f, el)}: <${el.tagName.getText(f.p.sf)}> (cs2/ui ${kind}) renders in a Panel ${via} ${why} - ` +
        `without it the section is an ActiveFocusDiv (FOCUS_AUTO, activation Always: index.js@586265) and ${R15_WHY} (rule R15)`);
    }
  };
  /** A helper passed by name (`.map(tile)`) is followed only when it is a function */
  const isFnLike = (d) => !!d && (ts.isFunctionDeclaration(d) || (ts.isVariableDeclaration(d) && !!d.initializer &&
    (ts.isArrowFunction(d.initializer) || ts.isFunctionExpression(d.initializer))));
  /** how: undefined = a JSX tag, "call" = a called helper, "ref" = a helper passed by name */
  const follow = (f, name, via, how) => {
    let tf = f, tname = name;
    if (!f.decls.has(name)) {
      const imp = f.imports.get(name);
      if (!imp || !imp.spec.startsWith(".")) return;
      const rel = resolveRel(f.rel, imp.spec);
      tf = rel ? factsOf(rel) : null;
      if (!tf) return;
      tname = imp.name;
    }
    const decl = tf.decls.get(tname);
    if (how === "ref" && !isFnLike(decl)) return;
    const key = `${tf.rel}#${tname}`;
    if (visited.has(key)) return;
    visited.add(key);
    const shown = how ? `${tname}()` : tname;
    if (how) out.helpers++; else out.components++;
    out.names.push(shown);
    walk(tf, decl || tf.p.sf, `${via} > ${shown}`);
  };
  /** Every JSX tag under `node`: focusable cs2/ui components are checked, our own components and helpers followed. */
  const walk = (f, node, via) => {
    const visit = (n) => {
      if ((ts.isJsxOpeningElement(n) || ts.isJsxSelfClosingElement(n)) && ts.isIdentifier(n.tagName)) {
        const name = n.tagName.text;
        const imp = f.imports.get(name);
        if (imp && imp.spec === "cs2/ui" && EDGE_FOCUSABLE.has(imp.name)) {
          out.buttons++;
          checkFocusKey(f, n, imp.name, via);
        } else if (imp && imp.spec === "cs2/ui" && EDGE_SECTIONS.has(imp.name)) {
          out.buttons++;
          checkDisableFocus(f, n, imp.name, via);
        } else if (/^[A-Z]/.test(name)) {
          follow(f, name, via);
        }
      } else if (ts.isCallExpression(n)) {
        // JSX returned by a helper called in the slot, or by one passed to a call by name
        if (ts.isIdentifier(n.expression)) follow(f, n.expression.text, via, "call");
        for (const a of n.arguments) if (ts.isIdentifier(a)) follow(f, a.text, via, "ref");
      }
      ts.forEachChild(n, visit);
    };
    visit(node);
  };

  for (const p of parsed) {
    const f = factsOf(p.rel.split(path.sep).join("/"));
    if (!f) continue;
    const findPanels = (n) => {
      if ((ts.isJsxOpeningElement(n) || ts.isJsxSelfClosingElement(n)) && ts.isIdentifier(n.tagName)) {
        const imp = f.imports.get(n.tagName.text);
        if (imp && imp.spec === "cs2/ui" && imp.name === "Panel") {
          for (const a of n.attributes.properties) {
            if (!ts.isJsxAttribute(a) || !a.initializer || !EDGE_SLOTS.has(a.name.getText(p.sf))) continue;
            out.edges++;
            walk(f, a.initializer, `${a.name.getText(p.sf)} (${at(f, a)})`);
          }
        }
      }
      ts.forEachChild(n, findPanels);
    };
    findPanels(p.sf);
  }
  if (out.edges === 0) {
    errors.push("src: rule R15 found no cs2/ui <Panel header={...} / footer={...}> - it would check nothing; update lintPanelEdgeFocus in tools/check-bundle.js (rule R15)");
  }

  // Hub-wide pass: every focusable cs2/ui element anywhere in src/, not only in the Panel slots
  const R15_HUB_WHY = "the hub is mouse-only and its controls never take the game's key focus (a mouse click still works " +
    "with FOCUS_DISABLED)";
  for (const p of parsed) {
    const f = factsOf(p.rel.split(path.sep).join("/"));
    if (!f) continue;
    const visit = (n) => {
      if ((ts.isJsxOpeningElement(n) || ts.isJsxSelfClosingElement(n)) && ts.isIdentifier(n.tagName) && !slotChecked.has(n)) {
        const imp = f.imports.get(n.tagName.text);
        if (imp && imp.spec === "cs2/ui" && EDGE_FOCUSABLE.has(imp.name)) {
          out.hubButtons++;
          const fault = focusKeyFault(f, n);
          if (fault) errors.push(`${at(f, n)}: <${n.tagName.text}> (cs2/ui ${imp.name}) ${fault} - ${R15_HUB_WHY} (rule R15)`);
        } else if (imp && imp.spec === "cs2/ui" && EDGE_SECTIONS.has(imp.name)) {
          out.hubButtons++;
          let flag = false;
          for (const a of n.attributes.properties) {
            if (!ts.isJsxAttribute(a) || a.name.getText(f.p.sf) !== "disableFocus") continue;
            const e = !a.initializer ? null : ts.isJsxExpression(a.initializer) ? a.initializer.expression : a.initializer;
            flag = !a.initializer || (!!e && e.kind === ts.SyntaxKind.TrueKeyword);
          }
          if (!flag) {
            errors.push(`${at(f, n)}: <${n.tagName.text}> (cs2/ui ${imp.name}) without a literal disableFocus (bare, or ={true}) - ` +
              "it registers a FOCUS_AUTO focus key, and the hub is mouse-only: no hub control takes the game's key focus (rule R15)");
          }
        }
      }
      ts.forEachChild(n, visit);
    };
    visit(p.sf);
  }
  return out;
}

// R16 and R17 read the C# files literally: keep the compared enums literal (HubRules.cs, EntityRef.cs, SkiMoneyRules.cs,
// SkiDayRules.cs). R16 reads Systems/SkiCatalog.cs's NAMES and BUILD-ITEMS blocks as raw lines (one grammar each, below).

/** `enum <name> [: type] { A = 0, B, ... }` -> Map name -> number (explicit values or the implicit +1 rule), or null when absent or not literal. Comments must be stripped first. */
function csEnum(src, name) {
  const m = new RegExp(`\\benum\\s+${name}\\b[^{;]*\\{([^}]*)\\}`).exec(src);
  if (!m) return null;
  const out = new Map();
  let next = 0;
  for (const raw of m[1].split(",")) {
    const s = raw.trim();
    if (!s) continue;
    const kv = /^([A-Za-z_]\w*)\s*(?:=\s*(-?\d+))?$/.exec(s);
    if (!kv) return null;
    const v = kv[2] !== undefined ? Number(kv[2]) : next;
    out.set(kv[1], v);
    next = v + 1;
  }
  return out;
}

/** The TS literals R16 / R17 read (TypeScript AST): bindings.ts BUILD_ITEM_IDS, the BuildItemId union, BUILD_ITEM_PREFAB, LIFT_TYPE_IDS and every UPPER_CASE const object of literal numbers; buildItems.ts BUILD_GROUPS, BUILD_ITEMS (rows of literals) and BUILD_ITEM_ICON (its keys). A missing or non-literal value stays null (the rule reports it). */
function readTsLiterals(ts, errors) {
  const out = { ids: null, union: null, prefab: null, groups: null, liftTypes: null, items: null, iconKeys: null, consts: new Map() };
  const unwrap = (e) => { while (e && (ts.isAsExpression(e) || ts.isParenthesizedExpression(e))) e = e.expression; return e; };
  const keyText = (sf, p) => p.name.getText(sf).replace(/^["']|["']$/g, "");
  let textB = null, textI = null;
  try { textB = fs.readFileSync(path.join(SRC, BINDINGS_TS), "utf8"); } catch { errors.push(`contract: cannot read src/${BINDINGS_TS} (rules R16, R17)`); }
  try { textI = fs.readFileSync(path.join(SRC, BUILD_ITEMS_TS), "utf8"); } catch { errors.push(`contract: cannot read src/${BUILD_ITEMS_TS.split(path.sep).join("/")} (rule R16)`); }
  if (textB !== null) {
    const sf = ts.createSourceFile(BINDINGS_TS, textB, ts.ScriptTarget.ES2020, true, ts.ScriptKind.TS);
    const visit = (n) => {
      if (ts.isTypeAliasDeclaration(n) && n.name.text === "BuildItemId" && ts.isUnionTypeNode(n.type)) {
        out.union = n.type.types.filter((x) => ts.isLiteralTypeNode(x) && ts.isStringLiteral(x.literal)).map((x) => x.literal.text);
      }
      if (ts.isVariableDeclaration(n) && ts.isIdentifier(n.name) && n.initializer) {
        const init = unwrap(n.initializer);
        if (n.name.text === "BUILD_ITEM_IDS" && ts.isArrayLiteralExpression(init)) {
          out.ids = init.elements.every(ts.isStringLiteral) ? init.elements.map((e) => e.text) : null;
        }
        if (n.name.text === "LIFT_TYPE_IDS" && ts.isArrayLiteralExpression(init)) {
          out.liftTypes = init.elements.every(ts.isStringLiteral) ? init.elements.map((e) => e.text) : null;
        }
        if (n.name.text === "BUILD_ITEM_PREFAB" && ts.isObjectLiteralExpression(init)) {
          out.prefab = new Map();
          for (const p of init.properties) {
            if (ts.isPropertyAssignment(p) && ts.isStringLiteralLike(p.initializer)) out.prefab.set(keyText(sf, p), p.initializer.text);
            else { out.prefab = null; break; }
          }
        }
        if (/^[A-Z][A-Z_]+$/.test(n.name.text) && ts.isObjectLiteralExpression(init)) {
          const m = new Map();
          let ok = true;
          for (const p of init.properties) {
            if (!ts.isPropertyAssignment(p)) { ok = false; break; }
            const e = p.initializer;
            const v = ts.isNumericLiteral(e) ? Number(e.text)
              : ts.isPrefixUnaryExpression(e) && e.operator === ts.SyntaxKind.MinusToken && ts.isNumericLiteral(e.operand) ? -Number(e.operand.text) : null;
            if (v === null) { ok = false; break; }
            m.set(keyText(sf, p), v);
          }
          if (ok && m.size) out.consts.set(n.name.text, m);
        }
      }
      ts.forEachChild(n, visit);
    };
    visit(sf);
  }
  if (textI !== null) {
    const sf = ts.createSourceFile("buildItems.ts", textI, ts.ScriptTarget.ES2020, true, ts.ScriptKind.TS);
    // One literal row: { id: "x", kind: "y", liftType: "z", runKind: n } (a leading "-" allowed), nothing else
    const row = (e) => {
      e = unwrap(e);
      if (!ts.isObjectLiteralExpression(e)) return null;
      const r = {};
      for (const p of e.properties) {
        if (!ts.isPropertyAssignment(p)) return null;
        const k = keyText(sf, p);
        const v = unwrap(p.initializer);
        if (ts.isStringLiteralLike(v)) r[k] = v.text;
        else if (ts.isNumericLiteral(v)) r[k] = Number(v.text);
        else if (ts.isPrefixUnaryExpression(v) && v.operator === ts.SyntaxKind.MinusToken && ts.isNumericLiteral(v.operand)) r[k] = -Number(v.operand.text);
        else return null;
      }
      const keys = Object.keys(r).sort().join(",");
      if (keys !== "id,kind,liftType,runKind") return null;
      if (typeof r.id !== "string" || typeof r.kind !== "string" || typeof r.liftType !== "string" || typeof r.runKind !== "number") return null;
      return r;
    };
    const visit = (n) => {
      if (ts.isVariableDeclaration(n) && ts.isIdentifier(n.name) && n.initializer) {
        const init = unwrap(n.initializer);
        if (n.name.text === "BUILD_GROUPS" && ts.isArrayLiteralExpression(init) &&
            init.elements.every((g) => ts.isArrayLiteralExpression(g) && g.elements.every(ts.isStringLiteral))) {
          out.groups = init.elements.map((g) => g.elements.map((e) => e.text));
        }
        if (n.name.text === "BUILD_ITEMS" && ts.isArrayLiteralExpression(init)) {
          const rows = init.elements.map(row);
          out.items = rows.every((r) => r !== null) ? rows : null;
        }
        if (n.name.text === "BUILD_ITEM_ICON" && ts.isObjectLiteralExpression(init)) {
          out.iconKeys = init.properties.every(ts.isPropertyAssignment) ? init.properties.map((p) => keyText(sf, p)) : null;
        }
      }
      ts.forEachChild(n, visit);
    };
    visit(sf);
  }
  return out;
}

/** One NAMES line; groups 1 const, 2 prefab name, 3 PrefabID type, 4 item, 5 pending */
const NAMES_LINE_RE = /^\s*public const string (\w+)\s*=\s*"([^"]+)";\s*\/\/\s*(\w+Prefab)(?:,\s*item\s+(\w+))?(?:,\s*(pending))?\s*$/;
/** One BUILD-ITEMS row; groups 1 id, 2 Names const, 3 kind, 4 lift type, 5 run kind */
const BUILD_ITEM_LINE_RE = /^\s*new BuildItemDef\("(\w+)",\s*Names\.(\w+),\s*BuildItemKind\.(\w+),\s*"(\w*)",\s*(-?\d+)\),\s*$/;
/** C# BuildItemKind -> its PrefabID type (SkiCatalog.TypeNameOf): Line -> PowerLinePrefab, Run -> SurfacePrefab, Fence -> NetFencePrefab, else BuildingPrefab */
function typeNameOfKind(kind) {
  return kind === "Line" ? "PowerLinePrefab" : kind === "Run" ? "SurfacePrefab" : kind === "Fence" ? "NetFencePrefab" : "BuildingPrefab";
}

/**
 * The raw lines between `// <name>-BEGIN` and `// <name>-END` of a C# text, with their 1-based line numbers, or an error text
 * (markers missing, repeated or out of order). `before` / `after` = the lines right outside the markers.
 */
function markerBlock(lines, name) {
  const begin = [], end = [];
  lines.forEach((l, i) => {
    if (new RegExp(`^\\s*//\\s*${name}-BEGIN\\s*$`).test(l)) begin.push(i);
    if (new RegExp(`^\\s*//\\s*${name}-END\\s*$`).test(l)) end.push(i);
  });
  if (begin.length !== 1 || end.length !== 1) return { error: `has ${begin.length} "// ${name}-BEGIN" and ${end.length} "// ${name}-END" marker line(s), want one each` };
  if (end[0] <= begin[0]) return { error: `"// ${name}-END" (line ${end[0] + 1}) is not after "// ${name}-BEGIN" (line ${begin[0] + 1})` };
  const body = [];
  for (let i = begin[0] + 1; i < end[0]; i++) body.push({ text: lines[i], line: i + 1 });
  return { body, before: begin[0] > 0 ? lines[begin[0] - 1] : "", after: end[0] + 1 < lines.length ? lines[end[0] + 1] : "", beginLine: begin[0] + 1 };
}

/**
 * Rule R16: build items, C# <-> TS. Reads CATALOG_CS raw (its NAMES grammar sits in end-of-line comments and the markers are
 * comments) and the TS literals. FAILS, one line each ending "(rule R16)": the file unreadable; a block's markers missing /
 * repeated / out of order; a BUILD-ITEMS block not directly inside its array braces (`{` before BEGIN, `};` after END); a line
 * inside a block that does not match its grammar (a comment line included: every line between the markers is an entry); an
 * empty NAMES block or table; a duplicate NAMES const or prefab name; a duplicate table id; an unknown BuildItemKind (the C#
 * enum, comments stripped); `Names.X` not in NAMES; the NAMES type not the kind's (TypeNameOf); a NAMES `item` token without a
 * table row, or a row whose NAMES entry has a different or no item token; a Run row whose id does not start with "run" (or
 * another row whose id does), a run kind outside RUN_KIND (TS, itself R17-checked against C# RunKind), a non-run row with run
 * kind != -1; a Station / Line row without a lift type or another row with one; a lift type without exactly one base station
 * ("baseStation..." id), one top station ("topStation..." id) and one line row; the table's lift types != bindings.ts
 * LIFT_TYPE_IDS; a TS literal missing or not literal; BUILD_ITEMS row i != C# row i (id, kind in lower camel case, liftType,
 * runKind) or a different row count; the BuildItemId union, BUILD_ITEM_IDS, BUILD_ITEM_PREFAB keys or BUILD_ITEM_ICON keys !=
 * the table's id set; BUILD_ITEM_PREFAB[id] != the C# row's prefab; BUILD_GROUPS not holding every id exactly once.
 */
function checkBuildItems(tsSide, errors, report) {
  const at = path.basename(CATALOG_CS);
  const fail = (msg) => errors.push(`contract: ${msg} (rule R16)`);
  let text;
  try { text = fs.readFileSync(CATALOG_CS, "utf8"); } catch (e) { fail(`cannot read ${CATALOG_CS} (${e && e.code ? e.code : e})`); return; }
  const lines = text.split(/\r?\n/);

  const nb = markerBlock(lines, "NAMES");
  if (nb.error) { fail(`${at} ${nb.error}`); return; }
  const names = new Map();
  const byPrefab = new Set();
  for (const { text: l, line } of nb.body) {
    const m = NAMES_LINE_RE.exec(l);
    if (!m) { fail(`${at}:${line} inside the NAMES block does not match \`public const string X = "Name"; // Type[, item id][, pending]\`: "${l.trim()}"`); continue; }
    if (names.has(m[1])) fail(`${at}:${line} NAMES const ${m[1]} declared twice`);
    if (byPrefab.has(m[2])) fail(`${at}:${line} NAMES prefab "${m[2]}" listed twice`);
    byPrefab.add(m[2]);
    names.set(m[1], { name: m[2], type: m[3], item: m[4] || null, pending: !!m[5], line });
  }
  if (names.size === 0) { fail(`${at} has an empty NAMES block`); return; }

  const bb = markerBlock(lines, "BUILD-ITEMS");
  if (bb.error) { fail(`${at} ${bb.error}`); return; }
  if (bb.before.trim() !== "{") fail(`${at}:${bb.beginLine} the line before "// BUILD-ITEMS-BEGIN" must be "{" (the markers sit INSIDE the array braces: MUST 8), found "${bb.before.trim()}"`);
  if (bb.after.trim() !== "};") fail(`${at} the line after "// BUILD-ITEMS-END" must be "};" (the markers sit INSIDE the array braces: MUST 8), found "${bb.after.trim()}"`);
  const csKinds = csEnum(stripCsComments(text), "BuildItemKind");
  if (!csKinds) fail(`${at} has no enum BuildItemKind with literal values`);
  const rows = [];
  for (const { text: l, line } of bb.body) {
    const m = BUILD_ITEM_LINE_RE.exec(l);
    if (!m) { fail(`${at}:${line} inside the BUILD-ITEMS block does not match \`new BuildItemDef("id", Names.X, BuildItemKind.K, "type", n),\`: "${l.trim()}"`); continue; }
    rows.push({ id: m[1], nameConst: m[2], kind: m[3], liftType: m[4], runKind: Number(m[5]), line });
  }
  if (rows.length === 0) { fail(`${at} has an empty BUILD-ITEMS table`); return; }

  const runKinds = tsSide.consts.get("RUN_KIND");
  const runKindValues = runKinds ? new Set(runKinds.values()) : null;
  if (!runKindValues) fail("bindings.ts has no literal const object RUN_KIND");
  const ids = [];
  const prefabOf = new Map();
  const itemRows = new Map();
  const types = new Map();
  for (const r of rows) {
    const w = `${at}:${r.line} row "${r.id}"`;
    if (ids.indexOf(r.id) >= 0) fail(`${w} duplicates an id`);
    ids.push(r.id);
    if (csKinds && !csKinds.has(r.kind)) fail(`${w}: BuildItemKind.${r.kind} is not a member of the C# enum`);
    const n = names.get(r.nameConst);
    if (!n) { fail(`${w}: Names.${r.nameConst} is not in the NAMES block`); continue; }
    prefabOf.set(r.id, n.name);
    itemRows.set(r.id, r.nameConst);
    if (n.type !== typeNameOfKind(r.kind)) fail(`${w}: Names.${r.nameConst} "${n.name}" is a ${n.type}, a ${r.kind} item needs a ${typeNameOfKind(r.kind)}`);
    if (n.item !== r.id) fail(`${w}: Names.${r.nameConst} carries ${n.item ? `item token "${n.item}"` : "no item token"} (${at}:${n.line}), want "item ${r.id}"`);
    const isRun = r.kind === "Run";
    if (isRun !== r.id.startsWith("run")) fail(`${w}: ${isRun ? "a Run row's id must start with \"run\"" : `only Run rows may have an id starting with "run" (kind ${r.kind})`}`);
    if (isRun && runKindValues && !runKindValues.has(r.runKind)) fail(`${w}: run kind ${r.runKind} is not a RUN_KIND value`);
    if (!isRun && r.runKind !== -1) fail(`${w}: a ${r.kind} row must have run kind -1, has ${r.runKind}`);
    const lift = r.kind === "Station" || r.kind === "Line";
    if (lift !== (r.liftType !== "")) fail(`${w}: ${lift ? `a ${r.kind} row needs a lift type` : `a ${r.kind} row must have lift type "", has "${r.liftType}"`}`);
    if (lift && r.liftType) {
      if (!types.has(r.liftType)) types.set(r.liftType, { base: 0, top: 0, line: 0 });
      const t = types.get(r.liftType);
      if (r.kind === "Line") t.line++;
      else if (r.id.startsWith("baseStation")) t.base++;
      else if (r.id.startsWith("topStation")) t.top++;
      else fail(`${w}: a Station row's id must start with "baseStation" or "topStation" (LiftBatch CT-14)`);
    }
  }
  for (const [c, n] of names) {
    if (n.item && !rows.some((r) => r.id === n.item && r.nameConst === c)) {
      fail(`${at}:${n.line} Names.${c} "${n.name}" carries "item ${n.item}" but no BUILD-ITEMS row "${n.item}" names Names.${c}`);
    }
  }
  for (const [type, t] of types) {
    if (t.base !== 1 || t.top !== 1 || t.line !== 1) fail(`${at} lift type "${type}" has ${t.base} base station, ${t.top} top station and ${t.line} line row(s), want 1 each`);
  }

  if (!tsSide.ids || !tsSide.union || !tsSide.prefab || !tsSide.groups || !tsSide.items || !tsSide.iconKeys || !tsSide.liftTypes) {
    const missing = [["bindings.ts BUILD_ITEM_IDS", tsSide.ids], ["bindings.ts BuildItemId", tsSide.union], ["bindings.ts BUILD_ITEM_PREFAB", tsSide.prefab],
      ["bindings.ts LIFT_TYPE_IDS", tsSide.liftTypes], ["buildItems.ts BUILD_ITEMS", tsSide.items], ["buildItems.ts BUILD_GROUPS", tsSide.groups],
      ["buildItems.ts BUILD_ITEM_ICON", tsSide.iconKeys]].filter((x) => !x[1]).map((x) => x[0]);
    fail(`${missing.join(", ")} missing or not literal`);
    return;
  }
  const set = (a) => [...new Set(a)].sort().join(",");
  const tableSet = set(ids);
  const lowerCamel = (k) => k.charAt(0).toLowerCase() + k.slice(1);
  if (tsSide.items.length !== rows.length) fail(`buildItems.ts BUILD_ITEMS has ${tsSide.items.length} row(s), ${at} BUILD-ITEMS ${rows.length}`);
  for (let i = 0; i < Math.min(rows.length, tsSide.items.length); i++) {
    const c = rows[i], s = tsSide.items[i];
    const diffs = [];
    if (s.id !== c.id) diffs.push(`id "${s.id}" vs "${c.id}"`);
    if (s.kind !== lowerCamel(c.kind)) diffs.push(`kind "${s.kind}" vs "${lowerCamel(c.kind)}"`);
    if (s.liftType !== c.liftType) diffs.push(`liftType "${s.liftType}" vs "${c.liftType}"`);
    if (s.runKind !== c.runKind) diffs.push(`runKind ${s.runKind} vs ${c.runKind}`);
    if (diffs.length) fail(`buildItems.ts BUILD_ITEMS[${i}] differs from ${at}:${c.line}: ${diffs.join(", ")}`);
  }
  if (set(tsSide.union) !== tableSet) fail(`bindings.ts BuildItemId {${set(tsSide.union)}} != the C# table's ids {${tableSet}} - an id only one side knows is dropped silently`);
  if (set(tsSide.ids) !== tableSet || tsSide.ids.length !== ids.length) fail(`bindings.ts BUILD_ITEM_IDS {${tsSide.ids.join(",")}} != the C# table's ids {${tableSet}}`);
  if (set([...tsSide.prefab.keys()]) !== tableSet) fail(`bindings.ts BUILD_ITEM_PREFAB keys {${set([...tsSide.prefab.keys()])}} != the C# table's ids {${tableSet}}`);
  if (set(tsSide.iconKeys) !== tableSet || tsSide.iconKeys.length !== ids.length) fail(`buildItems.ts BUILD_ITEM_ICON keys {${set(tsSide.iconKeys)}} != the C# table's ids {${tableSet}} (every item needs its icon)`);
  for (const id of ids) {
    const p = tsSide.prefab.get(id);
    if (p !== undefined && prefabOf.has(id) && p !== prefabOf.get(id)) fail(`build id "${id}": the C# row's prefab "${prefabOf.get(id)}" != bindings.ts BUILD_ITEM_PREFAB "${p}"`);
  }
  const grouped = tsSide.groups.flat();
  if (grouped.length !== new Set(grouped).size || set(grouped) !== tableSet) fail(`buildItems.ts BUILD_GROUPS {${grouped.join(",")}} must hold every build id exactly once`);
  if (set(tsSide.liftTypes) !== set([...types.keys()])) fail(`bindings.ts LIFT_TYPE_IDS {${set(tsSide.liftTypes)}} != the C# table's lift types {${set([...types.keys()])}}`);

  let expected = 0, pending = 0;
  for (const n of names.values()) { if (n.pending) pending++; else expected++; }
  report.push(`contract R16: ${rows.length} C# build item(s) (${at}) ${rows.length === tsSide.items.length ? "=" : "!="} ${tsSide.items.length} TS row(s); ` +
    `${names.size} NAMES (${expected} expected, ${pending} pending); ${types.size} lift type(s) with base/top/line; prefabs, kinds, run kinds, groups, icons checked`);
}

/**
 * Rule R17: enum parity, C# <-> TS. For each ENUM_PAIRS entry the TS const object (literal numbers, a leading "-" allowed) and the
 * C# enum (literal values or the implicit +1 rule) must hold the same names with the same values, both directions; a missing file,
 * enum or const object FAILS too. A TS-only or C#-only member would make the UI misread a code silently.
 */
function checkEnumParity(tsSide, errors, report) {
  let n = 0;
  const cache = new Map();
  for (const [tsName, file, csName] of ENUM_PAIRS) {
    const tsMap = tsSide.consts.get(tsName);
    if (!cache.has(file)) {
      let src = null;
      try { src = stripCsComments(fs.readFileSync(path.join(CS_DIR, ...file.split("/")), "utf8")); } catch { /* reported below */ }
      cache.set(file, src);
    }
    const src = cache.get(file);
    const csMap = src ? csEnum(src, csName) : null;
    if (!tsMap) { errors.push(`contract: bindings.ts has no literal const object ${tsName} (rule R17)`); continue; }
    if (src === null) { errors.push(`contract: cannot read ${file} under ${CS_DIR} - enum ${csName} cannot be compared (rule R17)`); continue; }
    if (!csMap) { errors.push(`contract: ${file} has no enum ${csName} with literal values (rule R17)`); continue; }
    for (const [k, v] of tsMap) if (csMap.get(k) !== v) errors.push(`contract: ${tsName}.${k} = ${v} in TS but ${csName}.${k} = ${csMap.get(k)} in ${file} (rule R17)`);
    for (const [k, v] of csMap) if (!tsMap.has(k)) errors.push(`contract: ${csName}.${k} = ${v} in ${file} has no ${tsName}.${k} in bindings.ts (rule R17)`);
    n++;
  }
  report.push(`contract R17: ${n} enum pair(s) compared`);
}

/**
 * Rule R18: the UI rules self-test. tools/test-hub-rules.js compiles src/bindings.ts and src/hub/hubRules.ts (of --src= when
 * given) in Node and checks its literal oracles; each failure (or a crash: an impure import, a syntax error) FAILS the gate.
 */
function checkHubRules(errors, report) {
  let r;
  try {
    r = require("./test-hub-rules").run(SRC);
  } catch (e) {
    errors.push(`src/hub/hubRules.ts: test-hub-rules crashed: ${e && e.message ? e.message : e} (rule R18)`);
    report.push("contract R18: test-hub-rules CRASHED");
    return;
  }
  for (const f of r.failures) errors.push(`src/hub/hubRules.ts: ${f} (rule R18)`);
  report.push(`contract R18: test-hub-rules ${r.checks} check(s), failures=${r.failures.length}`);
}

/** `npm run contract:lock`: rewrite tools/contract.lock (refuses a CORE break or a change without a bump) */
function writeLock() {
  // The lock records the TS side only (bindings.ts); C# parity is the gate's business (R1/R2)
  const errors = [];
  for (const o of OVERRIDES) console.warn(`contract:lock: WARNING path override in use: ${o}`);
  let ts;
  try { ts = require("typescript"); } catch { console.error("contract:lock: REFUSED - typescript is not installed (run npm ci)"); return 1; }
  const snap = readBindingsTs(ts, path.join(SRC, BINDINGS_TS), errors);
  if (!snap || errors.length) {
    for (const e of errors) console.error(`contract:lock: ERROR ${e}`);
    console.error("contract:lock: REFUSED - src/bindings.ts could not be read as a contract");
    return 1;
  }
  let old = null;
  try { old = parseLock(fs.readFileSync(CONTRACT_LOCK, "utf8")); } catch { /* first lock */ }
  if (old) {
    const v = coreViolations(snap, old);
    if (v.length) {
      for (const x of v) console.error(`contract:lock: ERROR ${x}`);
      console.error("contract:lock: REFUSED - CORE is frozen since contract 1 (spec 2.2). To break CORE on purpose, delete the lock by hand and say why in the change.");
      return 1;
    }
    const diff = factDiff(old.facts, snapFacts(snap));
    const changed = diff.removed.length + diff.added.length > 0;
    if (changed && old.contract === snap.contract) {
      console.error(`contract:lock: REFUSED - the contract changed but HUB_CONTRACT is still ${snap.contract}: bump it in bindings.ts and C# first. Changes: ${diff.text}`);
      return 1;
    }
    console.log(`contract:lock: contract ${old.contract} -> ${snap.contract}; ${diff.removed.length} fact(s) removed, ${diff.added.length} added${changed ? `: ${diff.text}` : ""}`);
  } else {
    console.warn(`contract:lock: WARNING no previous lock at ${CONTRACT_LOCK} - CORE could not be checked against an earlier contract`);
  }
  fs.writeFileSync(CONTRACT_LOCK, lockText(snap));
  console.log(`contract:lock: wrote ${CONTRACT_LOCK} (contract ${snap.contract}, ${snapFacts(snap).length} fact(s))`);
  return 0;
}


function universalStub(label) {
  const fn = function () { return null; };
  return new Proxy(fn, {
    get(t, p) { if (p === Symbol.toPrimitive) return () => label; if (p === "then") return undefined; return universalStub(`${label}.${String(p)}`); },
    apply() { return null; },
    construct() { return {}; },
  });
}

async function smokeTest(mjsPath, errors, warnings) {
  const React = require("react");
  const log = { bindings: [], triggersAtImport: [], appended: [], otherCalls: [] };
  const valueStub = (v) => ({ value: v, subscribe: () => ({ value: v, setChangeListener() {}, dispose() {} }), dispose() {}, update() {} });
  const api = {
    bindValue(group, name, fallback) {
      log.bindings.push({ group, name, hasFallback: fallback !== undefined });
      return valueStub(fallback);
    },
    bindLocalValue: (v) => valueStub(v),
    bindTrigger: () => () => {},
    bindTriggerWithArgs: () => () => {},
    bindEvent: () => ({ subscribe: () => ({ dispose() {} }), dispose() {} }),
    bindMap: () => ({ getValue: () => undefined, subscribe: () => ({ value: undefined, setChangeListener() {}, dispose() {} }), dispose() {} }),
    trigger: (group, name) => { log.triggersAtImport.push(`${group}.${name}`); },
    call: () => Promise.resolve(undefined),
  };
  const win = {
    React, ReactDOM: universalStub("ReactDOM"), ReactDOMClient: universalStub("ReactDOMClient"),
    "cs2/api": new Proxy(api, { get: (t, p) => (p in t ? t[p] : universalStub(`cs2/api.${String(p)}`)) }),
    "cs2/bindings": universalStub("cs2/bindings"), "cs2/l10n": universalStub("cs2/l10n"), "cs2/ui": universalStub("cs2/ui"),
    "cs2/utils": universalStub("cs2/utils"), "cs2/input": universalStub("cs2/input"), "cs2/modding": universalStub("cs2/modding"),
    "cohtml/cohtml": universalStub("cohtml"), "chart.js": universalStub("chart.js"),
  };
  const hadWindow = Object.prototype.hasOwnProperty.call(globalThis, "window");
  const prevWindow = globalThis.window;
  globalThis.window = win;
  let mod;
  try {
    mod = await import(pathToFileURL(mjsPath).href + `?smoke=${Date.now()}`);
  } catch (e) {
    errors.push(`smoke: importing the bundle threw: ${e && e.stack ? e.stack.split("\n")[0] : e}`);
    return null;
  } finally {
    if (hadWindow) globalThis.window = prevWindow; else delete globalThis.window;
  }
  if (typeof mod.default !== "function") errors.push(`smoke: default export is ${typeof mod.default}, not the registrar function`);
  if (mod.hasCSS !== true) errors.push(`smoke: hasCSS export is ${JSON.stringify(mod.hasCSS)}, must be true (paired with ${MOD.id}.css)`);
  const extra = Object.keys(mod).filter((k) => k !== "default" && k !== "hasCSS");
  if (extra.length) warnings.push(`smoke: unexpected named exports ${extra.join(", ")} (the game reads only default and hasCSS)`);
  for (const b of log.bindings) if (!b.hasFallback) errors.push(`smoke: bindValue("${b.group}", "${b.name}") has no fallback - it throws in game when C# lacks the binding`);
  if (log.triggersAtImport.length) errors.push(`smoke: trigger() during module import: ${log.triggersAtImport.join(", ")}`);

  if (typeof mod.default === "function") {
    const registry = new Proxy({}, {
      get(t, p) {
        if (p === "append") return (target, component) => { log.appended.push({ target, kind: typeof component, component }); };
        if (p === "hasAppend") return () => false;
        if (p === "then") return undefined;
        return (...args) => { log.otherCalls.push(`${String(p)}(${args.map((a) => typeof a).join(",")})`); };
      },
    });
    try { mod.default(registry); } catch (e) { errors.push(`smoke: the registrar threw: ${e}`); }
    for (const c of log.otherCalls) errors.push(`smoke: the registrar called moduleRegistry.${c} - append only`);
    for (const a of log.appended) {
      if (!ALLOWED_HOOKS.includes(a.target)) errors.push(`smoke: append to "${a.target}" - allowed hooks: ${ALLOWED_HOOKS.join(", ")}`);
      if (a.kind !== "function") errors.push(`smoke: append("${a.target}", <${a.kind}>) - must be a plain function component`);
    }
    if (!log.appended.length) errors.push("smoke: the registrar appended nothing");

    // Containment (header item 7). The registrar's own console.error lines are expected here and only counted.
    const savedConsoleError = console.error;
    let quietLines = 0;
    console.error = () => { quietLines++; };
    try {
      // (a) every append() throws, as the game's does when the ModdingHook path moved
      const thrown = "Module game-ui/modding/modding-hook.tsx was not found.";
      const throwing = new Proxy({}, {
        get(t, p) { if (p === "then") return undefined; return () => { throw thrown; }; },
      });
      try {
        mod.default(throwing);
        log.containedThrowingAppend = true;
      } catch (e) {
        errors.push(`smoke: with a throwing moduleRegistry.append the registrar let "${e}" escape - in game every ` +
          "later mod's registrar would be skipped (index.js@800454: no try/catch). Wrap every append in try/catch");
      }
      // (b) each appended wrapper, called as a plain function, must return an error boundary element
      log.boundaries = 0;
      for (const a of log.appended) {
        if (a.kind !== "function") continue;
        let el;
        try {
          el = a.component({});
        } catch (e) {
          errors.push(`smoke: the component appended at "${a.target}" threw when called as a plain function ` +
            `(${e && e.message ? e.message : e}) - appended wrappers must stay hook-free and only return an ErrorBoundary`);
          continue;
        }
        const type = el && typeof el === "object" ? el.type : undefined;
        const isBoundary = typeof type === "function" && (typeof type.getDerivedStateFromError === "function" ||
          (type.prototype !== undefined && type.prototype !== null && typeof type.prototype.componentDidCatch === "function"));
        if (isBoundary) log.boundaries++;
        else {
          const got = typeof type === "function" ? `<${type.name || "anonymous"}>` : typeof type === "string" ? `<${type}>` : String(type);
          errors.push(`smoke: the component appended at "${a.target}" renders ${got} at its root, not an error boundary - ` +
            "a render error below it would unmount the game's whole UI (research 2.A.1). Wrap it in <ErrorBoundary>");
        }
      }
    } finally {
      console.error = savedConsoleError;
    }
    log.quietConsoleLines = quietLines;
  }
  return log;
}


/**
 * Runs every check on buildDir. Never leaves a stale pass behind: it deletes build/.gate-pass first, so a FAIL, an exception
 * (CRASH) or a killed process leaves no marker, and writes it (= the stamp) only on PASS. Resolves { ok, errors, warnings,
 * report, stamp }.
 */
async function checkBundle({ quiet = false, buildDir = BUILD } = {}) {
  const errors = [], warnings = [], report = [];
  const mjs = path.join(buildDir, `${MOD.id}.mjs`);
  const css = path.join(buildDir, `${MOD.id}.css`);
  let stamp = null;
  removeGateMarker(buildDir);
  if (readGateMarker(buildDir) !== null) errors.push(`build/${GATE_FILE} cannot be deleted - delete it by hand (a stale marker would let a rejected bundle deploy)`);

  if (!fs.existsSync(buildDir)) { errors.push(`build folder missing: ${buildDir}`); return finish(); }
  const files = listFiles(buildDir);
  for (const f of files) if (!isAllowedBuildFile(f)) errors.push(`build/${f}: unexpected output file (exactly one .mjs + .css may ship; .json/.map/.js are forbidden)`);
  if (!fs.existsSync(mjs)) errors.push(`build/${MOD.id}.mjs missing`);
  if (!fs.existsSync(css)) errors.push(`build/${MOD.id}.css missing - with hasCSS = true the game waits for it forever (blank UI)`);
  try { stamp = fs.readFileSync(path.join(buildDir, STAMP_FILE), "utf8").trim(); } catch { errors.push(`build/${STAMP_FILE} missing (written by webpack.config.js)`); }
  if (stamp !== null && !STAMP_RE.test(stamp)) errors.push(`build/${STAMP_FILE}: "${stamp}" is not a valid stamp`);
  if (errors.length && (!fs.existsSync(mjs) || !fs.existsSync(css))) return finish();

  const bundle = fs.readFileSync(mjs, "utf8");
  const info = parseModuleInfo(bundle);
  if (!info.identifier) errors.push("banner: no 'Cities: Skylines II UI Module' line (terser writes the banner only when it extracted a comment)");
  if (info.id !== MOD.id) errors.push(`banner: Id is ${JSON.stringify(info.id)}, expected "${MOD.id}" (null = the game registers the module without an id)`);
  if (info.deps === null) errors.push("banner: no Dependencies line - UIModuleAsset.PostCreate would call AddTags(null) and throw");
  if (!info.author) errors.push("banner: no Author line");
  if (info.version === null) errors.push(`banner: Version ${JSON.stringify(info.versionRaw)} does not parse as System.Version`);
  if (stamp && info.extra.Build !== stamp) errors.push(`banner: Build ${JSON.stringify(info.extra.Build)} differs from build/${STAMP_FILE} "${stamp}"`);
  // The banner must not hinge on a third-party license comment (src/index.tsx header)
  const licence = path.join(buildDir, `${MOD.id}.mjs.LICENSE.txt`);
  const licenceText = fs.existsSync(licence) ? fs.readFileSync(licence, "utf8") : "";
  if (!licenceText.includes(BANNER_ANCHOR)) errors.push(`banner: our "/*! ${BANNER_ANCHOR} ..." comment (src/index.tsx, on hasCSS) was not extracted - the banner now depends on a third-party license comment`);

  if (stamp && !bundle.includes(JSON.stringify(stamp))) errors.push(`bundle: the stamp "${stamp}" is not compiled in (DefinePlugin __BUILD_STAMP__)`);
  if (/\bimport\(/.test(bundle)) errors.push("bundle: contains dynamic import() - must be ONE module");
  if (/game-ui\//.test(bundle)) errors.push("bundle: references an internal game-ui/ module path - append hooks only");
  if (/\.(extend|override)\(/.test(bundle)) errors.push("bundle: contains .extend( or .override( - append hooks only");
  if (/["'](select|option|table)["']/.test(bundle)) errors.push("bundle: renders a select/option/table element - unsupported by Gameface");
  if (/display:"(inline-)?grid"|"space-evenly"|margin(Left|Right):"auto"/.test(bundle)) errors.push("bundle: inline style uses grid, space-evenly or an auto margin");

  const cssText = fs.readFileSync(css, "utf8");
  if (!cssText.trim()) warnings.push(`${MOD.id}.css is empty (allowed, but src/index.tsx imports base.module.scss to keep it non-empty)`);
  const selectorCount = lintCss(cssText, errors);

  const runtime = readRuntimeExports();
  if (runtime.problem) {
    errors.push(`runtime exports: CSII_INSTALLATIONPATH is set but the installed game's export table could not be checked: ` +
      `${runtime.problem}. The patch-day check (research 2.A.5 step 4) would otherwise fall back silently to the ` +
      "1.6.2f1 lists. Fix the install path, or, if a game patch changed index.js's shape, update readRuntimeExports() " +
      "in tools/check-bundle.js");
  }
  for (const d of runtime.drift) warnings.push(`runtime exports: ${d}`);
  const srcCount = lintSources(runtime, errors);
  const locCount = lintLocale(errors, warnings);

  checkContract(errors, warnings, report);

  const smoke = errors.length ? null : await smokeTest(mjs, errors, warnings);

  const kb = (p) => (fs.existsSync(p) ? (fs.statSync(p).size / 1024).toFixed(1) + " KB" : "-");
  report.push(`bundle ${MOD.id}.mjs ${kb(mjs)}, ${MOD.id}.css ${kb(css)}, license ${kb(path.join(buildDir, `${MOD.id}.mjs.LICENSE.txt`))}, stamp ${stamp}`);
  report.push(`banner Id=${info.id} Author=${info.author} Version=${info.versionRaw} Dependencies=[${(info.deps || []).join(",")}] Build=${info.extra.Build}`);
  report.push(`css: ${selectorCount} selector(s) checked for scoping under an .sr- class of ours`);
  report.push(`sources checked: ${srcCount} TS file(s), ${locCount} SkiResort.Hub.* locale value(s); runtime exports from ${runtime.source}`);
  if (smoke) {
    report.push(`smoke: appended ${smoke.appended.map((a) => a.target).join(", ")}; ${smoke.bindings.length} bindValue call(s) at import, all with fallbacks`);
    report.push(`smoke containment: a throwing append() ${smoke.containedThrowingAppend ? "stayed inside" : "ESCAPED"} the registrar ` +
      `(${smoke.quietConsoleLines} console.error line(s) muted); ${smoke.boundaries}/${smoke.appended.length} appended component(s) render an error boundary`);
  }
  return finish();

  function finish() {
    let ok = errors.length === 0;
    // A run on substituted files (fault tests) proves nothing about the real ones: it never marks the bundle deployable (the
    // marker was deleted when this run started).
    const overridden = OVERRIDES.length > 0;
    if (ok && !overridden) {
      try {
        fs.writeFileSync(path.join(buildDir, GATE_FILE), `${stamp}\n`);
        if (readGateMarker(buildDir) !== stamp) throw new Error("read-back differs");
        report.push(`gate marker build/${GATE_FILE} = ${stamp}`);
      } catch (e) {
        errors.push(`cannot write build/${GATE_FILE} (${e && e.message ? e.message : e}) - without it the C# build refuses the bundle`);
        removeGateMarker(buildDir);
        ok = false;
      }
    }
    if (!quiet) {
      for (const r of report) console.log(`check-bundle: ${r}`);
      for (const w of warnings) console.warn(`check-bundle: WARNING ${w}`);
      for (const e of errors) console.error(`check-bundle: ERROR ${e}`);
      console.log(!ok ? `check-bundle: FAIL (${errors.length} error(s)) - nothing may be deployed; build/${GATE_FILE} is absent`
        : overridden ? `check-bundle: PASS WITH PATH OVERRIDES - build/${GATE_FILE} NOT written; nothing may be deployed`
          : "check-bundle: PASS");
    }
    return { ok, errors, warnings, report, stamp };
  }
}

/**
 * `--contract-only`: the source lint, the locale glyph lint and rules R1-R7 / R10 without a bundle (no build/ needed; the gate
 * marker is not touched). For quick checks and fault tests.
 */
function checkContractOnly() {
  const errors = [], warnings = [], report = [];
  const runtime = readRuntimeExports();
  if (runtime.problem) errors.push(`runtime exports: ${runtime.problem}`);
  const srcCount = lintSources(runtime, errors);
  const locCount = lintLocale(errors, warnings);
  checkContract(errors, warnings, report);
  report.unshift(`contract-only: ${srcCount} TS file(s) linted, ${locCount} SkiResort.Hub.* locale value(s) glyph-checked; runtime exports from ${runtime.source}`);
  for (const r of report) console.log(`check-bundle: ${r}`);
  for (const w of warnings) console.warn(`check-bundle: WARNING ${w}`);
  for (const e of errors) console.error(`check-bundle: ERROR ${e}`);
  console.log(errors.length ? `check-bundle: CONTRACT FAIL (${errors.length} error(s))` : "check-bundle: CONTRACT PASS");
  return errors.length ? 1 : 0;
}

module.exports = { checkBundle, bundleFiles, parseModuleInfo, readGateMarker, removeGateMarker, STAMP_FILE, GATE_FILE, ALLOWED_HOOKS };

if (require.main === module && ARGS.includes("--write-lock")) {
  process.exit(writeLock());
} else if (require.main === module && ARGS.includes("--contract-only")) {
  process.exit(checkContractOnly());
} else if (require.main === module) {
  checkBundle().then((r) => process.exit(r.ok ? 0 : 1), (e) => {
    removeGateMarker();
    console.error(`check-bundle: CRASHED (build/${GATE_FILE} is absent, nothing may be deployed) ${e && e.stack ? e.stack : e}`);
    process.exit(1);
  });
}
