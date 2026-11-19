import 'server-only';

import { NextResponse, type NextRequest } from 'next/server';

import type { AdminConfig, ConfigResult } from './config';
import { cookieNamesFor, splitCookie } from './cookie-chunks';
import { refresherFor, type Refresh } from './oidc';
import { sealSession, sessionCookieName, unsealSession } from './session';
import { clearSessionCookies, readSessionValue, writeSessionCookies, type CookieReader } from './session-cookies';

/**
 * What the proxy does with a request for a dashboard page or the BFF. It is an optimistic check: no session means
 * sign in, an access token about to expire is refreshed here (the only place that may rewrite the session cookie
 * before pages render). Pages and the API still decide access themselves.
 */

export interface ProxyDeps {
  result: ConfigResult;
  refresh?: Refresh;
  now?: number;
}

/** Refresh this long before the access token expires, so a page's API calls never carry an expired token. */
const REFRESH_MARGIN_MS = 60_000;

function problemNext(status: number, code: string): NextResponse {
  return NextResponse.json(
    { status, code },
    { status, headers: { 'Content-Type': 'application/problem+json', 'Cache-Control': 'no-store' } },
  );
}

function isBff(request: NextRequest): boolean {
  return request.nextUrl.pathname.startsWith('/bff/');
}

function signedOut(request: NextRequest, config: AdminConfig, clear: boolean): NextResponse {
  let response: NextResponse;
  if (isBff(request)) {
    response = problemNext(401, 'bff.signed_out');
  } else {
    const target = new URL('/sign-in', config.appOrigin);
    const path = `${request.nextUrl.pathname}${request.nextUrl.search}`;
    if (path !== '/') target.searchParams.set('returnTo', path);
    response = NextResponse.redirect(target, 307);
  }
  if (clear) clearSessionCookies(response, request.cookies, config);
  return response;
}

export async function decide(request: NextRequest, deps: ProxyDeps): Promise<NextResponse> {
  const now = deps.now ?? Date.now();
  if (!deps.result.ok) {
    return isBff(request) ? problemNext(503, 'bff.not_configured') : NextResponse.redirect(new URL('/sign-in', request.nextUrl.origin), 307);
  }
  const { config } = deps.result;
  const value = readSessionValue(request.cookies, config);
  const session = await unsealSession(value, config.sessionSecret, now);
  if (!session) return signedOut(request, config, value !== undefined || request.cookies.has(sessionCookieName(config.secureCookies)));

  if (session.kind !== 'oidc' || session.accessTokenExpiresAt - now >= REFRESH_MARGIN_MS) return NextResponse.next();

  const refresh = deps.refresh ?? refresherFor(config);
  const renewed = await refresh(session, now);
  if (!renewed) return signedOut(request, config, true);

  const sealed = await sealSession(renewed, config.sessionSecret, config.sessionMaxAgeSeconds);
  const original = new Map(request.cookies.getAll().map((cookie) => [cookie.name, cookie.value]));
  const before: CookieReader = { get: (name) => (original.has(name) ? { value: original.get(name) ?? '' } : undefined) };
  const name = sessionCookieName(config.secureCookies);
  for (const stale of cookieNamesFor(name, (cookie) => original.get(cookie))) request.cookies.delete(stale);
  for (const chunk of splitCookie(name, sealed)) request.cookies.set(chunk.name, chunk.value);
  const response = NextResponse.next({ request: { headers: request.headers } });
  writeSessionCookies(response, before, sealed, config);
  return response;
}
