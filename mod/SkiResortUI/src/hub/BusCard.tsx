/*
 * src/hub/BusCard.tsx - the Ski Bus card beside the game's selected-info panel, for a selected coach or depot.
 * It is always mounted at the "Game" hook, so it reads only busCard$, never a panel binding. A section inside the game's panel would need
 * moduleRegistry.extend and an internal game-ui path, which the gate bans. The card shows facts only: pointer-events stay off, so it never takes a click.
 * Engine notes: EngineNotes/SkiResortHubUISystem.md, Near HubContract, 26.
 */
import { useValue } from "cs2/api";
import { Unit } from "cs2/l10n";
import { busCard$ } from "../bindings";
import type { BusCardStateName } from "../bindings";
import { useT } from "../l10n";
import type { LocEntry } from "../l10n";
import styles from "./hub.module.scss";
import { HUB_LOC } from "./hubText";
import { InfoLine, Num, numArg } from "./parts";

const NOT_RUNNING: Readonly<Partial<Record<BusCardStateName, LocEntry>>> = {
  noRoad: HUB_LOC.busCardNoRoad,
  noHillLodge: HUB_LOC.busCardNoHillLodge,
  off: HUB_LOC.busCardOff,
};

export function BusCard(): JSX.Element | null {
  const t = useT();
  const card = useValue(busCard$);
  if (!card.shown) return null;
  const notRunning = NOT_RUNNING[card.state];
  return (
    <div className={styles.busCard}>
      <div className={styles.busCardTitle}>{t(HUB_LOC.busCardTitle)}</div>
      {card.area !== "" && <InfoLine label={t(HUB_LOC.resortCaption)} value={card.area} />}
      {notRunning && <InfoLine label={t(HUB_LOC.busCardStatus)} value={t(notRunning)} />}
      <InfoLine label={t(HUB_LOC.busCardGuests)} value={numArg(card.guestsMonth, Unit.Integer)} />
      <InfoLine label={t(HUB_LOC.busCardBill)} value={<Num value={card.billMonth} unit={Unit.MoneyPerMonth} />} />
      {card.distanceM > 0 && <InfoLine label={t(HUB_LOC.busCardDistance)} value={<Num value={card.distanceM} unit={Unit.Length} />} />}
    </div>
  );
}
