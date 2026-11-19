import 'server-only';

import type {
  SafetyAlertStatus,
  SafetyConcern,
  SafetyDetector,
  SafetyResolution,
  SafetySeverity,
  SafetySource,
} from '@/lib/api/schemas';
import { between, householdReference, seeded, seedOf, uuid, type Random } from './random';

/**
 * Synthetic, nameless development data for the mock data source: one population of households, members, assignments,
 * sessions and score results, the same on every run. Every page's numbers are counted from it (`aggregates.ts`), so
 * the pages agree with each other. Nothing here comes from a person.
 *
 * The questionnaires carry the platform's codes and shape (`household_assessment`: 39 items with four options;
 * `abu_dhabi_family_survey`: modules A, B and C with 10, 13 and 13 items, five options and a non-substantive 99), but
 * none of their wording: every title, item and option is a placeholder labelled [DEV SAMPLE], and every approval names
 * a synthetic approver and source. Only the household assessment is scored, under `household_assessment_total` with the
 * bands of D-103; no scoring exists for the family-survey modules, so they have no totals and no bands.
 */

export const MOCK_NOW = '2026-10-16T09:00:00Z';

export type WaveStatus = 'planned' | 'open' | 'closed';

export interface MockWave {
  id: string;
  code: string;
  status: WaveStatus;
  opensAt: string;
  closesAt: string;
}

export const MOCK_WAVES: readonly MockWave[] = [
  {
    id: '0f6e3c2a-1b4d-4c8e-9a71-5d2e8f4b6a01',
    code: 'dev_wave_2026_h1',
    status: 'closed',
    opensAt: '2026-01-01T00:00:00Z',
    closesAt: '2026-07-01T00:00:00Z',
  },
  {
    id: '7c1d9e4b-2a6f-4b3c-8d5e-9f0a1b2c3d02',
    code: 'dev_wave_2026_h2',
    status: 'open',
    opensAt: '2026-07-01T00:00:00Z',
    closesAt: '2027-01-01T00:00:00Z',
  },
];

/** The scored questionnaire: 39 items worth 1 to 4 each, so a total runs from 39 to 156. */
export const HOUSEHOLD_ASSESSMENT = {
  code: 'household_assessment',
  versionNumber: 1,
  ruleSetCode: 'household_assessment_total',
  ruleSetVersion: 1,
  minimumTotal: 39,
  maximumTotal: 156,
  /** Section codes and item counts of the platform's version 1 (39 items in all). */
  sections: [
    ['communication', 7],
    ['connectedness', 5],
    ['outlook', 5],
    ['extended_family', 5],
    ['wealth_debt', 6],
    ['seniors', 5],
    ['work_life', 6],
  ],
} as const;

/** The bands of D-103, low to high, as the platform's `household-assessment.scoring.json` holds them. */
export const HOUSEHOLD_BANDS = [
  { code: 'vulnerable', min: 39, max: 78 },
  { code: 'transitional', min: 79, max: 117 },
  { code: 'flourishing', min: 118, max: 156 },
] as const;

/** The band a synthetic total falls in, by the cut-offs above, so a mock row can never pair a total with another band. */
export function bandOf(total: number): string | null {
  return HOUSEHOLD_BANDS.find((band) => total >= band.min && total <= band.max)?.code ?? null;
}

/** The family-survey modules: section codes and item counts of the platform's codebook draft. None is scored. */
export const FAMILY_SURVEY = {
  code: 'abu_dhabi_family_survey',
  versionNumber: 1,
  sections: [
    ['A', 10],
    ['B', 13],
    ['C', 13],
  ],
} as const;

type Text = { ar: string | null; en: string };

export interface MockOption {
  id: string;
  code: string;
  displayOrder: number;
  value: number;
  isNonSubstantive: boolean;
  label: Text;
}

export interface MockItem {
  id: string;
  code: string;
  displayOrder: number;
  itemType: 'single_choice';
  isRequired: boolean;
  scoringCategory: string | null;
  scoringDirection: 'forward' | 'reverse' | 'not_scored' | 'unspecified';
  prompt: Text;
  options: MockOption[];
}

export interface MockSection {
  id: string;
  code: string;
  displayOrder: number;
  title: Text;
  items: MockItem[];
}

export interface MockVersion {
  id: string;
  instrumentId: string;
  versionNumber: number;
  status: 'draft' | 'approved' | 'retired';
  contentReferenceAr: string | null;
  contentReferenceEn: string | null;
  approvedBy: string | null;
  approvedAt: string | null;
  approvalSource: string | null;
  effectiveFrom: string | null;
  effectiveTo: string | null;
  retiredAt: string | null;
  hasCompleteText: boolean;
  sections: MockSection[];
}

export interface MockInstrument {
  id: string;
  code: string;
  kind: 'household_assessment' | 'family_voices';
  name: Text;
  description: Text;
  status: 'active' | 'retired';
  hasCompleteText: boolean;
}

export interface MockRuleSet {
  id: string;
  kind: 'scoring' | 'eligibility' | 'referral';
  code: string;
  versionNumber: number;
  instrumentVersionId: string;
  status: 'proposed' | 'approved' | 'retired';
  definition: unknown;
  approvedBy: string | null;
  approvedAt: string | null;
  approvalSource: string | null;
  retiredAt: string | null;
}

const SAMPLE_APPROVER = 'dev-sample-approver';
const SAMPLE_SOURCE = '[DEV SAMPLE] synthetic approval record';

function options(random: Random, scale: number, withNotAnswered: boolean, arabic: boolean): MockOption[] {
  const list: MockOption[] = Array.from({ length: scale }, (_, index) => ({
    id: uuid(random),
    code: String(index + 1),
    displayOrder: index + 1,
    value: index + 1,
    isNonSubstantive: false,
    label: { ar: arabic ? `[عينة تطوير] خيار ${index + 1}` : null, en: `[DEV SAMPLE] Option ${index + 1}` },
  }));
  if (withNotAnswered) {
    list.push({
      id: uuid(random),
      code: '99',
      displayOrder: scale + 1,
      value: 99,
      isNonSubstantive: true,
      label: { ar: arabic ? '[عينة تطوير] خيار خارج المقياس' : null, en: '[DEV SAMPLE] Option outside the scale' },
    });
  }
  return list;
}

interface SectionShape {
  code: string;
  itemCount: number;
  scale: number;
  withNotAnswered: boolean;
  /** Items are numbered through the whole version (Q01 to Q39) or within their section (A_q1). */
  itemCode: (index: number) => string;
  scoringCategory: string | null;
}

function section(random: Random, shape: SectionShape, order: number, arabic: boolean): MockSection {
  return {
    id: uuid(random),
    code: shape.code,
    displayOrder: order,
    title: { ar: arabic ? `[عينة تطوير] قسم ${shape.code}` : null, en: `[DEV SAMPLE] Section ${shape.code}` },
    items: Array.from({ length: shape.itemCount }, (_, index) => ({
      id: uuid(random),
      code: shape.itemCode(index),
      displayOrder: index + 1,
      itemType: 'single_choice' as const,
      isRequired: true,
      scoringCategory: shape.scoringCategory,
      // The placeholders stand for no real item, so no scoring direction is claimed for them.
      scoringDirection: 'unspecified' as const,
      prompt: { ar: arabic ? `[عينة تطوير] بند اصطناعي ${index + 1}` : null, en: `[DEV SAMPLE] Synthetic item ${index + 1}` },
      options: options(random, shape.scale, shape.withNotAnswered, arabic),
    })),
  };
}

function householdSections(random: Random, arabic: boolean): MockSection[] {
  let first = 0;
  return HOUSEHOLD_ASSESSMENT.sections.map(([code, itemCount], index) => {
    const offset = first;
    first += itemCount;
    return section(
      random,
      { code, itemCount, scale: 4, withNotAnswered: false, itemCode: (item) => `Q${String(offset + item + 1).padStart(2, '0')}`, scoringCategory: code },
      index + 1,
      arabic,
    );
  });
}

function surveySections(random: Random): MockSection[] {
  return FAMILY_SURVEY.sections.map(([code, itemCount], index) =>
    section(random, { code, itemCount, scale: 5, withNotAnswered: true, itemCode: (item) => `${code}_q${item + 1}`, scoringCategory: null }, index + 1, true),
  );
}

export interface MockContent {
  instruments: MockInstrument[];
  versions: MockVersion[];
  ruleSets: MockRuleSet[];
}

export function buildContent(scoringApproved: boolean): MockContent {
  const random = seeded(11);
  const placeholder = {
    ar: '[عينة تطوير] نصوص بديلة للتطوير فقط، وليست محتوى الاستبيان',
    en: '[DEV SAMPLE] Placeholder text for development only, not the questionnaire content',
  };
  const household: MockInstrument = {
    id: uuid(random),
    code: HOUSEHOLD_ASSESSMENT.code,
    kind: 'household_assessment',
    name: { ar: '[عينة تطوير] تقييم الأسرة (39 بندًا)', en: '[DEV SAMPLE] Household assessment (39 items)' },
    description: placeholder,
    status: 'active',
    hasCompleteText: true,
  };
  const survey: MockInstrument = {
    id: uuid(random),
    code: FAMILY_SURVEY.code,
    kind: 'family_voices',
    name: { ar: '[عينة تطوير] وحدات استبيان الأسرة A و B و C', en: '[DEV SAMPLE] Family survey modules A, B and C' },
    description: placeholder,
    status: 'active',
    hasCompleteText: true,
  };
  const approved = (instrument: MockInstrument, sections: MockSection[]): MockVersion => ({
    id: uuid(random),
    instrumentId: instrument.id,
    versionNumber: 1,
    status: 'approved',
    contentReferenceAr: '[عينة تطوير] مرجع اصطناعي',
    contentReferenceEn: '[DEV SAMPLE] synthetic reference',
    approvedBy: SAMPLE_APPROVER,
    approvedAt: '2025-12-15T09:00:00Z',
    approvalSource: SAMPLE_SOURCE,
    effectiveFrom: '2026-01-01T00:00:00Z',
    effectiveTo: null,
    retiredAt: null,
    hasCompleteText: true,
    sections,
  });
  const householdV1 = approved(household, householdSections(random, true));
  // A draft without Arabic text yet, so the "Arabic missing" and "Not approved" states can be seen.
  const householdV2: MockVersion = {
    ...approved(household, householdSections(random, false)),
    id: uuid(random),
    versionNumber: 2,
    status: 'draft',
    contentReferenceAr: null,
    approvedBy: null,
    approvedAt: null,
    approvalSource: null,
    effectiveFrom: null,
    hasCompleteText: false,
  };
  const surveyV1 = approved(survey, surveySections(random));
  const rule = (
    kind: MockRuleSet['kind'],
    code: string,
    version: MockVersion,
    status: MockRuleSet['status'],
    definition: unknown,
  ): MockRuleSet => ({
    id: uuid(random),
    kind,
    code,
    versionNumber: 1,
    instrumentVersionId: version.id,
    status,
    definition,
    approvedBy: status === 'approved' ? SAMPLE_APPROVER : null,
    approvedAt: status === 'approved' ? '2025-12-20T09:00:00Z' : null,
    approvalSource: status === 'approved' ? SAMPLE_SOURCE : null,
    retiredAt: null,
  });
  // The scoring definition as the platform's `household-assessment.scoring.json` holds it (D-103's bands).
  const scoringDefinition = {
    schema: 'scoring.v1',
    target: null,
    policy: 'summed_item_values',
    algorithmVersion: 1,
    parameters: { reverseScoring: 'mirror_scale', unanswered: 'reject', total: true, tierBands: HOUSEHOLD_BANDS },
  };
  // The modules' eligibility definitions are not copied: the mock assigns at random and evaluates no rule.
  const eligibilityDefinition = (sectionCode: string) => ({
    schema: 'eligibility.v1',
    target: { sectionCode },
    devSample: true,
    note: 'Synthetic development rule set; the rule itself is not part of the mock data.',
  });
  return {
    instruments: [household, survey],
    versions: [householdV1, householdV2, surveyV1],
    ruleSets: [
      rule('scoring', HOUSEHOLD_ASSESSMENT.ruleSetCode, householdV1, scoringApproved ? 'approved' : 'proposed', scoringDefinition),
      rule('eligibility', 'family_voices_module_a', surveyV1, 'approved', eligibilityDefinition('A')),
      rule('eligibility', 'family_voices_module_b', surveyV1, 'approved', eligibilityDefinition('B')),
      rule('eligibility', 'family_voices_module_c', surveyV1, 'approved', eligibilityDefinition('C')),
    ],
  };
}

export type AssignmentStatus = 'assigned' | 'in_progress' | 'completed' | 'expired' | 'cancelled';
export type SessionStatus = 'in_progress' | 'submitted' | 'scored' | 'abandoned';
export const EMPLOYMENT_STATUSES = ['employed', 'self_employed', 'unemployed', 'student', 'homemaker', 'retired', 'other'] as const;
export type EmploymentStatus = (typeof EMPLOYMENT_STATUSES)[number];

export interface MockHousehold {
  householdId: string;
  reference: string;
  status: 'active' | 'withdrawn';
  createdAt: string;
  rosterConfirmedAt: string | null;
  /** Counted from the household's members below. */
  activeMemberCount: number;
}

/** A nameless member: a birth month, an employment status (or none recorded) and whether they head the household. */
export interface MockMember {
  memberId: string;
  householdId: string;
  birthYear: number;
  birthMonth: number;
  employmentStatus: EmploymentStatus | null;
  isHeadOfHousehold: boolean;
  active: boolean;
}

/** One assignment of one member, with its session if it has one. The API never lists an abandoned session. */
export interface MockAssessmentRow {
  assignmentId: string;
  householdId: string;
  memberId: string;
  instrumentCode: string;
  versionNumber: number;
  sectionCode: string | null;
  waveCode: string;
  waveId: string;
  assignmentStatus: AssignmentStatus;
  assignedAt: string;
  expiresAt: string | null;
  completedAt: string | null;
  session: SessionStatus | null;
  submittedAt: string | null;
}

export interface MockResult {
  sessionId: string;
  assignmentId: string;
  householdId: string;
  memberId: string;
  householdReference: string;
  waveId: string;
  waveCode: string;
  instrumentCode: string;
  versionNumber: number;
  sectionCode: string | null;
  ruleSetCode: string;
  ruleSetVersion: number;
  state: 'complete' | 'incomplete';
  totalScore: number | null;
  tierCode: string | null;
  computedAt: string;
}

export interface MockPopulation {
  households: MockHousehold[];
  members: MockMember[];
  assessments: MockAssessmentRow[];
  /** One result per scored session, newest first; empty unless the scenario has approved scoring. */
  results: MockResult[];
}

const HOUR = 60 * 60 * 1000;
const DAY = 24 * HOUR;
const HOUSEHOLD_COUNT = 160;
const iso = (time: number) => new Date(time).toISOString().replace('.000Z', 'Z');

function weighted<T>(random: Random, choices: [T, number][]): T {
  let roll = random() * choices.reduce((sum, [, weight]) => sum + weight, 0);
  for (const [value, weight] of choices) {
    roll -= weight;
    if (roll < 0) return value;
  }
  return choices[choices.length - 1][0];
}

function employmentFor(random: Random, age: number): EmploymentStatus | null {
  if (age < 25) return weighted<EmploymentStatus | null>(random, [['student', 50], ['employed', 25], ['unemployed', 12], ['other', 3], [null, 10]]);
  if (age < 60) {
    return weighted<EmploymentStatus | null>(random, [
      ['employed', 52],
      ['self_employed', 11],
      ['homemaker', 16],
      ['unemployed', 7],
      ['student', 2],
      ['other', 2],
      [null, 10],
    ]);
  }
  return weighted<EmploymentStatus | null>(random, [['retired', 55], ['homemaker', 15], ['employed', 12], ['self_employed', 6], ['other', 2], [null, 10]]);
}

/** A head, often a spouse, sometimes other adults, and children. Children are never assigned anything. */
function membersOf(random: Random, householdId: string, today: Date): { member: MockMember; age: number }[] {
  const ages = [between(random, 26, 72)];
  if (random() < 0.78) ages.push(Math.max(20, ages[0] + between(random, -8, 6)));
  for (let extra = weighted(random, [[0, 55], [1, 30], [2, 15]]); extra > 0; extra--) ages.push(between(random, 18, 86));
  for (let children = weighted(random, [[0, 30], [1, 22], [2, 24], [3, 16], [4, 8]]); children > 0; children--) ages.push(between(random, 0, 17));
  return ages.map((age, index) => {
    const birthMonth = between(random, 1, 12);
    return {
      age,
      member: {
        memberId: uuid(random),
        householdId,
        birthYear: today.getUTCFullYear() - age - (today.getUTCMonth() + 1 < birthMonth ? 1 : 0),
        birthMonth,
        employmentStatus: age < 18 ? null : employmentFor(random, age),
        isHeadOfHousehold: index === 0,
        active: true,
      },
    };
  });
}

/**
 * One synthetic score result for a scored session. Nothing is scored: the total is drawn inside the questionnaire's
 * range and the band is read from that total by the cut-offs of D-103. Every result is complete: the household
 * assessment's rule set rejects unanswered items and its four options have no non-substantive one, so it cannot
 * produce an incomplete result, and the mock shows none.
 */
function resultOf(row: MockAssessmentRow, reference: string): MockResult {
  const random = seeded(seedOf(`${row.assignmentId}:result`));
  const sessionId = uuid(random);
  const [first, second, third] = [random(), random(), random()];
  // Half the draws are flat across the range and half lean to the middle, so every band is well filled.
  const position = first < 0.5 ? second : (second + third) / 2;
  const span = HOUSEHOLD_ASSESSMENT.maximumTotal - HOUSEHOLD_ASSESSMENT.minimumTotal;
  const total = HOUSEHOLD_ASSESSMENT.minimumTotal + Math.min(span, Math.floor(position * (span + 1)));
  return {
    sessionId,
    assignmentId: row.assignmentId,
    householdId: row.householdId,
    memberId: row.memberId,
    householdReference: reference,
    waveId: row.waveId,
    waveCode: row.waveCode,
    instrumentCode: row.instrumentCode,
    versionNumber: row.versionNumber,
    sectionCode: row.sectionCode,
    ruleSetCode: HOUSEHOLD_ASSESSMENT.ruleSetCode,
    ruleSetVersion: HOUSEHOLD_ASSESSMENT.ruleSetVersion,
    state: 'complete',
    totalScore: total,
    tierCode: bandOf(total),
    computedAt: row.completedAt ?? row.assignedAt,
  };
}

/**
 * How many answers of a submitted session chose the non-substantive option (99), for the export only. The household
 * assessment has no such option, so it is always 0 there; a family-survey module gets a synthetic count, drawn from
 * the assignment alone so it is the same on every run and in both scenarios. No answer itself is held anywhere.
 */
export function nonSubstantiveAnswers(row: MockAssessmentRow, answered: number): number {
  if (row.instrumentCode !== FAMILY_SURVEY.code) return 0;
  const random = seeded(seedOf(`${row.assignmentId}:non_substantive`));
  return Math.min(answered, weighted(random, [[0, 70], [1, 18], [2, 8], [3, 4]]));
}

/**
 * The synthetic population, the same on every run. Each roster-confirmed household's head is assigned the household
 * assessment in every wave it was enrolled for, and its adults are assigned family-survey modules at random (no
 * eligibility rule is evaluated). A withdrawn household's assignments are all cancelled, so it has no result, and every
 * member with a result counts as having research-use consent: the analytics and the results list therefore count the
 * same sessions. The scenario changes only whether completed household assessments are scored.
 */
export function buildPopulation(scoringApproved: boolean): MockPopulation {
  const random = seeded(29);
  const now = Date.parse(MOCK_NOW);
  const today = new Date(now);
  const households: MockHousehold[] = [];
  const members: MockMember[] = [];
  const assessments: MockAssessmentRow[] = [];
  const results: MockResult[] = [];
  for (let index = 0; index < HOUSEHOLD_COUNT; index++) {
    // Enrolment was quickest at the start of the study and tails off.
    const created = Date.parse('2025-12-01T00:00:00Z') + Math.floor(300 * random() ** 1.6) * DAY + between(random, 0, 23) * HOUR;
    const confirmed = random() < 0.9 ? created + between(random, 0, 5) * DAY : null;
    const householdId = uuid(random);
    const roster = membersOf(random, householdId, today);
    const household: MockHousehold = {
      householdId,
      reference: householdReference(random),
      status: random() < 0.04 ? 'withdrawn' : 'active',
      createdAt: iso(created),
      rosterConfirmedAt: confirmed === null ? null : iso(confirmed),
      activeMemberCount: roster.filter(({ member }) => member.active).length,
    };
    households.push(household);
    members.push(...roster.map(({ member }) => member));
    if (confirmed === null) continue;

    const adults = roster.filter(({ age }) => age >= 18).map(({ member }) => member);
    for (const wave of MOCK_WAVES) {
      const opens = Date.parse(wave.opensAt);
      const closes = Date.parse(wave.closesAt);
      const open = wave.status === 'open';
      const end = Math.min(closes, now);
      const start = Math.max(opens, confirmed);
      if (start > end - DAY) continue;
      const targets: [MockMember, string, string | null][] = [[adults[0], HOUSEHOLD_ASSESSMENT.code, null]];
      for (const adult of adults) {
        if (random() < 0.45) targets.push([adult, FAMILY_SURVEY.code, 'A']);
        if (random() < 0.3) targets.push([adult, FAMILY_SURVEY.code, 'B']);
        if (random() < 0.2) targets.push([adult, FAMILY_SURVEY.code, 'C']);
      }
      for (const [member, instrumentCode, sectionCode] of targets) {
        const assigned = Math.min(start + between(random, 0, 70) * DAY + between(random, 0, 23) * HOUR, end - 2 * HOUR);
        let status = weighted<AssignmentStatus>(
          random,
          open
            ? [['completed', 48], ['in_progress', 20], ['assigned', 27], ['cancelled', 5]]
            : [['completed', 72], ['expired', 22], ['cancelled', 6]],
        );
        let finished = assigned + between(random, 1, 40) * DAY + between(random, 0, 23) * HOUR;
        const lastHours = between(random, 1, 96);
        const sessionRoll = random();
        if (household.status === 'withdrawn') status = 'cancelled';
        // A closed wave's last completions arrive in its final days; an open wave's unfinished ones are still under way.
        if (!open && finished >= closes) finished = Math.max(assigned + HOUR, closes - lastHours * HOUR);
        if (status === 'completed' && finished > now) status = 'in_progress';
        const completedAt = status === 'completed' ? iso(finished) : null;
        const scored = scoringApproved && instrumentCode === HOUSEHOLD_ASSESSMENT.code;
        const session: SessionStatus | null =
          status === 'completed'
            ? scored
              ? 'scored'
              : 'submitted'
            : status === 'in_progress'
              ? 'in_progress'
              : (status === 'expired' && sessionRoll < 0.5) || (status === 'cancelled' && sessionRoll < 0.25)
                ? 'abandoned'
                : null;
        const row: MockAssessmentRow = {
          assignmentId: uuid(random),
          householdId,
          memberId: member.memberId,
          instrumentCode,
          versionNumber: 1,
          sectionCode,
          waveCode: wave.code,
          waveId: wave.id,
          assignmentStatus: status,
          assignedAt: iso(assigned),
          expiresAt: wave.closesAt,
          completedAt,
          session,
          submittedAt: completedAt,
        };
        assessments.push(row);
        if (session === 'scored') results.push(resultOf(row, household.reference));
      }
    }
  }
  households.sort((a, b) => b.createdAt.localeCompare(a.createdAt) || b.householdId.localeCompare(a.householdId));
  assessments.sort((a, b) => b.assignedAt.localeCompare(a.assignedAt) || b.assignmentId.localeCompare(a.assignmentId));
  results.sort((a, b) => b.computedAt.localeCompare(a.computedAt) || b.sessionId.localeCompare(a.sessionId));
  return { households, members, assessments, results };
}

export type PrivacyRequestType = 'access' | 'correction' | 'erasure';
export type PrivacyResolution = 'completed' | 'declined' | 'withdrawn';

export interface MockPrivacyRequest {
  id: string;
  householdReference: string;
  memberId: string;
  type: PrivacyRequestType;
  status: 'open' | 'closed';
  createdAt: string;
  closedAt: string | null;
  resolution: PrivacyResolution | null;
}

/**
 * Synthetic privacy requests about members of the synthetic households, the same on every run: a household reference
 * and a member ID, never a name or free text. Every third one is already closed. Each has its own member, so the
 * platform's rule of one open request per member and type always holds.
 */
export function buildPrivacyRequests(population: Pick<MockPopulation, 'households' | 'members'>): MockPrivacyRequest[] {
  const random = seeded(41);
  const now = Date.parse(MOCK_NOW);
  const references = new Map(population.households.map((household) => [household.householdId, household.reference]));
  const requests: MockPrivacyRequest[] = [];
  const taken = new Set<string>();
  for (let index = 0; index < 36; index++) {
    let member = population.members[between(random, 0, population.members.length - 1)];
    while (taken.has(member.memberId)) member = population.members[between(random, 0, population.members.length - 1)];
    taken.add(member.memberId);
    const type = weighted<PrivacyRequestType>(random, [['access', 5], ['correction', 3], ['erasure', 2]]);
    const closed = index % 3 === 0;
    const created = now - between(random, closed ? 20 : 0, closed ? 80 : 40) * DAY - between(random, 1, 23) * HOUR;
    requests.push({
      id: uuid(random),
      householdReference: references.get(member.householdId) ?? '',
      memberId: member.memberId,
      type,
      status: closed ? 'closed' : 'open',
      createdAt: iso(created),
      closedAt: closed ? iso(created + between(random, 1, 15) * DAY) : null,
      resolution: closed ? weighted<PrivacyResolution>(random, [['completed', 6], ['declined', 2], ['withdrawn', 2]]) : null,
    });
  }
  return requests;
}

export interface MockSafetyAlert {
  id: string;
  householdReference: string | null;
  memberRelationship: string | null;
  source: SafetySource;
  category: SafetyConcern;
  severity: SafetySeverity;
  detector: SafetyDetector;
  signalCode: string | null;
  language: 'en' | 'ar' | null;
  occurrenceCount: number;
  firstDetectedAt: string;
  lastDetectedAt: string;
  status: SafetyAlertStatus;
  acknowledgedAt: string | null;
  resolvedAt: string | null;
  resolution: SafetyResolution | null;
}

/** The band whose results raise a safety alert (D-136): the lowest of the household assessment's. */
export const ALERTING_BAND = HOUSEHOLD_BANDS[0].code;

/**
 * Synthetic safety alerts about households of the population, the same on every run. Four come from what someone wrote
 * or said (the assistant, a call, the services search), about active households, and between them cover every other
 * concern and every status; hours before `MOCK_NOW` are fixed so the ordering and the summary can be checked exactly.
 * Score alerts come only from the population's own results, as on the platform: with approved scoring, the newest
 * result in the lowest band raised an open alert and an older one, in another household, a resolved one, each when the
 * result was computed. Without approved scoring there is no score, so there is no score alert. They hold codes and a
 * household reference only: no conversation, no name, no free text.
 */
export function buildSafetyAlerts(population: Pick<MockPopulation, 'households' | 'results'>): MockSafetyAlert[] {
  const random = seeded(59);
  const now = Date.parse(MOCK_NOW);
  const ago = (hours: number) => iso(now - hours * HOUR);
  const active = population.households.filter((household) => household.status === 'active');
  const reference = () => active[between(random, 0, active.length - 1)].reference;
  const alert = (fields: Omit<MockSafetyAlert, 'id'>): MockSafetyAlert => ({ id: uuid(random), ...fields });
  const alerts = [
    alert({
      householdReference: reference(),
      memberRelationship: 'head',
      source: 'assistant_chat',
      category: 'suicide_or_self_harm',
      severity: 'critical',
      detector: 'phrase_screen',
      signalCode: 'suicide.intent',
      language: 'en',
      occurrenceCount: 2,
      firstDetectedAt: ago(30),
      lastDetectedAt: ago(2),
      status: 'open',
      acknowledgedAt: null,
      resolvedAt: null,
      resolution: null,
    }),
    alert({
      householdReference: reference(),
      memberRelationship: 'child',
      source: 'consultant_voice',
      category: 'abuse_or_danger',
      severity: 'critical',
      detector: 'ai_moderation',
      signalCode: null,
      language: 'ar',
      occurrenceCount: 1,
      firstDetectedAt: ago(5),
      lastDetectedAt: ago(5),
      status: 'open',
      acknowledgedAt: null,
      resolvedAt: null,
      resolution: null,
    }),
    alert({
      householdReference: reference(),
      memberRelationship: 'spouse',
      source: 'support_search',
      category: 'harm_to_others',
      severity: 'critical',
      detector: 'phrase_screen',
      signalCode: 'harm_others.intent',
      language: 'en',
      occurrenceCount: 3,
      firstDetectedAt: ago(72),
      lastDetectedAt: ago(20),
      status: 'acknowledged',
      acknowledgedAt: ago(18),
      resolvedAt: null,
      resolution: null,
    }),
    alert({
      householdReference: reference(),
      memberRelationship: 'parent',
      source: 'consultant_voice',
      category: 'suicide_or_self_harm',
      severity: 'critical',
      detector: 'provider_filter',
      signalCode: null,
      language: 'ar',
      occurrenceCount: 1,
      firstDetectedAt: ago(120),
      lastDetectedAt: ago(120),
      status: 'resolved',
      acknowledgedAt: ago(118),
      resolvedAt: ago(100),
      resolution: 'family_contacted',
    }),
  ];

  // Results are listed newest first. The household assessment is answered by the head (see `buildPopulation`).
  const alerting = population.results.filter(
    (result) => result.ruleSetCode === HOUSEHOLD_ASSESSMENT.ruleSetCode && result.state === 'complete' && result.tierCode === ALERTING_BAND,
  );
  const newest = alerting[0];
  const older = newest
    ? alerting.find(
        (result) => result.householdId !== newest.householdId && Date.parse(result.computedAt) <= Date.parse(newest.computedAt) - 7 * DAY,
      )
    : undefined;
  const scoreAlert = (result: MockResult, resolved: boolean) =>
    alert({
      householdReference: result.householdReference,
      memberRelationship: 'head',
      source: 'household_assessment',
      category: 'low_resilience_score',
      severity: 'high',
      detector: 'score_band',
      signalCode: ALERTING_BAND,
      language: null,
      occurrenceCount: 1,
      firstDetectedAt: result.computedAt,
      lastDetectedAt: result.computedAt,
      status: resolved ? 'resolved' : 'open',
      acknowledgedAt: null,
      resolvedAt: resolved ? iso(Math.min(now, Date.parse(result.computedAt) + 10 * HOUR)) : null,
      resolution: resolved ? 'no_action_needed' : null,
    });
  if (newest) alerts.push(scoreAlert(newest, false));
  if (older) alerts.push(scoreAlert(older, true));
  return alerts;
}

/**
 * The export allowlist, copied in order from the API's `ExportCatalog` for the mock only (with the API, its catalog
 * endpoint answers); `consistency.test.ts` compares it with the backend file. The mock writes a value for every column
 * of each list (`backend.ts` types each row by them), so the catalog and the CSV cannot drift apart.
 */
export const SESSION_EXPORT_COLUMNS = [
  'participant_pseudonym',
  'household_pseudonym',
  'instrument_code',
  'version_number',
  'section_code',
  'wave_code',
  'session_status',
  'submitted_month',
  'age_band',
  'employment_status',
  'is_head_of_household',
  'answered_count',
  'non_substantive_count',
] as const;

export const SCORE_EXPORT_COLUMNS = [
  'participant_pseudonym',
  'household_pseudonym',
  'instrument_code',
  'version_number',
  'section_code',
  'wave_code',
  'rule_set_code',
  'rule_set_version',
  'score_status',
  'incomplete_reason',
  'total_score',
  'tier_code',
  'excluded_count',
  'scored_month',
  'age_band',
  'employment_status',
] as const;

export type SessionExportColumn = (typeof SESSION_EXPORT_COLUMNS)[number];
export type ScoreExportColumn = (typeof SCORE_EXPORT_COLUMNS)[number];

export const MOCK_EXPORT_CATALOG: { datasets: { code: string; columns: string[] }[]; filters: string[] } = {
  datasets: [
    { code: 'assessment_sessions', columns: [...SESSION_EXPORT_COLUMNS] },
    { code: 'score_results', columns: [...SCORE_EXPORT_COLUMNS] },
  ],
  filters: ['wave_id', 'instrument_code'],
};
