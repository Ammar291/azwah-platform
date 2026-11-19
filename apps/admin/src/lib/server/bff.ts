import 'server-only';

import { fetchApiStatus } from '@/lib/api/health';
import type { MockBackend } from '@/lib/mock/backend';

import { matchBffRoute, type BffRoute } from './bff-routes';
import type { AdminConfig } from './config';
import { isSameOrigin, problem } from './problem';
import type { Session } from './session';
import { bearerFor, callApi, type FetchApi } from './upstream';

/**
 * The backend-for-frontend: the browser's only way to the API. It forwards allowlisted requests with the session's
 * bearer token and keeps the API's status and body; it never decides what a role may do (the API does), and it logs
 * nothing about requests, responses or tokens.
 */

export interface BffDeps {
  config: AdminConfig;
  session: Session | null;
  fetchApi: FetchApi;
  mock: MockBackend | null;
  now?: Date;
}

/** Far above an export request of 30 column codes, matching the API's own limit. */
const MAX_BODY_BYTES = 16 * 1024;

const KEPT_HEADERS = ['content-type'] as const;

function forwardQuery(request: Request, route: BffRoute): URLSearchParams {
  const incoming = new URL(request.url).searchParams;
  const outgoing = new URLSearchParams();
  for (const key of route.query) {
    for (const value of incoming.getAll(key)) outgoing.append(key, value);
  }
  return outgoing;
}

function relay(upstream: Response, route: BffRoute): Response {
  const headers = new Headers({ 'Cache-Control': 'no-store', 'X-Content-Type-Options': 'nosniff' });
  for (const name of KEPT_HEADERS) {
    const value = upstream.headers.get(name);
    if (value) headers.set(name, value);
  }
  if (route.stream) {
    const disposition = upstream.headers.get('content-disposition');
    if (disposition) headers.set('Content-Disposition', disposition);
  }
  return new Response(upstream.body, { status: upstream.status, headers });
}

async function readJsonBody(request: Request): Promise<{ ok: true; text: string } | { ok: false; response: Response }> {
  const type = request.headers.get('content-type') ?? '';
  if (!/^application\/json(\s*;|$)/i.test(type)) return { ok: false, response: problem(415, 'bff.unsupported_media_type') };
  const declared = Number(request.headers.get('content-length') ?? '0');
  if (declared > MAX_BODY_BYTES) return { ok: false, response: problem(413, 'bff.body_too_large') };
  const text = await request.text();
  if (new TextEncoder().encode(text).byteLength > MAX_BODY_BYTES) return { ok: false, response: problem(413, 'bff.body_too_large') };
  try {
    JSON.parse(text);
  } catch {
    return { ok: false, response: problem(400, 'bff.body_invalid') };
  }
  return { ok: true, text };
}

async function health(deps: BffDeps): Promise<Response> {
  const status = deps.config.data.source === 'mock' ? 'mock' : await fetchApiStatus(deps.config.data.apiUrl, deps.fetchApi);
  return Response.json({ status }, { headers: { 'Cache-Control': 'no-store' } });
}

export async function handleBff(request: Request, segments: string[], deps: BffDeps): Promise<Response> {
  const matched = matchBffRoute(request.method, segments);
  if (!matched) return problem(404, 'bff.not_found');
  const { route, apiPath } = matched;
  const { config, session } = deps;
  if (!session) return problem(401, 'bff.signed_out');

  let body: string | null = null;
  if (route.method === 'POST') {
    if (!isSameOrigin(request, config.appOrigin)) return problem(403, 'bff.cross_origin');
    const read = await readJsonBody(request);
    if (!read.ok) return read.response;
    body = read.text;
  }
  const idempotencyKey = route.idempotent ? request.headers.get('idempotency-key') : null;

  if (route.local === 'health') return health(deps);

  if (config.data.source === 'mock') {
    if (!deps.mock || session.kind !== 'development') return problem(401, 'bff.signed_out');
    const upstream = await deps.mock.handle({
      method: route.method,
      apiPath,
      query: forwardQuery(request, route),
      body: body === null ? null : (JSON.parse(body) as unknown),
      persona: session.persona,
      idempotencyKey,
      now: deps.now ?? new Date(),
    });
    return relay(upstream, route);
  }

  const bearer = bearerFor(config, session);
  if (!bearer.ok) return bearer.reason === 'dev_token_missing' ? problem(503, 'bff.dev_token_missing') : problem(401, 'bff.signed_out');

  const query = forwardQuery(request, route).toString();
  const headers = new Headers();
  if (body !== null) headers.set('Content-Type', 'application/json');
  if (idempotencyKey) headers.set('Idempotency-Key', idempotencyKey);
  if (route.stream) headers.set('Accept', 'text/csv, application/problem+json');

  let upstream: Response;
  try {
    upstream = await callApi(
      config.data.apiUrl,
      bearer.token,
      query ? `${apiPath}?${query}` : apiPath,
      { method: route.method, headers, body },
      deps.fetchApi,
    );
  } catch {
    return problem(502, 'bff.api_unreachable');
  }
  if (upstream.status >= 300 && upstream.status < 400) return problem(502, 'bff.api_unreachable');
  return relay(upstream, route);
}
