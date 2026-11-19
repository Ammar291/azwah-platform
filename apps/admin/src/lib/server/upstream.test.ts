// @vitest-environment node
import { describe, expect, it, vi } from 'vitest';

import { oidcTestConfig, testConfig } from '@/test/config';

import { bearerFor, callApi } from './upstream';

const now = Date.UTC(2026, 9, 16, 9, 0, 0);
const oidcSession = { kind: 'oidc' as const, accessToken: 'at', refreshToken: null, accessTokenExpiresAt: now + 60_000, issuedAt: now };

describe('bearerFor', () => {
  it('uses the access token of an OIDC session', () => {
    expect(bearerFor(oidcTestConfig(), oidcSession)).toEqual({ ok: true, token: 'at' });
  });

  it('uses the development token of the persona', () => {
    expect(bearerFor(testConfig(), { kind: 'development', persona: 'researcher', issuedAt: now })).toEqual({
      ok: true,
      token: 'dev-token-researcher',
    });
  });

  it('reports a persona without a development token', () => {
    expect(bearerFor(testConfig(), { kind: 'development', persona: 'program_administrator', issuedAt: now })).toEqual({
      ok: false,
      reason: 'dev_token_missing',
    });
  });

  it('refuses a session from another sign-in mode', () => {
    expect(bearerFor(testConfig(), oidcSession)).toEqual({ ok: false, reason: 'session_mismatch' });
    expect(bearerFor(oidcTestConfig(), { kind: 'development', persona: 'researcher', issuedAt: now })).toEqual({
      ok: false,
      reason: 'session_mismatch',
    });
  });
});

describe('callApi', () => {
  it('sends the bearer token and asks for JSON, without caching', async () => {
    const fetchApi = vi.fn(async () => new Response('{}'));

    await callApi('http://api.test', 'token', '/api/v1/me/permissions', { method: 'GET' }, fetchApi);

    const [url, init] = fetchApi.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe('http://api.test/api/v1/me/permissions');
    const headers = new Headers(init.headers);
    expect(headers.get('authorization')).toBe('Bearer token');
    expect(headers.get('accept')).toContain('application/json');
    expect(init.cache).toBe('no-store');
    expect(init.signal).toBeInstanceOf(AbortSignal);
  });
});
