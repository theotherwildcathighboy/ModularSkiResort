/*
 * src/hub/resortData.ts - which resort the hub shows, and which rows belong to it.
 *
 * Written for this repo (MIT). Created 2026-10-03 (Round U2, implementer I-3); contract 3 on
 * 2026-10-04 (Round E1, stream S6, econ-round spec ch6 section 6.14: the Not linked row counts its
 * census rows `liftRows`; `lodgesOf` and LodgeRow are gone with the `lodges` binding).
 * Authority: docs/research/hub-u2-spec.md section 4.3 (switcher entries), 4.2 (row filters),
 * 3.2 (C# owns the selected resort; the UI only asks with selectResort).
 * Contract:
 *   - Switcher entries: the sign rows (kind 0) in C#'s order, then the "Not linked" row (kind 1)
 *     ONLY when it has any run, lift row or lodge (spec 4.3). No "All resorts" entry (OD-15).
 *   - The current entry is the row whose sign equals C#'s selectedResort (Entity.Null = the Not
 *     linked row, which C# always sends). While C#'s selection names no row (one push apart, or a
 *     sign that just died) the first entry is shown; C# corrects the selection itself (spec 3.2
 *     rules 5-6) and the UI never "fixes" it by sending a selection of its own.
 *   - List rows of the selected resort: `sameEntity(row.sign, selected)` - Null selects the rows
 *     that belong to no resort (spec 4.2 RunTable / LiftTable).
 *   - Pure functions (no React) so they stay cheap: called on each render with arrays that C#
 *     replaces only when their revision moved (RawValueBinding, spec 2.2 push rules).
 */
import type { Entity } from "cs2/utils";
import { sameEntity } from "../bindings";
import type { LiftRow, ResortRow, RunRow } from "../bindings";

export interface ResortEntries {
  /** Every sign row (kind 0), C#'s order. */
  signs: ResortRow[];
  /** The Not linked row (kind 1), or null when C# sent none. */
  unlinked: ResortRow | null;
  /** What the switcher cycles through. */
  entries: ResortRow[];
}

/** Does the Not linked row hold anything to show? */
export function unlinkedHasItems(row: ResortRow | null | undefined): boolean {
  if (!row) return false;
  return (row.runs | 0) + (row.liftRows | 0) + (row.lodges | 0) + (row.freeLodges | 0) > 0;
}

export function resortEntries(resorts: readonly ResortRow[] | null | undefined): ResortEntries {
  const signs: ResortRow[] = [];
  let unlinked: ResortRow | null = null;
  for (const r of resorts ?? []) {
    if (!r) continue;
    if (r.kind === 1) unlinked = r;
    else signs.push(r);
  }
  const entries = signs.slice();
  if (unlinked && unlinkedHasItems(unlinked)) entries.push(unlinked);
  return { signs, unlinked, entries };
}

export interface CurrentResort {
  /** The row shown (null: nothing to show). */
  row: ResortRow | null;
  /** Its index in `entries`, or -1 when it is not an entry (an empty Not linked row). */
  index: number;
  /** The row shown is the Not linked row. */
  isUnlinked: boolean;
}

/** The row C#'s selection points at (see header), with its switcher position. */
export function currentResort(e: ResortEntries, selected: Entity | null | undefined): CurrentResort {
  for (let i = 0; i < e.entries.length; i++) {
    const r = e.entries[i];
    if (r.kind === 1 ? sameEntity(null, selected) : sameEntity(r.sign, selected)) {
      return { row: r, index: i, isUnlinked: r.kind === 1 };
    }
  }
  if (sameEntity(null, selected) && e.unlinked) return { row: e.unlinked, index: -1, isUnlinked: true };
  if (e.entries.length) return { row: e.entries[0], index: 0, isUnlinked: e.entries[0].kind === 1 };
  return { row: null, index: -1, isUnlinked: false };
}

/** The sign (or Null) of the entry `step` places after the current one, wrapping around. */
export function stepResort(e: ResortEntries, cur: CurrentResort, step: number): Entity | null {
  const n = e.entries.length;
  if (n < 2) return null;
  const from = cur.index < 0 ? 0 : cur.index;
  const to = e.entries[(((from + step) % n) + n) % n];
  return to.kind === 1 ? { index: 0, version: 0 } : to.sign;
}

export function runsOf(rows: readonly RunRow[] | null | undefined, selected: Entity | null | undefined): RunRow[] {
  const out: RunRow[] = [];
  for (const r of rows ?? []) if (r && sameEntity(r.sign, selected)) out.push(r);
  return out;
}

export function liftsOf(rows: readonly LiftRow[] | null | undefined, selected: Entity | null | undefined): LiftRow[] {
  const out: LiftRow[] = [];
  for (const r of rows ?? []) if (r && sameEntity(r.sign, selected)) out.push(r);
  return out;
}
