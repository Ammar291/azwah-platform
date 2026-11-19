import { z } from 'zod';

/*
 * Response and request shapes of the participant API, transcribed from backend/openapi/v1.json (the committed
 * contract). Every response the app uses is validated against these before it reaches a screen.
 */

const id = z.guid();
const timestamp = z.iso.datetime({ offset: true });
const int = z.number().int();

export const textSchema = z.object({ ar: z.string().nullable(), en: z.string() });
export type Text = z.infer<typeof textSchema>;

const paged = <T extends z.ZodType>(item: T) =>
  z.object({ items: z.array(item), page: int, pageSize: int, totalCount: int });

export const householdStatusSchema = z.enum(['active', 'withdrawn']);
export const householdRoleSchema = z.enum(['manager', 'member']);
export const relationshipSchema = z.enum([
  'spouse',
  'child',
  'parent',
  'sibling',
  'grandchild',
  'grandparent',
  'other_relative',
  'non_relative',
]);
export const employmentStatusSchema = z.enum([
  'employed',
  'self_employed',
  'unemployed',
  'student',
  'homemaker',
  'retired',
  'other',
]);
export const memberStatusSchema = z.enum(['active', 'inactive']);
export const assignmentStatusSchema = z.enum(['assigned', 'in_progress', 'completed', 'expired', 'cancelled']);
export const sessionStatusSchema = z.enum(['in_progress', 'submitted', 'scored', 'abandoned']);
export const consentTypeSchema = z.enum(['participation', 'research_use', 'referral']);
export const resultStateSchema = z.enum(['pending', 'complete', 'incomplete']);

export type Relationship = z.infer<typeof relationshipSchema>;
export type EmploymentStatus = z.infer<typeof employmentStatusSchema>;
export type AssignmentStatus = z.infer<typeof assignmentStatusSchema>;
export type SessionStatus = z.infer<typeof sessionStatusSchema>;
export type ConsentType = z.infer<typeof consentTypeSchema>;

// MyHouseholdResponse, in GET /api/v1/me and the data export
export const myHouseholdSchema = z.object({
  householdId: id,
  reference: z.string(),
  status: householdStatusSchema,
  role: householdRoleSchema,
  memberId: id,
  rosterConfirmedAt: timestamp.nullable(),
  actableMemberIds: z.array(id),
});

// GET /api/v1/me
export const meSchema = z.object({
  subject: z.string(),
  roles: z.array(z.string()),
  household: myHouseholdSchema.nullable(),
  /**
   * When the person confirmed their details and became a platform user (D-97); null until then, so the app asks once.
   * Absent from a server that predates registration: the app then treats the person as registered.
   */
  registeredAt: timestamp.nullable().optional(),
});
export type Me = z.infer<typeof meSchema>;

// POST /api/v1/me/registration
export const registrationSchema = z.object({ registeredAt: timestamp });
export type Registration = z.infer<typeof registrationSchema>;

// GET /api/v1/households/{householdId}
export const householdSchema = z.object({
  id,
  reference: z.string(),
  status: householdStatusSchema,
  createdAt: timestamp,
  updatedAt: timestamp,
  rosterConfirmedAt: timestamp.nullable(),
  activeMemberCount: int,
});
export type Household = z.infer<typeof householdSchema>;

// GET /api/v1/households/{householdId}/members
export const memberSchema = z.object({
  id,
  birthYear: int,
  birthMonth: int,
  age: int,
  relationshipToHousehold: relationshipSchema.nullable(),
  employmentStatus: employmentStatusSchema.nullable(),
  isHeadOfHousehold: z.boolean(),
  isPrimaryIncomeEarner: z.boolean(),
  status: memberStatusSchema,
  hasAccount: z.boolean(),
  isCaller: z.boolean(),
  createdAt: timestamp,
  updatedAt: timestamp,
});
export type Member = z.infer<typeof memberSchema>;
export const memberPageSchema = paged(memberSchema);

/** MemberDetailsRequest: POST /households/{id}/members. */
export interface MemberDetails {
  birthYear: number;
  birthMonth: number;
  relationshipToHousehold: Relationship | null;
  employmentStatus: EmploymentStatus | null;
  isHeadOfHousehold: boolean;
  isPrimaryIncomeEarner: boolean;
}

/**
 * UpdateMemberRequest: PATCH /households/{id}/members/{memberId}, a JSON merge patch (D-57). Absent fields stay as
 * they are; `null` clears the relationship or the work status. At least one field must be present.
 */
export type MemberPatch = Partial<MemberDetails>;

/** CreateHouseholdRequest: POST /households. Exactly one member is the caller (`isSelf`). */
export interface CreateHouseholdRequest {
  members: (MemberDetails & { isSelf: boolean })[];
}

// POST /api/v1/households/{householdId}/confirm-roster
export const rosterConfirmationSchema = z.object({
  householdId: id,
  rosterConfirmedAt: timestamp,
  waveId: id.nullable(),
  waveCode: z.string().nullable(),
  assignments: z.array(
    z.object({
      assignmentId: id,
      memberId: id,
      instrumentCode: z.string(),
      sectionCode: z.string().nullable(),
      status: assignmentStatusSchema,
    }),
  ),
});
export type RosterConfirmation = z.infer<typeof rosterConfirmationSchema>;
export type RosterAssignment = RosterConfirmation['assignments'][number];

// GET /api/v1/consent/policies/current
export const consentPolicySchema = z.object({
  id,
  consentType: consentTypeSchema,
  versionNumber: int,
  documentReferenceAr: z.string(),
  documentReferenceEn: z.string(),
  approvedAt: timestamp.nullable(),
});
export type ConsentPolicy = z.infer<typeof consentPolicySchema>;
export const currentConsentPoliciesSchema = z.object({ policies: z.array(consentPolicySchema) });

// POST /api/v1/consent and GET /api/v1/me/consents
export const consentRecordSchema = z.object({
  id,
  memberId: id,
  consentType: consentTypeSchema,
  policyVersionId: id,
  policyVersionNumber: int,
  captureMethod: z.enum(['in_app', 'paper_form', 'assisted_by_staff']),
  grantedAt: timestamp,
  withdrawnAt: timestamp.nullable(),
  isActive: z.boolean(),
});
export type ConsentRecord = z.infer<typeof consentRecordSchema>;
export const consentRecordPageSchema = paged(consentRecordSchema);

export interface RecordConsentRequest {
  memberId: string;
  policyVersionId: string;
  decision: 'grant' | 'withdraw';
}

// GET /api/v1/assignments and /assignments/{assignmentId}
export const assignmentSchema = z.object({
  id,
  memberId: id,
  instrumentCode: z.string(),
  instrumentName: textSchema,
  versionNumber: int,
  sectionCode: z.string().nullable(),
  sectionTitle: textSchema.nullable(),
  waveId: id,
  waveCode: z.string(),
  status: assignmentStatusSchema,
  assignedAt: timestamp,
  expiresAt: timestamp.nullable(),
  completedAt: timestamp.nullable(),
  activeSessionId: id.nullable(),
  /** The household assessment, whose result the family's next steps and trend come from, or a family-voices module (D-146). */
  instrumentKind: z.enum(['household_assessment', 'family_voices']),
});
export type Assignment = z.infer<typeof assignmentSchema>;
export const assignmentPageSchema = paged(assignmentSchema);

// GET /api/v1/questionnaires/{code}/versions/{version}: no scoring direction, category or option values.
export const publishedOptionSchema = z.object({
  id,
  code: z.string(),
  displayOrder: int,
  isNonSubstantive: z.boolean(),
  label: textSchema,
});
const singleChoiceItemSchema = z.object({
  id,
  code: z.string(),
  displayOrder: int,
  itemType: z.literal('single_choice'),
  isRequired: z.boolean(),
  prompt: textSchema,
  options: z.array(publishedOptionSchema),
});
export type SingleChoiceItem = z.infer<typeof singleChoiceItemSchema>;

/**
 * An item of a type this app cannot show (the contract's ItemType lists only `single_choice`; a newer server could
 * add more). Kept rather than refused, so the questionnaire can say it needs a newer app instead of failing to load,
 * and never silently skipped: a questionnaire containing one cannot be answered (decision M-43).
 */
const unsupportedItemSchema = z
  .object({
    id,
    code: z.string(),
    displayOrder: int,
    itemType: z.string(),
    isRequired: z.boolean(),
    prompt: textSchema,
  })
  .transform((item) => ({ ...item, itemType: 'unsupported' as const, options: [] as PublishedOption[] }));
export type UnsupportedItem = z.output<typeof unsupportedItemSchema>;

export const publishedItemSchema = z.union([singleChoiceItemSchema, unsupportedItemSchema]);
export type PublishedItem = z.output<typeof publishedItemSchema>;
export type PublishedOption = z.infer<typeof publishedOptionSchema>;
export const publishedVersionSchema = z.object({
  instrumentCode: z.string(),
  kind: z.enum(['household_assessment', 'family_voices']),
  name: textSchema,
  description: textSchema,
  versionId: id,
  versionNumber: int,
  status: z.enum(['draft', 'approved', 'retired']),
  effectiveFrom: timestamp.nullable(),
  effectiveTo: timestamp.nullable(),
  sections: z.array(
    z.object({
      id,
      code: z.string(),
      displayOrder: int,
      title: textSchema,
      items: z.array(publishedItemSchema),
    }),
  ),
});
export type PublishedVersion = z.infer<typeof publishedVersionSchema>;

// Sessions
export const sessionSchema = z.object({
  id,
  assignmentId: id,
  memberId: id,
  instrumentVersionId: id,
  versionNumber: int,
  sectionCode: z.string().nullable(),
  status: sessionStatusSchema,
  startedAt: timestamp,
  submittedAt: timestamp.nullable(),
  scoredAt: timestamp.nullable(),
  answeredCount: int,
  requiredCount: int,
});
export type Session = z.infer<typeof sessionSchema>;

// AnswerResponse
export const answerSchema = z.object({ itemId: id, optionId: id, answeredAt: timestamp, lastModifiedAt: timestamp });

export const sessionAnswersSchema = z.object({
  sessionId: id,
  status: sessionStatusSchema,
  responses: z.array(answerSchema),
});
export type SessionAnswers = z.infer<typeof sessionAnswersSchema>;

export interface AnswerRequest {
  itemId: string;
  optionId: string;
}

/** The result a demonstration asks the server to aim for (D-130). */
export type DemoLevel = 'low' | 'medium' | 'high';

// POST /api/v1/assignments/demo-check-in: the check-in to answer next, a new round of its own or the one still open.
// Development servers only (D-144).
export const demoCheckInSchema = z.object({
  assignmentId: id,
  activeSessionId: id.nullable(),
  waveCode: z.string().min(1),
  opened: z.boolean(),
});
export type DemoCheckIn = z.infer<typeof demoCheckInSchema>;

export const submissionSchema = z.object({
  sessionId: id,
  outcome: z.enum(['submitted', 'already_submitted']),
  status: sessionStatusSchema,
  submittedAt: timestamp,
});
export type Submission = z.infer<typeof submissionSchema>;

/** One band of a scale, both bounds included, with the rule set's own keys. */
const scoreBandSchema = z.object({ code: z.string(), min: z.number().int(), max: z.number().int() });

/**
 * A grading scale as the approved rule set states it, the same shape on a result (D-111) and on the trend (D-132). The
 * app draws it; it never decides a band from it. A result's scale always has bands; the trend's may have none.
 */
export const scoreScaleSchema = z.object({
  minimum: z.number().int(),
  maximum: z.number().int(),
  bands: z.array(scoreBandSchema).min(1),
});
export type ScoreScale = z.infer<typeof scoreScaleSchema>;

export const sessionResultSchema = z.object({
  sessionId: id,
  sessionStatus: sessionStatusSchema,
  state: resultStateSchema,
  incompleteReason: z.string().nullable(),
  totalScore: z.number().nullable(),
  tierCode: z.string().nullable(),
  categoryScores: z.array(z.object({ categoryCode: z.string(), score: z.number(), itemCount: int })),
  computedAt: timestamp.nullable(),
  // The server sends score values to the household's head only (D-103); everyone else gets the state without them.
  scoreVisibility: z.enum(['visible', 'head_of_household_only']),
  scale: scoreScaleSchema.nullable(),
});
export type SessionResult = z.infer<typeof sessionResultSchema>;

// POST /api/v1/sessions/{id}/report
const reportPointSchema = z.object({ title: z.string().min(1), detail: z.string().min(1) });

const httpsUrl = z.url({ protocol: /^https$/ });

/** Which emirates the app can name; a code it has no words for would fail its translations, so it fails here first. */
const emirateSchema = z.enum(['uae', 'abu_dhabi', 'dubai', 'sharjah', 'ajman', 'umm_al_quwain', 'ras_al_khaimah', 'fujairah']);

const authorityKindSchema = z.enum(['government', 'public_provider', 'private']);

/** A service, word for word from the knowledge graph (D-115). An empty procedure means no source publishes one. */
const reportServiceSchema = z.object({
  code: z.string().min(1),
  name: z.string().min(1),
  summary: z.string().min(1),
  need: z.string().min(1),
  procedure: z.array(z.string().min(1)),
  documents: z.array(z.string().min(1)),
  website: httpsUrl.nullable(),
  via: z.string().min(1).nullable(),
});
export type ReportService = z.infer<typeof reportServiceSchema>;

/** An authority and the services reached for this family, from the knowledge graph. A model may pick but wrote none of it. */
const reportAuthoritySchema = z.object({
  code: z.string().min(1),
  kind: authorityKindSchema,
  name: z.string().min(1),
  purpose: z.string().min(1),
  emirates: z.array(emirateSchema).min(1),
  services: z.array(reportServiceSchema).min(1),
  website: httpsUrl.nullable(),
  phone: z.string().min(1).nullable(),
  checkedOn: z.iso.date(),
});
export type ReportAuthority = z.infer<typeof reportAuthoritySchema>;

/** One step of a family's flow: where and why, what to start at home, then where to go. `standing` only orders the steps. */
const reportNextStepSchema = z.object({
  sectionCode: z.string(),
  area: z.string().min(1),
  reason: z.string().min(1),
  steps: z.array(z.string().min(1)).min(1),
  standing: z.number(),
  authorities: z.array(reportAuthoritySchema),
});
export type ReportNextStep = z.infer<typeof reportNextStepSchema>;

/**
 * A written report on a recorded score (D-112). The total, band and scale are the platform's record; the rest is a
 * model's text, named by `modelName`, which nothing acts on. It is kept on the phone; the server holds no copy.
 */
export const scoreReportSchema = z.object({
  sessionId: id,
  language: z.enum(['en', 'ar']),
  totalScore: z.number(),
  tierCode: z.string(),
  scale: scoreScaleSchema,
  summary: z.string().min(1),
  strengths: z.array(reportPointSchema),
  weaknesses: z.array(reportPointSchema),
  nextSteps: z.array(reportNextStepSchema),
  modelName: z.string().min(1),
  generatedAt: timestamp,
});
export type ScoreReport = z.infer<typeof scoreReportSchema>;

// POST /api/v1/sessions/{id}/report/parts/{sectionCode}: one area's text, written as the person answers (D-113).
export const scoreReportPartSchema = z.object({
  sectionCode: z.string().min(1),
  language: z.enum(['en', 'ar']),
  strengths: scoreReportSchema.shape.strengths,
  weaknesses: scoreReportSchema.shape.weaknesses,
  nextSteps: scoreReportSchema.shape.nextSteps,
  modelName: z.string().min(1),
  generatedAt: timestamp,
  /** Where the area stands, 0 to 1 (D-145): the report keeps the strongest and weakest areas by it. Never shown. */
  standing: z.number().min(0).max(1),
});
export type ScoreReportPart = z.infer<typeof scoreReportPartSchema>;

// POST /api/v1/sessions/{id}/report/summary
export const scoreReportSummarySchema = z.object({
  language: z.enum(['en', 'ar']),
  summary: z.string().min(1),
  modelName: z.string().min(1),
  generatedAt: timestamp,
});
export type ScoreReportSummary = z.infer<typeof scoreReportSummarySchema>;

// GET /api/v1/sessions/{id}/report/eligibility
export const scoreReportEligibilitySchema = z.object({ eligible: z.boolean() });
export type ScoreReportEligibility = z.infer<typeof scoreReportEligibilitySchema>;

// GET /api/v1/me/assessment-history
export const historyItemSchema = z.object({
  sessionId: id,
  assignmentId: id,
  memberId: id,
  instrumentCode: z.string(),
  versionNumber: int,
  sectionCode: z.string().nullable(),
  waveCode: z.string(),
  status: sessionStatusSchema,
  startedAt: timestamp,
  submittedAt: timestamp.nullable(),
  scoreStatus: z.enum(['complete', 'incomplete']).nullable(),
  tierCode: z.string().nullable(),
});
export type HistoryItem = z.infer<typeof historyItemSchema>;
export const historyPageSchema = paged(historyItemSchema);

// GET /api/v1/me/resilience-trend (D-132): the household assessment's recorded totals, oldest first, on the scale of the
// latest one, for the household's head only (D-103). The app draws them as sent; it computes no score, band or change.
export const resilienceTrendSchema = z
  .object({
    scoreVisibility: z.enum(['visible', 'head_of_household_only']),
    scale: scoreScaleSchema
      .extend({ bands: z.array(scoreBandSchema) })
      .refine((scale) => scale.minimum < scale.maximum, { message: 'The scale must run upwards.' })
      .nullable(),
    points: z.array(
      z.object({
        sessionId: id,
        waveCode: z.string(),
        submittedAt: timestamp,
        totalScore: z.number(),
        tierCode: z.string().nullable(),
        segment: int.nonnegative(),
      }),
    ),
    notShownCount: int.nonnegative(),
  })
  .refine((trend) => trend.points.length === 0 || trend.scale !== null, {
    message: 'Points come with their scale.',
  });
export type ResilienceTrend = z.infer<typeof resilienceTrendSchema>;
export type ResilienceTrendPoint = ResilienceTrend['points'][number];

// GET /api/v1/me/recommendations: support categories an approved referral rule set routed a scored session to.
// Codes only; no participant-facing wording is approved (open question 13), so they are shown only under
// RESULT_DISCLOSURE (decision M-46).
export const recommendationSchema = z.object({
  id,
  memberId: id,
  sessionId: id,
  categoryCode: z.string(),
  createdAt: timestamp,
  referralId: id.nullable(),
  referralStatus: z.enum(['open', 'in_progress', 'resolved', 'cancelled']).nullable(),
});
export type Recommendation = z.infer<typeof recommendationSchema>;
export const recommendationPageSchema = paged(recommendationSchema);

// ReferralResponse (GET /api/v1/me/referrals), as the data export carries it. Codes only; the app shows none of it.
export const referralSchema = z.object({
  id,
  memberId: id,
  sessionId: id,
  categoryCode: z.string(),
  status: z.enum(['open', 'in_progress', 'resolved', 'cancelled']),
  assignedServiceCode: z.string().nullable(),
  resolutionCode: z.string().nullable(),
  createdAt: timestamp,
  updatedAt: timestamp,
  resolvedAt: timestamp.nullable(),
  handOff: z
    .object({
      partnerCode: z.string(),
      partnerNameAr: z.string(),
      partnerNameEn: z.string(),
      method: z.string(),
      handedOffAt: timestamp,
    })
    .nullable(),
});
export type Referral = z.infer<typeof referralSchema>;

// Rights requests (security follow-ups spec section 6): GET and POST /api/v1/me/privacy-requests
export const privacyRequestTypeSchema = z.enum(['access', 'correction', 'erasure']);
export const privacyRequestStatusSchema = z.enum(['open', 'closed']);
export const privacyRequestResolutionSchema = z.enum(['completed', 'declined', 'withdrawn']);
export type PrivacyRequestType = z.infer<typeof privacyRequestTypeSchema>;

export const privacyRequestSchema = z.object({
  id,
  memberId: id,
  type: privacyRequestTypeSchema,
  status: privacyRequestStatusSchema,
  createdAt: timestamp,
  closedAt: timestamp.nullable(),
  resolution: privacyRequestResolutionSchema.nullable(),
});
export type PrivacyRequest = z.infer<typeof privacyRequestSchema>;
export const privacyRequestPageSchema = paged(privacyRequestSchema);
export type PrivacyRequestPage = z.infer<typeof privacyRequestPageSchema>;

/** CreatePrivacyRequestRequest. No free text: details go through support, outside the platform. */
export interface CreatePrivacyRequest {
  type: PrivacyRequestType;
  /** Default: the caller's own member. Any other must be one the caller may act for (D-49). */
  memberId?: string;
}

// GET /api/v1/me/data-export: what the platform holds about the members the caller may act for (D-49).
export const myDataExportSchema = z.object({
  generatedAt: timestamp,
  subject: z.string(),
  household: myHouseholdSchema.nullable(),
  members: z.array(memberSchema),
  consents: z.array(consentRecordSchema),
  assignments: z.array(assignmentSchema),
  sessions: z.array(z.object({ session: sessionSchema, answers: z.array(answerSchema) })),
  referrals: z.array(referralSchema),
  privacyRequests: z.array(privacyRequestSchema),
  /** What is left out on purpose, as codes: today `score_results` and `support_recommendations` (D-72). */
  notIncluded: z.array(z.string()),
});
export type MyDataExport = z.infer<typeof myDataExportSchema>;

/**
 * The export as the app received it: `data` validated, and `raw` the body exactly as the server sent it (after it
 * passed validation), which the person's copy is written from so that no field is stripped.
 */
export interface MyDataExportDownload {
  data: MyDataExport;
  raw: unknown;
}

// GET /api/v1/assistant/availability: whether a model is connected for each AI helper (D-108, D-109).
export const assistantAvailabilitySchema = z.object({ assistant: z.boolean(), consultant: z.boolean(), scoreReport: z.boolean() });
export type AssistantAvailability = z.infer<typeof assistantAvailabilitySchema>;

export type AssistantRole = 'user' | 'assistant';

/** Where a message was said: typed in the chat, or spoken in a call with the consultant (D-123). */
export type AssistantChannel = 'chat' | 'voice';

export interface AssistantMessage {
  role: AssistantRole;
  text: string;
  /** The server reads a missing channel as `chat`. */
  channel?: AssistantChannel;
}

/** POST /api/v1/assistant/replies v2: the rolling memory and the latest messages, the last the person's (D-123). */
export interface AssistantReplyRequest {
  language: 'en' | 'ar';
  /** The summary the server wrote earlier, sent back as it came; left out when there is none. */
  memory?: string | null;
  messages: readonly AssistantMessage[];
}

/** The app screens the assistant and the consultant may suggest (D-122); the app shows its own label for each. */
export const ASSISTANT_ACTIONS = ['sos', 'services', 'home', 'resilience', 'help', 'consultant', 'assistant'] as const;
export const assistantActionSchema = z.enum(ASSISTANT_ACTIONS);
export type AssistantAction = z.infer<typeof assistantActionSchema>;

/** A reply names at most this many actions and sources (D-122). */
const MAX_REPLY_EXTRAS = 3;

/**
 * The known codes, each once, at most three. A code this app does not know (a newer server) is dropped rather than
 * failing the whole reply.
 */
export const assistantActionsSchema = z
  .array(z.unknown())
  .transform((codes) =>
    [...new Set(codes.flatMap((code) => {
      const parsed = assistantActionSchema.safeParse(code);
      return parsed.success ? [parsed.data] : [];
    }))].slice(0, MAX_REPLY_EXTRAS),
  );

/** An entry of the approved directory that the reply names (D-122), shown as a card that opens its https link. */
export const assistantSourceSchema = z.object({
  id: z.string().min(1).max(100),
  name: z.string().trim().min(1).max(300),
  provider: z.string().trim().max(300),
  url: z.url({ protocol: /^https$/ }),
});
export type AssistantSource = z.infer<typeof assistantSourceSchema>;

/** Valid entries only, at most three: an entry without an https link is left out, never shown. */
export const assistantSourcesSchema = z
  .array(z.unknown())
  .transform((entries) =>
    entries
      .flatMap((entry) => {
        const parsed = assistantSourceSchema.safeParse(entry);
        return parsed.success ? [parsed.data] : [];
      })
      .slice(0, MAX_REPLY_EXTRAS),
  );

/**
 * The safety notice (D-135): any string the server sends is read as `crisis_support`, so a value added later still shows
 * help; null or absent means none. The app shows its own words, never the server's.
 */
export const safetyNoticeSchema = z.string().transform((): 'crisis_support' => 'crisis_support');
export type SafetyNotice = 'crisis_support';

export const assistantReplySchema = z.object({
  reply: z.string().min(1),
  generatedAt: timestamp,
  actions: assistantActionsSchema.default([]),
  sources: assistantSourcesSchema.default([]),
  /** A refreshed summary that replaces the one kept on the phone; null or absent when it was not refreshed. */
  memory: z.string().nullable().optional(),
  /** `crisis_support` when the server's safety screen matched; older servers leave it out. */
  safetyNotice: safetyNoticeSchema.nullable().optional(),
});
export type AssistantReply = z.infer<typeof assistantReplySchema>;

/** One approved entry of the support directory (D-92), in both languages; the app shows the person's language. */
export const supportResourceSchema = z.object({
  id: z.string(),
  code: z.string(),
  versionNumber: int,
  nameAr: z.string(),
  nameEn: z.string(),
  summaryAr: z.string(),
  summaryEn: z.string(),
  providerNameAr: z.string(),
  providerNameEn: z.string(),
  url: z.url({ protocol: /^https$/ }),
  categories: z.array(z.string()),
  audiences: z.array(z.string()),
  serviceLanguages: z.array(z.string()),
});
export type SupportResource = z.infer<typeof supportResourceSchema>;

// GET /api/v1/support-resources: the approved, findable entries, a page at a time (D-110).
export const supportResourcePageSchema = paged(supportResourceSchema);

const bilingualText = z.object({ en: z.string().min(1), ar: z.string().min(1) });

// GET /api/v1/support-authorities: the organisations in the knowledge graph, the same for everyone (D-115).
export const supportAuthoritySchema = z.object({
  code: z.string().min(1),
  kind: authorityKindSchema,
  name: bilingualText,
  purpose: bilingualText,
  emirates: z.array(emirateSchema).min(1),
  website: httpsUrl.nullable(),
  phone: z.string().min(1).nullable(),
  services: z.array(
    z.object({
      code: z.string().min(1),
      name: bilingualText,
      summary: bilingualText,
      procedure: z.array(bilingualText),
      documents: z.array(bilingualText),
      website: httpsUrl.nullable(),
      via: bilingualText.nullable(),
    }),
  ),
  checkedOn: z.iso.date(),
});
export type SupportAuthority = z.infer<typeof supportAuthoritySchema>;

export const supportAuthorityListSchema = z.array(supportAuthoritySchema);

// POST /api/v1/support-resources/search: matches carry their rank; every answer is information only (D-93).
/** What the app keeps of a search: the entries and whether the server's safety screen matched the text. */
export interface SupportSearchResult {
  resources: SupportResource[];
  safetyNotice: SafetyNotice | null;
}

export const supportResourceSearchSchema = z.object({
  outcome: z.enum(['matched', 'no_match']),
  notice: z.literal('information_only'),
  matches: z.array(supportResourceSchema.extend({ rank: int })),
  safetyNotice: safetyNoticeSchema.nullable().optional(),
});
