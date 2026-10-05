/*
 * src/hub/GuestsView.tsx - the Guests view (Guests tile): arrivals this month and last month, the
 * game-formula tourist estimate, and the notes on what is counted and what tickets do now.
 *
 * Written for this repo (MIT). Created 2026-10-04 (Round E1, stream S6; docs/research/econ-round/
 * spec/ch6-hub-ui.md section 6.10, design hub-v3-design.md 3.4, 6.8; D25 guests, D26 tickets, D34
 * honest labels).
 * Contract:
 *   - A sign: GUESTS header; two bars (this month, last month) on ONE scale (the larger month;
 *     hubRules.guestBarParts, integer flex-grow; an unknown value = an empty track and "–"); the
 *     table (this month / last month columns: tourists, tourist groups, residents, paid a lift
 *     ticket, lift tickets in money); the estimate slot (the game-formula target tourists, or - with
 *     Tourism Overhaul detected, a session state - EST_TO_NOTE in the same place); the note area
 *     (where guests are counted; "since load" while this month started before the load; the ticket
 *     note of hubRules.ticketNote, one ellipsized line with its full text in the tooltip, U8).
 *   - Last month is never a fake 0 (second pass, e1-spec H10): C# sends -1 for every last-month field
 *     unless `lastMonthFull`; the UI then shows "–", the empty track and GUESTS_LAST_NA.
 *   - Not linked: the header and UNLINKED_NOTE.
 *   - Fixed slots only: StableWidth cells with the shared money / count samples, note slots of fixed
 *     height (note2 = 2 XS lines, note1 = 1) whose empty state keeps its height (D33).
 *   - Panel file (gate R11): subscribes resorts$, selectedResort$, resortLive$, city$.
 */
import { useValue } from "cs2/api";
import { Unit } from "cs2/l10n";
import { city$, resortLive$, resorts$, selectedResort$ } from "../bindings";
import type { CityStats, ResortLive } from "../bindings";
import { useT } from "../l10n";
import type { Translate } from "../l10n";
import { guestBarParts, liveOf, ticketNote } from "./hubRules";
import { HUB_FMT, HUB_LOC } from "./hubText";
import {
  Divider, FlexBar, InfoLine, LocText, Num, SignedNum, StableWidth, Tip, TipLines, cx, numArg, useNumberSamples,
} from "./parts";
import type { NumberSamples } from "./parts";
import { currentResort, resortEntries } from "./resortData";
import styles from "./hub.module.scss";

const DASH = "–";

/** A count cell: numArg (Integer) or "–" for -1. */
function countCell(v: number): JSX.Element | string {
  return numArg(v, Unit.Integer);
}

/** One guest bar row: label (StableWidth of both month labels), track, value. */
function GuestBar({ label, labels, value, max, samples, tip }: {
  label: string; labels: readonly string[]; value: number; max: number; samples: NumberSamples; tip?: JSX.Element | string;
}): JSX.Element {
  const [fill, rest] = guestBarParts(value, max);
  return (
    <Tip tip={tip}>
      <div className={styles.guestBar}>
        <StableWidth samples={labels} align="start" className={styles.guestLabel}>{label}</StableWidth>
        <FlexBar
          className={styles.guestTrack}
          parts={[{ grow: fill, className: styles.guestFill }, { grow: rest, className: styles.barRest }]}
        />
        <StableWidth samples={samples.count} align="end">{countCell(value)}</StableWidth>
      </div>
    </Tip>
  );
}

/** One table row: label + this-month and last-month cells (the same StableWidth as the heads). */
function GuestRow({ label, thisMonth, lastMonth, tip, samples }: {
  label: string; thisMonth: JSX.Element | string; lastMonth: JSX.Element | string; tip: JSX.Element; samples: NumberSamples;
}): JSX.Element {
  return (
    <Tip tip={tip}>
      <div className={styles.infoLine}>
        <div className={styles.infoLabel}>{label}</div>
        <StableWidth samples={samples.money} align="end" className={styles.valueCell}>{thisMonth}</StableWidth>
        <StableWidth samples={samples.money} align="end" className={styles.valueCell}>{lastMonth}</StableWidth>
      </div>
    </Tip>
  );
}

function TicketNoteText({ l, city, t }: { l: ResortLive; city: CityStats; t: Translate }): JSX.Element {
  switch (ticketNote(l.ticketGate, city.ticketPrice)) {
    case "open": return <LocText entry={HUB_FMT.ticketsNoteOpen} args={{ PRICE: numArg(city.ticketPrice, Unit.Money) }} />;
    case "noEfficiency": return <>{t(HUB_LOC.ticketsNoteNoEfficiency)}</>;
    case "nothingOpen": return <>{t(HUB_LOC.ticketsNoteNothingOpen)}</>;
    case "notKnownYet": return <>{t(HUB_LOC.ticketsNoteNotKnownYet)}</>;
    default: return <>{t(HUB_LOC.ticketsNoteOff)}</>;
  }
}

function SignGuests({ l, city, samples, t }: { l: ResortLive | null; city: CityStats; samples: NumberSamples; t: Translate }): JSX.Element {
  const full = !!l && l.lastMonthFull;
  const thisMonth = l ? l.guestsThisMonth : -1;
  const lastMonth = full && l ? l.guestsLastMonth : -1;
  const max = Math.max(thisMonth, full ? lastMonth : 0);
  const labels = [t(HUB_LOC.guestsMonthThis), t(HUB_LOC.guestsMonthLast)];
  const lastNa = t(HUB_LOC.guestsLastNa);
  /** A last-month value: "–" until a whole month was counted (H10). */
  const last = (v: number | undefined, money?: boolean): JSX.Element | string =>
    !full || v === undefined || v < 0 ? DASH : money ? <Num value={v} unit={Unit.Money} /> : countCell(v);
  const rowTip = (title: string, body: string | JSX.Element): JSX.Element =>
    <TipLines lines={[title, body, !full && lastNa]} />;

  return (
    <>
      <InfoLine header label={t(HUB_LOC.sectionGuests)} />
      <GuestBar label={labels[0]} labels={labels} value={thisMonth} max={max} samples={samples} />
      <GuestBar label={labels[1]} labels={labels} value={lastMonth} max={max} samples={samples} tip={full ? undefined : lastNa} />
      <Divider />
      <div className={styles.colHeads}>
        <div className={styles.colHeadSpacer} />
        {/* E1 fixer (F14): heads measured in the row font (S), labels in XS - aligned with GuestRow's cells. */}
        <StableWidth samples={samples.money} align="end" className={cx(styles.valueCell, styles.colHeadCell)}>
          <span className={styles.colLabel}>{labels[0]}</span>
        </StableWidth>
        <StableWidth samples={samples.money} align="end" className={cx(styles.valueCell, styles.colHeadCell)}>
          <span className={styles.colLabel}>{labels[1]}</span>
        </StableWidth>
      </div>
      <GuestRow
        label={t(HUB_LOC.guestsRowTourists)} samples={samples}
        thisMonth={countCell(l ? l.touristsThisMonth : -1)} lastMonth={last(l?.touristsLastMonth)}
        tip={rowTip(t(HUB_LOC.guestsRowTourists), t(HUB_LOC.guestsTipTourists))}
      />
      <GuestRow
        label={t(HUB_LOC.guestsRowGroups)} samples={samples}
        thisMonth={countCell(l ? l.touristGroupsThisMonth : -1)} lastMonth={last(l?.touristGroupsLastMonth)}
        tip={rowTip(t(HUB_LOC.guestsRowGroups), t(HUB_LOC.guestsTipGroups))}
      />
      <GuestRow
        label={t(HUB_LOC.guestsRowResidents)} samples={samples}
        thisMonth={countCell(l ? l.residentsThisMonth : -1)} lastMonth={last(l?.residentsLastMonth)}
        tip={rowTip(t(HUB_LOC.guestsRowResidents), t(HUB_LOC.guestsTipResidents))}
      />
      <GuestRow
        label={t(HUB_LOC.guestsRowPaid)} samples={samples}
        thisMonth={countCell(l ? l.paidThisMonth : -1)} lastMonth={last(l?.paidLastMonth)}
        tip={rowTip(t(HUB_LOC.guestsRowPaid), <LocText entry={HUB_FMT.guestsTipPaid} args={{
          GUESTS: countCell(l ? l.guestsThisMonth : -1), PAID: countCell(l ? l.paidThisMonth : -1),
          UNPAID: countCell(l ? l.unpaidThisMonth : -1), FREE: countCell(l ? l.freeThisMonth : -1),
        }} />)}
      />
      <GuestRow
        label={t(HUB_LOC.guestsRowTickets)} samples={samples}
        thisMonth={l && l.ticketsThisMonth >= 0 ? <Num value={l.ticketsThisMonth} unit={Unit.Money} /> : DASH}
        lastMonth={last(l?.ticketsLastMonth, true)}
        tip={rowTip(t(HUB_LOC.guestsRowTickets), t(HUB_LOC.guestsTipTickets))}
      />
      <Divider />
      {city.tourismOverhaul ? (
        <div className={styles.noteArea}>
          <div className={styles.note2}>{t(HUB_LOC.estToNote)}</div>
        </div>
      ) : (
        <InfoLine
          label={t(HUB_LOC.estRowTourists)}
          value={l && l.estKnown ? <SignedNum value={l.estTourists} unit={Unit.Integer} /> : DASH}
          tip={t(HUB_LOC.estTouristsTip)}
        />
      )}
      <Divider />
      <div className={styles.noteArea}>
        <div className={styles.note2}>{t(HUB_LOC.guestsNoteWhere)}</div>
        <div className={styles.note2}>{l && l.thisMonthSinceLoad ? t(HUB_LOC.guestsNoteSinceLoad) : ""}</div>
        {l ? (
          <Tip tip={<TicketNoteText l={l} city={city} t={t} />}>
            <div className={styles.note1}><TicketNoteText l={l} city={city} t={t} /></div>
          </Tip>
        ) : (
          <div className={styles.note1}>{DASH}</div>
        )}
      </div>
    </>
  );
}

export function GuestsView(): JSX.Element {
  const t = useT();
  const samples = useNumberSamples();
  const resorts = useValue(resorts$);
  const selected = useValue(selectedResort$);
  const live = useValue(resortLive$);
  const city = useValue(city$);

  const cur = currentResort(resortEntries(resorts), selected);
  if (!cur.row || cur.isUnlinked) {
    return (
      <>
        <InfoLine header label={t(HUB_LOC.sectionGuests)} />
        <div className={styles.noteArea}>
          <div className={styles.note2}>{t(HUB_LOC.unlinkedNote)}</div>
        </div>
      </>
    );
  }
  return <SignGuests l={liveOf(live, cur.row.sign)} city={city} samples={samples} t={t} />;
}
