import { useTranslation } from 'react-i18next';

import { AppScreen } from '@/components/AppScreen';
import { ConsultantView } from '@/features/consultant/ConsultantView';

export default function Consultant() {
  const { t } = useTranslation();
  return (
    <AppScreen title={t('consultant.title')} menu>
      <ConsultantView />
    </AppScreen>
  );
}
