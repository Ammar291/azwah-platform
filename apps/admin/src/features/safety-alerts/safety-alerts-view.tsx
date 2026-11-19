'use client';

import { useEffect, useId, useState } from 'react';
import { toast } from 'sonner';

import { PageHeader } from '@/components/app-shell/page-header';
import { Field, NativeSelect } from '@/components/data/field';
import { Pagination } from '@/components/data/pagination';
import { StatusBadge } from '@/components/data/status-badge';
import { EmptyState } from '@/components/states/empty-state';
import { ErrorState } from '@/components/states/error-state';
import { TableSkeleton } from '@/components/states/table-skeleton';
import { Button } from '@/components/ui/button';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { useAcknowledgeSafetyAlert, useSafetyAlerts } from '@/lib/api/queries';
import { SAFETY_CONCERNS, type AdminSafetyAlert, type SafetyConcern } from '@/lib/api/schemas';
import { fill } from '@/lib/format';
import { useI18n } from '@/lib/i18n-context';
import { useSearchState } from '@/lib/url-state';
import { cn } from '@/lib/utils';

import { ResolveAlertDialog } from './resolve-alert-dialog';
import {
  ALERT_STATUS_FILTERS,
  parseAlertFilters,
  serializeAlertFilters,
  toRequestFilters,
  type AlertStatusFilter,
} from './safety-alert-filters';
import { alertRefusal, detectorName, relationshipName, signalName, sourceName } from './safety-alert-labels';
import { SafetyGuidance } from './safety-guidance';

/**
 * Safety alerts for system administrators: signs that a participant may be in danger, and families in the lowest
 * score band. The platform raises, orders and records them; this page shows codes only (no conversation) and records
 * who acknowledged or resolved each one. The list is read when the page opens, when the window regains focus, after
 * each action, and whenever the shell's watcher sees the summary change (which also raises the toast).
 * `canManage` (the `safety_alerts.manage` permission) decides whether Acknowledge and Resolve are offered; the API
 * checks it again.
 */
export function SafetyAlertsView({ canManage }: { canManage: boolean }) {
  const { t } = useI18n();
  const messages = t.safetyAlerts;
  const [filters, setFilters] = useSearchState(parseAlertFilters, serializeAlertFilters);
  const alerts = useSafetyAlerts(toRequestFilters(filters), filters.page);
  const [target, setTarget] = useState<AdminSafetyAlert | null>(null);
  const [dialogOpen, setDialogOpen] = useState(false);
  const data = alerts.data;
  const settled = data !== undefined && !alerts.isPlaceholderData;
  const categoryId = useId();

  // Handling the last alert of the last page leaves that page empty: go to the page that is now the last.
  useEffect(() => {
    if (settled && data.items.length === 0 && data.page > 1 && data.totalCount > 0) {
      setFilters({ ...filters, page: Math.ceil(data.totalCount / data.pageSize) });
    }
  }, [settled, data, filters, setFilters]);

  const unresolved = filters.status === 'unresolved';
  return (
    <>
      <PageHeader title={messages.title} description={messages.description} />
      <SafetyGuidance />
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div className="flex flex-wrap items-end gap-3">
          <StatusFilter value={filters.status} onChange={(status) => setFilters({ ...filters, status, page: 1 })} />
          <Field id={categoryId} label={messages.categoryFilter} className="w-56">
            <NativeSelect
              id={categoryId}
              value={filters.category ?? ''}
              onChange={(event) => setFilters({ ...filters, category: (event.target.value || null) as SafetyConcern | null, page: 1 })}
            >
              <option value="">{messages.allConcerns}</option>
              {SAFETY_CONCERNS.map((concern) => (
                <option key={concern} value={concern}>
                  {messages.concerns[concern]}
                </option>
              ))}
            </NativeSelect>
          </Field>
        </div>
        <p className="text-xs text-muted-foreground">{t.common.utcNote}</p>
      </div>
      {alerts.isError && !data ? (
        <ErrorState error={alerts.error} onRetry={() => void alerts.refetch()} />
      ) : !data ? (
        <TableSkeleton columns={5} />
      ) : data.totalCount === 0 ? (
        <EmptyState
          title={unresolved ? messages.emptyUnresolvedTitle : messages.emptyOtherTitle}
          description={unresolved ? messages.emptyUnresolvedBody : messages.emptyOtherBody}
        />
      ) : (
        <div className="flex flex-col gap-3" aria-busy={alerts.isFetching}>
          {alerts.isError ? (
            <p role="status" className="text-sm text-status-danger">
              {messages.refreshFailed}
            </p>
          ) : null}
          <AlertTable
            rows={data.items}
            canManage={canManage}
            onResolve={(row) => {
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
      {canManage ? <ResolveAlertDialog alert={target} open={dialogOpen} onOpenChange={setDialogOpen} /> : null}
    </>
  );
}

function StatusFilter({ value, onChange }: { value: AlertStatusFilter; onChange: (status: AlertStatusFilter) => void }) {
  const { t } = useI18n();
  return (
    <div role="group" aria-label={t.safetyAlerts.statusFilter} className="inline-flex flex-wrap gap-1 rounded-lg border border-border bg-card p-1">
      {ALERT_STATUS_FILTERS.map((status) => (
        <Button
          key={status}
          type="button"
          size="lg"
          variant={value === status ? 'default' : 'ghost'}
          aria-pressed={value === status}
          onClick={() => onChange(status)}
        >
          {t.safetyAlerts.filters[status]}
        </Button>
      ))}
    </div>
  );
}

function AlertTable({
  rows,
  canManage,
  onResolve,
}: {
  rows: AdminSafetyAlert[];
  canManage: boolean;
  onResolve: (row: AdminSafetyAlert) => void;
}) {
  const { t, f } = useI18n();
  const messages = t.safetyAlerts;
  const columns = messages.columns;
  const acknowledge = useAcknowledgeSafetyAlert();
  const actionable = canManage && rows.some((row) => row.status !== 'resolved');

  async function acknowledgeRow(row: AdminSafetyAlert) {
    try {
      await acknowledge.mutateAsync({ alertId: row.id });
      toast.success(messages.acknowledgedToast);
    } catch (error) {
      toast.error(alertRefusal(t, error, messages.notAcknowledged));
    }
  }

  // Few, tall columns: the actions stay in view on a 1280 px window without scrolling sideways.
  return (
    <div className="rounded-lg border border-border bg-card">
      <Table aria-label={messages.table}>
        <TableHeader>
          <TableRow>
            <TableHead className="ps-4">{columns.alert}</TableHead>
            <TableHead>{columns.concern}</TableHead>
            <TableHead>{columns.household}</TableHead>
            <TableHead className={actionable ? undefined : 'pe-4'}>{columns.seen}</TableHead>
            {actionable ? <TableHead className="pe-4 text-end">{columns.action}</TableHead> : null}
          </TableRow>
        </TableHeader>
        <TableBody>
          {rows.map((row) => {
            const concern = messages.concerns[row.category];
            const reference = row.householdReference ?? t.common.none;
            const details = [
              row.signalCode ? fill(messages.signalLabel, { signal: signalName(t, row.signalCode) }) : null,
              row.language ? fill(messages.languageLabel, { language: messages.languages[row.language] }) : null,
            ].filter((part): part is string => part !== null);
            return (
              <TableRow key={row.id}>
                <TableCell className="ps-4 align-top whitespace-normal">
                  <div className="flex flex-col items-start gap-1">
                    <StatusBadge kind="safetySeverity" value={row.severity} />
                    <StatusBadge kind="safetyAlert" value={row.status} />
                    {row.resolution ? (
                      <p className="text-xs text-muted-foreground">
                        {fill(messages.outcomeLabel, { outcome: messages.resolutions[row.resolution] })}
                      </p>
                    ) : null}
                  </div>
                </TableCell>
                <TableCell className="min-w-56 align-top whitespace-normal">
                  <p className="font-medium">{concern}</p>
                  {details.length > 0 ? <p className="text-xs text-muted-foreground">{details.join(' · ')}</p> : null}
                  <p className="text-xs text-muted-foreground">
                    <span>{sourceName(t, row.source)}</span>
                    {' · '}
                    <span>{fill(messages.detectedBy, { detector: detectorName(t, row.detector) })}</span>
                  </p>
                </TableCell>
                <TableCell className="align-top whitespace-normal">
                  <p className="font-mono text-xs" dir="ltr">
                    {reference}
                  </p>
                  <p className="text-xs text-muted-foreground">{relationshipName(t, row.memberRelationship)}</p>
                </TableCell>
                <TableCell className={cn('tabular align-top text-xs whitespace-nowrap', actionable ? undefined : 'pe-4')}>
                  <p>{fill(messages.firstSeenLabel, { time: f.dateTime(row.firstDetectedAt) })}</p>
                  <p>{fill(messages.lastSeenLabel, { time: f.dateTime(row.lastDetectedAt) })}</p>
                  <p className="text-muted-foreground">{fill(messages.timesLabel, { count: f.number(row.occurrenceCount) })}</p>
                </TableCell>
                {actionable ? (
                  <TableCell className="pe-4 align-top text-end">
                    <div className="flex flex-col items-end gap-2">
                      {row.status === 'open' ? (
                        <Button
                          variant="outline"
                          size="lg"
                          disabled={acknowledge.isPending}
                          aria-label={fill(messages.acknowledgeLabel, { concern, reference })}
                          onClick={() => void acknowledgeRow(row)}
                        >
                          {messages.acknowledge}
                        </Button>
                      ) : null}
                      {row.status !== 'resolved' ? (
                        <Button
                          variant="outline"
                          size="lg"
                          aria-label={fill(messages.resolveLabel, { concern, reference })}
                          onClick={() => onResolve(row)}
                        >
                          {messages.resolve}
                        </Button>
                      ) : null}
                    </div>
                  </TableCell>
                ) : null}
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
    </div>
  );
}
