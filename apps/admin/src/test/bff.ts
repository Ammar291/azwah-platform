import { vi } from 'vitest';

type Handler = (url: URL, init: RequestInit) => Response | unknown | Promise<Response | unknown>;

/**
 * Stubs `fetch` for the BFF: each key is a path after `/bff/` (no query string), each value a JSON body, a Response,
 * or a function of the request. Unknown paths answer 404. `calls` lists every request URL in order.
 */
export function stubBff(routes: Record<string, Handler | unknown>) {
  const calls: { url: URL; init: RequestInit }[] = [];
  const fetchMock = vi.fn(async (input: string, init: RequestInit = {}) => {
    const url = new URL(input, 'http://localhost:3000');
    calls.push({ url, init });
    const path = url.pathname.replace(/^\/bff\//, '');
    if (!(path in routes)) {
      return new Response(JSON.stringify({ status: 404, code: 'bff.not_found' }), {
        status: 404,
        headers: { 'content-type': 'application/problem+json' },
      });
    }
    const route = routes[path];
    const result = typeof route === 'function' ? await (route as Handler)(url, init) : route;
    return result instanceof Response ? result : Response.json(result);
  });
  vi.stubGlobal('fetch', fetchMock);
  return {
    calls,
    fetchMock,
    /** The query of the last request to a path. */
    lastQuery(path: string): URLSearchParams | null {
      const match = [...calls].reverse().find((call) => call.url.pathname === `/bff/${path}`);
      return match ? match.url.searchParams : null;
    },
  };
}

export function problem(status: number, code?: string, extra: Record<string, unknown> = {}) {
  return new Response(JSON.stringify({ status, ...(code ? { code } : {}), traceId: '00-test-trace-01', ...extra }), {
    status,
    headers: { 'content-type': 'application/problem+json' },
  });
}
