import { getJson, type FetchLike } from './client';
import { healthResponseSchema } from './schemas';

export type ApiStatus = 'ready' | 'not_ready' | 'unreachable' | 'not_configured';

/**
 * Liveness says whether the API answers at all; readiness says whether it can reach its database. A 503 from
 * readiness is a valid answer ("not ready"), not a failure to connect.
 */
export async function fetchApiStatus(baseUrl: string | undefined, fetchImpl?: FetchLike): Promise<ApiStatus> {
  if (!baseUrl || !/^https?:\/\//i.test(baseUrl)) {
    return 'not_configured';
  }

  try {
    await getJson({ baseUrl, path: '/health/live', schema: healthResponseSchema, fetchImpl });
  } catch {
    return 'unreachable';
  }

  try {
    const ready = await getJson({
      baseUrl,
      path: '/health/ready',
      schema: healthResponseSchema,
      acceptStatuses: [200, 503],
      fetchImpl,
    });
    return ready.data.status === 'Healthy' ? 'ready' : 'not_ready';
  } catch {
    return 'not_ready';
  }
}
