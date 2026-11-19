import type { ReactNode } from 'react';

import { LoadingState } from '@/design-system/LoadingState';

import { ProblemState } from './ProblemState';

/** The part of a TanStack Query result (or several combined) that QueryView needs. */
export interface QueryLike<T> {
  data: T | undefined;
  error: unknown;
  isPending: boolean;
  refetch: () => Promise<unknown>;
}

export interface QueryViewProps<T> {
  query: QueryLike<T>;
  /** Read while loading, for example "Loading your household". */
  loadingLabel: string;
  children: (data: T) => ReactNode;
}

/** Loading, a translated problem with retry, or the data: the three states of every screen that reads the API. */
export function QueryView<T>({ query, loadingLabel, children }: QueryViewProps<T>) {
  if (query.isPending) {
    return <LoadingState label={loadingLabel} />;
  }
  if (query.error || query.data === undefined) {
    return <ProblemState error={query.error} onRetry={() => void query.refetch()} />;
  }
  return <>{children(query.data)}</>;
}
