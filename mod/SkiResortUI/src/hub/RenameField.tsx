/*
 * src/hub/RenameField.tsx - the hub's one text input: renaming a resort, a run or a lift.
 *
 * Written for this repo (MIT). Created 2026-10-03 (Round U2, implementer I-3).
 * Authority: docs/research/hub-u2-spec.md section 4.3 "RenameField". This is the ONLY file allowed
 * an <input> (tools/check-bundle.js rule 6 also requires `stopPropagation` here and a
 * useLayoutEffect cleanup that calls `.blur()`).
 *
 * Contract:
 *   - Mounted when the player clicks a pen; focuses and selects its text in useLayoutEffect.
 *   - Every keydown and mousedown stops propagating: the panel's title bar is a drag handle whose
 *     cloned onMouseDown starts a drag (DragHandle, index.js@719900 cloneElement(... onMouseDown));
 *     a key must not reach the panel's other handlers.
 *   - Enter -> commit, then blur. Esc -> cancel, then blur. Blur (clicking away) -> commit.
 *     (Enter commits itself rather than through onBlur: whether Gameface raises a blur event from
 *     a scripted blur() is not relied on.) The starting text is fixed at mount (initialRef).
 *   - Commit = the trimmed text cut to MAX_NAME (64, vanilla EllipsisTextInput's maxLength
 *     default, index.js@1008900). It is sent only when it differs from the name shown when the
 *     field opened (vanilla useInputName submits only a changed value, index.js@2099223 NMe);
 *     "" is sent as "" (C# restores the default name). C# trims and cuts again, refuses an entity
 *     that is not our sign, run or station, and logs every rename.
 *   - Unmounted while focused (the panel was closed, the resort switched) -> COMMIT (vanilla
 *     commits on blur), then blur() in the layout-effect cleanup, so the game's input-field focus
 *     flag cannot stay set: Game.SceneFlow/UserInterface.cs:170 sets InputManager.hasInputFieldFocus
 *     from the view's TextInputTypeChanged, and while it is set the keyboard is masked off for
 *     gameplay (Game.Input/InputManager.cs:644-652, 1.6.0f1: KeyboardAndMouse -> mask 2 instead of
 *     3). A commit happens at most once per mount (doneRef), whichever path comes first.
 *   - That Gameface raises TextInputTypeChanged for a mod's <input> (so typing a hotkey letter
 *     does nothing in the world) is [likely, spec 4.3 / 7.2 item 10]: settled in field test step 11.
 *   - InputActionBarrier is not used: vanilla's is gamepad-only and gamepad is not a design
 *     constraint (DECISIONS 130; spec 4.3).
 */
import { useCallback, useLayoutEffect, useRef, useState } from "react";
import type { ChangeEvent, KeyboardEvent, MouseEvent } from "react";
import styles from "./hub.module.scss";

/** Vanilla EllipsisTextInput maxLength (index.js@1008900); C# cuts to the same length. */
export const MAX_NAME = 64;

export interface RenameFieldProps {
  /** The name shown when the field opened (its starting text). */
  initial: string;
  /** Called at most once, with the trimmed, cut text, only when it differs from `initial`. */
  onCommit: (text: string) => void;
  /** Called when the field closes without a commit (Esc, or an unchanged text). */
  onClose: () => void;
  className?: string;
}

/** Trim, then cut to MAX_NAME characters. */
export function cleanName(text: string): string {
  const t = (text ?? "").trim();
  return t.length > MAX_NAME ? t.slice(0, MAX_NAME).trim() : t;
}

export function RenameField({ initial, onCommit, onClose, className }: RenameFieldProps): JSX.Element {
  const [value, setValue] = useState(initial);
  const inputRef = useRef<HTMLInputElement>(null);
  // The starting name is fixed for this mount: a C# push of a new name while the player types
  // (a vanilla rename arrives within one heartbeat, spec 3.2) must not close or reset the field.
  const initialRef = useRef(initial);
  const valueRef = useRef(initial);
  const doneRef = useRef(false);
  const cancelRef = useRef(false);
  // The latest callbacks, for the unmount path (the cleanup must not capture stale ones).
  const commitRef = useRef(onCommit);
  const closeRef = useRef(onClose);
  commitRef.current = onCommit;
  closeRef.current = onClose;

  const finish = useCallback((cancel: boolean): void => {
    if (doneRef.current) return;
    doneRef.current = true;
    const text = cleanName(valueRef.current);
    try {
      if (!cancel && text !== cleanName(initialRef.current)) commitRef.current(text);
      else closeRef.current();
    } catch (e) {
      console.error("SkiResortMod UI: rename commit failed", e);
    }
  }, []);

  useLayoutEffect(() => {
    const el = inputRef.current;
    try {
      el?.focus();
      el?.select();
    } catch {
      // focus/select are best effort; the player can click into the field
    }
    return () => {
      // Unmounted (panel closed, resort switched): commit like a blur, then drop the focus.
      finish(cancelRef.current);
      try {
        el?.blur();
      } catch {
        // the element may already be detached
      }
    };
  }, [finish]);

  const onChange = (e: ChangeEvent<HTMLInputElement>): void => {
    valueRef.current = e.target.value;
    setValue(e.target.value);
  };

  const onKeyDown = (e: KeyboardEvent<HTMLInputElement>): void => {
    e.stopPropagation();
    if (e.key === "Enter") {
      e.preventDefault();
      finish(false); // commit here, not only from onBlur: blur() raising onBlur is not relied on
      inputRef.current?.blur();
    } else if (e.key === "Escape" || e.key === "Esc") {
      e.preventDefault();
      cancelRef.current = true;
      finish(true);
      inputRef.current?.blur();
    }
  };

  const stop = (e: MouseEvent<HTMLInputElement>): void => {
    e.stopPropagation();
  };

  return (
    <input
      ref={inputRef}
      className={className ?? styles.renameInput}
      type="text"
      value={value}
      maxLength={MAX_NAME}
      onChange={onChange}
      onKeyDown={onKeyDown}
      onKeyUp={(e) => e.stopPropagation()}
      onMouseDown={stop}
      onClick={stop}
      onBlur={() => finish(cancelRef.current)}
    />
  );
}
