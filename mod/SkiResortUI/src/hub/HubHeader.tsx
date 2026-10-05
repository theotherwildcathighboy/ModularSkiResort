/*
 * src/hub/HubHeader.tsx - the panel's title bar: the resort switcher, or the plain title.
 *
 * Written for this repo (MIT). Created 2026-10-03 (Round U2, implementer I-3).
 * Authority: docs/research/hub-u2-spec.md section 4.1 ("Header"), 4.3 (switcher and rename),
 * OD-14 / OD-15 (switching never selects the sign in the world; no "All resorts" entry).
 *
 * Where this renders (installed 1.6.2f1 index.js / index.css):
 *   - cs2/ui Panel (`uS`, index.js@733600) wraps `header` in PanelTitleBar (`IE`, @686490):
 *     div.titleBar > [div.iconSpace] [div.title > OUR ROOT] [close IconButton]. For a draggable
 *     panel the whole title bar is cloned by DragHandle with a drag `onMouseDown` (`Bj`, @719900),
 *     so every button here stops mousedown exactly like the vanilla close button does
 *     (`onMouseDown:e=>{e.preventDefault(),e.stopPropagation()}`, @686490). The name label does NOT
 *     stop it: dragging the panel by its name keeps working, and renaming is the pen.
 *   - `.title` = `.title_SVH .title_zQN` (index.css@57050 / @55900): flex:1, centred, uppercase,
 *     `max-height: 2.4em` with `coh-font-fit-mode: shrink`. Our root therefore sets
 *     `text-transform: none`, `flex: 1 1 0; min-width: 0` and its own fixed row height
 *     (hub.module.scss .switcher), so the title's fit-shrink has nothing to shrink.
 *   - Buttons: cs2/ui Button with `variant="icon"` + `src` renders the vanilla IconButton `$b`
 *     (button variant table @712900: icon -> the icon-button theme; with src and no label ->
 *     IconButton @633990), the close button's own component; `tinted` draws the glyph as a mask
 *     in --iconColor (TintedIcon @633525).
 *   - FOCUS (Round F6, C1; field test 6 log-audit A1): every button passes
 *     focusKey={FOCUS_DISABLED}, exactly like the close button beside them (`$b` with focusKey:Ug,
 *     @686490). The Panel wraps its header in a FocusKeyOverride whose KeyFocusController hosts ONE
 *     child (@684800 `Tp` around the header div; registerChild @578500), and a Button registers
 *     itself by default (FOCUS_AUTO, @629600): our four buttons logged 3 UI.log ERRORs ("Cannot
 *     register second focus key") on every open and 3 more on every close. Mouse clicks are
 *     untouched; keyboard / gamepad focus on these buttons goes (DECISIONS 130). tools/check-bundle.js
 *     rule R15 enforces it.
 * Contract:
 *   - Row: [‹ ThickStrokeArrowLeft] [› ThickStrokeArrowRight] name (flex 1, ellipsis) or the
 *     RenameField, RESORT_POSITION "N / COUNT" (LocalizedString, dim), [Pen] [MapMarker = fly to the
 *     sign: focusBuilding(sign), the selected-info panel's own "focus" glyph, index.js@2047600].
 *   - Arrows cycle the entries (src/hub/resortData.ts), wrapping; with one entry they are
 *     inactive (dimmed, no action), never hidden. A click sends selectResort(sign | Null); C#
 *     answers by pushing selectedResort (it may refuse; the UI never assumes).
 *   - The Not linked entry (warning colour) has no sign: its pen and fly buttons keep their space
 *     (visibility hidden) so nothing moves when switching.
 *   - Rename: pen -> RenameField; commit -> rename(sign, text) (C# trims, cuts to 64, writes
 *     NameSystem.SetCustomName, logs `Hub [rename]`). Switching resort while typing commits first
 *     (the field unmounts: RenameField header).
 *   - No resort entry at all (no sign and nothing unlinked) -> the plain title "Ski Resort".
 *   - Mounted only while the panel is open, and only when the link is OK (contract 2): subscribes
 *     resorts$ and selectedResort$ (spec 4.5: on a mismatch only CORE bindings are read, and the
 *     panel renders PlainTitle instead).
 */
import { useState } from "react";
import type { MouseEvent, ReactNode } from "react";
import { useValue } from "cs2/api";
import { Button } from "cs2/ui";
import { FOCUS_DISABLED } from "cs2/input";
import { hubTriggers, isNullEntity, resorts$, selectedResort$ } from "../bindings";
import { LOC, useT } from "../l10n";
import { HUB_FMT, HUB_LOC } from "./hubText";
import { LocText, Tip, TipLines, cx } from "./parts";
import { RenameField } from "./RenameField";
import { currentResort, resortEntries, stepResort } from "./resortData";
import styles from "./hub.module.scss";

/** The vanilla close button's guard against the title bar's drag (index.js@686490). */
function noDrag(e: MouseEvent): void {
  e.preventDefault();
  e.stopPropagation();
}

/** The contract-mismatch / no-resort title. */
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

/** An icon button of the title bar: never starts a drag; `inactive` keeps it visible and inert. */
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
      <HeaderButton src="Media/Glyphs/ThickStrokeArrowRight.svg" tip={t(HUB_LOC.resortNext)} inactive={single} onSelect={() => go(1)} />
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
        <Tip tip={<TipLines lines={[name, unlinked && t(HUB_LOC.unlinkedHint)]} />}>
          <div className={cx(styles.switcherName, unlinked && styles.warnText)}>{name}</div>
        </Tip>
      )}
      <div className={styles.switcherPosition}>
        <LocText entry={HUB_FMT.resortPosition} args={{ N: String(position), COUNT: String(Math.max(count, 1)) }} />
      </div>
      <HeaderButton
        src="Media/Glyphs/Pen.svg"
        tip={<TipLines lines={[t(HUB_LOC.rename), t(HUB_LOC.renameTip)]} />}
        hidden={unlinked}
        onSelect={() => setRenaming(isRenaming ? null : signKey)}
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
