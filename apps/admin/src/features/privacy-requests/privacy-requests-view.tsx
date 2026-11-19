'use client';

import { useState } from 'react';

import { PageHeader } from '@/components/app-shell/page-header';
import { Pagination } from '@/components/data/pagination';
import { StatusBadge } from '@/components/data/status-badge';
import { EmptyState } from '@/components/states/empty-state';
import { ErrorState } from '@/components/states/error-state';
import { TableSkeleton } from '@/components/states/table-skeleton';
import { Button } from '@/components/ui/button';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { usePrivacyRequests, type PrivacyRequestFilters } from '@/lib/api/queries';
import { PRIVACY_REQUEST_STATUSES, type AdminPrivacyRequest, type PrivacyRequestStatus } from '@/lib/api/schemas';
import type { Query } from '@/lib/api/search';
import { fill } from '@/lib/format';
import { useI18n } from '@/lib/i18n-context';
import { oneOf, positiveInt, useSearchState } from '@/lib/url-state';
import { useLastPage } from '@/lib/use-last-page';

import { CloseRequestDialog } from './close-request-dialog';
import { shortMemberId } from './short-member-id';

const PAGE_SIZE = 20;

/** Status and page live in the URL; open and the first page are the defaults and are left out of it. */
function parse(params: URLSearchParams): PrivacyRequestFilters {
  return {
    status: oneOf(params, 'status', PRIVACY_REQUEST_STATUSES) ?? 'open',
    page: positiveInt(params, 'page') ?? 1,
    pageSize: PAGE_SIZE,
  };
}

function serialize(filters: PrivacyRequestFilters): Query {
  return { status: filters.status === 'open' ? null : filters.status, page: filters.page === 1 ? null : filters.page, pageSize: null };
}

/**
 * Families' requests to see, correct or erase a member's data, for program administrators. The platform lists them
 * (open ones oldest first) and records the outcome; the dashboard decides nothing about them.
 */
export function PrivacyRequestsView() {
  const { t } = useI18n();
  const messages = t.privacyRequests;
  const [filters, setFilters] = useSearchState(parse, serialize);
  const requests = usePrivacyRequests(filters);
  const [target, setTarget] = useState<AdminPrivacyRequest | null>(null);
  const [dialogOpen, setDialogOpen] = useState(false);
  const data = requests.data;
  // Closing the last request of the last page leaves that page empty: go to the page that is now the last.
  const pastEnd = useLastPage(data, !requests.isPlaceholderData, (page) => setFilters({ ...filters, page }));

  const open = filters.status === 'open';

  return (
    <>
      <PageHeader title={messages.title} description={messages.description} />
      <div className="flex flex-wrap items-center justify-between gap-3">
        <StatusFilter value={filters.status} onChange={(status) => setFilters({ ...filters, status, page: 1 })} />
        <p className="text-xs text-muted-foreground">{t.common.utcNote}</p>
      </div>
      {requests.isError ? (
        <ErrorState error={requests.error} onRetry={() => void requests.refetch()} />
      ) : !data || pastEnd ? (
        <TableSkeleton columns={8} />
      ) : data.totalCount === 0 ? (
        <EmptyState
          title={open ? messages.emptyOpenTitle : messages.emptyClosedTitle}
          description={open ? messages.emptyOpenBody : messages.emptyClosedBody}
        />
      ) : (
        <div className="flex flex-col gap-3" aria-busy={requests.isFetching}>
          <RequestTable
            rows={data.items}
            onClose={(row) => {
              setTarget(row);
              setDialogOpen(true);
            }}
          />
          <Pagination
            page={data.page}
            pageSize={data.pageSize}
            totalCount={data.totalCount}
            onPage={(page) => setFilters({ ...filters, page })}
          />
        </div>
      )}
      <CloseRequestDialog request={target} open={dialogOpen} onOpenChange={setDialogOpen} />
    </>
  );
}

function StatusFilter({ value, onChange }: { value: PrivacyRequestStatus; onChange: (status: PrivacyRequestStatus) => void }) {
  const { t } = useI18n();
  const labels: Record<PrivacyRequestStatus, string> = { open: t.privacyRequests.filterOpen, closed: t.privacyRequests.filterClosed };
  return (
    <div role="group" aria-label={t.privacyRequests.statusFilter} className="inline-flex gap-1 rounded-lg border border-border bg-card p-1">
      {PRIVACY_REQUEST_STATUSES.map((status) => (
        <Button
          key={status}
          type="button"
          size="lg"
          variant={value === status ? 'default' : 'ghost'}
          aria-pressed={value === status}
          onClick={() => onChange(status)}
        >
          {labels[status]}
        </Button>
      ))}
    </div>
  );
}

function RequestTable({ rows, onClose }: { rows: AdminPrivacyRequest[]; onClose: (row: AdminPrivacyRequest) => void }) {
  const { t, f } = useI18n();
  const messages = t.privacyRequests;
  const columns = messages.columns;
  const actionable = rows.some((row) => row.status === 'open');
  return (
    <div className="rounded-lg border border-border bg-card">
      <Table aria-label={messages.table}>
        <TableHeader>
          <TableRow>
            <TableHead className="ps-4">{columns.household}</TableHead>
            <TableHead>{columns.member}</TableHead>
            <TableHead>{columns.type}</TableHead>
            <TableHead>{columns.status}</TableHead>
            <TableHead>{columns.created}</TableHead>
            <TableHead>{columns.closed}</TableHead>
            <TableHead className={actionable ? undefined : 'pe-4'}>{columns.resolution}</TableHead>
            {actionable ? <TableHead className="pe-4 text-end">{columns.action}</TableHead> : null}
          </TableRow>
        </TableHeader>
        <TableBody>
          {rows.map((row) => (
            <TableRow key={row.id}>
              <TableCell className="ps-4">
                <span className="font-mono text-xs" dir="ltr">
                  {row.householdReference}
                </span>
              </TableCell>
              <TableCell>
                <span className="font-mono text-xs" dir="ltr" title={fill(messages.memberTitle, { id: row.memberId })}>
                  {shortMemberId(row.memberId)}
                </span>
              </TableCell>
              <TableCell className="whitespace-nowrap">{messages.types[row.type]}</TableCell>
              <TableCell>
                <StatusBadge kind="privacyRequest" value={row.status} />
              </TableCell>
              <TableCell className="tabular whitespace-nowrap">{f.date(row.createdAt)}</TableCell>
              <TableCell className="tabular whitespace-nowrap">{f.date(row.closedAt)}</TableCell>
              <TableCell className={actionable ? 'whitespace-nowrap' : 'pe-4 whitespace-nowrap'}>
                {row.resolution ? messages.resolutions[row.resolution] : t.common.none}
              </TableCell>
              {actionable ? (
                <TableCell className="pe-4 text-end">
                  {row.status === 'open' ? (
                    <Button
                      variant="outline"
                      size="lg"
                      aria-label={fill(messages.closeLabel, { type: messages.types[row.type], reference: row.householdReference })}
                      onClick={() => onClose(row)}
                    >
                      {messages.close}
                    </Button>
                  ) : null}
                </TableCell>
              ) : null}
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}
