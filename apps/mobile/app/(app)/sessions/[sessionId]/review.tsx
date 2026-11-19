import { useTranslation } from 'react-i18next';

import { useRouteParam } from '@/components/useRouteParam';
import { ReviewAnswers } from '@/features/sessions/ReviewAnswers';
import { SessionGate } from '@/features/sessions/SessionGate';

export default function ReviewScreen() {
  const { t } = useTranslation();
  const sessionId = useRouteParam('sessionId') ?? '';
  return (
    <SessionGate sessionId={sessionId} title={t('review.title')}>
      {(content) => <ReviewAnswers content={content} />}
    </SessionGate>
  );
}
