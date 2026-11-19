import type { UseQueryResult } from '@tanstack/react-query';
import { StyleSheet, View } from 'react-native';

import { useAssignments } from '@/api/participant/queries';
import type { Recommendation, ScoreReport } from '@/api/participant/schemas';
import { ProblemState } from '@/components/ProblemState';
import { AppButton } from '@/design-system/AppButton';
import { AppCard } from '@/design-system/AppCard';
import { AppText } from '@/design-system/AppText';
import { Badge } from '@/design-system/Badge';
import { Icon } from '@/design-system/Icon';
import { LoadingState } from '@/design-system/LoadingState';
import { Section } from '@/design-system/Section';
import { colors, radii, spacing } from '@/design-system/tokens';
import { useLocalization } from '@/i18n/useLocalization';

import { isCheckInDone } from './familyPath';

const NUMBER_SIZE = 28;

export interface NextStepsProps {
  recommendations: UseQueryResult<Recommendation[]>;
  /** The latest result's report on this phone (D-146), whose next steps are listed when nothing was routed. */
  report: ScoreReport | null;
  /** Opens the latest recorded result, where its report and next steps are; null when there is none to open. */
  onOpenResult: (() => void) | null;
}

/**
 * The family's next steps. Support the server routed (D-53) comes first, newest first, exactly as recorded: its category
 * code, as the result screen shows it, since category wording is not approved (open question 13). With none routed, the
 * steps of the latest result's written report are listed by area, as the report has them (D-114, D-146), with the way
 * to the whole report; the app never chooses, orders or explains a step itself. Before the family survey it says where
 * the steps will appear.
 */
export function NextSteps({ recommendations, report, onOpenResult }: NextStepsProps) {
  const { t, formatNumber } = useLocalization();
  const assignments = useAssignments();
  const surveyDone = isCheckInDone(assignments.data ?? []);
  const items = [...(recommendations.data ?? [])].sort((a, b) => b.createdAt.localeCompare(a.createdAt));
  const written = report?.nextSteps ?? [];
  const showWritten = recommendations.data !== undefined && items.length === 0 && written.length > 0;

  return (
    <Section title={t('home.nextSteps.title')}>
      {recommendations.isPending ? <LoadingState label={t('home.nextSteps.loading')} /> : null}
      {recommendations.error ? (
        <ProblemState error={recommendations.error} variant="inline" onRetry={() => void recommendations.refetch()} />
      ) : null}
      {showWritten ? (
        <AppCard testID="next-steps-report">
          {/* The report's words, so they keep its mark (D-112). */}
          <Badge tone="info" label={t('scoreReport.badge')} />
          {written.map((step, index) => (
            <View key={`${index}-${step.sectionCode}`} style={styles.row} testID={`next-steps-report-${index + 1}`}>
              <View style={styles.number}>
                <AppText variant="label" tone="inverse">
                  {formatNumber(index + 1)}
                </AppText>
              </View>
              <View style={styles.words}>
                <AppText variant="bodyStrong">{step.area}</AppText>
                <AppText tone="secondary">{step.reason}</AppText>
              </View>
            </View>
          ))}
          {onOpenResult ? <AppButton label={t('home.nextSteps.openReport')} variant="secondary" onPress={onOpenResult} /> : null}
        </AppCard>
      ) : null}
      {recommendations.data && items.length === 0 && !showWritten ? (
        <AppCard variant="muted" testID="next-steps-empty">
          <AppText tone="secondary">
            {t(!surveyDone ? 'home.nextSteps.empty' : onOpenResult ? 'home.nextSteps.withResult' : 'home.nextSteps.emptyAfterSurvey')}
          </AppText>
          {surveyDone && onOpenResult ? (
            <AppButton label={t('home.nextSteps.openResult')} variant="secondary" onPress={onOpenResult} />
          ) : null}
        </AppCard>
      ) : null}
      {items.map((recommendation) => (
        <AppCard key={recommendation.id} testID={`next-step-${recommendation.id}`}>
          <View style={styles.row}>
            <View style={styles.glyph}>
              <Icon name="dot" size={20} color={colors.primary} />
            </View>
            <View style={styles.words}>
              <AppText variant="label" tone="secondary">
                {t('result.recommendationCategory')}
              </AppText>
              <AppText variant="bodyStrong">{recommendation.categoryCode}</AppText>
              {recommendation.referralStatus ? (
                <Badge
                  tone={recommendation.referralStatus === 'cancelled' ? 'neutral' : 'info'}
                  label={t(`home.nextSteps.referral.${recommendation.referralStatus}`)}
                />
              ) : null}
            </View>
          </View>
        </AppCard>
      ))}
    </Section>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', alignItems: 'flex-start', gap: spacing.md },
  glyph: {
    width: spacing.xxl,
    height: spacing.xxl,
    borderRadius: radii.pill,
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: colors.primarySoft,
  },
  number: {
    width: NUMBER_SIZE,
    height: NUMBER_SIZE,
    borderRadius: radii.pill,
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: colors.primary,
  },
  words: { flex: 1, gap: spacing.xxs },
});
