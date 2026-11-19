import { ApiError, isApiError, isRetryable } from '@/api/client';
import type { ParticipantApi } from '@/api/participant/participantApi';
import type { Member, MemberDetails, MemberPatch } from '@/api/participant/schemas';

import { memberPatch, sameDetails } from './memberForm';
import { activeMembers } from './rosterRules';

/*
 * The household requests behind the enrollment screens, written so that trying again never repeats a change. Creating
 * a household and adding a member are not idempotent on the server, so after a failure that may have reached it
 * (no connection, a timeout, a server fault) the next attempt first looks at what the server now holds. Updates are
 * merge patches computed against the server's current record, so repeating one changes nothing more. Every rule is
 * still the server's; these functions only order the requests and read the answers.
 */

/** A failed request that may still have been carried out: the connection dropped or the server failed after it. */
export function mayHaveReachedServer(error: unknown): boolean {
  return isRetryable(error);
}

export interface CreatedHousehold {
  householdId: string;
  /** `already_set_up`: the account already had a household before this request (one per account). */
  outcome: 'created' | 'already_set_up';
}

/**
 * Creates the caller's household with the caller as its first member and `others` (usually nobody: the roster is
 * added next, one person at a time) in the same request. After an uncertain attempt, the caller's account is checked
 * first, so a household created by the lost request is used rather than a second one requested. The server also
 * refuses a second household per account (`households.already_member`); that answer leads to the existing household.
 */
export async function createHousehold(
  api: ParticipantApi,
  self: MemberDetails,
  afterUncertainAttempt: boolean,
  others: readonly MemberDetails[] = [],
): Promise<CreatedHousehold> {
  if (afterUncertainAttempt) {
    const me = await api.getMe();
    if (me.household) {
      return { householdId: me.household.householdId, outcome: 'created' };
    }
  }
  try {
    const household = await api.createHousehold({
      members: [{ ...self, isSelf: true }, ...others.map((member) => ({ ...member, isSelf: false }))],
    });
    return { householdId: household.id, outcome: 'created' };
  } catch (error) {
    if (isApiError(error) && error.code === 'households.already_member') {
      const me = await api.getMe();
      if (me.household) {
        return { householdId: me.household.householdId, outcome: 'already_set_up' };
      }
    }
    throw error;
  }
}

export interface RoleRelease {
  memberId: string;
  patch: MemberPatch;
}

/**
 * The patches that free the head-of-household and main-earner roles from whoever else holds them, so the member
 * `memberId` (null for a new member) can take them. The server allows one active holder of each role, so the old
 * holder is cleared first. The two roles are independent: they may be one person or two.
 */
export function roleReleases(
  roster: readonly Member[],
  wants: Pick<MemberDetails, 'isHeadOfHousehold' | 'isPrimaryIncomeEarner'>,
  memberId: string | null,
): RoleRelease[] {
  const releases: RoleRelease[] = [];
  for (const member of activeMembers(roster)) {
    if (member.id === memberId) {
      continue;
    }
    const patch: MemberPatch = {};
    if (wants.isHeadOfHousehold && member.isHeadOfHousehold) {
      patch.isHeadOfHousehold = false;
    }
    if (wants.isPrimaryIncomeEarner && member.isPrimaryIncomeEarner) {
      patch.isPrimaryIncomeEarner = false;
    }
    if (Object.keys(patch).length > 0) {
      releases.push({ memberId: member.id, patch });
    }
  }
  return releases;
}

/** The patch that gives back what a release took (every role it cleared). */
function restoring(release: RoleRelease): MemberPatch {
  return Object.fromEntries(Object.keys(release.patch).map((field) => [field, true]));
}

/**
 * Moves roles to `memberId` (null for a new member): frees them from their current holders, then runs `change`, which
 * gives them to the member. The server allows one holder of each role, so these are separate commits. If any step
 * fails, the roles taken so far are given back, newest first, so a failed move does not leave the household without
 * its head or main earner. Giving back is best effort: if `change` did reach the server the role is taken and the
 * server refuses the give-back, which is right; if the connection is down the roster shows what is missing, and
 * retrying completes the move.
 */
async function moveRoles<T>(
  api: ParticipantApi,
  householdId: string,
  roster: readonly Member[],
  details: MemberDetails,
  memberId: string | null,
  change: () => Promise<T>,
): Promise<T> {
  const released: RoleRelease[] = [];
  try {
    for (const release of roleReleases(roster, details, memberId)) {
      await api.updateMember(householdId, release.memberId, release.patch);
      released.push(release);
    }
    return await change();
  } catch (error) {
    for (const release of released.reverse()) {
      await api.updateMember(householdId, release.memberId, restoring(release)).catch(() => undefined);
    }
    throw error;
  }
}

/** Brings an existing member to `details`, moving roles to them first. Nothing is sent when nothing differs. */
export async function updateMember(
  api: ParticipantApi,
  householdId: string,
  roster: readonly Member[],
  member: Member,
  details: MemberDetails,
): Promise<Member> {
  const patch = memberPatch(member, details);
  if (Object.keys(patch).length === 0) {
    return member;
  }
  return moveRoles(api, householdId, roster, details, member.id, () =>
    api.updateMember(householdId, member.id, patch),
  );
}

/** What an add attempt that may have reached the server knew: the roster before it and the details it sent. */
export interface UncertainAdd {
  baseline: ReadonlySet<string>;
  details: MemberDetails;
}

/**
 * The member an uncertain add created, if it did: an active member who was not on the roster before the first
 * attempt. Only the household's manager changes its roster, so one new member is the lost request's; with several,
 * the one with the details that were sent.
 */
export function findUncertainAdd(roster: readonly Member[], pending: UncertainAdd): Member | null {
  const added = activeMembers(roster).filter((member) => !pending.baseline.has(member.id));
  if (added.length <= 1) {
    return added[0] ?? null;
  }
  return added.find((member) => sameDetails(member, pending.details)) ?? null;
}

/**
 * Adds a member to the roster `roster` (read just before), moving roles to them first. When an earlier uncertain
 * attempt turns out to have created the member, that member is brought to `details` instead of adding another.
 */
export async function addMember(
  api: ParticipantApi,
  householdId: string,
  roster: readonly Member[],
  details: MemberDetails,
  pending: UncertainAdd | null,
): Promise<Member> {
  const created = pending ? findUncertainAdd(roster, pending) : null;
  if (created) {
    return updateMember(api, householdId, roster, created, details);
  }
  return moveRoles(api, householdId, roster, details, null, () => api.addMember(householdId, details));
}

/** The active member `memberId` on a roster just read, or the server's not-found answer. */
export function requireActiveMember(roster: readonly Member[], memberId: string): Member {
  const member = activeMembers(roster).find((candidate) => candidate.id === memberId);
  if (!member) {
    throw new ApiError('not_found', 404, 'households.member_not_found');
  }
  return member;
}

/**
 * Takes a member off the roster. After an uncertain attempt, "not found" means the earlier request removed them, so
 * it counts as done.
 */
export async function removeMember(
  api: ParticipantApi,
  householdId: string,
  memberId: string,
  afterUncertainAttempt: boolean,
): Promise<void> {
  try {
    await api.removeMember(householdId, memberId);
  } catch (error) {
    if (afterUncertainAttempt && isApiError(error) && error.code === 'households.member_not_found') {
      return;
    }
    throw error;
  }
}
