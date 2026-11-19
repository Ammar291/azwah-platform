'use client';

import { useId, useState, type FormEvent } from 'react';
import { z } from 'zod';

import { PageHeader } from '@/components/app-shell/page-header';
import { Field, NativeInput, NativeSelect } from '@/components/data/field';
import { Pagination } from '@/components/data/pagination';
import { StatusBadge } from '@/components/data/status-badge';
import { EmptyState } from '@/components/states/empty-state';
import { ErrorState } from '@/components/states/error-state';
import { TableSkeleton } from '@/components/states/table-skeleton';
import { Button } from '@/components/ui/button';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { useParticipation, type ParticipationFilters } from '@/lib/api/queries';
import type { Query } from '@/lib/api/search';
import { fill } from '@/lib/format';
import { useI18n } from '@/lib/i18n-context';
import { oneOf, positiveInt, useSearchState } from '@/lib/url-state';
import { useLastPage } from '@/lib/use-last-page';

const PAGE_SIZE = 20;
/** The platform's household reference: `HH-` and two groups of four Crockford base-32 characters. */
const REFERENCE = /^HH-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}$/;

/**
 * Status, roster and page live in the URL. The household reference does not: it identifies a family, and a URL ends
 * up in browser history and in the access logs of any proxy in front of the dashboard.
 */
function parse(params: URLSearchParams): ParticipationFilters {
  const roster = oneOf(params, 'rosterConfirmed', ['true', 'false'] as const);
  return {
    status: oneOf(params, 'status', ['active', 'withdrawn'] as const),
    rosterConfirmed: roster === null ? null : roster === 'true',
    reference: null,
    page: positiveInt(params, 'page') ?? 1,
    pageSize: PAGE_SIZE,
  };
}

function serialize(filters: ParticipationFilters): Query {
  return { ...filters, reference: null, page: filters.page === 1 ? null : filters.page, pageSize: null };
}

const formSchema = z.object({
  reference: z
    .string()
    .transform((value) => value.trim().toUpperCase())
    .refine((value) => value === '' || REFERENCE.test(value)),
  status: z.enum(['', 'active', 'withdrawn']),
  rosterConfirmed: z.enum(['', 'true', 'false']),
});

/** Households taking part, for program administrators. A household is found by its full reference only. */
export function ParticipationView() {
  const { t, f } = useI18n();
  const [urlFilters, setUrlFilters] = useSearchState(parse, serialize);
  const [reference, setReference] = useState<string | null>(null);
  const filters: ParticipationFilters = { ...urlFilters, reference };
  const setFilters = (next: ParticipationFilters) => {
    setReference(next.reference);
    setUrlFilters(next);
  };
  const participation = useParticipation(filters);
  const pastEnd = useLastPage(participation.data, !participation.isPlaceholderData, (page) => setFilters({ ...filters, page }));
  const columns = t.monitoring.participationColumns;

  return (
    <>
      <PageHeader title={t.monitoring.participationTitle} description={t.monitoring.participationDescription} />
      <ParticipationForm key={JSON.stringify({ ...filters, page: 0 })} filters={filters} onApply={setFilters} />
      {participation.isError ? (
        <ErrorState error={participation.error} onRetry={() => void participation.refetch()} />
      ) : !participation.data || pastEnd ? (
        <TableSkeleton columns={6} />
      ) : participation.data.totalCount === 0 ? (
        <EmptyState title={t.monitoring.participationEmptyTitle} description={t.monitoring.participationEmptyBody} />
      ) : (
        <div className="flex flex-col gap-3" aria-busy={participation.isFetching}>
          <div className="rounded-lg border border-border bg-card">
            <Table aria-label={t.monitoring.participationTable}>
              <TableHeader>
                <TableRow>
                  <TableHead className="ps-4">{columns.reference}</TableHead>
                  <TableHead>{columns.status}</TableHead>
                  <TableHead>{columns.enrolled}</TableHead>
                  <TableHead>{columns.roster}</TableHead>
                  <TableHead className="text-end">{columns.members}</TableHead>
                  <TableHead className="pe-4">{columns.progress}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {participation.data.items.map((row) => (
                  <TableRow key={row.householdId}>
                    {/* The cell keeps the page's direction, so it lines up with its header in Arabic; only the reference is LTR. */}
                    <TableCell className="ps-4 font-mono text-xs">
                      <span dir="ltr">{row.reference}</span>
                    </TableCell>
                    <TableCell>
                      <StatusBadge kind="household" value={row.status} />
                    </TableCell>
                    <TableCell className="tabular whitespace-nowrap">{f.date(row.createdAt)}</TableCell>
                    <TableCell className="whitespace-nowrap">{row.rosterConfirmedAt ? f.date(row.rosterConfirmedAt) : t.common.no}</TableCell>
                    <TableCell className="tabular text-end">{f.number(row.activeMemberCount)}</TableCell>
                    <TableCell className="pe-4 text-sm">
                      {fill(t.monitoring.progress, {
                        completed: f.number(row.assignments.completed),
                        inProgress: f.number(row.assignments.inProgress),
                        assigned: f.number(row.assignments.assigned),
                      })}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
          <Pagination
            page={participation.data.page}
            pageSize={participation.data.pageSize}
            totalCount={participation.data.totalCount}
            onPage={(page) => setFilters({ ...filters, page })}
          />
        </div>
      )}
    </>
  );
}

function ParticipationForm({ filters, onApply }: { filters: ParticipationFilters; onApply: (filters: ParticipationFilters) => void }) {
  const { t } = useI18n();
  const [reference, setReference] = useState(filters.reference ?? '');
  const [status, setStatus] = useState<string>(filters.status ?? '');
  const [roster, setRoster] = useState<string>(filters.rosterConfirmed === null ? '' : String(filters.rosterConfirmed));
  const [invalid, setInvalid] = useState(false);
  const ids = { reference: useId(), status: useId(), roster: useId() };

  function submit(event: FormEvent) {
    event.preventDefault();
    const parsed = formSchema.safeParse({ reference, status, rosterConfirmed: roster });
    if (!parsed.success) {
      setInvalid(true);
      return;
    }
    setInvalid(false);
    onApply({
      reference: parsed.data.reference || null,
      status: parsed.data.status || null,
      rosterConfirmed: parsed.data.rosterConfirmed === '' ? null : parsed.data.rosterConfirmed === 'true',
      page: 1,
      pageSize: PAGE_SIZE,
    });
  }

  return (
    <form aria-label={t.common.filters} onSubmit={submit} noValidate className="flex flex-col gap-4 rounded-lg border border-border bg-card p-4">
      <div className="grid gap-4 sm:grid-cols-3">
        <Field id={ids.reference} label={t.monitoring.reference} hint={t.monitoring.referenceHint} error={invalid ? t.monitoring.referenceInvalid : null}>
          <NativeInput
            id={ids.reference}
            dir="ltr"
            autoComplete="off"
            spellCheck={false}
            value={reference}
            aria-invalid={invalid || undefined}
            onChange={(event) => setReference(event.target.value)}
          />
        </Field>
        <Field id={ids.status} label={t.monitoring.householdStatus}>
          <NativeSelect id={ids.status} value={status} onChange={(event) => setStatus(event.target.value)}>
            <option value="">{t.monitoring.allHouseholds}</option>
            <option value="active">{t.status.household.active}</option>
            <option value="withdrawn">{t.status.household.withdrawn}</option>
          </NativeSelect>
        </Field>
        <Field id={ids.roster} label={t.monitoring.rosterConfirmed}>
          <NativeSelect id={ids.roster} value={roster} onChange={(event) => setRoster(event.target.value)}>
            <option value="">{t.monitoring.anyRoster}</option>
            <option value="true">{t.monitoring.rosterYes}</option>
            <option value="false">{t.monitoring.rosterNo}</option>
          </NativeSelect>
        </Field>
      </div>
      <div className="flex flex-wrap gap-2">
        <Button type="submit" size="lg">
          {t.monitoring.search}
        </Button>
        <Button type="button" size="lg" variant="ghost" onClick={() => onApply(parse(new URLSearchParams()))}>
          {t.common.clear}
        </Button>
      </div>
    </form>
  );
}
