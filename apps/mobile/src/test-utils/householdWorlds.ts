import type { Member } from '@/api/participant/schemas';
import { createFixtureParticipantApi } from '@/dev-fixtures/fixtureParticipantApi';

import { withFaults } from './faultyApi';
import { PLACEHOLDER_CATALOG } from './placeholderContent';

/** Synthetic people only. */
export const LEAD_SUBJECT = 'dev-mock|household_lead';
export const NEW_PARTICIPANT_SUBJECT = 'dev-mock|new_participant';

/** A fixture API for one persona serving the test placeholder, with scriptable faults and no artificial delay. */
export function fixtureApi(subject: string = LEAD_SUBJECT) {
  return withFaults(createFixtureParticipantApi({ getSubject: () => subject, latencyMs: 0, catalog: PLACEHOLDER_CATALOG }));
}

/**
 * The household lead's world: a confirmed household of three (the lead is head of household and main earner, a
 * spouse and a child). `householdUnconfirmed` makes a harmless change so the roster needs confirming again.
 */
export async function leadHousehold(options: { householdUnconfirmed?: boolean } = {}) {
  const faults = fixtureApi(LEAD_SUBJECT);
  const me = await faults.api.getMe();
  const householdId = me.household?.householdId ?? '';
  let members = await faults.api.listMembers(householdId);
  const find = (predicate: (member: Member) => boolean) => {
    const member = members.find(predicate);
    if (!member) throw new Error('The household lead fixture changed.');
    return member;
  };
  const spouse = find((member) => member.relationshipToHousehold === 'spouse');
  if (options.householdUnconfirmed) {
    await faults.api.updateMember(householdId, spouse.id, { employmentStatus: 'employed' });
    members = await faults.api.listMembers(householdId);
  }
  faults.calls.length = 0;
  return {
    ...faults,
    householdId,
    self: find((member) => member.isCaller),
    spouse: find((member) => member.relationshipToHousehold === 'spouse'),
    child: find((member) => member.relationshipToHousehold === 'child'),
  };
}

/** A member as the API returns one, for unit tests of pure functions. */
export function memberFixture(overrides: Partial<Member> = {}): Member {
  return {
    id: '0199e0a1-0000-7000-8000-00000000aaaa',
    birthYear: 1985,
    birthMonth: 4,
    age: 41,
    relationshipToHousehold: 'spouse',
    employmentStatus: 'employed',
    isHeadOfHousehold: false,
    isPrimaryIncomeEarner: false,
    status: 'active',
    hasAccount: false,
    isCaller: false,
    createdAt: '2026-10-14T09:00:00Z',
    updatedAt: '2026-10-14T09:00:00Z',
    ...overrides,
  };
}
