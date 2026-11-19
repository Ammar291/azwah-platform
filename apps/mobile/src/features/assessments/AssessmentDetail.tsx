import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useRouter } from 'expo-router';
import { useRef } from 'react';
import { StyleSheet, View } from 'react-native';

import { isApiError } from '@/api/client';
import { useParticipantApi } from '@/api/participant/ParticipantApiContext';
import { invalidateParticipantData, useHistory, usePublishedVersion } from '@/api/participant/queries';
import type { Assignment } from '@/api/participant/schemas';
import { useAppMode } from '@/app-shell/AppModeContext';
import { ASSIGNMENT_STATUS_VIEW } from '@/components/assignmentStatus';
import { ProblemState } from '@/components/ProblemState';
import { roundOf } from '@/components/roundLabel';
import { useSingleFlight } from '@/components/useSingleFlight';
import { AppButton } from '@/design-system/AppButton';
import { AppCard } from '@/design-system/AppCard';
import { AppText } from '@/design-system/AppText';
import { Badge } from '@/design-system/Badge';
import { DetailRow } from '@/design-system/DetailRow';
import { Notice } from '@/design-system/Notice';
import { Section } from '@/design-system/Section';
import { borderWidths, colors, spacing } from '@/design-system/tokens';
import { useDraftStore } from '@/drafts/DraftStoreContext';
import { useSubmissionState } from '@/features/sessions/AssessmentEngineContext';
import { isAnswerable, selectItems } from '@/features/sessions/questionnaire';
import { useLocalization } from '@/i18n/useLocalization';


/**
 * The introduction to one assignment (decision M-45): its name, its purpose and number of questions from the published
 * version, its round and dates, how answers are handled, and the next step (start, continue or see the result). No
 * duration is shown: the API gives none, and an estimate from the question count would not be defensible.
 *
 * The purpose is the description of an approved (or since retired) version only: a draft carries its source
 * document's own wording (a codebook subtitle, say), which nobody wrote for participants. An Arabic reader is told
 * when part of the questionnaire has no Arabic yet and so appears in English (draft content only, D-40).
 */
export function AssessmentDetail({ assignment }: { assignment: Assignment }) {
  const { t, pickText, formatDate, formatNumber } = useLocalization();
  const router = useRouter();
  const api = useParticipantApi();
  const queryClient = useQueryClient();
  const mode = useAppMode();
  const status = ASSIGNMENT_STATUS_VIEW[assignment.status];
  const store = useDraftStore();
  const version = usePublishedVersion(assignment.instrumentCode, assignment.versionNumber);
  const items = version.data ? selectItems(version.data, assignment.sectionCode).items : null;
  const answerable = items !== null && isAnswerable(items);
  const submission = useSubmissionState(assignment.activeSessionId);
  const awaitingSubmission = submission.status === 'queued' || submission.status === 'sending';
  // What this phone keeps. With development fixtures the simulated server is on the phone too and its made-up data
  // outlives sign-out until it is reset, so the real build's wording would not be true there; the screen says nothing
  // about it (D-142; the demonstration guide does).
  const onPhone = mode === 'development_fixtures' ? '' : t(`assessments.intro.privacyOnPhone.${store.persistence}`);
  const openSession = (sessionId: string) =>
    router.push({ pathname: '/sessions/[sessionId]', params: { sessionId } });

  const once = useSingleFlight();
  const started = useRef(false);

  const start = useMutation({
    mutationFn: async () => {
      try {
        return (await api.startSession(assignment.id)).id;
      } catch (error) {
        // After a lost answer the session may already exist: open it instead of refusing.
        if (isApiError(error) && error.code === 'sessions.session_already_exists') {
          const current = await api.getAssignment(assignment.id);
          if (current.activeSessionId) {
            return current.activeSessionId;
          }
        }
        throw error;
      }
    },
    onSuccess: (sessionId) => {
      void invalidateParticipantData(queryClient);
      openSession(sessionId);
    },
  });

  // One start however often it is pressed, and none once the session is open.
  const startOnce = () =>
    void once(async () => {
      if (started.current) {
        return;
      }
      try {
        await start.mutateAsync();
        started.current = true;
      } catch {
        // Shown with a retry.
      }
    });

  return (
    <>
      <AppCard>
        <Badge label={t(status.labelKey)} tone={status.tone} />
        <AppText variant="heading" accessibilityRole="header">
          {pickText(assignment.instrumentName)}
        </AppText>
        {assignment.sectionTitle ? <AppText tone="secondary">{pickText(assignment.sectionTitle)}</AppText> : null}
        <View style={styles.facts}>
          <DetailRow style={styles.fact} label={t('assessments.facts.wave')} value={roundOf(assignment.waveCode)} />
          <DetailRow style={styles.fact} label={t('assessments.facts.assigned')} value={formatDate(assignment.assignedAt)} />
          {assignment.expiresAt ? (
            <DetailRow style={styles.fact} label={t('assessments.facts.due')} value={formatDate(assignment.expiresAt)} />
          ) : null}
          {assignment.completedAt ? (
            <DetailRow style={styles.fact} label={t('assessments.facts.completed')} value={formatDate(assignment.completedAt)} />
          ) : null}
          {items ? (
            <DetailRow style={styles.fact} label={t('assessments.facts.questions')} value={formatNumber(items.length)} />
          ) : null}
          {items && items.some((item) => !item.isRequired) ? (
            <DetailRow style={styles.fact}
              label={t('assessments.facts.optionalQuestions')}
              value={formatNumber(items.filter((item) => !item.isRequired).length)}
            />
          ) : null}
        </View>
      </AppCard>

      {version.data && version.data.status !== 'draft' ? (
        <Section title={t('assessments.intro.purpose')}>
          <AppText>{pickText(version.data.description)}</AppText>
        </Section>
      ) : null}
      {version.error ? (
        <ProblemState error={version.error} variant="inline" onRetry={() => void version.refetch()} />
      ) : null}
      {items && !answerable ? (
        <Notice tone="warning" title={t('questionnaire.unsupported.title')} message={t('questionnaire.unsupported.message')} />
      ) : null}

      <Notice
        tone="info"
        title={t('assessments.intro.privacyTitle')}
        message={`${t('assessments.intro.privacy')} ${onPhone}`.trim()}
        testID="privacy-notice"
      />

      {awaitingSubmission && assignment.activeSessionId ? (
        <Notice
          tone="warning"
          title={t('submission.queued.title')}
          message={t('assessments.intro.awaitingSubmission')}
          action={{
            label: t('assessments.intro.viewSubmission'),
            onPress: () =>
              router.push({
                pathname: '/sessions/[sessionId]/review',
                params: { sessionId: assignment.activeSessionId ?? '' },
              }),
          }}
        />
      ) : null}

      {start.error ? <ProblemState error={start.error} variant="inline" onRetry={startOnce} /> : null}

      {assignment.status === 'assigned' ? (
        <>
          <AppText tone="secondary">{t('assessments.beforeYouStart')}</AppText>
          <AppButton
            label={t('assessments.actions.start')}
            loading={start.isPending || version.isPending}
            disabled={!answerable}
            onPress={startOnce}
          />
        </>
      ) : null}
      {assignment.status === 'in_progress' && assignment.activeSessionId && !awaitingSubmission ? (
        <AppButton
          label={t('assessments.actions.continue')}
          trailingIcon="chevron-forward"
          onPress={() => openSession(assignment.activeSessionId ?? '')}
        />
      ) : null}
      {assignment.status === 'completed' ? <CompletedResultLink assignmentId={assignment.id} /> : null}
      {assignment.status === 'expired' || assignment.status === 'cancelled' ? (
        <Notice tone="info" message={t(`assessments.closed.${assignment.status}`)} />
      ) : null}
    </>
  );
}

/** The way to a finished questionnaire's result, found through the history; says so while loading or when it fails. */
function CompletedResultLink({ assignmentId }: { assignmentId: string }) {
  const { t } = useLocalization();
  const router = useRouter();
  const history = useHistory();
  if (history.error) {
    return <ProblemState error={history.error} variant="inline" onRetry={() => void history.refetch()} />;
  }
  const session = history.data?.find((item) => item.assignmentId === assignmentId);
  if (!session) {
    return history.isPending ? (
      <AppButton label={t('assessments.actions.viewResult')} variant="secondary" loading />
    ) : null;
  }
  return (
    <AppButton
      label={t('assessments.actions.viewResult')}
      variant="secondary"
      onPress={() =>
        router.push({ pathname: '/sessions/[sessionId]/result', params: { sessionId: session.sessionId } })
      }
    />
  );
}


const styles = StyleSheet.create({
  // Two columns, so the round, dates and counts read as one set of facts rather than a tall list.
  facts: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    rowGap: spacing.md,
    columnGap: spacing.lg,
    paddingTop: spacing.md,
    borderTopWidth: borderWidths.hairline,
    borderTopColor: colors.borderSoft,
  },
  fact: { flexGrow: 1, flexBasis: '40%' },
});
