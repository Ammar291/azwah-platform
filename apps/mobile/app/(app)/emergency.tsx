import { useTranslation } from 'react-i18next';

import { AppScreen } from '@/components/AppScreen';
import { EmergencyNumberList } from '@/features/emergency/EmergencyNumberList';

export default function Emergency() {
  const { t } = useTranslation();
  return (
    <AppScreen title={t('emergency.title')} keyboardAware>
      <EmergencyNumberList />
    </AppScreen>
  );
}
