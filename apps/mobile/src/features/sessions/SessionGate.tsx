import { useRouter } from 'expo-router';
import { useEffect, useState, type ReactNode } from 'react';

import { isApiError } from '@/api/client';
import { AppScreen } from '@/components/AppScreen';
import { ProblemState } from '@/components/ProblemState';
import { EmptyState } from '@/design-system/EmptyState';
import { ErrorState } from '@/design-system/ErrorState';
import { LoadingState } from '@/design-system/LoadingState';
import { Notice } from '@/design-system/Notice';
import { useLocalization } from '@/i18n/useLocalization';

import { useAssessmentEngine } from './AssessmentEngineContext';
import { useSessionContent, VersionMismatchError, type SessionContent } from './useSessionContent';

const VERSION_CODES: ReadonlySet<string> = new Set(['questionnaires.version_not_found', 'sessions.version_not_published']);

/** The version this session was started on cannot be shown (decision M-42). */
const isVersionUnavailable = (error: unknown) =>
  error instanceof VersionMismatchError || (isApiError(error) && error.code !== undefined && VERSION_CODES.has(error.code));

const GONE_CODES: ReadonlySet<string> = new Set(['sessions.session_not_found', 'assignments.assignment_not_found']);

/** The session, or the assignment it answers, is no longer visible to this person (D-48 answers 404). */
const isSessionGone = (error: unknown) => isApiError(error) && error.code !== undefined && GONE_CODES.has(error.code);

/**
 * Loads a session and shows every state in which it cannot be answered, so the questionnaire and the review only
 * ever see an answerable session: loading, offline without a copy, version unavailable, no longer available,
 * submitted, closed, or another problem.
 */
export function SessionGate({
  sessionId,
  title,
  children,
}: {
  sessionId: string;
  title: string;
  children: (content: SessionContent) => ReactNode;
}) {
  const { t } = useLocalization();
  const router = useRouter();
  const content = useSessionContent(sessionId);
  const toQuestionnaires = { label: t('result.backToQuestionnaires'), onPress: () => router.replace('/assessments') };

  if (content.data) {
    const { status } = content.data.session;
    if (status === 'abandoned') {
      // Never submitted, so there is no result to show.
      return (
        <AppScreen title={title}>
          <ForgetLocalData sessionId={sessionId} />
          <EmptyState title={t('session.abandoned.title')} message={t('session.abandoned.message')} action={toQuestionnaires} />
        </AppScreen>
      );
    }
    if (status !== 'in_progress') {
      return (
        <AppScreen title={title}>
          <ForgetLocalData sessionId={sessionId} />
          <EmptyState
            title={t('session.alreadySubmitted.title')}
            message={t('session.alreadySubmitted.message')}
            action={{
              label: t('assessments.actions.viewResult'),
              onPress: () => router.replace({ pathname: '/sessions/[sessionId]/result', params: { sessionId } }),
            }}
          />
        </AppScreen>
      );
    }
    return <>{children(content.data)}</>;
  }

  let body: ReactNode;
  if (content.isPending) {
    body = <LoadingState label={t('session.loading')} />;
  } else if (content.offlineWithoutCopy) {
    body = (
      <ErrorState
        title={t('session.offlineNoCopy.title')}
        message={t('session.offlineNoCopy.message')}
        retry={{ label: t('common.retry'), onPress: () => void content.refetch() }}
      />
    );
  } else if (isVersionUnavailable(content.error)) {
    body = (
      <ErrorState
        title={t('session.versionUnavailable.title')}
        message={t('session.versionUnavailable.message')}
        action={toQuestionnaires}
      />
    );
  } else if (isSessionGone(content.error)) {
    body = (
      <>
        <ForgetLocalData sessionId={sessionId} />
        <ErrorState title={t('session.unavailable.title')} message={t('session.unavailable.message')} action={toQuestionnaires} />
      </>
    );
  } else {
    body = <ProblemState error={content.error} onRetry={() => void content.refetch()} />;
  }
  return <AppScreen title={title}>{body}</AppScreen>;
}

/**
 * The session can no longer take answers, so nothing about it needs to stay on the phone. Unsent answers are counted
 * first and the person is told how many could not be included: never removed silently.
 */
function ForgetLocalData({ sessionId }: { sessionId: string }) {
  const { t, formatNumber } = useLocalization();
  const engine = useAssessmentEngine();
  const [removed, setRemoved] = useState(0);
  useEffect(() => {
    let current = true;
    void (async () => {
      try {
        if (engine.submissions.getState(sessionId).status === 'sending') return;
        // Answers the sync already removed when the server refused them count too.
        const removedBySync = engine.existingSync(sessionId)?.getSnapshot().discarded ?? 0;
        const unsent = (await engine.store.listPending(sessionId)).length + removedBySync;
        engine.release(sessionId);
        await engine.store.discardSession(sessionId);
        if (current) setRemoved(unsent);
      } catch {
        // Kept; tried again the next time the session is opened.
      }
    })();
    return () => {
      current = false;
    };
  }, [engine, sessionId]);
  return removed > 0 ? (
    <Notice
      tone="warning"
      title={t('session.notIncluded.title')}
      message={t('session.notIncluded.message', { count: formatNumber(removed) })}
      announce
    />
  ) : null;
}
