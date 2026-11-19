// @vitest-environment node
import { NextRequest } from 'next/server';
import { describe, expect, it, vi } from 'vitest';

import { oidcTestConfig, testConfig } from '@/test/config';

import { createRefresher } from './oidc';
import { decide } from './proxy-decision';
import { sealSession, unsealSession, type OidcSession } from './session';

const now = Date.UTC(2026, 9, 16, 9, 0, 0);
const dev = testConfig();
const oidc = oidcTestConfig();

function request(url: string, cookie?: string) {
  return new NextRequest(url, { headers: cookie ? { cookie } : {} });
}

function setCookieValue(response: Response, name: string): string | undefined {
  return response.headers
    .getSetCookie()
    .find((line) => line.startsWith(`${name}=`))
    ?.slice(name.length + 1)
    .split(';')[0];
}

const fresh: OidcSession = { kind: 'oidc', accessToken: 'a1', refreshToken: 'r1', accessTokenExpiresAt: now + 600_000, issuedAt: now };
const expiring: OidcSession = { ...fresh, accessTokenExpiresAt: now + 30_000 };

describe('decide', () => {
  it('sends a page request without a session to sign-in, keeping the path and query', async () => {
    const response = await decide(request('http://localhost:3000/monitoring/assessments?page=2'), { result: { ok: true, config: dev }, now });

    expect(response.status).toBe(307);
    expect(response.headers.get('location')).toBe('http://localhost:3000/sign-in?returnTo=%2Fmonitoring%2Fassessments%3Fpage%3D2');
  });

  it('answers a BFF request without a session with a 401 problem', async () => {
    const response = await decide(request('http://localhost:3000/bff/me/permissions'), { result: { ok: true, config: dev }, now });

    expect(response.status).toBe(401);
    expect(response.headers.get('content-type')).toBe('application/problem+json');
  });

  it('clears an invalid session cookie on the way to sign-in', async () => {
    const response = await decide(request('http://localhost:3000/overview', 'azwah_session=forged'), { result: { ok: true, config: dev }, now });

    expect(response.status).toBe(307);
    expect(response.headers.getSetCookie().join('\n')).toMatch(/azwah_session=;.*Max-Age=0/i);
  });

  it('lets a valid session through untouched', async () => {
    const sealed = await sealSession({ kind: 'development', persona: 'researcher', issuedAt: now }, dev.sessionSecret, 3600);

    const response = await decide(request('http://localhost:3000/overview', `azwah_session=${sealed}`), { result: { ok: true, config: dev }, now });

    expect(response.headers.get('x-middleware-next')).toBe('1');
    expect(response.headers.getSetCookie()).toEqual([]);
  });

  it('refreshes an access token about to expire, once for concurrent requests, and passes it on', async () => {
    const sealed = await sealSession(expiring, oidc.sessionSecret, 3600);
    const grant = vi.fn(async () => ({ accessToken: 'a2', refreshToken: 'r2', expiresIn: 300 }));
    const refresh = createRefresher(grant);
    const cookie = `__Host-azwah_session=${sealed}`;

    const [first, second] = await Promise.all([
      decide(request('https://admin.example.org/overview', cookie), { result: { ok: true, config: oidc }, refresh, now }),
      decide(request('https://admin.example.org/bff/me/permissions', cookie), { result: { ok: true, config: oidc }, refresh, now }),
    ]);

    expect(grant).toHaveBeenCalledTimes(1);
    for (const response of [first, second]) {
      const session = await unsealSession(setCookieValue(response, '__Host-azwah_session'), oidc.sessionSecret, now);
      expect(session).toMatchObject({ accessToken: 'a2', refreshToken: 'r2' });
      expect(response.headers.get('x-middleware-override-headers')).toContain('cookie');
      expect(response.headers.get('x-middleware-request-cookie')).toContain('__Host-azwah_session=');
    }
  });

  it('does not refresh a token with time left', async () => {
    const sealed = await sealSession(fresh, oidc.sessionSecret, 3600);
    const grant = vi.fn();

    await decide(request('https://admin.example.org/overview', `__Host-azwah_session=${sealed}`), {
      result: { ok: true, config: oidc },
      refresh: createRefresher(grant),
      now,
    });

    expect(grant).not.toHaveBeenCalled();
  });

  it('signs out when the refresh is refused', async () => {
    const sealed = await sealSession(expiring, oidc.sessionSecret, 3600);
    const refresh = createRefresher(async () => {
      throw new Error('invalid_grant');
    });

    const response = await decide(request('https://admin.example.org/overview', `__Host-azwah_session=${sealed}`), {
      result: { ok: true, config: oidc },
      refresh,
      now,
    });

    expect(response.status).toBe(307);
    expect(response.headers.get('location')).toContain('/sign-in?returnTo=%2Foverview');
    expect(response.headers.getSetCookie().join('\n')).toMatch(/__Host-azwah_session=;.*Max-Age=0/i);
  });

  it('shows the sign-in page when the deployment is not configured', async () => {
    const pages = await decide(request('http://localhost:3000/overview'), { result: { ok: false, problems: ['api_url_missing'] }, now });
    const bff = await decide(request('http://localhost:3000/bff/me/permissions'), { result: { ok: false, problems: ['api_url_missing'] }, now });

    expect(pages.headers.get('location')).toBe('http://localhost:3000/sign-in');
    expect(bff.status).toBe(503);
  });
});
