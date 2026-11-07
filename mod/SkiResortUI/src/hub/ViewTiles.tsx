/*
 * src/hub/ViewTiles.tsx - the tiles: they are the navigation.
 * `div.tileRow` with one cs2/ui Button per HUB_VIEWS entry (selected = on). A click sets the local view$. A value change never changes a tile's size.
 * The Money tile's "≈" sits in a fixed trailing slot (a corner mark overlapped the last digit at 130-150 % text). The History tile shows a glyph,
 * never a number: it must not subscribe `history`, which only the History view observes. Not linked: Runs and Lifts show totals.
 * Panel file (hub gate). It subscribes resorts$, selectedResort$, resortLive$ and city$ (the month's name in the Guests tooltip), and the local view$.
 */
import type { RefObject } from "react";
import { useValue } from "cs2/api";
import { Button } from "cs2/ui";
import { FOCUS_DISABLED } from "cs2/input";
import { Unit } from "cs2/l10n";
import { LocalizedFraction } from "../cs2shim";
import { city$, resortLive$, resorts$, selectedResort$ } from "../bindings";
import type { ResortLive, ResortRow } from "../bindings";
import { useT } from "../l10n";
import type { LocEntry, Translate } from "../l10n";
import { buildingsAndWages, liftsTile, liveOf, unfinishedLifts } from "./hubRules";
import { HUB_VIEWS, view$ } from "./hubState";
import type { HubView } from "./hubState";
import { HUB_FMT, HUB_LOC, monthName } from "./hubText";
import { Glyph, LocText, SignedNum, Tip, TipLines, cx, numArg } from "./parts";
import { currentResort, resortEntries } from "./resortData";
import historyGlyph from "./HistoryGlyph.svg";
import styles from "./hub.module.scss";

const CAPTION: Readonly<Record<HubView, LocEntry>> = {
  overview: HUB_LOC.tileAttraction,
  runs: HUB_LOC.tileRuns,
  lifts: HUB_LOC.tileLifts,
  guests: HUB_LOC.tileGuests,
  money: HUB_LOC.tileMoney,
  history: HUB_LOC.tileHistory,
};

/** The History tile's value: our own flat glyph (the game's CityStatistics.svg is isometric and reads as a blob when masked flat
 *  at tile size). It is drawn as a mask in the tile's text color. It is a webpack asset/resource, like SkiLauncherGlyph.svg. */
const HISTORY_GLYPH: string = historyGlyph;

const DASH = <span>{"–"}</span>;
const HELD = <span>{"…"}</span>;

/** The tile's caption: "Runs open" on a sign's Runs tile, else CAPTION. */
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
      // While the first write after a load is held the game keeps the SAVED provider, not this Result, so the tile shows "…".
      if (!unlinked && l && l.attractionHeld) return HELD;
      return !unlinked && l && l.attraction >= 0 ? numArgEl(l.attraction) : DASH;
    case "runs":
      if (unlinked) return numArgEl(row.runs);
      return l && l.runsOpen >= 0
        ? <LocalizedFraction value={Math.max(0, l.runsOpen)} total={Math.max(0, row.runs | 0)} unit={Unit.Integer} /> : DASH;
    case "lifts": {
      if (unlinked) return numArgEl(row.liftRows);
      // Built / drawn: every line counts, unfinished ones included.
      const lt = liftsTile(row, l);
      return lt ? <LocalizedFraction value={lt.built} total={lt.drawn} unit={Unit.Integer} /> : DASH;
    }
    case "guests":
      return !unlinked && l && l.guestsThisMonth >= 0 ? numArgEl(l.guestsThisMonth) : DASH;
    case "history":
      // A glyph, not a number: the tile must not subscribe `history` (only the History view does).
      return <Glyph src={HISTORY_GLYPH} className={styles.tileGlyph} />;
    default:
      // runsOpen -1 on a linked row: not known yet (paused right after a load)
      return l && (unlinked || l.runsOpen >= 0) ? <SignedNum value={l.netMonth} unit={Unit.Money} /> : DASH;
  }
}

function tileMark(v: HubView, l: ResortLive | null, unlinked: boolean): "≈" | "" {
  if (!l || unlinked) return "";
  // (overview: the held "…" is the value itself, no corner mark)
  if (v === "runs" && l.snowEstimated) return "≈";
  if (v === "money" && l.netEstimated) return "≈";
  return "";
}

function tileTip(v: HubView, row: ResortRow, l: ResortLive | null, unlinked: boolean, month: number, t: Translate): JSX.Element {
  switch (v) {
    case "overview":
      return <TipLines lines={[t(HUB_LOC.tileTipAttraction),
        l && l.efficiencyPct >= 0 && <LocText key="s" entry={HUB_FMT.tileTipStaffing} args={{ PCT: numArg(l.efficiencyPct, Unit.Percentage) }} />,
        l && l.attractionFull >= 0 && <LocText key="f" entry={HUB_FMT.tileTipFull} args={{ N: numArg(l.attractionFull, Unit.Integer) }} />,
        l && l.attractionHeld && t(HUB_LOC.tileTipHeld)]} />;
    // The Not linked tiles show totals (row.runs, row.liftRows). So their second line is UNLINKED_NOTE, not the
    // "fully open" / "counted of complete" meaning of a sign's tile.
    case "runs":
      return <TipLines lines={[unlinked ? t(HUB_LOC.unlinkedNote) : t(HUB_LOC.tileTipRuns),
        !unlinked && l && l.runsPartly > 0 && <LocText key="p" entry={HUB_FMT.tileTipPartly} args={{ N: numArg(l.runsPartly, Unit.Integer) }} />]} />;
    case "lifts": {
      // hubRules.unfinishedLifts is 0 unless the live row knows the complete count.
      const n = unlinked ? 0 : unfinishedLifts(row, l);
      return <TipLines lines={[unlinked ? t(HUB_LOC.unlinkedNote) : t(HUB_LOC.tileTipLifts),
        n > 0 && <LocText key="u" entry={HUB_FMT.tileTipUnfinished} args={{ N: numArg(n, Unit.Integer) }} />,
        !unlinked && l && l.liftsCounted >= 0 && <LocText key="p" entry={HUB_FMT.tileTipPowered} args={{ N: numArg(l.liftsCounted, Unit.Integer) }} />]} />;
    }
    case "guests":
      // This month and the one before by name (city.month).
      return <TipLines lines={[<LocText key="g" entry={HUB_FMT.tileTipGuests} args={{ MONTH: monthName(month, t) }} />,
        l && l.thisMonthSinceLoad && t(HUB_LOC.tileTipSinceLoad),
        l && l.lastMonthFull && l.guestsLastMonth >= 0 && <LocText key="l" entry={HUB_FMT.tileTipLast} args={{
          MONTH: monthName(month, t, 1), N: numArg(l.guestsLastMonth, Unit.Integer),
        }} />]} />;
    case "history":
      return <TipLines lines={[t(HUB_LOC.tileTipHistory)]} />;
    default:
      // The tile is the Finances view's Net, this month so far. It shows "≈" only while a term is unknown (netEstimated).
      return <TipLines lines={[t(HUB_LOC.tileTipMoney), l && l.thisMonthSinceLoad && t(HUB_LOC.tileTipSinceLoad),
        l && l.netEstimated && buildingsAndWages(l) >= 0 && t(HUB_LOC.tileTipEst),
        l && buildingsAndWages(l) < 0 && t(HUB_LOC.tileTipEstBuildings)]} />;
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
              focusKey={FOCUS_DISABLED}
              theme={{ button: cx(styles.tile, on && styles.tileOn) }}
              selected={on}
              onSelect={() => { if (!on) view$.update(v); }}
            >
              <div className={styles.tileValue}>{row ? tileValue(v, row, l, unlinked) : DASH}</div>
              {v === "money" ? (
                // The Money tile's "≈" sits in a fixed trailing slot of the caption (always reserved), because the absolute corner
                // mark overlapped the last digit at 130-150 % text. The slot is a fixed-width flex item in a flex row. It is not an
                // inline-block span, which is unproven in Gameface and would collapse when empty. That would shift the caption when
                // "≈" toggles.
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
