'use client';

import { Lock } from 'lucide-react';
import type { ReactNode } from 'react';

import { useI18n } from '@/lib/i18n-context';

/** Says on every configuration page that nothing here can be changed, and where changes are made instead. */
export function ReadOnlyNotice() {
  const { t } = useI18n();
  return (
    <p role="note" className="flex items-start gap-2 rounded-lg border border-border bg-secondary px-4 py-3 text-sm text-secondary-foreground">
      <Lock aria-hidden className="mt-0.5 size-4 shrink-0" />
      {t.configuration.readOnly}
    </p>
  );
}

/** Content text in the reader's language; English, marked as such, when the Arabic does not exist yet. */
export function LocalText({ text }: { text: { ar: string | null; en: string } }) {
  const { t, language } = useI18n();
  if (language === 'ar' && text.ar) return <span>{text.ar}</span>;
  if (language === 'ar') {
    return (
      <span className="flex flex-col">
        <span lang="en" dir="ltr" className="self-start">
          {text.en}
        </span>
        <span className="text-xs text-status-warning">{t.configuration.missingArabic}</span>
      </span>
    );
  }
  return <span>{text.en}</span>;
}

/** A label and its value in a definition list. */
export function Detail({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex flex-col gap-0.5">
      <dt className="text-sm text-muted-foreground">{label}</dt>
      <dd className="text-sm">{children}</dd>
    </div>
  );
}
