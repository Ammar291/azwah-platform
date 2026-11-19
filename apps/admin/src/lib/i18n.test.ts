import { describe, expect, it } from 'vitest';

import { directionOf, isLanguage, messages } from './i18n';
import { ar } from './messages/ar';
import { en } from './messages/en';

function entries(value: object, prefix = ''): [string, string][] {
  return Object.entries(value).flatMap(([key, child]): [string, string][] =>
    typeof child === 'string' ? [[`${prefix}${key}`, child]] : entries(child as object, `${prefix}${key}.`),
  );
}

/** Keys whose Arabic value is deliberately not Arabic script. */
const NOT_ARABIC = new Set(['language.switchTo', 'common.none', 'filters.waveOption']);

describe('admin i18n', () => {
  it('knows its languages and their direction', () => {
    expect(isLanguage('ar')).toBe(true);
    expect(isLanguage('fr')).toBe(false);
    expect(directionOf('ar')).toBe('rtl');
    expect(directionOf('en')).toBe('ltr');
  });

  it('serves the English and Arabic message files', () => {
    expect(messages.en).toBe(en);
    expect(messages.ar).toBe(ar);
  });

  it('has the same keys in English and Arabic', () => {
    expect(entries(ar).map(([key]) => key).sort()).toEqual(entries(en).map(([key]) => key).sort());
  });

  it('writes every Arabic message in Arabic script', () => {
    const notArabic = entries(ar).filter(([key, value]) => !NOT_ARABIC.has(key) && !/[\u0600-\u06FF]/.test(value));

    expect(notArabic).toEqual([]);
  });

  it('uses the same placeholders in both languages', () => {
    const placeholders = (value: string) => [...value.matchAll(/\{(\w+)\}/g)].map((match) => match[1]).sort();
    const arabic = new Map(entries(ar));
    // Arabic writes one and two as words ("بند واحد", "بندان"), so those plural forms carry no number.
    const spelledOut = (key: string) => /\.(one|two)$/.test(key) && placeholders(arabic.get(key) ?? '').length === 0;
    const mismatched = entries(en).filter(
      ([key, value]) => !spelledOut(key) && placeholders(value).join() !== placeholders(arabic.get(key) ?? '').join(),
    );

    expect(mismatched).toEqual([]);
  });

  it('gives every counted noun a form for each plural category of both languages', () => {
    const categories = ['zero', 'one', 'two', 'few', 'many', 'other'];
    for (const forms of [en.configuration.sectionCount, en.configuration.itemCount, en.exports.fieldCount, ar.configuration.sectionCount, ar.configuration.itemCount, ar.exports.fieldCount]) {
      expect(Object.keys(forms).sort()).toEqual([...categories].sort());
    }
  });

  it('uses Western digits only', () => {
    expect(entries(ar).filter(([, value]) => /[\u0660-\u0669\u06F0-\u06F9]/.test(value))).toEqual([]);
  });
});
