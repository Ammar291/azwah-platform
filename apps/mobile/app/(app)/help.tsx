import { useTranslation } from 'react-i18next';

import { AppScreen } from '@/components/AppScreen';
import { HelpContent } from '@/features/help/HelpContent';

export default function Help() {
  const { t } = useTranslation();
  return (
    <AppScreen title={t('help.title')}>
      <HelpContent />
    </AppScreen>
  );
}
