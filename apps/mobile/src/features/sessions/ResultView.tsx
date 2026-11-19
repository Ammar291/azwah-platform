import { useTranslation } from 'react-i18next';
import { StyleSheet, View } from 'react-native';

import { useRecommendations } from '@/api/participant/queries';
import type { Session, SessionResult } from '@/api/participant/schemas';
import { ProblemState } from '@/components/ProblemState';
import { useLeaveFlow } from '@/components/useLeaveFlow';
import { AppButton } from '@/design-system/AppButton';
import { AppCard } from '@/design-system/AppCard';
import { AppText } from '@/design-system/AppText';
import { Badge } from '@/design-system/Badge';
import { DetailRow } from '@/design-system/DetailRow';
import { ButtonRow } from '@/design-system/ButtonRow';
import { Notice } from '@/design-system/Notice';
import { spacing } from '@/design-system/tokens';
import { ScoreReportCard } from '@/features/score-report/ScoreReportCard';
import { useLocalization } from '@/i18n/useLocalization';

import { NewCheckInButton } from './NewCheckInButton';
import { RESULT_DISCLOSURE, type ResultDisclosure } from './resultDisclosure';
import { ScoreScale } from './ScoreScale';

export interface ResultViewProps {
  result: SessionResult;
  session: Session;
  /** Answers on this phone that the server could not include (it was already submitted). */
  changesNotIncluded?: number;
  disclosure?: ResultDisclosure;
}

/**
 * The server's result, as recorded (decision M-46). The app computes nothing and infers nothing: no score or tier is
 * derived from answers, and values are shown only where participant-facing display is approved (RESULT_DISCLOSURE),
 * exactly as the API returns them.
 */
export function ResultView({ result, session, changesNotIncluded = 0, disclosure = RESULT_DISCLOSURE }: ResultViewProps) {
  const { t, formatDate, formatNumber } = useLocalization();
  const { i18n } = useTranslation();
  const leaveFlow = useLeaveFlow();
  const submittedOn = session.submittedAt ? formatDate(session.submittedAt, 'dateTime') : null;
  const showsValues = disclosure.score || disclosure.tier || disclosure.categoryScores;
  // The server leaves the values out for anyone but the household's head (D-103); the app only says so.
  const headOnly = result.scoreVisibility === 'head_of_household_only';
  const tierLabel = (code: string) => (i18n.exists(`result.tiers.${code}`) ? t(`result.tiers.${code}`) : code);
  // A total with its band on a scale from the approved rule set: all the server's, for the head alone (D-111).
  const graded =
    result.state === 'complete' &&
    !headOnly &&
    disclosure.score &&
    disclosure.tier &&
    result.totalScore !== null &&
    result.tierCode !== null &&
    result.scale !== null
      ? { total: result.totalScore, tierCode: result.tierCode, scale: result.scale }
      : null;

  return (
    <>
      <AppCard variant="highlight">
        <Badge tone="success" label={t('result.submitted')} />
        <AppText variant="heading" accessibilityRole="header">
          {t('result.thanks')}
        </AppText>
        {submittedOn ? <AppText tone="secondary">{t('result.submittedOn', { date: submittedOn })}</AppText> : null}
      </AppCard>

      {changesNotIncluded > 0 ? (
        <Notice
          tone="warning"
          title={t('result.notIncludedTitle')}
          message={t('result.notIncludedMessage', { count: formatNumber(changesNotIncluded) })}
        />
      ) : null}

      <View style={styles.status}>
        <Badge
          tone={result.state === 'complete' ? 'success' : result.state === 'incomplete' ? 'warning' : 'info'}
          label={t(`result.state.${result.state}`)}
        />
      </View>

      {result.state === 'pending' ? (
        // Why it is pending ("rules await approval") is not said on screen (D-142).
        <Notice tone="info" message={t('result.pendingTitle')} />
      ) : null}

      {result.state === 'incomplete' ? (
        <Notice
          tone="warning"
          title={t('result.incompleteTitle')}
          message={
            result.incompleteReason && i18n.exists(`result.incompleteReasons.${result.incompleteReason}`)
              ? t(`result.incompleteReasons.${result.incompleteReason}`)
              : t('result.incompleteMessage')
          }
        />
      ) : null}

      {result.state === 'complete' && headOnly ? (
        <Notice tone="info" title={t('result.headOnlyTitle')} message={t('result.headOnly')} testID="result-head-only" />
      ) : null}

      {result.state === 'complete' && !headOnly && !showsValues ? (
        <Notice tone="info" message={t('result.notDisclosed')} testID="result-not-disclosed" />
      ) : null}

      {result.state === 'complete' && !headOnly && showsValues ? (
        <AppCard testID="result-values">
          <AppText variant="heading" accessibilityRole="header">
            {t('result.valuesTitle')}
          </AppText>
          {disclosure.score && result.totalScore !== null ? (
            <DetailRow layout="inline" label={t('result.totalScore')} value={formatNumber(result.totalScore)} />
          ) : null}
          {disclosure.tier && result.tierCode ? (
            <DetailRow layout="inline" label={t('result.tier')} value={tierLabel(result.tierCode)} />
          ) : null}
          {disclosure.categoryScores
            ? result.categoryScores.map((category) => (
                <DetailRow
                  key={category.categoryCode}
                  layout="inline"
                  label={t('result.category', { code: category.categoryCode })}
                  value={formatNumber(category.score)}
                />
              ))
            : null}
          {result.computedAt ? (
            <AppText variant="caption" tone="muted">
              {t('result.computedOn', { date: formatDate(result.computedAt, 'dateTime') })}
            </AppText>
          ) : null}
          {graded ? (
            <ScoreScale total={graded.total} tierCode={graded.tierCode} scale={graded.scale} tierLabel={tierLabel} />
          ) : null}
        </AppCard>
      ) : null}

      {graded ? (
        <ScoreReportCard
          sessionId={session.id}
          recorded={{ totalScore: graded.total, tierCode: graded.tierCode, scale: graded.scale }}
        />
      ) : null}

      {disclosure.recommendations && result.state === 'complete' ? (
        <SupportRecommendations sessionId={session.id} />
      ) : null}

      {/* Both leave the finished questionnaire's screens behind, so Back never returns to them. */}
      <ButtonRow minButtonWidth={130}>
        <AppButton label={t('result.backToQuestionnaires')} onPress={() => leaveFlow('/assessments')} />
        <AppButton label={t('common.goHome')} variant="secondary" onPress={() => leaveFlow()} />
      </ButtonRow>
      {/* Demo tools only (D-144): another round at once, so a second result adds a point to the trend. */}
      <NewCheckInButton />
    </>
  );
}

/** The support categories the server routed this result to, by code, as recorded. Fetched only when shown. */
function SupportRecommendations({ sessionId }: { sessionId: string }) {
  const { t } = useLocalization();
  const recommendations = useRecommendations(true);
  if (recommendations.error) {
    return <ProblemState error={recommendations.error} variant="inline" onRetry={() => void recommendations.refetch()} />;
  }
  const mine = (recommendations.data ?? []).filter((recommendation) => recommendation.sessionId === sessionId);
  if (mine.length === 0) {
    return null;
  }
  return (
    <AppCard testID="result-recommendations">
      <AppText variant="heading" accessibilityRole="header">
        {t('result.recommendationsTitle')}
      </AppText>
      {mine.map((recommendation) => (
        <DetailRow
          key={recommendation.id}
          layout="inline"
          label={t('result.recommendationCategory')}
          value={recommendation.categoryCode}
        />
      ))}
    </AppCard>
  );
}

const styles = StyleSheet.create({
  status: { gap: spacing.sm },
});
