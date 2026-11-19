'use client';

import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useState, type ReactNode } from 'react';

import { Toaster } from '@/components/ui/sonner';
import { TooltipProvider } from '@/components/ui/tooltip';
import { shouldRetry } from '@/lib/api/errors';
import { directionOf, type Language } from '@/lib/i18n';
import { I18nProvider } from '@/lib/i18n-context';

export function Providers({ language, children }: { language: Language; children: ReactNode }) {
  const [queryClient] = useState(
    () => new QueryClient({ defaultOptions: { queries: { staleTime: 30_000, retry: shouldRetry }, mutations: { retry: false } } }),
  );
  const dir = directionOf(language);
  return (
    <QueryClientProvider client={queryClient}>
      <I18nProvider language={language}>
        <TooltipProvider>
          {children}
          <Toaster dir={dir} position={dir === 'rtl' ? 'bottom-left' : 'bottom-right'} />
        </TooltipProvider>
      </I18nProvider>
    </QueryClientProvider>
  );
}
