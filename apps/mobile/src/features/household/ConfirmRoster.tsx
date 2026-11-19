import { useRouter } from 'expo-router';
import { useRef } from 'react';
import { useTranslation } from 'react-i18next';

import { AppScreen } from '@/components/AppScreen';
import { describeProblem } from '@/components/describeProblem';
import { ProblemState } from '@/components/ProblemState';
import { QueryView } from '@/components/QueryView';
import { useLeaveFlow } from '@/components/useLeaveFlow';
import { useRouteParam } from '@/components/useRouteParam';
import { useSingleFlight } from '@/components/useSingleFlight';
import { AppButton } from '@/design-system/AppButton';
import { Notice } from '@/design-system/Notice';
import { useLocalization } from '@/i18n/useLocalization';

import { useConfirmRoster } from './householdMutations';
import { rosterIssues } from './rosterRules';
import { RosterSummary } from './RosterSummary';
import { useRosterData } from './useRosterData';

/** The server's answers that mean the roster is incomplete (D-50): the person goes back to review it. */
const MISSING_INFORMATION = new Set([
  'households.roster_has_no_head',
  'households.roster_missing_relationship',
  'households.roster_missing_employment_status',
]);

/**
 * Confirms the roster with the server, which then decides from approved rules which questionnaires each person is
 * asked to answer. The app sends no eligibility of its own and shows the server's answer on the next screen. A
 * failed confirmation can be retried as often as needed: the server never assigns the same questionnaire twice.
 */
export function ConfirmRoster() {
  const { t } = useLocalization();
  const { i18n } = useTranslation();
  const router = useRouter();
  const leaveFlow = useLeaveFlow();
  const householdId = useRouteParam('householdId') ?? '';
  const roster = useRosterData(householdId);
  const confirm = useConfirmRoster(householdId);
  const once = useSingleFlight();
  const confirmed = useRef(false);
  const review = () => router.replace({ pathname: '/household/[householdId]/review', params: { householdId } });
  const problem = confirm.error ? describeProblem(confirm.error, (key) => i18n.exists(key)) : null;
  const missingInformation = problem?.code ? MISSING_INFORMATION.has(problem.code) : false;
  const ready =
    roster.data !== undefined && roster.data.household.status === 'active' && rosterIssues(roster.data.members).length === 0;

  const submit = () =>
    once(async () => {
      if (confirmed.current) {
        return;
      }
      try {
        await confirm.mutateAsync();
      } catch {
        return; // Shown below with what to do next.
      }
      confirmed.current = true;
      // The set-up, roster and review screens under this one are finished with: Back from the assignments goes Home.
      leaveFlow({ pathname: '/household/[householdId]/assignments', params: { householdId } });
    });

  return (
    <AppScreen
      title={t('household.confirm.title')}
      footer={
        ready ? (
          <>
            {problem && missingInformation ? (
              <Notice
                tone="warning"
                title={t('household.confirm.missingTitle')}
                message={t(problem.messageKey)}
                action={{ label: t('household.confirm.backToReview'), onPress: review }}
                announce
              />
            ) : null}
            {confirm.error && !missingInformation ? (
              <ProblemState error={confirm.error} variant="inline" onRetry={() => void submit()} />
            ) : null}
            <AppButton label={t('household.confirm.action')} loading={confirm.isPending} onPress={() => void submit()} />
          </>
        ) : undefined
      }
    >
      <QueryView query={roster} loadingLabel={t('members.loading')}>
        {(data) =>
          data.household.status !== 'active' ? (
            <Notice tone="warning" title={t('household.withdrawn.title')} message={t('household.withdrawn.message')} />
          ) : rosterIssues(data.members).length > 0 ? (
            <Notice
              tone="warning"
              title={t('household.confirm.missingTitle')}
              message={t('household.review.issuesMessage')}
              action={{ label: t('household.confirm.backToReview'), onPress: review }}
            />
          ) : (
            <>
              <RosterSummary members={data.members} />
              <Notice tone="info" title={t('household.confirm.decidesTitle')} message={t('household.confirm.decides')} />
            </>
          )
        }
      </QueryView>
    </AppScreen>
  );
}
