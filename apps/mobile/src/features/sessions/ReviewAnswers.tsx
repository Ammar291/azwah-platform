import { useRouter } from 'expo-router';
import { useEffect, useState } from 'react';
import { StyleSheet, View } from 'react-native';

import { AppScreen } from '@/components/AppScreen';
import { useLeaveFlow } from '@/components/useLeaveFlow';
import { useSingleFlight } from '@/components/useSingleFlight';
import { AppButton } from '@/design-system/AppButton';
import { AppText } from '@/design-system/AppText';
import { ConfirmationDialog } from '@/design-system/ConfirmationDialog';
import { Icon } from '@/design-system/Icon';
import { ListGroup, ListRow } from '@/design-system/ListGroup';
import { Notice } from '@/design-system/Notice';
import { spacing, statusColors } from '@/design-system/tokens';
import { useLocalization } from '@/i18n/useLocalization';

import { closedByExpiry } from './answerSync';
import { useAssessmentEngine, useSubmissionState } from './AssessmentEngineContext';
import { checkAnswers } from './questionnaire';
import { requestQuestion } from './questionRequests';
import { SaveStatusLine } from './SaveStatusLine';
import { SessionNotices } from './SessionNotices';
import { SubmissionStatus } from './SubmissionStatus';
import { useAnswerDrafts } from './useAnswerDrafts';
import type { SessionContent } from './useSessionContent';

/**
 * Every question with the chosen answer, what still needs the person (required questions, conflicts, refused
 * answers) with a way to each, and submission. Submitting queues a request with a stable Idempotency-Key; the
 * queue sends every answer first and submits once, now or when the connection returns (decision M-41).
 */
export function ReviewAnswers({ content }: { content: SessionContent }) {
  const { t, pickText, formatNumber } = useLocalization();
  const router = useRouter();
  const leaveFlow = useLeaveFlow();
  const engine = useAssessmentEngine();
  const { session, assignment, items } = content;
  const drafts = useAnswerDrafts(content);
  const submission = useSubmissionState(session.id);
  const once = useSingleFlight();
  const [confirming, setConfirming] = useState(false);

  const check = checkAnswers(items, drafts.answers);
  const conflicts = drafts.sync.conflicts;
  const waiting = submission.status === 'queued' || submission.status === 'sending';
  // The time to answer has passed (SessionNotices says so): the server would refuse a submission.
  const expired = closedByExpiry(drafts.sync);
  const ready = check.missingRequired.length === 0 && check.invalid.length === 0 && conflicts.length === 0 && !expired;

  // Confirmed by the server: show the result. Only then. The questionnaire and this review are finished with, so
  // they leave the stack: Back from the result goes Home, not to "already submitted".
  useEffect(() => {
    if (submission.status === 'submitted') {
      leaveFlow({ pathname: '/sessions/[sessionId]/result', params: { sessionId: session.id } });
    }
  }, [leaveFlow, session.id, submission.status]);

  // Back to the questionnaire under this screen, at the chosen question; opened directly, it replaces this screen.
  const edit = (index: number) => {
    requestQuestion(session.id, index);
    if (router.canGoBack()) {
      router.back();
    } else {
      router.replace({ pathname: '/sessions/[sessionId]', params: { sessionId: session.id } });
    }
  };
  const indexOf = (itemId: string) => items.findIndex((item) => item.id === itemId);
  const firstMissing = check.missingRequired[0];
  const firstConflict = conflicts[0];

  const submit = () =>
    void once(async () => {
      setConfirming(false);
      await engine.submissions.submit(session.id);
    });

  return (
    <AppScreen
      title={t('review.title')}
      footer={
        waiting && submission.status === 'queued' ? (
          <AppButton
            label={t('review.cancelSubmission')}
            variant="secondary"
            onPress={() => void once(() => engine.submissions.cancel(session.id))}
          />
        ) : (
          <AppButton
            label={t(waiting ? 'review.submitting' : 'review.submit')}
            loading={submission.status === 'sending'}
            disabled={!ready || waiting}
            onPress={() => setConfirming(true)}
          />
        )
      }
    >
      <AppText variant="bodyStrong">{pickText(assignment.instrumentName)}</AppText>
      <SessionNotices content={content} sync={drafts.sync} />
      <AppText tone="secondary" accessibilityRole="summary" testID="review-summary">
        {t('review.summary', { answered: formatNumber(check.answered), total: formatNumber(check.total) })}
      </AppText>

      <SubmissionStatus state={submission} onRetry={() => void engine.submissions.retryNow(session.id)} />

      {firstMissing ? (
        <Notice
          tone="warning"
          title={t('review.missingTitle')}
          message={t('review.missingMessage', { count: formatNumber(check.missingRequired.length) })}
          action={{ label: t('review.answerMissing'), onPress: () => edit(indexOf(firstMissing.id)) }}
        />
      ) : null}
      {firstConflict ? (
        <Notice
          tone="warning"
          title={t('review.conflictsTitle')}
          message={t('review.conflictsMessage', { count: formatNumber(conflicts.length) })}
          action={{ label: t('review.resolveConflict'), onPress: () => edit(indexOf(firstConflict.itemId)) }}
        />
      ) : null}
      {ready && !waiting ? <Notice tone="info" message={t('review.readyMessage')} /> : null}
      <SaveStatusLine status={drafts.saveStatus} lastSyncedAt={drafts.sync.lastSyncedAt} onRetry={() => void drafts.flush()} />

      {/* One surface for the whole list, rows divided by hairlines, rather than a card per question. */}
      <ListGroup>
        {items.map((item, index) => {
          const chosen = item.options.find((option) => option.id === drafts.answers[item.id]);
          const prompt = pickText(item.prompt);
          const conflicted = conflicts.some((conflict) => conflict.itemId === item.id);
          const answer = chosen ? pickText(chosen.label) : t('review.notAnswered');
          return (
            <ListRow
              key={item.id}
              testID={`review-item-${index + 1}`}
              onPress={() => edit(index)}
              accessibilityLabel={t('review.itemLabel', { number: formatNumber(index + 1), prompt, answer })}
              accessibilityHint={t('review.changeHint')}
            >
              <View style={styles.item}>
                <AppText variant="caption" tone="muted">
                  {t('review.questionNumber', { number: formatNumber(index + 1) })}
                </AppText>
                <AppText>{prompt}</AppText>
                <View style={styles.answer}>
                  {chosen && !conflicted ? null : <Icon name="alert" size={16} color={statusColors.warning} />}
                  <AppText variant="bodyStrong" tone={chosen ? 'primary' : 'warning'} style={styles.answerText}>
                    {answer}
                  </AppText>
                </View>
                {conflicted ? (
                  <AppText variant="caption" tone="warning">
                    {t('review.conflicted')}
                  </AppText>
                ) : null}
                {!chosen && item.isRequired ? (
                  <AppText variant="caption" tone="warning">
                    {t('review.required')}
                  </AppText>
                ) : null}
              </View>
            </ListRow>
          );
        })}
      </ListGroup>

      <ConfirmationDialog
        visible={confirming}
        title={t('review.confirmTitle')}
        message={t('review.confirmMessage')}
        confirmLabel={t('review.submit')}
        cancelLabel={t('review.keepEditing')}
        busy={submission.status === 'sending'}
        onConfirm={submit}
        onCancel={() => setConfirming(false)}
      />
    </AppScreen>
  );
}

const styles = StyleSheet.create({
  item: { gap: spacing.xs },
  answer: { flexDirection: 'row', alignItems: 'center', gap: spacing.xs },
  answerText: { flexShrink: 1 },
});
