import 'server-only';

import { EncryptJWT, jwtDecrypt } from 'jose';
import * as client from 'openid-client';
import { z } from 'zod';

import type { AdminConfig, AuthConfig } from './config';
import { sanitizeReturnTo } from './return-to';
import type { OidcSession } from './session';

/**
 * OpenID Connect authorization code flow with PKCE, run entirely on the dashboard's server (a backend-for-frontend).
 * The browser only ever sees the issuer's pages and HttpOnly cookies.
 */

export type OidcErrorCode = 'not_configured' | 'issuer_unreachable' | 'transaction_missing' | 'callback_rejected';

export class OidcError extends Error {
  constructor(readonly code: OidcErrorCode) {
    super(code);
    this.name = 'OidcError';
  }
}

type OidcAuth = Extract<AuthConfig, { mode: 'oidc' }>;

export interface OidcDeps {
  load(auth: OidcAuth, environment: AdminConfig['environment']): Promise<client.Configuration>;
}

/** Ten minutes to finish signing in at the issuer. */
export const TRANSACTION_SECONDS = 600;

/** Without `expires_in`, assume a short-lived token and refresh early rather than late. */
const DEFAULT_TOKEN_SECONDS = 300;

const discovered = new Map<string, Promise<client.Configuration>>();

export const defaultOidcDeps: OidcDeps = {
  load(auth, environment) {
    const key = `${auth.issuer.href}|${auth.clientId}`;
    let pending = discovered.get(key);
    if (!pending) {
      pending = client
        .discovery(
          auth.issuer,
          auth.clientId,
          auth.clientSecret ?? undefined,
          auth.clientSecret ? client.ClientSecretPost(auth.clientSecret) : client.None(),
          {
            timeout: 10,
            // A local issuer over plain HTTP is allowed only in development (configuration refuses it elsewhere).
            execute: environment === 'development' && auth.issuer.protocol === 'http:' ? [client.allowInsecureRequests] : [],
          },
        )
        .catch((error: unknown) => {
          discovered.delete(key);
          throw error;
        });
      discovered.set(key, pending);
    }
    return pending;
  },
};

function oidcAuth(config: AdminConfig): OidcAuth {
  if (config.auth.mode !== 'oidc') throw new OidcError('not_configured');
  return config.auth;
}

async function load(config: AdminConfig, deps: OidcDeps): Promise<client.Configuration> {
  try {
    return await deps.load(oidcAuth(config), config.environment);
  } catch (error) {
    throw error instanceof OidcError ? error : new OidcError('issuer_unreachable');
  }
}

const transactionSchema = z.object({
  verifier: z.string().min(43),
  state: z.string().min(1),
  nonce: z.string().min(1),
  returnTo: z.string(),
});

type Transaction = z.infer<typeof transactionSchema>;

async function sealTransaction(transaction: Transaction, secret: Uint8Array, now: number): Promise<string> {
  const issuedAt = Math.floor(now / 1000);
  return new EncryptJWT({ transaction })
    .setProtectedHeader({ alg: 'dir', enc: 'A256GCM' })
    .setIssuedAt(issuedAt)
    .setExpirationTime(issuedAt + TRANSACTION_SECONDS)
    .encrypt(secret);
}

async function unsealTransaction(value: string | undefined, secret: Uint8Array, now: number): Promise<Transaction | null> {
  if (!value) return null;
  try {
    const { payload } = await jwtDecrypt(value, secret, { currentDate: new Date(now) });
    const parsed = transactionSchema.safeParse(payload.transaction);
    return parsed.success ? parsed.data : null;
  } catch {
    return null;
  }
}

function redirectUri(config: AdminConfig): string {
  return `${config.appOrigin}/auth/callback`;
}

/** The issuer URL to send the browser to, and the sealed transaction to keep in a short-lived cookie. */
export async function startSignIn(
  config: AdminConfig,
  returnTo: string,
  deps: OidcDeps = defaultOidcDeps,
  now: number = Date.now(),
): Promise<{ url: URL; transaction: string }> {
  const auth = oidcAuth(config);
  const configuration = await load(config, deps);
  const verifier = client.randomPKCECodeVerifier();
  const transaction: Transaction = {
    verifier,
    state: client.randomState(),
    nonce: client.randomNonce(),
    returnTo: sanitizeReturnTo(returnTo),
  };
  const parameters: Record<string, string> = {
    redirect_uri: redirectUri(config),
    scope: auth.scope,
    code_challenge: await client.calculatePKCECodeChallenge(verifier),
    code_challenge_method: 'S256',
    state: transaction.state,
    nonce: transaction.nonce,
  };
  if (auth.resource) parameters.resource = auth.resource;
  return {
    url: client.buildAuthorizationUrl(configuration, parameters),
    transaction: await sealTransaction(transaction, config.sessionSecret, now),
  };
}

/** Exchanges the callback's code for tokens, checking state, PKCE and the ID token's nonce. */
export async function finishSignIn(
  config: AdminConfig,
  callbackUrl: URL,
  sealedTransaction: string | undefined,
  deps: OidcDeps = defaultOidcDeps,
  now: number = Date.now(),
): Promise<{ session: OidcSession; returnTo: string }> {
  const auth = oidcAuth(config);
  const transaction = await unsealTransaction(sealedTransaction, config.sessionSecret, now);
  if (!transaction) throw new OidcError('transaction_missing');
  const configuration = await load(config, deps);
  // The issuer redirected to the public origin; rebuild the URL there in case a proxy changed the host.
  const publicUrl = new URL(`${redirectUri(config)}${callbackUrl.search}`);
  let tokens: Awaited<ReturnType<typeof client.authorizationCodeGrant>>;
  try {
    tokens = await client.authorizationCodeGrant(
      configuration,
      publicUrl,
      {
        pkceCodeVerifier: transaction.verifier,
        expectedState: transaction.state,
        expectedNonce: transaction.nonce,
        idTokenExpected: true,
      },
      auth.resource ? { resource: auth.resource } : undefined,
    );
  } catch {
    throw new OidcError('callback_rejected');
  }
  return {
    session: {
      kind: 'oidc',
      accessToken: tokens.access_token,
      refreshToken: tokens.refresh_token ?? null,
      accessTokenExpiresAt: now + (tokens.expiresIn() ?? DEFAULT_TOKEN_SECONDS) * 1000,
      issuedAt: now,
    },
    returnTo: transaction.returnTo,
  };
}

export interface RefreshedTokens {
  accessToken: string;
  refreshToken: string | null;
  expiresIn: number | null;
}

export type Refresh = (session: OidcSession, now?: number) => Promise<OidcSession | null>;

/** How long a refresh result answers requests that still carry the old cookie. */
const SETTLED_RESULT_MS = 30_000;

/**
 * A refresher that spends each refresh token once per server process: concurrent requests, and requests that arrive
 * shortly after with the old cookie, share one result. Issuers that rotate refresh tokens would otherwise see reuse
 * and revoke the session. A deployment with several instances needs a shared session store instead (see README).
 */
export function createRefresher(grant: (refreshToken: string) => Promise<RefreshedTokens>): Refresh {
  const results = new Map<string, Promise<OidcSession | null>>();
  return (session, now = Date.now()) => {
    const refreshToken = session.refreshToken;
    if (!refreshToken) return Promise.resolve(null);
    const known = results.get(refreshToken);
    if (known) return known;
    const pending = grant(refreshToken)
      .then(
        (tokens): OidcSession => ({
          ...session,
          accessToken: tokens.accessToken,
          refreshToken: tokens.refreshToken ?? refreshToken,
          accessTokenExpiresAt: now + (tokens.expiresIn ?? DEFAULT_TOKEN_SECONDS) * 1000,
        }),
      )
      .catch(() => null)
      .finally(() => {
        setTimeout(() => results.delete(refreshToken), SETTLED_RESULT_MS).unref?.();
      });
    results.set(refreshToken, pending);
    return pending;
  };
}

const refreshers = new Map<string, Refresh>();

/** The process-wide refresher for a configuration, using the issuer's token endpoint. */
export function refresherFor(config: AdminConfig, deps: OidcDeps = defaultOidcDeps): Refresh {
  const auth = oidcAuth(config);
  const key = `${auth.issuer.href}|${auth.clientId}`;
  const known = refreshers.get(key);
  if (known) return known;
  const refresh = createRefresher(async (refreshToken) => {
    const tokens = await client.refreshTokenGrant(
      await load(config, deps),
      refreshToken,
      auth.resource ? { resource: auth.resource } : undefined,
    );
    return { accessToken: tokens.access_token, refreshToken: tokens.refresh_token ?? null, expiresIn: tokens.expiresIn() ?? null };
  });
  refreshers.set(key, refresh);
  return refresh;
}

/**
 * Revokes the session's refresh token at the issuer (RFC 7009) when the issuer has a revocation endpoint, so a copy of
 * the cookie taken before sign-out cannot be refreshed. Best effort: a failure never stops the sign-out.
 */
export async function revokeRefreshToken(config: AdminConfig, refreshToken: string, deps: OidcDeps = defaultOidcDeps): Promise<void> {
  if (config.auth.mode !== 'oidc') return;
  try {
    const configuration = await load(config, deps);
    if (!configuration.serverMetadata().revocation_endpoint) return;
    await client.tokenRevocation(configuration, refreshToken, { token_type_hint: 'refresh_token' });
  } catch {
    // The token still expires on the issuer's own schedule.
  }
}

/** Where to send the browser to end the issuer's session too, when the issuer supports it. */
export async function signOutUrl(config: AdminConfig, deps: OidcDeps = defaultOidcDeps): Promise<URL | null> {
  if (config.auth.mode !== 'oidc') return null;
  try {
    const configuration = await load(config, deps);
    if (!configuration.serverMetadata().end_session_endpoint) return null;
    return client.buildEndSessionUrl(configuration, {
      client_id: config.auth.clientId,
      post_logout_redirect_uri: `${config.appOrigin}/sign-in?signedOut=1`,
    });
  } catch {
    return null;
  }
}
