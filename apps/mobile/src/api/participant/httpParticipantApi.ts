import type { ZodType } from 'zod';

import type { AuthProvider } from '@/auth/types';

import { ApiError, isApiError, requestJsonWithBody, type FetchLike, type HttpMethod } from '../client';
import { PRIVACY_REQUEST_PAGE_SIZE, SUPPORT_SEARCH_LIMIT, type ParticipantApi } from './participantApi';
import {
  assignmentPageSchema,
  assignmentSchema,
  assistantAvailabilitySchema,
  assistantReplySchema,
  consentRecordPageSchema,
  consentRecordSchema,
  currentConsentPoliciesSchema,
  demoCheckInSchema,
  historyPageSchema,
  householdSchema,
  memberPageSchema,
  memberSchema,
  meSchema,
  myDataExportSchema,
  privacyRequestPageSchema,
  privacyRequestSchema,
  publishedVersionSchema,
  recommendationPageSchema,
  registrationSchema,
  resilienceTrendSchema,
  rosterConfirmationSchema,
  scoreReportEligibilitySchema,
  scoreReportPartSchema,
  scoreReportSchema,
  scoreReportSummarySchema,
  sessionAnswersSchema,
  sessionResultSchema,
  sessionSchema,
  submissionSchema,
  supportAuthorityListSchema,
  supportResourcePageSchema,
  supportResourceSearchSchema,
} from './schemas';

const PAGE_SIZE = 100;
/** Ten pages of 100 is far beyond any household; stop rather than loop on a server bug. */
const MAX_PAGES = 10;
/** The server gives the model 90 seconds to write a report; the phone waits a little longer to hear how it went. */
export const SCORE_REPORT_TIMEOUT_MS = 100_000;
/** The server gives the model 30 seconds for one area or a summary. */
export const SCORE_REPORT_PART_TIMEOUT_MS = 35_000;

interface Page<T> {
  items: T[];
  totalCount: number;
}

export interface HttpParticipantApiOptions {
  baseUrl: string | undefined;
  auth: Pick<AuthProvider, 'getAccessToken' | 'reportUnauthorized'>;
  fetchImpl?: FetchLike;
}

const segment = encodeURIComponent;

/**
 * The server may spend about 40 s before it answers a reply with the safety notice (recording, a 30 s model time-out,
 * recording), so the app waits longer than its usual 15 s for this one call.
 */
const ASSISTANT_REPLY_TIMEOUT_MS = 45_000;

/** The participant endpoints of the platform API (`/api/v1`), with the signed-in person's bearer token. */
export function createHttpParticipantApi({ baseUrl, auth, fetchImpl }: HttpParticipantApiOptions): ParticipantApi {
  async function callWithBody<T>(
    method: HttpMethod,
    path: string,
    schema: ZodType<T>,
    body?: unknown,
    headers?: Record<string, string>,
    timeoutMs?: number,
  ): Promise<{ data: T; raw: unknown }> {
    if (!baseUrl || !/^https?:\/\//i.test(baseUrl)) {
      throw new ApiError('not_configured');
    }
    try {
      return await requestJsonWithBody({
        baseUrl,
        method,
        path: `/api/v1${path}`,
        schema,
        body,
        headers,
        timeoutMs,
        accessToken: await auth.getAccessToken(),
        fetchImpl,
      });
    } catch (error) {
      if (isApiError(error) && error.kind === 'unauthorized') {
        auth.reportUnauthorized();
      }
      throw error;
    }
  }

  const call = async <T,>(
    method: HttpMethod,
    path: string,
    schema: ZodType<T>,
    body?: unknown,
    headers?: Record<string, string>,
    timeoutMs?: number,
  ): Promise<T> => (await callWithBody(method, path, schema, body, headers, timeoutMs)).data;

  async function all<T>(path: string, schema: ZodType<Page<T>>): Promise<T[]> {
    const items: T[] = [];
    for (let page = 1; page <= MAX_PAGES; page += 1) {
      const separator = path.includes('?') ? '&' : '?';
      const result = await call('GET', `${path}${separator}page=${page}&pageSize=${PAGE_SIZE}`, schema);
      items.push(...result.items);
      if (result.items.length === 0 || items.length >= result.totalCount) {
        break;
      }
    }
    return items;
  }

  return {
    getMe: () => call('GET', '/me', meSchema),
    registerMe: () => call('POST', '/me/registration', registrationSchema, { detailsConfirmed: true }),

    createHousehold: (request) => call('POST', '/households', householdSchema, request),
    getHousehold: (householdId) => call('GET', `/households/${segment(householdId)}`, householdSchema),
    listMembers: (householdId) => all(`/households/${segment(householdId)}/members`, memberPageSchema),
    addMember: (householdId, details) =>
      call('POST', `/households/${segment(householdId)}/members`, memberSchema, details),
    updateMember: (householdId, memberId, patch) =>
      call('PATCH', `/households/${segment(householdId)}/members/${segment(memberId)}`, memberSchema, patch),
    removeMember: (householdId, memberId) =>
      call('PATCH', `/households/${segment(householdId)}/members/${segment(memberId)}`, memberSchema, {
        status: 'inactive',
      }),
    confirmRoster: (householdId) =>
      call('POST', `/households/${segment(householdId)}/confirm-roster`, rosterConfirmationSchema),

    getCurrentConsentPolicies: async () =>
      (await call('GET', '/consent/policies/current', currentConsentPoliciesSchema)).policies,
    listMyConsents: () => all('/me/consents', consentRecordPageSchema),
    recordConsent: (request) => call('POST', '/consent', consentRecordSchema, request),

    listAssignments: () => all('/assignments', assignmentPageSchema),
    getAssignment: (assignmentId) => call('GET', `/assignments/${segment(assignmentId)}`, assignmentSchema),
    startSession: (assignmentId) => call('POST', `/assignments/${segment(assignmentId)}/sessions`, sessionSchema),
    getPublishedVersion: (instrumentCode, versionNumber) =>
      call('GET', `/questionnaires/${segment(instrumentCode)}/versions/${versionNumber}`, publishedVersionSchema),

    getSession: (sessionId) => call('GET', `/sessions/${segment(sessionId)}`, sessionSchema),
    getAnswers: (sessionId) => call('GET', `/sessions/${segment(sessionId)}/responses`, sessionAnswersSchema),
    saveAnswers: (sessionId, answers) =>
      call('PUT', `/sessions/${segment(sessionId)}/responses`, sessionAnswersSchema, { responses: answers }),
    submitSession: (sessionId, idempotencyKey) =>
      call('POST', `/sessions/${segment(sessionId)}/submit`, submissionSchema, undefined, {
        'Idempotency-Key': idempotencyKey,
      }),
    getResult: (sessionId) => call('GET', `/sessions/${segment(sessionId)}/result`, sessionResultSchema),
    submitDemoAnswers: (sessionId, level, idempotencyKey) =>
      call('POST', `/sessions/${segment(sessionId)}/demo-submission`, submissionSchema, { level }, {
        'Idempotency-Key': idempotencyKey,
      }),
    openDemoCheckIn: () => call('POST', '/assignments/demo-check-in', demoCheckInSchema, {}),
    listHistory: () => all('/me/assessment-history', historyPageSchema),
    getResilienceTrend: () => call('GET', '/me/resilience-trend', resilienceTrendSchema),
    listRecommendations: () => all('/me/recommendations', recommendationPageSchema),

    exportMyData: () => callWithBody('GET', '/me/data-export', myDataExportSchema),
    listPrivacyRequests: (page) =>
      call(
        'GET',
        `/me/privacy-requests?page=${Math.max(1, Math.trunc(page))}&pageSize=${PRIVACY_REQUEST_PAGE_SIZE}`,
        privacyRequestPageSchema,
      ),
    createPrivacyRequest: ({ type, memberId }) =>
      call('POST', '/me/privacy-requests', privacyRequestSchema, memberId === undefined ? { type } : { type, memberId }),

    getAssistantAvailability: () => call('GET', '/assistant/availability', assistantAvailabilitySchema),
    createScoreReport: async (sessionId, language) =>
      (
        await callWithBody(
          'POST',
          `/sessions/${segment(sessionId)}/report`,
          scoreReportSchema,
          { language },
          undefined,
          SCORE_REPORT_TIMEOUT_MS,
        )
      ).data,
    getScoreReportEligibility: (sessionId) =>
      call('GET', `/sessions/${segment(sessionId)}/report/eligibility`, scoreReportEligibilitySchema),
    createScoreReportPart: async (sessionId, sectionCode, language) =>
      (
        await callWithBody(
          'POST',
          `/sessions/${segment(sessionId)}/report/parts/${segment(sectionCode)}`,
          scoreReportPartSchema,
          { language },
          undefined,
          SCORE_REPORT_PART_TIMEOUT_MS,
        )
      ).data,
    createScoreReportSummary: async (sessionId, language) =>
      (
        await callWithBody(
          'POST',
          `/sessions/${segment(sessionId)}/report/summary`,
          scoreReportSummarySchema,
          { language },
          undefined,
          SCORE_REPORT_PART_TIMEOUT_MS,
        )
      ).data,
    requestAssistantReply: ({ language, memory, messages }) =>
      call(
        'POST',
        '/assistant/replies',
        assistantReplySchema,
        memory ? { language, memory, messages } : { language, messages },
        undefined,
        ASSISTANT_REPLY_TIMEOUT_MS,
      ),
    listSupportResources: () => all('/support-resources', supportResourcePageSchema),
    listSupportAuthorities: () => call('GET', '/support-authorities', supportAuthorityListSchema),
    searchSupportResources: async (text) => {
      const found = await call('POST', '/support-resources/search', supportResourceSearchSchema, {
        text,
        limit: SUPPORT_SEARCH_LIMIT,
      });
      return { resources: found.matches, safetyNotice: found.safetyNotice ?? null };
    },
  };
}
