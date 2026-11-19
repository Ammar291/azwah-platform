import type { Language } from './i18n';

const DASH = '—';

/** Locales with Western digits in both languages (CONTRIBUTING.md); every date is shown in UTC. */
const LOCALES: Record<Language, string> = { en: 'en-GB', ar: 'ar-AE-u-nu-latn' };

export interface Formatter {
  number(value: number): string;
  /** A score exactly as the API recorded it: every decimal it sent, never rounded; null shows a dash. */
  score(value: number | null): string;
  /** A rate from 0 to 1, as the API returns it, with one decimal; null shows a dash. */
  percent(value: number | null): string;
  /** An ISO 8601 timestamp, shown as its UTC day. */
  date(value: string | null): string;
  /** An ISO 8601 timestamp with its UTC time. */
  dateTime(value: string | null): string;
  /** A `YYYY-MM-DD` day. */
  day(value: string | null): string;
  /** The month of a `YYYY-MM-DD` day. */
  month(value: string): string;
}

export function createFormatter(language: Language): Formatter {
  const locale = LOCALES[language];
  const integer = new Intl.NumberFormat(locale, { maximumFractionDigits: 0 });
  const exact = new Intl.NumberFormat(locale, { maximumFractionDigits: 20 });
  // Always one decimal: a column of rates reads and aligns better as 58.0% and 59.9% than as 58% and 59.9%.
  const rate = new Intl.NumberFormat(locale, { style: 'percent', minimumFractionDigits: 1, maximumFractionDigits: 1 });
  const dayFormat = new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeZone: 'UTC' });
  const timeFormat = new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'short', timeZone: 'UTC' });
  const monthFormat = new Intl.DateTimeFormat(locale, { month: 'short', year: 'numeric', timeZone: 'UTC' });
  const dayOf = (value: string) => new Date(`${value}T00:00:00Z`);
  return {
    number: (value) => integer.format(value),
    score: (value) => (value === null ? DASH : exact.format(value)),
    percent: (value) => (value === null ? DASH : rate.format(value)),
    date: (value) => (value === null ? DASH : dayFormat.format(new Date(value))),
    dateTime: (value) => (value === null ? DASH : timeFormat.format(new Date(value))),
    day: (value) => (value === null ? DASH : dayFormat.format(dayOf(value))),
    month: (value) => monthFormat.format(dayOf(value)),
  };
}

/** Replaces `{name}` placeholders; unknown names stay visible so a missing value is noticed, never hidden. */
export function fill(template: string, values: Record<string, string | number>): string {
  return template.replace(/\{(\w+)\}/g, (match, name: string) => (name in values ? String(values[name]) : match));
}
