import { cookies } from 'next/headers';

import { isLanguage, LANGUAGE_COOKIE, type Language } from './i18n';

/** The language chosen with the header switch; English until one is chosen. */
export async function getRequestLanguage(): Promise<Language> {
  const value = (await cookies()).get(LANGUAGE_COOKIE)?.value;
  return isLanguage(value) ? value : 'en';
}
