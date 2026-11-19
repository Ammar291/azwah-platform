import type { ScoreScale } from '@/api/participant/schemas';

/**
 * Where a total sits along a drawn scale, from 0 to 1: geometry only, never scoring. A band is drawn as wide as the
 * number of whole totals in it, so each total owns one equal slot and the marker sits in the middle of its slot. That
 * keeps it inside the band the server recorded, even at a band's edge. A total off the scale stays at the nearer end.
 */
export function scalePosition(total: number, { minimum, maximum }: Pick<ScoreScale, 'minimum' | 'maximum'>): number {
  const slots = maximum - minimum + 1;
  if (slots <= 0) return 0;
  const position = (total - minimum + 0.5) / slots;
  return Math.min(1, Math.max(0, position));
}

/** The share of the drawn scale a band takes, from 0 to 1. */
export function bandShare(
  band: ScoreScale['bands'][number],
  { minimum, maximum }: Pick<ScoreScale, 'minimum' | 'maximum'>,
): number {
  const slots = maximum - minimum + 1;
  return slots <= 0 ? 0 : (band.max - band.min + 1) / slots;
}
