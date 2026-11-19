'use client';

import { Download } from 'lucide-react';
import { useState } from 'react';

import { Pagination } from '@/components/data/pagination';
import { StatusBadge } from '@/components/data/status-badge';
import { EmptyState } from '@/components/states/empty-state';
import { ErrorState } from '@/components/states/error-state';
import { TableSkeleton } from '@/components/states/table-skeleton';
import { Button } from '@/components/ui/button';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { EXPORT_POLL_MS, isExportPending, useExports } from '@/lib/api/queries';
import type { Export } from '@/lib/api/schemas';
import { downloadHref } from '@/lib/api/search';
import { fill } from '@/lib/format';
import { useI18n } from '@/lib/i18n-context';
import { plural } from '@/lib/plural';

import { datasetLabel } from './export-form';

/** The researcher's own exports, read again every few seconds while one is still being written. */
export function ExportList({ pollMs = EXPORT_POLL_MS }: { pollMs?: number }) {
  const { t } = useI18n();
  const [page, setPage] = useState(1);
  const exports = useExports(page, pollMs);

  if (exports.isError) return <ErrorState error={exports.error} onRetry={() => void exports.refetch()} />;
  if (!exports.data) return <TableSkeleton columns={7} rows={3} />;
  if (exports.data.totalCount === 0) return <EmptyState title={t.exports.listEmptyTitle} description={t.exports.listEmptyBody} />;
  const pending = exports.data.items.some(isExportPending);
  return (
    <div className="flex flex-col gap-3">
      {pending ? (
        <p role="status" className="text-sm text-muted-foreground">
          {t.exports.refreshing}
        </p>
      ) : null}
      <div className="rounded-lg border border-border bg-card">
        <Table aria-label={t.exports.listTable}>
          <TableHeader>
            <TableRow>
              <TableHead className="ps-4">{t.exports.listColumns.dataset}</TableHead>
              <TableHead>{t.exports.listColumns.requested}</TableHead>
              <TableHead>{t.exports.listColumns.status}</TableHead>
              <TableHead className="text-end">{t.exports.listColumns.rows}</TableHead>
              <TableHead>{t.exports.listColumns.finished}</TableHead>
              <TableHead>{t.exports.listColumns.expires}</TableHead>
              <TableHead className="pe-4">{t.exports.listColumns.file}</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {exports.data.items.map((item) => (
              <ExportRow key={item.id} item={item} />
            ))}
          </TableBody>
        </Table>
      </div>
      <p className="text-xs text-muted-foreground">{t.common.utcNote}</p>
      <Pagination page={exports.data.page} pageSize={exports.data.pageSize} totalCount={exports.data.totalCount} onPage={setPage} />
    </div>
  );
}

const FILE_NAME = /filename="?([\w.-]+)"?/i;
/** Some browsers abandon a download whose object URL is revoked before it starts, so it is released a little later. */
const RELEASE_URL_MS = 30_000;

/**
 * Fetches the file and hands it to the browser only when it really is the CSV. A plain download link would save
 * whatever came back under the file's name, so an expired session would leave an error message in a file called .csv.
 */
async function saveCsv(href: string): Promise<boolean> {
  let response: Response;
  try {
    response = await fetch(href, { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'text/csv' } });
  } catch {
    return false;
  }
  if (!response.ok || !(response.headers.get('content-type') ?? '').toLowerCase().startsWith('text/csv')) return false;
  const url = URL.createObjectURL(await response.blob());
  const link = document.createElement('a');
  link.href = url;
  link.download = FILE_NAME.exec(response.headers.get('content-disposition') ?? '')?.[1] ?? 'export.csv';
  document.body.append(link);
  link.click();
  link.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), RELEASE_URL_MS);
  return true;
}

function DownloadButton({ href, label }: { href: string; label: string }) {
  const { t } = useI18n();
  const [state, setState] = useState<'idle' | 'busy' | 'failed'>('idle');
  async function download() {
    setState('busy');
    setState((await saveCsv(href)) ? 'idle' : 'failed');
  }
  return (
    <span className="flex flex-col items-start gap-1">
      <Button variant="outline" size="lg" aria-label={label} disabled={state === 'busy'} onClick={() => void download()}>
        <Download aria-hidden />
        {state === 'busy' ? t.exports.downloading : t.exports.download}
      </Button>
      {state === 'failed' ? (
        <span role="alert" className="max-w-56 text-xs whitespace-normal text-status-danger">
          {t.exports.downloadFailed}
        </span>
      ) : null}
    </span>
  );
}

function ExportRow({ item }: { item: Export }) {
  const { t, f, language } = useI18n();
  const href = downloadHref(item.downloadPath);
  const failure = item.errorCode ? ((t.exports.errors as Record<string, string>)[item.errorCode] ?? null) : null;
  return (
    <TableRow>
      <TableCell className="ps-4">
        <span className="flex flex-col">
          <span className="font-medium">{datasetLabel(t, item.dataset)}</span>
          <span className="text-xs text-muted-foreground">{plural(language, t.exports.fieldCount, item.columns.length, f.number(item.columns.length))}</span>
        </span>
      </TableCell>
      <TableCell className="tabular whitespace-nowrap">{f.dateTime(item.requestedAt)}</TableCell>
      <TableCell>
        <span className="flex flex-col items-start gap-1">
          <StatusBadge kind="export" value={item.status} />
          {failure ? <span className="text-xs text-status-danger">{failure}</span> : null}
        </span>
      </TableCell>
      <TableCell className="tabular text-end">{item.rowCount === null ? t.common.none : f.number(item.rowCount)}</TableCell>
      <TableCell className="tabular whitespace-nowrap">{f.dateTime(item.finishedAt)}</TableCell>
      <TableCell className="tabular whitespace-nowrap">{f.dateTime(item.expiresAt)}</TableCell>
      <TableCell className="pe-4">
        {href ? (
          <DownloadButton href={href} label={fill(t.exports.downloadLabel, { date: f.dateTime(item.requestedAt) })} />
        ) : (
          <span className="text-sm text-muted-foreground">{t.exports.noFile}</span>
        )}
      </TableCell>
    </TableRow>
  );
}
