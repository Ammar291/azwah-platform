'use client';

import { RateBar } from '@/components/data/rate-bar';
import { SuppressedValue } from '@/components/data/suppressed-value';
import { Table, TableBody, TableCaption, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import type { DemographicRow } from '@/lib/api/schemas';
import { displayCount } from '@/lib/display';
import { useI18n } from '@/lib/i18n-context';

import { groupLabel } from './group-label';

/** Completion by a demographic group. Hidden cells stay hidden; a row with any hidden count shows no rate. */
export function DemographicTable({
  title,
  kind,
  rows,
  minimumGroupSize,
}: {
  title: string;
  kind: 'age' | 'employment';
  rows: DemographicRow[];
  minimumGroupSize: number;
}) {
  const { t } = useI18n();
  return (
    // min-w-0: a grid item, so a wide table scrolls inside its card on a phone instead of widening the page.
    <div className="min-w-0 rounded-lg border border-border bg-card">
      <Table>
        <TableCaption className="caption-top px-4 pt-4 text-start text-base font-semibold text-foreground">{title}</TableCaption>
        <TableHeader>
          <TableRow>
            <TableHead className="ps-4">{t.overview.group}</TableHead>
            <TableHead className="text-end">{t.overview.total}</TableHead>
            <TableHead className="text-end">{t.overview.completed}</TableHead>
            <TableHead className="pe-4">{t.overview.rate}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {rows.map((row) => {
            const hidden =
              displayCount(row.total, row.suppressed, minimumGroupSize).kind === 'suppressed' ||
              displayCount(row.completed, row.suppressed, minimumGroupSize).kind === 'suppressed';
            return (
              <TableRow key={row.group}>
                <TableCell className="ps-4">{groupLabel(t, kind, row.group)}</TableCell>
                <TableCell className="text-end">
                  <SuppressedValue value={row.total} suppressed={row.suppressed} minimumGroupSize={minimumGroupSize} />
                </TableCell>
                <TableCell className="text-end">
                  <SuppressedValue value={row.completed} suppressed={row.suppressed} minimumGroupSize={minimumGroupSize} />
                </TableCell>
                <TableCell className="pe-4">{hidden ? <span className="text-muted-foreground">{t.common.none}</span> : <RateBar rate={row.completionRate} />}</TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
    </div>
  );
}
