'use client';

import { Table2 } from 'lucide-react';
import { useId, useState, type ReactNode } from 'react';

import { Button } from '@/components/ui/button';
import { useI18n } from '@/lib/i18n-context';
import { cn } from '@/lib/utils';

export interface FigureTable {
  columns: string[];
  rows: string[][];
}

/**
 * Wraps a chart so it can be read without seeing it: a caption, a one-sentence summary as the image's accessible name,
 * and the same numbers as a real table on request.
 *
 * The drawing itself is always laid out left to right. Under `dir="rtl"` SVG text inherits the direction and its
 * `text-anchor` start and end swap, which pushes axis labels over the bars; a chart mirrors for Arabic by reversing
 * its axes instead. Tooltips inside the drawing set the page direction on themselves again (`dir` from `useI18n`).
 */
export function ChartFigure({
  title,
  captionId,
  description,
  summary,
  table,
  legend,
  note,
  children,
}: {
  title: string;
  /**
   * The ID of a heading that already shows the title, when the figure sits in a card of its own: the figure is then
   * named by that heading and draws neither a second caption nor a second border.
   */
  captionId?: string;
  description?: ReactNode;
  summary: string;
  table: FigureTable;
  legend?: ReactNode;
  /** Text under the drawing, in the page direction (for example which values are hidden). */
  note?: ReactNode;
  children: ReactNode;
}) {
  const { t } = useI18n();
  const [showTable, setShowTable] = useState(false);
  const ownTitleId = useId();
  const tableId = useId();
  const embedded = captionId !== undefined;
  return (
    <figure
      aria-labelledby={captionId ?? ownTitleId}
      className={cn('flex flex-col gap-3', !embedded && 'rounded-lg border border-border bg-card p-4 sm:p-5')}
    >
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex min-w-0 flex-col gap-1">
          {embedded ? null : (
            <figcaption id={ownTitleId} className="text-base font-semibold">
              {title}
            </figcaption>
          )}
          {description ? <div className="max-w-prose text-sm text-muted-foreground">{description}</div> : null}
        </div>
        <Button variant="ghost" size="lg" aria-expanded={showTable} aria-controls={tableId} onClick={() => setShowTable((open) => !open)}>
          <Table2 aria-hidden />
          {showTable ? t.common.hideTable : t.common.showTable}
        </Button>
      </div>
      {legend}
      <div role="img" aria-label={summary} dir="ltr" className="w-full">
        {children}
      </div>
      {note}
      <div id={tableId} hidden={!showTable} className="overflow-x-auto">
        {showTable ? (
          <table className="w-full text-sm">
            {embedded ? <caption className="sr-only">{title}</caption> : null}
            <thead>
              <tr className="border-b border-border">
                {table.columns.map((column) => (
                  <th key={column} scope="col" className="px-2 py-2 text-start font-medium text-muted-foreground">
                    {column}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {table.rows.map((row, index) => (
                <tr key={index} className="border-b border-border last:border-0">
                  {row.map((cell, cellIndex) =>
                    cellIndex === 0 ? (
                      <th key={cellIndex} scope="row" className="px-2 py-2 text-start font-normal">
                        {cell}
                      </th>
                    ) : (
                      <td key={cellIndex} className="tabular px-2 py-2 text-start">
                        {cell}
                      </td>
                    ),
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        ) : null}
      </div>
    </figure>
  );
}
