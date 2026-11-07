/*
 * The hub's one text input: renaming a resort, a run or a lift. The only file allowed an <input> (tools/check-bundle.js rule 6
 * also requires `stopPropagation` here and a useLayoutEffect cleanup that calls `.blur()`).
 * Keydown / mousedown stop propagating (the title bar's cloned onMouseDown starts a drag, index.js@719900).
 * Enter commits itself, not via blur: the code does not rely on a scripted blur() event in Gameface.
 * Commit = trimmed text cut to MAX_NAME, sent only if changed (as vanilla useInputName, index.js@2099223).
 * Unmount while focused commits and blurs in the cleanup. UserInterface.cs:170 sets InputManager.hasInputFieldFocus,
 * which masks the keyboard for gameplay (Game.Input/InputManager.cs:644-652, 1.6.0f1).
 * Assumed, not verified: Gameface raises TextInputTypeChanged for a mod's <input>.
 */
import { useCallback, useLayoutEffect, useRef, useState } from "react";
import type { ChangeEvent, KeyboardEvent, MouseEvent } from "react";
import styles from "./hub.module.scss";

/** Vanilla EllipsisTextInput maxLength (index.js@1008900). C# cuts to the same length. */
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
  // Fixed for this mount: a C# push of a new name while the player types must not close or reset the field.
  const initialRef = useRef(initial);
  const valueRef = useRef(initial);
  const doneRef = useRef(false);
  const cancelRef = useRef(false);
  // Latest callbacks for the unmount path (the cleanup must not capture stale ones)
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
      // best effort. The player can click into the field
    }
    return () => {
      // Unmounted (panel closed, resort switched): commit like a blur, then drop the focus
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
      finish(false); // commit here, not only from onBlur. The code does not rely on blur() raising onBlur
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
