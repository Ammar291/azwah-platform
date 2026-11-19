'use client';

import { PageHeader } from '@/components/app-shell/page-header';
import { useI18n } from '@/lib/i18n-context';

import { StatusPanel } from './status-panel';

export function StatusPageView() {
  const { t } = useI18n();
  return (
    <>
      <PageHeader title={t.nav.items.status} />
      <StatusPanel />
    </>
  );
}
