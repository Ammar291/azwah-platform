import { createI18n } from '../i18n';

describe('createI18n', () => {
  it('translates in the requested language and interpolates values', async () => {
    const i18n = await createI18n('ar');

    expect(i18n.t('common.back')).toBe('رجوع');
    expect(i18n.t('designSystem.saved', { name: 'ليلى' })).toBe('تم الحفظ: ليلى');
  });

  it('switches language at runtime', async () => {
    const i18n = await createI18n('en');
    await i18n.changeLanguage('ar');

    expect(i18n.t('app.name')).toBe('عزوة');
  });

  it('names each language in the other language', async () => {
    const english = await createI18n('en');
    const arabic = await createI18n('ar');

    expect(english.t('language.names.ar')).toBe('Arabic');
    expect(arabic.t('language.names.en')).toBe('الإنجليزية');
  });
});
