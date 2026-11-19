import { useTranslation } from 'react-i18next';

import { AppScreen } from '@/components/AppScreen';
import { ResilienceView } from '@/features/resilience/ResilienceView';

export default function Resilience() {
  const { t } = useTranslation();
  return (
    <AppScreen title={t('resilience.title')} menu>
      <ResilienceView />
    </AppScreen>
  );
}
