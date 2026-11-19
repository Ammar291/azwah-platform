'use client';

import { EyeOff } from 'lucide-react';

import { displayCount } from '@/lib/display';
import { fill } from '@/lib/format';
import { useI18n } from '@/lib/i18n-context';

/**
 * A count from an aggregate, or "Hidden" when it is hidden to protect privacy. A hidden count is not always a small
 * one: the platform decides on the people behind a count, not the count, and when only one cell of a group would be
 * hidden it hides the one covering the next fewest people too, so the label never claims the value is below the
 * minimum group size.
 */
export function SuppressedValue({ value, suppressed, minimumGroupSize }: { value: number | null; suppressed: boolean; minimumGroupSize: number }) {
  const { t, f } = useI18n();
  const shown = displayCount(value, suppressed, minimumGroupSize);
  if (shown.kind === 'value') return <span className="tabular">{f.number(shown.value)}</span>;
  return (
    <span className="inline-flex items-center gap-1 text-muted-foreground">
      <EyeOff aria-hidden className="size-3.5" />
      <span title={fill(t.display.suppressedLabel, { n: minimumGroupSize })}>{t.display.suppressed}</span>
    </span>
  );
}
