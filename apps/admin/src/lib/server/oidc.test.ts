// @vitest-environment node
import * as client from 'openid-client';
import { describe, expect, it, vi } from 'vitest';

import { oidcTestConfig } from '@/test/config';

import { createRefresher, finishSignIn, OidcError, revokeRefreshToken, signOutUrl, startSignIn, type OidcDeps } from './oidc';
import type { OidcSession } from './session';

const config = oidcTestConfig();
const now = Date.UTC(2026, 9, 16, 9, 0, 0);

const deps: OidcDeps = {
  load: async () =>
    new client.Configuration(
      {
        issuer: 'https://issuer.example.org',
        authorization_endpoint: 'https://issuer.example.org/authorize',
        token_endpoint: 'https://issuer.example.org/token',
        end_session_endpoint: 'https://issuer.example.org/logout',
      },
      'azwah-admin',
    ),
};

describe('startSignIn', () => {
  it('sends the browser to the issuer with PKCE, state and nonce, and seals them in a transaction', async () => {
    const { url, transaction } = await startSignIn(config, '/exports', deps, now);

    expect(url.origin + url.pathname).toBe('https://issuer.example.org/authorize');
    expect(url.searchParams.get('response_type')).toBe('code');
    expect(url.searchParams.get('client_id')).toBe('azwah-admin');
    expect(url.searchParams.get('redirect_uri')).toBe('https://admin.example.org/auth/callback');
    expect(url.searchParams.get('scope')).toBe('openid offline_access');
    expect(url.searchParams.get('code_challenge_method')).toBe('S256');
    expect(url.searchParams.get('code_challenge')).toMatch(/^[A-Za-z0-9_-]{43}$/);
    expect(url.searchParams.get('state')).toBeTruthy();
    expect(url.searchParams.get('nonce')).toBeTruthy();
    expect(transaction).not.toContain(url.searchParams.get('state'));
  });

  it('asks for the API as the resource when one is configured', async () => {
    const withResource = oidcTestConfig({
      auth: { ...(config.auth as Extract<typeof config.auth, { mode: 'oidc' }>), resource: 'https://api.example.org' },
    });

    const { url } = await startSignIn(withResource, '/', deps, now);

    expect(url.searchParams.get('resource')).toBe('https://api.example.org');
  });
});

describe('finishSignIn', () => {
  it('refuses a callback without a transaction', async () => {
    await expect(
      finishSignIn(config, new URL('https://admin.example.org/auth/callback?code=c&state=s'), undefined, deps, now),
    ).rejects.toEqual(new OidcError('transaction_missing'));
  });

  it('refuses a transaction older than ten minutes', async () => {
    const { transaction } = await startSignIn(config, '/', deps, now);

    await expect(
      finishSignIn(config, new URL('https://admin.example.org/auth/callback?code=c&state=s'), transaction, deps, now + 601_000),
    ).rejects.toEqual(new OidcError('transaction_missing'));
  });

  it('refuses a callback whose state does not match the transaction', async () => {
    const { transaction } = await startSignIn(config, '/', deps, now);

    await expect(
      finishSignIn(config, new URL('https://admin.example.org/auth/callback?code=c&state=forged'), transaction, deps, now),
    ).rejects.toEqual(new OidcError('callback_rejected'));
  });

  it('refuses a callback that carries an error from the issuer', async () => {
    const { url, transaction } = await startSignIn(config, '/', deps, now);
    const state = url.searchParams.get('state') ?? '';

    await expect(
      finishSignIn(
        config,
        new URL(`https://admin.example.org/auth/callback?error=access_denied&state=${state}`),
        transaction,
        deps,
        now,
      ),
    ).rejects.toEqual(new OidcError('callback_rejected'));
  });
});

describe('createRefresher', () => {
  const session: OidcSession = {
    kind: 'oidc',
    accessToken: 'old-access',
    refreshToken: 'old-refresh',
    accessTokenExpiresAt: now + 30_000,
    issuedAt: now - 60_000,
  };

  it('spends a refresh token once for concurrent requests', async () => {
    const grant = vi.fn(async () => ({ accessToken: 'new-access', refreshToken: 'new-refresh', expiresIn: 300 }));
    const refresh = createRefresher(grant);

    const [first, second] = await Promise.all([refresh(session, now), refresh(session, now)]);

    expect(grant).toHaveBeenCalledTimes(1);
    expect(first).toEqual({ ...session, accessToken: 'new-access', refreshToken: 'new-refresh', accessTokenExpiresAt: now + 300_000 });
    expect(second).toEqual(first);
  });

  it('answers a late request that still carries the old refresh token with the same result', async () => {
    const grant = vi.fn(async () => ({ accessToken: 'new-access', refreshToken: 'new-refresh', expiresIn: 300 }));
    const refresh = createRefresher(grant);

    const first = await refresh(session, now);
    const late = await refresh(session, now + 5_000);

    expect(grant).toHaveBeenCalledTimes(1);
    expect(late).toEqual(first);
  });

  it('keeps the refresh token when the issuer does not rotate it', async () => {
    const refresh = createRefresher(async () => ({ accessToken: 'new-access', refreshToken: null, expiresIn: 60 }));

    expect((await refresh(session, now))?.refreshToken).toBe('old-refresh');
  });

  it('signs out when the issuer refuses the refresh token', async () => {
    const refresh = createRefresher(async () => {
      throw new Error('invalid_grant');
    });

    expect(await refresh(session, now)).toBeNull();
  });

  it('signs out when there is no refresh token', async () => {
    const grant = vi.fn();
    const refresh = createRefresher(grant);

    expect(await refresh({ ...session, refreshToken: null }, now)).toBeNull();
    expect(grant).not.toHaveBeenCalled();
  });
});

describe('revokeRefreshToken', () => {
  function revocationDeps(fetchImpl: client.CustomFetch, metadata: Partial<client.ServerMetadata> = {}): OidcDeps {
    return {
      load: async () => {
        const configuration = new client.Configuration(
          {
            issuer: 'https://issuer.example.org',
            token_endpoint: 'https://issuer.example.org/token',
            revocation_endpoint: 'https://issuer.example.org/revoke',
            ...metadata,
          },
          'azwah-admin',
        );
        configuration[client.customFetch] = fetchImpl;
        return configuration;
      },
    };
  }

  it('revokes the refresh token at the issuer', async () => {
    const fetchImpl = vi.fn<client.CustomFetch>(async () => new Response(null, { status: 200 }));

    await revokeRefreshToken(config, 'refresh-to-revoke', revocationDeps(fetchImpl));

    expect(fetchImpl).toHaveBeenCalledTimes(1);
    const [url, options] = fetchImpl.mock.calls[0] ?? [];
    expect(url).toBe('https://issuer.example.org/revoke');
    const body = new URLSearchParams(String(options?.body));
    expect(body.get('token')).toBe('refresh-to-revoke');
    expect(body.get('token_type_hint')).toBe('refresh_token');
  });

  it('does nothing when the issuer has no revocation endpoint', async () => {
    const fetchImpl = vi.fn<client.CustomFetch>(async () => new Response(null, { status: 200 }));

    await revokeRefreshToken(config, 'refresh-to-revoke', revocationDeps(fetchImpl, { revocation_endpoint: undefined }));

    expect(fetchImpl).not.toHaveBeenCalled();
  });

  it('never throws when the issuer fails', async () => {
    const fetchImpl = vi.fn<client.CustomFetch>(async () => new Response('{"error":"server_error"}', { status: 503 }));

    await expect(revokeRefreshToken(config, 'refresh-to-revoke', revocationDeps(fetchImpl))).resolves.toBeUndefined();
  });
});

describe('signOutUrl', () => {
  it('uses the issuer end-session endpoint with the client and the sign-in page', async () => {
    const url = await signOutUrl(config, deps);

    expect(url?.origin + (url?.pathname ?? '')).toBe('https://issuer.example.org/logout');
    expect(url?.searchParams.get('client_id')).toBe('azwah-admin');
    expect(url?.searchParams.get('post_logout_redirect_uri')).toBe('https://admin.example.org/sign-in?signedOut=1');
  });

  it('has nothing to call in development sign-in', async () => {
    const { testConfig } = await import('@/test/config');

    expect(await signOutUrl(testConfig(), deps)).toBeNull();
  });
});
