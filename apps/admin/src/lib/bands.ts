import type { Messages } from './messages';

/**
 * The household assessment's bands (D-103), from low to high, as its scoring rule set orders them. The API lists the
 * tiers of a distribution by code, and the analytics pages cannot read the rule set, so the order is kept here. This
 * names and orders bands for display only; which band a total falls in is decided by the platform, never here.
 */
const KNOWN_BANDS = ['vulnerable', 'transitional', 'flourishing'] as const;
type KnownBand = (typeof KNOWN_BANDS)[number];

export function isKnownBand(code: string): code is KnownBand {
  return (KNOWN_BANDS as readonly string[]).includes(code);
}

/** The band's name in the reader's language for the codes the specification names; any other code exactly as recorded. */
export function bandLabel(t: Messages, code: string): string {
  return isKnownBand(code) ? t.bands[code] : code;
}

/** Named bands from low to high, then any other code in the order it arrived. */
export function orderBands<T>(items: readonly T[], codeOf: (item: T) => string): T[] {
  const rank = (item: T) => {
    const index = (KNOWN_BANDS as readonly string[]).indexOf(codeOf(item));
    return index === -1 ? KNOWN_BANDS.length : index;
  };
  return items
    .map((item, index) => ({ item, index }))
    .sort((a, b) => rank(a.item) - rank(b.item) || a.index - b.index)
    .map(({ item }) => item);
}
