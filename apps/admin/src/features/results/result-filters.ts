import { z } from 'zod';

import type { ResultFilters } from '@/lib/api/queries';
import type { Query } from '@/lib/api/search';
import { guidParam, positiveInt } from '@/lib/url-state';

export const PAGE_SIZE = 20;
const SNAKE = /^[a-z][a-z0-9]*(_[a-z0-9]+)*$/;
/** The platform's household reference: `HH-` and two groups of four Crockford base-32 characters. */
const REFERENCE = /^HH-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}$/;

/**
 * Wave, questionnaire and page live in the URL. The household reference never does: it identifies a family, and a URL
 * ends up in browser history and in the access logs of any proxy in front of the dashboard.
 */
export function parseResultFilters(params: URLSearchParams): ResultFilters {
  const instrumentCode = params.get('instrumentCode');
  return {
    waveId: guidParam(params, 'waveId'),
    instrumentCode: instrumentCode && SNAKE.test(instrumentCode) ? instrumentCode : null,
    reference: null,
    page: positiveInt(params, 'page') ?? 1,
    pageSize: PAGE_SIZE,
  };
}

export function serializeResultFilters(filters: ResultFilters): Query {
  return { ...filters, reference: null, page: filters.page === 1 ? null : filters.page, pageSize: null };
}

/** What the filter form holds while it is edited: text, exactly as typed. */
export interface ResultDraft {
  waveId: string;
  instrumentCode: string;
  reference: string;
}

export function draftOf(filters: ResultFilters): ResultDraft {
  return { waveId: filters.waveId ?? '', instrumentCode: filters.instrumentCode ?? '', reference: filters.reference ?? '' };
}

export type ResultDraftError = 'instrumentCodeInvalid' | 'referenceInvalid';

const optional = (value: string) => (value.trim() === '' ? null : value.trim());

const draftSchema = z.object({
  waveId: z.string().transform(optional),
  instrumentCode: z
    .string()
    .transform(optional)
    .refine((value) => value === null || SNAKE.test(value), { message: 'instrumentCodeInvalid' }),
  reference: z
    .string()
    .transform((value) => optional(value)?.toUpperCase() ?? null)
    .refine((value) => value === null || REFERENCE.test(value), { message: 'referenceInvalid' }),
});

/** The filters to apply (back on the first page), or a translated message key per field that needs fixing. */
export function applyResultDraft(
  draft: ResultDraft,
): { ok: true; filters: ResultFilters } | { ok: false; errors: Partial<Record<keyof ResultDraft, ResultDraftError>> } {
  const parsed = draftSchema.safeParse(draft);
  if (!parsed.success) {
    const errors: Partial<Record<keyof ResultDraft, ResultDraftError>> = {};
    for (const issue of parsed.error.issues) {
      const field = issue.path[0] as keyof ResultDraft;
      errors[field] ??= issue.message as ResultDraftError;
    }
    return { ok: false, errors };
  }
  return { ok: true, filters: { ...parsed.data, page: 1, pageSize: PAGE_SIZE } };
}
