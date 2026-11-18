/*
 * src/hub/SyncCard.tsx - Sync Resort (contract 51, T229, DECISIONS 670): the Overview's "Sync resort (N)" button and the Sync card.
 * The button shows only while the shown area has objects Sync can bring up to date (sync$.areas). It opens the card in place of the
 * Overview's sections (syncCardOpen$). The card lists the area's objects: a check box (unchecked = skipped by the next Sync), the name and
 * one sentence of what changes (SYNC_STEP), the state while queued or after a failure, "Keep as is" per row. The row is one .planRow toggle
 * (r2/hub-ui's control scale: only scale classes on a Button). One Sync button for the checked rows; Sync is free and has no undo (the
 * title's tooltip says so).
 * While the card is mounted, C# draws a ring around each listed object (syncCard trigger: open on mount, closed on unmount). A view switch,
 * an area switch or closing the hub unmounts it.
 * The card decides nothing: the list, states and counts are C#'s (SkiSyncSystem). Panel file: subscribes sync$.
 * Engine notes: EngineNotes/SkiSyncSystem.md
 */
import { useEffect } from "react";
import { useValue } from "cs2/api";
import { Button } from "cs2/ui";
import { FOCUS_DISABLED } from "cs2/input";
import { Unit } from "cs2/l10n";
import type { Entity } from "cs2/utils";
import { SYNC_STATE, hubTriggers, isNullEntity, sameEntity, sync$ } from "../bindings";
import type { SyncRow, SyncState } from "../bindings";
import { useT } from "../l10n";
import type { LocEntry } from "../l10n";
import { HUB_FMT, HUB_LOC } from "./hubText";
import { syncCardOpen$ } from "./hubState";
import { CheckBox, LocText, Tip, cx, numArg } from "./parts";
import styles from "./hub.module.scss";

/** One sentence per SkiSyncCatalog step number: what Sync changes. */
export const SYNC_STEP: Readonly<Record<number, LocEntry>> = {
  1: HUB_LOC.syncStepPatrolParts,
};

/** The row state's word (none while it waits for the player). */
const STATE_WORD: Readonly<Record<number, LocEntry>> = {
  [SYNC_STATE.Queued]: HUB_LOC.syncStateQueued,
  [SYNC_STATE.Working]: HUB_LOC.syncStateWorking,
  [SYNC_STATE.Failed]: HUB_LOC.syncStateFailed,
  [SYNC_STATE.HandEdited]: HUB_LOC.syncStateHandEdited,
};

function sameArea(a: Entity, sign: Entity | null): boolean {
  return sign ? sameEntity(a, sign) : isNullEntity(a);
}

/** Objects of the area Sync can bring up to date (the button's N). */
export function syncCountOf(state: SyncState | null | undefined, sign: Entity | null): number {
  if (!state || !state.ready) return 0;
  for (const a of state.areas) if (sameArea(a.sign, sign)) return a.n | 0;
  return 0;
}

/** Rows the next Sync would start: checked and waiting (or failed before). */
function checkedCount(rows: readonly SyncRow[]): number {
  let n = 0;
  for (const r of rows) if (r.include && (r.state === SYNC_STATE.Open || r.state === SYNC_STATE.Failed)) n++;
  return n;
}

/** "Sync resort (N)": one action line at the top of the Overview, only while N > 0. */
export function SyncLine({ sign }: { sign: Entity | null }): JSX.Element | null {
  const t = useT();
  const state = useValue(sync$);
  const n = syncCountOf(state, sign);
  if (n === 0) return null;
  return (
    <div className={styles.syncLine}>
      <Tip tip={t(HUB_LOC.syncTip)}>
        <Button focusKey={FOCUS_DISABLED} variant="flat" className={cx(styles.actionButton, styles.fitText)} onSelect={() => syncCardOpen$.update(true)}>
          <LocText entry={HUB_FMT.syncButton} args={{ N: numArg(n, Unit.Integer) }} />
        </Button>
      </Tip>
    </div>
  );
}

/** The Sync card for the shown area. Mounted only while syncCardOpen$ is true. */
export function SyncCard({ sign }: { sign: Entity | null }): JSX.Element {
  const t = useT();
  const state = useValue(sync$);
  const wireSign: Entity = sign ?? { index: 0, version: 0 };
  // Open on mount, closed on unmount: C# draws the world rings only in between
  useEffect(() => {
    hubTriggers.syncCard(wireSign, true);
    return () => hubTriggers.syncCard(wireSign, false);
  }, [wireSign.index, wireSign.version]);

  const rows = state.open && sameArea(state.sign, sign) ? state.rows : [];
  const checked = checkedCount(rows);
  const close = (): void => syncCardOpen$.update(false);
  return (
    <div className={styles.syncCard}>
      <Tip tip={t(HUB_LOC.syncTip)}>
        <div className={styles.syncTitle}>{t(HUB_LOC.syncTitle)}</div>
      </Tip>
      {rows.length === 0 && <div className={styles.dimLine}>{t(HUB_LOC.syncEmpty)}</div>}
      {rows.length > 0 && <div className={styles.syncNote}>{t(HUB_LOC.syncRings)}</div>}
      {rows.map((r) => {
        const step = SYNC_STEP[r.step];
        const word = STATE_WORD[r.state];
        const canPick = r.state === SYNC_STATE.Open || r.state === SYNC_STATE.Failed;
        return (
          <div key={`${r.entity.index}:${r.entity.version}`} className={styles.syncRow}>
            <div className={styles.syncWhat}>
              <Tip tip={t(HUB_LOC.syncIncludeTip)}>
                <Button focusKey={FOCUS_DISABLED} theme={{ button: cx(styles.planRow, !(r.include && canPick) && styles.planRowOff) }}
                  disabled={!canPick} selected={r.include && canPick} onSelect={() => hubTriggers.syncInclude(r.entity, !r.include)}>
                  <CheckBox on={r.include && canPick} />
                  <div className={styles.syncText}>
                    <div className={styles.syncName}>{r.name}</div>
                    <div className={styles.syncSentence}>{step ? t(step) : ""}</div>
                    {word && <div className={cx(styles.syncState, r.state === SYNC_STATE.Failed && styles.syncStateBad)}>{t(word)}</div>}
                  </div>
                </Button>
              </Tip>
            </div>
            {canPick && (
              <div className={styles.syncKeep}>
                <Tip tip={t(HUB_LOC.syncKeepTip)}>
                  <Button focusKey={FOCUS_DISABLED} variant="flat" className={cx(styles.actionButton, styles.fitText)}
                    onSelect={() => hubTriggers.syncKeep(r.entity)}>
                    {t(HUB_LOC.syncKeep)}
                  </Button>
                </Tip>
              </div>
            )}
          </div>
        );
      })}
      {state.total > 0 && (
        <div className={styles.syncNote}>
          <LocText entry={HUB_FMT.syncProgress} args={{ DONE: numArg(state.done, Unit.Integer), TOTAL: numArg(state.total, Unit.Integer) }} />
          {state.failed > 0 && <> · <LocText entry={HUB_FMT.syncFailed} args={{ N: numArg(state.failed, Unit.Integer) }} /></>}
        </div>
      )}
      <div className={styles.syncActions}>
        <Button focusKey={FOCUS_DISABLED} variant="flat" className={cx(styles.actionButton, styles.fitText)} disabled={checked === 0 || state.running}
          onSelect={() => hubTriggers.syncRun(wireSign)}>
          <LocText entry={HUB_FMT.syncRun} args={{ N: numArg(checked, Unit.Integer) }} />
        </Button>
        <div className={styles.syncGap}>
          <Button focusKey={FOCUS_DISABLED} variant="flat" className={cx(styles.actionButton, styles.fitText)} onSelect={close}>
            {t(HUB_LOC.syncClose)}
          </Button>
        </div>
      </div>
    </div>
  );
}
