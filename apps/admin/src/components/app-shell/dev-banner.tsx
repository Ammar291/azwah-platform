'use client';

import { FlaskConical } from 'lucide-react';

import { useI18n } from '@/lib/i18n-context';

/** Says plainly when sign-in or data is simulated, on every page (CONTRIBUTING.md: simulated behaviour is labelled). */
export function DevBanner({ developmentSignIn, mockData }: { developmentSignIn: boolean; mockData: boolean }) {
  const { t } = useI18n();
  if (!developmentSignIn && !mockData) return null;
  return (
    <div role="note" className="flex items-start gap-2 border-b border-status-warning/20 bg-status-warning-soft px-4 py-2 text-sm text-status-warning md:px-8">
      <FlaskConical aria-hidden className="mt-0.5 size-4 shrink-0" />
      <div className="flex flex-col gap-0.5 sm:flex-row sm:flex-wrap sm:gap-x-3">
        {developmentSignIn ? <p>{t.shell.devSignIn}</p> : null}
        {mockData ? <p>{t.shell.mockData}</p> : null}
      </div>
    </div>
  );
}
