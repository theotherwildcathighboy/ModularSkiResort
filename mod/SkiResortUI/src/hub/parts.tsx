/*
 * src/hub/parts.tsx - small building blocks shared by the hub sections.
 * Relies on engine facts of the installed 1.6.2f1 UI bundle (Cities2_Data/Content/Game/UI/index.js / index.css):
 * - PanelSectionRow renders its `left` / `right` slots only when truthy. Never pass a bare 0: pass an element or a non-empty string.
 * - LocalizedNumber is the unit formatter.
 * - LocalizedString substitutes {NAME} placeholders also in the fallback.
 * - The stable-width idiom is vanilla's FullWidthDigits.
 * - Use Tooltip, not a Button's `tooltipLabel`, for every hint.
 * - Glyphs are masked.
 * Engine notes: EngineNotes/parts.tsx.md
 */
import { isValidElement, useMemo, useState } from "react";
import type { ReactNode } from "react";
import { useValue } from "cs2/api";
import { Tooltip } from "cs2/ui";
import { Unit, useLocalization } from "cs2/l10n";
import type { LocStringRenderer, Localization, LocalizedNumberProps, LocalizedStringProps, PropsAreEqual } from "cs2/l10n";
import { LocalizedNumber, LocalizedString } from "../cs2shim";
import { displayUnits$, gradeScheme$ } from "../bindings";
import type { LocEntry } from "../l10n";
import { GLYPH_INFO, unitNbsp } from "./hubRules";
import { EU_TEXT, HUB_FMT } from "./hubText";
import { areaHa, bandShape, euClassShape, imperialMiles, imperialYards, isEuropean, schemeEntry, slopeValue, speed, unitSettingsFor, unpackUnits } from "./units";
import type { DisplayUnits } from "./units";
import { IconKey, iconKeyOf } from "./iconKey";
import styles from "./hub.module.scss";

/** The resolved unit and slope choices (Options > Lengths and elevation, Weather, Slope; binding displayUnits, units.ts unpackUnits).
 *  Re-renders the caller when one changes. Snow depth has no vanilla format: depthImperial picks whole inches (SNOW_DEPTH_IN). */
export function useUnits(): DisplayUnits {
  return unpackUnits(useValue(displayUnits$));
}

/** The localization vanilla's number formatter reads for one unit: the game's, with the unit system (lengths, heights, areas) or the
 *  temperature unit of the mod's Options (units.ts unitSettingsFor). Vanilla formats by loc.unitSettings (installed index.js unit
 *  table reads the unit system and the temperature unit from its loc argument), so a derived object with only unitSettings replaced keeps
 *  every other lookup (Object.create: the prototype is the game's object). Reads the binding's current value (render time, like
 *  stringProps); cached per localization and choice. */
const unitLocCache = new WeakMap<Localization, Map<string, Localization>>();
export function unitLoc(loc: Localization, unit: string): Localization {
  const game = loc && loc.unitSettings;
  if (!game) return loc;
  const settings = unitSettingsFor(game, unit, unpackUnits(displayUnits$.value));
  if (settings === game) return loc;
  const key = settings.unitSystem + "|" + settings.temperatureUnit;
  let byKey = unitLocCache.get(loc);
  if (!byKey) unitLocCache.set(loc, (byKey = new Map()));
  let out = byKey.get(key);
  if (!out) {
    out = Object.create(loc) as Localization;
    Object.defineProperty(out, "unitSettings", { value: settings });
    byKey.set(key, out);
  }
  return out;
}

/** The snow depth number in the shown unit: cm, or whole inches (1 in = 2.54 cm) when `inches` */
export function depthValue(cm: number, inches: boolean): number {
  return inches ? Math.round(cm / 2.54) : cm;
}

/** Vanilla's mile text (Common.VALUE_MILE, its {SIGN} as installed index.js eu: "-", or "+" when signed; never 0 here), for the imperial lengths
 *  from 500 yd that vanilla would still show in yards (units.ts imperialMiles, T215). */
function mileText(loc: Localization, miles: number, signed?: boolean): string {
  const sign = miles < 0 ? "-" : signed ? "+" : "";
  const value = LocalizedNumber.renderString(loc, { value: Math.abs(miles), unit: Unit.FloatSingleFraction });
  return LocalizedString.renderString(loc, { id: "Common.VALUE_MILE", fallback: "{SIGN}{VALUE} mi", args: { SIGN: sign, VALUE: value } });
}

/** Vanilla's yard text (Common.VALUE_YARD, the same {SIGN} rule as mileText) with whole yards, for the imperial lengths under 500 yd
 *  (units.ts imperialYards: vanilla keeps one decimal there). */
function yardText(loc: Localization, yards: number, signed?: boolean): string {
  const sign = yards < 0 ? "-" : signed && yards > 0 ? "+" : "";
  const value = LocalizedNumber.renderString(loc, { value: Math.abs(yards), unit: Unit.Integer });
  return LocalizedString.renderString(loc, { id: "Common.VALUE_YARD", fallback: "{SIGN}{VALUE} yd", args: { SIGN: sign, VALUE: value } });
}

/** THE number render of the hub: vanilla's formatter on unitLoc, except the imperial lengths: whole yards under 500 yd (yardText) and
 *  miles from 500 yd (mileText). */
function renderNumber(loc: Localization, p: LocalizedNumberProps): string {
  if (p.unit === Unit.Length && typeof p.value === "number") {
    const imperial = unpackUnits(displayUnits$.value).lengthImperial;
    const miles = imperialMiles(p.value, imperial);
    if (Number.isFinite(miles)) return mileText(loc, miles, p.signed);
    const yards = imperialYards(p.value, imperial);
    if (Number.isFinite(yards)) return yardText(loc, yards, p.signed);
  }
  return LocalizedNumber.renderString(unitLoc(loc, p.unit as string), p);
}

/** LocalizedNumber's text with the number-unit space as U+00A0 (hubRules unitNbsp): vanilla writes "376 m" with an ASCII space. A
 *  LocComponent renders exactly its renderString in a fragment (installed index.js@434221 createLocComponent), so this is the same
 *  text node with one character changed. */
function NumText({ value, unit, signed }: { value: number; unit: Unit; signed?: boolean }): JSX.Element {
  const loc = useLocalization();
  useValue(displayUnits$);   // re-render when a unit choice changes (unitLoc reads it)
  return <>{unitNbsp(renderNumber(loc, { value, unit, signed }))}</>;
}

/** LocalizedNumber in the mod's unit choices: the same renderString on unitLoc. A LocComponent, so it may be a <LocText> argument. */
const renderUnitNumber: LocStringRenderer<LocalizedNumberProps> = (loc, p) => renderNumber(loc, p);
const unitNumberPropsEqual: PropsAreEqual<LocalizedNumberProps> = (a, b) => LocalizedNumber.propsAreEqual(a, b);
function UnitNumberView(p: LocalizedNumberProps): JSX.Element {
  const loc = useLocalization();
  useValue(displayUnits$);
  return <>{renderUnitNumber(loc, p)}</>;
}
const UnitNumber = Object.assign(UnitNumberView, { renderString: renderUnitNumber, propsAreEqual: unitNumberPropsEqual });

/** A number with its unit. A value that is not finite shows "–" (never "NaN m"). */
export function Num({ value, unit, signed }: { value: number; unit: Unit; signed?: boolean }): JSX.Element {
  if (typeof value !== "number" || !Number.isFinite(value)) return <span>{"–"}</span>;
  return <NumText value={value} unit={unit} signed={signed} />;
}

type LocTextProps = { entry: LocEntry; args: Record<string, string | JSX.Element> };

/** The run grading (T162) reads the binding's current value here (renderString runs outside a component); LocTextView subscribes. */
function stringProps(p: LocTextProps): LocalizedStringProps {
  const e = schemeEntry(p.entry, gradeScheme$.value, EU_TEXT);
  return { id: e[0], fallback: e[1], args: p.args };
}

/** LocalizedString's own text (its component renders exactly its renderString, index.js@434221), with every number kept on one line
 *  with its unit (hubRules unitNbsp, render time only: the locale string stays ASCII). */
function LocTextView(p: LocTextProps): JSX.Element {
  const loc = useLocalization();
  useValue(gradeScheme$);   // re-render when the run grading changes (stringProps reads it)
  useValue(displayUnits$);   // and when a unit choice changes (its number arguments read it, unitLoc)
  return <>{renderLocText(loc, p)}</>;
}

const renderLocText: LocStringRenderer<LocTextProps> = (loc, p) => unitNbsp(LocalizedString.renderString(loc, stringProps(p)));
const locTextPropsEqual: PropsAreEqual<LocTextProps> = (a, b) => LocalizedString.propsAreEqual(stringProps(a), stringProps(b));

/** A placeholder entry ("... {N} ...") rendered through LocalizedString with its arguments.
 *  The only way to render an entry of HUB_FMT (src/l10n.ts header, gate rule 5). It may also be an argument of another LocText.
 *  The game turns an element argument into text through its type's renderString (index.js renderLocElement). Any other
 *  component prints "[name/]" (the Guests view showed "Car spaces at [_i/]" for a numbered lodge). So LocText carries
 *  LocalizedString's renderString and propsAreEqual. */
export const LocText = Object.assign(LocTextView, { renderString: renderLocText, propsAreEqual: locTextPropsEqual });

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
 * not one of our own function components. Reason: the Tooltip attaches its mouse listeners through DOMNodeModifier (installed
 * index.js@529099 `Su`). DOMNodeModifier clones a host element with a merged ref (DOMNodeProvider `ju`). It hands a function
 * component the ref only through DOMNodeContext, which our components do not read. A memo component gets the ref by
 * cloneElement and drops it. So a component that needs a tooltip takes a `tip` prop and puts the Tip around its own
 * root div (InfoLine, SubLine, FlexBar).
 * A body that shows icon images gets the icon key under it (iconKey.tsx, DECISIONS 620): the legend at the point of use.
 */
export function Tip({ tip, children }: { tip?: ReactNode; children: JSX.Element }): JSX.Element {
  if (tip === undefined || tip === null || tip === "" || tip === false) return children;
  const keyRows = iconKeyOf(tip);
  const body = keyRows.length ? <div className={styles.tipStack}>{tip}<IconKey rows={keyRows} /></div> : tip;
  return <Tooltip tooltip={body}>{children}</Tooltip>;
}

/** One tooltip line: a plain text, ONE element (a <LocText>: one text node), or a label (a string or ONE <LocText>) with a value */
export type TipLine = string | JSX.Element | { label: string | JSX.Element; value: ReactNode } | false | null | undefined;

function isPair(l: string | JSX.Element | { label: string | JSX.Element; value: ReactNode }): l is { label: string | JSX.Element; value: ReactNode } {
  return typeof l === "object" && l !== null && !isValidElement(l) && "label" in l;
}

/** Multi-line tooltip body. The first line is the title. Empty entries are skipped. */
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
 * changes. The samples are blind children (height 0, hidden, nowrap, no pointer events). `align` is where the visible
 * content sits.
 */
export function StableWidth({ samples, align, className, children }: {
  /** Strings, or one-text-node elements such as a <LocText> ("{MONTH}: {N}" for every month) */
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

/** The width samples of the number columns. If ok=false, renderString threw, and the cells fall back to em min-widths. */
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

/** Digits -> "0" (vanilla FullWidthDigits takes '0' as the widest digit. This is unverified for Overpass / Noto Sans) */
function zeroDigits(s: string): string {
  return s.replace(/[0-9]/g, "0");
}

/**
 * Calls `LocalizedNumber.renderString(loc, {value, unit})` on the widest plausible values and zeroes the digits. The result
 * is memoized on the localization context. Samples:
 * - length: 457 m (the widest yards, 500 yd), 999 m, 1,608 m (vanilla's yard/mile switch) and 99,900 m ("99.9 km": 99,000 would print
 *   "99 km")
 * - height: 999 and 2,999 m (real drops are below 3 km)
 * - area: 99,999 m^2 and 9,900,000 m^2
 * - percent: 199
 */
export function useNumberSamples(): NumberSamples {
  const loc = useLocalization();
  const units = useValue(displayUnits$);   // the samples follow the unit choices (unitLoc)
  return useMemo(() => {
    try {
      if (typeof LocalizedNumber.renderString !== "function") return NO_SAMPLES;
      const r = (value: number, unit: Unit, signed?: boolean): string => zeroDigits(String(renderNumber(loc, { value, unit, signed })));
      const uniq = (xs: string[]): string[] => xs.filter((x, i) => xs.indexOf(x) === i);
      return {
        ok: true,
        length: uniq([r(457, Unit.Length), r(999, Unit.Length), r(1608, Unit.Length), r(99900, Unit.Length)]),
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
  }, [loc, units]);
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
 * 0.4 opacity, cannot be rated (-2) = a hollow circle in the warning color. European grading (T162 rev. 2, units.ts bandShape): green,
 * blue, red and black discs by the class of the band and the crux, and an orange diamond for the ski route.
 */
export function BandMarker({ band, gradePct, euClass }: {
  band: number;
  /** The run's crux, %: European grading splits the Green band into green and blue by it (units.ts euClassOf). Left out = both discs. */
  gradePct?: number;
  /** A European class to show as is (the legend), in place of the band. */
  euClass?: number;
}): JSX.Element {
  const scheme = useValue(gradeScheme$);
  let inner: JSX.Element;
  switch (euClass !== undefined && isEuropean(scheme) ? euClassShape(euClass) : bandShape(band, scheme, gradePct)) {
    case "flat": inner = <div className={cx(styles.bandShape, styles.bandFlat)} />; break;
    case "green": inner = <div className={cx(styles.bandShape, styles.bandGreen)} />; break;
    case "blue": inner = <div className={cx(styles.bandShape, styles.bandBlue)} />; break;
    case "black": inner = <div className={cx(styles.bandShape, styles.bandBlack)} />; break;
    case "dblack":
      inner = (
        <>
          <div className={cx(styles.bandShape, styles.bandBlack, styles.bandSmall)} />
          <div className={cx(styles.bandShape, styles.bandBlack, styles.bandSmall, styles.bandSecond)} />
        </>
      );
      break;
    case "euGreen": inner = <div className={cx(styles.bandShape, styles.bandEuGreen)} />; break;
    case "euBlue": inner = <div className={cx(styles.bandShape, styles.bandEuBlue)} />; break;
    case "euGreenBlue":
      inner = (
        <>
          <div className={cx(styles.bandShape, styles.bandEuGreen, styles.bandSmall)} />
          <div className={cx(styles.bandShape, styles.bandEuBlue, styles.bandSmall, styles.bandSecond)} />
        </>
      );
      break;
    case "euRed": inner = <div className={cx(styles.bandShape, styles.bandEuRed)} />; break;
    case "euBlack": inner = <div className={cx(styles.bandShape, styles.bandEuBlack)} />; break;
    case "euRoute": inner = <div className={cx(styles.bandShape, styles.bandEuRoute)} />; break;
    case "unrateable": inner = <div className={cx(styles.bandShape, styles.bandUnrateable)} />; break;
    default: inner = <div className={cx(styles.bandShape, styles.bandPending)} />; break;
  }
  return <div className={styles.bandMarker}>{inner}</div>;
}


/**
 * A number as a <LocText> ARGUMENT. LocalizedString renders an argument through renderLocElement (installed
 * index.js@433704 `mc`). A LocComponent (LocalizedNumber) becomes its string, a plain string stays, and any other
 * element prints "[Name/]". So never pass <Num/> as an argument. A value that is not finite, or is negative when
 * `minusIsUnknown`, gives "–". The function applies `signed` only to a non-zero value, because the game prints a signed 0
 * as "±" (index.js@509409 `ed`).
 */
export function numArg(value: number, unit: Unit, signed?: boolean, minusIsUnknown = true): JSX.Element | string {
  if (typeof value !== "number" || !Number.isFinite(value) || (minusIsUnknown && value < 0 && !signed)) return "–";
  return <UnitNumber value={value} unit={unit} signed={!!signed && value !== 0} />;
}

/** A percent grade (25 = 25 %) as the shown slope, a value or a <LocText> argument: degrees with one decimal (Unit.Angle), or whole
 *  percent grade (Unit.Percentage) under Options > Slope > Percent (T160). Unknown (negative) gives "–". */
export function slopeArg(pct: number, u: DisplayUnits): JSX.Element | string {
  return numArg(slopeValue(pct, u.slopePercent), u.slopePercent ? Unit.Percentage : Unit.Angle);
}

/** An area in hectares as a value or a <LocText> argument: "3.2 ha", or "7.9 acres" when `imperial` (DisplayUnits.lengthImperial,
 *  T161; vanilla's own area unit would show m² / ft² for a run). Unknown (negative, missing) gives "–". */
export function areaArg(ha: number | undefined, imperial: boolean): JSX.Element | string {
  const v = ha ?? -1;
  if (typeof v !== "number" || !Number.isFinite(v) || v < 0) return "–";
  const HA = numArg(areaHa(v, imperial), Unit.FloatSingleFraction);
  // T218: "1 acre", never "1 acres" (the number as shown, one decimal)
  const one = imperial && Math.round(areaHa(v, true) * 10) === 10;
  if (!imperial) return <LocText entry={HUB_FMT.areaHa} args={{ HA }} />;
  return one ? <LocText entry={HUB_FMT.areaAcre} args={{ HA }} /> : <LocText entry={HUB_FMT.areaAcres} args={{ HA }} />;
}

/** A lift's line speed as a <LocText> argument: "5 m/s", or "984 ft/min" when `imperial` (DisplayUnits.lengthImperial, T161; US lifts quote
 *  ft/min, vanilla has no speed unit). Not positive gives "–". */
export function speedArg(mps: number, imperial: boolean): JSX.Element | string {
  if (typeof mps !== "number" || !Number.isFinite(mps) || mps <= 0) return "–";
  const N = numArg(speed(mps, imperial), imperial ? Unit.Integer : Unit.FloatSingleFraction);
  return imperial ? <LocText entry={HUB_FMT.speedFpm} args={{ N }} /> : <LocText entry={HUB_FMT.speedMps} args={{ N }} />;
}

/** A display number (not an argument): like Num, with the signed-zero rule of numArg */
export function SignedNum({ value, unit }: { value: number; unit: Unit }): JSX.Element {
  if (typeof value !== "number" || !Number.isFinite(value)) return <span>{"–"}</span>;
  return <NumText value={value} unit={unit} signed={value !== 0} />;
}

/** The "≈" / "…" slot before a value (fixed width, and an empty slot keeps its width) */
export function MarkSlot({ mark }: { mark: "≈" | "…" | null }): JSX.Element {
  return <div className={styles.markSlot}>{mark ?? ""}</div>;
}

/** One label / value row with the vanilla InfoRow metrics (28.2 / 39.7 / 47.3 rem at 100 / 130 / 150 %) */
export function InfoLine({ label, value, tip, mark, header, sub, info, flag, flagDim, flagShapes, op }: {
  label: string | JSX.Element;
  value?: ReactNode;
  tip?: ReactNode;
  /** Render the "≈" slot. null = an empty slot of the same width. undefined = no slot. */
  mark?: "≈" | "…" | null;
  header?: boolean;
  sub?: boolean;
  /** A dim Info glyph right after the label text (not at the row end, where it would push the value column). The row's
   *  `tip` carries the notes. The label is then a flex row [text (shrinks, ellipsis) | glyph | rest], never inline-block
   *  (unproven in Gameface). */
  info?: boolean;
  /** A row whose 0 has a cause says it after the label: the problem band's Warning glyph + a short warning-colored word
   *  ("No ski school"), instead of a tooltip sentence. Same flex box as `info`. */
  flag?: string;
  /** The flag is a state, not a fault ("No run counts yet"): the dim Info glyph and dim text */
  flagDim?: boolean;
  /** A flag drawn as shapes instead of a word (icon scheme: the Variety row's missing ratings, BandMarker elements). Same flex box as
   *  `flag`: the Warning glyph, then the shapes. The row stays one text node (the label). */
  flagShapes?: ReactNode;
  /** The receipt term's operator ("+", "×") in a fixed-width cell before the label, so the terms read as the equation they
   *  are. Symbols only (nothing to translate). */
  op?: string;
}): JSX.Element {
  return (
    <Tip tip={tip}>
      <div className={cx(styles.infoLine, header && styles.infoHeader, sub && styles.infoSub)}>
        {op !== undefined && <div className={styles.infoOp}>{op}</div>}
        {flag || flagShapes ? (
          <div className={styles.infoLabelBox}>
            <div className={styles.infoLabelText}>{label}</div>
            {flagDim
              ? <Glyph src={GLYPH_INFO} className={cx(styles.infoMark, styles.glyphDimTone)} />
              : <Glyph src="Media/Glyphs/Warning.svg" className={cx(styles.infoMark, styles.glyphWarnTone)} />}
            {flagShapes
              ? <div className={styles.flagShapes}>{flagShapes}</div>
              : <div className={cx(styles.flagText, flagDim && styles.flagTextDim)}>{flag}</div>}
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

/** The half-filled caution dot of the run and lift rows (icon scheme): a patchy run, a lift without power. It matches the patchy icon of
 *  hub text, and it differs from the filled dot by shape, not by color alone. CSS only (hub.module.scss .liftDotHalf*). */
export function HalfDot(): JSX.Element {
  return (
    <div className={cx(styles.liftDot, styles.liftDotRing, styles.liftRingWarn, styles.liftDotHalf)}>
      <div className={styles.liftDotHalfClip}>
        <div className={styles.liftDotHalfDisc} />
      </div>
    </div>
  );
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

/** A bar of integer flex-grow parts (the proven difficulty-bar technique). The bar does not draw 0 parts.
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

/** A game notification icon (<img>). On a load error it shows the masked Warning glyph. */
export function NotifIcon({ src, className }: { src: string; className: string }): JSX.Element {
  const [failed, setFailed] = useState(false);
  if (failed) return <Glyph src="Media/Glyphs/Warning.svg" className={cx(className, styles.glyphWarnTone)} />;
  return <img src={src} className={className} onError={() => setFailed(true)} />;
}
