'use client';

import { ChevronRight } from 'lucide-react';
import { useId } from 'react';

import { FigureStat } from '@/components/data/figure-stat';
import type { Overview } from '@/lib/api/schemas';
import { useI18n } from '@/lib/i18n-context';

/**
 * Assessments as the sequence they move through (assigned, started, completed), then the rate and the other states.
 * Every number is the platform's; the arrows only show the order of the stages.
 */
export function AssignmentFlow({ assignments }: { assignments: Overview['assignments'] }) {
  const { t, f } = useI18n();
  const headingId = useId();
  const stages = [
    { label: t.overview.assigned, value: assignments.total },
    { label: t.overview.started, value: assignments.started },
    { label: t.overview.completed, value: assignments.completed },
  ];
  const others = [
    { label: t.overview.notStarted, value: assignments.assigned },
    { label: t.overview.inProgress, value: assignments.inProgress },
    { label: t.overview.expired, value: assignments.expired },
    { label: t.overview.cancelled, value: assignments.cancelled },
  ];
  return (
    <section aria-labelledby={headingId} className="flex flex-col gap-5 rounded-lg border-t-4 border-primary bg-card px-5 py-5 shadow-[0_0_0_1px_var(--border)]">
      <h2 id={headingId} className="text-base font-semibold">
        {t.overview.assessments}
      </h2>
      <div className="flex flex-col gap-6 lg:flex-row lg:items-end lg:justify-between">
        <ol className="flex flex-wrap items-end gap-x-4 gap-y-4">
          {stages.map((stage, index) => (
            <li key={stage.label} className="flex items-end gap-4">
              {index > 0 ? <ChevronRight aria-hidden className="mb-2 size-5 text-muted-foreground rtl:rotate-180" /> : null}
              <FigureStat label={stage.label} value={f.number(stage.value)} />
            </li>
          ))}
        </ol>
        <FigureStat
          className="lg:max-w-60 lg:text-end"
          emphasis="primary"
          label={t.overview.completionRate}
          value={f.percent(assignments.completionRate)}
          detail={t.overview.completionRateNote}
        />
      </div>
      <ul className="grid grid-cols-2 gap-4 border-t border-border pt-4 sm:grid-cols-4">
        {others.map((other) => (
          <li key={other.label}>
            <FigureStat emphasis="quiet" label={other.label} value={f.number(other.value)} />
          </li>
        ))}
      </ul>
    </section>
  );
}
