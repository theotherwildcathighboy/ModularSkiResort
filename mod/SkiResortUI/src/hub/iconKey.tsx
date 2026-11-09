/*
 * src/hub/iconKey.tsx - one icon image, and the tooltip key (icon scheme, DECISIONS 620).
 * IconImg draws one hub icon (hubIcons.ts) as a fixed-size <img>, or its word when the NAME is unknown or the image fails.
 * The tooltip key is the legend at the point of use: under every tooltip that shows icon images, one dim row per three icons, each an
 * icon | word pair. parts.tsx Tip adds it, so no call site can forget it. It reads the tooltip's element tree as it is passed (IconText
 * elements in `lines`, `parts`, `children`), so it costs no extra render and no late relayout. The header's Info glyph keeps the full
 * table (IconLegend in iconText.tsx).
 * This file imports nothing from parts.tsx (parts.tsx imports it).
 */
import { isValidElement, useState } from "react";
import type { ReactNode } from "react";
import { useLocalization } from "cs2/l10n";
import type { Localization } from "cs2/l10n";
import { iconKeyBudgetEm, iconKeyFit, iconKeyPairEm, iconKeyRows } from "./hubRules";
import type { IconKeySource } from "./hubRules";
import { HUB_ICON_GROUPS, hubIconOf, iconMode } from "./hubIcons";
import { reportUiError } from "./report";
import styles from "./hub.module.scss";

const cx = (...xs: Array<string | false | null | undefined>): string => xs.filter(Boolean).join(" ");

/** An icon's image source and box, or null: unknown NAME. A NAME with "/" is a vanilla Media/ path, as in vanilla text. */
export function iconSource(name: string): { src: string; wide: boolean } | null {
  const ic = hubIconOf(name);
  if (ic) return { src: ic.src, wide: ic.wide };
  if (name.startsWith("Media/")) return { src: name, wide: false };
  return null;
}

/** An icon's word: SkiResort.Hub.ICON[NAME], a vanilla path's file name, or the NAME itself (reported once). */
export function wordOf(loc: Localization, name: string): string {
  const ic = hubIconOf(name);
  if (ic) return (loc && loc.translate(ic.word[0], ic.word[1])) ?? ic.word[1];
  if (name.startsWith("Media/")) return name.slice(name.lastIndexOf("/") + 1).replace(/\.svg$/i, "").toLowerCase();
  reportUiError("IconText", `unknown icon name "${name}" (its word form shows instead)`);
  return name;
}

/** One icon as a fixed-size <img>. On a load error, or for an unknown NAME, its word. `inline` = inside the cohinline paragraph. */
export function IconImg({ name, word, gap, inline, lead }: { name: string; word: string; gap?: boolean; inline?: boolean; lead?: boolean }): JSX.Element {
  const [failed, setFailed] = useState(false);
  const source = iconSource(name);
  if (failed || !source) {
    return inline ? <>{word}</> : <div className={cx(styles.iconLineText, gap && styles.iconGap, lead && styles.iconMarkLead)}>{word}</div>;
  }
  return (
    <img src={source.src}
      className={cx(styles.iconImg, source.wide && styles.iconImgWide, gap && styles.iconGap, lead && styles.iconMarkLead)}
      onError={() => setFailed(true)} />
  );
}

/** The legend order of the key (HUB_ICON_GROUPS, flattened). */
const KEY_ORDER: readonly string[] = HUB_ICON_GROUPS.reduce<string[]>((a, g) => a.concat(g.names), []);

/** Marks a component whose elements carry an icon text: the key reads `entry` (a LocEntry) and `line` from its props. */
export interface IconKeyAware { iconKeySource: true }

function collect(node: unknown, out: IconKeySource[], depth: number): void {
  if (depth > 10 || node === null || node === undefined || typeof node !== "object") return;
  if (Array.isArray(node)) { for (const n of node) collect(n, out, depth + 1); return; }
  if (isValidElement(node)) {
    const props = (node.props ?? {}) as Record<string, unknown>;
    const type = node.type as unknown as Partial<IconKeyAware> | string;
    if (typeof type !== "string" && type && type.iconKeySource === true) {
      const entry = props.entry as readonly [string, string] | undefined;
      if (entry && typeof entry[1] === "string") out.push({ text: entry[1], line: !!props.line });
    }
    for (const k of Object.keys(props)) if (k !== "entry" && k !== "args") collect(props[k], out, depth + 1);
    return;
  }
  // A TipLines pair { label, value }
  const o = node as Record<string, unknown>;
  if ("label" in o || "value" in o) { collect(o.label, out, depth + 1); collect(o.value, out, depth + 1); }
}

/** The key rows for a tooltip body: [] when it shows no icon image. */
export function iconKeyOf(tip: ReactNode): string[][] {
  const sources: IconKeySource[] = [];
  collect(tip, sources, 0);
  return sources.length ? iconKeyRows(sources, iconMode(), KEY_ORDER) : [];
}

/** The game's text scale: the root's --fontScale (the inline value applyTextScale writes, else the computed one). A style read, not a
 *  size read, so it does not lag a drawn frame. 1 when unset. */
export function textScale(): number {
  const root = document.documentElement;
  const v = parseFloat(root.style.getPropertyValue("--fontScale").trim() || getComputedStyle(root).getPropertyValue("--fontScale").trim());
  return Number.isFinite(v) && v > 0 ? v : 1;
}

/**
 * The key under a tooltip: dim icon | word pairs, at most three per row. Its root declares the hub tokens (class tipLines).
 * Width budget (round 624): it mounts with the tooltip, so it packs the rows once, at mount, for the current text scale (hubRules
 * iconKeyFit, an estimate from the font's glyph widths): a long pair moves to the next row before the row passes the balloon edge.
 */
export function IconKey({ rows }: { rows: readonly string[][] }): JSX.Element {
  const loc = useLocalization();
  const scale = textScale();
  const fitted = iconKeyFit(rows, (n) => iconKeyPairEm(wordOf(loc, n), !!iconSource(n)?.wide, scale), iconKeyBudgetEm(scale));
  return (
    <div className={cx(styles.tipLines, styles.iconKey)}>
      {fitted.map((r, i) => (
        <div key={i} className={styles.iconLine}>
          {r.map((n, j) => (
            <div key={n} className={cx(styles.iconKeyPair, j > 0 && styles.iconKeyGap)}>
              <IconImg name={n} word={wordOf(loc, n)} />
              <div className={cx(styles.iconLineText, styles.iconGap)}>{wordOf(loc, n)}</div>
            </div>
          ))}
        </div>
      ))}
    </div>
  );
}
