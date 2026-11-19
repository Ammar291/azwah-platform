import type { ErrorBoundaryProps } from 'expo-router';
import { router } from 'expo-router';
import { useTranslation } from 'react-i18next';

import { ErrorState } from '@/design-system/ErrorState';
import { Screen } from '@/design-system/Screen';

/**
 * Exported as `ErrorBoundary` by route layouts. Recovers from a screen that threw while rendering. The error itself
 * is never shown (it may hold internal detail); saved answers are unaffected.
 */
export function RouteErrorBoundary({ retry }: ErrorBoundaryProps) {
  const { t } = useTranslation();
  return (
    <Screen scroll={false}>
      <ErrorState
        title={t('errors.boundary.title')}
        message={t('errors.boundary.message')}
        retry={{ label: t('common.retry'), onPress: () => void retry() }}
        action={{ label: t('common.goToStart'), onPress: () => router.replace('/') }}
      />
    </Screen>
  );
}
