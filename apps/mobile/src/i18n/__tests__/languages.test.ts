import { isLanguage, isRtlLanguage, resolveInitialLanguage } from '../languages';

describe('isLanguage', () => {
  it('accepts the two supported languages only', () => {
    expect(isLanguage('en')).toBe(true);
    expect(isLanguage('ar')).toBe(true);
    expect(isLanguage('fr')).toBe(false);
    expect(isLanguage(null)).toBe(false);
    expect(isLanguage('AR')).toBe(false);
  });
});

describe('isRtlLanguage', () => {
  it('is true for Arabic only', () => {
    expect(isRtlLanguage('ar')).toBe(true);
    expect(isRtlLanguage('en')).toBe(false);
  });
});

describe('resolveInitialLanguage', () => {
  it('prefers a stored choice', () => {
    expect(resolveInitialLanguage('en', ['ar'])).toBe('en');
  });

  it('uses Arabic when the device first language is Arabic', () => {
    expect(resolveInitialLanguage(null, ['ar', 'en'])).toBe('ar');
    expect(resolveInitialLanguage(null, ['AR'])).toBe('ar');
  });

  it('falls back to English otherwise', () => {
    expect(resolveInitialLanguage(null, ['fr', 'ar'])).toBe('en');
    expect(resolveInitialLanguage(null, [])).toBe('en');
    expect(resolveInitialLanguage(null, [null])).toBe('en');
    expect(resolveInitialLanguage('klingon', ['fr'])).toBe('en');
  });
});
