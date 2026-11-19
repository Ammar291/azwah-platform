import { formatDate, formatNumber, pickText, toWesternDigits } from '../format';

const AT = '2026-10-14T09:30:00Z';

describe('toWesternDigits', () => {
  it('replaces Arabic-Indic and Extended Arabic-Indic digits', () => {
    expect(toWesternDigits('١٤ أكتوبر ٢٠٢٦')).toBe('14 أكتوبر 2026');
    expect(toWesternDigits('۱۲۳')).toBe('123');
    expect(toWesternDigits('2026')).toBe('2026');
  });
});

describe('formatDate', () => {
  it('writes dates in English', () => {
    expect(formatDate(AT, 'en', 'date', 'UTC')).toBe('14 October 2026');
    expect(formatDate(AT, 'en', 'monthYear', 'UTC')).toBe('October 2026');
  });

  it('writes Arabic month names with Western digits', () => {
    const text = formatDate(AT, 'ar', 'date', 'UTC');

    expect(text).toMatch(/أكتوبر/);
    expect(text).toMatch(/14/);
    expect(text).toMatch(/2026/);
    expect(text).not.toMatch(/[٠-٩]/);
  });

  it('includes the time when asked', () => {
    expect(formatDate(AT, 'en', 'dateTime', 'UTC')).toMatch(/9:30/);
    expect(formatDate(AT, 'ar', 'dateTime', 'UTC')).toMatch(/9:30/);
  });

  it('returns nothing for a value that is not a date', () => {
    expect(formatDate('not a date', 'en')).toBe('');
  });
});

describe('formatNumber', () => {
  it('uses Western digits in both languages', () => {
    expect(formatNumber(1234.5, 'en')).toBe('1,234.5');
    expect(formatNumber(1234.5, 'ar')).not.toMatch(/[٠-٩]/);
    expect(formatNumber(42, 'ar')).toBe('42');
  });
});

describe('pickText', () => {
  it('chooses the language and falls back to English when Arabic is missing', () => {
    expect(pickText({ ar: 'نعم', en: 'Yes' }, 'ar')).toBe('نعم');
    expect(pickText({ ar: 'نعم', en: 'Yes' }, 'en')).toBe('Yes');
    expect(pickText({ ar: null, en: 'Yes' }, 'ar')).toBe('Yes');
  });
});
