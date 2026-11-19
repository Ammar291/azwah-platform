// @vitest-environment node
import { NextRequest } from 'next/server';
import * as client from 'openid-client';
import { describe, expect, it, vi } from 'vitest';

import { oidcTestConfig, testConfig } from '@/test/config';

import { handleCallback, handleDevelopmentSignIn, handleSignIn, handleSignOut } from './auth-handlers';
import { startSignIn, type OidcDeps } from './oidc';
import { sealSession, unsealSession } from './session';

const config = testConfig();
const ok = { ok: true as const, config };

function post(path: string, body: Record<string, string>, origin: string | null = 'http://localhost:3000', cookie?: string) {
  const headers = new Headers({ 'content-type': 'application/x-www-form-urlencoded' });
  if (origin) headers.set('origin', origin);
  if (cookie) headers.set('cookie', cookie);
  return new NextRequest(`http://localhost:3000${path}`, { method: 'POST', headers, body: new URLSearchParams(body) });
}

/** A form posted by a browser from the page at `url`: its origin is the page's own, and it asks for a page back. */
function browserPost(url: string, body: Record<string, string>, cookie?: string) {
  const headers = new Headers({ 'content-type': 'application/x-www-form-urlencoded', origin: new URL(url).origin, accept: 'text/html,application/xhtml+xml,*/*;q=0.8' });
  if (cookie) headers.set('cookie', cookie);
  return new NextRequest(url, { method: 'POST', headers, body: new URLSearchParams(body) });
}

function cookieValue(response: Response, name: string): string | undefined {
  const header = response.headers.getSetCookie().find((line) => line.startsWith(`${name}=`));
  return header?.slice(name.length + 1).split(';')[0];
}

describe('handleDevelopmentSignIn', () => {
  it('signs a known persona in, records the sign-in with the platform and goes to the requested page', async () => {
    const fetchApi = vi.fn(async () => new Response(null, { status: 204 }));

    const response = await handleDevelopmentSignIn(
      post('/auth/development', { persona: 'researcher', returnTo: '/exports' }),
      ok,
      Date.now(),
      fetchApi,
    );

    expect(fetchApi).toHaveBeenCalledWith('http://api.test/api/v1/me/sign-ins', expect.objectContaining({ method: 'POST' }));

    expect(response.status).toBe(303);
    expect(response.headers.get('location')).toBe('http://localhost:3000/exports');
    const setCookie = response.headers.getSetCookie().join('\n');
    expect(setCookie).toMatch(/azwah_session=[^;]+;.*HttpOnly/i);
    expect(setCookie).toMatch(/SameSite=lax/i);
    const session = await unsealSession(cookieValue(response, 'azwah_session'), config.sessionSecret);
    expect(session).toMatchObject({ kind: 'development', persona: 'researcher' });
  });

  it('goes home instead of an unsafe return path', async () => {
    const response = await handleDevelopmentSignIn(
      post('/auth/development', { persona: 'researcher', returnTo: '//evil.example' }),
      ok,
    );

    expect(response.headers.get('location')).toBe('http://localhost:3000/');
  });

  it('refuses an unknown persona', async () => {
    const response = await handleDevelopmentSignIn(post('/auth/development', { persona: 'root' }), ok);

    expect(response.status).toBe(400);
    expect(response.headers.getSetCookie()).toEqual([]);
  });

  it('refuses a request from another origin, or with no origin', async () => {
    expect((await handleDevelopmentSignIn(post('/auth/development', { persona: 'researcher' }, 'https://evil.example'), ok)).status).toBe(403);
    expect((await handleDevelopmentSignIn(post('/auth/development', { persona: 'researcher' }, null), ok)).status).toBe(403);
  });

  it('explains the refusal on the sign-in page when a browser posted the form from another address, and signs no one in', async () => {
    const response = await handleDevelopmentSignIn(browserPost('http://localhost:3001/auth/development', { persona: 'researcher' }), ok);

    expect(response.status).toBe(303);
    // Relative, so the browser stays at http://localhost:3001, the address it used.
    expect(response.headers.get('location')).toBe('/sign-in?error=cross_origin');
    expect(response.headers.getSetCookie()).toEqual([]);
  });

  it('does not exist in OIDC mode or without configuration', async () => {
    const oidc = { ok: true as const, config: oidcTestConfig({ appOrigin: 'http://localhost:3000' }) };

    expect((await handleDevelopmentSignIn(post('/auth/development', { persona: 'researcher' }), oidc)).status).toBe(404);
    expect(
      (await handleDevelopmentSignIn(post('/auth/development', { persona: 'researcher' }), { ok: false, problems: ['session_secret_missing'] })).status,
    ).toBe(404);
  });
});

describe('handleSignOut', () => {
  it('records the sign-out with the session token, revokes the refresh token, then clears the session', async () => {
    const oidcConfig = oidcTestConfig();
    const calls: string[] = [];
    const fetchApi = vi.fn(async (url: string) => {
      calls.push(`api ${url}`);
      return new Response(null, { status: 204 });
    });
    const deps: OidcDeps = {
      load: async () => {
        const configuration = new client.Configuration(
          {
            issuer: 'https://issuer.example.org',
            token_endpoint: 'https://issuer.example.org/token',
            revocation_endpoint: 'https://issuer.example.org/revoke',
          },
          'azwah-admin',
        );
        configuration[client.customFetch] = async (url) => {
          calls.push(`issuer ${url}`);
          return new Response(null, { status: 200 });
        };
        return configuration;
      },
    };
    const sealed = await sealSession(
      { kind: 'oidc', accessToken: 'access', refreshToken: 'refresh', accessTokenExpiresAt: Date.now() + 60_000, issuedAt: Date.now() },
      oidcConfig.sessionSecret,
      oidcConfig.sessionMaxAgeSeconds,
    );

    const response = await handleSignOut(
      post('/auth/sign-out', {}, 'https://admin.example.org', `__Host-azwah_session=${sealed}`),
      { ok: true, config: oidcConfig },
      deps,
      fetchApi,
    );

    expect(calls).toEqual(['api http://api.test/api/v1/me/sign-outs', 'issuer https://issuer.example.org/revoke']);
    expect(response.status).toBe(303);
    expect(response.headers.getSetCookie().join('\n')).toMatch(/__Host-azwah_session=;.*Max-Age=0/i);
  });

  it('renews an expired access token first, so the sign-out is recorded, and revokes the renewed refresh token', async () => {
    const oidcConfig = oidcTestConfig({ auth: { ...(oidcTestConfig().auth as Extract<ReturnType<typeof oidcTestConfig>['auth'], { mode: 'oidc' }>), issuer: new URL('https://renewing-issuer.example.org') } });
    const revoked: string[] = [];
    const fetchApi = vi.fn(async () => new Response(null, { status: 204 }));
    const deps: OidcDeps = {
      load: async () => {
        const configuration = new client.Configuration(
          {
            issuer: 'https://renewing-issuer.example.org',
            token_endpoint: 'https://renewing-issuer.example.org/token',
            revocation_endpoint: 'https://renewing-issuer.example.org/revoke',
          },
          'azwah-admin',
        );
        configuration[client.customFetch] = async (url, options) => {
          if (url.endsWith('/token')) {
            return Response.json({ access_token: 'renewed-access', token_type: 'Bearer', expires_in: 300, refresh_token: 'renewed-refresh' });
          }
          revoked.push(new URLSearchParams(String(options.body)).get('token') ?? '');
          return new Response(null, { status: 200 });
        };
        return configuration;
      },
    };
    const now = Date.now();
    const sealed = await sealSession(
      { kind: 'oidc', accessToken: 'expired-access', refreshToken: 'old-refresh', accessTokenExpiresAt: now - 60_000, issuedAt: now - 600_000 },
      oidcConfig.sessionSecret,
      oidcConfig.sessionMaxAgeSeconds,
    );

    const response = await handleSignOut(
      post('/auth/sign-out', {}, 'https://admin.example.org', `__Host-azwah_session=${sealed}`),
      { ok: true, config: oidcConfig },
      deps,
      fetchApi,
      now,
    );

    const [, init] = fetchApi.mock.calls[0] as unknown as [string, RequestInit];
    expect(new Headers(init.headers).get('authorization')).toBe('Bearer renewed-access');
    expect(revoked).toEqual(['renewed-refresh']);
    expect(response.status).toBe(303);
  });

  it('still signs out when the platform and the issuer cannot be reached', async () => {
    const fetchApi = vi.fn(async () => {
      throw new TypeError('fetch failed');
    });
    const sealed = await sealSession({ kind: 'development', persona: 'researcher', issuedAt: Date.now() }, config.sessionSecret, 3600);

    const response = await handleSignOut(
      post('/auth/sign-out', {}, 'http://localhost:3000', `azwah_session=${sealed}`),
      ok,
      undefined,
      fetchApi,
    );

    expect(fetchApi).toHaveBeenCalledTimes(1);
    expect(response.status).toBe(303);
    expect(response.headers.getSetCookie().join('\n')).toMatch(/azwah_session=;.*Max-Age=0/i);
  });

  it('clears the session and returns to the sign-in page', async () => {
    const response = await handleSignOut(post('/auth/sign-out', {}, 'http://localhost:3000', 'azwah_session=abc'), ok);

    expect(response.status).toBe(303);
    expect(response.headers.get('location')).toBe('http://localhost:3000/sign-in?signedOut=1');
    expect(response.headers.getSetCookie().join('\n')).toMatch(/azwah_session=;.*Max-Age=0/i);
  });

  it('refuses a sign-out posted from another origin', async () => {
    expect((await handleSignOut(post('/auth/sign-out', {}, 'https://evil.example'), ok)).status).toBe(403);
  });

  it('explains a sign-out refused from another address on the sign-in page, and leaves the session alone', async () => {
    const fetchApi = vi.fn(async () => new Response(null, { status: 204 }));
    const sealed = await sealSession({ kind: 'development', persona: 'researcher', issuedAt: Date.now() }, config.sessionSecret, 3600);

    const response = await handleSignOut(browserPost('http://localhost:3001/auth/sign-out', {}, `azwah_session=${sealed}`), ok, undefined, fetchApi);

    expect(response.status).toBe(303);
    expect(response.headers.get('location')).toBe('/sign-in?error=cross_origin');
    expect(response.headers.getSetCookie()).toEqual([]);
    expect(fetchApi).not.toHaveBeenCalled();
  });
});

describe('handleSignIn', () => {
  it('sends development sign-in to the persona picker, keeping the return path', async () => {
    const response = await handleSignIn(new NextRequest('http://localhost:3000/auth/sign-in?returnTo=/analytics'), ok);

    expect(response.status).toBe(303);
    expect(response.headers.get('location')).toBe('http://localhost:3000/sign-in?returnTo=%2Fanalytics');
  });

  it('shows the not-configured page when the configuration has problems', async () => {
    const response = await handleSignIn(new NextRequest('http://localhost:3000/auth/sign-in'), {
      ok: false,
      problems: ['oidc_issuer_missing'],
    });

    expect(response.headers.get('location')).toBe('http://localhost:3000/sign-in');
  });
});

/** The issuer's token endpoint answers with tokens for the nonce of the sign-in under way. */
function issuerDeps(nonce: () => string): OidcDeps {
  const encode = (value: object) => Buffer.from(JSON.stringify(value)).toString('base64url');
  return {
    load: async () => {
      const configuration = new client.Configuration(
        {
          issuer: 'https://issuer.example.org',
          authorization_endpoint: 'https://issuer.example.org/authorize',
          token_endpoint: 'https://issuer.example.org/token',
        },
        'azwah-admin',
      );
      configuration[client.customFetch] = async () => {
        const issuedAt = Math.floor(Date.now() / 1000);
        const idToken = [
          encode({ alg: 'RS256', typ: 'JWT' }),
          encode({ iss: 'https://issuer.example.org', aud: 'azwah-admin', sub: 'staff-1', iat: issuedAt, exp: issuedAt + 300, nonce: nonce() }),
          'c2lnbmF0dXJl',
        ].join('.');
        return Response.json({ access_token: 'new-access', token_type: 'Bearer', expires_in: 300, refresh_token: 'new-refresh', id_token: idToken });
      };
      return configuration;
    },
  };
}

describe('handleCallback', () => {
  it.each([
    ['answers', async () => new Response(null, { status: 204 })],
    ['cannot be reached', async () => Promise.reject(new TypeError('fetch failed'))],
  ])('records the sign-in with the new access token, and signs in when the platform %s', async (_, answer) => {
    const oidcConfig = oidcTestConfig();
    let nonce = '';
    const deps = issuerDeps(() => nonce);
    const fetchApi = vi.fn(answer);
    const { url, transaction } = await startSignIn(oidcConfig, '/exports', deps);
    nonce = url.searchParams.get('nonce') ?? '';
    const request = new NextRequest(
      `https://admin.example.org/auth/callback?code=the-code&state=${url.searchParams.get('state') ?? ''}`,
      { headers: { cookie: `__Host-azwah_auth_tx=${transaction}` } },
    );

    const response = await handleCallback(request, { ok: true, config: oidcConfig }, deps, fetchApi);

    expect(response.headers.get('location')).toBe('https://admin.example.org/exports');
    expect(response.headers.getSetCookie().join('\n')).toMatch(/__Host-azwah_session=/);
    expect(fetchApi).toHaveBeenCalledTimes(1);
    const [target, init] = fetchApi.mock.calls[0] as unknown as [string, RequestInit];
    expect(target).toBe('http://api.test/api/v1/me/sign-ins');
    expect(new Headers(init.headers).get('authorization')).toBe('Bearer new-access');
  });

  it('returns to the sign-in page with a code when the transaction is missing, and clears it', async () => {
    const oidc = { ok: true as const, config: oidcTestConfig() };

    const response = await handleCallback(new NextRequest('https://admin.example.org/auth/callback?code=c&state=s'), oidc);

    expect(response.status).toBe(303);
    expect(response.headers.get('location')).toBe('https://admin.example.org/sign-in?error=transaction_missing');
    expect(response.headers.getSetCookie().join('\n')).toMatch(/__Host-azwah_auth_tx=;.*Max-Age=0/i);
  });
});
