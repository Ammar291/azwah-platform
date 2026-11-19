'use client';

import Link from 'next/link';
import { useState } from 'react';

import { PageHeader } from '@/components/app-shell/page-header';
import { Pagination } from '@/components/data/pagination';
import { StatusBadge } from '@/components/data/status-badge';
import { EmptyState } from '@/components/states/empty-state';
import { ErrorState } from '@/components/states/error-state';
import { TableSkeleton } from '@/components/states/table-skeleton';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { useInstrument, useInstrumentVersions } from '@/lib/api/queries';
import type { InstrumentVersionSummary } from '@/lib/api/schemas';
import { fill, type Formatter } from '@/lib/format';
import { useI18n } from '@/lib/i18n-context';
import type { Messages } from '@/lib/messages';
import { plural } from '@/lib/plural';

import { LocalText, ReadOnlyNotice } from './shared';

export function effectiveWindow(t: Messages, f: Formatter, version: Pick<InstrumentVersionSummary, 'effectiveFrom' | 'effectiveTo'>): string {
  if (!version.effectiveFrom) return t.configuration.notEffective;
  return fill(t.configuration.effectiveRange, {
    from: f.date(version.effectiveFrom),
    to: version.effectiveTo ? f.date(version.effectiveTo) : t.configuration.openEnded,
  });
}

export function approval(t: Messages, f: Formatter, version: { approvedAt: string | null; approvedBy: string | null }): string {
  return version.approvedAt && version.approvedBy
    ? fill(t.configuration.approvedBy, { date: f.date(version.approvedAt), approver: version.approvedBy })
    : t.configuration.notApproved;
}

/** One questionnaire and its versions, newest first. */
export function InstrumentView({ instrumentId }: { instrumentId: string }) {
  const { t, f, language } = useI18n();
  const [page, setPage] = useState(1);
  const instrument = useInstrument(instrumentId);
  const versions = useInstrumentVersions(instrumentId, page);
  const columns = t.configuration.versionColumns;

  if (instrument.isError) {
    return (
      <>
        {/* No crumbs: the title is already the section, and repeating it as a crumb read "… rules › … rules". */}
        <PageHeader title={t.configuration.title} />
        <ErrorState error={instrument.error} onRetry={() => void instrument.refetch()} />
      </>
    );
  }
  const code = instrument.data?.code ?? '…';
  return (
    <>
      <PageHeader
        title={code}
        description={instrument.data ? <LocalText text={instrument.data.name} /> : null}
        crumbs={[{ label: t.configuration.title, href: '/configuration' }]}
      />
      <ReadOnlyNotice />
      <section className="flex flex-col gap-3">
        <h2 className="text-lg font-semibold">{t.configuration.versionsTitle}</h2>
        {versions.isError ? (
          <ErrorState error={versions.error} onRetry={() => void versions.refetch()} />
        ) : !versions.data ? (
          <TableSkeleton columns={6} rows={2} />
        ) : versions.data.totalCount === 0 ? (
          <EmptyState title={t.configuration.emptyVersionsTitle} description={t.configuration.emptyVersionsBody} />
        ) : (
          <>
            <div className="rounded-lg border border-border bg-card">
              <Table aria-label={t.configuration.versionsTable}>
                <TableHeader>
                  <TableRow>
                    <TableHead className="ps-4">{columns.version}</TableHead>
                    <TableHead>{columns.status}</TableHead>
                    <TableHead>{columns.approved}</TableHead>
                    <TableHead>{columns.source}</TableHead>
                    <TableHead>{columns.effective}</TableHead>
                    <TableHead className="pe-4">{columns.content}</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {versions.data.items.map((version) => (
                    <TableRow key={version.id}>
                      <TableCell className="ps-4 font-medium">
                        <Link href={`/configuration/versions/${version.id}`} className="text-primary underline-offset-4 hover:underline">
                          {`${columns.version} ${f.number(version.versionNumber)}`}
                        </Link>
                      </TableCell>
                      <TableCell>
                        <StatusBadge kind="version" value={version.status} />
                      </TableCell>
                      <TableCell>{approval(t, f, version)}</TableCell>
                      <TableCell className="max-w-56 truncate" title={version.approvalSource ?? undefined}>
                        <bdi>{version.approvalSource ?? t.configuration.none}</bdi>
                      </TableCell>
                      <TableCell>{effectiveWindow(t, f, version)}</TableCell>
                      <TableCell className="pe-4">
                        {fill(t.configuration.contentCount, {
                          sections: plural(language, t.configuration.sectionCount, version.sectionCount, f.number(version.sectionCount)),
                          items: plural(language, t.configuration.itemCount, version.itemCount, f.number(version.itemCount)),
                        })}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
            <Pagination page={versions.data.page} pageSize={versions.data.pageSize} totalCount={versions.data.totalCount} onPage={setPage} />
          </>
        )}
      </section>
    </>
  );
}
