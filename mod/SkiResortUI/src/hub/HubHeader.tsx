/*
 * src/hub/HubHeader.tsx - the panel's title bar: the ski-area selector, or the plain title.
 *
 * Row: `‹ [name ▾] › N / COUNT [pen] [fly]`. The name is a cs2/ui Button styled as the panel's bold text (.areaName, no box until
 * hover) between the two arrows. A click toggles hubState.areaListOpen$ and the list is AreaList, drawn by HubPanel as an
 * overlay at the top of the content (position absolute inside our body: no Portal, nothing for the title bar to clip, no drag
 * guard needed). The vanilla Dropdown was dropped: its toggle drew a light box in the dark title bar with the panel's light
 * text on it.
 *   - Arrows cycle the entries (src/hub/resortData.ts), wrapping; with one entry they are inactive (dimmed), never hidden.
 *     A click sends selectResort(sign | Null); C# answers by pushing selectedResort (it may refuse; the UI never assumes).
 *   - List: the same entries in the same order (signs in C#'s order, Not linked last and only when it holds anything), each
 *     "name" + "N runs · N lodges" (hubRules.resortCountsVariant / resortListCounts); the shown one highlighted. Picking sends
 *     selectResort(entrySign(r)) and closes the list; the backdrop closes it. Under the list RESORT_MENU_NOTE says what a map
 *     selection does (SkiResortHubUISystem.MapWorldSelection).
 *   - The Not linked entry (warning color) has no sign: its pen and fly buttons keep their space (visibility hidden) so
 *     nothing moves when switching.
 *   - Rename: pen -> RenameField in the selector's place; commit -> rename(sign, text) (C# trims, cuts to 64, writes
 *     NameSystem.SetCustomName). Switching resort while typing commits first (the field unmounts).
 *   - No resort entry at all -> the plain title "Ski Resort".
 *   - Mounted only while the panel is open and the link is OK; subscribes resorts$ and selectedResort$ (on a mismatch only
 *     core bindings are read and the panel renders PlainTitle).
 *
 * Where this renders (installed 1.6.2f1 index.js / index.css):
 *   - cs2/ui Panel (`uS`, index.js@733600) wraps `header` in PanelTitleBar (`IE`, @686490): div.titleBar > [div.iconSpace]
 *     [div.title > OUR ROOT] [close IconButton]. A draggable panel clones the whole title bar with a drag onMouseDown (`Bj`,
 *     @719900), so every button here stops mousedown like the vanilla close button (@686490). The caption and "N / COUNT" do
 *     not stop it: the panel still drags by them.
 *   - `.title` (index.css@57050 / @55900): flex:1, centered, uppercase, max-height 2.4em with coh-font-fit-mode: shrink. Our
 *     root therefore sets text-transform: none, flex: 1 1 0; min-width: 0 and its own fixed row height (hub.module.scss
 *     .switcher), so the fit-shrink has nothing to shrink.
 *   - Button variant="icon" + src renders the vanilla IconButton (`$b`; variant table @712900, IconButton @633990), the close
 *     button's own component; `tinted` draws the glyph as a mask in --iconColor (TintedIcon @633525).
 *   - Focus: every button passes focusKey={FOCUS_DISABLED}, like the close button (`$b` with focusKey:Ug, @686490). The Panel
 *     wraps its header in a FocusKeyOverride whose KeyFocusController hosts one child (@684800 `Tp`; registerChild @578500),
 *     and a Button registers itself by default (FOCUS_AUTO, @629600): the buttons logged "Cannot register second focus key"
 *     UI.log errors on every open and close. Mouse clicks are untouched; keyboard / gamepad focus on these buttons is lost.
 *     tools/check-bundle.js rule R15 enforces it.
 */
import { useState } from "react";
import type { MouseEvent, ReactNode } from "react";
import { useValue } from "cs2/api";
import { Button } from "cs2/ui";
import { FOCUS_DISABLED } from "cs2/input";
import { Unit } from "cs2/l10n";
import { hubTriggers, isNullEntity, resorts$, selectedResort$ } from "../bindings";
import type { ResortRow } from "../bindings";
import { LOC, useT } from "../l10n";
import type { Translate } from "../l10n";
import { resortCountsVariant, resortListCounts } from "./hubRules";
import { areaListOpen$ } from "./hubState";
import { HUB_FMT, HUB_LOC } from "./hubText";
import { Glyph, LocText, Tip, TipLines, cx, numArg } from "./parts";
import { RenameField } from "./RenameField";
import { currentResort, entryKey, entrySign, resortEntries, stepResort } from "./resortData";
import styles from "./hub.module.scss";

/* The vanilla close button's guard against the title bar's drag (index.js@686490). */
function noDrag(e: MouseEvent): void {
  e.preventDefault();
  e.stopPropagation();
}

/* The contract-mismatch / no-resort title. */
export function PlainTitle(): JSX.Element {
  const t = useT();
  return <span className={styles.plainTitle}>{t(LOC.title)}</span>;
}

export interface HeaderButtonProps {
  src: string;
  tip: ReactNode;
  onSelect: () => void;
  inactive?: boolean;
  hidden?: boolean;
}

/* An icon button of the title bar: never starts a drag; `inactive` keeps it visible and inert. */
function HeaderButton({ src, tip, onSelect, inactive, hidden }: HeaderButtonProps): JSX.Element {
  return (
    <Tip tip={hidden ? undefined : tip}>
      <Button
        variant="icon"
        src={src}
        tinted
        focusKey={FOCUS_DISABLED}
        className={cx(styles.headerButton, inactive && styles.headerButtonInactive, hidden && styles.hiddenKeepSpace)}
        onMouseDown={noDrag}
        onSelect={() => {
          if (!inactive && !hidden) onSelect();
        }}
      />
    </Tip>
  );
}

/* An item's "N runs · N lodges": one text node, singular forms by hubRules.resortCountsVariant. */
function ResortCounts({ row, t }: { row: ResortRow; t: Translate }): JSX.Element {
  const { runs, lodges } = resortListCounts(row);
  const RUNS = numArg(runs, Unit.Integer);
  const LODGES = numArg(lodges, Unit.Integer);
  switch (resortCountsVariant(runs, lodges)) {
    case "one": return <>{t(HUB_LOC.resortCountsOne)}</>;
    case "runOne": return <LocText entry={HUB_FMT.resortCountsRunOne} args={{ LODGES }} />;
    case "lodgeOne": return <LocText entry={HUB_FMT.resortCountsLodgeOne} args={{ RUNS }} />;
    default: return <LocText entry={HUB_FMT.resortCounts} args={{ RUNS, LODGES }} />;
  }
}


/* The area name as a plain text button with a chevron; a click opens / closes the AreaList overlay. */
function AreaNameButton({ name, unlinked, t }: { name: string; unlinked: boolean; t: Translate }): JSX.Element {
  const open = useValue(areaListOpen$);
  return (
    <Tip tip={open ? undefined : <TipLines lines={[unlinked ? t(HUB_LOC.unlinkedHint) : t(HUB_LOC.resortTip)]} />}>
      <Button
        focusKey={FOCUS_DISABLED}
        theme={{ button: cx(styles.areaName, open && styles.areaNameOpen) }}
        onMouseDown={noDrag}
        onSelect={() => areaListOpen$.update(!areaListOpen$.value)}
      >
        <div className={cx(styles.areaNameText, unlinked && styles.warnText)}>{name}</div>
        <Glyph src={open ? "Media/Glyphs/StrokeArrowUp.svg" : "Media/Glyphs/StrokeArrowDown.svg"} className={styles.areaChevron} />
      </Button>
    </Tip>
  );
}

/*
 * The ski-area list, drawn by HubPanel at the top of its content while areaListOpen$ is on (a panel file: resorts$,
 * selectedResort$). Same entries and order as the arrows; picking one sends selectResort and closes the list; the backdrop
 * closes it.
 */
export function AreaList(): JSX.Element | null {
  const t = useT();
  const open = useValue(areaListOpen$);
  const resorts = useValue(resorts$);
  const selected = useValue(selectedResort$);
  if (!open) return null;
  const e = resortEntries(resorts);
  const cur = currentResort(e, selected);
  const curKey = cur.row ? entryKey(cur.row) : "";
  const close = (): void => areaListOpen$.update(false);
  return (
    <>
      <div className={cx(styles.overlayCard, styles.overlayTop)}>
        <div className={styles.overlayTitle}>{t(HUB_LOC.resortCaption)}</div>
        {e.entries.map((r) => {
          const key = entryKey(r);
          const isUnlinked = r.kind === 1;
          const on = key === curKey;
          return (
            <Button key={key} theme={{ button: cx(styles.overlayItem, on && styles.overlayItemOn) }} selected={on}
              onSelect={() => {
                close();
                if (!on) hubTriggers.selectResort(entrySign(r));
              }}>
              <div className={cx(styles.overlayItemName, isUnlinked && styles.warnText)}>
                {isUnlinked ? t(HUB_LOC.unlinkedEntry) : r.name}
              </div>
              <div className={cx(styles.overlayItemCounts, isUnlinked && styles.warnText)}>
                <ResortCounts row={r} t={t} />
              </div>
            </Button>
          );
        })}
        <div className={styles.overlayNote}>{t(HUB_LOC.resortMenuNote)}</div>
      </div>
    </>
  );
}

export function HubHeader(): JSX.Element {
  const t = useT();
  const resorts = useValue(resorts$);
  const selected = useValue(selectedResort$);
  const [renaming, setRenaming] = useState<string | null>(null);

  const e = resortEntries(resorts);
  const cur = currentResort(e, selected);
  const row = cur.row;
  if (!row || (cur.index < 0 && e.entries.length === 0)) return <PlainTitle />;

  const unlinked = cur.isUnlinked;
  const count = e.entries.length;
  const position = cur.index < 0 ? 1 : cur.index + 1;
  const single = count < 2;
  const signKey = unlinked ? "" : `${row.sign.index}:${row.sign.version}`;
  const isRenaming = !unlinked && renaming === signKey;
  const name = unlinked ? t(HUB_LOC.unlinkedEntry) : row.name;

  const go = (step: number): void => {
    const to = stepResort(e, cur, step);
    if (to) hubTriggers.selectResort(to);
  };

  return (
    <div className={styles.switcher}>
      <HeaderButton src="Media/Glyphs/ThickStrokeArrowLeft.svg" tip={t(HUB_LOC.resortPrev)} inactive={single} onSelect={() => go(-1)} />
      {isRenaming ? (
        <div className={styles.switcherName}>
          <RenameField
            key={signKey}
            initial={row.name}
            className={styles.renameInputHeader}
            onCommit={(text) => {
              setRenaming(null);
              if (!isNullEntity(row.sign)) hubTriggers.rename(row.sign, text);
            }}
            onClose={() => setRenaming(null)}
          />
        </div>
      ) : (
        <AreaNameButton name={name} unlinked={unlinked} t={t} />
      )}
      <HeaderButton src="Media/Glyphs/ThickStrokeArrowRight.svg" tip={t(HUB_LOC.resortNext)} inactive={single} onSelect={() => go(1)} />
      <div className={styles.switcherPosition}>
        <LocText entry={HUB_FMT.resortPosition} args={{ N: String(position), COUNT: String(Math.max(count, 1)) }} />
      </div>
      <HeaderButton
        src="Media/Glyphs/Pen.svg"
        tip={<TipLines lines={[t(HUB_LOC.rename), t(HUB_LOC.renameTip)]} />}
        hidden={unlinked}
        onSelect={() => { areaListOpen$.update(false); setRenaming(isRenaming ? null : signKey); }}
      />
      <HeaderButton
        src="Media/Game/Icons/MapMarker.svg"
        tip={t(HUB_LOC.resortFly)}
        hidden={unlinked}
        onSelect={() => {
          if (!isNullEntity(row.sign)) hubTriggers.focusBuilding(row.sign);
        }}
      />
    </div>
  );
}
