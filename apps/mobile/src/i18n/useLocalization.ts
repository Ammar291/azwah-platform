import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';

import { formatDate, formatNumber, pickText, type BilingualText, type DateStyle } from './format';
import { useLanguage } from './LanguageContext';
import { isRtlLanguage } from './languages';

/**
 * The one hook screens use for language: translations, the current language and its direction, switching, and
 * formatting of dates, numbers and the API's bilingual text.
 */
export function useLocalization() {
  const { t } = useTranslation();
  const { language, changeLanguage } = useLanguage();

  return useMemo(() => {
    const isRTL = isRtlLanguage(language);
    return {
      t,
      language,
      isRTL,
      direction: isRTL ? ('rtl' as const) : ('ltr' as const),
      changeLanguage,
      formatDate: (value: string | Date, style?: DateStyle) => formatDate(value, language, style),
      formatNumber: (value: number, options?: Intl.NumberFormatOptions) => formatNumber(value, language, options),
      pickText: (text: BilingualText) => pickText(text, language),
    };
  }, [t, language, changeLanguage]);
}

export type Localization = ReturnType<typeof useLocalization>;
