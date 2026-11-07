/*
 * src/hub/HubHeader.tsx - the panel's title bar: the ski-area selector, or the plain title.
 * Row `‹ [name ▾] › [pen] [fly]`. It has no "N / COUNT" since DECISIONS 613: that read like the pager's page count, and the list shows every area.
 * The name button toggles hubState.areaListOpen$, and HubPanel draws AreaList as an overlay in our body (no Portal).
 * The arrows cycle the entries (resortData.ts) and send selectResort(sign | Null). C# may refuse, and the UI never assumes.
 * The Not linked entry has no sign: its pen / fly keep their space (visibility hidden). The arrows also keep their space with one area.
 * Rename: pen -> RenameField. Commit -> rename(sign, text).
 * Every button stops mousedown (the Panel title bar is a drag handle) and passes focusKey={FOCUS_DISABLED} (rule R15).
 * Engine notes: EngineNotes/HubHeader.tsx.md
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
  hidden?: boolean;
}

/* An icon button of the title bar: never starts a drag. `hidden` keeps its space. */
function HeaderButton({ src, tip, onSelect, hidden }: HeaderButtonProps): JSX.Element {
  return (
    <Tip tip={hidden ? undefined : tip}>
      <Button
        variant="icon"
        src={src}
        tinted
        focusKey={FOCUS_DISABLED}
        className={cx(styles.headerButton, hidden && styles.hiddenKeepSpace)}
        onMouseDown={noDrag}
        onSelect={() => {
          if (!hidden) onSelect();
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


/* The area name as a plain text button with a chevron. A click opens or closes the AreaList overlay. */
function AreaNameButton({ name, unlinked, t }: { name: string; unlinked: boolean; t: Translate }): JSX.Element {
  const open = useValue(areaListOpen$);
  return (
    <Tip tip={open || !unlinked ? undefined : <TipLines lines={[t(HUB_LOC.unlinkedHint)]} />}>
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
 * selectedResort$). It has the same entries and order as the arrows. Picking one sends selectResort and closes the list.
 * The backdrop closes the list too.
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
            <Button focusKey={FOCUS_DISABLED} key={key} theme={{ button: cx(styles.overlayItem, on && styles.overlayItemOn) }} selected={on}
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
      <HeaderButton src="Media/Glyphs/ThickStrokeArrowLeft.svg" tip={t(HUB_LOC.resortPrev)} hidden={single} onSelect={() => go(-1)} />
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
      <HeaderButton src="Media/Glyphs/ThickStrokeArrowRight.svg" tip={t(HUB_LOC.resortNext)} hidden={single} onSelect={() => go(1)} />
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
