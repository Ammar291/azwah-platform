import type { FetchLike } from '../client';
import { fetchApiStatus } from '../health';

const respond = (status: number, body: unknown) =>
  ({ status, json: async () => body }) as unknown as Response;

function routes(handlers: Record<string, () => Promise<Response>>): FetchLike {
  return async (input) => {
    const handler = handlers[new URL(input).pathname];
    if (!handler) {
      throw new TypeError('Network request failed');
    }
    return handler();
  };
}

describe('fetchApiStatus', () => {
  it('is not_configured without a usable base URL', async () => {
    await expect(fetchApiStatus(undefined)).resolves.toBe('not_configured');
    await expect(fetchApiStatus('')).resolves.toBe('not_configured');
    await expect(fetchApiStatus('10.0.2.2:8080')).resolves.toBe('not_configured');
  });

  it('is ready when live and ready are both healthy', async () => {
    const fetchImpl = routes({
      '/health/live': async () => respond(200, { status: 'Healthy' }),
      '/health/ready': async () => respond(200, { status: 'Healthy' }),
    });

    await expect(fetchApiStatus('http://api', fetchImpl)).resolves.toBe('ready');
  });

  it('is not_ready when the API answers but the database is down (503)', async () => {
    const fetchImpl = routes({
      '/health/live': async () => respond(200, { status: 'Healthy' }),
      '/health/ready': async () => respond(503, { status: 'Unhealthy' }),
    });

    await expect(fetchApiStatus('http://api', fetchImpl)).resolves.toBe('not_ready');
  });

  it('is unreachable when the API cannot be reached', async () => {
    await expect(fetchApiStatus('http://api', routes({}))).resolves.toBe('unreachable');
  });

  it('is unreachable when something other than the API answers', async () => {
    const fetchImpl = routes({
      '/health/live': async () => ({ status: 200, json: async () => { throw new SyntaxError('<html>'); } }) as unknown as Response,
    });

    await expect(fetchApiStatus('http://api', fetchImpl)).resolves.toBe('unreachable');
  });
});
