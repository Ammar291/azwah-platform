'use client';

import { EyeOff } from 'lucide-react';
import { useId } from 'react';
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';

import { ChartFigure } from '@/components/data/chart-figure';
import type { ScoreDistribution, Wave } from '@/lib/api/schemas';
import { bandLabel, orderBands } from '@/lib/bands';
import { displayCount } from '@/lib/display';
import { fill } from '@/lib/format';
import { useI18n } from '@/lib/i18n-context';

import { distributionKey } from './distribution-key';

/** At most three compared waves, so the validated three-colour set (with legend and table) always suffices. */
export const MAX_COMPARED_WAVES = 3;
const SERIES_COLOURS = ['var(--chart-2)', 'var(--chart-3)', 'var(--chart-4)'] as const;
const AXIS_TICK = { fill: 'var(--chart-axis)', fontSize: 12 };

export interface WaveDistributions {
  wave: Wave;
  distributions: ScoreDistribution[];
}

/**
 * One distribution key across waves, oldest wave first: grouped columns for band counts that may be shown, low band
 * first, and a table with every cell, hidden ones included. When every compared wave covers too few people to list any
 * band, there is nothing to draw, and the card says so instead of showing an empty chart.
 */
export function WaveComparison({
  sample,
  perWave,
  minimumGroupSize,
}: {
  sample: ScoreDistribution;
  perWave: WaveDistributions[];
  minimumGroupSize: number;
}) {
  const { t, f, dir } = useI18n();
  const headingId = useId();
  const key = distributionKey(sample);
  const matches = perWave.map(({ wave, distributions }) => ({
    wave,
    distribution: distributions.find((candidate) => distributionKey(candidate) === key) ?? null,
  }));
  const tiers = orderBands([...new Set(matches.flatMap((match) => match.distribution?.tiers.map((tier) => tier.tier) ?? []))], (code) => code);
  const cell = (waveIndex: number, tierCode: string) => {
    const distribution = matches[waveIndex].distribution;
    if (!distribution) return { kind: 'missing' as const };
    if (displayCount(distribution.total, distribution.suppressed, minimumGroupSize).kind === 'suppressed') return { kind: 'hidden' as const };
    const tier = distribution.tiers.find((candidate) => candidate.tier === tierCode);
    if (!tier) return { kind: 'value' as const, value: 0 };
    const count = displayCount(tier.count, tier.suppressed, minimumGroupSize);
    return count.kind === 'value' ? { kind: 'value' as const, value: count.value } : { kind: 'hidden' as const };
  };
  const chartData = tiers.map((tier) => {
    const row: Record<string, string | number | null> = { tier: bandLabel(t, tier) };
    matches.forEach((match, index) => {
      const value = cell(index, tier);
      row[match.wave.code] = value.kind === 'value' ? value.value : null;
    });
    return row;
  });
  const rtl = dir === 'rtl';
  const title = fill(t.analytics.trendsChartTitle, { instrument: sample.instrumentCode, version: f.number(sample.versionNumber) });
  const description = `${sample.sectionCode ? fill(t.analytics.module, { module: sample.sectionCode }) : t.analytics.wholeVersion}. ${fill(t.analytics.ruleSet, { code: sample.ruleSetCode, version: f.number(sample.ruleSetVersion) })}.`;

  if (tiers.length === 0) {
    return (
      <section aria-labelledby={headingId} className="flex flex-col gap-3 rounded-lg border border-border bg-card p-4 sm:p-5">
        <header className="flex flex-col gap-1">
          <h3 id={headingId} className="text-base font-semibold">
            {title}
          </h3>
          <p className="max-w-prose text-sm text-muted-foreground">{description}</p>
        </header>
        <p className="flex items-start gap-2 text-sm text-muted-foreground">
          <EyeOff aria-hidden className="mt-0.5 size-4 shrink-0" />
          {fill(t.analytics.trendsSuppressed, { n: minimumGroupSize })}
        </p>
      </section>
    );
  }

  return (
    <ChartFigure
      title={title}
      description={description}
      summary={fill(t.analytics.trendsChartSummary, { instrument: sample.instrumentCode, version: f.number(sample.versionNumber) })}
      legend={
        <ul className="flex flex-wrap gap-x-4 gap-y-1 text-sm">
          {matches.map((match, index) => (
            <li key={match.wave.id} className="flex items-center gap-1.5">
              <span aria-hidden className="size-2.5 rounded-sm" style={{ background: SERIES_COLOURS[index] }} />
              {match.wave.code}
            </li>
          ))}
        </ul>
      }
      table={{
        columns: [t.analytics.tier, ...matches.map((match) => match.wave.code)],
        rows: tiers.map((tier) => [
          bandLabel(t, tier),
          ...matches.map((_, index) => {
            const value = cell(index, tier);
            if (value.kind === 'value') return f.number(value.value);
            return value.kind === 'hidden' ? t.display.suppressed : t.analytics.trendsMissing;
          }),
        ]),
      }}
    >
      <ResponsiveContainer width="100%" height={260}>
        <BarChart data={chartData} margin={{ top: 8, right: 4, bottom: 0, left: 4 }} barGap={2} barCategoryGap="28%">
          <CartesianGrid vertical={false} stroke="var(--chart-grid)" />
          <XAxis dataKey="tier" reversed={rtl} tick={AXIS_TICK} tickLine={false} axisLine={{ stroke: 'var(--chart-grid)' }} />
          <YAxis
            orientation={rtl ? 'right' : 'left'}
            allowDecimals={false}
            tick={AXIS_TICK}
            tickLine={false}
            axisLine={false}
            tickFormatter={(value: number) => f.number(value)}
            width={44}
          />
          <Tooltip
            cursor={{ fill: 'var(--accent)' }}
            content={(props) =>
              props.active && props.payload?.length ? (
                <div dir={dir} className="rounded-md border border-border bg-popover px-3 py-2 text-sm shadow-sm">
                  <p className="font-medium">{String(props.label ?? '')}</p>
                  {props.payload.map((entry) => (
                    <p key={String(entry.dataKey)} className="tabular text-muted-foreground">
                      {String(entry.dataKey)}: <span className="text-foreground">{typeof entry.value === 'number' ? f.number(entry.value) : t.common.none}</span>
                    </p>
                  ))}
                </div>
              ) : null
            }
          />
          {matches.map((match, index) => (
            <Bar
              key={match.wave.id}
              dataKey={match.wave.code}
              fill={SERIES_COLOURS[index]}
              radius={[4, 4, 0, 0]}
              maxBarSize={24}
              isAnimationActive={false}
            />
          ))}
        </BarChart>
      </ResponsiveContainer>
    </ChartFigure>
  );
}
