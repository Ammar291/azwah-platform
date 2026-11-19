import { z, type ZodType } from 'zod';

export type ApiErrorKind =
  | 'not_configured'
  | 'network'
  | 'timeout'
  | 'server'
  | 'invalid_response'
  | 'bad_request'
  | 'unauthorized'
  | 'forbidden'
  | 'not_found'
  | 'conflict'
  | 'payload_too_large'
  /** 429 `requests.rate_limited`: too many requests in a short while; retried with backoff. */
  | 'rate_limited';

/**
 * What went wrong, in terms the UI can translate: a kind, the HTTP status and the ProblemDetails `code` (a stable
 * dotted code such as `sessions.required_items_unanswered`). Server-provided text is never kept or shown.
 */
export class ApiError extends Error {
  constructor(
    readonly kind: ApiErrorKind,
    readonly status?: number,
    readonly code?: string,
    /** The fixed safety notice a problem body may carry (D-135); never any server text. */
    readonly safetyNotice: 'crisis_support' | null = null,
  ) {
    super(`API request failed: ${kind}${status === undefined ? '' : ` (HTTP ${status})`}${code ? ` ${code}` : ''}`);
    this.name = 'ApiError';
  }
}

export type FetchLike = (input: string, init?: RequestInit) => Promise<Response>;

export function joinUrl(baseUrl: string, path: string): string {
  return `${baseUrl.replace(/\/+$/, '')}/${path.replace(/^\/+/, '')}`;
}

/** Fetch with a deadline. A failure to get any response becomes `ApiError('timeout' | 'network')`; call `done`. */
export async function fetchWithTimeout(
  url: string,
  init: RequestInit,
  timeoutMs: number,
  fetchImpl: FetchLike,
): Promise<{ response: Response; aborted: () => boolean; done: () => void }> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  const done = () => clearTimeout(timer);
  try {
    const response = await fetchImpl(url, { ...init, signal: controller.signal });
    return { response, aborted: () => controller.signal.aborted, done };
  } catch {
    done();
    throw new ApiError(controller.signal.aborted ? 'timeout' : 'network');
  }
}

export interface GetJsonOptions<T> {
  baseUrl: string;
  path: string;
  schema: ZodType<T>;
  /** Statuses whose body is still parsed, for example 503 from /health/ready. */
  acceptStatuses?: readonly number[];
  timeoutMs?: number;
  fetchImpl?: FetchLike;
}

/** GET an anonymous JSON resource (health) and validate it with Zod. Anything unexpected becomes an ApiError. */
export async function getJson<T>({
  baseUrl,
  path,
  schema,
  acceptStatuses = [200],
  timeoutMs = 5000,
  fetchImpl = fetch,
}: GetJsonOptions<T>): Promise<{ status: number; data: T }> {
  const { response, aborted, done } = await fetchWithTimeout(
    joinUrl(baseUrl, path),
    { headers: { Accept: 'application/json' } },
    timeoutMs,
    fetchImpl,
  );
  try {
    if (!acceptStatuses.includes(response.status)) {
      throw new ApiError(response.status >= 500 ? 'server' : 'invalid_response', response.status);
    }
    let body: unknown;
    try {
      body = await response.json();
    } catch {
      throw new ApiError(aborted() ? 'timeout' : 'invalid_response', response.status);
    }
    const parsed = schema.safeParse(body);
    if (!parsed.success) {
      throw new ApiError('invalid_response', response.status);
    }
    return { status: response.status, data: parsed.data };
  } finally {
    done();
  }
}

export function kindForStatus(status: number): ApiErrorKind {
  if (status >= 500) return 'server';
  switch (status) {
    case 400:
      return 'bad_request';
    case 401:
      return 'unauthorized';
    case 403:
      return 'forbidden';
    case 404:
      return 'not_found';
    case 409:
      return 'conflict';
    case 413:
      return 'payload_too_large';
    case 429:
      return 'rate_limited';
    default:
      return 'invalid_response';
  }
}

/**
 * Only the stable `code` extension and the fixed `safetyNotice` value are read from a ProblemDetails body; `title` and
 * `detail` are ignored. Each is read on its own, so a malformed one never hides the other.
 */
const problemCodeSchema = z.string().regex(/^[a-z0-9_]+(\.[a-z0-9_]+)+$/);
// Any string counts, as in the success schemas: a notice this app does not know still shows help.
const problemSafetyNoticeSchema = z.string();

async function readProblem(response: Response): Promise<{ code?: string; safetyNotice: 'crisis_support' | null }> {
  try {
    const body: unknown = await response.json();
    const fields = typeof body === 'object' && body !== null ? (body as Record<string, unknown>) : {};
    const code = problemCodeSchema.safeParse(fields.code);
    const notice = problemSafetyNoticeSchema.safeParse(fields.safetyNotice);
    return { code: code.success ? code.data : undefined, safetyNotice: notice.success ? 'crisis_support' : null };
  } catch {
    return { safetyNotice: null };
  }
}

export type HttpMethod = 'GET' | 'POST' | 'PUT' | 'PATCH';

export interface RequestJsonOptions<T> {
  baseUrl: string;
  method: HttpMethod;
  path: string;
  schema: ZodType<T>;
  body?: unknown;
  accessToken?: string | null;
  headers?: Record<string, string>;
  timeoutMs?: number;
  fetchImpl?: FetchLike;
}

/**
 * Calls a business endpoint (`/api/v1/…`) and validates the JSON response with Zod. Errors carry the kind, status
 * and problem code. Nothing about the request or response is logged.
 */
export async function requestJson<T>(options: RequestJsonOptions<T>): Promise<T> {
  return (await requestJsonWithBody(options)).data;
}

/**
 * requestJson that also hands back the body as the server sent it, once it has passed validation: for a copy that
 * must keep fields this app does not know (the person's data export). `data` is still the validated value.
 */
export async function requestJsonWithBody<T>({
  baseUrl,
  method,
  path,
  schema,
  body,
  accessToken,
  headers = {},
  timeoutMs = 15000,
  fetchImpl = fetch,
}: RequestJsonOptions<T>): Promise<{ data: T; raw: unknown }> {
  const requestHeaders: Record<string, string> = { Accept: 'application/json', ...headers };
  if (body !== undefined) {
    requestHeaders['Content-Type'] = 'application/json';
  }
  if (accessToken) {
    requestHeaders.Authorization = `Bearer ${accessToken}`;
  }
  const { response, aborted, done } = await fetchWithTimeout(
    joinUrl(baseUrl, path),
    { method, headers: requestHeaders, body: body === undefined ? undefined : JSON.stringify(body) },
    timeoutMs,
    fetchImpl,
  );
  try {
    if (response.status < 200 || response.status >= 300) {
      const kind = kindForStatus(response.status);
      const problem = await readProblem(response);
      // A server fault's code is not read (it says nothing the app acts on); its safety notice still is.
      throw new ApiError(kind, response.status, kind === 'server' ? undefined : problem.code, problem.safetyNotice);
    }
    let json: unknown;
    try {
      json = await response.json();
    } catch {
      throw new ApiError(aborted() ? 'timeout' : 'invalid_response', response.status);
    }
    const parsed = schema.safeParse(json);
    if (!parsed.success) {
      throw new ApiError('invalid_response', response.status);
    }
    return { data: parsed.data, raw: json };
  } finally {
    done();
  }
}

export function isApiError(error: unknown): error is ApiError {
  return error instanceof ApiError;
}

/** Network trouble, rate limits and server faults are worth retrying; answers about the request itself are not. */
export function isRetryable(error: unknown): boolean {
  return (
    isApiError(error) &&
    (error.kind === 'network' || error.kind === 'timeout' || error.kind === 'rate_limited' || error.kind === 'server')
  );
}
