# index.tsx: how the game loads the UI bundle and why the registrar must never throw

`index.tsx` is the registrar, the only code the game calls directly.

## How the game loads a UI mod (installed 1.6.2f1 `Game/UI/index.js`)

- The game `import()`s `SkiResortMod.mjs`, keeps `default` as the registrar and reads `hasCSS` (truthy: it loads `SkiResortMod.css` and
  waits for it, `index.js@2306554`).
- It then calls every mod's registrar in ONE loop with no try/catch (`index.js@800454`): a throwing registrar skips every later mod at
  boot and blanks the whole UI on a live reload.

## Contract of the registrar

- The default export is a registrar that NEVER throws: each append in its own try/catch, the whole body in another; failures go to the
  console and are queued for C# (`src/hub/report.ts`).
- Append-only, on the game's documented hooks (`tools/check-bundle.js` fails the build otherwise).
- Every appended component is a PLAIN FUNCTION wrapping an `ErrorBoundary`: `append(target, c)` takes the anchor form only when
  `typeof c === "function"`; a `React.memo` / `forwardRef` object would be treated as a module path and throw (`index.js@213139`).
- `hasCSS = true` is written by hand and `base.module.scss` is always imported, so `SkiResortMod.css` always exists.
- `append`'s position argument is ignored for the anchor form (`index.js@213139`), so none is passed.

## Near `hasCSS`: the `/*!` banner comment

The `/*!` comment on `hasCSS` is load-bearing: terser-webpack-plugin writes the manifest banner only when it extracted at least one
comment, so this keeps the banner independent of react/jsx-runtime's license comment. It sits on a statement that is always emitted (a
comment on the type-only import would be dropped with the import). `tools/check-bundle.js` checks that it reached
`SkiResortMod.mjs.LICENSE.txt`. Do not write the at-sign words license/preserve in src comments: terser extracts those too.
