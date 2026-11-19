import { afterEach, describe, expect, it, vi } from 'vitest';
import { z } from 'zod';

import { bffGet, bffPost } from './client';
import { ApiError, errorKind } from './errors';
import { exportSchema, overviewSchema } from './schemas';
import { downloadHref, toSearch } from './search';

function stubFetch(response: Response | Error) {
  const fetchMock = vi.fn(async () => {
    if (response instanceof Error) throw response;
    return response;
  });
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

afterEach(() => vi.unstubAllGlobals());

describe('bffGet', () => {
  it('calls the BFF on the same origin with the query and returns the parsed body', async () => {
    const fetchMock = stubFetch(Response.json({ count: 2 }));

    const body = await bffGet('admin/waves', z.object({ count: z.number() }), { page: 2, pageSize: 20, empty: '' });

    expect(body).toEqual({ count: 2 });
    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe('/bff/admin/waves?page=2&pageSize=20');
    expect(init.credentials).toBe('same-origin');
  });

  it('turns a ProblemDetails body into an ApiError with its code and trace ID', async () => {
    stubFetch(
      new Response(JSON.stringify({ status: 409, code: 'analytics.export_storage_not_configured', title: 'Server text', traceId: '00-abc-01' }), {
        status: 409,
        headers: { 'content-type': 'application/problem+json' },
      }),
    );

    const error = await bffGet('admin/exports', z.unknown()).catch((caught: unknown) => caught);

    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ status: 409, code: 'analytics.export_storage_not_configured', traceId: '00-abc-01' });
    expect((error as Error).message).not.toContain('Server text');
  });

  it('keeps the field names of a validation problem', async () => {
    stubFetch(
      new Response(JSON.stringify({ status: 400, errors: { versionNumber: ['x'], pageSize: ['y'] }, traceId: 't' }), {
        status: 400,
        headers: { 'content-type': 'application/problem+json' },
      }),
    );

    const error = (await bffGet('admin/assessments', z.unknown()).catch((caught: unknown) => caught)) as ApiError;

    expect(error.fields).toEqual(['versionNumber', 'pageSize']);
    expect(errorKind(error)).toBe('filters');
  });

  it('reads an error without a body as a status only', async () => {
    stubFetch(new Response(null, { status: 500 }));

    expect(await bffGet('x', z.unknown()).catch((caught: unknown) => caught)).toMatchObject({ status: 500, code: null, traceId: null });
  });

  it('refuses a success body that does not match the contract', async () => {
    stubFetch(Response.json({ generatedAt: 'yesterday' }));

    const error = await bffGet('admin/analytics/overview', overviewSchema).catch((caught: unknown) => caught);

    expect(error).toMatchObject({ status: 200, code: 'unexpected_response' });
    expect(errorKind(error)).toBe('unexpected');
  });

  it('reports a network failure', async () => {
    stubFetch(new TypeError('Failed to fetch'));

    const error = await bffGet('admin/waves', z.unknown()).catch((caught: unknown) => caught);

    expect(error).toMatchObject({ status: 0, code: 'network' });
    expect(errorKind(error)).toBe('network');
  });
});

describe('bffPost', () => {
  it('posts JSON with the given headers', async () => {
    const fetchMock = stubFetch(
      Response.json({
        id: '0190f0e2-7b1c-7d2e-8f3a-4b5c6d7e8f90',
        dataset: 'score_results',
        columns: ['tier_code'],
        filters: { waveId: null, instrumentCode: null },
        status: 'queued',
        rowCount: null,
        errorCode: null,
        requestedAt: '2026-10-16T09:00:00Z',
        finishedAt: null,
        expiresAt: null,
        downloadPath: null,
      }, { status: 202 }),
    );

    const created = await bffPost('admin/exports', { dataset: 'score_results' }, exportSchema, { 'Idempotency-Key': 'k' });

    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe('/bff/admin/exports');
    expect(init.method).toBe('POST');
    expect(init.body).toBe('{"dataset":"score_results"}');
    expect(new Headers(init.headers).get('idempotency-key')).toBe('k');
    expect(new Headers(init.headers).get('content-type')).toBe('application/json');
    expect(created.status).toBe('queued');
  });
});

describe('errorKind', () => {
  it.each([
    [new ApiError(401, 'bff.signed_out', null), 'signed_out'],
    [new ApiError(403, null, 't'), 'denied'],
    [new ApiError(404, 'questionnaires.version_not_found', 't'), 'not_found'],
    [new ApiError(409, 'analytics.export_expired', 't'), 'conflict'],
    [new ApiError(429, 'requests.rate_limited', 't'), 'rate_limited'],
    [new ApiError(502, 'bff.api_unreachable', null), 'unavailable'],
    [new ApiError(503, 'bff.dev_token_missing', null), 'dev_token'],
    [new ApiError(500, null, 't'), 'generic'],
    [new Error('boom'), 'generic'],
  ])('classifies %o as %s', (error, kind) => {
    expect(errorKind(error)).toBe(kind);
  });
});

describe('toSearch', () => {
  it('drops empty values and sorts the keys', () => {
    expect(toSearch({ status: 'completed', waveId: null, page: 1, instrumentCode: undefined, from: '' })).toBe('?page=1&status=completed');
    expect(toSearch({})).toBe('');
  });
});

describe('downloadHref', () => {
  it('turns the API download path into the BFF path', () => {
    expect(downloadHref('/api/v1/admin/exports/0190f0e2-7b1c-7d2e-8f3a-4b5c6d7e8f90/file')).toBe(
      '/bff/admin/exports/0190f0e2-7b1c-7d2e-8f3a-4b5c6d7e8f90/file',
    );
  });

  it('refuses any other path', () => {
    expect(downloadHref(null)).toBeNull();
    expect(downloadHref('https://evil.example/file')).toBeNull();
    expect(downloadHref('/api/v1/admin/exports/../../me/file')).toBeNull();
  });
});
