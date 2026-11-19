import { useRouter } from 'expo-router';

import { useMe } from '@/api/participant/queries';
import { AppScreen } from '@/components/AppScreen';
import { QueryView } from '@/components/QueryView';
import { Notice } from '@/design-system/Notice';
import { useLocalization } from '@/i18n/useLocalization';

import { AlreadySetUp } from './HouseholdStart';
import { useCreateHousehold } from './householdMutations';
import { emptyMemberForm } from './memberForm';
import { MemberFormScreen } from './MemberFormScreen';

/**
 * Household creation, step two: the household's details start with the person setting it up, who becomes its
 * manager (and so must be 18 or over). Saving creates the household on the server; the rest of the roster is added
 * next, one person at a time. Retrying after a lost answer never creates a second household.
 */
export function AboutYouForm() {
  const { t } = useLocalization();
  const router = useRouter();
  const me = useMe();
  const create = useCreateHousehold();

  if (me.data?.household === null) {
    return (
      <MemberFormScreen
        title={t('household.aboutYou.title')}
        intro={t('household.aboutYou.intro')}
        notice={<Notice tone="info" message={t('household.start.privacy')} />}
        initialValues={emptyMemberForm()}
        isSelf
        others={[]}
        submitLabel={t('household.aboutYou.submit')}
        save={create}
        onSaved={({ householdId, outcome }) =>
          router.replace({
            pathname: '/household/[householdId]/members',
            params: { householdId, notice: outcome },
          })
        }
      />
    );
  }

  return (
    <AppScreen title={t('household.aboutYou.title')}>
      <QueryView query={me} loadingLabel={t('household.start.checking')}>
        {(data) => (data.household ? <AlreadySetUp household={data.household} /> : null)}
      </QueryView>
    </AppScreen>
  );
}
