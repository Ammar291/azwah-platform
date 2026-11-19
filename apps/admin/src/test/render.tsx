import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, type RenderOptions } from '@testing-library/react';
import type { ReactElement, ReactNode } from 'react';

import { I18nProvider } from '@/lib/i18n-context';
import type { Language } from '@/lib/i18n';

/** Renders with the providers every dashboard view expects; queries never retry in tests. */
export function renderWithProviders(ui: ReactElement, { language = 'en', ...options }: { language?: Language } & RenderOptions = {}) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 }, mutations: { retry: false } } });
  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={client}>
        <I18nProvider language={language}>{children}</I18nProvider>
      </QueryClientProvider>
    );
  }
  return { client, ...render(ui, { wrapper: Wrapper, ...options }) };
}
