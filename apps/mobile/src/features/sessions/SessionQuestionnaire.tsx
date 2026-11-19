import { useRouter } from 'expo-router';
import { useEffect, useRef, useState } from 'react';
import { AccessibilityInfo, type Text } from 'react-native';

import { AppScreen } from '@/components/AppScreen';
import { demoToolsEnabled } from '@/config';
import { AppButton } from '@/design-system/AppButton';
import { AppText } from '@/design-system/AppText';
import { ButtonRow } from '@/design-system/ButtonRow';
import { ErrorState } from '@/design-system/ErrorState';
import { LoadingState } from '@/design-system/LoadingState';
import { Notice } from '@/design-system/Notice';
import { ProgressIndicator } from '@/design-system/ProgressIndicator';
import { ReportPreparationChoice } from '@/features/score-report/ReportPreparationChoice';
import { useLocalization } from '@/i18n/useLocalization';

import { closedByExpiry } from './answerSync';
import { useSubmissionState } from './AssessmentEngineContext';
import { ConflictCard } from './ConflictCard';
import { DemoScoreButtons } from './DemoScoreButtons';
import { isAnswerable } from './questionnaire';
import { QuestionRenderer } from './QuestionRenderer';
import { clearQuestionRequest, useQuestionRequest } from './questionRequests';
import { SaveStatusLine } from './SaveStatusLine';
import { SessionNotices } from './SessionNotices';
import { useAnswerDrafts } from './useAnswerDrafts';
import type { SessionContent } from './useSessionContent';

/**
 * How long Previous and Next keep ignoring presses once the question they moved to is on screen: longer than the gap
 * in a double tap, shorter than reading a question.
 */
export const QUESTION_SETTLE_MS = 350;

/**
 * One question at a time from the server's schema, with large answer cards, progress in words and a bar, the save
 * status and anything that needs the person (a conflict, a refused answer). Nothing here interprets answers.
 */
export function SessionQuestionnaire({ content }: { content: SessionContent }) {
  const { t, pickText } = useLocalization();
  const router = useRouter();
  if (!isAnswerable(content.items)) {
    const unsupported = content.items.length > 0;
    return (
      <AppScreen title={pickText(content.assignment.instrumentName)}>
        <ErrorState
          title={t(unsupported ? 'questionnaire.unsupported.title' : 'session.noQuestions.title')}
          message={t(unsupported ? 'questionnaire.unsupported.message' : 'session.noQuestions.message')}
          action={{ label: t('common.goHome'), onPress: () => router.replace('/home') }}
        />
      </AppScreen>
    );
  }
  return <Questions content={content} />;
}

function Questions({ content }: { content: SessionContent }) {
  const { t, pickText, formatNumber } = useLocalization();
  const router = useRouter();
  const { session, assignment, items, sectionTitle } = content;
  const drafts = useAnswerDrafts(content);
  const submission = useSubmissionState(session.id);
  const request = useQuestionRequest(session.id);
  const [position, setPosition] = useState<number | null>(null);
  const [handledRequest, setHandledRequest] = useState<string | null>(null);
  /** The question was opened from the review to answer it: say why it matters if it is still unanswered. */
  const [fromReview, setFromReview] = useState<number | null>(null);
  const heading = useRef<Text>(null);
  const clamp = (value: number) => Math.min(Math.max(value, 0), items.length - 1);

  // Go where the review screen asked; otherwise start once at the first unanswered question. Adjusted during
  // render (React's pattern for state that follows props), so the screen never shows the wrong question first.
  if (request && request.key !== handledRequest) {
    setHandledRequest(request.key);
    setPosition(clamp(request.index));
    setFromReview(clamp(request.index));
  } else if (position === null && drafts.ready) {
    const firstOpen = items.findIndex((item) => drafts.answers[item.id] === undefined);
    setPosition(firstOpen === -1 ? 0 : firstOpen);
  }
  const index = position ?? 0;
  // The second tap of a double tap lands on the question the first one opened, and would skip it. So once Previous or
  // Next moves, both ignore presses until the new question has rendered, and for QUESTION_SETTLE_MS after that.
  const moving = useRef(false);
  const settlesAt = useRef(0);
  const go = (next: number) => {
    const target = clamp(next);
    if (moving.current || Date.now() < settlesAt.current || target === index) {
      return;
    }
    moving.current = true;
    setFromReview(null);
    setPosition(target);
  };

  // The question a press moved to is on screen: the settling time starts now, however long rendering took.
  useEffect(() => {
    if (moving.current) {
      moving.current = false;
      settlesAt.current = Date.now() + QUESTION_SETTLE_MS;
    }
  }, [index]);

  // A request is used once; leaving and coming back later starts at the first unanswered question again.
  useEffect(() => {
    if (handledRequest !== null) {
      clearQuestionRequest(session.id, handledRequest);
    }
  }, [handledRequest, session.id]);

  // Screen readers move to the new question.
  useEffect(() => {
    if (heading.current) {
      AccessibilityInfo.sendAccessibilityEvent(heading.current, 'focus');
    }
  }, [index]);

  const item = items[index];
  // Only while the local drafts load (a moment); `items` is never empty here.
  if (!item || position === null) {
    return (
      <AppScreen title={pickText(assignment.instrumentName)}>
        <LoadingState label={t('session.loading')} />
      </AppScreen>
    );
  }
  const prompt = pickText(item.prompt);
  const last = index === items.length - 1;
  const value = drafts.answers[item.id] ?? null;
  const conflict = drafts.sync.conflicts.find((candidate) => candidate.itemId === item.id);
  const refused = drafts.sync.rejected.includes(item.id);
  // A submission waiting for the server would send these answers: they are frozen until it is cancelled.
  const locked = submission.status === 'queued' || submission.status === 'sending';
  // The time to answer has passed (SessionNotices says so): nothing chosen now could be kept.
  const expired = closedByExpiry(drafts.sync);

  const review = () => {
    void drafts.flush();
    router.push({ pathname: '/sessions/[sessionId]/review', params: { sessionId: session.id } });
  };

  return (
    <AppScreen
      title={pickText(assignment.instrumentName)}
      footer={
        <ButtonRow>
          <AppButton label={t('common.previous')} variant="secondary" disabled={index === 0} onPress={() => go(index - 1)} />
          {last ? (
            <AppButton label={t('session.reviewAnswers')} onPress={review} />
          ) : (
            <AppButton label={t('common.next')} trailingIcon="chevron-forward" onPress={() => go(index + 1)} />
          )}
        </ButtonRow>
      }
    >
      <SessionNotices content={content} sync={drafts.sync} />
      <ReportPreparationChoice content={content} />
      {locked ? (
        <Notice
          tone="info"
          title={t('session.locked.title')}
          message={t('session.locked.message')}
          action={{ label: t('session.locked.action'), onPress: review }}
        />
      ) : null}
      {demoToolsEnabled ? <DemoScoreButtons sessionId={session.id} disabled={locked || expired} /> : null}
      <ProgressIndicator
        current={index + 1}
        total={items.length}
        label={t('session.progress', { current: formatNumber(index + 1), total: formatNumber(items.length) })}
      />
      {sectionTitle ? (
        <AppText variant="label" tone="secondary">
          {pickText(sectionTitle)}
        </AppText>
      ) : null}
      <AppText ref={heading} variant="heading" accessibilityRole="header">
        {prompt}
      </AppText>
      <AppText variant="caption" tone="muted">
        {t(item.isRequired ? 'session.required' : 'session.optional')}
      </AppText>
      {/* Draft content may lack Arabic for a question or its answers (D-40); they show in English, unlabelled (D-142). */}
      {fromReview === index && item.isRequired && value === null ? (
        <Notice tone="warning" message={t('session.requiredMissing')} announce />
      ) : null}
      {refused && value === null ? <Notice tone="danger" message={t('session.answerRefused')} announce /> : null}
      {conflict ? (
        <ConflictCard
          item={item}
          conflict={conflict}
          onKeepLocal={() => drafts.keepLocal(item.id)}
          onKeepServer={() => drafts.keepServer(item.id)}
        />
      ) : null}
      <QuestionRenderer
        item={item}
        value={value}
        onChange={(optionId) => void drafts.choose(item.id, optionId)}
        disabled={locked || expired}
      />
      <SaveStatusLine
        status={drafts.saveStatus}
        lastSyncedAt={drafts.sync.lastSyncedAt}
        onRetry={() => void drafts.flush()}
      />
    </AppScreen>
  );
}
