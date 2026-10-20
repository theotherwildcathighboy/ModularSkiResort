/*
 * src/hub/HubHeader.tsx - the panel's title bar: the ski-area selector, or the plain title.
 *
 * Written for this repo (MIT). Created 2026-10-03 (Round U2, implementer I-3).
 * Authority: docs/research/hub-u2-spec.md section 4.1 ("Header"), 4.3 (switcher and rename),
 * OD-14 / OD-15 (switching never selects the sign in the world; no "All resorts" entry).
 * T97 P1 (2026-10-19, DECISIONS 308; docs/research/ui/hub-selector-p1-p3-2026-10-19.md): the name is a
 * selector (vanilla cs2/ui Dropdown) under a "Ski area" caption; its list names every area with its runs and
 * lodges, Not linked last, and one line on what the map selection does; its tooltip states the one rule.
 *
 * Where this renders (installed 1.6.2f1 index.js / index.css):
 *   - cs2/ui Panel (`uS`, index.js@733600) wraps `header` in PanelTitleBar (`IE`, @686490):
 *     div.titleBar > [div.iconSpace] [div.title > OUR ROOT] [close IconButton]. For a draggable
 *     panel the whole title bar is cloned by DragHandle with a drag `onMouseDown` (`Bj`, @719900),
 *     so every button here stops mousedown exactly like the vanilla close button does
 *     (`onMouseDown:e=>{e.preventDefault(),e.stopPropagation()}`, @686490). The caption and the
 *     "N / COUNT" do NOT stop it: the panel still drags by them (the name is a button since P1).
 *   - `.title` = `.title_SVH .title_zQN` (index.css@57050 / @55900): flex:1, centered, uppercase,
 *     `max-height: 2.4em` with `coh-font-fit-mode: shrink`. Our root therefore sets
 *     `text-transform: none`, `flex: 1 1 0; min-width: 0` and its own fixed row height
 *     (hub.module.scss .switcher), so the title's fit-shrink has nothing to shrink.
 *   - Buttons: cs2/ui Button with `variant="icon"` + `src` renders the vanilla IconButton `$b`
 *     (button variant table @712900: icon -> the icon-button theme; with src and no label ->
 *     IconButton @633990), the close button's own component; `tinted` draws the glyph as a mask
 *     in --iconColor (TintedIcon @633525).
 *   - The selector (P1) is the vanilla Dropdown (`Rj`, @716237): a FocusScope with OUR focusKey around a
 *     FocusKeyOverride(DROPDOWN_TOGGLE_KEY) that hosts the toggle, and an AnchoredPopup (`jj`, @713812) that renders the
 *     list in a Portal (`sh`, @540731) under the toggle, at least the toggle's width (minWidth: true), flipping up when
 *     fewer than 300 rem fit below. Open: a full-screen PointerBarrier (`ab`, @621027, also a Portal) closes it on a
 *     click; a document mousedown outside the list closes it too. DropdownToggle (`kj` @717950 / `Dj` @718388) is a Button
 *     whose onSelect toggles, with the theme's StrokeArrowDown / Up indicator; DropdownItem (`Uj`, @719062) is a Button:
 *     onChange(value) when not selected, then closes. React events bubble through portals along the React tree, so a
 *     mousedown on the list or the barrier would reach the title bar's drag handler: the wrapper div stops it (noDrag).
 *   - FOCUS (Round F6, C1; field test 6 log-audit A1): every button passes
 *     focusKey={FOCUS_DISABLED}, exactly like the close button beside them (`$b` with focusKey:Ug,
 *     @686490). The Panel wraps its header in a FocusKeyOverride whose KeyFocusController hosts ONE
 *     child (@684800 `Tp` around the header div; registerChild @578500), and a Button registers
 *     itself by default (FOCUS_AUTO, @629600): our four buttons logged 3 UI.log ERRORs ("Cannot
 *     register second focus key") on every open and 3 more on every close. Mouse clicks are
 *     untouched; keyboard / gamepad focus on these buttons goes (DECISIONS 130). tools/check-bundle.js
 *     rule R15 enforces it. P1: the Dropdown passes FOCUS_DISABLED, so its FocusScope has a null key
 *     (useUniqueFocusKey @566407) and never attaches to the header's controller (_tryAttach needs a key, @552708);
 *     the toggle and the items register INSIDE that scope (the toggle's focusKey is forced to DROPDOWN_TOGGLE_KEY,
 *     @718643: ours is a no-op kept for R15, typed in src/globals.d.ts; the items take their string value as key and
 *     live in the list's own vertical FocusController, which hosts many).
 * Contract:
 *   - Row: [‹ ThickStrokeArrowLeft] [› ThickStrokeArrowRight] RESORT_CAPTION "Ski area" (dim) [selector: name ▾
 *     (flex 1, ellipsis)] or the RenameField, RESORT_POSITION "N / COUNT" (LocalizedString, dim), [Pen] [MapMarker =
 *     fly to the sign: focusBuilding(sign), the selected-info panel's own "focus" glyph, index.js@2047600].
 *   - Arrows cycle the entries (src/hub/resortData.ts), wrapping; with one entry they are
 *     inactive (dimmed, no action), never hidden. A click sends selectResort(sign | Null); C#
 *     answers by pushing selectedResort (it may refuse; the UI never assumes).
 *   - Selector list: the same entries in the same order (signs in C#'s order, Not linked last and only when it holds
 *     anything), each "name" + RESORT_COUNTS "N runs · N lodges" (hubRules.resortCountsVariant / resortListCounts); the
 *     shown entry is `selected` (vanilla highlight; picking it again only closes the list); picking another sends
 *     selectResort(sign | Null), exactly like the arrows. Under the list RESORT_MENU_NOTE (what a map selection does,
 *     SkiResortHubUISystem.MapWorldSelection). Tooltip of the closed selector: RESORT_TIP, the one rule (Not linked:
 *     UNLINKED_HINT). With one entry the list still opens: it is where the rule and the counts are read.
 *   - The Not linked entry (warning color) has no sign: its pen and fly buttons keep their space
 *     (visibility hidden) so nothing moves when switching.
 *   - Rename: pen -> RenameField in the selector's place; commit -> rename(sign, text) (C# trims, cuts to 64, writes
 *     NameSystem.SetCustomName, logs `Hub [rename]`). Switching resort while typing commits first
 *     (the field unmounts: RenameField header).
 *   - No resort entry at all (no sign and nothing unlinked) -> the plain title "Ski Resort".
 *   - Mounted only while the panel is open, and only when the link is OK (contract 2): subscribes
 *     resorts$ and selectedResort$ (spec 4.5: on a mismatch only CORE bindings are read, and the
 *     panel renders PlainTitle instead).
 *   - T114 (2026-10-20, field report item 2, crop docs/research/field/2026-10-20/shots/_hub-crop-23.10.43.png; Richard: the dropdown "is
 *     ugly", "possibly an error on the display"; docs/research/ui/hub-round-t114-2026-10-20.md): SUPERSEDES the vanilla Dropdown above.
 *     In game its toggle drew a light box in the dark title bar with our name text in the panel's light text color on it, so the name
 *     read as faded white on white (the "display error"), and "Ski area" + box + arrows + position + pen + fly left the name little
 *     room. Now the row is `‹ [name ▾] › N / COUNT [pen] [fly]`: the name is a cs2/ui Button styled as the panel's own bold text
 *     (.areaName, no box until hover) between the two arrows, the caption moved into the list's title; a click toggles the local
 *     hubState.areaListOpen$, and the LIST is AreaList below, drawn by HubPanel as an overlay at the top of the content (inside our
 *     body, position absolute: no Portal, nothing for the title bar to clip, no drag guard on a portal). The list keeps T97's facts:
 *     every entry in the arrows' order, "N runs · N lodges", the shown one highlighted, Not linked in warning color, the map note;
 *     picking = selectResort(entrySign(r)), then the list closes; a click on the dimmed backdrop closes it. R15: the name button
 *     passes FOCUS_DISABLED like the other header buttons (the list's buttons are content buttons).
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

/** An item's "N runs · N lodges" (T97 P1): one text node, singular forms by hubRules.resortCountsVariant. */
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


/** T114: the area name as a plain text button with a chevron; a click opens / closes the AreaList overlay. */
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

/**
 * T114: the ski-area list, drawn by HubPanel at the top of its content while areaListOpen$ is on (a panel file: resorts$,
 * selectedResort$). Same entries and order as the arrows; picking one sends selectResort and closes the list; the backdrop closes it.
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
