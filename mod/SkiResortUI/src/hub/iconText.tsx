/*
 * src/hub/iconText.tsx - renders hub text with icon tags (icon scheme, hub contract 40, DECISIONS 620).
 * Scope: icons only in structured lines (the receipt's missing lines, counts), defining marks, and lists of run ratings. Prose stays words.
 * Three layouts, by risk:
 * - line (IconText `line`, MissingLine, the legend rows): ONE flex row that never wraps: text boxes and fixed-size <img> items. Proven
 *   Gameface primitives only (the rows already mix <img> and text boxes).
 * - hanging mark (`<mark:NAME>` at the start of the text or of a sentence): a flex row [icon | text box that wraps]. No inline layout.
 * - paragraph with icons (`<icon:NAME>` inside a wrapping sentence): `<p cohinline>`, the vanilla FormattedText path (installed
 *   index.js@647337). Unproven inside a cs2/ui Tooltip portal: hubIcons.ts iconMode() switches it off (spike S0), and the sentence then shows
 *   its word form, which is today's text.
 * Glue rule: a tag and the next word or punctuation sit in one nowrap span, so an icon never starts a line alone.
 * Fallback to the word: an unknown NAME (reported once), an image that fails to load, iconMode "words", and every renderString use.
 * IconText carries renderString (the word form) and propsAreEqual, so it may be a <LocText> argument (check-bundle R19), and iconKeySource,
 * so the tooltip key (iconKey.tsx, added by parts.tsx Tip) finds it in a tooltip body.
 */
import { Fragment } from "react";
import { useLocalization } from "cs2/l10n";
import type { LocReactNode, Localization, LocStringRenderer, PropsAreEqual } from "cs2/l10n";
import { LocalizedString } from "../cs2shim";
import type { LocEntry, Translate } from "../l10n";
import { iconLineItems, iconPieces, iconRows, iconTokens, iconWordForm } from "./hubRules";
import type { IconToken } from "./hubRules";
import { HUB_ICON, HUB_ICON_GROUPS, iconMode } from "./hubIcons";
import { IconImg, wordOf } from "./iconKey";
import type { IconMode } from "./hubIcons";
import { HUB_LOC } from "./hubText";
import { TipLines, cx } from "./parts";
import styles from "./hub.module.scss";

export interface IconTextProps {
  /** A HUB_LOC entry, or a HUB_FMT entry with `args` (check-bundle R13 allows a placeholder entry here as in <LocText>). */
  entry: LocEntry;
  args?: Record<string, LocReactNode>;
  /** One structured row that never wraps (missing lines, counts). Default: a paragraph that wraps. */
  line?: boolean;
  /** The missing-line tone (caution color). */
  missing?: boolean;
}

/** The resolved text with its tags (arguments filled). */
function tagForm(loc: Localization, p: IconTextProps): string {
  if (p.args) return String(LocalizedString.renderString(loc, { id: p.entry[0], fallback: p.entry[1], args: p.args }));
  return (loc && loc.translate(p.entry[0], p.entry[1])) ?? p.entry[1];
}

function wordsOf(tokens: readonly IconToken[], word: (name: string) => string): string {
  let s = "";
  for (const tk of tokens) s += tk.kind === "text" ? tk.text : tk.kind === "icon" ? word(tk.name) : "";
  return s;
}

/** A wrapping paragraph of tokens: plain text, or with icons the cohinline path ("inline" mode only, else its word form). */
function Para({ tokens, mode, word }: { tokens: readonly IconToken[]; mode: IconMode; word: (name: string) => string }): JSX.Element {
  if (mode !== "inline" || !tokens.some((tk) => tk.kind !== "text")) return <>{wordsOf(tokens, word)}</>;
  return (
    <p cohinline="cohinline" className={styles.iconPara}>
      {iconPieces(tokens).map((pc, i) => pc.kind === "text"
        ? <Fragment key={i}>{pc.text}</Fragment>
        : (
          <span key={i} className={styles.iconGlue}>
            {pc.items.map((it, j) => it.kind === "text"
              ? <Fragment key={j}>{it.text}</Fragment>
              : <IconImg key={j} name={it.name} word={word(it.name)} inline />)}
          </span>
        ))}
    </p>
  );
}

function IconTextView(p: IconTextProps): JSX.Element {
  const loc = useLocalization();
  const mode = iconMode();
  const s = tagForm(loc, p);
  const word = (name: string): string => wordOf(loc, name);
  const tokens = iconTokens(s);
  const tagged = tokens.some((tk) => tk.kind !== "text");
  if (!tagged || mode === "words") {
    const text = tagged ? iconWordForm(s, word) : s;
    return p.line || p.missing ? <div className={cx(styles.iconLine, p.missing && styles.iconMissing)}><div className={styles.iconLineText}>{text}</div></div> : <>{text}</>;
  }
  if (p.line) {
    return (
      <div className={cx(styles.iconLine, p.missing && styles.iconMissing)}>
        {iconLineItems(tokens).map((it, i) => it.kind === "text"
          ? <div key={i} className={cx(styles.iconLineText, it.gap && styles.iconGap)}>{it.text}</div>
          : <IconImg key={i} name={it.name} word={word(it.name)} gap={it.gap} />)}
      </div>
    );
  }
  const rows = iconRows(tokens);
  if (rows.length === 1 && rows[0].mark === null) return <Para tokens={rows[0].tokens} mode={mode} word={word} />;
  return (
    <div className={cx(styles.iconRows, p.missing && styles.iconMissing)}>
      {rows.map((r, i) => r.mark === null
        ? <div key={i} className={styles.iconMarkText}><Para tokens={r.tokens} mode={mode} word={word} /></div>
        : (
          <div key={i} className={styles.iconMarkRow}>
            <IconImg name={r.mark} word="" lead />
            <div className={styles.iconMarkText}><Para tokens={r.tokens} mode={mode} word={word} /></div>
          </div>
        ))}
    </div>
  );
}

/** The word form: what a string slot, a <LocText> argument and the logs get. */
const renderIconText: LocStringRenderer<IconTextProps> = (loc, p) => iconWordForm(tagForm(loc, p), (name) => wordOf(loc, name));
const iconTextPropsEqual: PropsAreEqual<IconTextProps> = (a, b) =>
  a.entry === b.entry && a.line === b.line && a.missing === b.missing &&
  LocalizedString.propsAreEqual({ id: a.entry[0], fallback: a.entry[1], args: a.args }, { id: b.entry[0], fallback: b.entry[1], args: b.args });

/** A hub text that may hold icon tags. The ONLY way to render an entry with a tag (check-bundle R-icon): t() would print the tag. */
export const IconText = Object.assign(IconTextView, { renderString: renderIconText, propsAreEqual: iconTextPropsEqual, iconKeySource: true as const });

/**
 * A missing line made of parts: the head ("Missing:", "Not in full:") and its parts joined by " · ", in the caution color, on one row.
 * Each part is an <IconText line> (a part with a number is a HUB_FMT entry). Parts left out are simply not passed (zero counts).
 */
export function MissingLine({ head, parts }: { head: LocEntry; parts: ReadonlyArray<JSX.Element | false | null> }): JSX.Element {
  const shown = parts.filter((x): x is JSX.Element => !!x);
  return (
    <div className={cx(styles.iconLine, styles.iconMissing)}>
      <IconText entry={head} line />
      {shown.map((part, i) => (
        <Fragment key={i}>
          {i > 0 && <div className={cx(styles.iconLineText, styles.iconGap)}>{"·"}</div>}
          <div className={cx(styles.iconLine, styles.iconGap)}>{part}</div>
        </Fragment>
      ))}
    </div>
  );
}

/** The header legend (tooltip of the Info button after the area name): the whole symbol table, icon | word, by group. */
export function IconLegend({ t }: { t: Translate }): JSX.Element {
  return (
    <TipLines lines={[
      t(HUB_LOC.iconLegend),
      ...HUB_ICON_GROUPS.map((g) => (
        <div key={g.title[0]} className={styles.iconLegendGroup}>
          <div className={styles.iconLegendTitle}>{t(g.title)}</div>
          {g.names.map((n) => (
            <div key={n} className={styles.iconLine}>
              <div className={styles.iconLegendCell}><IconImg name={n} word={t(HUB_ICON[n].word)} /></div>
              <div className={styles.iconLineText}>{t(HUB_ICON[n].word)}</div>
            </div>
          ))}
        </div>
      )),
    ]} />
  );
}
