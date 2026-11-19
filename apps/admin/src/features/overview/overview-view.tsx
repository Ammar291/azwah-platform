'use client';

import { useId } from 'react';

import { PageHeader } from '@/components/app-shell/page-header';
import { DateInput } from '@/components/data/date-input';
import { Field, NativeSelect } from '@/components/data/field';
import { FigureStat } from '@/components/data/figure-stat';
import { ErrorState } from '@/components/states/error-state';
import { TableSkeleton } from '@/components/states/table-skeleton';
import { Skeleton } from '@/components/ui/skeleton';
import { WaveSelect } from '@/features/filters/wave-select';
import { useOverview, type OverviewFilters } from '@/lib/api/queries';
import { fill } from '@/lib/format';
import { useI18n } from '@/lib/i18n-context';
import { useSearchState } from '@/lib/url-state';

import { AssignmentFlow } from './assignment-flow';
import { CompletionTrend } from './completion-trend';
import { DemographicTable } from './demographic-table';
import { isReversed, parseOverviewFilters, serializeOverviewFilters } from './overview-filters';
import { VersionCompletion } from './version-completion';

/** The analytics overview for researchers and program administrators: counts and completion, never scores. */
export function OverviewView() {
  const { t, f } = useI18n();
  const [filters, setFilters] = useSearchState(parseOverviewFilters, serializeOverviewFilters);
  const reversed = isReversed(filters);
  const overview = useOverview(filters, { enabled: !reversed });
  const enrolmentId = useId();
  const ids = { wave: useId(), from: useId(), to: useId(), interval: useId() };
  // An updater, so two filters changed in quick succession both stick (see useSearchState).
  const update = (change: Partial<OverviewFilters>) => setFilters((current) => ({ ...current, ...change }));
  const data = overview.data;

  return (
    <>
      <PageHeader title={t.overview.title} description={t.overview.description} />

      <section aria-label={t.common.filters} className="grid gap-4 rounded-lg border border-border bg-card p-4 sm:grid-cols-2 lg:grid-cols-4">
        <WaveSelect id={ids.wave} value={filters.waveId} onChange={(waveId) => update({ waveId })} hint={t.filters.noCohorts} />
        <Field id={ids.from} label={t.filters.from} error={reversed ? t.filters.reversedRange : null}>
          <DateInput id={ids.from} value={filters.from} aria-invalid={reversed || undefined} onCommit={(from) => update({ from })} />
        </Field>
        <Field id={ids.to} label={t.filters.to}>
          <DateInput id={ids.to} value={filters.to} onCommit={(to) => update({ to })} />
        </Field>
        <Field id={ids.interval} label={t.filters.interval} hint={t.common.utcNote}>
          <NativeSelect
            id={ids.interval}
            value={filters.interval}
            onChange={(event) => update({ interval: event.target.value === 'week' ? 'week' : 'month' })}
          >
            <option value="month">{t.filters.monthly}</option>
            <option value="week">{t.filters.weekly}</option>
          </NativeSelect>
        </Field>
      </section>

      {reversed ? null : overview.isError ? (
        <ErrorState error={overview.error} onRetry={() => void overview.refetch()} />
      ) : !data ? (
        <div className="flex flex-col gap-4">
          <Skeleton className="h-40 w-full" />
          <TableSkeleton columns={4} rows={4} />
        </div>
      ) : (
        <div className={overview.isPlaceholderData ? 'flex flex-col gap-6 opacity-60 transition-opacity' : 'flex flex-col gap-6'} aria-busy={overview.isFetching}>
          <section aria-labelledby={enrolmentId} className="flex flex-col gap-4 rounded-lg border border-border bg-card px-5 py-5">
            <div className="flex flex-col gap-1">
              <h2 id={enrolmentId} className="text-base font-semibold">
                {t.overview.enrolment}
              </h2>
              <p className="text-sm text-muted-foreground">{t.overview.enrolmentNote}</p>
            </div>
            <div className="grid gap-6 sm:grid-cols-2">
              <FigureStat
                label={t.overview.households}
                value={f.number(data.enrollment.households)}
                detail={fill(t.overview.householdsDetail, {
                  active: f.number(data.enrollment.activeHouseholds),
                  withdrawn: f.number(data.enrollment.withdrawnHouseholds),
                  confirmed: f.number(data.enrollment.rosterConfirmedHouseholds),
                })}
              />
              <FigureStat label={t.overview.members} value={f.number(data.enrollment.activeMembers)} detail={t.overview.membersDetail} />
            </div>
          </section>

          <AssignmentFlow assignments={data.assignments} />
          <CompletionTrend overview={data} />
          <VersionCompletion rows={data.byVersion} />

          <section className="flex flex-col gap-3">
            <p className="text-sm text-muted-foreground">
              <span>{fill(t.display.suppressedNote, { n: data.minimumGroupSize })}</span>{' '}
              <span>{fill(t.overview.ageAsOf, { date: f.day(data.demographics.ageAsOf) })}</span>
            </p>
            <div className="grid gap-6 lg:grid-cols-2">
              <DemographicTable title={t.overview.byAgeTitle} kind="age" rows={data.demographics.byAgeBand} minimumGroupSize={data.minimumGroupSize} />
              <DemographicTable
                title={t.overview.byEmploymentTitle}
                kind="employment"
                rows={data.demographics.byEmploymentStatus}
                minimumGroupSize={data.minimumGroupSize}
              />
            </div>
          </section>
          <p className="text-xs text-muted-foreground">{fill(t.overview.generatedAt, { time: f.dateTime(data.generatedAt) })}</p>
        </div>
      )}
    </>
  );
}
