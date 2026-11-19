import 'server-only';

import { NextResponse, type NextRequest } from 'next/server';
import { z } from 'zod';

import { PERSONAS } from '@/lib/personas';

import type { ConfigResult } from './config';
import {
  defaultOidcDeps,
  finishSignIn,
  OidcError,
  refresherFor,
  revokeRefreshToken,
  signOutUrl,
  startSignIn,
  TRANSACTION_SECONDS,
  type OidcDeps,
} from './oidc';
import { isSameOrigin, problem } from './problem';
import { sanitizeReturnTo } from './return-to';
import { sealSession, unsealSession, type Session } from './session';
import {
  clearSessionCookies,
  clearTransactionCookie,
  readSessionValue,
  transactionCookieName,
  writeSessionCookies,
  writeTransactionCookie,
} from './session-cookies';
import { recordSessionEvent } from './session-events';
import type { FetchApi } from './upstream';

/** The four `/auth/*` routes. The route files only pass in the request and the deployment's configuration. */

function signInPage(origin: string, query: Record<string, string> = {}): URL {
  const url = new URL('/sign-in', origin);
  for (const [key, value] of Object.entries(query)) url.searchParams.set(key, value);
  return url;
}

/**
 * The answer to a state-changing form post that did not come from this dashboard's configured origin. It is refused
 * whatever it asks for, and nothing is changed. A browser that posted a form (it asks for a page) is sent to the
 * sign-in page at the address it used, which explains the refusal in the reader's language: the usual cause is the
 * dashboard opened on another port or host than ADMIN_APP_ORIGIN names. Anything else gets the problem as JSON.
 * The location is relative, so the browser stays on the host and port it used and no Host header is trusted.
 */
function refuseCrossOrigin(request: NextRequest): Response {
  if (!(request.headers.get('accept') ?? '').includes('text/html')) return problem(403, 'auth.cross_origin');
  return new Response(null, { status: 303, headers: { Location: '/sign-in?error=cross_origin', 'Cache-Control': 'no-store' } });
}

/** `GET /auth/sign-in?returnTo=` starts the OIDC flow; other modes go to the sign-in page. */
export async function handleSignIn(request: NextRequest, result: ConfigResult, deps: OidcDeps = defaultOidcDeps): Promise<Response> {
  const origin = result.ok ? result.config.appOrigin : request.nextUrl.origin;
  const returnTo = sanitizeReturnTo(request.nextUrl.searchParams.get('returnTo'));
  if (!result.ok) return NextResponse.redirect(signInPage(origin), 303);
  const { config } = result;
  if (config.auth.mode !== 'oidc') {
    return NextResponse.redirect(signInPage(origin, returnTo === '/' ? {} : { returnTo }), 303);
  }
  try {
    const { url, transaction } = await startSignIn(config, returnTo, deps);
    const response = NextResponse.redirect(url, 303);
    writeTransactionCookie(response, transaction, config, TRANSACTION_SECONDS);
    return response;
  } catch (error) {
    const code = error instanceof OidcError ? error.code : 'issuer_unreachable';
    return NextResponse.redirect(signInPage(origin, { error: code }), 303);
  }
}

/** `GET /auth/callback` finishes the OIDC flow, seals the session and records the sign-in with the platform. */
export async function handleCallback(
  request: NextRequest,
  result: ConfigResult,
  deps: OidcDeps = defaultOidcDeps,
  fetchApi: FetchApi = fetch,
): Promise<Response> {
  if (!result.ok || result.config.auth.mode !== 'oidc') {
    return NextResponse.redirect(signInPage(result.ok ? result.config.appOrigin : request.nextUrl.origin), 303);
  }
  const { config } = result;
  const transaction = request.cookies.get(transactionCookieName(config.secureCookies))?.value;
  try {
    const { session, returnTo } = await finishSignIn(config, request.nextUrl, transaction, deps);
    await recordSessionEvent(config, session, 'sign-ins', fetchApi);
    const response = NextResponse.redirect(new URL(returnTo, config.appOrigin), 303);
    clearTransactionCookie(response, config);
    writeSessionCookies(response, request.cookies, await sealSession(session, config.sessionSecret, config.sessionMaxAgeSeconds), config);
    return response;
  } catch (error) {
    const code = error instanceof OidcError ? error.code : 'callback_rejected';
    const response = NextResponse.redirect(signInPage(config.appOrigin, { error: code }), 303);
    clearTransactionCookie(response, config);
    return response;
  }
}

/** An access token this close to expiry is renewed before the sign-out is recorded with it. */
const SIGN_OUT_TOKEN_MARGIN_MS = 30_000;

/**
 * `POST /auth/sign-out` from the dashboard's own form: records the sign-out with the platform (renewing an expired
 * access token first, so the record is not refused), revokes the refresh token at the issuer, clears the session, then
 * ends the issuer's session when it supports that. Each step is best effort; the sign-out always completes.
 */
export async function handleSignOut(
  request: NextRequest,
  result: ConfigResult,
  deps: OidcDeps = defaultOidcDeps,
  fetchApi: FetchApi = fetch,
  now: number = Date.now(),
): Promise<Response> {
  if (!result.ok) return problem(404, 'auth.not_configured');
  const { config } = result;
  if (!isSameOrigin(request, config.appOrigin)) return refuseCrossOrigin(request);
  let session: Session | null = await unsealSession(readSessionValue(request.cookies, config), config.sessionSecret, now);
  if (session?.kind === 'oidc' && session.accessTokenExpiresAt - now < SIGN_OUT_TOKEN_MARGIN_MS) {
    try {
      session = (await refresherFor(config, deps)(session, now)) ?? session;
    } catch {
      // Recorded with the old token, which the platform may refuse; signing out goes on.
    }
  }
  if (session) {
    await recordSessionEvent(config, session, 'sign-outs', fetchApi);
    if (session.kind === 'oidc' && session.refreshToken) await revokeRefreshToken(config, session.refreshToken, deps);
  }
  const issuerSignOut = await signOutUrl(config, deps);
  const response = NextResponse.redirect(issuerSignOut ?? signInPage(config.appOrigin, { signedOut: '1' }), 303);
  clearSessionCookies(response, request.cookies, config);
  return response;
}

const developmentForm = z.object({ persona: z.enum(PERSONAS), returnTo: z.string().optional() });

/** `POST /auth/development`: development sign-in as a persona. It does not exist in any other mode. */
export async function handleDevelopmentSignIn(
  request: NextRequest,
  result: ConfigResult,
  now: number = Date.now(),
  fetchApi: FetchApi = fetch,
): Promise<Response> {
  if (!result.ok || result.config.auth.mode !== 'development' || result.config.environment !== 'development') {
    return problem(404, 'auth.not_found');
  }
  const { config } = result;
  if (!isSameOrigin(request, config.appOrigin)) return refuseCrossOrigin(request);
  let form: FormData;
  try {
    form = await request.formData();
  } catch {
    return problem(400, 'auth.persona_invalid');
  }
  const parsed = developmentForm.safeParse({
    persona: form.get('persona'),
    returnTo: form.get('returnTo') ?? undefined,
  });
  if (!parsed.success) return problem(400, 'auth.persona_invalid');
  const session = { kind: 'development' as const, persona: parsed.data.persona, issuedAt: now };
  const sealed = await sealSession(session, config.sessionSecret, config.sessionMaxAgeSeconds);
  await recordSessionEvent(config, session, 'sign-ins', fetchApi);
  const response = NextResponse.redirect(new URL(sanitizeReturnTo(parsed.data.returnTo), config.appOrigin), 303);
  writeSessionCookies(response, request.cookies, sealed, config);
  return response;
}
