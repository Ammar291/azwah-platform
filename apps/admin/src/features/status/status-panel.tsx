'use client';

import { useHealth } from '@/lib/api/queries';
import { useI18n } from '@/lib/i18n-context';

import { StatusView, type StatusViewState } from './status-view';

/** The API's health, asked through the dashboard's own server: the browser never calls the API. */
export function StatusPanel() {
  const { t } = useI18n();
  const health = useHealth();
  const status: StatusViewState = health.isFetching ? 'checking' : health.isError ? 'unreachable' : (health.data?.status ?? 'checking');
  return <StatusView status={status} messages={t} onRetry={() => void health.refetch()} />;
}
