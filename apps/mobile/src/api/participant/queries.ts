import { skipToken, useInfiniteQuery, useQuery, type QueryClient } from '@tanstack/react-query';

import { useParticipantApi } from './ParticipantApiContext';
import type { RosterConfirmation } from './schemas';

/** Query keys; everything participant-specific sits under one prefix so signing out can drop it at once. */
export const participantKeys = {
  all: ['participant'] as const,
  me: () => [...participantKeys.all, 'me'] as const,
  household: (householdId: string) => [...participantKeys.all, 'household', householdId] as const,
  members: (householdId: string) => [...participantKeys.all, 'members', householdId] as const,
  rosterConfirmation: (householdId: string) => [...participantKeys.all, 'roster-confirmation', householdId] as const,
  consentPolicies: () => [...participantKeys.all, 'consent-policies'] as const,
  consents: () => [...participantKeys.all, 'consents'] as const,
  assignments: () => [...participantKeys.all, 'assignments'] as const,
  assignment: (assignmentId: string) => [...participantKeys.all, 'assignment', assignmentId] as const,
  version: (instrumentCode: string, versionNumber: number) =>
    [...participantKeys.all, 'version', instrumentCode, versionNumber] as const,
  session: (sessionId: string) => [...participantKeys.all, 'session', sessionId] as const,
  answers: (sessionId: string) => [...participantKeys.all, 'answers', sessionId] as const,
  reportEligibility: (sessionId: string) => [...participantKeys.all, 'report-eligibility', sessionId] as const,
  reportChoice: (sessionId: string) => [...participantKeys.all, 'report-choice', sessionId] as const,
  result: (sessionId: string) => [...participantKeys.all, 'result', sessionId] as const,
  history: () => [...participantKeys.all, 'history'] as const,
  resilienceTrend: () => [...participantKeys.all, 'resilience-trend'] as const,
  recommendations: () => [...participantKeys.all, 'recommendations'] as const,
  privacyRequests: () => [...participantKeys.all, 'privacy-requests'] as const,
  assistantAvailability: () => [...participantKeys.all, 'assistant-availability'] as const,
  supportResources: () => [...participantKeys.all, 'support-resources'] as const,
  supportAuthorities: () => [...participantKeys.all, 'support-authorities'] as const,
  supportSearch: (text: string) => [...participantKeys.all, 'support-search', text] as const,
  /** The copy of a session kept on this phone (decision M-38): local, but dropped with everything else at sign-out. */
  localCopy: (sessionId: string) => [...participantKeys.all, 'local-copy', sessionId] as const,
  /** Reports sealed on this phone (D-113), read for Home's next steps (D-146); local like the copy above. */
  savedReports: () => [...participantKeys.all, 'saved-report'] as const,
  savedReport: (sessionId: string, language: string) => [...participantKeys.savedReports(), sessionId, language] as const,
};

/**
 * Refetches the participant data a change can affect. Published questionnaire versions are left alone: approved
 * content never changes (D-25), and it is the largest response.
 */
export function invalidateParticipantData(queryClient: QueryClient): Promise<void> {
  return queryClient.invalidateQueries({
    queryKey: participantKeys.all,
    predicate: (query) => query.queryKey[1] !== 'version',
  });
}

export function useMe({ enabled = true }: { enabled?: boolean } = {}) {
  const api = useParticipantApi();
  return useQuery({ queryKey: participantKeys.me(), queryFn: () => api.getMe(), enabled });
}

export function useHousehold(householdId: string) {
  const api = useParticipantApi();
  return useQuery({ queryKey: participantKeys.household(householdId), queryFn: () => api.getHousehold(householdId) });
}

export function useMembers(householdId: string) {
  const api = useParticipantApi();
  return useQuery({ queryKey: participantKeys.members(householdId), queryFn: () => api.listMembers(householdId) });
}

/**
 * The server's answer to the last roster confirmation made on this device, kept in the cache by the confirm mutation.
 * There is no endpoint to read it again, so it is never fetched: after a restart, screens use the assignment list.
 */
export function useRosterConfirmation(householdId: string) {
  return useQuery<RosterConfirmation>({
    queryKey: participantKeys.rosterConfirmation(householdId),
    queryFn: skipToken,
  });
}

export function useConsentPolicies() {
  const api = useParticipantApi();
  return useQuery({ queryKey: participantKeys.consentPolicies(), queryFn: () => api.getCurrentConsentPolicies() });
}

export function useMyConsents() {
  const api = useParticipantApi();
  return useQuery({ queryKey: participantKeys.consents(), queryFn: () => api.listMyConsents() });
}

export function useAssignments() {
  const api = useParticipantApi();
  return useQuery({ queryKey: participantKeys.assignments(), queryFn: () => api.listAssignments() });
}

export function useAssignment(assignmentId: string | undefined) {
  const api = useParticipantApi();
  return useQuery({
    queryKey: participantKeys.assignment(assignmentId ?? ''),
    queryFn: () => api.getAssignment(assignmentId ?? ''),
    enabled: Boolean(assignmentId),
  });
}

/** Approved and retired versions never change (D-25), so they are never refetched. */
export function usePublishedVersion(instrumentCode: string | undefined, versionNumber: number | undefined) {
  const api = useParticipantApi();
  return useQuery({
    queryKey: participantKeys.version(instrumentCode ?? '', versionNumber ?? 0),
    queryFn: () => api.getPublishedVersion(instrumentCode ?? '', versionNumber ?? 0),
    enabled: Boolean(instrumentCode) && versionNumber !== undefined,
    staleTime: Infinity,
  });
}

export function useSession(sessionId: string) {
  const api = useParticipantApi();
  return useQuery({ queryKey: participantKeys.session(sessionId), queryFn: () => api.getSession(sessionId) });
}

export function useSessionAnswers(sessionId: string) {
  const api = useParticipantApi();
  return useQuery({ queryKey: participantKeys.answers(sessionId), queryFn: () => api.getAnswers(sessionId) });
}

export function useSessionResult(sessionId: string) {
  const api = useParticipantApi();
  return useQuery({ queryKey: participantKeys.result(sessionId), queryFn: () => api.getResult(sessionId) });
}

export function useRecommendations(enabled: boolean) {
  const api = useParticipantApi();
  return useQuery({ queryKey: participantKeys.recommendations(), queryFn: () => api.listRecommendations(), enabled });
}

/** Whether a model is connected for the assistant and the consultant; checked again after a minute. */
export function useAssistantAvailability() {
  const api = useParticipantApi();
  return useQuery({
    queryKey: participantKeys.assistantAvailability(),
    queryFn: () => api.getAssistantAvailability(),
    staleTime: 60_000,
  });
}

/** The approved support directory (D-110). */
export function useSupportResources() {
  const api = useParticipantApi();
  return useQuery({
    queryKey: participantKeys.supportResources(),
    queryFn: () => api.listSupportResources(),
    staleTime: 5 * 60_000,
  });
}

/** The organisations in the support knowledge graph (D-115). Search runs on the phone: there are few and they change rarely. */
export function useSupportAuthorities() {
  const api = useParticipantApi();
  return useQuery({
    queryKey: participantKeys.supportAuthorities(),
    queryFn: () => api.listSupportAuthorities(),
    staleTime: 60 * 60_000,
  });
}

/** The directory's search for `text`; off while `text` is empty. */
export function useSupportSearch(text: string) {
  const api = useParticipantApi();
  return useQuery({
    queryKey: participantKeys.supportSearch(text),
    queryFn: () => api.searchSupportResources(text),
    enabled: text.length > 0,
    staleTime: 5 * 60_000,
  });
}

export function useHistory() {
  const api = useParticipantApi();
  return useQuery({ queryKey: participantKeys.history(), queryFn: () => api.listHistory() });
}

/**
 * The family's recorded totals over time (D-132); refetched with everything else after a submission. Home reads it too,
 * for the latest result's next steps (D-146).
 */
export function useResilienceTrend(enabled = true) {
  const api = useParticipantApi();
  return useQuery({ queryKey: participantKeys.resilienceTrend(), queryFn: () => api.getResilienceTrend(), enabled });
}

/** The caller's privacy requests, newest first, a page at a time. */
export function usePrivacyRequests(enabled = true) {
  const api = useParticipantApi();
  return useInfiniteQuery({
    queryKey: participantKeys.privacyRequests(),
    queryFn: ({ pageParam }) => api.listPrivacyRequests(pageParam),
    initialPageParam: 1,
    // The server's page size, not the one asked for: it may answer with fewer per page.
    getNextPageParam: (last) =>
      last.items.length > 0 && last.page * last.pageSize < last.totalCount ? last.page + 1 : undefined,
    enabled,
  });
}
