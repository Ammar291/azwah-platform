'use client';

import { LifeBuoy } from 'lucide-react';

import { useI18n } from '@/lib/i18n-context';

/** What to do about a critical alert. The platform holds no contact details, so this points to the programme's own records. */
export function SafetyGuidance() {
  const { t } = useI18n();
  return (
    <aside className="flex items-start gap-3 rounded-lg border border-status-danger/40 bg-status-danger-soft p-4 text-status-danger">
      <LifeBuoy aria-hidden className="mt-0.5 size-5 shrink-0" />
      <div className="flex flex-col gap-1">
        <h2 className="text-sm font-semibold">{t.safetyAlerts.guidanceTitle}</h2>
        <p className="text-sm text-foreground">{t.safetyAlerts.guidance}</p>
      </div>
    </aside>
  );
}
