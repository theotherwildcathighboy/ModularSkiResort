/*
 * src/hub/parts.tsx - small building blocks shared by the hub sections.
 *
 * Engine facts relied on (installed 1.6.2f1 Cities2_Data/Content/Game/UI/index.js / index.css):
 *   - cs2/ui PanelSectionRow (@731753) renders its `left` / `right` slots only when the value is truthy. A bare number
 *     0 would vanish: always pass an element (<Num/>) or a non-empty string.
 *   - cs2/l10n LocalizedNumber is `Fc` (@192159), a LocComponent (@434250 createLocComponent). Its renderString
 *     (@503029) is the unit formatter (table @503780: Length m < 1,000 m else km with 1 decimal, yd < 1,609 m else mi;
 *     Height whole m / ft; Area m^2 / ft^2 below 100,000 m^2 else km^2 / acres; Percentage whole %) with the first
 *     digit-space-digit turned into a no-break space. The decimal helper `Xc` (@508570) prints one decimal below 100
 *     and strips a trailing ".0" ($c, @508453), so "99.9 km" and "1.6 km" have different shapes.
 *   - cs2/l10n useLocalization (@433200) returns an object memoised on the localization context (dictionary + unit
 *     settings), so useNumberSamples recomputes only when the language or the unit system changes.
 *   - cs2/l10n LocalizedString (`fc`, @435324) translates (id, fallback) and then substitutes {NAME} placeholders
 *     (@510010), also in the fallback text, so a key the dictionary lacks still shows its arguments. A JSX arg with
 *     renderString (e.g. <LocalizedNumber/>) is rendered to a string first (@433704 renderLocElement).
 *   - Vanilla's stable-width idiom: FullWidthDigits renders each number twice, the second copy with every digit
 *     replaced by "0" in a `.blind` child (index.js@1007800; index.css@127211): the cell is as wide as the widest digit
 *     string and never jitters. StableWidth / NumCell generalize it to several sample strings.
 *   - cs2/ui Tooltip (@564782) attaches listeners to its single child through a ref; use it for every hint (a Button's
 *     own `tooltipLabel` shows only while the content overflows).
 *   - Masked glyphs: vanilla's TintedIcon is a div with `mask-image: url(<src>)` and the icon color as background
 *     (index.js@633525; index.css@28104). Glyph does the same with our own class, so the color is ours (warning, lock)
 *     and not --iconColor.
 */
import { isValidElement, useMemo, useState } from "react";
import type { ReactNode } from "react";
import { Tooltip } from "cs2/ui";
import { Unit, useLocalization } from "cs2/l10n";
import { LocalizedNumber, LocalizedString } from "../cs2shim";
import { BAND } from "../bindings";
import type { LocEntry } from "../l10n";
import { GLYPH_INFO } from "./hubRules";
import styles from "./hub.module.scss";

/** True while vanilla's unit setting is US customary (UnitSystem.Freedom = 1). Snow depth is the one unit vanilla has no
 *  format for, so the hub shows whole inches then (SNOW_DEPTH_IN / STATUS_SNOW_IN), else cm. */
export function useInches(): boolean {
  const loc = useLocalization();
  return !!loc && !!loc.unitSettings && loc.unitSettings.unitSystem === 1;
}

/** The snow depth number in the shown unit: cm, or whole inches (1 in = 2.54 cm) when `inches` */
export function depthValue(cm: number, inches: boolean): number {
  return inches ? Math.round(cm / 2.54) : cm;
}

/** A number with its unit; "–" for a value that is not finite (never "NaN m"). */
export function Num({ value, unit, signed }: { value: number; unit: Unit; signed?: boolean }): JSX.Element {
  if (typeof value !== "number" || !Number.isFinite(value)) return <span>{"–"}</span>;
  return <LocalizedNumber value={value} unit={unit} signed={signed} />;
}

/** "–" for the -1 "unknown" convention of the contract, else <Num> */
export function NumOrDash({ value, unit, signed }: { value: number; unit: Unit; signed?: boolean }): JSX.Element {
  if (typeof value !== "number" || !Number.isFinite(value) || value < 0 && !signed) return <span>{"–"}</span>;
  return <Num value={value} unit={unit} signed={signed} />;
}

/** A placeholder entry ("... {N} ...") rendered through LocalizedString with its arguments.
 *  The only way to render an entry of HUB_FMT (src/l10n.ts header; gate rule 5). */
export function LocText({ entry, args }: { entry: LocEntry; args: Record<string, string | JSX.Element> }): JSX.Element {
  return <LocalizedString id={entry[0]} fallback={entry[1]} args={args} />;
}

/** Join class names, skipping false/undefined (the gate allows only react) */
export function cx(...names: Array<string | false | null | undefined>): string {
  let out = "";
  for (const n of names) if (n) out = out ? `${out} ${n}` : n;
  return out;
}

/** A prefab icon (assetdb:// URL). If the image fails to load, a neutral CSS square is drawn instead. */
export function AssetIcon({ src, className }: { src: string; className: string }): JSX.Element {
  const [failed, setFailed] = useState(false);
  if (failed) return <div className={cx(className, styles.iconFallback)} />;
  return <img src={src} className={className} onError={() => setFailed(true)} />;
}

/** A game glyph (Media/Glyphs/*.svg) drawn as a mask, colored by `className`'s background. */
export function Glyph({ src, className }: { src: string; className?: string }): JSX.Element {
  return <div className={cx(styles.glyph, className)} style={{ maskImage: `url(${src})` }} />;
}

/**
 * Wraps `child` in a cs2/ui Tooltip when `tip` is set. The child must be ONE host element (<div>) or a cs2/ui component,
 * not one of our own function components: the Tooltip attaches its mouse listeners through DOMNodeModifier (installed
 * index.js@529099 `Su`), which clones a host element with a merged ref (DOMNodeProvider `ju`) but hands a function
 * component the ref only through DOMNodeContext, which our components do not read (a memo component gets the ref by
 * cloneElement and drops it). So a component that needs a tooltip takes a `tip` prop and puts the Tip around its own
 * root div (InfoLine, SubLine, FlexBar).
 */
export function Tip({ tip, children }: { tip?: ReactNode; children: JSX.Element }): JSX.Element {
  if (tip === undefined || tip === null || tip === "" || tip === false) return children;
  return <Tooltip tooltip={tip}>{children}</Tooltip>;
}

/** One tooltip line: a plain text, ONE element (a <LocText>: one text node), or a label (a string or ONE <LocText>) with a value */
export type TipLine = string | JSX.Element | { label: string | JSX.Element; value: ReactNode } | false | null | undefined;

function isPair(l: string | JSX.Element | { label: string | JSX.Element; value: ReactNode }): l is { label: string | JSX.Element; value: ReactNode } {
  return typeof l === "object" && l !== null && !isValidElement(l) && "label" in l;
}

/** Multi-line tooltip body: the first line is the title; empty entries are skipped. */
export function TipLines({ lines }: { lines: ReadonlyArray<TipLine> }): JSX.Element {
  const shown = lines.filter((l) => !!l) as Array<string | JSX.Element | { label: string | JSX.Element; value: ReactNode }>;
  return (
    <div className={styles.tipLines}>
      {shown.map((line, i) => {
        const cls = i === 0 ? styles.tipTitle : styles.tipLine;
        if (!isPair(line)) return <div key={i} className={cls}>{line}</div>;
        return (
          <div key={i} className={cx(cls, styles.tipPair)}>
            <div className={styles.tipLabel}>{line.label}</div>
            <div className={styles.tipValue}>{line.value}</div>
          </div>
        );
      })}
    </div>
  );
}

/**
 * A cell as wide as the widest of `samples` (plus its own content), so nothing beside it moves when the content
 * changes. The samples are blind children (height 0, hidden, nowrap, no pointer events). `align` = where the visible
 * content sits.
 */
export function StableWidth({ samples, align, className, children }: {
  /** Strings, or one-text-node elements such as a <LocText> ("Month of {MONTH}" for every month) */
  samples: readonly ReactNode[] | null;
  align?: "start" | "center" | "end";
  className?: string;
  children: ReactNode;
}): JSX.Element {
  const alignCls = align === "end" ? styles.stableEnd : align === "center" ? styles.stableCenter : styles.stableStart;
  return (
    <div className={cx(styles.stable, alignCls, className)}>
      <div className={styles.stableValue}>{children}</div>
      {samples && samples.map((s, i) => <div key={i} className={styles.blind}>{s}</div>)}
    </div>
  );
}

/** The width samples of the number columns. ok=false: renderString threw, and the cells fall back to em min-widths. */
export interface NumberSamples {
  ok: boolean;
  length: readonly string[];
  height: readonly string[];
  area: readonly string[];
  percent: readonly string[];
  /** Two-digit counts (legend). */
  count2: readonly string[];
  /** Signed money up to 6 digits (Guests / Money columns) */
  money: readonly string[];
  /** Counts up to 5 digits (Guests columns, guest bars) */
  count: readonly string[];
}

const NO_SAMPLES: NumberSamples = { ok: false, length: [], height: [], area: [], percent: [], count2: ["00"], money: ["-000,000"], count: ["00,000"] };

/** Digits -> "0" (vanilla FullWidthDigits: '0' is taken as the widest digit; unverified for Overpass / Noto Sans) */
function zeroDigits(s: string): string {
  return s.replace(/[0-9]/g, "0");
}

/**
 * `LocalizedNumber.renderString(loc, {value, unit})` of the widest plausible values, digits zeroed, memoised on the
 * localization context. Samples: length 999 m, 1,608 m (the yard/mile switch) and 99,900 m ("99.9 km": 99,000 would
 * print "99 km"); height 999 and 2,999 m (real drops are below 3 km); area 99,999 m^2 and 9,900,000 m^2; percent 199.
 */
export function useNumberSamples(): NumberSamples {
  const loc = useLocalization();
  return useMemo(() => {
    try {
      const render = LocalizedNumber.renderString;
      if (typeof render !== "function") return NO_SAMPLES;
      const r = (value: number, unit: Unit, signed?: boolean): string => zeroDigits(String(render(loc, { value, unit, signed })));
      const uniq = (xs: string[]): string[] => xs.filter((x, i) => xs.indexOf(x) === i);
      return {
        ok: true,
        length: uniq([r(999, Unit.Length), r(1608, Unit.Length), r(99900, Unit.Length)]),
        height: uniq([r(999, Unit.Height), r(2999, Unit.Height)]),
        area: uniq([r(99999, Unit.Area), r(9900000, Unit.Area)]),
        percent: [r(199, Unit.Percentage)],
        count2: ["00"],
        money: uniq([r(-999999, Unit.Money, true), r(999999, Unit.Money, true)]),
        count: [r(99999, Unit.Integer)],
      };
    } catch {
      return NO_SAMPLES;
    }
  }, [loc]);
}

export type SampleKind = "length" | "height" | "area";

/**
 * A right-aligned number cell of a list column. Every cell of a column, rows and header label alike, carries the same
 * blinds, so all cells resolve to the same width and the columns line up.
 */
export function NumCell({ samples, kind, fallbackClass, className, children }: {
  samples: NumberSamples;
  kind: SampleKind;
  /** The em min-width class, used only when samples.ok is false */
  fallbackClass: string;
  className?: string;
  children: ReactNode;
}): JSX.Element {
  return (
    <div className={cx(styles.num, !samples.ok && fallbackClass, className)}>
      <StableWidth samples={samples.ok ? samples[kind] : null} align="end">{children}</StableWidth>
    </div>
  );
}

/**
 * Difficulty marker: CSS shapes in em, outlines with border longhands. Flat = hollow gray circle, green circle, blue
 * square, black diamond (rotated square with a light outline), double black = two diamonds, pending (-1) = hollow at
 * 0.4 opacity, cannot be rated (-2) = a hollow circle in the warning color.
 */
export function BandMarker({ band }: { band: number }): JSX.Element {
  let inner: JSX.Element;
  switch (band) {
    case BAND.Flat: inner = <div className={cx(styles.bandShape, styles.bandFlat)} />; break;
    case BAND.Green: inner = <div className={cx(styles.bandShape, styles.bandGreen)} />; break;
    case BAND.Blue: inner = <div className={cx(styles.bandShape, styles.bandBlue)} />; break;
    case BAND.Black: inner = <div className={cx(styles.bandShape, styles.bandBlack)} />; break;
    case BAND.DoubleBlack:
      inner = (
        <>
          <div className={cx(styles.bandShape, styles.bandBlack, styles.bandSmall)} />
          <div className={cx(styles.bandShape, styles.bandBlack, styles.bandSmall, styles.bandSecond)} />
        </>
      );
      break;
    case BAND.Unrateable: inner = <div className={cx(styles.bandShape, styles.bandUnrateable)} />; break;
    default: inner = <div className={cx(styles.bandShape, styles.bandPending)} />; break;
  }
  return <div className={styles.bandMarker}>{inner}</div>;
}


/**
 * A number as a <LocText> ARGUMENT. LocalizedString renders an argument through renderLocElement (installed
 * index.js@433704 `mc`): a LocComponent (LocalizedNumber) becomes its string, a plain string stays, and any other
 * element prints "[Name/]", so never pass <Num/> as an argument. Non-finite, or negative when `minusIsUnknown` -> "–".
 * `signed` is applied only to a non-zero value: the game prints a signed 0 as "±" (index.js@509409 `ed`).
 */
export function numArg(value: number, unit: Unit, signed?: boolean, minusIsUnknown = true): JSX.Element | string {
  if (typeof value !== "number" || !Number.isFinite(value) || (minusIsUnknown && value < 0 && !signed)) return "–";
  return <LocalizedNumber value={value} unit={unit} signed={!!signed && value !== 0} />;
}

/** A display number (not an argument): like Num, with the signed-zero rule of numArg */
export function SignedNum({ value, unit }: { value: number; unit: Unit }): JSX.Element {
  if (typeof value !== "number" || !Number.isFinite(value)) return <span>{"–"}</span>;
  return <LocalizedNumber value={value} unit={unit} signed={value !== 0} />;
}

/** The "≈" / "…" slot before a value (fixed width; empty keeps its width) */
export function MarkSlot({ mark }: { mark: "≈" | "…" | null }): JSX.Element {
  return <div className={styles.markSlot}>{mark ?? ""}</div>;
}

/** One label / value row with the vanilla InfoRow metrics (28.2 / 39.7 / 47.3 rem at 100 / 130 / 150 %) */
export function InfoLine({ label, value, tip, mark, header, sub, info, flag, flagDim, op }: {
  label: string | JSX.Element;
  value?: ReactNode;
  tip?: ReactNode;
  /** Render the "≈" slot (null = an empty slot of the same width; undefined = no slot) */
  mark?: "≈" | "…" | null;
  header?: boolean;
  sub?: boolean;
  /** A dim Info glyph right after the label text (not at the row end, where it would push the value column); the row's
   *  `tip` carries the notes. The label is then a flex row [text (shrinks, ellipsis) | glyph | rest], never inline-block
   *  (unproven in Gameface). */
  info?: boolean;
  /** A row whose 0 has a cause says it after the label: the problem band's Warning glyph + a short warning-colored word
   *  ("No ski school"), instead of a tooltip sentence. Same flex box as `info`. */
  flag?: string;
  /** The flag is a state, not a fault ("No run counts yet"): the dim Info glyph and dim text */
  flagDim?: boolean;
  /** The receipt term's operator ("+", "×") in a fixed-width cell before the label, so the terms read as the equation they
   *  are. Symbols only (nothing to translate). */
  op?: string;
}): JSX.Element {
  return (
    <Tip tip={tip}>
      <div className={cx(styles.infoLine, header && styles.infoHeader, sub && styles.infoSub)}>
        {op !== undefined && <div className={styles.infoOp}>{op}</div>}
        {flag ? (
          <div className={styles.infoLabelBox}>
            <div className={styles.infoLabelText}>{label}</div>
            {flagDim
              ? <Glyph src={GLYPH_INFO} className={cx(styles.infoMark, styles.glyphDimTone)} />
              : <Glyph src="Media/Glyphs/Warning.svg" className={cx(styles.infoMark, styles.glyphWarnTone)} />}
            <div className={cx(styles.flagText, flagDim && styles.flagTextDim)}>{flag}</div>
          </div>
        ) : info ? (
          <div className={styles.infoLabelBox}>
            <div className={styles.infoLabelText}>{label}</div>
            <Glyph src={GLYPH_INFO} className={cx(styles.infoMark, styles.glyphDimTone)} />
          </div>
        ) : (
          <div className={styles.infoLabel}>{label}</div>
        )}
        {mark !== undefined && <MarkSlot mark={mark} />}
        {value !== undefined && <div className={styles.infoValue}>{value}</div>}
      </div>
    </Tip>
  );
}

/** A dim XS sub-line under a receipt row (one text node: a string or one <LocText>) */
export function SubLine({ children, tip }: { children: string | JSX.Element; tip?: ReactNode }): JSX.Element {
  return (
    <Tip tip={tip}>
      <div className={styles.subLine}>{children}</div>
    </Tip>
  );
}

export function Divider(): JSX.Element {
  return <div className={styles.divider} />;
}

/** Run-type stripe (natural = two stacked bars, the dashed outline's echo) */
export function KindStripe({ kind }: { kind: number }): JSX.Element {
  if (kind === 3) {
    return (
      <div className={cx(styles.runMarker, styles.kindNatural)}>
        <div className={styles.kindNaturalBar} />
        <div className={cx(styles.kindNaturalBar, styles.kindNaturalBarNext)} />
      </div>
    );
  }
  const cls = kind === 0 ? styles.kindGroomed : kind === 1 ? styles.kindSnowmaking : kind === 2 ? styles.kindNight
    : kind === 4 ? styles.kindCatTrack : styles.kindUnknown;
  return <div className={cx(styles.runMarker, cls)} />;
}

/** A bar of integer flex-grow parts (the proven difficulty-bar technique); 0 parts are not drawn.
 *  `tip` is the bar's tooltip, attached to its own root div (see Tip). */
export function FlexBar({ parts, className, tip }: {
  parts: ReadonlyArray<{ grow: number; className: string }>;
  className: string;
  tip?: ReactNode;
}): JSX.Element {
  return (
    <Tip tip={tip}>
      <div className={className}>
        {parts.map((p, i) => (p.grow > 0 ? <div key={i} className={cx(styles.barPart, p.className)} style={{ flexGrow: Math.round(p.grow) }} /> : null))}
      </div>
    </Tip>
  );
}

export function CheckBox({ on }: { on: boolean }): JSX.Element {
  return (
    <div className={cx(styles.checkBox, on && styles.checkBoxOn)}>
      {on && <Glyph src="Media/Glyphs/Checkmark.svg" className={styles.checkGlyph} />}
    </div>
  );
}

/** A game notification icon (<img>); on a load error the masked Warning glyph */
export function NotifIcon({ src, className }: { src: string; className: string }): JSX.Element {
  const [failed, setFailed] = useState(false);
  if (failed) return <Glyph src="Media/Glyphs/Warning.svg" className={cx(className, styles.glyphWarnTone)} />;
  return <img src={src} className={className} onError={() => setFailed(true)} />;
}
