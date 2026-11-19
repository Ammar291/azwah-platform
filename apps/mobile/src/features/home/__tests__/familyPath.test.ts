import type { Assignment, ConsentRecord, Me, Recommendation } from '@/api/participant/schemas';

import { buildFamilyPath, isCheckInDone, type FamilyPathInput } from '../familyPath';

const HOUSEHOLD_ID = '0192f3a0-0000-7000-8000-000000000001';
const MEMBER_ID = '0192f3a0-0000-7000-8000-000000000002';
const AT = '2026-10-28T08:00:00Z';

function household(confirmed: boolean): NonNullable<Me['household']> {
  return {
    householdId: HOUSEHOLD_ID,
    reference: 'DEV-4821',
    status: 'active',
    role: 'manager',
    memberId: MEMBER_ID,
    rosterConfirmedAt: confirmed ? AT : null,
    actableMemberIds: [MEMBER_ID],
  };
}

function consent(overrides: Partial<ConsentRecord> = {}): ConsentRecord {
  return {
    id: '0192f3a0-0000-7000-8000-000000000010',
    memberId: MEMBER_ID,
    consentType: 'participation',
    policyVersionId: '0192f3a0-0000-7000-8000-000000000011',
    policyVersionNumber: 1,
    captureMethod: 'in_app',
    grantedAt: AT,
    withdrawnAt: null,
    isActive: true,
    ...overrides,
  };
}

function assignment(id: string, status: Assignment['status'], kind: Assignment['instrumentKind'] = 'household_assessment'): Assignment {
  return {
    id,
    memberId: MEMBER_ID,
    instrumentCode: kind === 'household_assessment' ? 'household_assessment' : 'family_voices',
    instrumentName: { ar: null, en: 'Household assessment' },
    versionNumber: 1,
    sectionCode: kind === 'household_assessment' ? null : 'A',
    sectionTitle: null,
    waveId: '0192f3a0-0000-7000-8000-000000000020',
    waveCode: 'W1',
    status,
    assignedAt: AT,
    expiresAt: null,
    completedAt: status === 'completed' ? AT : null,
    activeSessionId: null,
    instrumentKind: kind,
  };
}

const recommendation: Recommendation = {
  id: '0192f3a0-0000-7000-8000-000000000030',
  memberId: MEMBER_ID,
  sessionId: '0192f3a0-0000-7000-8000-000000000031',
  categoryCode: 'family_counselling',
  createdAt: AT,
  referralId: null,
  referralStatus: null,
};

const ASSIGNED = '0192f3a0-0000-7000-8000-000000000040';
const STARTED = '0192f3a0-0000-7000-8000-000000000041';
const DONE = '0192f3a0-0000-7000-8000-000000000042';

function input(overrides: Partial<FamilyPathInput> = {}): FamilyPathInput {
  return {
    household: household(true),
    consents: [consent()],
    assignments: [assignment(ASSIGNED, 'assigned')],
    recommendations: [],
    ...overrides,
  };
}

const states = (path: ReturnType<typeof buildFamilyPath>) => path.map((step) => [step.id, step.state]);

describe('buildFamilyPath', () => {
  it('lists the five steps in order and starts a new participant at the household', () => {
    const path = buildFamilyPath(input({ household: null, consents: [], assignments: [] }));

    expect(states(path)).toEqual([
      ['household', 'current'],
      ['consent', 'upcoming'],
      ['checkIn', 'upcoming'],
      ['nextSteps', 'upcoming'],
      ['nextCheckIn', 'upcoming'],
    ]);
    expect(path[0]?.action).toEqual({ kind: 'createHousehold' });
  });

  it('asks a household that is not confirmed yet to check and confirm it', () => {
    const path = buildFamilyPath(input({ household: household(false), consents: [], assignments: [] }));

    expect(path[0]).toMatchObject({
      state: 'current',
      action: { kind: 'confirmHousehold', householdId: HOUSEHOLD_ID },
    });
  });

  it('asks for consent once the household is confirmed', () => {
    const path = buildFamilyPath(input({ consents: [] }));

    expect(states(path).slice(0, 3)).toEqual([
      ['household', 'done'],
      ['consent', 'current'],
      ['checkIn', 'upcoming'],
    ]);
    expect(path[1]?.action).toEqual({ kind: 'giveConsent' });
  });

  it.each([
    ['withdrawn', consent({ isActive: false, withdrawnAt: AT })],
    ['for research only', consent({ consentType: 'research_use' })],
    ['for another member', consent({ memberId: '0192f3a0-0000-7000-8000-000000000099' })],
  ])('does not count consent %s', (_, record) => {
    expect(buildFamilyPath(input({ consents: [record] }))[1]?.state).toBe('current');
  });

  it('continues a check-in already started before one not started yet', () => {
    const path = buildFamilyPath(
      input({ assignments: [assignment(ASSIGNED, 'assigned'), assignment(STARTED, 'in_progress')] }),
    );

    expect(path[2]).toMatchObject({
      state: 'current',
      action: { kind: 'openAssignment', assignmentId: STARTED, started: true },
      openCount: 2,
    });
  });

  it('starts the only open check-in', () => {
    expect(buildFamilyPath(input())[2]).toMatchObject({
      state: 'current',
      action: { kind: 'openAssignment', assignmentId: ASSIGNED, started: false },
      openCount: 1,
    });
  });

  it('waits at the check-in while none is open and none was completed', () => {
    const path = buildFamilyPath(input({ assignments: [assignment(DONE, 'expired')] }));

    expect(path[2]).toMatchObject({ state: 'current', openCount: 0 });
    expect(path[2]?.action).toBeUndefined();
    expect(path[3]?.state).toBe('upcoming');
  });

  it('moves on to the next steps once the check-in is completed', () => {
    const path = buildFamilyPath(input({ assignments: [assignment(DONE, 'completed')] }));

    expect(states(path).slice(2)).toEqual([
      ['checkIn', 'done'],
      ['nextSteps', 'current'],
      ['nextCheckIn', 'upcoming'],
    ]);
  });

  it('marks the next steps seen when the server has routed support, and points to the next check-in', () => {
    const path = buildFamilyPath(
      input({ assignments: [assignment(DONE, 'completed')], recommendations: [recommendation] }),
    );

    expect(states(path).slice(3)).toEqual([
      ['nextSteps', 'done'],
      ['nextCheckIn', 'current'],
    ]);
  });

  it('opens a new check-in again in a later wave', () => {
    const path = buildFamilyPath(
      input({
        assignments: [assignment(DONE, 'completed'), assignment(ASSIGNED, 'assigned')],
        recommendations: [recommendation],
      }),
    );

    expect(states(path)).toEqual([
      ['household', 'done'],
      ['consent', 'done'],
      ['checkIn', 'current'],
      ['nextSteps', 'done'],
      ['nextCheckIn', 'upcoming'],
    ]);
  });

  it('counts every questionnaire the family has on the survey step, whatever its state', () => {
    expect(buildFamilyPath(input({ household: null, consents: [], assignments: [] }))[2]?.assignmentCount).toBe(0);
    // A roster edit makes the household step current again; the family's questionnaires are still there.
    const edited = buildFamilyPath(
      input({ household: household(false), assignments: [assignment(DONE, 'completed'), assignment(ASSIGNED, 'assigned')] }),
    );
    expect(edited[2]).toMatchObject({ state: 'upcoming', assignmentCount: 2, openCount: 1 });
    expect(edited[2]?.action).toBeUndefined();
    expect(buildFamilyPath(input({ assignments: [assignment(DONE, 'completed')] }))[2]).toMatchObject({
      state: 'done',
      assignmentCount: 1,
    });
  });

  it('knows when the family survey is finished: nothing open and something completed', () => {
    expect(isCheckInDone([assignment(DONE, 'completed')])).toBe(true);
    expect(isCheckInDone([assignment(DONE, 'completed'), assignment(ASSIGNED, 'assigned')])).toBe(false);
    expect(isCheckInDone([assignment(DONE, 'expired')])).toBe(false);
    expect(isCheckInDone([])).toBe(false);
  });

  it('counts the household assessment as the family survey, so a module still open does not hold the path back (D-146)', () => {
    const MODULE = '0192f3a0-0000-7000-8000-000000000043';
    const finished = [assignment(DONE, 'completed'), assignment(MODULE, 'assigned', 'family_voices')];

    expect(isCheckInDone(finished)).toBe(true);
    const path = buildFamilyPath(input({ assignments: finished }));
    expect(states(path).slice(2)).toEqual([
      ['checkIn', 'done'],
      ['nextSteps', 'current'],
      ['nextCheckIn', 'upcoming'],
    ]);
    // The module stays one press away under all questionnaires.
    expect(path[2]).toMatchObject({ assignmentCount: 2 });
    // A family that has only modules counts them all, as before.
    expect(isCheckInDone([assignment(DONE, 'completed', 'family_voices'), assignment(MODULE, 'assigned', 'family_voices')])).toBe(false);
  });

  it('opens the latest result for the next steps, and counts them seen once its report holds them (D-146)', () => {
    const SESSION = '0192f3a0-0000-7000-8000-000000000050';
    const done = [assignment(DONE, 'completed')];

    expect(buildFamilyPath(input({ assignments: done, latestResultSessionId: SESSION }))[3]).toMatchObject({
      state: 'current',
      action: { kind: 'openResult', sessionId: SESSION },
    });
    expect(buildFamilyPath(input({ assignments: done }))[3]?.action).toBeUndefined();
    expect(
      states(buildFamilyPath(input({ assignments: done, latestResultSessionId: SESSION, hasReportSteps: true }))).slice(3),
    ).toEqual([
      ['nextSteps', 'done'],
      ['nextCheckIn', 'current'],
    ]);
  });

  it('never marks more than one step current', () => {
    const inputs = [
      input({ household: null, consents: [], assignments: [] }),
      input({ consents: [] }),
      input(),
      input({ assignments: [assignment(DONE, 'completed')], recommendations: [recommendation] }),
    ];
    for (const value of inputs) {
      expect(buildFamilyPath(value).filter((step) => step.state === 'current')).toHaveLength(1);
    }
  });
});
