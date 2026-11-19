import { useTranslation } from 'react-i18next';

import { AppScreen } from '@/components/AppScreen';
import { AssessmentList } from '@/features/assessments/AssessmentList';

export default function Assessments() {
  const { t } = useTranslation();
  return (
    <AppScreen title={t('assessments.title')}>
      <AssessmentList />
    </AppScreen>
  );
}
