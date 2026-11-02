/*
 * src/hub/GuestsView.tsx - the Guests view: arrivals this month and last month, the game-formula tourist estimate, and the notes on what
 * is counted and what tickets do. Two bars on one scale (hubRules.guestBarScale, integer flex-grow via guestBarParts); the table is three
 * Folds (Lodges, Sent, Tickets) whose populations differ by design. Fixed slots only (StableWidth cells, fixed-height note slots), so a
 * state change moves nothing. Last month is never a fake 0 (hubRules.lastMonthMark): "full" = a whole month, "partial" = counted since the
 * load, "none" = -1 shown as "–". Under the folds, always shown: one car space row per active road lodge of the area (lodgeParking$;
 * none for the Hut, the Summit or an off-road lodge), numbered per tier while the area has two of a tier, or named as the player renamed
 * the lodge in the game; a click flies to the lodge.
 * Panel file: subscribes resorts$, selectedResort$, resortLive$, city$, lodgeParking$.
 */
import { useValue } from "cs2/api";
import { Unit } from "cs2/l10n";
import { Button } from "cs2/ui";
import { city$, hubTriggers, lodgeParking$, resortLive$, resorts$, sameEntity, selectedResort$ } from "../bindings";
import type { BuildItemId, CityStats, LodgeParkingRow, ResortLive } from "../bindings";
import { useT } from "../l10n";
import type { Translate } from "../l10n";
import {
  guestBarParts, guestBarScale, lastMonthMark, lessonsShown, liveOf, passesShown, skiDayLine, skiDayTip, skiDayWalk, ticketNote, tripPlannerNote,
} from "./hubRules";
import type { LastMonthMark, TicketNote, TripPlannerNote } from "./hubRules";
import { HUB_FMT, HUB_LOC, LODGE_TIER, allMonthNames, monthName } from "./hubText";
import {
  Divider, FlexBar, InfoLine, LocText, Num, StableWidth, SubLine, Tip, TipLines, cx, numArg, useNumberSamples,
} from "./parts";
import type { NumberSamples } from "./parts";
import { currentResort, resortEntries } from "./resortData";
import { Fold } from "./fit";
import styles from "./hub.module.scss";

const DASH = "–";

/* A count cell: numArg (Integer) or "–" for -1. */
function countCell(v: number): JSX.Element | string {
  return numArg(v, Unit.Integer);
}

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

/*
 * Ski days are blocked (LeisureSystem or CitizenBehaviorSystem off: SystemOff, so C# sends no ski-day state) and the state
 * line would read "–": it shows the trip-planner "Off" note instead, so the block is not mistaken for "before the first pass".
 */
function skiDaysBlocked(l: ResortLive | null, trip: TripPlannerNote): trip is "knownOff" | "unknownOff" {
  return (trip === "knownOff" || trip === "unknownOff") && skiDayLine(l) === "unknown";
}

/*
 * The ski-day state line as ONE text node per branch. N = the capacity at once (skiDaysCap). The blocked case renders
 * TripPlannerNoteText.
 */
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

/* The state line's tooltip; undefined = none. Blocked: TRIP_PLANNER_TIP. */
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

/* The last-month note of a tooltip: partial -> GUESTS_LAST_PARTIAL, none -> GUESTS_LAST_NA, full -> nothing. */
function lastMonthNote(mark: LastMonthMark, t: Translate): string | false {
  return mark === "partial" ? t(HUB_LOC.guestsLastPartial) : mark === "none" ? t(HUB_LOC.guestsLastNa) : false;
}

/* The ticket note while tickets are not sold (the "open" sentence lives in the header tooltip). */
function TicketNoteText({ note, t }: { note: TicketNote; t: Translate }): JSX.Element {
  switch (note) {
    case "noEfficiency": return <>{t(HUB_LOC.ticketsNoteNoEfficiency)}</>;
    case "nothingOpen": return <>{t(HUB_LOC.ticketsNoteNothingOpen)}</>;
    case "notKnownYet": return <>{t(HUB_LOC.ticketsNoteNotKnownYet)}</>;
    default: return <>{t(HUB_LOC.ticketsNoteOff)}</>;
  }
}

/*
 * The trip-planner note as ONE text node per branch; NAME = the C# label (CityStats.tripPlanner: a known mod's name, or names
 * joined ", "). "none" renders nothing.
 */
function TripPlannerNoteText({ note, name, t }: { note: TripPlannerNote; name: string; t: Translate }): JSX.Element {
  switch (note) {
    case "known": return <LocText entry={HUB_FMT.tripPlannerNote} args={{ NAME: name }} />;
    case "knownOff": return <LocText entry={HUB_FMT.tripPlannerNoteOff} args={{ NAME: name }} />;
    case "unknown": return <>{t(HUB_LOC.tripPlannerNoteUnknown)}</>;
    case "unknownOff": return <>{t(HUB_LOC.tripPlannerNoteUnknownOff)}</>;
    default: return <></>;
  }
}

/*
 * One row per active road lodge of the area (LodgeParkingRow, C# order): "Car spaces at {NAME}", the count or "–" before the first
 * census. {NAME} is the tier word, or "Chalet 2" (LODGE_TIER_NUMBERED) while the area has two or more of that tier: the number is
 * C#'s, persisted on the lodge, so it stays the same across saves. A lodge the player renamed in the game shows that name instead
 * (contract 31, `name`; C# cleans it to one line of at most 64 characters, the game's rename field length): a plain string argument,
 * never an element (gate R19), in the player-name label class (ellipsis, as the game cuts long names in its own lists; the layout test
 * allows it), and the whole row text leads the tooltip. A Button like a Fold row: a click flies to the lodge. It has no chevron, so
 * lodgeRow puts its text at the plain rows' indent (10rem, not the fold's 4rem). No road lodge: no row.
 */
function LodgeParkingLines({ rows, samples, t }: { rows: readonly LodgeParkingRow[]; samples: NumberSamples; t: Translate }): JSX.Element {
  return (
    <>
      {rows.map((r) => {
        const tier = LODGE_TIER[r.item as BuildItemId];
        const custom = r.name !== "";
        const name = custom ? r.name
          : !tier ? DASH
            : r.number > 0 ? <LocText entry={HUB_FMT.lodgeTierNumbered} args={{ NAME: t(tier), N: numArg(r.number, Unit.Integer) }} />
              : t(tier);
        const tip = custom
          ? <TipLines lines={[<LocText key="n" entry={HUB_FMT.guestsRowCarSpaces} args={{ NAME: r.name }} />, t(HUB_LOC.guestsTipCarSpaces)]} />
          : t(HUB_LOC.guestsTipCarSpaces);
        return (
          <Tip key={`${r.lodge.index}:${r.lodge.version}`} tip={tip}>
            <Button theme={{ button: cx(styles.infoLine, styles.foldRow, styles.lodgeRow) }} onSelect={() => hubTriggers.focusBuilding(r.lodge)}>
              <div className={custom ? styles.lodgeNameLabel : styles.infoLabel}><LocText entry={HUB_FMT.guestsRowCarSpaces} args={{ NAME: name }} /></div>
              <StableWidth samples={samples.count} align="end">{countCell(r.carSpaces)}</StableWidth>
            </Button>
          </Tip>
        );
      })}
    </>
  );
}

/* The car space total of the area's road lodges; "–" while none is counted yet (carSpaces -1). */
function parkingTotal(rows: readonly LodgeParkingRow[]): JSX.Element | string {
  const known = rows.filter((r) => r.carSpaces >= 0);
  return known.length ? countCell(known.reduce((s, r) => s + r.carSpaces, 0)) : DASH;
}

/* The GUESTS header with its Info glyph; the notes are its tooltip. */
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

function SignGuests({ l, city, parking, samples, t }: {
  l: ResortLive | null; city: CityStats; parking: readonly LodgeParkingRow[]; samples: NumberSamples; t: Translate;
}): JSX.Element {
  // The last-month numbers show whenever C# sent them (>= 0), a since-load month marked "partial".
  const mark = lastMonthMark(l);
  const thisMonth = l ? l.guestsThisMonth : -1;
  const lastMonth = l && l.guestsLastMonth >= 0 ? l.guestsLastMonth : -1;
  // A fixed ladder (50, 100, 200, 500, ...) instead of the larger month.
  const scale = guestBarScale(thisMonth, lastMonth);
  // The months by name (city.month; last month = the one before: the ledger only rolls to the next calendar month, a jump
  // leaves last month unknown). Bars, column heads and the row tooltips all carry the bare name (the column cells are the money
  // sample's width).
  const thisName = monthName(city.month, t);
  const lastName = monthName(city.month, t, 1);
  const thisLabel = <LocText entry={HUB_FMT.guestsMonthThis} args={{ MONTH: thisName }} />;
  const lastLabel = mark === "partial"
    ? <LocText entry={HUB_FMT.guestsMonthLastPartial} args={{ MONTH: lastName }} />
    : <LocText entry={HUB_FMT.guestsMonthLast} args={{ MONTH: lastName }} />;
  /*
   * Bar label samples: the partial form of every month (always the longest), so the track keeps its width whatever the month
   * or the mark.
   */
  const barLabels = allMonthNames(t).map((n, i) => <LocText key={i} entry={HUB_FMT.guestsMonthLastPartial} args={{ MONTH: n }} />);
  const lastNote = lastMonthNote(mark, t);
  const scaleLine = <LocText key="s" entry={HUB_FMT.guestsBarScale} args={{ MAX: numArg(scale, Unit.Integer) }} />;
  /* A last-month value: the number when C# sent one (a whole or a partial month), else "–". */
  const last = (v: number | undefined, money?: boolean): JSX.Element | string =>
    v === undefined || v < 0 ? DASH : money ? <Num value={v} unit={Unit.Money} /> : countCell(v);
  const rowTip = (title: string, body: string | JSX.Element): JSX.Element =>
    <TipLines lines={[title, body, lastNote]} />;
  const note = l ? ticketNote(l.ticketGate, city.ticketPrice) : null;
  // The trip-planner note ("none" in a base-game city: no line).
  const tripNote = tripPlannerNote(city);
  const tripName = typeof city.tripPlanner === "string" ? city.tripPlanner : "";
  // The Ski days row's tooltip; skipped lines are false.
  const skiDaysTip = (
    <TipLines lines={[
      t(HUB_LOC.guestsRowSkiDays),
      t(HUB_LOC.guestsTipSkiDays),
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
        {/* heads measured in the row font (S), labels in XS - aligned with GuestRow's cells. */}
        <StableWidth samples={samples.money} align="end" className={cx(styles.valueCell, styles.colHeadCell)}>
          <span className={styles.colLabel}>{thisName}</span>
        </StableWidth>
        <StableWidth samples={samples.money} align="end" className={cx(styles.valueCell, styles.colHeadCell)}>
          <span className={styles.colLabel}>{lastName}</span>
        </StableWidth>
      </div>
      {/* group 1: the people at the lodges (open or not), and the note that not all of them ski. */}
      <Fold view="guests" id="lodges" header label={t(HUB_LOC.guestsGroupLodges)} tip={t(HUB_LOC.guestsNoteNeverSki)}>
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
      </Fold>
      {/* group 2: the Ski days row and its state line. */}
      <Fold view="guests" id="sent" header label={t(HUB_LOC.guestsGroupSent)}>
      <GuestRow
        label={t(HUB_LOC.guestsRowSkiDays)} samples={samples}
        thisMonth={countCell(l ? l.skiDaysThisMonth : -1)} lastMonth={last(l?.skiDaysLastMonth)}
        tip={skiDaysTip}
      />
      <SubLine tip={skiDayLineTip(l, t, tripNote)}><SkiDayLineText l={l} t={t} trip={tripNote} tripName={tripName} /></SubLine>
      </Fold>
      {/* group 3: what the gate charged. */}
      <Fold view="guests" id="tickets" header label={t(HUB_LOC.guestsGroupTickets)}>
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
      </Fold>
      <Divider />
      {/* group 4: the car spaces, one row per road lodge under one header whose value is their total ("–" before the first count). */}
      {parking.length > 0 && (
        <Fold view="guests" id="parking" header label={t(HUB_LOC.guestsGroupCarSpaces)} tip={t(HUB_LOC.guestsTipCarSpacesAll)}
          value={parkingTotal(parking)}>
          <LodgeParkingLines rows={parking} samples={samples} t={t} />
        </Fold>
      )}
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
      {/* The ticket note has no tooltip: it would only repeat the note, which wraps instead of being cut (2026-11-02, DECISIONS 568). */}
      <div className={styles.noteArea}>
        {note !== null && note !== "open" ? (
          <div className={styles.note1}><TicketNoteText note={note} t={t} /></div>
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
  const parking = useValue(lodgeParking$);

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
  const sign = cur.row.sign;
  const rows = parking.filter((r) => sameEntity(r.sign, sign));
  return <SignGuests l={liveOf(live, sign)} city={city} parking={rows} samples={samples} t={t} />;
}
