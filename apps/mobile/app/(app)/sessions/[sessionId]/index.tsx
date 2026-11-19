import { useTranslation } from 'react-i18next';

import { useRouteParam } from '@/components/useRouteParam';
import { SessionGate } from '@/features/sessions/SessionGate';
import { SessionQuestionnaire } from '@/features/sessions/SessionQuestionnaire';

export default function SessionScreen() {
  const { t } = useTranslation();
  const sessionId = useRouteParam('sessionId') ?? '';
  return (
    <SessionGate sessionId={sessionId} title={t('session.title')}>
      {(content) => <SessionQuestionnaire content={content} />}
    </SessionGate>
  );
}
