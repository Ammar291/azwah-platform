import { useTranslation } from 'react-i18next';

import { AppScreen } from '@/components/AppScreen';
import { ProfileView } from '@/features/profile/ProfileView';

export default function Settings() {
  const { t } = useTranslation();
  return (
    <AppScreen title={t('settings.title')} menu>
      <ProfileView />
    </AppScreen>
  );
}
