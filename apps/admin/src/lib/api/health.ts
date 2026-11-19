import { z } from 'zod';

export type ApiStatus = 'ready' | 'not_ready' | 'unreachable' | 'not_configured';
export type FetchLike = (input: string, init?: RequestInit) => Promise<Response>;

const healthResponseSchema = z.object({ status: z.enum(['Healthy', 'Degraded', 'Unhealthy']) });

async function probe(
  baseUrl: string,
  path: string,
  acceptStatuses: readonly number[],
  fetchImpl: FetchLike,
  timeoutMs: number,
): Promise<'Healthy' | 'Degraded' | 'Unhealthy' | null> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  try {
    const response = await fetchImpl(`${baseUrl.replace(/\/+$/, '')}${path}`, {
      headers: { Accept: 'application/json' },
      signal: controller.signal,
      cache: 'no-store',
    });
    if (!acceptStatuses.includes(response.status)) {
      return null;
    }
    const parsed = healthResponseSchema.safeParse(await response.json());
    return parsed.success ? parsed.data.status : null;
  } catch {
    return null;
  } finally {
    clearTimeout(timer);
  }
}

/**
 * Same rules as the mobile app (apps/mobile/src/api/health.ts); duplicated on purpose because the apps share no
 * packages. A 503 from readiness means "not ready", not "unreachable".
 */
export async function fetchApiStatus(
  baseUrl: string | undefined,
  fetchImpl: FetchLike = fetch,
  timeoutMs = 5000,
): Promise<ApiStatus> {
  if (!baseUrl || !/^https?:\/\//i.test(baseUrl)) {
    return 'not_configured';
  }
  const live = await probe(baseUrl, '/health/live', [200], fetchImpl, timeoutMs);
  if (live === null) {
    return 'unreachable';
  }
  const ready = await probe(baseUrl, '/health/ready', [200, 503], fetchImpl, timeoutMs);
  return ready === 'Healthy' ? 'ready' : 'not_ready';
}
