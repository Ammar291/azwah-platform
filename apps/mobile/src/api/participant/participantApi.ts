import type {
  AnswerRequest,
  Assignment,
  AssistantAvailability,
  ScoreReport,
  ScoreReportEligibility,
  ScoreReportPart,
  ScoreReportSummary,
  AssistantReply,
  AssistantReplyRequest,
  ConsentPolicy,
  ConsentRecord,
  CreateHouseholdRequest,
  CreatePrivacyRequest,
  DemoCheckIn,
  DemoLevel,
  HistoryItem,
  Household,
  Me,
  Member,
  MemberDetails,
  MemberPatch,
  MyDataExportDownload,
  PrivacyRequest,
  PrivacyRequestPage,
  PublishedVersion,
  Recommendation,
  RecordConsentRequest,
  ResilienceTrend,
  Registration,
  RosterConfirmation,
  Session,
  SessionAnswers,
  SessionResult,
  Submission,
  SupportAuthority,
  SupportResource,
  SupportSearchResult,
} from './schemas';

/**
 * Everything the participant screens ask of the platform. Two implementations: createHttpParticipantApi (the real
 * API) and createFixtureParticipantApi (src/dev-fixtures, development only). Eligibility, scoring and access are
 * always decided by whoever is behind this interface, never by the screens.
 */
export interface ParticipantApi {
  getMe(): Promise<Me>;
  /**
   * The person confirmed the details their identity provider released; the server registers them (once; repeating is
   * harmless). Nothing about the details themselves is sent (D-97).
   */
  registerMe(): Promise<Registration>;

  createHousehold(request: CreateHouseholdRequest): Promise<Household>;
  getHousehold(householdId: string): Promise<Household>;
  listMembers(householdId: string): Promise<Member[]>;
  addMember(householdId: string, details: MemberDetails): Promise<Member>;
  /** Merge-patches an active member. Any roster change clears the household's confirmation (D-50). */
  updateMember(householdId: string, memberId: string, patch: MemberPatch): Promise<Member>;
  /** Takes a member off the roster (`{"status": "inactive"}`); their history is kept. The manager cannot be removed. */
  removeMember(householdId: string, memberId: string): Promise<Member>;
  /** Confirms the roster; the server then assigns this round's questionnaires from approved rules. */
  confirmRoster(householdId: string): Promise<RosterConfirmation>;

  getCurrentConsentPolicies(): Promise<ConsentPolicy[]>;
  listMyConsents(): Promise<ConsentRecord[]>;
  recordConsent(request: RecordConsentRequest): Promise<ConsentRecord>;

  listAssignments(): Promise<Assignment[]>;
  getAssignment(assignmentId: string): Promise<Assignment>;
  startSession(assignmentId: string): Promise<Session>;
  getPublishedVersion(instrumentCode: string, versionNumber: number): Promise<PublishedVersion>;

  getSession(sessionId: string): Promise<Session>;
  getAnswers(sessionId: string): Promise<SessionAnswers>;
  saveAnswers(sessionId: string, answers: readonly AnswerRequest[]): Promise<SessionAnswers>;
  /** The key identifies one submission; send the same key when retrying it (D-54). */
  submitSession(sessionId: string, idempotencyKey: string): Promise<Submission>;
  getResult(sessionId: string): Promise<SessionResult>;
  /**
   * DEMONSTRATIONS ONLY (D-130, D-143): the server answers every question as its approved scoring rules score the level,
   * saves the answers, then submits and scores the session, as a submission does. The key works as a submission's (D-54).
   * 404 unless the server's demo tools are on, 409 when no approved rules score the session or it is no longer open.
   */
  submitDemoAnswers(sessionId: string, level: DemoLevel, idempotencyKey: string): Promise<Submission>;
  /**
   * DEMONSTRATIONS ONLY (D-144): a new check-in round for the family at once, assigned by the server's approved rules, or
   * the check-in still open. 404 unless the server's demo tools are on.
   */
  openDemoCheckIn(): Promise<DemoCheckIn>;
  listHistory(): Promise<HistoryItem[]>;
  /** The household assessment's recorded totals over time, for the head of household only (D-103, D-132). */
  getResilienceTrend(): Promise<ResilienceTrend>;
  listRecommendations(): Promise<Recommendation[]>;

  /** Everything the platform holds about the members the caller may act for (rate limited: 10 an hour). */
  exportMyData(): Promise<MyDataExportDownload>;
  /** One page (from 1) of the requests about members the caller may act for, newest first. */
  listPrivacyRequests(page: number): Promise<PrivacyRequestPage>;
  /** Asks for access, correction or erasure; one open request per member and type. */
  createPrivacyRequest(request: CreatePrivacyRequest): Promise<PrivacyRequest>;

  /** Whether a model is connected for the assistant, the consultant and the score report (D-108, D-109, D-112). */
  getAssistantAvailability(): Promise<AssistantAvailability>;
  /**
   * A written report on a session's recorded score, for the household's head (D-112). It sends the session's answers
   * to a model, so only a person's own tap asks for it. 503 when no model is connected (rate limited: 10 an hour).
   */
  createScoreReport(sessionId: string, language: 'en' | 'ar'): Promise<ScoreReport>;
  /** Whether a report can be written as this session is answered (D-113). Sends nothing. */
  getScoreReportEligibility(sessionId: string): Promise<ScoreReportEligibility>;
  /**
   * One fully answered area's text, from the answers saved on the server (D-113). It sends those answers to a model, so
   * it runs only after the person agreed. 409 until the area is answered (rate limited: 120 an hour, with the summary).
   */
  createScoreReportPart(sessionId: string, sectionCode: string, language: 'en' | 'ar'): Promise<ScoreReportPart>;
  /** The report's summary, from the answers saved on the server, under the same terms (D-113). */
  createScoreReportSummary(sessionId: string, language: 'en' | 'ar'): Promise<ScoreReportSummary>;
  /**
   * The assistant's reply to the conversation so far (protocol v2, D-122, D-123): the text, the app screens and
   * directory entries it suggests, and a refreshed memory when the conversation is long. The server adds what it holds
   * about the family and keeps nothing; 503 when no model is connected (rate limited: 20 a minute).
   */
  requestAssistantReply(request: AssistantReplyRequest): Promise<AssistantReply>;
  /** The approved support directory (D-92, D-110), every page up to a sane limit. */
  listSupportResources(): Promise<SupportResource[]>;
  /** GET /api/v1/support-authorities: the organisations in the knowledge graph (D-115). */
  listSupportAuthorities(): Promise<SupportAuthority[]>;
  /** The directory's own search for what the person typed (D-93; rate limited: 30 a minute). */
  searchSupportResources(text: string): Promise<SupportSearchResult>;
}

/** PUT /sessions/{id}/responses accepts 1 to 200 answers per request. */
export const MAX_ANSWERS_PER_SAVE = 200;

/** Privacy requests are read this many at a time. */
export const PRIVACY_REQUEST_PAGE_SIZE = 20;

/**
 * The server's limits on an assistant request and on the start of a call (D-123): the app sends at most this many of
 * the latest messages, each at most 4,000 characters, 20,000 in all, and a memory of at most 2,000 characters.
 */
export const ASSISTANT_MAX_MESSAGES = 40;
export const ASSISTANT_MAX_MESSAGE_LENGTH = 4000;
export const ASSISTANT_MAX_TOTAL_LENGTH = 20000;
export const ASSISTANT_MAX_MEMORY_LENGTH = 2000;

/** The directory search returns at most this many matches (the server allows 1 to 10). */
export const SUPPORT_SEARCH_LIMIT = 10;
