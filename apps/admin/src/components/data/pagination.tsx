'use client';

import { ChevronLeft, ChevronRight } from 'lucide-react';

import { Button } from '@/components/ui/button';
import { fill } from '@/lib/format';
import { useI18n } from '@/lib/i18n-context';

/** Previous and next over server-side pages; the total comes from the API's `totalCount`. */
export function Pagination({
  page,
  pageSize,
  totalCount,
  onPage,
}: {
  page: number;
  pageSize: number;
  totalCount: number;
  onPage: (page: number) => void;
}) {
  const { t, f } = useI18n();
  const pages = Math.max(1, Math.ceil(totalCount / pageSize));
  return (
    <nav aria-label={t.common.pagination} className="flex flex-wrap items-center justify-between gap-3 text-sm">
      <div className="flex items-center gap-3 text-muted-foreground">
        <p aria-live="polite" className="tabular">
          {fill(t.common.pageOf, { page: f.number(page), pages: f.number(pages) })}
        </p>
        <p className="tabular">{fill(t.common.total, { count: f.number(totalCount) })}</p>
      </div>
      <div className="flex gap-2">
        <Button variant="outline" size="lg" disabled={page <= 1} onClick={() => onPage(page - 1)}>
          <ChevronLeft aria-hidden className="rtl:rotate-180" />
          {t.common.previous}
        </Button>
        <Button variant="outline" size="lg" disabled={page >= pages} onClick={() => onPage(page + 1)}>
          {t.common.next}
          <ChevronRight aria-hidden className="rtl:rotate-180" />
        </Button>
      </div>
    </nav>
  );
}
