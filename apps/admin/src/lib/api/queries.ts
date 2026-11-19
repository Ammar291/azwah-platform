'use client';

import { keepPreviousData, useMutation, useQueries, useQuery, useQueryClient } from '@tanstack/react-query';

import { bffGet, bffPost } from './client';
import { ApiError } from './errors';
import {
  adminPrivacyRequestSchema,
  adminSafetyAlertSchema,
  assessmentRowSchema,
  exportCatalogSchema,
  exportSchema,
  healthSchema,
  householdResultSchema,
  instrumentSchema,
  instrumentVersionSchema,
  instrumentVersionSummarySchema,
  overviewSchema,
  pagedSchema,
  participationRowSchema,
  ruleSetSchema,
  safetyAlertSummarySchema,
  scoreDistributionsSchema,
  waveSchema,
  type Export,
  type PrivacyRequestStatus,
  type PrivacyResolution,
  type SafetyAlertStatus,
  type SafetyConcern,
  type SafetyResolution,
} from './schemas';

/** Query hooks over the BFF. Keys hold every filter, so a change of filter is a new request, never a cached guess. */

export interface OverviewFilters {
  waveId: string | null;
  from: string | null;
  to: string | null;
  interval: 'month' | 'week';
}

export interface AssessmentFilters {
  waveId: string | null;
  instrumentCode: string | null;
  versionNumber: number | null;
  status: string | null;
  assignedFrom: string | null;
  assignedTo: string | null;
  page: number;
  pageSize: number;
}

export interface ParticipationFilters {
  status: string | null;
  rosterConfirmed: boolean | null;
  reference: string | null;
  page: number;
  pageSize: number;
}

const wavesSchema = pagedSchema(waveSchema);

export function useWaves() {
  return useQuery({
    queryKey: ['waves'],
    queryFn: ({ signal }) => bffGet('admin/waves', wavesSchema, { page: 1, pageSize: 100 }, signal),
    staleTime: 5 * 60_000,
  });
}

export function useOverview(filters: OverviewFilters, { enabled = true }: { enabled?: boolean } = {}) {
  return useQuery({
    queryKey: ['overview', filters],
    queryFn: ({ signal }) => bffGet('admin/analytics/overview', overviewSchema, { ...filters }, signal),
    placeholderData: keepPreviousData,
    enabled,
  });
}

export function useScoreDistributions(waveId: string | null) {
  return useQuery({
    queryKey: ['score-distributions', waveId],
    queryFn: ({ signal }) => bffGet('admin/analytics/score-distributions', scoreDistributionsSchema, { waveId }, signal),
  });
}

/** One distribution read per wave, for comparing the same rule set across assessment periods. */
export function useScoreDistributionsByWave(waveIds: string[]) {
  return useQueries({
    queries: waveIds.map((waveId) => ({
      queryKey: ['score-distributions', waveId],
      queryFn: ({ signal }: { signal: AbortSignal }) =>
        bffGet('admin/analytics/score-distributions', scoreDistributionsSchema, { waveId }, signal),
    })),
  });
}

const assessmentsSchema = pagedSchema(assessmentRowSchema);

export function useAssessments(filters: AssessmentFilters) {
  return useQuery({
    queryKey: ['assessments', filters],
    queryFn: ({ signal }) => bffGet('admin/assessments', assessmentsSchema, { ...filters }, signal),
    placeholderData: keepPreviousData,
  });
}

const participationSchema = pagedSchema(participationRowSchema);

export function useParticipation(filters: ParticipationFilters) {
  return useQuery({
    queryKey: ['participation', filters],
    queryFn: ({ signal }) => bffGet('admin/participation', participationSchema, { ...filters }, signal),
    placeholderData: keepPreviousData,
  });
}

export interface ResultFilters {
  waveId: string | null;
  instrumentCode: string | null;
  reference: string | null;
  page: number;
  pageSize: number;
}

const householdResultsSchema = pagedSchema(householdResultSchema);

/** Recorded score results, for system administrators. The platform audits every read. */
export function useHouseholdResults(filters: ResultFilters) {
  return useQuery({
    queryKey: ['household-results', filters],
    queryFn: ({ signal }) => bffGet('admin/results', householdResultsSchema, { ...filters }, signal),
    placeholderData: keepPreviousData,
  });
}

const exportsSchema = pagedSchema(exportSchema);

/** How often the export list is read again while an export is still being written. */
export const EXPORT_POLL_MS = 3000;

export function isExportPending(item: Export): boolean {
  return item.status === 'queued' || item.status === 'running';
}

export function useExports(page: number, pollMs: number = EXPORT_POLL_MS) {
  return useQuery({
    queryKey: ['exports', page],
    queryFn: ({ signal }) => bffGet('admin/exports', exportsSchema, { page, pageSize: 20 }, signal),
    placeholderData: keepPreviousData,
    refetchInterval: (query) => (query.state.data?.items.some(isExportPending) ? pollMs : false),
  });
}

export function useExportCatalog() {
  return useQuery({
    queryKey: ['export-catalog'],
    queryFn: ({ signal }) => bffGet('admin/exports/catalog', exportCatalogSchema, {}, signal),
    staleTime: 10 * 60_000,
  });
}

export interface ExportRequestBody {
  dataset: string;
  columns: string[];
  filters: { waveId: string | null; instrumentCode: string | null };
}

export function useCreateExport() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ body, idempotencyKey }: { body: ExportRequestBody; idempotencyKey: string }) =>
      bffPost('admin/exports', body, exportSchema, { 'Idempotency-Key': idempotencyKey }),
    onSuccess: () => client.invalidateQueries({ queryKey: ['exports'] }),
    retry: false,
  });
}

export interface PrivacyRequestFilters {
  status: PrivacyRequestStatus;
  page: number;
  pageSize: number;
}

const privacyRequestsSchema = pagedSchema(adminPrivacyRequestSchema);

export function usePrivacyRequests(filters: PrivacyRequestFilters) {
  return useQuery({
    queryKey: ['privacy-requests', filters],
    queryFn: ({ signal }) => bffGet('admin/privacy-requests', privacyRequestsSchema, { ...filters }, signal),
    placeholderData: keepPreviousData,
  });
}

/**
 * Closes one request with its outcome. The list is read again after a success, and after a 404 or 409 too: then the
 * row on screen is out of date (someone else closed it, or it is gone).
 */
export function useClosePrivacyRequest() {
  const client = useQueryClient();
  const refresh = () => client.invalidateQueries({ queryKey: ['privacy-requests'] });
  return useMutation({
    mutationFn: ({ requestId, resolution }: { requestId: string; resolution: PrivacyResolution }) =>
      bffPost(`admin/privacy-requests/${encodeURIComponent(requestId)}/close`, { resolution }, adminPrivacyRequestSchema),
    onSuccess: refresh,
    onError: (error) => {
      if (error instanceof ApiError && (error.status === 404 || error.status === 409)) void refresh();
    },
    retry: false,
  });
}

/** How often the shell's summary is read again; the list is read again when the summary shows a change. */
export const SAFETY_POLL_MS = 30_000;

/** Every query for the alert list starts with this key, so one invalidation reads them all again. */
export const SAFETY_LIST_KEY = ['safety-alerts', 'list'] as const;

/** `unresolved` is open and acknowledged together; `null` lists every status. */
export interface SafetyAlertFilters {
  status: SafetyAlertStatus | 'unresolved' | null;
  category: SafetyConcern | null;
}

const safetyAlertsSchema = pagedSchema(adminSafetyAlertSchema);

export function useSafetyAlerts(filters: SafetyAlertFilters, page: number) {
  const { status, category } = filters;
  return useQuery({
    queryKey: [...SAFETY_LIST_KEY, filters, page],
    queryFn: ({ signal }) => bffGet('admin/safety-alerts', safetyAlertsSchema, { status, category, page, pageSize: 20 }, signal),
    placeholderData: keepPreviousData,
  });
}

export function useSafetyAlertSummary(pollMs: number = SAFETY_POLL_MS) {
  return useQuery({
    queryKey: ['safety-alerts', 'summary'],
    queryFn: ({ signal }) => bffGet('admin/safety-alerts/summary', safetyAlertSummarySchema, {}, signal),
    refetchInterval: pollMs,
    refetchIntervalInBackground: true,
  });
}

/** Both actions read the alerts again after a success, and after a 404 or 409 too: then the row on screen is stale. */
function useSafetyAlertAction<TVariables extends { alertId: string }>(
  action: string,
  body: (variables: TVariables) => unknown,
) {
  const client = useQueryClient();
  const refresh = () => client.invalidateQueries({ queryKey: ['safety-alerts'] });
  return useMutation({
    mutationFn: (variables: TVariables) =>
      bffPost(`admin/safety-alerts/${encodeURIComponent(variables.alertId)}/${action}`, body(variables), adminSafetyAlertSchema),
    onSuccess: refresh,
    onError: (error) => {
      if (error instanceof ApiError && (error.status === 404 || error.status === 409)) void refresh();
    },
    retry: false,
  });
}

export function useAcknowledgeSafetyAlert() {
  return useSafetyAlertAction<{ alertId: string }>('acknowledge', () => ({}));
}

export function useResolveSafetyAlert() {
  return useSafetyAlertAction<{ alertId: string; resolution: SafetyResolution }>('resolve', ({ resolution }) => ({ resolution }));
}

const instrumentsSchema = pagedSchema(instrumentSchema);
const versionsSchema = pagedSchema(instrumentVersionSummarySchema);

export function useInstruments(page: number) {
  return useQuery({
    queryKey: ['instruments', page],
    queryFn: ({ signal }) => bffGet('instruments', instrumentsSchema, { page, pageSize: 20 }, signal),
    placeholderData: keepPreviousData,
  });
}

/** Waits until an ID is known (an empty ID means "not yet"). */
export function useInstrument(instrumentId: string) {
  return useQuery({
    queryKey: ['instrument', instrumentId],
    queryFn: ({ signal }) => bffGet(`instruments/${instrumentId}`, instrumentSchema, {}, signal),
    enabled: instrumentId !== '',
  });
}

export function useInstrumentVersions(instrumentId: string, page: number) {
  return useQuery({
    queryKey: ['instrument-versions', instrumentId, page],
    queryFn: ({ signal }) => bffGet(`instruments/${instrumentId}/versions`, versionsSchema, { page, pageSize: 20 }, signal),
    placeholderData: keepPreviousData,
  });
}

export function useInstrumentVersion(versionId: string) {
  return useQuery({
    queryKey: ['instrument-version', versionId],
    queryFn: ({ signal }) => bffGet(`instrument-versions/${versionId}`, instrumentVersionSchema, {}, signal),
  });
}

export function useVersionRuleSets(versionId: string) {
  return useQuery({
    queryKey: ['version-rule-sets', versionId],
    queryFn: ({ signal }) => bffGet(`instrument-versions/${versionId}/rule-sets`, ruleSetSchema.array(), {}, signal),
  });
}

export function useRuleSet(ruleSetId: string) {
  return useQuery({
    queryKey: ['rule-set', ruleSetId],
    queryFn: ({ signal }) => bffGet(`rule-sets/${ruleSetId}`, ruleSetSchema, {}, signal),
  });
}

export function useHealth() {
  return useQuery({
    queryKey: ['health'],
    queryFn: ({ signal }) => bffGet('health', healthSchema, {}, signal),
    retry: false,
  });
}
