import { render } from '@testing-library/react-native';
import type { ReactElement } from 'react';
import { I18nextProvider } from 'react-i18next';

import { createI18n } from '@/i18n/i18n';
import type { Language } from '@/i18n/languages';

export async function renderWithI18n(ui: ReactElement, language: Language = 'en') {
  const i18n = await createI18n(language);
  return render(<I18nextProvider i18n={i18n}>{ui}</I18nextProvider>);
}
