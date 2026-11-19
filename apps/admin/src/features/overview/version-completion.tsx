'use client';

import { useId } from 'react';

import { RateBar } from '@/components/data/rate-bar';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import type { Overview } from '@/lib/api/schemas';
import { useI18n } from '@/lib/i18n-context';

/** One row per instrument version and module, as the platform counts them; rows are never summed together. */
export function VersionCompletion({ rows }: { rows: Overview['byVersion'] }) {
  const { t, f } = useI18n();
  const titleId = useId();
  return (
    <div className="rounded-lg border border-border bg-card">
      {/* Above the table, not its caption: a caption is as wide as the table and was cut off on a phone. */}
      <div className="px-4 pt-4 pb-2">
        <p id={titleId} className="text-base font-semibold text-foreground">
          {t.overview.byVersionTitle}
        </p>
        <p className="mt-1 text-sm text-muted-foreground">{t.overview.byVersionNote}</p>
      </div>
      <Table aria-labelledby={titleId}>
        <TableHeader>
          <TableRow>
            <TableHead className="ps-4">{t.overview.instrument}</TableHead>
            <TableHead>{t.overview.version}</TableHead>
            <TableHead>{t.overview.module}</TableHead>
            <TableHead className="text-end">{t.overview.total}</TableHead>
            <TableHead className="text-end">{t.overview.completed}</TableHead>
            <TableHead className="pe-4">{t.overview.rate}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {rows.length === 0 ? (
            <TableRow>
              <TableCell colSpan={6} className="px-4 py-6 text-muted-foreground">
                {t.overview.noVersions}
              </TableCell>
            </TableRow>
          ) : (
            rows.map((row) => (
              <TableRow key={`${row.instrumentCode}-${row.versionNumber}-${row.sectionCode ?? 'all'}`}>
                <TableCell className="ps-4 font-medium">{row.instrumentCode}</TableCell>
                <TableCell className="tabular">{f.number(row.versionNumber)}</TableCell>
                <TableCell>{row.sectionCode ?? t.overview.wholeVersion}</TableCell>
                <TableCell className="tabular text-end">{f.number(row.total)}</TableCell>
                <TableCell className="tabular text-end">{f.number(row.completed)}</TableCell>
                <TableCell className="pe-4">
                  <RateBar rate={row.completionRate} />
                </TableCell>
              </TableRow>
            ))
          )}
        </TableBody>
      </Table>
    </div>
  );
}
