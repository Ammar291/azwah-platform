import { z } from 'zod';

import { ApiError } from '@/api/client';
import type { DemonstrationData } from '@/api/participant/demonstrationData';
import { PRIVACY_REQUEST_PAGE_SIZE, type ParticipantApi } from '@/api/participant/participantApi';
import {
  answerSchema,
  assignmentSchema,
  consentRecordSchema,
  householdSchema,
  memberSchema,
  privacyRequestSchema,
  sessionResultSchema,
  sessionSchema,
} from '@/api/participant/schemas';
import type {
  AnswerRequest,
  Assignment,
  ConsentPolicy,
  ConsentRecord,
  ConsentType,
  HistoryItem,
  Household,
  Me,
  Member,
  MemberDetails,
  MyDataExport,
  MyDataExportDownload,
  PrivacyRequest,
  ResilienceTrend,
  Session,
  SessionResult,
} from '@/api/participant/schemas';
import type { FieldCipher } from '@/storage/protection/fieldCipher';
import type { SettingsRepository } from '@/storage/settings';

import { fixtureVersion, itemsOf, PLATFORM_CATALOG, type FixtureCatalog, type FixtureVersion } from './content';
import {
  FIXTURE_SUPPORT_AUTHORITIES,
  FIXTURE_SUPPORT_RESOURCES,
  searchFixtureResources,
  simulatedAssistantAnswer,
  simulatedSafetyNotice,
  simulatedScoreReport,
  simulatedScoreReportPart,
  simulatedScoreReportSummary,
} from './fixtureAssistant';
import { personaForSubject, type DevPersonaCode } from './personas';
import { simulateDemoAnswers } from './simulatedDemoAnswers';
import { simulateScale, simulateScoring, type SimulatedScore } from './simulatedScoring';

/*
 * DEVELOPMENT FIXTURES ONLY. An in-memory stand-in for the participant API, so every screen can be used before
 * sign-in exists. It serves a catalog of draft questionnaires (by default the platform's own draft content: the
 * household assessment and the codebook's Module A, see ./content.ts; tests pass the small placeholder catalog) and
 * returns scripted outcomes: it evaluates no eligibility rule and applies no research rule. Confirming a roster
 * always assigns the catalog's two questionnaires to the person signed in. Submitting a session whose version has a
 * scoring proposal records a result simulated from that proposal, the way the real API scores in the submission
 * transaction once a rule set is approved (D-52); a session without one stays "submitted" with a pending result.
 * Roster changes are refused with the server's problem codes for its published roster rules (D-50, D-58), so the
 * screens' error states can be seen; the server stays the authority. Privacy requests stay open: no staff work on
 * fixture data. The worlds live in memory; given the app's settings and a cipher they are also sealed into the
 * settings after every change and read back when the API is created, so the reload that applies a language and a
 * switch between simulated accounts keep the demonstration. They are sealed with the answers' AES-GCM cipher under a
 * key of their own (FIXTURE_WORLDS_KEY_NAME), because every sign-out deletes the drafts key; without a cipher (no
 * keystore) nothing is kept on the phone. `resetDemonstration` empties them and deletes the setting and the key.
 */

interface StoredAnswer {
  optionId: string;
  answeredAt: string;
  lastModifiedAt: string;
}

interface StoredSession {
  session: Session;
  fixture: FixtureVersion;
  answers: Map<string, StoredAnswer>;
  submissionKey: string | null;
}

interface World {
  subject: string;
  /** When the person confirmed their details (D-97); null until then. */
  registeredAt: string | null;
  household: Household | null;
  members: Member[];
  selfMemberId: string | null;
  consents: ConsentRecord[];
  assignments: Assignment[];
  sessions: Map<string, StoredSession>;
  results: Map<string, SessionResult>;
  privacyRequests: PrivacyRequest[];
}

let sequence = 0x1000;
const newId = () => `0199e0a1-0000-7000-9000-${(sequence++).toString(16).padStart(12, '0')}`;

const daysAgo = (now: Date, days: number) => new Date(now.getTime() - days * 86_400_000).toISOString();

const POLICIES: ConsentPolicy[] = (['participation', 'research_use', 'referral'] as const).map((consentType) => ({
  id: newId(),
  consentType,
  versionNumber: 1,
  documentReferenceAr: `DEV-PLACEHOLDER-${consentType}-v1-ar`,
  documentReferenceEn: `DEV-PLACEHOLDER-${consentType}-v1-en`,
  approvedAt: null,
}));

const policyOf = (type: ConsentType) => POLICIES.find((policy) => policy.consentType === type) as ConsentPolicy;

function ageOn(now: Date, birthYear: number, birthMonth: number): number {
  const months = (now.getUTCFullYear() - birthYear) * 12 + (now.getUTCMonth() + 1 - birthMonth);
  return Math.floor(months / 12);
}

const ADULT_AGE = 18;
const MAX_ACTIVE_MEMBERS = 30;

/**
 * Whether the person is 18 whatever day of their birth month they were born on: only from the first day of the month
 * after the birth month, 18 years on (only the month is stored). The server's adults-only safeguard (D-58,
 * `YearMonth.IsCertainlyAtLeast`): managing a household, holding an account, giving consent and being assigned a
 * questionnaire. The roster rules (head, main earner, work status) and the age shown keep `ageOn`'s convention.
 */
function isCertainlyAdult(now: Date, birthYear: number, birthMonth: number): boolean {
  return now.getUTCFullYear() * 12 + now.getUTCMonth() + 1 > (birthYear + ADULT_AGE) * 12 + birthMonth;
}

function newMember(now: Date, details: MemberDetails, extra: Partial<Member> = {}): Member {
  const at = now.toISOString();
  return {
    id: newId(),
    ...details,
    age: ageOn(now, details.birthYear, details.birthMonth),
    status: 'active',
    hasAccount: false,
    isCaller: false,
    createdAt: at,
    updatedAt: at,
    ...extra,
  };
}

function newAssignment(
  world: World,
  now: Date,
  fixture: FixtureVersion,
  sectionCode: string | null,
  waveCode: string,
): Assignment {
  const section = fixture.version.sections.find((candidate) => candidate.code === sectionCode);
  return {
    id: newId(),
    memberId: world.selfMemberId ?? '',
    instrumentCode: fixture.version.instrumentCode,
    instrumentName: fixture.version.name,
    versionNumber: fixture.version.versionNumber,
    sectionCode,
    sectionTitle: section?.title ?? null,
    waveId: newId(),
    waveCode,
    status: 'assigned',
    assignedAt: now.toISOString(),
    expiresAt: new Date(now.getTime() + 60 * 86_400_000).toISOString(),
    completedAt: null,
    activeSessionId: null,
    instrumentKind: fixture.version.kind,
  };
}

function newSession(assignment: Assignment, fixture: FixtureVersion, startedAt: string): StoredSession {
  return {
    session: {
      id: newId(),
      assignmentId: assignment.id,
      memberId: assignment.memberId,
      instrumentVersionId: fixture.version.versionId,
      versionNumber: assignment.versionNumber,
      sectionCode: assignment.sectionCode,
      status: 'in_progress',
      startedAt,
      submittedAt: null,
      scoredAt: null,
      answeredCount: 0,
      requiredCount: 0,
    },
    fixture,
    answers: new Map(),
    submissionKey: null,
  };
}

const itemsOfSession = (stored: StoredSession) => itemsOf(stored.fixture.version, stored.session.sectionCode);

/** Records a simulated result when the version has a scoring proposal; otherwise the session stays submitted. */
function scoreIfProposed(world: World, stored: StoredSession, at: string): boolean {
  const answers = new Map([...stored.answers].map(([itemId, answer]) => [itemId, answer.optionId]));
  const score = simulateScoring(stored.fixture, answers);
  if (!score) {
    return false;
  }
  recordResult(world, stored, score, at);
  return true;
}

/** A submitted session becomes scored with `score`, as in the server's submission transaction (D-52). */
function recordResult(world: World, stored: StoredSession, score: SimulatedScore, at: string) {
  stored.session = { ...stored.session, status: 'scored', scoredAt: at };
  world.results.set(stored.session.id, {
    sessionId: stored.session.id,
    sessionStatus: 'scored',
    state: 'complete',
    incompleteReason: null,
    totalScore: score.totalScore,
    tierCode: score.tierCode,
    categoryScores: score.categoryScores,
    computedAt: at,
    scoreVisibility: 'visible',
    scale: score.scale ?? null,
  });
}

/** Whether the signed-in persona's own member is the household's active head, like the server's rule (D-103). */
function callerIsHead(world: World): boolean {
  return world.members.some(
    (member) => member.id === world.selfMemberId && member.status === 'active' && member.isHeadOfHousehold,
  );
}

/** The result as the server sends it to this caller: score values for the household's head only (D-103). */
function asSeenByCaller(world: World, result: SessionResult): SessionResult {
  return callerIsHead(world)
    ? { ...result, scoreVisibility: 'visible' }
    : { ...result, totalScore: null, tierCode: null, categoryScores: [], scoreVisibility: 'head_of_household_only', scale: null };
}

function emptyWorld(subject: string): World {
  return {
    subject,
    registeredAt: null,
    household: null,
    members: [],
    selfMemberId: null,
    consents: [],
    assignments: [],
    sessions: new Map(),
    results: new Map(),
    privacyRequests: [],
  };
}

/**
 * A household with three members, consent for participation, the catalog's first questionnaire finished in an earlier
 * wave (scored when its version has a proposal) and half answered in the current one, and the second not started.
 */
function householdLeadWorld(subject: string, now: Date, catalog: FixtureCatalog): World {
  const world = { ...emptyWorld(subject), registeredAt: daysAgo(now, 201) };
  const self = newMember(
    now,
    {
      birthYear: 1984,
      birthMonth: 3,
      relationshipToHousehold: null,
      employmentStatus: 'employed',
      isHeadOfHousehold: true,
      isPrimaryIncomeEarner: true,
    },
    { hasAccount: true, isCaller: true },
  );
  world.selfMemberId = self.id;
  world.members = [
    self,
    newMember(now, {
      birthYear: 1987,
      birthMonth: 9,
      relationshipToHousehold: 'spouse',
      employmentStatus: 'homemaker',
      isHeadOfHousehold: false,
      isPrimaryIncomeEarner: false,
    }),
    newMember(now, {
      birthYear: 2014,
      birthMonth: 5,
      relationshipToHousehold: 'child',
      employmentStatus: null,
      isHeadOfHousehold: false,
      isPrimaryIncomeEarner: false,
    }),
  ];
  world.household = {
    id: newId(),
    reference: 'DEV-4821',
    status: 'active',
    createdAt: daysAgo(now, 200),
    updatedAt: daysAgo(now, 20),
    rosterConfirmedAt: daysAgo(now, 190),
    activeMemberCount: 3,
  };
  world.consents = [
    {
      id: newId(),
      memberId: self.id,
      consentType: 'participation',
      policyVersionId: policyOf('participation').id,
      policyVersionNumber: 1,
      captureMethod: 'in_app',
      grantedAt: daysAgo(now, 190),
      withdrawnAt: null,
      isActive: true,
    },
  ];

  // An earlier wave, finished and scored, so history and the result screen have something to show. With a scoring
  // proposal (the platform's household assessment) a wave before that one too, so the trend chart (D-132) draws a line.
  const { first, second } = catalog.lead;
  const assessmentItems = itemsOf(first.fixture.version, first.sectionCode);
  const finishCheckIn = (waveCode: string, days: number, optionIndex: number) => {
    const assignment = { ...newAssignment(world, now, first.fixture, first.sectionCode, waveCode), assignedAt: daysAgo(now, days + 1) };
    const stored = newSession(assignment, first.fixture, daysAgo(now, days));
    for (const item of assessmentItems) {
      const optionId = item.options[optionIndex]?.id ?? '';
      stored.answers.set(item.id, { optionId, answeredAt: daysAgo(now, days), lastModifiedAt: daysAgo(now, days) });
    }
    stored.session = { ...stored.session, status: 'submitted', submittedAt: daysAgo(now, days) };
    if (!scoreIfProposed(world, stored, daysAgo(now, days)) && first.earlierResult) {
      recordResult(world, stored, first.earlierResult, daysAgo(now, days));
    }
    world.sessions.set(stored.session.id, stored);
    world.assignments.push({ ...assignment, status: 'completed', completedAt: daysAgo(now, days) });
  };
  finishCheckIn('dev-wave-1', 184, 1);
  if (first.fixture.scoring) {
    finishCheckIn('dev-wave-0', 366, 0);
  }

  // The current wave: the first questionnaire started with two answers, the second not started.
  const current = { ...newAssignment(world, now, first.fixture, first.sectionCode, 'dev-wave-2'), status: 'in_progress' as const };
  const currentSession = newSession(current, first.fixture, daysAgo(now, 1));
  for (const item of assessmentItems.slice(0, 2)) {
    const optionId = item.options[2]?.id ?? '';
    currentSession.answers.set(item.id, { optionId, answeredAt: daysAgo(now, 1), lastModifiedAt: daysAgo(now, 1) });
  }
  world.sessions.set(currentSession.session.id, currentSession);
  world.assignments.push({ ...current, activeSessionId: currentSession.session.id });
  world.assignments.push(newAssignment(world, now, second.fixture, second.sectionCode, 'dev-wave-2'));

  // An earlier request to see the data held, closed by the programme team.
  world.privacyRequests.push({
    id: newId(),
    memberId: self.id,
    type: 'access',
    status: 'closed',
    createdAt: daysAgo(now, 40),
    closedAt: daysAgo(now, 33),
    resolution: 'completed',
  });
  return world;
}

/** What is kept in settings, sealed: every world, and the identifier counter so a reload never hands out an id twice. */
const WORLDS_SETTING = 'dev_fixture_worlds';
/** Binds the sealed snapshot to its setting (GCM associated data). */
const WORLDS_CONTEXT = `settings/${WORLDS_SETTING}`;
const SNAPSHOT_SCHEMA = 1;

/** The keystore entry for the key that seals the kept worlds. Sign-out keeps it; resetting the demonstration deletes it. */
export const FIXTURE_WORLDS_KEY_NAME = 'azwah.dev_fixture_worlds.key.v1';

const storedAnswerSchema = answerSchema.omit({ itemId: true });

const snapshotSchema = z.object({
  schema: z.literal(SNAPSHOT_SCHEMA),
  /** The questionnaires the worlds were made from; a snapshot of other content is dropped. */
  catalog: z.string(),
  sequence: z.number().int().nonnegative(),
  worlds: z.array(
    z.object({
      subject: z.string(),
      registeredAt: z.string().nullable(),
      household: householdSchema.nullable(),
      members: z.array(memberSchema),
      selfMemberId: z.string().nullable(),
      consents: z.array(consentRecordSchema),
      assignments: z.array(assignmentSchema),
      sessions: z.array(
        z.object({
          session: sessionSchema,
          instrumentCode: z.string(),
          versionNumber: z.number().int(),
          answers: z.array(z.tuple([z.string(), storedAnswerSchema])),
          submissionKey: z.string().nullable(),
        }),
      ),
      results: z.array(sessionResultSchema),
      privacyRequests: z.array(privacyRequestSchema),
    }),
  ),
});
type Snapshot = z.infer<typeof snapshotSchema>;

/** Changes whenever the served questionnaires or the consent policies would no longer match stored identifiers. */
const catalogMark = (catalog: FixtureCatalog) =>
  [
    ...catalog.versions.map(
      ({ version }) =>
        `${version.instrumentCode}@${version.versionNumber}:${version.versionId}:${itemsOf(version, null).length}`,
    ),
    ...POLICIES.map((policy) => policy.id),
  ].join('|');

function toSnapshot(worlds: ReadonlyMap<string, World>, catalog: FixtureCatalog): Snapshot {
  return {
    schema: SNAPSHOT_SCHEMA,
    catalog: catalogMark(catalog),
    sequence,
    worlds: [...worlds.values()].map((world) => ({
      ...world,
      sessions: [...world.sessions.values()].map((stored) => ({
        session: stored.session,
        instrumentCode: stored.fixture.version.instrumentCode,
        versionNumber: stored.fixture.version.versionNumber,
        answers: [...stored.answers.entries()],
        submissionKey: stored.submissionKey,
      })),
      results: [...world.results.values()],
    })),
  };
}

/** The worlds a snapshot holds, or null when it cannot be read or was made from other questionnaires. */
function fromSnapshot(json: string, catalog: FixtureCatalog): Map<string, World> | null {
  let parsed: unknown;
  try {
    parsed = JSON.parse(json);
  } catch {
    return null;
  }
  const snapshot = snapshotSchema.safeParse(parsed);
  if (!snapshot.success || snapshot.data.catalog !== catalogMark(catalog)) {
    return null;
  }
  const worlds = new Map<string, World>();
  for (const kept of snapshot.data.worlds) {
    const sessions = new Map<string, StoredSession>();
    for (const { session, instrumentCode, versionNumber, answers, submissionKey } of kept.sessions) {
      const fixture = fixtureVersion(instrumentCode, versionNumber, catalog);
      if (!fixture) {
        return null;
      }
      sessions.set(session.id, { session, fixture, answers: new Map(answers), submissionKey });
    }
    worlds.set(kept.subject, {
      ...kept,
      sessions,
      results: new Map(kept.results.map((result) => [result.sessionId, result])),
    });
  }
  sequence = Math.max(sequence, snapshot.data.sequence);
  return worlds;
}

const seedWorld = (persona: DevPersonaCode, subject: string, now: Date, catalog: FixtureCatalog): World =>
  persona === 'household_lead' ? householdLeadWorld(subject, now, catalog) : emptyWorld(subject);

const fail = (kind: ApiError['kind'], status: number, code?: string): never => {
  throw new ApiError(kind, status, code);
};

export interface FixtureParticipantApiOptions {
  /** The signed-in subject; each subject gets its own world. */
  getSubject: () => string | null;
  /** Artificial delay so loading states are visible. 0 in tests. */
  latencyMs?: number;
  now?: () => Date;
  /** The questionnaires served and assigned. The app uses the platform's draft content; screen tests pass the placeholder. */
  catalog?: FixtureCatalog;
  /** Where the worlds are kept across a reload (the app's settings); without it they live in memory only. */
  settings?: Pick<SettingsRepository, 'get' | 'set' | 'remove'>;
  /**
   * Seals what is kept in `settings`: the answers' cipher with the key FIXTURE_WORLDS_KEY_NAME. Without one (no
   * keystore) the worlds live in memory only, and anything an earlier build kept is removed.
   */
  cipher?: FieldCipher | null;
}

/** The fixture API: the participant API plus the reset of everything it made up. */
export type FixtureParticipantApi = ParticipantApi & DemonstrationData;

export function createFixtureParticipantApi({
  getSubject,
  latencyMs = 350,
  now = () => new Date(),
  catalog = PLATFORM_CATALOG,
  settings,
  cipher = null,
}: FixtureParticipantApiOptions): FixtureParticipantApi {
  let worlds = new Map<string, World>();
  /** The snapshot last sealed into settings (as JSON), so an unchanged world is not written again. */
  let lastKept: string | null = null;

  // Read once, before the first response. Anything unreadable is ignored: the worlds start from their seeds, and the
  // next response seals them over it. Without a cipher nothing may stay in settings.
  const restored: Promise<void> = settings
    ? (async () => {
        const sealed = await settings.get(WORLDS_SETTING);
        if (sealed === null) {
          return;
        }
        if (!cipher) {
          await settings.remove(WORLDS_SETTING);
          return;
        }
        const json = await cipher.decrypt(sealed, WORLDS_CONTEXT);
        const kept = fromSnapshot(json, catalog);
        if (kept) {
          worlds = kept;
          lastKept = json;
        }
      })().catch(() => undefined)
    : Promise.resolve();

  /** Writes to settings one at a time, in order, so an older snapshot never lands after a newer one. */
  let writes: Promise<void> = Promise.resolve();
  const serially = (write: () => Promise<void>): Promise<void> => {
    const next = writes.then(write);
    writes = next.catch(() => undefined);
    return next;
  };

  /** Seals the worlds when they changed. A failed write is tried again after the next response. */
  const keep = () =>
    serially(async () => {
      if (!settings || !cipher) {
        return;
      }
      const json = JSON.stringify(toSnapshot(worlds, catalog));
      if (json === lastKept) {
        return;
      }
      lastKept = json;
      try {
        await settings.set(WORLDS_SETTING, await cipher.encrypt(json, WORLDS_CONTEXT));
      } catch {
        lastKept = null;
      }
    });

  const worldOf = (): World => {
    const subject = getSubject();
    if (!subject) {
      return fail('unauthorized', 401);
    }
    let world = worlds.get(subject);
    if (!world) {
      const persona = personaForSubject(subject);
      world = persona ? seedWorld(persona, subject, now(), catalog) : emptyWorld(subject);
      worlds.set(subject, world);
    }
    return world;
  };

  /** Waits, runs against the current world and hands back a copy, as a network response would be. */
  async function respond<T>(work: (world: World) => T): Promise<T> {
    await new Promise((resolve) => setTimeout(resolve, latencyMs));
    await restored;
    const response = JSON.parse(JSON.stringify(work(worldOf()))) as T;
    await keep();
    return response;
  }

  const requireHousehold = (world: World, householdId: string): Household =>
    world.household && world.household.id === householdId
      ? world.household
      : fail('not_found', 404, 'households.household_not_found');

  const requireActiveHousehold = (world: World, householdId: string): Household => {
    const household = requireHousehold(world, householdId);
    return household.status === 'active' ? household : fail('conflict', 409, 'households.not_active');
  };

  const requireAssignment = (world: World, assignmentId: string): Assignment =>
    world.assignments.find((assignment) => assignment.id === assignmentId) ??
    fail('not_found', 404, 'assignments.assignment_not_found');

  const requireSession = (world: World, sessionId: string): StoredSession =>
    world.sessions.get(sessionId) ?? fail('not_found', 404, 'sessions.session_not_found');

  const requireFixture = (assignment: Assignment): FixtureVersion =>
    fixtureVersion(assignment.instrumentCode, assignment.versionNumber, catalog) ??
    fail('not_found', 404, 'questionnaires.version_not_found');

  const sessionView = (stored: StoredSession): Session => ({
    ...stored.session,
    answeredCount: stored.answers.size,
    requiredCount: itemsOfSession(stored).filter((item) => item.isRequired).length,
  });

  const answersView = (stored: StoredSession) => ({
    sessionId: stored.session.id,
    status: stored.session.status,
    responses: [...stored.answers.entries()].map(([itemId, answer]) => ({ itemId, ...answer })),
  });

  const replaceAssignment = (world: World, next: Assignment) => {
    world.assignments = world.assignments.map((assignment) => (assignment.id === next.id ? next : assignment));
  };

  /** Records answers as the server's SaveResponses does: an open session, and every item and option its own. */
  const saveIn = (stored: StoredSession, answers: readonly AnswerRequest[]) => {
    if (stored.session.status !== 'in_progress') {
      fail('conflict', 409, 'sessions.not_in_progress');
    }
    const items = itemsOfSession(stored);
    for (const { itemId, optionId } of answers) {
      const item = items.find((candidate) => candidate.id === itemId);
      if (!item) fail('bad_request', 400, 'sessions.item_not_in_section');
      if (!item?.options.some((option) => option.id === optionId)) {
        fail('bad_request', 400, 'sessions.option_not_in_item');
      }
    }
    const at = now().toISOString();
    for (const { itemId, optionId } of answers) {
      const existing = stored.answers.get(itemId);
      stored.answers.set(itemId, { optionId, answeredAt: existing?.answeredAt ?? at, lastModifiedAt: at });
    }
  };

  /** Submits and scores as the server's SubmitSession does, completing the assignment; a repeat changes nothing. */
  const submitIn = (world: World, stored: StoredSession, idempotencyKey: string) => {
    if (stored.session.status !== 'in_progress') {
      return {
        sessionId: stored.session.id,
        outcome: stored.submissionKey === idempotencyKey ? ('submitted' as const) : ('already_submitted' as const),
        status: stored.session.status,
        submittedAt: stored.session.submittedAt ?? now().toISOString(),
      };
    }
    const unanswered = itemsOfSession(stored).filter((item) => item.isRequired && !stored.answers.has(item.id));
    if (unanswered.length > 0) {
      fail('conflict', 409, 'sessions.required_items_unanswered');
    }
    const at = now().toISOString();
    stored.session = { ...stored.session, status: 'submitted', submittedAt: at };
    stored.submissionKey = idempotencyKey;
    scoreIfProposed(world, stored, at);
    const assignment = requireAssignment(world, stored.session.assignmentId);
    replaceAssignment(world, { ...assignment, status: 'completed', completedAt: at, activeSessionId: null });
    return { sessionId: stored.session.id, outcome: 'submitted' as const, status: stored.session.status, submittedAt: at };
  };

  const activeMembers = (world: World) => world.members.filter((member) => member.status === 'active');

  /** The server's checks on one member's details (Household.Validate), with its problem codes. */
  const checkDetails = (world: World, details: MemberDetails, replacing: Member | null) => {
    const age = ageOn(now(), details.birthYear, details.birthMonth);
    if (age < 0) fail('bad_request', 400, 'households.birth_month_in_future');
    const others = activeMembers(world).filter((member) => member.id !== replacing?.id);
    if (details.isHeadOfHousehold) {
      if (details.relationshipToHousehold !== null) fail('bad_request', 400, 'households.head_has_relationship');
      if (age < ADULT_AGE) fail('bad_request', 400, 'households.head_under_18');
      if (others.some((member) => member.isHeadOfHousehold)) fail('conflict', 409, 'households.head_already_exists');
    }
    if (details.isPrimaryIncomeEarner) {
      if (age < ADULT_AGE) fail('bad_request', 400, 'households.primary_earner_under_18');
      if (others.some((member) => member.isPrimaryIncomeEarner)) {
        fail('conflict', 409, 'households.primary_earner_already_exists');
      }
    }
    // Safeguards, so the member must be certainly 18 (the server's adults-only rule), not 18 by the convention above.
    const certainlyAdult = isCertainlyAdult(now(), details.birthYear, details.birthMonth);
    if (replacing?.hasAccount && !certainlyAdult) {
      fail('conflict', 409, replacing.isCaller ? 'households.manager_under_18' : 'households.account_holder_under_18');
    }
    if (
      replacing &&
      !certainlyAdult &&
      world.consents.some((consent) => consent.memberId === replacing.id && consent.isActive)
    ) {
      fail('conflict', 409, 'households.member_has_consent');
    }
  };

  const requireActiveMember = (world: World, memberId: string): Member =>
    activeMembers(world).find((member) => member.id === memberId) ??
    fail('not_found', 404, 'households.member_not_found');

  const replaceMember = (world: World, next: Member) => {
    world.members = world.members.map((member) => (member.id === next.id ? next : member));
  };

  /** As D-49: the caller and active members without an account of their own. */
  const actableMembers = (world: World) =>
    world.members.filter((member) => member.status === 'active' && (member.isCaller || !member.hasAccount));

  const myHousehold = (world: World): Me['household'] =>
    world.household && world.selfMemberId
      ? {
          householdId: world.household.id,
          reference: world.household.reference,
          status: world.household.status,
          role: 'manager' as const,
          memberId: world.selfMemberId,
          rosterConfirmedAt: world.household.rosterConfirmedAt,
          actableMemberIds: actableMembers(world).map((member) => member.id),
        }
      : null;

  /** The requests about members the caller may act for, newest first. */
  const visibleRequests = (world: World) => {
    const actable = new Set(actableMembers(world).map((member) => member.id));
    return world.privacyRequests
      .filter((request) => actable.has(request.memberId))
      .sort((a, b) => b.createdAt.localeCompare(a.createdAt));
  };

  const touchHousehold = (world: World) => {
    if (world.household) {
      world.household = {
        ...world.household,
        updatedAt: now().toISOString(),
        rosterConfirmedAt: null,
        activeMemberCount: world.members.filter((member) => member.status === 'active').length,
      };
    }
  };

  return {
    getMe: () =>
      respond((world) => ({
        subject: world.subject,
        roles: ['participant'],
        household: myHousehold(world),
        registeredAt: world.registeredAt,
      })),

    registerMe: () =>
      respond((world) => {
        world.registeredAt ??= now().toISOString();
        return { registeredAt: world.registeredAt };
      }),

    createHousehold: (request) =>
      respond((world) => {
        if (world.household) {
          fail('conflict', 409, 'households.already_member');
        }
        if (request.members.length === 0) fail('bad_request', 400, 'households.roster_empty');
        const at = now();
        const draft = emptyWorld(world.subject);
        for (const { isSelf, ...details } of request.members) {
          checkDetails(draft, details, null);
          if (isSelf && !isCertainlyAdult(at, details.birthYear, details.birthMonth)) {
            fail('conflict', 409, 'households.manager_under_18');
          }
          draft.members.push(newMember(at, details));
        }
        world.members = request.members.map(({ isSelf, ...details }) =>
          newMember(at, details, isSelf ? { hasAccount: true, isCaller: true } : {}),
        );
        world.selfMemberId = world.members[request.members.findIndex((member) => member.isSelf)]?.id ?? null;
        // As the server: creating a household registers the person if they were not yet (D-47).
        world.registeredAt ??= at.toISOString();
        world.household = {
          id: newId(),
          reference: `DEV-${Math.floor(1000 + Math.random() * 9000)}`,
          status: 'active',
          createdAt: at.toISOString(),
          updatedAt: at.toISOString(),
          rosterConfirmedAt: null,
          activeMemberCount: world.members.length,
        };
        return world.household;
      }),

    getHousehold: (householdId) => respond((world) => requireHousehold(world, householdId)),

    listMembers: (householdId) =>
      respond((world) => {
        requireHousehold(world, householdId);
        return world.members;
      }),

    addMember: (householdId, details) =>
      respond((world) => {
        requireActiveHousehold(world, householdId);
        checkDetails(world, details, null);
        if (activeMembers(world).length >= MAX_ACTIVE_MEMBERS) fail('conflict', 409, 'households.roster_full');
        const member = newMember(now(), details);
        world.members.push(member);
        touchHousehold(world);
        return member;
      }),

    updateMember: (householdId, memberId, patch) =>
      respond((world) => {
        requireActiveHousehold(world, householdId);
        const member = requireActiveMember(world, memberId);
        if (Object.keys(patch).length === 0) fail('bad_request', 400);
        const details: MemberDetails = {
          birthYear: patch.birthYear ?? member.birthYear,
          birthMonth: patch.birthMonth ?? member.birthMonth,
          relationshipToHousehold:
            patch.relationshipToHousehold === undefined ? member.relationshipToHousehold : patch.relationshipToHousehold,
          employmentStatus: patch.employmentStatus === undefined ? member.employmentStatus : patch.employmentStatus,
          isHeadOfHousehold: patch.isHeadOfHousehold ?? member.isHeadOfHousehold,
          isPrimaryIncomeEarner: patch.isPrimaryIncomeEarner ?? member.isPrimaryIncomeEarner,
        };
        checkDetails(world, details, member);
        const updated: Member = {
          ...member,
          ...details,
          age: ageOn(now(), details.birthYear, details.birthMonth),
          updatedAt: now().toISOString(),
        };
        replaceMember(world, updated);
        touchHousehold(world);
        return updated;
      }),

    removeMember: (householdId, memberId) =>
      respond((world) => {
        requireActiveHousehold(world, householdId);
        const member = requireActiveMember(world, memberId);
        if (member.isCaller) fail('conflict', 409, 'households.member_is_manager');
        const removed: Member = { ...member, status: 'inactive', updatedAt: now().toISOString() };
        replaceMember(world, removed);
        touchHousehold(world);
        return removed;
      }),

    confirmRoster: (householdId) =>
      respond((world) => {
        const household = requireActiveHousehold(world, householdId);
        // The approved completeness rules (D-50, D-58), as the server checks them.
        const active = activeMembers(world);
        if (active.filter((member) => member.isHeadOfHousehold).length !== 1) {
          fail('conflict', 409, 'households.roster_has_no_head');
        }
        if (active.some((member) => !member.isHeadOfHousehold && member.relationshipToHousehold === null)) {
          fail('conflict', 409, 'households.roster_missing_relationship');
        }
        if (active.some((member) => member.age >= ADULT_AGE && member.employmentStatus === null)) {
          fail('conflict', 409, 'households.roster_missing_employment_status');
        }
        const at = now().toISOString();
        world.household = { ...household, rosterConfirmedAt: at };
        // Scripted, not an eligibility rule: the catalog's two questionnaires for the person signed in, who is
        // certainly 18 (checked at enrolment and at every change), as the server's adults-only guard requires.
        const live = world.assignments.filter((assignment) => assignment.status !== 'completed');
        if (live.length === 0) {
          const { first, second } = catalog.lead;
          world.assignments.push(newAssignment(world, now(), first.fixture, first.sectionCode, 'dev-wave-2'));
          world.assignments.push(newAssignment(world, now(), second.fixture, second.sectionCode, 'dev-wave-2'));
        }
        return {
          householdId,
          rosterConfirmedAt: at,
          waveId: world.assignments[world.assignments.length - 1]?.waveId ?? null,
          waveCode: 'dev-wave-2',
          assignments: world.assignments
            .filter((assignment) => assignment.status !== 'completed')
            .map((assignment) => ({
              assignmentId: assignment.id,
              memberId: assignment.memberId,
              instrumentCode: assignment.instrumentCode,
              sectionCode: assignment.sectionCode,
              status: assignment.status,
            })),
        };
      }),

    getCurrentConsentPolicies: () => respond(() => POLICIES),

    listMyConsents: () => respond((world) => world.consents),

    recordConsent: ({ memberId, policyVersionId, decision }) =>
      respond((world) => {
        const policy = POLICIES.find((candidate) => candidate.id === policyVersionId);
        if (!policy) {
          return fail('not_found', 404, 'consent.policy_not_found');
        }
        const active = world.consents.find(
          (consent) => consent.memberId === memberId && consent.consentType === policy.consentType && consent.isActive,
        );
        if (decision === 'grant') {
          const member = world.members.find((candidate) => candidate.id === memberId);
          if (member && !isCertainlyAdult(now(), member.birthYear, member.birthMonth)) {
            return fail('conflict', 409, 'consent.member_under_18');
          }
          if (active) {
            return active;
          }
          const granted: ConsentRecord = {
            id: newId(),
            memberId,
            consentType: policy.consentType,
            policyVersionId: policy.id,
            policyVersionNumber: policy.versionNumber,
            captureMethod: 'in_app',
            grantedAt: now().toISOString(),
            withdrawnAt: null,
            isActive: true,
          };
          world.consents.push(granted);
          return granted;
        }
        if (!active) {
          return fail('conflict', 409, 'consent.not_active');
        }
        const withdrawn = { ...active, isActive: false, withdrawnAt: now().toISOString() };
        world.consents = world.consents.map((consent) => (consent.id === active.id ? withdrawn : consent));
        return withdrawn;
      }),

    listAssignments: () => respond((world) => world.assignments),

    getAssignment: (assignmentId) => respond((world) => requireAssignment(world, assignmentId)),

    startSession: (assignmentId) =>
      respond((world) => {
        const assignment = requireAssignment(world, assignmentId);
        if (assignment.activeSessionId) {
          fail('conflict', 409, 'sessions.session_already_exists');
        }
        if (assignment.status !== 'assigned') {
          fail('conflict', 409, 'assignments.not_assigned');
        }
        const consented = world.consents.some(
          (consent) =>
            consent.memberId === assignment.memberId && consent.consentType === 'participation' && consent.isActive,
        );
        if (!consented) {
          fail('forbidden', 403, 'sessions.participation_consent_required');
        }
        const stored = newSession(assignment, requireFixture(assignment), now().toISOString());
        world.sessions.set(stored.session.id, stored);
        replaceAssignment(world, { ...assignment, status: 'in_progress', activeSessionId: stored.session.id });
        return sessionView(stored);
      }),

    getPublishedVersion: (instrumentCode, versionNumber) =>
      respond(
        () =>
          fixtureVersion(instrumentCode, versionNumber, catalog)?.version ??
          fail('not_found', 404, 'questionnaires.version_not_found'),
      ),

    getSession: (sessionId) => respond((world) => sessionView(requireSession(world, sessionId))),

    getAnswers: (sessionId) => respond((world) => answersView(requireSession(world, sessionId))),

    saveAnswers: (sessionId, answers) =>
      respond((world) => {
        const stored = requireSession(world, sessionId);
        saveIn(stored, answers);
        return answersView(stored);
      }),

    submitSession: (sessionId, idempotencyKey) => respond((world) => submitIn(world, requireSession(world, sessionId), idempotencyKey)),

    // As the server's demo submission (D-143): the simulation chooses the answers for the level, they are saved, then the
    // session is submitted and scored as above. A retry with the key that submitted it answers as it did.
    submitDemoAnswers: (sessionId, level, idempotencyKey) =>
      respond((world) => {
        const stored = requireSession(world, sessionId);
        if (stored.session.status !== 'in_progress') {
          return stored.submissionKey === idempotencyKey
            ? submitIn(world, stored, idempotencyKey)
            : fail('conflict', 409, 'sessions.not_in_progress');
        }
        const assignment = requireAssignment(world, stored.session.assignmentId);
        if (assignment.expiresAt !== null && new Date(assignment.expiresAt) <= now()) {
          return fail('conflict', 409, 'assignments.assignment_expired');
        }
        const answers =
          simulateDemoAnswers(stored.fixture, stored.session.sectionCode, level) ??
          fail('conflict', 409, 'scoring.policy_not_approved');
        saveIn(stored, answers);
        return submitIn(world, stored, idempotencyKey);
      }),

    // As the server's new check-in (D-144): the household assessment again, in a round of its own, unless one is open.
    openDemoCheckIn: () =>
      respond((world) => {
        if (!world.household) {
          return fail('not_found', 404, 'households.household_not_found');
        }
        if (world.household.rosterConfirmedAt === null) {
          return fail('conflict', 409, 'demo_tools.roster_not_confirmed');
        }
        const { first } = catalog.lead;
        const isSurvey = (assignment: Assignment) =>
          assignment.instrumentCode === first.fixture.version.instrumentCode && assignment.sectionCode === first.sectionCode;
        const live = (status: Assignment['status']) =>
          world.assignments.find(
            (assignment) =>
              isSurvey(assignment) &&
              assignment.status === status &&
              (assignment.expiresAt === null || new Date(assignment.expiresAt) > now()),
          );
        const open = live('in_progress') ?? live('assigned');
        if (open) {
          return { assignmentId: open.id, activeSessionId: open.activeSessionId, waveCode: open.waveCode, opened: false };
        }
        const rounds = world.assignments.map((assignment) => Number(/(\d+)$/.exec(assignment.waveCode)?.[1] ?? 0));
        const waveCode = `dev-wave-${Math.max(0, ...rounds) + 1}`;
        const assignment = newAssignment(world, now(), first.fixture, first.sectionCode, waveCode);
        world.assignments.push(assignment);
        return { assignmentId: assignment.id, activeSessionId: null, waveCode, opened: true };
      }),

    getResult: (sessionId) =>
      respond((world) => {
        const stored = requireSession(world, sessionId);
        if (stored.session.status === 'in_progress' || stored.session.status === 'abandoned') {
          return fail('conflict', 409, 'sessions.not_submitted');
        }
        return asSeenByCaller(
          world,
          world.results.get(sessionId) ?? {
            sessionId,
            sessionStatus: stored.session.status,
            state: 'pending' as const,
            incompleteReason: null,
            totalScore: null,
            tierCode: null,
            categoryScores: [],
            computedAt: null,
            scoreVisibility: 'visible',
            scale: null,
          },
        );
      }),

    listHistory: () =>
      respond((world) =>
        [...world.sessions.values()]
          .filter((stored) => stored.session.status === 'submitted' || stored.session.status === 'scored')
          .map((stored): HistoryItem => {
            const result = world.results.get(stored.session.id);
            return {
              sessionId: stored.session.id,
              assignmentId: stored.session.assignmentId,
              memberId: stored.session.memberId,
              instrumentCode: stored.fixture.version.instrumentCode,
              versionNumber: stored.session.versionNumber,
              sectionCode: stored.session.sectionCode,
              waveCode: world.assignments.find((a) => a.id === stored.session.assignmentId)?.waveCode ?? '',
              status: stored.session.status,
              startedAt: stored.session.startedAt,
              submittedAt: stored.session.submittedAt,
              scoreStatus: result ? (result.state === 'complete' ? 'complete' : 'incomplete') : null,
              tierCode: callerIsHead(world) ? (result?.tierCode ?? null) : null,
            };
          })
          .sort((a, b) => (b.submittedAt ?? '').localeCompare(a.submittedAt ?? '')),
      ),

    // The household assessment's recorded totals, oldest first, for the head only, as the server sends them (D-132).
    // The fixtures hold one version per instrument, scored by one proposal, so every point shares one segment.
    getResilienceTrend: () =>
      respond((world): ResilienceTrend => {
        if (!callerIsHead(world)) {
          return { scoreVisibility: 'head_of_household_only', scale: null, points: [], notShownCount: 0 };
        }
        const checkIns = [...world.sessions.values()]
          .filter((stored) => stored.fixture.version.kind === 'household_assessment' && stored.session.submittedAt !== null)
          .sort((a, b) => (a.session.submittedAt ?? '').localeCompare(b.session.submittedAt ?? ''));
        const plotted = checkIns.flatMap((stored) => {
          const result = world.results.get(stored.session.id);
          const scale = simulateScale(stored.fixture);
          return result?.state === 'complete' && result.totalScore !== null && scale ? [{ stored, result, scale }] : [];
        });
        const points = plotted.map(({ stored, result }) => ({
          sessionId: stored.session.id,
          waveCode: world.assignments.find((a) => a.id === stored.session.assignmentId)?.waveCode ?? '',
          submittedAt: stored.session.submittedAt ?? '',
          totalScore: result.totalScore ?? 0,
          tierCode: result.tierCode,
          segment: 0,
        }));
        const scale = plotted[plotted.length - 1]?.scale ?? null;
        return { scoreVisibility: 'visible', scale, points, notShownCount: checkIns.length - points.length };
      }),

    // No referral rule set is approved anywhere (D-53), so nothing is ever routed.
    listRecommendations: () => respond(() => []),

    // Scores and support recommendations are left out, as the real API does until their display is approved (D-72).
    exportMyData: () =>
      respond((world): MyDataExportDownload => {
        const members = actableMembers(world);
        const ids = new Set(members.map((member) => member.id));
        const data: MyDataExport = {
          generatedAt: now().toISOString(),
          subject: world.subject,
          household: myHousehold(world),
          members,
          consents: world.consents.filter((consent) => ids.has(consent.memberId)),
          assignments: world.assignments.filter((assignment) => ids.has(assignment.memberId)),
          sessions: [...world.sessions.values()]
            .filter((stored) => ids.has(stored.session.memberId))
            .map((stored) => ({ session: sessionView(stored), answers: answersView(stored).responses })),
          referrals: [],
          privacyRequests: visibleRequests(world),
          notIncluded: ['score_results', 'support_recommendations'],
        };
        // The fixture sends exactly what the contract describes, so the body as sent is the data itself.
        return { data, raw: data };
      }),

    listPrivacyRequests: (page) =>
      respond((world) => {
        const requests = visibleRequests(world);
        const start = (Math.max(1, Math.trunc(page)) - 1) * PRIVACY_REQUEST_PAGE_SIZE;
        return {
          items: requests.slice(start, start + PRIVACY_REQUEST_PAGE_SIZE),
          page: Math.max(1, Math.trunc(page)),
          pageSize: PRIVACY_REQUEST_PAGE_SIZE,
          totalCount: requests.length,
        };
      }),

    createPrivacyRequest: ({ type, memberId }) =>
      respond((world) => {
        const target = memberId ?? world.selfMemberId;
        if (!target || !actableMembers(world).some((member) => member.id === target)) {
          return fail('not_found', 404, 'households.member_not_found');
        }
        if (world.privacyRequests.some((r) => r.memberId === target && r.type === type && r.status === 'open')) {
          return fail('conflict', 409, 'privacy.request_already_open');
        }
        const created: PrivacyRequest = {
          id: newId(),
          memberId: target,
          type,
          status: 'open',
          createdAt: now().toISOString(),
          closedAt: null,
          resolution: null,
        };
        world.privacyRequests.push(created);
        return created;
      }),

    // No model is connected in development: the AI helpers answer with labelled simulations (./fixtureAssistant.ts).
    getAssistantAvailability: () => respond(() => ({ assistant: true, consultant: true, scoreReport: true })),
    getScoreReportEligibility: (sessionId) =>
      respond((world) => {
        const stored = requireSession(world, sessionId);
        return { eligible: callerIsHead(world) && (stored.fixture.scoring?.parameters.tierBands?.length ?? 0) > 0 };
      }),
    createScoreReportPart: (sessionId, sectionCode, language) =>
      respond((world) => {
        requireSession(world, sessionId);
        return simulatedScoreReportPart(sectionCode, language, now().toISOString());
      }),
    createScoreReportSummary: (sessionId, language) =>
      respond((world) => {
        requireSession(world, sessionId);
        return simulatedScoreReportSummary(language, now().toISOString());
      }),
    createScoreReport: (sessionId, language) =>
      respond((world) => {
        requireSession(world, sessionId);
        if (!callerIsHead(world)) {
          return fail('forbidden', 403, 'assistant.score_report_head_only');
        }
        const result = world.results.get(sessionId);
        if (!result || result.totalScore === null || result.tierCode === null || result.scale === null) {
          return fail('conflict', 409, 'assistant.score_report_not_ready');
        }
        return simulatedScoreReport(
          { sessionId, totalScore: result.totalScore, tierCode: result.tierCode, scale: result.scale },
          language,
          now().toISOString(),
        );
      }),
    requestAssistantReply: (request) =>
      respond(() => ({ ...simulatedAssistantAnswer(request), generatedAt: now().toISOString() })),
    listSupportResources: () => respond(() => [...FIXTURE_SUPPORT_RESOURCES]),
    listSupportAuthorities: () => respond(() => [...FIXTURE_SUPPORT_AUTHORITIES]),
    searchSupportResources: (text) =>
      respond(() => ({ resources: searchFixtureResources(text), safetyNotice: simulatedSafetyNotice(text) })),

    // Every persona starts again from its seed, and nothing of the demonstration stays on the phone: the setting and
    // its key are deleted (a sealed copy left in free pages can no longer be opened). The counter is kept in memory,
    // so this run never reuses an identifier; a reload may, and nothing on the phone refers to the old ones any more,
    // because the reset signs out, which deletes the drafts.
    resetDemonstration: async () => {
      await restored;
      worlds = new Map();
      await serially(async () => {
        lastKept = JSON.stringify(toSnapshot(worlds, catalog));
        if (settings) {
          await settings.remove(WORLDS_SETTING);
          await cipher?.destroyKey();
        }
      });
    },
  };
}
