'use client';

import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';

import { ChartFigure } from '@/components/data/chart-figure';
import { EmptyState } from '@/components/states/empty-state';
import type { Overview } from '@/lib/api/schemas';
import { fill } from '@/lib/format';
import { useI18n } from '@/lib/i18n-context';

const AXIS_TICK = { fill: 'var(--chart-axis)', fontSize: 12 };

/**
 * Completed assessments per period, one series in the brand chart colour: thin columns with rounded tops, a hairline
 * grid, a hover tooltip, and the same points as a table.
 */
export function CompletionTrend({ overview }: { overview: Overview }) {
  const { t, f, dir } = useI18n();
  const interval = overview.interval;
  const points = overview.completionTrend.map((point) => ({
    label: interval === 'month' ? f.month(point.periodStart) : f.day(point.periodStart),
    completed: point.completed,
  }));
  if (points.length === 0) {
    return <EmptyState title={t.overview.trendEmptyTitle} description={t.overview.trendEmptyBody} />;
  }
  const title = t.overview.trendTitle[interval];
  return (
    <ChartFigure
      title={title}
      summary={fill(t.overview.trendSummary[interval], { from: f.day(overview.from), to: f.day(overview.to) })}
      table={{
        columns: [t.overview.period, t.overview.completedColumn],
        rows: points.map((point) => [point.label, f.number(point.completed)]),
      }}
    >
      <ResponsiveContainer width="100%" height={260}>
        <BarChart data={points} margin={{ top: 8, right: 4, bottom: 0, left: 4 }} barCategoryGap="30%">
          <CartesianGrid vertical={false} stroke="var(--chart-grid)" />
          <XAxis
            dataKey="label"
            reversed={dir === 'rtl'}
            tickLine={false}
            axisLine={{ stroke: 'var(--chart-grid)' }}
            tick={AXIS_TICK}
            interval="preserveStartEnd"
            minTickGap={12}
          />
          <YAxis
            orientation={dir === 'rtl' ? 'right' : 'left'}
            allowDecimals={false}
            tickLine={false}
            axisLine={false}
            tick={AXIS_TICK}
            tickFormatter={(value: number) => f.number(value)}
            width={44}
          />
          <Tooltip
            cursor={{ fill: 'var(--accent)' }}
            content={(props) => <TrendTooltip active={props.active} value={props.payload?.[0]?.value} label={String(props.label ?? '')} />}
          />
          <Bar dataKey="completed" fill="var(--chart-1)" radius={[4, 4, 0, 0]} maxBarSize={24} isAnimationActive={false} />
        </BarChart>
      </ResponsiveContainer>
    </ChartFigure>
  );
}

function TrendTooltip({ active, value, label }: { active?: boolean; value: unknown; label: string }) {
  const { t, f, dir } = useI18n();
  if (!active || typeof value !== 'number') return null;
  // The chart is drawn left to right in both languages (see ChartFigure); the tooltip's text follows the page again.
  return (
    <div dir={dir} className="rounded-md border border-border bg-popover px-3 py-2 text-sm shadow-sm">
      <p className="font-medium">{label}</p>
      <p className="tabular text-muted-foreground">
        {t.overview.completedColumn}: <span className="text-foreground">{f.number(value)}</span>
      </p>
    </div>
  );
}
