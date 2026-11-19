import 'server-only';

import { EMPLOYMENT_STATUSES, type AssignmentStatus, type MockAssessmentRow, type MockPopulation } from './fixtures';

/**
 * The mock's aggregates, counted from the synthetic population with the API's own definitions, so that the mock sends
 * only what the API would. This mirrors `GetAnalyticsOverview`, `Suppression` and `ScoreDistributions` in
 * `backend/FamilyResilience.Application/Analytics` and the queries behind them; it is development code and never runs
 * against real data, where the platform alone counts and suppresses.
 */

/** The API's `Analytics:MinimumGroupSize`. */
export const MINIMUM_GROUP_SIZE = 10;

/**
 * Small-cell suppression for one group of counts that add up to a published total, decided on people as the API's
 * `Suppression.Apply` decides it (D-58): a cell with a non-zero count is hidden when fewer than `minimum` distinct
 * members are behind it, whatever the count itself (one member can hold several assignments or results). When exactly
 * one cell is hidden, the non-zero cell covering the next fewest people (then the smaller count, then the earlier
 * cell) is hidden too, so the hidden one cannot be recovered by subtraction; when there is no such cell, every cell is
 * hidden.
 */
export function suppress(counts: readonly number[], members: readonly number[], minimum: number): boolean[] {
  if (members.length !== counts.length) throw new Error('Every cell needs its member count.');
  const hidden = counts.map((count, index) => count > 0 && members[index] < minimum);
  if (hidden.filter(Boolean).length !== 1) return hidden;
  const [complement] = counts
    .map((_, index) => index)
    .filter((index) => !hidden[index] && counts[index] > 0)
    .sort((a, b) => members[a] - members[b] || counts[a] - counts[b] || a - b);
  if (complement === undefined) return counts.map(() => true);
  hidden[complement] = true;
  return hidden;
}

/** Completed over the given denominator to four decimal places; null when there is nothing to divide by. */
export function completionRate(completed: number, total: number): number | null {
  return total === 0 ? null : Math.round((completed / total) * 10_000) / 10_000;
}

/** The API's age bands: the birthday counts as the first of the birth month. `asOf` is a UTC day or timestamp. */
export function ageBandOf(birthYear: number, birthMonth: number, asOf: string): string {
  const age = Number(asOf.slice(0, 4)) - birthYear - (Number(asOf.slice(5, 7)) < birthMonth ? 1 : 0);
  return age < 18 ? 'under_18' : age < 30 ? '18_29' : age < 45 ? '30_44' : age < 60 ? '45_59' : '60_plus';
}

const ADULT_AGE_BANDS = ['18_29', '30_44', '45_59', '60_plus'];
const NOT_RECORDED = 'not_recorded';
const EMPLOYMENT_GROUPS = [...EMPLOYMENT_STATUSES, NOT_RECORDED];

const ordinal = (a: string, b: string) => (a < b ? -1 : a > b ? 1 : 0);
const day = (date: Date) => date.toISOString().slice(0, 10);

/**
 * Enrolment as the API's `EnrollmentAsync` counts it: every household, by status and roster confirmation, and the
 * active members of the households that have not withdrawn (a withdrawn household's members no longer take part,
 * though their rows stay active and the participation list still shows them).
 */
export function countEnrollment(population: MockPopulation) {
  const taking = new Set(population.households.filter((household) => household.status === 'active').map((household) => household.householdId));
  return {
    households: population.households.length,
    activeHouseholds: taking.size,
    withdrawnHouseholds: population.households.length - taking.size,
    rosterConfirmedHouseholds: population.households.filter((household) => household.rosterConfirmedAt !== null).length,
    activeMembers: population.members.filter((member) => member.active && taking.has(member.householdId)).length,
  };
}

export function countByStatus(rows: readonly MockAssessmentRow[]) {
  const count = (status: AssignmentStatus) => rows.filter((row) => row.assignmentStatus === status).length;
  return { assigned: count('assigned'), inProgress: count('in_progress'), completed: count('completed'), expired: count('expired'), cancelled: count('cancelled') };
}

/** `started` counts assignments with a session of any status, abandoned ones included. */
function countTotals(rows: readonly MockAssessmentRow[]) {
  const statuses = countByStatus(rows);
  return {
    total: rows.length,
    started: rows.filter((row) => row.session !== null).length,
    ...statuses,
    completionRate: completionRate(statuses.completed, rows.length - statuses.cancelled),
  };
}

/** One row per instrument version and module, in the API's order; cancelled assignments are left out of the total. */
function countVersions(rows: readonly MockAssessmentRow[]) {
  const groups = new Map<string, MockAssessmentRow[]>();
  for (const row of rows) {
    const key = `${row.instrumentCode}|${row.versionNumber}|${row.sectionCode ?? ''}`;
    groups.set(key, [...(groups.get(key) ?? []), row]);
  }
  return [...groups.values()]
    .map((group) => {
      const total = group.filter((row) => row.assignmentStatus !== 'cancelled').length;
      const completed = group.filter((row) => row.assignmentStatus === 'completed').length;
      const { instrumentCode, versionNumber, sectionCode } = group[0];
      return { instrumentCode, versionNumber, sectionCode, total, completed, completionRate: completionRate(completed, total) };
    })
    .sort(
      (a, b) => ordinal(a.instrumentCode, b.instrumentCode) || a.versionNumber - b.versionNumber || ordinal(a.sectionCode ?? '', b.sectionCode ?? ''),
    );
}

function periodStart(value: string, interval: 'month' | 'week'): string {
  const date = new Date(`${value}T00:00:00Z`);
  if (interval === 'month') date.setUTCDate(1);
  else date.setUTCDate(date.getUTCDate() - ((date.getUTCDay() + 6) % 7));
  return day(date);
}

/** Completed assignments per month or ISO week, for completions from `from` to `to` inclusive (UTC days). */
function countTrend(rows: readonly MockAssessmentRow[], from: string, to: string, interval: 'month' | 'week') {
  const completions = rows
    .filter((row) => row.assignmentStatus === 'completed' && row.completedAt !== null)
    .map((row) => (row.completedAt as string).slice(0, 10))
    .filter((completed) => completed >= from && completed <= to)
    .map((completed) => periodStart(completed, interval));
  const points: { periodStart: string; completed: number }[] = [];
  for (let period = new Date(`${periodStart(from, interval)}T00:00:00Z`); day(period) <= to; ) {
    const start = day(period);
    points.push({ periodStart: start, completed: completions.filter((completed) => completed === start).length });
    if (interval === 'month') period.setUTCMonth(period.getUTCMonth() + 1);
    else period.setUTCDate(period.getUTCDate() + 7);
  }
  return points;
}

interface GroupedAssignment {
  group: string;
  memberId: string;
  completed: boolean;
}

/**
 * One row per group, in `order`, with totals and completions suppressed column by column on the distinct members
 * behind each cell, as the API does. A member has one birth month and one employment status, so no member is counted
 * in two rows of a group.
 */
function breakdown(rows: readonly GroupedAssignment[], order: readonly string[], minimum: number) {
  const groups = [...order, ...new Set(rows.map((row) => row.group).filter((group) => !order.includes(group)))];
  const cells = groups.map((group) => {
    const all = rows.filter((row) => row.group === group);
    const done = all.filter((row) => row.completed);
    return {
      total: all.length,
      completed: done.length,
      members: new Set(all.map((row) => row.memberId)).size,
      membersCompleted: new Set(done.map((row) => row.memberId)).size,
    };
  });
  const hiddenTotals = suppress(
    cells.map((cell) => cell.total),
    cells.map((cell) => cell.members),
    minimum,
  );
  const hiddenCompleted = suppress(
    cells.map((cell) => cell.completed),
    cells.map((cell) => cell.membersCompleted),
    minimum,
  );
  return groups.map((group, index) => {
    const suppressed = hiddenTotals[index] || hiddenCompleted[index];
    return {
      group,
      total: hiddenTotals[index] ? null : cells[index].total,
      completed: hiddenCompleted[index] ? null : cells[index].completed,
      completionRate: suppressed ? null : completionRate(cells[index].completed, cells[index].total),
      suppressed,
    };
  });
}

export interface OverviewScope {
  waveId: string | null;
  from: string;
  to: string;
  interval: 'month' | 'week';
  /** The UTC day ages are worked out on. */
  today: string;
}

/** Everything the overview shows, counted from one wave's assignments or from all of them, and the whole enrolment. */
export function countOverview(population: MockPopulation, scope: OverviewScope) {
  const rows = population.assessments.filter((row) => scope.waveId === null || row.waveId === scope.waveId);
  const members = new Map(population.members.map((member) => [member.memberId, member]));
  // Assignments that were not cancelled, of members on a roster: the denominator of every demographic rate.
  const live = rows.filter((row) => row.assignmentStatus !== 'cancelled' && members.has(row.memberId));
  const byMember = (group: (memberId: string) => string): GroupedAssignment[] =>
    live.map((row) => ({ group: group(row.memberId), memberId: row.memberId, completed: row.assignmentStatus === 'completed' }));
  return {
    enrollment: countEnrollment(population),
    assignments: countTotals(rows),
    byVersion: countVersions(rows),
    completionTrend: countTrend(rows, scope.from, scope.to, scope.interval),
    demographics: {
      ageAsOf: scope.today,
      byAgeBand: breakdown(
        byMember((memberId) => {
          const member = members.get(memberId);
          return member ? ageBandOf(member.birthYear, member.birthMonth, scope.today) : NOT_RECORDED;
        }),
        ADULT_AGE_BANDS,
        MINIMUM_GROUP_SIZE,
      ),
      byEmploymentStatus: breakdown(
        byMember((memberId) => members.get(memberId)?.employmentStatus ?? NOT_RECORDED),
        EMPLOYMENT_GROUPS,
        MINIMUM_GROUP_SIZE,
      ),
    },
  };
}

/** The API's `TierCount`: results of one rule set in one tier, the distinct members behind them and behind the scope. */
export interface TierCount {
  instrumentCode: string;
  versionNumber: number;
  sectionCode: string | null;
  ruleSetCode: string;
  ruleSetVersion: number;
  tierCode: string;
  count: number;
  members: number;
  distributionMembers: number;
}

/**
 * Complete results with a tier, by scoring rule set and tier, for one wave or all of them, each with the distinct
 * members behind its tier and behind its whole distribution (someone scored in both waves has two results). Every
 * synthetic member with a result counts as having research-use consent, and a withdrawn household has no result.
 */
export function countTiers(population: MockPopulation, waveId: string | null): TierCount[] {
  type Cell = Omit<TierCount, 'count' | 'members' | 'distributionMembers'> & { scope: string; results: number; people: Set<string> };
  const cells = new Map<string, Cell>();
  const scopes = new Map<string, Set<string>>();
  for (const result of population.results) {
    if (result.state !== 'complete' || result.tierCode === null || (waveId !== null && result.waveId !== waveId)) continue;
    const { instrumentCode, versionNumber, sectionCode, ruleSetCode, ruleSetVersion, tierCode, memberId } = result;
    const scope = [instrumentCode, versionNumber, sectionCode ?? '', ruleSetCode, ruleSetVersion].join('|');
    const key = `${scope}|${tierCode}`;
    const cell = cells.get(key) ?? { instrumentCode, versionNumber, sectionCode, ruleSetCode, ruleSetVersion, tierCode, scope, results: 0, people: new Set() };
    cell.results++;
    cell.people.add(memberId);
    cells.set(key, cell);
    scopes.set(scope, (scopes.get(scope) ?? new Set()).add(memberId));
  }
  return [...cells.values()].map(({ scope, results, people, ...row }) => ({
    ...row,
    count: results,
    members: people.size,
    distributionMembers: scopes.get(scope)?.size ?? 0,
  }));
}

/**
 * One distribution per scoring rule set, as the API's `ScoreDistributions.Build` makes them: a distribution covering
 * fewer than `minimum` people is hidden whole, including which tiers occur; otherwise its tiers, in code order, are
 * suppressed like any breakdown, on the people behind each tier. The counts shown stay counts of results.
 */
export function buildDistributions(rows: readonly TierCount[], minimum: number) {
  const groups = new Map<string, TierCount[]>();
  for (const row of rows) {
    const key = [row.instrumentCode, row.versionNumber, row.sectionCode ?? '', row.ruleSetCode, row.ruleSetVersion].join('|');
    groups.set(key, [...(groups.get(key) ?? []), row]);
  }
  return [...groups.values()]
    .sort(
      ([a], [b]) =>
        ordinal(a.instrumentCode, b.instrumentCode) ||
        a.versionNumber - b.versionNumber ||
        ordinal(a.sectionCode ?? '', b.sectionCode ?? '') ||
        ordinal(a.ruleSetCode, b.ruleSetCode) ||
        a.ruleSetVersion - b.ruleSetVersion,
    )
    .map((group) => {
      const tiers = [...group].sort((a, b) => ordinal(a.tierCode, b.tierCode));
      const total = tiers.reduce((sum, tier) => sum + tier.count, 0);
      const totalHidden = total > 0 && Math.max(...tiers.map((tier) => tier.distributionMembers)) < minimum;
      const hidden = suppress(
        tiers.map((tier) => tier.count),
        tiers.map((tier) => tier.members),
        minimum,
      );
      const { instrumentCode, versionNumber, sectionCode, ruleSetCode, ruleSetVersion } = group[0];
      return {
        instrumentCode,
        versionNumber,
        sectionCode,
        ruleSetCode,
        ruleSetVersion,
        total: totalHidden ? null : total,
        suppressed: totalHidden,
        tiers: totalHidden ? [] : tiers.map((tier, index) => ({ tier: tier.tierCode, count: hidden[index] ? null : tier.count, suppressed: hidden[index] })),
      };
    });
}
