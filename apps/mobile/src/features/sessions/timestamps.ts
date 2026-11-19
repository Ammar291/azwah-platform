const ISO = /^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(\d+))?(Z|[+-]\d{2}:\d{2})$/;

/** One microsecond in .NET ticks (100 ns). */
const TICKS_PER_MICROSECOND = 10;

function parse(value: string): { seconds: number; ticks: number } | null {
  const match = ISO.exec(value);
  if (!match) return null;
  const [, whole, fraction = '', zone] = match;
  const millis = Date.parse(`${whole}${zone}`);
  if (Number.isNaN(millis)) return null;
  return { seconds: millis / 1000, ticks: Number(fraction.padEnd(7, '0').slice(0, 7)) };
}

/**
 * Whether two server timestamps name the same instant. The same `lastModifiedAt` comes back with 100 ns precision in
 * a save's response (the server's clock) and with microseconds in a later read (PostgreSQL), and possibly with another
 * offset spelling, so strings cannot be compared: a difference under a microsecond is the same instant.
 */
export function sameInstant(a: string | null, b: string | null): boolean {
  if (a === null || b === null) return a === b;
  if (a === b) return true;
  const left = parse(a);
  const right = parse(b);
  if (!left || !right) return false;
  // Whole seconds apart times 10^7, plus the ticks: small integers, so exact.
  const difference = (left.seconds - right.seconds) * 10_000_000 + (left.ticks - right.ticks);
  return Math.abs(difference) < TICKS_PER_MICROSECOND;
}
