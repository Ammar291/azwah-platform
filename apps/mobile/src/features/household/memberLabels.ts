import type { Member } from '@/api/participant/schemas';
import type { Localization } from '@/i18n/useLocalization';

/** A short name for a member without storing names: "You", "Head of household" or their relationship. */
export function memberTitle(member: Member, t: Localization['t']): string {
  if (member.isCaller) return t('members.you');
  if (member.isHeadOfHousehold) return t('members.head');
  return member.relationshipToHousehold
    ? t(`relationship.${member.relationshipToHousehold}`)
    : t('members.relationshipNotRecorded');
}

/** The member's birth month and year, for display ("May 2014"). */
export function memberBirth(member: Member, formatDate: Localization['formatDate']): string {
  return formatDate(new Date(Date.UTC(member.birthYear, member.birthMonth - 1, 15)), 'monthYear');
}

/**
 * Tells members apart in button names and dialogs, where two children would otherwise both be "Son or daughter":
 * "Son or daughter, born May 2014".
 */
export function memberDescription(member: Member, { t, formatDate }: Pick<Localization, 't' | 'formatDate'>): string {
  return t('members.describe', { member: memberTitle(member, t), date: memberBirth(member, formatDate) });
}

/**
 * Names the member who holds a role, in summaries and messages: "You", "Spouse, born September 1987", or "Born
 * March 1960" for a head of household (who has no relationship to describe them).
 */
export function memberReference(member: Member, localization: Pick<Localization, 't' | 'formatDate'>): string {
  if (member.isCaller) return localization.t('members.you');
  if (member.relationshipToHousehold) return memberDescription(member, localization);
  return localization.t('members.born', { date: memberBirth(member, localization.formatDate) });
}
