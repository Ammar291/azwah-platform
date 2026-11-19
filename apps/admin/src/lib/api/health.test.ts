import { describe, expect, it } from 'vitest';

import { fetchApiStatus, type FetchLike } from './health';

const respond = (status: number, body: unknown) => ({ status, json: async () => body }) as unknown as Response;

function routes(handlers: Record<string, () => Promise<Response>>): FetchLike {
  return async (input) => {
    const handler = handlers[new URL(input).pathname];
    if (!handler) {
      throw new TypeError('Failed to fetch');
    }
    return handler();
  };
}

describe('fetchApiStatus', () => {
  it('is not_configured without a usable base URL', async () => {
    await expect(fetchApiStatus(undefined)).resolves.toBe('not_configured');
    await expect(fetchApiStatus('localhost:8080')).resolves.toBe('not_configured');
  });

  it('is ready when live and ready are healthy', async () => {
    const fetchImpl = routes({
      '/health/live': async () => respond(200, { status: 'Healthy' }),
      '/health/ready': async () => respond(200, { status: 'Healthy' }),
    });
    await expect(fetchApiStatus('http://api', fetchImpl)).resolves.toBe('ready');
  });

  it('is not_ready when readiness answers 503', async () => {
    const fetchImpl = routes({
      '/health/live': async () => respond(200, { status: 'Healthy' }),
      '/health/ready': async () => respond(503, { status: 'Unhealthy' }),
    });
    await expect(fetchApiStatus('http://api', fetchImpl)).resolves.toBe('not_ready');
  });

  it('is unreachable on a network failure or a non-API answer', async () => {
    await expect(fetchApiStatus('http://api', routes({}))).resolves.toBe('unreachable');
    const html = routes({
      '/health/live': async () =>
        ({ status: 200, json: async () => { throw new SyntaxError('<html>'); } }) as unknown as Response,
    });
    await expect(fetchApiStatus('http://api', html)).resolves.toBe('unreachable');
  });

  it('is unreachable when the API does not answer in time', async () => {
    const hanging: FetchLike = (_input, init) =>
      new Promise((_resolve, reject) => {
        init?.signal?.addEventListener('abort', () => reject(new Error('aborted')));
      });
    await expect(fetchApiStatus('http://api', hanging, 20)).resolves.toBe('unreachable');
  });
});
