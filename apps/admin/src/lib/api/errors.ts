import { problemSchema } from './schemas';

/** A failed BFF call: the status, the platform's stable error code and the trace ID, never its text. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string | null,
    readonly traceId: string | null,
    readonly fields: string[] = [],
  ) {
    super(code ?? `http_${status}`);
    this.name = 'ApiError';
  }
}

export async function toApiError(response: Response): Promise<ApiError> {
  let body: unknown = null;
  try {
    body = await response.json();
  } catch {
    return new ApiError(response.status, null, null);
  }
  const parsed = problemSchema.safeParse(body);
  if (!parsed.success) return new ApiError(response.status, null, null);
  return new ApiError(response.status, parsed.data.code ?? null, parsed.data.traceId ?? null, Object.keys(parsed.data.errors ?? {}));
}

export type ErrorKind =
  | 'signed_out'
  | 'denied'
  | 'not_found'
  | 'filters'
  | 'conflict'
  | 'rate_limited'
  | 'unexpected'
  | 'network'
  | 'unavailable'
  | 'dev_token'
  | 'generic';

/** Which translated state a failure is shown as. */
export function errorKind(error: unknown): ErrorKind {
  if (!(error instanceof ApiError)) return 'generic';
  if (error.code === 'network') return 'network';
  if (error.code === 'unexpected_response') return 'unexpected';
  if (error.code === 'bff.dev_token_missing') return 'dev_token';
  switch (error.status) {
    case 400:
      return 'filters';
    case 401:
      return 'signed_out';
    case 403:
      return 'denied';
    case 404:
      return 'not_found';
    case 409:
      return 'conflict';
    case 429:
      return 'rate_limited';
    case 502:
    case 503:
    case 504:
      return 'unavailable';
    default:
      return 'generic';
  }
}

/** Client errors never get better by retrying; network and server failures may. */
export function shouldRetry(failureCount: number, error: unknown): boolean {
  if (error instanceof ApiError && error.status >= 400 && error.status < 500) return false;
  if (error instanceof ApiError && error.code === 'unexpected_response') return false;
  return failureCount < 2;
}
