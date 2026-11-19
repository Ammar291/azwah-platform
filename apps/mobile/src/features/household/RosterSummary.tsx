import type { Member } from '@/api/participant/schemas';
import { AppCard } from '@/design-system/AppCard';
import { DetailRow } from '@/design-system/DetailRow';
import { useLocalization } from '@/i18n/useLocalization';

import { memberReference } from './memberLabels';
import { activeMembers, roleHolders } from './rosterRules';

/**
 * How many people are listed and who holds each role. The head of household and the main earner are shown on
 * separate lines even when they are the same person, so neither is assumed from the other.
 */
export function RosterSummary({ members }: { members: readonly Member[] }) {
  const localization = useLocalization();
  const { t, formatNumber } = localization;
  const { head, primaryEarner } = roleHolders(members);
  return (
    <AppCard variant="muted" testID="roster-summary">
      <DetailRow
        layout="inline"
        label={t('household.summary.people')}
        value={formatNumber(activeMembers(members).length)}
      />
      <DetailRow
        label={t('household.summary.head')}
        value={head ? memberReference(head, localization) : t('household.summary.notChosen')}
      />
      <DetailRow
        label={t('household.summary.primaryEarner')}
        value={primaryEarner ? memberReference(primaryEarner, localization) : t('household.summary.notChosen')}
      />
    </AppCard>
  );
}
