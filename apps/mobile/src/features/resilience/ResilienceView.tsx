import { useRecommendations } from '@/api/participant/queries';
import { Section } from '@/design-system/Section';
import { AssessmentList } from '@/features/assessments/AssessmentList';
import { HistoryList } from '@/features/history/HistoryList';
import { NextSteps } from '@/features/home/NextSteps';
import { useLatestResult } from '@/features/home/useLatestResult';
import { NewCheckInButton } from '@/features/sessions/NewCheckInButton';
import { useLocalization } from '@/i18n/useLocalization';

import { ResilienceTrendCard } from './ResilienceTrendCard';

/**
 * "Your family resilience" (D-106): the family survey in one place. The questionnaires still to answer, the family's
 * recorded scores over time (D-132), the ones already sent (each opens its result; only the head of household sees the
 * family's score and band, D-103), and the family's next steps (D-146). With demo tools on, "New survey" opens another
 * round at once (D-144). Everything here is the server's; the app decides and computes nothing.
 */
export function ResilienceView() {
  const { t } = useLocalization();
  const recommendations = useRecommendations(true);
  const latest = useLatestResult();
  return (
    <>
      <AssessmentList openOnly />
      <ResilienceTrendCard />
      <NewCheckInButton />
      <Section title={t('history.title')} description={t('resilience.results')}>
        <HistoryList />
      </Section>
      <NextSteps recommendations={recommendations} report={latest.report} onOpenResult={latest.open} />
    </>
  );
}
