import { ar } from './messages/ar';
import { en } from './messages/en';
import type { Messages } from './messages';

export type { Messages } from './messages';

export const LANGUAGES = ['en', 'ar'] as const;
export type Language = (typeof LANGUAGES)[number];
export const LANGUAGE_COOKIE = 'lang';

export function isLanguage(value: unknown): value is Language {
  return typeof value === 'string' && (LANGUAGES as readonly string[]).includes(value);
}

export function directionOf(language: Language): 'ltr' | 'rtl' {
  return language === 'ar' ? 'rtl' : 'ltr';
}

export const messages: Record<Language, Messages> = { en, ar };
