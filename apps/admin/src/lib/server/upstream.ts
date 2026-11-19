import 'server-only';

import type { AdminConfig } from './config';
import type { Session } from './session';

/** How long the dashboard waits for the API before telling the user it is not answering. */
const API_TIMEOUT_MS = 15_000;

export type Bearer = { ok: true; token: string } | { ok: false; reason: 'dev_token_missing' | 'session_mismatch' };

/** The bearer token to call the API with for this session: never sent to the browser. */
export function bearerFor(config: AdminConfig, session: Session): Bearer {
  if (session.kind === 'oidc') {
    return config.auth.mode === 'oidc' ? { ok: true, token: session.accessToken } : { ok: false, reason: 'session_mismatch' };
  }
  if (config.auth.mode !== 'development') return { ok: false, reason: 'session_mismatch' };
  const token = config.auth.tokens[session.persona];
  return token ? { ok: true, token } : { ok: false, reason: 'dev_token_missing' };
}

export type FetchApi = (input: string, init?: RequestInit) => Promise<Response>;

/** Calls the API with the caller's token. Nothing about the request or the response is logged. */
export function callApi(
  apiUrl: string,
  token: string,
  path: string,
  init: RequestInit = {},
  fetchApi: FetchApi = fetch,
): Promise<Response> {
  const headers = new Headers(init.headers);
  headers.set('Authorization', `Bearer ${token}`);
  if (!headers.has('Accept')) headers.set('Accept', 'application/json, application/problem+json');
  return fetchApi(`${apiUrl}${path}`, {
    ...init,
    headers,
    cache: 'no-store',
    redirect: 'manual',
    signal: init.signal ?? AbortSignal.timeout(API_TIMEOUT_MS),
  });
}
