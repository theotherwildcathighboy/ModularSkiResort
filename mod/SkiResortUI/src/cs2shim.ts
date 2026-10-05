/*
 * src/cs2shim.ts - typed access to game runtime exports whose SHIPPED declarations hide them.
 *
 * Written for this repo (MIT). Created 2026-10-01 (Round U1, implementer U1-A).
 * The defect (game typings 1.6.2f1, synced into types/): l10n.d.ts declares BOTH
 * `export interface LocalizedNumber {...}` (the C# LocalizedNumber data shape) and
 * `export { LocalizedNumber$1 as LocalizedNumber }` (the component). That duplicate export is
 * hidden by skipLibCheck, and TypeScript resolves the name to the INTERFACE, so
 * `import { LocalizedNumber } from "cs2/l10n"` fails with TS1444/TS2693 ("only refers to a type").
 * The same holds for LocalizedString, LocalizedBounds and LocalizedFraction (cs2/l10n) and for
 * DropdownItem (cs2/ui). All five ARE runtime exports of the game (export tables at
 * index.js@192007 and @197852 of the installed 1.6.2f1 bundle; tools/check-bundle.js re-reads
 * them on every build).
 *
 * Import these five from HERE, never from cs2/*. This is the ONLY file allowed a namespace import
 * of cs2/*: tools/check-bundle.js checks every `<namespace>.<Name>` access in it against the
 * game's runtime export table, exactly like a named import elsewhere.
 */
import * as l10n from "cs2/l10n";
import * as ui from "cs2/ui";
import type { PropsWithChildren } from "react";
import type {
  LocComponent, LocalizedBoundsProps, LocalizedFractionProps, LocalizedNumberProps, LocalizedStringProps,
} from "cs2/l10n";
import type { DropdownItemProps } from "cs2/ui";

type Values = Record<string, unknown>;

/** <LocalizedNumber value={m} unit={Unit.Length} /> - Unit comes from cs2/l10n. */
export const LocalizedNumber = (l10n as unknown as Values).LocalizedNumber as LocComponent<LocalizedNumberProps>;
/** <LocalizedString id fallback args={{ X: "..." }} /> - the only one that expands {X:Length}-style placeholders. */
export const LocalizedString = (l10n as unknown as Values).LocalizedString as LocComponent<LocalizedStringProps>;
export const LocalizedBounds = (l10n as unknown as Values).LocalizedBounds as LocComponent<LocalizedBoundsProps>;
export const LocalizedFraction = (l10n as unknown as Values).LocalizedFraction as LocComponent<LocalizedFractionProps>;
/** Item of a cs2/ui Dropdown. */
export const DropdownItem = (ui as unknown as Values).DropdownItem as
  <T>(props: PropsWithChildren<DropdownItemProps<T>>) => JSX.Element;
