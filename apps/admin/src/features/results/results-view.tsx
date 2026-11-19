'use client';

import { Info } from 'lucide-react';
import { useId, useState, type FormEvent } from 'react';

import { PageHeader } from '@/components/app-shell/page-header';
import { BandLabel } from '@/components/data/band-label';
import { Field, NativeInput } from '@/components/data/field';
import { Pagination } from '@/components/data/pagination';
import { StatusBadge } from '@/components/data/status-badge';
import { EmptyState } from '@/components/states/empty-state';
import { ErrorState } from '@/components/states/error-state';
import { TableSkeleton } from '@/components/states/table-skeleton';
import { Button } from '@/components/ui/button';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { WaveSelect } from '@/features/filters/wave-select';
import { useHouseholdResults, type ResultFilters } from '@/lib/api/queries';
import type { HouseholdResult } from '@/lib/api/schemas';
import { fill } from '@/lib/format';
import { useI18n } from '@/lib/i18n-context';
import { useSearchState } from '@/lib/url-state';
import { useLastPage } from '@/lib/use-last-page';

import {
  applyResultDraft,
  draftOf,
  parseResultFilters,
  serializeResultFilters,
  type ResultDraft,
  type ResultDraftError,
} from './result-filters';

/**
 * Recorded household score results for system administrators. Every total and band is the platform's, shown as it
 * arrives: nothing here calculates, rounds or infers a score or band.
 */
export function ResultsView() {
  const { t } = useI18n();
  const [urlFilters, setUrlFilters] = useSearchState(parseResultFilters, serializeResultFilters);
  const [reference, setReference] = useState<string | null>(null);
  const filters: ResultFilters = { ...urlFilters, reference };
  const setFilters = (next: ResultFilters) => {
    setReference(next.reference);
    setUrlFilters(next);
  };
  const results = useHouseholdResults(filters);
  const pastEnd = useLastPage(results.data, !results.isPlaceholderData, (page) => setFilters({ ...filters, page }));

  return (
    <>
      <PageHeader title={t.results.title} description={t.results.description} />
      <p role="note" className="flex items-start gap-2 rounded-lg border border-border bg-secondary px-4 py-3 text-sm text-secondary-foreground">
        <Info aria-hidden className="mt-0.5 size-4 shrink-0" />
        {t.results.recordedNote}
      </p>
      <ResultFilterForm key={JSON.stringify({ ...filters, page: 0 })} filters={filters} onApply={setFilters} />
      {results.isError ? (
        <ErrorState error={results.error} onRetry={() => void results.refetch()} />
      ) : !results.data || pastEnd ? (
        <TableSkeleton columns={7} />
      ) : results.data.totalCount === 0 ? (
        <EmptyState title={t.results.emptyTitle} description={t.results.emptyBody} />
      ) : (
        <div className="flex flex-col gap-3" aria-busy={results.isFetching}>
          <ResultTable rows={results.data.items} />
          <Pagination
            page={results.data.page}
            pageSize={results.data.pageSize}
            totalCount={results.data.totalCount}
            onPage={(page) => setFilters({ ...filters, page })}
          />
        </div>
      )}
    </>
  );
}

function ResultFilterForm({ filters, onApply }: { filters: ResultFilters; onApply: (filters: ResultFilters) => void }) {
  const { t } = useI18n();
  const [draft, setDraft] = useState<ResultDraft>(() => draftOf(filters));
  const [errors, setErrors] = useState<Partial<Record<keyof ResultDraft, ResultDraftError>>>({});
  const ids = { wave: useId(), code: useId(), reference: useId() };
  const change = (field: keyof ResultDraft) => (value: string) => setDraft((current) => ({ ...current, [field]: value }));
  const message = (error: ResultDraftError | undefined) => (error === undefined ? null : t.results[error]);

  function submit(event: FormEvent) {
    event.preventDefault();
    const result = applyResultDraft(draft);
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
        <Field id={ids.reference} label={t.results.reference} hint={t.results.referenceHint} error={message(errors.reference)}>
          <NativeInput
            id={ids.reference}
            dir="ltr"
            autoComplete="off"
            spellCheck={false}
            value={draft.reference}
            aria-invalid={errors.reference ? true : undefined}
            onChange={(event) => change('reference')(event.target.value)}
          />
        </Field>
        <WaveSelect id={ids.wave} value={draft.waveId || null} onChange={(waveId) => change('waveId')(waveId ?? '')} />
        <Field id={ids.code} label={t.results.instrumentCode} hint={t.results.instrumentCodeHint} error={message(errors.instrumentCode)}>
          <NativeInput
            id={ids.code}
            dir="ltr"
            autoComplete="off"
            spellCheck={false}
            value={draft.instrumentCode}
            aria-invalid={errors.instrumentCode ? true : undefined}
            onChange={(event) => change('instrumentCode')(event.target.value)}
          />
        </Field>
      </div>
      <div className="flex flex-wrap gap-2">
        <Button type="submit" size="lg">
          {t.results.apply}
        </Button>
        <Button type="button" size="lg" variant="ghost" onClick={() => onApply(parseResultFilters(new URLSearchParams()))}>
          {t.common.clear}
        </Button>
      </div>
    </form>
  );
}

function ResultTable({ rows }: { rows: HouseholdResult[] }) {
  const { t, f } = useI18n();
  const columns = t.results.columns;
  return (
    <div className="rounded-lg border border-border bg-card">
      <Table aria-label={t.results.table}>
        <TableHeader>
          <TableRow>
            <TableHead className="ps-4">{columns.household}</TableHead>
            <TableHead>{columns.wave}</TableHead>
            <TableHead>{columns.questionnaire}</TableHead>
            <TableHead>{columns.state}</TableHead>
            <TableHead className="text-end">{columns.total}</TableHead>
            <TableHead>{columns.band}</TableHead>
            <TableHead className="pe-4">{columns.computed}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {rows.map((row) => (
            <TableRow key={row.sessionId}>
              {/* The cell keeps the page's direction, so it lines up with its header in Arabic; only the reference is LTR. */}
              <TableCell className="ps-4 font-mono text-xs">
                <span dir="ltr">{row.householdReference}</span>
              </TableCell>
              <TableCell>{row.waveCode}</TableCell>
              <TableCell>
                <span className="flex flex-col">
                  <span className="font-medium">{row.instrumentCode}</span>
                  <span className="text-xs text-muted-foreground">
                    {row.sectionCode === null
                      ? fill(t.results.version, { version: f.number(row.versionNumber) })
                      : fill(t.results.versionModule, { version: f.number(row.versionNumber), module: row.sectionCode })}
                  </span>
                </span>
              </TableCell>
              <TableCell>
                <StatusBadge kind="scoreResult" value={row.state} />
              </TableCell>
              <TableCell className="tabular text-end">{f.score(row.totalScore)}</TableCell>
              <TableCell>
                <BandLabel code={row.tierCode} />
              </TableCell>
              <TableCell className="tabular pe-4 whitespace-nowrap">{f.date(row.computedAt)}</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}
