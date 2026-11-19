import { useTranslation } from 'react-i18next';

import { AppScreen } from '@/components/AppScreen';
import { HistoryList } from '@/features/history/HistoryList';

export default function History() {
  const { t } = useTranslation();
  return (
    <AppScreen title={t('history.title')}>
      <HistoryList />
    </AppScreen>
  );
}
