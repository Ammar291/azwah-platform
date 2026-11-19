'use client';

import { useEffect, useEffectEvent } from 'react';

import type { Paged } from './api/schemas';

/**
 * A page past the end comes back empty while the total is not: a hand-edited `?page=99`, or the last row of the last
 * page just gone. The list then goes to the page that is now the last. Returns true while that is about to happen, so
 * the view can show its loading state instead of an empty table reading "Page 99 of 14".
 *
 * `settled` is false while a previous page's rows are still shown in place of the page being loaded. `onPage` may be
 * a new function on every render: the move is made once per page past the end, not again on each render.
 */
export function useLastPage(data: Paged<unknown> | undefined, settled: boolean, onPage: (page: number) => void): boolean {
  const pastEnd = settled && data !== undefined && data.items.length === 0 && data.page > 1 && data.totalCount > 0;
  const lastPage = data ? Math.ceil(data.totalCount / data.pageSize) : 1;
  const goTo = useEffectEvent(onPage);
  useEffect(() => {
    if (pastEnd) goTo(lastPage);
  }, [pastEnd, lastPage]);
  return pastEnd;
}
