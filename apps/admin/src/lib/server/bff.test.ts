// @vitest-environment node
import { describe, expect, it, vi } from 'vitest';

import { createMockBackend } from '@/lib/mock/backend';
import { testConfig } from '@/test/config';

import { handleBff, type BffDeps } from './bff';
import { matchBffRoute } from './bff-routes';

const now = Date.UTC(2026, 9, 16, 9, 0, 0);
const researcher = { kind: 'development' as const, persona: 'researcher' as const, issuedAt: now };
const exportId = '0190f0e2-7b1c-7d2e-8f3a-4b5c6d7e8f90';
const requestId = '0190f0e2-5555-7000-8000-000000000001';

function deps(overrides: Partial<BffDeps> = {}): BffDeps & { fetchApi: ReturnType<typeof vi.fn> } {
  const fetchApi = vi.fn(async () => Response.json({ ok: true }));
  return { config: testConfig(), session: researcher, fetchApi, mock: null, ...overrides } as BffDeps & {
    fetchApi: ReturnType<typeof vi.fn>;
  };
}

function get(path: string) {
  return new Request(`http://localhost:3000/bff/${path}`);
}

function post(path: string, body: string, headers: Record<string, string> = {}) {
  return new Request(`http://localhost:3000/bff/${path}`, {
    method: 'POST',
    body,
    headers: { origin: 'http://localhost:3000', 'content-type': 'application/json', ...headers },
  });
}

const segments = (path: string) => path.split('?')[0].split('/');

describe('matchBffRoute', () => {
  it('maps an allowed route to its API path', () => {
    expect(matchBffRoute('GET', ['admin', 'analytics', 'overview'])?.apiPath).toBe('/api/v1/admin/analytics/overview');
    expect(matchBffRoute('GET', ['admin', 'exports', exportId, 'file'])?.route.stream).toBe(true);
  });

  it('refuses unknown paths, methods and identifiers that are not UUIDs', () => {
    expect(matchBffRoute('GET', ['admin', 'households'])).toBeNull();
    expect(matchBffRoute('DELETE', ['admin', 'exports', exportId])).toBeNull();
    expect(matchBffRoute('POST', ['instruments'])).toBeNull();
    expect(matchBffRoute('GET', ['admin', 'exports', '..', 'file'])).toBeNull();
    expect(matchBffRoute('GET', ['rule-sets', 'not-a-uuid'])).toBeNull();
  });

  it('allows reading privacy requests and closing one, and nothing else on them', () => {
    expect(matchBffRoute('GET', ['admin', 'privacy-requests'])).toMatchObject({
      apiPath: '/api/v1/admin/privacy-requests',
      route: { query: ['status', 'page', 'pageSize'] },
    });
    expect(matchBffRoute('POST', ['admin', 'privacy-requests', requestId, 'close'])?.apiPath).toBe(
      `/api/v1/admin/privacy-requests/${requestId}/close`,
    );

    expect(matchBffRoute('DELETE', ['admin', 'privacy-requests', requestId])).toBeNull();
    expect(matchBffRoute('GET', ['admin', 'privacy-requests', requestId])).toBeNull();
    expect(matchBffRoute('GET', ['admin', 'privacy-requests', requestId, 'close'])).toBeNull();
    expect(matchBffRoute('POST', ['admin', 'privacy-requests'])).toBeNull();
    expect(matchBffRoute('POST', ['admin', 'privacy-requests', requestId, 'reopen'])).toBeNull();
    expect(matchBffRoute('POST', ['admin', 'privacy-requests', 'not-a-uuid', 'close'])).toBeNull();
    expect(matchBffRoute('GET', ['me', 'privacy-requests'])).toBeNull();
    expect(matchBffRoute('GET', ['me', 'data-export'])).toBeNull();
  });
});

describe('the safety alert routes', () => {
  it('allows listing, the summary, acknowledging and resolving, and nothing else', () => {
    expect(matchBffRoute('GET', ['admin', 'safety-alerts'])).toMatchObject({
      apiPath: '/api/v1/admin/safety-alerts',
      route: { query: ['status', 'category', 'page', 'pageSize'] },
    });
    expect(matchBffRoute('GET', ['admin', 'safety-alerts', 'summary'])).toMatchObject({
      apiPath: '/api/v1/admin/safety-alerts/summary',
      route: { query: [] },
    });
    expect(matchBffRoute('POST', ['admin', 'safety-alerts', requestId, 'acknowledge'])?.apiPath).toBe(
      `/api/v1/admin/safety-alerts/${requestId}/acknowledge`,
    );
    expect(matchBffRoute('POST', ['admin', 'safety-alerts', requestId, 'resolve'])?.apiPath).toBe(
      `/api/v1/admin/safety-alerts/${requestId}/resolve`,
    );

    expect(matchBffRoute('POST', ['admin', 'safety-alerts', 'summary', 'acknowledge'])).toBeNull();
    expect(matchBffRoute('POST', ['admin', 'safety-alerts', 'not-a-uuid', 'resolve'])).toBeNull();
    expect(matchBffRoute('GET', ['admin', 'safety-alerts', requestId])).toBeNull();
    expect(matchBffRoute('DELETE', ['admin', 'safety-alerts', requestId])).toBeNull();
    expect(matchBffRoute('POST', ['admin', 'safety-alerts'])).toBeNull();
    expect(matchBffRoute('POST', ['admin', 'safety-alerts', 'summary'])).toBeNull();
    expect(matchBffRoute('GET', ['admin', 'safety-alerts', requestId, 'resolve'])).toBeNull();
    expect(matchBffRoute('POST', ['admin', 'safety-alerts', requestId, 'reopen'])).toBeNull();
  });
});

describe('the household results route', () => {
  it('allows reading results with the wave, questionnaire, reference and paging keys only', () => {
    expect(matchBffRoute('GET', ['admin', 'results'])).toEqual({
      apiPath: '/api/v1/admin/results',
      route: { method: 'GET', pattern: ['admin', 'results'], query: ['waveId', 'instrumentCode', 'reference', 'page', 'pageSize'] },
    });
    expect(matchBffRoute('POST', ['admin', 'results'])).toBeNull();
    expect(matchBffRoute('GET', ['admin', 'results', requestId])).toBeNull();
  });

  it('forwards only the allowed query keys', async () => {
    const d = deps();
    const path = 'admin/results?waveId=w&instrumentCode=household_assessment&reference=HH-7K2M-9QXA&page=2&pageSize=20&householdId=x&tierCode=y';

    await handleBff(get(path), segments(path), d);

    const [url] = d.fetchApi.mock.calls[0] as [string, RequestInit];
    expect(url).toBe(
      'http://api.test/api/v1/admin/results?waveId=w&instrumentCode=household_assessment&reference=HH-7K2M-9QXA&page=2&pageSize=20',
    );
  });
});

describe('handleBff', () => {
  it('answers 404 for a path that is not on the allowlist, without calling the API', async () => {
    const d = deps();

    const response = await handleBff(get('admin/households'), ['admin', 'households'], d);

    expect(response.status).toBe(404);
    expect(await response.json()).toMatchObject({ code: 'bff.not_found' });
    expect(d.fetchApi).not.toHaveBeenCalled();
  });

  it('answers 401 without a session', async () => {
    const response = await handleBff(get('me/permissions'), ['me', 'permissions'], deps({ session: null }));

    expect(response.status).toBe(401);
    expect(response.headers.get('content-type')).toBe('application/problem+json');
  });

  it('forwards only the allowed query keys, with the bearer token', async () => {
    const d = deps();
    const path = 'admin/analytics/overview?waveId=w&from=2026-01-01&secret=x&interval=week';

    await handleBff(get(path), segments(path), d);

    const [url, init] = d.fetchApi.mock.calls[0] as [string, RequestInit];
    expect(url).toBe('http://api.test/api/v1/admin/analytics/overview?waveId=w&from=2026-01-01&interval=week');
    expect(new Headers(init.headers).get('authorization')).toBe('Bearer dev-token-researcher');
  });

  it('forwards only the status and paging of a privacy request list', async () => {
    const d = deps();
    const path = 'admin/privacy-requests?status=closed&page=2&pageSize=20&householdId=x&reference=HH-7K2M-9QXA';

    await handleBff(get(path), segments(path), d);

    const [url] = d.fetchApi.mock.calls[0] as [string, RequestInit];
    expect(url).toBe('http://api.test/api/v1/admin/privacy-requests?status=closed&page=2&pageSize=20');
  });

  it('forwards the JSON body of a privacy request close, from this origin only', async () => {
    const d = deps();
    const path = `admin/privacy-requests/${requestId}/close`;

    await handleBff(post(`${path}?resolution=completed`, '{"resolution":"declined"}'), segments(path), d);
    const crossOrigin = await handleBff(post(path, '{"resolution":"declined"}', { origin: 'https://evil.example' }), segments(path), d);
    const notJson = await handleBff(post(path, 'resolution=declined', { 'content-type': 'application/x-www-form-urlencoded' }), segments(path), d);

    expect(d.fetchApi).toHaveBeenCalledTimes(1);
    const [url, init] = d.fetchApi.mock.calls[0] as [string, RequestInit];
    expect(url).toBe(`http://api.test/api/v1/${path}`);
    expect(init.method).toBe('POST');
    expect(init.body).toBe('{"resolution":"declined"}');
    expect(crossOrigin.status).toBe(403);
    expect(notJson.status).toBe(415);
  });

  it('answers 404 for a method on privacy requests that is not allowed, without calling the API', async () => {
    const d = deps();
    const request = new Request(`http://localhost:3000/bff/admin/privacy-requests/${requestId}`, {
      method: 'DELETE',
      headers: { origin: 'http://localhost:3000' },
    });

    const response = await handleBff(request, ['admin', 'privacy-requests', requestId], d);

    expect(response.status).toBe(404);
    expect(d.fetchApi).not.toHaveBeenCalled();
  });

  it('passes the API status and body through, keeping only safe headers and never caching', async () => {
    const d = deps({
      fetchApi: vi.fn(async () =>
        new Response(JSON.stringify({ status: 403, code: 'x' }), {
          status: 403,
          headers: { 'content-type': 'application/problem+json', 'set-cookie': 'a=b', server: 'kestrel', 'x-internal': '1' },
        }),
      ),
    });

    const response = await handleBff(get('me/permissions'), ['me', 'permissions'], d);

    expect(response.status).toBe(403);
    expect(response.headers.get('content-type')).toBe('application/problem+json');
    expect(response.headers.get('cache-control')).toBe('no-store');
    expect(response.headers.get('set-cookie')).toBeNull();
    expect(response.headers.get('server')).toBeNull();
    expect(response.headers.get('x-internal')).toBeNull();
  });

  it('keeps the content disposition of a download', async () => {
    const d = deps({
      fetchApi: vi.fn(async () =>
        new Response('a,b\r\n', { headers: { 'content-type': 'text/csv', 'content-disposition': 'attachment; filename="x.csv"' } }),
      ),
    });

    const response = await handleBff(get(`admin/exports/${exportId}/file`), ['admin', 'exports', exportId, 'file'], d);

    expect(response.headers.get('content-disposition')).toBe('attachment; filename="x.csv"');
    expect(await response.text()).toBe('a,b\r\n');
  });

  it('refuses a POST from another origin', async () => {
    const d = deps();

    const response = await handleBff(post('admin/exports', '{}', { origin: 'https://evil.example' }), ['admin', 'exports'], d);

    expect(response.status).toBe(403);
    expect(d.fetchApi).not.toHaveBeenCalled();
  });

  it('refuses a POST that is not JSON or is too large', async () => {
    const d = deps();

    const form = await handleBff(post('admin/exports', 'a=b', { 'content-type': 'application/x-www-form-urlencoded' }), ['admin', 'exports'], d);
    const large = await handleBff(post('admin/exports', JSON.stringify({ pad: 'x'.repeat(17 * 1024) })), ['admin', 'exports'], d);

    expect(form.status).toBe(415);
    expect(large.status).toBe(413);
    expect(d.fetchApi).not.toHaveBeenCalled();
  });

  it('forwards the idempotency key and the JSON body, and no other request header', async () => {
    const d = deps();

    await handleBff(
      post('admin/exports', '{"dataset":"score_results"}', { 'idempotency-key': 'k-1', cookie: 'azwah_session=x', 'x-forwarded-for': '1.2.3.4' }),
      ['admin', 'exports'],
      d,
    );

    const [, init] = d.fetchApi.mock.calls[0] as [string, RequestInit];
    const headers = new Headers(init.headers);
    expect(init.method).toBe('POST');
    expect(init.body).toBe('{"dataset":"score_results"}');
    expect(headers.get('idempotency-key')).toBe('k-1');
    expect(headers.get('content-type')).toBe('application/json');
    expect(headers.get('cookie')).toBeNull();
    expect(headers.get('x-forwarded-for')).toBeNull();
  });

  it('answers 502 when the API cannot be reached', async () => {
    const response = await handleBff(
      get('me/permissions'),
      ['me', 'permissions'],
      deps({ fetchApi: vi.fn(async () => Promise.reject(new TypeError('fetch failed'))) }),
    );

    expect(response.status).toBe(502);
    expect(await response.json()).toMatchObject({ code: 'bff.api_unreachable' });
  });

  it('answers 503 when the development persona has no token', async () => {
    const response = await handleBff(
      get('me/permissions'),
      ['me', 'permissions'],
      deps({ session: { ...researcher, persona: 'program_administrator' } }),
    );

    expect(response.status).toBe(503);
    expect(await response.json()).toMatchObject({ code: 'bff.dev_token_missing' });
  });

  it('answers from the mock without calling the API', async () => {
    const d = deps({ config: testConfig({ data: { source: 'mock', scenario: 'no_approved_scores' } }), mock: createMockBackend('no_approved_scores') });

    const response = await handleBff(get('admin/exports/catalog'), ['admin', 'exports', 'catalog'], d);

    expect(response.status).toBe(200);
    expect(await response.json()).toMatchObject({ filters: ['wave_id', 'instrument_code'] });
    expect(d.fetchApi).not.toHaveBeenCalled();
  });

  it('reports mock data on the health route', async () => {
    const d = deps({ config: testConfig({ data: { source: 'mock', scenario: 'no_approved_scores' } }), mock: createMockBackend('no_approved_scores') });

    expect(await (await handleBff(get('health'), ['health'], d)).json()).toEqual({ status: 'mock' });
  });

  it('reports the API health through the server', async () => {
    const d = deps({
      fetchApi: vi.fn(async (url: string) => Response.json({ status: url.endsWith('/ready') ? 'Healthy' : 'Healthy' })),
    });

    expect(await (await handleBff(get('health'), ['health'], d)).json()).toEqual({ status: 'ready' });
  });
});
