'use client';

import { ChartColumnBig, Users } from 'lucide-react';
import { useId } from 'react';

import { PageHeader } from '@/components/app-shell/page-header';
import { EmptyState } from '@/components/states/empty-state';
import { ErrorState } from '@/components/states/error-state';
import { Skeleton } from '@/components/ui/skeleton';
import { WaveSelect } from '@/features/filters/wave-select';
import { useScoreDistributions, useScoreDistributionsByWave, useWaves } from '@/lib/api/queries';
import type { ScoreDistributions } from '@/lib/api/schemas';
import type { Query } from '@/lib/api/search';
import { fill } from '@/lib/format';
import { useI18n } from '@/lib/i18n-context';
import { guidParam, useSearchState } from '@/lib/url-state';

import { DistributionCard } from './distribution-card';
import { distributionKey } from './distribution-key';
import { MAX_COMPARED_WAVES, WaveComparison, type WaveDistributions } from './wave-comparison';

const parse = (params: URLSearchParams) => ({ waveId: guidParam(params, 'waveId') });
const serialize = (filters: { waveId: string | null }): Query => filters;

/** Score distributions for researchers only (the API refuses everyone else). Nothing is computed or estimated here. */
export function AnalyticsView() {
  const { t, f } = useI18n();
  const [filters, setFilters] = useSearchState(parse, serialize);
  const distributions = useScoreDistributions(filters.waveId);
  const waveFilterId = useId();
  const data = distributions.data;

  return (
    <>
      <PageHeader title={t.analytics.title} description={t.analytics.description} />
      <section aria-label={t.common.filters} className="grid gap-4 rounded-lg border border-border bg-card p-4 sm:grid-cols-2 lg:grid-cols-3">
        <WaveSelect id={waveFilterId} value={filters.waveId} onChange={(waveId) => setFilters({ waveId })} hint={t.filters.noCohorts} />
      </section>

      {distributions.isError ? (
        <ErrorState error={distributions.error} onRetry={() => void distributions.refetch()} />
      ) : !data ? (
        <div role="status" className="grid gap-4 md:grid-cols-2">
          <span className="sr-only">{t.common.loading}</span>
          <Skeleton className="h-64" />
          <Skeleton className="h-64" />
        </div>
      ) : data.distributions.length === 0 ? (
        <EmptyState icon={<ChartColumnBig />} title={t.analytics.emptyTitle} description={t.analytics.emptyBody} />
      ) : (
        <>
          <section className="flex flex-col gap-3">
            <h2 className="text-lg font-semibold">{t.analytics.distributionsTitle}</h2>
            <p className="text-sm text-muted-foreground">{fill(t.display.suppressedNote, { n: data.minimumGroupSize })}</p>
            <div className={data.distributions.length > 1 ? 'grid gap-4 md:grid-cols-2' : 'grid gap-4'}>
              {data.distributions.map((distribution) => (
                <DistributionCard key={distributionKey(distribution)} distribution={distribution} minimumGroupSize={data.minimumGroupSize} />
              ))}
            </div>
            <p className="text-xs text-muted-foreground">{fill(t.analytics.countedAt, { time: f.dateTime(data.generatedAt) })}</p>
          </section>
          <TrendsSection data={data} waveFiltered={filters.waveId !== null} />
        </>
      )}

      <section className="flex flex-col gap-3">
        <h2 className="text-lg font-semibold">{t.analytics.demographicsTitle}</h2>
        <EmptyState icon={<Users />} title={t.analytics.demographicsEmptyTitle} description={t.analytics.demographicsEmptyBody} />
      </section>
    </>
  );
}

function TrendsSection({ data, waveFiltered }: { data: ScoreDistributions; waveFiltered: boolean }) {
  const { t } = useI18n();
  const headingId = useId();
  const waves = useWaves();
  // Planned waves cannot have scores yet; compare the most recent waves that have opened, oldest first.
  const opened = (waves.data?.items ?? []).filter((wave) => wave.status !== 'planned');
  const compared = waveFiltered ? [] : opened.slice(0, MAX_COMPARED_WAVES).reverse();
  const results = useScoreDistributionsByWave(compared.map((wave) => wave.id));
  const loaded = results.every((result) => result.data);
  const perWave: WaveDistributions[] = loaded ? compared.map((wave, index) => ({ wave, distributions: results[index].data?.distributions ?? [] })) : [];
  const keyed = data.distributions.filter(
    (sample) => perWave.filter((entry) => entry.distributions.some((candidate) => distributionKey(candidate) === distributionKey(sample))).length >= 2,
  );
  const failed = results.find((result) => result.isError);

  return (
    <section aria-labelledby={headingId} className="flex flex-col gap-3">
      <h2 id={headingId} className="text-lg font-semibold">
        {t.analytics.trendsTitle}
      </h2>
      <p className="max-w-prose text-sm text-muted-foreground">{t.analytics.trendsNote}</p>
      {waveFiltered ? (
        <p className="text-sm">{t.analytics.trendsWaveFiltered}</p>
      ) : failed ? (
        <ErrorState error={failed.error} onRetry={() => void failed.refetch()} />
      ) : !waves.data || !loaded ? (
        <Skeleton className="h-64" />
      ) : compared.length < 2 ? (
        <p className="text-sm">{t.analytics.trendsNoWaves}</p>
      ) : keyed.length === 0 ? (
        <p className="text-sm">{t.analytics.trendsNoOverlap}</p>
      ) : (
        <div className="grid gap-4">
          {keyed.map((sample) => (
            <WaveComparison key={distributionKey(sample)} sample={sample} perWave={perWave} minimumGroupSize={data.minimumGroupSize} />
          ))}
        </div>
      )}
    </section>
  );
}
