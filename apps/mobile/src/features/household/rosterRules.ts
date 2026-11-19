import type { Member } from '@/api/participant/schemas';

/*
 * The approved roster rules (D-50, D-58), used only to guide people before they send anything: which fields to show,
 * which roles a member may take and what still stops the roster being confirmed. They decide nothing. The server
 * checks every rule again and its answer, as a translated problem code, always wins. Nothing here is eligibility:
 * which questionnaires anyone answers is decided by the server when the roster is confirmed.
 */

/** Only members aged 18 or over may head the household, be its main earner or manage it (D-58). */
export const ADULT_AGE = 18;

/** The most active members a household may have (`Household.MaxActiveMembers`). */
export const MAX_ACTIVE_MEMBERS = 30;

/** The earliest birth year the server accepts (`YearMonth.MinYear`). */
export const MIN_BIRTH_YEAR = 1900;

/** Age in whole years on `today` from a year and month of birth, counted as the server does (`YearMonth.AgeOn`). */
export function ageOn(birthYear: number, birthMonth: number, today: Date): number {
  return today.getUTCFullYear() - birthYear - (today.getUTCMonth() + 1 < birthMonth ? 1 : 0);
}

export function activeMembers(members: readonly Member[]): Member[] {
  return members.filter((member) => member.status === 'active');
}

/** Who currently holds each household role, among active members. Either may be nobody; they may be one person. */
export function roleHolders(members: readonly Member[]): { head: Member | null; primaryEarner: Member | null } {
  const active = activeMembers(members);
  return {
    head: active.find((member) => member.isHeadOfHousehold) ?? null,
    primaryEarner: active.find((member) => member.isPrimaryIncomeEarner) ?? null,
  };
}

export type MemberGap = 'missing_relationship' | 'missing_employment';

/** What a member's record still needs before the roster can be confirmed. Uses the age the server computed. */
export function memberGaps(member: Member): MemberGap[] {
  const gaps: MemberGap[] = [];
  if (!member.isHeadOfHousehold && member.relationshipToHousehold === null) {
    gaps.push('missing_relationship');
  }
  if (member.age >= ADULT_AGE && member.employmentStatus === null) {
    gaps.push('missing_employment');
  }
  return gaps;
}

export type RosterIssue = { kind: 'no_head' } | { kind: MemberGap; member: Member };

/**
 * What stops the roster being confirmed: no active head of household, or a member missing a relationship or (for
 * an adult) a work status. A household without a main earner is allowed.
 */
export function rosterIssues(members: readonly Member[]): RosterIssue[] {
  const active = activeMembers(members);
  const issues: RosterIssue[] = active.some((member) => member.isHeadOfHousehold) ? [] : [{ kind: 'no_head' }];
  for (const member of active) {
    for (const gap of memberGaps(member)) {
      issues.push({ kind: gap, member });
    }
  }
  return issues;
}
