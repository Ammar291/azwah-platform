'use client';

import Link from 'next/link';
import { useState } from 'react';

import { PageHeader } from '@/components/app-shell/page-header';
import { Pagination } from '@/components/data/pagination';
import { EmptyState } from '@/components/states/empty-state';
import { ErrorState } from '@/components/states/error-state';
import { TableSkeleton } from '@/components/states/table-skeleton';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { useInstruments } from '@/lib/api/queries';
import { useI18n } from '@/lib/i18n-context';

import { LocalText, ReadOnlyNotice } from './shared';

/** Every questionnaire the platform holds, for system administrators to read. */
export function InstrumentsView() {
  const { t } = useI18n();
  const [page, setPage] = useState(1);
  const instruments = useInstruments(page);
  const columns = t.configuration.instrumentColumns;
  return (
    <>
      <PageHeader title={t.configuration.title} description={t.configuration.description} />
      <ReadOnlyNotice />
      {instruments.isError ? (
        <ErrorState error={instruments.error} onRetry={() => void instruments.refetch()} />
      ) : !instruments.data ? (
        <TableSkeleton columns={5} rows={3} />
      ) : instruments.data.totalCount === 0 ? (
        <EmptyState title={t.configuration.emptyInstrumentsTitle} description={t.configuration.emptyInstrumentsBody} />
      ) : (
        <div className="flex flex-col gap-3">
          <div className="rounded-lg border border-border bg-card">
            <Table aria-label={t.configuration.instrumentsTable}>
              <TableHeader>
                <TableRow>
                  <TableHead className="ps-4">{columns.code}</TableHead>
                  <TableHead>{columns.name}</TableHead>
                  <TableHead>{columns.kind}</TableHead>
                  <TableHead>{columns.status}</TableHead>
                  <TableHead className="pe-4">{columns.text}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {instruments.data.items.map((instrument) => (
                  <TableRow key={instrument.id}>
                    <TableCell className="ps-4 font-medium">
                      <Link href={`/configuration/instruments/${instrument.id}`} className="text-primary underline-offset-4 hover:underline">
                        {instrument.code}
                      </Link>
                    </TableCell>
                    <TableCell>
                      <LocalText text={instrument.name} />
                    </TableCell>
                    <TableCell>{t.configuration.kinds[instrument.kind]}</TableCell>
                    <TableCell>{t.configuration.instrumentStatus[instrument.status]}</TableCell>
                    <TableCell className="pe-4">{instrument.hasCompleteText ? t.configuration.textComplete : t.configuration.textIncomplete}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
          <Pagination page={instruments.data.page} pageSize={instruments.data.pageSize} totalCount={instruments.data.totalCount} onPage={setPage} />
        </div>
      )}
    </>
  );
}
