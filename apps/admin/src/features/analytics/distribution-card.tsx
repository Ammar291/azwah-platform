'use client';

import { EyeOff } from 'lucide-react';
import { useId } from 'react';
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';

import { ChartFigure } from '@/components/data/chart-figure';
import { SuppressedValue } from '@/components/data/suppressed-value';
import type { ScoreDistribution } from '@/lib/api/schemas';
import { bandLabel, orderBands } from '@/lib/bands';
import { displayCount } from '@/lib/display';
import { fill } from '@/lib/format';
import { useI18n } from '@/lib/i18n-context';

const AXIS_TICK = { fill: 'var(--chart-axis)', fontSize: 12 };

/**
 * One approved rule set's bands for one version and module, low to high. Hidden bands are listed, never drawn. The
 * card's heading captions the chart, so the chart draws no second card inside this one.
 */
export function DistributionCard({ distribution, minimumGroupSize }: { distribution: ScoreDistribution; minimumGroupSize: number }) {
  const { t, f, dir } = useI18n();
  const headingId = useId();
  const rtl = dir === 'rtl';
  const title = fill(t.analytics.cardTitle, { instrument: distribution.instrumentCode, version: f.number(distribution.versionNumber) });
  const tiers = orderBands(distribution.tiers, (tier) => tier.tier).map((tier) => ({
    label: bandLabel(t, tier.tier),
    count: displayCount(tier.count, tier.suppressed, minimumGroupSize),
  }));
  const shown = tiers.flatMap((tier) => (tier.count.kind === 'value' ? [{ tier: tier.label, count: tier.count.value }] : []));
  const hidden = tiers.filter((tier) => tier.count.kind === 'suppressed');
  // A distribution covering too few people arrives with no bands at all: there is then nothing to draw or to list.
  const noBands = displayCount(distribution.total, distribution.suppressed, minimumGroupSize).kind === 'suppressed' || tiers.length === 0;

  return (
    <article aria-labelledby={headingId} className="flex flex-col gap-4 rounded-lg border border-border bg-card p-5">
      <header className="flex flex-col gap-1">
        <h3 id={headingId} className="text-base font-semibold">
          {title}
        </h3>
        <p className="text-sm text-muted-foreground">
          {distribution.sectionCode ? fill(t.analytics.module, { module: distribution.sectionCode }) : t.analytics.wholeVersion}
        </p>
        <p className="text-sm text-muted-foreground">
          {fill(t.analytics.ruleSet, { code: distribution.ruleSetCode, version: f.number(distribution.ruleSetVersion) })}
        </p>
      </header>
      <div className="flex flex-col gap-1">
        <p className="text-sm text-muted-foreground">{t.analytics.people}</p>
        <p className="text-2xl font-semibold">
          <SuppressedValue value={distribution.total} suppressed={distribution.suppressed} minimumGroupSize={minimumGroupSize} />
        </p>
        <p className="text-xs text-muted-foreground">{t.analytics.peopleNote}</p>
      </div>
      {noBands ? (
        <p className="flex items-start gap-2 text-sm text-muted-foreground">
          <EyeOff aria-hidden className="mt-0.5 size-4 shrink-0" />
          {fill(t.analytics.distributionSuppressed, { n: minimumGroupSize })}
        </p>
      ) : (
        <ChartFigure
          title={title}
          captionId={headingId}
          summary={fill(t.analytics.tierChartSummary, { instrument: distribution.instrumentCode, version: f.number(distribution.versionNumber) })}
          description={t.analytics.tierCodesNote}
          table={{
            columns: [t.analytics.tier, t.analytics.count],
            rows: tiers.map((tier) => [tier.label, tier.count.kind === 'value' ? f.number(tier.count.value) : t.display.suppressed]),
          }}
          note={
            hidden.length > 0 ? (
              <p className="flex items-start gap-2 text-sm text-muted-foreground">
                <EyeOff aria-hidden className="mt-0.5 size-4 shrink-0" />
                {fill(t.analytics.hiddenTiers, { bands: hidden.map((tier) => tier.label).join(t.common.listSeparator) })}
              </p>
            ) : null
          }
        >
          {shown.length > 0 ? (
            <ResponsiveContainer width="100%" height={Math.max(120, shown.length * 44 + 32)}>
              <BarChart layout="vertical" data={shown} margin={{ top: 4, right: 16, bottom: 4, left: 16 }}>
                <CartesianGrid horizontal={false} stroke="var(--chart-grid)" />
                <XAxis
                  type="number"
                  allowDecimals={false}
                  reversed={rtl}
                  tick={AXIS_TICK}
                  tickLine={false}
                  axisLine={false}
                  tickFormatter={(value: number) => f.number(value)}
                />
                <YAxis
                  type="category"
                  dataKey="tier"
                  orientation={rtl ? 'right' : 'left'}
                  width={112}
                  tick={AXIS_TICK}
                  tickLine={false}
                  axisLine={{ stroke: 'var(--chart-grid)' }}
                />
                <Tooltip
                  cursor={{ fill: 'var(--accent)' }}
                  content={(props) =>
                    props.active && typeof props.payload?.[0]?.value === 'number' ? (
                      <div dir={dir} className="rounded-md border border-border bg-popover px-3 py-2 text-sm shadow-sm">
                        <p className="font-medium">{String(props.label ?? '')}</p>
                        <p className="tabular text-muted-foreground">
                          {t.analytics.count}: <span className="text-foreground">{f.number(props.payload[0].value)}</span>
                        </p>
                      </div>
                    ) : null
                  }
                />
                <Bar dataKey="count" fill="var(--chart-1)" radius={rtl ? [4, 0, 0, 4] : [0, 4, 4, 0]} maxBarSize={24} isAnimationActive={false} />
              </BarChart>
            </ResponsiveContainer>
          ) : null}
        </ChartFigure>
      )}
    </article>
  );
}
