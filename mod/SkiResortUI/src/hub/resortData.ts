/*
 * src/hub/resortData.ts - which resort the hub shows, and which rows belong to it. Pure functions (no React), called on each render.
 * Switcher entries: the sign rows (kind 0) in C#'s order. Then the "Not linked" row (kind 1), only when it has any run, lift row or
 * lodge. The current entry is the row whose sign equals C#'s selectedResort (Entity.Null = Not linked, which C# always sends).
 * While the selection names no row, the UI shows the first entry. The UI never "fixes" the selection by sending one of its own.
 * List rows of the selected resort: `sameEntity(row.sign, selected)`.
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

/** What selectResort takes for an entry: its sign, Null for the Not linked row. */
export function entrySign(row: ResortRow): Entity {
  return row.kind === 1 ? { index: 0, version: 0 } : row.sign;
}

/** A stable key per entry: "index:version", "unlinked" for the Not linked row. */
export function entryKey(row: ResortRow): string {
  return row.kind === 1 ? "unlinked" : `${row.sign.index}:${row.sign.version}`;
}

/** The sign (or Null) of the entry `step` places after the current one. The list wraps at its ends. */
export function stepResort(e: ResortEntries, cur: CurrentResort, step: number): Entity | null {
  const n = e.entries.length;
  if (n < 2) return null;
  const from = cur.index < 0 ? 0 : cur.index;
  return entrySign(e.entries[(((from + step) % n) + n) % n]);
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
