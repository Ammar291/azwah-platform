import { z } from 'zod';

import type { AssessmentFilters } from '@/lib/api/queries';
import { ASSIGNMENT_STATUSES } from '@/lib/api/schemas';
import type { Query } from '@/lib/api/search';
import { dayParam, guidParam, oneOf, positiveInt } from '@/lib/url-state';

export const PAGE_SIZE = 20;
const SNAKE = /^[a-z][a-z0-9]*(_[a-z0-9]+)*$/;

export function parseAssessmentFilters(params: URLSearchParams): AssessmentFilters {
  const instrumentCode = params.get('instrumentCode');
  const code = instrumentCode && SNAKE.test(instrumentCode) ? instrumentCode : null;
  return {
    waveId: guidParam(params, 'waveId'),
    instrumentCode: code,
    versionNumber: code ? positiveInt(params, 'versionNumber') : null,
    status: oneOf(params, 'status', ASSIGNMENT_STATUSES),
    assignedFrom: dayParam(params, 'assignedFrom'),
    assignedTo: dayParam(params, 'assignedTo'),
    page: positiveInt(params, 'page') ?? 1,
    pageSize: PAGE_SIZE,
  };
}

export function serializeAssessmentFilters(filters: AssessmentFilters): Query {
  return { ...filters, page: filters.page === 1 ? null : filters.page, pageSize: null };
}

/** What the filter form holds while it is edited: text, exactly as typed. */
export interface AssessmentDraft {
  waveId: string;
  instrumentCode: string;
  versionNumber: string;
  status: string;
  assignedFrom: string;
  assignedTo: string;
}

export function draftOf(filters: AssessmentFilters): AssessmentDraft {
  return {
    waveId: filters.waveId ?? '',
    instrumentCode: filters.instrumentCode ?? '',
    versionNumber: filters.versionNumber === null ? '' : String(filters.versionNumber),
    status: filters.status ?? '',
    assignedFrom: filters.assignedFrom ?? '',
    assignedTo: filters.assignedTo ?? '',
  };
}

export type DraftError = 'instrumentCodeInvalid' | 'versionNumberInvalid' | 'reversedRange';

const optional = (value: string) => (value.trim() === '' ? null : value.trim());

const draftSchema = z
  .object({
    waveId: z.string().transform(optional),
    instrumentCode: z
      .string()
      .transform(optional)
      .refine((value) => value === null || SNAKE.test(value), { message: 'instrumentCodeInvalid' }),
    versionNumber: z.string().transform(optional),
    status: z.string().transform(optional).pipe(z.enum(ASSIGNMENT_STATUSES).nullable()),
    assignedFrom: z.string().transform(optional),
    assignedTo: z.string().transform(optional),
  })
  .transform((draft, context) => {
    let versionNumber: number | null = null;
    if (draft.instrumentCode !== null && draft.versionNumber !== null) {
      versionNumber = Number(draft.versionNumber);
      if (!Number.isInteger(versionNumber) || versionNumber < 1) {
        context.addIssue({ code: 'custom', path: ['versionNumber'], message: 'versionNumberInvalid' });
      }
    }
    if (draft.assignedFrom !== null && draft.assignedTo !== null && draft.assignedFrom > draft.assignedTo) {
      context.addIssue({ code: 'custom', path: ['assignedFrom'], message: 'reversedRange' });
    }
    return { ...draft, versionNumber };
  });

/** The filters to apply (back on the first page), or a translated message key per field that needs fixing. */
export function applyDraft(
  draft: AssessmentDraft,
): { ok: true; filters: AssessmentFilters } | { ok: false; errors: Partial<Record<keyof AssessmentDraft, DraftError>> } {
  const parsed = draftSchema.safeParse(draft);
  if (!parsed.success) {
    const errors: Partial<Record<keyof AssessmentDraft, DraftError>> = {};
    for (const issue of parsed.error.issues) {
      const field = issue.path[0] as keyof AssessmentDraft;
      errors[field] ??= issue.message as DraftError;
    }
    return { ok: false, errors };
  }
  return { ok: true, filters: { ...parsed.data, page: 1, pageSize: PAGE_SIZE } };
}
