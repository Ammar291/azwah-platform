import { useRouter } from 'expo-router';
import { useState } from 'react';

import { useConsentPolicies, useMe, useMembers, useMyConsents } from '@/api/participant/queries';
import type { ConsentType, Me } from '@/api/participant/schemas';
import { ProblemState } from '@/components/ProblemState';
import { EmptyState } from '@/design-system/EmptyState';
import { LoadingState } from '@/design-system/LoadingState';
import { RadioCardGroup } from '@/design-system/RadioCardGroup';
import { memberTitle } from '@/features/household/memberLabels';
import { useLocalization } from '@/i18n/useLocalization';

import { ConsentPolicyCard } from './ConsentPolicyCard';

const ORDER: ConsentType[] = ['participation', 'research_use', 'referral'];

/**
 * Consent for each purpose, for the person signed in or, for a household manager, a relative without an account
 * of their own (D-49). Which members can be chosen comes from the server (`actableMemberIds`).
 */
export function ConsentPanel() {
  const { t } = useLocalization();
  const router = useRouter();
  const me = useMe();

  if (me.isPending) {
    return <LoadingState label={t('consent.loading')} />;
  }
  if (me.error) {
    return <ProblemState error={me.error} onRetry={() => void me.refetch()} />;
  }
  const household = me.data.household;
  if (!household) {
    return (
      <EmptyState
        title={t('consent.noHousehold.title')}
        message={t('consent.noHousehold.message')}
        action={{ label: t('home.household.create'), onPress: () => router.replace('/household/create') }}
      />
    );
  }
  return <ConsentForHousehold household={household} />;
}

function ConsentForHousehold({ household }: { household: NonNullable<Me['household']> }) {
  const { t } = useLocalization();
  const [memberId, setMemberId] = useState(household.memberId);
  const policies = useConsentPolicies();
  const consents = useMyConsents();
  const canChoose = household.role === 'manager' && household.actableMemberIds.length > 1;

  const failed = policies.error ?? consents.error;
  if (failed) {
    return (
      <ProblemState
        error={failed}
        onRetry={() => {
          void policies.refetch();
          void consents.refetch();
        }}
      />
    );
  }
  if (!policies.data || !consents.data) {
    return <LoadingState label={t('consent.loading')} />;
  }

  return (
    <>
      {canChoose ? (
        <MemberChooser
          householdId={household.householdId}
          actableMemberIds={household.actableMemberIds}
          value={memberId}
          onChange={setMemberId}
        />
      ) : null}
      {policies.data.length === 0 ? (
        <EmptyState title={t('consent.none.title')} message={t('consent.none.message')} />
      ) : (
        [...policies.data]
          .sort((a, b) => ORDER.indexOf(a.consentType) - ORDER.indexOf(b.consentType))
          .map((policy) => (
            <ConsentPolicyCard
              key={`${policy.id}-${memberId}`}
              policy={policy}
              memberId={memberId}
              active={consents.data.find(
                (consent) =>
                  consent.memberId === memberId && consent.consentType === policy.consentType && consent.isActive,
              )}
            />
          ))
      )}
    </>
  );
}

function MemberChooser({
  householdId,
  actableMemberIds,
  value,
  onChange,
}: {
  householdId: string;
  actableMemberIds: readonly string[];
  value: string;
  onChange: (memberId: string) => void;
}) {
  const { t } = useLocalization();
  const members = useMembers(householdId);
  if (!members.data) {
    return null;
  }
  const options = members.data
    .filter((member) => actableMemberIds.includes(member.id))
    .map((member) => ({
      value: member.id,
      label: memberTitle(member, t),
      description: t('members.bornIn', { year: String(member.birthYear) }),
    }));
  return (
    <RadioCardGroup
      label={t('consent.forWhom')}
      density="compact"
      options={options}
      value={value}
      onChange={onChange}
    />
  );
}
