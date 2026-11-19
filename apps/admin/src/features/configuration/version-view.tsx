'use client';

import Link from 'next/link';

import { PageHeader } from '@/components/app-shell/page-header';
import { StatusBadge } from '@/components/data/status-badge';
import { ErrorState } from '@/components/states/error-state';
import { TableSkeleton } from '@/components/states/table-skeleton';
import { Skeleton } from '@/components/ui/skeleton';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { useInstrument, useInstrumentVersion, useVersionRuleSets } from '@/lib/api/queries';
import type { InstrumentVersion } from '@/lib/api/schemas';
import { fill } from '@/lib/format';
import { useI18n } from '@/lib/i18n-context';

import { approval, effectiveWindow } from './instrument-view';
import { Detail, LocalText, ReadOnlyNotice } from './shared';

/** One version: its approval and window, its rule sets, and its full content, all read-only. */
export function VersionView({ versionId }: { versionId: string }) {
  const { t, f } = useI18n();
  const version = useInstrumentVersion(versionId);
  const instrument = useInstrument(version.data?.instrumentId ?? '');
  const crumbs = [{ label: t.configuration.title, href: '/configuration' }];

  if (version.isError) {
    return (
      <>
        {/* No crumbs: the title is already the section, and repeating it as a crumb read "… rules › … rules". */}
        <PageHeader title={t.configuration.title} />
        <ErrorState error={version.error} onRetry={() => void version.refetch()} />
      </>
    );
  }
  if (!version.data) return <Skeleton role="status" aria-label={t.common.loading} className="h-64" />;
  const data = version.data;
  const code = instrument.data?.code ?? '…';
  return (
    <>
      <PageHeader
        title={fill(t.configuration.versionTitle, { code, version: f.number(data.versionNumber) })}
        crumbs={[...crumbs, { label: code, href: `/configuration/instruments/${data.instrumentId}` }]}
      />
      <ReadOnlyNotice />
      <section className="flex flex-col gap-3 rounded-lg border border-border bg-card p-5">
        <h2 className="text-lg font-semibold">{t.configuration.details}</h2>
        <dl className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          <Detail label={t.configuration.status}>
            <StatusBadge kind="version" value={data.status} />
          </Detail>
          <Detail label={t.configuration.approval}>{approval(t, f, data)}</Detail>
          <Detail label={t.configuration.approvalSource}>
            <bdi>{data.approvalSource ?? t.configuration.none}</bdi>
          </Detail>
          <Detail label={t.configuration.effective}>{effectiveWindow(t, f, data)}</Detail>
          <Detail label={t.configuration.retired}>{data.retiredAt ? f.date(data.retiredAt) : t.configuration.none}</Detail>
          <Detail label={t.configuration.contentReference}>
            <span className="flex flex-col">
              <span className="self-start" dir="ltr">
                {data.contentReferenceEn ?? t.configuration.none}
              </span>
              <span>{data.contentReferenceAr ?? t.configuration.none}</span>
            </span>
          </Detail>
        </dl>
      </section>
      <RuleSets versionId={versionId} />
      <Content version={data} />
    </>
  );
}

function RuleSets({ versionId }: { versionId: string }) {
  const { t, f } = useI18n();
  const ruleSets = useVersionRuleSets(versionId);
  const columns = t.configuration.ruleSetColumns;
  return (
    <section className="flex flex-col gap-3">
      <h2 className="text-lg font-semibold">{t.configuration.ruleSetsTitle}</h2>
      {ruleSets.isError ? (
        <ErrorState error={ruleSets.error} onRetry={() => void ruleSets.refetch()} />
      ) : !ruleSets.data ? (
        <TableSkeleton columns={5} rows={2} />
      ) : ruleSets.data.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t.configuration.emptyRuleSets}</p>
      ) : (
        <div className="rounded-lg border border-border bg-card">
          <Table aria-label={t.configuration.ruleSetsTable}>
            <TableHeader>
              <TableRow>
                <TableHead className="ps-4">{columns.kind}</TableHead>
                <TableHead>{columns.code}</TableHead>
                <TableHead>{columns.version}</TableHead>
                <TableHead>{columns.status}</TableHead>
                <TableHead className="pe-4">{columns.approved}</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {ruleSets.data.map((ruleSet) => (
                <TableRow key={ruleSet.id}>
                  <TableCell className="ps-4">{t.configuration.ruleKinds[ruleSet.kind]}</TableCell>
                  <TableCell className="font-medium">
                    <Link href={`/configuration/rule-sets/${ruleSet.id}`} className="text-primary underline-offset-4 hover:underline">
                      {ruleSet.code}
                    </Link>
                  </TableCell>
                  <TableCell className="tabular">{f.number(ruleSet.versionNumber)}</TableCell>
                  <TableCell>
                    <StatusBadge kind="ruleSet" value={ruleSet.status} />
                  </TableCell>
                  <TableCell className="pe-4">{approval(t, f, ruleSet)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </section>
  );
}

function Content({ version }: { version: InstrumentVersion }) {
  const { t, f } = useI18n();
  return (
    <section className="flex flex-col gap-4">
      <h2 className="text-lg font-semibold">{t.configuration.contentTitle}</h2>
      {[...version.sections]
        .sort((a, b) => a.displayOrder - b.displayOrder)
        .map((section) => (
          <article key={section.id} className="flex flex-col gap-3 rounded-lg border border-border bg-card p-5">
            <header className="flex flex-col gap-0.5">
              <h3 className="text-base font-semibold">
                <LocalText text={section.title} />
              </h3>
              <p className="self-start font-mono text-xs text-muted-foreground" dir="ltr">
                {section.code}
              </p>
            </header>
            <ol className="flex flex-col divide-y divide-border">
              {[...section.items]
                .sort((a, b) => a.displayOrder - b.displayOrder)
                .map((item) => (
                  <li key={item.id} className="flex flex-col gap-2 py-3">
                    <div className="flex flex-wrap items-baseline justify-between gap-2">
                      <p className="font-medium">
                        <LocalText text={item.prompt} />
                      </p>
                      <p className="text-xs text-muted-foreground">{fill(t.configuration.item, { code: item.code })}</p>
                    </div>
                    <dl className="flex flex-wrap gap-x-6 gap-y-1 text-sm">
                      <div className="flex gap-1">
                        <dt className="text-muted-foreground">{t.configuration.direction}:</dt>
                        <dd>{t.configuration.directions[item.scoringDirection]}</dd>
                      </div>
                      <div className="flex gap-1">
                        <dt className="text-muted-foreground">{t.configuration.category}:</dt>
                        <dd>{item.scoringCategory ?? t.configuration.none}</dd>
                      </div>
                    </dl>
                    <p className="text-sm">{item.isRequired ? t.configuration.required : t.configuration.optional}</p>
                    <div>
                      <p className="text-sm text-muted-foreground">{t.configuration.options}</p>
                      <ul className="mt-1 flex flex-col gap-1">
                        {[...item.options]
                          .sort((a, b) => a.displayOrder - b.displayOrder)
                          .map((option) => (
                            <li key={option.id} className="flex flex-wrap items-center gap-x-3 text-sm">
                              <span className="tabular w-20 shrink-0 text-muted-foreground">{fill(t.configuration.optionValue, { value: f.number(option.value) })}</span>
                              <LocalText text={option.label} />
                              {option.isNonSubstantive ? (
                                <span className="rounded-full bg-status-neutral-soft px-2 py-0.5 text-xs text-status-neutral">{t.configuration.nonSubstantive}</span>
                              ) : null}
                            </li>
                          ))}
                      </ul>
                    </div>
                  </li>
                ))}
            </ol>
          </article>
        ))}
    </section>
  );
}
