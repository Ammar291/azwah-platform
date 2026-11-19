import type { OverviewFilters } from '@/lib/api/queries';
import type { Query } from '@/lib/api/search';
import { dayParam, guidParam, oneOf } from '@/lib/url-state';

export function parseOverviewFilters(params: URLSearchParams): OverviewFilters {
  return {
    waveId: guidParam(params, 'waveId'),
    from: dayParam(params, 'from'),
    to: dayParam(params, 'to'),
    interval: oneOf(params, 'interval', ['month', 'week'] as const) ?? 'month',
  };
}

export function serializeOverviewFilters(filters: OverviewFilters): Query {
  return { ...filters, interval: filters.interval === 'month' ? null : filters.interval };
}

/** Only a reversed window is refused here; the platform checks the rest (bounds and the three-year limit). */
export function isReversed(filters: Pick<OverviewFilters, 'from' | 'to'>): boolean {
  return filters.from !== null && filters.to !== null && filters.from > filters.to;
}
