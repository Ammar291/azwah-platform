import { describe, expect, it } from 'vitest';

import { createFormatter, fill } from './format';

const easternDigits = /[\u0660-\u0669\u06F0-\u06F9]/;

describe('createFormatter', () => {
  it('formats English numbers, rates and UTC dates', () => {
    const f = createFormatter('en');

    expect(f.number(12345)).toBe('12,345');
    expect(f.percent(0.875)).toBe('87.5%');
    expect(f.percent(null)).toBe('—');
    expect(f.date('2026-10-11T23:30:00Z')).toBe('11 Oct 2026');
    expect(f.day('2026-10-11')).toBe('11 Oct 2026');
    expect(f.month('2026-10-01')).toBe('Oct 2026');
  });

  it('gives every rate one decimal, so rates in a column line up', () => {
    const f = createFormatter('en');

    expect(f.percent(0.58)).toBe('58.0%');
    expect(f.percent(0.599)).toBe('59.9%');
    expect(f.percent(1)).toBe('100.0%');
    expect(f.percent(0)).toBe('0.0%');
  });

  it('keeps Western digits in Arabic', () => {
    const f = createFormatter('ar');

    for (const value of [f.number(12345), f.percent(0.875), f.date('2026-10-11T09:00:00Z'), f.month('2026-10-01')]) {
      expect(value).not.toMatch(easternDigits);
      expect(value).toMatch(/[0-9]/);
    }
    expect(f.number(12345)).toContain('12');
  });

  it('shows a score exactly as recorded, never rounded, with a dash for none', () => {
    const en = createFormatter('en');
    const ar = createFormatter('ar');

    expect(en.score(112)).toBe('112');
    expect(en.score(2.75)).toBe('2.75');
    expect(en.score(1234.5)).toBe('1,234.5');
    expect(en.score(0.1)).toBe('0.1');
    expect(en.score(null)).toBe('—');
    expect(ar.score(112.25)).not.toMatch(easternDigits);
    expect(ar.score(112.25)).toContain('112');
    expect(ar.score(112.25)).toContain('25');
  });

  it('shows a dash for a missing date', () => {
    expect(createFormatter('en').date(null)).toBe('—');
  });
});

describe('fill', () => {
  it('replaces named placeholders', () => {
    expect(fill('Page {page} of {pages}', { page: 2, pages: 5 })).toBe('Page 2 of 5');
  });

  it('leaves unknown placeholders visible', () => {
    expect(fill('Hello {name}', {})).toBe('Hello {name}');
  });
});
