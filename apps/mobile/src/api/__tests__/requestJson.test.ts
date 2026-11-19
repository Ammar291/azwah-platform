import { z } from 'zod';

import { ApiError, isRetryable, kindForStatus, requestJson, type FetchLike } from '../client';

const schema = z.object({ ok: z.boolean() });
const respond = (status: number, body: unknown) => ({ status, json: async () => body }) as unknown as Response;

describe('requestJson', () => {
  it('sends JSON with the bearer token and validates the answer', async () => {
    const fetchImpl = jest.fn<ReturnType<FetchLike>, Parameters<FetchLike>>(async () => respond(200, { ok: true }));

    await expect(
      requestJson({
        baseUrl: 'http://api/',
        method: 'PUT',
        path: '/api/v1/x',
        schema,
        body: { a: 1 },
        accessToken: 'token-1',
        headers: { 'Idempotency-Key': 'k' },
        fetchImpl,
      }),
    ).resolves.toEqual({ ok: true });

    const [url, init] = fetchImpl.mock.calls[0] ?? [];
    expect(url).toBe('http://api/api/v1/x');
    expect(init?.method).toBe('PUT');
    expect(init?.body).toBe('{"a":1}');
    expect(init?.headers).toEqual({
      Accept: 'application/json',
      'Content-Type': 'application/json',
      Authorization: 'Bearer token-1',
      'Idempotency-Key': 'k',
    });
  });

  it('sends no Authorization header without a token', async () => {
    const fetchImpl = jest.fn<ReturnType<FetchLike>, Parameters<FetchLike>>(async () => respond(200, { ok: true }));

    await requestJson({ baseUrl: 'http://api', method: 'GET', path: '/x', schema, accessToken: null, fetchImpl });

    expect(fetchImpl.mock.calls[0]?.[1]?.headers).toEqual({ Accept: 'application/json' });
  });

  it('keeps only the problem code from an error, never its text', async () => {
    const fetchImpl: FetchLike = async () =>
      respond(409, {
        title: 'Conflict',
        detail: 'Internal detail that must not be shown',
        status: 409,
        code: 'sessions.required_items_unanswered',
      });

    const error = await requestJson({ baseUrl: 'http://api', method: 'POST', path: '/x', schema, fetchImpl }).catch(
      (caught: unknown) => caught,
    );

    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ kind: 'conflict', status: 409, code: 'sessions.required_items_unanswered' });
    expect(String((error as Error).message)).not.toMatch(/Internal detail/);
  });

  it('ignores a malformed problem code', async () => {
    const fetchImpl: FetchLike = async () => respond(404, { code: 'DROP TABLE; <script>' });

    await expect(
      requestJson({ baseUrl: 'http://api', method: 'GET', path: '/x', schema, fetchImpl }),
    ).rejects.toMatchObject({ kind: 'not_found', code: undefined });
  });

  it('reports a 500 without a code', async () => {
    const fetchImpl: FetchLike = async () => respond(500, { code: 'scoring.policy_not_approved' });

    await expect(
      requestJson({ baseUrl: 'http://api', method: 'GET', path: '/x', schema, fetchImpl }),
    ).rejects.toMatchObject({ kind: 'server', status: 500, code: undefined });
  });

  it('rejects a success body of the wrong shape', async () => {
    const fetchImpl: FetchLike = async () => respond(200, { ok: 'yes' });

    await expect(
      requestJson({ baseUrl: 'http://api', method: 'GET', path: '/x', schema, fetchImpl }),
    ).rejects.toMatchObject({ kind: 'invalid_response' });
  });

  it('reports a rate limit with its code, as a retryable error', async () => {
    const fetchImpl: FetchLike = async () => respond(429, { status: 429, code: 'requests.rate_limited' });

    const error = await requestJson({ baseUrl: 'http://api', method: 'POST', path: '/x', schema, fetchImpl }).catch(
      (caught: unknown) => caught,
    );

    expect(error).toMatchObject({ kind: 'rate_limited', status: 429, code: 'requests.rate_limited' });
    expect(isRetryable(error)).toBe(true);
  });
});

describe('kindForStatus', () => {
  it.each([
    [400, 'bad_request'],
    [401, 'unauthorized'],
    [403, 'forbidden'],
    [404, 'not_found'],
    [409, 'conflict'],
    [413, 'payload_too_large'],
    [418, 'invalid_response'],
    [429, 'rate_limited'],
    [503, 'server'],
  ] as const)('maps %i to %s', (status, kind) => {
    expect(kindForStatus(status)).toBe(kind);
  });
});

describe('isRetryable', () => {
  it('retries connection trouble, rate limits and server faults only', () => {
    expect(isRetryable(new ApiError('network'))).toBe(true);
    expect(isRetryable(new ApiError('timeout'))).toBe(true);
    expect(isRetryable(new ApiError('rate_limited', 429))).toBe(true);
    expect(isRetryable(new ApiError('server', 503))).toBe(true);
    expect(isRetryable(new ApiError('conflict', 409))).toBe(false);
    expect(isRetryable(new Error('bug'))).toBe(false);
  });
});
