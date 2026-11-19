import type { ParticipantApi } from '@/api/participant/participantApi';
import { createFixtureParticipantApi } from '@/dev-fixtures/fixtureParticipantApi';

import { withFaults } from './faultyApi';
import { PLACEHOLDER_CATALOG, PLACEHOLDER_VERSION } from './placeholderContent';

/** The placeholder's section A: four required questions, each with four options and one outside the scale. */
export const SECTION_A = PLACEHOLDER_VERSION.sections.find((section) => section.code === 'A')?.items ?? [];

/**
 * The household lead's fixture world: a session in section A with the first two questions answered, a section B
 * assignment not started, and an earlier scored session. The API is wrapped so a test can script failures.
 */
export async function leadSession() {
  // A clock that moves a second per reading, so two saves never share a `lastModifiedAt` (conflicts compare them).
  let tick = 0;
  const now = () => new Date(Date.UTC(2026, 9, 15, 9, 0, 0) + 1000 * tick++);
  const faults = withFaults(
    createFixtureParticipantApi({
      getSubject: () => 'dev-mock|household_lead',
      latencyMs: 0,
      now,
      catalog: PLACEHOLDER_CATALOG,
    }),
  );
  const assignments = await faults.api.listAssignments();
  const inProgress = assignments.find((assignment) => assignment.status === 'in_progress');
  const notStarted = assignments.find((assignment) => assignment.status === 'assigned');
  return {
    ...faults,
    sessionId: inProgress?.activeSessionId ?? '',
    assignmentId: inProgress?.id ?? '',
    notStartedId: notStarted?.id ?? '',
  };
}

/** Every question of section A answered on the server, with each question's first option. */
export const answerEverything = (api: ParticipantApi, sessionId: string) =>
  api.saveAnswers(
    sessionId,
    SECTION_A.map((item) => ({ itemId: item.id, optionId: item.options[0]?.id ?? '' })),
  );

/** One option's id: question `item` (0-based) of section A, option `option` (0-based). */
export const optionOf = (item: number, option: number) => SECTION_A[item]?.options[option]?.id ?? '';
