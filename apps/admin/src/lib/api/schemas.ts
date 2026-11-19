import { z } from 'zod';

/**
 * Runtime contracts for every API response the dashboard reads, matching backend/openapi/v1.json. A response that does
 * not match is refused (shown as "the platform sent a response this page cannot read"), never partly displayed.
 */

const id = z.guid();
const count = z.number().int().nonnegative();
const rate = z.number().min(0).max(1).nullable();
const timestamp = z.iso.datetime({ offset: true });
const day = z.iso.date();
const text = z.object({ ar: z.string().nullable(), en: z.string() });

export function pagedSchema<T extends z.ZodType>(item: T) {
  return z.object({ items: z.array(item), page: z.number().int().min(1), pageSize: z.number().int().min(1), totalCount: count });
}

export type Paged<T> = { items: T[]; page: number; pageSize: number; totalCount: number };

export const waveSchema = z.object({
  id,
  code: z.string(),
  status: z.enum(['planned', 'open', 'closed']),
  opensAt: timestamp,
  closesAt: timestamp,
});
export type Wave = z.infer<typeof waveSchema>;

export const ASSIGNMENT_STATUSES = ['assigned', 'in_progress', 'completed', 'expired', 'cancelled'] as const;
export const SESSION_STATUSES = ['in_progress', 'submitted', 'scored', 'abandoned'] as const;

const demographicRow = z.object({
  group: z.string(),
  total: count.nullable(),
  completed: count.nullable(),
  completionRate: rate,
  suppressed: z.boolean(),
});
export type DemographicRow = z.infer<typeof demographicRow>;

export const overviewSchema = z.object({
  generatedAt: timestamp,
  minimumGroupSize: z.number().int().min(1),
  waveId: id.nullable(),
  from: day,
  to: day,
  interval: z.enum(['month', 'week']),
  enrollment: z.object({
    households: count,
    activeHouseholds: count,
    withdrawnHouseholds: count,
    rosterConfirmedHouseholds: count,
    activeMembers: count,
  }),
  assignments: z.object({
    total: count,
    started: count,
    assigned: count,
    inProgress: count,
    completed: count,
    expired: count,
    cancelled: count,
    completionRate: rate,
  }),
  byVersion: z.array(
    z.object({
      instrumentCode: z.string(),
      versionNumber: z.number().int(),
      sectionCode: z.string().nullable(),
      total: count,
      completed: count,
      completionRate: rate,
    }),
  ),
  completionTrend: z.array(z.object({ periodStart: day, completed: count })),
  demographics: z.object({ ageAsOf: day, byAgeBand: z.array(demographicRow), byEmploymentStatus: z.array(demographicRow) }),
});
export type Overview = z.infer<typeof overviewSchema>;

const tierCount = z.object({ tier: z.string(), count: count.nullable(), suppressed: z.boolean() });

export const scoreDistributionSchema = z.object({
  instrumentCode: z.string(),
  versionNumber: z.number().int(),
  sectionCode: z.string().nullable(),
  ruleSetCode: z.string(),
  ruleSetVersion: z.number().int(),
  total: count.nullable(),
  suppressed: z.boolean(),
  tiers: z.array(tierCount),
});
export type ScoreDistribution = z.infer<typeof scoreDistributionSchema>;

export const scoreDistributionsSchema = z.object({
  generatedAt: timestamp,
  minimumGroupSize: z.number().int().min(1),
  waveId: id.nullable(),
  distributions: z.array(scoreDistributionSchema),
});
export type ScoreDistributions = z.infer<typeof scoreDistributionsSchema>;

export const assessmentRowSchema = z.object({
  assignmentId: id,
  householdId: id,
  instrumentCode: z.string(),
  versionNumber: z.number().int(),
  sectionCode: z.string().nullable(),
  waveCode: z.string(),
  assignmentStatus: z.enum(ASSIGNMENT_STATUSES),
  assignedAt: timestamp,
  expiresAt: timestamp.nullable(),
  completedAt: timestamp.nullable(),
  sessionStatus: z.enum(SESSION_STATUSES).nullable(),
  submittedAt: timestamp.nullable(),
  scored: z.boolean(),
});
export type AssessmentRow = z.infer<typeof assessmentRowSchema>;

export const participationRowSchema = z.object({
  householdId: id,
  reference: z.string(),
  status: z.enum(['active', 'withdrawn']),
  createdAt: timestamp,
  rosterConfirmedAt: timestamp.nullable(),
  activeMemberCount: count,
  assignments: z.object({ assigned: count, inProgress: count, completed: count, expired: count, cancelled: count }),
});
export type ParticipationRow = z.infer<typeof participationRowSchema>;

export const SCORE_STATES = ['complete', 'incomplete'] as const;

/**
 * A household's latest recorded score result for one scored session, for system administrators. The total and band are
 * the platform's, shown exactly as recorded. As the platform stores them, an incomplete result has neither a total nor
 * a band, and a band never comes without a total; anything else is refused as malformed.
 */
export const householdResultSchema = z
  .object({
    sessionId: id,
    householdId: id,
    householdReference: z.string().min(1),
    waveCode: z.string(),
    instrumentCode: z.string(),
    versionNumber: z.number().int(),
    sectionCode: z.string().nullable(),
    state: z.enum(SCORE_STATES),
    totalScore: z.number().nullable(),
    tierCode: z.string().min(1).nullable(),
    computedAt: timestamp,
  })
  .refine((result) =>
    result.state === 'incomplete' ? result.totalScore === null && result.tierCode === null : result.tierCode === null || result.totalScore !== null,
  );
export type HouseholdResult = z.infer<typeof householdResultSchema>;

export const EXPORT_STATUSES = ['queued', 'running', 'completed', 'failed', 'expired'] as const;

export const exportSchema = z.object({
  id,
  dataset: z.string(),
  columns: z.array(z.string()),
  filters: z.object({ waveId: id.nullable(), instrumentCode: z.string().nullable() }),
  status: z.enum(EXPORT_STATUSES),
  rowCount: count.nullable(),
  errorCode: z.string().nullable(),
  requestedAt: timestamp,
  finishedAt: timestamp.nullable(),
  expiresAt: timestamp.nullable(),
  downloadPath: z.string().nullable(),
});
export type Export = z.infer<typeof exportSchema>;

export const exportCatalogSchema = z.object({
  datasets: z.array(z.object({ code: z.string(), columns: z.array(z.string()).min(1) })),
  filters: z.array(z.string()),
});
export type ExportCatalog = z.infer<typeof exportCatalogSchema>;

export const instrumentSchema = z.object({
  id,
  code: z.string(),
  kind: z.enum(['household_assessment', 'family_voices']),
  name: text,
  description: text,
  status: z.enum(['active', 'retired']),
  hasCompleteText: z.boolean(),
});
export type Instrument = z.infer<typeof instrumentSchema>;

export const VERSION_STATUSES = ['draft', 'approved', 'retired'] as const;

export const instrumentVersionSummarySchema = z.object({
  id,
  instrumentId: id,
  versionNumber: z.number().int(),
  status: z.enum(VERSION_STATUSES),
  approvedBy: z.string().nullable(),
  approvedAt: timestamp.nullable(),
  approvalSource: z.string().nullable(),
  effectiveFrom: timestamp.nullable(),
  effectiveTo: timestamp.nullable(),
  retiredAt: timestamp.nullable(),
  sectionCount: count,
  itemCount: count,
});
export type InstrumentVersionSummary = z.infer<typeof instrumentVersionSummarySchema>;

const optionSchema = z.object({
  id,
  code: z.string(),
  displayOrder: z.number().int(),
  value: z.number().int(),
  isNonSubstantive: z.boolean(),
  label: text,
});

const itemSchema = z.object({
  id,
  code: z.string(),
  displayOrder: z.number().int(),
  itemType: z.string(),
  isRequired: z.boolean(),
  scoringCategory: z.string().nullable(),
  scoringDirection: z.enum(['forward', 'reverse', 'not_scored', 'unspecified']),
  prompt: text,
  options: z.array(optionSchema),
});

export const instrumentVersionSchema = z.object({
  id,
  instrumentId: id,
  versionNumber: z.number().int(),
  status: z.enum(VERSION_STATUSES),
  contentReferenceAr: z.string().nullable(),
  contentReferenceEn: z.string().nullable(),
  approvedBy: z.string().nullable(),
  approvedAt: timestamp.nullable(),
  approvalSource: z.string().nullable(),
  effectiveFrom: timestamp.nullable(),
  effectiveTo: timestamp.nullable(),
  retiredAt: timestamp.nullable(),
  hasCompleteText: z.boolean(),
  sections: z.array(
    z.object({ id, code: z.string(), displayOrder: z.number().int(), title: text, items: z.array(itemSchema) }),
  ),
});
export type InstrumentVersion = z.infer<typeof instrumentVersionSchema>;

export const ruleSetSchema = z.object({
  id,
  kind: z.enum(['scoring', 'eligibility', 'referral']),
  code: z.string(),
  versionNumber: z.number().int(),
  instrumentVersionId: id,
  status: z.enum(['proposed', 'approved', 'retired']),
  definition: z.unknown(),
  approvedBy: z.string().nullable(),
  approvedAt: timestamp.nullable(),
  approvalSource: z.string().nullable(),
  retiredAt: timestamp.nullable(),
});
export type RuleSet = z.infer<typeof ruleSetSchema>;

export const PRIVACY_REQUEST_TYPES = ['access', 'correction', 'erasure'] as const;
export const PRIVACY_REQUEST_STATUSES = ['open', 'closed'] as const;
export const PRIVACY_RESOLUTIONS = ['completed', 'declined', 'withdrawn'] as const;
export type PrivacyRequestStatus = (typeof PRIVACY_REQUEST_STATUSES)[number];
export type PrivacyResolution = (typeof PRIVACY_RESOLUTIONS)[number];

/**
 * A family's request about a member's data (`AdminPrivacyRequestResponse`). It carries no free text. An open request
 * has neither a closing time nor an outcome and a closed one has both; anything else is refused as malformed.
 */
export const adminPrivacyRequestSchema = z
  .object({
    id,
    householdReference: z.string().min(1),
    memberId: id,
    type: z.enum(PRIVACY_REQUEST_TYPES),
    status: z.enum(PRIVACY_REQUEST_STATUSES),
    createdAt: timestamp,
    closedAt: timestamp.nullable(),
    resolution: z.enum(PRIVACY_RESOLUTIONS).nullable(),
  })
  .refine((request) =>
    request.status === 'open' ? request.closedAt === null && request.resolution === null : request.closedAt !== null && request.resolution !== null,
  );
export type AdminPrivacyRequest = z.infer<typeof adminPrivacyRequestSchema>;

export const SAFETY_ALERT_STATUSES = ['open', 'acknowledged', 'resolved'] as const;
export const SAFETY_CONCERNS = ['suicide_or_self_harm', 'harm_to_others', 'abuse_or_danger', 'low_resilience_score'] as const;
export const SAFETY_SOURCES = ['assistant_chat', 'consultant_voice', 'support_search', 'household_assessment'] as const;
export const SAFETY_DETECTORS = ['phrase_screen', 'ai_moderation', 'provider_filter', 'score_band'] as const;
export const SAFETY_SEVERITIES = ['critical', 'high'] as const;
export const SAFETY_RESOLUTIONS = [
  'family_contacted',
  'referred_to_service',
  'emergency_services_contacted',
  'no_action_needed',
  'false_alarm',
  'unable_to_contact',
] as const;
export type SafetyAlertStatus = (typeof SAFETY_ALERT_STATUSES)[number];
export type SafetyConcern = (typeof SAFETY_CONCERNS)[number];
export type SafetySource = (typeof SAFETY_SOURCES)[number];
export type SafetyDetector = (typeof SAFETY_DETECTORS)[number];
export type SafetySeverity = (typeof SAFETY_SEVERITIES)[number];
export type SafetyResolution = (typeof SAFETY_RESOLUTIONS)[number];

/**
 * A safety concern raised for a household or member (`AdminSafetyAlertResponse`). It carries codes only: no
 * conversation, no free text, no name. An open alert has neither an acknowledgement nor an outcome, an acknowledged one
 * has an acknowledgement time and no outcome, and a resolved one has a time and an outcome (it may have skipped the
 * acknowledgement); anything else is refused as malformed.
 */
/** A code the platform may add later: lowercase words joined by `_` or `.`, never free text. */
const safetyCode = z.string().regex(/^[a-z][a-z0-9_.]{0,63}$/);

export const adminSafetyAlertSchema = z
  .object({
    id,
    householdReference: z.string().min(1).nullable(),
    memberRelationship: safetyCode.nullable(),
    source: z.union([z.enum(SAFETY_SOURCES), safetyCode]),
    category: z.enum(SAFETY_CONCERNS),
    severity: z.enum(SAFETY_SEVERITIES),
    detector: z.union([z.enum(SAFETY_DETECTORS), safetyCode]),
    signalCode: safetyCode.nullable(),
    language: z.enum(['en', 'ar']).nullable(),
    occurrenceCount: z.number().int().min(1),
    firstDetectedAt: timestamp,
    lastDetectedAt: timestamp,
    status: z.enum(SAFETY_ALERT_STATUSES),
    acknowledgedAt: timestamp.nullable(),
    resolvedAt: timestamp.nullable(),
    resolution: z.enum(SAFETY_RESOLUTIONS).nullable(),
  })
  .refine((alert) => {
    if (alert.status === 'open') return alert.acknowledgedAt === null && alert.resolvedAt === null && alert.resolution === null;
    if (alert.status === 'acknowledged') return alert.acknowledgedAt !== null && alert.resolvedAt === null && alert.resolution === null;
    return alert.resolvedAt !== null && alert.resolution !== null;
  });
export type AdminSafetyAlert = z.infer<typeof adminSafetyAlertSchema>;

/** Counts only, no personal data (`SafetyAlertSummaryResponse`); the shell polls it. */
export const safetyAlertSummarySchema = z.object({
  open: z.number().int().min(0),
  acknowledged: z.number().int().min(0),
  openCritical: z.number().int().min(0),
  latestDetectedAt: timestamp.nullable(),
});
export type SafetyAlertSummary = z.infer<typeof safetyAlertSummarySchema>;

export const healthSchema = z.object({ status: z.enum(['ready', 'not_ready', 'unreachable', 'not_configured', 'mock']) });
export type Health = z.infer<typeof healthSchema>;

/** The parts of a ProblemDetails body the dashboard reads; `title` and `detail` are never shown. */
export const problemSchema = z
  .object({
    code: z.string().optional(),
    traceId: z.string().optional(),
    errors: z.record(z.string(), z.array(z.string())).optional(),
  })
  .loose();
