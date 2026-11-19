// @vitest-environment node
import { describe, expect, it, vi } from 'vitest';

import { oidcTestConfig, testConfig } from '@/test/config';

import type { OidcSession } from './session';
import { recordSessionEvent } from './session-events';

const oidcSession: OidcSession = {
  kind: 'oidc',
  accessToken: 'access-token',
  refreshToken: 'refresh-token',
  accessTokenExpiresAt: Date.UTC(2026, 9, 16, 10),
  issuedAt: Date.UTC(2026, 9, 16, 9),
};

describe('recordSessionEvent', () => {
  it('posts the admin client to the platform with the session token', async () => {
    const fetchApi = vi.fn(async () => new Response(null, { status: 204 }));

    await recordSessionEvent(oidcTestConfig(), oidcSession, 'sign-ins', fetchApi);

    expect(fetchApi).toHaveBeenCalledTimes(1);
    const [url, init] = fetchApi.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe('http://api.test/api/v1/me/sign-ins');
    expect(init.method).toBe('POST');
    expect(init.body).toBe('{"client":"admin"}');
    expect(new Headers(init.headers).get('authorization')).toBe('Bearer access-token');
  });

  it('uses the persona token for development sign-in against the API', async () => {
    const fetchApi = vi.fn(async () => new Response(null, { status: 204 }));

    await recordSessionEvent(testConfig(), { kind: 'development', persona: 'researcher', issuedAt: 0 }, 'sign-outs', fetchApi);

    const [url, init] = fetchApi.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe('http://api.test/api/v1/me/sign-outs');
    expect(new Headers(init.headers).get('authorization')).toBe('Bearer dev-token-researcher');
  });

  it('sends nothing with mock data or without a token', async () => {
    const fetchApi = vi.fn(async () => new Response(null, { status: 204 }));

    await recordSessionEvent(
      testConfig({ data: { source: 'mock', scenario: 'no_approved_scores' } }),
      { kind: 'development', persona: 'researcher', issuedAt: 0 },
      'sign-ins',
      fetchApi,
    );
    await recordSessionEvent(testConfig(), { kind: 'development', persona: 'support_coordinator', issuedAt: 0 }, 'sign-ins', fetchApi);

    expect(fetchApi).not.toHaveBeenCalled();
  });

  it('swallows a failure from the platform', async () => {
    const fetchApi = vi.fn(async () => {
      throw new TypeError('fetch failed');
    });

    await expect(recordSessionEvent(oidcTestConfig(), oidcSession, 'sign-ins', fetchApi)).resolves.toBeUndefined();
  });
});
