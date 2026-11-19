'use client';

import { DoorOpen } from 'lucide-react';

import { PageHeader } from '@/components/app-shell/page-header';
import { EmptyState } from '@/components/states/empty-state';
import { useI18n } from '@/lib/i18n-context';

/**
 * For a signed-in caller whose roles open no section (for example a support coordinator today). The page sends anyone
 * whose role does open a section there instead, so this is never shown to them.
 */
export function WelcomeView() {
  const { t } = useI18n();
  return (
    <>
      <PageHeader title={t.welcome.title} />
      <EmptyState icon={<DoorOpen />} title={t.welcome.emptyTitle} description={t.welcome.body} />
    </>
  );
}
