import { useTranslation } from 'react-i18next';

import { AppScreen } from '@/components/AppScreen';
import { RouteErrorBoundary } from '@/components/RouteErrorBoundary';
import { ConsentPanel } from '@/features/consent/ConsentPanel';

/** Signed in, outside the main group: reached from home, profile and anywhere consent is missing. */
export default function Consent() {
  const { t } = useTranslation();
  return (
    <AppScreen title={t('consent.title')}>
      <ConsentPanel />
    </AppScreen>
  );
}

export { RouteErrorBoundary as ErrorBoundary };
