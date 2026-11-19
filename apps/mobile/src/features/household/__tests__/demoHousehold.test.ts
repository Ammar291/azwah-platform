import { fixtureApi, NEW_PARTICIPANT_SUBJECT } from '@/test-utils/householdWorlds';

import { demoHousehold } from '../demoHousehold';
import { createHousehold } from '../rosterRequests';
import { ADULT_AGE, ageOn, MAX_ACTIVE_MEMBERS, rosterIssues } from '../rosterRules';

const TODAY = new Date('2026-11-09T10:00:00Z');

/** Mid-year, the last moment of a year and the first of the next, and a leap day. */
const DAYS = [
  TODAY,
  new Date('2026-12-31T23:59:59Z'),
  new Date('2027-01-01T00:00:00Z'),
  new Date('2028-02-29T12:00:00Z'),
];

describe('the demo household', () => {
  it.each(DAYS)('is the same family of six on %s: you, a spouse, three children and a parent', (now) => {
    const { self, others } = demoHousehold(now);

    expect([self, ...others].map((member) => ageOn(member.birthYear, member.birthMonth, now))).toEqual([
      42, 39, 19, 15, 10, 70,
    ]);
    expect(others.map((member) => member.relationshipToHousehold)).toEqual([
      'spouse',
      'child',
      'child',
      'child',
      'parent',
    ]);
    expect(others.length + 1).toBeLessThanOrEqual(MAX_ACTIVE_MEMBERS);
  });

  it('makes the person setting it up its adult head of household and main earner, and nobody else', () => {
    const now = TODAY;
    const { self, others } = demoHousehold(now);

    expect(self).toMatchObject({
      relationshipToHousehold: null,
      employmentStatus: 'employed',
      isHeadOfHousehold: true,
      isPrimaryIncomeEarner: true,
    });
    expect(ageOn(self.birthYear, self.birthMonth, now)).toBeGreaterThanOrEqual(ADULT_AGE);
    expect(others.some((member) => member.isHeadOfHousehold || member.isPrimaryIncomeEarner)).toBe(false);
  });

  it('gives every adult a work status and no child one', () => {
    const now = TODAY;
    const { others } = demoHousehold(now);

    for (const member of others) {
      const adult = ageOn(member.birthYear, member.birthMonth, now) >= ADULT_AGE;
      expect(member.employmentStatus === null).toBe(!adult);
    }
  });

  it('is accepted as it is and can be confirmed without changes', async () => {
    const { api } = fixtureApi(NEW_PARTICIPANT_SUBJECT);
    const { self, others } = demoHousehold(new Date());

    const created = await createHousehold(api, self, false, others);
    const members = await api.listMembers(created.householdId);

    expect(members).toHaveLength(6);
    expect(members.filter((member) => member.isCaller)).toHaveLength(1);
    expect(rosterIssues(members)).toEqual([]);
    await expect(api.confirmRoster(created.householdId)).resolves.toMatchObject({ householdId: created.householdId });
  });
});
