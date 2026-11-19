import { describe, expect, it } from 'vitest';

import { bandLabel, orderBands } from './bands';
import { ar } from './messages/ar';
import { en } from './messages/en';

describe('bands', () => {
  it('names the bands of the specification in the reader language', () => {
    expect(bandLabel(en, 'vulnerable')).toBe('Vulnerable');
    expect(bandLabel(ar, 'flourishing')).toBe('مزدهر');
  });

  it('shows any other code exactly as recorded', () => {
    expect(bandLabel(en, 'tier_a')).toBe('tier_a');
    expect(bandLabel(ar, 'toString')).toBe('toString');
  });

  it('orders the named bands from low to high, not by code, and keeps other codes after them as they came', () => {
    expect(orderBands(['flourishing', 'transitional', 'vulnerable'], (code) => code)).toEqual(['vulnerable', 'transitional', 'flourishing']);
    expect(orderBands(['tier_b', 'flourishing', 'tier_a', 'vulnerable'], (code) => code)).toEqual(['vulnerable', 'flourishing', 'tier_b', 'tier_a']);
  });
});
