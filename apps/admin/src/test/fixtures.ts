import type {
  AdminPrivacyRequest,
  AdminSafetyAlert,
  AssessmentRow,
  Export,
  HouseholdResult,
  Overview,
  ParticipationRow,
  ScoreDistribution,
  ScoreDistributions,
  Wave,
} from '@/lib/api/schemas';

/** Small, synthetic API bodies for view tests. They pass the client's schemas. */

export const WAVE_H1: Wave = {
  id: '0f6e3c2a-1b4d-4c8e-9a71-5d2e8f4b6a01',
  code: 'wave_2026_h1',
  status: 'closed',
  opensAt: '2026-01-01T00:00:00+00:00',
  closesAt: '2026-07-01T00:00:00+00:00',
};

export const WAVE_H2: Wave = {
  id: '7c1d9e4b-2a6f-4b3c-8d5e-9f0a1b2c3d02',
  code: 'wave_2026_h2',
  status: 'open',
  opensAt: '2026-07-01T00:00:00+00:00',
  closesAt: '2027-01-01T00:00:00+00:00',
};

export function paged<T>(items: T[], page = 1, pageSize = 20, totalCount = items.length) {
  return { items, page, pageSize, totalCount };
}

export const wavesBody = paged([WAVE_H2, WAVE_H1], 1, 100);

export function overviewBody(overrides: Partial<Overview> = {}): Overview {
  return {
    generatedAt: '2026-10-16T09:00:00+00:00',
    minimumGroupSize: 10,
    waveId: null,
    from: '2025-10-16',
    to: '2026-10-16',
    interval: 'month',
    enrollment: { households: 412, activeHouseholds: 398, withdrawnHouseholds: 14, rosterConfirmedHouseholds: 377, activeMembers: 1534 },
    assignments: { total: 980, started: 702, assigned: 278, inProgress: 121, completed: 559, expired: 12, cancelled: 10, completionRate: 0.5763 },
    byVersion: [
      { instrumentCode: 'household_assessment', versionNumber: 1, sectionCode: null, total: 400, completed: 250, completionRate: 0.625 },
      { instrumentCode: 'family_voices', versionNumber: 1, sectionCode: 'module_a', total: 300, completed: 180, completionRate: 0.6 },
    ],
    completionTrend: [
      { periodStart: '2026-08-01', completed: 40 },
      { periodStart: '2026-09-01', completed: 55 },
      { periodStart: '2026-10-01', completed: 21 },
    ],
    demographics: {
      ageAsOf: '2026-10-16',
      byAgeBand: [
        { group: '18_29', total: 120, completed: 70, completionRate: 0.5833, suppressed: false },
        { group: '60_plus', total: null, completed: null, completionRate: null, suppressed: true },
      ],
      byEmploymentStatus: [
        { group: 'employed', total: 300, completed: 200, completionRate: 0.6667, suppressed: false },
        { group: 'retired', total: null, completed: null, completionRate: null, suppressed: true },
        { group: 'student', total: 4, completed: 2, completionRate: 0.5, suppressed: false },
      ],
    },
    ...overrides,
  };
}

export function assessmentRow(overrides: Partial<AssessmentRow> = {}): AssessmentRow {
  return {
    assignmentId: '0190f0e2-0000-7000-8000-000000000001',
    householdId: '0190f0e2-1111-7000-8000-000000000001',
    instrumentCode: 'household_assessment',
    versionNumber: 1,
    sectionCode: null,
    waveCode: 'wave_2026_h2',
    assignmentStatus: 'completed',
    assignedAt: '2026-07-03T08:00:00+00:00',
    expiresAt: '2027-01-01T00:00:00+00:00',
    completedAt: '2026-07-10T10:30:00+00:00',
    sessionStatus: 'submitted',
    submittedAt: '2026-07-10T10:30:00+00:00',
    scored: false,
    ...overrides,
  };
}

export function participationRow(overrides: Partial<ParticipationRow> = {}): ParticipationRow {
  return {
    householdId: '0190f0e2-1111-7000-8000-000000000001',
    reference: 'HH-7K2M-9QXA',
    status: 'active',
    createdAt: '2026-06-01T08:00:00+00:00',
    rosterConfirmedAt: '2026-06-02T08:00:00+00:00',
    activeMemberCount: 4,
    assignments: { assigned: 1, inProgress: 0, completed: 2, expired: 0, cancelled: 0 },
    ...overrides,
  };
}

/** The contract's own example row of `GET /api/v1/admin/results`. */
export function householdResult(overrides: Partial<HouseholdResult> = {}): HouseholdResult {
  return {
    sessionId: '0190f0e2-7777-7000-8000-000000000001',
    householdId: '0190f0e2-1111-7000-8000-000000000001',
    householdReference: 'HH-7K2M-9QXA',
    waveCode: 'wave_2026_h2',
    instrumentCode: 'household_assessment',
    versionNumber: 1,
    sectionCode: null,
    state: 'complete',
    totalScore: 112,
    tierCode: 'transitional',
    computedAt: '2026-10-20T08:15:00Z',
    ...overrides,
  };
}

export function distribution(overrides: Partial<ScoreDistribution> = {}): ScoreDistribution {
  return {
    instrumentCode: 'household_assessment',
    versionNumber: 1,
    sectionCode: null,
    ruleSetCode: 'household_scoring',
    ruleSetVersion: 1,
    total: 60,
    suppressed: false,
    tiers: [
      { tier: 'tier_a', count: 25, suppressed: false },
      { tier: 'tier_b', count: 35, suppressed: false },
    ],
    ...overrides,
  };
}

export function distributionsBody(distributions: ScoreDistribution[], waveId: string | null = null): ScoreDistributions {
  return { generatedAt: '2026-10-16T09:00:00+00:00', minimumGroupSize: 10, waveId, distributions };
}

export function exportJob(overrides: Partial<Export> = {}): Export {
  return {
    id: '0190f0e2-2222-7000-8000-000000000001',
    dataset: 'score_results',
    columns: ['participant_pseudonym', 'tier_code'],
    filters: { waveId: null, instrumentCode: null },
    status: 'completed',
    rowCount: 42,
    errorCode: null,
    requestedAt: '2026-10-15T08:00:00+00:00',
    finishedAt: '2026-10-15T08:01:00+00:00',
    expiresAt: '2026-10-22T08:01:00+00:00',
    downloadPath: '/api/v1/admin/exports/0190f0e2-2222-7000-8000-000000000001/file',
    ...overrides,
  };
}

export function privacyRequest(overrides: Partial<AdminPrivacyRequest> = {}): AdminPrivacyRequest {
  return {
    id: '0190f0e2-5555-7000-8000-000000000001',
    householdReference: 'HH-7K2M-9QXA',
    memberId: '0190f0e2-6666-7000-8000-00000000a1b2',
    type: 'access',
    status: 'open',
    createdAt: '2026-10-02T08:30:00+00:00',
    closedAt: null,
    resolution: null,
    ...overrides,
  };
}

export function safetyAlert(overrides: Partial<AdminSafetyAlert> = {}): AdminSafetyAlert {
  return {
    id: '0190f0e2-7777-7000-8000-000000000001',
    householdReference: 'HH-7K2M-9QXA',
    memberRelationship: 'parent',
    source: 'assistant_chat',
    category: 'suicide_or_self_harm',
    severity: 'critical',
    detector: 'phrase_screen',
    signalCode: 'suicide.intent',
    language: 'en',
    occurrenceCount: 2,
    firstDetectedAt: '2026-10-14T08:00:00+00:00',
    lastDetectedAt: '2026-10-15T09:30:00+00:00',
    status: 'open',
    acknowledgedAt: null,
    resolvedAt: null,
    resolution: null,
    ...overrides,
  };
}

export const catalogBody = {
  datasets: [
    { code: 'assessment_sessions', columns: ['participant_pseudonym', 'wave_code', 'session_status'] },
    { code: 'score_results', columns: ['participant_pseudonym', 'tier_code', 'score_status'] },
  ],
  filters: ['wave_id', 'instrument_code'],
};
