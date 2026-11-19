/**
 * Whether a count from an aggregate may be shown. The API suppresses small cells (D-55, D-59); as a display
 * safeguard, a count from 1 to `minimumGroupSize - 1` that arrives unflagged is hidden too. Zero is shown: it reveals
 * no one. This never computes or re-derives a statistic.
 */
export type DisplayCount = { kind: 'value'; value: number } | { kind: 'suppressed' };

export function displayCount(value: number | null, suppressed: boolean, minimumGroupSize: number): DisplayCount {
  if (suppressed || value === null) return { kind: 'suppressed' };
  if (value > 0 && value < minimumGroupSize) return { kind: 'suppressed' };
  return { kind: 'value', value };
}
