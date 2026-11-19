import { isApiError, type ApiErrorKind } from '@/api/client';

export type ProblemKind = ApiErrorKind | 'unexpected';

export interface ProblemDescription {
  kind: ProblemKind;
  /** The problem code from the server, when it sent a known one. */
  code: string | null;
  /** Translation keys. */
  titleKey: string;
  messageKey: string;
}

/**
 * Turns any thrown value into translation keys. A known problem code gives the specific message; otherwise the
 * message follows the error kind. Server-provided text is never used, and unexpected errors say nothing about why.
 */
export function describeProblem(error: unknown, hasTranslation: (key: string) => boolean): ProblemDescription {
  const kind: ProblemKind = isApiError(error) ? error.kind : 'unexpected';
  const code = isApiError(error) && error.code ? error.code : null;
  const codeKey = code ? `problems.${code}` : null;
  const known = codeKey !== null && hasTranslation(codeKey);
  return {
    kind,
    code: known ? code : null,
    titleKey: `errors.kinds.${kind}.title`,
    messageKey: known && codeKey ? codeKey : `errors.kinds.${kind}.message`,
  };
}
