import type { Language } from './languages';

/** UAE locales with the Latin numbering system: CONTRIBUTING.md asks for Western digits in both languages. */
const LOCALES: Record<Language, string> = { en: 'en-AE-u-nu-latn', ar: 'ar-AE-u-nu-latn' };

export type DateStyle = 'date' | 'dateTime' | 'monthYear';

const DATE_OPTIONS: Record<DateStyle, Intl.DateTimeFormatOptions> = {
  date: { day: 'numeric', month: 'long', year: 'numeric' },
  dateTime: { day: 'numeric', month: 'long', year: 'numeric', hour: 'numeric', minute: '2-digit' },
  monthYear: { month: 'long', year: 'numeric' },
};

/**
 * Replaces Arabic-Indic and Extended Arabic-Indic digits with Western ones. Some Intl implementations (Hermes on
 * older Android versions) ignore the `-u-nu-latn` extension, so formatted output is normalised as well.
 */
export function toWesternDigits(text: string): string {
  return text
    .replace(/[٠-٩]/g, (digit) => String(digit.charCodeAt(0) - 0x0660))
    .replace(/[۰-۹]/g, (digit) => String(digit.charCodeAt(0) - 0x06f0));
}

/** Formats an ISO 8601 timestamp or Date in the person's language. Returns '' for a value that is not a date. */
export function formatDate(
  value: string | Date,
  language: Language,
  style: DateStyle = 'date',
  timeZone?: string,
): string {
  const date = typeof value === 'string' ? new Date(value) : value;
  if (Number.isNaN(date.getTime())) {
    return '';
  }
  return toWesternDigits(new Intl.DateTimeFormat(LOCALES[language], { ...DATE_OPTIONS[style], timeZone }).format(date));
}

export function formatNumber(value: number, language: Language, options?: Intl.NumberFormatOptions): string {
  return toWesternDigits(new Intl.NumberFormat(LOCALES[language], options).format(value));
}

/** Bilingual text as the API sends it (TextDto). Arabic may be null only in draft content (D-40). */
export interface BilingualText {
  ar: string | null;
  en: string;
}

/** The text in the person's language; English when the Arabic is missing, which approved content never is. */
export function pickText(text: BilingualText, language: Language): string {
  return language === 'ar' && text.ar ? text.ar : text.en;
}
