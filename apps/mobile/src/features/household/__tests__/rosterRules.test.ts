import { memberFixture } from '@/test-utils/householdWorlds';

import { ageOn, memberGaps, roleHolders, rosterIssues } from '../rosterRules';

const NOW = new Date('2026-10-14T00:00:00Z');

const head = memberFixture({ id: 'head', isHeadOfHousehold: true, relationshipToHousehold: null, isCaller: true });
const spouse = memberFixture({ id: 'spouse' });
const child = memberFixture({ id: 'child', age: 12, relationshipToHousehold: 'child', employmentStatus: null });

describe('roster rules (guidance only; the server decides)', () => {
  it('counts age in whole years by month, as the server does', () => {
    expect(ageOn(1990, 6, NOW)).toBe(36);
    expect(ageOn(2008, 10, NOW)).toBe(18);
    expect(ageOn(2008, 11, NOW)).toBe(17);
  });

  it('finds nothing missing in a complete roster', () => {
    expect(rosterIssues([head, spouse, child])).toEqual([]);
  });

  it('needs a head of household', () => {
    expect(rosterIssues([spouse, child])).toEqual([{ kind: 'no_head' }]);
  });

  it('needs a relationship for everyone but the head, and a work status for adults only', () => {
    const noRelationship = { ...spouse, relationshipToHousehold: null };
    const noWork = { ...spouse, id: 'other', employmentStatus: null };

    expect(rosterIssues([head, noRelationship, noWork, child])).toEqual([
      { kind: 'missing_relationship', member: noRelationship },
      { kind: 'missing_employment', member: noWork },
    ]);
    expect(memberGaps(child)).toEqual([]);
    expect(memberGaps(head)).toEqual([]);
  });

  it('ignores members who are no longer in the household', () => {
    const removedHead = { ...head, status: 'inactive' as const };
    const removedIncomplete = { ...spouse, status: 'inactive' as const, employmentStatus: null };

    expect(rosterIssues([removedHead, removedIncomplete, child])).toEqual([{ kind: 'no_head' }]);
  });

  it('keeps the head of household and the main earner as separate roles', () => {
    const earner = { ...spouse, isPrimaryIncomeEarner: true };

    expect(roleHolders([head, earner, child])).toEqual({ head, primaryEarner: earner });
    expect(roleHolders([spouse, child])).toEqual({ head: null, primaryEarner: null });
  });
});
