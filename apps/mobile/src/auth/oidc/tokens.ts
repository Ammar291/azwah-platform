import { z } from 'zod';

import { fetchWithTimeout, type FetchLike } from '@/api/client';
import { utf8Decode } from '@/storage/protection/utf8';

import { profileFromIdTokenClaims, type IdentityProfile } from '../identityProfile';
import { SignInError } from '../types';
import { base64UrlDecode } from './pkce';

const TOKEN_TIMEOUT_MS = 15_000;
/** Clock skew allowed between the phone and the identity provider. */
export const CLOCK_SKEW_SECONDS = 60;

/** `application/x-www-form-urlencoded`, leaving out absent values. */
export function formEncode(values: Record<string, string | null | undefined>): string {
  return Object.entries(values)
    .filter((entry): entry is [string, string] => typeof entry[1] === 'string')
    .map(([name, value]) => `${encodeURIComponent(name)}=${encodeURIComponent(value)}`)
    .join('&');
}

export function authorizationCodeBody(request: {
  code: string;
  codeVerifier: string;
  redirectUri: string;
  clientId: string;
  resource: string | null;
}): string {
  return formEncode({
    grant_type: 'authorization_code',
    code: request.code,
    code_verifier: request.codeVerifier,
    redirect_uri: request.redirectUri,
    client_id: request.clientId,
    resource: request.resource,
  });
}

export function refreshTokenBody(request: { refreshToken: string; clientId: string; resource: string | null }): string {
  return formEncode({
    grant_type: 'refresh_token',
    refresh_token: request.refreshToken,
    client_id: request.clientId,
    resource: request.resource,
  });
}

const tokenResponseSchema = z.object({
  token_type: z.string().refine((value) => value.toLowerCase() === 'bearer'),
  access_token: z.string().min(1),
  expires_in: z.union([z.number(), z.string().regex(/^\d+$/).transform(Number)]).pipe(z.number().positive()),
  refresh_token: z.string().min(1).optional(),
  id_token: z.string().min(1).optional(),
});

const oauthErrorSchema = z.object({ error: z.string() });

export interface TokenSet {
  accessToken: string;
  /** Seconds. */
  expiresIn: number;
  refreshToken: string | null;
  idToken: string | null;
}

/**
 * Why a token request failed. `invalid_grant` and `rejected` (any other 4xx) are the provider's final answer;
 * `network`, `server` (5xx, 429) and `invalid_response` may be temporary.
 */
export type TokenFailure = 'network' | 'server' | 'invalid_grant' | 'rejected' | 'invalid_response';

export class TokenRequestError extends Error {
  constructor(readonly reason: TokenFailure) {
    super(`Token request failed: ${reason}`);
    this.name = 'TokenRequestError';
  }
}

async function readOAuthError(response: Response): Promise<string | null> {
  try {
    const parsed = oauthErrorSchema.safeParse(await response.json());
    return parsed.success ? parsed.data.error : null;
  } catch {
    return null;
  }
}

/** POSTs a form to the token endpoint and validates the answer. Nothing about it is logged. */
export async function requestTokens(
  endpoint: string,
  body: string,
  fetchImpl: FetchLike,
  timeoutMs: number = TOKEN_TIMEOUT_MS,
): Promise<TokenSet> {
  let request: Awaited<ReturnType<typeof fetchWithTimeout>>;
  try {
    request = await fetchWithTimeout(
      endpoint,
      {
        method: 'POST',
        headers: { Accept: 'application/json', 'Content-Type': 'application/x-www-form-urlencoded' },
        body,
      },
      timeoutMs,
      fetchImpl,
    );
  } catch {
    throw new TokenRequestError('network');
  }
  const { response, done } = request;
  try {
    if (response.status >= 500 || response.status === 429) {
      throw new TokenRequestError('server');
    }
    if (response.status !== 200) {
      if (response.status < 400) {
        throw new TokenRequestError('invalid_response');
      }
      throw new TokenRequestError((await readOAuthError(response)) === 'invalid_grant' ? 'invalid_grant' : 'rejected');
    }
    let json: unknown;
    try {
      json = await response.json();
    } catch {
      throw new TokenRequestError(request.aborted() ? 'network' : 'invalid_response');
    }
    const parsed = tokenResponseSchema.safeParse(json);
    if (!parsed.success) {
      throw new TokenRequestError('invalid_response');
    }
    return {
      accessToken: parsed.data.access_token,
      expiresIn: parsed.data.expires_in,
      refreshToken: parsed.data.refresh_token ?? null,
      idToken: parsed.data.id_token ?? null,
    };
  } finally {
    done();
  }
}

const jwtHeaderSchema = z.object({ alg: z.string().refine((alg) => alg.toLowerCase() !== 'none') });

const idTokenClaimsSchema = z.object({
  iss: z.string(),
  sub: z.string().min(1),
  aud: z.union([z.string(), z.array(z.string()).min(1)]),
  exp: z.number(),
  iat: z.number(),
  azp: z.string().optional(),
  nonce: z.string().optional(),
});

function decodeJwtPart(part: string): unknown {
  return JSON.parse(utf8Decode(base64UrlDecode(part)));
}

export interface IdTokenExpectations {
  issuer: string;
  clientId: string;
  /** The phone's clock, in milliseconds. */
  now: number;
  /** The nonce of the authorization request; null on refresh, where none is expected (OIDC Core 12.2). */
  nonce: string | null;
  /** On refresh: the subject of the session, which must not change. */
  subject?: string;
}

/**
 * Checks an ID token's claims (OIDC Core 3.1.3.7). Its signature is not checked on the phone: the token came straight
 * from the token endpoint over TLS (step 6 of that section). Anything wrong is `unsafe_response`. Also reads the
 * profile the provider released, if any (identityProfile.ts); a malformed profile is dropped, not fatal.
 */
export function validateIdToken(
  idToken: string,
  expected: IdTokenExpectations,
): { subject: string; profile: IdentityProfile | null } {
  const parts = idToken.split('.');
  if (parts.length !== 3 || parts.some((part) => part === '')) {
    throw new SignInError('unsafe_response');
  }
  let header: unknown;
  let payload: unknown;
  try {
    header = decodeJwtPart(parts[0] ?? '');
    payload = decodeJwtPart(parts[1] ?? '');
  } catch {
    throw new SignInError('unsafe_response');
  }
  const parsedHeader = jwtHeaderSchema.safeParse(header);
  const parsed = idTokenClaimsSchema.safeParse(payload);
  if (!parsedHeader.success || !parsed.success) {
    throw new SignInError('unsafe_response');
  }
  const claims = parsed.data;
  const audiences = typeof claims.aud === 'string' ? [claims.aud] : claims.aud;
  const nowSeconds = expected.now / 1000;
  const valid =
    claims.iss === expected.issuer &&
    audiences.includes(expected.clientId) &&
    (audiences.length === 1 || claims.azp !== undefined) &&
    (claims.azp === undefined || claims.azp === expected.clientId) &&
    claims.exp + CLOCK_SKEW_SECONDS > nowSeconds &&
    claims.iat <= nowSeconds + CLOCK_SKEW_SECONDS &&
    (expected.nonce === null || claims.nonce === expected.nonce) &&
    (expected.subject === undefined || claims.sub === expected.subject);
  if (!valid) {
    throw new SignInError('unsafe_response');
  }
  return { subject: claims.sub, profile: profileFromIdTokenClaims(payload) };
}
