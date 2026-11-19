import type { SafetyAlertFilters } from '@/lib/api/queries';
import { SAFETY_CONCERNS, type SafetyConcern } from '@/lib/api/schemas';
import type { Query } from '@/lib/api/search';
import { oneOf, positiveInt } from '@/lib/url-state';

/** `unresolved` (open and acknowledged together) is the default; `all` leaves the status out of the request. */
export const ALERT_STATUS_FILTERS = ['unresolved', 'open', 'acknowledged', 'resolved', 'all'] as const;
export type AlertStatusFilter = (typeof ALERT_STATUS_FILTERS)[number];

export interface AlertViewFilters {
  status: AlertStatusFilter;
  category: SafetyConcern | null;
  page: number;
}

export function parseAlertFilters(params: URLSearchParams): AlertViewFilters {
  return {
    status: oneOf(params, 'status', ALERT_STATUS_FILTERS) ?? 'unresolved',
    category: oneOf(params, 'category', SAFETY_CONCERNS),
    page: positiveInt(params, 'page') ?? 1,
  };
}

/** Defaults are left out of the address so it stays short. */
export function serializeAlertFilters(filters: AlertViewFilters): Query {
  return {
    status: filters.status === 'unresolved' ? null : filters.status,
    category: filters.category,
    page: filters.page === 1 ? null : filters.page,
  };
}

/** What the list request carries: `all` means no status at all. */
export function toRequestFilters(filters: AlertViewFilters): SafetyAlertFilters {
  return { status: filters.status === 'all' ? null : filters.status, category: filters.category };
}
