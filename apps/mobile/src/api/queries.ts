import { QueryClient, useQuery } from '@tanstack/react-query';

import { apiBaseUrl } from '@/config';

import { isRetryable } from './client';
import { fetchApiStatus } from './health';

export const apiStatusQueryKey = ['api-status'] as const;

/** Retries only network trouble and server faults, once; a 4xx is an answer, not a glitch. */
export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: { retry: (failureCount, error) => isRetryable(error) && failureCount < 1, staleTime: 30_000 },
      mutations: { retry: false },
    },
  });
}

/** fetchApiStatus never throws: every outcome is a status to show. */
export function useApiStatus() {
  return useQuery({ queryKey: apiStatusQueryKey, queryFn: () => fetchApiStatus(apiBaseUrl), retry: false });
}
