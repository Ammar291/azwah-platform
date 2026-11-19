import { useTranslation } from 'react-i18next';

import { isApiError } from '@/api/client';
import { useSession, useSessionResult } from '@/api/participant/queries';
import { AppScreen } from '@/components/AppScreen';
import { QueryView } from '@/components/QueryView';
import { useRouteParam } from '@/components/useRouteParam';
import { ErrorState } from '@/design-system/ErrorState';
import { useSubmissionState } from '@/features/sessions/AssessmentEngineContext';
import { ResultView } from '@/features/sessions/ResultView';

/** A failure that says nothing about the submission itself: the server could not give the result just now. */
const temporarilyUnavailable = (error: unknown) =>
  isApiError(error) && (error.kind === 'server' || error.kind === 'timeout' || error.kind === 'network');

export default function ResultScreen() {
  const { t } = useTranslation();
  const sessionId = useRouteParam('sessionId') ?? '';
  const session = useSession(sessionId);
  const result = useSessionResult(sessionId);
  const submission = useSubmissionState(sessionId);
  const submitted = session.data?.status === 'submitted' || session.data?.status === 'scored';

  // The server has the answers; only the result cannot be read right now (scoring or the connection).
  if (submitted && temporarilyUnavailable(result.error)) {
    return (
      <AppScreen title={t('result.title')}>
        <ErrorState
          title={t('result.unavailableTitle')}
          message={t('result.unavailableMessage')}
          retry={{ label: t('result.checkAgain'), onPress: () => void result.refetch() }}
        />
      </AppScreen>
    );
  }

  return (
    <AppScreen title={t('result.title')}>
      <QueryView query={session} loadingLabel={t('result.loading')}>
        {(sessionData) => (
          <QueryView query={result} loadingLabel={t('result.loading')}>
            {(data) => (
              <ResultView
                result={data}
                session={sessionData}
                changesNotIncluded={submission.status === 'submitted' ? submission.changesNotIncluded : 0}
              />
            )}
          </QueryView>
        )}
      </QueryView>
    </AppScreen>
  );
}
