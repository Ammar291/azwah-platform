import { useRouter } from 'expo-router';
import { StyleSheet, View } from 'react-native';

import { AppScreen } from '@/components/AppScreen';
import { QueryView } from '@/components/QueryView';
import { useRouteParam } from '@/components/useRouteParam';
import { AppButton } from '@/design-system/AppButton';
import { AppCard } from '@/design-system/AppCard';
import { AppText } from '@/design-system/AppText';
import { Badge } from '@/design-system/Badge';
import { Notice } from '@/design-system/Notice';
import { ListGroup, ListRow } from '@/design-system/ListGroup';
import { Section } from '@/design-system/Section';
import { spacing } from '@/design-system/tokens';
import { useLocalization } from '@/i18n/useLocalization';

import { RosterSummary } from './RosterSummary';
import { useRosterData, type RosterData } from './useRosterData';

/**
 * The household profile: its reference and status, whether its details are confirmed, who holds each role, the next
 * step, and the way to the roster and to the questionnaires the server assigned.
 */
export function HouseholdProfile() {
  const { t } = useLocalization();
  const householdId = useRouteParam('householdId') ?? '';
  const roster = useRosterData(householdId);
  return (
    <AppScreen title={t('household.title')}>
      <QueryView query={roster} loadingLabel={t('household.loading')}>
        {(data) => <Profile householdId={householdId} {...data} />}
      </QueryView>
    </AppScreen>
  );
}

function Profile({ householdId, household, members }: RosterData & { householdId: string }) {
  const { t, formatDate } = useLocalization();
  const router = useRouter();
  const active = household.status === 'active';
  const go = (pathname: '/household/[householdId]/members' | '/household/[householdId]/review' | '/household/[householdId]/assignments') =>
    router.push({ pathname, params: { householdId } });

  return (
    <>
      <AppCard>
        <AppText variant="heading" accessibilityRole="header">
          {t('household.reference', { reference: household.reference })}
        </AppText>
        <View style={styles.badges}>
          <Badge tone={active ? 'success' : 'neutral'} label={t(`householdStatus.${household.status}`)} />
          <Badge
            tone={household.rosterConfirmedAt ? 'success' : 'warning'}
            label={t(household.rosterConfirmedAt ? 'household.confirmed' : 'household.notConfirmed')}
          />
        </View>
        {household.rosterConfirmedAt ? (
          <AppText variant="caption" tone="muted">
            {t('household.confirmedOn', { date: formatDate(household.rosterConfirmedAt) })}
          </AppText>
        ) : null}
        <AppText variant="caption" tone="muted">
          {t('household.createdOn', { date: formatDate(household.createdAt) })}
        </AppText>
      </AppCard>

      {active ? null : (
        <Notice tone="warning" title={t('household.withdrawn.title')} message={t('household.withdrawn.message')} />
      )}

      {active && !household.rosterConfirmedAt ? (
        <AppCard variant="highlight">
          <AppText variant="bodyStrong">{t('household.nextStep.confirmTitle')}</AppText>
          <AppButton label={t('household.nextStep.confirmAction')} onPress={() => go('/household/[householdId]/review')} />
        </AppCard>
      ) : null}
      {active && household.rosterConfirmedAt ? (
        <AppButton
          label={t('household.nextStep.confirmAgain')}
          variant="secondary"
          onPress={() => go('/household/[householdId]/review')}
        />
      ) : null}

      <Section title={t('household.summary.title')}>
        <RosterSummary members={members} />
      </Section>

      <Section title={t('household.links.title')}>
        <ListGroup>
          <ListRow title={t('household.links.people')} onPress={() => go('/household/[householdId]/members')} />
          <ListRow title={t('household.links.assignments')} onPress={() => go('/household/[householdId]/assignments')} />
        </ListGroup>
      </Section>
    </>
  );
}

const styles = StyleSheet.create({
  badges: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm },
});
