import { StyleSheet, View } from 'react-native';

import type { Member } from '@/api/participant/schemas';
import { AppButton } from '@/design-system/AppButton';
import { AppCard } from '@/design-system/AppCard';
import { AppText } from '@/design-system/AppText';
import { Badge } from '@/design-system/Badge';
import { ButtonRow } from '@/design-system/ButtonRow';
import { spacing } from '@/design-system/tokens';
import { useLocalization } from '@/i18n/useLocalization';

import { memberBirth, memberDescription, memberTitle } from './memberLabels';
import { memberGaps } from './rosterRules';

export interface MemberCardProps {
  member: Member;
  /** Shown when the roster can be changed. */
  onEdit?: () => void;
  /** Shown when this member can be removed (never the person who manages the household). */
  onRemove?: () => void;
}

/**
 * One person on the roster. The facts are read as one element; each role is a badge in words, so who is the head of
 * household and who is the main earner is clear whether they are one person or two. What is still needed before
 * confirming is shown on the card.
 */
export function MemberCard({ member, onEdit, onRemove }: MemberCardProps) {
  const localization = useLocalization();
  const { t, formatNumber } = localization;
  const active = member.status === 'active';
  const gaps = active ? memberGaps(member) : [];
  const roles = [
    member.isHeadOfHousehold ? t('members.head') : null,
    member.isPrimaryIncomeEarner ? t('members.primaryEarner') : null,
  ].filter((role): role is string => role !== null);
  const born = t('members.bornAndAge', { date: memberBirth(member, localization.formatDate), age: formatNumber(member.age) });
  const work = member.employmentStatus
    ? t(`employmentStatus.${member.employmentStatus}`)
    : t('members.employmentNotRecorded');
  const description = memberDescription(member, localization);

  return (
    <AppCard variant={active ? 'default' : 'muted'} testID={`member-${member.id}`}>
      <View
        accessible
        accessibilityLabel={[memberTitle(member, t), born, work, ...roles, ...gaps.map((gap) => t(`members.gaps.${gap}`))].join(
          t('common.listSeparator'),
        )}
        style={styles.facts}
      >
        <AppText variant="bodyStrong">{memberTitle(member, t)}</AppText>
        <AppText tone="secondary">{born}</AppText>
        <AppText tone="secondary">{work}</AppText>
        <View style={styles.badges}>
          {member.isHeadOfHousehold ? <Badge tone="info" label={t('members.head')} /> : null}
          {member.isPrimaryIncomeEarner ? <Badge tone="info" label={t('members.primaryEarner')} /> : null}
          {member.isCaller ? <Badge label={t('members.managesHousehold')} /> : null}
          {member.hasAccount && !member.isCaller ? <Badge label={t('members.ownAccount')} /> : null}
          {gaps.map((gap) => (
            <Badge key={gap} tone="warning" label={t(`members.gaps.${gap}`)} />
          ))}
          {active ? null : <Badge tone="neutral" label={t('members.inactive')} />}
        </View>
      </View>
      {onEdit || onRemove ? (
        <ButtonRow>
          {onEdit ? (
            <AppButton
              label={t('members.edit')}
              accessibilityLabel={t('members.editLabel', { member: description })}
              variant="secondary"
              onPress={onEdit}
            />
          ) : null}
          {onRemove ? (
            <AppButton
              label={t('members.remove')}
              accessibilityLabel={t('members.removeLabel', { member: description })}
              variant="tertiary"
              onPress={onRemove}
            />
          ) : null}
        </ButtonRow>
      ) : null}
    </AppCard>
  );
}

const styles = StyleSheet.create({
  facts: { gap: spacing.xs },
  badges: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm, marginTop: spacing.xs },
});
