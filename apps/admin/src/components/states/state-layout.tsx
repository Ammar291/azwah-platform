import type { ReactNode } from 'react';

import { cn } from '@/lib/utils';

/** The frame every empty, error and denied state shares: an icon, a heading, a sentence and an optional action. */
export function StateLayout({
  icon,
  title,
  body,
  children,
  role,
  tone = 'neutral',
  headingLevel = 2,
  className,
}: {
  icon: ReactNode;
  title: string;
  body?: ReactNode;
  children?: ReactNode;
  role?: 'alert' | 'status';
  tone?: 'neutral' | 'danger' | 'warning';
  /** 1 when the state is the whole page (it then has no other heading), 2 inside a page. */
  headingLevel?: 1 | 2;
  className?: string;
}) {
  const Heading = headingLevel === 1 ? 'h1' : 'h2';
  return (
    <div
      role={role}
      className={cn(
        'flex flex-col items-start gap-3 rounded-lg border border-border bg-card px-5 py-6 sm:flex-row sm:gap-4',
        className,
      )}
    >
      <span
        aria-hidden
        className={cn(
          'flex size-10 shrink-0 items-center justify-center rounded-full [&_svg]:size-5',
          tone === 'danger' && 'bg-status-danger-soft text-status-danger',
          tone === 'warning' && 'bg-status-warning-soft text-status-warning',
          tone === 'neutral' && 'bg-accent text-primary',
        )}
      >
        {icon}
      </span>
      <div className="flex min-w-0 flex-col gap-1.5">
        <Heading className="text-base font-semibold text-foreground">{title}</Heading>
        {body ? <div className="max-w-prose text-sm text-muted-foreground">{body}</div> : null}
        {children ? <div className="mt-2 flex flex-wrap items-center gap-3">{children}</div> : null}
      </div>
    </div>
  );
}
