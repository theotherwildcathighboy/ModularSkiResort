/*
 * src/cs2shim.ts - typed access to game runtime exports whose shipped declarations hide them.
 *
 * l10n.d.ts declares both `export interface LocalizedNumber {...}` and `export { LocalizedNumber$1 as LocalizedNumber }`
 * (the component). skipLibCheck hides the duplicate and TypeScript resolves the name to the interface, so
 * `import { LocalizedNumber } from "cs2/l10n"` fails with TS1444/TS2693. Same for LocalizedString, LocalizedBounds,
 * LocalizedFraction (cs2/l10n) and DropdownItem (cs2/ui). All five are runtime exports; tools/check-bundle.js re-reads the
 * export tables on every build.
 *
 * Import these five from here, never from cs2/*. This is the only file allowed a namespace import of cs2/*:
 * tools/check-bundle.js checks every `<namespace>.<Name>` access in it against the runtime export table.
 */
import * as l10n from "cs2/l10n";
import * as ui from "cs2/ui";
import type { PropsWithChildren } from "react";
import type {
  LocComponent, LocalizedBoundsProps, LocalizedFractionProps, LocalizedNumberProps, LocalizedStringProps,
} from "cs2/l10n";
import type { DropdownItemProps } from "cs2/ui";

type Values = Record<string, unknown>;

/* <LocalizedNumber value={m} unit={Unit.Length} /> - Unit comes from cs2/l10n. */
export const LocalizedNumber = (l10n as unknown as Values).LocalizedNumber as LocComponent<LocalizedNumberProps>;
/* <LocalizedString id fallback args={{ X: "..." }} /> - the only one that expands {X:Length}-style placeholders. */
export const LocalizedString = (l10n as unknown as Values).LocalizedString as LocComponent<LocalizedStringProps>;
export const LocalizedBounds = (l10n as unknown as Values).LocalizedBounds as LocComponent<LocalizedBoundsProps>;
export const LocalizedFraction = (l10n as unknown as Values).LocalizedFraction as LocComponent<LocalizedFractionProps>;
export const DropdownItem = (ui as unknown as Values).DropdownItem as
  <T>(props: PropsWithChildren<DropdownItemProps<T>>) => JSX.Element;
