/*
 * src/hub/ViewTiles.tsx - the five tiles: they ARE the navigation (D33, design 3.1, 5.2, 6.4, 7.1).
 *
 * Written for this repo (MIT). Created 2026-10-04 (Round E1, stream S6; docs/research/econ-round/
 * spec/ch6-hub-ui.md section 6.6; code from the checked reference
 * spec/ch6-hub-ui-ref/src/hub/ViewTiles.tsx).
 * Contract:
 *   - `div.tileRow` (ref tilesRef) with one cs2/ui Button per HUB_VIEWS entry (theme `tile [tileOn]`,
 *     selected = on); a click sets the local view$ unless already on. Content focus (ch6 section 9).
 *   - Each tile: `div.tileValue` (1.25 em of M, fit-shrink), `div.tileCaption` (TILE[...], sentence
 *     case), and the "≈" mark: a corner `div.tileMark` (out of flow), except the Money tile, whose "≈"
 *     sits in a fixed trailing slot beside its caption (`div.tileCaptionRow` > `div.tileCaption` +
 *     `div.tileCaptionMark`, a fixed-width flex item, always reserved; second pass: the corner mark
 *     overlapped the last digit at 130-150 % text; E1 fixer F13: flex, not inline-block). A value
 *     change never changes a tile's size.
 *   - Values (a sign / Not linked): Attraction = "…" while the first write after a load is held
 *     (second pass: the game keeps the SAVED score then), else `attraction` / "–"; Runs = runsOpen /
 *     runs (LocalizedFraction) / `runs`; Lifts = liftsCounted / liftsComplete / `liftRows`; Guests =
 *     guestsThisMonth / "–"; Money = the signed netMonth (C# sends -liftCostMonth for Not linked).
 *   - Tooltips: the tile's title and meaning, its state lines (full-efficiency score, held, partly
 *     open, unfinished lifts, since load, last month only when a whole month was counted, the "≈"
 *     reasons), then TILE_TIP_OPEN. Not linked (E1 fixer F15): the Runs and Lifts tiles show totals,
 *     so their meaning line is UNLINKED_NOTE and no partly-open / unfinished line is added.
 *   - Round F7 D1 (docs/research/field-test-7-fixes.md): a sign's Runs tile is captioned "Runs open"
 *     (TILE[RunsOpen], XS 56.6 / 93.1 / 117.5 rem against the caption room 75 / 111 / 135 of design §2.3:
 *     fits, no shrink), so "2 / 3" says what it counts; the Not linked tile shows a total and keeps
 *     "Runs" (TILE[Runs]). The tooltip title follows the caption.
 *   - Panel file (gate R11): subscribes resorts$, selectedResort$, resortLive$, city$ (T77: the month's name in the Guests
 *     tooltip, "Month of <name>"); local view$.
 */
import type { RefObject } from "react";
import { useValue } from "cs2/api";
import { Button } from "cs2/ui";
import { Unit } from "cs2/l10n";
import { LocalizedFraction } from "../cs2shim";
import { city$, resortLive$, resorts$, selectedResort$ } from "../bindings";
import type { ResortLive, ResortRow } from "../bindings";
import { useT } from "../l10n";
import type { LocEntry, Translate } from "../l10n";
import { buildingsAndWages, liveOf, unfinishedLifts } from "./hubRules";
import { HUB_VIEWS, view$ } from "./hubState";
import type { HubView } from "./hubState";
import { HUB_FMT, HUB_LOC, monthName } from "./hubText";
import { LocText, SignedNum, Tip, TipLines, cx, numArg } from "./parts";
import { currentResort, resortEntries } from "./resortData";
import styles from "./hub.module.scss";

const CAPTION: Readonly<Record<HubView, LocEntry>> = {
  overview: HUB_LOC.tileAttraction,
  runs: HUB_LOC.tileRuns,
  lifts: HUB_LOC.tileLifts,
  guests: HUB_LOC.tileGuests,
  money: HUB_LOC.tileMoney,
};

const DASH = <span>{"–"}</span>;
const HELD = <span>{"…"}</span>;

/** The tile's caption: "Runs open" on a sign's Runs tile (Round F7 D1), else CAPTION. */
function captionOf(v: HubView, unlinked: boolean): LocEntry {
  return v === "runs" && !unlinked ? HUB_LOC.tileRunsOpen : CAPTION[v];
}

function numArgEl(n: number): JSX.Element {
  const a = numArg(n, Unit.Integer);
  return typeof a === "string" ? <span>{a}</span> : a;
}

function tileValue(v: HubView, row: ResortRow, l: ResortLive | null, unlinked: boolean): JSX.Element {
  switch (v) {
    case "overview":
      // second pass: while the first write after a load is held the game keeps the SAVED provider, not
      // this Result, so the tile shows "…" instead of a number the game is not using.
      if (!unlinked && l && l.attractionHeld) return HELD;
      return !unlinked && l && l.attraction >= 0 ? numArgEl(l.attraction) : DASH;
    case "runs":
      if (unlinked) return numArgEl(row.runs);
      return l && l.runsOpen >= 0
        ? <LocalizedFraction value={Math.max(0, l.runsOpen)} total={Math.max(0, row.runs | 0)} unit={Unit.Integer} /> : DASH;
    case "lifts":
      if (unlinked) return numArgEl(row.liftRows);
      return l && l.liftsComplete >= 0 && l.liftsCounted >= 0
        ? <LocalizedFraction value={l.liftsCounted} total={l.liftsComplete} unit={Unit.Integer} /> : DASH;
    case "guests":
      return !unlinked && l && l.guestsThisMonth >= 0 ? numArgEl(l.guestsThisMonth) : DASH;
    default:
      return l ? <SignedNum value={l.netMonth} unit={Unit.Money} /> : DASH;
  }
}

function tileMark(v: HubView, l: ResortLive | null, unlinked: boolean): "≈" | "" {
  if (!l || unlinked) return "";
  // (overview: the held "…" is the value itself, second pass - no corner mark)
  if (v === "runs" && l.snowEstimated) return "≈";
  if (v === "money" && l.netEstimated) return "≈";
  return "";
}

function tileTip(v: HubView, row: ResortRow, l: ResortLive | null, unlinked: boolean, month: number, t: Translate): JSX.Element {
  const open = t(HUB_LOC.tileTipOpen);
  switch (v) {
    case "overview":
      return <TipLines lines={[t(HUB_LOC.tileAttraction), t(HUB_LOC.tileTipAttraction),
        l && l.efficiencyPct >= 0 && <LocText key="s" entry={HUB_FMT.tileTipStaffing} args={{ PCT: numArg(l.efficiencyPct, Unit.Percentage) }} />,
        l && l.attractionFull >= 0 && <LocText key="f" entry={HUB_FMT.tileTipFull} args={{ N: numArg(l.attractionFull, Unit.Integer) }} />,
        l && l.attractionHeld && t(HUB_LOC.tileTipHeld), open]} />;
    // E1 fixer (e1-spec 8.9 F15): the Not linked tiles show totals (row.runs, row.liftRows), so their
    // second line is UNLINKED_NOTE, not the "fully open" / "counted of complete" meaning of a sign's tile.
    case "runs":
      return <TipLines lines={[t(captionOf(v, unlinked)), unlinked ? t(HUB_LOC.unlinkedNote) : t(HUB_LOC.tileTipRuns),
        !unlinked && l && l.runsPartly > 0 && <LocText key="p" entry={HUB_FMT.tileTipPartly} args={{ N: numArg(l.runsPartly, Unit.Integer) }} />, open]} />;
    case "lifts": {
      // E1 fixer (F11): hubRules.unfinishedLifts is 0 unless the live row knows the complete count.
      const n = unlinked ? 0 : unfinishedLifts(row, l);
      return <TipLines lines={[t(HUB_LOC.tileLifts), unlinked ? t(HUB_LOC.unlinkedNote) : t(HUB_LOC.tileTipLifts),
        n > 0 && <LocText key="u" entry={HUB_FMT.tileTipUnfinished} args={{ N: numArg(n, Unit.Integer) }} />, open]} />;
    }
    case "guests":
      // T77 (contract 9): this month and the one before by name (city.month).
      return <TipLines lines={[t(HUB_LOC.tileGuests), <LocText key="g" entry={HUB_FMT.tileTipGuests} args={{ MONTH: monthName(month, t) }} />,
        l && l.thisMonthSinceLoad && t(HUB_LOC.tileTipSinceLoad),
        l && l.lastMonthFull && l.guestsLastMonth >= 0 && <LocText key="l" entry={HUB_FMT.tileTipLast} args={{
          MONTH: monthName(month, t, 1), N: numArg(l.guestsLastMonth, Unit.Integer),
        }} />, open]} />;
    default:
      return <TipLines lines={[t(HUB_LOC.tileMoney), t(HUB_LOC.tileTipMoney), l && l.ticketIncomeEstimated && t(HUB_LOC.tileTipEst),
        l && buildingsAndWages(l) < 0 && t(HUB_LOC.tileTipEstBuildings), open]} />;
  }
}

export function ViewTiles({ tilesRef }: { tilesRef: RefObject<HTMLDivElement> }): JSX.Element {
  const t = useT();
  const resorts = useValue(resorts$);
  const selected = useValue(selectedResort$);
  const live = useValue(resortLive$);
  const view = useValue(view$);
  const city = useValue(city$);
  const cur = currentResort(resortEntries(resorts), selected);
  const row = cur.row;
  const unlinked = cur.isUnlinked;
  const l = row ? liveOf(live, unlinked ? null : row.sign) : null;
  return (
    <div ref={tilesRef} className={styles.tileRow}>
      {HUB_VIEWS.map((v) => {
        const on = v === view;
        const mark = tileMark(v, l, unlinked);
        return (
          <Tip key={v} tip={row ? tileTip(v, row, l, unlinked, city.month, t) : undefined}>
            <Button
              theme={{ button: cx(styles.tile, on && styles.tileOn) }}
              selected={on}
              onSelect={() => { if (!on) view$.update(v); }}
            >
              <div className={styles.tileValue}>{row ? tileValue(v, row, l, unlinked) : DASH}</div>
              {v === "money" ? (
                // second pass: the Money tile's "≈" sits in a fixed trailing slot of the caption (always
                // reserved), because the absolute corner mark overlapped the last digit at 130-150 % text.
                // E1 fixer (e1-spec 8.9 F13): the slot is a fixed-width flex item beside the caption in a
                // flex row (the technique our bundle has proven), not an inline-block span (unproven in
                // Gameface: collapsed when empty, the caption would shift when "≈" toggles).
                <div className={styles.tileCaptionRow}>
                  <div className={styles.tileCaption}>{t(captionOf(v, unlinked))}</div>
                  <div className={styles.tileCaptionMark}>{mark}</div>
                </div>
              ) : (
                <>
                  <div className={styles.tileCaption}>{t(captionOf(v, unlinked))}</div>
                  <div className={styles.tileMark}>{mark}</div>
                </>
              )}
            </Button>
          </Tip>
        );
      })}
    </div>
  );
}
