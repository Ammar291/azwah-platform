'use client';

import { useState, type ComponentProps } from 'react';

import { NativeInput } from './field';

const DAY = /^(\d{4})-\d{2}-\d{2}$/;

/** A full `YYYY-MM-DD` day from 2000 to 2100, the range the API accepts for analytics windows. */
export function isSupportedDay(value: string): boolean {
  const match = DAY.exec(value);
  if (!match) return false;
  const year = Number(match[1]);
  return year >= 2000 && year <= 2100 && !Number.isNaN(Date.parse(`${value}T00:00:00Z`));
}

/**
 * A date filter that applies a day only once it is whole and in range, or at once when cleared, so typing a year
 * digit by digit never sends "0002" to the platform. An unfinished entry reverts when the field loses focus.
 */
export function DateInput({
  value,
  onCommit,
  ...props
}: { value: string | null; onCommit: (value: string | null) => void } & Omit<ComponentProps<'input'>, 'value' | 'onChange' | 'type'>) {
  const [draft, setDraft] = useState(value ?? '');
  const [applied, setApplied] = useState(value);
  if (applied !== value) {
    setApplied(value);
    setDraft(value ?? '');
  }
  return (
    <NativeInput
      {...props}
      type="date"
      min="2000-01-01"
      max="2100-12-31"
      value={draft}
      onChange={(event) => {
        const next = event.target.value;
        setDraft(next);
        if (next === '') onCommit(null);
        else if (isSupportedDay(next)) onCommit(next);
      }}
      onBlur={(event) => {
        if (draft !== '' && !isSupportedDay(draft)) setDraft(value ?? '');
        props.onBlur?.(event);
      }}
    />
  );
}
