'use client';

import { Ban, CircleCheck, CircleDashed, CircleDot, Clock, LoaderCircle, OctagonAlert, TriangleAlert, type LucideIcon } from 'lucide-react';
import type { ReactNode } from 'react';

import { useI18n } from '@/lib/i18n-context';
import type { Messages } from '@/lib/messages';
import { cn } from '@/lib/utils';

type Tone = 'success' | 'info' | 'warning' | 'danger' | 'neutral';
type Kinds = Messages['status'];

/** Every status the dashboard shows, its reserved status tone and its icon. Text always carries the meaning. */
const TONES: { [K in keyof Kinds]: Record<keyof Kinds[K], [Tone, LucideIcon]> } = {
  assignment: {
    assigned: ['neutral', CircleDashed],
    in_progress: ['info', LoaderCircle],
    completed: ['success', CircleCheck],
    expired: ['warning', Clock],
    cancelled: ['neutral', Ban],
  },
  session: {
    in_progress: ['info', LoaderCircle],
    submitted: ['info', CircleDot],
    scored: ['success', CircleCheck],
    abandoned: ['warning', Clock],
    none: ['neutral', CircleDashed],
  },
  export: {
    queued: ['neutral', Clock],
    running: ['info', LoaderCircle],
    completed: ['success', CircleCheck],
    failed: ['danger', TriangleAlert],
    expired: ['neutral', Ban],
  },
  version: { draft: ['warning', CircleDashed], approved: ['success', CircleCheck], retired: ['neutral', Ban] },
  ruleSet: { proposed: ['warning', CircleDashed], approved: ['success', CircleCheck], retired: ['neutral', Ban] },
  household: { active: ['success', CircleCheck], withdrawn: ['neutral', Ban] },
  wave: { planned: ['neutral', CircleDashed], open: ['info', CircleDot], closed: ['neutral', Ban] },
  privacyRequest: { open: ['warning', CircleDot], closed: ['neutral', CircleCheck] },
  safetyAlert: { open: ['danger', TriangleAlert], acknowledged: ['warning', CircleDot], resolved: ['success', CircleCheck] },
  safetySeverity: { critical: ['danger', OctagonAlert], high: ['warning', TriangleAlert] },
  scoreResult: { complete: ['success', CircleCheck], incomplete: ['warning', CircleDashed] },
};

const TONE_CLASSES: Record<Tone, string> = {
  success: 'bg-status-success-soft text-status-success',
  info: 'bg-status-info-soft text-status-info',
  warning: 'bg-status-warning-soft text-status-warning',
  danger: 'bg-status-danger-soft text-status-danger',
  neutral: 'bg-status-neutral-soft text-status-neutral',
};

export type StatusTone = Tone;

/**
 * The pill every status is shown in: a reserved status tone, an icon and the words, which carry the meaning. `lg` is
 * for a status that stands on its own (the connection status), not inside a table row.
 */
export function StatusPill({
  tone,
  icon: Icon,
  size = 'sm',
  role,
  children,
}: {
  tone: StatusTone;
  icon: LucideIcon;
  size?: 'sm' | 'lg';
  role?: 'status';
  children: ReactNode;
}) {
  const large = size === 'lg';
  return (
    <span
      role={role}
      className={cn(
        'inline-flex items-center rounded-full font-medium',
        // A status on its own may be a sentence, so it wraps on a narrow screen; one in a table row stays on one line.
        large ? 'gap-1.5 px-2.5 py-1 text-sm' : 'gap-1 px-2 py-0.5 text-xs whitespace-nowrap',
        TONE_CLASSES[tone],
      )}
    >
      <Icon aria-hidden className={large ? 'size-4 shrink-0' : 'size-3.5'} />
      {children}
    </span>
  );
}

export function StatusBadge<K extends keyof Kinds>({ kind, value }: { kind: K; value: keyof Kinds[K] & string }) {
  const { t } = useI18n();
  const [tone, Icon] = (TONES[kind] as Record<string, [Tone, LucideIcon]>)[value] ?? ['neutral', CircleDashed];
  const label = (t.status[kind] as Record<string, string>)[value] ?? value;
  return (
    <StatusPill tone={tone} icon={Icon}>
      {label}
    </StatusPill>
  );
}
