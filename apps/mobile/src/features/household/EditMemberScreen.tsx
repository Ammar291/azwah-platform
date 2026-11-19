import { useRouter } from 'expo-router';
import { useState } from 'react';

import type { Member } from '@/api/participant/schemas';
import { AppScreen } from '@/components/AppScreen';
import { ProblemState } from '@/components/ProblemState';
import { QueryView } from '@/components/QueryView';
import { useRouteParam } from '@/components/useRouteParam';
import { useSingleFlight } from '@/components/useSingleFlight';
import { AppButton } from '@/design-system/AppButton';
import { ConfirmationDialog } from '@/design-system/ConfirmationDialog';
import { EmptyState } from '@/design-system/EmptyState';
import { useLocalization } from '@/i18n/useLocalization';

import { useRemoveMember, useUpdateMember } from './householdMutations';
import { memberDescription } from './memberLabels';
import { memberFormValues } from './memberForm';
import { MemberFormScreen } from './MemberFormScreen';
import { activeMembers } from './rosterRules';
import { useRosterData, type RosterData } from './useRosterData';

/**
 * Changes one member's details, sending only what changed (a merge patch), or removes them after a confirmation.
 * The person who manages the household cannot be removed.
 */
export function EditMemberScreen() {
  const { t } = useLocalization();
  const householdId = useRouteParam('householdId') ?? '';
  const memberId = useRouteParam('memberId') ?? '';
  const roster = useRosterData(householdId);

  if (roster.data) {
    return <EditMember householdId={householdId} memberId={memberId} {...roster.data} />;
  }
  return (
    <AppScreen title={t('members.editScreen.title')}>
      <QueryView query={roster} loadingLabel={t('members.loading')}>
        {() => null}
      </QueryView>
    </AppScreen>
  );
}

function EditMember({ householdId, memberId, household, members }: RosterData & { householdId: string; memberId: string }) {
  const { t } = useLocalization();
  const router = useRouter();
  const update = useUpdateMember(householdId, memberId);
  const active = activeMembers(members);
  const member = active.find((candidate) => candidate.id === memberId);

  if (!member || household.status !== 'active') {
    return (
      <AppScreen title={t('members.editScreen.title')}>
        <EmptyState
          title={t(member ? 'household.withdrawn.title' : 'members.editScreen.notFoundTitle')}
          message={t(member ? 'household.withdrawn.message' : 'members.editScreen.notFoundMessage')}
          action={{ label: t('members.editScreen.backToRoster'), onPress: () => router.back() }}
        />
      </AppScreen>
    );
  }

  return (
    <MemberFormScreen
      title={t('members.editScreen.title')}
      initialValues={memberFormValues(member)}
      isSelf={member.isCaller}
      others={active.filter((other) => other.id !== member.id)}
      submitLabel={t('members.editScreen.submit')}
      save={update}
      onSaved={() => router.back()}
    >
      {({ allowLeave }) =>
        member.isCaller ? null : (
          <RemoveMember
            householdId={householdId}
            member={member}
            onRemoved={() => {
              allowLeave();
              router.back();
            }}
          />
        )
      }
    </MemberFormScreen>
  );
}

function RemoveMember({ householdId, member, onRemoved }: { householdId: string; member: Member; onRemoved: () => void }) {
  const localization = useLocalization();
  const { t } = localization;
  const remove = useRemoveMember(householdId);
  const once = useSingleFlight();
  const [asking, setAsking] = useState(false);

  const confirm = () =>
    once(async () => {
      try {
        await remove.mutateAsync(member.id);
      } catch {
        setAsking(false);
        return;
      }
      setAsking(false);
      onRemoved();
    });

  return (
    <>
      {remove.error ? <ProblemState error={remove.error} variant="inline" onRetry={() => void confirm()} /> : null}
      <AppButton
        label={t('members.editScreen.remove')}
        variant="tertiary"
        onPress={() => {
          remove.reset();
          setAsking(true);
        }}
      />
      <ConfirmationDialog
        visible={asking}
        destructive
        title={t('members.removeDialog.title')}
        message={t('members.removeDialog.message', { member: memberDescription(member, localization) })}
        confirmLabel={t('members.removeDialog.confirm')}
        cancelLabel={t('common.cancel')}
        busy={remove.isPending}
        onConfirm={() => void confirm()}
        onCancel={() => setAsking(false)}
      />
    </>
  );
}
