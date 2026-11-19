import { useRouter } from 'expo-router';

import { AppScreen } from '@/components/AppScreen';
import { QueryView } from '@/components/QueryView';
import { useRouteParam } from '@/components/useRouteParam';
import { Notice } from '@/design-system/Notice';
import { useLocalization } from '@/i18n/useLocalization';

import { useAddMember } from './householdMutations';
import { emptyMemberForm } from './memberForm';
import { MemberFormScreen } from './MemberFormScreen';
import { activeMembers, MAX_ACTIVE_MEMBERS } from './rosterRules';
import { useRosterData, type RosterData } from './useRosterData';

/** Adds one person to the roster. Any roster change clears the household's confirmation on the server (D-50). */
export function AddMemberScreen() {
  const { t } = useLocalization();
  const householdId = useRouteParam('householdId') ?? '';
  const roster = useRosterData(householdId);

  if (roster.data) {
    return <AddMember householdId={householdId} {...roster.data} />;
  }
  return (
    <AppScreen title={t('members.add.title')}>
      <QueryView query={roster} loadingLabel={t('members.loading')}>
        {() => null}
      </QueryView>
    </AppScreen>
  );
}

function AddMember({ householdId, household, members }: RosterData & { householdId: string }) {
  const { t, formatNumber } = useLocalization();
  const router = useRouter();
  const add = useAddMember(householdId);
  const active = activeMembers(members);

  if (household.status !== 'active' || active.length >= MAX_ACTIVE_MEMBERS) {
    return (
      <AppScreen title={t('members.add.title')}>
        {household.status === 'active' ? (
          <Notice tone="info" message={t('members.add.full', { max: formatNumber(MAX_ACTIVE_MEMBERS) })} />
        ) : (
          <Notice tone="warning" title={t('household.withdrawn.title')} message={t('household.withdrawn.message')} />
        )}
      </AppScreen>
    );
  }

  return (
    <MemberFormScreen
      title={t('members.add.title')}
      intro={t('members.add.intro')}
      initialValues={emptyMemberForm()}
      isSelf={false}
      others={active}
      submitLabel={t('members.add.submit')}
      save={add}
      onSaved={() => router.back()}
    />
  );
}
