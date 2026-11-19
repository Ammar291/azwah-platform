'use client';

import { createContext, useContext, useMemo, type ReactNode } from 'react';

import { createFormatter, type Formatter } from './format';
import { directionOf, messages, type Language, type Messages } from './i18n';

interface I18n {
  language: Language;
  dir: 'ltr' | 'rtl';
  t: Messages;
  f: Formatter;
}

const I18nContext = createContext<I18n | null>(null);

/** The request's language for client components; the root layout reads it from the language cookie. */
export function I18nProvider({ language, children }: { language: Language; children: ReactNode }) {
  const value = useMemo(
    () => ({ language, dir: directionOf(language), t: messages[language], f: createFormatter(language) }),
    [language],
  );
  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>;
}

export function useI18n(): I18n {
  const value = useContext(I18nContext);
  if (!value) throw new Error('useI18n must be used inside I18nProvider.');
  return value;
}
