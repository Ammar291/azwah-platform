import { useRouter } from 'expo-router';
import { useState } from 'react';

import type { Household, Member } from '@/api/participant/schemas';
import { AppScreen } from '@/components/AppScreen';
import { ProblemState } from '@/components/ProblemState';
import { QueryView } from '@/components/QueryView';
import { useRouteParam } from '@/components/useRouteParam';
import { useSingleFlight } from '@/components/useSingleFlight';
import { AppButton } from '@/design-system/AppButton';
import { ConfirmationDialog } from '@/design-system/ConfirmationDialog';
import { Notice } from '@/design-system/Notice';
import { Section } from '@/design-system/Section';
import { useLocalization } from '@/i18n/useLocalization';

import { useRemoveMember } from './householdMutations';
import { MemberCard } from './MemberCard';
import { memberDescription } from './memberLabels';
import { activeMembers, MAX_ACTIVE_MEMBERS } from './rosterRules';
import { RosterSummary } from './RosterSummary';
import { useRosterData } from './useRosterData';

/**
 * The member roster: everyone in the household as cards, with adding, editing and removing (after a confirmation)
 * while the household takes part. Removed members stay listed apart, without actions; their history is kept.
 */
export function RosterScreen() {
  const { t } = useLocalization();
  const router = useRouter();
  const householdId = useRouteParam('householdId') ?? '';
  const notice = useRouteParam('notice');
  const roster = useRosterData(householdId);
  const editable = roster.data?.household.status === 'active';

  return (
    <AppScreen
      title={t('members.title')}
      footer={
        editable ? (
          <AppButton
            label={t('members.reviewAction')}
            trailingIcon="chevron-forward"
            onPress={() => router.push({ pathname: '/household/[householdId]/review', params: { householdId } })}
          />
        ) : undefined
      }
    >
      {notice === 'created' ? (
        <Notice
          tone="success"
          title={t('members.notices.created.title')}
          message={t('members.notices.created.message')}
          announce
        />
      ) : null}
      {notice === 'demo_created' ? (
        <Notice
          tone="success"
          title={t('members.notices.created.title')}
          message={t('members.notices.demoCreated.message')}
          announce
        />
      ) : null}
      {notice === 'already_set_up' ? (
        <Notice
          tone="info"
          title={t('members.notices.alreadySetUp.title')}
          message={t('members.notices.alreadySetUp.message')}
          announce
        />
      ) : null}
      <QueryView query={roster} loadingLabel={t('members.loading')}>
        {(data) => <Roster householdId={householdId} household={data.household} members={data.members} />}
      </QueryView>
    </AppScreen>
  );
}

function Roster({ householdId, household, members }: { householdId: string; household: Household; members: Member[] }) {
  const localization = useLocalization();
  const { t, formatNumber } = localization;
  const router = useRouter();
  const remove = useRemoveMember(householdId);
  const once = useSingleFlight();
  const [asking, setAsking] = useState<Member | null>(null);
  const [failed, setFailed] = useState<Member | null>(null);
  const [removed, setRemoved] = useState<Member | null>(null);
  const editable = household.status === 'active';
  const active = activeMembers(members);
  const inactive = members.filter((member) => member.status !== 'active');

  const removeMember = (member: Member) =>
    once(async () => {
      setRemoved(null);
      try {
        await remove.mutateAsync(member.id);
        setFailed(null);
        setRemoved(member);
      } catch {
        setFailed(member);
      } finally {
        setAsking(null);
      }
    });

  return (
    <>
      {editable ? null : (
        <Notice tone="warning" title={t('household.withdrawn.title')} message={t('household.withdrawn.message')} />
      )}
      {removed ? (
        <Notice
          tone="success"
          message={t('members.removedNotice', { member: memberDescription(removed, localization) })}
          announce
        />
      ) : null}
      {failed && remove.error ? (
        <ProblemState error={remove.error} variant="inline" onRetry={() => void removeMember(failed)} />
      ) : null}
      <RosterSummary members={members} />
      <Section title={t('members.listTitle')}>
        {active.map((member) => (
          <MemberCard
            key={member.id}
            member={member}
            onEdit={
              editable
                ? () =>
                    router.push({
                      pathname: '/household/[householdId]/members/[memberId]',
                      params: { householdId, memberId: member.id },
                    })
                : undefined
            }
            onRemove={editable && !member.isCaller ? () => setAsking(member) : undefined}
          />
        ))}
      </Section>
      {editable && active.length >= MAX_ACTIVE_MEMBERS ? (
        <Notice tone="info" message={t('members.add.full', { max: formatNumber(MAX_ACTIVE_MEMBERS) })} />
      ) : null}
      {editable && active.length < MAX_ACTIVE_MEMBERS ? (
        <AppButton
          label={t('members.add.action')}
          variant="secondary"
          onPress={() => router.push({ pathname: '/household/[householdId]/members/add', params: { householdId } })}
        />
      ) : null}
      {inactive.length > 0 ? (
        <Section title={t('members.removedTitle')}>
          {inactive.map((member) => (
            <MemberCard key={member.id} member={member} />
          ))}
        </Section>
      ) : null}
      <ConfirmationDialog
        visible={asking !== null}
        destructive
        title={t('members.removeDialog.title')}
        message={t('members.removeDialog.message', {
          member: asking ? memberDescription(asking, localization) : '',
        })}
        confirmLabel={t('members.removeDialog.confirm')}
        cancelLabel={t('common.cancel')}
        busy={remove.isPending}
        onConfirm={() => {
          if (asking) {
            void removeMember(asking);
          }
        }}
        onCancel={() => setAsking(null)}
      />
    </>
  );
}
