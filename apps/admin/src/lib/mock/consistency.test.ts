// @vitest-environment node
import { readFileSync } from 'node:fs';

import { describe, expect, it } from 'vitest';

import type { Persona } from '@/lib/personas';

import { buildDistributions, completionRate, MINIMUM_GROUP_SIZE, suppress } from './aggregates';
import { createMockBackend } from './backend';
import { bandOf, buildPopulation, HOUSEHOLD_ASSESSMENT, HOUSEHOLD_BANDS, MOCK_WAVES } from './fixtures';

/**
 * The mock is one data set: every page's numbers are counted from the same synthetic households, members, assignments,
 * sessions and results, with the API's own definitions and suppression. These tests read the pages' routes and check
 * them against each other and against the population.
 */

const now = new Date(Date.UTC(2026, 9, 16, 9, 0, 0));
const STATUSES = ['assigned', 'inProgress', 'completed', 'expired', 'cancelled'] as const;
const STATUS_CODES = { assigned: 'assigned', inProgress: 'in_progress', completed: 'completed', expired: 'expired', cancelled: 'cancelled' } as const;
const SCOPES: (string | null)[] = [null, ...MOCK_WAVES.map((wave) => wave.id)];

type Totals = Record<(typeof STATUSES)[number] | 'total' | 'started', number> & { completionRate: number | null };
type VersionRow = { instrumentCode: string; versionNumber: number; sectionCode: string | null; total: number; completed: number; completionRate: number | null };
type DemographicRow = { group: string; total: number | null; completed: number | null; completionRate: number | null; suppressed: boolean };
type Overview = {
  enrollment: { households: number; activeHouseholds: number; withdrawnHouseholds: number; rosterConfirmedHouseholds: number; activeMembers: number };
  assignments: Totals;
  byVersion: VersionRow[];
  completionTrend: { periodStart: string; completed: number }[];
  demographics: { byAgeBand: DemographicRow[]; byEmploymentStatus: DemographicRow[] };
};
type Tier = { tier: string; count: number | null; suppressed: boolean };
type Distribution = { instrumentCode: string; sectionCode: string | null; ruleSetCode: string; total: number | null; suppressed: boolean; tiers: Tier[] };
type Result = { sessionId: string; waveCode: string; instrumentCode: string; sectionCode: string | null; state: string; totalScore: number | null; tierCode: string | null };
type Page<T> = { items: T[]; totalCount: number };

const backend = createMockBackend('approved_scores');
const population = buildPopulation(true);

async function get<T>(target: ReturnType<typeof createMockBackend>, apiPath: string, persona: Persona, query: Record<string, string | null> = {}): Promise<T> {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) if (value !== null) params.set(key, value);
  const response = await target.handle({ method: 'GET', apiPath, query: params, body: null, persona, idempotencyKey: null, now });
  expect(response.status).toBe(200);
  return (await response.json()) as T;
}

async function all<T>(target: ReturnType<typeof createMockBackend>, apiPath: string, persona: Persona, query: Record<string, string | null> = {}): Promise<T[]> {
  const items: T[] = [];
  for (let page = 1; ; page++) {
    const body = await get<Page<T>>(target, apiPath, persona, { ...query, page: String(page), pageSize: '100' });
    items.push(...body.items);
    if (items.length >= body.totalCount) return items;
  }
}

const overview = (waveId: string | null, query: Record<string, string> = {}, target = backend) =>
  get<Overview>(target, '/api/v1/admin/analytics/overview', 'researcher', { waveId, ...query });
const distributions = async (waveId: string | null, target = backend) =>
  (await get<{ distributions: Distribution[] }>(target, '/api/v1/admin/analytics/score-distributions', 'researcher', { waveId })).distributions;
const results = (waveId: string | null, target = backend) => all<Result>(target, '/api/v1/admin/results', 'system_administrator', { waveId });

describe('suppression, as the API applies it', () => {
  it('hides a cell of 1 to 9 people and, when it is the only one, the cell covering the next fewest people too', () => {
    expect(suppress([30, 5, 12, 40], [30, 5, 12, 40], 10)).toEqual([false, true, true, false]);
    expect(suppress([30, 5, 7, 40], [30, 5, 7, 40], 10)).toEqual([false, true, true, false]);
    expect(suppress([30, 12, 40], [30, 12, 40], 10)).toEqual([false, false, false]);
    expect(suppress([0, 12, 40], [0, 12, 40], 10)).toEqual([false, false, false]);
  });

  it('decides on the people behind a cell, not on its count', () => {
    // Twelve assignments held by six members are hidden; the second hidden cell is the one of fewest people (25),
    // although its count (30) is not the smallest shown.
    expect(suppress([30, 12, 28], [25, 6, 27], 10)).toEqual([true, true, false]);
  });

  it('breaks a tie on people by the smaller count, then the earlier cell, and never takes a zero', () => {
    expect(suppress([0, 24, 5, 20], [0, 20, 5, 20], 10)).toEqual([false, false, true, true]);
    expect(suppress([0, 20, 5, 20], [0, 20, 5, 20], 10)).toEqual([false, true, true, false]);
  });

  it('hides every cell when the only other cells are zero', () => {
    expect(suppress([0, 5, 0], [0, 5, 0], 10)).toEqual([true, true, true]);
  });

  it('lists no tiers for a distribution of fewer than ten people, however many results, and suppresses the tiers of a larger one', () => {
    const row = { instrumentCode: 'household_assessment', versionNumber: 1, sectionCode: null, ruleSetCode: 'household_assessment_total', ruleSetVersion: 1 };
    // Six people scored in both waves: twelve results, still six people.
    const [small] = buildDistributions(
      [
        { ...row, tierCode: 'vulnerable', count: 6, members: 3, distributionMembers: 6 },
        { ...row, tierCode: 'flourishing', count: 6, members: 3, distributionMembers: 6 },
      ],
      10,
    );
    const [large] = buildDistributions(
      [
        { ...row, tierCode: 'vulnerable', count: 4, members: 4, distributionMembers: 46 },
        { ...row, tierCode: 'transitional', count: 30, members: 30, distributionMembers: 46 },
        { ...row, tierCode: 'flourishing', count: 12, members: 12, distributionMembers: 46 },
      ],
      10,
    );

    expect(small).toMatchObject({ total: null, suppressed: true, tiers: [] });
    expect(large).toMatchObject({ total: 46, suppressed: false });
    expect(large.tiers).toEqual([
      { tier: 'flourishing', count: null, suppressed: true },
      { tier: 'transitional', count: 30, suppressed: false },
      { tier: 'vulnerable', count: null, suppressed: true },
    ]);
  });

  it('rounds a completion rate to four decimals and has none without a denominator', () => {
    expect(completionRate(2, 3)).toBe(0.6667);
    expect(completionRate(0, 0)).toBeNull();
  });
});

describe('the overview is counted from the population', () => {
  it('counts enrolment from the households and members the participation page lists', async () => {
    const { enrollment } = await overview(null);
    const households = await all<{ status: string; rosterConfirmedAt: string | null; activeMemberCount: number }>(
      backend,
      '/api/v1/admin/participation',
      'program_administrator',
    );
    const taking = households.filter((household) => household.status === 'active');
    const withdrawn = households.filter((household) => household.status === 'withdrawn');

    expect(households).toHaveLength(population.households.length);
    expect(enrollment).toEqual({
      households: households.length,
      activeHouseholds: taking.length,
      withdrawnHouseholds: withdrawn.length,
      rosterConfirmedHouseholds: households.filter((household) => household.rosterConfirmedAt !== null).length,
      // As the API counts them: the active members of the households that have not withdrawn.
      activeMembers: taking.reduce((sum, household) => sum + household.activeMemberCount, 0),
    });
    expect(enrollment.activeHouseholds + enrollment.withdrawnHouseholds).toBe(enrollment.households);
    // The participation list still shows a withdrawn household's members; the overview does not count them.
    expect(withdrawn.reduce((sum, household) => sum + household.activeMemberCount, 0)).toBeGreaterThan(0);
    expect(enrollment.activeMembers).toBeLessThan(population.members.filter((member) => member.active).length);
  });

  it.each(SCOPES)('counts assignments by status as the assessments page lists them (wave %s)', async (waveId) => {
    const { assignments } = await overview(waveId);
    const rows = await all<{ assignmentStatus: string; sessionStatus: string | null }>(backend, '/api/v1/admin/assessments', 'program_administrator', { waveId });

    expect(assignments.total).toBe(rows.length);
    for (const status of STATUSES) {
      expect(assignments[status]).toBe(rows.filter((row) => row.assignmentStatus === STATUS_CODES[status]).length);
    }
    expect(STATUSES.reduce((sum, status) => sum + assignments[status], 0)).toBe(assignments.total);
    expect(assignments.started).toBeGreaterThanOrEqual(assignments.inProgress + assignments.completed);
    expect(assignments.started).toBeLessThanOrEqual(assignments.total);
    expect(assignments.completionRate).toBe(completionRate(assignments.completed, assignments.total - assignments.cancelled));
    expect(assignments.total).toBe(population.assessments.filter((row) => waveId === null || row.waveId === waveId).length);
  });

  it('makes all waves the sum of the waves', async () => {
    const whole = await overview(null);
    const waves = await Promise.all(MOCK_WAVES.map((wave) => overview(wave.id)));

    for (const field of ['total', 'started', ...STATUSES] as const) {
      expect(waves.reduce((sum, wave) => sum + wave.assignments[field], 0)).toBe(whole.assignments[field]);
    }
    for (const row of whole.byVersion) {
      const same = waves.flatMap((wave) => wave.byVersion.filter((candidate) => candidate.instrumentCode === row.instrumentCode && candidate.sectionCode === row.sectionCode));
      expect(same.reduce((sum, candidate) => sum + candidate.total, 0)).toBe(row.total);
      expect(same.reduce((sum, candidate) => sum + candidate.completed, 0)).toBe(row.completed);
    }
  });

  it.each(SCOPES)('counts each version and module on its own, without cancelled assignments (wave %s)', async (waveId) => {
    const { assignments, byVersion } = await overview(waveId);

    expect(byVersion.reduce((sum, row) => sum + row.total, 0)).toBe(assignments.total - assignments.cancelled);
    expect(byVersion.reduce((sum, row) => sum + row.completed, 0)).toBe(assignments.completed);
    for (const row of byVersion) expect(row.completionRate).toBe(completionRate(row.completed, row.total));
    expect(byVersion.map((row) => `${row.instrumentCode}:${row.sectionCode ?? ''}`)).toEqual([
      'abu_dhabi_family_survey:A',
      'abu_dhabi_family_survey:B',
      'abu_dhabi_family_survey:C',
      'household_assessment:',
    ]);
  });

  it.each(SCOPES)('makes the trend points add up to the completed count in the window (wave %s)', async (waveId) => {
    for (const interval of ['month', 'week']) {
      const body = await overview(waveId, { interval });

      expect(body.completionTrend.reduce((sum, point) => sum + point.completed, 0)).toBe(body.assignments.completed);
    }
    const september = await overview(waveId, { from: '2026-09-01', to: '2026-09-30', interval: 'week' });
    const inSeptember = population.assessments.filter(
      (row) => (waveId === null || row.waveId === waveId) && row.assignmentStatus === 'completed' && row.completedAt?.slice(0, 7) === '2026-09',
    );
    expect(september.completionTrend.reduce((sum, point) => sum + point.completed, 0)).toBe(inSeptember.length);
  });

  it('gives each household the assignments the overview counts', async () => {
    const { assignments } = await overview(null);
    const households = await all<{ assignments: Record<(typeof STATUSES)[number], number> }>(backend, '/api/v1/admin/participation', 'program_administrator');

    for (const status of STATUSES) {
      expect(households.reduce((sum, household) => sum + household.assignments[status], 0)).toBe(assignments[status]);
    }
  });

  it.each(SCOPES)('breaks the same assignments down by age band and employment status (wave %s)', async (waveId) => {
    const { assignments, demographics } = await overview(waveId);

    for (const rows of [demographics.byAgeBand, demographics.byEmploymentStatus]) {
      // Where no cell is hidden, the breakdown adds up to the assignments that were not cancelled.
      if (rows.every((row) => row.total !== null)) expect(rows.reduce((sum, row) => sum + (row.total ?? 0), 0)).toBe(assignments.total - assignments.cancelled);
      if (rows.every((row) => row.completed !== null)) expect(rows.reduce((sum, row) => sum + (row.completed ?? 0), 0)).toBe(assignments.completed);
      for (const row of rows) {
        expect(row.suppressed).toBe(row.total === null || row.completed === null);
        expect(row.completionRate).toBe(row.suppressed ? null : completionRate(row.completed ?? 0, row.total ?? 0));
      }
    }
    expect(demographics.byAgeBand.map((row) => row.group)).toEqual(['18_29', '30_44', '45_59', '60_plus']);
    expect(demographics.byEmploymentStatus.map((row) => row.group)).toEqual([
      'employed',
      'self_employed',
      'unemployed',
      'student',
      'homemaker',
      'retired',
      'other',
      'not_recorded',
    ]);
  });
});

describe('scores use one vocabulary', () => {
  it('scores the household assessment only, with every band following its total', async () => {
    const rows = await results(null);

    expect(rows.length).toBeGreaterThan(0);
    for (const row of rows) {
      // Its rule set rejects unanswered items and it has no non-substantive option, so every result is complete.
      expect(row).toMatchObject({ instrumentCode: HOUSEHOLD_ASSESSMENT.code, sectionCode: null, state: 'complete' });
      const total = row.totalScore as number;
      expect(Number.isInteger(total) && total >= 39 && total <= 156).toBe(true);
      expect(row.tierCode).toBe(total <= 78 ? 'vulnerable' : total <= 117 ? 'transitional' : 'flourishing');
      expect(row.tierCode).toBe(bandOf(total));
    }
    for (const band of ['vulnerable', 'transitional', 'flourishing']) expect(rows.some((row) => row.tierCode === band)).toBe(true);
    expect(HOUSEHOLD_BANDS).toEqual([
      { code: 'vulnerable', min: 39, max: 78 },
      { code: 'transitional', min: 79, max: 117 },
      { code: 'flourishing', min: 118, max: 156 },
    ]);
  });

  it.each(SCOPES)('counts the same tiered results on analytics as the results page lists (wave %s)', async (waveId) => {
    const tiered = (await results(waveId)).filter((row) => row.state === 'complete' && row.tierCode !== null);
    const list = await distributions(waveId);

    expect(list).toHaveLength(1);
    const [distribution] = list;
    expect(distribution).toMatchObject({ instrumentCode: 'household_assessment', sectionCode: null, ruleSetCode: 'household_assessment_total', suppressed: false });
    expect(distribution.total).toBe(tiered.length);
    for (const tier of distribution.tiers) {
      if (!tier.suppressed) expect(tier.count).toBe(tiered.filter((row) => row.tierCode === tier.tier).length);
    }
    expect(distribution.tiers.map((tier) => tier.tier)).toEqual([...distribution.tiers.map((tier) => tier.tier)].sort());
  });

  it('gives every scored session one result, and no module a score', async () => {
    const rows = await results(null);
    const scored = (await all<{ scored: boolean; sessionStatus: string | null; instrumentCode: string; sectionCode: string | null }>(
      backend,
      '/api/v1/admin/assessments',
      'program_administrator',
    )).filter((row) => row.scored);

    expect(rows).toHaveLength(scored.length);
    expect(new Set(rows.map((row) => row.sessionId)).size).toBe(rows.length);
    for (const row of scored) expect(row).toMatchObject({ sessionStatus: 'scored', instrumentCode: 'household_assessment', sectionCode: null });
  });
});

describe('the mock sends only what the API would', () => {
  it.each(SCOPES)('never sends a count from 1 to 9, or exactly one hidden cell in a group (wave %s)', async (waveId) => {
    const { demographics } = await overview(waveId);
    const groups: (number | null)[][] = [];
    for (const rows of [demographics.byAgeBand, demographics.byEmploymentStatus]) {
      groups.push(rows.map((row) => row.total), rows.map((row) => row.completed));
    }
    for (const distribution of await distributions(waveId)) {
      expect(distribution.total === null || distribution.total === 0 || distribution.total >= MINIMUM_GROUP_SIZE).toBe(true);
      groups.push(distribution.tiers.map((tier) => tier.count));
      for (const tier of distribution.tiers) expect(tier.suppressed).toBe(tier.count === null);
    }

    expect(groups.length).toBeGreaterThanOrEqual(5);
    for (const group of groups) {
      for (const count of group) expect(count === null || count === 0 || count >= MINIMUM_GROUP_SIZE).toBe(true);
      expect(group.filter((count) => count === null).length).not.toBe(1);
    }
  });

  it.each(SCOPES)('shows a demographic cell only when ten or more people are behind it (wave %s)', async (waveId) => {
    const { demographics } = await overview(waveId);
    const members = new Map(population.members.map((member) => [member.memberId, member]));
    // Counted here straight from the population: one member can hold several assignments (modules, and both waves).
    const people = (match: (memberId: string) => boolean, completedOnly: boolean) =>
      new Set(
        population.assessments
          .filter(
            (row) =>
              (waveId === null || row.waveId === waveId) &&
              row.assignmentStatus !== 'cancelled' &&
              (!completedOnly || row.assignmentStatus === 'completed') &&
              match(row.memberId),
          )
          .map((row) => row.memberId),
      ).size;

    for (const row of demographics.byEmploymentStatus) {
      const inGroup = (memberId: string) => (members.get(memberId)?.employmentStatus ?? 'not_recorded') === row.group;
      if (row.total !== null && row.total > 0) expect(people(inGroup, false)).toBeGreaterThanOrEqual(MINIMUM_GROUP_SIZE);
      if (row.completed !== null && row.completed > 0) expect(people(inGroup, true)).toBeGreaterThanOrEqual(MINIMUM_GROUP_SIZE);
    }
  });

  it.each(SCOPES)('shows a distribution and each band only when ten or more people are behind it (wave %s)', async (waveId) => {
    const list = await distributions(waveId);
    expect(list.length).toBeGreaterThan(0);
    for (const distribution of list) {
      const scored = population.results.filter(
        (result) => (waveId === null || result.waveId === waveId) && result.state === 'complete' && result.tierCode !== null && result.ruleSetCode === distribution.ruleSetCode,
      );
      const people = (rows: typeof scored) => new Set(rows.map((result) => result.memberId)).size;
      if (!distribution.suppressed) expect(people(scored)).toBeGreaterThanOrEqual(MINIMUM_GROUP_SIZE);
      for (const tier of distribution.tiers) {
        if (!tier.suppressed && (tier.count ?? 0) > 0) expect(people(scored.filter((result) => result.tierCode === tier.tier))).toBeGreaterThanOrEqual(MINIMUM_GROUP_SIZE);
      }
    }
    // Across both waves some heads were scored twice, so results and people differ and the rule above is exercised.
    if (waveId === null) expect(population.results.length).toBeGreaterThan(new Set(population.results.map((result) => result.memberId)).size);
  });

  it('uses the platform codes: modules A, B and C, and no abandoned session', async () => {
    const rows = await all<{ instrumentCode: string; sectionCode: string | null; sessionStatus: string | null }>(
      backend,
      '/api/v1/admin/assessments',
      'program_administrator',
    );

    expect(new Set(rows.map((row) => `${row.instrumentCode}:${row.sectionCode ?? ''}`))).toEqual(
      new Set(['household_assessment:', 'abu_dhabi_family_survey:A', 'abu_dhabi_family_survey:B', 'abu_dhabi_family_survey:C']),
    );
    expect(rows.some((row) => row.sessionStatus === 'abandoned')).toBe(false);
    expect(population.assessments.some((row) => row.session === 'abandoned')).toBe(true);
  });
});

describe('exports carry the fields the platform declares', () => {
  // The backend's allowlist, read from its source: the mock must offer exactly these columns, in this order.
  const source = readFileSync(new URL('../../../../../backend/FamilyResilience.Application/Analytics/ExportCatalog.cs', import.meta.url), 'utf8');
  const declared = (table: string) => {
    const block = new RegExp(`${table} =\\s*\\[([\\s\\S]*?)\\];`).exec(source)?.[1] ?? '';
    return [...block.matchAll(/^\s*\("([a-z_]+)",/gm)].map((match) => match[1]);
  };

  it('offers the backend catalog, column for column', async () => {
    const catalog = await get<{ datasets: { code: string; columns: string[] }[]; filters: string[] }>(backend, '/api/v1/admin/exports/catalog', 'researcher');

    expect(declared('SessionColumns')).toContain('non_substantive_count');
    expect(declared('ScoreColumns')).toEqual(expect.arrayContaining(['incomplete_reason', 'excluded_count']));
    expect(catalog.datasets).toEqual([
      { code: 'assessment_sessions', columns: declared('SessionColumns') },
      { code: 'score_results', columns: declared('ScoreColumns') },
    ]);
    expect(catalog.filters).toEqual(['wave_id', 'instrument_code']);
  });

  async function download(dataset: string, columns: string[]): Promise<Record<string, string>[]> {
    const key = dataset === 'score_results' ? '9a9a9a9a-0000-4000-8000-000000000001' : '9a9a9a9a-0000-4000-8000-000000000002';
    const created = await backend.handle({
      method: 'POST',
      apiPath: '/api/v1/admin/exports',
      query: new URLSearchParams(),
      body: { dataset, columns, filters: { waveId: null, instrumentCode: null } },
      persona: 'researcher',
      idempotencyKey: key,
      now,
    });
    const { id } = (await created.json()) as { id: string };
    const later = new Date(now.getTime() + 60_000);
    const file = await backend.handle({ method: 'GET', apiPath: `/api/v1/admin/exports/${id}/file`, query: new URLSearchParams(), body: null, persona: 'researcher', idempotencyKey: null, now: later });
    const [header, ...lines] = (await file.text()).trimEnd().split('\r\n');
    expect(header.split(',')).toEqual(columns);
    return lines.map((line) => {
      const cells = line.split(',');
      expect(cells).toHaveLength(columns.length);
      return Object.fromEntries(columns.map((column, index) => [column, cells[index]]));
    });
  }

  it('writes every session column, with the non-substantive answers counted within the answers given', async () => {
    const rows = await download('assessment_sessions', declared('SessionColumns'));
    const items: Record<string, number> = { household_assessment: 39, A: 10, B: 13, C: 13 };

    expect(rows).toHaveLength(population.assessments.filter((row) => row.session === 'submitted' || row.session === 'scored').length);
    for (const row of rows) {
      const answered = Number(row.answered_count);
      expect(answered).toBe(items[row.section_code || row.instrument_code]);
      expect(Number(row.non_substantive_count)).toBeLessThanOrEqual(answered);
      if (row.instrument_code === 'household_assessment') expect(row.non_substantive_count).toBe('0');
      expect(['18_29', '30_44', '45_59', '60_plus']).toContain(row.age_band);
      expect(row.employment_status).not.toBe('');
      expect(['true', 'false']).toContain(row.is_head_of_household);
    }
    expect(rows.some((row) => Number(row.non_substantive_count) > 0)).toBe(true);
  });

  it('writes every score column: complete results with nothing excluded and no reason to be incomplete', async () => {
    const rows = await download('score_results', declared('ScoreColumns'));

    expect(rows).toHaveLength(population.results.length);
    for (const row of rows) {
      expect(row).toMatchObject({ score_status: 'complete', incomplete_reason: '', excluded_count: '0' });
      expect(row.tier_code).toBe(bandOf(Number(row.total_score)));
      expect(row.employment_status).not.toBe('');
    }
  });
});

describe('safety alerts point at the population', () => {
  type Alert = { householdReference: string | null; category: string; source: string; signalCode: string | null; firstDetectedAt: string };
  const alerts = (target = backend) => all<Alert>(target, '/api/v1/admin/safety-alerts', 'system_administrator');

  it('names only active households, and raises a score alert only for a result in the lowest band, when it was computed', async () => {
    const active = new Set(population.households.filter((household) => household.status === 'active').map((household) => household.reference));
    const listed = await all<Result & { householdReference: string; computedAt: string }>(backend, '/api/v1/admin/results', 'system_administrator');
    const list = await alerts();

    expect(list.length).toBeGreaterThan(0);
    for (const alert of list) expect(active.has(alert.householdReference ?? '')).toBe(true);
    const scores = list.filter((alert) => alert.category === 'low_resilience_score');
    expect(scores.length).toBeGreaterThan(0);
    for (const alert of scores) {
      expect(alert).toMatchObject({ source: 'household_assessment', signalCode: HOUSEHOLD_BANDS[0].code });
      expect(
        listed.some(
          (row) =>
            row.householdReference === alert.householdReference &&
            row.tierCode === HOUSEHOLD_BANDS[0].code &&
            row.computedAt === alert.firstDetectedAt,
        ),
      ).toBe(true);
    }
  });

  it('has no score alert until scoring is approved, and the same alerts from what people said', async () => {
    const plain = await alerts(createMockBackend('no_approved_scores'));
    const said = (await alerts()).filter((alert) => alert.category !== 'low_resilience_score');

    expect(plain.some((alert) => alert.category === 'low_resilience_score')).toBe(false);
    expect(plain).toEqual(said);
  });
});

describe('both scenarios share the population', () => {
  it('has the same households and assignments, with no score anywhere, until scoring is approved', async () => {
    const plain = createMockBackend('no_approved_scores');
    const unscored = buildPopulation(false);

    expect(unscored.households).toEqual(population.households);
    expect(unscored.members).toEqual(population.members);
    expect(unscored.assessments.map((row) => ({ ...row, session: null }))).toEqual(population.assessments.map((row) => ({ ...row, session: null })));
    expect(unscored.results).toEqual([]);
    expect(unscored.assessments.some((row) => row.session === 'scored')).toBe(false);
    expect(await distributions(null, plain)).toEqual([]);
    expect(await results(null, plain)).toEqual([]);
    expect((await overview(null, {}, plain)).assignments).toEqual((await overview(null)).assignments);
  });
});
