import { useRouter } from 'expo-router';
import { StyleSheet, View } from 'react-native';

import { useAssignments, useMe, useMyConsents, useRecommendations } from '@/api/participant/queries';
import type { Me, Recommendation } from '@/api/participant/schemas';
import { useAuth } from '@/auth/AuthContext';
import { givenName } from '@/auth/identityProfile';
import { ProblemState } from '@/components/ProblemState';
import { QueryView } from '@/components/QueryView';
import { AppText } from '@/design-system/AppText';
import { ListGroup, ListRow } from '@/design-system/ListGroup';
import { LoadingState } from '@/design-system/LoadingState';
import { Section } from '@/design-system/Section';
import { spacing } from '@/design-system/tokens';
import { useLocalization } from '@/i18n/useLocalization';

import { FamilyPathCard } from './FamilyPathCard';
import { buildFamilyPath, type FamilyPathAction } from './familyPath';
import { NextSteps } from './NextSteps';
import { useLatestResult, type LatestResult } from './useLatestResult';

/** The signed-in hub: the family's path through Azwah, the support routed to it, and links to everything else. */
export function HomeDashboard() {
  const { t } = useLocalization();
  const me = useMe();
  return (
    <QueryView query={me} loadingLabel={t('home.loading')}>
      {(data) => <Dashboard me={data} />}
    </QueryView>
  );
}

/** Morning until noon, afternoon until six, evening after, on the phone's clock. */
export function partOfDay(date: Date): 'morning' | 'afternoon' | 'evening' {
  const hour = date.getHours();
  return hour < 12 ? 'morning' : hour < 18 ? 'afternoon' : 'evening';
}

function Dashboard({ me }: { me: Me }) {
  const { t, language } = useLocalization();
  const router = useRouter();
  const { state } = useAuth();
  const household = me.household;
  const name = givenName(state.status === 'signed_in' ? state.session.profile : null, language);
  const part = partOfDay(new Date());
  const recommendations = useRecommendations(true);
  const latest = useLatestResult(household !== null);

  return (
    <>
      <View style={styles.greeting}>
        <AppText variant="title" accessibilityRole="header">
          {name ? t(`home.greetings.${part}Named`, { name }) : t(`home.greetings.${part}`)}
        </AppText>
      </View>

      <Path household={household} recommendations={recommendations.data ?? []} latest={latest} />
      <NextSteps recommendations={recommendations} report={latest.report} onOpenResult={latest.open} />

      <Section title={t('home.more')}>
        <ListGroup>
          {household ? (
            <ListRow
              title={t('home.household.open')}
              description={t('household.reference', { reference: household.reference })}
              onPress={() =>
                router.push({ pathname: '/household/[householdId]', params: { householdId: household.householdId } })
              }
            />
          ) : null}
          <ListRow title={t('history.title')} onPress={() => router.push('/history')} />
          <ListRow title={t('help.title')} onPress={() => router.push('/help')} />
        </ListGroup>
      </Section>
    </>
  );
}

interface PathProps {
  household: Me['household'];
  recommendations: readonly Recommendation[];
  latest: LatestResult;
}

/** The path waits for consents and assignments once there is a household; before that it needs neither. */
function Path({ household, recommendations, latest }: PathProps) {
  const { t } = useLocalization();
  const router = useRouter();
  const consents = useMyConsents();
  const assignments = useAssignments();

  const onAction = (action: FamilyPathAction) => {
    switch (action.kind) {
      case 'createHousehold':
        return router.push('/household/create');
      case 'confirmHousehold':
        return router.push({ pathname: '/household/[householdId]', params: { householdId: action.householdId } });
      case 'giveConsent':
        return router.push('/consent');
      case 'openAssignment':
        return router.push({ pathname: '/assessments/[assignmentId]', params: { assignmentId: action.assignmentId } });
      case 'openResult':
        return router.push({ pathname: '/sessions/[sessionId]/result', params: { sessionId: action.sessionId } });
    }
  };

  if (household) {
    const error = consents.error ?? assignments.error;
    if (error) {
      return (
        <ProblemState
          error={error}
          variant="inline"
          onRetry={() => {
            void consents.refetch();
            void assignments.refetch();
          }}
        />
      );
    }
    if (!consents.data || !assignments.data) {
      return <LoadingState label={t('home.path.loading')} />;
    }
  }

  const steps = buildFamilyPath({
    household,
    consents: household ? (consents.data ?? []) : [],
    assignments: household ? (assignments.data ?? []) : [],
    recommendations,
    latestResultSessionId: latest.sessionId,
    hasReportSteps: (latest.report?.nextSteps.length ?? 0) > 0,
  });
  return <FamilyPathCard steps={steps} onAction={onAction} onSeeAllAssignments={() => router.push('/assessments')} />;
}

const styles = StyleSheet.create({
  greeting: { gap: spacing.xs },
});
