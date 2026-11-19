import { useRouter } from 'expo-router';
import { useState } from 'react';
import { StyleSheet, View } from 'react-native';

import { AppScreen } from '@/components/AppScreen';
import { QueryView } from '@/components/QueryView';
import { useRouteParam } from '@/components/useRouteParam';
import { AppButton } from '@/design-system/AppButton';
import { AppCard } from '@/design-system/AppCard';
import { AppText } from '@/design-system/AppText';
import { Notice } from '@/design-system/Notice';
import { Section } from '@/design-system/Section';
import { SwitchField } from '@/design-system/SwitchField';
import { spacing } from '@/design-system/tokens';
import { useLocalization } from '@/i18n/useLocalization';

import { MemberCard } from './MemberCard';
import { memberReference } from './memberLabels';
import { activeMembers, rosterIssues, type RosterIssue } from './rosterRules';
import { RosterSummary } from './RosterSummary';
import { useRosterData, type RosterData } from './useRosterData';

/**
 * Review before confirming: the roster as it will be sent, what still stops it being confirmed (with a way to fix
 * each), and the person's own check that nobody is missing. Continuing is possible only when nothing is missing. A
 * confirmed household can confirm again: the server clears a confirmation only when the roster changes, and
 * confirming is how a household asks for a newly opened round's questionnaires.
 */
export function ReviewRoster() {
  const { t } = useLocalization();
  const router = useRouter();
  const householdId = useRouteParam('householdId') ?? '';
  const roster = useRosterData(householdId);
  const [everyoneListed, setEveryoneListed] = useState(false);
  const data = roster.data;
  const ready = data !== undefined && data.household.status === 'active' && rosterIssues(data.members).length === 0;

  return (
    <AppScreen
      title={t('household.review.title')}
      footer={
        ready ? (
          <>
            <SwitchField
              label={t('household.review.everyoneListed')}
              value={everyoneListed}
              onValueChange={setEveryoneListed}
            />
            <AppButton
              label={t('household.review.continue')}
              trailingIcon="chevron-forward"
              disabled={!everyoneListed}
              onPress={() => router.push({ pathname: '/household/[householdId]/confirm', params: { householdId } })}
            />
          </>
        ) : undefined
      }
    >
      <QueryView query={roster} loadingLabel={t('members.loading')}>
        {(loaded) => <Review householdId={householdId} {...loaded} />}
      </QueryView>
    </AppScreen>
  );
}

function Review({ householdId, household, members }: RosterData & { householdId: string }) {
  const { t, formatDate } = useLocalization();
  const router = useRouter();
  const issues = rosterIssues(members);
  const edit = (memberId: string) =>
    router.push({ pathname: '/household/[householdId]/members/[memberId]', params: { householdId, memberId } });

  return (
    <>
      {household.status !== 'active' ? (
        <Notice tone="warning" title={t('household.withdrawn.title')} message={t('household.withdrawn.message')} />
      ) : null}
      {household.rosterConfirmedAt ? (
        <Notice
          tone="success"
          title={t('household.review.confirmedTitle')}
          message={t('household.confirmedOn', { date: formatDate(household.rosterConfirmedAt) })}
          action={{
            label: t('household.links.assignments'),
            onPress: () => router.push({ pathname: '/household/[householdId]/assignments', params: { householdId } }),
          }}
        />
      ) : null}
      <AppText>{t(household.rosterConfirmedAt ? 'household.review.confirmAgain' : 'household.review.intro')}</AppText>
      <RosterSummary members={members} />
      {issues.length > 0 ? <Issues issues={issues} householdId={householdId} onFix={edit} /> : null}
      <Section title={t('household.review.peopleTitle')}>
        {activeMembers(members).map((member) => (
          <MemberCard key={member.id} member={member} onEdit={household.status === 'active' ? () => edit(member.id) : undefined} />
        ))}
      </Section>
      {household.status === 'active' ? (
        <AppButton
          label={t('household.review.addMissing')}
          variant="secondary"
          onPress={() => router.push({ pathname: '/household/[householdId]/members/add', params: { householdId } })}
        />
      ) : null}
    </>
  );
}

function Issues({
  issues,
  householdId,
  onFix,
}: {
  issues: RosterIssue[];
  householdId: string;
  onFix: (memberId: string) => void;
}) {
  const localization = useLocalization();
  const { t } = localization;
  const router = useRouter();
  return (
    <AppCard variant="muted" testID="roster-issues">
      <Notice tone="warning" title={t('household.review.issuesTitle')} message={t('household.review.issuesMessage')} />
      {issues.map((issue) => {
        const text =
          issue.kind === 'no_head'
            ? t('household.review.issues.no_head')
            : t(`household.review.issues.${issue.kind}`, { member: memberReference(issue.member, localization) });
        return (
          <View key={issue.kind === 'no_head' ? issue.kind : `${issue.kind}-${issue.member.id}`} style={styles.issue}>
            <AppText>{text}</AppText>
            <AppButton
              label={t('household.review.fix')}
              accessibilityLabel={t('household.review.fixLabel', { issue: text })}
              variant="secondary"
              onPress={() =>
                issue.kind === 'no_head'
                  ? router.push({ pathname: '/household/[householdId]/members', params: { householdId } })
                  : onFix(issue.member.id)
              }
            />
          </View>
        );
      })}
    </AppCard>
  );
}

const styles = StyleSheet.create({
  issue: { gap: spacing.sm },
});
