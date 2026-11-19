'use client';

import Link from 'next/link';
import { useId } from 'react';

import { PageHeader } from '@/components/app-shell/page-header';
import { StatusBadge } from '@/components/data/status-badge';
import { ErrorState } from '@/components/states/error-state';
import { Skeleton } from '@/components/ui/skeleton';
import { useRuleSet } from '@/lib/api/queries';
import { fill } from '@/lib/format';
import { useI18n } from '@/lib/i18n-context';

import { approval } from './instrument-view';
import { Detail, ReadOnlyNotice } from './shared';

/** One rule set: its status, approval and stored definition, exactly as the platform holds it. Nothing is editable. */
export function RuleSetView({ ruleSetId }: { ruleSetId: string }) {
  const { t, f } = useI18n();
  const ruleSet = useRuleSet(ruleSetId);
  const definitionId = useId();
  const crumbs = [{ label: t.configuration.title, href: '/configuration' }];

  if (ruleSet.isError) {
    return (
      <>
        {/* No crumbs: the title is already the section, and repeating it as a crumb read "… rules › … rules". */}
        <PageHeader title={t.configuration.title} />
        <ErrorState error={ruleSet.error} onRetry={() => void ruleSet.refetch()} />
      </>
    );
  }
  if (!ruleSet.data) return <Skeleton role="status" aria-label={t.common.loading} className="h-64" />;
  const data = ruleSet.data;
  return (
    <>
      <PageHeader
        title={fill(t.configuration.ruleSetTitle, { code: data.code, version: f.number(data.versionNumber) })}
        crumbs={[...crumbs, { label: t.configuration.ruleKinds[data.kind] }]}
      />
      <ReadOnlyNotice />
      <section className="flex flex-col gap-3 rounded-lg border border-border bg-card p-5">
        <h2 className="text-lg font-semibold">{t.configuration.details}</h2>
        <dl className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          <Detail label={t.configuration.ruleSetColumns.kind}>{t.configuration.ruleKinds[data.kind]}</Detail>
          <Detail label={t.configuration.status}>
            <StatusBadge kind="ruleSet" value={data.status} />
          </Detail>
          <Detail label={t.configuration.approval}>{approval(t, f, data)}</Detail>
          <Detail label={t.configuration.approvalSource}>
            <bdi>{data.approvalSource ?? t.configuration.none}</bdi>
          </Detail>
          <Detail label={t.configuration.retired}>{data.retiredAt ? f.date(data.retiredAt) : t.configuration.none}</Detail>
          <Detail label={t.configuration.forVersion}>
            <Link href={`/configuration/versions/${data.instrumentVersionId}`} className="text-primary underline-offset-4 hover:underline">
              {t.configuration.openVersion}
            </Link>
          </Detail>
        </dl>
      </section>
      <section className="flex flex-col gap-2">
        <h2 id={definitionId} className="text-lg font-semibold">
          {t.configuration.definition}
        </h2>
        <p className="text-sm text-muted-foreground">{t.configuration.definitionNote}</p>
        <pre
          aria-labelledby={definitionId}
          dir="ltr"
          tabIndex={0}
          className="max-h-[32rem] overflow-auto rounded-lg border border-border bg-secondary p-4 text-start font-mono text-xs leading-relaxed"
        >
          {JSON.stringify(data.definition, null, 2)}
        </pre>
      </section>
    </>
  );
}
