/*
 * src/hub/GuestsView.tsx - the Guests view (Guests tile): arrivals this month and last month, the
 * game-formula tourist estimate, and the notes on what is counted and what tickets do now.
 *
 * Written for this repo (MIT). Created 2026-10-04 (Round E1, stream S6; docs/research/econ-round/
 * spec/ch6-hub-ui.md section 6.10, design hub-v3-design.md 3.4, 6.8; D25 guests, D26 tickets, D34
 * honest labels). Round F7 (2026-10-04, implementer 4; docs/research/field-test-7-fixes.md D2, D3;
 * findings/hub-ux.md §3.2 with its Verification): the always-visible notes moved into the header's Info
 * tooltip, Tourism Overhaul reads as a detection on one line, and one ticket-state line stays. Field test
 * 7 state (TO detected, tickets open): 13 text lines in 16 slot lines -> 10 text lines in 11. Round S1
 * "Ski days" (2026-10-05, stream E; docs/research/ski-days/spec/ch4-hub.md section B, D27, F7): the Ski
 * days row and its state line open the table (two lines: 12 text lines in 13), the bars use a fixed
 * ladder, and a since-load last month shows its numbers marked "(partial)". Round M2 (2026-10-10, builder
 * B4; ruling R9, docs/research/round-m2/understand/enabled-flags.md C5, build-contract.md section 8.3): one
 * trip-planner note line under the estimate slot while a known mod replaced, or something switched off, the
 * game's trip systems (a session state read at each city load; absent in a base-game city).
 * Contract:
 *   - A sign: GUESTS header with the Info glyph right after the label (tooltip: GUESTS_NOTE[Where];
 *     GUESTS_NOTE[SinceLoad] while this month started before the load; TICKETS_NOTE[Open] while tickets
 *     are sold); two bars (this month, last month) on ONE scale (Round S1: hubRules.guestBarScale, the
 *     smallest step of 50, 100, 200, 500, 1000, ... that holds both months, so 2 guests fill 4 % of the
 *     bar, not all of it; GUESTS_BAR_SCALE "Full bar: {MAX} guests" in both bars' tooltips;
 *     hubRules.guestBarParts, integer flex-grow; an unknown value = an empty track and "–"); bar 2's
 *     label reads "Last month (partial)" for a since-load month (its StableWidth samples are all three
 *     labels, so the track never moves, D33); the table (this month / last month columns: Round S1 D27
 *     FIRST the Ski days row (arrivals at a lodge; tooltip: GUESTS_TIP[SkiDays], the sent counts by
 *     class, the guests on the way or on the hill now of the capacity) and under it ONE state line
 *     (hubRules.skiDayLine: "Holds {N} skiers at once (runs limit)", "Full right now", "Closed: no open
 *     run", ...; one text node, XS, ellipsis, its tooltip by hubRules.skiDayTip), first so it stays above
 *     the fold at 130 / 150 % (ch4 B.5); then tourists, tourist groups, residents, paid a lift ticket,
 *     lift tickets in money); the estimate slot (the game-formula target tourists, or - with
 *     Tourism Overhaul detected, a session state - ONE line EST_TO_NOTE "Detected: ..." with EST_TO_TIP
 *     as its tooltip; XS 280.3 / 461.1 / 581.6 rem against W - 20 = 402 / 580 / 699: fits); Round M2 R9:
 *     while hubRules.tripPlannerNote(city) is not "none", ONE note line (note1, one text node per branch,
 *     gate R13): TRIP_PLANNER_NOTE[Known] "Detected: {NAME}. ..." / [KnownOff] "Ski days off: {NAME}
 *     replaced the game's leisure systems." (NAME = city.tripPlanner, the C# label) / [Unknown] /
 *     [UnknownOff]; its tooltip repeats the note (the line ellipsizes: Known with "Realistic PathFinding"
 *     is about 95 characters, ≈ 480 rem XS at 100 %) and adds TRIP_PLANNER_TIP (what is read, and when);
 *     the line appears or goes at a city load, or when the game's trip systems change during play (rare:
 *     SkiDaySystem.TripSystemsTick updates the label; the line then pushes the ticket slot down one line); the
 *     ski-day state line also shows the "Off" note while ski days are blocked and the state is unknown (fixer,
 *     review-ui-hub-money F1: R9's "the ski-day rows show it"); the ticket
 *     slot: one ellipsized line (U8) with the ticket note of hubRules.ticketNote while tickets are NOT
 *     sold (off, sign not running, nothing open, not known yet; full text in its tooltip), empty at the
 *     same height while they are (that sentence is in the header tooltip), so a gate flip moves nothing.
 *   - Last month is never a fake 0 (second pass, e1-spec H10). Round S1 F7 (hubRules.lastMonthMark): C#
 *     sends the last-month fields whenever a month rolled since counting started; "full" = a whole month,
 *     "partial" = counted since the load (numbers shown; bar 2's label and every tooltip say so with
 *     GUESTS_LAST_PARTIAL); "none" = -1 (the UI shows "–", the empty track and GUESTS_LAST_NA). The
 *     column head stays "Last month" (its cell is the money sample's width; ch4 B.4 rule 4, J5).
 *   - T07 (2026-10-16, field item 90): the table is grouped under three header lines, GUESTS_GROUP[Lodges] (tourists, tourist
 *     groups, residents; a dim GUESTS_NOTE[NeverSki] line under them: the guest count is everyone at a lodge), GUESTS_GROUP[Sent]
 *     (the Ski days row and its state line) and GUESTS_GROUP[Tickets] (paid, pass holders, lessons, lift tickets in money). The
 *     three populations differ by design; the rows, bindings and tooltips keep their keys. Supersedes "FIRST the Ski days row"
 *     above: Ski days is now the first row of the second group.
 *   - T77 (2026-10-18, contract 9): the months are named, "Month of <name>" on the bars and in the tooltips (CityStats.month,
 *     vanilla Common.MONTH:<n>), the bare name in the two column heads (their cells keep the money width). Supersedes the
 *     "This month" / "Last month" / "Last month (partial)" labels above.
 *   - Not linked: the header and UNLINKED_NOTE.
 *   - Fixed slots only: StableWidth cells with the shared money / count samples, note slots of fixed
 *     height (note2 = 2 XS lines, note1 = 1) whose empty state keeps its height (D33).
 *     Ticket-slot texts in XS, the longest "No tickets yet: the runs are still being checked after
 *     loading." 320.9 / 527.9 / 665.9 rem: fits 402 / 580 / 699. Round M2 (build-contract.md section
 *     8.4, Decided here 16; shortened by the fixer, review-ui-hub-money F2): NotKnownYet is now "No tickets
 *     yet: runs are being checked (new sign or load)." (58 characters, under the 63 measured above: fits);
 *     the slot's tooltip carries the full sentence (as for every ticket note). The longest ski-day state line, "Holds
 *     1,440 skiers at once (lodges limit)" (41 characters, ≈ 21 em XS by ch4 B.5's 0.5 em estimate:
 *     252 / 415 / 523 rem), fits the SubLine room W - 32 = 390 / 568 / 687 (an estimate, not Overpass
 *     advances: the ellipsis is the net).
 *   - Contract 8 (2026-10-15, econ-2 round 2): once a lesson was taken (hubRules.lessonsShown), the row GUESTS_ROW[Lessons] "of which took a
 *     lesson" (this month / last month from lessonsThisMonth / lessonsLastMonth; tooltip GUESTS_TIP[Lessons]) above the tickets row. People
 *     who took a lesson, one lesson each; a subset of the guests counted, like the pass-holder row.
 *   - Contract 7 (2026-10-14, season passes v1): under "Paid a lift ticket", while hubRules.passesShown (the Options pass share above 0,
 *     or a pass sold / a holder skied), the row GUESTS_ROW[PassHolders] "of which pass holders" (this month / last month from
 *     passHoldersThisMonth / passHoldersLastMonth; tooltip GUESTS_TIP[PassHolders]). It is a subset of the residents row, not of the paid row.
 *   - Panel file (gate R11): subscribes resorts$, selectedResort$, resortLive$, city$.
 */
import { useValue } from "cs2/api";
import { Unit } from "cs2/l10n";
import { city$, resortLive$, resorts$, selectedResort$ } from "../bindings";
import type { CityStats, ResortLive } from "../bindings";
import { useT } from "../l10n";
import type { Translate } from "../l10n";
import {
  guestBarParts, guestBarScale, lastMonthMark, lessonsShown, liveOf, passesShown, skiDayLine, skiDayTip, skiDayWalk, ticketNote, tripPlannerNote,
} from "./hubRules";
import type { LastMonthMark, TicketNote, TripPlannerNote } from "./hubRules";
import { HUB_FMT, HUB_LOC, allMonthNames, monthName } from "./hubText";
import {
  Divider, FlexBar, InfoLine, LocText, Num, StableWidth, SubLine, Tip, TipLines, cx, numArg, useNumberSamples,
} from "./parts";
import type { NumberSamples } from "./parts";
import { currentResort, resortEntries } from "./resortData";
import styles from "./hub.module.scss";

const DASH = "–";

/** A count cell: numArg (Integer) or "–" for -1. */
function countCell(v: number): JSX.Element | string {
  return numArg(v, Unit.Integer);
}

/** One guest bar row: label (StableWidth of every month label), track, value. */
function GuestBar({ label, labels, value, max, samples, tip }: {
  label: JSX.Element | string; labels: readonly JSX.Element[]; value: number; max: number; samples: NumberSamples; tip?: JSX.Element | string;
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

/** Fixer (review-ui-hub-money F1, ruling R9): ski days are blocked (LeisureSystem or CitizenBehaviorSystem off:
 *  SystemOff, so C# sends no ski-day state) and the state line would read "–": it shows the trip-planner "Off"
 *  note instead, so the block is not mistaken for "before the first pass". */
function skiDaysBlocked(l: ResortLive | null, trip: TripPlannerNote): trip is "knownOff" | "unknownOff" {
  return (trip === "knownOff" || trip === "unknownOff") && skiDayLine(l) === "unknown";
}

/** Round S1 D27 (ch4 B.2): the ski-day state line as ONE text node per branch (gate R13). N = the capacity
 *  at once (skiDaysCap, D21; ruling R-Q3 "Holds {N} skiers at once"). Round M2 R9 (fixer F1): the blocked
 *  case renders TripPlannerNoteText. */
function SkiDayLineText({ l, t, trip, tripName }: { l: ResortLive | null; t: Translate; trip: TripPlannerNote; tripName: string }): JSX.Element {
  if (skiDaysBlocked(l, trip)) return <TripPlannerNoteText note={trip} name={tripName} t={t} />;
  const N = numArg(l ? l.skiDaysCap : -1, Unit.Integer);
  switch (skiDayLine(l)) {
    case "off": return <>{t(HUB_LOC.skiDaysOff)}</>;
    case "signOff": return <>{t(HUB_LOC.skiDaysSignOff)}</>;
    case "noRun": return <>{t(HUB_LOC.skiDaysNoRun)}</>;
    case "noLodge": return <>{t(HUB_LOC.skiDaysNoLodge)}</>;
    case "night": return <>{t(HUB_LOC.skiDaysNight)}</>;
    case "full": return <>{t(HUB_LOC.skiDaysFull)}</>;
    case "roomRuns": return <LocText entry={HUB_FMT.skiDaysRoomRuns} args={{ N }} />;
    case "roomLodges": return <LocText entry={HUB_FMT.skiDaysRoomLodges} args={{ N }} />;
    case "room": return <LocText entry={HUB_FMT.skiDaysRoom} args={{ N }} />;
    default: return <>{DASH}</>;
  }
}

/** The state line's tooltip (ch4 B.2 last column); undefined = none. Blocked (fixer F1): TRIP_PLANNER_TIP. */
function skiDayLineTip(l: ResortLive | null, t: Translate, trip: TripPlannerNote): string | undefined {
  if (skiDaysBlocked(l, trip)) return t(HUB_LOC.tripPlannerTip);
  switch (skiDayTip(l)) {
    case "off": return t(HUB_LOC.skiDaysTipOff);
    case "noLodge": return t(HUB_LOC.skiDaysTipNoLodge);
    case "runs": return t(HUB_LOC.skiDaysTipRuns);
    case "lodges": return t(HUB_LOC.skiDaysTipLodges);
    default: return undefined;
  }
}

/** The last-month note of a tooltip (F7): partial -> GUESTS_LAST_PARTIAL, none -> GUESTS_LAST_NA, full -> nothing. */
function lastMonthNote(mark: LastMonthMark, t: Translate): string | false {
  return mark === "partial" ? t(HUB_LOC.guestsLastPartial) : mark === "none" ? t(HUB_LOC.guestsLastNa) : false;
}

/** The ticket note while tickets are NOT sold (Round F7: the "open" sentence lives in the header tooltip). */
function TicketNoteText({ note, t }: { note: TicketNote; t: Translate }): JSX.Element {
  switch (note) {
    case "noEfficiency": return <>{t(HUB_LOC.ticketsNoteNoEfficiency)}</>;
    case "nothingOpen": return <>{t(HUB_LOC.ticketsNoteNothingOpen)}</>;
    case "notKnownYet": return <>{t(HUB_LOC.ticketsNoteNotKnownYet)}</>;
    default: return <>{t(HUB_LOC.ticketsNoteOff)}</>;
  }
}

/** Round M2 R9 (enabled-flags.md C5): the trip-planner note as ONE text node per branch (gate R13); NAME = the C#
 *  label (CityStats.tripPlanner: a known mod's name, or names joined ", "). "none" renders nothing (never shown). */
function TripPlannerNoteText({ note, name, t }: { note: TripPlannerNote; name: string; t: Translate }): JSX.Element {
  switch (note) {
    case "known": return <LocText entry={HUB_FMT.tripPlannerNote} args={{ NAME: name }} />;
    case "knownOff": return <LocText entry={HUB_FMT.tripPlannerNoteOff} args={{ NAME: name }} />;
    case "unknown": return <>{t(HUB_LOC.tripPlannerNoteUnknown)}</>;
    case "unknownOff": return <>{t(HUB_LOC.tripPlannerNoteUnknownOff)}</>;
    default: return <></>;
  }
}

/** Round F7 D2: the GUESTS header with its Info glyph; the moved notes are its tooltip. */
function GuestsHeader({ l, city, note, t }: { l: ResortLive | null; city: CityStats; note: TicketNote | null; t: Translate }): JSX.Element {
  return (
    <InfoLine
      header
      info
      label={t(HUB_LOC.sectionGuests)}
      tip={<TipLines lines={[
        t(HUB_LOC.sectionGuests),
        t(HUB_LOC.guestsNoteWhere),
        !!l && l.thisMonthSinceLoad && t(HUB_LOC.guestsNoteSinceLoad),
        note === "open" && <LocText key="o" entry={HUB_FMT.ticketsNoteOpen} args={{ PRICE: numArg(city.ticketPrice, Unit.Money) }} />,
      ]} />}
    />
  );
}

function SignGuests({ l, city, samples, t }: { l: ResortLive | null; city: CityStats; samples: NumberSamples; t: Translate }): JSX.Element {
  // Round S1 F7: the last-month numbers show whenever C# sent them (>= 0), a since-load month marked "partial".
  const mark = lastMonthMark(l);
  const thisMonth = l ? l.guestsThisMonth : -1;
  const lastMonth = l && l.guestsLastMonth >= 0 ? l.guestsLastMonth : -1;
  // Round S1 (ch4 B.3): a fixed ladder (50, 100, 200, 500, ...) instead of the larger month.
  const scale = guestBarScale(thisMonth, lastMonth);
  // T77 (contract 9): the months by name, "Month of <name>" (city.month; last month = the one before, the ledger only rolls
  // to the next calendar month, a jump leaves last month unknown). The column heads carry the bare name: their cells are
  // the money sample's width, and the bars right above already say "Month of".
  const thisName = monthName(city.month, t);
  const lastName = monthName(city.month, t, 1);
  const thisLabel = <LocText entry={HUB_FMT.guestsMonthThis} args={{ MONTH: thisName }} />;
  const lastLabel = mark === "partial"
    ? <LocText entry={HUB_FMT.guestsMonthLastPartial} args={{ MONTH: lastName }} />
    : <LocText entry={HUB_FMT.guestsMonthLast} args={{ MONTH: lastName }} />;
  /** Bar label samples: the partial form of every month (always the longest), so the track keeps its width whatever the
   *  month or the mark (D33). */
  const barLabels = allMonthNames(t).map((n, i) => <LocText key={i} entry={HUB_FMT.guestsMonthLastPartial} args={{ MONTH: n }} />);
  const lastNote = lastMonthNote(mark, t);
  const scaleLine = <LocText key="s" entry={HUB_FMT.guestsBarScale} args={{ MAX: numArg(scale, Unit.Integer) }} />;
  /** A last-month value: the number when C# sent one (a whole or a partial month), else "–" (H10). */
  const last = (v: number | undefined, money?: boolean): JSX.Element | string =>
    v === undefined || v < 0 ? DASH : money ? <Num value={v} unit={Unit.Money} /> : countCell(v);
  const rowTip = (title: string, body: string | JSX.Element): JSX.Element =>
    <TipLines lines={[title, body, lastNote]} />;
  const note = l ? ticketNote(l.ticketGate, city.ticketPrice) : null;
  // Round M2 R9: the trip-planner note ("none" in a base-game city: no line).
  const tripNote = tripPlannerNote(city);
  const tripName = typeof city.tripPlanner === "string" ? city.tripPlanner : "";
  // Round S1 D27 (ch4 B.6): the Ski days row's tooltip; skipped lines are false.
  const skiDaysTip = (
    <TipLines lines={[
      t(HUB_LOC.guestsRowSkiDays),
      t(HUB_LOC.guestsTipSkiDays),
      // T80 (Richard item 1): dispatch and arrival in one line, the split of the sent skiers under it.
      !!l && l.skiDaysSent >= 0 && <LocText key="sent" entry={HUB_FMT.skiDaysTipSent} args={{
        MONTH: thisName, SENT: numArg(l.skiDaysSent, Unit.Integer), ARRIVED: numArg(l.skiDaysThisMonth, Unit.Integer),
      }} />,
      !!l && l.skiDaysSent >= 0 && <LocText key="split" entry={HUB_FMT.skiDaysTipSplit} args={{
        WALK: numArg(skiDayWalk(l), Unit.Integer), CAR: numArg(l.skiDaysSentCar, Unit.Integer), FAR: numArg(l.skiDaysSentNoCar, Unit.Integer),
      }} />,
      !!l && l.skiDaysCap >= 0 && l.skiDaysNow >= 0 && <LocText key="now" entry={HUB_FMT.skiDaysTipNow} args={{
        NOW: numArg(l.skiDaysNow, Unit.Integer), CAP: numArg(l.skiDaysCap, Unit.Integer),
      }} />,
      lastNote,
    ]} />
  );

  return (
    <>
      <GuestsHeader l={l} city={city} note={note} t={t} />
      <GuestBar label={thisLabel} labels={barLabels} value={thisMonth} max={scale} samples={samples}
        tip={<TipLines lines={[thisLabel, scaleLine]} />} />
      <GuestBar label={lastLabel} labels={barLabels} value={lastMonth} max={scale} samples={samples}
        tip={<TipLines lines={[lastLabel, lastNote, scaleLine]} />} />
      <Divider />
      <div className={styles.colHeads}>
        <div className={styles.colHeadSpacer} />
        {/* E1 fixer (F14): heads measured in the row font (S), labels in XS - aligned with GuestRow's cells. */}
        <StableWidth samples={samples.money} align="end" className={cx(styles.valueCell, styles.colHeadCell)}>
          <span className={styles.colLabel}>{thisName}</span>
        </StableWidth>
        <StableWidth samples={samples.money} align="end" className={cx(styles.valueCell, styles.colHeadCell)}>
          <span className={styles.colLabel}>{lastName}</span>
        </StableWidth>
      </div>
      {/* T07: group 1, the people at the lodges (open or not), and the note that not all of them ski. */}
      <InfoLine header label={t(HUB_LOC.guestsGroupLodges)} />
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
      <SubLine>{t(HUB_LOC.guestsNoteNeverSki)}</SubLine>
      {/* Round S1 D27: the Ski days row and its one state line (T07: group 2). */}
      <InfoLine header label={t(HUB_LOC.guestsGroupSent)} />
      <GuestRow
        label={t(HUB_LOC.guestsRowSkiDays)} samples={samples}
        thisMonth={countCell(l ? l.skiDaysThisMonth : -1)} lastMonth={last(l?.skiDaysLastMonth)}
        tip={skiDaysTip}
      />
      <SubLine tip={skiDayLineTip(l, t, tripNote)}><SkiDayLineText l={l} t={t} trip={tripNote} tripName={tripName} /></SubLine>
      {/* T07: group 3, what the gate charged. */}
      <InfoLine header label={t(HUB_LOC.guestsGroupTickets)} />
      <GuestRow
        label={t(HUB_LOC.guestsRowPaid)} samples={samples}
        thisMonth={countCell(l ? l.paidThisMonth : -1)} lastMonth={last(l?.paidLastMonth)}
        tip={rowTip(t(HUB_LOC.guestsRowPaid), <LocText entry={HUB_FMT.guestsTipPaid} args={{
          MONTH: thisName, GUESTS: countCell(l ? l.guestsThisMonth : -1), PAID: countCell(l ? l.paidThisMonth : -1),
          HOLDERS: countCell(l ? l.passHoldersThisMonth : -1),
          UNPAID: countCell(l ? l.unpaidThisMonth : -1), FREE: countCell(l ? l.freeThisMonth : -1),
        }} />)}
      />
      {passesShown(l, city) && (
        <GuestRow
          label={t(HUB_LOC.guestsRowPassHolders)} samples={samples}
          thisMonth={countCell(l ? l.passHoldersThisMonth : -1)} lastMonth={last(l?.passHoldersLastMonth)}
          tip={rowTip(t(HUB_LOC.guestsRowPassHolders), t(HUB_LOC.guestsTipPassHolders))}
        />
      )}
      {lessonsShown(l) && (
        <GuestRow
          label={t(HUB_LOC.guestsRowLessons)} samples={samples}
          thisMonth={countCell(l ? l.lessonsThisMonth : -1)} lastMonth={last(l?.lessonsLastMonth)}
          tip={rowTip(t(HUB_LOC.guestsRowLessons), t(HUB_LOC.guestsTipLessons))}
        />
      )}
      <GuestRow
        label={t(HUB_LOC.guestsRowTickets)} samples={samples}
        thisMonth={l && l.ticketsThisMonth >= 0 ? <Num value={l.ticketsThisMonth} unit={Unit.Money} /> : DASH}
        lastMonth={last(l?.ticketsLastMonth, true)}
        tip={rowTip(t(HUB_LOC.guestsRowTickets), t(HUB_LOC.guestsTipTickets))}
      />
      <Divider />
      {city.tourismOverhaul ? (
        <Tip tip={t(HUB_LOC.estToTip)}>
          <div className={styles.noteArea}>
            <div className={styles.note1}>{t(HUB_LOC.estToNote)}</div>
          </div>
        </Tip>
      ) : (
        <InfoLine
          label={t(HUB_LOC.estRowTourists)}
          value={l && l.estKnown ? <Num value={l.estTourists} unit={Unit.Integer} /> : DASH}
          tip={t(HUB_LOC.estTouristsTip)}
        />
      )}
      {tripNote !== "none" && (
        <Tip tip={<TipLines lines={[<TripPlannerNoteText key="n" note={tripNote} name={tripName} t={t} />, t(HUB_LOC.tripPlannerTip)]} />}>
          <div className={styles.noteArea}>
            <div className={styles.note1}><TripPlannerNoteText note={tripNote} name={tripName} t={t} /></div>
          </div>
        </Tip>
      )}
      <Divider />
      <div className={styles.noteArea}>
        {note !== null && note !== "open" ? (
          <Tip tip={<TicketNoteText note={note} t={t} />}>
            <div className={styles.note1}><TicketNoteText note={note} t={t} /></div>
          </Tip>
        ) : (
          <div className={styles.note1}>{note === null ? DASH : ""}</div>
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
