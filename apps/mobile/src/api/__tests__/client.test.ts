import { z } from 'zod';

import { ApiError, getJson, joinUrl, type FetchLike } from '../client';

const schema = z.object({ status: z.string() });
const respond = (status: number, body: unknown) =>
  ({ status, json: async () => body }) as unknown as Response;

describe('joinUrl', () => {
  it('joins without doubling or dropping slashes', () => {
    expect(joinUrl('http://10.0.2.2:8080/', '/health/live')).toBe('http://10.0.2.2:8080/health/live');
    expect(joinUrl('http://10.0.2.2:8080', 'health/live')).toBe('http://10.0.2.2:8080/health/live');
  });
});

describe('getJson', () => {
  it('returns the parsed body', async () => {
    const fetchImpl: FetchLike = async () => respond(200, { status: 'Healthy' });

    await expect(getJson({ baseUrl: 'http://api', path: '/x', schema, fetchImpl })).resolves.toEqual({
      status: 200,
      data: { status: 'Healthy' },
    });
  });

  it('reports a network failure', async () => {
    const fetchImpl: FetchLike = async () => {
      throw new TypeError('Network request failed');
    };

    await expect(getJson({ baseUrl: 'http://api', path: '/x', schema, fetchImpl })).rejects.toMatchObject({
      kind: 'network',
    });
  });

  it('gives up on a request that hangs', async () => {
    const hanging: FetchLike = (_input, init) =>
      new Promise((_resolve, reject) => {
        init?.signal?.addEventListener('abort', () => reject(new Error('aborted')));
      });

    await expect(
      getJson({ baseUrl: 'http://api', path: '/x', schema, fetchImpl: hanging, timeoutMs: 20 }),
    ).rejects.toMatchObject({ kind: 'timeout' });
  });

  it('treats a 5xx as a server error', async () => {
    const fetchImpl: FetchLike = async () => respond(500, { title: 'oops' });

    await expect(getJson({ baseUrl: 'http://api', path: '/x', schema, fetchImpl })).rejects.toMatchObject({
      kind: 'server',
      status: 500,
    });
  });

  it('rejects a body that is not JSON, such as a captive-portal page', async () => {
    const fetchImpl: FetchLike = async () =>
      ({ status: 200, json: async () => { throw new SyntaxError('Unexpected token <'); } }) as unknown as Response;

    await expect(getJson({ baseUrl: 'http://api', path: '/x', schema, fetchImpl })).rejects.toBeInstanceOf(ApiError);
  });

  it('rejects JSON of the wrong shape', async () => {
    const fetchImpl: FetchLike = async () => respond(200, { ok: true });

    await expect(getJson({ baseUrl: 'http://api', path: '/x', schema, fetchImpl })).rejects.toMatchObject({
      kind: 'invalid_response',
    });
  });
});
