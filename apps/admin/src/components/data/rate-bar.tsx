'use client';

import { useI18n } from '@/lib/i18n-context';

/** A rate from the API as text, with a thin meter beside it; the track is a lighter step of the same hue. */
export function RateBar({ rate }: { rate: number | null }) {
  const { f } = useI18n();
  return (
    <span className="flex min-w-28 items-center gap-2">
      <span className="tabular w-14 shrink-0 text-sm">{f.percent(rate)}</span>
      {rate === null ? null : (
        <span aria-hidden className="h-1.5 flex-1 overflow-hidden rounded-full bg-accent">
          <span className="block h-full rounded-full bg-chart-1" style={{ width: `${Math.min(100, Math.max(0, rate * 100))}%` }} />
        </span>
      )}
    </span>
  );
}
