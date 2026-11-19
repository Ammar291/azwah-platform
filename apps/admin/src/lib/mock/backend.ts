import 'server-only';

import { SAFETY_CONCERNS, SAFETY_RESOLUTIONS, type SafetyConcern, type SafetyResolution } from '@/lib/api/schemas';
import type { MockScenario } from '@/lib/server/config';
import type { Persona } from '@/lib/personas';

import { MOCK_PERMISSIONS } from './permissions';
import { ageBandOf, buildDistributions, countByStatus, countOverview, countTiers, MINIMUM_GROUP_SIZE } from './aggregates';
import {
  buildContent,
  buildPopulation,
  buildPrivacyRequests,
  buildSafetyAlerts,
  MOCK_EXPORT_CATALOG,
  MOCK_NOW,
  MOCK_WAVES,
  nonSubstantiveAnswers,
  type MockAssessmentRow,
  type MockPrivacyRequest,
  type MockResult,
  type MockSafetyAlert,
  type PrivacyResolution,
  type ScoreExportColumn,
  type SessionExportColumn,
} from './fixtures';
import { hex, seeded, seedOf } from './random';

/**
 * A development-only stand-in for the API, answering the same routes and shapes over one synthetic population. It
 * mirrors the API's role checks so the dashboard's permission-denied states can be seen, and it counts and suppresses
 * its aggregates the way the API does (`aggregates.ts`), so every page shows the same data set. It scores nothing and
 * decides no eligibility: synthetic totals are drawn, and their bands read from them, in `fixtures.ts`.
 */

export interface MockRequest {
  method: 'GET' | 'POST';
  apiPath: string;
  query: URLSearchParams;
  body: unknown;
  persona: Persona;
  idempotencyKey: string | null;
  now: Date;
}

export interface MockBackend {
  handle(request: MockRequest): Promise<Response>;
}

const TRACE = '00-0000000000000000000000000000mock-0000000000000000-01';
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const SNAKE = /^[a-z][a-z0-9]*(_[a-z0-9]+)*$/;
const REFERENCE = /^HH-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}$/;
const DAY = /^\d{4}-\d{2}-\d{2}$/;
const ASSIGNMENT_STATUSES = ['assigned', 'in_progress', 'completed', 'expired', 'cancelled'];
const RESOLUTIONS: readonly PrivacyResolution[] = ['completed', 'declined', 'withdrawn'];
const STATUS_RANK = { open: 0, acknowledged: 1, resolved: 2 } as const;

function json(body: unknown, status = 200, headers: Record<string, string> = {}): Response {
  return Response.json(body, { status, headers });
}

function problem(status: number, code?: string): Response {
  const response = Response.json(code ? { status, code, traceId: TRACE } : { status, traceId: TRACE }, { status });
  response.headers.set('Content-Type', 'application/problem+json');
  return response;
}

function invalid(errors: Record<string, string>): Response {
  const response = Response.json(
    {
      title: 'One or more validation errors occurred.',
      status: 400,
      errors: Object.fromEntries(Object.entries(errors).map(([key, message]) => [key, [message]])),
      traceId: TRACE,
    },
    { status: 400 },
  );
  response.headers.set('Content-Type', 'application/problem+json');
  return response;
}

type Errors = Record<string, string>;

function paging(query: URLSearchParams, errors: Errors): { page: number; pageSize: number } {
  const page = Number(query.get('page') ?? '1');
  const pageSize = Number(query.get('pageSize') ?? '20');
  if (!Number.isInteger(page) || page < 1) errors.page = "'Page' must be at least 1.";
  if (!Number.isInteger(pageSize) || pageSize < 1 || pageSize > 100) errors.pageSize = "'Page Size' must be between 1 and 100.";
  return { page, pageSize };
}

function paged<T>(rows: T[], page: number, pageSize: number) {
  return { items: rows.slice((page - 1) * pageSize, page * pageSize), page, pageSize, totalCount: rows.length };
}

function validDay(value: string | null): boolean {
  return value === null || (DAY.test(value) && !Number.isNaN(Date.parse(`${value}T00:00:00Z`)));
}

function waveFilter(query: URLSearchParams, errors: Errors): string | null {
  const waveId = query.get('waveId') || null;
  if (waveId !== null && (!UUID.test(waveId) || /^0{8}-/.test(waveId))) errors.waveId = 'The value is not valid.';
  return waveId;
}

type Policy = 'analytics' | 'waves' | 'researcher' | 'program_administrator' | 'system_administrator' | 'anyone';

const POLICY_ROLES: Record<Exclude<Policy, 'anyone'>, Persona[]> = {
  analytics: ['researcher', 'program_administrator'],
  // The API's WaveReader policy: the wave list filters analytics and the household results page (D-104).
  waves: ['researcher', 'program_administrator', 'system_administrator'],
  researcher: ['researcher'],
  program_administrator: ['program_administrator'],
  system_administrator: ['system_administrator'],
};

interface MockExport {
  id: string;
  persona: Persona;
  key: string;
  fingerprint: string;
  dataset: string;
  columns: string[];
  filters: { waveId: string | null; instrumentCode: string | null };
  requestedAt: number;
}

const QUEUED_MS = 3_000;
const RUNNING_MS = 10_000;
const RETENTION_MS = 7 * 24 * 60 * 60 * 1000;
const iso = (time: number) => new Date(time).toISOString().replace('.000Z', 'Z');

export function createMockBackend(scenario: MockScenario): MockBackend {
  const scoringApproved = scenario === 'approved_scores';
  const content = buildContent(scoringApproved);
  const population = buildPopulation(scoringApproved);
  const exports = new Map<string, MockExport>();
  const idCounter = { next: 1 };
  const privacy = new Map<string, MockPrivacyRequest>(buildPrivacyRequests(population).map((item) => [item.id, item]));
  const results = population.results;
  const safetyAlerts = new Map<string, MockSafetyAlert>(buildSafetyAlerts(population).map((item) => [item.id, item]));
  const today = MOCK_NOW.slice(0, 10);
  const members = new Map(population.members.map((member) => [member.memberId, member]));

  /** A submitted session answered every item of its questionnaire or module (all are required). */
  function answeredCount(row: MockAssessmentRow): number {
    const version = content.versions.find(
      (candidate) =>
        candidate.versionNumber === row.versionNumber &&
        content.instruments.some((instrument) => instrument.id === candidate.instrumentId && instrument.code === row.instrumentCode),
    );
    const sections = version?.sections.filter((section) => row.sectionCode === null || section.code === row.sectionCode) ?? [];
    return sections.reduce((sum, section) => sum + section.items.length, 0);
  }

  /**
   * The rows of an export, as the API reads them: submitted and scored sessions, or score results, narrowed by the
   * job's wave and questionnaire. (Every synthetic member with a session counts as having research-use consent, and a
   * withdrawn household has none.) Each row has a cell for every column of its dataset's catalog entry, written as the
   * API's `ExportCatalog` writes it: a value the platform does not hold (no module, no reason) is an empty cell, an
   * age band is worked out on the day of the session or the score, and a member with no employment status recorded
   * reads `not_recorded`. The row types list every column, so a column cannot be offered without a value.
   */
  function exportRows(job: MockExport): Record<string, string>[] {
    const kept = (row: { waveId: string; instrumentCode: string }) =>
      (job.filters.waveId === null || row.waveId === job.filters.waveId) &&
      (job.filters.instrumentCode === null || row.instrumentCode === job.filters.instrumentCode);
    // Pseudonyms change with every export, as the API's do.
    const person = (memberId: string, householdId: string, on: string) => {
      const member = members.get(memberId);
      return {
        participant_pseudonym: hex(seeded(seedOf(`${job.id}:${memberId}`)), 32),
        household_pseudonym: hex(seeded(seedOf(`${job.id}:${householdId}`)), 32),
        age_band: member ? ageBandOf(member.birthYear, member.birthMonth, on) : '',
        employment_status: member?.employmentStatus ?? 'not_recorded',
      };
    };
    const common = (row: MockAssessmentRow | MockResult) => ({
      instrument_code: row.instrumentCode,
      version_number: String(row.versionNumber),
      section_code: row.sectionCode ?? '',
      wave_code: row.waveCode,
    });
    if (job.dataset === 'score_results') {
      return results.filter(kept).map(
        (row): Record<ScoreExportColumn, string> => ({
          ...person(row.memberId, row.householdId, row.computedAt),
          ...common(row),
          rule_set_code: row.ruleSetCode,
          rule_set_version: String(row.ruleSetVersion),
          score_status: row.state,
          // Every synthetic result is complete (see `fixtures.ts`), so none has a reason to give.
          incomplete_reason: '',
          total_score: row.totalScore === null ? '' : String(row.totalScore),
          tier_code: row.tierCode ?? '',
          // The household assessment has no non-substantive option, so no answer is ever left out of its total.
          excluded_count: '0',
          scored_month: row.computedAt.slice(0, 7),
        }),
      );
    }
    return population.assessments
      .filter((row) => (row.session === 'submitted' || row.session === 'scored') && kept(row))
      .map((row): Record<SessionExportColumn, string> => {
        const submittedAt = row.submittedAt ?? row.assignedAt;
        const answered = answeredCount(row);
        return {
          ...person(row.memberId, row.householdId, submittedAt),
          ...common(row),
          session_status: row.session ?? '',
          submitted_month: submittedAt.slice(0, 7),
          is_head_of_household: members.get(row.memberId)?.isHeadOfHousehold ? 'true' : 'false',
          answered_count: String(answered),
          non_substantive_count: String(nonSubstantiveAnswers(row, answered)),
        };
      });
  }

  function exportView(job: MockExport, now: number) {
    const finishedAt = job.requestedAt + RUNNING_MS;
    const expiresAt = finishedAt + RETENTION_MS;
    const elapsed = now - job.requestedAt;
    const status = elapsed < QUEUED_MS ? 'queued' : elapsed < RUNNING_MS ? 'running' : now < expiresAt ? 'completed' : 'expired';
    const done = status === 'completed' || status === 'expired';
    return {
      id: job.id,
      dataset: job.dataset,
      columns: job.columns,
      filters: job.filters,
      status,
      rowCount: done ? exportRows(job).length : null,
      errorCode: null,
      requestedAt: iso(job.requestedAt),
      finishedAt: done ? iso(finishedAt) : null,
      expiresAt: done ? iso(expiresAt) : null,
      downloadPath: status === 'completed' ? `/api/v1/admin/exports/${job.id}/file` : null,
    };
  }

  /** The file's rows in pseudonym order, as the API writes them, so the file does not reveal the order of the data. */
  function csv(job: MockExport): string {
    const rows = exportRows(job).sort((a, b) => a.participant_pseudonym.localeCompare(b.participant_pseudonym));
    const lines = [job.columns.join(',')];
    for (const row of rows) lines.push(job.columns.map((column) => row[column] ?? '').join(','));
    return `${lines.join('\r\n')}\r\n`;
  }

  function overview(query: URLSearchParams): Response {
    const errors: Errors = {};
    const waveId = waveFilter(query, errors);
    const from = query.get('from');
    const to = query.get('to');
    const interval = query.get('interval') || 'month';
    if (!validDay(from)) errors.from = 'The value is not valid.';
    if (!validDay(to)) errors.to = 'The value is not valid.';
    if (interval !== 'month' && interval !== 'week') errors.interval = 'The value is not valid.';
    if (Object.keys(errors).length > 0) return invalid(errors);
    if (waveId !== null && !MOCK_WAVES.some((wave) => wave.id === waveId)) return invalid({ waveId: 'No wave has this ID.' });

    const toDay = to ?? today;
    const fromDay = from ?? `${Number(toDay.slice(0, 4)) - 1}${toDay.slice(4)}`;
    if (fromDay > toDay) return invalid({ from: "'From' must not be after 'To'." });
    return json({
      generatedAt: MOCK_NOW,
      minimumGroupSize: MINIMUM_GROUP_SIZE,
      waveId,
      from: fromDay,
      to: toDay,
      interval,
      ...countOverview(population, { waveId, from: fromDay, to: toDay, interval: interval === 'week' ? 'week' : 'month', today }),
    });
  }

  /** Empty until scoring is approved: the population then has no score results to count. */
  function scoreDistributions(query: URLSearchParams): Response {
    const errors: Errors = {};
    const waveId = waveFilter(query, errors);
    if (Object.keys(errors).length > 0) return invalid(errors);
    return json({
      generatedAt: MOCK_NOW,
      minimumGroupSize: MINIMUM_GROUP_SIZE,
      waveId,
      distributions: buildDistributions(countTiers(population, waveId), MINIMUM_GROUP_SIZE),
    });
  }

  function assessments(query: URLSearchParams): Response {
    const errors: Errors = {};
    const { page, pageSize } = paging(query, errors);
    const waveId = waveFilter(query, errors);
    const householdId = query.get('householdId') || null;
    const instrumentCode = query.get('instrumentCode') || null;
    const versionText = query.get('versionNumber') || null;
    const status = query.get('status') || null;
    const assignedFrom = query.get('assignedFrom') || null;
    const assignedTo = query.get('assignedTo') || null;
    const versionNumber = versionText === null ? null : Number(versionText);
    if (householdId !== null && !UUID.test(householdId)) errors.householdId = 'The value is not valid.';
    if (instrumentCode !== null && !SNAKE.test(instrumentCode)) errors.instrumentCode = 'An instrument code is lowercase snake case.';
    if (versionNumber !== null && (!Number.isInteger(versionNumber) || versionNumber < 1 || instrumentCode === null)) {
      errors.versionNumber = 'A version number needs an instrument code.';
    }
    if (status !== null && !ASSIGNMENT_STATUSES.includes(status)) errors.status = 'The value is not valid.';
    if (!validDay(assignedFrom)) errors.assignedFrom = 'The value is not valid.';
    if (!validDay(assignedTo)) errors.assignedTo = 'The value is not valid.';
    if (!errors.assignedFrom && !errors.assignedTo && assignedFrom && assignedTo && assignedFrom > assignedTo) {
      errors.assignedFrom = 'The first assigned day must not be after the last.';
    }
    if (Object.keys(errors).length > 0) return invalid(errors);
    const rows = population.assessments.filter(
      (row) =>
        (waveId === null || row.waveId === waveId) &&
        (householdId === null || row.householdId === householdId) &&
        (instrumentCode === null || row.instrumentCode === instrumentCode) &&
        (versionNumber === null || row.versionNumber === versionNumber) &&
        (status === null || row.assignmentStatus === status) &&
        (assignedFrom === null || row.assignedAt.slice(0, 10) >= assignedFrom) &&
        (assignedTo === null || row.assignedAt.slice(0, 10) <= assignedTo),
    );
    const view = (row: MockAssessmentRow) => ({
      assignmentId: row.assignmentId,
      householdId: row.householdId,
      instrumentCode: row.instrumentCode,
      versionNumber: row.versionNumber,
      sectionCode: row.sectionCode,
      waveCode: row.waveCode,
      assignmentStatus: row.assignmentStatus,
      assignedAt: row.assignedAt,
      expiresAt: row.expiresAt,
      completedAt: row.completedAt,
      // As the API does, an abandoned session is not listed: the row then shows no session.
      sessionStatus: row.session === 'abandoned' ? null : row.session,
      submittedAt: row.submittedAt,
      scored: row.session === 'scored',
    });
    return json(paged(rows.map(view), page, pageSize));
  }

  function participation(query: URLSearchParams): Response {
    const errors: Errors = {};
    const { page, pageSize } = paging(query, errors);
    const status = query.get('status') || null;
    const roster = query.get('rosterConfirmed') || null;
    const reference = query.get('reference') || null;
    if (status !== null && status !== 'active' && status !== 'withdrawn') errors.status = 'The value is not valid.';
    if (roster !== null && roster !== 'true' && roster !== 'false') errors.rosterConfirmed = 'The value is not valid.';
    if (reference !== null && !REFERENCE.test(reference)) errors.reference = 'A household reference looks like HH-XXXX-XXXX.';
    if (Object.keys(errors).length > 0) return invalid(errors);
    const rows = population.households
      .filter(
        (household) =>
          (status === null || household.status === status) &&
          (roster === null || (household.rosterConfirmedAt !== null) === (roster === 'true')) &&
          (reference === null || household.reference === reference),
      )
      .map((household) => ({
        ...household,
        assignments: countByStatus(population.assessments.filter((row) => row.householdId === household.householdId)),
      }));
    return json(paged(rows, page, pageSize));
  }

  /** The latest result of each scored session, newest first, for system administrators; the reference matches exactly. */
  function householdResults(query: URLSearchParams): Response {
    const errors: Errors = {};
    const { page, pageSize } = paging(query, errors);
    const waveId = waveFilter(query, errors);
    const instrumentCode = query.get('instrumentCode') || null;
    const reference = query.get('reference') || null;
    if (instrumentCode !== null && !SNAKE.test(instrumentCode)) errors.instrumentCode = 'An instrument code is lowercase snake case.';
    if (reference !== null && !REFERENCE.test(reference)) errors.reference = 'A household reference looks like HH-XXXX-XXXX.';
    if (Object.keys(errors).length > 0) return invalid(errors);
    const view = (row: MockResult) => ({
      sessionId: row.sessionId,
      householdId: row.householdId,
      householdReference: row.householdReference,
      waveCode: row.waveCode,
      instrumentCode: row.instrumentCode,
      versionNumber: row.versionNumber,
      sectionCode: row.sectionCode,
      state: row.state,
      totalScore: row.totalScore,
      tierCode: row.tierCode,
      computedAt: row.computedAt,
    });
    const rows = results.filter(
      (row) =>
        (waveId === null || row.waveId === waveId) &&
        (instrumentCode === null || row.instrumentCode === instrumentCode) &&
        (reference === null || row.householdReference === reference),
    );
    return json(paged(rows.map(view), page, pageSize));
  }

  /** Open requests before closed ones, oldest first, as the platform lists them. */
  function privacyRequests(query: URLSearchParams): Response {
    const errors: Errors = {};
    const { page, pageSize } = paging(query, errors);
    const status = query.get('status') || null;
    if (status !== null && status !== 'open' && status !== 'closed') errors.status = 'The value is not valid.';
    if (Object.keys(errors).length > 0) return invalid(errors);
    const rows = [...privacy.values()]
      .filter((item) => status === null || item.status === status)
      .sort(
        (a, b) =>
          Number(a.status === 'closed') - Number(b.status === 'closed') || a.createdAt.localeCompare(b.createdAt) || a.id.localeCompare(b.id),
      );
    return json(paged(rows, page, pageSize));
  }

  /** Validation first, then not found, then already closed, as the platform answers. */
  function closePrivacyRequest(request: MockRequest, id: string): Response {
    const resolution = (request.body as { resolution?: unknown } | null)?.resolution;
    if (typeof resolution !== 'string' || !(RESOLUTIONS as readonly string[]).includes(resolution)) {
      return invalid({ resolution: "'Resolution' must be completed, declined or withdrawn." });
    }
    const item = privacy.get(id.toLowerCase());
    if (!item) return problem(404, 'privacy.request_not_found');
    if (item.status === 'closed') return problem(409, 'privacy.request_already_closed');
    const closed: MockPrivacyRequest = {
      ...item,
      status: 'closed',
      closedAt: iso(request.now.getTime()),
      resolution: resolution as PrivacyResolution,
    };
    privacy.set(item.id, closed);
    return json(closed);
  }

  /** Open, then acknowledged, then resolved; critical before high; newest detection first, as the platform lists them. */
  function safetyAlertList(query: URLSearchParams): Response {
    const errors: Errors = {};
    const { page, pageSize } = paging(query, errors);
    const status = query.get('status') || null;
    if (status !== null && !['open', 'acknowledged', 'resolved', 'unresolved'].includes(status)) errors.status = 'The value is not valid.';
    const category = query.get('category') || null;
    if (category !== null && !(SAFETY_CONCERNS as readonly string[]).includes(category)) errors.category = 'The value is not valid.';
    if (Object.keys(errors).length > 0) return invalid(errors);
    const rank = (item: MockSafetyAlert) => STATUS_RANK[item.status];
    const rows = [...safetyAlerts.values()]
      .filter(
        (item) =>
          (status === null || (status === 'unresolved' ? item.status !== 'resolved' : item.status === status)) &&
          (category === null || item.category === (category as SafetyConcern)),
      )
      .sort(
        (a, b) =>
          rank(a) - rank(b) ||
          Number(a.severity === 'high') - Number(b.severity === 'high') ||
          b.lastDetectedAt.localeCompare(a.lastDetectedAt) ||
          a.id.localeCompare(b.id),
      );
    return json(paged(rows, page, pageSize));
  }

  function safetyAlertSummary(): Response {
    const all = [...safetyAlerts.values()];
    // Unresolved alerts only: a resolved one must never move the marker the shell watches.
    const unresolved = all.filter((item) => item.status !== 'resolved');
    return json({
      open: all.filter((item) => item.status === 'open').length,
      acknowledged: all.filter((item) => item.status === 'acknowledged').length,
      openCritical: all.filter((item) => item.status === 'open' && item.severity === 'critical').length,
      latestDetectedAt: unresolved.map((item) => item.lastDetectedAt).sort().at(-1) ?? null,
    });
  }

  function acknowledgeSafetyAlert(request: MockRequest, id: string): Response {
    const item = safetyAlerts.get(id.toLowerCase());
    if (!item) return problem(404, 'safeguarding.alert_not_found');
    if (item.status !== 'open') return problem(409, 'safeguarding.alert_not_open');
    const acknowledged: MockSafetyAlert = { ...item, status: 'acknowledged', acknowledgedAt: iso(request.now.getTime()) };
    safetyAlerts.set(item.id, acknowledged);
    return json(acknowledged);
  }

  /** Validation first, then not found, then already resolved, as the platform answers. */
  function resolveSafetyAlert(request: MockRequest, id: string): Response {
    const resolution = (request.body as { resolution?: unknown } | null)?.resolution;
    if (typeof resolution !== 'string' || !(SAFETY_RESOLUTIONS as readonly string[]).includes(resolution)) {
      return invalid({ resolution: "'Resolution' must be one of the listed outcomes." });
    }
    const item = safetyAlerts.get(id.toLowerCase());
    if (!item) return problem(404, 'safeguarding.alert_not_found');
    if (item.status === 'resolved') return problem(409, 'safeguarding.alert_resolved');
    const resolved: MockSafetyAlert = {
      ...item,
      status: 'resolved',
      resolvedAt: iso(request.now.getTime()),
      resolution: resolution as SafetyResolution,
    };
    safetyAlerts.set(item.id, resolved);
    return json(resolved);
  }

  function waves(query: URLSearchParams): Response {
    const errors: Errors = {};
    const { page, pageSize } = paging(query, errors);
    if (Object.keys(errors).length > 0) return invalid(errors);
    const newestFirst = [...MOCK_WAVES].sort((a, b) => b.opensAt.localeCompare(a.opensAt));
    return json(paged(newestFirst, page, pageSize));
  }

  function createExport(request: MockRequest): Response {
    const key = request.idempotencyKey;
    if (!key || !UUID.test(key)) return invalid({ idempotencyKey: 'An Idempotency-Key header with a UUID is required.' });
    const body = (request.body ?? {}) as { dataset?: unknown; columns?: unknown; filters?: { waveId?: unknown; instrumentCode?: unknown } | null };
    const fingerprint = JSON.stringify(body);
    const replay = [...exports.values()].find((job) => job.persona === request.persona && job.key === key);
    if (replay) {
      return replay.fingerprint === fingerprint
        ? json(exportView(replay, request.now.getTime()), 202, { Location: `/api/v1/admin/exports/${replay.id}` })
        : problem(409, 'requests.idempotency_key_reused');
    }
    const dataset = MOCK_EXPORT_CATALOG.datasets.find((candidate) => candidate.code === body.dataset);
    if (typeof body.dataset !== 'string' || body.dataset.length === 0) return invalid({ dataset: "'Dataset' must not be empty." });
    if (!dataset) return problem(400, 'analytics.export_dataset_invalid');
    const columns = Array.isArray(body.columns) ? body.columns : [];
    if (columns.length === 0) return invalid({ columns: "'Columns' must not be empty." });
    if (
      columns.length > 30 ||
      new Set(columns).size !== columns.length ||
      !columns.every((column) => typeof column === 'string' && dataset.columns.includes(column))
    ) {
      return problem(400, 'analytics.export_column_invalid');
    }
    const waveId = typeof body.filters?.waveId === 'string' ? body.filters.waveId : null;
    const instrumentCode = typeof body.filters?.instrumentCode === 'string' ? body.filters.instrumentCode : null;
    if ((waveId !== null && !MOCK_WAVES.some((wave) => wave.id === waveId)) || (instrumentCode !== null && !SNAKE.test(instrumentCode))) {
      return problem(400, 'analytics.export_filter_invalid');
    }
    const id = `${hex(seeded(seedOf(`${key}:${idCounter.next++}`)), 8)}-0000-4000-8000-${String(idCounter.next).padStart(12, '0')}`;
    const job: MockExport = {
      id,
      persona: request.persona,
      key,
      fingerprint,
      dataset: dataset.code,
      columns: columns as string[],
      filters: { waveId, instrumentCode },
      requestedAt: request.now.getTime(),
    };
    exports.set(id, job);
    return json(exportView(job, request.now.getTime()), 202, { Location: `/api/v1/admin/exports/${id}` });
  }

  function ownExport(id: string, persona: Persona): MockExport | null {
    const job = exports.get(id);
    return job && job.persona === persona ? job : null;
  }

  type Route = { method: 'GET' | 'POST'; pattern: RegExp; policy: Policy; run: (request: MockRequest, match: RegExpExecArray) => Response };

  const routes: Route[] = [
    {
      method: 'GET',
      pattern: /^\/api\/v1\/me\/permissions$/,
      policy: 'anyone',
      run: (request) => json({ permissions: MOCK_PERMISSIONS[request.persona], households: [] }),
    },
    { method: 'GET', pattern: /^\/api\/v1\/admin\/analytics\/overview$/, policy: 'analytics', run: (request) => overview(request.query) },
    {
      method: 'GET',
      pattern: /^\/api\/v1\/admin\/analytics\/score-distributions$/,
      policy: 'researcher',
      run: (request) => scoreDistributions(request.query),
    },
    { method: 'GET', pattern: /^\/api\/v1\/admin\/assessments$/, policy: 'program_administrator', run: (request) => assessments(request.query) },
    {
      method: 'GET',
      pattern: /^\/api\/v1\/admin\/participation$/,
      policy: 'program_administrator',
      run: (request) => participation(request.query),
    },
    {
      method: 'GET',
      pattern: /^\/api\/v1\/admin\/privacy-requests$/,
      policy: 'program_administrator',
      run: (request) => privacyRequests(request.query),
    },
    {
      method: 'POST',
      pattern: /^\/api\/v1\/admin\/privacy-requests\/([0-9a-f-]{36})\/close$/i,
      policy: 'program_administrator',
      run: (request, match) => closePrivacyRequest(request, match[1]),
    },
    {
      method: 'GET',
      pattern: /^\/api\/v1\/admin\/safety-alerts$/,
      policy: 'system_administrator',
      run: (request) => safetyAlertList(request.query),
    },
    { method: 'GET', pattern: /^\/api\/v1\/admin\/safety-alerts\/summary$/, policy: 'system_administrator', run: () => safetyAlertSummary() },
    {
      method: 'POST',
      pattern: /^\/api\/v1\/admin\/safety-alerts\/([0-9a-f-]{36})\/acknowledge$/i,
      policy: 'system_administrator',
      run: (request, match) => acknowledgeSafetyAlert(request, match[1]),
    },
    {
      method: 'POST',
      pattern: /^\/api\/v1\/admin\/safety-alerts\/([0-9a-f-]{36})\/resolve$/i,
      policy: 'system_administrator',
      run: (request, match) => resolveSafetyAlert(request, match[1]),
    },
    {
      method: 'GET',
      pattern: /^\/api\/v1\/admin\/results$/,
      policy: 'system_administrator',
      run: (request) => householdResults(request.query),
    },
    { method: 'GET', pattern: /^\/api\/v1\/admin\/waves$/, policy: 'waves', run: (request) => waves(request.query) },
    { method: 'GET', pattern: /^\/api\/v1\/admin\/exports\/catalog$/, policy: 'researcher', run: () => json(MOCK_EXPORT_CATALOG) },
    {
      method: 'GET',
      pattern: /^\/api\/v1\/admin\/exports$/,
      policy: 'researcher',
      run: (request) => {
        const errors: Errors = {};
        const { page, pageSize } = paging(request.query, errors);
        if (Object.keys(errors).length > 0) return invalid(errors);
        const mine = [...exports.values()]
          .filter((job) => job.persona === request.persona)
          .sort((a, b) => b.requestedAt - a.requestedAt || b.id.localeCompare(a.id))
          .map((job) => exportView(job, request.now.getTime()));
        return json(paged(mine, page, pageSize));
      },
    },
    { method: 'POST', pattern: /^\/api\/v1\/admin\/exports$/, policy: 'researcher', run: (request) => createExport(request) },
    {
      method: 'GET',
      pattern: /^\/api\/v1\/admin\/exports\/([0-9a-f-]{36})$/i,
      policy: 'researcher',
      run: (request, match) => {
        const job = ownExport(match[1], request.persona);
        return job ? json(exportView(job, request.now.getTime())) : problem(404, 'analytics.export_not_found');
      },
    },
    {
      method: 'GET',
      pattern: /^\/api\/v1\/admin\/exports\/([0-9a-f-]{36})\/file$/i,
      policy: 'researcher',
      run: (request, match) => {
        const job = ownExport(match[1], request.persona);
        if (!job) return problem(404, 'analytics.export_not_found');
        const view = exportView(job, request.now.getTime());
        if (view.status === 'expired') return problem(409, 'analytics.export_expired');
        if (view.status !== 'completed') return problem(409, 'analytics.export_not_ready');
        return new Response(csv(job), {
          headers: {
            'Content-Type': 'text/csv',
            'Content-Disposition': `attachment; filename="dev-sample-${job.dataset}-${job.id.slice(0, 8)}.csv"`,
            'Cache-Control': 'no-store',
          },
        });
      },
    },
    {
      method: 'GET',
      pattern: /^\/api\/v1\/instruments$/,
      policy: 'system_administrator',
      run: (request) => {
        const errors: Errors = {};
        const { page, pageSize } = paging(request.query, errors);
        return Object.keys(errors).length > 0 ? invalid(errors) : json(paged(content.instruments, page, pageSize));
      },
    },
    {
      method: 'GET',
      pattern: /^\/api\/v1\/instruments\/([0-9a-f-]{36})$/i,
      policy: 'system_administrator',
      run: (_request, match) => {
        const instrument = content.instruments.find((candidate) => candidate.id === match[1]);
        return instrument ? json(instrument) : problem(404, 'questionnaires.instrument_not_found');
      },
    },
    {
      method: 'GET',
      pattern: /^\/api\/v1\/instruments\/([0-9a-f-]{36})\/versions$/i,
      policy: 'system_administrator',
      run: (request, match) => {
        if (!content.instruments.some((candidate) => candidate.id === match[1])) return problem(404, 'questionnaires.instrument_not_found');
        const errors: Errors = {};
        const { page, pageSize } = paging(request.query, errors);
        if (Object.keys(errors).length > 0) return invalid(errors);
        const summaries = content.versions
          .filter((version) => version.instrumentId === match[1])
          .sort((a, b) => b.versionNumber - a.versionNumber)
          .map((version) => ({
            id: version.id,
            instrumentId: version.instrumentId,
            versionNumber: version.versionNumber,
            status: version.status,
            approvedBy: version.approvedBy,
            approvedAt: version.approvedAt,
            approvalSource: version.approvalSource,
            effectiveFrom: version.effectiveFrom,
            effectiveTo: version.effectiveTo,
            retiredAt: version.retiredAt,
            sectionCount: version.sections.length,
            itemCount: version.sections.reduce((sum, section) => sum + section.items.length, 0),
          }));
        return json(paged(summaries, page, pageSize));
      },
    },
    {
      method: 'GET',
      pattern: /^\/api\/v1\/instrument-versions\/([0-9a-f-]{36})$/i,
      policy: 'system_administrator',
      run: (_request, match) => {
        const version = content.versions.find((candidate) => candidate.id === match[1]);
        return version ? json(version) : problem(404, 'questionnaires.version_not_found');
      },
    },
    {
      method: 'GET',
      pattern: /^\/api\/v1\/instrument-versions\/([0-9a-f-]{36})\/rule-sets$/i,
      policy: 'system_administrator',
      run: (_request, match) => {
        if (!content.versions.some((candidate) => candidate.id === match[1])) return problem(404, 'questionnaires.version_not_found');
        const ruleSets = content.ruleSets
          .filter((ruleSet) => ruleSet.instrumentVersionId === match[1])
          .sort((a, b) => a.kind.localeCompare(b.kind) || a.code.localeCompare(b.code) || a.versionNumber - b.versionNumber);
        return json(ruleSets);
      },
    },
    {
      method: 'GET',
      pattern: /^\/api\/v1\/rule-sets\/([0-9a-f-]{36})$/i,
      policy: 'system_administrator',
      run: (_request, match) => {
        const ruleSet = content.ruleSets.find((candidate) => candidate.id === match[1]);
        return ruleSet ? json(ruleSet) : problem(404, 'questionnaires.rule_set_not_found');
      },
    },
  ];

  return {
    async handle(request) {
      for (const route of routes) {
        const match = route.pattern.exec(request.apiPath);
        if (!match || route.method !== request.method) continue;
        if (route.policy !== 'anyone' && !POLICY_ROLES[route.policy].includes(request.persona)) return problem(403);
        return route.run(request, match);
      }
      return problem(404);
    },
  };
}

const backends = new Map<MockScenario, MockBackend>();

/** One mock per scenario and server process, so exports requested earlier are still there. */
export function getMockBackend(scenario: MockScenario): MockBackend {
  let backend = backends.get(scenario);
  if (!backend) {
    backend = createMockBackend(scenario);
    backends.set(scenario, backend);
  }
  return backend;
}
