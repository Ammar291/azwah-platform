'use client';

import { Skeleton } from '@/components/ui/skeleton';
import { useI18n } from '@/lib/i18n-context';

/** Placeholder rows while a table loads, announced once as "Loading". */
export function TableSkeleton({ columns, rows = 6 }: { columns: number; rows?: number }) {
  const { t } = useI18n();
  return (
    <div role="status" className="overflow-hidden rounded-lg border border-border bg-card">
      <span className="sr-only">{t.common.loading}</span>
      <div aria-hidden className="divide-y divide-border">
        {Array.from({ length: rows }, (_, row) => (
          <div key={row} className="flex gap-4 px-4 py-3">
            {Array.from({ length: columns }, (_, column) => (
              <Skeleton key={column} className="h-4 flex-1" />
            ))}
          </div>
        ))}
      </div>
    </div>
  );
}
