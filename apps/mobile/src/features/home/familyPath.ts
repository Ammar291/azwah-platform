import type { Assignment, ConsentRecord, Me, Recommendation } from '@/api/participant/schemas';

export type FamilyPathStepId = 'household' | 'consent' | 'checkIn' | 'nextSteps' | 'nextCheckIn';
export type FamilyPathStepState = 'done' | 'current' | 'upcoming';

/** What the current step's button does. Every target is a screen the app already has. */
export type FamilyPathAction =
  | { kind: 'createHousehold' }
  | { kind: 'confirmHousehold'; householdId: string }
  | { kind: 'giveConsent' }
  | { kind: 'openAssignment'; assignmentId: string; started: boolean }
  | { kind: 'openResult'; sessionId: string };

export interface FamilyPathStep {
  id: FamilyPathStepId;
  state: FamilyPathStepState;
  /** Only on the current step, and only when there is something to do now. */
  action?: FamilyPathAction;
  /** The check-in step only: how many questionnaires of the family survey are open. */
  openCount?: number;
  /**
   * The check-in step only: how many questionnaires the family has at all, open or not. The way to them stays on the
   * path whatever the step's state: a roster edit makes the household step current again, but they are still there.
   */
  assignmentCount?: number;
}

export interface FamilyPathInput {
  household: Me['household'];
  consents: readonly ConsentRecord[];
  assignments: readonly Assignment[];
  recommendations: readonly Recommendation[];
  /** The family survey's latest recorded result, which holds the family's written next steps (D-114, D-146). */
  latestResultSessionId?: string | null;
  /** That result's report, with its next steps, is on this phone. */
  hasReportSteps?: boolean;
}

const ORDER: readonly FamilyPathStepId[] = ['household', 'consent', 'checkIn', 'nextSteps', 'nextCheckIn'];

const isOpen = (assignment: Assignment) => assignment.status === 'assigned' || assignment.status === 'in_progress';

/**
 * The family survey's questionnaires (D-105, D-146): the household assessment, whose result the family's next steps and
 * trend come from. A family-voices module open beside it stays under "All questionnaires" and does not hold the path back.
 * A family given only modules (no household assessment) counts all of them.
 */
export function surveyAssignments(assignments: readonly Assignment[]): readonly Assignment[] {
  const survey = assignments.filter((assignment) => assignment.instrumentKind === 'household_assessment');
  return survey.length > 0 ? survey : assignments;
}

/** The family survey is finished for now: none of it is open and at least one questionnaire of it was completed. */
export function isCheckInDone(assignments: readonly Assignment[]): boolean {
  const survey = surveyAssignments(assignments);
  return !survey.some(isOpen) && survey.some((assignment) => assignment.status === 'completed');
}

/**
 * Where the family is on its way through Azwah, from what the server says: the household and its confirmation, the
 * member's participation consent, the assignments, the support the server routed and the latest recorded result. Display
 * only: the server decides every status, assignment, result and recommendation; this orders them. The first step not
 * done is current; the next check-in is never done, so there is always exactly one current step.
 */
export function buildFamilyPath({
  household,
  consents,
  assignments,
  recommendations,
  latestResultSessionId = null,
  hasReportSteps = false,
}: FamilyPathInput): FamilyPathStep[] {
  const open = surveyAssignments(assignments).filter(isOpen);
  const started = open.find((assignment) => assignment.status === 'in_progress');
  const next = started ?? open[0];

  const done: Record<FamilyPathStepId, boolean> = {
    household: household?.rosterConfirmedAt != null,
    consent:
      household != null &&
      consents.some(
        (record) => record.memberId === household.memberId && record.consentType === 'participation' && record.isActive,
      ),
    checkIn: isCheckInDone(assignments),
    nextSteps: recommendations.length > 0 || hasReportSteps,
    nextCheckIn: false,
  };

  const actions: Partial<Record<FamilyPathStepId, FamilyPathAction>> = {
    household: household
      ? { kind: 'confirmHousehold', householdId: household.householdId }
      : { kind: 'createHousehold' },
    consent: { kind: 'giveConsent' },
    checkIn: next ? { kind: 'openAssignment', assignmentId: next.id, started: next === started } : undefined,
    nextSteps: latestResultSessionId ? { kind: 'openResult', sessionId: latestResultSessionId } : undefined,
  };

  const current = ORDER.find((id) => !done[id]);
  return ORDER.map((id): FamilyPathStep => {
    const state: FamilyPathStepState = done[id] ? 'done' : id === current ? 'current' : 'upcoming';
    return {
      id,
      state,
      ...(state === 'current' && actions[id] ? { action: actions[id] } : {}),
      ...(id === 'checkIn' ? { openCount: open.length, assignmentCount: assignments.length } : {}),
    };
  });
}
