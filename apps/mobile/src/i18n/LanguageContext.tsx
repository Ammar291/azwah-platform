import { createContext, useContext } from 'react';

import type { Language } from './languages';

export interface LanguageContextValue {
  language: Language;
  changeLanguage: (next: Language) => Promise<void>;
}

const LanguageContext = createContext<LanguageContextValue | null>(null);

export const LanguageProvider = LanguageContext.Provider;

export function useLanguage(): LanguageContextValue {
  const value = useContext(LanguageContext);
  if (!value) {
    throw new Error('useLanguage must be used inside LanguageProvider.');
  }
  return value;
}
