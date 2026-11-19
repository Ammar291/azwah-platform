export const LANGUAGES = ['en', 'ar'] as const;

export type Language = (typeof LANGUAGES)[number];

/** Each language's name in its own script. Deliberately not translated: people look for their own language's name. */
export const LANGUAGE_ENDONYMS: Record<Language, string> = { en: 'English', ar: 'العربية' };

export function isLanguage(value: unknown): value is Language {
  return typeof value === 'string' && (LANGUAGES as readonly string[]).includes(value);
}

export function isRtlLanguage(language: Language): boolean {
  return language === 'ar';
}

/** A stored choice wins; otherwise Arabic when the device's first language is Arabic; otherwise English. */
export function resolveInitialLanguage(
  stored: string | null,
  deviceLanguageCodes: readonly (string | null)[],
): Language {
  if (isLanguage(stored)) {
    return stored;
  }
  return deviceLanguageCodes[0]?.toLowerCase() === 'ar' ? 'ar' : 'en';
}
