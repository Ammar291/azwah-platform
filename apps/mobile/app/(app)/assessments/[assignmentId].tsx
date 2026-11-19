import { useTranslation } from 'react-i18next';

import { useAssignment } from '@/api/participant/queries';
import { AppScreen } from '@/components/AppScreen';
import { QueryView } from '@/components/QueryView';
import { useRouteParam } from '@/components/useRouteParam';
import { AssessmentDetail } from '@/features/assessments/AssessmentDetail';

export default function AssessmentScreen() {
  const { t } = useTranslation();
  const assignment = useAssignment(useRouteParam('assignmentId'));
  return (
    <AppScreen title={t('assessments.detailTitle')}>
      <QueryView query={assignment} loadingLabel={t('assessments.loadingOne')}>
        {(data) => <AssessmentDetail assignment={data} />}
      </QueryView>
    </AppScreen>
  );
}
