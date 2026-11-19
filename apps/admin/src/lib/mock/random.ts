import 'server-only';

/** A small seeded generator (mulberry32), so the synthetic data is the same on every run. */
export type Random = () => number;

export function seeded(seed: number): Random {
  let state = seed >>> 0;
  return () => {
    state = (state + 0x6d2b79f5) | 0;
    let t = Math.imul(state ^ (state >>> 15), 1 | state);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

/** A seed from text, for values that must stay the same per key (for example one trend period). */
export function seedOf(text: string): number {
  let hash = 2166136261;
  for (let index = 0; index < text.length; index++) {
    hash = Math.imul(hash ^ text.charCodeAt(index), 16777619);
  }
  return hash >>> 0;
}

export function between(random: Random, min: number, max: number): number {
  return min + Math.floor(random() * (max - min + 1));
}

export function hex(random: Random, length: number): string {
  let text = '';
  for (let index = 0; index < length; index++) text += Math.floor(random() * 16).toString(16);
  return text;
}

/** A version-4-shaped UUID from the generator. */
export function uuid(random: Random): string {
  const value = hex(random, 32);
  const variant = ((parseInt(value[16], 16) & 0x3) | 0x8).toString(16);
  return `${value.slice(0, 8)}-${value.slice(8, 12)}-4${value.slice(13, 16)}-${variant}${value.slice(17, 20)}-${value.slice(20, 32)}`;
}

const CROCKFORD = '0123456789ABCDEFGHJKMNPQRSTVWXYZ';

/** A household reference in the platform's format, `HH-XXXX-XXXX`. */
export function householdReference(random: Random): string {
  const part = () => Array.from({ length: 4 }, () => CROCKFORD[Math.floor(random() * CROCKFORD.length)]).join('');
  return `HH-${part()}-${part()}`;
}
