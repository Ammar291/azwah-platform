import type { z } from 'zod';

import { ApiError, toApiError } from './errors';
import { toSearch, type Query } from './search';

/**
 * Calls the dashboard's own BFF (`/bff/...`), never the API directly: the session cookie travels with the request and
 * the server adds the token. Every success body is checked against its schema before anything uses it.
 */
async function send<T>(path: string, schema: z.ZodType<T>, init: RequestInit): Promise<T> {
  let response: Response;
  try {
    response = await fetch(`/bff/${path}`, { credentials: 'same-origin', cache: 'no-store', ...init });
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') throw error;
    throw new ApiError(0, 'network', null);
  }
  if (!response.ok) throw await toApiError(response);
  let body: unknown;
  try {
    body = await response.json();
  } catch {
    throw new ApiError(response.status, 'unexpected_response', null);
  }
  const parsed = schema.safeParse(body);
  if (!parsed.success) throw new ApiError(response.status, 'unexpected_response', null);
  return parsed.data;
}

export function bffGet<T>(path: string, schema: z.ZodType<T>, query: Query = {}, signal?: AbortSignal): Promise<T> {
  return send(`${path}${toSearch(query)}`, schema, { method: 'GET', headers: { Accept: 'application/json' }, signal });
}

export function bffPost<T>(path: string, body: unknown, schema: z.ZodType<T>, headers: Record<string, string> = {}): Promise<T> {
  return send(path, schema, {
    method: 'POST',
    headers: { Accept: 'application/json', 'Content-Type': 'application/json', ...headers },
    body: JSON.stringify(body),
  });
}
