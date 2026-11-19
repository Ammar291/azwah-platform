'use client';

import { useId } from 'react';

import { PageHeader } from '@/components/app-shell/page-header';
import { useI18n } from '@/lib/i18n-context';

import { ExportForm } from './export-form';
import { ExportList } from './export-list';

/** Research exports for researchers: request from the catalog, then follow and download your own files. */
export function ExportsView({ pollMs }: { pollMs?: number }) {
  const { t } = useI18n();
  const requestId = useId();
  const listId = useId();
  return (
    <>
      <PageHeader title={t.exports.title} description={t.exports.description} />
      <section aria-labelledby={requestId} className="flex flex-col gap-4 rounded-lg border border-border bg-card p-5">
        <h2 id={requestId} className="text-lg font-semibold">
          {t.exports.requestTitle}
        </h2>
        <ExportForm />
      </section>
      <section aria-labelledby={listId} className="flex flex-col gap-3">
        <h2 id={listId} className="text-lg font-semibold">
          {t.exports.listTitle}
        </h2>
        <ExportList pollMs={pollMs} />
      </section>
    </>
  );
}
