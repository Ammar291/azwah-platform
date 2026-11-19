'use client';

import { useId, useState, type FormEvent } from 'react';

import { PageHeader } from '@/components/app-shell/page-header';
import { Field, NativeInput, NativeSelect } from '@/components/data/field';
import { Pagination } from '@/components/data/pagination';
import { StatusBadge } from '@/components/data/status-badge';
import { EmptyState } from '@/components/states/empty-state';
import { ErrorState } from '@/components/states/error-state';
import { TableSkeleton } from '@/components/states/table-skeleton';
import { Button } from '@/components/ui/button';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { WaveSelect } from '@/features/filters/wave-select';
import { useAssessments, type AssessmentFilters } from '@/lib/api/queries';
import { ASSIGNMENT_STATUSES } from '@/lib/api/schemas';
import { fill } from '@/lib/format';
import { useI18n } from '@/lib/i18n-context';
import { useSearchState } from '@/lib/url-state';
import { useLastPage } from '@/lib/use-last-page';

import {
  applyDraft,
  draftOf,
  parseAssessmentFilters,
  serializeAssessmentFilters,
  type AssessmentDraft,
  type DraftError,
} from './assessment-filters';

/** Assessment progress for program administrators: server-side filters and paging, no answers or scores. */
export function AssessmentsView() {
  const { t } = useI18n();
  const [filters, setFilters] = useSearchState(parseAssessmentFilters, serializeAssessmentFilters);
  const assessments = useAssessments(filters);
  const filterKey = JSON.stringify({ ...filters, page: 0 });
  const pastEnd = useLastPage(assessments.data, !assessments.isPlaceholderData, (page) => setFilters({ ...filters, page }));

  return (
    <>
      <PageHeader title={t.monitoring.assessmentsTitle} description={t.monitoring.assessmentsDescription} crumbs={[]} />
      <AssessmentFilterForm key={filterKey} filters={filters} onApply={setFilters} />
      {assessments.isError ? (
        <ErrorState error={assessments.error} onRetry={() => void assessments.refetch()} />
      ) : !assessments.data || pastEnd ? (
        <TableSkeleton columns={8} />
      ) : assessments.data.totalCount === 0 ? (
        <EmptyState title={t.monitoring.emptyTitle} description={t.monitoring.emptyBody} />
      ) : (
        <div className="flex flex-col gap-3" aria-busy={assessments.isFetching}>
          <AssessmentTable rows={assessments.data.items} />
          <Pagination
            page={assessments.data.page}
            pageSize={assessments.data.pageSize}
            totalCount={assessments.data.totalCount}
            onPage={(page) => setFilters({ ...filters, page })}
          />
        </div>
      )}
    </>
  );
}

function AssessmentFilterForm({ filters, onApply }: { filters: AssessmentFilters; onApply: (filters: AssessmentFilters) => void }) {
  const { t } = useI18n();
  const [draft, setDraft] = useState<AssessmentDraft>(() => draftOf(filters));
  const [errors, setErrors] = useState<Partial<Record<keyof AssessmentDraft, DraftError>>>({});
  const ids = { wave: useId(), code: useId(), version: useId(), status: useId(), from: useId(), to: useId() };
  const change = (field: keyof AssessmentDraft) => (value: string) => setDraft((current) => ({ ...current, [field]: value }));
  const message = (error: DraftError | undefined) =>
    error === undefined ? null : error === 'reversedRange' ? t.filters.reversedRange : t.monitoring[error];
  const hasCode = draft.instrumentCode.trim() !== '';

  function submit(event: FormEvent) {
    event.preventDefault();
    const result = applyDraft(draft);
    if (!result.ok) {
      setErrors(result.errors);
      return;
    }
    setErrors({});
    onApply(result.filters);
  }

  return (
    <form aria-label={t.common.filters} onSubmit={submit} noValidate className="flex flex-col gap-4 rounded-lg border border-border bg-card p-4">
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        <WaveSelect id={ids.wave} value={draft.waveId || null} onChange={(waveId) => change('waveId')(waveId ?? '')} />
        <Field id={ids.code} label={t.monitoring.instrumentCode} hint={t.monitoring.instrumentCodeHint} error={message(errors.instrumentCode)}>
          <NativeInput
            id={ids.code}
            value={draft.instrumentCode}
            dir="ltr"
            autoComplete="off"
            spellCheck={false}
            aria-invalid={errors.instrumentCode ? true : undefined}
            onChange={(event) => change('instrumentCode')(event.target.value)}
          />
        </Field>
        <Field
          id={ids.version}
          label={t.monitoring.versionNumber}
          hint={hasCode ? undefined : t.monitoring.versionNumberHint}
          error={message(errors.versionNumber)}
        >
          <NativeInput
            id={ids.version}
            type="number"
            inputMode="numeric"
            min={1}
            disabled={!hasCode}
            value={hasCode ? draft.versionNumber : ''}
            aria-invalid={errors.versionNumber ? true : undefined}
            onChange={(event) => change('versionNumber')(event.target.value)}
          />
        </Field>
        <Field id={ids.status} label={t.monitoring.status}>
          <NativeSelect id={ids.status} value={draft.status} onChange={(event) => change('status')(event.target.value)}>
            <option value="">{t.monitoring.allStatuses}</option>
            {ASSIGNMENT_STATUSES.map((status) => (
              <option key={status} value={status}>
                {t.status.assignment[status]}
              </option>
            ))}
          </NativeSelect>
        </Field>
        <Field id={ids.from} label={t.monitoring.assignedFrom} error={message(errors.assignedFrom)}>
          <NativeInput id={ids.from} type="date" value={draft.assignedFrom} onChange={(event) => change('assignedFrom')(event.target.value)} />
        </Field>
        <Field id={ids.to} label={t.monitoring.assignedTo} hint={t.common.utcNote}>
          <NativeInput id={ids.to} type="date" value={draft.assignedTo} onChange={(event) => change('assignedTo')(event.target.value)} />
        </Field>
      </div>
      <div className="flex flex-wrap gap-2">
        <Button type="submit" size="lg">
          {t.monitoring.apply}
        </Button>
        <Button
          type="button"
          size="lg"
          variant="ghost"
          onClick={() => onApply(parseAssessmentFilters(new URLSearchParams()))}
        >
          {t.common.clear}
        </Button>
      </div>
    </form>
  );
}

function AssessmentTable({ rows }: { rows: NonNullable<ReturnType<typeof useAssessments>['data']>['items'] }) {
  const { t, f } = useI18n();
  const columns = t.monitoring.columns;
  return (
    <div className="rounded-lg border border-border bg-card">
      <Table aria-label={t.monitoring.tableLabel}>
        <TableHeader>
          <TableRow>
            <TableHead className="ps-4">{columns.instrument}</TableHead>
            <TableHead>{columns.wave}</TableHead>
            <TableHead>{columns.assignment}</TableHead>
            <TableHead>{columns.session}</TableHead>
            <TableHead>{columns.assigned}</TableHead>
            <TableHead>{columns.completed}</TableHead>
            <TableHead>{columns.scored}</TableHead>
            <TableHead className="pe-4">{columns.household}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {rows.map((row) => (
            <TableRow key={row.assignmentId}>
              {/* Version and module under the code, as on the household results: ten columns did not fit at 1440 px. */}
              <TableCell className="ps-4">
                <span className="flex flex-col">
                  <span className="font-medium">{row.instrumentCode}</span>
                  <span className="text-xs text-muted-foreground">
                    {row.sectionCode === null
                      ? fill(t.monitoring.version, { version: f.number(row.versionNumber) })
                      : fill(t.monitoring.versionModule, { version: f.number(row.versionNumber), module: row.sectionCode })}
                  </span>
                </span>
              </TableCell>
              <TableCell>{row.waveCode}</TableCell>
              <TableCell>
                <StatusBadge kind="assignment" value={row.assignmentStatus} />
              </TableCell>
              <TableCell>
                <StatusBadge kind="session" value={row.sessionStatus ?? 'none'} />
              </TableCell>
              <TableCell className="tabular whitespace-nowrap">{f.date(row.assignedAt)}</TableCell>
              <TableCell className="tabular whitespace-nowrap">{f.date(row.completedAt)}</TableCell>
              <TableCell>{row.scored ? t.common.yes : t.common.no}</TableCell>
              <TableCell className="pe-4">
                {/* The last 12 hex digits: version 7 IDs begin with their creation time, so a prefix repeats. */}
                <span className="font-mono text-xs" dir="ltr" title={fill(t.monitoring.householdTitle, { id: row.householdId })}>
                  {row.householdId.slice(-12)}
                </span>
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}
