/*
 * src/hub/Slider.tsx - the hub's value slider (r3/sliders). Written for this repo (MIT). It replaces the stepper buttons of the ticket
 * price, the season pass, a run's snow gun and light spacing and the tree density.
 * The values are a list of stops in screen order, left to right, evenly spaced on the track. A list can be uneven in value (the
 * spacing ladder) or run high to low (spacing: Off, 200 m ... 20 m, so right is always "more").
 * Input: drag the thumb, or click the track (the thumb jumps there, a no-drag way in). No keyboard (the arrows move the game camera) and
 * no wheel (it scrolls the Runs view, and a wheel step on a run re-emits it).
 * C# writes: onCommit once, on release, and only when the stop changed. onShown gets the value the slider shows (each stop while
 * dragging, then the asked value, then C#'s), so a text line can show it and its effect live (the demand next to the price) with no trigger. After a commit the slider shows the asked value until C# sends it
 * back or PENDING_MS passes (C# refused or did not answer), as the old price stepper did (T240).
 * Gameface: no <input type=range> (not drawn there). The thumb and fill sit by percent inside a fixed box, so a value change never moves
 * the layout. The track box is read once at mouse down (a size read lags one drawn frame, but the row does not move during a drag).
 */
import { useEffect, useRef, useState } from "react";
import type { ReactNode } from "react";
import { StableWidth, Tip, cx } from "./parts";
import styles from "./hub.module.scss";

const PENDING_MS = 3000;

/** Where a value sits on the track, 0..1. A value on a stop sits on it. A value between two neighbor stops (a kind's default such as
 *  90 m between 100 and 80) sits in between, by its value. -1 = not on the track (unknown). */
export function sliderPos(stops: readonly number[], value: number): number {
  const n = stops.length;
  if (n < 2 || !Number.isFinite(value)) return -1;
  for (let i = 0; i < n; i++) if (stops[i] === value) return i / (n - 1);
  for (let i = 0; i + 1 < n; i++) {
    const a = stops[i], b = stops[i + 1];
    if ((value - a) * (value - b) < 0) return (i + (value - a) / (b - a)) / (n - 1);
  }
  return -1;
}

/** The stop nearest to a track fraction 0..1 (clamped). */
export function sliderStopAt(stops: readonly number[], frac: number): number {
  const n = stops.length;
  const f = Math.min(1, Math.max(0, Number.isFinite(frac) ? frac : 0));
  return stops[Math.round(f * (n - 1))];
}

export interface SliderProps {
  /** The values in screen order, left to right (at least 2). */
  stops: readonly number[];
  /** C#'s value. Off the track (unknown, -1) = no thumb, and the slider is disabled. */
  value: number;
  /** The readout text of a value. */
  format: (v: number) => ReactNode;
  /** Every readout the slider can show, so its slot keeps one width. */
  samples: readonly ReactNode[];
  onCommit: (v: number) => void;
  /** The value the slider shows now (the stop under the pointer while dragging). Cheap work only (no trigger). */
  onShown?: (v: number) => void;
  disabled?: boolean;
  /** Extra marks on the track, by value (the full-demand price). */
  marks?: readonly number[];
  /** A tick at every stop (a short list: the tree levels, the pass multiplier). */
  ticks?: boolean;
  tip?: ReactNode;
  /** The track takes the whole row width under a text line (the Money view), else it sits between a caption and the readout. */
  wide?: boolean;
}

export function Slider({ stops, value, format, samples, onCommit, onShown, disabled, marks, ticks, tip, wide }: SliderProps): JSX.Element {
  const track = useRef<HTMLDivElement>(null);
  const [drag, setDrag] = useState<number | null>(null);
  const [pending, setPending] = useState<number | null>(null);
  useEffect(() => { if (pending !== null && pending === value) setPending(null); }, [value, pending]);
  useEffect(() => {
    if (pending === null) return undefined;
    const id = window.setTimeout(() => setPending(null), PENDING_MS);
    return () => window.clearTimeout(id);
  }, [pending]);

  const shown = drag !== null ? drag : pending !== null ? pending : value;
  useEffect(() => { if (onShown) onShown(shown); }, [shown, onShown]);
  const pos = sliderPos(stops, shown);
  const off = disabled || sliderPos(stops, value) < 0;

  const onDown = (e: React.MouseEvent<HTMLDivElement>): void => {
    if (off || e.button !== 0 || !track.current) return;
    e.stopPropagation();
    e.preventDefault();
    const r = track.current.getBoundingClientRect();
    const at = (x: number): number => sliderStopAt(stops, r.width > 0 ? (x - r.left) / r.width : 0);
    let cur = at(e.clientX);
    setDrag(cur);
    const move = (ev: MouseEvent): void => {
      const v = at(ev.clientX);
      if (v === cur) return;
      cur = v;
      setDrag(v);
    };
    const stop = (): void => {
      document.removeEventListener("mousemove", move);
      document.removeEventListener("mouseup", up);
      window.removeEventListener("blur", cancel);
      setDrag(null);
    };
    // The window lost focus mid-drag (alt-tab): drop the drag, send nothing.
    const cancel = (): void => stop();
    const up = (): void => {
      stop();
      if (cur !== (pending !== null ? pending : value)) {
        setPending(cur);
        onCommit(cur);
      }
    };
    document.addEventListener("mousemove", move);
    document.addEventListener("mouseup", up);
    window.addEventListener("blur", cancel);
  };

  const pct = (p: number): string => `${Math.round(p * 10000) / 100}%`;
  const n = stops.length;
  const body = (
    <div className={cx(styles.slider, wide && styles.sliderWide, off && styles.sliderOff, drag !== null && styles.sliderDrag)}>
      <div className={styles.sliderHit} onMouseDown={onDown}>
        <div ref={track} className={styles.sliderTrack}>
          {pos >= 0 && <div className={styles.sliderFill} style={{ width: pct(pos) }} />}
          {ticks && stops.map((_, i) => <div key={i} className={styles.sliderTick} style={{ left: pct(i / (n - 1)) }} />)}
          {marks && marks.map((m) => {
            const p = sliderPos(stops, m);
            return p >= 0 ? <div key={"m" + m} className={styles.sliderMark} style={{ left: pct(p) }} /> : null;
          })}
          {pos >= 0 && <div className={styles.sliderThumb} style={{ left: pct(pos) }} />}
        </div>
      </div>
      {!wide && (
        <div className={styles.sliderValue}>
          <StableWidth samples={samples} align="end">{pos >= 0 || shown >= 0 ? format(shown) : "–"}</StableWidth>
        </div>
      )}
    </div>
  );
  return tip ? <Tip tip={tip}>{body}</Tip> : body;
}
