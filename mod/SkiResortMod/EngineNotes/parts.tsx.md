# parts.tsx: engine facts of the cs2/ui and cs2/l10n bundle that the shared hub building blocks rely on

Facts about the installed 1.6.2f1 `Cities2_Data/Content/Game/UI/index.js` / `index.css` that `src/hub/parts.tsx` depends on.

- `cs2/ui` `PanelSectionRow` (`@731753`) renders its `left` / `right` slots only when the value is truthy. A bare number 0 would vanish.
  Always pass an element (`<Num/>`) or a non-empty string.
- `cs2/l10n` `LocalizedNumber` is `Fc` (`@192159`), a LocComponent (`@434250` `createLocComponent`). Its `renderString` (`@503029`) is the
  unit formatter. It turns the first digit-space-digit into a no-break space. The formatter table (`@503780`) has these units:
  - Length: m < 1,000 m else km with 1 decimal, yd < 1,609 m else mi.
  - Height: whole m / ft.
  - Area: m^2 / ft^2 below 100,000 m^2 else km^2 / acres.
  - Percentage: whole %.

  The decimal helper `Xc` (`@508570`) prints one decimal below 100 and strips a trailing ".0" (`$c`, `@508453`), so "99.9 km" and
  "1.6 km" have different shapes.
- `cs2/l10n` `useLocalization` (`@433200`) returns an object memoized on the localization context (dictionary + unit settings), so
  `useNumberSamples` recomputes only when the language or the unit system changes.
- `cs2/l10n` `LocalizedString` (`fc`, `@435324`) translates (id, fallback) and then substitutes `{NAME}` placeholders (`@510010`), also in
  the fallback text, so a key the dictionary lacks still shows its arguments. It renders a JSX arg with `renderString` (e.g.
  `<LocalizedNumber/>`) to a string first (`@433704` `renderLocElement`).
- Vanilla's stable-width idiom: `FullWidthDigits` renders each number twice. The second copy has every digit replaced by "0" in a
  `.blind` child (`index.js@1007800`, `index.css@127211`). So the cell is as wide as the widest digit string and never jitters.
  `StableWidth` / `NumCell` generalize it to several sample strings.
- `cs2/ui` `Tooltip` (`@564782`) attaches listeners to its single child through a ref. Use it for every hint (a Button's own
  `tooltipLabel` shows only while the content overflows).
- Masked glyphs: vanilla's `TintedIcon` is a div with `mask-image: url(<src>)` and the icon color as background (`index.js@633525`,
  `index.css@28104`). `Glyph` does the same with our own class, so the color is ours (warning, lock) and not `--iconColor`.
