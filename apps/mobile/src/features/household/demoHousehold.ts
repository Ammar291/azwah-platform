import type { EmploymentStatus, MemberDetails, Relationship } from '@/api/participant/schemas';

/*
 * DEMONSTRATIONS ONLY (D-131). A made-up family for the "Auto-fill a demo family" button, which exists only in
 * development builds. Nobody here is a real person; like every household on the platform it has no names or identity
 * numbers, only the roster details. Ages are fixed and birth dates follow from today, so the family looks the same at
 * every demonstration. It is not eligibility or research content: the server checks it as it checks any roster, and
 * decides the questionnaires when the roster is confirmed.
 */

interface DemoPerson {
  age: number;
  birthMonth: number;
  relationshipToHousehold: Relationship | null;
  employmentStatus: EmploymentStatus | null;
}

/** The person setting it up first: head of household and main earner. Children under 18 have no work status. */
const SELF: DemoPerson = { age: 42, birthMonth: 4, relationshipToHousehold: null, employmentStatus: 'employed' };
const OTHERS: readonly DemoPerson[] = [
  { age: 39, birthMonth: 9, relationshipToHousehold: 'spouse', employmentStatus: 'self_employed' },
  { age: 19, birthMonth: 2, relationshipToHousehold: 'child', employmentStatus: 'student' },
  { age: 15, birthMonth: 11, relationshipToHousehold: 'child', employmentStatus: null },
  { age: 10, birthMonth: 6, relationshipToHousehold: 'child', employmentStatus: null },
  { age: 70, birthMonth: 1, relationshipToHousehold: 'parent', employmentStatus: 'retired' },
];

/** The birth year that makes someone born in `birthMonth` exactly `age` on `now`, counted as the server counts. */
function birthYearFor(age: number, birthMonth: number, now: Date): number {
  const birthdayStillToCome = now.getUTCMonth() + 1 < birthMonth;
  return now.getUTCFullYear() - age - (birthdayStillToCome ? 1 : 0);
}

function details(person: DemoPerson, now: Date, isHead: boolean): MemberDetails {
  return {
    birthYear: birthYearFor(person.age, person.birthMonth, now),
    birthMonth: person.birthMonth,
    relationshipToHousehold: person.relationshipToHousehold,
    employmentStatus: person.employmentStatus,
    isHeadOfHousehold: isHead,
    isPrimaryIncomeEarner: isHead,
  };
}

/** The demo family on `now`: the caller (`self`) and the five people they live with. */
export function demoHousehold(now: Date): { self: MemberDetails; others: MemberDetails[] } {
  return {
    self: details(SELF, now, true),
    others: OTHERS.map((person) => details(person, now, false)),
  };
}
