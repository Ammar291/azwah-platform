import { createInstance, type i18n } from 'i18next';
import { initReactI18next } from 'react-i18next';

import type { Language } from './languages';
import ar from './resources/ar.json';
import en from './resources/en.json';

export const resources = {
  en: { translation: en },
  ar: { translation: ar },
} as const;

/** A fresh instance per app start (and per test), never the global singleton. */
export async function createI18n(language: Language): Promise<i18n> {
  const instance = createInstance();
  await instance.use(initReactI18next).init({
    resources,
    lng: language,
    fallbackLng: 'en',
    interpolation: { escapeValue: false },
  });
  return instance;
}
